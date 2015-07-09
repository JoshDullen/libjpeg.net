#if C_ARITH_CODING_SUPPORTED
// jcarith.cs
//
// Based on Arithmetric enconding and decoding for libjpeg version 6b - 28-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1997, Guido Vollbeding <guivol@esc.de>.
// This file is NOT part of the Independent JPEG Group's software
// for legal reasons.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains portable arithmetic entropy encoding routines for JPEG
// (implementing the ISO/IEC IS 10918-1 and CCITT Recommendation ITU-T T.81).
//
// Both sequential and progressive modes are supported in this single module.
//
// Suspension is not currently supported in this module.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Expanded entropy encoder object for arithmetic encoding.
		class arith_entropy_encoder : jpeg_lossy_c_codec
		{
			public int c;		// C register, base of coding interval, layout as in sec. D.1.3
			public int a;		// A register, normalized size of coding interval
			public int sc;		// counter for stacked 0xFF values which might overflow
			public int zc;		// counter for pending 0x00 output values which might
			// be discarded at the end ("Pacman" termination)
			public int ct;		// bit shift counter, determines when next byte will be written
			public int buffer;	// buffer for most recent output byte != 0xFF

			public int[] last_dc_val=new int[MAX_COMPS_IN_SCAN];	// last DC coef for each component
			public int[] dc_context=new int[MAX_COMPS_IN_SCAN];	// context index for DC conditioning

			public uint restarts_to_go;	// MCUs left in this restart interval
			public int next_restart_num;	// next restart number to write (0-7)

			// Pointers to statistics areas (these workspaces have image lifespan)
			public byte[][] dc_stats=new byte[NUM_ARITH_TBLS][];
			public byte[][] ac_stats=new byte[NUM_ARITH_TBLS][];
		}

		// NOTE: Uncomment the following #define if you want to use the
		// given formula for calculating the AC conditioning parameter Kx
		// for spectral selection progressive coding in section G.1.3.2
		// of the spec (Kx = Kmin + SRL (8 + Se - Kmin) 4).
		// Although the spec and P&M authors claim that this "has proven
		// to give good results for 8 bit precision samples", I'm not
		// convinced yet that this is really beneficial.
		// Early tests gave only very marginal compression enhancements
		// (a few - around 5 or so - bytes even for very large files),
		// which would turn out rather negative if we'd suppress the
		// DAC (Define Arithmetic Conditioning) marker segments for
		// the default parameters in the future.
		// Note that currently the marker writing module emits 12-byte
		// DAC segments for a full-component scan in a color image.
		// This is not worth worrying about IMHO. However, since the
		// spec defines the default values to be used if the tables
		// are omitted (unlike Huffman tables, which are required
		// anyway), one might optimize this behaviour in the future,
		// and then it would be disadvantageous to use custom tables if
		// they don't provide sufficient gain to exceed the DAC size.
		//
		// On the other hand, I'd consider it as a reasonable result
		// that the conditioning has no significant influence on the
		// compression performance. This means that the basic
		// statistical model is already rather stable.
		//
		// Thus, at the moment, we use the default conditioning values
		// anyway, and do not use the custom formula.
		//#define CALCULATE_SPECTRAL_CONDITIONING

		// Finish up at the end of an arithmetic-compressed scan.
		static void finish_pass_c_arith(jpeg_compress cinfo)
		{
			arith_entropy_encoder e=(arith_entropy_encoder)cinfo.coef;

			// Section D.1.8: Termination of encoding

			// Find the e.c in the coding interval with the largest
			// number of trailing zero bits
			int temp=(int)((uint)(e.a-1+e.c)&(uint)0xFFFF0000);
			if(temp<e.c) e.c=temp+0x8000;
			else e.c=temp;

			// Send remaining bytes to output
			e.c<<=e.ct;
			if(((uint)e.c&0xF8000000)!=0)
			{
				// One final overflow has to be handled
				if(e.buffer>=0)
				{
					if(e.zc!=0)
					{
						do emit_byte(cinfo, 0x00);
						while((--e.zc)!=0);
					}
					emit_byte(cinfo, e.buffer+1);
					if((e.buffer+1)==0xFF) emit_byte(cinfo, 0x00);
				}
				e.zc+=e.sc;	// carry-over converts stacked 0xFF bytes to 0x00
				e.sc=0;
			}
			else
			{
				if(e.buffer==0) ++e.zc;
				else if(e.buffer>=0)
				{
					if(e.zc!=0)
					{
						do emit_byte(cinfo, 0x00);
						while((--e.zc)!=0);
					}
					emit_byte(cinfo, e.buffer);
				}
				if(e.sc!=0)
				{
					if(e.zc!=0)
					{
						do emit_byte(cinfo, 0x00);
						while((--e.zc)!=0);
					}

					do
					{
						emit_byte(cinfo, 0xFF);
						emit_byte(cinfo, 0x00);
					} while((--e.sc)!=0);
				}
			}

			// Output final bytes only if they are not 0x00
			if((e.c&0x7FFF800)!=0)
			{
				if(e.zc!=0)	// output final pending zero bytes
				{
					do emit_byte(cinfo, 0x00);
					while((--e.zc)!=0);
				}
				emit_byte(cinfo, (e.c>>19)&0xFF);
				if(((e.c>>19)&0xFF)==0xFF) emit_byte(cinfo, 0x00);
				if((e.c&0x7F800)!=0)
				{
					emit_byte(cinfo, (e.c>>11)&0xFF);
					if(((e.c>>11)&0xFF)==0xFF) emit_byte(cinfo, 0x00);
				}
			}
		}

		// The core arithmetic encoding routine (common in JPEG and JBIG).
		// This needs to go as fast as possible.
		// Machine-dependent optimization facilities
		// are not utilized in this portable implementation.
		// However, this code should be fairly efficient and
		// may be a good base for further optimizations anyway.
		//
		// Parameter 'val' to be encoded may be 0 or 1 (binary decision).
		//
		// Note: I've added full "Pacman" termination support to the
		// byte output routines, which is equivalent to the optional
		// Discard_final_zeros procedure (Figure D.15) in the spec.
		// Thus, we always produce the shortest possible output
		// stream compliant to the spec (no trailing zero bytes,
		// except for FF stuffing).
		//
		// I've also introduced a new scheme for accessing
		// the probability estimation state machine table,
		// derived from Markus Kuhn's JBIG implementation.
		static void arith_encode(jpeg_compress cinfo, ref byte st, int val)
		{
			arith_entropy_encoder e=(arith_entropy_encoder)cinfo.coef;

			// Fetch values from our compact representation of Table D.2:
			// Qe values and probability estimation state machine
			int sv=st;
			int qe=jaritab[sv&0x7F];			// => Qe_Value
			byte nl=(byte)(qe&0xFF); qe>>=8;	// Next_Index_LPS + Switch_MPS
			byte nm=(byte)(qe&0xFF); qe>>=8;	// Next_Index_MPS

			// Encode & estimation procedures per sections D.1.4 & D.1.5
			e.a-=qe;
			if(val!=(sv>>7))
			{
				// Encode the less probable symbol
				if(e.a>=qe)
				{
					// If the interval size (qe) for the less probable symbol (LPS)
					// is larger than the interval size for the MPS, then exchange
					// the two symbols for coding efficiency, otherwise code the LPS
					// as usual:
					e.c+=e.a;
					e.a=qe;
				}
				st=(byte)((sv&0x80)^nl);	// Estimate_after_LPS
			}
			else
			{
				// Encode the more probable symbol
				if(e.a>=0x8000) return;	// A >= 0x8000 -> ready, no renormalization required
				if(e.a<qe)
				{
					// If the interval size (qe) for the less probable symbol (LPS)
					// is larger than the interval size for the MPS, then exchange
					// the two symbols for coding efficiency:
					e.c+=e.a;
					e.a=qe;
				}
				st=(byte)((sv&0x80)^nm);	// Estimate_after_MPS
			}

			// Renormalization & data output per section D.1.6
			do
			{
				e.a<<=1;
				e.c<<=1;
				e.ct--;
				if(e.ct==0)
				{
					// Another byte is ready for output
					int temp=e.c>>19;
					if(temp>0xFF)
					{
						// Handle overflow over all stacked 0xFF bytes
						if(e.buffer>=0)
						{
							if(e.zc!=0)
							{
								do emit_byte(cinfo, 0x00);
								while((--e.zc)!=0);
							}
							emit_byte(cinfo, e.buffer+1);
							if(e.buffer+1==0xFF) emit_byte(cinfo, 0x00);
						}
						e.zc+=e.sc;	// carry-over converts stacked 0xFF bytes to 0x00
						e.sc=0;
						// Note: The 3 spacer bits in the C register guarantee
						// that the new buffer byte can't be 0xFF here
						// (see page 160 in the P&M JPEG book).
						e.buffer=temp&0xFF;	// new output byte, might overflow later
					}
					else if(temp==0xFF)
					{
						e.sc++;	// stack 0xFF byte (which might overflow later)
					}
					else
					{
						// Output all stacked 0xFF bytes, they will not overflow any more
						if(e.buffer==0) e.zc++;
						else if(e.buffer>=0)
						{
							if(e.zc!=0)
							{
								do emit_byte(cinfo, 0x00);
								while((--e.zc)!=0);
							}
							emit_byte(cinfo, e.buffer);
						}
						if(e.sc!=0)
						{
							if(e.zc!=0)
							{
								do emit_byte(cinfo, 0x00);
								while((--e.zc)!=0);
							}
							do
							{
								emit_byte(cinfo, 0xFF);
								emit_byte(cinfo, 0x00);
							} while((--e.sc)!=0);
						}
						e.buffer=temp&0xFF;	// new output byte (can still overflow)
					}
					e.c&=0x7FFFF;
					e.ct+=8;
				}
			} while(e.a<0x8000);
		}

		// Emit a restart marker & resynchronize predictions.
		static void emit_restart_arith(jpeg_compress cinfo, int restart_num)
		{
			arith_entropy_encoder entropy=(arith_entropy_encoder)cinfo.coef;

			finish_pass_c_arith(cinfo);

			emit_byte(cinfo, 0xFF);
			emit_byte(cinfo, JPEG_RST0+restart_num);

			for(int ci=0; ci<cinfo.comps_in_scan; ci++)
			{
				jpeg_component_info compptr=cinfo.cur_comp_info[ci];

				// Re-initialize statistics areas
				if(cinfo.process!=J_CODEC_PROCESS.JPROC_PROGRESSIVE||(cinfo.Ss==0&&cinfo.Ah==0))
				{
					for(int i=0; i<DC_STAT_BINS; i++) entropy.dc_stats[compptr.dc_tbl_no][i]=0;
					// Reset DC predictions to 0
					entropy.last_dc_val[ci]=0;
					entropy.dc_context[ci]=0;
				}

				if(cinfo.process!=J_CODEC_PROCESS.JPROC_PROGRESSIVE||cinfo.Ss!=0)
					for(int i=0; i<AC_STAT_BINS; i++) entropy.ac_stats[compptr.ac_tbl_no][i]=0;
			}

			// Reset arithmetic encoding variables
			entropy.c=0;
			entropy.a=0x10000;
			entropy.sc=0;
			entropy.zc=0;
			entropy.ct=11;
			entropy.buffer=-1;	// empty
		}

		// MCU encoding for DC initial scan (either spectral selection,
		// or first pass of successive approximation).
		static bool encode_mcu_DC_first_arith(jpeg_compress cinfo, short[][] MCU_data)
		{
			arith_entropy_encoder entropy=(arith_entropy_encoder)cinfo.coef;

			// Emit restart marker if needed
			if(cinfo.restart_interval!=0)
			{
				if(entropy.restarts_to_go==0)
				{
					emit_restart_arith(cinfo, entropy.next_restart_num);
					entropy.restarts_to_go=cinfo.restart_interval;
					entropy.next_restart_num++;
					entropy.next_restart_num&=7;
				}
				entropy.restarts_to_go--;
			}

			// Encode the MCU data blocks
			for(int blkn=0; blkn<cinfo.block_in_MCU; blkn++)
			{
				short[] block=MCU_data[blkn];
				int ci=cinfo.MCU_membership[blkn];
				int tbl=cinfo.cur_comp_info[ci].dc_tbl_no;

				// Compute the DC value after the required point transform by Al.
				// This is simply an arithmetic right shift.
				int m=(int)(block[0])>>cinfo.Al;

				// Sections F.1.4.1 & F.1.4.4.1: Encoding of DC coefficients

				// Table F.4: Point to statistics bin S0 for DC coefficient coding
				byte[] st=entropy.dc_stats[tbl];
				int st_ind=entropy.dc_context[ci];

				// Figure F.4: Encode_DC_DIFF
				int v=m-entropy.last_dc_val[ci];
				if(v==0)
				{
					arith_encode(cinfo, ref st[st_ind], 0);
					entropy.dc_context[ci]=0;	// zero diff category
				}
				else
				{
					entropy.last_dc_val[ci]=m;
					arith_encode(cinfo, ref st[st_ind], 1);

					// Figure F.6: Encoding nonzero value v
					// Figure F.7: Encoding the sign of v
					if(v>0)
					{
						arith_encode(cinfo, ref st[st_ind+1], 0);	// Table F.4: SS = S0 + 1
						st_ind+=2;									// Table F.4: SP = S0 + 2
						entropy.dc_context[ci]=4;					// small positive diff category
					}
					else
					{
						v=-v;
						arith_encode(cinfo, ref st[st_ind+1], 1);	// Table F.4: SS = S0 + 1
						st_ind+=3;									// Table F.4: SN = S0 + 3
						entropy.dc_context[ci]=8;					// small negative diff category
					}

					// Figure F.8: Encoding the magnitude category of v
					m=0;
					v--;
					if(v!=0)
					{
						arith_encode(cinfo, ref st[st_ind], 1);
						m=1;
						int v2=v;
						st=entropy.dc_stats[tbl];
						st_ind=20;					// Table F.4: X1 = 20
						while((v2>>=1)!=0)
						{
							arith_encode(cinfo, ref st[st_ind], 1);
							m<<=1;
							st_ind+=1;
						}
					}
					arith_encode(cinfo, ref st[st_ind], 0);

					// Section F.1.4.4.1.2: Establish dc_context conditioning category
					if(m<(int)((1<<cinfo.arith_dc_L[tbl])>>1)) entropy.dc_context[ci]=0;		// zero diff category
					else if(m>(int)((1<<cinfo.arith_dc_U[tbl])>>1)) entropy.dc_context[ci]+=8;	// large diff category

					// Figure F.9: Encoding the magnitude bit pattern of v
					st_ind+=14;
					while((m>>=1)!=0) arith_encode(cinfo, ref st[st_ind], ((m&v)!=0)?1:0);
				}
			}

			return true;
		}

		// MCU encoding for AC initial scan (either spectral selection,
		// or first pass of successive approximation).
		static bool encode_mcu_AC_first_arith(jpeg_compress cinfo, short[][] MCU_data)
		{
			arith_entropy_encoder entropy=(arith_entropy_encoder)cinfo.coef;

			// Emit restart marker if needed
			if(cinfo.restart_interval!=0)
			{
				if(entropy.restarts_to_go==0)
				{
					emit_restart_arith(cinfo, entropy.next_restart_num);
					entropy.restarts_to_go=cinfo.restart_interval;
					entropy.next_restart_num++;
					entropy.next_restart_num&=7;
				}
				entropy.restarts_to_go--;
			}

			// Encode the MCU data block
			short[] block=MCU_data[0];
			int tbl=cinfo.cur_comp_info[0].ac_tbl_no;

			// Sections F.1.4.2 & F.1.4.4.2: Encoding of AC coefficients
			int k, ke, v;

			// Establish EOB (end-of-block) index
			for(ke=cinfo.Se+1; ke>1; ke--)
			{
				// We must apply the point transform by Al. For AC coefficients this
				// is an integer division with rounding towards 0. To do this portably
				// in C#, we shift after obtaining the absolute value.
				v=block[jpeg_natural_order[ke-1]];
				if(v>=0)
				{
					v>>=cinfo.Al;
					if(v!=0) break;
				}
				else
				{
					v=-v;
					v>>=cinfo.Al;
					if(v!=0) break;
				}
			}

			// Figure F.5: Encode_AC_Coefficients
			for(k=cinfo.Ss; k<ke; k++)
			{
				byte[] st=entropy.ac_stats[tbl];
				int st_ind=3*(k-1);
				arith_encode(cinfo, ref st[st_ind], 0);		// EOB decision
				entropy.ac_stats[tbl][245]=0;
				for(; ; )
				{
					v=block[jpeg_natural_order[k]];
					if(v>=0)
					{
						v>>=cinfo.Al;
						if(v!=0)
						{
							arith_encode(cinfo, ref st[st_ind+1], 1);
							arith_encode(cinfo, ref entropy.ac_stats[tbl][245], 0);
							break;
						}
					}
					else
					{
						v=-v;
						v>>=cinfo.Al;
						if(v!=0)
						{
							arith_encode(cinfo, ref st[st_ind+1], 1);
							arith_encode(cinfo, ref entropy.ac_stats[tbl][245], 1);
							break;
						}
					}
					arith_encode(cinfo, ref st[st_ind+1], 0); st_ind+=3; k++;
				}
				st_ind+=2;

				// Figure F.8: Encoding the magnitude category of v
				int m=0;
				v--;
				if(v!=0)
				{
					arith_encode(cinfo, ref st[st_ind], 1);
					m=1;
					int v2=v;
					v2>>=1;
					if(v2!=0)
					{
						arith_encode(cinfo, ref st[st_ind], 1);
						m<<=1;
						st=entropy.ac_stats[tbl];
						st_ind=(k<=cinfo.arith_ac_K[tbl]?189:217);
						while((v2>>=1)!=0)
						{
							arith_encode(cinfo, ref st[st_ind], 1);
							m<<=1;
							st_ind+=1;
						}
					}
				}
				arith_encode(cinfo, ref st[st_ind], 0);

				// Figure F.9: Encoding the magnitude bit pattern of v
				st_ind+=14;
				while((m>>=1)!=0) arith_encode(cinfo, ref st[st_ind], ((m&v)!=0)?1:0);
			}

			// Encode EOB decision only if k <= cinfo.Se
			if(k<=cinfo.Se)
			{
				byte[] st=entropy.ac_stats[tbl];
				int st_ind=3*(k-1);
				arith_encode(cinfo, ref st[st_ind], 1);
			}

			return true;
		}

		// MCU encoding for DC successive approximation refinement scan.
		static bool encode_mcu_DC_refine_arith(jpeg_compress cinfo, short[][] MCU_data)
		{
			arith_entropy_encoder entropy=(arith_entropy_encoder)cinfo.coef;

			// Emit restart marker if needed
			if(cinfo.restart_interval!=0)
			{
				if(entropy.restarts_to_go==0)
				{
					emit_restart_arith(cinfo, entropy.next_restart_num);
					entropy.restarts_to_go=cinfo.restart_interval;
					entropy.next_restart_num++;
					entropy.next_restart_num&=7;
				}
				entropy.restarts_to_go--;
			}

			int Al=cinfo.Al;

			// Encode the MCU data blocks
			for(int blkn=0; blkn<cinfo.block_in_MCU; blkn++)
			{
				byte st=0;	// use fixed probability estimation
				// We simply emit the Al'th bit of the DC coefficient value.
				arith_encode(cinfo, ref st, (MCU_data[blkn][0]>>Al)&1);
			}

			return true;
		}

		// MCU encoding for AC successive approximation refinement scan.
		static bool encode_mcu_AC_refine_arith(jpeg_compress cinfo, short[][] MCU_data)
		{
			arith_entropy_encoder entropy=(arith_entropy_encoder)cinfo.coef;

			// Emit restart marker if needed
			if(cinfo.restart_interval!=0)
			{
				if(entropy.restarts_to_go==0)
				{
					emit_restart_arith(cinfo, entropy.next_restart_num);
					entropy.restarts_to_go=cinfo.restart_interval;
					entropy.next_restart_num++;
					entropy.next_restart_num&=7;
				}
				entropy.restarts_to_go--;
			}

			// Encode the MCU data block
			short[] block=MCU_data[0];
			int tbl=cinfo.cur_comp_info[0].ac_tbl_no;

			// Section G.1.3.3: Encoding of AC coefficients
			int k, ke, kex, v;

			// Establish EOB (end-of-block) index
			for(ke=cinfo.Se+1; ke>1; ke--)
			{
				// We must apply the point transform by Al. For AC coefficients this
				// is an integer division with rounding towards 0. To do this portably
				// in C#, we shift after obtaining the absolute value.
				v=block[jpeg_natural_order[ke-1]];
				if(v>=0)
				{
					v>>=cinfo.Al;
					if(v!=0) break;
				}
				else
				{
					v=-v;
					v>>=cinfo.Al;
					if(v!=0) break;
				}
			}

			// Establish EOBx (previous stage end-of-block) index
			for(kex=ke; kex>1; kex--)
			{
				v=block[jpeg_natural_order[kex-1]];
				if(v>=0)
				{
					v>>=cinfo.Ah;
					if(v!=0) break;
				}
				else
				{
					v=-v;
					v>>=cinfo.Ah;
					if(v!=0) break;
				}
			}

			// Figure G.10: Encode_AC_Coefficients_SA
			for(k=cinfo.Ss; k<ke; k++)
			{
				byte[] st=entropy.ac_stats[tbl];
				int st_ind=3*(k-1);
				if(k>=kex) arith_encode(cinfo, ref st[st_ind], 0);	// EOB decision
				entropy.ac_stats[tbl][245]=0;
				for(; ; )
				{
					v=block[jpeg_natural_order[k]];
					if(v>=0)
					{
						v>>=cinfo.Al;
						if(v!=0)
						{
							if((v>>1)!=0) arith_encode(cinfo, ref st[st_ind+2], (v&1)); // previously nonzero coef
							else
							{ // newly nonzero coef
								arith_encode(cinfo, ref st[st_ind+1], 1);
								arith_encode(cinfo, ref entropy.ac_stats[tbl][245], 0);
							}
							break;
						}
					}
					else
					{
						v=-v;
						v>>=cinfo.Al;
						if(v!=0)
						{
							if((v>>1)!=0) arith_encode(cinfo, ref st[st_ind+2], (v&1)); // previously nonzero coef
							else
							{ // newly nonzero coef
								arith_encode(cinfo, ref st[st_ind+1], 1);
								arith_encode(cinfo, ref entropy.ac_stats[tbl][245], 1);
							}
							break;
						}
					}
					arith_encode(cinfo, ref st[st_ind+1], 0); st_ind+=3; k++;
				}
			}

			// Encode EOB decision only if k <= cinfo.Se
			if(k<=cinfo.Se)
			{
				byte[] st=entropy.ac_stats[tbl];
				int st_ind=3*(k-1);
				arith_encode(cinfo, ref st[st_ind], 1);
			}

			return true;
		}

		// Encode and output one MCU's worth of arithmetic-compressed coefficients.
		static bool encode_mcu_arith(jpeg_compress cinfo, short[][] MCU_data)
		{
			arith_entropy_encoder entropy=(arith_entropy_encoder)cinfo.coef;

			// Emit restart marker if needed
			if(cinfo.restart_interval!=0)
			{
				if(entropy.restarts_to_go==0)
				{
					emit_restart_arith(cinfo, entropy.next_restart_num);
					entropy.restarts_to_go=cinfo.restart_interval;
					entropy.next_restart_num++;
					entropy.next_restart_num&=7;
				}
				entropy.restarts_to_go--;
			}

			// Encode the MCU data blocks
			for(int blkn=0; blkn<cinfo.block_in_MCU; blkn++)
			{
				short[] block=MCU_data[blkn];
				int ci=cinfo.MCU_membership[blkn];
				jpeg_component_info compptr=cinfo.cur_comp_info[ci];

				// Sections F.1.4.1 & F.1.4.4.1: Encoding of DC coefficients
				int tbl=compptr.dc_tbl_no;

				// Table F.4: Point to statistics bin S0 for DC coefficient coding
				byte[] st=entropy.dc_stats[tbl];
				int st_ind=entropy.dc_context[ci];

				// Figure F.4: Encode_DC_DIFF
				int v=block[0]-entropy.last_dc_val[ci];
				if(v==0)
				{
					arith_encode(cinfo, ref st[st_ind], 0);
					entropy.dc_context[ci]=0;	// zero diff category
				}
				else
				{
					entropy.last_dc_val[ci]=block[0];
					arith_encode(cinfo, ref st[st_ind], 1);

					// Figure F.6: Encoding nonzero value v
					// Figure F.7: Encoding the sign of v
					if(v>0)
					{
						arith_encode(cinfo, ref st[st_ind+1], 0);	// Table F.4: SS = S0 + 1
						st_ind+=2;									// Table F.4: SP = S0 + 2
						entropy.dc_context[ci]=4;					// small positive diff category
					}
					else
					{
						v=-v;
						arith_encode(cinfo, ref st[st_ind+1], 1);	//Table F.4: SS = S0 + 1
						st_ind+=3;									// Table F.4: SN = S0 + 3
						entropy.dc_context[ci]=8;					// small negative diff category
					}

					// Figure F.8: Encoding the magnitude category of v
					int m=0;
					v--;
					if(v!=0)
					{
						arith_encode(cinfo, ref st[st_ind], 1);
						m=1;
						int v2=v;
						st=entropy.dc_stats[tbl];
						st_ind=20; // Table F.4: X1 = 20
						while((v2>>=1)!=0)
						{
							arith_encode(cinfo, ref st[st_ind], 1);
							m<<=1;
							st_ind+=1;
						}
					}
					arith_encode(cinfo, ref st[st_ind], 0);

					// Section F.1.4.4.1.2: Establish dc_context conditioning category
					if(m<(int)((1<<cinfo.arith_dc_L[tbl])>>1)) entropy.dc_context[ci]=0;		// zero diff category
					else if(m>(int)((1<<cinfo.arith_dc_U[tbl])>>1)) entropy.dc_context[ci]+=8;	// large diff category

					// Figure F.9: Encoding the magnitude bit pattern of v
					st_ind+=14;
					while((m>>=1)!=0) arith_encode(cinfo, ref st[st_ind], ((m&v)!=0)?1:0);
				}

				// Sections F.1.4.2 & F.1.4.4.2: Encoding of AC coefficients
				tbl=compptr.ac_tbl_no;

				int k, ke;
				// Establish EOB (end-of-block) index
				for(ke=DCTSIZE2; ke>1; ke--)
				{
					if(block[jpeg_natural_order[ke-1]]!=0) break;
				}

				// Figure F.5: Encode_AC_Coefficients
				for(k=1; k<ke; k++)
				{
					st=entropy.ac_stats[tbl];
					st_ind=3*(k-1);
					arith_encode(cinfo, ref st[st_ind], 0);	// EOB decision
					while((v=block[jpeg_natural_order[k]])==0)
					{
						arith_encode(cinfo, ref st[st_ind+1], 0); st_ind+=3; k++;
					}
					arith_encode(cinfo, ref st[st_ind+1], 1);

					// Figure F.6: Encoding nonzero value v
					// Figure F.7: Encoding the sign of v
					entropy.ac_stats[tbl][245]=0;
					if(v>0)
					{
						arith_encode(cinfo, ref entropy.ac_stats[tbl][245], 0);
					}
					else
					{
						v=-v;
						arith_encode(cinfo, ref entropy.ac_stats[tbl][245], 1);
					}
					st_ind+=2;

					// Figure F.8: Encoding the magnitude category of v
					int m=0;
					v--;
					if(v!=0)
					{
						arith_encode(cinfo, ref st[st_ind], 1);
						m=1;
						int v2=v;
						v2>>=1;
						if(v2!=0)
						{
							arith_encode(cinfo, ref st[st_ind], 1);
							m<<=1;
							st=entropy.ac_stats[tbl];
							st_ind=(k<=cinfo.arith_ac_K[tbl]?189:217);
							while((v2>>=1)!=0)
							{
								arith_encode(cinfo, ref st[st_ind], 1);
								m<<=1;
								st_ind+=1;
							}
						}
					}
					arith_encode(cinfo, ref st[st_ind], 0);

					// Figure F.9: Encoding the magnitude bit pattern of v
					st_ind+=14;
					while((m>>=1)!=0) arith_encode(cinfo, ref st[st_ind], ((m&v)!=0)?1:0);
				}

				// Encode EOB decision only if k < DCTSIZE2
				if(k<DCTSIZE2)
				{
					st=entropy.ac_stats[tbl];
					st_ind=3*(k-1);
					arith_encode(cinfo, ref st[st_ind], 1);
				}
			}

			return true;
		}

		// Initialize for an arithmetic-compressed scan.
		static void start_pass_c_arith(jpeg_compress cinfo, bool gather_statistics)
		{
			arith_entropy_encoder entropy=(arith_entropy_encoder)cinfo.coef;

			if(gather_statistics)
			{
				// Make sure to avoid that in the master control logic!
				// We are fully adaptive here and need no extra
				// statistics gathering pass!
				ERREXIT(cinfo, J_MESSAGE_CODE.JERR_NOT_COMPILED);
			}

			// We assume jcmaster.cs already validated the progressive scan parameters.

			// Select execution routines
			if(cinfo.process==J_CODEC_PROCESS.JPROC_PROGRESSIVE)
			{
				if(cinfo.Ah==0)
				{
					if(cinfo.Ss==0) entropy.entropy_encode_mcu=encode_mcu_DC_first_arith;
					else entropy.entropy_encode_mcu=encode_mcu_AC_first_arith;
				}
				else
				{
					if(cinfo.Ss==0) entropy.entropy_encode_mcu=encode_mcu_DC_refine_arith;
					else entropy.entropy_encode_mcu=encode_mcu_AC_refine_arith;
				}
			}
			else if(cinfo.process==J_CODEC_PROCESS.JPROC_SEQUENTIAL) entropy.entropy_encode_mcu=encode_mcu_arith;
			else ERREXIT(cinfo, J_MESSAGE_CODE.JERR_NOTIMPL);

			for(int ci=0; ci<cinfo.comps_in_scan; ci++)
			{
				jpeg_component_info compptr=cinfo.cur_comp_info[ci];

				// Allocate & initialize requested statistics areas
				if(cinfo.process!=J_CODEC_PROCESS.JPROC_PROGRESSIVE||(cinfo.Ss==0&&cinfo.Ah==0))
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
				if(cinfo.process!=J_CODEC_PROCESS.JPROC_PROGRESSIVE||cinfo.Ss!=0)
				{
					int tbl=compptr.ac_tbl_no;
					if(tbl<0||tbl>=NUM_ARITH_TBLS) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_NO_ARITH_TABLE, tbl);
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

#if CALCULATE_SPECTRAL_CONDITIONING
					if(cinfo.process==J_CODEC_PROCESS.JPROC_PROGRESSIVE)
					{
						// Section G.1.3.2: Set appropriate arithmetic conditioning value Kx
						cinfo.arith_ac_K[tbl]=(byte)(cinfo.Ss+((8+cinfo.Se-cinfo.Ss)>>4));
					}
#endif
				}
			}

			// Initialize arithmetic encoding variables
			entropy.c=0;
			entropy.a=0x10000;
			entropy.sc=0;
			entropy.zc=0;
			entropy.ct=11;
			entropy.buffer=-1;	// empty

			// Initialize restart stuff
			entropy.restarts_to_go=cinfo.restart_interval;
			entropy.next_restart_num=0;
		}

		// Module initialization routine for arithmetic entropy encoding.
		static void jinit_arith_encoder(jpeg_compress cinfo)
		{
			arith_entropy_encoder entropy=cinfo.coef as arith_entropy_encoder;

			if(entropy==null)
			{
				try
				{
					entropy=new arith_entropy_encoder();
				}
				catch
				{
					ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
				}
				cinfo.coef=entropy;
			}

			entropy.entropy_start_pass=start_pass_c_arith;
			entropy.entropy_finish_pass=finish_pass_c_arith;

			// Mark tables unallocated
			for(int i=0; i<NUM_ARITH_TBLS; i++)
			{
				entropy.dc_stats[i]=null;
				entropy.ac_stats[i]=null;
			}
		}
	}
}
#endif