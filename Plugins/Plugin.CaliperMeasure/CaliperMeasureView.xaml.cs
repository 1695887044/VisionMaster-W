using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Plugin.CaliperMeasure
{
    /// <summary>
    /// 卡尺测量配置视图 —— 纯绑定层（MVVM）：
    /// 参数面板、搜索区画布、标注图预览、信息栏都由 <see cref="CaliperMeasurePlugin"/> 提供数据。
    /// 本文件只做四件事：
    ///  ① 视图就绪后通知 ViewModel 补足搜索区并算一遍预览（原行为）；
    ///  ② 预览工具条「适应窗口 / 1:1」调用 <c>ImageEdit</c> 既有的公开方法；
    ///  ③ 窄档收敛预览头/状态栏的次要文字（纯布局）；
    ///  ④ 状态文案空串兜底（仅显示层，不改插件文案）。
    /// 不引入独立 VM、不新增业务逻辑。
    /// </summary>
    public partial class CaliperMeasureView : UserControl
    {
        public CaliperMeasureView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            SizeChanged += OnSizeChanged;
            UpdateNarrowLayout();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            (DataContext as CaliperMeasurePlugin)?.OnViewLoaded();
        }

        /// <summary>
        /// 窄档收敛：宿主 640 内容区约 600px 时，预览头只保留标题 + 判定胶囊 + 工具条，
        /// 状态栏只保留消息（隐藏类型文字与「卡尺 N 把 · 搜索区 M 个」摘要）。
        /// 纯布局处理，不碰任何业务属性。
        /// </summary>
        private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateNarrowLayout();

        private void UpdateNarrowLayout()
        {
            var visibility = ActualWidth > 0 && ActualWidth < 720
                ? Visibility.Collapsed
                : Visibility.Visible;

            if (PreviewKindText != null) PreviewKindText.Visibility = visibility;
            if (PreviewKindSeparator != null) PreviewKindSeparator.Visibility = visibility;
            if (StatusSummary != null) StatusSummary.Visibility = visibility;
        }

        /// <summary>适应窗口（既有 HalconBase.FitToWindow，不新增缩放逻辑）</summary>
        private void FitWindow_Click(object sender, RoutedEventArgs e) => ImageEditor.FitToWindow();

        /// <summary>实际像素 1:1（既有 HalconBase.FitToImage，不新增缩放逻辑）</summary>
        private void FitImage_Click(object sender, RoutedEventArgs e) => ImageEditor.FitToImage();
    }

    /// <summary>
    /// 状态文案空串兜底：配置视图在 Loaded 之前 StatusMessage 可能还是空串；
    /// 只影响显示，不改插件写入的原文（与 ImageAcquisition V2 的 StatusMessageConverter 同口径）。
    /// </summary>
    public class StatusTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.IsNullOrWhiteSpace(value as string) ? "等待输入图像…" : value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// 搜索区数量是否不足（纯显示判定）：CaliperRegions.Count &lt; RequiredRegionCount。
    /// 只驱动提示条配色/图标，不参与测量逻辑（CM1：VM 不暴露布尔，视图层自算）。
    /// </summary>
    public class RegionCountWarningConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2) return false;
            int count = values[0] is int c ? c : 0;
            int required = values[1] is int r ? r : 0;
            return required > 0 && count < required;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// 状态点是否应显示绿色（CM2）：仅 StatusLevel=Info 且 StatusMessage 非空。
    /// 其余情况（包括初始未加载、空消息）保持中性色，避免"还没跑就乐观绿"。
    /// </summary>
    public class StatusDotSuccessConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2) return false;
            return values[0] is StatusLevel level
                && level == StatusLevel.Info
                && !string.IsNullOrWhiteSpace(values[1] as string);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
