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

namespace HslCommunication.Profinet.AllenBradley
{
	/// <summary>
	/// 虚拟的PCCC服务器，模拟的AB 1400通信
	/// </summary>
	public class AllenBradleyPcccServer : DeviceServer
	{
		private SoftBuffer aBuffer;

		private SoftBuffer bBuffer;

		private SoftBuffer nBuffer;

		private SoftBuffer fBuffer;

		private SoftBuffer sBuffer;

		private SoftBuffer iBuffer;

		private SoftBuffer oBuffer;

		private uint sessionID = 3305331106u;

		private const int DataPoolLength = 65536;

		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		public AllenBradleyPcccServer()
		{
			base.ByteTransform = new RegularByteTransform();
			base.WordLength = 2;
			aBuffer = new SoftBuffer(65536);
			bBuffer = new SoftBuffer(65536);
			nBuffer = new SoftBuffer(65536);
			fBuffer = new SoftBuffer(65536);
			sBuffer = new SoftBuffer(65536);
			iBuffer = new SoftBuffer(65536);
			oBuffer = new SoftBuffer(65536);
		}

		/// <inheritdoc />
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			OperateResult<AllenBradleySLCAddress> operateResult = AllenBradleySLCAddress.ParseFrom(address);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			return operateResult.Content.DataCode switch
			{
				142 => OperateResult.CreateSuccessResult(aBuffer.GetBytes(operateResult.Content.AddressStart, length)), 
				133 => OperateResult.CreateSuccessResult(bBuffer.GetBytes(operateResult.Content.AddressStart, length)), 
				137 => OperateResult.CreateSuccessResult(nBuffer.GetBytes(operateResult.Content.AddressStart, length)), 
				138 => OperateResult.CreateSuccessResult(fBuffer.GetBytes(operateResult.Content.AddressStart, length)), 
				132 => OperateResult.CreateSuccessResult(sBuffer.GetBytes(operateResult.Content.AddressStart, length)), 
				131 => OperateResult.CreateSuccessResult(iBuffer.GetBytes(operateResult.Content.AddressStart, length)), 
				130 => OperateResult.CreateSuccessResult(oBuffer.GetBytes(operateResult.Content.AddressStart, length)), 
				_ => new OperateResult<byte[]>(StringResources.Language.NotSupportedDataType), 
			};
		}

		/// <inheritdoc />
		[HslMqttApi("WriteByteArray", "")]
		public override OperateResult Write(string address, byte[] value)
		{
			OperateResult<AllenBradleySLCAddress> operateResult = AllenBradleySLCAddress.ParseFrom(address);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			switch (operateResult.Content.DataCode)
			{
			case 142:
				aBuffer.SetBytes(value, operateResult.Content.AddressStart);
				return OperateResult.CreateSuccessResult();
			case 133:
				bBuffer.SetBytes(value, operateResult.Content.AddressStart);
				return OperateResult.CreateSuccessResult();
			case 137:
				nBuffer.SetBytes(value, operateResult.Content.AddressStart);
				return OperateResult.CreateSuccessResult();
			case 138:
				fBuffer.SetBytes(value, operateResult.Content.AddressStart);
				return OperateResult.CreateSuccessResult();
			case 132:
				sBuffer.SetBytes(value, operateResult.Content.AddressStart);
				return OperateResult.CreateSuccessResult();
			case 131:
				iBuffer.SetBytes(value, operateResult.Content.AddressStart);
				return OperateResult.CreateSuccessResult();
			case 130:
				oBuffer.SetBytes(value, operateResult.Content.AddressStart);
				return OperateResult.CreateSuccessResult();
			default:
				return new OperateResult(StringResources.Language.NotSupportedDataType);
			}
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new AllenBradleyMessage();
		}

		/// <inheritdoc />
		protected override OperateResult ThreadPoolLoginAfterClientCheck(PipeSession session, IPEndPoint endPoint)
		{
			CommunicationPipe communication = session.Communication;
			AllenBradleyMessage netMessage = new AllenBradleyMessage();
			OperateResult<byte[]> operateResult = communication.ReceiveMessage(netMessage, null, useActivePush: false, null, delegate(byte[] m)
			{
				LogRevcMessage(m, session);
			});
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			string text = operateResult.Content.SelectMiddle(12, 8).ToHexString();
			byte[] array = AllenBradleyHelper.PackRequestHeader(101, sessionID, new byte[4] { 1, 0, 0, 0 });
			LogSendMessage(array, session);
			OperateResult operateResult2 = communication.Send(array);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			OperateResult<byte[]> operateResult3 = communication.ReceiveMessage(netMessage, null, useActivePush: false, null, delegate(byte[] m)
			{
				LogRevcMessage(m, session);
			});
			if (!operateResult3.IsSuccess)
			{
				return operateResult3;
			}
			byte[] array2 = AllenBradleyHelper.PackRequestHeader(111, sessionID, "00 00 00 00 00 04 02 00 00 00 00 00 b2 00 1e 00 d4 00 00 00 cc 31 59 a2 e8 a3 14 00 27 04 09 10 0b 46 a5 c1 01 40 20 00 01 40 20 00 00 00".ToHexBytes());
			LogSendMessage(array2, session);
			OperateResult operateResult4 = communication.Send(array2);
			if (!operateResult4.IsSuccess)
			{
				return operateResult4;
			}
			return base.ThreadPoolLoginAfterClientCheck(session, endPoint);
		}

