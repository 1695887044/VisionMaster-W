using System.IO;
using HslCommunication.Profinet.Siemens.Helper;

namespace HslCommunication.Core.IMessage
{
	/// <summary>
	/// 西门子PPI协议的消息类
	/// </summary>
	public class SiemensPPIMessage : NetMessageBase, INetMessage
	{
		/// <inheritdoc cref="P:HslCommunication.Core.IMessage.INetMessage.ProtocolHeadBytesLength" />
		public int ProtocolHeadBytesLength => -1;

		/// <inheritdoc cref="M:HslCommunication.Core.IMessage.INetMessage.GetContentLengthByHeadBytes" />
		public int GetContentLengthByHeadBytes()
		{
			return 0;
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IMessage.INetMessage.CheckReceiveDataComplete(System.Byte[],System.IO.MemoryStream)" />
		public override bool CheckReceiveDataComplete(byte[] send, MemoryStream ms)
		{
			return SiemensPPIHelper.CheckReceiveDataComplete(ms);
		}
	}
}
