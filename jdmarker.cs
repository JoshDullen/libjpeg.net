// jdmarker.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1991-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains routines to decode JPEG datastream markers.
// Most of the complexity arises from our desire to support input
// suspension: if not all of the data for a marker is available,
// we must exit back to the application. On resumption, we reprocess
// the marker.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Private state
		class my_marker_reader : jpeg_marker_reader
		{
			// Application-overridable marker processing methods
			public jpeg_marker_parser_method process_COM;
			public jpeg_marker_parser_method[] process_APPn=new jpeg_marker_parser_method[16];

			// Limit on marker data length to save for each marker type
			public uint length_limit_COM;
			public uint[] length_limit_APPn=new uint[16];

			// Status of COM/APPn marker saving
			public jpeg_marker_struct cur_marker;	// null if not processing a marker
			public uint bytes_read;					// data bytes read so far in marker
			// Note: cur_marker is not linked into marker_list until it's all read.
		}

		// Routines to process JPEG markers.
		//
		// Entry condition: JPEG marker itself has been read and its code saved
		//	in cinfo.unread_marker; input restart point is just after the marker.
		//
		// Exit: if return true, have read and processed any parameters, and have
		//	updated the restart point to point after the parameters.
		//	If return false, was forced to suspend before reaching end of
		//	marker parameters; restart point has not been moved. Same routine
		//	will be called again after application supplies more input data.
		//
		// This approach to suspension assumes that all of a marker's parameters
		// can fit into a single input bufferload. This should hold for "normal"
		// markers. Some COM/APPn markers might have large parameter segments
		// that might not fit. If we are simply dropping such a marker, we use
		// skip_input_data to get past it, and thereby put the problem on the
		// source manager's shoulders. If we are saving the marker's contents
		// into memory, we use a slightly different convention: when forced to
		// suspend, the marker processor updates the restart point to the end of
		// what it's consumed (ie, the end of the buffer) before returning false.
		// On resumption, cinfo.unread_marker still contains the marker code,
		// but the data source will point to the next chunk of marker data.
		// The marker processor must retain internal state to deal with this.
		//
		// Note that we don't bother to avoid duplicate trace messages if a
		// suspension occurs within marker parameters. Other side effects
		// require more care.

		// Process an SOI marker
		static bool get_soi(jpeg_decompress cinfo)
		{
			TRACEMS(cinfo, 1, J_MESSAGE_CODE.JTRC_SOI);

			if(cinfo.marker.saw_SOI) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_SOI_DUPLICATE);

			// Reset all parameters that are defined to be reset by SOI
			for(int i=0; i<NUM_ARITH_TBLS; i++)
			{
				cinfo.arith_dc_L[i]=0;
				cinfo.arith_dc_U[i]=1;
				cinfo.arith_ac_K[i]=5;
			}
			cinfo.restart_interval=0;

			// Set initial assumptions for colorspace etc
			cinfo.jpeg_color_space=J_COLOR_SPACE.JCS_UNKNOWN;
			cinfo.CCIR601_sampling=false;	// Assume non-CCIR sampling???

			cinfo.saw_JFIF_marker=false;
			cinfo.JFIF_major_version=1;		// set default JFIF APP0 values
			cinfo.JFIF_minor_version=1;
			cinfo.density_unit=0;
			cinfo.X_density=1;
			cinfo.Y_density=1;
			cinfo.saw_Adobe_marker=false;
			cinfo.Adobe_transform=0;

			cinfo.marker.saw_SOI=true;

			return true;
		}

		// Process a SOFn marker
		static bool get_sof(jpeg_decompress cinfo, J_CODEC_PROCESS process, bool is_arith, int data_unit)
		{
			jpeg_source_mgr datasrc=cinfo.src;
			byte[] input_bytes=datasrc.input_bytes;
			int next_input_byte=datasrc.next_input_byte;
			uint bytes_in_buffer=datasrc.bytes_in_buffer;

			cinfo.DCT_size=data_unit;
			cinfo.process=process;
			cinfo.arith_code=is_arith;

			if(bytes_in_buffer==0)
			{
				if(!datasrc.fill_input_buffer(cinfo)) return false;
				input_bytes=datasrc.input_bytes;
				next_input_byte=datasrc.next_input_byte;
				bytes_in_buffer=datasrc.bytes_in_buffer;
			}
			bytes_in_buffer--;
			int length=((int)input_bytes[next_input_byte++])<<8;
			if(bytes_in_buffer==0)
			{
				if(!datasrc.fill_input_buffer(cinfo)) return false;
				input_bytes=datasrc.input_bytes;
				next_input_byte=datasrc.next_input_byte;
				bytes_in_buffer=datasrc.bytes_in_buffer;
			}
			bytes_in_buffer--;
			length+=input_bytes[next_input_byte++];

			if(bytes_in_buffer==0)
			{
				if(!datasrc.fill_input_buffer(cinfo)) return false;
				input_bytes=datasrc.input_bytes;
				next_input_byte=datasrc.next_input_byte;
				bytes_in_buffer=datasrc.bytes_in_buffer;
			}
			bytes_in_buffer--;
			cinfo.data_precision=input_bytes[next_input_byte++];

			if(bytes_in_buffer==0)
			{
				if(!datasrc.fill_input_buffer(cinfo)) return false;
				input_bytes=datasrc.input_bytes;
				next_input_byte=datasrc.next_input_byte;
				bytes_in_buffer=datasrc.bytes_in_buffer;
			}
			bytes_in_buffer--;
			cinfo.image_height=((uint)input_bytes[next_input_byte++])<<8;
			if(bytes_in_buffer==0)
			{
				if(!datasrc.fill_input_buffer(cinfo)) return false;
				input_bytes=datasrc.input_bytes;
				next_input_byte=datasrc.next_input_byte;
				bytes_in_buffer=datasrc.bytes_in_buffer;
			}
			bytes_in_buffer--;
			cinfo.image_height+=input_bytes[next_input_byte++];

			if(bytes_in_buffer==0)
			{
				if(!datasrc.fill_input_buffer(cinfo)) return false;
				input_bytes=datasrc.input_bytes;
				next_input_byte=datasrc.next_input_byte;
				bytes_in_buffer=datasrc.bytes_in_buffer;
			}
			bytes_in_buffer--;
			cinfo.image_width=((uint)input_bytes[next_input_byte++])<<8;
			if(bytes_in_buffer==0)
			{
				if(!datasrc.fill_input_buffer(cinfo)) return false;
				input_bytes=datasrc.input_bytes;
				next_input_byte=datasrc.next_input_byte;
				bytes_in_buffer=datasrc.bytes_in_buffer;
			}
			bytes_in_buffer--;
			cinfo.image_width+=input_bytes[next_input_byte++];

			if(bytes_in_buffer==0)
			{
				if(!datasrc.fill_input_buffer(cinfo)) return false;
				input_bytes=datasrc.input_bytes;
				next_input_byte=datasrc.next_input_byte;
				bytes_in_buffer=datasrc.bytes_in_buffer;
			}
			bytes_in_buffer--;
			cinfo.num_components=input_bytes[next_input_byte++];

			length-=8;

			TRACEMS4(cinfo, 1, J_MESSAGE_CODE.JTRC_SOF, cinfo.unread_marker, (int)cinfo.image_width, (int)cinfo.image_height, cinfo.num_components);

			if(cinfo.marker.saw_SOF) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_SOF_DUPLICATE);

			// We don't support files in which the image height is initially specified
			// as 0 and is later redefined by DNL. As long as we have to check that,
			// might as well have a general sanity check.
			if(cinfo.image_height<=0||cinfo.image_width<=0||cinfo.num_components<=0) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_EMPTY_IMAGE);

			if(length!=(cinfo.num_components*3)) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_LENGTH);

			if(cinfo.comp_info==null)	// do only once, even if suspend
			{
				try
				{
					cinfo.comp_info=new jpeg_component_info[cinfo.num_components];
					for(int i=0; i<cinfo.num_components; i++) cinfo.comp_info[i]=new jpeg_component_info();
				}
				catch
				{
					ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
				}
			}

			for(int ci=0; ci<cinfo.num_components; ci++)
			{
				jpeg_component_info compptr=cinfo.comp_info[ci];

				compptr.component_index=ci;

				if(bytes_in_buffer==0)
				{
					if(!datasrc.fill_input_buffer(cinfo)) return false;
					input_bytes=datasrc.input_bytes;
					next_input_byte=datasrc.next_input_byte;
					bytes_in_buffer=datasrc.bytes_in_buffer;
				}
				bytes_in_buffer--;
				compptr.component_id=input_bytes[next_input_byte++];

				if(bytes_in_buffer==0)
				{
					if(!datasrc.fill_input_buffer(cinfo)) return false;
					input_bytes=datasrc.input_bytes;
					next_input_byte=datasrc.next_input_byte;
					bytes_in_buffer=datasrc.bytes_in_buffer;
				}
				bytes_in_buffer--;
				int c=input_bytes[next_input_byte++];

				compptr.h_samp_factor=(c>>4)&15;
				compptr.v_samp_factor=(c)&15;

				if(bytes_in_buffer==0)
				{
					if(!datasrc.fill_input_buffer(cinfo)) return false;
					input_bytes=datasrc.input_bytes;
					next_input_byte=datasrc.next_input_byte;
					bytes_in_buffer=datasrc.bytes_in_buffer;
				}
				bytes_in_buffer--;
				compptr.quant_tbl_no=input_bytes[next_input_byte++];

				TRACEMS4(cinfo, 1, J_MESSAGE_CODE.JTRC_SOF_COMPONENT,
					 compptr.component_id, compptr.h_samp_factor,
					 compptr.v_samp_factor, compptr.quant_tbl_no);
			}

			cinfo.marker.saw_SOF=true;

			datasrc.input_bytes=input_bytes;
			datasrc.next_input_byte=next_input_byte;
			datasrc.bytes_in_buffer=bytes_in_buffer;
			return true;
		}

		// Process a SOS marker
		static bool get_sos(jpeg_decompress cinfo)
		{
			jpeg_source_mgr datasrc=cinfo.src;
			byte[] input_bytes=datasrc.input_bytes;
			int next_input_byte=datasrc.next_input_byte;
			uint bytes_in_buffer=datasrc.bytes_in_buffer;

			if(!cinfo.marker.saw_SOF) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_SOS_NO_SOF);

			if(bytes_in_buffer==0)
			{
				if(!datasrc.fill_input_buffer(cinfo)) return false;
				input_bytes=datasrc.input_bytes;
				next_input_byte=datasrc.next_input_byte;
				bytes_in_buffer=datasrc.bytes_in_buffer;
			}
			bytes_in_buffer--;
			int length=((int)input_bytes[next_input_byte++])<<8;
			if(bytes_in_buffer==0)
			{
				if(!datasrc.fill_input_buffer(cinfo)) return false;
				input_bytes=datasrc.input_bytes;
				next_input_byte=datasrc.next_input_byte;
				bytes_in_buffer=datasrc.bytes_in_buffer;
			}
			bytes_in_buffer--;
			length+=input_bytes[next_input_byte++];

			// Number of components
			if(bytes_in_buffer==0)
			{
				if(!datasrc.fill_input_buffer(cinfo)) return false;
				input_bytes=datasrc.input_bytes;
				next_input_byte=datasrc.next_input_byte;
				bytes_in_buffer=datasrc.bytes_in_buffer;
			}
			bytes_in_buffer--;
			int n=input_bytes[next_input_byte++];

			TRACEMS1(cinfo, 1, J_MESSAGE_CODE.JTRC_SOS, n);

			// pseudo SOS marker only allowed in progressive mode
			if(length!=(n*2+6)||n>MAX_COMPS_IN_SCAN) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_LENGTH);

			cinfo.comps_in_scan=n;

			int c;
			// Collect the component-spec parameters
			for(int i=0; i<n; i++)
			{
				if(bytes_in_buffer==0)
				{
					if(!datasrc.fill_input_buffer(cinfo)) return false;
					input_bytes=datasrc.input_bytes;
					next_input_byte=datasrc.next_input_byte;
					bytes_in_buffer=datasrc.bytes_in_buffer;
				}
				bytes_in_buffer--;
				int cc=input_bytes[next_input_byte++];

				if(bytes_in_buffer==0)
				{
					if(!datasrc.fill_input_buffer(cinfo)) return false;
					input_bytes=datasrc.input_bytes;
					next_input_byte=datasrc.next_input_byte;
					bytes_in_buffer=datasrc.bytes_in_buffer;
				}
				bytes_in_buffer--;
				c=input_bytes[next_input_byte++];

				jpeg_component_info compptr=null;
				for(int ci=0; ci<cinfo.num_components; ci++)
				{
					compptr=cinfo.comp_info[ci];
					if(cc==compptr.component_id) goto id_found;
				}

				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_COMPONENT_ID, cc);

