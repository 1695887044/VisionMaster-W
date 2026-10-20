using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Core.Halcon.Controls;
using Microsoft.Win32;

namespace Plugin.Yolo
{
    /// <summary>
    /// YOLO 目标检测的配置视图（Web 化 v1）。
    ///
    /// 视图不写业务逻辑：模型加载、参数校验、试算都在 YoloPlugin 里。
    /// 本文件只保留四件事 —— 视图就绪信号、两个文件选择/试算的 UI 命令、两个预览缩放转发、
    /// 以及纯展示用的文件名转换器。
    ///
    /// 状态（空态/错误态/结果卡配色）一律绑插件的 ModelState / PreviewState，
    /// **不再从 ModelSummary / ResultSummary / Hint 的中文文案倒推**：文案是给人看的，不是判据。
    ///
    /// 说明：视图本地命令只是把原来的 Click 处理器换成 ICommand，
    /// 行为仍是调用 YoloPlugin 的既有公开方法，不新增业务逻辑。
    /// </summary>
    public partial class YoloView : UserControl
    {
        public YoloView()
        {
            // 先建命令再 InitializeComponent：XAML 里的 RelativeSource 绑定会在解析时读这两个属性，
            // 若先解析后赋值，绑定会拿到 null 且不会因普通 CLR 属性赋值而刷新。
            BrowseModelCommand = new RelayCommand(BrowseModel);
            TryDetectCommand = new RelayCommand(() => Plugin?.TryPreviewDetect());

            InitializeComponent();
            Loaded += (_, _) => Plugin?.OnViewLoaded();
        }

        /// <summary>选择 ONNX 模型：文件对话框属于 UI 职责，插件层不弹窗。</summary>
        public ICommand BrowseModelCommand { get; }

        /// <summary>用当前 DisplayImage 试算：转发既有 TryPreviewDetect()。</summary>
        public ICommand TryDetectCommand { get; }

        private YoloPlugin? Plugin => DataContext as YoloPlugin;

        private void BrowseModel()
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择 ONNX 检测模型",
                Filter = "ONNX 模型|*.onnx|所有文件|*.*",
                CheckFileExists = true,
            };

            if (dialog.ShowDialog() == true && Plugin != null)
            {
                Plugin.ModelPath = dialog.FileName;
                // 模型换了 → 重新读一次元数据，让摘要立刻反映新模型（选错模型时这一步就是线索）
                Plugin.OnViewLoaded();
            }
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
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
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
    /// 预览头文件名：ModelPath 取文件名；空路径给「检测预览」。
    /// 纯展示转换，不参与任何配置/执行逻辑。
    /// </summary>
    public sealed class ModelFileNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var path = value as string;
            if (string.IsNullOrWhiteSpace(path))
                return "检测预览";

            try
            {
                var name = Path.GetFileName(path);
                return string.IsNullOrEmpty(name) ? path : name;
            }
            catch
            {
                return path;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>
    /// 视图本地命令：只转发既有公开方法，不引入独立 VM。
    /// </summary>
    internal sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object? parameter) => _execute();
    }
}
