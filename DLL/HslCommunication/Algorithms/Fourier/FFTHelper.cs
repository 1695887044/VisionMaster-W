using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;

namespace HslCommunication.Algorithms.Fourier
{
	/// <summary>
	/// 离散傅氏变换的快速算法，处理的信号，适合单周期信号数为2的N次方个，支持变换及逆变换
	/// </summary>
	public class FFTHelper
	{
		/// <summary>
		///
		/// </summary>
		/// <param name="xreal"></param>
		/// <param name="ximag"></param>
		/// <param name="n"></param>
		private static void bitrp(double[] xreal, double[] ximag, int n)
		{
			int num = 1;
			int num2 = 0;
			while (num < n)
			{
				num2++;
				num *= 2;
			}
			for (num = 0; num < n; num++)
			{
				int num3 = num;
				int num4 = 0;
				for (int i = 0; i < num2; i++)
				{
					num4 = num4 * 2 + num3 % 2;
					num3 /= 2;
				}
				if (num4 > num)
				{
					double num5 = xreal[num];
					xreal[num] = xreal[num4];
					xreal[num4] = num5;
					num5 = ximag[num];
					ximag[num] = ximag[num4];
					ximag[num4] = num5;
				}
			}
		}

		/// <summary>
		/// 快速傅立叶变换
		/// </summary>
		/// <param name="xreal">实数部分</param>
		/// <returns>变换后的数组值</returns>
		public static double[] FFT(double[] xreal)
		{
			return FFTValue(xreal, new double[xreal.Length]);
		}

