// jerror.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1997-1998, Guido Vollbeding <guivol@esc.de>.
// Copyright (C) 1994-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file defines the error and message codes for the JPEG library.
// Edit this file to add new codes, or to translate the message strings to
// some other language.
// A set of error-reporting macros are defined too. Some applications using
// the JPEG library may wish to include this file to get the error codes
// and/or the macros.
//
// This file contains simple error-reporting and trace-message routines.
// These routines are used by both the compression and decompression code.

using System;
using System.Diagnostics;

namespace Free.Ports.LibJpeg
{
	#region public enum J_MESSAGE_CODE {...}
	public enum J_MESSAGE_CODE : int
	{
		JMSG_NOMESSAGE=0,	// Must be first entry!

		// For maintenance convenience, list is alphabetical by message code name
		JERR_ARITH_NOTIMPL,
		JERR_BAD_BUFFER_MODE,
		JERR_BAD_COMPONENT_ID,
		JERR_BAD_DCT_COEF,
		JERR_BAD_DCTSIZE,
		JERR_BAD_DIFF,
		JERR_BAD_HUFF_TABLE,
		JERR_BAD_IN_COLORSPACE,
		JERR_BAD_J_COLORSPACE,
		JERR_BAD_LENGTH,
		JERR_BAD_LIB_VERSION,
		JERR_BAD_LOSSLESS,
		JERR_BAD_LOSSLESS_SCRIPT,
		JERR_BAD_MCU_SIZE,
		JERR_BAD_POOL_ID,
		JERR_BAD_PRECISION,
		JERR_BAD_PROGRESSION,
		JERR_BAD_PROG_SCRIPT,
		JERR_BAD_RESTART,
		JERR_BAD_SAMPLING,
		JERR_BAD_SCAN_SCRIPT,
		JERR_BAD_STATE,
		JERR_BAD_STRUCT_SIZE,
		JERR_BAD_VIRTUAL_ACCESS,
		JERR_BUFFER_SIZE,
		JERR_CANT_SUSPEND,
		JERR_CANT_TRANSCODE,
		JERR_CCIR601_NOTIMPL,
		JERR_COMPONENT_COUNT,
		JERR_CONVERSION_NOTIMPL,
		JERR_DAC_INDEX,
		JERR_DAC_VALUE,
		JERR_DHT_INDEX,
		JERR_DQT_INDEX,
		JERR_EMPTY_IMAGE,
		JERR_EMS_READ,
		JERR_EMS_WRITE,
		JERR_EOI_EXPECTED,
		JERR_FILE_READ,
		JERR_FILE_WRITE,
		JERR_FRACT_SAMPLE_NOTIMPL,
		JERR_HUFF_CLEN_OVERFLOW,
		JERR_HUFF_MISSING_CODE,
		JERR_IMAGE_TOO_BIG,
		JERR_INPUT_EMPTY,
		JERR_INPUT_EOF,
		JERR_MISMATCHED_QUANT_TABLE,
		JERR_MISSING_DATA,
		JERR_MODE_CHANGE,
		JERR_NOTIMPL,
		JERR_NOT_COMPILED,
		JERR_NO_ARITH_TABLE,
		JERR_NO_BACKING_STORE,
		JERR_NO_HUFF_TABLE,
		JERR_NO_IMAGE,
		JERR_NO_LOSSLESS_SCRIPT,
		JERR_NO_QUANT_TABLE,
		JERR_NO_SOI,
		JERR_OUT_OF_MEMORY,
		JERR_QUANT_COMPONENTS,
		JERR_QUANT_FEW_COLORS,
		JERR_QUANT_MANY_COLORS,
		JERR_SOF_DUPLICATE,
		JERR_SOF_NO_SOS,
		JERR_SOF_UNSUPPORTED,
		JERR_SOI_DUPLICATE,
		JERR_SOS_NO_SOF,
		JERR_TFILE_CREATE,
		JERR_TFILE_READ,
		JERR_TFILE_SEEK,
		JERR_TFILE_WRITE,
		JERR_TOO_LITTLE_DATA,
		JERR_UNKNOWN_MARKER,
		JERR_VIRTUAL_BUG,
		JERR_WIDTH_OVERFLOW,
		JERR_XMS_READ,
		JERR_XMS_WRITE,
		JMSG_COPYRIGHT,
		JMSG_VERSION,
		JTRC_16BIT_TABLES,
		JTRC_ADOBE,
		JTRC_APP0,
		JTRC_APP14,
		JTRC_DAC,
		JTRC_DHT,
		JTRC_DQT,
		JTRC_DRI,
		JTRC_EMS_CLOSE,
		JTRC_EMS_OPEN,
		JTRC_EOI,
		JTRC_HUFFBITS,
		JTRC_JFIF,
		JTRC_JFIF_BADTHUMBNAILSIZE,
		JTRC_JFIF_EXTENSION,
		JTRC_JFIF_THUMBNAIL,
		JTRC_MISC_MARKER,
		JTRC_PARMLESS_MARKER,
		JTRC_QUANTVALS,
		JTRC_QUANT_3_NCOLORS,
		JTRC_QUANT_NCOLORS,
		JTRC_QUANT_SELECTED,
		JTRC_RECOVERY_ACTION,
		JTRC_RST,
		JTRC_SMOOTH_NOTIMPL,
		JTRC_SOF,
		JTRC_SOF_COMPONENT,
		JTRC_SOI,
		JTRC_SOS,
		JTRC_SOS_COMPONENT,
		JTRC_SOS_PARAMS,
		JTRC_TFILE_CLOSE,
		JTRC_TFILE_OPEN,
		JTRC_THUMB_JPEG,
		JTRC_THUMB_PALETTE,
		JTRC_THUMB_RGB,
		JTRC_UNKNOWN_LOSSLESS_IDS,
		JTRC_UNKNOWN_LOSSY_IDS,
		JTRC_XMS_CLOSE,
		JTRC_XMS_OPEN,
		JWRN_ADOBE_XFORM,
		JWRN_ARITH_BAD_CODE,
		JWRN_BOGUS_PROGRESSION,
		JWRN_EXTRANEOUS_DATA,
		JWRN_HIT_MARKER,
		JWRN_HUFF_BAD_CODE,
		JWRN_JFIF_MAJOR,
		JWRN_JPEG_EOF,
		JWRN_MUST_DOWNSCALE,
		JWRN_MUST_RESYNC,
		JWRN_NOT_SEQUENTIAL,
		JWRN_TOO_MUCH_DATA,
		JMSG_LASTMSGCODE
	}
	#endregion

