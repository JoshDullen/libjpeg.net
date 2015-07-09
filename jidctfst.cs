// jidctfst.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1994-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains a fast, not so accurate integer implementation of the
// inverse DCT (Discrete Cosine Transform). In the IJG code, this routine
// must also perform dequantization of the input coefficients.
//
// A 2-D IDCT can be done by 1-D IDCT on each column followed by 1-D IDCT
// on each row (or vice versa, but it's more convenient to emit a row at
// a time). Direct algorithms are also available, but they are much more
// complex and seem not to be any faster when reduced to code.
//
// This implementation is based on Arai, Agui, and Nakajima's algorithm for
// scaled DCT. Their original paper (Trans. IEICE E-71(11):1095) is in
// Japanese, but the algorithm is described in the Pennebaker & Mitchell
// JPEG textbook (see REFERENCES section in file README). The following code
// is based directly on figure 4-8 in P&M.
// While an 8-point DCT cannot be done in less than 11 multiplies, it is
// possible to arrange the computation so that many of the multiplies are
// simple scalings of the final outputs. These multiplies can then be
// folded into the multiplications or divisions by the JPEG quantization
// table entries. The AA&N method leaves only 5 multiplies and 29 adds
// to be done in the DCT itself.
// The primary disadvantage of this method is that with fixed-point math,
// accuracy is lost due to imprecise representation of the scaled
// quantization values. The smaller the quantization table entry, the less
// precise the scaled value, so this implementation does worse with high-
// quality-setting files than with low-quality ones.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Scaling decisions are generally the same as in the LL&M algorithm;
		// see jidctint.cs for more details. However, we choose to descale
		// (right shift) multiplication products as soon as they are formed,
		// rather than carrying additional fractional bits into subsequent additions.
		// This compromises accuracy slightly, but it lets us save a few shifts.
		// More importantly, 16-bit arithmetic is then adequate (for 8-bit samples)
		// everywhere except in the multiplications proper; this saves a good deal
		// of work on 16-bit-int machines.
		//
		// A final compromise is to represent the multiplicative constants to only
		// 8 fractional bits, rather than 13. This saves some shifting work on some
		// machines, and may also reduce the cost of multiplication (since there
		// are fewer one-bits in the constants).
		const int FIX_1_082392200=277;
		const int FIX_1_414213562=362;
		const int FIX_1_847759065=473;
		const int FIX_2_613125930=669;

		// Perform dequantization and inverse DCT on one block of coefficients.
