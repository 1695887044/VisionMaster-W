using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.UI.WebControls;
using VM.Halcon.Config;
using VM.Start.Attributes;
using VM.Start.Common.Enums;
using VM.Start.Common.Provide;
using VM.Start.Common;
using VM.Start.Core;
using VM.Start.Dialogs.Views;
using VM.Start.Models;
using Plugin.CreateROI.Views;
using HalconDotNet;
using System.Windows.Forms;
using VM.Halcon.Model;
using VM.Start.ViewModels;
using VM.Halcon;
using VM.Start.Common.Helper;
using VM.Start.Events;
using EventMgrLib;
using VM.Start.Views.Dock;
using HandyControl.Controls;
using System.Configuration;
using System.Data.Common;

namespace Plugin.CreateROI.ViewModels
{
    #region enum
    public enum eLinkCommand
    {
        InputImageLink,
        Rect2Len1,
        Rect2Len2,
        Rect2MidR,
        Rect2MidC,
        Rect2Deg,
        CircleX,
        CircleY,
        CircleRadius
    }
    public enum eEditMode
    {
        正常显示,
        绘制涂抹,
        擦除涂抹,
    }
    public enum eDrawShape
    {
        矩形,
        圆形,
    }

    #endregion

    [Category("检测识别")]
    [DisplayName("创建ROI")]
    [ModuleImageName("CreateROI")]
    [Serializable]
    public class CreateROIViewModel : ModuleBase
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
                if (DispImage == null || !DispImage.IsInitialized())
                {
                    ChangeModuleRunStatus(eRunStatus.NG);
                    return false;
                }
                switch (SelectedROIType)
                {
                    case eDrawShape.矩形:
                        OutRegion.GenRectangle2(
                            Convert.ToDouble(GetLinkValue(Rect2MidR)),
                            Convert.ToDouble(GetLinkValue(Rect2MidC)),
                            (double)((HTuple)(Convert.ToDouble(GetLinkValue(Rect2Deg)))).TupleRad(),
                            Convert.ToDouble(GetLinkValue(Rect2Len1)),
                            Convert.ToDouble(GetLinkValue(Rect2Len2)));
                        break;
                    case eDrawShape.圆形:
                        OutRegion.GenCircle(
                            Convert.ToDouble(GetLinkValue(CircleX)),
                            Convert.ToDouble(GetLinkValue(CircleY)),
                            Convert.ToDouble(GetLinkValue(CircleRadius)));
                        break;
                    default:
                        break;
                }
                GetHomMat2D();
                if (HomMat2D != null && HomMat2D.Length > 0)
                {
                    OutRegion = OutRegion.AffineTransRegion(new HHomMat2D(HomMat2D), "nearest_neighbor");
                    if (finalRegion != null && finalRegion.IsInitialized())
                    {
                        finalRegion_Temp = finalRegion.AffineTransRegion(new HHomMat2D(HomMat2D), "nearest_neighbor");
                    }
                    else
                    {
                        finalRegion_Temp = new HRegion(finalRegion);
                    }
                }
                else
                {
                    if (finalRegion != null && finalRegion.IsInitialized())
                    {
                        finalRegion_Temp = new HRegion(finalRegion);
                    }
                }

