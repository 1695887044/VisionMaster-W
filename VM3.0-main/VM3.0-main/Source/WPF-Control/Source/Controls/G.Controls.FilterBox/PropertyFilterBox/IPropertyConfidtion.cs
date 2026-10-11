namespace G.Controls.FilterBox
{
    public interface IPropertyConfidtion : IConditionable
    {
        IPropertyFilter Filter { get; set; }
    }
}
