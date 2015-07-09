// jcmaster.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1997-1998, Guido Vollbeding <guivol@esc.de>.
// Copyright (C) 1991-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains master control logic for the JPEG compressor.
// These routines are concerned with parameter validation, initial setup,
// and inter-pass control (determining the number of passes and the work 
// to be done in each pass).

#if C_MULTISCAN_FILES_SUPPORTED
	#define NEED_SCAN_SCRIPT
#else
	#if C_LOSSLESS_SUPPORTED
		#define NEED_SCAN_SCRIPT
	#endif
#endif

using System;

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Private state
		enum c_pass_type
		{
			main_pass,		// input data, also do first output step
			huff_opt_pass,	// Huffman code optimization pass
			output_pass		// data output pass
		}

		class my_comp_master : jpeg_comp_master
		{
			public c_pass_type pass_type;	// the type of the current pass

			public int pass_number;			// # of passes completed
			public int total_passes;		// total # of passes needed

			public int scan_number;			// current index in scan_info[]
		}

		// Support routines that do various essential calculations.

		// Do computations that are needed before master selection phase
		static void initial_setup(jpeg_compress cinfo)
		{
			// Sanity check on image dimensions
			if(cinfo.image_height<=0||cinfo.image_width<=0||cinfo.num_components<=0||cinfo.input_components<=0)
				ERREXIT(cinfo, J_MESSAGE_CODE.JERR_EMPTY_IMAGE);

			// Make sure image isn't bigger than I can handle
			if(cinfo.image_height>JPEG_MAX_DIMENSION||cinfo.image_width>JPEG_MAX_DIMENSION)
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_IMAGE_TOO_BIG, JPEG_MAX_DIMENSION);

			// Width of an input scanline must be representable as uint.
			long samplesperrow=cinfo.image_width*cinfo.input_components;
			if(samplesperrow<0||samplesperrow>uint.MaxValue) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_WIDTH_OVERFLOW);

			// For now, precision must match compiled-in value...
			if(cinfo.data_precision!=BITS_IN_JSAMPLE) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_PRECISION, cinfo.data_precision);

			// Check that number of components won't exceed internal array sizes
			if(cinfo.num_components>MAX_COMPONENTS) ERREXIT2(cinfo, J_MESSAGE_CODE.JERR_COMPONENT_COUNT, cinfo.num_components, MAX_COMPONENTS);

			// Compute maximum sampling factors; check factor validity
			cinfo.max_h_samp_factor=1;
			cinfo.max_v_samp_factor=1;
			for(int ci=0; ci<cinfo.num_components; ci++)
			{
				jpeg_component_info compptr=cinfo.comp_info[ci];
				if(compptr.h_samp_factor<=0||compptr.h_samp_factor>MAX_SAMP_FACTOR||compptr.v_samp_factor<=0||compptr.v_samp_factor>MAX_SAMP_FACTOR)
					ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_SAMPLING);
				cinfo.max_h_samp_factor=Math.Max(cinfo.max_h_samp_factor, compptr.h_samp_factor);
				cinfo.max_v_samp_factor=Math.Max(cinfo.max_v_samp_factor, compptr.v_samp_factor);
			}

			// Compute dimensions of components
			uint DCT_size=cinfo.DCT_size;
			for(int ci=0; ci<cinfo.num_components; ci++)
			{
				jpeg_component_info compptr=cinfo.comp_info[ci];
				// Fill in the correct component_index value; don't rely on application
				compptr.component_index=ci;
				// For compression, we never do any codec-based processing.
				compptr.DCT_scaled_size=DCT_size;
				// Size in blocks
				compptr.width_in_blocks=(uint)jdiv_round_up(cinfo.image_width*compptr.h_samp_factor, cinfo.max_h_samp_factor*DCT_size);
				compptr.height_in_blocks=(uint)jdiv_round_up(cinfo.image_height*compptr.v_samp_factor, cinfo.max_v_samp_factor*DCT_size);
				// Size in samples
				compptr.downsampled_width=(uint)jdiv_round_up(cinfo.image_width*compptr.h_samp_factor, cinfo.max_h_samp_factor);
				compptr.downsampled_height=(uint)jdiv_round_up(cinfo.image_height*compptr.v_samp_factor, cinfo.max_v_samp_factor);
				// Mark component needed (this flag isn't actually used for compression)
				compptr.component_needed=true;
			}

			// Compute number of fully interleaved MCU rows (number of times that
			// main controller will call coefficient controller).
			cinfo.total_iMCU_rows=(uint)jdiv_round_up(cinfo.image_height, cinfo.max_v_samp_factor*DCT_size);
		}

