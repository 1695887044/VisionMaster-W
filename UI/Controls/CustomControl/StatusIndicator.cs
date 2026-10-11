using System.Windows;
using System.Windows.Controls;

namespace UI.CustomControl
{
    /// <summary>
    /// 状态灯的四档（与具体业务无关：连接、设备、任务、扫描组都能用）。
    /// 刻意不直接用通信域的 <c>ConnectionState</c>：控件在 UI 库，业务枚举在宿主/通信域，
    /// 让控件认识业务枚举 = 以后每加一种业务都要改控件。宿主侧用触发器/转换器映射过来即可。
    /// </summary>
    public enum StatusLevel
    {
        /// <summary>灰：断开 / 未启用</summary>
        Off,

        /// <summary>绿：正常</summary>
        Ok,

        /// <summary>橙：连接中 / 重连中（默认脉动）</summary>
        Busy,

        /// <summary>红：出错</summary>
        Error,
    }

    /// <summary>
    /// 状态灯：**一枚圆点 + 一行文字**，四档颜色 + 忙碌脉动 + 悬停详情。
    ///
    /// 【为什么重写】旧版是一个 VSM 驱动的"开关灯"（IsActive / ActiveBrush / InactiveBrush + Content），
    /// 全仓零消费 —— 而仓库里真正需要它的地方（通信设置"状态"列）当时是**在 DataTemplate 里手搓**
    /// 的：圆点、文案、错误角标、悬停提示四套 DataTrigger 铺了 100 多行 XAML。
    /// 现在把这四件事收进控件：宿主只写"状态 → 档位/文案"的映射。
    ///
    /// 【颜色口径】模板里的取值刻意与通信设置原胶囊**逐字对齐**（文字色 #15803D/#B45309/#B91C1C
    /// 就是原来那几个），换控件不换观感。
    /// </summary>
    public class StatusIndicator : ContentControl
    {
        static StatusIndicator()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(StatusIndicator), new FrameworkPropertyMetadata(typeof(StatusIndicator)));
        }

        #region 依赖属性

        public static readonly DependencyProperty LevelProperty =
            DependencyProperty.Register(nameof(Level), typeof(StatusLevel), typeof(StatusIndicator),
                new PropertyMetadata(StatusLevel.Off));

        /// <summary>当前档位（颜色 + 是否脉动都由它决定）</summary>
        public StatusLevel Level
        {
            get => (StatusLevel)GetValue(LevelProperty);
            set => SetValue(LevelProperty, value);
        }

        public static readonly DependencyProperty StatusTextProperty =
            DependencyProperty.Register(nameof(StatusText), typeof(string), typeof(StatusIndicator),
                new PropertyMetadata(string.Empty));

        /// <summary>圆点右边的文案（"在线" / "重连中" …）；空串时整段收起，只留圆点</summary>
        public string StatusText
        {
            get => (string)GetValue(StatusTextProperty);
            set => SetValue(StatusTextProperty, value);
        }

        public static readonly DependencyProperty DetailProperty =
            DependencyProperty.Register(nameof(Detail), typeof(string), typeof(StatusIndicator),
                new PropertyMetadata(string.Empty, OnDetailChanged));

        /// <summary>悬停详情（一般是最近一次错误）。空白时**不挂 ToolTip** —— 空串会弹出一个空框。</summary>
        public string Detail
        {
            get => (string)GetValue(DetailProperty);
            set => SetValue(DetailProperty, value);
        }

        public static readonly DependencyProperty IsPulsingProperty =
            DependencyProperty.Register(nameof(IsPulsing), typeof(bool), typeof(StatusIndicator),
                new PropertyMetadata(true));

        /// <summary>Busy 档是否脉动（默认是；静态截图/打印场景可关）</summary>
        public bool IsPulsing
        {
            get => (bool)GetValue(IsPulsingProperty);
            set => SetValue(IsPulsingProperty, value);
        }

        public static readonly DependencyProperty DotSizeProperty =
            DependencyProperty.Register(nameof(DotSize), typeof(double), typeof(StatusIndicator),
                new PropertyMetadata(8.0));

        /// <summary>圆点直径</summary>
        public double DotSize
        {
            get => (double)GetValue(DotSizeProperty);
            set => SetValue(DotSizeProperty, value);
        }

        #endregion

        private static void OnDetailChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not StatusIndicator indicator) return;

            // 在代码里设而不是模板绑定：ToolTip 只有为 null 才不弹，空字符串照样弹一个空框
            var detail = e.NewValue as string;
            indicator.ToolTip = string.IsNullOrWhiteSpace(detail) ? null : detail;
        }
    }
}