	public static partial class libjpeg
	{
		// Create the message string table.
		// The message table is made an external symbol just in case any applications
		// want to refer to it directly.

		#region static readonly string[] jpeg_std_message_table=new string[] {...}
		static readonly string[] jpeg_std_message_table=new string[]
		{
			"Bogus message code {0}", // Must be first entry!

			// For maintenance convenience, list is alphabetical by message code name
			"Sorry, there are legal restrictions on arithmetic coding",
			"Bogus buffer control mode",
			"Invalid component ID {0} in SOS",
			"DCT coefficient out of range",
			"IDCT output block size {0} not supported",
			"spatial difference out of range",
			"Bogus Huffman table definition",
			"Bogus input colorspace",
			"Bogus JPEG colorspace",
			"Bogus marker length",
			"Wrong JPEG library version: library is {0}, caller expects {1}",
			"Invalid lossless parameters Ss={0} Se={1} Ah={2} Al={3}",
			"Invalid lossless parameters at scan script entry {0}",
			"Sampling factors too large for interleaved scan",
			"Invalid memory pool code {0}",
			"Unsupported JPEG data precision {0}",
			"Invalid progressive parameters Ss={0} Se={1} Ah={2} Al={3}",
			"Invalid progressive parameters at scan script entry {0}",
			"Invalid restart interval: {0}, must be an integer multiple of the number of MCUs in an MCU_row ({1})",
			"Bogus sampling factors",
			"Invalid scan script at entry {0}",
			"Improper call to JPEG library in state {0}",
			"JPEG parameter struct mismatch: library thinks size is {0}, caller expects {1}",
			"Bogus virtual array access",
			"Buffer passed to JPEG library is too small",
			"Suspension not allowed here",
			"Cannot transcode to/from lossless JPEG datastreams",
			"CCIR601 sampling not implemented yet",
			"Too many color components: {0}, max {1}",
			"Unsupported color conversion request",
			"Bogus DAC index {0}",
			"Bogus DAC value 0x{0:X}",
			"Bogus DHT index {0}",
			"Bogus DQT index {0}",
			"Empty JPEG image (DNL not supported)",
			"Read from EMS failed",
			"Write to EMS failed",
			"Didn't expect more than one scan",
			"Input file read error",
			"Output file write error --- out of disk space?",
			"Fractional sampling not implemented yet",
			"Huffman code size table overflow",
			"Missing Huffman code table entry",
			"Maximum supported image dimension is {0} pixels",
			"Empty input file",
			"Premature end of input file",
			"Cannot transcode due to multiple use of quantization table {0}",
			"Scan script does not transmit all data",
			"Invalid color quantization mode change",
			"Not implemented yet",
			"Requested feature was omitted at compile time",
			"Arithmetic table 0x{0:X2} was not defined",
			"Backing store not supported",
			"Huffman table 0x{0:X2} was not defined",
			"JPEG datastream contains no image",
			"Lossless encoding was requested but no scan script was supplied",
			"Quantization table 0x{0:X2} was not defined",
			"Not a JPEG file: starts with 0x{0:X2} 0x{1:X2}",
			"Insufficient memory (case {0})",
			"Cannot quantize more than {0} color components",
			"Cannot quantize to fewer than {0} colors",
			"Cannot quantize to more than {0} colors",
			"Invalid JPEG file structure: two SOF markers",
			"Invalid JPEG file structure: missing SOS marker",
			"Unsupported JPEG process: SOF type 0x{0:X2}",
			"Invalid JPEG file structure: two SOI markers",
			"Invalid JPEG file structure: SOS before SOF",
			"Failed to create temporary file {0}",
			"Read failed on temporary file",
			"Seek failed on temporary file",
			"Write failed on temporary file --- out of disk space?",
			"Application transferred too few scanlines",
			"Unsupported marker type 0x{0:X2}",
			"Virtual array controller messed up",
			"Image too wide for this implementation",
			"Read from XMS failed",
			"Write to XMS failed",
			JCOPYRIGHT,
			JVERSION,
			"Caution: quantization tables are too coarse for baseline JPEG",
			"Adobe APP14 marker: version {0}, flags 0x{1:X4} 0x{2:X4}, transform {3}",
			"Unknown APP0 marker (not JFIF), length {0}",
			"Unknown APP14 marker (not Adobe), length {0}",
			"Define Arithmetic Table 0x{0:X2}: 0x{1:X2}",
			"Define Huffman Table 0x{0:X2}",
			"Define Quantization Table {0} precision {1}",
			"Define Restart Interval {0}",
			"Freed EMS handle {0}",
			"Obtained EMS handle {0}",
			"End Of Image",
			"        {0,3} {1,3} {2,3} {3,3} {4,3} {5,3} {6,3} {7,3}",
			"JFIF APP0 marker: version {0}.{1:00}, density {2}x{3}  {4}",
			"Warning: thumbnail image size does not match data length {0}",
			"JFIF extension marker: type 0x{0:X2}, length {1}",
			"    with {0} x {1} thumbnail image",
			"Miscellaneous marker 0x{0:X2}, length {1}",
			"Unexpected marker 0x{0:X2}",
			"        {0,4} {1,4} {2,4} {3,4} {4,4} {5,4} {6,4} {7,4}",
			"Quantizing to {0} = {1}*{2}*{3} colors",
			"Quantizing to {0} colors",
			"Selected {0} colors for quantization",
			"At marker 0x{0:X2}, recovery action {1}",
			"RST{0}",
			"Smoothing not supported with nonstandard sampling ratios",
			"Start Of Frame 0x{0:X2}: width={1}, height={2}, components={3}",
			"    Component {0}: {1}hx{2}v q={3}",
			"Start of Image",
			"Start Of Scan: {0} components",
			"    Component {0}: dc={1} ac={2}",
			"  Ss={0}, Se={1}, Ah={2}, Al={3}",
			"Closed temporary file {0}",
			"Opened temporary file {0}",
			"JFIF extension marker: JPEG-compressed thumbnail image, length {0}",
			"JFIF extension marker: palette thumbnail image, length {0}",
			"JFIF extension marker: RGB thumbnail image, length {0}",
			"Unrecognized component IDs {0} {1} {2}, assuming RGB",
			"Unrecognized component IDs {0} {1} {2}, assuming YCbCr",
			"Freed XMS handle {0}",
			"Obtained XMS handle {0}",
			"Unknown Adobe color transform code {0}",
			"Corrupt JPEG data: bad arithmetic code",
			"Inconsistent progression sequence for component {0} coefficient {1}",
			"Corrupt JPEG data: {0} extraneous bytes before marker 0x{1:X2}",
			"Corrupt JPEG data: premature end of data segment",
			"Corrupt JPEG data: bad Huffman code",
			"Warning: unknown JFIF revision number {0}.{1:00}",
			"Premature end of JPEG file",
			"Must downscale data from {0} bits to {1}",
			"Corrupt JPEG data: found marker 0x{0:X2} instead of RST{1}",
			"Invalid SOS parameters for sequential JPEG",
			"Application transferred too many scanlines",
			null
		};
		#endregion

