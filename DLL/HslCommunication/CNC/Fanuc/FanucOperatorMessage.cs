using System.Text;
using HslCommunication.Core;

namespace HslCommunication.CNC.Fanuc
{
	/// <summary>
	/// Fanuc机床的操作信息
	/// </summary>
	public class FanucOperatorMessage
	{
		/// <summary>
		/// Number of operator's message
		/// </summary>
		public short Number { get; set; }

		/// <summary>
		/// Kind of operator's message
		/// </summary>
		public short Type { get; set; }

		/// <summary>
		/// Operator's message strings
		/// </summary>
		public string Data { get; set; }

		/// <summary>
		/// 创建一个fanuc的操作消息对象
		/// </summary>
		/// <param name="byteTransform">数据变换对象</param>
		/// <param name="buffer">读取的数据缓存信息</param>
		/// <param name="encoding">解析的编码信息</param>
		/// <returns>fanuc设备的操作信息</returns>
		public static FanucOperatorMessage CreateMessage(IByteTransform byteTransform, byte[] buffer, Encoding encoding)
		{
			FanucOperatorMessage fanucOperatorMessage = new FanucOperatorMessage();
			fanucOperatorMessage.Number = byteTransform.TransInt16(buffer, 2);
			fanucOperatorMessage.Type = byteTransform.TransInt16(buffer, 6);
			short num = byteTransform.TransInt16(buffer, 10);
			if (num + 12 <= buffer.Length)
			{
				fanucOperatorMessage.Data = encoding.GetString(buffer, 12, num);
			}
			else
			{
				fanucOperatorMessage.Data = encoding.GetString(buffer, 12, buffer.Length - 12).TrimEnd(default(char));
			}
			return fanucOperatorMessage;
		}
	}
}
