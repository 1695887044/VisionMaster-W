using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Common.Helper;
using VM.Start.PersistentData;

namespace VM.Start.Common.RightControl
{
    public class IsEnableControl : NotifyPropertyBase
    {
        #region 属性单例模式

        private static Lazy<IsEnableControl> Instance = new Lazy<IsEnableControl>(
            () => new IsEnableControl()
        );

        public IsEnableControl() { }

        public static IsEnableControl Ins { get; set; } = Instance.Value;

        #endregion
        #region Prop
        private bool _QuickMode = true;
        public bool QuickMode
        {
            get { return _QuickMode; }
            set
            {
                _QuickMode = value;
                this.RaisePropertyChanged();
            }
        }

        private bool _CommunicationSet = true;
        public bool CommunicationSet
        {
            get { return _CommunicationSet; }
            set
            {
                _CommunicationSet = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _HardwareConfig = true;
        public bool HardwareConfig
        {
            get { return _HardwareConfig; }
            set
            {
                _HardwareConfig = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _Camera = true;
        public bool Camera
        {
            get { return _Camera; }
            set
            {
                _Camera = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _Temperature = true;
        public bool Temperature
        {
            get { return _Temperature; }
            set
            {
                _Temperature = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _Barcode = true;
        public bool Barcode
        {
            get { return _Barcode; }
            set
            {
                _Barcode = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _HeightSensor = true;
        public bool HeightSensor
        {
            get { return _HeightSensor; }
            set
            {
                _HeightSensor = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _PressureSensor = true;
        public bool PressureSensor
        {
            get { return _PressureSensor; }
            set
            {
                _PressureSensor = value;
                this.RaisePropertyChanged();
            }
        }

        private bool _InputAndOutput = true;
        public bool InputAndOutput
        {
            get { return _InputAndOutput; }
            set
            {
                _InputAndOutput = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _open = true;
        public bool Open
        {
            get { return _open; }
            set
            {
                _open = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _edit = true;
        public bool Edit
        {
            get { return _edit; }
            set
            {
                _edit = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _save = true;
        public bool Save
        {
            get { return _save; }
            set
            {
                _save = value;
                this.RaisePropertyChanged();
            }
        }

        private bool _runOnce = true;
        public bool RunOnce
        {
            get { return _runOnce; }
            set
            {
                _runOnce = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _runCycle = true;
        public bool RunCycle
        {
            get { return _runCycle; }
            set
            {
                _runCycle = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _stop = true;
        public bool Stop
        {
            get { return _stop; }
            set
            {
                _stop = value;
                this.RaisePropertyChanged();
            }
        }

        private bool _OpenFile = true;
        public bool OpenFile
        {
            get { return _OpenFile; }
            set
            {
                _OpenFile = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _SaveFile = true;
        public bool SaveFile
        {
            get { return _SaveFile; }
            set
            {
                _SaveFile = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _systemConfig = true;
        public bool SystemConfig
        {
            get { return _systemConfig; }
            set
            {
                _systemConfig = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _OpenOrCloseCamera = true;
        public bool OpenOrCloseCamera
        {
            get { return _OpenOrCloseCamera; }
            set
            {
                _OpenOrCloseCamera = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _RedLight = true;
        public bool RedLight
        {
            get { return _RedLight; }
            set
            {
                _RedLight = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _DeviceParam = true;
        public bool DeviceParam
        {
            get { return _DeviceParam; }
            set
            {
                _DeviceParam = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _SystemParam = true;
        public bool SystemParam
        {
            get { return _SystemParam; }
            set
            {
                _SystemParam = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _View = true;
        public bool View
        {
            get { return _View; }
            set
            {
                _View = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _ManufactureParam = true;
        public bool ManufactureParam
        {
            get { return _ManufactureParam; }
            set
            {
                _ManufactureParam = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _TemplateLayout = true;
        public bool TemplateLayout
        {
            get { return _TemplateLayout; }
            set
            {
                _TemplateLayout = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _CameraSetting = true;
        public bool CameraSetting
        {
            get { return _CameraSetting; }
            set
            {
                _CameraSetting = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _ServoDebug = true;
        public bool ServoDebug
        {
            get { return _ServoDebug; }
            set
            {
                _ServoDebug = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _IODebug = true;
        public bool IODebug
        {
            get { return _IODebug; }
            set
            {
                _IODebug = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _Home = true;
        public bool Home
        {
            get { return _Home; }
            set
            {
                _Home = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _UIDesign = true;
        public bool UIDesign
        {
            get { return _UIDesign; }
            set
            {
                _UIDesign = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _PowerDebug = true;
        public bool PowerDebug
        {
            get { return _PowerDebug; }
            set
            {
                _PowerDebug = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _LaserDebug = true;
        public bool LaserDebug
        {
            get { return _LaserDebug; }
            set
            {
                _LaserDebug = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _newSolution = true;
        public bool NewSolution
        {
            get { return _newSolution; }
            set
            {
                _newSolution = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _SolutionList = true;
        public bool SolutionList
        {
            get { return _SolutionList; }
            set
            {
                _SolutionList = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _GlobalVar = true;
        public bool GlobalVar
        {
            get { return _GlobalVar; }
            set
            {
                _GlobalVar = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _CameraSet = true;
        public bool CameraSet
        {
            get { return _CameraSet; }
            set
            {
                _CameraSet = value;
                this.RaisePropertyChanged();
            }
        }
        private bool _LaserSet = true;
        public bool LaserSet
        {
            get { return _LaserSet; }
            set
            {
                _LaserSet = value;
                this.RaisePropertyChanged();
            }
        }
        #endregion
    }
}
