using System;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Address;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Core.Net;
using HslCommunication.Reflection;

namespace HslCommunication.Profinet.Toyota
{
	/// <summary>
	/// 丰田工机的PLC对象信息
	/// </summary>
	public class ToyoPucServer : DeviceServer
	{
		private SoftBuffer buffer1;

		private SoftBuffer buffer00;

		private SoftBuffer buffer01;

		private SoftBuffer buffer02;

		private SoftBuffer buffer03;

		private SoftBuffer buffer08;

		private const int DataPoolLength = 65536;

		private int station = 0;

		/// <summary>
		/// 实例化一个丰田工机PLC的网口和串口服务器，支持数据读写操作
		/// </summary>
		public ToyoPucServer()
		{
			buffer1 = new SoftBuffer(65536);
			buffer00 = new SoftBuffer(65536);
			buffer01 = new SoftBuffer(65536);
			buffer02 = new SoftBuffer(65536);
			buffer03 = new SoftBuffer(65536);
			buffer08 = new SoftBuffer(65536);
			base.ByteTransform = new RegularByteTransform();
			base.WordLength = 1;
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new ToyoPucMessage();
		}

		/// <inheritdoc />
		protected override byte[] SaveToBytes()
		{
			byte[] array = new byte[393216];
			buffer1.GetBytes().CopyTo(array, 0);
			buffer00.GetBytes().CopyTo(array, 65536);
			buffer01.GetBytes().CopyTo(array, 131072);
			buffer02.GetBytes().CopyTo(array, 196608);
			buffer03.GetBytes().CopyTo(array, 262144);
			buffer08.GetBytes().CopyTo(array, 327680);
			return array;
		}

