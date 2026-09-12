using EventMgrLib;
using HalconDotNet;
using Plugin.FitCircle.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.UI.WebControls;
using System.Windows;
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
using VM.Start.Dialogs.Views;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.ViewModels;
using VM.Start.Views.Dock;

namespace Plugin.FitCircle.ViewModels
{
    #region enum
    public enum eLinkCommand
    {
        InputImageLinkText,
        LinkX,
        LinkY
    }
    #endregion

    [Category("几何关系")]
    [DisplayName("拟合圆形")]
    [ModuleImageName("FitCircle")]
    [Serializable]
    public class FitCircleViewModel : ModuleBase
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
                DispImage.mHRoi.Clear();
                if (InputImageLinkText == null || InputImageLinkText == "")
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
                if (CoordinateXY.Count < 3)
                {
                    var messageView1 = MessageView.Ins;
                    messageView1.MessageBoxShow("坐标点需要3个以上", eMsgType.Warn, MessageBoxButton.OK);
                    ChangeModuleRunStatus(eRunStatus.NG);
                    return false;
                }
                double[] X = new double[CoordinateXY.Count];
                double[] Y = new double[CoordinateXY.Count];
                HTuple r = 45;
                for (int i = 0; i < CoordinateXY.Count; i++)
                {
                    X[i] = Convert.ToDouble(GetLinkValue(CoordinateXY[i].LinkX));
                    Y[i] = Convert.ToDouble(GetLinkValue(CoordinateXY[i].LinkY));
               
                }
                if (ShowCoordinateXY)
                {
                    HOperatorSet.GenCrossContourXld(out HObject Cross, Y, X, 6, r.TupleRad());
                    DispImage.ShowHRoi(new HRoi(ModuleParam.ModuleEncode, ModuleParam.ModuleName, ModuleParam.Remarks, HRoiType.参考坐标, "green", new HObject(Cross)));
                }
                r = 360;
                HOperatorSet.GenContourPolygonXld(out HObject Contour, Y, X);
                HOperatorSet.FitCircleContourXld(Contour, "algebraic", -1, 0, 0, 3, 2, out HTuple hv_Row, out HTuple hv_Column, out HTuple hv_Radius
                    , out HTuple hv_StartPhi, out HTuple hv_EndPhi, out HTuple PointOrder);
                ResultCircleX = Math.Round(hv_Column.D, 5);
                ResultCircleY = Math.Round(hv_Row.D, 5);
                ResultCircleR = Math.Round(hv_Radius.D, 5);
                HOperatorSet.GenCircleContourXld(out HObject ContCircle, hv_Row, hv_Column, hv_Radius, 0, r.TupleRad(), "positive", 1);
                if (ShowResultCircle)
                    DispImage.ShowHRoi(new HRoi(ModuleParam.ModuleEncode, ModuleParam.ModuleName, ModuleParam.Remarks, HRoiType.检测结果, "green", new HObject(ContCircle)));
                if (ShowCenter)
                {
                    r = 45;
                    HOperatorSet.GenCrossContourXld(out HObject Center, ResultCircleY, ResultCircleX, 6, r.TupleRad());
                    ShowHRoi(new HRoi(ModuleParam.ModuleEncode, ModuleParam.ModuleName, ModuleParam.Remarks, HRoiType.检测中心, "green", new HObject(Center)));
                }
                ShowHRoi();
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
            if (DispImage == null)
                DispImage = new RImage();
            if (!DispImage.IsInitialized())
                base.AddOutputParams();
            AddOutputParam("中心X", "double", ResultCircleX);
            AddOutputParam("中心Y", "double", ResultCircleY);
            AddOutputParam("中心R", "double", ResultCircleR);
        }
        #region Prop
        private string _InputImageLinkText;
        /// <summary>
        /// 输入图像链接文本
        /// </summary>
        public string InputImageLinkText
        {
            get { return _InputImageLinkText; }
            set { Set(ref _InputImageLinkText, value); }
        }

        private ObservableCollection<CoordinateParams> _CoordinateXY = new ObservableCollection<CoordinateParams>();
        /// <summary>
        /// 定义图像参数
        /// </summary>
        public ObservableCollection<CoordinateParams> CoordinateXY
        {
            get { return _CoordinateXY; }
            set { _CoordinateXY = value; RaisePropertyChanged(); }
        }

        private int _nSelectIndex;
        public int nSelectIndex
        {
            get { return _nSelectIndex; }
            set { Set(ref _nSelectIndex, value); }
        }

        private double _ResultCircleX;
        public double ResultCircleX
        {
            get { return _ResultCircleX; }
            set
            {
                Set(ref _ResultCircleX, value);
            }
        }
        private double _ResultCircleY;
        public double ResultCircleY
        {
            get { return _ResultCircleY; }
            set
            {
                Set(ref _ResultCircleY, value);
            }
        }
        private double _ResultCircleR;
        public double ResultCircleR
        {
            get { return _ResultCircleR; }
            set
            {
                Set(ref _ResultCircleR, value);
            }
        }
        //
        private bool _ShowCoordinateXY = true;
        public bool ShowCoordinateXY
        {
            get { return _ShowCoordinateXY; }
            set { Set(ref _ShowCoordinateXY, value); }
        }
        private bool _ShowResultCircle = true;
        public bool ShowResultCircle
        {
            get { return _ShowResultCircle; }
            set { Set(ref _ShowResultCircle, value); }
        }
        private bool _ShowCenter = true;
        public bool ShowCenter
        {
            get { return _ShowCenter; }
            set
            {
                Set(ref _ShowCenter, value);
            }
        }
        private bool _ShowOkLog;
        /// <summary>
        /// 显示OK日志
        /// </summary>
        public bool ShowOkLog
        {
            get { return _ShowOkLog; }
            set
            {
                Set(ref _ShowOkLog, value);
            }
        }
        private bool _ShowNgLog;
        /// <summary>
        /// 显示NG日志
        /// </summary>
        public bool ShowNgLog
        {
            get { return _ShowNgLog; }
            set
            {
                Set(ref _ShowNgLog, value);
            }
        }
        #endregion

        #region Command
        public override void Loaded()
        {
            base.Loaded();
            var view = ModuleView as FitCircleView;
            if (view != null)
            {
                ClosedView = true;
                if (view.mWindowH == null)
                {
                    view.mWindowH = new VMHWindowControl();
                    view.winFormHost.Child = view.mWindowH;
                }
                if (InputImageLinkText == null || InputImageLinkText == "")
                    SetDefaultLink();
                if (InputImageLinkText == null || InputImageLinkText == "") return;
                GetDispImage(InputImageLinkText);
                if (DispImage != null && DispImage.IsInitialized())
                {
                    view.mWindowH.HobjectToHimage(DispImage);
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
                        var view = this.ModuleView as FitCircleView;
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
                case "LinkX":
                    CoordinateXY[nSelectIndex].LinkX.Text = obj.LinkName;
                    break;
                case "LinkY":
                    CoordinateXY[nSelectIndex].LinkY.Text = obj.LinkName;
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
                            case eLinkCommand.LinkX:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "int,double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},LinkX");
                                break;
                            case eLinkCommand.LinkY:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "int,double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},LinkY");
                                break;
                            case eLinkCommand.InputImageLinkText:
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
                                CoordinateXY.Add(new CoordinateParams()
                                {
                                    Index = CoordinateXY.Count,
                                    LinkCommand = LinkCommand
                                });
                                break;
                            case "Delete":
                                if (nSelectIndex < 0) return;
                                CoordinateXY.RemoveAt(nSelectIndex);
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
        private void ShowHRoi()
        {
            var view = ModuleView as FitCircleView;
            VMHWindowControl mWindowH;
            if (view == null || view.IsClosed)
            {
                mWindowH = ViewDic.GetView(DispImage.DispViewID);
            }
            else
            {
                mWindowH = view.mWindowH;
                mWindowH.HobjectToHimage(DispImage);
            }
            List<HRoi> roiList = DispImage.mHRoi.Where(c => c.ModuleName == ModuleParam.ModuleName && c.ModuleEncode == ModuleParam.ModuleEncode).ToList();
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
                    mWindowH.WindowH.DispHobject(roi.hobject, roi.drawColor);
                }
            }
        }
        #endregion

    }
    [Serializable]
    public class CoordinateParams : NotifyPropertyBase
    {
        public int Index { get; set; }
        private LinkVarModel _LinkX = new LinkVarModel();
        public LinkVarModel LinkX
        {
            get { return _LinkX; }
            set { _LinkX = value; RaisePropertyChanged(); }
        }
        private LinkVarModel _LinkY = new LinkVarModel();
        public LinkVarModel LinkY
        {
            get { return _LinkY; }
            set { _LinkY = value; RaisePropertyChanged(); }
        }
        public CommandBase LinkCommand { get; set; }
    }
}
