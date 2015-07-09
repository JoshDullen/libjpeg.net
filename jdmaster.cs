// jdmaster.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1991-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains master control logic for the JPEG decompressor.
// These routines are concerned with selecting the modules to be executed
// and with determining the number of passes and the work to be done in each
// pass.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Private state
		internal class my_decomp_master : jpeg_decomp_master
		{
			public int pass_number;				// # of passes completed

			public bool using_merged_upsample;	// true if using merged upsample/cconvert

			// Saved references to initialized quantizer modules,
			// in case we need to switch modes.
			public jpeg_color_quantizer quantizer_1pass;
			public jpeg_color_quantizer quantizer_2pass;
		}

		// Determine whether merged upsample/color conversion should be used.
		// CRUCIAL: this must match the actual capabilities of jdmerge.cs!
		static bool use_merged_upsample(jpeg_decompress cinfo)
		{
#if UPSAMPLE_MERGING_SUPPORTED
			// Merging is the equivalent of plain box-filter upsampling
			if(cinfo.do_fancy_upsampling||cinfo.CCIR601_sampling) return false;

			// jdmerge.cs only supports YCC=>RGB color conversion
			if(cinfo.jpeg_color_space!=J_COLOR_SPACE.JCS_YCbCr||cinfo.num_components!=3||
				cinfo.out_color_space!=J_COLOR_SPACE.JCS_RGB||cinfo.out_color_components!=RGB_PIXELSIZE) return false;

			// and it only handles 2h1v or 2h2v sampling ratios
			if(cinfo.comp_info[0].h_samp_factor!=2||cinfo.comp_info[1].h_samp_factor!=1||
				cinfo.comp_info[2].h_samp_factor!=1||cinfo.comp_info[0].v_samp_factor>2||
				cinfo.comp_info[1].v_samp_factor!=1||cinfo.comp_info[2].v_samp_factor!=1) return false;

			// furthermore, it doesn't work if each component has been processed differently
			if(cinfo.comp_info[0].DCT_scaled_size!=cinfo.min_DCT_scaled_size||
				cinfo.comp_info[1].DCT_scaled_size!=cinfo.min_DCT_scaled_size||
				cinfo.comp_info[2].DCT_scaled_size!=cinfo.min_DCT_scaled_size) return false;

			// ??? also need to test for upsample-time rescaling, when & if supported
			return true; // by golly, it'll work... 
#else
			return false;
#endif
		}

		// Compute output image dimensions and related values.
		// NOTE: this is exported for possible use by application.
		// Hence it mustn't do anything that can't be done twice.
		// Also note that it may be called before the master module is initialized!

		// Do computations that are needed before master selection phase
		public static void jpeg_calc_output_dimensions(jpeg_decompress cinfo)
		{
			// Prevent application from calling me at wrong times
			if(cinfo.global_state!=STATE.DREADY) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_STATE, cinfo.global_state);

			cinfo.coef.calc_output_dimensions(cinfo);

			// Report number of components in selected colorspace.
			// Probably this should be in the color conversion module...
			switch(cinfo.out_color_space)
			{
				case J_COLOR_SPACE.JCS_GRAYSCALE: cinfo.out_color_components=1; break;
				case J_COLOR_SPACE.JCS_RGB:
				case J_COLOR_SPACE.JCS_YCbCr: cinfo.out_color_components=3; break;
				case J_COLOR_SPACE.JCS_CMYK:
				case J_COLOR_SPACE.JCS_YCCK: cinfo.out_color_components=4; break;
				default: cinfo.out_color_components=cinfo.num_components; break; // else must be same colorspace as in file
			}
			cinfo.output_components=(cinfo.quantize_colors?1:cinfo.out_color_components);

			// See if upsampler will want to emit more than one row at a time
			if(use_merged_upsample(cinfo)) cinfo.rec_outbuf_height=cinfo.max_v_samp_factor;
			else cinfo.rec_outbuf_height=1;
		}

		// Master selection of decompression modules.
		// This is done once at jpeg_start_decompress time. We determine
		// which modules will be used and give them appropriate initialization calls.
		// We also initialize the decompressor input side to begin consuming data.
		//
		// Since jpeg_read_header has finished, we know what is in the SOF
		// and (first) SOS markers. We also have all the application parameter
		// settings.
		static void master_selection(jpeg_decompress cinfo)
		{
			my_decomp_master master=(my_decomp_master)cinfo.master;

			// Initialize dimensions and other stuff
			jpeg_calc_output_dimensions(cinfo);
			//prepare_range_limit_table(cinfo);

			// Width of an output scanline must be representable as uint.
			int samplesperrow=(int)cinfo.output_width*(int)cinfo.out_color_components;
			uint jd_samplesperrow=(uint)samplesperrow;
			if((int)jd_samplesperrow!=samplesperrow) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_WIDTH_OVERFLOW);

			// Initialize my private state
			master.pass_number=0;
			master.using_merged_upsample=use_merged_upsample(cinfo);

			// Color quantizer selection
			master.quantizer_1pass=null;
			master.quantizer_2pass=null;

			// No mode changes if not using buffered-image mode.
			if(!cinfo.quantize_colors||!cinfo.buffered_image)
			{
				cinfo.enable_1pass_quant=false;
				cinfo.enable_external_quant=false;
				cinfo.enable_2pass_quant=false;
			}

			if(cinfo.quantize_colors)
			{
				if(cinfo.raw_data_out) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_NOTIMPL);

				// 2-pass quantizer only works in 3-component color space.
				if(cinfo.out_color_components!=3)
				{
					cinfo.enable_1pass_quant=true;
					cinfo.enable_external_quant=false;
					cinfo.enable_2pass_quant=false;
					cinfo.colormap=null;
				}
				else if(cinfo.colormap!=null) cinfo.enable_external_quant=true;
				else if(cinfo.two_pass_quantize) cinfo.enable_2pass_quant=true;
				else cinfo.enable_1pass_quant=true;

				if(cinfo.enable_1pass_quant)
				{
#if QUANT_1PASS_SUPPORTED
					jinit_1pass_quantizer(cinfo);
					master.quantizer_1pass=cinfo.cquantize;
#else
					 ERREXIT(cinfo, J_MESSAGE_CODE.JERR_NOT_COMPILED);
#endif
				}

				// We use the 2-pass code to map to external colormaps.
				if(cinfo.enable_2pass_quant||cinfo.enable_external_quant)
				{
#if QUANT_2PASS_SUPPORTED
					jinit_2pass_quantizer(cinfo);
					master.quantizer_2pass=cinfo.cquantize;
#else
					ERREXIT(cinfo, J_MESSAGE_CODE.JERR_NOT_COMPILED);
#endif
				}
				// If both quantizers are initialized, the 2-pass one is left active;
				// this is necessary for starting with quantization to an external map.
			}

			// Post-processing: in particular, color conversion first
			if(!cinfo.raw_data_out)
			{
				if(master.using_merged_upsample)
				{
#if UPSAMPLE_MERGING_SUPPORTED
					jinit_merged_upsampler(cinfo); // does color conversion too
#else
					ERREXIT(cinfo, J_MESSAGE_CODE.JERR_NOT_COMPILED);
#endif
				}
				else
				{
					jinit_color_deconverter(cinfo);
					jinit_upsampler(cinfo);
				}
				jinit_d_post_controller(cinfo, cinfo.enable_2pass_quant);
			}

			// Initialize principal buffer controllers.
			if(!cinfo.raw_data_out) jinit_d_main_controller(cinfo, false); // never need full buffer here

			// Initialize input side of decompressor to consume first scan.
			cinfo.inputctl.start_input_pass(cinfo);

