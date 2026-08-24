namespace HslCommunication.Core
{
	/// <summary>
	/// 等待切割的地址信息
	/// </summary>
	public class CuttingAddress
	{
		/// <summary>
		/// 地址的切割类型
		/// </summary>
		public string DataType { get; set; }

		/// <summary>
		/// 等待切割的地址的临界点
		/// </summary>
		public int Address { get; set; }

		/// <summary>
		/// 地址的进制信息
		/// </summary>
		public int FromBase { get; set; } = 10;


		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		public CuttingAddress()
		{
		}

		/// <summary>
		/// 指定地址类型，临界地址，地址的进制来实例化一个对象
		/// </summary>
		/// <param name="type">地址类型</param>
		/// <param name="address">临界地址</param>
		/// <param name="fromBase">地址的进制</param>
		public CuttingAddress(string type, int address, int fromBase = 10)
		{
			DataType = type;
			Address = address;
			FromBase = fromBase;
		}
	}
}
