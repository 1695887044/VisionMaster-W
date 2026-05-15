using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using HslCommunication.Core;

namespace HslCommunication.BasicFramework
{
	/// <summary>
	/// 系统框架支持的一些常用的动画特效
	/// </summary>
	public class SoftAnimation
	{
		/// <summary>
		/// 最小的时间片段
		/// </summary>
		private static int TimeFragment { get; set; } = 20;


		/// <summary>
		/// 调整控件背景色，采用了线性的颜色插补方式，实现了控件的背景色渐变，需要指定控件，颜色，以及渐变的时间
		/// </summary>
		/// <param name="control">控件</param>
		/// <param name="color">设置的颜色</param>
		/// <param name="time">时间</param>
		public static void BeginBackcolorAnimation(Control control, Color color, int time)
		{
			if (control.BackColor != color)
			{
				Func<Control, Color> func = (Control m) => m.BackColor;
				Action<Control, Color> action = delegate(Control m, Color n)
				{
					m.BackColor = n;
				};
				ThreadPool.QueueUserWorkItem(ThreadPoolColorAnimation, new object[5] { control, color, time, func, action });
			}
		}

		private static byte GetValue(byte Start, byte End, int i, int count)
		{
			if (Start == End)
			{
				return Start;
			}
			return (byte)((End - Start) * i / count + Start);
		}

		private static float GetValue(float Start, float End, int i, int count)
		{
			if (Start == End)
			{
				return Start;
			}
			return (End - Start) * (float)i / (float)count + Start;
		}

		private static void ThreadPoolColorAnimation(object obj)
		{
			object[] array = obj as object[];
			Control control = array[0] as Control;
			Color color = (Color)array[1];
			int num = (int)array[2];
			Func<Control, Color> func = (Func<Control, Color>)array[3];
			Action<Control, Color> setcolor = (Action<Control, Color>)array[4];
			int count = (num + TimeFragment - 1) / TimeFragment;
			Color color_old = func(control);
			try
			{
				int i;
				for (i = 0; i < count; i++)
				{
					control.Invoke((Action)delegate
					{
						setcolor(control, Color.FromArgb(GetValue(color_old.R, color.R, i, count), GetValue(color_old.G, color.G, i, count), GetValue(color_old.B, color.B, i, count)));
					});
					HslHelper.ThreadSleep(TimeFragment);
				}
				control?.Invoke((Action)delegate
				{
					setcolor(control, color);
				});
			}
			catch
			{
			}
		}

		private static void ThreadPoolFloatAnimation(object obj)
		{
			object[] array = obj as object[];
			Control control = array[0] as Control;
			lock (control)
			{
				float value = (float)array[1];
				int num = (int)array[2];
				Func<Control, float> func = (Func<Control, float>)array[3];
				Action<Control, float> setValue = (Action<Control, float>)array[4];
				int count = (num + TimeFragment - 1) / TimeFragment;
				float value_old = func(control);
				int i;
				for (i = 0; i < count; i++)
				{
					if (control.IsHandleCreated && !control.IsDisposed)
					{
						control.Invoke((Action)delegate
						{
							setValue(control, GetValue(value_old, value, i, count));
						});
						HslHelper.ThreadSleep(TimeFragment);
						continue;
					}
					return;
				}
				if (control.IsHandleCreated && !control.IsDisposed)
				{
					control.Invoke((Action)delegate
					{
						setValue(control, value);
					});
				}
			}
		}
	}
}
