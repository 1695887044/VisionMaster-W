using Core.Interfaces;
using Prism.Commands;
using Prism.Dialogs;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 插件自定义配置视图的公共底板 ViewModel
    /// 顶部：插件图标+名称
    /// 中间：注入插件自定义视图（实现 IPluginConfigView）
    /// 底部：状态/耗时/执行/确认/取消
    /// 试运行由 PluginTestRunner 基建统一执行（与正式运行同一套编译与执行链路，仅执行目标节点）
    /// </summary>
    public class PluginConfigShellViewModel : BindableBase, IDialogAware
    {
        private IStepConfigData _stepData;
        private IPluginConfigView _pluginView;
        private IVisionPlugin _plugin;
        private FlowSession _trialSession; // 试运行会话（含编译实例及输出数据），窗口关闭或下次试运行时释放
        private CancellationTokenSource _execCts; // 当前试运行的取消源：点"取消"/关窗时 Cancel，叫停插件里的阻塞等待
        private Task _execTask;                   // 当前试运行的后台任务：关窗时据此"等收尾"，避免释放掉它正在用的资源
        private int _execRunId;                   // 试运行轮次号：丢弃上一轮迟到的回调，防止旧结果覆盖新一轮界面
        private bool _closeAfterStop;             // 点过"取消"且当时正在执行：等后台收尾后再关窗
        private bool _closed;                     // 窗口已关闭：迟到的回调只回收资源，不再碰界面
        private readonly IWorkspaceManager _workspace;
        private readonly ILogService _logger;
        private readonly FlowCompiler _flowCompiler;
        private readonly HttpImageServer _httpServer;

        public PluginConfigShellViewModel(IWorkspaceManager workspace, ILogService logger, FlowCompiler flowCompiler, HttpImageServer httpServer, ICameraProvider cameras)
        {
            _workspace = workspace;
            _logger = logger;
            _flowCompiler = flowCompiler;
            _httpServer = httpServer;
            _cameras = cameras ?? NullCameraProvider.Instance;
            ExecuteCommand = new DelegateCommand(ExecutePlugin, () => CanExecute);
            ConfirmCommand = new DelegateCommand(Confirm);
            CancelCommand = new DelegateCommand(Cancel);
        }

        /// <summary>
        /// 相机仓库：一是给插件的配置上下文提供"当前有哪些相机"（相机采集模式的下拉），
        /// 二是透传给试运行的执行上下文（否则试运行里取相机会拿到 NullCameraProvider）。
        /// </summary>
        private readonly ICameraProvider _cameras;

        #region IDialogAware

        public DialogCloseListener RequestClose { get; }

        public bool CanCloseDialog() => true;

        public void OnDialogClosed()
        {
            // 窗口已关：后台回调若迟到，只回收本轮会话，不再刷界面
            _closed = true;

            // 1. 先取消：让仍在阻塞等待的试运行（如网络采集等图）立刻退出
            _execCts?.Cancel();

            // 2. 等收尾（上限 2 秒）：此刻绝不能提前释放会话/插件——后台线程还在用它们。
            //    正常插件（如网络采集等图）收到令牌后毫秒级返回，2 秒只是兜底
            var pending = _execTask;
            if (pending != null && !pending.IsCompleted)
                pending.Wait(TimeSpan.FromSeconds(2));

            // 3. 已停下则就地释放；仍未停下（某插件不理会取消令牌）则把释放移交后台线程。
            //    宁可有短暂的资源滞留，也不让窗口假死
            if (pending == null || pending.IsCompleted)
                DisposeResources();
            else
                pending.ContinueWith(_ => DisposeResources(), TaskScheduler.Default);
        }

        private void DisposeResources()
        {
            // 释放试运行会话：整套编译实例及其输出数据（HImage 等）随窗口关闭一起回收，
            // 兼顾"数据留存调试"与"非托管资源防泄漏"
            _trialSession?.Dispose();
            _trialSession = null;

            // 配置窗口关闭（确认/取消/叉掉均走此回调）：释放配置实例持有的非托管资源（如 HImage 预览图）
            // 配置实例由 ProcessViewModel.ResolvePluginInstance 每次 new（Activator.CreateInstance），
            // 与流程运行实例完全隔离，Dispose 不影响流程执行
            try
            {
                _plugin?.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.Info($"释放插件配置实例异常: {ex.Message}");
            }
            finally
            {
                _plugin = null;
                _pluginView = null;
                PluginViewContent = null;
                _execCts?.Dispose();
                _execCts = null;
            }
        }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            if (parameters.TryGetValue<IStepConfigData>("StepData", out var stepData))
                _stepData = stepData;

            if (parameters.TryGetValue<FrameworkElement>("PluginView", out FrameworkElement viewObj))
            {
                // 视图由插件DLL返回，同时实现 IPluginConfigView 接口

                _pluginView = viewObj.DataContext as IPluginConfigView;
                PluginViewContent = viewObj;
            }

            if (parameters.TryGetValue<IVisionPlugin>("Plugin", out var plugin))
                _plugin = plugin;

            if (_stepData != null)
            {
                PluginIcon = _stepData.Icon ?? "\uf110";
                PluginName = _stepData.StepName ?? "插件配置";
                Title = _stepData.StepName ?? "插件配置";
                PluginDescription = _stepData.Description ?? string.Empty;
                _pluginView?.Initialize(_stepData);
            }

            // 把宿主运行环境透传给需要它的插件视图（如采集插件的"本地图片推送测试"要拼真实 URL / 令牌）
            // 必须放在 Initialize 之后：插件 Initialize 可能写默认值，宿主透传的真实值应当最后落地
            if (_pluginView is IPluginConfigContextProvider contextProvider)
                contextProvider.SetConfigContext(BuildConfigContext());

            RaisePropertyChanged(nameof(PluginIcon));
            RaisePropertyChanged(nameof(PluginName));
            RaisePropertyChanged(nameof(PluginDescription));
            RaisePropertyChanged(nameof(PluginViewContent));
        }

        /// <summary>
        /// 组装透传给插件视图的宿主上下文快照
        /// 流程名取自当前工作区，HTTP 连接参数取自 HttpImageServer 的生效配置
        /// </summary>
        private PluginConfigContext BuildConfigContext()
        {
            var cfg = _httpServer?.EffectiveSettings ?? new HttpImageServerSettings();
            var host = string.IsNullOrWhiteSpace(cfg.Host) ? HttpImageServerSettings.DefaultHost : cfg.Host;

            return new PluginConfigContext
            {
                FlowName = _workspace?.CurrentFlow?.FlowName ?? string.Empty,
                HttpEnabled = cfg.Enabled,
                HttpListening = _httpServer?.IsListening ?? false,
                // 监听端写 0.0.0.0 是"听所有网卡"，它不是可连接的目标地址 —— 本机测试要连回环地址
                HttpHost = host is "0.0.0.0" or "[::]" or "::" ? "127.0.0.1" : host,
                HttpPort = cfg.Port > 0 && cfg.Port <= 65535 ? cfg.Port : HttpImageServerSettings.DefaultPort,
                HttpToken = cfg.Token ?? string.Empty,
                RequestTimeoutMs = cfg.RequestTimeoutMs,
                Cameras = BuildCameraOptions()
            };
        }

        /// <summary>
        /// 把当前方案的相机拷成快照列表。
        /// 只拷展示与寻址需要的字段，不让插件拿到宿主方案里的活对象（见 PluginConfigContext.Cameras 的说明）。
        /// 状态文字一并带上：用户在配置界面选相机时最想知道的就是"这台现在连上没有"，
        /// 若只给个名字，选完才发现相机根本没连，白跑一次试运行。
        /// </summary>
        private List<CameraOption> BuildCameraOptions()
        {
            var options = new List<CameraOption>();

            foreach (var descriptor in _cameras.Cameras)
            {
                var state = CameraConnectionState.Closed;
                if (_cameras.TryGetDevice(descriptor.Id, out var device) && device != null)
                    state = device.State;

                options.Add(new CameraOption
                {
                    Id = descriptor.Id,
                    SerialNo = descriptor.SerialNo ?? string.Empty,
                    DisplayName = descriptor.DisplayName ?? string.Empty,
                    State = state,
                    StateText = DescribeCameraState(state)
                });
            }

            return options;
        }

        /// <summary>
        /// 状态 → 界面文案。写成"人在现场会怎么描述它"，不要用枚举名直译：
        /// 用户在配置界面上看到 "Connecting" 只会更困惑，看到"等待接入"才知道要去看客户端。
        /// </summary>
        private static string DescribeCameraState(CameraConnectionState state) => state switch
        {
            CameraConnectionState.Streaming => "采流中",
            CameraConnectionState.Online => "已连接·未采流",
            CameraConnectionState.Connecting => "等待接入",
            _ => "未连接"
        };

        #endregion

        #region 绑定属性

        public string Title { get; set; }
        public string PluginIcon { get; private set; }
        public string PluginName { get; private set; }
        public string PluginDescription { get; private set; }
        public object PluginViewContent { get; private set; }

        private string _statusText = "状态: 待执行";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private string _elapsedText = "耗时: 0 ms";
        public string ElapsedText
        {
            get => _elapsedText;
            set => SetProperty(ref _elapsedText, value);
        }

        private bool _isExecuting;
        public bool IsExecuting
        {
            get => _isExecuting;
            set
            {
                if (SetProperty(ref _isExecuting, value))
                {
                    RaisePropertyChanged(nameof(CanExecute));
                }
            }
        }

        public bool CanExecute => !IsExecuting;

        #endregion

        #region 命令

        public ICommand ExecuteCommand { get; private set; }
        public ICommand ConfirmCommand { get; private set; }
        public ICommand CancelCommand { get; private set; }

        #endregion
        private void ExecutePlugin()
        {
            if (_plugin == null)
            {
                StatusText = "状态: 插件实例未注入";
                return;
            }

            if (_stepData == null)
            {
                StatusText = "状态: 步骤数据未注入";
                return;
            }

            // 先把界面上未确认的修改同步进流程（触发版本号递增），保证"所见即所试"
            _pluginView?.OnConfirm(_stepData);

            // 释放上一次试运行的会话（旧数据随本次试运行被替换，防非托管资源堆积）
            _trialSession?.Dispose();
            _trialSession = null;

            // 换发新令牌，并让上一轮（若有）作废
            _execCts?.Cancel();
            _execCts?.Dispose();
            _execCts = new CancellationTokenSource();
            var token = _execCts.Token;

            var runId = ++_execRunId;
            var plugin = _plugin;
            var stepData = _stepData;

            IsExecuting = true;
            ElapsedText = "耗时: 0 ms";
            StatusText = "状态: 执行中...";

            // 试运行必须跑在后台线程：Prism 的 DelegateCommand 对同步委托是"就地执行"（即 UI 线程），
            // 一旦插件内部阻塞等待（如网络采集等图），消息泵停转 → 连"取消"按钮都点不动。
            // 试运行基建：上游链在编译实例上供数，目标插件在配置实例上执行（所见即所得，
            // RunAlgorithm 赋值的属性/输出直接落在界面绑定的实例上），数据留存供调试查看
            _execTask = Task.Run(() =>
            {
                PluginExecuteResult result = null;
                FlowSession session = null;
                Exception error = null;

                try
                {
                    result = PluginTestRunner.Run(plugin, stepData, _workspace, _logger, _flowCompiler, token, out session, _cameras);
                }
                catch (Exception ex)
                {
                    error = ex;
                }

                // "是否被取消"由发起方判定（令牌是自己 Cancel 的），不依赖插件的返回约定
                var cancelled = token.IsCancellationRequested;

                // InvokeAsync 是非阻塞投递：即使此刻 UI 线程正卡在关窗流程里等本任务收尾，也不会互相死等
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    session?.Dispose();
                    return;
                }

                dispatcher.InvokeAsync(() => CompleteExecute(runId, result, session, cancelled, error));
            });
        }

        /// <summary>
        /// 试运行收尾（UI 线程）：刷新状态栏、回收本轮会话，必要时补关窗口
        /// </summary>
        private void CompleteExecute(int runId, PluginExecuteResult result, FlowSession session, bool cancelled, Exception error)
        {
            // 窗口已关，或已开了新一轮：本次结果过期，只回收本轮会话，不碰界面
            if (_closed || runId != _execRunId)
            {
                session?.Dispose();
                return;
            }

            _trialSession = session;
            ElapsedText = $"耗时: {result?.ElapsedMs ?? 0} ms";

            if (cancelled)
            {
                // 用户主动停止（如网络采集正在等图）：本步确实没产出，但不是业务失败，
                // 界面按"已取消"呈现，避免把主动停止误报成故障
                var msg = result?.ErrorMessage;
                StatusText = $"状态: ⏹ 已取消 - {(string.IsNullOrEmpty(msg) ? "已停止" : msg)}";
            }
            else if (error != null)
            {
                StatusText = $"状态: ❌ 异常 - {error.Message}";
            }
            else if (result != null && result.Success)
            {
                StatusText = $"状态: ✅ 成功 - {result.Message}";
            }
            else
            {
                var msg = result?.ErrorMessage;
                StatusText = $"状态: ❌ 失败 - {(string.IsNullOrEmpty(msg) ? "执行失败" : msg)}";
            }

            IsExecuting = false;

            // 点过"取消"的：后台已收尾，现在才真正关窗
            if (_closeAfterStop)
                RequestClose.Invoke(ButtonResult.Cancel);
        }

        private void Confirm()
        {
            _pluginView?.OnConfirm(_stepData);
            RequestClose.Invoke(ButtonResult.OK);
        }

        private void Cancel()
        {
            _pluginView?.OnCancel();

            // 有试运行在跑：先取消、等它收尾，收尾完成后由 CompleteExecute 关窗。
            // 不能立刻关窗——关窗会释放掉后台线程正在使用的会话与插件实例
            if (_execTask != null && !_execTask.IsCompleted)
            {
                _closeAfterStop = true;
                StatusText = "状态: ⏹ 正在停止...";
                _execCts?.Cancel();
                return;
            }

            RequestClose.Invoke(ButtonResult.Cancel);
        }
    }
}
