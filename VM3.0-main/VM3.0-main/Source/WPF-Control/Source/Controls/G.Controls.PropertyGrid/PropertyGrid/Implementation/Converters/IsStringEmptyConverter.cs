// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System;
using System.Globalization;
using System.Windows.Data;

namespace G.Controls.PropertyGrid
{
    public class IsStringEmptyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string)
            {
                return string.IsNullOrEmpty((string)value);
            }

            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

