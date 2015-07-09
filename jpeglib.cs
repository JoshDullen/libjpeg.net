// jpeglib.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1991-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file defines the application interface for the JPEG library.

namespace Free.Ports.LibJpeg
{
	// Known codec processes.
	public enum J_CODEC_PROCESS
	{
		JPROC_SEQUENTIAL,	// baseline/extended sequential DCT
		JPROC_PROGRESSIVE,	// progressive DCT
		JPROC_LOSSLESS		// lossless (sequential)
	}

	// Known color spaces.
	public enum J_COLOR_SPACE
	{
		JCS_UNKNOWN,	// error/unspecified
		JCS_GRAYSCALE,	// monochrome
		JCS_RGB,		// red/green/blue
		JCS_YCbCr,		// Y/Cb/Cr (also known as YUV)
		JCS_CMYK,		// C/M/Y/K
		JCS_YCCK,		// Y/Cb/Cr/K
	}

	// Dithering options for decompression.
	public enum J_DITHER_MODE
	{
		JDITHER_NONE,		// no dithering
		JDITHER_ORDERED,	// simple ordered dither
		JDITHER_FS			// Floyd-Steinberg error diffusion dither
	}

	public enum J_SUBSAMPLING
	{
		NONE=0,
		JPEG444=0,
		JPEG422=1,
		JPEG420=2
	}

	// Values of global_state field (jdapi.cs has some dependencies on ordering!)
	public enum STATE
	{
		None=0,
		CSTART=100,		// after create_compress
		CSCANNING=101,	// start_compress done, write_scanlines OK
		CRAW_OK=102,	// start_compress done, write_raw_data OK
		CWRCOEFS=103,	// jpeg_write_coefficients done
		DSTART=200,		// after create_decompress
		DINHEADER=201,	// reading header markers, no SOS yet
		DREADY=202,		// found SOS, ready for start_decompress
		DPRELOAD=203,	// reading multiscan file in start_decompress
		DPRESCAN=204,	// performing dummy pass for 2-pass quant
		DSCANNING=205,	// start_decompress done, read_scanlines OK
		DRAW_OK=206,	// start_decompress done, read_raw_data OK
		DBUFIMAGE=207,	// expecting jpeg_start_output
		DBUFPOST=208,	// looking for SOS/EOI in jpeg_finish_output
		DRDCOEFS=209,	// reading file in jpeg_read_coefficients
		DSTOPPING=210	// looking for EOI in jpeg_finish_decompress
	}

	public enum CONSUME_INPUT
	{
		JPEG_SUSPENDED=0,		// Suspended due to lack of input data
		JPEG_REACHED_SOS=1,		// Reached start of new scan
		JPEG_REACHED_EOI=2,		// Reached end of image

		JPEG_HEADER_OK=1,			// Found valid image datastream
		JPEG_HEADER_TABLES_ONLY=2,	// Found valid table-specs-only datastream

		JPEG_ROW_COMPLETED=3,	// Completed one iMCU row
		JPEG_SCAN_COMPLETED=4	// Completed last iMCU row of a scan
	}

	public static partial class libjpeg
	{
		#region Delegates
		public delegate void void_jpeg_common_Handler(jpeg_common cinfo);
		public delegate void void_jpeg_common_int_Handler(jpeg_common cinfo, int i1);
		public delegate string string_jpeg_common_Handler(jpeg_common cinfo);

		public delegate void void_jpeg_compress_Handler(jpeg_compress cinfo);
		public delegate bool bool_jpeg_compress_Handler(jpeg_compress cinfo);
		public delegate void void_jpeg_compress_int_Handler(jpeg_compress cinfo, int i1);
		public delegate void void_jpeg_compress_int_uint_Handler(jpeg_compress cinfo, int i1, uint ui1);

		public delegate void void_jpeg_decompress_Handler(jpeg_decompress cinfo);
		public delegate void void_jpeg_decompress_bool_byteAOut_Handler(jpeg_decompress cinfo, bool readExtraBytes, out byte[] data);
		public delegate bool bool_jpeg_decompress_Handler(jpeg_decompress cinfo);
		public delegate bool bool_jpeg_decompress_int_Handler(jpeg_decompress cinfo, int i1);
		public delegate int int_jpeg_decompress_Handler(jpeg_decompress cinfo);
		public delegate void void_jpeg_decompress_int_Handler(jpeg_decompress cinfo, int i1);

		// Routine signature for application-supplied marker processing methods.
		// Need not pass marker code since it is stored in cinfo.unread_marker.
		public delegate bool jpeg_marker_parser_method(jpeg_decompress cinfo);

		public delegate CONSUME_INPUT CONSUME_INPUT_jpeg_decompress_Handler(jpeg_decompress cinfo);