#if NEED_SCAN_SCRIPT
		// Verify that the scan script in cinfo.scan_info[] is valid; also
		// determine whether it uses progressive JPEG, and set cinfo.process.
		static void validate_script(jpeg_compress cinfo)
		{
#if C_PROGRESSIVE_SUPPORTED
			int[,] last_bitpos=new int[MAX_COMPONENTS, DCTSIZE2];
			// -1 until that coefficient has been seen; then last Al for it
#endif
			if(cinfo.num_scans<=0) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_SCAN_SCRIPT, 0);

#if !C_MULTISCAN_FILES_SUPPORTED
			if(cinfo.num_scans>1) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_NOT_COMPILED);
#endif

			bool[] component_sent=new bool[MAX_COMPONENTS];
			if(cinfo.lossless)
			{
#if C_LOSSLESS_SUPPORTED
				cinfo.process=J_CODEC_PROCESS.JPROC_LOSSLESS;
				for(int ci=0; ci<cinfo.num_components; ci++) component_sent[ci]=false;
#else
				ERREXIT(cinfo, J_MESSAGE_CODE.JERR_NOT_COMPILED);
#endif
			}
			// For sequential JPEG, all scans must have Ss=0, Se=DCTSIZE2-1;
			// for progressive JPEG, no scan can have this.
			else if(cinfo.scan_info[0].Ss!=0||cinfo.scan_info[0].Se!=DCTSIZE2-1)
			{
#if C_PROGRESSIVE_SUPPORTED

				cinfo.process=J_CODEC_PROCESS.JPROC_PROGRESSIVE;
				for(int ci=0; ci<cinfo.num_components; ci++)
					for(int coefi=0; coefi<DCTSIZE2; coefi++) last_bitpos[ci, coefi]=-1;
#else
				ERREXIT(cinfo, J_MESSAGE_CODE.JERR_NOT_COMPILED);
#endif
			}
			else
			{
				cinfo.process=J_CODEC_PROCESS.JPROC_SEQUENTIAL;
				for(int ci=0; ci<cinfo.num_components; ci++) component_sent[ci]=false;
			}

			for(int scanno=1; scanno<=cinfo.num_scans; scanno++)
			{
				jpeg_scan_info scan_info=cinfo.scan_info[scanno-1];

				// Validate component indexes
				int ncomps=scan_info.comps_in_scan;
				if(ncomps<=0||ncomps>MAX_COMPS_IN_SCAN) ERREXIT2(cinfo, J_MESSAGE_CODE.JERR_COMPONENT_COUNT, ncomps, MAX_COMPS_IN_SCAN);
				for(int ci=0; ci<ncomps; ci++)
				{
					int thisi=scan_info.component_index[ci];
					if(thisi<0||thisi>=cinfo.num_components) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_SCAN_SCRIPT, scanno);

					// Components must appear in SOF order within each scan
					if(ci>0&&thisi<=scan_info.component_index[ci-1]) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_SCAN_SCRIPT, scanno);
				}

				// Validate progression parameters
				int Ss=scan_info.Ss;
				int Se=scan_info.Se;
				int Ah=scan_info.Ah;
				int Al=scan_info.Al;
				if(cinfo.process==J_CODEC_PROCESS.JPROC_LOSSLESS)
				{
#if C_LOSSLESS_SUPPORTED
					// The JPEG spec simply gives the range 0..15 for Al (Pt), but that
					// seems wrong: the upper bound ought to depend on data precision.
					// Perhaps they really meant 0..N-1 for N-bit precision, which is what
					// we allow here.
					if(Ss<1||Ss>7||Se!=0||Ah!=0||Al<0||Al>=cinfo.data_precision) // Ss predictor selector; Al point transform
						ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_LOSSLESS_SCRIPT, scanno);

					// Make sure components are not sent twice
					for(int ci=0; ci<ncomps; ci++)
					{
						int thisi=scan_info.component_index[ci];
						if(component_sent[thisi]) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_SCAN_SCRIPT, scanno);
						component_sent[thisi]=true;
					}
