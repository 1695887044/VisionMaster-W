namespace G.Controls.FilterBox
{

    public class LongPropertyFilter : ComparablePropertyFilterBase<long>
    {
        public LongPropertyFilter()
        {

        }

        public LongPropertyFilter(PropertyInfo property) : base(property)
        {

        }

        public override IFilterable Copy()
        {
            return new LongPropertyFilter(this.PropertyInfo) { Operate = this.Operate, Value = this.Value };
        }
    }
}
