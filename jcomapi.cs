// jcomapi.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1994-1997, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains application interface routines that are used for both
// compression and decompression.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Abort processing of a JPEG compression or decompression operation,
		// but don't destroy the object itself.
		public static void jpeg_abort(jpeg_common cinfo)
		{
			// Reset overall state for possible reuse of object
			if(cinfo.is_decompressor)
			{
				cinfo.global_state=STATE.DSTART;
				// Try to keep application from accessing now-deleted marker list.
				// A bit kludgy to do it here, but this is the most central place.
				((jpeg_decompress)cinfo).marker_list=null;
			}
			else cinfo.global_state=STATE.CSTART;
		}

		// Destruction of a JPEG object.
		//
		// Everything gets deallocated except the master jpeg_compress_struct itself
		// and the error manager struct. Both of these are supplied by the application
		// and must be freed, if necessary, by the application. (Often they are on
		// the stack and so don't need to be freed anyway.)
		// Closing a data source or destination, if necessary, is the application's
		// responsibility.
		public static void jpeg_destroy(jpeg_common cinfo)
		{
			cinfo.global_state=STATE.None;	// mark it destroyed
		}
		
		// Convenience routines for allocating quantization and Huffman tables.
		// (Would jutils.cs be a more reasonable place to put these?)
		public static JQUANT_TBL jpeg_alloc_quant_table(jpeg_common cinfo)
		{
			JQUANT_TBL tbl=null;

			try
			{
				tbl=new JQUANT_TBL();
				tbl.sent_table=false;	// make sure this is false in any new table
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}

			return tbl;
		}

		public static JHUFF_TBL jpeg_alloc_huff_table(jpeg_common cinfo)
		{
			JHUFF_TBL tbl=null;

			try
			{
				tbl=new JHUFF_TBL();
				tbl.sent_table=false;	// make sure this is false in any new table
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}

			return tbl;
		}
	}
}