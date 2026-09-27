using System;
using System.Windows;
using System.Windows.Threading;

namespace UI.Behaviors
{
    /// <summary>
    /// 让弹窗在加载后居中显示。
    ///
    /// 【为什么不用 WindowStartupLocation】
    /// 它是"在窗口**显示的那一刻**算一次"的语义 —— 我们在 View 的 Loaded 里设时已经晚了，
    /// 位置早已算完（现象就是弹窗留在系统给的默认位置：偏左上或被主界面侧栏压住）。
    /// 而且它**不是依赖属性**，也写不进 &lt;prism:Dialog.WindowStyle&gt; 的 Setter
    ///（那样会在开窗时抛 ArgumentNullException，仓库里有断言守着）。
    ///
    /// 【为什么直接设 Left / Top 有效】
    /// 它们就是窗口的位置属性，改一下就立即移动窗口 —— 不存在"错过时机"。
    ///
    /// 【为什么要 Dispatcher.BeginInvoke 延后一拍】
    /// 无边框弹窗的尺寸在 Loaded 时还可能被内容改写（例如参数面板会把自己改成"按内容定高"），
    /// 立刻按当时尺寸居中会偏。排到布局之后（Loaded 优先级）再算，拿到的是最终尺寸。
    /// </summary>
    public static class WindowCenteringBehavior
    {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled", typeof(bool), typeof(WindowCenteringBehavior),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static void SetIsEnabled(DependencyObject element, bool value)
            => element.SetValue(IsEnabledProperty, value);

        public static bool GetIsEnabled(DependencyObject element)
            => (bool)element.GetValue(IsEnabledProperty);

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Window window || e.NewValue is not true) return;

            window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() => CenterOnWorkArea(window)));
        }

        /// <summary>
        /// 按**工作区**（去掉任务栏）居中，而不是整个屏幕 ——
        /// 任务栏在下方时，按整屏居中会让窗口重心偏上，看着像没居中。
        /// </summary>
        private static void CenterOnWorkArea(Window window)
        {
            var area = SystemParameters.WorkArea;

            // 尺寸取实际值：Loaded 之后布局已完成，ActualWidth/Height 才是真实大小。
            // 万一是 0（极端情况，例如窗口尚未完成布局），退回窗口的设定尺寸。
            var width = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
            var height = window.ActualHeight > 0 ? window.ActualHeight : window.Height;

            if (double.IsNaN(width) || double.IsNaN(height) || width <= 0 || height <= 0) return;

            window.Left = area.Left + (area.Width - width) / 2;
            window.Top = area.Top + (area.Height - height) / 2;
        }
    }
}
