// jdcoefct.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1994-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains the coefficient buffer controller for decompression.
// This controller is the top level of the lossy JPEG decompressor proper.
// The coefficient buffer lies between entropy decoding and inverse-DCT steps.
//
// In buffered-image mode, this controller is the interface between
// input-oriented processing and output-oriented processing.
// Also, the input side (only) is used when reading a file for transcoding.

// Block smoothing is only applicable for progressive JPEG, so:
#if !D_PROGRESSIVE_SUPPORTED
	#undef BLOCK_SMOOTHING_SUPPORTED
#endif

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Private buffer controller object
		class d_coef_controller
		{
			// These variables keep track of the current location of the input side.
			// cinfo.input_iMCU_row is also used for this.
			public uint MCU_ctr;				// counts MCUs processed in current row
			public int MCU_vert_offset;			// counts MCU rows within iMCU row
			public int MCU_rows_per_iMCU_row;	// number of such rows needed

			// The output side's location is represented by cinfo.output_iMCU_row.

			// In single-pass modes, it's sufficient to buffer just one MCU.
			// We allocate a workspace of D_MAX_BLOCKS_IN_MCU coefficient blocks,
			// and let the entropy decoder write into that workspace each time.
			// In multi-pass modes, this array points to the current MCU's blocks
			// within the arrays; it is used only by the input side.
			public short[][] MCU_buffer=new short[D_MAX_BLOCKS_IN_MCU][];

#if D_MULTISCAN_FILES_SUPPORTED
			// In multi-pass modes, we need a block array for each component.
			public short[][][][] whole_image=new short[MAX_COMPONENTS][][][];
#endif

#if BLOCK_SMOOTHING_SUPPORTED
			// When doing block smoothing, we latch coefficient Al values here
			public int[][] coef_bits_latch;
#endif
		}
		public const int SAVED_COEFS=6;		// we save coef_bits[0..5]

		// Reset within-iMCU-row counters for a new row (input side)
		static void start_iMCU_row_d_coef(jpeg_decompress cinfo)
		{
			jpeg_lossy_d_codec lossyd=(jpeg_lossy_d_codec)cinfo.coef;
			d_coef_controller coef=(d_coef_controller)lossyd.coef_private;

			// In an interleaved scan, an MCU row is the same as an iMCU row.
			// In a noninterleaved scan, an iMCU row has v_samp_factor MCU rows.
			// But at the bottom of the image, process only what's left.
			if(cinfo.comps_in_scan>1) coef.MCU_rows_per_iMCU_row=1;
			else
			{
				if(cinfo.input_iMCU_row<(cinfo.total_iMCU_rows-1)) coef.MCU_rows_per_iMCU_row=cinfo.cur_comp_info[0].v_samp_factor;
				else coef.MCU_rows_per_iMCU_row=cinfo.cur_comp_info[0].last_row_height;
			}

			coef.MCU_ctr=0;
			coef.MCU_vert_offset=0;
		}

		// Initialize for an input processing pass.
		static void start_input_pass_d_coef(jpeg_decompress cinfo)
		{
			cinfo.input_iMCU_row=0;
			start_iMCU_row_d_coef(cinfo);
		}

		// Initialize for an output processing pass.
		static void start_output_pass_d_coef(jpeg_decompress cinfo)
		{
#if BLOCK_SMOOTHING_SUPPORTED
			jpeg_lossy_d_codec lossyd=(jpeg_lossy_d_codec)cinfo.coef;
			d_coef_controller coef=(d_coef_controller)lossyd.coef_private;

			// If multipass, check to see whether to use block smoothing on this pass
			if(lossyd.coef_arrays!=null)
			{
				if(cinfo.do_block_smoothing&&smoothing_ok(cinfo)) lossyd.decompress_data=decompress_smooth_data;
				else lossyd.decompress_data=decompress_data;
			}
#endif
			cinfo.output_iMCU_row=0;
		}

		// Decompress and return some data in the single-pass case.
		// Always attempts to emit one fully interleaved MCU row ("iMCU" row).
		// Input and output must run in lockstep since we have only a one-MCU buffer.
		// Return value is JPEG_ROW_COMPLETED, JPEG_SCAN_COMPLETED, or JPEG_SUSPENDED.
		//		
		// NB: output_buf contains a plane for each component in image,
		// which we index according to the component's SOF position.
		static CONSUME_INPUT decompress_onepass(jpeg_decompress cinfo, byte[][][] output_buf)
		{
			jpeg_lossy_d_codec lossyd=(jpeg_lossy_d_codec)cinfo.coef;
			d_coef_controller coef=(d_coef_controller)lossyd.coef_private;

			uint last_MCU_col=cinfo.MCUs_per_row-1;
			uint last_iMCU_row=cinfo.total_iMCU_rows-1;

			// Loop to process as much as one whole iMCU row
			for(int yoffset=coef.MCU_vert_offset; yoffset<coef.MCU_rows_per_iMCU_row; yoffset++)
			{
				for(uint MCU_col_num=coef.MCU_ctr; MCU_col_num<=last_MCU_col; MCU_col_num++) // index of current MCU within row
				{
					// Try to fetch an MCU. Entropy decoder expects buffer to be zeroed.
					for(int i=0; i<cinfo.blocks_in_MCU; i++)
					{
						for(int a=0; a<DCTSIZE2; a++) coef.MCU_buffer[i][a]=0;
					}

					if(!lossyd.entropy_decode_mcu(cinfo, coef.MCU_buffer))
					{
						// Suspension forced; update state counters and exit
						coef.MCU_vert_offset=yoffset;
						coef.MCU_ctr=MCU_col_num;
						return CONSUME_INPUT.JPEG_SUSPENDED;
					}

					// Determine where data should go in output_buf and do the IDCT thing.
					// We skip dummy blocks at the right and bottom edges (but blkn gets
					// incremented past them!). Note the inner loop relies on having
					// allocated the MCU_buffer[] blocks sequentially.
					int blkn=0; // index of current DCT block within MCU
					for(int ci=0; ci<cinfo.comps_in_scan; ci++)
					{
						jpeg_component_info compptr=cinfo.cur_comp_info[ci];

						// Don't bother to IDCT an uninteresting component.
						if(!compptr.component_needed)
						{
							blkn+=(int)compptr.MCU_blocks;
							continue;
						}

						inverse_DCT_method_ptr inverse_DCT=lossyd.inverse_DCT[compptr.component_index];

						int useful_width=(MCU_col_num<last_MCU_col)?compptr.MCU_width:compptr.last_col_width;

						byte[][] output_ptr=output_buf[compptr.component_index];
						uint output_ptr_ind=(uint)yoffset*compptr.DCT_scaled_size;

						uint start_col=MCU_col_num*(uint)compptr.MCU_sample_width;

						for(int yindex=0; yindex<compptr.MCU_height; yindex++)
						{
							if(cinfo.input_iMCU_row<last_iMCU_row||yoffset+yindex<compptr.last_row_height)
							{
								uint output_col=start_col;
								for(int xindex=0; xindex<useful_width; xindex++)
								{
									inverse_DCT(cinfo, compptr, coef.MCU_buffer[blkn+xindex], output_ptr, output_ptr_ind, output_col);
									output_col+=compptr.DCT_scaled_size;
								}
							}
							blkn+=compptr.MCU_width;
							output_ptr_ind+=compptr.DCT_scaled_size;
						}
					}
				}

				// Completed an MCU row, but perhaps not an iMCU row
				coef.MCU_ctr=0;
			}

			// Completed the iMCU row, advance counters for next one
			cinfo.output_iMCU_row++;
			if(++(cinfo.input_iMCU_row)<cinfo.total_iMCU_rows)
			{
				start_iMCU_row_d_coef(cinfo);
				return CONSUME_INPUT.JPEG_ROW_COMPLETED;
			}

			// Completed the scan
			cinfo.inputctl.finish_input_pass(cinfo);
			return CONSUME_INPUT.JPEG_SCAN_COMPLETED;
		}

		// Dummy consume-input routine for single-pass operation.
		static CONSUME_INPUT dummy_consume_data_d_coef(jpeg_decompress cinfo)
		{
			return CONSUME_INPUT.JPEG_SUSPENDED;	// Always indicate nothing was done
		}

