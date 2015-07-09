// jdlossy.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1997-1998, Guido Vollbeding <guivol@esc.de>.
// Copyright (C) 1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains the control logic for the lossy JPEG decompressor.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Compute output image dimensions and related values.
		static void calc_output_dimensions_lossy(jpeg_decompress cinfo)
		{
			// Hardwire it to "no scaling"
			cinfo.output_width=cinfo.image_width;
			cinfo.output_height=cinfo.image_height;
			// jdinput.cs has already initialized codec_data_unit to DCTSIZE,
			// and has computed unscaled downsampled_width and downsampled_height.
		}

		// Save away a copy of the Q-table referenced by each component present
		// in the current scan, unless already saved during a prior scan.
		//
		// In a multiple-scan JPEG file, the encoder could assign different components
		// the same Q-table slot number, but change table definitions between scans
		// so that each component uses a different Q-table. (The IJG encoder is not
		// currently capable of doing this, but other encoders might.) Since we want
		// to be able to dequantize all the components at the end of the file, this
		// means that we have to save away the table actually used for each component.
		// We do this by copying the table at the start of the first scan containing
		// the component.
		// The JPEG spec prohibits the encoder from changing the contents of a Q-table
		// slot between scans of a component using that slot. If the encoder does so
		// anyway, this decoder will simply use the Q-table values that were current
		// at the start of the first scan for the component.
		//
		// The decompressor output side looks only at the saved quant tables,
		// not at the current Q-table slots.
		static void latch_quant_tables_lossy(jpeg_decompress cinfo)
		{
			for(int ci=0; ci<cinfo.comps_in_scan; ci++)
			{
				jpeg_component_info compptr=cinfo.cur_comp_info[ci];
				// No work if we already saved Q-table for this component
				if(compptr.quant_table!=null) continue;

				// Make sure specified quantization table is present
				int qtblno=compptr.quant_tbl_no;
				if(qtblno<0||qtblno>=NUM_QUANT_TBLS||cinfo.quant_tbl_ptrs[qtblno]==null) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_NO_QUANT_TABLE, qtblno);

				// OK, save away the quantization table
				JQUANT_TBL qtbl=new JQUANT_TBL();

				cinfo.quant_tbl_ptrs[qtblno].quantval.CopyTo(qtbl.quantval, 0);
				qtbl.sent_table=cinfo.quant_tbl_ptrs[qtblno].sent_table;

				compptr.quant_table=qtbl;
			}
		}

		// Initialize for an input processing pass.
		static void start_input_pass_lossy(jpeg_decompress cinfo)
		{
			jpeg_lossy_d_codec lossyd=(jpeg_lossy_d_codec)cinfo.coef;

			latch_quant_tables_lossy(cinfo);
			lossyd.entropy_start_pass(cinfo);
			lossyd.coef_start_input_pass(cinfo);
		}

		// Initialize for an output processing pass.
		static void start_output_pass_lossy(jpeg_decompress cinfo)
		{
			jpeg_lossy_d_codec lossyd=(jpeg_lossy_d_codec)cinfo.coef;

			lossyd.idct_start_pass(cinfo);
			lossyd.coef_start_output_pass(cinfo);
		}

		// Initialize the lossy decompression codec.
		// This is called only once, during master selection.
		static void jinit_lossy_d_codec(jpeg_decompress cinfo)
		{
			jpeg_lossy_d_codec lossyd=null;

			// Create subobject
			try
			{
				if(cinfo.arith_code)
				{
#if D_ARITH_CODING_SUPPORTED
					lossyd=new arith_entropy_decoder();
#else
					ERREXIT(cinfo, J_MESSAGE_CODE.JERR_ARITH_NOTIMPL);
#endif
				}
				else lossyd=new jpeg_lossy_d_codec();
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}

			cinfo.coef=lossyd;

			// Initialize sub-modules

			// Inverse DCT
			jinit_inverse_dct(cinfo);

			// Entropy decoding: either Huffman or arithmetic coding.
			if(cinfo.arith_code)
			{
#if D_ARITH_CODING_SUPPORTED
				jinit_arith_decoder(cinfo);
#else
				ERREXIT(cinfo, J_MESSAGE_CODE.JERR_ARITH_NOTIMPL);
#endif
			}
			else
			{
				if(cinfo.process==J_CODEC_PROCESS.JPROC_PROGRESSIVE)
				{
#if D_PROGRESSIVE_SUPPORTED
					jinit_phuff_decoder(cinfo);
#else
					ERREXIT(cinfo, J_MESSAGE_CODE.JERR_NOT_COMPILED);
#endif
				}
				else jinit_shuff_decoder(cinfo);
			}

			bool use_c_buffer=cinfo.inputctl.has_multiple_scans||cinfo.buffered_image;
			jinit_d_coef_controller(cinfo, use_c_buffer);

			// Initialize method pointers.
			//
			// Note: consume_data and decompress_data are assigned in jdcoefct.cs.
			lossyd.calc_output_dimensions=calc_output_dimensions_lossy;
			lossyd.start_input_pass=start_input_pass_lossy;
			lossyd.start_output_pass=start_output_pass_lossy;
		}
	}
}