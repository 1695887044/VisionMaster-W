using EventMgrLib;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Threading;
using System.Windows;
using System;
using System.IO;
using AvalonDock.Layout.Serialization;
using VM.Start.Common.Enums;
using VM.Start.Common;
using VM.Start.Common.Helper;
using VM.Start.DataAccess;
using VM.Start.Dialogs.Views;
using VM.Start.Events;
using VM.Start.Localization;
using VM.Start.PersistentData;
using VM.Start.Views;
using VM.Start.Common.Provide;
using VM.Start.Views.Dock;
using System.Windows.Interop;
using VM.Start.Common.RightControl;
using Microsoft.Win32;
using System.Drawing.Imaging;
using VM.Start.Common.Const;
using VM.Start.Models;
using VM.Start.Services;
using HandyControl.Controls;
using System.Windows.Media;
using AvalonDock.Layout;
using System.Linq;
using System.Web.UI.WebControls;
using HalconDotNet;

namespace VM.Start.ViewModels
{
    public class MainViewModel : NotifyPropertyBase
    {
        #region Singleton

        private static readonly MainViewModel _instance = new MainViewModel();

        private MainViewModel()
        {
            EventMgr.Ins.GetEvent<CurrentUserChangedEvent>().Subscribe(user => OnUserChanged(user));
        }

        public static MainViewModel Ins
        {
            get { return _instance; }
        }

        #endregion
        #region Prop
        private bool _IsCheckedUIDesign = false;
        public bool IsCheckedUIDesign
        {
            get { return this._IsCheckedUIDesign; }
            set
            {
                this._IsCheckedUIDesign = value;
                base.RaisePropertyChanged();
                this.VisibleOrHideView(this._IsCheckedUIDesign, "UIDesign", "LayoutDocument");
            }
        }

        private string _CurrentTime;

        /// <summary>
        /// 当前时间
        /// </summary>
        public string CurrentTime
        {
            get { return _CurrentTime; }
            set { Set(ref _CurrentTime, value); }
        }
        private string _CurrentSolution;

        /// <summary>
        /// 当前解决方案
        /// </summary>
        public string CurrentSolution
        {
            get { return _CurrentSolution; }
            set { Set(ref _CurrentSolution, value); }
        }
        private string _CurrentUserName;

