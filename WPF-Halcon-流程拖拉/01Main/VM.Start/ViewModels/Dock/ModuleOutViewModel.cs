using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Common.Helper;
using VM.Start.Core;
using VM.Start.Dialogs.Views;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.Services;
using VM.Start.Views.Dock;

namespace VM.Start.ViewModels.Dock
{
    public class ModuleOutViewModel:NotifyPropertyBase
    {
        #region Singleton
        private static readonly ModuleOutViewModel _instance = new ModuleOutViewModel();

        private ModuleOutViewModel()
        {
            EventMgrLib.EventMgr.Ins.GetEvent<ModuleOutChangedEvent>().Subscribe(OnModuleOutChanged);
        }


        public static ModuleOutViewModel Ins
        {
            get { return _instance; }
        }
        #endregion

        #region Prop
        private DataTable _GlobalVars = new DataTable();

        public DataTable GlobalVars
        {
            get { return _GlobalVars; }
            set { _GlobalVars = value; RaisePropertyChanged(); }
        }
        private DataTable _Modules = new DataTable();

        public DataTable Modules
        {
            get { return _Modules; }
            set { _Modules = value; RaisePropertyChanged(); }
        }
        private string _ModuleName;

        public string ModuleName
        {
            get { return _ModuleName; }
            set { _ModuleName = value; RaisePropertyChanged(); }
        }

        #endregion

        #region Method
        private void OnModuleOutChanged()
        {
            Common.CommonMethods.UIAsync(() =>
            {
                #region 全局变量
                if (Solution.Ins.SysVar.Count > 0)
                {
                    ModuleOutView.Ins.dgGlobalVar.ItemsSource = null;
                    GlobalVars.Columns.Clear();
                    GlobalVars.Rows.Clear();
                    foreach (var item in Solution.Ins.SysVar)
                    {
                        GlobalVars.Columns.Add(new DataColumn(item.Name));
                    }
                    DataRow row = GlobalVars.NewRow();
                    for (int i = 0; i < Solution.Ins.SysVar.Count; i++)
                    {
                        row[GlobalVars.Columns[i].ColumnName] = Solution.Ins.SysVar[i].Value.ToString();
                    }
                    GlobalVars.Rows.Add(row);
                    ModuleOutView.Ins.dgGlobalVar.ItemsSource = GlobalVars.DefaultView;
                    ModuleOutView.Ins.gdGlobalVar.Visibility = System.Windows.Visibility.Visible;
                }
                else
                {
                    ModuleOutView.Ins.gdGlobalVar.Visibility = System.Windows.Visibility.Collapsed;
                }
                #endregion

                #region 模块变量
                if (ProcessView.Ins.moduleTree.SelectedItem == null) return;
                ModuleNode node = (ProcessView.Ins.moduleTree.SelectedItem as ModuleNode);
                if (node == null) return;
                string moduleName = node.DisplayName;
                ModuleBase moduleObj = Solution.Ins.CurrentProject.GetModuleByName(moduleName);
                if (moduleObj == null) return;
                if (!Solution.Ins.CurrentProject.OutputMap.ContainsKey(moduleObj.ModuleParam.ModuleName)) return;
                Dictionary<string, VarModel> modules = Solution.Ins.CurrentProject.OutputMap[moduleObj.ModuleParam.ModuleName];
                ModuleOutView.Ins.dgModule.ItemsSource = null;
                Modules.Columns.Clear();
                Modules.Rows.Clear();
                foreach (var item in modules)
                {
                    Modules.Columns.Add(new DataColumn(item.Key));
                }
                DataRow row2 = Modules.NewRow();
                for (int i = 0; i < modules.Count; i++)
                {
                    if (modules[Modules.Columns[i].ColumnName].Value == null)
                    {
                        row2[Modules.Columns[i].ColumnName] = "null";
                    }
                    else
                    {
                        row2[Modules.Columns[i].ColumnName] = modules[Modules.Columns[i].ColumnName].Value.ToString();
                    }
                }
                Modules.Rows.Add(row2);
                ModuleName = moduleName + ":";
                ModuleOutView.Ins.dgModule.ItemsSource = Modules.DefaultView;
                #endregion
            });
        }

        #endregion
    }
}
