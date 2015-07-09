#if C_LOSSLESS_SUPPORTED
// jcdiffct.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1994-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains the difference buffer controller for compression.
// This controller is the top level of the lossless JPEG compressor proper.
// The difference buffer lies between prediction/differencing and entropy
// encoding.

// We use a full-image sample buffer when doing Huffman optimization,
// and also for writing multiple-scan JPEG files. In all cases, the
// full-image buffer is filled during the first pass, and the scaling,
// prediction and differencing steps are run during subsequent passes.
#if ENTROPY_OPT_SUPPORTED
	#define FULL_SAMP_BUFFER_SUPPORTED
#else
	#if C_MULTISCAN_FILES_SUPPORTED
		#define FULL_SAMP_BUFFER_SUPPORTED
	#endif
#endif

using System;

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Private buffer controller object
		class c_diff_controller
		{
			public uint iMCU_row_num;			// iMCU row # within image
			public uint mcu_ctr;				// counts MCUs processed in current row
			public int MCU_vert_offset;			// counts MCU rows within iMCU row
			public int MCU_rows_per_iMCU_row;	// number of such rows needed

			public byte[][] cur_row=new byte[MAX_COMPONENTS][];		// row of point transformed samples
			public byte[][] prev_row=new byte[MAX_COMPONENTS][];	// previous row of Pt'd samples
			public int[][][] diff_buf=new int[MAX_COMPONENTS][][];	// iMCU row of differences

			// In multi-pass modes, we need a sample array for each component.
			public byte[][][] whole_image=new byte[MAX_COMPONENTS][][];
		}

		// Reset within-iMCU-row counters for a new row
		static void start_iMCU_row_c_diff(jpeg_compress cinfo)
		{
			jpeg_lossless_c_codec losslsc=(jpeg_lossless_c_codec)cinfo.coef;
			c_diff_controller diff=(c_diff_controller)losslsc.diff_private;

			// In an interleaved scan, an MCU row is the same as an iMCU row.
			// In a noninterleaved scan, an iMCU row has v_samp_factor MCU rows.
			// But at the bottom of the image, process only what's left.
			if(cinfo.comps_in_scan>1)
			{
				diff.MCU_rows_per_iMCU_row=1;
			}
			else
			{
				if(diff.iMCU_row_num<(cinfo.total_iMCU_rows-1))
					diff.MCU_rows_per_iMCU_row=cinfo.cur_comp_info[0].v_samp_factor;
				else
					diff.MCU_rows_per_iMCU_row=cinfo.cur_comp_info[0].last_row_height;
			}

			diff.mcu_ctr=0;
			diff.MCU_vert_offset=0;
		}

		// Initialize for a processing pass.
		static void start_pass_diff(jpeg_compress cinfo, J_BUF_MODE pass_mode)
		{
			jpeg_lossless_c_codec losslsc=(jpeg_lossless_c_codec)cinfo.coef;
			c_diff_controller diff=(c_diff_controller)losslsc.diff_private;

			diff.iMCU_row_num=0;
			start_iMCU_row_c_diff(cinfo);

			switch(pass_mode)
			{
				case J_BUF_MODE.JBUF_PASS_THRU:
					if(diff.whole_image[0]!=null)
						ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_BUFFER_MODE);
					losslsc.compress_data=compress_data_diff;
					break;
#if FULL_SAMP_BUFFER_SUPPORTED
				case J_BUF_MODE.JBUF_SAVE_AND_PASS:
					if(diff.whole_image[0]==null)
						ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_BUFFER_MODE);
					losslsc.compress_data=compress_first_pass_diff;
					break;
				case J_BUF_MODE.JBUF_CRANK_DEST:
					if(diff.whole_image[0]==null)
						ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_BUFFER_MODE);
					losslsc.compress_data=compress_output_diff;
					break;
