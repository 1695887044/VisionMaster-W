using System.Collections.ObjectModel;

namespace G.Data.Test
{
    public class TestBindables : ObservableCollection<TestBindable>
    {
        public TestBindables()
        {

        }
        public TestBindables(IEnumerable<TestBindable> collection) : base(collection)
        {

        }
    }
}
