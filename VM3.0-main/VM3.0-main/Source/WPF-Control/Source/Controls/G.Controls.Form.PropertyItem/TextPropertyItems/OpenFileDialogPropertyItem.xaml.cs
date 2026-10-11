using G.Controls.Form.PropertyItem.TextPropertyItems.Base;
using G.Services.Message;
using System.ComponentModel.DataAnnotations;
using System.IO;

namespace G.Controls.Form.PropertyItem.TextPropertyItems
{
    public class OpenFileDialogPropertyItem : CommandsTextPropertyItemBase
    {
        public OpenFileDialogPropertyItem(PropertyInfo property, object obj) : base(property, obj)
        {

        }

        [Display(Name = "浏览", Order = 2)]
        public DisplayCommand OpenCommand => new DisplayCommand(l =>
        {
            var r = IocMessage.IOFileDialog.ShowOpenFile(x =>
            {
                if (File.Exists(this.Value))
                    x.InitialDirectory = Path.GetDirectoryName(this.Value);
            });
            if (!File.Exists(r))
                return;
            this.Value = r;
        })
        { Name = "浏览" };
    }

}
