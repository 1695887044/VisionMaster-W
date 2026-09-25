using System.Windows;
using System.Windows.Controls;

namespace Plugin.Ocr
{
    /// <summary>
    /// OCR 文本识别的配置视图。
    ///
    /// 视图不写业务逻辑：框选由 Core.Halcon 的 ImageEdit 控件负责，画布集合与形状参数的双向同步
    /// 都在 OcrPlugin 里。本文件只留三处 —— 视图就绪信号（回填示意图与识别区），
    /// 以及三个按钮的转发（载入示意图 / 清空识别区 / 试算）。
    /// </summary>
    public partial class OcrView : UserControl
    {
        public OcrView()
        {
            InitializeComponent();
            Loaded += (_, _) => (DataContext as OcrPlugin)?.OnViewLoaded();
        }

        private OcrPlugin? Plugin => DataContext as OcrPlugin;

        private void OnLoadPreviewClick(object sender, RoutedEventArgs e) => Plugin?.LoadPreviewImage();

        private void OnClearRoiClick(object sender, RoutedEventArgs e) => Plugin?.ClearRoi();

        private void OnTryRecognizeClick(object sender, RoutedEventArgs e) => Plugin?.TryPreviewRecognize();
    }
}
