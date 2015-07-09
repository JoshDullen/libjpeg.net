// jdatadst.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1994-1996, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains compression data destination routines for the case of
// emitting JPEG data to a file (or any stdio stream). While these routines
// are sufficient for most applications, some will want to use a different
// destination manager.
// IMPORTANT: we assume that Write() will correctly transcribe an array of
// bytes into 8-bit-wide elements on external storage. If byte is wider
// than 8 bits on your machine, you may need to do some tweaking.

using System.IO;

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Expanded data destination object for stdio output
		class my_destination_mgr : jpeg_destination_mgr
		{
			public Stream outfile;		// target stream
			public byte[] buffer;		// start of buffer
		}

		const int OUTPUT_BUF_SIZE=4096;	// choose an efficiently Write'able size

		// Initialize destination --- called by jpeg_start_compress
		// before any data is actually written.
		static void init_destination(jpeg_compress cinfo)
		{
			my_destination_mgr dest=(my_destination_mgr)cinfo.dest;

			// Allocate the output buffer --- it will be released when done with image
			try
			{
				dest.buffer=new byte[OUTPUT_BUF_SIZE];
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}
			dest.output_bytes=dest.buffer;
			dest.next_output_byte=0;
			dest.free_in_buffer=OUTPUT_BUF_SIZE;
		}
		
		// Empty the output buffer --- called whenever buffer fills up.
		//
		// In typical applications, this should write the entire output buffer
		// (ignoring the current state of next_output_byte & free_in_buffer),
		// reset the pointer & count to the start of the buffer, and return true
		// indicating that the buffer has been dumped.
		//
		// In applications that need to be able to suspend compression due to output
		// overrun, a false return indicates that the buffer cannot be emptied now.
		// In this situation, the compressor will return to its caller (possibly with
		// an indication that it has not accepted all the supplied scanlines). The
		// application should resume compression after it has made more room in the
		// output buffer. Note that there are substantial restrictions on the use of
		// suspension --- see the documentation.
		//
		// When suspending, the compressor will back up to a convenient restart point
		// (typically the start of the current MCU). next_output_byte & free_in_buffer
		// indicate where the restart point will be if the current call returns false.
		// Data beyond this point will be regenerated after resumption, so do not
		// write it out when emptying the buffer externally.
		static bool empty_output_buffer(jpeg_compress cinfo)
		{
			my_destination_mgr dest=(my_destination_mgr)cinfo.dest;

			try
			{
				dest.outfile.Write(dest.buffer, 0, OUTPUT_BUF_SIZE);
			}
			catch
			{
				ERREXIT(cinfo, J_MESSAGE_CODE.JERR_FILE_WRITE);
			}

			dest.output_bytes=dest.buffer;
			dest.next_output_byte=0;
			dest.free_in_buffer=OUTPUT_BUF_SIZE;

			return true;
		}

		// Terminate destination --- called by jpeg_finish_compress
		// after all data has been written. Usually needs to flush buffer.
		//
		// NB: *not* called by jpeg_abort or jpeg_destroy; surrounding
		// application must deal with any cleanup that should happen even
		// for error exit.
		static void term_destination(jpeg_compress cinfo)
		{
			my_destination_mgr dest=(my_destination_mgr)cinfo.dest;
			int datacount=OUTPUT_BUF_SIZE-(int)dest.free_in_buffer;

			// Write any data remaining in the buffer
			if(datacount>0)
			{
				try
				{
					dest.outfile.Write(dest.buffer, 0, datacount);
				}
				catch
				{
					ERREXIT(cinfo, J_MESSAGE_CODE.JERR_FILE_WRITE);
				}
			}
			dest.outfile.Flush();
		}

		// Prepare for output to a stdio stream.
		// The caller must have already opened the stream, and is responsible
		// for jpeg_destination_mgr it after finishing compression.
		public static void jpeg_stdio_dest(jpeg_compress cinfo, Stream outfile)
		{
			my_destination_mgr dest;

			// The destination object is made permanent so that multiple JPEG images
			// can be written to the same file without re-executing jpeg_stdio_dest.
			// This makes it dangerous to use this manager and a different destination
			// manager serially with the same JPEG object, because their private object
			// sizes may be different. Caveat programmer.
			if(cinfo.dest==null)
			{	
				// first time for this JPEG object?
				cinfo.dest=new my_destination_mgr();
			}

			dest=(my_destination_mgr)cinfo.dest;
			dest.init_destination=init_destination;
			dest.empty_output_buffer=empty_output_buffer;
			dest.term_destination=term_destination;
			dest.outfile=outfile;
		}
	}
}
