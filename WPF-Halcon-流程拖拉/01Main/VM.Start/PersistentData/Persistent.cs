using ControlzEx.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Common.Enums;
using VM.Start.Common.Helper;
using VM.Start.Models;

namespace VM.Start.PersistentData
{
    public class Persistent:NotifyPropertyBase
    {
        #region Singleton
        private static Persistent _instance = new Persistent();
        private Persistent()
        {

        }
        public static Persistent Ins
        {
            set { _instance = value; }
            get { return _instance; }
        }
        #endregion
        #region Method
        public void SavePersistent()
        {
            SerializeHelp.SerializeAndSaveFile(Ins, FilePaths.RecipePath + SystemConfig.Ins.CurrentRecipe);
        }
        public void LoadPersistent()
        {
            Ins = SerializeHelp.Deserialize<Persistent>(FilePaths.RecipePath + SystemConfig.Ins.CurrentRecipe);
            if (Ins == null)
            {
                Ins = new Persistent();
            }
        }
        #endregion
        #region Prop
        private ObservableCollection<PointModel> _powerUpLimitList = new ObservableCollection<PointModel>();
        public ObservableCollection<PointModel> PowerUpLimitList
        {
            get { return _powerUpLimitList; }
            set { _powerUpLimitList = value; this.RaisePropertyChanged(); }
        }
        private ObservableCollection<PointModel> _powerDownLimitList = new ObservableCollection<PointModel>();
        public ObservableCollection<PointModel> PowerDownLimitList
        {
            get { return _powerDownLimitList; }
            set { _powerDownLimitList = value; this.RaisePropertyChanged(); }
        }
        #region 轴参数
        private double _X_PosWork = 0;
        /// <summary>
        /// X轴工作位置
        /// </summary>
        public double X_PosWork
        {
            get { return _X_PosWork; }
            set { _X_PosWork = value; this.RaisePropertyChanged(); }
        }
        private double _Z_PosWork = 0;
        /// <summary>
        /// Z轴工作位置
        /// </summary>
        public double Z_PosWork
        {
            get { return _Z_PosWork; }
            set { _Z_PosWork = value; this.RaisePropertyChanged(); }
        }
        private double _Z_Vel = 1;
        /// <summary>
        /// Z轴移动速度
        /// </summary>
        public double Z_Vel
        {
            get { return _Z_Vel; }
            set { _Z_Vel = value; this.RaisePropertyChanged(); }
        }
        private double _X_Vel = 1;
        /// <summary>
        /// X轴移动速度
        /// </summary>
        public double X_Vel
        {
            get { return _X_Vel; }
            set { _X_Vel = value; this.RaisePropertyChanged(); }
        }
        private double _RotateVel;
        /// <summary>
        /// R轴旋转速度
        /// </summary>
        public double RotateVel
        {
            get { return _RotateVel; }
            set { _RotateVel = value; this.RaisePropertyChanged(); }
        }

        #endregion
        private eProcessMode _ProcessMode;
        /// <summary>
        /// 加工模式
        /// </summary>
        public eProcessMode ProcessMode
        {
            get { return _ProcessMode; }
            set { _ProcessMode = value; this.RaisePropertyChanged(); }
        }
        private ObservableCollection<RippleEditModel> _RippleEditList;
        public ObservableCollection<RippleEditModel> RippleEditList
        {
            get 
            {
                if (_RippleEditList == null)
                {
                    _RippleEditList = new ObservableCollection<RippleEditModel>();
                }
                return _RippleEditList; 
            }
            set { _RippleEditList = value; this.RaisePropertyChanged(); }
        }
        private uint _TimeOutStopModulate = 4000;
        /// <summary>
        /// 超时停光
        /// </summary>
        public uint TimeOutStopModulate
        {
            get { return _TimeOutStopModulate; }
            set { _TimeOutStopModulate = value; this.RaisePropertyChanged(); }
        }

        #endregion


    }
}
