using System;
using System.Linq;
using System.Text;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Address;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Core.Net;
using HslCommunication.Reflection;

namespace HslCommunication.Profinet.Yokogawa
{
	/// <summary>
	/// 横河PLC的虚拟服务器，支持X,Y,I,E,M,T,C,L继电器类型的数据读写，支持D,B,F,R,V,Z,W,TN,CN寄存器类型的数据读写，可以用来测试横河PLC的二进制通信类型<br />
	/// Yokogawa PLC's virtual server, supports X, Y, I, E, M, T, C, L relay type data read and write, 
	/// supports D, B, F, R, V, Z, W, TN, CN register types The data read and write can be used to test the binary communication type of Yokogawa PLC
	/// </summary>
	/// <remarks>
	/// 其中的X继电器可以在服务器进行读写操作，但是远程的PLC只能进行读取，所有的数据读写的最大的范围按照协议进行了限制。
	/// </remarks>
	/// <example>
	/// 你可以很快速并且简单的创建一个虚拟的横河服务器
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Profinet\YokogawaLinkServerSample.cs" region="UseExample1" title="简单的创建服务器" />
	/// 当然如果需要高级的服务器，指定日志，限制客户端的IP地址，获取客户端发送的信息，在服务器初始化的时候就要参照下面的代码：
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Profinet\YokogawaLinkServerSample.cs" region="UseExample4" title="定制服务器" />
	/// 服务器创建好之后，我们就可以对服务器进行一些读写的操作了，下面的代码是基础的BCL类型的读写操作。
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Profinet\YokogawaLinkServerSample.cs" region="ReadWriteExample" title="基础的读写示例" />
	/// 高级的对于byte数组类型的数据进行批量化的读写操作如下：   
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Profinet\YokogawaLinkServerSample.cs" region="BytesReadWrite" title="字节的读写示例" />
	/// 更高级操作请参见源代码。
	/// </example>
	public class YokogawaLinkServer : DeviceServer
	{
		private SoftBuffer xBuffer;

		private SoftBuffer yBuffer;

		private SoftBuffer iBuffer;

		private SoftBuffer eBuffer;

		private SoftBuffer mBuffer;

		private SoftBuffer lBuffer;

		private SoftBuffer dBuffer;

		private SoftBuffer bBuffer;

		private SoftBuffer fBuffer;

		private SoftBuffer rBuffer;

		private SoftBuffer vBuffer;

		private SoftBuffer zBuffer;

		private SoftBuffer wBuffer;

		private SoftBuffer specialBuffer;

		private const int DataPoolLength = 65536;

		private IByteTransform transform;

		private bool isProgramStarted = false;

		/// <summary>
		/// 实例化一个横河PLC的服务器，支持X,Y,I,E,M,T,C,L继电器类型的数据读写，支持D,B,F,R,V,Z,W,TN,CN寄存器类型的数据读写<br />
		/// Instantiate a Yokogawa PLC server, support X, Y, I, E, M, T, C, L relay type data read and write, 
		/// support D, B, F, R, V, Z, W, TN, CN Register type data reading and writing
		/// </summary>
		public YokogawaLinkServer()
		{
			xBuffer = new SoftBuffer(65536);
			yBuffer = new SoftBuffer(65536);
			iBuffer = new SoftBuffer(65536);
			eBuffer = new SoftBuffer(65536);
			mBuffer = new SoftBuffer(65536);
			lBuffer = new SoftBuffer(65536);
			dBuffer = new SoftBuffer(131072);
			bBuffer = new SoftBuffer(131072);
			fBuffer = new SoftBuffer(131072);
			rBuffer = new SoftBuffer(131072);
			vBuffer = new SoftBuffer(131072);
			zBuffer = new SoftBuffer(131072);
			wBuffer = new SoftBuffer(131072);
			specialBuffer = new SoftBuffer(131072);
			base.WordLength = 2;
			base.ByteTransform = new RegularByteTransform(DataFormat.CDAB);
			base.ByteTransform.DataFormat = DataFormat.CDAB;
			transform = new ReverseBytesTransform();
		}

