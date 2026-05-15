using HslCommunication.Core.IMessage;

namespace HslCommunication.CNC.Fanuc
{
	/// <summary>
	/// Fanuc床子的消息对象
	/// </summary>
	public class CNCFanucSeriesMessage : NetMessageBase, INetMessage
	{
		/// <inheritdoc />
		public int ProtocolHeadBytesLength => 10;

		/// <inheritdoc />
		public int GetContentLengthByHeadBytes()
		{
			return base.HeadBytes[8] * 256 + base.HeadBytes[9];
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return "CNCFanucSeriesMessage";
		}
	}
}