id_found:

				cinfo.cur_comp_info[i]=compptr;
				compptr.dc_tbl_no=(c>>4)&15;
				compptr.ac_tbl_no=c&15;

				TRACEMS3(cinfo, 1, J_MESSAGE_CODE.JTRC_SOS_COMPONENT, cc, compptr.dc_tbl_no, compptr.ac_tbl_no);
			}

			// Collect the additional scan parameters Ss, Se, Ah/Al.
			if(bytes_in_buffer==0)
			{
				if(!datasrc.fill_input_buffer(cinfo)) return false;
				input_bytes=datasrc.input_bytes;
				next_input_byte=datasrc.next_input_byte;
				bytes_in_buffer=datasrc.bytes_in_buffer;
			}
			bytes_in_buffer--;
			cinfo.Ss=input_bytes[next_input_byte++];

			if(bytes_in_buffer==0)
			{
				if(!datasrc.fill_input_buffer(cinfo)) return false;
				input_bytes=datasrc.input_bytes;
				next_input_byte=datasrc.next_input_byte;
				bytes_in_buffer=datasrc.bytes_in_buffer;
			}
			bytes_in_buffer--;
			cinfo.Se=input_bytes[next_input_byte++];

			if(bytes_in_buffer==0)
			{
				if(!datasrc.fill_input_buffer(cinfo)) return false;
				input_bytes=datasrc.input_bytes;
				next_input_byte=datasrc.next_input_byte;
				bytes_in_buffer=datasrc.bytes_in_buffer;
			}
			bytes_in_buffer--;
			c=input_bytes[next_input_byte++];
			cinfo.Ah=(c>>4)&15;
			cinfo.Al=c&15;

			TRACEMS4(cinfo, 1, J_MESSAGE_CODE.JTRC_SOS_PARAMS, cinfo.Ss, cinfo.Se, cinfo.Ah, cinfo.Al);

			// Prepare to scan data & restart markers
			cinfo.marker.next_restart_num=0;

			// Count another (non-pseudo) SOS marker
			if(n!=0) cinfo.input_scan_number++;

			datasrc.input_bytes=input_bytes;
			datasrc.next_input_byte=next_input_byte;
			datasrc.bytes_in_buffer=bytes_in_buffer;

			return true;
		}

		// Process a DAC marker
		static bool get_dac(jpeg_decompress cinfo)
		{
#if D_ARITH_CODING_SUPPORTED
			jpeg_source_mgr datasrc=cinfo.src;
			byte[] input_bytes=datasrc.input_bytes;
			int next_input_byte=datasrc.next_input_byte;
			uint bytes_in_buffer=datasrc.bytes_in_buffer;

			if(bytes_in_buffer==0)
			{
				if(!datasrc.fill_input_buffer(cinfo)) return false;
				input_bytes=datasrc.input_bytes;
				next_input_byte=datasrc.next_input_byte;
				bytes_in_buffer=datasrc.bytes_in_buffer;
			}
			bytes_in_buffer--;
			int length=((int)input_bytes[next_input_byte++])<<8;
			if(bytes_in_buffer==0)
			{
				if(!datasrc.fill_input_buffer(cinfo)) return false;
				input_bytes=datasrc.input_bytes;
				next_input_byte=datasrc.next_input_byte;
				bytes_in_buffer=datasrc.bytes_in_buffer;
			}
			bytes_in_buffer--;
			length+=input_bytes[next_input_byte++];

			length-=2;

			while(length>0)
			{
				if(bytes_in_buffer==0)
				{
					if(!datasrc.fill_input_buffer(cinfo)) return false;
					input_bytes=datasrc.input_bytes;
					next_input_byte=datasrc.next_input_byte;
					bytes_in_buffer=datasrc.bytes_in_buffer;
				}
				bytes_in_buffer--;
				int index=input_bytes[next_input_byte++];

				if(bytes_in_buffer==0)
				{
					if(!datasrc.fill_input_buffer(cinfo)) return false;
					input_bytes=datasrc.input_bytes;
					next_input_byte=datasrc.next_input_byte;
					bytes_in_buffer=datasrc.bytes_in_buffer;
				}
				bytes_in_buffer--;
				int val=input_bytes[next_input_byte++];

				length-=2;

				TRACEMS2(cinfo, 1, J_MESSAGE_CODE.JTRC_DAC, index, val);

				if(index<0||index>=(2*NUM_ARITH_TBLS)) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_DAC_INDEX, index);

				if(index>=NUM_ARITH_TBLS)
				{ // define AC table
					cinfo.arith_ac_K[index-NUM_ARITH_TBLS]=(byte)val;
				}
				else
				{ // define DC table
					cinfo.arith_dc_L[index]=(byte)(val&0x0F);
					cinfo.arith_dc_U[index]=(byte)(val>>4);
					if(cinfo.arith_dc_L[index]>cinfo.arith_dc_U[index]) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_DAC_VALUE, val);
				}
			}

			if(length!=0) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_LENGTH);

			datasrc.input_bytes=input_bytes;
			datasrc.next_input_byte=next_input_byte;
			datasrc.bytes_in_buffer=bytes_in_buffer;

			return true;
