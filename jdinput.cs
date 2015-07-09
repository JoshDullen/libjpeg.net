// jdinput.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1991-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains input control logic for the JPEG decompressor.
// These routines are concerned with controlling the decompressor's input
// processing (marker reading and coefficient/difference decoding).
// The actual input reading is done in jdmarker.cs, jdhuff.cs, jdphuff.cs,
// and jdlhuff.cs.

using System;

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Private state
		class my_input_controller : jpeg_input_controller
		{
			public int inheaders; // Nonzero until first SOS is reached
		}

		// Routines to calculate various quantities related to the size of the image.

		// Called once, when first SOS marker is reached
		static void initial_setup_d_input(jpeg_decompress cinfo)
		{
			// Make sure image isn't bigger than I can handle
			if((int)cinfo.image_height>JPEG_MAX_DIMENSION||(int)cinfo.image_width>JPEG_MAX_DIMENSION) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_IMAGE_TOO_BIG, JPEG_MAX_DIMENSION);

			if(cinfo.process==J_CODEC_PROCESS.JPROC_LOSSLESS)
			{
				// If precision > compiled-in value, we must downscale
				if(cinfo.data_precision>BITS_IN_JSAMPLE)
					WARNMS2(cinfo, J_MESSAGE_CODE.JWRN_MUST_DOWNSCALE, cinfo.data_precision, BITS_IN_JSAMPLE);
			}
			else
			{ // Lossy processes
				// For now, precision must match compiled-in value...
				if(cinfo.data_precision!=BITS_IN_JSAMPLE) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_PRECISION, cinfo.data_precision);
			}

			// Check that number of components won't exceed internal array sizes
			if(cinfo.num_components>MAX_COMPONENTS) ERREXIT2(cinfo, J_MESSAGE_CODE.JERR_COMPONENT_COUNT, cinfo.num_components, MAX_COMPONENTS);

			// Compute maximum sampling factors; check factor validity
			cinfo.max_h_samp_factor=1;
			cinfo.max_v_samp_factor=1;
			for(int ci=0; ci<cinfo.num_components; ci++)
			{
				jpeg_component_info compptr=cinfo.comp_info[ci];
				if(compptr.h_samp_factor<=0||compptr.h_samp_factor>MAX_SAMP_FACTOR||compptr.v_samp_factor<=0||compptr.v_samp_factor>MAX_SAMP_FACTOR) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_SAMPLING);
				cinfo.max_h_samp_factor=Math.Max(cinfo.max_h_samp_factor, compptr.h_samp_factor);
				cinfo.max_v_samp_factor=Math.Max(cinfo.max_v_samp_factor, compptr.v_samp_factor);
			}

			// We initialize DCT_scaled_size and min_DCT_scaled_size to DCTSIZE.
			// In the full decompressor, this will be overridden by jdmaster.cs;
			// but in the transcoder, jdmaster.cs is not used, so we must do it here.
			cinfo.min_DCT_scaled_size=cinfo.DCT_size;

			// Compute dimensions of components
			for(int ci=0; ci<cinfo.num_components; ci++)
			{
				jpeg_component_info compptr=cinfo.comp_info[ci];
				compptr.DCT_scaled_size=(uint)cinfo.DCT_size;

				// Size in data units
				compptr.width_in_blocks=(uint)jdiv_round_up(cinfo.image_width*compptr.h_samp_factor, cinfo.max_h_samp_factor*cinfo.DCT_size);
				compptr.height_in_blocks=(uint) jdiv_round_up(cinfo.image_height*compptr.v_samp_factor, cinfo.max_v_samp_factor*cinfo.DCT_size);
				// downsampled_width and downsampled_height will also be overridden by
				// jdmaster.cs if we are doing full decompression. The transcoder library
				// doesn't use these values, but the calling application might.
				// Size in samples
				compptr.downsampled_width=(uint)jdiv_round_up(cinfo.image_width*compptr.h_samp_factor, cinfo.max_h_samp_factor);
				compptr.downsampled_height=(uint)jdiv_round_up(cinfo.image_height*compptr.v_samp_factor, cinfo.max_v_samp_factor);

				// Mark component needed, until color conversion says otherwise
				compptr.component_needed=true;

				// Mark no quantization table yet saved for component
				compptr.quant_table=null;
			}

			// Compute number of fully interleaved MCU rows.
			cinfo.total_iMCU_rows=(uint)jdiv_round_up(cinfo.image_height,cinfo.max_v_samp_factor*cinfo.DCT_size);

			// Decide whether file contains multiple scans
			if(cinfo.comps_in_scan<cinfo.num_components||cinfo.process==J_CODEC_PROCESS.JPROC_PROGRESSIVE) cinfo.inputctl.has_multiple_scans=true;
			else cinfo.inputctl.has_multiple_scans=false;
		}

		// Do computations that are needed before processing a JPEG scan
		// cinfo.comps_in_scan and cinfo.cur_comp_info[] were set from SOS marker
		static void per_scan_setup_d_input(jpeg_decompress cinfo)
		{
			if(cinfo.comps_in_scan==1)
			{
				// Noninterleaved (single-component) scan
				jpeg_component_info compptr=cinfo.cur_comp_info[0];

				// Overall image size in MCUs
				cinfo.MCUs_per_row=compptr.width_in_blocks;
				cinfo.MCU_rows_in_scan=compptr.height_in_blocks;

				// For noninterleaved scan, always one block per MCU
				compptr.MCU_width=1;
				compptr.MCU_height=1;
				compptr.MCU_blocks=1;
				compptr.MCU_sample_width=(int)compptr.DCT_scaled_size;
				compptr.last_col_width=1;

				// For noninterleaved scans, it is convenient to define last_row_height
				// as the number of block rows present in the last iMCU row.
				int tmp=(int)(compptr.height_in_blocks%compptr.v_samp_factor);
				if(tmp==0) tmp=compptr.v_samp_factor;
				compptr.last_row_height=tmp;

				// Prepare array describing MCU composition
				cinfo.blocks_in_MCU=1;
				cinfo.MCU_membership[0]=0;
			}
			else
			{
				// Interleaved (multi-component) scan
				if(cinfo.comps_in_scan<=0||cinfo.comps_in_scan>MAX_COMPS_IN_SCAN) ERREXIT2(cinfo, J_MESSAGE_CODE.JERR_COMPONENT_COUNT, cinfo.comps_in_scan, MAX_COMPS_IN_SCAN);

				// Overall image size in MCUs
				cinfo.MCUs_per_row=(uint)jdiv_round_up(cinfo.image_width, cinfo.max_h_samp_factor*cinfo.DCT_size);
				cinfo.MCU_rows_in_scan=(uint)jdiv_round_up(cinfo.image_height,cinfo.max_v_samp_factor*cinfo.DCT_size);

				cinfo.blocks_in_MCU=0;

				for(int ci=0; ci<cinfo.comps_in_scan; ci++)
				{
					jpeg_component_info compptr=cinfo.cur_comp_info[ci];

					// Sampling factors give # of blocks of component in each MCU
					compptr.MCU_width=compptr.h_samp_factor;
					compptr.MCU_height=compptr.v_samp_factor;
					compptr.MCU_blocks=(uint)(compptr.MCU_width*compptr.MCU_height);
					compptr.MCU_sample_width=(int)(compptr.MCU_width*compptr.DCT_scaled_size);

					// Figure number of non-dummy blocks in last MCU column & row
					int tmp=(int)(compptr.width_in_blocks%compptr.MCU_width);
					if(tmp==0) tmp=compptr.MCU_width;
					compptr.last_col_width=tmp;
					tmp=(int)(compptr.height_in_blocks%compptr.MCU_height);
					if(tmp==0) tmp=compptr.MCU_height;
					compptr.last_row_height=tmp;

					// Prepare array describing MCU composition
					int mcublks=(int)compptr.MCU_blocks;
					if(cinfo.blocks_in_MCU+mcublks>D_MAX_BLOCKS_IN_MCU) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_MCU_SIZE);
					while((mcublks--)>0)
					{
						cinfo.MCU_membership[cinfo.blocks_in_MCU++]=ci;
					}
				}
			}
		}
		
		// Initialize the input modules to read a scan of compressed data.
		// The first call to this is done by jdmaster.c after initializing
		// the entire decompressor (during jpeg_start_decompress).
		// Subsequent calls come from consume_markers, below.
		static void start_input_pass_d_input(jpeg_decompress cinfo)
		{
			per_scan_setup_d_input(cinfo);
			cinfo.coef.start_input_pass(cinfo);
			cinfo.inputctl.consume_input=cinfo.coef.consume_data;
		}

		// Finish up after inputting a compressed-data scan.
		// This is called by the coefficient controller after it's read all
		// the expected data of the scan.
		static void finish_input_pass_d_input(jpeg_decompress cinfo)
		{
			cinfo.inputctl.consume_input=consume_markers_d_input;
		}

		// Read JPEG markers before, between, or after compressed-data scans.
		// Change state as necessary when a new scan is reached.
		// Return value is JPEG_SUSPENDED, JPEG_REACHED_SOS, or JPEG_REACHED_EOI.
		//
		// The consume_input method pointer points either here or to the
		// coefficient controller's consume_data routine, depending on whether
		// we are reading a compressed data segment or inter-segment markers.
		//
		// Note: This function should NOT return a pseudo SOS marker (with zero
		// component number) to the caller.  A pseudo marker received by
		// read_markers is processed and then skipped for other markers.
		static CONSUME_INPUT consume_markers_d_input(jpeg_decompress cinfo)
		{
			my_input_controller inputctl=(my_input_controller)cinfo.inputctl;
			if(inputctl.eoi_reached) return CONSUME_INPUT.JPEG_REACHED_EOI; // After hitting EOI, read no further

			CONSUME_INPUT val=cinfo.marker.read_markers(cinfo);
			for(; ; ) // Loop to pass pseudo SOS marker
			{
				switch(val)
				{
					case CONSUME_INPUT.JPEG_REACHED_SOS: // Found SOS
						if(inputctl.inheaders!=0)
						{ // 1st SOS
							if(inputctl.inheaders==1)
							{
								initial_setup_d_input(cinfo);

								// Initialize the decompression codec. We need to do this here so that
								// any codec-specific fields and function pointers are available to
								// the rest of the library.
								jinit_d_codec(cinfo);
							}

							if(cinfo.comps_in_scan==0) // pseudo SOS marker
							{
								inputctl.inheaders=2;
								break;
							}

							inputctl.inheaders=0;
							// Note: start_input_pass must be called by jdmaster.cs
							// before any more input can be consumed. jdapimin.cs is
							// responsible for enforcing this sequencing.
						}
						else
						{ // 2nd or later SOS marker
							if(!inputctl.has_multiple_scans) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_EOI_EXPECTED); // Oops, I wasn't expecting this!
							if(cinfo.comps_in_scan==0) // unexpected pseudo SOS marker
								break;
							start_input_pass_d_input(cinfo);
						}
						return val;
					case CONSUME_INPUT.JPEG_REACHED_EOI: // Found EOI
						inputctl.eoi_reached=true;
						if(inputctl.inheaders!=0)
						{	// Tables-only datastream, apparently
							if(cinfo.marker.saw_SOF) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_SOF_NO_SOS);
						}
						else
						{
							// Prevent infinite loop in coef ctlr's decompress_data routine
							// if user set output_scan_number larger than number of scans.
							if(cinfo.output_scan_number>cinfo.input_scan_number) cinfo.output_scan_number=cinfo.input_scan_number;
						}
						return val;
					case CONSUME_INPUT.JPEG_SUSPENDED:
						return val;
				}
			}
		}

		// Reset state to begin a fresh datastream.
		static void reset_input_controller_d_input(jpeg_decompress cinfo)
		{
			my_input_controller inputctl=(my_input_controller)cinfo.inputctl;

			inputctl.consume_input=consume_markers_d_input;
			inputctl.has_multiple_scans=false; // "unknown" would be better
			inputctl.eoi_reached=false;
			inputctl.inheaders=1;

			// Reset other modules
			cinfo.err.reset_error_mgr(cinfo);
			cinfo.marker.reset_marker_reader(cinfo);

			// Reset progression state -- would be cleaner if entropy decoder did this
			cinfo.coef_bits=null;
		}

		// Initialize the input controller module.
		// This is called only once, when the decompression object is created.
		public static void jinit_input_controller(jpeg_decompress cinfo)
		{
			my_input_controller inputctl=null;

			// Create subobject in pool
			try
			{
				inputctl=new my_input_controller();
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}
			cinfo.inputctl=inputctl;

			// Initialize method pointers
			inputctl.consume_input=consume_markers_d_input;
			inputctl.reset_input_controller=reset_input_controller_d_input;
			inputctl.start_input_pass=start_input_pass_d_input;
			inputctl.finish_input_pass=finish_input_pass_d_input;

			// Initialize state: can't use reset_input_controller since we don't
			// want to try to reset other modules yet.
			inputctl.has_multiple_scans=false; // "unknown" would be better
			inputctl.eoi_reached=false;
			inputctl.inheaders=1;
		}
	}
}
