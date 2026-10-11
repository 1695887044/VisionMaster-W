global using G.Common.Attributes;
global using G.Common.Commands;
global using G.Services.Message;
global using G.Services.Message.IODialog;
global using System.ComponentModel.DataAnnotations;

namespace G.Controls.ZoomBox.Extension
{
    [Icon("\xEB9F")]
    [Display(Name = "图片")]
    public class ShowZoomViewImageFileCommand : DisplayMarkupCommandBase
    {
        public string FilePath { get; set; }
        public bool UseCache { get; set; } = true;
        public override async Task ExecuteAsync(object parameter)
        {
            var path = parameter?.ToString() ?? this.FilePath;
            if (string.IsNullOrEmpty(path))
            {
                IocMessage.IOFileDialog.ShowOpenImageFile(x =>
                {
                    path = x;
                    if (this.UseCache)
                        this.FilePath = path;
                });
            }
            await IocMessage.Dialog.ShowZoomViewImage(path);
        }
    }
}
