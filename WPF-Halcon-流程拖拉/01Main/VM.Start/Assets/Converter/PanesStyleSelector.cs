using System;
using System.Windows;
using System.Windows.Controls;
using VM.Start.UIDesign;

namespace VM.Start.Assets.Converter
{
	// Token: 0x020001EF RID: 495
	public class PanesStyleSelector : StyleSelector
	{
		// Token: 0x170004E6 RID: 1254
		// (get) Token: 0x0600118B RID: 4491 RVA: 0x000089B0 File Offset: 0x00006BB0
		// (set) Token: 0x0600118C RID: 4492 RVA: 0x000089B8 File Offset: 0x00006BB8
		public Style DocumentStyle { get; set; }

		// Token: 0x0600118D RID: 4493 RVA: 0x00047B50 File Offset: 0x00045D50
		public override Style SelectStyle(object item, DependencyObject container)
		{
			Style result;
			if (item is Document)
			{
				result = this.DocumentStyle;
			}
			else
			{
				result = base.SelectStyle(item, container);
			}
			return result;
		}
	}
}