		// Error exit handler: must not 'return' to caller.
		//
		// Applications may override this if they want to get control back after
		// an error. Typically one would throw a more specific exception than Exception().
		// Note that the info needed to generate an error message is stored in the error
		// object, so you can generate the message now or later, at your convenience.
		// You should make sure that the JPEG object is cleaned up (with jpeg_abort
		// or jpeg_destroy) at some point.
		static void error_exit(jpeg_common cinfo)
		{
			// Always display the message
			cinfo.err.output_message(cinfo);

			// Let the memory manager delete any temp files before we die
			jpeg_destroy(cinfo);

			throw new Exception();
		}

		// Actual output of an error or trace message.
		// Applications may override this method to send JPEG messages somewhere
		// other than stderr.
		//
		// On Windows, printing to stderr is generally completely useless.
		// Most Windows applications will still prefer to override this routine,
		// but if they don't, it'll do something at least marginally useful.
		static void output_message(jpeg_common cinfo)
		{
			// Create the message
			string buffer=cinfo.err.format_message(cinfo);

			// Send it to stderr, adding a newline
			Console.Error.WriteLine(buffer);
		}

		// Decide whether to emit a trace or warning message.
		// msg_level is one of:
		//	-1: recoverable corrupt-data warning, may want to abort.
		//	 0: important advisory messages (always display to user).
		//	 1: first level of tracing detail.
		//	 2,3,...: successively more detailed tracing messages.
		// An application might override this method if it wanted to abort on warnings
		// or change the policy about which messages to display.
		static void emit_message(jpeg_common cinfo, int msg_level)
		{
			jpeg_error_mgr err=cinfo.err;

			if(msg_level<0)
			{
				// It's a warning message. Since corrupt files may generate many warnings,
				// the policy implemented here is to show only the first warning,
				// unless trace_level >= 3.
				if(err.num_warnings==0||err.trace_level>=3) err.output_message(cinfo);
				// Always count warnings in num_warnings.
				err.num_warnings++;
			}
			else
			{
				// It's a trace message. Show it if trace_level >= msg_level.
				if(err.trace_level>=msg_level)
				{
					err.output_message(cinfo);

					// Create the message and Send it to Debug Console, adding a newline
					//Debug.WriteLine(cinfo.err.format_message(cinfo));
				}
			}
		}

