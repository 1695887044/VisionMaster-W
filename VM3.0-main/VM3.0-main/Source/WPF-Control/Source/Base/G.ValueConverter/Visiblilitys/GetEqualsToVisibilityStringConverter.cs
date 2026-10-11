namespace G.ValueConverter.Visiblilitys;

public class GetEqualsToVisibilityStringConverter : MarkupValueConverterBase
{
    public Visibility Visibility { get; set; } = Visibility.Visible;
    public override object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value != null && parameter != null)
        {
            if (value.Equals(parameter))
                return this.Visibility;
        }
        return this.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    }
}
