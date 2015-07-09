// jcinit.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1991-1997, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains initialization logic for the JPEG compressor.
// This routine is in charge of selecting the modules to be executed and
// making an initialization call to each one.
//
// Logically, this code belongs in jcmaster.cs.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Master selection of compression modules.
		// This is done once at the start of processing an image. We determine
		// which modules will be used and give them appropriate initialization calls.
		static void jinit_compress_master(jpeg_compress cinfo)
		{
			// Initialize master control (includes parameter checking/processing)
			jinit_c_master_control(cinfo, false); // full compression

			// Initialize compression codec
			jinit_c_codec(cinfo);

			// Preprocessing
			if(!cinfo.raw_data_in)
			{
				jinit_color_converter(cinfo);
				jinit_downsampler(cinfo);
				jinit_c_prep_controller(cinfo, false); // never need full buffer here
			}

			jinit_c_main_controller(cinfo, false); // never need full buffer here

			jinit_marker_writer(cinfo);

			// Write the datastream header (SOI) immediately.
			// Frame and scan headers are postponed till later.
			// This lets application insert special markers after the SOI.
			cinfo.marker.write_file_header(cinfo);
		}
	}
}
