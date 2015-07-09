#if C_LOSSLESS_SUPPORTED
// jcscale.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains sample downscaling by 2^Pt for lossless JPEG.

using System;

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		static void simple_downscale(jpeg_compress cinfo, byte[] input_buf, byte[] output_buf, uint width)
		{
			jpeg_lossless_c_codec losslsc=(jpeg_lossless_c_codec)cinfo.coef;
			for(uint xindex=0; xindex<width; xindex++) output_buf[xindex]=(byte)(input_buf[xindex]>>cinfo.Al);
		}

		static void noscale(jpeg_compress cinfo, byte[] input_buf, byte[] output_buf, uint width)
		{
			Array.Copy(input_buf, output_buf, width);
		}

		static void scaler_start_pass(jpeg_compress cinfo)
		{
			jpeg_lossless_c_codec losslsc=(jpeg_lossless_c_codec)cinfo.coef;

			// Set scaler function based on Pt
			if(cinfo.Al!=0) losslsc.scaler_scale=simple_downscale;
			else losslsc.scaler_scale=noscale;
		}

		static void jinit_c_scaler(jpeg_compress cinfo)
		{
			jpeg_lossless_c_codec losslsc=(jpeg_lossless_c_codec)cinfo.coef;
			losslsc.scaler_start_pass=scaler_start_pass;
		}
	}
}
#endif // C_LOSSLESS_SUPPORTED
