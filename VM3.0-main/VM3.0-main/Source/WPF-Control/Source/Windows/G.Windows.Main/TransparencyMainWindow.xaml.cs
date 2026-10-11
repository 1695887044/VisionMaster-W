using G.ValueConverter;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Input;

namespace G.Windows.Main;

public class TransparencyMainWindow : MainWindow
{
    static TransparencyMainWindow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(TransparencyMainWindow), new FrameworkPropertyMetadata(typeof(TransparencyMainWindow)));
    }


    public TransparencyMainWindow()
    {
        this.AllowsTransparency = true;
        this.WindowStyle = WindowStyle.None;
        this.MouseDown += (s, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        };
    }
}
