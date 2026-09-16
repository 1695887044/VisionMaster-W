using EventMgrLib;
using HalconDotNet;
using Plugin.VarDefine.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
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

namespace Plugin.VarDefine.ViewModels
{
    // Token: 0x02000005 RID: 5
    [Category("变量工具")]
    [DisplayName("变量定义")]
    [ModuleImageName("VarDefine")]
    [Serializable]
    public class VarDefineViewModel : ModuleBase
    {
        // Token: 0x0600001F RID: 31 RVA: 0x0000305C File Offset: 0x0000125C
        public override bool ExeModule()
        {
            base.Stopwatch.Restart();
            bool result;
            try
            {
                bool isAlwaysExe = this.IsAlwaysExe;
                if (isAlwaysExe)
                {
                    bool flag = this.Task_Compile != null && this.Task_Compile.Count > 0;
                    if (flag)
                    {
                        Task.WaitAll(this.Task_Compile.ToArray());
                    }
                    foreach (VarModel item in this.LocalVar)
                    {
                        bool flag2 = item.Expression == "" || item.Expression == "NULL";
                        if (!flag2)
                        {
                            bool flag3 = !item.IsCompileSuccess;
                            if (flag3)
                            {
                                item.m_TempScriptSupport.Source =
                                    ExpressionScriptTemplate.GetScriptCode(
                                        base.ModuleParam.ProjectID,
                                        base.ModuleParam.ModuleName,
                                        item.Expression
                                    );
                                bool flag4 = !item.m_TempScriptSupport.Compile();
                                if (flag4)
                                {
                                    item.IsCompileSuccess = false;
                                    base.ChangeModuleRunStatus(eRunStatus.NG);
                                    return false;
                                }
                                item.Value = item.m_TempScriptSupport.CodeRun();
                                item.IsCompileSuccess = true;
                            }
                            else
                            {
                                item.Value = item.m_TempScriptSupport.CodeRun();
                            }
                        }
                    }
                }
                base.ChangeModuleRunStatus(eRunStatus.OK);
                result = true;
            }
            catch (Exception ex)
            {
                base.ChangeModuleRunStatus(eRunStatus.NG);
                Logger.GetExceptionMsg(ex, "", true);
                result = false;
            }
            return result;
        }

        // Token: 0x06000020 RID: 32 RVA: 0x00003218 File Offset: 0x00001418
        public override void AddOutputParams()
        {
            base.AddOutputParams();
            foreach (VarModel item in this.LocalVar)
            {
                bool flag = !this.IsAlwaysExe;
                if (flag)
                {
                    bool flag2 = base.Prj.OutputMap.ContainsKey(base.ModuleParam.ModuleName);
                    if (flag2)
                    {
                        bool flag3 = base.Prj.OutputMap[base.ModuleParam.ModuleName].ContainsKey(
                            item.Name
                        );
                        if (flag3)
                        {
                            base.Prj.OutputMap[base.ModuleParam.ModuleName][item.Name].Note =
                                item.Note;
                            break;
                        }
                    }
                }
                base.AddOutputParam(item.Name, item.DataType, item.Value, item.Note);
            }
        }

