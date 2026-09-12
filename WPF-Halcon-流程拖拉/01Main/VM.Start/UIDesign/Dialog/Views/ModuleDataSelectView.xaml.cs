using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using MahApps.Metro.Controls;
using VM.Start.UIDesign.Dialog.ViewModels;

namespace VM.Start.UIDesign.Dialog.Views
{
    // Token: 0x020000AC RID: 172
    public partial class ModuleDataSelectView : MetroWindow, IStyleConnector
    {
        private ModuleDataSelectView()
        {
            this.InitializeComponent();
            base.DataContext = ModuleDataSelectViewModel.Ins;
        }

        public static ModuleDataSelectView Ins
        {
            get { return ModuleDataSelectView._instance; }
        }

        public bool IsClosed { get; set; } = true;

        protected override void OnClosing(CancelEventArgs e)
        {
            ModuleDataSelectViewModel.Ins.IsRunOnceProject = false;
            ModuleDataSelectViewModel.Ins.IsModuleSelect = false;
            e.Cancel = true;
            this.IsClosed = true;
            base.Hide();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            base.Close();
        }

        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ModuleDataSelectViewModel.Ins.ConfirmCommand.Execute(0);
        }

        [GeneratedCode("PresentationBuildTasks", "4.0.0.0")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        void IStyleConnector.Connect(int connectionId, object target)
        {
            if (connectionId == 4)
            {
                ((DataGrid)target).MouseDoubleClick += this.DataGrid_MouseDoubleClick;
            }
        }

        private static ModuleDataSelectView _instance = new ModuleDataSelectView();
    }
}
