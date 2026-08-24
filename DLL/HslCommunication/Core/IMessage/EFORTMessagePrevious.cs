using System;

namespace HslCommunication.Core.IMessage
{
	/// <summary>
	/// 旧版的机器人的消息类对象，保留此类为了实现兼容
	/// </summary>
	public class EFORTMessagePrevious : NetMessageBase, INetMessage
	{
		/// <inheritdoc cref="P:HslCommunication.Core.IMessage.INetMessage.ProtocolHeadBytesLength" />
		public int ProtocolHeadBytesLength => 17;

		/// <inheritdoc cref="M:HslCommunication.Core.IMessage.INetMessage.GetContentLengthByHeadBytes" />
		public int GetContentLengthByHeadBytes()
		{
			int num = BitConverter.ToInt16(base.HeadBytes, 15) - 17;
			if (num < 0)
			{
				num = 0;
			}
			return num;
		}
	}
}
