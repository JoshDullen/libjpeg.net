#if QUANT_2PASS_SUPPORTED
// jquant2.cs
//
// Based on libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// Copyright (C) 1991-1996, Thomas G. Lane.
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file contains 2-pass color quantization (color mapping) routines.
// These routines provide selection of a custom color map for an image,
// followed by mapping of the image to that color map, with optional
// Floyd-Steinberg dithering.
// It is also possible to use just the second pass to map to an arbitrary
// externally-given color map.
//
// Note: ordered dithering is not supported, since there isn't any fast
// way to compute intercolor distances; it's unclear that ordered dither's
// fundamental assumptions even hold with an irregularly spaced color map.

namespace Free.Ports.LibJpeg
{
	public static partial class libjpeg
	{
		// This module implements the well-known Heckbert paradigm for color
		// quantization. Most of the ideas used here can be traced back to
		// Heckbert's seminal paper
		//	Heckbert, Paul. "Color Image Quantization for Frame Buffer Display",
		//	Proc. SIGGRAPH '82, Computer Graphics v.16 #3 (July 1982), pp 297-304.
		//
		// In the first pass over the image, we accumulate a histogram showing the
		// usage count of each possible color. To keep the histogram to a reasonable
		// size, we reduce the precision of the input; typical practice is to retain
		// 5 or 6 bits per color, so that 8 or 4 different input values are counted
		// in the same histogram cell.
		//
		// Next, the color-selection step begins with a box representing the whole
		// color space, and repeatedly splits the "largest" remaining box until we
		// have as many boxes as desired colors. Then the mean color in each
		// remaining box becomes one of the possible output colors.
		//
		// The second pass over the image maps each input pixel to the closest output
		// color (optionally after applying a Floyd-Steinberg dithering correction).
		// This mapping is logically trivial, but making it go fast enough requires
		// considerable care.
		//
		// Heckbert-style quantizers vary a good deal in their policies for choosing
		// the "largest" box and deciding where to cut it. The particular policies
		// used here have proved out well in experimental comparisons, but better ones
		// may yet be found.
		//
		// In earlier versions of the IJG code, this module quantized in YCbCr color
		// space, processing the raw upsampled data without a color conversion step.
		// This allowed the color conversion math to be done only once per colormap
		// entry, not once per pixel. However, that optimization precluded other
		// useful optimizations (such as merging color conversion with upsampling)
		// and it also interfered with desired capabilities such as quantizing to an
		// externally-supplied colormap. We have therefore abandoned that approach.
		// The present code works in the post-conversion color space, typically RGB.
		//
		// To improve the visual quality of the results, we actually work in scaled
		// RGB space, giving G distances more weight than R, and R in turn more than
		// B. To do everything in integer math, we must use integer scale factors.
		// The 2/3/1 scale factors used here correspond loosely to the relative
		// weights of the colors in the NTSC grayscale equation.
		// If you want to use this code to quantize a non-RGB color space, you'll
		// probably need to change these scale factors.

		const int R_SCALE=2;	// scale R distances by this much
		const int G_SCALE=3;	// scale G distances by this much
		const int B_SCALE=1;	// and B by this much

		// Relabel R/G/B as components 0/1/2, respecting the RGB ordering defined
		// in jmorecfg.cs. As the code stands, it will do the right thing for R,G,B
		// and B,G,R orders. If you define some other weird order in jmorecfg.cs,
		// you'll get compile errors until you extend this logic. In that case
		// you'll probably want to tweak the histogram sizes too.
#if !BGR
		const int C0_SCALE=R_SCALE;
		const int C1_SCALE=G_SCALE;
		const int C2_SCALE=B_SCALE;
#else
		const int C0_SCALE=B_SCALE;
		const int C1_SCALE=G_SCALE;
		const int C2_SCALE=R_SCALE;
#endif

		// First we have the histogram data structure and routines for creating it.
		//
		// The number of bits of precision can be adjusted by changing these symbols.
		// We recommend keeping 6 bits for G and 5 each for R and B.
		// If you have plenty of memory and cycles, 6 bits all around gives marginally
		// better results; if you are short of memory, 5 bits all around will save
		// some space but degrade the results.
		// To maintain a fully accurate histogram, we'd need to allocate a "int"
		// (preferably uint) for each cell. In practice this is overkill;
		// we can get by with 16 bits per cell. Few of the cell counts will overflow,
		// and clamping those that do overflow to the maximum value will give close-
		// enough results. This reduces the recommended histogram size from 256Kb
		// to 128Kb, which is a useful savings on PC-class machines.
		// (In the second pass the histogram space is re-used for pixel mapping data;
		// in that capacity, each cell must be able to store zero to the number of
		// desired colors. 16 bits/cell is plenty for that too.)
		// Since the JPEG code is intended to run in small memory model on 80x86
		// machines, we can't just allocate the histogram in one chunk. Instead
		// of a true 3-D array, we use a row of pointers to 2-D arrays. Each
		// pointer corresponds to a C0 value (typically 2^5 = 32 pointers) and
		// each 2-D array has 2^6*2^5 = 2048 or 2^6*2^6 = 4096 entries. Note that
		// on 80x86 machines, the pointer row is in near memory but the actual
		// arrays are in far memory (same arrangement as we use for image arrays).

		const int MAXNUMCOLORS=(MAXJSAMPLE+1); // maximum size of colormap

		// These will do the right thing for either R,G,B or B,G,R color order,
		// but you may not like the results for other color orders.
		const int HIST_C0_BITS=5;	// bits of precision in R/B histogram
		const int HIST_C1_BITS=6;	// bits of precision in G histogram
		const int HIST_C2_BITS=5;	// bits of precision in B/R histogram

		// Number of elements along histogram axes.
		const int HIST_C0_ELEMS=(1<<HIST_C0_BITS); // 32
		const int HIST_C1_ELEMS=(1<<HIST_C1_BITS); // 64
		const int HIST_C2_ELEMS=(1<<HIST_C2_BITS); // 32

		// These are the amounts to shift an input value to get a histogram index.
		const int C0_SHIFT=(BITS_IN_JSAMPLE-HIST_C0_BITS);
		const int C1_SHIFT=(BITS_IN_JSAMPLE-HIST_C1_BITS);
		const int C2_SHIFT=(BITS_IN_JSAMPLE-HIST_C2_BITS);

