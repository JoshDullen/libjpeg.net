// jdatasrc.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1994-1996, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains decompression data source routines for the case of
// reading JPEG data from a file (or any stdio stream). While these routines
// are sufficient for most applications, some will want to use a different
// source manager.
// IMPORTANT: we assume that Read() will correctly transcribe an array of
// bytes from 8-bit-wide elements on external storage. If byte is wider
// than 8 bits on your machine, you may need to do some tweaking.

using System.IO;

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Expanded data source object for stdio input
		class my_source_mgr : jpeg_source_mgr
		{
			public Stream infile;		// source stream
			public byte[] buffer;		// start of buffer
			public bool start_of_file;	// have we gotten any data yet?
		}

		const int INPUT_BUF_SIZE=4096; // choose an efficiently Read'able size

		// Initialize source --- called by jpeg_read_header
		// before any data is actually read.
		static void init_source(jpeg_decompress cinfo)
		{
			my_source_mgr src=(my_source_mgr)cinfo.src;

			// We reset the empty-input-file flag for each image,
			// but we don't clear the input buffer.
			// This is correct behavior for reading a series of images from one source.
			src.start_of_file=true;
		}

		// Fill the input buffer --- called whenever buffer is emptied.
		//
		// In typical applications, this should read fresh data into the buffer
		// (ignoring the current state of next_input_byte & bytes_in_buffer),
		// reset the pointer & count to the start of the buffer, and return true
		// indicating that the buffer has been reloaded. It is not necessary to
		// fill the buffer entirely, only to obtain at least one more byte.
		//
		// There is no such thing as an EOF return. If the end of the file has been
		// reached, the routine has a choice of ERREXIT() or inserting fake data into
		// the buffer. In most cases, generating a warning message and inserting a
		// fake EOI marker is the best course of action --- this will allow the
		// decompressor to output however much of the image is there. However,
		// the resulting error message is misleading if the real problem is an empty
		// input file, so we handle that case specially.
		//
		// In applications that need to be able to suspend compression due to input
		// not being available yet, a false return indicates that no more data can be
		// obtained right now, but more may be forthcoming later. In this situation,
		// the decompressor will return to its caller (with an indication of the
		// number of scanlines it has read, if any). The application should resume
		// decompression after it has loaded more data into the input buffer. Note
		// that there are substantial restrictions on the use of suspension --- see
		// the documentation.
		//
		// When suspending, the decompressor will back up to a convenient restart point
		// (typically the start of the current MCU). next_input_byte & bytes_in_buffer
		// indicate where the restart point will be if the current call returns false.
		// Data beyond this point must be rescanned after resumption, so move it to
		// the front of the buffer rather than discarding it.
		static bool fill_input_buffer(jpeg_decompress cinfo)
		{
			my_source_mgr src=(my_source_mgr)cinfo.src;
			uint nbytes=(uint)src.infile.Read(src.buffer, 0, INPUT_BUF_SIZE);

			if(nbytes<=0)
			{
				if(src.start_of_file) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_INPUT_EMPTY);	// Treat empty input file as fatal error
				WARNMS(cinfo, J_MESSAGE_CODE.JWRN_JPEG_EOF);
				// Insert a fake EOI marker
				src.buffer[0]=(byte)0xFF;
				src.buffer[1]=(byte)JPEG_EOI;
				nbytes=2;
			}

			src.input_bytes=src.buffer;
			src.next_input_byte=0;
			src.bytes_in_buffer=nbytes;
			src.start_of_file=false;

			return true;
		}

		// Skip data --- used to skip over a potentially large amount of
		// uninteresting data (such as an APPn marker).
		//
		// Writers of suspendable-input applications must note that skip_input_data
		// is not granted the right to give a suspension return. If the skip extends
		// beyond the data currently in the buffer, the buffer can be marked empty so
		// that the next read will cause a fill_input_buffer call that can suspend.
		// Arranging for additional bytes to be discarded before reloading the input
		// buffer is the application writer's problem.
		static void skip_input_data(jpeg_decompress cinfo, int num_bytes)
		{
			my_source_mgr src=(my_source_mgr)cinfo.src;

			// Just a dumb implementation for now. Could use Seek() except
			// it doesn't work on pipes. Not clear that being smart is worth
			// any trouble anyway --- large skips are infrequent.
			if(num_bytes>0)
			{
				while(num_bytes>(int)src.bytes_in_buffer)
				{
					num_bytes-=(int)src.bytes_in_buffer;
					fill_input_buffer(cinfo);
					// note we assume that fill_input_buffer will never return false,
					// so suspension need not be handled.
				}
				src.next_input_byte+=num_bytes;
				src.bytes_in_buffer-=(uint)num_bytes;
			}
		}

		// An additional method that can be provided by data source modules is the
		// resync_to_restart method for error recovery in the presence of RST markers.
		// For the moment, this source module just uses the default resync method
		// provided by the JPEG library. That method assumes that no backtracking
		// is possible.

		// Terminate source --- called by jpeg_finish_decompress
		// after all data has been read. Often a no-op.
		//
		// NB: *not* called by jpeg_abort or jpeg_destroy; surrounding
		// application must deal with any cleanup that should happen even
		// for error exit.
		static void term_source(jpeg_decompress cinfo, bool readExtraBytes, out byte[] data)
		{
			if(!readExtraBytes)
			{
				data=null;

				// no work necessary here
				return;
			}

			my_source_mgr src=(my_source_mgr)cinfo.src;

			MemoryStream mem=new MemoryStream();
			mem.Write(src.input_bytes, (int)src.next_input_byte, (int)src.bytes_in_buffer);
			src.next_input_byte+=(int)src.bytes_in_buffer;
			src.bytes_in_buffer=0;

			int nbytes=0;
			while((nbytes=src.infile.Read(src.buffer, 0, INPUT_BUF_SIZE))!=0) mem.Write(src.buffer, 0, nbytes);

			data=mem.ToArray();
		}

		// Prepare for input from a stdio stream.
		// The caller must have already opened the stream, and is responsible
		// for closing it after finishing decompression.
		public static void jpeg_stdio_src(jpeg_decompress cinfo, Stream infile)
		{
			my_source_mgr src=null;

			// The source object and input buffer are made permanent so that a series
			// of JPEG images can be read from the same file by calling jpeg_stdio_src
			// only before the first one. (If we discarded the buffer at the end of
			// one image, we'd likely lose the start of the next one.)
			// This makes it unsafe to use this manager and a different source
			// manager serially with the same JPEG object. Caveat programmer.
			if(cinfo.src==null)
			{ // first time for this JPEG object?
				try
				{
					src=new my_source_mgr();
					src.buffer=new byte[INPUT_BUF_SIZE];
				}
				catch
				{
					ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
				}
				cinfo.src=src;
			}

			src=(my_source_mgr)cinfo.src;
			src.init_source=init_source;
			src.fill_input_buffer=fill_input_buffer;
			src.skip_input_data=skip_input_data;
			src.resync_to_restart=jpeg_resync_to_restart; // use default method
			src.term_source=term_source;
			src.infile=infile;
			src.bytes_in_buffer=0;	// forces fill_input_buffer on first read
			src.input_bytes=null;
			src.next_input_byte=0;	// until buffer loaded
		}
	}
}