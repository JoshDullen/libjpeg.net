#if D_PROGRESSIVE_SUPPORTED
// jdphuff.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1995-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains Huffman entropy decoding routines for progressive JPEG.
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
		// Expanded entropy decoder object for progressive Huffman decoding.
		//
		// The savable_state subrecord contains fields that change within an MCU,
		// but must not be updated permanently until we complete the MCU.
		struct savable_state
		{
			public uint EOBRUN;		// remaining EOBs in EOBRUN
			public int[] last_dc_val; // last DC coef for each component
		}

		class phuff_entropy_decoder : jpeg_entropy_decoder
		{
			// These fields are loaded into local variables at start of each MCU.
			// In case of suspension, we exit WITHOUT updating them.
			//+ public bitread_perm_state bitstate;	// Bit buffer at start of MCU => moved to jpeg_entropy_decoder
			public savable_state saved;	// Other state at start of MCU

			// These fields are NOT loaded into local working state.
			public uint restarts_to_go;	// MCUs left in this restart interval

			// Pointers to derived tables (these workspaces have image lifespan)
			public d_derived_tbl[] derived_tbls=new d_derived_tbl[NUM_HUFF_TBLS];

			public d_derived_tbl ac_derived_tbl; // active table during an AC scan
		}

		// Initialize for a Huffman-compressed scan.
		static void start_pass_phuff_decoder(jpeg_decompress cinfo)
		{
			jpeg_lossy_d_codec lossyd=(jpeg_lossy_d_codec)cinfo.coef;
			phuff_entropy_decoder entropy=(phuff_entropy_decoder)lossyd.entropy_private;

			bool is_DC_band=(cinfo.Ss==0);

			// Validate scan parameters
			bool bad=false;
			if(is_DC_band)
			{
				if(cinfo.Se!=0) bad=true;
			}
			else
			{
				// need not check Ss/Se < 0 since they came from unsigned bytes
				if(cinfo.Ss>cinfo.Se||cinfo.Se>=DCTSIZE2) bad=true;
				// AC scans may have only one component
				if(cinfo.comps_in_scan!=1) bad=true;
			}

			if(cinfo.Ah!=0)
			{
				// Successive approximation refinement scan: must have Al = Ah-1.
				if(cinfo.Al!=cinfo.Ah-1) bad=true;
			}

			// Arguably the maximum Al value should be less than 13 for 8-bit precision,
			// but the spec doesn't say so, and we try to be liberal about what we
			// accept. Note: large Al values could result in out-of-range DC
			// coefficients during early scans, leading to bizarre displays due to
			// overflows in the IDCT math. But we won't crash.
			if(cinfo.Al>13) bad=true;	// need not check for < 0

			if(bad) ERREXIT4(cinfo, J_MESSAGE_CODE.JERR_BAD_PROGRESSION, cinfo.Ss, cinfo.Se, cinfo.Ah, cinfo.Al);

			// Update progression status, and verify that scan order is legal.
			// Note that inter-scan inconsistencies are treated as warnings
			// not fatal errors ... not clear if this is right way to behave.
			for(int ci=0; ci<cinfo.comps_in_scan; ci++)
			{
				int cindex=cinfo.cur_comp_info[ci].component_index;

				int[] coef_bit_ptr=cinfo.coef_bits[cindex];
				if(!is_DC_band&&coef_bit_ptr[0]<0) WARNMS2(cinfo, J_MESSAGE_CODE.JWRN_BOGUS_PROGRESSION, cindex, 0); // AC without prior DC scan

				for(int coefi=cinfo.Ss; coefi<=cinfo.Se; coefi++)
				{
					int expected=(coef_bit_ptr[coefi]<0)?0:coef_bit_ptr[coefi];
					if(cinfo.Ah!=expected) WARNMS2(cinfo, J_MESSAGE_CODE.JWRN_BOGUS_PROGRESSION, cindex, coefi);
					coef_bit_ptr[coefi]=cinfo.Al;
				}
			}

			// Select MCU decoding routine
			if(cinfo.Ah==0)
			{
				if(is_DC_band) lossyd.entropy_decode_mcu=decode_mcu_DC_first;
				else lossyd.entropy_decode_mcu=decode_mcu_AC_first;
			}
			else
			{
				if(is_DC_band) lossyd.entropy_decode_mcu=decode_mcu_DC_refine;
				else lossyd.entropy_decode_mcu=decode_mcu_AC_refine;
			}

			for(int ci=0; ci<cinfo.comps_in_scan; ci++)
			{
				jpeg_component_info compptr=cinfo.cur_comp_info[ci];
				// Make sure requested tables are present, and compute derived tables.
				// We may build same derived table more than once, but it's not expensive.
				if(is_DC_band)
				{
					if(cinfo.Ah==0)
					{ // DC refinement needs no table
						int tbl=compptr.dc_tbl_no;
						jpeg_make_d_derived_tbl(cinfo, true, tbl, ref entropy.derived_tbls[tbl]);
					}
				}
				else
				{
					int tbl=compptr.ac_tbl_no;
					jpeg_make_d_derived_tbl(cinfo, false, tbl, ref entropy.derived_tbls[tbl]);

					// remember the single active table
					entropy.ac_derived_tbl=entropy.derived_tbls[tbl];
				}
				// Initialize DC predictions to 0
				entropy.saved.last_dc_val[ci]=0;
			}

			// Initialize bitread state variables
			entropy.bitstate.bits_left=0;
			entropy.bitstate.get_buffer=0; // unnecessary, but keeps Purify quiet
			entropy.insufficient_data=false;

			// Initialize private state variables
			entropy.saved.EOBRUN=0;

			// Initialize restart counter
			entropy.restarts_to_go=cinfo.restart_interval;
		}

		//#define HUFF_EXTEND(x,s) ((x) < (1<<((s)-1)) ? (x) + (((-1)<<(s)) + 1) : (x))

		// Check for a restart marker & resynchronize decoder.
		// Returns false if must suspend.
		static bool process_restart_dphuff(jpeg_decompress cinfo)
		{
			jpeg_lossy_d_codec lossyd=(jpeg_lossy_d_codec)cinfo.coef;
			phuff_entropy_decoder entropy=(phuff_entropy_decoder)lossyd.entropy_private;

			// Throw away any unused bits remaining in bit buffer;
			// include any full bytes in next_marker's count of discarded bytes
			cinfo.marker.discarded_bytes+=(uint)(entropy.bitstate.bits_left/8);
			entropy.bitstate.bits_left=0;

			// Advance past the RSTn marker
			if(!cinfo.marker.read_restart_marker(cinfo)) return false;

			// Re-initialize DC predictions to 0
			for(int ci=0; ci<cinfo.comps_in_scan; ci++) entropy.saved.last_dc_val[ci]=0;

			// Re-init EOB run count, too
			entropy.saved.EOBRUN=0;

			// Reset restart counter
			entropy.restarts_to_go=cinfo.restart_interval;

			// Reset out-of-data flag, unless read_restart_marker left us smack up
			// against a marker. In that case we will end up treating the next data
			// segment as empty, and we can avoid producing bogus output pixels by
			// leaving the flag set.
			if(cinfo.unread_marker==0) entropy.insufficient_data=false;

			return true;
		}

		// Huffman MCU decoding.
		// Each of these routines decodes and returns one MCU's worth of
		// Huffman-compressed coefficients. 
		// The coefficients are reordered from zigzag order into natural array order,
		// but are not dequantized.
		//
		// The i'th block of the MCU is stored into the block pointed to by
		// MCU_data[i]. WE ASSUME THIS AREA IS INITIALLY ZEROED BY THE CALLER.
		//
		// We return false if data source requested suspension. In that case no
		// changes have been made to permanent state. (Exception: some output
		// coefficients may already have been assigned. This is harmless for
		// spectral selection, since we'll just re-assign them on the next call.
		// Successive approximation AC refinement has to be more careful, however.)
		
		// MCU decoding for DC initial scan (either spectral selection,
		// or first pass of successive approximation).
		static bool decode_mcu_DC_first(jpeg_decompress cinfo, short[][] MCU_data)
		{
			jpeg_lossy_d_codec lossyd=(jpeg_lossy_d_codec)cinfo.coef;
			phuff_entropy_decoder entropy=(phuff_entropy_decoder)lossyd.entropy_private;
			int Al=cinfo.Al;

			// Process restart marker if needed; may have to suspend
			if(cinfo.restart_interval!=0)
			{
				if(entropy.restarts_to_go==0)
					if(!process_restart_dphuff(cinfo)) return false;
			}

			// If we've run out of data, just leave the MCU set to zeroes.
			// This way, we return uniform gray for the remainder of the segment.
			if(!entropy.insufficient_data)
			{
				// Load up working state
				//was BITREAD_STATE_VARS;
				bitread_working_state br_state=new bitread_working_state();

				savable_state state;
				state.last_dc_val=new int[MAX_COMPS_IN_SCAN];

				//was BITREAD_LOAD_STATE(cinfo, entropy.bitstate);
				br_state.cinfo=cinfo;
				br_state.input_bytes=cinfo.src.input_bytes;
				br_state.next_input_byte=cinfo.src.next_input_byte;
				br_state.bytes_in_buffer=cinfo.src.bytes_in_buffer;
				ulong get_buffer=entropy.bitstate.get_buffer;
				int bits_left=entropy.bitstate.bits_left;

				//was state=entropy.saved;
				state.EOBRUN=entropy.saved.EOBRUN;
				entropy.saved.last_dc_val.CopyTo(state.last_dc_val, 0);

				// Outer loop handles each block in the MCU
				for(int blkn=0; blkn<cinfo.blocks_in_MCU; blkn++)
				{
					short[] block=MCU_data[blkn];
					int ci=cinfo.MCU_membership[blkn];
					jpeg_component_info compptr=cinfo.cur_comp_info[ci];
					d_derived_tbl tbl=entropy.derived_tbls[compptr.dc_tbl_no];

					int s=0;

					// Decode a single block's worth of coefficients

					// Section F.2.2.1: decode the DC coefficient difference
					//was HUFF_DECODE(s, br_state, tbl, return false, label1);
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
								if((s=jpeg_huff_decode(ref br_state, get_buffer, bits_left, tbl, nb))<0) return false;
								get_buffer=br_state.get_buffer;
								bits_left=br_state.bits_left;
							}
						}

						if(!label)
						{
							//was look=PEEK_BITS(HUFF_LOOKAHEAD);
							look=((int)(get_buffer>>(bits_left-HUFF_LOOKAHEAD)))&((1<<HUFF_LOOKAHEAD)-1);
							if((nb=tbl.look_nbits[look])!=0)
							{
								//was DROP_BITS(nb);
								bits_left-=nb;
								s=tbl.look_sym[look];
							}
							else
							{
								nb=HUFF_LOOKAHEAD+1;
								if((s=jpeg_huff_decode(ref br_state, get_buffer, bits_left, tbl, nb))<0) return false;
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
						int r=((int)(get_buffer>>(bits_left-=s)))&((1<<s)-1);
						//was s=HUFF_EXTEND(r, s);
						s=(r<(1<<(s-1))?r+(((-1)<<s)+1):r);
					}

					// Convert DC difference to actual value, update last_dc_val
					s+=state.last_dc_val[ci];
					state.last_dc_val[ci]=s;
					// Scale and output the coefficient (assumes jpeg_natural_order[0]=0)
					block[0]=(short)(s<<Al);
				}

				// Completed MCU, so update state
				//was BITREAD_SAVE_STATE(cinfo, entropy.bitstate);
				cinfo.src.input_bytes=br_state.input_bytes;
				cinfo.src.next_input_byte=br_state.next_input_byte;
				cinfo.src.bytes_in_buffer=br_state.bytes_in_buffer;
				entropy.bitstate.get_buffer=get_buffer;
				entropy.bitstate.bits_left=bits_left;

				//was entropy.saved=state;
				entropy.saved.EOBRUN=state.EOBRUN;
				state.last_dc_val.CopyTo(entropy.saved.last_dc_val, 0);
			}

			// Account for restart interval (no-op if not using restarts)
			entropy.restarts_to_go--;

			return true;
		}

		// MCU decoding for AC initial scan (either spectral selection,
		// or first pass of successive approximation).
		static bool decode_mcu_AC_first(jpeg_decompress cinfo, short[][] MCU_data)
		{
			jpeg_lossy_d_codec lossyd=(jpeg_lossy_d_codec)cinfo.coef;
			phuff_entropy_decoder entropy=(phuff_entropy_decoder)lossyd.entropy_private;
			int Se=cinfo.Se;
			int Al=cinfo.Al;

			// Process restart marker if needed; may have to suspend
			if(cinfo.restart_interval!=0)
			{
				if(entropy.restarts_to_go==0)
					if(!process_restart_dphuff(cinfo)) return false;
			}

			// If we've run out of data, just leave the MCU set to zeroes.
			// This way, we return uniform gray for the remainder of the segment.
			if(!entropy.insufficient_data)
			{
				// Load up working state.
				// We can avoid loading/saving bitread state if in an EOB run.
				uint EOBRUN=entropy.saved.EOBRUN; // only part of saved state we need

				// There is always only one block per MCU
				if(EOBRUN>0) EOBRUN--; // if it's a band of zeroes... ...process it now (we do nothing)
				else
				{
					//was BITREAD_STATE_VARS;
					bitread_working_state br_state=new bitread_working_state();

					//was BITREAD_LOAD_STATE(cinfo, entropy.bitstate);
					br_state.cinfo=cinfo;
					br_state.input_bytes=cinfo.src.input_bytes;
					br_state.next_input_byte=cinfo.src.next_input_byte;
					br_state.bytes_in_buffer=cinfo.src.bytes_in_buffer;
					ulong get_buffer=entropy.bitstate.get_buffer;
					int bits_left=entropy.bitstate.bits_left;
					short[] block=MCU_data[0];
					d_derived_tbl tbl=entropy.ac_derived_tbl;

					for(int k=cinfo.Ss; k<=Se; k++)
					{
						int s=0, r;

						//was HUFF_DECODE(s, br_state, tbl, return false, label2);
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
									if((s=jpeg_huff_decode(ref br_state, get_buffer, bits_left, tbl, nb))<0) return false;
									get_buffer=br_state.get_buffer;
									bits_left=br_state.bits_left;
								}
							}

							if(!label)
							{
								//was look=PEEK_BITS(HUFF_LOOKAHEAD);
								look=((int)(get_buffer>>(bits_left-HUFF_LOOKAHEAD)))&((1<<HUFF_LOOKAHEAD)-1);
								if((nb=tbl.look_nbits[look])!=0)
								{
									//was DROP_BITS(nb);
									bits_left-=nb;
									s=tbl.look_sym[look];
								}
								else
								{
									nb=HUFF_LOOKAHEAD+1;
									if((s=jpeg_huff_decode(ref br_state, get_buffer, bits_left, tbl, nb))<0) return false;
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

							// Scale and output coefficient in natural (dezigzagged) order
							block[jpeg_natural_order[k]]=(short)(s<<Al);
						}
						else
						{
							if(r==15)
							{ // ZRL
								k+=15; // skip 15 zeroes in band
							}
							else
							{ // EOBr, run length is 2^r + appended bits
								EOBRUN=(uint)(1<<r);
								if(r!=0)
								{ // EOBr, r > 0
									//was CHECK_BIT_BUFFER(br_state, r, return false);
									if(bits_left<r)
									{
										if(!jpeg_fill_bit_buffer(ref br_state, get_buffer, bits_left, r)) return false;
										get_buffer=br_state.get_buffer; bits_left=br_state.bits_left;
									}
									//was r = GET_BITS(r);
									r=((int)(get_buffer>>(bits_left-=r)))&((1<<r)-1);
									EOBRUN+=(uint)r;
								}
								EOBRUN--;	// this band is processed at this moment
								break;		// force end-of-band
							}
						}
					}

					//was BITREAD_SAVE_STATE(cinfo, entropy.bitstate);
					cinfo.src.input_bytes=br_state.input_bytes;
					cinfo.src.next_input_byte=br_state.next_input_byte;
					cinfo.src.bytes_in_buffer=br_state.bytes_in_buffer;
					entropy.bitstate.get_buffer=get_buffer;
					entropy.bitstate.bits_left=bits_left;
				}

				// Completed MCU, so update state
				entropy.saved.EOBRUN=EOBRUN; // only part of saved state we need
			}

			// Account for restart interval (no-op if not using restarts)
			entropy.restarts_to_go--;

			return true;
		}

		// MCU decoding for DC successive approximation refinement scan.
		// Note: we assume such scans can be multi-component, although the spec
		// is not very clear on the point.
		static bool decode_mcu_DC_refine(jpeg_decompress cinfo, short[][] MCU_data)
		{
			jpeg_lossy_d_codec lossyd=(jpeg_lossy_d_codec)cinfo.coef;
			phuff_entropy_decoder entropy=(phuff_entropy_decoder)lossyd.entropy_private;
			short p1=(short)(1<<cinfo.Al);	// 1 in the bit position being coded

			// Process restart marker if needed; may have to suspend
			if(cinfo.restart_interval!=0)
			{
				if(entropy.restarts_to_go==0)
					if(!process_restart_dphuff(cinfo)) return false;
			}

			// Not worth the cycles to check insufficient_data here,
			// since we will not change the data anyway if we read zeroes.

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

			// Outer loop handles each block in the MCU
			for(int blkn=0; blkn<cinfo.blocks_in_MCU; blkn++)
			{
				short[] block=MCU_data[blkn];

				// Encoded data is simply the next bit of the two's-complement DC value
				//was CHECK_BIT_BUFFER(br_state, 1, return false);
				if(bits_left<1)
				{
					if(!jpeg_fill_bit_buffer(ref br_state, get_buffer, bits_left, 1)) return false;
					get_buffer=br_state.get_buffer; bits_left=br_state.bits_left;
				}

				//was if(GET_BITS(1))
				if((((int)(get_buffer>>(bits_left-=1)))&1)!=0) block[0]|=p1;
				// Note: since we use |=, repeating the assignment later is safe
			}

			// Completed MCU, so update state
			//was BITREAD_SAVE_STATE(cinfo, entropy.bitstate);
			cinfo.src.input_bytes=br_state.input_bytes;
			cinfo.src.next_input_byte=br_state.next_input_byte;
			cinfo.src.bytes_in_buffer=br_state.bytes_in_buffer;
			entropy.bitstate.get_buffer=get_buffer;
			entropy.bitstate.bits_left=bits_left;

			// Account for restart interval (no-op if not using restarts)
			entropy.restarts_to_go--;

			return true;
		}

		// MCU decoding for AC successive approximation refinement scan.
		static bool decode_mcu_AC_refine(jpeg_decompress cinfo, short[][] MCU_data)
		{
			jpeg_lossy_d_codec lossyd=(jpeg_lossy_d_codec)cinfo.coef;
			phuff_entropy_decoder entropy=(phuff_entropy_decoder)lossyd.entropy_private;
			int Se=cinfo.Se;
			short p1=(short)(1<<cinfo.Al);		// 1 in the bit position being coded
			short m1=(short)((-1)<<cinfo.Al);	// -1 in the bit position being coded
			short[] block=null;

			// If we are forced to suspend, we must undo the assignments to any newly
			// nonzero coefficients in the block, because otherwise we'd get confused
			// next time about which coefficients were already nonzero.
			// But we need not undo addition of bits to already-nonzero coefficients;
			// instead, we can test the current bit to see if we already did it.
			int num_newnz=0;

			int[] newnz_pos=new int[DCTSIZE2];

			// Process restart marker if needed; may have to suspend
			if(cinfo.restart_interval!=0)
			{
				if(entropy.restarts_to_go==0)
					if(!process_restart_dphuff(cinfo)) return false;
			}

			// If we've run out of data, don't modify the MCU.
			if(!entropy.insufficient_data)
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
				uint EOBRUN=entropy.saved.EOBRUN; // only part of saved state we need

				// There is always only one block per MCU
				block=MCU_data[0];
				d_derived_tbl tbl=entropy.ac_derived_tbl;

				// initialize coefficient loop counter to start of band
				int k=cinfo.Ss;

				if(EOBRUN==0)
				{
					for(; k<=Se; k++)
					{
						int s=0, r;

						//was HUFF_DECODE(s, br_state, tbl, goto undoit, label3);
						{
							int nb, look;
							bool label=false;
							if(bits_left<HUFF_LOOKAHEAD)
							{
								if(!jpeg_fill_bit_buffer(ref br_state, get_buffer, bits_left, 0)) goto undoit;
								get_buffer=br_state.get_buffer;
								bits_left=br_state.bits_left;
								if(bits_left<HUFF_LOOKAHEAD)
								{
									nb=1;
									label=true;
									if((s=jpeg_huff_decode(ref br_state, get_buffer, bits_left, tbl, nb))<0) goto undoit;
									get_buffer=br_state.get_buffer;
									bits_left=br_state.bits_left;
								}
							}

							if(!label)
							{
								//was look=PEEK_BITS(HUFF_LOOKAHEAD);
								look=((int)(get_buffer>>(bits_left-HUFF_LOOKAHEAD)))&((1<<HUFF_LOOKAHEAD)-1);
								if((nb=tbl.look_nbits[look])!=0)
								{
									//was DROP_BITS(nb);
									bits_left-=nb;
									s=tbl.look_sym[look];
								}
								else
								{
									nb=HUFF_LOOKAHEAD+1;
									if((s=jpeg_huff_decode(ref br_state, get_buffer, bits_left, tbl, nb))<0) goto undoit;
									get_buffer=br_state.get_buffer;
									bits_left=br_state.bits_left;
								}
							}
						}

						r=s>>4;
						s&=15;
						if(s!=0)
						{
							if(s!=1) WARNMS(cinfo, J_MESSAGE_CODE.JWRN_HUFF_BAD_CODE); // size of new coef should always be 1
							//was CHECK_BIT_BUFFER(br_state, 1, goto undoit);
							if(bits_left<1)
							{
								if(!jpeg_fill_bit_buffer(ref br_state, get_buffer, bits_left, 1)) goto undoit;
								get_buffer=br_state.get_buffer; bits_left=br_state.bits_left;
							}
							//was if (GET_BITS(1))
							if((((int)(get_buffer>>(bits_left-=1)))&1)!=0) s=p1; // newly nonzero coef is positive
							else s=m1; // newly nonzero coef is negative
						}
						else
						{
							if(r!=15)
							{
								EOBRUN=(uint)(1<<r); // EOBr, run length is 2^r + appended bits
								if(r!=0)
								{
									//was CHECK_BIT_BUFFER(br_state, r, goto undoit);
									if(bits_left<r)
									{
										if(!jpeg_fill_bit_buffer(ref br_state, get_buffer, bits_left, r)) goto undoit;
										get_buffer=br_state.get_buffer; bits_left=br_state.bits_left;
									}
									//was r = GET_BITS(r);
									r=((int)(get_buffer>>(bits_left-=r)))&((1<<r)-1);
									EOBRUN+=(uint)r;
								}
								break; // rest of block is handled by EOB logic
							}
							// note s = 0 for processing ZRL
						}
						// Advance over already-nonzero coefs and r still-zero coefs,
						// appending correction bits to the nonzeroes. A correction bit is 1
						// if the absolute value of the coefficient must be increased.
						do
						{
							int thiscoef=jpeg_natural_order[k];
							if(block[thiscoef]!=0)
							{
								//was CHECK_BIT_BUFFER(br_state, 1, goto undoit);
								if(bits_left<1)
								{
									if(!jpeg_fill_bit_buffer(ref br_state, get_buffer, bits_left, 1)) goto undoit;
									get_buffer=br_state.get_buffer; bits_left=br_state.bits_left;
								}
								//was if (GET_BITS(1))
								if((((int)(get_buffer>>(bits_left-=1)))&1)!=0)
								{
									if((block[thiscoef]&p1)==0)
									{ // do nothing if already set it
										if(block[thiscoef]>=0) block[thiscoef]+=p1;
										else block[thiscoef]+=m1;
									}
								}
							}
							else
							{
								if(--r<0) break; // reached target zero coefficient
							}
							k++;
						} while(k<=Se);

						if(s!=0)
						{
							int pos=jpeg_natural_order[k];
							// Output newly nonzero coefficient
							block[pos]=(short)s;
							// Remember its position in case we have to suspend
							newnz_pos[num_newnz++]=pos;
						}
					}
				}

				if(EOBRUN>0)
				{
					// Scan any remaining coefficient positions after the end-of-band
					// (the last newly nonzero coefficient, if any). Append a correction
					// bit to each already-nonzero coefficient. A correction bit is 1
					// if the absolute value of the coefficient must be increased.
					for(; k<=Se; k++)
					{
						int thiscoef=jpeg_natural_order[k];
						if(block[thiscoef]!=0)
						{
							//was CHECK_BIT_BUFFER(br_state, 1, goto undoit);
							if(bits_left<1)
							{
								if(!jpeg_fill_bit_buffer(ref br_state, get_buffer, bits_left, 1)) goto undoit;
								get_buffer=br_state.get_buffer; bits_left=br_state.bits_left;
							}
							//was if (GET_BITS(1))
							if((((int)(get_buffer>>(bits_left-=1)))&1)!=0)
							{
								if((block[thiscoef]&p1)==0)
								{ // do nothing if already changed it
									if(block[thiscoef]>=0) block[thiscoef]+=p1;
									else block[thiscoef]+=m1;
								}
							}
						}
					}
					// Count one block completed in EOB run
					EOBRUN--;
				}

				// Completed MCU, so update state
				//was BITREAD_SAVE_STATE(cinfo, entropy.bitstate);
				cinfo.src.input_bytes=br_state.input_bytes;
				cinfo.src.next_input_byte=br_state.next_input_byte;
				cinfo.src.bytes_in_buffer=br_state.bytes_in_buffer;
				entropy.bitstate.get_buffer=get_buffer;
				entropy.bitstate.bits_left=bits_left;
				entropy.saved.EOBRUN=EOBRUN; // only part of saved state we need
			}

			// Account for restart interval (no-op if not using restarts)
			entropy.restarts_to_go--;

			return true;

