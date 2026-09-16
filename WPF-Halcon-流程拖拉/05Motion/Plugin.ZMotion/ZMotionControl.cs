using cszmcaux;
using VM.Start.Common.Enums;
using VM.Start.Common.Helper;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.Dialogs.Views;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.PersistentData;
using VM.Start.ViewModels;
using VM.Start.ViewModels.Dock;
using VM.Start.Views.Dock;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EventMgrLib;
using System.ComponentModel;

namespace Plugin.ZMotion
{
    [Category("轴卡")]
    [DisplayName("正运动")]
    [Serializable]
    public class ZMotionControl:MotionBase
    {
        #region const
        int AxisNum = 4;
        int DINum = 24;
        int DONum = 20;
        #endregion
        #region 属性单例模式

        public ZMotionControl()
        {
        }
        #endregion
        [OnDeserialized()]
        internal void OnDeserializedMethod(StreamingContext context)
        {
            Init();
        }

        public override void Init()
        {
            if (ZMotion_Handle == (IntPtr)0)
            {
                zmcaux.ZAux_Close(ZMotion_Handle);
            }
            zmcaux.ZAux_OpenEth(IPAddress, out ZMotion_Handle);
            if (ZMotion_Handle != (IntPtr)0)
            {
                if (MotionType == "ECI3428")
                {
                    AxisNum = 4;
                    DINum = 24;
                    DONum = 16;
                }
                else if (MotionType == "ECI3828")
                {
                    AxisNum = 8;
                    DINum = 24;
                    DONum = 20;
                }
                else if (MotionType == "ZMC408SCAN")
                {
                    AxisNum = 4;
                    DINum = 24;
                    DONum = 20;
                }

                if (axislist == null)
                {
                    axislist = new int[3]{ 0, 1, 2 };
                }
                EventMgr.Ins.GetEvent<SoftwareExitEvent>().Subscribe(Close);
                EventMgr.Ins.GetEvent<MotionConnectEvent>().Publish(true);
                EventMgr.Ins.GetEvent<HardwareChangedEvent>().Publish();
                Connected = true;
                //读取BAS文件中的变量判断是否有加载BAS文件
                int ret = zmcaux.ZAux_Direct_GetUserVar(ZMotion_Handle, "BUS_TYPE", ref Bus_type);
                if (ret == 0 && Bus_type != -1)
                {
                    IsLoadedBAS = true;//文件已经加载
                }
                else
                {
                    //下载到ROM
                    ret = zmcaux.ZAux_BasDown(ZMotion_Handle, FilePaths.ConfigFilePath + "ECAT初始化.bas", 1);
                    if (ret != 0)
                    {
                        Logger.AddLog("BAS文件下载失败!", eMsgType.Error);
                    }
                }
                Logger.AddLog("正运动控制卡连接成功!");
                //添加轴
                if (Axis == null || Axis.Count == 0)
                {
                    for (int i = 0; i < AxisNum; i++)
                    {
                        Axis.Add(new ZMotionAxis(ZMotion_Handle) { CardID = 0, AxisID = (short)(i), AxisName = $"{i + 1}轴" });
                    }
                }
                else
                {
                    foreach (var item in Axis)
                    {
                        item.ZMotion_Handle = ZMotion_Handle;
                    }
                }
                //添加输入
                if (DI == null || DI.Count == 0)
                {
                    for (int i = 0; i < DINum; i++)
                    {
                        DI.Add(new IOIn() { CardID = 0, InputID = (short)(i) });
                    }
                }
                //添加输出
                if (DO == null || DO.Count == 0)
                {
                    for (int i = 0; i < DONum; i++)
                    {
                        DO.Add(new ZMotionIOOut(ZMotion_Handle) { CardID = 0, OutputID = (short)(i) });
                    }
                }
                else
                {
                    foreach (var item in DO)
                    {
                        item.ZMotion_Handle = ZMotion_Handle;
                    }
                }
                foreach (var item in Axis)
                {
                    if (item.IsEnable)
                    {
                        item.Enable();
                        item.SetFeed(item.Feed);
                        item.SetAcc(item.Acc);
                        Thread.Sleep(200);
                    }
                }
                Task.Run(new Action(UpdateData));

            }
            else
            {
                Connected = false;
                EventMgr.Ins.GetEvent<MotionConnectEvent>().Publish(false);
                Logger.AddLog("正运动控制卡连接失败，请检查IP地址!",eMsgType.Error);
            }
        }

