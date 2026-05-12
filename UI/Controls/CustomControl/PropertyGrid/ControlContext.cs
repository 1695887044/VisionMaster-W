using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Permissions;
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
    public class ControlContext
    {
        public PropertyInfo Property { get; set; }
        public object BindingSource { get; set; }
        public FrameworkElement Control { get; set; } // 具体的输入控件（TextBox, ComboBox等）
        public Panel WrapPanel { get; set; }        // 控件的直接包装容器
        public Grid RootCellGrid { get; set; }      // 包含 Label 和 Control 的最外层格子
    }

    public interface IControlProcessor
    {
        void Execute(ControlContext context);
    }
    public class ValidationProcessor : IControlProcessor
    {
        public void Execute(ControlContext context)
        {
            var validators = context.Property.GetCustomAttributes<ValidationBaseAttribute>().ToList();
            if (!validators.Any()) return;

            // 创建一个用于显示错误的 TextBlock
            var errorText = new TextBlock
            {
                FontSize = 10,
                Foreground = Brushes.Red,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(2, 2, 0, 0)
            };

            // 将错误提示加到控件下方的包装容器中
            if (context.WrapPanel is StackPanel sp) sp.Children.Add(errorText);

            // 注入逻辑
            if (context.Control is TextBox tb)
            {
                tb.LostFocus += (s, e) =>
                {
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
            }
        }
    }
    public class CommandProcessor : IControlProcessor
    {
        public void Execute(ControlContext context)
        {
            var attr = context.Property.GetCustomAttribute<CommandAttribute>();
            if (attr == null) return;
            ApplyCommandBinding(context.Control, attr, context.BindingSource);

        }
        private void ApplyCommandBinding(FrameworkElement target, CommandAttribute attr, object BindingObject)
        {
            if (attr == null || string.IsNullOrEmpty(attr.Command))
                return;
            // 1. 创建绑定对象
            Binding commandBinding = new Binding(attr.Command);

            // 2. 处理 RelativeSource 逻辑
            if (attr.Mode != RelativeSourceMode.Self)
            {
                commandBinding.RelativeSource = new RelativeSource
                {
                    Mode = attr.Mode,
                    AncestorType = attr.AncestorType,
                    AncestorLevel = attr.AncestorLevel,
                };
            }
            else
            {
                commandBinding.Source = BindingObject;
            }

            // 3. 确定绑定目标
            // 如果是 Button，绑定 Command 属性；如果是 TextBox，可以绑定到行为或特定属性
            if (target is ButtonBase button)
            {
                BindingOperations.SetBinding(button, ButtonBase.CommandProperty, commandBinding);

                // 处理 CommandParameter
                if (attr.CommandParam != null)
                {
                    button.CommandParameter = attr.CommandParam;
                }
            }
            // 这里可以根据需要扩展，比如给 TextBox 绑定回车命令等
        }
    }
    public class LayoutProcessor : IControlProcessor
    {
        private readonly SuperDisplayAttribute _display;

        public LayoutProcessor(SuperDisplayAttribute display)
        {
            _display = display;
        }

        public void Execute(ControlContext context)
        {
            var grid = context.RootCellGrid;

            var iconAttr = context.Property.GetCustomAttribute<IconAttribute>();
            var label = CreateLabel(_display, iconAttr);

            var wrapper = context.WrapPanel;

            grid.Margin = new Thickness(0, 0, 16, 0);
            grid.RowDefinitions.Clear();
            grid.ColumnDefinitions.Clear();

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "PropertyLabelGroup" });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid.SetColumn(label, 0);
            Grid.SetColumn(wrapper, 1);

            grid.Children.Add(label);
            grid.Children.Add(wrapper);

            // 🚨 修复 2：强行扒掉控件默认的傲娇脾气，要求它们 100% 填充满右侧的网格空间
            context.Control.HorizontalAlignment = HorizontalAlignment.Stretch;
            context.Control.VerticalAlignment = VerticalAlignment.Center;

            if (!wrapper.Children.Contains(context.Control))
            {
                wrapper.Children.Add(context.Control);
            }
        }

        private UIElement CreateLabel(SuperDisplayAttribute display, IconAttribute iconAttr)
        {
            var textBlock = new TextBlock
            {
                Text = display?.Name,
                Style = (Style)Application.Current.TryFindResource("PropertyLabelStyle"),
                ToolTip = string.IsNullOrEmpty(display?.Description) ? display?.Name : display.Description,
                VerticalAlignment = VerticalAlignment.Center
            };

            var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            if (iconAttr != null && !string.IsNullOrEmpty(iconAttr.IconCode))
            {
                var path = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse(iconAttr.IconCode),
                    Fill = new SolidColorBrush(Color.FromRgb(0, 95, 184)),
                    Width = 14,
                    Height = 14,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 0, 8, 0), // 图标宽 14 + 边距 8 = 22 像素
                    VerticalAlignment = VerticalAlignment.Center
                };
                panel.Children.Add(path);
            }
            else
            {
                // 🚨 修复 3：如果没有图标，塞入一个绝对透明的占位盒子！
                // 宽度严格等于 (图标的 14 + 边距的 8) = 22
                // 这将彻底消灭文字参差不齐的锯齿感，让没有图标的文字也完美左对齐！
                panel.Children.Add(new Border { Width = 22 });
            }

            panel.Children.Add(textBlock);
            return panel;
        }
    }
    public class PermissionProcessor : IControlProcessor
    {
        public static string CurrentMockRole = "Admin";

        public void Execute(ControlContext context)
        {
            var attr = context.Property.GetCustomAttribute<PermissionAttribute>();
            if (attr == null) return;

            // 检查权限
            bool hasPermission = CheckUserHasRole(attr.RequiredRole);

            if (!hasPermission)
            {
                if (attr.HideIfDenied)
                {
                    // 没权限直接整个小格子隐身
                    context.RootCellGrid.Visibility = Visibility.Collapsed;
                }
                else
                {
                    // 没权限则置灰变只读
                    context.Control.IsEnabled = false;
                    context.Control.Opacity = 0.4;

                    var label = context.RootCellGrid.Children.OfType<TextBlock>().FirstOrDefault();
                    if (label != null)
                    {
                        label.Inlines.Add(new Run { Text = " 🔒", Foreground = Brushes.Red });
                        label.ToolTip = $"权限不足！需要角色：[{attr.RequiredRole}]";
                    }
                }
            }
        }

        private bool CheckUserHasRole(string requiredRole)
        {
            // 测试逻辑：如果是 Admin 就拥有所有权限，否则必须绝对匹配
            if (CurrentMockRole == "Admin") return true;
            return CurrentMockRole == requiredRole;
        }
    }
}

