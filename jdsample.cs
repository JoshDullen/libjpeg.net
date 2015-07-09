// jdsample.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1991-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains upsampling routines.
//
// Upsampling input data is counted in "row groups". A row group
// is defined to be (v_samp_factor * DCT_scaled_size / min_DCT_scaled_size)
// sample rows of each component. Upsampling will normally produce
// max_v_samp_factor pixel rows from each row group (but this could vary
// if the upsampler is applying a scale factor of its own).
//
// An excellent reference for image resampling is
//	Digital Image Warping, George Wolberg, 1990.
//	Pub. by IEEE Computer Society Press, Los Alamitos, CA. ISBN 0-8186-8944-7.

using System;

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Pointer to routine to upsample a single component
		delegate void upsample1_ptr(jpeg_decompress cinfo, jpeg_component_info compptr, byte[][] input_data, int input_data_offset, byte[][][] output_data_ptr, int output_data_offset);

		// Private subobject
		class my_upsampler : jpeg_upsampler
		{
			// Color conversion buffer. When using separate upsampling and color
			// conversion steps, this buffer holds one upsampled row group until it
			// has been color converted and output.
			// Note: we do not allocate any storage for component(s) which are full-size,
			// ie do not need rescaling. The corresponding entry of color_buf[] is
			// simply set to point to the input data array, thereby avoiding copying.
			public byte[][][] color_buf=new byte[MAX_COMPONENTS][][];

			// Per-component upsampling method pointers
			public upsample1_ptr[] methods=new upsample1_ptr[MAX_COMPONENTS];

			public int next_row_out;	// counts rows emitted from color_buf
			public uint rows_to_go;		// counts rows remaining in image

			// Height of an input row group for each component.
			public int[] rowgroup_height=new int[MAX_COMPONENTS];

			// These arrays save pixel expansion factors so that int_expand need not
			// recompute them each time. They are unused for other upsampling methods.
			public byte[] h_expand=new byte[MAX_COMPONENTS];
			public byte[] v_expand=new byte[MAX_COMPONENTS];
		}

		// Initialize for an upsampling pass.
		static void start_pass_upsample(jpeg_decompress cinfo)
		{
			my_upsampler upsample=(my_upsampler)cinfo.upsample;

			// Mark the conversion buffer empty
			upsample.next_row_out=cinfo.max_v_samp_factor;
			// Initialize total-height counter for detecting bottom of image
			upsample.rows_to_go=cinfo.output_height;
		}

		// Control routine to do upsampling (and color conversion).
		//
		// In this version we upsample each component independently.
		// We upsample one row group into the conversion buffer, then apply
		// color conversion a row at a time.
		static void sep_upsample(jpeg_decompress cinfo, byte[][][] input_buf, ref uint in_row_group_ctr, uint in_row_groups_avail, byte[][] output_buf, uint output_buf_offset, ref uint out_row_ctr, uint out_rows_avail)
		{
			my_upsampler upsample=(my_upsampler)cinfo.upsample;

			// Fill the conversion buffer, if it's empty
			if(upsample.next_row_out>=cinfo.max_v_samp_factor)
			{
				for(int ci=0; ci<cinfo.num_components; ci++)
				{
					jpeg_component_info compptr=cinfo.comp_info[ci];

					// Invoke per-component upsample method. Notice we pass a POINTER
					// to color_buf[ci], so that fullsize_upsample can change it.
					upsample.methods[ci](cinfo, compptr, input_buf[ci], (int)(in_row_group_ctr*upsample.rowgroup_height[ci]), upsample.color_buf, ci);
				}
				upsample.next_row_out=0;
			}

			// Color-convert and emit rows

			// How many we have in the buffer:
			uint num_rows=(uint)(cinfo.max_v_samp_factor-upsample.next_row_out);

			// Not more than the distance to the end of the image. Need this test
			// in case the image height is not a multiple of max_v_samp_factor:
			if(num_rows>upsample.rows_to_go) num_rows=upsample.rows_to_go;

			// And not more than what the client can accept:
			out_rows_avail-=out_row_ctr;
			if(num_rows>out_rows_avail) num_rows=out_rows_avail;

			cinfo.cconvert.color_convert(cinfo, upsample.color_buf, (uint)upsample.next_row_out, output_buf, output_buf_offset+out_row_ctr, (int)num_rows);

			// Adjust counts
			out_row_ctr+=num_rows;
			upsample.rows_to_go-=num_rows;
			upsample.next_row_out+=(int)num_rows;

			// When the buffer is emptied, declare this input row group consumed
			if(upsample.next_row_out>=cinfo.max_v_samp_factor) in_row_group_ctr++;
		}

		// These are the routines invoked by sep_upsample to upsample pixel values
		// of a single component. One row group is processed per call.

		// For full-size components, we just make color_buf[ci] point at the
		// input buffer, and thus avoid copying any data. Note that this is
		// safe only because sep_upsample doesn't declare the input row group
		// "consumed" until we are done color converting and emitting it.
		static void fullsize_upsample(jpeg_decompress cinfo, jpeg_component_info compptr, byte[][] input_data, int input_data_offset, byte[][][] output_data_ptr, int output_data_offset)
		{		
			for(int i=0; i<cinfo.max_v_samp_factor; i++) output_data_ptr[output_data_offset][i]=input_data[input_data_offset++];
		}

		// This is a no-op version used for "uninteresting" components.
		// These components will not be referenced by color conversion.
		static void noop_upsample(jpeg_decompress cinfo, jpeg_component_info compptr, byte[][] input_data, int input_data_offset, byte[][][] output_data_ptr, int output_data_offset)
		{
			output_data_ptr[output_data_offset]=null; // safety check
		}

		// This version handles any integral sampling ratios.
		// This is not used for typical JPEG files, so it need not be fast.
		// Nor, for that matter, is it particularly accurate: the algorithm is
		// simple replication of the input pixel onto the corresponding output
		// pixels. The hi-falutin sampling literature refers to this as a
		// "box filter". A box filter tends to introduce visible artifacts,
		// so if you are actually going to use 3:1 or 4:1 sampling ratios
		// you would be well advised to improve this code.
		static void int_upsample(jpeg_decompress cinfo, jpeg_component_info compptr, byte[][] input_data, int input_data_offset, byte[][][] output_data_ptr, int output_data_offset)
		{
			my_upsampler upsample=(my_upsampler)cinfo.upsample;
			byte[][] output_data=output_data_ptr[output_data_offset];

			int h_expand=upsample.h_expand[compptr.component_index];
			int v_expand=upsample.v_expand[compptr.component_index];

			int outrow=0;
			uint outend=cinfo.output_width;
			while(outrow<cinfo.max_v_samp_factor)
			{
				// Generate one output row with proper horizontal expansion
				byte[] inptr=input_data[input_data_offset];
				uint inptr_ind=0;
				byte[] outptr=output_data[outrow];
				uint outptr_ind=0;
				while(outptr_ind<outend)
				{
					byte invalue=inptr[inptr_ind++];
					for(int h=h_expand; h>0; h--) outptr[outptr_ind++]=invalue;
				}

				// Generate any additional output rows by duplicating the first one
				if(v_expand>1) jcopy_sample_rows(output_data, outrow, output_data, outrow+1, v_expand-1, cinfo.output_width);
				input_data_offset++;
				outrow+=v_expand;
			}
		}

		// Fast processing for the common case of 2:1 horizontal and 1:1 vertical.
		// It's still a box filter.
		static void h2v1_upsample(jpeg_decompress cinfo, jpeg_component_info compptr, byte[][] input_data, int input_data_offset, byte[][][] output_data_ptr, int output_data_offset)
		{
			byte[][] output_data=output_data_ptr[output_data_offset];

			uint outend=cinfo.output_width;
			for(int outrow=0; outrow<cinfo.max_v_samp_factor; outrow++)
			{
				byte[] inptr=input_data[input_data_offset++];
				uint inptr_ind=0;
				byte[] outptr=output_data[outrow];
				uint outptr_ind=0;
				while(outptr_ind<outend)
				{
					byte invalue=inptr[inptr_ind++];
					outptr[outptr_ind++]=invalue;
					outptr[outptr_ind++]=invalue;
				}
			}
		}

		// Fast processing for the common case of 2:1 horizontal and 2:1 vertical.
		// It's still a box filter.
		static void h2v2_upsample(jpeg_decompress cinfo, jpeg_component_info compptr, byte[][] input_data, int input_data_offset, byte[][][] output_data_ptr, int output_data_offset)
		{
			byte[][] output_data=output_data_ptr[output_data_offset];

			int outrow=0;
			uint outend=cinfo.output_width;
			while(outrow<cinfo.max_v_samp_factor)
			{
				byte[] inptr=input_data[input_data_offset];
				uint inptr_ind=0;
				byte[] outptr=output_data[outrow];
				uint outptr_ind=0;
				while(outptr_ind<outend)
				{
					byte invalue=inptr[inptr_ind++];
					outptr[outptr_ind++]=invalue;
					outptr[outptr_ind++]=invalue;
				}

				Array.Copy(output_data[outrow], output_data[outrow+1], cinfo.output_width);
				input_data_offset++;
				outrow+=2;
			}
		}

		// Fancy processing for the common case of 2:1 horizontal and 1:1 vertical.
		//
		// The upsampling algorithm is linear interpolation between pixel centers,
		// also known as a "triangle filter". This is a good compromise between
		// speed and visual quality. The centers of the output pixels are 1/4 and 3/4
		// of the way between input pixel centers.
		//
		// A note about the "bias" calculations: when rounding fractional values to
		// integer, we do not want to always round 0.5 up to the next integer.
		// If we did that, we'd introduce a noticeable bias towards larger values.
		// Instead, this code is arranged so that 0.5 will be rounded up or down at
		// alternate pixel locations (a simple ordered dither pattern).