        // Token: 0x06000021 RID: 33 RVA: 0x00003324 File Offset: 0x00001524
        public override void CompileScript()
        {
            bool flag = this.Task_Compile == null;
            if (flag)
            {
                this.Task_Compile = new List<Task>();
            }
            foreach (VarModel item in this.LocalVar)
            {
                bool flag2 = item.Expression == "" || item.Expression == "NULL";
                if (!flag2)
                {
                    VarModel varModel = item;
                    this.Task_Compile.Add(
                        Task.Run(
                            delegate()
                            {
                                varModel.m_TempScriptSupport.Source =
                                    ExpressionScriptTemplate.GetScriptCode(
                                        this.ModuleParam.ProjectID,
                                        this.ModuleParam.ModuleName,
                                        varModel.Expression
                                    );
                                bool flag3 = !varModel.m_TempScriptSupport.Compile();
                                if (flag3)
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
        }

        // Token: 0x17000004 RID: 4
        // (get) Token: 0x06000022 RID: 34 RVA: 0x000033E8 File Offset: 0x000015E8
        // (set) Token: 0x06000023 RID: 35 RVA: 0x00003400 File Offset: 0x00001600
        public int SelectedIndex
        {
            get { return this._SelectedIndex; }
            set
            {
                this._SelectedIndex = value;
                base.RaisePropertyChanged("SelectedIndex");
            }
        }

        // Token: 0x17000005 RID: 5
        // (get) Token: 0x06000024 RID: 36 RVA: 0x00003418 File Offset: 0x00001618
        // (set) Token: 0x06000025 RID: 37 RVA: 0x00003430 File Offset: 0x00001630
        public bool IsAlwaysExe
        {
            get { return this._IsAlwaysExe; }
            set
            {
                this._IsAlwaysExe = value;
                base.RaisePropertyChanged("IsAlwaysExe");
            }
        }

        // Token: 0x17000006 RID: 6
        // (get) Token: 0x06000026 RID: 38 RVA: 0x00003448 File Offset: 0x00001648
        // (set) Token: 0x06000027 RID: 39 RVA: 0x0000347A File Offset: 0x0000167A
        public ObservableCollection<VarModel> LocalVar
        {
            get
            {
                bool flag = this._LocalVar == null;
                if (flag)
                {
                    this._LocalVar = new ObservableCollection<VarModel>();
                }
                return this._LocalVar;
            }
            set { this._LocalVar = value; }
        }

        // Token: 0x17000007 RID: 7
        // (get) Token: 0x06000028 RID: 40 RVA: 0x00003484 File Offset: 0x00001684
        // (set) Token: 0x06000029 RID: 41 RVA: 0x000034B6 File Offset: 0x000016B6
        public ObservableCollection<ModuleList> Modules
        {
            get
            {
                bool flag = this._Modules == null;
                if (flag)
                {
                    this._Modules = new ObservableCollection<ModuleList>();
                }
                return this._Modules;
            }
            set { this._Modules = value; }
        }

        // Token: 0x0600002A RID: 42 RVA: 0x000034C0 File Offset: 0x000016C0
        public override void Loaded()
        {
            base.Loaded();
            this.ClosedView = true;
            foreach (VarModel item in this.LocalVar)
            {
                bool flag = base.Prj.OutputMap.ContainsKey(base.ModuleParam.ModuleName);
                if (flag)
                {
                    bool flag2 = base.Prj.OutputMap[base.ModuleParam.ModuleName].ContainsKey(
                        item.Name
                    );
                    if (flag2)
                    {
                        bool flag3 =
                            base.Prj.OutputMap[base.ModuleParam.ModuleName][item.Name].Value
                            != null;
                        if (flag3)
                        {
                            item.Value = base.Prj.OutputMap[base.ModuleParam.ModuleName][
                                item.Name
                            ].Value;
                        }
                    }
                }
            }
        }

        // Token: 0x17000008 RID: 8
        // (get) Token: 0x0600002B RID: 43 RVA: 0x000035D8 File Offset: 0x000017D8
        public CommandBase ConfirmCommand
        {
            get
            {
                bool flag = this._ConfirmCommand == null;
                if (flag)
                {
                    this._ConfirmCommand = new CommandBase(
                        delegate(object obj)
                        {
                            VarDefineView view = base.ModuleView as VarDefineView;
                            bool flag2 = view != null;
                            if (flag2)
                            {
                                string sameName;
                                bool flag3 = CommonMethods.SameNameJudge(
                                    this.LocalVar,
                                    out sameName
                                );
                                if (flag3)
                                {
                                    MessageView.Ins.MessageBoxShow(
                                        "(" + sameName + ")变量重名，请检查！",
                                        eMsgType.Warn,
                                        MessageBoxButton.OK,
                                        true
                                    );
                                }
                                else
                                {
                                    foreach (VarModel item in this.LocalVar)
                                    {
                                        bool flag4 =
                                            item.Expression == "" || item.Expression == "NULL";
                                        if (!flag4)
                                        {
                                            bool flag5 = !item.IsCompileSuccess;
                                            if (flag5)
                                            {
                                                item.m_TempScriptSupport.Source =
                                                    ExpressionScriptTemplate.GetScriptCode(
                                                        base.ModuleParam.ProjectID,
                                                        base.ModuleParam.ModuleName,
                                                        item.Expression
                                                    );
                                                bool flag6 = !item.m_TempScriptSupport.Compile();
                                                if (flag6)
                                                {
                                                    item.IsCompileSuccess = false;
                                                    base.ChangeModuleRunStatus(eRunStatus.NG);
                                                    MessageView.Ins.MessageBoxShow(
                                                        "表达式错误，无法保存！",
                                                        eMsgType.Warn,
                                                        MessageBoxButton.OK,
                                                        true
                                                    );
                                                    return;
                                                }
                                                item.Value = item.m_TempScriptSupport.CodeRun();
                                                item.IsCompileSuccess = true;
                                            }
                                        }
                                    }
                                    base.ChangeModuleRunStatus(eRunStatus.OK);
                                    view.Close();
                                }
                            }
                        }
                    );
                }
                return this._ConfirmCommand;
            }
        }

        // Token: 0x17000009 RID: 9
        // (get) Token: 0x0600002C RID: 44 RVA: 0x00003618 File Offset: 0x00001818
        public CommandBase ExecuteCommand
        {
            get
            {
                bool flag = this._ExecuteCommand == null;
                if (flag)
                {
                    this._ExecuteCommand = new CommandBase(
                        delegate(object obj)
                        {
                            bool temp = this.IsAlwaysExe;
                            this.IsAlwaysExe = true;
                            this.ExeModule();
                            this.IsAlwaysExe = temp;
                        }
                    );
                }
                return this._ExecuteCommand;
            }
        }

        // Token: 0x1700000A RID: 10
        // (get) Token: 0x0600002D RID: 45 RVA: 0x00003658 File Offset: 0x00001858
        public CommandBase AddVarCommand
        {
            get
            {
                bool flag = this._AddVarCommand == null;
                if (flag)
                {
                    this._AddVarCommand = new CommandBase(
                        delegate(object obj)
                        {
                            string text = obj as string;
                            if (text != null)
                            {
                                if (!(text == "int"))
                                {
                                    if (!(text == "double"))
                                    {
                                        if (!(text == "string"))
                                        {
                                            if (!(text == "bool"))
                                            {
                                                if (text == "Region")
                                                {
                                                    this.LocalVar.Add(
                                                        new VarModel
                                                        {
                                                            Index = this.LocalVar.Count + 1,
                                                            Name = CommonMethods.GetNewVarName(
                                                                obj.ToString(),
                                                                this.LocalVar
                                                            ),
                                                            DataType = obj.ToString(),
                                                            Value = new HRegion(10.0, 10.0, 5.0),
                                                            Note = "区域"
                                                        }
                                                    );
                                                }
                                            }
                                            else
                                            {
                                                this.LocalVar.Add(
                                                    new VarModel
                                                    {
                                                        Index = this.LocalVar.Count + 1,
                                                        Name = CommonMethods.GetNewVarName(
                                                            obj.ToString(),
                                                            this.LocalVar
                                                        ),
                                                        DataType = obj.ToString(),
                                                        Value = false,
                                                        Note = "True为真，False为假"
                                                    }
                                                );
                                            }
                                        }
                                        else
                                        {
                                            this.LocalVar.Add(
                                                new VarModel
                                                {
                                                    Index = this.LocalVar.Count + 1,
                                                    Name = CommonMethods.GetNewVarName(
                                                        obj.ToString(),
                                                        this.LocalVar
                                                    ),
                                                    DataType = obj.ToString(),
                                                    Value = "",
                                                    Note = "字符串类型"
                                                }
                                            );
                                        }
                                    }
                                    else
                                    {
                                        this.LocalVar.Add(
                                            new VarModel
                                            {
                                                Index = this.LocalVar.Count + 1,
                                                Name = CommonMethods.GetNewVarName(
                                                    obj.ToString(),
                                                    this.LocalVar
                                                ),
                                                DataType = obj.ToString(),
                                                Value = 0,
                                                Note = "双精度浮点类型"
                                            }
                                        );
                                    }
                                }
                                else
                                {
                                    this.LocalVar.Add(
                                        new VarModel
                                        {
                                            Index = this.LocalVar.Count + 1,
                                            Name = CommonMethods.GetNewVarName(
                                                obj.ToString(),
                                                this.LocalVar
                                            ),
                                            DataType = obj.ToString(),
                                            Value = 0,
                                            Note = "整数类型"
                                        }
                                    );
                                }
                            }
                            this.UpdateIndex();
                        }
                    );
                }
                return this._AddVarCommand;
            }
        }

        // Token: 0x1700000B RID: 11
        // (get) Token: 0x0600002E RID: 46 RVA: 0x00003698 File Offset: 0x00001898
        public CommandBase DeleteCommand
        {
            get
            {
                bool flag = this._DeleteCommand == null;
                if (flag)
                {
                    this._DeleteCommand = new CommandBase(
                        delegate(object obj)
                        {
                            bool flag2 = this.SelectedIndex == -1;
                            if (!flag2)
                            {
                                this.LocalVar.RemoveAt(this.SelectedIndex);
                                this.UpdateIndex();
                            }
                        }
                    );
                }
                return this._DeleteCommand;
            }
        }

        // Token: 0x1700000C RID: 12
        // (get) Token: 0x0600002F RID: 47 RVA: 0x000036D8 File Offset: 0x000018D8
        public CommandBase MoveCommand
        {
            get
            {
                bool flag = this._MoveCommand == null;
                if (flag)
                {
                    this._MoveCommand = new CommandBase(
                        delegate(object obj)
                        {
                            string text = obj as string;
                            if (text != null)
                            {
                                if (!(text == "Up"))
                                {
                                    if (text == "Down")
                                    {
                                        bool flag2 =
                                            this.SelectedIndex == -1
                                            || this.LocalVar.Count <= 1
                                            || this.SelectedIndex == this.LocalVar.Count - 1;
                                        if (!flag2)
                                        {
                                            this.LocalVar.Move(
                                                this.SelectedIndex,
                                                this.SelectedIndex + 1
                                            );
                                            this.UpdateIndex();
                                        }
                                    }
                                }
                                else
                                {
                                    bool flag3 =
                                        this.SelectedIndex <= 0 || this.LocalVar.Count <= 1;
                                    if (!flag3)
                                    {
                                        this.LocalVar.Move(
                                            this.SelectedIndex,
                                            this.SelectedIndex - 1
                                        );
                                        this.UpdateIndex();
                                    }
                                }
                            }
                        }
                    );
                }
                return this._MoveCommand;
            }
        }

        // Token: 0x06000030 RID: 48 RVA: 0x00003718 File Offset: 0x00001918
        private void UpdateIndex()
        {
            bool flag = this.LocalVar.Count == 0;
            if (!flag)
            {
                for (int i = 0; i < this.LocalVar.Count; i++)
                {
                    this.LocalVar[i].Index = i;
                }
            }
        }

        // Token: 0x04000013 RID: 19
        [NonSerialized]
        private List<Task> Task_Compile = new List<Task>();

        // Token: 0x04000014 RID: 20
        [NonSerialized]
        private int _SelectedIndex;

        // Token: 0x04000015 RID: 21
        private bool _IsAlwaysExe;

        // Token: 0x04000016 RID: 22
        private ObservableCollection<VarModel> _LocalVar;

        // Token: 0x04000017 RID: 23
        [NonSerialized]
        private ObservableCollection<ModuleList> _Modules;

        // Token: 0x04000018 RID: 24
        [NonSerialized]
        public ExpressionView expressionView;

        // Token: 0x04000019 RID: 25
        [NonSerialized]
        private CommandBase _ConfirmCommand;

        // Token: 0x0400001A RID: 26
        [NonSerialized]
        private CommandBase _ExecuteCommand;

        // Token: 0x0400001B RID: 27
        [NonSerialized]
        private CommandBase _AddVarCommand;

        // Token: 0x0400001C RID: 28
        [NonSerialized]
        private CommandBase _DeleteCommand;

        // Token: 0x0400001D RID: 29
        [NonSerialized]
        private CommandBase _MoveCommand;
    }
}
