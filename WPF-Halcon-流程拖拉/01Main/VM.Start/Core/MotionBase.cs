using HalconDotNet;
using MahApps.Metro.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using VM.Halcon.Config;
using VM.Start.Common.Enums;
using VM.Start.Common.Helper;
using VM.Start.Common.Provide;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.Services;
using VM.Start.Views;

namespace VM.Start.Core
{
    [Serializable]
    public class MotionBase : NotifyPropertyBase
    {
        #region Prop
        protected Guid ModuleGuid = Guid.NewGuid();
        public bool ClosedView = false;
        private ModuleParam _ModuleParam;
        /// <summary>
        /// 模块参数
        /// </summary>
        [Browsable(false)]
        public ModuleParam ModuleParam
        {
            get
            {
                if (_ModuleParam == null)
                {
                    _ModuleParam = new ModuleParam();
                }
                return _ModuleParam;
            }

            set { _ModuleParam = value; }
        }
        /// <summary>停止插补 </summary>
        public bool StopInterpolateFlag = false;
        /// <summary>最新编号 </summary>
        public static int LastNo = 0;
        /// <summary>设备自己编号 </summary>
        [Category("轴卡"), Description("编号"), DisplayName("编号")]
        public string MotionNo { set; get; }
        /// <summary>备注</summary>
        [Category("轴卡"), Description("备注"), DisplayName("备注")]
        public string Remarks { get; set; }
        private bool _Connected = false;

        /// <summary>初始连接状态</summary>

        [Category("轴卡"), Description("连接状态"), DisplayName("连接状态")]
        public bool Connected
        {
            get { return _Connected; }
            set { Set(ref _Connected, value); }
        }

        [field: NonSerialized()]
        [Browsable(false)]
        public ModuleViewBase ModuleView { get; set; }
        [field: NonSerialized()]
        [Browsable(false)]
        public List<string> MotionTypes { get; set; }
        private string _MotionType;
        /// <summary>
        /// 轴卡型号
        /// </summary>
        [Category("轴卡"), Description("轴卡型号"), DisplayName("轴卡型号")]
        public string MotionType
        {
            get { return _MotionType; }
            set
            {
                Set(ref _MotionType, value);
            }
        }
        [NonSerialized]
        private Stopwatch _Stopwatch;
        [Browsable(false)]
        public Stopwatch Stopwatch
        {
            get
            {
                if (_Stopwatch == null)
                {
                    _Stopwatch = new Stopwatch();
                }
                return _Stopwatch;
            }
            set { _Stopwatch = value; }
        }

        [NonSerialized]
        private Project _Prj;
        [Browsable(false)]
        public Project Prj
        {
            get
            {
                if (_Prj == null)
                {
                    _Prj = Solution.Ins.GetProjectById(ModuleParam.ProjectID);
                }
                return _Prj;
            }
            set { _Prj = value; }
        }

        private int _TimeOut = 5000;
        /// <summary>
        /// 超时时间ms
        /// </summary>
        [Browsable(false)]
        public int TimeOut
        {
            get { return _TimeOut; }
            set
            {
                Set(ref _TimeOut, value);
            }
        }
        [NonSerialized]
        /// <summary>
        /// 正运动链接返回句柄,可作为卡号
        /// </summary>
        public IntPtr ZMotion_Handle;
        /// <summary>
        /// BAS文件中变量判断总线类型，也作为BAS文件是否下载成功判断
        /// </summary>
        public float Bus_type = -1;
        public bool IsLoadedBAS = false;
        /// <summary>
        /// IP地址
        /// </summary>
        [Category("轴卡"), Description("IP地址"), DisplayName("IP地址")]
        public string IPAddress { get; set; } = "192.168.0.11";
        /// <summary>
        /// 端口号
        /// </summary>
        [Category("轴卡"), Description("端口号"), DisplayName("端口号")]

        public int Port { get; set; }

        [Category("轴卡"), Description("轴数组"), DisplayName("轴数组")]
        public ObservableCollection<AxisParam> Axis { get; set; } = new ObservableCollection<AxisParam>();

        [Category("轴卡"), Description("输入数组"), DisplayName("输入数组")]
        public ObservableCollection<IOIn> DI { get; set; } = new ObservableCollection<IOIn>();

