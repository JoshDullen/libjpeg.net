// jmorecfg.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1991-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains additional configuration options that customize the
// JPEG software for special applications or support machine-dependent
// optimizations. Most users will not need to touch this file.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Define BITS_IN_JSAMPLE as 8 bits
		public const int BITS_IN_JSAMPLE=8;

		// Maximum number of components (color channels) allowed in JPEG image.
		// To meet the letter of the JPEG spec, set this to 255. However, darn
		// few applications need more than 4 channels (maybe 5 for CMYK + alpha
		// mask). We recommend 10 as a reasonable compromise; use 4 if you are
		// really short on memory. (Each allowed component costs a hundred or so
		// bytes of storage, whether actually used in an image or not.)
		public const int MAX_COMPONENTS=10;	// maximum number of image components

		// Representation of a single sample (pixel element value).
		// We frequently allocate large arrays of these, so it's important to keep
		// them small. But if you have memory to burn and access to char or short
		// arrays is very slow on your hardware, you might want to change these.
		public const int MAXJSAMPLE=255;
		public const int CENTERJSAMPLE=128;

		// Datatype used for image dimensions. The JPEG standard only supports
		// images up to 64K*64K due to 16-bit fields in SOF markers. Therefore
		// "unsigned int" is sufficient on all machines. However, if you need to
		// handle larger images and you don't mind deviating from the spec, you
		// can change this datatype.
		public const int JPEG_MAX_DIMENSION=65500;	// a tad under 64K to prevent overflows

		// These defines indicate whether to include various optional functions.
		// Undefining some of these symbols will produce a smaller but less capable
		// library. Note that you can leave certain source files out of the
		// compilation/linking process if you've #undef'd the corresponding symbols.
		// (You may HAVE to do that if your compiler doesn't like null source files.)

		// Encoder capability options:
		//public const bool C_ARITH_CODING_SUPPORTED=true;		// Arithmetic coding back end?
		//public const bool C_MULTISCAN_FILES_SUPPORTED=true;	// Multiple-scan JPEG files?
		//public const bool C_PROGRESSIVE_SUPPORTED=true;		// Progressive JPEG? (Requires MULTISCAN
		//public const bool C_LOSSLESS_SUPPORTED=true;			// Lossless JPEG?
		//public const bool ENTROPY_OPT_SUPPORTED=true;			// Optimization of entropy coding parms?
		//public const bool INPUT_SMOOTHING_SUPPORTED=true;		// Input image smoothing option?

		// Decoder capability options:
		//public const bool D_ARITH_CODING_SUPPORTED=true;		// Arithmetic coding back end?
		//public const bool D_MULTISCAN_FILES_SUPPORTED=true;	// Multiple-scan JPEG files?
		//public const bool D_PROGRESSIVE_SUPPORTED=true;		// Progressive JPEG? (Requires MULTISCAN)
		//public const bool D_LOSSLESS_SUPPORTED=true;			// Lossless JPEG?
		//public const bool SAVE_MARKERS_SUPPORTED=true;		// jpeg_save_markers() needed?
		//public const bool BLOCK_SMOOTHING_SUPPORTED=true;		// Block smoothing? (Progressive only)
		//public const bool UPSAMPLE_MERGING_SUPPORTED=true;	// Fast path for sloppy upsampling?
		//public const bool QUANT_1PASS_SUPPORTED=true;			// 1-pass color quantization?
		//public const bool QUANT_2PASS_SUPPORTED=true;			// 2-pass color quantization?

		// more capability options later, no doubt

		// Ordering of RGB data in scanlines passed to or from the application.
		// If your application wants to deal with data in the order B,G,R, just
		// change these macros. You can also deal with formats such as R,G,B,X
		// (one extra byte per pixel) by changing RGB_PIXELSIZE. Note that changing
		// the offsets will also change the order in which colormap data is organized.
		// RESTRICTIONS:
		// 1.	These macros only affect RGB<=>YCbCr color conversion, so they are not
		//		useful if you are using JPEG color spaces other than YCbCr or grayscale.
		// 2.	The color quantizer modules will not behave desirably if RGB_PIXELSIZE
		//		is not 3 (they don't understand about dummy color components!). So you
		//		can't use color quantization if you change that value.
#if !BGR 
		public const int RGB_RED=0;			// Offset of Red in an RGB scanline element
		public const int RGB_GREEN=1;		// Offset of Green
		public const int RGB_BLUE=2;		// Offset of Blue
#else
		public const int RGB_RED=2;			// Offset of Red in an RGB scanline element
		public const int RGB_GREEN=1;		// Offset of Green
		public const int RGB_BLUE=0;		// Offset of Blue
#endif
		public const int RGB_PIXELSIZE=3;	// Bytes per RGB scanline element

	}
}