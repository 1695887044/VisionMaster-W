using System.Windows;
using System.Windows.Controls;
using Core.Halcon.Controls;

namespace Plugin.ImageAlign
{
    /// <summary>
    /// 图像对齐配置视图：左侧画布取点，右侧点对表格 + 参数弹窗。
    /// 业务逻辑全部在 <see cref="ImageAlignPlugin"/>；本文件只做事件桥接。
    /// </summary>
    public partial class ImageAlignView : UserControl
    {
        public ImageAlignView()
        {
            InitializeComponent();
            Loaded += (_, _) => (DataContext as ImageAlignPlugin)?.OnViewLoaded();
        }

        private void OnImagePicked(object sender, HalconBase.ImagePickEventArgs e)
            => (DataContext as ImageAlignPlugin)?.ApplyPickedPoint(e.Row, e.Column);

        private void PickReference_Click(object sender, RoutedEventArgs e)
            => (DataContext as ImageAlignPlugin)?.StartPick(true);

        private void PickCurrent_Click(object sender, RoutedEventArgs e)
            => (DataContext as ImageAlignPlugin)?.StartPick(false);

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
            => (DataContext as ImageAlignPlugin)?.DeleteSelectedPoint();

        private void Clear_Click(object sender, RoutedEventArgs e)
            => (DataContext as ImageAlignPlugin)?.ClearPoints();

        private void Parameters_Click(object sender, RoutedEventArgs e)
            => (DataContext as ImageAlignPlugin)?.OpenParameters();
    }
}
