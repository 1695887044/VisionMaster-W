// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace G.Controls.PropertyGrid
{
    public class ObjectToUIElementConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is UIElement)
                return value;

            return new System.Windows.Controls.Control();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

