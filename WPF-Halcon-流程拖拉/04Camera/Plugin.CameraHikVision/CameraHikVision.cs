using HalconDotNet;
using MvCamCtrl.NET;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Attributes;
using VM.Start.Common;
using VM.Start.Common.Enums;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.Dialogs.Views;
using VM.Start.Models;

namespace Plugin.CameraHikVision
{
    [Category("相机")]
    [DisplayName("海康相机")]
    [Serializable]
    public class CameraHikVision : CameraBase
    {
        [NonSerialized]
        private MyCamera MyCamera = new MyCamera();
        [NonSerialized]
        public MyCamera.MV_CC_DEVICE_INFO CurDevice;
        [NonSerialized]
        private MyCamera.cbOutputExdelegate ImageCallback;
        //public object ExtInfo
        //{
        //    set { CurDevice = (MyCamera.MV_CC_DEVICE_INFO)value; }
        //    get { return CurDevice; }
        //}
        // ch:用于保存图像的缓存 | en:Buffer for saving image
        private UInt32 m_nBufSizeForSaveImage = 5120 * 5120 * 3 + 2048;
        [NonSerialized]
        private byte[] m_pBufForSaveImage = new byte[5120 * 5120 * 3 + 2048];

        public CameraHikVision() : base() { }
        /// <summary>搜索相机</summary>
        public override List<CameraInfoModel> SearchCameras()
        {
            List<CameraInfoModel> mCamInfoList = new List<CameraInfoModel>();
            MyCamera.MV_CC_DEVICE_INFO_LIST mDeviceList = new MyCamera.MV_CC_DEVICE_INFO_LIST();
            if (MyCamera.MV_CC_EnumDevices_NET(MyCamera.MV_GIGE_DEVICE | MyCamera.MV_USB_DEVICE, ref mDeviceList) != 0)
            {
                MessageView.Ins.MessageBoxShow("查找设备失败", eMsgType.Warn);
                return mCamInfoList;
            }
            // ch:在窗体列表中显示设备名 | en:Display device name in the form list
            for (int i = 0; i < mDeviceList.nDeviceNum; i++)
            {
                CameraInfoModel _camInfo = new CameraInfoModel();
                MyCamera.MV_CC_DEVICE_INFO device = (MyCamera.MV_CC_DEVICE_INFO)Marshal.PtrToStructure(mDeviceList.pDeviceInfo[i], typeof(MyCamera.MV_CC_DEVICE_INFO));
                if (device.nTLayerType == MyCamera.MV_GIGE_DEVICE)
                {
                    IntPtr buffer = Marshal.UnsafeAddrOfPinnedArrayElement(device.SpecialInfo.stGigEInfo, 0);
                    MyCamera.MV_GIGE_DEVICE_INFO gigeInfo = (MyCamera.MV_GIGE_DEVICE_INFO)Marshal.PtrToStructure(buffer, typeof(MyCamera.MV_GIGE_DEVICE_INFO));
                    if (gigeInfo.chUserDefinedName != "")
                    {
                        _camInfo.CamName = "HikVision: " + gigeInfo.chUserDefinedName + " (" + gigeInfo.chSerialNumber + ")";
                    }
                    else
                    {
                        _camInfo.CamName = "HikVision: " + gigeInfo.chManufacturerName + " " + gigeInfo.chModelName + " (" + gigeInfo.chSerialNumber + ")";
                    }
                    _camInfo.SerialNO = gigeInfo.chSerialNumber;
                    _camInfo.MaskName = gigeInfo.chSerialNumber;
                    _camInfo.ExtInfo = device;
                    ExtInfo = device;
                }
                else if (device.nTLayerType == MyCamera.MV_USB_DEVICE)
                {
                    IntPtr buffer = Marshal.UnsafeAddrOfPinnedArrayElement(device.SpecialInfo.stUsb3VInfo, 0);
                    MyCamera.MV_USB3_DEVICE_INFO usbInfo = (MyCamera.MV_USB3_DEVICE_INFO)Marshal.PtrToStructure(buffer, typeof(MyCamera.MV_USB3_DEVICE_INFO));
                    if (usbInfo.chUserDefinedName != "")
                    {
                        _camInfo.CamName = "HikVision: " + usbInfo.chUserDefinedName + " (" + usbInfo.chSerialNumber + ")";
                    }
                    else
                    {
                        _camInfo.CamName = ("HikVision: " + usbInfo.chManufacturerName + " " + usbInfo.chModelName + " (" + usbInfo.chSerialNumber + ")");
                    }

                    _camInfo.SerialNO = usbInfo.chSerialNumber;
                    _camInfo.MaskName = usbInfo.chSerialNumber;
                    _camInfo.ExtInfo = device;
                    ExtInfo = device;
                }

                mCamInfoList.Add(_camInfo);
            }
            return mCamInfoList;
        }

