// Exif.cs
//
// Extends libjpeg version 6b - 27-Mar-1998
// Copyright (C) 2007-2008 by the Authors
// For conditions of distribution and use, see the accompanying License.txt file.
//
// This file extends the JPEG library with reading and writting capabilities for EXIF Version 2.2.
//
// It only interprets APP1 and read/write only IFD0 (Thumbnails are not supported)

using System;
using System.Collections.Generic;
using System.Text;

#if DEBUG
	using System.Diagnostics;
#endif

namespace Free.Ports.LibJpeg
{
	#region Stuff
	public enum EXIF_TAG_STATE
	{
		NotSet,
		ReadWrite,
		DontWrite
	}

	public enum EXIF_TYPE:ushort
	{
		BYTE=1,
		ASCII=2,
		SHORT=3,
		LONG=4,
		RATIONAL=5,
		SBYTE=6,
		UNDEFINED=7,
		SSHORT=8,
		SLONG=9,
		SRATIONAL=10,
		FLOAT=11,
		DOUBLE=12
	}

	public struct RATIONAL
	{
		public uint numerator, denominator;

		public double Value
		{
			get { return (double)numerator/denominator; }
		}
	}

	public struct SRATIONAL
	{
		public int numerator, denominator;

		public double Value
		{
			get { return (double)numerator/denominator; }
		}
	}

	public class IFDEntry
	{
		ushort tag;
		EXIF_TYPE type;
		object data;

		#region public IFDEntry(ushort tag, Type data)
		public IFDEntry(ushort tag, byte data, bool undefined)
		{
			this.tag=tag;
			this.data=new byte[] { data };
			type=undefined?EXIF_TYPE.UNDEFINED:EXIF_TYPE.ASCII;
		}

		public IFDEntry(ushort tag, char data)
		{
			this.tag=tag;
			this.data=data.ToString();
			type=EXIF_TYPE.ASCII;
		}

		public IFDEntry(ushort tag, ushort data)
		{
			this.tag=tag;
			this.data=new ushort[] { data };
			type=EXIF_TYPE.SHORT;
		}

		public IFDEntry(ushort tag, uint data)
		{
			this.tag=tag;
			this.data=new uint[] { data };
			type=EXIF_TYPE.LONG;
		}

		public IFDEntry(ushort tag, RATIONAL data)
		{
			this.tag=tag;
			this.data=new RATIONAL[] { data };
			type=EXIF_TYPE.RATIONAL;
		}

		public IFDEntry(ushort tag, sbyte data)
		{
			this.tag=tag;
			this.data=new sbyte[] { data };
			type=EXIF_TYPE.SBYTE;
		}

		public IFDEntry(ushort tag, short data)
		{
			this.tag=tag;
			this.data=new short[] { data };
			type=EXIF_TYPE.SSHORT;
		}

		public IFDEntry(ushort tag, int data)
		{
			this.tag=tag;
			this.data=new int[] { data };
			type=EXIF_TYPE.SLONG;
		}

		public IFDEntry(ushort tag, SRATIONAL data)
		{
			this.tag=tag;
			this.data=new SRATIONAL[] { data };
			type=EXIF_TYPE.SRATIONAL;
		}

		public IFDEntry(ushort tag, float data)
		{
			this.tag=tag;
			this.data=new float[] { data };
			type=EXIF_TYPE.FLOAT;
		}

		public IFDEntry(ushort tag, double data)
		{
			this.tag=tag;
			this.data=new double[] { data };
			type=EXIF_TYPE.DOUBLE;
		}
		#endregion

		#region public IFDEntry(ushort tag, Array[] data)
		public IFDEntry(ushort tag, byte[] data, bool undefined)
		{
			if(data.Length==0) throw new ArgumentException("Must be an filled array.", "data");
			this.tag=tag;
			this.data=data;
			type=undefined?EXIF_TYPE.UNDEFINED:EXIF_TYPE.ASCII;
		}

		public IFDEntry(ushort tag, string data)
		{
			if(data.Length==0) throw new ArgumentException("Must be an filled string.", "data");
			this.tag=tag;
			this.data=data;
			type=EXIF_TYPE.ASCII;
		}

		public IFDEntry(ushort tag, ushort[] data)
		{
			if(data.Length==0) throw new ArgumentException("Must be an filled array.", "data");
			this.tag=tag;
			this.data=data;
			type=EXIF_TYPE.SHORT;
		}

		public IFDEntry(ushort tag, uint[] data)
		{
			if(data.Length==0) throw new ArgumentException("Must be an filled array.", "data");
			this.tag=tag;
			this.data=data;
			type=EXIF_TYPE.LONG;
		}

		public IFDEntry(ushort tag, RATIONAL[] data)
		{
			if(data.Length==0) throw new ArgumentException("Must be an filled array.", "data");
			this.tag=tag;
			this.data=data;
			type=EXIF_TYPE.RATIONAL;
		}

		public IFDEntry(ushort tag, sbyte[] data)
		{
			if(data.Length==0) throw new ArgumentException("Must be an filled array.", "data");
			this.tag=tag;
			this.data=data;
			type=EXIF_TYPE.SBYTE;
		}

		public IFDEntry(ushort tag, short[] data)
		{
			if(data.Length==0) throw new ArgumentException("Must be an filled array.", "data");
			this.tag=tag;
			this.data=data;
			type=EXIF_TYPE.SSHORT;
		}

		public IFDEntry(ushort tag, int[] data)
		{
			if(data.Length==0) throw new ArgumentException("Must be an filled array.", "data");
			this.tag=tag;
			this.data=data;
			type=EXIF_TYPE.SLONG;
		}

		public IFDEntry(ushort tag, SRATIONAL[] data)
		{
			if(data.Length==0) throw new ArgumentException("Must be an filled array.", "data");
			this.tag=tag;
			this.data=data;
			type=EXIF_TYPE.SRATIONAL;
		}

		public IFDEntry(ushort tag, float[] data)
		{
			if(data.Length==0) throw new ArgumentException("Must be an filled array.", "data");
			this.tag=tag;
			this.data=data;
			type=EXIF_TYPE.FLOAT;
		}

		public IFDEntry(ushort tag, double[] data)
		{
			if(data.Length==0) throw new ArgumentException("Must be an filled array.", "data");
			this.tag=tag;
			this.data=data;
			type=EXIF_TYPE.DOUBLE;
		}
		#endregion

		public ushort Tag { get { return tag; } }
		public EXIF_TYPE Type { get { return type; } }
		public object Data { get { return data; } }

		internal uint DataSize
		{
			get 
			{
				uint size=0;
				if(type==EXIF_TYPE.ASCII)
				{
					string str=data as string;
					size=(uint)str.Length+1; // +1 for NUL
				}
				else
				{
					Array ar=data as Array;
					size=(uint)ar.Length*EXIF.GetTypeSize(type);
				}

				if(size<=4) return 0;

				size+=1;
				size&=0xFFFFFFFE; // word-Boundary of the next data

				return size;
			}
		}

		static internal int CompareIFDEntry(IFDEntry x, IFDEntry y)
		{
			if(x==null) return (y==null)?0:-1;
			if(y==null) return 1;

			if(x.tag==y.tag) return 0;
			if(x.tag<y.tag) return -1;

			return 1;
		}
	}
	#endregion

	public class EXIF
	{
		bool isIntel;

		Dictionary<ushort, IFDEntry> IFD0Table=new Dictionary<ushort,IFDEntry>();
		Dictionary<ushort, IFDEntry> ExifTable=new Dictionary<ushort, IFDEntry>();
		Dictionary<ushort, IFDEntry> GPSTable=new Dictionary<ushort, IFDEntry>();
		Dictionary<ushort, IFDEntry> InteroperabilityTable=new Dictionary<ushort, IFDEntry>();

		#region Add, get and remove
		public void AddToIFD0Table(IFDEntry entry) 
		{
			if(IFD0Table.ContainsKey(entry.Tag)) IFD0Table[entry.Tag]=entry;
			else IFD0Table.Add(entry.Tag, entry); 
		}
		
		public void AddToExifTable(IFDEntry entry) 
		{
			if(ExifTable.ContainsKey(entry.Tag)) ExifTable[entry.Tag]=entry;
			else ExifTable.Add(entry.Tag, entry);
		}
		
		public void AddToGPSTable(IFDEntry entry) 
		{
			if(GPSTable.ContainsKey(entry.Tag)) GPSTable[entry.Tag]=entry;
			else GPSTable.Add(entry.Tag, entry);
		}

		public void AddToInteroperabilityTable(IFDEntry entry) 
		{
			if(InteroperabilityTable.ContainsKey(entry.Tag)) InteroperabilityTable[entry.Tag]=entry;
			else InteroperabilityTable.Add(entry.Tag, entry);
		}

		public IFDEntry GetFromIFD0Table(ushort tag)
		{
			if(IFD0Table.ContainsKey(tag)) return IFD0Table[tag];
			return null;
		}

		public IFDEntry GetFromExifTable(ushort tag)
		{
			if(ExifTable.ContainsKey(tag)) return ExifTable[tag];
			return null;
		}

		public IFDEntry GetFromGPSTable(ushort tag)
		{
			if(GPSTable.ContainsKey(tag)) return GPSTable[tag];
			return null;
		}

		public IFDEntry GetFromInteroperabilityTable(ushort tag)
		{
			if(InteroperabilityTable.ContainsKey(tag)) return InteroperabilityTable[tag];
			return null;
		}

		public bool RemoveFromIFD0Table(ushort tag)
		{
			if(!IFD0Table.ContainsKey(tag)) return false;
			IFD0Table.Remove(tag);
			return true;
		}

		public bool RemoveFromExifTable(ushort tag)
		{
			if(!ExifTable.ContainsKey(tag)) return false;
			ExifTable.Remove(tag);
			return true;
		}

		public bool RemoveFromGPSTable(ushort tag)
		{
			if(!GPSTable.ContainsKey(tag)) return false;
			GPSTable.Remove(tag);
			return true;
		}

		public bool RemoveFromInteroperabilityTable(ushort tag)
		{
			if(!InteroperabilityTable.ContainsKey(tag)) return false;
			InteroperabilityTable.Remove(tag);
			return true;
		}
		#endregion
		
		#region Get??? methods
		byte[] getConvertTemp=new byte[8];

		short GetSSHORT(byte[] data, uint offset)
		{
			if(isIntel)
			{
				if(offset<=int.MaxValue) return BitConverter.ToInt16(data, (int)offset);
				getConvertTemp[0]=data[offset+0];
				getConvertTemp[1]=data[offset+1];
				return BitConverter.ToInt16(getConvertTemp, 0);
			}

			getConvertTemp[0]=data[offset+1];
			getConvertTemp[1]=data[offset+0];
			return BitConverter.ToInt16(getConvertTemp, 0);
		}

		ushort GetSHORT(byte[] data, uint offset)
		{
			if(isIntel)
			{
				if(offset<=int.MaxValue) return BitConverter.ToUInt16(data, (int)offset);
				getConvertTemp[0]=data[offset+0];
				getConvertTemp[1]=data[offset+1];
				return BitConverter.ToUInt16(getConvertTemp, 0);
			}

			getConvertTemp[0]=data[offset+1];
			getConvertTemp[1]=data[offset+0];
			return BitConverter.ToUInt16(getConvertTemp, 0);
		}

		int GetSLONG(byte[] data, uint offset)
		{
			if(isIntel)
			{
				if(offset<=int.MaxValue) return BitConverter.ToInt32(data, (int)offset);
				getConvertTemp[0]=data[offset+0];
				getConvertTemp[1]=data[offset+1];
				getConvertTemp[2]=data[offset+2];
				getConvertTemp[3]=data[offset+3];
				return BitConverter.ToInt32(getConvertTemp, 0);
			}

			getConvertTemp[0]=data[offset+3];
			getConvertTemp[1]=data[offset+2];
			getConvertTemp[2]=data[offset+1];
			getConvertTemp[3]=data[offset+0];
			return BitConverter.ToInt32(getConvertTemp, 0);
		}

		uint GetLONG(byte[] data, uint offset)
		{
			if(isIntel)
			{
				if(offset<=int.MaxValue) return BitConverter.ToUInt32(data, (int)offset);
				getConvertTemp[0]=data[offset+0];
				getConvertTemp[1]=data[offset+1];
				getConvertTemp[2]=data[offset+2];
				getConvertTemp[3]=data[offset+3];
				return BitConverter.ToUInt32(getConvertTemp, 0);
			}

			getConvertTemp[0]=data[offset+3];
			getConvertTemp[1]=data[offset+2];
			getConvertTemp[2]=data[offset+1];
			getConvertTemp[3]=data[offset+0];
			return BitConverter.ToUInt32(getConvertTemp, 0);
		}

		float GetFloat(byte[] data, uint offset)
		{
			if(isIntel)
			{
				if(offset<=int.MaxValue) return BitConverter.ToSingle(data, (int)offset);
				getConvertTemp[0]=data[offset+0];
				getConvertTemp[1]=data[offset+1];
				getConvertTemp[2]=data[offset+2];
				getConvertTemp[3]=data[offset+3];
				return BitConverter.ToSingle(getConvertTemp, 0);
			}

			getConvertTemp[0]=data[offset+3];
			getConvertTemp[1]=data[offset+2];
			getConvertTemp[2]=data[offset+1];
			getConvertTemp[3]=data[offset+0];
			return BitConverter.ToSingle(getConvertTemp, 0);
		}

		double GetDouble(byte[] data, uint offset)
		{
			if(isIntel)
			{
				if(offset<=int.MaxValue) return BitConverter.ToDouble(data, (int)offset);
				getConvertTemp[0]=data[offset+0];
				getConvertTemp[1]=data[offset+1];
				getConvertTemp[2]=data[offset+2];
				getConvertTemp[3]=data[offset+3];
				getConvertTemp[4]=data[offset+4];
				getConvertTemp[5]=data[offset+5];
				getConvertTemp[6]=data[offset+6];
				getConvertTemp[7]=data[offset+7];
				return BitConverter.ToDouble(getConvertTemp, 0);
			}

			getConvertTemp[0]=data[offset+7];
			getConvertTemp[1]=data[offset+6];
			getConvertTemp[2]=data[offset+5];
			getConvertTemp[3]=data[offset+4];
			getConvertTemp[4]=data[offset+3];
			getConvertTemp[5]=data[offset+2];
			getConvertTemp[6]=data[offset+1];
			getConvertTemp[7]=data[offset+0];
			return BitConverter.ToDouble(getConvertTemp, 0);
		}

		RATIONAL GetRATIONAL(byte[] data, uint offset)
		{
			RATIONAL ret;
			ret.numerator=GetLONG(data, offset);
			ret.denominator=GetLONG(data, offset+4);
			return ret;
		}

		SRATIONAL GetSRATIONAL(byte[] data, uint offset)
		{
			SRATIONAL ret;
			ret.numerator=GetSLONG(data, offset);
			ret.denominator=GetSLONG(data, offset+4);
			return ret;
		}
		#endregion

		#region Set methods
		void Set(byte value, byte[] data, uint offset)
		{
			data[offset]=value;
		}

		void Set(sbyte value, byte[] data, uint offset)
		{
			data[offset]=(byte)value;
		}

		void Set(ushort value, byte[] data, uint offset)
		{
			byte[] setConvertTemp=BitConverter.GetBytes(value);
			data[offset+0]=setConvertTemp[0];
			data[offset+1]=setConvertTemp[1];
		}

		void Set(short value, byte[] data, uint offset)
		{
			byte[] setConvertTemp=BitConverter.GetBytes(value);
			data[offset+0]=setConvertTemp[0];
			data[offset+1]=setConvertTemp[1];
		}

		void Set(uint value, byte[] data, uint offset)
		{
			byte[] setConvertTemp=BitConverter.GetBytes(value);
			data[offset+0]=setConvertTemp[0];
			data[offset+1]=setConvertTemp[1];
			data[offset+2]=setConvertTemp[2];
			data[offset+3]=setConvertTemp[3];
		}

		void Set(int value, byte[] data, uint offset)
		{
			byte[] setConvertTemp=BitConverter.GetBytes(value);
			data[offset+0]=setConvertTemp[0];
			data[offset+1]=setConvertTemp[1];
			data[offset+2]=setConvertTemp[2];
			data[offset+3]=setConvertTemp[3];
		}

		void Set(float value, byte[] data, uint offset)
		{
			byte[] setConvertTemp=BitConverter.GetBytes(value);
			data[offset+0]=setConvertTemp[0];
			data[offset+1]=setConvertTemp[1];
			data[offset+2]=setConvertTemp[2];
			data[offset+3]=setConvertTemp[3];
		}

		void Set(double value, byte[] data, uint offset)
		{
			byte[] setConvertTemp=BitConverter.GetBytes(value);
			data[offset+0]=setConvertTemp[0];
			data[offset+1]=setConvertTemp[1];
			data[offset+2]=setConvertTemp[2];
			data[offset+3]=setConvertTemp[3];
			data[offset+4]=setConvertTemp[4];
			data[offset+5]=setConvertTemp[5];
			data[offset+6]=setConvertTemp[6];
			data[offset+7]=setConvertTemp[7];
		}

		void Set(RATIONAL value, byte[] data, uint offset)
		{
			Set(value.numerator, data, offset);
			Set(value.denominator, data, offset+4);
		}

		void Set(SRATIONAL value, byte[] data, uint offset)
		{
			Set(value.numerator, data, offset);
			Set(value.denominator, data, offset+4);
		}
		#endregion

		#region Misc methods
		static internal uint GetTypeSize(EXIF_TYPE t)
		{
			switch(t)
			{
				case EXIF_TYPE.BYTE:
				case EXIF_TYPE.ASCII:
				case EXIF_TYPE.SBYTE:
				case EXIF_TYPE.UNDEFINED:
					return 1;
				case EXIF_TYPE.SHORT:
				case EXIF_TYPE.SSHORT:
					return 2;
				case EXIF_TYPE.LONG:
				case EXIF_TYPE.SLONG:
				case EXIF_TYPE.FLOAT:
					return 4;
				case EXIF_TYPE.RATIONAL:
				case EXIF_TYPE.SRATIONAL:
				case EXIF_TYPE.DOUBLE:
					return 8;
				default: return 0;
			}
		}

		static DateTime GetDateTime(string str)
		{
			str.TrimEnd('\0');

			if(str.Length!=19) throw new ArgumentException("Not a DateTime string");
			if(str[4]!=':'||str[7]!=':'||str[10]!=' '||str[13]!=':'||str[16]!=':') throw new ArgumentException("Not a DateTime string");

			DateTime ret=new DateTime(int.Parse(str.Substring(0, 4)), int.Parse(str.Substring(5, 2)), int.Parse(str.Substring(8, 2)), int.Parse(str.Substring(11, 2)), int.Parse(str.Substring(14, 2)), int.Parse(str.Substring(17, 2)));
			return ret;
		}

		static DateTime GetDate(string str)
		{
			str.TrimEnd('\0');

			if(str.Length!=11) throw new ArgumentException("Not a Date string");
			if(str[4]!=':'||str[7]!=':') throw new ArgumentException("Not a Date string");

			DateTime ret=new DateTime(int.Parse(str.Substring(0, 4)), int.Parse(str.Substring(5, 2)), int.Parse(str.Substring(8, 2)));
			return ret;
		}

		static string ToSingleString(string[] strs)
		{
			StringBuilder ret=new StringBuilder();
			for(int i=0; i<strs.Length; i++)
			{
				if(i!=0) ret.Append('\0');
				if(strs[i]!=null) ret.Append(strs[i].TrimEnd('\0'));
			}

			return ret.ToString();
		}
		#endregion

