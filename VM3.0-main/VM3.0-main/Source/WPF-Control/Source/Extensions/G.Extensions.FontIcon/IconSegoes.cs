using System.Collections.ObjectModel;

namespace G.Extensions.FontIcon;

public class IconSegoes : ObservableCollection<IconSegoe>
{
    public IconSegoes(IEnumerable<IconSegoe> collection) : base(collection)
    {
    }
}
