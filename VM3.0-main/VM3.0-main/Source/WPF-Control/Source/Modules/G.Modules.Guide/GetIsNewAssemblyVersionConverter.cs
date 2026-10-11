using G.ValueConverter;
using System.Globalization;
using System.Reflection;

namespace G.Modules.Guide;

public class GetIsNewAssemblyVersionConverter : MarkupValueConverterBase
{
    public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Version version)
            return version == Assembly.GetEntryAssembly().GetName().Version;
        return false;
    }
}