		#region ParseToTable from buffer (byte[])
		void ParseToTable(Dictionary<ushort, IFDEntry> table, byte[] buffer, ushort tag, EXIF_TYPE type, uint count, uint valueOffset)
		{
			try
			{
				if(type==EXIF_TYPE.UNDEFINED||type==EXIF_TYPE.BYTE)
				{
					byte[] values=new byte[count];
					Array.Copy(buffer, valueOffset, values, 0, count);

					IFDEntry newEntry=new IFDEntry(tag, values, type==EXIF_TYPE.UNDEFINED);
					table.Add(tag, newEntry);
				}
				else if(type==EXIF_TYPE.ASCII&&count>1) // ignore string w/o characters
				{
					string str;
					if(valueOffset<=int.MaxValue) str=Encoding.ASCII.GetString(buffer, (int)valueOffset, (int)count-1).TrimEnd('\0');
					else
					{
						byte[] temp=new byte[count];
						Array.Copy(buffer, valueOffset, temp, 0, count);
						str=Encoding.ASCII.GetString(temp, 0, (int)count-1);
					}

					IFDEntry newEntry=new IFDEntry(tag, str);
					table.Add(tag, newEntry);
				}
				else if(type==EXIF_TYPE.SHORT)
				{
					ushort[] values=new ushort[count];
					for(uint a=0; a<count; a++) values[a]=GetSHORT(buffer, valueOffset+2*a);

					IFDEntry newEntry=new IFDEntry(tag, values);
					table.Add(tag, newEntry);
				}
				else if(type==EXIF_TYPE.LONG)
				{
					uint[] values=new uint[count];
					for(uint a=0; a<count; a++) values[a]=GetLONG(buffer, valueOffset+4*a);

					IFDEntry newEntry=new IFDEntry(tag, values);
					table.Add(tag, newEntry);
				}
				else if(type==EXIF_TYPE.RATIONAL)
				{
					RATIONAL[] values=new RATIONAL[count];
					for(uint a=0; a<count; a++) values[a]=GetRATIONAL(buffer, valueOffset+8*a);

					IFDEntry newEntry=new IFDEntry(tag, values);
					table.Add(tag, newEntry);
				}
				else if(type==EXIF_TYPE.SBYTE)
				{
					sbyte[] values=new sbyte[count];
					for(uint a=0; a<count; a++) values[a]=(sbyte)buffer[valueOffset+a];

					IFDEntry newEntry=new IFDEntry(tag, values);
					table.Add(tag, newEntry);
				}
				else if(type==EXIF_TYPE.SSHORT)
				{
					short[] values=new short[count];
					for(uint a=0; a<count; a++) values[a]=GetSSHORT(buffer, valueOffset+2*a);

					IFDEntry newEntry=new IFDEntry(tag, values);
					table.Add(tag, newEntry);
				}
				else if(type==EXIF_TYPE.SLONG)
				{
					int[] values=new int[count];
					for(uint a=0; a<count; a++) values[a]=GetSLONG(buffer, valueOffset+4*a);

					IFDEntry newEntry=new IFDEntry(tag, values);
					table.Add(tag, newEntry);
				}
				else if(type==EXIF_TYPE.SRATIONAL)
				{
					SRATIONAL[] values=new SRATIONAL[count];
					for(uint a=0; a<count; a++) values[a]=GetSRATIONAL(buffer, valueOffset+8*a);

					IFDEntry newEntry=new IFDEntry(tag, values);
					table.Add(tag, newEntry);
				}
				else if(type==EXIF_TYPE.FLOAT)
				{
					float[] values=new float[count];
					for(uint a=0; a<count; a++) values[a]=GetFloat(buffer, valueOffset+4*a);

					IFDEntry newEntry=new IFDEntry(tag, values);
					table.Add(tag, newEntry);
				}
				else if(type==EXIF_TYPE.DOUBLE)
				{
					double[] values=new double[count];
					for(uint a=0; a<count; a++) values[a]=GetDouble(buffer, valueOffset+8*a);

					IFDEntry newEntry=new IFDEntry(tag, values);
					table.Add(tag, newEntry);
				}
			}
#if DEBUG
			catch(Exception ex)
			{
				Debug.WriteLine(ex.ToString());
#else
			catch
			{
#endif
			}
		}
		#endregion

		#region Scan exif data from buffer (byte[])
		internal bool Scan(byte[] buffer, uint offset)
		{
			try
			{
				isIntel=false;
				if(buffer[offset]=='I'&&buffer[offset+1]=='I') isIntel=true;
				else if(buffer[offset]=='M'&&buffer[offset+1]=='M') isIntel=false;
				else return false;

				ushort number42=GetSHORT(buffer, offset+2);
				if(number42!=42) return false;

				uint IFD0Offset=GetLONG(buffer, offset+4);
				if(IFD0Offset==0) return false;
				IFD0Offset+=offset;

				uint ExifOffset=0;
				uint GPSOffset=0;
				uint InteropOffset=0;
				IFD0Table=new Dictionary<ushort,IFDEntry>();
				ExifTable=new Dictionary<ushort, IFDEntry>();
				GPSTable=new Dictionary<ushort, IFDEntry>();
				InteroperabilityTable=new Dictionary<ushort, IFDEntry>();

				#region Read IFD0
				uint tableOffset=IFD0Offset;
				int tableLength=GetSHORT(buffer, tableOffset);
				tableOffset+=2;

				for(int i=0; i<tableLength; i++, tableOffset+=12)
				{
					ushort tag=GetSHORT(buffer, tableOffset);
					EXIF_TYPE type=(EXIF_TYPE)GetSHORT(buffer, tableOffset+2);
					uint count=GetLONG(buffer, tableOffset+4);

					uint size=GetTypeSize(type)*count;
					if(size==0) continue; // unknown tag or no data(count==0)

					uint valueOffset=tableOffset+8;
					if(size>4)
					{
						valueOffset=GetLONG(buffer, tableOffset+8);
						if(valueOffset==0) return false;
						valueOffset+=offset;
					}

					bool found=false;

					try
					{
						if(count==1&&type!=EXIF_TYPE.ASCII)
						{
							if(type==EXIF_TYPE.RATIONAL)
							{
								switch(tag)
								{
									case 282: XResolution=GetRATIONAL(buffer, valueOffset); found=true; break;
									case 283: YResolution=GetRATIONAL(buffer, valueOffset); found=true; break;
								}
							}
							else if(type==EXIF_TYPE.LONG)
							{
								switch(tag)
								{
									case 256: ImageWidth=GetLONG(buffer, valueOffset); found=true; break;
									case 257: ImageLength=GetLONG(buffer, valueOffset); found=true; break;
									case 34665: ExifOffset=GetLONG(buffer, valueOffset); found=true; break;
									case 34853: GPSOffset=GetLONG(buffer, valueOffset); found=true; break;
								}
							}
							else if(type==EXIF_TYPE.SHORT)
							{
								switch(tag)
								{
									case 256: ImageWidth=GetSHORT(buffer, valueOffset); found=true; break;
									case 257: ImageLength=GetSHORT(buffer, valueOffset); found=true; break;
									case 259: Compression=GetSHORT(buffer, valueOffset); found=true; break;
									case 262: PhotometricInterpretation=GetSHORT(buffer, valueOffset); found=true; break;
									case 274: Orientation=GetSHORT(buffer, valueOffset); found=true; break;
									case 277: SamplesPerPixel=GetSHORT(buffer, valueOffset); found=true; break;
									case 284: PlanarConfiguration=GetSHORT(buffer, valueOffset); found=true; break;
									case 531: YCbCrPositioning=GetSHORT(buffer, valueOffset); found=true; break;
									case 296: ResolutionUnit=GetSHORT(buffer, valueOffset); found=true; break;
								}
							}
							else if(type==EXIF_TYPE.BYTE)
							{
								switch(tag)
								{
									case 40091: XPTitle=new byte[] { buffer[valueOffset] }; found=true; break;
									case 40092: XPComment=new byte[] { buffer[valueOffset] }; found=true; break;
									case 40093: XPAuthor=new byte[] { buffer[valueOffset] }; found=true; break;
									case 40094: XPKeywords=new byte[] { buffer[valueOffset] }; found=true; break;
									case 40095: XPSubject=new byte[] { buffer[valueOffset] }; found=true; break;
								}
							}
						}
						else
						{
							if(type==EXIF_TYPE.RATIONAL)
							{
								RATIONAL[] values=new RATIONAL[count];
								for(uint a=0; a<count; a++) values[a]=GetRATIONAL(buffer, valueOffset+8*a);

								switch(tag)
								{
									case 318: WhitePoint=values; found=true; break;
									case 319: PrimaryChromaticities=values; found=true; break;
									case 529: YCbCrCoefficients=values; found=true; break;
									case 532: ReferenceBlackWhite=values; found=true; break;
								}
							}
							else if(type==EXIF_TYPE.SHORT)
							{
								ushort[] values=new ushort[count];
								for(uint a=0; a<count; a++) values[a]=GetSHORT(buffer, valueOffset+2*a);
								
								switch(tag)
								{
									case 258: BitsPerSample=values; found=true; break;
									case 530: YCbCrSubSampling=values; found=true; break;
									case 301: TransferFunction=values; found=true; break;
								}
							}
							else if(type==EXIF_TYPE.ASCII&&count>1)
							{
								string str;
								if(valueOffset<=int.MaxValue) str=Encoding.ASCII.GetString(buffer, (int)valueOffset, (int)count-1).TrimEnd('\0');
								else
								{
									byte[] temp=new byte[count];
									Array.Copy(buffer, valueOffset, temp, 0, count);
									str=Encoding.ASCII.GetString(temp, 0, (int)count-1);
								}

								switch(tag)
								{
									case 306: DateTime=GetDateTime(str); found=true; break;
									case 270: ImageDescription=str; found=true; break;
									case 271: Make=str; found=true; break;
									case 272: Model=str; found=true; break;
									case 305: Software=str; found=true; break;
									case 315: Artist=str; found=true; break;
									case 33432: Copyright=str.Split('\0'); found=true; break;
								}
							}
							else if(type==EXIF_TYPE.BYTE)
							{
								byte[] values=new byte[count];
								Array.Copy(buffer, valueOffset, values, 0, count);

								switch(tag)
								{
									case 40091: XPTitle=values; found=true; break;
									case 40092: XPComment=values; found=true; break;
									case 40093: XPAuthor=values; found=true; break;
									case 40094: XPKeywords=values; found=true; break;
									case 40095: XPSubject=values; found=true; break;
								}
							}
						}
					}
#if DEBUG
					catch(Exception ex)
					{
						Debug.WriteLine(ex.ToString());
#else
					catch
					{
#endif
						found=false;
					}

					if(!found) ParseToTable(IFD0Table, buffer, tag, type, count, valueOffset);
				}
				#endregion

				#region Read Exif
				if(ExifOffset!=0)
				{
					tableOffset=ExifOffset+offset;
					tableLength=GetSHORT(buffer, tableOffset);
					tableOffset+=2;

					for(int i=0; i<tableLength; i++, tableOffset+=12)
					{
						ushort tag=GetSHORT(buffer, tableOffset);
						EXIF_TYPE type=(EXIF_TYPE)GetSHORT(buffer, tableOffset+2);
						uint count=GetLONG(buffer, tableOffset+4);

						uint size=GetTypeSize(type)*count;
						if(size==0) continue; // unknown tag or no data(count==0)

						uint valueOffset=tableOffset+8;
						if(size>4)
						{
							valueOffset=GetLONG(buffer, tableOffset+8);
							if(valueOffset==0) return false;
							valueOffset+=offset;
						}

						bool found=false;

						try
						{
							if(count==1&&type!=EXIF_TYPE.ASCII)
							{
								if(type==EXIF_TYPE.RATIONAL)
								{
									switch(tag)
									{
										case 37122: CompressedBitsPerPixel=GetRATIONAL(buffer, valueOffset); found=true; break;
										case 33434: ExposureTime=GetRATIONAL(buffer, valueOffset); found=true; break;
										case 33437: FNumber=GetRATIONAL(buffer, valueOffset); found=true; break;
										case 37378: ApertureValue=GetRATIONAL(buffer, valueOffset); found=true; break;
										case 37381: MaxApertureValue=GetRATIONAL(buffer, valueOffset); found=true; break;
										case 37382: SubjectDistance=GetRATIONAL(buffer, valueOffset); found=true; break;
										case 37386: FocalLength=GetRATIONAL(buffer, valueOffset); found=true; break;
										case 41483: FlashEnergy=GetRATIONAL(buffer, valueOffset); found=true; break;
										case 41486: FocalPlaneXResolution=GetRATIONAL(buffer, valueOffset); found=true; break;
										case 41487: FocalPlaneYResolution=GetRATIONAL(buffer, valueOffset); found=true; break;
										case 41493: ExposureIndex=GetRATIONAL(buffer, valueOffset); found=true; break;
										case 41988: DigitalZoomRatio=GetRATIONAL(buffer, valueOffset); found=true; break;
										case 42240: Gamma=GetRATIONAL(buffer, valueOffset); found=true; break;
									}
								}
								else if(type==EXIF_TYPE.SRATIONAL)
								{
									switch(tag)
									{
										case 37377: ShutterSpeedValue=GetSRATIONAL(buffer, valueOffset); found=true; break;
										case 37379: BrightnessValue=GetSRATIONAL(buffer, valueOffset); found=true; break;
										case 37380: ExposureBiasValue=GetSRATIONAL(buffer, valueOffset); found=true; break;
									}
								}
								else if(type==EXIF_TYPE.LONG)
								{
									switch(tag)
									{
										case 40962: PixelXDimension=GetLONG(buffer, valueOffset); found=true; break;
										case 40963: PixelYDimension=GetLONG(buffer, valueOffset); found=true; break;
										case 40965: InteropOffset=GetLONG(buffer, valueOffset); found=true; break;
									}
								}
								else if(type==EXIF_TYPE.SHORT)
								{
									switch(tag)
									{
										case 40961: ColorSpace=GetSHORT(buffer, valueOffset); found=true; break;
										case 40962: PixelXDimension=GetSHORT(buffer, valueOffset); found=true; break;
										case 40963: PixelYDimension=GetSHORT(buffer, valueOffset); found=true; break;
										case 34850: ExposureProgram=GetSHORT(buffer, valueOffset); found=true; break;
										case 34855: ISOSpeedRatings=new ushort[1] { GetSHORT(buffer, valueOffset) }; found=true; break;
										case 37383: MeteringMode=GetSHORT(buffer, valueOffset); found=true; break;
										case 37384: LightSource=GetSHORT(buffer, valueOffset); found=true; break;
										case 37385: Flash=GetSHORT(buffer, valueOffset); found=true; break;
										case 41488: FocalPlaneResolutionUnit=GetSHORT(buffer, valueOffset); found=true; break;
										case 41495: SensingMethode=GetSHORT(buffer, valueOffset); found=true; break;
										case 41985: CustomRendering=GetSHORT(buffer, valueOffset); found=true; break;
										case 41986: ExposureMode=GetSHORT(buffer, valueOffset); found=true; break;
										case 41987: WhiteBalance=GetSHORT(buffer, valueOffset); found=true; break;
										case 41989: FocalLengthIn35mmFilm=GetSHORT(buffer, valueOffset); found=true; break;
										case 41990: SceneCaptureType=GetSHORT(buffer, valueOffset); found=true; break;
										case 41991: GainControl=GetSHORT(buffer, valueOffset); found=true; break;
										case 41992: Contrast=GetSHORT(buffer, valueOffset); found=true; break;
										case 41993: Saturation=GetSHORT(buffer, valueOffset); found=true; break;
										case 41994: Sharpness=GetSHORT(buffer, valueOffset); found=true; break;
										case 41996: SubjectDistanceRange=GetSHORT(buffer, valueOffset); found=true; break;
										case 34859: SelfTimerMode=GetSHORT(buffer, valueOffset); found=true; break;
									}
								}
								else if(type==EXIF_TYPE.UNDEFINED||type==EXIF_TYPE.BYTE)
								{
									switch(tag)
									{
										case 41728: FileSource=buffer[valueOffset]; found=true; break;
										case 41729: SceneType=buffer[valueOffset]; found=true; break;
										case 34856: OECF=new byte[1] { buffer[valueOffset] }; found=true; break;
										case 41484: SpatialFrequencyResponse=new byte[1] { buffer[valueOffset] }; found=true; break;
										case 41730: CFAPattern=new byte[1] { buffer[valueOffset] }; found=true; break;
										case 41995: DeviceSettingDescription=new byte[1] { buffer[valueOffset] }; found=true; break;
										case 700: ApplicationNotes=new byte[1] { buffer[valueOffset] }; found=true; break;
										case 59932: Padding=new byte[1] { buffer[valueOffset] }; found=true; break;
									}
								}
								else if(type==EXIF_TYPE.SSHORT)
								{
									switch(tag)
									{
										case 34858: TimeZoneOffset=new short[1] { GetSSHORT(buffer, valueOffset) }; found=true; break;
									}
								}
								else if(type==EXIF_TYPE.SLONG)
								{
									switch(tag)
									{
										case 59933: OffsetSchema=GetSLONG(buffer, valueOffset); found=true; break;
									}
								}
							}
							else
							{
								if(type==EXIF_TYPE.SHORT)
								{
									ushort[] values=new ushort[count];
									for(uint a=0; a<count; a++) values[a]=GetSHORT(buffer, valueOffset+2*a);

									switch(tag)
									{
										case 34855: ISOSpeedRatings=values; found=true; break;
										case 37396: SubjectArea=values; found=true; break;
										case 41492: SubjectLocation=values; found=true; break;
									}
								}
								else if(type==EXIF_TYPE.UNDEFINED||type==EXIF_TYPE.BYTE)
								{
									byte[] values=new byte[count];
									Array.Copy(buffer, valueOffset, values, 0, count);

									switch(tag)
									{
										case 36864: ExifVersion=values; found=true; break;
										case 37121: ComponentsConfiguration=values; found=true; break;
										case 37500: MakerNote=values; found=true; break;
										case 37510: UserComment=values; found=true; break;
										case 34856: OECF=values; found=true; break;
										case 41484: SpatialFrequencyResponse=values; found=true; break;
										case 41730: CFAPattern=values; found=true; break;
										case 41995: DeviceSettingDescription=values; found=true; break;
										case 41728: FileSource=values[0]; found=true; break; // Sigma Digital Camera Bug
										case 700: ApplicationNotes=values; found=true; break;
										case 59932: Padding=values; found=true; break;
									}
								}
								else if(type==EXIF_TYPE.ASCII&&count>1)
								{
									string str;
									if(valueOffset<=int.MaxValue) str=Encoding.ASCII.GetString(buffer, (int)valueOffset, (int)count-1).TrimEnd('\0');
									else
									{
										byte[] temp=new byte[count];
										Array.Copy(buffer, valueOffset, temp, 0, count);
										str=Encoding.ASCII.GetString(temp, 0, (int)count-1);
									}

									switch(tag)
									{
										case 40964: RelatedSoundFile=str; found=true; break;
										case 36867: DateTimeOriginal=GetDateTime(str); found=true; break;
										case 36868: DateTimeDigitized=GetDateTime(str); found=true; break;
										case 37520: SubsecTime=str; found=true; break;
										case 37521: SubsecTimeOriginal=str; found=true; break;
										case 37522: SubsecTimeDigitized=str; found=true; break;
										case 34852: SpectralSensitivity=str; found=true; break;
										case 42016: ImageUniqueID=str; found=true; break;
										case 37394: SecurityClassification=str[0]; found=true; break; // should work even the classification is written out
										case 37395: ImageHistory=str; found=true; break;
										case 65000: PCROwnerName=str; found=true; break;
										case 65001: PCRSerialNumber=str; found=true; break;
										case 65002: PCRLens=str; found=true; break;
										case 65100: PCRRawFile=str; found=true; break;
										case 65101: PCRConverter=str; found=true; break;
										case 65102: PCRWhiteBalance=str; found=true; break;
										case 65105: PCRExposure=str; found=true; break;
										case 65106: PCRShadows=str; found=true; break;
										case 65107: PCRBrightness=str; found=true; break;
										case 65108: PCRContrast=str; found=true; break;
										case 65109: PCRSaturation=str; found=true; break;
										case 65110: PCRSharpness=str; found=true; break;
										case 65111: PCRSmoothness=str; found=true; break;
										case 65112: PCRMoireFilter=str; found=true; break;
									}
								}
								else if(type==EXIF_TYPE.SSHORT)
								{
									short[] values=new short[count];
									for(uint a=0; a<count; a++) values[a]=GetSSHORT(buffer, valueOffset+2*a);

									switch(tag)
									{
										case 34858: TimeZoneOffset=values; found=true; break;
									}
								}
							}
						}
#if DEBUG
						catch(Exception ex)
						{
							Debug.WriteLine(ex.ToString());
#else
						catch
						{
#endif
							found=false;
						}

						if(!found) ParseToTable(ExifTable, buffer, tag, type, count, valueOffset);
					}
				}
				#endregion

				#region Read GPS
				if(GPSOffset!=0)
				{
					tableOffset=GPSOffset+offset;
					tableLength=GetSHORT(buffer, tableOffset);
					tableOffset+=2;

					for(int i=0; i<tableLength; i++, tableOffset+=12)
					{
						ushort tag=GetSHORT(buffer, tableOffset);
						EXIF_TYPE type=(EXIF_TYPE)GetSHORT(buffer, tableOffset+2);
						uint count=GetLONG(buffer, tableOffset+4);

						uint size=GetTypeSize(type)*count;
						if(size==0) continue; // unknown tag or no data(count==0)

						uint valueOffset=tableOffset+8;
						if(size>4)
						{
							valueOffset=GetLONG(buffer, tableOffset+8);
							if(valueOffset==0) return false;
							valueOffset+=offset;
						}

						bool found=false;

						try
						{
							if(count==1&&type!=EXIF_TYPE.ASCII)
							{
								if(type==EXIF_TYPE.RATIONAL)
								{
									switch(tag)
									{
										case 6: GPSAltitude=GetRATIONAL(buffer, valueOffset); found=true; break;
										case 11: GPSDOP=GetRATIONAL(buffer, valueOffset); found=true; break;
										case 13: GPSSpeed=GetRATIONAL(buffer, valueOffset); found=true; break;
										case 15: GPSTrack=GetRATIONAL(buffer, valueOffset); found=true; break;
										case 17: GPSImgDirection=GetRATIONAL(buffer, valueOffset); found=true; break;
										case 24: GPSDestBearing=GetRATIONAL(buffer, valueOffset); found=true; break;
										case 26: GPSDestDistance=GetRATIONAL(buffer, valueOffset); found=true; break;
									}
								}
								else
								{
									switch(tag)
									{
										case 30: GPSDifferential=GetSHORT(buffer, valueOffset); found=true; break;
										case 5: GPSAltitudeRef=buffer[valueOffset]; found=true; break;
										case 27: GPSProcessingMethode=new byte[1] { buffer[valueOffset] }; found=true; break;
										case 28: GPSAreaInformation=new byte[1] { buffer[valueOffset] }; found=true; break;
									}
								}
							}
							else
							{
								if(type==EXIF_TYPE.RATIONAL)
								{
									RATIONAL[] values=new RATIONAL[count];
									for(uint a=0; a<count; a++) values[a]=GetRATIONAL(buffer, valueOffset+8*a);

									switch(tag)
									{
										case 2: GPSLatitude=values; found=true; break;
										case 4: GPSLongitude=values; found=true; break;
										case 7: GPSTimeStamp=values; found=true; break;
										case 20: GPSDestLatitude=values; found=true; break;
										case 22: GPSDestLongitude=values; found=true; break;
									}
								}
								else if(type==EXIF_TYPE.UNDEFINED||type==EXIF_TYPE.BYTE)
								{
									byte[] values=new byte[count];
									Array.Copy(buffer, valueOffset, values, 0, count);

									switch(tag)
									{
										case 0: GPSVersionID=values; found=true; break;
										case 27: GPSProcessingMethode=values; found=true; break;
										case 28: GPSAreaInformation=values; found=true; break;
									}
								}
								else if(type==EXIF_TYPE.ASCII&&count>1)
								{
									string str;
									if(valueOffset<=int.MaxValue) str=Encoding.ASCII.GetString(buffer, (int)valueOffset, (int)count-1).TrimEnd('\0');
									else
									{
										byte[] temp=new byte[count];
										Array.Copy(buffer, valueOffset, temp, 0, count);
										str=Encoding.ASCII.GetString(temp, 0, (int)count-1);
									}

									if(str.Length==1)
									{
										switch(tag)
										{
											case 1: GPSLatitudeRef=str[0]; found=true; break;
											case 3: GPSLongitudeRef=str[0]; found=true; break;
											case 8: GPSSatellites=str; found=true; break;
											case 9: GPSStatus=str[0]; found=true; break;
											case 10: GPSMeasureMode=str[0]; found=true; break;
											case 12: GPSSpeedRef=str[0]; found=true; break;
											case 14: GPSTrackRef=str[0]; found=true; break;
											case 16: GPSImgDirectionRef=str[0]; found=true; break;
											case 19: GPSDestLatitudeRef=str[0]; found=true; break;
											case 21: GPSDestLongitudeRef=str[0]; found=true; break;
											case 23: GPSDestBearingRef=str[0]; found=true; break;
											case 25: GPSDestDistanceRef=str[0]; found=true; break;
										}
									}
									else
									{
										switch(tag)
										{
											case 8: GPSSatellites=str; found=true; break;
											case 18: GPSMapDatum=str; found=true; break;
											case 29: GPSDateStamp=GetDate(str); found=true; break;
										}
									}
								}
							}
						}
#if DEBUG
						catch(Exception ex)
						{
							Debug.WriteLine(ex.ToString());
#else
						catch
						{
#endif
							found=false;
						}

						if(!found) ParseToTable(GPSTable, buffer, tag, type, count, valueOffset);
					}
				}
				#endregion

				#region Read Interoperability
				if(InteropOffset!=0)
				{
					tableOffset=InteropOffset+offset;
					tableLength=GetSHORT(buffer, tableOffset);
					tableOffset+=2;

					for(int i=0; i<tableLength; i++, tableOffset+=12)
					{
						ushort tag=GetSHORT(buffer, tableOffset);
						EXIF_TYPE type=(EXIF_TYPE)GetSHORT(buffer, tableOffset+2);
						uint count=GetLONG(buffer, tableOffset+4);

						uint size=GetTypeSize(type)*count;
						if(size==0) continue; // unknown tag or no data(count==0)

						uint valueOffset=tableOffset+8;
						if(size>4)
						{
							valueOffset=GetLONG(buffer, tableOffset+8);
							if(valueOffset==0) return false;
							valueOffset+=offset;
						}

						bool found=false;

						try
						{
							if(count==1&&type!=EXIF_TYPE.ASCII)
							{
								if(type==EXIF_TYPE.SHORT)
								{
									switch(tag)
									{
										case 4097: RelatedImageWidth=GetSHORT(buffer, valueOffset); found=true; break;
										case 4098: RelatedImageLength=GetSHORT(buffer, valueOffset); found=true; break;
									}
								}
								else if(type==EXIF_TYPE.LONG)
								{
									switch(tag)
									{
										case 4097: RelatedImageWidth=GetLONG(buffer, valueOffset); found=true; break;
										case 4098: RelatedImageLength=GetLONG(buffer, valueOffset); found=true; break;
									}
								}
							}
							else
							{
								if(type==EXIF_TYPE.UNDEFINED)
								{
									byte[] values=new byte[count];
									Array.Copy(buffer, valueOffset, values, 0, count);

									if(tag==2)
									{
										InteroperabilityVersion=values;
										found=true;
									}
								}
								else if(type==EXIF_TYPE.ASCII&&count>1)
								{
									string str;
									if(valueOffset<=int.MaxValue) str=Encoding.ASCII.GetString(buffer, (int)valueOffset, (int)count-1).TrimEnd('\0');
									else
									{
										byte[] temp=new byte[count];
										Array.Copy(buffer, valueOffset, temp, 0, count);
										str=Encoding.ASCII.GetString(temp, 0, (int)count-1);
									}

									switch(tag)
									{
										case 1: InteroperabilityIndex=str; found=true; break;
										case 4096: RelatedImageFileFormat=str; found=true; break;
									}
								}
							}
						}
#if DEBUG
						catch(Exception ex)
						{
							Debug.WriteLine(ex.ToString());
#else
						catch
						{
#endif
							found=false;
						}

						if(!found) ParseToTable(InteroperabilityTable, buffer, tag, type, count, valueOffset);
					}
				}
				#endregion

				return true;
			}
#if DEBUG
			catch(Exception ex)
			{
				Debug.WriteLine(ex.ToString());
#else
			catch
			{
#endif
				return false;
			}
		}
		#endregion

		#region WriteIFDTable to buffer (byte[])
		void WriteIFDTable(byte[] buffer, List<IFDEntry> table, uint offset, uint valueOffset)
		{
			Set((ushort)table.Count, buffer, offset);
			offset+=2; // count

			uint tmpValueOffset=valueOffset;

			#region IFD Table
			foreach(IFDEntry entry in table)
			{
				Set(entry.Tag, buffer, offset);
				Set((ushort)entry.Type, buffer, offset+2);

				if(entry.Type==EXIF_TYPE.ASCII)
				{
					string str=entry.Data as string;
					Set((uint)str.Length+1, buffer, offset+4);
				}
				else
				{
					Array ar=entry.Data as Array;
					Set((uint)ar.Length, buffer, offset+4);
				}

				if(entry.DataSize==0)
				{
					switch(entry.Type)
					{
						case EXIF_TYPE.ASCII: // max. 3 chars
							{
								string str=entry.Data as string;
								str+='\0';
								uint coffset=offset+8;
								foreach(char c in str) Set((byte)c, buffer, coffset++);
							}
							break;
						case EXIF_TYPE.UNDEFINED:
						case EXIF_TYPE.BYTE: // max. 4 BYTE or 4 UNDEFINED
							{
								byte[] ar=entry.Data as byte[];
								uint boffset=offset+8;
								foreach(byte b in ar) Set(b, buffer, boffset++);
							}
							break;
						case EXIF_TYPE.FLOAT:// max. 1 FLOAT
							{
								float[] ar=entry.Data as float[];
								Set(ar[0], buffer, offset+8);
							}
							break;
						case EXIF_TYPE.LONG: // max. 1 LONG
							{
								uint[] ar=entry.Data as uint[];
								Set(ar[0], buffer, offset+8);
							}
							break;
						case EXIF_TYPE.SBYTE: // max. 4 SBYTE
							{
								sbyte[] ar=entry.Data as sbyte[];
								uint sboffset=offset+8;
								foreach(sbyte sb in ar) Set(sb, buffer, sboffset++);
							}
							break;
						case EXIF_TYPE.SHORT: // max. 2 SHORT
							{
								ushort[] ar=entry.Data as ushort[];
								uint usoffset=offset+8;
								foreach(ushort us in ar) { Set(us, buffer, usoffset); usoffset+=2; }
							}
							break;
						case EXIF_TYPE.SLONG: // max. 1 SLONG
							{
								int[] ar=entry.Data as int[];
								Set(ar[0], buffer, offset+8);
							}
							break;
						case EXIF_TYPE.SSHORT: // max. 2 SSHORT
							{
								short[] ar=entry.Data as short[];
								uint soffset=offset+8;
								foreach(short s in ar) { Set(s, buffer, soffset); soffset+=2; }
							}
							break;
					}
				}
				else
				{
					Set(tmpValueOffset, buffer, offset+8);
					tmpValueOffset+=entry.DataSize;
				}
				offset+=12;
			}
			#endregion

			Set((uint)0, buffer, offset); // next IFD

			#region IFD Data
			offset=valueOffset; 
			
			foreach(IFDEntry entry in table)
			{
				if(entry.DataSize!=0)
				{
					uint coffset=offset;
					switch(entry.Type)
					{
						case EXIF_TYPE.ASCII:
							{
								string str=entry.Data as string;
								str+='\0';
								foreach(char c in str) Set((byte)c, buffer, coffset++);
							}
							break;
						case EXIF_TYPE.UNDEFINED:
						case EXIF_TYPE.BYTE:
							{
								byte[] ar=entry.Data as byte[];
								foreach(byte c in ar) Set(c, buffer, coffset++);
							}
							break;
						case EXIF_TYPE.SHORT:
							{
								ushort[] ar=entry.Data as ushort[];
								foreach(ushort c in ar) { Set(c, buffer, coffset); coffset+=2; }
							}
							break;
						case EXIF_TYPE.LONG:
							{
								uint[] ar=entry.Data as uint[];
								foreach(uint c in ar) { Set(c, buffer, coffset); coffset+=4; }
							}
							break;
						case EXIF_TYPE.RATIONAL:
							{
								RATIONAL[] ar=entry.Data as RATIONAL[];
								foreach(RATIONAL c in ar) { Set(c, buffer, coffset); coffset+=8; }
							}
							break;
						case EXIF_TYPE.SBYTE:
							{
								sbyte[] ar=entry.Data as sbyte[];
								foreach(sbyte c in ar) Set(c, buffer, coffset++);
							}
							break;
						case EXIF_TYPE.SSHORT:
							{
								short[] ar=entry.Data as short[];
								foreach(short c in ar) { Set(c, buffer, coffset); coffset+=2; }
							}
							break;
						case EXIF_TYPE.SLONG:
							{
								int[] ar=entry.Data as int[];
								foreach(int c in ar) { Set(c, buffer, coffset); coffset+=4; }
							}
							break;
						case EXIF_TYPE.SRATIONAL:
							{
								SRATIONAL[] ar=entry.Data as SRATIONAL[];
								foreach(SRATIONAL c in ar) { Set(c, buffer, coffset); coffset+=8; }
							}
							break;
						case EXIF_TYPE.FLOAT:
							{
								float[] ar=entry.Data as float[];
								foreach(float c in ar) { Set(c, buffer, coffset); coffset+=4; }
							}
							break;
						case EXIF_TYPE.DOUBLE:
							{
								double[] ar=entry.Data as double[];
								foreach(double c in ar) { Set(c, buffer, coffset); coffset+=8; }
							}
							break;
					}
					offset+=entry.DataSize;
				}
			}
			#endregion
		}
		#endregion

		#region Generate exif data to buffer (byte[])
		internal byte[] Generate()
		{
			#region IF0
			Dictionary<ushort, IFDEntry> iIFD0Table=new Dictionary<ushort, IFDEntry>();
			if(imageWidthState==EXIF_TAG_STATE.ReadWrite) iIFD0Table.Add(256, new IFDEntry(256, imageWidth));
			if(imageLengthState==EXIF_TAG_STATE.ReadWrite) iIFD0Table.Add(257, new IFDEntry(257, imageLength));
			if(bitsPerSampleState==EXIF_TAG_STATE.ReadWrite) iIFD0Table.Add(258, new IFDEntry(258, bitsPerSample));
			if(compressionState==EXIF_TAG_STATE.ReadWrite) iIFD0Table.Add(259, new IFDEntry(259, compression));
			if(photometricInterpretationState==EXIF_TAG_STATE.ReadWrite) iIFD0Table.Add(262, new IFDEntry(262, photometricInterpretation));
			if(orientationState==EXIF_TAG_STATE.ReadWrite) iIFD0Table.Add(274, new IFDEntry(274, orientation));
			if(samplesPerPixelState==EXIF_TAG_STATE.ReadWrite) iIFD0Table.Add(277, new IFDEntry(277, samplesPerPixel));
			if(planarConfigurationState==EXIF_TAG_STATE.ReadWrite) iIFD0Table.Add(284, new IFDEntry(284, planarConfiguration));
			if(yCbCrSubSamplingState==EXIF_TAG_STATE.ReadWrite) iIFD0Table.Add(530, new IFDEntry(530, yCbCrSubSampling));
			if(yCbCrPositioningState==EXIF_TAG_STATE.ReadWrite) iIFD0Table.Add(531, new IFDEntry(531, yCbCrPositioning));
			if(xResolutionState==EXIF_TAG_STATE.ReadWrite) iIFD0Table.Add(282, new IFDEntry(282, xResolution));
			if(yResolutionState==EXIF_TAG_STATE.ReadWrite) iIFD0Table.Add(283, new IFDEntry(283, YResolution));
			if(resolutionUnitState==EXIF_TAG_STATE.ReadWrite) iIFD0Table.Add(296, new IFDEntry(296, resolutionUnit));
			if(transferFunctionState==EXIF_TAG_STATE.ReadWrite) iIFD0Table.Add(301, new IFDEntry(301, transferFunction));
			if(whitePointState==EXIF_TAG_STATE.ReadWrite) iIFD0Table.Add(318, new IFDEntry(318, whitePoint));
			if(primaryChromaticitiesState==EXIF_TAG_STATE.ReadWrite) iIFD0Table.Add(319, new IFDEntry(319, primaryChromaticities));
			if(yCbCrCoefficientsState==EXIF_TAG_STATE.ReadWrite) iIFD0Table.Add(529, new IFDEntry(529, yCbCrCoefficients));
			if(referenceBlackWhiteState==EXIF_TAG_STATE.ReadWrite) iIFD0Table.Add(532, new IFDEntry(532, referenceBlackWhite));
			if(dateTimeState==EXIF_TAG_STATE.ReadWrite) iIFD0Table.Add(306, new IFDEntry(306, dateTime.ToString("yyyy:MM:dd HH:mm:ss")));
			if(imageDescriptionState==EXIF_TAG_STATE.ReadWrite) iIFD0Table.Add(270, new IFDEntry(270, imageDescription));
			if(makeState==EXIF_TAG_STATE.ReadWrite) iIFD0Table.Add(271, new IFDEntry(271, make));
			if(modelState==EXIF_TAG_STATE.ReadWrite) iIFD0Table.Add(272, new IFDEntry(272, model));
			if(softwareState==EXIF_TAG_STATE.ReadWrite) iIFD0Table.Add(305, new IFDEntry(305, software));
			if(artistState==EXIF_TAG_STATE.ReadWrite) iIFD0Table.Add(315, new IFDEntry(315, artist));
			if(copyrightState==EXIF_TAG_STATE.ReadWrite) iIFD0Table.Add(33432, new IFDEntry(33432, ToSingleString(copyright)));
			if(xpTitleState==EXIF_TAG_STATE.ReadWrite) iIFD0Table.Add(40091, new IFDEntry(40091, xpTitle, false));
			if(xpCommentState==EXIF_TAG_STATE.ReadWrite) iIFD0Table.Add(40092, new IFDEntry(40092, xpComment, false));
			if(xpAuthorState==EXIF_TAG_STATE.ReadWrite) iIFD0Table.Add(40093, new IFDEntry(40093, xpAuthor, false));
			if(xpKeywordsState==EXIF_TAG_STATE.ReadWrite) iIFD0Table.Add(40094, new IFDEntry(40094, xpKeywords, false));
			if(xpSubjectState==EXIF_TAG_STATE.ReadWrite) iIFD0Table.Add(40095, new IFDEntry(40095, xpSubject, false));

			foreach(ushort tag in IFD0Table.Keys) if(!iIFD0Table.ContainsKey(tag)) iIFD0Table.Add(tag, IFD0Table[tag]);
			#endregion

			#region Exif
			Dictionary<ushort, IFDEntry> iExifTable=new Dictionary<ushort, IFDEntry>();
			if(exifVersionState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(36864, new IFDEntry(36864, exifVersion, true));
			if(colorSpaceState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(40961, new IFDEntry(40961, colorSpace));
			if(componentsConfigurationState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(37121, new IFDEntry(37121, componentsConfiguration, true));
			if(compressedBitsPerPixelState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(37122, new IFDEntry(37122, compressedBitsPerPixel));
			if(pixelXDimensionState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(40962, new IFDEntry(40962, pixelXDimension));
			if(pixelYDimensionState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(40963, new IFDEntry(40963, pixelYDimension));
			if(makerNoteState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(37500, new IFDEntry(37500, makerNote, true));
			if(userCommentState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(37510, new IFDEntry(37510, userComment, true));
			if(relatedSoundFileState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(40964, new IFDEntry(40964, relatedSoundFile));
			if(dateTimeOriginalState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(36867, new IFDEntry(36867, dateTimeOriginal.ToString("yyyy:MM:dd HH:mm:ss")));
			if(dateTimeDigitizedState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(36868, new IFDEntry(36868, dateTimeDigitized.ToString("yyyy:MM:dd HH:mm:ss")));
			if(subsecTimeState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(37520, new IFDEntry(37520, subsecTime));
			if(subsecTimeOriginalState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(37521, new IFDEntry(37521, subsecTimeOriginal));
			if(subsecTimeDigitizedState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(37522, new IFDEntry(37522, subsecTimeDigitized));

			if(exposureTimeState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(33434, new IFDEntry(33434, exposureTime));
			if(fNumberState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(33437, new IFDEntry(33437, fNumber));
			if(exposureProgramState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(34850, new IFDEntry(34850, exposureProgram));
			if(spectralSensitivityState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(34852, new IFDEntry(34852, spectralSensitivity));
			if(isoSpeedRatingsState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(34855, new IFDEntry(34855, isoSpeedRatings));
			if(oecfState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(34856, new IFDEntry(34856, oecf, true));
			if(shutterSpeedValueState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(37377, new IFDEntry(37377, shutterSpeedValue));
			if(apertureValueState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(37378, new IFDEntry(37378, apertureValue));
			if(brightnessValueState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(37379, new IFDEntry(37379, brightnessValue));
			if(exposureBiasValueState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(37380, new IFDEntry(37380, exposureBiasValue));
			if(maxApertureValueState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(37381, new IFDEntry(37381, maxApertureValue));
			if(subjectDistanceState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(37382, new IFDEntry(37382, subjectDistance));
			if(meteringModeState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(37383, new IFDEntry(37383, meteringMode));
			if(lightSourceState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(37384, new IFDEntry(37384, lightSource));
			if(flashState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(37385, new IFDEntry(37385, flash));
			if(focalLengthState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(37386, new IFDEntry(37386, focalLength));
			if(subjectAreaState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(37396, new IFDEntry(37396, subjectArea));
			if(flashEnergyState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(41483, new IFDEntry(41483, flashEnergy));
			if(spatialFrequencyResponseState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(41484, new IFDEntry(41484, spatialFrequencyResponse, true));
			if(focalPlaneXResolutionState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(41486, new IFDEntry(41486, focalPlaneXResolution));
			if(focalPlaneYResolutionState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(41487, new IFDEntry(41487, focalPlaneYResolution));
			if(focalPlaneResolutionUnitState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(41488, new IFDEntry(41488, focalPlaneResolutionUnit));
			if(subjectLocationState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(41492, new IFDEntry(41492, subjectLocation));
			if(exposureIndexState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(41493, new IFDEntry(41493, exposureIndex));
			if(sensingMethodeState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(41495, new IFDEntry(41495, sensingMethode));
			if(fileSourceState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(41728, new IFDEntry(41728, fileSource, true));
			if(sceneTypeState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(41729, new IFDEntry(41729, sceneType, true));
			if(cfaPatternState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(41730, new IFDEntry(41730, cfaPattern, true));
			if(customRenderingState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(41985, new IFDEntry(41985, customRendering));
			if(exposureModeState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(41986, new IFDEntry(41986, exposureMode));
			if(whiteBalanceState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(41987, new IFDEntry(41987, whiteBalance));
			if(digitalZoomRatioState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(41988, new IFDEntry(41988, digitalZoomRatio));
			if(focalLengthIn35mmFilmState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(41989, new IFDEntry(41989, focalLengthIn35mmFilm));
			if(sceneCaptureTypeState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(41990, new IFDEntry(41990, sceneCaptureType));
			if(gainControlState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(41991, new IFDEntry(41991, gainControl));
			if(contrastState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(41992, new IFDEntry(41992, contrast));
			if(saturationState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(41993, new IFDEntry(41993, saturation));
			if(sharpnessState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(41994, new IFDEntry(41994, sharpness));
			if(deviceSettingDescriptionState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(41995, new IFDEntry(41995, deviceSettingDescription, true));
			if(subjectDistanceRangeState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(41996, new IFDEntry(41996, subjectDistanceRange));

			if(imageUniqueIDState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(42016, new IFDEntry(42016, imageUniqueID));

			if(applicationNotesState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(700, new IFDEntry(700, applicationNotes, false));
			if(timeZoneOffsetState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(34858, new IFDEntry(34858, timeZoneOffset));
			if(selfTimerModeState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(34859, new IFDEntry(34859, selfTimerMode));
			if(securityClassificationState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(37394, new IFDEntry(37394, securityClassification));
			if(imageHistoryState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(37395, new IFDEntry(37395, imageHistory));
			if(gammaState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(42240, new IFDEntry(42240, gamma));
			if(paddingState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(59932, new IFDEntry(59932, padding, true));
			if(offsetSchemaState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(59933, new IFDEntry(59933, offsetSchema));

			// Photoshop Camera RAW
			if(pcrOwnerNameState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(65000, new IFDEntry(65000, pcrOwnerName));
			if(pcrSerialNumberState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(65001, new IFDEntry(65001, pcrSerialNumber));
			if(pcrLensState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(65002, new IFDEntry(65002, pcrLens));
			if(pcrRawFileState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(65100, new IFDEntry(65100, pcrRawFile));
			if(pcrConverterState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(65101, new IFDEntry(65101, pcrConverter));
			if(pcrWhiteBalanceState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(65102, new IFDEntry(65102, pcrWhiteBalance));
			if(pcrExposureState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(65105, new IFDEntry(65105, pcrExposure));
			if(pcrShadowsState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(65106, new IFDEntry(65106, pcrShadows));
			if(pcrBrightnessState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(65107, new IFDEntry(65107, pcrBrightness));
			if(pcrContrastState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(65108, new IFDEntry(65108, pcrContrast));
			if(pcrSaturationState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(65109, new IFDEntry(65109, pcrSaturation));
			if(pcrSharpnessState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(65110, new IFDEntry(65110, pcrSharpness));
			if(pcrSmoothnessState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(65111, new IFDEntry(65111, pcrSmoothness));
			if(pcrMoireFilterState==EXIF_TAG_STATE.ReadWrite) iExifTable.Add(65112, new IFDEntry(65112, pcrMoireFilter));

			foreach(ushort tag in ExifTable.Keys) if(!iExifTable.ContainsKey(tag)) iExifTable.Add(tag, ExifTable[tag]);
			#endregion

			#region GPS
			Dictionary<ushort, IFDEntry> iGPSTable=new Dictionary<ushort, IFDEntry>();
			if(gpsVersionIDState==EXIF_TAG_STATE.ReadWrite) iGPSTable.Add(0, new IFDEntry(0, gpsVersionID, false));
			if(gpsLatitudeState==EXIF_TAG_STATE.ReadWrite) iGPSTable.Add(1, new IFDEntry(1, gpsLatitude));
			if(gpsLatitudeRefState==EXIF_TAG_STATE.ReadWrite) iGPSTable.Add(2, new IFDEntry(2, gpsLatitudeRef));
			if(gpsLongitudeState==EXIF_TAG_STATE.ReadWrite) iGPSTable.Add(3, new IFDEntry(3, gpsLongitude));
			if(gpsLongitudeRefState==EXIF_TAG_STATE.ReadWrite) iGPSTable.Add(4, new IFDEntry(4, gpsLongitudeRef));
			if(gpsAltitudeState==EXIF_TAG_STATE.ReadWrite) iGPSTable.Add(5, new IFDEntry(5, gpsAltitude));
			if(gpsAltitudeRefState==EXIF_TAG_STATE.ReadWrite) iGPSTable.Add(6, new IFDEntry(6, gpsAltitudeRef, false));
			if(gpsTimeStampState==EXIF_TAG_STATE.ReadWrite) iGPSTable.Add(7, new IFDEntry(7, gpsTimeStamp));
			if(gpsSatellitesState==EXIF_TAG_STATE.ReadWrite) iGPSTable.Add(8, new IFDEntry(8, gpsSatellites));
			if(gpsStatusState==EXIF_TAG_STATE.ReadWrite) iGPSTable.Add(9, new IFDEntry(9, gpsStatus));
			if(gpsMeasureModeState==EXIF_TAG_STATE.ReadWrite) iGPSTable.Add(10, new IFDEntry(10, gpsMeasureMode));
			if(gpsDOPState==EXIF_TAG_STATE.ReadWrite) iGPSTable.Add(11, new IFDEntry(11, gpsDOP));
			if(gpsSpeedRefState==EXIF_TAG_STATE.ReadWrite) iGPSTable.Add(12, new IFDEntry(12, gpsSpeedRef));
			if(gpsSpeedState==EXIF_TAG_STATE.ReadWrite) iGPSTable.Add(13, new IFDEntry(13, gpsSpeed));
			if(gpsTrackRefState==EXIF_TAG_STATE.ReadWrite) iGPSTable.Add(14, new IFDEntry(14, gpsTrackRef));
			if(gpsTrackState==EXIF_TAG_STATE.ReadWrite) iGPSTable.Add(15, new IFDEntry(15, gpsTrack));
			if(gpsImgDirectionRefState==EXIF_TAG_STATE.ReadWrite) iGPSTable.Add(16, new IFDEntry(16, gpsImgDirectionRef));
			if(gpsImgDirectionState==EXIF_TAG_STATE.ReadWrite) iGPSTable.Add(17, new IFDEntry(17, gpsImgDirection));
			if(gpsMapDatumState==EXIF_TAG_STATE.ReadWrite) iGPSTable.Add(18, new IFDEntry(18, gpsMapDatum));
			if(gpsDestLatitudeRefState==EXIF_TAG_STATE.ReadWrite) iGPSTable.Add(19, new IFDEntry(19, gpsDestLatitudeRef));
			if(gpsDestLatitudeState==EXIF_TAG_STATE.ReadWrite) iGPSTable.Add(20, new IFDEntry(20, gpsDestLatitude));
			if(gpsDestLongitudeRefState==EXIF_TAG_STATE.ReadWrite) iGPSTable.Add(21, new IFDEntry(21, gpsDestLongitudeRef));
			if(gpsDestLongitudeState==EXIF_TAG_STATE.ReadWrite) iGPSTable.Add(22, new IFDEntry(22, gpsDestLongitude));
			if(gpsDestBearingRefState==EXIF_TAG_STATE.ReadWrite) iGPSTable.Add(23, new IFDEntry(23, gpsDestBearingRef));
			if(gpsDestBearingState==EXIF_TAG_STATE.ReadWrite) iGPSTable.Add(24, new IFDEntry(24, gpsDestBearing));
			if(gpsDestDistanceRefState==EXIF_TAG_STATE.ReadWrite) iGPSTable.Add(25, new IFDEntry(25, gpsDestDistanceRef));
			if(gpsDestDistanceState==EXIF_TAG_STATE.ReadWrite) iGPSTable.Add(26, new IFDEntry(26, gpsDestDistance));
			if(gpsProcessingMethodeState==EXIF_TAG_STATE.ReadWrite) iGPSTable.Add(27, new IFDEntry(27, gpsProcessingMethode, true));
			if(gpsAreaInformationState==EXIF_TAG_STATE.ReadWrite) iGPSTable.Add(28, new IFDEntry(28, gpsAreaInformation, true));
			if(gpsDateStampState==EXIF_TAG_STATE.ReadWrite) iGPSTable.Add(29, new IFDEntry(29, gpsDateStamp.ToString("yyyy:MM:dd")));
			if(gpsDifferentialState==EXIF_TAG_STATE.ReadWrite) iGPSTable.Add(30, new IFDEntry(30, gpsDifferential));

			foreach(ushort tag in GPSTable.Keys) if(!iGPSTable.ContainsKey(tag)) iGPSTable.Add(tag, GPSTable[tag]);
			#endregion

			#region Interoperability
			Dictionary<ushort, IFDEntry> iInteroperabilityTable=new Dictionary<ushort, IFDEntry>();
			if(interoperabilityIndexState==EXIF_TAG_STATE.ReadWrite) iInteroperabilityTable.Add(1, new IFDEntry(1, interoperabilityIndex));

			// DCF
			if(interoperabilityVersionState==EXIF_TAG_STATE.ReadWrite) iInteroperabilityTable.Add(2, new IFDEntry(2, interoperabilityVersion, true));
			if(relatedImageFileFormatState==EXIF_TAG_STATE.ReadWrite) iInteroperabilityTable.Add(4096, new IFDEntry(4096, relatedImageFileFormat));
			if(relatedImageWidthState==EXIF_TAG_STATE.ReadWrite) iInteroperabilityTable.Add(4097, new IFDEntry(4097, relatedImageWidth));
			if(relatedImageLengthState==EXIF_TAG_STATE.ReadWrite) iInteroperabilityTable.Add(4098, new IFDEntry(4098, relatedImageLength));

			foreach(ushort tag in InteroperabilityTable.Keys) if(!iInteroperabilityTable.ContainsKey(tag)) iInteroperabilityTable.Add(tag, InteroperabilityTable[tag]);
			#endregion

			#region Add/remove table offset to/from tables
			if(iInteroperabilityTable.Count==0&&iExifTable.ContainsKey(40965)) iExifTable.Remove(40965);
			else if(iInteroperabilityTable.Count!=0&&!iExifTable.ContainsKey(40965)) iExifTable.Add(40965, new IFDEntry(40965, (uint)0));
			else iExifTable[40965]=new IFDEntry(40965, (uint)0);

			if(iGPSTable.Count==0&&iIFD0Table.ContainsKey(34853)) iIFD0Table.Remove(34853);
			else if(iGPSTable.Count!=0&&!iIFD0Table.ContainsKey(34853)) iIFD0Table.Add(34853, new IFDEntry(34853, (uint)0));
			else iIFD0Table[34853]=new IFDEntry(34853, (uint)0);

			if(iExifTable.Count==0&&iIFD0Table.ContainsKey(34665)) iIFD0Table.Remove(34665);
			else if(iExifTable.Count!=0&&!iIFD0Table.ContainsKey(34665)) iIFD0Table.Add(34665, new IFDEntry(34665, (uint)0));
			else iIFD0Table[34665]=new IFDEntry(34665, (uint)0);
			#endregion

			if(iIFD0Table.Count==0) return null;

			List<IFDEntry> lIFD0Table=new List<IFDEntry>();
			List<IFDEntry> lExifTable=new List<IFDEntry>();
			List<IFDEntry> lGPSTable=new List<IFDEntry>();
			List<IFDEntry> lInteropTable=new List<IFDEntry>();

			#region Calculated resulting size
			uint size=8; // TIFF header
			uint ifd0Offset=size;

			size+=6; // IFD0 header and footer
			size+=12*(uint)iIFD0Table.Count; // Tags
			uint ifd0ValueOffset=size;
			foreach(IFDEntry entry in iIFD0Table.Values) size+=entry.DataSize;

			uint exifOffset=size;
			uint exifValueOffset=size;
			if(iExifTable.Count>0)
			{
				size+=6; // Exif header and footer
				size+=12*(uint)iExifTable.Count; // Tags
				exifValueOffset=size;
				foreach(IFDEntry entry in iExifTable.Values) size+=entry.DataSize;

				iIFD0Table[34665]=new IFDEntry(34665, exifOffset);
			}

			uint gpsOffset=size;
			uint gpsValueOffset=size;
			if(iGPSTable.Count>0)
			{
				size+=6; // GPS header and footer
				size+=12*(uint)iGPSTable.Count; // Tags
				gpsValueOffset=size;
				foreach(IFDEntry entry in iGPSTable.Values) size+=entry.DataSize;

				iIFD0Table[34853]=new IFDEntry(34853, gpsOffset);
			}

			uint interopOffset=size;
			uint interopValueOffset=size;
			if(iInteroperabilityTable.Count>0)
			{
				size+=6; // Interoperability header and footer
				size+=12*(uint)iInteroperabilityTable.Count; // Tags
				interopValueOffset=size;
				foreach(IFDEntry entry in iInteroperabilityTable.Values) size+=entry.DataSize;

				iExifTable[40965]=new IFDEntry(40965, interopOffset);
			}
			#endregion

			#region Create and sort tables
			foreach(IFDEntry entry in iIFD0Table.Values) lIFD0Table.Add(entry);
			lIFD0Table.Sort(IFDEntry.CompareIFDEntry);

			foreach(IFDEntry entry in iExifTable.Values) lExifTable.Add(entry);
			lExifTable.Sort(IFDEntry.CompareIFDEntry);

			foreach(IFDEntry entry in iGPSTable.Values) lGPSTable.Add(entry);
			lGPSTable.Sort(IFDEntry.CompareIFDEntry);

			foreach(IFDEntry entry in iInteroperabilityTable.Values) lInteropTable.Add(entry);
			lInteropTable.Sort(IFDEntry.CompareIFDEntry);
			#endregion

			byte[] buffer=new byte[size];

			#region Write header and tables
			buffer[0]=buffer[1]=(byte)'I';
			Set((ushort)42, buffer, 2);
			Set(ifd0Offset, buffer, 4);

			WriteIFDTable(buffer, lIFD0Table, ifd0Offset, ifd0ValueOffset);
			if(lExifTable.Count!=0) WriteIFDTable(buffer, lExifTable, exifOffset, exifValueOffset);
			if(lGPSTable.Count!=0) WriteIFDTable(buffer, lGPSTable, gpsOffset, gpsValueOffset);
			if(lInteropTable.Count!=0) WriteIFDTable(buffer, lInteropTable, interopOffset, interopValueOffset);
			#endregion

			return buffer;
		}
		#endregion

		#region public bool HasData
		public bool HasData
		{
			get
			{
				if(IFD0Table!=null&&IFD0Table.Count>0) return true;
				if(ExifTable!=null&&ExifTable.Count>0) return true;
				if(GPSTable!=null&&GPSTable.Count>0) return true;
				if(InteroperabilityTable!=null&&InteroperabilityTable.Count>0) return true;

				#region if(IFD0) return true;
				if(	imageWidthState==EXIF_TAG_STATE.ReadWrite||
					imageLengthState==EXIF_TAG_STATE.ReadWrite||
					bitsPerSampleState==EXIF_TAG_STATE.ReadWrite||
					compressionState==EXIF_TAG_STATE.ReadWrite||
					photometricInterpretationState==EXIF_TAG_STATE.ReadWrite||
					orientationState==EXIF_TAG_STATE.ReadWrite||
					samplesPerPixelState==EXIF_TAG_STATE.ReadWrite||
					planarConfigurationState==EXIF_TAG_STATE.ReadWrite||
					yCbCrSubSamplingState==EXIF_TAG_STATE.ReadWrite||
					yCbCrPositioningState==EXIF_TAG_STATE.ReadWrite||
					xResolutionState==EXIF_TAG_STATE.ReadWrite||
					yResolutionState==EXIF_TAG_STATE.ReadWrite||
					resolutionUnitState==EXIF_TAG_STATE.ReadWrite||
					transferFunctionState==EXIF_TAG_STATE.ReadWrite||
					whitePointState==EXIF_TAG_STATE.ReadWrite||
					primaryChromaticitiesState==EXIF_TAG_STATE.ReadWrite||
					yCbCrCoefficientsState==EXIF_TAG_STATE.ReadWrite||
					referenceBlackWhiteState==EXIF_TAG_STATE.ReadWrite||
					dateTimeState==EXIF_TAG_STATE.ReadWrite||
					imageDescriptionState==EXIF_TAG_STATE.ReadWrite||
					makeState==EXIF_TAG_STATE.ReadWrite||
					modelState==EXIF_TAG_STATE.ReadWrite||
					softwareState==EXIF_TAG_STATE.ReadWrite||
					artistState==EXIF_TAG_STATE.ReadWrite||
					copyrightState==EXIF_TAG_STATE.ReadWrite) return true;
				#endregion

				#region if(Exif) return true;
				if(	exifVersionState==EXIF_TAG_STATE.ReadWrite||
					colorSpaceState==EXIF_TAG_STATE.ReadWrite||
					componentsConfigurationState==EXIF_TAG_STATE.ReadWrite||
					compressedBitsPerPixelState==EXIF_TAG_STATE.ReadWrite||
					pixelXDimensionState==EXIF_TAG_STATE.ReadWrite||
					pixelYDimensionState==EXIF_TAG_STATE.ReadWrite||
					makerNoteState==EXIF_TAG_STATE.ReadWrite||
					userCommentState==EXIF_TAG_STATE.ReadWrite||
					relatedSoundFileState==EXIF_TAG_STATE.ReadWrite||
					dateTimeOriginalState==EXIF_TAG_STATE.ReadWrite||
					dateTimeDigitizedState==EXIF_TAG_STATE.ReadWrite||
					subsecTimeState==EXIF_TAG_STATE.ReadWrite||
					subsecTimeOriginalState==EXIF_TAG_STATE.ReadWrite||
					subsecTimeDigitizedState==EXIF_TAG_STATE.ReadWrite||

					exposureTimeState==EXIF_TAG_STATE.ReadWrite||
					fNumberState==EXIF_TAG_STATE.ReadWrite||
					exposureProgramState==EXIF_TAG_STATE.ReadWrite||
					spectralSensitivityState==EXIF_TAG_STATE.ReadWrite||
					isoSpeedRatingsState==EXIF_TAG_STATE.ReadWrite||
					oecfState==EXIF_TAG_STATE.ReadWrite||
					shutterSpeedValueState==EXIF_TAG_STATE.ReadWrite||
					apertureValueState==EXIF_TAG_STATE.ReadWrite||
					brightnessValueState==EXIF_TAG_STATE.ReadWrite||
					exposureBiasValueState==EXIF_TAG_STATE.ReadWrite||
					maxApertureValueState==EXIF_TAG_STATE.ReadWrite||
					subjectDistanceState==EXIF_TAG_STATE.ReadWrite||
					meteringModeState==EXIF_TAG_STATE.ReadWrite||
					lightSourceState==EXIF_TAG_STATE.ReadWrite||
					flashState==EXIF_TAG_STATE.ReadWrite||
					focalLengthState==EXIF_TAG_STATE.ReadWrite||
					subjectAreaState==EXIF_TAG_STATE.ReadWrite||
					flashEnergyState==EXIF_TAG_STATE.ReadWrite||
					spatialFrequencyResponseState==EXIF_TAG_STATE.ReadWrite||
					focalPlaneXResolutionState==EXIF_TAG_STATE.ReadWrite||
					focalPlaneYResolutionState==EXIF_TAG_STATE.ReadWrite||
					focalPlaneResolutionUnitState==EXIF_TAG_STATE.ReadWrite||
					subjectLocationState==EXIF_TAG_STATE.ReadWrite||
					exposureIndexState==EXIF_TAG_STATE.ReadWrite||
					sensingMethodeState==EXIF_TAG_STATE.ReadWrite||
					fileSourceState==EXIF_TAG_STATE.ReadWrite||
					sceneTypeState==EXIF_TAG_STATE.ReadWrite||
					cfaPatternState==EXIF_TAG_STATE.ReadWrite||
					customRenderingState==EXIF_TAG_STATE.ReadWrite||
					exposureModeState==EXIF_TAG_STATE.ReadWrite||
					whiteBalanceState==EXIF_TAG_STATE.ReadWrite||
					digitalZoomRatioState==EXIF_TAG_STATE.ReadWrite||
					focalLengthIn35mmFilmState==EXIF_TAG_STATE.ReadWrite||
					sceneCaptureTypeState==EXIF_TAG_STATE.ReadWrite||
					gainControlState==EXIF_TAG_STATE.ReadWrite||
					contrastState==EXIF_TAG_STATE.ReadWrite||
					saturationState==EXIF_TAG_STATE.ReadWrite||
					sharpnessState==EXIF_TAG_STATE.ReadWrite||
					deviceSettingDescriptionState==EXIF_TAG_STATE.ReadWrite||
					subjectDistanceRangeState==EXIF_TAG_STATE.ReadWrite||

					imageUniqueIDState==EXIF_TAG_STATE.ReadWrite) return true;
				#endregion

				#region if(GPS) return true;
				if(	gpsVersionIDState==EXIF_TAG_STATE.ReadWrite||
					gpsLatitudeRefState==EXIF_TAG_STATE.ReadWrite||
					gpsLatitudeState==EXIF_TAG_STATE.ReadWrite||
					gpsLongitudeRefState==EXIF_TAG_STATE.ReadWrite||
					gpsLongitudeState==EXIF_TAG_STATE.ReadWrite||
					gpsAltitudeRefState==EXIF_TAG_STATE.ReadWrite||
					gpsAltitudeState==EXIF_TAG_STATE.ReadWrite||
					gpsTimeStampState==EXIF_TAG_STATE.ReadWrite||
					gpsSatellitesState==EXIF_TAG_STATE.ReadWrite||
					gpsStatusState==EXIF_TAG_STATE.ReadWrite||
					gpsMeasureModeState==EXIF_TAG_STATE.ReadWrite||
					gpsDOPState==EXIF_TAG_STATE.ReadWrite||
					gpsSpeedRefState==EXIF_TAG_STATE.ReadWrite||
					gpsSpeedState==EXIF_TAG_STATE.ReadWrite||
					gpsTrackRefState==EXIF_TAG_STATE.ReadWrite||
					gpsTrackState==EXIF_TAG_STATE.ReadWrite||
					gpsImgDirectionRefState==EXIF_TAG_STATE.ReadWrite||
					gpsImgDirectionState==EXIF_TAG_STATE.ReadWrite||
					gpsMapDatumState==EXIF_TAG_STATE.ReadWrite||
					gpsDestLatitudeRefState==EXIF_TAG_STATE.ReadWrite||
					gpsDestLatitudeState==EXIF_TAG_STATE.ReadWrite||
					gpsDestLongitudeRefState==EXIF_TAG_STATE.ReadWrite||
					gpsDestLongitudeState==EXIF_TAG_STATE.ReadWrite||
					gpsDestBearingRefState==EXIF_TAG_STATE.ReadWrite||
					gpsDestBearingState==EXIF_TAG_STATE.ReadWrite||
					gpsDestDistanceRefState==EXIF_TAG_STATE.ReadWrite||
					gpsDestDistanceState==EXIF_TAG_STATE.ReadWrite||
					gpsProcessingMethodeState==EXIF_TAG_STATE.ReadWrite||
					gpsAreaInformationState==EXIF_TAG_STATE.ReadWrite||
					gpsDateStampState==EXIF_TAG_STATE.ReadWrite||
					gpsDifferentialState==EXIF_TAG_STATE.ReadWrite) return true;
				#endregion

				if(interoperabilityIndexState==EXIF_TAG_STATE.ReadWrite) return true;

				return false;
			}
		}
		#endregion

		#region TIFF Attributes
		/////////////////////////////////////
		// TIFF Attributes
		/////////////////////////////////////

		// Tags relating to image data structure

		#region ImageWidth
		EXIF_TAG_STATE imageWidthState;
		public EXIF_TAG_STATE ImageWidthState
		{
			get { return imageWidthState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				imageWidthState=value;
				imageWidth=0;
			}
		}

		uint imageWidth;
		public uint ImageWidth
		{
			get { return imageWidth; }
			set
			{
				if(imageWidthState==EXIF_TAG_STATE.DontWrite) return;
				if(value==0) throw new ArgumentException("ImageWidth must be >0.");

				imageWidth=value;
				imageWidthState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region ImageLength
		EXIF_TAG_STATE imageLengthState;
		public EXIF_TAG_STATE ImageLengthState
		{
			get { return imageLengthState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				imageLengthState=value;
				imageLength=0;
			}
		}

		uint imageLength;
		public uint ImageLength
		{
			get { return imageLength; }
			set
			{
				if(imageLengthState==EXIF_TAG_STATE.DontWrite) return;
				if(value==0) throw new ArgumentException("ImageLength must be >0.");

				imageLength=value;
				imageLengthState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region BitsPerSample
		EXIF_TAG_STATE bitsPerSampleState;
		public EXIF_TAG_STATE BitsPerSampleState
		{
			get { return bitsPerSampleState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				bitsPerSampleState=value;
				bitsPerSample=null;
			}
		}

		ushort[] bitsPerSample;
		public ushort[] BitsPerSample
		{
			get { return bitsPerSample; }
			set
			{
				if(bitsPerSampleState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length!=3) throw new ArgumentException("BitsPerSample.Length must be 3.");
				if(value[0]!=8||value[1]!=8||value[2]!=8) throw new ArgumentException("BitsPerSample[] must be {8, 8, 8}.");

				bitsPerSample=value;
				bitsPerSampleState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region Compression
		EXIF_TAG_STATE compressionState;
		public EXIF_TAG_STATE CompressionState
		{
			get { return compressionState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				compressionState=value;
				compression=0;
			}
		}

		ushort compression;
		public ushort Compression
		{
			get { return compression; }
			set
			{
				if(compressionState==EXIF_TAG_STATE.DontWrite) return;
				if(value!=6) throw new ArgumentException("Compression must be 6 (JPEG compression).");

				compression=value;
				compressionState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region PhotometricInterpretation
		EXIF_TAG_STATE photometricInterpretationState;
		public EXIF_TAG_STATE PhotometricInterpretationState
		{
			get { return photometricInterpretationState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				photometricInterpretationState=value;
				photometricInterpretation=0;
			}
		}

		ushort photometricInterpretation;
		public ushort PhotometricInterpretation
		{
			get { return photometricInterpretation; }
			set
			{
				if(photometricInterpretationState==EXIF_TAG_STATE.DontWrite) return;
				if(value!=2&&value!=6) throw new ArgumentException("PhotometricInterpretation must be 6 (YCrCb) (or 2 (RGB)).");

				photometricInterpretation=value;
				photometricInterpretationState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region Orientation
		EXIF_TAG_STATE orientationState;
		public EXIF_TAG_STATE OrientationState
		{
			get { return orientationState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				orientationState=value;
				orientation=0;
			}
		}

		ushort orientation;
		public ushort Orientation
		{
			get { return orientation; }
			set
			{
				if(orientationState==EXIF_TAG_STATE.DontWrite) return;
				if(value<1||value>8) throw new ArgumentException("Orientation must be >=1 and <=8.");

				orientation=value;
				orientationState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region SamplesPerPixel
		EXIF_TAG_STATE samplesPerPixelState;
		public EXIF_TAG_STATE SamplesPerPixelState
		{
			get { return samplesPerPixelState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				samplesPerPixelState=value;
				samplesPerPixel=0;
			}
		}

		ushort samplesPerPixel;
		public ushort SamplesPerPixel
		{
			get { return samplesPerPixel; }
			set
			{
				if(samplesPerPixelState==EXIF_TAG_STATE.DontWrite) return;
				if(value!=3) throw new ArgumentException("SamplesPerPixel must be 3.");

				samplesPerPixel=value;
				samplesPerPixelState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region PlanarConfiguration
		EXIF_TAG_STATE planarConfigurationState;
		public EXIF_TAG_STATE PlanarConfigurationState
		{
			get { return planarConfigurationState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				planarConfigurationState=value;
				planarConfiguration=0;
			}
		}

		ushort planarConfiguration;
		public ushort PlanarConfiguration
		{
			get { return planarConfiguration; }
			set
			{
				if(planarConfigurationState==EXIF_TAG_STATE.DontWrite) return;
				if(value!=2) throw new ArgumentException("PlanarConfiguration must be 2 (planar format).");

				planarConfiguration=value;
				planarConfigurationState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region YCbCrSubSampling
		EXIF_TAG_STATE yCbCrSubSamplingState;
		public EXIF_TAG_STATE YCbCrSubSamplingState
		{
			get { return yCbCrSubSamplingState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				yCbCrSubSamplingState=value;
				yCbCrSubSampling=null;
			}
		}

		ushort[] yCbCrSubSampling;
		public ushort[] YCbCrSubSampling
		{
			get { return yCbCrSubSampling; }
			set
			{
				if(yCbCrSubSamplingState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length!=2) throw new ArgumentException("YCbCrSubSampling.Length must be 2.");

				yCbCrSubSampling=value;
				yCbCrSubSamplingState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region YCbCrPositioning
		EXIF_TAG_STATE yCbCrPositioningState;
		public EXIF_TAG_STATE YCbCrPositioningState
		{
			get { return yCbCrPositioningState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				yCbCrPositioningState=value;
				yCbCrPositioning=0;
			}
		}

		ushort yCbCrPositioning;
		public ushort YCbCrPositioning
		{
			get { return yCbCrPositioning; }
			set
			{
				if(yCbCrPositioningState==EXIF_TAG_STATE.DontWrite) return;
				if(value<1||value>2) throw new ArgumentException("YCbCrPositioning must be 1 (centered) or 2 (co-sited).");

				yCbCrPositioning=value;
				yCbCrPositioningState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region XResolution
		EXIF_TAG_STATE xResolutionState;
		public EXIF_TAG_STATE XResolutionState
		{
			get { return xResolutionState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				xResolutionState=value;
				xResolution.numerator=0;
				xResolution.denominator=0;
			}
		}

		RATIONAL xResolution;
		public RATIONAL XResolution
		{
			get { return xResolution; }
			set
			{
				if(xResolutionState==EXIF_TAG_STATE.DontWrite) return;
				if(value.denominator==0) throw new ArgumentException("XResolution.denominator must be >0.");

				xResolution=value;
				xResolutionState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region YResolution
		EXIF_TAG_STATE yResolutionState;
		public EXIF_TAG_STATE YResolutionState
		{
			get { return yResolutionState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				yResolutionState=value;
				yResolution.numerator=0;
				yResolution.denominator=0;
			}
		}

		RATIONAL yResolution;
		public RATIONAL YResolution
		{
			get { return yResolution; }
			set
			{
				if(yResolutionState==EXIF_TAG_STATE.DontWrite) return;
				if(value.denominator==0) throw new ArgumentException("YResolution.denominator must be >0.");

				yResolution=value;
				yResolutionState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region ResolutionUnit
		EXIF_TAG_STATE resolutionUnitState;
		public EXIF_TAG_STATE ResolutionUnitState
		{
			get { return resolutionUnitState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				resolutionUnitState=value;
				resolutionUnit=0;
			}
		}

		ushort resolutionUnit;
		public ushort ResolutionUnit
		{
			get { return resolutionUnit; }
			set
			{
				if(resolutionUnitState==EXIF_TAG_STATE.DontWrite) return;
				if(value<2||value>3) throw new ArgumentException("ResolutionUnit must be 2 (inches) or 3 (centimeters).");

				resolutionUnit=value;
				resolutionUnitState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		// Tags relation to recording offset
		// --- Omitted ---

		// Tags relating to image data characteristics

		#region TransferFunction
		EXIF_TAG_STATE transferFunctionState;
		public EXIF_TAG_STATE TransferFunctionState
		{
			get { return transferFunctionState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				transferFunctionState=value;
				transferFunction=null;
			}
		}

		ushort[] transferFunction;
		public ushort[] TransferFunction
		{
			get { return transferFunction; }
			set
			{
				if(transferFunctionState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length!=3*256) throw new ArgumentException("TransferFunction.Length must be 3*256.");

				transferFunction=value;
				transferFunctionState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region WhitePoint
		EXIF_TAG_STATE whitePointState;
		public EXIF_TAG_STATE WhitePointState
		{
			get { return whitePointState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				whitePointState=value;
				whitePoint=null;
			}
		}

		RATIONAL[] whitePoint;
		public RATIONAL[] WhitePoint
		{
			get { return whitePoint; }
			set
			{
				if(whitePointState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length!=2) throw new ArgumentException("WhitePoint.Length must be 2.");

				whitePoint=value;
				whitePointState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region PrimaryChromaticities
		EXIF_TAG_STATE primaryChromaticitiesState;
		public EXIF_TAG_STATE PrimaryChromaticitiesState
		{
			get { return primaryChromaticitiesState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				primaryChromaticitiesState=value;
				primaryChromaticities=null;
			}
		}

		RATIONAL[] primaryChromaticities;
		public RATIONAL[] PrimaryChromaticities
		{
			get { return primaryChromaticities; }
			set
			{
				if(primaryChromaticitiesState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length!=6) throw new ArgumentException("PrimaryChromaticities.Length must be 6.");

				primaryChromaticities=value;
				primaryChromaticitiesState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region YCbCrCoefficients
		EXIF_TAG_STATE yCbCrCoefficientsState;
		public EXIF_TAG_STATE YCbCrCoefficientsState
		{
			get { return yCbCrCoefficientsState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				yCbCrCoefficientsState=value;
				yCbCrCoefficients=null;
			}
		}

		RATIONAL[] yCbCrCoefficients;
		public RATIONAL[] YCbCrCoefficients
		{
			get { return yCbCrCoefficients; }
			set
			{
				if(yCbCrCoefficientsState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length!=3) throw new ArgumentException("YCbCrCoefficients.Length must be 3.");

				yCbCrCoefficients=value;
				yCbCrCoefficientsState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region ReferenceBlackWhite
		EXIF_TAG_STATE referenceBlackWhiteState;
		public EXIF_TAG_STATE ReferenceBlackWhiteState
		{
			get { return referenceBlackWhiteState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				referenceBlackWhiteState=value;
				referenceBlackWhite=null;
			}
		}

		RATIONAL[] referenceBlackWhite;
		public RATIONAL[] ReferenceBlackWhite
		{
			get { return referenceBlackWhite; }
			set
			{
				if(referenceBlackWhiteState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length!=6) throw new ArgumentException("ReferenceBlackWhite.Length must be 6.");

				referenceBlackWhite=value;
				referenceBlackWhiteState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		// Other tags

		#region DateTime
		EXIF_TAG_STATE dateTimeState;
		public EXIF_TAG_STATE DateTimeState
		{
			get { return dateTimeState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				dateTimeState=value;
				dateTime=DateTime.Now;
			}
		}

		DateTime dateTime;
		public DateTime DateTime
		{
			get { return dateTime; }
			set
			{
				if(dateTimeState==EXIF_TAG_STATE.DontWrite) return;

				dateTime=value;
				dateTimeState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region ImageDescription
		EXIF_TAG_STATE imageDescriptionState;
		public EXIF_TAG_STATE ImageDescriptionState
		{
			get { return imageDescriptionState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				imageDescriptionState=value;
				imageDescription=null;
			}
		}

		string imageDescription;
		public string ImageDescription
		{
			get { return imageDescription; }
			set
			{
				if(imageDescriptionState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();

				imageDescription=value;
				imageDescriptionState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region Make
		EXIF_TAG_STATE makeState;
		public EXIF_TAG_STATE MakeState
		{
			get { return makeState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				makeState=value;
				make=null;
			}
		}

		string make;
		public string Make
		{
			get { return make; }
			set
			{
				if(makeState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();

				make=value;
				makeState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region Model
		EXIF_TAG_STATE modelState;
		public EXIF_TAG_STATE ModelState
		{
			get { return modelState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				modelState=value;
				model=null;
			}
		}

		string model;
		public string Model
		{
			get { return model; }
			set
			{
				if(modelState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();

				model=value;
				modelState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region Software
		EXIF_TAG_STATE softwareState;
		public EXIF_TAG_STATE SoftwareState
		{
			get { return softwareState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				softwareState=value;
				software=null;
			}
		}

		string software;
		public string Software
		{
			get { return software; }
			set
			{
				if(softwareState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();

				software=value;
				softwareState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region Artist
		EXIF_TAG_STATE artistState;
		public EXIF_TAG_STATE ArtistState
		{
			get { return artistState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				artistState=value;
				artist=null;
			}
		}

		string artist;
		public string Artist
		{
			get { return artist; }
			set
			{
				if(artistState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();

				artist=value;
				artistState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region Copyright
		EXIF_TAG_STATE copyrightState;
		public EXIF_TAG_STATE CopyrightState
		{
			get { return copyrightState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				copyrightState=value;
				copyright=null;
			}
		}

		string[] copyright;
		public string[] Copyright
		{
			get { return copyright; }
			set
			{
				if(copyrightState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentNullException();

				copyright=value;
				copyrightState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		// Unofficial tags

		#region XPTitle
		EXIF_TAG_STATE xpTitleState;
		public EXIF_TAG_STATE XPTitleState
		{
			get { return xpTitleState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				xpTitleState=value;
				xpTitle=null;
			}
		}

		byte[] xpTitle;
		public byte[] XPTitle
		{
			get { return xpTitle; }
			set
			{
				if(xpTitleState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("XPTitle.Length must be >0.");

				xpTitle=value;
				xpTitleState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region XPComment
		EXIF_TAG_STATE xpCommentState;
		public EXIF_TAG_STATE XPCommentState
		{
			get { return xpCommentState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				xpCommentState=value;
				xpComment=null;
			}
		}

		byte[] xpComment;
		public byte[] XPComment
		{
			get { return xpComment; }
			set
			{
				if(xpCommentState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("XPComment.Length must be >0.");

				xpComment=value;
				xpCommentState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region XPAuthor
		EXIF_TAG_STATE xpAuthorState;
		public EXIF_TAG_STATE XPAuthorState
		{
			get { return xpAuthorState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				xpAuthorState=value;
				xpAuthor=null;
			}
		}

		byte[] xpAuthor;
		public byte[] XPAuthor
		{
			get { return xpAuthor; }
			set
			{
				if(xpAuthorState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("XPAuthor.Length must be >0.");

				xpAuthor=value;
				xpAuthorState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region XPKeywords
		EXIF_TAG_STATE xpKeywordsState;
		public EXIF_TAG_STATE XPKeywordsState
		{
			get { return xpKeywordsState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				xpKeywordsState=value;
				xpKeywords=null;
			}
		}

		byte[] xpKeywords;
		public byte[] XPKeywords
		{
			get { return xpKeywords; }
			set
			{
				if(xpKeywordsState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("XPKeywords.Length must be >0.");

				xpKeywords=value;
				xpKeywordsState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region XPSubject
		EXIF_TAG_STATE xpSubjectState;
		public EXIF_TAG_STATE XPSubjectState
		{
			get { return xpSubjectState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				xpSubjectState=value;
				xpSubject=null;
			}
		}

		byte[] xpSubject;
		public byte[] XPSubject
		{
			get { return xpSubject; }
			set
			{
				if(xpSubjectState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("XPSubject.Length must be >0.");

				xpSubject=value;
				xpSubjectState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#endregion

		#region Exif IFD Attributes
		/////////////////////////////////////
		// Exif IFD Attributes
		/////////////////////////////////////

		// Tags relating to version

		#region ExifVersion
		EXIF_TAG_STATE exifVersionState;
		public EXIF_TAG_STATE ExifVersionState
		{
			get { return exifVersionState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				exifVersionState=value;
				exifVersion=null;
			}
		}

		byte[] exifVersion;
		public byte[] ExifVersion
		{
			get { return exifVersion; }
			set
			{
				if(exifVersionState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length!=4) throw new ArgumentException("ExifVersion.Length must be 4.");

				exifVersion=value;
				exifVersionState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		// FlashpixVersion omitted

		// Tags relating to color spcae

		#region ColorSpace
		EXIF_TAG_STATE colorSpaceState;
		public EXIF_TAG_STATE ColorSpaceState
		{
			get { return colorSpaceState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				colorSpaceState=value;
				colorSpace=0;
			}
		}

		ushort colorSpace;
		public ushort ColorSpace
		{
			get { return colorSpace; }
			set
			{
				if(colorSpaceState==EXIF_TAG_STATE.DontWrite) return;
				if(value!=1&&value!=0xFFFF) throw new ArgumentException("ColorSpace must be 1 (sRGB) or 0xFFFF (Uncalibrated).");

				colorSpace=value;
				colorSpaceState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		// Tags relating to image configuration

		#region PixelXDimension
		EXIF_TAG_STATE pixelXDimensionState;
		public EXIF_TAG_STATE PixelXDimensionState
		{
			get { return pixelXDimensionState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				pixelXDimensionState=value;
				pixelXDimension=0;
			}
		}

		uint pixelXDimension;
		public uint PixelXDimension
		{
			get { return pixelXDimension; }
			set
			{
				if(pixelXDimensionState==EXIF_TAG_STATE.DontWrite) return;

				pixelXDimension=value;
				pixelXDimensionState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region PixelYDimension
		EXIF_TAG_STATE pixelYDimensionState;
		public EXIF_TAG_STATE PixelYDimensionState
		{
			get { return pixelYDimensionState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				pixelYDimensionState=value;
				pixelYDimension=0;
			}
		}

		uint pixelYDimension;
		public uint PixelYDimension
		{
			get { return pixelYDimension; }
			set
			{
				if(pixelYDimensionState==EXIF_TAG_STATE.DontWrite) return;

				pixelYDimension=value;
				pixelYDimensionState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region ComponentsConfiguration
		EXIF_TAG_STATE componentsConfigurationState;
		public EXIF_TAG_STATE ComponentsConfigurationState
		{
			get { return componentsConfigurationState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				componentsConfigurationState=value;
				componentsConfiguration=null;
			}
		}

		byte[] componentsConfiguration;
		public byte[] ComponentsConfiguration
		{
			get { return componentsConfiguration; }
			set
			{
				if(componentsConfigurationState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length!=4) throw new ArgumentException("ComponentsConfiguration.Length must be 4.");

				componentsConfiguration=value;
				componentsConfigurationState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region CompressedBitsPerPixel
		EXIF_TAG_STATE compressedBitsPerPixelState;
		public EXIF_TAG_STATE CompressedBitsPerPixelState
		{
			get { return compressedBitsPerPixelState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				compressedBitsPerPixelState=value;
				compressedBitsPerPixel.numerator=0;
				compressedBitsPerPixel.denominator=0;
			}
		}

		RATIONAL compressedBitsPerPixel;
		public RATIONAL CompressedBitsPerPixel
		{
			get { return compressedBitsPerPixel; }
			set
			{
				if(compressedBitsPerPixelState==EXIF_TAG_STATE.DontWrite) return;
				if(value.denominator==0) throw new ArgumentException("CompressedBitsPerPixel.denominator must be >0.");

				compressedBitsPerPixel=value;
				compressedBitsPerPixelState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		// Tags relating to user information

		#region MakerNote
		EXIF_TAG_STATE makerNoteState;
		public EXIF_TAG_STATE MakerNoteState
		{
			get { return makerNoteState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				makerNoteState=value;
				makerNote=null;
			}
		}

		byte[] makerNote;
		public byte[] MakerNote
		{
			get { return makerNote; }
			set
			{
				if(makerNoteState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("MakerNote.Length must be >0.");

				makerNote=value;
				makerNoteState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region UserComment
		EXIF_TAG_STATE userCommentState;
		public EXIF_TAG_STATE UserCommentState
		{
			get { return userCommentState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				userCommentState=value;
				userComment=null;
			}
		}

		byte[] userComment;
		public byte[] UserComment
		{
			get { return userComment; }
			set
			{
				if(userCommentState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length<=8) throw new ArgumentException("UserComment.Length must be >8.");

				userComment=value;
				userCommentState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		// Tags relating to related file

		#region RelatedSoundFile
		EXIF_TAG_STATE relatedSoundFileState;
		public EXIF_TAG_STATE RelatedSoundFileState
		{
			get { return relatedSoundFileState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				relatedSoundFileState=value;
				relatedSoundFile=null;
			}
		}

		string relatedSoundFile;
		public string RelatedSoundFile
		{
			get { return relatedSoundFile; }
			set
			{
				if(relatedSoundFileState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length!=12) throw new ArgumentException("RelatedSoundFile.Length must be 12 (8.3-Filename)");

				relatedSoundFile=value;
				relatedSoundFileState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		// Tags relating to date and time

		#region DateTimeOriginal
		EXIF_TAG_STATE dateTimeOriginalState;
		public EXIF_TAG_STATE DateTimeOriginalState
		{
			get { return dateTimeOriginalState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				dateTimeOriginalState=value;
				dateTimeOriginal=DateTime.Now;
			}
		}

		DateTime dateTimeOriginal;
		public DateTime DateTimeOriginal
		{
			get { return dateTimeOriginal; }
			set
			{
				if(dateTimeOriginalState==EXIF_TAG_STATE.DontWrite) return;

				dateTimeOriginal=value;
				dateTimeOriginalState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region DateTimeDigitized
		EXIF_TAG_STATE dateTimeDigitizedState;
		public EXIF_TAG_STATE DateTimeDigitizedState
		{
			get { return dateTimeDigitizedState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				dateTimeDigitizedState=value;
				dateTimeDigitized=DateTime.Now;
			}
		}

		DateTime dateTimeDigitized;
		public DateTime DateTimeDigitized
		{
			get { return dateTimeDigitized; }
			set
			{
				if(dateTimeDigitizedState==EXIF_TAG_STATE.DontWrite) return;

				dateTimeDigitized=value;
				dateTimeDigitizedState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region SubsecTime
		EXIF_TAG_STATE subsecTimeState;
		public EXIF_TAG_STATE SubsecTimeState
		{
			get { return subsecTimeState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				subsecTimeState=value;
				subsecTime=null;
			}
		}

		string subsecTime;
		public string SubsecTime
		{
			get { return subsecTime; }
			set
			{
				if(subsecTimeState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("SubsecTime.Length must be >0.");

				subsecTime=value;
				subsecTimeState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region SubsecTimeOriginal
		EXIF_TAG_STATE subsecTimeOriginalState;
		public EXIF_TAG_STATE SubsecTimeOriginalState
		{
			get { return subsecTimeOriginalState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				subsecTimeOriginalState=value;
				subsecTimeOriginal=null;
			}
		}

		string subsecTimeOriginal;
		public string SubsecTimeOriginal
		{
			get { return subsecTimeOriginal; }
			set
			{
				if(subsecTimeOriginalState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("SubsecTimeOriginal.Length must be >0.");

				subsecTimeOriginal=value;
				subsecTimeOriginalState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region SubsecTimeDigitized
		EXIF_TAG_STATE subsecTimeDigitizedState;
		public EXIF_TAG_STATE SubsecTimeDigitizedState
		{
			get { return subsecTimeDigitizedState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				subsecTimeDigitizedState=value;
				subsecTimeDigitized=null;
			}
		}

		string subsecTimeDigitized;
		public string SubsecTimeDigitized
		{
			get { return subsecTimeDigitized; }
			set
			{
				if(subsecTimeDigitizedState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("SubsecTimeDigitized.Length must be >0.");

				subsecTimeDigitized=value;
				subsecTimeDigitizedState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		// Tags relating to picture-taking conditions

		#region ExposureTime
		EXIF_TAG_STATE exposureTimeState;
		public EXIF_TAG_STATE ExposureTimeState
		{
			get { return exposureTimeState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				exposureTimeState=value;
				exposureTime.numerator=0;
				exposureTime.denominator=0;
			}
		}

		RATIONAL exposureTime;
		public RATIONAL ExposureTime
		{
			get { return exposureTime; }
			set
			{
				if(exposureTimeState==EXIF_TAG_STATE.DontWrite) return;
				if(value.denominator==0) throw new ArgumentException("ExposureTime.denominator must be >0.");

				exposureTime=value;
				exposureTimeState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region FNumber
		EXIF_TAG_STATE fNumberState;
		public EXIF_TAG_STATE FNumberState
		{
			get { return fNumberState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				fNumberState=value;
				fNumber.numerator=0;
				fNumber.denominator=0;
			}
		}

		RATIONAL fNumber;
		public RATIONAL FNumber
		{
			get { return fNumber; }
			set
			{
				if(fNumberState==EXIF_TAG_STATE.DontWrite) return;
				if(value.denominator==0) throw new ArgumentException("FNumber.denominator must be >0.");

				fNumber=value;
				fNumberState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region ExposureProgram
		EXIF_TAG_STATE exposureProgramState;
		public EXIF_TAG_STATE ExposureProgramState
		{
			get { return exposureProgramState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				exposureProgramState=value;
				exposureProgram=0;
			}
		}

		ushort exposureProgram;
		public ushort ExposureProgram
		{
			get { return exposureProgram; }
			set
			{
				if(exposureProgramState==EXIF_TAG_STATE.DontWrite) return;
				if(value>8) throw new ArgumentException("ExposureProgram must be <9.");

				exposureProgram=value;
				exposureProgramState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region SpectralSensitivity
		EXIF_TAG_STATE spectralSensitivityState;
		public EXIF_TAG_STATE SpectralSensitivityState
		{
			get { return spectralSensitivityState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				spectralSensitivityState=value;
				spectralSensitivity=null;
			}
		}

		string spectralSensitivity;
		public string SpectralSensitivity
		{
			get { return spectralSensitivity; }
			set
			{
				if(spectralSensitivityState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("SpectralSensitivity.Length must be >0.");

				spectralSensitivity=value;
				spectralSensitivityState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region ISOSpeedRatings
		EXIF_TAG_STATE isoSpeedRatingsState;
		public EXIF_TAG_STATE ISOSpeedRatingsState
		{
			get { return isoSpeedRatingsState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				isoSpeedRatingsState=value;
				isoSpeedRatings=null;
			}
		}

		ushort[] isoSpeedRatings;
		public ushort[] ISOSpeedRatings
		{
			get { return isoSpeedRatings; }
			set
			{
				if(isoSpeedRatingsState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("ISOSpeedRatings.Length must be >0.");

				isoSpeedRatings=value;
				isoSpeedRatingsState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region OECF
		EXIF_TAG_STATE oecfState;
		public EXIF_TAG_STATE OECFState
		{
			get { return oecfState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				oecfState=value;
				oecf=null;
			}
		}

		byte[] oecf;
		public byte[] OECF
		{
			get { return oecf; }
			set
			{
				if(oecfState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("OECF.Length must be >0.");

				oecf=value;
				oecfState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region ShutterSpeedValue
		EXIF_TAG_STATE shutterSpeedValueState;
		public EXIF_TAG_STATE ShutterSpeedValueState
		{
			get { return shutterSpeedValueState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				shutterSpeedValueState=value;
				shutterSpeedValue.numerator=0;
				shutterSpeedValue.denominator=0;
			}
		}

		SRATIONAL shutterSpeedValue;
		public SRATIONAL ShutterSpeedValue
		{
			get { return shutterSpeedValue; }
			set
			{
				if(shutterSpeedValueState==EXIF_TAG_STATE.DontWrite) return;
				if(value.denominator==0) throw new ArgumentException("ShutterSpeedValue.denominator must be >0.");

				shutterSpeedValue=value;
				shutterSpeedValueState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region ApertureValue
		EXIF_TAG_STATE apertureValueState;
		public EXIF_TAG_STATE ApertureValueState
		{
			get { return apertureValueState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				apertureValueState=value;
				apertureValue.numerator=0;
				apertureValue.denominator=0;
			}
		}

		RATIONAL apertureValue;
		public RATIONAL ApertureValue
		{
			get { return apertureValue; }
			set
			{
				if(apertureValueState==EXIF_TAG_STATE.DontWrite) return;
				if(value.denominator==0) throw new ArgumentException("ApertureValue.denominator must be >0.");

				apertureValue=value;
				apertureValueState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region BrightnessValue
		EXIF_TAG_STATE brightnessValueState;
		public EXIF_TAG_STATE BrightnessValueState
		{
			get { return brightnessValueState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				brightnessValueState=value;
				brightnessValue.numerator=0;
				brightnessValue.denominator=0;
			}
		}

		SRATIONAL brightnessValue;
		public SRATIONAL BrightnessValue
		{
			get { return brightnessValue; }
			set
			{
				if(brightnessValueState==EXIF_TAG_STATE.DontWrite) return;
				if(value.denominator==0) throw new ArgumentException("BrightnessValue.denominator must be >0.");

				brightnessValue=value;
				brightnessValueState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region ExposureBiasValue
		EXIF_TAG_STATE exposureBiasValueState;
		public EXIF_TAG_STATE ExposureBiasValueState
		{
			get { return exposureBiasValueState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				exposureBiasValueState=value;
				exposureBiasValue.numerator=0;
				exposureBiasValue.denominator=0;
			}
		}

		SRATIONAL exposureBiasValue;
		public SRATIONAL ExposureBiasValue
		{
			get { return exposureBiasValue; }
			set
			{
				if(exposureBiasValueState==EXIF_TAG_STATE.DontWrite) return;
				if(value.denominator==0) throw new ArgumentException("ExposureBiasValue.denominator must be >0.");

				exposureBiasValue=value;
				exposureBiasValueState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region MaxApertureValue
		EXIF_TAG_STATE maxApertureValueState;
		public EXIF_TAG_STATE MaxApertureValueState
		{
			get { return maxApertureValueState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				maxApertureValueState=value;
				maxApertureValue.numerator=0;
				maxApertureValue.denominator=0;
			}
		}

		RATIONAL maxApertureValue;
		public RATIONAL MaxApertureValue
		{
			get { return maxApertureValue; }
			set
			{
				if(maxApertureValueState==EXIF_TAG_STATE.DontWrite) return;
				if(value.denominator==0) throw new ArgumentException("MaxApertureValue.denominator must be >0.");

				maxApertureValue=value;
				maxApertureValueState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region SubjectDistance
		EXIF_TAG_STATE subjectDistanceState;
		public EXIF_TAG_STATE SubjectDistanceState
		{
			get { return subjectDistanceState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				subjectDistanceState=value;
				subjectDistance.numerator=0;
				subjectDistance.denominator=0;
			}
		}

		RATIONAL subjectDistance;
		public RATIONAL SubjectDistance
		{
			get { return subjectDistance; }
			set
			{
				if(subjectDistanceState==EXIF_TAG_STATE.DontWrite) return;
				if(value.denominator==0) throw new ArgumentException("SubjectDistance.denominator must be >0.");

				subjectDistance=value;
				subjectDistanceState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region MeteringMode
		EXIF_TAG_STATE meteringModeState;
		public EXIF_TAG_STATE MeteringModeState
		{
			get { return meteringModeState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				meteringModeState=value;
				meteringMode=0;
			}
		}

		ushort meteringMode;
		public ushort MeteringMode
		{
			get { return meteringMode; }
			set
			{
				if(meteringModeState==EXIF_TAG_STATE.DontWrite) return;

				meteringMode=value;
				meteringModeState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region LightSource
		EXIF_TAG_STATE lightSourceState;
		public EXIF_TAG_STATE LightSourceState
		{
			get { return lightSourceState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				lightSourceState=value;
				lightSource=0;
			}
		}

		ushort lightSource;
		public ushort LightSource
		{
			get { return lightSource; }
			set
			{
				if(lightSourceState==EXIF_TAG_STATE.DontWrite) return;

				lightSource=value;
				lightSourceState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region Flash
		EXIF_TAG_STATE flashState;
		public EXIF_TAG_STATE FlashState
		{
			get { return flashState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				flashState=value;
				flash=0;
			}
		}

		ushort flash;
		public ushort Flash
		{
			get { return flash; }
			set
			{
				if(flashState==EXIF_TAG_STATE.DontWrite) return;

				flash=value;
				flashState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region SubjectArea
		EXIF_TAG_STATE subjectAreaState;
		public EXIF_TAG_STATE SubjectAreaState
		{
			get { return subjectAreaState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				subjectAreaState=value;
				subjectArea=null;
			}
		}

		ushort[] subjectArea;
		public ushort[] SubjectArea
		{
			get { return subjectArea; }
			set
			{
				if(subjectAreaState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length<2||value.Length>4) throw new ArgumentException("SubjectArea.Length must be 2, 3, or 4.");

				subjectArea=value;
				subjectAreaState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region FocalLength
		EXIF_TAG_STATE focalLengthState;
		public EXIF_TAG_STATE FocalLengthState
		{
			get { return focalLengthState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				focalLengthState=value;
				focalLength.numerator=0;
				focalLength.denominator=0;
			}
		}

		RATIONAL focalLength;
		public RATIONAL FocalLength
		{
			get { return focalLength; }
			set
			{
				if(focalLengthState==EXIF_TAG_STATE.DontWrite) return;
				if(value.denominator==0) throw new ArgumentException("FocalLength.denominator must be >0.");

				focalLength=value;
				focalLengthState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region FlashEnergy
		EXIF_TAG_STATE flashEnergyState;
		public EXIF_TAG_STATE FlashEnergyState
		{
			get { return flashEnergyState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				flashEnergyState=value;
				flashEnergy.numerator=0;
				flashEnergy.denominator=0;
			}
		}

		RATIONAL flashEnergy;
		public RATIONAL FlashEnergy
		{
			get { return flashEnergy; }
			set
			{
				if(flashEnergyState==EXIF_TAG_STATE.DontWrite) return;
				if(value.denominator==0) throw new ArgumentException("FlashEnergy.denominator must be >0.");

				flashEnergy=value;
				flashEnergyState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region SpatialFrequencyResponse
		EXIF_TAG_STATE spatialFrequencyResponseState;
		public EXIF_TAG_STATE SpatialFrequencyResponseState
		{
			get { return spatialFrequencyResponseState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				spatialFrequencyResponseState=value;
				spatialFrequencyResponse=null;
			}
		}

		byte[] spatialFrequencyResponse;
		public byte[] SpatialFrequencyResponse
		{
			get { return spatialFrequencyResponse; }
			set
			{
				if(spatialFrequencyResponseState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("SpatialFrequencyResponse.Length must be >0.");

				spatialFrequencyResponse=value;
				spatialFrequencyResponseState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region FocalPlaneXResolution
		EXIF_TAG_STATE focalPlaneXResolutionState;
		public EXIF_TAG_STATE FocalPlaneXResolutionState
		{
			get { return focalPlaneXResolutionState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				focalPlaneXResolutionState=value;
				focalPlaneXResolution.numerator=0;
				focalPlaneXResolution.denominator=0;
			}
		}

		RATIONAL focalPlaneXResolution;
		public RATIONAL FocalPlaneXResolution
		{
			get { return focalPlaneXResolution; }
			set
			{
				if(focalPlaneXResolutionState==EXIF_TAG_STATE.DontWrite) return;
				if(value.denominator==0) throw new ArgumentException("FocalPlaneXResolution.denominator must be >0.");

				focalPlaneXResolution=value;
				focalPlaneXResolutionState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region FocalPlaneYResolution
		EXIF_TAG_STATE focalPlaneYResolutionState;
		public EXIF_TAG_STATE FocalPlaneYResolutionState
		{
			get { return focalPlaneYResolutionState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				focalPlaneYResolutionState=value;
				focalPlaneYResolution.numerator=0;
				focalPlaneYResolution.denominator=0;
			}
		}

		RATIONAL focalPlaneYResolution;
		public RATIONAL FocalPlaneYResolution
		{
			get { return focalPlaneYResolution; }
			set
			{
				if(focalPlaneYResolutionState==EXIF_TAG_STATE.DontWrite) return;
				if(value.denominator==0) throw new ArgumentException("FocalPlaneYResolution.denominator must be >0.");

				focalPlaneYResolution=value;
				focalPlaneYResolutionState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region FocalPlaneResolutionUnit
		EXIF_TAG_STATE focalPlaneResolutionUnitState;
		public EXIF_TAG_STATE FocalPlaneResolutionUnitState
		{
			get { return focalPlaneResolutionUnitState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				focalPlaneResolutionUnitState=value;
				focalPlaneResolutionUnit=0;
			}
		}

		ushort focalPlaneResolutionUnit;
		public ushort FocalPlaneResolutionUnit
		{
			get { return focalPlaneResolutionUnit; }
			set
			{
				if(focalPlaneResolutionUnitState==EXIF_TAG_STATE.DontWrite) return;
				if(value<2||value>5) throw new ArgumentException("FocalPlaneResolutionUnit must be 1 (none), 2 (inches), 3 (centimeters), 4 (millimeters) or 5 (mircometers).");

				focalPlaneResolutionUnit=value;
				focalPlaneResolutionUnitState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region SubjectLocation
		EXIF_TAG_STATE subjectLocationState;
		public EXIF_TAG_STATE SubjectLocationState
		{
			get { return subjectLocationState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				subjectLocationState=value;
				subjectLocation=null;
			}
		}

		ushort[] subjectLocation;
		public ushort[] SubjectLocation
		{
			get { return subjectLocation; }
			set
			{
				if(subjectLocationState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length!=2) throw new ArgumentException("SubjectLocation.Length must be 2.");

				subjectLocation=value;
				subjectLocationState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region ExposureIndex
		EXIF_TAG_STATE exposureIndexState;
		public EXIF_TAG_STATE ExposureIndexState
		{
			get { return exposureIndexState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				exposureIndexState=value;
				exposureIndex.numerator=0;
				exposureIndex.denominator=0;
			}
		}

		RATIONAL exposureIndex;
		public RATIONAL ExposureIndex
		{
			get { return exposureIndex; }
			set
			{
				if(exposureIndexState==EXIF_TAG_STATE.DontWrite) return;
				if(value.denominator==0) throw new ArgumentException("ExposureIndex.denominator must be >0.");

				exposureIndex=value;
				exposureIndexState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region SensingMethode
		EXIF_TAG_STATE sensingMethodeState;
		public EXIF_TAG_STATE SensingMethodeState
		{
			get { return sensingMethodeState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				sensingMethodeState=value;
				sensingMethode=0;
			}
		}

		ushort sensingMethode;
		public ushort SensingMethode
		{
			get { return sensingMethode; }
			set
			{
				if(sensingMethodeState==EXIF_TAG_STATE.DontWrite) return;
				if(value>8) throw new ArgumentException("SensingMethode must be <9.");

				sensingMethode=value;
				sensingMethodeState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region FileSource
		EXIF_TAG_STATE fileSourceState;
		public EXIF_TAG_STATE FileSourceState
		{
			get { return fileSourceState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				fileSourceState=value;
				fileSource=0;
			}
		}

		byte fileSource;
		public byte FileSource
		{
			get { return fileSource; }
			set
			{
				if(fileSourceState==EXIF_TAG_STATE.DontWrite) return;
				if(value!=3) throw new ArgumentException("FileSource must be 3 (DSC).");

				fileSource=value;
				fileSourceState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region SceneType
		EXIF_TAG_STATE sceneTypeState;
		public EXIF_TAG_STATE SceneTypeState
		{
			get { return sceneTypeState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				sceneTypeState=value;
				sceneType=0;
			}
		}

		byte sceneType;
		public byte SceneType
		{
			get { return sceneType; }
			set
			{
				if(sceneTypeState==EXIF_TAG_STATE.DontWrite) return;
				if(value!=1) throw new ArgumentException("SceneType must be 1 (a directly photographed image).");

				sceneType=value;
				sceneTypeState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region CFAPattern
		EXIF_TAG_STATE cfaPatternState;
		public EXIF_TAG_STATE CFAPatternState
		{
			get { return cfaPatternState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				cfaPatternState=value;
				cfaPattern=null;
			}
		}

		byte[] cfaPattern;
		public byte[] CFAPattern
		{
			get { return cfaPattern; }
			set
			{
				if(cfaPatternState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("CFAPattern.Length must be >0.");

				cfaPattern=value;
				cfaPatternState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region CustomRendering
		EXIF_TAG_STATE customRenderingState;
		public EXIF_TAG_STATE CustomRenderingState
		{
			get { return customRenderingState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				customRenderingState=value;
				customRendering=0;
			}
		}

		ushort customRendering;
		public ushort CustomRendering
		{
			get { return customRendering; }
			set
			{
				if(customRenderingState==EXIF_TAG_STATE.DontWrite) return;
				if(value>1) throw new ArgumentException("CustomRendering must be 0 (normal) or 1 (custom).");

				customRendering=value;
				customRenderingState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region ExposureMode
		EXIF_TAG_STATE exposureModeState;
		public EXIF_TAG_STATE ExposureModeState
		{
			get { return exposureModeState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				exposureModeState=value;
				exposureMode=0;
			}
		}

		ushort exposureMode;
		public ushort ExposureMode
		{
			get { return exposureMode; }
			set
			{
				if(exposureModeState==EXIF_TAG_STATE.DontWrite) return;
				if(value>2) throw new ArgumentException("ExposureMode must be 0 (auto exposure), 1 (manual exposure) or 2 (auto bracket).");

				exposureMode=value;
				exposureModeState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region WhiteBalance
		EXIF_TAG_STATE whiteBalanceState;
		public EXIF_TAG_STATE WhiteBalanceState
		{
			get { return whiteBalanceState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				whiteBalanceState=value;
				whiteBalance=0;
			}
		}

		ushort whiteBalance;
		public ushort WhiteBalance
		{
			get { return whiteBalance; }
			set
			{
				if(whiteBalanceState==EXIF_TAG_STATE.DontWrite) return;
				if(value>2) throw new ArgumentException("WhiteBalance must be 0 (auto white balance) or 1 (manual white balance).");

				whiteBalance=value;
				whiteBalanceState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region DigitalZoomRatio
		EXIF_TAG_STATE digitalZoomRatioState;
		public EXIF_TAG_STATE DigitalZoomRatioState
		{
			get { return digitalZoomRatioState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				digitalZoomRatioState=value;
				digitalZoomRatio.numerator=0;
				digitalZoomRatio.denominator=0;
			}
		}

		RATIONAL digitalZoomRatio;
		public RATIONAL DigitalZoomRatio
		{
			get { return digitalZoomRatio; }
			set
			{
				if(digitalZoomRatioState==EXIF_TAG_STATE.DontWrite) return;
				if(value.denominator==0) throw new ArgumentException("DigitalZoomRatio.denominator must be >0.");

				digitalZoomRatio=value;
				digitalZoomRatioState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region FocalLengthIn35mmFilm
		EXIF_TAG_STATE focalLengthIn35mmFilmState;
		public EXIF_TAG_STATE FocalLengthIn35mmFilmState
		{
			get { return focalLengthIn35mmFilmState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				focalLengthIn35mmFilmState=value;
				focalLengthIn35mmFilm=0;
			}
		}

		ushort focalLengthIn35mmFilm;
		public ushort FocalLengthIn35mmFilm
		{
			get { return focalLengthIn35mmFilm; }
			set
			{
				if(focalLengthIn35mmFilmState==EXIF_TAG_STATE.DontWrite) return;

				focalLengthIn35mmFilm=value;
				focalLengthIn35mmFilmState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region SceneCaptureType
		EXIF_TAG_STATE sceneCaptureTypeState;
		public EXIF_TAG_STATE SceneCaptureTypeState
		{
			get { return sceneCaptureTypeState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				sceneCaptureTypeState=value;
				sceneCaptureType=0;
			}
		}

		ushort sceneCaptureType;
		public ushort SceneCaptureType
		{
			get { return sceneCaptureType; }
			set
			{
				if(sceneCaptureTypeState==EXIF_TAG_STATE.DontWrite) return;
				if(value>3) throw new ArgumentException("SceneCaptureType must be 0 (standard), 1 (landscape), 2 (portrait) or 3 (night scene).");

				sceneCaptureType=value;
				sceneCaptureTypeState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region GainControl
		EXIF_TAG_STATE gainControlState;
		public EXIF_TAG_STATE GainControlState
		{
			get { return gainControlState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gainControlState=value;
				gainControl=0;
			}
		}

		ushort gainControl;
		public ushort GainControl
		{
			get { return gainControl; }
			set
			{
				if(gainControlState==EXIF_TAG_STATE.DontWrite) return;
				if(value>4) throw new ArgumentException("GainControl must be 0 (none), 1 (low gain up), 2 (high gain up), 3 (low gain down) or 4 (high gain down).");

				gainControl=value;
				gainControlState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region Contrast
		EXIF_TAG_STATE contrastState;
		public EXIF_TAG_STATE ContrastState
		{
			get { return contrastState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				contrastState=value;
				contrast=0;
			}
		}

		ushort contrast;
		public ushort Contrast
		{
			get { return contrast; }
			set
			{
				if(contrastState==EXIF_TAG_STATE.DontWrite) return;
				if(value>2) throw new ArgumentException("Contrast must be 0 (normal), 1 (soft) or 2 (hard).");

				contrast=value;
				contrastState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region Saturation
		EXIF_TAG_STATE saturationState;
		public EXIF_TAG_STATE SaturationState
		{
			get { return saturationState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				saturationState=value;
				saturation=0;
			}
		}

		ushort saturation;
		public ushort Saturation
		{
			get { return saturation; }
			set
			{
				if(saturationState==EXIF_TAG_STATE.DontWrite) return;
				if(value>2) throw new ArgumentException("Saturation must be 0 (normal), 1 (low saturation) or 2 (high saturation).");

				saturation=value;
				saturationState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region Sharpness
		EXIF_TAG_STATE sharpnessState;
		public EXIF_TAG_STATE SharpnessState
		{
			get { return sharpnessState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				sharpnessState=value;
				sharpness=0;
			}
		}

		ushort sharpness;
		public ushort Sharpness
		{
			get { return sharpness; }
			set
			{
				if(sharpnessState==EXIF_TAG_STATE.DontWrite) return;
				if(value>2) throw new ArgumentException("Sharpness must be 0 (normal), 1 (soft) or 2 (hard).");

				sharpness=value;
				sharpnessState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region DeviceSettingDescription
		EXIF_TAG_STATE deviceSettingDescriptionState;
		public EXIF_TAG_STATE DeviceSettingDescriptionState
		{
			get { return deviceSettingDescriptionState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				deviceSettingDescriptionState=value;
				deviceSettingDescription=null;
			}
		}

		byte[] deviceSettingDescription;
		public byte[] DeviceSettingDescription
		{
			get { return deviceSettingDescription; }
			set
			{
				if(deviceSettingDescriptionState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("DeviceSettingDescription.Length must be >0.");

				deviceSettingDescription=value;
				deviceSettingDescriptionState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region SubjectDistanceRange
		EXIF_TAG_STATE subjectDistanceRangeState;
		public EXIF_TAG_STATE SubjectDistanceRangeState
		{
			get { return subjectDistanceRangeState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				subjectDistanceRangeState=value;
				subjectDistanceRange=0;
			}
		}

		ushort subjectDistanceRange;
		public ushort SubjectDistanceRange
		{
			get { return subjectDistanceRange; }
			set
			{
				if(subjectDistanceRangeState==EXIF_TAG_STATE.DontWrite) return;
				if(value>3) throw new ArgumentException("SubjectDistanceRange must be 0 (unknown), 1 (macro), 2 (close view) or 3 (distant view).");

				subjectDistanceRange=value;
				subjectDistanceRangeState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		// Other tags

		#region ImageUniqueID
		EXIF_TAG_STATE imageUniqueIDState;
		public EXIF_TAG_STATE ImageUniqueIDState
		{
			get { return imageUniqueIDState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				imageUniqueIDState=value;
				imageUniqueID=null;
			}
		}

		string imageUniqueID;
		public string ImageUniqueID
		{
			get { return imageUniqueID; }
			set
			{
				if(imageUniqueIDState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length!=32) throw new ArgumentException("ImageUniqueID.Length must be 32 (32 hexadecimals)");
				foreach(char ch in value)
				{
					if((ch>='0'&&ch<='9')||(ch>='A'&&ch<='F')||(ch>='a'&&ch<='f')) continue;
					throw new ArgumentException("ImageUniqueID must be 32 hexadecimals.");
				}

				imageUniqueID=value;
				imageUniqueIDState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		// Unofficial tags

		#region ApplicationNotes
		EXIF_TAG_STATE applicationNotesState;
		public EXIF_TAG_STATE ApplicationNotesState
		{
			get { return applicationNotesState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				applicationNotesState=value;
				applicationNotes=null;
			}
		}

		byte[] applicationNotes;
		public byte[] ApplicationNotes
		{
			get { return applicationNotes; }
			set
			{
				if(applicationNotesState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("DeviceSettingDescription.Length must be >0.");

				applicationNotes=value;
				applicationNotesState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region TimeZoneOffset
		EXIF_TAG_STATE timeZoneOffsetState;
		public EXIF_TAG_STATE TimeZoneOffsetState
		{
			get { return timeZoneOffsetState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				timeZoneOffsetState=value;
				timeZoneOffset=null;
			}
		}

		short[] timeZoneOffset;
		public short[] TimeZoneOffset
		{
			get { return timeZoneOffset; }
			set
			{
				if(timeZoneOffsetState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("TimeZoneOffset.Length must be 1 or 2.");

				timeZoneOffset=value;
				timeZoneOffsetState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region SelfTimerMode
		EXIF_TAG_STATE selfTimerModeState;
		public EXIF_TAG_STATE SelfTimerModeState
		{
			get { return selfTimerModeState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				selfTimerModeState=value;
				selfTimerMode=0;
			}
		}

		ushort selfTimerMode;
		public ushort SelfTimerMode
		{
			get { return selfTimerMode; }
			set
			{
				if(selfTimerModeState==EXIF_TAG_STATE.DontWrite) return;

				selfTimerMode=value;
				selfTimerModeState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region SecurityClassification
		EXIF_TAG_STATE securityClassificationState;
		public EXIF_TAG_STATE SecurityClassificationState
		{
			get { return securityClassificationState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				securityClassificationState=value;
				securityClassification=' ';
			}
		}

		char securityClassification;
		public char SecurityClassification
		{
			get { return securityClassification; }
			set
			{
				if(securityClassificationState==EXIF_TAG_STATE.DontWrite) return;
				if(value!='C'&&value!='R'&&value!='S'&&value!='T'&&value!='U') throw new ArgumentException("SecurityClassification must be 'C' (Confidential), 'R' (Restricted), 'S' (Secret), 'T' (Top Secret) or 'U' (Unclassified).");

				securityClassification=value;
				securityClassificationState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region ImageHistory
		EXIF_TAG_STATE imageHistoryState;
		public EXIF_TAG_STATE ImageHistoryState
		{
			get { return imageHistoryState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				imageHistoryState=value;
				imageHistory=null;
			}
		}

		string imageHistory;
		public string ImageHistory
		{
			get { return imageHistory; }
			set
			{
				if(imageHistoryState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("ImageHistory.Length must be >0");

				imageHistory=value;
				imageHistoryState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region Gamma
		EXIF_TAG_STATE gammaState;
		public EXIF_TAG_STATE GammaState
		{
			get { return gammaState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gammaState=value;
				gamma.numerator=0;
				gamma.denominator=0;
			}
		}

		RATIONAL gamma;
		public RATIONAL Gamma
		{
			get { return gamma; }
			set
			{
				if(gammaState==EXIF_TAG_STATE.DontWrite) return;
				if(value.denominator==0) throw new ArgumentException("Gamma.denominator must be >0.");

				gamma=value;
				gammaState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region Padding
		EXIF_TAG_STATE paddingState;
		public EXIF_TAG_STATE PaddingState
		{
			get { return paddingState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				paddingState=value;
				padding=null;
			}
		}

		byte[] padding;
		public byte[] Padding
		{
			get { return padding; }
			set
			{
				if(paddingState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("Padding.Length must be >0.");

				padding=value;
				paddingState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region OffsetSchema
		EXIF_TAG_STATE offsetSchemaState;
		public EXIF_TAG_STATE OffsetSchemaState
		{
			get { return offsetSchemaState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				offsetSchemaState=value;
				offsetSchema=0;
			}
		}

		int offsetSchema;
		public int OffsetSchema
		{
			get { return offsetSchema; }
			set
			{
				if(offsetSchemaState==EXIF_TAG_STATE.DontWrite) return;

				offsetSchema=value;
				offsetSchemaState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		// Photoshop Camera RAW tags

		#region PCROwnerName
		EXIF_TAG_STATE pcrOwnerNameState;
		public EXIF_TAG_STATE PCROwnerNameState
		{
			get { return pcrOwnerNameState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				pcrOwnerNameState=value;
				pcrOwnerName=null;
			}
		}

		string pcrOwnerName;
		public string PCROwnerName
		{
			get { return pcrOwnerName; }
			set
			{
				if(pcrOwnerNameState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("PCROwnerName.Length must be >0.");

				pcrOwnerName=value;
				pcrOwnerNameState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region PCRSerialNumber
		EXIF_TAG_STATE pcrSerialNumberState;
		public EXIF_TAG_STATE PCRSerialNumberState
		{
			get { return pcrSerialNumberState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				pcrSerialNumberState=value;
				pcrSerialNumber=null;
			}
		}

		string pcrSerialNumber;
		public string PCRSerialNumber
		{
			get { return pcrSerialNumber; }
			set
			{
				if(pcrSerialNumberState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("PCRSerialNumber.Length must be >0.");

				pcrSerialNumber=value;
				pcrSerialNumberState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region PCRLens
		EXIF_TAG_STATE pcrLensState;
		public EXIF_TAG_STATE PCRLensState
		{
			get { return pcrLensState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				pcrLensState=value;
				pcrLens=null;
			}
		}

		string pcrLens;
		public string PCRLens
		{
			get { return pcrLens; }
			set
			{
				if(pcrLensState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("PCRLens.Length must be >0.");

				pcrLens=value;
				pcrLensState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region PCRRawFile
		EXIF_TAG_STATE pcrRawFileState;
		public EXIF_TAG_STATE PCRRawFileState
		{
			get { return pcrRawFileState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				pcrRawFileState=value;
				pcrRawFile=null;
			}
		}

		string pcrRawFile;
		public string PCRRawFile
		{
			get { return pcrRawFile; }
			set
			{
				if(pcrRawFileState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("PCRRawFile.Length must be >0.");

				pcrRawFile=value;
				pcrRawFileState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region PCRConverter
		EXIF_TAG_STATE pcrConverterState;
		public EXIF_TAG_STATE PCRConverterState
		{
			get { return pcrConverterState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				pcrConverterState=value;
				pcrConverter=null;
			}
		}

		string pcrConverter;
		public string PCRConverter
		{
			get { return pcrConverter; }
			set
			{
				if(pcrConverterState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("PCRConverter.Length must be >0.");

				pcrConverter=value;
				pcrConverterState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region PCRWhiteBalance
		EXIF_TAG_STATE pcrWhiteBalanceState;
		public EXIF_TAG_STATE PCRWhiteBalanceState
		{
			get { return pcrWhiteBalanceState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				pcrWhiteBalanceState=value;
				pcrWhiteBalance=null;
			}
		}

		string pcrWhiteBalance;
		public string PCRWhiteBalance
		{
			get { return pcrWhiteBalance; }
			set
			{
				if(pcrWhiteBalanceState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("PCRWhiteBalance.Length must be >0.");

				pcrWhiteBalance=value;
				pcrWhiteBalanceState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region PCRExposure
		EXIF_TAG_STATE pcrExposureState;
		public EXIF_TAG_STATE PCRExposureState
		{
			get { return pcrExposureState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				pcrExposureState=value;
				pcrExposure=null;
			}
		}

		string pcrExposure;
		public string PCRExposure
		{
			get { return pcrExposure; }
			set
			{
				if(pcrExposureState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("PCRExposure.Length must be >0.");

				pcrExposure=value;
				pcrExposureState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region PCRShadows
		EXIF_TAG_STATE pcrShadowsState;
		public EXIF_TAG_STATE PCRShadowsState
		{
			get { return pcrShadowsState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				pcrShadowsState=value;
				pcrShadows=null;
			}
		}

		string pcrShadows;
		public string PCRShadows
		{
			get { return pcrShadows; }
			set
			{
				if(pcrShadowsState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("PCRShadows.Length must be >0.");

				pcrShadows=value;
				pcrShadowsState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region PCRBrightness
		EXIF_TAG_STATE pcrBrightnessState;
		public EXIF_TAG_STATE PCRBrightnessState
		{
			get { return pcrBrightnessState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				pcrBrightnessState=value;
				pcrBrightness=null;
			}
		}

		string pcrBrightness;
		public string PCRBrightness
		{
			get { return pcrBrightness; }
			set
			{
				if(pcrBrightnessState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("PCRBrightness.Length must be >0.");

				pcrBrightness=value;
				pcrBrightnessState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region PCRContrast
		EXIF_TAG_STATE pcrContrastState;
		public EXIF_TAG_STATE PCRContrastState
		{
			get { return pcrContrastState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				pcrContrastState=value;
				pcrContrast=null;
			}
		}

		string pcrContrast;
		public string PCRContrast
		{
			get { return pcrContrast; }
			set
			{
				if(pcrContrastState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("PCRContrast.Length must be >0.");

				pcrContrast=value;
				pcrContrastState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region PCRSaturation
		EXIF_TAG_STATE pcrSaturationState;
		public EXIF_TAG_STATE PCRSaturationState
		{
			get { return pcrSaturationState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				pcrSaturationState=value;
				pcrSaturation=null;
			}
		}

		string pcrSaturation;
		public string PCRSaturation
		{
			get { return pcrSaturation; }
			set
			{
				if(pcrSaturationState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("PCRSaturation.Length must be >0.");

				pcrSaturation=value;
				pcrSaturationState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region PCRSharpness
		EXIF_TAG_STATE pcrSharpnessState;
		public EXIF_TAG_STATE PCRSharpnessState
		{
			get { return pcrSharpnessState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				pcrSharpnessState=value;
				pcrSharpness=null;
			}
		}

		string pcrSharpness;
		public string PCRSharpness
		{
			get { return pcrSharpness; }
			set
			{
				if(pcrSharpnessState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("PCRSharpness.Length must be >0.");

				pcrSharpness=value;
				pcrSharpnessState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region PCRSmoothness
		EXIF_TAG_STATE pcrSmoothnessState;
		public EXIF_TAG_STATE PCRSmoothnessState
		{
			get { return pcrSmoothnessState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				pcrSmoothnessState=value;
				pcrSmoothness=null;
			}
		}

		string pcrSmoothness;
		public string PCRSmoothness
		{
			get { return pcrSmoothness; }
			set
			{
				if(pcrSmoothnessState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("PCRSmoothness.Length must be >0.");

				pcrSmoothness=value;
				pcrSmoothnessState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region PCRMoireFilter
		EXIF_TAG_STATE pcrMoireFilterState;
		public EXIF_TAG_STATE PCRMoireFilterState
		{
			get { return pcrMoireFilterState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				pcrMoireFilterState=value;
				pcrMoireFilter=null;
			}
		}

		string pcrMoireFilter;
		public string PCRMoireFilter
		{
			get { return pcrMoireFilter; }
			set
			{
				if(pcrMoireFilterState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("PCRMoireFilter.Length must be >0.");

				pcrMoireFilter=value;
				pcrMoireFilterState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#endregion

		#region GPS IFD Attributes
		/////////////////////////////////////
		// GPS IFD Attributes
		/////////////////////////////////////

		#region GPSVersionID
		EXIF_TAG_STATE gpsVersionIDState;
		public EXIF_TAG_STATE GPSVersionIDState
		{
			get { return gpsVersionIDState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gpsVersionIDState=value;
				gpsVersionID=null;
			}
		}

		byte[] gpsVersionID;
		public byte[] GPSVersionID
		{
			get { return gpsVersionID; }
			set
			{
				if(gpsVersionIDState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length!=4) throw new ArgumentException("GPSVersionID.Length must be 4.");

				gpsVersionID=value;
				gpsVersionIDState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region GPSLatitudeRef
		EXIF_TAG_STATE gpsLatitudeRefState;
		public EXIF_TAG_STATE GPSLatitudeRefState
		{
			get { return gpsLatitudeRefState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gpsLatitudeRefState=value;
				gpsLatitudeRef=' ';
			}
		}

		char gpsLatitudeRef;
		public char GPSLatitudeRef
		{
			get { return gpsLatitudeRef; }
			set
			{
				if(gpsLatitudeRefState==EXIF_TAG_STATE.DontWrite) return;
				if(value!='N'&&value!='S') throw new ArgumentException("GPSLatitudeRef must be 'N' or 'S'.");

				gpsLatitudeRef=value;
				gpsLatitudeRefState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region GPSLatitude
		EXIF_TAG_STATE gpsLatitudeState;
		public EXIF_TAG_STATE GPSLatitudeState
		{
			get { return gpsLatitudeState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gpsLatitudeState=value;
				gpsLatitude=null;
			}
		}

		RATIONAL[] gpsLatitude;
		public RATIONAL[] GPSLatitude
		{
			get { return gpsLatitude; }
			set
			{
				if(gpsLatitudeState==EXIF_TAG_STATE.DontWrite) return;
				if(value.Length!=3) throw new ArgumentException("GPSLatitude.Length must be 3.");

				gpsLatitude=value;
				gpsLatitudeState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region GPSLongitudeRef
		EXIF_TAG_STATE gpsLongitudeRefState;
		public EXIF_TAG_STATE GPSLongitudeRefState
		{
			get { return gpsLongitudeRefState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gpsLongitudeRefState=value;
				gpsLongitudeRef=' ';
			}
		}

		char gpsLongitudeRef;
		public char GPSLongitudeRef
		{
			get { return gpsLongitudeRef; }
			set
			{
				if(gpsLongitudeRefState==EXIF_TAG_STATE.DontWrite) return;
				if(value!='E'&&value!='W') throw new ArgumentException("GPSLongitudeRef must be 'E' or 'W'.");

				gpsLongitudeRef=value;
				gpsLongitudeRefState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region GPSLongitude
		EXIF_TAG_STATE gpsLongitudeState;
		public EXIF_TAG_STATE GPSLongitudeState
		{
			get { return gpsLongitudeState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gpsLongitudeState=value;
				gpsLongitude=null;
			}
		}

		RATIONAL[] gpsLongitude;
		public RATIONAL[] GPSLongitude
		{
			get { return gpsLongitude; }
			set
			{
				if(gpsLongitudeState==EXIF_TAG_STATE.DontWrite) return;
				if(value.Length!=3) throw new ArgumentException("GPSLongitude.Length must be 3.");

				gpsLongitude=value;
				gpsLongitudeState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region GPSAltitudeRef
		EXIF_TAG_STATE gpsAltitudeRefState;
		public EXIF_TAG_STATE GPSAltitudeRefState
		{
			get { return gpsAltitudeRefState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gpsAltitudeRefState=value;
				gpsAltitudeRef=0;
			}
		}

		byte gpsAltitudeRef;
		public byte GPSAltitudeRef
		{
			get { return gpsAltitudeRef; }
			set
			{
				if(gpsAltitudeRefState==EXIF_TAG_STATE.DontWrite) return;
				if(value!=0&&value!=1) throw new ArgumentException("GPSAltitudeRef must be 0 (above sea level) or 1 (below sea level).");

				gpsAltitudeRef=value;
				gpsAltitudeRefState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region GPSAltitude
		EXIF_TAG_STATE gpsAltitudeState;
		public EXIF_TAG_STATE GPSAltitudeState
		{
			get { return gpsAltitudeState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gpsAltitudeState=value;
				gpsAltitude.numerator=0;
				gpsAltitude.denominator=0;
			}
		}

		RATIONAL gpsAltitude;
		public RATIONAL GPSAltitude
		{
			get { return gpsAltitude; }
			set
			{
				if(gpsAltitudeState==EXIF_TAG_STATE.DontWrite) return;
				if(value.denominator==0) throw new ArgumentException("GPSAltitude.denominator must be >0.");

				gpsAltitude=value;
				gpsAltitudeState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region GPSTimeStamp
		EXIF_TAG_STATE gpsTimeStampState;
		public EXIF_TAG_STATE GPSTimeStampState
		{
			get { return gpsTimeStampState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gpsTimeStampState=value;
				gpsTimeStamp=null;
			}
		}

		RATIONAL[] gpsTimeStamp;
		public RATIONAL[] GPSTimeStamp
		{
			get { return gpsTimeStamp; }
			set
			{
				if(gpsTimeStampState==EXIF_TAG_STATE.DontWrite) return;
				if(value.Length!=3) throw new ArgumentException("GPSTimeStamp.Length must be 3.");

				gpsTimeStamp=value;
				gpsTimeStampState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region GPSSatellites
		EXIF_TAG_STATE gpsSatellitesState;
		public EXIF_TAG_STATE GPSSatellitesState
		{
			get { return gpsSatellitesState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gpsSatellitesState=value;
				gpsSatellites=null;
			}
		}

		string gpsSatellites;
		public string GPSSatellites
		{
			get { return gpsSatellites; }
			set
			{
				if(gpsSatellitesState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("GPSSatellites.Length must be >0.");

				gpsSatellites=value;
				gpsSatellitesState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region GPSStatus
		EXIF_TAG_STATE gpsStatusState;
		public EXIF_TAG_STATE GPSStatusState
		{
			get { return gpsStatusState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gpsStatusState=value;
				gpsStatus=' ';
			}
		}

		char gpsStatus;
		public char GPSStatus
		{
			get { return gpsStatus; }
			set
			{
				if(gpsStatusState==EXIF_TAG_STATE.DontWrite) return;
				if(value!='A'&&value!='V') throw new ArgumentException("GPSStatus must be 'A' (Measurement Active) or 'V' (Measurement Void).");

				gpsStatus=value;
				gpsStatusState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region GPSMeasureMode
		EXIF_TAG_STATE gpsMeasureModeState;
		public EXIF_TAG_STATE GPSMeasureModeState
		{
			get { return gpsMeasureModeState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gpsMeasureModeState=value;
				gpsMeasureMode=' ';
			}
		}

		char gpsMeasureMode;
		public char GPSMeasureMode
		{
			get { return gpsMeasureMode; }
			set
			{
				if(gpsMeasureModeState==EXIF_TAG_STATE.DontWrite) return;
				if(value!='2'&&value!='3') throw new ArgumentException("GPSMeasureMode must be '2' or '3'.");

				gpsMeasureMode=value;
				gpsMeasureModeState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region GPSDOP
		EXIF_TAG_STATE gpsDOPState;
		public EXIF_TAG_STATE GPSDOPState
		{
			get { return gpsDOPState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gpsDOPState=value;
				gpsDOP.numerator=0;
				gpsDOP.denominator=0;
			}
		}

		RATIONAL gpsDOP;
		public RATIONAL GPSDOP
		{
			get { return gpsDOP; }
			set
			{
				if(gpsDOPState==EXIF_TAG_STATE.DontWrite) return;
				if(value.denominator==0) throw new ArgumentException("GPSDOP.denominator must be >0.");

				gpsDOP=value;
				gpsDOPState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region GPSSpeedRef
		EXIF_TAG_STATE gpsSpeedRefState;
		public EXIF_TAG_STATE GPSSpeedRefState
		{
			get { return gpsSpeedRefState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gpsSpeedRefState=value;
				gpsSpeedRef=' ';
			}
		}

		char gpsSpeedRef;
		public char GPSSpeedRef
		{
			get { return gpsSpeedRef; }
			set
			{
				if(gpsSpeedRefState==EXIF_TAG_STATE.DontWrite) return;
				if(value!='K'&&value!='M'&&value!='N') throw new ArgumentException("GPSSpeedRef must be 'K' (km/h), 'M' (miles/h) or 'N' (Knots).");

				gpsSpeedRef=value;
				gpsSpeedRefState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region GPSSpeed
		EXIF_TAG_STATE gpsSpeedState;
		public EXIF_TAG_STATE GPSSpeedState
		{
			get { return gpsSpeedState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gpsSpeedState=value;
				gpsSpeed.numerator=0;
				gpsSpeed.denominator=0;
			}
		}

		RATIONAL gpsSpeed;
		public RATIONAL GPSSpeed
		{
			get { return gpsSpeed; }
			set
			{
				if(gpsSpeedState==EXIF_TAG_STATE.DontWrite) return;
				if(value.denominator==0) throw new ArgumentException("GPSSpeed.denominator must be >0.");

				gpsSpeed=value;
				gpsSpeedState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region GPSTrackRef
		EXIF_TAG_STATE gpsTrackRefState;
		public EXIF_TAG_STATE GPSTrackRefState
		{
			get { return gpsTrackRefState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gpsTrackRefState=value;
				gpsTrackRef=' ';
			}
		}

		char gpsTrackRef;
		public char GPSTrackRef
		{
			get { return gpsTrackRef; }
			set
			{
				if(gpsTrackRefState==EXIF_TAG_STATE.DontWrite) return;
				if(value!='T'&&value!='M') throw new ArgumentException("GPSTrackRef must be 'T' (True direction) or 'M' (Magnetic direction).");

				gpsTrackRef=value;
				gpsTrackRefState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region GPSTrack
		EXIF_TAG_STATE gpsTrackState;
		public EXIF_TAG_STATE GPSTrackState
		{
			get { return gpsTrackState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gpsTrackState=value;
				gpsTrack.numerator=0;
				gpsTrack.denominator=0;
			}
		}

		RATIONAL gpsTrack;
		public RATIONAL GPSTrack
		{
			get { return gpsTrack; }
			set
			{
				if(gpsTrackState==EXIF_TAG_STATE.DontWrite) return;
				if(value.denominator==0) throw new ArgumentException("GPSTrack.denominator must be >0.");
				if(value.Value<0||value.Value>=360) throw new ArgumentException("GPSTrack must be 0 to 359.99.");

				gpsTrack=value;
				gpsTrackState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region GPSImgDirectionRef
		EXIF_TAG_STATE gpsImgDirectionRefState;
		public EXIF_TAG_STATE GPSImgDirectionRefState
		{
			get { return gpsImgDirectionRefState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gpsImgDirectionRefState=value;
				gpsImgDirectionRef=' ';
			}
		}

		char gpsImgDirectionRef;
		public char GPSImgDirectionRef
		{
			get { return gpsImgDirectionRef; }
			set
			{
				if(gpsImgDirectionRefState==EXIF_TAG_STATE.DontWrite) return;
				if(value!='T'&&value!='M') throw new ArgumentException("GPSImgDirectionRef must be 'T' (True direction) or 'M' (Magnetic direction).");

				gpsImgDirectionRef=value;
				gpsImgDirectionRefState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region GPSImgDirection
		EXIF_TAG_STATE gpsImgDirectionState;
		public EXIF_TAG_STATE GPSImgDirectionState
		{
			get { return gpsImgDirectionState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gpsImgDirectionState=value;
				gpsImgDirection.numerator=0;
				gpsImgDirection.denominator=0;
			}
		}

		RATIONAL gpsImgDirection;
		public RATIONAL GPSImgDirection
		{
			get { return gpsImgDirection; }
			set
			{
				if(gpsImgDirectionState==EXIF_TAG_STATE.DontWrite) return;
				if(value.denominator==0) throw new ArgumentException("GPSImgDirection.denominator must be >0.");
				if(value.Value<0||value.Value>=360) throw new ArgumentException("GPSImgDirection must be 0 to 359.99.");

				gpsImgDirection=value;
				gpsImgDirectionState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region GPSMapDatum
		EXIF_TAG_STATE gpsMapDatumState;
		public EXIF_TAG_STATE GPSMapDatumState
		{
			get { return gpsMapDatumState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gpsMapDatumState=value;
				gpsMapDatum=null;
			}
		}

		string gpsMapDatum;
		public string GPSMapDatum
		{
			get { return gpsMapDatum; }
			set
			{
				if(gpsMapDatumState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("GPSMapDatum.Length must be >0.");

				gpsMapDatum=value;
				gpsMapDatumState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region GPSDestLatitudeRef
		EXIF_TAG_STATE gpsDestLatitudeRefState;
		public EXIF_TAG_STATE GPSDestLatitudeRefState
		{
			get { return gpsDestLatitudeRefState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gpsDestLatitudeRefState=value;
				gpsDestLatitudeRef=' ';
			}
		}

		char gpsDestLatitudeRef;
		public char GPSDestLatitudeRef
		{
			get { return gpsDestLatitudeRef; }
			set
			{
				if(gpsDestLatitudeRefState==EXIF_TAG_STATE.DontWrite) return;
				if(value!='N'&&value!='S') throw new ArgumentException("GPSDestLatitudeRef must be 'N' or 'S'.");

				gpsDestLatitudeRef=value;
				gpsDestLatitudeRefState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region GPSDestLatitude
		EXIF_TAG_STATE gpsDestLatitudeState;
		public EXIF_TAG_STATE GPSDestLatitudeState
		{
			get { return gpsDestLatitudeState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gpsDestLatitudeState=value;
				gpsDestLatitude=null;
			}
		}

		RATIONAL[] gpsDestLatitude;
		public RATIONAL[] GPSDestLatitude
		{
			get { return gpsDestLatitude; }
			set
			{
				if(gpsDestLatitudeState==EXIF_TAG_STATE.DontWrite) return;
				if(value.Length!=3) throw new ArgumentException("GPSDestLatitude.Length must be 3.");

				gpsDestLatitude=value;
				gpsDestLatitudeState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region GPSDestLongitudeRef
		EXIF_TAG_STATE gpsDestLongitudeRefState;
		public EXIF_TAG_STATE GPSDestLongitudeRefState
		{
			get { return gpsDestLongitudeRefState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gpsDestLongitudeRefState=value;
				gpsDestLongitudeRef=' ';
			}
		}

		char gpsDestLongitudeRef;
		public char GPSDestLongitudeRef
		{
			get { return gpsDestLongitudeRef; }
			set
			{
				if(gpsDestLongitudeRefState==EXIF_TAG_STATE.DontWrite) return;
				if(value!='E'&&value!='W') throw new ArgumentException("GPSDestLongitudeRef must be 'E' or 'W'.");

				gpsDestLongitudeRef=value;
				gpsDestLongitudeRefState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region GPSDestLongitude
		EXIF_TAG_STATE gpsDestLongitudeState;
		public EXIF_TAG_STATE GPSDestLongitudeState
		{
			get { return gpsDestLongitudeState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gpsDestLongitudeState=value;
				gpsDestLongitude=null;
			}
		}

		RATIONAL[] gpsDestLongitude;
		public RATIONAL[] GPSDestLongitude
		{
			get { return gpsDestLongitude; }
			set
			{
				if(gpsDestLongitudeState==EXIF_TAG_STATE.DontWrite) return;
				if(value.Length!=3) throw new ArgumentException("GPSDestLongitude.Length must be 3.");

				gpsDestLongitude=value;
				gpsDestLongitudeState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region GPSDestBearingRef
		EXIF_TAG_STATE gpsDestBearingRefState;
		public EXIF_TAG_STATE GPSDestBearingRefState
		{
			get { return gpsDestBearingRefState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gpsDestBearingRefState=value;
				gpsDestBearingRef=' ';
			}
		}

		char gpsDestBearingRef;
		public char GPSDestBearingRef
		{
			get { return gpsDestBearingRef; }
			set
			{
				if(gpsDestBearingRefState==EXIF_TAG_STATE.DontWrite) return;
				if(value!='T'&&value!='M') throw new ArgumentException("GPSDestBearingRef must be 'T' (True direction) or 'M' (Magnetic direction).");

				gpsDestBearingRef=value;
				gpsDestBearingRefState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region GPSDestBearing
		EXIF_TAG_STATE gpsDestBearingState;
		public EXIF_TAG_STATE GPSDestBearingState
		{
			get { return gpsDestBearingState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gpsDestBearingState=value;
				gpsDestBearing.numerator=0;
				gpsDestBearing.denominator=0;
			}
		}

		RATIONAL gpsDestBearing;
		public RATIONAL GPSDestBearing
		{
			get { return gpsDestBearing; }
			set
			{
				if(gpsDestBearingState==EXIF_TAG_STATE.DontWrite) return;
				if(value.denominator==0) throw new ArgumentException("GPSDestBearing.denominator must be >0.");
				if(value.Value<0||value.Value>=360) throw new ArgumentException("GPSDestBearing must be 0 to 359.99.");

				gpsDestBearing=value;
				gpsDestBearingState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region GPSDestDistanceRef
		EXIF_TAG_STATE gpsDestDistanceRefState;
		public EXIF_TAG_STATE GPSDestDistanceRefState
		{
			get { return gpsDestDistanceRefState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gpsDestDistanceRefState=value;
				gpsDestDistanceRef=' ';
			}
		}

		char gpsDestDistanceRef;
		public char GPSDestDistanceRef
		{
			get { return gpsDestDistanceRef; }
			set
			{
				if(gpsDestDistanceRefState==EXIF_TAG_STATE.DontWrite) return;
				if(value!='K'&&value!='M'&&value!='N') throw new ArgumentException("GPSDestDistanceRef must be 'K' (Kilometers), 'M' (Miles) or 'N' (Nautical Miles).");

				gpsDestDistanceRef=value;
				gpsDestDistanceRefState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region GPSDestDistance
		EXIF_TAG_STATE gpsDestDistanceState;
		public EXIF_TAG_STATE GPSDestDistanceState
		{
			get { return gpsDestDistanceState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gpsDestDistanceState=value;
				gpsDestDistance.numerator=0;
				gpsDestDistance.denominator=0;
			}
		}

		RATIONAL gpsDestDistance;
		public RATIONAL GPSDestDistance
		{
			get { return gpsDestDistance; }
			set
			{
				if(gpsDestDistanceState==EXIF_TAG_STATE.DontWrite) return;
				if(value.denominator==0) throw new ArgumentException("GPSDestDistance.denominator must be >0.");

				gpsDestDistance=value;
				gpsDestDistanceState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion
		
		#region GPSProcessingMethode
		EXIF_TAG_STATE gpsProcessingMethodeState;
		public EXIF_TAG_STATE GPSProcessingMethodeState
		{
			get { return gpsProcessingMethodeState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gpsProcessingMethodeState=value;
				gpsProcessingMethode=null;
			}
		}

		byte[] gpsProcessingMethode;
		public byte[] GPSProcessingMethode
		{
			get { return gpsProcessingMethode; }
			set
			{
				if(gpsProcessingMethodeState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length<=8) throw new ArgumentException("GPSProcessingMethode.Length must be >8.");

				gpsProcessingMethode=value;
				gpsProcessingMethodeState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region GPSAreaInformation
		EXIF_TAG_STATE gpsAreaInformationState;
		public EXIF_TAG_STATE GPSAreaInformationState
		{
			get { return gpsAreaInformationState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gpsAreaInformationState=value;
				gpsAreaInformation=null;
			}
		}

		byte[] gpsAreaInformation;
		public byte[] GPSAreaInformation
		{
			get { return gpsAreaInformation; }
			set
			{
				if(gpsAreaInformationState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length<=8) throw new ArgumentException("GPSAreaInformation.Length must be >8.");

				gpsAreaInformation=value;
				gpsAreaInformationState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region GPSDateStamp
		EXIF_TAG_STATE gpsDateStampState;
		public EXIF_TAG_STATE GPSDateStampState
		{
			get { return gpsDateStampState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gpsDateStampState=value;
				gpsDateStamp=DateTime.Today;
			}
		}

		DateTime gpsDateStamp;
		public DateTime GPSDateStamp
		{
			get { return gpsDateStamp; }
			set
			{
				if(gpsDateStampState==EXIF_TAG_STATE.DontWrite) return;

				gpsDateStamp=value.Date;
				gpsDateStampState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region GPSDifferential
		EXIF_TAG_STATE gpsDifferentialState;
		public EXIF_TAG_STATE GPSDifferentialState
		{
			get { return gpsDifferentialState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				gpsDifferentialState=value;
				gpsDifferential=0;
			}
		}

		ushort gpsDifferential;
		public ushort GPSDifferential
		{
			get { return gpsDifferential; }
			set
			{
				if(gpsDifferentialState==EXIF_TAG_STATE.DontWrite) return;
				if(value!=0&&value!=1) throw new ArgumentException("GPSDifferential must be 0 (w/o differential correction) or 1 (differential correction applied).");

				gpsDifferential=value;
				gpsDifferentialState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#endregion

		#region Interoperability IFD Attributes
		/////////////////////////////////////
		// Interoperability IFD Attributes
		/////////////////////////////////////

		#region InteroperabilityIndex
		EXIF_TAG_STATE interoperabilityIndexState;
		public EXIF_TAG_STATE InteroperabilityIndexState
		{
			get { return interoperabilityIndexState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				interoperabilityIndexState=value;
				interoperabilityIndex=null;
			}
		}

		string interoperabilityIndex;
		public string InteroperabilityIndex
		{
			get { return interoperabilityIndex; }
			set
			{
				if(interoperabilityIndexState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("InteroperabilityIndex.Length must be >0.");

				interoperabilityIndex=value;
				interoperabilityIndexState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		// DCF tags

		#region InteroperabilityVersion
		EXIF_TAG_STATE interoperabilityVersionState;
		public EXIF_TAG_STATE InteroperabilityVersionState
		{
			get { return interoperabilityVersionState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				interoperabilityVersionState=value;
				interoperabilityVersion=null;
			}
		}

		byte[] interoperabilityVersion;
		public byte[] InteroperabilityVersion
		{
			get { return interoperabilityVersion; }
			set
			{
				if(interoperabilityVersionState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length!=4) throw new ArgumentException("InteroperabilityVersion.Length must be 4.");

				interoperabilityVersion=value;
				interoperabilityVersionState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region RelatedImageFileFormat
		EXIF_TAG_STATE relatedImageFileFormatState;
		public EXIF_TAG_STATE RelatedImageFileFormatState
		{
			get { return relatedImageFileFormatState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				relatedImageFileFormatState=value;
				relatedImageFileFormat=null;
			}
		}

		string relatedImageFileFormat;
		public string RelatedImageFileFormat
		{
			get { return relatedImageFileFormat; }
			set
			{
				if(relatedImageFileFormatState==EXIF_TAG_STATE.DontWrite) return;
				if(value==null) throw new ArgumentNullException();
				if(value.Length==0) throw new ArgumentException("RelatedImageFileFormat.Length must be >0.");

				relatedImageFileFormat=value;
				relatedImageFileFormatState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region RelatedImageWidth
		EXIF_TAG_STATE relatedImageWidthState;
		public EXIF_TAG_STATE RelatedImageWidthState
		{
			get { return relatedImageWidthState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				relatedImageWidthState=value;
				relatedImageWidth=0;
			}
		}

		uint relatedImageWidth;
		public uint RelatedImageWidth
		{
			get { return relatedImageWidth; }
			set
			{
				if(relatedImageWidthState==EXIF_TAG_STATE.DontWrite) return;

				relatedImageWidth=value;
				relatedImageWidthState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#region RelatedImageLength
		EXIF_TAG_STATE relatedImageLengthState;
		public EXIF_TAG_STATE RelatedImageLengthState
		{
			get { return relatedImageLengthState; }
			set
			{
				if(value!=EXIF_TAG_STATE.NotSet&&value!=EXIF_TAG_STATE.DontWrite)
					throw new ArgumentException("Only EXIF_TAG_STATE.NotSet or EXIF_TAG_STATE.DontWrite allowed.");

				relatedImageLengthState=value;
				relatedImageLength=0;
			}
		}

		uint relatedImageLength;
		public uint RelatedImageLength
		{
			get { return relatedImageLength; }
			set
			{
				if(relatedImageLengthState==EXIF_TAG_STATE.DontWrite) return;

				relatedImageLength=value;
				relatedImageLengthState=EXIF_TAG_STATE.ReadWrite;
			}
		}
		#endregion

		#endregion
	}
}
