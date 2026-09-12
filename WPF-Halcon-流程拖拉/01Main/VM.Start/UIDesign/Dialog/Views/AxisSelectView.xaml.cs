using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using MahApps.Metro.Controls;
using VM.Start.UIDesign.Dialog.ViewModels;

namespace VM.Start.UIDesign.Dialog.Views
{
    public partial class AxisSelectView : MetroWindow
    {
        private AxisSelectView()
        {
            this.InitializeComponent();
            base.DataContext = AxisSelectViewModel.Ins;
        }

        public static AxisSelectView Ins
        {
            get { return AxisSelectView._instance; }
        }

        public bool IsClosed { get; set; } = true;

        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            this.IsClosed = true;
            base.Hide();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            base.Close();
        }

        private static AxisSelectView _instance = new AxisSelectView();
    }
}
