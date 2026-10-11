using G.Common.Attributes;
using G.Common.Commands;
using G.Services.Common.Guide;

namespace G.Modules.Guide.Commands;

[Icon("\xE963")]
[Display(Name = "新手向导", Description = "显示新手向导")]
public class ShowGuideCommand : DisplayMarkupCommandBase
{
    public override async Task ExecuteAsync(object parameter)
    {
        await Ioc<IGuideService>.Instance.Show();
    }

    public override bool CanExecute(object parameter)
    {
        return base.CanExecute(parameter) && Ioc<IGuideService>.Instance != null;
    }
}
