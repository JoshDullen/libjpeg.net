// jcmarker.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1997-1998, Guido Vollbeding <guivol@esc.de>.
// Copyright (C) 1991-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains routines to write JPEG datastream markers.

namespace Free.Ports.LibJpeg
{
	enum JPEG_MARKER
	{	// JPEG marker codes
		M_SOF0=0xc0,
		M_SOF1=0xc1,
		M_SOF2=0xc2,
		M_SOF3=0xc3,

		M_SOF5=0xc5,
		M_SOF6=0xc6,
		M_SOF7=0xc7,

		M_JPG=0xc8,
		M_SOF9=0xc9,
		M_SOF10=0xca,
		M_SOF11=0xcb,

		M_SOF13=0xcd,
		M_SOF14=0xce,
		M_SOF15=0xcf,

		M_DHT=0xc4,

		M_DAC=0xcc,

		M_RST0=0xd0,
		M_RST1=0xd1,
		M_RST2=0xd2,
		M_RST3=0xd3,
		M_RST4=0xd4,
		M_RST5=0xd5,
		M_RST6=0xd6,
		M_RST7=0xd7,

		M_SOI=0xd8,
		M_EOI=0xd9,
		M_SOS=0xda,
		M_DQT=0xdb,
		M_DNL=0xdc,
		M_DRI=0xdd,
		M_DHP=0xde,
		M_EXP=0xdf,

		M_APP0=0xe0,
		M_APP1=0xe1,
		M_APP2=0xe2,
		M_APP3=0xe3,
		M_APP4=0xe4,
		M_APP5=0xe5,
		M_APP6=0xe6,
		M_APP7=0xe7,
		M_APP8=0xe8,
		M_APP9=0xe9,
		M_APP10=0xea,
		M_APP11=0xeb,
		M_APP12=0xec,
		M_APP13=0xed,
		M_APP14=0xee,
		M_APP15=0xef,

		M_JPG0=0xf0,
		M_JPG13=0xfd,
		M_COM=0xfe,

		M_TEM=0x01,

		M_ERROR=0x100
	}

	public static partial class libjpeg
	{
		// Private state
		class my_marker_writer : jpeg_marker_writer
		{
			public uint last_restart_interval; // last DRI value emitted; 0 after SOI
		}

		// Basic output routines.
		//
		// Note that we do not support suspension while writing a marker.
		// Therefore, an application using suspension must ensure that there is
		// enough buffer space for the initial markers (typ. 600-700 bytes) before
		// calling jpeg_start_compress, and enough space to write the trailing EOI
		// (a few bytes) before calling jpeg_finish_compress. Multipass compression
		// modes are not supported at all with suspension, so those two are the only
		// points where markers will be written.

		// Emit a byte
		static void emit_byte(jpeg_compress cinfo, int val)
		{
			jpeg_destination_mgr dest=cinfo.dest;

			dest.output_bytes[dest.next_output_byte++]=(byte)val;
			if(--dest.free_in_buffer==0)
			{
				if(!dest.empty_output_buffer(cinfo)) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_CANT_SUSPEND);
			}
		}

		// Emit a marker code
		static void emit_marker(jpeg_compress cinfo, JPEG_MARKER mark)
		{
			emit_byte(cinfo, 0xFF);
			emit_byte(cinfo, (int)mark);
		}

		// Emit a 2-byte integer; these are always MSB first in JPEG files
		static void emit_2bytes(jpeg_compress cinfo, int value)
		{
			emit_byte(cinfo, (value>>8)&0xFF);
			emit_byte(cinfo, value&0xFF);
		}

		// Routines to write specific marker types.

		// Emit a DQT marker
		// Returns the precision used (0 = 8bits, 1 = 16bits) for baseline checking
		static int emit_dqt(jpeg_compress cinfo, int index)
		{
			JQUANT_TBL qtbl=cinfo.quant_tbl_ptrs[index];
			if(qtbl==null) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_NO_QUANT_TABLE, index);

