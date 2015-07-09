// jdcolor.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1991-1997, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains output colorspace conversion routines.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Private subobject
		class my_color_deconverter : jpeg_color_deconverter
		{
			// Private state for YCC->RGB conversion
			public int[] Cr_r_tab;	// => table for Cr to R conversion
			public int[] Cb_b_tab;	// => table for Cb to B conversion
			public int[] Cr_g_tab;	// => table for Cr to G conversion
			public int[] Cb_g_tab;	// => table for Cb to G conversion
		}

		// *************** YCbCr -> RGB conversion: most common case *************

		// YCbCr is defined per CCIR 601-1, except that Cb and Cr are
		// normalized to the range 0..MAXJSAMPLE rather than -0.5 .. 0.5.
		// The conversion equations to be implemented are therefore
		//	R = Y				 + 1.40200 * Cr
		//	G = Y - 0.34414 * Cb - 0.71414 * Cr
		//	B = Y + 1.77200 * Cb
		// where Cb and Cr represent the incoming values less CENTERJSAMPLE.
		// (These numbers are derived from TIFF 6.0 section 21, dated 3-June-92.)
		//
		// To avoid floating-point arithmetic, we represent the fractional constants
		// as integers scaled up by 2^16 (about 4 digits precision); we have to divide
		// the products by 2^16, with appropriate rounding, to get the correct answer.
		// Notice that Y, being an integral input, does not contribute any fraction
		// so it need not participate in the rounding.
		//
		// For even more speed, we avoid doing any multiplications in the inner loop
		// by precalculating the constants times Cb and Cr for all possible values.
		// For 8-bit samples this is very reasonable (only 256 entries per table);
		// for 12-bit samples it is still acceptable. It's not very reasonable for
		// 16-bit samples, but if you want lossless storage you shouldn't be changing
		// colorspace anyway.
		// The Cr=>R and Cb=>B values can be rounded to integers in advance; the
		// values for the G calculation are left scaled up, since we must add them
		// together before rounding.

		// Initialize tables for YCC->RGB colorspace conversion.
		static void build_ycc_rgb_table(jpeg_decompress cinfo)
		{
			my_color_deconverter cconvert=(my_color_deconverter)cinfo.cconvert;

			try
			{
				cconvert.Cr_r_tab=new int[MAXJSAMPLE+1];
				cconvert.Cb_b_tab=new int[MAXJSAMPLE+1];
				cconvert.Cr_g_tab=new int[MAXJSAMPLE+1];
				cconvert.Cb_g_tab=new int[MAXJSAMPLE+1];
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
				cconvert.Cr_r_tab[i]=(int)((FIX_140200*x+ONE_HALF)>>SCALEBITS);
				// Cb=>B value is nearest int to 1.77200 * x
				cconvert.Cb_b_tab[i]=(int)((FIX_177200*x+ONE_HALF)>>SCALEBITS);
				// Cr=>G value is scaled-up -0.71414 * x
				cconvert.Cr_g_tab[i]=(-FIX_071414)*x;
				// Cb=>G value is scaled-up -0.34414 * x
				// We also add in ONE_HALF so that need not do it in inner loop
				cconvert.Cb_g_tab[i]=(-FIX_034414)*x+ONE_HALF;
			}
		}

		// Convert some rows of samples to the output colorspace.
		//
		// Note that we change from noninterleaved, one-plane-per-component format
		// to interleaved-pixel format. The output buffer is therefore three times
		// as wide as the input buffer.
		// A starting row offset is provided only for the input buffer. The caller
		// can easily adjust the passed output_buf value to accommodate any row
		// offset required on that side.
#if! USE_UNSAFE_STUFF
		static void ycc_rgb_convert(jpeg_decompress cinfo, byte[][][] input_buf, uint input_row, byte[][] output_buf, uint output_row, int num_rows)
		{
			my_color_deconverter cconvert=(my_color_deconverter)cinfo.cconvert;
			uint num_cols=cinfo.output_width;

			int[] Crrtab=cconvert.Cr_r_tab;
			int[] Cbbtab=cconvert.Cb_b_tab;
			int[] Crgtab=cconvert.Cr_g_tab;
			int[] Cbgtab=cconvert.Cb_g_tab;

			while(--num_rows>=0)
			{
				byte[] inptr0=input_buf[0][input_row];
				byte[] inptr1=input_buf[1][input_row];
				byte[] inptr2=input_buf[2][input_row];
				input_row++;
				byte[] outptr=output_buf[output_row++];
				for(uint col=0, outptr_ind=0; col<num_cols; col++, outptr_ind+=RGB_PIXELSIZE)
				{
					int y=inptr0[col];
					int cb=inptr1[col];
					int cr=inptr2[col];

					// Range-limiting is essential due to noise introduced by DCT losses.
					int tmp=y+Crrtab[cr];
					outptr[outptr_ind+RGB_RED]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
					tmp=y+((int)((Cbgtab[cb]+Crgtab[cr])>>SCALEBITS));
					outptr[outptr_ind+RGB_GREEN]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
					tmp=y+Cbbtab[cb];
					outptr[outptr_ind+RGB_BLUE]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
				}
			}
		}
