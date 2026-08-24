using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Address;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Core.Net;
using HslCommunication.Reflection;

namespace HslCommunication.Profinet.Keyence
{
	/// <summary>
	/// 基恩士的上位链路协议的虚拟服务器
	/// </summary>
	public class KeyenceNanoServer : DeviceServer
	{
		private SoftBuffer rBuffer;

		private SoftBuffer bBuffer;

		private SoftBuffer mrBuffer;

		private SoftBuffer lrBuffer;

		private SoftBuffer crBuffer;

		private SoftBuffer vbBuffer;

		private SoftBuffer dmBuffer;

		private SoftBuffer emBuffer;

		private SoftBuffer wBuffer;

		private SoftBuffer atBuffer;

		private const int DataPoolLength = 65536;

		/// <summary>
		/// 实例化一个基于上位链路协议的虚拟的基恩士PLC对象，可以用来和<see cref="T:HslCommunication.Profinet.Keyence.KeyenceNanoSerialOverTcp" />进行通信测试。
		/// </summary>
		public KeyenceNanoServer()
		{
			rBuffer = new SoftBuffer(65536);
			bBuffer = new SoftBuffer(65536);
			mrBuffer = new SoftBuffer(65536);
			lrBuffer = new SoftBuffer(65536);
			crBuffer = new SoftBuffer(65536);
			vbBuffer = new SoftBuffer(65536);
			dmBuffer = new SoftBuffer(131072);
			emBuffer = new SoftBuffer(131072);
			wBuffer = new SoftBuffer(131072);
			atBuffer = new SoftBuffer(65536);
			base.ByteTransform = new RegularByteTransform();
			base.ByteTransform.IsStringReverseByteWord = true;
			base.WordLength = 1;
			LogMsgFormatBinary = false;
		}

		/// <inheritdoc />
		protected override byte[] SaveToBytes()
		{
			byte[] array = new byte[851968];
			rBuffer.GetBytes().CopyTo(array, 0);
			bBuffer.GetBytes().CopyTo(array, 65536);
			mrBuffer.GetBytes().CopyTo(array, 131072);
			lrBuffer.GetBytes().CopyTo(array, 196608);
			crBuffer.GetBytes().CopyTo(array, 262144);
			vbBuffer.GetBytes().CopyTo(array, 327680);
			dmBuffer.GetBytes().CopyTo(array, 393216);
			emBuffer.GetBytes().CopyTo(array, 524288);
			wBuffer.GetBytes().CopyTo(array, 655360);
			atBuffer.GetBytes().CopyTo(array, 786432);
			return array;
		}

