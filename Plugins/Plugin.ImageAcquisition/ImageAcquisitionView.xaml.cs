using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Core.Halcon.Controls;
using Core.Interfaces;

namespace Plugin.ImageAcquisition
{
    public partial class ImageAcquisitionView : UserControl
    {
        public ImageAcquisitionView()
        {
        }

        /// <summary>
        /// 配置视图构造：DataContext = 插件实例自身（Plugin 与 ViewModel 合一）
        /// </summary>
        public ImageAcquisitionView(IStepConfigData stepData, ImageAcquisitionPlugin plugin)
        {
            InitializeComponent();
            DataContext = plugin;
            plugin.Initialize(stepData);
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

        /// <summary>相机空态里的「选择相机」：只把焦点移回左侧下拉，不新增导航契约。</summary>
        private void FocusCamera_Click(object sender, RoutedEventArgs e)
        {
            CameraCombo.Focus();
            CameraCombo.IsDropDownOpen = true;
        }

        /// <summary>网络推送空态里的「推送测试」：展开左侧「调试与接口」，不新增命令。</summary>
        private void OpenDebug_Click(object sender, RoutedEventArgs e)
            => DebugExpander.IsExpanded = true;
    }

    /// <summary>
    /// 预览头文件名：路径取文件名；相机/网络伪路径给可读标题；空路径给「预览」。
    /// 纯展示转换，不参与任何配置/执行逻辑。
    /// </summary>
    public sealed class FileNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var path = value as string;
            if (string.IsNullOrWhiteSpace(path))
                return "预览";
            if (path.StartsWith("camera://", StringComparison.OrdinalIgnoreCase))
                return "相机预览";
            if (path.StartsWith("hub://", StringComparison.OrdinalIgnoreCase))
                return "等待网络图像";

            var name = Path.GetFileName(path);
            return string.IsNullOrEmpty(name) ? path : name;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>预览头右侧的「文件 / 目录 / 网络 / 相机 · 只读」标签。</summary>
    public sealed class ModeLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is AcquisitionMode mode
                ? mode switch
                {
                    AcquisitionMode.SingleFile => "文件 · 只读",
                    AcquisitionMode.Folder => "目录 · 只读",
                    AcquisitionMode.Hub => "网络 · 只读",
                    AcquisitionMode.Camera => "相机 · 只读",
                    _ => "只读"
                }
                : "只读";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>
    /// 状态栏文案：插件在"路径有效"时会把 StatusMessage 清空（SetStatus("")），
    /// 空串在界面上显示为「就绪」；不改变插件的状态语义。
    /// </summary>
    public sealed class StatusMessageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.IsNullOrWhiteSpace(value as string) ? "就绪" : value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