        [Category("轴卡"), Description("输出数组"), DisplayName("输出数组")]
        public ObservableCollection<IOOut> DO { get; set; } = new ObservableCollection<IOOut>();


        #endregion
        #region Command
        [NonSerialized]
        private CommandBase _ConnectCommand;
        [Browsable(false)]
        public CommandBase ConnectCommand
        {
            get
            {
                if (_ConnectCommand == null)
                {
                    _ConnectCommand = new CommandBase((obj) =>
                    {
                        Init();
                        EventMgrLib.EventMgr.Ins.GetEvent<HardwareChangedEvent>().Publish();
                    });
                }
                return _ConnectCommand;
            }
        }
        #endregion
        #region Method
        /// <summary>
        /// 加载视图
        /// </summary>
        public virtual void Loaded()
        {
            if (ModuleView!=null)
            {
                ModuleView.IsClosed = false;
            }
        }
        /// <summary>
        /// 添加模块输出参数
        /// </summary>
        /// <returns></returns>
        public virtual void AddOutputParams()
        {
            AddOutputParam("状态", "bool", ModuleParam.Status == eRunStatus.OK ? true : false);
            AddOutputParam("时间", "int", ModuleParam.ElapsedTime);
        }
        public virtual void SetDefaultLink()
        {

        }
        public object GetLinkValue(LinkVarModel linkVar)
        {
            object value = null;
            if (linkVar.Text.StartsWith("&"))
            {
                value = Prj.GetParamByName(linkVar.Text).Value;
            }
            else
            {
                value = linkVar.Text;
            }
            return value;
        }
        /// <summary>
        /// 输出变量
        /// </summary>
        protected void AddOutputParam(string varName, string varType, object obj)
        {
            Prj.AddOutputParam(ModuleParam, varName,varType, obj);
        }

        protected void ChangeModuleRunStatus(eRunStatus runStatus)
        {
            ModuleParam.Status = runStatus;
            Stopwatch.Stop();
            ModuleParam.ElapsedTime = Stopwatch.ElapsedMilliseconds;
            if (runStatus == eRunStatus.OK)
            {
                Logger.AddLog($"流程[{Solution.Ins.GetProjectById(ModuleParam.ProjectID).ProjectInfo.ProcessName}]执行[{ModuleParam.ModuleName}]模块成功，耗时{ModuleParam.ElapsedTime}ms.");
            }
            else
            {
                Logger.AddLog($"流程[{Solution.Ins.GetProjectById(ModuleParam.ProjectID).ProjectInfo.ProcessName}]执行[{ModuleParam.ModuleName}]模块失败，耗时{ModuleParam.ElapsedTime}ms.", eMsgType.Warn);
            }
            AddOutputParams();
        }
        public virtual bool ExeModule() { return false; }
        public virtual void Init() { }
        public virtual void Close() { }
        public virtual void UpdateData() { }

        #endregion


    }
    [Serializable]
    public abstract class AxisParam : NotifyPropertyBase
    {
        #region 轴参数
        private int _EncoderResolution = 8388608;
        /// <summary>
        /// 轴名称
        /// </summary>
        [Category("轴参数"), Description("编码器分辨率"), DisplayName("编码器分辨率")]
        public int EncoderResolution
        {
            get { return _EncoderResolution; }
            set { Set(ref _EncoderResolution, value, new Action(() => SetEncoderResolution(_EncoderResolution))); }
        }
        private bool _IsEnable = true;
        /// <summary>
        /// 轴名称
        /// </summary>
        [Category("轴参数"), Description("是否使用轴"), DisplayName("是否使用轴")]
        public bool IsEnable
        {
            get { return _IsEnable; }
            set { Set(ref _IsEnable, value); }
        }

