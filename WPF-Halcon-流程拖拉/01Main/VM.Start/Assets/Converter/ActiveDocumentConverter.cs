using System;
using System.Globalization;
using System.Windows.Data;
using VM.Start.UIDesign;

namespace VM.Start.Assets.Converter
{
	// Token: 0x020001DF RID: 479
	public class ActiveDocumentConverter : IValueConverter
	{
		// Token: 0x06001142 RID: 4418 RVA: 0x000477C0 File Offset: 0x000459C0
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			object result;
			if (value is Document)
			{
				result = value;
			}
			else
			{
				result = Binding.DoNothing;
			}
			return result;
		}

		// Token: 0x06001143 RID: 4419 RVA: 0x000477C0 File Offset: 0x000459C0
		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			object result;
			if (value is Document)
			{
				result = value;
			}
			else
			{
				result = Binding.DoNothing;
			}
			return result;
		}
	}
}