		// Format a message string for the most recent JPEG error or message.
		// The message is stored into buffer, which should be at least JMSG_LENGTH_MAX
		// characters. Note that no '\n' character is added to the string.
		// Few applications should need to override this method.
		static string format_message(jpeg_common cinfo)
		{
			jpeg_error_mgr err=cinfo.err;
			int msg_code=err.msg_code;
			string msgtext=null;

			// Look up message string in proper table
			if(msg_code>0&&msg_code<=err.last_jpeg_message) msgtext=err.jpeg_message_table[msg_code];
			else if(err.addon_message_table!=null&&msg_code>=err.first_addon_message&&msg_code<=err.last_addon_message)
				msgtext=err.addon_message_table[msg_code-err.first_addon_message];

			// Defend against bogus message number
			if(msgtext==null)
			{
				err.msg_parm[0]=msg_code;
				msgtext=err.jpeg_message_table[0];
			}

			// Format the message into the passed buffer
			return string.Format(msgtext, err.msg_parm);
		}

		// Reset error state variables at start of a new image.
		// This is called during compression startup to reset trace/error
		// processing to default state, without losing any application-specific
		// method pointers. An application might possibly want to override
		// this method if it has additional error processing state.
		static void reset_error_mgr(jpeg_common cinfo)
		{
			cinfo.err.num_warnings=0;
			// trace_level is not reset since it is an application-supplied parameter
			cinfo.err.msg_code=0;	// may be useful as a flag for "no error"
		}