		// internal
		internal delegate void void_jpeg_compress_bool_Handler(jpeg_compress cinfo, bool b1);
		internal delegate void void_jpeg_compress_J_BUF_MODE_Handler(jpeg_compress cinfo, J_BUF_MODE mode);
		internal delegate void void_jpeg_compress_byteA_byteA_uint_Handler(jpeg_compress cinfo, byte[] input_buf, byte[] output_buf, uint width);
		internal delegate void void_jpeg_compress_byteAA_uintRef_uint_Handler(jpeg_compress cinfo, byte[][] input_buf, ref uint in_row_ctr, uint in_rows_avail);
		internal delegate bool bool_jpeg_compress_byteAAA_Handler(jpeg_compress cinfo, byte[][][] input_buf);
		internal delegate void void_jpeg_compress_byteAA_uint_byteAAA_uint_int_Handler(jpeg_compress cinfo, byte[][] input_buf, uint in_row_index, byte[][][] output_buf, uint output_row, int num_rows);
		internal delegate void void_jpeg_compress_byteAAA_uint_byteAAA_uint_Handler(jpeg_compress cinfo, byte[][][] input_buf, uint in_row_index, byte[][][] output_buf, uint out_row_group_index);
		internal delegate void void_jpeg_compress_byteAA_uintRef_uint_byteAAA_uintRef_uint_Handler(jpeg_compress cinfo, byte[][] input_buf, ref uint in_row_ctr, uint in_rows_avail,
			byte[][][] output_buf, ref uint out_row_group_ctr, uint out_row_groups_avail);
		internal delegate uint uint_jpeg_compress_intAAA_uint_uint_uint_Handler(jpeg_compress cinfo, int[][][] diff_buf, uint MCU_row_num, uint MCU_col_num, uint nMCU);
		internal delegate bool bool_jpeg_compress_shortAAA_Handler(jpeg_compress cinfo, short[][] MCU_data);
		internal delegate void void_jpeg_compress_jpeg_component_info_byteAA_JBLOCKAA_int_uint_uint_uint_Handler(jpeg_compress cinfo, jpeg_component_info compptr, byte[][] sample_data, short[][] coef_blocks, int coef_offset, uint start_row, uint start_col, uint num_blocks);

		internal delegate void void_jpeg_decompress_bool_Handler(jpeg_decompress cinfo, bool b1);
		internal delegate void void_jpeg_decompress_J_BUF_MODE_Handler(jpeg_decompress cinfo, J_BUF_MODE mode);
		internal delegate void void_jpeg_decompress_byteAA_byteAA_int_Handler(jpeg_decompress cinfo, byte[][] input_buf, byte[][] output_buf, int num_rows);
		internal delegate void void_jpeg_decompress_byteAA_uint_byteAA_uint_int_Handler(jpeg_decompress cinfo, byte[][] input_buf, uint input_row, byte[][] output_buf, uint output_row, int num_rows);
		internal delegate void void_jpeg_decompress_byteAA_uintRef_uint_Handler(jpeg_decompress cinfo, byte[][] output_buf, ref uint out_row_ctr, uint out_rows_avail);
		internal delegate CONSUME_INPUT CONSUME_INPUT_jpeg_decompress_byteAAA_Handler(jpeg_decompress cinfo, byte[][][] output_buf);
		internal delegate void void_jpeg_decompress_byteAAA_uintRef_uint_byteAA_uint_uintRef_uint_Handler(jpeg_decompress cinfo, byte[][][] input_buf, ref uint in_row_group_ctr, uint in_row_groups_avail,
			byte[][] output_buf, uint output_buf_offset, ref uint out_row_ctr, uint out_rows_avail);
		internal delegate void void_jpeg_decompress_byteAAA_uint_byteAA_int_Handler(jpeg_decompress cinfo, byte[][][] input_buf, uint input_row, byte[][] output_buf, int num_rows);
		internal delegate void void_jpeg_decompress_byteAAA_uint_byteAA_uint_int_Handler(jpeg_decompress cinfo, byte[][][] input_buf, uint input_row, byte[][] output_buf, uint output_row, int num_rows);
		internal delegate void void_jpeg_decompress_intA_byteA_uint_Handler(jpeg_decompress cinfo, int[] diff_buf, byte[] output_buf, uint width);
		internal delegate uint uint_jpeg_decompress_intAAA_uint_uint_uint_Handler(jpeg_decompress cinfo, int[][][] diff_buf, uint MCU_row_num, uint MCU_col_num, uint nMCU);
		internal delegate bool bool_jpeg_decompress_shortAA_Handler(jpeg_decompress cinfo, short[][] MCU_data);
		#endregion

