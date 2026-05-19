using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace UI.CustomControl.PropertyGrid
{
    #region 扁平化布局处理器 (FlatPropertyGrid 用)
    public class FlatLayoutProcessor : IControlProcessor
    {
        private readonly SuperDisplayAttribute _display;

        public FlatLayoutProcessor(SuperDisplayAttribute display) => _display = display;

        public void Execute(ControlContext context)
        {
            var grid = context.RootCellGrid;
            var wrapper = context.WrapPanel;

            // 动态获取你的 XAML 主题色，如果没有则给个低调的默认灰
            var borderColor = new SolidColorBrush(Color.FromRgb(229, 229, 229));
            var labelBgColor = Application.Current.TryFindResource("SidebarBg") as Brush ?? new SolidColorBrush(Color.FromRgb(249, 249, 249));

            var rowBorder = new Border { BorderBrush = borderColor, BorderThickness = new Thickness(0, 0, 0, 1) };
            var innerGrid = new Grid();
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "FlatLabelGroup" });
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var labelBorder = new Border
            {
                BorderBrush = borderColor,
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(16, 12, 32, 12),
                Background = labelBgColor, // 🌟 使用你的 SidebarBg
                Child = CreateLabel(context.Property, _display, context.Property.GetCustomAttribute<IconAttribute>())
            };
            Grid.SetColumn(labelBorder, 0);

            var controlBorder = new Border { Padding = new Thickness(16, 8, 16, 8) };
            context.Control.HorizontalAlignment = HorizontalAlignment.Stretch;
            context.Control.VerticalAlignment = VerticalAlignment.Center;

            if (!wrapper.Children.Contains(context.Control)) wrapper.Children.Add(context.Control);

            controlBorder.Child = wrapper;
            Grid.SetColumn(controlBorder, 1);

            innerGrid.Children.Add(labelBorder);
            innerGrid.Children.Add(controlBorder);
            rowBorder.Child = innerGrid;
            grid.Children.Add(rowBorder);
        }

        private UIElement CreateLabel(PropertyInfo prop, SuperDisplayAttribute display, IconAttribute? iconAttr)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            // 🌟 动态获取你定义的 AccentBlue (高亮蓝)
            var accentBrush = Application.Current.TryFindResource("AccentBlue") as Brush ?? Brushes.DodgerBlue;

            if (!string.IsNullOrEmpty(iconAttr?.IconCode))
            {
                try
                {
                    panel.Children.Add(new System.Windows.Shapes.Path
                    {
                        Data = Geometry.Parse(iconAttr.IconCode),
                        Fill = accentBrush, // 🌟 使用你的主题蓝
                        Width = 14,
                        Height = 14,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(0, 0, 8, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                }
                catch { panel.Children.Add(new TextBlock { Text = "⚠ ", Foreground = Brushes.Red, VerticalAlignment = VerticalAlignment.Center }); }
            }
            else { panel.Children.Add(new Border { Width = 22 }); }

            // 🌟 完美应用你的 PropertyLabelStyle
            var textBlock = new TextBlock { Text = display.Name };
            var labelStyle = Application.Current.TryFindResource("PropertyLabelStyle") as Style;
            if (labelStyle != null)
            {
                textBlock.Style = labelStyle;
                // 如果你的 Style 里写了 ToolTip 绑定，这里就不再写死覆盖它了
                if (string.IsNullOrEmpty(display.Description) == false)
                {
                    textBlock.ToolTip = display.Description;
                }
            }
            else
            {
                // 兜底样式
                textBlock.Foreground = new SolidColorBrush(Color.FromRgb(51, 51, 51));
                textBlock.VerticalAlignment = VerticalAlignment.Center;
                textBlock.FontSize = 13;
            }
            panel.Children.Add(textBlock);

            if (Attribute.IsDefined(prop, typeof(RequiredAttribute)))
                panel.Children.Add(new TextBlock { Text = " *", Foreground = new SolidColorBrush(Color.FromRgb(216, 59, 1)), VerticalAlignment = VerticalAlignment.Center });

            return panel;
        }
    }
    #endregion


    /// <summary>
    /// 🌟 验证拦截器：自动读取 ValidationBase 特性并挂载错误提示
    /// </summary>
    public class ValidationProcessor : IControlProcessor
    {
        public void Execute(ControlContext context)
        {
            // 获取我们写的 ValidationBaseAttribute
            var validators = context.Property.GetCustomAttributes<ValidationBaseAttribute>().ToList();
            if (!validators.Any()) return;

            var errorText = new TextBlock
            {
                FontSize = 10,
                Foreground = Brushes.Red,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(2, 2, 0, 0)
            };

            if (context.WrapPanel is StackPanel sp) sp.Children.Add(errorText);

            if (context.Control is TextBox tb)
            {
                RoutedEventHandler onLostFocus = (s, e) =>
                {
                    // 遍历检查哪个验证失败了
                    var firstError = validators.FirstOrDefault(v => !v.IsValid(tb.Text));
                    if (firstError != null)
                    {
                        tb.BorderBrush = Brushes.Red;
                        errorText.Text = firstError.ErrorMessage;
                        errorText.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        tb.ClearValue(Control.BorderBrushProperty);
                        errorText.Visibility = Visibility.Collapsed;
                    }
                };

                tb.LostFocus += onLostFocus;
                context.RegisterCleanup?.Invoke(() => tb.LostFocus -= onLostFocus); // 内存防泄漏
            }
        }
    }



}
