using System.Collections.ObjectModel;

namespace G.Data.Test
{
    public class Students : ObservableCollection<Student>
    {
        public Students()
        {

        }
        public Students(IEnumerable<Student> collection) : base(collection)
        {

        }
    }
}
