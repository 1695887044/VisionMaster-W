using ICSharpCode.WpfDesign.Designer.OutlineView;
using ICSharpCode.WpfDesign.Designer.Services;
using Mono.CSharp;
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
using VM.Start.UIDesign;
using VM.Start.ViewModels;

namespace VM.Start.Views
{
    /// <summary>
    /// UIToolboxView.xaml 的交互逻辑
    /// </summary>
    public partial class UIToolboxView : UserControl
    {
        // Token: 0x060003C7 RID: 967 RVA: 0x0001AD38 File Offset: 0x00018F38
        public UIToolboxView()
        {
            this.InitializeComponent();
            base.DataContext = UIShowTypes.Ins;
            new DragListener(this).DragStarted += this.Toolbox_DragStarted;
            this.uxTreeView.SelectedItemChanged += this.uxTreeView_SelectedItemChanged;
            this.uxTreeView.GotKeyboardFocus += this.uxTreeView_GotKeyboardFocus;
        }

        // Token: 0x17000178 RID: 376
        // (get) Token: 0x060003C8 RID: 968 RVA: 0x0001ADA4 File Offset: 0x00018FA4
        public static UIToolboxView Ins
        {
            get { return UIToolboxView._instance; }
        }

        // Token: 0x060003C9 RID: 969 RVA: 0x00003050 File Offset: 0x00001250
        private void uxTreeView_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            this.PrepareTool(this.uxTreeView.SelectedItem as FooNode, false);
        }

        // Token: 0x060003CA RID: 970 RVA: 0x00003050 File Offset: 0x00001250
        private void uxTreeView_SelectedItemChanged(
            object sender,
            RoutedPropertyChangedEventArgs<object> e
        )
        {
            this.PrepareTool(this.uxTreeView.SelectedItem as FooNode, false);
        }

        // Token: 0x060003CB RID: 971 RVA: 0x00003069 File Offset: 0x00001269
        private void Toolbox_DragStarted(object sender, MouseButtonEventArgs e)
        {
            this.PrepareTool(e.GetDataContext() as FooNode, true);
        }

        // Token: 0x060003CC RID: 972 RVA: 0x0001ADB8 File Offset: 0x00018FB8
        private void PrepareTool(FooNode node, bool drag)
        {
            if (node != null)
            {
                Type typeForFooType = UIShowTypes.Ins.GetTypeForFooType(node.FooType);
                CreateComponentTool createComponentTool = new CreateComponentTool(typeForFooType);
                if (UIDesignViewModel.Ins.CurrentDocument != null)
                {
                    UIDesignViewModel.Ins.CurrentDocument.DesignContext.Services.Tool.CurrentTool =
                        createComponentTool;
                    if (drag)
                    {
                        DragDrop.DoDragDrop(this, createComponentTool, DragDropEffects.Copy);
                    }
                }
            }
        }

        // Token: 0x04000186 RID: 390
        private static readonly UIToolboxView _instance = new UIToolboxView();
    }
}