		// Version ID for the JPEG library.
		// Might be useful for tests like "if(JPEG_LIB_VERSION>=60)".
		public const int JPEG_LIB_VERSION=62;	// Version 6b

		// Various constants determining the sizes of things.
		// All of these are specified by the JPEG standard, so don't change them
		// if you want to be compatible.
		public const int DCTSIZE=8;				// The basic DCT block is 8x8 samples
		public const int DCTSIZE2=64;			// DCTSIZE squared; # of elements in a block
		public const int NUM_QUANT_TBLS=4;		// Quantization tables are numbered 0..3
		public const int NUM_HUFF_TBLS=4;		// Huffman tables are numbered 0..3
		public const int NUM_ARITH_TBLS=16;		// Arith-coding tables are numbered 0..15
		public const int MAX_COMPS_IN_SCAN=4;	// JPEG limit on # of components in one scan
		public const int MAX_SAMP_FACTOR=4;		// JPEG limit on sampling factors
		// Unfortunately, some bozo at Adobe saw no reason to be bound by the standard;
		// the PostScript DCT filter can emit files with many more than 10 blocks/MCU.
		// If you happen to run across such a file, you can up D_MAX_BLOCKS_IN_MCU
		// to handle it. However, we strongly discourage changing C_MAX_BLOCKS_IN_MCU;
		// just because Adobe sometimes emits noncompliant files doesn't mean you should too.
		public const int C_MAX_BLOCKS_IN_MCU=10;	// compressor's limit on blocks per MCU
		public const int D_MAX_BLOCKS_IN_MCU=10;	// decompressor's limit on blocks per MCU
	}
	// Types for JPEG compression parameters and working tables.

	// DCT coefficient quantization tables.
	public class JQUANT_TBL
	{
		// This array gives the coefficient quantizers in natural array order
		// (not the zigzag order in which they are stored in a JPEG DQT marker).
		// CAUTION: IJG versions prior to v6a kept this array in zigzag order.
		public ushort[] quantval=new ushort[libjpeg.DCTSIZE2];	// quantization step for each coefficient
		// This field is used only during compression. It's initialized false when
		// the table is created, and set true when it's been output to the file.
		// You could suppress output of a table by setting this to true.
		// (See jpeg_suppress_tables for an example.)
		public bool sent_table;	// true when table has been output
	}

	// Huffman coding tables.
	public class JHUFF_TBL
	{
		// These two fields directly represent the contents of a JPEG DHT marker
		public byte[] bits=new byte[17];		// bits[k] = # of symbols with codes of
		// length k bits; bits[0] is unused
		public byte[] huffval=new byte[256];	// The symbols, in order of incr code length
		// This field is used only during compression. It's initialized false when
		// the table is created, and set true when it's been output to the file.
		// You could suppress output of a table by setting this to true.
		// (See jpeg_suppress_tables for an example.)
		public bool sent_table;	// true when table has been output
	}

	// Basic info about one component (color channel).
	public class jpeg_component_info
	{
		// These values are fixed over the whole image.
		// For compression, they must be supplied by parameter setup;
		// for decompression, they are read from the SOF marker.
		public int component_id;	// identifier for this component (0..255)
		public int component_index;	// its index in SOF or cinfo.comp_info[]
		public int h_samp_factor;	// horizontal sampling factor (1..4)
		public int v_samp_factor;	// vertical sampling factor (1..4)
		public int quant_tbl_no;	// quantization table selector (0..3)
		// These values may vary between scans.
		// For compression, they must be supplied by parameter setup;
		// for decompression, they are read from the SOS marker.
		// The decompressor output side may not use these variables.
		public int dc_tbl_no;		// DC entropy table selector (0..3)
		public int ac_tbl_no;		// AC entropy table selector (0..3)

		// Remaining fields should be treated as private by applications.

		// These values are computed during compression or decompression startup:
		// Component's size in DCT blocks.
		// Any dummy blocks added to complete an MCU are not counted; therefore
		// these values do not depend on whether a scan is interleaved or not.
		public uint width_in_blocks;
		public uint height_in_blocks;
		// Size of a DCT block in samples. Always DCTSIZE for compression.
		// For decompression this is the size of the output from one DCT block,
		// reflecting any scaling we choose to apply during the IDCT step.
		// Values of 1,2,4,8 are likely to be supported. Note that different
		// components may receive different IDCT scalings.
		public uint DCT_scaled_size;
		// The downsampled dimensions are the component's actual, unpadded number
		// of samples at the main buffer (preprocessing/compression interface), thus
		// downsampled_width = ceil(image_width * Hi/Hmax)
		// and similarly for height. For decompression, IDCT scaling is included, so
		// downsampled_width = ceil(image_width * Hi/Hmax * DCT_scaled_size/DCTSIZE)
		public uint downsampled_width;	// actual width in samples
		public uint downsampled_height;	// actual height in samples
		// This flag is used only for decompression. In cases where some of the
		// components will be ignored (eg grayscale output from YCbCr image),
		// we can skip most computations for the unused components.
		public bool component_needed;	// do we need the value of this component?

