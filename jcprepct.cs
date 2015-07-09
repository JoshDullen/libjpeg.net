// jcprepct.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1994-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains the compression preprocessing controller.
// This controller manages the color conversion, downsampling,
// and edge expansion steps.
//
// Most of the complexity here is associated with buffering input rows
// as required by the downsampler. See the comments at the head of
// jcsample.cs for the downsampler's needs.

// At present, jcsample.cs can request context rows only for smoothing.
// In the future, we might also need context rows for CCIR601 sampling
// or other more-complex downsampling procedures. The code to support
// context rows should be compiled only if needed.
#if INPUT_SMOOTHING_SUPPORTED
	#define CONTEXT_ROWS_SUPPORTED
#endif

using System;

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// For the simple (no-context-row) case, we just need to buffer one
		// row group's worth of pixels for the downsampling step. At the bottom of
		// the image, we pad to a full row group by replicating the last pixel row.
		// The downsampler's last output row is then replicated if needed to pad
		// out to a full iMCU row.
		//
		// When providing context rows, we must buffer three row groups' worth of
		// pixels. Three row groups are physically allocated, but the row pointer
		// arrays are made five row groups high, with the extra pointers above and
		// below "wrapping around" to point to the last and first real row groups.
		// This allows the downsampler to access the proper context rows.
		// At the top and bottom of the image, we create dummy context rows by
		// copying the first or last real pixel row. This copying could be avoided
		// by pointer hacking as is done in jdmainct.cs, but it doesn't seem worth the
		// trouble on the compression side.

		// Private buffer controller object
		class my_prep_controller : jpeg_c_prep_controller
		{
			// Downsampling input buffer. This buffer holds color-converted data
			// until we have enough to do a downsample step.
			public byte[][][] color_buf=new byte[MAX_COMPONENTS][][];

			public uint rows_to_go;		// counts rows remaining in source image
			public int next_buf_row;	// index of next row to store in color_buf

#if CONTEXT_ROWS_SUPPORTED				// only needed for context case
			public int this_row_group;	// starting row index of group to process
			public int next_buf_stop;	// downsample when we reach this index
#endif
		}

		// Initialize for a processing pass.
		static void start_pass_prep(jpeg_compress cinfo, J_BUF_MODE pass_mode)
		{
			my_prep_controller prep=(my_prep_controller)cinfo.prep;

			if(pass_mode!=J_BUF_MODE.JBUF_PASS_THRU) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_BUFFER_MODE);

			// Initialize total-height counter for detecting bottom of image
			prep.rows_to_go=cinfo.image_height;
			// Mark the conversion buffer empty
			prep.next_buf_row=0;
#if CONTEXT_ROWS_SUPPORTED
			// Preset additional state variables for context mode.
			// These aren't used in non-context mode, so we needn't test which mode.
			prep.this_row_group=0;
			// Set next_buf_stop to stop after two row groups have been read in.
			prep.next_buf_stop=2*cinfo.max_v_samp_factor;
