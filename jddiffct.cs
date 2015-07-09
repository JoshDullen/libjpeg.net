#if D_LOSSLESS_SUPPORTED
// jddiffct.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1994-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains the [un]difference buffer controller for decompression.
// This controller is the top level of the lossless JPEG decompressor proper.
// The difference buffer lies between the entropy decoding and
// prediction/undifferencing steps. The undifference buffer lies between the
// prediction/undifferencing and scaling steps.
//
// In buffered-image mode, this controller is the interface between
// input-oriented processing and output-oriented processing.

using System;

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Private buffer controller object
		class d_diff_controller
		{
			// These variables keep track of the current location of the input side.
			// cinfo.input_iMCU_row is also used for this.
			public uint MCU_ctr;				// counts MCUs processed in current row
			public uint restart_rows_to_go;		// MCU-rows left in this restart interval
			public uint MCU_vert_offset;		// counts MCU rows within iMCU row
			public uint MCU_rows_per_iMCU_row;	// number of such rows needed

			// The output side's location is represented by cinfo.output_iMCU_row.
			public int[][][] diff_buf=new int[MAX_COMPONENTS][][];		// iMCU row of differences
			public int[][][] undiff_buf=new int[MAX_COMPONENTS][][];	// iMCU row of undiff'd samples

#if D_MULTISCAN_FILES_SUPPORTED
			// In multi-pass modes, we need a sample array for each component.
			public byte[][][] whole_image=new byte[MAX_COMPONENTS][][];
#endif
		}

		// Reset within-iMCU-row counters for a new row (input side)
		static void start_iMCU_row_d_diff(jpeg_decompress cinfo)
		{
			jpeg_lossless_d_codec losslsd=(jpeg_lossless_d_codec)cinfo.coef;
			d_diff_controller diff=(d_diff_controller)losslsd.diff_private;

			// In an interleaved scan, an MCU row is the same as an iMCU row.
			// In a noninterleaved scan, an iMCU row has v_samp_factor MCU rows.
			// But at the bottom of the image, process only what's left.
			if(cinfo.comps_in_scan>1) diff.MCU_rows_per_iMCU_row=1;
			else
			{
				if(cinfo.input_iMCU_row<(cinfo.total_iMCU_rows-1)) diff.MCU_rows_per_iMCU_row=(uint)cinfo.cur_comp_info[0].v_samp_factor;
				else diff.MCU_rows_per_iMCU_row=(uint)cinfo.cur_comp_info[0].last_row_height;
			}

			diff.MCU_ctr=0;
			diff.MCU_vert_offset=0;
		}

		// Initialize for an input processing pass.
		static void start_input_pass_d_diff(jpeg_decompress cinfo)
		{
			jpeg_lossless_d_codec losslsd=(jpeg_lossless_d_codec)cinfo.coef;
			d_diff_controller diff=(d_diff_controller)losslsd.diff_private;

			// Check that the restart interval is an integer multiple of the number 
			// of MCU in an MCU-row.
			if(cinfo.restart_interval%cinfo.MCUs_per_row!=0) ERREXIT2(cinfo, J_MESSAGE_CODE.JERR_BAD_RESTART, (int)cinfo.restart_interval, (int)cinfo.MCUs_per_row);

			// Initialize restart counter
			diff.restart_rows_to_go=cinfo.restart_interval/cinfo.MCUs_per_row;

			cinfo.input_iMCU_row=0;
			start_iMCU_row_d_diff(cinfo);
		}

		// Check for a restart marker & resynchronize decoder, undifferencer.
		// Returns false if must suspend.
		static bool process_restart_d_diff(jpeg_decompress cinfo)
		{
			jpeg_lossless_d_codec losslsd=(jpeg_lossless_d_codec)cinfo.coef;
			d_diff_controller diff=(d_diff_controller)losslsd.diff_private;

			if(!losslsd.entropy_process_restart(cinfo)) return false;

			losslsd.predict_process_restart(cinfo);

			// Reset restart counter
			diff.restart_rows_to_go=cinfo.restart_interval/cinfo.MCUs_per_row;

			return true;
		}

		// Initialize for an output processing pass.
		static void start_output_pass_d_diff(jpeg_decompress cinfo)
		{
			cinfo.output_iMCU_row=0;
		}

		// Decompress and return some data in the supplied buffer.
		// Always attempts to emit one fully interleaved MCU row ("iMCU" row).
		// Input and output must run in lockstep since we have only a one-MCU buffer.
		// Return value is JPEG_ROW_COMPLETED, JPEG_SCAN_COMPLETED, or JPEG_SUSPENDED.
		//
		// NB: output_buf contains a plane for each component in image,
		// which we index according to the component's SOF position.
		static CONSUME_INPUT decompress_data_d_diff(jpeg_decompress cinfo, byte[][][] output_buf)
		{
			jpeg_lossless_d_codec losslsd=(jpeg_lossless_d_codec)cinfo.coef;
			d_diff_controller diff=(d_diff_controller)losslsd.diff_private;

			// Loop to process as much as one whole iMCU row
			for(uint yoffset=diff.MCU_vert_offset; yoffset<diff.MCU_rows_per_iMCU_row; yoffset++)
			{
				// Process restart marker if needed; may have to suspend
				if(cinfo.restart_interval!=0)
				{
					if(diff.restart_rows_to_go==0)
					{
						if(!process_restart_d_diff(cinfo)) return CONSUME_INPUT.JPEG_SUSPENDED;
					}
				}

				uint MCU_col_num=diff.MCU_ctr; // index of current MCU within row

				// Try to fetch an MCU-row (or remaining portion of suspended MCU-row).
				uint MCU_count=losslsd.entropy_decode_mcus(cinfo, diff.diff_buf, yoffset, MCU_col_num, cinfo.MCUs_per_row-MCU_col_num);
				if(MCU_count!=cinfo.MCUs_per_row-MCU_col_num)
				{
					// Suspension forced; update state counters and exit
					diff.MCU_vert_offset=yoffset;
					diff.MCU_ctr+=MCU_count;
					return CONSUME_INPUT.JPEG_SUSPENDED;
				}

				// Account for restart interval (no-op if not using restarts)
				diff.restart_rows_to_go--;

				// Completed an MCU row, but perhaps not an iMCU row
				diff.MCU_ctr=0;
			}

			uint last_iMCU_row=cinfo.total_iMCU_rows-1;

			// Undifference and scale each scanline of the disassembled MCU-row
			// separately. We do not process dummy samples at the end of a scanline
			// or dummy rows at the end of the image.
			for(int comp=0; comp<cinfo.comps_in_scan; comp++)
			{
				jpeg_component_info compptr=cinfo.cur_comp_info[comp];
				int ci=compptr.component_index;
				int stop=cinfo.input_iMCU_row==last_iMCU_row?compptr.last_row_height:compptr.v_samp_factor;
				for(int row=0, prev_row=compptr.v_samp_factor-1; row<stop; prev_row=row, row++)
				{
					losslsd.predict_undifference[ci](cinfo, ci, diff.diff_buf[ci][row], diff.undiff_buf[ci][prev_row], diff.undiff_buf[ci][row], compptr.width_in_blocks);
					losslsd.scaler_scale(cinfo, diff.undiff_buf[ci][row], output_buf[ci][row], compptr.width_in_blocks);
				}
			}

			// Completed the iMCU row, advance counters for next one.
			//
			// NB: output_data will increment output_iMCU_row.
			// This counter is not needed for the single-pass case
			// or the input side of the multi-pass case.
			if(++(cinfo.input_iMCU_row)<cinfo.total_iMCU_rows)
			{
				start_iMCU_row_d_diff(cinfo);
				return CONSUME_INPUT.JPEG_ROW_COMPLETED;
			}

			// Completed the scan
			cinfo.inputctl.finish_input_pass(cinfo);
			return CONSUME_INPUT.JPEG_SCAN_COMPLETED;
		}

		// Dummy consume-input routine for single-pass operation.
		static CONSUME_INPUT dummy_consume_data_d_diff(jpeg_decompress cinfo)
		{
			return CONSUME_INPUT.JPEG_SUSPENDED;	// Always indicate nothing was done
		}

