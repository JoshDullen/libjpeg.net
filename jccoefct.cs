// jccoefct.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1994-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains the coefficient buffer controller for compression.
// This controller is the top level of the JPEG compressor proper.
// The coefficient buffer lies between forward-DCT and entropy encoding steps.

// We use a full-image coefficient buffer when doing Huffman optimization,
// and also for writing multiple-scan JPEG files. In all cases, the DCT
// step is run during the first pass, and subsequent passes need only read
// the buffered coefficients.
#if ENTROPY_OPT_SUPPORTED
	#define FULL_COEF_BUFFER_SUPPORTED
#else
	#if C_MULTISCAN_FILES_SUPPORTED
		#define FULL_COEF_BUFFER_SUPPORTED
	#endif
#endif

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Private buffer controller object
		class c_coef_controller
		{
			public uint iMCU_row_num;			// iMCU row # within image
			public uint mcu_ctr;				// counts MCUs processed in current row
			public int MCU_vert_offset;			// counts MCU rows within iMCU row
			public int MCU_rows_per_iMCU_row;	// number of such rows needed

			// For single-pass compression, it's sufficient to buffer just one MCU
			// (although this may prove a bit slow in practice). We allocate a
			// workspace of C_MAX_BLOCKS_IN_MCU coefficient blocks, and reuse it for
			// each MCU constructed and sent.
			// In multi-pass modes, this array points to the current MCU's blocks
			// within the arrays.
			public short[][] MCU_buffer=new short[C_MAX_BLOCKS_IN_MCU][];

			// In multi-pass modes, we need a block array for each component.
			public short[][][][] whole_image=new short[MAX_COMPONENTS][][][];
		}

		// Reset within-iMCU-row counters for a new row
		static void start_iMCU_row_c_coef(jpeg_compress cinfo)
		{
			jpeg_lossy_c_codec lossyc=(jpeg_lossy_c_codec)cinfo.coef;
			c_coef_controller coef=(c_coef_controller)lossyc.coef_private;

			// In an interleaved scan, an MCU row is the same as an iMCU row.
			// In a noninterleaved scan, an iMCU row has v_samp_factor MCU rows.
			// But at the bottom of the image, process only what's left.
			if(cinfo.comps_in_scan>1) coef.MCU_rows_per_iMCU_row=1;
			else
			{
				if(coef.iMCU_row_num<(cinfo.total_iMCU_rows-1))
					coef.MCU_rows_per_iMCU_row=cinfo.cur_comp_info[0].v_samp_factor;
				else
					coef.MCU_rows_per_iMCU_row=cinfo.cur_comp_info[0].last_row_height;
			}

			coef.mcu_ctr=0;
			coef.MCU_vert_offset=0;
		}

		// Initialize for a processing pass.
		static void start_pass_coef(jpeg_compress cinfo, J_BUF_MODE pass_mode)
		{
			jpeg_lossy_c_codec lossyc=(jpeg_lossy_c_codec)cinfo.coef;
			c_coef_controller coef=(c_coef_controller)lossyc.coef_private;

			coef.iMCU_row_num=0;
			start_iMCU_row_c_coef(cinfo);

			switch(pass_mode)
			{
				case J_BUF_MODE.JBUF_PASS_THRU:
					if(coef.whole_image[0]!=null) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_BUFFER_MODE);
					lossyc.compress_data=compress_data_coef;
					break;
#if FULL_COEF_BUFFER_SUPPORTED
				case J_BUF_MODE.JBUF_SAVE_AND_PASS:
					if(coef.whole_image[0]==null) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_BUFFER_MODE);
					lossyc.compress_data=compress_first_pass_coef;
					break;
				case J_BUF_MODE.JBUF_CRANK_DEST:
					if(coef.whole_image[0]==null) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_BUFFER_MODE);
					lossyc.compress_data=compress_output_coef;
					break;
