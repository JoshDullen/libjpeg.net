// jdmainct.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1994-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains the main buffer controller for decompression.
// The main buffer lies between the JPEG decompressor proper and the
// post-processor; it holds downsampled data in the JPEG colorspace.
//
// Note that this code is bypassed in raw-data mode, since the application
// supplies the equivalent of the main buffer in that case.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// In the current system design, the main buffer need never be a full-image
		// buffer; any full-height buffers will be found inside the coefficient or
		// postprocessing controllers. Nonetheless, the main controller is not
		// trivial. Its responsibility is to provide context rows for upsampling/
		// rescaling, and doing this in an efficient fashion is a bit tricky.
		//
		// Postprocessor input data is counted in "row groups".  A row group
		// is defined to be (v_samp_factor * DCT_scaled_size / min_DCT_scaled_size)
		// sample rows of each component.  (We require DCT_scaled_size values to be
		// chosen such that these numbers are integers.  In practice DCT_scaled_size
		// values will likely be powers of two, so we actually have the stronger
		// condition that DCT_scaled_size / min_DCT_scaled_size is an integer.)
		// Upsampling will typically produce max_v_samp_factor pixel rows from each
		// row group (times any additional scale factor that the upsampler is
		// applying).
		//
		// The coefficient controller will deliver data to us one iMCU row at a time;
		// each iMCU row contains v_samp_factor * DCT_scaled_size sample rows, or
		// exactly min_DCT_scaled_size row groups.  (This amount of data corresponds
		// to one row of MCUs when the image is fully interleaved.)  Note that the
		// number of sample rows varies across components, but the number of row
		// groups does not.  Some garbage sample rows may be included in the last iMCU
		// row at the bottom of the image.

		// Private buffer controller object
		class my_d_main_controller : jpeg_d_main_controller
		{
			// Pointer to allocated workspace (M or M+2 row groups).
			public byte[][][] buffer=new byte[MAX_COMPONENTS][][];

			public bool buffer_full;	// Have we gotten an iMCU row from decoder?
			public uint rowgroup_ctr;	// counts row groups output to postprocessor

#if UPSCALING_CONTEXT
			// Remaining fields are only used in the context case.
			public int context_state;		// process_data state machine status
			public uint rowgroups_avail;	// row groups available to postprocessor
			public uint iMCU_row_ctr;		// counts iMCU rows to detect image top/bot
#endif
		}

#if UPSCALING_CONTEXT
		// context_state values:
		const int CTX_PREPARE_FOR_IMCU=0;	// need to prepare for MCU row
		const int CTX_PROCESS_IMCU=1;		// feeding iMCU to postprocessor
		const int CTX_POSTPONED_ROW=2;		// feeding postponed row group
#endif
		// Initialize for a processing pass.
		static void start_pass_d_main(jpeg_decompress cinfo, J_BUF_MODE pass_mode)
		{
			my_d_main_controller main=(my_d_main_controller)cinfo.main;

			switch(pass_mode)
			{
				case J_BUF_MODE.JBUF_PASS_THRU:
#if UPSCALING_CONTEXT
					if(cinfo.upsample.need_context_rows)
					{
						main.process_data=process_data_context_d_main;
						main.context_state=CTX_PREPARE_FOR_IMCU;
						main.iMCU_row_ctr=0;
					}
					else
#endif
						main.process_data=process_data_simple_d_main; // Simple case with no context needed
					main.buffer_full=false;	// Mark buffer empty
					main.rowgroup_ctr=0;
					break;
#if QUANT_2PASS_SUPPORTED
				case J_BUF_MODE.JBUF_CRANK_DEST: main.process_data=process_data_crank_post_d_main; break; // For last pass of 2-pass quantization, just crank the postprocessor
#endif
				default: ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_BUFFER_MODE); break;
			}
		}

		// Process some data.
		// This handles the simple case where no context is required.
		static void process_data_simple_d_main(jpeg_decompress cinfo, byte[][] output_buf, ref uint out_row_ctr, uint out_rows_avail)
		{
			my_d_main_controller main=(my_d_main_controller)cinfo.main;
			uint rowgroups_avail;

			// Read input data if we haven't filled the main buffer yet
			if(!main.buffer_full)
			{
				if(cinfo.coef.decompress_data(cinfo, main.buffer)==CONSUME_INPUT.JPEG_SUSPENDED) return; // suspension forced, can do nothing more
				main.buffer_full=true; // OK, we have an iMCU row to work with
			}

			// There are always min_codec_data_unit row groups in an iMCU row.
			rowgroups_avail=(uint)cinfo.min_DCT_scaled_size;
			// Note: at the bottom of the image, we may pass extra garbage row groups
			// to the postprocessor. The postprocessor has to check for bottom
			// of image anyway (at row resolution), so no point in us doing it too.

			// Feed the postprocessor
			cinfo.post.post_process_data(cinfo, main.buffer, ref main.rowgroup_ctr, rowgroups_avail, output_buf, 0, ref out_row_ctr, out_rows_avail);

			// Has postprocessor consumed all the data yet? If so, mark buffer empty
			if(main.rowgroup_ctr>=rowgroups_avail)
			{
				main.buffer_full=false;
				main.rowgroup_ctr=0;
			}
		}