        /// <summary>
        /// 当前用户名
        /// </summary>
        public string CurrentUserName
        {
            get { return _CurrentUserName; }
            set
            {
                Set(
                    ref _CurrentUserName,
                    value,
                    new Action(() =>
                    {
                        IsCheckedUIDesign = Solution.Ins.IsUseUIDesign;
                        switch (_CurrentUserName)
                        {
                            case UserType.Developer_en:
                            case UserType.Developer: //
                                Logger.AddLog($"{Resource.DeveloperLoginSystem}");
                                RightControl.Ins.CameraSet = true;
                                RightControl.Ins.QuickMode = true;
                                RightControl.Ins.HardwareConfig = true;
                                RightControl.Ins.CommunicationSet = true;
                                RightControl.Ins.Camera = true;
                                RightControl.Ins.Temperature = true;
                                RightControl.Ins.Barcode = true;
                                RightControl.Ins.HeightSensor = true;
                                RightControl.Ins.PressureSensor = true;
                                RightControl.Ins.InputAndOutput = true;
                                RightControl.Ins.ServoDebug = true;
                                RightControl.Ins.IODebug = true;
                                RightControl.Ins.Home = true;
                                RightControl.Ins.Open = true;
                                RightControl.Ins.Edit = true;
                                RightControl.Ins.Save = true;
                                //RightControl.Ins.RunOnce = true;
                                //RightControl.Ins.RunCycle = true;
                                RightControl.Ins.Stop = true;
                                RightControl.Ins.OpenFile = true;
                                RightControl.Ins.SaveFile = true;
                                RightControl.Ins.SystemConfig = true;
                                RightControl.Ins.OpenOrCloseCamera = true;
                                RightControl.Ins.RedLight = true;
                                RightControl.Ins.DeviceParam = true;
                                RightControl.Ins.SystemParam = true;
                                RightControl.Ins.View = true;
                                RightControl.Ins.ManufactureParam = true;
                                RightControl.Ins.TemplateLayout = true;
                                RightControl.Ins.CameraSetting = true;
                                RightControl.Ins.LaserDebug = true;
                                RightControl.Ins.PowerDebug = true;
                                RightControl.Ins.NewSolution = true;
                                RightControl.Ins.SolutionList = true;
                                RightControl.Ins.GlobalVar = true;
                                RightControl.Ins.UIDesign = true;
                                IsCheckedTool = true;
                                IsCheckedProcess = true;
                                IsCheckedData = true;
                                IsCheckedModuleOut = true;
                                IsCheckedDeviceState = true;
                                LoadLayoutCommand.Execute(0);
                                MainView.Ins.toolBar.Visibility = Visibility.Visible;
                                break;
                            case UserType.Administrator_en:
                            case UserType.Administrator: //
                                Logger.AddLog($"{Resource.AdministratorLoginSystem}");
                                RightControl.Ins.CameraSet = false;
                                RightControl.Ins.QuickMode = true;
                                RightControl.Ins.HardwareConfig = false;
                                RightControl.Ins.CommunicationSet = false;
                                RightControl.Ins.Camera = false;
                                RightControl.Ins.Temperature = false;
                                RightControl.Ins.Barcode = false;
                                RightControl.Ins.HeightSensor = false;
                                RightControl.Ins.PressureSensor = false;
                                RightControl.Ins.InputAndOutput = false;
                                RightControl.Ins.ServoDebug = true;
                                RightControl.Ins.IODebug = true;
                                RightControl.Ins.Home = true;
                                RightControl.Ins.Open = true;
                                RightControl.Ins.Edit = true;
                                RightControl.Ins.Save = true;
                                //RightControl.Ins.RunOnce = true;
                                //RightControl.Ins.RunCycle = true;
                                RightControl.Ins.Stop = true;
                                RightControl.Ins.OpenFile = true;
                                RightControl.Ins.SaveFile = true;
                                RightControl.Ins.SystemConfig = true;
                                RightControl.Ins.OpenOrCloseCamera = true;
                                RightControl.Ins.RedLight = true;
                                RightControl.Ins.DeviceParam = true;
                                RightControl.Ins.SystemParam = true;
                                RightControl.Ins.View = true;
                                RightControl.Ins.ManufactureParam = true;
                                RightControl.Ins.TemplateLayout = true;
                                RightControl.Ins.CameraSetting = true;
                                RightControl.Ins.LaserDebug = false;
                                RightControl.Ins.PowerDebug = false;
                                RightControl.Ins.NewSolution = false;
                                RightControl.Ins.SolutionList = true;
                                RightControl.Ins.GlobalVar = false;
                                RightControl.Ins.UIDesign = true;
                                IsCheckedTool = true;
                                IsCheckedProcess = true;
                                IsCheckedData = true;
                                IsCheckedModuleOut = true;
                                IsCheckedDeviceState = true;
                                LoadLayoutCommand.Execute(0);
                                MainView.Ins.toolBar.Visibility = Visibility.Visible;
                                break;
                            case UserType.Operator_en:
                            case UserType.Operator: //
                                Logger.AddLog($"{Resource.OperatorLoginSystem}");
                                RightControl.Ins.CameraSet = false;
                                RightControl.Ins.QuickMode = false;
                                RightControl.Ins.HardwareConfig = false;
                                RightControl.Ins.CommunicationSet = false;
                                RightControl.Ins.Camera = false;
                                RightControl.Ins.Temperature = false;
                                RightControl.Ins.Barcode = false;
                                RightControl.Ins.HeightSensor = false;
                                RightControl.Ins.PressureSensor = false;
                                RightControl.Ins.InputAndOutput = false;
                                RightControl.Ins.ServoDebug = false;
                                RightControl.Ins.IODebug = true;
                                RightControl.Ins.Home = true;
                                RightControl.Ins.Open = true;
                                RightControl.Ins.Edit = false;
                                RightControl.Ins.Save = false;
                                //RightControl.Ins.RunOnce = true;
                                //RightControl.Ins.RunCycle = true;
                                RightControl.Ins.Stop = true;
                                RightControl.Ins.OpenFile = true;
                                RightControl.Ins.SaveFile = true;
                                RightControl.Ins.SystemConfig = false;
                                RightControl.Ins.OpenOrCloseCamera = false;
                                RightControl.Ins.RedLight = false;
                                RightControl.Ins.DeviceParam = false;
                                RightControl.Ins.SystemParam = false;
                                RightControl.Ins.View = false;
                                RightControl.Ins.ManufactureParam = false;
                                RightControl.Ins.TemplateLayout = false;
                                RightControl.Ins.CameraSetting = false;
                                RightControl.Ins.LaserDebug = false;
                                RightControl.Ins.PowerDebug = false;
                                RightControl.Ins.NewSolution = false;
                                RightControl.Ins.SolutionList = false;
                                RightControl.Ins.GlobalVar = false;
                                RightControl.Ins.UIDesign = false;
                                IsCheckedTool = false;
                                IsCheckedProcess = false;
                                IsCheckedData = false;
                                IsCheckedModuleOut = false;
                                IsCheckedDeviceState = false;
                                MainView.Ins.toolBar.Visibility = Visibility.Collapsed;

                                break;

                            default:
                                RightControl.Ins.Camera = false;
                                RightControl.Ins.HardwareConfig = false;
                                RightControl.Ins.CommunicationSet = false;
                                RightControl.Ins.Temperature = false;
                                RightControl.Ins.Barcode = false;
                                RightControl.Ins.HeightSensor = false;
                                RightControl.Ins.PressureSensor = false;
                                RightControl.Ins.InputAndOutput = false;
                                RightControl.Ins.ServoDebug = false;
                                RightControl.Ins.IODebug = false;
                                RightControl.Ins.Home = false;
                                RightControl.Ins.Open = false;
                                RightControl.Ins.Edit = false;
                                RightControl.Ins.Save = false;
                                RightControl.Ins.RunOnce = true;
                                RightControl.Ins.RunCycle = true;
                                RightControl.Ins.Stop = true;
                                RightControl.Ins.OpenFile = false;
                                RightControl.Ins.SaveFile = false;
                                RightControl.Ins.SystemConfig = false;
                                RightControl.Ins.OpenOrCloseCamera = false;
                                RightControl.Ins.RedLight = false;
                                RightControl.Ins.DeviceParam = false;
                                RightControl.Ins.SystemParam = false;
                                RightControl.Ins.View = false;
                                RightControl.Ins.ManufactureParam = false;
                                RightControl.Ins.TemplateLayout = false;
                                RightControl.Ins.CameraSetting = false;
                                RightControl.Ins.LaserDebug = false;
                                RightControl.Ins.PowerDebug = false;
                                RightControl.Ins.NewSolution = false;
                                RightControl.Ins.SolutionList = false;
                                RightControl.Ins.GlobalVar = false;
                                RightControl.Ins.UIDesign = false;
                                MainView.Ins.toolBar.Visibility = Visibility.Collapsed;
                                IsCheckedTool = false;
                                IsCheckedProcess = false;
                                IsCheckedData = false;
                                IsCheckedModuleOut = false;
                                IsCheckedDeviceState = false;
                                break;
                        }
                    })
                );
            }
        }

