using System;

namespace HslCommunication.Reflection
{
	/// <summary>
	/// 对应redis的一个列表信息的内容
	/// </summary>
	[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
	public class HslRedisListAttribute : Attribute
	{
		/// <summary>
		/// 列表键值的名称
		/// </summary>
		public string ListKey { get; set; }

		/// <summary>
		/// 当前的位置的索引
		/// </summary>
		public long StartIndex { get; set; }

		/// <summary>
		/// 当前位置的结束索引
		/// </summary>
		public long EndIndex { get; set; } = -1L;


		/// <summary>
		/// 根据键名来读取写入当前的列表中的多个信息
		/// </summary>
		/// <param name="listKey">列表键名</param>
		public HslRedisListAttribute(string listKey)
		{
			ListKey = listKey;
		}

		/// <summary>
		/// 根据键名来读取写入当前的列表中的多个信息
		/// </summary>
		/// <param name="listKey">列表键名</param>
		/// <param name="startIndex">开始的索引信息</param>
		public HslRedisListAttribute(string listKey, long startIndex)
		{
			ListKey = listKey;
			StartIndex = startIndex;
		}

		/// <summary>
		/// 根据键名来读取写入当前的列表中的多个信息
		/// </summary>
		/// <param name="listKey">列表键名</param>
		/// <param name="startIndex">开始的索引信息</param>
		/// <param name="endIndex">结束的索引位置，-1为倒数第一个，以此类推。</param>
		public HslRedisListAttribute(string listKey, long startIndex, long endIndex)
		{
			ListKey = listKey;
			StartIndex = startIndex;
			EndIndex = endIndex;
		}
	}
}