		// Declarations for Floyd-Steinberg dithering.
		//
		// Errors are accumulated into the array fserrors[], at a resolution of
		// 1/16th of a pixel count. The error at a given pixel is propagated
		// to its not-yet-processed neighbors using the standard F-S fractions,
		//		...		(here)	7/16
		//		3/16	5/16	1/16
		// We work left-to-right on even rows, right-to-left on odd rows.
		//
		// We can get away with a single array (holding one row's worth of errors)
		// by using it to store the current row's errors at pixel columns not yet
		// processed, but the next row's errors at columns already processed. We
		// need only a few extra variables to hold the errors immediately around the
		// current column. (If we are lucky, those variables are in registers, but
		// even if not, they're probably cheaper to access than array elements are.)
		//
		// The fserrors[] array has (#columns + 2) entries; the extra entry at
		// each end saves us from special-casing the first and last pixels.
		// Each entry is three values long, one value for each color component.
		//
		// Note: on a wide image, we might not have enough room in a PC's near data
		// segment to hold the error array; so it is allocated with alloc.

		// Private subobject
		class my_cquantizer2 : jpeg_color_quantizer
		{
			// Space for the eventually created colormap is stashed here
			public byte[][] sv_colormap;	// colormap allocated at init time
			public int desired;				// desired # of colors = size of colormap

			// Variables for accumulating image statistics
			public ushort[, ,] histogram;	// pointer to the histogram

			public bool needs_zeroed;		// true if next pass must zero histogram

			// Variables for Floyd-Steinberg dithering
			public int[] fserrors;			// accumulated errors
			public bool on_odd_row;			// flag to remember which row we are on
			public int[] error_limiter;		// table for clamping the applied error
		}

		// Prescan some rows of pixels.
		// In this module the prescan simply updates the histogram, which has been
		// initialized to zeroes by start_pass.
		// An output_buf parameter is required by the method signature, but no data
		// is actually output (in fact the buffer controller is probably passing a
		// null pointer).
		static void prescan_quantize(jpeg_decompress cinfo, byte[][] input_buf, uint input_row, byte[][] output_buf, uint output_row, int num_rows)
		{
			my_cquantizer2 cquantize=(my_cquantizer2)cinfo.cquantize;
			ushort[, ,] histogram=cquantize.histogram;
			uint width=cinfo.output_width;

			for(int row=0; row<num_rows; row++)
			{
				byte[] ptr=input_buf[input_row+row];
				for(uint col=width, ind=0; col>0; col--, ind+=3)
				{
					// check for overflow and increment if not.
					if(histogram[ptr[ind]>>C0_SHIFT, ptr[ind+1]>>C1_SHIFT, ptr[ind+2]>>C2_SHIFT]==ushort.MaxValue) continue;
					histogram[ptr[ind]>>C0_SHIFT, ptr[ind+1]>>C1_SHIFT, ptr[ind+2]>>C2_SHIFT]++;
				}
			}
		}

		// Next we have the really interesting routines: selection of a colormap
		// given the completed histogram.
		// These routines work with a list of "boxes", each representing a rectangular
		// subset of the input color space (to histogram precision).
		class box
		{
			// The bounds of the box (inclusive); expressed as histogram indexes
			public int c0min, c0max;
			public int c1min, c1max;
			public int c2min, c2max;
			// The volume (actually 2-norm) of the box
			public int volume;
			// The number of nonzero histogram cells within this box
			public int colorcount;
		}

		// Find the splittable box with the largest color population
		// Returns null if no splittable boxes remain
		static box find_biggest_color_pop(box[] boxlist, int numboxes)
		{
			int maxc=0;
			box which=null;

			for(int i=0; i<numboxes; i++)
			{
				box boxp=boxlist[i];
				if(boxp.colorcount>maxc&&boxp.volume>0)
				{
					which=boxp;
					maxc=boxp.colorcount;
				}
			}
			return which;
		}

		// Find the splittable box with the largest (scaled) volume
		// Returns null if no splittable boxes remain
		static box find_biggest_volume(box[] boxlist, int numboxes)
		{
			int maxv=0;
			box which=null;

			for(int i=0; i<numboxes; i++)
			{
				box boxp=boxlist[i];
				if(boxp.volume>maxv)
				{
					which=boxp;
					maxv=boxp.volume;
				}
			}
			return which;
		}