        private System.Windows.Media.Brush _CurrentUserNameForeground = System
            .Windows
            .Media
            .Brushes
            .White;

        /// <summary>
        /// 当前用户名的字体颜色
        /// </summary>
        public System.Windows.Media.Brush CurrentUserNameForeground
        {
            get { return _CurrentUserNameForeground; }
            set { Set(ref _CurrentUserNameForeground, value); }
        }
        private bool _IsCheckedVision = true;

        /// <summary>
        /// 视图是否选中
        /// </summary>
        public bool IsCheckedVision
        {
            get { return _IsCheckedVision; }
            set
            {
                _IsCheckedVision = value;
                this.RaisePropertyChanged();
                VisibleOrHideView(_IsCheckedVision, "Vision", "LayoutDocument");
            }
        }
        private bool _IsCheckedTool = true;

        /// <summary>
        /// 视图是否选中
        /// </summary>
        public bool IsCheckedTool
        {
            get { return _IsCheckedTool; }
            set
            {
                _IsCheckedTool = value;
                this.RaisePropertyChanged();
                VisibleOrHideView(_IsCheckedTool, "Tool");
            }
        }
        private bool _IsCheckedProcess = true;

        /// <summary>
        /// 视图是否选中
        /// </summary>
        public bool IsCheckedProcess
        {
            get { return _IsCheckedProcess; }
            set
            {
                _IsCheckedProcess = value;
                this.RaisePropertyChanged();
                VisibleOrHideView(_IsCheckedProcess, "Process");
            }
        }
        private bool _IsCheckedLog = true;

        /// <summary>
        /// 视图是否选中
        /// </summary>
        public bool IsCheckedLog
        {
            get { return _IsCheckedLog; }
            set
            {
                _IsCheckedLog = value;
                VisibleOrHideView(_IsCheckedLog, "Log");
                this.RaisePropertyChanged();
            }
        }
        private bool _IsCheckedData = true;

        /// <summary>
        /// 视图是否选中
        /// </summary>
        public bool IsCheckedData
        {
            get { return _IsCheckedData; }
            set
            {
                _IsCheckedData = value;
                this.RaisePropertyChanged();
                VisibleOrHideView(_IsCheckedData, "Data");
            }
        }
        private bool _IsCheckedModuleOut = true;

        /// <summary>
        /// 视图是否选中
        /// </summary>
        public bool IsCheckedModuleOut
        {
            get { return _IsCheckedModuleOut; }
            set
            {
                _IsCheckedModuleOut = value;
                this.RaisePropertyChanged();
                VisibleOrHideView(_IsCheckedModuleOut, "ModuleOut");
            }
        }
        private bool _IsCheckedDeviceState = true;

