using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VM.Start.Assets.Converter
{
	// Token: 0x020001EA RID: 490
	public class LevelConverter : IValueConverter
	{
		// Token: 0x06001173 RID: 4467 RVA: 0x00047A38 File Offset: 0x00045C38
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			return new Thickness((double)(5 + 19 * (int)value), 0.0, 5.0, 0.0);
		}

		// Token: 0x06001174 RID: 4468 RVA: 0x00005292 File Offset: 0x00003492
		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}
}
