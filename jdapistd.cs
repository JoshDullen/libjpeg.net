// jdapistd.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1994-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains application interface code for the decompression half
// of the JPEG library. These are the "standard" API routines that are
// used in the normal full-decompression case. They are not used by a
// transcoding-only application.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Decompression initialization.
		// jpeg_read_header must be completed before calling this.
		//
		// If a multipass operating mode was selected, this will do all but the
		// last pass, and thus may take a great deal of time.
		//
		// Returns false if suspended. The return value need be inspected only if
		// a suspending data source is used.
		public static bool jpeg_start_decompress(jpeg_decompress cinfo)
		{
			if(cinfo.global_state==STATE.DREADY)
			{
				// First call: initialize master control, select active modules
				jinit_master_decompress(cinfo);
				if(cinfo.buffered_image)
				{
					// No more work here; expecting jpeg_start_output next
					cinfo.global_state=STATE.DBUFIMAGE;
					return true;
				}
				cinfo.global_state=STATE.DPRELOAD;
			}

			if(cinfo.global_state==STATE.DPRELOAD)
			{
				// If file has multiple scans, absorb them all into the coef buffer
				if(cinfo.inputctl.has_multiple_scans)
				{
#if D_MULTISCAN_FILES_SUPPORTED
					for(; ; )
					{
						// Call progress monitor hook if present
						if(cinfo.progress!=null) cinfo.progress.progress_monitor(cinfo);
						// Absorb some more input
						CONSUME_INPUT retcode=cinfo.inputctl.consume_input(cinfo);
						if(retcode==CONSUME_INPUT.JPEG_SUSPENDED) return false;
						if(retcode==CONSUME_INPUT.JPEG_REACHED_EOI) break;

						// Advance progress counter if appropriate
						if(cinfo.progress!=null&&(retcode==CONSUME_INPUT.JPEG_ROW_COMPLETED||retcode==CONSUME_INPUT.JPEG_REACHED_SOS))
						{
							if(++cinfo.progress.pass_counter>=cinfo.progress.pass_limit)
							{
								// jdmaster underestimated number of scans; ratchet up one scan
								cinfo.progress.pass_limit+=(int)cinfo.total_iMCU_rows;
							}
						}
					}
#else
					ERREXIT(cinfo, J_MESSAGE_CODE.JERR_NOT_COMPILED);
#endif // D_MULTISCAN_FILES_SUPPORTED
				}
				cinfo.output_scan_number=cinfo.input_scan_number;
			}
			else if(cinfo.global_state!=STATE.DPRESCAN) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_STATE, cinfo.global_state);

			// Perform any dummy output passes, and set up for the final pass
			return output_pass_setup(cinfo);
		}

		// Set up for an output pass, and perform any dummy pass(es) needed.
		// Common subroutine for jpeg_start_decompress and jpeg_start_output.
		// Entry: global_state = DPRESCAN only if previously suspended.
		// Exit:	If done, returns true and sets global_state for proper output mode.
		//			If suspended, returns false and sets global_state = DPRESCAN.
		static bool output_pass_setup(jpeg_decompress cinfo)
		{
			if(cinfo.global_state!=STATE.DPRESCAN)
			{
				// First call: do pass setup
				cinfo.master.prepare_for_output_pass(cinfo);
				cinfo.output_scanline=0;
				cinfo.global_state=STATE.DPRESCAN;
			}

			// Loop over any required dummy passes
			while(cinfo.master.is_dummy_pass)
			{
#if QUANT_2PASS_SUPPORTED
				// Crank through the dummy pass
				while(cinfo.output_scanline<cinfo.output_height)
				{
					// Call progress monitor hook if present
					if(cinfo.progress!=null)
					{
						cinfo.progress.pass_counter=(int)cinfo.output_scanline;
						cinfo.progress.pass_limit=(int)cinfo.output_height;
						cinfo.progress.progress_monitor(cinfo);
					}

					// Process some data
					uint last_scanline=cinfo.output_scanline;
					cinfo.main.process_data(cinfo, null, ref cinfo.output_scanline, 0);
					if(cinfo.output_scanline==last_scanline) return false; // No progress made, must suspend
				}

				// Finish up dummy pass, and set up for another one
				cinfo.master.finish_output_pass(cinfo);
				cinfo.master.prepare_for_output_pass(cinfo);
				cinfo.output_scanline=0;
#else
				ERREXIT(cinfo, J_MESSAGE_CODE.JERR_NOT_COMPILED);
#endif // QUANT_2PASS_SUPPORTED
			}

			// Ready for application to drive output pass through
			// jpeg_read_scanlines or jpeg_read_raw_data.
			cinfo.global_state=cinfo.raw_data_out?STATE.DRAW_OK:STATE.DSCANNING;
			return true;
		}

		// Read some scanlines of data from the JPEG decompressor.
		//
		// The return value will be the number of lines actually read.
		// This may be less than the number requested in several cases,
		// including bottom of image, data source suspension, and operating
		// modes that emit multiple scanlines at a time.
		//
		// Note: we warn about excess calls to jpeg_read_scanlines() since
		// this likely signals an application programmer error. However,
		// an oversize buffer (max_lines > scanlines remaining) is not an error.
		public static uint jpeg_read_scanlines(jpeg_decompress cinfo, byte[][] scanlines, uint max_lines)
		{
			if(cinfo.global_state!=STATE.DSCANNING) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_STATE, cinfo.global_state);
			if(cinfo.output_scanline>=cinfo.output_height)
			{
				WARNMS(cinfo, J_MESSAGE_CODE.JWRN_TOO_MUCH_DATA);
				return 0;
			}

			// Call progress monitor hook if present
			if(cinfo.progress!=null)
			{
				cinfo.progress.pass_counter=(int)cinfo.output_scanline;
				cinfo.progress.pass_limit=(int)cinfo.output_height;
				cinfo.progress.progress_monitor(cinfo);
			}

			// Process some data
			uint row_ctr=0;
			cinfo.main.process_data(cinfo, scanlines, ref row_ctr, max_lines);
			cinfo.output_scanline+=row_ctr;
			return row_ctr;
		}

		// Alternate entry point to read raw data.
		// Processes exactly one iMCU row per call, unless suspended.
		public static uint jpeg_read_raw_data(jpeg_decompress cinfo, byte[][][] data, uint max_lines)
		{
			if(cinfo.global_state!=STATE.DRAW_OK) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_STATE, cinfo.global_state);
			if(cinfo.output_scanline>=cinfo.output_height)
			{
				WARNMS(cinfo, J_MESSAGE_CODE.JWRN_TOO_MUCH_DATA);
				return 0;
			}

			// Call progress monitor hook if present
			if(cinfo.progress!=null)
			{
				cinfo.progress.pass_counter=(int)cinfo.output_scanline;
				cinfo.progress.pass_limit=(int)cinfo.output_height;
				cinfo.progress.progress_monitor(cinfo);
			}

			// Verify that at least one iMCU row can be returned.
			uint lines_per_iMCU_row=(uint)(cinfo.max_v_samp_factor*cinfo.min_DCT_scaled_size);
			if(max_lines<lines_per_iMCU_row) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BUFFER_SIZE);

			// Decompress directly into user's buffer.
			if(cinfo.coef.decompress_data(cinfo, data)==CONSUME_INPUT.JPEG_SUSPENDED) return 0; // suspension forced, can do nothing more

			// OK, we processed one iMCU row.
			cinfo.output_scanline+=lines_per_iMCU_row;
			return lines_per_iMCU_row;
		}

		// Additional entry points for buffered-image mode.