		// These values are computed before starting a scan of the component.
		// The decompressor output side may not use these variables.
		public int MCU_width;			// number of blocks per MCU, horizontally
		public int MCU_height;			// number of blocks per MCU, vertically
		public uint MCU_blocks;			// MCU_width * MCU_height
		public int MCU_sample_width;	// MCU width in samples, MCU_width*DCT_scaled_size
		public int last_col_width;		// # of non-dummy blocks across in last MCU
		public int last_row_height;		// # of non-dummy blocks down in last MCU

		// Saved quantization table for component; null if none yet saved.
		// See jdinput.cs comments about the need for this information.
		// This field is currently used only for decompression.
		public JQUANT_TBL quant_table;

		// Private per-component storage for DCT or IDCT subsystem.
		public int[] dct_table;

#if UPSCALING_CONTEXT
		public bool notFirst=false;
		public bool doContext=false;
#endif
	}

	// The script for encoding a multiple-scan file is an array of these:
	public class jpeg_scan_info
	{
		public int comps_in_scan;	// number of components encoded in this scan
		public int[] component_index=new int[libjpeg.MAX_COMPS_IN_SCAN]; // their SOF/comp_info[] indexes
		public int Ss, Se;	// progressive JPEG spectral selection parms
		// lossless JPEG predictor select parm (Ss)
		public int Ah, Al;	// progressive JPEG successive approx. parms
		// lossless JPEG point transform parm (Al)
	}

	// The decompressor can save APPn and COM markers in a list of these:
	public class jpeg_marker_struct
	{
		public jpeg_marker_struct next;	// next in list, or null
		public byte marker;				// marker code: JPEG_COM, or JPEG_APP0+n
		public uint original_length;	// # bytes of data in the file
		public uint data_length;		// # bytes of data saved at data[]
		public byte[] data;				// the data contained in the marker
		// the marker length word is not counted in data_length or original_length
	}

	public static partial class libjpeg
	{
		// DCT/IDCT algorithm options.
		const int JDCT_IFAST=1;	// faster, less accurate integer method
	}

	// Routines that are to be used by both halves of the library are declared
	// to receive a pointer to this structure. There are no actual instances of
	// jpeg_common, only of jpeg_compress and jpeg_decompress.
	public class jpeg_common
	{
		public jpeg_error_mgr err;			// Error handler module
		public jpeg_progress_mgr progress;	// Progress monitor, or null if none
		public object client_data;			// Available for use by application
		public bool is_decompressor;		// So common code can tell which is which
		public STATE global_state;			// For checking call sequence validity
		public EXIF exif;
	}

	#region jpeg_compress
	public class jpeg_compress : jpeg_common
	{
		// Destination for compressed data
		public jpeg_destination_mgr dest;

		// Description of source image --- these fields must be filled in by
		// outer application before starting compression. in_color_space must
		// be correct before you can even call jpeg_set_defaults().

		public uint image_width;		// input image width
		public uint image_height;		// input image height
		public int input_components;	// # of color components in input image
		public J_COLOR_SPACE in_color_space;	// colorspace of input image

		public double input_gamma;	// image gamma of input image

		// Compression parameters --- these fields must be set before calling
		// jpeg_start_compress(). We recommend calling jpeg_set_defaults() to
		// initialize everything to reasonable defaults, then changing anything
		// the application specifically wants to change. That way you won't get
		// burnt when new parameters are added. Also note that there are several
		// helper routines to simplify changing parameters.

		public bool lossless;		// true=lossless encoding, false=lossy

		public int data_precision;	// bits of precision in image data

		public int num_components;	// # of color components in JPEG image
		public J_COLOR_SPACE jpeg_color_space; // colorspace of JPEG image

		public jpeg_component_info[] comp_info;
		// comp_info[i] describes component that appears i'th in SOF

		public JQUANT_TBL[] quant_tbl_ptrs=new JQUANT_TBL[libjpeg.NUM_QUANT_TBLS];
		// ptrs to coefficient quantization tables, or null if not defined

		public JHUFF_TBL[] dc_huff_tbl_ptrs=new JHUFF_TBL[libjpeg.NUM_HUFF_TBLS];
		public JHUFF_TBL[] ac_huff_tbl_ptrs=new JHUFF_TBL[libjpeg.NUM_HUFF_TBLS];
		// ptrs to Huffman coding tables, or null if not defined

