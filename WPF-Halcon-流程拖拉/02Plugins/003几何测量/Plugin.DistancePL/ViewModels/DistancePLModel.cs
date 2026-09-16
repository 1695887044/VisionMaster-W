using EventMgrLib;
using HalconDotNet;
using HandyControl.Controls;
using Plugin.DistancePL.Views;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Documents;
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

namespace Plugin.DistancePL.ViewModels
{
    #region enum
    public enum eLinkCommand
    {
        InputImageLink,
        Line1,
        X,
        Y,
    }

    #endregion

    [Category("几何测量")]
    [DisplayName("点线距离")]
    [ModuleImageName("DistancePL")]
    [Serializable]
    public class DistancePLModel : ModuleBase
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
                GetDispImage(InputImageLinkText,true);
                if (DispImage != null && DispImage.IsInitialized())
                {
                    Line1 = (ROILine)Prj.GetParamByName(Line1LinkText).Value;
                    PXLinkValue = Convert.ToDouble(GetLinkValue(PXLinkText));
                    PYLinkValue = Convert.ToDouble(GetLinkValue(PYLinkText));
                    RPoint mRPoint = new RPoint(Line1.X, Line1.Y);

                    Dis.PLPedal(PXLinkValue, PYLinkValue, Line1, out double outY, out double outX, out double dis);
                    PointX=Math.Round(outX,4); 
                    PointY= Math.Round(outY, 4);
                    Distance = Math.Round(dis, 4);
                    if (ShowResultLine)
                    {
                        Gen.GenContour(out HObject Line1XLD, PYLinkValue, outY, PXLinkValue, outX);
                        ShowHRoi(new HRoi(ModuleParam.ModuleEncode, ModuleParam.ModuleName, ModuleParam.Remarks, HRoiType.测量直线1, "red", new HObject(Line1XLD)));
                    }
                    if (ShowResultPoint)
                    {
                        Gen.GenCross(out HObject Point1, outY, outX, 60, 0);
                        Gen.GenCross(out HObject Point2, PYLinkValue, PXLinkValue, 60, 0);
                        ShowHRoi(new HRoi(ModuleParam.ModuleEncode, ModuleParam.ModuleName, ModuleParam.Remarks, HRoiType.检测点P1, "cyan", new HObject(Point1)));
                        ShowHRoi(new HRoi(ModuleParam.ModuleEncode, ModuleParam.ModuleName, ModuleParam.Remarks, HRoiType.检测点P2, "cyan", new HObject(Point2)));
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
            AddOutputParam("距离", "double", Distance);
            AddOutputParam("垂点X", "double", PointX);
            AddOutputParam("垂点Y", "double", PointY);
            AddOutputParam("状态", "bool", ModuleParam.Status == eRunStatus.OK ? true : false);
            AddOutputParam("时间", "int", ModuleParam.ElapsedTime);
        }
        #region Prop
        private double _Distance;
        /// <summary>距离</summary>
        public double Distance
        {
            get { return _Distance; }
            set { Set(ref _Distance, value); }
        }
        private bool _ShowResultLine = true;
        /// <summary>显示垂线</summary>
        public bool ShowResultLine
        {
            get { return _ShowResultLine; }
            set { Set(ref _ShowResultLine, value); }
        }
        private bool _ShowResultPoint = true;
        /// <summary>显示垂点</summary>
        public bool ShowResultPoint
        {
            get { return _ShowResultPoint; }
            set { Set(ref _ShowResultPoint, value); }
        }
        /// <summary>
        /// 直线1信息
        /// </summary>
        public ROILine Line1 { get; set; } = new ROILine();
        /// <summary>
        /// 直线2信息
        /// </summary>
        private string _InputImageLinkText;
        /// <summary>
        /// 输入图像链接文本
        /// </summary>
        public string InputImageLinkText
        {
            get { return _InputImageLinkText; }
            set { Set(ref _InputImageLinkText, value); }
        }
        private string _Line1LinkText;
        /// <summary>
        /// 直线1链接文本
        /// </summary>
        public string Line1LinkText
        {
            get { return _Line1LinkText; }
            set { Set(ref _Line1LinkText, value); }
        }
        private string _PXLinkText;
        /// <summary>
        /// 点X链接文本
        /// </summary>
        public string PXLinkText
        {
            get { return _PXLinkText; }
            set { Set(ref _PXLinkText, value); }
        }
        private double  _PXLinkValue;
        /// <summary>
        /// 点X链接文本
        /// </summary>
        public double  PXLinkValue
        {
            get { return _PXLinkValue; }
            set { _PXLinkValue = value; }
        }
        private string _PYLinkText;
        /// <summary>
        /// 点Y链接文本
        /// </summary>
        public string PYLinkText
        {
            get { return _PYLinkText; }
            set { Set(ref _PYLinkText, value); }
        }
        private double _PYLinkValue;
        /// <summary>
        /// 点Y链接文本
        /// </summary>
        public double PYLinkValue
        {
            get { return _PYLinkValue; }
            set { _PYLinkValue = value; }
        }

        private double _PointX;
        /// <summary>
        /// 垂点X
        /// </summary>
        public double PointX
        {
            get { return _PointX; }
            set { Set(ref _PointX, value); }
        }
        private double _PointY;
        /// <summary>
        /// 垂点Y
        /// </summary>
        public double PointY
        {
            get { return _PointY; }
            set { Set(ref _PointY, value); }
        }
        #endregion
        #region Command
        public override void Loaded()
        {
            base.Loaded();
            var view = ModuleView as DistancePLView;
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
            GetDispImage(InputImageLinkText,true);
            if (DispImage != null && DispImage.IsInitialized())
            {
                ShowHRoi();
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
                        var view = this.ModuleView as DistancePLView;
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
                case "Line1":
                    Line1LinkText = obj.LinkName;
                    break;
                case "PX":
                    PXLinkText = obj.LinkName;
                    break;
                case "PY":
                    PYLinkText = obj.LinkName;
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
                            case eLinkCommand.Line1:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "object");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},Line1");
                                break;
                            case eLinkCommand.X:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},PX");
                                break;
                            case eLinkCommand.Y:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},PY");
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
        #endregion
        #region Method
        
        #endregion
    }
}
