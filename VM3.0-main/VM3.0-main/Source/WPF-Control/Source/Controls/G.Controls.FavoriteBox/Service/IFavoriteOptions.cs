using System.Collections.ObjectModel;

namespace G.Controls.FavoriteBox;
public interface IFavoriteOptions
{
    ObservableCollection<FavoriteItem> FavoriteItems { get; set; }
}