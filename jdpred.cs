#if D_LOSSLESS_SUPPORTED
// jdpred.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains sample undifferencing (reconstruction) for lossless JPEG.
//
// In order to avoid paying the performance penalty of having to check the
// predictor being used and the row being processed for each call of the
// undifferencer, and to promote optimization, we have separate undifferencing
// functions for each case.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Undifferencers for the all rows but the first in a scan or restart interval.
		// The first sample in the row is undifferenced using the vertical
		// predictor (2). The rest of the samples are undifferenced using the
		// predictor specified in the scan header.
		static void jpeg_undifference1(jpeg_decompress cinfo, int comp_index, int[] diff_buf, int[] prev_row, int[] undiff_buf, uint width)
		{
			int Ra=(diff_buf[0]+prev_row[0])&0xFFFF;
			undiff_buf[0]=Ra;

			for(uint xindex=1; xindex<width; xindex++)
			{
				Ra=(diff_buf[xindex]+Ra)&0xFFFF;
				undiff_buf[xindex]=Ra;
			}
		}

		static void jpeg_undifference2(jpeg_decompress cinfo, int comp_index, int[] diff_buf, int[] prev_row, int[] undiff_buf, uint width)
		{
			int Rb=prev_row[0];
			int Ra=(diff_buf[0]+Rb)&0xFFFF;
			undiff_buf[0]=Ra;

			for(uint xindex=1; xindex<width; xindex++)
			{
				int Rc=Rb;
				Rb=prev_row[xindex];
				Ra=(diff_buf[xindex]+Rb)&0xFFFF;
				undiff_buf[xindex]=Ra;
			}
		}

		static void jpeg_undifference3(jpeg_decompress cinfo, int comp_index, int[] diff_buf, int[] prev_row, int[] undiff_buf, uint width)
		{
			int Rb=prev_row[0];
			int Ra=(diff_buf[0]+Rb)&0xFFFF;
			undiff_buf[0]=Ra;

			for(uint xindex=1; xindex<width; xindex++)
			{
				int Rc=Rb;
				Rb=prev_row[xindex];
				Ra=(diff_buf[xindex]+Rc)&0xFFFF;
				undiff_buf[xindex]=Ra;
			}
		}

		static void jpeg_undifference4(jpeg_decompress cinfo, int comp_index, int[] diff_buf, int[] prev_row, int[] undiff_buf, uint width)
		{
			int Rb=prev_row[0];
			int Ra=(diff_buf[0]+Rb)&0xFFFF;
			undiff_buf[0]=Ra;

			for(uint xindex=1; xindex<width; xindex++)
			{
				int Rc=Rb;
				Rb=prev_row[xindex];
				Ra=(diff_buf[xindex]+Ra+Rb-Rc)&0xFFFF;
				undiff_buf[xindex]=Ra;
			}
		}

		static void jpeg_undifference5(jpeg_decompress cinfo, int comp_index, int[] diff_buf, int[] prev_row, int[] undiff_buf, uint width)
		{
			int Rb=prev_row[0];
			int Ra=(diff_buf[0]+Rb)&0xFFFF;
			undiff_buf[0]=Ra;

			for(uint xindex=1; xindex<width; xindex++)
			{
				int Rc=Rb;
				Rb=prev_row[xindex];
				Ra=(diff_buf[xindex]+Ra+((Rb-Rc)>>1))&0xFFFF;
				undiff_buf[xindex]=Ra;
			}
		}

		static void jpeg_undifference6(jpeg_decompress cinfo, int comp_index, int[] diff_buf, int[] prev_row, int[] undiff_buf, uint width)
		{
			int Rb=prev_row[0];
			int Ra=(diff_buf[0]+Rb)&0xFFFF;
			undiff_buf[0]=Ra;

			for(uint xindex=1; xindex<width; xindex++)
			{
				int Rc=Rb;
				Rb=prev_row[xindex];
				Ra=(diff_buf[xindex]+Rb+((Ra-Rc)>>1))&0xFFFF;
				undiff_buf[xindex]=Ra;
			}
		}

		static void jpeg_undifference7(jpeg_decompress cinfo, int comp_index, int[] diff_buf, int[] prev_row, int[] undiff_buf, uint width)
		{
			int Rb=prev_row[0];
			int Ra=(diff_buf[0]+Rb)&0xFFFF;
			undiff_buf[0]=Ra;

			for(uint xindex=1; xindex<width; xindex++)
			{
				int Rc=Rb;
				Rb=prev_row[xindex];
				Ra=(diff_buf[xindex]+((Ra+Rb)>>1))&0xFFFF;
				undiff_buf[xindex]=Ra;
			}
		}

		// Undifferencer for the first row in a scan or restart interval. The first
		// sample in the row is undifferenced using the special predictor constant
		// x=2^(P-Pt-1). The rest of the samples are undifferenced using the
		// 1-D horizontal predictor (1).
		static void jpeg_undifference_first_row(jpeg_decompress cinfo, int comp_index, int[] diff_buf, int[] prev_row, int[] undiff_buf, uint width)
		{
			jpeg_lossless_d_codec losslsd=(jpeg_lossless_d_codec)cinfo.coef;

			int Ra=(diff_buf[0]+(1<<(cinfo.data_precision-cinfo.Al-1)))&0xFFFF;
			undiff_buf[0]=Ra;

			for(uint xindex=1; xindex<width; xindex++)
			{
				Ra=(diff_buf[xindex]+Ra)&0xFFFF;
				undiff_buf[xindex]=Ra;
			}

			// Now that we have undifferenced the first row, we want to use the
			// undifferencer which corresponds to the predictor specified in the
			// scan header.
			switch(cinfo.Ss)
			{
				case 1: losslsd.predict_undifference[comp_index]=jpeg_undifference1; break;
				case 2: losslsd.predict_undifference[comp_index]=jpeg_undifference2; break;
				case 3: losslsd.predict_undifference[comp_index]=jpeg_undifference3; break;
				case 4: losslsd.predict_undifference[comp_index]=jpeg_undifference4; break;
				case 5: losslsd.predict_undifference[comp_index]=jpeg_undifference5; break;
				case 6: losslsd.predict_undifference[comp_index]=jpeg_undifference6; break;
				case 7: losslsd.predict_undifference[comp_index]=jpeg_undifference7; break;
			}
		}

		// Initialize for an input processing pass.
		static void start_pass_d_pred(jpeg_decompress cinfo)
		{
			jpeg_lossless_d_codec losslsd=(jpeg_lossless_d_codec)cinfo.coef;

			// Check that the scan parameters Ss, Se, Ah, Al are OK for lossless JPEG.
			//
			// Ss is the predictor selection value (psv). Legal values for sequential
			// lossless JPEG are: 1 <= psv <= 7.
			//
			// Se and Ah are not used and should be zero.
			//
			// Al specifies the point transform (Pt). Legal values are: 0 <= Pt <= 15.
			if(cinfo.Ss<1||cinfo.Ss>7||cinfo.Se!=0||cinfo.Ah!=0||cinfo.Al>15) // need not check for < 0
				ERREXIT4(cinfo, J_MESSAGE_CODE.JERR_BAD_LOSSLESS, cinfo.Ss, cinfo.Se, cinfo.Ah, cinfo.Al);

			// Set undifference functions to first row function
			for(int ci=0; ci<cinfo.num_components; ci++) losslsd.predict_undifference[ci]=jpeg_undifference_first_row;
		}

		// Module initialization routine for the undifferencer.
		static void jinit_undifferencer(jpeg_decompress cinfo)
		{
			jpeg_lossless_d_codec losslsd=(jpeg_lossless_d_codec)cinfo.coef;

			losslsd.predict_start_pass=start_pass_d_pred;
			losslsd.predict_process_restart=start_pass_d_pred;
		}
	}
}
#endif // D_LOSSLESS_SUPPORTED
