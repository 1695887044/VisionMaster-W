using EventMgrLib;
using Plugin.If.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Attributes;
using VM.Start.Common;
using VM.Start.Common.Helper;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.Services;
using VM.Start.ViewModels;
using VM.Start.Views;
using VM.Start.Common.Enums;
using HandyControl.Controls;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;
using System.Windows;
using VM.Start.Script;

namespace Plugin.If.ViewModels
{
    #region enum
    public enum eLinkCommand
    {
        BoolLink,

    }
    #endregion

    [Category("逻辑工具")]
    [DisplayName("条件分支")]
    [ModuleImageName("If")]
    [Serializable]
    public class IfViewModel : ModuleBase
    {
        private void OnVarChanged(VarChangedEventParamModel obj)
        {

            switch (obj.SendName.Split(',')[1])
            {
                case "BoolLinkText":
                    BoolLinkText = obj.LinkName;
                    break;
                default:
                    break;
            }
        }
        public override bool ExeModule()
        {
            Stopwatch.Restart();
            if (ModuleParam.ModuleName.Contains("否则") || ModuleParam.ModuleName.Contains("结束"))
            {
                if (!ModuleParam.ModuleName.Contains("否则如果"))
                {
                    ChangeModuleRunStatus(eRunStatus.OK);
                    return true;
                }
            }
            try
            {
                if (BoolLink)//链接布尔
                {
                    if (BoolLinkText == null)
                    {
                        ChangeModuleRunStatus(eRunStatus.NG);
                        return false;
                    }
                    object str = Prj.GetParamByName(BoolLinkText).Value;
                    if (str != null && (BoolInversion == false ? bool.Parse(str.ToString()) : !bool.Parse(str.ToString())))
                    {
                        ChangeModuleRunStatus(eRunStatus.OK);
                        return true;
                    }
                    else
                    {
                        ChangeModuleRunStatus(eRunStatus.NG);
                        return false;
                    }
                }
                else if (Expression)//链接表达式
                {
                    if (!IsCompileSuccess)
                    {
                        m_TempScriptSupport.Source = BoolScriptTemplate.GetScriptCode(
                             ModuleParam.ProjectID,
                             ModuleParam.ModuleName,
                             ExpressionString);
                        if (!m_TempScriptSupport.Compile())
                        {
                            IsCompileSuccess = false;
                            ChangeModuleRunStatus(eRunStatus.NG);
                            return false;
                        }
                        else
                        {
                            IsCompileSuccess = true;
                        }
                    }
                    if (m_TempScriptSupport.CodeRun())
                    {
                        ChangeModuleRunStatus(eRunStatus.OK);
                        return true;
                    }
                    else
                    {
                        ChangeModuleRunStatus(eRunStatus.NG);
                        return false;
                    }
                }
                else
                {
                    ChangeModuleRunStatus(eRunStatus.NG);
                    return false;
                }
            }
            catch (Exception ex)
            {
                ChangeModuleRunStatus(eRunStatus.NG);
                Logger.GetExceptionMsg(ex);
                return false;
            }
        }

        #region Prop
        [NonSerialized]
        public bool IsCompileSuccess = false;
        [NonSerialized]
        private BoolScriptSupport _m_TempScriptSupport;
        public BoolScriptSupport m_TempScriptSupport
        {
            get 
            {
                if (_m_TempScriptSupport ==null)
                {
                    _m_TempScriptSupport = new BoolScriptSupport();
                }
                return _m_TempScriptSupport; 
            }
            set { _m_TempScriptSupport = value; }
        }
        [NonSerialized]
        public ExpressionView expressionView;

        private bool _BoolInversion;
        /// <summary>
        /// 逻辑取反
        /// </summary>
        public bool BoolInversion
        {
            get { return _BoolInversion; }
            set { _BoolInversion = value; RaisePropertyChanged(); }
        }

        private bool _BoolLink = true;
        /// <summary>
        /// BoolLink
        /// </summary>
        public bool BoolLink
        {
            get { return _BoolLink; }
            set { _BoolLink = value; RaisePropertyChanged(); }
        }
        private bool _Expression;
        /// <summary>
        /// Expression
        /// </summary>
        public bool Expression
        {
            get { return _Expression; }
            set { _Expression = value; RaisePropertyChanged(); }
        }
        /// <summary>
        /// ExpressionString
        /// </summary>
        private string _ExpressionString;
        public string ExpressionString
        {
            get { return _ExpressionString; }
            set { _ExpressionString = value; RaisePropertyChanged(); }
        }

        private string _BoolLinkText;
        /// <summary>
        /// bool链接文本
        /// </summary>
        public string BoolLinkText
        {
            get { return _BoolLinkText; }
            set { _BoolLinkText = value; RaisePropertyChanged(); }
        }
        [NonSerialized]
        private ObservableCollection<ModuleList> _Modules;

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


        #endregion

        #region Command
        [NonSerialized]
        private CommandBase _LinkCommand;
        public CommandBase LinkCommand
        {
            get
            {
                if (_LinkCommand == null)
                {
                    //以类名作为筛选器
                    EventMgr.Ins.GetEvent<VarChangedEvent>().Subscribe(OnVarChanged, o => o.SendName.StartsWith($"{ModuleGuid}"));
                    _LinkCommand = new CommandBase((obj) =>
                    {
                        CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "bool");
                        EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},BoolLinkText");
                    });
                }
                return _LinkCommand;
            }
        }
        [NonSerialized]
        private CommandBase _EditCommand;
        public CommandBase EditCommand
        {
            get
            {
                if (_EditCommand == null)
                {
                    _EditCommand = new CommandBase((obj) =>
                    {
                        if (expressionView == null)
                        {
                            expressionView = new ExpressionView();
                        }
                        CommonMethods.GetModuleList(ModuleParam, Modules);
                        expressionView.tcModuleList.ItemsSource = null;
                        expressionView.tcModuleList.ItemsSource = Modules;
                        expressionView.tcModuleList.SelectedIndex = 0;
                        expressionView.viewModel.m_Param = ModuleParam;
                        if (ExpressionString == null) ExpressionString = "true";
                        expressionView.viewModel.MyEditer.Text = ExpressionString;
                        expressionView.viewModel.m_TempScriptSupport = m_TempScriptSupport;
                        expressionView.ShowDialog();
                        if (expressionView.IsCompileSuccess)
                        {
                            IsCompileSuccess = true;
                            ExpressionString = expressionView.viewModel.MyEditer.Text;
                        }
                        else
                        {
                            IsCompileSuccess = false;
                        }
                    });
                }
                return _EditCommand;
            }
        }
        [NonSerialized]
        private CommandBase _ConfirmCommand;
        public CommandBase ConfirmCommand
        {
            get
            {
                if (_ConfirmCommand == null)
                {
                    _ConfirmCommand = new CommandBase((obj) =>
                    {
                        var view = this.ModuleView as IfView;
                        if (view != null)
                        {
                            view.Close();
                        }
                    });
                }
                return _ConfirmCommand;
            }
        }
        [NonSerialized]
        private CommandBase _ExecuteCommand;
        public CommandBase ExecuteCommand
        {
            get
            {
                if (_ExecuteCommand == null)
                {
                    _ExecuteCommand = new CommandBase((obj) =>
                    {
                        ExeModule();
                    });
                }
                return _ExecuteCommand;
            }
        }

        #endregion
    }

}
