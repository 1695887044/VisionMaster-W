using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using HalconDotNet;
using Plugin.QueueOut.Views;
using VM.Start.Attributes;
using VM.Start.Common;
using VM.Start.Common.Enums;
using VM.Start.Common.Helper;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.Dialogs.Views;
using VM.Start.Models;
using VM.Start.Script;
using VM.Start.Services;

namespace Plugin.QueueOut.ViewModels
{
    // Token: 0x02000003 RID: 3
    [Category("变量工具")]
    [DisplayName("数据出队")]
    [ModuleImageName("QueueOut")]
    [Serializable]
    public class QueueOutViewModel : ModuleBase
    {
        public override bool ExeModule()
        {
            base.Stopwatch.Restart();
            bool result;
            try
            {
                bool flag = this.DataOut.ExeModule();
                if (flag)
                {
                    base.ChangeModuleRunStatus(eRunStatus.OK);
                    result = true;
                }
                else
                {
                    base.ChangeModuleRunStatus(eRunStatus.NG);
                    result = false;
                }
            }
            catch (Exception ex)
            {
                base.ChangeModuleRunStatus(eRunStatus.NG);
                Logger.GetExceptionMsg(ex, "", true);
                result = false;
            }
            return result;
        }

        // Token: 0x06000007 RID: 7 RVA: 0x000021A4 File Offset: 0x000003A4
        public override void AddOutputParams()
        {
            base.AddOutputParams();
        }

        // Token: 0x06000008 RID: 8 RVA: 0x000021B0 File Offset: 0x000003B0
        public override void Init()
        {
            base.Init();
            this.DataOut = new DataOut(
                base.Prj.ProjectInfo.ProcessName + "." + base.ModuleParam.ModuleName
            );
            this.DataOut.ModuleParam = base.ModuleParam;
        }

        // Token: 0x17000001 RID: 1
        // (get) Token: 0x06000009 RID: 9 RVA: 0x00002208 File Offset: 0x00000408
        // (set) Token: 0x0600000A RID: 10 RVA: 0x00002210 File Offset: 0x00000410
        public DataOut DataOut { get; set; }

        // Token: 0x17000002 RID: 2
        // (get) Token: 0x0600000B RID: 11 RVA: 0x0000221C File Offset: 0x0000041C
        // (set) Token: 0x0600000C RID: 12 RVA: 0x00002234 File Offset: 0x00000434
        public int SelectedIndex
        {
            get { return this._SelectedIndex; }
            set
            {
                this._SelectedIndex = value;
                base.RaisePropertyChanged("SelectedIndex");
            }
        }

        // Token: 0x17000003 RID: 3
        // (get) Token: 0x0600000D RID: 13 RVA: 0x0000224C File Offset: 0x0000044C
        // (set) Token: 0x0600000E RID: 14 RVA: 0x0000227E File Offset: 0x0000047E
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

        // Token: 0x17000004 RID: 4
        // (get) Token: 0x0600000F RID: 15 RVA: 0x00002288 File Offset: 0x00000488
        // (set) Token: 0x06000010 RID: 16 RVA: 0x000022BA File Offset: 0x000004BA
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

        // Token: 0x17000005 RID: 5
        // (get) Token: 0x06000011 RID: 17 RVA: 0x000022C4 File Offset: 0x000004C4
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
                            QueueOutView view = base.ModuleView as QueueOutView;
                            bool flag2 = view != null;
                            if (flag2)
                            {
                                foreach (VarModel item in this.LocalVar)
                                {
                                    bool flag3 = item.Expression == "" || item.Expression == "NULL";
                                    if (!flag3)
                                    {
                                        bool flag4 = !item.IsCompileSuccess;
                                        if (flag4)
                                        {
                                            item.m_TempScriptSupport.Source =
                                                ExpressionScriptTemplate.GetScriptCode(
                                                    base.ModuleParam.ProjectID,
                                                    base.ModuleParam.ModuleName,
                                                    item.Expression
                                                );
                                            bool flag5 = !item.m_TempScriptSupport.Compile();
                                            if (flag5)
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
                    );
                }
                return this._ConfirmCommand;
            }
        }

        // Token: 0x17000006 RID: 6
        // (get) Token: 0x06000012 RID: 18 RVA: 0x00002304 File Offset: 0x00000504
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
                            this.ExeModule();
                        }
                    );
                }
                return this._ExecuteCommand;
            }
        }

        // Token: 0x17000007 RID: 7
        // (get) Token: 0x06000013 RID: 19 RVA: 0x00002344 File Offset: 0x00000544
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
                                                this.DataOut.DefineBoolQueue();
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
                                            this.DataOut.DefineStringQueue();
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
                                        this.DataOut.DefineDoubleQueue();
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
                                    this.DataOut.DefineIntQueue();
                                }
                            }
                            this.UpdateIndex();
                        }
                    );
                }
                return this._AddVarCommand;
            }
        }

        // Token: 0x17000008 RID: 8
        // (get) Token: 0x06000014 RID: 20 RVA: 0x00002384 File Offset: 0x00000584
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

        // Token: 0x17000009 RID: 9
        // (get) Token: 0x06000015 RID: 21 RVA: 0x000023C4 File Offset: 0x000005C4
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

        // Token: 0x06000016 RID: 22 RVA: 0x00002404 File Offset: 0x00000604
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

        // Token: 0x04000007 RID: 7
        [NonSerialized]
        private int _SelectedIndex;

        // Token: 0x04000008 RID: 8
        private ObservableCollection<VarModel> _LocalVar;

        // Token: 0x04000009 RID: 9
        [NonSerialized]
        private ObservableCollection<ModuleList> _Modules;

        // Token: 0x0400000A RID: 10
        [NonSerialized]
        private CommandBase _ConfirmCommand;

        // Token: 0x0400000B RID: 11
        [NonSerialized]
        private CommandBase _ExecuteCommand;

        // Token: 0x0400000C RID: 12
        [NonSerialized]
        private CommandBase _AddVarCommand;

        // Token: 0x0400000D RID: 13
        [NonSerialized]
        private CommandBase _DeleteCommand;

        // Token: 0x0400000E RID: 14
        [NonSerialized]
        private CommandBase _MoveCommand;
    }
}
