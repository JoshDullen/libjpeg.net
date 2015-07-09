// jcsample.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1991-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains downsampling routines.
//
// Downsampling input data is counted in "row groups". A row group
// is defined to be max_v_samp_factor pixel rows of each component,
// from which the downsampler produces v_samp_factor sample rows.
// A single row group is processed in each call to the downsampler module.
//
// The downsampler is responsible for edge-expansion of its output data
// to fill an integral number of DCT blocks horizontally. The source buffer
// may be modified if it is helpful for this purpose (the source buffer is
// allocated wide enough to correspond to the desired output width).
// The caller (the prep controller) is responsible for vertical padding.
//
// The downsampler may request "context rows" by setting need_context_rows
// during startup. In this case, the input arrays will contain at least
// one row group's worth of pixels above and below the passed-in data;
// the caller will create dummy rows at image top and bottom by replicating
// the first or last real pixel row.
//
// An excellent reference for image resampling is
//	Digital Image Warping, George Wolberg, 1990.
//	Pub. by IEEE Computer Society Press, Los Alamitos, CA. ISBN 0-8186-8944-7.
//
// The downsampling algorithm used here is a simple average of the source
// pixels covered by the output pixel. The hi-falutin sampling literature
// refers to this as a "box filter". In general the characteristics of a box
// filter are not very good, but for the specific cases we normally use (1:1
// and 2:1 ratios) the box is equivalent to a "triangle filter" which is not
// nearly so bad. If you intend to use other sampling ratios, you'd be well
// advised to improve this code.
//
// A simple input-smoothing capability is provided. This is mainly intended
// for cleaning up color-dithered GIF input files (if you find it inadequate,
// we suggest using an external filtering program such as pnmconvol). When
// enabled, each input pixel P is replaced by a weighted sum of itself and its
// eight neighbors. P's weight is 1-8*SF and each neighbor's weight is SF,
// where SF = (smoothing_factor / 1024).
// Currently, smoothing is only supported for 2h2v sampling factors.

