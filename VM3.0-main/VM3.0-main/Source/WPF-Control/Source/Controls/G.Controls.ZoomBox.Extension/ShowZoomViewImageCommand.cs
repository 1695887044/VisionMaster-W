using System.Windows.Media;

namespace G.Controls.ZoomBox.Extension
{
    [Icon("\xEB9F")]
    [Display(Name = "图片")]
    public class ShowZoomViewImageCommand : DisplayMarkupCommandBase
    {
        public override async Task ExecuteAsync(object parameter)
        {
            if (parameter is ImageSource source)
                await IocMessage.Dialog.ShowZoomViewImage(source);
            if (parameter is string filePath)
                await IocMessage.Dialog.ShowZoomViewImage(filePath);
        }
    }
}
