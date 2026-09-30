using System.Windows.Controls;

namespace Plugin.BlobDetect
{
    /// <summary>
    /// Blob 缺陷检测配置视图 —— 纯绑定层（MVVM）：
    /// 参数面板、标注图预览、信息栏、缺陷清单都由 <see cref="BlobDetectPlugin"/> 提供数据，
    /// 本文件只做两件事：视图就绪后通知 ViewModel 取输入图算预览；
    /// 缺陷清单的行选中转发给 ViewModel 做预览高亮联动。
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

        /// <summary>缺陷清单行选中 → 预览图上给那根缺陷套黄框（编号与清单行一一对应）</summary>
        private void DefectList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not BlobDetectPlugin vm) return;

            // 追加进来的就是新选中的行（单选模式最多一条）
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is DefectRow row)
                vm.HighlightDefect(row.Index);
        }
    }
}