using System;

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Pointer to routine to downsample a single component
		delegate void downsample1_ptr(jpeg_compress cinfo, jpeg_component_info compptr, byte[][] input_data, uint in_row_index, byte[][] output_data, uint out_row_index);

		// Private subobject
		class my_downsampler : jpeg_downsampler
		{
			// Downsampling method pointers, one per component
			public downsample1_ptr[] methods=new downsample1_ptr[MAX_COMPONENTS];
		}

		// Initialize for a downsampling pass.
		static void start_pass_downsample(jpeg_compress cinfo)
		{
			// no work for now
		}

		// Expand a component horizontally from width input_cols to width output_cols,
		// by duplicating the rightmost samples.
		static void expand_right_edge(byte[][] image_data, uint in_row_index, int num_rows, uint input_cols, uint output_cols)
		{
			int numcols=(int)(output_cols-input_cols);

			if(numcols>0)
			{
				for(int row=0; row<num_rows; row++)
				{
					byte[] ptr=image_data[in_row_index+row];
					byte pixval=ptr[input_cols-1];
					uint ind=input_cols;
					for(int count=numcols; count>0; count--) ptr[ind++]=pixval;
				}
			}
		}

		// Do downsampling for a whole row group (all components).
		// In this version we simply downsample each component independently.
		static void sep_downsample(jpeg_compress cinfo, byte[][][] input_buf, uint in_row_index, byte[][][] output_buf, uint out_row_group_index)
		{
			my_downsampler downsample=(my_downsampler)cinfo.downsample;

			for(int ci=0; ci<cinfo.num_components; ci++)
				downsample.methods[ci](cinfo, cinfo.comp_info[ci], input_buf[ci], in_row_index, output_buf[ci], (uint)(out_row_group_index*cinfo.comp_info[ci].v_samp_factor));
		}

		// Downsample pixel values of a single component.
		// One row group is processed per call.
		// This version handles arbitrary integral sampling ratios, without smoothing.
		// Note that this version is not actually used for customary sampling ratios.
		static void int_downsample(jpeg_compress cinfo, jpeg_component_info compptr, byte[][] input_data, uint in_row_index, byte[][] output_data, uint out_row_index)
		{
			uint output_cols=compptr.width_in_blocks*cinfo.DCT_size;
			int h_expand=cinfo.max_h_samp_factor/compptr.h_samp_factor;
			int v_expand=cinfo.max_v_samp_factor/compptr.v_samp_factor;
			int numpix=h_expand*v_expand;
			int numpix2=numpix/2;

			// Expand input data enough to let all the output samples be generated
			// by the standard loop. Special-casing padded output would be more
			// efficient.
			expand_right_edge(input_data, in_row_index, cinfo.max_v_samp_factor, cinfo.image_width, (uint)(output_cols*h_expand));

			int inrow=(int)in_row_index;
			for(int outrow=0; outrow<compptr.v_samp_factor; outrow++)
			{
				byte[] outptr=output_data[out_row_index+outrow];
				for(uint outcol=0, outcol_h=0; outcol<output_cols; outcol++, outcol_h+=(uint)h_expand)
				{
					byte[] inptr;
					int outvalue=0;
					for(int v=0; v<v_expand; v++)
					{
						inptr=input_data[inrow+v];
						for(int h=0; h<h_expand; h++) outvalue+=(int)inptr[outcol_h+h];
					}
					outptr[outcol]=(byte)((outvalue+numpix2)/numpix);
				}
				inrow+=v_expand;
			}
		}

		// Downsample pixel values of a single component.
		// This version handles the special case of a full-size component,
		// without smoothing.
		static void fullsize_downsample(jpeg_compress cinfo, jpeg_component_info compptr, byte[][] input_data, uint in_row_index, byte[][] output_data, uint out_row_index)
		{
			// Copy the data
			jcopy_sample_rows(input_data, (int)in_row_index, output_data, (int)out_row_index, cinfo.max_v_samp_factor, cinfo.image_width);

			// Edge-expand
			expand_right_edge(output_data, out_row_index, cinfo.max_v_samp_factor, cinfo.image_width, compptr.width_in_blocks*cinfo.DCT_size);
		}

		// Downsample pixel values of a single component.
		// This version handles the common case of 2:1 horizontal and 1:1 vertical,
		// without smoothing.
		//
		// A note about the "bias" calculations: when rounding fractional values to
		// integer, we do not want to always round 0.5 up to the next integer.
		// If we did that, we'd introduce a noticeable bias towards larger values.
		// Instead, this code is arranged so that 0.5 will be rounded up or down at
		// alternate pixel locations (a simple ordered dither pattern).
		static void h2v1_downsample(jpeg_compress cinfo, jpeg_component_info compptr, byte[][] input_data, uint in_row_index, byte[][] output_data, uint out_row_index)
		{
			uint output_cols=compptr.width_in_blocks*cinfo.DCT_size;

			// Expand input data enough to let all the output samples be generated
			// by the standard loop. Special-casing padded output would be more
			// efficient.
			expand_right_edge(input_data, in_row_index, cinfo.max_v_samp_factor, cinfo.image_width, output_cols*2);

			for(int outrow=0; outrow<compptr.v_samp_factor; outrow++)
			{
				byte[] outptr=output_data[out_row_index+outrow];
				byte[] inptr=input_data[in_row_index+outrow];
				int bias=0; // bias = 0,1,0,1,... for successive samples
				for(uint outcol=0, ind=0; outcol<output_cols; outcol++, ind+=2)
				{
					outptr[outcol]=(byte)((inptr[ind]+inptr[ind+1]+bias)>>1);
					bias^=1; // 0=>1, 1=>0
				}
			}
		}

		// Downsample pixel values of a single component.
		// This version handles the standard case of 2:1 horizontal and 2:1 vertical,
		// without smoothing.
		static void h2v2_downsample(jpeg_compress cinfo, jpeg_component_info compptr, byte[][] input_data, uint in_row_index, byte[][] output_data, uint out_row_index)
		{
			uint output_cols=compptr.width_in_blocks*cinfo.DCT_size;

			// Expand input data enough to let all the output samples be generated
			// by the standard loop. Special-casing padded output would be more
			// efficient.
			expand_right_edge(input_data, in_row_index, cinfo.max_v_samp_factor, cinfo.image_width, output_cols*2);

			int inrow=(int)in_row_index;
			for(int outrow=0; outrow<compptr.v_samp_factor; outrow++)
			{
				byte[] outptr=output_data[out_row_index+outrow];
				byte[] inptr0=input_data[inrow];
				byte[] inptr1=input_data[inrow+1];
				int bias=1; // bias = 1,2,1,2,... for successive samples
				for(uint outcol=0, ind=0; outcol<output_cols; outcol++, ind+=2)
				{
					outptr[outcol]=(byte)((inptr0[ind]+inptr0[ind+1]+inptr1[ind]+inptr1[ind+1]+bias)>>2);
					bias^=3; // 1=>2, 2=>1
				}
				inrow+=2;
			}
		}

