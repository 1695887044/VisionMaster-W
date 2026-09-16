using System;
using System.Windows;
using System.Windows.Controls;
using VM.Start.Common;

namespace VM.Start.UIDesign.Control
{
	// Token: 0x020000BB RID: 187
	public class UITextBox : TextBox
	{
		// Token: 0x060007F9 RID: 2041 RVA: 0x0002E38C File Offset: 0x0002C58C
		public UITextBox()
		{
			base.Loaded += this.UITextBox_Loaded;
			base.Text = "数据显示";
			Style style = (Style)base.FindResource("MahApps.Styles.TextBox");
			base.Style = style;
			base.TextChanged += this.UITextBox_TextChanged;
			base.IsReadOnly = true;
			base.MinWidth = 100.0;
		}

		// Token: 0x060007FA RID: 2042 RVA: 0x0002E400 File Offset: 0x0002C600
		private void UITextBox_TextChanged(object sender, TextChangedEventArgs e)
		{
			if (!string.IsNullOrEmpty(base.Text) && !base.IsReadOnly)
			{
				string text = base.Text;
				if (!string.IsNullOrEmpty(this.前缀))
				{
					text = text.Replace(this.前缀, "");
				}
				if (!string.IsNullOrEmpty(this.后缀))
				{
					text = text.Replace(this.后缀, "");
				}
				CommonMethods.SetObject(this.tb数据源, text);
			}
		}

		// Token: 0x060007FB RID: 2043 RVA: 0x00004B5E File Offset: 0x00002D5E
		private void UITextBox_Loaded(object sender, RoutedEventArgs e)
		{
			this.Refresh();
		}

		// Token: 0x17000285 RID: 645
		// (get) Token: 0x060007FC RID: 2044 RVA: 0x0002E480 File Offset: 0x0002C680
		// (set) Token: 0x060007FD RID: 2045 RVA: 0x00004B66 File Offset: 0x00002D66
		public string tb数据源
		{
			get
			{
				return (string)base.GetValue(UITextBox.tb数据源Property);
			}
			set
			{
				base.SetValue(UITextBox.tb数据源Property, value);
			}
		}

		// Token: 0x17000286 RID: 646
		// (get) Token: 0x060007FE RID: 2046 RVA: 0x0002E4A0 File Offset: 0x0002C6A0
		// (set) Token: 0x060007FF RID: 2047 RVA: 0x00004B74 File Offset: 0x00002D74
		public int 保留小数位数
		{
			get
			{
				return (int)base.GetValue(UITextBox.保留小数位数Property);
			}
			set
			{
				base.SetValue(UITextBox.保留小数位数Property, value);
			}
		}

		// Token: 0x17000287 RID: 647
		// (get) Token: 0x06000800 RID: 2048 RVA: 0x0002E4C0 File Offset: 0x0002C6C0
		// (set) Token: 0x06000801 RID: 2049 RVA: 0x00004B87 File Offset: 0x00002D87
		public string 前缀
		{
			get
			{
				return (string)base.GetValue(UITextBox.前缀Property);
			}
			set
			{
				base.SetValue(UITextBox.前缀Property, value);
			}
		}

		// Token: 0x17000288 RID: 648
		// (get) Token: 0x06000802 RID: 2050 RVA: 0x0002E4E0 File Offset: 0x0002C6E0
		// (set) Token: 0x06000803 RID: 2051 RVA: 0x00004B95 File Offset: 0x00002D95
		public string 后缀
		{
			get
			{
				return (string)base.GetValue(UITextBox.后缀Property);
			}
			set
			{
				base.SetValue(UITextBox.后缀Property, value);
			}
		}

		// Token: 0x06000804 RID: 2052 RVA: 0x0002E500 File Offset: 0x0002C700
		public void Refresh()
		{
			if (!string.IsNullOrEmpty(this.tb数据源))
			{
				object @object = CommonMethods.GetObject(this.tb数据源, this.保留小数位数);
				if (@object != null)
				{
					base.Text = this.前缀 + @object.ToString() + this.后缀;
				}
			}
		}

		// Token: 0x0400039E RID: 926
		public static readonly DependencyProperty tb数据源Property = DependencyProperty.Register("tb数据源", typeof(string), typeof(UITextBox), new PropertyMetadata(""));

		// Token: 0x0400039F RID: 927
		public static readonly DependencyProperty 保留小数位数Property = DependencyProperty.Register("保留小数位数", typeof(int), typeof(UITextBox), new PropertyMetadata(3));

		// Token: 0x040003A0 RID: 928
		public static readonly DependencyProperty 前缀Property = DependencyProperty.Register("前缀", typeof(string), typeof(UITextBox), new PropertyMetadata(""));

		// Token: 0x040003A1 RID: 929
		public static readonly DependencyProperty 后缀Property = DependencyProperty.Register("后缀", typeof(string), typeof(UITextBox), new PropertyMetadata(""));
	}
}
