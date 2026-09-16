using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using ICSharpCode.AvalonEdit;
using ICSharpCode.WpfDesign.Designer.Services;
using VM.Start.ViewModels;

namespace VM.Start.UIDesign
{
    // Token: 0x0200009F RID: 159
    public partial class DocumentView : UserControl
    {
        // Token: 0x0600071E RID: 1822 RVA: 0x000045BB File Offset: 0x000027BB
        public DocumentView()
        {
            this.InitializeComponent();
            base.Loaded += this.DocumentView_Loaded;
        }

        // Token: 0x0600071F RID: 1823 RVA: 0x0002BEB4 File Offset: 0x0002A0B4
        private void DocumentView_Loaded(object sender, RoutedEventArgs e)
        {
            base.Loaded -= this.DocumentView_Loaded;
            this.Document = (Document)base.DataContext;
            UIDesignViewModel.Ins.Views[this.Document] = this;
            this.uxTextEditor.Text = this.Document.Text;
            this.Document.Mode = DocumentMode.Design;
            this.Document.PropertyChanged += this.Document_PropertyChanged;
            this.uxTextEditor.TextChanged += this.uxTextEditor_TextChanged;
        }

        // Token: 0x06000720 RID: 1824 RVA: 0x000045DB File Offset: 0x000027DB
        private void uxTextEditor_TextChanged(object sender, EventArgs e)
        {
            this.Document.Text = this.uxTextEditor.Text;
        }

        // Token: 0x06000721 RID: 1825 RVA: 0x0002BF4C File Offset: 0x0002A14C
        async void Document_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Text" && Document.Text != uxTextEditor.Text)
                uxTextEditor.Text = Document.Text;
            if (e.PropertyName == "XamlElementLineInfo")
            {
                try
                {
                    await Task.Delay(70);
                    if (Document.XamlElementLineInfo != null)
                    {
                        uxTextEditor.SelectionLength = 0;
                        uxTextEditor.SelectionStart = Document.XamlElementLineInfo.Position;
                        uxTextEditor.SelectionLength = Document.XamlElementLineInfo.Length;
                    }
                    else
                    {
                        uxTextEditor.SelectionStart = 0;
                        uxTextEditor.SelectionLength = 0;
                    }

                    uxTextEditor.Focus();
                }
                catch (Exception) { }
            }
        }

        // Token: 0x17000260 RID: 608
        // (get) Token: 0x06000722 RID: 1826 RVA: 0x000045F3 File Offset: 0x000027F3
        // (set) Token: 0x06000723 RID: 1827 RVA: 0x000045FB File Offset: 0x000027FB
        public Document Document { get; private set; }

        // Token: 0x06000724 RID: 1828 RVA: 0x0002BF94 File Offset: 0x0002A194
        public void JumpToError(XamlError error)
        {
            this.Document.Mode = DocumentMode.Xaml;
            try
            {
                this.uxTextEditor.ScrollTo(error.Line, error.Column);
                this.uxTextEditor.CaretOffset = this.uxTextEditor.Document.GetOffset(
                    error.Line,
                    error.Column
                );
                int num = 0;
                char charAt;
                while (
                    (
                        charAt = this.uxTextEditor.Document.GetCharAt(
                            this.uxTextEditor.CaretOffset + num
                        )
                    ) != ' '
                    && charAt != '.'
                    && charAt != '<'
                    && charAt != '>'
                    && charAt != '"'
                )
                {
                    num++;
                }
                this.uxTextEditor.SelectionLength = num;
            }
            catch (ArgumentException) { }
        }
    }
}
