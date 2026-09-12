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
using CameraHandle = System.Int32;

namespace Plugin.CameraMindVision
{
    [Category("相机")]
    [DisplayName("迈德相机")]
    [Serializable]
    public class CameraMindVision : CameraBase
    {
        [NonSerialized]
        protected IntPtr device;
        [NonSerialized]
        protected CameraHandle m_hCamera = 0;// 句柄 
        [NonSerialized]
        protected tSdkCameraCapbility tCameraCapability;// 相机特性描述
        [NonSerialized]
        protected IntPtr m_ImageBuffer;// 预览通道RGB图像缓存
        [NonSerialized]
        protected IntPtr m_iCaptureCallbackCtx;// 图像回调函数的上下文参数
        [NonSerialized]
        protected pfnCameraGrabberFrameCallback m_FrameCallback;

        public CameraMindVision() : base() { }
        /// <summary>搜索相机</summary>
        public override List<CameraInfoModel> SearchCameras()
        {
            List<CameraInfoModel> mCamInfoList = new List<CameraInfoModel>(); 
            CameraSdkStatus status = CameraSdkStatus.CAMERA_STATUS_FAILED;
            tSdkCameraDevInfo[] tCameraDevInfoList = null;

            try
            {
                status = MvApi.CameraEnumerateDevice(out tCameraDevInfoList);
                if (status != CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                {
                    MessageView.Ins.MessageBoxShow($"MindVision枚举失败，Err:{status}",eMsgType.Error);
                    return mCamInfoList;
                }
                //获取相机数
                int iCameraCounts = (tCameraDevInfoList != null ? tCameraDevInfoList.Length : 0);

                string sn = "";

                for (int i = 0; i < iCameraCounts; i++)
                {
                    sn = System.Text.Encoding.Default.GetString(tCameraDevInfoList[i].acSn);
                    var spSN = sn.Split('\0');

                    status = MvApi.CameraGrabber_Create(out device, ref tCameraDevInfoList[i]);
                    if (status != CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                    {
                        Logger.AddLog($"MindVision({spSN[0]})创建失败，Err:{status}", eMsgType.Error);
                    }
                    CameraInfoModel _camInfo = new CameraInfoModel();
                    _camInfo.SerialNO = spSN[0];
                    _camInfo.MaskName = spSN[0];
                    _camInfo.ExtInfo = device; 
                    ExtInfo = device;
                    mCamInfoList.Add(_camInfo);
                }
            }
            catch (Exception ex)
            {
                Logger.AddLog($"MindVision枚举失败，Err:{status}", eMsgType.Error);
            }
            return mCamInfoList;
        }

        /// <summary>连接相机</summary>
        public override void ConnectDev()
        {
            CameraSdkStatus status = CameraSdkStatus.CAMERA_STATUS_FAILED;
            try
            {
                base.ConnectDev();
                // 如果设备已经连接先断开
                DisConnectDev();
                if (ExtInfo == null) return;
                device = (IntPtr)ExtInfo;
                status = MvApi.CameraGrabber_GetCameraHandle(device, out m_hCamera);
                if (status != CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                {
                    m_hCamera = 0;
                    Logger.AddLog($"MindVision({CameraNo})获取句柄失败，Err:{status}", eMsgType.Error);
                }
                //获得相机特性描述
                status = MvApi.CameraGetCapability(m_hCamera, out tCameraCapability);
                if (status != CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                {
                    Logger.AddLog($"MindVision({CameraNo})获取相机特性失败，Err:{status}", eMsgType.Error);
                    return;
                }
                //设置缓存
                m_ImageBuffer = Marshal.AllocHGlobal(tCameraCapability.sResolutionRange.iWidthMax * tCameraCapability.sResolutionRange.iHeightMax * 3 + 1024);

                //设置图像模式
                if (tCameraCapability.sIspCapacity.bMonoSensor != 0)
                {
                    // 黑白相机输出8位灰度数据
                    MvApi.CameraSetIspOutFormat(m_hCamera, (uint)MVSDK.emImageFormat.CAMERA_MEDIA_TYPE_MONO8);
                }

                // 一般情况，0表示连续采集模式；1表示软件触发模式；2表示硬件触发模式。  
                MvApi.CameraSetTriggerMode(m_hCamera, 1);

                //设置回调
                m_FrameCallback = new pfnCameraGrabberFrameCallback(ImageCallbackFunc);

                MvApi.CameraGrabber_SetRGBCallback(device, m_FrameCallback, IntPtr.Zero);

                SetSetting();
                MvApi.CameraGrabber_StartLive(device);
                Connected = true;
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

                MvApi.CameraGrabber_StopLive(device);
                //CameraSdkStatus status = MvApi.CameraUnInit(m_hCamera);
                //if (status != CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                //{
                //    Trace.TraceError($"{CameraNo}关闭失败，Err:{status}");
                //}
                //status = MvApi.CameraGrabber_Destroy(device);
                //if (status != CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                //{
                //    Trace.TraceError($"{CameraNo}关闭失败，Err:{status}");
                //}
                //Marshal.FreeHGlobal(m_ImageBuffer);
                //m_hCamera = 0;
                Connected = false;
            }
            base.DisConnectDev();
        }
        /// <summary>采集图像,是否手动采图</summary>
        public override bool CaptureImage(bool byHand)
        {
            try
            {
                int nRet = 0;
                if (byHand)
                {
                    eTrigMode temp = TrigMode;
                    //设置内触发
                    SetTriggerMode(eTrigMode.软触发);
                    var status = MvApi.CameraSoftTrigger(m_hCamera);
                    if (status != CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                    {
                        m_hCamera = 0;
                        Logger.AddLog($"{CameraNo}相机软触发失败，Err:{status}",eMsgType.Error);
                        return false;
                    }
                    Thread.Sleep(200);
                    //恢复旧模式
                    SetTriggerMode(temp);

                }
                else
                {
                    if (TrigMode == eTrigMode.软触发)
                    {
                        SetTriggerMode(eTrigMode.软触发);
                        var status = MvApi.CameraSoftTrigger(m_hCamera);
                        if (status != CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                        {
                            m_hCamera = 0;
                            Logger.AddLog($"{CameraNo}相机软触发失败，Err:{status}", eMsgType.Error);
                            return false;
                        }
                    }
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
                Logger.AddLog("MindVision:" + ex.Message, eMsgType.Error);
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
            var ret = MvApi.CameraSetAnalogGain(m_hCamera, (int)value);
            var ret1 = MvApi.CameraSaveParameter(m_hCamera, (int)emSdkParameterTeam.PARAMETER_TEAM_A);
        }
        /// <summary>设置曝光</summary>
        public override void SetExposureTime(float value)
        {
            ExposeTime = value;
            var ret = MvApi.CameraSetExposureTime(m_hCamera, Convert.ToDouble(value));
            var ret1 = MvApi.CameraSaveParameter(m_hCamera, (int)emSdkParameterTeam.PARAMETER_TEAM_A);
        }
        /// <summary>设置触发</summary>
        public override bool SetTriggerMode(eTrigMode mode)
        {
            CameraSdkStatus status = CameraSdkStatus.CAMERA_STATUS_FAILED;
            switch (mode)
            {
                case eTrigMode.内触发:
                    status = MvApi.CameraSetTriggerMode(m_hCamera, 1); //软件触发模式，由软件发送指令后，传感器开始采集指定帧数的图像，采集完成后，停止输出
                    break;
                case eTrigMode.软触发:   // freerunning
                    {
                        status = MvApi.CameraSetTriggerMode(m_hCamera, 1); //软件触发模式，由软件发送指令后，传感器开始采集指定帧数的图像，采集完成后，停止输出
                        if (status != CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                        {
                            m_hCamera = 0;
                            Logger.AddLog($"{CameraNo}相机设置软触发失败，Err:{status}", eMsgType.Error);
                            return false;
                        }
                        break;
                    }
                case eTrigMode.上升沿:   // Software trigger
                    {
                        status = MvApi.CameraSetTriggerMode(m_hCamera, 2); //软件触发模式，由软件发送指令后，传感器开始采集指定帧数的图像，采集完成后，停止输出
                        MvApi.CameraSetExtTrigSignalType(m_hCamera, 0); //设置为上升沿信号触发。
                        if (status != CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                        {
                            m_hCamera = 0;
                            Logger.AddLog($"{CameraNo}相机设置上升沿触发失败，Err:{status}", eMsgType.Error);
                            return false;
                        }
                        break;
                    }
                case eTrigMode.下降沿:   // Software trigger
                    {
                        status = MvApi.CameraSetTriggerMode(m_hCamera, 2); //软件触发模式，由软件发送指令后，传感器开始采集指定帧数的图像，采集完成后，停止输出
                        MvApi.CameraSetExtTrigSignalType(m_hCamera, 1); //设置为上升沿信号触发。
                        if (status != CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                        {
                            m_hCamera = 0;
                            Logger.AddLog($"{CameraNo}相机设置下降沿触发失败，Err:{status}", eMsgType.Error);
                            return false;
                        }
                        break;
                    }
            }
            return true;
        }
        #region 相机事件
        /// <summary>采集回调</summary>
        private void ImageCallbackFunc(IntPtr hCamera, IntPtr pFrameBuffer, ref tSdkFrameHead pFrameHead, IntPtr pContext)
        {
            // 由于黑白相机在相机打开后设置了ISP输出灰度图像
            // 因此此处pFrameBuffer=8位灰度数据
            // 否则会和彩色相机一样输出BGR24数据

            // 彩色相机ISP默认会输出BGR24图像
            // pFrameBuffer=BGR24数据
            int w = pFrameHead.iWidth;
            int h = pFrameHead.iHeight;

            try
            {
                if (pFrameHead.uiMediaType == (uint)MVSDK.emImageFormat.CAMERA_MEDIA_TYPE_MONO8)
                {
                    HImage hImage = null, ImageTemp0 = new HImage();
                    hImage = new HImage("byte", w, h, pFrameBuffer);

                    ImageTemp0 = hImage;
                    hImage = ImageTemp0.MirrorImage("row");
                    Image = hImage;
                }
                else
                {
                    //图像处理，将原始输出转换为RGB格式的位图数据，同时叠加白平衡、饱和度、LUT等ISP处理。
                    //MvApi.CameraImageProcess(hCamera, pFrameBuffer, m_ImageBuffer, ref pFrameHead);

                    ////叠加十字线、自动曝光窗口、白平衡窗口信息(仅叠加设置为可见状态的)。   
                    //MvApi.CameraImageOverlay(hCamera, m_ImageBuffer, ref pFrameHead);

                    ////调用SDK封装好的接口，显示预览图像
                    //MvApi.CameraDisplayRGB24(hCamera, m_ImageBuffer, ref pFrameHead);

                    HImage Image2 = new HImage(), ImageTemp0 = new HImage();
                    Image2.GenImageInterleaved(pFrameBuffer, "bgr", w, h, -1, "byte", 0, 0, 0, 0, -1, 0);
                    ImageTemp0 = Image2;
                    Image2 = ImageTemp0.MirrorImage("row");
                    Image = Image2;
                }
                EventWait.Set();
                ImageGrab?.Invoke(Image);
            }
            catch (Exception ex)
            {
                EventWait.Set();
            }
        }

        /// <summary>断开连接时触发</summary>
        private void OnConnectionLost(object sender, EventArgs e)
        {
            // Close the MyCamera object.
            DisConnectDev();
        }
        #endregion
        public void FindCBySN(string Ctemp)
        {
            CameraSdkStatus status = CameraSdkStatus.CAMERA_STATUS_FAILED;
            tSdkCameraDevInfo[] tCameraDevInfoList = null;
            try
            {
                status = MvApi.CameraEnumerateDevice(out tCameraDevInfoList);
                if (status != CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                {
                    MessageView.Ins.MessageBoxShow($"MindVision枚举失败，Err:{status}", eMsgType.Error);
                    return;
                }
                //获取相机数
                int iCameraCounts = (tCameraDevInfoList != null ? tCameraDevInfoList.Length : 0);

                string sn = "";

                for (int i = 0; i < iCameraCounts; i++)
                {
                    CameraInfoModel _camInfo = new CameraInfoModel();
                    sn = System.Text.Encoding.Default.GetString(tCameraDevInfoList[i].acSn);
                    var spSN = sn.Split('\0');
                    if (Ctemp == spSN[0])
                    {
                        status = MvApi.CameraGrabber_Create(out device, ref tCameraDevInfoList[i]);
                        if (status != CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                        {
                            Logger.AddLog($"MindVision({_camInfo.SerialNO})创建失败，Err:{status}", eMsgType.Error);
                            return;
                        }
                        ExtInfo = device;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.AddLog($"MindVision枚举失败，Err:{status}", eMsgType.Error);
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
    }
}
