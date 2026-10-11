namespace G.Controls.FilterBox
{
    public class DoublePropertyFilter : ComparablePropertyFilterBase<double>
    {
        public DoublePropertyFilter()
        {

        }
        public DoublePropertyFilter(PropertyInfo property) : base(property)
        {

        }

        public override IFilterable Copy()
        {
            return new DoublePropertyFilter(this.PropertyInfo) { Operate = this.Operate, Value = this.Value };
        }
    }

}
