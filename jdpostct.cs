// jdpostct.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1994-1996, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains the decompression postprocessing controller.
// This controller manages the upsampling, color conversion, and color
// quantization/reduction steps; specifically, it controls the buffering
// between upsample/color conversion and color quantization/reduction.
//
// If no color quantization/reduction is required, then this module has no
// work to do, and it just hands off to the upsample/color conversion code.
// An integrated upsample/convert/quantize process would replace this module
// entirely.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Private buffer controller object
		class my_post_controller : jpeg_d_post_controller
		{
			// Color quantization source buffer: this holds output data from
			// the upsample/color conversion step to be passed to the quantizer.
			// For two-pass color quantization, we need a full-image buffer;
			// for one-pass operation, a strip buffer is sufficient.
			public byte[][] whole_image;	// virtual array, or null if one-pass
			public byte[][] buffer;			// strip buffer, or current strip of virtual
			public uint buffer_offset;

			public uint strip_height;		// buffer size in rows
			// for two-pass mode only:
			public uint starting_row;	// row # of first row in current strip
			public uint next_row;		// index of next row to fill/empty in strip
		}

		// Initialize for a processing pass.
		static void start_pass_dpost(jpeg_decompress cinfo, J_BUF_MODE pass_mode)
		{
			my_post_controller post=(my_post_controller)cinfo.post;

			switch(pass_mode)
			{
				case J_BUF_MODE.JBUF_PASS_THRU:
					if(cinfo.quantize_colors)
					{
						// Single-pass processing with color quantization.
						post.post_process_data=post_process_1pass;

						// We could be doing buffered-image output before starting a 2-pass
						// color quantization; in that case, jinit_d_post_controller did not
						// allocate a strip buffer. Use the virtual-array buffer as workspace.
						if(post.buffer==null)
						{
							post.buffer=post.whole_image;
							post.buffer_offset=0;
						}
					}
					else
					{
						// For single-pass processing without color quantization,
						// I have no work to do; just call the upsampler directly.
						post.post_process_data=cinfo.upsample.upsample;
					}
					break;
#if QUANT_2PASS_SUPPORTED
				case J_BUF_MODE.JBUF_SAVE_AND_PASS:
					// First pass of 2-pass quantization
					if(post.whole_image==null) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_BUFFER_MODE);
					post.post_process_data=post_process_prepass;
					break;
				case J_BUF_MODE.JBUF_CRANK_DEST:
					// Second pass of 2-pass quantization
					if(post.whole_image==null) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_BUFFER_MODE);
					post.post_process_data=post_process_2pass;
					break;
#endif // QUANT_2PASS_SUPPORTED
				default: ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_BUFFER_MODE); break;
			}
			post.starting_row=post.next_row=0;
		}

		// Process some data in the one-pass (strip buffer) case.
		// This is used for color precision reduction as well as one-pass quantization.
		static void post_process_1pass(jpeg_decompress cinfo, byte[][][] input_buf, ref uint in_row_group_ctr, uint in_row_groups_avail, byte[][] output_buf, uint ignore_me, ref uint out_row_ctr, uint out_rows_avail)
		{
			my_post_controller post=(my_post_controller)cinfo.post;

			// Fill the buffer, but not more than what we can dump out in one go.
			// Note we rely on the upsampler to detect bottom of image.
			uint max_rows=out_rows_avail-out_row_ctr;
			if(max_rows>post.strip_height) max_rows=post.strip_height;

			uint num_rows=0;
			cinfo.upsample.upsample(cinfo, input_buf, ref in_row_group_ctr, in_row_groups_avail, post.buffer, post.buffer_offset, ref num_rows, max_rows);

			// Quantize and emit data.
			cinfo.cquantize.color_quantize(cinfo, post.buffer, post.buffer_offset, output_buf, out_row_ctr, (int)num_rows);
			out_row_ctr+=num_rows;
		}

