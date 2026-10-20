using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Plugin.PreProcessing
{
    /// <summary>
    /// 处理链步号转换器：ListBox.AlternationIndex 从 0 起算，
    /// 而界面上要给人看"1. 2. 3."—— 序号是给人读的，不是给程序用的。
    /// </summary>
    public class StepNumberConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is int i ? (i + 1).ToString(CultureInfo.InvariantCulture) + "." : string.Empty;

        // 单向显示用不到回写；抛出不支持的异常比返回一个假值更容易在改错时暴露
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// 操作反馈文案：ActionHint 为空时显示默认提示（参数改动会自动重算预览）。
    /// 只做展示，不参与任何配置/执行逻辑。
    /// </summary>
    public sealed class ActionHintConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.IsNullOrWhiteSpace(value as string)
                ? "参数改动会自动重算预览"
                : value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>字符串非空 → Visible；空串 / null → Collapsed（LoadWarning 横幅用）。</summary>
    public sealed class StringNotEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>null → Visible；非 null → Collapsed（画布无图信息条用）。</summary>
    public sealed class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value == null ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>
    /// 根滚动容器专用：把 ScrollViewer 的视口尺寸夹到"自然最小尺寸"以上。
    /// 外层 ScrollViewer 会给内容无限测量空间，若不夹住，内部星号行列与内层 ScrollViewer 会按
    /// 全部内容撑开（实测 920×520 下 Extent 会变成 1120×1215），正常窗口也长出滚动条。
    /// 夹住后：视口 ≥ 最小尺寸时内容按视口拉伸；视口更小时内容保持最小尺寸并由外层滚动。
    /// </summary>
    public sealed class MinSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double viewport = value is double d ? d : 0d;
            double min = 0d;
            if (parameter is string text)
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out min);
            return System.Math.Max(viewport, min);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>
    /// 根内容宽度：表格式最小 914（280 + 250 + 360 + 24），卡片式最小 1074（280 + 250 + 520 + 24）。
    /// 必须随 <see cref="PreProcessingPlugin.IsCardLayout"/> 动态切换，否则 960/640 卡片式会被裁掉右侧约 130px。
    /// </summary>
    public sealed class RootWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            double viewport = values.Length > 0 && values[0] is double d ? d : 0d;
            bool card = values.Length > 1 && values[1] is bool b && b;
            double min = card ? 1074d : 914d;
            return System.Math.Max(viewport, min);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// 预处理配置视图 —— 纯绑定层（MVVM）：
    /// 算子库、处理链、参数面板、图像预览全部由 <see cref="PreProcessingPlugin"/> 提供数据与命令，
    /// 本文件只剩三件事：视图就绪后通知 ViewModel 去取输入图像；按插件配置壳的开关切换参数面板布局；
    /// 预览工具条的适应窗口 / 1:1（只调 <see cref="Core.Halcon.Controls.HalconBase"/> 的既有公开方法）。
    /// </summary>
    public partial class PreProcessingView : UserControl, UI.CustomControl.IPropertyGridLayoutSwitch
    {
        /// <summary>
        /// 三栏布局（左 280 + 预览 + 右 360）舒适展开所需的内容区尺寸。
        /// 宿主默认 960×680，内容区约 920×520，装不下；打开时把窗口"只放大不缩小"地撑到 1120×680。
        /// </summary>
        private const double ContentDesiredWidth = 1080;
        private const double ContentDesiredHeight = 520;

        // 内容区之外的固定开销：外壳标题栏 72 + 底部按钮栏 56 + ContentPresenter 上下边距 16+16
        private const double ShellChromeHeight = 72 + 56 + 16 + 16;
        // ContentPresenter 左右边距 20+20
        private const double ShellChromeWidth = 20 + 20;

        /// <summary>参数列宽度：表格式 360（默认）</summary>
        private const double FlatParamColumnWidth = 360;

        /// <summary>
        /// 卡片式的参数列宽度 520。卡片式自带左侧分组页签条 + 卡片留白，固定开销约 196px，
        /// 520 − 196 ≈ 324，接近表格式 360 的可用宽度；再宽会把中间预览压到 250px 以下。
        /// </summary>
        private const double CardParamColumnWidth = 520;

        private PreProcessingPlugin? Plugin => DataContext as PreProcessingPlugin;

        /// <summary>
        /// 参数面板布局（插件配置壳的"卡片式"开关写这里）。
        /// 状态存插件那边（两个网格的可见性绑它的 IsCardLayout / IsFlatLayout），
        /// 列宽留视图这边 —— 布局归视图。
        /// </summary>
        public bool UseCardLayout
        {
            get => Plugin?.IsCardLayout ?? false;
            set
            {
                if (Plugin is { } plugin) plugin.IsCardLayout = value;
                ParamColumn.Width = new GridLength(value ? CardParamColumnWidth : FlatParamColumnWidth);
            }
        }

        public PreProcessingView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            (DataContext as PreProcessingPlugin)?.OnViewLoaded();
            EnlargeHostWindowIfNeeded();
        }

        /// <summary>适应窗口：走 HalconBase 的既有入口，不另造缩放逻辑。</summary>
        private void FitWindow_Click(object sender, RoutedEventArgs e) => PreviewImageEdit?.FitToWindow();

        /// <summary>实际像素 1:1：与右键菜单「适应图片」同一实现。</summary>
        private void FitImage_Click(object sender, RoutedEventArgs e) => PreviewImageEdit?.FitToImage();

        /// <summary>
        /// 把承载本视图的窗口撑到能完整显示三栏的尺寸。
        ///
        /// 三条硬约束：
        /// · 只放大不缩小 —— 用户手动调小过就别再给他弹回去；
        /// · 不超过屏幕工作区 —— 否则窗口超出屏幕，"确认"按钮永远点不到，等于把自己关在里面；
        /// · 不碰宿主一行代码 —— 尺寸本来就该由使用者决定，这里只是给个合理的初始值。
        /// Loaded 会因窗口在屏幕间移动等多次触发，上面的"只放大"判断天然幂等。
        /// </summary>
        private void EnlargeHostWindowIfNeeded()
        {
            var window = Window.GetWindow(this);
            if (window == null) return;

            // 最大化时 Width/Height 只是"还原尺寸"，改了不生效，别去动
            if (window.WindowState != WindowState.Normal) return;

            var work = SystemParameters.WorkArea;
            double wantedWidth = ContentDesiredWidth + ShellChromeWidth;
            double wantedHeight = ContentDesiredHeight + ShellChromeHeight;

            if (double.IsNaN(window.Width) || window.Width < wantedWidth)
                window.Width = System.Math.Min(wantedWidth, work.Width);
            if (double.IsNaN(window.Height) || window.Height < wantedHeight)
                window.Height = System.Math.Min(wantedHeight, work.Height);

            // 撑大之后窗口可能压到任务栏外或屏幕外，把左上角夹回工作区，保证三栏与底部按钮都够得着
            double maxLeft = System.Math.Max(work.Left, work.Right - window.ActualWidth);
            double maxTop = System.Math.Max(work.Top, work.Bottom - window.ActualHeight);
            if (window.Left > maxLeft) window.Left = maxLeft;
            if (window.Top > maxTop) window.Top = maxTop;
        }
    }
}
