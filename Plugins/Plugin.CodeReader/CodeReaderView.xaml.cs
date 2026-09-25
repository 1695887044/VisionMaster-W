using System.Windows;
using System.Windows.Controls;

namespace Plugin.CodeReader
{
    /// <summary>
    /// 码读取的配置视图。
    ///
    /// 视图不写业务逻辑：码制/参数的下拉绑定与可见性切换全在 CodeReaderPlugin 的属性上，
    /// 本文件只留视图就绪信号与两个按钮的转发（载入示意图 / 试算）。
    /// </summary>
    public partial class CodeReaderView : UserControl
    {
        public CodeReaderView()
        {
            InitializeComponent();
            Loaded += (_, _) => (DataContext as CodeReaderPlugin)?.OnViewLoaded();
        }

        private CodeReaderPlugin? Plugin => DataContext as CodeReaderPlugin;

        private void OnLoadPreviewClick(object sender, RoutedEventArgs e) => Plugin?.LoadPreviewImage();

        private void OnTryRecognizeClick(object sender, RoutedEventArgs e) => Plugin?.TryPreviewRecognize();
    }
}
