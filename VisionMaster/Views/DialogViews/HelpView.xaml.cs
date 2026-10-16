using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 「帮助手册」弹窗（左目录带搜索 / 右正文 / 隐藏目录·上一步·打印·字号）。
    /// 逻辑全在 <see cref="ViewModels.DialogViewModels.HelpViewModel"/> 里，这里只留 XAML 入口
    /// 和两件只能做在视图侧的事：把光标送进搜索框、把正文滚轮的手感改成补间。
    /// </summary>
    public partial class HelpView : UserControl
    {
        /// <summary>
        /// 滚轮一格走"五行正文"（跟着字号档位变）。
        /// WPF 默认一格三行且**逐格硬跳**——读长文时既慢又一顿一顿（2026-10-16 现场反馈"卡顿"）。
        /// 浏览器一格约 100px ≈ 标准档四行，这里取五行、再配下面的补间，滚动节奏才像网页那份。
        /// </summary>
        private const int WheelLines = 5;

        /// <summary>每帧向目标靠拢的比例：15ms 一帧时约 100ms 走完 90% 的距离</summary>
        private const double EasePerFrame = 0.25;

        /// <summary>补间一帧的间隔。用 DispatcherTimer 而不是 CompositionTarget.Rendering：
        /// 后者要合成线程出帧才走，断言宿主（离屏、无可见窗口）里根本不 tick，等于测不到。</summary>
        private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(15);

        private readonly DispatcherTimer _scrollTick;
        private double _currentOffset;
        private double _targetOffset;
        private ScrollViewer _bodyScroll;

        public HelpView()
        {
            InitializeComponent();

            _scrollTick = new DispatcherTimer(DispatcherPriority.Render) { Interval = TickInterval };
            _scrollTick.Tick += OnScrollTick;
        }

        /// <summary>
        /// Ctrl+F = 聚焦目录搜索框并全选（网页手册的键位）。
        /// 走代码而不是一条 KeyBinding：聚焦的对象是视图里的命名元素，命令那侧够不到它。
        /// </summary>
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
            }
        }

        // ==================================================================
        //  正文滚轮：接管 → 累加目标 → 补间追过去
        // ==================================================================

        /// <summary>
        /// 只接管正文那一段的滚轮：目录树的滚轮照旧（它要的是逐行点选，不是长文阅读），
        /// 判据是"事件源在不在正文容器里"，而不是鼠标坐标——坐标要跨窗口换算，源元素不用。
        ///
        /// 连续滚动时**不重启动画**：每次只把目标推远，让补间自己追。
        /// 每格都重新起一段动画正是"一顿一顿"的来源（动画被反复打断在起点）。
        /// </summary>
        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
            base.OnPreviewMouseWheel(e);

            var source = e.OriginalSource as DependencyObject;
            if (!IsInsideBody(source))
                return;

            var scroll = BodyScroll();
            if (scroll == null || scroll.ScrollableHeight <= 0)
                return;   // 模板还没就位 / 本页短到不滚动：交回默认处理

            e.Handled = true;

            var lines = BodyViewer.Document is { } doc
                ? doc.LineHeight > 0 ? doc.LineHeight : doc.FontSize * 1.8
                : 25.0;

            _currentOffset = scroll.VerticalOffset;

            // 动画还在跑就从**目标**继续累加，落定了才从**实际位置**起算。
            // 只按实际位置累加的话，快速连滚 N 格会走近似"1 格 + 已到位的那点"，
            // 越滚越追不上——现场抱怨的"持续滚动太慢"正是这个（浏览器的手感也是累加到目标）。
            var from = _scrollTick.IsEnabled ? _targetOffset : scroll.VerticalOffset;
            _targetOffset = Clamp(
                from - e.Delta / 120.0 * lines * WheelLines,
                0,
                scroll.ScrollableHeight);

            if (!_scrollTick.IsEnabled)
                _scrollTick.Start();
        }

        private void OnScrollTick(object sender, EventArgs e)
        {
            var scroll = BodyScroll();
            if (scroll == null)
            {
                _scrollTick.Stop();
                return;
            }

            // 实际位置和上一帧应用值对不上 = 用户中途自己滚了（拖滑块 / 键盘 / 换页重置）：
            // 立刻放弃这段补间跟到新位置，绝不把用户的手动滚动"拽回去"
            if (Math.Abs(scroll.VerticalOffset - _currentOffset) > 1.5)
            {
                _scrollTick.Stop();
                return;
            }

            var diff = _targetOffset - _currentOffset;
            if (Math.Abs(diff) < 0.6)
            {
                scroll.ScrollToVerticalOffset(_targetOffset);
                _scrollTick.Stop();
                return;
            }

            _currentOffset += diff * EasePerFrame;
            scroll.ScrollToVerticalOffset(_currentOffset);
        }

        private ScrollViewer BodyScroll()
        {
            if (_bodyScroll != null)
                return _bodyScroll;

            BodyViewer.ApplyTemplate();
            _bodyScroll = FindVisualChild<ScrollViewer>(BodyViewer);
            return _bodyScroll;
        }

        /// <summary>
        /// 事件源落不落在正文容器里。自己沿视觉树往上找，而不用 VisualTreeHelper.IsDescendantOf：
        /// 那个 API 要求传入的是 Visual，而滚轮源偶尔是内容元素（Run 一类），传进去直接抛参数异常。
        /// </summary>
        private bool IsInsideBody(DependencyObject source)
        {
            for (var current = source; current != null;)
            {
                if (ReferenceEquals(current, BodyViewer))
                    return true;
                current = current is Visual ? VisualTreeHelper.GetParent(current) : null;
            }
            return false;
        }

        private static double Clamp(double value, double min, double max) =>
            value < min ? min : value > max ? max : value;

        private static T FindVisualChild<T>(DependencyObject root) where T : DependencyObject
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T hit)
                    return hit;

                var deeper = FindVisualChild<T>(child);
                if (deeper != null)
                    return deeper;
            }
            return null;
        }
    }
}
