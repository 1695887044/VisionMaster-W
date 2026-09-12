using EventMgrLib;
using HalconDotNet;
using HandyControl.Controls;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using Plugin.CalculateOffset.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
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
using VM.Start.Common.Helper;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.Services;
using VM.Start.ViewModels;
using VM.Start.Views;
using VM.Start.Views.Dock;

namespace Plugin.CalculateOffset.ViewModels
{
    #region enum
    public enum eLinkCommand
    {
        InputImageLink,
        XLink,
        YLink,
        DegLink,
        OpenFile
    }
    #endregion
    [Category("坐标标定")]
    [DisplayName("计算偏移")]
    [ModuleImageName("CalculateOffset")]
    [Serializable]
    public class CalculateOffsetViewModel : ModuleBase
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
            int index = Prj.ModuleList.IndexOf(this);
            for (int i = index - 1; i >= 0; i--)
            {
                if (Prj.ModuleList[i].ModuleParam.ModuleName.StartsWith("模板匹配"))
                {
                    HomMat2D = Prj.ModuleList[i].HomMat2D;
                    XLinkText.Text = $"&{Prj.ModuleList[i].ModuleParam.ModuleName}.X";
                    YLinkText.Text = $"&{Prj.ModuleList[i].ModuleParam.ModuleName}.Y";
                    DegLinkText.Text = $"&{Prj.ModuleList[i].ModuleParam.ModuleName}.Deg";
                    return;
                }
            }
        }
        public override bool ExeModule()
        {
            if (ModuleParam.ModuleName.StartsWith("坐标补正结束"))
            {
                ChangeModuleRunStatus(eRunStatus.OK);
                return true;
            }
            Stopwatch.Restart();
            if (Hommat2DTrans.Length == 0) return false;
            if ((EnableRotateCenter) && (CenterX == -999) || (CenterY == -999)) return false;
            try
            {
                if (InputImageLinkText == null)
                {
                    ChangeModuleRunStatus(eRunStatus.NG);
                    return false;
                }
                GetModeCoord();
                MathCoord.X = double.Parse(GetLinkValue(XLinkText).ToString());
                MathCoord.Y = double.Parse(GetLinkValue(YLinkText).ToString());
                MathCoord.Phi = double.Parse(GetLinkValue(DegLinkText).ToString());
                HTuple hv_RealXRef = new HTuple(); HTuple hv_RealYRef = new HTuple(); HTuple hv_RealFindX = new HTuple(); HTuple hv_RealFindY = new HTuple();
                hv_RealXRef.Dispose();  hv_RealYRef.Dispose();hv_RealFindX.Dispose(); hv_RealFindY.Dispose();
                if (!EnableRotateCenter)
                {
                    HOperatorSet.AffineTransPoint2d(Hommat2DTrans, ModeCoord.X, ModeCoord.Y, out hv_RealXRef, out hv_RealYRef);
                    HOperatorSet.AffineTransPoint2d(Hommat2DTrans, MathCoord.X, MathCoord.Y, out hv_RealFindX, out hv_RealFindY);
                    OffsetX = -(hv_RealXRef.D - hv_RealFindX.D);
                    OffsetY = -(hv_RealYRef.D - hv_RealFindY.D);
                    OffsetR = -(ModeCoord.Phi - MathCoord.Phi);
                }
                else
                {
                    HTuple  Hommat2dRotate=new HTuple();
                    Hommat2dRotate.Dispose();
                    HOperatorSet.HomMat2dIdentity(out Hommat2dRotate);
                    HOperatorSet.HomMat2dRotate(Hommat2dRotate, -(ModeCoord.Phi - MathCoord.Phi), CenterX, CenterY,out Hommat2dRotate);
                    HOperatorSet.AffineTransPoint2d(Hommat2DTrans, ModeCoord.X, ModeCoord.Y, out hv_RealXRef, out hv_RealYRef);
                    HOperatorSet.AffineTransPoint2d(Hommat2dRotate, MathCoord.X, MathCoord.Y, out hv_RealFindX, out hv_RealFindY);
                    Hommat2dRotate.Dispose();
                    OffsetX = Math.Round(-(hv_RealXRef.D - hv_RealFindX.D), 2);
                    OffsetY = Math.Round(-(hv_RealYRef.D - hv_RealFindY.D), 2);
                    OffsetR = Math.Round(-(ModeCoord.Phi - MathCoord.Phi), 2);
                }
                hv_RealXRef.Dispose(); hv_RealYRef.Dispose(); hv_RealFindX.Dispose(); hv_RealFindY.Dispose();
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
        private LinkVarModel _XLinkText = new LinkVarModel() { Value = 0 };
        /// <summary>
        /// X链接文本
        /// </summary>
        public LinkVarModel XLinkText
        {
            get { return _XLinkText;  }
            set { Set(ref _XLinkText, value); RaisePropertyChanged(); }
        }
        private LinkVarModel _YLinkText = new LinkVarModel() { Value = 0 };
        /// <summary>
        /// Y链接文本
        /// </summary>
        public LinkVarModel YLinkText
        {
            get { return _YLinkText; }
            set { Set(ref _YLinkText, value); RaisePropertyChanged(); }
        }
        private LinkVarModel _DegLinkText = new LinkVarModel() { Value = 0 };
        /// <summary>
        /// Deg链接文本
        /// </summary>
        public LinkVarModel DegLinkText
        {
            get { return _DegLinkText; }
            set { Set(ref _DegLinkText, value); RaisePropertyChanged(); }
        }
        private string _Hommat2DFilePath = "数据链接";
        public string Hommat2DFilePath
        {
            get { return _Hommat2DFilePath; }
            set { Set(ref _Hommat2DFilePath, value); RaisePropertyChanged(); }
        }
        private HTuple _Hommat2DTrans = new HTuple();
        public HTuple Hommat2DTrans
        {
            get { return _Hommat2DTrans; }
            set { _Hommat2DTrans = value; }
        }
        private double  _CenterX=-999;
        public double  CenterX
        {
            get { return _CenterX; }
            set { _CenterX = value; }
        }
        private double  _CenterY=-999;
        public double CenterY
        {
            get { return _CenterY; }
            set { _CenterY = value; }
        }
        private bool _EnableRotateCenter = false;
        public bool EnableRotateCenter
        {
            get { return _EnableRotateCenter; }
            set { _EnableRotateCenter = value; }
        }
        private double _OffsetX;
        public double OffsetX
        {
            get { return _OffsetX; }
            set { _OffsetX = value; }
        }
        private double _OffsetY;
        public double OffsetY
        {
            get { return _OffsetY; }
            set { _OffsetY = value; }
        }
        private double _OffsetR;
        public double OffsetR
        {
            get { return _OffsetR; }
            set { _OffsetR = value; }
       }

        #endregion

        #region Command
        public override void Loaded()
        {
            base.Loaded();
            var view = ModuleView as CalculateOffsetView;
            if (view != null)
            {
                if (view.mWindowH == null)
                {
                    view.mWindowH = new VMHWindowControl();
                    view.winFormHost.Child = view.mWindowH;
                }
                SetDefaultLink();
                GetDispImage(InputImageLinkText);
                if (DispImage != null && DispImage.IsInitialized())
                {
                    view.mWindowH.Image = DispImage;
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
                        var view = this.ModuleView as CalculateOffsetView;
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
                case "XLink":
                    XLinkText.Text = obj.LinkName;
                    break;
                case "YLink":
                    YLinkText.Text = obj.LinkName;
                    break;
                case "DegLink":
                    DegLinkText.Text = obj.LinkName;
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
                            case eLinkCommand.XLink:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},XLink");
                                break;
                            case eLinkCommand.YLink:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},YLink");
                                break;
                            case eLinkCommand.DegLink:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},DegLink");
                                break;
                            default:
                                break;
                        }

                    });
                }
                return _LinkCommand;
            }
        }
        public override void AddOutputParams()
        {
            HTuple deg=new HTuple(); deg.Dispose();
            HOperatorSet.TupleDeg(OffsetR, out deg);
            MathCoord.Phi = deg.D;
            base.AddOutputParams();
            AddOutputParam("OffsetX", "double", OffsetX);
            AddOutputParam("OffsetY", "double", OffsetY);
            AddOutputParam("OffsetA", "double", MathCoord.Phi);
            deg.Dispose();
        }
        [NonSerialized]
        private CommandBase _OpenFileCommand;
        public CommandBase OpenFileCommand
        {
            get
            {
                if (_OpenFileCommand == null)
                {
                    _OpenFileCommand = new CommandBase((obj) =>
                    {
                        System.Windows.Forms.OpenFileDialog  op=new System.Windows.Forms.OpenFileDialog();
                        op.Filter = "标定结果文件(*.tup)|*.tup";
                        if (op.ShowDialog()==DialogResult.OK)
                        {
                            HTuple hTuple = new HTuple();
                            HOperatorSet.ReadTuple(op.FileName,out hTuple);
                            if ((hTuple != null) &&(hTuple.Length>=1))
                            {
                                if (hTuple.Length>1)
                                {
                                    HTuple n = new HTuple();

                                    HOperatorSet.TupleSelectRange(hTuple, 0, hTuple.Length-3,out n);
                                    Hommat2DTrans = n;
                                    CenterX = hTuple[hTuple.Length - 2].D;
                                    CenterY = hTuple[hTuple.Length - 1].D;
                                }
                                else
                                {
                                    Hommat2DTrans = hTuple[0];
                                }
                                Hommat2DFilePath= op.FileName;  
                            }
                        }
                    });
                }
                return _OpenFileCommand;
            }
        }
        #endregion

        #region Method
        /// <summary>
        /// 获取模板坐标
        /// </summary>
        private void GetModeCoord()
        {
            int index = Prj.ModuleList.IndexOf(this);
            for (int i = index - 1; i >= 0; i--)
            {
                if (Prj.ModuleList[i].ModuleParam.ModuleName.StartsWith("模板匹配"))
                {
                    ModeCoord = Prj.ModuleList[i].ModeCoord; return;
                }
            }
        }
        #endregion

    }

}
