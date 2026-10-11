using G.Common.Interfaces;

namespace G.Controls.FavoriteBox
{
    public interface IFavoriteService : IDataSource<IFavoriteItem>, ISplashLoadable, ISplashSave
    {
        IFavoriteItem Create();
    }
}
