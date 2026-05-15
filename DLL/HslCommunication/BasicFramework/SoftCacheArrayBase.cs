using HslCommunication.Core;

namespace HslCommunication.BasicFramework
{
	/// <summary>
	/// 内存队列的基类
	/// </summary>
	public abstract class SoftCacheArrayBase
	{
		/// <summary>
		/// 字节数据流
		/// </summary>
		protected byte[] DataBytes = null;

		/// <summary>
		/// 数据数组变动时的数据锁
		/// </summary>
		protected SimpleHybirdLock HybirdLock = new SimpleHybirdLock();

		/// <summary>
		/// 数据的长度
		/// </summary>
		public int ArrayLength { get; protected set; }

		/// <summary>
		/// 用于从保存的数据对象初始化的
		/// </summary>
		/// <param name="dataSave"></param>
		/// <exception cref="T:System.NullReferenceException"></exception>
		public virtual void LoadFromBytes(byte[] dataSave)
		{
		}

		/// <summary>
		/// 获取原本的数据字节
		/// </summary>
		/// <returns>字节数组</returns>
		public byte[] GetAllData()
		{
			byte[] array = new byte[DataBytes.Length];
			DataBytes.CopyTo(array, 0);
			return array;
		}
	}
}