        public override void Close()
        {
            if (ZMotion_Handle != (IntPtr)0)
            {
                zmcaux.ZAux_Close(ZMotion_Handle);
                ZMotion_Handle = (IntPtr)0;
            }
        }
        public override void UpdateData()
        {
            int[] varInput = new int[2];
            uint[] varOutput = new uint[2];
            R_TRIG R_TRIG_TemperatureStart = new R_TRIG();
            R_TRIG R_TRIG_TemperatureStop = new R_TRIG();
            int err = 0;
            while (true)
            {
                Thread.Sleep(5);
                try
                {
                    //轴数据更新
                    foreach (var item in Axis)
                    {
                        if (item.IsEnable)
                        {
                            item.UpdateData();
                        }
                    }
                    //输入数据更新
                    err = zmcaux.ZAux_Direct_GetInMulti(ZMotion_Handle, 0, DINum-1, varInput);
                    for (int i = 0; i < DI.Count; i++)
                    {
                        DI[i].State = (varInput[0] & 1 << i) != 0 ? true : false;
                    }
                    //输出数据更新
                    err = zmcaux.ZAux_Direct_GetOutMulti(ZMotion_Handle, 0, (ushort)(DONum - 1), varOutput);
                    for (int i = 0; i < DO.Count; i++)
                    {
                        DO[i].State = (varOutput[0] & 1 << i) != 0 ? true : false;
                    }
                    #region 信号监控
                    #endregion
                }
                catch (Exception ex)
                {
                }
            }
        }
        #region 插补运动
        int[] axislist = { 0, 1, 2 };
        public void Init_Interpolate()
        {
            int ret = 0;
            StopInterpolateFlag = false;
            ret += zmcaux.ZAux_Direct_Base(ZMotion_Handle, 3, axislist); //选择运动轴列表
            ret += zmcaux.ZAux_Direct_SetMerge(ZMotion_Handle, axislist[0], 1);//设置连续插补
            if (ret != 0)
            {
                Logger.AddLog("[插补]插补初始化失败!", eMsgType.Error);
            }
        }
        public void AddCircle(InterpolateDataModel data)
        {
            CheckRemainBuffer();
            if (StopInterpolateFlag) return;
            int ret = 0;
            ret += zmcaux.ZAux_Direct_SetForceSpeed(ZMotion_Handle, axislist[0], data.Velocity1);
            ret += zmcaux.ZAux_Direct_MoveCircAbsSp(ZMotion_Handle, 2, axislist,
                (float)data.CircleEndPoint.X, (float)data.CircleEndPoint.Y, //圆弧终止点
                (float)data.CircleCenter.X, (float)data.CircleCenter.Y, 1); //圆弧圆心
            if (ret != 0)
            {
                Logger.AddLog("[插补]圆弧插入失败!", eMsgType.Error);
            }
        }
        public void AddLine(InterpolateDataModel data, bool isNotCheckStopInterpolateFlag = false)
        {
            CheckRemainBuffer();
            if (isNotCheckStopInterpolateFlag == false)
            {
                if (StopInterpolateFlag) return;
            }
            int ret = 0;
            ret += zmcaux.ZAux_Direct_SetForceSpeed(ZMotion_Handle, axislist[0], data.Velocity1);
            float[] pos = { data.LineEndPoint.X, data.LineEndPoint.Y, data.LineEndPoint.Z };
            ret += zmcaux.ZAux_Direct_MoveAbsSp(ZMotion_Handle, 3, axislist, pos);
            if (ret != 0)
            {
                Logger.AddLog("[插补]直线插入失败!", eMsgType.Error);
            }
        }
        public void AddDelay(InterpolateDataModel data)
        {
            CheckRemainBuffer();
            if (StopInterpolateFlag) return;
            int ret = 0;
            ret += zmcaux.ZAux_Direct_MoveDelay(ZMotion_Handle, axislist[0], data.DelayTime);
            if (ret != 0)
            {
                Logger.AddLog("[插补]延时插入失败!", eMsgType.Error);
            }
        }
        public void AddInput(InterpolateDataModel data)
        {
            CheckRemainBuffer();
            if (StopInterpolateFlag) return;
            int ret = 0;
            ret += zmcaux.ZAux_Direct_MoveWait(ZMotion_Handle, (uint)axislist[0], "IN", data.IoIndex, 1, data.IoState == true ? 1 : 0);
            if (ret != 0)
            {
                Logger.AddLog("[插补]输入插入失败!", eMsgType.Error);
            }
        }
        public void AddOutput(InterpolateDataModel data)
        {
            CheckRemainBuffer();
            if (StopInterpolateFlag) return;
            int ret = 0;
            ret += zmcaux.ZAux_Direct_MoveOp(ZMotion_Handle, axislist[0], data.IoIndex, data.IoState == true ? 1 : 0);
            if (ret != 0)
            {
                Logger.AddLog("[插补]输出插入失败!", eMsgType.Error);
            }
        }
        public void Stop_Interpolate()
        {
            StopInterpolateFlag = true;
            int ret = 0;
            ret += zmcaux.ZAux_Direct_Single_Cancel(ZMotion_Handle, axislist[0], 2);
            if (ret != 0)
            {
                Logger.AddLog("[插补]停止插补失败!", eMsgType.Error);
            }
        }
        public void CheckRemainBuffer()
        {
            int ret = 0;
            int remainBufferNum = 0;
            while (true)
            {
                ret += zmcaux.ZAux_Direct_GetRemain_LineBuffer(ZMotion_Handle, axislist[0], ref remainBufferNum);
                if (remainBufferNum > 20 || StopInterpolateFlag == true)
                {
                    break;
                }
                Thread.Sleep(5);
            }
        }
        public bool CheckInterpolateFinish()
        {
            int ret = 0;
            int idle = 0;
            int bufferNum = 0;
            int haveBuffer = 0;
            ret += zmcaux.ZAux_Direct_GetLoaded(ZMotion_Handle, axislist[0], ref haveBuffer);
            ret += zmcaux.ZAux_Direct_GetIfIdle(ZMotion_Handle, axislist[0], ref idle);
            ret += zmcaux.ZAux_Direct_GetMovesBuffered(ZMotion_Handle, axislist[0], ref bufferNum);
            if (ret != 0)
            {
                Logger.AddLog("[插补]检查插补完成状态失败!", eMsgType.Error);
            }
            if (idle < 0 && bufferNum == 0 && haveBuffer < 0)//判断插补数量为0并且插补状态结束
            {
                return true;
            }
            return false;
        }

