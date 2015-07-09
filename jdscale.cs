#if D_LOSSLESS_SUPPORTED
// jdscale.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains sample scaling for lossless JPEG. This is a
// combination of upscaling the undifferenced sample by 2^Pt and downscaling
// the sample to fit into byte.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Private scaler object for lossless decoding.
		class scaler
		{
			public int scale_factor;
		}

		// Scalers for packing sample differences into bytes.
		static void simple_upscale(jpeg_decompress cinfo, int[] diff_buf, byte[] output_buf, uint width)
		{
			jpeg_lossless_d_codec losslsd=(jpeg_lossless_d_codec)cinfo.coef;
			scaler scaler=(scaler)losslsd.scaler_private;

			int scale_factor=scaler.scale_factor;
			for(uint xindex=0; xindex<width; xindex++) output_buf[xindex]=(byte)(diff_buf[xindex]<<scale_factor);
		}

		static void simple_downscale(jpeg_decompress cinfo, int[] diff_buf, byte[] output_buf, uint width)
		{
			jpeg_lossless_d_codec losslsd=(jpeg_lossless_d_codec)cinfo.coef;
			scaler scaler=(scaler)losslsd.scaler_private;

			int scale_factor=scaler.scale_factor;
			for(uint xindex=0; xindex<width; xindex++) output_buf[xindex]=(byte)(diff_buf[xindex]>>scale_factor);
		}

		static void noscale(jpeg_decompress cinfo, int[] diff_buf, byte[] output_buf, uint width)
		{
			for(uint xindex=0; xindex<width; xindex++) output_buf[xindex]=(byte)diff_buf[xindex];
		}

		static void scaler_start_pass(jpeg_decompress cinfo)
		{
			jpeg_lossless_d_codec losslsd=(jpeg_lossless_d_codec)cinfo.coef;
			scaler scaler=(scaler)losslsd.scaler_private;

			// Downscale by the difference in the input vs. output precision. If the
			// output precision >= input precision, then do not downscale.
			int downscale=BITS_IN_JSAMPLE<cinfo.data_precision?cinfo.data_precision-BITS_IN_JSAMPLE:0;

			scaler.scale_factor=cinfo.Al-downscale;

			// Set scaler functions based on scale_factor (positive = left shift)
			if(scaler.scale_factor>0) losslsd.scaler_scale=simple_upscale;
			else if(scaler.scale_factor<0)
			{
				scaler.scale_factor=-scaler.scale_factor;
				losslsd.scaler_scale=simple_downscale;
			}
			else losslsd.scaler_scale=noscale;
		}

		static void jinit_d_scaler(jpeg_decompress cinfo)
		{
			jpeg_lossless_d_codec losslsd=(jpeg_lossless_d_codec)cinfo.coef;
			scaler scaler=null;

			try
			{
				scaler=new scaler();
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}

			losslsd.scaler_private=scaler;
			losslsd.scaler_start_pass=scaler_start_pass;
		}
	}
}
#endif // D_LOSSLESS_SUPPORTED