		/// <inheritdoc />
		protected override void LoadFromBytes(byte[] content)
		{
			if (content.Length < 851968)
			{
				throw new Exception("File is not correct");
			}
			rBuffer.SetBytes(content, 0, 65536);
			bBuffer.SetBytes(content, 65536, 65536);
			mrBuffer.SetBytes(content, 131072, 65536);
			lrBuffer.SetBytes(content, 196608, 65536);
			crBuffer.SetBytes(content, 262144, 65536);
			vbBuffer.SetBytes(content, 327680, 65536);
			dmBuffer.SetBytes(content, 393216, 131072);
			emBuffer.SetBytes(content, 524288, 131072);
			wBuffer.SetBytes(content, 655360, 131072);
			atBuffer.SetBytes(content, 786432, 65536);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceNanoSerialOverTcp.Read(System.String,System.UInt16)" />
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			OperateResult<KeyenceNanoAddress> operateResult = KeyenceNanoAddress.ParseFrom(address, length);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			try
			{
				if (address.StartsWith("DM"))
				{
					return OperateResult.CreateSuccessResult(dmBuffer.GetBytes(operateResult.Content.AddressStart * 2, length * 2));
				}
				if (address.StartsWith("EM"))
				{
					return OperateResult.CreateSuccessResult(emBuffer.GetBytes(operateResult.Content.AddressStart * 2, length * 2));
				}
				if (address.StartsWith("W"))
				{
					return OperateResult.CreateSuccessResult(wBuffer.GetBytes(operateResult.Content.AddressStart * 2, length * 2));
				}
				if (address.StartsWith("AT"))
				{
					return OperateResult.CreateSuccessResult(atBuffer.GetBytes(operateResult.Content.AddressStart * 4, length * 4));
				}
				return new OperateResult<byte[]>(StringResources.Language.NotSupportedDataType);
			}
			catch (Exception ex)
			{
				return new OperateResult<byte[]>(StringResources.Language.NotSupportedDataType + " Reason:" + ex.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceNanoSerialOverTcp.Write(System.String,System.Byte[])" />
		[HslMqttApi("WriteByteArray", "")]
		public override OperateResult Write(string address, byte[] value)
		{
			OperateResult<KeyenceNanoAddress> operateResult = KeyenceNanoAddress.ParseFrom(address, 0);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			try
			{
				if (address.StartsWith("DM"))
				{
					dmBuffer.SetBytes(value, operateResult.Content.AddressStart * 2);
				}
				else if (address.StartsWith("EM"))
				{
					emBuffer.SetBytes(value, operateResult.Content.AddressStart * 2);
				}
				else if (address.StartsWith("W"))
				{
					wBuffer.SetBytes(value, operateResult.Content.AddressStart * 2);
				}
				else
				{
					if (!address.StartsWith("AT"))
					{
						return new OperateResult<byte[]>(StringResources.Language.NotSupportedDataType);
					}
					atBuffer.SetBytes(value, operateResult.Content.AddressStart * 4);
				}
				return OperateResult.CreateSuccessResult();
			}
			catch (Exception ex)
			{
				return new OperateResult<byte[]>(StringResources.Language.NotSupportedDataType + " Reason:" + ex.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadBool(System.String,System.UInt16)" />
		[HslMqttApi("ReadBoolArray", "")]
		public override OperateResult<bool[]> ReadBool(string address, ushort length)
		{
			OperateResult<KeyenceNanoAddress> operateResult = KeyenceNanoAddress.ParseFrom(address, 0);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(operateResult);
			}
			try
			{
				if (address.StartsWith("R"))
				{
					return OperateResult.CreateSuccessResult((from m in rBuffer.GetBytes(operateResult.Content.AddressStart, length)
						select m != 0).ToArray());
				}
				if (address.StartsWith("B"))
				{
					return OperateResult.CreateSuccessResult((from m in bBuffer.GetBytes(operateResult.Content.AddressStart, length)
						select m != 0).ToArray());
				}
				if (address.StartsWith("MR"))
				{
					return OperateResult.CreateSuccessResult((from m in mrBuffer.GetBytes(operateResult.Content.AddressStart, length)
						select m != 0).ToArray());
				}
				if (address.StartsWith("LR"))
				{
					return OperateResult.CreateSuccessResult((from m in lrBuffer.GetBytes(operateResult.Content.AddressStart, length)
						select m != 0).ToArray());
				}
				if (address.StartsWith("CR"))
				{
					return OperateResult.CreateSuccessResult((from m in crBuffer.GetBytes(operateResult.Content.AddressStart, length)
						select m != 0).ToArray());
				}
				if (address.StartsWith("VB"))
				{
					return OperateResult.CreateSuccessResult((from m in vbBuffer.GetBytes(operateResult.Content.AddressStart, length)
						select m != 0).ToArray());
				}
				return new OperateResult<bool[]>(StringResources.Language.NotSupportedDataType);
			}
			catch (Exception ex)
			{
				return new OperateResult<bool[]>(StringResources.Language.NotSupportedDataType + " Reason:" + ex.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Boolean[])" />
		[HslMqttApi("WriteBoolArray", "")]
		public override OperateResult Write(string address, bool[] value)
		{
			OperateResult<KeyenceNanoAddress> operateResult = KeyenceNanoAddress.ParseFrom(address, 0);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(operateResult);
			}
			try
			{
				byte[] data = value.Select((bool m) => (byte)(m ? 1 : 0)).ToArray();
				if (address.StartsWith("R"))
				{
					rBuffer.SetBytes(data, operateResult.Content.AddressStart);
				}
				else if (address.StartsWith("B"))
				{
					bBuffer.SetBytes(data, operateResult.Content.AddressStart);
				}
				else if (address.StartsWith("MR"))
				{
					mrBuffer.SetBytes(data, operateResult.Content.AddressStart);
				}
				else if (address.StartsWith("LR"))
				{
					lrBuffer.SetBytes(data, operateResult.Content.AddressStart);
				}
				else if (address.StartsWith("CR"))
				{
					crBuffer.SetBytes(data, operateResult.Content.AddressStart);
				}
				else
				{
					if (!address.StartsWith("VB"))
					{
						return new OperateResult<bool[]>(StringResources.Language.NotSupportedDataType);
					}
					vbBuffer.SetBytes(data, operateResult.Content.AddressStart);
				}
				return OperateResult.CreateSuccessResult();
			}
			catch (Exception ex)
			{
				return new OperateResult<bool[]>(StringResources.Language.NotSupportedDataType + " Reason:" + ex.Message);
			}
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new SpecifiedCharacterMessage(13);
		}

		/// <inheritdoc />
		protected override OperateResult<byte[]> ReadFromCoreServer(PipeSession session, byte[] receive)
		{
			return OperateResult.CreateSuccessResult(ReadFromNanoCore(receive));
		}

		private byte[] GetBoolResponseData(byte[] data)
		{
			StringBuilder stringBuilder = new StringBuilder();
			for (int i = 0; i < data.Length; i++)
			{
				stringBuilder.Append(data[i]);
				if (i != data.Length - 1)
				{
					stringBuilder.Append(" ");
				}
			}
			stringBuilder.Append("\r\n");
			return Encoding.ASCII.GetBytes(stringBuilder.ToString());
		}

		private byte[] GetWordResponseData(byte[] data)
		{
			StringBuilder stringBuilder = new StringBuilder();
			for (int i = 0; i < data.Length / 2; i++)
			{
				stringBuilder.Append(BitConverter.ToUInt16(data, i * 2));
				if (i != data.Length / 2 - 1)
				{
					stringBuilder.Append(" ");
				}
			}
			stringBuilder.Append("\r\n");
			return Encoding.ASCII.GetBytes(stringBuilder.ToString());
		}

		private byte[] GetDoubleWordResponseData(byte[] data)
		{
			StringBuilder stringBuilder = new StringBuilder();
			for (int i = 0; i < data.Length / 4; i++)
			{
				stringBuilder.Append(BitConverter.ToUInt32(data, i * 4));
				if (i != data.Length / 4 - 1)
				{
					stringBuilder.Append(" ");
				}
			}
			stringBuilder.Append("\r\n");
			return Encoding.ASCII.GetBytes(stringBuilder.ToString());
		}

		private byte[] ReadFromNanoCore(byte[] receive)
		{
			string[] array = Encoding.ASCII.GetString(receive).Trim('\r', '\n').Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			if (array[0] == "CR")
			{
				return Encoding.ASCII.GetBytes("CC\r\n");
			}
			if (array[0] == "CQ")
			{
				return Encoding.ASCII.GetBytes("CF\r\n");
			}
			if (array[0] == "ER")
			{
				return Encoding.ASCII.GetBytes("OK\r\n");
			}
			if (array[0] == "RD" || array[0] == "RDS")
			{
				return ReadByCommand(array);
			}
			if (array[0] == "WR" || array[0] == "WRS")
			{
				return WriteByCommand(array);
			}
			if (array[0] == "ST")
			{
				return WriteByCommand(new string[4]
				{
					"WRS",
					array[1],
					"1",
					"1"
				});
			}
			if (array[0] == "RS")
			{
				return WriteByCommand(new string[4]
				{
					"WRS",
					array[1],
					"1",
					"0"
				});
			}
			if (array[0] == "?K")
			{
				return Encoding.ASCII.GetBytes("53\r\n");
			}
			if (array[0] == "?M")
			{
				return Encoding.ASCII.GetBytes("1\r\n");
			}
			return Encoding.ASCII.GetBytes("E0\r\n");
		}

		private byte[] ReadByCommand(string[] command)
		{
			try
			{
				if (command[1].EndsWith(new string[5] { ".U", ".S", ".D", ".L", ".H" }))
				{
					command[1] = command[1].Remove(command[1].Length - 2);
				}
				int num = ((command.Length <= 2) ? 1 : int.Parse(command[2]));
				if (Regex.IsMatch(command[1], "^[0-9]+$"))
				{
					command[1] = "R" + command[1];
				}
				OperateResult<KeyenceNanoAddress> operateResult = KeyenceNanoAddress.ParseFrom(command[1], (ushort)num);
				if (!operateResult.IsSuccess)
				{
					return Encoding.ASCII.GetBytes("E0\r\n");
				}
				KeyenceNanoAddress content = operateResult.Content;
				if (num > 1000)
				{
					return Encoding.ASCII.GetBytes("E0\r\n");
				}
				switch (content.DataCode)
				{
				case "":
				case "R":
					return GetBoolResponseData(rBuffer.GetBytes(content.AddressStart, num));
				case "B":
					return GetBoolResponseData(bBuffer.GetBytes(content.AddressStart, num));
				case "MR":
					return GetBoolResponseData(mrBuffer.GetBytes(content.AddressStart, num));
				case "LR":
					return GetBoolResponseData(lrBuffer.GetBytes(content.AddressStart, num));
				case "CR":
					return GetBoolResponseData(crBuffer.GetBytes(content.AddressStart, num));
				case "VB":
					return GetBoolResponseData(vbBuffer.GetBytes(content.AddressStart, num));
				case "DM":
					return GetWordResponseData(dmBuffer.GetBytes(content.AddressStart * 2, num * 2));
				case "EM":
					return GetWordResponseData(emBuffer.GetBytes(content.AddressStart * 2, num * 2));
				case "W":
					return GetWordResponseData(wBuffer.GetBytes(content.AddressStart * 2, num * 2));
				case "AT":
					return GetDoubleWordResponseData(atBuffer.GetBytes(content.AddressStart * 4, num * 4));
				default:
					return Encoding.ASCII.GetBytes("E0\r\n");
				}
			}
			catch
			{
				return Encoding.ASCII.GetBytes("E1\r\n");
			}
		}

		private byte[] WriteByCommand(string[] command)
		{
			if (!base.EnableWrite)
			{
				return Encoding.ASCII.GetBytes("E4\r\n");
			}
			try
			{
				if (command[1].EndsWith(new string[5] { ".U", ".S", ".D", ".L", ".H" }))
				{
					command[1] = command[1].Remove(command[1].Length - 2);
				}
				int num = ((!(command[0] == "WRS")) ? 1 : int.Parse(command[2]));
				if (Regex.IsMatch(command[1], "^[0-9]+$"))
				{
					command[1] = "R" + command[1];
				}
				OperateResult<KeyenceNanoAddress> operateResult = KeyenceNanoAddress.ParseFrom(command[1], (ushort)num);
				if (!operateResult.IsSuccess)
				{
					return Encoding.ASCII.GetBytes("E0\r\n");
				}
				KeyenceNanoAddress content = operateResult.Content;
				if (num > 1000)
				{
					return Encoding.ASCII.GetBytes("E0\r\n");
				}
				if (command[1].StartsWith("R") || command[1].StartsWith("B") || command[1].StartsWith("MR") || command[1].StartsWith("LR") || command[1].StartsWith("CR") || command[1].StartsWith("VB"))
				{
					byte[] data = (from m in command.RemoveBegin((command[0] == "WRS") ? 3 : 2)
						select byte.Parse(m)).ToArray();
					if (command[1].StartsWith("R"))
					{
						rBuffer.SetBytes(data, content.AddressStart);
					}
					else if (command[1].StartsWith("B"))
					{
						bBuffer.SetBytes(data, content.AddressStart);
					}
					else if (command[1].StartsWith("MR"))
					{
						mrBuffer.SetBytes(data, content.AddressStart);
					}
					else if (command[1].StartsWith("LR"))
					{
						lrBuffer.SetBytes(data, content.AddressStart);
					}
					else if (command[1].StartsWith("CR"))
					{
						crBuffer.SetBytes(data, content.AddressStart);
					}
					else
					{
						if (!command[1].StartsWith("VB"))
						{
							return Encoding.ASCII.GetBytes("E0\r\n");
						}
						vbBuffer.SetBytes(data, content.AddressStart);
					}
				}
				else
				{
					byte[] data2 = base.ByteTransform.TransByte((from m in command.RemoveBegin((command[0] == "WRS") ? 3 : 2)
						select ushort.Parse(m)).ToArray());
					if (command[1].StartsWith("DM"))
					{
						dmBuffer.SetBytes(data2, content.AddressStart * 2);
					}
					else if (command[1].StartsWith("EM"))
					{
						emBuffer.SetBytes(data2, content.AddressStart * 2);
					}
					else if (command[1].StartsWith("W"))
					{
						wBuffer.SetBytes(data2, content.AddressStart * 2);
					}
					else
					{
						if (!command[1].StartsWith("AT"))
						{
							return Encoding.ASCII.GetBytes("E0\r\n");
						}
						atBuffer.SetBytes(data2, content.AddressStart * 4);
					}
				}
				return Encoding.ASCII.GetBytes("OK\r\n");
			}
			catch
			{
				return Encoding.ASCII.GetBytes("E1\r\n");
			}
		}

		/// <inheritdoc />
		protected override bool CheckSerialReceiveDataComplete(byte[] buffer, int receivedLength)
		{
			if (receivedLength < 1)
			{
				return false;
			}
			return buffer[receivedLength - 1] == 13;
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"KeyenceNanoServer[{base.Port}]";
		}
	}
}