#if D_MULTISCAN_FILES_SUPPORTED
		static CONSUME_INPUT decompress_data_d_diff(jpeg_decompress cinfo, byte[][][] output_buf, int[] output_buf_ind)
		{
			jpeg_lossless_d_codec losslsd=(jpeg_lossless_d_codec)cinfo.coef;
			d_diff_controller diff=(d_diff_controller)losslsd.diff_private;

			// Loop to process as much as one whole iMCU row
			for(uint yoffset=diff.MCU_vert_offset; yoffset<diff.MCU_rows_per_iMCU_row; yoffset++)
			{
				// Process restart marker if needed; may have to suspend
				if(cinfo.restart_interval!=0)
				{
					if(diff.restart_rows_to_go==0)
					{
						if(!process_restart_d_diff(cinfo)) return CONSUME_INPUT.JPEG_SUSPENDED;
					}
				}

				uint MCU_col_num=diff.MCU_ctr; // index of current MCU within row

				// Try to fetch an MCU-row (or remaining portion of suspended MCU-row).
				uint MCU_count=losslsd.entropy_decode_mcus(cinfo, diff.diff_buf, yoffset, MCU_col_num, cinfo.MCUs_per_row-MCU_col_num);
				if(MCU_count!=cinfo.MCUs_per_row-MCU_col_num)
				{
					// Suspension forced; update state counters and exit
					diff.MCU_vert_offset=yoffset;
					diff.MCU_ctr+=MCU_count;
					return CONSUME_INPUT.JPEG_SUSPENDED;
				}

				// Account for restart interval (no-op if not using restarts)
				diff.restart_rows_to_go--;

				// Completed an MCU row, but perhaps not an iMCU row
				diff.MCU_ctr=0;
			}

			uint last_iMCU_row=cinfo.total_iMCU_rows-1;

			// Undifference and scale each scanline of the disassembled MCU-row
			// separately. We do not process dummy samples at the end of a scanline
			// or dummy rows at the end of the image.
			for(int comp=0; comp<cinfo.comps_in_scan; comp++)
			{
				jpeg_component_info compptr=cinfo.cur_comp_info[comp];
				int ci=compptr.component_index;
				int stop=cinfo.input_iMCU_row==last_iMCU_row?compptr.last_row_height:compptr.v_samp_factor;
				for(int row=0, prev_row=compptr.v_samp_factor-1; row<stop; prev_row=row, row++)
				{
					losslsd.predict_undifference[ci](cinfo, ci, diff.diff_buf[ci][row], diff.undiff_buf[ci][prev_row], diff.undiff_buf[ci][row], compptr.width_in_blocks);
					losslsd.scaler_scale(cinfo, diff.undiff_buf[ci][row], output_buf[ci][output_buf_ind[ci]+row], compptr.width_in_blocks);
				}
			}

			// Completed the iMCU row, advance counters for next one.
			//
			// NB: output_data will increment output_iMCU_row.
			// This counter is not needed for the single-pass case
			// or the input side of the multi-pass case.
			if(++(cinfo.input_iMCU_row)<cinfo.total_iMCU_rows)
			{
				start_iMCU_row_d_diff(cinfo);
				return CONSUME_INPUT.JPEG_ROW_COMPLETED;
			}

			// Completed the scan
			cinfo.inputctl.finish_input_pass(cinfo);
			return CONSUME_INPUT.JPEG_SCAN_COMPLETED;
		}

		// Consume input data and store it in the full-image sample buffer.
		// We read as much as one fully interleaved MCU row ("iMCU" row) per call,
		// ie, v_samp_factor rows for each component in the scan.
		// Return value is JPEG_ROW_COMPLETED, JPEG_SCAN_COMPLETED, or JPEG_SUSPENDED.
		static CONSUME_INPUT consume_data_d_diff(jpeg_decompress cinfo)
		{
			jpeg_lossless_d_codec losslsd=(jpeg_lossless_d_codec)cinfo.coef;
			d_diff_controller diff=(d_diff_controller)losslsd.diff_private;
			uint last_iMCU_row=cinfo.total_iMCU_rows-1;
			byte[][][] buffer=new byte[MAX_COMPS_IN_SCAN][][];
			int[] buffer_ind=new int[MAX_COMPS_IN_SCAN];

			// Align the buffers for the components used in this scan.
			for(int comp=0; comp<cinfo.comps_in_scan; comp++)
			{
				jpeg_component_info compptr=cinfo.cur_comp_info[comp];
				int ci=compptr.component_index;
				buffer[ci]=diff.whole_image[ci];
				buffer_ind[ci]=(int)cinfo.input_iMCU_row*compptr.v_samp_factor;
			}

			return decompress_data_d_diff(cinfo, buffer, buffer_ind);
		}

		// Output some data from the full-image buffer sample in the multi-pass case.
		// Always attempts to emit one fully interleaved MCU row ("iMCU" row).
		// Return value is JPEG_ROW_COMPLETED, JPEG_SCAN_COMPLETED, or JPEG_SUSPENDED.
		//
		// NB: output_buf contains a plane for each component in image.
		static CONSUME_INPUT output_data_d_diff(jpeg_decompress cinfo, byte[][][] output_buf)
		{
			jpeg_lossless_d_codec losslsd=(jpeg_lossless_d_codec)cinfo.coef;
			d_diff_controller diff=(d_diff_controller)losslsd.diff_private;
			uint last_iMCU_row=cinfo.total_iMCU_rows-1;
			
			// Force some input to be done if we are getting ahead of the input.
			while(cinfo.input_scan_number<cinfo.output_scan_number||(cinfo.input_scan_number==cinfo.output_scan_number&&cinfo.input_iMCU_row<=cinfo.output_iMCU_row))
			{
				if(cinfo.inputctl.consume_input(cinfo)==CONSUME_INPUT.JPEG_SUSPENDED) return CONSUME_INPUT.JPEG_SUSPENDED;
			}

			// OK, output from the arrays.
			for(int ci=0; ci<cinfo.num_components; ci++)
			{
				jpeg_component_info compptr=cinfo.comp_info[ci];

				// Align the buffer for this component.
				byte[][] buffer=diff.whole_image[ci];

				int samp_rows;
				if(cinfo.output_iMCU_row<last_iMCU_row) samp_rows=compptr.v_samp_factor;
				else
				{
					// NB: can't use last_row_height here; it is input-side-dependent!
					samp_rows=(int)(compptr.height_in_blocks%compptr.v_samp_factor);
					if(samp_rows==0) samp_rows=compptr.v_samp_factor;
				}

				for(int row=0; row<samp_rows; row++)
				{
					Array.Copy(buffer[cinfo.output_iMCU_row*compptr.v_samp_factor+row], output_buf[ci][row], compptr.width_in_blocks);
				}
			}

			if(++(cinfo.output_iMCU_row)<cinfo.total_iMCU_rows) return CONSUME_INPUT.JPEG_ROW_COMPLETED;
			return CONSUME_INPUT.JPEG_SCAN_COMPLETED;
		}
