namespace HslCommunication.Core.IMessage
{
	/// <summary>
	/// Kuka机器人的 KRC4 控制器中的服务器KUKAVARPROXY
	/// </summary>
	public class KukaVarProxyMessage : NetMessageBase, INetMessage
	{
		/// <inheritdoc cref="P:HslCommunication.Core.IMessage.INetMessage.ProtocolHeadBytesLength" />
		public int ProtocolHeadBytesLength => 4;

		/// <inheritdoc cref="M:HslCommunication.Core.IMessage.INetMessage.GetContentLengthByHeadBytes" />
		public int GetContentLengthByHeadBytes()
		{
			byte[] headBytes = base.HeadBytes;
			if (headBytes != null && headBytes.Length >= 4)
			{
				return base.HeadBytes[2] * 256 + base.HeadBytes[3];
			}
			return 0;
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IMessage.INetMessage.GetHeadBytesIdentity" />
		public override int GetHeadBytesIdentity()
		{
			byte[] headBytes = base.HeadBytes;
			if (headBytes != null && headBytes.Length >= 4)
			{
				return base.HeadBytes[0] * 256 + base.HeadBytes[1];
			}
			return 0;
		}
	}
}