#if! USE_UNSAFE_STUFF
		static void h2v1_fancy_upsample(jpeg_decompress cinfo, jpeg_component_info compptr, byte[][] input_data, int input_data_offset, byte[][][] output_data_ptr, int output_data_offset)
		{
			byte[][] output_data=output_data_ptr[output_data_offset];

			for(int inrow=0; inrow<cinfo.max_v_samp_factor; inrow++)
			{
				byte[] inptr=input_data[input_data_offset++];
				uint inptr_ind=0;
				byte[] outptr=output_data[inrow];
				uint outptr_ind=0;

				// Special case for first column
				int invalue=inptr[inptr_ind++];
				outptr[outptr_ind++]=(byte)invalue;
				outptr[outptr_ind++]=(byte)((invalue*3+inptr[inptr_ind]+2)>>2);

				for(uint colctr=compptr.downsampled_width-2; colctr>0; colctr--)
				{
					// General case: 3/4 * nearer pixel + 1/4 * further pixel
					invalue=inptr[inptr_ind++]*3;
					outptr[outptr_ind++]=(byte)((invalue+inptr[inptr_ind-2]+1)>>2);
					outptr[outptr_ind++]=(byte)((invalue+inptr[inptr_ind]+2)>>2);
				}

				// Special case for last column
				invalue=inptr[inptr_ind];
				outptr[outptr_ind++]=(byte)((invalue*3+inptr[inptr_ind-1]+1)>>2);
				outptr[outptr_ind]=(byte)invalue;
			}
		}