#endif // D_MULTISCAN_FILES_SUPPORTED

		// Initialize difference buffer controller.
		static void jinit_d_diff_controller(jpeg_decompress cinfo, bool need_full_buffer)
		{
			jpeg_lossless_d_codec losslsd=(jpeg_lossless_d_codec)cinfo.coef;
			d_diff_controller diff=null;

			try
			{
				diff=new d_diff_controller();
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}
			losslsd.diff_private=diff;
			losslsd.diff_start_input_pass=start_input_pass_d_diff;
			losslsd.start_output_pass=start_output_pass_d_diff;

			// Create the [un]difference buffers.
			for(int ci=0; ci<cinfo.num_components; ci++)
			{
				jpeg_component_info compptr=cinfo.comp_info[ci];
				diff.diff_buf[ci]=alloc_darray(cinfo, (uint)jround_up(compptr.width_in_blocks, compptr.h_samp_factor), (uint)compptr.v_samp_factor);
				diff.undiff_buf[ci]=alloc_darray(cinfo, (uint)jround_up(compptr.width_in_blocks, compptr.h_samp_factor), (uint)compptr.v_samp_factor);
			}

			if(need_full_buffer)
			{
#if D_MULTISCAN_FILES_SUPPORTED
				// Allocate a full-image array for each component.
				for(int ci=0; ci<cinfo.num_components; ci++)
				{
					jpeg_component_info compptr=cinfo.comp_info[ci];
					diff.whole_image[ci]=alloc_sarray(cinfo, (uint)jround_up(compptr.width_in_blocks, compptr.h_samp_factor), (uint)jround_up(compptr.height_in_blocks, compptr.v_samp_factor));
				}
				losslsd.consume_data=consume_data_d_diff;
				losslsd.decompress_data=output_data_d_diff;
#else
				ERREXIT(cinfo, J_MESSAGE_CODE.JERR_NOT_COMPILED);
#endif
			}
			else
			{
				losslsd.consume_data=dummy_consume_data_d_diff;
				losslsd.decompress_data=decompress_data_d_diff;
				diff.whole_image[0]=null; // flag for no arrays
			}
		}
	}
}
#endif // D_LOSSLESS_SUPPORTED
