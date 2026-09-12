using EventMgrLib;
using Plugin.Delay.Views;
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
using VM.Start.Core;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.Services;
using VM.Start.ViewModels;
using VM.Start.Views;

namespace Plugin.Delay.ViewModels
{
    #region enum
    public enum eLinkCommand
    {
        IntLink,

    }
    #endregion

    [Category("常用工具")]
    [DisplayName("延时工具")]
    [ModuleImageName("Delay")]
    [Serializable]
    public class DelayViewModel : ModuleBase
    {
        public override bool ExeModule()
        {
            Stopwatch.Restart();
            try
            {
                int delayTime = Convert.ToInt32(GetLinkValue(DelayTime));
                while (Stopwatch.ElapsedMilliseconds < delayTime)
                {
                    if (CancelWait)
                    {
                        break;
                    }
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
                case "IntLinkText":
                    DelayTime.Text = obj.LinkName;
                    break;
                default:
                    break;
            }
        }

        #region Prop
        private LinkVarModel _DelayTime = new LinkVarModel() { Text ="10"};
        /// <summary>
        /// 延时时间
        /// </summary>
        public LinkVarModel DelayTime
        {
            get { return _DelayTime; }
            set { _DelayTime = value; RaisePropertyChanged(); }
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
                    EventMgr.Ins.GetEvent<VarChangedEvent>().Subscribe(OnVarChanged, o => o.SendName.StartsWith($"{ModuleGuid}"));
                    _LinkCommand = new CommandBase((obj) =>
                    {
                        CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "int");
                        EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},IntLinkText");
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
                        var view = this.ModuleView as DelayView;
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
