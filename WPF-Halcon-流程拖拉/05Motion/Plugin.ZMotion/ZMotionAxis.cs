using cszmcaux;
using VM.Start.Common;
using VM.Start.Common.Enums;
using VM.Start.Common.Provide;
using VM.Start.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Plugin.ZMotion
{
    [Serializable]
    public class ZMotionAxis : AxisParam
    {

        public ZMotionAxis(IntPtr handle)
        {
            ZMotion_Handle = handle;
        }
        #region Prop
        private bool StopHome = false;
        #endregion
        #region 轴运动
        public override bool ClearAlm(uint mode = 0)
        {
            if (ZMotion_Handle == (IntPtr)0)
            {
                Logger.AddLog("未链接到控制器!", eMsgType.Error);
                return false;
            }
            int ret = zmcaux.ZAux_BusCmd_DriveClear(ZMotion_Handle, (uint)AxisID, 0);
            if (ret != 0)
            {
                Logger.AddLog($"清除总线伺服报警出错，出错码{ret}!", eMsgType.Error);
            }
            return true;
        }

        public override bool Disable()
        {
            if (ZMotion_Handle == (IntPtr)0)
            {
                Logger.AddLog("未链接到控制器!", eMsgType.Error);
                return false;
            }
            int ret = zmcaux.ZAux_Direct_SetAxisEnable(ZMotion_Handle, AxisID, 0);
            if (ret != 0)
            {
                Logger.AddLog($"{AxisName}下使能出错，出错码{ret}!", eMsgType.Error);
            }
            return true;
        }

        public override bool Enable()
        {
            if (ZMotion_Handle == (IntPtr)0)
            {
                Logger.AddLog("未链接到控制器!", eMsgType.Error);
                return false;
            }
            int ret = zmcaux.ZAux_Direct_SetAxisEnable(ZMotion_Handle, AxisID, 1);
            if (ret != 0)
            {
                Logger.AddLog($"{AxisName}上使能出错，出错码{ret}!", eMsgType.Error);
            }
            return true;
        }

        public override void Home()
        {
            HomeDone = false;
            HomeMsg = "开始回零...";
            if (ZMotion_Handle == (IntPtr)0)
            {
                Logger.AddLog("未链接到控制器!", eMsgType.Error);
            }
            Stopwatch stopwatch_HomeTimeout = new Stopwatch();
            Task.Run(() =>
            {
                int ret = 0;
                ret += zmcaux.ZAux_Direct_SetSpeed(ZMotion_Handle, AxisID, (float)HomeHighVel);
                ret += zmcaux.ZAux_Direct_SetCreep(ZMotion_Handle, AxisID, (float)HomeLowVel);
                ret += zmcaux.ZAux_BusCmd_SetDatumOffpos(ZMotion_Handle, (uint)AxisID, (float)HomeOffset);
                uint homeMode = 1;
                switch (HomeMode)
                {
                    case eHomeMode.负极限_原点:
                        homeMode = 27;
                        break;
                    case eHomeMode.正极限_原点:
                        homeMode = 23;
                        break;
                    case eHomeMode.原点:
                        homeMode = 19;
                        break;
                    case eHomeMode.负极限_Index:
                        homeMode = 1;
                        break;
                    case eHomeMode.正极限_Index:
                        homeMode = 2;
                        break;
                    case eHomeMode.零位置预设:
                        homeMode = 37;
                        break;
                    case eHomeMode.负极限:
                        homeMode = 17;
                        break;
                    case eHomeMode.正极限:
                        homeMode = 18;
                        break;
                    default:
                        break;
                }
                stopwatch_HomeTimeout.Restart();
                uint homeState = 0;
                ret += zmcaux.ZAux_BusCmd_Datum(ZMotion_Handle, (uint)AxisID, homeMode);
                do
                {
                    ret += zmcaux.ZAux_BusCmd_GetHomeStatus(ZMotion_Handle, (uint)AxisID, ref homeState);
                    if (homeState == 1)
                    {
                        break;
                    }
                    Thread.Sleep(10);
                } while (homeState != 1 && stopwatch_HomeTimeout.ElapsedMilliseconds < 180000 && StopHome == false);//超时时间3分钟
                stopwatch_HomeTimeout.Stop();
                Thread.Sleep(2000);
                if (homeState != 1 || stopwatch_HomeTimeout.ElapsedMilliseconds >= 180000 || StopHome)
                {
                    StopHome = false;
                    HomeMsg = "回零失败!";
                    HomeDone = true;
                }
                else
                {
                    HomeMsg = "回零完成!";
                    HomeDone = true;
                }

                if (ret != 0)
                {
                    Logger.AddLog($"{AxisName}回零出错，出错码{ret}!", eMsgType.Error);
                }
            });
        }

        public override bool MoveAbs(double pos, double vel)
        {
            int ret = 0;
            if (ZMotion_Handle == (IntPtr)0)
            {
                Logger.AddLog("未链接到控制器!", eMsgType.Error);
                return false;
            }
            //设置速度
            ret += zmcaux.ZAux_Direct_SetSpeed(ZMotion_Handle, AxisID, (float)vel);
            //开始绝对运动
            ret += zmcaux.ZAux_Direct_Single_MoveAbs(ZMotion_Handle, AxisID, (float)pos);
            if (ret != 0)
            {
                Logger.AddLog($"{AxisName}绝对运动出错，出错码{ret}!", eMsgType.Error);
            }
            Thread.Sleep(50);
            return true;
        }

        public override bool MoveJog(eDirection dir, double vel)
        {
            int ret = 0;
            if (ZMotion_Handle == (IntPtr)0)
            {
                Logger.AddLog("未链接到控制器!", eMsgType.Error);
                return false;
            }
            float tempVel = (float)(vel);
            float acc = (float)(Acc);
            ret = zmcaux.ZAux_Direct_SetSpeed(ZMotion_Handle, AxisID, (float)vel);
            ret = zmcaux.ZAux_Direct_SetAccel(ZMotion_Handle, AxisID, (float)acc);
            ret = zmcaux.ZAux_Direct_Single_Vmove(ZMotion_Handle, AxisID, dir == eDirection.Positive ? 1 : -1);
            return true;
        }

        public override bool MoveRel(double pos, double vel)
        {
            int ret = 0;
            if (ZMotion_Handle == (IntPtr)0)
            {
                Logger.AddLog("未链接到控制器!", eMsgType.Error);
                return false;
            }
            //设置速度
            ret += zmcaux.ZAux_Direct_SetSpeed(ZMotion_Handle, AxisID, (float)vel);
            //开始绝对运动
            ret += zmcaux.ZAux_Direct_Single_Move(ZMotion_Handle, AxisID, (float)pos);
            if (ret != 0)
            {
                Logger.AddLog($"{AxisName}相对运动出错，出错码{ret}!", eMsgType.Error);
            }
            Thread.Sleep(50);
            return true;
        }

        public override bool Stop(int mode = 2)
        {
            if (ZMotion_Handle == (IntPtr)0)
            {
                Logger.AddLog("未链接到控制器!", eMsgType.Error);
                return false;
            }
            StopHome = true;
            int ret = zmcaux.ZAux_Direct_Single_Cancel(ZMotion_Handle, AxisID, 2);
            if (ret != 0)
            {
                Logger.AddLog($"{AxisName}上使能出错，出错码{ret}!", eMsgType.Error);
            }
            return true;
        }
        #endregion
        #region 轴状态刷新
        public override void UpdateData()
        {
            // 读取轴状态
            int axisEnableState = 0;
            zmcaux.ZAux_Direct_GetAxisEnable(ZMotion_Handle, AxisID, ref axisEnableState);
            SvOn = axisEnableState == 0 ? false : true;
            int axisBusyState = 0;
            zmcaux.ZAux_Direct_GetIfIdle(ZMotion_Handle, AxisID, ref axisBusyState);
            Busy = axisBusyState == 0 ? true : false;
            int axisState = 0;
            zmcaux.ZAux_Direct_GetAxisStatus(ZMotion_Handle, AxisID, ref axisState);
            Alm = (axisState & 0b_0000_0000_0000_0000_0000_0000_0000_1000) == 0b_0000_0000_0000_0000_0000_0000_0000_1000;
            Pot = (axisState & 0b_0000_0000_0000_0000_0000_0000_0001_0000) == 0b_0000_0000_0000_0000_0000_0000_0001_0000;
            Net = (axisState & 0b_0000_0000_0000_0000_0000_0000_0010_0000) == 0b_0000_0000_0000_0000_0000_0000_0010_0000;
            Emg = (axisState & 0b_0000_0001_0000_0000) == 0b_0000_0001_0000_0000;
            Inp = (axisState & 0b_0000_1000_0000_0000) == 0b_0000_1000_0000_0000;
            int orgIndex = 0;
            zmcaux.ZAux_Direct_GetDatumIn(ZMotion_Handle, AxisID, ref orgIndex);
            uint orgState = 0;
            zmcaux.ZAux_Direct_GetIn(ZMotion_Handle, orgIndex, ref orgState);
            Org = orgState == 0 ? false : true;
            //读取轴规划位置
            float pos = 0;
            zmcaux.ZAux_Direct_GetMpos(ZMotion_Handle, AxisID, ref pos);
            CurPos =Math.Round(pos,3);// Math.Round(pos / Feed * Screw, 3);
            if (Alm && CommonMethods.Mach.AlmFlag == false && IsEnable)
            {
                Logger.AddLog($"[报警]{AxisName}报警！", eMsgType.Alarm);
                CommonMethods.Mach.AlmFlag = true;
            }

        }
        #endregion
        #region 轴参数设置
        public override bool SetFeed(float feed)
        {
            if (ZMotion_Handle == (IntPtr)0)
            {
                Logger.AddLog("未链接到控制器!", eMsgType.Error);
                return false;
            }
            int ret = 0;
            ret = zmcaux.ZAux_Direct_SetUnits(ZMotion_Handle, AxisID, feed);
            if (ret != 0)
            {
                Logger.AddLog($"{AxisName}设置脉冲当量出错，出错码{ret}!", eMsgType.Error);
            }
            return true;
        }
        public override bool SetAcc(double acc)
        {
            if (ZMotion_Handle == (IntPtr)0)
            {
                Logger.AddLog("未链接到控制器!", eMsgType.Error);
                return false;
            }
            int ret = 0;
            float accTemp = (float)(Acc);
            ret += zmcaux.ZAux_Direct_SetAccel(ZMotion_Handle, AxisID, (float)accTemp);
            ret += zmcaux.ZAux_Direct_SetDecel(ZMotion_Handle, AxisID, (float)accTemp);
            if (ret != 0)
            {
                Logger.AddLog($"{AxisName}设置加减速出错，出错码{ret}!", eMsgType.Error);
            }
            return true;
        }
        public override bool SetEncoderResolution(int value)
        {
            if (ZMotion_Handle == (IntPtr)0)
            {
                Logger.AddLog("未链接到控制器!", eMsgType.Error);
                return false;
            }
            int ret = 0;
            //连接句柄 槽位号 节点编号(此处与轴ID相同) 对象字典编号 对象字典子编号 数据类型 值
            ret = zmcaux.ZAux_BusCmd_SDOWrite(ZMotion_Handle, 0, (uint)AxisID, 24721, 1, 7, value);
            ret = zmcaux.ZAux_BusCmd_SDOWrite(ZMotion_Handle, 0, (uint)AxisID, 24721, 2, 7, 10000);
            if (ret != 0)
            {
                Logger.AddLog($"{AxisName}设置编码器分辨率出错，出错码{ret}!", eMsgType.Error);
            }
            return true;
        }


        #endregion

    }
}