#else // ! D_ARITH_CODING_SUPPORTED
			return skip_variable(cinfo);
#endif // D_ARITH_CODING_SUPPORTED
		}

		// Process a DHT marker
		static bool get_dht(jpeg_decompress cinfo)
		{
			jpeg_source_mgr datasrc=cinfo.src;
			byte[] input_bytes=datasrc.input_bytes;
			int next_input_byte=datasrc.next_input_byte;
			uint bytes_in_buffer=datasrc.bytes_in_buffer;

			if(bytes_in_buffer==0)
			{
				if(!datasrc.fill_input_buffer(cinfo)) return false;
				input_bytes=datasrc.input_bytes;
				next_input_byte=datasrc.next_input_byte;
				bytes_in_buffer=datasrc.bytes_in_buffer;
			}
			bytes_in_buffer--;
			int length=((int)input_bytes[next_input_byte++])<<8;
			if(bytes_in_buffer==0)
			{
				if(!datasrc.fill_input_buffer(cinfo)) return false;
				input_bytes=datasrc.input_bytes;
				next_input_byte=datasrc.next_input_byte;
				bytes_in_buffer=datasrc.bytes_in_buffer;
			}
			bytes_in_buffer--;
			length+=input_bytes[next_input_byte++];

			length-=2;

			byte[] bits=new byte[17];
			byte[] huffval=new byte[256];

			while(length>16)
			{
				if(bytes_in_buffer==0)
				{
					if(!datasrc.fill_input_buffer(cinfo)) return false;
					input_bytes=datasrc.input_bytes;
					next_input_byte=datasrc.next_input_byte;
					bytes_in_buffer=datasrc.bytes_in_buffer;
				}
				bytes_in_buffer--;
				int index=input_bytes[next_input_byte++];

				TRACEMS1(cinfo, 1, J_MESSAGE_CODE.JTRC_DHT, index);

				bits[0]=0;
				int count=0;
				for(int i=1; i<=16; i++)
				{
					if(bytes_in_buffer==0)
					{
						if(!datasrc.fill_input_buffer(cinfo)) return false;
						input_bytes=datasrc.input_bytes;
						next_input_byte=datasrc.next_input_byte;
						bytes_in_buffer=datasrc.bytes_in_buffer;
					}
					bytes_in_buffer--;
					bits[i]=input_bytes[next_input_byte++];

					count+=bits[i];
				}

				length-=1+16;

				TRACEMS8(cinfo, 2, J_MESSAGE_CODE.JTRC_HUFFBITS, bits[1], bits[2], bits[3], bits[4], bits[5], bits[6], bits[7], bits[8]);
				TRACEMS8(cinfo, 2, J_MESSAGE_CODE.JTRC_HUFFBITS, bits[9], bits[10], bits[11], bits[12], bits[13], bits[14], bits[15], bits[16]);

				// Here we just do minimal validation of the counts to avoid walking
				// off the end of our table space. jdhuff.cs will check more carefully.
				if(count>256||((int)count)>length) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_HUFF_TABLE);

				for(int i=0; i<count; i++)
				{
					if(bytes_in_buffer==0)
					{
						if(!datasrc.fill_input_buffer(cinfo)) return false;
						input_bytes=datasrc.input_bytes;
						next_input_byte=datasrc.next_input_byte;
						bytes_in_buffer=datasrc.bytes_in_buffer;
					}
					bytes_in_buffer--;
					huffval[i]=input_bytes[next_input_byte++];
				}

				length-=count;

				if((index&0x10)!=0)
				{ // AC table definition
					index-=0x10;
					if(index<0||index>=NUM_HUFF_TBLS) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_DHT_INDEX, index);

					if(cinfo.ac_huff_tbl_ptrs[index]==null) cinfo.ac_huff_tbl_ptrs[index]=jpeg_alloc_huff_table(cinfo);

					bits.CopyTo(cinfo.ac_huff_tbl_ptrs[index].bits, 0);
					huffval.CopyTo(cinfo.ac_huff_tbl_ptrs[index].huffval, 0);
				}
				else
				{ // DC table definition
					if(index<0||index>=NUM_HUFF_TBLS) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_DHT_INDEX, index);

					if(cinfo.dc_huff_tbl_ptrs[index]==null) cinfo.dc_huff_tbl_ptrs[index]=jpeg_alloc_huff_table(cinfo);

					bits.CopyTo(cinfo.dc_huff_tbl_ptrs[index].bits, 0);
					huffval.CopyTo(cinfo.dc_huff_tbl_ptrs[index].huffval, 0);
				}
			}

			if(length!=0) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_LENGTH);

			datasrc.input_bytes=input_bytes;
			datasrc.next_input_byte=next_input_byte;
			datasrc.bytes_in_buffer=bytes_in_buffer;

			return true;
		}

		// Process a DQT marker
		static bool get_dqt(jpeg_decompress cinfo)
		{
			jpeg_source_mgr datasrc=cinfo.src;
			byte[] input_bytes=datasrc.input_bytes;
			int next_input_byte=datasrc.next_input_byte;
			uint bytes_in_buffer=datasrc.bytes_in_buffer;

			if(bytes_in_buffer==0)
			{
				if(!datasrc.fill_input_buffer(cinfo)) return false;
				input_bytes=datasrc.input_bytes;
				next_input_byte=datasrc.next_input_byte;
				bytes_in_buffer=datasrc.bytes_in_buffer;
			}
			bytes_in_buffer--;
			int length=((int)input_bytes[next_input_byte++])<<8;
			if(bytes_in_buffer==0)
			{
				if(!datasrc.fill_input_buffer(cinfo)) return false;
				input_bytes=datasrc.input_bytes;
				next_input_byte=datasrc.next_input_byte;
				bytes_in_buffer=datasrc.bytes_in_buffer;
			}
			bytes_in_buffer--;
			length+=input_bytes[next_input_byte++];

			length-=2;

			while(length>0)
			{
				if(bytes_in_buffer==0)
				{
					if(!datasrc.fill_input_buffer(cinfo)) return false;
					input_bytes=datasrc.input_bytes;
					next_input_byte=datasrc.next_input_byte;
					bytes_in_buffer=datasrc.bytes_in_buffer;
				}
				bytes_in_buffer--;
				int n=input_bytes[next_input_byte++];

				int prec=n>>4;
				n&=0x0F;

				TRACEMS2(cinfo, 1, J_MESSAGE_CODE.JTRC_DQT, n, prec);

				if(n>=NUM_QUANT_TBLS) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_DQT_INDEX, n);

				if(cinfo.quant_tbl_ptrs[n]==null) cinfo.quant_tbl_ptrs[n]=jpeg_alloc_quant_table(cinfo);
				JQUANT_TBL quant_ptr=cinfo.quant_tbl_ptrs[n];

				for(int i=0; i<DCTSIZE2; i++)
				{
					uint tmp;
					if(prec!=0)
					{
						if(bytes_in_buffer==0)
						{
							if(!datasrc.fill_input_buffer(cinfo)) return false;
							input_bytes=datasrc.input_bytes;
							next_input_byte=datasrc.next_input_byte;
							bytes_in_buffer=datasrc.bytes_in_buffer;
						}
						bytes_in_buffer--;
						tmp=((uint)input_bytes[next_input_byte++])<<8;
						if(bytes_in_buffer==0)
						{
							if(!datasrc.fill_input_buffer(cinfo)) return false;
							input_bytes=datasrc.input_bytes;
							next_input_byte=datasrc.next_input_byte;
							bytes_in_buffer=datasrc.bytes_in_buffer;
						}
						bytes_in_buffer--;
						tmp+=input_bytes[next_input_byte++];
					}
					else
					{
						if(bytes_in_buffer==0)
						{
							if(!datasrc.fill_input_buffer(cinfo)) return false;
							input_bytes=datasrc.input_bytes;
							next_input_byte=datasrc.next_input_byte;
							bytes_in_buffer=datasrc.bytes_in_buffer;
						}
						bytes_in_buffer--;
						tmp=input_bytes[next_input_byte++];
					}
					// We convert the zigzag-order table to natural array order.
					quant_ptr.quantval[jpeg_natural_order[i]]=(ushort)tmp;
				}

				if(cinfo.err.trace_level>=2)
				{
					for(int i=0; i<DCTSIZE2; i+=8)
					{
						TRACEMS8(cinfo, 2, J_MESSAGE_CODE.JTRC_QUANTVALS, quant_ptr.quantval[i], quant_ptr.quantval[i+1], quant_ptr.quantval[i+2], quant_ptr.quantval[i+3],
							 quant_ptr.quantval[i+4], quant_ptr.quantval[i+5], quant_ptr.quantval[i+6], quant_ptr.quantval[i+7]);
					}
				}

				length-=DCTSIZE2+1;
				if(prec!=0) length-=DCTSIZE2;
			}

			if(length!=0) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_LENGTH);

			datasrc.input_bytes=input_bytes;
			datasrc.next_input_byte=next_input_byte;
			datasrc.bytes_in_buffer=bytes_in_buffer;

			return true;
		}

		// Process a DRI marker
		static bool get_dri(jpeg_decompress cinfo)
		{
			jpeg_source_mgr datasrc=cinfo.src;
			byte[] input_bytes=datasrc.input_bytes;
			int next_input_byte=datasrc.next_input_byte;
			uint bytes_in_buffer=datasrc.bytes_in_buffer;

			if(bytes_in_buffer==0)
			{
				if(!datasrc.fill_input_buffer(cinfo)) return false;
				input_bytes=datasrc.input_bytes;
				next_input_byte=datasrc.next_input_byte;
				bytes_in_buffer=datasrc.bytes_in_buffer;
			}
			bytes_in_buffer--;
			int length=((int)input_bytes[next_input_byte++])<<8;
			if(bytes_in_buffer==0)
			{
				if(!datasrc.fill_input_buffer(cinfo)) return false;
				input_bytes=datasrc.input_bytes;
				next_input_byte=datasrc.next_input_byte;
				bytes_in_buffer=datasrc.bytes_in_buffer;
			}
			bytes_in_buffer--;
			length+=input_bytes[next_input_byte++];

			if(length!=4) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_LENGTH);

			if(bytes_in_buffer==0)
			{
				if(!datasrc.fill_input_buffer(cinfo)) return false;
				input_bytes=datasrc.input_bytes;
				next_input_byte=datasrc.next_input_byte;
				bytes_in_buffer=datasrc.bytes_in_buffer;
			}
			bytes_in_buffer--;
			uint tmp=((uint)input_bytes[next_input_byte++])<<8;
			if(bytes_in_buffer==0)
			{
				if(!datasrc.fill_input_buffer(cinfo)) return false;
				input_bytes=datasrc.input_bytes;
				next_input_byte=datasrc.next_input_byte;
				bytes_in_buffer=datasrc.bytes_in_buffer;
			}
			bytes_in_buffer--;
			tmp+=input_bytes[next_input_byte++];

			TRACEMS1(cinfo, 1, J_MESSAGE_CODE.JTRC_DRI, (int)tmp);

			cinfo.restart_interval=tmp;

			datasrc.input_bytes=input_bytes;
			datasrc.next_input_byte=next_input_byte;
			datasrc.bytes_in_buffer=bytes_in_buffer;

			return true;
		}

		// Routines for processing APPn and COM markers.
		// These are either saved in memory or discarded, per application request.
		// APP0 and APP14 are specially checked to see if they are
		// JFIF and Adobe markers, respectively.
		const int APP0_DATA_LEN=14;		// Length of interesting data in APP0
		const int APP14_DATA_LEN=12;	// Length of interesting data in APP14 
		const int APPN_DATA_LEN=14;		// Must be the largest of the above!!

		// Examine first few bytes from an APP0.
		// Take appropriate action if it is a JFIF marker.
		// datalen is # of bytes at data[], remaining is length of rest of marker data.
		static void examine_app0(jpeg_decompress cinfo, byte[] data, uint datalen, int remaining)
		{
			int totallen=(int)datalen+remaining;

			if(datalen>=APP0_DATA_LEN&&data[0]==0x4A&&data[1]==0x46&&data[2]==0x49&&data[3]==0x46&&data[4]==0)
			{
				// Found JFIF APP0 marker: save info
				cinfo.saw_JFIF_marker=true;
				cinfo.JFIF_major_version=data[5];
				cinfo.JFIF_minor_version=data[6];
				cinfo.density_unit=data[7];
				cinfo.X_density=(ushort)((data[8]<<8)+data[9]);
				cinfo.Y_density=(ushort)((data[10]<<8)+data[11]);

				// Check version.
				// Major version must be 1, anything else signals an incompatible change.
				// (We used to treat this as an error, but now it's a nonfatal warning,
				// because some bozo at Hijaak couldn't read the spec.)
				// Minor version should be 0..2, but process anyway if newer.
				if(cinfo.JFIF_major_version!=1) WARNMS2(cinfo, J_MESSAGE_CODE.JWRN_JFIF_MAJOR, cinfo.JFIF_major_version, cinfo.JFIF_minor_version);

				// Generate trace messages
				TRACEMS5(cinfo, 1, J_MESSAGE_CODE.JTRC_JFIF, cinfo.JFIF_major_version, cinfo.JFIF_minor_version, cinfo.X_density, cinfo.Y_density, cinfo.density_unit);

				// Validate thumbnail dimensions and issue appropriate messages
				if((data[12]|data[13])!=0) TRACEMS2(cinfo, 1, J_MESSAGE_CODE.JTRC_JFIF_THUMBNAIL, data[12], data[13]);
				totallen-=APP0_DATA_LEN;
				if(totallen!=((int)data[12]*(int)data[13]*(int)3)) TRACEMS1(cinfo, 1, J_MESSAGE_CODE.JTRC_JFIF_BADTHUMBNAILSIZE, (int)totallen);
			}
			else if(datalen>=6&&data[0]==0x4A&&data[1]==0x46&&data[2]==0x58&&data[3]==0x58&&data[4]==0)
			{
				// Found JFIF "JFXX" extension APP0 marker
				// The library doesn't actually do anything with these,
				// but we try to produce a helpful trace message.
				switch(data[5])
				{
					case 0x10: TRACEMS1(cinfo, 1, J_MESSAGE_CODE.JTRC_THUMB_JPEG, (int)totallen); break;
					case 0x11: TRACEMS1(cinfo, 1, J_MESSAGE_CODE.JTRC_THUMB_PALETTE, (int)totallen); break;
					case 0x13: TRACEMS1(cinfo, 1, J_MESSAGE_CODE.JTRC_THUMB_RGB, (int)totallen); break;
					default: TRACEMS2(cinfo, 1, J_MESSAGE_CODE.JTRC_JFIF_EXTENSION, data[5], (int)totallen); break;
				}
			}
			else
			{
				// Start of APP0 does not match "JFIF" or "JFXX", or too short
				TRACEMS1(cinfo, 1, J_MESSAGE_CODE.JTRC_APP0, (int)totallen);
			}
		}

		// Examine first few bytes from an APP14.
		// Take appropriate action if it is an Adobe marker.
		// datalen is # of bytes at data[], remaining is length of rest of marker data.
		static void examine_app14(jpeg_decompress cinfo, byte[] data, uint datalen, int remaining)
		{
			if(datalen>=APP14_DATA_LEN&&data[0]==0x41&&data[1]==0x64&&data[2]==0x6F&&data[3]==0x62&&data[4]==0x65)
			{
				// Found Adobe APP14 marker
				int version=(data[5]<<8)+data[6];
				int flags0=(data[7]<<8)+data[8];
				int flags1=(data[9]<<8)+data[10];
				int transform=data[11];
				TRACEMS4(cinfo, 1, J_MESSAGE_CODE.JTRC_ADOBE, version, flags0, flags1, transform);
				cinfo.saw_Adobe_marker=true;
				cinfo.Adobe_transform=(byte)transform;
			}
			else
			{
				// Start of APP14 does not match "Adobe", or too short
				TRACEMS1(cinfo, 1, J_MESSAGE_CODE.JTRC_APP14, (int)(datalen+remaining));
			}
		}

		// Process an APP0 or APP14 marker without saving it
		static bool get_interesting_appn(jpeg_decompress cinfo)
		{
			jpeg_source_mgr datasrc=cinfo.src;
			byte[] input_bytes=datasrc.input_bytes;
			int next_input_byte=datasrc.next_input_byte;
			uint bytes_in_buffer=datasrc.bytes_in_buffer;

			if(bytes_in_buffer==0)
			{
				if(!datasrc.fill_input_buffer(cinfo)) return false;
				input_bytes=datasrc.input_bytes;
				next_input_byte=datasrc.next_input_byte;
				bytes_in_buffer=datasrc.bytes_in_buffer;
			}
			bytes_in_buffer--;
			int length=((int)input_bytes[next_input_byte++])<<8;
			if(bytes_in_buffer==0)
			{
				if(!datasrc.fill_input_buffer(cinfo)) return false;
				input_bytes=datasrc.input_bytes;
				next_input_byte=datasrc.next_input_byte;
				bytes_in_buffer=datasrc.bytes_in_buffer;
			}
			bytes_in_buffer--;
			length+=input_bytes[next_input_byte++];

			length-=2;

			// get the interesting part of the marker data
			uint numtoread=0;
			if(length>=APPN_DATA_LEN) numtoread=APPN_DATA_LEN;
			else if(length>0) numtoread=(uint)length;

			byte[] b=new byte[APPN_DATA_LEN];
			for(uint i=0; i<numtoread; i++)
			{
				if(bytes_in_buffer==0)
				{
					if(!datasrc.fill_input_buffer(cinfo)) return false;
					input_bytes=datasrc.input_bytes;
					next_input_byte=datasrc.next_input_byte;
					bytes_in_buffer=datasrc.bytes_in_buffer;
				}
				bytes_in_buffer--;
				b[i]=input_bytes[next_input_byte++];
			}
			length-=(int)numtoread;

			// process it
			switch(cinfo.unread_marker)
			{
				case (int)JPEG_MARKER.M_APP0: examine_app0(cinfo, b, numtoread, length); break;
				case (int)JPEG_MARKER.M_APP14: examine_app14(cinfo, b, numtoread, length); break;
				default:
					// can't get here unless jpeg_save_markers chooses wrong processor
					ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_UNKNOWN_MARKER, cinfo.unread_marker);
					break;
			}

			// skip any remaining data -- could be lots
			datasrc.input_bytes=input_bytes;
			datasrc.next_input_byte=next_input_byte;
			datasrc.bytes_in_buffer=bytes_in_buffer;

			if(length>0) cinfo.src.skip_input_data(cinfo, length);

			return true;
		}

