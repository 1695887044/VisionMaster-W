using System;
using System.IO;
using System.Linq;
using System.Net;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Address;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Core.Net;
using HslCommunication.Core.Pipe;
using HslCommunication.Reflection;

namespace HslCommunication.Profinet.Omron
{
	/// <summary>
	/// 欧姆龙的虚拟服务器，支持DM区，CIO区，Work区，Hold区，Auxiliary区，可以方便的进行测试<br />
	/// Omron's virtual server supports DM area, CIO area, Work area, Hold area, and Auxiliary area, which can be easily tested
	/// </summary>
	public class OmronFinsServer : DeviceServer
	{
		protected SoftBuffer dBuffer;

		protected SoftBuffer cioBuffer;

		protected SoftBuffer wBuffer;

		protected SoftBuffer hBuffer;

		protected SoftBuffer arBuffer;

		protected SoftBuffer emBuffer;

		protected SoftBuffer cfBuffer;

		protected SoftBuffer irBuffer;

		protected SoftBuffer drBuffer;

		protected bool connectionInitialization = true;

		private const int DataPoolLength = 65536;

		/// <inheritdoc cref="P:HslCommunication.Core.IByteTransform.DataFormat" />
		public DataFormat DataFormat
		{
			get
			{
				return base.ByteTransform.DataFormat;
			}
			set
			{
				base.ByteTransform.DataFormat = value;
			}
		}

		/// <summary>
		/// 实例化一个Fins协议的服务器<br />
		/// Instantiate a Fins protocol server
		/// </summary>
		public OmronFinsServer()
		{
			dBuffer = new SoftBuffer(131072);
			cioBuffer = new SoftBuffer(131072);
			wBuffer = new SoftBuffer(131072);
			hBuffer = new SoftBuffer(131072);
			arBuffer = new SoftBuffer(131072);
			emBuffer = new SoftBuffer(131072);
			cfBuffer = new SoftBuffer(131072);
			irBuffer = new SoftBuffer(131072);
			drBuffer = new SoftBuffer(131072);
			dBuffer.IsBoolReverseByWord = true;
			cioBuffer.IsBoolReverseByWord = true;
			wBuffer.IsBoolReverseByWord = true;
			hBuffer.IsBoolReverseByWord = true;
			arBuffer.IsBoolReverseByWord = true;
			emBuffer.IsBoolReverseByWord = true;
			cfBuffer.IsBoolReverseByWord = true;
			irBuffer.IsBoolReverseByWord = true;
			drBuffer.IsBoolReverseByWord = true;
			base.WordLength = 1;
			base.ByteTransform = new RegularByteTransform(DataFormat.CDAB);
		}

