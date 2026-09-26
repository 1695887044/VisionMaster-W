using System.Windows;
using System.Windows.Controls;

namespace Plugin.Matching
{
    /// <summary>
    /// 模板匹配的配置视图。
    /// 视图不写业务逻辑：画框由 Core.Halcon 的 ImageEdit 负责，画布与参数的同步都在 MatchingPlugin 里。
    /// 本文件只留四处 —— 视图就绪信号（带图上屏），以及三个按钮的转发。
    /// </summary>
    public partial class MatchingView : UserControl
    {
        public MatchingView()
        {
            InitializeComponent();
            Loaded += (_, _) => (DataContext as MatchingPlugin)?.OnViewLoaded();
        }

        private MatchingPlugin? Plugin => DataContext as MatchingPlugin;

        private void OnLoadImageClick(object sender, RoutedEventArgs e) => Plugin?.LoadPreviewImage();

        private void OnCreateTemplateClick(object sender, RoutedEventArgs e) => Plugin?.CreateTemplate();

        private void OnClearTemplateClick(object sender, RoutedEventArgs e) => Plugin?.ClearTemplate();

        private void OnClearSmearClick(object sender, RoutedEventArgs e) => Plugin?.ClearSmear();

        private void OnAddEntryClick(object sender, RoutedEventArgs e) => Plugin?.AddTemplateEntry();

        private void OnDeleteEntryClick(object sender, RoutedEventArgs e) => Plugin?.DeleteSelectedEntry();

        private void OnSetDefaultClick(object sender, RoutedEventArgs e) => Plugin?.SetAsDefault();
    }
}
