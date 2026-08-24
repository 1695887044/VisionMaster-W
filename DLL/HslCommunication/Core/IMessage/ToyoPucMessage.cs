using System;

namespace HslCommunication.Core.IMessage
{
	/// <summary>
	/// 丰田工机的PLC的协议消息
	/// </summary>
	public class ToyoPucMessage : NetMessageBase, INetMessage
	{
		/// <inheritdoc cref="P:HslCommunication.Core.IMessage.INetMessage.ProtocolHeadBytesLength" />
		public int ProtocolHeadBytesLength => 4;

		/// <inheritdoc cref="M:HslCommunication.Core.IMessage.INetMessage.GetContentLengthByHeadBytes" />
		public int GetContentLengthByHeadBytes()
		{
			try
			{
				byte[] headBytes = base.HeadBytes;
				if (headBytes != null && headBytes.Length >= 4)
				{
					return BitConverter.ToUInt16(base.HeadBytes, 2);
				}
				return 0;
			}
			catch
			{
				return 0;
			}
		}
	}
}
