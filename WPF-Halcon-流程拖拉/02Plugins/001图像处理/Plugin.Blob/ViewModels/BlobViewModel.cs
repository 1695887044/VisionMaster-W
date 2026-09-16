using EventMgrLib;
using HalconDotNet;
using HandyControl.Controls;
using Plugin.Blob.Views;
using Plugin.GrabImage.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.UI.WebControls;
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
using VM.Start.Dialogs.Views;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.ViewModels;
using VM.Start.Views.Dock;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ToolTip;

namespace Plugin.Blob.ViewModels
{
    #region enum
    public enum eLinkCommand
    {
        InputImageLink,
        MathNum
    }
    public enum eOperateCommand
    {
        StartLearn,
        Edit,
        EndLearn,
        Cancel
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
        输入图像
    }
    public enum eChannels
    { 
        R,
        G,
        B
    }
    #endregion

    [Category("图像处理")]
    [DisplayName("斑点分析")]
    [ModuleImageName("Blob")]
    [Serializable]
    public class BlobViewModel : ModuleBase
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
                GetDispImage(InputImageLinkText);
                if (DispImage == null || !DispImage.IsInitialized())
                {
                    ChangeModuleRunStatus(eRunStatus.NG);
                    return false;
                }
                ChannelNum = DispImage.CountChannels().I;
                if (ChannelNum ==3) 
                { 
                    DecomposeImage();
                    IsThreeChannel = true;
                }
                else
                {
                    IsThreeChannel = false;
                }
                if (ReduceRegion==null)
                {
                    ReduceRegion = new HRegion();
                }
                switch (SelectedROIType)
                {
                    case eDrawShape.矩形:
                        ReduceRegion.GenRectangle2(Rectangle2Region.MidR, Rectangle2Region.MidC, -Rectangle2Region.Phi, Rectangle2Region.Length1, Rectangle2Region.Length2);
                        break;
                    case eDrawShape.圆形:
                        ReduceRegion.GenCircle(CircleRegion.CenterY, CircleRegion.CenterX, CircleRegion.Radius);
                        break;
                    case eDrawShape.输入图像:
                        ReduceRegion = DispImage.GetDomain();
                        ReduceRegion.GenCircle(CircleRegion.CenterY, CircleRegion.CenterX, CircleRegion.Radius);
                        break;
                    default:
                        break;
                }
                GetHomMat2D();
                HImage ImageRe = new HImage();
                if (HomMat2D!=null&&(HomMat2D.Length > 0))
                {
                    HHomMat2D _HHmmat2d = new HHomMat2D(HomMat2D);
                    ReduceRegion = ReduceRegion.AffineTransRegion(_HHmmat2d, "nearest_neighbor");
                    //OutRegion = OutRegion.AffineTransRegion(_HHmmat2d, "nearest_neighbor");
                    if (finalRegion != null && finalRegion.IsInitialized())
                    {
                        HRegion region = finalRegion.AffineTransRegion(_HHmmat2d, "nearest_neighbor");
                        ReduceRegion = ReduceRegion.Difference(region);
                    }
                }
                if (ChannelNum == 3)
                {
                    switch (SelectedImageChannel)
                    {
                        case eChannels.R:
                            ImageRe = hImageR.ReduceDomain(ReduceRegion); 
                            OutRegion = ImageRe.Threshold((double)ThresholdMin, (double)ThresholdMax);
                            ShowRoi();
                            break;
                        case eChannels.G:
                            ImageRe = hImageG.ReduceDomain(ReduceRegion);
                            OutRegion = ImageRe.Threshold((double)ThresholdMin, (double)ThresholdMax);
                            ShowRoi();
                            break;
                        case eChannels.B:
                            ImageRe = hImageB.ReduceDomain(ReduceRegion);
                            OutRegion = ImageRe.Threshold((double)ThresholdMin, (double)ThresholdMax);
                            ShowRoi();
                            break;
                        default:
                            break;
                    }
                }
                else if (ChannelNum == 1)
                {
                    ImageRe = DispImage.ReduceDomain(ReduceRegion);
                    OutRegion = ImageRe.Threshold((double)ThresholdMin, (double)ThresholdMax);
                    ShowRoi();
                }
                if (!ReduceRegion.IsInitialized())
                {
                    ChangeModuleRunStatus(eRunStatus.NG);
                    return false;
                }
                if (OutPutMaxArea)
                {
                    HRegion _region = OutRegion.Connection();
                    OutRegion = _region.SelectShapeStd("max_area", 70);
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
        public void DecomposeImage()
        {
            hImageR = DispImage.Decompose3(out hImageG, out hImageB);
        }
        public override void AddOutputParams()
        {
            base.AddOutputParams();
            AddOutputParam("区域", "HRegion", OutRegion);
            try
            {
                if (OutRegion==null)
                {
                    OutRegion = new HRegion();
                }
                Area = OutRegion.Area.D;
                AddOutputParam("面积", "double", OutRegion.Area.D);
                AddOutputParam("X坐标", "double", OutRegion.Column.D);
                AddOutputParam("Y坐标", "double", OutRegion.Row.D);
            }
            catch (Exception)
            {
                AddOutputParam("面积", "double", 0);
                AddOutputParam("X坐标", "double", 0);
                AddOutputParam("Y坐标", "double", 0);
            }
        }

        #region Prop
        [NonSerialized]
        int ChannelNum = 1;
        [NonSerialized]
        HImage hImageR = new HImage();
        [NonSerialized]
        HImage hImageG = new HImage();
        [NonSerialized]
        HImage hImageB = new HImage();
        private bool _IsThreeChannel;

        public bool IsThreeChannel
        {
            get { return _IsThreeChannel; }
            set { _IsThreeChannel = value; RaisePropertyChanged(); }
        }

        [NonSerialized]
        private HRegion _ReduceRegion;

        public HRegion ReduceRegion
        {
            get
            {
                if (_ReduceRegion == null)
                {
                    _ReduceRegion = new HRegion();
                }
                return _ReduceRegion;
            }
            set { _ReduceRegion = value; }
        }

        [NonSerialized]
        HRegion OutRegion = new HRegion();
        /// <summary> 区域列表 </summary>
        public Dictionary<string, ROI> RoiList = new Dictionary<string, ROI>();
    
        private int _ThresholdMax;
        public int ThresholdMax
        {
            get { return _ThresholdMax; }
            set
            {
                if (ReduceRegion==null)
                {
                    ReduceRegion = new HRegion();
                }
                _ThresholdMax = value;
                ThresholdChanged();
            }
        }

        private int _ThresholdMin;
        public int ThresholdMin
        {
            get { return _ThresholdMin; }
            set
            {
                if (ReduceRegion==null ) { ReduceRegion = new HRegion(); }
                _ThresholdMin = value;
                ThresholdChanged();
            }
        }
        private double _Area;
        public double Area
        {
            get { return _Area; }
            set
            {
                _Area = value;
                RaisePropertyChanged();
            }
        }
        private bool _OutPutMaxArea = false;
        public bool OutPutMaxArea
        {
            get { return _OutPutMaxArea; }
            set { _OutPutMaxArea = value; ExeModule(); RaisePropertyChanged(); }

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
                    ImageChanged();
                }));
            }
        }
        public eChannels _SelectedImageChannel = eChannels.R;
        public eChannels SelectedImageChannel
        {
            get { return _SelectedImageChannel; }
            set { _SelectedImageChannel = value;
                ImageChanged();
                ThresholdChanged();
            }
        }


        private ROIRectangle2 _Rectangle2Region;
        /// <summary>
        /// 矩形2
        /// </summary>
        public ROIRectangle2 Rectangle2Region
        {
            get
            {
                if (_Rectangle2Region == null)
                {
                    int hv_imageWidth=1000, hv_imageHeight=1000; //图片宽,高
                    if (DispImage != null && DispImage.IsInitialized())
                    {
                        DispImage.GetImageSize(out hv_imageWidth, out hv_imageHeight);
                    }
                    _Rectangle2Region = new ROIRectangle2(hv_imageWidth / 2, hv_imageHeight / 2, 0, hv_imageWidth / 10, hv_imageHeight / 10);
                }
                return _Rectangle2Region;
            }
            set { Set(ref _Rectangle2Region, value); }
        }
        private ROICircle _CircleRegion;
        /// <summary>
        /// 圆
        /// </summary>
        public ROICircle CircleRegion
        {
            get
            {
                if (_CircleRegion == null)
                {
                    int hv_imageWidth = 1000, hv_imageHeight = 1000; //图片宽,高
                    if (DispImage != null && DispImage.IsInitialized())
                    {
                        DispImage.GetImageSize(out hv_imageWidth, out hv_imageHeight);
                    }
                    _CircleRegion = new ROICircle(hv_imageWidth / 2, hv_imageHeight / 2, 30);
                }
                return _CircleRegion;
            }
            set { Set(ref _CircleRegion, value); }
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

        private int _DrawSize = 10;
        /// <summary>
        /// 涂抹尺寸
        /// </summary>
        public int DrawSize
        {
            get { return _DrawSize; }
            set { Set(ref _DrawSize, value, new Action(() => SetBurshRegion())); }
        }
        [NonSerialized]
        public HXLDCont contour_xld;
        [NonSerialized]
        HRegion finalRegion = new HRegion();
        [NonSerialized]
        HObject brushRegion = new HObject();
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
                            var view = ModuleView as BlobView;
                            view.mWindowH.DrawModel = false;
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
            var view = ModuleView as BlobView;
            view = ModuleView as BlobView;
            if (view != null)
            {
                ClosedView = true;
                if (view.mWindowH == null)
                {
                    view.mWindowH = new VMHWindowControl();
                    view.winFormHost.Child = view.mWindowH;
                }
                if (InputImageLinkText == null || InputImageLinkText =="")
                {
                    SetDefaultLink();
                    if (InputImageLinkText == null) return;
                }
                GetDispImage(InputImageLinkText);
                //view.mWindowH.DispObj(DispImage);
                ImageChanged();
                ThresholdChanged();
                view.mWindowH.hControl.MouseUp += HControl_MouseUp;
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
                        var view = ModuleView as BlobView;
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
                    if (InputImageLinkText == null) return;
                    GetDispImage(InputImageLinkText);
                    ImageChanged();
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
                            case eLinkCommand.MathNum:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "int");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},MathNumLink");
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
                        var view = ModuleView as BlobView;
                        view.mWindowH.HobjectToHimage(DispImage);
                        view.mWindowH.WindowH.DispHobject(contour_xld, "green");
                    });
                }
                return _ClearPaintCommand;
            }
        }
        #endregion

        #region Method
        private void HControl_MouseUp(object sender, MouseEventArgs e)
        {
            try
            {
                var view = ModuleView as BlobView;
                if (view == null) return; ;
                ROI roi = view.mWindowH.WindowH.smallestActiveROI(out string info, out string index);
                view.mWindowH.DispObj(finalRegion);
                if (index.Length < 1) return;
                RoiList[index] = roi;
                switch (SelectedROIType)
                {
                    case eDrawShape.矩形:
                        Rectangle2Region = (ROIRectangle2)roi;
                        Rectangle2Region.Length1 = Math.Round(Rectangle2Region.Length1, 2);
                        Rectangle2Region.Length2 = Math.Round(Rectangle2Region.Length2, 2);
                        Rectangle2Region.MidC = Math.Round(Rectangle2Region.MidC, 2);
                        Rectangle2Region.MidR = Math.Round(Rectangle2Region.MidR, 2);
                        ReduceRegion.GenRectangle2(Rectangle2Region.MidR, Rectangle2Region.MidC, -Rectangle2Region.Phi, Rectangle2Region.Length1, Rectangle2Region.Length2);
                        break;
                    case eDrawShape.圆形:
                        CircleRegion = (ROICircle)roi;
                        CircleRegion.CenterX = Math.Round(CircleRegion.CenterX, 2);
                        CircleRegion.CenterY = Math.Round(CircleRegion.CenterY, 2);
                        CircleRegion.Radius = Math.Round(CircleRegion.Radius, 2);
                        ReduceRegion.GenCircle(CircleRegion.CenterY, CircleRegion.CenterX, CircleRegion.Radius);
                        break;
                    default:
                        break;
                }
                ThresholdChanged();
            }
            catch (Exception ex)
            {
            }
        }
        [NonSerialized]
        VMHWindowControl mWindowH;
        private void ShowImage(HImage image)
        {
            var view = ModuleView as BlobView;
            if (view == null || view.IsClosed)
            {
                mWindowH = ViewDic.GetView(DispImage.DispViewID);
            }
            else
            {
                mWindowH = view.mWindowH;
            }
            mWindowH.ClearWindow();
            mWindowH.HobjectToHimage(image);
        }
        private void ShowRoi()
        {
            //var view = ModuleView as BlobView;
            bool dispDrawRoi = true;
            //if (view == null || view.IsClosed)
            //{
            //    dispDrawRoi = false;
            //}

            var view = ModuleView as BlobView;
            if (view == null || view.IsClosed)
            {
                mWindowH = ViewDic.GetView(DispImage.DispViewID);
                dispDrawRoi = false;
            }
            else
            {
                mWindowH = view.mWindowH;
            }


            if (dispDrawRoi)
            {
                switch (SelectedROIType)
                {
                    case eDrawShape.矩形:
                        mWindowH.WindowH.DispROI(ModuleParam.ModuleEncode + ModuleParam.ModuleName + ROIType.Rectangle2, Rectangle2Region);
                        break;

                    case eDrawShape.圆形:
                        mWindowH.WindowH.DispROI(ModuleParam.ModuleEncode + ModuleParam.ModuleName + ROIType.Circle, CircleRegion);
                        break;
                    default:
                        break;
                }
            }

            if (ReduceRegion != null && ReduceRegion.IsInitialized())
            {
                ShowHRoi(new HRoi(ModuleParam.ModuleEncode, ModuleParam.ModuleName, ModuleParam.Remarks, HRoiType.搜索范围, "green", new HObject(ReduceRegion)));
            }
            if (OutRegion != null && OutRegion.IsInitialized())
            {
                ShowHRoi(new HRoi(ModuleParam.ModuleEncode, ModuleParam.ModuleName, ModuleParam.Remarks, HRoiType.检测结果, "green", new HObject(OutRegion),true));
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
        public void ShowImageAndRoi(HImage image)
        {
            ShowImage(image);
            ShowRoi();
        }
        private void SetBurshRegion()
        {
            HObject ho_temp_brush = new HObject();
            HTuple hv_Row1 = 10, hv_Column1 = 10, hv_Row2 = null, hv_Column2 = null;
            HTuple imageWidth, imageHeight;
            HImage image = new HImage(DispImage);
            image.GetImageSize(out imageWidth, out imageHeight);
            switch (DrawShape)
            {
                case eDrawShape.圆形:
                    HOperatorSet.GenCircle(out ho_temp_brush, imageWidth / 2, imageHeight / 2, DrawSize);
                    if (hv_Row1.D != 0)
                    {
                        brushRegion.Dispose();
                        brushRegion = ho_temp_brush;
                    }
                    break;
                case eDrawShape.矩形:
                    HOperatorSet.GenRectangle1(out ho_temp_brush, 0, 0, DrawSize, DrawSize);
                    if (hv_Row1.D != 0)
                    {
                        brushRegion.Dispose();
                        brushRegion = ho_temp_brush;
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
            var view = ModuleView as BlobView;
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
                string color = "blue";
                //画出笔刷
                switch (editMode)
                {
                    case eEditMode.绘制涂抹:
                        color = "blue";
                        break;
                    case eEditMode.擦除涂抹:
                        color = "red";
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
                //显示
                view.mWindowH.HobjectToHimage(DispImage);
                view.mWindowH.DispObj(contour_xld, "green");
                if (finalRegion.IsInitialized())
                {
                    view.mWindowH.DispObj(finalRegion, color);
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
                    view.mWindowH.DispObj(contour_xld, "green");

                    if (finalRegion.IsInitialized())
                    {
                        view.mWindowH.DispObj(finalRegion, color);
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
                                        if (finalRegion.IsInitialized())
                                        {
                                            HObject ExpTmpOutVar_0;
                                            HOperatorSet.Union2(finalRegion, brush_region_affine, out ExpTmpOutVar_0);
                                            finalRegion.Dispose();
                                            finalRegion = new HRegion(ExpTmpOutVar_0);
                                        }
                                        else
                                        {
                                            finalRegion = new HRegion(brush_region_affine);
                                        }

                                    }
                                    break;
                                case eEditMode.擦除涂抹:
                                    {
                                        HObject ExpTmpOutVar_0;
                                        HOperatorSet.Difference(finalRegion, brush_region_affine, out ExpTmpOutVar_0);
                                        finalRegion.Dispose();
                                        finalRegion = new HRegion(ExpTmpOutVar_0);
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
                view.mWindowH.HobjectToHimage(DispImage);
                view.mWindowH.DispObj(finalRegion, "blue");
                view.mWindowH.DispObj(contour_xld, "green");
                view.mWindowH.DrawModel = false;
            }

        }
        private void ThresholdChanged() 
        {
            mWindowH.ClearROI();
            switch (SelectedROIType)
            {
                case eDrawShape.矩形:
                    ReduceRegion.GenRectangle2(Rectangle2Region.MidR, Rectangle2Region.MidC, -Rectangle2Region.Phi, Rectangle2Region.Length1, Rectangle2Region.Length2);
                    break;
                case eDrawShape.圆形:
                    ReduceRegion.GenCircle(CircleRegion.CenterY, CircleRegion.CenterX, CircleRegion.Radius);
                    break;
                case eDrawShape.输入图像:
                    ReduceRegion = DispImage.GetDomain();
                    ReduceRegion.GenCircle(CircleRegion.CenterY, CircleRegion.CenterX, CircleRegion.Radius);
                    break;
                default:
                    break;

            }
            if (!ReduceRegion.IsInitialized())
            {
                return;
            }
            HImage ImageRe = new HImage();
            if (finalRegion != null && finalRegion.IsInitialized())
            {
                ReduceRegion = ReduceRegion.Difference(finalRegion);
            }
            if (ChannelNum == 3)
            {
                switch (_SelectedImageChannel)
                {
                    case eChannels.R:
                        ImageRe = hImageR.ReduceDomain(ReduceRegion);
                        break;
                    case eChannels.G:
                        ImageRe = hImageG.ReduceDomain(ReduceRegion);
                        break;
                    case eChannels.B:
                        ImageRe = hImageB.ReduceDomain(ReduceRegion);
                        break;
                    default:
                        break;
                }
            }
            else if (ChannelNum == 1)
            {
                ImageRe = DispImage.ReduceDomain(ReduceRegion);
            }
            if (ImageRe.IsInitialized())
            {
                OutRegion = ImageRe.Threshold((double)_ThresholdMin, (double)_ThresholdMax);
                if (OutRegion.IsInitialized())
                {
                    Area = OutRegion.Area;
                    ShowRoi();
                }
            }
        }
        private void ImageChanged()
        {
            if (DispImage != null && DispImage.IsInitialized())
            {
                ChannelNum = DispImage.CountChannels().I;
                if (ChannelNum == 3)
                {
                    DecomposeImage();
                    IsThreeChannel = true;
                    switch (_SelectedImageChannel)
                    {
                        case eChannels.R:
                            ShowImageAndRoi(hImageR);
                            break;
                        case eChannels.G:
                            ShowImageAndRoi(hImageG);
                            break;
                        case eChannels.B:
                            ShowImageAndRoi(hImageB);
                            break;
                        default:
                            break;
                    }
                }
                else if (ChannelNum == 1)
                {
                    IsThreeChannel = false;
                    ShowImageAndRoi(DispImage);
                }
            }
        }

        #endregion
    }
}
