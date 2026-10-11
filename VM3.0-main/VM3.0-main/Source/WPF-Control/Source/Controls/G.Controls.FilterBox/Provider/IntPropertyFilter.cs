namespace G.Controls.FilterBox
{
    public class IntPropertyFilter : ComparablePropertyFilterBase<int>
    {
        public IntPropertyFilter()
        {

        }

        public IntPropertyFilter(PropertyInfo property) : base(property)
        {

        }

        public override IFilterable Copy()
        {
            return new IntPropertyFilter(this.PropertyInfo) { Operate = this.Operate, Value = this.Value };
        }
    }
}
