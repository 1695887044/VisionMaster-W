using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using ICSharpCode.WpfDesign.PropertyGrid;
using VM.Start.UIDesign.Control;
using VM.Start.UIDesign.Dialog.ViewModels;
using VM.Start.UIDesign.Dialog.Views;

namespace VM.Start.UIDesign.Editor
{
    [PropertyEditor(typeof(RunOnceProjectButton), "执行流程")]
    [PropertyEditor(typeof(DataCount), "流程路径")]
    public partial class RunOnceProjectLinkEditor : UserControl
    {
        public RunOnceProjectLinkEditor()
        {
            this.InitializeComponent();
        }

        // Token: 0x06000750 RID: 1872 RVA: 0x0002C834 File Offset: 0x0002AA34
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            ModuleDataSelectViewModel.Ins.GetData("");
            ModuleDataSelectViewModel.Ins.IsRunOnceProject = true;
            ModuleDataSelectView.Ins.ShowDialog();
            if (ModuleDataSelectViewModel.Ins.ResultLinkData != "")
            {
                PropertyNode propertyNode = base.DataContext as PropertyNode;
                propertyNode.ValueString = ModuleDataSelectViewModel.Ins.ResultLinkData;
            }
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            PropertyNode propertyNode = base.DataContext as PropertyNode;
            string valueString = propertyNode.ValueString;
            if (!string.IsNullOrEmpty(valueString))
            {
                string[] array = valueString.Split(new char[] { '.' });
                if (array.Length == 1)
                {
                    this.showText.Text = valueString;
                }
            }
            else
            {
                this.showText.Text = "";
            }
            this.showText.ToolTip = this.showText.Text;
        }
    }
}
