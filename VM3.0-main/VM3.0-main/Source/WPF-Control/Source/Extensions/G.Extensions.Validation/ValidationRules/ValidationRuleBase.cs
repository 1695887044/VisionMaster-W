using System.Windows.Controls;

namespace G.Extensions.Validation.ValidationRules;

public abstract class ValidationRuleBase : ValidationRule
{
    public string ErrorMessage { get; set; } = "数据不匹配";
}
