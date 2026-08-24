using System;

namespace HslCommunication.BasicFramework
{
	/// <summary>
	/// 一个内存队列缓存的类，数据类型为Int32
	/// </summary>
	public sealed class SoftCacheArrayInt : SoftCacheArrayBase
	{
		/// <summary>
		/// 数据的本身面貌
		/// </summary>
		private int[] DataArray = null;

		/// <summary>
		/// 实例化一个数据对象
		/// </summary>
		/// <param name="capacity"></param>
		/// <param name="defaultValue"></param>
		public SoftCacheArrayInt(int capacity, int defaultValue)
		{
			if (capacity < 10)
			{
				capacity = 10;
			}
			base.ArrayLength = capacity;
			DataArray = new int[capacity];
			DataBytes = new byte[capacity * 4];
			if (defaultValue != 0)
			{
				for (int i = 0; i < capacity; i++)
				{
					DataArray[i] = defaultValue;
				}
			}
		}

		/// <summary>
		/// 用于从保存的数据对象初始化的
		/// </summary>
		/// <param name="dataSave"></param>
		/// <exception cref="T:System.NullReferenceException"></exception>
		public override void LoadFromBytes(byte[] dataSave)
		{
			int num2 = (base.ArrayLength = dataSave.Length / 4);
			DataArray = new int[num2];
			DataBytes = new byte[num2 * 4];
			for (int i = 0; i < num2; i++)
			{
				DataArray[i] = BitConverter.ToInt32(dataSave, i * 4);
			}
		}

		/// <summary>
		/// 线程安全的添加数据
		/// </summary>
		/// <param name="value">值</param>
		public void AddValue(int value)
		{
			HybirdLock.Enter();
			for (int i = 0; i < base.ArrayLength - 1; i++)
			{
				DataArray[i] = DataArray[i + 1];
			}
			DataArray[base.ArrayLength - 1] = value;
			for (int j = 0; j < base.ArrayLength; j++)
			{
				BitConverter.GetBytes(DataArray[j]).CopyTo(DataBytes, 4 * j);
			}
			HybirdLock.Leave();
		}

		/// <summary>
		/// 安全的获取数组队列
		/// </summary>
		/// <returns></returns>
		public int[] GetIntArray()
		{
			int[] array = null;
			HybirdLock.Enter();
			array = new int[base.ArrayLength];
			for (int i = 0; i < base.ArrayLength; i++)
			{
				array[i] = DataArray[i];
			}
			HybirdLock.Leave();
			return array;
		}
	}
}