#if UPSCALING_CONTEXT
		// Process some data.
		// This handles the case where context rows must be provided.
		static void process_data_context_d_main(jpeg_decompress cinfo, byte[][] output_buf, ref uint out_row_ctr, uint out_rows_avail)
		{
			my_d_main_controller main=(my_d_main_controller)cinfo.main;

			// Read input data if we haven't filled the main buffer yet
			if(!main.buffer_full)
			{
				if(cinfo.coef.decompress_data(cinfo, main.buffer)==CONSUME_INPUT.JPEG_SUSPENDED) return; // suspension forced, can do nothing more
				main.buffer_full=true;	// OK, we have an iMCU row to work with
				main.iMCU_row_ctr++;	// count rows received
			}

			// Postprocessor typically will not swallow all the input data it is handed
			// in one call (due to filling the output buffer first). Must be prepared
			// to exit and restart. This switch lets us keep track of how far we got.
			// Note that each case falls through to the next on successful completion.
			switch(main.context_state)
			{
				case CTX_POSTPONED_ROW:
					// Call postprocessor using previously set pointers for postponed row
					cinfo.post.post_process_data(cinfo, main.buffer, ref main.rowgroup_ctr, main.rowgroups_avail, output_buf, 0, ref out_row_ctr, out_rows_avail);
					if(main.rowgroup_ctr<main.rowgroups_avail) return; // Need to suspend
					main.context_state=CTX_PREPARE_FOR_IMCU;
					if(out_row_ctr>=out_rows_avail) return; // Postprocessor exactly filled output buf
					goto case CTX_PREPARE_FOR_IMCU; // FALLTHROUGH
				case CTX_PREPARE_FOR_IMCU:
					// Prepare to process first M-1 row groups of this iMCU row
					main.rowgroup_ctr=0;
					main.rowgroups_avail=(uint)(cinfo.min_DCT_scaled_size-1);
					// Check for bottom of image: if so, tweak pointers to "duplicate"
					// the last sample row, and adjust rowgroups_avail to ignore padding rows.
					if(main.iMCU_row_ctr==cinfo.total_iMCU_rows)
					{
						for(int ci=0; ci<cinfo.num_components; ci++)
						{
							jpeg_component_info compptr=cinfo.comp_info[ci];

							// Count sample rows in one iMCU row and in one row group
							int iMCUheight=compptr.v_samp_factor*(int)compptr.DCT_scaled_size;
							int rgroup=iMCUheight/cinfo.min_DCT_scaled_size;

							// Count nondummy sample rows remaining for this component
							int rows_left=(int)(compptr.downsampled_height%(uint)iMCUheight);
							if(rows_left==0) rows_left=iMCUheight;

							// Count nondummy row groups. Should get same answer for each component,
							// so we need only do it once.
							if(ci==0) main.rowgroups_avail=(uint)((rows_left-1)/rgroup+1);

							if(!compptr.doContext) continue;

							byte[][] rows=main.buffer[ci];

							int l=rows.Length-1;
							for(int i=0; i<rgroup; i++) rows[l-rgroup*2].CopyTo(rows[rows.Length-rgroup*2+i], 0);
						}
					}

					main.context_state=CTX_PROCESS_IMCU;
					goto case CTX_PROCESS_IMCU; // FALLTHROUGH
				case CTX_PROCESS_IMCU:
					// Call postprocessor using previously set pointers
					cinfo.post.post_process_data(cinfo, main.buffer, ref main.rowgroup_ctr, main.rowgroups_avail, output_buf, 0, ref out_row_ctr, out_rows_avail);
					if(main.rowgroup_ctr<main.rowgroups_avail) return; // Need to suspend

					for(int ci=0; ci<cinfo.num_components; ci++)
					{
						jpeg_component_info compptr=cinfo.comp_info[ci];

						// Count sample rows in one iMCU row and in one row group
						int iMCUheight=compptr.v_samp_factor*(int)compptr.DCT_scaled_size;
						int rgroup=iMCUheight/cinfo.min_DCT_scaled_size;

						byte[][] rows=main.buffer[ci];

						int l=rows.Length-1;
						for(int i=0; i<rgroup*2; i++)
						{
							byte[] tmp=rows[l-i-rgroup*2];
							rows[l-i-rgroup*2]=rows[l-i];
							rows[l-i]=tmp;
						}
					}

					// Prepare to load new iMCU row using other xbuffer list
					//main.whichptr^=1;	// 0=>1 or 1=>0
					main.buffer_full=false;

					// Still need to process last row group of this iMCU row,
					// which is saved at index M+1 of the other xbuffer
					main.rowgroup_ctr=(uint)(cinfo.min_DCT_scaled_size+1);
					main.rowgroups_avail=(uint)(cinfo.min_DCT_scaled_size+2);
					main.context_state=CTX_POSTPONED_ROW;
					break;
			}
		}
