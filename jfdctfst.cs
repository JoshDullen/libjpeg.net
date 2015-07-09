// jfdctfst.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1994-1996, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains a fast, not so accurate integer implementation of the
// forward DCT (Discrete Cosine Transform).
//
// A 2-D DCT can be done by 1-D DCT on each row followed by 1-D DCT
// on each column. Direct algorithms are also available, but they are
// much more complex and seem not to be any faster when reduced to code.
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
		// see jfdctint.cs for more details. However, we choose to descale
		// (right shift) multiplication products as soon as they are formed,
		// rather than carrying additional fractional bits into subsequent additions.
		// This compromises accuracy slightly, but it lets us save a few shifts.
		// More importantly, 16-bit arithmetic is then adequate (for 8-bit samples)
		// everywhere except in the multiplications proper; this saves a good deal
		// of work on 16-bit-int machines.
		//
		// Again to save a few shifts, the intermediate results between pass 1 and
		// pass 2 are not upscaled, but are represented only to integral precision.
		//
		// A final compromise is to represent the multiplicative constants to only
		// 8 fractional bits, rather than 13. This saves some shifting work on some
		// machines, and may also reduce the cost of multiplication (since there
		// are fewer one-bits in the constants).
		const int FIX_0_382683433=98;
		const int FIX_0_541196100=139;
		const int FIX_0_707106781=181;
		const int FIX_1_306562965=334;

		// Perform the forward DCT on one block of samples.
		static void jpeg_fdct_ifast(int[] data)
		{
			// Pass 1: process rows.
			int ptr=0;
			for(int ctr=DCTSIZE-1; ctr>=0; ctr--)
			{
				int tmp0=data[ptr]+data[ptr+7];
				int tmp7=data[ptr]-data[ptr+7];
				int tmp1=data[ptr+1]+data[ptr+6];
				int tmp6=data[ptr+1]-data[ptr+6];
				int tmp2=data[ptr+2]+data[ptr+5];
				int tmp5=data[ptr+2]-data[ptr+5];
				int tmp3=data[ptr+3]+data[ptr+4];
				int tmp4=data[ptr+3]-data[ptr+4];

				// Even part
				int tmp10=tmp0+tmp3; // phase 2
				int tmp13=tmp0-tmp3;
				int tmp11=tmp1+tmp2;
				int tmp12=tmp1-tmp2;

				data[ptr]=tmp10+tmp11; // phase 3
				data[ptr+4]=tmp10-tmp11;

				int z1=((tmp12+tmp13)*FIX_0_707106781)>>8; // c4
				data[ptr+2]=tmp13+z1; // phase 5
				data[ptr+6]=tmp13-z1;

				// Odd part
				tmp10=tmp4+tmp5; // phase 2
				tmp11=tmp5+tmp6;
				tmp12=tmp6+tmp7;

				// The rotator is modified from fig 4-8 to avoid extra negations.
				int z5=((tmp10-tmp12)*FIX_0_382683433)>>8;	// c6
				int z2=((tmp10*FIX_0_541196100)>>8)+z5;		// c2-c6
				int z4=((tmp12*FIX_1_306562965)>>8)+z5;		// c2+c6
				int z3=(tmp11*FIX_0_707106781)>>8;			// c4

				int z11=tmp7+z3; // phase 5
				int z13=tmp7-z3;

				data[ptr+5]=z13+z2; // phase 6
				data[ptr+3]=z13-z2;
				data[ptr+1]=z11+z4;
				data[ptr+7]=z11-z4;

				ptr+=DCTSIZE; // advance pointer to next row
			}

			// Pass 2: process columns.
			ptr=0;
			for(int ctr=DCTSIZE-1; ctr>=0; ctr--)
			{
				int tmp0=data[ptr+DCTSIZE*0]+data[ptr+DCTSIZE*7];
				int tmp7=data[ptr+DCTSIZE*0]-data[ptr+DCTSIZE*7];
				int tmp1=data[ptr+DCTSIZE*1]+data[ptr+DCTSIZE*6];
				int tmp6=data[ptr+DCTSIZE*1]-data[ptr+DCTSIZE*6];
				int tmp2=data[ptr+DCTSIZE*2]+data[ptr+DCTSIZE*5];
				int tmp5=data[ptr+DCTSIZE*2]-data[ptr+DCTSIZE*5];
				int tmp3=data[ptr+DCTSIZE*3]+data[ptr+DCTSIZE*4];
				int tmp4=data[ptr+DCTSIZE*3]-data[ptr+DCTSIZE*4];

				// Even part
				int tmp10=tmp0+tmp3; // phase 2
				int tmp13=tmp0-tmp3;
				int tmp11=tmp1+tmp2;
				int tmp12=tmp1-tmp2;

				data[ptr+DCTSIZE*0]=tmp10+tmp11; // phase 3
				data[ptr+DCTSIZE*4]=tmp10-tmp11;

				int z1=((tmp12+tmp13)*FIX_0_707106781)>>8; // c4
				data[ptr+DCTSIZE*2]=tmp13+z1; // phase 5
				data[ptr+DCTSIZE*6]=tmp13-z1;

				// Odd part
				tmp10=tmp4+tmp5; // phase 2
				tmp11=tmp5+tmp6;
				tmp12=tmp6+tmp7;

				// The rotator is modified from fig 4-8 to avoid extra negations.
				int z5=((tmp10-tmp12)*FIX_0_382683433)>>8;	// c6
				int z2=((tmp10*FIX_0_541196100)>>8)+z5;		// c2-c6
				int z4=((tmp12*FIX_1_306562965)>>8)+z5;		// c2+c6
				int z3=(tmp11*FIX_0_707106781)>>8;			// c4

				int z11=tmp7+z3; // phase 5
				int z13=tmp7-z3;

				data[ptr+DCTSIZE*5]=z13+z2; // phase 6
				data[ptr+DCTSIZE*3]=z13-z2;
				data[ptr+DCTSIZE*1]=z11+z4;
				data[ptr+DCTSIZE*7]=z11-z4;

				ptr++; // advance pointer to next column
			}
		}
	}
}
