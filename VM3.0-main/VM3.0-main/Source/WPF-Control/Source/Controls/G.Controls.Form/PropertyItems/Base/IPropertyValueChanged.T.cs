namespace G.Controls.Form.PropertyItems.Base;

public interface IPropertyValueChanged<PropertyType>
{
    void OnPropertyValueChanged(PropertyInfo propertyInfo, PropertyType o, PropertyType n);
}
