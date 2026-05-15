using System;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Address;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Core.Net;
using HslCommunication.Reflection;

namespace HslCommunication.Profinet.Siemens
{
	/// <summary>
	/// 西门子的Fetch/Write协议的虚拟PLC，可以用来调试通讯，也可以实现一个虚拟的PLC功能，从而开发一套带虚拟环境的上位机系统，可以用来演示，测试。<br />
	/// The virtual PLC of Siemens Fetch/Write protocol can be used for debugging communication, and can also realize a virtual PLC function, so as to develop a set of upper computer system with virtual environment, which can be used for demonstration and testing.
	/// </summary>
	/// <remarks>
	/// 本虚拟服务器的使用需要企业商业授权，否则只能运行24小时。本协议实现的虚拟PLC服务器，主要支持I,Q,M,DB块的数据读写操作，例如 M100, DB1.100，服务器端也可以对位进行读写操作，例如M100.1，DB1.100.2；
	/// 但是不支持连接的远程客户端对位进行操作。
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
	/// </list>
	/// 本虚拟的PLC共有4个DB块，DB1.X, DB2.X, DB3.X, 和其他DB块。对于远程客户端的读写长度，暂时没有限制。
	/// </example>
	public class SiemensFetchWriteServer : DeviceServer
	{
		private SoftBuffer inputBuffer;

		private SoftBuffer outputBuffer;

		private SoftBuffer memeryBuffer;

		private SoftBuffer counterBuffer;

		private SoftBuffer timerBuffer;

		private SoftBuffer db1BlockBuffer;

		private SoftBuffer db2BlockBuffer;

		private SoftBuffer db3BlockBuffer;

		private SoftBuffer dbOtherBlockBuffer;

		private const int DataPoolLength = 65536;

		/// <summary>
		/// 实例化一个S7协议的服务器，支持I，Q，M，DB1.X, DB2.X, DB3.X 数据区块的读写操作<br />
		/// Instantiate a server with S7 protocol, support I, Q, M, DB1.X data block read and write operations
		/// </summary>
		public SiemensFetchWriteServer()
		{
			inputBuffer = new SoftBuffer(65536);
			outputBuffer = new SoftBuffer(65536);
			memeryBuffer = new SoftBuffer(65536);
			db1BlockBuffer = new SoftBuffer(65536);
			db2BlockBuffer = new SoftBuffer(65536);
			db3BlockBuffer = new SoftBuffer(65536);
			dbOtherBlockBuffer = new SoftBuffer(65536);
			counterBuffer = new SoftBuffer(65536);
			timerBuffer = new SoftBuffer(65536);
			base.WordLength = 2;
			base.ByteTransform = new ReverseBytesTransform();
		}

