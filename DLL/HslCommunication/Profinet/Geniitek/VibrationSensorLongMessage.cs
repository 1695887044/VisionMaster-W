using HslCommunication.Core.IMessage;

namespace HslCommunication.Profinet.Geniitek
{
	/// <summary>
	/// 完整的数据报文信息
	/// </summary>
	public class VibrationSensorLongMessage : NetMessageBase, INetMessage
	{
		/// <inheritdoc cref="P:HslCommunication.Core.IMessage.INetMessage.ProtocolHeadBytesLength" />
		public int ProtocolHeadBytesLength => 12;

		/// <inheritdoc cref="M:HslCommunication.Core.IMessage.INetMessage.CheckHeadBytesLegal(System.Byte[])" />
		public override bool CheckHeadBytesLegal(byte[] token)
		{
			if (base.HeadBytes == null)
			{
				return false;
			}
			if (base.HeadBytes[0] == 170 && base.HeadBytes[1] == 85 && base.HeadBytes[2] == 127)
			{
				return true;
			}
			return false;
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IMessage.INetMessage.GetContentLengthByHeadBytes" />
		public int GetContentLengthByHeadBytes()
		{
			return base.HeadBytes[10] * 256 + base.HeadBytes[11] + 4;
		}
	}
}