#if! USE_UNSAFE_STUFF
		static void jpeg_idct_ifast(jpeg_decompress cinfo, jpeg_component_info compptr, short[] coef_block, byte[][] output_buf, uint output_row, uint output_col)
		{
			int[] workspace=new int[DCTSIZE2];	// buffers data between passes

			// Pass 1: process columns from input, store into work array.
			short[] inptr=coef_block;
			int[] quantptr=compptr.dct_table;
			int[] wsptr=workspace;
			int ptr=0;
			for(int ctr=DCTSIZE; ctr>0; ctr--, ptr++) // ptr++: advance pointers to next column
			{
				// Due to quantization, we will usually find that many of the input
				// coefficients are zero, especially the AC terms. We can exploit this
				// by short-circuiting the IDCT calculation for any column in which all
				// the AC terms are zero. In that case each output is equal to the
				// DC coefficient (with scale factor as needed).
				// With typical images and quantization tables, half or more of the
				// column DCT calculations can be simplified this way.
				if(inptr[ptr+DCTSIZE*1]==0&&inptr[ptr+DCTSIZE*2]==0&&inptr[ptr+DCTSIZE*3]==0&&inptr[ptr+DCTSIZE*4]==0&&
					inptr[ptr+DCTSIZE*5]==0&&inptr[ptr+DCTSIZE*6]==0&&inptr[ptr+DCTSIZE*7]==0)
				{
					// AC terms all zero
					int dcval=inptr[ptr]*quantptr[ptr];

					wsptr[ptr]=dcval;
					wsptr[ptr+DCTSIZE*1]=dcval;
					wsptr[ptr+DCTSIZE*2]=dcval;
					wsptr[ptr+DCTSIZE*3]=dcval;
					wsptr[ptr+DCTSIZE*4]=dcval;
					wsptr[ptr+DCTSIZE*5]=dcval;
					wsptr[ptr+DCTSIZE*6]=dcval;
					wsptr[ptr+DCTSIZE*7]=dcval;
					continue;
				}

				// Even part
				int tmp0=inptr[ptr]*quantptr[ptr];
				int tmp1=inptr[ptr+DCTSIZE*2]*quantptr[ptr+DCTSIZE*2];
				int tmp2=inptr[ptr+DCTSIZE*4]*quantptr[ptr+DCTSIZE*4];
				int tmp3=inptr[ptr+DCTSIZE*6]*quantptr[ptr+DCTSIZE*6];

				int tmp10=tmp0+tmp2;	// phase 3
				int tmp11=tmp0-tmp2;

				int tmp13=tmp1+tmp3;	// phases 5-3
				int tmp12=(((tmp1-tmp3)*FIX_1_414213562)>>8)-tmp13; // 2*c4

				tmp0=tmp10+tmp13;		// phase 2
				tmp3=tmp10-tmp13;
				tmp1=tmp11+tmp12;
				tmp2=tmp11-tmp12;

				// Odd part
				int tmp4=inptr[ptr+DCTSIZE*1]*quantptr[ptr+DCTSIZE*1];
				int tmp5=inptr[ptr+DCTSIZE*3]*quantptr[ptr+DCTSIZE*3];
				int tmp6=inptr[ptr+DCTSIZE*5]*quantptr[ptr+DCTSIZE*5];
				int tmp7=inptr[ptr+DCTSIZE*7]*quantptr[ptr+DCTSIZE*7];

				int z13=tmp6+tmp5;		// phase 6
				int z10=tmp6-tmp5;
				int z11=tmp4+tmp7;
				int z12=tmp4-tmp7;

				tmp7=z11+z13;			// phase 5
				tmp11=((z11-z13)*FIX_1_414213562)>>8;	// 2*c4

				int z5=((z10+z12)*FIX_1_847759065)>>8;	// 2*c2
				tmp10=((z12*FIX_1_082392200)>>8)-z5;	// 2*(c2-c6)
				tmp12=((z10*-FIX_2_613125930)>>8)+z5;	// -2*(c2+c6)

				tmp6=tmp12-tmp7;		// phase 2
				tmp5=tmp11-tmp6;
				tmp4=tmp10+tmp5;

				wsptr[ptr]=tmp0+tmp7;
				wsptr[ptr+DCTSIZE*7]=tmp0-tmp7;
				wsptr[ptr+DCTSIZE*1]=tmp1+tmp6;
				wsptr[ptr+DCTSIZE*6]=tmp1-tmp6;
				wsptr[ptr+DCTSIZE*2]=tmp2+tmp5;
				wsptr[ptr+DCTSIZE*5]=tmp2-tmp5;
				wsptr[ptr+DCTSIZE*4]=tmp3+tmp4;
				wsptr[ptr+DCTSIZE*3]=tmp3-tmp4;
			}

			// Pass 2: process rows from work array, store into output array.
			ptr=0;
			for(int ctr=0; ctr<DCTSIZE; ctr++)
			{
				byte[] outptr=output_buf[output_row+ctr];
				// Rows of zeroes can be exploited in the same way as we did with columns.
				// However, the column calculation has created many nonzero AC terms, so
				// the simplification applies less often (typically 5% to 10% of the time).
				// On machines with very fast multiplication, it's possible that the
				// test takes more time than it's worth. In that case this section
				// may be commented out.

#if !NO_ZERO_ROW_TEST
				if(wsptr[ptr+1]==0&&wsptr[ptr+2]==0&&wsptr[ptr+3]==0&&wsptr[ptr+4]==0&&
				wsptr[ptr+5]==0&&wsptr[ptr+6]==0&&wsptr[ptr+7]==0)
				{
					// AC terms all zero
					int dc=CENTERJSAMPLE+(wsptr[ptr]>>5); byte dcval=(byte)((dc>=MAXJSAMPLE)?MAXJSAMPLE:((dc<0)?0:dc));

					outptr[output_col]=dcval;
					outptr[output_col+1]=dcval;
					outptr[output_col+2]=dcval;
					outptr[output_col+3]=dcval;
					outptr[output_col+4]=dcval;
					outptr[output_col+5]=dcval;
					outptr[output_col+6]=dcval;
					outptr[output_col+7]=dcval;

					ptr+=DCTSIZE;		// advance pointer to next row
					continue;
				}
#endif

				// Even part
				int tmp10=wsptr[ptr]+wsptr[ptr+4];
				int tmp11=wsptr[ptr]-wsptr[ptr+4];

				int tmp13=wsptr[ptr+2]+wsptr[ptr+6];
				int tmp12=(((wsptr[ptr+2]-wsptr[ptr+6])*FIX_1_414213562)>>8)-tmp13;

				int tmp0=tmp10+tmp13;
				int tmp3=tmp10-tmp13;
				int tmp1=tmp11+tmp12;
				int tmp2=tmp11-tmp12;

				// Odd part
				int z13=wsptr[ptr+5]+wsptr[ptr+3];
				int z10=wsptr[ptr+5]-wsptr[ptr+3];
				int z11=wsptr[ptr+1]+wsptr[ptr+7];
				int z12=wsptr[ptr+1]-wsptr[ptr+7];

				int tmp7=z11+z13;		// phase 5
				tmp11=((z11-z13)*FIX_1_414213562)>>8;	// 2*c4

				int z5=((z10+z12)*FIX_1_847759065)>>8;	// 2*c2
				tmp10=((z12*FIX_1_082392200)>>8)-z5;	// 2*(c2-c6)
				tmp12=((z10*-FIX_2_613125930)>>8)+z5;	// -2*(c2+c6)

				int tmp6=tmp12-tmp7;	// phase 2
				int tmp5=tmp11-tmp6;
				int tmp4=tmp10+tmp5;

				// Final output stage: scale down by a factor of 8 and range-limit
				int x;
				x=CENTERJSAMPLE+((tmp0+tmp7)>>5); outptr[output_col+0]=(byte)((x>=MAXJSAMPLE)?MAXJSAMPLE:((x<0)?0:x));
				x=CENTERJSAMPLE+((tmp0-tmp7)>>5); outptr[output_col+7]=(byte)((x>=MAXJSAMPLE)?MAXJSAMPLE:((x<0)?0:x));
				x=CENTERJSAMPLE+((tmp1+tmp6)>>5); outptr[output_col+1]=(byte)((x>=MAXJSAMPLE)?MAXJSAMPLE:((x<0)?0:x));
				x=CENTERJSAMPLE+((tmp1-tmp6)>>5); outptr[output_col+6]=(byte)((x>=MAXJSAMPLE)?MAXJSAMPLE:((x<0)?0:x));
				x=CENTERJSAMPLE+((tmp2+tmp5)>>5); outptr[output_col+2]=(byte)((x>=MAXJSAMPLE)?MAXJSAMPLE:((x<0)?0:x));
				x=CENTERJSAMPLE+((tmp2-tmp5)>>5); outptr[output_col+5]=(byte)((x>=MAXJSAMPLE)?MAXJSAMPLE:((x<0)?0:x));
				x=CENTERJSAMPLE+((tmp3+tmp4)>>5); outptr[output_col+4]=(byte)((x>=MAXJSAMPLE)?MAXJSAMPLE:((x<0)?0:x));
				x=CENTERJSAMPLE+((tmp3-tmp4)>>5); outptr[output_col+3]=(byte)((x>=MAXJSAMPLE)?MAXJSAMPLE:((x<0)?0:x));

				ptr+=DCTSIZE;		// advance pointer to next row
			}
		}