                if (finalRegion_Temp != null && finalRegion_Temp.IsInitialized())
                {
                    OutRegion = OutRegion.Difference(finalRegion_Temp);
                }
                var view = ModuleView as CreateROIView;
                VMHWindowControl mWindowH;
                if (view == null || view.IsClosed)
                {
                    mWindowH = ViewDic.GetView(DispImage.DispViewID);
                }
                else
                {
                    mWindowH = view.mWindowH;
                }
                if (ShowResultContour)
                {
                    if (OutRegion != null && OutRegion.IsInitialized())
                    {
                        mWindowH.WindowH.DispHobject(OutRegion,"green",false);
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
        public override void AddOutputParams()
        {
            base.AddOutputParams();
            AddOutputParam("区域", "HRegion", OutRegion);
            if (IsOutImageReduced)
            {
                ImageReduced = DispImage.ReduceDomain(OutRegion);
                AddOutputParam("裁剪图像", "HImage", ImageReduced);
            }
        }
        #region Prop
        [NonSerialized]
        private bool IsDrawing = false;
        HRegion OutRegion = new HRegion(0.0,0,3);
        HImage ImageReduced = new HImage(FilePaths.ConfigFilePath+ "Background.bmp");
        private bool _ShowResultContour = true;
        /// <summary>
        /// 显示结果轮廓
        /// </summary>
        public bool ShowResultContour
        {
            get { return _ShowResultContour; }
            set { Set(ref _ShowResultContour, value); }
        }
        private bool _IsOutImageReduced;
        /// <summary>
        /// 输入裁剪图像
        /// </summary>
        public bool IsOutImageReduced
        {
            get { return _IsOutImageReduced; }
            set { Set(ref _IsOutImageReduced, value); }
        }

        private string _InputImageLinkText="";
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
        private eDrawShape _SelectedROIType = eDrawShape.矩形;
        /// <summary>
        /// 搜索区域源
        /// </summary>
        public eDrawShape SelectedROIType
        {
            get { return _SelectedROIType; }
            set
            {
                Set(ref _SelectedROIType, value, new Action(() =>
                {
                    ShowHRoi();
                })) ;
            }
        }
        private ROIRectangle2 _Rect2;

        public ROIRectangle2 Rect2
        {
            get 
            { 
                if (_Rect2 == null)
                {
                    _Rect2 = new ROIRectangle2(200,200,0,30,30);
                }
                return _Rect2; 
            }
            set { _Rect2 = value; }
        }
        private ROICircle _Circle;

        public ROICircle Circle
        {
            get
            {
                if (_Circle == null)
                {
                    _Circle = new ROICircle(200,200,30);
                }
                return _Circle;
            }
            set { _Circle = value; }
        }
        private LinkVarModel _Rect2Len1 = new LinkVarModel() { Text = "60" };
        public LinkVarModel Rect2Len1
        {
            get { return _Rect2Len1; }
            set { _Rect2Len1 = value; RaisePropertyChanged(); }
        }
        private LinkVarModel _Rect2Len2 = new LinkVarModel() { Text = "60" };
        public LinkVarModel Rect2Len2
        {
            get { return _Rect2Len2; }
            set { _Rect2Len2 = value; RaisePropertyChanged(); }
        }
        private LinkVarModel _Rect2MidR = new LinkVarModel() { Text = "300" };
        public LinkVarModel Rect2MidR
        {
            get { return _Rect2MidR; }
            set { _Rect2MidR = value; RaisePropertyChanged(); }
        }
        private LinkVarModel _Rect2MidC = new LinkVarModel() { Text = "300" };
        public LinkVarModel Rect2MidC
        {
            get { return _Rect2MidC; }
            set { _Rect2MidC = value; RaisePropertyChanged(); }
        }
        private LinkVarModel _Rect2Deg = new LinkVarModel() { Text = "90" };
        public LinkVarModel Rect2Deg
        {
            get { return _Rect2Deg; }
            set { _Rect2Deg = value; RaisePropertyChanged(); }
        }
        private LinkVarModel _CircleX = new LinkVarModel() { Text = "200" };
        /// <summary>
        /// 变换前-圆信息
        /// </summary>
        public LinkVarModel CircleX
        {
            get { return _CircleX; }
            set { _CircleX = value; RaisePropertyChanged(); }
        }
        private LinkVarModel _CircleY = new LinkVarModel() { Text = "200" };
        /// <summary>
        /// 变换前-圆信息
        /// </summary>
        public LinkVarModel CircleY
        {
            get { return _CircleY; }
            set { _CircleY = value; RaisePropertyChanged(); }
        }
        private LinkVarModel _CircleRadius = new LinkVarModel() { Text = "30" };
        /// <summary>
        /// 变换前-圆信息
        /// </summary>
        public LinkVarModel CircleRadius
        {
            get { return _CircleRadius; }
            set { _CircleRadius = value; RaisePropertyChanged(); }
        }

        public Array DrawShapes { get; set; } = Enum.GetValues(typeof(eDrawShape));
        private eDrawShape _DrawShape = eDrawShape.圆形;
        /// <summary>
        /// 涂抹形状
        /// </summary>
        public eDrawShape DrawShape
        {
            get { return _DrawShape; }
            set { Set(ref _DrawShape, value, new Action(() => SetBurshRegion())); }
        }

        private int _DrawSize=10;
        /// <summary>
        /// 涂抹尺寸
        /// </summary>
        public int DrawSize
        {
            get { return _DrawSize; }
            set { Set(ref _DrawSize, value, new Action(() => SetBurshRegion())); }
        }
        [NonSerialized]
        private bool RoiChanged_Flag = false;
        /// <summary> 区域列表 </summary>
        public Dictionary<string, ROI> RoiList = new Dictionary<string, ROI>();
        [NonSerialized]
        HRegion finalRegion_Temp = new HRegion(1.0, 1, 1);
        HRegion finalRegion = new HRegion(1.0,1,1);
        HRegion brushRegion = new HRegion(10.0, 10, 10);
        private eEditMode _EditMode = eEditMode.正常显示;
        /// <summary>
        /// 指定图像
        /// </summary>
        public eEditMode EditMode
        {
            get { return _EditMode; }
            set
            {
                Set(ref _EditMode, value, new Action(() =>
                {
                    switch (_EditMode)
                    {
                        case eEditMode.正常显示:
                            var view = ModuleView as CreateROIView;
                            if (view!=null)
                            {
                                view.mWindowH.DrawModel = false;
                            }
                            break;
                        case eEditMode.绘制涂抹:
                            DrawOrWipe(_EditMode);
                            break;
                        case eEditMode.擦除涂抹:
                            DrawOrWipe(_EditMode);
                            break;
                        default:
                            break;
                    }
                }));
            }
        }
        #endregion

        #region Command
        public override void Loaded()
        {
            base.Loaded();
            var view = ModuleView as CreateROIView;
            if (view != null)
            {
                ClosedView = true;
                if (view.mWindowH == null)
                {
                    view.mWindowH = new VMHWindowControl();
                    view.winFormHost.Child = view.mWindowH;
                }
                GetHomMat2D();
                if (DispImage == null || !DispImage.IsInitialized())
                {
                    SetDefaultLink();
                    if (InputImageLinkText == null) return;
                }
                GetDispImage(InputImageLinkText,true);
                if (DispImage != null && DispImage.IsInitialized())
                {
                    view.mWindowH.hControl.MouseUp += HControl_MouseUp;
                    IsLoaded_Flag = true;
                    ShowHRoi();
                    IsLoaded_Flag = false;
                }
                DrawSize = 5;
                Rect2Len1.TextChanged = new Action(() => { RoiChanged(); });
                Rect2Len2.TextChanged = new Action(() => { RoiChanged(); });
                Rect2MidR.TextChanged = new Action(() => { RoiChanged(); });
                Rect2MidC.TextChanged = new Action(() => { RoiChanged(); });
                Rect2Deg.TextChanged = new Action(() => { RoiChanged(); });
                CircleX.TextChanged = new Action(() => { RoiChanged(); });
                CircleY.TextChanged = new Action(() => { RoiChanged(); });
                CircleRadius.TextChanged = new Action(() => { RoiChanged(); });

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
                        var view = ModuleView as CreateROIView;
                        if (view == null) return;
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
            eLinkCommand linkCommand = (eLinkCommand)Enum.Parse(typeof(eLinkCommand), obj.SendName.Split(',')[1].ToString());
            switch (linkCommand)
            {
                case eLinkCommand.InputImageLink:
                    InputImageLinkText = obj.LinkName;
                    break;
                case eLinkCommand.Rect2Len1:
                    Rect2Len1.Text = obj.LinkName;
                    break;
                case eLinkCommand.Rect2Len2:
                    Rect2Len2.Text = obj.LinkName;
                    break;
                case eLinkCommand.Rect2MidR:
                    Rect2MidR.Text = obj.LinkName;
                    break;
                case eLinkCommand.Rect2MidC:
                    Rect2MidC.Text = obj.LinkName;
                    break;
                case eLinkCommand.Rect2Deg:
                    Rect2Deg.Text = obj.LinkName;
                    break;
                case eLinkCommand.CircleX:
                    CircleX.Text = obj.LinkName;
                    break;
                case eLinkCommand.CircleY:
                    CircleY.Text = obj.LinkName;
                    break;
                case eLinkCommand.CircleRadius:
                    CircleRadius.Text = obj.LinkName;
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
                            case eLinkCommand.Rect2Len1:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},Rect2Len1");
                                break;
                            case eLinkCommand.Rect2Len2:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},Rect2Len2");
                                break;
                            case eLinkCommand.Rect2MidR:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},Rect2MidR");
                                break;
                            case eLinkCommand.Rect2MidC:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},Rect2MidC");
                                break;
                            case eLinkCommand.Rect2Deg:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},Rect2Deg");
                                break;
                            case eLinkCommand.CircleX:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},CircleX");
                                break;
                            case eLinkCommand.CircleY:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},CircleY");
                                break;
                            case eLinkCommand.CircleRadius:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},CircleRadius");
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
        private CommandBase _ClearPaintCommand;
        public CommandBase ClearPaintCommand
        {
            get
            {
                if (_ClearPaintCommand == null)
                {
                    _ClearPaintCommand = new CommandBase((obj) =>
                    {
                        finalRegion.Dispose();
                        var view = ModuleView as CreateROIView;
                        if (view == null) return;
                        ShowHRoi();
                    });
                }
                return _ClearPaintCommand;
            }
        }


        #endregion

        #region Method
        private bool IsLoaded_Flag = false;
        private void RoiChanged()
        {
            var view = ModuleView as CreateROIView;
            if (view == null) return;
            if (RoiChanged_Flag == true) return;
            RoiChanged_Flag = true;
            IsLoaded_Flag = true;
            ShowHRoi();
            RoiChanged_Flag = false;
            IsLoaded_Flag = false;
        }
        private void HControl_MouseUp(object sender, MouseEventArgs e)
        {
            if (IsDrawing) return;
            try
            {
                RoiChanged_Flag = true;
                var view = ModuleView as CreateROIView;
                if (view == null) return;
                ROI roi = view.mWindowH.WindowH.smallestActiveROI(out string info, out string index);
                if (index.Length < 1) return;
                RoiList[index] = roi;
                switch (SelectedROIType)
                {
                    case eDrawShape.矩形:
                        ROIRectangle2 rectangle2 = (ROIRectangle2)roi;
                        if (HomMat2D_Inverse!=null && HomMat2D_Inverse.Length>0)
                        {
                            HRegion region = rectangle2.GetRegion();
                            region = region.AffineTransRegion(new HHomMat2D(HomMat2D_Inverse), "nearest_neighbor");
                            region.SmallestRectangle2(out double row, out double column, out double phi, out double length1, out double length2);
                            Rect2.Length1 = Math.Round(length1, 3);
                            Rect2.Length2 = Math.Round(length2, 3);
                            Rect2.MidC = Math.Round(column, 3);
                            Rect2.MidR = Math.Round(row, 3);
                            Rect2.Phi = Math.Round(phi, 3);
                        }
                        else
                        {
                            Rect2.Length1 = Math.Round(rectangle2.Length1, 3);
                            Rect2.Length2 = Math.Round(rectangle2.Length2, 3);
                            Rect2.MidC = Math.Round(rectangle2.MidC, 3);
                            Rect2.MidR = Math.Round(rectangle2.MidR, 3);
                            Rect2.Phi = -Math.Round(rectangle2.Phi, 3);
                        }
                        if (!Rect2Len1.Text.StartsWith("&"))
                        {
                            Rect2Len1.Text = Rect2.Length1.ToString();
                        }
                        if (!Rect2Len2.Text.StartsWith("&"))
                        {
                            Rect2Len2.Text = Rect2.Length2.ToString();
                        }
                        if (!Rect2MidC.Text.StartsWith("&"))
                        {
                            Rect2MidC.Text = Rect2.MidC.ToString();
                        }
                        if (!Rect2MidR.Text.StartsWith("&"))
                        {
                            Rect2MidR.Text = Rect2.MidR.ToString();
                        }
                        if (!Rect2Deg.Text.StartsWith("&"))
                        {
                            Rect2Deg.Text = Rect2.Deg.ToString();
                        }

                        view.mWindowH.WindowH.genRect2(ModuleParam.ModuleName + ROIDefine.Rectangle2, rectangle2.MidR, rectangle2.MidC, rectangle2.Phi, rectangle2.Length1, rectangle2.Length2, ref RoiList);
                        break;
                    case eDrawShape.圆形:
                        ROICircle circle = (ROICircle)roi;
                        if (HomMat2D_Inverse != null && HomMat2D_Inverse.Length > 0)
                        {
                            HRegion region2 = circle.GetRegion();
                            region2 = region2.AffineTransRegion(new HHomMat2D(HomMat2D_Inverse), "nearest_neighbor");
                            region2.SmallestCircle(out double row, out double column, out double radius);
                            Circle.CenterX = Math.Round(row, 3);
                            Circle.CenterY = Math.Round(column, 3);
                            Circle.Radius = Math.Round(radius, 3);
                        }
                        else
                        {
                            Circle.CenterX = Math.Round(circle.CenterX, 3);
                            Circle.CenterY = Math.Round(circle.CenterY, 3);
                            Circle.Radius = Math.Round(circle.Radius, 3);
                        }
                        if (!CircleX.Text.StartsWith("&"))
                        {
                            CircleX.Text = circle.CenterX.ToString();
                        }
                        if (!CircleY.Text.StartsWith("&"))
                        {
                            CircleY.Text = circle.CenterY.ToString();
                        }
                        if (!CircleRadius.Text.StartsWith("&"))
                        {
                            CircleRadius.Text = circle.Radius.ToString();
                        }
                        view.mWindowH.WindowH.genCircle(ModuleParam.ModuleName + ROIDefine.Circle, circle.CenterY, circle.CenterX, circle.Radius, ref RoiList);
                        break;
                    default:
                        break;
                }
                ShowHRoi();
            }
            catch (Exception ex)
            {
            }
            finally 
            { 
                RoiChanged_Flag = false; 
            }
        }
        public override void ShowHRoi()
        {
            var view = ModuleView as CreateROIView;
            if (view == null) return;
            VMHWindowControl mWindowH;
            if (view == null || view.IsClosed)
            {
                mWindowH = ViewDic.GetView(DispImage.DispViewID);
            }
            else
            {
                mWindowH = view.mWindowH;
            }
            ClearRoiAndText();
            mWindowH.ClearROI();
            HRegion region = new HRegion();
            switch (SelectedROIType)
            {
                case eDrawShape.矩形:
                    if (RoiList.ContainsKey(ModuleParam.ModuleName + ROIDefine.Rectangle2))
                    {
                        ROIRectangle2 ROIRect2 = (ROIRectangle2)RoiList[ModuleParam.ModuleName + ROIDefine.Rectangle2];
                        if (IsLoaded_Flag && HomMat2D!=null && HomMat2D.Length>0)
                        {
                            region = new HRegion();
                            region.GenRectangle2(
                                Convert.ToDouble(GetLinkValue(Rect2MidR)),
                                Convert.ToDouble(GetLinkValue(Rect2MidC)),
                                (double)((HTuple)(Convert.ToDouble(GetLinkValue(Rect2Deg)))).TupleRad(),
                                Convert.ToDouble(GetLinkValue(Rect2Len1)),
                                Convert.ToDouble(GetLinkValue(Rect2Len2)));
                            region = region.AffineTransRegion(new HHomMat2D(HomMat2D), "nearest_neighbor");
                            region.SmallestRectangle2(out double row, out double column, out double phi, out double length1, out double length2);
                            if (region.Area.I != 0)
                            {
                                mWindowH.WindowH.genRect2(ModuleParam.ModuleName + ROIDefine.Rectangle2,
                                    row,
                                    column,
                                    -phi,
                                    length1,
                                    length2,
                                    ref RoiList);
                            }
                            else
                            {
                                mWindowH.WindowH.genRect2(ModuleParam.ModuleName + ROIDefine.Rectangle2, 200, 200, 0, 30, 30, ref RoiList);
                            }
                        }
                        else
                        {
                            mWindowH.WindowH.genRect2(ModuleParam.ModuleName + ROIDefine.Rectangle2, ROIRect2.MidR, ROIRect2.MidC, ROIRect2.Phi, ROIRect2.Length1, ROIRect2.Length2, ref RoiList);
                        }
                    }
                    else
                    {
                        mWindowH.WindowH.genRect2(ModuleParam.ModuleName + ROIDefine.Rectangle2, 200, 200, 0, 30, 30, ref RoiList);
                    }
                    break;
                case eDrawShape.圆形:
                    if (RoiList.ContainsKey(ModuleParam.ModuleName + ROIDefine.Circle))
                    {
                        ROICircle ROICircle = (ROICircle)RoiList[ModuleParam.ModuleName + ROIDefine.Circle];
                        if (IsLoaded_Flag)
                        {
                            region.GenCircle(
                                Convert.ToDouble(GetLinkValue(CircleY)),
                                Convert.ToDouble(GetLinkValue(CircleX)),
                                Convert.ToDouble(GetLinkValue(CircleRadius)));
                            region = region.AffineTransRegion(new HHomMat2D(HomMat2D), "nearest_neighbor");
                            region.SmallestCircle(out double row, out double column, out double radius);
                            if (region.Area.I != 0)
                            {
                                mWindowH.WindowH.genCircle(
                                    ModuleParam.ModuleName + ROIDefine.Circle,
                                    row,
                                    column,
                                    radius,
                                    ref RoiList);
                            }
                            else
                            {
                                mWindowH.WindowH.genCircle(ModuleParam.ModuleName + ROIDefine.Circle, 100, 100, 30, ref RoiList);
                            }
                        }
                        else
                        {
                            mWindowH.WindowH.genCircle(ModuleParam.ModuleName + ROIDefine.Circle, ROICircle.CenterY, ROICircle.CenterX, ROICircle.Radius, ref RoiList);
                        }
                    }
                    else
                    {
                        mWindowH.WindowH.genCircle(ModuleParam.ModuleName + ROIDefine.Circle, 100,100,30, ref RoiList);
                    }
                    break;
                default:
                    break;
            }
            if (finalRegion!=null && finalRegion.IsInitialized())
            {
                if (HomMat2D != null && HomMat2D.Length > 0)
                {
                    finalRegion_Temp = finalRegion.AffineTransRegion(new HHomMat2D(HomMat2D), "nearest_neighbor");
                }
                else
                {
                    finalRegion_Temp = new HRegion(finalRegion);
                }
                ShowHRoi(new HRoi(ModuleParam.ModuleEncode, ModuleParam.ModuleName, ModuleParam.Remarks, HRoiType.屏蔽范围, "green", new HObject(finalRegion_Temp)));
            }
            List<HRoi> roiList = mHRoi.Where(c => c.ModuleName == ModuleParam.ModuleName).ToList();
            foreach (HRoi roi in roiList)
            {
                if (roi.roiType == HRoiType.文字显示)
                {
                    HText roiText = (HText)roi;
                    ShowTool.SetFont(mWindowH.hControl.HalconWindow, roiText.size, "false", "false");
                    ShowTool.SetMsg(mWindowH.hControl.HalconWindow, roiText.text, "image", roiText.row, roiText.col, roiText.drawColor, "false");
                }
                else
                {
                    mWindowH.WindowH.DispHobject(roi.hobject, roi.drawColor, roi.IsFillDisp);
                }
            }

        }
        #region 涂抹
        private void SetBurshRegion()
        {
            HObject ho_temp_brush = new HObject();
            HTuple hv_Row1 = 10, hv_Column1 = 10, hv_Row2 = null, hv_Column2 = null;
            HTuple imageWidth, imageHeight;
            if (DispImage == null || !DispImage.IsInitialized())
            {
                return;
            }
            HImage image = new HImage(DispImage);
            image.GetImageSize(out imageWidth, out imageHeight);
            switch (_DrawShape)
            {
                case eDrawShape.圆形:
                    HOperatorSet.GenCircle(out ho_temp_brush, imageWidth / 2, imageHeight / 2, DrawSize);
                    if (hv_Row1.D != 0)
                    {
                        brushRegion.Dispose();
                        brushRegion = new HRegion(ho_temp_brush);
                    }
                    break;
                case eDrawShape.矩形:
                    HOperatorSet.GenRectangle1(out ho_temp_brush, 0, 0, DrawSize, DrawSize);
                    if (hv_Row1.D != 0)
                    {
                        brushRegion.Dispose();
                        brushRegion = new HRegion(ho_temp_brush);
                    }
                    break;
                default:
                    break;
            }
        }
        /// <summary>
        /// 绘制或者擦除涂抹
        /// </summary>
        /// <param name="editMode"></param>
        private void DrawOrWipe(eEditMode editMode)
        {
            IsDrawing = true;
            var view = ModuleView as CreateROIView;
            if (view == null) return;
            view.mWindowH.DrawModel = true;
            view.mWindowH.Focus();
            HTuple hv_Button = null;
            HTuple hv_Row = null, hv_Column = null;
            HTuple areaBrush, rowBrush, columnBrush, homMat2D;
            HObject brush_region_affine = new HObject();
            HObject ho_Image = new HObject(DispImage);
            try
            {
                if (!brushRegion.IsInitialized())
                {
                    MessageView.Ins.MessageBoxShow("未设置画刷!", eMsgType.Warn);
                    return;
                }
                else
                {
                    HOperatorSet.AreaCenter(brushRegion, out areaBrush, out rowBrush, out columnBrush);
                }
                string color = "green";
                //画出笔刷
                switch (editMode)
                {
                    case eEditMode.绘制涂抹:
                        color = "green";
                        break;
                    case eEditMode.擦除涂抹:
                        color = "green";
                        //检查finalRegion是否有效
                        if (!finalRegion.IsInitialized())
                        {
                            MessageView.Ins.MessageBoxShow("请先涂抹出合适区域,再使用擦除功能!", eMsgType.Warn);
                            return;
                        }
                        break;
                    default:
                        return;
                }
                HOperatorSet.SetColor(view.mWindowH.hv_window, color);
                if (finalRegion.IsInitialized())
                {
                    if (HomMat2D!=null && HomMat2D.Length>0)
                    {
                        finalRegion_Temp = finalRegion.AffineTransRegion(new HHomMat2D(HomMat2D), "nearest_neighbor");
                    }
                    else
                    {
                        finalRegion_Temp = new HRegion(finalRegion);
                    }
                    view.mWindowH.DispObj(finalRegion_Temp, color);
                }
                #region "循环,等待涂抹"

                //鼠标状态
                hv_Button = 0;
                // 4为鼠标右键
                while (hv_Button != 4)
                {
                    //一直在循环,需要让halcon控件也响应事件,不然到时候跳出循环,之前的事件会一起爆发触发,
                    Application.DoEvents();
                    hv_Row = -1;
                    hv_Column = -1;
                    //获取鼠标坐标
                    try
                    {
                        HOperatorSet.GetMposition(view.mWindowH.hv_window, out hv_Row, out hv_Column, out hv_Button);
                    }
                    catch (HalconException ex)
                    {
                        hv_Button = 0;
                    }
                    HOperatorSet.SetSystem("flush_graphic", "false");
                    HOperatorSet.DispObj(ho_Image, view.mWindowH.hv_window);
                    ShowHRoi();
                    //view.mWindowH.ClearROI();
                    if (finalRegion!=null && finalRegion.IsInitialized())
                    {
                        if (HomMat2D != null && HomMat2D.Length > 0)
                        {
                            finalRegion_Temp = finalRegion.AffineTransRegion(new HHomMat2D(HomMat2D), "nearest_neighbor");
                        }
                        else
                        {
                            finalRegion_Temp = new HRegion(finalRegion);
                        }
                        view.mWindowH.DispObj(finalRegion_Temp, color);
                    }
                    //check if mouse cursor is over window
                    if (hv_Row >= 0 && hv_Column >= 0)
                    {
                        //放射变换
                        HOperatorSet.VectorAngleToRigid(rowBrush, columnBrush, 0, hv_Row, hv_Column, 0, out homMat2D);
                        brush_region_affine.Dispose();
                        HOperatorSet.AffineTransRegion(brushRegion, out brush_region_affine, homMat2D, "nearest_neighbor");
                        HOperatorSet.DispObj(brush_region_affine, view.mWindowH.hv_window);
                        HOperatorSet.SetSystem("flush_graphic", "true");
                        ShowTool.SetFont(view.mWindowH.hv_window, 20, "true", "false");
                        ShowTool.SetMsg(view.mWindowH.hv_window, "按下鼠标左键涂抹,右键结束!", "window", 20, 20, "green", "false");
                        //1为鼠标左键
                        if (hv_Button == 1)
                        {

                            //画出笔刷
                            switch (editMode)
                            {
                                case eEditMode.绘制涂抹:
                                    {
                                        if (finalRegion_Temp.IsInitialized())
                                        {
                                            HObject ExpTmpOutVar_0;
                                            HOperatorSet.Union2(finalRegion_Temp, brush_region_affine, out ExpTmpOutVar_0);
                                            finalRegion_Temp.Dispose();
                                            finalRegion_Temp = new HRegion(ExpTmpOutVar_0);
                                        }
                                        else
                                        {
                                            finalRegion_Temp = new HRegion(brush_region_affine);
                                        }
                                        if (HomMat2D_Inverse != null && HomMat2D_Inverse.Length > 0)
                                        {
                                            finalRegion = finalRegion_Temp.AffineTransRegion(new HHomMat2D(HomMat2D_Inverse), "nearest_neighbor");
                                        }
                                        else
                                        {
                                            finalRegion = new HRegion(finalRegion_Temp);
                                        }
                                    }
                                    break;
                                case eEditMode.擦除涂抹:
                                    {
                                        HObject ExpTmpOutVar_0;
                                        HOperatorSet.Difference(finalRegion_Temp, brush_region_affine, out ExpTmpOutVar_0);
                                        finalRegion_Temp.Dispose();
                                        finalRegion_Temp = new HRegion(ExpTmpOutVar_0);
                                        if (HomMat2D_Inverse != null && HomMat2D_Inverse.Length > 0)
                                        {
                                            finalRegion = finalRegion_Temp.AffineTransRegion(new HHomMat2D(HomMat2D_Inverse), "nearest_neighbor");
                                        }
                                        else
                                        {
                                            finalRegion = new HRegion(finalRegion_Temp);
                                        }
                                    }
                                    break;
                                default:
                                    return;
                            }
                        }
                    }
                }
                #endregion
            }
            catch (HalconException HDevExpDefaultException)
            {
                throw HDevExpDefaultException;
            }
            finally
            {
                EditMode = eEditMode.正常显示;
                view.mWindowH.ClearROI();
                if (HomMat2D_Inverse != null && HomMat2D_Inverse.Length > 0)
                {
                    finalRegion = finalRegion_Temp.AffineTransRegion(new HHomMat2D(HomMat2D_Inverse), "nearest_neighbor");
                }
                else
                {
                    finalRegion= new HRegion(finalRegion_Temp);
                }
                ShowHRoi();
                view.mWindowH.DispObj(finalRegion_Temp, "blue");
                view.mWindowH.DrawModel = false;
                IsDrawing = false;
            }

        }
        #endregion
        #endregion

    }
}
