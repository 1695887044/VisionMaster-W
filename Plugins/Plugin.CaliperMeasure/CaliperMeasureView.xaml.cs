using System.Windows.Controls;

namespace Plugin.CaliperMeasure
{
    /// <summary>
    /// 卡尺测量配置视图 —— 纯绑定层（MVVM）：
    /// 参数面板、搜索区画布、标注图预览、信息栏都由 <see cref="CaliperMeasurePlugin"/> 提供数据，
    /// 本文件只做一件事：视图就绪后通知 ViewModel 补足搜索区并算一遍预览。
    /// </summary>
    public partial class CaliperMeasureView : UserControl
    {
        public CaliperMeasureView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            (DataContext as CaliperMeasurePlugin)?.OnViewLoaded();
        }
    }
}