        private string _AxisName = "X轴";
        /// <summary>
        /// 轴名称
        /// </summary>
        [Category("轴参数"), Description("轴名称"), DisplayName("轴名称")]
        public string AxisName
        {
            get { return _AxisName; }
            set { Set(ref _AxisName, value); }
        }
        private string _Unit = "mm";
        /// <summary>
        /// 单位
        /// </summary>
        [Category("轴参数"), Description("单位"), DisplayName("单位")]
        public string Unit
        {
            get { return _Unit; }
            set { Set(ref _Unit, value); }
        }
        private bool _IsRelMove ;
        /// <summary>
        /// 使能相对运动
        /// </summary>
        [Browsable(false)]
        public bool IsRelMove
        {
            get { return _IsRelMove; }
            set { Set(ref _IsRelMove, value); }
        }
        /// <summary>
        /// 正运动链接返回句柄,可作为卡号
        /// </summary>
        [NonSerialized]
        public IntPtr ZMotion_Handle;
        private int _CardID = 0;
        /// <summary>
        /// 板卡ID
        /// </summary>
        [Category("轴参数"), Description("卡ID"), ReadOnly(true), DisplayName("卡ID")]
        public int CardID
        {
            get { return _CardID; }
            set { Set(ref _CardID, value); }
        }
        private short _AxisID = 0;
        /// <summary>
        /// 轴ID
        /// </summary>
        [Category("轴参数"), Description("轴ID"), ReadOnly(true), DisplayName("轴ID")]
        public short AxisID
        {
            get { return _AxisID; }
            set { Set(ref _AxisID, value); }
        }
        private int _Feed = 1000;
        /// <summary>
        /// 脉冲当量
        /// </summary>
        [Category("轴参数"), Description("脉冲当量，电机旋转一圈的脉冲数"), DisplayName("脉冲当量")]
        public int Feed
        {
            get { return _Feed; }
            set { Set(ref _Feed, value, new Action(() => SetFeed(_Feed))); }
        }
        private double _Screw = 5;
        /// <summary>
        /// 螺距
        /// </summary>
        [Category("轴参数"), Description("螺距"), DisplayName("螺距")]
        public double Screw
        {
            get { return _Screw; }
            set { Set(ref _Screw, value); }
        }
        private double _RunVel = 5;
        /// <summary>
        /// 运行速度
        /// </summary>
        [Category("轴参数"), Description("运行速度"), DisplayName("运行速度")]
        public double RunVel
        {
            get { return _RunVel; }
            set { Set(ref _RunVel, value); }
        }
        private float _JogVel = 1;
        /// <summary>
        /// JOG速度
        /// </summary>
        [Category("轴参数"), Description("JOG速度"), DisplayName("JOG速度")]
        public float JogVel
        {
            get { return _JogVel; }
            set { Set(ref _JogVel, value); }
        }
        private double _RunPos = 1;
        /// <summary>
        /// 设置运行位置
        /// </summary>
        [Category("轴参数"), Description("运行位置"), DisplayName("运行位置")]
        public double RunPos
        {
            get { return _RunPos; }
            set { Set(ref _RunPos, value); }
        }
        private double _SetVel = 1;
        /// <summary>
        /// 设置运行速度
        /// </summary>
        [Category("轴参数"), Description("运行速度"), DisplayName("运行速度")]
        public double SetVel
        {
            get { return _SetVel; }
            set { Set(ref _SetVel, value); }
        }

        #endregion
        #region 速度
        private double _Acc = 1000;
        [Category("速度"), Description("加速度"), DisplayName("加速度")]
        public double Acc
        {
            get { return _Acc; }
            set { Set(ref _Acc, value, new Action(() => SetAcc(_Acc))); }
        }
        private short _SmoothTime = 25;
        [Category("速度"), Description("平滑时间,单位ms"), DisplayName("平滑时间")]
        public short SmoothTime
        {
            get { return _SmoothTime; }
            set { Set(ref _SmoothTime, value); }
        }
        private double _MaxVel = 1000;
        [Category("速度"), Description("最大速度"), DisplayName("最大速度")]
        public double MaxVel
        {
            get { return _MaxVel; }
            set { Set(ref _MaxVel, value); }
        }
        private double _MinVel = 0;
        [Category("速度"), Description("最小速度"), DisplayName("最小速度")]
        public double MinVel
        {
            get { return _MinVel; }
            set { Set(ref _MinVel, value); }
        }
        private double _MaxAcc = 1000;
        [Category("速度"), Description("最大加速度"), DisplayName("最大加速度")]
        public double MaxAcc
        {
            get { return _MaxAcc; }
            set { Set(ref _MaxAcc, value); }
        }
        private double _MinAcc = 100;
        [Category("速度"), Description("最小加速度"), DisplayName("最小加速度")]
        public double MinAcc
        {
            get { return _MinAcc; }
            set { Set(ref _MinAcc, value); }
        }
        private double _StopVel = 1000;
        [Category("速度"), Description("停止速度"), DisplayName("停止速度")]
        public double StopVel
        {
            get { return _StopVel; }
            set { Set(ref _StopVel, value); }
        }

