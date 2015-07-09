// jdhuff.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1991-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains declarations for Huffman entropy decoding routines
// that are shared between the sequential decoder (jdhuff.cs), the
// progressive decoder (jdphuff.cs) and the lossless decoder (jdlhuff.cs).
// No other modules need to see these.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Derived data constructed for each Huffman table
		const int HUFF_LOOKAHEAD=8;	// # of bits of lookahead

		class d_derived_tbl
		{
			// Basic tables: (element [0] of each array is unused)
			public int[] maxcode=new int[18];	// largest code of length k (-1 if none)
			// (maxcode[17] is a sentinel to ensure jpeg_huff_decode terminates)

			public int[] valoffset=new int[17];	// huffval[] offset for codes of length k
			// valoffset[k] = huffval[] index of 1st symbol of code length k, less
			// the smallest code of length k; so given a code of length k, the
			// corresponding symbol is huffval[code + valoffset[k]]

			// Link to public Huffman table (needed only in jpeg_huff_decode)
			public JHUFF_TBL pub;

			// Lookahead tables: indexed by the next HUFF_LOOKAHEAD bits of
			// the input data stream. If the next Huffman code is no more
			// than HUFF_LOOKAHEAD bits long, we can obtain its length and
			// the corresponding symbol directly from these tables.
			public int[] look_nbits=new int[1<<HUFF_LOOKAHEAD];	// # bits, or 0 if too long
			public byte[] look_sym=new byte[1<<HUFF_LOOKAHEAD];	// symbol, or unused
		}

		// Expand a Huffman table definition into the derived format
		// Compute the derived values for a Huffman table.
		// This routine also performs some validation checks on the table.
		static void jpeg_make_d_derived_tbl(jpeg_decompress cinfo, bool isDC, int tblno, ref d_derived_tbl pdtbl)
		{
			// Note that huffsize[] and huffcode[] are filled in code-length order,
			// paralleling the order of the symbols themselves in htbl.huffval[].

			// Find the input Huffman table
			if(tblno<0||tblno>=NUM_HUFF_TBLS) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_NO_HUFF_TABLE, tblno);
			JHUFF_TBL htbl=isDC?cinfo.dc_huff_tbl_ptrs[tblno]:cinfo.ac_huff_tbl_ptrs[tblno];
			if(htbl==null) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_NO_HUFF_TABLE, tblno);

			// Allocate a workspace if we haven't already done so.
			if(pdtbl==null)
			{
				try
				{
					pdtbl=new d_derived_tbl();
				}
				catch
				{
					ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
				}
			}

			d_derived_tbl dtbl=pdtbl;
			dtbl.pub=htbl; // fill in back link

			// Figure C.1: make table of Huffman code length for each symbol
			byte[] huffsize=new byte[257];
			int p=0;
			for(byte l=1; l<=16; l++)
			{
				int i=(int)htbl.bits[l];
				if(i<0||p+i>256) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_HUFF_TABLE); // protect against table overrun
				while((i--)!=0) huffsize[p++]=l;
			}
			huffsize[p]=0;
			int numsymbols=p;

			// Figure C.2: generate the codes themselves
			// We also validate that the counts represent a legal Huffman code tree.
			uint[] huffcode=new uint[257];
			uint code=0;
			int si=huffsize[0];
			p=0;
			while(huffsize[p]!=0)
			{
				while(((int)huffsize[p])==si)
				{
					huffcode[p++]=code;
					code++;
				}
				// code is now 1 more than the last code used for codelength si; but
				// it must still fit in si bits, since no code is allowed to be all ones.
				if(((int)code)>=(1<<si)) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_HUFF_TABLE);
				code<<=1;
				si++;
			}

			// Figure F.15: generate decoding tables for bit-sequential decoding
			p=0;
			for(int l=1; l<=16; l++)
			{
				if(htbl.bits[l]!=0)
				{
					// valoffset[l] = huffval[] index of 1st symbol of code length l,
					// minus the minimum code of length l
					dtbl.valoffset[l]=(int)p-(int)huffcode[p];
					p+=htbl.bits[l];
					dtbl.maxcode[l]=(int)huffcode[p-1]; // maximum code of length l
				}
				else
				{
					dtbl.maxcode[l]=-1;	// -1 if no codes of this length
				}
			}
			dtbl.maxcode[17]=0xFFFFF; // ensures jpeg_huff_decode terminates

			// Compute lookahead tables to speed up decoding.
			// First we set all the table entries to 0, indicating "too long";
			// then we iterate through the Huffman codes that are short enough and
			// fill in all the entries that correspond to bit sequences starting
			// with that code.
			for(int i=0; i<dtbl.look_nbits.Length; i++) dtbl.look_nbits[i]=0;

			p=0;
			for(int l=1; l<=HUFF_LOOKAHEAD; l++)
			{
				for(int i=1; i<=(int)htbl.bits[l]; i++, p++)
				{
					// l = current code's length, p = its index in huffcode[] & huffval[].
					// Generate left-justified code followed by all possible bit sequences
					int lookbits=((int)huffcode[p])<<(HUFF_LOOKAHEAD-l);
					for(int ctr=1<<(HUFF_LOOKAHEAD-l); ctr>0; ctr--)
					{
						dtbl.look_nbits[lookbits]=l;
						dtbl.look_sym[lookbits]=htbl.huffval[p];
						lookbits++;
					}
				}
			}

			// Validate symbols as being reasonable.
			// For AC tables, we make no check, but accept all byte values 0..255.
			// For DC tables, we require the symbols to be in range 0..16.
			// (Tighter bounds could be applied depending on the data depth and mode,
			// but this is sufficient to ensure safe decoding.)
			if(isDC)
			{
				for(int i=0; i<numsymbols; i++)
				{
					int sym=htbl.huffval[i];
					if(sym<0||sym>16) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_HUFF_TABLE);
				}
			}
		}

		// Fetching the next N bits from the input stream is a time-critical operation
		// for the Huffman decoders. We implement it with a combination of inline
		// macros and out-of-line subroutines. Note that N (the number of bits
		// demanded at one time) never exceeds 15 for JPEG use.
		//
		// We read source bytes into get_buffer and dole out bits as needed.
		// If get_buffer already contains enough bits, they are fetched in-line
		// by the macros CHECK_BIT_BUFFER and GET_BITS. When there aren't enough
		// bits, jpeg_fill_bit_buffer is called; it will attempt to fill get_buffer
		// as full as possible (not just to the number of bits needed; this
		// prefetching reduces the overhead cost of calling jpeg_fill_bit_buffer).
		// Note that jpeg_fill_bit_buffer may return false to indicate suspension.
		// On true return, jpeg_fill_bit_buffer guarantees that get_buffer contains
		// at least the requested number of bits --- dummy zeroes are inserted if
		// necessary.

		public struct bitread_perm_state
		{ // Bitreading state saved across MCUs
			public ulong get_buffer;	// current bit-extraction buffer
			public int bits_left;		// # of unused bits in it
		}

		struct bitread_working_state
		{ // Bitreading working state within an MCU
			// Current data source location
			// We need a copy, rather than munging the original, in case of suspension
			public byte[] input_bytes;
			public int next_input_byte;		// => next byte to read from source
			public uint bytes_in_buffer;	// # of bytes remaining in source buffer

			// Bit input buffer.
			public ulong get_buffer;		// current bit-extraction buffer
			public int bits_left;			// # of unused bits in it

			// Pointer needed by jpeg_fill_bit_buffer.
			public jpeg_decompress cinfo;	// back link to decompress master record
		}

		// Macros to declare and load/save bitread local variables.
		//#define BITREAD_STATE_VARS \
		//	ulong get_buffer; \
		//	int bits_left; \
		//	bitread_working_state br_state

		//#define BITREAD_LOAD_STATE(cinfop, permstate) \
		//	br_state.cinfo=cinfop; \
		//	br_state.next_input_byte=cinfop.src.next_input_byte; \
		//	br_state.bytes_in_buffer=cinfop.src.bytes_in_buffer; \
		//	get_buffer=permstate.get_buffer; \
		//	bits_left=permstate.bits_left;

		//#define BITREAD_SAVE_STATE(cinfop, permstate) \
		//	cinfop.src.next_input_byte=br_state.next_input_byte; \
		//	cinfop.src.bytes_in_buffer=br_state.bytes_in_buffer; \
		//	permstate.get_buffer=get_buffer; \
		//	permstate.bits_left=bits_left

		// These macros provide the in-line portion of bit fetching.
		// Use CHECK_BIT_BUFFER to ensure there are N bits in get_buffer
		// before using GET_BITS, PEEK_BITS, or DROP_BITS.
		// The variables get_buffer and bits_left are assumed to be locals,
		// but the state struct might not be (jpeg_huff_decode needs this).
		//	CHECK_BIT_BUFFER(state,n,action);
		//		Ensure there are N bits in get_buffer; if suspend, take action.
		//		val = GET_BITS(n);
		//		Fetch next N bits.
		//		val = PEEK_BITS(n);
		//		Fetch next N bits without removing them from the buffer.
		//	DROP_BITS(n);
		//		Discard next N bits.
		// The value N should be a simple variable, not an expression, because it
		// is evaluated multiple times.

		//#define CHECK_BIT_BUFFER(state, nbits, action) \
		//	{ if(bits_left<(nbits)) { \
		//		if(!jpeg_fill_bit_buffer(&(state), get_buffer, bits_left, nbits)) { action; } \
		//		get_buffer=(state).get_buffer; bits_left=(state).bits_left; } }

		//#define GET_BITS(nbits) (((int)(get_buffer>>(bits_left-=(nbits))))&((1<<(nbits))-1))

		//#define PEEK_BITS(nbits) (((int)(get_buffer>>(bits_left-(nbits))))&((1<<(nbits))-1))

		//#define DROP_BITS(nbits) (bits_left-=(nbits))

		// Load up the bit buffer to a depth of at least nbits
		// Out-of-line code for bit fetching.
		// Note: current values of get_buffer and bits_left are passed as parameters,
		// but are returned in the corresponding fields of the state struct.

		// On most machines MIN_GET_BITS should be 25 to allow the full 32-bit width
		// of get_buffer to be used. (On machines with wider words, an even larger
		// buffer could be used.) However, on some machines 32-bit shifts are
		// quite slow and take time proportional to the number of places shifted.
		// (This is true with most PC compilers, for instance.) In this case it may
		// be a win to set MIN_GET_BITS to the minimum value of 15. This reduces the
		// average shift distance at the cost of more calls to jpeg_fill_bit_buffer.
		static bool jpeg_fill_bit_buffer(ref bitread_working_state state, ulong get_buffer, int bits_left, int nbits)
		{
			int MIN_GET_BITS=57;
			// Copy heavily used state fields into locals (hopefully registers)
			byte[] input_bytes=state.input_bytes;
			int next_input_byte=state.next_input_byte;
			uint bytes_in_buffer=state.bytes_in_buffer;
			jpeg_decompress cinfo=state.cinfo;

			// Attempt to load at least MIN_GET_BITS bits into get_buffer.
			// (It is assumed that no request will be for more than that many bits.)
			// We fail to do so only if we hit a marker or are forced to suspend.
			if(cinfo.unread_marker==0)
			{	// cannot advance past a marker
				while(bits_left<MIN_GET_BITS)
				{
					// Attempt to read a byte
					if(bytes_in_buffer==0)
					{
						if(!cinfo.src.fill_input_buffer(cinfo)) return false;
						input_bytes=cinfo.src.input_bytes;
						next_input_byte=cinfo.src.next_input_byte;
						bytes_in_buffer=cinfo.src.bytes_in_buffer;
					}
					bytes_in_buffer--;
					int c=input_bytes[next_input_byte++];

					// If it's 0xFF, check and discard stuffed zero byte
					if(c==0xFF)
					{
						// Loop here to discard any padding FF's on terminating marker,
						// so that we can save a valid unread_marker value. NOTE: we will
						// accept multiple FF's followed by a 0 as meaning a single FF data
						// byte. This data pattern is not valid according to the standard.
						do
						{
							if(bytes_in_buffer==0)
							{
								if(!cinfo.src.fill_input_buffer(cinfo)) return false;
								input_bytes=cinfo.src.input_bytes;
								next_input_byte=cinfo.src.next_input_byte;
								bytes_in_buffer=cinfo.src.bytes_in_buffer;
							}
							bytes_in_buffer--;
							c=input_bytes[next_input_byte++];
						} while(c==0xFF);

						if(c==0)
						{
							// Found FF/00, which represents an FF data byte
							c=0xFF;
						}
						else
						{
							// Oops, it's actually a marker indicating end of compressed data.
							// Save the marker code for later use.
							// Fine point: it might appear that we should save the marker into
							// bitread working state, not straight into permanent state. But
							// once we have hit a marker, we cannot need to suspend within the
							// current MCU, because we will read no more bytes from the data
							// source. So it is OK to update permanent state right away.
							cinfo.unread_marker=c;
							// See if we need to insert some fake zero bits.
							break;
						}
					}

					// OK, load c into get_buffer
					get_buffer*=256;
					get_buffer|=(uint)c;
					bits_left+=8;
				} // end while
			}

			if(cinfo.unread_marker!=0)
			{
				// We get here if we've read the marker that terminates the compressed
				// data segment. There should be enough bits in the buffer register
				// to satisfy the request; if so, no problem.
				if(nbits>bits_left)
				{
					// Uh-oh. Report corrupted data to user and stuff zeroes into
					// the data stream, so that we can produce some kind of image.
					// We use a nonvolatile flag to ensure that only one warning message
					// appears per data segment.
					jpeg_entropy_decoder huffd;
#if D_LOSSLESS_SUPPORTED
					if(cinfo.process==J_CODEC_PROCESS.JPROC_LOSSLESS)
						huffd=(jpeg_entropy_decoder)((jpeg_lossless_d_codec)cinfo.coef).entropy_private;
					else 
#endif
						huffd=(jpeg_entropy_decoder)((jpeg_lossy_d_codec)cinfo.coef).entropy_private;
					if(!huffd.insufficient_data)
					{
						WARNMS(cinfo, J_MESSAGE_CODE.JWRN_HIT_MARKER);
						huffd.insufficient_data=true;
					}
					// Fill the buffer with zero bits
					get_buffer<<=MIN_GET_BITS-bits_left;
					bits_left=MIN_GET_BITS;
				}
			}

			// Unload the local registers
			state.input_bytes=input_bytes;
			state.next_input_byte=next_input_byte;
			state.bytes_in_buffer=bytes_in_buffer;
			state.get_buffer=get_buffer;
			state.bits_left=bits_left;

			return true;
		}
		
		// Out-of-line code for Huffman code decoding.
		static int jpeg_huff_decode(ref bitread_working_state state, ulong get_buffer, int bits_left, d_derived_tbl htbl, int min_bits)
		{
			int l=min_bits;

			// HUFF_DECODE has determined that the code is at least min_bits
			// bits long, so fetch that many bits in one swoop.

			//was CHECK_BIT_BUFFER(*state, l, return -1);
			if(bits_left<l)
			{
				if(!jpeg_fill_bit_buffer(ref state, get_buffer, bits_left, l)) return -1;
				get_buffer=state.get_buffer;
				bits_left=state.bits_left;
			}

			//was code = GET_BITS(l);
			int code=((int)(get_buffer>>(bits_left-=l)))&((1<<l)-1);

			// Collect the rest of the Huffman code one bit at a time.
			// This is per Figure F.16 in the JPEG spec.
			while(code>htbl.maxcode[l])
			{
				code<<=1;
				//was CHECK_BIT_BUFFER(*state, 1, return -1);
				if(bits_left<1)
				{
					if(!jpeg_fill_bit_buffer(ref state, get_buffer, bits_left, 1)) return -1;
					get_buffer=state.get_buffer;
					bits_left=state.bits_left;
				}
				//was code |= GET_BITS(1);
				code|=((int)(get_buffer>>(bits_left-=1)))&1;
				l++;
			}

			// Unload the local registers
			state.get_buffer=get_buffer;
			state.bits_left=bits_left;

			// With garbage input we may reach the sentinel value l = 17.

			if(l>16)
			{
				WARNMS(state.cinfo, J_MESSAGE_CODE.JWRN_HUFF_BAD_CODE);
				return 0;			// fake a zero as the safest result
			}

			return htbl.pub.huffval[(int)(code+htbl.valoffset[l])];
		}
	}
}
