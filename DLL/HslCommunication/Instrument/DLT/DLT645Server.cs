using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Core.Net;
using HslCommunication.Instrument.DLT.Helper;
using HslCommunication.Reflection;

namespace HslCommunication.Instrument.DLT
{
	/// <summary>
	/// DLT645协议的虚拟服务器
	/// </summary>
	public class DLT645Server : DeviceServer
	{
		private class DLTAddress
		{
			public string Address { get; set; }

			public byte[] Buffer { get; set; }

			/// <summary>
			/// 小数位，小于0则表示字符串
			/// </summary>
			public int Digtal { get; set; }

			public DLTAddress()
			{
			}

			public DLTAddress(string address, byte[] buffer, int digtal)
			{
				Address = address;
				Buffer = buffer;
				Digtal = digtal;
			}
		}

		private Dictionary<string, DLTAddress> allDatas = new Dictionary<string, DLTAddress>();

		private string station = "1";

		/// <inheritdoc cref="P:HslCommunication.Instrument.DLT.DLT645.Station" />
		public string Station
		{
			get
			{
				return station;
			}
			set
			{
				station = value;
			}
		}

		/// <summary>
		/// 字符串的数据是否发生反转，默认为 <c>True</c>, 将根据高地位进行翻转操作<br />
		/// Whether the data of the string is reversed or not, the default is <c>True</c>, and the flip operation will be performed according to the high status
		/// </summary>
		public bool StringReverse { get; set; } = true;


		/// <inheritdoc cref="P:HslCommunication.Instrument.DLT.Helper.IDlt645.EnableCodeFE" />
		public bool EnableCodeFE { get; set; }

		/// <summary>
		/// 是否对客户端及主站的站号进行强制校验，只有两者站号匹配才进行返回数据，默认为false，对站号信息不校验操作<br />
		/// Whether to forcibly verify the station ids of the client and the master station. The data is returned only when the station ids of the client and master station match. The default value is false
		/// </summary>
		public bool StationMatch { get; set; } = false;


		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		public DLT645Server()
		{
			AddDltTag("02010100", "2311", 1);
			AddDltTag("02010200", "2312", 1);
			AddDltTag("02010300", "2313", 1);
			AddDltTag("02020100", "012345", 3);
			AddDltTag("02020200", "012346", 3);
			AddDltTag("02020300", "012347", 3);
			AddDltTag("02030000", "123456", 4);
			AddDltTag("02030100", "123457", 4);
			AddDltTag("02030200", "123458", 4);
			AddDltTag("02030300", "123459", 4);
			AddDltTag("02040000", "654321", 4);
			AddDltTag("02040100", "654322", 4);
			AddDltTag("02040200", "654323", 4);
			AddDltTag("02040300", "654324", 4);
			AddDltTag("02050000", "553322", 4);
			AddDltTag("02050100", "553323", 4);
			AddDltTag("02050200", "553324", 4);
			AddDltTag("02050300", "553325", 4);
			AddDltTag("02060000", "1236", 3);
			AddDltTag("02060100", "1237", 3);
			AddDltTag("02060200", "1238", 3);
			AddDltTag("02060300", "1239", 3);
			AddDltTag("02070100", "2345", 1);
			AddDltTag("02070200", "2346", 1);
			AddDltTag("02070300", "2347", 1);
			AddDltTag("02080001", "123789", 3);
			AddDltTag("02080002", "5012", 2);
			AddDltTag("02080003", "112233", 4);
			AddDltTag("02080004", "223344", 4);
			AddDltTag("02080005", "223355", 4);
			AddDltTag("02080006", "223366", 4);
			AddDltTag("02080007", "2312", 1);
			AddDltTag("02080008", "1221", 2);
			AddDltTag("02080009", "1223", 2);
			AddDltTag("0208000A", "11223344", 0);
			AddDltTag("00000000", "12345612", 2);
			AddDltTag("00010000", "12345613", 2);
			AddDltTag("00020000", "12345614", 2);
			AddDltTag("00030000", "12345615", 2);
			AddDltTag("04000101", "23110904", -1);
			AddDltTag("04000102", DateTime.Now.ToString("HH:mm:ss").Replace(":", ""), -1);
			AddDltTag("04000401", "112233665544", -1);
			AddDltTag("04000402", "552233665544", -1);
			AddDltAsciiTag("0400040B", "DLTServer ", -1);
			AddDltAsciiTag("0400040C", "2023111101 ", -1);
		}

		private void AddDltTag(string address, string hex, int digtal)
		{
			allDatas.Add(address, new DLTAddress(address, hex.ToHexBytes(), digtal));
		}

		private void AddDltAsciiTag(string address, string ascii, int digtal)
		{
			allDatas.Add(address, new DLTAddress(address, Encoding.ASCII.GetBytes(ascii), digtal));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadDouble(System.String,System.UInt16)" />
		[HslMqttApi("ReadDoubleArray", "")]
		public override OperateResult<double[]> ReadDouble(string address, ushort length)
		{
			OperateResult<string[]> operateResult = ReadStringArray(address);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<double[]>(operateResult);
			}
			try
			{
				return OperateResult.CreateSuccessResult((from m in operateResult.Content.Take(length)
					select double.Parse(m)).ToArray());
			}
			catch (Exception ex)
			{
				return new OperateResult<double[]>("double.Parse failed: " + ex.Message + Environment.NewLine + "Source: " + operateResult.Content.ToArrayString());
			}
		}

