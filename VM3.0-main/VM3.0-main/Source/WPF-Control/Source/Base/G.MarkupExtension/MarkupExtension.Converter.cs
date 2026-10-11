using System.ComponentModel;

namespace G.MarkupExtension;

/// <summary>
/// TypeConverter需继承TypeConverter
/// </summary>
public class GetTypeConverterExtension : GetValueToTypeExtensionBase
{
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        TypeConverter converter = Activator.CreateInstance(this.Type) as TypeConverter;
        return converter.ConvertFromString(this.Value);
    }
}

/// <summary>
/// ToType需要实现IConvertible
/// </summary>
public class GetIConvertibleExtension : GetValueToTypeExtensionBase
{
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return Convert.ChangeType(this.Value, this.Type);
    }
}
