using System.Collections.ObjectModel;
using System.Windows.Markup;

namespace G.Data.Test
{
    public class GetStudentsExtension : MarkupExtension
    {
        public int Count { get; set; } = 10;

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return new ObservableCollection<Student>(Enumerable.Range(0, this.Count).Select(x => new Student()));
        }
    }
}
