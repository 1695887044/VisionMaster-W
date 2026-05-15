using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Core.Net;
using HslCommunication.Profinet.Beckhoff.Helper;
using HslCommunication.Reflection;

namespace HslCommunication.Profinet.Beckhoff
{
	/// <summary>
	/// 倍福Ads协议的虚拟服务器
	/// </summary>
	public class BeckhoffAdsServer : DeviceServer
	{
		private SoftBuffer mBuffer;

		private SoftBuffer iBuffer;

		private SoftBuffer qBuffer;

		private Dictionary<string, AdsTagItem> adsValues;

		private object dicLock = new object();

		private int memoryAddress;

		private const int DataPoolLength = 65536;

		/// <summary>
		/// 实例化一个基于ADS协议的虚拟的倍福PLC对象，可以用来和<see cref="T:HslCommunication.Profinet.Beckhoff.BeckhoffAdsNet" />进行通信测试。
		/// </summary>
		public BeckhoffAdsServer()
		{
			mBuffer = new SoftBuffer(65536);
			iBuffer = new SoftBuffer(65536);
			qBuffer = new SoftBuffer(65536);
			base.ByteTransform = new RegularByteTransform();
			base.WordLength = 2;
			memoryAddress = 1048576;
			adsValues = new Dictionary<string, AdsTagItem>();
		}

		/// <inheritdoc />
		protected override byte[] SaveToBytes()
		{
			byte[] array = new byte[196608];
			mBuffer.GetBytes().CopyTo(array, 0);
			iBuffer.GetBytes().CopyTo(array, 65536);
			qBuffer.GetBytes().CopyTo(array, 131072);
			return array;
		}

		/// <inheritdoc />
		protected override void LoadFromBytes(byte[] content)
		{
			if (content.Length < 196608)
			{
				throw new Exception("File is not correct");
			}
			mBuffer.SetBytes(content, 0, 65536);
			iBuffer.SetBytes(content, 65536, 65536);
			qBuffer.SetBytes(content, 131072, 65536);
		}

		private int TransAddressOffset(int indexGroup, int address)
		{
			return indexGroup switch
			{
				61472 => (address >= 128000) ? (address - 128000) : address, 
				61473 => (address >= 1024000) ? (address - 1024000) : address, 
				61488 => (address >= 256000) ? (address - 256000) : address, 
				61489 => (address >= 2048000) ? (address - 2048000) : address, 
				_ => address, 
			};
		}

		private OperateResult<byte[]> TagsAction(string tagName, Func<AdsTagItem, int, OperateResult<byte[]>> action)
		{
			lock (dicLock)
			{
				if (adsValues.ContainsKey(tagName))
				{
					return action(adsValues[tagName], 0);
				}
				if (Regex.IsMatch(tagName, "\\[[0-9]+\\]$"))
				{
					int num = tagName.LastIndexOf('[');
					int num2 = int.Parse(tagName.Substring(num + 1).RemoveLast(1));
					tagName = tagName.Substring(0, num);
					if (adsValues.ContainsKey(tagName))
					{
						num = num2 * adsValues[tagName].TypeLength;
						if (num >= adsValues[tagName].Buffer.Length)
						{
							return new OperateResult<byte[]>(StringResources.Language.AllenBradley04);
						}
						return action(adsValues[tagName], num);
					}
				}
				return new OperateResult<byte[]>(StringResources.Language.AllenBradley04);
			}
		}

		private OperateResult<byte[]> ReadTags(string tagName, int length)
		{
			return TagsAction(tagName, (AdsTagItem tag, int index) => OperateResult.CreateSuccessResult(tag.Buffer.SelectMiddle(index, Math.Min(length, tag.Buffer.Length - index))));
		}

