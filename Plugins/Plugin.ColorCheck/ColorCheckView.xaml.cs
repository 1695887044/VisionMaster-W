using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Core.Commands;

namespace Plugin.ColorCheck
{
    /// <summary>
    /// 颜色序列检查的配置视图（Web 基线 v1 复刻）。
    ///
    /// 视图不写业务逻辑：ROI 由 Core.Halcon 的 ImageEdit 负责，颜色序列分析全在 ColorCheckPlugin 里。
    /// 本文件只保留三件事：
    ///   ① 三个既有公开方法的命令转发（ClearRoi / TryPreviewAnalyze / SavePreviewAsRecipe）；
    ///   ② 画布工具条的 FitToWindow / FitToImage 转发；
    ///   ③ 纯展示转换器（采样区状态、结果块四色、投射窗口显示名）。
    /// 没有独立 VM：DataContext 仍是插件实例，宿主靠它取 IPluginConfigView.OnConfirm。
    /// </summary>
    public partial class ColorCheckView : UserControl
    {
        /// <summary>清空采样区：转发插件的 ClearRoi()，不复制任何 ROI 逻辑。</summary>
        public ICommand ClearRoiCommand { get; }

        /// <summary>
        /// 试算：转发插件的 TryPreviewAnalyze(HImage)，把**画布当前显示的那张图**交过去。
        ///
        /// 为什么传 ImageEditor.HImage 而不是让插件自己用 DisplayImage：
        /// 画布右键「打开图片」换的是控件自己的 HImage（共享控件 HalconBase.OpenImage 直接写
        /// <c>this.HImage</c>），而 DisplayImage 是插件持有的上游副本 —— 两者会分叉。
        /// 传画布当前图，用户右键打开的图就能直接试算；插件侧只读这张图，不改 DisplayImage 所有权。
        /// </summary>
        public ICommand TryAnalyzeCommand { get; }

        /// <summary>把试算结果存成配方：转发插件的 SavePreviewAsRecipe()。</summary>
        public ICommand SaveRecipeCommand { get; }

        public ColorCheckView()
        {
            ClearRoiCommand = new RelayCommand(_ => Plugin?.ClearRoi());
            // 传画布当前显示的那张图：右键「打开图片」换的图也能直接试算（插件侧只读，不接管所有权）
            TryAnalyzeCommand = new RelayCommand(_ => Plugin?.TryPreviewAnalyze(ImageEditor?.HImage));
            SaveRecipeCommand = new RelayCommand(_ => SaveRecipe());

            InitializeComponent();
            Loaded += (_, _) => Plugin?.OnViewLoaded();
        }

        private ColorCheckPlugin? Plugin => DataContext as ColorCheckPlugin;

        /// <summary>
        /// SavePreviewAsRecipe 写的是普通自动属性 RecipeText（无 INotifyPropertyChanged），
        /// 因此命令执行后让输入框重新读一次源，保证「存成配方」立刻可见。
        /// 只刷新显示，不改变插件的属性与持久化语义。
        /// </summary>
        private void SaveRecipe()
        {
            Plugin?.SavePreviewAsRecipe();
            RecipeTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        }

        /// <summary>适应窗口：走 HalconBase 的既有入口，不另造缩放逻辑。</summary>
        private void FitWindow_Click(object sender, RoutedEventArgs e) => ImageEditor.FitToWindow();

        /// <summary>实际像素 1:1：与右键菜单「适应图片」同一实现。</summary>
        private void FitImage_Click(object sender, RoutedEventArgs e) => ImageEditor.FitToImage();

        /// <summary>
        /// 窄档（宿主 640 窗口的内容区约 600px）只收起辅助文字，不动任何控件与绑定：
        /// 画布头副标题、状态栏右侧 ROI 摘要让位给主内容。
        /// </summary>
        private void OnViewSizeChanged(object sender, SizeChangedEventArgs e)
        {
            bool narrow = e.NewSize.Width < 640;
            if (CanvasSubtitle != null)
                CanvasSubtitle.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
            if (RoiSummaryText != null)
                RoiSummaryText.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>
    /// 采样区状态的中文显示：只读 <c>HasRoi</c>（= RoiParams.Length == 5）布尔，不解析任何中文文案。
    /// </summary>
    public sealed class RoiStateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool hasRoi && hasRoi ? "已框选" : "未框选";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>
    /// 结果块四色：只按 ResultSummary 的文案给颜色，不新增 StatusLevel。
    /// 插件本身没有状态枚举；这里只做「看得见的提示」，不参与任何判定。
    /// </summary>
    public sealed class ResultToneConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string text = value as string ?? string.Empty;

            if (text.StartsWith("试算失败", StringComparison.Ordinal)
                || text.Contains("判定：不合格", StringComparison.Ordinal))
                return "Error";

            if (text.Contains("判定：合格", StringComparison.Ordinal))
                return "Success";

            if (text.StartsWith("还没有图像", StringComparison.Ordinal))
                return "Warning";

            return "Info";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>投射视图窗口的显示名：0 = 不投射，1~9 = 视图窗口 N。绑定值仍是 int。</summary>
    public sealed class ViewIndexLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is int index && index > 0 ? $"视图窗口 {index}" : "不投射";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
