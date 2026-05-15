using System;
using System.Net;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Address;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Core.Net;
using HslCommunication.Core.Pipe;
using HslCommunication.Reflection;

namespace HslCommunication.Profinet.GE
{
	/// <summary>
	/// Ge的SRTP协议实现的虚拟PLC，支持I,Q,M,T,SA,SB,SC,S,G的位和字节读写，支持AI,AQ,R的字读写操作，支持读取当前时间及程序名称。<br />
	/// Virtual PLC implemented by Ge's SRTP protocol, supports bit and byte read and write of I, Q, M, T, SA, SB, SC, S, G, 
	/// supports word read and write operations of AI, AQ, R, and supports reading Current time and program name.
	/// </summary>
	/// <remarks>
	/// 实例化之后，直接调用 <see cref="M:HslCommunication.Core.Net.NetworkServerBase.ServerStart(System.Int32)" /> 方法就可以通信及交互，所有的地址都是从1开始的，地址示例：M1,M100, R1，
	/// 具体的用法参考 HslCommunicationDemo 相关界面的源代码。
	/// </remarks>
	/// <example>
	/// 地址的示例，参考 <see cref="T:HslCommunication.Profinet.GE.GeSRTPNet" /> 相关的示例说明
	/// </example>
	public class GeSRTPServer : DeviceServer
	{
		private SoftBuffer iBuffer;

		private SoftBuffer qBuffer;

		private SoftBuffer mBuffer;

		private SoftBuffer tBuffer;

		private SoftBuffer saBuffer;

		private SoftBuffer sbBuffer;

		private SoftBuffer scBuffer;

		private SoftBuffer sBuffer;

		private SoftBuffer gBuffer;

		private SoftBuffer aiBuffer;

		private SoftBuffer aqBuffer;

		private SoftBuffer rBuffer;

		private const int DataPoolLength = 65536;