#if QUANT_2PASS_SUPPORTED
		// Process some data in the first pass of 2-pass quantization.
		static void post_process_prepass(jpeg_decompress cinfo, byte[][][] input_buf, ref uint in_row_group_ctr, uint in_row_groups_avail, byte[][] output_buf, uint ignore_me, ref uint out_row_ctr, uint out_rows_avail)
		{
			my_post_controller post=(my_post_controller)cinfo.post;

			// Reposition virtual buffer if at start of strip.
			if(post.next_row==0)
			{
				post.buffer=post.whole_image;
				post.buffer_offset=post.starting_row;
			}

			// Upsample some data (up to a strip height's worth).
			uint old_next_row=post.next_row;
			cinfo.upsample.upsample(cinfo, input_buf, ref in_row_group_ctr, in_row_groups_avail, post.buffer, post.buffer_offset, ref post.next_row, post.strip_height);

			// Allow quantizer to scan new data. No data is emitted,
			// but we advance out_row_ctr so outer loop can tell when we're done.
			if(post.next_row>old_next_row)
			{
				uint num_rows=post.next_row-old_next_row;
				cinfo.cquantize.color_quantize(cinfo, post.buffer, post.buffer_offset+old_next_row, null, 0, (int)num_rows);
				out_row_ctr+=num_rows;
			}

			// Advance if we filled the strip.
			if(post.next_row>=post.strip_height)
			{
				post.starting_row+=post.strip_height;
				post.next_row=0;
			}
		}

		// Process some data in the second pass of 2-pass quantization.
		static void post_process_2pass(jpeg_decompress cinfo, byte[][][] input_buf, ref uint in_row_group_ctr, uint in_row_groups_avail, byte[][] output_buf, uint ignore_me, ref uint out_row_ctr, uint out_rows_avail)
		{
			my_post_controller post=(my_post_controller)cinfo.post;

			// Reposition virtual buffer if at start of strip.
			if(post.next_row==0)
			{
				post.buffer=post.whole_image;
				post.buffer_offset=post.starting_row;
			}

			// Determine number of rows to emit.
			uint num_rows=post.strip_height-post.next_row; // available in strip
			uint max_rows=out_rows_avail-out_row_ctr; // available in output area
			if(num_rows>max_rows) num_rows=max_rows;

			// We have to check bottom of image here, can't depend on upsampler.
			max_rows=cinfo.output_height-post.starting_row;
			if(num_rows>max_rows) num_rows=max_rows;

			// Quantize and emit data.
			cinfo.cquantize.color_quantize(cinfo, post.buffer, post.buffer_offset+post.next_row, output_buf, out_row_ctr, (int)num_rows);
			out_row_ctr+=num_rows;

			// Advance if we filled the strip.
			post.next_row+=num_rows;
			if(post.next_row>=post.strip_height)
			{
				post.starting_row+=post.strip_height;
				post.next_row=0;
			}
		}
#endif // QUANT_2PASS_SUPPORTED

		// Initialize postprocessing controller.
		public static void jinit_d_post_controller(jpeg_decompress cinfo, bool need_full_buffer)
		{
			my_post_controller post=null;

			try
			{
				post=new my_post_controller();
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}
			cinfo.post=post;
			post.start_pass=start_pass_dpost;
			post.whole_image=null;	// flag for no virtual arrays
			post.buffer=null;		// flag for no strip buffer
			post.buffer_offset=0;

			// Create the quantization buffer, if needed
			if(cinfo.quantize_colors)
			{
				// The buffer strip height is max_v_samp_factor, which is typically
				// an efficient number of rows for upsampling to return.
				// (In the presence of output rescaling, we might want to be smarter?)
				post.strip_height=(uint)cinfo.max_v_samp_factor;
				if(need_full_buffer)
				{
					// Two-pass color quantization: need full-image storage.
					// We round up the number of rows to a multiple of the strip height.
#if QUANT_2PASS_SUPPORTED
					post.whole_image=alloc_sarray(cinfo, (uint)(cinfo.output_width*cinfo.out_color_components), (uint)jround_up(cinfo.output_height, post.strip_height));
#else
					ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_BUFFER_MODE);
#endif // QUANT_2PASS_SUPPORTED
				}
				else
				{
					// One-pass color quantization: just make a strip buffer.
					post.buffer=alloc_sarray(cinfo, (uint)(cinfo.output_width*cinfo.out_color_components), post.strip_height);
				}
			}
		}
	}
}