		/// <inheritdoc />
		public override OperateResult<string> ReadString(string address, ushort length, Encoding encoding)
		{
			return ByteTransformHelper.GetResultFromArray(ReadStringArray(address));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Double)" />
		[HslMqttApi("WriteDouble", "")]
		public override OperateResult Write(string address, double value)
		{
			OperateResult<string, byte[]> operateResult = DLT645Helper.AnalysisBytesAddress(DLT645Type.DLT2007, address, Station, 1);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string[]>(operateResult);
			}
			string key = operateResult.Content2.Reverse().ToArray().ToHexString();
			if (allDatas.ContainsKey(key))
			{
				value *= Math.Pow(10.0, allDatas[key].Digtal);
				string text = Convert.ToInt32(value).ToString().PadLeft(allDatas[key].Buffer.Length * 2, '0');
				if (text.Length > allDatas[key].Buffer.Length * 2)
				{
					text = text.Substring(text.Length - allDatas[key].Buffer.Length * 2);
				}
				text.ToHexBytes().CopyTo(allDatas[key].Buffer, 0);
				return OperateResult.CreateSuccessResult();
			}
			return new OperateResult<string[]>("None address data");
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.String,System.Text.Encoding)" />
		public override OperateResult Write(string address, string value, Encoding encoding)
		{
			OperateResult<string, byte[]> operateResult = DLT645Helper.AnalysisBytesAddress(DLT645Type.DLT2007, address, Station, 1);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string[]>(operateResult);
			}
			string key = operateResult.Content2.Reverse().ToArray().ToHexString();
			if (allDatas.ContainsKey(key))
			{
				byte[] array = value.PadLeft(allDatas[key].Buffer.Length * 2, '0').ToHexBytes();
				if (array.Length > allDatas[key].Buffer.Length)
				{
					array = array.SelectBegin(allDatas[key].Buffer.Length);
				}
				array.CopyTo(allDatas[key].Buffer, 0);
				return OperateResult.CreateSuccessResult();
			}
			return new OperateResult<string[]>("None address data");
		}

		/// <summary>
		/// 根据指定的地址，读取DLT设备的字符串格式的数据信息<br />
		/// Read the DLT device data in string format based on the specified address
		/// </summary>
		/// <param name="address">地址信息</param>
		/// <returns>包含字符串结果数组的对象</returns>
		public OperateResult<string[]> ReadStringArray(string address)
		{
			bool reverse = HslHelper.ExtractBooleanParameter(ref address, "reverse", defaultValue: true);
			OperateResult<string, byte[]> operateResult = DLT645Helper.AnalysisBytesAddress(DLT645Type.DLT2007, address, Station, 1);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string[]>(operateResult);
			}
			string key = operateResult.Content2.Reverse().ToArray().ToHexString();
			if (allDatas.ContainsKey(key))
			{
				return DLTTransform.TransStringsFromDLt(DLT645Type.DLT2007, allDatas[key].Buffer.Reverse().ToArray().EveryByteAdd(51), operateResult.Content2, reverse);
			}
			return new OperateResult<string[]>("None address data");
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new DLT645Message();
		}

		/// <inheritdoc />
		protected override OperateResult<byte[]> ReadFromCoreServer(PipeSession session, byte[] receive)
		{
			if (receive.Length == 4 && receive[0] == 254 && receive[1] == 254 && receive[2] == 254 && receive[3] == 254)
			{
				return OperateResult.CreateSuccessResult(new byte[0]);
			}
			if (receive.Length < 5)
			{
				return new OperateResult<byte[]>("Uknown message");
			}
			int num = DLT645Helper.FindHeadCode68H(receive);
			if (num < 0)
			{
				return new OperateResult<byte[]>("Uknown message: " + receive.ToHexString(' '));
			}
			if (num > 0)
			{
				receive = receive.RemoveBegin(num);
			}
			return ReadFromDLTCore(receive);
		}

		private OperateResult CheckStationMatch(byte[] command)
		{
			if (CheckAddress(command, 170))
			{
				return OperateResult.CreateSuccessResult();
			}
			if (!StationMatch)
			{
				return OperateResult.CreateSuccessResult();
			}
			string text = command.SelectMiddle(1, 6).ReverseNew().ToHexString();
			try
			{
				if (Convert.ToInt32(text) != Convert.ToInt32(station))
				{
					return new OperateResult("Station not match, need[" + station + "] actual[" + text + "]");
				}
				return OperateResult.CreateSuccessResult();
			}
			catch
			{
				return OperateResult.CreateSuccessResult();
			}
		}

		private bool CheckAddress(byte[] command, byte match)
		{
			for (int i = 1; i < 7; i++)
			{
				if (command[i] != match)
				{
					return false;
				}
			}
			return true;
		}

		private OperateResult<byte[]> CreateResponseBack(byte err, byte[] command, byte[] data, bool withAddress)
		{
			if (err == 0 && withAddress)
			{
				data = SoftBasic.SpliceArray<byte>(command.SelectMiddle(10, 4).EveryByteAdd(-51), data);
			}
			byte[] array = null;
			string address = station;
			if (!CheckAddress(command, 170))
			{
				address = command.SelectMiddle(1, 6).ReverseNew().ToHexString();
			}
			array = ((err <= 0) ? DLT645Helper.BuildDlt645EntireCommand(address, (byte)(command[8] + 128), data).Content : DLT645Helper.BuildDlt645EntireCommand(address, (byte)(command[8] + 192), new byte[1] { err }).Content);
			if (EnableCodeFE)
			{
				array = SoftBasic.SpliceArray<byte>(new byte[4] { 254, 254, 254, 254 }, array);
			}
			return OperateResult.CreateSuccessResult(array);
		}

		private OperateResult<byte[]> ReadFromDLTCore(byte[] receive)
		{
			if (receive[8] == 17)
			{
				OperateResult operateResult = CheckStationMatch(receive);
				if (!operateResult.IsSuccess)
				{
					return OperateResult.CreateFailedResult<byte[]>(operateResult);
				}
				string key = receive.SelectMiddle(10, 4).EveryByteAdd(-51).Reverse()
					.ToArray()
					.ToHexString();
				if (allDatas.ContainsKey(key))
				{
					if (allDatas[key].Digtal < 0 && !StringReverse)
					{
						return CreateResponseBack(0, receive, allDatas[key].Buffer.Reverse().ToArray(), withAddress: true);
					}
					return CreateResponseBack(0, receive, allDatas[key].Buffer.Reverse().ToArray(), withAddress: true);
				}
				return CreateResponseBack(2, receive, null, withAddress: true);
			}
			if (receive[8] == 19)
			{
				return CreateResponseBack(0, receive, station.PadLeft(12, '0').ToHexBytes(), withAddress: false);
			}
			if (receive[8] == 20)
			{
				OperateResult operateResult2 = CheckStationMatch(receive);
				if (!operateResult2.IsSuccess)
				{
					return OperateResult.CreateFailedResult<byte[]>(operateResult2);
				}
				string key2 = receive.SelectMiddle(10, 4).EveryByteAdd(-51).Reverse()
					.ToArray()
					.ToHexString();
				if (allDatas.ContainsKey(key2))
				{
					byte[] source = receive.SelectMiddle(22, receive.Length - 24);
					source.Reverse().ToArray().EveryByteAdd(-51)
						.CopyTo(allDatas[key2].Buffer, 0);
					return CreateResponseBack(0, receive, new byte[0], withAddress: false);
				}
				return CreateResponseBack(2, receive, null, withAddress: false);
			}
			if (receive[8] == 21)
			{
				OperateResult operateResult3 = CheckStationMatch(receive);
				if (!operateResult3.IsSuccess)
				{
					return OperateResult.CreateFailedResult<byte[]>(operateResult3);
				}
				string text = receive.SelectMiddle(10, 6).EveryByteAdd(-51).ReverseNew()
					.ToHexString();
				base.LogNet?.WriteInfo(ToString(), "Station change, " + station + " -> " + text);
				station = text;
				return CreateResponseBack(0, receive, new byte[0], withAddress: false);
			}
			if (receive[8] == 8)
			{
				string text2 = receive.SelectMiddle(13, 3).EveryByteAdd(-51).Reverse()
					.ToArray()
					.ToHexString();
				Write("04000101", text2 + "00");
				string value = receive.SelectMiddle(10, 3).EveryByteAdd(-51).Reverse()
					.ToArray()
					.ToHexString();
				Write("04000102", value);
				return new OperateResult<byte[]>
				{
					ErrorCode = int.MinValue
				};
			}
			if (receive[8] == 22)
			{
				base.LogNet?.WriteDebug(ToString(), "FreezeCommand: " + receive.SelectMiddle(10, 4).ToHexString(' '));
				if (receive[1] != 153 || receive[2] != 153 || receive[3] != 153 || receive[4] != 153 || receive[5] != 153 || receive[6] != 153)
				{
					return CreateResponseBack(0, receive, new byte[0], withAddress: false);
				}
			}
			else if (receive[8] == 28)
			{
				return CreateResponseBack(0, receive, new byte[0], withAddress: false);
			}
			return CreateResponseBack(2, receive, null, withAddress: false);
		}

		/// <inheritdoc />
		protected override bool CheckSerialReceiveDataComplete(byte[] buffer, int receivedLength)
		{
			MemoryStream ms = new MemoryStream();
			ms.Write(buffer.SelectBegin(receivedLength));
			return DLT645Helper.CheckReceiveDataComplete(ms);
		}

		/// <inheritdoc />
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
			}
			base.Dispose(disposing);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"DLT645Server[{base.Port}]";
		}
	}
}