#else
		static void ycc_rgb_convert(jpeg_decompress cinfo, byte[][][] input_buf, uint input_row, byte[][] output_buf, uint output_row, int num_rows)
		{
			my_color_deconverter cconvert=(my_color_deconverter)cinfo.cconvert;
			uint num_cols=cinfo.output_width;

			int[] Crrtab=cconvert.Cr_r_tab;
			int[] Cbbtab=cconvert.Cb_b_tab;
			int[] Crgtab=cconvert.Cr_g_tab;
			int[] Cbgtab=cconvert.Cb_g_tab;

			unsafe
			{
				while(--num_rows>=0)
				{
					byte[] inptr0=input_buf[0][input_row];
					byte[] inptr1=input_buf[1][input_row];
					byte[] inptr2=input_buf[2][input_row];
					input_row++;

					fixed(byte* outptr_=output_buf[output_row++])
					{
						byte* outptr=outptr_;

						for(uint col=0; col<num_cols; col++)
						{
							int y=inptr0[col];
							int cb=inptr1[col];
							int cr=inptr2[col];

							// Range-limiting is essential due to noise introduced by DCT losses.
							int tmp=y+Crrtab[cr];
							outptr[RGB_RED]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
							tmp=y+((int)((Cbgtab[cb]+Crgtab[cr])>>SCALEBITS));
							outptr[RGB_GREEN]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
							tmp=y+Cbbtab[cb];
							outptr[RGB_BLUE]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
							outptr+=RGB_PIXELSIZE;
						}
					}
				}
			}
		}
