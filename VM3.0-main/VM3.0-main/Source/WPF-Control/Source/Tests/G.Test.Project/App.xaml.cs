using G.DataBases.Share;
using G.Extensions.ApplicationBase;
using G.Modules.Identity;
using G.Services.Common;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using G.Extensions.DataBase;
namespace G.Test.Project
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : ApplicationBase
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            
            services.AddAdornerDialogMessage();
            services.AddFormMessageService();
            services.AddProject<UserProjectService>();


            //  Do ：根据登录用户加载不同工程
            //services.AddLoginViewPresenter();
            //services.AddTestLoginService();
            services.AddWindowMessage();
            //services.AddRegisterLoginViewPresenter();
            //services.AddRegisterService();
            //services.AddLoginService();
            //services.AddDbContextBySetting<IdentifyDataContext>();
            //services.AddSingleton<IStringRepository<hi_dd_user>, DbContextRepository<IdentifyDataContext, hi_dd_user>>();
        }

        protected override Window CreateMainWindow(StartupEventArgs e)
        {
            return new MainWindow();
        }
    }
}
