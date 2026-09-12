using MahApps.Metro.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using VM.Halcon;
using VM.Start.Common.Helper;
using VM.Start.Services;

namespace VM.Start.Core
{
    public class ModuleViewBase: MetroWindow
    {
        public ModuleViewBase()
        {
            this.WindowStartupLocation= WindowStartupLocation.CenterScreen;
            this.IsMinButtonEnabled= false;
            Loaded += OnLoaded;
        }
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (ModuleBase == null) return;
            DataContext = ModuleBase;
            this.Title = ModuleBase.ModuleParam.ModuleName;
            //ModuleBaseBack = CloneObject.DeepCopy(ModuleBase);
            ModuleBase.Loaded();
        }

        #region Prop
        /// <summary>
        /// ui对应的module
        /// </summary>
        public ModuleBase ModuleBase { get; set; }

        /// <summary>
        /// 备份 取消的时候还原
        /// </summary>
        private ModuleBase ModuleBaseBack;

        [NonSerialized]
        public VMHWindowControl mWindowH;



        #endregion

        #region Method
        private void Cancel()
        {
            Project prj = Solution.Ins.GetProjectById(ModuleBaseBack.ModuleParam.ProjectID);
            prj.RecoverModuleObj(ModuleBaseBack);//还原
            this.Close();
        }
        public bool IsClosed { get; set; } = true;

        protected override void OnClosing(CancelEventArgs e)
        {
            if (ModuleBase != null && ModuleBase.ClosedView)
            {
                ModuleBase.ModuleView = null;
                return;
            }
            e.Cancel = true;  // cancels the window close
            IsClosed = true;
            this.Hide();      // Programmatically hides the window
        }
        protected override void OnActivated(EventArgs e)
        {
            IsClosed = false;
            base.OnActivated(e);
        }
        #endregion
    }
}
