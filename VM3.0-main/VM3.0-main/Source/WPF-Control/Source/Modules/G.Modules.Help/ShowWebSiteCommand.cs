// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using G.Common.Attributes;
using G.Common.Commands;
using G.Extensions.FontIcon;
using G.Modules.Help.WebSite;
using System.ComponentModel.DataAnnotations;

namespace G.Modules.Help;

[Icon(FontIcons.Home)]
[Display(Name = "官方网址", Description = "查看官方网址")]
public class ShowWebSiteCommand : DisplayMarkupCommandBase
{
    public override Task ExecuteAsync(object parameter)
    {
        Ioc.GetService<IWebsiteService>()?.Show();
        return base.ExecuteAsync(parameter);
    }

    public override bool CanExecute(object parameter)
    {
        return base.CanExecute(parameter) && Ioc.Exist<IWebsiteService>();
    }
}

