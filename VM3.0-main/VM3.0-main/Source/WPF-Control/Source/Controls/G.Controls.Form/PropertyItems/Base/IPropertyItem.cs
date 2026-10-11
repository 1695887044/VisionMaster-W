namespace G.Controls.Form.PropertyItems.Base;

public interface IPropertyItem
{
    string Name { get; set; }
    int Order { get; set; }
    string TabGroup { get; set; }
    string GroupName { get; set; }
    PropertyInfo PropertyInfo { get; set; }
    object Obj { get; set; }
}

public interface IPropertyViewItem : IPropertyItem
{

}

public interface IHitTestPropertyViewItem: IPropertyViewItem
{
    bool IsHitTestVisible { get; set; }
}