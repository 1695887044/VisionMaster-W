namespace HslCommunication.Core.IMessage
{
	/// <summary>
	/// 西门子Fetch/Write消息解析协议
	/// </summary>
	public class FetchWriteMessage : NetMessageBase, INetMessage
	{
		/// <inheritdoc cref="P:HslCommunication.Core.IMessage.INetMessage.ProtocolHeadBytesLength" />
		public int ProtocolHeadBytesLength => 16;

		/// <inheritdoc cref="M:HslCommunication.Core.IMessage.INetMessage.GetContentLengthByHeadBytes" />
		public int GetContentLengthByHeadBytes()
		{
			if (base.HeadBytes[5] == 5 || base.HeadBytes[5] == 4)
			{
				return 0;
			}
			if (base.HeadBytes[5] == 6)
			{
				if (base.SendBytes == null)
				{
					return 0;
				}
				if (base.HeadBytes[8] != 0)
				{
					return 0;
				}
				if (base.SendBytes[8] == 1 || base.SendBytes[8] == 6 || base.SendBytes[8] == 7)
				{
					return (base.SendBytes[12] * 256 + base.SendBytes[13]) * 2;
				}
				return base.SendBytes[12] * 256 + base.SendBytes[13];
			}
			if (base.HeadBytes[5] == 3)
			{
				if (base.HeadBytes[8] == 1 || base.HeadBytes[8] == 6 || base.HeadBytes[8] == 7)
				{
					return (base.HeadBytes[12] * 256 + base.HeadBytes[13]) * 2;
				}
				return base.HeadBytes[12] * 256 + base.HeadBytes[13];
			}
			return 0;
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IMessage.INetMessage.CheckHeadBytesLegal(System.Byte[])" />
		public override bool CheckHeadBytesLegal(byte[] token)
		{
			if (base.HeadBytes == null)
			{
				return false;
			}
			if (base.HeadBytes[0] == 83 && base.HeadBytes[1] == 53)
			{
				return true;
			}
			return false;
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IMessage.INetMessage.GetHeadBytesIdentity" />
		public override int GetHeadBytesIdentity()
		{
			return base.HeadBytes[3];
		}
	}
}
