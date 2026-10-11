using System.Globalization;

namespace G.ValueConverter.IEnumerables;

/// <summary>
/// 并运算
/// </summary>
public class GetTrueForAllConverter : MarkupMultiValueConverterBase
{
    public override object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values == null)
            return null;
        List<bool> result = values.OfType<bool>()?.ToList();

        return result.TrueForAll(l => l == true);
    }
}
