// jdapimin.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1994-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains application interface code for the decompression half
// of the JPEG library. These are the "minimum" API routines that may be
// needed in either the normal full-decompression case or the
// transcoding-only case.
//
// Most of the routines intended to be called directly by an application
// are in this file or in jdapistd.cs. But also see jcomapi.cs for routines
// shared by compression and decompression, and jdtrans.cs for the transcoding
// case.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Initialization of a JPEG decompression object.
		// The error manager must already be set up (in case memory manager fails).
		public static void jpeg_create_decompress(jpeg_decompress cinfo)
		{
			// For debugging purposes, we zero the whole master structure.
			// But the application has already set the err pointer, and may have set
			// client_data, so we have to save and restore those fields.
			// Note: if application hasn't set client_data, tools like Purify may
			// complain here.
			{
				cinfo.global_state=STATE.None;
				cinfo.image_width=cinfo.image_height=0;
				cinfo.num_components=0;
				cinfo.jpeg_color_space=cinfo.out_color_space=J_COLOR_SPACE.JCS_UNKNOWN;
				cinfo.output_gamma=0;
				cinfo.buffered_image=cinfo.raw_data_out=cinfo.do_fancy_upsampling=cinfo.do_block_smoothing=cinfo.quantize_colors=false;
				cinfo.dither_mode=J_DITHER_MODE.JDITHER_NONE;
				cinfo.two_pass_quantize=false;
				cinfo.desired_number_of_colors=0;
				cinfo.enable_1pass_quant=cinfo.enable_external_quant=cinfo.enable_2pass_quant=false;
				cinfo.output_width=cinfo.output_height=0;
				cinfo.out_color_components=cinfo.output_components=cinfo.rec_outbuf_height=cinfo.actual_number_of_colors=0;
				cinfo.colormap=null;
				cinfo.output_scanline=0;
				cinfo.input_scan_number=0;
				cinfo.input_iMCU_row=0;
				cinfo.output_scan_number=0;
				cinfo.output_iMCU_row=0;
				cinfo.coef_bits=new int[DCTSIZE2][];
				cinfo.data_precision=0;
				cinfo.comp_info=null;
				cinfo.arith_code=false;
				cinfo.arith_dc_L=new byte[NUM_ARITH_TBLS];
				cinfo.arith_dc_U=new byte[NUM_ARITH_TBLS];
				cinfo.arith_ac_K=new byte[NUM_ARITH_TBLS];
				cinfo.restart_interval=0;
				cinfo.saw_JFIF_marker=false;
				cinfo.JFIF_major_version=cinfo.JFIF_minor_version=0;
				cinfo.density_unit=0;
				cinfo.X_density=cinfo.Y_density=0;
				cinfo.saw_Adobe_marker=false;
				cinfo.Adobe_transform=0;
				cinfo.CCIR601_sampling=false;
				cinfo.marker_list=null;
				cinfo.DCT_size=0;
				cinfo.process=J_CODEC_PROCESS.JPROC_SEQUENTIAL;
				cinfo.max_h_samp_factor=cinfo.max_v_samp_factor=cinfo.min_DCT_scaled_size=0;
				cinfo.total_iMCU_rows=0;
				cinfo.sample_range_limit=null;
				cinfo.comps_in_scan=0;
				cinfo.cur_comp_info=new jpeg_component_info[MAX_COMPS_IN_SCAN];
				cinfo.MCUs_per_row=cinfo.MCU_rows_in_scan=0;
				cinfo.blocks_in_MCU=0;
				cinfo.MCU_membership=new int[D_MAX_BLOCKS_IN_MCU];
				cinfo.Ss=cinfo.Se=cinfo.Ah=cinfo.Al=cinfo.unread_marker=0;
				cinfo.master=null;
				cinfo.main=null;
				cinfo.coef=null;
				cinfo.post=null;
				cinfo.inputctl=null;
				cinfo.marker=null;
				cinfo.upsample=null;
				cinfo.cconvert=null;
				cinfo.cquantize=null;
			}
			cinfo.is_decompressor=true;

			// Zero out pointers to permanent structures.
			cinfo.progress=null;
			cinfo.src=null;

			for(int i=0; i<NUM_QUANT_TBLS; i++) cinfo.quant_tbl_ptrs[i]=null;

			for(int i=0; i<NUM_HUFF_TBLS; i++)
			{
				cinfo.dc_huff_tbl_ptrs[i]=null;
				cinfo.ac_huff_tbl_ptrs[i]=null;
			}

			// Initialize marker processor so application can override methods
			// for COM, APPn markers before calling jpeg_read_header.
			cinfo.marker_list=null;
			jinit_marker_reader(cinfo);

			// And initialize the overall input controller.
			jinit_input_controller(cinfo);

			// OK, I'm ready
			cinfo.global_state=STATE.DSTART;
		}

		// Destruction of a JPEG decompression object
		public static void jpeg_destroy_decompress(jpeg_decompress cinfo)
		{
			jpeg_destroy(cinfo); // use common routine
		}

		// Abort processing of a JPEG decompression operation,
		// but don't destroy the object itself.
		public static void jpeg_abort_decompress(jpeg_decompress cinfo)
		{
			jpeg_abort(cinfo); // use common routine
		}

		// Set default decompression parameters.
		static void default_decompress_parms(jpeg_decompress cinfo)
		{
			// Guess the input colorspace, and set output colorspace accordingly.
			// (Wish JPEG committee had provided a real way to specify this...)
			// Note application may override our guesses.
			switch(cinfo.num_components)
			{
				case 1:
					cinfo.jpeg_color_space=J_COLOR_SPACE.JCS_GRAYSCALE;
					cinfo.out_color_space=J_COLOR_SPACE.JCS_GRAYSCALE;
					break;
				case 3:
					if(cinfo.saw_JFIF_marker) cinfo.jpeg_color_space=J_COLOR_SPACE.JCS_YCbCr; // JFIF implies YCbCr
					else if(cinfo.saw_Adobe_marker)
					{
						switch(cinfo.Adobe_transform)
						{
							case 0: cinfo.jpeg_color_space=J_COLOR_SPACE.JCS_RGB; break;
							case 1: cinfo.jpeg_color_space=J_COLOR_SPACE.JCS_YCbCr; break;
							default:
								WARNMS1(cinfo, J_MESSAGE_CODE.JWRN_ADOBE_XFORM, cinfo.Adobe_transform);
								cinfo.jpeg_color_space=J_COLOR_SPACE.JCS_YCbCr; // assume it's YCbCr
								break;
						}
					}
					else
					{
						// Saw no special markers, try to guess from the component IDs
						int cid0=cinfo.comp_info[0].component_id;
						int cid1=cinfo.comp_info[1].component_id;
						int cid2=cinfo.comp_info[2].component_id;

						if(cid0==1&&cid1==2&&cid2==3) cinfo.jpeg_color_space=J_COLOR_SPACE.JCS_YCbCr; // assume JFIF w/out marker
						else if(cid0==82&&cid1==71&&cid2==66) cinfo.jpeg_color_space=J_COLOR_SPACE.JCS_RGB; // ASCII 'R', 'G', 'B'
						else
						{
							if(cinfo.process==J_CODEC_PROCESS.JPROC_LOSSLESS)
							{
								TRACEMS3(cinfo, 1, J_MESSAGE_CODE.JTRC_UNKNOWN_LOSSLESS_IDS, cid0, cid1, cid2);
								cinfo.jpeg_color_space=J_COLOR_SPACE.JCS_RGB; // assume it's RGB
							}
							else
							{ // Lossy processes
								TRACEMS3(cinfo, 1, J_MESSAGE_CODE.JTRC_UNKNOWN_LOSSY_IDS, cid0, cid1, cid2);
								cinfo.jpeg_color_space=J_COLOR_SPACE.JCS_YCbCr; // assume it's YCbCr
							}
						}
					}

					//Always guess RGB is proper output colorspace.
					cinfo.out_color_space=J_COLOR_SPACE.JCS_RGB;
					break;
				case 4:
					if(cinfo.saw_Adobe_marker)
					{
						switch(cinfo.Adobe_transform)
						{
							case 0:
								cinfo.jpeg_color_space=J_COLOR_SPACE.JCS_CMYK;
								break;
							case 2:
								cinfo.jpeg_color_space=J_COLOR_SPACE.JCS_YCCK;
								break;
							default:
								WARNMS1(cinfo, J_MESSAGE_CODE.JWRN_ADOBE_XFORM, cinfo.Adobe_transform);
								cinfo.jpeg_color_space=J_COLOR_SPACE.JCS_YCCK; // assume it's YCCK
								break;
						}
					}
					else cinfo.jpeg_color_space=J_COLOR_SPACE.JCS_CMYK; // No special markers, assume straight CMYK.
					cinfo.out_color_space=J_COLOR_SPACE.JCS_CMYK;
					break;
				default:
					cinfo.jpeg_color_space=J_COLOR_SPACE.JCS_UNKNOWN;
					cinfo.out_color_space=J_COLOR_SPACE.JCS_UNKNOWN;
					break;
			}

			// Set defaults for other decompression parameters.
			cinfo.output_gamma=1.0;
			cinfo.buffered_image=false;
			cinfo.raw_data_out=false;
			cinfo.do_fancy_upsampling=true;
			cinfo.do_block_smoothing=true;
			cinfo.quantize_colors=false;
			// We set these in case application only sets quantize_colors.
			cinfo.dither_mode=J_DITHER_MODE.JDITHER_FS;
#if QUANT_2PASS_SUPPORTED
			cinfo.two_pass_quantize=true;
#else
			cinfo.two_pass_quantize=false;
#endif
			cinfo.desired_number_of_colors=256;
			cinfo.colormap=null;

			// Initialize for no mode change in buffered-image mode.
			cinfo.enable_1pass_quant=false;
			cinfo.enable_external_quant=false;
			cinfo.enable_2pass_quant=false;
		}

		// Decompression startup: read start of JPEG datastream to see what's there.
		// Need only initialize JPEG object and supply a data source before calling.
		//
		// This routine will read as far as the first SOS marker (ie, actual start of
		// compressed data), and will save all tables and parameters in the JPEG
		// object. It will also initialize the decompression parameters to default
		// values, and finally return JPEG_HEADER_OK. On return, the application may
		// adjust the decompression parameters and then call jpeg_start_decompress.
		// (Or, if the application only wanted to determine the image parameters,
		// the data need not be decompressed. In that case, call jpeg_abort or
		// jpeg_destroy to release any temporary space.)
		// If an abbreviated (tables only) datastream is presented, the routine will
		// return JPEG_HEADER_TABLES_ONLY upon reaching EOI. The application may then
		// re-use the JPEG object to read the abbreviated image datastream(s).
		// It is unnecessary (but OK) to call jpeg_abort in this case.
		// The JPEG_SUSPENDED return code only occurs if the data source module
		// requests suspension of the decompressor. In this case the application
		// should load more source data and then re-call jpeg_read_header to resume
		// processing.
		// If a non-suspending data source is used and require_image is true, then the
		// return code need not be inspected since only JPEG_HEADER_OK is possible.
		//
		// This routine is now just a front end to jpeg_consume_input, with some
		// extra error checking.
		public static CONSUME_INPUT jpeg_read_header(jpeg_decompress cinfo, bool require_image)
		{
			if(cinfo.global_state!=STATE.DSTART&&cinfo.global_state!=STATE.DINHEADER) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_STATE, cinfo.global_state);

			CONSUME_INPUT retcode=jpeg_consume_input(cinfo);

			switch(retcode)
			{
				case CONSUME_INPUT.JPEG_REACHED_SOS: return CONSUME_INPUT.JPEG_HEADER_OK;
				case CONSUME_INPUT.JPEG_REACHED_EOI:
					if(require_image) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_NO_IMAGE); // Complain if application wanted an image

					// Reset to start state; it would be safer to require the application to
					// call jpeg_abort, but we can't change it now for compatibility reasons.
					// A side effect is to free any temporary memory (there shouldn't be any).
					jpeg_abort(cinfo); // sets state=DSTART
					return CONSUME_INPUT.JPEG_HEADER_TABLES_ONLY;
			}

			return retcode;
		}

		// Consume data in advance of what the decompressor requires.
		// This can be called at any time once the decompressor object has
		// been created and a data source has been set up.
		//
		// This routine is essentially a state machine that handles a couple
		// of critical state-transition actions, namely initial setup and
		// transition from header scanning to ready-for-start_decompress.
		// All the actual input is done via the input controller's consume_input
		// method.
		public static CONSUME_INPUT jpeg_consume_input(jpeg_decompress cinfo)
		{
			CONSUME_INPUT retcode=CONSUME_INPUT.JPEG_SUSPENDED;

			// NB: every possible DSTATE value should be listed in this switch
			switch(cinfo.global_state)
			{
				case STATE.DSTART:
					// Start-of-datastream actions: reset appropriate modules
					cinfo.inputctl.reset_input_controller(cinfo);
					// Initialize application's data source module
					cinfo.src.init_source(cinfo);
					cinfo.global_state=STATE.DINHEADER;
					goto case STATE.DINHEADER; // FALLTHROUGH
				case STATE.DINHEADER:
					retcode=cinfo.inputctl.consume_input(cinfo);
					if(retcode==CONSUME_INPUT.JPEG_REACHED_SOS)
					{ // Found SOS, prepare to decompress
						// Set up default parameters based on header data
						default_decompress_parms(cinfo);

						// Set global state: ready for start_decompress
						cinfo.global_state=STATE.DREADY;
					}
					break;
				case STATE.DREADY: retcode=CONSUME_INPUT.JPEG_REACHED_SOS; break;// Can't advance past first SOS until start_decompress is called
				case STATE.DPRELOAD:
				case STATE.DPRESCAN:
				case STATE.DSCANNING:
				case STATE.DRAW_OK:
				case STATE.DBUFIMAGE:
				case STATE.DBUFPOST:
				case STATE.DSTOPPING: retcode=cinfo.inputctl.consume_input(cinfo); break;
				default: ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_STATE, cinfo.global_state); break;
			}
			return retcode;
		}

		// Have we finished reading the input file?
		public static bool jpeg_input_complete(jpeg_decompress cinfo)
		{
			// Check for valid jpeg object
			if(cinfo.global_state<STATE.DSTART||cinfo.global_state>STATE.DSTOPPING) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_STATE, cinfo.global_state);
			return cinfo.inputctl.eoi_reached;
		}

		// Is there more than one scan?
		public static bool jpeg_has_multiple_scans(jpeg_decompress cinfo)
		{
			// Only valid after jpeg_read_header completes
			if(cinfo.global_state<STATE.DREADY||cinfo.global_state>STATE.DSTOPPING) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_STATE, cinfo.global_state);
			return cinfo.inputctl.has_multiple_scans;
		}

		// Finish JPEG decompression.
		//
		// This will normally just verify the file trailer and release temp storage.
		//
		// Returns false if suspended. The return value need be inspected only if
		// a suspending data source is used.
		public static bool jpeg_finish_decompress(jpeg_decompress cinfo)
		{
			if((cinfo.global_state==STATE.DSCANNING||cinfo.global_state==STATE.DRAW_OK)&&!cinfo.buffered_image)
			{
				// Terminate final pass of non-buffered mode
				if(cinfo.output_scanline<cinfo.output_height) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_TOO_LITTLE_DATA);
				cinfo.master.finish_output_pass(cinfo);
				cinfo.global_state=STATE.DSTOPPING;
			}
			else if(cinfo.global_state==STATE.DBUFIMAGE)
			{
				// Finishing after a buffered-image operation
				cinfo.global_state=STATE.DSTOPPING;
			}
			else if(cinfo.global_state!=STATE.DSTOPPING)
			{
				// STOPPING = repeat call after a suspension, anything else is error
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_STATE, cinfo.global_state);
			}

			// Read until EOI
			while(!cinfo.inputctl.eoi_reached)
			{
				if(cinfo.inputctl.consume_input(cinfo)==CONSUME_INPUT.JPEG_SUSPENDED) return false; // Suspend, come back later
			}

			// Do final cleanup
			cinfo.src.term_source(cinfo, cinfo.readExtraBytesAtEndOfStream, out cinfo.extraBytesAtEndOfStream);

			// We can use jpeg_abort to release memory and reset global_state
			jpeg_abort(cinfo);
			return true;
		}
	}
}
