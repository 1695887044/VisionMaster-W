using G.Common.Commands;
using G.Controls.ZoomBox;
using G.VisionMaster.OpenCV.Base;
using G.VisionMaster.ResultPresenter.ResultPresenters;
using OpenCvSharp.WpfExtensions;
using System.Windows.Input;
using System.Windows.Media;

namespace G.App.VisionMaster.OpenCV.Commands;

public class ZoomToRectCommand : MarkupCommandBase
{
    public override void Execute(object parameter)
    {
        if (parameter is MouseButtonEventArgs args && args.OriginalSource is FrameworkElement element && element.DataContext is IRectangleResultItem rectangle)
        {
            System.Windows.Window root = element.GetVisualRoot<System.Windows.Window>();
            Zoombox zoombox = root.GetChildren<Zoombox>().FirstOrDefault(x => x.Tag?.ToString() == "Image");
            if (zoombox == null)
                return;

            if (zoombox.DataContext is OpenCVVisionDiagramData data && data.ResultNodeData is IOpenCVNodeData openCVNodeData)
            {
                using Mat clone = openCVNodeData.Mat.Clone();
                double thickness = clone.ToThickness();
                var rect = new System.Windows.Rect(rectangle.Rect.X - thickness, rectangle.Rect.Top - thickness, rectangle.Rect.Width + 2 * thickness, rectangle.Rect.Height + 2 * thickness);
                clone.DrawRectangle(rect.ToCVRect(), Colors.Red, clone.ToThickness());
                data.ResultImageSource = clone.ToBitmapSource();
            }
            if (args.ClickCount == 2)
                zoombox.ZoomTo(rectangle.Rect);
        }
    }
}
