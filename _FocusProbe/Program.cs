using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FocusProbe
{
    // 视觉探针（不参与产品构建）：
    //   dotnet run --project _FocusProbe -- --view
    // 实例化真实的 ConditionEditorView（1120x760），输出：
    //   tools\_condition_view_hover.png  悬停叠加层铺满按钮
    //   tools\_condition_view_focus.png  2px 强调色圆角焦点环
    // 不带参数时只渲染一组 DialogSecondaryButton / DialogPrimaryButton，
    // 用于单独观察 Fluent 按钮模板的悬停/焦点几何。
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--view")
            {
                RenderConditionView();
                return;
            }

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/UI;component/Themes/Generic.xaml")
            });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml")
            });

            var style1 = app.TryFindResource("DialogSecondaryButton") as Style;
            var style2 = app.TryFindResource("DialogPrimaryButton") as Style;
            var style3 = app.TryFindResource("DialogFooter") as Style;
            Console.WriteLine($"style1={(style1 == null ? "NULL" : style1.TargetType.ToString())}");
            Console.WriteLine($"style2={(style2 == null ? "NULL" : style2.TargetType.ToString())}");
            Console.WriteLine($"style3={(style3 == null ? "NULL" : style3.TargetType.ToString())}");

            var win = new Window
            {
                Width = 460,
                Height = 160,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                Left = -4000,
                Top = 0,
                Background = Brushes.White,
            };

            var footer = new Border { Style = style3 };
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var b1 = new Button { Content = "Cancel", MinWidth = 96, Style = style1, Margin = new Thickness(0, 0, 12, 0) };
            var b2 = new Button { Content = "Save", MinWidth = 130, Style = style2 };
            panel.Children.Add(b1);
            panel.Children.Add(b2);
            footer.Child = panel;
            win.Content = new AdornerDecorator { Child = footer };

            win.Show();
            win.UpdateLayout();

            FocusManager.SetFocusedElement(win, b1);
            b1.Focus();
            Keyboard.Focus(b1);
            var keyArgs = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(win), 0, Key.Tab)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            };
            win.RaiseEvent(keyArgs);

            Pump(300);
            win.UpdateLayout();

            Console.WriteLine($"b1 actual: {b1.ActualWidth} x {b1.ActualHeight}");
            Console.WriteLine($"b1 padding: {b1.Padding}");
            Console.WriteLine($"b1 border: {b1.BorderThickness}");
            Console.WriteLine($"b1 focusstyle: {(b1.FocusVisualStyle == null ? "NULL" : "SET")}");
            Console.WriteLine($"b1 keyboardfocus: {b1.IsKeyboardFocused}");
            Console.WriteLine("---- FOCUS STYLE XAML ----");
            Console.WriteLine(System.Windows.Markup.XamlWriter.Save(b1.FocusVisualStyle));
            Console.WriteLine("---- KeyboardNavigation focus members ----");
            foreach (var f in typeof(KeyboardNavigation).GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))
                if (f.Name.Contains("FocusVisual")) Console.WriteLine($"FIELD {f.Name} = {f.GetValue(null)}");
            foreach (var p in typeof(KeyboardNavigation).GetProperties(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))
                if (p.Name.Contains("FocusVisual")) Console.WriteLine($"PROP {p.Name}");

            var layer = AdornerLayer.GetAdornerLayer(b1);
            if (layer == null)
            {
                Console.WriteLine("ADORNER_LAYER: NULL");
            }
            else
            {
                int n = VisualTreeHelper.GetChildrenCount(layer);
                Console.WriteLine($"ADORNER_COUNT: {n}");
                for (int i = 0; i < n; i++)
                {
                    var a = VisualTreeHelper.GetChild(layer, i) as FrameworkElement;
                    Console.WriteLine($"ADORNER[{i}]: {a?.GetType().FullName} size={a?.RenderSize}");
                }
            }

            var rtb = new RenderTargetBitmap(460, 160, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(win);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(@"D:\C#\VM\tools\_probe_focus.png"))
                enc.Save(fs);
            Console.WriteLine("SAVED normal");

            b1.ApplyTemplate();
            if (b1.Template.FindName("HoverOverlay", b1) is Border hover)
            {
                hover.Opacity = 1;
                Console.WriteLine($"hover overlay size={hover.RenderSize} button={b1.RenderSize}");
                win.UpdateLayout();
                var rtb2 = new RenderTargetBitmap(460, 160, 96, 96, PixelFormats.Pbgra32);
                rtb2.Render(win);
                var enc2 = new PngBitmapEncoder();
                enc2.Frames.Add(BitmapFrame.Create(rtb2));
                using (var fs = File.Create(@"D:\C#\VM\tools\_probe_hover.png"))
                    enc2.Save(fs);
                Console.WriteLine("SAVED hover");
                hover.Opacity = 0;
            }

            typeof(KeyboardNavigation).GetProperty("AlwaysShowFocusVisual", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?.SetValue(null, true);
            Console.WriteLine($"always after set={typeof(KeyboardNavigation).GetField("_alwaysShowFocusVisual", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)?.GetValue(null)}");
            b2.Focus();
            Pump(50);
            b1.Focus();
            Pump(200);
            win.UpdateLayout();
            var layer2 = AdornerLayer.GetAdornerLayer(b1);
            Console.WriteLine($"focus layer null={(layer2 == null)} count={(layer2 == null ? -1 : VisualTreeHelper.GetChildrenCount(layer2))}");
            Console.WriteLine($"focus style writable={typeof(KeyboardNavigation).GetProperty("AlwaysShowFocusVisual", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?.CanWrite}");
            var rtb3 = new RenderTargetBitmap(460, 160, 96, 96, PixelFormats.Pbgra32);
            rtb3.Render(win);
            var enc3 = new PngBitmapEncoder();
            enc3.Frames.Add(BitmapFrame.Create(rtb3));
            using (var fs = File.Create(@"D:\C#\VM\tools\_probe_focus2.png"))
                enc3.Save(fs);
            Console.WriteLine("SAVED focus");

            win.Close();
            app.Shutdown();
        }

        private static void RenderConditionView()
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/UI;component/Themes/Generic.xaml")
            });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml")
            });

            Prism.Mvvm.ViewModelLocationProvider.SetDefaultViewModelFactory(_ => null);

            var step = new VisionMaster.Models.ConditionStep("", "If", "IfPlugin");
            var vm = new VisionMaster.ViewModels.DialogViewModels.ConditionEditorViewModel(null, null);
            var p = new Prism.Dialogs.DialogParameters();
            p.Add("Node", step);
            vm.OnDialogOpened(p);

            var view = new VisionMaster.Views.DialogViews.ConditionEditorView { DataContext = vm };
            var win = new Window
            {
                Width = 1120,
                Height = 760,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Left = -4000,
                Top = 0,
                Background = Brushes.White,
                Content = new AdornerDecorator { Child = view },
            };
            win.Show();
            win.UpdateLayout();
            Pump(400);
            win.UpdateLayout();

            var cancel = FindButton(view, 96);
            if (cancel != null)
            {
                Console.WriteLine($"cancel actual={cancel.ActualWidth}x{cancel.ActualHeight} padding={cancel.Padding}");
                cancel.ApplyTemplate();
                if (cancel.Template.FindName("HoverOverlay", cancel) is Border hover)
                {
                    hover.Opacity = 1;
                    Console.WriteLine($"cancel hover overlay={hover.RenderSize}");
                    win.UpdateLayout();
                    Save(win, 1120, 760, @"D:\C#\VM\tools\_condition_view_hover.png");
                    hover.Opacity = 0;
                }
            }
            else
            {
                Console.WriteLine("cancel button NOT FOUND");
            }

            win.UpdateLayout();
            Save(win, 1120, 760, @"D:\C#\VM\tools\_condition_view.png");
            Console.WriteLine("SAVED view");

            if (cancel != null)
            {
                typeof(KeyboardNavigation).GetProperty("AlwaysShowFocusVisual", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?.SetValue(null, true);
                var saveBtn = FindButton(view, 130);
                saveBtn?.Focus();
                Pump(60);
                if (saveBtn != null)
                {
                    win.UpdateLayout();
                    Save(win, 1120, 760, @"D:\C#\VM\tools\_condition_view_focus_save.png");
                    Console.WriteLine("SAVED focus save view");
                }
                cancel.Focus();
                Pump(120);
                win.UpdateLayout();
                Save(win, 1120, 760, @"D:\C#\VM\tools\_condition_view_focus.png");
                Console.WriteLine("SAVED focus view");
            }
            win.Close();
            app.Shutdown();
        }

        private static Button FindButton(DependencyObject root, double minWidth)
        {
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is Button b && Math.Abs(b.MinWidth - minWidth) < 0.1)
                    return b;
                var found = FindButton(child, minWidth);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static void Save(Visual visual, int w, int h, string path)
        {
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(path);
            enc.Save(fs);
        }

        private static void Pump(int ms)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }
    }
}