#if INPUT_SMOOTHING_SUPPORTED
		// Downsample pixel values of a single component.
		// This version handles the standard case of 2:1 horizontal and 2:1 vertical,
		// with smoothing. One row of context is required.
		static void h2v2_smooth_downsample(jpeg_compress cinfo, jpeg_component_info compptr, byte[][] input_data, uint in_row_index, byte[][] output_data, uint out_row_index)
		{
			uint output_cols=compptr.width_in_blocks*cinfo.DCT_size;

			// Expand input data enough to let all the output samples be generated
			// by the standard loop. Special-casing padded output would be more efficient.
			expand_right_edge(input_data, in_row_index-1, cinfo.max_v_samp_factor+2, cinfo.image_width, output_cols*2);

			// We don't bother to form the individual "smoothed" input pixel values;
			// we can directly compute the output which is the average of the four
			// smoothed values. Each of the four member pixels contributes a fraction
			// (1-8*SF) to its own smoothed image and a fraction SF to each of the three
			// other smoothed pixels, therefore a total fraction (1-5*SF)/4 to the final
			// output. The four corner-adjacent neighbor pixels contribute a fraction
			// SF to just one smoothed pixel, or SF/4 to the final output; while the
			// eight edge-adjacent neighbors contribute SF to each of two smoothed
			// pixels, or SF/2 overall. In order to use integer arithmetic, these
			// factors are scaled by 2^16 = 65536.
			// Also recall that SF = smoothing_factor / 1024.
			int memberscale=16384-cinfo.smoothing_factor*80; // scaled (1-5*SF)/4
			int neighscale=cinfo.smoothing_factor*16; // scaled SF/4

			int inrow=(int)in_row_index;
			for(int outrow=0; outrow<compptr.v_samp_factor; outrow++)
			{
				byte[] outptr=output_data[out_row_index+outrow];
				byte[] inptr0=input_data[inrow];
				byte[] inptr1=input_data[inrow+1];
				byte[] above_ptr=input_data[inrow-1];
				byte[] below_ptr=input_data[inrow+2];

				// Special case for first column: pretend column -1 is same as column 0
				int membersum=inptr0[0]+inptr0[1]+inptr1[0]+inptr1[1];
				int neighsum=above_ptr[0]+above_ptr[1]+below_ptr[0]+below_ptr[1]+inptr0[0]+inptr0[2]+inptr1[0]+inptr1[2];
				neighsum+=neighsum;
				neighsum+=above_ptr[0]+above_ptr[2]+below_ptr[0]+below_ptr[2];
				membersum=membersum*memberscale+neighsum*neighscale;
				outptr[0]=(byte)((membersum+32768)>>16);
				int iind=2, oind=1;

				for(uint colctr=output_cols-2; colctr>0; colctr--)
				{
					// sum of pixels directly mapped to this output element
					membersum=inptr0[iind]+inptr0[iind+1]+inptr1[iind]+inptr1[iind+1];
					// sum of edge-neighbor pixels
					neighsum=above_ptr[iind]+above_ptr[iind+1]+below_ptr[iind]+below_ptr[iind+1]+inptr0[iind-1]+inptr0[iind+2]+inptr1[iind-1]+inptr1[iind+2];
					// The edge-neighbors count twice as much as corner-neighbors
					neighsum+=neighsum;
					// Add in the corner-neighbors
					neighsum+=above_ptr[iind-1]+above_ptr[iind+2]+below_ptr[iind-1]+below_ptr[iind+2];
					// form final output scaled up by 2^16
					membersum=membersum*memberscale+neighsum*neighscale;
					// round, descale and output it
					outptr[oind]=(byte)((membersum+32768)>>16);
					iind+=2;
					oind++;
				}

				// Special case for last column
				membersum=inptr0[iind]+inptr0[iind+1]+inptr1[iind]+inptr1[iind+1];
				neighsum=above_ptr[iind]+above_ptr[iind+1]+below_ptr[iind]+below_ptr[iind+1]+inptr0[iind-1]+inptr0[iind+1]+inptr1[iind-1]+inptr1[iind+1];
				neighsum+=neighsum;
				neighsum+=above_ptr[iind-1]+above_ptr[iind+1]+below_ptr[iind-1]+below_ptr[iind+1];
				membersum=membersum*memberscale+neighsum*neighscale;
				outptr[oind]=(byte)((membersum+32768)>>16);

				inrow+=2;
			}
		}

		// Downsample pixel values of a single component.
		// This version handles the special case of a full-size component,
		// with smoothing. One row of context is required.
		static void fullsize_smooth_downsample(jpeg_compress cinfo, jpeg_component_info compptr, byte[][] input_data, uint in_row_index, byte[][] output_data, uint out_row_index)
		{
			uint output_cols=compptr.width_in_blocks*cinfo.DCT_size;

			// Expand input data enough to let all the output samples be generated
			// by the standard loop. Special-casing padded output would be more
			// efficient.
			expand_right_edge(input_data, in_row_index-1, cinfo.max_v_samp_factor+2, cinfo.image_width, output_cols);

			// Each of the eight neighbor pixels contributes a fraction SF to the
			// smoothed pixel, while the main pixel contributes (1-8*SF). In order
			// to use integer arithmetic, these factors are multiplied by 2^16 = 65536.
			// Also recall that SF = smoothing_factor / 1024.
			int memberscale=65536-cinfo.smoothing_factor*512; // scaled 1-8*SF
			int neighscale=cinfo.smoothing_factor*64; // scaled SF

			int membersum, neighsum;
			int colsum, lastcolsum, nextcolsum;

			for(int outrow=0; outrow<compptr.v_samp_factor; outrow++)
			{
				byte[] outptr=output_data[out_row_index+outrow];
				byte[] inptr=input_data[in_row_index+outrow];
				byte[] above_ptr=input_data[in_row_index+outrow-1];
				byte[] below_ptr=input_data[in_row_index+outrow+1];

				int iind=0, aind=0, bind=0, oind=0;

				// Special case for first column
				colsum=above_ptr[aind++]+below_ptr[bind++]+inptr[iind];
				membersum=inptr[iind++];
				nextcolsum=above_ptr[aind]+below_ptr[bind]+inptr[iind];
				neighsum=colsum+(colsum-membersum)+nextcolsum;
				membersum=membersum*memberscale+neighsum*neighscale;
				outptr[oind++]=(byte)((membersum+32768)>>16);
				lastcolsum=colsum; colsum=nextcolsum;

				for(uint colctr=output_cols-2; colctr>0; colctr--)
				{
					membersum=inptr[iind++];
					nextcolsum=above_ptr[aind++]+below_ptr[bind++]+inptr[iind];
					neighsum=lastcolsum+(colsum-membersum)+nextcolsum;
					membersum=membersum*memberscale+neighsum*neighscale;
					outptr[oind++]=(byte)((membersum+32768)>>16);
					lastcolsum=colsum; colsum=nextcolsum;
				}

				// Special case for last column
				membersum=inptr[iind];
				neighsum=lastcolsum+(colsum-membersum)+colsum;
				membersum=membersum*memberscale+neighsum*neighscale;
				outptr[oind]=(byte)((membersum+32768)>>16);
			}
		}
