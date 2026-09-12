using EventMgrLib;
using HalconDotNet;
using Plugin.VarSet.Models;
using Plugin.VarSet.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Common.Engine;
using VM.Start.Attributes;
using VM.Start.Common;
using VM.Start.Common.Enums;
using VM.Start.Common.Helper;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.Dialogs.Views;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.Script;
using VM.Start.Services;
using VM.Start.ViewModels;
using VM.Start.Views;

namespace Plugin.VarSet.ViewModels
{
    [Serializable]
    [Category("变量工具")]
    [DisplayName("变量设置")]
    [ModuleImageName("VarSet")]
    public class VarSetViewModel : ModuleBase
    {
        [NonSerialized]
        private List<Task> Task_Compile = new List<Task>();

        private ObservableCollection<VarSetModel> _VarSet;

        [NonSerialized]
        private VarSetModel _SelectedVar;

        [NonSerialized]
        private int _SelectedIndex;

        [NonSerialized]
        private ObservableCollection<ModuleList> _Modules;

        [NonSerialized]
        public ExpressionView expressionView;

        [NonSerialized]
        private CommandBase _ConfirmCommand;

        [NonSerialized]
        private CommandBase _ExecuteCommand;

        [NonSerialized]
        private CommandBase _AddCommand;

        [NonSerialized]
        private CommandBase _DeleteCommand;

        [NonSerialized]
        private CommandBase _MoveCommand;

        public ObservableCollection<VarSetModel> VarSet
        {
            get
            {
                if (_VarSet == null)
                {
                    _VarSet = new ObservableCollection<VarSetModel>();
                }
                return _VarSet;
            }
            set { _VarSet = value; }
        }

        public VarSetModel SelectedVar
        {
            get { return _SelectedVar; }
            set { _SelectedVar = value; }
        }

        public int SelectedIndex
        {
            get { return _SelectedIndex; }
            set
            {
                _SelectedIndex = value;
                RaisePropertyChanged("SelectedIndex");
            }
        }

        public ObservableCollection<ModuleList> Modules
        {
            get
            {
                if (_Modules == null)
                {
                    _Modules = new ObservableCollection<ModuleList>();
                }
                return _Modules;
            }
            set { _Modules = value; }
        }

        public CommandBase ConfirmCommand
        {
            get
            {
                if (_ConfirmCommand == null)
                {
                    _ConfirmCommand = new CommandBase(
                        delegate
                        {
                            if (base.ModuleView is VarSetView varSetView)
                            {
                                foreach (VarSetModel current in VarSet)
                                {
                                    if (
                                        !(current.Expression == "")
                                        && !(current.Expression == "NULL")
                                        && !current.IsCompileSuccess
                                    )
                                    {
                                        current.m_TempScriptSupport.Source =
                                            ExpressionScriptTemplate.GetScriptCode(
                                                base.ModuleParam.ProjectID,
                                                base.ModuleParam.ModuleName,
                                                current.Expression
                                            );
                                        if (!current.m_TempScriptSupport.Compile())
                                        {
                                            current.IsCompileSuccess = false;
                                            ChangeModuleRunStatus(eRunStatus.NG);
                                            MessageView.Ins.MessageBoxShow(
                                                "表达式错误，无法保存！",
                                                eMsgType.Warn
                                            );
                                            return;
                                        }
                                        current.IsCompileSuccess = true;
                                    }
                                }
                                ChangeModuleRunStatus(eRunStatus.OK);
                                varSetView.Close();
                            }
                        }
                    );
                }
                return _ConfirmCommand;
            }
        }

        public CommandBase ExecuteCommand
        {
            get
            {
                if (_ExecuteCommand == null)
                {
                    _ExecuteCommand = new CommandBase(
                        delegate
                        {
                            ExeModule();
                        }
                    );
                }
                return _ExecuteCommand;
            }
        }

        public CommandBase AddCommand
        {
            get
            {
                if (_AddCommand == null)
                {
                    EventMgr.Ins
                        .GetEvent<VarChangedEvent>()
                        .Subscribe(
                            OnVarChanged,
                            (VarChangedEventParamModel o) => o.SendName.StartsWith($"{ModuleGuid}")
                        );
                    _AddCommand = new CommandBase(
                        delegate
                        {
                            CommonMethods.GetModuleList(
                                base.ModuleParam,
                                VarLinkViewModel.Ins.Modules,
                                "bool,int,double,string,bool[],int[],double[],string[]"
                            );
                            EventMgr.Ins
                                .GetEvent<OpenVarLinkViewEvent>()
                                .Publish($"{ModuleGuid},VarSetLinkText,IsAdd");
                        }
                    );
                }
                return _AddCommand;
            }
        }

        public CommandBase DeleteCommand
        {
            get
            {
                if (_DeleteCommand == null)
                {
                    _DeleteCommand = new CommandBase(
                        delegate
                        {
                            if (SelectedIndex != -1)
                            {
                                VarSet.RemoveAt(SelectedIndex);
                                UpdateIndex();
                            }
                        }
                    );
                }
                return _DeleteCommand;
            }
        }

