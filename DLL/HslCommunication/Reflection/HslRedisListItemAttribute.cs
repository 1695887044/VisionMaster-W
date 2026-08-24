using System;

namespace HslCommunication.Reflection
{
	/// <summary>
	/// 对应redis的一个列表信息的内容
	/// </summary>
	[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
	public class HslRedisListItemAttribute : Attribute
	{
		/// <summary>
		/// 列表键值的名称
		/// </summary>
		public string ListKey { get; set; }

		/// <summary>
		/// 当前的位置的索引
		/// </summary>
		public long Index { get; set; }

		/// <summary>
		/// 根据键名来读取写入当前的列表中的单个信息
		/// </summary>
		/// <param name="listKey">列表键名</param>
		/// <param name="index">当前的索引位置</param>
		public HslRedisListItemAttribute(string listKey, long index)
		{
			ListKey = listKey;
			Index = index;
		}
	}
}
