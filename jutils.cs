// jutils.cs
//
// Based on libjpeg versions until 8 - 10-Jan-2010
// Copyright (C) 2007-2010 by the Authors
// Copyright (C) 1991-2010, Thomas G. Lane, Guido Vollbeding.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains tables and miscellaneous utility routines needed
// for both compression and decompression.
// Note we prefix all global names with "j" to minimize conflicts with
// a surrounding application.

using System;

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		//static int FIX(double x) { return ((int)(x*(1<<SCALEBITS)+0.5)); }

		const int FIX_034414=(int)(0.34414*(1<<SCALEBITS)+0.5); //FIX(0.34414);
		const int FIX_071414=(int)(0.71414*(1<<SCALEBITS)+0.5); //FIX(0.71414);
		const int FIX_177200=(int)(1.77200*(1<<SCALEBITS)+0.5); //FIX(1.77200);
		const int FIX_140200=(int)(1.40200*(1<<SCALEBITS)+0.5); //FIX(1.40200);
		const int FIX_008131=(int)(0.08131*(1<<SCALEBITS)+0.5); //FIX(0.08131);
		const int FIX_041869=(int)(0.41869*(1<<SCALEBITS)+0.5); //FIX(0.41869);
		const int FIX_050000=(int)(0.50000*(1<<SCALEBITS)+0.5); //FIX(0.50000);
		const int FIX_033126=(int)(0.33126*(1<<SCALEBITS)+0.5); //FIX(0.33126);
		const int FIX_016874=(int)(0.16874*(1<<SCALEBITS)+0.5); //FIX(0.16874);
		const int FIX_011400=(int)(0.11400*(1<<SCALEBITS)+0.5); //FIX(0.11400);
		const int FIX_058700=(int)(0.58700*(1<<SCALEBITS)+0.5); //FIX(0.58700);
		const int FIX_029900=(int)(0.29900*(1<<SCALEBITS)+0.5); //FIX(0.29900);

		// jpeg_zigzag_order[i] is the zigzag-order position of the i'th element
		// of a DCT block read in natural order (left to right, top to bottom).
#if FALSE // This table is not actually needed in v6a
		static readonly int[] jpeg_zigzag_order=new int[DCTSIZE2] 
		{
		   0,  1,  5,  6, 14, 15, 27, 28,
		   2,  4,  7, 13, 16, 26, 29, 42,
		   3,  8, 12, 17, 25, 30, 41, 43,
		   9, 11, 18, 24, 31, 40, 44, 53,
		  10, 19, 23, 32, 39, 45, 52, 54,
		  20, 22, 33, 38, 46, 51, 55, 60,
		  21, 34, 37, 47, 50, 56, 59, 61,
		  35, 36, 48, 49, 57, 58, 62, 63
		};