#if D_MULTISCAN_FILES_SUPPORTED
		// Initialize for an output pass in buffered-image mode.
		public static bool jpeg_start_output(jpeg_decompress cinfo, int scan_number)
		{
			if(cinfo.global_state!=STATE.DBUFIMAGE&&cinfo.global_state!=STATE.DPRESCAN) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_STATE, cinfo.global_state);

			// Limit scan number to valid range
			if(scan_number<=0) scan_number=1;
			if(cinfo.inputctl.eoi_reached&&scan_number>cinfo.input_scan_number) scan_number=cinfo.input_scan_number;
			cinfo.output_scan_number=scan_number;

			// Perform any dummy output passes, and set up for the real pass
			return output_pass_setup(cinfo);
		}

		// Finish up after an output pass in buffered-image mode.
		//
		// Returns false if suspended. The return value need be inspected only if
		// a suspending data source is used.
		public static bool jpeg_finish_output(jpeg_decompress cinfo)
		{
			if((cinfo.global_state==STATE.DSCANNING||cinfo.global_state==STATE.DRAW_OK)&&cinfo.buffered_image)
			{
				// Terminate this pass.
				// We do not require the whole pass to have been completed.
				cinfo.master.finish_output_pass(cinfo);
				cinfo.global_state=STATE.DBUFPOST;
			}
			else if(cinfo.global_state!=STATE.DBUFPOST)
			{
				// BUFPOST = repeat call after a suspension, anything else is error
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_STATE, cinfo.global_state);
			}

			// Read markers looking for SOS or EOI
			while(cinfo.input_scan_number<=cinfo.output_scan_number&&!cinfo.inputctl.eoi_reached)
			{
				if(cinfo.inputctl.consume_input(cinfo)==CONSUME_INPUT.JPEG_SUSPENDED) return false; // Suspend, come back later
			}
			cinfo.global_state=STATE.DBUFIMAGE;
			return true;
		}
#endif // D_MULTISCAN_FILES_SUPPORTED
	}
}
