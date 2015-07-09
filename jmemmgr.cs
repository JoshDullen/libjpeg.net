// jmemmgr.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1991-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains the JPEG memory management (more or less a few helper methods) for dotNet.

using System;

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Some important notes:
		//	The allocation routines provided here must never return null.
		//	They should throw a exception if unsuccessful.

		// Allocate a 2-D sample array
		public static byte[][] alloc_sarray(jpeg_common cinfo, uint samplesperrow, uint numrows)
		{
			byte[][] result=null;
			try
			{
				// Get space for row pointers
				result=new byte[numrows][];

				// Get the rows themselves
				uint currow=0;
				while(currow<numrows) result[currow++]=new byte[samplesperrow];
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}
			return result;
		}

		// Creation of 2-D coefficient-block arrays.
		// This is essentially the same as the code for sample arrays, above.

		// Allocate a 2-D coefficient-block array
		public static short[][][] alloc_barray(jpeg_common cinfo, uint blocksperrow, uint numrows)
		{
			// Get space for row pointers
			short[][][] result=null;
			try
			{
				result=new short[numrows][][];

				// Get the rows themselves
				for(uint currow=0; currow<numrows; currow++)
				{
					result[currow]=new short[blocksperrow][];
					for(uint curblockinrow=0; curblockinrow<blocksperrow; curblockinrow++) result[currow][curblockinrow]=new short[DCTSIZE2];
				}
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}
			return result;
		}

		// Creation of 2-D difference arrays.
		// This is essentially the same as the code for sample arrays, above.

		// Allocate a 2-D difference array
		public static int[][] alloc_darray(jpeg_common cinfo, uint diffsperrow, uint numrows)
		{
			int[][] result=null;
			try
			{
				// Get space for row pointers
				result=new int[numrows][];

				// Get the rows themselves
				uint currow=0;
				while(currow<numrows) result[currow++]=new int[diffsperrow];
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}
			return result;
		}
	}
}
