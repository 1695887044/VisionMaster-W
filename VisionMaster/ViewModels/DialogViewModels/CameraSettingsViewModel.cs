using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Core.Interfaces;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using UI.CustomControl;
using VisionMaster.Helpers;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 触发模式下拉项：把枚举包成"中文名 + 值"。
    /// 直接把 <see cref="CameraTriggerMode"/> 绑进下拉，选中框里会显示 Software / RisingEdge
    /// 这类英文键；现场看着像"设了但不知道设成了什么"，所以给一层中文壳。
    /// </summary>
    public sealed class CameraTriggerModeOption
    {
        public CameraTriggerMode Value { get; init; }

        public string Display { get; init; } = string.Empty;
    }

    /// <summary>
    /// 相机列表的一行：<see cref="CameraDescriptor"/>（配置） + <see cref="ICameraDevice"/>（运行态）。
    ///
    /// 为什么单独一个类而不是直接绑 <see cref="CameraDescriptor"/>：
    /// 列表要显示的"状态 / 状态说明 / 溢出数 / 已收帧 / 待消费"全在**运行态设备**上，
    /// 而设备在非 UI 线程变化（收图回调每帧都改"采流中（已收 N 帧）"、看门狗超时改状态）。
    /// 把"订阅 + 封送回 UI 线程 + 转成可绑定字段"收在这一行里，
    /// 视图模型与 XAML 就都不必再关心线程问题。
    /// </summary>
    public sealed class CameraRowViewModel : BindableBase, IDisposable
    {
        private readonly ICameraDevice? _device;
        private bool _disposed;

        public CameraRowViewModel(CameraDescriptor descriptor, ICameraDevice device, string driverDisplayName)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            _device = device;
            DriverDisplayName = driverDisplayName ?? string.Empty;

            if (_device != null)
            {
                _device.StateChanged += OnDeviceStateChanged;
                RefreshFromDevice();
            }
            else
            {
                // 驱动插件没加载 / 类型解析不到时，provider 不会创建设备。
                // 这里必须给出可见文案，否则用户会以为"相机在，只是连不上"，去查错方向。
                StateText = "未创建";
                StateDetail = "运行态设备尚未创建（驱动插件可能未加载或已被删除）";
            }
        }

        public CameraDescriptor Descriptor { get; }

        public Guid Id => Descriptor.Id;

        public string DisplayName => Descriptor.Caption;

        public string SerialNo => Descriptor.SerialNo;

        public string DriverDisplayName { get; }

        /// <summary>运行态设备；驱动不可用时为 null（界面据此禁用操作按钮并说明原因）</summary>
        public ICameraDevice? Device => _device;

        /// <summary>
        /// 是否网络相机（虚拟）驱动。
        /// 判 DriverTypeKey 而不是判 DisplayName：显示名是可翻译、可改的，类型键有编译期约束。
        /// </summary>
        public bool IsNetworkCamera =>
            Descriptor.DriverTypeKey?.Contains("Plugin.Camera.Network", StringComparison.OrdinalIgnoreCase) == true;

        private CameraConnectionState _state = CameraConnectionState.Closed;

        public CameraConnectionState State
        {
            get => _state;
            private set
            {
                if (!SetProperty(ref _state, value)) return;
                RaisePropertyChanged(nameof(StateText));
                RaisePropertyChanged(nameof(IsConnected));
                RaisePropertyChanged(nameof(IsStreaming));
            }
        }

        private string _stateText = "未连接";

        /// <summary>中文状态文字（列表胶囊 / 状态区都用它，XAML 里不再写枚举值判断）</summary>
        public string StateText
        {
            get => _stateText;
            private set => SetProperty(ref _stateText, value);
        }

        private string _stateDetail = string.Empty;

        public string StateDetail
        {
            get => _stateDetail;
            private set => SetProperty(ref _stateDetail, value);
        }

        private long _receivedFrameCount;

        public long ReceivedFrameCount
        {
            get => _receivedFrameCount;
            private set => SetProperty(ref _receivedFrameCount, value);
        }

        private int _pendingFrameCount;

        public int PendingFrameCount
        {
            get => _pendingFrameCount;
            private set => SetProperty(ref _pendingFrameCount, value);
        }

        private long _overflowCount;

        /// <summary>累计溢出丢帧数（队列满 → 丢最旧）。界面必须能看见它</summary>
        public long OverflowCount
        {
            get => _overflowCount;
            private set
            {
                if (!SetProperty(ref _overflowCount, value)) return;
                RaisePropertyChanged(nameof(HasOverflow));
                RaisePropertyChanged(nameof(OverflowWarning));
            }
        }

        private long _droppedFrameCount;

        /// <summary>累计"非溢出"丢弃数（掉线清空队列等）。与溢出分开显示，原因与处置完全不同</summary>
        public long DroppedFrameCount
        {
            get => _droppedFrameCount;
            private set => SetProperty(ref _droppedFrameCount, value);
        }

        public bool HasOverflow => OverflowCount > 0;

        public bool IsConnected => State != CameraConnectionState.Closed;

        public bool IsStreaming => State == CameraConnectionState.Streaming;

        /// <summary>
        /// 溢出告警文案。用词刻意写重：队列满时被丢掉的是**最旧的图像**，
        /// 而每一帧对应一个工件——丢帧就是漏检，绝不能让用户把它当成"性能指标"。
        /// </summary>
        public string OverflowWarning =>
            $"已丢弃 {OverflowCount} 帧最旧图像（处理速度跟不上相机出图速度）。每一帧都意味着一个未被检测的工件。";

        /// <summary>
        /// 从设备把"状态 + 计数器"重新读一遍。本方法只读设备属性、只写自身绑定字段；
        /// 由调用方保证在 UI 线程上执行（StateChanged 回调 / 预览定时器 / 界面按钮都各自封送）。
        /// </summary>
        public void RefreshFromDevice()
        {
            if (_disposed) return;

            if (_device == null)
            {
                StateText = "未创建";
                return;
            }

            State = _device.State;
            StateDetail = _device.StateDetail;
            StateText = DescribeState(_device.State);
            ReceivedFrameCount = _device.ReceivedFrameCount;
            PendingFrameCount = _device.PendingFrameCount;
            OverflowCount = _device.OverflowCount;
            DroppedFrameCount = _device.DroppedFrameCount;
        }

        private static string DescribeState(CameraConnectionState state) => state switch
        {
            CameraConnectionState.Closed => "未连接",
            CameraConnectionState.Connecting => "连接中/等待接入",
            CameraConnectionState.Online => "在线（未采流）",
            CameraConnectionState.Streaming => "采流中",
            _ => state.ToString()
        };

        private void OnDeviceStateChanged(object? sender, EventArgs e)
        {
            // 事件在设备线程（收图回调 / 看门狗）上触发，而绑定属性只能在 UI 线程改，必须先封送。
            // 走 SafeDispatch：关窗阶段的派发会被静默丢弃，避免"窗口已关设备还在刷界面"崩溃。
            SafeDispatch.BeginInvoke(() =>
            {
                if (!_disposed) RefreshFromDevice();
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // 退订必须成对：设备是长生命周期对象（可能比本窗口活得久），
            // 不退订会让每次打开"相机设置"都在设备上多挂一份回调，窗口越开越多、界面越刷越慢。
            if (_device != null) _device.StateChanged -= OnDeviceStateChanged;
        }
    }

    /// <summary>
    /// 「系统 → 相机设置」对话框：方案级相机资源的管理界面。
    ///
    /// 与「通讯设置」的分工
    /// ---------
    /// 通讯设置管的是"这台机器怎么和外部设备说话"（连接 / 协议 / 变量），
    /// 本界面管的是"这条方案用哪几台相机、什么驱动、什么序列号、当前连没连上、丢没丢帧"。
    /// 两者都是**方案级硬件资源**，但相机的配置对象直接存在
    /// <see cref="SolutionModel.CameraConfigs"/> 里（不走任何 Manager），所以本界面直接改方案。
    ///
    /// 界面为什么必须能看到"溢出"
    /// ---------
    /// 相机侧的核心承诺是"丢帧 = 漏检工件，绝不静默"。落在界面上就是两条：
    ///   ① 溢出数与其它丢弃数**分开**显示（一个查产能、一个查掉线，处置完全不同）；
    ///   ② 溢出 > 0 时整块红色告警，把"每一帧都是一个没被检测的工件"这句话说出来。
    /// 把它们合并成一个"丢弃数"，现场就会把它当性能指标而不是质量事故。
    /// </summary>
    public class CameraSettingsViewModel : BindableBase, IDialogAware
    {
        private readonly CameraProvider _provider;
        private readonly NetworkCameraServer _networkServer;
        private readonly IWorkspaceManager _workspace;

        /// <summary>预览刷新定时器：300ms 足够"看着像实时"，又不会把 UI 线程占满</summary>
        private readonly DispatcherTimer _previewTimer;

        /// <summary>防重入：ReloadRows 内部要读设备，期间不该再被 DevicesChanged 唤起一轮重建</summary>
        private bool _reloading;

        /// <summary>窗口已关闭：迟到的回调不再碰界面</summary>
        private bool _closed;

        /// <summary>装载参数期间为 true：避免"选中相机 → 灌参数"这一步把值又当成用户输入写回配置</summary>
        private bool _loadingParams;

        public CameraSettingsViewModel(CameraProvider provider, NetworkCameraServer networkServer, IWorkspaceManager workspace)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _networkServer = networkServer ?? throw new ArgumentNullException(nameof(networkServer));
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));

            AddCommand = new DelegateCommand(ExecuteBeginAdd);
            ConfirmAddCommand = new DelegateCommand(ExecuteConfirmAdd);
            CancelAddCommand = new DelegateCommand(() => IsAdding = false);
            DeleteCommand = new DelegateCommand<CameraRowViewModel>(ExecuteDelete);
            CloseCommand = new DelegateCommand(() => RequestClose.Invoke(new DialogParameters(), ButtonResult.OK));

            // 作用于"当前选中相机"的命令：canExecute 直接绑设备是否存在，
            // 驱动不可用（设备为空）时按钮自动置灰，用户一眼看出"这台现在动不了"
            ToggleConnectCommand = new DelegateCommand(ExecuteToggleConnect, () => SelectedCamera?.Device != null);
            ToggleStreamCommand = new DelegateCommand(ExecuteToggleStream, () => SelectedCamera?.Device != null);
            ResetCountersCommand = new DelegateCommand(ExecuteResetCounters, () => SelectedCamera?.Device != null);
            ApplySettingsCommand = new DelegateCommand(ExecuteApplySettings, () => SelectedCamera?.Device != null);

            // 空状态是"集合内容"的派生属性，靠集合变更事件统一重算——
            // 手工在增删点补 RaisePropertyChanged 迟早会漏一处
            Cameras.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(IsEmpty));

            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _previewTimer.Tick += OnPreviewTick;
        }

        #region IDialogAware

        public string Title => "相机设置";

        public DialogCloseListener RequestClose { get; set; }

        public bool CanCloseDialog() => true;

        public void OnDialogOpened(IDialogParameters parameters)
        {
            // 每次打开都重读驱动：插件是启动期扫描的，但"类型能否被解析出来"受程序集加载时机影响，
            // 第一次打开时解析不到、第二次就好了——缓存一个空结论会让界面永远显示"没有可用驱动"
            AvailableDrivers = _provider.AvailableDrivers;

            // 打开即对齐一次：方案里可能刚加过相机，运行态设备表还没跟上
            try
            {
                var errors = _provider.SyncFromSolution();
                if (errors.Count > 0) Notifier.ShowError("部分相机未能创建：\n" + string.Join("\n", errors));
            }
            catch (Exception ex)
            {
                Notifier.ShowError($"同步相机运行态失败：{ex.Message}");
            }

            _provider.DevicesChanged += OnDevicesChanged;
            ReloadRows();
            RefreshNetworkInfo();
            _previewTimer.Start();
        }

        public void OnDialogClosed()
        {
            _closed = true;

            // 定时器与事件订阅必须成对清理：漏了这一步，对话框关了还在刷界面 → 崩溃或内存泄漏
            _previewTimer.Stop();
            _previewTimer.Tick -= OnPreviewTick;
            _provider.DevicesChanged -= OnDevicesChanged;

            // 每一行都要退订设备的 StateChanged（见 CameraRowViewModel.Dispose）
            foreach (var row in Cameras) row.Dispose();
            Cameras.Clear();
        }

        #endregion

        #region 列表与选中

        public ObservableCollection<CameraRowViewModel> Cameras { get; } = new();

        private CameraRowViewModel? _selectedCamera;

        public CameraRowViewModel? SelectedCamera
        {
            get => _selectedCamera;
            set
            {
                if (!SetProperty(ref _selectedCamera, value)) return;

                LoadParamsFromSelection();
                RefreshNetworkInfo();
                RaisePropertyChanged(nameof(HasSelection));

                // 换相机先清掉上一台的画面：否则会"看着像新相机已经出图了"
                PreviewImage = null;

                ToggleConnectCommand.RaiseCanExecuteChanged();
                ToggleStreamCommand.RaiseCanExecuteChanged();
                ResetCountersCommand.RaiseCanExecuteChanged();
                ApplySettingsCommand.RaiseCanExecuteChanged();
            }
        }

        public bool HasSelection => _selectedCamera != null;

        /// <summary>列表里一台相机都没有。空状态要引导"添加相机"</summary>
        public bool IsEmpty => Cameras.Count == 0;

        /// <summary>与列表匹配的驱动名缓存（TypeKey → 显示名），仅用于列表展示</summary>
        private IReadOnlyList<CameraDriverInfo> _availableDrivers = Array.Empty<CameraDriverInfo>();

        public IReadOnlyList<CameraDriverInfo> AvailableDrivers
        {
            get => _availableDrivers;
            private set => SetProperty(ref _availableDrivers, value);
        }

        #endregion

        #region 新增相机（内联表单）

        private bool _isAdding;

        public bool IsAdding
        {
            get => _isAdding;
            set
            {
                if (!SetProperty(ref _isAdding, value)) return;
                if (value) ResetAddForm();
            }
        }

        private CameraDriverInfo? _newDriver;

        public CameraDriverInfo? NewDriver
        {
            get => _newDriver;
            set => SetProperty(ref _newDriver, value);
        }

        private string _newSerial = string.Empty;

        public string NewSerial
        {
            get => _newSerial;
            set => SetProperty(ref _newSerial, value);
        }

        private string _newDisplayName = string.Empty;

        public string NewDisplayName
        {
            get => _newDisplayName;
            set => SetProperty(ref _newDisplayName, value);
        }

        #endregion

        #region 采集参数（作用于选中相机）

        private double _exposureTimeUs = 10000;

        public double ExposureTimeUs
        {
            get => _exposureTimeUs;
            set => SetProperty(ref _exposureTimeUs, value);
        }

        private double _gain;

        public double Gain
        {
            get => _gain;
            set => SetProperty(ref _gain, value);
        }

        private CameraTriggerMode _triggerMode = CameraTriggerMode.Software;

        public CameraTriggerMode TriggerMode
        {
            get => _triggerMode;
            set => SetProperty(ref _triggerMode, value);
        }

        public IReadOnlyList<CameraTriggerModeOption> TriggerModeOptions { get; } = new[]
        {
            new CameraTriggerModeOption { Value = CameraTriggerMode.Software, Display = "软触发（软件下令曝光）" },
            new CameraTriggerModeOption { Value = CameraTriggerMode.RisingEdge, Display = "上升沿（外部信号触发）" },
            new CameraTriggerModeOption { Value = CameraTriggerMode.FallingEdge, Display = "下降沿（外部信号触发）" },
        };

        private int _frameTimeoutMs = 3000;

        public int FrameTimeoutMs
        {
            get => _frameTimeoutMs;
            set => SetProperty(ref _frameTimeoutMs, value);
        }

        private int _bufferCapacity = 8;

        public int BufferCapacity
        {
            get => _bufferCapacity;
            set => SetProperty(ref _bufferCapacity, value);
        }

        private int _heartbeatTimeoutMs = 3000;

        public int HeartbeatTimeoutMs
        {
            get => _heartbeatTimeoutMs;
            set => SetProperty(ref _heartbeatTimeoutMs, value);
        }

        private bool _autoConnect;

        /// <summary>
        /// 自动连接：勾选即刻写回配置对象（随方案落盘）。
        /// 与曝光那类"要下发给硬件"的参数不同，它只是"下次开机连不连"的意图，
        /// 没必要等"应用参数"——等的话用户勾了没点应用就会以为已经生效。
        /// </summary>
        public bool AutoConnect
        {
            get => _autoConnect;
            set
            {
                if (!SetProperty(ref _autoConnect, value)) return;
                if (_loadingParams) return;

                if (SelectedCamera != null) SelectedCamera.Descriptor.AutoConnect = value;
                _provider.MarkConfigDirty(); // 让运行态下次取用时把新配置灌进设备
            }
        }

        #endregion

        #region 网络相机提示

        private bool _isNetworkCamera;

        public bool IsNetworkCamera
        {
            get => _isNetworkCamera;
            private set => SetProperty(ref _isNetworkCamera, value);
        }

        private bool _networkListening;

        public bool NetworkListening
        {
            get => _networkListening;
            private set
            {
                if (!SetProperty(ref _networkListening, value)) return;
                RaisePropertyChanged(nameof(NetworkListeningText));
            }
        }

        private string _networkFrameUrl = string.Empty;

        public string NetworkFrameUrl
        {
            get => _networkFrameUrl;
            private set => SetProperty(ref _networkFrameUrl, value);
        }

        private string _networkHeartbeatUrl = string.Empty;

        public string NetworkHeartbeatUrl
        {
            get => _networkHeartbeatUrl;
            private set => SetProperty(ref _networkHeartbeatUrl, value);
        }

        private string _networkTokenText = string.Empty;

        public string NetworkTokenText
        {
            get => _networkTokenText;
            private set => SetProperty(ref _networkTokenText, value);
        }

        public string NetworkListeningText => NetworkListening
            ? "收图服务正在监听，可接收客户端推来的帧。"
            : "收图服务未启动，网络相机会永远等不到图。请检查 AppConfig.json 的 NetworkCameraServer.Enabled 是否为 true，并确认端口未被占用。";

        #endregion

        #region 预览

        private BitmapSource? _previewImage;

        /// <summary>选中相机的最新一帧（预览用）。只读不消费，理由见 OnPreviewTick</summary>
        public BitmapSource? PreviewImage
        {
            get => _previewImage;
            private set
            {
                if (!SetProperty(ref _previewImage, value)) return;
                RaisePropertyChanged(nameof(HasPreviewImage));
            }
        }

        /// <summary>是否有可显示的预览帧（无帧时界面显示"等待图像"提示，而不是一片空白）</summary>
        public bool HasPreviewImage => _previewImage != null;

        #endregion

        #region 命令

        public DelegateCommand AddCommand { get; }

        public DelegateCommand ConfirmAddCommand { get; }

        public DelegateCommand CancelAddCommand { get; }

        public DelegateCommand<CameraRowViewModel> DeleteCommand { get; }

        public DelegateCommand ToggleConnectCommand { get; }

        public DelegateCommand ToggleStreamCommand { get; }

        public DelegateCommand ResetCountersCommand { get; }

        public DelegateCommand ApplySettingsCommand { get; }

        public DelegateCommand CloseCommand { get; }

        #endregion

        #region 列表装载与运行态对齐

        private void ExecuteBeginAdd()
        {
            AvailableDrivers = _provider.AvailableDrivers;
            if (AvailableDrivers.Count == 0)
            {
                Notifier.ShowWarning("没有可用的相机驱动。请确认相机驱动插件已部署，并在插件扫描目录中被加载。");
                return;
            }
            IsAdding = true;
        }

        private void ResetAddForm()
        {
            NewDriver = AvailableDrivers.FirstOrDefault();
            NewSerial = string.Empty;
            NewDisplayName = string.Empty;
        }

        private void ExecuteConfirmAdd()
        {
            var driver = NewDriver;
            if (driver == null)
            {
                Notifier.ShowWarning("请先选择相机驱动类型。");
                return;
            }

            var serial = (NewSerial ?? string.Empty).Trim();
            if (serial.Length == 0)
            {
                Notifier.ShowWarning("序列号不能为空：它是客户端寻址相机的唯一键，必须与推图 URL 里的 {序列号} 完全一致。");
                return;
            }

            if (!_provider.IsSerialAvailable(serial, Guid.Empty))
            {
                Notifier.ShowWarning(
                    $"序列号「{serial}」已被其它相机占用。序列号必须唯一——两个同序列号的相机，"
                    + "会让按序列号取图变成随机命中，现场表现为「A 相机的图跑到 B 流程里」，最难排查。");
                return;
            }

            var configs = _workspace?.CurrentSolution?.CameraConfigs;
            if (configs == null)
            {
                Notifier.ShowWarning("当前没有打开的方案，无法添加相机。请先新建或打开一个方案。");
                return;
            }

            var descriptor = new CameraDescriptor
            {
                Id = Guid.NewGuid(),
                SerialNo = serial,
                // 显示名留空就用序列号兜底：列表里总得有个能认的标识
                DisplayName = string.IsNullOrWhiteSpace(NewDisplayName) ? serial : NewDisplayName.Trim(),
                DriverTypeKey = driver.TypeKey,
                Settings = new CameraSettings()
            };

            configs.Add(descriptor);

            IsAdding = false;
            ReloadAndSync();
            SelectedCamera = Cameras.FirstOrDefault(r => r.Id == descriptor.Id) ?? SelectedCamera;

            Notifier.ShowSuccess($"已添加相机「{descriptor.Caption}」。如需长期生效请保存方案。");
        }

        private void ExecuteDelete(CameraRowViewModel row)
        {
            if (row == null) return;

            var configs = _workspace?.CurrentSolution?.CameraConfigs;
            if (configs == null)
            {
                Notifier.ShowWarning("当前没有打开的方案，无法删除相机。");
                return;
            }

            var message = $"确定要删除相机「{row.DisplayName}」吗？\n"
                          + "此操作不可撤销：该相机的配置会从当前方案移除，运行态连接会立即断开。";
            var flow = _workspace?.CurrentFlow;
            if (flow != null && flow.RunState == FlowRunState.Running)
                message += "\n\n警告：流程正在运行，删除相机可能让引用它的采集步骤立即报错！";

            if (!EasyDialog.ShowSync("删除相机确认", message)) return;

            var descriptor = configs.FirstOrDefault(c => c.Id == row.Id);
            if (descriptor != null) configs.Remove(descriptor);

            // 设备实例的 Close/Dispose 一律交给 provider 的 SyncFromSolution 处理：
            // 界面自己先 Dispose 会和 provider 内部那份引用打架（"界面已释放，provider 还在用"）
            ReloadAndSync();
            Notifier.ShowInfo($"相机「{row.DisplayName}」已删除。如需长期生效请保存方案。");
        }

        /// <summary>
        /// 增删改后统一走这里：标脏 → 对齐运行态 → 重建列表行。
        /// 顺序不能反：Must 先 MarkConfigDirty + SyncFromSolution 让设备表跟上，
        /// 再重建行（行里缓存的是设备引用，设备一旦被重建，旧引用就是已释放对象）。
        /// </summary>
        private void ReloadAndSync()
        {
            _reloading = true;
            try
            {
                _provider.MarkConfigDirty();
                var errors = _provider.SyncFromSolution();
                if (errors.Count > 0) Notifier.ShowError("部分相机未能创建：\n" + string.Join("\n", errors));
            }
            catch (Exception ex)
            {
                Notifier.ShowError($"同步相机运行态失败：{ex.Message}");
            }
            finally
            {
                _reloading = false;
            }

            ReloadRows();
        }

        /// <summary>按"当前方案配置 + 运行态设备"重建列表行。旧行必须先 Dispose 退订</summary>
        private void ReloadRows()
        {
            if (_closed) return;

            var keepId = SelectedCamera?.Id ?? Guid.Empty;

            foreach (var row in Cameras) row.Dispose();
            Cameras.Clear();

            var driverNames = AvailableDrivers
                .GroupBy(d => d.TypeKey, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().DisplayName, StringComparer.Ordinal);

            foreach (var descriptor in _provider.Cameras)
            {
                _provider.TryGetDevice(descriptor.Id, out var device);

                var driverName = descriptor.DriverTypeKey != null
                                 && driverNames.TryGetValue(descriptor.DriverTypeKey, out var name)
                    ? name
                    : "驱动不可用";

                Cameras.Add(new CameraRowViewModel(descriptor, device, driverName));
            }

            // 尽量保住原来的选中项（按 Id），保不住就选第一台
            SelectedCamera = Cameras.FirstOrDefault(r => r.Id == keepId) ?? Cameras.FirstOrDefault();
        }

        /// <summary>
        /// 设备表被别处重建（例如流程线程触发的按需对齐）时，行里缓存的设备引用会失效，必须重建。
        /// 但本窗口自己发起的同步也会走到这里：靠 RowsMatchProvider 判定"这只是自己同步后的回声"，
        /// 是回声就不重建——否则每加一台相机会重建两遍列表（选中项也会闪一下）。
        /// </summary>
        private void OnDevicesChanged(object? sender, EventArgs e)
        {
            if (_closed) return;

            SafeDispatch.BeginInvoke(() =>
            {
                if (_closed || _reloading) return;
                if (RowsMatchProvider()) return;
                ReloadRows();
            });
        }

        /// <summary>列表行与 provider 的设备表是否逐项对应（Id 相同、设备引用相同）</summary>
        private bool RowsMatchProvider()
        {
            var descriptors = _provider.Cameras;
            if (descriptors.Count != Cameras.Count) return false;

            for (var i = 0; i < descriptors.Count; i++)
            {
                if (Cameras[i].Id != descriptors[i].Id) return false;
                if (!_provider.TryGetDevice(descriptors[i].Id, out var device)) return false;
                if (!ReferenceEquals(Cameras[i].Device, device)) return false;
            }

            return true;
        }

        private void LoadParamsFromSelection()
        {
            var row = SelectedCamera;
            if (row == null) return;

            _loadingParams = true;
            try
            {
                var settings = (row.Descriptor.Settings ?? new CameraSettings()).Normalized();
                ExposureTimeUs = settings.ExposureTimeUs;
                Gain = settings.Gain;
                TriggerMode = settings.TriggerMode;
                FrameTimeoutMs = settings.FrameTimeoutMs;
                BufferCapacity = settings.BufferCapacity;
                HeartbeatTimeoutMs = settings.HeartbeatTimeoutMs;
                AutoConnect = row.Descriptor.AutoConnect;
            }
            finally
            {
                _loadingParams = false;
            }
        }

        private void RefreshNetworkInfo()
        {
            var row = SelectedCamera;
            IsNetworkCamera = row?.IsNetworkCamera ?? false;

            var cfg = _networkServer?.EffectiveSettings ?? new NetworkCameraServerSettings();
            NetworkListening = _networkServer?.IsListening ?? false;

            if (!IsNetworkCamera || row == null) return;

            var rawHost = string.IsNullOrWhiteSpace(cfg.Host) ? NetworkCameraServerSettings.DefaultHost : cfg.Host;
            var port = cfg.Port > 0 && cfg.Port <= 65535 ? cfg.Port : NetworkCameraServerSettings.DefaultPort;

            // 监听端写 0.0.0.0 / [::] 是"听所有网卡"，它不是可连接的目标地址：
            // 直接显示会让用户拿 0.0.0.0 去填客户端（连不上），这里换成可读的"本机IP"
            var host = rawHost is "0.0.0.0" or "[::]" or "::" ? "本机IP" : rawHost;
            var serial = string.IsNullOrWhiteSpace(row.SerialNo) ? "{序列号}" : row.SerialNo;

            NetworkFrameUrl = $"http://{host}:{port}/camera/{serial}/frame";
            NetworkHeartbeatUrl = $"http://{host}:{port}/camera/{serial}/heartbeat";
            NetworkTokenText = string.IsNullOrWhiteSpace(cfg.Token) ? "（未配置）" : cfg.Token;
        }

        #endregion

        #region 选中相机的操作

        private void ExecuteToggleConnect()
        {
            var row = SelectedCamera;
            var device = row?.Device;
            if (row == null || device == null)
            {
                Notifier.ShowWarning("该相机没有可用的运行态设备（驱动插件未加载），无法连接。");
                return;
            }

            try
            {
                if (device.State == CameraConnectionState.Closed)
                {
                    if (!device.Open())
                    {
                        Notifier.ShowError($"连接相机「{row.DisplayName}」失败：{device.StateDetail}");
                        row.RefreshFromDevice();
                        return;
                    }

                    Notifier.ShowSuccess(row.IsNetworkCamera
                        ? $"相机「{row.DisplayName}」已打开，正在等待客户端按序列号接入（推帧 / 心跳）。"
                        : $"相机「{row.DisplayName}」已连接。");
                }
                else
                {
                    device.Close();
                    Notifier.ShowInfo($"相机「{row.DisplayName}」已断开。");
                }

                row.RefreshFromDevice();
            }
            catch (Exception ex)
            {
                Notifier.ShowError($"操作相机「{row.DisplayName}」失败：{ex.Message}");
            }
        }

        private void ExecuteToggleStream()
        {
            var row = SelectedCamera;
            var device = row?.Device;
            if (row == null || device == null)
            {
                Notifier.ShowWarning("该相机没有可用的运行态设备（驱动插件未加载），无法采流。");
                return;
            }

            try
            {
                if (device.State == CameraConnectionState.Streaming)
                {
                    device.StopStream();
                    Notifier.ShowInfo($"相机「{row.DisplayName}」已停止采流。");
                }
                else
                {
                    if (device.State == CameraConnectionState.Closed)
                    {
                        Notifier.ShowWarning("请先点击「连接」，再开始采流。");
                        return;
                    }

                    if (!device.StartStream())
                    {
                        Notifier.ShowError($"相机「{row.DisplayName}」开始采流失败：{device.StateDetail}");
                        row.RefreshFromDevice();
                        return;
                    }

                    Notifier.ShowSuccess($"相机「{row.DisplayName}」已开始采流。");
                }

                row.RefreshFromDevice();
            }
            catch (Exception ex)
            {
                Notifier.ShowError($"操作相机「{row.DisplayName}」失败：{ex.Message}");
            }
        }

        private void ExecuteResetCounters()
        {
            var row = SelectedCamera;
            var device = row?.Device;
            if (row == null || device == null) return;

            try
            {
                device.ResetCounters();
                row.RefreshFromDevice();
                Notifier.ShowInfo($"相机「{row.DisplayName}」的溢出 / 丢弃计数已清零，重新开始观察。");
            }
            catch (Exception ex)
            {
                Notifier.ShowError($"清零计数失败：{ex.Message}");
            }
        }

        private void ExecuteApplySettings()
        {
            var row = SelectedCamera;
            var device = row?.Device;
            if (row == null || device == null) return;

            var settings = new CameraSettings
            {
                ExposureTimeUs = ExposureTimeUs,
                Gain = Gain,
                TriggerMode = TriggerMode,
                FrameTimeoutMs = FrameTimeoutMs,
                BufferCapacity = BufferCapacity,
                HeartbeatTimeoutMs = HeartbeatTimeoutMs
            };

            bool ok;
            try
            {
                ok = device.ApplySettings(settings);
            }
            catch (Exception ex)
            {
                Notifier.ShowError($"应用参数失败：{ex.Message}");
                return;
            }

            if (!ok)
            {
                Notifier.ShowError($"相机「{row.DisplayName}」拒绝了本次参数：{device.StateDetail}");
                return;
            }

            // ApplySettings 已把归一化后的值写回 Descriptor.Settings（同一实例），随方案落盘。
            // 标脏让运行态下次取用时重新灌一遍配置（driver/serial 未变的相机不会重建，连接不中断）
            _provider.MarkConfigDirty();

            LoadParamsFromSelection(); // 回灌归一化后的真实值（例如容量被收敛到 [1,1024]）
            row.RefreshFromDevice();

            Notifier.ShowSuccess("参数已应用（如需长期生效请保存方案）。");
        }

        #endregion

        #region 预览

        private void OnPreviewTick(object? sender, EventArgs e)
        {
            if (_closed) return;

            var row = SelectedCamera;
            var device = row?.Device;
            if (row == null || device == null) return;

            // 必须用 TryGetLatest（只读，Peek 不 Dequeue）：预览绝不能吃掉生产帧，
            // 否则一打开本窗口就会把流程该消费的图抢走，表现为"偶发漏检"，且极难复现。
            if (device.TryGetLatest(out var frame) && frame != null)
                PreviewImage = ToBitmapSource(frame);

            // 计数顺便刷一次：StateChanged 只在状态"文字变化"时才发通知，
            // 采流稳定后"已收帧数"不再触发，界面数字会看起来卡住
            row.RefreshFromDevice();
        }

        /// <summary>
        /// <see cref="CameraFrame"/> 原始像素 → WPF <see cref="BitmapSource"/>。
        ///
        /// 像素布局（与 Core.Interfaces.CameraFrame 的约定逐字一致）：
        ///   行优先、无行填充（stride = 宽 × 通道）；
        ///   灰度 = 1 字节/像素 → <see cref="PixelFormats.Gray8"/>；
        ///   彩色 = BGR 交错 3 字节/像素 → <see cref="PixelFormats.Bgr24"/>（B 在前）。
        /// 写错通道序的表现是"灰度图看着正常、彩色图红蓝反"，只影响彩色判别类算子，
        /// 是极难定位的一类缺陷——所以这里必须与 ImageBytesCodec 的约定保持一致。
        /// </summary>
        private static BitmapSource? ToBitmapSource(CameraFrame frame)
        {
            if (frame == null || !frame.IsValid) return null;

            var channels = frame.Channels < 1 ? 1 : frame.Channels;
            var isColor = channels >= 3;

            var format = isColor ? PixelFormats.Bgr24 : PixelFormats.Gray8;
            var stride = frame.Width * (isColor ? 3 : 1);

            var source = BitmapSource.Create(frame.Width, frame.Height, 96, 96, format, null, frame.PixelData, stride);
            // Freeze 后可跨线程只读共享，WPF 渲染时不必再拷贝，也避免"建立 UI 线程亲和"的隐患
            source.Freeze();
            return source;
        }

        #endregion
    }
}