#endif
				default:
					ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_BUFFER_MODE);
					break;
			}
		}

		// Process some data in the single-pass case.
		// We process the equivalent of one fully interleaved MCU row ("iMCU" row)
		// per call, ie, v_samp_factor rows for each component in the image.
		// Returns true if the iMCU row is completed, false if suspended.
		//
		// NB: input_buf contains a plane for each component in image,
		// which we index according to the component's SOF position.
		static bool compress_data_diff(jpeg_compress cinfo, byte[][][] input_buf)
		{
			jpeg_lossless_c_codec losslsc=(jpeg_lossless_c_codec)cinfo.coef;
			c_diff_controller diff=(c_diff_controller)losslsc.diff_private;

			uint last_MCU_col=cinfo.MCUs_per_row-1;
			uint last_iMCU_row=cinfo.total_iMCU_rows-1;

			// Loop to write as much as one whole iMCU row
			for(int yoffset=diff.MCU_vert_offset; yoffset<diff.MCU_rows_per_iMCU_row; yoffset++)
			{
				uint MCU_col_num=diff.mcu_ctr; // index of current MCU within row

				// Scale and predict each scanline of the MCU-row separately.
				//
				// Note: We only do this if we are at the start of a MCU-row, ie,
				// we don't want to reprocess a row suspended by the output.
				if(MCU_col_num==0)
				{
					for(int comp=0; comp<cinfo.comps_in_scan; comp++)
					{
						jpeg_component_info compptr=cinfo.cur_comp_info[comp];
						int ci=compptr.component_index;
						int samp_rows;
						if(diff.iMCU_row_num<last_iMCU_row) samp_rows=compptr.v_samp_factor;
						else
						{
							// NB: can't use last_row_height here, since may not be set!
							samp_rows=(int)(compptr.height_in_blocks%compptr.v_samp_factor);
							if(samp_rows==0) samp_rows=compptr.v_samp_factor;
							else
							{
								// Fill dummy difference rows at the bottom edge with zeros, which
								// will encode to the smallest amount of data.
								for(int samp_row=samp_rows; samp_row<compptr.v_samp_factor; samp_row++)
								{
									int c=jround_up((int)compptr.width_in_blocks, (int)compptr.h_samp_factor);
									for(int i=0; i<c; i++) diff.diff_buf[ci][samp_row][i]=0;
								}
							}
						}

						uint samps_across=compptr.width_in_blocks;
						for(int samp_row=0; samp_row<samp_rows; samp_row++)
						{
							losslsc.scaler_scale(cinfo, input_buf[ci][samp_row], diff.cur_row[ci], samps_across);
							losslsc.predict_difference[ci](cinfo, ci, diff.cur_row[ci], diff.prev_row[ci], diff.diff_buf[ci][samp_row], samps_across);

							byte[] temp=diff.cur_row[ci];
							diff.cur_row[ci]=diff.prev_row[ci];
							diff.prev_row[ci]=temp;
						}
					}
				}

				// Try to write the MCU-row (or remaining portion of suspended MCU-row).
				uint MCU_count=losslsc.entropy_encode_mcus(cinfo, diff.diff_buf, (uint)yoffset, MCU_col_num, cinfo.MCUs_per_row-MCU_col_num);
				if(MCU_count!=cinfo.MCUs_per_row-MCU_col_num)
				{
					// Suspension forced; update state counters and exit
					diff.MCU_vert_offset=yoffset;
					diff.mcu_ctr+=MCU_col_num;
					return false;
				}

				// Completed an MCU row, but perhaps not an iMCU row
				diff.mcu_ctr=0;
			}

			// Completed the iMCU row, advance counters for next one
			diff.iMCU_row_num++;
			start_iMCU_row_c_diff(cinfo);
			return true;
		}

