using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Common.Helper;
using VM.Start.Core;

namespace Plugin.MarkMatSingle
{
    [Category("激光")]
    [DisplayName("MarkMat-单头")]
    [Serializable]
    public class MarkMatSingle : NotifyPropertyBase, ILaserDevice
    {
        private int laserIndex;
        private bool isInit;
        private bool isMarking;
        private string laserName;
        private string laserFilePath;

        private AxMMMARKLib.AxMMMark laserModel = null;

        [NonSerialized]
        public WPFMarkMatSingle LaserContent;

        public MarkMatSingle()
        {
            LaserContent = new WPFMarkMatSingle();
        }

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
            }
        }
        public Action<int> MarkEnd { get; set; }

        public bool IsMarking
        {
            get => isMarking;
            private set
            {
                isMarking = value;
                RaisePropertyChanged();
            }
        }
        public string LaserName
        {
            get => laserName;
            set => laserName = value;
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

        public object LaserControl
        {
            get { return LaserContent; }
        }

        public bool Finish()
        {
            return true;
        }

        public bool GoEdit(string filePath)
        {
            return true;
        }

        public bool Initial()
        {
            return true;
        }

        public bool LoadFile(string filePath)
        {
            return true;
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
            return true;
        }
    }
}
