using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using VM.Start.Core;

namespace Plugin.QueueIn.Views
{
    // Token: 0x02000002 RID: 2
    public partial class QueueInView : ModuleViewBase
    {
        // Token: 0x06000001 RID: 1 RVA: 0x00002050 File Offset: 0x00000250
        public QueueInView()
        {
            this.InitializeComponent();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            base.Close();
        }
    }
}
