using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Web.UI.WebControls;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ControlzEx.Standard;
using EventMgrLib;
using Microsoft.Win32;
using Mono.CSharp.Linq;
using VM.Start.Common;
using VM.Start.Common.Enums;
using VM.Start.Common.Extension;
using VM.Start.Common.Helper;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.Dialogs.Views;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.PersistentData;
using VM.Start.Services;
using VM.Start.Views;
using WPFLocalizeExtension.Engine;

namespace VM.Start.ViewModels
{
    [Serializable]
    public class HardwareConfigViewModel : NotifyPropertyBase
    {
        #region Singleton

        //private static readonly HardwareConfigViewModel _instance = new HardwareConfigViewModel();

        private HardwareConfigViewModel()
        {
        }


        public static HardwareConfigViewModel Ins
        {
            get
            {
                if (Solution.Ins.HardwareConfigViewModel == null)
                {
                    Solution.Ins.HardwareConfigViewModel = new HardwareConfigViewModel();
                }
                return Solution.Ins.HardwareConfigViewModel;
            }
        }

        #endregion

        #region Prop
        /// <summary>轴卡列表</summary>
        public ObservableCollection<MotionBase> MotionModels { get; set; } = new ObservableCollection<MotionBase>();
        private MotionBase _SelectedMotion;

        public MotionBase SelectedMotion
        {
            get { return _SelectedMotion; }
            set 
            { 
                _SelectedMotion = value; RaisePropertyChanged(); 
                HardwareConfigView.Ins.dg_Axis.ItemsSource = SelectedMotion.Axis;
                HardwareConfigView.Ins.dg_DI.ItemsSource = SelectedMotion.DI;
                HardwareConfigView.Ins.dg_DO.ItemsSource = SelectedMotion.DO;
            }
        }
        private List<string> _MotionBrands = PluginService.PluginDic_Motion.Keys.ToList();

        public List<string> MotionBrands
        {
            get { return _MotionBrands; }
            set { _MotionBrands = value; }
        }
        private string _SelectedMotionBrand;

        public string SelectedMotionBrand
        {
            get { return _SelectedMotionBrand; }
            set { _SelectedMotionBrand = value; }
        }
        private List<string> _MotionTypes = new List<string>();
        public List<string> MotionTypes
        {
            get { return _MotionTypes; }
            set { _MotionTypes = value; }
        }
        private string _MotionType;

        public string MotionType
        {
            get { return _MotionType; }
            set { _MotionType = value; }
        }
        private IOIn _SelectedDI;

        public IOIn SelectedDI
        {
            get { return _SelectedDI; }
            set { _SelectedDI = value; }
        }
        private IOOut _SelectedDO;

        public IOOut SelectedDO
        {
            get { return _SelectedDO; }
            set { _SelectedDO = value; }
        }
        private AxisParam _SelectedAxis;

        public AxisParam SelectedAxis
        {
            get { return _SelectedAxis; }
            set { _SelectedAxis = value; }
        }
        private string _CurAxisName;

        public string CurAxisName
        {
            get { return _CurAxisName; }
            set { _CurAxisName = value; RaisePropertyChanged(); }
        }


        #endregion

        #region Command
        [NonSerialized]
        private CommandBase _ActivatedCommand;
        public CommandBase ActivatedCommand
        {
            get
            {
                if (_ActivatedCommand == null)
                {
                    _ActivatedCommand = new CommandBase((obj) =>
                    {
                        if (HardwareConfigView.Ins.IsClosed)
                        {
                            HardwareConfigView.Ins.IsClosed = false;
                        }
                    });
                }
                return _ActivatedCommand;
            }
        }

