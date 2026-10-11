// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;

namespace G.Controls.PropertyGrid
{
    public class IsDefaultCategoryConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string categoryName = value as string;
            if (categoryName != null)
            {
                return categoryName == CategoryAttribute.Default.Category;
            }

            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

