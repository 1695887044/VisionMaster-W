using ICSharpCode.AvalonEdit;
using ICSharpCode.CodeCompletion;
using Plugin.CSharpScript.ViewModels;
using System;
using System.Linq;
using System.Web.Configuration;
using System.Web.UI.WebControls;
using System.Windows;
using System.Windows.Media;
using VM.Common.Engine;
using VM.Script.Support;
using VM.Start.Common;
using VM.Start.Core;
using VM.Start.Models;
using VM.Start.Script;

namespace Plugin.CSharpScript.Views
{
    /// <summary>
    /// GrabImageModuleView.xaml 的交互逻辑
    /// </summary>
    public partial class CSharpScriptView : ModuleViewBase
    {
        public CSharpScriptView()
        {
            InitializeComponent();

        }
        private void ModuleViewBase_Loaded(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as CSharpScriptViewModel;
            if (viewModel == null) return;
            viewModel.ClosedView = true;
            CommonMethods.GetModuleList(viewModel.ModuleParam, viewModel.Modules);
            tcModuleList.ItemsSource = null;
            tcModuleList.ItemsSource = viewModel.Modules;
            tcModuleList.SelectedIndex = 0;
            editor.FontFamily = new FontFamily("Consolas");
            editor.FontSize = 12;
            editor.Completion = new CSharpCompletion(new ScriptProvider(), ScriptProvider.GetRelativeAssemblies()); ;
            editor.SetCsharpText(viewModel.CsharpText);
        }
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as CSharpScriptViewModel;
            if (viewModel == null) return;
            viewModel.ModuleView = null;
            this.Close();
        }

        private void DataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var viewModel = DataContext as CSharpScriptViewModel;
            if (viewModel == null) return;
            if (viewModel.ModuleIndex == -1 || viewModel.Var == null) return;
            string str = "";
            string varName = $"&{viewModel.Modules[viewModel.ModuleIndex].DisplayName}.{viewModel.Var.Name}";
            if (viewModel.Var.DataType == "double")
            {
                str = $"GetDouble(\"{varName}\")";
            }
            else if (viewModel.Var.DataType == "int")
            {
                str = $"GetInt(\"{varName}\")";
            }
            else if (viewModel.Var.DataType == "bool")
            {
                str = $"GetBool(\"{varName}\")";
            }
            else if (viewModel.Var.DataType == "string")
            {
                str = $"GetString(\"{varName}\")";
            }
            else
            {
                return;
            }
            editor.Document.Insert(editor.SelectionStart, str);//在固定字符处插入内容
        }
    }
}
