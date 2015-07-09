// jpegint.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1991-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file provides common declarations for the various JPEG modules.
// These declarations are considered internal to the JPEG library; most
// applications using the library shouldn't need to include this file.

namespace Free.Ports.LibJpeg
{
	// Declarations for both compression & decompression

	// Operating modes for buffer controllers
	internal enum J_BUF_MODE
	{
		JBUF_PASS_THRU,		// Plain stripwise operation
		// Remaining modes require a full-image buffer to have been created
		JBUF_SAVE_SOURCE,	// Run source subobject only, save output
		JBUF_CRANK_DEST,	// Run dest subobject only, using saved data
		JBUF_SAVE_AND_PASS	// Run both subobjects, save output
	}

	public static partial class libjpeg
	{
		// Declarations for compression modules

		// Master control module
		internal class jpeg_comp_master
		{
			public void_jpeg_compress_Handler prepare_for_pass;
			public void_jpeg_compress_Handler pass_startup;
			public void_jpeg_compress_Handler finish_pass;

			// State variables made visible to other modules
			public bool call_pass_startup;	// True if pass_startup must be called
			public bool is_last_pass;		// True during last pass
		}

		// Main buffer control (downsampled-data buffer)
		internal class jpeg_c_main_controller
		{
			public void_jpeg_compress_J_BUF_MODE_Handler start_pass;
			public void_jpeg_compress_byteAA_uintRef_uint_Handler process_data;
		}

		// Compression preprocessing (downsampling input buffer control)
		internal class jpeg_c_prep_controller
		{
			public void_jpeg_compress_J_BUF_MODE_Handler start_pass;
			public void_jpeg_compress_byteAA_uintRef_uint_byteAAA_uintRef_uint_Handler pre_process_data;
		}

		// Coefficient buffer control
		internal class jpeg_c_coef_contoller
		{
			public void_jpeg_compress_bool_Handler entropy_start_pass;
			public void_jpeg_compress_Handler entropy_finish_pass;
			public bool_jpeg_compress_Handler need_optimization_pass;
			public void_jpeg_compress_J_BUF_MODE_Handler start_pass;
			public bool_jpeg_compress_byteAAA_Handler compress_data;
		}

		// Colorspace conversion
		internal class jpeg_color_converter
		{
			public void_jpeg_compress_Handler start_pass;
			public void_jpeg_compress_byteAA_uint_byteAAA_uint_int_Handler color_convert;
		}

		// Downsampling
		internal class jpeg_downsampler
		{
			public void_jpeg_compress_Handler start_pass;
			public void_jpeg_compress_byteAAA_uint_byteAAA_uint_Handler downsample;

			public bool need_context_rows;	// true if need rows above & below
		}
	}

		// Marker writing
	public class jpeg_marker_writer
	{
		public libjpeg.void_jpeg_compress_Handler write_file_header;
		public libjpeg.void_jpeg_compress_Handler write_frame_header;
		public libjpeg.void_jpeg_compress_Handler write_scan_header;
		public libjpeg.void_jpeg_compress_Handler write_file_trailer;
		public libjpeg.void_jpeg_compress_Handler write_tables_only;
		// These routines are exported to allow insertion of extra markers
		// Probably only COM and APPn markers should be written this way
		public libjpeg.void_jpeg_compress_int_uint_Handler write_marker_header;
		public libjpeg.void_jpeg_compress_int_Handler write_marker_byte;
	}

	public static partial class libjpeg
	{
		// Declarations for decompression modules

		// Master control module
		internal class jpeg_decomp_master
		{
			public void_jpeg_decompress_Handler prepare_for_output_pass;
			public void_jpeg_decompress_Handler finish_output_pass;

			// State variables made visible to other modules
			public bool is_dummy_pass;	// True during 1st pass for 2-pass quant
		}

