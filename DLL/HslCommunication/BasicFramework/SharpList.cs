using System;
using System.Collections.Generic;

namespace HslCommunication.BasicFramework
{
	/// <summary>
	/// 一个高效的数组管理类，用于高效控制固定长度的数组实现<br />
	/// An efficient array management class for efficient control of fixed-length array implementations
	/// </summary>
	/// <typeparam name="T">泛型类型</typeparam>
	public class SharpList<T>
	{
		private T[] array;

		private int capacity = 2048;

		private int count = 0;

		private int lastIndex = 0;

		private object objLock = new object();

		/// <summary>
		/// 获取数组设定时候的固定数量<br />
		/// Gets the fixed number of arrays when they are set
		/// </summary>
		public int Count => count;

		/// <summary>
		/// 获取或设置指定索引的位置的数据<br />
		/// Gets or sets the data at the specified index
		/// </summary>
		/// <param name="index">索引位置</param>
		/// <returns>当前索引的数据值</returns>
		public T this[int index]
		{
			get
			{
				if (index < 0)
				{
					throw new IndexOutOfRangeException("Index must larger than zero");
				}
				if (index >= count)
				{
					throw new IndexOutOfRangeException("Index must smaller than array length");
				}
				T val = default(T);
				lock (objLock)
				{
					if (lastIndex < count)
					{
						return array[index];
					}
					return array[index + lastIndex - count];
				}
			}
			set
			{
				if (index < 0)
				{
					throw new IndexOutOfRangeException("Index must larger than zero");
				}
				if (index >= count)
				{
					throw new IndexOutOfRangeException("Index must smaller than array length");
				}
				lock (objLock)
				{
					if (lastIndex < count)
					{
						array[index] = value;
					}
					else
					{
						array[index + lastIndex - count] = value;
					}
				}
			}
		}

		/// <summary>
		/// 实例化一个对象，需要指定数组的最大长度信息，以及是否从最后一个添加，之前的值为类型默认值<br />
		/// To instantiate an object, you need to specify the maximum length of the array and whether to add it from the last value before the type default
		/// </summary>
		/// <param name="count">数组的固定长度信息</param>
		/// <param name="appendLast">是否从最后一个索引开始添加</param>
		public SharpList(int count, bool appendLast = false)
		{
			if (count > 65535)
			{
				capacity = 8192;
			}
			else if (count > 8192)
			{
				capacity = 4096;
			}
			array = new T[capacity + count];
			this.count = count;
			if (appendLast)
			{
				lastIndex = count;
			}
		}

		private void addItem(T value)
		{
			if (lastIndex >= capacity + count)
			{
				T[] destinationArray = new T[capacity + count];
				Array.Copy(array, capacity, destinationArray, 0, count);
				array = destinationArray;
				lastIndex = count;
			}
			array[lastIndex++] = value;
		}

		/// <summary>
		/// 新增一个数据值<br />
		/// Add a data value
		/// </summary>
		/// <param name="value">数据值</param>
		public void Add(T value)
		{
			lock (objLock)
			{
				addItem(value);
			}
		}

		/// <summary>
		/// 将一个可以遍历的数据集合一次性批量的添加到当前的数据缓存中<br />
		/// Adds a traversable data set to the current data cache in one batch
		/// </summary>
		/// <param name="values">批量数据信息</param>
		public void Add(IEnumerable<T> values)
		{
			lock (objLock)
			{
				foreach (T value in values)
				{
					addItem(value);
				}
			}
		}

		/// <summary>
		/// 获取数据的浅拷贝数组对象<br />
		/// Get array value of data
		/// </summary>
		/// <returns>数组值</returns>
		public T[] ToArray()
		{
			T[] array = null;
			lock (objLock)
			{
				if (lastIndex < count)
				{
					array = new T[lastIndex];
					Array.Copy(this.array, 0, array, 0, lastIndex);
				}
				else
				{
					array = new T[count];
					Array.Copy(this.array, lastIndex - count, array, 0, count);
				}
			}
			return array;
		}

		/// <summary>
		/// 获取最后一个值，如果从来没有添加过，则引发异常<br />
		/// Gets the last value and throws an exception if it has never been added
		/// </summary>
		/// <returns>值信息</returns>
		public T LastValue()
		{
			T result = default(T);
			lock (objLock)
			{
				if (lastIndex - 1 < count + capacity)
				{
					result = array[lastIndex - 1];
					return result;
				}
			}
			return result;
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"SharpList<{typeof(T)}>[{capacity}]";
		}
	}
}
