// jcapimin.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1994-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains application interface code for the compression half
// of the JPEG library. These are the "minimum" API routines that may be
// needed in either the normal full-compression case or the transcoding-only
// case.
//
// Most of the routines intended to be called directly by an application
// are in this file or in jcapistd.cs. But also see jcparam.cs for
// parameter-setup helper routines, jcomapi.cs for routines shared by
// compression and decompression, and jctrans.cs for the transcoding case.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Initialization of a JPEG compression object.
		// The error manager must already be set up (in case memory manager fails).
		public static void jpeg_create_compress(jpeg_compress cinfo)
		{
			// For debugging purposes, we zero the whole master structure.
			// But the application has already set the err pointer, and may have set
			// client_data, so we have to save and restore those fields.
			// Note: if application hasn't set client_data, tools like Purify may
			// complain here.
			{
				cinfo.global_state=STATE.None;
				cinfo.image_width=cinfo.image_height=0;
				cinfo.input_components=0;
				cinfo.in_color_space=J_COLOR_SPACE.JCS_UNKNOWN;
				cinfo.lossless=false;
				cinfo.data_precision=cinfo.num_components=0;
				cinfo.jpeg_color_space=J_COLOR_SPACE.JCS_UNKNOWN;
				cinfo.arith_dc_L=new byte[NUM_ARITH_TBLS];
				cinfo.arith_dc_U=new byte[NUM_ARITH_TBLS];
				cinfo.arith_ac_K=new byte[NUM_ARITH_TBLS];
				cinfo.num_scans=0;
				cinfo.scan_info=null;
				cinfo.raw_data_in=cinfo.arith_code=cinfo.optimize_coding=cinfo.CCIR601_sampling=false;
				cinfo.smoothing_factor=0;
#if DCT_FLOAT_SUPPORTED
				cinfo.useFloatDCT=false;
#endif
				cinfo.restart_interval=0;
				cinfo.restart_in_rows=0;
				cinfo.write_JFIF_header=false;
				cinfo.JFIF_major_version=cinfo.JFIF_minor_version=0;
				cinfo.density_unit=0;
				cinfo.X_density=cinfo.Y_density=0;
				cinfo.write_Adobe_marker=false;
				cinfo.next_scanline=cinfo.DCT_size=0;
				cinfo.process=J_CODEC_PROCESS.JPROC_SEQUENTIAL;
				cinfo.max_h_samp_factor=cinfo.max_v_samp_factor=0;
				cinfo.total_iMCU_rows=0;
				cinfo.comps_in_scan=0;
				cinfo.cur_comp_info=new jpeg_component_info[MAX_COMPS_IN_SCAN];
				cinfo.MCUs_per_row=cinfo.MCU_rows_in_scan=0;
				cinfo.block_in_MCU=0;
				cinfo.MCU_membership=new int[C_MAX_BLOCKS_IN_MCU];
				cinfo.Ss=cinfo.Se=cinfo.Ah=cinfo.Al=0;
				cinfo.master=null;
				cinfo.main=null;
				cinfo.prep=null;
				cinfo.coef=null;
				cinfo.marker=null;
				cinfo.cconvert=null;
				cinfo.downsample=null;
				cinfo.script_space_size=0;
			}
			cinfo.is_decompressor=false;

			// Zero out pointers to permanent structures.
			cinfo.progress=null;
			cinfo.dest=null;

			cinfo.comp_info=null;

			for(int i=0; i<NUM_QUANT_TBLS; i++)
			{
				cinfo.quant_tbl_ptrs[i]=null;
				// TODO8 cinfo.q_scale_factor[i]=100;
			}

			for(int i=0; i<NUM_HUFF_TBLS; i++)
			{
				cinfo.dc_huff_tbl_ptrs[i]=null;
				cinfo.ac_huff_tbl_ptrs[i]=null;
			}

			cinfo.script_space=null;
			cinfo.input_gamma=1.0;	// in case application forgets

			// OK, I'm ready
			cinfo.global_state=STATE.CSTART;
		}

		// Destruction of a JPEG compression object
		public static void jpeg_destroy_compress(jpeg_compress cinfo)
		{
			jpeg_destroy(cinfo); // use common routine
		}

		// Abort processing of a JPEG compression operation,
		// but don't destroy the object itself.
		public static void jpeg_abort_compress(jpeg_compress cinfo)
		{
			jpeg_abort(cinfo); // use common routine
		}
		
		// Forcibly suppress or un-suppress all quantization and Huffman tables.
		// Marks all currently defined tables as already written (if suppress)
		// or not written (if !suppress). This will control whether they get emitted
		// by a subsequent jpeg_start_compress call.
		//
		// This routine is exported for use by applications that want to produce
		// abbreviated JPEG datastreams. It logically belongs in jcparam.cs, but
		// since it is called by jpeg_start_compress, we put it here.
		public static void jpeg_suppress_tables(jpeg_compress cinfo, bool suppress)
		{
			JQUANT_TBL qtbl;
			JHUFF_TBL htbl;

			for(int i=0; i<NUM_QUANT_TBLS; i++)
			{
				if((qtbl=cinfo.quant_tbl_ptrs[i])!=null) qtbl.sent_table=suppress;
			}

			for(int i=0; i<NUM_HUFF_TBLS; i++)
			{
				if((htbl=cinfo.dc_huff_tbl_ptrs[i])!=null) htbl.sent_table=suppress;
				if((htbl=cinfo.ac_huff_tbl_ptrs[i])!=null) htbl.sent_table=suppress;
			}
		}

		// Finish JPEG compression.
		//
		// If a multipass operating mode was selected, this may do a great deal of
		// work including most of the actual output.
		public static void jpeg_finish_compress(jpeg_compress cinfo)
		{
			if(cinfo.global_state==STATE.CSCANNING||cinfo.global_state==STATE.CRAW_OK)
			{
				// Terminate first pass
				if(cinfo.next_scanline<cinfo.image_height) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_TOO_LITTLE_DATA);
				cinfo.master.finish_pass(cinfo);
			}
			else if(cinfo.global_state!=STATE.CWRCOEFS) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_STATE, cinfo.global_state);

			// Perform any remaining passes
			while(!cinfo.master.is_last_pass)
			{
				cinfo.master.prepare_for_pass(cinfo);
				for(uint iMCU_row=0; iMCU_row<cinfo.total_iMCU_rows; iMCU_row++)
				{
					if(cinfo.progress!=null)
					{
						cinfo.progress.pass_counter=(int)iMCU_row;
						cinfo.progress.pass_limit=(int)cinfo.total_iMCU_rows;
						cinfo.progress.progress_monitor(cinfo);
					}
					// We bypass the main controller and invoke coef controller directly;
					// all work is being done from the coefficient buffer.
					if(!cinfo.coef.compress_data(cinfo, null)) ERREXIT(cinfo,J_MESSAGE_CODE.JERR_CANT_SUSPEND);
				}
				cinfo.master.finish_pass(cinfo);
			}

			// Write EOI, do final cleanup
			cinfo.marker.write_file_trailer(cinfo);
			cinfo.dest.term_destination(cinfo);

			// We can use jpeg_abort to release memory and reset global_state
			jpeg_abort(cinfo);
		}

		// Write a special marker.
		// This is only recommended for writing COM or APPn markers.
		// Must be called after jpeg_start_compress() and before
		// first call to jpeg_write_scanlines() or jpeg_write_raw_data().
		public static void jpeg_write_marker(jpeg_compress cinfo, int marker, byte[] dataptr_buf, uint datalen)
		{
			if(cinfo.next_scanline!=0||(cinfo.global_state!=STATE.CSCANNING&&cinfo.global_state!=STATE.CRAW_OK&&cinfo.global_state!=STATE.CWRCOEFS))
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_STATE, cinfo.global_state);

			cinfo.marker.write_marker_header(cinfo, marker, datalen);
			void_jpeg_compress_int_Handler write_marker_byte=cinfo.marker.write_marker_byte;	// copy for speed
			for(int dataptr=0; dataptr<datalen; dataptr++) write_marker_byte(cinfo, dataptr_buf[dataptr]);
		}

		// Same, but piecemeal.
		public static void jpeg_write_m_header(jpeg_compress cinfo, int marker, uint datalen)
		{
			if(cinfo.next_scanline!=0||(cinfo.global_state!=STATE.CSCANNING&&cinfo.global_state!=STATE.CRAW_OK&&cinfo.global_state!=STATE.CWRCOEFS))
				ERREXIT1(cinfo,J_MESSAGE_CODE.JERR_BAD_STATE, cinfo.global_state);

			cinfo.marker.write_marker_header(cinfo, marker, datalen);
		}

		public static void jpeg_write_m_byte(jpeg_compress cinfo, int val)
		{
			cinfo.marker.write_marker_byte(cinfo, val);
		}

		// Alternate compression function: just write an abbreviated table file.
		// Before calling this, all parameters and a data destination must be set up.
		//
		// To produce a pair of files containing abbreviated tables and abbreviated
		// image data, one would proceed as follows:
		//
		//		initialize JPEG object
		//		set JPEG parameters
		//		set destination to table file
		//		jpeg_write_tables(cinfo);
		//		set destination to image file
		//		jpeg_start_compress(cinfo, false);
		//		write data...
		//		jpeg_finish_compress(cinfo);
		//
		// jpeg_write_tables has the side effect of marking all tables written
		// (same as jpeg_suppress_tables(..., true)). Thus a subsequent start_compress
		// will not re-emit the tables unless it is passed write_all_tables=true.
		public static void jpeg_write_tables(jpeg_compress cinfo)
		{
			if(cinfo.global_state!=STATE.CSTART)
				ERREXIT1(cinfo,J_MESSAGE_CODE.JERR_BAD_STATE, cinfo.global_state);

			// (Re)initialize error mgr and destination modules
			cinfo.err.reset_error_mgr(cinfo);
			cinfo.dest.init_destination(cinfo);

			// Initialize the marker writer ... bit of a crock to do it here.
			jinit_marker_writer(cinfo);

			// Write them tables!
			cinfo.marker.write_tables_only(cinfo);

			// And clean up.
			cinfo.dest.term_destination(cinfo);
			
			// In library releases up through v6a, we called jpeg_abort() here to free
			// any working memory allocated by the destination manager and marker
			// writer. Some applications had a problem with that: they allocated space
			// of their own from the library memory manager, and didn't want it to go
			// away during write_tables. So now we do nothing. This will cause a
			// memory leak if an app calls write_tables repeatedly without doing a full
			// compression cycle or otherwise resetting the JPEG object. However, that
			// seems less bad than unexpectedly freeing memory in the normal case.
			// An app that prefers the old behavior can call jpeg_abort for itself after
			// each call to jpeg_write_tables().
		}
	}
}
