#if DCT_FLOAT_SUPPORTED
// jfdctflt.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1994-1996, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains a floating-point implementation of the
// forward DCT (Discrete Cosine Transform).
//
// This implementation should be more accurate than either of the integer
// DCT implementations. However, it may not give the same results on all
// machines because of differences in roundoff behavior. Speed will depend
// on the hardware's floating point capacity.
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
// The primary disadvantage of this method is that with a fixed-point
// implementation, accuracy is lost due to imprecise representation of the
// scaled quantization values. However, that problem does not arise if
// we use floating point arithmetic.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Perform the forward DCT on one block of samples.
		static void jpeg_fdct_float(double[] data)
		{
			// Pass 1: process rows.
			int ptr=0;
			for(int ctr=DCTSIZE-1; ctr>=0; ctr--)
			{
				double tmp0=data[ptr]+data[ptr+7];
				double tmp7=data[ptr]-data[ptr+7];
				double tmp1=data[ptr+1]+data[ptr+6];
				double tmp6=data[ptr+1]-data[ptr+6];
				double tmp2=data[ptr+2]+data[ptr+5];
				double tmp5=data[ptr+2]-data[ptr+5];
				double tmp3=data[ptr+3]+data[ptr+4];
				double tmp4=data[ptr+3]-data[ptr+4];

				// Even part
				double tmp10=tmp0+tmp3; // phase 2
				double tmp13=tmp0-tmp3;
				double tmp11=tmp1+tmp2;
				double tmp12=tmp1-tmp2;

				data[ptr]=tmp10+tmp11; // phase 3
				data[ptr+4]=tmp10-tmp11;

				double z1=(tmp12+tmp13)*0.707106781; // c4
				data[ptr+2]=tmp13+z1; // phase 5
				data[ptr+6]=tmp13-z1;

				// Odd part
				tmp10=tmp4+tmp5; // phase 2
				tmp11=tmp5+tmp6;
				tmp12=tmp6+tmp7;

				// The rotator is modified from fig 4-8 to avoid extra negations.
				double z5=(tmp10-tmp12)*0.382683433;	// c6
				double z2=0.541196100*tmp10+z5;			// c2-c6
				double z4=1.306562965*tmp12+z5;			// c2+c6
				double z3=tmp11*0.707106781;			// c4

				double z11=tmp7+z3; // phase 5
				double z13=tmp7-z3;

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
				double tmp0=data[ptr+DCTSIZE*0]+data[ptr+DCTSIZE*7];
				double tmp7=data[ptr+DCTSIZE*0]-data[ptr+DCTSIZE*7];
				double tmp1=data[ptr+DCTSIZE*1]+data[ptr+DCTSIZE*6];
				double tmp6=data[ptr+DCTSIZE*1]-data[ptr+DCTSIZE*6];
				double tmp2=data[ptr+DCTSIZE*2]+data[ptr+DCTSIZE*5];
				double tmp5=data[ptr+DCTSIZE*2]-data[ptr+DCTSIZE*5];
				double tmp3=data[ptr+DCTSIZE*3]+data[ptr+DCTSIZE*4];
				double tmp4=data[ptr+DCTSIZE*3]-data[ptr+DCTSIZE*4];

				// Even part
				double tmp10=tmp0+tmp3; // phase 2
				double tmp13=tmp0-tmp3;
				double tmp11=tmp1+tmp2;
				double tmp12=tmp1-tmp2;

				data[ptr+DCTSIZE*0]=tmp10+tmp11; // phase 3
				data[ptr+DCTSIZE*4]=tmp10-tmp11;

				double z1=(tmp12+tmp13)*0.707106781; // c4
				data[ptr+DCTSIZE*2]=tmp13+z1; // phase 5
				data[ptr+DCTSIZE*6]=tmp13-z1;

				// Odd part
				tmp10=tmp4+tmp5; // phase 2
				tmp11=tmp5+tmp6;
				tmp12=tmp6+tmp7;

				// The rotator is modified from fig 4-8 to avoid extra negations.
				double z5=(tmp10-tmp12)*0.382683433;	// c6
				double z2=0.541196100*tmp10+z5;			// c2-c6
				double z4=1.306562965*tmp12+z5;			// c2+c6
				double z3=tmp11*0.707106781;			// c4

				double z11=tmp7+z3; // phase 5
				double z13=tmp7-z3;

				data[ptr+DCTSIZE*5]=z13+z2; // phase 6
				data[ptr+DCTSIZE*3]=z13-z2;
				data[ptr+DCTSIZE*1]=z11+z4;
				data[ptr+DCTSIZE*7]=z11-z4;

				ptr++; // advance pointer to next column
			}
		}
	}
}
#endif // DCT_FLOAT_SUPPORTED
