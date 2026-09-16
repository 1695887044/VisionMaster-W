using EventMgrLib;
using Plugin.SendStr.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Attributes;
using VM.Start.Common;
using VM.Start.Common.Enums;
using VM.Start.Common.Helper;
using VM.Start.Common.Provide;
using VM.Start.Communacation;
using VM.Start.Core;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.Services;
using VM.Start.ViewModels;
using VM.Start.Views;

namespace Plugin.SendStr.ViewModels
{
    public enum eEnableEndStr
    {
        Have,
        No,
    }
    [Category("文件通讯")]
    [DisplayName("发送文本")]
    [ModuleImageName("SendStr")]
    [Serializable]
    public class SendStrViewModel : ModuleBase
    {
        public override bool ExeModule()
        {
            Stopwatch.Restart();
            try
            {
                if (CurKey == "")
                {
                    ChangeModuleRunStatus(eRunStatus.NG);
                    return false;
                }
                if (!SendStr.Text.StartsWith("&"))
                {
                    if (eEnableEndstr == eEnableEndStr.Have)
                    {
                        EComManageer.IsSendByHex = IsSendByHex;
                        EComManageer.SendStr(CurKey, SendStr.Value.ToString()+"\r\n");
                    }
                    else
                    {
                        EComManageer.IsSendByHex = IsSendByHex;
                        EComManageer.SendStr(CurKey, SendStr.Value.ToString());
                    }
                }
                else
                {
                    object str = Prj.GetParamByName(SendStr.Text).Value;
                    if (eEnableEndstr == eEnableEndStr.Have)
                    {
                        EComManageer.IsSendByHex = IsSendByHex;
                        EComManageer.SendStr(CurKey, str.ToString()+"\r\n");
                    }
                    else
                    {
                        EComManageer.IsSendByHex = IsSendByHex;
                        EComManageer.SendStr(CurKey, str.ToString());
                    }
                }
                if (Continue)
                {
                    Prj.ExeModuleName = "";
                }
                ChangeModuleRunStatus(eRunStatus.OK);
                return true;
            }
            catch (Exception ex)
            {
                ChangeModuleRunStatus(eRunStatus.NG);
                Logger.GetExceptionMsg(ex);
                return false;
            }
        }
        public override void AddOutputParams()
        {
            base.AddOutputParams();
        }

        #region Prop
        private bool _Continue = false;
        public bool Continue
        {
            get { return _Continue; }
            set { _Continue = value; }
        }
        private LinkVarModel _SendStr =new LinkVarModel() { Value="发送文本"};
        /// <summary>
        /// 发送内容
        /// </summary>
        public LinkVarModel SendStr
        {
            get { return _SendStr; }
            set { _SendStr = value; RaisePropertyChanged(); }
        }
        private bool _IsSendByHex=false;

        public bool IsSendByHex
        {
            get { return _IsSendByHex; }
            set { _IsSendByHex = value; RaisePropertyChanged(); }   
        }

        private bool _IsEnableTimeOut = false;
        /// <summary>
        /// 启用超时
        /// </summary>
        public bool IsEnableTimeOut
        {
            get { return _IsEnableTimeOut; }
            set { _IsEnableTimeOut = value; RaisePropertyChanged(); }
        }
        private eEnableEndStr _eEnableEndstr = eEnableEndStr.No;
        public eEnableEndStr eEnableEndstr
        {
            get { return _eEnableEndstr; }
            set { _eEnableEndstr = value;}
        }

        private string _CurKey = "";
        /// <summary>
        /// 当前Key
        /// </summary>
        public string CurKey
        {
            get { return _CurKey; }
            set
            {
                Set(ref _CurKey, value, new Action(() => { Remarks = EComManageer.GetRemarks(_CurKey); }));
            }
        }
        private string _Remarks;
        /// <summary>
        /// 备注
        /// </summary>
        public string Remarks
        {
            get { return _Remarks; }
            set
            {
                Set(ref _Remarks, value);
            }
        }
        private List<string> _ComKeys;

        public List<string> ComKeys
        {
            get
            {
                if (_ComKeys == null)
                {
                    _ComKeys = new List<string>();
                }
                return _ComKeys;
            }
            set { _ComKeys = value; RaisePropertyChanged(); }
        }

        #endregion

        #region Command
        private void OnVarChanged(VarChangedEventParamModel obj)
        {

            switch (obj.SendName.Split(',')[1])
            {
                case "StringLinkText":
                    SendStr.Text = obj.LinkName;
                    break;
                default:
                    break;
            }
        }
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
                        CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "string,double,int,bool");
                        EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},StringLinkText");
                    });
                }
                return _LinkCommand;
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
                        var view = this.ModuleView as SendStrView;
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
