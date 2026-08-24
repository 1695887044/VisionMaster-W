using System;
using System.Linq;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Address;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Core.Net;
using HslCommunication.Profinet.YASKAWA.Helper;
using HslCommunication.Reflection;

namespace HslCommunication.Profinet.YASKAWA
{
	/// <summary>
	/// 基于扩展memobus协议的虚拟服务器，支持线圈，输入线圈，保持型寄存器，输入寄存器，保持型寄存器（扩展），输入寄存器（扩展）的读写。对于远程客户端来说，还支持对扩展保持寄存器的随机字读取和写入操作。<br />
	/// A virtual server based on the extended memobus protocol, which supports the reading and writing of coils, input coils, holding registers, input registers, 
	/// holding registers (expansion), input registers (expansion). For remote clients, random word reads and writes to extended hold registers are also supported.
	/// </summary>
	/// <remarks>
	/// 支持的功能码为 01 02 03 04 05 06 08 09 0A 0B 0D 0E 0F 10, 访问方式分为位读写和字读写，位读写：线圈：100; 输入线圈：x=2;100<br />
	/// 字读写时，保持型寄存器：100; 输入寄存器：x=4;100  保持型寄存器（扩展）: x=9;100   输入寄存器（扩展）: x=10;100
	/// </remarks>
	public class MemobusTcpServer : DeviceServer
	{
		private SoftBuffer xBuffer;

		private SoftBuffer yBuffer;

		private SoftBuffer inputBuffer;

		private SoftBuffer rBuffer;

		private SoftBuffer rExtBuffer;

		private SoftBuffer inputExtBuffer;

		private const int DataPoolLength = 65536;

		/// <summary>
		/// 实例化一个memobus协议的PLC的服务器，支持线圈，输入线圈，保持型寄存器，输入寄存器，保持型寄存器（扩展），输入寄存器（扩展）的读写。
		/// </summary>
		public MemobusTcpServer()
		{
			xBuffer = new SoftBuffer(65536);
			yBuffer = new SoftBuffer(65536);
			inputBuffer = new SoftBuffer(131072)
			{
				IsBoolReverseByWord = true
			};
			rBuffer = new SoftBuffer(131072)
			{
				IsBoolReverseByWord = true
			};
			inputExtBuffer = new SoftBuffer(131072)
			{
				IsBoolReverseByWord = true
			};
			rExtBuffer = new SoftBuffer(131072)
			{
				IsBoolReverseByWord = true
			};
			base.WordLength = 2;
			base.ByteTransform = new RegularByteTransform(DataFormat.CDAB);
		}