#if D_MULTISCAN_FILES_SUPPORTED
		// Consume input data and store it in the full-image coefficient buffer.
		// We read as much as one fully interleaved MCU row ("iMCU" row) per call,
		// ie, v_samp_factor block rows for each component in the scan.
		// Return value is JPEG_ROW_COMPLETED, JPEG_SCAN_COMPLETED, or JPEG_SUSPENDED.
		static CONSUME_INPUT consume_data(jpeg_decompress cinfo)
		{
			jpeg_lossy_d_codec lossyd=(jpeg_lossy_d_codec)cinfo.coef;
			d_coef_controller coef=(d_coef_controller)lossyd.coef_private;

			short[][][][] buffer=new short[MAX_COMPS_IN_SCAN][][][];
			int[] buffer_ind=new int[MAX_COMPS_IN_SCAN];

			// Align the buffers for the components used in this scan.
			for(int ci=0; ci<cinfo.comps_in_scan; ci++)
			{
				jpeg_component_info compptr=cinfo.cur_comp_info[ci];
				buffer[ci]=coef.whole_image[compptr.component_index];
				buffer_ind[ci]=(int)cinfo.input_iMCU_row*compptr.v_samp_factor;
				// Note: entropy decoder expects buffer to be zeroed,
				// but this is handled automatically by the memory manager
				// because we requested a pre-zeroed array.
			}

			// Loop to process one whole iMCU row
			for(int yoffset=coef.MCU_vert_offset; yoffset<coef.MCU_rows_per_iMCU_row; yoffset++)
			{
				for(uint MCU_col_num=coef.MCU_ctr; MCU_col_num<cinfo.MCUs_per_row; MCU_col_num++) // index of current MCU within row
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

					// Try to fetch the MCU.
					if(!lossyd.entropy_decode_mcu(cinfo, coef.MCU_buffer))
					{
						// Suspension forced; update state counters and exit
						coef.MCU_vert_offset=yoffset;
						coef.MCU_ctr=MCU_col_num;
						return CONSUME_INPUT.JPEG_SUSPENDED;
					}
				}

				// Completed an MCU row, but perhaps not an iMCU row
				coef.MCU_ctr=0;
			}

			// Completed the iMCU row, advance counters for next one
			if(++(cinfo.input_iMCU_row)<cinfo.total_iMCU_rows)
			{
				start_iMCU_row_d_coef(cinfo);
				return CONSUME_INPUT.JPEG_ROW_COMPLETED;
			}

			// Completed the scan
			cinfo.inputctl.finish_input_pass(cinfo);
			return CONSUME_INPUT.JPEG_SCAN_COMPLETED;
		}

		// Decompress and return some data in the multi-pass case.
		// Always attempts to emit one fully interleaved MCU row ("iMCU" row).
		// Return value is JPEG_ROW_COMPLETED, JPEG_SCAN_COMPLETED, or JPEG_SUSPENDED.
		//
		// NB: output_buf contains a plane for each component in image.
		static CONSUME_INPUT decompress_data(jpeg_decompress cinfo, byte[][][] output_buf)
		{
			jpeg_lossy_d_codec lossyd=(jpeg_lossy_d_codec)cinfo.coef;
			d_coef_controller coef=(d_coef_controller)lossyd.coef_private;

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

				// Don't bother to IDCT an uninteresting component.
				if(!compptr.component_needed) continue;

				// Align the buffer for this component.
				short[][][] buffer=coef.whole_image[ci];
				uint buffer_ind=cinfo.output_iMCU_row*(uint)compptr.v_samp_factor;

				// Count non-dummy DCT block rows in this iMCU row.
				int block_rows;
				if(cinfo.output_iMCU_row<last_iMCU_row) block_rows=compptr.v_samp_factor;
				else
				{
					// NB: can't use last_row_height here; it is input-side-dependent!
					block_rows=(int)(compptr.height_in_blocks%compptr.v_samp_factor);
					if(block_rows==0) block_rows=compptr.v_samp_factor;
				}
				inverse_DCT_method_ptr inverse_DCT=lossyd.inverse_DCT[ci];
				byte[][] output_ptr=output_buf[ci];
				uint output_ptr_ind=0;

				// Loop over all DCT blocks to be processed.
				for(int block_row=0; block_row<block_rows; block_row++)
				{
					short[][] buffer_ptr=buffer[buffer_ind+block_row];
					uint output_col=0;
					for(uint block_num=0; block_num<compptr.width_in_blocks; block_num++)
					{
						inverse_DCT(cinfo, compptr, buffer_ptr[block_num], output_ptr, output_ptr_ind, output_col);
						output_col+=compptr.DCT_scaled_size;
					}
					output_ptr_ind+=compptr.DCT_scaled_size;
				}
			}

			if(++(cinfo.output_iMCU_row)<cinfo.total_iMCU_rows)	return CONSUME_INPUT.JPEG_ROW_COMPLETED;
			return CONSUME_INPUT.JPEG_SCAN_COMPLETED;
		}
