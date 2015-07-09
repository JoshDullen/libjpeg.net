// jdct.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1994-1996, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This include file contains common declarations for the forward and
// inverse DCT modules. These declarations are private to the DCT managers
// (jcdctmgr.cs, jddctmgr.cs) and the individual DCT algorithms.
// The individual DCT algorithms are kept in separate files to ease 
// machine-dependent tuning (e.g., assembly coding).

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// A forward DCT routine is given a pointer to a work area of type int[];
		// the DCT is to be performed in-place in that buffer.
		// The DCT inputs are expected to be signed (range +-CENTERJSAMPLE).
		// The DCT outputs are returned scaled up by a factor of 8; they therefore
		// have a range of +-8K for 8-bit data, +-128K for 12-bit data. This
		// convention improves accuracy in integer implementations and saves some
		// work in floating-point ones.
		// Quantization of the output coefficients is done by jcdctmgr.cs.
		delegate void forward_DCT_method_ptr(int[] data);
		delegate void float_DCT_method_ptr(double[] data);

		// An inverse DCT routine is given a pointer to the input JBLOCK and a pointer
		// to an output sample array. The routine must dequantize the input data as
		// well as perform the IDCT; for dequantization, it uses the multiplier table
		// pointed to by compptr.dct_table. The output data is to be placed into the
		// sample array starting at a specified column. (Any row offset needed will
		// be applied to the array pointer before it is passed to the IDCT code.)
		// Note that the number of samples emitted by the IDCT routine is
		// DCT_scaled_size*DCT_scaled_size.
		delegate void inverse_DCT_method_ptr(jpeg_decompress cinfo, jpeg_component_info compptr, short[] coef_block, byte[][] output_buf, uint output_row, uint output_col);
	}
}