using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 运行态诊断标记的严重程度，决定角标颜色。
    ///
    /// 只分两级，因为这两级的处理方式完全不同：
    /// 橙（<see cref="Warning"/>）是"没接上"——绑定的变量没解析到、图元没渲染出来，
    /// 属于组态配置问题，改配置即可；红（<see cref="Error"/>）是"接上了但跑不通"——
    /// 值转换失败，属于数据/类型问题，得看具体值。合成一个"有问题"等级会让操作员分不清该找谁。
    /// </summary>
    public enum ScadaDiagnosticLevel
    {
        /// <summary>橙：未命中 / 未接线（变量没解析到、图元未渲染）</summary>
        Warning = 0,

        /// <summary>红：值转换失败（接上了但值用不了）</summary>
        Error = 1,
    }

    /// <summary>
    /// 运行态诊断角标层：在出问题的图元右上角点一个小圆点，鼠标悬停看原因。
    ///
    /// 为什么是"集中一层"而不是"往每个图元模板里塞一个角标槽"：
    /// 图元模板有 5 套（矩形/文本/按钮/…），将来还会加；把诊断塞进模板意味着
    /// 每加一个图元都要记得再写一遍角标，漏一个就"这个图元出问题看不见"。
    /// 集中在画布模板的 PART_DiagnosticLayer 上，新增图元自动获得诊断能力，一行都不用改。
    ///
    /// 为什么角标位置用<b>绑定</b>而不是 Set 时算一次：
    /// S6 的能力之一就是"变量驱动图元几何"（宽度/位置随值变化）。算一次的话，
    /// 图元一移动角标就留在原地，变成"指向空气的红点"——比没有标记更误导。
    /// 绑定到控件的 Canvas.Left/Top/ActualWidth 后，几何怎么变角标都跟得住。
    ///
    /// 已知边界：角标是<b>画面坐标系</b>的 6px，不随 Zoom 反向补偿——
    /// 缩放很小（&lt;0.5）时会明显偏小。诊断的权威记录始终是日志，角标只是"往哪看"的提示。
    /// </summary>
    public sealed class ScadaDiagnosticOverlay
    {
        /// <summary>圆点直径（画面坐标系 px）</summary>
        private const double DotSize = 6;

        /// <summary>命中区边长：圆点太小，鼠标很难精确停上去，外面套一圈透明方块专门接 ToolTip</summary>
        private const double HitSize = 14;

        private static readonly Brush WarningBrush = ScadaBrushes.Frozen(0xE8, 0x8B, 0x1A);
        private static readonly Brush ErrorBrush = ScadaBrushes.Frozen(0xE0, 0x3A, 0x2B);

        private readonly ScadaCanvas _owner;
        private readonly Dictionary<ScadaElementBase, Marker> _markers = new();

        internal ScadaDiagnosticOverlay(ScadaCanvas owner) => _owner = owner;

        /// <summary>当前角标数（自检/断言用）</summary>
        public int Count => _markers.Count;

        /// <summary>
        /// 给某个图元控件打上诊断角标。同一控件重复调用只更新颜色与提示语，不会叠出第二个点。
        /// </summary>
        public void Set(ScadaElementBase control, ScadaDiagnosticLevel level, string? message)
        {
            if (control == null)
                return;

            if (_markers.TryGetValue(control, out var existing))
            {
                Apply(existing, level, message);
                return;
            }

            var layer = _owner.DiagnosticLayer;
            if (layer == null)
                return; // 模板里没有诊断层部件（自定义模板）：静默跳过，日志仍然有记录

            var marker = CreateMarker(control);
            _markers[control] = marker;
            layer.Children.Add(marker.Root);
            Apply(marker, level, message);
        }

        /// <summary>
        /// 撤掉某个图元的角标。用于"这个图元原来有问题、现在恢复正常"——
        /// 只清一个而不是整层 <see cref="Clear"/>，否则一个值转好了会把别处的提示一起抹掉。
        /// </summary>
        public void Remove(ScadaElementBase control)
        {
            if (control == null || !_markers.Remove(control, out var marker))
                return;

            _owner.DiagnosticLayer?.Children.Remove(marker.Root);
        }

        /// <summary>清空全部角标（停止运行、元素层重建时调用）</summary>
        public void Clear()
        {
            if (_markers.Count == 0)
                return;

            if (_owner.DiagnosticLayer is { } layer)
            {
                foreach (var marker in _markers.Values)
                    layer.Children.Remove(marker.Root);
            }

            _markers.Clear();
        }

        private static void Apply(Marker marker, ScadaDiagnosticLevel level, string? message)
        {
            marker.Dot.Background = level == ScadaDiagnosticLevel.Error ? ErrorBrush : WarningBrush;
            marker.Root.ToolTip = string.IsNullOrWhiteSpace(message)
                ? level.ToString()
                : message;
        }

        private static Marker CreateMarker(ScadaElementBase control)
        {
            var dot = new Border
            {
                Width = DotSize,
                Height = DotSize,
                CornerRadius = new CornerRadius(DotSize / 2),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };

            var root = new Border
            {
                Width = HitSize,
                Height = HitSize,
                Background = Brushes.Transparent, // 透明但非 null：null 画刷不参与命中，ToolTip 就永远不弹
                Child = dot,
            };

            // 诊断层与选中层同在元素层之上，再给个高 ZIndex，保证不被后加的图元盖住
            Panel.SetZIndex(root, 1000);

            root.SetBinding(Canvas.LeftProperty, BuildPositionBinding(control, "Left"));
            root.SetBinding(Canvas.TopProperty, BuildPositionBinding(control, "Top"));
            root.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(UIElement.Visibility)) { Source = control });

            return new Marker(root, dot);
        }

        /// <summary>
        /// 位置绑定：Left = 控件左边界 + 控件宽 - 命中区宽（贴右上角）；Top = 控件上边界。
        /// 两个方向都用 [位置, 尺寸] 两值的 MultiBinding，转换器按参数区分方向，
        /// 好处是"位置怎么算"只有一处（转换器），不会出现 Left 补偿了、Top 忘了补偿。
        /// </summary>
        private static MultiBinding BuildPositionBinding(ScadaElementBase control, string direction)
        {
            bool isLeft = direction == "Left";

            var binding = new MultiBinding
            {
                Converter = MarkerPositionConverter.Instance,
                ConverterParameter = direction,
            };

            binding.Bindings.Add(new Binding
            {
                Source = control,
                Path = new PropertyPath("(0)", isLeft ? Canvas.LeftProperty : Canvas.TopProperty),
                Mode = BindingMode.OneWay,
            });
            binding.Bindings.Add(new Binding
            {
                Source = control,
                Path = new PropertyPath(isLeft ? nameof(FrameworkElement.ActualWidth) : nameof(FrameworkElement.ActualHeight)),
                Mode = BindingMode.OneWay,
            });

            return binding;
        }

        private sealed record Marker(Border Root, Border Dot);

        private sealed class MarkerPositionConverter : IMultiValueConverter
        {
            public static readonly MarkerPositionConverter Instance = new();

            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            {
                double position = ToDouble(values, 0);
                double size = ToDouble(values, 1);

                if (double.IsNaN(position)) position = 0;
                if (double.IsNaN(size)) size = 0;

                return parameter as string == "Left"
                    ? position + size - HitSize
                    : position;
            }

            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
                => throw new NotSupportedException();

            private static double ToDouble(object[] values, int index)
                => index < values.Length && values[index] is double d ? d : 0;
        }
    }
}