		// Shrink the min/max bounds of a box to enclose only nonzero elements,
		// and recompute its volume and population
		static void update_box(jpeg_decompress cinfo, box boxp)
		{
			my_cquantizer2 cquantize=(my_cquantizer2)cinfo.cquantize;
			ushort[, ,] histogram=cquantize.histogram;
			int dist0, dist1, dist2;
			int ccount;

			int c0min=boxp.c0min, c0max=boxp.c0max;
			int c1min=boxp.c1min, c1max=boxp.c1max;
			int c2min=boxp.c2min, c2max=boxp.c2max;

			if(c0max>c0min)
			{
				for(int c0=c0min; c0<=c0max; c0++)
				{
					for(int c1=c1min; c1<=c1max; c1++)
					{
						for(int c2=c2min; c2<=c2max; c2++)
						{
							if(histogram[c0, c1, c2]!=0)
							{
								boxp.c0min=c0min=c0;
								goto have_c0min;
							}
						}
					}
				}
			}
have_c0min:
			if(c0max>c0min)
			{
				for(int c0=c0max; c0>=c0min; c0--)
				{
					for(int c1=c1min; c1<=c1max; c1++)
					{
						for(int c2=c2min; c2<=c2max; c2++)
						{
							if(histogram[c0, c1, c2]!=0)
							{
								boxp.c0max=c0max=c0;
								goto have_c0max;
							}
						}
					}
				}
			}
have_c0max:
			if(c1max>c1min)
			{
				for(int c1=c1min; c1<=c1max; c1++)
				{
					for(int c0=c0min; c0<=c0max; c0++)
					{
						for(int c2=c2min; c2<=c2max; c2++)
						{
							if(histogram[c0, c1, c2]!=0)
							{
								boxp.c1min=c1min=c1;
								goto have_c1min;
							}
						}
					}
				}
			}
have_c1min:
			if(c1max>c1min)
			{
				for(int c1=c1max; c1>=c1min; c1--)
				{
					for(int c0=c0min; c0<=c0max; c0++)
					{
						for(int c2=c2min; c2<=c2max; c2++)
						{
							if(histogram[c0, c1, c2]!=0)
							{
								boxp.c1max=c1max=c1;
								goto have_c1max;
							}
						}
					}
				}
			}
have_c1max:
			if(c2max>c2min)
			{
				for(int c2=c2min; c2<=c2max; c2++)
				{
					for(int c0=c0min; c0<=c0max; c0++)
					{
						for(int c1=c1min; c1<=c1max; c1++)
						{
							if(histogram[c0, c1, c2]!=0)
							{
								boxp.c2min=c2min=c2;
								goto have_c2min;
							}
						}
					}
				}
			}
have_c2min:
			if(c2max>c2min)
			{
				for(int c2=c2max; c2>=c2min; c2--)
				{
					for(int c0=c0min; c0<=c0max; c0++)
					{
						for(int c1=c1min; c1<=c1max; c1++)
						{
							if(histogram[c0, c1, c2]!=0)
							{
								boxp.c2max=c2max=c2;
								goto have_c2max;
							}
						}
					}
				}
			}
have_c2max:

			// Update box volume.
			// We use 2-norm rather than real volume here; this biases the method
			// against making long narrow boxes, and it has the side benefit that
			// a box is splittable iff norm > 0.
			// Since the differences are expressed in histogram-cell units,
			// we have to shift back to byte units to get consistent distances;
			// after which, we scale according to the selected distance scale factors.
			dist0=((c0max-c0min)<<C0_SHIFT)*C0_SCALE;
			dist1=((c1max-c1min)<<C1_SHIFT)*C1_SCALE;
			dist2=((c2max-c2min)<<C2_SHIFT)*C2_SCALE;
			boxp.volume=dist0*dist0+dist1*dist1+dist2*dist2;

			// Now scan remaining volume of box and compute population
			ccount=0;
			for(int c0=c0min; c0<=c0max; c0++)
			{
				for(int c1=c1min; c1<=c1max; c1++)
				{
					for(int c2=c2min; c2<=c2max; c2++)
					{
						if(histogram[c0, c1, c2]!=0) ccount++;
					}
				}
			}
			boxp.colorcount=ccount;
		}

		// Repeatedly select and split the largest box until we have enough boxes
		static int median_cut(jpeg_decompress cinfo, box[] boxlist, int numboxes, int desired_colors)
		{
			while(numboxes<desired_colors)
			{
				// Select box to split.
				// Current algorithm: by population for first half, then by volume.
				box b1;
				if(numboxes*2<=desired_colors) b1=find_biggest_color_pop(boxlist, numboxes);
				else b1=find_biggest_volume(boxlist, numboxes);
				if(b1==null) break; // no splittable boxes left!

				box b2=boxlist[numboxes];	// where new box will go

				// Copy the color bounds to the new box.
				b2.c0max=b1.c0max; b2.c1max=b1.c1max; b2.c2max=b1.c2max;
				b2.c0min=b1.c0min; b2.c1min=b1.c1min; b2.c2min=b1.c2min;

				// Choose which axis to split the box on.
				// Current algorithm: longest scaled axis.
				// See notes in update_box about scaling distances.
				int c0=((b1.c0max-b1.c0min)<<C0_SHIFT)*C0_SCALE;
				int c1=((b1.c1max-b1.c1min)<<C1_SHIFT)*C1_SCALE;
				int c2=((b1.c2max-b1.c2min)<<C2_SHIFT)*C2_SCALE;

				// We want to break any ties in favor of green, then red, blue last.
				// This code does the right thing for R,G,B or B,G,R color orders only.
#if !BGR
				int cmax=c1, n=1;
				if(c0>cmax) { cmax=c0; n=0; }
				if(c2>cmax) { n=2; }
#else
				cmax=c1; n=1;
				if(c2>cmax) { cmax=c2; n=2; }
				if(c0>cmax) { n=0; }
#endif

				// Choose split point along selected axis, and update box bounds.
				// Current algorithm: split at halfway point.
				// (Since the box has been shrunk to minimum volume,
				// any split will produce two nonempty subboxes.)
				// Note that lb value is max for lower box, so must be < old max.
				int lb;
				switch(n)
				{
					case 0:
						lb=(b1.c0max+b1.c0min)/2;
						b1.c0max=lb;
						b2.c0min=lb+1;
						break;
					case 1:
						lb=(b1.c1max+b1.c1min)/2;
						b1.c1max=lb;
						b2.c1min=lb+1;
						break;
					case 2:
						lb=(b1.c2max+b1.c2min)/2;
						b1.c2max=lb;
						b2.c2min=lb+1;
						break;
				}
				// Update stats for boxes
				update_box(cinfo, b1);
				update_box(cinfo, b2);
				numboxes++;
			}
			return numboxes;
		}

		// Compute representative color for a box, put it in colormap[icolor]
		static void compute_color(jpeg_decompress cinfo, box boxp, int icolor)
		{
			// Current algorithm: mean weighted by pixels (not colors)
			// Note it is important to get the rounding correct!
			my_cquantizer2 cquantize=(my_cquantizer2)cinfo.cquantize;
			ushort[, ,] histogram=cquantize.histogram;
			int total=0;
			int c0total=0;
			int c1total=0;
			int c2total=0;

			int c0min=boxp.c0min, c0max=boxp.c0max;
			int c1min=boxp.c1min, c1max=boxp.c1max;
			int c2min=boxp.c2min, c2max=boxp.c2max;

			for(int c0=c0min; c0<=c0max; c0++)
			{
				for(int c1=c1min; c1<=c1max; c1++)
				{
					for(int c2=c2min; c2<=c2max; c2++)
					{
						int count=histogram[c0, c1, c2];
						if(count!=0)
						{
							total+=count;
							c0total+=((c0<<C0_SHIFT)+((1<<C0_SHIFT)>>1))*count;
							c1total+=((c1<<C1_SHIFT)+((1<<C1_SHIFT)>>1))*count;
							c2total+=((c2<<C2_SHIFT)+((1<<C2_SHIFT)>>1))*count;
						}
					}
				}
			}

			cinfo.colormap[0][icolor]=(byte)((c0total+(total>>1))/total);
			cinfo.colormap[1][icolor]=(byte)((c1total+(total>>1))/total);
			cinfo.colormap[2][icolor]=(byte)((c2total+(total>>1))/total);
		}

