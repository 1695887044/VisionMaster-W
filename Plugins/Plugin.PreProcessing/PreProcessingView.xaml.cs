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
    /// 预处理配置视图 —— 纯绑定层（MVVM）：
    /// 算子库、处理链、参数面板、图像预览全部由 <see cref="PreProcessingPlugin"/> 提供数据与命令，
    /// 本文件只剩两件事：视图就绪后通知 ViewModel 去取输入图像；按插件配置壳的开关切换参数面板布局。
    /// </summary>
    public partial class PreProcessingView : UserControl, UI.CustomControl.IPropertyGridLayoutSwitch
    {
        /// <summary>
        /// 三栏布局（算子库 210 + 预览 + 链与参数 360）舒适展开所需的内容区尺寸。
        /// 外壳 PluginConfigShell 的窗口写死 880×600，装不下，所以打开时要把窗口撑开。
        /// </summary>
        private const double ContentDesiredWidth = 1120;
        private const double ContentDesiredHeight = 620;

        // 内容区之外的固定开销：外壳标题栏 50 + 底部按钮栏 50 + ContentPresenter 上下各 8 边距
        private const double ShellChromeHeight = 50 + 50 + 8 + 8;
        private const double ShellChromeWidth = 8 + 8;

        /// <summary>参数列宽度：表格式 360（现状）</summary>
        private const double FlatParamColumnWidth = 360;

        /// <summary>
        /// 卡片式的参数列宽度 540。为什么要多这 180：
        /// 卡片式自带左侧分组页签条 + 卡片留白，固定开销约 196px（表格式几乎没有），
        /// 540 − 196 ≈ 344，才与表格式的 360 全宽基本等价（未减重前只有 248，比表格式还窄）。
        /// 宽度是算得出来的：内容区 1120 − 算子库 240 − 参数列 540 − 三处边距 32 = 308，
        /// 预览列 MinWidth=300 兜得住 —— 正好卡在线上，所以这三个数改动前要先重算一遍。
        /// </summary>
        private const double CardParamColumnWidth = 540;

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

            // 撑大之后窗口可能压到任务栏外或屏幕外，把左上角夹回工作区，保证四栏与底部按钮都够得着
            double maxLeft = System.Math.Max(work.Left, work.Right - window.ActualWidth);
            double maxTop = System.Math.Max(work.Top, work.Bottom - window.ActualHeight);
            if (window.Left > maxLeft) window.Left = maxLeft;
            if (window.Top > maxTop) window.Top = maxTop;
        }
    }
}
