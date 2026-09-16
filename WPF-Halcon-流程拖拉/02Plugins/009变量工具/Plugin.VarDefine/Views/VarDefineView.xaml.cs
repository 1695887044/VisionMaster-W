using Plugin.VarDefine.ViewModels;
using System.Windows;
using VM.Start.Common;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.Models;

namespace Plugin.VarDefine.Views
{
    /// <summary>
    /// VarDefineView.xaml 的交互逻辑
    /// </summary>
    public partial class VarDefineView : ModuleViewBase
    {
        public VarDefineView()
        {
            InitializeComponent();

        }
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void dg_PreviewMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (dg.SelectedItem == null) return;
            VarModel var = dg.SelectedItem as VarModel;
            if (var == null) return;
            var vm = DataContext as VarDefineViewModel;
            if (vm == null) return;
            if (vm.expressionView == null)
            {
                vm.expressionView = new ExpressionView();
            }
            CommonMethods.GetModuleList(vm.ModuleParam, vm.Modules);
            vm.expressionView.tcModuleList.ItemsSource = null;
            vm.expressionView.tcModuleList.ItemsSource = vm.Modules;
            vm.expressionView.tcModuleList.SelectedIndex = 0;
            vm.expressionView.viewModel.m_Param = vm.ModuleParam;
            if (var.Expression == "NULL")
            {
                vm.expressionView.viewModel.MyEditer.Text = "";
            }
            else
            {
                vm.expressionView.viewModel.MyEditer.Text = var.Expression;
            }
            vm.expressionView.Var = var;
            vm.expressionView.ShowDialog();
            if (vm.expressionView.Var.IsCompileSuccess)
            {
                var.IsCompileSuccess = true;
                var.Value = var.m_TempScriptSupport.CodeRun();
                var.Expression = vm.expressionView.viewModel.MyEditer.Text;
            }
            else
            {
                var.IsCompileSuccess = false;
            }
        }
    }
}