		// Default error-management setup
		// Fill in the standard error-handling methods in a jpeg_error_mgr object.
		// Typical call is:
		//	jpeg_compress cinfo=new jpeg_compress();
		//	jpeg_error_mgr err=new jpeg_error_mgr();
		//
		//	cinfo.err=libjpeg.jpeg_std_error(err);
		// after which the application may override some of the methods.
		public static jpeg_error_mgr jpeg_std_error(jpeg_error_mgr err)
		{
			err.error_exit=error_exit;
			err.emit_message=emit_message;
			err.output_message=output_message;
			err.format_message=format_message;
			err.reset_error_mgr=reset_error_mgr;

			err.trace_level=0;	// default = no tracing
			err.num_warnings=0;	// no warnings emitted yet
			err.msg_code=0;		// may be useful as a flag for "no error"

			// Initialize message table pointers
			err.jpeg_message_table=jpeg_std_message_table;
			err.last_jpeg_message=(int)J_MESSAGE_CODE.JMSG_LASTMSGCODE-1;

			err.addon_message_table=null;
			err.first_addon_message=0;	// for safety
			err.last_addon_message=0;

			return err;
		}

		// Functions to simplify using the error and trace message stuff
		// Fatal errors (print message and exit)
		#region ERREXIT, ERREXIT1, .. ERREXITS
		public static void ERREXIT(jpeg_common cinfo, J_MESSAGE_CODE code)
		{
			cinfo.err.msg_code=(int)code;
			cinfo.err.error_exit(cinfo);
		}

		public static void ERREXIT1(jpeg_common cinfo, J_MESSAGE_CODE code, int p1)
		{
			cinfo.err.msg_parm[0]=p1;
			cinfo.err.msg_code=(int)code;
			cinfo.err.error_exit(cinfo);
		}

		public static void ERREXIT1(jpeg_common cinfo, J_MESSAGE_CODE code, STATE p1)
		{
			cinfo.err.msg_parm[0]=(int)p1;
			cinfo.err.msg_code=(int)code;
			cinfo.err.error_exit(cinfo);
		}

		public static void ERREXIT2(jpeg_common cinfo, J_MESSAGE_CODE code, int p1, int p2)
		{
			cinfo.err.msg_parm[0]=p1;
			cinfo.err.msg_parm[1]=p2;
			cinfo.err.msg_code=(int)code;
			cinfo.err.error_exit(cinfo);
		}

		public static void ERREXIT3(jpeg_common cinfo, J_MESSAGE_CODE code, int p1, int p2, int p3)
		{
			object[] _mp=cinfo.err.msg_parm;
			_mp[0]=p1;
			_mp[1]=p2;
			_mp[2]=p3;
			cinfo.err.msg_code=(int)code;
			cinfo.err.error_exit(cinfo);
		}

		public static void ERREXIT4(jpeg_common cinfo, J_MESSAGE_CODE code, int p1, int p2, int p3, int p4)
		{
			object[] _mp=cinfo.err.msg_parm;
			_mp[0]=p1;
			_mp[1]=p2;
			_mp[2]=p3;
			_mp[3]=p4;
			cinfo.err.msg_code=(int)code;
			cinfo.err.error_exit(cinfo);
		}

		public static void ERREXITS(jpeg_common cinfo, J_MESSAGE_CODE code, string str)
		{
			cinfo.err.msg_code=(int)code;
			cinfo.err.msg_parm[0]=str;
			cinfo.err.error_exit(cinfo);
		}
		#endregion

