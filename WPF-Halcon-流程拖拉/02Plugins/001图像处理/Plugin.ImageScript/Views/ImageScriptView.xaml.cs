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
using VM.Start.Core;
using ScintillaNET;
using Plugin.ImageScript.ViewModels;
using System.Drawing;
using System.Windows.Forms;
using VM.Start.Dialogs.Views;
using HalconDotNet;

namespace Plugin.ImageScript.Views
{
    /// <summary>
    /// SaveImageView.xaml 的交互逻辑
    /// </summary>
    [Serializable]
    public partial class ImageScriptView : ModuleViewBase
    {
        public ImageScriptView()
        {
            InitializeComponent();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        public void procedureMethodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (procedureMethodComboBox.SelectedIndex == -1)
            {
                return;
            }
            var viewModel = DataContext as ImageScriptViewModel;
            if (viewModel == null) return;
            viewModel.MyEditer.Text = viewModel.m_EProcedureList[procedureMethodComboBox.SelectedIndex].Body;
        }
        #region Method


        #endregion

        private void CheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            //增加预编译,在脚本里有大量的循环的时候 速度会提示,否则没什么效果
            ImageScriptViewModel.s_HDevEngine.SetEngineAttribute("execute_procedures_jit_compiled", "true");
            ImageScriptViewModel.s_HDevEngine.StopDebugServer();
        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {

        }
    }
    #region enum
    public enum eOperateCommand
    {
        Import,
        Export,
    }
    #endregion

}
