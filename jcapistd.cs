// jcapistd.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1994-1996, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains application interface code for the compression half
// of the JPEG library. These are the "standard" API routines that are
// used in the normal full-compression case. They are not used by a
// transcoding-only application.

using System;

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// Compression initialization.
		// Before calling this, all parameters and a data destination must be set up.
		//
		// We require a write_all_tables parameter as a failsafe check when writing
		// multiple datastreams from the same compression object. Since prior runs
		// will have left all the tables marked sent_table=true, a subsequent run
		// would emit an abbreviated stream (no tables) by default. This may be what
		// is wanted, but for safety's sake it should not be the default behavior:
		// programmers should have to make a deliberate choice to emit abbreviated
		// images. Therefore the documentation and examples should encourage people
		// to pass write_all_tables=true; then it will take active thought to do the
		// wrong thing.
		public static void jpeg_start_compress(jpeg_compress cinfo, bool write_all_tables)
		{
			if(cinfo.global_state!=STATE.CSTART) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_STATE, cinfo.global_state);

			if(write_all_tables) jpeg_suppress_tables(cinfo, false); // mark all tables to be written

			// (Re)initialize error mgr and destination modules 
			cinfo.err.reset_error_mgr(cinfo);
			cinfo.dest.init_destination(cinfo);

			// Perform master selection of active modules
			jinit_compress_master(cinfo);

			// Set up for the first pass
			cinfo.master.prepare_for_pass(cinfo);

			// Ready for application to drive first pass through jpeg_write_scanlines
			// or jpeg_write_raw_data.
			cinfo.next_scanline=0;
			cinfo.global_state=(cinfo.raw_data_in?STATE.CRAW_OK:STATE.CSCANNING);
		}

		// Write some scanlines of data to the JPEG compressor.
		//
		// The return value will be the number of lines actually written.
		// This should be less than the supplied num_lines only in case that
		// the data destination module has requested suspension of the compressor,
		// or if more than image_height scanlines are passed in.
		//
		// Note: we warn about excess calls to jpeg_write_scanlines() since
		// this likely signals an application programmer error. However,
		// excess scanlines passed in the last valid call are *silently* ignored,
		// so that the application need not adjust num_lines for end-of-image
		// when using a multiple-scanline buffer.
		public static uint jpeg_write_scanlines(jpeg_compress cinfo, byte[][] scanlines, uint num_lines)
		{
			if(cinfo.global_state!=STATE.CSCANNING) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_STATE, cinfo.global_state);
			if(cinfo.next_scanline>=cinfo.image_height) WARNMS(cinfo, J_MESSAGE_CODE.JWRN_TOO_MUCH_DATA);

			// Call progress monitor hook if present
			if(cinfo.progress!=null)
			{
				cinfo.progress.pass_counter=(int)cinfo.next_scanline;
				cinfo.progress.pass_limit=(int)cinfo.image_height;
				cinfo.progress.progress_monitor(cinfo);
			}

			// Give master control module another chance if this is first call to
			// jpeg_write_scanlines. This lets output of the frame/scan headers be
			// delayed so that application can write COM, etc, markers between
			// jpeg_start_compress and jpeg_write_scanlines.
			if(cinfo.master.call_pass_startup) cinfo.master.pass_startup(cinfo);

			// Ignore any extra scanlines at bottom of image.
			uint rows_left=cinfo.image_height-cinfo.next_scanline;
			if(num_lines>rows_left) num_lines=rows_left;

			uint row_ctr=0;
			cinfo.main.process_data(cinfo, scanlines, ref row_ctr, num_lines);
			cinfo.next_scanline+=row_ctr;
			return row_ctr;
		}

		// Alternate entry point to write raw data.
		// Processes exactly one iMCU row per call, unless suspended.
		public static uint jpeg_write_raw_data(jpeg_compress cinfo, byte[][][] data, uint num_lines)
		{
			if(cinfo.global_state!=STATE.CRAW_OK) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_STATE, cinfo.global_state);
			if(cinfo.next_scanline>=cinfo.image_height)
			{
				WARNMS(cinfo, J_MESSAGE_CODE.JWRN_TOO_MUCH_DATA);
				return 0;
			}

			// Call progress monitor hook if present
			if(cinfo.progress!=null)
			{
				cinfo.progress.pass_counter=(int)cinfo.next_scanline;
				cinfo.progress.pass_limit=(int)cinfo.image_height;
				cinfo.progress.progress_monitor(cinfo);
			}

			// Give master control module another chance if this is first call to
			// jpeg_write_raw_data. This lets output of the frame/scan headers be
			// delayed so that application can write COM, etc, markers between
			// jpeg_start_compress and jpeg_write_raw_data.
			if(cinfo.master.call_pass_startup) cinfo.master.pass_startup(cinfo);

			// Verify that at least one iMCU row has been passed.
			uint lines_per_iMCU_row=(uint)cinfo.max_v_samp_factor*cinfo.DCT_size;
			if(num_lines<lines_per_iMCU_row) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_BUFFER_SIZE);

			// Directly compress the row.
			if(!cinfo.coef.compress_data(cinfo, data)) return 0; // If compressor did not consume the whole row, suspend processing.

			// OK, we processed one iMCU row.
			cinfo.next_scanline+=lines_per_iMCU_row;
			return lines_per_iMCU_row;
		}

