using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace UI.CustomControl.PropertyGrid
{
    /// <summary>
    /// 属性网格的**统一度量与外观**（v1 设计基线：docs\UI设计基线\property-grid\design.html）。
    ///
    /// 【为什么要有它】改造前每个生成器各写各的：文本 32px、数值 44px（`NumericBox` 的 MinHeight=32
    /// 又叠了 Padding=10,6）、下拉 32px + 写死 Padding、开关 24px。同一张表里三种行高，
    /// 并排看就像三套控件体系。度量集中到一处之后，"统一行高"只需改这个常量。
    ///
    /// 【颜色口径】一律 <c>TryFindResource</c> + 令牌，取不到回退中性灰。
    /// **绝不用 FindResource**：属性网格可能被"没有合并本库主题的宿主"或设计器直接 new 出来，
    /// 抛异常等于整个面板打不开（`PropertyGridLayoutProcessor` 的既有注释已写明这条）。
    /// </summary>
    public static class PropertyGridMetrics
    {
        /// <summary>输入类控件的统一高度（基线 36px）</summary>
        public const double ControlHeight = 36;

        /// <summary>统一圆角（基线 8px）</summary>
        public const double ControlRadius = 8;

        /// <summary>统一字号（基线 12.5px）</summary>
        public const double ControlFontSize = 12.5;

        /// <summary>
        /// 给输入类控件套上统一外观：高 36、圆角 8、令牌配色。
        ///
        /// 圆角靠给控件资源塞一份隐式 <see cref="Border"/> 样式实现（WPF 的 TextBox/ComboBox
        /// 默认模板里那层 Border 会查它）——这是仓内既有的做法（`StructValueGenerator` 原来就这么干），
        /// 不改模板、不引入新的主题依赖。
        ///
        /// 【为什么在这里显式写 Height】`NumericBox` 的主题是 <c>MinHeight=32</c> + <c>Padding=10,6</c>，
        /// 实得 44px；而它被 6 个插件视图共用，改主题默认值会连带改掉那些界面。
        /// 所以统一高度只能在本层**逐实例覆盖**（Height 优先于样式里的 MinHeight）。
        /// </summary>
        public static void ApplyInputLook(Control control)
        {
            control.Height = ControlHeight;
            control.MinHeight = 0;
            control.Padding = new Thickness(10, 0, 10, 0);
            control.FontSize = ControlFontSize;
            control.VerticalContentAlignment = VerticalAlignment.Center;
            control.Background = Brush("DialogSurfaceBrush", 255, 255, 255);
            control.BorderBrush = Brush("DialogBorderBrush", 226, 232, 240);
            control.Foreground = Brush("DialogTextPrimaryBrush", 15, 23, 42);

            if (!control.Resources.Contains(typeof(Border)))
            {
                var borderStyle = new Style(typeof(Border));
                borderStyle.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(ControlRadius)));
                control.Resources.Add(typeof(Border), borderStyle);
            }
        }

        /// <summary>只读态的配色：浅底 + 弱化文字（基线「虚线边框 + 浅底」）</summary>
        public static void ApplyReadOnlyLook(Control control)
        {
            control.Background = Brush("DialogRowHoverBrush", 241, 245, 249);
            control.Foreground = Brush("DialogTextMutedBrush", 148, 163, 184);
            control.BorderBrush = Brush("DialogBorderBrush", 226, 232, 240);
        }

        /// <summary>
        /// 布尔开关的统一尺寸。`Grid_SwitchToggleStyle` 的模板已经给了 38×22 的轨道，
        /// 这里只把控件自身的外框高度压到与输入框一致，避免 24px 的开关行比旁边矮一截。
        /// </summary>
        public static void ApplySwitchLook(Control control)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            control.Height = 22;
            control.MinHeight = 0;
        }

        /// <summary>取令牌画刷；取不到回退中性灰（不抛——见类型注释）</summary>
        public static Brush Brush(string key, byte r, byte g, byte b)
            => Application.Current?.TryFindResource(key) as Brush
               ?? new SolidColorBrush(Color.FromRgb(r, g, b));
    }
}
