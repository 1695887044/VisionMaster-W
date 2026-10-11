using Microsoft.Extensions.Options;

namespace G.Modules.Theme;
public class ColorThemeViewPresenter : IColorThemeViewPresenter
{
    private readonly IOptions<ThemeOptions> _options;
    public ColorThemeViewPresenter(IOptions<ThemeOptions> options)
    {
        _options = options;
    }

    //private ObservableCollection<IColorResource> _collection = new ObservableCollection<IColorResource>();
    ///// <summary> 说明  </summary>
    //public ObservableCollection<IColorResource> Collection
    //{
    //    get { return _collection; }
    //    set
    //    {
    //        _collection = value;
    //    }
    //}

}
