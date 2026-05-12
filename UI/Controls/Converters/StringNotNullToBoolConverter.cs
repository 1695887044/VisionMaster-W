using System.Collections.Generic;
using System.Globalization;

namespace UI.Converters
{
    public class StringNotNullToBoolConverter : BaseMarkupConverter
    {
        public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !string.IsNullOrEmpty(value as string);
        }
    }
}