#if D_MULTISCAN_FILES_SUPPORTED
			// If jpeg_start_decompress will read the whole file, initialize
			// progress monitoring appropriately. The input step is counted
			// as one pass.
			if(cinfo.progress!=null&&!cinfo.buffered_image&&cinfo.inputctl.has_multiple_scans)
			{
				int nscans;
				// Estimate number of scans to set pass_limit.
				if(cinfo.process==J_CODEC_PROCESS.JPROC_PROGRESSIVE)
				{
					// Arbitrarily estimate 2 interleaved DC scans + 3 AC scans/component.
					nscans=2+3*cinfo.num_components;
				}
				else
				{
					// For a nonprogressive multiscan file, estimate 1 scan per component.
					nscans=cinfo.num_components;
				}
				cinfo.progress.pass_counter=0;
				cinfo.progress.pass_limit=(int)cinfo.total_iMCU_rows*nscans;
				cinfo.progress.completed_passes=0;
				cinfo.progress.total_passes=(cinfo.enable_2pass_quant?3:2);
				// Count the input pass as done
				master.pass_number++;
			}
#endif // D_MULTISCAN_FILES_SUPPORTED
		}

		// Per-pass setup.
		// This is called at the beginning of each output pass. We determine which
		// modules will be active during this pass and give them appropriate
		// start_pass calls. We also set is_dummy_pass to indicate whether this
		// is a "real" output pass or a dummy pass for color quantization.
		// (In the latter case, jdapistd.cs will crank the pass to completion.)
		static void prepare_for_output_pass(jpeg_decompress cinfo)
		{
			my_decomp_master master=(my_decomp_master)cinfo.master;

			if(master.is_dummy_pass)
			{
#if QUANT_2PASS_SUPPORTED
				// Final pass of 2-pass quantization
				master.is_dummy_pass=false;
				cinfo.cquantize.start_pass(cinfo, false);
				cinfo.post.start_pass(cinfo, J_BUF_MODE.JBUF_CRANK_DEST);
				cinfo.main.start_pass(cinfo, J_BUF_MODE.JBUF_CRANK_DEST);
#else
				ERREXIT(cinfo, J_MESSAGE_CODE.JERR_NOT_COMPILED);
#endif // QUANT_2PASS_SUPPORTED
			}
			else
			{
				if(cinfo.quantize_colors&&cinfo.colormap==null)
				{
					// Select new quantization method
					if(cinfo.two_pass_quantize&&cinfo.enable_2pass_quant)
					{
						cinfo.cquantize=master.quantizer_2pass;
						master.is_dummy_pass=true;
					}
					else if(cinfo.enable_1pass_quant) cinfo.cquantize=master.quantizer_1pass;
					else ERREXIT(cinfo, J_MESSAGE_CODE.JERR_MODE_CHANGE);
				}

				cinfo.coef.start_output_pass(cinfo);
				if(!cinfo.raw_data_out)
				{
					if(!master.using_merged_upsample) cinfo.cconvert.start_pass(cinfo);
					cinfo.upsample.start_pass(cinfo);
					if(cinfo.quantize_colors) cinfo.cquantize.start_pass(cinfo, master.is_dummy_pass);
					cinfo.post.start_pass(cinfo, (master.is_dummy_pass?J_BUF_MODE.JBUF_SAVE_AND_PASS:J_BUF_MODE.JBUF_PASS_THRU));
					cinfo.main.start_pass(cinfo, J_BUF_MODE.JBUF_PASS_THRU);
				}
			}

			// Set up progress monitor's pass info if present
			if(cinfo.progress!=null)
			{
				cinfo.progress.completed_passes=master.pass_number;
				cinfo.progress.total_passes=master.pass_number+(master.is_dummy_pass?2:1);

				// In buffered-image mode, we assume one more output pass if EOI not
				// yet reached, but no more passes if EOI has been reached.
				if(cinfo.buffered_image&&!cinfo.inputctl.eoi_reached) cinfo.progress.total_passes+=(cinfo.enable_2pass_quant?2:1);
			}
		}

		// Finish up at end of an output pass.
		static void finish_output_pass(jpeg_decompress cinfo)
		{
			my_decomp_master master=(my_decomp_master)cinfo.master;

			if(cinfo.quantize_colors) cinfo.cquantize.finish_pass(cinfo);
			master.pass_number++;
		}