#endif

		// Process some data.
		// Final pass of two-pass quantization: just call the postprocessor.
		// Source data will be the postprocessor controller's internal buffer.
#if QUANT_2PASS_SUPPORTED
		static void process_data_crank_post_d_main(jpeg_decompress cinfo, byte[][] output_buf, ref uint out_row_ctr, uint out_rows_avail)
		{
			uint dummy=0;
			cinfo.post.post_process_data(cinfo, null, ref dummy, 0, output_buf, 0, ref out_row_ctr, out_rows_avail);
		}
#endif // QUANT_2PASS_SUPPORTED

		// Initialize main buffer controller.
		public static void jinit_d_main_controller(jpeg_decompress cinfo, bool need_full_buffer)
		{
			my_d_main_controller main=null;

			try
			{
				main=new my_d_main_controller();
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}

			cinfo.main=main;
			main.start_pass=start_pass_d_main;

			if(need_full_buffer) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_BUFFER_MODE); // shouldn't happen

			// Allocate the workspace.
			// ngroups is the number of row groups we need.
			int ngroups=cinfo.min_DCT_scaled_size;
#if UPSCALING_CONTEXT
			if(cinfo.upsample.need_context_rows)
			{
				if(cinfo.min_DCT_scaled_size<2) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_NOTIMPL); // unsupported, see comments above
				ngroups=cinfo.min_DCT_scaled_size+2;
			}
#endif

			for(int ci=0; ci<cinfo.num_components; ci++)
			{
				jpeg_component_info compptr=cinfo.comp_info[ci];
				compptr.notFirst=false;

				int rgroup=(compptr.v_samp_factor*(int)compptr.DCT_scaled_size)/cinfo.min_DCT_scaled_size; // height of a row group of component
				main.buffer[ci]=alloc_sarray(cinfo, compptr.width_in_blocks*compptr.DCT_scaled_size, (uint)(rgroup*ngroups));
			}
		}
	}
}