#endif // D_MULTISCAN_FILES_SUPPORTED

#if BLOCK_SMOOTHING_SUPPORTED
		// This code applies interblock smoothing as described by section K.8
		// of the JPEG standard: the first 5 AC coefficients are estimated from
		// the DC values of a DCT block and its 8 neighboring blocks.
		// We apply smoothing only for progressive JPEG decoding, and only if
		// the coefficients it can estimate are not yet known to full precision.

		// Natural-order array positions of the first 5 zigzag-order coefficients
		const int Q01_POS=1;
		const int Q10_POS=8;
		const int Q20_POS=16;
		const int Q11_POS=9;
		const int Q02_POS=2;

		// Determine whether block smoothing is applicable and safe.
		// We also latch the current states of the coef_bits[] entries for the
		// AC coefficients; otherwise, if the input side of the decompressor
		// advances into a new scan, we might think the coefficients are known
		// more accurately than they really are.
		static bool smoothing_ok(jpeg_decompress cinfo)
		{
			jpeg_lossy_d_codec lossyd=(jpeg_lossy_d_codec)cinfo.coef;
			d_coef_controller coef=(d_coef_controller)lossyd.coef_private;
			
			if(cinfo.process!=J_CODEC_PROCESS.JPROC_PROGRESSIVE||cinfo.coef_bits==null) return false;

			// Allocate latch area if not already done
			if(coef.coef_bits_latch==null)
			{
				try
				{
					coef.coef_bits_latch=new int[cinfo.num_components][];
					for(int i=0; i<cinfo.num_components; i++) coef.coef_bits_latch[i]=new int[SAVED_COEFS];
				}
				catch
				{
					ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
				}
			}

			bool smoothing_useful=false;
			for(int ci=0; ci<cinfo.num_components; ci++)
			{
				jpeg_component_info compptr=cinfo.comp_info[ci];

				// All components' quantization values must already be latched.
				JQUANT_TBL qtable=compptr.quant_table;
				if(qtable==null) return false;

				// Verify DC & first 5 AC quantizers are nonzero to avoid zero-divide.
				if(qtable.quantval[0]==0||qtable.quantval[Q01_POS]==0||qtable.quantval[Q10_POS]==0||qtable.quantval[Q20_POS]==0||qtable.quantval[Q11_POS]==0||qtable.quantval[Q02_POS]==0) return false;

				// DC values must be at least partly known for all components.
				int[] coef_bits=cinfo.coef_bits[ci];
				if(coef_bits[0]<0) return false;

				// Block smoothing is helpful if some AC coefficients remain inaccurate.
				for(int coefi=1; coefi<=5; coefi++)
				{
					coef.coef_bits_latch[ci][coefi]=coef_bits[coefi];
					if(coef_bits[coefi]!=0) smoothing_useful=true;
				}
			}

			return smoothing_useful;
		}

		// Variant of decompress_data for use when doing block smoothing.
		static CONSUME_INPUT decompress_smooth_data(jpeg_decompress cinfo, byte[][][] output_buf)
		{
			jpeg_lossy_d_codec lossyd=(jpeg_lossy_d_codec)cinfo.coef;
			d_coef_controller coef=(d_coef_controller)lossyd.coef_private;

			// Force some input to be done if we are getting ahead of the input.
			while(cinfo.input_scan_number<=cinfo.output_scan_number&&!cinfo.inputctl.eoi_reached)
			{
				if(cinfo.input_scan_number==cinfo.output_scan_number)
				{
					// If input is working on current scan, we ordinarily want it to
					// have completed the current row. But if input scan is DC,
					// we want it to keep one row ahead so that next block row's DC
					// values are up to date.
					uint delta=(cinfo.Ss==0)?1u:0u;
					if(cinfo.input_iMCU_row>cinfo.output_iMCU_row+delta) break;
				}
				if(cinfo.inputctl.consume_input(cinfo)==CONSUME_INPUT.JPEG_SUSPENDED) return CONSUME_INPUT.JPEG_SUSPENDED;
			}

			uint last_iMCU_row=cinfo.total_iMCU_rows-1;
			short[] workspace=new short[DCTSIZE2];

			// OK, output from the arrays.
			for(int ci=0; ci<cinfo.num_components; ci++)
			{
				jpeg_component_info compptr=cinfo.comp_info[ci];

				// Don't bother to IDCT an uninteresting component.
				if(!compptr.component_needed) continue;

				// Count non-dummy DCT block rows in this iMCU row.
				int block_rows, access_rows;
				bool last_row;
				if(cinfo.output_iMCU_row<last_iMCU_row)
				{
					block_rows=compptr.v_samp_factor;
					access_rows=block_rows*2; // this and next iMCU row
					last_row=false;
				}
				else
				{
					// NB: can't use last_row_height here; it is input-side-dependent!
					block_rows=(int)(compptr.height_in_blocks%compptr.v_samp_factor);
					if(block_rows==0) block_rows=compptr.v_samp_factor;
					access_rows=block_rows; // this iMCU row only
					last_row=true;
				}

				// Align the buffer for this component.
				bool first_row;
				short[][][] buffer;
				int buffer_ind;
				if(cinfo.output_iMCU_row>0)
				{
					access_rows+=compptr.v_samp_factor; // prior iMCU row too
					buffer=coef.whole_image[ci];
					buffer_ind=(int)cinfo.output_iMCU_row*compptr.v_samp_factor; // point to current iMCU row
					first_row=false;
				}
				else
				{
					buffer=coef.whole_image[ci];
					buffer_ind=0;
					first_row=true;
				}

				// Fetch component-dependent info
				int[] coef_bits=coef.coef_bits_latch[ci];
				JQUANT_TBL quanttbl=compptr.quant_table;
				int Q00=quanttbl.quantval[0];
				int Q01=quanttbl.quantval[Q01_POS];
				int Q10=quanttbl.quantval[Q10_POS];
				int Q20=quanttbl.quantval[Q20_POS];
				int Q11=quanttbl.quantval[Q11_POS];
				int Q02=quanttbl.quantval[Q02_POS];
				inverse_DCT_method_ptr inverse_DCT=lossyd.inverse_DCT[ci];
				uint output_buf_ind=0;

				// Loop over all DCT blocks to be processed.
				for(int block_row=0; block_row<block_rows; block_row++)
				{
					short[][] buffer_ptr=buffer[buffer_ind+block_row];
					short[][] prev_block_row;
					short[][] next_block_row;

					if(first_row&&block_row==0) prev_block_row=buffer_ptr;
					else prev_block_row=buffer[buffer_ind+block_row-1];
					if(last_row&&block_row==block_rows-1) next_block_row=buffer_ptr;
					else next_block_row=buffer[buffer_ind+block_row+1];

					// We fetch the surrounding DC values using a sliding-register approach.
					// Initialize all nine here so as to do the right thing on narrow pics.
					int DC1, DC2, DC3, DC4, DC5, DC6, DC7, DC8, DC9;
					DC1=DC2=DC3=(int)prev_block_row[0][0];
					DC4=DC5=DC6=(int)buffer_ptr[0][0];
					DC7=DC8=DC9=(int)next_block_row[0][0];
					int ind=1;

					uint output_col=0;

					uint last_block_column=compptr.width_in_blocks-1;
					for(uint block_num=0; block_num<=last_block_column; block_num++)
					{
						// Fetch current DCT block into workspace so we can modify it.
						buffer_ptr.CopyTo(workspace, 0);

						// Update DC values
						if(block_num<last_block_column)
						{
							DC3=(int)prev_block_row[ind][0];
							DC6=(int)buffer_ptr[ind][0];
							DC9=(int)next_block_row[ind][0];
						}
						// Compute coefficient estimates per K.8.
						// An estimate is applied only if coefficient is still zero,
						// and is not known to be fully accurate.

						// AC01
						int Al=coef_bits[1];
						if(Al!=0&&workspace[1]==0)
						{
							int num=36*Q00*(DC4-DC6);
							int pred;
							if(num>=0)
							{
								pred=(int)(((Q01<<7)+num)/(Q01<<8));
								if(Al>0&&pred>=(1<<Al)) pred=(1<<Al)-1;
							}
							else
							{
								pred=(int)(((Q01<<7)-num)/(Q01<<8));
								if(Al>0&&pred>=(1<<Al)) pred=(1<<Al)-1;
								pred=-pred;
							}
							workspace[1]=(short)pred;
						}

						// AC10
						Al=coef_bits[2];
						if(Al!=0&&workspace[8]==0)
						{
							int num=36*Q00*(DC2-DC8);
							int pred;
							if(num>=0)
							{
								pred=(int)(((Q10<<7)+num)/(Q10<<8));
								if(Al>0&&pred>=(1<<Al)) pred=(1<<Al)-1;
							}
							else
							{
								pred=(int)(((Q10<<7)-num)/(Q10<<8));
								if(Al>0&&pred>=(1<<Al)) pred=(1<<Al)-1;
								pred=-pred;
							}
							workspace[8]=(short)pred;
						}

						// AC20
						Al=coef_bits[3];
						if(Al!=0&&workspace[16]==0)
						{
							int num=9*Q00*(DC2+DC8-2*DC5);
							int pred;
							if(num>=0)
							{
								pred=(int)(((Q20<<7)+num)/(Q20<<8));
								if(Al>0&&pred>=(1<<Al)) pred=(1<<Al)-1;
							}
							else
							{
								pred=(int)(((Q20<<7)-num)/(Q20<<8));
								if(Al>0&&pred>=(1<<Al)) pred=(1<<Al)-1;
								pred=-pred;
							}
							workspace[16]=(short)pred;
						}

						// AC11
						Al=coef_bits[4];
						if(Al!=0&&workspace[9]==0)
						{
							int num=5*Q00*(DC1-DC3-DC7+DC9);
							int pred;
							if(num>=0)
							{
								pred=(int)(((Q11<<7)+num)/(Q11<<8));
								if(Al>0&&pred>=(1<<Al)) pred=(1<<Al)-1;
							}
							else
							{
								pred=(int)(((Q11<<7)-num)/(Q11<<8));
								if(Al>0&&pred>=(1<<Al)) pred=(1<<Al)-1;
								pred=-pred;
							}
							workspace[9]=(short)pred;
						}

						// AC02
						Al=coef_bits[5];
						if(Al!=0&&workspace[2]==0)
						{
							int num=9*Q00*(DC4+DC6-2*DC5);
							int pred;
							if(num>=0)
							{
								pred=(int)(((Q02<<7)+num)/(Q02<<8));
								if(Al>0&&pred>=(1<<Al)) pred=(1<<Al)-1;
							}
							else
							{
								pred=(int)(((Q02<<7)-num)/(Q02<<8));
								if(Al>0&&pred>=(1<<Al)) pred=(1<<Al)-1;
								pred=-pred;
							}
							workspace[2]=(short)pred;
						}

						// OK, do the IDCT
						inverse_DCT(cinfo, compptr, workspace, output_buf[ci], output_buf_ind, output_col);

						// Advance for next column
						DC1=DC2; DC2=DC3;
						DC4=DC5; DC5=DC6;
						DC7=DC8; DC8=DC9;
						ind++;
						output_col+=compptr.DCT_scaled_size;
					}
					output_buf_ind+=compptr.DCT_scaled_size;
				}
			}

			if(++(cinfo.output_iMCU_row)<cinfo.total_iMCU_rows) return CONSUME_INPUT.JPEG_ROW_COMPLETED;
			return CONSUME_INPUT.JPEG_SCAN_COMPLETED;
		}