		private OperateResult<SoftBuffer> GetDataAreaFromS7Address(S7AddressData s7Address)
		{
			switch (s7Address.DataCode)
			{
			case 129:
				return OperateResult.CreateSuccessResult(inputBuffer);
			case 130:
				return OperateResult.CreateSuccessResult(outputBuffer);
			case 131:
				return OperateResult.CreateSuccessResult(memeryBuffer);
			case 132:
				if (s7Address.DbBlock == 1)
				{
					return OperateResult.CreateSuccessResult(db1BlockBuffer);
				}
				if (s7Address.DbBlock == 2)
				{
					return OperateResult.CreateSuccessResult(db2BlockBuffer);
				}
				if (s7Address.DbBlock == 3)
				{
					return OperateResult.CreateSuccessResult(db3BlockBuffer);
				}
				return OperateResult.CreateSuccessResult(dbOtherBlockBuffer);
			default:
				return new OperateResult<SoftBuffer>(StringResources.Language.NotSupportedDataType);
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
			dataAreaFromS7Address.Content.SetBytes(value, operateResult.Content.AddressStart / 8);
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
		protected override INetMessage GetNewNetMessage()
		{
			return new FetchWriteMessage();
		}

		/// <inheritdoc />
		protected override OperateResult<byte[]> ReadFromCoreServer(PipeSession session, byte[] receive)
		{
			byte[] value = null;
			if (receive[5] == 3)
			{
				value = WriteByMessage(receive);
			}
			else if (receive[5] == 5)
			{
				value = ReadByMessage(receive);
			}
			return OperateResult.CreateSuccessResult(value);
		}

		private SoftBuffer GetBufferFromCommand(byte[] command)
		{
			if (command[8] == 2)
			{
				return memeryBuffer;
			}
			if (command[8] == 3)
			{
				return inputBuffer;
			}
			if (command[8] == 4)
			{
				return outputBuffer;
			}
			if (command[8] == 1)
			{
				if (command[9] == 1)
				{
					return db1BlockBuffer;
				}
				if (command[9] == 2)
				{
					return db2BlockBuffer;
				}
				if (command[9] == 3)
				{
					return db3BlockBuffer;
				}
				return dbOtherBlockBuffer;
			}
			if (command[8] == 6)
			{
				return counterBuffer;
			}
			if (command[8] == 7)
			{
				return timerBuffer;
			}
			return null;
		}

		private byte[] ReadByMessage(byte[] command)
		{
			SoftBuffer bufferFromCommand = GetBufferFromCommand(command);
			int index = command[10] * 256 + command[11];
			int num = command[12] * 256 + command[13];
			if (bufferFromCommand == null)
			{
				return PackCommandResponse(6, 1, null);
			}
			if (command[8] == 1 || command[8] == 6 || command[8] == 7)
			{
				return PackCommandResponse(6, 0, bufferFromCommand.GetBytes(index, num * 2));
			}
			return PackCommandResponse(6, 0, bufferFromCommand.GetBytes(index, num));
		}

		private byte[] WriteByMessage(byte[] command)
		{
			if (!base.EnableWrite)
			{
				return PackCommandResponse(4, 1, null);
			}
			SoftBuffer bufferFromCommand = GetBufferFromCommand(command);
			int destIndex = command[10] * 256 + command[11];
			int num = command[12] * 256 + command[13];
			if (bufferFromCommand == null)
			{
				return PackCommandResponse(4, 1, null);
			}
			if (command[8] == 1 || command[8] == 6 || command[8] == 7)
			{
				if (num != (command.Length - 16) / 2)
				{
					return PackCommandResponse(4, 1, null);
				}
				bufferFromCommand.SetBytes(command.RemoveBegin(16), destIndex);
				return PackCommandResponse(4, 0, null);
			}
			if (num != command.Length - 16)
			{
				return PackCommandResponse(4, 1, null);
			}
			bufferFromCommand.SetBytes(command.RemoveBegin(16), destIndex);
			return PackCommandResponse(4, 0, null);
		}

		private byte[] PackCommandResponse(byte opCode, byte err, byte[] data)
		{
			if (data == null)
			{
				data = new byte[0];
			}
			byte[] array = new byte[16 + data.Length];
			array[0] = 83;
			array[1] = 53;
			array[2] = 16;
			array[3] = 1;
			array[4] = 3;
			array[5] = opCode;
			array[6] = 15;
			array[7] = 3;
			array[8] = err;
			array[9] = byte.MaxValue;
			array[10] = 7;
			if (data.Length != 0)
			{
				data.CopyTo(array, 16);
			}
			return array;
		}

		/// <inheritdoc />
		protected override void LoadFromBytes(byte[] content)
		{
			if (content.Length < 589824)
			{
				throw new Exception("File is not correct");
			}
			inputBuffer.SetBytes(content, 0, 0, 65536);
			outputBuffer.SetBytes(content, 65536, 0, 65536);
			memeryBuffer.SetBytes(content, 131072, 0, 65536);
			db1BlockBuffer.SetBytes(content, 196608, 0, 65536);
			db2BlockBuffer.SetBytes(content, 262144, 0, 65536);
			db3BlockBuffer.SetBytes(content, 327680, 0, 65536);
			dbOtherBlockBuffer.SetBytes(content, 393216, 0, 65536);
			counterBuffer.SetBytes(content, 458752, 0, 65536);
			timerBuffer.SetBytes(content, 524288, 0, 65536);
		}

		/// <inheritdoc />
		protected override byte[] SaveToBytes()
		{
			byte[] array = new byte[589824];
			Array.Copy(inputBuffer.GetBytes(), 0, array, 0, 65536);
			Array.Copy(outputBuffer.GetBytes(), 0, array, 65536, 65536);
			Array.Copy(memeryBuffer.GetBytes(), 0, array, 131072, 65536);
			Array.Copy(db1BlockBuffer.GetBytes(), 0, array, 196608, 65536);
			Array.Copy(db2BlockBuffer.GetBytes(), 0, array, 262144, 65536);
			Array.Copy(db3BlockBuffer.GetBytes(), 0, array, 327680, 65536);
			Array.Copy(dbOtherBlockBuffer.GetBytes(), 0, array, 393216, 65536);
			Array.Copy(counterBuffer.GetBytes(), 0, array, 458752, 65536);
			Array.Copy(timerBuffer.GetBytes(), 0, array, 524288, 65536);
			return array;
		}

		/// <inheritdoc />
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				inputBuffer?.Dispose();
				outputBuffer?.Dispose();
				memeryBuffer?.Dispose();
				db1BlockBuffer?.Dispose();
				db2BlockBuffer?.Dispose();
				db3BlockBuffer?.Dispose();
				dbOtherBlockBuffer?.Dispose();
			}
			base.Dispose(disposing);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"SiemensFetchWriteServer[{base.Port}]";
		}
	}
}
