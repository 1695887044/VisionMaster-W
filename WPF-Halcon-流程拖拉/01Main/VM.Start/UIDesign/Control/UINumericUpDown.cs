using System;
using System.Windows;
using MahApps.Metro.Controls;
using VM.Start.Common;

namespace VM.Start.UIDesign.Control
{
	// Token: 0x020000B9 RID: 185
	public class UINumericUpDown : NumericUpDown
	{
		// Token: 0x060007EE RID: 2030 RVA: 0x0002E210 File Offset: 0x0002C410
		public UINumericUpDown()
		{
			base.Loaded += this.UITextBox_Loaded;
			base.Value = new double?(0.0);
			base.ValueChanged += this.UINumericUpDown_ValueChanged;
			base.MinWidth = 100.0;
		}

		// Token: 0x060007EF RID: 2031 RVA: 0x0002E26C File Offset: 0x0002C46C
		private void UINumericUpDown_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double?> e)
		{
			CommonMethods.SetObject(this.num数据源, base.Value.ToString());
		}

		// Token: 0x060007F0 RID: 2032 RVA: 0x00004B22 File Offset: 0x00002D22
		private void UITextBox_Loaded(object sender, RoutedEventArgs e)
		{
			this.Refresh();
		}

		// Token: 0x17000283 RID: 643
		// (get) Token: 0x060007F1 RID: 2033 RVA: 0x0002E298 File Offset: 0x0002C498
		// (set) Token: 0x060007F2 RID: 2034 RVA: 0x00004B2A File Offset: 0x00002D2A
		public string num数据源
		{
			get
			{
				return (string)base.GetValue(UINumericUpDown.num数据源Property);
			}
			set
			{
				base.SetValue(UINumericUpDown.num数据源Property, value);
			}
		}

		// Token: 0x17000284 RID: 644
		// (get) Token: 0x060007F3 RID: 2035 RVA: 0x0002E2B8 File Offset: 0x0002C4B8
		// (set) Token: 0x060007F4 RID: 2036 RVA: 0x00004B38 File Offset: 0x00002D38
		public int 保留小数位数
		{
			get
			{
				return (int)base.GetValue(UINumericUpDown.保留小数位数Property);
			}
			set
			{
				base.SetValue(UINumericUpDown.保留小数位数Property, value);
			}
		}

		// Token: 0x060007F5 RID: 2037 RVA: 0x0002E2D8 File Offset: 0x0002C4D8
		public void Refresh()
		{
			if (!string.IsNullOrEmpty(this.num数据源))
			{
				object @object = CommonMethods.GetObject(this.num数据源, this.保留小数位数);
				if (@object != null)
				{
					base.Value = new double?(Convert.ToDouble(@object));
				}
			}
		}

		// Token: 0x0400039C RID: 924
		public static readonly DependencyProperty num数据源Property = DependencyProperty.Register("num数据源", typeof(string), typeof(UINumericUpDown), new PropertyMetadata(""));

		// Token: 0x0400039D RID: 925
		public static readonly DependencyProperty 保留小数位数Property = DependencyProperty.Register("保留小数位数", typeof(int), typeof(UINumericUpDown), new PropertyMetadata(3));
	}
}