        /// <summary>连接相机</summary>
        public override void ConnectDev()
        {
            try
            {
                base.ConnectDev();
                // 如果设备已经连接先断开
                DisConnectDev();
                int nRet = -1;
                // ch:打开设备 | en:Open device
                if (MyCamera == null)
                {
                    MyCamera = new MyCamera();
                    if (null == MyCamera)
                    {
                        return;
                    }
                }
                if (null == ExtInfo) { return; }
                var curDevice = (MyCamera.MV_CC_DEVICE_INFO)ExtInfo;
                nRet = MyCamera.MV_CC_CreateDevice_NET(ref curDevice);
                if (MyCamera.MV_OK != nRet)
                {
                    ShowErrorMsg("Device open fail!", nRet);
                    return;
                }
                nRet = MyCamera.MV_CC_OpenDevice_NET();
                if (MyCamera.MV_OK != nRet)
                {
                    MyCamera.MV_CC_DestroyDevice_NET();
                    ShowErrorMsg("Device open fail!", nRet);
                    Logger.AddLog("Device open fail!" + nRet.ToString(), eMsgType.Error);
                    return;
                }
                SetSetting();
                // ch:设置采集连续模式 | en:Set Continues Aquisition Mode
                //MyCamera.MV_CC_SetEnumValue_NET("AcquisitionMode", 2);// ch:工作在连续模式 | en:Acquisition On Continuous Mode
                // ch:注册回调函数 | en:Register image callback
                ImageCallback = new MyCamera.cbOutputExdelegate(ImageCallbackFunc);
                nRet = MyCamera.MV_CC_RegisterImageCallBackEx_NET(ImageCallback, IntPtr.Zero);
                if (MyCamera.MV_OK != nRet)
                {
                    Console.WriteLine("Register image callback failed!");
                }
                // ch:开启抓图 || en: start grab image
                nRet = MyCamera.MV_CC_StartGrabbing_NET();
                if (MyCamera.MV_OK != nRet)
                {
                    Console.WriteLine("Start grabbing failed:{0:x8}", nRet);
                }
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
                int nRet = MyCamera.MV_CC_CloseDevice_NET();
                if (MyCamera.MV_OK != nRet)
                {
                    return;
                }
                nRet = MyCamera.MV_CC_DestroyDevice_NET();
                if (MyCamera.MV_OK != nRet)
                {
                    return;
                }
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
                    //获取触发模式和触发源
                    //MyCamera.MVCC_ENUMVALUE stTriggerMode = new MyCamera.MVCC_ENUMVALUE();
                    //MyCamera.MVCC_ENUMVALUE stTriggerSource = new MyCamera.MVCC_ENUMVALUE();
                    //nRet = MyCamera.MV_CC_GetTriggerMode_NET(ref stTriggerMode);
                    //nRet = MyCamera.MV_CC_GetTriggerSource_NET(ref stTriggerSource);
                    eTrigMode temp = TrigMode;
                    //设置内触发
                    SetTriggerMode(eTrigMode.软触发);
                    nRet = MyCamera.MV_CC_SetCommandValue_NET("TriggerSoftware");
                    //恢复旧模式
                    SetTriggerMode(temp);

                }
                else
                {
                    if (TrigMode == eTrigMode.软触发)
                    {
                        //SignalWait.Reset();
                        //SignalWait.WaitOne();
                        nRet = MyCamera.MV_CC_SetCommandValue_NET("TriggerSoftware");
                    }
                }

                if (nRet != MyCamera.MV_OK)
                {
                    ShowErrorMsg("Set CaptureImage Time Fail!", nRet);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.GetExceptionMsg(ex);
                return false;
            }
        }
        public override bool SetOutPut(int lineIndex, int time)
        {
            ////Strobe输出
            //nRet = MV_CC_SetEnumValue(handle, "LineSelector", 1);
            ////0:Line0 1:Line1 2:Line2 
            //nRet = MV_CC_SetEnumValue(handle, "LineMode", 8);//仅LineSelector为line2时需要特意设置，其他输出不需要
            //                                                 //0:Input 1:Output 8:Strobe 
            //int DurationValue = 0, DelayValue = 0, PreDelayValue = 0;//us
            //nRet = MV_CC_SetIntValue(handle, "StrobeLineDuration", DurationValue);
            ////strobe持续时间，设置为0，持续时间就是曝光时间，设置其他值，就是其他值时间
            //nRet = MV_CC_SetIntValue(handle, "StrobeLineDelay", DelayValue);//strobe延时，从曝光开始，延时多久输出
            //nRet = MV_CC_SetIntValue(handle, "StrobeLinePreDelay", PreDelayValue);//strobe提前输出，曝光延后开始
            //                                                                      //--------------------------------------------------------------------------------------------------
            //nRet = MV_CC_SetBoolValue(handle, "StrobeEnable", TRUE);/
            int nRet = 0;
            if (lineIndex != 1 && lineIndex != 2)
                return false;
            nRet += MyCamera.MV_CC_SetEnumValue_NET("LineSelector", (uint)lineIndex);//1:Line1 2:Line2 
            //if (lineIndex == 2)
            //    nRet += MyCamera.MV_CC_SetEnumValue_NET("LineMode", 8);//0:Input 1:Output 8:Strobe //仅LineSelector为line2时需要特意设置，其他输出不需要
            nRet += MyCamera.MV_CC_SetIntValue_NET("StrobeLineDuration", (uint)time); //strobe持续时间，设置为0，持续时间就是曝光时间，设置其他值，就是其他值时间
            //nRet += MyCamera.MV_CC_SetBoolValue_NET("StrobeEnable", true); //Strobe使能
            nRet += MyCamera.MV_CC_SetCommandValue_NET("LineTriggerSoftware");//触发输出
            if (nRet != MyCamera.MV_OK)
            {
                //ShowErrorMsg("Set CaptureImage Time Fail!", nRet);
                return false;
            }
            return true;
        }
        /// <summary>未使用</summary>
        public override void LoadSetting(string filePath)
        {
            if (File.Exists(filePath))
                MyCamera.MV_CC_FeatureLoad_NET(filePath);
        }
        /// <summary>未使用</summary>
        public override void SaveSetting(string filePath)
        {
            MyCamera.MV_CC_FeatureSave_NET(filePath);
        }
        /// <summary> 相机设置</summary>
        public override void SetSetting()
        {
            int nRet = 0;
            //设置采集模式
            SetTriggerMode(TrigMode);
            //设置曝光时间
            SetExposureTime(ExposeTime);
            //nRet = MyCamera.MV_CC_SetFloatValue_NET("ExposureTime", ExposeTime);
            SetGain((long)Gain);
            //置帧率
            nRet = MyCamera.MV_CC_SetFloatValue_NET("AcquisitionFrameRate", float.Parse(Framerate));
            //设置ip
            //apiReturn = myApi.Gige_Camera_setIPAddress(m_Handle, uint.Parse(_UniqueLabel), uint.Parse(_DevDirExt));

        }
        public void GetSetting()
        {
            int nRet = 0;
            MyCamera.MVCC_ENUMVALUE stTriggerMode = new MyCamera.MVCC_ENUMVALUE();
            MyCamera.MVCC_ENUMVALUE stTriggerSource = new MyCamera.MVCC_ENUMVALUE();
            MyCamera.MVCC_ENUMVALUE stTriggerActivation = new MyCamera.MVCC_ENUMVALUE();
            nRet = MyCamera.MV_CC_GetTriggerMode_NET(ref stTriggerMode);
            nRet = MyCamera.MV_CC_GetTriggerSource_NET(ref stTriggerSource);
            if (stTriggerMode.nCurValue == (uint)eTrigMode.内触发)
                TrigMode = eTrigMode.内触发;
            else if (stTriggerSource.nCurValue == 7)//软触发
                TrigMode = eTrigMode.软触发;
            else if (stTriggerSource.nCurValue == 0) //Line0 触发
            {
                nRet += MyCamera.MV_CC_GetEnumValue_NET("TriggerActivation", ref stTriggerActivation);
                if (stTriggerActivation.nCurValue == 0)
                    TrigMode = eTrigMode.上升沿;
                else
                    TrigMode = eTrigMode.下降沿;
            }

            MyCamera.MVCC_FLOATVALUE stParam = new MyCamera.MVCC_FLOATVALUE();
            nRet = MyCamera.MV_CC_GetFloatValue_NET("ExposureTime", ref stParam);
            ExposeTime = stParam.fCurValue;
            MyCamera.MV_CC_GetFloatValue_NET("ResultingFrameRate", ref stParam);
            Framerate = stParam.fCurValue.ToString();
            //base.GetSetting();
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
                Logger.AddLog("HikVision:" + ex.Message, eMsgType.Error);
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
            try
            {
                Gain = value;
                int nRet = MyCamera.MV_CC_SetFloatValue_NET("Gain", value);
                if (nRet != MyCamera.MV_OK)
                {
                    Logger.AddLog("HikVision:" + "Gain Err", eMsgType.Error);
                }
            }
            catch (Exception ex)
            {
                Logger.AddLog("HikVision:" + ex.Message, eMsgType.Error);
            }
        }
        /// <summary>设置曝光</summary>
        public override void SetExposureTime(float value)
        {
            ExposeTime = value;
            int nRet = MyCamera.MV_CC_SetFloatValue_NET("ExposureTime", value);
            if (nRet != MyCamera.MV_OK)
            {
                Logger.AddLog("HikVision:" + "ExposureTime Err", eMsgType.Error);
            }
        }
        /// <summary>设置触发</summary>
        public override bool SetTriggerMode(eTrigMode mode)
        {
            int nRet = 0;
            if (mode == eTrigMode.内触发)
                nRet += MyCamera.MV_CC_SetEnumValue_NET("TriggerMode", 1); //之前是这个设置，不知道內触发是什么玩意，暂且设置为软触发nRet += MyCamera.MV_CC_SetEnumValue_NET("TriggerMode", 0);
            else
                nRet += MyCamera.MV_CC_SetEnumValue_NET("TriggerMode", 1);
            // ch:触发源选择:0 - Line0; | en:Trigger source select:0 - Line0;
            //           1 - Line1;
            //           2 - Line2;
            //           3 - Line3;
            //           4 - Counter;
            //           7 - Software;
            switch (mode)
            {
                case eTrigMode.内触发:   // no acquisition                    
                    break;
                case eTrigMode.软触发:   // freerunning
                    {
                        nRet += MyCamera.MV_CC_SetEnumValue_NET("TriggerSource", 7);
                        break;
                    }
                case eTrigMode.上升沿:   // Software trigger
                    {
                        nRet += MyCamera.MV_CC_SetEnumValue_NET("TriggerSource", 0);
                        nRet += MyCamera.MV_CC_SetEnumValue_NET("TriggerActivation", 0);
                        break;
                    }
                case eTrigMode.下降沿:   // Software trigger
                    {
                        nRet += MyCamera.MV_CC_SetEnumValue_NET("TriggerSource", 0);
                        nRet += MyCamera.MV_CC_SetEnumValue_NET("TriggerActivation", 1);
                        break;
                    }
            }
            if (nRet != MyCamera.MV_OK)
                return false;
            else
                return true;
        }
        #region 相机事件
        /// <summary>采集回调</summary>
        private void ImageCallbackFunc(IntPtr pData, ref MyCamera.MV_FRAME_OUT_INFO_EX pFrameInfo, IntPtr pUser)
        {
            try
            {
                MyCamera.MvGvspPixelType enDstPixelType;
                if (IsMonoData(pFrameInfo.enPixelType))
                {
                    enDstPixelType = MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono8;
                }
                else if (IsColorData(pFrameInfo.enPixelType))
                {
                    enDstPixelType = MyCamera.MvGvspPixelType.PixelType_Gvsp_RGB8_Packed;
                }
                else
                {
                    Trace.TraceError("{0} GrabImage Fail!ex: No such pixel type!");
                    return;
                }
                if (m_pBufForSaveImage == null)
                {
                    m_pBufForSaveImage = new byte[5120 * 5120 * 3 + 2048];
                }

                IntPtr pImage = Marshal.UnsafeAddrOfPinnedArrayElement(m_pBufForSaveImage, 0);

                MyCamera.MV_PIXEL_CONVERT_PARAM stConverPixelParam = new MyCamera.MV_PIXEL_CONVERT_PARAM();
                stConverPixelParam.nWidth = pFrameInfo.nWidth;
                stConverPixelParam.nHeight = pFrameInfo.nHeight;
                stConverPixelParam.pSrcData = pData;
                stConverPixelParam.nSrcDataLen = pFrameInfo.nFrameLen;
                stConverPixelParam.enSrcPixelType = pFrameInfo.enPixelType;
                stConverPixelParam.enDstPixelType = enDstPixelType;
                stConverPixelParam.pDstBuffer = pImage;
                stConverPixelParam.nDstBufferSize = m_nBufSizeForSaveImage;
                int nRet = MyCamera.MV_CC_ConvertPixelType_NET(ref stConverPixelParam);
                if (MyCamera.MV_OK != nRet)
                {
                    return;
                }


                HImage hImage = new HImage();
                if (enDstPixelType == MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono8)
                {
                    //************************Mono8 转 HImage*******************************
                    try
                    {
                        hImage = new HImage("byte", pFrameInfo.nWidth, pFrameInfo.nHeight, pImage);
                    }
                    catch (Exception ex)
                    {

                    }


                }
                else
                {
                    //*********************RGB8 转 Bitmap**************************
                    try
                    {

                        hImage.GenImageInterleaved(pImage, "rgb", pFrameInfo.nWidth, pFrameInfo.nHeight, -1, "byte", 0, 0, 0, 0, -1, 0);

                    }

                    catch (Exception ex)
                    {
                        Trace.TraceError("{0} GrabImage Fail!ex:{1}", ex);
                    }

                }

                Image = hImage;
                EventWait.Set();
                ImageGrab?.Invoke(Image);
            }
            catch (Exception ex)
            {
                EventWait.Set();
            }
        }
        private Boolean IsMonoData(MyCamera.MvGvspPixelType enGvspPixelType)
        {
            switch (enGvspPixelType)
            {
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono8:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono10:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono10_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono12:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono12_Packed:
                    return true;

                default:
                    return false;
            }
        }