        #endregion
        #region 回零
        private eHomeMode _HomeMode = eHomeMode.负极限_Index;
        [Category("回零"), Description("回零模式"), DisplayName("回零模式")]
        public eHomeMode HomeMode
        {
            get { return _HomeMode; }
            set { Set(ref _HomeMode, value); }
        }

        private bool _HomeDir;
        [Category("回零"), Description("回零方向"), DisplayName("回零方向")]
        public bool HomeDir
        {
            get { return _HomeDir; }
            set { Set(ref _HomeDir, value); }
        }
        private double _HomeLowVel = 8;
        [Category("回零"), Description("回零低速"), DisplayName("回零低速")]
        public double HomeLowVel
        {
            get { return _HomeLowVel; }
            set { Set(ref _HomeLowVel, value); }
        }
        private double _HomeHighVel = 30;
        [Category("回零"), Description("回零高速"), DisplayName("回零高速")]
        public double HomeHighVel
        {
            get { return _HomeHighVel; }
            set { Set(ref _HomeHighVel, value); }
        }
        private double _HomeAcc = 1000;
        [Category("回零"), Description("回零加速度"), DisplayName("回零加速度")]
        public double HomeAcc
        {
            get { return _HomeAcc; }
            set { Set(ref _HomeAcc, value); }
        }
        private double _HomeOffset;
        [Category("回零"), Description("回零偏移"), DisplayName("回零偏移")]
        public double HomeOffset
        {
            get { return _HomeOffset; }
            set { Set(ref _HomeOffset, value); }
        }
        [NonSerialized]
        private string _HomeMsg = "等待回零！";
        /// <summary>
        /// 到位信号
        /// </summary>
        [Browsable(false)]
        public string HomeMsg
        {
            get { return _HomeMsg; }
            set { Set(ref _HomeMsg, value); }
        }

        #endregion
        #region 轴状态

        #region VM框架新增加参数
        private double _GoalPt;
        [Browsable(false)]
        public double GoalPt
        {
            get { return _GoalPt; }
            set { _GoalPt = value; RaisePropertyChanged(); }
        }
        private string _OffsetLinkText;
        [Browsable(false)]
        public string OffsetLinkText
        {
            get { return _OffsetLinkText; }
            set { _OffsetLinkText = value; RaisePropertyChanged(); }
        }
        private bool _AxisChecked=false;
        public bool AxisChecked
        {
            get { return _AxisChecked; }
            set { _AxisChecked = value;}
        }
        #endregion

