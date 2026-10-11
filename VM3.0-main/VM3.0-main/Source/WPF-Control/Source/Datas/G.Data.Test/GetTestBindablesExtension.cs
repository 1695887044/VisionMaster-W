using System.Windows.Markup;

namespace G.Data.Test
{
    public class GetTestBindablesExtension : MarkupExtension
    {
        public int Count { get; set; } = 10;

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return Enumerable.Range(0, this.Count).Select(x => new TestBindable());
        }
    }
}
