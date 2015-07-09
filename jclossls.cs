#if C_LOSSLESS_SUPPORTED
// jclossls.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains the control logic for the lossless JPEG compressor.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Initialize for a processing pass.
		static void start_pass_ls(jpeg_compress cinfo, J_BUF_MODE pass_mode)
		{
			jpeg_lossless_c_codec losslsc=(jpeg_lossless_c_codec)cinfo.coef;

			losslsc.scaler_start_pass(cinfo);
			losslsc.predict_start_pass(cinfo);
			losslsc.diff_start_pass(cinfo, pass_mode);
		}

		// Initialize the lossless compression codec.
		// This is called only once, during master selection.
		static void jinit_lossless_c_codec(jpeg_compress cinfo)
		{
			jpeg_lossless_c_codec losslsc=null;

			// Create subobject in permanent pool
			try
			{
				losslsc=new jpeg_lossless_c_codec();
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}
			cinfo.coef=losslsc;

			// Initialize sub-modules

			// Scaler
			jinit_c_scaler(cinfo);

			// Differencer
			jinit_differencer(cinfo);

			// Entropy encoding: either Huffman or arithmetic coding.
			if(cinfo.arith_code)
			{
				ERREXIT(cinfo, J_MESSAGE_CODE.JERR_ARITH_NOTIMPL);
			}
			else
			{
				jinit_lhuff_encoder(cinfo);
			}

			// Need a full-image difference buffer in any multi-pass mode.
			jinit_c_diff_controller(cinfo, (bool)(cinfo.num_scans>1|| cinfo.optimize_coding));

			// Initialize method pointers.
			//
			// Note: entropy_start_pass and entropy_finish_pass are assigned in
			// jclhuff.cs and compress_data is assigned in jcdiffct.cs.
			losslsc.start_pass=start_pass_ls;
		}
	}
}
#endif // C_LOSSLESS_SUPPORTED
