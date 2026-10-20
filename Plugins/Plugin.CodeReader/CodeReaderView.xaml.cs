using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Core.Commands;
using Core.Halcon.Controls;

namespace Plugin.CodeReader
{
    /// <summary>
    /// 码读取的配置视图。
    ///
    /// 视图不写业务逻辑：码制/极性/超时的绑定与 Is2D 可见性切换全在 CodeReaderPlugin 上。
    /// 本文件只保留视图就绪信号、试算命令转发、预览 Fit 入口与结果区配色转换器。
    /// </summary>
    public partial class CodeReaderView : UserControl
    {
        /// <summary>
        /// 试算命令：只转发插件的既有公开方法，不复制任何读码逻辑。
        /// 用命令而不是 Click，是为了让按钮在宿主/冒烟里都能通过 Command 契约发现。
        /// </summary>
        public ICommand TryRecognizeCommand { get; }

        public CodeReaderView()
        {
            TryRecognizeCommand = new RelayCommand(_ => Plugin?.TryPreviewRecognize());
            InitializeComponent();
            Loaded += (_, _) => Plugin?.OnViewLoaded();
            SizeChanged += (_, _) => UpdateNarrowLayout();
        }

        private CodeReaderPlugin? Plugin => DataContext as CodeReaderPlugin;

        /// <summary>
        /// 窄档（宿主内容区约 600px，阈值 620）折叠状态栏右侧的码制名，
        /// 把宽度让给 Hint。只做显隐，不改 Symbology 与任何业务状态。
        /// </summary>
        private void UpdateNarrowLayout()
        {
            if (SymbologyText == null) return;
            SymbologyText.Visibility = ActualWidth < 620 ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>适应窗口：走 HalconBase 的既有入口，不另造缩放逻辑。</summary>
        private void FitWindow_Click(object sender, RoutedEventArgs e)
            => FindPreviewControl()?.FitToWindow();

        /// <summary>实际像素 1:1：与右键菜单「适应图片」同一实现。</summary>
        private void FitImage_Click(object sender, RoutedEventArgs e)
            => FindPreviewControl()?.FitToImage();

        /// <summary>
        /// 预览控件由 ContentTemplate 承载（有图才创建），不在视图 namescope 里；
        /// 这里从 PreviewHost 的可视树里取当前实例。无图时返回 null，按钮静默空跑。
        /// </summary>
        private ImageReadOnly? FindPreviewControl()
            => FindVisualChild<ImageReadOnly>(PreviewHost);

        private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                    return match;

                var nested = FindVisualChild<T>(child);
                if (nested != null)
                    return nested;
            }
            return null;
        }
    }

    /// <summary>
    /// 码制显示名：存盘值是 HALCON 原始名（如 "Data Matrix ECC 200"），状态栏显示码制表里的
    /// DisplayName；查不到时原样回退存盘值（不隐藏配置错误，与 SymbologyHint 的口径一致）。
    /// </summary>
    public sealed class CodeSymbologyDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string halconName = value as string ?? string.Empty;
            return CodeSymbologyTable.TryGet(halconName, out var symbology)
                ? symbology.DisplayName
                : halconName;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>
    /// 结果区/状态点的视觉配色：只读 ResultSummary 文本前缀，不新增任何状态属性或枚举。
    ///
    /// 映射口径（与插件现有文案一致）：
    ///   · “试算失败：…” → 危险
    ///   · “读到 0 个码” → 警示（未读到码是正常工况，不是失败）
    ///   · “码制：…”     → 成功
    ///   · 其它（尚未试算 / 无图提示…）→ 中性
    ///
    /// 颜色全部从公共令牌取，视图 XAML 不写十六进制；取不到资源时退回透明/中性，
    /// 不影响任何业务路径（纯展示转换）。
    /// </summary>
    public sealed class ResultSummaryBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var text = value as string ?? string.Empty;

            var level =
                text.StartsWith("试算失败", StringComparison.Ordinal) ? "err"
                : text.Contains("读到 0 个码", StringComparison.Ordinal) ? "warn"
                : text.StartsWith("码制：", StringComparison.Ordinal) ? "ok"
                : "neutral";

            var role = parameter as string ?? "Accent";

            var key = role switch
            {
                "Background" => level switch
                {
                    "ok" => "DialogSuccessSubtleBrush",
                    "warn" => "DialogWarningSubtleBrush",
                    "err" => "DialogDangerSubtleBrush",
                    _ => "DialogSurfaceBrush",
                },
                "Foreground" => level switch
                {
                    "ok" => "DialogSuccessTextBrush",
                    "warn" => "DialogWarningTextBrush",
                    "err" => "DialogDangerTextBrush",
                    _ => "DialogTextSecondaryBrush",
                },
                "Dot" => level switch
                {
                    "ok" => "DialogSuccessBrush",
                    "warn" => "DialogWarningBrush",
                    "err" => "DialogDangerBrush",
                    _ => "DialogTextMutedBrush",
                },
                _ => level switch
                {
                    "ok" => "DialogSuccessBrush",
                    "warn" => "DialogWarningBrush",
                    "err" => "DialogDangerBrush",
                    _ => "DialogBorderStrongBrush",
                },
            };

            try
            {
                return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;
            }
            catch
            {
                return Brushes.Transparent;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
