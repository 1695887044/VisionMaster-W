global using G.Iocable;
using G.Common.Commands;
using G.Services.Common.Guide;

namespace G.Modules.Guide.Commands;

public abstract class ShowGuideCommandBase : DisplayMarkupCommandBase
{
    public override async Task ExecuteAsync(object parameter)
    {
        await Ioc<IGuideService>.Instance.Show(this.IsMatch);
    }

    protected abstract bool IsMatch(UIElement element);
}
