namespace HslCommunication.Core.IMessage
{
	/// <summary>
	/// SAM身份证通信协议的消息
	/// </summary>
	public class SAMMessage : NetMessageBase, INetMessage
	{
		/// <inheritdoc cref="P:HslCommunication.Core.IMessage.INetMessage.ProtocolHeadBytesLength" />
		public int ProtocolHeadBytesLength => 7;

		/// <inheritdoc cref="M:HslCommunication.Core.IMessage.INetMessage.CheckHeadBytesLegal(System.Byte[])" />
		public override bool CheckHeadBytesLegal(byte[] token)
		{
			if (base.HeadBytes == null)
			{
				return true;
			}
			return base.HeadBytes[0] == 170 && base.HeadBytes[1] == 170 && base.HeadBytes[2] == 170 && base.HeadBytes[3] == 150 && base.HeadBytes[4] == 105;
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IMessage.SAMMessage.GetContentLengthByHeadBytes" />
		public int GetContentLengthByHeadBytes()
		{
			byte[] headBytes = base.HeadBytes;
			if (headBytes != null && headBytes.Length >= 7)
			{
				return base.HeadBytes[5] * 256 + base.HeadBytes[6];
			}
			return 0;
		}
	}
}
