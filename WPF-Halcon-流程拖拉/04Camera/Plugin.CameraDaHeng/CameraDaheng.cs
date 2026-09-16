using HalconDotNet;
using MVSDK;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Threading;
using VM.Start.Common.Enums;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.Dialogs.Views;
using VM.Start.Models;
using GxIAPINET;
using CameraHandle = System.Int32;
using System.Windows;
using System.Drawing;
using VM.Start.Common;
using VM.Start.Views;

namespace Plugin.CameraDaHeng
{
    [Category("相机")]
    [DisplayName("大恒相机")]
    [Serializable]
    public class CameraDaheng : CameraBase
    {
        #region pro
        [NonSerialized]
        GX_DEVICE_OFFLINE_CALLBACK_HANDLE m_hCB = null;                           ///<掉线回调句柄
        [NonSerialized]
        IGXFactory m_objIGXFactory = null;                             ///<Factory对像
        [NonSerialized]
        IGXStream m_objIGXStream = null;                 ///<流对像
        [NonSerialized]
        IGXFeatureControl m_objIGXStreamFeatureControl = null;               ///<流层属性控制器对象
        [NonSerialized]
        IGXDevice m_objIGXDevice = null;                 ///<设备对像
        [NonSerialized]
        IGXFeatureControl m_objIGXFeatureControl = null;                ///<远端设备属性控制器对像 
        [NonSerialized]///<图像显示类对象
        GX_FEATURE_CALLBACK_HANDLE m_hFeatureCallback = null;                 ///<Feature事件的句柄
        [NonSerialized]
        private bool OfflineFlag=false;
        #endregion

        #region Command

        #endregion

        #region Method
        public CameraDaheng() : base()
        {
            m_objIGXFactory = IGXFactory.GetInstance();
            m_objIGXFactory.Init();
        }
        /// <summary>搜索相机</summary>
        public override List<CameraInfoModel> SearchCameras()
        {
            List<CameraInfoModel> mCamInfoList = new List<CameraInfoModel>();
            CameraSdkStatus status = CameraSdkStatus.CAMERA_STATUS_FAILED;
            try
            {
                List<IGXDeviceInfo> m_listIGXDeviceInfo = new List<IGXDeviceInfo>();        ///<设备信息列表
                                                                                            /// <summary>
                                                                                            /// 枚举设备
                                                                                            /// </summary>
                m_listIGXDeviceInfo.Clear();
                if (null != m_objIGXFactory)
                {
                    m_objIGXFactory.UpdateDeviceList(200, m_listIGXDeviceInfo);
                }
                if (m_listIGXDeviceInfo.Count == 0)
                {
                    MessageView.Ins.MessageBoxShow($"DaHeng枚举失败，Err:{status}", eMsgType.Error);
                    return mCamInfoList;
                }
                //获取相机数
                int iCameraCounts = m_listIGXDeviceInfo.Count;
                string sn = "";
                for (int i = 0; i < iCameraCounts; i++)
                {
                    CameraInfoModel _camInfo = new CameraInfoModel();
                    _camInfo.SerialNO = m_listIGXDeviceInfo[i].GetSN();
                    _camInfo.MaskName = m_listIGXDeviceInfo[i].GetDisplayName();

                    mCamInfoList.Add(_camInfo);
                }
            }
            catch (Exception ex)
            {
                Logger.AddLog($"DaHeng枚举失败，Err:{status}", eMsgType.Error);
            }
            return mCamInfoList;
        }

