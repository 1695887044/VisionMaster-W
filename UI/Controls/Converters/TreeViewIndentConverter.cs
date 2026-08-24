using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace UI.Converters
{
    public class TreeViewIndentConverter: BaseMarkupConverter
    {
        public double IndentStep { get; set; } = 16;


        public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var item = value as TreeViewItem;
            if (item == null) return new Thickness(0);

            int depth = 0;

            // 安全地向上查找逻辑树，计算当前处于第几层级
            ItemsControl parent = ItemsControl.ItemsControlFromItemContainer(item);
            while (parent != null && parent is TreeViewItem)
            {
                depth++;
                parent = ItemsControl.ItemsControlFromItemContainer(parent);
            }

            // 返回一个只有 Left 有值的 Margin (比如第0层返回 0, 第1层返回 16, 第2层返回 32)
            return new Thickness(depth * IndentStep, 0, 0, 0);
        }

    }
}
