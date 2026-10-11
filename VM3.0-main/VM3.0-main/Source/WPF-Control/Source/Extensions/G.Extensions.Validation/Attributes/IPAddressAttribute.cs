using System.ComponentModel.DataAnnotations;

namespace G.Extensions.Validation.Attributes;

public class IPAddressAttribute : RegularExpressionAttribute
{
    public IPAddressAttribute() : base(@"^((25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)$")
    {
    }
}
