using DMSkin.Socket;
using EventMgrLib;
using HalconDotNet;
using HandyControl.Controls;
using Plugin.GrabImage.Model;
using Plugin.Matching.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.ModelBinding;
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
using VM.Start.Dialogs.Views;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.Services;
using VM.Start.ViewModels;
using VM.Start.Views.Dock;

namespace Plugin.Matching.ViewModels
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
        圆形,
        矩形,
    }

    #endregion

    [Category("检测识别")]
    [DisplayName("模板匹配")]
    [ModuleImageName("Matching")]
    [Serializable]
    public class MatchingViewModel : ModuleBase
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
                    Logger.AddLog(
                        $"流程[{Solution.Ins.GetProjectById(ModuleParam.ProjectID).ProjectInfo.ProcessName}]执行[{ModuleParam.ModuleName}]模块失败，未链接图像源！",
                        eMsgType.Warn
                    );
                    ChangeModuleRunStatus(eRunStatus.NG);
                    return false;
                }
                if (!ModelImage.IsInitialized())
                {
                    Logger.AddLog(
                        $"流程[{Solution.Ins.GetProjectById(ModuleParam.ProjectID).ProjectInfo.ProcessName}]执行[{ModuleParam.ModuleName}]模块失败，模版句柄为空！",
                        eMsgType.Warn
                    );
                    ChangeModuleRunStatus(eRunStatus.NG);
                    return false;
                }
                GetDispImage(InputImageLinkText);
                if (DispImage == null || !DispImage.IsInitialized())
                {
                    ChangeModuleRunStatus(eRunStatus.NG);
                    return false;
                }
                ROI ModelSearch = RoiList[ModuleParam.ModuleName + ROIDefine.Search];
                ROI ModelTemplet = RoiList[ModuleParam.ModuleName + ROIDefine.Templet];
                int mathNum = int.Parse(GetLinkValue(MathNum).ToString());
                HImage imageReduce = new HImage(DispImage.ReduceDomain(ModelSearch.GetRegion()));
                if (
                    Find.FindModel(
                        ModelType,
                        imageReduce,
                        ModelImage,
                        MinScore,
                        mathNum,
                        MaxOverlap,
                        GreedDeg,
                        out MathCoord
                    ) > 0
                )
                {
                    //仿射变换-检测结果
                    HTuple tempMat2D = new HTuple();
                    HOperatorSet.VectorAngleToRigid(
                        0,
                        0,
                        0,
                        MathCoord.Y,
                        MathCoord.X,
                        MathCoord.Phi,
                        out tempMat2D
                    );
                    //检测结果-对XLD应用任意加法 2D 变换
                    HXLDCont contour_xld = ((HShapeModel)ModelImage)
                        .GetShapeModelContours(1)
                        .AffineTransContourXld(new HHomMat2D(tempMat2D));
                    //检测中心-为输入点生成一个十字形状的 XLD 轮廓
                    HOperatorSet.GenCrossContourXld(
                        out HObject cross,
                        MathCoord.Y,
                        MathCoord.X,
                        10,
                        MathCoord.Phi
                    );
                    //ROI显示
                    if (ShowSearchRegion && ModuleView == null)
                    {
                        ShowHRoi(
                            new HRoi(
                                ModuleParam.ModuleEncode,
                                ModuleParam.ModuleName,
                                ModuleParam.Remarks,
                                HRoiType.搜索范围,
                                "blue",
                                new HObject(ModelSearch.GetRegion())
                            )
                        );
                    }
                    if (ShowResultContour)
                    {
                        ShowHRoi(
                            new HRoi(
                                ModuleParam.ModuleEncode,
                                ModuleParam.ModuleName,
                                ModuleParam.Remarks,
                                HRoiType.参考坐标,
                                "red",
                                new HObject(Gen.GetCoord(DispImage, MathCoord))
                            )
                        );
                        ShowHRoi(
                            new HRoi(
                                ModuleParam.ModuleEncode,
                                ModuleParam.ModuleName,
                                ModuleParam.Remarks,
                                HRoiType.检测中心,
                                "cyan",
                                new HObject(cross)
                            )
                        );
                        ShowHRoi(
                            new HRoi(
                                ModuleParam.ModuleEncode,
                                ModuleParam.ModuleName,
                                ModuleParam.Remarks,
                                HRoiType.检测结果,
                                "green",
                                new HObject(contour_xld)
                            )
                        );
                    }
                    MathCoord.Phi = MathCoord.Phi - ModeCoord.Phi;
                    MathCoord.Status = true;
                    ShowHRoi();
                    ChangeModuleRunStatus(eRunStatus.OK);
                    if (ModuleView != null)
                    {
                        CommonMethods.UIAsync(() =>
                        {
                            MathTemplateModels.Clear();
                            MathTemplateModels.Add(
                                new MathTemplateModel()
                                {
                                    ID = MathTemplateModels.Count + 1,
                                    X = MathCoord.X,
                                    Y = MathCoord.Y,
                                    Deg = MathCoord.Phi,
                                    Score = MathCoord.Score
                                }
                            );
                        });
                    }
                    return true;
                }
                else
                {
                    MathCoord.Status = false;
                    DispImage.mHRoi.Clear();
                    ChangeModuleRunStatus(eRunStatus.NG);
                    return false;
                }
            }
            catch (Exception ex)
            {
                ChangeModuleRunStatus(eRunStatus.NG);
                //Logger.GetExceptionMsg(ex);
                return false;
            }
        }

        public override void AddOutputParams()
        {
            base.AddOutputParams();
            AddOutputParam("X", "double", MathCoord.X);
            AddOutputParam("Y", "double", MathCoord.Y);
            AddOutputParam("Deg", "double", MathCoord.Phi);
            AddOutputParam("分数", "double", MathCoord.Score);
        }

        #region Prop
        private EditViewModel _editViewModel;

        /// <summary>
        /// 编辑模版Model
        /// </summary>
        public EditViewModel editViewModel
        {
            get
            {
                if (_editViewModel == null)
                {
                    _editViewModel = new EditViewModel();
                }
                return _editViewModel;
            }
            set { Set(ref _editViewModel, value); }
        }

        [NonSerialized]
        private bool _IsStudying = false;

        /// <summary>
        /// 学习中
        /// </summary>
        public bool IsStudying
        {
            get { return _IsStudying; }
            set { Set(ref _IsStudying, value); }
        }
        private bool _ShowSearchRegion = true;

        /// <summary>
        /// 显示搜索区域
        /// </summary>
        public bool ShowSearchRegion
        {
            get { return _ShowSearchRegion; }
            set { Set(ref _ShowSearchRegion, value); }
        }
        private bool _ShowResultContour = true;

        /// <summary>
        /// 显示结果轮廓
        /// </summary>
        public bool ShowResultContour
        {
            get { return _ShowResultContour; }
            set { Set(ref _ShowResultContour, value); }
        }

        /// <summary> 模板图像 </summary>
        public HHandle ModelImage;

        /// <summary> 区域列表 </summary>
        public Dictionary<string, ROI> RoiList = new Dictionary<string, ROI>();
        private eSearchRegion _SearchRegionSource = eSearchRegion.矩形1;

        /// <summary>
        /// 搜索区域源
        /// </summary>
        public eSearchRegion SearchRegionSource
        {
            get { return _SearchRegionSource; }
            set { Set(ref _SearchRegionSource, value); }
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
        private Rectangle1Model _Rectangle1SearchRegion;

        /// <summary>
        /// 输入图像链接文本
        /// </summary>
        public Rectangle1Model Rectangle1SearchRegion
        {
            get
            {
                if (_Rectangle1SearchRegion == null)
                {
                    _Rectangle1SearchRegion = new Rectangle1Model();
                }
                return _Rectangle1SearchRegion;
            }
            set { Set(ref _Rectangle1SearchRegion, value); }
        }
        private eModelType _ModelType;

        /// <summary>
        /// 模板类型
        /// </summary>
        public eModelType ModelType
        {
            get { return _ModelType; }
            set { Set(ref _ModelType, value); }
        }
        private int _Levels = 0;

        /// <summary>
        /// 金字塔层数
        /// </summary>
        public int Levels
        {
            get { return _Levels; }
            set { Set(ref _Levels, value); }
        }
        private LinkVarModel _MathNum = new LinkVarModel() { Value = 1 };

        /// <summary>
        /// 匹配个数
        /// </summary>
        public LinkVarModel MathNum
        {
            get { return _MathNum; }
            set { Set(ref _MathNum, value); }
        }
        private double _MaxOverlap = 0.5;

        /// <summary>
        /// 最大重叠
        /// </summary>
        public double MaxOverlap
        {
            get { return _MaxOverlap; }
            set { Set(ref _MaxOverlap, value); }
        }
        private double _GreedDeg = 0.9;

        /// <summary>
        /// 贪心算法
        /// </summary>
        public double GreedDeg
        {
            get { return _GreedDeg; }
            set { Set(ref _GreedDeg, value); }
        }

        /// <summary>
        /// 修改坐标
        /// </summary>
        public Coord_Info ChangeCoord = new Coord_Info();
        private double _MinScore = 0.5;

        /// <summary>
        /// 最小分数
        /// </summary>
        public double MinScore
        {
            get { return _MinScore; }
            set { Set(ref _MinScore, value); }
        }

        [NonSerialized]
        private ObservableCollection<MathTemplateModel> _MathTemplateModels;

        public ObservableCollection<MathTemplateModel> MathTemplateModels
        {
            get
            {
                if (_MathTemplateModels == null)
                {
                    _MathTemplateModels = new ObservableCollection<MathTemplateModel>();
                }
                return _MathTemplateModels;
            }
            set { _MathTemplateModels = value; }
        }

        [NonSerialized]
        public HXLDCont contour_xld; //无法反序列化！！！
        public HObject OutImage;
        #endregion

        #region Command
        public override void Loaded()
        {
            base.Loaded();
            var view = ModuleView as MatchingView;
            if (view != null)
            {
                ClosedView = true;
                if (view.mWindowH == null)
                {
                    view.mWindowH = new VMHWindowControl();
                    view.winFormHost.Child = view.mWindowH;
                }
                if (view.mWindowH_Template == null)
                {
                    view.mWindowH_Template = new VMHWindowControl();
                    view.winFormHost1.Child = view.mWindowH_Template;
                }
                if (DispImage == null || !DispImage.IsInitialized())
                {
                    SetDefaultLink();
                    if (InputImageLinkText == null)
                        return;
                }
                GetDispImage(InputImageLinkText);
                view.mWindowH.DispObj(DispImage);
                if (DispImage != null && DispImage.IsInitialized())
                {
                    view.mWindowH.hControl.MouseUp += HControl_MouseUp;
                    view.mWindowH.hControl.MouseMove += HControl_MouseMove;
                    view.mWindowH.hControl.MouseWheel += HControl_MouseWheel;
                    ShowHRoi();
                    ShowHText();
                    if (OutImage != null && OutImage.IsInitialized())
                    {
                        view.mWindowH_Template.HobjectToHimage(OutImage);
                    }
                    if (contour_xld != null && contour_xld.IsInitialized())
                    {
                        view.mWindowH_Template.WindowH.DispHobject(contour_xld, "green");
                    }
                    //ShowTemp();
                }
            }
        }

        [NonSerialized]
        private CommandBase _OperateCommand;
        public CommandBase OperateCommand
        {
            get
            {
                if (_OperateCommand == null)
                {
                    _OperateCommand = new CommandBase(
                        (obj) =>
                        {
                            try
                            {
                                eOperateCommand par = (eOperateCommand)obj;
                                switch (par)
                                {
                                    case eOperateCommand.StartLearn:
                                        IsStudying = true;
                                        if (DispImage == null || !DispImage.IsInitialized())
                                        {
                                            MessageView.Ins.MessageBoxShow("图像不能为空！");
                                            return;
                                        }
                                        var view = ModuleView as MatchingView;
                                        if (view == null)
                                            return;
                                        view.mWindowH.HobjectToHimage(DispImage);
                                        if (
                                            RoiList.ContainsKey(
                                                ModuleParam.ModuleName + ROIDefine.Templet
                                            )
                                        )
                                        {
                                            ROIRectangle1 ROIRect1 = (ROIRectangle1)
                                                RoiList[ModuleParam.ModuleName + ROIDefine.Templet];
                                            view.mWindowH.WindowH.genRect1(
                                                ModuleParam.ModuleName + ROIDefine.Templet,
                                                ROIRect1.row1,
                                                ROIRect1.col1,
                                                ROIRect1.row2,
                                                ROIRect1.col2,
                                                ref RoiList
                                            );
                                        }
                                        else
                                        {
                                            view.mWindowH.WindowH.genRect1(
                                                ModuleParam.ModuleName + ROIDefine.Templet,
                                                view.mWindowH.hv_imageHeight / 2,
                                                view.mWindowH.hv_imageWidth / 2,
                                                view.mWindowH.hv_imageHeight / 2 + 150,
                                                view.mWindowH.hv_imageWidth / 2 + 150,
                                                ref RoiList
                                            );
                                        }
                                        break;
                                    case eOperateCommand.Edit:
                                        EditView editView = new EditView();
                                        editView.DataContext = editViewModel;
                                        editViewModel.view = editView;
                                        editViewModel.contour_xld = contour_xld;
                                        editViewModel.OutImage = OutImage;
                                        editViewModel.matchingViewModel = this;
                                        editView.ShowDialog();
                                        break;
                                    case eOperateCommand.EndLearn:
                                        try
                                        {
                                            IsStudying = false;
                                            switch (ModelType)
                                            {
                                                case eModelType.形状模板:
                                                    ModelImage = new HShapeModel();
                                                    break;
                                                case eModelType.灰度模板:
                                                    ModelImage = new HNCCModel();
                                                    break;
                                                default:
                                                    break;
                                            }
                                            CreateModel();
                                            if (ModelImage.IsInitialized())
                                            {
                                                ExeModule();
                                                ShowTemp();
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            Logger.GetExceptionMsg(ex);
                                        }
                                        break;
                                    case eOperateCommand.Cancel:
                                        IsStudying = false;
                                        break;
                                    default:
                                        throw new ArgumentOutOfRangeException();
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.GetExceptionMsg(ex);
                            }
                        }
                    );
                }
                return _OperateCommand;
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
                    _ExecuteCommand = new CommandBase(
                        (obj) =>
                        {
                            ExeModule();
                        }
                    );
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
                    _ConfirmCommand = new CommandBase(
                        (obj) =>
                        {
                            var view = this.ModuleView as MatchingView;
                            if (view != null)
                            {
                                view.Close();
                            }
                        }
                    );
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
                case "MathNumLink":
                    MathNum.Text = obj.LinkName;
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
                    EventMgr.Ins
                        .GetEvent<VarChangedEvent>()
                        .Subscribe(OnVarChanged, o => o.SendName.StartsWith($"{ModuleGuid}"));
                    _LinkCommand = new CommandBase(
                        (obj) =>
                        {
                            eLinkCommand linkCommand = (eLinkCommand)obj;
                            switch (linkCommand)
                            {
                                case eLinkCommand.InputImageLink:
                                    CommonMethods.GetModuleList(
                                        ModuleParam,
                                        VarLinkViewModel.Ins.Modules,
                                        "HImage"
                                    );
                                    EventMgr.Ins
                                        .GetEvent<OpenVarLinkViewEvent>()
                                        .Publish($"{ModuleGuid},InputImageLink");
                                    break;
                                case eLinkCommand.MathNum:
                                    CommonMethods.GetModuleList(
                                        ModuleParam,
                                        VarLinkViewModel.Ins.Modules,
                                        "int"
                                    );
                                    EventMgr.Ins
                                        .GetEvent<OpenVarLinkViewEvent>()
                                        .Publish($"{ModuleGuid},MathNumLink");
                                    break;
                                default:
                                    break;
                            }
                        }
                    );
                }
                return _LinkCommand;
            }
        }

        #endregion

        #region Method
        private void HControl_MouseUp(object sender, MouseEventArgs e)
        {
            try
            {
                var view = ModuleView as MatchingView;
                if (view == null)
                    return;
                ;
                ROI roi = view.mWindowH.WindowH.smallestActiveROI(
                    out string info,
                    out string index
                );
                if (index.Length < 1)
                    return;
                RoiList[index] = roi;
                ShowHText();
            }
            catch (Exception ex) { }
        }

        private void HControl_MouseWheel(object sender, MouseEventArgs e)
        {
            ShowHText();
        }

        private void HControl_MouseMove(object sender, MouseEventArgs e)
        {
            ShowHText();
        }

        public void CreateModel()
        {
            try
            {
                if (DispImage == null || !DispImage.IsInitialized())
                {
                    Logger.AddLog($"{ModuleParam.ModuleName}无图像！", eMsgType.Warn);
                    return;
                }
                ROI ModelSearch = RoiList[ModuleParam.ModuleName + ROIDefine.Search];
                ROI ModelTemplet = RoiList[ModuleParam.ModuleName + ROIDefine.Templet];
                HImage imageReduce = new HImage(DispImage.ReduceDomain(ModelSearch.GetRegion()));
                Find.CreateModel(
                    ModelType,
                    imageReduce,
                    ModelTemplet,
                    editViewModel.Threshold,
                    Levels,
                    editViewModel.StartPhi,
                    editViewModel.EndPhi,
                    editViewModel.MinScale,
                    editViewModel.MaxScale,
                    editViewModel.CompType,
                    editViewModel.Optimization,
                    ref ModelImage
                );
                if (ModelImage.IsInitialized())
                {
                    int mathNum = int.Parse(GetLinkValue(MathNum).ToString());
                    imageReduce = new HImage(DispImage.ReduceDomain(ModelSearch.GetRegion()));
                    Find.FindModel(
                        ModelType,
                        imageReduce,
                        ModelImage,
                        MinScore,
                        mathNum,
                        MaxOverlap,
                        GreedDeg,
                        out ModeCoord
                    );
                    Logger.AddLog(ModuleParam.ModuleName + ":创建模板成功！");
                }
            }
            catch (Exception ex)
            {
                Logger.GetExceptionMsg(ex);
            }
        }

        public override void ShowHRoi()
        {
            var view = ModuleView as MatchingView;
            VMHWindowControl mWindowH;
            bool dispSearchRegion = true;
            if (view == null || view.IsClosed)
            {
                mWindowH = ViewDic.GetView(DispImage.DispViewID);
                dispSearchRegion = false;
            }
            else
            {
                mWindowH = view.mWindowH;
                if (mWindowH != null)
                {
                    mWindowH.ClearWindow();
                    mWindowH.Image = new HImage(DispImage);
                }
            }
            if (dispSearchRegion)
            {
                if (RoiList.ContainsKey(ModuleParam.ModuleName + ROIDefine.Search))
                {
                    ROIRectangle1 ROIRect1 = (ROIRectangle1)
                        RoiList[ModuleParam.ModuleName + ROIDefine.Search];
                    mWindowH.WindowH.genRect1(
                        ModuleParam.ModuleName + ROIDefine.Search,
                        ROIRect1.row1,
                        ROIRect1.col1,
                        ROIRect1.row2,
                        ROIRect1.col2,
                        ref RoiList
                    );
                }
                else
                {
                    mWindowH.WindowH.genRect1(
                        ModuleParam.ModuleName + ROIDefine.Search,
                        5,
                        5,
                        mWindowH.hv_imageHeight - 5,
                        mWindowH.hv_imageWidth - 5,
                        ref RoiList
                    );
                }
            }
            List<HRoi> roiList = mHRoi.Where(c => c.ModuleName == ModuleParam.ModuleName).ToList();
            foreach (HRoi roi in roiList)
            {
                if (roi.roiType == HRoiType.文字显示)
                {
                    HText roiText = (HText)roi;
                    ShowTool.SetFont(
                        mWindowH.hControl.HalconWindow,
                        roiText.size,
                        "false",
                        "false"
                    );
                    ShowTool.SetMsg(
                        mWindowH.hControl.HalconWindow,
                        roiText.text,
                        "image",
                        roiText.row,
                        roiText.col,
                        roiText.drawColor,
                        "false"
                    );
                }
                else if (roi.roiType == HRoiType.搜索范围)
                {
                    if (ShowSearchRegion && ModuleView == null)
                    {
                        mWindowH.WindowH.DispHobject(roi.hobject, roi.drawColor);
                    }
                }
                else
                {
                    mWindowH.WindowH.DispHobject(roi.hobject, roi.drawColor);
                }
            }
        }

        private void ShowHText()
        {
            var view = ModuleView as MatchingView;
            if (view == null)
                return;
            if (RoiList.Count == 0 || DispImage == null)
                return;
            HTuple info = RoiList[ModuleParam.ModuleName + ROIDefine.Search].GetModelData();
            Rectangle1SearchRegion.Row1 = Math.Round(info.DArr[0], 0);
            Rectangle1SearchRegion.Col1 = Math.Round(info.DArr[1], 0);
            Rectangle1SearchRegion.Row2 = Math.Round(info.DArr[2], 0);
            Rectangle1SearchRegion.Col2 = Math.Round(info.DArr[3], 0);
            if (
                info.DArr[2] > view.mWindowH.hv_imageHeight
                || info.DArr[3] > view.mWindowH.hv_imageWidth
            )
            {
                ROIRectangle1 ROIRect1 = new ROIRectangle1(
                    Rectangle1SearchRegion.Row1,
                    Rectangle1SearchRegion.Col1,
                    view.mWindowH.hv_imageHeight - 5,
                    view.mWindowH.hv_imageWidth - 5
                );
                RoiList[ModuleParam.ModuleName + ROIDefine.Search] = ROIRect1;
            }
            ShowTool.SetFont(view.mWindowH.hControl.HalconWindow, 15, "false", "false");
            if (!IsStudying)
            {
                ShowTool.SetMsg(
                    view.mWindowH.hControl.HalconWindow,
                    "搜索框",
                    "image",
                    info.DArr[1] + 5,
                    info.DArr[0] + 5,
                    "cyan",
                    "false"
                );
            }
            if (IsStudying & RoiList.ContainsKey(ModuleParam.ModuleName + ROIDefine.Templet))
            {
                HTuple info1 = RoiList[ModuleParam.ModuleName + ROIDefine.Templet].GetModelData();
                ShowTool.SetMsg(
                    view.mWindowH.hControl.HalconWindow,
                    "学习框",
                    "image",
                    info1.DArr[1],
                    info1.DArr[0],
                    "cyan",
                    "false"
                );
            }
        }

        private void ShowTemp()
        {
            try
            {
                if (DispImage == null || ModelImage == null)
                    return;
                //模板区域
                HRegion ModeRegion = RoiList[
                    ModuleParam.ModuleName + ROIDefine.Templet
                ].GetRegion();
                //在模板窗口显示模板
                HOperatorSet.ReduceDomain(DispImage, ModeRegion, out HObject CutImage);
                HOperatorSet.CropDomain(CutImage, out OutImage);
                //求中心
                HOperatorSet.AreaCenter(
                    ModeRegion,
                    out HTuple FormArea,
                    out HTuple FormY,
                    out HTuple FormX
                );
                HOperatorSet.AreaCenter(
                    OutImage,
                    out HTuple ToArea,
                    out HTuple ToY,
                    out HTuple ToX
                );
                //检测结果-对XLD应用任意加法 2D 变换
                HOperatorSet.VectorAngleToRigid(
                    0,
                    0,
                    0,
                    ToY - (FormY - MathCoord.Y),
                    ToX - (FormX - MathCoord.X),
                    0,
                    out HTuple tempMat2D
                );
                contour_xld = ((HShapeModel)ModelImage)
                    .GetShapeModelContours(1)
                    .AffineTransContourXld(new HHomMat2D(tempMat2D));
                var view = ModuleView as MatchingView;
                if (view == null)
                    return;
                //显示
                view.mWindowH_Template.HobjectToHimage(OutImage);
                view.mWindowH_Template.WindowH.DispHobject(contour_xld, "green");
            }
            catch (Exception ex)
            {
                Logger.AddLog(ModuleParam.ModuleName + ":" + ex.Message);
            }
        }
        #endregion
    }
}
