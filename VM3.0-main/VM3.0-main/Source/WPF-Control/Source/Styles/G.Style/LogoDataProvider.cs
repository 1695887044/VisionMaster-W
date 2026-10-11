namespace G.Styles;

public class LogoDataProvider
{
    public static ImageSource Logo
    {
        get
        {
            var uri = new System.Uri(@"d:\GVision\Document\logo.ico", System.UriKind.Absolute);
            var decoder = new System.Windows.Media.Imaging.IconBitmapDecoder(uri, System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            double scale = 1.0;
            var main = System.Windows.Application.Current?.MainWindow;
            var src = main == null ? null : System.Windows.PresentationSource.FromVisual(main);
            if (src != null && src.CompositionTarget != null)
            {
                var m = src.CompositionTarget.TransformToDevice;
                scale = m.M11;
            }
            int target = (int)System.Math.Round(System.Windows.SystemParameters.SmallIconWidth * scale);
            System.Windows.Media.Imaging.BitmapFrame best = null;
            int bestDiff = int.MaxValue;
            foreach (var f in decoder.Frames)
            {
                int diff = System.Math.Abs(f.PixelWidth - target);
                if (diff < bestDiff)
                {
                    best = f;
                    bestDiff = diff;
                }
            }
            return best ?? decoder.Frames[0];
        }
    }
}
