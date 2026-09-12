using EventMgrLib;
using HalconDotNet;
using Plugin.DistancePP.Views;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Documents;
using System.Windows.Media;
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

namespace Plugin.DistancePP.ViewModels
{
    #region enum
    public enum eLinkCommand
    {
        InputImageLink,
        P1X,
        P1Y,
        P2X,
        P2Y,
    }

    #endregion
    [Category("几何测量")]
    [DisplayName("点点距离")]
    [ModuleImageName("DistancePP")]
    [Serializable]
    public class DistancePPModel : ModuleBase
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
                ClearRoiAndText();
                if (InputImageLinkText == null)
                {
                    ChangeModuleRunStatus(eRunStatus.NG);
                    return false;
                }
                GetDispImage(InputImageLinkText, true);
                if (DispImage != null && DispImage.IsInitialized())
                {
                    P1XLinkValue = Convert.ToDouble(GetLinkValue(P1XLinkText));
                    P1YLinkValue = Convert.ToDouble(GetLinkValue(P1YLinkText));
                    P2XLinkValue = Convert.ToDouble(GetLinkValue(P2XLinkText));
                    P2YLinkValue = Convert.ToDouble(GetLinkValue(P2YLinkText));
                    PointX = Math.Round((P1XLinkValue + P2XLinkValue) / 2,4);
                    PointY = Math.Round((P1YLinkValue + P2YLinkValue) / 2,4);     
                    Distance = Math.Round(Dis.DisPP(P1YLinkValue,P1XLinkValue,P2YLinkValue,P2XLinkValue),4);
                    Gen.GenContour(out HObject ResultLine, P1YLinkValue, P2YLinkValue, P1XLinkValue, P2XLinkValue);
                    double DAngle = HMisc.AngleLx(P1YLinkValue, P1XLinkValue, P2YLinkValue, P2XLinkValue);
                    HOperatorSet.TupleDeg(DAngle, out HTuple hv_Deg);
                    Angle = Math.Round(Convert.ToDouble(hv_Deg.ToString()), 4);
                    Gen.GenCross(out HObject Point, PointY, PointX, 60, 0);
                    Gen.GenCross(out HObject Point1, P1YLinkValue, P1XLinkValue, 60, 0);
                    Gen.GenCross(out HObject Point2, P2YLinkValue, P2XLinkValue, 60, 0);
                    if (ShowResultLine)
                        ShowHRoi(new HRoi(ModuleParam.ModuleEncode, ModuleParam.ModuleName, ModuleParam.Remarks, HRoiType.测量直线1, _LineColor.Remove(1,2), new HObject(ResultLine)));

                    if (ShowResultPoint)
                        ShowHRoi(new HRoi(ModuleParam.ModuleEncode, ModuleParam.ModuleName, ModuleParam.Remarks, HRoiType.检测点, "green", new HObject(Point)));

                    if (ShowResultGPoint)
                    {
                        ShowHRoi(new HRoi(ModuleParam.ModuleEncode, ModuleParam.ModuleName, ModuleParam.Remarks, HRoiType.检测点P1, "yellow", new HObject(Point1)));
                        ShowHRoi(new HRoi(ModuleParam.ModuleEncode, ModuleParam.ModuleName, ModuleParam.Remarks, HRoiType.检测点P2, "yellow", new HObject(Point2)));
                    }
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
            AddOutputParam("距离", "double", Distance);
            AddOutputParam("角度", "double", Angle);
            AddOutputParam("中心点X", "double", PointX);
            AddOutputParam("中心点Y", "double", PointY);
            AddOutputParam("状态", "bool", ModuleParam.Status == eRunStatus.OK ? true : false);
            AddOutputParam("时间", "int", ModuleParam.ElapsedTime);
        }
        #region Prop
        private double _Distance;
        /// <summary>距离</summary>
        public double Distance
        {
            get { return _Distance; }
            set { Set(ref _Distance, value); }
        }
        private double _Angle;
        /// <summary>角度</summary>
        public double Angle
        {
            get { return _Angle; }
            set { Set(ref _Angle, value); }
        }
        private bool _ShowResultLine = true;
        /// <summary>显示结果线</summary>
        public bool ShowResultLine
        {
            get { return _ShowResultLine; }
            set { Set(ref _ShowResultLine, value); }
        }