#endif
				default:
					ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_BUFFER_MODE);
					break;
			}
		}

		// Process some data in the single-pass case.
		// We process the equivalent of one fully interleaved MCU row ("iMCU" row)
		// per call, ie, v_samp_factor block rows for each component in the image.
		// Returns true if the iMCU row is completed, false if suspended.
		//
		// NB: input_buf contains a plane for each component in image,
		// which we index according to the component's SOF position.
		static bool compress_data_coef(jpeg_compress cinfo, byte[][][] input_buf)
		{
			jpeg_lossy_c_codec lossyc=(jpeg_lossy_c_codec)cinfo.coef;
			c_coef_controller coef=(c_coef_controller)lossyc.coef_private;
			uint last_MCU_col=cinfo.MCUs_per_row-1;
			uint last_iMCU_row=cinfo.total_iMCU_rows-1;

			// Loop to write as much as one whole iMCU row
			for(int yoffset=coef.MCU_vert_offset; yoffset<coef.MCU_rows_per_iMCU_row; yoffset++)
			{
				for(uint MCU_col_num=coef.mcu_ctr; MCU_col_num<=last_MCU_col; MCU_col_num++) // index of current MCU within row
				{
					// Determine where data comes from in input_buf and do the DCT thing.
					// Each call on forward_DCT processes a horizontal row of DCT blocks
					// as wide as an MCU; we rely on having allocated the MCU_buffer[] blocks
					// sequentially. Dummy blocks at the right or bottom edge are filled in
					// specially. The data in them does not matter for image reconstruction,
					// so we fill them with values that will encode to the smallest amount of
					// data, viz: all zeroes in the AC entries, DC entries equal to previous
					// block's DC value. (Thanks to Thomas Kinsman for this idea.)
					int blkn=0;
					for(int ci=0; ci<cinfo.comps_in_scan; ci++)
					{
						jpeg_component_info compptr=cinfo.cur_comp_info[ci];
						int blockcnt=(MCU_col_num<last_MCU_col)?compptr.MCU_width:compptr.last_col_width;

						uint xpos=MCU_col_num*(uint)compptr.MCU_sample_width;
						uint ypos=(uint)yoffset*DCTSIZE; // ypos == (yoffset+yindex) * DCTSIZE

						for(int yindex=0; yindex<compptr.MCU_height; yindex++)
						{
							if(coef.iMCU_row_num<last_iMCU_row||yoffset+yindex<compptr.last_row_height)
							{
								lossyc.fdct_forward_DCT(cinfo, compptr, input_buf[compptr.component_index], coef.MCU_buffer, blkn, ypos, xpos, (uint)blockcnt);
								if(blockcnt<compptr.MCU_width)
								{
									// Create some dummy blocks at the right edge of the image.
									for(int bi=blockcnt; bi<compptr.MCU_width; bi++)
									{
										short[] block=coef.MCU_buffer[blkn+bi];
										for(int i=1; i<DCTSIZE2; i++) block[i]=0; // ACs
										block[0]=coef.MCU_buffer[blkn+bi-1][0]; // DCs
									}
								}
							}
							else
							{
								// Create a row of dummy blocks at the bottom of the image.
								for(int bi=0; bi<compptr.MCU_width; bi++)
								{
									short[] block=coef.MCU_buffer[blkn+bi];
									for(int i=1; i<DCTSIZE2; i++) block[i]=0; // ACs
									block[0]=coef.MCU_buffer[blkn-1][0]; // DCs
								}
							}
							blkn+=compptr.MCU_width;
							ypos+=DCTSIZE;
						}
					}

					// Try to write the MCU. In event of a suspension failure, we will
					// re-DCT the MCU on restart (a bit inefficient, could be fixed...)
					if(!lossyc.entropy_encode_mcu(cinfo, coef.MCU_buffer))
					{
						// Suspension forced; update state counters and exit
						coef.MCU_vert_offset=yoffset;
						coef.mcu_ctr=MCU_col_num;
						return false;
					}
				}

				// Completed an MCU row, but perhaps not an iMCU row
				coef.mcu_ctr=0;
			}

			// Completed the iMCU row, advance counters for next one
			coef.iMCU_row_num++;
			start_iMCU_row_c_coef(cinfo);

			return true;
		}