#if DCT_FLOAT_SUPPORTED
		public static uint jpeg_write_image(jpeg_compress cinfo, byte[] image, bool swapChannels, bool alpha)
		{
			if(cinfo.input_components!=3||cinfo.lossless||cinfo.in_color_space!=J_COLOR_SPACE.JCS_RGB||
				cinfo.num_components!=3||cinfo.jpeg_color_space!=J_COLOR_SPACE.JCS_YCbCr||
				cinfo.data_precision!=8||cinfo.DCT_size!=8||cinfo.block_in_MCU!=3||cinfo.arith_code||
				cinfo.max_h_samp_factor!=1||cinfo.max_v_samp_factor!=1||cinfo.next_scanline!=0||cinfo.num_scans!=1)
			{
				throw new Exception();
			}

			if(cinfo.global_state!=STATE.CSCANNING) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_BAD_STATE, cinfo.global_state);

			// Give master control module another chance if this is first call to
			// jpeg_write_scanlines. This lets output of the frame/scan headers be
			// delayed so that application can write COM, etc, markers between
			// jpeg_start_compress and jpeg_write_scanlines.
			if(cinfo.master.call_pass_startup) cinfo.master.pass_startup(cinfo);

			jpeg_lossy_c_codec lossyc=(jpeg_lossy_c_codec)cinfo.coef;
			c_coef_controller coef=(c_coef_controller)lossyc.coef_private;
			fdct_controller fdct=(fdct_controller)lossyc.fdct_private;

			double[] workspaceY=new double[DCTSIZE2];
			double[] workspaceCr=new double[DCTSIZE2];
			double[] workspaceCb=new double[DCTSIZE2];

			short[][] coefs=new short[][] { new short[DCTSIZE2], new short[DCTSIZE2], new short[DCTSIZE2] };

			short[] coefY=coefs[0];
			short[] coefCb=coefs[1];
			short[] coefCr=coefs[2];

			double[] divisorY=fdct.float_divisors[0];
			double[] divisorC=fdct.float_divisors[1];

			int bpp=alpha?4:3;

			for(int y=0; y<(cinfo.image_height+DCTSIZE-1)/DCTSIZE; y++)
			{
				int yFormImage=Math.Min((int)cinfo.image_height-y*DCTSIZE, DCTSIZE);
				for(int x=0; x<(cinfo.image_width+DCTSIZE-1)/DCTSIZE; x++)
				{
					int xFormImage=Math.Min((int)cinfo.image_width-x*DCTSIZE, DCTSIZE);
					int workspacepos=0;
					for(int j=0; j<yFormImage; j++)
					{
						int imagepos=((y*DCTSIZE+j)*(int)cinfo.image_width+x*DCTSIZE)*bpp;
						for(int i=0; i<xFormImage; i++, workspacepos++)
						{
							byte r=image[imagepos++];
							byte g=image[imagepos++];
							byte b=image[imagepos++];
							if(alpha) imagepos++;

							if(!swapChannels)
							{
								workspaceY[workspacepos]=0.299*r+0.587*g+0.114*b-CENTERJSAMPLE;
								workspaceCb[workspacepos]=-0.168736*r-0.331264*g+0.5*b;
								workspaceCr[workspacepos]=0.5*r-0.418688*g-0.081312*b;
							}
							else
							{
								workspaceY[workspacepos]=0.299*b+0.587*g+0.114*r-CENTERJSAMPLE;
								workspaceCb[workspacepos]=-0.168736*b-0.331264*g+0.5*r;
								workspaceCr[workspacepos]=0.5*b-0.418688*g-0.081312*r;
							}
						}

						int lastworkspacepos=workspacepos-1;
						for(int i=xFormImage; i<DCTSIZE; i++, workspacepos++)
						{
							workspaceY[workspacepos]=workspaceY[lastworkspacepos];
							workspaceCb[workspacepos]=workspaceCb[lastworkspacepos];
							workspaceCr[workspacepos]=workspaceCr[lastworkspacepos];
						}
					}

					int lastworkspacelinepos=(yFormImage-1)*DCTSIZE;
					for(int j=yFormImage; j<DCTSIZE; j++)
					{
						int lastworkspacepos=lastworkspacelinepos;
						for(int i=0; i<DCTSIZE; i++, workspacepos++, lastworkspacepos++)
						{
							workspaceY[workspacepos]=workspaceY[lastworkspacepos];
							workspaceCb[workspacepos]=workspaceCb[lastworkspacepos];
							workspaceCr[workspacepos]=workspaceCr[lastworkspacepos];
						}
					}

					// ein block (3 componenten)
					jpeg_fdct_float(workspaceY);
					jpeg_fdct_float(workspaceCb);
					jpeg_fdct_float(workspaceCr);

					for(int i=0; i<DCTSIZE2; i++)
					{
						// Apply the quantization and scaling factor
						double tempY=workspaceY[i]*divisorY[i];
						double tempCb=workspaceCb[i]*divisorC[i];
						double tempCr=workspaceCr[i]*divisorC[i];

						// Round to nearest integer.
						// Since C does not specify the direction of rounding for negative
						// quotients, we have to force the dividend positive for portability.
						// The maximum coefficient size is +-16K (for 12-bit data), so this
						// code should work for either 16-bit or 32-bit ints.
						coefY[i]=(short)((int)(tempY+16384.5)-16384);
						coefCb[i]=(short)((int)(tempCb+16384.5)-16384);
						coefCr[i]=(short)((int)(tempCr+16384.5)-16384);
					}

					lossyc.entropy_encode_mcu(cinfo, coefs);
				}
			}

			cinfo.next_scanline=cinfo.image_height;
			return cinfo.image_height;
		}
#endif
	}
}