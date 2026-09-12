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
    /// ProjectSet.xaml 的交互逻辑
    /// </summary>
    public partial class ProjectSetView : MetroWindow
    {
        #region Singleton
        private static readonly ProjectSetView _instance = new ProjectSetView();

        private ProjectSetView()
        {
            InitializeComponent();
            this.DataContext = ProjectSetViewModel.Ins;
        }
        public static ProjectSetView Ins
        {
            get { return _instance; }
        }

        #endregion

        #region Prop
        public bool IsClosed { get; set; } = true;
        #endregion

        #region Method
        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;  // cancels the window close
            IsClosed = true;
            this.Hide();      // Programmatically hides the window
        }
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
        #endregion
    }
}