		public byte[] arith_dc_L=new byte[libjpeg.NUM_ARITH_TBLS];	// L values for DC arith-coding tables
		public byte[] arith_dc_U=new byte[libjpeg.NUM_ARITH_TBLS];	// U values for DC arith-coding tables
		public byte[] arith_ac_K=new byte[libjpeg.NUM_ARITH_TBLS];	// Kx values for AC arith-coding tables

		public int num_scans;		// # of entries in scan_info array
		public jpeg_scan_info[] scan_info;	// script for multi-scan file, or null
		// The default value of scan_info is null, which causes a single-scan
		// sequential JPEG file to be emitted. To create a multi-scan file,
		// set num_scans and scan_info to point to an array of scan definitions.

		public bool raw_data_in;		// true=caller supplies downsampled data
		public bool arith_code;			// true=arithmetic coding, false=Huffman
		public bool optimize_coding;	// true=optimize entropy encoding parms
		public bool CCIR601_sampling;	// true=first samples are cosited
		public int smoothing_factor;	// 1..100, or 0 for no input smoothing

#if DCT_FLOAT_SUPPORTED
		public bool useFloatDCT;		// use float DCT instead of integer version
#endif

		// The restart interval can be specified in absolute MCUs by setting
		// restart_interval, or in MCU rows by setting restart_in_rows
		// (in which case the correct restart_interval will be figured
		// for each scan).
		public uint restart_interval;	// MCUs per restart, or 0 for no restart
		public int restart_in_rows;		// if > 0, MCU rows per restart interval

		// Parameters controlling emission of special markers.

		public bool write_JFIF_header;	// should a JFIF marker be written?
		public byte JFIF_major_version;	// What to write for the JFIF version number
		public byte JFIF_minor_version;
		// These three values are not used by the JPEG code, merely copied
		// into the JFIF APP0 marker. density_unit can be 0 for unknown,
		// 1 for dots/inch, or 2 for dots/cm. Note that the pixel aspect
		// ratio is defined by X_density/Y_density even when density_unit=0.
		public byte density_unit;		// JFIF code for pixel size units
		public ushort X_density;		// Horizontal pixel density
		public ushort Y_density;		// Vertical pixel density
		public bool write_Adobe_marker;	// should an Adobe marker be written?

		// State variable: index of next scanline to be written to
		// jpeg_write_scanlines(). Application may use this to control its
		// processing loop, e.g., "while (next_scanline < image_height)".
		public uint next_scanline;		// 0 .. image_height-1

		// Remaining fields are known throughout compressor, but generally
		// should not be touched by a surrounding application.

		// These fields are computed during compression startup
		//+ bool progressive_mode;			// TRUE if scan script uses progressive mode
		public uint DCT_size;			// size of DCT/data unit in samples
		public J_CODEC_PROCESS process;	// encoding process of JPEG image

		public int max_h_samp_factor;	// largest h_samp_factor
		public int max_v_samp_factor;	// largest v_samp_factor

		public uint total_iMCU_rows;	// # of iMCU rows to be input to codec
		// The codec receives data in units of MCU rows as defined for fully
		// interleaved scans (whether the JPEG file is interleaved or not).
		// There are v_samp_factor * data_unit sample rows of each component in an
		// "iMCU" (interleaved MCU) row.

		// These fields are valid during any one scan.
		// They describe the components and MCUs actually appearing in the scan.
		public int comps_in_scan;		// # of JPEG components in this scan 
		public jpeg_component_info[] cur_comp_info=new jpeg_component_info[libjpeg.MAX_COMPS_IN_SCAN];
		// *cur_comp_info[i] describes component that appears i'th in SOS

		public uint MCUs_per_row;		// # of MCUs across the image
		public uint MCU_rows_in_scan;	// # of MCU rows in the image

		public int block_in_MCU;		// # of DCT blocks per MCU
		public int[] MCU_membership=new int[libjpeg.C_MAX_BLOCKS_IN_MCU];
		// MCU_membership[i] is index in cur_comp_info of component owning
		// i'th block in an MCU

		public int Ss, Se, Ah, Al;		// progressive JPEG parameters for scan

		// Links to compression subobjects (methods and private variables of modules)
		internal libjpeg.jpeg_comp_master master;
		internal libjpeg.jpeg_c_main_controller main;
		internal libjpeg.jpeg_c_prep_controller prep;
		internal libjpeg.jpeg_c_coef_contoller coef;
		public jpeg_marker_writer marker;
		internal libjpeg.jpeg_color_converter cconvert;
		internal libjpeg.jpeg_downsampler downsample;
		//+ internal libjpeg.jpeg_forward_dct fdct; // moved to jpeg_lossy_c_codec.fdct_private
		//+ internal libjpeg.jpeg_entropy_encoder entropy; // moved to jpeg_lossy/lossless_c_codec.entropy_private
		public jpeg_scan_info[] script_space;	// workspace for jpeg_simple_progression
		public int script_space_size;
	}
	#endregion