        /// <summary>连接相机</summary>
        public override void ConnectDev()
        {
            try
            {
                base.ConnectDev();
                List<IGXDeviceInfo> listGXDeviceInfo = new List<IGXDeviceInfo>();

                //关闭流
                __CloseStream();
                // 如果设备已经打开则关闭，保证相机在初始化出错情况下能再次打开
                __CloseDevice();

                m_objIGXFactory.UpdateDeviceList(200, listGXDeviceInfo);
                if (listGXDeviceInfo.Count <= 0) return;
                int i;
                for (i = 0; i < listGXDeviceInfo.Count; i++)
                {
                    if (base.SerialNo == listGXDeviceInfo[i].GetSN())
                    {
                        break;
                    }
                }
                // 如果设备已经打开则关闭，保证相机在初始化出错情况下能再次打开
                if (null != m_objIGXDevice)
                {
                    m_objIGXDevice.Close();
                    m_objIGXDevice = null;
                }
                m_objIGXDevice = m_objIGXFactory.OpenDeviceBySN(listGXDeviceInfo[i].GetSN(), GX_ACCESS_MODE.GX_ACCESS_EXCLUSIVE);
                m_objIGXFeatureControl = m_objIGXDevice.GetRemoteFeatureControl();

                //打开流
                if (null != m_objIGXDevice)
                {
                    m_objIGXStream = m_objIGXDevice.OpenStream((uint)i);
                    m_objIGXStreamFeatureControl = m_objIGXStream.GetFeatureControl();
                }

                // 建议用户在打开网络相机之后，根据当前网络环境设置相机的流通道包长值，
                // 以提高网络相机的采集性能,设置方法参考以下代码。
                GX_DEVICE_CLASS_LIST objDeviceClass = m_objIGXDevice.GetDeviceInfo().GetDeviceClass();
                if (GX_DEVICE_CLASS_LIST.GX_DEVICE_CLASS_GEV == objDeviceClass)
                {
                    // 判断设备是否支持流通道数据包功能
                    if (true == m_objIGXFeatureControl.IsImplemented("GevSCPSPacketSize"))
                    {
                        // 获取当前网络环境的最优包长值
                        uint nPacketSize = m_objIGXStream.GetOptimalPacketSize();
                        // 将最优包长值设置为当前设备的流通道包长值
                        m_objIGXFeatureControl.GetIntFeature("GevSCPSPacketSize").SetValue(nPacketSize);
                    }
                }

                __InitDevice();

                //    m_objGxBitmap = new GxBitmap(m_objIGXDevice, m_pic_ShowImage);
                Connected = true;
                if (null != m_objIGXStreamFeatureControl)
                {
                    //设置流层Buffer处理模式为OldestFirst
                    m_objIGXStreamFeatureControl.GetEnumFeature("StreamBufferHandlingMode").SetValue("OldestFirst");
                }
                //开启采集流通道
                if (null != m_objIGXStream)
                {
                    m_objIGXStream.RegisterCaptureCallback(null, __OnFrameCallbackFun);
                    m_hCB = m_objIGXDevice.RegisterDeviceOfflineCallback(null, __OnDeviceOfflineCallbackFun);
                    m_objIGXStream.StartGrab();
                }
                //发送开采命令
                if (null != m_objIGXFeatureControl)
                {
                    m_objIGXFeatureControl.GetCommandFeature("AcquisitionStart").Execute();
                }

            
            }
            catch (Exception ex)
            {
                Connected = false;
            }
            base.ConnectDev();
        }
        /// <summary>断开相机</summary>
        public override void DisConnectDev()
        {
            if (Connected)
            {
                try
                {
                    if (!OfflineFlag)
                        m_objIGXDevice.UnregisterDeviceOfflineCallback(m_hCB);
                    // 如果未停采则先停止采集
                    if (null != m_objIGXFeatureControl)
                    {
                        m_objIGXFeatureControl.GetCommandFeature("AcquisitionStop").Execute();
                        m_objIGXFeatureControl = null;
                    }
                }
                catch (Exception)
                {

                }

                try
                {
                    //停止流通道、注销采集回调和关闭流
                    if (null != m_objIGXStream)
                    {
                        m_objIGXStream.StopGrab();
                        //注销采集回调函数
                        m_objIGXStream.UnregisterCaptureCallback();
                        m_objIGXStream.Close();
                        m_objIGXStream = null;
                        m_objIGXStreamFeatureControl = null;
                    }
                }
                catch (Exception)
                {

                }

                try
                {
                    //关闭设备
                    if (null != m_objIGXDevice)
                    {
                        m_objIGXDevice.Close();
                        m_objIGXDevice = null;

                    }
                }
                catch (Exception)
                {

                }
                Connected = false;
            }
            base.DisConnectDev();
        }
        /// <summary>采集图像,是否手动采图</summary>
        public override bool CaptureImage(bool byHand)
        {
            if ((null == m_objIGXFeatureControl)||(!Connected)) return false;
            try
            {
                if (null != m_objIGXStream)
                {
                    m_objIGXStream.FlushQueue();
                }
                //软触发
                if ((byHand) || (TrigMode != eTrigMode.上升沿 && TrigMode != eTrigMode.下降沿))
                {
                    m_objIGXFeatureControl.GetCommandFeature("TriggerSoftware").Execute();
                }//硬触发
                else
                {
                    SetTriggerMode(TrigMode);
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.GetExceptionMsg(ex);
                return false;
            }
        }
        /// <summary> 相机设置</summary>
        public override void SetSetting()
        {
            //设置采集模式
            SetTriggerMode(TrigMode);
            //设置曝光时间
            SetExposureTime(ExposeTime);
            //nRet = MyCamera.MV_CC_SetFloatValue_NET("ExposureTime", ExposeTime);
            SetGain((long)Gain);
        }
        public override void CameraChanged(ChangType changTyp)
        {
            try
            {
                switch (changTyp)
                {
                    case ChangType.增益:
                        SetGain((long)Gain);
                        break;
                    case ChangType.曝光:
                        SetExposureTime((long)ExposeTime);
                        break;
                    case ChangType.宽度:
                        SetWidth();
                        break;
                    case ChangType.高度:
                        SetHeight();
                        break;
                    case ChangType.触发:
                        SetTriggerMode(TrigMode);
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.AddLog("DaHengVision:" + ex.Message, eMsgType.Error);
            }
        }
        /// <summary>设置宽度</summary>
        public void SetWidth()
        {
            if (Width > 100 & Width <= WidthMax)
            {

            }
        }
        /// <summary>设置高度</summary>
        public void SetHeight()
        {
            if (Height > 100 & Height <= HeightMax)
            {

            }
        }
        /// <summary>设置增益 </summary>
        public override void SetGain(float value)
        {
            Gain = value;
            if ((null != m_objIGXFeatureControl) && (Connected))
            {
                float dMin = (float)m_objIGXFeatureControl.GetFloatFeature("Gain").GetMin();
                float dMax = (float)m_objIGXFeatureControl.GetFloatFeature("Gain").GetMax();
                //判断输入值是否在曝光时间的范围内
                //若大于最大值则将曝光值设为最大值
                if (Gain > dMax)
                {
                    Gain = dMax;
                }
                //若小于最小值将曝光值设为最小值
                if (Gain < dMin)
                {
                    Gain = dMin;
                }
                m_objIGXFeatureControl.GetFloatFeature("Gain").SetValue(Gain);
            }
        }
        /// <summary>设置曝光</summary>
        public override void SetExposureTime(float value)
        {
            ExposeTime = value;
            if ((null != m_objIGXFeatureControl) && (Connected))
            {
                float dMin = (float)m_objIGXFeatureControl.GetFloatFeature("ExposureTime").GetMin();
                float dMax = (float)m_objIGXFeatureControl.GetFloatFeature("ExposureTime").GetMax();
                //判断输入值是否在曝光时间的范围内
                //若大于最大值则将曝光值设为最大值
                if (ExposeTime > dMax)
                {
                    ExposeTime = dMax;
                }
                //若小于最小值将曝光值设为最小值
                if (ExposeTime < dMin)
                {
                    ExposeTime = dMin;
                }
                m_objIGXFeatureControl.GetFloatFeature("ExposureTime").SetValue(ExposeTime);
            }
        }
        /// <summary>设置触发</summary>
        public override bool SetTriggerMode(eTrigMode mode)
        {
            if (m_objIGXFeatureControl == null) return false;

            switch (mode)
            {
                case eTrigMode.内触发:

                case eTrigMode.软触发:    // Software trigger
                    {

                        //选择触发源为软触发
                        m_objIGXFeatureControl.GetEnumFeature("TriggerSource").SetValue("Software");
                        break;
                    }
                case eTrigMode.下降沿:   // Software trigger
                    {
                        //选择触发源为软触发
                        m_objIGXFeatureControl.GetEnumFeature("TriggerSource").SetValue("Line0");
                        __SetEnumValue("TriggerActivation", "FallingEdge", m_objIGXFeatureControl);
                        break;
                    }
                case eTrigMode.上升沿:   // Software trigger
                    {
                        m_objIGXFeatureControl.GetEnumFeature("TriggerSource").SetValue("Line0");
                        __SetEnumValue("TriggerActivation", "RisingEdge", m_objIGXFeatureControl);
                        break;
                    }
            }
            return true;
        }

        public void FindCBySN(string Ctemp)
        {
            List<IGXDeviceInfo> listGXDeviceInfo = new List<IGXDeviceInfo>();
            try
            {
                if (m_objIGXFactory==null)
                {
                    m_objIGXFactory = IGXFactory.GetInstance();
                    m_objIGXFactory.Init();
                }
                m_objIGXFactory.UpdateDeviceList(200, listGXDeviceInfo);

                if (listGXDeviceInfo.Count <= 0)
                {
                    MessageView.Ins.MessageBoxShow($"DaHeng枚举失败", eMsgType.Error);
                    return;
                }
                int i;
                for (i = 0; i < listGXDeviceInfo.Count; i++)
                {
                    if (base.SerialNo == listGXDeviceInfo[i].GetSN())
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.AddLog($"DaHeng枚举失败", eMsgType.Error);
            }
            return;
        }
        //[OnSerializing()] 序列化之前
        //[OnSerialized()] 序列化之后
        //[OnDeserializing()] 反序列化之前
        [OnDeserialized()] //反序列化之后
        internal void OnDeserializedMethod(StreamingContext context)
        {
            if (SerialNo == null || SerialNo == "")
            {
                return;
            }
            FindCBySN(SerialNo);
            ConnectDev();
        }
        private void __CloseStream()
        {
            try
            {
                //关闭流
                if (null != m_objIGXStream)
                {
                    m_objIGXStream.Close();
                    m_objIGXStream = null;
                    m_objIGXStreamFeatureControl = null;
                }
            }
            catch (Exception)
            {
            }
        }
        /// <summary>
        /// 关闭设备
        /// </summary>
        private void __CloseDevice()
        {
            try
            {
                //关闭设备
                if (null != m_objIGXDevice)
                {
                    m_objIGXDevice.Close();
                    m_objIGXDevice = null;
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 相机初始化
        /// </summary>
        private void __InitDevice()
        {
            if (null != m_objIGXFeatureControl)
            {
                //设置采集模式连续采集
                m_objIGXFeatureControl.GetEnumFeature("AcquisitionMode").SetValue("Continuous");

                //设置触发模式为开
                m_objIGXFeatureControl.GetEnumFeature("TriggerMode").SetValue("On");

                //选择触发源为软触发
                m_objIGXFeatureControl.GetEnumFeature("TriggerSource").SetValue("Software");
            }
        }
        /// <summary>
        /// 对枚举型变量按照功能名称设置值
        /// </summary>
        /// <param name="strFeatureName">枚举功能名称</param>
        /// <param name="strValue">功能的值</param>
        /// <param name="objIGXFeatureControl">属性控制器对像</param>
        private void __SetEnumValue(string strFeatureName, string strValue, IGXFeatureControl objIGXFeatureControl)
        {
            if (null != objIGXFeatureControl)
            {
                //设置当前功能值
                objIGXFeatureControl.GetEnumFeature(strFeatureName).SetValue(strValue);
            }
        }

            #region 相机事件
            /// <summary>
            /// 采集事件的委托函数
            /// </summary>
            /// <param name="objUserParam">用户私有参数</param>
            /// <param name="objIFrameData">图像信息对象</param>
            private void __OnFrameCallbackFun(object objUserParam, IFrameData objIFrameData)
            {
                try
                {
                    #region 图像格式转化
                    //图像获取为完整帧，可以读取图像宽、高、数据格式等
                    UInt64 nWidth = objIFrameData.GetWidth();
                    UInt64 nHeight = objIFrameData.GetHeight();
                    Width = (int)nWidth;
                    Height = (int)nHeight;
                    GX_PIXEL_FORMAT_ENTRY emPixelFormat = objIFrameData.GetPixelFormat();
                    IntPtr imagebuffer = objIFrameData.GetBuffer();
                    HObject camImage = new HObject(); HOperatorSet.GenEmptyObj(out camImage);
                    camImage.Dispose();
                    HOperatorSet.GenImage1Extern(out camImage, "byte", nWidth, nHeight, imagebuffer, 0);
                    #endregion
                    Image = new HImage(camImage);
                    EventWait.Set();
                    ImageGrab?.Invoke(Image);
                    camImage.Dispose();
                }
                catch (Exception ex)
                {
                    EventWait.Set();
                }
            }

            /// <summary>
            /// 掉线回调函数
            /// </summary>
            /// <param name="pUserParam">用户私有参数</param>
            private void __OnDeviceOfflineCallbackFun(object pUserParam)
            {
            OfflineFlag = true;
            if (!Connected) return; 
                try
                {
               
                    DisConnectDev();
                Connected = false;
                }
                catch (Exception)
                {

                }
            }
            #endregion

        #endregion

    }
}
