// jddctmgr.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1994-1998, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains the inverse-DCT management logic.
// This code selects a particular IDCT implementation to be used,
// and it performs related housekeeping chores. No code in this file
// is executed per IDCT step, only during output pass setup.
//
// Note that the IDCT routines are responsible for performing coefficient
// dequantization as well as the IDCT proper. This module sets up the
// dequantization multiplier table needed by the IDCT routine.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// The decompressor input side (jdinput.cs) saves away the appropriate
		// quantization table for each component at the start of the first scan
		// involving that component. (This is necessary in order to correctly
		// decode files that reuse Q-table slots.)
		// When we are ready to make an output pass, the saved Q-table is converted
		// to a multiplier table that will actually be used by the IDCT routine.
		// The multiplier table contents are IDCT-method-dependent. To support
		// application changes in IDCT method between scans, we can remake the
		// multiplier tables if necessary.
		// In buffered-image mode, the first output pass may occur before any data
		// has been seen for some components, and thus before their Q-tables have
		// been saved away. To handle this case, multiplier tables are preset
		// to zeroes; the result of the IDCT will be a neutral gray level.

		// Private subobject for this module
		class idct_controller
		{
			// This array contains the IDCT method code that each multiplier table
			// is currently set up for, or -1 if it's not yet set up.
			// The actual multiplier tables are pointed to by dct_table in the
			// per-component comp_info structures.
			public int[] cur_method=new int[MAX_COMPONENTS];
		}

		// Prepare for an output pass.
		// Here we select the proper IDCT routine for each component and build
		// a matching multiplier table.
		static void start_pass_idctmgr(jpeg_decompress cinfo)
		{
			jpeg_lossy_d_codec lossyd=(jpeg_lossy_d_codec)cinfo.coef;
			idct_controller idct=(idct_controller)lossyd.idct_private;

			for(int ci=0; ci<cinfo.num_components; ci++)
			{
				jpeg_component_info compptr=cinfo.comp_info[ci];

				// Select the proper IDCT routine for this component's scaling
				lossyd.inverse_DCT[ci]=jpeg_idct_ifast;

				// Create multiplier table from quant table.
				// However, we can skip this if the component is uninteresting
				// or if we already built the table. Also, if no quant table
				// has yet been saved for the component, we leave the
				// multiplier table all-zero; we'll be reading zeroes from the
				// coefficient controller's buffer anyway.
				if(!compptr.component_needed||idct.cur_method[ci]==JDCT_IFAST) continue;

				JQUANT_TBL qtbl=compptr.quant_table;
				if(qtbl==null) continue;	// happens if no data yet for component

				idct.cur_method[ci]=JDCT_IFAST;
				// For AA&N IDCT method, multipliers are equal to quantization
				// coefficients scaled by scalefactor[row]*scalefactor[col], where
				//	scalefactor[0] = 1
				//	scalefactor[k] = cos(k*PI/16) * sqrt(2)		for k=1..7
				// For integer operation, the multiplier table is to be scaled by 2.
				int[] ifmtbl=compptr.dct_table;

				for(int i=0; i<DCTSIZE2; i++) ifmtbl[i]=(((int)qtbl.quantval[i]*aanscales[i])+(1<<11))>>12;
			}
		}

		// Initialize IDCT manager.
		static void jinit_inverse_dct(jpeg_decompress cinfo)
		{
			jpeg_lossy_d_codec lossyd=(jpeg_lossy_d_codec)cinfo.coef;
			idct_controller idct=null;

			try
			{
				idct=new idct_controller();
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}

			lossyd.idct_private=idct;
			lossyd.idct_start_pass=start_pass_idctmgr;

			for(int ci=0; ci<cinfo.num_components; ci++)
			{
				jpeg_component_info compptr=cinfo.comp_info[ci];

				// Allocate and pre-zero a multiplier table for each component
				try
				{
					compptr.dct_table=new int[DCTSIZE2];
				}
				catch
				{
					ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
				}

				// Mark multiplier table not yet set up for any method
				idct.cur_method[ci]=-1;
			}
		}
	}
}
