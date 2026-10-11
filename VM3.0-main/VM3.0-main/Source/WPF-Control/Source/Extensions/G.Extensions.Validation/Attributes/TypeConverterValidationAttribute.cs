using System.ComponentModel.DataAnnotations;

namespace G.Extensions.Validation.Attributes;

/// <summary>
/// 应用TypeConverter去验证数据是否合法
/// </summary>
public class TypeConverterValidationAttribute : ValidationAttribute
{
    public Type Type { get; set; }

    public TypeConverterValidationAttribute(Type type)
    {
        this.Type = type;
    }
}
