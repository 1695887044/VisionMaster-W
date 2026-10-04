using System.Windows;
using System.Windows.Controls;

namespace Plugin.ColorRegion
{
    /// <summary>
    /// 区域颜色检查的配置视图。
    ///
    /// 视图不写业务逻辑：画框由 Core.Halcon 的 ImageEdit 负责，画布与参数的同步都在 RegionColorPlugin 里。
    /// 本文件只留五处 —— 视图就绪信号（回填上游图与采样区），以及四个按钮的转发。
    /// </summary>
    public partial class RegionColorView : UserControl
    {
        public RegionColorView()
        {
            InitializeComponent();
            Loaded += (_, _) => (DataContext as RegionColorPlugin)?.OnViewLoaded();
        }

        private RegionColorPlugin? Plugin => DataContext as RegionColorPlugin;

        private void OnClearRoiClick(object sender, RoutedEventArgs e) => Plugin?.ClearRoi();

        private void OnTryAnalyzeClick(object sender, RoutedEventArgs e) => Plugin?.TryPreviewAnalyze();

        private void OnApplyExpectedClick(object sender, RoutedEventArgs e) => Plugin?.ApplyPreviewAsExpected();
    }
}