        #endregion

        #region 手轮操作相关
        /// <summary>
        /// 设置手轮模式并且链接手轮
        /// </summary>
        /// <param name="SpeedScoreSelected">速度倍率</param>
        /// <param name="AxisSelected">要关联的轴（从轴）</param>
        /// <param name="Axis">手轮轴号（主轴）</param>
        /// <param name="units">轴走一个单位要多少脉冲</param>
        public void ConnectEcoder(int SpeedScoreSelected, int AxisSelected, int AxisCurrent, int Axis = 3, int units = 1000)
        {
            if (ZMotion_Handle != (IntPtr)0)
            {

                zmcaux.ZAux_Direct_Single_Cancel(ZMotion_Handle, AxisCurrent, 3);       //取消主轴运动

                Thread.Sleep(300);
                zmcaux.ZAux_Direct_SetAtype(ZMotion_Handle, Axis, 3);                //轴类型 脉冲方向式编码器
                zmcaux.ZAux_Direct_SetUnits(ZMotion_Handle, 3, units);              //脉冲当量 1 脉冲为单位
                zmcaux.ZAux_Direct_Connect(ZMotion_Handle, SpeedScoreSelected, Axis, AxisSelected);
            }

        }
        /// <summary>
        ///断开
        /// </summary>
        /// <param name="MainAxis">主轴号</param>
        public void DisConnectEcoder(int AxisCurrent)
        {
            if (ZMotion_Handle != (IntPtr)0)
            {
                zmcaux.ZAux_Direct_Single_Cancel(ZMotion_Handle, AxisCurrent, 3);        //取消主轴运动
            }
        }

        #endregion
    }
    [Serializable]
    public class ZMotionIOOut : IOOut
    {
        public ZMotionIOOut(IntPtr handle)
        {
            ZMotion_Handle = handle;
        }
        public override bool SetValue(bool setValue)
        {
            int err = 0;
            uint value = setValue == true ? (uint)1: (uint)0;
            err += zmcaux.ZAux_Direct_SetOp(ZMotion_Handle, OutputID, value);
            if (err != 0)
            {
                Logger.AddLog($"[报错]设置输出{OutputID}出错，出错码({err})", eMsgType.Error);
                return false;
            }
            return true;
        }
    }

}