        [NonSerialized]
        private CommandBase _LoadedCommand;
        public CommandBase LoadedCommand
        {
            get
            {
                if (_LoadedCommand == null)
                {
                    _LoadedCommand = new CommandBase((obj) =>
                    {
                        if (SelectedMotion!=null && SelectedMotion.Axis != null && SelectedMotion.Axis.Count > 0)
                        {
                            HardwareConfigView.Ins.propGrid.SelectedObject = SelectedAxis;
                            HardwareConfigView.Ins.gd.DataContext = SelectedAxis;
                            if (SelectedAxis!=null)
                            {
                                CurAxisName = $"{SelectedAxis.AxisName}-当前位置:";
                            }
                            HardwareConfigView.Ins.dg_Axis.ItemsSource = SelectedMotion.Axis;
                            HardwareConfigView.Ins.dg_DI.ItemsSource = SelectedMotion.DI;
                            HardwareConfigView.Ins.dg_DO.ItemsSource = SelectedMotion.DO;
                            HardwareConfigView.Ins.dg_Axis.SelectedIndex = 0;
                        }
                    });
                }
                return _LoadedCommand;
            }
        }
        [NonSerialized]
        private CommandBase _ConfirmCommand;
        public CommandBase ConfirmCommand
        {
            get
            {
                if (_ConfirmCommand == null)
                {
                    _ConfirmCommand = new CommandBase((obj) =>
                    {
                        foreach (var item in SelectedMotion.DO)
                        {
                            item.IsForce = false;
                        }
                        //SerializeHelp.BinSerializeAndSaveFile(SelectedMotion, FilePaths.MotionConfig);
                        HardwareConfigView.Ins.Close();
                    });
                }
                return _ConfirmCommand;
            }
        }
        [NonSerialized,Browsable(false)]
        private CommandBase _MouseUpCommand;
        public CommandBase MouseUpCommand
        {
            get
            {
                if (_MouseUpCommand == null)
                {
                    _MouseUpCommand = new CommandBase((obj) =>
                    {
                        switch (obj.ToString())
                        {
                            case "DI":
                                if (SelectedDI == null) return;
                                HardwareConfigView.Ins.propGrid.SelectedObject = SelectedDI;
                                break;
                            case "DO":
                                if (SelectedDO == null) return;
                                HardwareConfigView.Ins.propGrid.SelectedObject = SelectedDO;
                                break;
                            case "Axis":
                                if (SelectedAxis == null) return;
                                HardwareConfigView.Ins.propGrid.SelectedObject = SelectedAxis;
                                HardwareConfigView.Ins.gd.DataContext = SelectedAxis;
                                CurAxisName = $"{SelectedAxis.AxisName}-当前位置:";
                                break;
                            case "Motion":
                                if (SelectedMotion == null) return;
                                HardwareConfigView.Ins.propGrid.SelectedObject = SelectedMotion;
                                break;
                            default:
                                break;
                        }
                    });
                }
                return _MouseUpCommand;
            }
        }
        [NonSerialized]
        private CommandBase _DataOperateCommand;
        public CommandBase DataOperateCommand
        {
            get
            {
                if (_DataOperateCommand == null)
                {
                    _DataOperateCommand = new CommandBase((obj) =>
                    {
                        switch (obj)
                        {
                            case "Add":
                                try
                                {
                                    if (HardwareConfigView.Ins.cmbMotionBrand.SelectedIndex == -1) return;
                                    //根据选中的插件 new一个 模块
                                    PluginsInfo m_PluginsInfo = PluginService.PluginDic_Motion[MotionBrands[HardwareConfigView.Ins.cmbMotionBrand.SelectedIndex]];
                                    MotionBase module = (MotionBase)Activator.CreateInstance(m_PluginsInfo.ModuleType);
                                    if (module == null) return;
                                    module.MotionType = MotionType;
                                    //确定新模块的不重名名称
                                    if (MotionModels != null)
                                    {
                                        if (MotionModels.Count > 0)
                                        {
                                            List<string> nameList = MotionModels.Select(c => c.MotionNo).ToList();
                                            while (true)
                                            {
                                                if (!nameList.Contains("Dev" + MotionBase.LastNo))
                                                {
                                                    break;//没有重名就跳出循环
                                                }
                                                MotionBase.LastNo++;
                                            }
                                        }
                                        else
                                        {
                                            MotionBase.LastNo++;
                                        }
                                    }
                                    module.MotionNo = "Dev" + MotionBase.LastNo;
                                    module.Remarks = m_PluginsInfo.ModuleName + MotionBase.LastNo;
                                    MotionModels.Add(module);
                                    SelectedMotion = module;
                                    //module.Init();
                                }
                                catch (Exception ex)
                                {
                                    Logger.GetExceptionMsg(ex);
                                }
                                break;
                            case "Delete":
                                if (SelectedMotion == null) return;
                                MotionModels.Remove(SelectedMotion);
                                break;
                            case "Modify":
                                break;

                            default:
                                break;
                        }
                    });
                }
                return _DataOperateCommand;
            }
        }

        #endregion

        #region Method

        #endregion
    }
}