        /// <summary>
        /// 视图是否选中
        /// </summary>
        public bool IsCheckedDeviceState
        {
            get { return _IsCheckedDeviceState; }
            set
            {
                _IsCheckedDeviceState = value;
                this.RaisePropertyChanged();
                VisibleOrHideView(_IsCheckedDeviceState, "DeviceState");
            }
        }
        #endregion
        #region Command
        private CommandBase _LoadedCommand;
        public CommandBase LoadedCommand
        {
            get
            {
                if (_LoadedCommand == null)
                {
                    _LoadedCommand = new CommandBase(
                        (obj) =>
                        {
                            //【1】当前时间
                            DispatcherTimer timer = new DispatcherTimer();
                            timer.Interval = TimeSpan.FromMilliseconds(100);
                            timer.Tick += Timer100ms_Tick;
                            timer.Start();
                            //【3】注册快捷键
                            RegisterHotKey();
                            UnregisterHotKey();
                            //【4】初始化UI
                            InitUI();
                            //【6】数据库配置
                            SQLiteHelper.Ins.Init();
                            //【18】关闭splashScreen
                            App.splashScreen.Close(TimeSpan.FromMilliseconds(100));
                            //【21】软件启动成功
                            Logger.AddLog(Resource.SoftwareStartupSucceeded);
                            //【22】加载配方
                            if (SystemConfig.Ins.SolutionAutoLoad)
                            {
                                OpenSolution(SystemConfig.Ins.SolutionPathText);
                                if (SystemConfig.Ins.SolutionAutoRun)
                                {
                                    //Solution.Ins.QuickMode = true;
                                    //MainView.Ins.btnQuickMode.Foreground = Brushes.Lime;
                                    Solution.Ins.StartRun();
                                }
                            }
                        }
                    );
                }
                return _LoadedCommand;
            }
        }

        private void InitUI()
        {
            //【1】加载布局
            if (SystemConfig.Ins.AutoLoadLayout)
            {
                LoadLayoutCommand.Execute(1);
            }
            //【1】急速模式背景显示
            if (Solution.Ins.QuickMode)
            {
                MainView.Ins.btnQuickMode.Foreground = Brushes.Lime;
            }
            else
            {
                MainView.Ins.btnQuickMode.Foreground = Brushes.LightGray;
            }
            //【3】图像视图显示
            IsCheckedVision = true;
        }

