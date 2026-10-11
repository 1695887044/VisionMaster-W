using G.Extensions.ApplicationBase;
using G.Extensions.FontIcon;
using G.Modules.Setting;
using G.Services.Setting;
using G.Styles;
using G.Styles.Controls;
using G.Themes.Colors.Accent;
using G.Themes.Colors.Blue;
using G.Themes.Colors.Copper;
using G.Themes.Colors.Gray;
using G.Themes.Colors.Industrial;
using G.Themes.Colors.Mineral;
using G.Themes.Colors.Platform;
using G.Themes.Colors.Purple;
using G.Themes.Colors.Solid;
using G.Themes.Colors.Technology;
using G.Themes.Colors.Web;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace G.Test.Theme
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : ApplicationBase
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            services.AddSetting();
            services.AddAdornerDialogMessage();
            services.AddTheme();
        }

        protected override Window CreateMainWindow(StartupEventArgs e)
        {
            return new MainWindow();
        }

        protected override void Configure(IApplicationBuilder app)
        {
            base.Configure(app);
            app.UseThemeOptions(x =>
            {
                x.ColorResources.Add(new PurpleDarkColorResource());
                x.ColorResources.Add(new PurpleLightColorResource());
                x.ColorResources.Add(new GrayDarkColorResource());
                x.ColorResources.Add(new GrayLightColorResource());
                x.ColorResources.Add(new BlueDarkColorResource());
                x.ColorResources.Add(new BlueLightColorResource());
                x.ColorResources.Add(new AccentLightColorResource());
                x.ColorResources.Add(new AccentDarkColorResource());
                x.ColorResources.Add(new CopperColorResource());
                x.ColorResources.Add(new VintageFilmColorResource());
                x.ColorResources.Add(new CyberpunkColorResource());
                x.ColorResources.Add(new MineralColorResource());
                x.ColorResources.Add(new FuturismColorResource());
                x.ColorResources.Add(new TechnologyBlueDarkColorResource());
                x.ColorResources.Add(new FuturisticGreenDarkColorResource());
                x.ColorResources.Add(new AmberTerminalDarkColorResource());
                x.ColorResources.Add(new IndustrialDarkColorResource());
                x.ColorResources.Add(new AntDesignProColorResource());
                x.ColorResources.Add(new BootstrapColorResource());
                x.ColorResources.Add(new LayUIColorResource());
                x.ColorResources.Add(new WeUIColorResource());
                x.ColorResources.Add(new ColorUIGAColorResource());
                x.ColorResources.Add(new FluentUIColorResource());
                x.ColorResources.Add(new MaterialDesignColorResource());
                x.ColorResources.Add(new AppleColorResource());
                x.ColorResources.Add(new OracleDarkColorResource());
                x.ColorResources.Add(new VikingDarkColorResource());
                x.ColorResources.Add(new ChambrayDarkColorResource());
                //x.ColorResources.Add(new IndustrialDarkColorResource());
                //x.ColorResources.Add(new IndustrialDarkColorResource());
                //x.ColorResources.Add(new IndustrialDarkColorResource());
                //x.ColorResources.Add(new IndustrialDarkColorResource());
                //x.ColorResources.Add(new IndustrialDarkColorResource());
                //x.ColorResources.Add(new IndustrialDarkColorResource());
                //x.ColorResources.Add(new IndustrialDarkColorResource());
                //x.ColorResources.Add(new IndustrialDarkColorResource());
                //x.ColorResources.Add(new IndustrialDarkColorResource());

                x.IconFontFamilys.Add(IconFontFamilys.SystemSegoeMDL2Asset);
                x.IconFontFamilys.Add(IconFontFamilys.SystemSegoeFluentIcons);
                x.IconFontFamilys.Add(IconFontFamilys.LocationSegoeMDL2Asset);
                x.IconFontFamilys.Add(IconFontFamilys.LocationSegoeFluentIcons);
            });

            //app.UseWindowSetting(x =>
            //{
            //    x.BackImagePath = "pack://application:,,,/G.Extensions.BackgroundImage;component/b13.png";
            //});
        }
    }
}