#if FULL_COEF_BUFFER_SUPPORTED
		// Process some data in the first pass of a multi-pass case.
		// We process the equivalent of one fully interleaved MCU row ("iMCU" row)
		// per call, ie, v_samp_factor block rows for each component in the image.
		// This amount of data is read from the source buffer, DCT'd and quantized,
		// and saved into the arrays. We also generate suitable dummy blocks
		// as needed at the right and lower edges. (The dummy blocks are constructed
		// in the arrays, which have been padded appropriately.) This makes
		// it possible for subsequent passes not to worry about real vs. dummy blocks.
		//
		// We must also emit the data to the entropy encoder. This is conveniently
		// done by calling compress_output_coef() after we've loaded the current strip
		// of the arrays.
		//
		// NB: input_buf contains a plane for each component in image. All
		// components are DCT'd and loaded into the arrays in this pass.
		// However, it may be that only a subset of the components are emitted to
		// the entropy encoder during this first pass; be careful about looking
		// at the scan-dependent variables (MCU dimensions, etc).
		static bool compress_first_pass_coef(jpeg_compress cinfo, byte[][][] input_buf)
		{
			jpeg_lossy_c_codec lossyc=(jpeg_lossy_c_codec)cinfo.coef;
			c_coef_controller coef=(c_coef_controller)lossyc.coef_private;

			uint last_iMCU_row=cinfo.total_iMCU_rows-1;

			for(int ci=0; ci<cinfo.num_components; ci++)
			{
				jpeg_component_info compptr=cinfo.comp_info[ci];

				// Align the buffer for this component.
				short[][][] buffer=coef.whole_image[ci];
				int buffer_ind=(int)coef.iMCU_row_num*compptr.v_samp_factor;

				// Count non-dummy DCT block rows in this iMCU row.
				int block_rows;
				if(coef.iMCU_row_num<last_iMCU_row) block_rows=compptr.v_samp_factor;
				else
				{
					// NB: can't use last_row_height here, since may not be set!
					block_rows=(int)(compptr.height_in_blocks%compptr.v_samp_factor);
					if(block_rows==0) block_rows=compptr.v_samp_factor;
				}

				uint blocks_across=compptr.width_in_blocks;
				int h_samp_factor=compptr.h_samp_factor;

				// Count number of dummy blocks to be added at the right margin.
				int ndummy=(int)(blocks_across%h_samp_factor);
				if(ndummy>0) ndummy=h_samp_factor-ndummy;

				// Perform DCT for all non-dummy blocks in this iMCU row. Each call
				// on forward_DCT processes a complete horizontal row of DCT blocks.
				for(int block_row=0; block_row<block_rows; block_row++)
				{
					short[][] row=buffer[buffer_ind+block_row];
					lossyc.fdct_forward_DCT(cinfo, compptr, input_buf[ci], row, 0, (uint)(block_row*DCTSIZE), 0, blocks_across);
					if(ndummy>0)
					{
						// Create dummy blocks at the right edge of the image.
						short lastDC=row[blocks_across-1][0];
						for(int bi=0; bi<ndummy; bi++)
						{
							short[] block=row[blocks_across+bi];
							for(int i=1; i<DCTSIZE2; i++) block[i]=0;
							block[0]=lastDC;
						}
					}
				}
				// If at end of image, create dummy block rows as needed.
				// The tricky part here is that within each MCU, we want the DC values
				// of the dummy blocks to match the last real block's DC value.
				// This squeezes a few more bytes out of the resulting file...
				if(coef.iMCU_row_num==last_iMCU_row)
				{
					blocks_across+=(uint)ndummy; // include lower right corner
					uint MCUs_across=blocks_across/(uint)h_samp_factor;
					for(int block_row=block_rows; block_row<compptr.v_samp_factor; block_row++)
					{
						short[][] thisblockrow=buffer[buffer_ind+block_row];
						short[][] lastblockrow=buffer[buffer_ind+block_row-1];
						int thisblockrow_ind=0;
						int lastblockrow_ind=h_samp_factor-1;

						for(int j=0; j<blocks_across; j++)
						{
							short[] block=thisblockrow[j];
							for(int i=0; i<DCTSIZE2; i++) block[i]=0;
						}

						for(uint MCUindex=0; MCUindex<MCUs_across; MCUindex++)
						{
							short lastDC=lastblockrow[lastblockrow_ind][0];
							for(int bi=0; bi<h_samp_factor; bi++) thisblockrow[thisblockrow_ind+bi][0]=lastDC;

							thisblockrow_ind+=h_samp_factor; // advance to next MCU in row
							lastblockrow_ind+=h_samp_factor;
						}
					}
				}
			}

			// NB: compress_output will increment iMCU_row_num if successful.
			// A suspension return will result in redoing all the work above next time.

			// Emit data to the entropy encoder, sharing code with subsequent passes
			return compress_output_coef(cinfo, input_buf);
		}

		// Process some data in subsequent passes of a multi-pass case.
		// We process the equivalent of one fully interleaved MCU row ("iMCU" row)
		// per call, ie, v_samp_factor block rows for each component in the scan.
		// The data is obtained from the arrays and fed to the entropy coder.
		// Returns true if the iMCU row is completed, false if suspended.
		//
		// NB: input_buf is ignored; it is likely to be a null pointer.
		static bool compress_output_coef(jpeg_compress cinfo, byte[][][] input_buf)
		{
			jpeg_lossy_c_codec lossyc=(jpeg_lossy_c_codec)cinfo.coef;
			c_coef_controller coef=(c_coef_controller)lossyc.coef_private;
			short[][][][] buffer=new short[MAX_COMPS_IN_SCAN][][][];
			int[] buffer_ind=new int[MAX_COMPS_IN_SCAN];

			// Align the buffers for the components used in this scan.
			// NB: during first pass, this is safe only because the buffers will
			// already be aligned properly, so jmemmgr.cs won't need to do any I/O.
			for(int ci=0; ci<cinfo.comps_in_scan; ci++)
			{
				jpeg_component_info compptr=cinfo.cur_comp_info[ci];
				buffer[ci]=coef.whole_image[compptr.component_index];
				buffer_ind[ci]=(int)coef.iMCU_row_num*compptr.v_samp_factor;
			}

			// Loop to process one whole iMCU row
			for(int yoffset=coef.MCU_vert_offset; yoffset<coef.MCU_rows_per_iMCU_row; yoffset++)
			{
				for(uint MCU_col_num=coef.mcu_ctr; MCU_col_num<cinfo.MCUs_per_row; MCU_col_num++)
				{
					// Construct list of pointers to DCT blocks belonging to this MCU
					int blkn=0; // index of current DCT block within MCU
					for(int ci=0; ci<cinfo.comps_in_scan; ci++)
					{
						jpeg_component_info compptr=cinfo.cur_comp_info[ci];
						uint start_col=MCU_col_num*(uint)compptr.MCU_width;
						for(int yindex=0; yindex<compptr.MCU_height; yindex++)
						{
							short[][] buffer_ptr=buffer[ci][buffer_ind[ci]+yindex+yoffset];
							uint buffer_ptr_ind=start_col;
							for(int xindex=0; xindex<compptr.MCU_width; xindex++) coef.MCU_buffer[blkn++]=buffer_ptr[buffer_ptr_ind++];
						}
					}

					// Try to write the MCU.
					if(!lossyc.entropy_encode_mcu(cinfo, coef.MCU_buffer))
					{
						// Suspension forced; update state counters and exit
						coef.MCU_vert_offset=yoffset;
						coef.mcu_ctr=MCU_col_num;
						return false;
					}
				}

				// Completed an MCU row, but perhaps not an iMCU row
				coef.mcu_ctr=0;
			}

			// Completed the iMCU row, advance counters for next one
			coef.iMCU_row_num++;
			start_iMCU_row_c_coef(cinfo);

			return true;
		}