#endif
				}
				else if(cinfo.process==J_CODEC_PROCESS.JPROC_PROGRESSIVE)
				{
#if C_PROGRESSIVE_SUPPORTED
					// The JPEG spec simply gives the ranges 0..13 for Ah and Al, but that
					// seems wrong: the upper bound ought to depend on data precision.
					// Perhaps they really meant 0..N+1 for N-bit precision.
					// Here we allow 0..10 for 8-bit data; Al larger than 10 results in
					// out-of-range reconstructed DC values during the first DC scan,
					// which might cause problems for some decoders.
					const int MAX_AH_AL=10;
					if(Ss<0||Ss>=DCTSIZE2||Se<Ss||Se>=DCTSIZE2||
					Ah<0||Ah>MAX_AH_AL||Al<0||Al>MAX_AH_AL) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_PROG_SCRIPT, scanno);
					if(Ss==0)
					{
						if(Se!=0) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_PROG_SCRIPT, scanno); // DC and AC together not OK
					}
					else
					{
						if(ncomps!=1) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_PROG_SCRIPT, scanno); // AC scans must be for only one component
					}

					for(int ci=0; ci<ncomps; ci++)
					{
						int comp_ind=scan_info.component_index[ci];
						if(Ss!=0&&last_bitpos[comp_ind, 0]<0) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_PROG_SCRIPT, scanno); // AC without prior DC scan
						for(int coefi=Ss; coefi<=Se; coefi++)
						{
							if(last_bitpos[comp_ind, coefi]<0)
							{ // first scan of this coefficient
								if(Ah!=0) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_PROG_SCRIPT, scanno);
							}
							else
							{ // not first scan
								if(Ah!=last_bitpos[comp_ind, coefi]||Al!=Ah-1) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_PROG_SCRIPT, scanno);
							}
							last_bitpos[comp_ind, coefi]=Al;
						}
					}
#endif // C_PROGRESSIVE_SUPPORTED
				}
				else
				{
					// For sequential JPEG, all progression parameters must be these:
					if(Ss!=0||Se!=DCTSIZE2-1||Ah!=0||Al!=0) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_PROG_SCRIPT, scanno);

					// Make sure components are not sent twice
					for(int ci=0; ci<ncomps; ci++)
					{
						int thisi=scan_info.component_index[ci];
						if(component_sent[thisi]) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_SCAN_SCRIPT, scanno);
						component_sent[thisi]=true;
					}
				}
			} // for(...)

			// Now verify that everything got sent.
			if(cinfo.process==J_CODEC_PROCESS.JPROC_PROGRESSIVE)
			{
#if C_PROGRESSIVE_SUPPORTED
				// For progressive mode, we only check that at least some DC data
				// got sent for each component; the spec does not require that all bits
				// of all coefficients be transmitted. Would it be wiser to enforce
				// transmission of all coefficient bits??
				for(int ci=0; ci<cinfo.num_components; ci++)
				{
					if(last_bitpos[ci, 0]<0) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_MISSING_DATA);
				}
#endif
			}
			else
			{
				for(int ci=0; ci<cinfo.num_components; ci++)
				{
					if(!component_sent[ci]) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_MISSING_DATA);
				}
			}
		}
