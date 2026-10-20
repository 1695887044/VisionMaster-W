using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Plugin.BlobDetect
{
    /// <summary>
    /// Blob 缺陷检测配置视图 —— 纯绑定层（MVVM）：
    /// 参数面板、标注图预览、信息栏、缺陷清单都由 <see cref="BlobDetectPlugin"/> 提供数据，
    /// 本文件只做三件事：视图就绪后通知 ViewModel 取输入图算预览；
    /// 缺陷清单的行选中转发给 ViewModel 做预览高亮联动；
    /// 预览工具条的「适应窗口 / 1:1」调用 HalconBase 的既有公开方法。
    /// </summary>
    public partial class BlobDetectView : UserControl
    {
        public BlobDetectView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            (DataContext as BlobDetectPlugin)?.OnViewLoaded();
        }

        /// <summary>适应窗口：走 HalconBase 的既有入口，不另造缩放逻辑。</summary>
        private void FitWindow_Click(object sender, RoutedEventArgs e)
            => PreviewCanvas.FitToWindow();

        /// <summary>实际像素 1:1：与右键菜单「适应图片」同一实现。</summary>
        private void FitImage_Click(object sender, RoutedEventArgs e)
            => PreviewCanvas.FitToImage();

        /// <summary>缺陷清单行选中 → 预览图上给那根缺陷套黄框（编号与清单行一一对应）</summary>
        private void DefectList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not BlobDetectPlugin vm) return;

            // 追加进来的就是新选中的行（单选模式最多一条）
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is DefectRow row)
                vm.HighlightDefect(row.Index);
        }
    }

    /// <summary>
    /// 状态栏文案：插件在"预览成功"时会把 StatusMessage 写成实际结果，
    /// 初始空串在界面上显示为「就绪」；不改变插件的状态语义。
    /// </summary>
    public sealed class StatusMessageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.IsNullOrWhiteSpace(value as string) ? "就绪" : value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
