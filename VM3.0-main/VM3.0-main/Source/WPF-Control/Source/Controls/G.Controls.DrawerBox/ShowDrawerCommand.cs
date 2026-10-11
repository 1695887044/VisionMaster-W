global using G.Common.Attributes;
global using G.Common.Commands;
global using System.ComponentModel.DataAnnotations;

namespace G.Controls.DrawerBox
{
    [Icon("\xE713")]
    [Display(Name = "显示", Description = "显示页面")]
    public class ShowDrawerCommand : DisplayMarkupCommandBase
    {
        public override void Execute(object parameter)
        {
            if (parameter is DrawerBox drawer)
                drawer.Show();
        }
    }

}