#if FULL_SAMP_BUFFER_SUPPORTED
		// Special version of compress_data_diff with input_buf offsets.
		static bool compress_data_diff(jpeg_compress cinfo, byte[][][] input_buf, int[] input_buf_ind)
		{
			jpeg_lossless_c_codec losslsc=(jpeg_lossless_c_codec)cinfo.coef;
			c_diff_controller diff=(c_diff_controller)losslsc.diff_private;

			uint last_MCU_col=cinfo.MCUs_per_row-1;
			uint last_iMCU_row=cinfo.total_iMCU_rows-1;

			// Loop to write as much as one whole iMCU row
			for(int yoffset=diff.MCU_vert_offset; yoffset<diff.MCU_rows_per_iMCU_row; yoffset++)
			{
				uint MCU_col_num=diff.mcu_ctr; // index of current MCU within row

				// Scale and predict each scanline of the MCU-row separately.
				//
				// Note: We only do this if we are at the start of a MCU-row, ie,
				// we don't want to reprocess a row suspended by the output.
				if(MCU_col_num==0)
				{
					for(int comp=0; comp<cinfo.comps_in_scan; comp++)
					{
						jpeg_component_info compptr=cinfo.cur_comp_info[comp];
						int ci=compptr.component_index;
						int samp_rows;
						if(diff.iMCU_row_num<last_iMCU_row) samp_rows=compptr.v_samp_factor;
						else
						{
							// NB: can't use last_row_height here, since may not be set!
							samp_rows=(int)(compptr.height_in_blocks%compptr.v_samp_factor);
							if(samp_rows==0) samp_rows=compptr.v_samp_factor;
							else
							{
								// Fill dummy difference rows at the bottom edge with zeros, which
								// will encode to the smallest amount of data.
								for(int samp_row=samp_rows; samp_row<compptr.v_samp_factor; samp_row++)
								{
									int c=jround_up((int)compptr.width_in_blocks, (int)compptr.h_samp_factor);
									for(int i=0; i<c; i++) diff.diff_buf[ci][samp_row][i]=0;
								}
							}
						}

						uint samps_across=compptr.width_in_blocks;
						for(int samp_row=0; samp_row<samp_rows; samp_row++)
						{
							losslsc.scaler_scale(cinfo, input_buf[ci][input_buf_ind[ci]+samp_row], diff.cur_row[ci], samps_across);
							losslsc.predict_difference[ci](cinfo, ci, diff.cur_row[ci], diff.prev_row[ci], diff.diff_buf[ci][samp_row], samps_across);

							byte[] temp=diff.cur_row[ci];
							diff.cur_row[ci]=diff.prev_row[ci];
							diff.prev_row[ci]=temp;
						}
					}
				}

				// Try to write the MCU-row (or remaining portion of suspended MCU-row).
				uint MCU_count=losslsc.entropy_encode_mcus(cinfo, diff.diff_buf, (uint)yoffset, MCU_col_num, cinfo.MCUs_per_row-MCU_col_num);
				if(MCU_count!=cinfo.MCUs_per_row-MCU_col_num)
				{
					// Suspension forced; update state counters and exit
					diff.MCU_vert_offset=yoffset;
					diff.mcu_ctr+=MCU_col_num;
					return false;
				}

				// Completed an MCU row, but perhaps not an iMCU row
				diff.mcu_ctr=0;
			}

			// Completed the iMCU row, advance counters for next one
			diff.iMCU_row_num++;
			start_iMCU_row_c_diff(cinfo);
			return true;
		}

		// Process some data in the first pass of a multi-pass case.
		// We process the equivalent of one fully interleaved MCU row ("iMCU" row)
		// per call, ie, v_samp_factor rows for each component in the image.
		// This amount of data is read from the source buffer and saved into the arrays.
		//
		// We must also emit the data to the compressor. This is conveniently
		// done by calling compress_output_diff() after we've loaded the current strip
		// of the arrays.
		//
		// NB: input_buf contains a plane for each component in image. All components
		// are loaded into the arrays in this pass. However, it may be that
		// only a subset of the components are emitted to the compressor during
		// this first pass; be careful about looking at the scan-dependent variables
		// (MCU dimensions, etc).
		static bool compress_first_pass_diff(jpeg_compress cinfo, byte[][][] input_buf)
		{
			jpeg_lossless_c_codec losslsc=(jpeg_lossless_c_codec)cinfo.coef;
			c_diff_controller diff=(c_diff_controller)losslsc.diff_private;

			uint last_iMCU_row=cinfo.total_iMCU_rows-1;

			for(int ci=0; ci<cinfo.num_components; ci++)
			{
				jpeg_component_info compptr=cinfo.comp_info[ci];

				// Count non-dummy sample rows in this iMCU row.
				int samp_rows;
				if(diff.iMCU_row_num<last_iMCU_row) samp_rows=compptr.v_samp_factor;
				else
				{
					// NB: can't use last_row_height here, since may not be set!
					samp_rows=(int)(compptr.height_in_blocks%compptr.v_samp_factor);
					if(samp_rows==0) samp_rows=compptr.v_samp_factor;
				}

				uint samps_across=compptr.width_in_blocks;

				// Perform point transform scaling and prediction/differencing for all
				// non-dummy rows in this iMCU row. Each call on these functions
				// process a complete row of samples.
				for(int samp_row=0; samp_row<samp_rows; samp_row++)
				{
					Array.Copy(input_buf[ci][samp_row], diff.whole_image[ci][samp_row+diff.iMCU_row_num*compptr.v_samp_factor], samps_across);
				}
			}

			// NB: compress_output will increment iMCU_row_num if successful.
			// A suspension return will result in redoing all the work above next time.

			// Emit data to the compressor, sharing code with subsequent passes
			return compress_output_diff(cinfo, input_buf);
		}

		// Process some data in subsequent passes of a multi-pass case.
		// We process the equivalent of one fully interleaved MCU row ("iMCU" row)
		// per call, ie, v_samp_factor rows for each component in the scan.
		// The data is obtained from the arrays and fed to the compressor.
		// Returns true if the iMCU row is completed, false if suspended.
		//
		// NB: input_buf is ignored; it is likely to be a null pointer.
		static bool compress_output_diff(jpeg_compress cinfo, byte[][][] input_buf)
		{
			jpeg_lossless_c_codec losslsc=(jpeg_lossless_c_codec)cinfo.coef;
			c_diff_controller diff=(c_diff_controller)losslsc.diff_private;
			byte[][][] buffer=new byte[MAX_COMPONENTS][][];
			int[] buffer_ind=new int[MAX_COMPONENTS];

			// Align the buffers for the components used in this scan.
			// NB: during first pass, this is safe only because the buffers will
			// already be aligned properly, so jmemmgr.cs won't need to do any I/O.
			for(int comp=0; comp<cinfo.comps_in_scan; comp++)
			{
				jpeg_component_info compptr=cinfo.cur_comp_info[comp];
				int ci=compptr.component_index;
				buffer[ci]=diff.whole_image[ci];
				buffer_ind[ci]=(int)diff.iMCU_row_num*compptr.v_samp_factor;
			}

			return compress_data_diff(cinfo, buffer, buffer_ind);
		}