	#region jpeg_decompress
	public class jpeg_decompress : jpeg_common
	{
		// Source of compressed data
		public jpeg_source_mgr src;

		// Basic description of image --- filled in by jpeg_read_header().
		// Application may inspect these values to decide how to process image.

		public uint image_width;		// nominal image width (from SOF marker)
		public uint image_height;		// nominal image height
		public int num_components;		// # of color components in JPEG image
		public J_COLOR_SPACE jpeg_color_space;	// colorspace of JPEG image

		// Decompression processing parameters --- these fields must be set before
		// calling jpeg_start_decompress(). Note that jpeg_read_header() initializes
		// them to default values.
		public J_COLOR_SPACE out_color_space;	// colorspace for output

		public double output_gamma;		// image gamma wanted in output

		public bool buffered_image;		// true=multiple output passes
		public bool raw_data_out;		// true=downsampled data wanted

		public bool do_fancy_upsampling;// true=apply fancy upsampling
		public bool do_block_smoothing;	// true=apply interblock smoothing

		public bool quantize_colors;	// true=colormapped output wanted
		// the following are ignored if not quantize_colors:
		public J_DITHER_MODE dither_mode;		// type of color dithering to use
		public bool two_pass_quantize;			// true=use two-pass color quantization
		public int desired_number_of_colors;	// max # colors to use in created colormap
		// these are significant only in buffered-image mode:
		public bool enable_1pass_quant;		// enable future use of 1-pass quantizer
		public bool enable_external_quant;	// enable future use of external colormap
		public bool enable_2pass_quant;		// enable future use of 2-pass quantizer

		// Description of actual output image that will be returned to application.
		// These fields are computed by jpeg_start_decompress().
		// You can also use jpeg_calc_output_dimensions() to determine these values
		// in advance of calling jpeg_start_decompress().

		public uint output_width;	// scaled image width
		public uint output_height;	// scaled image height
		public int out_color_components;	// # of color components in out_color_space
		public int output_components;		// # of color components returned
		// output_components is 1 (a colormap index) when quantizing colors;
		// otherwise it equals out_color_components.
		public int rec_outbuf_height;		// min recommended height of scanline buffer
		// If the buffer passed to jpeg_read_scanlines() is less than this many rows
		// high, space and time will be wasted due to unnecessary data copying.
		// Usually rec_outbuf_height will be 1 or 2, at most 4.

		// When quantizing colors, the output colormap is described by these fields.
		// The application can supply a colormap by setting colormap non-null before
		// calling jpeg_start_decompress; otherwise a colormap is created during
		// jpeg_start_decompress or jpeg_start_output.
		// The map has out_color_components rows and actual_number_of_colors columns.
		public int actual_number_of_colors;	// number of entries in use
		public byte[][] colormap;			// The color map as a 2-D pixel array

		// State variables: these variables indicate the progress of decompression.
		// The application may examine these but must not modify them.

		// Row index of next scanline to be read from jpeg_read_scanlines().
		// Application may use this to control its processing loop, e.g.,
		// "while (output_scanline < output_height)".
		public uint output_scanline;	// 0 .. output_height-1

		// Current input scan number and number of iMCU rows completed in scan.
		// These indicate the progress of the decompressor input side.
		public int input_scan_number;	// Number of SOS markers seen so far
		public uint input_iMCU_row;		// Number of iMCU rows completed

		// The "output scan number" is the notional scan being displayed by the
		// output side. The decompressor will not allow output scan/row number
		// to get ahead of input scan/row, but it can fall arbitrarily far behind.
		public int output_scan_number;		// Nominal scan number being displayed
		public uint output_iMCU_row;	// Number of iMCU rows read

		// Current progression status. coef_bits[c][i] indicates the precision
		// with which component c's DCT coefficient i (in zigzag order) is known.
		// It is -1 when no data has yet been received, otherwise it is the point
		// transform (shift) value for the most recent scan of the coefficient
		// (thus, 0 at completion of the progression).
		// This pointer is null when reading a non-progressive file.
		public int[][] coef_bits=new int[libjpeg.DCTSIZE2][];	// -1 or current Al value for each coef

		// Internal JPEG parameters --- the application usually need not look at
		// these fields. Note that the decompressor output side may not use
		// any parameters that can change between scans.

		// Quantization and Huffman tables are carried forward across input
		// datastreams when processing abbreviated JPEG datastreams.

		public JQUANT_TBL[] quant_tbl_ptrs=new JQUANT_TBL[libjpeg.NUM_QUANT_TBLS];
		// ptrs to coefficient quantization tables, or null if not defined

