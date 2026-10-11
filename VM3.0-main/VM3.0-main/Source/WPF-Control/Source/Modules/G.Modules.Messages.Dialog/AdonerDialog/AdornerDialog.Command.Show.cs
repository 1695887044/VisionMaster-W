global using G.Common.Commands;
global using System.ComponentModel.DataAnnotations;

namespace G.Modules.Messages.Dialog
{
    [Icon("\xE77F")]
    [Display(Name = "显示", Description = "显示对话框页面")]
    public class ShowAdornerDialogCommand : DisplayMarkupCommandBase
    {
        public override void Execute(object parameter)
        {
            UIElement child = PresenterAdorner.GetAdonerElement();
            AdornerLayer layer = AdornerLayer.GetAdornerLayer(child);
            AdornerDialogPresenter contentDialog = new AdornerDialogPresenter(parameter);
            PresenterAdorner adorner = new PresenterAdorner(child, contentDialog);
            layer.Add(adorner);
        }
    }
}
