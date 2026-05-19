using System;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UI.Attributes;

namespace UI.CustomControl.PropertyGrid
{
    #region 卡片式布局处理器 (CardPropertyGrid 用)
    public class LayoutProcessor : IControlProcessor
    {
        private readonly SuperDisplayAttribute _display;

        public LayoutProcessor(SuperDisplayAttribute display) => _display = display;

        public void Execute(ControlContext context)
        {
            var grid = context.RootCellGrid;
            var wrapper = context.WrapPanel;

            grid.Margin = new Thickness(0, 0, 16, 0);
            grid.RowDefinitions.Clear();
            grid.ColumnDefinitions.Clear();

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "CardLabelGroup" });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var iconAttr = context.Property.GetCustomAttribute<IconAttribute>();
            var labelPanel = (FrameworkElement)CreateLabel(context.Property, _display, iconAttr);

            labelPanel.HorizontalAlignment = HorizontalAlignment.Right;
            labelPanel.Margin = new Thickness(0, 0, 16, 0);
            Grid.SetColumn(labelPanel, 0);

            context.Control.HorizontalAlignment = HorizontalAlignment.Stretch;
            context.Control.VerticalAlignment = VerticalAlignment.Center;

            if (!wrapper.Children.Contains(context.Control))
            {
                wrapper.Children.Add(context.Control);
            }

            Grid.SetColumn(wrapper, 1);
            grid.Children.Add(labelPanel);
            grid.Children.Add(wrapper);
        }

        private UIElement CreateLabel(PropertyInfo prop, SuperDisplayAttribute display, IconAttribute? iconAttr)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            // 获取你在 Dictionary1.xaml 中定义的主题 Label 样式
            var labelStyle = Application.Current.TryFindResource("PropertyLabelStyle") as Style;

            // 🎨 1. 图标层
            if (!string.IsNullOrEmpty(iconAttr?.IconCode))
            {
                try
                {
                    panel.Children.Add(new System.Windows.Shapes.Path
                    {
                        // ✅ 使用更专业、更清晰的图标数据：
                        // “内存限制 (MB)”图标使用：M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8z
                        Data = Geometry.Parse(iconAttr.IconCode),
                        Fill = (SolidColorBrush)Application.Current.FindResource("AccentBlue"), // 使用你在主题中定义的高亮蓝
                        Width = 14,
                        Height = 14,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(0, 0, 10, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                }
                catch { panel.Children.Add(new TextBlock { Text = "⚠ ", Foreground = Brushes.Red, VerticalAlignment = VerticalAlignment.Center }); }
            }
            else { panel.Children.Add(new Border { Width = 24 }); }

            // 📝 2. 主文本层
            var textBlock = new TextBlock
            {
                Text = display.Name,
                // ✅ 寻找资源字典中定义的 PropertyLabelStyle Style 并应用
                Style = labelStyle ?? new Style(typeof(TextBlock)) // 兜底
            };
            panel.Children.Add(textBlock);

            // ⭐ 3. 必填红星
            if (Attribute.IsDefined(prop, typeof(RequiredAttribute)))
                panel.Children.Add(new TextBlock { Text = " *", Foreground = Brushes.Red, VerticalAlignment = VerticalAlignment.Center });

            return panel;
        }
    }
    #endregion
}