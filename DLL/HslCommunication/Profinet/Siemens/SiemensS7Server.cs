using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Address;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Core.Net;
using HslCommunication.Core.Pipe;
using HslCommunication.Profinet.Siemens.Helper;
using HslCommunication.Reflection;

namespace HslCommunication.Profinet.Siemens
{
	/// <summary>
	/// 西门子S7协议的虚拟服务器，支持TCP协议，模拟的是1200的PLC进行通信，在客户端进行操作操作的时候，最好是选择1200的客户端对象进行通信。<br />
	/// The virtual server of Siemens S7 protocol supports TCP protocol. It simulates 1200 PLC for communication. When the client is operating, it is best to select the 1200 client object for communication.
	/// </summary>
	/// <remarks>
	/// 本西门子的虚拟PLC仅限商业授权用户使用，感谢支持。
	/// <note type="important">对于200smartPLC的V区，就是DB1.X，例如，V100=DB1.100</note>
	/// </remarks>
	/// <example>
	/// 地址支持的列表如下：
	/// <list type="table">
	///   <listheader>
	///     <term>地址名称</term>
	///     <term>地址代号</term>
	///     <term>示例</term>
	///     <term>地址进制</term>
	///     <term>字操作</term>
	///     <term>位操作</term>
	///     <term>备注</term>
	///   </listheader>
	///   <item>
	///     <term>中间寄存器</term>
	///     <term>M</term>
	///     <term>M100,M200</term>
	///     <term>10</term>
	///     <term>√</term>
	///     <term>√</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>输入寄存器</term>
	///     <term>I</term>
	///     <term>I100,I200</term>
	///     <term>10</term>
	///     <term>√</term>
	///     <term>√</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>输出寄存器</term>
	///     <term>Q</term>
	///     <term>Q100,Q200</term>
	///     <term>10</term>
	///     <term>√</term>
	///     <term>√</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>DB块寄存器</term>
	///     <term>DB</term>
	///     <term>DB1.100,DB1.200</term>
	///     <term>10</term>
	///     <term>√</term>
	///     <term>√</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>V寄存器</term>
	///     <term>V</term>
	///     <term>V100,V200</term>
	///     <term>10</term>
	///     <term>√</term>
	///     <term>√</term>
	///     <term>V寄存器本质就是DB块1</term>
	///   </item>
	///   <item>
	///     <term>定时器的值</term>
	///     <term>T</term>
	///     <term>T100,T200</term>
	///     <term>10</term>
	///     <term>√</term>
	///     <term>√</term>
	///     <term>未测试通过</term>
	///   </item>
	///   <item>
	///     <term>计数器的值</term>
	///     <term>C</term>
	///     <term>C100,C200</term>
	///     <term>10</term>
	///     <term>√</term>
	///     <term>√</term>
	///     <term>未测试通过</term>
	///   </item>
	/// </list>
	/// 你可以很快速并且简单的创建一个虚拟的s7服务器
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Profinet\SiemensS7ServerExample.cs" region="UseExample1" title="简单的创建服务器" />
	/// 当然如果需要高级的服务器，指定日志，限制客户端的IP地址，获取客户端发送的信息，在服务器初始化的时候就要参照下面的代码：
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Profinet\SiemensS7ServerExample.cs" region="UseExample4" title="定制服务器" />
	/// 服务器创建好之后，我们就可以对服务器进行一些读写的操作了，下面的代码是基础的BCL类型的读写操作。
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Profinet\SiemensS7ServerExample.cs" region="ReadWriteExample" title="基础的读写示例" />
	/// 高级的对于byte数组类型的数据进行批量化的读写操作如下：   
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Profinet\SiemensS7ServerExample.cs" region="BytesReadWrite" title="字节的读写示例" />
	/// 更高级操作请参见源代码。
	/// </example>
	public class SiemensS7Server : DeviceServer
	{
		private SoftBuffer systemBuffer;

		private SoftBuffer inputBuffer;

		private SoftBuffer outputBuffer;

		private SoftBuffer memeryBuffer;

		private SoftBuffer countBuffer;

		private SoftBuffer timerBuffer;

		private SoftBuffer aiBuffer;

		private SoftBuffer aqBuffer;

		private Dictionary<int, SoftBuffer> dbBlockBuffer;

		private const int DataPoolLength = 65536;