#endif // FULL_SAMP_BUFFER_SUPPORTED

		// Initialize difference buffer controller.
		static void jinit_c_diff_controller(jpeg_compress cinfo, bool need_full_buffer)
		{
			jpeg_lossless_c_codec losslsc=(jpeg_lossless_c_codec)cinfo.coef;
			c_diff_controller diff=null;

			try
			{
				diff=new c_diff_controller();
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}
			losslsc.diff_private=diff;
			losslsc.diff_start_pass=start_pass_diff;

			// Create the prediction row buffers.
			for(int ci=0; ci<cinfo.num_components; ci++)
			{
				jpeg_component_info compptr=cinfo.comp_info[ci];
				try
				{
					diff.cur_row[ci]=new byte[jround_up(compptr.width_in_blocks, compptr.h_samp_factor)];
					diff.prev_row[ci]=new byte[jround_up(compptr.width_in_blocks, compptr.h_samp_factor)];
				}
				catch
				{
					ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
				}
			}

			// Create the difference buffer.
			for(int ci=0; ci<cinfo.num_components; ci++)
			{
				jpeg_component_info compptr=cinfo.comp_info[ci];
				diff.diff_buf[ci]=alloc_darray(cinfo, (uint)jround_up(compptr.width_in_blocks, compptr.h_samp_factor), (uint)compptr.v_samp_factor);

				// Prefill difference rows with zeros. We do this because only actual
				// data is placed in the buffers during prediction/differencing, leaving
				// any dummy differences at the right edge as zeros, which will encode
				// to the smallest amount of data.
				for(int row=0; row<compptr.v_samp_factor; row++)
				{
					int c=(int)jround_up(compptr.width_in_blocks, compptr.h_samp_factor);
					for(int i=0; i<c; i++) diff.diff_buf[ci][row][i]=0;
				}
			}

			// Create the sample buffer.
			if(need_full_buffer)
			{
#if FULL_SAMP_BUFFER_SUPPORTED
				// Allocate a full-image array for each component,
				// padded to a multiple of samp_factor differences in each direction.
				for(int ci=0; ci<cinfo.num_components; ci++)
				{
					jpeg_component_info compptr=cinfo.comp_info[ci];
					diff.whole_image[ci]=alloc_sarray(cinfo, (uint)jround_up(compptr.width_in_blocks, compptr.h_samp_factor), (uint)jround_up(compptr.height_in_blocks, compptr.v_samp_factor));
				}
#else
				ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_BUFFER_MODE);
#endif
			}
			else diff.whole_image[0]=null; // flag for no arrays
		}
	}
}
#endif // C_LOSSLESS_SUPPORTED