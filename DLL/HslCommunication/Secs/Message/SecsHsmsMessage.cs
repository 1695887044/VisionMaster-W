using System;
using HslCommunication.Core.IMessage;

namespace HslCommunication.Secs.Message
{
	/// <summary>
	/// Hsms协议的消息定义
	/// </summary>
	public class SecsHsmsMessage : NetMessageBase, INetMessage
	{
		/// <inheritdoc cref="P:HslCommunication.Core.IMessage.INetMessage.ProtocolHeadBytesLength" />
		public int ProtocolHeadBytesLength => 4;

		/// <inheritdoc cref="M:HslCommunication.Core.IMessage.INetMessage.GetContentLengthByHeadBytes" />
		public int GetContentLengthByHeadBytes()
		{
			int num = BitConverter.ToInt32(new byte[4]
			{
				base.HeadBytes[3],
				base.HeadBytes[2],
				base.HeadBytes[1],
				base.HeadBytes[0]
			}, 0);
			if (num < 0)
			{
				return 0;
			}
			return num;
		}

		/// <inheritdoc />
		public override bool CheckHeadBytesLegal(byte[] token)
		{
			return true;
		}
	}
}
