using EventMgrLib;
using HalconDotNet;
using Plugin.LinesDistance.Views;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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

namespace Plugin.LinesDistance.ViewModels
{
    #region enum
    public enum eLinkCommand
    {
        InputImageLink,
        Line1,
        Line2,
    }
    public enum eFilterMode
    {
        全部,
        剔除最大m个点取n个点,
        剔除最小m个点取n个点,
        剔除最小m个点剔除最大n个点
    }

    #endregion

    [Category("几何测量")]
    [DisplayName("线线距离")]
    [ModuleImageName("LinesDistance")]
    [Serializable]
    public class LinesDistanceModel : ModuleBase
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
                if (DispImage != null && DispImage.IsInitialized())
                {
                    Line1 = (ROILine)Prj.GetParamByName(Line1LinkText).Value;
                    Line2 = (ROILine)Prj.GetParamByName(Line2LinkText).Value;
                    if (Line1.Status && Line2.Status)
                    {
                        RPoint mRPoint = new RPoint(Line1.X, Line1.Y);
                        switch (ValueMode)
                        {
                            case eValueMode.最大值:
                                Distance = Dis.DisPL(mRPoint, Line2, eValueMode.最大值);
                                break;
                            case eValueMode.最小值:
                                Distance = Dis.DisPL(mRPoint, Line2, eValueMode.最小值);
                                break;
                            case eValueMode.平均值:
                                Distance = Dis.DisPL(mRPoint, Line2, eValueMode.平均值);
                                break;
                        }
                        if (ShowResultPoint)
                        {
                            ShowHRoi();
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
            AddOutputParam("状态", "bool", ModuleParam.Status == eRunStatus.OK ? true : false);
            AddOutputParam("时间", "int", ModuleParam.ElapsedTime);
        }
        #region Prop
        public Array ValueModes { get; set; } = Enum.GetValues(typeof(eValueMode));
        private eValueMode _ValueMode = eValueMode.平均值;
        /// <summary>
        /// 取值模式
        /// </summary>
        public eValueMode ValueMode
        {
            get { return _ValueMode; }
            set { Set(ref _ValueMode, value); }
        }
        public Array FilterModes { get; set; } = Enum.GetValues(typeof(eFilterMode));
        private eFilterMode _FilterMode = eFilterMode.全部;
        /// <summary>
        /// 取值模式
        /// </summary>
        public eFilterMode FilterMode
        {
            get { return _FilterMode; }
            set { Set(ref _FilterMode, value); }
        }
        private double _Distance;
        /// <summary>距离</summary>
        public double Distance
        {
            get { return _Distance; }
            set { Set(ref _Distance, value); }
        }
        private bool _ShowResultPoint = true;
        /// <summary>显示结果点</summary>
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
        public ROILine Line2 { get; set; } = new ROILine();
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
        private string _Line1LinkText;
        /// <summary>
        /// 直线1链接文本
        /// </summary>
        public string Line1LinkText
        {
            get { return _Line1LinkText; }
            set { Set(ref _Line1LinkText, value); }
        }
        private string _Line2LinkText;
        /// <summary>
        /// 直线2链接文本
        /// </summary>
        public string Line2LinkText
        {
            get { return _Line2LinkText; }
            set { Set(ref _Line2LinkText, value); }
        }
        #endregion
        #region Command
        public override void Loaded()
        {
            base.Loaded();
            var view = ModuleView as LinesDistanceView;
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
                        Gen.GenContour(out HObject Line1XLD, Line1.StartY, Line1.EndY, Line1.StartX, Line1.EndX);
                        ShowHRoi(new HRoi(ModuleParam.ModuleEncode, ModuleParam.ModuleName, ModuleParam.Remarks, HRoiType.测量直线1, "green", new HObject(Line1XLD)));
                        Gen.GenContour(out HObject Line2XLD, Line2.StartY, Line2.EndY, Line2.StartX, Line2.EndX);
                        ShowHRoi(new HRoi(ModuleParam.ModuleEncode, ModuleParam.ModuleName, ModuleParam.Remarks, HRoiType.测量直线2, "green", new HObject(Line2XLD)));
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
                        var view = this.ModuleView as LinesDistanceView;
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
                case "Line2":
                    Line2LinkText = obj.LinkName;
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
                            case eLinkCommand.Line2:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "object");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},Line2");
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
