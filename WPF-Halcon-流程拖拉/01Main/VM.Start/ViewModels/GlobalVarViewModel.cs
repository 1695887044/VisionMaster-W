using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using EventMgrLib;
using HalconDotNet;
using Microsoft.Win32;
using VM.Start.Common;
using VM.Start.Common.Enums;
using VM.Start.Common.Helper;
using VM.Start.Dialogs.Views;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.PersistentData;
using VM.Start.Services;
using VM.Start.Views;
using WPFLocalizeExtension.Engine;

namespace VM.Start.ViewModels
{
    public class GlobalVarViewModel : NotifyPropertyBase
    {
        #region Singleton

        private static readonly GlobalVarViewModel _instance = new GlobalVarViewModel();

        private GlobalVarViewModel()
        {
            this.RecipeList = CommonMethods.GetFilesName(FilePaths.RecipePath, "*.rep");
            this.CurrentRecipe = SystemConfig.Ins.CurrentRecipe;
        }

        public static GlobalVarViewModel Ins
        {
            get { return _instance; }
        }

        #endregion

        #region Prop
        private ObservableCollection<string> _RecipeList;
        public ObservableCollection<string> RecipeList
        {
            get { return this._RecipeList; }
            set
            {
                base.Set<ObservableCollection<string>>(
                    ref this._RecipeList,
                    value,
                    null,
                    "RecipeList"
                );
            }
        }
        private ObservableCollection<VarModel> _SysVar = new ObservableCollection<VarModel>();
        private string _CurrentRecipe;
        public string CurrentRecipe
        {
            get { return this._CurrentRecipe; }
            set { base.Set<string>(ref this._CurrentRecipe, value, null, "CurrentRecipe"); }
        }

        /// <summary>
        /// 系统变量
        /// </summary>
        public ObservableCollection<VarModel> SysVar
        {
            get { return _SysVar; }
            set
            {
                _SysVar = value;
                RaisePropertyChanged();
            }
        }

        private bool bSelectionChangedFlag = false;
        #endregion

        #region Command

