using EventMgrLib;
using HalconDotNet;
using Plugin.MeasureLine.Views;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.UI.WebControls;
using System.Windows.Documents;
using System.Windows.Forms;
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

namespace Plugin.MeasureLine.ViewModels
{
    #region enum
    public enum eLinkCommand
    {
        InputImageLink,
        InitLineStartX,
        InitLineStartY,
        InitLineEndX,
        InitLineEndY
    }

    #endregion

    [Category("检测识别")]
    [DisplayName("直线工具")]
    [ModuleImageName("MeasureLine")]
    [Serializable]
    public class MeasureLineViewModel : ModuleBase
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
                    if (DisenableAffine2d && HomMat2D_Inverse!=null && HomMat2D_Inverse.Length > 0)
                    {
                        DisenableAffine2d = false;
                        Aff.Affine2d(HomMat2D_Inverse, TempLine, InitLine);
                        if (InitLineChanged_Flag)
                        {
                            InitLineStartX.Text = InitLine.StartX.ToString();
                            InitLineStartY.Text = InitLine.StartY.ToString();
                            InitLineEndX.Text = InitLine.EndX.ToString();
                            InitLineEndY.Text = InitLine.EndY.ToString();
                        }
                    }
                    if (HomMat2D != null && HomMat2D.Length > 0)
                    {
                        InitLine.StartX = Convert.ToDouble(GetLinkValue(InitLineStartX));
                        InitLine.StartY = Convert.ToDouble(GetLinkValue(InitLineStartY));
                        InitLine.EndX = Convert.ToDouble(GetLinkValue(InitLineEndX));
                        InitLine.EndY = Convert.ToDouble(GetLinkValue(InitLineEndY));
                        Aff.Affine2d(HomMat2D, InitLine, TranLine);
                    }
                    else
                    {
                        InitLine.StartX = TranLine.StartX = TempLine.StartX;
                        InitLine.StartY = TranLine.StartY = TempLine.StartY;
                        InitLine.EndX = TranLine.EndX = TempLine.EndX;
                        InitLine.EndY = TranLine.EndY = TempLine.EndY;
                    }
                    Meas.MeasLine(DispImage, TranLine, MeasInfo, OutLine, out HTuple RowList, out HTuple ColList, out HXLDCont m_MeasXLD, null);
                    if (ShowResultPoint && RowList.ToDArr().Length > 0) //显示结果点
                    {
                        Gen.GenCross(out HObject m_MeasCross, RowList, ColList, MeasInfo.Length2, new HTuple(45).TupleRad());
                        ShowHRoi(new HRoi(ModuleParam.ModuleEncode, ModuleParam.ModuleName, ModuleParam.Remarks, HRoiType.检测点, "red", new HObject(m_MeasCross)));
                    }
                    if (ShowResultLine && RowList.ToDArr().Length > 0) //显示结果线
                    {
                        Gen.GenContour(out HObject m_ResultXLD, OutLine.StartY, OutLine.EndY, OutLine.StartX, OutLine.EndX);
                        ShowHRoi(new HRoi(ModuleParam.ModuleEncode, ModuleParam.ModuleName, ModuleParam.Remarks, HRoiType.检测结果, "green", new HObject(m_ResultXLD)));
                    }
                    if (ShowMeasContour) //显示检测范围
                    {
                        if (m_MeasXLD != null && m_MeasXLD.IsInitialized())
                        {
                            ShowHRoi(new HRoi(ModuleParam.ModuleEncode, ModuleParam.ModuleName, ModuleParam.Remarks, HRoiType.检测范围, "blue", new HObject(m_MeasXLD.GenRegionContourXld("margin").Union1().ShapeTrans("rectangle2"))));
                        }
                    }
                    ShowHRoi();
                    if (RowList.ToDArr().Length > 0)
                    {
                        OutLine.Status = true;
                        ChangeModuleRunStatus(eRunStatus.OK);
                        return true;
                    }
                    else
                    {
                        OutLine.Status = false;
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
            //OutLine.Status == eRunStatus.OK ? true : false; 
            AddOutputParam("测量直线", "object", OutLine);
            AddOutputParam("中心X", "double", OutLine.MidX);
            AddOutputParam("中心Y", "double", OutLine.MidY);
            AddOutputParam("角度", "double", OutLine.Phi);
            AddOutputParam("起点X", "double", OutLine.StartX);
            AddOutputParam("起点Y", "double", OutLine.StartY);
            AddOutputParam("终点X", "double", OutLine.EndX);
            AddOutputParam("终点Y", "double", OutLine.EndY);
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
        private bool _ShowResultLine = true;
        /// <summary>显示结果直线 </summary>
        public bool ShowResultLine
        {
            get { return _ShowResultLine; }
            set { Set(ref _ShowResultLine, value); }
        }
        /// <summary> 区域列表 </summary>
        public Dictionary<string, ROI> RoiList = new Dictionary<string, ROI>();
        /// <summary>
        /// 检测形态信息
        /// </summary>
        public MeasInfoModel MeasInfo { get; set; } = new MeasInfoModel();
        private ROILine _OutLine = new ROILine();
        /// <summary>
        /// 输出直线信息
        /// </summary>
        public ROILine OutLine
        {
            get { return _OutLine; }
            set { Set(ref _OutLine, value); }
        }
        private LinkVarModel _InitLineStartX = new LinkVarModel() { Text = "10" };
        /// <summary>
        /// 变换前-直线信息
        /// </summary>
        public LinkVarModel InitLineStartX
        {
            get { return _InitLineStartX; }
            set { _InitLineStartX = value; RaisePropertyChanged(); }
        }
        private LinkVarModel _InitLineStartY = new LinkVarModel() { Text = "10" };
        /// <summary>
        /// 变换前-直线信息
        /// </summary>
        public LinkVarModel InitLineStartY
        {
            get { return _InitLineStartY; }
            set { _InitLineStartY = value; RaisePropertyChanged(); }
        }
        private LinkVarModel _InitLineEndX = new LinkVarModel() { Text = "10" };
        /// <summary>
        /// 变换前-直线信息
        /// </summary>
        public LinkVarModel InitLineEndX
        {
            get { return _InitLineEndX; }
            set { _InitLineEndX = value; RaisePropertyChanged(); }
        }
        private LinkVarModel _InitLineEndY = new LinkVarModel() { Text = "10" };
        /// <summary>
        /// 变换前-直线信息
        /// </summary>
        public LinkVarModel InitLineEndY
        {
            get { return _InitLineEndY; }
            set { _InitLineEndY = value; RaisePropertyChanged(); }
        }
        /// <summary>
        /// 变换前-直线信息
        /// </summary>
        public ROILine InitLine { get; set; } = new ROILine();
        /// <summary>
        /// 直线信息
        /// </summary>
        public ROILine TempLine { get; set; } = new ROILine();
        /// <summary>
        /// 变换后-直线信息
        /// </summary>
        public ROILine TranLine { get; set; } = new ROILine();
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
        #endregion

        #region Command
        public override void Loaded()
        {
            base.Loaded();
            var view = ModuleView as MeasureLineView;
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
                InitLineStartX.TextChanged = new Action(() => { InitLineChanged(); });
                InitLineStartY.TextChanged = new Action(() => { InitLineChanged(); });
                InitLineEndX.TextChanged = new Action(() => { InitLineChanged(); });
                InitLineEndY.TextChanged = new Action(() => { InitLineChanged(); });
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
                case eLinkCommand.InitLineStartX:
                    InitLineStartX.Text = obj.LinkName;
                    break;
                case eLinkCommand.InitLineStartY:
                    InitLineStartY.Text = obj.LinkName;
                    break;
                case eLinkCommand.InitLineEndX:
                    InitLineStartX.Text = obj.LinkName;
                    break;
                case eLinkCommand.InitLineEndY:
                    InitLineStartX.Text = obj.LinkName;
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
                            case eLinkCommand.InitLineStartX:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},InitLineStartX");
                                break;
                            case eLinkCommand.InitLineStartY:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},InitLineStartY");
                                break;
                            case eLinkCommand.InitLineEndX:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},InitLineEndX");
                                break;
                            case eLinkCommand.InitLineEndY:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},InitLineEndY");
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
                        var view = this.ModuleView as MeasureLineView;
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
            InitLine.StartX = Convert.ToDouble(GetLinkValue(InitLineStartX));
            InitLine.StartY = Convert.ToDouble(GetLinkValue(InitLineStartY));
            InitLine.EndX = Convert.ToDouble(GetLinkValue(InitLineEndX));
            InitLine.EndY = Convert.ToDouble(GetLinkValue(InitLineEndY));
            DisenableAffine2d = true;
            if (roiLine != null)
            {
                if (DisenableAffine2d && HomMat2D != null && HomMat2D.Length > 0)
                {
                    Aff.Affine2d(HomMat2D, InitLine, TempLine);
                    if (InitLineChanged_Flag)
                    {
                        roiLine.StartX = TempLine.StartX;
                        roiLine.StartY = TempLine.StartY;
                        roiLine.EndX = TempLine.EndX;
                        roiLine.EndY = TempLine.EndY;
                    }
                }
                else
                {
                    roiLine.StartX = InitLine.StartX;
                    roiLine.StartY = InitLine.StartY;
                    roiLine.EndX = InitLine.EndX;
                    roiLine.EndY = InitLine.EndY;
                    TempLine.StartX = InitLine.StartX;
                    TempLine.StartY = InitLine.StartY;
                    TempLine.EndX = InitLine.EndX;
                    TempLine.EndY = InitLine.EndY;
                }
                ExeModule();
                InitLineMethod();
            }
        }
        ROILine roiLine;
        private void HControl_MouseUp(object sender, MouseEventArgs e)
        {
            try
            {
                var view = ModuleView as MeasureLineView;
                if (view == null) return;
                ROI roi = view.mWindowH.WindowH.smallestActiveROI(out string info, out string index);
                if (index.Length > 0)
                {
                    roiLine = roi as ROILine;
                    if (roiLine != null)
                    {
                        TempLine.StartX = Math.Round(roiLine.StartX, 3);
                        TempLine.StartY = Math.Round(roiLine.StartY, 3);
                        TempLine.EndX = Math.Round(roiLine.EndX, 3);
                        TempLine.EndY = Math.Round(roiLine.EndY, 3);
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
            var view = ModuleView as MeasureLineView;
            if (view == null)
            {
                return;
            }
            if (TranLine.FlagLineStyle != null)
            {
                view.mWindowH.WindowH.genLine(ModuleParam.ModuleName, TranLine.StartX, TranLine.StartY, TranLine.EndX, TranLine.EndY, ref RoiList);
            }
            else if (DispImage != null && !RoiList.ContainsKey(ModuleParam.ModuleName))
            {
                view.mWindowH.WindowH.genLine(ModuleParam.ModuleName, view.mWindowH.hv_imageHeight / 4, view.mWindowH.hv_imageHeight / 4, view.mWindowH.hv_imageHeight / 4, view.mWindowH.hv_imageWidth / 2, ref RoiList);
                TranLine.StartX = view.mWindowH.hv_imageHeight / 4;
                TranLine.StartY = view.mWindowH.hv_imageHeight / 4;
                TranLine.EndX = view.mWindowH.hv_imageHeight / 4;
                TranLine.EndY = view.mWindowH.hv_imageWidth / 4;
            }
            else if (DispImage != null && RoiList.ContainsKey(ModuleParam.ModuleName))
            {
                if (HomMat2D_Inverse != null && HomMat2D_Inverse.Length > 0)
                {
                    view.mWindowH.WindowH.genLine(ModuleParam.ModuleName, TranLine.StartY, TranLine.StartX, TranLine.EndY, TranLine.EndX, ref RoiList);
                    Aff.Affine2d(HomMat2D_Inverse, TranLine, InitLine);
                    InitLine.StartX = Math.Round(InitLine.StartX, 3);
                    InitLine.StartY = Math.Round(InitLine.StartY, 3);
                    InitLine.EndX = Math.Round(InitLine.EndX, 3);
                    InitLine.EndY = Math.Round(InitLine.EndY, 3);
                    if (InitLineChanged_Flag)
                    {
                        InitLineStartX.Text = InitLine.StartX.ToString();
                        InitLineStartY.Text = InitLine.StartY.ToString();
                        InitLineEndX.Text = InitLine.EndX.ToString();
                        InitLineEndY.Text = InitLine.EndY.ToString();
                    }
                }
                else
                {
                    view.mWindowH.WindowH.genLine(ModuleParam.ModuleName, InitLine.StartY, InitLine.StartX, InitLine.EndY, InitLine.EndX, ref RoiList);
                    if (InitLineChanged_Flag)
                    {
                        InitLineStartX.Text = InitLine.StartX.ToString();
                        InitLineStartY.Text = InitLine.StartY.ToString();
                        InitLineEndX.Text = InitLine.EndX.ToString();
                        InitLineEndY.Text = InitLine.EndY.ToString();
                    }
                }
            }
        }
        #endregion
    }
}
