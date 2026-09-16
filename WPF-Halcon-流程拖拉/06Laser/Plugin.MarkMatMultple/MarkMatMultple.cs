using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Common.Helper;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.Events;

namespace Plugin.MarkMatMultple
{
    [Category("激光")]
    [DisplayName("MarkMat-多头")]
    [Serializable]
    public class MarkMatMultple : NotifyPropertyBase, ILaserDevice
    {
        private int laserIndex;

        [NonSerialized]
        private bool isInit;

        [NonSerialized]
        private bool isMarking;

        private string laserName;
        private string laserFilePath;

        [NonSerialized]
        private AxMMMark_1Lib.AxMMMark_1 laserModel = null;

        [NonSerialized]
        public WPFMarkMatMultple LaserContent;

        public object LaserControl
        {
            get { return LaserContent; }
        }

        public MarkMatMultple() { }

        public int LaserIndex
        {
            get => laserIndex;
            set
            {
                laserIndex = value;
                RaisePropertyChanged();
            }
        }

        public bool IsInit
        {
            get => isInit;
            set
            {
                isInit = value;
                RaisePropertyChanged();
                EventMgrLib.EventMgr.Ins.GetEvent<HardwareChangedEvent>().Publish();
            }
        }
        public Action<int> MarkEnd { get; set; }

        public bool IsMarking
        {
            get
            {
                if (laserModel == null)
                    return false;
                return laserModel.IsMarking() == 0;
            }
            private set
            {
                isMarking = value;
                RaisePropertyChanged();
            }
        }
        public string LaserName
        {
            get => laserName;
            set
            {
                laserName = value;
                RaisePropertyChanged();
            }
        }
        public string LaserFilePath
        {
            get => laserFilePath;
            set
            {
                laserFilePath = value;
                RaisePropertyChanged();
            }
        }

        private void OnSoftwareExit()
        {
            Finish();
        }

        public bool Finish()
        {
            IsInit = false;
            return laserModel.Finish() == 0;
        }

        public bool GoEdit(string filePath)
        {
            return true;
        }

        public bool Initial()
        {
            if (IsInit)
            {
                return true;
            }
            EventMgrLib.EventMgr.Ins.GetEvent<SoftwareExitEvent>().Unsubscribe(OnSoftwareExit);
            EventMgrLib.EventMgr.Ins.GetEvent<SoftwareExitEvent>().Subscribe(OnSoftwareExit);
            if (LaserContent == null)
            {
                LaserContent = new WPFMarkMatMultple();
            }
            if (laserModel == null)
            {
                laserModel = LaserContent.winMarkMatMult.markMult;
            }
            string strCfg = $"/cfg_config_MM{LaserIndex + 1}";
            int retCode = laserModel.InitialExt(strCfg);
            IsInit = retCode == 0;
            if (!IsInit)
            {
                Logger.AddLog(
                    $"激光：{laserName} 初始化失败，错误代码：{retCode}",
                    VM.Start.Common.Enums.eMsgType.Error
                );
            }
            return IsInit;
        }

        public bool LoadFile(string filePath)
        {
            return laserModel.LoadFile(filePath) == 0;
        }

        public bool MarkDataLock()
        {
            return true;
        }

        public bool MarkDataRotate(double offsetX, double offsetY, double angle)
        {
            return true;
        }

        public bool MarkDataUnLock()
        {
            return true;
        }

        public bool StartMarkExt(
            double offsetX = 0,
            double offsetY = 0,
            double angle = 0,
            int iMode = 4
        )
        {
            if (offsetX != 0 || offsetY != 0 || angle != 0)
            {
                var ret = laserModel.SetMatrix(offsetX, offsetY, angle);
                if (ret != 0)
                {
                    return false;
                }
            }

            int markRet = laserModel.StartMarkingExt(iMode);
            return markRet == 0;
        }

        [OnDeserialized()] //反序列化之后
        internal void OnDeserializedMethod(StreamingContext context)
        {
            Initial();
        }
    }
}
