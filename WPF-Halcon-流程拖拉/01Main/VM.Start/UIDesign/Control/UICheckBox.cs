using System;
using System.Windows;
using System.Windows.Controls;
using VM.Start.Common;

namespace VM.Start.UIDesign.Control
{
	// Token: 0x020000B8 RID: 184
	public class UICheckBox : CheckBox
	{
		// Token: 0x060007E7 RID: 2023 RVA: 0x0002E134 File Offset: 0x0002C334
		public UICheckBox()
		{
			base.Loaded += this.UICheckBox_Loaded;
			base.Content = "复选框";
			Style style = (Style)base.FindResource("MahApps.Styles.CheckBox.Win10");
			base.Style = style;
		}

		// Token: 0x060007E8 RID: 2024 RVA: 0x00004ADB File Offset: 0x00002CDB
		private void UICheckBox_Loaded(object sender, RoutedEventArgs e)
		{
			this.Refresh();
		}

		// Token: 0x17000282 RID: 642
		// (get) Token: 0x060007E9 RID: 2025 RVA: 0x0002E180 File Offset: 0x0002C380
		// (set) Token: 0x060007EA RID: 2026 RVA: 0x00004AE3 File Offset: 0x00002CE3
		public string cb数据源
		{
			get
			{
				return (string)base.GetValue(UICheckBox.cb数据源Property);
			}
			set
			{
				base.SetValue(UICheckBox.cb数据源Property, value);
			}
		}

		// Token: 0x060007EB RID: 2027 RVA: 0x0002E1A0 File Offset: 0x0002C3A0
		protected override void OnClick()
		{
			base.OnClick();
			if (!string.IsNullOrEmpty(this.cb数据源))
			{
				CommonMethods.SetBool(this.cb数据源, base.IsChecked.Value);
			}
		}

		// Token: 0x060007EC RID: 2028 RVA: 0x0002E1DC File Offset: 0x0002C3DC
		public void Refresh()
		{
			if (!string.IsNullOrEmpty(this.cb数据源))
			{
				base.IsChecked = new bool?(CommonMethods.GetBool(this.cb数据源));
			}
		}

		// Token: 0x0400039B RID: 923
		public static readonly DependencyProperty cb数据源Property = DependencyProperty.Register("cb数据源", typeof(string), typeof(UICheckBox), new PropertyMetadata(""));
	}
}
