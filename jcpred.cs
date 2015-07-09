#if C_LOSSLESS_SUPPORTED
// jcpred.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains sample differencing for lossless JPEG.
//
// In order to avoid paying the performance penalty of having to check the
// predictor being used and the row being processed for each call of the
// undifferencer, and to promote optimization, we have separate differencing
// functions for each case.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Private predictor object
		class c_predictor
		{
			// MCU-rows left in the restart interval for each component
			public uint[] restart_rows_to_go=new uint[MAX_COMPONENTS];
		}

		// Differencers for the all rows but the first in a scan or restart interval.
		// The first sample in the row is differenced using the vertical
		// predictor (2). The rest of the samples are differenced using the
		// predictor specified in the scan header.
		static void jpeg_difference1(jpeg_compress cinfo, int ci, byte[] input_buf, byte[] prev_row, int[] diff_buf, uint width)
		{
			jpeg_lossless_c_codec losslsc=(jpeg_lossless_c_codec)cinfo.coef;
			c_predictor pred=(c_predictor)losslsc.pred_private;

			int samp=input_buf[0];
			diff_buf[0]=samp-prev_row[0];

			for(uint xindex=1; xindex<width; xindex++)
			{
				int Ra=samp;
				samp=input_buf[xindex];
				diff_buf[xindex]=samp-Ra;
			}

			// Account for restart interval (no-op if not using restarts)
			if(cinfo.restart_interval!=0)
			{
				if(--(pred.restart_rows_to_go[ci])==0) reset_predictor(cinfo, ci);
			}
		}

		static void jpeg_difference2(jpeg_compress cinfo, int ci, byte[] input_buf, byte[] prev_row, int[] diff_buf, uint width)
		{
			jpeg_lossless_c_codec losslsc=(jpeg_lossless_c_codec)cinfo.coef;
			c_predictor pred=(c_predictor)losslsc.pred_private;

			int Rb=prev_row[0];
			int samp=input_buf[0];
			diff_buf[0]=samp-Rb;

			for(uint xindex=1; xindex<width; xindex++)
			{
				int Rc=Rb;
				Rb=prev_row[xindex];
				int Ra=samp;
				samp=input_buf[xindex];
				diff_buf[xindex]=samp-Rb;
			}

			// Account for restart interval (no-op if not using restarts)
			if(cinfo.restart_interval!=0)
			{
				if(--pred.restart_rows_to_go[ci]==0) reset_predictor(cinfo, ci);
			}
		}

		static void jpeg_difference3(jpeg_compress cinfo, int ci, byte[] input_buf, byte[] prev_row, int[] diff_buf, uint width)
		{
			jpeg_lossless_c_codec losslsc=(jpeg_lossless_c_codec)cinfo.coef;
			c_predictor pred=(c_predictor)losslsc.pred_private;

			int Rb=prev_row[0];
			int samp=input_buf[0];
			diff_buf[0]=samp-Rb;

			for(uint xindex=1; xindex<width; xindex++)
			{
				int Rc=Rb;
				Rb=prev_row[xindex];
				int Ra=samp;
				samp=input_buf[xindex];
				diff_buf[xindex]=samp-Rc;
			}

			// Account for restart interval (no-op if not using restarts)
			if(cinfo.restart_interval!=0)
			{
				if(--pred.restart_rows_to_go[ci]==0) reset_predictor(cinfo, ci);
			}
		}

		static void jpeg_difference4(jpeg_compress cinfo, int ci, byte[] input_buf, byte[] prev_row, int[] diff_buf, uint width)
		{
			jpeg_lossless_c_codec losslsc=(jpeg_lossless_c_codec)cinfo.coef;
			c_predictor pred=(c_predictor)losslsc.pred_private;

			int Rb=prev_row[0];
			int samp=input_buf[0];
			diff_buf[0]=samp-Rb;

			for(uint xindex=1; xindex<width; xindex++)
			{
				int Rc=Rb;
				Rb=prev_row[xindex];
				int Ra=samp;
				samp=input_buf[xindex];
				diff_buf[xindex]=samp-(Ra+Rb-Rc);
			}

			// Account for restart interval (no-op if not using restarts)
			if(cinfo.restart_interval!=0)
			{
				if(--pred.restart_rows_to_go[ci]==0) reset_predictor(cinfo, ci);
			}
		}

		static void jpeg_difference5(jpeg_compress cinfo, int ci, byte[] input_buf, byte[] prev_row, int[] diff_buf, uint width)
		{
			jpeg_lossless_c_codec losslsc=(jpeg_lossless_c_codec)cinfo.coef;
			c_predictor pred=(c_predictor)losslsc.pred_private;

			int Rb=prev_row[0];
			int samp=input_buf[0];
			diff_buf[0]=samp-Rb;

			for(uint xindex=1; xindex<width; xindex++)
			{
				int Rc=Rb;
				Rb=prev_row[xindex];
				int Ra=samp;
				samp=input_buf[xindex];
				diff_buf[xindex]=samp-(Ra+((Rb-Rc)>>1));
			}

			// Account for restart interval (no-op if not using restarts)
			if(cinfo.restart_interval!=0)
			{
				if(--pred.restart_rows_to_go[ci]==0) reset_predictor(cinfo, ci);
			}
		}

		static void jpeg_difference6(jpeg_compress cinfo, int ci, byte[] input_buf, byte[] prev_row, int[] diff_buf, uint width)
		{
			jpeg_lossless_c_codec losslsc=(jpeg_lossless_c_codec)cinfo.coef;
			c_predictor pred=(c_predictor)losslsc.pred_private;

			int Rb=prev_row[0];
			int samp=input_buf[0];
			diff_buf[0]=samp-Rb;

			for(uint xindex=1; xindex<width; xindex++)
			{
				int Rc=Rb;
				Rb=prev_row[xindex];
				int Ra=samp;
				samp=input_buf[xindex];
				diff_buf[xindex]=samp-(Rb+((Ra-Rc)>>1));
			}

			// Account for restart interval (no-op if not using restarts)
			if(cinfo.restart_interval!=0)
			{
				if(--pred.restart_rows_to_go[ci]==0) reset_predictor(cinfo, ci);
			}
		}

		static void jpeg_difference7(jpeg_compress cinfo, int ci, byte[] input_buf, byte[] prev_row, int[] diff_buf, uint width)
		{
			jpeg_lossless_c_codec losslsc=(jpeg_lossless_c_codec)cinfo.coef;
			c_predictor pred=(c_predictor)losslsc.pred_private;

			int Rb=prev_row[0];
			int samp=input_buf[0];
			diff_buf[0]=samp-Rb;

			for(uint xindex=1; xindex<width; xindex++)
			{
				int Rc=Rb;
				Rb=prev_row[xindex];
				int Ra=samp;
				samp=input_buf[xindex];
				diff_buf[xindex]=samp-((Ra+Rb)>>1);
			}

			// Account for restart interval (no-op if not using restarts)
			if(cinfo.restart_interval!=0)
			{
				if(--pred.restart_rows_to_go[ci]==0) reset_predictor(cinfo, ci);
			}
		}
		
		// Differencer for the first row in a scan or restart interval. The first
		// sample in the row is differenced using the special predictor constant
		// x=2^(P-Pt-1). The rest of the samples are differenced using the
		// 1-D horizontal predictor (1).
		static void jpeg_difference_first_row(jpeg_compress cinfo, int ci, byte[] input_buf, byte[] prev_row, int[] diff_buf, uint width)
		{
			jpeg_lossless_c_codec losslsc=(jpeg_lossless_c_codec)cinfo.coef;
			c_predictor pred=(c_predictor)losslsc.pred_private;
			bool restart=false;

			int samp=input_buf[0];
			diff_buf[0]=samp-(1<<(cinfo.data_precision-cinfo.Al-1));

			for(uint xindex=1; xindex<width; xindex++)
			{
				int Ra=samp;
				samp=input_buf[xindex];
				diff_buf[xindex]=samp-Ra;
			}

			// Account for restart interval (no-op if not using restarts)
			if(cinfo.restart_interval!=0)
			{
				if(--(pred.restart_rows_to_go[ci])==0)
				{
					reset_predictor(cinfo, ci);
					restart=true;
				}
			}
			
			// Now that we have differenced the first row, we want to use the
			// differencer which corresponds to the predictor specified in the
			// scan header.
			//
			// Note that we don't to do this if we have just reset the predictor
			// for a new restart interval.
			if(!restart)
			{
				switch(cinfo.Ss)
				{
					case 1: losslsc.predict_difference[ci]=jpeg_difference1; break;
					case 2: losslsc.predict_difference[ci]=jpeg_difference2; break;
					case 3: losslsc.predict_difference[ci]=jpeg_difference3; break;
					case 4: losslsc.predict_difference[ci]=jpeg_difference4; break;
					case 5: losslsc.predict_difference[ci]=jpeg_difference5; break;
					case 6: losslsc.predict_difference[ci]=jpeg_difference6; break;
					case 7: losslsc.predict_difference[ci]=jpeg_difference7; break;
				}
			}
		}

		// Reset predictor at the start of a pass or restart interval.
		static void reset_predictor(jpeg_compress cinfo, int ci)
		{
			jpeg_lossless_c_codec losslsc=(jpeg_lossless_c_codec)cinfo.coef;
			c_predictor pred=(c_predictor)losslsc.pred_private;

			// Initialize restart counter
			pred.restart_rows_to_go[ci]=cinfo.restart_interval/cinfo.MCUs_per_row;

			// Set difference function to first row function
			losslsc.predict_difference[ci]=jpeg_difference_first_row;
		}

		// Initialize for an input processing pass.
		static void start_pass_c_pred(jpeg_compress cinfo)
		{
			jpeg_lossless_c_codec losslsc=(jpeg_lossless_c_codec)cinfo.coef;
			c_predictor pred=(c_predictor)losslsc.pred_private;

			// Check that the restart interval is an integer multiple of the number 
			// of MCU in an MCU-row.
			if(cinfo.restart_interval%cinfo.MCUs_per_row!=0) ERREXIT2(cinfo, J_MESSAGE_CODE.JERR_BAD_RESTART, (int)cinfo.restart_interval, (int)cinfo.MCUs_per_row);

			// Set predictors for start of pass
			for(int ci=0; ci<cinfo.num_components; ci++) reset_predictor(cinfo, ci);
		}

		// Module initialization routine for the differencer.
		static void jinit_differencer(jpeg_compress cinfo)
		{
			jpeg_lossless_c_codec losslsc=(jpeg_lossless_c_codec)cinfo.coef;
			c_predictor pred=null;

			try
			{
				pred=new c_predictor();
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}
			losslsc.pred_private=pred;
			losslsc.predict_start_pass=start_pass_c_pred;
		}
	}
}
#endif // C_LOSSLESS_SUPPORTED

