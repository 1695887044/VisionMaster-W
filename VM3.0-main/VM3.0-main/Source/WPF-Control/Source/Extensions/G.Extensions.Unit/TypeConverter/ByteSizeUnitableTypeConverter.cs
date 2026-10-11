namespace G.Extensions.Unit
{
    public class ByteSizeUnitableTypeConverter : UnitableTypeConverterBase
    {
        protected override IUnitable GetUnitable() => new ByteSizeUnitable();
    }
}

