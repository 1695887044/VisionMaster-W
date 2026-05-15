using System.IO;
using HslCommunication.Instrument.DLT.Helper;

namespace HslCommunication.Core.IMessage
{
	/// <summary>
	/// CJT188的协议信息
	/// </summary>
	public class CJT188Message : NetMessageBase, INetMessage
	{
		/// <inheritdoc cref="P:HslCommunication.Core.IMessage.INetMessage.ProtocolHeadBytesLength" />
		public int ProtocolHeadBytesLength => 11;

		/// <summary>
		/// 获取或设置是否验证匹配接收到的站号信息<br />
		/// Gets or sets whether to verify that the received station number information is matched
		/// </summary>
		public bool StationMatch { get; set; } = false;


		/// <summary>
		/// 根据是否进行站号检查来实例化一个对象
		/// </summary>
		/// <param name="stationMatch">是否进行站号检查</param>
		public CJT188Message(bool stationMatch)
		{
			StationMatch = stationMatch;
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IMessage.INetMessage.GetContentLengthByHeadBytes" />
		public int GetContentLengthByHeadBytes()
		{
			return base.HeadBytes[10] + 2;
		}

		/// <inheritdoc />
		public override int PependedUselesByteLength(byte[] headByte)
		{
			return DLT645Helper.FindHeadCode68H(headByte);
		}

		/// <inheritdoc />
		public override int CheckMessageMatch(byte[] send, byte[] receive)
		{
			if (!StationMatch)
			{
				return 1;
			}
			if (send.Length < 9 || receive.Length < 9)
			{
				return 1;
			}
			string text = send.SelectMiddle(2, 7).ToHexString();
			string text2 = receive.SelectMiddle(2, 7).ToHexString();
			if (text == "AAAAAAAAAAAAAA" || text2 == "AAAAAAAAAAAAAA" || text == text2)
			{
				return 1;
			}
			return -1;
		}

		/// <inheritdoc />
		public override bool CheckReceiveDataComplete(byte[] send, MemoryStream ms)
		{
			byte[] array = ms.ToArray();
			if (array.Length < 11)
			{
				return false;
			}
			int num = DLT645Helper.FindHeadCode68H(array);
			if (num < 0)
			{
				return false;
			}
			if (array[num + 10] + 13 + num == array.Length && array[array.Length - 1] == 22)
			{
				return true;
			}
			return false;
		}
	}
}