undoit:
			// Re-zero any output coefficients that we made newly nonzero
			while(num_newnz>0) block[newnz_pos[--num_newnz]]=0;

			return false;
		}

		// Module initialization routine for progressive Huffman entropy decoding.
		public static void jinit_phuff_decoder(jpeg_decompress cinfo)
		{
			jpeg_lossy_d_codec lossyd=(jpeg_lossy_d_codec)cinfo.coef;
			phuff_entropy_decoder entropy=null;

			try
			{
				entropy=new phuff_entropy_decoder();
				entropy.saved.last_dc_val=new int[MAX_COMPS_IN_SCAN];
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}
			lossyd.entropy_private=entropy;
			lossyd.entropy_start_pass=start_pass_phuff_decoder;

			// Mark derived tables unallocated
			for(int i=0; i<NUM_HUFF_TBLS; i++) entropy.derived_tbls[i]=null;

			try
			{
				// Create progression status table
				cinfo.coef_bits=new int[cinfo.num_components][];

				for(int ci=0; ci<cinfo.num_components; ci++)
				{
					int[] coef_bit_ptr=cinfo.coef_bits[ci]=new int[DCTSIZE2];
					for(int i=0; i<DCTSIZE2; i++) coef_bit_ptr[i]=-1;
				}
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}
		}
	}
}
#endif // D_PROGRESSIVE_SUPPORTED