#if SAVE_MARKERS_SUPPORTED
		// Save an APPn or COM marker into the marker list
		static bool save_marker(jpeg_decompress cinfo)
		{
			my_marker_reader marker=(my_marker_reader)cinfo.marker;
			jpeg_marker_struct cur_marker=marker.cur_marker;
			uint bytes_read, data_length;
			byte[] data;
			jpeg_source_mgr datasrc=cinfo.src;
			byte[] input_bytes=datasrc.input_bytes;
			int next_input_byte=datasrc.next_input_byte;
			uint bytes_in_buffer=datasrc.bytes_in_buffer;

			int length=0;
			uint data_ind=0;
			if(cur_marker==null)
			{
				// begin reading a marker
				if(bytes_in_buffer==0)
				{
					if(!datasrc.fill_input_buffer(cinfo)) return false;
					input_bytes=datasrc.input_bytes;
					next_input_byte=datasrc.next_input_byte;
					bytes_in_buffer=datasrc.bytes_in_buffer;
				}
				bytes_in_buffer--;
				length=((int)input_bytes[next_input_byte++])<<8;
				if(bytes_in_buffer==0)
				{
					if(!datasrc.fill_input_buffer(cinfo)) return false;
					input_bytes=datasrc.input_bytes;
					next_input_byte=datasrc.next_input_byte;
					bytes_in_buffer=datasrc.bytes_in_buffer;
				}
				bytes_in_buffer--;
				length+=input_bytes[next_input_byte++];

				length-=2;
				if(length>=0)
				{	// watch out for bogus length word
					// figure out how much we want to save
					uint limit;
					if(cinfo.unread_marker==(int)JPEG_MARKER.M_COM) limit=marker.length_limit_COM;
					else limit=marker.length_limit_APPn[cinfo.unread_marker-(int)JPEG_MARKER.M_APP0];

					if((uint)length<limit) limit=(uint)length;

					// allocate and initialize the marker item
					try
					{
						cur_marker=new jpeg_marker_struct();
						cur_marker.data=new byte[limit];
					}
					catch
					{
						ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
					}
					cur_marker.next=null;
					cur_marker.marker=(byte)cinfo.unread_marker;
					cur_marker.original_length=(uint)length;
					cur_marker.data_length=limit;

					// data area is just beyond the jpeg_marker_struct
					data=cur_marker.data;
					marker.cur_marker=cur_marker;
					marker.bytes_read=0;
					bytes_read=0;
					data_length=limit;
				}
				else
				{
					// deal with bogus length word
					bytes_read=data_length=0;
					data=null;
				}
			}
			else
			{
				// resume reading a marker
				bytes_read=marker.bytes_read;
				data_length=cur_marker.data_length;
				data=cur_marker.data;
				data_ind=bytes_read;
			}

			while(bytes_read<data_length)
			{
				// move the restart point to here
				datasrc.input_bytes=input_bytes;
				datasrc.next_input_byte=next_input_byte;
				datasrc.bytes_in_buffer=bytes_in_buffer;

				marker.bytes_read=bytes_read;

				// If there's not at least one byte in buffer, suspend
				if(bytes_in_buffer==0)
				{
					if(!datasrc.fill_input_buffer(cinfo)) return false;
					input_bytes=datasrc.input_bytes;
					next_input_byte=datasrc.next_input_byte;
					bytes_in_buffer=datasrc.bytes_in_buffer;
				}

				// Copy bytes with reasonable rapidity
				while(bytes_read<data_length&&bytes_in_buffer>0)
				{
					data[data_ind++]=input_bytes[next_input_byte++];
					bytes_in_buffer--;
					bytes_read++;
				}
			}

			// Done reading what we want to read
			if(cur_marker!=null)
			{	// will be null if bogus length word
				// Add new marker to end of list
				if(cinfo.marker_list==null)
				{
					cinfo.marker_list=cur_marker;
				}
				else
				{
					jpeg_marker_struct prev=cinfo.marker_list;
					while(prev.next!=null) prev=prev.next;
					prev.next=cur_marker;
				}

				// Reset pointer & calc remaining data length
				data=cur_marker.data;
				length=(int)(cur_marker.original_length-data_length);
			}
			// Reset to initial state for next marker
			marker.cur_marker=null;

			// Process the marker if interesting; else just make a generic trace msg
			switch(cinfo.unread_marker)
			{
				case (int)JPEG_MARKER.M_APP0: examine_app0(cinfo, data, data_length, length); break;
				case (int)JPEG_MARKER.M_APP1:
					if(data_length>=14&&data[0]==0x45&&data[1]==0x78&&data[2]==0x69&&data[3]==0x66&&data[4]==0&&data[5]==0)
					{ // Exif found
						EXIF exif=new EXIF();
						if(exif.Scan(data, 6)) cinfo.exif=exif;
					}
					break;
				case (int)JPEG_MARKER.M_APP14: examine_app14(cinfo, data, data_length, length); break;
				default: TRACEMS2(cinfo, 1, J_MESSAGE_CODE.JTRC_MISC_MARKER, cinfo.unread_marker, (int)(data_length+length)); break;
			}

			// skip any remaining data -- could be lots
			// do before skip_input_data
			datasrc.input_bytes=input_bytes;
			datasrc.next_input_byte=next_input_byte;
			datasrc.bytes_in_buffer=bytes_in_buffer;

			if(length>0) cinfo.src.skip_input_data(cinfo, (int)length);

			return true;
		}
