#if D_LOSSLESS_SUPPORTED
// jclhuff.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1991-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains Huffman entropy encoding routines for lossless JPEG.
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
		// The legal range of a spatial difference is
		// -32767 .. +32768.
		// Hence the magnitude should always fit in 16 bits.
		const int MAX_DIFF_BITS=16;

		// Expanded entropy encoder object for Huffman encoding.
		//
		// The savable_state subrecord contains fields that change within an MCU,
		// but must not be updated permanently until we complete the MCU.
		struct savable_state_ls
		{
			public int put_buffer;		// current bit-accumulation buffer
			public int put_bits;		// # of bits now in it
		}

		struct lhe_input_ptr_info
		{
			public int ci;
			public int yoffset;
			public int MCU_width;
		}

		class lhuff_entropy_encoder
		{
			public savable_state_ls saved;	// Bit buffer at start of MCU

			// These fields are NOT loaded into local working state.
			public uint restarts_to_go;		// MCUs left in this restart interval
			public int next_restart_num;	// next restart number to write (0-7)

			// Pointers to derived tables (these workspaces have image lifespan)
			public c_derived_tbl[] derived_tbls=new c_derived_tbl[NUM_HUFF_TBLS];

			// Pointers to derived tables to be used for each data unit within an MCU
			public c_derived_tbl[] cur_tbls=new c_derived_tbl[C_MAX_BLOCKS_IN_MCU];

#if ENTROPY_OPT_SUPPORTED	// Statistics tables for optimization
			public int[][] count_ptrs=new int[NUM_HUFF_TBLS][];

			// Pointers to stats tables to be used for each data unit within an MCU
			public int[][] cur_counts=new int[C_MAX_BLOCKS_IN_MCU][];
#endif

			// Pointers to the proper input difference row for each group of data units
			// within an MCU. For each component, there are Vi groups of Hi data units.
			public int[][] input_ptr=new int[C_MAX_BLOCKS_IN_MCU][];
			public int[] input_ptr_ind=new int[C_MAX_BLOCKS_IN_MCU];

			// Number of input pointers in use for the current MCU. This is the sum of all Vi in the MCU.
			public int num_input_ptrs;

			// Information used for positioning the input pointers within the input difference rows.
			public lhe_input_ptr_info[] input_ptr_info=new lhe_input_ptr_info[C_MAX_BLOCKS_IN_MCU];

			// Index of the proper input pointer for each data unit within an MCU
			public int[] input_ptr_index=new int[C_MAX_BLOCKS_IN_MCU];
		}

		// Working state while writing an MCU.
		// This struct contains all the fields that are needed by subroutines.
		struct working_state_ls
		{
			public byte[] output_bytes;
			public int next_output_byte;	// => next byte to write in buffer
			public uint free_in_buffer;		// # of byte spaces remaining in buffer
			public savable_state_ls cur;	// Current bit buffer & DC state
			public jpeg_compress cinfo;		// dump_buffer needs access to this
		}

		// Initialize for a Huffman-compressed scan.
		// If gather_statistics is true, we do not output anything during the scan,
		// just count the Huffman symbols used and generate Huffman code tables.
		static void start_pass_huff_ls(jpeg_compress cinfo, bool gather_statistics)
		{
			jpeg_lossless_c_codec losslsc=(jpeg_lossless_c_codec)cinfo.coef;
			lhuff_entropy_encoder entropy=(lhuff_entropy_encoder)losslsc.entropy_private;

			if(gather_statistics)
			{
#if ENTROPY_OPT_SUPPORTED
				losslsc.entropy_encode_mcus=encode_mcus_gather_ls;
				losslsc.entropy_finish_pass=finish_pass_gather_ls;
#else
				ERREXIT(cinfo, J_MESSAGE_CODE.JERR_NOT_COMPILED);
#endif
			}
			else
			{
				losslsc.entropy_encode_mcus=encode_mcus_huff_ls;
				losslsc.entropy_finish_pass=finish_pass_huff_ls;
			}

			for(int ci=0; ci<cinfo.comps_in_scan; ci++)
			{
				jpeg_component_info compptr=cinfo.cur_comp_info[ci];
				int dctbl=compptr.dc_tbl_no;
				if(gather_statistics)
				{
#if ENTROPY_OPT_SUPPORTED
					// Check for invalid table indexes
					// (make_c_derived_tbl does this in the other path)
					if(dctbl<0||dctbl>=NUM_HUFF_TBLS) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_NO_HUFF_TABLE, dctbl);

					// Allocate and zero the statistics tables
					// Note that jpeg_gen_optimal_table expects 257 entries in each table!
					if(entropy.count_ptrs[dctbl]==null)
					{
						entropy.count_ptrs[dctbl]=new int[257];
					}
					else
					{
						for(int i=0; i<257; i++) entropy.count_ptrs[dctbl][i]=0;
					}
#endif
				}
				else
				{
					// Compute derived values for Huffman tables
					// We may do this more than once for a table, but it's not expensive
					jpeg_make_c_derived_tbl(cinfo, true, dctbl, ref entropy.derived_tbls[dctbl]);
				}
			}

			// Precalculate encoding info for each sample in an MCU of this scan
			int ptrn=0;
			for(int sampn=0; sampn<cinfo.block_in_MCU; )
			{
				jpeg_component_info compptr=cinfo.cur_comp_info[cinfo.MCU_membership[sampn]];
				int ci=compptr.component_index;
				//ci=cinfo.MCU_membership[sampn];
				//compptr=cinfo.cur_comp_info[ci];
				for(int yoffset=0; yoffset<compptr.MCU_height; yoffset++, ptrn++)
				{
					// Precalculate the setup info for each input pointer
					entropy.input_ptr_info[ptrn].ci=ci;
					entropy.input_ptr_info[ptrn].yoffset=yoffset;
					entropy.input_ptr_info[ptrn].MCU_width=compptr.MCU_width;
					for(int xoffset=0; xoffset<compptr.MCU_width; xoffset++, sampn++)
					{
						// Precalculate the input pointer index for each sample
						entropy.input_ptr_index[sampn]=ptrn;
						// Precalculate which tables to use for each sample
						entropy.cur_tbls[sampn]=entropy.derived_tbls[compptr.dc_tbl_no];
						entropy.cur_counts[sampn]=entropy.count_ptrs[compptr.dc_tbl_no];
					}
				}
			}
			entropy.num_input_ptrs=ptrn;

			// Initialize bit buffer to empty
			entropy.saved.put_buffer=0;
			entropy.saved.put_bits=0;

			// Initialize restart stuff
			entropy.restarts_to_go=cinfo.restart_interval;
			entropy.next_restart_num=0;
		}

		// Outputting bytes to the file

		// Empty the output buffer; return true if successful, false if must suspend
		static bool dump_buffer(ref working_state_ls state)
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
		static bool emit_bits(ref working_state_ls state, uint code, int size)
		{
			// This routine is heavily used, so it's worth coding tightly.
			int put_buffer=(int)code;
			int put_bits=state.cur.put_bits;

			// if size is 0, caller used an invalid Huffman table entry
			if(size==0) ERREXIT(state.cinfo, J_MESSAGE_CODE.JERR_HUFF_MISSING_CODE);

			put_buffer&=(1<<size)-1;		// mask off any extra bits in code
			put_bits+=size;						// new number of bits in buffer
			put_buffer<<=24-put_bits;			// align incoming bits
			put_buffer|=state.cur.put_buffer;	// and merge with old buffer contents

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

		static bool flush_bits(ref working_state_ls state)
		{
			if(!emit_bits(ref state, 0x7F, 7)) return false; // fill any partial byte with ones
			state.cur.put_buffer=0;	// and reset bit-buffer to empty
			state.cur.put_bits=0;
			return true;
		}

		// Emit a restart marker & resynchronize predictions.
		static bool emit_restart(ref working_state_ls state, int restart_num)
		{
			if(!flush_bits(ref state)) return false;

			//was emit_byte(state, 0xFF, return false);
			state.output_bytes[state.next_output_byte++]=0xFF;
			state.free_in_buffer--;
			if(state.free_in_buffer==0)
			{
				if(!dump_buffer(ref state)) return false;
			}

			//was emit_byte(state, JPEG_RST0+restart_num, return false);
			state.output_bytes[state.next_output_byte++]=(byte)(JPEG_RST0+restart_num);
			state.free_in_buffer--;
			if(state.free_in_buffer==0)
			{
				if(!dump_buffer(ref state)) return false;
			}

			// The restart counter is not updated until we successfully write the MCU.

			return true;
		}

		// Encode and output one nMCU's worth of Huffman-compressed differences.
		static uint encode_mcus_huff_ls(jpeg_compress cinfo, int[][][] diff_buf, uint MCU_row_num, uint MCU_col_num, uint nMCU)
		{
			jpeg_lossless_c_codec losslsc=(jpeg_lossless_c_codec)cinfo.coef;
			lhuff_entropy_encoder entropy=(lhuff_entropy_encoder)losslsc.entropy_private;

			// Load up working state
			working_state_ls state;
			state.output_bytes=cinfo.dest.output_bytes;
			state.next_output_byte=cinfo.dest.next_output_byte;
			state.free_in_buffer=cinfo.dest.free_in_buffer;
			state.cur=entropy.saved;
			state.cinfo=cinfo;

			// Emit restart marker if needed
			if(cinfo.restart_interval!=0)
			{
				if(entropy.restarts_to_go==0)
				{
					if(!emit_restart(ref state, entropy.next_restart_num)) return 0;
				}
			}

			// Set input pointer locations based on MCU_col_num
			for(int ptrn=0; ptrn<entropy.num_input_ptrs; ptrn++)
			{
				int ci=entropy.input_ptr_info[ptrn].ci;
				int yoffset=entropy.input_ptr_info[ptrn].yoffset;
				int MCU_width=entropy.input_ptr_info[ptrn].MCU_width;
				entropy.input_ptr[ptrn]=diff_buf[ci][MCU_row_num+yoffset];
				entropy.input_ptr_ind[ptrn]=(int)MCU_col_num*MCU_width;
			}

			for(uint mcu_num=0; mcu_num<nMCU; mcu_num++)
			{
				// Inner loop handles the samples in the MCU
				for(int sampn=0; sampn<cinfo.block_in_MCU; sampn++)
				{
					c_derived_tbl dctbl=entropy.cur_tbls[sampn];

					// Encode the difference per section H.1.2.2

					// Input the sample difference
					int temp3=entropy.input_ptr_index[sampn];
					int temp=entropy.input_ptr[temp3][entropy.input_ptr_ind[temp3]++];

					int temp2;
					if((temp&0x8000)!=0)
					{	// instead of temp < 0
						temp=(-temp)&0x7FFF;			// absolute value, mod 2^16
						if(temp==0) temp2=temp=0x8000;	// special case: magnitude = 32768
						temp2=~temp;					// one's complement of magnitude
					}
					else
					{
						temp&=0x7FFF;	// abs value mod 2^16
						temp2=temp;		// magnitude
					}

					// Find the number of bits needed for the magnitude of the difference
					int nbits=0;
					while(temp!=0)
					{
						nbits++;
						temp>>=1;
					}

					// Check for out-of-range difference values.
					if(nbits>MAX_DIFF_BITS) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_DIFF);

					// Emit the Huffman-coded symbol for the number of bits
					if(!emit_bits(ref state, dctbl.ehufco[nbits], dctbl.ehufsi[nbits])) return mcu_num;

					// Emit that number of bits of the value, if positive,
					// or the complement of its magnitude, if negative.
					if(nbits!=0&&		// emit_bits rejects calls with size 0
						nbits!=16)		// special case: no bits should be emitted
					{
						if(!emit_bits(ref state, (uint)temp2, nbits)) return mcu_num;
					}
				}

				// Completed MCU, so update state
				cinfo.dest.output_bytes=state.output_bytes;
				cinfo.dest.next_output_byte=state.next_output_byte;
				cinfo.dest.free_in_buffer=state.free_in_buffer;
				entropy.saved=state.cur;

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
			}

			return nMCU;
		}

		// Finish up at the end of a Huffman-compressed scan.
		static void finish_pass_huff_ls(jpeg_compress cinfo)
		{
			jpeg_lossless_c_codec losslsc=(jpeg_lossless_c_codec)cinfo.coef;
			lhuff_entropy_encoder entropy=(lhuff_entropy_encoder)losslsc.entropy_private;

			// Load up working state ... flush_bits needs it
			working_state_ls state;
			state.output_bytes=cinfo.dest.output_bytes;
			state.next_output_byte=cinfo.dest.next_output_byte;
			state.free_in_buffer=cinfo.dest.free_in_buffer;
			state.cur=entropy.saved;
			state.cinfo=cinfo;

			// Flush out the last data
			if(!flush_bits(ref state)) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_CANT_SUSPEND);

			// Update state
			cinfo.dest.output_bytes=state.output_bytes;
			cinfo.dest.next_output_byte=state.next_output_byte;
			cinfo.dest.free_in_buffer=state.free_in_buffer;
			entropy.saved=state.cur;
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

		// Trial-encode one nMCU's worth of Huffman-compressed differences.
		// No data is actually output, so no suspension return is possible.
		static uint encode_mcus_gather_ls(jpeg_compress cinfo, int[][][] diff_buf, uint MCU_row_num, uint MCU_col_num, uint nMCU)
		{
			jpeg_lossless_c_codec losslsc=(jpeg_lossless_c_codec)cinfo.coef;
			lhuff_entropy_encoder entropy=(lhuff_entropy_encoder)losslsc.entropy_private;

			// Take care of restart intervals if needed
			if(cinfo.restart_interval!=0)
			{
				if(entropy.restarts_to_go==0) entropy.restarts_to_go=cinfo.restart_interval; // Update restart state
				entropy.restarts_to_go--;
			}

			// Set input pointer locations based on MCU_col_num
			for(int ptrn=0; ptrn<entropy.num_input_ptrs; ptrn++)
			{
				int ci=entropy.input_ptr_info[ptrn].ci;
				int yoffset=entropy.input_ptr_info[ptrn].yoffset;
				int MCU_width=entropy.input_ptr_info[ptrn].MCU_width;
				entropy.input_ptr[ptrn]=diff_buf[ci][MCU_row_num+yoffset];
				entropy.input_ptr_ind[ptrn]=(int)MCU_col_num*MCU_width;
			}

			for(uint mcu_num=0; mcu_num<nMCU; mcu_num++)
			{
				// Inner loop handles the samples in the MCU
				for(int sampn=0; sampn<cinfo.block_in_MCU; sampn++)
				{
					c_derived_tbl dctbl=entropy.cur_tbls[sampn];
					int[] counts=entropy.cur_counts[sampn];

					// Encode the difference per section H.1.2.2

					// Input the sample difference
					int temp3=entropy.input_ptr_index[sampn];
					int temp=entropy.input_ptr[temp3][entropy.input_ptr_ind[temp3]++];

					if((temp&0x8000)!=0)
					{	// instead of temp < 0
						temp=(-temp)&0x7FFF;		// absolute value, mod 2^16
						if(temp==0) temp=0x8000;	// special case: magnitude = 32768
					}
					else temp&=0x7FFF;				// abs value mod 2^16

					// Find the number of bits needed for the magnitude of the difference
					int nbits=0;
					while(temp!=0)
					{
						nbits++;
						temp>>=1;
					}

					// Check for out-of-range difference values.
					if(nbits>MAX_DIFF_BITS) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_DIFF);

					// Count the Huffman symbol for the number of bits
					counts[nbits]++;
				}
			}

			return nMCU;
		}

		// Finish up a statistics-gathering pass and create the new Huffman tables.
		static void finish_pass_gather_ls(jpeg_compress cinfo)
		{
			jpeg_lossless_c_codec losslsc=(jpeg_lossless_c_codec)cinfo.coef;
			lhuff_entropy_encoder entropy=(lhuff_entropy_encoder)losslsc.entropy_private;

			// It's important not to apply jpeg_gen_optimal_table more than once
			// per table, because it clobbers the input frequency counts!
			bool[] did_dc=new bool[NUM_HUFF_TBLS];

			for(int ci=0; ci<cinfo.comps_in_scan; ci++)
			{
				jpeg_component_info compptr=cinfo.cur_comp_info[ci];
				int dctbl=compptr.dc_tbl_no;
				if(!did_dc[dctbl])
				{
					if(cinfo.dc_huff_tbl_ptrs[dctbl]==null) cinfo.dc_huff_tbl_ptrs[dctbl]=jpeg_alloc_huff_table(cinfo);
					jpeg_gen_optimal_table(cinfo, cinfo.dc_huff_tbl_ptrs[dctbl], entropy.count_ptrs[dctbl]);
					did_dc[dctbl]=true;
				}
			}
		}
#endif // ENTROPY_OPT_SUPPORTED

		static bool need_optimization_pass_ls(jpeg_compress cinfo)
		{
			return true;
		}

		// Module initialization routine for Huffman entropy encoding.
		static void jinit_lhuff_encoder(jpeg_compress cinfo)
		{
			jpeg_lossless_c_codec losslsc=(jpeg_lossless_c_codec)cinfo.coef;
			lhuff_entropy_encoder entropy=null;

			try
			{
				entropy=new lhuff_entropy_encoder();
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}

			losslsc.entropy_private=entropy;
			losslsc.entropy_start_pass=start_pass_huff_ls;
			losslsc.need_optimization_pass=need_optimization_pass_ls;

			// Mark tables unallocated
			for(int i=0; i<NUM_HUFF_TBLS; i++)
			{
				entropy.derived_tbls[i]=null;
#if ENTROPY_OPT_SUPPORTED
				entropy.count_ptrs[i]=null;
#endif
			}
		}
	}
}
#endif // D_LOSSLESS_SUPPORTED