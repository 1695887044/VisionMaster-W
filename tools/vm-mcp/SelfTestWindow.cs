using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace VmMcp;

/// <summary>
/// Deterministic UI target for the MCP self test: a tiny WPF window with a menu, a
/// button, a text box, a drag surface and a scroll viewer. It exists so the GUI half of
/// this server (tree walk, pattern actions, typing, drag, wheel, capture) can be
/// regression-tested without starting VisionMaster and without touching whatever the
/// user has open.
/// Started with --selftest-window; prints PID= on stdout and runs until killed.
/// </summary>
internal static class SelfTestWindow
{
    public static int Run()
    {
        var window = new Window
        {
            Title = "vm-mcp self test",
            Width = 560,
            Height = 640,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };

        var dock = new DockPanel();

        var menu = new Menu();
        var file = new MenuItem { Header = "File" };
        AutomationProperties.SetAutomationId(file, "MnuFile");
        AutomationProperties.SetName(file, "File");
        var open = new MenuItem { Header = "Open" };
        AutomationProperties.SetAutomationId(open, "MnuOpen");
        AutomationProperties.SetName(open, "Open");
        file.Items.Add(open);
        file.Items.Add(new MenuItem { Header = "Exit" });
        menu.Items.Add(file);
        DockPanel.SetDock(menu, Dock.Top);
        dock.Children.Add(menu);

        var counter = 0;
        var countText = new TextBlock { Text = "Count: 0", Margin = new Thickness(16, 12, 16, 4), FontSize = 16 };
        AutomationProperties.SetAutomationId(countText, "TxtCount");

        var button = new Button
        {
            Content = "ClickMe",
            Width = 140,
            Height = 36,
            Margin = new Thickness(16, 0, 16, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetAutomationId(button, "BtnClick");
        AutomationProperties.SetName(button, "ClickMe");
        button.Click += (_, _) =>
        {
            counter++;
            countText.Text = $"Count: {counter}";
        };

        var input = new TextBox
        {
            Width = 260,
            Height = 32,
            Margin = new Thickness(16, 0, 16, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetAutomationId(input, "TxtInput");
        AutomationProperties.SetName(input, "Input");
        input.TextChanged += (_, _) => input.ToolTip = input.Text;

        var dropText = new TextBlock { Text = "Drop: none", Margin = new Thickness(16, 4, 16, 4) };
        AutomationProperties.SetAutomationId(dropText, "TxtDrop");

        var surface = new GroupBox
        {
            Header = "DragSurface",
            Height = 110,
            Margin = new Thickness(16, 0, 16, 8),
            Background = new SolidColorBrush(Color.FromRgb(0xD6, 0xE4, 0xF0)),
        };
        AutomationProperties.SetAutomationId(surface, "DragSurface");
        AutomationProperties.SetName(surface, "DragSurface");

        Point? dragOrigin = null;
        surface.PreviewMouseLeftButtonDown += (_, e) =>
        {
            dragOrigin = e.GetPosition(surface);
            surface.CaptureMouse();
        };
        surface.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (dragOrigin is not null)
            {
                var p = e.GetPosition(surface);
                dropText.Text = $"Drop: {(int)(p.X - dragOrigin.Value.X)},{(int)(p.Y - dragOrigin.Value.Y)}";
            }
            dragOrigin = null;
            surface.ReleaseMouseCapture();
        };

        var offsetText = new TextBlock { Text = "Offset: 0", Margin = new Thickness(16, 4, 16, 4) };
        AutomationProperties.SetAutomationId(offsetText, "TxtOffset");

        // Deliberately a bare Border with an AutomationId: it has no automation peer, so
        // it must NOT appear in the UIA tree even though it is visible on screen. The
        // TextBlock inside it must appear. uismoke asserts both halves of that.
        var plainBorder = new Border
        {
            Height = 28,
            Margin = new Thickness(16, 0, 16, 8),
            Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            Child = new TextBlock { Text = "BorderChild" },
        };
        AutomationProperties.SetAutomationId(plainBorder, "PlainBorder");
        AutomationProperties.SetName(plainBorder, "PlainBorder");

        var scroller = new ScrollViewer
        {
            Height = 110,
            Margin = new Thickness(16, 0, 16, 8),
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
        };
        AutomationProperties.SetAutomationId(scroller, "ScrollArea");
        var items = new StackPanel();
        for (int i = 0; i < 40; i++)
        {
            items.Children.Add(new TextBlock { Text = $"Item {i}", Height = 20 });
        }
        scroller.Content = items;
        scroller.ScrollChanged += (_, e) => offsetText.Text = $"Offset: {(int)e.VerticalOffset}";

        var stack = new StackPanel();
        stack.Children.Add(countText);
        stack.Children.Add(button);
        stack.Children.Add(input);
        stack.Children.Add(dropText);
        stack.Children.Add(surface);
        stack.Children.Add(plainBorder);
        stack.Children.Add(offsetText);
        stack.Children.Add(scroller);
        dock.Children.Add(stack);

        window.Content = dock;
        window.Show();

        Console.WriteLine($"PID={Environment.ProcessId}");
        Console.Out.Flush();

        Dispatcher.Run();
        return 0;
    }
}
