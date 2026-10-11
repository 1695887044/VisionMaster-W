global using G.Common.Attributes;
global using G.Common.Commands;
global using System.ComponentModel.DataAnnotations;
global using System.Windows;

namespace G.Windows.Dock
{
    [Icon("\xE77F")]
    [Display(Name = "显示", Description = "显示停靠窗口页面")]
    public class ShowDockWindowCommand : DisplayMarkupCommandBase
    {
        public HorizontalAlignment Horizontal { get; set; } = HorizontalAlignment.Right;
        public VerticalAlignment Vertical { get; set; } = VerticalAlignment.Bottom;

        public object Content { get; set; }

        public override void Execute(object parameter)
        {
            var w = new DockWindow()
            {
                HorizontalDock = this.Horizontal,
                VerticalDock = this.Vertical,
                Content = this.Content ?? parameter
            };
            w.Show();
        }
    }
}
