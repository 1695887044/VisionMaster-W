namespace HslCommunication.Core.IMessage
{
	/// <summary>
	/// 图尔克Reader协议的消息
	/// </summary>
	public class TurckReaderMessage : NetMessageBase, INetMessage
	{
		/// <inheritdoc cref="P:HslCommunication.Core.IMessage.INetMessage.ProtocolHeadBytesLength" />
		public int ProtocolHeadBytesLength => 3;

		/// <inheritdoc cref="M:HslCommunication.Core.IMessage.INetMessage.CheckHeadBytesLegal(System.Byte[])" />
		public override bool CheckHeadBytesLegal(byte[] token)
		{
			if (base.HeadBytes == null)
			{
				return true;
			}
			return base.HeadBytes[0] == 170;
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IMessage.INetMessage.GetContentLengthByHeadBytes" />
		public int GetContentLengthByHeadBytes()
		{
			if (base.HeadBytes[2] <= 3)
			{
				return 0;
			}
			int num = base.HeadBytes[2] - 3;
			if (num < 0)
			{
				num = 0;
			}
			return num;
		}

		/// <inheritdoc />
		public override int CheckMessageMatch(byte[] send, byte[] receive)
		{
			if (CheckResponseACK(receive))
			{
				return -1;
			}
			return 1;
		}

		private static bool CheckResponseACK(byte[] content)
		{
			try
			{
				if (content[1] == 7 && content[2] == 7 && (content[3] == 104 || (content[3] == 105 && content[4] == 137) || content[3] == 112 || (content[3] == 105 && content[4] == 129)))
				{
					return true;
				}
			}
			catch
			{
				return false;
			}
			return false;
		}
	}
}
