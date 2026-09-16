using EventMgrLib;
using Plugin.HKSetOutPut.Views;
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

namespace Plugin.HKSetOutPut.ViewModels
{
    #region enum
    public enum eLinkCommand
    {
        IntLink,

    }
    public enum eLineIndex
    {
        Line1 = 1,
        Line2 = 2,
    }
    #endregion

    [Category("文件通讯")]
    [DisplayName("海康设置输出")]
    [ModuleImageName("Delay")]
    [Serializable]
    public class HKSetOutPutViewModel : ModuleBase
    {
        public override bool ExeModule()
        {
            Stopwatch.Restart();
            try
            {
                if (SelectedCameraModel.Connected)
                {
                    int delayTime = Convert.ToInt32(GetLinkValue(DelayTime));
                    int lineIndex = 1;
                    if (SelectedLine == eLineIndex.Line2)
                        lineIndex = 2;
                    if (SelectedCameraModel.SetOutPut(lineIndex, delayTime))
                    { 
                        ChangeModuleRunStatus(eRunStatus.OK);
                        return true;
                    }
                }
                ChangeModuleRunStatus(eRunStatus.NG);
                return false;
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

        /// <summary>相机列表</summary>
        public ObservableCollection<CameraBase> CameraModels { get; set; } = CameraSetViewModel.Ins.CameraModels;
        private CameraBase _SelectedCameraModel = new CameraBase();

        public CameraBase SelectedCameraModel
        {
            get { return _SelectedCameraModel; }
            set { _SelectedCameraModel = value; RaisePropertyChanged(); }
        }
        [field: NonSerialized]
        public Array SelectedLines   //输出LineIndex数据源
        {
            get { return Enum.GetValues(typeof(eLineIndex)); }
            set {; }
        }
        /// <summary>
        /// 输出LineIndex
        /// </summary>
        private eLineIndex _PerPorcessSelectedText = new eLineIndex();
        public eLineIndex SelectedLine
        {
            get { return _PerPorcessSelectedText; }
            set { Set(ref _PerPorcessSelectedText, value); }
        }

        /// <summary>
        /// 延时时间
        /// </summary>
        private LinkVarModel _DelayTime = new LinkVarModel() { Text = "1000" };
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