#endif // SAVE_MARKERS_SUPPORTED

		// Skip over an unknown or uninteresting variable-length marker
		static bool skip_variable(jpeg_decompress cinfo)
		{
			jpeg_source_mgr datasrc=cinfo.src;
			byte[] input_bytes=datasrc.input_bytes;
			int next_input_byte=datasrc.next_input_byte;
			uint bytes_in_buffer=datasrc.bytes_in_buffer;

			if(bytes_in_buffer==0)
			{
				if(!datasrc.fill_input_buffer(cinfo)) return false;
				input_bytes=datasrc.input_bytes;
				next_input_byte=datasrc.next_input_byte;
				bytes_in_buffer=datasrc.bytes_in_buffer;
			}
			bytes_in_buffer--;
			int length=((int)input_bytes[next_input_byte++])<<8;
			if(bytes_in_buffer==0)
			{
				if(!datasrc.fill_input_buffer(cinfo)) return false;
				input_bytes=datasrc.input_bytes;
				next_input_byte=datasrc.next_input_byte;
				bytes_in_buffer=datasrc.bytes_in_buffer;
			}
			bytes_in_buffer--;
			length+=input_bytes[next_input_byte++];

			length-=2;

			TRACEMS2(cinfo, 1, J_MESSAGE_CODE.JTRC_MISC_MARKER, cinfo.unread_marker, length);

			// do before skip_input_data
			datasrc.input_bytes=input_bytes;
			datasrc.next_input_byte=next_input_byte;
			datasrc.bytes_in_buffer=bytes_in_buffer;

			if(length>0) cinfo.src.skip_input_data(cinfo, length);

			return true;
		}

		// Find the next JPEG marker, save it in cinfo.unread_marker.
		// Returns false if had to suspend before reaching a marker;
		// in that case cinfo.unread_marker is unchanged.
		//
		// Note that the result might not be a valid marker code,
		// but it will never be 0 or FF.
		static bool next_marker(jpeg_decompress cinfo)
		{
			jpeg_source_mgr datasrc=cinfo.src;
			byte[] input_bytes=datasrc.input_bytes;
			int next_input_byte=datasrc.next_input_byte;
			uint bytes_in_buffer=datasrc.bytes_in_buffer;

			int c;
			for(; ; )
			{
				if(bytes_in_buffer==0)
				{
					if(!datasrc.fill_input_buffer(cinfo)) return false;
					input_bytes=datasrc.input_bytes;
					next_input_byte=datasrc.next_input_byte;
					bytes_in_buffer=datasrc.bytes_in_buffer;
				}
				bytes_in_buffer--;
				c=input_bytes[next_input_byte++];

				// Skip any non-FF bytes.
				// This may look a bit inefficient, but it will not occur in a valid file.
				// We sync after each discarded byte so that a suspending data source
				// can discard the byte from its buffer.
				while(c!=0xFF)
				{
					cinfo.marker.discarded_bytes++;
					datasrc.input_bytes=input_bytes;
					datasrc.next_input_byte=next_input_byte;
					datasrc.bytes_in_buffer=bytes_in_buffer;

					if(bytes_in_buffer==0)
					{
						if(!datasrc.fill_input_buffer(cinfo)) return false;
						input_bytes=datasrc.input_bytes;
						next_input_byte=datasrc.next_input_byte;
						bytes_in_buffer=datasrc.bytes_in_buffer;
					}
					bytes_in_buffer--;
					c=input_bytes[next_input_byte++];
				}
				// This loop swallows any duplicate FF bytes. Extra FFs are legal as
				// pad bytes, so don't count them in discarded_bytes. We assume there
				// will not be so many consecutive FF bytes as to overflow a suspending
				// data source's input buffer.
				do
				{
					if(bytes_in_buffer==0)
					{
						if(!datasrc.fill_input_buffer(cinfo)) return false;
						input_bytes=datasrc.input_bytes;
						next_input_byte=datasrc.next_input_byte;
						bytes_in_buffer=datasrc.bytes_in_buffer;
					}
					bytes_in_buffer--;
					c=input_bytes[next_input_byte++];
				} while(c==0xFF);
				if(c!=0) break; // found a valid marker, exit loop

				// Reach here if we found a stuffed-zero data sequence (FF/00).
				// Discard it and loop back to try again.
				cinfo.marker.discarded_bytes+=2;
				datasrc.input_bytes=input_bytes;
				datasrc.next_input_byte=next_input_byte;
				datasrc.bytes_in_buffer=bytes_in_buffer;
			}

			if(cinfo.marker.discarded_bytes!=0)
			{
				WARNMS2(cinfo, J_MESSAGE_CODE.JWRN_EXTRANEOUS_DATA, (int)cinfo.marker.discarded_bytes, c);
				cinfo.marker.discarded_bytes=0;
			}

			cinfo.unread_marker=c;

			datasrc.input_bytes=input_bytes;
			datasrc.next_input_byte=next_input_byte;
			datasrc.bytes_in_buffer=bytes_in_buffer;

			return true;
		}

		// Like next_marker, but used to obtain the initial SOI marker.
		// For this marker, we do not allow preceding garbage or fill; otherwise,
		// we might well scan an entire input file before realizing it ain't JPEG.
		// If an application wants to process non-JFIF files, it must seek to the
		// SOI before calling the JPEG library.
		static bool first_marker(jpeg_decompress cinfo)
		{
			jpeg_source_mgr datasrc=cinfo.src;
			byte[] input_bytes=datasrc.input_bytes;
			int next_input_byte=datasrc.next_input_byte;
			uint bytes_in_buffer=datasrc.bytes_in_buffer;

			if(bytes_in_buffer==0)
			{
				if(!datasrc.fill_input_buffer(cinfo)) return false;
				input_bytes=datasrc.input_bytes;
				next_input_byte=datasrc.next_input_byte;
				bytes_in_buffer=datasrc.bytes_in_buffer;
			}
			bytes_in_buffer--;
			int c=input_bytes[next_input_byte++];

			if(bytes_in_buffer==0)
			{
				if(!datasrc.fill_input_buffer(cinfo)) return false;
				input_bytes=datasrc.input_bytes;
				next_input_byte=datasrc.next_input_byte;
				bytes_in_buffer=datasrc.bytes_in_buffer;
			}
			bytes_in_buffer--;
			int c2=input_bytes[next_input_byte++];

			if(c!=0xFF||c2!=(int)JPEG_MARKER.M_SOI) ERREXIT2(cinfo, J_MESSAGE_CODE.JERR_NO_SOI, c, c2);

			cinfo.unread_marker=c2;

			datasrc.input_bytes=input_bytes;
			datasrc.next_input_byte=next_input_byte;
			datasrc.bytes_in_buffer=bytes_in_buffer;

			return true;
		}

		// Read markers until SOS or EOI.
		//
		// Returns same codes as are defined for jpeg_consume_input:
		// JPEG_SUSPENDED, JPEG_REACHED_SOS, or JPEG_REACHED_EOI.
		static CONSUME_INPUT read_markers(jpeg_decompress cinfo)
		{
			// Outer loop repeats once for each marker.
			for(; ; )
			{
				// Collect the marker proper, unless we already did.
				// NB: first_marker() enforces the requirement that SOI appear first.
				if(cinfo.unread_marker==0)
				{
					if(!cinfo.marker.saw_SOI)
					{
						if(!first_marker(cinfo)) return CONSUME_INPUT.JPEG_SUSPENDED;
					}
					else
					{
						if(!next_marker(cinfo)) return CONSUME_INPUT.JPEG_SUSPENDED;
					}
				}

				// At this point cinfo.unread_marker contains the marker code and the
				// input point is just past the marker proper, but before any parameters.
				// A suspension will cause us to return with this state still true.
				switch(cinfo.unread_marker)
				{
					case (int)JPEG_MARKER.M_SOI: if(!get_soi(cinfo)) return CONSUME_INPUT.JPEG_SUSPENDED; break;
					case (int)JPEG_MARKER.M_SOF0: // Baseline
					case (int)JPEG_MARKER.M_SOF1: // Extended sequential, Huffman
						if(!get_sof(cinfo, J_CODEC_PROCESS.JPROC_SEQUENTIAL, false, DCTSIZE)) return CONSUME_INPUT.JPEG_SUSPENDED;
						break;
					case (int)JPEG_MARKER.M_SOF2: // Progressive, Huffman
						if(!get_sof(cinfo, J_CODEC_PROCESS.JPROC_PROGRESSIVE, false, DCTSIZE)) return CONSUME_INPUT.JPEG_SUSPENDED;
						break;
					case (int)JPEG_MARKER.M_SOF3: // Lossless, Huffman
						if(!get_sof(cinfo, J_CODEC_PROCESS.JPROC_LOSSLESS, false, 1)) return CONSUME_INPUT.JPEG_SUSPENDED;
						break;
					case (int)JPEG_MARKER.M_SOF9: // Extended sequential, arithmetic
						if(!get_sof(cinfo, J_CODEC_PROCESS.JPROC_SEQUENTIAL, true, DCTSIZE)) return CONSUME_INPUT.JPEG_SUSPENDED;
						break;
					case (int)JPEG_MARKER.M_SOF10: // Progressive, arithmetic
						if(!get_sof(cinfo, J_CODEC_PROCESS.JPROC_PROGRESSIVE, true, DCTSIZE)) return CONSUME_INPUT.JPEG_SUSPENDED;
						break;
					case (int)JPEG_MARKER.M_SOF11: // Lossless, arithmetic
						if(!get_sof(cinfo, J_CODEC_PROCESS.JPROC_LOSSLESS, true, 1)) return CONSUME_INPUT.JPEG_SUSPENDED;
						break;

					// Currently unsupported SOFn types
					case (int)JPEG_MARKER.M_SOF5:		// Differential sequential, Huffman
					case (int)JPEG_MARKER.M_SOF6:		// Differential progressive, Huffman
					case (int)JPEG_MARKER.M_SOF7:		// Differential lossless, Huffman
					case (int)JPEG_MARKER.M_JPG:		// Reserved for JPEG extensions
					case (int)JPEG_MARKER.M_SOF13:		// Differential sequential, arithmetic
					case (int)JPEG_MARKER.M_SOF14:		// Differential progressive, arithmetic
					case (int)JPEG_MARKER.M_SOF15:		// Differential lossless, arithmetic
						ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_SOF_UNSUPPORTED, cinfo.unread_marker); break;
					case (int)JPEG_MARKER.M_SOS: if(!get_sos(cinfo)) return CONSUME_INPUT.JPEG_SUSPENDED;
						cinfo.unread_marker=0; // processed the marker
						return CONSUME_INPUT.JPEG_REACHED_SOS;
					case (int)JPEG_MARKER.M_EOI:
						TRACEMS(cinfo, 1, J_MESSAGE_CODE.JTRC_EOI);
						cinfo.unread_marker=0; // processed the marker
						return CONSUME_INPUT.JPEG_REACHED_EOI;
					case (int)JPEG_MARKER.M_DAC: if(!get_dac(cinfo)) return CONSUME_INPUT.JPEG_SUSPENDED; break;
					case (int)JPEG_MARKER.M_DHT: if(!get_dht(cinfo)) return CONSUME_INPUT.JPEG_SUSPENDED; break;
					case (int)JPEG_MARKER.M_DQT: if(!get_dqt(cinfo)) return CONSUME_INPUT.JPEG_SUSPENDED; break;
					case (int)JPEG_MARKER.M_DRI: if(!get_dri(cinfo)) return CONSUME_INPUT.JPEG_SUSPENDED; break;
					case (int)JPEG_MARKER.M_APP0:
					case (int)JPEG_MARKER.M_APP1:
					case (int)JPEG_MARKER.M_APP2:
					case (int)JPEG_MARKER.M_APP3:
					case (int)JPEG_MARKER.M_APP4:
					case (int)JPEG_MARKER.M_APP5:
					case (int)JPEG_MARKER.M_APP6:
					case (int)JPEG_MARKER.M_APP7:
					case (int)JPEG_MARKER.M_APP8:
					case (int)JPEG_MARKER.M_APP9:
					case (int)JPEG_MARKER.M_APP10:
					case (int)JPEG_MARKER.M_APP11:
					case (int)JPEG_MARKER.M_APP12:
					case (int)JPEG_MARKER.M_APP13:
					case (int)JPEG_MARKER.M_APP14:
					case (int)JPEG_MARKER.M_APP15: if(!((my_marker_reader)cinfo.marker).process_APPn[cinfo.unread_marker-(int)JPEG_MARKER.M_APP0](cinfo)) return CONSUME_INPUT.JPEG_SUSPENDED; break;
					case (int)JPEG_MARKER.M_COM: if(!((my_marker_reader)cinfo.marker).process_COM(cinfo)) return CONSUME_INPUT.JPEG_SUSPENDED; break;
					case (int)JPEG_MARKER.M_RST0: // these are all parameterless
					case (int)JPEG_MARKER.M_RST1:
					case (int)JPEG_MARKER.M_RST2:
					case (int)JPEG_MARKER.M_RST3:
					case (int)JPEG_MARKER.M_RST4:
					case (int)JPEG_MARKER.M_RST5:
					case (int)JPEG_MARKER.M_RST6:
					case (int)JPEG_MARKER.M_RST7:
					case (int)JPEG_MARKER.M_TEM: TRACEMS1(cinfo, 1, J_MESSAGE_CODE.JTRC_PARMLESS_MARKER, cinfo.unread_marker); break;
					case (int)JPEG_MARKER.M_DNL: // Ignore DNL ... perhaps the wrong thing
						if(!skip_variable(cinfo)) return CONSUME_INPUT.JPEG_SUSPENDED; break;
					default: // must be DHP, EXP, JPGn, or RESn
						// For now, we treat the reserved markers as fatal errors since they are
						// likely to be used to signal incompatible JPEG Part 3 extensions.
						// Once the JPEG 3 version-number marker is well defined, this code
						// ought to change!
						ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_UNKNOWN_MARKER, cinfo.unread_marker);
						break;
				}
				// Successfully processed marker, so reset state variable
				cinfo.unread_marker=0;
			} // end loop
		}

		// Read a restart marker, which is expected to appear next in the datastream;
		// if the marker is not there, take appropriate recovery action.
		// Returns false if suspension is required.
		//
		// This is called by the entropy decoder after it has read an appropriate
		// number of MCUs. cinfo.unread_marker may be nonzero if the entropy decoder
		// has already read a marker from the data source. Under normal conditions
		// cinfo.unread_marker will be reset to 0 before returning; if not reset,
		// it holds a marker which the decoder will be unable to read past.
		static bool read_restart_marker(jpeg_decompress cinfo)
		{
			// Obtain a marker unless we already did.
			// Note that next_marker will complain if it skips any data.
			if(cinfo.unread_marker==0)
			{
				if(!next_marker(cinfo)) return false;
			}

			if(cinfo.unread_marker==((int)JPEG_MARKER.M_RST0+cinfo.marker.next_restart_num))
			{
				// Normal case --- swallow the marker and let entropy decoder continue
				TRACEMS1(cinfo, 3, J_MESSAGE_CODE.JTRC_RST, cinfo.marker.next_restart_num);
				cinfo.unread_marker=0;
			}
			else
			{
				// Uh-oh, the restart markers have been messed up.
				// Let the data source manager determine how to resync.
				if(!cinfo.src.resync_to_restart(cinfo, cinfo.marker.next_restart_num)) return false;
			}

			// Update next-restart state
			cinfo.marker.next_restart_num=(cinfo.marker.next_restart_num+1)&7;

			return true;
		}

		// This is the default resync_to_restart method for data source managers
		// to use if they don't have any better approach. Some data source managers
		// may be able to back up, or may have additional knowledge about the data
		// which permits a more intelligent recovery strategy; such managers would
		// presumably supply their own resync method.

		// read_restart_marker calls resync_to_restart if it finds a marker other than
		// the restart marker it was expecting. (This code is *not* used unless
		// a nonzero restart interval has been declared.) cinfo.unread_marker is
		// the marker code actually found (might be anything, except 0 or FF).
		// The desired restart marker number (0..7) is passed as a parameter.
		// This routine is supposed to apply whatever error recovery strategy seems
		// appropriate in order to position the input stream to the next data segment.
		// Note that cinfo.unread_marker is treated as a marker appearing before
		// the current data-source input point; usually it should be reset to zero
		// before returning.
		// Returns false if suspension is required.
		//
		// This implementation is substantially constrained by wanting to treat the
		// input as a data stream; this means we can't back up. Therefore, we have
		// only the following actions to work with:
		//	1.	Simply discard the marker and let the entropy decoder resume at next
		//		byte of file.
		//	2.	Read forward until we find another marker, discarding intervening
		//		data. (In theory we could look ahead within the current bufferload,
		//		without having to discard data if we don't find the desired marker.
		//		This idea is not implemented here, in part because it makes behavior
		//		dependent on buffer size and chance buffer-boundary positions.)
		//	3.	Leave the marker unread (by failing to zero cinfo.unread_marker).
		//		This will cause the entropy decoder to process an empty data segment,
		//		inserting dummy zeroes, and then we will reprocess the marker.
		//
		// #2 is appropriate if we think the desired marker lies ahead, while #3 is
		// appropriate if the found marker is a future restart marker (indicating
		// that we have missed the desired restart marker, probably because it got
		// corrupted).
		// We apply #2 or #3 if the found marker is a restart marker no more than
		// two counts behind or ahead of the expected one. We also apply #2 if the
		// found marker is not a legal JPEG marker code (it's certainly bogus data).
		// If the found marker is a restart marker more than 2 counts away, we do #1
		// (too much risk that the marker is erroneous; with luck we will be able to
		// resync at some future point).
		// For any valid non-restart JPEG marker, we apply #3. This keeps us from
		// overrunning the end of a scan. An implementation limited to single-scan
		// files might find it better to apply #2 for markers other than EOI, since
		// any other marker would have to be bogus data in that case.
		public static bool jpeg_resync_to_restart(jpeg_decompress cinfo, int desired)
		{
			int marker=cinfo.unread_marker;
			int action=1;

			// Always put up a warning.
			WARNMS2(cinfo, J_MESSAGE_CODE.JWRN_MUST_RESYNC, marker, desired);

			// Outer loop handles repeated decision after scanning forward.
			for(; ; )
			{
				if(marker<(int)JPEG_MARKER.M_SOF0) action=2; // invalid marker
				else if(marker<(int)JPEG_MARKER.M_RST0||marker>(int)JPEG_MARKER.M_RST7) action=3; // valid non-restart marker
				else
				{
					if(marker==((int)JPEG_MARKER.M_RST0+((desired+1)&7))||marker==((int)JPEG_MARKER.M_RST0+((desired+2)&7))) action=3; // one of the next two expected restarts
					else if(marker==((int)JPEG_MARKER.M_RST0+((desired-1)&7))||marker==((int)JPEG_MARKER.M_RST0+((desired-2)&7))) action=2; // a prior restart, so advance
					else action=1; // desired restart or too far away
				}
				TRACEMS2(cinfo, 4, J_MESSAGE_CODE.JTRC_RECOVERY_ACTION, marker, action);
				switch(action)
				{
					case 1:
						// Discard marker and let entropy decoder resume processing.
						cinfo.unread_marker=0;
						return true;
					case 2:
						// Scan to the next marker, and repeat the decision loop.
						if(!next_marker(cinfo)) return false;
						marker=cinfo.unread_marker;
						break;
					case 3:
						// Return without advancing past this marker.
						// Entropy decoder will be forced to process an empty segment.
						return true;
				}
			} // end loop
		}

		// Reset marker processing state to begin a fresh datastream.
		static void reset_marker_reader(jpeg_decompress cinfo)
		{
			my_marker_reader marker=(my_marker_reader)cinfo.marker;

			cinfo.comp_info=null;		// until allocated by get_sof
			cinfo.input_scan_number=0;	// no SOS seen yet
			cinfo.unread_marker=0;		// no pending marker
			marker.saw_SOI=false;		// set internal state too
			marker.saw_SOF=false;
			marker.discarded_bytes=0;
			marker.cur_marker=null;
		}

		// Initialize the marker reader module.
		// This is called only once, when the decompression object is created.
		public static void jinit_marker_reader(jpeg_decompress cinfo)
		{
			my_marker_reader marker=null;

			// Create subobject in pool
			try
			{
				marker=new my_marker_reader();
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}
			cinfo.marker=marker;

			// Initialize public method pointers
			marker.reset_marker_reader=reset_marker_reader;
			marker.read_markers=read_markers;
			marker.read_restart_marker=read_restart_marker;

			// Initialize COM/APPn processing.
			// By default, we examine and then discard APP0 and APP14,
			// but simply discard COM and all other APPn.
			marker.process_COM=skip_variable;
			marker.length_limit_COM=0;
			for(int i=0; i<16; i++)
			{
				marker.process_APPn[i]=skip_variable;
				marker.length_limit_APPn[i]=0;
			}
			marker.process_APPn[0]=get_interesting_appn;
#if SAVE_MARKERS_SUPPORTED
			marker.process_APPn[1]=save_marker;
			marker.length_limit_APPn[1]=65533;
#endif //SAVE_MARKERS_SUPPORTED
			marker.process_APPn[14]=get_interesting_appn;

			// Reset marker processing state
			reset_marker_reader(cinfo);
		}

