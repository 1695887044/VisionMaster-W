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
using VM.Start.ViewModels.Dock;

namespace VM.Start.Views.Dock
{
    /// <summary>
    /// DataView.xaml 的交互逻辑
    /// </summary>
    public partial class DataView : UserControl
    {
        #region Singleton
        private static readonly DataView _instance = new DataView();
        private DataView()
        {
            InitializeComponent();
            this.DataContext = DataViewModel.Ins;
        }
        public static DataView Ins
        {
            get { return _instance; }
        }
        #endregion
    }
}
