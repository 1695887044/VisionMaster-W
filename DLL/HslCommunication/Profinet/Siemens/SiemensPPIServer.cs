using System.Collections.Generic;
using HslCommunication.BasicFramework;
using HslCommunication.Core.IMessage;
using HslCommunication.Core.Net;
using HslCommunication.Core.Pipe;
using HslCommunication.Serial;

namespace HslCommunication.Profinet.Siemens
{
	/// <summary>
	/// PPIServer的虚拟服务器对象，支持的地址类型和S7虚拟服务器一致
	/// </summary>
	public class SiemensPPIServer : SiemensS7Server
	{
		/// <inheritdoc cref="P:HslCommunication.Profinet.Siemens.SiemensPPIOverTcp.Station" />
		public byte Station { get; set; }

		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		public SiemensPPIServer()
		{
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new SiemensPPIServerMessage();
		}

		/// <inheritdoc />
		protected override OperateResult<byte[]> ReadFromCoreServer(PipeSession session, byte[] receive)
		{
			CommunicationPipe communication = session.Communication;
			if (receive[4] != Station)
			{
				return new OperateResult<byte[]>($"Station not match, expect: {Station}, but actual: {receive[4]}");
			}
			byte[] array = new byte[1] { 229 };
			LogSendMessage(array, session);
			communication.Send(array);
			OperateResult<byte[]> operateResult = communication.ReceiveMessage(new SpecifiedCharacterMessage(22), null, useActivePush: false, null, delegate(byte[] m)
			{
				LogRevcMessage(m, session);
			});
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			return base.ReadFromCoreServer(session, receive);
		}

		/// <inheritdoc />
		protected override bool IsNeedShakeHands()
		{
			return false;
		}

		/// <inheritdoc />
		protected override byte[] PackReadBack(byte[] command, List<byte> content)
		{
			byte[] array = new byte[21 + content.Count + 2];
			SoftBasic.HexStringToBytes("68 1D 1D 68 00 02 08 32 03 00 00 00 00 00 02 00 0C 00 00 04 01").CopyTo(array, 0);
			array[1] = (byte)(array.Length - 6);
			array[2] = (byte)(array.Length - 6);
			array[15] = (byte)(content.Count / 256);
			array[16] = (byte)(content.Count % 256);
			array[20] = command[18];
			content.CopyTo(array, 21);
			array[array.Length - 2] = (byte)SoftLRC.CalculateAcc(array, 4, 2);
			array[array.Length - 1] = 22;
			return array;
		}

		/// <inheritdoc />
		protected override byte[] PackWriteBack(byte[] packCommand, byte[] status)
		{
			byte[] array = new byte[23 + status.Length];
			SoftBasic.HexStringToBytes("68 12 12 68 00 02 08 32 03 00 00 00 01 00 02 00 01 00 00 05 01 04 00 16").CopyTo(array, 0);
			array[20] = (byte)status.Length;
			status.CopyTo(array, 21);
			array[array.Length - 2] = (byte)SoftLRC.CalculateAcc(array, 4, 2);
			array[array.Length - 1] = 22;
			return array;
		}

		/// <inheritdoc />
		protected override bool CheckSerialReceiveDataComplete(byte[] buffer, int receivedLength)
		{
			if (receivedLength == 6 && buffer[0] == 16 && buffer[1] == 2 && buffer[5] == 22)
			{
				return true;
			}
			if (receivedLength > 6 && buffer[0] == 104 && buffer[1] + 6 == receivedLength && buffer[receivedLength - 1] == 22)
			{
				return true;
			}
			return base.CheckSerialReceiveDataComplete(buffer, receivedLength);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"SiemensPPIServer[{base.Port}]";
		}
	}
}
