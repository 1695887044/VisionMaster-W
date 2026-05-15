using System;

namespace HslCommunication.Reflection
{
	/// <summary>
	/// 对应redis的一个哈希信息的内容
	/// </summary>
	[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
	public class HslRedisHashFieldAttribute : Attribute
	{
		/// <summary>
		/// 哈希键值的名称
		/// </summary>
		public string HaskKey { get; set; }

		/// <summary>
		/// 当前的哈希域名称
		/// </summary>
		public string Field { get; set; }

		/// <summary>
		/// 根据键名来读取写入当前的哈希的单个信息
		/// </summary>
		/// <param name="hashKey">哈希键名</param>
		/// <param name="filed">哈希域名称</param>
		public HslRedisHashFieldAttribute(string hashKey, string filed)
		{
			HaskKey = hashKey;
			Field = filed;
		}
	}
}
