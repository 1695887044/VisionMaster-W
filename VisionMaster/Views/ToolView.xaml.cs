using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace VisionMaster.Views
{
    /// <summary>
    /// ToolView.xaml 的交互逻辑。
    ///
    /// 这里唯一的活是"悬停预览浮层"（<c>PreviewPopup</c>）：
    /// 条目上 MouseEnter 起一个 ~300ms 的延时定时器，到点开 Popup；MouseLeave 起一个 ~250ms 的关窗延时
    /// （给鼠标从条目移到浮层留出跨越空隙的时间）。鼠标移进浮层时给 ScrollViewer 焦点——Popup 里的滚轮
    /// 只有内容拿到焦点才进得来。**这正是不能用 ToolTip 的原因**：ToolTip 的内容拿不到焦点，
    /// 里面的滚动条在真机上点了不动（2026-10-14 实测）。
    /// </summary>
    public partial class ToolView : UserControl
    {
        private readonly DispatcherTimer _openTimer;
        private readonly DispatcherTimer _closeTimer;

        /// <summary>当前悬停的条目（预览内容源）</summary>
        private FrameworkElement? _hoveredItem;

        /// <summary>进浮层滚动时我们会抢一次焦点，关窗时还回去</summary>
        private IInputElement? _focusBeforePreview;

        public ToolView()
        {
            InitializeComponent();

            _openTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _openTimer.Tick += (_, _) => { _openTimer.Stop(); OpenPreview(); };

            _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _closeTimer.Tick += (_, _) => { _closeTimer.Stop(); ClosePreview(); };

            // 视图离树（切面板/关窗）时别把浮层留在屏幕上
            Unloaded += (_, _) => ClosePreview();
        }

        private void OnItemMouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement item) return;

            _closeTimer.Stop();

            // 鼠标在同一个条目内部移动（移到子元素又移回来）不必重开
            if (PreviewPopup.IsOpen && ReferenceEquals(_hoveredItem, item)) return;

            _hoveredItem = item;
            _openTimer.Stop();
            _openTimer.Start();
        }

        private void OnItemMouseLeave(object sender, MouseEventArgs e)
        {
            // 还没到延时就走人了：取消待开
            _openTimer.Stop();

            // 不立刻关：鼠标可能正移向浮层（浮层与条目之间有一小段空隙）
            _closeTimer.Stop();
            _closeTimer.Start();
        }

        private void OnPreviewMouseEnter(object sender, MouseEventArgs e)
        {
            _closeTimer.Stop();

            // 给内容焦点——Popup 里的滚轮只有焦点在这一侧才路由得到（ToolTip 就卡在这）
            _focusBeforePreview = Keyboard.FocusedElement;
            Keyboard.Focus(PreviewScroll);
        }

        private void OnPreviewMouseLeave(object sender, MouseEventArgs e)
        {
            _closeTimer.Stop();
            _closeTimer.Start();
        }

        /// <summary>
        /// 兜底滚动：焦点给了之后 ScrollViewer 本就会自己滚；这里再手动滚一把，
        /// 防止某些 WPF 版本/输入源下滚轮到了 Popup 却没被 ScrollViewer 处理。
        /// 鼠标每格 e.Delta = ±120，/3 得到约 40px/格的体感。
        /// </summary>
        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled) return;

            PreviewScroll.ScrollToVerticalOffset(PreviewScroll.VerticalOffset - e.Delta / 3.0);
            e.Handled = true;
        }

        private void OpenPreview()
        {
            if (_hoveredItem == null || !_hoveredItem.IsMouseOver) return;
            if (_hoveredItem.DataContext == null) return;

            PreviewContent.Content = _hoveredItem.DataContext;
            PreviewPopup.PlacementTarget = _hoveredItem;
            PreviewPopup.IsOpen = true;
        }

        private void ClosePreview()
        {
            if (!PreviewPopup.IsOpen) return;

            PreviewPopup.IsOpen = false;
            PreviewContent.Content = null;
            _hoveredItem = null;

            if (_focusBeforePreview != null)
            {
                Keyboard.Focus(_focusBeforePreview);
                _focusBeforePreview = null;
            }
        }
    }
}
