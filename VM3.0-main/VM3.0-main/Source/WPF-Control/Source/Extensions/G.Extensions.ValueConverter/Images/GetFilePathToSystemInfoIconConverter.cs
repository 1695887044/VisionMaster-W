namespace G.Extensions.ValueConverter.Images;

public class GetFilePathToSystemInfoIconConverter : MarkupValueConverterBase
{
    public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return null;
        string str = value.ToString();
        //if (File.Exists(str) == false)
        //    return null;
        var icon = IconHelper.GetSystemInfoIcon(str);
        return IconHelper.GetIconToImageSource(icon);
    }
}