#else
		static void jpeg_idct_ifast(jpeg_decompress cinfo, jpeg_component_info compptr, short[] coef_block, byte[][] output_buf, uint output_row, uint output_col)
		{
			int[] workspace=new int[DCTSIZE2];	// buffers data between passes

			unsafe
			{
				// Pass 1: process columns from input, store into work array.
				fixed(int* wsptr_=workspace, quantptr_=compptr.dct_table)
				{
					int* wsptr=wsptr_, quantptr=quantptr_;

					fixed(short* inptr_=coef_block)
					{
						short* inptr=inptr_;

						for(int ctr=DCTSIZE; ctr>0; ctr--)
						{
							// Due to quantization, we will usually find that many of the input
							// coefficients are zero, especially the AC terms. We can exploit this
							// by short-circuiting the IDCT calculation for any column in which all
							// the AC terms are zero. In that case each output is equal to the
							// DC coefficient (with scale factor as needed).
							// With typical images and quantization tables, half or more of the
							// column DCT calculations can be simplified this way.
							if(inptr[DCTSIZE*1]==0&&inptr[DCTSIZE*2]==0&&inptr[DCTSIZE*3]==0&&inptr[DCTSIZE*4]==0&&
								inptr[DCTSIZE*5]==0&&inptr[DCTSIZE*6]==0&&inptr[DCTSIZE*7]==0)
							{
								// AC terms all zero
								int dcval=inptr[0]*quantptr[0];

								wsptr[0]=dcval;
								wsptr[DCTSIZE*1]=dcval;
								wsptr[DCTSIZE*2]=dcval;
								wsptr[DCTSIZE*3]=dcval;
								wsptr[DCTSIZE*4]=dcval;
								wsptr[DCTSIZE*5]=dcval;
								wsptr[DCTSIZE*6]=dcval;
								wsptr[DCTSIZE*7]=dcval;

								inptr++;			// advance pointers to next column
								quantptr++;
								wsptr++;

								continue;
							}

							// Even part
							int tmp0=inptr[0]*quantptr[0];
							int tmp1=inptr[DCTSIZE*2]*quantptr[DCTSIZE*2];
							int tmp2=inptr[DCTSIZE*4]*quantptr[DCTSIZE*4];
							int tmp3=inptr[DCTSIZE*6]*quantptr[DCTSIZE*6];

							int tmp10=tmp0+tmp2;	// phase 3
							int tmp11=tmp0-tmp2;

							int tmp13=tmp1+tmp3;	// phases 5-3
							int tmp12=(((tmp1-tmp3)*FIX_1_414213562)>>8)-tmp13; // 2*c4

							tmp0=tmp10+tmp13;		// phase 2
							tmp3=tmp10-tmp13;
							tmp1=tmp11+tmp12;
							tmp2=tmp11-tmp12;

							// Odd part
							int tmp4=inptr[DCTSIZE*1]*quantptr[DCTSIZE*1];
							int tmp5=inptr[DCTSIZE*3]*quantptr[DCTSIZE*3];
							int tmp6=inptr[DCTSIZE*5]*quantptr[DCTSIZE*5];
							int tmp7=inptr[DCTSIZE*7]*quantptr[DCTSIZE*7];

							int z13=tmp6+tmp5;		// phase 6
							int z10=tmp6-tmp5;
							int z11=tmp4+tmp7;
							int z12=tmp4-tmp7;

							tmp7=z11+z13;			// phase 5
							tmp11=((z11-z13)*FIX_1_414213562)>>8;	// 2*c4

							int z5=((z10+z12)*FIX_1_847759065)>>8;	// 2*c2
							tmp10=((z12*FIX_1_082392200)>>8)-z5;	// 2*(c2-c6)
							tmp12=((z10*-FIX_2_613125930)>>8)+z5;	// -2*(c2+c6)

							tmp6=tmp12-tmp7;		// phase 2
							tmp5=tmp11-tmp6;
							tmp4=tmp10+tmp5;

							wsptr[0]=tmp0+tmp7;
							wsptr[DCTSIZE*7]=tmp0-tmp7;
							wsptr[DCTSIZE*1]=tmp1+tmp6;
							wsptr[DCTSIZE*6]=tmp1-tmp6;
							wsptr[DCTSIZE*2]=tmp2+tmp5;
							wsptr[DCTSIZE*5]=tmp2-tmp5;
							wsptr[DCTSIZE*4]=tmp3+tmp4;
							wsptr[DCTSIZE*3]=tmp3-tmp4;

							inptr++;			// advance pointers to next column
							quantptr++;
							wsptr++;
						} // for(...)
					} // fixed(short* inptr_=coef_block)

					// Pass 2: process rows from work array, store into output array.
					wsptr=wsptr_;

					for(int ctr=0; ctr<DCTSIZE; ctr++)
					{
						fixed(byte* outptr=&output_buf[output_row+ctr][output_col])
						{
							//byte* outptr=outptr_+output_col;

							// Rows of zeroes can be exploited in the same way as we did with columns.
							// However, the column calculation has created many nonzero AC terms, so
							// the simplification applies less often (typically 5% to 10% of the time).
							// On machines with very fast multiplication, it's possible that the
							// test takes more time than it's worth. In that case this section
							// may be commented out.
#if !NO_ZERO_ROW_TEST
							if(wsptr[1]==0&&wsptr[2]==0&&wsptr[3]==0&&wsptr[4]==0&&wsptr[5]==0&&wsptr[6]==0&&wsptr[7]==0)
							{
								// AC terms all zero
								int dc=CENTERJSAMPLE+(wsptr[0]>>5); byte dcval=(byte)((dc>=MAXJSAMPLE)?MAXJSAMPLE:((dc<0)?0:dc));

								outptr[0]=dcval;
								outptr[1]=dcval;
								outptr[2]=dcval;
								outptr[3]=dcval;
								outptr[4]=dcval;
								outptr[5]=dcval;
								outptr[6]=dcval;
								outptr[7]=dcval;

								wsptr+=DCTSIZE;		// advance pointer to next row
								continue;
							}
#endif

							// Even part
							int tmp10=wsptr[0]+wsptr[4];
							int tmp11=wsptr[0]-wsptr[4];

							int tmp13=wsptr[2]+wsptr[6];
							int tmp12=(((wsptr[2]-wsptr[6])*FIX_1_414213562)>>8)-tmp13;

							int tmp0=tmp10+tmp13;
							int tmp3=tmp10-tmp13;
							int tmp1=tmp11+tmp12;
							int tmp2=tmp11-tmp12;

							// Odd part
							int z13=wsptr[5]+wsptr[3];
							int z10=wsptr[5]-wsptr[3];
							int z11=wsptr[1]+wsptr[7];
							int z12=wsptr[1]-wsptr[7];

							int tmp7=z11+z13;		// phase 5
							tmp11=((z11-z13)*FIX_1_414213562)>>8;	// 2*c4

							int z5=((z10+z12)*FIX_1_847759065)>>8;	// 2*c2
							tmp10=((z12*FIX_1_082392200)>>8)-z5;	// 2*(c2-c6)
							tmp12=((z10*-FIX_2_613125930)>>8)+z5;	// -2*(c2+c6)

							int tmp6=tmp12-tmp7;	// phase 2
							int tmp5=tmp11-tmp6;
							int tmp4=tmp10+tmp5;

							// Final output stage: scale down by a factor of 8 and range-limit
							int x;
							x=CENTERJSAMPLE+((tmp0+tmp7)>>5); outptr[0]=(byte)((x>=MAXJSAMPLE)?MAXJSAMPLE:((x<0)?0:x));
							x=CENTERJSAMPLE+((tmp0-tmp7)>>5); outptr[7]=(byte)((x>=MAXJSAMPLE)?MAXJSAMPLE:((x<0)?0:x));
							x=CENTERJSAMPLE+((tmp1+tmp6)>>5); outptr[1]=(byte)((x>=MAXJSAMPLE)?MAXJSAMPLE:((x<0)?0:x));
							x=CENTERJSAMPLE+((tmp1-tmp6)>>5); outptr[6]=(byte)((x>=MAXJSAMPLE)?MAXJSAMPLE:((x<0)?0:x));
							x=CENTERJSAMPLE+((tmp2+tmp5)>>5); outptr[2]=(byte)((x>=MAXJSAMPLE)?MAXJSAMPLE:((x<0)?0:x));
							x=CENTERJSAMPLE+((tmp2-tmp5)>>5); outptr[5]=(byte)((x>=MAXJSAMPLE)?MAXJSAMPLE:((x<0)?0:x));
							x=CENTERJSAMPLE+((tmp3+tmp4)>>5); outptr[4]=(byte)((x>=MAXJSAMPLE)?MAXJSAMPLE:((x<0)?0:x));
							x=CENTERJSAMPLE+((tmp3-tmp4)>>5); outptr[3]=(byte)((x>=MAXJSAMPLE)?MAXJSAMPLE:((x<0)?0:x));

							wsptr+=DCTSIZE;		// advance pointer to next row
						} // fixed(byte* outptr=&output_buf[output_row+ctr][output_col])
					} // for(...)
				} // fixed(int* wsptr_=workspace, quantptr_=compptr.dct_table)
			} // unsafe
		}
#endif // USE_UNSAFE_STUFF
	}
}
