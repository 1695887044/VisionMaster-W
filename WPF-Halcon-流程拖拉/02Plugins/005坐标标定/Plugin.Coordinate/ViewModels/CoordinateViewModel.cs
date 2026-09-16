using EventMgrLib;
using HalconDotNet;
using HandyControl.Controls;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using Plugin.Coordinate.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Xml.Linq;
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
using VM.Start.Views;
using VM.Start.Views.Dock;

namespace Plugin.Coordinate.ViewModels
{
    #region enum
    public enum eLinkCommand
    {
        InputImageLink,
        XLink,
        YLink,
        DegLink,
    }
    #endregion
    [Category("坐标标定")]
    [DisplayName("坐标补正")]
    [ModuleImageName("Coordinate")]
    [Serializable]
    public class CoordinateViewModel : ModuleBase
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
            int index = Prj.ModuleList.IndexOf(this);
            for (int i = index - 1; i >= 0; i--)
            {
                if (Prj.ModuleList[i].ModuleParam.ModuleName.StartsWith("模板匹配"))
                {
                    HomMat2D = Prj.ModuleList[i].HomMat2D;
                    XLinkText.Text = $"&{Prj.ModuleList[i].ModuleParam.ModuleName}.X";
                    YLinkText.Text = $"&{Prj.ModuleList[i].ModuleParam.ModuleName}.Y";
                    DegLinkText.Text = $"&{Prj.ModuleList[i].ModuleParam.ModuleName}.Deg";
                    return;
                }
            }


        }
        public override bool ExeModule()
        {
            if (ModuleParam.ModuleName.StartsWith ("坐标补正结束"))
            {
                ChangeModuleRunStatus(eRunStatus.OK);
                return true;
            }
            Stopwatch.Restart();
            try
            {
                if (InputImageLinkText == null)
                {
                    ChangeModuleRunStatus(eRunStatus.NG);
                    return false;
                }
                GetModeCoord();
                MathCoord.X = double.Parse(GetLinkValue(XLinkText).ToString());
                MathCoord.Y = double.Parse(GetLinkValue(YLinkText).ToString());
                MathCoord.Phi = double.Parse(GetLinkValue(DegLinkText).ToString());
                HOperatorSet.VectorAngleToRigid(ModeCoord.Y, ModeCoord.X, ModeCoord.Phi, MathCoord.Y, MathCoord.X, MathCoord.Phi, out HomMat2D);
                HOperatorSet.VectorAngleToRigid(MathCoord.Y, MathCoord.X, MathCoord.Phi, ModeCoord.Y, ModeCoord.X, ModeCoord.Phi, out HomMat2D_Inverse);
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
                if (DispImage != null && DispImage.IsInitialized())
                {
                    ExeModule();
                }
            }
        }
        private LinkVarModel _XLinkText = new LinkVarModel() { Value = 0 };
        /// <summary>
        /// X链接文本
        /// </summary>
        public LinkVarModel XLinkText
        {
            get { return _XLinkText; }
            set { Set(ref _XLinkText, value); }
        }
        private LinkVarModel _YLinkText = new LinkVarModel() { Value = 0 };
        /// <summary>
        /// Y链接文本
        /// </summary>
        public LinkVarModel YLinkText
        {
            get { return _YLinkText; }
            set { Set(ref _YLinkText, value); }
        }
        private LinkVarModel _DegLinkText = new LinkVarModel() { Value = 0 };
        /// <summary>
        /// Deg链接文本
        /// </summary>
        public LinkVarModel DegLinkText
        {
            get { return _DegLinkText; }
            set { Set(ref _DegLinkText, value); }
        }
        private bool _ShowCoordinate=true;
        /// <summary>
        /// 显示坐标轴
        /// </summary>
        public bool ShowCoordinate
        {
            get { return _ShowCoordinate; }
            set { Set(ref _ShowCoordinate, value); }
        }

        #endregion

        #region Command
        public override void Loaded()
        {
            base.Loaded();
            var view = ModuleView as CoordinateView;
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
                GetDispImage(InputImageLinkText,true);
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
                        var view = this.ModuleView as CoordinateView;
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
                case "XLink":
                    XLinkText.Text = obj.LinkName;
                    break;
                case "YLink":
                    YLinkText.Text = obj.LinkName;
                    break;
                case "DegLink":
                    DegLinkText.Text = obj.LinkName;
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
                            case eLinkCommand.InputImageLink:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "HImage");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},InputImageLink");
                                break;
                            case eLinkCommand.XLink:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},XLink");
                                break;
                            case eLinkCommand.YLink:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},YLink");
                                break;
                            case eLinkCommand.DegLink:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},DegLink");
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
        /// <summary>
        /// 获取模板坐标
        /// </summary>
        private void GetModeCoord()
        {
            int index = Prj.ModuleList.IndexOf(this);
            for (int i = index-1; i >= 0; i--)
            {
                if (Prj.ModuleList[i].ModuleParam.ModuleName.StartsWith("模板匹配"))
                {
                    ModeCoord = Prj.ModuleList[i].ModeCoord; return;
                }
            }
        }
        #endregion

    }

}
