using System.Globalization;
using System.Windows.Data;

namespace G.Styles.Controls;

public class WindowKeys
{
#pragma warning disable CS0067
    public static ComponentResourceKey Default => new ComponentResourceKey(typeof(WindowKeys), "S.Window.Default");

    [Obsolete("用FontIconButton替换")]
    public static ComponentResourceKey Button => new ComponentResourceKey(typeof(WindowKeys), "S.Window.Button");
    public static ComponentResourceKey FontIconButton => new ComponentResourceKey(typeof(WindowKeys), "S.Window.FontIconButton");
    public static ComponentResourceKey MaximizeButton => new ComponentResourceKey(typeof(WindowKeys), "S.Window.FontIconButton.Maximize");
    public static ComponentResourceKey MinimizeButton => new ComponentResourceKey(typeof(WindowKeys), "S.Window.FontIconButton.Minimize");
    public static ComponentResourceKey RestoreButton => new ComponentResourceKey(typeof(WindowKeys), "S.Window.FontIconButton.Restore");
    public static ComponentResourceKey CloseButton => new ComponentResourceKey(typeof(WindowKeys), "S.Window.FontIconButton.Close");

    public static ComponentResourceKey WindowChrome => new ComponentResourceKey(typeof(WindowKeys), "S.Window.WindowChrome");

}


public abstract class WindowCommandBaseExtension : MarkupExtension
{
    public event EventHandler CanExecuteChanged;

    public bool CanExecute(object parameter)
    {
        return parameter is Window;
    }

    public abstract void Execute(object parameter);

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return this;
    }
}

public class CloseWindowCommandExtension : WindowCommandBaseExtension, ICommand
{
    public override void Execute(object parameter)
    {
        if (parameter is Window window)
            SystemCommands.CloseWindow(window);
    }
}

public class MaximizeWindowCommandExtension : WindowCommandBaseExtension, ICommand
{
    public override void Execute(object parameter)
    {
        if (parameter is Window window)
            SystemCommands.MaximizeWindow(window);
    }
}

public class RestoreWindowCommandExtension : WindowCommandBaseExtension, ICommand
{
    public override void Execute(object parameter)
    {
        if (parameter is Window window)
            SystemCommands.RestoreWindow(window);
    }
}

public class MinimizeWindowCommandExtension : WindowCommandBaseExtension, ICommand
{
    public override void Execute(object parameter)
    {
        if (parameter is Window window)
            SystemCommands.MinimizeWindow(window);
    }
}

public class GetNullToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return Visibility.Collapsed;
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
