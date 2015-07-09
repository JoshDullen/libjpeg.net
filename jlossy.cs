// jlossy.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This include file contains common declarations for the lossy (DCT-based)
// JPEG codec modules.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Lossy-specific compression codec (compressor proper)
		class jpeg_lossy_c_codec : jpeg_c_coef_contoller
		{
			// Coefficient buffer control
			public void_jpeg_compress_J_BUF_MODE_Handler coef_start_pass;
			//public bool_jpeg_compress_byteAAA_Handler coef_compress_data;

			// Pointer to data which is private to coef module
			public object coef_private;

			// Forward DCT (also controls coefficient quantization)
			public void_jpeg_compress_Handler fdct_start_pass;
			// perhaps this should be an array???
			public void_jpeg_compress_jpeg_component_info_byteAA_JBLOCKAA_int_uint_uint_uint_Handler fdct_forward_DCT;

			// Pointer to data which is private to fdct module
			public object fdct_private;

			// Entropy encoding
			public bool_jpeg_compress_shortAAA_Handler entropy_encode_mcu;

			// Pointer to data which is private to entropy module
			public object entropy_private;
		}

		//delegate void inverse_DCT_method_ptr(jpeg_decompress cinfo, jpeg_component_info compptr, short[] coef_block, byte[][] output_buf, uint output_col);

		// Lossy-specific decompression codec (decompressor proper)
		class jpeg_lossy_d_codec : jpeg_d_coef_controller
		{
			// Coefficient buffer control
			public void_jpeg_decompress_Handler coef_start_input_pass;
			public void_jpeg_decompress_Handler coef_start_output_pass;

			// Pointer to array of coefficient arrays, or null if none
			public short[][][][] coef_arrays;

			// Pointer to data which is private to coef module
			public object coef_private;

			// Entropy decoding
			public void_jpeg_decompress_Handler entropy_start_pass;
			public bool_jpeg_decompress_shortAA_Handler entropy_decode_mcu;

			// Pointer to data which is private to entropy module
			public object entropy_private;

			// Inverse DCT (also performs dequantization)
			public void_jpeg_decompress_Handler idct_start_pass;

			// It is useful to allow each component to have a separate IDCT method.
			public inverse_DCT_method_ptr[] inverse_DCT=new inverse_DCT_method_ptr[MAX_COMPONENTS];

			// Pointer to data which is private to idct module
			public object idct_private;
		}
	}
}
