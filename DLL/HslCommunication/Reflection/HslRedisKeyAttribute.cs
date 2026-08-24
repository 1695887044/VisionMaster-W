using System;

namespace HslCommunication.Reflection
{
	/// <summary>
	/// 对应redis的一个键值信息的内容
	/// </summary>
	[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
	public class HslRedisKeyAttribute : Attribute
	{
		/// <summary>
		/// 键值的名称
		/// </summary>
		public string KeyName { get; set; }

		/// <summary>
		/// 根据键名来读取写入当前的数据信息
		/// </summary>
		/// <param name="key">键名</param>
		public HslRedisKeyAttribute(string key)
		{
			KeyName = key;
		}
	}
}
