// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System;
using System.Windows.Data;

namespace G.Controls.PropertyGrid
{
    public class ObjectTypeToNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value != null)
            {
                string valueString = value.ToString();
                if (string.IsNullOrEmpty(valueString)
                 || (valueString == value.GetType().UnderlyingSystemType.ToString()))
                {
                    return value.GetType().Name;
                }
                return value;
            }
            return null;
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

