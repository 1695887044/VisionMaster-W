using EventMgrLib;
using Plugin.While.Views;
using System;
using System.Collections.Generic;
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
using VM.Start.ViewModels;

namespace Plugin.While.ViewModels
{
    #region enum
    #endregion
    [Category("逻辑工具")]
    [DisplayName("循环工具")]
    [ModuleImageName("While")]
    [Serializable]
    public class WhileModel : ModuleBase
    {
        public override bool ExeModule()
        {
            int start = 0;
            int end = 0;
            Stopwatch.Restart();
            switch (LoopMode)
            {
                case eLoopMode.Increase:
                    start = Convert.ToInt32(GetLinkValue(Start));
                    end = Convert.ToInt32(GetLinkValue(End));
                    ModuleParam.CyclicCount = end - start;
                    break;
                case eLoopMode.Decrease:
                    start = Convert.ToInt32(GetLinkValue(Start));
                    end = Convert.ToInt32(GetLinkValue(End));
                    ModuleParam.CyclicCount = end - start;
                    break;
                case eLoopMode.Loop:
                    ModuleParam.CyclicCount = int.MaxValue;
                    break;
                case eLoopMode.Foreach:
                    break;
                default:
                    break;
            }
            ChangeModuleRunStatus(eRunStatus.OK);
            return true;
        }
        public override void AddOutputParams()
        {
            AddOutputParam("索引", "int", ModuleParam.pIndex);
            base.AddOutputParams();
        }
        #region Prop
        private LinkVarModel _Start = new LinkVarModel() { Text = "0"};

        public LinkVarModel Start
        {
            get { return _Start; }
            set { _Start = value; RaisePropertyChanged(); }
        }
        private LinkVarModel _End = new LinkVarModel() { Text = "0" };

        public LinkVarModel End
        {
            get { return _End; }
            set { _End = value; RaisePropertyChanged(); }
        }
        private eLoopMode _LoopMode = eLoopMode.Increase;

        public eLoopMode LoopMode
        {
            get { return _LoopMode; }
            set { _LoopMode = value; RaisePropertyChanged(); }
        }
        #endregion
        #region Command
        private void OnVarChanged(VarChangedEventParamModel obj)
        {

            switch (obj.SendName.Split(',')[1])
            {
                case "StartLinkText":
                    Start.Text = obj.LinkName;
                    break;
                case "EndLinkText":
                    End.Text = obj.LinkName;
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
                        switch (obj.ToString())
                        {
                            case "Start":
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "int");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},StartLinkText");
                                break;
                            case "End":
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "int");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},EndLinkText");
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
                        var view = this.ModuleView as WhileView;
                        if (view != null)
                        {
                            ExeModule();
                            view.Close();
                        }
                    });
                }
                return _ConfirmCommand;
            }
        }

        #endregion
    }
}
