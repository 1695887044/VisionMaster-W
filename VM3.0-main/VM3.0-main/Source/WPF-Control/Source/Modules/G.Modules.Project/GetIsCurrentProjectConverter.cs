global using G.ValueConverter;
global using System;
global using System.Globalization;

namespace G.Modules.Project;

public class GetIsCurrentProjectConverter : MarkupMultiValueConverterBase
{
    public override object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        return IocProject.Instance.Current == values[0];
    }
}
