using EventMgrLib;
using HalconDotNet;
using Plugin.MeasureCircle.Views;
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

namespace Plugin.MeasureCircle.ViewModels
{
    public enum eLinkCommand
    {
        InputImageLink,
        InitCenterX,
        InitCenterY,
        InitRadius
    }

    [Category("检测识别")]
    [DisplayName("圆形测量")]
    [ModuleImageName("MeasureCircle")]
    [Serializable]
    public class MeasureCircleViewModel : ModuleBase
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
                GetDispImage(InputImageLinkText);
                if (DispImage != null && DispImage.IsInitialized())
                {
                    GetHomMat2D();
                    if (DisenableAffine2d && HomMat2D_Inverse != null && HomMat2D_Inverse.Length > 0)
                    {
                        DisenableAffine2d = false;
                        Aff.Affine2d(HomMat2D_Inverse, TempCircle, InitCircle);
                        if (InitCircleChanged_Flag)
                        {
                            InitCenterX.Text = InitCircle.CenterX.ToString();
                            InitCenterY.Text = InitCircle.CenterY.ToString();
                            InitRadius.Text = InitCircle.Radius.ToString();
                        }
                    }
                    if (HomMat2D!=null && HomMat2D.Length>0)
                    {
                        InitCircle.CenterX = Convert.ToDouble(GetLinkValue(InitCenterX));
                        InitCircle.CenterY = Convert.ToDouble(GetLinkValue(InitCenterY));
                        InitCircle.Radius = Convert.ToDouble(GetLinkValue(InitRadius));
                        Aff.Affine2d(HomMat2D, InitCircle, TranCircle);
                    }
                    else
                    {
                        InitCircle.CenterX = TranCircle.CenterX = TempCircle.CenterX;
                        InitCircle.CenterY = TranCircle.CenterY = TempCircle.CenterY;
                        InitCircle.Radius = TranCircle.Radius = TempCircle.Radius;
                    }
                    Meas.MeasCircle(DispImage, TranCircle, MeasInfo, null, OutCircle, out HTuple RowList, out HTuple ColList, out HXLDCont m_MeasXLD);
                    ///增加没查到圆判断输出NG
                    if (RowList.Length == 0)
                    {   
                        OutCircle.CenterX=0;
                        OutCircle.CenterY=0;    
                        OutCircle.Radius=0;
                        ShowHRoi(new HRoi(ModuleParam.ModuleEncode, ModuleParam.ModuleName, ModuleParam.Remarks, HRoiType.检测范围, "blue", new HObject(m_MeasXLD)));
                        ShowHRoi();
                        ChangeModuleRunStatus(eRunStatus.NG);
                        return false;
                    }
                    if (ShowResultPoint)
                    {
                        Gen.GenCross(out HObject m_MeasCross, RowList, ColList, MeasInfo.Length2, new HTuple(45).TupleRad());
                        ShowHRoi(new HRoi(ModuleParam.ModuleEncode, ModuleParam.ModuleName, ModuleParam.Remarks, HRoiType.检测点, "red", new HObject(m_MeasCross)));
                    }
                    if (ShowResultCircle)
                    {
                        Gen.GenCircle(out HObject m_ResultXLD, OutCircle.CenterX, OutCircle.CenterY, OutCircle.Radius);
                        ShowHRoi(new HRoi(ModuleParam.ModuleEncode, ModuleParam.ModuleName, ModuleParam.Remarks, HRoiType.检测结果, "green", new HObject(m_ResultXLD)));
                    }
                    if (ShowMeasContour)
                    {
                        ShowHRoi(new HRoi(ModuleParam.ModuleEncode, ModuleParam.ModuleName, ModuleParam.Remarks, HRoiType.检测范围, "blue", new HObject(m_MeasXLD)));
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
            AddOutputParam("测量圆", "object", OutCircle);
            AddOutputParam("圆心X", "double", OutCircle.CenterX);
            AddOutputParam("圆心Y", "double", OutCircle.CenterY);
            if (OutPutRealCoordFlag)
            {
                AddOutputParam("半径", "double", OutCircle.Radius * Scale);
                AddOutputParam("直径", "double", OutCircle.Radius * Scale * 2);
            }
            else
            {
                AddOutputParam("半径", "double", OutCircle.Radius);
                AddOutputParam("直径", "double", OutCircle.Radius * 2);
            }
           
            AddOutputParam("状态", "bool", ModuleParam.Status == eRunStatus.OK ? true : false);
            AddOutputParam("时间", "int", ModuleParam.ElapsedTime);
        }
        #region Prop
        private bool DisenableAffine2d = false;
        private bool InitCircleChanged_Flag = false;
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
        private bool _ShowResultCircle = true;
        /// <summary>显示结果直线 </summary>
        public bool ShowResultCircle
        {
            get { return _ShowResultCircle; }
            set { Set(ref _ShowResultCircle, value); }
        }
        private double _Scale=1;
        public double Scale
        {
            get { return _Scale; }
            set {_Scale=value; RaisePropertyChanged(); }
        }
        private bool _OutPutRealCoordFlag=false;
        public bool OutPutRealCoordFlag
        {
            get { return _OutPutRealCoordFlag; }
            set { _OutPutRealCoordFlag = value; RaisePropertyChanged(); }
        }

        /// <summary> 区域列表 </summary>
        public Dictionary<string, ROI> RoiList = new Dictionary<string, ROI>();
        /// <summary>
        /// 检测形态信息
        /// </summary>
        public MeasInfoModel MeasInfo { get; set; } = new MeasInfoModel();
        private ROICircle _OutCircle = new ROICircle();
        /// <summary>
        /// 输出直线信息
        /// </summary>
        public ROICircle OutCircle
        {
            get { return _OutCircle; }
            set { Set(ref _OutCircle, value); }
        }
        private LinkVarModel _InitCenterX = new LinkVarModel() { Text = "10" };
        /// <summary>
        /// 变换前-圆信息
        /// </summary>
        public LinkVarModel InitCenterX
        {
            get { return _InitCenterX; }
            set { _InitCenterX = value; RaisePropertyChanged(); }
        }
        private LinkVarModel _InitCenterY = new LinkVarModel() { Text = "10" };
        /// <summary>
        /// 变换前-圆信息
        /// </summary>
        public LinkVarModel InitCenterY
        {
            get { return _InitCenterY; }
            set { _InitCenterY = value; RaisePropertyChanged(); }
        }
        private LinkVarModel _InitRadius = new LinkVarModel() { Text = "10" };
        /// <summary>
        /// 变换前-圆信息
        /// </summary>
        public LinkVarModel InitRadius
        {
            get { return _InitRadius; }
            set { _InitRadius = value; RaisePropertyChanged(); }
        }

        /// <summary>
        /// 变换前-圆信息
        /// </summary>
        public ROICircle InitCircle { get; set; } = new ROICircle();
        /// <summary>
        /// 变换前-圆信息
        /// </summary>
        public ROICircle TempCircle { get; set; } = new ROICircle();
        /// <summary>
        /// 变换后-圆信息
        /// </summary>
        public ROICircle TranCircle { get; set; } = new ROICircle();
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
            var view = ModuleView as MeasureCircleView;
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
                    InitCircleMethod();
                }
                InitCenterX.TextChanged = new Action(() => { InitCircleChanged(); });
                InitCenterY.TextChanged = new Action(() => { InitCircleChanged(); });
                InitRadius.TextChanged = new Action(() => { InitCircleChanged(); });

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
                case eLinkCommand.InitCenterX:
                    InitCenterX.Text = obj.LinkName;
                    break;
                case eLinkCommand.InitCenterY:
                    InitCenterY.Text = obj.LinkName;
                    break;
                case eLinkCommand.InitRadius:
                    InitCenterX.Text = obj.LinkName;
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
                            case eLinkCommand.InitCenterX:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},InitCenterX");
                                break;
                            case eLinkCommand.InitCenterY:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},InitCenterY");
                                break;
                            case eLinkCommand.InitRadius:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},InitRadius");
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
                        InitCircleMethod();
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
                        var view = this.ModuleView as MeasureCircleView;
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
        private void InitCircleChanged()
        {
            if (InitCircleChanged_Flag == true) return;
            InitCircle.CenterX = Convert.ToDouble(GetLinkValue(InitCenterX));
            InitCircle.CenterX = Convert.ToDouble(GetLinkValue(InitCenterX));
            InitCircle.Radius = Convert.ToDouble(GetLinkValue(InitRadius));
            DisenableAffine2d = true;
            if (roiCircle != null)
            {
                if (DisenableAffine2d && HomMat2D.Length > 0)
                {
                    Aff.Affine2d(HomMat2D, InitCircle, TempCircle);
                    if (InitCircleChanged_Flag)
                    {
                        roiCircle.CenterX = TempCircle.CenterX;
                        TempCircle.CenterX = TempCircle.CenterX;
                        TempCircle.Radius = TempCircle.Radius;
                    }
                }
                else
                {
                    roiCircle.CenterX = InitCircle.CenterX;
                    roiCircle.CenterY = InitCircle.CenterY;
                    roiCircle.Radius = InitCircle.Radius;
                    TempCircle.CenterX = InitCircle.CenterX;
                    TempCircle.CenterY = InitCircle.CenterY;
                    TempCircle.Radius = InitCircle.Radius;
                }
                ExeModule();
                InitCircleMethod();
            }
        }
        ROICircle roiCircle;
        private void HControl_MouseUp(object sender, MouseEventArgs e)
        {
            try
            {
                var view = ModuleView as MeasureCircleView;
                if (view == null) return; ;
                ROI roi = view.mWindowH.WindowH.smallestActiveROI(out string info, out string index);
                if (index.Length > 0)
                {
                    roiCircle = roi as ROICircle;
                    if (roiCircle != null)
                    {
                        TempCircle.CenterX = Math.Round(roiCircle.CenterX, 3);
                        TempCircle.CenterY = Math.Round(roiCircle.CenterY, 3);
                        TempCircle.Radius = Math.Round(roiCircle.Radius, 3);
                        DisenableAffine2d = true;
                        InitCircleChanged_Flag = true;
                        ExeModule();
                        InitCircleMethod();
                        InitCircleChanged_Flag = false;
                    }
                }
            }
            catch (Exception ex)
            {
            }
        }
        public void InitCircleMethod()
        {
            var view = ModuleView as MeasureCircleView;
            if (view == null)
            {
                return;
            }
            if (TranCircle.FlagLineStyle != null)
            {
                view.mWindowH.WindowH.genCircle(ModuleParam.ModuleName, TranCircle.CenterX, TranCircle.CenterY, TranCircle.Radius, ref RoiList);
            }
            else if (DispImage != null && !RoiList.ContainsKey(ModuleParam.ModuleName))
            {
                view.mWindowH.WindowH.genCircle(ModuleParam.ModuleName, view.mWindowH.hv_imageHeight / 2, view.mWindowH.hv_imageHeight / 2, 30, ref RoiList);
                TranCircle.CenterX = view.mWindowH.hv_imageHeight / 2;
                TranCircle.CenterY = view.mWindowH.hv_imageHeight / 2;
                TranCircle.Radius = 30;
            }
            else if (DispImage != null && RoiList.ContainsKey(ModuleParam.ModuleName))
            {
                if ((HomMat2D_Inverse!=null)&&(HomMat2D_Inverse.Length > 0)) 
                {
                    view.mWindowH.WindowH.genCircle(ModuleParam.ModuleName, TranCircle.CenterY, TranCircle.CenterX, TranCircle.Radius, ref RoiList);
                    Aff.Affine2d(HomMat2D_Inverse, TranCircle, InitCircle);
                    InitCircle.CenterX = Math.Round(InitCircle.CenterX, 3);
                    InitCircle.CenterY = Math.Round(InitCircle.CenterY, 3);
                    InitCircle.Radius = Math.Round(InitCircle.Radius, 3);
                    if (InitCircleChanged_Flag)
                    {
                        InitCenterX.Text = InitCircle.CenterX.ToString();
                        InitCenterY.Text = InitCircle.CenterY.ToString();
                        InitRadius.Text = InitCircle.Radius.ToString();
                    }
                }
                else
                {
                    view.mWindowH.WindowH.genCircle(ModuleParam.ModuleName, InitCircle.CenterY, InitCircle.CenterX, InitCircle.Radius, ref RoiList);
                    if (InitCircleChanged_Flag)
                    {
                        InitCenterX.Text = InitCircle.CenterX.ToString();
                        InitCenterY.Text = InitCircle.CenterY.ToString();
                        InitRadius.Text = InitCircle.Radius.ToString();
                    }
                }
            }
        }
        #endregion
    }
}
