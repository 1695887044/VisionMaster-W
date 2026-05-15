using System.IO;
using HslCommunication.Instrument.DLT.Helper;

namespace HslCommunication.Core.IMessage
{
	/// <summary>
	/// DLT 645协议的串口透传的消息类
	/// </summary>
	public class DLT645Message : NetMessageBase, INetMessage
	{
		/// <inheritdoc cref="P:HslCommunication.Core.IMessage.INetMessage.ProtocolHeadBytesLength" />
		public int ProtocolHeadBytesLength => 10;

		/// <inheritdoc cref="M:HslCommunication.Core.IMessage.INetMessage.GetContentLengthByHeadBytes" />
		public int GetContentLengthByHeadBytes()
		{
			return base.HeadBytes[9] + 2;
		}

		/// <inheritdoc />
		public override int PependedUselesByteLength(byte[] headByte)
		{
			int num = DLT645Helper.FindHeadCode68H(headByte);
			if (num < 0)
			{
				return 10;
			}
			return num;
		}

		/// <inheritdoc />
		public override bool CheckReceiveDataComplete(byte[] send, MemoryStream ms)
		{
			return DLT645Helper.CheckReceiveDataComplete(ms);
		}
	}
}
