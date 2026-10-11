namespace G.Controls.Form.PropertyItem.Attribute.SourcePropertyItem
{
    public class SourcePropertyItemAttribute : SourcePropertyItemBaseAttribute
    {
        public SourcePropertyItemAttribute(Type type, IEnumerable source) : base(type)
        {
            this.Source = source;
        }

        public IEnumerable Source { get; set; }

        public override IEnumerable GetSource(PropertyInfo propertyInfo, object obj)
        {
            return this.Source;
        }
    }
}
