using ControlzEx.Standard;
using EventMgrLib;
using HalconDotNet;
using Plugin.CreatePoints.Model;
using Plugin.CreatePoints.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web.UI.WebControls;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Xml.Serialization;
using VM.Halcon;
using VM.Halcon.Config;
using VM.Halcon.Model;
using VM.Start.Attributes;
using VM.Start.Common;
using VM.Start.Common.Enums;
using VM.Start.Common.Extension;
using VM.Start.Common.Helper;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.Services;
using VM.Start.ViewModels;
using VM.Start.Views.Dock;

namespace Plugin.CreatePoints.ViewModels
{
    #region enum
    public enum eLinkCommand
    {
        InputImageLink,
        Length,
    }
    #endregion

    [Category("检测识别")]
    [DisplayName("创建点集")]
    [ModuleImageName("CreatePoints")]
    [Serializable]
    public class CreatePointsViewModel : ModuleBase
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

                        Aff.Affine2d(HomMat2D_Inverse, TempPoints[Index].PointX,
                            TempPoints[Index].PointY, out double X, out double Y);
                        Points[Index].PointX = Math.Round(X, 3);
                        Points[Index].PointY = Math.Round(Y, 3);
                    }
                    if (HomMat2D != null && HomMat2D.Length > 0)
                    {
                        for (int i = 0; i < Points.Count; i++)
                        {
                            Aff.Affine2d(HomMat2D, Points[i].PointX, Points[i].PointY,
                                out double X, out double Y);
                            TranPoints[i].PointX = Math.Round(X, 3);
                            TranPoints[i].PointY = Math.Round(Y, 3);
                        }

                    }
                    else
                    {
                        for (int i = 0; i < TempPoints.Count; i++)
                        {
                            Points[i].PointX =
                                TranPoints[i].PointX = TempPoints[i].PointX;
                            Points[i].PointY =
                                TranPoints[i].PointY = TempPoints[i].PointY;
                        }
                    }


                    HTuple RowList = new HTuple();
                    HTuple ColList = new HTuple();
                    for (int i = 0; i < TranPoints.Count; i++)
                    {
                        RowList[i] = TranPoints[i].PointX;
                        ColList[i] = TranPoints[i].PointY;
                    }

                    LengthLinkValue = Convert.ToDouble(GetLinkValue(LengthLinkText));
                    GenCross(out HObject _Cross, ColList, RowList, LengthLinkValue, 0, PointsShow[PointsShowSelectIndex]);
                    if (ShowResultPoints)
                    {
                        ShowHRoi(new HRoi(ModuleParam.ModuleEncode, ModuleParam.ModuleName, ModuleParam.Remarks, HRoiType.检测结果, _ColorLinkText.Remove(1, 2), new HObject(_Cross), true));
                    }
                    ShowHRoi();
                    ShowHRoi1();
                    if (ShowResultNum)
                    {
                        for (int i = 0; i < RowList.Length; i++)
                        {
                            var view = ModuleView as CreatePointsView;
                            HTuple hv_WindowHandle;
                            if (view == null || view.IsClosed)
                            {
                                hv_WindowHandle = ViewDic.GetView(DispViewID).hControl.HalconWindow;
                            }
                            else
                            {
                                hv_WindowHandle = view.mWindowH.hControl.HalconWindow;
                            }
                            ShowTool.SetFont(hv_WindowHandle, 20, "false", "false");
                            ShowTool.SetMsg(hv_WindowHandle, i.ToString(), "image", RowList[i], ColList[i], "red", "false");
                        }
                    }
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
            //增加输出前，先把原先字典里的输出清除
            if (Prj.OutputMap.ContainsKey(ModuleParam.ModuleName))
            {
                Prj.OutputMap.Remove(ModuleParam.ModuleName);
            }
            for (int i = 0; i < TranPoints.Count; i++)
            {
                AddOutputParam("输出点" + i.ToString() + "X", "double", TranPoints[i].PointX);
                AddOutputParam("输出点" + i.ToString() + "Y", "double", TranPoints[i].PointY);
            }
            AddOutputParam("状态", "bool", ModuleParam.Status == eRunStatus.OK ? true : false);
            AddOutputParam("时间", "int", ModuleParam.ElapsedTime);
        }
        #region Prop
        private bool DisenableAffine2d = false;
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

        private string _LengthLinkText = "60";
        /// <summary>
        /// 线长度链接文本
        /// </summary>
        public string LengthLinkText
        {
            get { return _LengthLinkText; }
            set { Set(ref _LengthLinkText, value); }
        }
        private double _LengthLinkValue;
        public double LengthLinkValue
        {
            get { return _LengthLinkValue; }
            set { _LengthLinkValue = value; }
        }

        private string _ColorLinkText = "#ffffff00";
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
        /// <summary>
        /// 变换前初始点集信息
        /// </summary>
        public ObservableCollection<PointsParamModel> Points { get; set; } = new ObservableCollection<PointsParamModel>();
        /// <summary>
        /// 变换后点集信息
        /// </summary>
        public ObservableCollection<PointsParamModel> TranPoints { get; set; } = new ObservableCollection<PointsParamModel>();
        /// <summary>
        /// 临时点集信息
        /// </summary>
        public ObservableCollection<PointsParamModel> TempPoints { get; set; } = new ObservableCollection<PointsParamModel>();

        private PointsParamModel _SelectedPoint = new PointsParamModel();
        /// <summary>
        /// 选中的文本
        /// </summary>
        public PointsParamModel SelectedPoint
        {
            get { return _SelectedPoint; }
            set { Set(ref _SelectedPoint, value); }
        }
        public List<string> PointsShow { get; set; } = new List<string>() { "十字线", "实心圆", "实心矩形", };

        private int _PointsShowSelectIndex;
        /// <summary>
        /// 点形态选择
        /// </summary>
        public int PointsShowSelectIndex
        {
            get { return _PointsShowSelectIndex; }
            set { Set(ref _PointsShowSelectIndex, value); }
        }
        private bool _ShowResultPoints = true;

        public bool ShowResultPoints
        {
            get { return _ShowResultPoints; }
            set { Set(ref _ShowResultPoints, value); }
        }

        private bool _ShowResultNum = true;

        public bool ShowResultNum
        {
            get { return _ShowResultNum; }
            set { Set(ref _ShowResultNum, value); }
        }

        /// <summary> 区域列表 </summary>
        public Dictionary<string, ROI> RoiList = new Dictionary<string, ROI>();

        #endregion
        #region Command
        public override void Loaded()
        {
            base.Loaded();

            var view = ModuleView as CreatePointsView;
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
                    view.mWindowH.hControl.MouseMove += HControl_MouseMove;
                    view.mWindowH.hControl.MouseDown += HControl_MouseDown;
                    if (Points.Count > 0)
                    {
                        for (int i = 0; i < Points.Count; i++)
                        {
                            InitPointMethod(i);
                        }
                    }
                }

            }
            ExeModule();
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
                        var view = this.ModuleView as CreatePointsView;
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
                case "Length":
                    LengthLinkText = obj.LinkName;
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
                            case eLinkCommand.Length:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},Length");
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
        [NonSerialized]
        private CommandBase _DataOperateCommand;
        public CommandBase DataOperateCommand
        {
            get
            {
                if (_DataOperateCommand == null)
                {
                    _DataOperateCommand = new CommandBase((obj) =>
                    {
                        switch (obj)
                        {
                            case "Add":
                                if (InputImageLinkText == null) return;
                                TranPoints.Add(new PointsParamModel()
                                    {
                                        ID = TranPoints.Count,
                                    });
                                    InitPointMethod(TranPoints.Count - 1);
                                break;
                            case "Delete":
                                if (SelectedPoint == null) return;
                                if (TranPoints.Count == 0) return;
                                DeletePoint();
                                break;
                            case "DeleteAll":
                                Points.Clear();
                                TranPoints.Clear();
                                TempPoints.Clear();
                                RoiList.Clear();
                                ShowHRoi();
                                ShowHRoi1();
                                break;
                            default:
                                break;
                        }
                    });
                }
                return _DataOperateCommand;
            }
        }
        #endregion
        #region Method

        private void InitPointMethod(int ID)
        {
            var view = ModuleView as CreatePointsView;
            if (view == null)
            {
                return;
            }
            if (DispImage != null && !RoiList.ContainsKey(ID.ToString()))
            {
                view.mWindowH.WindowH.genPoint(ID.ToString(), TranPoints[ID].PointY, TranPoints[ID].PointX, ref RoiList);
                Points.Add(new PointsParamModel() { ID = ID, PointX = TranPoints[ID].PointX, PointY = TranPoints[ID].PointY });
                TempPoints.Add(new PointsParamModel() { ID = ID, PointX = TranPoints[ID].PointX, PointY = TranPoints[ID].PointY });
            }
            GetHomMat2D();
            if (HomMat2D_Inverse != null && HomMat2D_Inverse.Length > 0)
            {
                for (int i = 0; i < TranPoints.Count; i++)
                {
                    view.mWindowH.WindowH.genPoint(i.ToString(), TranPoints[i].PointY, TranPoints[i].PointX,
                        ref RoiList);

                    Aff.Affine2d(HomMat2D_Inverse, TranPoints[i].PointX, TranPoints[i].PointY,
                        out double X, out double Y);
                    if (Points.Count < ID + 1)
                    {
                        Points.Add(new PointsParamModel() { ID = ID, PointX = X, PointY = Y });
                    }
                    else
                    {
                        Points[i].PointX = Math.Round(X, 3);
                        Points[i].PointY = Math.Round(Y, 3);
                    }
                }
            }
            else
            {
                for (int i = 0; i < Points.Count; i++)
                {
                    view.mWindowH.WindowH.genPoint(i.ToString(), Points[i].PointY, Points[i].PointX, ref RoiList);
                }
            }
            ShowHRoi1();
        }

        private void DeletePoint()
        {
            int selectID = Convert.ToInt32(SelectedPoint.ID.ToString());
            var view = ModuleView as CreatePointsView;
            RoiList.Remove((SelectedPoint.ID.ToString()));

            TranPoints.Remove(TranPoints[selectID]);
            for (int i = 0; i < TranPoints.Count; i++)
            {
                TranPoints[i].ID = i;
            }
            Points.Remove(Points[selectID]);
            for (int i = 0; i < Points.Count; i++)
            {
                Points[i].ID = i;
            }
            if (TempPoints.Count > 0)
            {
                TempPoints.Remove(TempPoints[selectID]);
                for (int i = 0; i < TempPoints.Count; i++)
                {
                    TempPoints[i].ID = i;
                }
            }
            ShowHRoi();
            ShowHRoi1();
            ////ExeModule();
        }
        /// <summary>
        /// 用于删除点集后，显示剩余点
        /// </summary>
        public void ShowHRoi1()
        {
            ClearRoiAndText();
            //删除点后，刷新下RoiList
            var view = ModuleView as CreatePointsView;
            if (view == null) return;
            RoiList.Clear();

            HTuple RowList = new HTuple();
            HTuple ColList = new HTuple();
            HTuple hv_WindowHandle;
            hv_WindowHandle = view.mWindowH.hControl.HalconWindow;
            for (int i = 0; i < TranPoints.Count; i++)
            {
                view.mWindowH.WindowH.genPoint(i.ToString(), TranPoints[i].PointY, TranPoints[i].PointX, ref RoiList);
                RowList[i] = TranPoints[i].PointX;
                ColList[i] = TranPoints[i].PointY;
            }
            if (ShowResultNum)
            {
                for (int i = 0; i < TranPoints.Count; i++)
                {
                    ShowTool.SetFont(hv_WindowHandle, 20, "false", "false");
                    ShowTool.SetMsg(hv_WindowHandle, i.ToString(), "image", RowList[i], ColList[i], "red", "false");
                }
            }
            //删除完最后一个刷新下图片
            if (TranPoints.Count == 0)
            {
                GetDispImage(InputImageLinkText, true);
            }
        }
        ROIPoint roiPoint = new ROIPoint();
        private void HControl_MouseUp(object sender, MouseEventArgs e)
        {
            try
            {
                IsMouseDown = false;
                var view = ModuleView as CreatePointsView;
                if (view == null) return;

                ROI roi = view.mWindowH.WindowH.smallestActiveROI(out string info, out string index);

                if (index.Length > 0)
                {
                    roiPoint = roi as ROIPoint;
                    if (roiPoint != null)
                    {
                        if (TempPoints.Count < Convert.ToInt32(index) + 1)
                        {
                            TempPoints.Add(new PointsParamModel() { ID = Index, PointX = Math.Round(roiPoint.midC, 3), PointY = Math.Round(roiPoint.midR, 3) });
                        }
                        else
                        {
                            TempPoints[Index].PointY = Math.Round(roiPoint.midR, 3);
                            TempPoints[Index].PointX = Math.Round(roiPoint.midC, 3);
                        }
                        DisenableAffine2d = true;

                        ExeModule();
                        ShowHRoi1();

                        //InitPointMethod(Index);
                    }
                }
            }
            catch (Exception ex)
            {
            }
        }
        /// <summary>
        /// 记录鼠标是否按下
        /// </summary>
        bool IsMouseDown = false;
        /// <summary>
        /// 记录选择点的编号
        /// </summary>
        int Index = 0;
        private void HControl_MouseDown(object sender, MouseEventArgs e)
        {
            var view = ModuleView as CreatePointsView;
            if (view == null) return;
            ROI roi = view.mWindowH.WindowH.smallestActiveROI(out string info, out string IndexS);
            if (IndexS.Length > 0)
            {
                roiPoint = roi as ROIPoint;
                IsMouseDown = true;
                Index = Convert.ToInt32(IndexS);
            }
        }

        private void HControl_MouseMove(object sender, MouseEventArgs e)
        {
            if (IsMouseDown)
            {
                var view = ModuleView as CreatePointsView;
                if (view == null) return;

                ROI roi = view.mWindowH.WindowH.smallestActiveROI(out string info, out string index);

                if (index.Length > 0)
                {
                    roiPoint = roi as ROIPoint;
                    if (roiPoint != null)
                    {
                        if (TempPoints.Count < Convert.ToInt32(index) + 1)
                        {
                            TempPoints.Add(new PointsParamModel() { ID = Index, PointX = Math.Round(roiPoint.midC, 3), PointY = Math.Round(roiPoint.midR, 3) });
                        }
                        else
                        {
                            TempPoints[Index].PointY = Math.Round(roiPoint.midR, 3);
                            TempPoints[Index].PointX = Math.Round(roiPoint.midC, 3);
                        }
                        GetHomMat2D();
                        if (HomMat2D_Inverse != null && HomMat2D_Inverse.Length > 0)
                        {

                            Aff.Affine2d(HomMat2D_Inverse, TempPoints[Index].PointX,
                                TempPoints[Index].PointY, out double X, out double Y);
                            Points[Index].PointX = Math.Round(X, 3);
                            Points[Index].PointY = Math.Round(Y, 3);
                        }
                        if (HomMat2D != null && HomMat2D.Length > 0)
                        {
                            for (int i = 0; i < Points.Count; i++)
                            {
                                Aff.Affine2d(HomMat2D, Points[i].PointX, Points[i].PointY,
                                    out double X, out double Y);
                                TranPoints[i].PointX = Math.Round(X, 3);
                                TranPoints[i].PointY = Math.Round(Y, 3);
                            }

                        }
                        else
                        {
                            for (int i = 0; i < TempPoints.Count; i++)
                            {
                                Points[i].PointX =
                                    TranPoints[i].PointX = TempPoints[i].PointX;
                                Points[i].PointY =
                                    TranPoints[i].PointY = TempPoints[i].PointY;
                            }
                        }


                        HTuple RowList = new HTuple();
                        HTuple ColList = new HTuple();
                        for (int i = 0; i < TranPoints.Count; i++)
                        {
                            RowList[i] = TranPoints[i].PointX;
                            ColList[i] = TranPoints[i].PointY;
                        }

                        LengthLinkValue = Convert.ToDouble(GetLinkValue(LengthLinkText));
                        Gen.GenCross(out HObject _Cross, ColList, RowList, LengthLinkValue, 0);

                        if (ShowResultNum)
                        {
                            for (int i = 0; i < RowList.Length; i++)
                            {
                                HTuple hv_WindowHandle;
                                if (view == null || view.IsClosed)
                                {
                                    hv_WindowHandle = ViewDic.GetView(DispViewID).hControl.HalconWindow;
                                }
                                else
                                {
                                    hv_WindowHandle = view.mWindowH.hControl.HalconWindow;
                                }
                                ShowTool.SetFont(hv_WindowHandle, 20, "false", "false");
                                ShowTool.SetMsg(hv_WindowHandle, i.ToString(), "image", RowList[i], ColList[i], "red", "false");
                            }
                        }

                    }
                }
            }
        }
        private void GenCross(out HObject MeasCross, HTuple RowList, HTuple ColList, HTuple size, HTuple angle, string type)
        {
            switch (type)
            {
                case "十字线":
                    HOperatorSet.GenCrossContourXld(out MeasCross, RowList, ColList, size, angle);
                    break;
                case "实心圆":
                    HObject UCircle;
                    HOperatorSet.GenEmptyObj(out UCircle);
                    for (int i = 0; i < RowList.Length; i++)
                    {
                        HOperatorSet.GenCircle(out HObject Circle, RowList[i], ColList[i], size);
                        HOperatorSet.Union2(Circle, UCircle, out UCircle);
                    }
                    HOperatorSet.Union1(UCircle, out MeasCross);
                    break;
                case "实心矩形":
                    HObject URect2;
                    HOperatorSet.GenEmptyObj(out URect2);
                    for (int i = 0; i < RowList.Length; i++)
                    {
                        HOperatorSet.GenRectangle2(out HObject Rect2, RowList[i], ColList[i], angle, size, size);
                        HOperatorSet.Union2(Rect2, URect2, out URect2);
                    }
                    HOperatorSet.Union1(URect2, out MeasCross);
                    break;
                default:
                    HOperatorSet.GenCrossContourXld(out MeasCross, RowList, ColList, size, angle);
                    break;
            }

        }


        #endregion
    }
}
