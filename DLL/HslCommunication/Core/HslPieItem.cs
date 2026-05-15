using System.Drawing;

namespace HslCommunication.Core
{
	/// <summary>
	/// 饼图的基本元素
	/// </summary>
	public class HslPieItem
	{
		/// <summary>
		/// 名称
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// 值
		/// </summary>
		public int Value { get; set; }

		/// <summary>
		/// 背景颜色
		/// </summary>
		public Color Back { get; set; }

		/// <summary>
		/// 实例化一个饼图基本元素的对象
		/// </summary>
		public HslPieItem()
		{
			Back = Color.DodgerBlue;
		}
	}
}
