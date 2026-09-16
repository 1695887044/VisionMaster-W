using AvalonDock.Layout;
using System;
using System.Collections.Generic;
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
using System.Windows.Navigation;
using System.Windows.Shapes;
using VM.Start.ViewModels;
using VM.Start.ViewModels.Dock;

namespace VM.Start.Views.Dock
{
    /// <summary>
    /// DockView.xaml 的交互逻辑
    /// </summary>
    public partial class DockView : UserControl
    {
        #region Singleton
        private static DockView _instance ;
        private DockView()
        {
            InitializeComponent();
            this.DataContext = DockViewModel.Ins;
        }

        public static DockView Ins
        {
            get 
            { 
                if (_instance == null)
                    _instance = new DockView();
                return _instance; 
            }
        }

        #endregion

    }
}
