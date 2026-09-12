using System;
using System.Globalization;
using System.Windows.Data;

namespace VM.Start.Assets.Converter
{
	// Token: 0x020001E9 RID: 489
	public sealed class IsNullConverter : IValueConverter
	{
		// Token: 0x0600116F RID: 4463 RVA: 0x000027F9 File Offset: 0x000009F9
		private IsNullConverter()
		{
		}

		// Token: 0x170004E3 RID: 1251
		// (get) Token: 0x06001170 RID: 4464 RVA: 0x000479E8 File Offset: 0x00045BE8
		public static IsNullConverter Instance
		{
			get
			{
				IsNullConverter result;
				if ((result = IsNullConverter._instance) == null)
				{
					result = (IsNullConverter._instance = new IsNullConverter());
				}
				return result;
			}
		}

		// Token: 0x06001171 RID: 4465 RVA: 0x00047A0C File Offset: 0x00045C0C
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			return value == null;
		}

		// Token: 0x06001172 RID: 4466 RVA: 0x00047A24 File Offset: 0x00045C24
		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			return Binding.DoNothing;
		}

		// Token: 0x04000988 RID: 2440
		private static IsNullConverter _instance;
	}
}