		/// <inheritdoc />
		protected override void LoadFromBytes(byte[] content)
		{
			if (content.Length < 393216)
			{
				throw new Exception("File is not correct");
			}
			buffer1.SetBytes(content, 0, 65536);
			buffer00.SetBytes(content, 65536, 65536);
			buffer01.SetBytes(content, 131072, 65536);
			buffer02.SetBytes(content, 196608, 65536);
			buffer03.SetBytes(content, 262144, 65536);
			buffer08.SetBytes(content, 327680, 65536);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Toyota.ToyoPuc.Read(System.String,System.UInt16)" />
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			OperateResult<ToyoPucAddress> operateResult = ToyoPucAddress.ParseFrom(address, length, isBit: false);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			ToyoPucAddress content = operateResult.Content;
			if (content.PRG >= 0)
			{
				if (content.PRG == 0)
				{
					return OperateResult.CreateSuccessResult(buffer00.GetBytes(content.AddressStart * 2, length * 2));
				}
				if (content.PRG == 1)
				{
					return OperateResult.CreateSuccessResult(buffer01.GetBytes(content.AddressStart * 2, length * 2));
				}
				if (content.PRG == 2)
				{
					return OperateResult.CreateSuccessResult(buffer02.GetBytes(content.AddressStart * 2, length * 2));
				}
				if (content.PRG == 3)
				{
					return OperateResult.CreateSuccessResult(buffer03.GetBytes(content.AddressStart * 2, length * 2));
				}
				if (content.PRG == 8)
				{
					return OperateResult.CreateSuccessResult(buffer08.GetBytes(content.AddressStart * 2, length * 2));
				}
				return new OperateResult<byte[]>(StringResources.Language.NotSupportedDataType);
			}
			return OperateResult.CreateSuccessResult(buffer1.GetBytes(content.AddressStart * 2, length * 2));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Toyota.ToyoPuc.Write(System.String,System.Byte[])" />
		[HslMqttApi("WriteByteArray", "")]
		public override OperateResult Write(string address, byte[] value)
		{
			OperateResult<ToyoPucAddress> operateResult = ToyoPucAddress.ParseFrom(address, 1, isBit: false);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			ToyoPucAddress content = operateResult.Content;
			if (content.PRG >= 0)
			{
				if (content.PRG == 0)
				{
					buffer00.SetBytes(value, content.AddressStart * 2);
				}
				else if (content.PRG == 1)
				{
					buffer01.SetBytes(value, content.AddressStart * 2);
				}
				else if (content.PRG == 2)
				{
					buffer02.SetBytes(value, content.AddressStart * 2);
				}
				else if (content.PRG == 3)
				{
					buffer03.SetBytes(value, content.AddressStart * 2);
				}
				else
				{
					if (content.PRG != 8)
					{
						return new OperateResult<byte[]>(StringResources.Language.NotSupportedDataType);
					}
					buffer08.SetBytes(value, content.AddressStart * 2);
				}
				return OperateResult.CreateSuccessResult();
			}
			buffer1.SetBytes(value, content.AddressStart * 2);
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Toyota.ToyoPuc.ReadBool(System.String,System.UInt16)" />
		[HslMqttApi("ReadBoolArray", "")]
		public override OperateResult<bool[]> ReadBool(string address, ushort length)
		{
			OperateResult<ToyoPucAddress> operateResult = ToyoPucAddress.ParseFrom(address, length, isBit: true);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(operateResult);
			}
			ToyoPucAddress content = operateResult.Content;
			if (content.PRG >= 0)
			{
				if (content.PRG == 0)
				{
					return OperateResult.CreateSuccessResult(buffer00.GetBool(content.AddressStart, length));
				}
				if (content.PRG == 1)
				{
					return OperateResult.CreateSuccessResult(buffer01.GetBool(content.AddressStart, length));
				}
				if (content.PRG == 2)
				{
					return OperateResult.CreateSuccessResult(buffer02.GetBool(content.AddressStart, length));
				}
				if (content.PRG == 3)
				{
					return OperateResult.CreateSuccessResult(buffer03.GetBool(content.AddressStart, length));
				}
				if (content.PRG == 8)
				{
					return OperateResult.CreateSuccessResult(buffer08.GetBool(content.AddressStart, length));
				}
				return new OperateResult<bool[]>(StringResources.Language.NotSupportedDataType);
			}
			return OperateResult.CreateSuccessResult(buffer1.GetBool(content.AddressStart, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Device.DeviceCommunication.Write(System.String,System.Boolean[])" />
		[HslMqttApi("WriteBoolArray", "")]
		public override OperateResult Write(string address, bool[] value)
		{
			OperateResult<ToyoPucAddress> operateResult = ToyoPucAddress.ParseFrom(address, 1, isBit: true);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			ToyoPucAddress content = operateResult.Content;
			if (content.PRG >= 0)
			{
				if (content.PRG == 0)
				{
					buffer00.SetBool(value, content.AddressStart);
				}
				else if (content.PRG == 1)
				{
					buffer01.SetBool(value, content.AddressStart);
				}
				else if (content.PRG == 2)
				{
					buffer02.SetBool(value, content.AddressStart);
				}
				else if (content.PRG == 3)
				{
					buffer03.SetBool(value, content.AddressStart);
				}
				else
				{
					if (content.PRG != 8)
					{
						return new OperateResult<byte[]>("Not supported prg write bool");
					}
					buffer08.SetBool(value, content.AddressStart);
				}
				return OperateResult.CreateSuccessResult();
			}
			buffer1.SetBool(value, content.AddressStart);
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc />
		protected override OperateResult<byte[]> ReadFromCoreServer(PipeSession session, byte[] receive)
		{
			if (receive.Length < 5)
			{
				return null;
			}
			try
			{
				byte[] array = null;
				if (receive[4] == 28)
				{
					array = ReadWordByCommand(receive);
				}
				else if (receive[4] == 32)
				{
					array = ReadBoolByCommand(receive);
				}
				else if (receive[4] == 29)
				{
					array = WriteWordByCommand(receive);
				}
				else if (receive[4] == 33)
				{
					array = WriteBoolByCommand(receive);
				}
				else if (receive[4] == 148)
				{
					array = ReadExWordByCommand(receive);
				}
				else if (receive[4] == 149)
				{
					array = WriteExWordByCommand(receive);
				}
				if (array != null)
				{
					return OperateResult.CreateSuccessResult(array);
				}
				return OperateResult.CreateSuccessResult(CreateResponseBack(receive, 35, null));
			}
			catch (Exception ex)
			{
				return new OperateResult<byte[]>(ex.Message);
			}
		}

		private byte[] CreateResponseBack(byte[] request, byte err, byte[] data)
		{
			if (err != 0)
			{
				return new byte[5] { 128, 16, 1, 0, err };
			}
			if (data == null)
			{
				data = new byte[0];
			}
			byte[] array = new byte[5 + data.Length];
			array[0] = 128;
			array[1] = 0;
			array[2] = BitConverter.GetBytes(data.Length + 1)[0];
			array[3] = BitConverter.GetBytes(data.Length + 1)[1];
			array[4] = request[4];
			if (data.Length != 0)
			{
				data.CopyTo(array, 5);
			}
			return array;
		}

		private byte[] ReadWordByCommand(byte[] command)
		{
			ushort num = BitConverter.ToUInt16(command, 5);
			ushort num2 = BitConverter.ToUInt16(command, 7);
			return CreateResponseBack(command, 0, buffer1.GetBytes(num * 2, num2 * 2));
		}

		private byte[] ReadExWordByCommand(byte[] command)
		{
			ushort num = BitConverter.ToUInt16(command, 6);
			ushort num2 = BitConverter.ToUInt16(command, 8);
			if (command[5] == 0)
			{
				return CreateResponseBack(command, 0, buffer00.GetBytes(num * 2, num2 * 2));
			}
			if (command[5] == 1)
			{
				return CreateResponseBack(command, 0, buffer01.GetBytes(num * 2, num2 * 2));
			}
			if (command[5] == 2)
			{
				return CreateResponseBack(command, 0, buffer02.GetBytes(num * 2, num2 * 2));
			}
			if (command[5] == 3)
			{
				return CreateResponseBack(command, 0, buffer03.GetBytes(num * 2, num2 * 2));
			}
			if (command[5] == 8)
			{
				if (num * 2 > 65535 || num * 2 + num2 * 2 > 65536)
				{
					return CreateResponseBack(command, 64, null);
				}
				return CreateResponseBack(command, 0, buffer08.GetBytes(num * 2, num2 * 2));
			}
			return CreateResponseBack(command, 52, null);
		}

		private byte[] ReadBoolByCommand(byte[] command)
		{
			ushort destIndex = BitConverter.ToUInt16(command, 5);
			bool @bool = buffer1.GetBool(destIndex);
			return CreateResponseBack(command, 0, new byte[1] { (byte)(@bool ? 1 : 0) });
		}

		private byte[] WriteWordByCommand(byte[] command)
		{
			if (!base.EnableWrite)
			{
				return CreateResponseBack(command, 52, null);
			}
			ushort num = BitConverter.ToUInt16(command, 5);
			buffer1.SetBytes(command.RemoveBegin(7), num * 2);
			return CreateResponseBack(command, 0, null);
		}

		private byte[] WriteExWordByCommand(byte[] command)
		{
			if (!base.EnableWrite)
			{
				return CreateResponseBack(command, 52, null);
			}
			ushort num = BitConverter.ToUInt16(command, 6);
			if (command[5] == 0)
			{
				buffer00.SetBytes(command.RemoveBegin(8), num * 2);
			}
			else if (command[5] == 1)
			{
				buffer01.SetBytes(command.RemoveBegin(8), num * 2);
			}
			else if (command[5] == 2)
			{
				buffer02.SetBytes(command.RemoveBegin(8), num * 2);
			}
			else if (command[5] == 3)
			{
				buffer03.SetBytes(command.RemoveBegin(8), num * 2);
			}
			else
			{
				if (command[5] != 8)
				{
					return CreateResponseBack(command, 52, null);
				}
				if (num * 2 > 65535)
				{
					return CreateResponseBack(command, 64, null);
				}
				buffer08.SetBytes(command.RemoveBegin(8), num * 2);
			}
			return CreateResponseBack(command, 0, null);
		}

		private byte[] WriteBoolByCommand(byte[] command)
		{
			if (!base.EnableWrite)
			{
				return CreateResponseBack(command, 52, null);
			}
			ushort destIndex = BitConverter.ToUInt16(command, 5);
			buffer1.SetBool(command[7] != 0, destIndex);
			return CreateResponseBack(command, 0, null);
		}

		/// <inheritdoc />
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				buffer1.Dispose();
				buffer00.Dispose();
				buffer01.Dispose();
				buffer02.Dispose();
				buffer03.Dispose();
				buffer08.Dispose();
			}
			base.Dispose(disposing);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"ToyoPucServer[{base.Port}]";
		}
	}
}
