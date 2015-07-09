// jcshuff.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1991-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains Huffman entropy encoding routines for sequential JPEG.
//
// Much of the complexity here has to do with supporting output suspension.
// If the data destination module demands suspension, we want to be able to
// back up to the start of the current MCU. To do this, we copy state
// variables into local working storage, and update them back to the
// permanent JPEG objects only upon successful completion of an MCU.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Expanded entropy encoder object for Huffman encoding.
		//
		// The savable_state subrecord contains fields that change within an MCU,
		// but must not be updated permanently until we complete the MCU.
		struct savable_state_sq
		{
			public int put_buffer;		// current bit-accumulation buffer
			public int put_bits;		// # of bits now in it
			public int[] last_dc_val;	// last DC coef for each component
		}

		class shuff_entropy_encoder
		{
			public savable_state_sq saved; // Bit buffer & DC state at start of MCU

			// These fields are NOT loaded into local working state.
			public uint restarts_to_go;		// MCUs left in this restart interval
			public int next_restart_num;	// next restart number to write (0-7)

			// Pointers to derived tables (these workspaces have image lifespan)
			public c_derived_tbl[] dc_derived_tbls=new c_derived_tbl[NUM_HUFF_TBLS];
			public c_derived_tbl[] ac_derived_tbls=new c_derived_tbl[NUM_HUFF_TBLS];

#if ENTROPY_OPT_SUPPORTED	// Statistics tables for optimization
			public int[][] dc_count_ptrs=new int[NUM_HUFF_TBLS][];
			public int[][] ac_count_ptrs=new int[NUM_HUFF_TBLS][];
#endif
		}

		// Working state while writing an MCU.
		// This struct contains all the fields that are needed by subroutines.
		struct working_state_sq
		{
			public byte[] output_bytes;
			public int next_output_byte;	// => next byte to write in buffer
			public uint free_in_buffer;		// # of byte spaces remaining in buffer
			public savable_state_sq cur;	// Current bit buffer & DC state
			public jpeg_compress cinfo;		// dump_buffer needs access to this
		}

		// Initialize for a Huffman-compressed scan.
		// If gather_statistics is true, we do not output anything during the scan,
		// just count the Huffman symbols used and generate Huffman code tables.
		static void start_pass_huff(jpeg_compress cinfo, bool gather_statistics)
		{
			jpeg_lossy_c_codec lossyc=(jpeg_lossy_c_codec)cinfo.coef;
			shuff_entropy_encoder entropy=(shuff_entropy_encoder)lossyc.entropy_private;

			if(gather_statistics)
			{
#if ENTROPY_OPT_SUPPORTED
				lossyc.entropy_encode_mcu=encode_mcu_gather_sq;
				lossyc.entropy_finish_pass=finish_pass_gather_sq;
#else
				ERREXIT(cinfo, J_MESSAGE_CODE.JERR_NOT_COMPILED);
#endif
			}
			else
			{
				lossyc.entropy_encode_mcu=encode_mcu_huff_sq;
				lossyc.entropy_finish_pass=finish_pass_huff_sq;
			}

			for(int ci=0; ci<cinfo.comps_in_scan; ci++)
			{
				jpeg_component_info compptr=cinfo.cur_comp_info[ci];
				int dctbl=compptr.dc_tbl_no;
				int actbl=compptr.ac_tbl_no;
				if(gather_statistics)
				{
#if ENTROPY_OPT_SUPPORTED
					// Check for invalid table indexes
					// (make_c_derived_tbl does this in the other path)
					if(dctbl<0||dctbl>=NUM_HUFF_TBLS) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_NO_HUFF_TABLE, dctbl);
					if(actbl<0||actbl>=NUM_HUFF_TBLS) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_NO_HUFF_TABLE, actbl);

					// Allocate and zero the statistics tables
					// Note that jpeg_gen_optimal_table expects 257 entries in each table!
					if(entropy.dc_count_ptrs[dctbl]==null)
					{
						try
						{
							entropy.dc_count_ptrs[dctbl]=new int[257];
						}
						catch
						{
							ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
						}
					}
					else
					{
						for(int i=0; i<257; i++) entropy.dc_count_ptrs[dctbl][i]=0;
					}

					if(entropy.ac_count_ptrs[actbl]==null)
					{
						try
						{
							entropy.ac_count_ptrs[actbl]=new int[257];
						}
						catch
						{
							ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
						}
					}
					else
					{
						for(int i=0; i<257; i++) entropy.ac_count_ptrs[actbl][i]=0;
					}
#endif
				}
				else
				{
					// Compute derived values for Huffman tables
					// We may do this more than once for a table, but it's not expensive
					jpeg_make_c_derived_tbl(cinfo, true, dctbl, ref entropy.dc_derived_tbls[dctbl]);
					jpeg_make_c_derived_tbl(cinfo, false, actbl, ref entropy.ac_derived_tbls[actbl]);
				}
				// Initialize DC predictions to 0
				entropy.saved.last_dc_val[ci]=0;
			}

			// Initialize bit buffer to empty
			entropy.saved.put_buffer=0;
			entropy.saved.put_bits=0;

			// Initialize restart stuff
			entropy.restarts_to_go=cinfo.restart_interval;
			entropy.next_restart_num=0;
		}

		// Outputting bytes to the file

		// Empty the output buffer; return true if successful, false if must suspend
		static bool dump_buffer(ref working_state_sq state)
		{
			jpeg_destination_mgr dest=state.cinfo.dest;

			if(!dest.empty_output_buffer(state.cinfo)) return false;

			// After a successful buffer dump, must reset buffer pointers
			state.output_bytes=dest.output_bytes;
			state.next_output_byte=dest.next_output_byte;
			state.free_in_buffer=dest.free_in_buffer;
			return true;
		}

		// Outputting bits to the file

		// Only the right 24 bits of put_buffer are used; the valid bits are
		// left-justified in this part. At most 16 bits can be passed to emit_bits
		// in one call, and we never retain more than 7 bits in put_buffer
		// between calls, so 24 bits are sufficient.

		// Emit some bits; return true if successful, false if must suspend
		static bool emit_bits(ref working_state_sq state, uint code, int size)
		{
			// This routine is heavily used, so it's worth coding tightly.
			int put_buffer=(int)code;
			int put_bits=state.cur.put_bits;

			// if size is 0, caller used an invalid Huffman table entry
			if(size==0) ERREXIT(state.cinfo, J_MESSAGE_CODE.JERR_HUFF_MISSING_CODE);

			put_buffer&=(1<<size)-1; // mask off any extra bits in code
			put_bits+=size;					// new number of bits in buffer
			put_buffer<<=24-put_bits;		// align incoming bits
			put_buffer|=state.cur.put_buffer; // and merge with old buffer contents

			while(put_bits>=8)
			{
				byte c=(byte)((put_buffer>>16)&0xFF);

				//was emit_byte(state, c, return false);
				state.output_bytes[state.next_output_byte++]=c;
				state.free_in_buffer--;
				if(state.free_in_buffer==0)
				{
					if(!dump_buffer(ref state)) return false;
				}

				if(c==0xFF)
				{ // need to stuff a zero byte?
					//was emit_byte(state, 0, return false);
					state.output_bytes[state.next_output_byte++]=0;
					state.free_in_buffer--;
					if(state.free_in_buffer==0)
					{
						if(!dump_buffer(ref state)) return false;
					}
				}
				put_buffer<<=8;
				put_bits-=8;
			}

			state.cur.put_buffer=put_buffer; // update state variables
			state.cur.put_bits=put_bits;

			return true;
		}

		static bool flush_bits(ref working_state_sq state)
		{
			if(!emit_bits(ref state, 0x7F, 7)) return false; // fill any partial byte with ones
			state.cur.put_buffer=0;	// and reset bit-buffer to empty
			state.cur.put_bits=0;
			return true;
		}

		// Encode a single block's worth of coefficients
		static bool encode_one_block(ref working_state_sq state, short[] block, int last_dc_val, c_derived_tbl dctbl, c_derived_tbl actbl)
		{
			// Encode the DC coefficient difference per section F.1.2.1
			int temp=block[0]-last_dc_val;
			int temp2=temp;

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
			if(nbits>MAX_COEF_BITS+1) ERREXIT(state.cinfo, J_MESSAGE_CODE.JERR_BAD_DCT_COEF);

			// Emit the Huffman-coded symbol for the number of bits
			if(!emit_bits(ref state, dctbl.ehufco[nbits], dctbl.ehufsi[nbits])) return false;

			// Emit that number of bits of the value, if positive,
			// or the complement of its magnitude, if negative.
			if(nbits!=0)			// emit_bits rejects calls with size 0
			{
				if(!emit_bits(ref state, (uint)temp2, nbits)) return false;
			}

			// Encode the AC coefficients per section F.1.2.2
			int r=0; // r = run length of zeros

			for(int k=1; k<DCTSIZE2; k++)
			{
				temp=block[jpeg_natural_order[k]];
				if(temp==0)
				{
					r++;
					continue;
				}
				// if run length > 15, must emit special run-length-16 codes (0xF0)
				while(r>15)
				{
					if(!emit_bits(ref state, actbl.ehufco[0xF0], actbl.ehufsi[0xF0])) return false;
					r-=16;
				}

				temp2=temp;
				if(temp<0)
				{
					temp=-temp; // temp is abs value of input
					// This code assumes we are on a two's complement machine
					temp2--;
				}

				// Find the number of bits needed for the magnitude of the coefficient
				nbits=1; // there must be at least one 1 bit
				while((temp>>=1)!=0) nbits++;

				// Check for out-of-range coefficient values
				if(nbits>MAX_COEF_BITS) ERREXIT(state.cinfo, J_MESSAGE_CODE.JERR_BAD_DCT_COEF);

				// Emit Huffman symbol for run length / number of bits
				int i=(r<<4)+nbits;
				if(!emit_bits(ref state, actbl.ehufco[i], actbl.ehufsi[i])) return false;

				// Emit that number of bits of the value, if positive,
				// or the complement of its magnitude, if negative.
				if(!emit_bits(ref state, (uint)temp2, nbits)) return false;

				r=0;
			}

			// If the last coef(s) were zero, emit an end-of-block code
			if(r>0)
			{
				if(!emit_bits(ref state, actbl.ehufco[0], actbl.ehufsi[0])) return false;
			}

			return true;
		}

		// Emit a restart marker & resynchronize predictions.
		static bool emit_restart(ref working_state_sq state, int restart_num)
		{
			if(!flush_bits(ref state)) return false;

			//was emit_byte(state, 0xFF, return false);
			state.output_bytes[state.next_output_byte++]=0xFF;
			state.free_in_buffer--;
			if(state.free_in_buffer==0)
			{
				if(!dump_buffer(ref state)) return false;
			}

			//was emit_byte(state, JPEG_RST0 + restart_num, return false);
			state.output_bytes[state.next_output_byte++]=(byte)(JPEG_RST0+restart_num);
			state.free_in_buffer--;
			if(state.free_in_buffer==0)
			{
				if(!dump_buffer(ref state)) return false;
			}

			// Re-initialize DC predictions to 0
			for(int ci=0; ci<state.cinfo.comps_in_scan; ci++) state.cur.last_dc_val[ci]=0;

			// The restart counter is not updated until we successfully write the MCU.

			return true;
		}

		// Encode and output one MCU's worth of Huffman-compressed coefficients.
		static bool encode_mcu_huff_sq(jpeg_compress cinfo, short[][] MCU_data)
		{
			jpeg_lossy_c_codec lossyc=(jpeg_lossy_c_codec)cinfo.coef;
			shuff_entropy_encoder entropy=(shuff_entropy_encoder)lossyc.entropy_private;

			// Load up working state
			working_state_sq state;
			state.cur.last_dc_val=new int[MAX_COMPS_IN_SCAN];
			state.output_bytes=cinfo.dest.output_bytes;
			state.next_output_byte=cinfo.dest.next_output_byte;
			state.free_in_buffer=cinfo.dest.free_in_buffer;

			//was state.cur=entropy.saved;
			state.cur.put_bits=entropy.saved.put_bits;
			state.cur.put_buffer=entropy.saved.put_buffer;
			entropy.saved.last_dc_val.CopyTo(state.cur.last_dc_val, 0);

			state.cinfo=cinfo;

			// Emit restart marker if needed
			if(cinfo.restart_interval!=0)
			{
				if(entropy.restarts_to_go==0)
				{
					if(!emit_restart(ref state, entropy.next_restart_num)) return false;
				}
			}

			// Encode the MCU data blocks
			for(int blkn=0; blkn<cinfo.block_in_MCU; blkn++)
			{
				int ci=cinfo.MCU_membership[blkn];
				jpeg_component_info compptr=cinfo.cur_comp_info[ci];
				if(!encode_one_block(ref state, MCU_data[blkn], state.cur.last_dc_val[ci], entropy.dc_derived_tbls[compptr.dc_tbl_no], entropy.ac_derived_tbls[compptr.ac_tbl_no]))
					return false;

				// Update last_dc_val
				state.cur.last_dc_val[ci]=MCU_data[blkn][0];
			}

			// Completed MCU, so update state
			cinfo.dest.output_bytes=state.output_bytes;
			cinfo.dest.next_output_byte=state.next_output_byte;
			cinfo.dest.free_in_buffer=state.free_in_buffer;

			//was entropy.saved=state.cur;
			entropy.saved.put_bits=state.cur.put_bits;
			entropy.saved.put_buffer=state.cur.put_buffer;
			state.cur.last_dc_val.CopyTo(entropy.saved.last_dc_val, 0);

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

		// Finish up at the end of a Huffman-compressed scan.
		static void finish_pass_huff_sq(jpeg_compress cinfo)
		{
			jpeg_lossy_c_codec lossyc=(jpeg_lossy_c_codec)cinfo.coef;
			shuff_entropy_encoder entropy=(shuff_entropy_encoder)lossyc.entropy_private;
			working_state_sq state;
			state.cur.last_dc_val=new int[MAX_COMPS_IN_SCAN];

			// Load up working state ... flush_bits needs it
			state.output_bytes=cinfo.dest.output_bytes;
			state.next_output_byte=cinfo.dest.next_output_byte;
			state.free_in_buffer=cinfo.dest.free_in_buffer;
			state.cur=entropy.saved;
			entropy.saved.last_dc_val.CopyTo(state.cur.last_dc_val, 0);
			state.cinfo=cinfo;

			// Flush out the last data
			if(!flush_bits(ref state)) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_CANT_SUSPEND);

			// Update state
			cinfo.dest.output_bytes=state.output_bytes;
			cinfo.dest.next_output_byte=state.next_output_byte;
			cinfo.dest.free_in_buffer=state.free_in_buffer;
			entropy.saved=state.cur;
			state.cur.last_dc_val.CopyTo(entropy.saved.last_dc_val, 0);
		}

		// Huffman coding optimization.
		//
		// We first scan the supplied data and count the number of uses of each symbol
		// that is to be Huffman-coded. (This process MUST agree with the code above.)
		// Then we build a Huffman coding tree for the observed counts.
		// Symbols which are not needed at all for the particular image are not
		// assigned any code, which saves space in the DHT marker as well as in
		// the compressed data.
