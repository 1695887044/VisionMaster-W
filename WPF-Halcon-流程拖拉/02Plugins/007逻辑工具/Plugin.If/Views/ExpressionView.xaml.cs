using ICSharpCode.CodeCompletion;
using MahApps.Metro.Controls;
using Plugin.If.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VM.Start.Common.Enums;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.Dialogs.Views;
using VM.Start.Models;
using VM.Start.Script;
using VM.Start.ViewModels;
using VM.Start.Views;

namespace Plugin.If.Views
{
    /// <summary>
    /// GrabImageModuleView.xaml 的交互逻辑
    /// </summary>
    public partial class ExpressionView : MetroWindow
    {
        public ExpressionViewModel viewModel = null;
        public bool IsCompileSuccess = false;

        public ExpressionView()
        {
            InitializeComponent();
            if (viewModel == null)
            {
                viewModel = new ExpressionViewModel();
            }
            this.DataContext = viewModel;
            winFormHost.Child = viewModel.MyEditer;
            string str = "\r\n\r\n'bool表达式 参考格式:\r\n\r\nGetString(\"接收文本.接收文本\").Contains(\"T1\")'含有字符T1\r\nGetString(\"接收文本.接收文本\") = \"T1\"\r\n\r\nGetBool(\"模块名1.变量名1\") = TRUE\r\nGetBool(\"模块名1.变量名1\") = FALSE\r\nGetBool(\"模块名1.变量名1\") = FALSE AndAlso GetBool(\"模块名2.变量名2\") = FALSE'类似于c#的 &&\r\nGetBool(\"模块名1.变量名1\") = FALSE OrElse GetBool(\"模块名2.变量名2\") = FALSE'类似于c#的 ||\r\n'\r\nGetDouble(\"模块名1.变量名1\") = GetDouble(\"模块名2.变量名2\")'等于\r\nGetDouble(\"模块名1.变量名1\") <> GetDouble(\"模块名2.变量名2\")'不等于\r\nGetDouble(\"模块名1.变量名1\") <= GetDouble(\"模块名2.变量名2\")'小于等于\r\nGetDouble(\"模块名1.变量名1\") < GetDouble(\"模块名2.变量名2\")'小于\r\nGetDouble(\"模块名1.变量名1\") >= GetDouble(\"模块名2.变量名2\")'大于等于\r\nGetDouble(\"模块名1.变量名1\") > GetDouble(\"模块名2.变量名2\")'大于\r\n\r\nGetBool(\"模块名1.变量名1[0]\")\r\nGetBool(\"模块名1.变量名1[模块名2.变量名2]\")";
            tip.IsReadOnly = true;
            tip.FontFamily = new FontFamily("Consolas");
            tip.FontSize = 12;
            tip.Completion = new CSharpCompletion(new ScriptProvider(), ScriptProvider.GetRelativeAssemblies()); ;
            tip.SetCsharpText(str);
        }

        public bool IsClosed { get; set; } = true;

        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;  // cancels the window close
            IsClosed = true;
            this.Hide();      // Programmatically hides the window
        }        

        private void dg_PreviewMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            DataGrid dg = sender as DataGrid;
            if (dg.SelectedItem == null) return;

            var selectModel = tcModuleList.SelectedItem as ModuleList;  //获取到模块名称
            var selectedRow = dg.SelectedItem as VarModel; //获取到变量名称
            string tempStr = "";

            switch (selectedRow.DataType)
            {
                case "bool":
                    tempStr = "GetBool(\"" + "&" + selectModel.DisplayName + "." + selectedRow.Name + "\")";
                    break;
                case "string":
                    tempStr = "GetString(\"" + "&" + selectModel.DisplayName + "." + selectedRow.Name + "\")";
                    break;
                case "double":
                    tempStr = "GetDouble(\"" + "&" + selectModel.DisplayName + "." + selectedRow.Name + "\")";
                    break;
                case "int":
                    tempStr = "GetInt(\"" + "&" + selectModel.DisplayName + "." + selectedRow.Name + "\")";
                    break;
                default:
                    break;
            }
            viewModel.MyEditer.InsertText(viewModel.MyEditer.CurrentPosition, tempStr);
            viewModel.MyEditer.CurrentPosition = viewModel.MyEditer.CurrentPosition + tempStr.Length;
        }

        private void btnCheck_Click(object sender, RoutedEventArgs e)
        {
            CheckCompile();
        }
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            IsCompileSuccess = false;
            this.Close();
        }

        private void btnConfirm_Click(object sender, RoutedEventArgs e)
        {
            CheckCompile();
            if (IsCompileSuccess == false) return;
            this.Close();
        }
        private void CheckCompile()
        {
            viewModel.m_TempScriptSupport.Source = BoolScriptTemplate.GetScriptCode(
                viewModel.m_Param.ProjectID,
                viewModel.m_Param.ModuleName,
                viewModel.MyEditer.Text);
            if (!viewModel.m_TempScriptSupport.Compile())
            {
                IsCompileSuccess = false;
                MessageView.Ins.MessageBoxShow("编译失败！错误信息：" + viewModel.m_TempScriptSupport.ErrorText, eMsgType.Error);
            }
            else
            {
                IsCompileSuccess = true;
                bool b = viewModel.m_TempScriptSupport.CodeRun();
                MessageView.Ins.MessageBoxShow(b.ToString());
            }
        }
    }
}
