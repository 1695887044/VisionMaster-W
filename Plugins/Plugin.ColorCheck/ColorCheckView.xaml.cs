using System.Windows;
using System.Windows.Controls;

namespace Plugin.ColorCheck
{
    /// <summary>
    /// 颜色序列检查的配置视图。
    ///
    /// 视图不写业务逻辑：框选由 Core.Halcon 的 ImageEdit 控件负责，画布集合与形状参数的双向同步
    /// 都在 ColorCheckPlugin 里。本文件只留三处 —— 视图就绪信号（回填示意图与采样区），
    /// 以及三个按钮的转发（载入示意图 / 清空采样区 / 试算）。
    /// </summary>
    public partial class ColorCheckView : UserControl
    {
        public ColorCheckView()
        {
            InitializeComponent();
            Loaded += (_, _) => (DataContext as ColorCheckPlugin)?.OnViewLoaded();
        }

        private ColorCheckPlugin? Plugin => DataContext as ColorCheckPlugin;

        private void OnLoadPreviewClick(object sender, RoutedEventArgs e) => Plugin?.LoadPreviewImage();

        private void OnClearRoiClick(object sender, RoutedEventArgs e) => Plugin?.ClearRoi();

        private void OnTryAnalyzeClick(object sender, RoutedEventArgs e) => Plugin?.TryPreviewAnalyze();

        private void OnSaveRecipeClick(object sender, RoutedEventArgs e) => Plugin?.SavePreviewAsRecipe();
    }
}
