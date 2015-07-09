#if D_LOSSLESS_SUPPORTED
// jdlhuff.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1991-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains Huffman entropy decoding routines for lossless JPEG.
//
// Much of the complexity here has to do with supporting input suspension.
// If the data source module demands suspension, we want to be able to back
// up to the start of the current MCU. To do this, we copy state variables
// into local working storage, and update them back to the permanent
// storage only upon successful completion of an MCU.

using System;
using System.Collections.Generic;
using System.Text;

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		struct lhd_output_ptr_info
		{
			public int ci, yoffset, MCU_width;
		}

		// Private entropy decoder object for lossless Huffman decoding.
		class lhuff_entropy_decoder : jpeg_entropy_decoder
		{
			// Pointers to derived tables (these workspaces have image lifespan)
			public d_derived_tbl[] derived_tbls=new d_derived_tbl[NUM_HUFF_TBLS];

			// Precalculated info set up by start_pass for use in decode_mcus:

			// Pointers to derived tables to be used for each data unit within an MCU
			public d_derived_tbl[] cur_tbls=new d_derived_tbl[D_MAX_BLOCKS_IN_MCU];

			// Pointers to the proper output difference row for each group of data units
			// within an MCU. For each component, there are Vi groups of Hi data units.
			public int[][] output_ptr=new int[D_MAX_BLOCKS_IN_MCU][];
			public int[] output_ptr_ind=new int[D_MAX_BLOCKS_IN_MCU];

			// Number of output pointers in use for the current MCU. This is the sum
			// of all Vi in the MCU.
			public int num_output_ptrs;

			// Information used for positioning the output pointers within the output
			// difference rows.
			public lhd_output_ptr_info[] output_ptr_info=new lhd_output_ptr_info[D_MAX_BLOCKS_IN_MCU];

			// Index of the proper output pointer for each data unit within an MCU
			public int[] output_ptr_index=new int[D_MAX_BLOCKS_IN_MCU];
		}

		// Initialize for a Huffman-compressed scan.
		static void start_pass_lhuff_decoder(jpeg_decompress cinfo)
		{
			jpeg_lossless_d_codec losslsd=(jpeg_lossless_d_codec)cinfo.coef;
			lhuff_entropy_decoder entropy=(lhuff_entropy_decoder)losslsd.entropy_private;

			for(int ci=0; ci<cinfo.comps_in_scan; ci++)
			{
				jpeg_component_info compptr=cinfo.cur_comp_info[ci];
				int dctbl=compptr.dc_tbl_no;

				// Make sure requested tables are present
				if(dctbl<0||dctbl>=NUM_HUFF_TBLS||
				cinfo.dc_huff_tbl_ptrs[dctbl]==null) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_NO_HUFF_TABLE, dctbl);

				// Compute derived values for Huffman tables
				// We may do this more than once for a table, but it's not expensive
				jpeg_make_d_derived_tbl(cinfo, true, dctbl, ref entropy.derived_tbls[dctbl]);
			}

			// Precalculate decoding info for each sample in an MCU of this scan
			int ptrn=0;
			for(int sampn=0; sampn<cinfo.blocks_in_MCU; )
			{
				jpeg_component_info compptr=cinfo.cur_comp_info[cinfo.MCU_membership[sampn]];
				int ci=compptr.component_index;
				for(int yoffset=0; yoffset<compptr.MCU_height; yoffset++, ptrn++)
				{
					// Precalculate the setup info for each output pointer
					entropy.output_ptr_info[ptrn].ci=ci;
					entropy.output_ptr_info[ptrn].yoffset=yoffset;
					entropy.output_ptr_info[ptrn].MCU_width=compptr.MCU_width;
					for(int xoffset=0; xoffset<compptr.MCU_width; xoffset++, sampn++)
					{
						// Precalculate the output pointer index for each sample
						entropy.output_ptr_index[sampn]=ptrn;

						// Precalculate which table to use for each sample
						entropy.cur_tbls[sampn]=entropy.derived_tbls[compptr.dc_tbl_no];
					}
				}
			}
			entropy.num_output_ptrs=ptrn;

			// Initialize bitread state variables
			entropy.bitstate.bits_left=0;
			entropy.bitstate.get_buffer=0; // unnecessary, but keeps Purify quiet
			entropy.insufficient_data=false;
		}

		//#define HUFF_EXTEND(x,s) ((x) < (1<<((s)-1)) ? (x) + (((-1)<<(s)) + 1) : (x))

		// Check for a restart marker & resynchronize decoder.
		// Returns false if must suspend.
		static bool process_restart_dlhuff(jpeg_decompress cinfo)
		{
			jpeg_lossless_d_codec losslsd=(jpeg_lossless_d_codec)cinfo.coef;
			lhuff_entropy_decoder entropy=(lhuff_entropy_decoder)losslsd.entropy_private;

			// Throw away any unused bits remaining in bit buffer;
			// include any full bytes in next_marker's count of discarded bytes
			cinfo.marker.discarded_bytes+=(uint)(entropy.bitstate.bits_left/8);
			entropy.bitstate.bits_left=0;

			// Advance past the RSTn marker
			if(!cinfo.marker.read_restart_marker(cinfo)) return false;

			// Reset out-of-data flag, unless read_restart_marker left us smack up
			// against a marker. In that case we will end up treating the next data
			// segment as empty, and we can avoid producing bogus output pixels by
			// leaving the flag set.
			if(cinfo.unread_marker==0) entropy.insufficient_data=false;

			return true;
		}
		
		// Decode and return nMCU's worth of Huffman-compressed differences.
		// Each MCU is also disassembled and placed accordingly in diff_buf.
		//
		// MCU_col_num specifies the column of the first MCU being requested within
		// the MCU-row. This tells us where to position the output row pointers in
		// diff_buf.
		//
		// Returns the number of MCUs decoded. This may be less than nMCU if data
		// source requested suspension. In that case no changes have been made to
		// permanent state. (Exception: some output differences may already have
		// been assigned. This is harmless for this module, since we'll just
		// re-assign them on the next call.)
		static uint decode_mcus_dlhuff(jpeg_decompress cinfo, int[][][] diff_buf, uint MCU_row_num, uint MCU_col_num, uint nMCU)
		{
			jpeg_lossless_d_codec losslsd=(jpeg_lossless_d_codec)cinfo.coef;
			lhuff_entropy_decoder entropy=(lhuff_entropy_decoder)losslsd.entropy_private;

			// Set output pointer locations based on MCU_col_num
			for(int ptrn=0; ptrn<entropy.num_output_ptrs; ptrn++)
			{
				int ci=entropy.output_ptr_info[ptrn].ci;
				int yoffset=entropy.output_ptr_info[ptrn].yoffset;
				int MCU_width=entropy.output_ptr_info[ptrn].MCU_width;
				entropy.output_ptr[ptrn]=diff_buf[ci][MCU_row_num+yoffset];
				entropy.output_ptr_ind[ptrn]=(int)(MCU_col_num*MCU_width);
			}

			// If we've run out of data, zero out the buffers and return.
			// By resetting the undifferencer, the output samples will be CENTERJSAMPLE.
			//
			// NB: We should find a way to do this without interacting with the
			// undifferencer module directly.
			if(entropy.insufficient_data)
			{
				for(int ptrn=0; ptrn<entropy.num_output_ptrs; ptrn++)
				{
					for(int i=0; i<nMCU*entropy.output_ptr_info[ptrn].MCU_width; i++) entropy.output_ptr[ptrn][entropy.output_ptr_ind[ptrn]+i]=0;
				}

				losslsd.predict_process_restart(cinfo);
			}

			else
			{
				// Load up working state

				//was BITREAD_STATE_VARS;
				bitread_working_state br_state=new bitread_working_state();

				//was BITREAD_LOAD_STATE(cinfo, entropy.bitstate);
				br_state.cinfo=cinfo;
				br_state.input_bytes=cinfo.src.input_bytes;
				br_state.next_input_byte=cinfo.src.next_input_byte;
				br_state.bytes_in_buffer=cinfo.src.bytes_in_buffer;
				ulong get_buffer=entropy.bitstate.get_buffer;
				int bits_left=entropy.bitstate.bits_left;

				// Outer loop handles the number of MCU requested
				for(uint mcu_num=0; mcu_num<nMCU; mcu_num++)
				{
					// Inner loop handles the samples in the MCU
					for(int sampn=0; sampn<cinfo.blocks_in_MCU; sampn++)
					{
						d_derived_tbl dctbl=entropy.cur_tbls[sampn];
						int s=0, r;

						// Section H.2.2: decode the sample difference
						//was HUFF_DECODE(s, br_state, dctbl, return mcu_num, label1);
						{
							int nb, look;
							bool label=false;
							if(bits_left<HUFF_LOOKAHEAD)
							{
								if(!jpeg_fill_bit_buffer(ref br_state, get_buffer, bits_left, 0)) return mcu_num;
								get_buffer=br_state.get_buffer;
								bits_left=br_state.bits_left;
								if(bits_left<HUFF_LOOKAHEAD)
								{
									nb=1;
									label=true;
									if((s=jpeg_huff_decode(ref br_state, get_buffer, bits_left, dctbl, nb))<0) return mcu_num;
									get_buffer=br_state.get_buffer;
									bits_left=br_state.bits_left;
								}
							}

							if(!label)
							{
								//was look=PEEK_BITS(HUFF_LOOKAHEAD);
								look=((int)(get_buffer>>(bits_left-HUFF_LOOKAHEAD)))&((1<<HUFF_LOOKAHEAD)-1);
								if((nb=dctbl.look_nbits[look])!=0)
								{
									//was DROP_BITS(nb);
									bits_left-=nb;
									s=dctbl.look_sym[look];
								}
								else
								{
									nb=HUFF_LOOKAHEAD+1;
									if((s=jpeg_huff_decode(ref br_state, get_buffer, bits_left, dctbl, nb))<0) return mcu_num;
									get_buffer=br_state.get_buffer;
									bits_left=br_state.bits_left;
								}
							}
						}

						if(s!=0)
						{
							if(s==16) s=32768; // special case: always output 32768
							else
							{ // normal case: fetch subsequent bits
								//was CHECK_BIT_BUFFER(br_state, s, return mcu_num);
								if(bits_left<s)
								{
									if(!jpeg_fill_bit_buffer(ref br_state, get_buffer, bits_left, s)) return mcu_num;
									get_buffer=br_state.get_buffer; bits_left=br_state.bits_left;
								}

								//was r = GET_BITS(s);
								r=((int)(get_buffer>>(bits_left-=s)))&((1<<s)-1);
								//was s=HUFF_EXTEND(r, s);
								s=(r<(1<<(s-1))?r+(((-1)<<s)+1):r);
							}
						}

						// Output the sample difference
						int ind=entropy.output_ptr_index[sampn];
						entropy.output_ptr[ind][entropy.output_ptr_ind[ind]++]=(int)s;
					}

					// Completed MCU, so update state
					//was BITREAD_SAVE_STATE(cinfo, entropy.bitstate);
					cinfo.src.input_bytes=br_state.input_bytes;
					cinfo.src.next_input_byte=br_state.next_input_byte;
					cinfo.src.bytes_in_buffer=br_state.bytes_in_buffer;
					entropy.bitstate.get_buffer=get_buffer;
					entropy.bitstate.bits_left=bits_left;
				}
			}

			return nMCU;
		}

		// Module initialization routine for lossless Huffman entropy decoding.
		public static void jinit_lhuff_decoder(jpeg_decompress cinfo)
		{
			jpeg_lossless_d_codec losslsd=(jpeg_lossless_d_codec)cinfo.coef;
			lhuff_entropy_decoder entropy=null;

			try
			{
				entropy=new lhuff_entropy_decoder();
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}
			losslsd.entropy_private=entropy;
			losslsd.entropy_start_pass=start_pass_lhuff_decoder;
			losslsd.entropy_process_restart=process_restart_dlhuff;
			losslsd.entropy_decode_mcus=decode_mcus_dlhuff;

			// Mark tables unallocated
			for(int i=0; i<NUM_HUFF_TBLS; i++) entropy.derived_tbls[i]=null;
		}
	}
}
#endif // D_LOSSLESS_SUPPORTED
