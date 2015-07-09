// jccolor.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1991-1996, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains input colorspace conversion routines.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Private subobject
		class my_color_converter : jpeg_color_converter
		{
			// Private state for RGB->YCC conversion
			public int[] rgb_ycc_tab; // => table for RGB to YCbCr conversion
		}

		// *************** RGB -> YCbCr conversion: most common case *************

		// YCbCr is defined per CCIR 601-1, except that Cb and Cr are
		// normalized to the range 0..MAXJSAMPLE rather than -0.5 .. 0.5.
		// The conversion equations to be implemented are therefore
		//		Y	=	 0.29900 * R + 0.58700 * G + 0.11400 * B
		//		Cb	=	-0.16874 * R - 0.33126 * G + 0.50000 * B + CENTERJSAMPLE
		//		Cr	=	 0.50000 * R - 0.41869 * G - 0.08131 * B + CENTERJSAMPLE
		// (These numbers are derived from TIFF 6.0 section 21, dated 3-June-92.)
		// Note: older versions of the IJG code used a zero offset of MAXJSAMPLE/2,
		// rather than CENTERJSAMPLE, for Cb and Cr. This gave equal positive and
		// negative swings for Cb/Cr, but meant that grayscale values (Cb=Cr=0)
		// were not represented exactly. Now we sacrifice exact representation of
		// maximum red and maximum blue in order to get exact grayscales.
		//
		// To avoid floating-point arithmetic, we represent the fractional constants
		// as integers scaled up by 2^16 (about 4 digits precision); we have to divide
		// the products by 2^16, with appropriate rounding, to get the correct answer.
		//
		// For even more speed, we avoid doing any multiplications in the inner loop
		// by precalculating the constants times R,G,B for all possible values.
		// For 8-bit samples this is very reasonable (only 256 entries per table);
		// for 12-bit samples it is still acceptable. It's not very reasonable for
		// 16-bit samples, but if you want lossless storage you shouldn't be changing
		// colorspace anyway.
		// The CENTERJSAMPLE offsets and the rounding fudge-factor of 0.5 are included
		// in the tables to save adding them separately in the inner loop.

		const int SCALEBITS=23;
		const int CBCR_OFFSET=CENTERJSAMPLE<<SCALEBITS;
		const int ONE_HALF=1<<(SCALEBITS-1);

		// We allocate one big table and divide it up into eight parts, instead of
		// doing eight alloc requests. This lets us use a single table base
		// address, which can be held in a register in the inner loops on many
		// machines (more than can hold all eight addresses, anyway).

		const int R_Y_OFF=0;					// offset to R => Y section
		const int G_Y_OFF=(1*(MAXJSAMPLE+1));	// offset to G => Y section
		const int B_Y_OFF=(2*(MAXJSAMPLE+1));	// etc.
		const int R_CB_OFF=(3*(MAXJSAMPLE+1));
		const int G_CB_OFF=(4*(MAXJSAMPLE+1));
		const int B_CB_OFF=(5*(MAXJSAMPLE+1));
		const int R_CR_OFF=B_CB_OFF;			// B=>Cb, R=>Cr are the same
		const int G_CR_OFF=(6*(MAXJSAMPLE+1));
		const int B_CR_OFF=(7*(MAXJSAMPLE+1));
		const int TABLE_SIZE=(8*(MAXJSAMPLE+1));

		// Initialize for RGB->YCC colorspace conversion.
		static void rgb_ycc_start(jpeg_compress cinfo)
		{
			my_color_converter cconvert=(my_color_converter)cinfo.cconvert;
			int[] rgb_ycc_tab=null;
			try
			{
				// Allocate and fill in the conversion tables.
				cconvert.rgb_ycc_tab=rgb_ycc_tab=new int[TABLE_SIZE];
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}
			for(int i=0; i<=MAXJSAMPLE; i++)
			{
				rgb_ycc_tab[i+R_Y_OFF]=FIX_029900*i;
				rgb_ycc_tab[i+G_Y_OFF]=FIX_058700*i;
				rgb_ycc_tab[i+B_Y_OFF]=FIX_011400*i+ONE_HALF;
				rgb_ycc_tab[i+R_CB_OFF]=(-FIX_016874)*i;
				rgb_ycc_tab[i+G_CB_OFF]=(-FIX_033126)*i;
				// We use a rounding fudge-factor of 0.5-epsilon for Cb and Cr.
				// This ensures that the maximum output will round to MAXJSAMPLE
				// not MAXJSAMPLE+1, and thus that we don't have to range-limit.
				rgb_ycc_tab[i+B_CB_OFF]=FIX_050000*i+CBCR_OFFSET+ONE_HALF-1;
				// B=>Cb and R=>Cr tables are the same
				// rgb_ycc_tab[i+R_CR_OFF]=FIX_050000*i+CBCR_OFFSET+ONE_HALF-1;
				rgb_ycc_tab[i+G_CR_OFF]=(-FIX_041869)*i;
				rgb_ycc_tab[i+B_CR_OFF]=(-FIX_008131)*i;
			}
		}
		
		// Convert some rows of samples to the JPEG colorspace.
		//
		// Note that we change from the application's interleaved-pixel format
		// to our internal noninterleaved, one-plane-per-component format.
		// The input buffer is therefore three times as wide as the output buffer.
		//
		// A starting row offset is provided only for the output buffer. The caller
		// can easily adjust the passed input_buf value to accommodate any row
		// offset required on that side.
		static void rgb_ycc_convert(jpeg_compress cinfo, byte[][] input_buf, uint in_row_index, byte[][][] output_buf, uint output_row, int num_rows)
		{
			my_color_converter cconvert=(my_color_converter)cinfo.cconvert;
			int r, g, b;
			int[] ctab=cconvert.rgb_ycc_tab;
			uint num_cols=cinfo.image_width;

			for(uint input_row=0; input_row<num_rows; input_row++)
			{
				byte[] inptr=input_buf[in_row_index+input_row];
				byte[] outptr0=output_buf[0][output_row];
				byte[] outptr1=output_buf[1][output_row];
				byte[] outptr2=output_buf[2][output_row];
				output_row++;
				for(uint col=0, off=0; col<num_cols; col++, off+=RGB_PIXELSIZE)
				{
					r=inptr[off+RGB_RED];
					g=inptr[off+RGB_GREEN];
					b=inptr[off+RGB_BLUE];
					// If the inputs are 0..MAXJSAMPLE, the outputs of these equations
					// must be too; we do not need an explicit range-limiting operation.
					outptr0[col]=(byte)((ctab[r+R_Y_OFF]+ctab[g+G_Y_OFF]+ctab[b+B_Y_OFF])>>SCALEBITS); // Y
					outptr1[col]=(byte)((ctab[r+R_CB_OFF]+ctab[g+G_CB_OFF]+ctab[b+B_CB_OFF])>>SCALEBITS); // Cb
					outptr2[col]=(byte)((ctab[r+R_CR_OFF]+ctab[g+G_CR_OFF]+ctab[b+B_CR_OFF])>>SCALEBITS); // Cr
					//outptr0[col]=(byte)(0.299*r+0.587*g+0.114*b);
					//outptr1[col]=(byte)(-0.168736*r-0.331264*g+0.5*b+CENTERJSAMPLE);
					//outptr2[col]=(byte)(0.5*r-0.418688*g-0.081312*b+CENTERJSAMPLE);
				}
			}
		}

		// *************** Cases other than RGB -> YCbCr *************
		
		// Convert some rows of samples to the JPEG colorspace.
		// This version handles RGB->grayscale conversion, which is the same
		// as the RGB->Y portion of RGB->YCbCr.
		// We assume rgb_ycc_start has been called (we only use the Y tables).
		static void rgb_gray_convert(jpeg_compress cinfo, byte[][] input_buf, uint in_row_index, byte[][][] output_buf, uint output_row, int num_rows)
		{
			my_color_converter cconvert=(my_color_converter)cinfo.cconvert;
			int r, g, b;
			int[] ctab=cconvert.rgb_ycc_tab;
			uint num_cols=cinfo.image_width;

			for(uint input_row=0; input_row<num_rows; input_row++)
			{
				byte[] inptr=input_buf[in_row_index+input_row];
				byte[] outptr=output_buf[0][output_row];
				output_row++;
				for(uint col=0, off=0; col<num_cols; col++, off+=RGB_PIXELSIZE)
				{
					r=inptr[off+RGB_RED];
					g=inptr[off+RGB_GREEN];
					b=inptr[off+RGB_BLUE];
					outptr[col]=(byte)((ctab[r+R_Y_OFF]+ctab[g+G_Y_OFF]+ctab[b+B_Y_OFF])>>SCALEBITS); // Y
				}
			}
		}
		
		// Convert some rows of samples to the JPEG colorspace.
		// This version handles Adobe-style CMYK->YCCK conversion,
		// where we convert R=1-C, G=1-M, and B=1-Y to YCbCr using the same
		// conversion as above, while passing K (black) unchanged.
		// We assume rgb_ycc_start has been called.
		static void cmyk_ycck_convert(jpeg_compress cinfo, byte[][] input_buf, uint in_row_index, byte[][][] output_buf, uint output_row, int num_rows)
		{
			my_color_converter cconvert=(my_color_converter)cinfo.cconvert;
			int r, g, b;
			int[] ctab=cconvert.rgb_ycc_tab;
			uint num_cols=cinfo.image_width;

			for(uint input_row=0; input_row<num_rows; input_row++)
			{
				byte[] inptr=input_buf[in_row_index+input_row];
				byte[] outptr0=output_buf[0][output_row];
				byte[] outptr1=output_buf[1][output_row];
				byte[] outptr2=output_buf[2][output_row];
				byte[] outptr3=output_buf[3][output_row];
				output_row++;
				for(uint col=0, off=0; col<num_cols; col++, off+=4)
				{
					r=MAXJSAMPLE-inptr[off+0];
					g=MAXJSAMPLE-inptr[off+1];
					b=MAXJSAMPLE-inptr[off+2];
					// K passes through as-is
					outptr3[col]=inptr[off+3];
					// If the inputs are 0..MAXJSAMPLE, the outputs of these equations
					// must be too; we do not need an explicit range-limiting operation.
					outptr0[col]=(byte)((ctab[r+R_Y_OFF]+ctab[g+G_Y_OFF]+ctab[b+B_Y_OFF])>>SCALEBITS); // Y
					outptr1[col]=(byte)((ctab[r+R_CB_OFF]+ctab[g+G_CB_OFF]+ctab[b+B_CB_OFF])>>SCALEBITS); // Cb
					outptr2[col]=(byte)((ctab[r+R_CR_OFF]+ctab[g+G_CR_OFF]+ctab[b+B_CR_OFF])>>SCALEBITS); // Cr
				}
			}
		}
		
		// Convert some rows of samples to the JPEG colorspace.
		// This version handles grayscale output with no conversion.
		// The source can be either plain grayscale or YCbCr (since Y == gray).
		static void grayscale_convert(jpeg_compress cinfo, byte[][] input_buf, uint in_row_index, byte[][][] output_buf, uint output_row, int num_rows)
		{
			uint num_cols=cinfo.image_width;
			int instride=cinfo.input_components;

			for(uint input_row=0; input_row<num_rows; input_row++)
			{
				byte[] inptr=input_buf[in_row_index+input_row];
				byte[] outptr=output_buf[0][output_row];
				output_row++;
				for(uint col=0, off=0; col<num_cols; col++, off+=(uint)instride) outptr[col]=inptr[off];
			}
		}

		// Convert some rows of samples to the JPEG colorspace.
		// This version handles multi-component colorspaces without conversion.
		// We assume input_components == num_components.
		static void null_convert(jpeg_compress cinfo, byte[][] input_buf, uint in_row_index, byte[][][] output_buf, uint output_row, int num_rows)
		{
			int nc=cinfo.num_components;
			uint num_cols=cinfo.image_width;

			for(uint input_row=0; input_row<num_rows; input_row++)
			{
				// It seems fastest to make a separate pass for each component.
				byte[] inptr=input_buf[in_row_index+input_row];
				for(int ci=0; ci<nc; ci++)
				{
					byte[] outptr=output_buf[ci][output_row];
					for(uint col=0, off=(uint)ci; col<num_cols; col++, off+=(uint)nc) outptr[col]=inptr[off];
				}
				output_row++;
			}
		}

		// Empty method for start_pass.
		static void null_method(jpeg_compress cinfo)
		{
			// no work needed
		}

		// Module initialization routine for input colorspace conversion.
		static void jinit_color_converter(jpeg_compress cinfo)
		{
			my_color_converter cconvert=null;
			try
			{
				cinfo.cconvert=cconvert=new my_color_converter();
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}

			// set start_pass to null method until we find out differently
			cconvert.start_pass=null_method;

			// Make sure input_components agrees with in_color_space
			switch(cinfo.in_color_space)
			{
				case J_COLOR_SPACE.JCS_GRAYSCALE:
					if(cinfo.input_components!=1) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_IN_COLORSPACE);
					break;
				case J_COLOR_SPACE.JCS_RGB:
				case J_COLOR_SPACE.JCS_YCbCr:
					if(cinfo.input_components!=3) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_IN_COLORSPACE);
					break;
				case J_COLOR_SPACE.JCS_CMYK:
				case J_COLOR_SPACE.JCS_YCCK:
					if(cinfo.input_components!=4) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_IN_COLORSPACE);
					break;
				default: // JCS_UNKNOWN can be anything
					if(cinfo.input_components<1) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_IN_COLORSPACE);
					break;
			}

			// Check num_components, set conversion method based on requested space
			switch(cinfo.jpeg_color_space)
			{
				case J_COLOR_SPACE.JCS_GRAYSCALE:
					if(cinfo.num_components!=1) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_J_COLORSPACE);
					if(cinfo.in_color_space==J_COLOR_SPACE.JCS_GRAYSCALE) cconvert.color_convert=grayscale_convert;
					else if(cinfo.in_color_space==J_COLOR_SPACE.JCS_RGB)
					{
						cconvert.start_pass=rgb_ycc_start;
						cconvert.color_convert=rgb_gray_convert;
					}
					else if(cinfo.in_color_space==J_COLOR_SPACE.JCS_YCbCr) cconvert.color_convert=grayscale_convert;
					else ERREXIT(cinfo, J_MESSAGE_CODE.JERR_CONVERSION_NOTIMPL);
					break;
				case J_COLOR_SPACE.JCS_RGB:
					if(cinfo.num_components!=3) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_J_COLORSPACE);
					if(cinfo.in_color_space==J_COLOR_SPACE.JCS_RGB&&RGB_PIXELSIZE==3) cconvert.color_convert=null_convert;
					else ERREXIT(cinfo, J_MESSAGE_CODE.JERR_CONVERSION_NOTIMPL);
					break;
				case J_COLOR_SPACE.JCS_YCbCr:
					if(cinfo.num_components!=3) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_J_COLORSPACE);
					if(cinfo.in_color_space==J_COLOR_SPACE.JCS_RGB)
					{
						cconvert.start_pass=rgb_ycc_start;
						cconvert.color_convert=rgb_ycc_convert;
					}
					else if(cinfo.in_color_space==J_COLOR_SPACE.JCS_YCbCr) cconvert.color_convert=null_convert;
					else ERREXIT(cinfo, J_MESSAGE_CODE.JERR_CONVERSION_NOTIMPL);
					break;
				case J_COLOR_SPACE.JCS_CMYK:
					if(cinfo.num_components!=4) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_J_COLORSPACE);
					if(cinfo.in_color_space==J_COLOR_SPACE.JCS_CMYK) cconvert.color_convert=null_convert;
					else ERREXIT(cinfo, J_MESSAGE_CODE.JERR_CONVERSION_NOTIMPL);
					break;
				case J_COLOR_SPACE.JCS_YCCK:
					if(cinfo.num_components!=4)
						ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BAD_J_COLORSPACE);
					if(cinfo.in_color_space==J_COLOR_SPACE.JCS_CMYK)
					{
						cconvert.start_pass=rgb_ycc_start;
						cconvert.color_convert=cmyk_ycck_convert;
					}
					else if(cinfo.in_color_space==J_COLOR_SPACE.JCS_YCCK) cconvert.color_convert=null_convert;
					else ERREXIT(cinfo, J_MESSAGE_CODE.JERR_CONVERSION_NOTIMPL);
					break;
				default: // allow null conversion of JCS_UNKNOWN
					if(cinfo.jpeg_color_space!=cinfo.in_color_space||
					cinfo.num_components!=cinfo.input_components) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_CONVERSION_NOTIMPL);
					cconvert.color_convert=null_convert;
					break;
			}
		}
	}
}