        private CommandBase _ClosingCommand;
        public CommandBase ClosingCommand
        {
            get
            {
                if (_ClosingCommand == null)
                {
                    _ClosingCommand = new CommandBase(
                        (obj) =>
                        {
                            foreach (var item in Solution.Ins.ProjectList)
                            {
                                if (item.GetThreadStatus())
                                {
                                    MessageView.Ins.MessageBoxShow("程序运行状态禁止关闭软件！", eMsgType.Warn);
                                    (obj as System.ComponentModel.CancelEventArgs).Cancel = true;
                                    return;
                                }
                            }
                            var messageView = MessageView.Ins;
                            messageView.MessageBoxShow(
                                Resource.QuitSystem,
                                eMsgType.Warn,
                                MessageBoxButton.OKCancel
                            );
                            if (messageView.DialogResult == false)
                            {
                                (obj as System.ComponentModel.CancelEventArgs).Cancel = true;
                            }
                            else
                            {
                                EventMgr.Ins.GetEvent<SoftwareExitEvent>().Publish();
                                CommonMethods.Exit();
                            }
                        }
                    );
                }
                return _ClosingCommand;
            }
        }
        private CommandBase _NavOperateCommand;
        public CommandBase NavOperateCommand
        {
            get
            {
                if (_NavOperateCommand == null)
                {
                    _NavOperateCommand = new CommandBase(
                        (obj) =>
                        {
                            switch (obj)
                            {
                                case "NewSolution": //新建解决方案
                                    Solution.Ins.CreateSolution();
                                    break;
                                case "SolutionList": //解决方案列表
                                    SolutionListView.Ins.ShowDialog();
                                    break;
                                case "Open": //打开
                                    OpenFileDialog openFileDialog = new OpenFileDialog();
                                    openFileDialog.Filter = "解决方案 (*.vm)|*.vm";
                                    openFileDialog.DefaultExt = "vm";
                                    if (openFileDialog.ShowDialog() == true)
                                    {
                                        var view = LoadingView.Ins;
                                        view.LoadingShow("加载项目中，请稍等...");
                                        OpenSolution(openFileDialog.FileName);
                                        view.Close();
                                    }
                                    break;
                                case "Save": //保存
                                    foreach (var item in Solution.Ins.ProjectList)
                                    {
                                        if (item.GetThreadStatus())
                                        {
                                            MessageView.Ins.MessageBoxShow(
                                                "程序运行状态禁止保存工程！",
                                                eMsgType.Warn
                                            );
                                            return;
                                        }
                                    }
                                    if (CurrentSolution != null && File.Exists(CurrentSolution))
                                    {
                                        SerializeHelp.BinSerializeAndSaveFile(
                                            Solution.Ins,
                                            CurrentSolution
                                        );
                                    }
                                    else
                                    {
                                        SaveFileDialog saveFileDialog = new SaveFileDialog();
                                        saveFileDialog.Title = "另存为";
                                        saveFileDialog.FileName = "Test.vm"; // Default file name
                                        saveFileDialog.DefaultExt = ".vm"; // Default file extension
                                        saveFileDialog.Filter = "解决方案|*.vm"; // Filter files by extension
                                        if (saveFileDialog.ShowDialog() == true)
                                        {
                                            CurrentSolution = saveFileDialog.FileName;
                                            SerializeHelp.BinSerializeAndSaveFile(
                                                Solution.Ins,
                                                CurrentSolution
                                            );
                                        }
                                        else
                                        {
                                            return;
                                        }
                                    }
                                    Logger.AddLog("解决方案保存成功！", eMsgType.Success, isDispGrowl: true);
                                    break;
                                case "GlobalVar": //全局变量
                                    GlobalVarView.Ins.ShowDialog();
                                    break;
                                case "UserLogin": //用户登陆
                                    CommonMethods.UserLogin();
                                    break;
                                case "ScreenCapture": //截屏
                                    System.Drawing.Image image = CommonMethods.ScreenCapture();
                                    SaveFileDialog sfd = new SaveFileDialog();
                                    sfd.Filter = "PNG图像|*.png|所有文件|*.*";
                                    if (sfd.ShowDialog() == true)
                                    {
                                        if (String.IsNullOrEmpty(sfd.FileName))
                                            return;
                                        image.Save(sfd.FileName, ImageFormat.Png);
                                    }
                                    break;
                                case "ReportQuery": //报表查询
                                    break;
                                case "SystemParam": //系统参数
                                    SystemParamView.Ins.ShowDialog();
                                    break;
                                case "CanvasSet": //画布设置
                                    CanvasSetView.Ins.ShowDialog();
                                    break;
                                case "RunOnce": //运行一次
                                    Solution.Ins.ExecuteOnce();
                                    break;
                                case "RunCycle": //循环运行
                                    //Solution.Ins.QuickMode = true;
                                    //MainView.Ins.btnQuickMode.Foreground = Brushes.Lime;
                                    Solution.Ins.StartRun();
                                    break;
                                case "Stop": //停止
                                    Solution.Ins.StopRun();
                                    break;
                                case "QuickMode": //急速模式
                                    Solution.Ins.QuickMode = !Solution.Ins.QuickMode;
                                    if (Solution.Ins.QuickMode)
                                    {
                                        MainView.Ins.btnQuickMode.Foreground = Brushes.Lime;
                                    }
                                    else
                                    {
                                        MainView.Ins.btnQuickMode.Foreground = Brushes.LightGray;
                                    }
                                    break;
                                case "CameraSet": //相机设置
                                    CameraSetView.Ins.ShowDialog();
                                    break;
                                case "CommunicationSet": //通讯设置
                                    CommunicationSetView.Ins.ShowDialog();
                                    break;
                                case "HardwareConfig": //硬件配置
                                    //HardwareConfigView.Ins = new HardwareConfigView();
                                    if (HardwareConfigView.Ins.IsClosed)
                                    {
                                        HardwareConfigView.Ins.Show();
                                    }
                                    else
                                    {
                                        HardwareConfigView.Ins.Activate();
                                    }
                                    break;
                                case "UIDesign": //UI 设计器

                                    new UIDesignView().Show();
                                    break;
                                case "LaserSet": //激光设置
                                    if (LaserSetView.Ins.IsClosed)
                                    {
                                        LaserSetView.Ins.Show();
                                    }
                                    else
                                    {
                                        LaserSetView.Ins.Activate();
                                    }
                                    break;

                                default:
                                    break;
                            }
                        }
                    );
                }
                return _NavOperateCommand;
            }
        }
        private CommandBase _licenseCommand;

        public CommandBase LicenseCommand
        {
            get
            {
                if (_licenseCommand == null)
                {
                    _licenseCommand = new CommandBase(
                        (obj) =>
                        {
                            if (EncryptionView.License == SystemConfig.Ins.License)
                            {
                                MessageView.Ins.MessageBoxShow($"软件已激活!");
                            }
                            else
                            {
                                EncryptionView.Ins.ShowDialog();
                            }
                        }
                    );
                }
                return _licenseCommand;
            }
        }
        private CommandBase _SaveLayoutCommand;

        public CommandBase SaveLayoutCommand
        {
            get
            {
                if (_SaveLayoutCommand == null)
                {
                    _SaveLayoutCommand = new CommandBase(
                        (obj) =>
                        {
                            var layoutSerializer = new XmlLayoutSerializer(
                                DockView.Ins.dockManager
                            );
                            layoutSerializer.Serialize(FilePaths.DockLayout);
                        }
                    );
                }
                return _SaveLayoutCommand;
            }
        }
        private CommandBase _LoadLayoutCommand;

