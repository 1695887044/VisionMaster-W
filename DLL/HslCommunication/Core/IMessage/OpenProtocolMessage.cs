using System;
using System.Text;

namespace HslCommunication.Core.IMessage
{
	/// <summary>
	/// OpenProtocol协议的消息
	/// </summary>
	public class OpenProtocolMessage : NetMessageBase, INetMessage
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
					int num = Convert.ToInt32(Encoding.ASCII.GetString(base.HeadBytes, 0, 4)) - 4 + 1;
					return (num >= 0) ? num : 0;
				}
				return 0;
			}
			catch
			{
				return 17;
			}
		}
	}
}