        [NonSerialized]
        private double _CurPos;
        /// <summary>
        /// 当前位置
        /// </summary>
        [Browsable(false)]
        public double CurPos
        {
            get { return _CurPos; }
            set { Set(ref _CurPos, value); }
        }
        [NonSerialized]
        private double _CurVel;
        /// <summary>
        /// 当前速度
        /// </summary>
        [Browsable(false)]
        public double CurVel
        {
            get { return _CurVel; }
            set { Set(ref _CurVel, value); }
        }
        [NonSerialized]
        private double _CurTorque;
        /// <summary>
        /// 当前力矩
        /// </summary>
        [Browsable(false)]
        public double CurTorque
        {
            get { return _CurTorque; }
            set { Set(ref _CurTorque, value); }
        }
        [NonSerialized]
        private bool _SvOn;
        /// <summary>
        /// 伺服使能
        /// </summary>
        [Browsable(false)]
        public bool SvOn
        {
            get { return _SvOn; }
            set { Set(ref _SvOn, value); }
        }
        [NonSerialized]
        private bool _Org;
        /// <summary>
        /// 原点信号
        /// </summary>
        [Browsable(false)]
        public bool Org
        {
            get { return _Org; }
            set { Set(ref _Org, value); }
        }
        private bool _Pot;
        /// <summary>
        /// 正限位信号
        /// </summary>
        [Browsable(false)]
        public bool Pot
        {
            get { return _Pot; }
            set { Set(ref _Pot, value); }
        }
        private bool _Net;
        /// <summary>
        /// 负限位信号
        /// </summary>
        [Browsable(false)]
        public bool Net
        {
            get { return _Net; }
            set { Set(ref _Net, value); }
        }
        [NonSerialized]
        private bool _Alm;
        /// <summary>
        /// 报警
        /// </summary>
        [Browsable(false)]
        public bool Alm
        {
            get { return _Alm; }
            set { Set(ref _Alm, value); }
        }
        [NonSerialized]
        private bool _Emg;
        /// <summary>
        /// 急停
        /// </summary>
        [Browsable(false)]
        public bool Emg
        {
            get { return _Emg; }
            set { Set(ref _Emg, value); }
        }
        [NonSerialized]
        private bool _Busy;
        /// <summary>
        /// 运动中信号
        /// </summary>
        [Browsable(false)]
        public bool Busy
        {
            get { return _Busy; }
            set { Set(ref _Busy, value); }
        }
        [NonSerialized]
        private bool _Inp;
        /// <summary>
        /// 到位信号
        /// </summary>
        [Browsable(false)]
        public bool Inp
        {
            get { return _Inp; }
            set { Set(ref _Inp, value); }
        }
        [NonSerialized]
        private bool _HomeDone;
        /// <summary>
        /// 回零完成信号
        /// </summary>
        [Browsable(false)]
        public bool HomeDone
        {
            get { return _HomeDone; }
            set { Set(ref _HomeDone, value); }
        }

        #endregion
        #region 参数设置&读取命令
        /// <summary>
        /// 设置编码器分辨率
        /// </summary>
        /// <param name="iaxis">分辨率</param>
        /// <returns></returns>
        public virtual bool SetEncoderResolution(int value) { return false; }
        /// <summary>
        /// 设置脉冲当量
        /// </summary>
        /// <param name="iaxis">轴ID,从0开始</param>
        /// <returns></returns>
        public virtual bool SetFeed(float fValue) { return false; }
        /// <summary>
        /// 读取脉冲当量
        /// </summary>
        /// <param name="iaxis">轴ID,从0开始</param>
        /// <returns></returns>
        public virtual bool GetFeed(ref float pfValue) { return false; }
        /// <summary>
        /// 设置加速度
        /// </summary>
        /// <param name="handle"></param>
        /// <param name="iaxis">轴ID,从0开始</param>
        /// <param name="pfValue"></param>
        /// <returns></returns>
        public virtual bool SetAcc(double fValue) { return false; }
        /// <summary>
        /// 设置减速度
        /// </summary>
        /// <param name="handle"></param>
        /// <param name="iaxis">轴ID,从0开始</param>
        /// <param name="pfValue"></param>
        /// <returns></returns>
        public virtual bool SetDec(double fValue) { return false; }
        #endregion
        #region 常用命令
        /// <summary>
        /// 设置加减速度
        /// </summary>
        /// <returns></returns>
        public virtual bool SetAcc(int feed) { return false; }

