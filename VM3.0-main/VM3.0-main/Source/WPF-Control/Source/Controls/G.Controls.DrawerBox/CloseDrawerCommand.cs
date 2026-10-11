namespace G.Controls.DrawerBox
{
    [Icon("\xE713")]
    [Display(Name = "隐藏", Description = "隐藏页面")]
    public class CloseDrawerCommand : DisplayMarkupCommandBase
    {
        public override void Execute(object parameter)
        {
            if (parameter is DrawerBox drawer)
                drawer.Close();
        }
    }

}
