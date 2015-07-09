#if D_LOSSLESS_SUPPORTED
// jdlossls.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains the control logic for the lossless JPEG decompressor.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Compute output image dimensions and related values.
		static void calc_output_dimensions_ls(jpeg_decompress cinfo)
		{
			// Hardwire it to "no scaling"
			cinfo.output_width=cinfo.image_width;
			cinfo.output_height=cinfo.image_height;
			// jdinput.cs has already initialized codec_data_unit to 1,
			// and has computed unscaled downsampled_width and downsampled_height.
		}

		// Initialize for an input processing pass.
		static void start_input_pass_ls(jpeg_decompress cinfo)
		{
			jpeg_lossless_d_codec losslsd=(jpeg_lossless_d_codec)cinfo.coef;

			losslsd.entropy_start_pass(cinfo);
			losslsd.predict_start_pass(cinfo);
			losslsd.scaler_start_pass(cinfo);
			losslsd.diff_start_input_pass(cinfo);
		}

		// Initialize the lossless decompression codec.
		// This is called only once, during master selection.
		static void jinit_lossless_d_codec(jpeg_decompress cinfo)
		{
			jpeg_lossless_d_codec losslsd=null;

			// Create subobject
			try
			{
				losslsd=new jpeg_lossless_d_codec();
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}

			cinfo.coef=losslsd;

			// Initialize sub-modules
			// Entropy decoding: either Huffman or arithmetic coding.
			if(cinfo.arith_code)
			{
				ERREXIT(cinfo, J_MESSAGE_CODE.JERR_ARITH_NOTIMPL);
			}
			else
			{
				jinit_lhuff_decoder(cinfo);
			}

			// Undifferencer
			jinit_undifferencer(cinfo);

			// Scaler
			jinit_d_scaler(cinfo);

			bool use_c_buffer=cinfo.inputctl.has_multiple_scans||cinfo.buffered_image;
			jinit_d_diff_controller(cinfo, use_c_buffer);

			// Initialize method pointers.
			//
			// Note: consume_data, start_output_pass and decompress_data are
			// assigned in jddiffct.cs.
			losslsd.calc_output_dimensions=calc_output_dimensions_ls;
			losslsd.start_input_pass=start_input_pass_ls;
		}
	}
}
#endif // D_LOSSLESS_SUPPORTED