		// Master routine for color selection
		static void select_colors(jpeg_decompress cinfo, int desired_colors)
		{
			box[] boxlist=null;

			// Allocate workspace for box list
			try
			{
				boxlist=new box[desired_colors];
				for(int i=0; i<desired_colors; i++) boxlist[i]=new box();
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}

			// Initialize one box containing whole space
			int numboxes=1;
			boxlist[0].c0min=0;
			boxlist[0].c0max=MAXJSAMPLE>>C0_SHIFT;
			boxlist[0].c1min=0;
			boxlist[0].c1max=MAXJSAMPLE>>C1_SHIFT;
			boxlist[0].c2min=0;
			boxlist[0].c2max=MAXJSAMPLE>>C2_SHIFT;

			// Shrink it to actually-used volume and set its statistics
			update_box(cinfo, boxlist[0]);

			// Perform median-cut to produce final box list
			numboxes=median_cut(cinfo, boxlist, numboxes, desired_colors);

			// Compute the representative color for each box, fill colormap
			for(int i=0; i<numboxes; i++) compute_color(cinfo, boxlist[i], i);
			cinfo.actual_number_of_colors=numboxes;

			TRACEMS1(cinfo, 1, J_MESSAGE_CODE.JTRC_QUANT_SELECTED, numboxes);
		}

		// These routines are concerned with the time-critical task of mapping input
		// colors to the nearest color in the selected colormap.
		//
		// We re-use the histogram space as an "inverse color map", essentially a
		// cache for the results of nearest-color searches. All colors within a
		// histogram cell will be mapped to the same colormap entry, namely the one
		// closest to the cell's center. This may not be quite the closest entry to
		// the actual input color, but it's almost as good. A zero in the cache
		// indicates we haven't found the nearest color for that cell yet; the array
		// is cleared to zeroes before starting the mapping pass. When we find the
		// nearest color for a cell, its colormap index plus one is recorded in the
		// cache for future use. The pass2 scanning routines call fill_inverse_cmap
		// when they need to use an unfilled entry in the cache.
		//
		// Our method of efficiently finding nearest colors is based on the "locally
		// sorted search" idea described by Heckbert and on the incremental distance
		// calculation described by Spencer W. Thomas in chapter III.1 of Graphics
		// Gems II (James Arvo, ed. Academic Press, 1991). Thomas points out that
		// the distances from a given colormap entry to each cell of the histogram can
		// be computed quickly using an incremental method: the differences between
		// distances to adjacent cells themselves differ by a constant. This allows a
		// fairly fast implementation of the "brute force" approach of computing the
		// distance from every colormap entry to every histogram cell. Unfortunately,
		// it needs a work array to hold the best-distance-so-far for each histogram
		// cell (because the inner loop has to be over cells, not colormap entries).
		// The work array elements have to be ints, so the work array would need
		// 256Kb at our recommended precision. This is not feasible in DOS machines.
		//
		// To get around these problems, we apply Thomas' method to compute the
		// nearest colors for only the cells within a small subbox of the histogram.
		// The work array need be only as big as the subbox, so the memory usage
		// problem is solved. Furthermore, we need not fill subboxes that are never
		// referenced in pass2; many images use only part of the color gamut, so a
		// fair amount of work is saved. An additional advantage of this
		// approach is that we can apply Heckbert's locality criterion to quickly
		// eliminate colormap entries that are far away from the subbox; typically
		// three-fourths of the colormap entries are rejected by Heckbert's criterion,
		// and we need not compute their distances to individual cells in the subbox.
		// The speed of this approach is heavily influenced by the subbox size: too
		// small means too much overhead, too big loses because Heckbert's criterion
		// can't eliminate as many colormap entries. Empirically the best subbox
		// size seems to be about 1/512th of the histogram (1/8th in each direction).
		//
		// Thomas' article also describes a refined method which is asymptotically
		// faster than the brute-force method, but it is also far more complex and
		// cannot efficiently be applied to small subboxes. It is therefore not
		// useful for programs intended to be portable to DOS machines. On machines
		// with plenty of memory, filling the whole histogram in one shot with Thomas'
		// refined method might be faster than the present code --- but then again,
		// it might not be any faster, and it's certainly more complicated.

		// log2(histogram cells in update box) for each axis; this can be adjusted
		const int BOX_C0_LOG=(HIST_C0_BITS-3);
		const int BOX_C1_LOG=(HIST_C1_BITS-3);
		const int BOX_C2_LOG=(HIST_C2_BITS-3);

		const int BOX_C0_ELEMS=(1<<BOX_C0_LOG); // # of hist cells in update box
		const int BOX_C1_ELEMS=(1<<BOX_C1_LOG);
		const int BOX_C2_ELEMS=(1<<BOX_C2_LOG);

		const int BOX_C0_SHIFT=(C0_SHIFT+BOX_C0_LOG);
		const int BOX_C1_SHIFT=(C1_SHIFT+BOX_C1_LOG);
		const int BOX_C2_SHIFT=(C2_SHIFT+BOX_C2_LOG);

		// The next three routines implement inverse colormap filling. They could
		// all be folded into one big routine, but splitting them up this way saves
		// some stack space (the mindist[] and bestdist[] arrays need not coexist)
		// and may allow some compilers to produce better code by registerizing more
		// inner-loop variables.