		public JHUFF_TBL[] dc_huff_tbl_ptrs=new JHUFF_TBL[libjpeg.NUM_HUFF_TBLS];
		public JHUFF_TBL[] ac_huff_tbl_ptrs=new JHUFF_TBL[libjpeg.NUM_HUFF_TBLS];
		// ptrs to Huffman coding tables, or null if not defined

		// These parameters are never carried across datastreams, since they
		// are given in SOF/SOS markers or defined to be reset by SOI.

		public int data_precision;		// bits of precision in image data

		public jpeg_component_info[] comp_info;
		// comp_info[i] describes component that appears i'th in SOF

		//+ bool progressive_mode;		// TRUE if SOFn specifies progressive mode
		public bool arith_code;			// true=arithmetic coding, false=Huffman

		public byte[] arith_dc_L=new byte[libjpeg.NUM_ARITH_TBLS]; // L values for DC arith-coding tables
		public byte[] arith_dc_U=new byte[libjpeg.NUM_ARITH_TBLS]; // U values for DC arith-coding tables
		public byte[] arith_ac_K=new byte[libjpeg.NUM_ARITH_TBLS]; // Kx values for AC arith-coding tables

		public uint restart_interval;		// MCUs per restart interval, or 0 for no restart

		// These fields record data obtained from optional markers recognized by
		// the JPEG library.
		public bool saw_JFIF_marker;		// true iff a JFIF APP0 marker was found
		// Data copied from JFIF marker; only valid if saw_JFIF_marker is true:
		public byte JFIF_major_version;		// JFIF version number
		public byte JFIF_minor_version;
		public byte density_unit;			// JFIF code for pixel size units
		public ushort X_density;			// Horizontal pixel density
		public ushort Y_density;			// Vertical pixel density
		public bool saw_Adobe_marker;		// true iff an Adobe APP14 marker was found
		public byte Adobe_transform;		// Color transform code from Adobe marker

		public bool CCIR601_sampling;		// true=first samples are cosited

		// Aside from the specific data retained from APPn markers known to the
		// library, the uninterpreted contents of any or all APPn and COM markers
		// can be saved in a list for examination by the application.
		public jpeg_marker_struct marker_list;	// Head of list of saved markers

		// Remaining fields are known throughout decompressor, but generally
		// should not be touched by a surrounding application.

		// These fields are computed during decompression startup
		public int DCT_size;				// size of DCT/data unit in samples
		public J_CODEC_PROCESS process;		// decoding process of JPEG image

		public int max_h_samp_factor;	// largest h_samp_factor
		public int max_v_samp_factor;	// largest v_samp_factor

		public int min_DCT_scaled_size;	// smallest DCT_scaled_size of any component

		public uint total_iMCU_rows;	// # of iMCU rows in image

		// The coefficient controller's input and output progress is measured in
		// units of "iMCU" (interleaved MCU) rows. These are the same as MCU rows
		// in fully interleaved JPEG scans, but are used whether the scan is
		// interleaved or not. We define an iMCU row as v_samp_factor DCT block
		// rows of each component. Therefore, the IDCT output contains
		// v_samp_factor*DCT_scaled_size sample rows of a component per iMCU row.
		public byte[] sample_range_limit; // table for fast range-limiting

		// These fields are valid during any one scan.
		// They describe the components and MCUs actually appearing in the scan.
		// Note that the decompressor output side must not use these fields.
		public int comps_in_scan;		// # of JPEG components in this scan
		public jpeg_component_info[] cur_comp_info=new jpeg_component_info[libjpeg.MAX_COMPS_IN_SCAN];
		// *cur_comp_info[i] describes component that appears i'th in SOS

		public uint MCUs_per_row;		// # of MCUs across the image
		public uint MCU_rows_in_scan;	// # of MCU rows in the image

		public int blocks_in_MCU;		// # of DCT blocks per MCU
		public int[] MCU_membership=new int[libjpeg.D_MAX_BLOCKS_IN_MCU];
		// MCU_membership[i] is index in cur_comp_info of component owning
		// i'th data unit in an MCU

		public int Ss, Se, Ah, Al;	// progressive JPEG parms for scan

		// This field is shared between entropy decoder and marker parser.
		// It is either zero or the code of a JPEG marker that has been
		// read from the data source, but has not yet been processed.
		public int unread_marker;

		// src.term_source will read the extra bytes and save them if readExtraBytesAtEndOfStream is set true
		public bool readExtraBytesAtEndOfStream;
		public byte[] extraBytesAtEndOfStream;

