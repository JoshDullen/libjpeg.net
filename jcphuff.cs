#if C_PROGRESSIVE_SUPPORTED
// jcphuff.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1995-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains Huffman entropy encoding routines for progressive JPEG.
//
// We do not support output suspension in this module, since the library
// currently does not allow multiple-scan files to be written with output
// suspension.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Expanded entropy encoder object for progressive Huffman encoding.
		class phuff_entropy_encoder
		{
			// Mode flag: true for optimization, false for actual data output
			public bool gather_statistics;

			// Bit-level coding status.
			// next_output_byte/free_in_buffer are local copies of cinfo.dest fields.
			public byte[] output_bytes;
			public int next_output_byte;	// => next byte to write in buffer
			public uint free_in_buffer;		// # of byte spaces remaining in buffer
			public int put_buffer;			// current bit-accumulation buffer
			public int put_bits;			// # of bits now in it
			public jpeg_compress cinfo;		// link to cinfo (needed for dump_buffer)

			// Coding status for DC components
			public int[] last_dc_val=new int[MAX_COMPS_IN_SCAN]; // last DC coef for each component

			// Coding status for AC components
			public int ac_tbl_no;		// the table number of the single component
			public uint EOBRUN;			// run length of EOBs
			public uint BE;				// # of buffered correction bits before MCU
			public byte[] bit_buffer;	// buffer for correction bits (1 per char)
			// packing correction bits tightly would save some space but cost time...

			public uint restarts_to_go;		// MCUs left in this restart interval
			public int next_restart_num;	// next restart number to write (0-7)

			// Pointers to derived tables (these workspaces have image lifespan).
			// Since any one scan codes only DC or only AC, we only need one set
			// of tables, not one for DC and one for AC.
			public c_derived_tbl[] derived_tbls=new c_derived_tbl[NUM_HUFF_TBLS];

			// Statistics tables for optimization; again, one set is enough
			public int[][] count_ptrs=new int[NUM_HUFF_TBLS][];
		}

		// MAX_CORR_BITS is the number of bits the AC refinement correction-bit
		// buffer can hold. Larger sizes may slightly improve compression, but
		// 1000 is already well into the realm of overkill.
		// The minimum safe size is 64 bits.
		const int MAX_CORR_BITS=1000;	// Max # of correction bits I can buffer

		// Initialize for a Huffman-compressed scan using progressive JPEG.
		static void start_pass_phuff(jpeg_compress cinfo, bool gather_statistics)
		{
			jpeg_lossy_c_codec lossyc=(jpeg_lossy_c_codec)cinfo.coef;
			phuff_entropy_encoder entropy=(phuff_entropy_encoder)lossyc.entropy_private;
			
			entropy.cinfo=cinfo;
			entropy.gather_statistics=gather_statistics;

			bool is_DC_band=(cinfo.Ss==0);

			// We assume jcmaster.cs already validated the scan parameters.
			// Select execution routines
			if(cinfo.Ah==0)
			{
				if(is_DC_band) lossyc.entropy_encode_mcu=encode_mcu_DC_first_phuff;
				else lossyc.entropy_encode_mcu=encode_mcu_AC_first_phuff;
			}
			else
			{
				if(is_DC_band) lossyc.entropy_encode_mcu=encode_mcu_DC_refine_phuff;
				else
				{
					lossyc.entropy_encode_mcu=encode_mcu_AC_refine_phuff;
					// AC refinement needs a correction bit buffer
					if(entropy.bit_buffer==null)
					{
						try
						{
							entropy.bit_buffer=new byte[MAX_CORR_BITS];
						}
						catch
						{
							ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
						}
					}
				}
			}

			if(gather_statistics) lossyc.entropy_finish_pass=finish_pass_gather_phuff;
			else lossyc.entropy_finish_pass=finish_pass_phuff;

			// Only DC coefficients may be interleaved, so cinfo.comps_in_scan = 1
			// for AC coefficients.
			for(int ci=0; ci<cinfo.comps_in_scan; ci++)
			{
				jpeg_component_info compptr=cinfo.cur_comp_info[ci];

				// Initialize DC predictions to 0
				entropy.last_dc_val[ci]=0;

				// Get table index
				int tbl;
				if(is_DC_band)
				{
					if(cinfo.Ah!=0) continue; // DC refinement needs no table
					tbl=compptr.dc_tbl_no;
				}
				else entropy.ac_tbl_no=tbl=compptr.ac_tbl_no;

				if(gather_statistics)
				{
					// Check for invalid table index
					// (make_c_derived_tbl does this in the other path)
					if(tbl<0||tbl>=NUM_HUFF_TBLS) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_NO_HUFF_TABLE, tbl);

					// Allocate and zero the statistics tables
					// Note that jpeg_gen_optimal_table expects 257 entries in each table!
					if(entropy.count_ptrs[tbl]==null)
					{
						try
						{
							entropy.count_ptrs[tbl]=new int[257];
						}
						catch
						{
							ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
						}
					}
					else
					{
						for(int i=0; i<257; i++) entropy.count_ptrs[tbl][i]=0;
					}
				}
				else
				{
					// Compute derived values for Huffman table
					// We may do this more than once for a table, but it's not expensive
					jpeg_make_c_derived_tbl(cinfo, is_DC_band, tbl, ref entropy.derived_tbls[tbl]);
				}
			}

			// Initialize AC stuff
			entropy.EOBRUN=0;
			entropy.BE=0;

			// Initialize bit buffer to empty
			entropy.put_buffer=0;
			entropy.put_bits=0;

			// Initialize restart stuff
			entropy.restarts_to_go=cinfo.restart_interval;
			entropy.next_restart_num=0;
		}

		// Outputting bytes to the file.
		// NB: these must be called only when actually outputting,
		// that is, entropy.gather_statistics == false.

		// Empty the output buffer; we do not support suspension in this module.
		static void dump_buffer(phuff_entropy_encoder entropy)
		{
			jpeg_destination_mgr dest=entropy.cinfo.dest;

			if(!dest.empty_output_buffer(entropy.cinfo)) ERREXIT(entropy.cinfo, J_MESSAGE_CODE.JERR_CANT_SUSPEND);

			// After a successful buffer dump, must reset buffer pointers
			entropy.output_bytes=dest.output_bytes;
			entropy.next_output_byte=dest.next_output_byte;
			entropy.free_in_buffer=dest.free_in_buffer;
		}

		// Outputting bits to the file

		// Only the right 24 bits of put_buffer are used; the valid bits are
		// left-justified in this part. At most 16 bits can be passed to emit_bits
		// in one call, and we never retain more than 7 bits in put_buffer
		// between calls, so 24 bits are sufficient.

		// Emit some bits, unless we are in gather mode
		static void emit_bits(phuff_entropy_encoder entropy, uint code, int size)
		{
			// This routine is heavily used, so it's worth coding tightly.
			int put_buffer=(int)code;
			int put_bits=entropy.put_bits;

			// if size is 0, caller used an invalid Huffman table entry
			if(size==0) ERREXIT(entropy.cinfo, J_MESSAGE_CODE.JERR_HUFF_MISSING_CODE);

			if(entropy.gather_statistics) return; // do nothing if we're only getting stats

			put_buffer&=(1<<size)-1; // mask off any extra bits in code
			put_bits+=size;					// new number of bits in buffer
			put_buffer<<=24-put_bits;		// align incoming bits
			put_buffer|=entropy.put_buffer;	// and merge with old buffer contents

			while(put_bits>=8)
			{
				byte c=(byte)((put_buffer>>16)&0xFF);

				//was emit_byte(entropy, c);
				entropy.output_bytes[entropy.next_output_byte++]=c;
				entropy.free_in_buffer--;
				if(entropy.free_in_buffer==0) dump_buffer(entropy);

				if(c==0xFF)
				{		// need to stuff a zero byte?
					//was emit_byte(entropy, 0);
					entropy.output_bytes[entropy.next_output_byte++]=0;
					entropy.free_in_buffer--;
					if(entropy.free_in_buffer==0) dump_buffer(entropy);

				}
				put_buffer<<=8;
				put_bits-=8;
			}

			entropy.put_buffer=put_buffer; // update variables
			entropy.put_bits=put_bits;
		}

		static void flush_bits(phuff_entropy_encoder entropy)
		{
			emit_bits(entropy, 0x7F, 7);	// fill any partial byte with ones
			entropy.put_buffer=0;			// and reset bit-buffer to empty
			entropy.put_bits=0;
		}

		// Emit (or just count) a Huffman symbol.
		static void emit_symbol(phuff_entropy_encoder entropy, int tbl_no, int symbol)
		{
			if(entropy.gather_statistics) entropy.count_ptrs[tbl_no][symbol]++;
			else
			{
				c_derived_tbl tbl=entropy.derived_tbls[tbl_no];
				emit_bits(entropy, tbl.ehufco[symbol], tbl.ehufsi[symbol]);
			}
		}

		// Emit bits from a correction bit buffer.
		static void emit_buffered_bits(phuff_entropy_encoder entropy, byte[] buf, uint bufstart, uint nbits)
		{
			if(entropy.gather_statistics) return; // no real work

			while(nbits>0)
			{
				emit_bits(entropy, buf[bufstart++], 1);
				nbits--;
			}
		}

		// Emit any pending EOBRUN symbol.
		static void emit_eobrun(phuff_entropy_encoder entropy)
		{
			if(entropy.EOBRUN<=0) return;

			// if there is any pending EOBRUN
			int temp=(int)entropy.EOBRUN;
			int nbits=0;

			while((temp>>=1)!=0) nbits++;

			// safety check: shouldn't happen given limited correction-bit buffer
			if(nbits>14) ERREXIT(entropy.cinfo, J_MESSAGE_CODE.JERR_HUFF_MISSING_CODE);

			emit_symbol(entropy, entropy.ac_tbl_no, nbits<<4);
			if(nbits!=0) emit_bits(entropy, entropy.EOBRUN, nbits);

			entropy.EOBRUN=0;

			// Emit any buffered correction bits
			emit_buffered_bits(entropy, entropy.bit_buffer, 0, entropy.BE);
			entropy.BE=0;
		}

		// Emit a restart marker & resynchronize predictions.
		static void emit_restart(phuff_entropy_encoder entropy, int restart_num)
		{
			emit_eobrun(entropy);

			if(!entropy.gather_statistics)
			{
				flush_bits(entropy);
				//was emit_byte(entropy, 0xFF);
				entropy.output_bytes[entropy.next_output_byte++]=0xFF;
				entropy.free_in_buffer--;
				if(entropy.free_in_buffer==0) dump_buffer(entropy);

				//was emit_byte(entropy, JPEG_RST0+restart_num);
				entropy.output_bytes[entropy.next_output_byte++]=(byte)(JPEG_RST0+restart_num);
				entropy.free_in_buffer--;
				if(entropy.free_in_buffer==0) dump_buffer(entropy);
			}

			if(entropy.cinfo.Ss==0)
			{
				// Re-initialize DC predictions to 0
				for(int ci=0; ci<entropy.cinfo.comps_in_scan; ci++) entropy.last_dc_val[ci]=0;
			}
			else
			{
				// Re-initialize all AC-related fields to 0
				entropy.EOBRUN=0;
				entropy.BE=0;
			}
		}

		// MCU encoding for DC initial scan (either spectral selection,
		// or first pass of successive approximation).
		static bool encode_mcu_DC_first_phuff(jpeg_compress cinfo, short[][] MCU_data)
		{
			jpeg_lossy_c_codec lossyc=(jpeg_lossy_c_codec)cinfo.coef;
			phuff_entropy_encoder entropy=(phuff_entropy_encoder)lossyc.entropy_private;
			int Al=cinfo.Al;

			entropy.output_bytes=cinfo.dest.output_bytes;
			entropy.next_output_byte=cinfo.dest.next_output_byte;
			entropy.free_in_buffer=cinfo.dest.free_in_buffer;

			// Emit restart marker if needed
			if(cinfo.restart_interval!=0)
			{
				if(entropy.restarts_to_go==0) emit_restart(entropy, entropy.next_restart_num);
			}

			// Encode the MCU data blocks
			for(int blkn=0; blkn<cinfo.block_in_MCU; blkn++)
			{
				short[] block=MCU_data[blkn];
				int ci=cinfo.MCU_membership[blkn];
				jpeg_component_info compptr=cinfo.cur_comp_info[ci];

				// Compute the DC value after the required point transform by Al.
				// This is simply an arithmetic right shift.
				int temp2=(int)block[0]>>Al;

				// DC differences are figured on the point-transformed values.
				int temp=temp2-entropy.last_dc_val[ci];
				entropy.last_dc_val[ci]=temp2;

				// Encode the DC coefficient difference per section G.1.2.1
				temp2=temp;
				if(temp<0)
				{
					temp=-temp; // temp is abs value of input
					// For a negative input, want temp2 = bitwise complement of abs(input)
					// This code assumes we are on a two's complement machine
					temp2--;
				}

				// Find the number of bits needed for the magnitude of the coefficient
				int nbits=0;
				while(temp!=0)
				{
					nbits++;
					temp>>=1;
				}

				// Check for out-of-range coefficient values.
				// Since we're encoding a difference, the range limit is twice as much.
				if(nbits>MAX_COEF_BITS+1) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_DCT_COEF);

				// Count/emit the Huffman-coded symbol for the number of bits
				emit_symbol(entropy, compptr.dc_tbl_no, nbits);

				// Emit that number of bits of the value, if positive,
				// or the complement of its magnitude, if negative.
				if(nbits!=0) emit_bits(entropy, (uint)temp2, nbits); // emit_bits rejects calls with size 0
			}

			cinfo.dest.output_bytes=entropy.output_bytes;
			cinfo.dest.next_output_byte=entropy.next_output_byte;
			cinfo.dest.free_in_buffer=entropy.free_in_buffer;

			// Update restart-interval state too
			if(cinfo.restart_interval!=0)
			{
				if(entropy.restarts_to_go==0)
				{
					entropy.restarts_to_go=cinfo.restart_interval;
					entropy.next_restart_num++;
					entropy.next_restart_num&=7;
				}
				entropy.restarts_to_go--;
			}

			return true;
		}

		// MCU encoding for AC initial scan (either spectral selection,
		// or first pass of successive approximation).
		static bool encode_mcu_AC_first_phuff(jpeg_compress cinfo, short[][] MCU_data)
		{
			jpeg_lossy_c_codec lossyc=(jpeg_lossy_c_codec)cinfo.coef;
			phuff_entropy_encoder entropy=(phuff_entropy_encoder)lossyc.entropy_private;
			int Se=cinfo.Se;
			int Al=cinfo.Al;

			entropy.output_bytes=cinfo.dest.output_bytes;
			entropy.next_output_byte=cinfo.dest.next_output_byte;
			entropy.free_in_buffer=cinfo.dest.free_in_buffer;

			// Emit restart marker if needed
			if(cinfo.restart_interval!=0)
			{
				if(entropy.restarts_to_go==0) emit_restart(entropy, entropy.next_restart_num);
			}

			// Encode the MCU data block
			short[] block=MCU_data[0];

			// Encode the AC coefficients per section G.1.2.2, fig. G.3
			int r=0; // r = run length of zeros

			for(int k=cinfo.Ss; k<=Se; k++)
			{
				int temp=block[jpeg_natural_order[k]];
				if(temp==0)
				{
					r++;
					continue;
				}

				// We must apply the point transform by Al. For AC coefficients this
				// is an integer division with rounding towards 0. To do this portably
				// in C, we shift after obtaining the absolute value; so the code is
				// interwoven with finding the abs value (temp) and output bits (temp2).
				int temp2;
				if(temp<0)
				{
					temp=-temp;		// temp is abs value of input
					temp>>=Al;		// apply the point transform
					// For a negative coef, want temp2 = bitwise complement of abs(coef)
					temp2=~temp;
				}
				else
				{
					temp>>=Al;		// apply the point transform
					temp2=temp;
				}

				// Watch out for case that nonzero coef is zero after point transform
				if(temp==0)
				{
					r++;
					continue;
				}

				// Emit any pending EOBRUN
				if(entropy.EOBRUN>0) emit_eobrun(entropy);

				// if run length > 15, must emit special run-length-16 codes (0xF0)
				while(r>15)
				{
					emit_symbol(entropy, entropy.ac_tbl_no, 0xF0);
					r-=16;
				}

				// Find the number of bits needed for the magnitude of the coefficient
				int nbits=1; // there must be at least one 1 bit
				while((temp>>=1)!=0) nbits++;

				// Check for out-of-range coefficient values
				if(nbits>MAX_COEF_BITS) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_DCT_COEF);

				// Count/emit Huffman symbol for run length / number of bits
				emit_symbol(entropy, entropy.ac_tbl_no, (r<<4)+nbits);

				// Emit that number of bits of the value, if positive,
				// or the complement of its magnitude, if negative.
				emit_bits(entropy, (uint)temp2, nbits);

				r=0; // reset zero run length
			}

			if(r>0) // If there are trailing zeroes,
			{
				entropy.EOBRUN++; // count an EOB
				if(entropy.EOBRUN==0x7FFF) emit_eobrun(entropy); // force it out to avoid overflow
			}

			cinfo.dest.output_bytes=entropy.output_bytes;
			cinfo.dest.next_output_byte=entropy.next_output_byte;
			cinfo.dest.free_in_buffer=entropy.free_in_buffer;

			// Update restart-interval state too
			if(cinfo.restart_interval!=0)
			{
				if(entropy.restarts_to_go==0)
				{
					entropy.restarts_to_go=cinfo.restart_interval;
					entropy.next_restart_num++;
					entropy.next_restart_num&=7;
				}
				entropy.restarts_to_go--;
			}

			return true;
		}

		// MCU encoding for DC successive approximation refinement scan.
		// Note: we assume such scans can be multi-component, although the spec
		// is not very clear on the point.
		static bool encode_mcu_DC_refine_phuff(jpeg_compress cinfo, short[][] MCU_data)
		{
			jpeg_lossy_c_codec lossyc=(jpeg_lossy_c_codec)cinfo.coef;
			phuff_entropy_encoder entropy=(phuff_entropy_encoder)lossyc.entropy_private;
			int Al=cinfo.Al;

			entropy.output_bytes=cinfo.dest.output_bytes;
			entropy.next_output_byte=cinfo.dest.next_output_byte;
			entropy.free_in_buffer=cinfo.dest.free_in_buffer;

			// Emit restart marker if needed
			if(cinfo.restart_interval!=0)
			{
				if(entropy.restarts_to_go==0) emit_restart(entropy, entropy.next_restart_num);
			}

			// Encode the MCU data blocks
			for(int blkn=0; blkn<cinfo.block_in_MCU; blkn++)
			{
				short[] block=MCU_data[blkn];

				// We simply emit the Al'th bit of the DC coefficient value.
				int temp=block[0];
				emit_bits(entropy, (uint)(temp>>Al), 1);
			}

			cinfo.dest.output_bytes=entropy.output_bytes;
			cinfo.dest.next_output_byte=entropy.next_output_byte;
			cinfo.dest.free_in_buffer=entropy.free_in_buffer;

			// Update restart-interval state too
			if(cinfo.restart_interval!=0)
			{
				if(entropy.restarts_to_go==0)
				{
					entropy.restarts_to_go=cinfo.restart_interval;
					entropy.next_restart_num++;
					entropy.next_restart_num&=7;
				}
				entropy.restarts_to_go--;
			}

			return true;
		}

		// MCU encoding for AC successive approximation refinement scan.
		static bool encode_mcu_AC_refine_phuff(jpeg_compress cinfo, short[][] MCU_data)
		{
			jpeg_lossy_c_codec lossyc=(jpeg_lossy_c_codec)cinfo.coef;
			phuff_entropy_encoder entropy=(phuff_entropy_encoder)lossyc.entropy_private;
			int r;
			byte[] BR_buffer;
			uint BR;
			int Se=cinfo.Se;
			int Al=cinfo.Al;
			int[] absvalues=new int[DCTSIZE2];

			entropy.output_bytes=cinfo.dest.output_bytes;
			entropy.next_output_byte=cinfo.dest.next_output_byte;
			entropy.free_in_buffer=cinfo.dest.free_in_buffer;

			// Emit restart marker if needed
			if(cinfo.restart_interval!=0)
			{
				if(entropy.restarts_to_go==0) emit_restart(entropy, entropy.next_restart_num);
			}

			// Encode the MCU data block
			short[] block=MCU_data[0];

			// It is convenient to make a pre-pass to determine the transformed
			// coefficients' absolute values and the EOB position.
			int EOB=0;
			int k;
			for(k=cinfo.Ss; k<=Se; k++)
			{
				int temp=block[jpeg_natural_order[k]];
				// We must apply the point transform by Al. For AC coefficients this
				// is an integer division with rounding towards 0. To do this portably
				// in C, we shift after obtaining the absolute value.
				if(temp<0) temp=-temp;	// temp is abs value of input
				temp>>=Al;				// apply the point transform
				absvalues[k]=temp;		// save abs value for main pass
				if(temp==1) EOB=k;		// EOB = index of last newly-nonzero coef
			}

			// Encode the AC coefficients per section G.1.2.3, fig. G.7
			r=0;			// r = run length of zeros
			BR=0;			// BR = count of buffered bits added now
			BR_buffer=entropy.bit_buffer; // Append bits to buffer
			uint BR_buffer_ind=entropy.BE;

			for(k=cinfo.Ss; k<=Se; k++)
			{
				int temp=absvalues[k];
				if(temp==0)
				{
					r++;
					continue;
				}

				// Emit any required ZRLs, but not if they can be folded into EOB
				while(r>15&&k<=EOB)
				{
					// emit any pending EOBRUN and the BE correction bits
					emit_eobrun(entropy);

					// Emit ZRL
					emit_symbol(entropy, entropy.ac_tbl_no, 0xF0);
					r-=16;

					// Emit buffered correction bits that must be associated with ZRL
					emit_buffered_bits(entropy, BR_buffer, BR_buffer_ind, BR);
					BR_buffer=entropy.bit_buffer; // BE bits are gone now
					BR_buffer_ind=0;
					BR=0;
				}

				// If the coef was previously nonzero, it only needs a correction bit.
				// NOTE: a straight translation of the spec's figure G.7 would suggest
				// that we also need to test r > 15. But if r > 15, we can only get here
				// if k > EOB, which implies that this coefficient is not 1.
				if(temp>1)
				{
					// The correction bit is the next bit of the absolute value.
					BR_buffer[BR_buffer_ind+BR++]=(byte)(temp&1);
					continue;
				}

				// Emit any pending EOBRUN and the BE correction bits
				emit_eobrun(entropy);

				// Count/emit Huffman symbol for run length / number of bits
				emit_symbol(entropy, entropy.ac_tbl_no, (r<<4)+1);

				// Emit output bit for newly-nonzero coef
				temp=(block[jpeg_natural_order[k]]<0)?0:1;
				emit_bits(entropy, (uint)temp, 1);

				// Emit buffered correction bits that must be associated with this code
				emit_buffered_bits(entropy, BR_buffer, BR_buffer_ind, BR);
				BR_buffer=entropy.bit_buffer; // BE bits are gone now
				BR_buffer_ind=0;
				BR=0;
				r=0; // reset zero run length
			}

			if(r>0||BR>0) // If there are trailing zeroes,
			{
				entropy.EOBRUN++;	// count an EOB
				entropy.BE+=BR;		// concat my correction bits to older ones
				// We force out the EOB if we risk either:
				// 1. overflow of the EOB counter;
				// 2. overflow of the correction bit buffer during the next MCU.
				if(entropy.EOBRUN==0x7FFF||entropy.BE>(MAX_CORR_BITS-DCTSIZE2+1)) emit_eobrun(entropy);
			}

			cinfo.dest.output_bytes=entropy.output_bytes;
			cinfo.dest.next_output_byte=entropy.next_output_byte;
			cinfo.dest.free_in_buffer=entropy.free_in_buffer;

			// Update restart-interval state too
			if(cinfo.restart_interval!=0)
			{
				if(entropy.restarts_to_go==0)
				{
					entropy.restarts_to_go=cinfo.restart_interval;
					entropy.next_restart_num++;
					entropy.next_restart_num&=7;
				}
				entropy.restarts_to_go--;
			}

			return true;
		}

		// Finish up at the end of a Huffman-compressed progressive scan.
		static void finish_pass_phuff(jpeg_compress cinfo)
		{
			jpeg_lossy_c_codec lossyc=(jpeg_lossy_c_codec)cinfo.coef;
			phuff_entropy_encoder entropy=(phuff_entropy_encoder)lossyc.entropy_private;

			entropy.output_bytes=cinfo.dest.output_bytes;
			entropy.next_output_byte=cinfo.dest.next_output_byte;
			entropy.free_in_buffer=cinfo.dest.free_in_buffer;

			// Flush out any buffered data
			emit_eobrun(entropy);
			flush_bits(entropy);

			cinfo.dest.output_bytes=entropy.output_bytes;
			cinfo.dest.next_output_byte=entropy.next_output_byte;
			cinfo.dest.free_in_buffer=entropy.free_in_buffer;
		}

		// Finish up a statistics-gathering pass and create the new Huffman tables.
		static void finish_pass_gather_phuff(jpeg_compress cinfo)
		{
			jpeg_lossy_c_codec lossyc=(jpeg_lossy_c_codec)cinfo.coef;
			phuff_entropy_encoder entropy=(phuff_entropy_encoder)lossyc.entropy_private;

			// Flush out buffered data (all we care about is counting the EOB symbol)
			emit_eobrun(entropy);

			bool is_DC_band=(cinfo.Ss==0);

			// It's important not to apply jpeg_gen_optimal_table more than once
			// per table, because it clobbers the input frequency counts!
			bool[] did=new bool[NUM_HUFF_TBLS];

			for(int ci=0; ci<cinfo.comps_in_scan; ci++)
			{
				jpeg_component_info compptr=cinfo.cur_comp_info[ci];
				int tbl;
				if(is_DC_band)
				{
					if(cinfo.Ah!=0) continue; // DC refinement needs no table
					tbl=compptr.dc_tbl_no;
				}
				else
				{
					tbl=compptr.ac_tbl_no;
				}

				if(!did[tbl])
				{
					if(is_DC_band)
					{
						if(cinfo.dc_huff_tbl_ptrs[tbl]==null) cinfo.dc_huff_tbl_ptrs[tbl]=jpeg_alloc_huff_table(cinfo);
						jpeg_gen_optimal_table(cinfo, cinfo.dc_huff_tbl_ptrs[tbl], entropy.count_ptrs[tbl]);
					}
					else
					{
						if(cinfo.ac_huff_tbl_ptrs[tbl]==null) cinfo.ac_huff_tbl_ptrs[tbl]=jpeg_alloc_huff_table(cinfo);
						jpeg_gen_optimal_table(cinfo, cinfo.ac_huff_tbl_ptrs[tbl], entropy.count_ptrs[tbl]);
					}
					did[tbl]=true;
				}
			}
		}

		static bool need_optimization_pass_phuff(jpeg_compress cinfo)
		{
			return (cinfo.Ss!=0||cinfo.Ah==0);
		}

		// Module initialization routine for progressive Huffman entropy encoding.
		static void jinit_phuff_encoder(jpeg_compress cinfo)
		{
			jpeg_lossy_c_codec lossyc=(jpeg_lossy_c_codec)cinfo.coef;
			phuff_entropy_encoder entropy=null;

			try
			{
				entropy=new phuff_entropy_encoder();
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}

			lossyc.entropy_private=entropy;
			lossyc.entropy_start_pass=start_pass_phuff;
			lossyc.need_optimization_pass=need_optimization_pass_phuff;

			// Mark tables unallocated
			for(int i=0; i<NUM_HUFF_TBLS; i++)
			{
				entropy.derived_tbls[i]=null;
				entropy.count_ptrs[i]=null;
			}
			entropy.bit_buffer=null; // needed only in AC refinement scan
		}
	}
}
#endif