        public CommandBase LoadLayoutCommand
        {
            get
            {
                if (_LoadLayoutCommand == null)
                {
                    _LoadLayoutCommand = new CommandBase(
                        (obj) =>
                        {
                            var layoutSerializer = new XmlLayoutSerializer(
                                DockView.Ins.dockManager
                            );
                            layoutSerializer.Deserialize(FilePaths.DockLayout);
                        },
                        (O) => File.Exists(FilePaths.DockLayout)
                    );
                }
                return _LoadLayoutCommand;
            }
        }
        private CommandBase _LoadDefaultLayoutCommand;

        public CommandBase LoadDefaultLayoutCommand
        {
            get
            {
                if (_LoadDefaultLayoutCommand == null)
                {
                    _LoadDefaultLayoutCommand = new CommandBase(
                        (obj) =>
                        {
                            var layoutSerializer = new XmlLayoutSerializer(
                                DockView.Ins.dockManager
                            );
                            layoutSerializer.Deserialize(FilePaths.DefaultDockLayout);
                        },
                        (O) => File.Exists(FilePaths.DefaultDockLayout)
                    );
                }
                return _LoadDefaultLayoutCommand;
            }
        }

        private CommandBase _HelpDocumentCommand;

        public CommandBase HelpDocumentCommand
        {
            get
            {
                if (_HelpDocumentCommand == null)
                {
                    _HelpDocumentCommand = new CommandBase(
                        (obj) =>
                        {
                            string HelpDecomentFullName =
                                $"{FilePaths.HelpDecomentPath}{SystemConfig.Ins.ProjectName}Help_{SystemConfig.Ins.CurrentCultureName}.chm";
                            if (!File.Exists(HelpDecomentFullName))
                            {
                                MessageView.Ins.MessageBoxShow(
                                    Resource.HelpDecomentNotFind,
                                    eMsgType.Warn
                                );
                                return;
                            }
                            System.Diagnostics.Process process = new System.Diagnostics.Process();
                            process.StartInfo.FileName = HelpDecomentFullName;
                            process.StartInfo.Verb = "Open";
                            process.StartInfo.CreateNoWindow = true;
                            process.StartInfo.WindowStyle = System
                                .Diagnostics
                                .ProcessWindowStyle
                                .Normal;
                            process.Start();
                        }
                    );
                }
                return _HelpDocumentCommand;
            }
        }
        private CommandBase _AboutCommand;

        public CommandBase AboutCommand
        {
            get
            {
                if (_AboutCommand == null)
                {
                    _AboutCommand = new CommandBase(
                        (obj) =>
                        {
                            AboutView.Ins.ShowDialog();
                        }
                    );
                }
                return _AboutCommand;
            }
        }

        #endregion
        #region Method
        private void OpenSolution(string fileName)
        {
            CurrentSolution = fileName;
            Solution.Ins = SerializeHelp.BinDeserialize<Solution>(fileName);
            Solution.Ins.LoadCommunacation();
            if (Solution.Ins.QuickMode)
            {
                MainView.Ins.btnQuickMode.Foreground = Brushes.Lime;
            }
            else
            {
                MainView.Ins.btnQuickMode.Foreground = Brushes.LightGray;
            }
            VisionView.Ins.ViewMode = Solution.Ins.ViewMode;
            ToolView.Ins.UpdateTree();
            Solution.Ins.CurrentProject = Solution.Ins.GetProjectById(
                Solution.Ins.CurrentProjectID
            );
            ProcessView.Ins.UpdateTree();
            UIDesignView.UpdateUIDesign(true);
        }

        private void Timer100ms_Tick(object sender, EventArgs e)
        {
            CurrentTime = DateTime.Now.ToString();
        }

        private void OnUserChanged(UserModel user)
        {
            if (user == null)
            {
                CurrentUserName = Resource.PleaseLoginFirst;
                CurrentUserNameForeground = System.Windows.Media.Brushes.Red;
            }
            else
            {
                CurrentUserName = user.UserName;
                switch (user.UserName)
                {
                    case UserType.Developer_en:
                    case UserType.Developer: //
                        CurrentUserName = Resource.Developer;
                        break;
                    case UserType.Administrator_en:
                    case UserType.Administrator: //
                        CurrentUserName = Resource.Administrator;
                        break;
                    case UserType.Operator_en:
                    case UserType.Operator: //
                        CurrentUserName = Resource.Operator;
                        break;
                    default:
                        CurrentUserName = Resource.PleaseLoginFirst;
                        break;
                }
                CurrentUserNameForeground = System.Windows.Media.Brushes.White;
            }
        }