        private CommandBase _ActivatedCommand;
        public CommandBase ActivatedCommand
        {
            get
            {
                if (_ActivatedCommand == null)
                {
                    _ActivatedCommand = new CommandBase(
                        (obj) =>
                        {
                            if (GlobalVarView.Ins.IsClosed)
                            {
                                GlobalVarView.Ins.IsClosed = false;
                                SysVar = CloneObject.DeepCopy(Solution.Ins.SysVar);
                            }
                        }
                    );
                }
                return _ActivatedCommand;
            }
        }
        private CommandBase _ConfirmCommand;
        public CommandBase ConfirmCommand
        {
            get
            {
                if (_ConfirmCommand == null)
                {
                    _ConfirmCommand = new CommandBase(
                        (obj) =>
                        {
                            Solution.Ins.SysVar = SysVar;
                            EventMgrLib.EventMgr.Ins.GetEvent<ModuleOutChangedEvent>().Publish();
                            GlobalVarView.Ins.Close();
                        }
                    );
                }
                return _ConfirmCommand;
            }
        }
        private CommandBase _AddVarCommand;
        public CommandBase AddVarCommand
        {
            get
            {
                if (_AddVarCommand == null)
                {
                    _AddVarCommand = new CommandBase(
                        (obj) =>
                        {
                            switch (obj)
                            {
                                case "int":
                                    this.SysVar.Add(
                                        new VarModel
                                        {
                                            Index = this.SysVar.Count + 1,
                                            Name = CommonMethods.GetNewVarName(
                                                obj.ToString(),
                                                this.SysVar
                                            ),
                                            DataType = obj.ToString(),
                                            Value = 0,
                                            Note = "32位整数类型"
                                        }
                                    );
                                    break;
                                case "double":
                                    this.SysVar.Add(
                                        new VarModel
                                        {
                                            Index = this.SysVar.Count + 1,
                                            Name = CommonMethods.GetNewVarName(
                                                obj.ToString(),
                                                this.SysVar
                                            ),
                                            DataType = obj.ToString(),
                                            Value = 0,
                                            Note = "双精度浮点类型"
                                        }
                                    );
                                    break;
                                case "string":
                                    this.SysVar.Add(
                                        new VarModel
                                        {
                                            Index = this.SysVar.Count + 1,
                                            Name = CommonMethods.GetNewVarName(
                                                obj.ToString(),
                                                this.SysVar
                                            ),
                                            DataType = obj.ToString(),
                                            Value = "",
                                            Note = "字符串类型"
                                        }
                                    );
                                    break;
                                case "bool":
                                    this.SysVar.Add(
                                        new VarModel
                                        {
                                            Index = this.SysVar.Count + 1,
                                            Name = CommonMethods.GetNewVarName(
                                                obj.ToString(),
                                                this.SysVar
                                            ),
                                            DataType = obj.ToString(),
                                            Value = false,
                                            Note = "True为真，False为假"
                                        }
                                    );
                                    break;
                                case "short":
                                    this.SysVar.Add(
                                        new VarModel
                                        {
                                            Index = this.SysVar.Count + 1,
                                            Name = CommonMethods.GetNewVarName(
                                                obj.ToString(),
                                                this.SysVar
                                            ),
                                            DataType = obj.ToString(),
                                            Value = 0,
                                            Note = "16位整数类型"
                                        }
                                    );
                                    break;
                                case "int[]":
                                    this.SysVar.Add(
                                        new VarModel
                                        {
                                            Index = this.SysVar.Count + 1,
                                            Name = CommonMethods.GetNewVarName(
                                                "intAry",
                                                this.SysVar
                                            ),
                                            DataType = obj.ToString(),
                                            Value = new List<int>(),
                                            Note = "整数数组类型"
                                        }
                                    );
                                    break;
                                case "bool[]":
                                    this.SysVar.Add(
                                        new VarModel
                                        {
                                            Index = this.SysVar.Count + 1,
                                            Name = CommonMethods.GetNewVarName(
                                                "boolAry",
                                                this.SysVar
                                            ),
                                            DataType = obj.ToString(),
                                            Value = new List<bool>(),
                                            Note = "bool数组类型"
                                        }
                                    );
                                    break;
                                case "double[]":
                                    this.SysVar.Add(
                                        new VarModel
                                        {
                                            Index = this.SysVar.Count + 1,
                                            Name = CommonMethods.GetNewVarName(
                                                "doubleAry",
                                                this.SysVar
                                            ),
                                            DataType = obj.ToString(),
                                            Value = new List<double>(),
                                            Note = "双精度浮点数组类型"
                                        }
                                    );
                                    break;
                                case "Region":
                                    SysVar.Add(
                                        new VarModel()
                                        {
                                            Index = SysVar.Count + 1,
                                            Name =
                                                obj.ToString()
                                                + SysVar
                                                    .Where(o => o.DataType == obj.ToString())
                                                    .ToArray()
                                                    .Length.ToString(),
                                            DataType = obj.ToString(),
                                            Value = new HRegion(10.0, 10, 5),
                                            Note = "区域"
                                        }
                                    );
                                    break;
                                default:
                                    break;
                            }
                            UpdateIndex();
                        }
                    );
                }
                return _AddVarCommand;
            }
        }
        private CommandBase _DeleteCommand;
        public CommandBase DeleteCommand
        {
            get
            {
                if (_DeleteCommand == null)
                {
                    _DeleteCommand = new CommandBase(
                        (obj) =>
                        {
                            if (GlobalVarView.Ins.dg.SelectedIndex == -1)
                                return;
                            SysVar.RemoveAt(GlobalVarView.Ins.dg.SelectedIndex);
                            UpdateIndex();
                        }
                    );
                }
                return _DeleteCommand;
            }
        }
        private CommandBase _MoveCommand;
        public CommandBase MoveCommand
        {
            get
            {
                if (_MoveCommand == null)
                {
                    _MoveCommand = new CommandBase(
                        (obj) =>
                        {
                            switch (obj)
                            {
                                case "Up":
                                    if (
                                        GlobalVarView.Ins.dg.SelectedIndex <= 0 || SysVar.Count <= 1
                                    )
                                        return;
                                    SysVar.Move(
                                        GlobalVarView.Ins.dg.SelectedIndex,
                                        GlobalVarView.Ins.dg.SelectedIndex - 1
                                    );
                                    UpdateIndex();
                                    break;
                                case "Down":
                                    if (
                                        GlobalVarView.Ins.dg.SelectedIndex == -1
                                        || SysVar.Count <= 1
                                        || GlobalVarView.Ins.dg.SelectedIndex == (SysVar.Count - 1)
                                    )
                                        return;
                                    SysVar.Move(
                                        GlobalVarView.Ins.dg.SelectedIndex,
                                        GlobalVarView.Ins.dg.SelectedIndex + 1
                                    );
                                    UpdateIndex();
                                    break;
                                default:
                                    break;
                            }
                        }
                    );
                }
                return _MoveCommand;
            }
        }