#else
		static void h2v1_fancy_upsample(jpeg_decompress cinfo, jpeg_component_info compptr, byte[][] input_data, int input_data_offset, byte[][][] output_data_ptr, int output_data_offset)
		{
			unsafe
			{
				byte[][] output_data=output_data_ptr[output_data_offset];

				for(int inrow=0; inrow<cinfo.max_v_samp_factor; inrow++)
				{
					fixed(byte* inptr_=input_data[input_data_offset++], outptr_=output_data[inrow])
					{
						byte* inptr=inptr_;
						byte* outptr=outptr_;

						// Special case for first column
						int invalue=*(inptr++);
						*(outptr++)=(byte)invalue;
						*(outptr++)=(byte)((invalue*3+*inptr+2)>>2);

						for(uint colctr=compptr.downsampled_width-2; colctr>0; colctr--)
						{
							// General case: 3/4 * nearer pixel + 1/4 * further pixel
							invalue=*(inptr++)*3;
							*(outptr++)=(byte)((invalue+inptr[-2]+1)>>2);
							*(outptr++)=(byte)((invalue+*inptr+2)>>2);
						}

						// Special case for last column
						invalue=*inptr;
						*(outptr++)=(byte)((invalue*3+inptr[-1]+1)>>2);
						*outptr=(byte)invalue;
					}
				}
			}
		}
#endif // USE_UNSAFE_STUFF

#if UPSCALING_CONTEXT
		// Fast processing for the common case of 2:1 horizontal and 2:1 vertical.
		// It's still a box filter.