        #region HotKey
        public void RegisterHotKey()
        {
            IntPtr m_Hwnd = ((HwndSource)PresentationSource.FromVisual(MainView.Ins)).Handle;
            HwndSource hWndSource = HwndSource.FromHwnd(m_Hwnd);
            if (hWndSource != null)
            {
                hWndSource.AddHook(WndProc);
            }
            HotKeyHelper.RegisterHotKey(
                new HotKeyModel() { SelectType = EType.Ctrl, SelectKey = EKey.S },
                m_Hwnd,
                HotKeyManager.SaveTemplate_ID
            );
            HotKeyHelper.RegisterHotKey(
                new HotKeyModel() { SelectType = EType.Ctrl, SelectKey = EKey.O },
                m_Hwnd,
                HotKeyManager.OpenTemplate_ID
            );
            HotKeyHelper.RegisterHotKey(
                new HotKeyModel() { SelectType = EType.Ctrl, SelectKey = EKey.P },
                m_Hwnd,
                HotKeyManager.OpenDeviceParam_ID
            );
            HotKeyHelper.RegisterHotKey(
                new HotKeyModel() { SelectType = EType.None, SelectKey = EKey.F5 },
                m_Hwnd,
                HotKeyManager.ContinueRun_ID
            );
            HotKeyHelper.RegisterHotKey(
                new HotKeyModel() { SelectType = EType.None, SelectKey = EKey.F6 },
                m_Hwnd,
                HotKeyManager.StepRun_ID
            );
            HotKeyHelper.RegisterHotKey(
                new HotKeyModel() { SelectType = EType.None, SelectKey = EKey.F8 },
                m_Hwnd,
                HotKeyManager.ScreenCapture_ID
            );
            HotKeyHelper.RegisterHotKey(
                new HotKeyModel() { SelectType = EType.Ctrl, SelectKey = EKey.E },
                m_Hwnd,
                HotKeyManager.EditTemplate_ID
            );
            HotKeyHelper.RegisterHotKey(
                new HotKeyModel() { SelectType = EType.None, SelectKey = EKey.F9 },
                m_Hwnd,
                HotKeyManager.Help_ID
            );
            HotKeyHelper.RegisterHotKey(
                new HotKeyModel() { SelectType = EType.None, SelectKey = EKey.Escape },
                m_Hwnd,
                HotKeyManager.StopProcess_ID
            );
        }

        public void UnregisterHotKey()
        {
            IntPtr m_Hwnd = ((HwndSource)PresentationSource.FromVisual(MainView.Ins)).Handle;
            HotKeyHelper.UnregisterHotKey(m_Hwnd, HotKeyManager.SaveTemplate_ID);
            HotKeyHelper.UnregisterHotKey(m_Hwnd, HotKeyManager.OpenTemplate_ID);
            HotKeyHelper.UnregisterHotKey(m_Hwnd, HotKeyManager.OpenDeviceParam_ID);
            HotKeyHelper.UnregisterHotKey(m_Hwnd, HotKeyManager.ContinueRun_ID);
            HotKeyHelper.UnregisterHotKey(m_Hwnd, HotKeyManager.StepRun_ID);
            HotKeyHelper.UnregisterHotKey(m_Hwnd, HotKeyManager.ScreenCapture_ID);
            HotKeyHelper.UnregisterHotKey(m_Hwnd, HotKeyManager.EditTemplate_ID);
            HotKeyHelper.UnregisterHotKey(m_Hwnd, HotKeyManager.Help_ID);
            HotKeyHelper.UnregisterHotKey(m_Hwnd, HotKeyManager.StopProcess_ID);
        }

        private IntPtr WndProc(
            IntPtr hWnd,
            int msg,
            IntPtr wideParam,
            IntPtr longParam,
            ref bool handled
        )
        {
            switch (msg)
            {
                case HotKeyManager.WM_HOTKEY:
                    switch (wideParam.ToInt32())
                    {
                        case HotKeyManager.SaveTemplate_ID:
                            if (RightControl.Ins.Save)
                            {
                                NavOperateCommand.Execute("Save");
                            }
                            break;
                        case HotKeyManager.OpenTemplate_ID:
                            if (RightControl.Ins.Open)
                            {
                                NavOperateCommand.Execute("Open");
                            }
                            break;
                        case HotKeyManager.OpenDeviceParam_ID:
                            break;
                        case HotKeyManager.ContinueRun_ID:
                            Solution.Ins.CurrentProject.ContinueRunFlag = true;
                            Solution.Ins.CurrentProject.Breakpoint.Set();
                            break;
                        case HotKeyManager.StepRun_ID:
                            Solution.Ins.CurrentProject.Breakpoint.Set();
                            break;
                        case HotKeyManager.ScreenCapture_ID:
                            break;
                        case HotKeyManager.EditTemplate_ID:
                            break;
                        case HotKeyManager.Help_ID:
                            HelpDocumentCommand.Execute(1);
                            break;
                        case HotKeyManager.StopProcess_ID:
                            if (ProcessView.Ins.moduleTree.SelectedItem == null)
                                return IntPtr.Zero;
                            ModuleNode node = (
                                ProcessView.Ins.moduleTree.SelectedItem as ModuleNode
                            );
                            if (node == null)
                                return IntPtr.Zero;
                            Solution.Ins.CurrentProject.CloseModuleByName(node.DisplayName);
                            break;
                        default:
                            break;
                    }
                    handled = true;
                    break;
            }
            return IntPtr.Zero;
        }

