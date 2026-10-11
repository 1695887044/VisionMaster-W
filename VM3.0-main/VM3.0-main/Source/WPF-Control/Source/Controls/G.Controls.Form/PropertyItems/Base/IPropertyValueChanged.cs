namespace G.Controls.Form.PropertyItems.Base;

/// <summary>
/// 用于Form Object注册相应属性值变化事件
/// </summary>
public interface IPropertyValueChanged
{
    void OnPropertyValueChanged(PropertyInfo propertyInfo, object o, object n);
}
