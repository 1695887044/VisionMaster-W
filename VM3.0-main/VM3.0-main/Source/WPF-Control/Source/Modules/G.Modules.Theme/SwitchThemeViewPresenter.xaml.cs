using G.Extensions.Mvvm.Commands;
using G.Mvvm.Commands;
using G.Services.Common.Theme;
using Microsoft.Extensions.Options;

namespace G.Modules.Theme
{
    public class SwitchThemeViewPresenter : ISwitchThemeViewPresenter
    {
        private readonly IOptions<ThemeOptions> _options;
        public SwitchThemeViewPresenter(IOptions<ThemeOptions> options)
        {
            _options = options;
        }
        public RelayCommand LoadedCommand => new RelayCommand(e =>
        {
            this._options.Value.RefreshThemeCommand.Execute(null);
        });

        public RelayCommand SwitchDarkCommand => new RelayCommand(x =>
        {
            this._options.Value.SwitchDark();
        });
    }
}
