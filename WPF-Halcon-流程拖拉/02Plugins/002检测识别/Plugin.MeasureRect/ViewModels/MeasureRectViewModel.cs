using EventMgrLib;
using HalconDotNet;
using Plugin.MeasureRect.Views;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Web.UI.WebControls;
using System.Windows.Documents;
using System.Windows.Forms;
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
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;

namespace Plugin.MeasureRect.ViewModels
{
    #region enum
    public enum eLinkCommand
    {
        InputImageLink,
        RectL1,
        RectL2,
        RectPX,
        RectPY,
        RectAngle
    }

    #endregion

    [Category("检测识别")]
    [DisplayName("矩形工具")]
    [ModuleImageName("MeasureRect")]
    [Serializable]
    public class MeasureRectViewModel : ModuleBase
    {
        public override void SetDefaultLink()
        {
            if (InputImageLinkText == null)
            {
                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "HImage");
                var moduls = VarLinkViewModel.Ins.Modules.LastOrDefault();
                if (moduls == null || moduls.VarModels.Count == 0)
                {
                    return;
                }
                InputImageLinkText = $"&{moduls.DisplayName}.{moduls.VarModels[0].Name}";
            }
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
                GetDispImage(InputImageLinkText);
                if (DispImage != null && DispImage.IsInitialized())
                {
                    GetHomMat2D();
                    if (DisenableAffine2d && HomMat2D_Inverse != null && HomMat2D_Inverse.Length > 0)
                    {
                        DisenableAffine2d = false;
                        Aff.Affine2d(HomMat2D_Inverse, TempRect, InitRect);
                        if (InitLineChanged_Flag)
                        {
                            RectL1.Text = InitRect.Length1.ToString();
                            RectL2.Text = InitRect.Length2.ToString();
                            RectPX.Text = InitRect.MidC.ToString();
                            RectPY.Text = InitRect.MidR.ToString();
                            RectAngle.Text = InitRect.Phi.ToString();
                        }
                    }
                    if (HomMat2D != null && HomMat2D.Length > 0)
                    {
                        InitRect._MidC = Convert.ToDouble(GetLinkValue(RectPX));
                        InitRect._MidR = Convert.ToDouble(GetLinkValue(RectPY));
                        InitRect._Phi = Convert.ToDouble(GetLinkValue(RectAngle));
                        InitRect.Length1 = Convert.ToDouble(GetLinkValue(RectL1));
                        InitRect.Length2 = Convert.ToDouble(GetLinkValue(RectL2));
                        Aff.Affine2d(HomMat2D, InitRect, TranRect);
                    }
                    else
                    {
                        InitRect.MidR = TranRect.MidR = TempRect.MidR;
                        InitRect.MidC = TranRect.MidC = TempRect.MidC;
                        InitRect.Phi = TranRect.Phi = TempRect.Phi;
                        InitRect.Length1 = TranRect.Length1 = TempRect.Length1;
                        InitRect.Length2 = TranRect.Length2 = TempRect.Length2;
                    }
                    Meas.MeasRect2(DispImage, out HXLDCont m_MeasXLD, out HObject ho_Rectangle2Contour, out HObject ho_ruleContours, TranRect.MidR, TranRect.MidC, -TranRect.Phi, TranRect.Length1, TranRect.Length2, MeasInfo.MeasDis, MeasInfo.Length1, MeasInfo.Length2, 1, MeasInfo.Threshold, MeasInfo.ParamValue[0], MeasInfo.ParamValue[1],
                        out HTuple hv_RectRow, out HTuple hv_RectCol, out HTuple hv_RectPhi, out HTuple hv_Len1, out HTuple hv_Len2, out HTuple RowList, out HTuple ColList);
                    if (ShowResultPoint && RowList.ToDArr().Length > 1) //显示结果点
                    {
                        Gen.GenCross(out HObject m_MeasCross, RowList, ColList, MeasInfo.Length2, new HTuple(45).TupleRad());
                        ShowHRoi(new HRoi(ModuleParam.ModuleEncode, ModuleParam.ModuleName, ModuleParam.Remarks, HRoiType.检测点, "red", new HObject(m_MeasCross)));
                    }
                    if (ShowMeasContour) //显示检测范围
                    {
                        ShowHRoi(new HRoi(ModuleParam.ModuleEncode, ModuleParam.ModuleName, ModuleParam.Remarks, HRoiType.检测范围, "blue", ho_ruleContours));
                    }

                    if (ShowResultRect && hv_Len1.Length > 0) //显示结果矩形
                    {
                            OutRect.CreateRectangle2(Math.Round(Convert.ToDouble(hv_RectRow.ToString()), 4),
                                                     Math.Round(Convert.ToDouble(hv_RectCol.ToString()), 4),
                                                     Math.Round(Convert.ToDouble(hv_RectPhi.ToString()), 4),
                                                     Math.Round(Convert.ToDouble(hv_Len1.ToString()), 4),
                                                     Math.Round(Convert.ToDouble(hv_Len2.ToString()), 4));
                            RectDeg = ((HTuple)Math.Round(Convert.ToDouble(hv_RectPhi.ToString()), 4)).TupleDeg();
                            ShowHRoi(new HRoi(ModuleParam.ModuleEncode, ModuleParam.ModuleName, ModuleParam.Remarks, HRoiType.检测结果, _ColorLinkText.Remove(1, 2), ho_Rectangle2Contour));
                    }
                    ShowHRoi();
                    if (hv_Len1.Length > 0)
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
        public override void AddOutputParams()
        {
            AddOutputParam("测量矩形", "object", OutRect);
            AddOutputParam("中心X", "double", OutRect.MidC);
            AddOutputParam("中心Y", "double", OutRect.MidR);
            AddOutputParam("长边l1", "double", OutRect.Length1);
            AddOutputParam("短边l2", "double", OutRect.Length2);
            AddOutputParam("角度(Phi)", "double", OutRect.Phi);
            AddOutputParam("角度(Deg)", "double", OutRect.Deg);
            AddOutputParam("状态", "bool", ModuleParam.Status == eRunStatus.OK ? true : false);
            AddOutputParam("时间", "int", ModuleParam.ElapsedTime);
        }
        #region Prop
        private bool DisenableAffine2d = false;
        private bool InitLineChanged_Flag = false;
        private bool _ShowResultPoint = true;
        /// <summary>显示结果点</summary>
        public bool ShowResultPoint
        {
            get { return _ShowResultPoint; }
            set { Set(ref _ShowResultPoint, value); }
        }
        private bool _ShowMeasContour = true;
        /// <summary>显示测量轮廓 </summary>
        public bool ShowMeasContour
        {
            get { return _ShowMeasContour; }
            set { Set(ref _ShowMeasContour, value); }
        }
        private bool _ShowResultRect = true;
        /// <summary>显示结果矩形 </summary>
        public bool ShowResultRect
        {
            get { return _ShowResultRect; }
            set { Set(ref _ShowResultRect, value); }
        }
        /// <summary> 区域列表 </summary>
        public Dictionary<string, ROI> RoiList = new Dictionary<string, ROI>();
        /// <summary>
        /// 检测形态信息
        /// </summary>
        public MeasInfoModel MeasInfo { get; set; } = new MeasInfoModel();

        private ROIRectangle2 _OutRect = new ROIRectangle2();
        /// <summary>
        /// 输出矩形信息
        /// </summary>
        public ROIRectangle2 OutRect
        {
            get { return _OutRect; }
            set { Set(ref _OutRect, value); }
        }
        private double _RectDeg;

        public double RectDeg
        {
            get { return _RectDeg; }
            set { Set(ref _RectDeg, value); }
        }


        private eShieldRegion _ShieldRegion = eShieldRegion.手绘区域;
        /// <summary>
        /// 屏蔽区域
        /// </summary>
        public eShieldRegion ShieldRegion
        {
            get { return _ShieldRegion; }
            set { _ShieldRegion = value; RaisePropertyChanged(); }
        }
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
                    ShowHRoi();
                }
            }
        }
        private LinkVarModel _RectL1 = new LinkVarModel() { Text = "100" };
        /// <summary>
        /// 矩形L1链接
        /// </summary>
        public LinkVarModel RectL1
        {
            get { return _RectL1; }
            set { Set(ref _RectL1, value); }
        }
        private LinkVarModel _RectL2 = new LinkVarModel() { Text = "100" };
        /// <summary>
        /// 矩形L2链接
        /// </summary>
        public LinkVarModel RectL2
        {
            get { return _RectL2; }
            set { Set(ref _RectL2, value); }
        }
        private LinkVarModel _RectPX = new LinkVarModel() { Text = "100" };
        /// <summary>
        /// 矩形中心点X链接
        /// </summary>
        public LinkVarModel RectPX
        {
            get { return _RectPX; }
            set { Set(ref _RectPX, value); }
        }
        private LinkVarModel _RectPY = new LinkVarModel() { Text = "100" };
        /// <summary>
        /// 矩形中心点Y链接
        /// </summary>
        public LinkVarModel RectPY
        {
            get { return _RectPY; }
            set { Set(ref _RectPY, value); }
        }
        private LinkVarModel _RectAngle = new LinkVarModel() { Text = "45" };
        /// <summary>
        /// 矩形角度链接
        /// </summary>
        public LinkVarModel RectAngle
        {
            get { return _RectAngle; }
            set { Set(ref _RectAngle, value); }
        }
        /// <summary>
        /// 变换前矩形
        /// </summary>
        public ROIRectangle2 InitRect { get; set; } = new ROIRectangle2();
        /// <summary>
        /// 矩形信息
        /// </summary>
        public ROIRectangle2 TempRect { get; set; } = new ROIRectangle2();
        /// <summary>
        /// 变换后矩形信息
        /// </summary>
        public ROIRectangle2 TranRect { get; set; } = new ROIRectangle2();

        private string _ColorLinkText = "#ffff0000";
        /// <summary>
        /// 颜色链接文本
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
            var view = ModuleView as MeasureRectView;
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
                GetDispImage(InputImageLinkText);
                if (DispImage != null && DispImage.IsInitialized())
                {
                    view.mWindowH.HobjectToHimage(DispImage);
                    view.mWindowH.hControl.MouseUp += HControl_MouseUp;
                    ShowHRoi();
                    InitLineMethod();
                }
                RectL1.TextChanged = new Action(() => { InitLineChanged(); });
                RectL2.TextChanged = new Action(() => { InitLineChanged(); });
                RectPX.TextChanged = new Action(() => { InitLineChanged(); });
                RectPY.TextChanged = new Action(() => { InitLineChanged(); });
                RectAngle.TextChanged = new Action(() => { InitLineChanged(); });
            }
        }
        private void OnVarChanged(VarChangedEventParamModel obj)
        {
            eLinkCommand linkCommand = (eLinkCommand)Enum.Parse(typeof(eLinkCommand), obj.SendName.Split(',')[1].ToString());
            switch (linkCommand)
            {
                case eLinkCommand.InputImageLink:
                    InputImageLinkText = obj.LinkName;
                    break;
                case eLinkCommand.RectL1:
                    RectL1.Text = obj.LinkName;
                    break;
                case eLinkCommand.RectL2:
                    RectL2.Text = obj.LinkName;
                    break;
                case eLinkCommand.RectPX:
                    RectPX.Text = obj.LinkName;
                    break;
                case eLinkCommand.RectPY:
                    RectPY.Text = obj.LinkName;
                    break;
                case eLinkCommand.RectAngle:
                    RectAngle.Text = obj.LinkName;
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
                            case eLinkCommand.RectL1:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},RectL1");
                                break;
                            case eLinkCommand.RectL2:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},RectL2");
                                break;
                            case eLinkCommand.RectPY:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},RectPY");
                                break;
                            case eLinkCommand.RectPX:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},RectPX");
                                break;
                            case eLinkCommand.RectAngle:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},RectAngle");
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
                        InitLineMethod();
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
                        var view = this.ModuleView as MeasureRectView;
                        if (view != null)
                        {
                            view.Close();
                        }
                    });
                }
                return _ConfirmCommand;
            }
        }

        #endregion

        #region Method
        private void InitLineChanged()
        {
            if (InitLineChanged_Flag == true) return;

            InitRect.MidR = Convert.ToDouble(GetLinkValue(RectPY));
            InitRect.MidC = Convert.ToDouble(GetLinkValue(RectPX));
            InitRect.Phi = Convert.ToDouble(GetLinkValue(RectAngle));
            InitRect.Length1 = Convert.ToDouble(GetLinkValue(RectL1));
            InitRect.Length2 = Convert.ToDouble(GetLinkValue(RectL2));

            DisenableAffine2d = true;
            if (roiRectangle2 != null)
            {
                if (DisenableAffine2d && HomMat2D.Length > 0)
                {
                    Aff.Affine2d(HomMat2D, InitRect, TempRect);
                    if (InitLineChanged_Flag)
                    {

                        roiRectangle2.MidR = TempRect.MidR;
                        roiRectangle2.MidC = TempRect.MidC;
                        roiRectangle2.Phi = TempRect.Phi;
                        roiRectangle2.Length1 = TempRect.Length1;
                        roiRectangle2.Length2 = TempRect.Length2;
                    }
                }
                else
                {
                    roiRectangle2.MidR = InitRect.MidR;
                    roiRectangle2.MidC = InitRect.MidC;
                    roiRectangle2.Phi = InitRect.Phi;
                    roiRectangle2.Length1 = InitRect.Length1;
                    roiRectangle2.Length2 = InitRect.Length2;

                    TempRect.MidR = InitRect.MidR;
                    TempRect.MidC = InitRect.MidC;
                    TempRect.Phi = InitRect.Phi;
                    TempRect.Length1 = InitRect.Length1;
                    TempRect.Length2 = InitRect.Length2;
                }
                ExeModule();
                InitLineMethod();
            }
        }
        ROIRectangle2 roiRectangle2;
        private void HControl_MouseUp(object sender, MouseEventArgs e)
        {
            try
            {
                var view = ModuleView as MeasureRectView;
                if (view == null) return;
                ROI roi = view.mWindowH.WindowH.smallestActiveROI(out string info, out string index);
                if (index.Length > 0)
                {
                    roiRectangle2 = roi as ROIRectangle2;
                    if (roiRectangle2 != null)
                    {
                        TempRect.MidC = Math.Round(roiRectangle2.MidC, 2);
                        TempRect.MidR = Math.Round(roiRectangle2.MidR, 2);
                        TempRect.Phi = Math.Round(roiRectangle2.Phi, 2);
                        TempRect.Length1 = Math.Round(roiRectangle2.Length1, 2);
                        TempRect.Length2 = Math.Round(roiRectangle2.Length2, 2);
                        DisenableAffine2d = true;
                        InitLineChanged_Flag = true;
                        ExeModule();
                        InitLineMethod();
                        InitLineChanged_Flag = false;
                    }
                }
            }
            catch (Exception ex)
            {
            }
        }
        public void InitLineMethod()
        {
            var view = ModuleView as MeasureRectView;
            if (view == null)
            {
                return;
            }
            if (TranRect.FlagLineStyle != null)
            {
                view.mWindowH.WindowH.genRect2(ModuleParam.ModuleName, TranRect.MidR, TranRect.MidC, TranRect.Phi, TranRect.Length1, TranRect.Length2, ref RoiList);
            }
            else if (DispImage != null && !RoiList.ContainsKey(ModuleParam.ModuleName))
            {
                view.mWindowH.WindowH.genRect2(ModuleParam.ModuleName, view.mWindowH.hv_imageHeight / 4, view.mWindowH.hv_imageWidth / 4, 0, view.mWindowH.hv_imageHeight / 4, view.mWindowH.hv_imageWidth / 4, ref RoiList);
                TranRect.MidR = view.mWindowH.hv_imageHeight / 4;
                TranRect.MidC = view.mWindowH.hv_imageWidth / 4;
                TranRect.Phi = 0;
                TranRect.Length1 = view.mWindowH.hv_imageHeight / 4;
                TranRect.Length2 = view.mWindowH.hv_imageWidth / 4;
            }
            else if (DispImage != null && RoiList.ContainsKey(ModuleParam.ModuleName))
            {
                if (HomMat2D_Inverse != null && HomMat2D_Inverse.Length > 0)
                {
                    view.mWindowH.WindowH.genRect2(ModuleParam.ModuleName, TranRect.MidR, TranRect.MidC, TranRect.Phi, TranRect.Length1, TranRect.Length2, ref RoiList);
                    Aff.Affine2d(HomMat2D_Inverse, TranRect, InitRect);

                    InitRect.MidC = Math.Round(InitRect.MidC, 2);
                    InitRect.Phi = Math.Round(InitRect.Phi, 2);
                    InitRect.MidR = Math.Round(InitRect.MidR, 2);
                    InitRect.Length1 = Math.Round(InitRect.Length1, 2);
                    InitRect.Length2 = Math.Round(InitRect.Length2, 2);

                    if (InitLineChanged_Flag)
                    {
                        RectL1.Text = InitRect.Length1.ToString();
                        RectL2.Text = InitRect.Length2.ToString();
                        RectPY.Text = InitRect.MidR.ToString();
                        RectPX.Text = InitRect.MidC.ToString();
                        RectAngle.Text = InitRect.Phi.ToString();
                    }
                }
                else
                {
                    view.mWindowH.WindowH.genRect2(ModuleParam.ModuleName, InitRect.MidR, InitRect.MidC, InitRect.Phi, InitRect.Length1, InitRect.Length2, ref RoiList);
                    if (InitLineChanged_Flag)
                    {
                        RectL1.Text = InitRect.Length1.ToString();
                        RectL2.Text = InitRect.Length2.ToString();
                        RectAngle.Text = InitRect.Phi.ToString();
                        RectPX.Text = InitRect.MidC.ToString();
                        RectPY.Text = InitRect.MidR.ToString();
                    }
                }
            }
        }
        #endregion
    }
}
