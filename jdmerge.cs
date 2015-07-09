#if UPSAMPLE_MERGING_SUPPORTED
// jdmerge.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1994-1996, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains code for merged upsampling/color conversion.
//
// This file combines functions from jdsample.cs and jdcolor.cs;
// read those files first to understand what's going on.
//
// When the chroma components are to be upsampled by simple replication
// (ie, box filtering), we can save some work in color conversion by
// calculating all the output pixels corresponding to a pair of chroma
// samples at one time. In the conversion equations
//	R = Y			+ K1 * Cr
//	G = Y + K2 * Cb + K3 * Cr
//	B = Y + K4 * Cb
// only the Y term varies among the group of pixels corresponding to a pair
// of chroma samples, so the rest of the terms can be calculated just once.
// At typical sampling ratios, this eliminates half or three-quarters of the
// multiplications needed for color conversion.
//
// This file currently provides implementations for the following cases:
//	* YCbCr => RGB color conversion only.
//	* Sampling ratios of 2h1v or 2h2v.
//	* No scaling needed at upsample time.
//	* Corner-aligned (non-CCIR601) sampling alignment.
// Other special cases could be added, but in most applications these are
// the only common cases. (For uncommon cases we fall back on the more
// general code in jdsample.cs and jdcolor.cs.)
using System;

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Private subobject
		class my_upsampler_merge : jpeg_upsampler
		{
			// Pointer to routine to do actual upsampling/conversion of one row group
			public void_jpeg_decompress_byteAAA_uint_byteAA_int_Handler upmethod;

			// Private state for YCC->RGB conversion
			public int[] Cr_r_tab;		// => table for Cr to R conversion
			public int[] Cb_b_tab;		// => table for Cb to B conversion
			public int[] Cr_g_tab;		// => table for Cr to G conversion
			public int[] Cb_g_tab;		// => table for Cb to G conversion

			// For 2:1 vertical sampling, we produce two output rows at a time.
			// We need a "spare" row buffer to hold the second output row if the
			// application provides just a one-row buffer; we also use the spare
			// to discard the dummy last row if the image height is odd.
			public byte[] spare_row;
			public bool spare_full;		// T if spare buffer is occupied

			public uint out_row_width;	// samples per output row
			public uint rows_to_go;		// counts rows remaining in image
		}

		// Initialize tables for YCC->RGB colorspace conversion.
		// This is taken directly from jdcolor.cs; see that file for more info.
		static void build_ycc_rgb_table_upsample(jpeg_decompress cinfo)
		{
			my_upsampler_merge upsample=(my_upsampler_merge)cinfo.upsample;

			try
			{
				upsample.Cr_r_tab=new int[MAXJSAMPLE+1];
				upsample.Cb_b_tab=new int[MAXJSAMPLE+1];
				upsample.Cr_g_tab=new int[MAXJSAMPLE+1];
				upsample.Cb_g_tab=new int[MAXJSAMPLE+1];
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}

			for(int i=0, x=-CENTERJSAMPLE; i<=MAXJSAMPLE; i++, x++)
			{
				// i is the actual input pixel value, in the range 0..MAXJSAMPLE
				// The Cb or Cr value we are thinking of is x = i - CENTERJSAMPLE
				// Cr=>R value is nearest int to 1.40200 * x
				upsample.Cr_r_tab[i]=(int)((FIX_140200*x+ONE_HALF)>>SCALEBITS);
				// Cb=>B value is nearest int to 1.77200 * x
				upsample.Cb_b_tab[i]=(int)((FIX_177200*x+ONE_HALF)>>SCALEBITS);
				// Cr=>G value is scaled-up -0.71414 * x
				upsample.Cr_g_tab[i]=(-FIX_071414)*x;
				// Cb=>G value is scaled-up -0.34414 * x
				// We also add in ONE_HALF so that need not do it in inner loop
				upsample.Cb_g_tab[i]=(-FIX_034414)*x+ONE_HALF;
			}
		}

		// Initialize for an upsampling pass.
		static void start_pass_merged_upsample(jpeg_decompress cinfo)
		{
			my_upsampler_merge upsample=(my_upsampler_merge)cinfo.upsample;

			// Mark the spare buffer empty
			upsample.spare_full=false;

			// Initialize total-height counter for detecting bottom of image
			upsample.rows_to_go=cinfo.output_height;
		}

		// Control routine to do upsampling (and color conversion).
		//
		// The control routine just handles the row buffering considerations.

		// 2:1 vertical sampling case: may need a spare row.
		static void merged_2v_upsample(jpeg_decompress cinfo, byte[][][] input_buf, ref uint in_row_group_ctr, uint in_row_groups_avail, byte[][] output_buf, uint output_buf_offset, ref uint out_row_ctr, uint out_rows_avail)
		{
			my_upsampler_merge upsample=(my_upsampler_merge)cinfo.upsample;
			byte[][] work_ptrs=new byte[2][];
			uint num_rows; // number of rows returned to caller

			if(upsample.spare_full)
			{
				// If we have a spare row saved from a previous cycle, just return it.
				Array.Copy(upsample.spare_row, output_buf[output_buf_offset+out_row_ctr], upsample.out_row_width);

				num_rows=1;
				upsample.spare_full=false;
			}
			else
			{
				// Figure number of rows to return to caller.
				num_rows=2;

				// Not more than the distance to the end of the image.
				if(num_rows>upsample.rows_to_go) num_rows=upsample.rows_to_go;

				// And not more than what the client can accept:
				out_rows_avail-=out_row_ctr;
				if(num_rows>out_rows_avail) num_rows=out_rows_avail;

				// Create output pointer array for upsampler.
				work_ptrs[0]=output_buf[output_buf_offset+out_row_ctr];
				if(num_rows>1)
				{
					work_ptrs[1]=output_buf[output_buf_offset+out_row_ctr+1];
				}
				else
				{
					work_ptrs[1]=upsample.spare_row;
					upsample.spare_full=true;
				}

				// Now do the upsampling.
				upsample.upmethod(cinfo, input_buf, in_row_group_ctr, work_ptrs, 0);
			}

			// Adjust counts
			out_row_ctr+=num_rows;
			upsample.rows_to_go-=num_rows;
			// When the buffer is emptied, declare this input row group consumed
			if(!upsample.spare_full) in_row_group_ctr++;
		}

		// 1:1 vertical sampling case: much easier, never need a spare row.
		static void merged_1v_upsample(jpeg_decompress cinfo, byte[][][] input_buf, ref uint in_row_group_ctr, uint in_row_groups_avail, byte[][] output_buf, uint output_buf_offset, ref uint out_row_ctr, uint out_rows_avail)
		{
			my_upsampler_merge upsample=(my_upsampler_merge)cinfo.upsample;

			// Just do the upsampling.
			upsample.upmethod(cinfo, input_buf, in_row_group_ctr, output_buf, (int)(output_buf_offset+out_row_ctr));

			// Adjust counts
			out_row_ctr++;
			in_row_group_ctr++;
		}

		// These are the routines invoked by the control routines to do
		// the actual upsampling/conversion. One row group is processed per call.
		//
		// Note: since we may be writing directly into application-supplied buffers,
		// we have to be honest about the output width; we can't assume the buffer
		// has been rounded up to an even width.

		// Upsample and color convert for the case of 2:1 horizontal and 1:1 vertical.
		static void h2v1_merged_upsample(jpeg_decompress cinfo, byte[][][] input_buf, uint in_row_group_ctr, byte[][] output_buf, int output_buf_offset)
		{
			my_upsampler_merge upsample=(my_upsampler_merge)cinfo.upsample;

			// copy these pointers into registers if possible
			int[] Crrtab=upsample.Cr_r_tab;
			int[] Cbbtab=upsample.Cb_b_tab;
			int[] Crgtab=upsample.Cr_g_tab;
			int[] Cbgtab=upsample.Cb_g_tab;

			byte[] inptr0=input_buf[0][in_row_group_ctr];
			byte[] inptr1=input_buf[1][in_row_group_ctr];
			byte[] inptr2=input_buf[2][in_row_group_ctr];
			byte[] outptr=output_buf[output_buf_offset];
			int inptr0_ind=0, inptrX_ind=0, outptr_ind=0;

			// Loop for each pair of output pixels
			for(uint col=cinfo.output_width>>1; col>0; col--)
			{
				// Do the chroma part of the calculation
				int cb=inptr1[inptrX_ind];
				int cr=inptr2[inptrX_ind];
				inptrX_ind++;
				int cred=Crrtab[cr];
				int cgreen=(int)((Cbgtab[cb]+Crgtab[cr])>>SCALEBITS);
				int cblue=Cbbtab[cb];

				// Fetch 2 Y values and emit 2 pixels
				int y=inptr0[inptr0_ind++];
				int tmp=y+cred; outptr[outptr_ind+RGB_RED]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
				tmp=y+cgreen; outptr[outptr_ind+RGB_GREEN]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
				tmp=y+cblue; outptr[outptr_ind+RGB_BLUE]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
				outptr_ind+=RGB_PIXELSIZE;
				y=inptr0[inptr0_ind++];
				tmp=y+cred; outptr[outptr_ind+RGB_RED]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
				tmp=y+cgreen; outptr[outptr_ind+RGB_GREEN]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
				tmp=y+cblue; outptr[outptr_ind+RGB_BLUE]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
				outptr_ind=RGB_PIXELSIZE;
			}

			// If image width is odd, do the last output column separately
			if((cinfo.output_width&1)!=0)
			{
				int cb=inptr1[inptrX_ind];
				int cr=inptr2[inptrX_ind];
				int cred=Crrtab[cr];
				int cgreen=(int)((Cbgtab[cb]+Crgtab[cr])>>SCALEBITS);
				int cblue=Cbbtab[cb];
				int y=inptr0[inptr0_ind];
				int tmp=y+cred; outptr[outptr_ind+RGB_RED]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
				tmp=y+cgreen; outptr[outptr_ind+RGB_GREEN]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
				tmp=y+cblue; outptr[outptr_ind+RGB_BLUE]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
			}
		}

		// Upsample and color convert for the case of 2:1 horizontal and 2:1 vertical.
		static void h2v2_merged_upsample(jpeg_decompress cinfo, byte[][][] input_buf, uint in_row_group_ctr, byte[][] output_buf, int output_buf_offset)
		{
			my_upsampler_merge upsample=(my_upsampler_merge)cinfo.upsample;

			// copy these pointers into registers if possible
			int[] Crrtab=upsample.Cr_r_tab;
			int[] Cbbtab=upsample.Cb_b_tab;
			int[] Crgtab=upsample.Cr_g_tab;
			int[] Cbgtab=upsample.Cb_g_tab;

			byte[] inptr00=input_buf[0][in_row_group_ctr*2];
			byte[] inptr01=input_buf[0][in_row_group_ctr*2+1];
			byte[] inptr1=input_buf[1][in_row_group_ctr];
			byte[] inptr2=input_buf[2][in_row_group_ctr];
			byte[] outptr0=output_buf[output_buf_offset];
			byte[] outptr1=output_buf[output_buf_offset+1];

			int inptr0_ind=0, inptrX_ind=0, outptr_ind=0;

			// Loop for each group of output pixels
			for(uint col=cinfo.output_width>>1; col>0; col--)
			{
				// Do the chroma part of the calculation
				int cb=inptr1[inptrX_ind];
				int cr=inptr2[inptrX_ind];
				inptrX_ind++;
				int cred=Crrtab[cr];
				int cgreen=(int)((Cbgtab[cb]+Crgtab[cr])>>SCALEBITS);
				int cblue=Cbbtab[cb];

				// Fetch 4 Y values and emit 4 pixels
				int y=inptr00[inptr0_ind];
				int tmp=y+cred; outptr0[outptr_ind+RGB_RED]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
				tmp=y+cgreen; outptr0[outptr_ind+RGB_GREEN]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
				tmp=y+cblue; outptr0[outptr_ind+RGB_BLUE]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));

				y=inptr01[inptr0_ind];
				tmp=y+cred; outptr1[outptr_ind+RGB_RED]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
				tmp=y+cgreen; outptr1[outptr_ind+RGB_GREEN]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
				tmp=y+cblue; outptr1[outptr_ind+RGB_BLUE]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
				inptr0_ind++;
				outptr_ind+=RGB_PIXELSIZE;

				y=inptr00[inptr0_ind];
				tmp=y+cred; outptr0[outptr_ind+RGB_RED]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
				tmp=y+cgreen; outptr0[outptr_ind+RGB_GREEN]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
				tmp=y+cblue; outptr0[outptr_ind+RGB_BLUE]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));

				y=inptr01[inptr0_ind];
				tmp=y+cred; outptr1[outptr_ind+RGB_RED]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
				tmp=y+cgreen; outptr1[outptr_ind+RGB_GREEN]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
				tmp=y+cblue; outptr1[outptr_ind+RGB_BLUE]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
				inptr0_ind++;
				outptr_ind+=RGB_PIXELSIZE;
			}

			// If image width is odd, do the last output column separately
			if((cinfo.output_width&1)!=0)
			{
				int cb=inptr1[inptrX_ind];
				int cr=inptr2[inptrX_ind];
				int cred=Crrtab[cr];
				int cgreen=(int)((Cbgtab[cb]+Crgtab[cr])>>SCALEBITS);
				int cblue=Cbbtab[cb];

				int y=inptr00[inptr0_ind];
				int tmp=y+cred; outptr0[outptr_ind+RGB_RED]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
				tmp=y+cgreen; outptr0[outptr_ind+RGB_GREEN]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
				tmp=y+cblue; outptr0[outptr_ind+RGB_BLUE]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));

				y=inptr01[inptr0_ind];
				tmp=y+cred; outptr1[outptr_ind+RGB_RED]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
				tmp=y+cgreen; outptr1[outptr_ind+RGB_GREEN]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
				tmp=y+cblue; outptr1[outptr_ind+RGB_BLUE]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
			}
		}

		// Module initialization routine for merged upsampling/color conversion.
		//
		// NB: this is called under the conditions determined by use_merged_upsample()
		// in jdmaster.cs. That routine MUST correspond to the actual capabilities
		// of this module; no safety checks are made here.
		public static void jinit_merged_upsampler(jpeg_decompress cinfo)
		{
			my_upsampler_merge upsample=null;

			try
			{
				upsample=new my_upsampler_merge();
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}
			cinfo.upsample=upsample;
			upsample.start_pass=start_pass_merged_upsample;
#if UPSCALING_CONTEXT
			upsample.need_context_rows=false;
#endif

			upsample.out_row_width=(uint)(cinfo.output_width*cinfo.out_color_components);

			if(cinfo.max_v_samp_factor==2)
			{
				upsample.upsample=merged_2v_upsample;
				upsample.upmethod=h2v2_merged_upsample;

				// Allocate a spare row buffer
				try
				{
					upsample.spare_row=new byte[upsample.out_row_width];
				}
				catch
				{
					ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
				}
			}
			else
			{
				upsample.upsample=merged_1v_upsample;
				upsample.upmethod=h2v1_merged_upsample;

				// No spare row needed
				upsample.spare_row=null;
			}

			build_ycc_rgb_table_upsample(cinfo);
		}
	}
}
#endif // UPSAMPLE_MERGING_SUPPORTED