#endif // BLOCK_SMOOTHING_SUPPORTED

		// Initialize coefficient buffer controller.
		static void jinit_d_coef_controller(jpeg_decompress cinfo, bool need_full_buffer)
		{
			jpeg_lossy_d_codec lossyd=(jpeg_lossy_d_codec)cinfo.coef;
			d_coef_controller coef=null;

			try
			{
				coef=new d_coef_controller();
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}
			lossyd.coef_private=coef;
			lossyd.coef_start_input_pass=start_input_pass_d_coef;
			lossyd.coef_start_output_pass=start_output_pass_d_coef;
#if BLOCK_SMOOTHING_SUPPORTED
			coef.coef_bits_latch=null;
#endif

			// Create the coefficient buffer.
			if(need_full_buffer)
			{
#if D_MULTISCAN_FILES_SUPPORTED
				// Allocate a full-image array for each component,
				// padded to a multiple of samp_factor DCT blocks in each direction.
				// Note we ask for a pre-zeroed array.
				for(int ci=0; ci<cinfo.num_components; ci++)
				{
					jpeg_component_info compptr=cinfo.comp_info[ci];
					int access_rows=compptr.v_samp_factor;
					coef.whole_image[ci]=alloc_barray(cinfo, (uint)jround_up(compptr.width_in_blocks, compptr.h_samp_factor), (uint)jround_up(compptr.height_in_blocks, compptr.v_samp_factor));
				}
				lossyd.consume_data=consume_data;
				lossyd.decompress_data=decompress_data;
				lossyd.coef_arrays=coef.whole_image; // link to arrays
#else
				ERREXIT(cinfo, J_MESSAGE_CODE.JERR_NOT_COMPILED);
#endif
			}
			else
			{
				// We only need a single-MCU buffer.
				for(int i=0; i<D_MAX_BLOCKS_IN_MCU; i++) coef.MCU_buffer[i]=new short[DCTSIZE2];

				lossyd.consume_data=dummy_consume_data_d_coef;
				lossyd.decompress_data=decompress_onepass;
				lossyd.coef_arrays=null; // flag for no arrays
			}
		}
	}
}