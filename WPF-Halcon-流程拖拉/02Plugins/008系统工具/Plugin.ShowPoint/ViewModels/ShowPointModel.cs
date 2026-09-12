using EventMgrLib;
using HalconDotNet;
using Plugin.ShowPoint.Views;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Documents;
using System.Windows.Media;
using System.Xml.Serialization;
using VM.Halcon;
using VM.Halcon.Config;
using VM.Halcon.Model;
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
using VM.Start.Views.Dock;

namespace Plugin.ShowPoint.ViewModels
{
    #region enum
    public enum eLinkCommand
    {
        InputImageLink,
        PX,
        PY,
        PLength,
    }
    #endregion

    [Category("系统工具")]
    [DisplayName("显示点")]
    [ModuleImageName("ShowPoint")]
    [Serializable]
    public class ShowPointModel : ModuleBase
    {
        public override void SetDefaultLink()
        {
            CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "HImage");
            var moduls = VarLinkViewModel.Ins.Modules.LastOrDefault();
            if (moduls == null || moduls.VarModels.Count == 0)
            {
                return;
            }
            InputImageLinkText = $"&{moduls.DisplayName}.{moduls.VarModels[0].Name}";
        }

        public override bool ExeModule()
        {
            Stopwatch.Restart();
            try
            {
                if (InputImageLinkText == null)
                {
                    ChangeModuleRunStatus(eRunStatus.NG);
                    return false;
                }
                ClearRoiAndText();
                GetDispImage(InputImageLinkText, true);
                if (DispImage != null && DispImage.IsInitialized())
                {
                    P1XLinkValue = Convert.ToDouble(GetLinkValue(P1XLinkText));
                    P1YLinkValue = Convert.ToDouble(GetLinkValue(P1YLinkText));
                    PLengthLinkValue=Convert.ToDouble(GetLinkValue(PLengthLinkText));
                    Gen.GenCross(out HObject _Cross, P1YLinkValue, P1XLinkValue, PLengthLinkValue, 0);

                    ShowHRoi(new HRoi(ModuleParam.ModuleEncode, ModuleParam.ModuleName, ModuleParam.Remarks, HRoiType.检测点, _ColorLinkText.Remove(1, 2), new HObject(_Cross)));
                    ShowHRoi();
                    ChangeModuleRunStatus(eRunStatus.OK);
                    return true;
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
        public override void AddOutputParams()
        {
            AddOutputParam("X", "double", P1XLinkValue);
            AddOutputParam("Y", "double", P1YLinkValue);
            AddOutputParam("状态", "bool", ModuleParam.Status == eRunStatus.OK ? true : false);
            AddOutputParam("时间", "int", ModuleParam.ElapsedTime);
        }
        #region Prop
        private string _InputImageLinkText;
        /// <summary>
        /// 输入图像链接文本
        /// </summary>
        public string InputImageLinkText
        {
            get { return _InputImageLinkText; }
            set
            {
                _InputImageLinkText = value;
                RaisePropertyChanged();
                GetDispImage(InputImageLinkText);
            }
        }

        private string _P1XLinkText="100";
        /// <summary>
        /// 点1X链接文本
        /// </summary>
        public string P1XLinkText
        {
            get { return _P1XLinkText; }
            set { Set(ref _P1XLinkText, value); }
        }
        private double _P1XLinkValue;
        public double P1XLinkValue
        {
            get { return _P1XLinkValue; }
            set { _P1XLinkValue = value; }
        }

        private string _P1YLinkText="100";
        /// <summary>
        /// 点1Y链接文本
        /// </summary>
        public string P1YLinkText
        {
            get { return _P1YLinkText; }
            set { Set(ref _P1YLinkText, value); }
        }
        private double _P1YLinkValue;
        public double P1YLinkValue
        {
            get { return _P1YLinkValue; }
            set { _P1YLinkValue = value; }
        }

        private string _PLengthLinkText="60";
        /// <summary>
        /// 线长度链接文本
        /// </summary>
        public string PLengthLinkText
        {
            get { return _PLengthLinkText; }
            set { Set(ref _PLengthLinkText, value); }
        }
        private double _PLengthLinkValue;
        public double PLengthLinkValue
        {
            get { return _PLengthLinkValue; }
            set { _PLengthLinkValue = value; }
        }
        
        private string _ColorLinkText="#ffffff00";
        /// <summary>
        /// 线颜色链接文本
        /// </summary>
        public Color ColorLinkText
        {
            get { return (Color)ColorConverter.ConvertFromString(_ColorLinkText); }
            set { Set(ref _ColorLinkText, value.ToString()); }
        }
        private string _ColorLinkValue;
        public string ColorLinkValue
        {
            get { return _ColorLinkValue; }
            set { _ColorLinkValue = value; }
        }
        

        #endregion
        #region Command
        public override void Loaded()
        {
            base.Loaded();
            var view = ModuleView as ShowPointView;
            if (view != null)
            {
                ClosedView = true;
                if (view.mWindowH == null)
                {
                    view.mWindowH = new VMHWindowControl();
                    view.winFormHost.Child = view.mWindowH;
                }
                if (DispImage == null || !DispImage.IsInitialized())
                {
                    SetDefaultLink();
                    if (InputImageLinkText == null) return;
                }
                GetDispImage(InputImageLinkText, true);
                if (DispImage != null && DispImage.IsInitialized())
                {
                    ShowHRoi();
                }
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
                        var view = this.ModuleView as ShowPointView;
                        if (view != null)
                        {
                            view.Close();
                        }
                    });
                }
                return _ConfirmCommand;
            }
        }
        private void OnVarChanged(VarChangedEventParamModel obj)
        {
            switch (obj.SendName.Split(',')[1])
            {
                case "InputImageLink":
                    InputImageLinkText = obj.LinkName;
                    break;
                case "PX":
                    P1XLinkText = obj.LinkName;
                    break;
                case "PY":
                    P1YLinkText = obj.LinkName;
                    break;
                case "PLength":
                    PLengthLinkText = obj.LinkName;
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
                    //以GUID+类名作为筛选器
                    EventMgr.Ins.GetEvent<VarChangedEvent>().Subscribe(OnVarChanged, o => o.SendName.StartsWith($"{ModuleGuid}"));
                    _LinkCommand = new CommandBase((obj) =>
                    {
                        eLinkCommand linkCommand = (eLinkCommand)obj;
                        switch (linkCommand)
                        {
                            case eLinkCommand.PX:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},PX");
                                break;
                            case eLinkCommand.PY:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},PY");
                                break;
                            case eLinkCommand.PLength:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},PLength");
                                break;
                            case eLinkCommand.InputImageLink:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "HImage");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},InputImageLink");
                                break;
                            default:
                                break;
                        }
                    });
                }
                return _LinkCommand;
            }
        }
        #endregion
        #region Method

        #endregion
    }
}
