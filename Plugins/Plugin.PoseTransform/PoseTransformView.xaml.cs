using Core.Interfaces;
using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace Plugin.PoseTransform
{
    /// <summary>
    /// 坐标变换插件配置视图（DataContext = 插件实例自身：Plugin 与 ViewModel 合一）。
    ///
    /// 挂接/卸载会通知插件（OnViewAttached/OnViewDetached）：
    /// 叠加预览只在配置窗口开着时刷新——对话框关闭后不再为"没人看的图"做重采样。
    /// </summary>
    public partial class PoseTransformView : UserControl
    {
        public PoseTransformView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 配置视图构造：灌值（Initialize）在构造里完成，与「图像采集」「标定」同一模式。
        /// </summary>
        public PoseTransformView(IStepConfigData stepData, PoseTransformPlugin plugin)
        {
            InitializeComponent();
            DataContext = plugin;

            plugin.Initialize(stepData);
            plugin.OnViewAttached();
            Unloaded += (_, _) => plugin.OnViewDetached();
        }

        /// <summary>适应窗口：走 HalconBase 的既有入口，不另造缩放逻辑。</summary>
        private void FitWindow_Click(object sender, System.Windows.RoutedEventArgs e)
            => PreviewCanvas.FitToWindow();

        /// <summary>实际像素 1:1：与右键菜单「适应图片」同一实现。</summary>
        private void FitImage_Click(object sender, System.Windows.RoutedEventArgs e)
            => PreviewCanvas.FitToImage();
    }

    /// <summary>预览头右侧的「位姿跟随 · 配置 / 像素→机械 · 配置」标签。</summary>
    public sealed class ModeLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is TransformMode mode
                ? mode switch
                {
                    TransformMode.FollowRoi => "位姿跟随 · 配置",
                    TransformMode.ToMechanical => "像素→机械 · 配置",
                    _ => "配置"
                }
                : "配置";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>
    /// 状态栏文案：插件在"已就绪"时可能把 StatusMessage 写成空串，
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