		// Input control module
		internal class jpeg_input_controller
		{
			public CONSUME_INPUT_jpeg_decompress_Handler consume_input;
			public void_jpeg_decompress_Handler reset_input_controller;
			public void_jpeg_decompress_Handler start_input_pass;
			public void_jpeg_decompress_Handler finish_input_pass;

			// State variables made visible to other modules
			public bool has_multiple_scans;	// True if file has multiple scans
			public bool eoi_reached;		// True when EOI has been consumed
		}

		// Main buffer control (downsampled-data buffer)
		internal class jpeg_d_main_controller
		{
			public void_jpeg_decompress_J_BUF_MODE_Handler start_pass;
			public void_jpeg_decompress_byteAA_uintRef_uint_Handler process_data;
		}

		// Coefficient buffer control
		internal class jpeg_d_coef_controller
		{
			public void_jpeg_decompress_Handler calc_output_dimensions;
			public void_jpeg_decompress_Handler start_input_pass;
			public CONSUME_INPUT_jpeg_decompress_Handler consume_data;
			public void_jpeg_decompress_Handler start_output_pass;
			public CONSUME_INPUT_jpeg_decompress_byteAAA_Handler decompress_data;
		}

		// Decompression postprocessing (color quantization buffer control)
		internal class jpeg_d_post_controller
		{
			public void_jpeg_decompress_J_BUF_MODE_Handler start_pass;
			public void_jpeg_decompress_byteAAA_uintRef_uint_byteAA_uint_uintRef_uint_Handler post_process_data;
		}
	}

	// Marker reading & parsing
	public class jpeg_marker_reader
	{
		public libjpeg.void_jpeg_decompress_Handler reset_marker_reader;
		// Read markers until SOS or EOI.
		// Returns same codes as are defined for jpeg_consume_input:
		// JPEG_SUSPENDED, JPEG_REACHED_SOS, or JPEG_REACHED_EOI.
		public libjpeg.CONSUME_INPUT_jpeg_decompress_Handler read_markers;
		// Read a restart marker --- exported for use by entropy decoder only
		public libjpeg.jpeg_marker_parser_method read_restart_marker;

		// State of marker reader --- nominally internal, but applications
		// supplying COM or APPn handlers might like to know the state.
		public bool saw_SOI;			// found SOI?
		public bool saw_SOF;			// found SOF?
		public int next_restart_num;	// next restart number expected (0-7)
		public uint discarded_bytes;	// # of bytes skipped looking for a marker
	}

	public static partial class libjpeg
	{
		// Common fields between sequential, progressive and lossless Huffman entropy
		// decoder master structs.
		internal class jpeg_entropy_decoder
		{
			public bool insufficient_data;		// set true after emmitting warning
			// These fields are loaded into local variables at start of each MCU.
			// In case of suspension, we exit WITHOUT updating them.
			public bitread_perm_state bitstate;	// Bit buffer at start of MCU
		}

		// Upsampling (note that upsampler must also call color converter)
		internal class jpeg_upsampler
		{
			public void_jpeg_decompress_Handler start_pass;
			public void_jpeg_decompress_byteAAA_uintRef_uint_byteAA_uint_uintRef_uint_Handler upsample;

#if UPSCALING_CONTEXT
			public bool need_context_rows;	// true if need rows above & below
#endif
		}

		// Colorspace conversion
		internal class jpeg_color_deconverter
		{
			public void_jpeg_decompress_Handler start_pass;
			public void_jpeg_decompress_byteAAA_uint_byteAA_uint_int_Handler color_convert;
		}

		// Color quantization or color precision reduction
		internal class jpeg_color_quantizer
		{
			public void_jpeg_decompress_bool_Handler start_pass;
			public void_jpeg_decompress_byteAA_uint_byteAA_uint_int_Handler color_quantize;
			public void_jpeg_decompress_Handler finish_pass;
			public void_jpeg_decompress_Handler new_color_map;
		}
	}
}
