using System.Windows.Controls;

namespace Plugin.BlobDetect
{
    /// <summary>
    /// Blob 缺陷检测配置视图 —— 纯绑定层（MVVM）：
    /// 参数面板、标注图预览、信息栏都由 <see cref="BlobDetectPlugin"/> 提供数据，
    /// 本文件只做一件事：视图就绪后通知 ViewModel 去取输入图像并算一遍预览。
    /// </summary>
    public partial class BlobDetectView : UserControl
    {
        public BlobDetectView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            (DataContext as BlobDetectPlugin)?.OnViewLoaded();
        }
    }
}