		private OperateResult<SoftBuffer, OmronFinsAddress> GetWordAddressBuffer(string address)
		{
			OperateResult<OmronFinsAddress> operateResult = OmronFinsAddress.ParseFrom(address);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<SoftBuffer, OmronFinsAddress>(operateResult);
			}
			if (operateResult.Content.WordCode == OmronFinsDataType.DM.WordCode)
			{
				return OperateResult.CreateSuccessResult(dBuffer, operateResult.Content);
			}
			if (operateResult.Content.WordCode == OmronFinsDataType.CIO.WordCode)
			{
				return OperateResult.CreateSuccessResult(cioBuffer, operateResult.Content);
			}
			if (operateResult.Content.WordCode == OmronFinsDataType.WR.WordCode)
			{
				return OperateResult.CreateSuccessResult(wBuffer, operateResult.Content);
			}
			if (operateResult.Content.WordCode == OmronFinsDataType.HR.WordCode)
			{
				return OperateResult.CreateSuccessResult(hBuffer, operateResult.Content);
			}
			if (operateResult.Content.WordCode == OmronFinsDataType.AR.WordCode)
			{
				return OperateResult.CreateSuccessResult(arBuffer, operateResult.Content);
			}
			if (operateResult.Content.WordCode == 188)
			{
				return OperateResult.CreateSuccessResult(drBuffer, operateResult.Content);
			}
			if (operateResult.Content.WordCode == 220)
			{
				return OperateResult.CreateSuccessResult(irBuffer, operateResult.Content);
			}
			if (address.StartsWith("E", StringComparison.OrdinalIgnoreCase))
			{
				return OperateResult.CreateSuccessResult(emBuffer, operateResult.Content);
			}
			return new OperateResult<SoftBuffer, OmronFinsAddress>(StringResources.Language.NotSupportedDataType);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Omron.OmronFinsNet.Read(System.String,System.UInt16)" />
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			OperateResult<SoftBuffer, OmronFinsAddress> wordAddressBuffer = GetWordAddressBuffer(address);
			if (!wordAddressBuffer.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(wordAddressBuffer);
			}
			return OperateResult.CreateSuccessResult(wordAddressBuffer.Content1.GetBytes(wordAddressBuffer.Content2.AddressStart / 16 * 2, length * 2));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Omron.OmronFinsNet.Write(System.String,System.Byte[])" />
		[HslMqttApi("WriteByteArray", "")]
		public override OperateResult Write(string address, byte[] value)
		{
			OperateResult<SoftBuffer, OmronFinsAddress> wordAddressBuffer = GetWordAddressBuffer(address);
			if (!wordAddressBuffer.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(wordAddressBuffer);
			}
			wordAddressBuffer.Content1.SetBytes(value, wordAddressBuffer.Content2.AddressStart / 16 * 2);
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Omron.OmronFinsNet.ReadBool(System.String,System.UInt16)" />
		[HslMqttApi("ReadBoolArray", "")]
		public override OperateResult<bool[]> ReadBool(string address, ushort length)
		{
			OperateResult<OmronFinsAddress> operateResult = OmronFinsAddress.ParseFrom(address);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(operateResult);
			}
			if (operateResult.Content.BitCode == OmronFinsDataType.DM.BitCode)
			{
				return OperateResult.CreateSuccessResult(dBuffer.GetBool(operateResult.Content.AddressStart, length));
			}
			if (operateResult.Content.BitCode == OmronFinsDataType.CIO.BitCode)
			{
				return OperateResult.CreateSuccessResult(cioBuffer.GetBool(operateResult.Content.AddressStart, length));
			}
			if (operateResult.Content.BitCode == OmronFinsDataType.WR.BitCode)
			{
				return OperateResult.CreateSuccessResult(wBuffer.GetBool(operateResult.Content.AddressStart, length));
			}
			if (operateResult.Content.BitCode == OmronFinsDataType.HR.BitCode)
			{
				return OperateResult.CreateSuccessResult(hBuffer.GetBool(operateResult.Content.AddressStart, length));
			}
			if (operateResult.Content.BitCode == OmronFinsDataType.AR.BitCode)
			{
				return OperateResult.CreateSuccessResult(arBuffer.GetBool(operateResult.Content.AddressStart, length));
			}
			if (operateResult.Content.BitCode == 7)
			{
				return OperateResult.CreateSuccessResult(cfBuffer.GetBool(operateResult.Content.AddressStart, length));
			}
			return OperateResult.CreateSuccessResult(emBuffer.GetBool(operateResult.Content.AddressStart, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Omron.OmronFinsNet.Write(System.String,System.Boolean[])" />
		[HslMqttApi("WriteBoolArray", "")]
		public override OperateResult Write(string address, bool[] value)
		{
			OperateResult<OmronFinsAddress> operateResult = OmronFinsAddress.ParseFrom(address);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(operateResult);
			}
			if (operateResult.Content.BitCode == OmronFinsDataType.DM.BitCode)
			{
				dBuffer.SetBool(value, operateResult.Content.AddressStart);
			}
			else if (operateResult.Content.BitCode == OmronFinsDataType.CIO.BitCode)
			{
				cioBuffer.SetBool(value, operateResult.Content.AddressStart);
			}
			else if (operateResult.Content.BitCode == OmronFinsDataType.WR.BitCode)
			{
				wBuffer.SetBool(value, operateResult.Content.AddressStart);
			}
			else if (operateResult.Content.BitCode == OmronFinsDataType.HR.BitCode)
			{
				hBuffer.SetBool(value, operateResult.Content.AddressStart);
			}
			else if (operateResult.Content.BitCode == OmronFinsDataType.AR.BitCode)
			{
				arBuffer.SetBool(value, operateResult.Content.AddressStart);
			}
			else if (operateResult.Content.BitCode == 7)
			{
				cfBuffer.SetBool(value, operateResult.Content.AddressStart);
			}
			else
			{
				emBuffer.SetBool(value, operateResult.Content.AddressStart);
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new FinsMessage();
		}

		/// <inheritdoc />
		protected override OperateResult ThreadPoolLoginAfterClientCheck(PipeSession session, IPEndPoint endPoint)
		{
			if (connectionInitialization)
			{
				CommunicationPipe communication = session.Communication;
				OperateResult<byte[]> operateResult = communication.ReceiveMessage(GetNewNetMessage(), null, useActivePush: false, null, delegate(byte[] m)
				{
					LogRevcMessage(m, session);
				});
				if (!operateResult.IsSuccess)
				{
					return operateResult;
				}
				byte[] array = SoftBasic.HexStringToBytes("46 49 4E 53 00 00 00 10 00 00 00 01 00 00 00 00 00 00 00 01 00 00 00 02");
				LogSendMessage(array, session);
				OperateResult operateResult2 = communication.Send(array);
				if (!operateResult2.IsSuccess)
				{
					return operateResult2;
				}
			}
			return base.ThreadPoolLoginAfterClientCheck(session, endPoint);
		}

		/// <inheritdoc />
		protected override OperateResult<byte[]> ReadFromCoreServer(PipeSession session, byte[] receive)
		{
			byte[] array = ReadFromFinsCore(receive.RemoveBegin(26));
			if (receive != null && receive.Length > 25)
			{
				array[20] = receive[23];
				array[23] = receive[20];
				array[25] = receive[25];
			}
			return OperateResult.CreateSuccessResult(array);
		}

		/// <summary>
		/// 当收到mc协议的报文的时候应该触发的方法，允许继承重写，来实现自定义的返回，或是数据监听。<br />
		/// The method that should be triggered when a message of the mc protocol is received is allowed to be inherited and rewritten to achieve a custom return or data monitoring.
		/// </summary>
		/// <param name="finsCore">mc报文</param>
		/// <returns>返回的报文信息</returns>
		protected virtual byte[] ReadFromFinsCore(byte[] finsCore)
		{
			if (finsCore.Length == 0)
			{
				return null;
			}
			if (finsCore[0] == 1 && finsCore[1] == 1)
			{
				byte[] array = ReadByCommand(finsCore);
				return PackCommand((array == null) ? 2 : 0, finsCore, array);
			}
			if (finsCore[0] == 1 && finsCore[1] == 2)
			{
				if (!base.EnableWrite)
				{
					return PackCommand(3, finsCore, null);
				}
				return PackCommand(0, finsCore, WriteByMessage(finsCore));
			}
			if (finsCore[0] == 1 && finsCore[1] == 4)
			{
				byte[] array2 = ReadByMultiCommand(finsCore);
				return PackCommand((array2 == null) ? 2 : 0, finsCore, array2);
			}
			if (finsCore[0] == 4 && finsCore[1] == 1)
			{
				return PackCommand(0, finsCore, null);
			}
			if (finsCore[0] == 4 && finsCore[1] == 2)
			{
				return PackCommand(0, finsCore, null);
			}
			if (finsCore[0] == 5 && finsCore[1] == 1)
			{
				return PackCommand(0, finsCore, "43 4A 32 4D 2D 43 50 55 33 31 20 20 20 20 20 20 20 20 20 20 30 32 2E 30 31 00 00 00 00 00 30 32 2E 31 30 00 00 00 00 00 00 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 01 00 00 80 01 80 01 80 01 80 00 00 00 00 00 00 00 00 02 01 00 00 0A 17 80 00 08 01 00 00 00 00 00".ToHexBytes());
			}
			if (finsCore[0] == 6 && finsCore[1] == 1)
			{
				return PackCommand(0, finsCore, "05 02 00 00 00 00 00 00 00 00 20 20 20 20 20 20 20 20 20 20 20 20 20 20 20 20".ToHexBytes());
			}
			if (finsCore[0] == 7 && finsCore[1] == 1)
			{
				return PackCommand(0, finsCore, (DateTime.Now.ToString("yy-MM-dd-HH-mm-ss").Replace("-", "") + ((int)DateTime.Now.DayOfWeek).ToString("D2")).ToHexBytes());
			}
			return PackCommand(3, finsCore, null);
		}

		/// <summary>
		/// 将核心报文打包的方法，追加报文头<br />
		/// The method of packing the core message, adding the message header
		/// </summary>
		/// <param name="status">错误码</param>
		/// <param name="finsCore">Fins的核心报文</param>
		/// <param name="data">核心的内容</param>
		/// <returns>完整的报文信息</returns>
		protected virtual byte[] PackCommand(int status, byte[] finsCore, byte[] data)
		{
			if (data == null)
			{
				data = new byte[0];
			}
			byte[] array = new byte[30 + data.Length];
			SoftBasic.HexStringToBytes("46 49 4E 53 00 00 00 0000 00 00 02 00 00 00 00C0 00 02 00 EF 00 00 33 00 00 00 00 00 00").CopyTo(array, 0);
			if (data.Length != 0)
			{
				data.CopyTo(array, 30);
			}
			array[26] = finsCore[0];
			array[27] = finsCore[1];
			BitConverter.GetBytes(array.Length - 8).ReverseNew().CopyTo(array, 4);
			BitConverter.GetBytes(status).ReverseNew().CopyTo(array, 12);
			return array;
		}

		private SoftBuffer GetSoftBuffer(byte code, int startIndex)
		{
			if (code == OmronFinsDataType.DM.BitCode)
			{
				return dBuffer;
			}
			if (code == OmronFinsDataType.DM.WordCode)
			{
				return dBuffer;
			}
			if (code == OmronFinsDataType.CIO.BitCode)
			{
				return cioBuffer;
			}
			if (code == OmronFinsDataType.CIO.WordCode)
			{
				return cioBuffer;
			}
			if (code == OmronFinsDataType.WR.BitCode)
			{
				return wBuffer;
			}
			if (code == OmronFinsDataType.WR.WordCode)
			{
				return wBuffer;
			}
			if (code == OmronFinsDataType.HR.BitCode)
			{
				return hBuffer;
			}
			if (code == OmronFinsDataType.HR.WordCode)
			{
				return hBuffer;
			}
			if (code == OmronFinsDataType.AR.BitCode)
			{
				return arBuffer;
			}
			if (code == OmronFinsDataType.AR.WordCode)
			{
				return arBuffer;
			}
			switch (code)
			{
			case 7:
				return cfBuffer;
			case 188:
				return drBuffer;
			case 220:
				return irBuffer;
			case 0:
				return (startIndex >= 45056) ? arBuffer : cioBuffer;
			case 128:
				return (startIndex >= 45056) ? arBuffer : cioBuffer;
			default:
				if ((32 <= code && code < 48) || (208 <= code && code < 224))
				{
					return emBuffer;
				}
				if ((160 <= code && code < 176) || (80 <= code && code < 96))
				{
					return emBuffer;
				}
				if (144 <= code && code < 153)
				{
					return emBuffer;
				}
				if (code == 10)
				{
					return emBuffer;
				}
				throw new Exception(StringResources.Language.NotSupportedDataType);
			}
		}

		private byte[] ReadByCommand(byte[] command)
		{
			if (command[2] == OmronFinsDataType.DM.BitCode || command[2] == OmronFinsDataType.CIO.BitCode || command[2] == OmronFinsDataType.WR.BitCode || command[2] == OmronFinsDataType.HR.BitCode || command[2] == OmronFinsDataType.AR.BitCode || command[2] == 0 || command[2] == 7 || command[2] == 10 || (32 <= command[2] && command[2] < 48) || (208 <= command[2] && command[2] < 224))
			{
				ushort length = (ushort)(command[6] * 256 + command[7]);
				int num = (command[3] * 256 + command[4]) * 16 + command[5];
				SoftBuffer softBuffer = GetSoftBuffer(command[2], num);
				if ((command[2] == 0 || command[2] == 128) && num >= 45056)
				{
					num -= 45056;
				}
				return (from m in softBuffer.GetBool(num, length)
					select (byte)(m ? 1 : 0)).ToArray();
			}
			if (command[2] == OmronFinsDataType.DM.WordCode || command[2] == OmronFinsDataType.CIO.WordCode || command[2] == OmronFinsDataType.WR.WordCode || command[2] == OmronFinsDataType.HR.WordCode || command[2] == OmronFinsDataType.AR.WordCode || command[2] == 128 || command[2] == 188 || command[2] == 220 || command[2] == 152 || (160 <= command[2] && command[2] < 176) || (80 <= command[2] && command[2] < 96))
			{
				ushort num2 = (ushort)(command[6] * 256 + command[7]);
				int num3 = command[3] * 256 + command[4];
				if (num2 > 999)
				{
					return null;
				}
				SoftBuffer softBuffer2 = GetSoftBuffer(command[2], num3);
				if ((command[2] == 0 || command[2] == 128) && num3 >= 45056)
				{
					num3 -= 45056;
				}
				return softBuffer2.GetBytes(num3 * 2, num2 * 2);
			}
			return new byte[0];
		}

		private byte[] ReadByMultiCommand(byte[] command)
		{
			MemoryStream memoryStream = new MemoryStream();
			for (int i = 2; i < command.Length; i += 4)
			{
				int num = command[i + 1] * 256 + command[i + 2];
				if (command[i] == OmronFinsDataType.DM.WordCode || command[i] == OmronFinsDataType.CIO.WordCode || command[i] == OmronFinsDataType.WR.WordCode || command[i] == OmronFinsDataType.HR.WordCode || command[i] == OmronFinsDataType.AR.WordCode || command[i] == 128 || command[i] == 188 || command[i] == 220 || command[i] == 152 || (160 <= command[i] && command[i] < 176) || (80 <= command[i] && command[i] < 96))
				{
					memoryStream.WriteByte(command[i]);
					SoftBuffer softBuffer = GetSoftBuffer(command[i], num);
					if ((command[2] == 0 || command[2] == 128) && num >= 45056)
					{
						num -= 45056;
					}
					memoryStream.Write(softBuffer.GetBytes(num * 2, 2));
				}
			}
			return memoryStream.ToArray();
		}

		private byte[] WriteByMessage(byte[] command)
		{
			if (command[2] == OmronFinsDataType.DM.BitCode || command[2] == OmronFinsDataType.CIO.BitCode || command[2] == OmronFinsDataType.WR.BitCode || command[2] == OmronFinsDataType.HR.BitCode || command[2] == OmronFinsDataType.AR.BitCode || command[2] == 0 || command[2] == 7 || (32 <= command[2] && command[2] < 48) || (208 <= command[2] && command[2] < 224))
			{
				ushort num = (ushort)(command[6] * 256 + command[7]);
				int num2 = (command[3] * 256 + command[4]) * 16 + command[5];
				bool[] value = (from m in SoftBasic.ArrayRemoveBegin(command, 8)
					select m == 1).ToArray();
				SoftBuffer softBuffer = GetSoftBuffer(command[2], num2);
				if ((command[2] == 0 || command[2] == 128) && num2 >= 45056)
				{
					num2 -= 45056;
				}
				softBuffer.SetBool(value, num2);
				return new byte[0];
			}
			ushort num3 = (ushort)(command[6] * 256 + command[7]);
			int num4 = command[3] * 256 + command[4];
			byte[] data = SoftBasic.ArrayRemoveBegin(command, 8);
			SoftBuffer softBuffer2 = GetSoftBuffer(command[2], num4);
			if ((command[2] == 0 || command[2] == 128) && num4 >= 45056)
			{
				num4 -= 45056;
			}
			softBuffer2.SetBytes(data, num4 * 2);
			return new byte[0];
		}

		/// <inheritdoc />
		protected override void LoadFromBytes(byte[] content)
		{
			if (content.Length < 786432)
			{
				throw new Exception("File is not correct");
			}
			dBuffer.SetBytes(content, 0, 0, 131072);
			cioBuffer.SetBytes(content, 131072, 0, 131072);
			wBuffer.SetBytes(content, 262144, 0, 131072);
			hBuffer.SetBytes(content, 393216, 0, 131072);
			arBuffer.SetBytes(content, 524288, 0, 131072);
			emBuffer.SetBytes(content, 655360, 0, 131072);
			if (content.Length >= 917504)
			{
				cfBuffer.SetBytes(content, 786432, 0, 131072);
			}
		}

		/// <inheritdoc />
		protected override byte[] SaveToBytes()
		{
			byte[] array = new byte[917504];
			Array.Copy(dBuffer.GetBytes(), 0, array, 0, 131072);
			Array.Copy(cioBuffer.GetBytes(), 0, array, 131072, 131072);
			Array.Copy(wBuffer.GetBytes(), 0, array, 262144, 131072);
			Array.Copy(hBuffer.GetBytes(), 0, array, 393216, 131072);
			Array.Copy(arBuffer.GetBytes(), 0, array, 524288, 131072);
			Array.Copy(emBuffer.GetBytes(), 0, array, 655360, 131072);
			Array.Copy(cfBuffer.GetBytes(), 0, array, 786432, 131072);
			return array;
		}

		/// <inheritdoc cref="M:System.IDisposable.Dispose" />
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				dBuffer?.Dispose();
				cioBuffer?.Dispose();
				wBuffer?.Dispose();
				hBuffer?.Dispose();
				arBuffer?.Dispose();
				emBuffer?.Dispose();
				cfBuffer?.Dispose();
			}
			base.Dispose(disposing);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"OmronFinsServer[{base.Port}]";
		}
	}
}
