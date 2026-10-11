using G.ValueConverter;
using System;
using System.Globalization;

namespace G.App.FileManager
{
    public class GetTickToMillisecond : MarkupValueConverterBase
    {
        public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long l)
                return TimeSpan.FromTicks(l).TotalMilliseconds;
            return value;
        }
    }
}
