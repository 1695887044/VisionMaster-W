using System;

namespace HslCommunication.Core.IMessage
{
	/// <summary>
	/// 倍福的ADS协议的信息
	/// </summary>
	public class AdsNetMessage : NetMessageBase, INetMessage
	{
		/// <inheritdoc cref="P:HslCommunication.Core.IMessage.INetMessage.ProtocolHeadBytesLength" />
		public int ProtocolHeadBytesLength => 6;

		/// <inheritdoc cref="M:HslCommunication.Core.IMessage.INetMessage.GetContentLengthByHeadBytes" />
		public int GetContentLengthByHeadBytes()
		{
			byte[] headBytes = base.HeadBytes;
			if (headBytes != null && headBytes.Length >= 6)
			{
				int num = BitConverter.ToInt32(base.HeadBytes, 2);
				if (num > 100000000)
				{
					num = 100000000;
				}
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