#endif
		}

		// Expand an image vertically from height input_rows to height output_rows,
		// by duplicating the bottom row.
		static void expand_bottom_edge(byte[][] image_data, uint num_cols, int input_rows, int output_rows)
		{
			for(int row=input_rows; row<output_rows; row++)
				jcopy_sample_rows(image_data, input_rows-1, image_data, row, 1, num_cols);
		}

		// Process some data in the simple no-context case.
		//
		// Preprocessor output data is counted in "row groups". A row group
		// is defined to be v_samp_factor sample rows of each component.
		// Downsampling will produce this much data from each max_v_samp_factor input rows.
		static void pre_process_data(jpeg_compress cinfo, byte[][] input_buf, ref uint in_row_ctr, uint in_rows_avail, byte[][][] output_buf, ref uint out_row_group_ctr, uint out_row_groups_avail)
		{
			my_prep_controller prep=(my_prep_controller)cinfo.prep;
				
			while(in_row_ctr<in_rows_avail&&out_row_group_ctr<out_row_groups_avail)
			{
				// Do color conversion to fill the conversion buffer.
				uint inrows=in_rows_avail-in_row_ctr;
				int numrows=cinfo.max_v_samp_factor-prep.next_buf_row;
				numrows=(int)Math.Min((uint)numrows, inrows);
				cinfo.cconvert.color_convert(cinfo, input_buf, in_row_ctr, prep.color_buf, (uint)prep.next_buf_row, numrows);
				in_row_ctr+=(uint)numrows;
				prep.next_buf_row+=numrows;
				prep.rows_to_go-=(uint)numrows;
				// If at bottom of image, pad to fill the conversion buffer.
				if(prep.rows_to_go==0&&prep.next_buf_row<cinfo.max_v_samp_factor)
				{
					for(int ci=0; ci<cinfo.num_components; ci++) expand_bottom_edge(prep.color_buf[ci], cinfo.image_width, prep.next_buf_row, cinfo.max_v_samp_factor);
					prep.next_buf_row=cinfo.max_v_samp_factor;
				}
				// If we've filled the conversion buffer, empty it.
				if(prep.next_buf_row==cinfo.max_v_samp_factor)
				{
					cinfo.downsample.downsample(cinfo, prep.color_buf, 0, output_buf, out_row_group_ctr);
					prep.next_buf_row=0;
					out_row_group_ctr++;
				}
				// If at bottom of image, pad the output to a full iMCU height.
				// Note we assume the caller is providing a one-iMCU-height output buffer!
				if(prep.rows_to_go==0&&out_row_group_ctr<out_row_groups_avail)
				{
					for(int ci=0; ci<cinfo.num_components; ci++)
					{
						jpeg_component_info compptr=cinfo.comp_info[ci];
						expand_bottom_edge(output_buf[ci], compptr.width_in_blocks*cinfo.DCT_size, (int)(out_row_group_ctr*compptr.v_samp_factor), (int)(out_row_groups_avail*compptr.v_samp_factor));
					}
					out_row_group_ctr=out_row_groups_avail;
					break;			// can exit outer loop without test
				}
			} // while(...)
		}

