using System;
using System.Linq;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Core.Net;
using HslCommunication.Profinet.Vigor.Helper;
using HslCommunication.Reflection;

namespace HslCommunication.Profinet.Vigor
{
	/// <summary>
	/// 丰炜的虚拟PLC，模拟了VS系列的通信，可以和对应的客户端进行数据读写测试，位地址支持 X,Y,M,S，字地址支持 D,R,SD
	/// </summary>
	public class VigorServer : DeviceServer
	{
		private SoftBuffer xBuffer;

		private SoftBuffer yBuffer;

		private SoftBuffer mBuffer;

		private SoftBuffer sBuffer;

		private SoftBuffer dBuffer;

		private SoftBuffer rBuffer;

		private SoftBuffer sdBuffer;

		private const int DataPoolLength = 65536;

		private int station = 0;

		/// <inheritdoc cref="P:HslCommunication.Profinet.Vigor.VigorSerial.Station" />
		public int Station
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
		/// 实例化一个丰炜PLC的网口和串口服务器，支持数据读写操作
		/// </summary>
		public VigorServer()
		{
			xBuffer = new SoftBuffer(65536);
			yBuffer = new SoftBuffer(65536);
			mBuffer = new SoftBuffer(65536);
			sBuffer = new SoftBuffer(65536);
			dBuffer = new SoftBuffer(131072);
			rBuffer = new SoftBuffer(131072);
			sdBuffer = new SoftBuffer(131072);
			base.ByteTransform = new RegularByteTransform();
			base.WordLength = 1;
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new VigorSerialMessage();
		}

		/// <inheritdoc />
		protected override byte[] SaveToBytes()
		{
			byte[] array = new byte[655360];
			xBuffer.GetBytes().CopyTo(array, 0);
			yBuffer.GetBytes().CopyTo(array, 65536);
			mBuffer.GetBytes().CopyTo(array, 131072);
			sBuffer.GetBytes().CopyTo(array, 196608);
			dBuffer.GetBytes().CopyTo(array, 262144);
			rBuffer.GetBytes().CopyTo(array, 393216);
			sdBuffer.GetBytes().CopyTo(array, 524288);
			return array;
		}

