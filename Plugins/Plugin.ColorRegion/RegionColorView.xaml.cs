using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Core.Commands;
using Core.Halcon.Color;

namespace Plugin.ColorRegion
{
    /// <summary>
    /// 区域颜色检查的配置视图（Web 基线 v1 复刻）。
    ///
    /// 视图不写业务逻辑：ROI 由 Core.Halcon 的 ImageEdit 负责，颜色分析全在 RegionColorPlugin 里。
    /// 本文件只保留三件事：
    ///   ① 三个既有公开方法的命令转发（ClearRoi / TryPreviewAnalyze / ApplyPreviewAsExpected）；
    ///   ② 画布工具条的 FitToWindow / FitToImage 转发；
    ///   ③ 纯展示转换器（形状中文名、结果块三态着色）。
    /// 没有独立 VM：DataContext 仍是插件实例，宿主靠它取 IPluginConfigView.OnConfirm。
    /// </summary>
    public partial class RegionColorView : UserControl
    {
        /// <summary>清空采样区：转发插件的 ClearRoi()，不复制任何 ROI 逻辑。</summary>
        public ICommand ClearRoiCommand { get; }

        /// <summary>试算：转发插件的 TryPreviewAnalyze()。</summary>
        public ICommand TryAnalyzeCommand { get; }

        /// <summary>把主色填成期望颜色：转发插件的 ApplyPreviewAsExpected()。</summary>
        public ICommand ApplyExpectedCommand { get; }

        public RegionColorView()
        {
            ClearRoiCommand = new RelayCommand(_ => Plugin?.ClearRoi());
            TryAnalyzeCommand = new RelayCommand(_ => Plugin?.TryPreviewAnalyze());
            ApplyExpectedCommand = new RelayCommand(_ => ApplyExpected());

            InitializeComponent();
            Loaded += (_, _) => Plugin?.OnViewLoaded();
            SizeChanged += (_, _) => UpdateNarrowLayout();
        }

        private RegionColorPlugin? Plugin => DataContext as RegionColorPlugin;

        /// <summary>
        /// 窄档（宿主内容区约 600px，阈值 620）折叠状态栏右侧的采样区摘要，
        /// 把宽度让给 Hint。只在视图层显隐，不改 RoiSummary 本身与任何业务状态。
        /// </summary>
        private void UpdateNarrowLayout()
        {
            if (RoiSummaryText == null) return;
            RoiSummaryText.Visibility = ActualWidth < 620 ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>
        /// ApplyPreviewAsExpected 写 ExpectedColors 后插件会发 PropertyChanged（CRG2）；
        /// 这里仍让输入框重读一次源，作为不依赖通知路径的无害兜底。
        /// 只刷新显示，不改变插件的属性与持久化语义。
        /// </summary>
        private void ApplyExpected()
        {
            Plugin?.ApplyPreviewAsExpected();
            ExpectedColorsBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        }

        /// <summary>适应窗口：走 HalconBase 的既有入口，不另造缩放逻辑。</summary>
        private void FitWindow_Click(object sender, RoutedEventArgs e) => ImageEditor.FitToWindow();

        /// <summary>实际像素 1:1：与右键菜单「适应图片」同一实现。</summary>
        private void FitImage_Click(object sender, RoutedEventArgs e) => ImageEditor.FitToImage();
    }

    /// <summary>采样区形状的中文显示名。RoiShape 本身仍是 Rectangle / Circle / Ellipse 字符串。</summary>
    public sealed class RoiShapeLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value as string) switch
            {
                RoiShapeNames.Circle => "圆形",
                RoiShapeNames.Ellipse => "椭圆",
                _ => "矩形",
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>
    /// 结果块三态：只按 ResultSummary 的文案给颜色，不新增 StatusLevel。
    /// 插件本身没有状态枚举；这里只做「看得见的提示」，不参与任何判定。
    /// </summary>
    public sealed class ResultToneConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string text = value as string ?? string.Empty;

            if (text.StartsWith("试算失败", StringComparison.Ordinal))
                return "Error";

            if (text.StartsWith("还没有图像", StringComparison.Ordinal)
                || text.Contains("尚未配置期望颜色", StringComparison.Ordinal)
                || text.Contains("无法判定", StringComparison.Ordinal))
                return "Warning";

            return "Info";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>投射视图窗口的显示名：0 = 不投射，1~9 = 视图窗口 N。绑定值仍是 int。</summary>
    public sealed class ViewIndexLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is int index && index > 0 ? $"视图窗口 {index}" : "不投射";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