		// Nonfatal errors (we can keep going, but the data is probably corrupt)
		#region WARNMS, WARNMS1, WARNMS2
		public static void WARNMS(jpeg_common cinfo, J_MESSAGE_CODE code)
		{
			cinfo.err.msg_code=(int)code;
			cinfo.err.emit_message(cinfo, -1);
		}

		public static void WARNMS1(jpeg_common cinfo, J_MESSAGE_CODE code, int p1)
		{
			cinfo.err.msg_parm[0]=p1;
			cinfo.err.msg_code=(int)code;
			cinfo.err.emit_message(cinfo, -1);
		}

		public static void WARNMS2(jpeg_common cinfo, J_MESSAGE_CODE code, int p1, int p2)
		{
			cinfo.err.msg_parm[0]=p1;
			cinfo.err.msg_parm[1]=p2;
			cinfo.err.msg_code=(int)code;
			cinfo.err.emit_message(cinfo, -1);
		}
		#endregion

		// Informational/debugging messages
		#region TRACEMS, TRACEMS1, .. TRACEMSS
		static void TRACEMS(jpeg_common cinfo, int lvl, J_MESSAGE_CODE code)
		{
			cinfo.err.msg_code=(int)code;
			cinfo.err.emit_message(cinfo, lvl);
		}

		static void TRACEMS1(jpeg_common cinfo, int lvl, J_MESSAGE_CODE code, int p1)
		{
			cinfo.err.msg_parm[0]=p1;
			cinfo.err.msg_code=(int)code;
			cinfo.err.emit_message(cinfo, lvl);
		}

		static void TRACEMS2(jpeg_common cinfo, int lvl, J_MESSAGE_CODE code, int p1, int p2)
		{
			cinfo.err.msg_parm[0]=p1;
			cinfo.err.msg_parm[1]=p2;
			cinfo.err.msg_code=(int)code;
			cinfo.err.emit_message(cinfo, lvl);
		}

		static void TRACEMS3(jpeg_common cinfo, int lvl, J_MESSAGE_CODE code, int p1, int p2, int p3)
		{
			object[] _mp=cinfo.err.msg_parm;
			_mp[0]=p1;
			_mp[1]=p2;
			_mp[2]=p3;
			cinfo.err.msg_code=(int)code;
			cinfo.err.emit_message(cinfo, lvl);
		}

		static void TRACEMS4(jpeg_common cinfo, int lvl, J_MESSAGE_CODE code, int p1, int p2, int p3, int p4)
		{
			object[] _mp=cinfo.err.msg_parm;
			_mp[0]=p1;
			_mp[1]=p2;
			_mp[2]=p3;
			_mp[3]=p4;
			cinfo.err.msg_code=(int)code;
			cinfo.err.emit_message(cinfo, lvl);
		}

		static void TRACEMS5(jpeg_common cinfo, int lvl, J_MESSAGE_CODE code, int p1, int p2, int p3, int p4, int p5)
		{
			object[] _mp=cinfo.err.msg_parm;
			_mp[0]=p1;
			_mp[1]=p2;
			_mp[2]=p3;
			_mp[3]=p4;
			_mp[4]=p5;
			cinfo.err.msg_code=(int)code;
			cinfo.err.emit_message(cinfo, lvl);
		}

		static void TRACEMS8(jpeg_common cinfo, int lvl, J_MESSAGE_CODE code, int p1, int p2, int p3, int p4, int p5, int p6, int p7, int p8)
		{
			object[] _mp=cinfo.err.msg_parm;
			_mp[0]=p1;
			_mp[1]=p2;
			_mp[2]=p3;
			_mp[3]=p4;
			_mp[4]=p5;
			_mp[5]=p6;
			_mp[6]=p7;
			_mp[7]=p8;
			cinfo.err.msg_code=(int)code;
			cinfo.err.emit_message(cinfo, lvl);
		}

		static void TRACEMSS(jpeg_common cinfo, int lvl, J_MESSAGE_CODE code, string str)
		{
			cinfo.err.msg_code=(int)code;
			cinfo.err.msg_parm[0]=str;
			cinfo.err.emit_message(cinfo, lvl);
		}
		#endregion
	}
}