#endif // FULL_COEF_BUFFER_SUPPORTED

		static void jinit_c_coef_controller(jpeg_compress cinfo, bool need_full_buffer)
		{
			jpeg_lossy_c_codec lossyc=(jpeg_lossy_c_codec)cinfo.coef;
			c_coef_controller coef=null;

			try
			{
				coef=new c_coef_controller();
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}

			lossyc.coef_private=coef;
			lossyc.coef_start_pass=start_pass_coef;

			// Create the coefficient buffer.
			if(need_full_buffer)
			{
#if FULL_COEF_BUFFER_SUPPORTED
				// Allocate a full-image array for each component,
				// padded to a multiple of samp_factor DCT blocks in each direction.
				for(int ci=0; ci<cinfo.num_components; ci++)
				{
					jpeg_component_info compptr=cinfo.comp_info[ci];

					coef.whole_image[ci]=alloc_barray(cinfo,
						(uint)jround_up((int)compptr.width_in_blocks, compptr.h_samp_factor),
						(uint)jround_up((int)compptr.height_in_blocks, compptr.v_samp_factor));
				}
#else
				ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_BUFFER_MODE);
#endif
			}
			else
			{
				// We only need a single-MCU buffer.
				try
				{
					for(int i=0; i<C_MAX_BLOCKS_IN_MCU; i++) coef.MCU_buffer[i]=new short[DCTSIZE2];
				}
				catch
				{
					ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
				}

				coef.whole_image[0]=null; // flag for no arrays
			}
		}
	}
}