		/// <inheritdoc />
		protected override void LoadFromBytes(byte[] content)
		{
			if (content.Length < 655360)
			{
				throw new Exception("File is not correct");
			}
			xBuffer.SetBytes(content, 0, 65536);
			yBuffer.SetBytes(content, 65536, 65536);
			mBuffer.SetBytes(content, 131072, 65536);
			sBuffer.SetBytes(content, 196608, 65536);
			dBuffer.SetBytes(content, 262144, 131072);
			rBuffer.SetBytes(content, 393216, 131072);
			sdBuffer.SetBytes(content, 524288, 131072);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Vigor.VigorSerial.Read(System.String,System.UInt16)" />
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			OperateResult<byte[]> operateResult = new OperateResult<byte[]>();
			try
			{
				if (address.StartsWith("SD") || address.StartsWith("sd"))
				{
					return OperateResult.CreateSuccessResult(sdBuffer.GetBytes(Convert.ToInt32(address.Substring(2)) * 2, length * 2));
				}
				if (address.StartsWith("D") || address.StartsWith("d"))
				{
					return OperateResult.CreateSuccessResult(dBuffer.GetBytes(Convert.ToInt32(address.Substring(1)) * 2, length * 2));
				}
				if (address.StartsWith("R") || address.StartsWith("r"))
				{
					return OperateResult.CreateSuccessResult(rBuffer.GetBytes(Convert.ToInt32(address.Substring(1)) * 2, length * 2));
				}
				throw new Exception(StringResources.Language.NotSupportedDataType);
			}
			catch (Exception ex)
			{
				operateResult.Message = ex.Message;
				return operateResult;
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Vigor.VigorSerial.Write(System.String,System.Byte[])" />
		[HslMqttApi("WriteByteArray", "")]
		public override OperateResult Write(string address, byte[] value)
		{
			OperateResult<byte[]> operateResult = new OperateResult<byte[]>();
			try
			{
				if (address.StartsWith("SD") || address.StartsWith("sd"))
				{
					sdBuffer.SetBytes(value, Convert.ToInt32(address.Substring(2)) * 2);
					return OperateResult.CreateSuccessResult();
				}
				if (address.StartsWith("D") || address.StartsWith("d"))
				{
					dBuffer.SetBytes(value, Convert.ToInt32(address.Substring(1)) * 2);
					return OperateResult.CreateSuccessResult();
				}
				if (address.StartsWith("R") || address.StartsWith("r"))
				{
					rBuffer.SetBytes(value, Convert.ToInt32(address.Substring(1)) * 2);
					return OperateResult.CreateSuccessResult();
				}
				throw new Exception(StringResources.Language.NotSupportedDataType);
			}
			catch (Exception ex)
			{
				operateResult.Message = ex.Message;
				return operateResult;
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Vigor.VigorSerial.ReadBool(System.String,System.UInt16)" />
		[HslMqttApi("ReadBoolArray", "")]
		public override OperateResult<bool[]> ReadBool(string address, ushort length)
		{
			try
			{
				int destIndex = Convert.ToInt32(address.Substring(1));
				switch (address[0])
				{
				case 'X':
				case 'x':
					return OperateResult.CreateSuccessResult(xBuffer.GetBool(destIndex, length));
				case 'Y':
				case 'y':
					return OperateResult.CreateSuccessResult(yBuffer.GetBool(destIndex, length));
				case 'M':
				case 'm':
					return OperateResult.CreateSuccessResult(mBuffer.GetBool(destIndex, length));
				case 'S':
				case 's':
					return OperateResult.CreateSuccessResult(sBuffer.GetBool(destIndex, length));
				default:
					throw new Exception(StringResources.Language.NotSupportedDataType);
				}
			}
			catch (Exception ex)
			{
				return new OperateResult<bool[]>(ex.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Device.DeviceCommunication.Write(System.String,System.Boolean[])" />
		[HslMqttApi("WriteBoolArray", "")]
		public override OperateResult Write(string address, bool[] value)
		{
			try
			{
				int destIndex = Convert.ToInt32(address.Substring(1));
				switch (address[0])
				{
				case 'X':
				case 'x':
					xBuffer.SetBool(value, destIndex);
					return OperateResult.CreateSuccessResult();
				case 'Y':
				case 'y':
					yBuffer.SetBool(value, destIndex);
					return OperateResult.CreateSuccessResult();
				case 'M':
				case 'm':
					mBuffer.SetBool(value, destIndex);
					return OperateResult.CreateSuccessResult();
				case 'S':
				case 's':
					sBuffer.SetBool(value, destIndex);
					return OperateResult.CreateSuccessResult();
				default:
					throw new Exception(StringResources.Language.NotSupportedDataType);
				}
			}
			catch (Exception ex)
			{
				return new OperateResult<bool[]>(ex.Message);
			}
		}

		/// <inheritdoc />
		protected override OperateResult<byte[]> ReadFromCoreServer(PipeSession session, byte[] receive)
		{
			if (receive[0] != 16 || receive[1] != 2)
			{
				return new OperateResult<byte[]>("start message must be 0x10, 0x02");
			}
			if (receive[2] != station)
			{
				return new OperateResult<byte[]>($"Station not match , Except: {station:X2} , Actual: {receive[2]}");
			}
			return OperateResult.CreateSuccessResult(ReadFromVigorCore(VigorVsHelper.UnPackCommand(receive)));
		}

		private byte[] CreateResponseBack(byte[] request, byte err, byte[] data)
		{
			if (data == null)
			{
				data = new byte[0];
			}
			byte[] array = new byte[4 + data.Length];
			array[0] = request[2];
			array[1] = BitConverter.GetBytes(1 + data.Length)[0];
			array[2] = BitConverter.GetBytes(1 + data.Length)[1];
			array[3] = err;
			if (data.Length != 0)
			{
				data.CopyTo(array, 4);
			}
			return VigorVsHelper.PackCommand(array, 6);
		}

		private byte[] ReadFromVigorCore(byte[] receive)
		{
			if (receive.Length < 16)
			{
				return null;
			}
			if (receive[5] == 32)
			{
				return ReadWordByCommand(receive);
			}
			if (receive[5] == 33)
			{
				return ReadBoolByCommand(receive);
			}
			if (receive[5] == 40)
			{
				return WriteWordByCommand(receive);
			}
			if (receive[5] == 41)
			{
				return WriteBoolByCommand(receive);
			}
			return CreateResponseBack(receive, 49, null);
		}

		private byte[] ReadWordByCommand(byte[] command)
		{
			int num = Convert.ToInt32(command.SelectMiddle(7, 3).Reverse().ToArray()
				.ToHexString());
			int num2 = base.ByteTransform.TransUInt16(command, 10);
			return command[6] switch
			{
				160 => CreateResponseBack(command, 0, dBuffer.GetBytes(num * 2, num2 * 2)), 
				161 => CreateResponseBack(command, 0, sdBuffer.GetBytes(num * 2, num2 * 2)), 
				162 => CreateResponseBack(command, 0, rBuffer.GetBytes(num * 2, num2 * 2)), 
				_ => CreateResponseBack(command, 49, null), 
			};
		}

		private byte[] ReadBoolByCommand(byte[] command)
		{
			string value = command.SelectMiddle(7, 3).Reverse().ToArray()
				.ToHexString();
			int length = base.ByteTransform.TransUInt16(command, 10);
			return command[6] switch
			{
				144 => CreateResponseBack(command, 0, xBuffer.GetBool(Convert.ToInt32(value, 8), length).ToByteArray()), 
				145 => CreateResponseBack(command, 0, yBuffer.GetBool(Convert.ToInt32(value, 8), length).ToByteArray()), 
				146 => CreateResponseBack(command, 0, mBuffer.GetBool(Convert.ToInt32(value), length).ToByteArray()), 
				147 => CreateResponseBack(command, 0, sBuffer.GetBool(Convert.ToInt32(value), length).ToByteArray()), 
				_ => CreateResponseBack(command, 49, null), 
			};
		}

		private byte[] WriteWordByCommand(byte[] command)
		{
			if (!base.EnableWrite)
			{
				return CreateResponseBack(command, 49, null);
			}
			int num = Convert.ToInt32(command.SelectMiddle(7, 3).Reverse().ToArray()
				.ToHexString());
			int length = base.ByteTransform.TransUInt16(command, 3) - 7;
			byte[] data = command.SelectMiddle(12, length);
			switch (command[6])
			{
			case 160:
				dBuffer.SetBytes(data, num * 2);
				return CreateResponseBack(command, 0, null);
			case 161:
				sdBuffer.SetBytes(data, num * 2);
				return CreateResponseBack(command, 0, null);
			case 162:
				rBuffer.SetBytes(data, num * 2);
				return CreateResponseBack(command, 0, null);
			default:
				return CreateResponseBack(command, 49, null);
			}
		}

		private byte[] WriteBoolByCommand(byte[] command)
		{
			if (!base.EnableWrite)
			{
				return CreateResponseBack(command, 49, null);
			}
			string value = command.SelectMiddle(7, 3).Reverse().ToArray()
				.ToHexString();
			int length = base.ByteTransform.TransUInt16(command, 3) - 7;
			int length2 = base.ByteTransform.TransUInt16(command, 10);
			bool[] value2 = command.SelectMiddle(12, length).ToBoolArray().SelectBegin(length2);
			switch (command[6])
			{
			case 144:
				xBuffer.SetBool(value2, Convert.ToInt32(value, 8));
				return CreateResponseBack(command, 0, null);
			case 145:
				yBuffer.SetBool(value2, Convert.ToInt32(value, 8));
				return CreateResponseBack(command, 0, null);
			case 146:
				mBuffer.SetBool(value2, Convert.ToInt32(value));
				return CreateResponseBack(command, 0, null);
			case 147:
				sBuffer.SetBool(value2, Convert.ToInt32(value));
				return CreateResponseBack(command, 0, null);
			default:
				return CreateResponseBack(command, 49, null);
			}
		}

		/// <inheritdoc />
		protected override bool CheckSerialReceiveDataComplete(byte[] buffer, int receivedLength)
		{
			return VigorVsHelper.CheckReceiveDataComplete(buffer, receivedLength);
		}

		/// <inheritdoc />
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				xBuffer.Dispose();
				yBuffer.Dispose();
				mBuffer.Dispose();
				sBuffer.Dispose();
				dBuffer.Dispose();
				rBuffer.Dispose();
				sdBuffer.Dispose();
			}
			base.Dispose(disposing);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"VigorServer[{base.Port}]";
		}
	}
}
