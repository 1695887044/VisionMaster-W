using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HslCommunication.BasicFramework
{
	/// <summary>
	/// 一个用于消息弹出显示的类
	/// </summary>
	public class FormPopup : Form
	{
		private static List<FormPopup> FormsPopup = new List<FormPopup>();

		private Timer time = null;

		private const int AW_HOR_POSITIVE = 1;

		private const int AW_HOR_NEGATIVE = 2;

		private const int AW_VER_POSITIVE = 4;

		private const int AW_VER_NEGATIVE = 8;

		private const int AW_CENTER = 16;

		private const int AW_HIDE = 65536;

		private const int AW_ACTIVE = 131072;

		private const int AW_SLIDE = 262144;

		private const int AW_BLEND = 524288;

		/// <summary>
		/// Required designer variable.
		/// </summary>
		private IContainer components = null;

		private Label label2;

		private Label label1;

		private string InfoText { get; set; } = "This is a test message";


		private Color InfoColor { get; set; } = Color.DimGray;


		private int InfoExistTime { get; set; } = -1;


		/// <summary>
		/// 新增一个显示的弹出窗口
		/// </summary>
		/// <param name="form"></param>
		private static void AddNewForm(FormPopup form)
		{
			try
			{
				foreach (FormPopup item in FormsPopup)
				{
					item.LocationUpMove();
				}
				FormsPopup.Add(form);
			}
			catch (Exception ex)
			{
				Console.WriteLine(SoftBasic.GetExceptionMessage(ex));
			}
		}

		/// <summary>
		/// 重置所有弹出窗口的位置
		/// </summary>
		private static void ResetLocation()
		{
			try
			{
				for (int i = 0; i < FormsPopup.Count; i++)
				{
					FormsPopup[i].LocationUpMove(FormsPopup.Count - 1 - i);
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine(SoftBasic.GetExceptionMessage(ex));
			}
		}

		/// <summary>
		/// 实例化一个窗口信息弹出的对象
		/// </summary>
		public FormPopup()
		{
			InitializeComponent();
		}

		/// <summary>
		/// 实例化一个窗口信息弹出的对象
		/// </summary>
		/// <param name="infotext">需要显示的文本</param>
		public FormPopup(string infotext)
		{
			InitializeComponent();
			InfoText = infotext;
		}

		/// <summary>
		/// 实例化一个窗口信息弹出的对象
		/// </summary>
		/// <param name="infotext">需要显示的文本</param>
		/// <param name="infocolor">文本的颜色</param>
		public FormPopup(string infotext, Color infocolor)
		{
			InitializeComponent();
			InfoText = infotext;
			InfoColor = infocolor;
		}

		/// <summary>
		/// 实例化一个窗口信息弹出的对象
		/// </summary>
		/// <param name="infotext">需要显示的文本</param>
		/// <param name="infocolor">文本的颜色</param>
		/// <param name="existTime">指定窗口多少时间后消失，单位毫秒</param>
		public FormPopup(string infotext, Color infocolor, int existTime)
		{
			InitializeComponent();
			InfoText = infotext;
			InfoColor = infocolor;
			InfoExistTime = existTime;
		}

		private void FormPopup_Load(object sender, EventArgs e)
		{
			label1.Text = InfoText;
			label1.ForeColor = InfoColor;
			label2.Text = StringResources.Language.Close;
			AddNewForm(this);
			int num = Screen.PrimaryScreen.WorkingArea.Right - base.Width;
			int num2 = Screen.PrimaryScreen.WorkingArea.Bottom - base.Height;
			base.Location = new Point(num, num2);
			AnimateWindow(base.Handle, 1000, 262152);
			base.TopMost = true;
			if (InfoExistTime <= 100)
			{
				return;
			}
			time = new Timer();
			time.Interval = InfoExistTime;
			time.Tick += delegate
			{
				if (base.IsHandleCreated)
				{
					time.Dispose();
					AnimateWindow(base.Handle, 1000, 589824);
					Close();
				}
			};
			time.Start();
		}

		/// <summary>
		/// 窗体的位置进行向上调整
		/// </summary>
		public void LocationUpMove()
		{
			base.Location = new Point(base.Location.X, base.Location.Y - base.Height);
		}

		/// <summary>
		/// 窗体的位置进行向上调整
		/// </summary>
		public void LocationUpMove(int index)
		{
			base.Location = new Point(base.Location.X, Screen.PrimaryScreen.WorkingArea.Bottom - base.Height - index * base.Height);
		}

		private void FormPopup_Closing(object sender, FormClosingEventArgs e)
		{
			try
			{
				time.Enabled = false;
				FormsPopup.Remove(this);
				ResetLocation();
			}
			catch (Exception ex)
			{
				Console.WriteLine(SoftBasic.GetExceptionMessage(ex));
			}
		}

		[DllImport("user32")]
		private static extern bool AnimateWindow(IntPtr hwnd, int dwTime, int dwFlags);

		private void FormPopup_Paint(object sender, PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			graphics.FillRectangle(Brushes.SkyBlue, new Rectangle(0, 0, base.Width - 1, 30));
			StringFormat format = new StringFormat
			{
				Alignment = StringAlignment.Near,
				LineAlignment = StringAlignment.Center
			};
			graphics.DrawString(StringResources.Language.MessageTip, label2.Font, Brushes.DimGray, new Rectangle(5, 0, base.Width - 1, 30), format);
			graphics.DrawRectangle(Pens.DimGray, 0, 0, base.Width - 1, base.Height - 1);
		}

		private void label2_Click(object sender, EventArgs e)
		{
			Close();
		}

		private void label2_MouseEnter(object sender, EventArgs e)
		{
			SoftAnimation.BeginBackcolorAnimation(label2, Color.Tomato, 100);
		}

		private void label2_MouseLeave(object sender, EventArgs e)
		{
			SoftAnimation.BeginBackcolorAnimation(label2, Color.MistyRose, 100);
		}

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing && components != null)
			{
				components.Dispose();
			}
			base.Dispose(disposing);
		}

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			label2 = new System.Windows.Forms.Label();
			label1 = new System.Windows.Forms.Label();
			SuspendLayout();
			label2.BackColor = System.Drawing.Color.MistyRose;
			label2.Font = new System.Drawing.Font("微软雅黑", 9f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 134);
			label2.Location = new System.Drawing.Point(287, 4);
			label2.Name = "label2";
			label2.Size = new System.Drawing.Size(41, 21);
			label2.TabIndex = 1;
			label2.Text = "关闭";
			label2.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
			label2.Click += new System.EventHandler(label2_Click);
			label2.MouseEnter += new System.EventHandler(label2_MouseEnter);
			label2.MouseLeave += new System.EventHandler(label2_MouseLeave);
			label1.Font = new System.Drawing.Font("微软雅黑", 10.5f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 134);
			label1.ForeColor = System.Drawing.Color.DimGray;
			label1.Location = new System.Drawing.Point(12, 30);
			label1.Name = "label1";
			label1.Size = new System.Drawing.Size(309, 119);
			label1.TabIndex = 2;
			label1.Text = "这是一条测试消息";
			label1.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
			base.AutoScaleDimensions = new System.Drawing.SizeF(10f, 21f);
			base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			base.ClientSize = new System.Drawing.Size(333, 163);
			base.Controls.Add(label1);
			base.Controls.Add(label2);
			Font = new System.Drawing.Font("微软雅黑", 12f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 134);
			base.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
			base.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
			base.MaximizeBox = false;
			MaximumSize = new System.Drawing.Size(333, 163);
			base.MinimizeBox = false;
			MinimumSize = new System.Drawing.Size(333, 163);
			base.Name = "FormPopup";
			base.ShowIcon = false;
			base.ShowInTaskbar = false;
			Text = "消息";
			base.FormClosing += new System.Windows.Forms.FormClosingEventHandler(FormPopup_Closing);
			base.Load += new System.EventHandler(FormPopup_Load);
			base.Paint += new System.Windows.Forms.PaintEventHandler(FormPopup_Paint);
			ResumeLayout(false);
		}
	}
}