		/// <summary>
		/// 实例化一个默认的对象<br />
		/// Instantiate a default object
		/// </summary>
		public GeSRTPServer()
		{
			iBuffer = new SoftBuffer(65536);
			qBuffer = new SoftBuffer(65536);
			mBuffer = new SoftBuffer(65536);
			tBuffer = new SoftBuffer(65536);
			saBuffer = new SoftBuffer(65536);
			sbBuffer = new SoftBuffer(65536);
			scBuffer = new SoftBuffer(65536);
			sBuffer = new SoftBuffer(65536);
			gBuffer = new SoftBuffer(65536);
			aiBuffer = new SoftBuffer(131072);
			aqBuffer = new SoftBuffer(131072);
			rBuffer = new SoftBuffer(131072);
			base.WordLength = 2;
			base.ByteTransform = new RegularByteTransform();
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.GE.GeSRTPNet.Read(System.String,System.UInt16)" />
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			OperateResult<GeSRTPAddress> operateResult = GeSRTPAddress.ParseFrom(address, length, isBit: false);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			return OperateResult.CreateSuccessResult(ReadByCommand(operateResult.Content.DataCode, (ushort)operateResult.Content.AddressStart, operateResult.Content.Length));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.GE.GeSRTPNet.Write(System.String,System.Byte[])" />
		[HslMqttApi("WriteByteArray", "")]
		public override OperateResult Write(string address, byte[] value)
		{
			OperateResult<GeSRTPAddress> operateResult = GeSRTPAddress.ParseFrom(address, (ushort)value.Length, isBit: false);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			return WriteByCommand(operateResult.Content.DataCode, (ushort)operateResult.Content.AddressStart, operateResult.Content.Length, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.GE.GeSRTPNet.ReadBool(System.String,System.UInt16)" />
		[HslMqttApi("ReadBoolArray", "")]
		public override OperateResult<bool[]> ReadBool(string address, ushort length)
		{
			OperateResult<GeSRTPAddress> operateResult = GeSRTPAddress.ParseFrom(address, length, isBit: true);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(operateResult);
			}
			bool isBit;
			return OperateResult.CreateSuccessResult(GetSoftBufferFromDataCode(operateResult.Content.DataCode, out isBit).GetBool(operateResult.Content.AddressStart, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.GE.GeSRTPNet.Write(System.String,System.Boolean[])" />
		[HslMqttApi("WriteBoolArray", "")]
		public override OperateResult Write(string address, bool[] value)
		{
			OperateResult<GeSRTPAddress> operateResult = GeSRTPAddress.ParseFrom(address, (ushort)value.Length, isBit: true);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(operateResult);
			}
			GetSoftBufferFromDataCode(operateResult.Content.DataCode, out var _).SetBool(value, operateResult.Content.AddressStart);
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new GeSRTPMessage();
		}

		/// <inheritdoc />
		protected override OperateResult ThreadPoolLoginAfterClientCheck(PipeSession session, IPEndPoint endPoint)
		{
			CommunicationPipe communication = session.Communication;
			OperateResult<byte[]> operateResult = communication.ReceiveMessage(new GeSRTPMessage(), null, useActivePush: false, null, delegate(byte[] m)
			{
				LogRevcMessage(m, session);
			});
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			byte[] array = new byte[56];
			array[0] = 1;
			array[8] = 15;
			LogSendMessage(array, session);
			OperateResult operateResult2 = communication.Send(array);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			return base.ThreadPoolLoginAfterClientCheck(session, endPoint);
		}

		/// <inheritdoc />
		protected override OperateResult<byte[]> ReadFromCoreServer(PipeSession session, byte[] receive)
		{
			byte[] array = null;
			array = ((receive[42] == 4) ? ReadByCommand(receive) : ((receive[42] == 1) ? ReadProgramNameByCommand(receive) : ((receive[42] == 37) ? ReadDateTimeByCommand(receive) : ((receive[50] != 7) ? null : WriteByCommand(receive)))));
			return OperateResult.CreateSuccessResult(array);
		}

		private SoftBuffer GetSoftBufferFromDataCode(byte code, out bool isBit)
		{
			switch (code)
			{
			case 70:
				isBit = true;
				return iBuffer;
			case 16:
				isBit = false;
				return iBuffer;
			case 72:
				isBit = true;
				return qBuffer;
			case 66:
				isBit = false;
				return qBuffer;
			case 76:
				isBit = true;
				return mBuffer;
			case 22:
				isBit = false;
				return mBuffer;
			case 74:
				isBit = true;
				return tBuffer;
			case 20:
				isBit = false;
				return tBuffer;
			case 78:
				isBit = true;
				return saBuffer;
			case 24:
				isBit = false;
				return saBuffer;
			case 80:
				isBit = true;
				return sbBuffer;
			case 26:
				isBit = false;
				return sbBuffer;
			case 82:
				isBit = true;
				return scBuffer;
			case 28:
				isBit = false;
				return scBuffer;
			case 84:
				isBit = true;
				return tBuffer;
			case 30:
				isBit = false;
				return tBuffer;
			case 86:
				isBit = true;
				return gBuffer;
			case 56:
				isBit = false;
				return gBuffer;
			case 10:
				isBit = false;
				return aiBuffer;
			case 12:
				isBit = false;
				return aqBuffer;
			case 8:
				isBit = false;
				return rBuffer;
			default:
				isBit = false;
				return null;
			}
		}

		private byte[] ReadByCommand(byte dataCode, ushort address, ushort length)
		{
			bool isBit;
			SoftBuffer softBufferFromDataCode = GetSoftBufferFromDataCode(dataCode, out isBit);
			if (softBufferFromDataCode == null)
			{
				return null;
			}
			if (isBit)
			{
				HslHelper.CalculateStartBitIndexAndLength(address, length, out var newStart, out var byteLength, out var _);
				return softBufferFromDataCode.GetBytes(newStart / 8, byteLength);
			}
			if (dataCode == 10 || dataCode == 12 || dataCode == 8)
			{
				return softBufferFromDataCode.GetBytes(address * 2, length * 2);
			}
			return softBufferFromDataCode.GetBytes(address, length);
		}

		private byte[] ReadByCommand(byte[] command)
		{
			byte[] array = ReadByCommand(command[43], BitConverter.ToUInt16(command, 44), BitConverter.ToUInt16(command, 46));
			if (array == null)
			{
				return null;
			}
			if (array.Length < 7)
			{
				byte[] array2 = "\r\n03 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00\r\n00 01 00 00 00 00 00 00 00 00 00 00 00 00 06 d4\r\n00 0e 00 00 00 60 01 a0 01 01 00 00 00 00 00 00\r\n00 00 ff 02 03 00 5c 01".ToHexBytes();
				array.CopyTo(array2, 44);
				command.SelectMiddle(2, 2).CopyTo(array2, 2);
				return array2;
			}
			byte[] array3 = new byte[56 + array.Length];
			"03 00 03 00 00 00 00 00 00 00 00 00 00 00 00 00 00 01 00 00 00 00 00 00 00 00 00 00 00 00 06 94\r\n\t\t\t\t00 0e 00 00 00 60 01 a0 00 00 0c 00 00 18 00 00 01 01 ff 02 03 00 5c 01".ToHexBytes().CopyTo(array3, 0);
			command.SelectMiddle(2, 2).CopyTo(array3, 2);
			array.CopyTo(array3, 56);
			BitConverter.GetBytes((ushort)array.Length).CopyTo(array3, 4);
			return array3;
		}

		private OperateResult WriteByCommand(byte dataCode, ushort address, ushort length, byte[] value)
		{
			bool isBit;
			SoftBuffer softBufferFromDataCode = GetSoftBufferFromDataCode(dataCode, out isBit);
			if (softBufferFromDataCode == null)
			{
				return new OperateResult(StringResources.Language.NotSupportedDataType);
			}
			if (isBit)
			{
				HslHelper.CalculateStartBitIndexAndLength(address, length, out var _, out var _, out var _);
				softBufferFromDataCode.SetBool(value.ToBoolArray().SelectMiddle((int)address % 8, length), address);
			}
			else if (dataCode == 10 || dataCode == 12 || dataCode == 8)
			{
				if (value.Length % 2 == 1)
				{
					return new OperateResult(StringResources.Language.GeSRTPWriteLengthMustBeEven);
				}
				softBufferFromDataCode.SetBytes(value, address * 2);
			}
			else
			{
				softBufferFromDataCode.SetBytes(value, address);
			}
			return OperateResult.CreateSuccessResult();
		}

		private byte[] WriteByCommand(byte[] command)
		{
			if (!base.EnableWrite)
			{
				return null;
			}
			OperateResult operateResult = WriteByCommand(command[51], BitConverter.ToUInt16(command, 52), BitConverter.ToUInt16(command, 54), command.RemoveBegin(56));
			if (!operateResult.IsSuccess)
			{
				return null;
			}
			byte[] array = "03 00 01 00 00 00 00 00 00 00 00 00 00 00 00 00\r\n00 02 00 00 00 00 00 00 00 00 00 00 00 00 09 d4\r\n00 0e 00 00 00 60 01 a0 01 01 00 00 00 00 00 00\r\n00 00 ff 02 03 00 5c 01".ToHexBytes();
			command.SelectMiddle(2, 2).CopyTo(array, 2);
			return array;
		}

		private byte[] ReadDateTimeByCommand(byte[] command)
		{
			byte[] array = "03 00 03 00 07 00 00 00 00 00 00 00 00 00 00 00 00 01 00 00 00 00 00 00 00 00 00 00 00 00 06 94\r\n\t\t\t\t00 0e 00 00 00 60 01 a0 00 00 0c 00 00 18 00 00 01 01 ff 02 03 00 5c 01 00 00 00 00 00 00 03".ToHexBytes();
			DateTime now = DateTime.Now;
			now.Second.ToString("D2").ToHexBytes().CopyTo(array, 56);
			now.Minute.ToString("D2").ToHexBytes().CopyTo(array, 57);
			now.Hour.ToString("D2").ToHexBytes().CopyTo(array, 58);
			now.Day.ToString("D2").ToHexBytes().CopyTo(array, 59);
			now.Month.ToString("D2").ToHexBytes().CopyTo(array, 60);
			(now.Year - 2000).ToString("D2").ToHexBytes().CopyTo(array, 61);
			command.SelectMiddle(2, 2).CopyTo(array, 2);
			return array;
		}

		private byte[] ReadProgramNameByCommand(byte[] command)
		{
			byte[] array = "\r\n03 00 07 00 2a 00 00 00 00 00 00 00 00 00 00 00 \r\n00 01 00 00 00 00 00 00 00 00 00 00 00 00 06 94 \r\n00 0e 00 00 00 62 01 a0 00 00 2a 00 00 18 00 00 \r\n01 01 ff 02 03 00 5c 01 00 00 00 00 00 00 00 00 \r\n01 00 00 00 00 00 00 00 00 00 50 41 43 34 30 30 \r\n00 00 00 00 00 00 00 00 00 00 03 00 01 50 05 18 \r\n01 21".ToHexBytes();
			command.SelectMiddle(2, 2).CopyTo(array, 2);
			return array;
		}

		/// <inheritdoc />
		protected override void LoadFromBytes(byte[] content)
		{
			if (content.Length < 983040)
			{
				throw new Exception("File is not correct");
			}
			iBuffer.SetBytes(content, 0, 0, 65536);
			qBuffer.SetBytes(content, 65536, 0, 65536);
			mBuffer.SetBytes(content, 131072, 0, 65536);
			tBuffer.SetBytes(content, 196608, 0, 65536);
			saBuffer.SetBytes(content, 262144, 0, 65536);
			sbBuffer.SetBytes(content, 327680, 0, 65536);
			scBuffer.SetBytes(content, 393216, 0, 65536);
			sBuffer.SetBytes(content, 458752, 0, 65536);
			gBuffer.SetBytes(content, 524288, 0, 65536);
			aiBuffer.SetBytes(content, 589824, 0, 131072);
			aqBuffer.SetBytes(content, 720896, 0, 131072);
			rBuffer.SetBytes(content, 851968, 0, 131072);
		}

		/// <inheritdoc />
		protected override byte[] SaveToBytes()
		{
			byte[] array = new byte[983040];
			Array.Copy(iBuffer.GetBytes(), 0, array, 0, 65536);
			Array.Copy(qBuffer.GetBytes(), 0, array, 65536, 65536);
			Array.Copy(mBuffer.GetBytes(), 0, array, 131072, 65536);
			Array.Copy(tBuffer.GetBytes(), 0, array, 196608, 65536);
			Array.Copy(saBuffer.GetBytes(), 0, array, 262144, 65536);
			Array.Copy(sbBuffer.GetBytes(), 0, array, 327680, 65536);
			Array.Copy(scBuffer.GetBytes(), 0, array, 393216, 65536);
			Array.Copy(sBuffer.GetBytes(), 0, array, 458752, 65536);
			Array.Copy(gBuffer.GetBytes(), 0, array, 524288, 65536);
			Array.Copy(aiBuffer.GetBytes(), 0, array, 589824, 131072);
			Array.Copy(aqBuffer.GetBytes(), 0, array, 720896, 131072);
			Array.Copy(rBuffer.GetBytes(), 0, array, 851968, 131072);
			return array;
		}

		/// <inheritdoc />
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				iBuffer.Dispose();
				qBuffer.Dispose();
				mBuffer.Dispose();
				tBuffer.Dispose();
				saBuffer.Dispose();
				sbBuffer.Dispose();
				scBuffer.Dispose();
				sBuffer.Dispose();
				gBuffer.Dispose();
				aiBuffer.Dispose();
				aqBuffer.Dispose();
				rBuffer.Dispose();
			}
			base.Dispose(disposing);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"GeSRTPServer[{base.Port}]";
		}
	}
}