#endif // INPUT_SMOOTHING_SUPPORTED

		// Module initialization routine for downsampling.
		// Note that we must select a routine for each component.
		static void jinit_downsampler(jpeg_compress cinfo)
		{
			my_downsampler downsample=null;

			try
			{
				downsample=new my_downsampler();
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}

			cinfo.downsample=downsample;
			downsample.start_pass=start_pass_downsample;
			downsample.downsample=sep_downsample;
			downsample.need_context_rows=false;

			if(cinfo.CCIR601_sampling) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_CCIR601_NOTIMPL);

			bool smoothok=true;

			// Verify we can handle the sampling factors, and set up method pointers
			for(int ci=0; ci<cinfo.num_components; ci++)
			{
				jpeg_component_info compptr=cinfo.comp_info[ci];
				if(compptr.h_samp_factor==cinfo.max_h_samp_factor&&compptr.v_samp_factor==cinfo.max_v_samp_factor)
				{
#if INPUT_SMOOTHING_SUPPORTED
					if(cinfo.smoothing_factor!=0)
					{
						downsample.methods[ci]=fullsize_smooth_downsample;
						downsample.need_context_rows=true;
					}
					else
#endif
						downsample.methods[ci]=fullsize_downsample;
				}
				else if(compptr.h_samp_factor*2==cinfo.max_h_samp_factor&&compptr.v_samp_factor==cinfo.max_v_samp_factor)
				{
					smoothok=false;
					downsample.methods[ci]=h2v1_downsample;
				}
				else if(compptr.h_samp_factor*2==cinfo.max_h_samp_factor&&compptr.v_samp_factor*2==cinfo.max_v_samp_factor)
				{
#if INPUT_SMOOTHING_SUPPORTED
					if(cinfo.smoothing_factor!=0)
					{
						downsample.methods[ci]=h2v2_smooth_downsample;
						downsample.need_context_rows=true;
					}
					else
#endif
						downsample.methods[ci]=h2v2_downsample;
				}
				else if((cinfo.max_h_samp_factor%compptr.h_samp_factor)==0&&(cinfo.max_v_samp_factor%compptr.v_samp_factor)==0)
				{
					smoothok=false;
					downsample.methods[ci]=int_downsample;
				}
				else ERREXIT(cinfo, J_MESSAGE_CODE.JERR_FRACT_SAMPLE_NOTIMPL);
			}

#if INPUT_SMOOTHING_SUPPORTED
			if(cinfo.smoothing_factor!=0&&!smoothok) TRACEMS(cinfo, 0, J_MESSAGE_CODE.JTRC_SMOOTH_NOTIMPL);
#endif
		}
	}
}
