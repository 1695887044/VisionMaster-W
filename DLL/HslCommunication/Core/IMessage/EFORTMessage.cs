using System;

namespace HslCommunication.Core.IMessage
{
	/// <summary>
	/// 埃夫特机器人的消息对象
	/// </summary>
	public class EFORTMessage : NetMessageBase, INetMessage
	{
		/// <inheritdoc cref="P:HslCommunication.Core.IMessage.INetMessage.ProtocolHeadBytesLength" />
		public int ProtocolHeadBytesLength => 18;

		/// <inheritdoc cref="M:HslCommunication.Core.IMessage.INetMessage.GetContentLengthByHeadBytes" />
		public int GetContentLengthByHeadBytes()
		{
			int num = BitConverter.ToInt16(base.HeadBytes, 16) - 18;
			if (num < 0)
			{
				num = 0;
			}
			return num;
		}
	}
}
