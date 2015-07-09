// jcmainct.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1994-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains the main buffer controller for compression.
// The main buffer lies between the pre-processor and the JPEG
// compressor proper; it holds downsampled data in the JPEG colorspace.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Private buffer controller object
		class my_c_main_controller : jpeg_c_main_controller
		{
			public uint cur_iMCU_row;		// number of current iMCU row
			public uint rowgroup_ctr;		// counts row groups received in iMCU row
			public bool suspended;			// remember if we suspended output
			public J_BUF_MODE pass_mode;	// current operating mode

			// If using just a strip buffer, this points to the entire set of buffers
			// (we allocate one for each component). In the full-image case, this
			// points to the currently accessible strips of the arrays.
			public byte[][][] buffer=new byte[MAX_COMPONENTS][][];
		}

		// Initialize for a processing pass.
		static void start_pass_c_main(jpeg_compress cinfo, J_BUF_MODE pass_mode)
		{
			my_c_main_controller main=(my_c_main_controller)cinfo.main;

			// Do nothing in raw-data mode.
			if(cinfo.raw_data_in) return;

			main.cur_iMCU_row=0;		// initialize counters
			main.rowgroup_ctr=0;
			main.suspended=false;
			main.pass_mode=pass_mode;	// save mode for use by process_data

			switch(pass_mode)
			{
				case J_BUF_MODE.JBUF_PASS_THRU: main.process_data=process_data_simple_c_main; break;
				default: ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_BUFFER_MODE); break;
			}
		}

		// Process some data.
		// This	routine handles the simple pass-through mode,
		// where we have only a strip buffer.
		static void process_data_simple_c_main(jpeg_compress cinfo, byte[][] input_buf, ref uint in_row_ctr, uint in_rows_avail)
		{
			my_c_main_controller main=(my_c_main_controller)cinfo.main;
			uint DCT_size=cinfo.DCT_size;

			while(main.cur_iMCU_row<cinfo.total_iMCU_rows)
			{
				// Read input data if we haven't filled the main buffer yet
				if(main.rowgroup_ctr<DCT_size)
					cinfo.prep.pre_process_data(cinfo, input_buf, ref in_row_ctr, in_rows_avail, main.buffer, ref main.rowgroup_ctr, (uint)DCT_size);

				// If we don't have a full iMCU row buffered, return to application for
				// more data. Note that preprocessor will always pad to fill the iMCU row
				// at the bottom of the image.
				if(main.rowgroup_ctr!=DCT_size) return;

				// Send the completed row to the compressor
				if(!cinfo.coef.compress_data(cinfo, main.buffer))
				{
					// If compressor did not consume the whole row, then we must need to
					// suspend processing and return to the application. In this situation
					// we pretend we didn't yet consume the last input row; otherwise, if
					// it happened to be the last row of the image, the application would
					// think we were done.
					if(!main.suspended)
					{
						in_row_ctr--;
						main.suspended=true;
					}
					return;
				}

				// We did finish the row. Undo our little suspension hack if a previous
				// call suspended; then mark the main buffer empty.
				if(main.suspended)
				{
					in_row_ctr++;
					main.suspended=false;
				}
				main.rowgroup_ctr=0;
				main.cur_iMCU_row++;
			} // while(...)
		}

		// Initialize main buffer controller.
		static void jinit_c_main_controller(jpeg_compress cinfo, bool need_full_buffer)
		{
			my_c_main_controller main=null;

			try
			{
				main=new my_c_main_controller();
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}
			cinfo.main=main;
			main.start_pass=start_pass_c_main;

			// We don't need to create a buffer in raw-data mode.
			if(cinfo.raw_data_in) return;

			// Create the buffer. It holds downsampled data, so each component
			// may be of a different size.
			if(need_full_buffer) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_BUFFER_MODE);
			else
			{
				uint DCT_size=cinfo.DCT_size;

				// Allocate a strip buffer for each component
				for(int ci=0; ci<cinfo.num_components; ci++)
				{
					jpeg_component_info compptr=cinfo.comp_info[ci];
					main.buffer[ci]=alloc_sarray(cinfo, compptr.width_in_blocks*DCT_size, (uint)(compptr.v_samp_factor*DCT_size));
				}
			}
		}
	}
}