#endif // NEED_SCAN_SCRIPT

		// Set up the scan parameters for the current scan
		static void select_scan_parameters(jpeg_compress cinfo)
		{
#if NEED_SCAN_SCRIPT
			if(cinfo.scan_info!=null)
			{
				// Prepare for current scan --- the script is already validated
				my_comp_master master=(my_comp_master)cinfo.master;
				jpeg_scan_info scanptr=cinfo.scan_info[master.scan_number];

				cinfo.comps_in_scan=scanptr.comps_in_scan;
				for(int ci=0; ci<scanptr.comps_in_scan; ci++)
				{
					cinfo.cur_comp_info[ci]=cinfo.comp_info[scanptr.component_index[ci]];
				}
				cinfo.Ss=scanptr.Ss;
				cinfo.Se=scanptr.Se;
				cinfo.Ah=scanptr.Ah;
				cinfo.Al=scanptr.Al;
			}
			else
#endif
			{
				// Prepare for single sequential-JPEG scan containing all components
				if(cinfo.num_components>MAX_COMPS_IN_SCAN) ERREXIT2(cinfo, J_MESSAGE_CODE.JERR_COMPONENT_COUNT, cinfo.num_components, MAX_COMPS_IN_SCAN);
				cinfo.comps_in_scan=cinfo.num_components;
				for(int ci=0; ci<cinfo.num_components; ci++)
				{
					cinfo.cur_comp_info[ci]=cinfo.comp_info[ci];
				}
				if(cinfo.lossless)
				{
#if C_LOSSLESS_SUPPORTED
					// If we fall through to here, the user specified lossless, but did not
					// provide a scan script.
					ERREXIT(cinfo, J_MESSAGE_CODE.JERR_NO_LOSSLESS_SCRIPT);
#endif
				}
				else
				{
					cinfo.process=J_CODEC_PROCESS.JPROC_SEQUENTIAL;
					cinfo.Ss=0;
					cinfo.Se=DCTSIZE2-1;
					cinfo.Ah=0;
					cinfo.Al=0;
				}
			}
		}

		// Do computations that are needed before processing a JPEG scan
		// cinfo.comps_in_scan and cinfo.cur_comp_info[] are already set
		static void per_scan_setup(jpeg_compress cinfo)
		{
			uint DCT_size=cinfo.DCT_size;

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
				compptr.MCU_sample_width=(int)DCT_size;
				compptr.last_col_width=1;

				// For noninterleaved scans, it is convenient to define last_row_height
				// as the number of block rows present in the last iMCU row.
				int tmp=(int)(compptr.height_in_blocks%compptr.v_samp_factor);
				if(tmp==0) tmp=compptr.v_samp_factor;
				compptr.last_row_height=tmp;

				// Prepare array describing MCU composition
				cinfo.block_in_MCU=1;
				cinfo.MCU_membership[0]=0;
			}
			else
			{
				// Interleaved (multi-component) scan
				if(cinfo.comps_in_scan<=0||cinfo.comps_in_scan>MAX_COMPS_IN_SCAN)
					ERREXIT2(cinfo, J_MESSAGE_CODE.JERR_COMPONENT_COUNT, cinfo.comps_in_scan, MAX_COMPS_IN_SCAN);

				// Overall image size in MCUs
				cinfo.MCUs_per_row=(uint)jdiv_round_up(cinfo.image_width, cinfo.max_h_samp_factor*DCT_size);
				cinfo.MCU_rows_in_scan=(uint)jdiv_round_up(cinfo.image_height, cinfo.max_v_samp_factor*DCT_size);

				cinfo.block_in_MCU=0;

				for(int ci=0; ci<cinfo.comps_in_scan; ci++)
				{
					jpeg_component_info compptr=cinfo.cur_comp_info[ci];

					// Sampling factors give # of blocks of component in each MCU
					compptr.MCU_width=compptr.h_samp_factor;
					compptr.MCU_height=compptr.v_samp_factor;
					compptr.MCU_blocks=(uint)(compptr.MCU_width*compptr.MCU_height);
					compptr.MCU_sample_width=(int)(compptr.MCU_width*DCT_size);

					// Figure number of non-dummy blocks in last MCU column & row
					int tmp=(int)(compptr.width_in_blocks%compptr.MCU_width);
					if(tmp==0) tmp=compptr.MCU_width;
					compptr.last_col_width=tmp;
					tmp=(int)(compptr.height_in_blocks%compptr.MCU_height);
					if(tmp==0) tmp=compptr.MCU_height;
					compptr.last_row_height=tmp;

					// Prepare array describing MCU composition
					int mcublks=(int)compptr.MCU_blocks;
					if(cinfo.block_in_MCU+mcublks>C_MAX_BLOCKS_IN_MCU) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_MCU_SIZE);

					while((mcublks--)>0) cinfo.MCU_membership[cinfo.block_in_MCU++]=ci;
				}
			}

			// Convert restart specified in rows to actual MCU count.
			// Note that count must fit in 16 bits, so we provide limiting.
			if(cinfo.restart_in_rows>0) cinfo.restart_interval=(uint)Math.Min(cinfo.restart_in_rows*cinfo.MCUs_per_row, 65535);
		}

		// Per-pass setup.
		// This is called at the beginning of each pass. We determine which modules
		// will be active during this pass and give them appropriate start_pass calls.
		// We also set is_last_pass to indicate whether any more passes will be required.
		static void prepare_for_pass(jpeg_compress cinfo)
		{
			my_comp_master master=(my_comp_master)cinfo.master;

			switch(master.pass_type)
			{
				case c_pass_type.main_pass:
					// Initial pass: will collect input data, and do either Huffman
					// optimization or data output for the first scan.
					select_scan_parameters(cinfo);
					per_scan_setup(cinfo);
					if(!cinfo.raw_data_in)
					{
						cinfo.cconvert.start_pass(cinfo);
						cinfo.downsample.start_pass(cinfo);
						cinfo.prep.start_pass(cinfo, J_BUF_MODE.JBUF_PASS_THRU);
					}
					cinfo.coef.entropy_start_pass(cinfo, cinfo.optimize_coding);
					cinfo.coef.start_pass(cinfo, (master.total_passes>1?J_BUF_MODE.JBUF_SAVE_AND_PASS:J_BUF_MODE.JBUF_PASS_THRU));
					cinfo.main.start_pass(cinfo, J_BUF_MODE.JBUF_PASS_THRU);
					if(cinfo.optimize_coding)
					{
						// No immediate data output; postpone writing frame/scan headers
						master.call_pass_startup=false;
					}
					else
					{
						// Will write frame/scan headers at first jpeg_write_scanlines call
						master.call_pass_startup=true;
					}
					break;
#if ENTROPY_OPT_SUPPORTED
				case c_pass_type.huff_opt_pass:
					// Do Huffman optimization for a scan after the first one.
					select_scan_parameters(cinfo);
					per_scan_setup(cinfo);
					if(cinfo.coef.need_optimization_pass(cinfo))
					{
						cinfo.coef.entropy_start_pass(cinfo, true);
						cinfo.coef.start_pass(cinfo, J_BUF_MODE.JBUF_CRANK_DEST);
						master.call_pass_startup=false;
						break;
					}
					// Special case: Huffman DC refinement scans need no Huffman table
					// and therefore we can skip the optimization pass for them.
					master.pass_type=c_pass_type.output_pass;
					master.pass_number++;
					goto case c_pass_type.output_pass; // FALLTHROUGH
#endif
				case c_pass_type.output_pass:
					// Do a data-output pass.
					// We need not repeat per-scan setup if prior optimization pass did it.
					if(!cinfo.optimize_coding)
					{
						select_scan_parameters(cinfo);
						per_scan_setup(cinfo);
					}
					cinfo.coef.entropy_start_pass(cinfo, false);
					cinfo.coef.start_pass(cinfo, J_BUF_MODE.JBUF_CRANK_DEST);
					// We emit frame/scan headers now
					if(master.scan_number==0) cinfo.marker.write_frame_header(cinfo);
					cinfo.marker.write_scan_header(cinfo);
					master.call_pass_startup=false;
					break;
				default: ERREXIT(cinfo, J_MESSAGE_CODE.JERR_NOT_COMPILED); break;
			}

			master.is_last_pass=(master.pass_number==master.total_passes-1);

			// Set up progress monitor's pass info if present
			if(cinfo.progress!=null)
			{
				cinfo.progress.completed_passes=master.pass_number;
				cinfo.progress.total_passes=master.total_passes;
			}
		}

		// Special start-of-pass hook.
		// This is called by jpeg_write_scanlines if call_pass_startup is true.
		// In single-pass processing, we need this hook because we don't want to
		// write frame/scan headers during jpeg_start_compress; we want to let the
		// application write COM markers etc. between jpeg_start_compress and the
		// jpeg_write_scanlines loop.
		// In multi-pass processing, this routine is not used.
		static void pass_startup(jpeg_compress cinfo)
		{
			cinfo.master.call_pass_startup=false; // reset flag so call only once

			cinfo.marker.write_frame_header(cinfo);
			cinfo.marker.write_scan_header(cinfo);
		}

		// Finish up at end of pass.
		static void finish_pass_master(jpeg_compress cinfo)
		{
			my_comp_master master=(my_comp_master)cinfo.master;

			// The entropy coder always needs an end-of-pass call,
			// either to analyze statistics or to flush its output buffer.
			cinfo.coef.entropy_finish_pass(cinfo);

			// Update state for next pass
			switch(master.pass_type)
			{
				case c_pass_type.main_pass:
					// next pass is either output of scan 0 (after optimization)
					// or output of scan 1 (if no optimization).
					master.pass_type=c_pass_type.output_pass;
					if(!cinfo.optimize_coding) master.scan_number++;
					break;
				case c_pass_type.huff_opt_pass:
					// next pass is always output of current scan
					master.pass_type=c_pass_type.output_pass;
					break;
				case c_pass_type.output_pass:
					// next pass is either optimization or output of next scan
					if(cinfo.optimize_coding) master.pass_type=c_pass_type.huff_opt_pass;
					master.scan_number++;
					break;
			}

			master.pass_number++;
		}

		// Initialize master compression control.
		static void jinit_c_master_control(jpeg_compress cinfo, bool transcode_only)
		{
			my_comp_master master=null;

			try
			{
				master=new my_comp_master();
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}

			cinfo.master=master;
			master.prepare_for_pass=prepare_for_pass;
			master.pass_startup=pass_startup;
			master.finish_pass=finish_pass_master;
			master.is_last_pass=false;

			cinfo.DCT_size=cinfo.lossless?1:(uint)DCTSIZE;

			// Validate parameters, determine derived values
			initial_setup(cinfo);

			if(cinfo.scan_info!=null)
			{
#if NEED_SCAN_SCRIPT
				validate_script(cinfo);
#else
				ERREXIT(cinfo, J_MESSAGE_CODE.JERR_NOT_COMPILED);
#endif
			}
			else
			{
				cinfo.process=J_CODEC_PROCESS.JPROC_SEQUENTIAL;
				cinfo.num_scans=1;
			}

			if((cinfo.process==J_CODEC_PROCESS.JPROC_PROGRESSIVE||cinfo.process==J_CODEC_PROCESS.JPROC_LOSSLESS)&&!cinfo.arith_code)
				cinfo.optimize_coding=true; // assume default tables no good for progressive mode or lossless mode; but only in Huffman case!

			// Initialize my private state
			if(transcode_only)
			{ // no main pass in transcoding
				if(cinfo.optimize_coding) master.pass_type=c_pass_type.huff_opt_pass;
				else master.pass_type=c_pass_type.output_pass;
			}
			else
			{ // for normal compression, first pass is always this type:
				master.pass_type=c_pass_type.main_pass;
			}
			master.scan_number=master.pass_number=0;
			if(cinfo.optimize_coding) master.total_passes=cinfo.num_scans*2;
			else master.total_passes=cinfo.num_scans;
		}
	}
}
