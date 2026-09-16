using EventMgrLib;
using HalconDotNet;
using Microsoft.WindowsAPICodePack.Dialogs;
using Plugin.RotateNewPoint.Views;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Halcon;
using VM.Start.Attributes;
using VM.Start.Common;
using VM.Start.Common.Enums;
using VM.Start.Common.Helper;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.ViewModels;
using VM.Start.Views.Dock;

namespace Plugin.RotateNewPoint.ViewModels
{
    #region enum
    public enum eLinkCommand
    {
        RotateCenterX,
        RotateCenterY,
        RotatePointX,
        RotatePointY,
        RotateAngle
    }
    #endregion
    [Category("对位工具")]
    [DisplayName("旋转新点")]
    [ModuleImageName("RotateNewPoint")]
    [Serializable]
    public class RotateNewPointViewModel : ModuleBase
    {
        public override bool ExeModule()
        {
            Stopwatch.Restart();
            try
            {
                double X1 = Convert.ToDouble(GetLinkValue(RotateCenterX));
                double Y1 = Convert.ToDouble(GetLinkValue(RotateCenterY));
                double X2 = Convert.ToDouble(GetLinkValue(RotatePointX));
                double Y2 = Convert.ToDouble(GetLinkValue(RotatePointY));
                double R = Convert.ToDouble(GetLinkValue(RotateAngle));
                if (InvertAngle)
                    R = 0 - R;
                double CenterX = 0, CenterY = 0;
                if (CalRotateNewPoint(X2, Y2, X1, Y1, R, out CenterX, out CenterY))
                {
                    RotateNewPointX = CenterX;
                    RotateNewPointY = CenterY;
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
            AddOutputParam("状态", "bool", ModuleParam.Status == eRunStatus.OK ? true : false);
            AddOutputParam("时间", "int", ModuleParam.ElapsedTime);
            AddOutputParam("新点X", "double", RotateNewPointX);
            AddOutputParam("新点Y", "double", RotateNewPointY);
        }
        #region Prop     
        private double PI = 3.1415926535897932384626433832795028841971;
        private LinkVarModel _RotateCenterX = new LinkVarModel() { Text = "0" };
        /// <summary>
        /// 起始点X
        /// </summary>
        public LinkVarModel RotateCenterX
        {
            get { return _RotateCenterX; }
            set { _RotateCenterX = value; RaisePropertyChanged(); }
        }
        private LinkVarModel _RotateCenterY = new LinkVarModel() { Text = "0" };
        /// <summary>
        /// 起始点Y
        /// </summary>
        public LinkVarModel RotateCenterY
        {
            get { return _RotateCenterY; }
            set { _RotateCenterY = value; RaisePropertyChanged(); }
        }

        private LinkVarModel _RotatePointX = new LinkVarModel() { Text = "0" };
        /// <summary>
        /// 终点X
        /// </summary>
        public LinkVarModel RotatePointX
        {
            get { return _RotatePointX; }
            set { _RotatePointX = value; RaisePropertyChanged(); }
        }
        private LinkVarModel _RotatePointY = new LinkVarModel() { Text = "0" };
        /// <summary>
        /// 终点Y
        /// </summary>
        public LinkVarModel RotatePointY
        {
            get { return _RotatePointY; }
            set { _RotatePointY = value; RaisePropertyChanged(); }
        }
        private LinkVarModel _RotateAngle = new LinkVarModel() { Text = "0" };
        /// <summary>
        /// 旋转角度
        /// </summary>
        public LinkVarModel RotateAngle
        {
            get { return _RotateAngle; }
            set { _RotateAngle = value; RaisePropertyChanged(); }
        }
        private bool _InvertAngle;
        /// <summary>
        /// 角度是否取反
        /// </summary>
        public bool InvertAngle
        {
            get { return _InvertAngle; }
            set
            {
                Set(ref _InvertAngle, value);
            }
        }
        private double _RotateNewPointX = 0;
        /// <summary>
        /// 旋转中心X
        /// </summary>
        public double RotateNewPointX
        {
            get { return _RotateNewPointX; }
            set
            {
                Set(ref _RotateNewPointX, value);
            }
        }
        private double _RotateNewPointY = 0;
        /// <summary>
        /// 旋转中心Y
        /// </summary>
        public double RotateNewPointY
        {
            get { return _RotateNewPointY; }
            set
            {
                Set(ref _RotateNewPointY, value);
            }
        }
        #endregion
        #region Command
        public override void Loaded()
        {
            base.Loaded();
            var view = ModuleView as RotateNewPointView;
            if (view != null)
            {
                ClosedView = true;
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
                        var view = ModuleView as RotateNewPointView;
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
                case "RotateCenterX":
                    RotateCenterX.Text = obj.LinkName;
                    break;
                case "RotateCenterY":
                    RotateCenterY.Text = obj.LinkName;
                    break;
                case "RotatePointX":
                    RotatePointX.Text = obj.LinkName;
                    break;
                case "RotatePointY":
                    RotatePointY.Text = obj.LinkName;
                    break;
                case "RotateAngle":
                    RotateAngle.Text = obj.LinkName;
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
                            case eLinkCommand.RotateCenterX:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "int,double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},RotateCenterX");
                                break;
                            case eLinkCommand.RotateCenterY:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "int,double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},RotateCenterY");
                                break;
                            case eLinkCommand.RotatePointX:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "int,double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},RotatePointX");
                                break;
                            case eLinkCommand.RotatePointY:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "int,double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},RotatePointY");
                                break;
                            case eLinkCommand.RotateAngle:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "int,double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},RotateAngle");
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
        private bool CalRotateNewPoint(double pt1X, double pt1Y, double CX, double CY, double angle, out double NewPointX, out double NewPointY)
        {
            NewPointX = 0;
            NewPointY = 0;
            try
            {
                double r = angle / 180 * PI;
                NewPointX = Math.Cos(r) * (pt1X - CX) - Math.Sin(r) * (pt1Y - CY) + CX;
                NewPointY = Math.Cos(r) * (pt1Y - CY) + Math.Sin(r) * (pt1X - CX) + CY;
                NewPointX = Math.Round(NewPointX, 3);
                NewPointY = Math.Round(NewPointY, 3);
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }
        #endregion
    }
}