        public CommandBase MoveCommand
        {
            get
            {
                if (_MoveCommand == null)
                {
                    _MoveCommand = new CommandBase(
                        delegate(object obj)
                        {
                            switch (obj as string)
                            {
                                case "Up":
                                    if (SelectedIndex > 0 && VarSet.Count > 1)
                                    {
                                        VarSet.Move(SelectedIndex, SelectedIndex - 1);
                                        UpdateIndex();
                                    }
                                    break;
                                case "Down":
                                    if (
                                        SelectedIndex != -1
                                        && VarSet.Count > 1
                                        && SelectedIndex != VarSet.Count - 1
                                    )
                                    {
                                        VarSet.Move(SelectedIndex, SelectedIndex + 1);
                                        UpdateIndex();
                                    }
                                    break;
                            }
                        }
                    );
                }
                return _MoveCommand;
            }
        }

        public override bool ExeModule()
        {
            base.Stopwatch.Restart();
            try
            {
                Logger.AddLog($"Test：ExeModule");
                if (Task_Compile != null && Task_Compile.Count > 0)
                {
                    Task.WaitAll(Task_Compile.ToArray());
                }
                foreach (VarSetModel item in VarSet)
                {
                    if (item.Expression == "" || item.Expression == "NULL")
                    {
                        continue;
                    }
                    if (!item.IsCompileSuccess)
                    {
                        item.m_TempScriptSupport.Source = ExpressionScriptTemplate.GetScriptCode(
                            base.ModuleParam.ProjectID,
                            base.ModuleParam.ModuleName,
                            item.Expression
                        );
                        if (!item.m_TempScriptSupport.Compile())
                        {
                            item.IsCompileSuccess = false;
                            ChangeModuleRunStatus(eRunStatus.NG);
                            return false;
                        }
                        VarModel var2 = base.Prj.GetParamByName(item.Link);
                        Logger.AddLog($"Test1：{var2.Value}");
                        if (var2 == null)
                        {
                            Logger.AddLog($"Test：false");
                            ChangeModuleRunStatus(eRunStatus.NG);
                            return false;
                        }
                        item.Value = item.m_TempScriptSupport.CodeRun();
                        var2.Value = item.Value;
                        var2.Note = item.Note;
                        item.IsCompileSuccess = true;
                    }
                    else
                    {
                        VarModel var = base.Prj.GetParamByName(item.Link);
                        Logger.AddLog($"Test2：{var.Value}");
                        if (var == null)
                        {
                            ChangeModuleRunStatus(eRunStatus.NG);
                            return false;
                        }
                        item.Value = item.m_TempScriptSupport.CodeRun();
                        var.Value = item.Value;
                        var.Note = item.Note;
                        Logger.AddLog($"Test3：{var.Value}");
                    }
                }
                ChangeModuleRunStatus(eRunStatus.OK);
                return true;
            }
            catch (Exception ex)
            {
                ChangeModuleRunStatus(eRunStatus.NG);
                Logger.GetExceptionMsg(ex);
                Logger.AddLog($"Test4：{ex.Message}");
                return false;
            }
        }

        public override void AddOutputParams()
        {
            base.AddOutputParams();
            foreach (VarSetModel item in VarSet)
            {
                AddOutputParam(item.Name, item.DataType, item.Value, item.Note);
            }
        }

        public override void CompileScript()
        {
            if (Task_Compile == null)
            {
                Task_Compile = new List<Task>();
            }
            foreach (VarSetModel item in VarSet)
            {
                if (item.Expression == "" || item.Expression == "NULL")
                {
                    continue;
                }
                VarSetModel varModel = item;
                Task_Compile.Add(
                    Task.Run(
                        delegate
                        {
                            varModel.m_TempScriptSupport.Source =
                                ExpressionScriptTemplate.GetScriptCode(
                                    base.ModuleParam.ProjectID,
                                    base.ModuleParam.ModuleName,
                                    varModel.Expression
                                );
                            if (!varModel.m_TempScriptSupport.Compile())
                            {
                                varModel.IsCompileSuccess = false;
                            }
                            else
                            {
                                varModel.IsCompileSuccess = true;
                            }
                        }
                    )
                );
            }
        }

        public override void Loaded()
        {
            base.Loaded();
            ClosedView = true;
            foreach (VarSetModel item in VarSet)
            {
                VarModel var = base.Prj.GetParamByName(item.Link);
                if (var != null)
                {
                    item.Note = var.Note;
                }
                if (
                    base.Prj.OutputMap.ContainsKey(base.ModuleParam.ModuleName)
                    && base.Prj.OutputMap[base.ModuleParam.ModuleName].ContainsKey(item.Name)
                    && base.Prj.OutputMap[base.ModuleParam.ModuleName][item.Name].Value != null
                )
                {
                    item.Value = base.Prj.OutputMap[base.ModuleParam.ModuleName][item.Name].Value;
                }
            }
        }

        private void OnVarChanged(VarChangedEventParamModel obj)
        {
            string[] strings = obj.SendName.Split(',');
            string text = strings[1];
            string text2 = text;
            if (text2 == "VarSetLinkText")
            {
                if (obj.IsAdd)
                {
                    VarSet.Add(
                        new VarSetModel
                        {
                            Index = VarSet.Count,
                            Name = obj.Name,
                            Link = obj.LinkName,
                            DataType = obj.DataType,
                            Note = obj.Note
                        }
                    );
                    SelectedIndex = VarSet.Count - 1;
                }
                else if (SelectedVar != null)
                {
                    SelectedVar.Link = obj.LinkName;
                    SelectedVar.Note = obj.Note;
                }
                UpdateIndex();
            }
        }

        private void UpdateIndex()
        {
            if (VarSet.Count != 0)
            {
                for (int i = 0; i < VarSet.Count; i++)
                {
                    VarSet[i].Index = i;
                }
            }
        }
    }
}