        private CommandBase _CobCurrentRepice_SelectionChanged;
        public CommandBase CobCurrentRepice_SelectionChanged
        {
            get
            {
                if (this._CobCurrentRepice_SelectionChanged == null)
                {
                    this._CobCurrentRepice_SelectionChanged = new CommandBase(
                        delegate(object obj)
                        {
                            if (this.bSelectionChangedFlag)
                            {
                                this.bSelectionChangedFlag = false;
                            }
                            else
                            {
                                MessageView ins = MessageView.Ins;
                                ins.MessageBoxShow(
                                    "确定更换该配方吗？",
                                    eMsgType.Warn,
                                    MessageBoxButton.OKCancel,
                                    true
                                );
                                bool? dialogResult = ins.DialogResult;
                                if (!dialogResult.GetValueOrDefault() & dialogResult != null)
                                {
                                    this.bSelectionChangedFlag = true;
                                    GlobalVarView.Ins.cobCurrentRepice.SelectedIndex = SystemConfig
                                        .Ins
                                        .CurrentRecipeIndex;
                                }
                                else
                                {
                                    Solution.Ins.SysVar = SerializeHelp.Deserialize<
                                        ObservableCollection<VarModel>
                                    >(
                                        FilePaths.RecipePath
                                            + GlobalVarView.Ins.cobCurrentRepice.SelectedItem.ToString(),
                                        false
                                    );
                                    SystemConfig.Ins.CurrentRecipe =
                                        GlobalVarView.Ins.cobCurrentRepice.SelectedItem.ToString();
                                    SystemConfig.Ins.CurrentRecipeIndex = GlobalVarView
                                        .Ins
                                        .cobCurrentRepice
                                        .SelectedIndex;
                                    SystemConfig.Ins.SaveSystemConfig();
                                    GlobalVarView.Ins.IsClosed = true;
                                    this.ActivatedCommand.Execute(1);
                                    EventMgr.Ins
                                        .GetEvent<RecipeChangedEvent>()
                                        .Publish(Solution.Ins.SysVar);
                                }
                            }
                        }
                    );
                }
                return this._CobCurrentRepice_SelectionChanged;
            }
        }

        private CommandBase _NewRepiceCommand;

        public CommandBase NewRepiceCommand
        {
            get
            {
                if (this._NewRepiceCommand == null)
                {
                    this._NewRepiceCommand = new CommandBase(
                        delegate(object obj)
                        {
                            SaveFileDialog saveFileDialog = new SaveFileDialog();
                            saveFileDialog.FileName = "New file";
                            saveFileDialog.DefaultExt = ".rep";
                            saveFileDialog.Title = "New an Repice file";
                            saveFileDialog.Filter = "(.rep)|*.rep";
                            saveFileDialog.InitialDirectory = FilePaths.RecipePath;
                            string text = "";
                            bool? flag = saveFileDialog.ShowDialog();
                            if (flag.GetValueOrDefault() & flag != null)
                            {
                                text = saveFileDialog.SafeFileName;
                                SerializeHelp.SerializeAndSaveFile<ObservableCollection<VarModel>>(
                                    Solution.Ins.SysVar,
                                    FilePaths.RecipePath + text,
                                    false
                                );
                            }
                            this.RecipeList = CommonMethods.GetFilesName(
                                FilePaths.RecipePath,
                                "*.rep"
                            );
                            GlobalVarView.Ins.cobCurrentRepice.SelectedIndex =
                                this.RecipeList.IndexOf(text);
                        }
                    );
                }
                return this._NewRepiceCommand;
            }
        }
        private CommandBase _SaveRepiceCommand;

        public CommandBase SaveRepiceCommand
        {
            get
            {
                if (this._SaveRepiceCommand == null)
                {
                    this._SaveRepiceCommand = new CommandBase(
                        delegate(object obj)
                        {
                            SerializeHelp.SerializeAndSaveFile<ObservableCollection<VarModel>>(
                                this.SysVar,
                                FilePaths.RecipePath + SystemConfig.Ins.CurrentRecipe,
                                false
                            );
                        }
                    );
                }
                return this._SaveRepiceCommand;
            }
        }

        private CommandBase _DeleteRepiceCommand;

