using MahApps.Metro.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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
using VM.Start.ViewModels;

namespace VM.Start.Views
{
    /// <summary>
    /// SystemParamView.xaml 的交互逻辑
    /// </summary>
    public partial class SolutionListView : MetroWindow
    {
        #region Singleton
        private static readonly SolutionListView _instance = new SolutionListView();

        private SolutionListView()
        {
            InitializeComponent();
            this.DataContext = SolutionListViewModel.Ins;
        }
        public static SolutionListView Ins
        {
            get { return _instance; }
        }

        #endregion

        public bool IsClosed { get; set; } = true;
        public bool IsOpen { get; set; } = false;
        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;  // cancels the window close
            IsClosed = true;
            IsOpen = false;
            this.Hide();      // Programmatically hides the window
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
