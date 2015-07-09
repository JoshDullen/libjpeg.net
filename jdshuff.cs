// jdshuff.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1991-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains Huffman entropy decoding routines for sequential JPEG.
//
// Much of the complexity here has to do with supporting input suspension.
// If the data source module demands suspension, we want to be able to back
// up to the start of the current MCU. To do this, we copy state variables
// into local working storage, and update them back to the permanent
// storage only upon successful completion of an MCU.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Private entropy decoder object for Huffman decoding.
		//
		// The savable_state subrecord contains fields that change within an MCU,
		// but must not be updated permanently until we complete the MCU.
		//struct savable_state_sq
		//{
		//	public int[] last_dc_val=new int[MAX_COMPS_IN_SCAN]; // last DC coef for each component
		//}

		class shuff_entropy_decoder : jpeg_entropy_decoder
		{
			// These fields are loaded into local variables at start of each MCU.
			// In case of suspension, we exit WITHOUT updating them.
			public savable_state_sq saved;	// Other state at start of MCU

			// These fields are NOT loaded into local working state.
			public uint restarts_to_go;	// MCUs left in this restart interval

			// Pointers to derived tables (these workspaces have image lifespan)
			public d_derived_tbl[] dc_derived_tbls=new d_derived_tbl[NUM_HUFF_TBLS];
			public d_derived_tbl[] ac_derived_tbls=new d_derived_tbl[NUM_HUFF_TBLS];

			// Precalculated info set up by start_pass for use in decode_mcu:

			// Pointers to derived tables to be used for each block within an MCU
			public d_derived_tbl[] dc_cur_tbls=new d_derived_tbl[D_MAX_BLOCKS_IN_MCU];
			public d_derived_tbl[] ac_cur_tbls=new d_derived_tbl[D_MAX_BLOCKS_IN_MCU];

			// Whether we care about the DC and AC coefficient values for each block
			public bool[] dc_needed=new bool[D_MAX_BLOCKS_IN_MCU];
			public bool[] ac_needed=new bool[D_MAX_BLOCKS_IN_MCU];
		}

		// Initialize for a Huffman-compressed scan.
		static void start_pass_huff_decoder(jpeg_decompress cinfo)
		{
			jpeg_lossy_d_codec lossyd=(jpeg_lossy_d_codec)cinfo.coef;
			shuff_entropy_decoder entropy=(shuff_entropy_decoder)lossyd.entropy_private;

			// Check that the scan parameters Ss, Se, Ah/Al are OK for sequential JPEG.
			// This ought to be an error condition, but we make it a warning because
			// there are some baseline files out there with all zeroes in these bytes.
			if(cinfo.Ss!=0||cinfo.Se!=DCTSIZE2-1||cinfo.Ah!=0||cinfo.Al!=0) WARNMS(cinfo, J_MESSAGE_CODE.JWRN_NOT_SEQUENTIAL);

			for(int ci=0; ci<cinfo.comps_in_scan; ci++)
			{
				jpeg_component_info compptr=cinfo.cur_comp_info[ci];
				int dctbl=compptr.dc_tbl_no;
				int actbl=compptr.ac_tbl_no;

				// Compute derived values for Huffman tables
				// We may do this more than once for a table, but it's not expensive
				jpeg_make_d_derived_tbl(cinfo, true, dctbl, ref entropy.dc_derived_tbls[dctbl]);
				jpeg_make_d_derived_tbl(cinfo, false, actbl, ref entropy.ac_derived_tbls[actbl]);

				// Initialize DC predictions to 0
				entropy.saved.last_dc_val[ci]=0;
			}

			// Precalculate decoding info for each block in an MCU of this scan
			for(int blkn=0; blkn<cinfo.blocks_in_MCU; blkn++)
			{
				int ci=cinfo.MCU_membership[blkn];
				jpeg_component_info compptr=cinfo.cur_comp_info[ci];

				// Precalculate which table to use for each block
				entropy.dc_cur_tbls[blkn]=entropy.dc_derived_tbls[compptr.dc_tbl_no];
				entropy.ac_cur_tbls[blkn]=entropy.ac_derived_tbls[compptr.ac_tbl_no];

				// Decide whether we really care about the coefficient values
				if(compptr.component_needed)
				{
					entropy.dc_needed[blkn]=true;
					// we don't need the ACs if producing a 1/8th-size image
					entropy.ac_needed[blkn]=(compptr.DCT_scaled_size>1);
				}
				else entropy.dc_needed[blkn]=entropy.ac_needed[blkn]=false;
			}

			// Initialize bitread state variables
			entropy.bitstate.bits_left=0;
			entropy.bitstate.get_buffer=0; // unnecessary, but keeps Purify quiet
			entropy.insufficient_data=false;

			// Initialize restart counter
			entropy.restarts_to_go=cinfo.restart_interval;
		}

		//#define HUFF_EXTEND(x,s) ((x) < (1<<((s)-1)) ? (x) + (((-1)<<(s)) + 1) : (x))

		// Check for a restart marker & resynchronize decoder.
		// Returns false if must suspend.
		static bool process_restart_dshuff(jpeg_decompress cinfo)
		{
			jpeg_lossy_d_codec lossyd=(jpeg_lossy_d_codec)cinfo.coef;
			shuff_entropy_decoder entropy=(shuff_entropy_decoder)lossyd.entropy_private;

			// Throw away any unused bits remaining in bit buffer;
			// include any full bytes in next_marker's count of discarded bytes
			cinfo.marker.discarded_bytes+=(uint)(entropy.bitstate.bits_left/8);
			entropy.bitstate.bits_left=0;

			// Advance past the RSTn marker
			if(!cinfo.marker.read_restart_marker(cinfo)) return false;

			// Re-initialize DC predictions to 0
			for(int ci=0; ci<cinfo.comps_in_scan; ci++) entropy.saved.last_dc_val[ci]=0;

			// Reset restart counter
			entropy.restarts_to_go=cinfo.restart_interval;

			// Reset out-of-data flag, unless read_restart_marker left us smack up
			// against a marker. In that case we will end up treating the next data
			// segment as empty, and we can avoid producing bogus output pixels by
			// leaving the flag set.
			if(cinfo.unread_marker==0) entropy.insufficient_data=false;

			return true;
		}

		// Decode and return one MCU's worth of Huffman-compressed coefficients.
		// The coefficients are reordered from zigzag order into natural array order,
		// but are not dequantized.
		//
		// The i'th block of the MCU is stored into the block pointed to by
		// MCU_data[i]. WE ASSUME THIS AREA HAS BEEN ZEROED BY THE CALLER.
		// (Wholesale zeroing is usually a little faster than retail...)
		//
		// Returns false if data source requested suspension. In that case no
		// changes have been made to permanent state. (Exception: some output
		// coefficients may already have been assigned. This is harmless for
		// this module, since we'll just re-assign them on the next call.)
		static bool decode_mcu(jpeg_decompress cinfo, short[][] MCU_data)
		{
			jpeg_lossy_d_codec lossyd=(jpeg_lossy_d_codec)cinfo.coef;
			shuff_entropy_decoder entropy=(shuff_entropy_decoder)lossyd.entropy_private;

			// Process restart marker if needed; may have to suspend
			if(cinfo.restart_interval!=0)
			{
				if(entropy.restarts_to_go==0)
					if(!process_restart_dshuff(cinfo)) return false;
			}

			// If we've run out of data, just leave the MCU set to zeroes.
			// This way, we return uniform gray for the remainder of the segment.
			if(!entropy.insufficient_data)
			{
				bitread_working_state br_state=new bitread_working_state();

				savable_state_sq state;
				state.last_dc_val=new int[MAX_COMPS_IN_SCAN];

				// Load up working state
				//was BITREAD_STATE_VARS;
				//was BITREAD_LOAD_STATE(cinfo, entropy.bitstate);
				br_state.cinfo=cinfo;
				br_state.input_bytes=cinfo.src.input_bytes;
				br_state.next_input_byte=cinfo.src.next_input_byte;
				br_state.bytes_in_buffer=cinfo.src.bytes_in_buffer;
				ulong get_buffer=entropy.bitstate.get_buffer;
				int bits_left=entropy.bitstate.bits_left;

				//was state=entropy.saved;
				entropy.saved.last_dc_val.CopyTo(state.last_dc_val, 0);

				// Outer loop handles each block in the MCU
				for(int blkn=0; blkn<cinfo.blocks_in_MCU; blkn++)
				{
					short[] block=MCU_data[blkn];
					d_derived_tbl dctbl=entropy.dc_cur_tbls[blkn];
					d_derived_tbl actbl=entropy.ac_cur_tbls[blkn];
					int s=0, k, r;

					// Decode a single block's worth of coefficients

					// Section F.2.2.1: decode the DC coefficient difference
					//was HUFF_DECODE(s, br_state, dctbl, return false, label1);
					{
						int nb, look;
						bool label=false;
						if(bits_left<HUFF_LOOKAHEAD)
						{
							if(!jpeg_fill_bit_buffer(ref br_state, get_buffer, bits_left, 0)) return false;
							get_buffer=br_state.get_buffer;
							bits_left=br_state.bits_left;
							if(bits_left<HUFF_LOOKAHEAD)
							{
								nb=1;
								label=true;
								if((s=jpeg_huff_decode(ref br_state, get_buffer, bits_left, dctbl, nb))<0) return false;
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
								if((s=jpeg_huff_decode(ref br_state, get_buffer, bits_left, dctbl, nb))<0) return false;
								get_buffer=br_state.get_buffer;
								bits_left=br_state.bits_left;
							}
						}
					}

					if(s!=0)
					{
						//was CHECK_BIT_BUFFER(br_state, s, return false);
						if(bits_left<s)
						{
							if(!jpeg_fill_bit_buffer(ref br_state, get_buffer, bits_left, s)) return false;
							get_buffer=br_state.get_buffer; bits_left=br_state.bits_left;
						}
						//was r = GET_BITS(s);
						r=((int)(get_buffer>>(bits_left-=s)))&((1<<s)-1);
						//was s=HUFF_EXTEND(r, s);
						s=(r<(1<<(s-1))?r+(((-1)<<s)+1):r);
					}

					if(entropy.dc_needed[blkn])
					{
						// Convert DC difference to actual value, update last_dc_val
						int ci=cinfo.MCU_membership[blkn];
						s+=state.last_dc_val[ci];
						state.last_dc_val[ci]=s;
						// Output the DC coefficient (assumes jpeg_natural_order[0] = 0)
						block[0]=(short)s;
					}

					if(entropy.ac_needed[blkn])
					{
						// Section F.2.2.2: decode the AC coefficients
						// Since zeroes are skipped, output area must be cleared beforehand
						for(k=1; k<DCTSIZE2; k++)
						{
							//was HUFF_DECODE(s, br_state, actbl, return false, label2);
							{
								int nb, look;
								bool label=false;
								if(bits_left<HUFF_LOOKAHEAD)
								{
									if(!jpeg_fill_bit_buffer(ref br_state, get_buffer, bits_left, 0)) return false;
									get_buffer=br_state.get_buffer;
									bits_left=br_state.bits_left;
									if(bits_left<HUFF_LOOKAHEAD)
									{
										nb=1;
										label=true;
										if((s=jpeg_huff_decode(ref br_state, get_buffer, bits_left, actbl, nb))<0) return false;
										get_buffer=br_state.get_buffer;
										bits_left=br_state.bits_left;
									}
								}

								if(!label)
								{
									//was look=PEEK_BITS(HUFF_LOOKAHEAD);
									look=((int)(get_buffer>>(bits_left-HUFF_LOOKAHEAD)))&((1<<HUFF_LOOKAHEAD)-1);
									if((nb=actbl.look_nbits[look])!=0)
									{
										//was DROP_BITS(nb);
										bits_left-=nb;
										s=actbl.look_sym[look];
									}
									else
									{
										nb=HUFF_LOOKAHEAD+1;
										if((s=jpeg_huff_decode(ref br_state, get_buffer, bits_left, actbl, nb))<0) return false;
										get_buffer=br_state.get_buffer;
										bits_left=br_state.bits_left;
									}
								}
							}

							r=s>>4;
							s&=15;

							if(s!=0)
							{
								k+=r;
								//was CHECK_BIT_BUFFER(br_state, s, return false);
								if(bits_left<s)
								{
									if(!jpeg_fill_bit_buffer(ref br_state, get_buffer, bits_left, s)) return false;
									get_buffer=br_state.get_buffer; bits_left=br_state.bits_left;
								}
								//was r = GET_BITS(s);
								r=((int)(get_buffer>>(bits_left-=s)))&((1<<s)-1);
								//was s=HUFF_EXTEND(r, s);
								s=(r<(1<<(s-1))?r+(((-1)<<s)+1):r);


								// Output coefficient in natural (dezigzagged) order.
								// Note: the extra entries in jpeg_natural_order[] will save us
								// if k >= DCTSIZE2, which could happen if the data is corrupted.
								block[jpeg_natural_order[k]]=(short)s;
							}
							else
							{
								if(r!=15) break;
								k+=15;
							}
						}
					}
					else
					{
						// Section F.2.2.2: decode the AC coefficients
						// In this path we just discard the values
						for(k=1; k<DCTSIZE2; k++)
						{
							//was HUFF_DECODE(s, br_state, actbl, return false, label3);
							{
								int nb, look;
								bool label=false;
								if(bits_left<HUFF_LOOKAHEAD)
								{
									if(!jpeg_fill_bit_buffer(ref br_state, get_buffer, bits_left, 0)) return false;
									get_buffer=br_state.get_buffer;
									bits_left=br_state.bits_left;
									if(bits_left<HUFF_LOOKAHEAD)
									{
										nb=1;
										label=true;
										if((s=jpeg_huff_decode(ref br_state, get_buffer, bits_left, actbl, nb))<0) return false;
										get_buffer=br_state.get_buffer;
										bits_left=br_state.bits_left;

									}
								}

								if(!label)
								{
									//was look=PEEK_BITS(HUFF_LOOKAHEAD);
									look=((int)(get_buffer>>(bits_left-HUFF_LOOKAHEAD)))&((1<<HUFF_LOOKAHEAD)-1);
									if((nb=actbl.look_nbits[look])!=0)
									{
										//was DROP_BITS(nb);
										bits_left-=nb;
										s=actbl.look_sym[look];
									}
									else
									{
										nb=HUFF_LOOKAHEAD+1;
										if((s=jpeg_huff_decode(ref br_state, get_buffer, bits_left, actbl, nb))<0) return false;
										get_buffer=br_state.get_buffer;
										bits_left=br_state.bits_left;
									}
								}
							}

							r=s>>4;
							s&=15;

							if(s!=0)
							{
								k+=r;
								//was CHECK_BIT_BUFFER(br_state, s, return false);
								if(bits_left<s)
								{
									if(!jpeg_fill_bit_buffer(ref br_state, get_buffer, bits_left, s)) return false;
									get_buffer=br_state.get_buffer; bits_left=br_state.bits_left;
								}
								//was DROP_BITS(s);
								bits_left-=s;
							}
							else
							{
								if(r!=15) break;
								k+=15;
							}
						}
					}
				}

				// Completed MCU, so update state
				//was BITREAD_SAVE_STATE(cinfo, entropy.bitstate);
				cinfo.src.input_bytes=br_state.input_bytes;
				cinfo.src.next_input_byte=br_state.next_input_byte;
				cinfo.src.bytes_in_buffer=br_state.bytes_in_buffer;
				entropy.bitstate.get_buffer=get_buffer;
				entropy.bitstate.bits_left=bits_left;

				//was entropy.saved=state;
				state.last_dc_val.CopyTo(entropy.saved.last_dc_val, 0);
			}

			// Account for restart interval (no-op if not using restarts)
			entropy.restarts_to_go--;

			return true;
		}

		// Module initialization routine for Huffman entropy decoding.
		public static void jinit_shuff_decoder(jpeg_decompress cinfo)
		{
			jpeg_lossy_d_codec lossyd=(jpeg_lossy_d_codec)cinfo.coef;
			shuff_entropy_decoder entropy=null;

			try
			{
				entropy=new shuff_entropy_decoder();
				entropy.saved.last_dc_val=new int[MAX_COMPS_IN_SCAN];
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}
			lossyd.entropy_private=entropy;
			lossyd.entropy_start_pass=start_pass_huff_decoder;
			lossyd.entropy_decode_mcu=decode_mcu;

			// Mark tables unallocated
			for(int i=0; i<NUM_HUFF_TBLS; i++) entropy.dc_derived_tbls[i]=entropy.ac_derived_tbls[i]=null;
		}
	}
}
