// jcdctmgr.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1994-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains the forward-DCT management logic.
// This code selects a particular DCT implementation to be used,
// and it performs related housekeeping chores including coefficient
// quantization.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Private subobject for this module
		class fdct_controller
		{
			// Pointer to the DCT routine actually in use
			public forward_DCT_method_ptr do_dct;

			// The actual post-DCT divisors --- not identical to the quant table
			// entries, because of scaling (especially for an unnormalized DCT).
			// Each table is given in normal array order.
			public int[][] divisors=new int[NUM_QUANT_TBLS][];

#if DCT_FLOAT_SUPPORTED
			// Same as above for the floating-point case.
			public float_DCT_method_ptr do_float_dct;
			public double[][] float_divisors=new double[NUM_QUANT_TBLS][];
#endif
		}

		// For AA&N IDCT method, divisors are equal to quantization
		// coefficients scaled by scalefactor[row]*scalefactor[col], where
		//	scalefactor[0] = 1
		//	scalefactor[k] = cos(k*PI/16) * sqrt(2) for k=1..7
		// We apply a further scale factor of 8.
		static readonly short[] aanscales=new short[DCTSIZE2]
		{ // precomputed values scaled up by 14 bits
			16384, 22725, 21407, 19266, 16384, 12873,  8867,  4520,
			22725, 31521, 29692, 26722, 22725, 17855, 12299,  6270,
			21407, 29692, 27969, 25172, 21407, 16819, 11585,  5906,
			19266, 26722, 25172, 22654, 19266, 15137, 10426,  5315,
			16384, 22725, 21407, 19266, 16384, 12873,  8867,  4520,
			12873, 17855, 16819, 15137, 12873, 10114,  6967,  3552,
			 8867, 12299, 11585, 10426,  8867,  6967,  4799,  2446,
			 4520,  6270,  5906,  5315,  4520,  3552,  2446,  1247
		};

#if DCT_FLOAT_SUPPORTED
		// For float AA&N IDCT method, divisors are equal to quantization
		// coefficients scaled by scalefactor[row]*scalefactor[col], where
		//	scalefactor[0] = 1
		//	scalefactor[k] = cos(k*PI/16) * sqrt(2) for k=1..7
		// We apply a further scale factor of 8.
		// What's actually stored is 1/divisor so that the inner loop can
		// use a multiplication rather than a division.
		static readonly double[] aanscalefactor=new double[DCTSIZE]
		{
			1.0,
			1.3870398453221474618216191915664,
			1.3065629648763765278566431734272,
			1.1758756024193587169744671046113,
			1.0,
			0.78569495838710218127789736765722,
			0.54119610014619698439972320536639,
			0.27589937928294301233595756366937
		};
#endif

		// Initialize for a processing pass.
		// Verify that all referenced Q-tables are present, and set up
		// the divisor table for each one.
		// In the current implementation, DCT of all components is done during
		// the first pass, even if only some components will be output in the
		// first scan. Hence all components should be examined here.
		static void start_pass_fdctmgr(jpeg_compress cinfo)
		{
			jpeg_lossy_c_codec lossyc=(jpeg_lossy_c_codec)cinfo.coef;
			fdct_controller fdct=(fdct_controller)lossyc.fdct_private;

			for(int ci=0; ci<cinfo.num_components; ci++)
			{
				jpeg_component_info compptr=cinfo.comp_info[ci];
				int qtblno=compptr.quant_tbl_no;

				// Make sure specified quantization table is present
				if(qtblno<0||qtblno>=NUM_QUANT_TBLS||cinfo.quant_tbl_ptrs[qtblno]==null)
					ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_NO_QUANT_TABLE, qtblno);

				JQUANT_TBL qtbl=cinfo.quant_tbl_ptrs[qtblno];

				// Compute divisors for this quant table
				// We may do this more than once for same table, but it's not a big deal
				if(fdct.divisors[qtblno]==null)
				{
					try
					{
						fdct.divisors[qtblno]=new int[DCTSIZE2];
					}
					catch
					{
						ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
					}
				}

				int[] dtbl=fdct.divisors[qtblno];
				for(int i=0; i<DCTSIZE2; i++)
				{
					dtbl[i]=((int)qtbl.quantval[i]*aanscales[i]+1024)>>11;
				}

#if DCT_FLOAT_SUPPORTED
				if(fdct.float_divisors[qtblno]==null)
				{
					try
					{
						fdct.float_divisors[qtblno]=new double[DCTSIZE2];
					}
					catch
					{
						ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
					}
				}

				double[] fdtbl=fdct.float_divisors[qtblno];
				int di=0;
				for(int row=0; row<DCTSIZE; row++)
				{
					for(int col=0; col<DCTSIZE; col++)
					{
						fdtbl[di]=1.0/(qtbl.quantval[di]*aanscalefactor[row]*aanscalefactor[col]*8.0);
						di++;
					}
				}
#endif
			}
		}

		// Perform forward DCT on one or more blocks of a component.
		//
		// The input samples are taken from the sample_data[] array starting at
		// position start_row/start_col, and moving to the right for any additional
		// blocks. The quantized coefficients are returned in coef_blocks[].

		// This version is used for integer DCT implementations.
		static void forward_DCT(jpeg_compress cinfo, jpeg_component_info compptr, byte[][] sample_data, short[][] coef_blocks, int coef_offset, uint start_row, uint start_col, uint num_blocks)
		{
			// This routine is heavily used, so it's worth coding it tightly
			jpeg_lossy_c_codec lossyc=(jpeg_lossy_c_codec)cinfo.coef;
			fdct_controller fdct=(fdct_controller)lossyc.fdct_private;

			forward_DCT_method_ptr do_dct=fdct.do_dct;
			int[] divisors=fdct.divisors[compptr.quant_tbl_no];
			int[] workspace=new int[DCTSIZE2];	// work area for FDCT subroutine

			for(int bi=0; bi<num_blocks; bi++, start_col+=DCTSIZE)
			{
				// Load data into workspace, applying unsigned->signed conversion
				int wsptr=0;
				for(int elemr=0; elemr<DCTSIZE; elemr++)
				{
					byte[] elem=sample_data[start_row+elemr];
					uint eptr=start_col;

					// unroll the inner loop
					workspace[wsptr++]=elem[eptr++]-CENTERJSAMPLE;
					workspace[wsptr++]=elem[eptr++]-CENTERJSAMPLE;
					workspace[wsptr++]=elem[eptr++]-CENTERJSAMPLE;
					workspace[wsptr++]=elem[eptr++]-CENTERJSAMPLE;
					workspace[wsptr++]=elem[eptr++]-CENTERJSAMPLE;
					workspace[wsptr++]=elem[eptr++]-CENTERJSAMPLE;
					workspace[wsptr++]=elem[eptr++]-CENTERJSAMPLE;
					workspace[wsptr++]=elem[eptr++]-CENTERJSAMPLE;
				}

				// Perform the DCT
				do_dct(workspace);

				// Quantize/descale the coefficients, and store into coef_blocks[]
				//old short[] output_ptr=coef_blocks[coef_offset][bi];
				short[] output_ptr=coef_blocks[coef_offset+bi];

				for(int i=0; i<DCTSIZE2; i++)
				{
					int qval=divisors[i];
					int temp=workspace[i];
					// Divide the coefficient value by qval, ensuring proper rounding.
					// Since C does not specify the direction of rounding for negative
					// quotients, we have to force the dividend positive for portability.
					if(temp<0)
					{
						temp=-temp;
						temp+=qval>>1;	// for rounding
						temp/=qval;
						temp=-temp;
					}
					else
					{
						temp+=qval>>1;	// for rounding
						temp/=qval;
					}
					output_ptr[i]=(short)temp;
				}
			}
		}

