namespace HslCommunication.Core.IMessage
{
	/// <summary>
	/// 西门子S7协议的消息解析规则
	/// </summary>
	public class S7Message : NetMessageBase, INetMessage
	{
		/// <inheritdoc cref="P:HslCommunication.Core.IMessage.INetMessage.ProtocolHeadBytesLength" />
		public int ProtocolHeadBytesLength => 4;

		/// <inheritdoc cref="M:HslCommunication.Core.IMessage.INetMessage.CheckHeadBytesLegal(System.Byte[])" />
		public override bool CheckHeadBytesLegal(byte[] token)
		{
			if (base.HeadBytes == null)
			{
				return false;
			}
			if (base.HeadBytes[0] == 3 && base.HeadBytes[1] == 0)
			{
				return true;
			}
			return false;
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IMessage.INetMessage.GetContentLengthByHeadBytes" />
		public int GetContentLengthByHeadBytes()
		{
			byte[] headBytes = base.HeadBytes;
			if (headBytes != null && headBytes.Length >= 4)
			{
				int num = base.HeadBytes[2] * 256 + base.HeadBytes[3] - 4;
				if (num < 0)
				{
					num = 0;
				}
				return num;
			}
			return 0;
		}
	}
}
