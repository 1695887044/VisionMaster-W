using DMSkin.Socket;
using EventMgrLib;
using HalconDotNet;
using HandyControl.Controls;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using Plugin.NPointCal.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.ModelBinding;
using System.Web.UI.WebControls.Expressions;
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
using VM.Start.Common.Extension;
using VM.Start.Common.Helper;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.Services;
using VM.Start.ViewModels;
using VM.Start.Views;
using VM.Start.Views.Dock;

namespace Plugin.NPointCal.ViewModels
{
    #region enum
    public enum eLinkCommand
    {
        DispContentLink,
        StatusLink,
        InputImageLink,
        InputImageX,
        InputImageY,
        InputRealX,
        InputRealY,
            InputStr
    }
    public enum eInputType
    {
        小数,
        字符
    }


    #endregion
    [Category("坐标标定")]
    [DisplayName("九点标定")]
    [ModuleImageName("NPointCal")]
    [Serializable]
    public class NPointCal : ModuleBase
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
            #region 新
            try
            {
                int CalLenght = 0;
                switch (MPointType)
                {
                    case PointType.Three:
                        {
                            CalLenght = 3;
                            break;
                        }
                    case PointType.Nine:
                        {
                            CalLenght = 9;
                            break;
                        }
                    case PointType.Fourteen:
                        {
                            CalLenght = 14;
                            break;
                        }
                }
                //大于  计算放射变换矩阵  小于则继续收集点为
                if (NPointCalParams==null)
                {
                    NPointCalParams = new AsyncObservableCollection<NPointCalParam>();
                }
                if (NPointCalParams.Count < CalLenght)
                {
                    if (InputType == eInputType.字符)
                    {
                        string s = base.GetString(InputStrXYLinkText);
                        string[] StrArray = s.Split(',');
                        double RealX = Convert.ToDouble(StrArray[0]);
                        double RealY = Convert.ToDouble(StrArray[1]);
                        double PxielX = base.GetDouble(InputPixelXLinkText);
                        double PxielY = base.GetDouble(InputPixelYLinkText);
                        NPointCalParams.Add(new NPointCalParam() { ID = NPointCalParams.Count + 1, ImageX = PxielX, ImageY = PxielY, RealX = RealX, RealY = RealY });
                        Logger.AddLog("Imagex:"+ PxielX.ToString()+"ImageY:"+PxielY+"RealX:"+RealX.ToString()+"RealY:"+RealY.ToString(), eMsgType.Info);
                    }
                    else
                    {
                        NPointCalParams.Add(new NPointCalParam() { ID = NPointCalParams.Count + 1, ImageX = base.GetDouble(InputPixelXLinkText), ImageY = base.GetDouble(InputPixelYLinkText), RealX = base.GetDouble(InputRealXLinkText), RealY = base.GetDouble(InputRealYLintText) });
                    }
                }
                else
                {
                    if (CalLenght <= 9)
                    {
                        List<double> LImageX = new List<double>();
                        List<double> LImageY = new List<double>();
                        List<double> LRealX = new List<double>();
                        List<double> LRealY = new List<double>();
                        for (int i = 0; i < NPointCalParams.Count; i++)
                        {
                            LImageX.Add(NPointCalParams[i].ImageX);
                            LImageY.Add(NPointCalParams[i].ImageY);
                            LRealX.Add(NPointCalParams[i].RealX);
                            LRealY.Add(NPointCalParams[i].RealY);
                        }
                        HOperatorSet.VectorToHomMat2d(new HTuple(LImageX.ToArray()), new HTuple(LImageY.ToArray()), new HTuple(LRealX.ToArray()), new HTuple(LRealY.ToArray()),out mHomMat2DTransl);//平移矩阵
                    }
                    else
                    {
                        List<double> LImageX = new List<double>();
                        List<double> LImageY = new List<double>();
                        List<double> LRealX = new List<double>();
                        List<double> LRealY = new List<double>();
                        List<double> XfitCir = new List<double>();
                        List<double> YfitCir = new List<double>();
                        for (int i = 0; i < NPointCalParams.Count; i++)
                        {
                            if (i <= 8)
                            {
                                LImageX.Add(NPointCalParams[i].ImageX);
                                LImageY.Add(NPointCalParams[i].ImageY);
                                LRealX.Add(NPointCalParams[i].RealX);
                                LRealY.Add(NPointCalParams[i].RealY);
                            }
                            else
                            {
                                XfitCir.Add(NPointCalParams[i].ImageX);
                                YfitCir.Add(NPointCalParams[i].ImageY);
                            }
                        }
                        HOperatorSet.VectorToHomMat2d(new HTuple(LImageX.ToArray()), new HTuple(LImageY.ToArray()), new HTuple(LRealX.ToArray()), new HTuple(LRealY.ToArray()),out mHomMat2DTransl);
                        Fit.FitCircle(YfitCir.ToArray(), XfitCir.ToArray(), out Circle_Info 拟合圆);
                        mRotateCenterX = 拟合圆.CenterX;
                        mRotateCenterY = 拟合圆.CenterY;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                ChangeModuleRunStatus(eRunStatus.OK);
                return false;
            }
            #endregion
        }
        #region 抄过来
        /// <summary>点类型：3/9/14</summary>
        private PointType mPointType = PointType.Nine;
        /// <summary>点类型：3/9/14</summary>
        public PointType MPointType { get => mPointType; set => mPointType = value; }
        /// <summary>标定:手动/自动 </summary>
      
        /// <summary>相机:固定/变化 </summary>
        private bool mCamerFix = false;
        /// <summary>相机:固定/变化 </summary>
        public bool MCamerFix { get => mCamerFix; set => mCamerFix = value; }
        /// <summary>输入X</summary>
        private string _InputPixelXLinkText = "数据链接";
        /// <summary>输入X</summary>
        public string InputPixelXLinkText
        {
            get{ return _InputPixelXLinkText; }
            set { _InputPixelXLinkText = value; RaisePropertyChanged() ; }
        }
        /// <summary>输入Y</summary>
        private string _InputPixelYLinkText = "数据链接";
        /// <summary>输入Y</summary>
        public string InputPixelYLinkText 
        { 
            get { return _InputPixelYLinkText; }
            set { _InputPixelYLinkText = value; RaisePropertyChanged(); }
        }
        /// <summary>输入X</summary>
        private string _InputRealXLinkText = "数据链接";
        /// <summary>输入X</summary>
        public string InputRealXLinkText 
        {
            get { return _InputRealXLinkText; }
            set { _InputRealXLinkText = value; RaisePropertyChanged(); }
        }
        /// <summary>输入Y</summary>
        private string _InputRealYLintText = "数据链接";
        /// <summary>输入Y</summary>
        public string InputRealYLintText
        {
            get { return _InputRealYLintText; }
            set { _InputRealYLintText = value; RaisePropertyChanged(); }
        }
  
        /// <summary>基准角度</summary>
        private double mBaseAngle = 0.0;
        /// <summary>基准角度</summary>
        public double MBaseAngle { get => mBaseAngle; set => mBaseAngle = value; }
        private bool mAngleNot = false;
        /// <summary>角度取反</summary>
        public bool MAngleNot { get => mAngleNot; set => mAngleNot = value; }
        /// <summary>是否启用旋转中心标定</summary>
        private bool mRotateCentre = true;
        /// <summary>MarkX</summary>
        private double mMarkX = 0.0;
        /// <summary>MarkY</summary>
        private double mMarkY = 0.0;
        /// <summary>旋转角度</summary>
        private double mRotateAngle = 0.0;
        /// <summary>是否自动清空</summary>
        private bool mAutoCleare = false;
        /// <summary>检查标定结果</summary>
        private bool mCheckCalResult = false;
        /// <summary>旋转中心X</summary>
        private double mRotateCenterX = 0.0;
        /// <summary>旋转中心Y</summary>
        private double mRotateCenterY = 0.0;
        ///<summary>相机和X轴的夹角 </summary>
        private double mPhiSingle = 0.0;
        ///<summary>标定RMS</summary>
        private double mCalibRms = 0.0;
        /// <summary>平移矩阵</summary>
        private HTuple mHomMat2DTransl = new HTuple();
        /// <summary>旋转矩阵</summary>
        private HHomMat2D mHomMat2DRotate = new HHomMat2D();
        //坐标集合-序列话也无所谓*****************************************
        /// <summary>像素坐标X</summary>
        public double[] ImageX_S = new double[14];
        /// <summary>像素坐标Y</summary>
        public double[] ImageY_S = new double[14];
        /// <summary>机械坐标X</summary>
        public double[] RobotX_S = new double[14];
        /// <summary>机械坐标Y</summary>
        public double[] RobotY_S = new double[14];
        /// <summary>自动标定计数</summary>
        public int mAutoCalCounter = 0;
        /// <summary>标定自动清零</summary>
        public bool mAutoClear = false;
        /// <summary>标定点信息</summary>
        public List<NPoint> mNPoint = new List<NPoint>();
        private int _CurrentRow;
        public int CurrentRow
        {
            get { return _CurrentRow; }
            set { _CurrentRow = value; }
        }

        private double _testImagex;
        public double testImagex
        {
            get { return _testImagex; }
            set { _testImagex = value; RaisePropertyChanged(); }
        }
        private double _testImagey;
        public double testImagey
        {
            get { return _testImagey; }
            set { _testImagey = value;  RaisePropertyChanged(); }
        }
        private double _testrealx;
        public double testrealx
        {
            get { return _testrealx; }
            set { _testrealx = value; RaisePropertyChanged(); }
        }
        private double _testrealy;
        public double testrealy
        {
            get { return _testrealy; }
            set { _testrealy = value; RaisePropertyChanged(); }
        }

        private eInputType _InputType = eInputType.字符;
        /// <summary>
        /// 搜索区域源
        /// </summary>
        public eInputType InputType
        {
            get { return _InputType; }
            set
            { _InputType=value; RaisePropertyChanged(); }
        }
        private string _InputStrXYLinkText = "数据连接";
        public string InputStrXYLinkText
        {
            get { return _InputStrXYLinkText; }
            set
            { _InputStrXYLinkText = value; RaisePropertyChanged(); }
        }
        #endregion
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
        [NonSerialized]
        private AsyncObservableCollection<NPointCalParam> _NPointCalParams = new AsyncObservableCollection<NPointCalParam>();
        public AsyncObservableCollection<NPointCalParam> NPointCalParams
        {
            get { return _NPointCalParams; }
            set { _NPointCalParams = value; RaisePropertyChanged(); }
        }
        #endregion
        #region Command
        public override void Loaded()
        {
            base.Loaded();
            var view = ModuleView as NPointCalView;
            if (view != null)
            {
              
                SetDefaultLink();
                GetDispImage(InputImageLinkText);
                if (DispImage != null && DispImage.IsInitialized())
                {
                    // view.mWindowH.Image = DispImage;
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
                        var view = this.ModuleView as NPointCalView;
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
                case "ImageX":
                    InputPixelXLinkText = obj.LinkName;
                    break;
                case "ImageY":
                    InputPixelYLinkText = obj.LinkName;
                    break;
                case "RealX":
                    InputRealXLinkText = obj.LinkName;
                    break;
                case "RealY":
                    InputRealYLintText = obj.LinkName;
                    break;
                case "StrRealXY":
                    InputStrXYLinkText = obj.LinkName;
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
                            case eLinkCommand.DispContentLink:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "bool,string,double,int");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},DispContent");
                                break;
                            case eLinkCommand.StatusLink:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "bool");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},StatusLink");
                                break;
                            case eLinkCommand.InputImageLink:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "HImage");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},InputImageLink");
                                break;
                            case eLinkCommand.InputImageX:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},ImageX");
                                break;
                            case eLinkCommand.InputImageY:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},ImageY");
                                break;
                            case eLinkCommand.InputRealX:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},MInputRealX");
                                break;
                            case eLinkCommand.InputRealY:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},RealX");
                                break;
                            case eLinkCommand.InputStr:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "string");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},StrRealXY");
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
                            case "AddRow":
                               NPointCalParams.Add(new NPointCalParam() { ID= NPointCalParams.Count+1});
                                break;
                            case "DeleteRow":
                                if ((CurrentRow < 0)||(NPointCalParams.Count<=0) )return;

                                NPointCalParams.RemoveAt(CurrentRow);
                                break;
                            case "WriteToFile":
                                {
                                    System.Windows.Forms.SaveFileDialog sa = new System.Windows.Forms.SaveFileDialog();
                                    if (sa.ShowDialog() == DialogResult.OK)
                                    {
                                        SerializeHelp.BinSerializeAndSaveFile(NPointCalParams, sa.FileName + ".cal");
                                    }
                                } 
                                break;
                            case "LoadFromFile":
                                { 
                                System.Windows.Forms.OpenFileDialog op=new System.Windows.Forms.OpenFileDialog();
                                    if (op.ShowDialog()==DialogResult.OK)
                                    {
                                        NPointCalParams = SerializeHelp.BinDeserialize<AsyncObservableCollection<NPointCalParam>>(op.FileName);
                                    }
                                }
                                break;
                            case "WriteReaultToFile":
                                {
                                    System.Windows.Forms.SaveFileDialog sa = new System.Windows.Forms.SaveFileDialog();
                                    if (sa.ShowDialog() == DialogResult.OK)
                                    {
                                        HTuple hTuple = new HTuple();
                                        HOperatorSet.TupleConcat(hTuple, MHomMat2DTransl,out hTuple);
                                        HOperatorSet.TupleConcat(hTuple, mRotateCenterX, out hTuple);
                                        HOperatorSet.TupleConcat(hTuple, mRotateCenterY, out hTuple);
                                        HOperatorSet.WriteTuple(hTuple,sa.FileName);
                                    }
                                }
                                break;
                            case "Test":
                                HTuple X=new HTuple();HTuple Y=new HTuple();
                                HOperatorSet.AffineTransPoint2d(MHomMat2DTransl,testImagex,testImagey,out X,out Y);
                                testrealx = X.D;
                                testrealy= Y.D; 
                                break;
                            default:
                                break;
                        }
                    });
                }
                return _DataOperateCommand;
            }
        }
        public override void AddOutputParams()
        {
            base.AddOutputParams();
            AddOutputParam("MHomMat2DTransl", "Htuple", MHomMat2DTransl);
            AddOutputParam("mRotateCenterX", "double", mRotateCenterX);
            AddOutputParam("mRotateCenterY", "double", mRotateCenterY);
        }
        /// <summary>平移矩阵</summary>
        public HTuple MHomMat2DTransl { get => mHomMat2DTransl; set => mHomMat2DTransl = value; }
        #endregion
    }

    [Serializable]
    public class NPointCalParam
    {
        public int ID { get; set; }
        public double ImageX { get; set; }
        public double ImageY { get; set; }
        public double RealX { get; set; }
        public double RealY { get; set; }
    }
}
