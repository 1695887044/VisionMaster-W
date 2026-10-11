// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System;
using System.Windows;
using System.Windows.Data;

namespace G.Controls.PropertyGrid
{
    public class CalculatorMemoryToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return (decimal)value == decimal.Zero ? Visibility.Hidden : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