		/// <inheritdoc />
		protected override OperateResult<byte[]> ReadFromCoreServer(PipeSession session, byte[] receive)
		{
			byte[] array = null;
			array = ((receive[0] == 111) ? AllenBradleyHelper.PackRequestHeader(111, sessionID, AllenBradleyHelper.PackCommandSpecificData(AllenBradleyHelper.PackCommandSingleService(null, 0, isConnected: false, 0), AllenBradleyHelper.PackCommandSingleService("ce 00 00 00 27 04 09 10 0b 46 a5 c1 00 00".ToHexBytes(), 178, isConnected: false, 0))) : ((receive[0] != 102) ? ReadWriteCommand(receive.RemoveBegin(59)) : AllenBradleyHelper.PackRequestHeader(111, sessionID, null)));
			return OperateResult.CreateSuccessResult(array);
		}

		private byte[] GetResponse(int status, byte[] data)
		{
			byte[] array = AllenBradleyHelper.PackRequestHeader(112, sessionID, AllenBradleyHelper.PackCommandSpecificData(AllenBradleyHelper.PackCommandSingleService("e8 a3 14 00".ToHexBytes(), 161, isConnected: false, 0), AllenBradleyHelper.PackCommandSingleService(SoftBasic.SpliceArray<byte>("09 00 cb 00 00 00 07 09 10 0b 46 a5 c1 4f 00 08 00".ToHexBytes(), data), 177, isConnected: false, 0)));
			base.ByteTransform.TransByte(status).CopyTo(array, 8);
			return array;
		}

		private int GetDynamicLengthData(byte[] fccc, ref int offset)
		{
			int num = fccc[offset++];
			if (num == 255)
			{
				num = BitConverter.ToUInt16(fccc, offset);
				offset += 2;
			}
			return num;
		}

		private byte[] ReadWriteCommand(byte[] fccc)
		{
			int length = fccc[5];
			int offset = 6;
			int dynamicLengthData = GetDynamicLengthData(fccc, ref offset);
			byte b = fccc[offset++];
			int dynamicLengthData2 = GetDynamicLengthData(fccc, ref offset);
			int dynamicLengthData3 = GetDynamicLengthData(fccc, ref offset);
			if (fccc[4] == 162)
			{
				return b switch
				{
					142 => GetResponse(0, aBuffer.GetBytes(dynamicLengthData2, length)), 
					133 => GetResponse(0, bBuffer.GetBytes(dynamicLengthData2, length)), 
					137 => GetResponse(0, nBuffer.GetBytes(dynamicLengthData2, length)), 
					138 => GetResponse(0, fBuffer.GetBytes(dynamicLengthData2, length)), 
					132 => GetResponse(0, sBuffer.GetBytes(dynamicLengthData2, length)), 
					131 => GetResponse(0, iBuffer.GetBytes(dynamicLengthData2, length)), 
					130 => GetResponse(0, oBuffer.GetBytes(dynamicLengthData2, length)), 
					_ => GetResponse(1, null), 
				};
			}
			if (fccc[4] == 170)
			{
				byte[] data = fccc.RemoveBegin(offset);
				switch (b)
				{
				case 142:
					aBuffer.SetBytes(data, dynamicLengthData2);
					return GetResponse(0, null);
				case 133:
					bBuffer.SetBytes(data, dynamicLengthData2);
					return GetResponse(0, null);
				case 137:
					nBuffer.SetBytes(data, dynamicLengthData2);
					return GetResponse(0, null);
				case 138:
					fBuffer.SetBytes(data, dynamicLengthData2);
					return GetResponse(0, null);
				case 132:
					sBuffer.SetBytes(data, dynamicLengthData2);
					return GetResponse(0, null);
				case 131:
					iBuffer.SetBytes(data, dynamicLengthData2);
					return GetResponse(0, null);
				case 130:
					oBuffer.SetBytes(data, dynamicLengthData2);
					return GetResponse(0, null);
				default:
					return GetResponse(1, null);
				}
			}
			if (fccc[4] == 171)
			{
				SoftBuffer softBuffer = null;
				switch (b)
				{
				case 142:
					softBuffer = aBuffer;
					break;
				case 133:
					softBuffer = bBuffer;
					break;
				case 137:
					softBuffer = nBuffer;
					break;
				case 138:
					softBuffer = fBuffer;
					break;
				case 132:
					softBuffer = sBuffer;
					break;
				case 131:
					softBuffer = iBuffer;
					break;
				case 130:
					softBuffer = oBuffer;
					break;
				default:
					return GetResponse(1, null);
				}
				int num = BitConverter.ToUInt16(fccc, offset);
				int num2 = BitConverter.ToUInt16(fccc, offset + 2);
				int uInt = softBuffer.GetUInt16(dynamicLengthData2);
				ushort value = (ushort)((uInt & ~num) | num2);
				softBuffer.SetValue(value, dynamicLengthData2);
				return GetResponse(0, null);
			}
			return GetResponse(1, null);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"AllenBradleyPcccServer[{base.Port}]";
		}
	}
}