		// Links to decompression subobjects (methods, private variables of modules)
		internal libjpeg.jpeg_decomp_master master;
		internal libjpeg.jpeg_d_main_controller main;
		internal libjpeg.jpeg_d_coef_controller coef;
		internal libjpeg.jpeg_d_post_controller post;
		internal libjpeg.jpeg_input_controller inputctl;
		public jpeg_marker_reader marker;
		//+ internal libjpeg.jpeg_entropy_decoder entropy; // moved to jpeg_lossy/lossless_d_codec.entropy_private
		//+ internal libjpeg.jpeg_inverse_dct idct; // moved to jpeg_lossy_d_codec.idct_private
		internal libjpeg.jpeg_upsampler upsample;
		internal libjpeg.jpeg_color_deconverter cconvert;
		internal libjpeg.jpeg_color_quantizer cquantize;
	}
	#endregion

	// "Object" declarations for JPEG modules that may be supplied or called
	// directly by the surrounding application.
	// As with all objects in the JPEG library, these structs only define the
	// publicly visible methods and state variables of a module. Additional
	// private fields may exist after the public ones.

	// Error handler object
	public class jpeg_error_mgr
	{
		// Error exit handler: does not return to caller
		public libjpeg.void_jpeg_common_Handler error_exit;

		//// Conditionally emit a trace or warning message
		public libjpeg.void_jpeg_common_int_Handler emit_message;

		// Routine that actually outputs a trace or error message
		public libjpeg.void_jpeg_common_Handler output_message;

		//// Format a message string for the most recent JPEG error or message
		public libjpeg.string_jpeg_common_Handler format_message;

		//// Reset error state variables at start of a new image
		public libjpeg.void_jpeg_common_Handler reset_error_mgr;

		// The message ID code and any parameters are saved here.
		// A message can have one string parameter or up to 8 int parameters.
		public int msg_code;

		public object[] msg_parm=new object[8];

		// Standard state variables for error facility
		public int trace_level;			// max msg_level that will be displayed

		// For recoverable corrupt-data errors, we emit a warning message,
		// but keep going unless emit_message chooses to abort. emit_message
		// should count warnings in num_warnings. The surrounding application
		// can check for bad data by seeing if num_warnings is nonzero at the
		// end of processing.
		public int num_warnings;		// number of corrupt-data warnings

		// These fields point to the table(s) of error message strings.
		// An application can change the table pointer to switch to a different
		// message list (typically, to change the language in which errors are
		// reported). Some applications may wish to add additional error codes
		// that will be handled by the JPEG library error mechanism; the second
		// table pointer is used for this purpose.

		// First table includes all errors generated by JPEG library itself.
		// Error code 0 is reserved for a "no such error string" message.
		public string[] jpeg_message_table;	// Library errors
		public int last_jpeg_message;		// Table contains strings 0..last_jpeg_message

		// Second table can be added by application (see cjpeg/djpeg for example).
		// It contains strings numbered first_addon_message..last_addon_message.
		public string[] addon_message_table;// Non-library errors
		public int first_addon_message;		// code for first string in addon table
		public int last_addon_message;		// code for last string in addon table
	}

	// Progress monitor object
	public class jpeg_progress_mgr
	{
		public libjpeg.void_jpeg_common_Handler progress_monitor;

		public int pass_counter;		// work units completed in this pass
		public int pass_limit;			// total number of work units in this pass
		public int completed_passes;	// passes completed so far
		public int total_passes;		// total number of passes expected
	}

	// Data destination object for compression
	public class jpeg_destination_mgr
	{
		public byte[] output_bytes;
		public int next_output_byte;	// => next byte to write in buffer
		public uint free_in_buffer;		// # of byte spaces remaining in buffer

		public libjpeg.void_jpeg_compress_Handler init_destination;
		public libjpeg.bool_jpeg_compress_Handler empty_output_buffer;
		public libjpeg.void_jpeg_compress_Handler term_destination;
	}

	// Data source object for decompression
	public class jpeg_source_mgr
	{
		public byte[] input_bytes;
		public int next_input_byte;		// => next byte to read from buffer
		public uint bytes_in_buffer;	// # of bytes remaining in buffer

		public libjpeg.void_jpeg_decompress_Handler init_source;
		public libjpeg.bool_jpeg_decompress_Handler fill_input_buffer;
		public libjpeg.void_jpeg_decompress_int_Handler skip_input_data;
		public libjpeg.bool_jpeg_decompress_int_Handler resync_to_restart;
		public libjpeg.void_jpeg_decompress_bool_byteAOut_Handler term_source;
	}

	public static partial class libjpeg
	{
		// These marker codes are exported since applications and data source modules
		// are likely to want to use them.
		public const int JPEG_RST0=0xD0;	// RST0 marker code
		public const int JPEG_EOI=0xD9;		// EOI marker code
		public const int JPEG_APP0=0xE0;	// APP0 marker code
		public const int JPEG_COM=0xFE;		// COM marker code
	}
}