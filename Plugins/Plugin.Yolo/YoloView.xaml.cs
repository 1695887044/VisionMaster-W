using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Plugin.Yolo
{
    /// <summary>
    /// YOLO 目标检测的配置视图。
    ///
    /// 视图不写业务逻辑：模型加载、参数校验、试算都在 YoloPlugin 里。
    /// 本文件只留三件事 —— 视图就绪信号、两个文件选择对话框、两个按钮转发。
    /// 文件对话框放在视图里是因为它属于 UI 职责（插件层不该弹窗）。
    /// </summary>
    public partial class YoloView : UserControl
    {
        public YoloView()
        {
            InitializeComponent();
            Loaded += (_, _) => (DataContext as YoloPlugin)?.OnViewLoaded();
        }

        private YoloPlugin? Plugin => DataContext as YoloPlugin;

        private void OnBrowseModelClick(object sender, RoutedEventArgs e)
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

        private void OnBrowsePreviewClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择示意图",
                Filter = "图像文件|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|所有文件|*.*",
                CheckFileExists = true,
            };

            if (dialog.ShowDialog() == true && Plugin != null)
                Plugin.PreviewImagePath = dialog.FileName;
        }

        private void OnLoadPreviewClick(object sender, RoutedEventArgs e) => Plugin?.LoadPreviewImage();

        private void OnTryDetectClick(object sender, RoutedEventArgs e) => Plugin?.TryPreviewDetect();
    }
}