#if! USE_UNSAFE_STUFF
		static void h2v2_fancy_upsample(jpeg_decompress cinfo, jpeg_component_info compptr, byte[][] input_data, int input_data_offset, byte[][][] output_data_ptr, int output_data_offset)
		{
			if(!compptr.notFirst)
			{
				input_data[0].CopyTo(input_data[input_data.Length-1], 0);
				compptr.notFirst=true;
			}

			byte[][] output_data=output_data_ptr[output_data_offset];

			int inrow=0, outrow=0;
			while(outrow<cinfo.max_v_samp_factor)
			{
				for(int v=0; v<2; v++)
				{
					// inptr0 points to nearest input row, inptr1 points to next nearest
					byte[] inptr0=input_data[input_data_offset+inrow];
					byte[] inptr1=null;
					if(v==0)
					{
						inptr1=input_data[input_data_offset==0?input_data.Length-1:input_data_offset+inrow-1];	// next nearest is row above
					}
					else inptr1=input_data[(input_data_offset+inrow+1)%input_data.Length];		// next nearest is row below
					uint intptr_ind=0;

					byte[] outptr=output_data[outrow++];
					uint outptr_ind=0;

					// Special case for first column
					int thiscolsum=inptr0[intptr_ind]*3+inptr1[intptr_ind]; intptr_ind++;
					int nextcolsum=inptr0[intptr_ind]*3+inptr1[intptr_ind]; intptr_ind++;
					outptr[outptr_ind++]=(byte)((thiscolsum*4+8)>>4);
					outptr[outptr_ind++]=(byte)((thiscolsum*3+nextcolsum+7)>>4);
					int lastcolsum=thiscolsum;
					thiscolsum=nextcolsum;

					for(uint colctr=compptr.downsampled_width-2; colctr>0; colctr--)
					{
						// General case: 3/4 * nearer pixel + 1/4 * further pixel in each
						// dimension, thus 9/16, 3/16, 3/16, 1/16 overall
						nextcolsum=inptr0[intptr_ind]*3+inptr1[intptr_ind]; intptr_ind++;
						outptr[outptr_ind++]=(byte)((thiscolsum*3+lastcolsum+8)>>4);
						outptr[outptr_ind++]=(byte)((thiscolsum*3+nextcolsum+7)>>4);
						lastcolsum=thiscolsum; thiscolsum=nextcolsum;
					}

					// Special case for last column
					outptr[outptr_ind++]=(byte)((thiscolsum*3+lastcolsum+8)>>4);
					outptr[outptr_ind++]=(byte)((thiscolsum*4+7)>>4);
				}
				inrow++;
			}
		}
#else
		static void h2v2_fancy_upsample(jpeg_decompress cinfo, jpeg_component_info compptr, byte[][] input_data, int input_data_offset, byte[][][] output_data_ptr, int output_data_offset)
		{
			unsafe
			{
				if(!compptr.notFirst)
				{
					input_data[0].CopyTo(input_data[input_data.Length-1], 0);
					compptr.notFirst=true;
				}

				byte[][] output_data=output_data_ptr[output_data_offset];

				int inrow=0, outrow=0;
				while(outrow<cinfo.max_v_samp_factor)
				{
					// inptr0 points to nearest input row
					fixed(byte* inptr0_=input_data[input_data_offset+inrow])
					{
						// inptr1 points to next nearest
						int nextnearestrow=input_data_offset==0?input_data.Length-1:input_data_offset+inrow-1; // next nearest is row above

						for(int v=0; v<2; v++)
						{
							fixed(byte* inptr1_=input_data[nextnearestrow], outptr_=output_data[outrow++])
							{
								byte* inptr0=inptr0_, inptr1=inptr1_, outptr=outptr_;

								// Special case for first column
								int thiscolsum=*(inptr0++)*3+*(inptr1++);
								int nextcolsum=*(inptr0++)*3+*(inptr1++);
								*(outptr++)=(byte)((thiscolsum*4+8)>>4);
								*(outptr++)=(byte)((thiscolsum*3+nextcolsum+7)>>4);
								int lastcolsum=thiscolsum;
								thiscolsum=nextcolsum;

								for(uint colctr=compptr.downsampled_width-2; colctr>0; colctr--)
								{
									// General case: 3/4 * nearer pixel + 1/4 * further pixel in each
									// dimension, thus 9/16, 3/16, 3/16, 1/16 overall
									nextcolsum=*(inptr0++)*3+*(inptr1++);
									*(outptr++)=(byte)((thiscolsum*3+lastcolsum+8)>>4);
									*(outptr++)=(byte)((thiscolsum*3+nextcolsum+7)>>4);
									lastcolsum=thiscolsum; thiscolsum=nextcolsum;
								}

								// Special case for last column
								*(outptr++)=(byte)((thiscolsum*3+lastcolsum+8)>>4);
								*(outptr++)=(byte)((thiscolsum*4+7)>>4);
							}

							nextnearestrow=(input_data_offset+inrow+1)%input_data.Length; // next nearest is row below
						}
					}
					inrow++;
				}
			}
		}