		private OperateResult<SoftBuffer> GetDataAreaFromYokogawaAddress(MemobusAddress address, bool isBit)
		{
			switch (address.SFC)
			{
			case 1:
			case 5:
			case 15:
				return OperateResult.CreateSuccessResult(yBuffer);
			case 2:
				return OperateResult.CreateSuccessResult(xBuffer);
			case 3:
			case 6:
			case 16:
				return OperateResult.CreateSuccessResult(rBuffer);
			case 4:
				return OperateResult.CreateSuccessResult(inputBuffer);
			case 9:
			case 11:
			case 13:
			case 14:
				return OperateResult.CreateSuccessResult(rExtBuffer);
			case 10:
				return OperateResult.CreateSuccessResult(inputExtBuffer);
			default:
				return new OperateResult<SoftBuffer>(StringResources.Language.NotSupportedDataType);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.YASKAWA.MemobusTcpNet.Read(System.String,System.UInt16)" />
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			OperateResult<MemobusAddress> operateResult = MemobusAddress.ParseFrom(address, isBit: false);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			OperateResult<SoftBuffer> dataAreaFromYokogawaAddress = GetDataAreaFromYokogawaAddress(operateResult.Content, isBit: false);
			if (!dataAreaFromYokogawaAddress.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(dataAreaFromYokogawaAddress);
			}
			return OperateResult.CreateSuccessResult(dataAreaFromYokogawaAddress.Content.GetBytes(operateResult.Content.AddressStart * 2, length * 2));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.YASKAWA.MemobusTcpNet.Write(System.String,System.Byte[])" />
		[HslMqttApi("WriteByteArray", "")]
		public override OperateResult Write(string address, byte[] value)
		{
			OperateResult<MemobusAddress> operateResult = MemobusAddress.ParseFrom(address, isBit: false);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			OperateResult<SoftBuffer> dataAreaFromYokogawaAddress = GetDataAreaFromYokogawaAddress(operateResult.Content, isBit: false);
			if (!dataAreaFromYokogawaAddress.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(dataAreaFromYokogawaAddress);
			}
			dataAreaFromYokogawaAddress.Content.SetBytes(value, operateResult.Content.AddressStart * 2);
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.YASKAWA.MemobusTcpNet.ReadBool(System.String,System.UInt16)" />
		[HslMqttApi("ReadBoolArray", "")]
		public override OperateResult<bool[]> ReadBool(string address, ushort length)
		{
			OperateResult<MemobusAddress> operateResult = MemobusAddress.ParseFrom(address, isBit: true);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(operateResult);
			}
			OperateResult<SoftBuffer> dataAreaFromYokogawaAddress = GetDataAreaFromYokogawaAddress(operateResult.Content, isBit: true);
			if (!dataAreaFromYokogawaAddress.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(dataAreaFromYokogawaAddress);
			}
			if (operateResult.Content.SFC == 3 || operateResult.Content.SFC == 4)
			{
				return OperateResult.CreateSuccessResult(dataAreaFromYokogawaAddress.Content.GetBool(operateResult.Content.AddressStart, length));
			}
			return OperateResult.CreateSuccessResult((from m in dataAreaFromYokogawaAddress.Content.GetBytes(operateResult.Content.AddressStart, length)
				select m != 0).ToArray());
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.YASKAWA.MemobusTcpNet.Write(System.String,System.Boolean[])" />
		[HslMqttApi("WriteBoolArray", "")]
		public override OperateResult Write(string address, bool[] value)
		{
			OperateResult<MemobusAddress> operateResult = MemobusAddress.ParseFrom(address, isBit: true);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<SoftBuffer> dataAreaFromYokogawaAddress = GetDataAreaFromYokogawaAddress(operateResult.Content, isBit: true);
			if (!dataAreaFromYokogawaAddress.IsSuccess)
			{
				return dataAreaFromYokogawaAddress;
			}
			if (operateResult.Content.SFC == 3 || operateResult.Content.SFC == 4)
			{
				dataAreaFromYokogawaAddress.Content.SetBool(value, operateResult.Content.AddressStart);
			}
			else
			{
				dataAreaFromYokogawaAddress.Content.SetBytes(value.Select((bool m) => (byte)(m ? 1 : 0)).ToArray(), operateResult.Content.AddressStart);
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new MemobusMessage();
		}

		/// <inheritdoc />
		protected override OperateResult<byte[]> ReadFromCoreServer(PipeSession session, byte[] receive)
		{
			byte[] array = null;
			array = ((receive[15] == 1 || receive[15] == 2 || receive[15] == 65) ? ReadBoolByCommand(receive) : ((receive[15] == 5 || receive[15] == 15 || receive[15] == 79) ? WriteBoolByCommand(receive) : ((receive[15] == 3 || receive[15] == 4 || receive[15] == 9 || receive[15] == 10 || receive[15] == 73) ? ReadWordByCommand(receive) : ((receive[15] == 6 || receive[15] == 16 || receive[15] == 11 || receive[15] == 75) ? WriteWordByCommand(receive) : ((receive[15] == 13 || receive[15] == 77) ? ReadRandomWordByCommand(receive) : ((receive[15] == 14) ? WriteRandomWordByCommand(receive) : ((receive[15] != 8) ? PackCommandBack(receive, 1, null) : PackCommandBack(receive, 0, null))))))));
			return OperateResult.CreateSuccessResult(array);
		}

		private byte[] ReadBoolByCommand(byte[] command)
		{
			if (command[15] == 1 || command[15] == 2)
			{
				int num = base.ByteTransform.TransUInt16(command, 17);
				int num2 = base.ByteTransform.TransUInt16(command, 19);
				if (num + num2 > 65535)
				{
					return PackCommandBack(command, 3, null);
				}
				return command[15] switch
				{
					1 => PackCommandBack(command, 0, (from m in yBuffer.GetBytes(num, num2)
						select m != 0).ToArray().ToByteArray()), 
					2 => PackCommandBack(command, 0, (from m in xBuffer.GetBytes(num, num2)
						select m != 0).ToArray().ToByteArray()), 
					_ => PackCommandBack(command, 1, null), 
				};
			}
			if (command[15] == 65)
			{
				byte b = command[18];
				int destIndex = BitConverter.ToInt32(command, 20);
				int length = BitConverter.ToInt32(command, 24);
				return b switch
				{
					77 => PackCommandBack(command, 0, rBuffer.GetBool(destIndex, length).ToByteArray()), 
					73 => PackCommandBack(command, 0, inputBuffer.GetBool(destIndex, length).ToByteArray()), 
					_ => PackCommandBack(command, 1, null), 
				};
			}
			return PackCommandBack(command, 1, null);
		}

		private byte[] WriteBoolByCommand(byte[] command)
		{
			if (!base.EnableWrite)
			{
				return PackCommandBack(command, 3, null);
			}
			if (command[15] == 5 || command[15] == 15)
			{
				int num = base.ByteTransform.TransUInt16(command, 17);
				int num2 = base.ByteTransform.TransUInt16(command, 19);
				switch (command[15])
				{
				case 5:
					if (command.Length != 21)
					{
						return PackCommandBack(command, 5, null);
					}
					yBuffer.SetBytes(new byte[1] { command[19] }, num);
					return PackCommandBack(command, 0, null);
				case 15:
				{
					if (num + num2 > 65535)
					{
						return PackCommandBack(command, 3, null);
					}
					if (command.Length != 21 + (num2 + 7) / 8)
					{
						return PackCommandBack(command, 5, null);
					}
					bool[] source = command.RemoveBegin(21).ToBoolArray().SelectBegin(num2);
					yBuffer.SetBytes(source.Select((bool m) => (byte)(m ? byte.MaxValue : 0)).ToArray(), num);
					return PackCommandBack(command, 0, null);
				}
				default:
					return PackCommandBack(command, 1, null);
				}
			}
			if (command[15] == 79)
			{
				byte b = command[18];
				int destIndex = BitConverter.ToInt32(command, 20);
				int length = BitConverter.ToUInt16(command, 24);
				bool[] value = command.RemoveBegin(28).ToBoolArray().SelectBegin(length);
				if (b == 77)
				{
					rBuffer.SetBool(value, destIndex);
					return PackCommandBack(command, 0, null);
				}
				return PackCommandBack(command, 1, null);
			}
			return PackCommandBack(command, 1, null);
		}

		private byte[] ReadWordByCommand(byte[] command)
		{
			if (command[15] == 3 || command[15] == 4)
			{
				int num = base.ByteTransform.TransUInt16(command, 17);
				int num2 = base.ByteTransform.TransUInt16(command, 19);
				if (num + num2 > 65535)
				{
					return PackCommandBack(command, 3, null);
				}
				if (command[15] == 3)
				{
					return PackCommandBack(command, 0, rBuffer.GetBytes(num * 2, num2 * 2));
				}
				if (command[15] == 4)
				{
					return PackCommandBack(command, 0, inputBuffer.GetBytes(num * 2, num2 * 2));
				}
			}
			else if (command[15] == 9 || command[15] == 10)
			{
				int num3 = BitConverter.ToUInt16(command, 18);
				int num4 = BitConverter.ToUInt16(command, 20);
				if (num3 + num4 > 65535)
				{
					return PackCommandBack(command, 3, null);
				}
				if (command[15] == 9)
				{
					return PackCommandBack(command, 0, SoftBasic.BytesReverseByWord(rExtBuffer.GetBytes(num3 * 2, num4 * 2)));
				}
				if (command[15] == 10)
				{
					return PackCommandBack(command, 0, SoftBasic.BytesReverseByWord(inputExtBuffer.GetBytes(num3 * 2, num4 * 2)));
				}
			}
			else if (command[15] == 73)
			{
				byte b = command[18];
				int num5 = BitConverter.ToInt32(command, 20);
				int num6 = BitConverter.ToUInt16(command, 24);
				switch (b)
				{
				case 77:
					return PackCommandBack(command, 0, SoftBasic.BytesReverseByWord(rBuffer.GetBytes(num5 * 2, num6 * 2)));
				case 73:
					return PackCommandBack(command, 0, SoftBasic.BytesReverseByWord(inputBuffer.GetBytes(num5 * 2, num6 * 2)));
				}
			}
			return PackCommandBack(command, 1, null);
		}

		private byte[] WriteWordByCommand(byte[] command)
		{
			if (!base.EnableWrite)
			{
				return PackCommandBack(command, 3, null);
			}
			if (command[15] == 6 || command[15] == 16)
			{
				int num = base.ByteTransform.TransUInt16(command, 17);
				int num2 = base.ByteTransform.TransUInt16(command, 19);
				if (command[15] == 6)
				{
					if (command.Length != 21)
					{
						return PackCommandBack(command, 3, null);
					}
					rBuffer.SetBytes(command.SelectLast(2), num * 2);
					return PackCommandBack(command, 0, null);
				}
				if (num + num2 > 65535)
				{
					return PackCommandBack(command, 3, null);
				}
				if (command.Length != 21 + num2 * 2)
				{
					return PackCommandBack(command, 3, null);
				}
				rBuffer.SetBytes(command.RemoveBegin(21), num * 2);
				return PackCommandBack(command, 0, null);
			}
			if (command[15] == 11)
			{
				int num3 = BitConverter.ToUInt16(command, 18);
				int num4 = BitConverter.ToUInt16(command, 20);
				if (num3 + num4 > 65535)
				{
					return PackCommandBack(command, 3, null);
				}
				if (command.Length != 22 + num4 * 2)
				{
					return PackCommandBack(command, 3, null);
				}
				rExtBuffer.SetBytes(SoftBasic.BytesReverseByWord(command.RemoveBegin(22)), num3 * 2);
				return PackCommandBack(command, 0, null);
			}
			if (command[15] == 75)
			{
				byte b = command[18];
				int num5 = BitConverter.ToInt32(command, 20);
				int num6 = BitConverter.ToUInt16(command, 24);
				if (command.Length != 26 + num6 * 2)
				{
					return PackCommandBack(command, 3, null);
				}
				if (b == 77)
				{
					rBuffer.SetBytes(SoftBasic.BytesReverseByWord(command.RemoveBegin(26)), num5 * 2);
					return PackCommandBack(command, 0, null);
				}
			}
			return PackCommandBack(command, 1, null);
		}

		private byte[] ReadRandomWordByCommand(byte[] command)
		{
			int num = BitConverter.ToUInt16(command, 18);
			if (command[15] == 13)
			{
				if (command.Length != 20 + num * 2)
				{
					return PackCommandBack(command, 3, null);
				}
				byte[] array = new byte[num * 2];
				for (int i = 0; i < num; i++)
				{
					int num2 = BitConverter.ToUInt16(command, 20 + i * 2);
					byte[] bytes = rExtBuffer.GetBytes(num2 * 2, 2);
					array[i * 2] = bytes[1];
					array[i * 2 + 1] = bytes[0];
				}
				return PackCommandBack(command, 0, array);
			}
			if (command.Length != 20 + num * 6)
			{
				return PackCommandBack(command, 3, null);
			}
			byte[] array2 = new byte[num * 2];
			for (int j = 0; j < num; j++)
			{
				byte b = command[20 + j * 6];
				int num3 = BitConverter.ToInt32(command, 22 + j * 6);
				switch (b)
				{
				case 77:
				{
					byte[] bytes3 = rBuffer.GetBytes(num3 * 2, 2);
					array2[j * 2] = bytes3[1];
					array2[j * 2 + 1] = bytes3[0];
					break;
				}
				case 73:
				{
					byte[] bytes2 = inputBuffer.GetBytes(num3 * 2, 2);
					array2[j * 2] = bytes2[1];
					array2[j * 2 + 1] = bytes2[0];
					break;
				}
				}
			}
			return PackCommandBack(command, 0, array2);
		}

		private byte[] WriteRandomWordByCommand(byte[] command)
		{
			if (!base.EnableWrite)
			{
				return PackCommandBack(command, 3, null);
			}
			int num = BitConverter.ToUInt16(command, 18);
			if (command.Length != 20 + num * 4)
			{
				return PackCommandBack(command, 3, null);
			}
			for (int i = 0; i < num; i++)
			{
				int num2 = BitConverter.ToUInt16(command, 20 + i * 4);
				rExtBuffer.SetValue(command[20 + i * 4 + 2], num2 * 2 + 1);
				rExtBuffer.SetValue(command[20 + i * 4 + 3], num2 * 2);
			}
			return PackCommandBack(command, 0, null);
		}

		private byte TransByteHighLow(byte value)
		{
			int num = value & 0xF0;
			num >>= 4;
			return (byte)(((value & 0xF) << 4) | num);
		}

		private byte[] PackCommandBack(byte[] cmds, byte err, byte[] result)
		{
			if (result == null)
			{
				result = new byte[0];
			}
			if (err > 0)
			{
				byte[] array = new byte[6 + result.Length];
				array[0] = 4;
				array[1] = 0;
				array[2] = cmds[14];
				array[3] = (byte)(cmds[15] + 128);
				array[4] = TransByteHighLow(cmds[16]);
				array[5] = err;
				return MemobusHelper.PackCommandWithHeader(array, cmds[1]);
			}
			if (cmds[15] == 1 || cmds[15] == 2 || cmds[15] == 3 || cmds[15] == 4)
			{
				byte[] array2 = new byte[5 + result.Length];
				array2[0] = BitConverter.GetBytes(3 + result.Length)[0];
				array2[1] = BitConverter.GetBytes(3 + result.Length)[1];
				array2[2] = cmds[14];
				array2[3] = cmds[15];
				array2[4] = TransByteHighLow(cmds[16]);
				result.CopyTo(array2, 5);
				return MemobusHelper.PackCommandWithHeader(array2, cmds[1]);
			}
			if (cmds[15] == 5 || cmds[15] == 6 || cmds[15] == 8)
			{
				byte[] array3 = cmds.RemoveBegin(12);
				array3[0] = BitConverter.GetBytes(array3.Length - 2)[0];
				array3[1] = BitConverter.GetBytes(array3.Length - 2)[1];
				array3[4] = TransByteHighLow(cmds[16]);
				return MemobusHelper.PackCommandWithHeader(array3, cmds[1]);
			}
			if (cmds[15] == 9 || cmds[15] == 10 || cmds[15] == 13)
			{
				byte[] array4 = new byte[8 + result.Length];
				array4[0] = BitConverter.GetBytes(array4.Length - 2)[0];
				array4[1] = BitConverter.GetBytes(array4.Length - 2)[1];
				array4[2] = cmds[14];
				array4[3] = cmds[15];
				array4[4] = TransByteHighLow(cmds[16]);
				array4[6] = BitConverter.GetBytes(result.Length / 2)[0];
				array4[7] = BitConverter.GetBytes(result.Length / 2)[1];
				result.CopyTo(array4, 8);
				return MemobusHelper.PackCommandWithHeader(array4, cmds[1]);
			}
			if (cmds[15] == 11)
			{
				byte[] array5 = cmds.SelectMiddle(12, 10);
				array5[0] = BitConverter.GetBytes(array5.Length - 2)[0];
				array5[1] = BitConverter.GetBytes(array5.Length - 2)[1];
				array5[4] = TransByteHighLow(cmds[16]);
				return MemobusHelper.PackCommandWithHeader(array5, cmds[1]);
			}
			if (cmds[15] == 14)
			{
				byte[] array6 = cmds.SelectMiddle(12, 8);
				array6[0] = BitConverter.GetBytes(array6.Length - 2)[0];
				array6[1] = BitConverter.GetBytes(array6.Length - 2)[1];
				array6[4] = TransByteHighLow(cmds[16]);
				return MemobusHelper.PackCommandWithHeader(array6, cmds[1]);
			}
			if (cmds[15] == 15 || cmds[15] == 16)
			{
				byte[] array7 = cmds.SelectMiddle(12, 9);
				array7[0] = BitConverter.GetBytes(array7.Length - 2)[0];
				array7[1] = BitConverter.GetBytes(array7.Length - 2)[1];
				array7[4] = TransByteHighLow(cmds[16]);
				return MemobusHelper.PackCommandWithHeader(array7, cmds[1]);
			}
			if (cmds[15] == 65)
			{
				byte[] array8 = new byte[8 + result.Length];
				array8[0] = BitConverter.GetBytes(array8.Length - 2)[0];
				array8[1] = BitConverter.GetBytes(array8.Length - 2)[1];
				array8[2] = cmds[14];
				array8[3] = cmds[15];
				array8[4] = TransByteHighLow(cmds[16]);
				array8[6] = cmds[18];
				result.CopyTo(array8, 8);
				return MemobusHelper.PackCommandWithHeader(array8, cmds[1]);
			}
			if (cmds[15] == 73)
			{
				byte[] array9 = new byte[10 + result.Length];
				array9[0] = BitConverter.GetBytes(8 + result.Length)[0];
				array9[1] = BitConverter.GetBytes(8 + result.Length)[1];
				array9[2] = cmds[14];
				array9[3] = cmds[15];
				array9[4] = TransByteHighLow(cmds[16]);
				array9[6] = cmds[18];
				array9[8] = BitConverter.GetBytes(result.Length / 2)[0];
				array9[9] = BitConverter.GetBytes(result.Length / 2)[1];
				result.CopyTo(array9, 10);
				return MemobusHelper.PackCommandWithHeader(array9, cmds[1]);
			}
			if (cmds[15] == 75)
			{
				byte[] array10 = cmds.SelectMiddle(12, 14);
				array10[0] = BitConverter.GetBytes(array10.Length - 2)[0];
				array10[1] = BitConverter.GetBytes(array10.Length - 2)[1];
				array10[4] = TransByteHighLow(cmds[16]);
				return MemobusHelper.PackCommandWithHeader(array10, cmds[1]);
			}
			if (cmds[15] == 77)
			{
				byte[] array11 = new byte[8 + result.Length];
				array11[0] = BitConverter.GetBytes(6 + result.Length)[0];
				array11[1] = BitConverter.GetBytes(6 + result.Length)[1];
				array11[2] = cmds[14];
				array11[3] = cmds[15];
				array11[4] = TransByteHighLow(cmds[16]);
				array11[6] = BitConverter.GetBytes(result.Length / 2)[0];
				array11[7] = BitConverter.GetBytes(result.Length / 2)[1];
				result.CopyTo(array11, 8);
				return MemobusHelper.PackCommandWithHeader(array11, cmds[1]);
			}
			if (cmds[15] == 79)
			{
				byte[] array12 = cmds.SelectMiddle(12, 16);
				array12[0] = BitConverter.GetBytes(array12.Length - 2)[0];
				array12[1] = BitConverter.GetBytes(array12.Length - 2)[1];
				array12[4] = TransByteHighLow(cmds[16]);
				return MemobusHelper.PackCommandWithHeader(array12, cmds[1]);
			}
			return PackCommandBack(cmds, 3, null);
		}

		/// <inheritdoc />
		protected override void LoadFromBytes(byte[] content)
		{
			if (content.Length < 655360)
			{
				throw new Exception("File is not correct");
			}
			xBuffer.SetBytes(content, 0, 0, 65536);
			yBuffer.SetBytes(content, 65536, 0, 65536);
			inputBuffer.SetBytes(content, 131072, 0, 65536);
			rBuffer.SetBytes(content, 262144, 0, 65536);
			rExtBuffer.SetBytes(content, 393216, 0, 65536);
			inputExtBuffer.SetBytes(content, 524288, 0, 65536);
		}

		/// <inheritdoc />
		protected override byte[] SaveToBytes()
		{
			byte[] array = new byte[655360];
			Array.Copy(xBuffer.GetBytes(), 0, array, 0, 65536);
			Array.Copy(yBuffer.GetBytes(), 0, array, 65536, 65536);
			Array.Copy(inputBuffer.GetBytes(), 0, array, 131072, 131072);
			Array.Copy(rBuffer.GetBytes(), 0, array, 262144, 131072);
			Array.Copy(rExtBuffer.GetBytes(), 0, array, 393216, 131072);
			Array.Copy(inputExtBuffer.GetBytes(), 0, array, 524288, 131072);
			return array;
		}

		/// <inheritdoc />
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				xBuffer?.Dispose();
				yBuffer?.Dispose();
				inputBuffer?.Dispose();
				rBuffer?.Dispose();
				rExtBuffer?.Dispose();
				inputExtBuffer?.Dispose();
			}
			base.Dispose(disposing);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"MemobusTcpServer[{base.Port}]";
		}
	}
}