#if CONTEXT_ROWS_SUPPORTED
		// Process some data in the context case.
		static void pre_process_context(jpeg_compress cinfo, byte[][] input_buf, ref uint in_row_ctr, uint in_rows_avail, byte[][][] output_buf, ref uint out_row_group_ctr, uint out_row_groups_avail)
		{
			my_prep_controller prep=(my_prep_controller)cinfo.prep;
			int buf_height=cinfo.max_v_samp_factor*3;
			int rgroup_height=cinfo.max_v_samp_factor;

			while(out_row_group_ctr<out_row_groups_avail)
			{
				if(in_row_ctr<in_rows_avail)
				{
					// Do color conversion to fill the conversion buffer.
					uint inrows=in_rows_avail-in_row_ctr;
					int numrows=prep.next_buf_stop-prep.next_buf_row;
					numrows=(int)Math.Min((uint)numrows, inrows);
					cinfo.cconvert.color_convert(cinfo, input_buf, in_row_ctr, prep.color_buf, (uint)rgroup_height+(uint)prep.next_buf_row, numrows);

					// Pad at top of image, if first time through
					if(prep.rows_to_go==cinfo.image_height)
					{
						for(int ci=0; ci<cinfo.num_components; ci++)
							for(int row=1; row<=cinfo.max_v_samp_factor; row++)
								jcopy_sample_rows(prep.color_buf[ci], rgroup_height, prep.color_buf[ci], rgroup_height-row, 1, cinfo.image_width);
					}
					in_row_ctr+=(uint)numrows;
					prep.next_buf_row+=numrows;
					prep.rows_to_go-=(uint)numrows;
				}
				else
				{
					// Return for more data, unless we are at the bottom of the image.
					if(prep.rows_to_go!=0) break;

					// When at bottom of image, pad to fill the conversion buffer.
					if(prep.next_buf_row<prep.next_buf_stop)
					{
						for(int ci=0; ci<cinfo.num_components; ci++)
							expand_bottom_edge(prep.color_buf[ci], cinfo.image_width, rgroup_height+prep.next_buf_row, rgroup_height+prep.next_buf_stop);

						prep.next_buf_row=prep.next_buf_stop;
					}
				}

				// If we've gotten enough data, downsample a row group.
				if(prep.next_buf_row==prep.next_buf_stop)
				{
					cinfo.downsample.downsample(cinfo, prep.color_buf, (uint)rgroup_height+(uint)prep.this_row_group, output_buf, out_row_group_ctr);
					out_row_group_ctr++;

					// Advance pointers with wraparound as necessary.
					prep.this_row_group+=cinfo.max_v_samp_factor;
					if(prep.this_row_group>=buf_height) prep.this_row_group=0;
					if(prep.next_buf_row>=buf_height) prep.next_buf_row=0;
					prep.next_buf_stop=prep.next_buf_row+cinfo.max_v_samp_factor;
				}
			} // while(...)
		}

		// Create the wrapped-around downsampling input buffer needed for context mode.
		static void create_context_buffer(jpeg_compress cinfo)
		{
			my_prep_controller prep=(my_prep_controller)cinfo.prep;
			int rgroup_height=cinfo.max_v_samp_factor;

			for(int ci=0; ci<cinfo.num_components; ci++)
			{
				jpeg_component_info compptr=cinfo.comp_info[ci];

				// Grab enough space for fake row pointers;
				// we need five row groups' worth of pointers for each component.
				byte[][] fake_buffer=new byte[5*rgroup_height][];

				// Allocate the actual buffer space (3 row groups) for this component.
				// We make the buffer wide enough to allow the downsampler to edge-expand
				// horizontally within the buffer, if it so chooses.
				byte[][] true_buffer=alloc_sarray(cinfo,
					(uint)(((int)compptr.width_in_blocks*cinfo.DCT_size*cinfo.max_h_samp_factor)/compptr.h_samp_factor),
					(uint)(3*rgroup_height));

				// Copy true buffer row pointers into the middle of the fake row array
				Array.Copy(true_buffer, 0, fake_buffer, rgroup_height, 3*rgroup_height);

				// Fill in the above and below wraparound pointers
				for(int i=0; i<rgroup_height; i++)
				{
					fake_buffer[i]=true_buffer[2*rgroup_height+i];
					fake_buffer[4*rgroup_height+i]=true_buffer[i];
				}
				prep.color_buf[ci]=fake_buffer;
			}
		}
#endif // CONTEXT_ROWS_SUPPORTED

		// Initialize preprocessing controller.
		static void jinit_c_prep_controller(jpeg_compress cinfo, bool need_full_buffer)
		{
			if(need_full_buffer) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_BUFFER_MODE); // safety check

			my_prep_controller prep=null;

			try
			{
				prep=new my_prep_controller();
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}
			cinfo.prep=prep;
			prep.start_pass=start_pass_prep;

			// Allocate the color conversion buffer.
			// We make the buffer wide enough to allow the downsampler to edge-expand
			// horizontally within the buffer, if it so chooses.
			if(cinfo.downsample.need_context_rows)
			{
				// Set up to provide context rows
#if CONTEXT_ROWS_SUPPORTED
				prep.pre_process_data=pre_process_context;
				create_context_buffer(cinfo);
#else
				ERREXIT(cinfo, J_MESSAGE_CODE.JERR_NOT_COMPILED);
#endif
			}
			else
			{
				// No context, just make it tall enough for one row group
				prep.pre_process_data=pre_process_data;
				for(int ci=0; ci<cinfo.num_components; ci++)
				{
					jpeg_component_info compptr=cinfo.comp_info[ci];
					prep.color_buf[ci]=alloc_sarray(cinfo,
						(uint)(((int)compptr.width_in_blocks*cinfo.DCT_size*cinfo.max_h_samp_factor)/compptr.h_samp_factor),
						(uint)cinfo.max_v_samp_factor);
				}
			}
		}
	}
}