		// Locate the colormap entries close enough to an update box to be candidates
		// for the nearest entry to some cell(s) in the update box. The update box
		// is specified by the center coordinates of its first cell. The number of
		// candidate colormap entries is returned, and their colormap indexes are
		// placed in colorlist[].
		// This routine uses Heckbert's "locally sorted search" criterion to select
		// the colors that need further consideration.
		static int find_nearby_colors(jpeg_decompress cinfo, int minc0, int minc1, int minc2, byte[] colorlist)
		{
			int numcolors=cinfo.actual_number_of_colors;
			int[] mindist=new int[MAXNUMCOLORS];	// min distance to colormap entry i

			// Compute true coordinates of update box's upper corner and center.
			// Actually we compute the coordinates of the center of the upper-corner
			// histogram cell, which are the upper bounds of the volume we care about.
			// Note that since ">>" rounds down, the "center" values may be closer to
			// min than to max; hence comparisons to them must be "<=", not "<".
			int maxc0=minc0+((1<<BOX_C0_SHIFT)-(1<<C0_SHIFT));
			int centerc0=(minc0+maxc0)>>1;
			int maxc1=minc1+((1<<BOX_C1_SHIFT)-(1<<C1_SHIFT));
			int centerc1=(minc1+maxc1)>>1;
			int maxc2=minc2+((1<<BOX_C2_SHIFT)-(1<<C2_SHIFT));
			int centerc2=(minc2+maxc2)>>1;

			// For each color in colormap, find:
			//	1.	its minimum squared-distance to any point in the update box
			//		(zero if color is within update box);
			//	2.	its maximum squared-distance to any point in the update box.
			// Both of these can be found by considering only the corners of the box.
			// We save the minimum distance for each color in mindist[];
			// only the smallest maximum distance is of interest.
			int minmaxdist=0x7FFFFFFF;

			for(int i=0; i<numcolors; i++)
			{
				int min_dist, max_dist, tdist;

				// We compute the squared-c0-distance term, then add in the other two.
				int x=cinfo.colormap[0][i];
				if(x<minc0)
				{
					tdist=(x-minc0)*C0_SCALE;
					min_dist=tdist*tdist;
					tdist=(x-maxc0)*C0_SCALE;
					max_dist=tdist*tdist;
				}
				else if(x>maxc0)
				{
					tdist=(x-maxc0)*C0_SCALE;
					min_dist=tdist*tdist;
					tdist=(x-minc0)*C0_SCALE;
					max_dist=tdist*tdist;
				}
				else
				{
					// within cell range so no contribution to min_dist
					min_dist=0;
					if(x<=centerc0)
					{
						tdist=(x-maxc0)*C0_SCALE;
						max_dist=tdist*tdist;
					}
					else
					{
						tdist=(x-minc0)*C0_SCALE;
						max_dist=tdist*tdist;
					}
				}

				x=cinfo.colormap[1][i];
				if(x<minc1)
				{
					tdist=(x-minc1)*C1_SCALE;
					min_dist+=tdist*tdist;
					tdist=(x-maxc1)*C1_SCALE;
					max_dist+=tdist*tdist;
				}
				else if(x>maxc1)
				{
					tdist=(x-maxc1)*C1_SCALE;
					min_dist+=tdist*tdist;
					tdist=(x-minc1)*C1_SCALE;
					max_dist+=tdist*tdist;
				}
				else
				{
					// within cell range so no contribution to min_dist
					if(x<=centerc1)
					{
						tdist=(x-maxc1)*C1_SCALE;
						max_dist+=tdist*tdist;
					}
					else
					{
						tdist=(x-minc1)*C1_SCALE;
						max_dist+=tdist*tdist;
					}
				}

				x=cinfo.colormap[2][i];
				if(x<minc2)
				{
					tdist=(x-minc2)*C2_SCALE;
					min_dist+=tdist*tdist;
					tdist=(x-maxc2)*C2_SCALE;
					max_dist+=tdist*tdist;
				}
				else if(x>maxc2)
				{
					tdist=(x-maxc2)*C2_SCALE;
					min_dist+=tdist*tdist;
					tdist=(x-minc2)*C2_SCALE;
					max_dist+=tdist*tdist;
				}
				else
				{
					// within cell range so no contribution to min_dist
					if(x<=centerc2)
					{
						tdist=(x-maxc2)*C2_SCALE;
						max_dist+=tdist*tdist;
					}
					else
					{
						tdist=(x-minc2)*C2_SCALE;
						max_dist+=tdist*tdist;
					}
				}

				mindist[i]=min_dist;	// save away the results
				if(max_dist<minmaxdist) minmaxdist=max_dist;
			}

			// Now we know that no cell in the update box is more than minmaxdist
			// away from some colormap entry. Therefore, only colors that are
			// within minmaxdist of some part of the box need be considered.
			int ncolors=0;
			for(int i=0; i<numcolors; i++)
			{
				if(mindist[i]<=minmaxdist) colorlist[ncolors++]=(byte)i;
			}
			return ncolors;
		}

		// Nominal steps between cell centers ("x" in Thomas article)
		const int STEP_C0=((1<<C0_SHIFT)*C0_SCALE);
		const int STEP_C1=((1<<C1_SHIFT)*C1_SCALE);
		const int STEP_C2=((1<<C2_SHIFT)*C2_SCALE);

		// Find the closest colormap entry for each cell in the update box,
		// given the list of candidate colors prepared by find_nearby_colors.
		// Return the indexes of the closest entries in the bestcolor[] array.
		// This routine uses Thomas' incremental distance calculation method to
		// find the distance from a colormap entry to successive cells in the box.
		static void find_best_colors(jpeg_decompress cinfo, int minc0, int minc1, int minc2, int numcolors, byte[] colorlist, byte[] bestcolor)
		{
			// This array holds the distance to the nearest-so-far color for each cell
			int[] bestdist=new int[BOX_C0_ELEMS*BOX_C1_ELEMS*BOX_C2_ELEMS];

			// Initialize best-distance for each cell of the update box
			for(int i=(BOX_C0_ELEMS*BOX_C1_ELEMS*BOX_C2_ELEMS-1); i>=0; i--) bestdist[i]=0x7FFFFFFF;

			// For each color selected by find_nearby_colors,
			// compute its distance to the center of each cell in the box.
			// If that's less than best-so-far, update best distance and color number.
			for(int i=0; i<numcolors; i++)
			{
				int icolor=colorlist[i];

				// Compute (square of) distance from minc0/c1/c2 to this color
				int inc0=(minc0-cinfo.colormap[0][icolor])*C0_SCALE;
				int dist0=inc0*inc0;
				int inc1=(minc1-cinfo.colormap[1][icolor])*C1_SCALE;
				dist0+=inc1*inc1;
				int inc2=(minc2-cinfo.colormap[2][icolor])*C2_SCALE;
				dist0+=inc2*inc2;

				// Form the initial difference increments
				inc0=inc0*(2*STEP_C0)+STEP_C0*STEP_C0;
				inc1=inc1*(2*STEP_C1)+STEP_C1*STEP_C1;
				inc2=inc2*(2*STEP_C2)+STEP_C2*STEP_C2;

				// Now loop over all cells in box, updating distance per Thomas method
				int bptr=0;// pointer into bestdist[] array
				int cptr=0;// pointer into bestcolor[] array
				int xx0=inc0;
				for(int ic0=BOX_C0_ELEMS-1; ic0>=0; ic0--)
				{
					int dist1=dist0;
					int xx1=inc1;
					for(int ic1=BOX_C1_ELEMS-1; ic1>=0; ic1--)
					{
						int dist2=dist1;
						int xx2=inc2;
						for(int ic2=BOX_C2_ELEMS-1; ic2>=0; ic2--)
						{
							if(dist2<bestdist[bptr])
							{
								bestdist[bptr]=dist2;
								bestcolor[cptr]=(byte)icolor;
							}
							dist2+=xx2;
							xx2+=2*STEP_C2*STEP_C2;
							bptr++;
							cptr++;
						}
						dist1+=xx1;
						xx1+=2*STEP_C1*STEP_C1;
					}
					dist0+=xx0;
					xx0+=2*STEP_C0*STEP_C0;
				}
			}
		}