#endif // USE_UNSAFE_STUFF
#endif

		// Module initialization routine for upsampling.
		public static void jinit_upsampler(jpeg_decompress cinfo)
		{
			my_upsampler upsample=null;
			bool need_buffer;
			int h_in_group, v_in_group, h_out_group, v_out_group;

			try
			{
				upsample=new my_upsampler();
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}
			cinfo.upsample=upsample;
			upsample.start_pass=start_pass_upsample;
			upsample.upsample=sep_upsample;
#if UPSCALING_CONTEXT
			upsample.need_context_rows=false; // until we find out differently
#endif

			if(cinfo.CCIR601_sampling) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_CCIR601_NOTIMPL); // this isn't supported

			// jdmainct.cs doesn't support context rows when min_DCT_scaled_size = 1,
			// so don't ask for it.
			bool do_fancy=cinfo.do_fancy_upsampling&&cinfo.min_DCT_scaled_size>1;

			// Verify we can handle the sampling factors, select per-component methods,
			// and create storage as needed.
			for(int ci=0; ci<cinfo.num_components; ci++)
			{
				jpeg_component_info compptr=cinfo.comp_info[ci];

				// Compute size of an "input group" after IDCT scaling. This many samples
				// are to be converted to max_h_samp_factor * max_v_samp_factor pixels.
				h_in_group=(int)(compptr.h_samp_factor*compptr.DCT_scaled_size)/cinfo.min_DCT_scaled_size;
				v_in_group=(int)(compptr.v_samp_factor*compptr.DCT_scaled_size)/cinfo.min_DCT_scaled_size;
				h_out_group=cinfo.max_h_samp_factor;
				v_out_group=cinfo.max_v_samp_factor;
				upsample.rowgroup_height[ci]=v_in_group; // save for use later
				need_buffer=true;
				if(!compptr.component_needed)
				{
					// Don't bother to upsample an uninteresting component.
					upsample.methods[ci]=noop_upsample;
					need_buffer=false;
				}
				else if(h_in_group==h_out_group&&v_in_group==v_out_group)
				{
					// Fullsize components can be processed without any work.
					upsample.methods[ci]=fullsize_upsample;
					need_buffer=false;
					upsample.color_buf[ci]=new byte[cinfo.max_v_samp_factor][];
				}
				else if(h_in_group*2==h_out_group&&v_in_group==v_out_group)
				{
					// Special cases for 2h1v upsampling
					if(do_fancy&&compptr.downsampled_width>2) upsample.methods[ci]=h2v1_fancy_upsample;
					else upsample.methods[ci]=h2v1_upsample;
				}
				else if(h_in_group*2==h_out_group&&v_in_group*2==v_out_group)
				{
					// Special cases for 2h2v upsampling
#if UPSCALING_CONTEXT
					if(do_fancy&&compptr.downsampled_width>2)
					{
						upsample.methods[ci]=h2v2_fancy_upsample;
						upsample.need_context_rows=true;
						compptr.doContext=true;
					}
					else 
#endif
						upsample.methods[ci]=h2v2_upsample;
				}
				else if((h_out_group%h_in_group)==0&&(v_out_group%v_in_group)==0)
				{
					// Generic integral-factors upsampling method
					upsample.methods[ci]=int_upsample;
					upsample.h_expand[ci]=(byte)(h_out_group/h_in_group);
					upsample.v_expand[ci]=(byte)(v_out_group/v_in_group);
				}
				else ERREXIT(cinfo, J_MESSAGE_CODE.JERR_FRACT_SAMPLE_NOTIMPL);

				if(need_buffer)
				{
					upsample.color_buf[ci]=alloc_sarray(cinfo, (uint)jround_up((int)cinfo.output_width, (int)cinfo.max_h_samp_factor), (uint)cinfo.max_v_samp_factor);
				}
			}
		}
	}
}