        /// <summary>
        /// 轴上使能
        /// </summary>
        /// <returns></returns>
        public abstract bool Enable();
        /// <summary>
        /// 轴下使能
        /// </summary>
        /// <returns></returns>
        public abstract bool Disable();
        /// <summary>
        /// 轴上使能
        /// </summary>
        /// <returns></returns>
        public abstract void Home();
        public abstract void UpdateData();
        /// <summary>
        /// 清除驱动器报警
        /// </summary>
        /// <param name="mode">模式0-清除当前 1-清除历史 2-清除外部输入警告</param>
        /// <returns></returns>
        public abstract bool ClearAlm(UInt32 mode = 0);
        public abstract bool MoveAbs(double pos, double vel);
        public abstract bool MoveRel(double pos, double vel);
        public abstract bool MoveJog(eDirection dir, double vel);
        /// <summary>
        /// 单轴运动停止
        /// </summary>
        /// <param name="mode">0(缺省)取消当前运动1-取消缓冲的运动2-取消当前运动和缓冲运动3-立即中断脉冲发送</param>
        /// <returns></returns>
        public abstract bool Stop(int mode = 2);
        #endregion
    }
    [Serializable]
    public class IOIn : NotifyPropertyBase
    {
        private int _CardID = 0;
        /// <summary>
        /// 板卡ID
        /// </summary>
        [Category("输入参数"), Description("卡ID"), ReadOnly(true), DisplayName("卡ID")]
        public int CardID
        {
            get { return _CardID; }
            set { Set(ref _CardID, value); }
        }
        private short _InputID = 0;
        /// <summary>
        /// 轴ID
        /// </summary>
        [Category("输入参数"), Description("输入ID"), ReadOnly(true), DisplayName("输入ID")]
        public short InputID
        {
            get { return _InputID; }
            set { Set(ref _InputID, value); }
        }
        private string _Name = "预留";
        /// <summary>
        /// 输入名称
        /// </summary>
        [Category("输入参数"), Description("输入名称"), DisplayName("输入名称")]
        public string Name
        {
            get { return _Name; }
            set { Set(ref _Name, value); }
        }
        [NonSerialized]
        private bool _State = false;
        /// <summary>
        /// 状态值
        /// </summary>
        [Browsable(false)]
        public bool State
        {
            get { return _State; }
            set { Set(ref _State, value); }
        }

    }
    [Serializable]
    public class IOOut : NotifyPropertyBase
    {
        [NonSerialized]
        /// <summary>
        /// 正运动链接返回句柄,可作为卡号
        /// </summary>
        public IntPtr ZMotion_Handle;

        private int _CardID = 0;
        /// <summary>
        /// 板卡ID
        /// </summary>
        [Category("输出参数"), Description("卡ID"), ReadOnly(true), DisplayName("卡ID")]
        public int CardID
        {
            get { return _CardID; }
            set { Set(ref _CardID, value); }
        }
        private short _OutputID = 0;
        /// <summary>
        /// 轴ID
        /// </summary>
        [Category("输出参数"), Description("输出ID"), ReadOnly(true), DisplayName("输出ID")]
        public short OutputID
        {
            get { return _OutputID; }
            set { Set(ref _OutputID, value); }
        }
        private string _Name = "预留";
        /// <summary>
        /// 输出名称
        /// </summary>
        [Category("输出参数"), Description("输出名称"), DisplayName("输出名称")]
        public string Name
        {
            get { return _Name; }
            set { Set(ref _Name, value); }
        }
        [NonSerialized]
        private bool _Value = false;
        /// <summary>
        /// 设定值
        /// </summary>
        [Browsable(false)]
        public bool Value
        {
            get { return _Value; }
            set { Set(ref _Value, value, new Action(() => SetValue(_Value))); }
        }
        [NonSerialized]
        private bool _State;
        /// <summary>
        /// 状态值
        /// </summary>
        [Browsable(false)]
        public bool State
        {
            get { return _State; }
            set { Set(ref _State, value); }
        }

        [NonSerialized]
        private bool _IsForce = false;
        /// <summary>
        /// 输出强制
        /// </summary>
        [Category("输出参数"), Description("输出强制"), DisplayName("输出强制")]
        public bool IsForce
        {
            get { return _IsForce; }
            set { Set(ref _IsForce, value); }
        }
        /// <summary>
        /// 设定值
        /// </summary>
        /// <param name="setValue"></param>
        public virtual bool SetValue(bool setValue)
        {
            return false;
        }
        [NonSerialized]
        private CommandBase _MouseUpCommand;
        [Browsable(false)]
        public CommandBase MouseUpCommand
        {
            get
            {
                if (_MouseUpCommand == null)
                {
                    _MouseUpCommand = new CommandBase((obj) =>
                    {
                        if (IsForce)
                        {
                            if (State)
                            {
                                SetValue(false);
                            }
                            else
                            {
                                SetValue(true);
                            }
                        }
                    });
                }
                return _MouseUpCommand;
            }
        }

    }

}
