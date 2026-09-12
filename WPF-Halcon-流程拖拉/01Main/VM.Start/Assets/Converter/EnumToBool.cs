using System;
using System.Globalization;
using System.Windows.Data;

namespace VM.Start.Assets.Converter
{
	// Token: 0x020001E6 RID: 486
	public class EnumToBool : IValueConverter
	{
		// Token: 0x06001162 RID: 4450 RVA: 0x00047930 File Offset: 0x00045B30
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			object result;
			if (value == null || parameter == null)
			{
				result = false;
			}
			else
			{
				result = value.Equals(parameter);
			}
			return result;
		}

		// Token: 0x06001163 RID: 4451 RVA: 0x00047960 File Offset: 0x00045B60
		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			bool flag;
			bool flag2;
			if (value is bool)
			{
				flag = (bool)value;
				flag2 = true;
			}
			else
			{
				flag2 = false;
			}
			object result;
			if (flag2 && flag)
			{
				result = parameter;
			}
			else
			{
				result = Binding.DoNothing;
			}
			return result;
		}
	}
}
