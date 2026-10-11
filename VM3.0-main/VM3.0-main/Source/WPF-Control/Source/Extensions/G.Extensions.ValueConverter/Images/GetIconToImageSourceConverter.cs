using System.Drawing;

namespace G.Extensions.ValueConverter.Images;

[ValueConversion(typeof(Icon), typeof(ImageSource))]
public class GetIconToImageSourceConverter : MarkupValueConverterBase
{
    public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return null;
        Icon icon = (Icon)value;
        ImageSource imageSource =
            System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        return imageSource;
    }
}

