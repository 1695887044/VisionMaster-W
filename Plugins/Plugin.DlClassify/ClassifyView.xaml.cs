using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Plugin.DlClassify
{
    /// <summary>
    /// 深度学习分类的配置视图。
    ///
    /// 视图不写业务逻辑：模型加载、参数校验、试算都在 DlClassifyPlugin 里。
    /// 本文件只留三件事 —— 视图就绪信号、一个文件选择对话框、一个按钮转发。
    /// 文件对话框放在视图里是因为它属于 UI 职责（插件层不该弹窗）。
    /// </summary>
    public partial class ClassifyView : UserControl
    {
        public ClassifyView()
        {
            InitializeComponent();
            Loaded += (_, _) => (DataContext as DlClassifyPlugin)?.OnViewLoaded();
        }

        private DlClassifyPlugin? Plugin => DataContext as DlClassifyPlugin;

        private void OnBrowseModelClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择 ONNX 分类模型",
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

        private void OnTryClassifyClick(object sender, RoutedEventArgs e) => Plugin?.TryPreviewClassify();
    }
}
