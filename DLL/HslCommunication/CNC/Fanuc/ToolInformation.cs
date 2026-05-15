using HslCommunication.Core;

namespace HslCommunication.CNC.Fanuc
{
	/// <summary>
	/// 刀具信息
	/// </summary>
	public class ToolInformation
	{
		/// <summary>
		/// 当前刀具的寿命
		/// </summary>
		public int Life { get; set; }

		/// <summary>
		/// 当前刀具的使用次数
		/// </summary>
		public int Use { get; set; }

		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		public ToolInformation()
		{
		}

		/// <summary>
		/// 指定内存数据来初始化对象
		/// </summary>
		/// <param name="content">机床返回的数据</param>
		/// <param name="byteTransform">字节变换规则</param>
		public ToolInformation(byte[] content, IByteTransform byteTransform)
		{
			Life = byteTransform.TransInt32(content, 26);
			Use = byteTransform.TransInt32(content, 34);
		}
	}
}