		/// <summary>
		/// 实例化一个S7协议的服务器，支持I，Q，M，DB1.X, DB2.X, DB3.X 数据区块的读写操作<br />
		/// Instantiate a server with S7 protocol, support I, Q, M, DB1.X data block read and write operations
		/// </summary>
		public SiemensS7Server()
		{
			inputBuffer = new SoftBuffer(65536);
			outputBuffer = new SoftBuffer(65536);
			memeryBuffer = new SoftBuffer(65536);
			countBuffer = new SoftBuffer(131072);
			timerBuffer = new SoftBuffer(131072);
			aiBuffer = new SoftBuffer(65536);
			aqBuffer = new SoftBuffer(65536);
			systemBuffer = new SoftBuffer(65536);
			systemBuffer.SetBytes("43 50 55 20 32 32 36 20 43 4E 20 20 20 20 20 20 30 32 30 31".ToHexBytes(), 0);
			base.WordLength = 2;
			base.ByteTransform = new ReverseBytesTransform();
			dbBlockBuffer = new Dictionary<int, SoftBuffer>();
			dbBlockBuffer.Add(1, new SoftBuffer(65536));
			dbBlockBuffer.Add(2, new SoftBuffer(65536));
			dbBlockBuffer.Add(3, new SoftBuffer(65536));
		}

