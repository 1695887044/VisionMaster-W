using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VM.Halcon.Config;
using VM.Start.Common;
using VM.Start.Common.Enums;
using VM.Start.Common.Helper;
using VM.Start.Events;
using VM.Start.Models;

namespace VM.Start.Core
{
    public delegate void ImageGrabcallback(HImage img);
    [Serializable]
    public class CameraBase:NotifyPropertyBase
    {
        #region 属性
        /// <summary>回调事件 </summary>
        [NonSerialized]
        public ImageGrabcallback ImageGrab = null;
        /// <summary>采集图像 </summary>
        [NonSerialized]
        public HImage Image = new HImage();
        /// <summary>采集信号 </summary>
        [NonSerialized]
        public AutoResetEvent EventWait = new AutoResetEvent(false);
        /// <summary>软触发时收到图像信号-同步</summary>
        [NonSerialized]
        public AutoResetEvent SignalWait = new AutoResetEvent(false);
        /// <summary>软触发时收到图像信号-异步</summary>
        [NonSerialized]
        public AutoResetEvent GetSignalWait = new AutoResetEvent(false);
        /// <summary>扩展信息 </summary>
        [NonSerialized]
        public object ExtInfo;
        /// <summary>触发模式 </summary>
        private eTrigMode _TrigMode = eTrigMode.软触发;
        public eTrigMode TrigMode
        {
            get { return _TrigMode; }
            set { Set(ref _TrigMode, value); }
        }
        public Array TrigModes { set; get; } = Enum.GetValues(typeof(eTrigMode));
        /// <summary>最新编号 </summary>
        public static int LastNo = 0;
        private string _CameraNo;
        /// <summary>设备自己编号 </summary>
        public string CameraNo
        {
            get { return _CameraNo; }
            set { _CameraNo = value; RaisePropertyChanged(); }
        }
        /// <summary>设备内部编号</summary>
        public string SerialNo { set; get; }
        /// <summary>相机类型</summary>
        public string CameraType { set; get; }
        /// <summary>设备内部IP</summary>
        public string CameraIP { set; get; }
        /// <summary>备注</summary>
        public string Remarks { get; set; }
        [NonSerialized]
        private bool _Connected = false;

        /// <summary>初始连接状态</summary>
        public bool Connected
        {
            get { return _Connected; }
            set { Set(ref _Connected, value,new Action(() =>EventMgrLib.EventMgr.Ins.GetEvent<HardwareChangedEvent>().Publish())); }
        }
        /// <summary>最大高度</summary>
        public int WidthMax { set; get; } = 0;
        /// <summary>最大高度 </summary>
        public int HeightMax { set; get; } = 0;
        private float _ExposeTime = 10000;

        /// <summary>曝光 </summary>
        public float ExposeTime
        {
            get { return _ExposeTime; }
            set { _ExposeTime = value; RaisePropertyChanged();  }
        }
        private float _Gain = 0;

        /// <summary>增益</summary>
        public float Gain
        {
            get { return _Gain; }
            set
            {
                _Gain = value; RaisePropertyChanged(); 
            }
        }
        public float ExposeTimeMax { set; get; } = 0;
        public float ExposeTimeMin { set; get; } = 0;
        /// <summary>宽度</summary>
        public int Width { set; get; } = 0;
        /// <summary>高度</summary>
        public int Height { set; get; } = 0;
        public float GainMax { set; get; } = 0;
        public float GainMin { set; get; } = 0;
        /// <summary>帧率 </summary>
        public string Framerate { set; get; } = "0";
        #endregion
        #region 构造函数
        /// <summary> 创建相机实体</summary>
        public CameraBase() 
        {
            EventMgrLib.EventMgr.Ins.GetEvent<SoftwareExitEvent>().Subscribe(DisConnectDev);
        }
        public CameraBase(string _SerialNo)
        {
            LastNo++;
            CameraNo = "Dev" + LastNo;
        }
        #endregion
        #region 虚函数
        /// <summary> 搜索相机</summary>    
        public virtual List<CameraInfoModel> SearchCameras() { return null; }
        /// <summary> 建立连接</summary>
        public virtual void ConnectDev()
        {
            string filePath = FilePaths.ConfigFilePath + "CameraConfig";
            if (!Directory.Exists(filePath))
            {
                Directory.CreateDirectory(filePath);
            }
            LoadSetting(filePath + this.SerialNo);
            EventMgrLib.EventMgr.Ins.GetEvent<HardwareChangedEvent>().Publish();
        }
        /// <summary> 断开连接</summary>
        public virtual void DisConnectDev()
        {
            EventMgrLib.EventMgr.Ins.GetEvent<HardwareChangedEvent>().Publish();
        }
        public virtual void SetGain(float value) { }
        public virtual void SetExposureTime(float value) { }
        /// <summary>抓捕图像</summary>
        /// <param name="byHand">是否手动采图</param>
        public virtual bool CaptureImage(bool byHand) { return true; }
        public virtual bool SetOutPut(int index,int time) { return true; }
        /// <summary> 导出设置</summary>
        public virtual void SaveSetting(string filePath) { }
        /// <summary> 导入设置</summary>
        public virtual void LoadSetting(string filePath) { }
        /// <summary> 相机设置</summary>
        public virtual void SetSetting() { }
        /// <summary>设置触发模式 </summary>
        public virtual bool SetTriggerMode(eTrigMode mode)
        {
            return true;
        }
        /// <summary>参数设置</summary>
        public virtual void CameraChanged(ChangType changTyp) { }
        #endregion
        [OnDeserializing()]
        internal void OnDeSerializingMethod(StreamingContext context)
        {
            Image = new HImage();
            EventMgrLib.EventMgr.Ins.GetEvent<SoftwareExitEvent>().Subscribe(DisConnectDev);
            SignalWait = new AutoResetEvent(false);//采集信号
            GetSignalWait = new AutoResetEvent(false);//软触收到图像信号
            EventWait = new AutoResetEvent(false);
        }
    }
}
