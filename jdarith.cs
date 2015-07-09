#if D_ARITH_CODING_SUPPORTED
// jdarith.cs
//
// Based on Arithmetric enconding and decoding for libjpeg version 6b - 28-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1997, Guido Vollbeding <guivol@esc.de>.
// This file is NOT part of the Independent JPEG Group's software
// for legal reasons.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains portable arithmetic entropy decoding routines for JPEG
// (implementing the ISO/IEC IS 10918-1 and CCITT Recommendation ITU-T T.81).
//
// Both sequential and progressive modes are supported in this single module.
//
// Suspension is not currently supported in this module.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Expanded entropy decoder object for arithmetic decoding.
		class arith_entropy_decoder : jpeg_lossy_d_codec
		{
			public int c;	// C register, base of coding interval + input bit buffer
			public int a;	// A register, normalized size of coding interval
			public int ct;	// bit shift counter, # of bits left in bit buffer part of C
			// init: ct = -16
			// run: ct = 0..7
			// error: ct = -1
			public int[] last_dc_val=new int[MAX_COMPS_IN_SCAN];	// last DC coef for each component
			public int[] dc_context=new int[MAX_COMPS_IN_SCAN];		// context index for DC conditioning

			public uint restarts_to_go;	// MCUs left in this restart interval

			// Pointers to statistics areas (these workspaces have image lifespan)
			public byte[][] dc_stats=new byte[NUM_ARITH_TBLS][];
			public byte[][] ac_stats=new byte[NUM_ARITH_TBLS][];
		}

		// Read next input byte; we do not support suspension in this module.
		static int get_byte_arith(jpeg_decompress cinfo)
		{
			jpeg_source_mgr src=cinfo.src;

			if(src.bytes_in_buffer==0)
			{
				if(!src.fill_input_buffer(cinfo)) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_CANT_SUSPEND);
			}
			src.bytes_in_buffer--;
			return src.input_bytes[src.next_input_byte++];
		}

		// The core arithmetic decoding routine (common in JPEG and JBIG).
		// This needs to go as fast as possible.
		// Machine-dependent optimization facilities
		// are not utilized in this portable implementation.
		// However, this code should be fairly efficient and
		// may be a good base for further optimizations anyway.
		//
		// Return value is 0 or 1 (binary decision).
		//
		// Note: I've changed the handling of the code base & bit
		// buffer register C compared to other implementations
		// based on the standards layout & procedures.
		// While it also contains both the actual base of the
		// coding interval (16 bits) and the next-bits buffer,
		// the cut-point between these two parts is floating
		// (instead of fixed) with the bit shift counter CT.
		// Thus, we also need only one (variable instead of
		// fixed size) shift for the LPS/MPS decision, and
		// we can get away with any renormalization update
		// of C (except for new data insertion, of course).
		//
		// I've also introduced a new scheme for accessing
		// the probability estimation state machine table,
		// derived from Markus Kuhn's JBIG implementation.
		static int arith_decode(jpeg_decompress cinfo, ref byte st)
		{
			arith_entropy_decoder e=(arith_entropy_decoder)cinfo.coef;

			// Renormalization & data input per section D.2.6
			while(e.a<0x8000)
			{
				e.ct--;
				if(e.ct<0)
				{
					// Need to fetch next data byte
					int data=0; // stuff zero data
					if(cinfo.unread_marker==0)
					{
						data=get_byte_arith(cinfo); // read next input byte
						if(data==0xFF)
						{ // zero stuff or marker code
							do data=get_byte_arith(cinfo);
							while(data==0xFF);		// swallow extra 0xFF bytes

							if(data==0) data=0xFF;	// discard stuffed zero byte
							else
							{
								// Note: Different from the Huffman decoder, hitting
								// a marker while processing the compressed data
								// segment is legal in arithmetic coding.
								// The convention is to supply zero data
								// then until decoding is complete.
								cinfo.unread_marker=data;
								data=0;
							}
						}
					}
					e.c=(e.c<<8)|data; // insert data into C register
					e.ct+=8;
					if(e.ct<0) // update bit shift counter
					{
						// Need more initial bytes
						e.ct++;
						if(e.ct==0)
						{
							// Got 2 initial bytes -> re-init A and exit loop
							e.a=0x8000; // => e.a = 0x10000 after loop exit
						}
					}
				}
				e.a<<=1;
			}

			// Fetch values from our compact representation of Table D.2:
			// Qe values and probability estimation state machine
			int sv=st;
			int qe=jaritab[sv&0x7F];			// => Qe_Value
			byte nl=(byte)(qe&0xFF); qe>>=8;	// Next_Index_LPS + Switch_MPS
			byte nm=(byte)(qe&0xFF); qe>>=8;	// Next_Index_MPS

			// Decode & estimation procedures per sections D.2.4 & D.2.5
			int temp=e.a-qe;
			e.a=temp;
			temp<<=e.ct;
			if(e.c>=temp)
			{
				e.c-=temp;
				// Conditional LPS (less probable symbol) exchange
				if(e.a<qe)
				{
					e.a=qe;
					st=(byte)((sv&0x80)^nm);	// Estimate_after_MPS
				}
				else
				{
					e.a=qe;
					st=(byte)((sv&0x80)^nl);	// Estimate_after_LPS
					sv^=0x80;					// Exchange LPS/MPS
				}
			}
			else if(e.a<0x8000)
			{
				// Conditional MPS (more probable symbol) exchange
				if(e.a<qe)
				{
					st=(byte)((sv&0x80)^nl);	// Estimate_after_LPS
					sv^=0x80;					// Exchange LPS/MPS
				}
				else
				{
					st=(byte)((sv&0x80)^nm);	// Estimate_after_MPS
				}
			}

			return sv>>7;
		}

		// Check for a restart marker & resynchronize decoder.
		static void process_restart_arith(jpeg_decompress cinfo)
		{
			arith_entropy_decoder entropy=(arith_entropy_decoder)cinfo.coef;

			// Advance past the RSTn marker
			if(!cinfo.marker.read_restart_marker(cinfo)) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_CANT_SUSPEND);

			for(int ci=0; ci<cinfo.comps_in_scan; ci++)
			{
				jpeg_component_info compptr=cinfo.cur_comp_info[ci];
				// Re-initialize statistics areas
				if(cinfo.process==J_CODEC_PROCESS.JPROC_SEQUENTIAL||(cinfo.Ss==0&&cinfo.Ah==0))
				{
					for(int i=0; i<DC_STAT_BINS; i++) entropy.dc_stats[compptr.dc_tbl_no][i]=0;

					// Reset DC predictions to 0
					entropy.last_dc_val[ci]=0;
					entropy.dc_context[ci]=0;
				}
				if(cinfo.process==J_CODEC_PROCESS.JPROC_SEQUENTIAL||cinfo.Ss!=0)
				{
					for(int i=0; i<AC_STAT_BINS; i++) entropy.ac_stats[compptr.ac_tbl_no][i]=0;
				}
			}

			// Reset arithmetic decoding variables
			entropy.c=0;
			entropy.a=0;
			entropy.ct=-16;	// force reading 2 initial bytes to fill C

			// Reset restart counter
			entropy.restarts_to_go=cinfo.restart_interval;
		}

		// Arithmetic MCU decoding.
		// Each of these routines decodes and returns one MCU's worth of
		// arithmetic-compressed coefficients.
		// The coefficients are reordered from zigzag order into natural array order,
		// but are not dequantized.
		//
		// The i'th block of the MCU is stored into the block pointed to by
		// MCU_data[i]. WE ASSUME THIS AREA IS INITIALLY ZEROED BY THE CALLER.

		// MCU decoding for DC initial scan (either spectral selection,
		// or first pass of successive approximation).
		static bool decode_mcu_DC_first_arith(jpeg_decompress cinfo, short[][] MCU_data)
		{
			arith_entropy_decoder entropy=(arith_entropy_decoder)cinfo.coef;

			// Process restart marker if needed
			if(cinfo.restart_interval!=0)
			{
				if(entropy.restarts_to_go==0) process_restart_arith(cinfo);
				entropy.restarts_to_go--;
			}

			if(entropy.ct==-1) return true;	// if error do nothing

			// Outer loop handles each block in the MCU
			for(int blkn=0; blkn<cinfo.blocks_in_MCU; blkn++)
			{
				short[] block=MCU_data[blkn];
				int ci=cinfo.MCU_membership[blkn];
				int tbl=cinfo.cur_comp_info[ci].dc_tbl_no;

				// Sections F.2.4.1 & F.1.4.4.1: Decoding of DC coefficients

				// Table F.4: Point to statistics bin S0 for DC coefficient coding
				byte[] st=entropy.dc_stats[tbl];
				int st_ind=entropy.dc_context[ci];

				// Figure F.19: Decode_DC_DIFF
				if(arith_decode(cinfo, ref st[st_ind])==0) entropy.dc_context[ci]=0;
				else
				{
					// Figure F.21: Decoding nonzero value v
					// Figure F.22: Decoding the sign of v
					int sign=arith_decode(cinfo, ref st[st_ind+1]);
					st_ind+=2;
					st_ind+=sign;
					// Figure F.23: Decoding the magnitude category of v
					int m=arith_decode(cinfo, ref st[st_ind]);
					if(m!=0)
					{
						st=entropy.dc_stats[tbl];
						st_ind=20;	// Table F.4: X1 = 20
						while(arith_decode(cinfo, ref st[st_ind])!=0)
						{
							if((m<<=1)==0x8000)
							{
								WARNMS(cinfo, J_MESSAGE_CODE.JWRN_ARITH_BAD_CODE);
								entropy.ct=-1;	// magnitude overflow
								return true;
							}
							st_ind+=1;
						}
					}
					// Section F.1.4.4.1.2: Establish dc_context conditioning category
					if(m<(int)((1<<cinfo.arith_dc_L[tbl])>>1))
						entropy.dc_context[ci]=0;			// zero diff category
					else if(m>(int)((1<<cinfo.arith_dc_U[tbl])>>1))
						entropy.dc_context[ci]=12+(sign*4);	// large diff category
					else entropy.dc_context[ci]=4+(sign*4);	// small diff category
					int v=m;

					// Figure F.24: Decoding the magnitude bit pattern of v
					st_ind+=14;
					while((m>>=1)!=0)
					{
						if(arith_decode(cinfo, ref st[st_ind])!=0) v|=m;
					}
					v+=1;
					if(sign!=0) v=-v;
					entropy.last_dc_val[ci]+=v;
				}

				// Scale and output the DC coefficient (assumes jpeg_natural_order[0]=0)
				block[0]=(short)(entropy.last_dc_val[ci]<<cinfo.Al);
			}

			return true;
		}

		// MCU decoding for AC initial scan (either spectral selection,
		// or first pass of successive approximation).
		static bool decode_mcu_AC_first_arith(jpeg_decompress cinfo, short[][] MCU_data)
		{
			arith_entropy_decoder entropy=(arith_entropy_decoder)cinfo.coef;

			// Process restart marker if needed
			if(cinfo.restart_interval!=0)
			{
				if(entropy.restarts_to_go==0) process_restart_arith(cinfo);
				entropy.restarts_to_go--;
			}

			if(entropy.ct==-1) return true;	// if error do nothing

			// There is always only one block per MCU
			short[] block=MCU_data[0];
			int tbl=cinfo.cur_comp_info[0].ac_tbl_no;

			// Sections F.2.4.2 & F.1.4.4.2: Decoding of AC coefficients

			// Figure F.20: Decode_AC_coefficients
			for(int k=cinfo.Ss; k<=cinfo.Se; k++)
			{
				byte[] st=entropy.ac_stats[tbl];
				int st_ind=3*(k-1);
				if(arith_decode(cinfo, ref st[st_ind])!=0) break; // EOB flag
				while(arith_decode(cinfo, ref st[st_ind+1])==0)
				{
					st_ind+=3; k++;
					if(k>cinfo.Se)
					{
						WARNMS(cinfo, J_MESSAGE_CODE.JWRN_ARITH_BAD_CODE);
						entropy.ct=-1; // spectral overflow
						return true;
					}
				}

				// Figure F.21: Decoding nonzero value v
				// Figure F.22: Decoding the sign of v
				entropy.ac_stats[tbl][245]=0;
				int sign=arith_decode(cinfo, ref entropy.ac_stats[tbl][245]);
				st_ind+=2;

				// Figure F.23: Decoding the magnitude category of v
				int m=arith_decode(cinfo, ref st[st_ind]);
				if(m!=0)
				{
					if(arith_decode(cinfo, ref st[st_ind])!=0)
					{
						m<<=1;
						st=entropy.ac_stats[tbl];
						st_ind=(k<=cinfo.arith_ac_K[tbl]?189:217);
						while(arith_decode(cinfo, ref st[st_ind])!=0)
						{
							m<<=1;
							if(m==0x8000)
							{
								WARNMS(cinfo, J_MESSAGE_CODE.JWRN_ARITH_BAD_CODE);
								entropy.ct=-1; // magnitude overflow
								return true;
							}
							st_ind+=1;
						}
					}
				}
				int v=m;

				// Figure F.24: Decoding the magnitude bit pattern of v
				st_ind+=14;
				while((m>>=1)!=0)
				{
					if(arith_decode(cinfo, ref st[st_ind])!=0) v|=m;
				}
				v+=1;
				if(sign!=0) v=-v;

				// Scale and output coefficient in natural (dezigzagged) order
				block[jpeg_natural_order[k]]=(short)(v<<cinfo.Al);
			}

			return true;
		}

		// MCU decoding for DC successive approximation refinement scan.
		static bool decode_mcu_DC_refine_arith(jpeg_decompress cinfo, short[][] MCU_data)
		{
			arith_entropy_decoder entropy=(arith_entropy_decoder)cinfo.coef;

			// Process restart marker if needed
			if(cinfo.restart_interval!=0)
			{
				if(entropy.restarts_to_go==0) process_restart_arith(cinfo);
				entropy.restarts_to_go--;
			}

			short p1=(short)(1<<cinfo.Al); // 1 in the bit position being coded

			// Outer loop handles each block in the MCU
			for(int blkn=0; blkn<cinfo.blocks_in_MCU; blkn++)
			{
				byte st=0;	// use fixed probability estimation
				// Encoded data is simply the next bit of the two's-complement DC value
				if(arith_decode(cinfo, ref st)!=0) MCU_data[blkn][0]|=p1;
			}

			return true;
		}

		// MCU decoding for AC successive approximation refinement scan.
		static bool decode_mcu_AC_refine_arith(jpeg_decompress cinfo, short[][] MCU_data)
		{
			arith_entropy_decoder entropy=(arith_entropy_decoder)cinfo.coef;

			// Process restart marker if needed
			if(cinfo.restart_interval!=0)
			{
				if(entropy.restarts_to_go==0) process_restart_arith(cinfo);
				entropy.restarts_to_go--;
			}

			if(entropy.ct==-1) return true;	// if error do nothing

			// There is always only one block per MCU
			short[] block=MCU_data[0];
			int tbl=cinfo.cur_comp_info[0].ac_tbl_no;

			short p1=(short)(1<<cinfo.Al);		// 1 in the bit position being coded
			short m1=(short)((-1)<<cinfo.Al);	// -1 in the bit position being coded

			// Establish EOBx (previous stage end-of-block) index
			int kex;
			for(kex=cinfo.Se+1; kex>1; kex--)
			{
				if(block[jpeg_natural_order[kex-1]]!=0) break;
			}

			for(int k=cinfo.Ss; k<=cinfo.Se; k++)
			{
				byte[] st=entropy.ac_stats[tbl];
				int st_ind=3*(k-1);
				if(k>=kex)
				{
					if(arith_decode(cinfo, ref st[st_ind])!=0) break;	// EOB flag
				}
				for(; ; )
				{
					int thiscoef=jpeg_natural_order[k];

					if(block[thiscoef]!=0)
					{ // previously nonzero coef
						if(arith_decode(cinfo, ref st[st_ind+2])!=0)
						{
							if(block[thiscoef]<0) block[thiscoef]+=m1;
							else block[thiscoef]+=p1;
						}
						break;
					}

					if(arith_decode(cinfo, ref st[st_ind+1])!=0)
					{ // newly nonzero coef
						entropy.ac_stats[tbl][245]=0;
						if(arith_decode(cinfo, ref entropy.ac_stats[tbl][245])!=0) block[thiscoef]=m1;
						else block[thiscoef]=p1;
						break;
					}
					st_ind+=3;

					k++;
					if(k>cinfo.Se)
					{
						WARNMS(cinfo, J_MESSAGE_CODE.JWRN_ARITH_BAD_CODE);
						entropy.ct=-1; // spectral overflow
						return true;
					}
				}
			}

			return true;
		}

		// Decode one MCU's worth of arithmetic-compressed coefficients.
		static bool decode_mcu_arith(jpeg_decompress cinfo, short[][] MCU_data)
		{
			arith_entropy_decoder entropy=(arith_entropy_decoder)cinfo.coef;

			// Process restart marker if needed
			if(cinfo.restart_interval!=0)
			{
				if(entropy.restarts_to_go==0) process_restart_arith(cinfo);
				entropy.restarts_to_go--;
			}

			if(entropy.ct==-1) return true;	// if error do nothing

			// Outer loop handles each block in the MCU
			for(int blkn=0; blkn<cinfo.blocks_in_MCU; blkn++)
			{
				short[] block=MCU_data[blkn];
				int ci=cinfo.MCU_membership[blkn];
				jpeg_component_info compptr=cinfo.cur_comp_info[ci];

				// Sections F.2.4.1 & F.1.4.4.1: Decoding of DC coefficients
				int tbl=compptr.dc_tbl_no;

				// Table F.4: Point to statistics bin S0 for DC coefficient coding
				byte[] st=entropy.dc_stats[tbl];
				int st_ind=entropy.dc_context[ci];

				// Figure F.19: Decode_DC_DIFF
				if(arith_decode(cinfo, ref st[st_ind])==0) entropy.dc_context[ci]=0;
				else
				{
					// Figure F.21: Decoding nonzero value v
					// Figure F.22: Decoding the sign of v
					int sign=arith_decode(cinfo, ref st[st_ind+1]);
					st_ind+=2;
					st_ind+=sign;

					// Figure F.23: Decoding the magnitude category of v
					int m=arith_decode(cinfo, ref st[st_ind]);
					if(m!=0)
					{
						st=entropy.dc_stats[tbl];
						st_ind=20; // Table F.4: X1 = 20
						while(arith_decode(cinfo, ref st[st_ind])!=0)
						{
							if((m<<=1)==0x8000)
							{
								WARNMS(cinfo, J_MESSAGE_CODE.JWRN_ARITH_BAD_CODE);
								entropy.ct=-1; // magnitude overflow
								return true;
							}
							st_ind+=1;
						}
					}

					// Section F.1.4.4.1.2: Establish dc_context conditioning category
					if(m<(int)((1<<cinfo.arith_dc_L[tbl])>>1)) entropy.dc_context[ci]=0; // zero diff category
					else if(m>(int)((1<<cinfo.arith_dc_U[tbl])>>1)) entropy.dc_context[ci]=12+(sign*4); // large diff category
					else entropy.dc_context[ci]=4+(sign*4); // small diff category
					int v=m;

					// Figure F.24: Decoding the magnitude bit pattern of v
					st_ind+=14;
					while((m>>=1)!=0)
					{
						if(arith_decode(cinfo, ref st[st_ind])!=0) v|=m;
					}
					v+=1;
					if(sign!=0) v=-v;
					entropy.last_dc_val[ci]+=v;
				}

				block[0]=(short)entropy.last_dc_val[ci];

				// Sections F.2.4.2 & F.1.4.4.2: Decoding of AC coefficients
				tbl=compptr.ac_tbl_no;

				// Figure F.20: Decode_AC_coefficients
				for(int k=1; k<DCTSIZE2; k++)
				{
					st=entropy.ac_stats[tbl];
					st_ind=3*(k-1);
					if(arith_decode(cinfo, ref st[st_ind])!=0) break; // EOB flag
					while(arith_decode(cinfo, ref st[st_ind+1])==0)
					{
						st_ind+=3; k++;
						if(k>=DCTSIZE2)
						{
							WARNMS(cinfo, J_MESSAGE_CODE.JWRN_ARITH_BAD_CODE);
							entropy.ct=-1; // spectral overflow
							return true;
						}
					}

					// Figure F.21: Decoding nonzero value v
					// Figure F.22: Decoding the sign of v
					entropy.ac_stats[tbl][245]=0;
					int sign=arith_decode(cinfo, ref entropy.ac_stats[tbl][245]);
					st_ind+=2;

					// Figure F.23: Decoding the magnitude category of v
					int m=arith_decode(cinfo, ref st[st_ind]);
					if(m!=0)
					{
						if(arith_decode(cinfo, ref st[st_ind])!=0)
						{
							m<<=1;
							st=entropy.ac_stats[tbl];
							st_ind=(k<=cinfo.arith_ac_K[tbl]?189:217);
							while(arith_decode(cinfo, ref st[st_ind])!=0)
							{
								m<<=1;
								if(m==0x8000)
								{
									WARNMS(cinfo, J_MESSAGE_CODE.JWRN_ARITH_BAD_CODE);
									entropy.ct=-1; // magnitude overflow
									return true;
								}
								st_ind+=1;
							}
						}
					}
					int v=m;

					// Figure F.24: Decoding the magnitude bit pattern of v
					st_ind+=14;
					while((m>>=1)!=0)
					{
						if(arith_decode(cinfo, ref st[st_ind])!=0) v|=m;
					}
					v+=1;
					if(sign!=0) v=-v;
					block[jpeg_natural_order[k]]=(short)v;
				}
			}

			return true;
		}

		// Initialize for an arithmetic-compressed scan.
		static void start_pass_d_arith(jpeg_decompress cinfo)
		{
			arith_entropy_decoder entropy=(arith_entropy_decoder)cinfo.coef;

			if(cinfo.process==J_CODEC_PROCESS.JPROC_PROGRESSIVE)
			{
				// Validate progressive scan parameters
				bool err=false;
				if(cinfo.Ss==0)
				{
					if(cinfo.Se!=0) err=true;
				}
				else
				{
					// need not check Ss/Se < 0 since they came from unsigned bytes
					if(cinfo.Se<cinfo.Ss||cinfo.Se>=DCTSIZE2) err=true;
					// AC scans may have only one component
					if(cinfo.comps_in_scan!=1) err=true;
				}
				if(cinfo.Ah!=0)
				{
					// Successive approximation refinement scan: must have Al = Ah-1.
					if(cinfo.Ah-1!=cinfo.Al) err=true;
				}

				if(cinfo.Al>13||err)
				{ // need not check for < 0
					ERREXIT4(cinfo, J_MESSAGE_CODE.JERR_BAD_PROGRESSION, cinfo.Ss, cinfo.Se, cinfo.Ah, cinfo.Al);
				}

				// Update progression status, and verify that scan order is legal.
				// Note that inter-scan inconsistencies are treated as warnings
				// not fatal errors ... not clear if this is right way to behave.
				for(int ci=0; ci<cinfo.comps_in_scan; ci++)
				{
					int coefi, cindex=cinfo.cur_comp_info[ci].component_index;
					int[] coef_bits=cinfo.coef_bits[cindex];
					if(cinfo.Ss!=0&&coef_bits[0]<0) WARNMS2(cinfo, J_MESSAGE_CODE.JWRN_BOGUS_PROGRESSION, cindex, 0); // AC without prior DC scan
					for(coefi=cinfo.Ss; coefi<=cinfo.Se; coefi++)
					{
						int expected=(coef_bits[coefi]<0)?0:coef_bits[coefi];
						if(cinfo.Ah!=expected) WARNMS2(cinfo, J_MESSAGE_CODE.JWRN_BOGUS_PROGRESSION, cindex, coefi);
						coef_bits[coefi]=cinfo.Al;
					}
				}
				// Select MCU decoding routine
				if(cinfo.Ah==0)
				{
					if(cinfo.Ss==0) entropy.entropy_decode_mcu=decode_mcu_DC_first_arith;
					else entropy.entropy_decode_mcu=decode_mcu_AC_first_arith;
				}
				else
				{
					if(cinfo.Ss==0) entropy.entropy_decode_mcu=decode_mcu_DC_refine_arith;
					else entropy.entropy_decode_mcu=decode_mcu_AC_refine_arith;
				}
			}
			else
			{
				// Check that the scan parameters Ss, Se, Ah/Al are OK for sequential JPEG.
				// This ought to be an error condition, but we make it a warning because
				// there are some baseline files out there with all zeroes in these bytes.
				if(cinfo.Ss!=0||cinfo.Se!=DCTSIZE2-1||cinfo.Ah!=0||cinfo.Al!=0) WARNMS(cinfo, J_MESSAGE_CODE.JWRN_NOT_SEQUENTIAL);

				// Select MCU decoding routine
				entropy.entropy_decode_mcu=decode_mcu_arith;
			}

			for(int ci=0; ci<cinfo.comps_in_scan; ci++)
			{
				jpeg_component_info compptr=cinfo.cur_comp_info[ci];

				// Allocate & initialize requested statistics areas
				if(cinfo.process==J_CODEC_PROCESS.JPROC_SEQUENTIAL||(cinfo.Ss==0&&cinfo.Ah==0))
				{
					int tbl=compptr.dc_tbl_no;
					if(tbl<0||tbl>=NUM_ARITH_TBLS) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_NO_ARITH_TABLE, tbl);
					if(entropy.dc_stats[tbl]==null)
					{
						try
						{
							entropy.dc_stats[tbl]=new byte[DC_STAT_BINS];
						}
						catch
						{
							ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
						}
					}
					else
					{
						for(int i=0; i<DC_STAT_BINS; i++) entropy.dc_stats[tbl][i]=0;
					}

					// Initialize DC predictions to 0
					entropy.last_dc_val[ci]=0;
					entropy.dc_context[ci]=0;
				}
				if(cinfo.process==J_CODEC_PROCESS.JPROC_SEQUENTIAL||cinfo.Ss!=0)
				{
					int tbl=compptr.ac_tbl_no;
					if(tbl<0||tbl>=NUM_ARITH_TBLS)
						ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_NO_ARITH_TABLE, tbl);
					if(entropy.ac_stats[tbl]==null)
					{
						try
						{
							entropy.ac_stats[tbl]=new byte[AC_STAT_BINS];
						}
						catch
						{
							ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
						}
					}
					else
					{
						for(int i=0; i<AC_STAT_BINS; i++) entropy.ac_stats[tbl][i]=0;
					}
				}
			}

			// Initialize arithmetic decoding variables
			entropy.c=0;
			entropy.a=0;
			entropy.ct=-16;	// force reading 2 initial bytes to fill C

			// Initialize restart counter
			entropy.restarts_to_go=cinfo.restart_interval;
		}

		// Module initialization routine for arithmetic entropy decoding.
		static void jinit_arith_decoder(jpeg_decompress cinfo)
		{
			arith_entropy_decoder entropy=cinfo.coef as arith_entropy_decoder;

			if(entropy==null)
			{
				try
				{
					entropy=new arith_entropy_decoder();
				}
				catch
				{
					ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
				}
				cinfo.coef=entropy;
			}
			entropy.entropy_start_pass=start_pass_d_arith;

			// Mark tables unallocated
			for(int i=0; i<NUM_ARITH_TBLS; i++)
			{
				entropy.dc_stats[i]=null;
				entropy.ac_stats[i]=null;
			}

			if(cinfo.process==J_CODEC_PROCESS.JPROC_PROGRESSIVE)
			{
				// Create progression status table
				cinfo.coef_bits=new int[cinfo.num_components][];

				for(int ci=0; ci<cinfo.num_components; ci++)
				{
					int[] coef_bits=new int[DCTSIZE2];
					cinfo.coef_bits[ci]=coef_bits;

					for(int i=0; i<DCTSIZE2; i++) coef_bits[i]=-1;
				}
			}
		}
	}
}
#endif