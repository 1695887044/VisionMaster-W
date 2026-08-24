using System;
using HslCommunication.Instrument.DLT.Helper;

namespace HslCommunication.Core.IMessage
{
	/// <summary>
	/// DLT698的协议消息文本
	/// </summary>
	public class DLT698Message : NetMessageBase, INetMessage
	{
		/// <inheritdoc cref="P:HslCommunication.Core.IMessage.INetMessage.ProtocolHeadBytesLength" />
		public int ProtocolHeadBytesLength => 8;

		/// <inheritdoc cref="M:HslCommunication.Core.IMessage.INetMessage.GetContentLengthByHeadBytes" />
		public int GetContentLengthByHeadBytes()
		{
			return BitConverter.ToUInt16(base.HeadBytes, 1) + 2 - 8;
		}

		/// <inheritdoc />
		public override int PependedUselesByteLength(byte[] headByte)
		{
			return DLT645Helper.FindHeadCode68H(headByte);
		}
	}
}
