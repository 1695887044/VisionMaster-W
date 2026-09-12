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
    [PropertyEditor(typeof(UITextBox), "tb数据源")]
    [PropertyEditor(typeof(UINumericUpDown), "num数据源")]
    public partial class TextBoxTextLinkEditor : UserControl
    {
        public TextBoxTextLinkEditor()
        {
            this.InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            ModuleDataSelectViewModel.Ins.GetData("double,int,string,short");
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
                if (array.Length == 3)
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
