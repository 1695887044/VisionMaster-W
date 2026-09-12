using EventMgrLib;
using Plugin.AssignmentStr.Views;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Attributes;
using VM.Start.Common;
using VM.Start.Common.Enums;
using VM.Start.Common.Helper;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.ViewModels;

namespace Plugin.AssignmentStr.ViewModels
{
    public enum eLinkCommand
    {
        DataValueLink,
        VarValueLinK,
    }

    [Category("测试工具")]
    [DisplayName("赋值字符串")]
    [ModuleImageName("AssignmentStr")]
    [Serializable]
    public class AssignmentStrViewModel : ModuleBase
    {

        public override bool ExeModule()
        {
            //Stopwatch.Restart();
            //ChangeModuleRunStatus(eRunStatus.OK);
            //return true;


            Stopwatch.Restart();
            try
            {
                if (DataValueLinkText != "" && VarValueLinkText != "")
                {
                    Prj.SetParamByName(VarValueLinkText, Prj.GetParamByName(DataValueLinkText));
                }
                else
                {
                    ChangeModuleRunStatus(eRunStatus.NG);
                    return false;
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
        private void OnVarChanged(VarChangedEventParamModel obj)
        {
            switch (obj.SendName.Split(',')[1])
            {
                case "DataValueLink":
                    DataValueLinkText = obj.LinkName;
                    break;
                case "VarValueLink":
                    VarValueLinkText = obj.LinkName;
                    break;
                default:
                    break;
            }
        }
        public override void AddOutputParams()
        {
            base.AddOutputParams();
        }

        #region Prop
        private string _DataValueLinkText = "";
        /// <summary>
        /// 值
        /// </summary>
        public string DataValueLinkText
        {
            get { return _DataValueLinkText; }
            set { _DataValueLinkText = value; RaisePropertyChanged(); }
        }

        private string _VarValueLinkText = "";
        /// <summary>
        /// 变量
        /// </summary>
        public string VarValueLinkText
        {
            get { return _VarValueLinkText; }
            set { _VarValueLinkText = value; RaisePropertyChanged(); }
        }

        private PLCDataWriteReadTypeEnum _DataType = PLCDataWriteReadTypeEnum.整型;
        /// <summary>
        /// 数据类型
        /// </summary>
        public PLCDataWriteReadTypeEnum DataType
        {
            get
            {
                return _DataType;
            }
            set
            {
                Set(ref _DataType, value);
            }
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
                    //以GUID+类名作为筛选器
                    //EventMgr.Ins.GetEvent<VarChangedEvent>().Subscribe(OnVarChanged, o => o.SendName.StartsWith($"{ModuleGuid}"));
                    //_LinkCommand = new CommandBase((obj) =>
                    //{
                    //    CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "string");
                    //    EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},DataValueLink");
                    //});
                    //以GUID + 类名作为筛选器
                    EventMgr.Ins.GetEvent<VarChangedEvent>().Subscribe(OnVarChanged, o => o.SendName.StartsWith($"{ModuleGuid}"));
                    _LinkCommand = new CommandBase((obj) =>
                    {
                        //eLinkCommand linkCommand = (eLinkCommand)obj;
                        //switch ((string)obj)
                        switch (DataType)
                        {
                            case PLCDataWriteReadTypeEnum.布尔:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "bool");
                                break;
                            case PLCDataWriteReadTypeEnum.整型:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "int");
                                break;
                            case PLCDataWriteReadTypeEnum.浮点:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                break;
                            case PLCDataWriteReadTypeEnum.字符串:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "string");
                                break;
                        }

                        eLinkCommand linkCommand = (eLinkCommand)obj;
                        switch (linkCommand)
                        {
                            case eLinkCommand.DataValueLink://"DataValueLink":
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},DataValueLink");
                                break;
                            case eLinkCommand.VarValueLinK://"VarValueLink":
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},VarValueLink");
                                break;
                            default:
                                break;
                        }

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
                        var view = this.ModuleView as AssignmentStrView;
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
