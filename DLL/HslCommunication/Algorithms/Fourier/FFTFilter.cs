using System;
using System.Collections.Generic;
using System.Linq;

namespace HslCommunication.Algorithms.Fourier
{
	/// <summary>
	/// 一个基于傅立叶变换的一个滤波算法
	/// </summary>
	/// <remarks>
	/// 非常感谢来自北京的monk网友，提供了完整的解决方法。
	/// </remarks>
	public class FFTFilter
	{
		/// <summary>
		/// 对指定的数据进行填充，方便的进行傅立叶计算
		/// </summary>
		/// <typeparam name="T">数据的数据类型</typeparam>
		/// <param name="source">数据源</param>
		/// <param name="putLength">输出的长度</param>
		/// <returns>填充结果</returns>
		public static List<T> FillDataArray<T>(List<T> source, out int putLength)
		{
			int num = (int)(Math.Pow(2.0, Convert.ToString(source.Count, 2).Length) - (double)source.Count);
			num = (putLength = num / 2 + 1);
			T item = source[0];
			T item2 = source[source.Count - 1];
			for (int i = 0; i < num; i++)
			{
				source.Insert(0, item);
			}
			for (int j = 0; j < num; j++)
			{
				source.Add(item2);
			}
			return source;
		}

		/// <summary>
		/// 对指定的原始数据进行滤波，并返回成功的数据值
		/// </summary>
		/// <param name="source">数据源，数组的长度需要为2的n次方。</param>
		/// <param name="filter">滤波值：最大值为1，不能低于0，越接近1，滤波强度越强，也可能会导致失去真实信号，为0时没有滤波效果。</param>
		/// <returns>滤波后的数据值</returns>
		public static double[] FilterFFT(double[] source, double filter)
		{
			double[] array = new double[source.Length];
			int putLength;
			List<double> list = FillDataArray(new List<double>(source), out putLength);
			float[] array2 = Filter(list.ToArray(), filter);
			for (int i = 0; i < array.Length; i++)
			{
				array[i] = array2[i + putLength];
			}
			return array;
		}

		/// <summary>
		/// 对指定的原始数据进行滤波，并返回成功的数据值
		/// </summary>
		/// <param name="source">数据源，数组的长度需要为2的n次方。</param>
		/// <param name="filter">滤波值：最大值为1，不能低于0，越接近1，滤波强度越强，也可能会导致失去真实信号，为0时没有滤波效果。</param>
		/// <returns>滤波后的数据值</returns>
		public static float[] FilterFFT(float[] source, double filter)
		{
			float[] array = new float[source.Length];
			int putLength;
			List<float> list = FillDataArray(new List<float>(source), out putLength);
			float[] array2 = Filter(list.ToArray(), filter);
			for (int i = 0; i < array.Length; i++)
			{
				array[i] = array2[i + putLength];
			}
			return array;
		}

		/// <summary>
		/// 对指定的原始数据进行滤波，并返回成功的数据值
		/// </summary>
		/// <param name="source">数据源，数组的长度需要为2的n次方。</param>
		/// <param name="filter">滤波值：最大值为1，不能低于0，越接近1，滤波强度越强，也可能会导致失去真实信号，为0时没有滤波效果。</param>
		/// <returns>滤波后的数据值</returns>
		private static float[] Filter(float[] source, double filter)
		{
			return Filter(((IEnumerable<float>)source).Select((Func<float, double>)((float m) => m)).ToArray(), filter);
		}

		/// <summary>
		/// 对指定的原始数据进行滤波，并返回成功的数据值
		/// </summary>
		/// <param name="source">数据源，数组的长度需要为2的n次方。</param>
		/// <param name="filter">滤波值：最大值为1，不能低于0，越接近1，滤波强度越强，也可能会导致失去真实信号，为0时没有滤波效果。</param>
		/// <returns>滤波后的数据值</returns>
		private static float[] Filter(double[] source, double filter)
		{
			if (filter > 1.0)
			{
				filter = 1.0;
			}
			if (filter < 0.0)
			{
				filter = 0.0;
			}
			double[] array = new double[source.Length];
			double[] array2 = new double[source.Length];
			List<double> list = new List<double>();
			for (int i = 0; i < source.Length; i++)
			{
				array[i] = source[i];
				array2[i] = 0.0;
			}
			double[] array3 = FFTHelper.FFTValue(array, array2);
			int num = array3.Length;
			double num2 = array3.Max();
			for (int j = 0; j < array3.Length; j++)
			{
				if (array3[j] < num2 * filter)
				{
					array[j] = 0.0;
					array2[j] = 0.0;
				}
			}
			num = FFTHelper.IFFT(array, array2);
			for (int k = 0; k < num; k++)
			{
				if (array[k] >= 0.0)
				{
					list.Add(Math.Sqrt(array[k] * array[k] + array2[k] * array2[k]));
				}
				else
				{
					list.Add(0.0 - Math.Sqrt(array[k] * array[k] + array2[k] * array2[k]));
				}
			}
			return list.Select((double m) => (float)m).ToArray();
		}
	}
}
