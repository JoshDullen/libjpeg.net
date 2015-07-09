// jclossy.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1997-1998, Guido Vollbeding <guivol@esc.de>.
// Copyright (C) 1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains the control logic for the lossy JPEG compressor.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Initialize for a processing pass.
		static void start_pass_lossy(jpeg_compress cinfo, J_BUF_MODE pass_mode)
		{
			jpeg_lossy_c_codec lossyc=(jpeg_lossy_c_codec)cinfo.coef;

			lossyc.fdct_start_pass(cinfo);
			lossyc.coef_start_pass(cinfo, pass_mode);
		}

		// Initialize the lossy compression codec.
		// This is called only once, during master selection.
		static void jinit_lossy_c_codec(jpeg_compress cinfo)
		{
			jpeg_lossy_c_codec lossyc=null;

			// Create subobject
			try
			{
				if(cinfo.arith_code)
				{
#if C_ARITH_CODING_SUPPORTED
					lossyc=new arith_entropy_encoder();
#else
					ERREXIT(cinfo, J_MESSAGE_CODE.JERR_ARITH_NOTIMPL);
#endif
				}
				else lossyc=new jpeg_lossy_c_codec();
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}
			cinfo.coef=lossyc;

			// Initialize sub-modules

			// Forward DCT
			jinit_forward_dct(cinfo);

			// Entropy encoding: either Huffman or arithmetic coding.
			if(cinfo.arith_code)
			{
#if C_ARITH_CODING_SUPPORTED
				jinit_arith_encoder(cinfo);
#else
				ERREXIT(cinfo, J_MESSAGE_CODE.JERR_ARITH_NOTIMPL);
#endif
			}
			else
			{
				if(cinfo.process==J_CODEC_PROCESS.JPROC_PROGRESSIVE)
				{
#if C_PROGRESSIVE_SUPPORTED
					jinit_phuff_encoder(cinfo);
#else
					ERREXIT(cinfo, J_MESSAGE_CODE.JERR_NOT_COMPILED);
#endif
				}
				else jinit_shuff_encoder(cinfo);
			}

			// Need a full-image coefficient buffer in any multi-pass mode.
			jinit_c_coef_controller(cinfo, cinfo.num_scans>1||cinfo.optimize_coding);

			// Initialize method pointers.
			//
			// Note: entropy_start_pass and entropy_finish_pass are assigned in
			// jcshuff.cs, jcphuff.cs or jcarith.cs and compress_data is assigned in jccoefct.cs.
			lossyc.start_pass=start_pass_lossy;
		}
	}
}