        public CommandBase DeleteRepiceCommand
        {
            get
            {
                if (this._DeleteRepiceCommand == null)
                {
                    this._DeleteRepiceCommand = new CommandBase(
                        delegate(object obj)
                        {
                            MessageView ins = MessageView.Ins;
                            ins.MessageBoxShow(
                                "确认删除当前" + this.CurrentRecipe + "配方吗?",
                                eMsgType.Warn,
                                MessageBoxButton.OKCancel,
                                true
                            );
                            bool? dialogResult = ins.DialogResult;
                            if (dialogResult.GetValueOrDefault() & dialogResult != null)
                            {
                                if (this.RecipeList.Count == 1)
                                {
                                    MessageView.Ins.MessageBoxShow(
                                        "违规操作，不能删除最后一个配方！",
                                        eMsgType.Warn,
                                        MessageBoxButton.OK,
                                        true
                                    );
                                }
                                else if (File.Exists(FilePaths.RecipePath + this.CurrentRecipe))
                                {
                                    File.Delete(FilePaths.RecipePath + this.CurrentRecipe);
                                    this.bSelectionChangedFlag = true;
                                    this.RecipeList = CommonMethods.GetFilesName(
                                        FilePaths.RecipePath,
                                        "*.rep"
                                    );
                                    this.bSelectionChangedFlag = true;
                                    GlobalVarView.Ins.cobCurrentRepice.SelectedIndex = 0;
                                    Solution.Ins.SysVar = SerializeHelp.Deserialize<
                                        ObservableCollection<VarModel>
                                    >(
                                        FilePaths.RecipePath
                                            + GlobalVarView.Ins.cobCurrentRepice.SelectedItem.ToString(),
                                        false
                                    );
                                    SystemConfig.Ins.CurrentRecipe =
                                        GlobalVarView.Ins.cobCurrentRepice.SelectedItem.ToString();
                                    SystemConfig.Ins.CurrentRecipeIndex = GlobalVarView
                                        .Ins
                                        .cobCurrentRepice
                                        .SelectedIndex;
                                    SystemConfig.Ins.SaveSystemConfig();
                                    GlobalVarView.Ins.IsClosed = true;
                                    this.ActivatedCommand.Execute(1);
                                }
                            }
                        }
                    );
                }
                return this._DeleteRepiceCommand;
            }
        }
        private CommandBase _LoadedCommand;

        public CommandBase LoadedCommand
        {
            get
            {
                if (this._LoadedCommand == null)
                {
                    this._LoadedCommand = new CommandBase(
                        delegate(object obj)
                        {
                            if (this.RecipeList.Count == 0)
                            {
                                File.Create(
                                        FilePaths.RecipePath + "\\" + SystemConfig.Ins.CurrentRecipe
                                    )
                                    .Close();
                                this.RecipeList = CommonMethods.GetFilesName(
                                    FilePaths.RecipePath,
                                    "*.rep"
                                );
                            }
                            if (
                                SystemConfig.Ins.CurrentRecipeIndex
                                >= GlobalVarView.Ins.cobCurrentRepice.Items.Count
                            )
                            {
                                GlobalVarView.Ins.cobCurrentRepice.SelectedIndex = 0;
                                SystemConfig.Ins.CurrentRecipeIndex = GlobalVarView
                                    .Ins
                                    .cobCurrentRepice
                                    .SelectedIndex;
                                SystemConfig.Ins.CurrentRecipe =
                                    GlobalVarView.Ins.cobCurrentRepice.SelectedItem.ToString();
                                SystemConfig.Ins.SaveSystemConfig();
                            }
                            else
                            {
                                GlobalVarView.Ins.cobCurrentRepice.SelectedIndex = SystemConfig
                                    .Ins
                                    .CurrentRecipeIndex;
                                if (
                                    SystemConfig.Ins.CurrentRecipe
                                    != GlobalVarView.Ins.cobCurrentRepice.SelectedItem.ToString()
                                )
                                {
                                    SystemConfig.Ins.CurrentRecipe =
                                        GlobalVarView.Ins.cobCurrentRepice.SelectedItem.ToString();
                                    SystemConfig.Ins.SaveSystemConfig();
                                }
                            }
                        }
                    );
                }
                return this._LoadedCommand;
            }
        }
        #endregion

        #region Method
        private void UpdateIndex()
        {
            if (SysVar.Count == 0)
                return;
            for (int i = 0; i < SysVar.Count; i++)
            {
                SysVar[i].Index = i;
            }
        }
        #endregion
    }
}
