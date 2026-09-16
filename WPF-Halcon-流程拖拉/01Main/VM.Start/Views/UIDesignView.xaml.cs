using MahApps.Metro.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml;
using VM.Start.Events;
using VM.Start.Services;
using VM.Start.UIDesign;
using VM.Start.ViewModels;
using VM.Start.Views.Dock;

namespace VM.Start.Views
{
    /// <summary>
    /// UIDesignView.xaml 的交互逻辑
    /// </summary>
    public partial class UIDesignView : MetroWindow
    {
        public UIDesignView()
        {
            this.InitializeComponent();
            base.DataContext = UIDesignViewModel.Ins;
            this.Init();
        }

        public void Init()
        {
            UIDesignViewModel.Ins.PropertyGrid = this.uxPropertyGridView.PropertyGrid;
            this.RouteDesignSurfaceCommands();
        }

        public bool IsClosed { get; set; } = true;

        protected override void OnClosing(CancelEventArgs e)
        {
            Solution.Ins.IsUseUIDesign = UIDesignViewModel.Ins.IsUseUIDesign;
            UIDesignViewModel.Ins.SaveCurrentDocument();
            UIDesignView.UpdateUIDesign(true);
            IsClosed = true;
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            base.Close();
        }

        public static void UpdateUIDesign(bool changeDockView = false)
        {
            try
            {
                if (Solution.Ins.IsUseUIDesign)
                {
                    if (!string.IsNullOrEmpty(Solution.Ins.UIDesignText))
                    {
                        string text = Solution.Ins.UIDesignText;
                        text = text.Replace("<Window", "<UserControl");
                        text = text.Replace("</Window>", "</UserControl>");
                        XmlReader reader = XmlReader.Create(new StringReader(text));
                        object obj = XamlReader.Load(reader);
                        UserControl content = obj as UserControl;
                        UIDisplayView.Ins.contentCtrl.Content = content;
                        if (changeDockView)
                        {
                            MainViewModel.Ins.IsCheckedUIDesign = true;
                        }
                    }
                }
                else if (changeDockView)
                {
                    MainViewModel.Ins.IsCheckedUIDesign = false;
                }
            }
            catch (Exception) { }
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            this.ProcessDrag(e);
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            this.ProcessDrag(e);
        }

        protected override void OnDrop(DragEventArgs e)
        {
            this.ProcessPaths(e.Data.Paths());
        }

        private void RecentFiles_Click(object sender, RoutedEventArgs e)
        {
            string path = (string)(e.OriginalSource as MenuItem).Header;
            UIDesignViewModel.Ins.Open(path);
        }

        private void ProcessDrag(DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            foreach (string text in e.Data.Paths())
            {
                if (
                    !text.EndsWith(".dll", StringComparison.InvariantCultureIgnoreCase)
                    && !text.EndsWith(".exe", StringComparison.InvariantCultureIgnoreCase)
                )
                {
                    if (!text.EndsWith(".xaml", StringComparison.InvariantCultureIgnoreCase))
                    {
                        continue;
                    }
                    e.Effects = DragDropEffects.Copy;
                }
                else
                {
                    e.Effects = DragDropEffects.Copy;
                }
                break;
            }
        }

        private void ProcessPaths(IEnumerable<string> paths) { }

        private void LoadSettings() { }

        private void SaveSettings() { }

        private void RouteDesignSurfaceCommands()
        {
            this.RouteDesignSurfaceCommand(ApplicationCommands.Undo);
            this.RouteDesignSurfaceCommand(ApplicationCommands.Redo);
            this.RouteDesignSurfaceCommand(ApplicationCommands.Copy);
            this.RouteDesignSurfaceCommand(ApplicationCommands.Cut);
            this.RouteDesignSurfaceCommand(ApplicationCommands.Paste);
            this.RouteDesignSurfaceCommand(ApplicationCommands.SelectAll);
            this.RouteDesignSurfaceCommand(ApplicationCommands.Delete);
        }

        private void RouteDesignSurfaceCommand(RoutedCommand command)
        {
            CommandBinding commandBinding = new CommandBinding(command);
            commandBinding.CanExecute += delegate(object sender, CanExecuteRoutedEventArgs e)
            {
                if (UIDesignViewModel.Ins.CurrentDocument != null)
                {
                    UIDesignViewModel.Ins.CurrentDocument.DesignSurface.RaiseEvent(e);
                }
                else
                {
                    e.CanExecute = false;
                }
            };
            commandBinding.Executed += delegate(object sender, ExecutedRoutedEventArgs e)
            {
                UIDesignViewModel.Ins.CurrentDocument.DesignSurface.RaiseEvent(e);
            };
            base.CommandBindings.Add(commandBinding);
        }
    }
}