			int prec=0;
			for(int i=0; i<DCTSIZE2; i++)
			{
				if(qtbl.quantval[i]>255) prec=1;
			}

			if(!qtbl.sent_table)
			{
				emit_marker(cinfo, JPEG_MARKER.M_DQT);

				emit_2bytes(cinfo, prec!=0?DCTSIZE2*2+1+2:DCTSIZE2+1+2);

				emit_byte(cinfo, index+(prec<<4));

				for(int i=0; i<DCTSIZE2; i++)
				{
					// The table entries must be emitted in zigzag order.
					uint qval=qtbl.quantval[jpeg_natural_order[i]];
					if(prec!=0) emit_byte(cinfo, (int)(qval>>8));
					emit_byte(cinfo, (int)(qval&0xFF));
				}

				qtbl.sent_table=true;
			}

			return prec;
		}

		// Emit a DHT marker
		static void emit_dht(jpeg_compress cinfo, int index, bool is_ac)
		{
			JHUFF_TBL htbl=null;

			if(is_ac)
			{
				htbl=cinfo.ac_huff_tbl_ptrs[index];
				index+=0x10; // output index has AC bit set
			}
			else htbl=cinfo.dc_huff_tbl_ptrs[index];

			if(htbl==null) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_NO_HUFF_TABLE, index);

			if(!htbl.sent_table)
			{
				emit_marker(cinfo, JPEG_MARKER.M_DHT);

				int length=0;
				for(int i=1; i<=16; i++) length+=htbl.bits[i];

				emit_2bytes(cinfo, length+2+1+16);
				emit_byte(cinfo, index);

				for(int i=1; i<=16; i++) emit_byte(cinfo, htbl.bits[i]);
				for(int i=0; i<length; i++) emit_byte(cinfo, htbl.huffval[i]);

				htbl.sent_table=true;
			}
		}

		// Emit a DAC marker
		// Since the useful info is so small, we want to emit all the tables in
		// one DAC marker. Therefore this routine does its own scan of the table.
		static void emit_dac(jpeg_compress cinfo)
		{
#if C_ARITH_CODING_SUPPORTED
			byte[] dc_in_use=new byte[NUM_ARITH_TBLS];
			byte[] ac_in_use=new byte[NUM_ARITH_TBLS];

			for(int i=0; i<NUM_ARITH_TBLS; i++) dc_in_use[i]=ac_in_use[i]=0;

			for(int i=0; i<cinfo.comps_in_scan; i++)
			{
				jpeg_component_info compptr=cinfo.cur_comp_info[i];
				// DC needs no table for refinement scan
				if(cinfo.Ss==0&&cinfo.Ah==0) dc_in_use[compptr.dc_tbl_no]=1;
				// AC needs no table when not present
				if(cinfo.Se!=0) ac_in_use[compptr.ac_tbl_no]=1;
			}

			int length=0;
			for(int i=0; i<NUM_ARITH_TBLS; i++) length+=dc_in_use[i]+ac_in_use[i];

			emit_marker(cinfo, JPEG_MARKER.M_DAC);
			emit_2bytes(cinfo, length*2+2);

			for(int i=0; i<NUM_ARITH_TBLS; i++)
			{
				if(dc_in_use[i]!=0)
				{
					emit_byte(cinfo, i);
					emit_byte(cinfo, cinfo.arith_dc_L[i]+(cinfo.arith_dc_U[i]<<4));
				}
				if(ac_in_use[i]!=0)
				{
					emit_byte(cinfo, i+0x10);
					emit_byte(cinfo, cinfo.arith_ac_K[i]);
				}
			}
#endif // C_ARITH_CODING_SUPPORTED
		}

		// Emit a DRI marker
		static void emit_dri(jpeg_compress cinfo)
		{
			emit_marker(cinfo, JPEG_MARKER.M_DRI);

			emit_2bytes(cinfo, 4);	// fixed length

			emit_2bytes(cinfo, (int)cinfo.restart_interval);
		}

		// Emit a SOF marker
		static void emit_sof(jpeg_compress cinfo, JPEG_MARKER code)
		{
			emit_marker(cinfo, code);

			emit_2bytes(cinfo, 3*cinfo.num_components+2+5+1); // length

			// Make sure image isn't bigger than SOF field can handle
			if(cinfo.image_height>65535||cinfo.image_width>65535) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_IMAGE_TOO_BIG, 65535);

			emit_byte(cinfo, cinfo.data_precision);
			emit_2bytes(cinfo, (int)cinfo.image_height);
			emit_2bytes(cinfo, (int)cinfo.image_width);

			emit_byte(cinfo, cinfo.num_components);

			for(int ci=0; ci<cinfo.num_components; ci++)
			{
				emit_byte(cinfo, cinfo.comp_info[ci].component_id);
				emit_byte(cinfo, (cinfo.comp_info[ci].h_samp_factor<<4)+cinfo.comp_info[ci].v_samp_factor);
				emit_byte(cinfo, cinfo.comp_info[ci].quant_tbl_no);
			}
		}

		// Emit a SOS marker
		static void emit_sos(jpeg_compress cinfo)
		{
			emit_marker(cinfo, JPEG_MARKER.M_SOS);

			emit_2bytes(cinfo, 2*cinfo.comps_in_scan+2+1+3); // length

			emit_byte(cinfo, cinfo.comps_in_scan);

			for(int i=0; i<cinfo.comps_in_scan; i++)
			{
				jpeg_component_info compptr=cinfo.cur_comp_info[i];
				emit_byte(cinfo, compptr.component_id);
				int td=compptr.dc_tbl_no;
				int ta=compptr.ac_tbl_no;
				if(cinfo.process==J_CODEC_PROCESS.JPROC_PROGRESSIVE)
				{
					// Progressive mode: only DC or only AC tables are used in one scan;
					// furthermore, Huffman coding of DC refinement uses no table at all.
					// We emit 0 for unused field(s); this is recommended by the P&M text
					// but does not seem to be specified in the standard.
					if(cinfo.Ss==0)
					{
						ta=0; // DC scan
						if(cinfo.Ah!=0&&!cinfo.arith_code) td=0; // no DC table either
					}
					else td=0; // AC scan
				}
				emit_byte(cinfo, (td<<4)+ta);
			}

			emit_byte(cinfo, cinfo.Ss);
			emit_byte(cinfo, cinfo.Se);
			emit_byte(cinfo, (cinfo.Ah<<4)+cinfo.Al);
		}

		// Emit a JFIF-compliant APP0 marker
		static void emit_jfif_app0(jpeg_compress cinfo)
		{
			// Length of APP0 block	(2 bytes)
			// Block ID				(4 bytes - ASCII "JFIF")
			// Zero byte			(1 byte to terminate the ID string)
			// Version Major, Minor	(2 bytes - major first)
			// Units				(1 byte - 0x00 = none, 0x01 = inch, 0x02 = cm)
			// Xdpu					(2 bytes - dots per unit horizontal)
			// Ydpu					(2 bytes - dots per unit vertical)
			// Thumbnail X size		(1 byte)
			// Thumbnail Y size		(1 byte)
			emit_marker(cinfo, JPEG_MARKER.M_APP0);

			emit_2bytes(cinfo, 2+4+1+2+1+2+2+1+1); // length

			emit_byte(cinfo, 0x4A);	// Identifier: ASCII "JFIF"
			emit_byte(cinfo, 0x46);
			emit_byte(cinfo, 0x49);
			emit_byte(cinfo, 0x46);
			emit_byte(cinfo, 0);
			emit_byte(cinfo, cinfo.JFIF_major_version); // Version fields
			emit_byte(cinfo, cinfo.JFIF_minor_version);
			emit_byte(cinfo, cinfo.density_unit);		// Pixel size information
			emit_2bytes(cinfo, (int)cinfo.X_density);
			emit_2bytes(cinfo, (int)cinfo.Y_density);
			emit_byte(cinfo, 0);						// No thumbnail image
			emit_byte(cinfo, 0);
		}

		// Emit a EXIF-compliant APP1 marker
		static void emit_exif_app1(jpeg_compress cinfo)
		{
			byte[] data=cinfo.exif.Generate();
			if(data==null) return;
			if(data.Length<8) return; // at least the TIFF header must be written completely

			// Length of APP1 block	(2 bytes)
			// Block ID				(4 bytes - ASCII "Exif")
			// Zero byte			(1 byte to terminate the ID string)
			// Zero byte			(1 byte padding)
			// Data					(n bytes)
			emit_marker(cinfo, JPEG_MARKER.M_APP1);

			emit_2bytes(cinfo, 2+4+1+1+data.Length); // length

			emit_byte(cinfo, 0x45);	// Identifier: ASCII "Exif"
			emit_byte(cinfo, 0x78);
			emit_byte(cinfo, 0x69);
			emit_byte(cinfo, 0x66);
			emit_byte(cinfo, 0);	// NULL
			emit_byte(cinfo, 0);	// Padding

			foreach(byte b in data) emit_byte(cinfo, b);
		}

		// Emit an Adobe APP14 marker
		static void emit_adobe_app14(jpeg_compress cinfo)
		{
			// Length of APP14 block	(2 bytes)
			// Block ID					(5 bytes - ASCII "Adobe")
			// Version Number			(2 bytes - currently 100)
			// Flags0					(2 bytes - currently 0)
			// Flags1					(2 bytes - currently 0)
			// Color transform			(1 byte)
			//
			// Although Adobe TN 5116 mentions Version = 101, all the Adobe files
			// now in circulation seem to use Version = 100, so that's what we write.
			//
			// We write the color transform byte as 1 if the JPEG color space is
			// YCbCr, 2 if it's YCCK, 0 otherwise. Adobe's definition has to do with
			// whether the encoder performed a transformation, which is pretty useless.
			emit_marker(cinfo, JPEG_MARKER.M_APP14);

			emit_2bytes(cinfo, 2+5+2+2+2+1); // length

			emit_byte(cinfo, 0x41);	// Identifier: ASCII "Adobe"
			emit_byte(cinfo, 0x64);
			emit_byte(cinfo, 0x6F);
			emit_byte(cinfo, 0x62);
			emit_byte(cinfo, 0x65);
			emit_2bytes(cinfo, 100);	// Version
			emit_2bytes(cinfo, 0);		// Flags0
			emit_2bytes(cinfo, 0);		// Flags1
			switch(cinfo.jpeg_color_space)
			{
				case J_COLOR_SPACE.JCS_YCbCr: emit_byte(cinfo, 1);	// Color transform = 1
					break;
				case J_COLOR_SPACE.JCS_YCCK: emit_byte(cinfo, 2);	// Color transform = 2
					break;
				default: emit_byte(cinfo, 0);						// Color transform = 0
					break;
			}
		}

		// These routines allow writing an arbitrary marker with parameters.
		// The only intended use is to emit COM or APPn markers after calling
		// write_file_header and before calling write_frame_header.
		// Other uses are not guaranteed to produce desirable results.
		// Counting the parameter bytes properly is the caller's responsibility.

		// Emit an arbitrary marker header
		static void write_marker_header(jpeg_compress cinfo, int marker, uint datalen)
		{
			if(datalen>65533) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_LENGTH); // safety check

			emit_marker(cinfo, (JPEG_MARKER)marker);

			emit_2bytes(cinfo, (int)(datalen+2));	// total length
		}

		// Emit one byte of marker parameters following write_marker_header
		static void write_marker_byte(jpeg_compress cinfo, int val)
		{
			emit_byte(cinfo, val);
		}

		// Write datastream header.
		// This consists of an SOI and optional APPn markers.
		// We recommend use of the JFIF marker, but not the Adobe marker,
		// when using YCbCr or grayscale data. The JFIF marker should NOT
		// be used for any other JPEG colorspace. The Adobe marker is helpful
		// to distinguish RGB, CMYK, and YCCK colorspaces.
		// Note that an application can write additional header markers after
		// jpeg_start_compress returns.
		static void write_file_header(jpeg_compress cinfo)
		{
			my_marker_writer marker=(my_marker_writer)cinfo.marker;

			emit_marker(cinfo, JPEG_MARKER.M_SOI);	// first the SOI

			// SOI is defined to reset restart interval to 0
			marker.last_restart_interval=0;

			if(cinfo.write_JFIF_header) emit_jfif_app0(cinfo);				// next an optional JFIF APP0
			if(cinfo.exif!=null&&cinfo.exif.HasData) emit_exif_app1(cinfo);	// next an optional EXIF APP1
			if(cinfo.write_Adobe_marker) emit_adobe_app14(cinfo);			// next an optional Adobe APP14
		}
		
		// Write frame header.
		// This consists of DQT and SOFn markers.
		// Note that we do not emit the SOF until we have emitted the DQT(s).
		// This avoids compatibility problems with incorrect implementations that
		// try to error-check the quant table numbers as soon as they see the SOF.
		static void write_frame_header(jpeg_compress cinfo)
		{
			int prec=0;
			bool is_baseline;

			if(cinfo.process!=J_CODEC_PROCESS.JPROC_LOSSLESS)
			{
				// Emit DQT for each quantization table.
				// Note that emit_dqt() suppresses any duplicate tables.
				prec=0;
				for(int ci=0; ci<cinfo.num_components; ci++)
				{
					prec+=emit_dqt(cinfo, cinfo.comp_info[ci].quant_tbl_no);
				}
				// now prec is nonzero iff there are any 16-bit quant tables.
			}

			// Check for a non-baseline specification.
			// Note we assume that Huffman table numbers won't be changed later.
			if(cinfo.arith_code||cinfo.process!=J_CODEC_PROCESS.JPROC_SEQUENTIAL||cinfo.data_precision!=8)
			{
				is_baseline=false;
			}
			else
			{
				is_baseline=true;
				for(int ci=0; ci<cinfo.num_components; ci++)
				{
					if(cinfo.comp_info[ci].dc_tbl_no>1||cinfo.comp_info[ci].ac_tbl_no>1)
					{
						is_baseline=false;
						break;
					}
				}
				if(prec!=0&&is_baseline)
				{
					is_baseline=false;
					// If it's baseline except for quantizer size, warn the user
					TRACEMS(cinfo, 0, J_MESSAGE_CODE.JTRC_16BIT_TABLES);
				}
			}

			// Emit the proper SOF marker
			if(cinfo.arith_code)
			{
				if(cinfo.process==J_CODEC_PROCESS.JPROC_PROGRESSIVE)
					emit_sof(cinfo, JPEG_MARKER.M_SOF10);	// SOF code for progressive arithmetic
				else if(cinfo.process==J_CODEC_PROCESS.JPROC_SEQUENTIAL)
					emit_sof(cinfo, JPEG_MARKER.M_SOF9);	// SOF code for sequential arithmetic
				else ERREXIT(cinfo, J_MESSAGE_CODE.JERR_NOTIMPL);
			}
			else
			{
				if(cinfo.process==J_CODEC_PROCESS.JPROC_PROGRESSIVE)
					emit_sof(cinfo, JPEG_MARKER.M_SOF2);	// SOF code for progressive Huffman
				else if(cinfo.process==J_CODEC_PROCESS.JPROC_LOSSLESS)
					emit_sof(cinfo, JPEG_MARKER.M_SOF3);	// SOF code for lossless Huffman
				else if(is_baseline)
					emit_sof(cinfo, JPEG_MARKER.M_SOF0);	// SOF code for baseline implementation
				else
					emit_sof(cinfo, JPEG_MARKER.M_SOF1);	// SOF code for non-baseline Huffman file
			}
		}

		// Write scan header.
		// This consists of DHT or DAC markers, optional DRI, and SOS.
		// Compressed data will be written following the SOS.
		static void write_scan_header(jpeg_compress cinfo)
		{
			if(cinfo.arith_code)
			{
				// Emit arith conditioning info. We may have some duplication
				// if the file has multiple scans, but it's so small it's hardly
				// worth worrying about.
				emit_dac(cinfo);
			}
			else
			{
				// Emit Huffman tables.
				// Note that emit_dht() suppresses any duplicate tables.
				for(int i=0; i<cinfo.comps_in_scan; i++)
				{
					jpeg_component_info compptr=cinfo.cur_comp_info[i];
					if(cinfo.process==J_CODEC_PROCESS.JPROC_PROGRESSIVE)
					{
						// Progressive mode: only DC or only AC tables are used in one scan
						if(cinfo.Ss==0)
						{
							if(cinfo.Ah==0) emit_dht(cinfo, compptr.dc_tbl_no, false); // DC needs no table for refinement scan
						}
						else
						{
							emit_dht(cinfo, compptr.ac_tbl_no, true);
						}
					}
					else if(cinfo.process==J_CODEC_PROCESS.JPROC_LOSSLESS)
					{
						// Lossless mode: only DC tables are used
						emit_dht(cinfo, compptr.dc_tbl_no, false);
					}
					else
					{
						// Sequential mode: need both DC and AC tables
						emit_dht(cinfo, compptr.dc_tbl_no, false);
						emit_dht(cinfo, compptr.ac_tbl_no, true);
					}
				}
			}

			my_marker_writer marker=(my_marker_writer)cinfo.marker;

			// Emit DRI if required --- note that DRI value could change for each scan.
			// We avoid wasting space with unnecessary DRIs, however.
			if(cinfo.restart_interval!=marker.last_restart_interval)
			{
				emit_dri(cinfo);
				marker.last_restart_interval=cinfo.restart_interval;
			}

			emit_sos(cinfo);
		}

		// Write datastream trailer.
		static void write_file_trailer(jpeg_compress cinfo)
		{
			emit_marker(cinfo, JPEG_MARKER.M_EOI);
		}
		
		// Write an abbreviated table-specification datastream.
		// This consists of SOI, DQT and DHT tables, and EOI.
		// Any table that is defined and not marked sent_table = true will be
		// emitted. Note that all tables will be marked sent_table = true at exit.
		static void write_tables_only(jpeg_compress cinfo)
		{
			emit_marker(cinfo, JPEG_MARKER.M_SOI);

			for(int i=0; i<NUM_QUANT_TBLS; i++)
			{
				if(cinfo.quant_tbl_ptrs[i]!=null) emit_dqt(cinfo, i);
			}

			if(!cinfo.arith_code)
			{
				for(int i=0; i<NUM_HUFF_TBLS; i++)
				{
					if(cinfo.dc_huff_tbl_ptrs[i]!=null) emit_dht(cinfo, i, false);
					if(cinfo.ac_huff_tbl_ptrs[i]!=null) emit_dht(cinfo, i, true);
				}
			}

			emit_marker(cinfo, JPEG_MARKER.M_EOI);
		}

		// Initialize the marker writer module.
		static void jinit_marker_writer(jpeg_compress cinfo)
		{
			my_marker_writer marker=null;

			try
			{	// Create the subobject
				marker=new my_marker_writer();
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}
			cinfo.marker=marker;

			// Initialize method pointers
			marker.write_file_header=write_file_header;
			marker.write_frame_header=write_frame_header;
			marker.write_scan_header=write_scan_header;
			marker.write_file_trailer=write_file_trailer;
			marker.write_tables_only=write_tables_only;
			marker.write_marker_header=write_marker_header;
			marker.write_marker_byte=write_marker_byte;

			// Initialize private state
			marker.last_restart_interval=0;
		}
	}
}