		// Fill the inverse-colormap entries in the update box that contains
		// histogram cell c0/c1/c2. (Only that one cell MUST be filled, but
		// we can fill as many others as we wish.)
		static void fill_inverse_cmap(jpeg_decompress cinfo, int c0, int c1, int c2)
		{
			my_cquantizer2 cquantize=(my_cquantizer2)cinfo.cquantize;
			ushort[, ,] histogram=cquantize.histogram;

			// This array lists the candidate colormap indexes.
			byte[] colorlist=new byte[MAXNUMCOLORS];

			// This array holds the actually closest colormap index for each cell.
			byte[] bestcolor=new byte[BOX_C0_ELEMS*BOX_C1_ELEMS*BOX_C2_ELEMS];

			// Convert cell coordinates to update box ID
			c0>>=BOX_C0_LOG;
			c1>>=BOX_C1_LOG;
			c2>>=BOX_C2_LOG;

			// Compute true coordinates of update box's origin corner.
			// Actually we compute the coordinates of the center of the corner
			// histogram cell, which are the lower bounds of the volume we care about.
			int minc0=(c0<<BOX_C0_SHIFT)+((1<<C0_SHIFT)>>1);
			int minc1=(c1<<BOX_C1_SHIFT)+((1<<C1_SHIFT)>>1);
			int minc2=(c2<<BOX_C2_SHIFT)+((1<<C2_SHIFT)>>1);

			// Determine which colormap entries are close enough to be candidates
			// for the nearest entry to some cell in the update box.
			int numcolors=find_nearby_colors(cinfo, minc0, minc1, minc2, colorlist);

			// Determine the actually nearest colors.
			find_best_colors(cinfo, minc0, minc1, minc2, numcolors, colorlist, bestcolor);

			// Save the best color numbers (plus 1) in the main cache array
			c0<<=BOX_C0_LOG;		// convert ID back to base cell indexes
			c1<<=BOX_C1_LOG;
			c2<<=BOX_C2_LOG;

			int cptr=0; // pointer into bestcolor[] array
			for(int ic0=0; ic0<BOX_C0_ELEMS; ic0++)
			{
				for(int ic1=0; ic1<BOX_C1_ELEMS; ic1++)
				{
					for(int ic2=0; ic2<BOX_C2_ELEMS; ic2++)
					{
						histogram[c0+ic0, c1+ic1, c2+ic2]=(ushort)(bestcolor[cptr++]+1);
					}
				}
			}
		}

		// Map some rows of pixels to the output colormapped representation.

		// This version performs no dithering
		static void pass2_no_dither(jpeg_decompress cinfo, byte[][] input_buf, uint input_row, byte[][] output_buf, uint output_row, int num_rows)
		{
			my_cquantizer2 cquantize=(my_cquantizer2)cinfo.cquantize;
			ushort[, ,] histogram=cquantize.histogram;
			uint width=cinfo.output_width;

			for(int row=0; row<num_rows; row++)
			{
				byte[] inptr=input_buf[input_row+row];
				byte[] outptr=output_buf[output_row+row];
				uint iind=0, oind=0;
				for(uint col=width; col>0; col--)
				{
					// get pixel value and index into the cache
					int c0=inptr[iind++]>>C0_SHIFT;
					int c1=inptr[iind++]>>C1_SHIFT;
					int c2=inptr[iind++]>>C2_SHIFT;

					// If we have not seen this color before, find nearest colormap entry
					// and update the cache
					if(histogram[c0, c1, c2]==0) fill_inverse_cmap(cinfo, c0, c1, c2);

					// Now emit the colormap index for this cell
					outptr[oind++]=(byte)(histogram[c0, c1, c2]-1);
				}
			}
		}

