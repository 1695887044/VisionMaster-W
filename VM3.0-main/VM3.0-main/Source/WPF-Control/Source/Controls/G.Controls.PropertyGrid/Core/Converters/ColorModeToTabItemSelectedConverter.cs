// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System;
using System.Globalization;
using System.Windows.Data;

namespace G.Controls.PropertyGrid
{
    public class ColorModeToTabItemSelectedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            ColorMode colorMode = (ColorMode)value;
            return (colorMode == ColorMode.ColorPalette) ? 0 : 1;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int index = (int)value;
            return (index == 0) ? ColorMode.ColorPalette : ColorMode.ColorCanvas;
        }
    }
}