		/// <summary>
		/// 获取FFT变换后的显示图形，需要指定图形的相关参数
		/// </summary>
		/// <param name="xreal">实数部分的值</param>
		/// <param name="width">图形的宽度</param>
		/// <param name="heigh">图形的高度</param>
		/// <param name="lineColor">线条颜色</param>
		/// <param name="isSqrtDouble">是否开两次根，显示的噪点信息会更新明显</param>
		/// <returns>等待呈现的图形</returns>
		/// <remarks>
		/// <note type="warning">.net standrard2.0 下不支持。</note>
		/// </remarks>
		public static Bitmap GetFFTImage(double[] xreal, int width, int heigh, Color lineColor, bool isSqrtDouble = false)
		{
			double[] ximag = new double[xreal.Length];
			double[] array = FFTValue(xreal, ximag, isSqrtDouble);
			Bitmap bitmap = new Bitmap(width, heigh);
			Graphics graphics = Graphics.FromImage(bitmap);
			graphics.SmoothingMode = SmoothingMode.HighQuality;
			graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
			graphics.Clear(Color.White);
			Pen pen = new Pen(Color.DimGray, 1f);
			Pen pen2 = new Pen(Color.LightGray, 1f);
			Pen pen3 = new Pen(lineColor, 1f);
			pen2.DashPattern = new float[2] { 5f, 5f };
			pen2.DashStyle = DashStyle.Custom;
			Font defaultFont = SystemFonts.DefaultFont;
			StringFormat stringFormat = new StringFormat();
			stringFormat.Alignment = StringAlignment.Far;
			stringFormat.LineAlignment = StringAlignment.Center;
			StringFormat stringFormat2 = new StringFormat();
			stringFormat2.LineAlignment = StringAlignment.Center;
			stringFormat2.Alignment = StringAlignment.Center;
			int num = 20;
			int num2 = 49;
			int num3 = 49;
			int num4 = 30;
			int num5 = 9;
			float num6 = heigh - num - num4;
			float num7 = width - num2 - num3;
			if (array.Length > 1)
			{
				double num8 = array.Max();
				double num9 = array.Min();
				num8 = ((num8 - num9 > 1.0) ? num8 : (num9 + 1.0));
				double num10 = num8 - num9;
				List<float> list = new List<float>();
				if (array.Length >= 2)
				{
					if (array[0] > array[1])
					{
						list.Add(0f);
					}
					for (int i = 1; i < array.Length - 2; i++)
					{
						if (array[i - 1] < array[i] && array[i] > array[i + 1])
						{
							list.Add(i);
						}
					}
					if (array[array.Length - 1] > array[array.Length - 2])
					{
						list.Add(array.Length - 1);
					}
				}
				for (int j = 0; j < num5; j++)
				{
					graphics.DrawString(layoutRectangle: new RectangleF(-10f, (float)j / (float)(num5 - 1) * num6, (float)num2 + 8f, 20f), s: ((double)(num5 - 1 - j) * num10 / (double)(num5 - 1) + num9).ToString("F1"), font: defaultFont, brush: Brushes.Black, format: stringFormat);
					graphics.DrawLine(pen2, num2 - 3, num6 * (float)j / (float)(num5 - 1) + (float)num, width - num3, num6 * (float)j / (float)(num5 - 1) + (float)num);
				}
				float num11 = num7 / (float)array.Length;
				for (int k = 0; k < list.Count; k++)
				{
					if (array[(int)list[k]] * 200.0 / num8 > 1.0)
					{
						graphics.DrawLine(pen2, list[k] * num11 + (float)num2 + 1f, num, list[k] * num11 + (float)num2 + 1f, heigh - num4);
						graphics.DrawString(layoutRectangle: new RectangleF(list[k] * num11 + (float)num2 + 1f - 40f, heigh - num4 + 1, 80f, 20f), s: list[k].ToString(), font: defaultFont, brush: Brushes.DeepPink, format: stringFormat2);
					}
				}
				for (int l = 0; l < array.Length; l++)
				{
					PointF pt = default(PointF);
					pt.X = (float)l * num11 + (float)num2 + 1f;
					pt.Y = (float)((double)num6 - (array[l] - num9) * (double)num6 / num10 + (double)num);
					PointF pt2 = default(PointF);
					pt2.X = (float)l * num11 + (float)num2 + 1f;
					pt2.Y = (float)((double)num6 - (num9 - num9) * (double)num6 / num10 + (double)num);
					graphics.DrawLine(Pens.Tomato, pt, pt2);
				}
			}
			else
			{
				double num12 = 100.0;
				double num13 = 0.0;
				double num14 = num12 - num13;
				for (int m = 0; m < num5; m++)
				{
					graphics.DrawString(layoutRectangle: new RectangleF(-10f, (float)m / (float)(num5 - 1) * num6, (float)num2 + 8f, 20f), s: ((double)(num5 - 1 - m) * num14 / (double)(num5 - 1) + num13).ToString("F1"), font: defaultFont, brush: Brushes.Black, format: stringFormat);
					graphics.DrawLine(pen2, num2 - 3, num6 * (float)m / (float)(num5 - 1) + (float)num, width - num3, num6 * (float)m / (float)(num5 - 1) + (float)num);
				}
			}
			pen2.Dispose();
			pen.Dispose();
			pen3.Dispose();
			defaultFont.Dispose();
			stringFormat.Dispose();
			stringFormat2.Dispose();
			graphics.Dispose();
			return bitmap;
		}

		/// <summary>
		/// 快速傅立叶变换
		/// </summary>
		/// <param name="xreal">实数部分，数组长度最好为2的n次方</param>
		/// <param name="ximag">虚数部分，数组长度最好为2的n次方</param>
		/// <param name="isSqrtDouble">是否开两次根，显示的噪点信息会更新明显</param>
		/// <returns>变换后的数组值</returns>
		public static double[] FFTValue(double[] xreal, double[] ximag, bool isSqrtDouble = false)
		{
			int num;
			for (num = 2; num <= xreal.Length; num *= 2)
			{
			}
			num /= 2;
			double[] array = new double[num / 2];
			double[] array2 = new double[num / 2];
			bitrp(xreal, ximag, num);
			double num2 = Math.PI * -2.0 / (double)num;
			double num3 = Math.Cos(num2);
			double num4 = Math.Sin(num2);
			array[0] = 1.0;
			array2[0] = 0.0;
			for (int i = 1; i < num / 2; i++)
			{
				array[i] = array[i - 1] * num3 - array2[i - 1] * num4;
				array2[i] = array[i - 1] * num4 + array2[i - 1] * num3;
			}
			for (int num5 = 2; num5 <= num; num5 *= 2)
			{
				for (int j = 0; j < num; j += num5)
				{
					for (int i = 0; i < num5 / 2; i++)
					{
						int num6 = j + i;
						int num7 = num6 + num5 / 2;
						int num8 = num * i / num5;
						num3 = array[num8] * xreal[num7] - array2[num8] * ximag[num7];
						num4 = array[num8] * ximag[num7] + array2[num8] * xreal[num7];
						double num9 = xreal[num6];
						double num10 = ximag[num6];
						xreal[num6] = num9 + num3;
						ximag[num6] = num10 + num4;
						xreal[num7] = num9 - num3;
						ximag[num7] = num10 - num4;
					}
				}
			}
			double[] array3 = new double[num];
			for (int k = 0; k < array3.Length; k++)
			{
				array3[k] = Math.Sqrt(Math.Pow(xreal[k], 2.0) + Math.Pow(ximag[k], 2.0));
				if (isSqrtDouble)
				{
					array3[k] = Math.Sqrt(array3[k]);
				}
			}
			return array3;
		}

