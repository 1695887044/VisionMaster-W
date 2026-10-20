using Core.Interfaces;
using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Plugin.Calibration
{
    /// <summary>
    /// 标定插件配置视图（DataContext = 插件实例自身：Plugin 与 ViewModel 合一）。
    ///
    /// 画布交互全部走控件既有通道（ActiveRoi / DrawObjectList / HTuples 回写），
    /// 「图上取点」是唯一的例外——<c>HalconBase.IsPickMode</c> 的 opt-in 通道
    /// （默认关、仅取点待命时开启），事件在本文件转交插件（事件无法走绑定）。
    /// </summary>
    public partial class CalibrationView : UserControl
    {
        private CalibrationPlugin? _plugin;

        public CalibrationView()
        {
            InitializeComponent();
            SizeChanged += OnSizeChanged;
            UpdateNarrowLayout();
        }

        /// <summary>
        /// 配置视图构造：灌值（Initialize）在构造里完成，与「图像采集」插件同一模式。
        /// </summary>
        public CalibrationView(IStepConfigData stepData, CalibrationPlugin plugin)
        {
            InitializeComponent();
            _plugin = plugin;
            DataContext = plugin;
            plugin.Initialize(stepData);
            SizeChanged += OnSizeChanged;
            UpdateNarrowLayout();
            Unloaded += (_, _) => { _plugin?.CancelPick(); _plugin = null; };
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateNarrowLayout();

        /// <summary>
        /// 窄档收敛：宿主 640 内容区约 600px 时，状态栏只保留消息、画布头只保留第一角标。
        /// 纯布局处理，不碰任何业务属性。
        /// </summary>
        private void UpdateNarrowLayout()
        {
            var visibility = ActualWidth > 0 && ActualWidth < 720
                ? Visibility.Collapsed
                : Visibility.Visible;

            if (StatusPath != null) StatusPath.Visibility = visibility;
            if (CanvasHintChip != null) CanvasHintChip.Visibility = visibility;
        }

        /// <summary>画布取点事件 → 插件（待命态下一次点击回填目标行 / A / B）</summary>
        private void OnImagePicked(object sender, Core.Halcon.Controls.HalconBase.ImagePickEventArgs e)
            => _plugin?.ApplyPickedPoint(e.Row, e.Column);

        /// <summary>Esc 取消取点待命（与"再点一次按钮取消"等效）</summary>
        private void OnViewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
                _plugin?.CancelPick();
        }
    }

    /// <summary>画布头文件名：空路径显示「未载入标定图」，否则只显示文件名。</summary>
    public sealed class FileNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string path = value as string ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path))
                return "未载入标定图";

            string name = Path.GetFileName(path);
            return string.IsNullOrWhiteSpace(name) ? path : name;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>状态消息：空串显示「就绪」，不改变插件写入的三档级别。</summary>
    public sealed class StatusMessageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string message = value as string ?? string.Empty;
            return string.IsNullOrWhiteSpace(message) ? "就绪" : message;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// 链接态 → 可见性：已链接（true）隐藏外置浏览按钮，未链接（false）显示。
    /// 只影响 UI，不改变 LinkableValueEditor 的链接/解除语义。
    /// </summary>
    public sealed class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