		// This version performs Floyd-Steinberg dithering
		static void pass2_fs_dither(jpeg_decompress cinfo, byte[][] input_buf, uint input_row, byte[][] output_buf, uint output_row, int num_rows)
		{
			my_cquantizer2 cquantize=(my_cquantizer2)cinfo.cquantize;
			ushort[, ,] histogram=cquantize.histogram;
			uint width=cinfo.output_width;
			int[] error_limit=cquantize.error_limiter;
			byte[] colormap0=cinfo.colormap[0];
			byte[] colormap1=cinfo.colormap[1];
			byte[] colormap2=cinfo.colormap[2];

			for(int row=0; row<num_rows; row++)
			{
				byte[] inptr=input_buf[input_row+row];
				byte[] outptr=output_buf[output_row+row];
				int iind=0, oind=0;

				int dir;			// +1 or -1 depending on direction
				int dir3;			// 3*dir, for advancing inptr & errorptr

				int[] errorptr=cquantize.fserrors;		// => fserrors[] at column before current
				int errorptr_ind=0;

				if(cquantize.on_odd_row)
				{
					// work right to left in this row
					iind+=(int)(width-1)*3;	// so point to rightmost pixel
					oind+=(int)width-1;
					dir=-1;
					dir3=-3;
					errorptr_ind=(int)(width+1)*3; // => entry after last column
					cquantize.on_odd_row=false; // flip for next time
				}
				else
				{
					// work left to right in this row
					dir=1;
					dir3=3;
					errorptr_ind=0;
					cquantize.on_odd_row=true; // flip for next time
				}

				// Preset error values: no error propagated to first pixel from left
				int cur0=0, cur1=0, cur2=0; // current error or pixel value

				// and no error propagated to row below yet
				int belowerr0=0, belowerr1=0, belowerr2=0;	// error for pixel below cur
				int bpreverr0=0, bpreverr1=0, bpreverr2=0;	// error for below/prev col

				for(uint col=width; col>0; col--)
				{
					// curN holds the error propagated from the previous pixel on the
					// current line. Add the error propagated from the previous line
					// to form the complete error correction term for this pixel, and
					// round the error term (which is expressed * 16) to an integer.
					// Right shift rounds towards minus infinity, so adding 8 is correct
					// for either sign of the error value.
					// Note: errorptr points to *previous* column's array entry.
					cur0=(cur0+errorptr[errorptr_ind+dir3]+8)>>4;
					cur1=(cur1+errorptr[errorptr_ind+dir3+1]+8)>>4;
					cur2=(cur2+errorptr[errorptr_ind+dir3+2]+8)>>4;

					// Limit the error using transfer function set by init_error_limit.
					// See comments with init_error_limit for rationale.
					cur0=error_limit[MAXJSAMPLE+cur0];
					cur1=error_limit[MAXJSAMPLE+cur1];
					cur2=error_limit[MAXJSAMPLE+cur2];

					// Form pixel value + error, and range-limit to 0..MAXJSAMPLE.
					// The maximum error is +- MAXJSAMPLE (or less with error limiting);
					// this sets the required size of the range_limit array.
					cur0+=inptr[iind];
					cur1+=inptr[iind+1];
					cur2+=inptr[iind+2];
					cur0=(cur0>=MAXJSAMPLE?MAXJSAMPLE:(cur0<0?0:cur0));
					cur1=(cur1>=MAXJSAMPLE?MAXJSAMPLE:(cur1<0?0:cur1));
					cur2=(cur2>=MAXJSAMPLE?MAXJSAMPLE:(cur2<0?0:cur2));

					// If we have not seen this color before, find nearest colormap
					// entry and update the cache
					if(histogram[cur0>>C0_SHIFT, cur1>>C1_SHIFT, cur2>>C2_SHIFT]==0) fill_inverse_cmap(cinfo, cur0>>C0_SHIFT, cur1>>C1_SHIFT, cur2>>C2_SHIFT);

					// Now emit the colormap index for this cell
					int pixcode=histogram[cur0>>C0_SHIFT, cur1>>C1_SHIFT, cur2>>C2_SHIFT]-1;
					outptr[oind]=(byte)pixcode;

					// Compute representation error for this pixel
					cur0-=colormap0[pixcode];
					cur1-=colormap1[pixcode];
					cur2-=colormap2[pixcode];

					// Compute error fractions to be propagated to adjacent pixels.
					// Add these into the running sums, and simultaneously shift the
					// next-line error sums left by 1 column.
					int bnexterr=cur0;	// Process component 0
					int delta=cur0*2;
					cur0+=delta;		// form error * 3
					errorptr[errorptr_ind]=bpreverr0+cur0;
					cur0+=delta;		// form error * 5
					bpreverr0=belowerr0+cur0;
					belowerr0=bnexterr;
					cur0+=delta;		// form error * 7
					bnexterr=cur1;		// Process component 1
					delta=cur1*2;
					cur1+=delta;		// form error * 3
					errorptr[errorptr_ind+1]=bpreverr1+cur1;
					cur1+=delta;		// form error * 5
					bpreverr1=belowerr1+cur1;
					belowerr1=bnexterr;
					cur1+=delta;		// form error * 7
					bnexterr=cur2;		// Process component 2
					delta=cur2*2;
					cur2+=delta;		// form error * 3
					errorptr[errorptr_ind+2]=bpreverr2+cur2;
					cur2+=delta;		// form error * 5
					bpreverr2=belowerr2+cur2;
					belowerr2=bnexterr;
					cur2+=delta;		// form error * 7

					// At this point curN contains the 7/16 error value to be propagated
					// to the next pixel on the current line, and all the errors for the
					// next line have been shifted over. We are therefore ready to move on.
					iind+=dir3;		// Advance pixel pointers to next column
					oind+=dir;
					errorptr_ind+=dir3;		// advance errorptr to current column
				}
				// Post-loop cleanup: we must unload the final error values into the
				// final fserrors[] entry. Note we need not unload belowerrN because
				// it is for the dummy column before or after the actual array.
				errorptr[errorptr_ind]=bpreverr0; // unload prev errs into array
				errorptr[errorptr_ind+1]=bpreverr1;
				errorptr[errorptr_ind+2]=bpreverr2;
			}
		}

		// Initialize the error-limiting transfer function (lookup table).
		// The raw F-S error computation can potentially compute error values of up to
		// +- MAXJSAMPLE. But we want the maximum correction applied to a pixel to be
		// much less, otherwise obviously wrong pixels will be created. (Typical
		// effects include weird fringes at color-area boundaries, isolated bright
		// pixels in a dark area, etc.) The standard advice for avoiding this problem
		// is to ensure that the "corners" of the color cube are allocated as output
		// colors; then repeated errors in the same direction cannot cause cascading
		// error buildup. However, that only prevents the error from getting
		// completely out of hand; Aaron Giles reports that error limiting improves
		// the results even with corner colors allocated.
		// A simple clamping of the error values to about +- MAXJSAMPLE/8 works pretty
		// well, but the smoother transfer function used below is even better. Thanks
		// to Aaron Giles for this idea.

		// Allocate and fill in the error_limiter table
		static void init_error_limit(jpeg_decompress cinfo)
		{
			my_cquantizer2 cquantize=(my_cquantizer2)cinfo.cquantize;
			int[] table=null;

			try
			{
				table=new int[MAXJSAMPLE*2+1];
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}
			int table_ind=MAXJSAMPLE;		// so can index -MAXJSAMPLE .. +MAXJSAMPLE
			cquantize.error_limiter=table;

			int STEPSIZE=((MAXJSAMPLE+1)/16);

			// Map errors 1:1 up to +- MAXJSAMPLE/16
			int _out=0;
			int _in=0;
			for(; _in<STEPSIZE; _in++, _out++)
			{
				table[table_ind+_in]=_out; table[table_ind-_in]=-_out;
			}

			// Map errors 1:2 up to +- 3*MAXJSAMPLE/16
			for(; _in<STEPSIZE*3; _in++, _out+=(_in&1)!=0?0:1)
			{
				table[table_ind+_in]=_out; table[table_ind-_in]=-_out;
			}

			// Clamp the rest to final out value (which is (MAXJSAMPLE+1)/8)
			for(; _in<=MAXJSAMPLE; _in++)
			{
				table[table_ind+_in]=_out; table[table_ind-_in]=-_out;
			}
		}