		/// <summary>
		/// 快速傅立叶变换
		/// </summary>
		/// <param name="xreal">实数部分，数组长度最好为2的n次方</param>
		/// <param name="ximag">虚数部分，数组长度最好为2的n次方</param>
		/// <returns>变换后的数组值</returns>
		public static int FFT(double[] xreal, double[] ximag)
		{
			return FFTValue(xreal, ximag).Length;
		}

		/// <summary>
		/// 快速傅立叶变换
		/// </summary>
		/// <param name="xreal">实数部分，数组长度最好为2的n次方</param>
		/// <param name="ximag">虚数部分，数组长度最好为2的n次方</param>
		/// <returns>变换后的数组值</returns>
		public static int FFT(float[] xreal, float[] ximag)
		{
			return FFT(((IEnumerable<float>)xreal).Select((Func<float, double>)((float m) => m)).ToArray(), ((IEnumerable<float>)ximag).Select((Func<float, double>)((float m) => m)).ToArray());
		}

		/// <summary>
		/// 快速傅立叶变换的逆变换
		/// </summary>
		/// <param name="xreal">实数部分，数组长度最好为2的n次方</param>
		/// <param name="ximag">虚数部分，数组长度最好为2的n次方</param>
		/// <returns>2的多少次方</returns>
		public static int IFFT(float[] xreal, float[] ximag)
		{
			return IFFT(((IEnumerable<float>)xreal).Select((Func<float, double>)((float m) => m)).ToArray(), ((IEnumerable<float>)ximag).Select((Func<float, double>)((float m) => m)).ToArray());
		}

		/// <summary>
		/// 快速傅立叶变换的逆变换
		/// </summary>
		/// <param name="xreal">实数部分，数组长度最好为2的n次方</param>
		/// <param name="ximag">虚数部分，数组长度最好为2的n次方</param>
		/// <returns>2的多少次方</returns>
		public static int IFFT(double[] xreal, double[] ximag)
		{
			int num;
			for (num = 2; num <= xreal.Length; num *= 2)
			{
			}
			num /= 2;
			double[] array = new double[num / 2];
			double[] array2 = new double[num / 2];
			bitrp(xreal, ximag, num);
			double num2 = Math.PI * 2.0 / (double)num;
			double num3 = Math.Cos(num2);
			double num4 = Math.Sin(num2);
			array[0] = 1.0;
			array2[0] = 0.0;
			for (int i = 1; i < num / 2; i++)
			{
				array[i] = array[i - 1] * num3 - array2[i - 1] * num4;
				array2[i] = array[i - 1] * num4 + array2[i - 1] * num3;
			}
			for (int num5 = 2; num5 <= num; num5 *= 2)
			{
				for (int j = 0; j < num; j += num5)
				{
					for (int i = 0; i < num5 / 2; i++)
					{
						int num6 = j + i;
						int num7 = num6 + num5 / 2;
						int num8 = num * i / num5;
						num3 = array[num8] * xreal[num7] - array2[num8] * ximag[num7];
						num4 = array[num8] * ximag[num7] + array2[num8] * xreal[num7];
						double num9 = xreal[num6];
						double num10 = ximag[num6];
						xreal[num6] = num9 + num3;
						ximag[num6] = num10 + num4;
						xreal[num7] = num9 - num3;
						ximag[num7] = num10 - num4;
					}
				}
			}
			for (int i = 0; i < num; i++)
			{
				xreal[i] /= num;
				ximag[i] /= num;
			}
			return num;
		}
	}
}
