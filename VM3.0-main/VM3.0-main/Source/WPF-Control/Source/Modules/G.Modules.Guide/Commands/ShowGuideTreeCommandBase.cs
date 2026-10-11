using G.Common.Commands;
using G.Modules.Guide.Base;
using G.Services.Common.Guide;
using G.Services.Message;

namespace G.Modules.Guide.Commands;

public abstract class ShowGuideTreeCommandBase : DisplayMarkupCommandBase
{
    public override async Task ExecuteAsync(object parameter)
    {
        UIElement element = parameter is UIElement e ? e : GuideExtension.GetAdornerElement();
        var tree = element.GetGuideTree(this.IsMatch);
        var presenter = new GuideTreePresenter(tree);
        await IocMessage.ShowDialog(presenter, x =>
        {
            x.MinWidth = 500;
            x.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        });
    }

    protected abstract bool IsMatch(UIElement element);

    public override bool CanExecute(object parameter)
    {
        return base.CanExecute(parameter) && Ioc<IGuideService>.Instance != null;
    }
}