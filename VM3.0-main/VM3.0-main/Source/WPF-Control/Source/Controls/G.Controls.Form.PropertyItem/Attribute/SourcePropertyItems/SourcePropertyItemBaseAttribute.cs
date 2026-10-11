namespace G.Controls.Form.PropertyItem.Attribute.SourcePropertyItem
{
    public abstract class SourcePropertyItemBaseAttribute : PropertyItemAttribute
    {
        public SourcePropertyItemBaseAttribute(Type type) : base(type)
        {

        }

        public abstract IEnumerable GetSource(PropertyInfo propertyInfo, object obj);
    }
}