#endif

		// jpeg_natural_order[i] is the natural-order position of the i'th element
		// of zigzag order.
		//
		// When reading corrupted data, the Huffman decoders could attempt
		// to reference an entry beyond the end of this array (if the decoded
		// zero run length reaches past the end of the block). To prevent
		// wild stores without adding an inner-loop test, we put some extra
		// "63"s after the real entries. This will cause the extra coefficient
		// to be stored in location 63 of the block, not somewhere random.
		// The worst case would be a run-length of 15, which means we need 16
		// fake entries.
		static readonly int[] jpeg_natural_order=new int[DCTSIZE2+16]
		{
		  0,  1,  8, 16,  9,  2,  3, 10,
		 17, 24, 32, 25, 18, 11,  4,  5,
		 12, 19, 26, 33, 40, 48, 41, 34,
		 27, 20, 13,  6,  7, 14, 21, 28,
		 35, 42, 49, 56, 57, 50, 43, 36,
		 29, 22, 15, 23, 30, 37, 44, 51,
		 58, 59, 52, 45, 38, 31, 39, 46,
		 53, 60, 61, 54, 47, 55, 62, 63,
		 63, 63, 63, 63, 63, 63, 63, 63, // extra entries for safety in decoder
		 63, 63, 63, 63, 63, 63, 63, 63
		};

		static readonly int[] jpeg_natural_order7=new int[7*7+16]
		{
		  0,  1,  8, 16,  9,  2,  3, 10,
		 17, 24, 32, 25, 18, 11,  4,  5,
		 12, 19, 26, 33, 40, 48, 41, 34,
		 27, 20, 13,  6, 14, 21, 28, 35,
		 42, 49, 50, 43, 36, 29, 22, 30,
		 37, 44, 51, 52, 45, 38, 46, 53,
		 54,
		 63, 63, 63, 63, 63, 63, 63, 63, // extra entries for safety in decoder
		 63, 63, 63, 63, 63, 63, 63, 63
		};

		static readonly int[] jpeg_natural_order6=new int[6*6+16]
		{
		  0,  1,  8, 16,  9,  2,  3, 10,
		 17, 24, 32, 25, 18, 11,  4,  5,
		 12, 19, 26, 33, 40, 41, 34, 27,
		 20, 13, 21, 28, 35, 42, 43, 36,
		 29, 37, 44, 45,
		 63, 63, 63, 63, 63, 63, 63, 63, // extra entries for safety in decoder
		 63, 63, 63, 63, 63, 63, 63, 63
		};

		static readonly int[] jpeg_natural_order5=new int[5*5+16]
		{
		  0,  1,  8, 16,  9,  2,  3, 10,
		 17, 24, 32, 25, 18, 11,  4, 12,
		 19, 26, 33, 34, 27, 20, 28, 35,
		 36,
		 63, 63, 63, 63, 63, 63, 63, 63, // extra entries for safety in decoder
		 63, 63, 63, 63, 63, 63, 63, 63
		};

		static readonly int[] jpeg_natural_order4=new int[4*4+16]
		{
		  0,  1,  8, 16,  9,  2,  3, 10,
		 17, 24, 25, 18, 11, 19, 26, 27,
		 63, 63, 63, 63, 63, 63, 63, 63, // extra entries for safety in decoder
		 63, 63, 63, 63, 63, 63, 63, 63
		};

		static readonly int[] jpeg_natural_order3=new int[3*3+16]
		{
		  0,  1,  8, 16,  9,  2, 10, 17,
		 18,
		 63, 63, 63, 63, 63, 63, 63, 63, // extra entries for safety in decoder
		 63, 63, 63, 63, 63, 63, 63, 63
		};

		static readonly int[] jpeg_natural_order2=new int[2*2+16]
		{
		  0,  1,  8,  9,
		 63, 63, 63, 63, 63, 63, 63, 63, // extra entries for safety in decoder
		 63, 63, 63, 63, 63, 63, 63, 63
		};

		// Arithmetic utilities

		// Compute a/b rounded up to next integer, ie, ceil(a/b)
		// Assumes a >= 0, b > 0
		static int jdiv_round_up(int a, int b)
		{
			return (a+b-1)/b;
		}

		// Compute a/b rounded up to next integer, ie, ceil(a/b)
		// Assumes a >= 0, b > 0
		static long jdiv_round_up(long a, long b)
		{
			return (a+b-1)/b;
		}

		// Compute a rounded up to next multiple of b, ie, ceil(a/b)*b
		// Assumes a >= 0, b > 0
		static int jround_up(int a, int b)
		{
			a+=b-1;
			return a-(a%b);
		}

		// Compute a rounded up to next multiple of b, ie, ceil(a/b)*b
		// Assumes a >= 0, b > 0
		static long jround_up(long a, long b)
		{
			a+=b-1;
			return a-(a%b);
		}

		// Copy some rows of samples from one place to another.
		// num_rows rows are copied from input_array[source_row++]
		// to output_array[dest_row++]; these areas may overlap for duplication.
		// The source and destination arrays must be at least as wide as num_cols.
		static void jcopy_sample_rows(byte[][] input_array, int source_row, byte[][] output_array, int dest_row, int num_rows, uint num_cols)
		{
			for(int row=num_rows; row>0; row--) Array.Copy(input_array[source_row++], output_array[dest_row++], num_cols);
		}
	}
}