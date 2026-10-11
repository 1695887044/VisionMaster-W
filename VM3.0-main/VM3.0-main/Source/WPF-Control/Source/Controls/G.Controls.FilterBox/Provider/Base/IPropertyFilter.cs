namespace G.Controls.FilterBox
{
    public interface IPropertyFilter : IDisplayFilter
    {
        bool IsSelected { get; set; }
        string PropertyName { get; set; }
        FilterOperate Operate { get; set; }
        PropertyInfo PropertyInfo { get; set; }
        IFilterable Copy();
    }
}