		private OperateResult WriteTags(string tagName, byte[] value)
		{
			return TagsAction(tagName, delegate(AdsTagItem tag, int index)
			{
				Array.Copy(value, 0, tag.Buffer, index, Math.Min(value.Length, tag.Buffer.Length - index));
				return OperateResult.CreateSuccessResult(new byte[0]);
			});
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Beckhoff.BeckhoffAdsNet.Read(System.String,System.UInt16)" />
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			OperateResult<uint, uint> operateResult = AdsHelper.AnalysisAddress(address, isBit: false);
			if (!operateResult.IsSuccess)
			{
				return operateResult.ConvertFailed<byte[]>();
			}
			operateResult.Content2 = (uint)TransAddressOffset((int)operateResult.Content1, (int)operateResult.Content2);
			if (operateResult.Content1 == 61443)
			{
				return ReadTags(address.Substring(2), length);
			}
			return operateResult.Content1 switch
			{
				16416u => OperateResult.CreateSuccessResult(mBuffer.GetBytes((int)operateResult.Content2, length)), 
				61472u => OperateResult.CreateSuccessResult(iBuffer.GetBytes((int)operateResult.Content2, length)), 
				61488u => OperateResult.CreateSuccessResult(qBuffer.GetBytes((int)operateResult.Content2, length)), 
				_ => new OperateResult<byte[]>(StringResources.Language.NotSupportedDataType), 
			};
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Beckhoff.BeckhoffAdsNet.Write(System.String,System.Byte[])" />
		[HslMqttApi("WriteByteArray", "")]
		public override OperateResult Write(string address, byte[] value)
		{
			OperateResult<uint, uint> operateResult = AdsHelper.AnalysisAddress(address, isBit: false);
			if (!operateResult.IsSuccess)
			{
				return operateResult.ConvertFailed<byte[]>();
			}
			operateResult.Content2 = (uint)TransAddressOffset((int)operateResult.Content1, (int)operateResult.Content2);
			if (operateResult.Content1 == 61443)
			{
				return WriteTags(address.Substring(2), value);
			}
			switch (operateResult.Content1)
			{
			case 16416u:
				mBuffer.SetBytes(value, (int)operateResult.Content2);
				break;
			case 61472u:
				iBuffer.SetBytes(value, (int)operateResult.Content2);
				break;
			case 61488u:
				qBuffer.SetBytes(value, (int)operateResult.Content2);
				break;
			default:
				return new OperateResult<byte[]>(StringResources.Language.NotSupportedDataType);
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadBool(System.String,System.UInt16)" />
		[HslMqttApi("ReadBoolArray", "")]
		public override OperateResult<bool[]> ReadBool(string address, ushort length)
		{
			OperateResult<uint, uint> operateResult = AdsHelper.AnalysisAddress(address, isBit: true);
			if (!operateResult.IsSuccess)
			{
				return operateResult.ConvertFailed<bool[]>();
			}
			operateResult.Content2 = (uint)TransAddressOffset((int)operateResult.Content1, (int)operateResult.Content2);
			if (operateResult.Content1 == 61443)
			{
				OperateResult<byte[]> operateResult2 = ReadTags(address.Substring(2), length);
				if (!operateResult2.IsSuccess)
				{
					return OperateResult.CreateFailedResult<bool[]>(operateResult2);
				}
				return OperateResult.CreateSuccessResult(operateResult2.Content.Select((byte m) => m != 0).ToArray());
			}
			return operateResult.Content1 switch
			{
				16417u => OperateResult.CreateSuccessResult(mBuffer.GetBool((int)operateResult.Content2, length)), 
				61473u => OperateResult.CreateSuccessResult(iBuffer.GetBool((int)operateResult.Content2, length)), 
				61489u => OperateResult.CreateSuccessResult(qBuffer.GetBool((int)operateResult.Content2, length)), 
				_ => new OperateResult<bool[]>(StringResources.Language.NotSupportedDataType), 
			};
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Boolean[])" />
		[HslMqttApi("WriteBoolArray", "")]
		public override OperateResult Write(string address, bool[] value)
		{
			OperateResult<uint, uint> operateResult = AdsHelper.AnalysisAddress(address, isBit: true);
			if (!operateResult.IsSuccess)
			{
				return operateResult.ConvertFailed<bool[]>();
			}
			operateResult.Content2 = (uint)TransAddressOffset((int)operateResult.Content1, (int)operateResult.Content2);
			if (operateResult.Content1 == 61443)
			{
				return WriteTags(address.Substring(2), value.Select((bool m) => (byte)(m ? 1 : 0)).ToArray());
			}
			switch (operateResult.Content1)
			{
			case 16417u:
				mBuffer.SetBool(value, (int)operateResult.Content2);
				break;
			case 61473u:
				iBuffer.SetBool(value, (int)operateResult.Content2);
				break;
			case 61489u:
				qBuffer.SetBool(value, (int)operateResult.Content2);
				break;
			default:
				return new OperateResult(StringResources.Language.NotSupportedDataType);
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new AdsNetMessage();
		}

		/// <inheritdoc />
		protected override OperateResult<byte[]> ReadFromCoreServer(PipeSession session, byte[] receive)
		{
			return OperateResult.CreateSuccessResult(ReadFromAdsCore(session, receive));
		}

		private byte[] PackCommand(byte[] cmd, int err, byte[] data)
		{
			if (data == null)
			{
				data = new byte[0];
			}
			byte[] array = new byte[32 + data.Length];
			Array.Copy(cmd, 0, array, 0, 32);
			byte[] array2 = array.SelectBegin(8);
			byte[] array3 = array.SelectMiddle(8, 8);
			array2.CopyTo(array, 8);
			array3.CopyTo(array, 0);
			array[18] = 5;
			array[19] = 0;
			BitConverter.GetBytes(data.Length).CopyTo(array, 20);
			BitConverter.GetBytes(err).CopyTo(array, 24);
			array[11] = 0;
			if (data.Length != 0)
			{
				data.CopyTo(array, 32);
			}
			return AdsHelper.PackAmsTcpHelper(AmsTcpHeaderFlags.Command, array);
		}

		private byte[] PackDataResponse(int err, byte[] data)
		{
			if (data != null)
			{
				byte[] array = new byte[8 + data.Length];
				BitConverter.GetBytes(err).CopyTo(array, 0);
				BitConverter.GetBytes(data.Length).CopyTo(array, 4);
				if (data.Length != 0)
				{
					data.CopyTo(array, 8);
				}
				return array;
			}
			return BitConverter.GetBytes(err);
		}

		private byte[] ReadFromAdsCore(PipeSession session, byte[] receive)
		{
			switch (BitConverter.ToUInt16(receive, 0))
			{
			case 0:
				receive = receive.RemoveBegin(6);
				if (session.Tag == null)
				{
					session.Tag = 1;
					base.LogNet?.WriteDebug(ToString(), "TargetId:" + AdsHelper.GetAmsNetIdString(receive, 0) + " SenderId:" + AdsHelper.GetAmsNetIdString(receive, 8));
				}
				return BitConverter.ToInt16(receive, 16) switch
				{
					2 => ReadByCommand(receive), 
					3 => WriteByCommand(receive), 
					9 => ReadWriteByCommand(receive), 
					_ => PackCommand(receive, 32, null), 
				};
			case 4098:
				return AdsHelper.PackAmsTcpHelper(AmsTcpHeaderFlags.GetLocalNetId, AdsHelper.StrToAMSNetId("192.168.163.8.1.1"));
			case 4096:
				return AdsHelper.PackAmsTcpHelper(AmsTcpHeaderFlags.PortConnect, AdsHelper.StrToAMSNetId("192.168.163.9.1.1:32957"));
			default:
				base.LogNet?.WriteDebug(ToString(), "Unknown Source: " + receive.ToHexString(' '));
				return null;
			}
		}

		private OperateResult<byte[]> ReadByCommand(byte[] command, int indexGroup, int address, int length)
		{
			address = TransAddressOffset(indexGroup, address);
			switch (indexGroup)
			{
			case 16416:
				return OperateResult.CreateSuccessResult(mBuffer.GetBytes(address, length));
			case 61472:
				return OperateResult.CreateSuccessResult(iBuffer.GetBytes(address, length));
			case 61488:
				return OperateResult.CreateSuccessResult(qBuffer.GetBytes(address, length));
			case 16417:
				return OperateResult.CreateSuccessResult((from m in mBuffer.GetBool(address, length)
					select (byte)(m ? 1 : 0)).ToArray());
			case 61473:
				return OperateResult.CreateSuccessResult((from m in iBuffer.GetBool(address, length)
					select (byte)(m ? 1 : 0)).ToArray());
			case 61489:
				return OperateResult.CreateSuccessResult((from m in qBuffer.GetBool(address, length)
					select (byte)(m ? 1 : 0)).ToArray());
			case 61445:
			{
				uint num = (uint)address;
				lock (dicLock)
				{
					foreach (AdsTagItem value in adsValues.Values)
					{
						if (value.Location == num)
						{
							return OperateResult.CreateSuccessResult(value.Buffer.SelectBegin(Math.Min(length, value.Buffer.Length)));
						}
					}
					return new OperateResult<byte[]>
					{
						Content = PackCommand(command, 0, PackDataResponse(1808, null))
					};
				}
			}
			default:
				return new OperateResult<byte[]>
				{
					Content = PackCommand(command, 64, null)
				};
			}
		}

		private byte[] ReadByCommand(byte[] command)
		{
			try
			{
				int indexGroup = BitConverter.ToInt32(command, 32);
				int address = BitConverter.ToInt32(command, 36);
				int length = BitConverter.ToInt32(command, 40);
				OperateResult<byte[]> operateResult = ReadByCommand(command, indexGroup, address, length);
				if (!operateResult.IsSuccess)
				{
					return operateResult.Content;
				}
				return PackCommand(command, 0, PackDataResponse(0, operateResult.Content));
			}
			catch
			{
				return PackCommand(command, 164, null);
			}
		}

		private byte[] WriteByCommand(byte[] command)
		{
			if (!base.EnableWrite)
			{
				return PackCommand(command, 16, null);
			}
			try
			{
				int num = BitConverter.ToInt32(command, 32);
				int address = BitConverter.ToInt32(command, 36);
				int num2 = BitConverter.ToInt32(command, 40);
				byte[] array = command.RemoveBegin(44);
				address = TransAddressOffset(num, address);
				switch (num)
				{
				case 16416:
					mBuffer.SetBytes(array, address);
					return PackCommand(command, 0, PackDataResponse(0, null));
				case 61472:
					iBuffer.SetBytes(array, address);
					return PackCommand(command, 0, PackDataResponse(0, null));
				case 61488:
					qBuffer.SetBytes(array, address);
					return PackCommand(command, 0, PackDataResponse(0, null));
				case 16417:
					mBuffer.SetBool(array.Select((byte m) => m != 0).ToArray(), address);
					return PackCommand(command, 0, PackDataResponse(0, null));
				case 61473:
					iBuffer.SetBool(array.Select((byte m) => m != 0).ToArray(), address);
					return PackCommand(command, 0, PackDataResponse(0, null));
				case 61489:
					qBuffer.SetBool(array.Select((byte m) => m != 0).ToArray(), address);
					return PackCommand(command, 0, PackDataResponse(0, null));
				case 61445:
				{
					uint num3 = (uint)address;
					lock (dicLock)
					{
						foreach (AdsTagItem value in adsValues.Values)
						{
							if (value.Location == num3)
							{
								Array.Copy(array, 0, value.Buffer, 0, Math.Min(array.Length, value.Buffer.Length));
								return PackCommand(command, 0, PackDataResponse(0, null));
							}
						}
						return PackCommand(command, 0, PackDataResponse(1808, null));
					}
				}
				default:
					return PackCommand(command, 64, null);
				}
			}
			catch
			{
				return PackCommand(command, 164, null);
			}
		}

		private byte[] ReadWriteByCommand(byte[] command)
		{
			try
			{
				int num = BitConverter.ToInt32(command, 32);
				int destIndex = BitConverter.ToInt32(command, 36);
				int num2 = BitConverter.ToInt32(command, 40);
				int num3 = BitConverter.ToInt32(command, 44);
				byte[] array = command.RemoveBegin(48);
				switch (num)
				{
				case 16416:
					mBuffer.SetBytes(array, destIndex);
					return PackCommand(command, 0, PackDataResponse(0, null));
				case 61472:
					iBuffer.SetBytes(array, destIndex);
					return PackCommand(command, 0, PackDataResponse(0, null));
				case 61488:
					qBuffer.SetBytes(array, destIndex);
					return PackCommand(command, 0, PackDataResponse(0, null));
				case 16417:
					mBuffer.SetBytes(array, destIndex);
					return PackCommand(command, 0, PackDataResponse(0, null));
				case 61473:
					iBuffer.SetBytes(array, destIndex);
					return PackCommand(command, 0, PackDataResponse(0, null));
				case 61489:
					qBuffer.SetBytes(array, destIndex);
					return PackCommand(command, 0, PackDataResponse(0, null));
				case 61443:
				{
					if (array[array.Length - 1] == 0)
					{
						array = array.RemoveLast(1);
					}
					string @string = Encoding.ASCII.GetString(array);
					lock (dicLock)
					{
						if (adsValues.ContainsKey(@string))
						{
							return PackCommand(command, 0, PackDataResponse(0, BitConverter.GetBytes(adsValues[@string].Location)));
						}
						return PackCommand(command, 0, PackDataResponse(1808, null));
					}
				}
				case 61568:
				{
					List<byte> list = new List<byte>();
					for (int i = 0; i < array.Length / 12; i++)
					{
						int indexGroup = BitConverter.ToInt32(array, 12 * i);
						int address = BitConverter.ToInt32(array, 12 * i + 4);
						int length = BitConverter.ToInt32(array, 12 * i + 8);
						OperateResult<byte[]> operateResult = ReadByCommand(command, indexGroup, address, length);
						if (!operateResult.IsSuccess)
						{
							return operateResult.Content;
						}
						list.AddRange(operateResult.Content);
					}
					return PackCommand(command, 0, PackDataResponse(0, list.ToArray()));
				}
				default:
					return PackCommand(command, 64, null);
				}
			}
			catch
			{
				return PackCommand(command, 164, null);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.AllenBradley.AllenBradleyServer.AddTagValue(System.String,HslCommunication.Profinet.AllenBradley.AllenBradleyItemValue)" />
		public void AddTagValue(string key, AdsTagItem value)
		{
			value.Location = (uint)Interlocked.Increment(ref memoryAddress);
			lock (dicLock)
			{
				if (adsValues.ContainsKey(key))
				{
					adsValues[key] = value;
				}
				else
				{
					adsValues.Add(key, value);
				}
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.AllenBradley.AllenBradleyServer.AddTagValue(System.String,HslCommunication.Profinet.AllenBradley.AllenBradleyItemValue)" />
		public void AddTagValue(string key, bool value)
		{
			AddTagValue(key, new AdsTagItem(key, (!value) ? new byte[1] : new byte[1] { 1 }, 1));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.AllenBradley.AllenBradleyServer.AddTagValue(System.String,HslCommunication.Profinet.AllenBradley.AllenBradleyItemValue)" />
		public void AddTagValue(string key, bool[] value)
		{
			AddTagValue(key, new AdsTagItem(key, value.Select((bool m) => (byte)(m ? 1 : 0)).ToArray(), 1));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.AllenBradley.AllenBradleyServer.AddTagValue(System.String,System.Int16)" />
		public void AddTagValue(string key, short value)
		{
			AddTagValue(key, new AdsTagItem(key, base.ByteTransform.TransByte(value), 2));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.AllenBradley.AllenBradleyServer.AddTagValue(System.String,System.Int16[])" />
		public void AddTagValue(string key, short[] value)
		{
			AddTagValue(key, new AdsTagItem(key, base.ByteTransform.TransByte(value), 2));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.AllenBradley.AllenBradleyServer.AddTagValue(System.String,System.UInt16)" />
		public void AddTagValue(string key, ushort value)
		{
			AddTagValue(key, new AdsTagItem(key, base.ByteTransform.TransByte(value), 2));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.AllenBradley.AllenBradleyServer.AddTagValue(System.String,System.UInt16[])" />
		public void AddTagValue(string key, ushort[] value)
		{
			AddTagValue(key, new AdsTagItem(key, base.ByteTransform.TransByte(value), 2));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.AllenBradley.AllenBradleyServer.AddTagValue(System.String,System.Int32)" />
		public void AddTagValue(string key, int value)
		{
			AddTagValue(key, new AdsTagItem(key, base.ByteTransform.TransByte(value), 4));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.AllenBradley.AllenBradleyServer.AddTagValue(System.String,System.Int32[])" />
		public void AddTagValue(string key, int[] value)
		{
			AddTagValue(key, new AdsTagItem(key, base.ByteTransform.TransByte(value), 4));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.AllenBradley.AllenBradleyServer.AddTagValue(System.String,System.UInt32)" />
		public void AddTagValue(string key, uint value)
		{
			AddTagValue(key, new AdsTagItem(key, base.ByteTransform.TransByte(value), 4));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.AllenBradley.AllenBradleyServer.AddTagValue(System.String,System.UInt32[])" />
		public void AddTagValue(string key, uint[] value)
		{
			AddTagValue(key, new AdsTagItem(key, base.ByteTransform.TransByte(value), 4));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.AllenBradley.AllenBradleyServer.AddTagValue(System.String,System.Int64)" />
		public void AddTagValue(string key, long value)
		{
			AddTagValue(key, new AdsTagItem(key, base.ByteTransform.TransByte(value), 8));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.AllenBradley.AllenBradleyServer.AddTagValue(System.String,System.Int64[])" />
		public void AddTagValue(string key, long[] value)
		{
			AddTagValue(key, new AdsTagItem(key, base.ByteTransform.TransByte(value), 8));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.AllenBradley.AllenBradleyServer.AddTagValue(System.String,System.UInt64)" />
		public void AddTagValue(string key, ulong value)
		{
			AddTagValue(key, new AdsTagItem(key, base.ByteTransform.TransByte(value), 8));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.AllenBradley.AllenBradleyServer.AddTagValue(System.String,System.UInt64[])" />
		public void AddTagValue(string key, ulong[] value)
		{
			AddTagValue(key, new AdsTagItem(key, base.ByteTransform.TransByte(value), 8));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.AllenBradley.AllenBradleyServer.AddTagValue(System.String,System.Single)" />
		public void AddTagValue(string key, float value)
		{
			AddTagValue(key, new AdsTagItem(key, base.ByteTransform.TransByte(value), 4));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.AllenBradley.AllenBradleyServer.AddTagValue(System.String,System.Single[])" />
		public void AddTagValue(string key, float[] value)
		{
			AddTagValue(key, new AdsTagItem(key, base.ByteTransform.TransByte(value), 4));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.AllenBradley.AllenBradleyServer.AddTagValue(System.String,System.Double)" />
		public void AddTagValue(string key, double value)
		{
			AddTagValue(key, new AdsTagItem(key, base.ByteTransform.TransByte(value), 8));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.AllenBradley.AllenBradleyServer.AddTagValue(System.String,System.Double[])" />
		public void AddTagValue(string key, double[] value)
		{
			AddTagValue(key, new AdsTagItem(key, base.ByteTransform.TransByte(value), 8));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.AllenBradley.AllenBradleyServer.AddTagValue(System.String,System.String,System.Int32)" />
		public void AddTagValue(string key, string value, int maxLength)
		{
			byte[] buffer = SoftBasic.ArrayExpandToLength(Encoding.UTF8.GetBytes(value), maxLength);
			AddTagValue(key, new AdsTagItem(key, buffer, maxLength));
		}

		/// <inheritdoc />
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				mBuffer.Dispose();
				iBuffer.Dispose();
				qBuffer.Dispose();
			}
			base.Dispose(disposing);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"BeckhoffAdsServer[{base.Port}]";
		}
	}
}
