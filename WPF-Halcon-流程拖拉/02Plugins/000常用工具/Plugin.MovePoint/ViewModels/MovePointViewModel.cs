using EventMgrLib;
using Plugin.MovePoint.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.UI.WebControls;
using System.Windows.Controls;
using System.Windows.Media;
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

namespace Plugin.MovePoint.ViewModels
{
    #region enum
    public enum eLinkCommand
    {
        RunPosLink,
        RunVelLink,
        JogVelLink
    }
    public enum eOperateMode
    {
        连续运动,
        单次运动,
        移动距离
    }
    public enum eJogV
    {
        m0_001,
        m0_01,
        m0_1,
        m1,
        m5,
        m10,
        m20,
        m50,
    }
    #endregion

    [Category("常用工具")]
    [DisplayName("点位运动")]
    [ModuleImageName("MovePoint")]
    [Serializable]
    public class MovePointViewModel : ModuleBase
    {
        public override bool ExeModule()
        {
            Stopwatch.Restart();
            try
            {
                //switch (OperateMode)
                //{
                //    case eOperateMode.绝对运动:
                //        SelectedAxis.MoveAbs(Convert.ToDouble(GetLinkValue(RunPos)), Convert.ToDouble(GetLinkValue(RunVel)));
                //        if (NotWaitMotionFinish == false)
                //        {
                //            while (true)
                //            {
                //                Thread.Sleep(10);
                //                if (CancelWait || !SelectedAxis.Busy)
                //                {
                //                    break;
                //                }
                //            }
                //        }
                //        break;
                //    case eOperateMode.相对运动:
                //        SelectedAxis.MoveRel(Convert.ToDouble(GetLinkValue(RunPos)), Convert.ToDouble(GetLinkValue(RunVel)));
                //        if (NotWaitMotionFinish == false)
                //        {
                //            while (true)
                //            {
                //                Thread.Sleep(10);
                //                if (CancelWait || !SelectedAxis.Busy)
                //                {
                //                    break;
                //                }
                //            }
                //        }
                //        break;
                //    case eOperateMode.回零:
                //        SelectedAxis.Home();
                //        if (NotWaitMotionFinish == false)
                //        {
                //            while (true)
                //            {
                //                Thread.Sleep(10);
                //                if (CancelWait || SelectedAxis.HomeDone)
                //                {
                //                    break;
                //                }
                //            }
                //        }
                //        break;
                //    case eOperateMode.正向JOG:
                //        SelectedAxis.MoveJog(eDirection.Positive, Convert.ToDouble(GetLinkValue(JogVel)));
                //        break;
                //    case eOperateMode.负向JOG:
                //        SelectedAxis.MoveJog(eDirection.Negative, Convert.ToDouble(GetLinkValue(JogVel)));
                //        break;
                //    case eOperateMode.停止运动:
                //        SelectedAxis.Stop();
                //        break;
                //    default:
                //        break;
                //}
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
        public string[] OperateModes { get; set; } = Enum.GetNames(typeof(eOperateMode));
        private eOperateMode _OperateMode = eOperateMode.连续运动;
  

        public eOperateMode OperateMode
        {
            get { return _OperateMode; }
            set { _OperateMode = value; RaisePropertyChanged(); }
        }
        /// <summary>轴卡列表</summary>
        [field: NonSerialized]
        public ObservableCollection<MotionBase> MotionModels { get; set; } = HardwareConfigViewModel.Ins.MotionModels;
        private MotionBase _SelectedMotion = new MotionBase();

        private ObservableCollection<AxisParam> _Axis= new ObservableCollection<AxisParam>();
        public ObservableCollection<AxisParam> Axis
        {

            get {
                if ((MotionModels==null)||(MotionModels.Count() <=0)) return null; 
                if (_Axis.Count == 0 || _Axis.Count != MotionModels[0].Axis.Count)
                    _Axis =MotionModels[0].Axis;
                return _Axis;            
            }
            set
            {
                _Axis = value; RaisePropertyChanged();
            }
        }

        public MotionBase SelectedMotion
        {
            get { return _SelectedMotion; }
            set
            {
                _SelectedMotion = value; RaisePropertyChanged();
            }
        }
        private int _SelectedIndexMotion;

        public int SelectedIndexMotion
        {
            get { return _SelectedIndexMotion; }
            set { _SelectedIndexMotion = value; RaisePropertyChanged(); }
        }
        private AxisParam _SelectedAxis;

        public AxisParam SelectedAxis
        {
            get { return _SelectedAxis; }
            set
            {
                if (_SelectedAxis!=null)
                {
                    _SelectedAxis.Stop();
                } 
                _SelectedAxis = value;
                RaisePropertyChanged();
            }
        }
        private List<Double> _PtMoveToList=new List<double>();
        /// <summary>
        /// 目标位置
        /// </summary>
        public List<Double> PtMoveToList
        {
            get { return _PtMoveToList; }
            set { _PtMoveToList = value; RaisePropertyChanged(); }  
        }
        private List<bool > _AxisChecked = new List<bool>();
        /// <summary>
        /// 目标位置
        /// </summary>
        public List<bool> AxisChecked
        {
            get { return _AxisChecked; }
            set { _AxisChecked = value; RaisePropertyChanged(); }
        }
        private List<String> _OffSetLinkTextList = new List<string>();
        /// <summary>
        /// 偏移量
        /// </summary>
        public List<string> OffSetLinkTextList
        {
            get { return _OffSetLinkTextList; }
            set { _OffSetLinkTextList = value; RaisePropertyChanged(); }
        }
        private bool _IsRelMove;
        /// <summary>
        /// 使能相对运动
        /// </summary>
        public bool IsRelMove
        {
            get { return _IsRelMove; }
            set { Set(ref _IsRelMove, value); }
        }
        private LinkVarModel _JogVel = new LinkVarModel() { Value = 10 };
        /// <summary>
        /// Jog速度
        /// </summary>
        public LinkVarModel JogVel
        {
            get { return _JogVel; }
            set { _JogVel = value; RaisePropertyChanged(); }
        }
        private LinkVarModel _RunVel = new LinkVarModel() { Value = 10 };
        /// <summary>
        /// 移动速度
        /// </summary>
        public LinkVarModel RunVel
        {
            get { return _RunVel; }
            set { _RunVel = value; RaisePropertyChanged(); }
        }
        private LinkVarModel _RunPos = new LinkVarModel() { Value = 10 };
        /// <summary>
        /// 移动速度
        /// </summary>
        public LinkVarModel RunPos
        {
            get { return _RunPos; }
            set { _RunPos = value; RaisePropertyChanged(); }
        }

        #endregion

        #region Command
        public override void Loaded()
        {
            base.Loaded();
            ClosedView = true;
            MotionModels = HardwareConfigViewModel.Ins.MotionModels;
            if (MotionModels.Count > 0)
            {
                if (ModuleView != null)
                {
                    var view = ModuleView as MovePointView;
                    if (view != null && _SelectedAxis != null)
                    {
                       // view.gd.DataContext = _SelectedAxis;
                    }
                }
            }
            //for (int i = 0; i < Axis.Count; i++)
            //{
            //    if (PtMoveToList.Count < Axis.Count) PtMoveToList.Add(0);
            //    Axis[i].GoalPt = PtMoveToList[i];
            //    if (OffSetLinkTextList.Count < Axis.Count) OffSetLinkTextList.Add("");
            //    Axis[i].OffsetLinkText = OffSetLinkTextList[i];
            //    if (AxisChecked.Count < Axis.Count) AxisChecked.Add(false);
            //    Axis[i].AxisChecked = AxisChecked[i];
            //}
        }
        private void OnVarChanged(VarChangedEventParamModel obj)
        {
            eLinkCommand linkCommand = (eLinkCommand)Enum.Parse(typeof(eLinkCommand), obj.SendName.Split(',')[1]);
            switch (linkCommand)
            {
                case eLinkCommand.RunPosLink:
                    RunPos.Text = obj.LinkName;
                    break;
                case eLinkCommand.RunVelLink:
                    RunVel.Text = obj.LinkName;
                    break;
                case eLinkCommand.JogVelLink:
                    JogVel.Text = obj.LinkName;
                    break;
                default:
                    break;
            }
        }

        [NonSerialized]
        private CommandBase _MouseDownCommand;
        public CommandBase MouseDownCommand
        {
            get
            {
                if (_MouseDownCommand == null)
                {
                    _MouseDownCommand = new CommandBase((obj) =>
                    {
                        var view = ModuleView as MovePointView;
                        if (SelectedAxis == null || view.dg_AxisList.SelectedIndex != SelectedAxis.AxisID)return;
                        switch (obj.ToString())
                        {
                            case "Pot":
                                SelectedAxis.MoveJog(eDirection.Positive,3);
                                break;
                            case "Net":
                                SelectedAxis.MoveJog(eDirection.Negative, 3);
                                break;
                        }
                       
                    });
                }
                return _MouseDownCommand;
            }

        }
    
        [NonSerialized]
        private CommandBase _MouseUpCommand;
        public CommandBase MouseUpCommand
        {
            get
            {
             
                if (_MouseUpCommand == null)
                {
                    _MouseUpCommand = new CommandBase((obj) =>
                    {
                        var view = ModuleView as MovePointView;
                        if (SelectedAxis == null || view.dg_AxisList.SelectedIndex != SelectedAxis.AxisID) return;
                        SelectedAxis.Stop();
                    });
                }
                return _MouseUpCommand;
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
                            case eLinkCommand.RunPosLink:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},RunPosLink");
                                break;
                            case eLinkCommand.RunVelLink:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},RunVelLink");
                                break;
                            case eLinkCommand.JogVelLink:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "double");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},JogVelLink");
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
        private CommandBase _ConfirmCommand;
        public CommandBase ConfirmCommand
        {
            get
            {
                if (_ConfirmCommand == null)
                {
                    _ConfirmCommand = new CommandBase((obj) =>
                    {
                        var view = this.ModuleView as MovePointView;
                        if (view != null)
                        {
                            view.Close();
                        }
                    });
                }
                return _ConfirmCommand;
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

        #endregion
    }
}
