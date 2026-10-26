using Core.Interfaces;
using Core.Interfaces.Assets;
using Core.Interfaces.Database;
using Prism.Commands;
using Prism.Dialogs;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
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
        private readonly IUserNotifier _notifier;

        public PluginConfigShellViewModel(IWorkspaceManager workspace, ILogService logger, FlowCompiler flowCompiler, HttpImageServer httpServer, ICameraProvider cameras, IMotionProvider motions, IFlowInvoker flowInvoker, ICommVariableAccess comms = null, IUserNotifier notifier = null, IDatabaseAccess database = null)
        {
            _workspace = workspace;
            _logger = logger;
            _flowCompiler = flowCompiler;
            _httpServer = httpServer;
            _cameras = cameras ?? NullCameraProvider.Instance;
            _motions = motions ?? NullMotionProvider.Instance;
            _flowInvoker = flowInvoker ?? NullFlowInvoker.Instance;
            _comms = comms ?? NullCommVariableAccess.Instance;
            _database = database ?? NullDatabaseAccess.Instance;
            // 可选依赖：容器里已注册 IUserNotifier；断言宿主/设计器手工构造时可以不传，
            // 此时只少一条非模态提示，日志仍然照记（不能因为提示缺失让异常隔离失效）。
            _notifier = notifier;
            ExecuteCommand = new DelegateCommand(ExecutePlugin, () => CanExecute);
            ConfirmCommand = new DelegateCommand(Confirm);
            CancelCommand = new DelegateCommand(Cancel);
        }

        /// <summary>
        /// 相机仓库：一是给插件的配置上下文提供"当前有哪些相机"（相机采集模式的下拉），
        /// 二是透传给试运行的执行上下文（否则试运行里取相机会拿到 NullCameraProvider）。
        /// </summary>
        private readonly ICameraProvider _cameras;

        /// <summary>运动设备仓库：试运行时透传给执行上下文（否则运动步骤取不到卡，见 ExecutePlugin）</summary>
        private readonly IMotionProvider _motions;

        /// <summary>流程调用器：透传给试运行的执行上下文（否则「调用流程」步骤在面板上点「执行」必失败）</summary>
        private readonly IFlowInvoker _flowInvoker;

        /// <summary>通讯变量立刻读写入口：透传给试运行（否则「立刻读/写」在面板上点「执行」必报没有接入通讯层）</summary>
        private readonly ICommVariableAccess _comms;

        /// <summary>数据库能力：透传给试运行与配置上下文（否则数据库步骤试运行/测试语句必报没有接入数据库服务）</summary>
        private readonly IDatabaseAccess _database;

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
                _layoutSwitch = null;
                CanSwitchLayout = false;
                RaisePropertyChanged(nameof(CanSwitchLayout));
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
                // 视图实现 IPluginConfigView 有两种形态，两种都要认：
                //   ① 插件自带的视图：控件本身只是"画界面"，接口在它的 DataContext（插件实例）上；
                //   ② 框架生成的参数面板（AutoPortConfigView）：接口直接实现在控件自身上。
                // 只认 ① 的话，② 的 Initialize 永远不会被调到 —— 现象是弹窗里一片空白
                //（面板建出来了，但没有任何参数行）。
                _pluginView = (viewObj as IPluginConfigView) ?? (viewObj.DataContext as IPluginConfigView);
                PluginViewContent = viewObj;

                // 参数面板布局切换：只有实现了契约的视图才亮出"卡片式"开关（壳不猜内容类型）。
                // 读一次视图的现值，免得开关状态和视图实际布局对不上。
                _layoutSwitch = viewObj as UI.CustomControl.IPropertyGridLayoutSwitch;
                CanSwitchLayout = _layoutSwitch != null;
                _useCardLayout = _layoutSwitch?.UseCardLayout ?? false;
                RaisePropertyChanged(nameof(CanSwitchLayout));
                RaisePropertyChanged(nameof(UseCardLayout));
            }

            if (parameters.TryGetValue<IVisionPlugin>("Plugin", out var plugin))
                _plugin = plugin;

            if (_stepData != null)
            {
                PluginIcon = _stepData.Icon ?? "\uf110";
                PluginName = _stepData.StepName ?? "插件配置";
                Title = _stepData.StepName ?? "插件配置";
                PluginDescription = _stepData.Description ?? string.Empty;

                // 插件视图是第三方代码：初始化失败不许把弹窗带崩（更不许冒到 UI 线程）。
                // 失败时窗口仍然打开——用户至少能看到"插件参数初始化失败"的提示，
                // 而不是点一下菜单什么都不发生。
                try
                {
                    _pluginView?.Initialize(_stepData);
                }
                catch (Exception ex)
                {
                    ReportUiFailure("初始化插件参数面板", ex);
                }
            }

            // 把宿主运行环境透传给需要它的插件视图（如采集插件的"本地图片推送测试"要拼真实 URL / 令牌）
            // 必须放在 Initialize 之后：插件 Initialize 可能写默认值，宿主透传的真实值应当最后落地
            if (_pluginView is IPluginConfigContextProvider contextProvider)
            {
                try
                {
                    contextProvider.SetConfigContext(BuildConfigContext());
                }
                catch (Exception ex)
                {
                    ReportUiFailure("透传宿主配置上下文", ex);
                }
            }

            BuildAssetOptions();

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
                Cameras = BuildCameraOptions(),
                // 变量名候选（变量管理里那些 + 上游「变量定义」声明的运行时变量）：
                // 插件够不到这两处，而"给哪个变量赋值/读哪个变量"只能从它们里选 —— 手打名字
                // 打错的后果（找不到变量 / 值写进另一个同名变量）都很难倒查（真机 2026-10-10）
                Variables = VisionMaster.Helpers.PluginVariableOptions.Build(_workspace, _stepData?.StepId ?? Guid.Empty),
                // 数据库能力（测试语句用）：与执行期同一个单例，保证"测试"和"真跑"看到同一份配置
                Database = _database
            };
        }

        private void BuildAssetOptions()
        {
            _loadingAssetOptions = true;
            AssetOptions.Clear();
            HasAssetOptions = false;

            if (_plugin is IAssetIdConfig config && _workspace?.CurrentSolution is { } solution)
            {
                foreach (var option in BuildAssetOptionsFor(solution, config.AssetKind))
                    AssetOptions.Add(option);

                AssetOptions.Add(new AssetOption(Guid.Empty, "（不使用资产）", config.AssetKind));
                SelectedAssetOption = AssetOptions.FirstOrDefault(
                    option => string.Equals(option.AssetId.ToString(), config.AssetId, StringComparison.OrdinalIgnoreCase))
                    ?? AssetOptions[0];
                HasAssetOptions = AssetOptions.Count > 1;
            }

            _loadingAssetOptions = false;
        }

        /// <summary>
        /// 按资产类型列出候选。**抽成静态方法是为了可断言**：配置壳要装一整套服务才能构造，
        /// 而"配方下拉里到底有没有这条资产"是必须能被测到的判据（选不到就等于没接）。
        /// </summary>
        public static IReadOnlyList<AssetOption> BuildAssetOptionsFor(SolutionModel solution, string assetKind)
        {
            var options = new List<AssetOption>();
            if (solution == null) return options;

            if (string.Equals(assetKind, "template", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var asset in solution.TemplateAssets)
                    options.Add(new AssetOption(asset.AssetId, asset.Name, "template"));
            }
            else if (string.Equals(assetKind, "model", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var asset in solution.ModelAssets)
                    options.Add(new AssetOption(asset.AssetId, asset.Name, "model"));
            }
            else if (string.Equals(assetKind, "image", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var asset in solution.ImageAssets)
                    options.Add(new AssetOption(asset.AssetId, asset.Name, "image"));
            }
            else if (string.Equals(assetKind, "recipe", StringComparison.OrdinalIgnoreCase))
            {
                // 只列**有内联内容**的配方：参数配方没有可被插件解析的文本，
                // 混进下拉只会让人选中后运行期报"没有内联内容"。
                foreach (var recipe in solution.RecipesOrEmpty())
                {
                    if (string.IsNullOrWhiteSpace(recipe.TextContent)) continue;
                    options.Add(new AssetOption(recipe.RecipeId, recipe.Name, "recipe"));
                }
            }

            return options;
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

        /// <summary>
        /// 当前承载的插件视图是否支持"表格 / 卡片"参数布局切换
        /// （视图实现 <see cref="UI.CustomControl.IPropertyGridLayoutSwitch"/> 才为 true）。
        /// </summary>
        public bool CanSwitchLayout { get; private set; }

        private bool _useCardLayout;

        /// <summary>
        /// 参数面板是否用卡片式；写下去直接落到插件视图 —— 视图自己负责切网格与重排列宽
        /// （壳只做"传话"，不碰视图内部布局）。
        /// </summary>
        public bool UseCardLayout
        {
            get => _useCardLayout;
            set
            {
                if (_useCardLayout == value) return;
                _useCardLayout = value;
                if (_layoutSwitch != null) _layoutSwitch.UseCardLayout = value;
                RaisePropertyChanged(nameof(UseCardLayout));
            }
        }

        private UI.CustomControl.IPropertyGridLayoutSwitch? _layoutSwitch;

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

        /// <summary>当前插件可选的方案资产（仅实现 IAssetIdConfig 的插件显示）。</summary>
        public ObservableCollection<AssetOption> AssetOptions { get; } = new();

        private AssetOption? _selectedAssetOption;
        public AssetOption? SelectedAssetOption
        {
            get => _selectedAssetOption;
            set
            {
                if (!SetProperty(ref _selectedAssetOption, value)) return;
                if (_loadingAssetOptions) return;
                if (_plugin is IAssetIdConfig config)
                {
                    config.AssetId = value == null || value.AssetId == Guid.Empty
                        ? string.Empty
                        : value.AssetId.ToString();
                }
            }
        }

        private bool _hasAssetOptions;
        public bool HasAssetOptions
        {
            get => _hasAssetOptions;
            private set => SetProperty(ref _hasAssetOptions, value);
        }

        private bool _loadingAssetOptions;

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

            // 先把界面上未确认的修改同步进流程（触发版本号递增），保证"所见即所试"。
            // 插件视图的 OnConfirm 是第三方代码：同步失败就中止本次试运行，
            // 但**窗口不关**——用户改的值还在界面上，状态栏给出原因。
            try
            {
                _pluginView?.OnConfirm(_stepData);
            }
            catch (Exception ex)
            {
                ReportUiFailure("同步界面参数", ex);
                StatusText = $"状态: ❌ 参数同步失败 - {ex.Message}";
                return;
            }

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
                    result = PluginTestRunner.Run(plugin, stepData, _workspace, _logger, _flowCompiler, token, out session, _cameras, _motions, _flowInvoker, _comms, _database);
                }
                catch (Exception ex)
                {
                    error = ex;
                    // 堆栈必须落日志：状态栏只显示 Message，跨线程/UI 这类问题没有堆栈无法定位
                    _logger.Error($"[试运行] {plugin.InstanceName} 执行异常堆栈: {error}");
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

            // 点过"取消"的：后台已收尾，现在才真正还原模型并关窗
            if (_closeAfterStop)
            {
                CancelPluginViewSafely();
                RequestClose.Invoke(ButtonResult.Cancel);
            }
        }

        private void Confirm()
        {
            try
            {
                _pluginView?.OnConfirm(_stepData);
            }
            catch (Exception ex)
            {
                // 确认失败**不关窗**：用户刚改的值还在界面上，关掉就只剩"没保存成功"的空白，
                // 而且再次打开时看到的是旧值（会以为"改了没生效"）。状态栏 + 非模态提示给原因。
                ReportUiFailure("保存插件参数", ex);
                StatusText = $"状态: ❌ 保存失败 - {ex.Message}";
                return;
            }

            RequestClose.Invoke(ButtonResult.OK);
        }

        private void Cancel()
        {
            // 有试运行在跑：先叫停，等它收尾，**收尾前不能动插件模型/端口**。
            // 旧顺序（先 OnCancel 再 Cancel 令牌）会让 OnCancel 在后台脚本还在
            // 通过 Inputs/Outputs 取写端口时重建动态端口字典（Dictionary 并发读写未定义）；
            // 收尾完成后由 CompleteExecute 补 OnCancel 再关窗。
            if (_execTask != null && !_execTask.IsCompleted)
            {
                _closeAfterStop = true;
                StatusText = "状态: ⏹ 正在停止...";
                _execCts?.Cancel();
                return;
            }

            CancelPluginViewSafely();
            RequestClose.Invoke(ButtonResult.Cancel);
        }

        /// <summary>
        /// 取消插件视图的异常隔离：OnCancel 抛异常也必须把窗口关掉
        ///（否则用户点"取消"毫无反应，比丢掉一次修改更糟）。
        /// </summary>
        private void CancelPluginViewSafely()
        {
            try
            {
                _pluginView?.OnCancel();
            }
            catch (Exception ex)
            {
                ReportUiFailure("取消插件配置", ex);
            }
        }

        /// <summary>
        /// 插件 UI 入口失败的统一出口：日志（含堆栈）+ 非模态提示。
        /// 日志/提示自身失败绝不再抛——异常隔离层再炸一次就等于没隔离。
        /// </summary>
        private void ReportUiFailure(string action, Exception ex)
        {
            try
            {
                _logger?.Error($"[插件配置] {action}失败：{ex}");
            }
            catch
            {
                // 日志服务故障时忽略
            }

            try
            {
                _notifier?.ShowError($"插件参数{action}失败：{ex.Message}（详情见日志）");
            }
            catch
            {
                // 提示服务故障时忽略
            }
        }
    }

    /// <summary>插件配置壳的资产下拉项（只带展示与 Id，不把方案活对象交给插件）。</summary>
    public sealed class AssetOption
    {
        public AssetOption(Guid assetId, string name, string kind)
        {
            AssetId = assetId;
            Name = name ?? string.Empty;
            Kind = kind ?? string.Empty;
        }

        public Guid AssetId { get; }
        public string Name { get; }
        public string Kind { get; }
        public string Caption => AssetId == Guid.Empty
            ? Name
            : $"{Name}（{AssetId.ToString("N")[..8]}）";
    }
}