        private bool _ShowResultPoint = true;
        /// <summary>显示结果中点</summary>
        public bool ShowResultPoint
        {
            get { return _ShowResultPoint; }
            set { Set(ref _ShowResultPoint, value); }
        }
        private bool _ShowResultGPoint = true;
        /// <summary>显示构造点</summary>
        public bool ShowResultGPoint
        {
            get { return _ShowResultGPoint; }
            set { Set(ref _ShowResultGPoint, value); }
        }
        private string _InputImageLinkText;
        /// <summary>
        /// 输入图像链接文本
        /// </summary>
        public string InputImageLinkText
        {
            get { return _InputImageLinkText; }
            set { Set(ref _InputImageLinkText, value); }
        }
     
        private string _P1XLinkText;
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

        private string _P1YLinkText;
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

        private string _P2XLinkText;
        /// <summary>
        /// 点2X链接文本
        /// </summary>
        public string P2XLinkText
        {
            get { return _P2XLinkText; }
            set { Set(ref _P2XLinkText, value); }
        }
        private double _P2XLinkValue;
        public double P2XLinkValue
        {
            get { return _P2XLinkValue; }
            set { _P2XLinkValue = value; }
        }

        private string _P2YLinkText;
        /// <summary>
        /// 点2Y链接文本
        /// </summary>
        public string P2YLinkText
        {
            get { return _P2YLinkText; }
            set { Set(ref _P2YLinkText, value); }
        }
        private double _P2YLinkValue;
        public double P2YLinkValue
        {
            get { return _P2YLinkValue; }
            set { _P2YLinkValue = value; }
        }

        private double _PointX;
        /// <summary>
        /// 中心点X
        /// </summary>
        public double PointX
        {
            get { return _PointX; }
            set { Set(ref _PointX, value); }
        }
        private double _PointY;
        /// <summary>
        /// 中心点Y
        /// </summary>
        public double PointY
        {
            get { return _PointY; }
            set { Set(ref _PointY, value); }
        }
        private string _LineColor = "#ffffff00";
        /// <summary>
        /// 线颜色链接文本
        /// </summary>
        public Color LineColor
        {
            get { return (Color)ColorConverter.ConvertFromString(_LineColor); }
            set { Set(ref _LineColor, value.ToString()); }
        }
        #endregion
        #region Command
        public override void Loaded()
        {
            base.Loaded();
            var view = ModuleView as DistancePPView;
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
                        var view = this.ModuleView as DistancePPView;
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
                case "P1X":
                    P1XLinkText = obj.LinkName;
                    break;
                case "P1Y":
                    P1YLinkText = obj.LinkName;
                    break;
                case "P2X":
                    P2XLinkText = obj.LinkName;
                    break;
                case "P2Y":
                    P2YLinkText = obj.LinkName;
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
                            case eLinkCommand.P1X:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},P1X");
                                break;
                            case eLinkCommand.P1Y:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},P1Y");
                                break;
                            case eLinkCommand.P2X:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},P2X");
                                break;
                            case eLinkCommand.P2Y:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},P2Y");
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
        //private void ShowHRoi(bool ShowLine=false)
        //{
        //    var view = ModuleView as DistancePPView;
        //    VMHWindowControl mWindowH;
        //    if (view == null || view.IsClosed)
        //    {
        //        mWindowH = ViewDic.GetView(DispImage.DispViewID);
        //    }
        //    else
        //    {
        //        mWindowH = view.mWindowH;
        //    }
        //    //mWindowH.ClearWindow();
        //    mWindowH.HobjectToHimage(DispImage);
        //    HObject hObject = new HObject();
        //    hObject.GenEmptyObj();
        //    hObject.Dispose();
        //    HOperatorSet.GenRegionLine(out hObject, P1YLinkValue, P1XLinkValue, P2YLinkValue, P2XLinkValue);
        //    mWindowH.DispObj(hObject);
        //    hObject.Dispose();
        //    List<HRoi> roiList = DispImage.mHRoi.Where(c => c.ModuleName == ModuleParam.ModuleName).ToList();
        //    foreach (HRoi roi in roiList)
        //    {
        //        if (roi.roiType == HRoiType.文字显示)
        //        {
        //            HText roiText = (HText)roi;
        //            ShowTool.SetFont(mWindowH.hControl.HalconWindow, roiText.size, "false", "false");
        //            ShowTool.SetMsg(mWindowH.hControl.HalconWindow, roiText.text, "image", roiText.row, roiText.col, roiText.drawColor, "false");
        //        }
        //        else
        //        {
        //            mWindowH.WindowH.DispHobject(roi.hobject, roi.drawColor);
        //        }
        //    }
        //}
        #endregion
    }
}
