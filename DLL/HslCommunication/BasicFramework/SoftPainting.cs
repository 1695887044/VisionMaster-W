using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;

namespace HslCommunication.BasicFramework
{
	/// <summary>
	/// 静态类，包含了几个常用的画图方法，获取字符串，绘制小三角等
	/// </summary>
	public static class SoftPainting
	{
		/// <summary>
		/// 获取一个直方图
		/// </summary>
		/// <param name="array">数据数组</param>
		/// <param name="width">宽度</param>
		/// <param name="height">高度</param>
		/// <param name="degree">刻度划分等级</param>
		/// <param name="lineColor">线条颜色</param>
		/// <returns></returns>
		public static Bitmap GetGraphicFromArray(int[] array, int width, int height, int degree, Color lineColor)
		{
			if (width < 10 && height < 10)
			{
				throw new ArgumentException("长宽不能小于等于10");
			}
			int max = array.Max();
			int min = 0;
			int num = array.Length;
			StringFormat stringFormat = new StringFormat();
			stringFormat.Alignment = StringAlignment.Far;
			Pen pen = new Pen(Color.LightGray, 1f);
			pen.DashStyle = DashStyle.Custom;
			pen.DashPattern = new float[2] { 5f, 5f };
			Font font = new Font("宋体", 9f);
			Bitmap bitmap = new Bitmap(width, height);
			Graphics graphics = Graphics.FromImage(bitmap);
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
			graphics.Clear(Color.White);
			int num2 = 60;
			int num3 = 8;
			int num4 = 8;
			int num5 = 8;
			int num6 = width - num2 - num3;
			int num7 = height - num4 - num5;
			Rectangle rectangle = new Rectangle(num2 - 1, num4 - 1, num6 + 1, num7 + 1);
			graphics.DrawLine(Pens.Gray, num2 - 1, num4, num2 + num6 + 1, num4);
			graphics.DrawLine(Pens.Gray, num2 - 1, num4 + num7 + 1, num2 + num6 + 1, num4 + num7 + 1);
			graphics.DrawLine(Pens.Gray, num2 - 1, num4 - 1, num2 - 1, num4 + num7 + 1);
			PaintCoordinateDivide(graphics, Pens.DimGray, pen, font, Brushes.DimGray, stringFormat, degree, max, min, width, height, num2, num3, num4, num5);
			PointF[] array2 = new PointF[array.Length];
			for (int i = 0; i < array.Length; i++)
			{
				array2[i].X = (float)num6 * 1f / (float)(array.Length - 1) * (float)i + (float)num2;
				array2[i].Y = ComputePaintLocationY(max, min, num7, array[i]) + (float)num4 + 1f;
			}
			Pen pen2 = new Pen(lineColor);
			graphics.DrawLines(pen2, array2);
			pen2.Dispose();
			pen.Dispose();
			font.Dispose();
			stringFormat.Dispose();
			graphics.Dispose();
			return bitmap;
		}

		/// <summary>
		/// 计算绘图时的相对偏移值
		/// </summary>
		/// <param name="max">0-100分的最大值，就是指准备绘制的最大值</param>
		/// <param name="min">0-100分的最小值，就是指准备绘制的最小值</param>
		/// <param name="height">实际绘图区域的高度</param>
		/// <param name="value">需要绘制数据的当前值</param>
		/// <returns>相对于0的位置，还需要增加上面的偏值</returns>
		public static float ComputePaintLocationY(int max, int min, int height, int value)
		{
			return (float)height - (float)(value - min) * 1f / (float)(max - min) * (float)height;
		}

		/// <summary>
		/// 计算绘图时的相对偏移值
		/// </summary>
		/// <param name="max">0-100分的最大值，就是指准备绘制的最大值</param>
		/// <param name="min">0-100分的最小值，就是指准备绘制的最小值</param>
		/// <param name="height">实际绘图区域的高度</param>
		/// <param name="value">需要绘制数据的当前值</param>
		/// <returns>相对于0的位置，还需要增加上面的偏值</returns>
		public static float ComputePaintLocationY(float max, float min, int height, float value)
		{
			return (float)height - (value - min) / (max - min) * (float)height;
		}

		/// <summary>
		/// 绘制坐标系中的刻度线
		/// </summary>
		/// <param name="g"></param>
		/// <param name="penLine"></param>
		/// <param name="penDash"></param>
		/// <param name="font"></param>
		/// <param name="brush"></param>
		/// <param name="sf"></param>
		/// <param name="degree"></param>
		/// <param name="max"></param>
		/// <param name="min"></param>
		/// <param name="width"></param>
		/// <param name="height"></param>
		/// <param name="left"></param>
		/// <param name="right"></param>
		/// <param name="up"></param>
		/// <param name="down"></param>
		public static void PaintCoordinateDivide(Graphics g, Pen penLine, Pen penDash, Font font, Brush brush, StringFormat sf, int degree, int max, int min, int width, int height, int left = 60, int right = 8, int up = 8, int down = 8)
		{
			for (int i = 0; i <= degree; i++)
			{
				int value = (max - min) * i / degree + min;
				int num = (int)ComputePaintLocationY(max, min, height - up - down, value) + up + 1;
				g.DrawLine(penLine, left - 1, num, left - 4, num);
				if (i != 0)
				{
					g.DrawLine(penDash, left, num, width - right, num);
				}
				g.DrawString(value.ToString(), font, brush, new Rectangle(-5, num - font.Height / 2, left, font.Height), sf);
			}
		}

		/// <summary>
		/// 根据指定的方向绘制一个箭头
		/// </summary>
		/// <param name="g"></param>
		/// <param name="brush"></param>
		/// <param name="point"></param>
		/// <param name="size"></param>
		/// <param name="direction"></param>
		public static void PaintTriangle(Graphics g, Brush brush, Point point, int size, GraphDirection direction)
		{
			Point[] array = new Point[4];
			switch (direction)
			{
			case GraphDirection.Ledtward:
				array[0] = new Point(point.X, point.Y - size);
				array[1] = new Point(point.X, point.Y + size);
				array[2] = new Point(point.X - 2 * size, point.Y);
				break;
			case GraphDirection.Rightward:
				array[0] = new Point(point.X, point.Y - size);
				array[1] = new Point(point.X, point.Y + size);
				array[2] = new Point(point.X + 2 * size, point.Y);
				break;
			case GraphDirection.Upward:
				array[0] = new Point(point.X - size, point.Y);
				array[1] = new Point(point.X + size, point.Y);
				array[2] = new Point(point.X, point.Y - 2 * size);
				break;
			default:
				array[0] = new Point(point.X - size, point.Y);
				array[1] = new Point(point.X + size, point.Y);
				array[2] = new Point(point.X, point.Y + 2 * size);
				break;
			}
			array[3] = array[0];
			g.FillPolygon(brush, array);
		}

		/// <summary>
		/// 根据数据生成一个可视化的图形
		/// </summary>
		/// <param name="array">数据集合</param>
		/// <param name="width">需要绘制图形的宽度</param>
		/// <param name="height">需要绘制图形的高度</param>
		/// <param name="graphic">指定绘制成什么样子的图形</param>
		/// <returns>返回一个bitmap对象</returns>
		public static Bitmap GetGraphicFromArray(Paintdata[] array, int width, int height, GraphicRender graphic)
		{
			if (width < 10 && height < 10)
			{
				throw new ArgumentException("长宽不能小于等于10");
			}
			array.Max((Paintdata m) => m.Count);
			Action<Paintdata[], GraphicRender, Graphics> action = delegate
			{
			};
			return null;
		}
	}
}
