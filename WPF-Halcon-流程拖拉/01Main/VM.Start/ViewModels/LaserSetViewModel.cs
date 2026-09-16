using EventMgrLib;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using VM.Halcon.Helper;
using VM.Start.Common.Helper;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.Dialogs.Views;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.Services;
using VM.Start.Views;
using NotifyPropertyBase = VM.Start.Common.Helper.NotifyPropertyBase;

namespace VM.Start.ViewModels
{
    [Serializable]
    public class LaserSetViewModel : NotifyPropertyBase
    {
        #region Singleton

        //private static readonly CameraSetViewModel _instance = new CameraSetViewModel();

        [NonSerialized]
        object _LaserControl;

        public object LaserControl
        {
            get => _LaserControl;
            set
            {
                _LaserControl = value;
                RaisePropertyChanged();
            }
        }

        private LaserSetViewModel() { }

        public static LaserSetViewModel Ins
        {
            get
            {
                if (Solution.Ins.LaserSetViewModel == null)
                {
                    Solution.Ins.LaserSetViewModel = new LaserSetViewModel();
                }
                return Solution.Ins.LaserSetViewModel;
            }
        }

        #endregion

        #region Property
        private List<string> _LaserTypes = PluginService.PluginDic_Laser.Keys.ToList();

        /// <summary>
        /// 激光设备列表
        /// </summary>
        public ObservableCollection<ILaserDevice> LaserModels { get; set; } =
            new ObservableCollection<ILaserDevice>();

        public List<string> LaserTypes
        {
            get { return _LaserTypes; }
            set { _LaserTypes = value; }
        }

        public string SelectedLaserType
        {
            get => _SelectedLaserType;
            set
            {
                _SelectedLaserType = value;
                RaisePropertyChanged();
            }
        }

        [NonSerialized]
        string _SelectedLaserType;

        ILaserDevice _SelectedLaserModel;
        public ILaserDevice SelectedLaserModel
        {
            get => _SelectedLaserModel;
            set
            {
                _SelectedLaserModel = value;
                RaisePropertyChanged();
                try
                {
                    LaserControl = _SelectedLaserModel?.LaserControl;
                }
                catch (Exception ex)
                {
                    Logger.GetExceptionMsg(ex);
                }
            }
        }

        #endregion


        #region Command
        [NonSerialized]
        CommandBase _DataOperateCommand;
        public CommandBase DataOperateCommand
        {
            get
            {
                if (_DataOperateCommand == null)
                {
                    _DataOperateCommand = new CommandBase(
                        (obj) =>
                        {
                            if (obj.ToString().Equals("Add"))
                            {
                                try
                                {
                                    int currLaserType = LaserTypes.FindIndex(
                                        t => t.Equals(SelectedLaserType)
                                    );
                                    if (currLaserType == -1)
                                    {
                                        Logger.AddLog("未选择激光类型", Common.Enums.eMsgType.Warn);
                                        return;
                                    }
                                    //根据选中的插件 new一个 模块
                                    PluginsInfo m_PluginsInfo = PluginService.PluginDic_Laser[
                                        LaserTypes[currLaserType]
                                    ];
                                    ILaserDevice module = (ILaserDevice)
                                        Activator.CreateInstance(m_PluginsInfo.ModuleType);

                                    int laserIdx = 0;
                                    string title = "Dev_";
                                    //确定新模块的不重名名称
                                    if (LaserModels != null)
                                    {
                                        List<string> nameList = LaserModels
                                            .Select(c => c.LaserName)
                                            .ToList();
                                        while (true)
                                        {
                                            if (!nameList.Contains(title + laserIdx.ToString()))
                                            {
                                                break; //没有重名就跳出循环
                                            }
                                            laserIdx++;
                                        }
                                        module.LaserName = title + laserIdx.ToString();
                                        module.LaserIndex = laserIdx;
                                        module.Initial();
                                        LaserModels.Add(module);
                                        EventMgrLib.EventMgr.Ins
                                            .GetEvent<HardwareChangedEvent>()
                                            .Publish();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Logger.GetExceptionMsg(ex);
                                }
                            }
                            else if (obj.ToString().Equals("Delete"))
                            {
                                if (SelectedLaserModel == null)
                                    return;
                                LaserModels.Remove(SelectedLaserModel);
                                EventMgrLib.EventMgr.Ins.GetEvent<HardwareChangedEvent>().Publish();
                            }
                        }
                    );
                }
                return _DataOperateCommand;
            }
        }

        [NonSerialized]
        CommandBase _LaserFilePathCommand;
        public CommandBase LaserFilePathCommand
        {
            get
            {
                if (_LaserFilePathCommand == null)
                {
                    _LaserFilePathCommand = new CommandBase(obj =>
                    {
                        if (SelectedLaserModel == null)
                        {
                            return;
                        }
                        OpenFileDialog openFileDialog = new OpenFileDialog();
                        openFileDialog.Filter = "图档文件 (*.ezm)|*.ezm;|所有文件(*.*)|*.*";
                        if (openFileDialog.ShowDialog() == true)
                        {
                            SelectedLaserModel.LaserFilePath = openFileDialog.FileName;
                            var isLoaded = SelectedLaserModel.LoadFile(openFileDialog.FileName);
                        }
                    });
                }
                return _LaserFilePathCommand;
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
                    _ConfirmCommand = new CommandBase(
                        (obj) =>
                        {
                            LaserSetView.Ins.Close();
                            EventMgrLib.EventMgr.Ins.GetEvent<HardwareChangedEvent>().Publish();
                        }
                    );
                }
                return _ConfirmCommand;
            }
        }

        [NonSerialized]
        private CommandBase _ButtonOperateCommand;
        public CommandBase ButtonOperateCommand
        {
            get
            {
                if (_ButtonOperateCommand == null)
                {
                    _ButtonOperateCommand = new CommandBase(
                        (obj) =>
                        {
                            if (SelectedLaserModel == null)
                            {
                                return;
                            }
                            if (obj.ToString().Equals("Init"))
                            {
                                SelectedLaserModel.Initial();
                            }
                            else if (obj.ToString().Equals("StartLaser"))
                            {
                                SelectedLaserModel.StartMarkExt();
                            }
                            else if (obj.ToString().Equals("EditImg")) { }
                        }
                    );
                }
                return _ButtonOperateCommand;
            }
        }

        #endregion

        [OnDeserialized()]
        internal void OnDeserializedMethod(StreamingContext context)
        {
            //LaserModels
            //    .ToList()
            //    .ForEach(model =>
            //    {
            //        var ret = model.Initial();
            //        if (!ret)
            //        {
            //            Logger.AddLog($"激光器:{model.LaserName}初始化失败", Common.Enums.eMsgType.Error);
            //        }
            //    });
        }
    }
}
