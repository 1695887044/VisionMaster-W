using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using ICSharpCode.WpfDesign.Designer.Services;
using VM.Start.ViewModels;

namespace VM.Start.UIDesign
{
    public partial class ErrorListView : ListBox
    {
        public ErrorListView()
        {
            this.InitializeComponent();
        }

        protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
        {
            XamlError xamlError = e.GetDataContext() as XamlError;
            if (xamlError != null)
            {
                UIDesignViewModel.Ins.JumpToError(xamlError);
            }
        }
    }
}