#if ENTROPY_OPT_SUPPORTED
		// Process a single block's worth of coefficients
		static void htest_one_block_sq(jpeg_compress cinfo, short[] block, int last_dc_val, int[] dc_counts, int[] ac_counts)
		{
			// Encode the DC coefficient difference per section F.1.2.1
			int temp=block[0]-last_dc_val;
			if(temp<0) temp=-temp;

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

			// Count the Huffman symbol for the number of bits
			dc_counts[nbits]++;

			// Encode the AC coefficients per section F.1.2.2
			int r=0;			// r = run length of zeros

			for(int k=1; k<DCTSIZE2; k++)
			{
				temp=block[jpeg_natural_order[k]];
				if(temp==0)
				{
					r++;
					continue;
				}

				// if run length > 15, must emit special run-length-16 codes (0xF0)
				while(r>15)
				{
					ac_counts[0xF0]++;
					r-=16;
				}

				// Find the number of bits needed for the magnitude of the coefficient
				if(temp<0) temp=-temp;

				// Find the number of bits needed for the magnitude of the coefficient
				nbits=1; // there must be at least one 1 bit
				while((temp>>=1)!=0) nbits++;

				// Check for out-of-range coefficient values
				if(nbits>MAX_COEF_BITS) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_DCT_COEF);

				// Count Huffman symbol for run length / number of bits
				ac_counts[(r<<4)+nbits]++;

				r=0;
			}

			// If the last coef(s) were zero, emit an end-of-block code
			if(r>0) ac_counts[0]++;
		}

		// Trial-encode one MCU's worth of Huffman-compressed coefficients.
		// No data is actually output, so no suspension return is possible.
		static bool encode_mcu_gather_sq(jpeg_compress cinfo, short[][] MCU_data)
		{
			jpeg_lossy_c_codec lossyc=(jpeg_lossy_c_codec)cinfo.coef;
			shuff_entropy_encoder entropy=(shuff_entropy_encoder)lossyc.entropy_private;

			// Take care of restart intervals if needed
			if(cinfo.restart_interval!=0)
			{
				if(entropy.restarts_to_go==0)
				{
					// Re-initialize DC predictions to 0
					for(int ci=0; ci<cinfo.comps_in_scan; ci++) entropy.saved.last_dc_val[ci]=0;
					// Update restart state
					entropy.restarts_to_go=cinfo.restart_interval;
				}
				entropy.restarts_to_go--;
			}

			for(int blkn=0; blkn<cinfo.block_in_MCU; blkn++)
			{
				int ci=cinfo.MCU_membership[blkn];
				jpeg_component_info compptr=cinfo.cur_comp_info[ci];
				htest_one_block_sq(cinfo, MCU_data[blkn], entropy.saved.last_dc_val[ci], entropy.dc_count_ptrs[compptr.dc_tbl_no], entropy.ac_count_ptrs[compptr.ac_tbl_no]);
				entropy.saved.last_dc_val[ci]=MCU_data[blkn][0];
			}

			return true;
		}

		// Finish up a statistics-gathering pass and create the new Huffman tables.
		static void finish_pass_gather_sq(jpeg_compress cinfo)
		{
			jpeg_lossy_c_codec lossyc=(jpeg_lossy_c_codec)cinfo.coef;
			shuff_entropy_encoder entropy=(shuff_entropy_encoder)lossyc.entropy_private;

			// It's important not to apply jpeg_gen_optimal_table more than once
			// per table, because it clobbers the input frequency counts!
			bool[] did_dc=new bool[NUM_HUFF_TBLS];
			bool[] did_ac=new bool[NUM_HUFF_TBLS];

			for(int ci=0; ci<cinfo.comps_in_scan; ci++)
			{
				jpeg_component_info compptr=cinfo.cur_comp_info[ci];
				int dctbl=compptr.dc_tbl_no;
				int actbl=compptr.ac_tbl_no;

				if(!did_dc[dctbl])
				{
					if(cinfo.dc_huff_tbl_ptrs[dctbl]==null) cinfo.dc_huff_tbl_ptrs[dctbl]=jpeg_alloc_huff_table(cinfo);
					jpeg_gen_optimal_table(cinfo, cinfo.dc_huff_tbl_ptrs[dctbl], entropy.dc_count_ptrs[dctbl]);
					did_dc[dctbl]=true;
				}
				if(!did_ac[actbl])
				{
					if(cinfo.ac_huff_tbl_ptrs[actbl]==null) cinfo.ac_huff_tbl_ptrs[actbl]=jpeg_alloc_huff_table(cinfo);
					jpeg_gen_optimal_table(cinfo, cinfo.ac_huff_tbl_ptrs[actbl], entropy.ac_count_ptrs[actbl]);
					did_ac[actbl]=true;
				}
			}
		}
#endif // ENTROPY_OPT_SUPPORTED

		static bool need_optimization_pass_sq(jpeg_compress cinfo)
		{
			return true;
		}

		// Module initialization routine for Huffman entropy encoding.
		static void jinit_shuff_encoder(jpeg_compress cinfo)
		{
			jpeg_lossy_c_codec lossyc=(jpeg_lossy_c_codec)cinfo.coef;
			shuff_entropy_encoder entropy=null;

			try
			{
				entropy=new shuff_entropy_encoder();
				entropy.saved.last_dc_val=new int[MAX_COMPS_IN_SCAN];
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}
			lossyc.entropy_private=entropy;
			lossyc.entropy_start_pass=start_pass_huff;
			lossyc.need_optimization_pass=need_optimization_pass_sq;

			// Mark tables unallocated
			for(int i=0; i<NUM_HUFF_TBLS; i++)
			{
				entropy.dc_derived_tbls[i]=entropy.ac_derived_tbls[i]=null;
#if ENTROPY_OPT_SUPPORTED
				entropy.dc_count_ptrs[i]=entropy.ac_count_ptrs[i]=null;
#endif
			}
		}
	}
}
