// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using G.Common.Attributes;
using G.Common.Commands;
using G.Extensions.FontIcon;
using G.Modules.Help.ReleaseVersions;
using System.ComponentModel.DataAnnotations;

namespace G.Modules.Help;

[Icon(FontIcons.History)]
[Display(Name = "发行说明", Description = "查看软件发行说明")]
public class ShowReleaseVersionsCommand : DisplayMarkupCommandBase
{
    public override Task ExecuteAsync(object parameter)
    {
        Ioc.GetService<IReleaseVersionsService>()?.Show();
        return base.ExecuteAsync(parameter);
    }

    public override bool CanExecute(object parameter)
    {
        return base.CanExecute(parameter) && Ioc.Exist<IReleaseVersionsService>();
    }
}

