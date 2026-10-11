// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System;
using System.Windows;
using System.Windows.Data;

namespace G.Controls.PropertyGrid
{
    public class CornerRadiusToDoubleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            double radius = 0.0;

            if (value != null)
                radius = ((CornerRadius)value).TopLeft;

            return radius;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            double radius = 0.0;

            if (value != null)
                radius = (double)value;

            return new CornerRadius(radius);
        }
    }
}