#if D_MULTISCAN_FILES_SUPPORTED
		// Switch to a new external colormap between output passes.
		public static void jpeg_new_colormap(jpeg_decompress cinfo)
		{
			my_decomp_master master=(my_decomp_master)cinfo.master;

			// Prevent application from calling me at wrong times
			if(cinfo.global_state!=STATE.DBUFIMAGE) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_STATE, cinfo.global_state);

			if(cinfo.quantize_colors&&cinfo.enable_external_quant&&cinfo.colormap!=null)
			{
				// Select 2-pass quantizer for external colormap use
				cinfo.cquantize=master.quantizer_2pass;

				// Notify quantizer of colormap change
				cinfo.cquantize.new_color_map(cinfo);
				master.is_dummy_pass=false; // just in case
			}
			else ERREXIT(cinfo, J_MESSAGE_CODE.JERR_MODE_CHANGE);
		}
#endif // D_MULTISCAN_FILES_SUPPORTED

		// Initialize master decompression control and select active modules.
		// This is performed at the start of jpeg_start_decompress.
		public static void jinit_master_decompress(jpeg_decompress cinfo)
		{
			my_decomp_master master=null;

			try
			{
				master=new my_decomp_master();
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}
			cinfo.master=master;
			master.prepare_for_output_pass=prepare_for_output_pass;
			master.finish_output_pass=finish_output_pass;

			master.is_dummy_pass=false;

			master_selection(cinfo);
		}
	}
}
