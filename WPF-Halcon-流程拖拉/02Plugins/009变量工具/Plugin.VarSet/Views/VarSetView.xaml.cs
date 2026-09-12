using EventMgrLib;
using Plugin.VarSet.Models;
using Plugin.VarSet.ViewModels;
using System.Web.UI.WebControls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using VM.Start.Common;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.ViewModels;

namespace Plugin.VarSet.Views
{
    /// <summary>
    /// VarDefineView.xaml 的交互逻辑
    /// </summary>
    public partial class VarSetView : ModuleViewBase
    {
        public VarSetView()
        {
            InitializeComponent();

        }
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void dg_PreviewMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            DependencyObject dep = (DependencyObject)e.OriginalSource;

            // 向上遍历Visual树，直到找到列头或者网格
            while ((dep != null) &&
                !(dep is DataGridCell) &&
                !(dep is DataGridColumnHeader))
            {
                dep = VisualTreeHelper.GetParent(dep);
            }

            if (dep == null)
                return;
            var vm = DataContext as VarSetViewModel;
            if (vm == null) return;
            var index = dg.SelectedIndex;
            VarSetModel var = dg.SelectedItem as VarSetModel;
            if (var == null) return;
            //如果是普通网格
            if (dep is DataGridCell)
            {
                DataGridCell cell = dep as DataGridCell;
                if (cell != null)
                {
                    if (cell.Column.Header.ToString() == "链接")
                    {
                        CommonMethods.GetModuleList(vm.ModuleParam, VarLinkViewModel.Ins.Modules, var.DataType);
                        EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{vm.ModuleGuid},VarSetLinkText");
                    }
                    else if (cell.Column.Header.ToString() == "表达式")
                    {
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
        }
    }
}