		private OperateResult<SoftBuffer> GetDataAreaFromYokogawaAddress(YokogawaLinkAddress yokogawaAddress, bool isBit)
		{
			if (isBit)
			{
				return yokogawaAddress.DataCode switch
				{
					24 => OperateResult.CreateSuccessResult(xBuffer), 
					25 => OperateResult.CreateSuccessResult(yBuffer), 
					9 => OperateResult.CreateSuccessResult(iBuffer), 
					5 => OperateResult.CreateSuccessResult(eBuffer), 
					13 => OperateResult.CreateSuccessResult(mBuffer), 
					12 => OperateResult.CreateSuccessResult(lBuffer), 
					_ => new OperateResult<SoftBuffer>(StringResources.Language.NotSupportedDataType), 
				};
			}
			return yokogawaAddress.DataCode switch
			{
				24 => OperateResult.CreateSuccessResult(xBuffer), 
				25 => OperateResult.CreateSuccessResult(yBuffer), 
				9 => OperateResult.CreateSuccessResult(iBuffer), 
				5 => OperateResult.CreateSuccessResult(eBuffer), 
				13 => OperateResult.CreateSuccessResult(mBuffer), 
				12 => OperateResult.CreateSuccessResult(lBuffer), 
				4 => OperateResult.CreateSuccessResult(dBuffer), 
				2 => OperateResult.CreateSuccessResult(bBuffer), 
				6 => OperateResult.CreateSuccessResult(fBuffer), 
				18 => OperateResult.CreateSuccessResult(rBuffer), 
				22 => OperateResult.CreateSuccessResult(vBuffer), 
				26 => OperateResult.CreateSuccessResult(zBuffer), 
				23 => OperateResult.CreateSuccessResult(wBuffer), 
				_ => new OperateResult<SoftBuffer>(StringResources.Language.NotSupportedDataType), 
			};
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Yokogawa.YokogawaLinkTcp.Read(System.String,System.UInt16)" />
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			if (address.StartsWith("Special:") || address.StartsWith("special:"))
			{
				address = address.Substring(8);
				OperateResult<int> operateResult = HslHelper.ExtractParameter(ref address, "unit");
				OperateResult<int> operateResult2 = HslHelper.ExtractParameter(ref address, "slot");
				try
				{
					return OperateResult.CreateSuccessResult(specialBuffer.GetBytes(ushort.Parse(address) * 2, length * 2));
				}
				catch (Exception ex)
				{
					return new OperateResult<byte[]>("Address format wrong: " + ex.Message);
				}
			}
			OperateResult<YokogawaLinkAddress> operateResult3 = YokogawaLinkAddress.ParseFrom(address, length);
			if (!operateResult3.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult3);
			}
			OperateResult<SoftBuffer> dataAreaFromYokogawaAddress = GetDataAreaFromYokogawaAddress(operateResult3.Content, isBit: false);
			if (!dataAreaFromYokogawaAddress.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(dataAreaFromYokogawaAddress);
			}
			if (operateResult3.Content.DataCode == 24 || operateResult3.Content.DataCode == 25 || operateResult3.Content.DataCode == 9 || operateResult3.Content.DataCode == 5 || operateResult3.Content.DataCode == 13 || operateResult3.Content.DataCode == 12)
			{
				return OperateResult.CreateSuccessResult((from m in dataAreaFromYokogawaAddress.Content.GetBytes(operateResult3.Content.AddressStart, length * 16)
					select m != 0).ToArray().ToByteArray());
			}
			return OperateResult.CreateSuccessResult(dataAreaFromYokogawaAddress.Content.GetBytes(operateResult3.Content.AddressStart * 2, length * 2));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Yokogawa.YokogawaLinkTcp.Write(System.String,System.Byte[])" />
		[HslMqttApi("WriteByteArray", "")]
		public override OperateResult Write(string address, byte[] value)
		{
			if (address.StartsWith("Special:") || address.StartsWith("special:"))
			{
				address = address.Substring(8);
				OperateResult<int> operateResult = HslHelper.ExtractParameter(ref address, "unit");
				OperateResult<int> operateResult2 = HslHelper.ExtractParameter(ref address, "slot");
				try
				{
					specialBuffer.SetBytes(value, ushort.Parse(address) * 2);
					return OperateResult.CreateSuccessResult();
				}
				catch (Exception ex)
				{
					return new OperateResult("Address format wrong: " + ex.Message);
				}
			}
			OperateResult<YokogawaLinkAddress> operateResult3 = YokogawaLinkAddress.ParseFrom(address, 0);
			if (!operateResult3.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult3);
			}
			OperateResult<SoftBuffer> dataAreaFromYokogawaAddress = GetDataAreaFromYokogawaAddress(operateResult3.Content, isBit: false);
			if (!dataAreaFromYokogawaAddress.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(dataAreaFromYokogawaAddress);
			}
			if (operateResult3.Content.DataCode == 24 || operateResult3.Content.DataCode == 25 || operateResult3.Content.DataCode == 9 || operateResult3.Content.DataCode == 5 || operateResult3.Content.DataCode == 13 || operateResult3.Content.DataCode == 12)
			{
				dataAreaFromYokogawaAddress.Content.SetBytes((from m in value.ToBoolArray()
					select (byte)(m ? 1 : 0)).ToArray(), operateResult3.Content.AddressStart);
			}
			else
			{
				dataAreaFromYokogawaAddress.Content.SetBytes(value, operateResult3.Content.AddressStart * 2);
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Yokogawa.YokogawaLinkTcp.ReadBool(System.String,System.UInt16)" />
		[HslMqttApi("ReadBoolArray", "")]
		public override OperateResult<bool[]> ReadBool(string address, ushort length)
		{
			OperateResult<YokogawaLinkAddress> operateResult = YokogawaLinkAddress.ParseFrom(address, length);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(operateResult);
			}
			OperateResult<SoftBuffer> dataAreaFromYokogawaAddress = GetDataAreaFromYokogawaAddress(operateResult.Content, isBit: true);
			if (!dataAreaFromYokogawaAddress.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(dataAreaFromYokogawaAddress);
			}
			return OperateResult.CreateSuccessResult((from m in dataAreaFromYokogawaAddress.Content.GetBytes(operateResult.Content.AddressStart, length)
				select m != 0).ToArray());
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Yokogawa.YokogawaLinkTcp.Write(System.String,System.Boolean[])" />
		[HslMqttApi("WriteBoolArray", "")]
		public override OperateResult Write(string address, bool[] value)
		{
			OperateResult<YokogawaLinkAddress> operateResult = YokogawaLinkAddress.ParseFrom(address, 0);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<SoftBuffer> dataAreaFromYokogawaAddress = GetDataAreaFromYokogawaAddress(operateResult.Content, isBit: true);
			if (!dataAreaFromYokogawaAddress.IsSuccess)
			{
				return dataAreaFromYokogawaAddress;
			}
			dataAreaFromYokogawaAddress.Content.SetBytes(value.Select((bool m) => (byte)(m ? 1 : 0)).ToArray(), operateResult.Content.AddressStart);
			return OperateResult.CreateSuccessResult();
		}

		/// <summary>
		/// 如果未执行程序，则开始执行程序<br />
		/// Starts executing a program if it is not being executed
		/// </summary>
		[HslMqttApi(Description = "Starts executing a program if it is not being executed")]
		public void StartProgram()
		{
			isProgramStarted = true;
		}

		/// <summary>
		/// 停止当前正在执行程序<br />
		/// Stops the executing program.
		/// </summary>
		[HslMqttApi(Description = "Stops the executing program.")]
		public void StopProgram()
		{
			isProgramStarted = false;
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new YokogawaLinkBinaryMessage();
		}

		/// <inheritdoc />
		protected override OperateResult<byte[]> ReadFromCoreServer(PipeSession session, byte[] receive)
		{
			byte[] array = null;
			if (receive[0] == 1)
			{
				array = ReadBoolByCommand(receive);
			}
			else if (receive[0] == 2)
			{
				array = WriteBoolByCommand(receive);
			}
			else if (receive[0] == 4)
			{
				array = ReadRandomBoolByCommand(receive);
			}
			else if (receive[0] == 5)
			{
				array = WriteRandomBoolByCommand(receive);
			}
			else if (receive[0] == 17)
			{
				array = ReadWordByCommand(receive);
			}
			else if (receive[0] == 18)
			{
				array = WriteWordByCommand(receive);
			}
			else if (receive[0] == 20)
			{
				array = ReadRandomWordByCommand(receive);
			}
			else if (receive[0] == 21)
			{
				array = WriteRandomWordByCommand(receive);
			}
			else if (receive[0] == 49)
			{
				array = ReadSpecialModule(receive);
			}
			else if (receive[0] == 50)
			{
				array = WriteSpecialModule(receive);
			}
			else if (receive[0] == 69)
			{
				array = StartByCommand(receive);
			}
			else if (receive[0] == 70)
			{
				array = StopByCommand(receive);
			}
			else
			{
				if (receive[0] == 97)
				{
					throw new RemoteCloseException();
				}
				array = ((receive[0] == 98) ? ReadSystemByCommand(receive) : ((receive[0] != 99) ? PackCommandBack(receive[0], 3, null) : ReadSystemDateTime(receive)));
			}
			return OperateResult.CreateSuccessResult(array);
		}

		private byte[] ReadBoolByCommand(byte[] command)
		{
			int num = transform.TransInt32(command, 6);
			int num2 = transform.TransUInt16(command, 10);
			if (num > 65535 || num < 0)
			{
				return PackCommandBack(command[0], 4, null);
			}
			if (num2 > 256)
			{
				return PackCommandBack(command[0], 5, null);
			}
			if (num + num2 > 65535)
			{
				return PackCommandBack(command[0], 5, null);
			}
			return command[5] switch
			{
				24 => PackCommandBack(command[0], 0, xBuffer.GetBytes(num, num2)), 
				25 => PackCommandBack(command[0], 0, yBuffer.GetBytes(num, num2)), 
				9 => PackCommandBack(command[0], 0, iBuffer.GetBytes(num, num2)), 
				5 => PackCommandBack(command[0], 0, eBuffer.GetBytes(num, num2)), 
				13 => PackCommandBack(command[0], 0, mBuffer.GetBytes(num, num2)), 
				12 => PackCommandBack(command[0], 0, lBuffer.GetBytes(num, num2)), 
				_ => PackCommandBack(command[0], 3, null), 
			};
		}

		private byte[] WriteBoolByCommand(byte[] command)
		{
			if (!base.EnableWrite)
			{
				return PackCommandBack(command[0], 3, null);
			}
			int num = transform.TransInt32(command, 6);
			int num2 = transform.TransUInt16(command, 10);
			if (num > 65535 || num < 0)
			{
				return PackCommandBack(command[0], 4, null);
			}
			if (num2 > 256)
			{
				return PackCommandBack(command[0], 5, null);
			}
			if (num + num2 > 65535)
			{
				return PackCommandBack(command[0], 5, null);
			}
			if (num2 != command.Length - 12)
			{
				return PackCommandBack(command[0], 5, null);
			}
			switch (command[5])
			{
			case 24:
				return PackCommandBack(command[0], 3, null);
			case 25:
				yBuffer.SetBytes(command.RemoveBegin(12), num);
				return PackCommandBack(command[0], 0, null);
			case 9:
				iBuffer.SetBytes(command.RemoveBegin(12), num);
				return PackCommandBack(command[0], 0, null);
			case 5:
				eBuffer.SetBytes(command.RemoveBegin(12), num);
				return PackCommandBack(command[0], 0, null);
			case 13:
				mBuffer.SetBytes(command.RemoveBegin(12), num);
				return PackCommandBack(command[0], 0, null);
			case 12:
				lBuffer.SetBytes(command.RemoveBegin(12), num);
				return PackCommandBack(command[0], 0, null);
			default:
				return PackCommandBack(command[0], 3, null);
			}
		}

		private byte[] ReadRandomBoolByCommand(byte[] command)
		{
			int num = transform.TransUInt16(command, 4);
			if (num > 32)
			{
				return PackCommandBack(command[0], 5, null);
			}
			if (num * 6 != command.Length - 6)
			{
				return PackCommandBack(command[0], 5, null);
			}
			byte[] array = new byte[num];
			for (int i = 0; i < num; i++)
			{
				int num2 = transform.TransInt32(command, 8 + 6 * i);
				if (num2 > 65535 || num2 < 0)
				{
					return PackCommandBack(command[0], 4, null);
				}
				switch (command[7 + i * 6])
				{
				case 24:
					array[i] = xBuffer.GetBytes(num2, 1)[0];
					break;
				case 25:
					array[i] = yBuffer.GetBytes(num2, 1)[0];
					break;
				case 9:
					array[i] = iBuffer.GetBytes(num2, 1)[0];
					break;
				case 5:
					array[i] = eBuffer.GetBytes(num2, 1)[0];
					break;
				case 13:
					array[i] = mBuffer.GetBytes(num2, 1)[0];
					break;
				case 12:
					array[i] = lBuffer.GetBytes(num2, 1)[0];
					break;
				default:
					return PackCommandBack(command[0], 3, null);
				}
			}
			return PackCommandBack(command[0], 0, array);
		}

		private byte[] WriteRandomBoolByCommand(byte[] command)
		{
			if (!base.EnableWrite)
			{
				return PackCommandBack(command[0], 3, null);
			}
			int num = transform.TransUInt16(command, 4);
			if (num > 32)
			{
				return PackCommandBack(command[0], 5, null);
			}
			if (num * 8 - 1 != command.Length - 6)
			{
				return PackCommandBack(command[0], 5, null);
			}
			for (int i = 0; i < num; i++)
			{
				int num2 = transform.TransInt32(command, 8 + 8 * i);
				if (num2 > 65535 || num2 < 0)
				{
					return PackCommandBack(command[0], 4, null);
				}
				switch (command[7 + i * 8])
				{
				case 24:
					return PackCommandBack(command[0], 3, null);
				case 25:
					yBuffer.SetValue(command[12 + 8 * i], num2);
					break;
				case 9:
					iBuffer.SetValue(command[12 + 8 * i], num2);
					break;
				case 5:
					eBuffer.SetValue(command[12 + 8 * i], num2);
					break;
				case 13:
					mBuffer.SetValue(command[12 + 8 * i], num2);
					break;
				case 12:
					lBuffer.SetValue(command[12 + 8 * i], num2);
					break;
				default:
					return PackCommandBack(command[0], 3, null);
				}
			}
			return PackCommandBack(command[0], 0, null);
		}

		private byte[] ReadWordByCommand(byte[] command)
		{
			int num = transform.TransInt32(command, 6);
			int num2 = transform.TransUInt16(command, 10);
			if (num > 65535 || num < 0)
			{
				return PackCommandBack(command[0], 4, null);
			}
			if (num2 > 64)
			{
				return PackCommandBack(command[0], 5, null);
			}
			if (num + num2 > 65535)
			{
				return PackCommandBack(command[0], 5, null);
			}
			return command[5] switch
			{
				24 => PackCommandBack(command[0], 0, (from m in xBuffer.GetBytes(num, num2 * 16)
					select m != 0).ToArray().ToByteArray()), 
				25 => PackCommandBack(command[0], 0, (from m in yBuffer.GetBytes(num, num2 * 16)
					select m != 0).ToArray().ToByteArray()), 
				9 => PackCommandBack(command[0], 0, (from m in iBuffer.GetBytes(num, num2 * 16)
					select m != 0).ToArray().ToByteArray()), 
				5 => PackCommandBack(command[0], 0, (from m in eBuffer.GetBytes(num, num2 * 16)
					select m != 0).ToArray().ToByteArray()), 
				13 => PackCommandBack(command[0], 0, (from m in mBuffer.GetBytes(num, num2 * 16)
					select m != 0).ToArray().ToByteArray()), 
				12 => PackCommandBack(command[0], 0, (from m in lBuffer.GetBytes(num, num2 * 16)
					select m != 0).ToArray().ToByteArray()), 
				4 => PackCommandBack(command[0], 0, dBuffer.GetBytes(num * 2, num2 * 2)), 
				2 => PackCommandBack(command[0], 0, bBuffer.GetBytes(num * 2, num2 * 2)), 
				6 => PackCommandBack(command[0], 0, fBuffer.GetBytes(num * 2, num2 * 2)), 
				18 => PackCommandBack(command[0], 0, rBuffer.GetBytes(num * 2, num2 * 2)), 
				22 => PackCommandBack(command[0], 0, vBuffer.GetBytes(num * 2, num2 * 2)), 
				26 => PackCommandBack(command[0], 0, zBuffer.GetBytes(num * 2, num2 * 2)), 
				23 => PackCommandBack(command[0], 0, wBuffer.GetBytes(num * 2, num2 * 2)), 
				_ => PackCommandBack(command[0], 3, null), 
			};
		}

		private byte[] WriteWordByCommand(byte[] command)
		{
			if (!base.EnableWrite)
			{
				return PackCommandBack(command[0], 3, null);
			}
			int num = transform.TransInt32(command, 6);
			int num2 = transform.TransUInt16(command, 10);
			if (num > 65535 || num < 0)
			{
				return PackCommandBack(command[0], 4, null);
			}
			if (num2 > 64)
			{
				return PackCommandBack(command[0], 5, null);
			}
			if (num + num2 > 65535)
			{
				return PackCommandBack(command[0], 5, null);
			}
			if (num2 * 2 != command.Length - 12)
			{
				return PackCommandBack(command[0], 5, null);
			}
			switch (command[5])
			{
			case 24:
				return PackCommandBack(command[0], 3, null);
			case 25:
				yBuffer.SetBytes((from m in command.RemoveBegin(12).ToBoolArray()
					select (byte)(m ? 1 : 0)).ToArray(), num);
				return PackCommandBack(command[0], 0, null);
			case 9:
				iBuffer.SetBytes((from m in command.RemoveBegin(12).ToBoolArray()
					select (byte)(m ? 1 : 0)).ToArray(), num);
				return PackCommandBack(command[0], 0, null);
			case 5:
				eBuffer.SetBytes((from m in command.RemoveBegin(12).ToBoolArray()
					select (byte)(m ? 1 : 0)).ToArray(), num);
				return PackCommandBack(command[0], 0, null);
			case 13:
				mBuffer.SetBytes((from m in command.RemoveBegin(12).ToBoolArray()
					select (byte)(m ? 1 : 0)).ToArray(), num);
				return PackCommandBack(command[0], 0, null);
			case 12:
				lBuffer.SetBytes((from m in command.RemoveBegin(12).ToBoolArray()
					select (byte)(m ? 1 : 0)).ToArray(), num);
				return PackCommandBack(command[0], 0, null);
			case 4:
				dBuffer.SetBytes(command.RemoveBegin(12), num * 2);
				return PackCommandBack(command[0], 0, null);
			case 2:
				bBuffer.SetBytes(command.RemoveBegin(12), num * 2);
				return PackCommandBack(command[0], 0, null);
			case 6:
				fBuffer.SetBytes(command.RemoveBegin(12), num * 2);
				return PackCommandBack(command[0], 0, null);
			case 18:
				rBuffer.SetBytes(command.RemoveBegin(12), num * 2);
				return PackCommandBack(command[0], 0, null);
			case 22:
				vBuffer.SetBytes(command.RemoveBegin(12), num * 2);
				return PackCommandBack(command[0], 0, null);
			case 26:
				zBuffer.SetBytes(command.RemoveBegin(12), num * 2);
				return PackCommandBack(command[0], 0, null);
			case 23:
				wBuffer.SetBytes(command.RemoveBegin(12), num * 2);
				return PackCommandBack(command[0], 0, null);
			default:
				return PackCommandBack(command[0], 3, null);
			}
		}

		private byte[] ReadRandomWordByCommand(byte[] command)
		{
			int num = transform.TransUInt16(command, 4);
			if (num > 32)
			{
				return PackCommandBack(command[0], 5, null);
			}
			if (num * 6 != command.Length - 6)
			{
				return PackCommandBack(command[0], 5, null);
			}
			byte[] array = new byte[num * 2];
			for (int i = 0; i < num; i++)
			{
				int num2 = transform.TransInt32(command, 8 + 6 * i);
				if (num2 > 65535 || num2 < 0)
				{
					return PackCommandBack(command[0], 4, null);
				}
				switch (command[7 + i * 6])
				{
				case 24:
					(from m in xBuffer.GetBytes(num2, 16)
						select m != 0).ToArray().ToByteArray().CopyTo(array, i * 2);
					break;
				case 25:
					(from m in yBuffer.GetBytes(num2, 16)
						select m != 0).ToArray().ToByteArray().CopyTo(array, i * 2);
					break;
				case 9:
					(from m in iBuffer.GetBytes(num2, 16)
						select m != 0).ToArray().ToByteArray().CopyTo(array, i * 2);
					break;
				case 5:
					(from m in eBuffer.GetBytes(num2, 16)
						select m != 0).ToArray().ToByteArray().CopyTo(array, i * 2);
					break;
				case 13:
					(from m in mBuffer.GetBytes(num2, 16)
						select m != 0).ToArray().ToByteArray().CopyTo(array, i * 2);
					break;
				case 12:
					(from m in lBuffer.GetBytes(num2, 16)
						select m != 0).ToArray().ToByteArray().CopyTo(array, i * 2);
					break;
				case 4:
					dBuffer.GetBytes(num2 * 2, 2).CopyTo(array, i * 2);
					break;
				case 2:
					bBuffer.GetBytes(num2 * 2, 2).CopyTo(array, i * 2);
					break;
				case 6:
					fBuffer.GetBytes(num2 * 2, 2).CopyTo(array, i * 2);
					break;
				case 18:
					rBuffer.GetBytes(num2 * 2, 2).CopyTo(array, i * 2);
					break;
				case 22:
					vBuffer.GetBytes(num2 * 2, 2).CopyTo(array, i * 2);
					break;
				case 26:
					zBuffer.GetBytes(num2 * 2, 2).CopyTo(array, i * 2);
					break;
				case 23:
					wBuffer.GetBytes(num2 * 2, 2).CopyTo(array, i * 2);
					break;
				default:
					return PackCommandBack(command[0], 3, null);
				}
			}
			return PackCommandBack(command[0], 0, array);
		}

		private byte[] WriteRandomWordByCommand(byte[] command)
		{
			if (!base.EnableWrite)
			{
				return PackCommandBack(command[0], 3, null);
			}
			int num = transform.TransUInt16(command, 4);
			if (num > 32)
			{
				return PackCommandBack(command[0], 5, null);
			}
			if (num * 8 != command.Length - 6)
			{
				return PackCommandBack(command[0], 5, null);
			}
			for (int i = 0; i < num; i++)
			{
				int num2 = transform.TransInt32(command, 8 + 8 * i);
				if (num2 > 65535 || num2 < 0)
				{
					return PackCommandBack(command[0], 4, null);
				}
				switch (command[7 + i * 8])
				{
				case 24:
					return PackCommandBack(command[0], 3, null);
				case 25:
					yBuffer.SetBytes((from m in command.SelectMiddle(12 + 8 * i, 2).ToBoolArray()
						select (byte)(m ? 1 : 0)).ToArray(), num2);
					break;
				case 9:
					iBuffer.SetBytes((from m in command.SelectMiddle(12 + 8 * i, 2).ToBoolArray()
						select (byte)(m ? 1 : 0)).ToArray(), num2);
					break;
				case 5:
					eBuffer.SetBytes((from m in command.SelectMiddle(12 + 8 * i, 2).ToBoolArray()
						select (byte)(m ? 1 : 0)).ToArray(), num2);
					break;
				case 13:
					mBuffer.SetBytes((from m in command.SelectMiddle(12 + 8 * i, 2).ToBoolArray()
						select (byte)(m ? 1 : 0)).ToArray(), num2);
					break;
				case 12:
					lBuffer.SetBytes((from m in command.SelectMiddle(12 + 8 * i, 2).ToBoolArray()
						select (byte)(m ? 1 : 0)).ToArray(), num2);
					break;
				case 4:
					dBuffer.SetBytes(command.SelectMiddle(12 + 8 * i, 2), num2 * 2);
					break;
				case 2:
					bBuffer.SetBytes(command.SelectMiddle(12 + 8 * i, 2), num2 * 2);
					break;
				case 6:
					fBuffer.SetBytes(command.SelectMiddle(12 + 8 * i, 2), num2 * 2);
					break;
				case 18:
					rBuffer.SetBytes(command.SelectMiddle(12 + 8 * i, 2), num2 * 2);
					break;
				case 22:
					vBuffer.SetBytes(command.SelectMiddle(12 + 8 * i, 2), num2 * 2);
					break;
				case 26:
					zBuffer.SetBytes(command.SelectMiddle(12 + 8 * i, 2), num2 * 2);
					break;
				case 23:
					wBuffer.SetBytes(command.SelectMiddle(12 + 8 * i, 2), num2 * 2);
					break;
				default:
					return PackCommandBack(command[0], 3, null);
				}
			}
			return PackCommandBack(command[0], 0, null);
		}

		private byte[] StartByCommand(byte[] command)
		{
			isProgramStarted = true;
			return PackCommandBack(command[0], 0, null);
		}

		private byte[] StopByCommand(byte[] command)
		{
			isProgramStarted = false;
			return PackCommandBack(command[0], 0, null);
		}

		private byte[] ReadSystemByCommand(byte[] command)
		{
			if (command[5] == 1)
			{
				return PackCommandBack(result: new byte[2]
				{
					0,
					(byte)(isProgramStarted ? 1 : 2)
				}, cmd: command[0], err: 0);
			}
			if (command[5] == 2)
			{
				byte[] array = new byte[28];
				Encoding.ASCII.GetBytes("F3SP38-6N").CopyTo(array, 0);
				Encoding.ASCII.GetBytes("12345").CopyTo(array, 16);
				array[25] = 17;
				array[26] = 2;
				array[27] = 3;
				return PackCommandBack(command[0], 0, array);
			}
			return PackCommandBack(command[0], 3, null);
		}

		private byte[] ReadSystemDateTime(byte[] command)
		{
			byte[] array = new byte[16];
			DateTime now = DateTime.Now;
			array[0] = BitConverter.GetBytes(now.Year - 2000)[1];
			array[1] = BitConverter.GetBytes(now.Year - 2000)[0];
			array[2] = BitConverter.GetBytes(now.Month)[1];
			array[3] = BitConverter.GetBytes(now.Month)[0];
			array[4] = BitConverter.GetBytes(now.Day)[1];
			array[5] = BitConverter.GetBytes(now.Day)[0];
			array[6] = BitConverter.GetBytes(now.Hour)[1];
			array[7] = BitConverter.GetBytes(now.Hour)[0];
			array[8] = BitConverter.GetBytes(now.Minute)[1];
			array[9] = BitConverter.GetBytes(now.Minute)[0];
			array[10] = BitConverter.GetBytes(now.Second)[1];
			array[11] = BitConverter.GetBytes(now.Second)[0];
			uint value = (uint)(now - new DateTime(now.Year, 1, 1)).TotalSeconds;
			array[12] = BitConverter.GetBytes(value)[3];
			array[13] = BitConverter.GetBytes(value)[2];
			array[14] = BitConverter.GetBytes(value)[1];
			array[15] = BitConverter.GetBytes(value)[0];
			return PackCommandBack(command[0], 0, array);
		}

		private byte[] ReadSpecialModule(byte[] command)
		{
			if (command[4] != 0 || command[5] != 1)
			{
				return PackCommandBack(command[0], 3, null);
			}
			ushort num = transform.TransUInt16(command, 6);
			ushort num2 = transform.TransUInt16(command, 8);
			return PackCommandBack(command[0], 0, specialBuffer.GetBytes(num * 2, num2 * 2));
		}

		private byte[] WriteSpecialModule(byte[] command)
		{
			if (!base.EnableWrite)
			{
				return PackCommandBack(command[0], 3, null);
			}
			if (command[4] != 0 || command[5] != 1)
			{
				return PackCommandBack(command[0], 3, null);
			}
			ushort num = transform.TransUInt16(command, 6);
			ushort num2 = transform.TransUInt16(command, 8);
			if (num2 * 2 != command.Length - 10)
			{
				return PackCommandBack(command[0], 5, null);
			}
			specialBuffer.SetBytes(command.RemoveBegin(10), num * 2);
			return PackCommandBack(command[0], 0, null);
		}

		private byte[] PackCommandBack(byte cmd, byte err, byte[] result)
		{
			if (result == null)
			{
				result = new byte[0];
			}
			byte[] array = new byte[4 + result.Length];
			array[0] = (byte)(cmd + 128);
			array[1] = err;
			array[2] = BitConverter.GetBytes(result.Length)[1];
			array[3] = BitConverter.GetBytes(result.Length)[0];
			result.CopyTo(array, 4);
			return array;
		}

		/// <inheritdoc />
		protected override void LoadFromBytes(byte[] content)
		{
			if (content.Length < 1310720)
			{
				throw new Exception("File is not correct");
			}
			xBuffer.SetBytes(content, 0, 0, 65536);
			yBuffer.SetBytes(content, 65536, 0, 65536);
			iBuffer.SetBytes(content, 131072, 0, 65536);
			eBuffer.SetBytes(content, 196608, 0, 65536);
			mBuffer.SetBytes(content, 262144, 0, 65536);
			lBuffer.SetBytes(content, 327680, 0, 65536);
			dBuffer.SetBytes(content, 393216, 0, 65536);
			bBuffer.SetBytes(content, 524288, 0, 65536);
			fBuffer.SetBytes(content, 655360, 0, 65536);
			rBuffer.SetBytes(content, 786432, 0, 65536);
			vBuffer.SetBytes(content, 917504, 0, 65536);
			zBuffer.SetBytes(content, 1048576, 0, 65536);
			wBuffer.SetBytes(content, 1179648, 0, 65536);
		}

		/// <inheritdoc />
		protected override byte[] SaveToBytes()
		{
			byte[] array = new byte[1310720];
			Array.Copy(xBuffer.GetBytes(), 0, array, 0, 65536);
			Array.Copy(yBuffer.GetBytes(), 0, array, 65536, 65536);
			Array.Copy(iBuffer.GetBytes(), 0, array, 131072, 65536);
			Array.Copy(eBuffer.GetBytes(), 0, array, 196608, 65536);
			Array.Copy(mBuffer.GetBytes(), 0, array, 262144, 65536);
			Array.Copy(lBuffer.GetBytes(), 0, array, 327680, 65536);
			Array.Copy(dBuffer.GetBytes(), 0, array, 393216, 65536);
			Array.Copy(bBuffer.GetBytes(), 0, array, 524288, 65536);
			Array.Copy(fBuffer.GetBytes(), 0, array, 655360, 65536);
			Array.Copy(rBuffer.GetBytes(), 0, array, 786432, 65536);
			Array.Copy(vBuffer.GetBytes(), 0, array, 917504, 65536);
			Array.Copy(zBuffer.GetBytes(), 0, array, 1048576, 65536);
			Array.Copy(wBuffer.GetBytes(), 0, array, 1179648, 65536);
			return array;
		}

		/// <inheritdoc />
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				xBuffer?.Dispose();
				yBuffer?.Dispose();
				iBuffer?.Dispose();
				eBuffer?.Dispose();
				mBuffer?.Dispose();
				lBuffer?.Dispose();
				dBuffer?.Dispose();
				bBuffer?.Dispose();
				fBuffer?.Dispose();
				rBuffer?.Dispose();
				vBuffer?.Dispose();
				zBuffer?.Dispose();
				wBuffer?.Dispose();
				specialBuffer?.Dispose();
			}
			base.Dispose(disposing);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"YokogawaLinkServer[{base.Port}]";
		}
	}
}