		// Finish up at the end of each pass.
		static void finish_pass1(jpeg_decompress cinfo)
		{
			my_cquantizer2 cquantize=(my_cquantizer2)cinfo.cquantize;

			// Select the representative colors and fill in cinfo.colormap
			cinfo.colormap=cquantize.sv_colormap;
			select_colors(cinfo, cquantize.desired);

			// Force next pass to zero the color index table
			cquantize.needs_zeroed=true;
		}

		static void finish_pass2(jpeg_decompress cinfo)
		{
			// no work
		}

		// Initialize for each processing pass.
		static void start_pass_2_quant(jpeg_decompress cinfo, bool is_pre_scan)
		{
			my_cquantizer2 cquantize=(my_cquantizer2)cinfo.cquantize;
			ushort[, ,] histogram=cquantize.histogram;

			// Only F-S dithering or no dithering is supported.
			// If user asks for ordered dither, give him F-S.
			if(cinfo.dither_mode!=J_DITHER_MODE.JDITHER_NONE) cinfo.dither_mode=J_DITHER_MODE.JDITHER_FS;

			if(is_pre_scan)
			{
				// Set up method pointers
				cquantize.color_quantize=prescan_quantize;
				cquantize.finish_pass=finish_pass1;
				cquantize.needs_zeroed=true; // Always zero histogram
			}
			else
			{
				// Set up method pointers
				if(cinfo.dither_mode==J_DITHER_MODE.JDITHER_FS) cquantize.color_quantize=pass2_fs_dither;
				else cquantize.color_quantize=pass2_no_dither;
				cquantize.finish_pass=finish_pass2;

				// Make sure color count is acceptable
				int i=cinfo.actual_number_of_colors;
				if(i<1) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_QUANT_FEW_COLORS, 1);
				if(i>MAXNUMCOLORS) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_QUANT_MANY_COLORS, MAXNUMCOLORS);

				if(cinfo.dither_mode==J_DITHER_MODE.JDITHER_FS)
				{
					uint arraysize=(cinfo.output_width+2)*3;

					// Allocate Floyd-Steinberg workspace if we didn't already.
					if(cquantize.fserrors==null)
					{
						try
						{
							cquantize.fserrors=new int[arraysize];
						}
						catch
						{
							ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
						}
					}

					// Initialize the propagated errors to zero.
					for(int j=0; j<arraysize; j++) cquantize.fserrors[j]=0;

					// Make the error-limit table if we didn't already.
					if(cquantize.error_limiter==null) init_error_limit(cinfo);
					cquantize.on_odd_row=false;
				}
			}

			// Zero the histogram or inverse color map, if necessary
			if(cquantize.needs_zeroed)
			{
				for(int i=0; i<HIST_C0_ELEMS; i++)
				{
					for(int j=0; j<HIST_C1_ELEMS; j++)
					{
						for(int k=0; k<HIST_C2_ELEMS; k++)
						{
							histogram[i, j, k]=0;
						}
					}
				}
				cquantize.needs_zeroed=false;
			}
		}

		// Switch to a new external colormap between output passes.
		static void new_color_map_2_quant(jpeg_decompress cinfo)
		{
			my_cquantizer2 cquantize=(my_cquantizer2)cinfo.cquantize;

			// Reset the inverse color map
			cquantize.needs_zeroed=true;
		}

		// Module initialization routine for 2-pass color quantization.
		public static void jinit_2pass_quantizer(jpeg_decompress cinfo)
		{
			my_cquantizer2 cquantize=null;

			try
			{
				cquantize=new my_cquantizer2();
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}
			cinfo.cquantize=cquantize;
			cquantize.start_pass=start_pass_2_quant;
			cquantize.new_color_map=new_color_map_2_quant;
			cquantize.fserrors=null;		// flag optional arrays not allocated
			cquantize.error_limiter=null;

			// Make sure jdmaster didn't give me a case I can't handle
			if(cinfo.out_color_components!=3) ERREXIT(cinfo, J_MESSAGE_CODE.JERR_NOTIMPL);

			// Allocate the histogram/inverse colormap storage
			try
			{
				cquantize.histogram=new ushort[HIST_C0_ELEMS, HIST_C1_ELEMS, HIST_C2_ELEMS];
			}
			catch
			{
				ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
			}
			cquantize.needs_zeroed=true; // histogram is garbage now

			// Allocate storage for the completed colormap, if required.
			// We do this now since it is storage and may affect
			// the memory manager's space calculations.
			if(cinfo.enable_2pass_quant)
			{
				// Make sure color count is acceptable
				int desired=cinfo.desired_number_of_colors;

				// Lower bound on # of colors ... somewhat arbitrary as long as > 0
				if(desired<8) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_QUANT_FEW_COLORS, 8);

				// Make sure colormap indexes can be represented by bytes
				if(desired>MAXNUMCOLORS) ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_QUANT_MANY_COLORS, MAXNUMCOLORS);
				cquantize.sv_colormap=alloc_sarray(cinfo, (uint)desired, 3);
				cquantize.desired=desired;
			}
			else cquantize.sv_colormap=null;

			// Only F-S dithering or no dithering is supported.
			// If user asks for ordered dither, give him F-S.
			if(cinfo.dither_mode!=J_DITHER_MODE.JDITHER_NONE) cinfo.dither_mode=J_DITHER_MODE.JDITHER_FS;

			// Allocate Floyd-Steinberg workspace if necessary.
			// This isn't really needed until pass 2, but again it is storage.
			// Although we will cope with a later change in dither_mode,
			// we do not promise to honor max_memory_to_use if dither_mode changes.
			if(cinfo.dither_mode==J_DITHER_MODE.JDITHER_FS)
			{
				try
				{
					cquantize.fserrors=new int[(cinfo.output_width+2)*3];
				}
				catch
				{
					ERREXIT1(cinfo, J_MESSAGE_CODE.JERR_OUT_OF_MEMORY, 4);
				}

				// Might as well create the error-limiting table too.
				init_error_limit(cinfo);
			}
		}
	}
}
#endif // QUANT_2PASS_SUPPORTED