        #endregion

        #region VisibleOrHideView
        public static LayoutDocument LayoutDocument_Vision = new LayoutDocument
        {
            Title = Resource.Dock_Vision,
            CanClose = false,
            ContentId = "Vision",
            Content = VisionView.Ins
        };

        public static LayoutDocument LayoutDocument_UIDesign = new LayoutDocument
        {
            Title = "主界面",
            CanClose = false,
            ContentId = "UIDesign",
            Content = UIDisplayView.Ins
        };
        private bool LayoutActive = true;

        public void LayoutStatusChanged()
        {
            LayoutActive = false;
            IsCheckedTool = GetLayoutStatus("Tool");
            IsCheckedProcess = GetLayoutStatus("Process");
            IsCheckedLog = GetLayoutStatus("Log");
            IsCheckedData = GetLayoutStatus("Data");
            IsCheckedModuleOut = GetLayoutStatus("ModuleOut");
            IsCheckedDeviceState = GetLayoutStatus("DeviceState");
            LayoutActive = true;
        }

        public bool GetLayoutStatus(string contentId)
        {
            if (DockView.Ins == null)
                return false;
            var toolWindow1 = DockView.Ins.dockManager.Layout
                .Descendents()
                .OfType<LayoutAnchorable>()
                .Single(a => a.ContentId == contentId);
            if (toolWindow1.IsHidden)
            {
                return false;
            }
            else
            {
                return true;
            }
        }

        public void VisibleOrHideView(
            bool isChecked,
            string contentId,
            string typeOfLayout = "LayoutAnchorable"
        )
        {
            if (DockView.Ins == null)
                return;
            if (typeOfLayout == "LayoutAnchorable")
            {
                var toolWindow1 = DockView.Ins.dockManager.Layout
                    .Descendents()
                    .OfType<LayoutAnchorable>()
                    .Single(a => a.ContentId == contentId);
                if (isChecked == true)
                {
                    if (toolWindow1.IsHidden)
                    {
                        toolWindow1.Show();
                    }
                    if (LayoutActive)
                    {
                        toolWindow1.IsActive = true;
                    }
                }
                else
                {
                    if (toolWindow1.IsVisible)
                    {
                        toolWindow1.Hide();
                    }
                    else if (toolWindow1.IsHidden) { }
                    else
                    {
                        toolWindow1.AddToLayout(
                            DockView.Ins.dockManager,
                            AnchorableShowStrategy.Bottom | AnchorableShowStrategy.Most
                        );
                    }
                }
            }
            else if (typeOfLayout == "LayoutDocument")
            {
                try
                {
                    var layoutDocumentPaneGroup = DockView.Ins.dockManager.Layout
                        .Descendents()
                        .OfType<LayoutDocumentPaneGroup>()
                        .FirstOrDefault();
                    if (layoutDocumentPaneGroup != null)
                    {
                        LayoutDocumentPane layoutDocumentPane = new LayoutDocumentPane();
                        LayoutDocument item = new LayoutDocument();

                        if (contentId == "UIDesign")
                        {
                            item = MainViewModel.LayoutDocument_UIDesign;
                            if (layoutDocumentPaneGroup.Children.Count < 2)
                            {
                                layoutDocumentPaneGroup.Children.Add(new LayoutDocumentPane());
                            }
                            layoutDocumentPane = (LayoutDocumentPane)
                                layoutDocumentPaneGroup.Children[1];
                        }
                        else if (contentId == "Vision")
                        {
                            item = MainViewModel.LayoutDocument_Vision;
                            if (layoutDocumentPaneGroup.Children.Count < 1)
                            {
                                layoutDocumentPaneGroup.Children.Add(new LayoutDocumentPane());
                            }
                            layoutDocumentPane = (LayoutDocumentPane)
                                layoutDocumentPaneGroup.Children[0];
                        }
                        if (isChecked)
                        {
                            if (!layoutDocumentPane.Children.Contains(item))
                            {
                                layoutDocumentPane.Children.Add(item);
                            }
                        }
                        else if (layoutDocumentPane.Children.Contains(item))
                        {
                            layoutDocumentPane.Children.Clear();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.GetExceptionMsg(ex, "", true);
                }
            }
        }
        #endregion

        #endregion
    }
}
