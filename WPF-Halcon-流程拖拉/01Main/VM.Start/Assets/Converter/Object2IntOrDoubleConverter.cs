using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Data;

namespace VM.Start.Assets.Converter
{
    public class Object2IntOrDoubleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int)
            {
                return System.Convert.ToInt32(value);
            }
            else if (value is double)
            {
                return System.Convert.ToDouble(value);
            }
            return null;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }
}
