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
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MahApps.Metro.Controls;
using VM.Start.Common.Helper;
using VM.Start.Dialogs.ViewModels;
using VM.Start.PersistentData;

namespace VM.Start.Dialogs.Views
{
    /// <summary>
    /// AboutView.xaml 的交互逻辑
    /// </summary>
    public partial class AboutView : MetroWindow
    {
        #region Singleton
        private static readonly Lazy<AboutView> Instance = new Lazy<AboutView>(() => new AboutView());

        private AboutView()
        {
            InitializeComponent();
            DataContext = AboutViewModel.Ins;
            LoadIcon();

        }
        public static AboutView Ins { get; } = Instance.Value;

        #endregion
        private void LoadIcon()
        {
            ImageBrush imageBrush = new ImageBrush();
            if (!File.Exists(SystemConfig.Ins.SoftwareIcoPath))
            {
                SystemConfig.Ins.SoftwareIcoPath = FilePaths.DefultSoftwareIcon;
            }
            imageBrush.ImageSource = new BitmapImage(new Uri(SystemConfig.Ins.SoftwareIcoPath, UriKind.Relative));
            bdImage.Background = imageBrush;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;  // cancels the window close
            this.Hide();      // Programmatically hides the window
        }

    }
}