        private Boolean IsColorData(MyCamera.MvGvspPixelType enGvspPixelType)
        {
            switch (enGvspPixelType)
            {
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR8:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG8:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB8:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG8:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR10:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG10:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB10:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG10:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR12:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG12:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB12:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG12:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR10_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG10_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB10_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG10_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR12_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG12_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB12_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG12_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_RGB8_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_YUV422_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_YUV422_YUYV_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_YCBCR411_8_CBYYCRYY:
                    return true;

                default:
                    return false;
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
            MyCamera.MV_CC_DEVICE_INFO_LIST m_pDeviceList = new MyCamera.MV_CC_DEVICE_INFO_LIST();
            int nRet;
            // ch:创建设备列表 en:Create Device List
            System.GC.Collect();
            nRet = MyCamera.MV_CC_EnumDevices_NET(MyCamera.MV_GIGE_DEVICE | MyCamera.MV_USB_DEVICE, ref m_pDeviceList);
            if (0 != nRet)
            {
                MessageView.Ins.MessageBoxShow("没有找到任何设备,请确认相机是否连接好!", eMsgType.Warn);
                return;
            }

            // ch:在窗体列表中显示设备名 | en:Display device name in the form list
            for (int i = 0; i < m_pDeviceList.nDeviceNum; i++)
            {
                MyCamera.MV_CC_DEVICE_INFO device = (MyCamera.MV_CC_DEVICE_INFO)Marshal.PtrToStructure(m_pDeviceList.pDeviceInfo[i], typeof(MyCamera.MV_CC_DEVICE_INFO));
                if (device.nTLayerType == MyCamera.MV_GIGE_DEVICE)
                {
                    IntPtr buffer = Marshal.UnsafeAddrOfPinnedArrayElement(device.SpecialInfo.stGigEInfo, 0);
                    MyCamera.MV_GIGE_DEVICE_INFO gigeInfo = (MyCamera.MV_GIGE_DEVICE_INFO)Marshal.PtrToStructure(buffer, typeof(MyCamera.MV_GIGE_DEVICE_INFO));
                    if (Ctemp == gigeInfo.chSerialNumber)//判断是否等于指定相机序号
                    {
                        CurDevice = device;
                        ExtInfo = device;
                        return;
                    }
                }
                else if (device.nTLayerType == MyCamera.MV_USB_DEVICE)
                {
                    IntPtr buffer = Marshal.UnsafeAddrOfPinnedArrayElement(device.SpecialInfo.stUsb3VInfo, 0);
                    MyCamera.MV_USB3_DEVICE_INFO usbInfo = (MyCamera.MV_USB3_DEVICE_INFO)Marshal.PtrToStructure(buffer, typeof(MyCamera.MV_USB3_DEVICE_INFO));
                    if (Ctemp == usbInfo.chSerialNumber)//判断是否等于指定相机序号
                        if (Ctemp == usbInfo.chSerialNumber)//判断是否等于指定相机序号
                        {
                            CurDevice = device;
                            ExtInfo = device;
                            return;
                        }

                }
            }
            MessageView.Ins.MessageBoxShow("没有找当前到设备,请确认相机是否连接好!", eMsgType.Warn);
            return;
        }
        //[OnSerializing()] 序列化之前
        //[OnSerialized()] 序列化之后
        //[OnDeserializing()] 反序列化之前
        [OnDeserialized()] //反序列化之后

        internal void OnDeserializedMethod(StreamingContext context)
        {
            MyCamera = new MyCamera();
            if (SerialNo == null || SerialNo == "")
            {
                return;
            }
            m_pBufForSaveImage = new byte[5120 * 5120 * 3 + 2048];
            FindCBySN(SerialNo);
            ConnectDev();
        }
        private void ShowErrorMsg(string csMessage, int nErrorNum)
        {
            string errorMsg;
            if (nErrorNum == 0)
            {
                errorMsg = csMessage;
            }
            else
            {
                errorMsg = csMessage + ": Error =" + String.Format("{0:X}", nErrorNum);
            }

            switch (nErrorNum)
            {
                case MyCamera.MV_E_HANDLE: errorMsg += " Error or invalid handle "; break;
                case MyCamera.MV_E_SUPPORT: errorMsg += " Not supported function "; break;
                case MyCamera.MV_E_BUFOVER: errorMsg += " Cache is full "; break;
                case MyCamera.MV_E_CALLORDER: errorMsg += " Function calling order error "; break;
                case MyCamera.MV_E_PARAMETER: errorMsg += " Incorrect parameter "; break;
                case MyCamera.MV_E_RESOURCE: errorMsg += " Applying resource failed "; break;
                case MyCamera.MV_E_NODATA: errorMsg += " No data "; break;
                case MyCamera.MV_E_PRECONDITION: errorMsg += " Precondition error, or running environment changed "; break;
                case MyCamera.MV_E_VERSION: errorMsg += " Version mismatches "; break;
                case MyCamera.MV_E_NOENOUGH_BUF: errorMsg += " Insufficient memory "; break;
                case MyCamera.MV_E_UNKNOW: errorMsg += " Unknown error "; break;
                case MyCamera.MV_E_GC_GENERIC: errorMsg += " General error "; break;
                case MyCamera.MV_E_GC_ACCESS: errorMsg += " Node accessing condition error "; break;
                case MyCamera.MV_E_ACCESS_DENIED: errorMsg += " No permission "; break;
                case MyCamera.MV_E_BUSY: errorMsg += " Device is busy, or network disconnected "; break;
                case MyCamera.MV_E_NETER: errorMsg += " Network error "; break;
            }
            Logger.AddLog("HikVision:" + errorMsg, eMsgType.Error);
        }
    }
}
