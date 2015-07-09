#if C_ARITH_CODING_SUPPORTED||D_ARITH_CODING_SUPPORTED
// jaricom.cs
//
// Based on libjpeg versions until 8 - 10-Jan-2010
// Copyright (C) 2007-2010 by the Authors
// Copyright (C) 1991-2010, Thomas G. Lane, Guido Vollbeding.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains probability estimation tables for common use in
// arithmetic entropy encoding and decoding routines.
//
// This data represents Table D.2 in the JPEG spec (ISO/IEC IS 10918-1
// and CCITT Recommendation ITU-T T.81) and Table 24 in the JBIG spec
// (ISO/IEC IS 11544 and CCITT Recommendation ITU-T T.82).

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// The following two definitions specify the allocation chunk size
		// for the statistics area.
		// According to sections F.1.4.4.1.3 and F.1.4.4.2, we need at least
		// 49 statistics bins for DC, and 245 statistics bins for AC coding.
		// Note that we use one additional AC bin for codings with fixed
		// probability (0.5), thus the minimum number for AC is 246.

		// We use a compact representation with 1 byte per statistics bin,
		// thus the numbers directly represent byte sizes.
		// This 1 byte per statistics bin contains the meaning of the MPS
		// (more probable symbol) in the highest bit (mask 0x80), and the
		// index into the probability estimation state machine table
		// in the lower bits (mask 0x7F).
		const int DC_STAT_BINS=64;
		const int AC_STAT_BINS=256;

		// The following #define specifies the packing of the four components
		// into the compact int representation.
		// Note that this formula must match the actual arithmetic encoder
		// and decoder implementation. The implementation has to be changed
		// if this formula is changed.
		// The current organization is leaned on Markus Kuhn's JBIG
		// implementation (jbig_tab.c).
		static int V_arith(int i, int a, int b, int c, int d) { return ((a<<16)|(c<<8)|(d<<7)|b); }

		static readonly int[] jaritab=new int[]
		{ // Index, Qe_Value, Next_Index_LPS, Next_Index_MPS, Switch_MPS
			V_arith(  0, 0x5a1d,   1,   1, 1),
			V_arith(  1, 0x2586,  14,   2, 0),
			V_arith(  2, 0x1114,  16,   3, 0),
			V_arith(  3, 0x080b,  18,   4, 0),
			V_arith(  4, 0x03d8,  20,   5, 0),
			V_arith(  5, 0x01da,  23,   6, 0),
			V_arith(  6, 0x00e5,  25,   7, 0),
			V_arith(  7, 0x006f,  28,   8, 0),
			V_arith(  8, 0x0036,  30,   9, 0),
			V_arith(  9, 0x001a,  33,  10, 0),
			V_arith( 10, 0x000d,  35,  11, 0),
			V_arith( 11, 0x0006,   9,  12, 0),
			V_arith( 12, 0x0003,  10,  13, 0),
			V_arith( 13, 0x0001,  12,  13, 0),
			V_arith( 14, 0x5a7f,  15,  15, 1),
			V_arith( 15, 0x3f25,  36,  16, 0),
			V_arith( 16, 0x2cf2,  38,  17, 0),
			V_arith( 17, 0x207c,  39,  18, 0),
			V_arith( 18, 0x17b9,  40,  19, 0),
			V_arith( 19, 0x1182,  42,  20, 0),
			V_arith( 20, 0x0cef,  43,  21, 0),
			V_arith( 21, 0x09a1,  45,  22, 0),
			V_arith( 22, 0x072f,  46,  23, 0),
			V_arith( 23, 0x055c,  48,  24, 0),
			V_arith( 24, 0x0406,  49,  25, 0),
			V_arith( 25, 0x0303,  51,  26, 0),
			V_arith( 26, 0x0240,  52,  27, 0),
			V_arith( 27, 0x01b1,  54,  28, 0),
			V_arith( 28, 0x0144,  56,  29, 0),
			V_arith( 29, 0x00f5,  57,  30, 0),
			V_arith( 30, 0x00b7,  59,  31, 0),
			V_arith( 31, 0x008a,  60,  32, 0),
			V_arith( 32, 0x0068,  62,  33, 0),
			V_arith( 33, 0x004e,  63,  34, 0),
			V_arith( 34, 0x003b,  32,  35, 0),
			V_arith( 35, 0x002c,  33,   9, 0),
			V_arith( 36, 0x5ae1,  37,  37, 1),
			V_arith( 37, 0x484c,  64,  38, 0),
			V_arith( 38, 0x3a0d,  65,  39, 0),
			V_arith( 39, 0x2ef1,  67,  40, 0),
			V_arith( 40, 0x261f,  68,  41, 0),
			V_arith( 41, 0x1f33,  69,  42, 0),
			V_arith( 42, 0x19a8,  70,  43, 0),
			V_arith( 43, 0x1518,  72,  44, 0),
			V_arith( 44, 0x1177,  73,  45, 0),
			V_arith( 45, 0x0e74,  74,  46, 0),
			V_arith( 46, 0x0bfb,  75,  47, 0),
			V_arith( 47, 0x09f8,  77,  48, 0),
			V_arith( 48, 0x0861,  78,  49, 0),
			V_arith( 49, 0x0706,  79,  50, 0),
			V_arith( 50, 0x05cd,  48,  51, 0),
			V_arith( 51, 0x04de,  50,  52, 0),
			V_arith( 52, 0x040f,  50,  53, 0),
			V_arith( 53, 0x0363,  51,  54, 0),
			V_arith( 54, 0x02d4,  52,  55, 0),
			V_arith( 55, 0x025c,  53,  56, 0),
			V_arith( 56, 0x01f8,  54,  57, 0),
			V_arith( 57, 0x01a4,  55,  58, 0),
			V_arith( 58, 0x0160,  56,  59, 0),
			V_arith( 59, 0x0125,  57,  60, 0),
			V_arith( 60, 0x00f6,  58,  61, 0),
			V_arith( 61, 0x00cb,  59,  62, 0),
			V_arith( 62, 0x00ab,  61,  63, 0),
			V_arith( 63, 0x008f,  61,  32, 0),
			V_arith( 64, 0x5b12,  65,  65, 1),
			V_arith( 65, 0x4d04,  80,  66, 0),
			V_arith( 66, 0x412c,  81,  67, 0),
			V_arith( 67, 0x37d8,  82,  68, 0),
			V_arith( 68, 0x2fe8,  83,  69, 0),
			V_arith( 69, 0x293c,  84,  70, 0),
			V_arith( 70, 0x2379,  86,  71, 0),
			V_arith( 71, 0x1edf,  87,  72, 0),
			V_arith( 72, 0x1aa9,  87,  73, 0),
			V_arith( 73, 0x174e,  72,  74, 0),
			V_arith( 74, 0x1424,  72,  75, 0),
			V_arith( 75, 0x119c,  74,  76, 0),
			V_arith( 76, 0x0f6b,  74,  77, 0),
			V_arith( 77, 0x0d51,  75,  78, 0),
			V_arith( 78, 0x0bb6,  77,  79, 0),
			V_arith( 79, 0x0a40,  77,  48, 0),
			V_arith( 80, 0x5832,  80,  81, 1),
			V_arith( 81, 0x4d1c,  88,  82, 0),
			V_arith( 82, 0x438e,  89,  83, 0),
			V_arith( 83, 0x3bdd,  90,  84, 0),
			V_arith( 84, 0x34ee,  91,  85, 0),
			V_arith( 85, 0x2eae,  92,  86, 0),
			V_arith( 86, 0x299a,  93,  87, 0),
			V_arith( 87, 0x2516,  86,  71, 0),
			V_arith( 88, 0x5570,  88,  89, 1),
			V_arith( 89, 0x4ca9,  95,  90, 0),
			V_arith( 90, 0x44d9,  96,  91, 0),
			V_arith( 91, 0x3e22,  97,  92, 0),
			V_arith( 92, 0x3824,  99,  93, 0),
			V_arith( 93, 0x32b4,  99,  94, 0),
			V_arith( 94, 0x2e17,  93,  86, 0),
			V_arith( 95, 0x56a8,  95,  96, 1),
			V_arith( 96, 0x4f46, 101,  97, 0),
			V_arith( 97, 0x47e5, 102,  98, 0),
			V_arith( 98, 0x41cf, 103,  99, 0),
			V_arith( 99, 0x3c3d, 104, 100, 0),
			V_arith(100, 0x375e,  99,  93, 0),
			V_arith(101, 0x5231, 105, 102, 0),
			V_arith(102, 0x4c0f, 106, 103, 0),
			V_arith(103, 0x4639, 107, 104, 0),
			V_arith(104, 0x415e, 103,  99, 0),
			V_arith(105, 0x5627, 105, 106, 1),
			V_arith(106, 0x50e7, 108, 107, 0),
			V_arith(107, 0x4b85, 109, 103, 0),
			V_arith(108, 0x5597, 110, 109, 0),
			V_arith(109, 0x504f, 111, 107, 0),
			V_arith(110, 0x5a10, 110, 111, 1),
			V_arith(111, 0x5522, 112, 109, 0),
			V_arith(112, 0x59eb, 112, 111, 1),
			// This last entry is used for fixed probability estimate of 0.5
			// as recommended in Section 10.3 Table 5 of ITU-T Rec. T.851.
			V_arith(113, 0x5a1d, 113, 113, 0)
		};
	}
}
#endif // C_ARITH_CODING_SUPPORTED||D_ARITH_CODING_SUPPORTED