#if SAVE_MARKERS_SUPPORTED
		// Control saving of COM and APPn markers into marker_list.
		public static void jpeg_save_markers(jpeg_decompress cinfo, int marker_code, uint length_limit)
		{
			my_marker_reader marker=(my_marker_reader)cinfo.marker;
			jpeg_marker_parser_method processor;

			// Choose processor routine to use.
			// APP0/APP14 have special requirements.
			if(length_limit!=0||marker_code==(int)JPEG_MARKER.M_APP1)
			{
				processor=save_marker;
				// If saving APP0/APP14, save at least enough for our internal use.
				if(marker_code==(int)JPEG_MARKER.M_APP0&&length_limit<APP0_DATA_LEN) length_limit=APP0_DATA_LEN;
				else if(marker_code==(int)JPEG_MARKER.M_APP1) length_limit=65533;
				else if(marker_code==(int)JPEG_MARKER.M_APP14&&length_limit<APP14_DATA_LEN) length_limit=APP14_DATA_LEN;
			}
			else
			{
				processor=skip_variable;
				// If discarding APP0/APP14, use our regular on-the-fly processor.
				if(marker_code==(int)JPEG_MARKER.M_APP0||marker_code==(int)JPEG_MARKER.M_APP14) processor=get_interesting_appn;
			}

			if(marker_code==(int)JPEG_MARKER.M_COM)
			{
				marker.process_COM=processor;
				marker.length_limit_COM=length_limit;
			}
			else if(marker_code>=(int)JPEG_MARKER.M_APP0&&marker_code<=(int)JPEG_MARKER.M_APP15)
			{
				marker.process_APPn[marker_code-(int)JPEG_MARKER.M_APP0]=processor;
				marker.length_limit_APPn[marker_code-(int)JPEG_MARKER.M_APP0]=length_limit;
			}
			else ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_UNKNOWN_MARKER, marker_code);
		}
#endif // SAVE_MARKERS_SUPPORTED

		// Install a special processing method for COM or APPn markers.
		public static void jpeg_set_marker_processor(jpeg_decompress cinfo, int marker_code, jpeg_marker_parser_method routine)
		{
			my_marker_reader marker=(my_marker_reader)cinfo.marker;

			if(marker_code==(int)JPEG_MARKER.M_COM) marker.process_COM=routine;
			else if(marker_code>=(int)JPEG_MARKER.M_APP0&&marker_code<=(int)JPEG_MARKER.M_APP15) marker.process_APPn[marker_code-(int)JPEG_MARKER.M_APP0]=routine;
			else ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_UNKNOWN_MARKER, marker_code);
		}
	}
}
