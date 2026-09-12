using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using HalconDotNet;
using Plugin.QueueIn.Views;
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

namespace Plugin.QueueIn.ViewModels
{
    // Token: 0x02000003 RID: 3
    [Category("变量工具")]
    [DisplayName("数据入队")]
    [ModuleImageName("QueueIn")]
    [Serializable]
    public class QueueInViewModel : ModuleBase
    {
        // Token: 0x06000006 RID: 6 RVA: 0x0000215C File Offset: 0x0000035C
        public override bool ExeModule()
        {
            base.Stopwatch.Restart();
            bool result;
            try
            {
                bool flag = this.DataIn.ExeModule();
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

        // Token: 0x06000007 RID: 7 RVA: 0x000021CC File Offset: 0x000003CC
        public override void AddOutputParams()
        {
            base.AddOutputParams();
        }

        // Token: 0x06000008 RID: 8 RVA: 0x000021D8 File Offset: 0x000003D8
        public override void Init()
        {
            base.Init();
            this.DataIn = new DataIn();
            this.DataIn.ModuleParam = base.ModuleParam;
            this.DataIn.QueueIndex = Convert.ToInt32(base.GetLinkValue(this._QueueIndex));
        }

        // Token: 0x17000001 RID: 1
        // (get) Token: 0x06000009 RID: 9 RVA: 0x00002228 File Offset: 0x00000428
        // (set) Token: 0x0600000A RID: 10 RVA: 0x00002230 File Offset: 0x00000430
        public DataIn DataIn { get; set; }

        // Token: 0x17000002 RID: 2
        // (get) Token: 0x0600000B RID: 11 RVA: 0x0000223C File Offset: 0x0000043C
        // (set) Token: 0x0600000C RID: 12 RVA: 0x00002254 File Offset: 0x00000454
        public LinkVarModel QueueIndex
        {
            get { return this._QueueIndex; }
            set
            {
                this._QueueIndex = value;
                base.RaisePropertyChanged("QueueIndex");
                this.DataIn.QueueIndex = (int)base.GetLinkValue(this._QueueIndex);
            }
        }

        // Token: 0x17000003 RID: 3
        // (get) Token: 0x0600000D RID: 13 RVA: 0x00002288 File Offset: 0x00000488
        // (set) Token: 0x0600000E RID: 14 RVA: 0x000022A0 File Offset: 0x000004A0
        public List<string> DataOuts
        {
            get { return this._DataOuts; }
            set { this._DataOuts = value; }
        }

        // Token: 0x17000004 RID: 4
        // (get) Token: 0x0600000F RID: 15 RVA: 0x000022AC File Offset: 0x000004AC
        // (set) Token: 0x06000010 RID: 16 RVA: 0x000022C4 File Offset: 0x000004C4
        public int SelectedIndex_DataOut
        {
            get { return this._SelectedIndex_DataOut; }
            set
            {
                this._SelectedIndex_DataOut = value;
                base.RaisePropertyChanged("SelectedIndex_DataOut");
            }
        }

        // Token: 0x17000005 RID: 5
        // (get) Token: 0x06000011 RID: 17 RVA: 0x000022DC File Offset: 0x000004DC
        // (set) Token: 0x06000012 RID: 18 RVA: 0x000022F4 File Offset: 0x000004F4
        public int SelectedIndex
        {
            get { return this._SelectedIndex; }
            set
            {
                this._SelectedIndex = value;
                base.RaisePropertyChanged("SelectedIndex");
            }
        }

        // Token: 0x17000006 RID: 6
        // (get) Token: 0x06000013 RID: 19 RVA: 0x0000230C File Offset: 0x0000050C
        // (set) Token: 0x06000014 RID: 20 RVA: 0x0000233E File Offset: 0x0000053E
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
        // (get) Token: 0x06000015 RID: 21 RVA: 0x00002348 File Offset: 0x00000548
        // (set) Token: 0x06000016 RID: 22 RVA: 0x0000237A File Offset: 0x0000057A
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

        // Token: 0x06000017 RID: 23 RVA: 0x00002384 File Offset: 0x00000584
        public override void Loaded()
        {
            base.Loaded();
            this.ClosedView = true;
            this.DataOuts.Clear();
            this.DataOuts = DataOut.s_QueueSignDic.Keys.ToList<string>();
        }

        // Token: 0x17000008 RID: 8
        // (get) Token: 0x06000018 RID: 24 RVA: 0x000023B8 File Offset: 0x000005B8
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
                            QueueInView view = base.ModuleView as QueueInView;
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

        // Token: 0x17000009 RID: 9
        // (get) Token: 0x06000019 RID: 25 RVA: 0x000023F8 File Offset: 0x000005F8
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

        // Token: 0x1700000A RID: 10
        // (get) Token: 0x0600001A RID: 26 RVA: 0x00002438 File Offset: 0x00000638
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
        // (get) Token: 0x0600001B RID: 27 RVA: 0x00002478 File Offset: 0x00000678
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
        // (get) Token: 0x0600001C RID: 28 RVA: 0x000024B8 File Offset: 0x000006B8
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

        // Token: 0x0600001D RID: 29 RVA: 0x000024F8 File Offset: 0x000006F8
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
        private LinkVarModel _QueueIndex = new LinkVarModel { Text = "1" };

        // Token: 0x04000008 RID: 8
        private List<string> _DataOuts = new List<string>();

        // Token: 0x04000009 RID: 9
        private int _SelectedIndex_DataOut = -1;

        // Token: 0x0400000A RID: 10
        [NonSerialized]
        private int _SelectedIndex;

        // Token: 0x0400000B RID: 11
        private ObservableCollection<VarModel> _LocalVar;

        // Token: 0x0400000C RID: 12
        [NonSerialized]
        private ObservableCollection<ModuleList> _Modules;

        // Token: 0x0400000D RID: 13
        [NonSerialized]
        private CommandBase _ConfirmCommand;

        // Token: 0x0400000E RID: 14
        [NonSerialized]
        private CommandBase _ExecuteCommand;

        // Token: 0x0400000F RID: 15
        [NonSerialized]
        private CommandBase _AddVarCommand;

        // Token: 0x04000010 RID: 16
        [NonSerialized]
        private CommandBase _DeleteCommand;

        // Token: 0x04000011 RID: 17
        [NonSerialized]
        private CommandBase _MoveCommand;
    }
}
