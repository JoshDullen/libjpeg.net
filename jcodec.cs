// jcodec.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains utility functions for the JPEG codec(s).

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Initialize the compression codec.
		// This is called only once, during master selection.
		static void jinit_c_codec(jpeg_compress cinfo)
		{
			if(cinfo.process==J_CODEC_PROCESS.JPROC_LOSSLESS)
			{
#if C_LOSSLESS_SUPPORTED
				jinit_lossless_c_codec(cinfo);
#else
				ERREXIT(cinfo, J_MESSAGE_CODE.JERR_NOT_COMPILED);
#endif
			}
			else jinit_lossy_c_codec(cinfo);
		}

		// Initialize the decompression codec.
		// This is called only once, during master selection.
		static void jinit_d_codec(jpeg_decompress cinfo)
		{
			if(cinfo.process==J_CODEC_PROCESS.JPROC_LOSSLESS)
			{
#if D_LOSSLESS_SUPPORTED
				jinit_lossless_d_codec(cinfo);
#else
				ERREXIT(cinfo, J_MESSAGE_CODE.JERR_NOT_COMPILED);
#endif
			}
			else jinit_lossy_d_codec(cinfo);
		}
	}
}