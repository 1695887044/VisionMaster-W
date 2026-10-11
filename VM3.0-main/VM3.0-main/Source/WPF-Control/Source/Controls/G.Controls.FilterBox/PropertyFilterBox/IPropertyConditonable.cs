namespace G.Controls.FilterBox
{
    public interface IPropertyConditonable : IConditionable
    {
        event EventHandler<IConditionable> ConditionChanged;
    }
}