#if DCT_FLOAT_SUPPORTED
		// This version is used for floating-point DCT implementations.
		static void forward_DCT_float(jpeg_compress cinfo, jpeg_component_info compptr, byte[][] sample_data, short[][] coef_blocks, int coef_offset, uint start_row, uint start_col, uint num_blocks)
		{
			// This routine is heavily used, so it's worth coding it tightly
			jpeg_lossy_c_codec lossyc=(jpeg_lossy_c_codec)cinfo.coef;
			fdct_controller fdct=(fdct_controller)lossyc.fdct_private;

			float_DCT_method_ptr do_dct=fdct.do_float_dct;
			double[] divisors=fdct.float_divisors[compptr.quant_tbl_no];
			double[] workspace=new double[DCTSIZE2];	// work area for FDCT subroutine

			for(int bi=0; bi<num_blocks; bi++, start_col+=DCTSIZE)
			{
				// Load data into workspace, applying unsigned->signed conversion
				int wsptr=0;
				for(int elemr=0; elemr<DCTSIZE; elemr++)
				{
					byte[] elem=sample_data[start_row+elemr];
					uint eptr=start_col;

					// unroll the inner loop
					workspace[wsptr++]=elem[eptr++]-CENTERJSAMPLE;
					workspace[wsptr++]=elem[eptr++]-CENTERJSAMPLE;
					workspace[wsptr++]=elem[eptr++]-CENTERJSAMPLE;
					workspace[wsptr++]=elem[eptr++]-CENTERJSAMPLE;
					workspace[wsptr++]=elem[eptr++]-CENTERJSAMPLE;
					workspace[wsptr++]=elem[eptr++]-CENTERJSAMPLE;
					workspace[wsptr++]=elem[eptr++]-CENTERJSAMPLE;
					workspace[wsptr++]=elem[eptr++]-CENTERJSAMPLE;
				}

				// Perform the DCT
				do_dct(workspace);

				// Quantize/descale the coefficients, and store into coef_blocks[]
				//old short[] output_ptr=coef_blocks[coef_offset][bi];
				short[] output_ptr=coef_blocks[coef_offset+bi];

				for(int i=0; i<DCTSIZE2; i++)
				{
					// Apply the quantization and scaling factor
					double temp=workspace[i]*divisors[i];

					// Round to nearest integer.
					// Since C does not specify the direction of rounding for negative
					// quotients, we have to force the dividend positive for portability.
					// The maximum coefficient size is +-16K (for 12-bit data), so this
					// code should work for either 16-bit or 32-bit ints.
					output_ptr[i]=(short)((int)(temp+16384.5)-16384);
				}
			}
		}
#endif // DCT_FLOAT_SUPPORTED

		// Initialize FDCT manager.
		static void jinit_forward_dct(jpeg_compress cinfo)
		{
			jpeg_lossy_c_codec lossyc=(jpeg_lossy_c_codec)cinfo.coef;
			fdct_controller fdct=null;

			try
			{
				fdct=new fdct_controller();
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}
			lossyc.fdct_private=fdct;
			lossyc.fdct_start_pass=start_pass_fdctmgr;

			fdct.do_dct=jpeg_fdct_ifast;
			lossyc.fdct_forward_DCT=forward_DCT;

#if DCT_FLOAT_SUPPORTED
			fdct.do_float_dct=jpeg_fdct_float;
			if(cinfo.useFloatDCT) lossyc.fdct_forward_DCT=forward_DCT_float;
#endif
			// Mark divisor tables unallocated
			for(int i=0; i<NUM_QUANT_TBLS; i++)
			{
				fdct.divisors[i]=null;
#if DCT_FLOAT_SUPPORTED
				fdct.float_divisors[i]=null;
#endif
			}
		}
	}
}
