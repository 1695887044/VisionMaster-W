namespace G.Controls.Form.Attributes;

public class RefreshOnValueChangedAttribute : Attribute
{
    public RefreshOnValueChangedAttribute(bool canRefresh = true)
    {
        this.CanRefresh = canRefresh;
    }
    public bool CanRefresh { get; }
}