#endif // USE_UNSAFE_STUFF

		// *************** Cases other than YCbCr -> RGB *************

		// Color conversion for no colorspace change: just copy the data,
		// converting from separate-planes to interleaved representation.
		static void null_convert(jpeg_decompress cinfo, byte[][][] input_buf, uint input_row, byte[][] output_buf, uint output_row, int num_rows)
		{
			int num_components=cinfo.num_components;
			uint num_cols=cinfo.output_width;
			while(--num_rows>=0)
			{
				for(int ci=0; ci<num_components; ci++)
				{
					byte[] inptr=input_buf[ci][input_row];
					int inptr_ind=0;
					byte[] outptr=output_buf[output_row];
					int outptr_ind=ci;
					for(uint count=num_cols; count>0; count--)
					{
						outptr[outptr_ind]=inptr[inptr_ind++];
						outptr_ind+=num_components;
					}
				}
				input_row++;
				output_row++;
			}
		}

		// Color conversion for grayscale: just copy the data.
		// This also works for YCbCr -> grayscale conversion, in which
		// we just copy the Y (luminance) component and ignore chrominance.
		static void grayscale_convert(jpeg_decompress cinfo, byte[][][] input_buf, uint input_row, byte[][] output_buf, uint output_row, int num_rows)
		{
			jcopy_sample_rows(input_buf[0], (int)input_row, output_buf, (int)output_row, num_rows, cinfo.output_width);
		}

		// Convert grayscale to RGB: just duplicate the graylevel three times.
		// This is provided to support applications that don't want to cope
		// with grayscale as a separate case.
		static void gray_rgb_convert(jpeg_decompress cinfo, byte[][][] input_buf, uint input_row, byte[][] output_buf, uint output_row, int num_rows)
		{
			uint num_cols=cinfo.output_width;
			while(--num_rows>=0)
			{
				byte[] inptr=input_buf[0][input_row++];
				byte[] outptr=output_buf[output_row++];
				for(uint col=0, outptr_ind=0; col<num_cols; col++, outptr_ind+=RGB_PIXELSIZE)
				{
					outptr[outptr_ind+RGB_RED]=outptr[outptr_ind+RGB_GREEN]=outptr[outptr_ind+RGB_BLUE]=inptr[col];
				}
			}
		}

		// Adobe-style YCCK->CMYK conversion.
		// We convert YCbCr to R=1-C, G=1-M, and B=1-Y using the same
		// conversion as above, while passing K (black) unchanged.
		// We assume build_ycc_rgb_table has been called.
		static void ycck_cmyk_convert(jpeg_decompress cinfo, byte[][][] input_buf, uint input_row, byte[][] output_buf, uint output_row, int num_rows)
		{
			my_color_deconverter cconvert=(my_color_deconverter)cinfo.cconvert;
			uint num_cols=cinfo.output_width;

			int[] Crrtab=cconvert.Cr_r_tab;
			int[] Cbbtab=cconvert.Cb_b_tab;
			int[] Crgtab=cconvert.Cr_g_tab;
			int[] Cbgtab=cconvert.Cb_g_tab;

			while(--num_rows>=0)
			{
				byte[] inptr0=input_buf[0][input_row];
				byte[] inptr1=input_buf[1][input_row];
				byte[] inptr2=input_buf[2][input_row];
				byte[] inptr3=input_buf[3][input_row];
				input_row++;
				byte[] outptr=output_buf[output_row++];
				int outptr_ind=0;
				for(uint col=0; col<num_cols; col++)
				{
					int y=inptr0[col];
					int cb=inptr1[col];
					int cr=inptr2[col];

					// Range-limiting is essential due to noise introduced by DCT losses
					int tmp=MAXJSAMPLE-(y+Crrtab[cr]); // red
					outptr[outptr_ind++]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
					tmp=MAXJSAMPLE-(y+((int)((Cbgtab[cb]+Crgtab[cr])>>SCALEBITS))); // green
					outptr[outptr_ind++]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));
					tmp=MAXJSAMPLE-(y+Cbbtab[cb]); // blue
					outptr[outptr_ind++]=(byte)(tmp>=MAXJSAMPLE?MAXJSAMPLE:(tmp<0?0:tmp));

					// K passes through unchanged
					outptr[outptr_ind++]=inptr3[col];
				}
			}
		}

		// Empty method for start_pass.
		static void start_pass_dcolor(jpeg_decompress cinfo)
		{
			// no work needed
		}

		// Module initialization routine for output colorspace conversion.
		public static void jinit_color_deconverter(jpeg_decompress cinfo)
		{
			my_color_deconverter cconvert=null;

			try
			{
				cconvert=new my_color_deconverter();
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}

			cinfo.cconvert=cconvert;
			cconvert.start_pass=start_pass_dcolor;

			// Make sure num_components agrees with jpeg_color_space
			switch(cinfo.jpeg_color_space)
			{
				case J_COLOR_SPACE.JCS_GRAYSCALE: if(cinfo.num_components!=1) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_J_COLORSPACE); break;
				case J_COLOR_SPACE.JCS_RGB:
				case J_COLOR_SPACE.JCS_YCbCr: if(cinfo.num_components!=3) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_J_COLORSPACE); break;
				case J_COLOR_SPACE.JCS_CMYK:
				case J_COLOR_SPACE.JCS_YCCK: if(cinfo.num_components!=4) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_J_COLORSPACE); break;
				default: if(cinfo.num_components<1) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_J_COLORSPACE); break; // JCS_UNKNOWN can be anything
			}

			// Set out_color_components and conversion method based on requested space.
			// Also clear the component_needed flags for any unused components,
			// so that earlier pipeline stages can avoid useless computation.
			switch(cinfo.out_color_space)
			{
				case J_COLOR_SPACE.JCS_GRAYSCALE:
					cinfo.out_color_components=1;
					if(cinfo.jpeg_color_space==J_COLOR_SPACE.JCS_GRAYSCALE||cinfo.jpeg_color_space==J_COLOR_SPACE.JCS_YCbCr)
					{
						cconvert.color_convert=grayscale_convert;
						// For color->grayscale conversion, only the Y (0) component is needed
						for(int ci=1; ci<cinfo.num_components; ci++) cinfo.comp_info[ci].component_needed=false;
					}
					else ERREXIT(cinfo, J_MESSAGE_CODE.JERR_CONVERSION_NOTIMPL);
					break;
				case J_COLOR_SPACE.JCS_RGB:
					cinfo.out_color_components=RGB_PIXELSIZE;
					if(cinfo.jpeg_color_space==J_COLOR_SPACE.JCS_YCbCr)
					{
						cconvert.color_convert=ycc_rgb_convert;
						build_ycc_rgb_table(cinfo);
					}
					else if(cinfo.jpeg_color_space==J_COLOR_SPACE.JCS_GRAYSCALE)
					{
						cconvert.color_convert=gray_rgb_convert;
					}
					else if(cinfo.jpeg_color_space==J_COLOR_SPACE.JCS_RGB&&RGB_PIXELSIZE==3) cconvert.color_convert=null_convert;
					else ERREXIT(cinfo, J_MESSAGE_CODE.JERR_CONVERSION_NOTIMPL);
					break;
				case J_COLOR_SPACE.JCS_CMYK:
					cinfo.out_color_components=4;
					if(cinfo.jpeg_color_space==J_COLOR_SPACE.JCS_YCCK)
					{
						cconvert.color_convert=ycck_cmyk_convert;
						build_ycc_rgb_table(cinfo);
					}
					else if(cinfo.jpeg_color_space==J_COLOR_SPACE.JCS_CMYK) cconvert.color_convert=null_convert;
					else ERREXIT(cinfo, J_MESSAGE_CODE.JERR_CONVERSION_NOTIMPL);
					break;
				default:
					// Permit null conversion to same output space
					if(cinfo.out_color_space==cinfo.jpeg_color_space)
					{
						cinfo.out_color_components=cinfo.num_components;
						cconvert.color_convert=null_convert;
					}
					else ERREXIT(cinfo, J_MESSAGE_CODE.JERR_CONVERSION_NOTIMPL); // unsupported non-null conversion
					break;
			}

			if(cinfo.quantize_colors) cinfo.output_components=1; // single colormapped output component
			else cinfo.output_components=cinfo.out_color_components;
		}
	}
}
