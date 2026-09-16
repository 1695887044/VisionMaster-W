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
using VM.Start.Common;
using VM.Start.Models;
using VM.Start.ViewModels;

namespace VM.Start.Views
{
    /// <summary>
    /// SystemParamView.xaml 的交互逻辑
    /// </summary>
    public partial class VarLinkView : MetroWindow
    {
        #region Singleton
        private static VarLinkView _instance = new VarLinkView();

        private VarLinkView()
        {
            InitializeComponent();
            this.DataContext = VarLinkViewModel.Ins;

        }
        public static VarLinkView Ins
        {
            get { return _instance; }
        }

        #endregion

        #region Prop

        #endregion

        #region Method
        public void ModuleListSelectFirst()
        {
            tcModuleList.ItemsSource = null;
            tcModuleList.ItemsSource = VarLinkViewModel.Ins.Modules;
            tcModuleList.SelectedIndex = 0;
        }

        public bool IsClosed { get; set; } = true;

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

        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            VarLinkViewModel.Ins.ConfirmCommand.Execute(0);
        }
    }
}