		/// <summary>
		/// 根据S7格式的地址，获取当前的数据缓存类对象
		/// </summary>
		/// <param name="s7Address">S7格式的数据地址信息</param>
		/// <returns>内存缓存对象</returns>
		private OperateResult<SoftBuffer> GetDataAreaFromS7Address(S7AddressData s7Address)
		{
			switch (s7Address.DataCode)
			{
			case 3:
				return OperateResult.CreateSuccessResult(systemBuffer);
			case 129:
				return OperateResult.CreateSuccessResult(inputBuffer);
			case 130:
				return OperateResult.CreateSuccessResult(outputBuffer);
			case 131:
				return OperateResult.CreateSuccessResult(memeryBuffer);
			case 132:
				if (dbBlockBuffer.ContainsKey(s7Address.DbBlock))
				{
					return OperateResult.CreateSuccessResult(dbBlockBuffer[s7Address.DbBlock]);
				}
				return new OperateResult<SoftBuffer>(10, StringResources.Language.SiemensError000A);
			case 30:
				return OperateResult.CreateSuccessResult(countBuffer);
			case 31:
				return OperateResult.CreateSuccessResult(timerBuffer);
			case 6:
				return OperateResult.CreateSuccessResult(aiBuffer);
			case 7:
				return OperateResult.CreateSuccessResult(aqBuffer);
			default:
				return new OperateResult<SoftBuffer>(6, StringResources.Language.SiemensError0006);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.SiemensS7Net.Read(System.String,System.UInt16)" />
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			OperateResult<S7AddressData> operateResult = S7AddressData.ParseFrom(address, length);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			OperateResult<SoftBuffer> dataAreaFromS7Address = GetDataAreaFromS7Address(operateResult.Content);
			if (!dataAreaFromS7Address.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(dataAreaFromS7Address);
			}
			if (operateResult.Content.DataCode == 30 || operateResult.Content.DataCode == 31)
			{
				return OperateResult.CreateSuccessResult(dataAreaFromS7Address.Content.GetBytes(operateResult.Content.AddressStart * 2, length * 2));
			}
			return OperateResult.CreateSuccessResult(dataAreaFromS7Address.Content.GetBytes(operateResult.Content.AddressStart / 8, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.SiemensS7Net.Write(System.String,System.Byte[])" />
		[HslMqttApi("WriteByteArray", "")]
		public override OperateResult Write(string address, byte[] value)
		{
			OperateResult<S7AddressData> operateResult = S7AddressData.ParseFrom(address);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			OperateResult<SoftBuffer> dataAreaFromS7Address = GetDataAreaFromS7Address(operateResult.Content);
			if (!dataAreaFromS7Address.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(dataAreaFromS7Address);
			}
			if (operateResult.Content.DataCode == 30 || operateResult.Content.DataCode == 31)
			{
				dataAreaFromS7Address.Content.SetBytes(value, operateResult.Content.AddressStart * 2);
			}
			else
			{
				dataAreaFromS7Address.Content.SetBytes(value, operateResult.Content.AddressStart / 8);
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.SiemensS7Net.ReadByte(System.String)" />
		[HslMqttApi("ReadByte", "")]
		public OperateResult<byte> ReadByte(string address)
		{
			return ByteTransformHelper.GetResultFromArray(Read(address, 1));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.SiemensS7Net.Write(System.String,System.Byte)" />
		[HslMqttApi("WriteByte", "")]
		public OperateResult Write(string address, byte value)
		{
			return Write(address, new byte[1] { value });
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.SiemensS7Net.ReadBool(System.String)" />
		[HslMqttApi("ReadBool", "")]
		public override OperateResult<bool> ReadBool(string address)
		{
			OperateResult<S7AddressData> operateResult = S7AddressData.ParseFrom(address);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool>(operateResult);
			}
			OperateResult<SoftBuffer> dataAreaFromS7Address = GetDataAreaFromS7Address(operateResult.Content);
			if (!dataAreaFromS7Address.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool>(dataAreaFromS7Address);
			}
			return OperateResult.CreateSuccessResult(dataAreaFromS7Address.Content.GetBool(operateResult.Content.AddressStart));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.SiemensS7Net.Write(System.String,System.Boolean)" />
		[HslMqttApi("WriteBool", "")]
		public override OperateResult Write(string address, bool value)
		{
			OperateResult<S7AddressData> operateResult = S7AddressData.ParseFrom(address);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<SoftBuffer> dataAreaFromS7Address = GetDataAreaFromS7Address(operateResult.Content);
			if (!dataAreaFromS7Address.IsSuccess)
			{
				return dataAreaFromS7Address;
			}
			dataAreaFromS7Address.Content.SetBool(value, operateResult.Content.AddressStart);
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc />
		public override OperateResult Write(string address, string value, Encoding encoding)
		{
			return SiemensS7Helper.Write(this, SiemensPLCS.S1200, address, value, encoding);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.Helper.SiemensS7Helper.WriteWString(HslCommunication.Core.IReadWriteNet,HslCommunication.Profinet.Siemens.SiemensPLCS,System.String,System.String)" />
		[HslMqttApi(ApiTopic = "WriteWString", Description = "写入unicode编码的字符串，支持中文")]
		public OperateResult WriteWString(string address, string value)
		{
			return SiemensS7Helper.WriteWString(this, SiemensPLCS.S1200, address, value);
		}

		/// <inheritdoc />
		public override OperateResult<string> ReadString(string address, ushort length, Encoding encoding)
		{
			return (length == 0) ? ReadString(address, encoding) : base.ReadString(address, length, encoding);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.SiemensS7Server.ReadString(System.String,System.Text.Encoding)" />
		[HslMqttApi("ReadS7String", "读取S7格式的字符串")]
		public OperateResult<string> ReadString(string address)
		{
			return ReadString(address, Encoding.ASCII);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.Helper.SiemensS7Helper.ReadString(HslCommunication.Core.IReadWriteNet,HslCommunication.Profinet.Siemens.SiemensPLCS,System.String,System.Text.Encoding)" />
		public OperateResult<string> ReadString(string address, Encoding encoding)
		{
			return SiemensS7Helper.ReadString(this, SiemensPLCS.S1200, address, encoding);
		}

		/// <summary>
		/// 读取西门子的地址的字符串信息，这个信息是和西门子绑定在一起，长度随西门子的信息动态变化的<br />
		/// Read the Siemens address string information. This information is bound to Siemens and its length changes dynamically with the Siemens information
		/// </summary>
		/// <param name="address">数据地址，具体的格式需要参照类的说明文档</param>
		/// <returns>带有是否成功的字符串结果类对象</returns>
		[HslMqttApi("ReadWString", "读取S7格式的双字节字符串")]
		public OperateResult<string> ReadWString(string address)
		{
			return SiemensS7Helper.ReadWString(this, SiemensPLCS.S1200, address);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.Helper.SiemensS7Helper.Write(HslCommunication.Core.IReadWriteNet,HslCommunication.Profinet.Siemens.SiemensPLCS,System.String,System.String,System.Text.Encoding)" />
		public override async Task<OperateResult> WriteAsync(string address, string value, Encoding encoding)
		{
			return await SiemensS7Helper.WriteAsync(this, SiemensPLCS.S1200, address, value, encoding);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.SiemensS7Server.WriteWString(System.String,System.String)" />
		public async Task<OperateResult> WriteWStringAsync(string address, string value)
		{
			return await SiemensS7Helper.WriteWStringAsync(this, SiemensPLCS.S1200, address, value);
		}

		/// <inheritdoc />
		public override async Task<OperateResult<string>> ReadStringAsync(string address, ushort length, Encoding encoding)
		{
			return (length != 0) ? (await base.ReadStringAsync(address, length, encoding)) : (await ReadStringAsync(address, encoding));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.SiemensS7Server.ReadString(System.String)" />
		public async Task<OperateResult<string>> ReadStringAsync(string address)
		{
			return await ReadStringAsync(address, Encoding.ASCII);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.SiemensS7Server.ReadString(System.String,System.Text.Encoding)" />
		public async Task<OperateResult<string>> ReadStringAsync(string address, Encoding encoding)
		{
			return await SiemensS7Helper.ReadStringAsync(this, SiemensPLCS.S1200, address, encoding);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.SiemensS7Server.ReadWString(System.String)" />
		public async Task<OperateResult<string>> ReadWStringAsync(string address)
		{
			return await SiemensS7Helper.ReadWStringAsync(this, SiemensPLCS.S1200, address);
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new S7Message();
		}

		/// <inheritdoc />
		protected override OperateResult ThreadPoolLoginAfterClientCheck(PipeSession session, IPEndPoint endPoint)
		{
			if (IsNeedShakeHands())
			{
				CommunicationPipe communication = session.Communication;
				OperateResult<byte[]> operateResult = communication.ReceiveMessage(new S7Message(), null, useActivePush: false, null, delegate(byte[] m)
				{
					LogRevcMessage(m, session);
				});
				if (!operateResult.IsSuccess)
				{
					return operateResult;
				}
				if (operateResult.Content == null || operateResult.Content.Length < 10)
				{
					return new OperateResult("Data is too short: " + operateResult.Content.ToHexString(' '));
				}
				operateResult.Content[5] = 208;
				operateResult.Content[6] = operateResult.Content[8];
				operateResult.Content[7] = operateResult.Content[9];
				operateResult.Content[8] = 0;
				operateResult.Content[9] = 12;
				LogSendMessage(operateResult.Content, session);
				OperateResult operateResult2 = communication.Send(operateResult.Content);
				if (!operateResult2.IsSuccess)
				{
					return operateResult2;
				}
				OperateResult<byte[]> operateResult3 = communication.ReceiveMessage(new S7Message(), null, useActivePush: false, null, delegate(byte[] m)
				{
					LogRevcMessage(m, session);
				});
				if (!operateResult3.IsSuccess)
				{
					return operateResult3;
				}
				byte[] array = SoftBasic.HexStringToBytes("03 00 00 1B 02 f0 80 32 03 00 00 00 00 00 08 00 00 00 00 f0 01 00 01 00 f0 00 f0");
				LogSendMessage(array, session);
				OperateResult operateResult4 = communication.Send(array);
				if (!operateResult4.IsSuccess)
				{
					return operateResult4;
				}
			}
			return base.ThreadPoolLoginAfterClientCheck(session, endPoint);
		}

		/// <summary>
		/// 获取是否需要进行握手报文信息
		/// </summary>
		/// <returns>是否需要进行握手操作</returns>
		protected virtual bool IsNeedShakeHands()
		{
			return true;
		}

		/// <inheritdoc />
		protected override OperateResult<byte[]> ReadFromCoreServer(PipeSession session, byte[] receive)
		{
			byte[] array = null;
			try
			{
				if (receive[17] == 4)
				{
					array = ReadByMessage(receive);
				}
				else if (receive[17] == 5)
				{
					array = WriteByMessage(receive);
				}
				else
				{
					if (receive[17] != 0)
					{
						return new OperateResult<byte[]>(StringResources.Language.NotSupportedFunction);
					}
					array = ReadPlcType();
				}
				receive.SelectMiddle(11, 2).CopyTo(array, 11);
				return OperateResult.CreateSuccessResult(array);
			}
			catch (Exception ex)
			{
				return new OperateResult<byte[]>(ex.Message);
			}
		}

		private byte[] ReadPlcType()
		{
			return SoftBasic.HexStringToBytes("03 00 00 7D 02 F0 80 32 07 00 00 00 01 00 0C 00 60 00 01 12 08 12 84 01 01 00 00 00 00 FF 09 00 5C 00 11 00 00 00 1C 00 03 00 01 36 45 53 37 20 32 31 35 2D 31 41 47 34 30 2D 30 58 42 30 20 00 00 00 06 20 20 00 06 36 45 53 37 20 32 31 35 2D 31 41 47 34 30 2D 30 58 42 30 20 00 00 00 06 20 20 00 07 36 45 53 37 20 32 31 35 2D 31 41 47 34 30 2D 30 58 42 30 20 00 00 56 04 02 01");
		}

		/// <summary>
		/// 将读取的结果数据内容进行打包，返回客户端读取
		/// </summary>
		/// <param name="command">接收的命令信息</param>
		/// <param name="content">读取的原始字节信息</param>
		/// <returns>返回客户端的原始报文信息</returns>
		protected virtual byte[] PackReadBack(byte[] command, List<byte> content)
		{
			if (content.Count > 226)
			{
				content = new List<byte>(PackReadWordCommandBack(5, null));
			}
			byte[] array = new byte[21 + content.Count];
			SoftBasic.HexStringToBytes("03 00 00 1A 02 F0 80 32 03 00 00 00 01 00 02 00 05 00 00 04 01").CopyTo(array, 0);
			array[2] = (byte)(array.Length / 256);
			array[3] = (byte)(array.Length % 256);
			array[15] = (byte)(content.Count / 256);
			array[16] = (byte)(content.Count % 256);
			array[20] = command[18];
			content.CopyTo(array, 21);
			return array;
		}

		private byte[] ReadByMessage(byte[] packCommand)
		{
			List<byte> list = new List<byte>();
			int num = packCommand[18];
			int num2 = 19;
			for (int i = 0; i < num; i++)
			{
				byte b = packCommand[num2 + 1];
				byte[] command = packCommand.SelectMiddle(num2, b + 2);
				num2 += b + 2;
				list.AddRange(ReadByCommand(command));
			}
			return PackReadBack(packCommand, list);
		}

		private byte[] ReadByCommand(byte[] command)
		{
			if (command[3] == 1)
			{
				int num = command[9] * 65536 + command[10] * 256 + command[11];
				ushort dbBlock = base.ByteTransform.TransUInt16(command, 6);
				ushort num2 = base.ByteTransform.TransUInt16(command, 4);
				OperateResult<SoftBuffer> dataAreaFromS7Address = GetDataAreaFromS7Address(new S7AddressData
				{
					AddressStart = num,
					DataCode = command[8],
					DbBlock = dbBlock,
					Length = 1
				});
				if (!dataAreaFromS7Address.IsSuccess)
				{
					throw new Exception(dataAreaFromS7Address.Message);
				}
				return PackReadBitCommandBack(dataAreaFromS7Address.Content.GetBool(num));
			}
			if (command[3] == 30 || command[3] == 31)
			{
				ushort num3 = base.ByteTransform.TransUInt16(command, 4);
				int num4 = command[9] * 65536 + command[10] * 256 + command[11];
				OperateResult<SoftBuffer> dataAreaFromS7Address2 = GetDataAreaFromS7Address(new S7AddressData
				{
					AddressStart = num4,
					DataCode = command[8],
					DbBlock = 0,
					Length = num3
				});
				if (!dataAreaFromS7Address2.IsSuccess)
				{
					throw new Exception(dataAreaFromS7Address2.Message);
				}
				return PackReadCTCommandBack(dataAreaFromS7Address2.Content.GetBytes(num4 * 2, num3 * 2), (command[3] == 30) ? 3 : 5);
			}
			ushort num5 = base.ByteTransform.TransUInt16(command, 4);
			if (command[3] == 4)
			{
				num5 = (ushort)(num5 * 2);
			}
			ushort dbBlock2 = base.ByteTransform.TransUInt16(command, 6);
			int num6 = (command[9] * 65536 + command[10] * 256 + command[11]) / 8;
			OperateResult<SoftBuffer> dataAreaFromS7Address3 = GetDataAreaFromS7Address(new S7AddressData
			{
				AddressStart = num6,
				DataCode = command[8],
				DbBlock = dbBlock2,
				Length = num5
			});
			if (!dataAreaFromS7Address3.IsSuccess)
			{
				return PackReadWordCommandBack((short)dataAreaFromS7Address3.ErrorCode, null);
			}
			return PackReadWordCommandBack(0, dataAreaFromS7Address3.Content.GetBytes(num6, num5));
		}

		private byte[] PackReadWordCommandBack(short err, byte[] result)
		{
			if (err > 0)
			{
				byte[] array = new byte[4];
				BitConverter.GetBytes(err).CopyTo(array, 0);
				return array;
			}
			byte[] array2 = new byte[4 + result.Length];
			array2[0] = byte.MaxValue;
			array2[1] = 4;
			base.ByteTransform.TransByte((ushort)(result.Length * 8)).CopyTo(array2, 2);
			result.CopyTo(array2, 4);
			return array2;
		}

		private byte[] PackReadCTCommandBack(byte[] result, int dataLength)
		{
			byte[] array = new byte[4 + result.Length * dataLength / 2];
			array[0] = byte.MaxValue;
			array[1] = 9;
			base.ByteTransform.TransByte((ushort)(array.Length - 4)).CopyTo(array, 2);
			for (int i = 0; i < result.Length / 2; i++)
			{
				result.SelectMiddle(i * 2, 2).CopyTo(array, 4 + dataLength - 2 + i * dataLength);
			}
			return array;
		}

		private byte[] PackReadBitCommandBack(bool value)
		{
			return new byte[5]
			{
				255,
				3,
				0,
				1,
				(byte)(value ? 1u : 0u)
			};
		}

		private byte[] PackWriteBack(byte[] packCommand, byte status)
		{
			return PackWriteBack(packCommand, new byte[1] { status });
		}

		/// <summary>
		/// 创建返回的报文信息
		/// </summary>
		/// <param name="packCommand">接收到的报文命令</param>
		/// <param name="status">返回的状态信息</param>
		/// <returns>返回的原始字节数组</returns>
		protected virtual byte[] PackWriteBack(byte[] packCommand, byte[] status)
		{
			byte[] array = new byte[21 + status.Length];
			SoftBasic.HexStringToBytes("03 00 00 16 02 F0 80 32 03 00 00 00 01 00 02 00 01 00 00 05 01").CopyTo(array, 0);
			array[20] = (byte)status.Length;
			status.CopyTo(array, 21);
			array[2] = BitConverter.GetBytes(array.Length)[1];
			array[3] = BitConverter.GetBytes(array.Length)[0];
			return array;
		}

		private byte[] WriteByMessage(byte[] packCommand)
		{
			if (!base.EnableWrite)
			{
				return PackWriteBack(packCommand, 4);
			}
			int num = packCommand[18];
			byte[] array = new byte[num];
			int num2 = 19 + num * 12;
			for (int i = 0; i < num; i++)
			{
				if (packCommand[22 + 12 * i] == 2 || packCommand[22 + 12 * i] == 4)
				{
					ushort dbBlock = base.ByteTransform.TransUInt16(packCommand, 25 + 12 * i);
					int num3 = base.ByteTransform.TransInt16(packCommand, 23 + 12 * i);
					if (packCommand[22 + 12 * i] == 4)
					{
						num3 *= 2;
					}
					int num4 = 0;
					num4 = ((packCommand[27 + 12 * i] < 28 || packCommand[27 + 12 * i] > 31) ? ((packCommand[28 + 12 * i] * 65536 + packCommand[29 + 12 * i] * 256 + packCommand[30 + 12 * i]) / 8) : ((packCommand[28 + 12 * i] * 65536 + packCommand[29 + 12 * i] * 256 + packCommand[30 + 12 * i]) * 2));
					byte[] data = base.ByteTransform.TransByte(packCommand, num2 + 4, num3);
					num2 += 4 + num3;
					if (i < num - 1 && num3 % 2 == 1)
					{
						num2++;
					}
					OperateResult<SoftBuffer> dataAreaFromS7Address = GetDataAreaFromS7Address(new S7AddressData
					{
						DataCode = packCommand[27 + 12 * i],
						DbBlock = dbBlock,
						Length = 1
					});
					if (!dataAreaFromS7Address.IsSuccess)
					{
						array[i] = (byte)dataAreaFromS7Address.ErrorCode;
						continue;
					}
					dataAreaFromS7Address.Content.SetBytes(data, num4);
					array[i] = byte.MaxValue;
				}
				else
				{
					ushort dbBlock2 = base.ByteTransform.TransUInt16(packCommand, 25 + 12 * i);
					int destIndex = packCommand[28 + 12 * i] * 65536 + packCommand[29 + 12 * i] * 256 + packCommand[30 + 12 * i];
					bool value = packCommand[num2 + 4] != 0;
					num2 += 5;
					if (i < num - 1)
					{
						num2++;
					}
					OperateResult<SoftBuffer> dataAreaFromS7Address2 = GetDataAreaFromS7Address(new S7AddressData
					{
						DataCode = packCommand[27 + 12 * i],
						DbBlock = dbBlock2,
						Length = 1
					});
					if (!dataAreaFromS7Address2.IsSuccess)
					{
						array[i] = (byte)dataAreaFromS7Address2.ErrorCode;
						continue;
					}
					dataAreaFromS7Address2.Content.SetBool(value, destIndex);
					array[i] = byte.MaxValue;
				}
			}
			return PackWriteBack(packCommand, array);
		}

		/// <summary>
		/// 新增一个独立的DB块数据区，如果这个DB块已经存在，则新增无效。<br />
		/// Add a separate DB block data area, if the DB block already exists, the new one is invalid.
		/// </summary>
		/// <param name="db">DB块ID信息</param>
		public void AddDbBlock(int db)
		{
			if (!dbBlockBuffer.ContainsKey(db))
			{
				dbBlockBuffer.Add(db, new SoftBuffer(65536));
			}
		}

		/// <summary>
		/// 移除指定的DB块数据区，如果这个DB块不存在的话，操作无效，本方法无法移除1，2，3的DB块<br />
		/// Remove the specified DB block data area, if the DB block does not exist, the operation is invalid, and this method cannot remove the DB block of 1, 2, 3
		/// </summary>
		/// <param name="db">指定的ID信息</param>
		public void RemoveDbBlock(int db)
		{
			if (db != 1 && db != 2 && db != 3 && dbBlockBuffer.ContainsKey(db))
			{
				dbBlockBuffer.Remove(db);
			}
		}

		/// <inheritdoc />
		protected override void LoadFromBytes(byte[] content)
		{
			if (content.Length < 458752)
			{
				throw new Exception("File is not correct");
			}
			inputBuffer.SetBytes(content, 0, 0, 65536);
			outputBuffer.SetBytes(content, 65536, 0, 65536);
			memeryBuffer.SetBytes(content, 131072, 0, 65536);
			dbBlockBuffer[1].SetBytes(content, 196608, 0, 65536);
			dbBlockBuffer[2].SetBytes(content, 262144, 0, 65536);
			dbBlockBuffer[3].SetBytes(content, 327680, 0, 65536);
			if (content.Length >= 720896)
			{
				countBuffer.SetBytes(content, 458752, 0, 131072);
				timerBuffer.SetBytes(content, 589824, 0, 131072);
			}
		}

		/// <inheritdoc />
		protected override byte[] SaveToBytes()
		{
			byte[] array = new byte[720896];
			Array.Copy(inputBuffer.GetBytes(), 0, array, 0, 65536);
			Array.Copy(outputBuffer.GetBytes(), 0, array, 65536, 65536);
			Array.Copy(memeryBuffer.GetBytes(), 0, array, 131072, 65536);
			Array.Copy(dbBlockBuffer[1].GetBytes(), 0, array, 196608, 65536);
			Array.Copy(dbBlockBuffer[2].GetBytes(), 0, array, 262144, 65536);
			Array.Copy(dbBlockBuffer[3].GetBytes(), 0, array, 327680, 65536);
			Array.Copy(countBuffer.GetBytes(), 0, array, 458752, 131072);
			Array.Copy(timerBuffer.GetBytes(), 0, array, 589824, 131072);
			return array;
		}

		/// <inheritdoc />
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				systemBuffer?.Dispose();
				inputBuffer?.Dispose();
				outputBuffer?.Dispose();
				memeryBuffer?.Dispose();
				countBuffer?.Dispose();
				timerBuffer?.Dispose();
				foreach (SoftBuffer value in dbBlockBuffer.Values)
				{
					value?.Dispose();
				}
				aiBuffer?.Dispose();
				aqBuffer?.Dispose();
			}
			base.Dispose(disposing);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"SiemensS7Server[{base.Port}]";
		}
	}
}
