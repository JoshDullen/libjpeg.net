#if C_LOSSLESS_SUPPORTED||D_LOSSLESS_SUPPORTED
// jlossls.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This include file contains common declarations for the lossless JPEG
// codec modules.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		delegate void predict_difference_method_ptr(jpeg_compress cinfo, int ci, byte[] input_buf, byte[] prev_row, int[] diff_buf, uint width);
		delegate void scaler_method_ptr(jpeg_compress cinfo, int ci, byte[] input_buf, byte[] output_buf, uint width);

		// Lossless-specific compression codec (compressor proper)
		class jpeg_lossless_c_codec : jpeg_c_coef_contoller
		{
			// Difference buffer control
			public void_jpeg_compress_J_BUF_MODE_Handler diff_start_pass;

			// Pointer to data which is private to diff controller
			public object diff_private;

			// Entropy encoding 
			public uint_jpeg_compress_intAAA_uint_uint_uint_Handler entropy_encode_mcus;

			// Pointer to data which is private to entropy module
			public object entropy_private;

			// Prediction, differencing
			public void_jpeg_compress_Handler predict_start_pass;

			// It is useful to allow each component to have a separate diff method.
			public predict_difference_method_ptr[] predict_difference=new predict_difference_method_ptr[MAX_COMPONENTS];

			// Pointer to data which is private to predictor module
			public object pred_private;

			// Sample scaling
			public void_jpeg_compress_Handler scaler_start_pass;
			public void_jpeg_compress_byteA_byteA_uint_Handler scaler_scale;

			// Pointer to data which is private to scaler module
			public object scaler_private=null;
		}

		delegate void predict_undifference_method_ptr(jpeg_decompress cinfo, int comp_index, int[] diff_buf, int[] prev_row, int[] undiff_buf, uint width);

		// Lossless-specific decompression codec (decompressor proper)
		class jpeg_lossless_d_codec : jpeg_d_coef_controller
		{
			// Difference buffer control
			public void_jpeg_decompress_Handler diff_start_input_pass;

			// Pointer to data which is private to diff controller
			public object diff_private;

			// Entropy decoding
			public void_jpeg_decompress_Handler entropy_start_pass;
			public bool_jpeg_decompress_Handler entropy_process_restart;
			public uint_jpeg_decompress_intAAA_uint_uint_uint_Handler entropy_decode_mcus;

			// Pointer to data which is private to entropy module
			public object entropy_private;

			// Prediction, undifferencing
			public void_jpeg_decompress_Handler predict_start_pass;
			public void_jpeg_decompress_Handler predict_process_restart;

			// It is useful to allow each component to have a separate undiff method.
			public predict_undifference_method_ptr[] predict_undifference=new predict_undifference_method_ptr[MAX_COMPONENTS];

			// Pointer to data which is private to predictor module
			public object pred_private=null;

			// Sample scaling
			public void_jpeg_decompress_Handler scaler_start_pass;
			public void_jpeg_decompress_intA_byteA_uint_Handler scaler_scale;

			// Pointer to data which is private to scaler module
			public object scaler_private;
		}
	}
}
#endif // C_LOSSLESS_SUPPORTED||D_LOSSLESS_SUPPORTED
