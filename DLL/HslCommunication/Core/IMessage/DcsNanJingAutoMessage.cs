namespace HslCommunication.Core.IMessage
{
	/// <summary>
	/// 南京自动化研究所推出的DCS设备的消息类
	/// </summary>
	public class DcsNanJingAutoMessage : NetMessageBase, INetMessage
	{
		/// <inheritdoc cref="P:HslCommunication.Core.IMessage.INetMessage.ProtocolHeadBytesLength" />
		public int ProtocolHeadBytesLength => 6;

		/// <inheritdoc cref="M:HslCommunication.Core.IMessage.INetMessage.GetContentLengthByHeadBytes" />
		public int GetContentLengthByHeadBytes()
		{
			if (base.HeadBytes?.Length >= ProtocolHeadBytesLength)
			{
				return base.HeadBytes[4] * 256 + base.HeadBytes[5];
			}
			return 0;
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IMessage.INetMessage.GetHeadBytesIdentity" />
		public override int GetHeadBytesIdentity()
		{
			return base.HeadBytes[0] * 256 + base.HeadBytes[1];
		}
	}
}
