using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace VisionMaster.Communications
{
    public class AdvancedCommunicationManager : ICommunicationManager, IDisposable
    {
        #region 私有字段

        // ✅ 全部改为线程安全集合
        private readonly ConcurrentDictionary<string, ICommunicationConnection> _connections = new();
        private readonly ConcurrentDictionary<string, CommunicationConfig> _configCache = new();

        // ✅ 连接专属工作线程：每个连接一条线程，把建连/读/写/轮询/断开全部串行化到同一线程。
        // 旧实现用"重连 + 心跳 + 变量轮询"三套 Timer 并发驱动同一设备对象，而 HSL 设备对象不是线程安全的，
        // 于是出现随机错包、半开连接检测不出来、多个重连叠加发起等问题——现在统一由 Worker 状态机接管。
        private readonly ConcurrentDictionary<string, ConnectionWorker> _workers = new();

        // ✅ 完美适配你的 CommunicationVariable 类
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, CommunicationVariable>> _registeredVariables = new();

        // ✅ ObservableCollection 专用锁（防止非UI线程操作崩溃）
        private readonly object _connectionsListLock = new();
        private readonly ObservableCollection<CommunicationConfig> _connectionsList = new();

        // ✅ 通信故障限流：退避重连期间同一条故障会反复上报，不限流会把日志窗口刷满
        private readonly ConcurrentDictionary<string, long> _throttleTicks = new();
        private const long ThrottleMs = 5000;

        // ✅ 恢复使用你的 ConnectionFactoryManager
        private readonly ConnectionFactoryManager _factoryManager = ConnectionFactoryManager.Instance;

        private readonly object _disposeLock = new();
        private bool _disposed = false;

        #endregion

        #region 公共属性

        public ObservableCollection<CommunicationConfig> ConnectionsList => _connectionsList;
        public int ConnectionCount => _connections.Count;
        public int ConnectedCount => _workers.Count(w => w.Value.IsConnected);
        public bool IsRunning { get; private set; } = false;
        public string ConfigFilePath { get; set; } =
            Path.Combine(AppContext.BaseDirectory, "communications.json"); // 固定 exe 目录，避免工作目录漂移
        public bool AutoReconnectEnabled { get; set; } = true;
        /// <summary>重连基准间隔（连接配置里的 RetryIntervalMs 优先，未配置时用它）</summary>
        public int GlobalReconnectIntervalMs { get; set; } = 5000;
        public int MaxReconnectAttempts { get; set; } = 0; // 0表示无限重连

        #endregion

        #region 事件

        public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;
        public event EventHandler<ConnectionErrorEventArgs>? ConnectionError;
        public event EventHandler<CommunicationDataEventArgs>? DataReceived;

        private event EventHandler<CommunicationErrorEventArgs>? OnCommError;
        private event EventHandler<VariableChangedEventArgs>? OnVarChanged;

        event EventHandler<CommunicationErrorEventArgs>? ICommunicationManager.OnCommunicationError
        {
            add => OnCommError += value;
            remove => OnCommError -= value;
        }

        event EventHandler<VariableChangedEventArgs>? ICommunicationManager.OnVariableChanged
        {
            add => OnVarChanged += value;
            remove => OnVarChanged -= value;
        }

        #endregion

        #region 构造函数

        public AdvancedCommunicationManager()
        {
            LogInfo("AdvancedCommunicationManager 初始化完成");
        }

        #endregion

        #region 连接管理

        public ICommunicationConnection? GetConnection(string connectionName)
        {
            if (string.IsNullOrWhiteSpace(connectionName))
                return null;

            return _connections.TryGetValue(connectionName, out var conn) ? conn : null;
        }

        public List<CommunicationConfig> GetAllConnections()
        {
            lock (_connectionsListLock)
            {
                return _connectionsList.ToList();
            }
        }

        /// <summary>
        /// 读取连接当前状态（读 Worker 状态机，<b>不经 Dispatcher</b>，任何宿主下都正确）。
        /// <para>为什么需要它：<c>GetAllConnections()[i].State</c> 那条回写被包在 SafeDispatch.BeginInvoke 里，
        /// 而 SafeDispatch 在 Application.Current == null（控制台 / Windows 服务 / 单元测试）时直接丢弃动作——
        /// 于是 config.State 会永久停在 Disconnected。本方法读 Worker（连接状态的唯一真相源）绕开该限制。</para>
        /// <para>未登记的连接返回 <see cref="ConnectionState.Disconnected"/>。</para>
        /// </summary>
        public ConnectionState GetConnectionState(string connectionName)
        {
            if (string.IsNullOrWhiteSpace(connectionName))
                return ConnectionState.Disconnected;

            if (_workers.TryGetValue(connectionName, out var worker))
                return worker.State;

            // 兜底：Worker 尚未建好等极少数情况，用连接实现自身的 IsConnected 推断
            if (_connections.TryGetValue(connectionName, out var conn))
                return conn.IsConnected ? ConnectionState.Connected : ConnectionState.Disconnected;

            return ConnectionState.Disconnected;
        }

        /// <summary>
        /// 读取连接最近一次通信故障消息（读 Worker，<b>不经 Dispatcher</b>）；无故障或连接不存在时返回 null。
        /// </summary>
        public string? GetConnectionError(string connectionName)
        {
            if (string.IsNullOrWhiteSpace(connectionName))
                return null;

            return _workers.TryGetValue(connectionName, out var worker) ? worker.LastError : null;
        }

        /// <summary>
        /// 启动通讯子系统：按 <see cref="CommunicationConfig.AutoStart"/> 发起自动连接并置运行标志。
        /// 建连不在此等待（见 <see cref="ConnectAll"/>），调用方不必担心被离线设备阻塞。
        /// </summary>
        public void StartAll()
        {
            LogInfo("正在启动所有连接...");
            ConnectAll();
            IsRunning = true;
        }

        public void StopAll()
        {
            LogInfo("正在停止所有连接...");
            DisconnectAll();
            IsRunning = false;
        }

        public bool AddConnection(CommunicationConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrWhiteSpace(config.ConnectionName))
                throw new ArgumentException("连接名称不能为空", nameof(config));
            if (_connections.ContainsKey(config.ConnectionName))
                throw new InvalidOperationException($"已存在同名连接: {config.ConnectionName}");
            if (!config.Validate(out string error))
                throw new InvalidOperationException($"连接配置验证失败: {error}");

            // B4：这两个对象提到 try 外面声明，catch 里才够得着去回滚。
            // 只用 `var connection = ...` 写在 try 内部的话，异常一抛就出了作用域，想收拾也没得收拾。
            ICommunicationConnection? connection = null;
            ConnectionWorker? worker = null;

            try
            {
                LogInfo($"正在添加连接: {config.ConnectionName} ({config.Protocol})");

                // ✅ 使用你的 ConnectionFactoryManager 创建连接
                connection = _factoryManager.CreateConnection(config);
                _connections[config.ConnectionName] = connection;
                _configCache[config.ConnectionName] = config;

                // 每个连接配一条专属工作线程：此后该连接的所有设备 I/O 都只在这条线程上发生
                worker = new ConnectionWorker(connection);
                worker.StateChanged += (oldState, newState) => OnWorkerStateChanged(config.ConnectionName, oldState, newState);
                worker.CommunicationError += ex => OnWorkerCommunicationError(config.ConnectionName, ex);
                _workers[config.ConnectionName] = worker;
                worker.Start();

                lock (_connectionsListLock)
                {
                    _connectionsList.Add(config);
                }

                config.State = ConnectionState.Disconnected;
                OnConnectionStateChanged(config.ConnectionName, ConnectionState.Disconnected, ConnectionState.Disconnected);

                LogInfo($"连接添加成功: {config.ConnectionName}");
                return true;
            }
            catch (Exception ex)
            {
                // B4：走到这里说明"登记了一部分，然后炸了"，必须把登记痕迹摘干净。
                // 登记顺序是 _connections → _configCache → _workers →（Start）→ _connectionsList，
                // 唯一"登记之后还可能抛"的动作就是 worker.Start()：此时前三个字典里已有它、
                // 而 _connectionsList 里还没有它。不回滚的后果不是"加失败"这么简单：
                // 它是一个**隐形僵尸连接**——UI 列表里看不见，线程却在跑；
                // 且开头那句 ContainsKey 预检会永远为真，用户换参数重试也只会一直得到
                // "已存在同名连接"，除了删连接无路可走。
                RollbackFailedAdd(config, connection, worker);

                LogError($"添加连接失败: {config.ConnectionName}", ex);
                OnConnectionError(config.ConnectionName, ex);
                throw;
            }
        }

        /// <summary>
        /// B4：添加连接中途失败时回滚——只摘**本次调用自己写进去的那一份**。
        /// <para>为什么不直接按名字 <c>TryRemove</c>：方法开头那句 <c>ContainsKey</c> 预检与后面的写入
        /// 之间没有锁，**不是原子的**（TOCTOU）。并发下同一名字可能已由另一路 Add 写入了它自己的对象，
        /// 盲删就会把别人的连接连线程一起干掉。故每一步都先 <c>TryGetValue</c> 再用
        /// <see cref="ReferenceEquals"/> 比对，确认"就是我放进去的那个"才摘。</para>
        /// <para>worker 与 connection 的释放归属：<b>Worker 负责释放 connection</b>
        /// （见 <see cref="ConnectionWorker.Dispose"/>），故有 worker 就只释放 worker，
        /// 没有 worker 才直接释放 connection。两边的 Dispose 都有幂等保护，重复调用安全。</para>
        /// </summary>
        private void RollbackFailedAdd(
            CommunicationConfig config,
            ICommunicationConnection? connection,
            ConnectionWorker? worker)
        {
            string name = config.ConnectionName;

            if (worker != null && _workers.TryGetValue(name, out var registeredWorker) && ReferenceEquals(registeredWorker, worker))
                _workers.TryRemove(name, out _);

            if (connection != null && _connections.TryGetValue(name, out var registeredConnection) && ReferenceEquals(registeredConnection, connection))
                _connections.TryRemove(name, out _);

            if (_configCache.TryGetValue(name, out var registeredConfig) && ReferenceEquals(registeredConfig, config))
                _configCache.TryRemove(name, out _);

            lock (_connectionsListLock)
            {
                // CommunicationConfig 没覆写 Equals，故 IndexOf 就是按引用找——正好是我们要的语义
                int index = _connectionsList.IndexOf(config);
                if (index >= 0)
                    _connectionsList.RemoveAt(index);
            }

            if (worker != null)
            {
                try
                {
                    worker.Dispose();
                }
                catch (Exception ex)
                {
                    LogError($"回滚时释放连接工作线程失败: {name}", ex);
                }
            }
            else if (connection != null)
            {
                try
                {
                    connection.Disconnect();
                    connection.Dispose();
                }
                catch (Exception ex)
                {
                    LogError($"回滚时释放连接对象失败: {name}", ex);
                }
            }
        }

        public bool RemoveConnection(string connectionName)
        {
            if (string.IsNullOrWhiteSpace(connectionName))
                return false;

            LogInfo($"正在移除连接: {connectionName}");

            // 工作线程负责停轮询、断开设备、释放连接对象：Dispose 返回后该连接已彻底不可用
            if (_workers.TryRemove(connectionName, out var worker))
            {
                try
                {
                    worker.Dispose();
                    LogInfo($"连接工作线程已停止: {connectionName}");
                }
                catch (Exception ex)
                {
                    LogError($"释放连接工作线程时发生错误: {connectionName}", ex);
                }
            }
            else if (_connections.TryGetValue(connectionName, out var orphan))
            {
                // 兜底：没有工作线程的连接（正常流程下不存在）仍按旧路径释放
                try
                {
                    orphan.Disconnect();
                    orphan.Dispose();
                }
                catch (Exception ex)
                {
                    LogError($"断开连接时发生错误: {connectionName}", ex);
                }
            }
            _connections.TryRemove(connectionName, out _);

            // 移除配置缓存
            _configCache.TryRemove(connectionName, out _);

            // 移除注册的变量。
            // H3：整连接移除必须同步摘除转发 handler——handler 闭包捕获变量对象，
            // 漏清理会让删连接后的变量对象被转发表钉住无法回收（几万点删连接 = 几万对象滞留）。
            // UnregisterVariable / Dispose 都有清理，唯独这里曾遗漏
            if (_registeredVariables.TryRemove(connectionName, out var removedVars))
            {
                foreach (var kv in removedVars)
                {
                    if (_varForwardHandlers.TryRemove(connectionName + "\\" + kv.Key, out var forwardHandler))
                        kv.Value.ValueChanged -= forwardHandler;
                }
            }

            // 从UI集合中移除
            lock (_connectionsListLock)
            {
                var config = _connectionsList.FirstOrDefault(c => c.ConnectionName == connectionName);
                if (config != null)
                    _connectionsList.Remove(config);
            }

            LogInfo($"连接移除完成: {connectionName}");
            return true;
        }

        public bool UpdateConnection(CommunicationConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            LogInfo($"正在更新连接: {config.ConnectionName}");

            // B1：连接对象会被重建，但"该连接下已注册的变量"必须延续。
            // 旧实现直接 Remove + Add，而 RemoveConnection 会连变量注册一起清空——
            // 于是"改个超时时间"就把这条连接所有变量的轮询悄悄停掉，且无任何提示。
            var keepVariables = _registeredVariables.TryGetValue(config.ConnectionName, out var existing)
                ? existing.Values.ToList()
                : new List<CommunicationVariable>();

            RemoveConnection(config.ConnectionName);

            bool added;
            try
            {
                added = AddConnection(config);
            }
            catch
            {
                // M1：Add 抛异常（配置非法 / 工厂创建失败）——旧连接对象已随 Remove 销毁，
                // 若不回填注册记录，这批变量会静默停止轮询且无任何提示（直到下次 RebindAll 才恢复）。
                // 先回填再向上抛，让 UI 感知失败原因
                RestoreVariables(config.ConnectionName, keepVariables);
                throw;
            }

            // M1：成功也要回填（变量必须延续）；Add 返回 false 时同样保留登记——
            // _registeredVariables 只是"该连接应轮询哪些变量"的登记表，与连接对象/工作线程解耦，
            // 无连接时静默保留，待重新添加同名连接后即可继续参与轮询
            RestoreVariables(config.ConnectionName, keepVariables);
            return added;
        }

        /// <summary>
        /// M1：把变量重新登记进注册表（幂等）。走 RegisterVariable 而非直接写字典——
        /// 它会重建转发 handler 并标脏轮询计划，因此"RemoveConnection（H3 已退订）→ 重新登记"的往返
        /// 不会残留失效订阅。单个变量失败不影响其余变量
        /// </summary>
        private void RestoreVariables(string connectionName, List<CommunicationVariable> variables)
        {
            foreach (var variable in variables)
            {
                try
                {
                    RegisterVariable(variable); // H1：内部只标脏，重建由 Connect 前的统一编译 / Worker 拍前消费兜底
                }
                catch (Exception ex)
                {
                    LogError($"更新连接后恢复变量注册失败: {connectionName}.{variable.VariableName}", ex);
                }
            }
        }

        public bool Connect(string connectionName)
        {
            if (string.IsNullOrWhiteSpace(connectionName))
                throw new ArgumentNullException(nameof(connectionName));
            if (!_connections.ContainsKey(connectionName))
                throw new InvalidOperationException($"连接不存在: {connectionName}");
            if (!_configCache.TryGetValue(connectionName, out var config))
                return false;
            if (!_workers.TryGetValue(connectionName, out var worker))
                return false;

            ConfigureWorker(worker, config);

            // 连接前统一编译一次轮询计划（注册变量时不编译，避免批量注册触发 N 次重建）
            RebuildPollPlan(connectionName);

            LogInfo($"正在连接: {connectionName}");

            try
            {
                // 建连与失败重连都由 Worker 状态机负责；这里只等待"首次建连"的结果。
                // 超时返回 false ≠ 放弃：Worker 仍在后台按退避重连，状态变化会经事件广播出来。
                int timeoutMs = config.Config?.TimeoutMs > 0 ? config.Config.TimeoutMs : 3000;
                if (!worker.Connect(timeoutMs).GetAwaiter().GetResult())
                {
                    LogWarning($"连接未在 {timeoutMs}ms 内建立: {connectionName}（后台按退避继续重连）");
                    return false;
                }

                LogInfo($"连接成功: {connectionName}");
                return true;
            }
            catch (Exception ex)
            {
                LogError($"连接异常: {connectionName}", ex);
                OnConnectionError(connectionName, ex);
                return false;
            }
        }

        public void Disconnect(string connectionName)
        {
            if (string.IsNullOrWhiteSpace(connectionName))
                return;
            if (!_configCache.ContainsKey(connectionName))
                return;
            if (!_workers.TryGetValue(connectionName, out var worker))
                return;

            LogInfo($"正在断开连接: {connectionName}");

            try
            {
                // 断开即停止自动重连；config.State 与状态事件由 Worker 的 StateChanged 回调统一同步
                worker.Disconnect().GetAwaiter().GetResult();
                LogInfo($"连接已断开: {connectionName}");
            }
            catch (Exception ex)
            {
                LogError($"断开连接异常: {connectionName}", ex);
                OnConnectionError(connectionName, ex);
            }
        }

        /// <summary>
        /// <para>发起单条连接的建连（**非阻塞**）：只向工作线程登记"维持连接"意图立即返回，
        /// 真正的 socket 建连由连接专属线程执行，结果经 <see cref="ConnectionStateChanged"/> 广播。</para>
        /// <para>供 UI"连接"按钮等**不能等** <c>Config.TimeoutMs</c> 的调用方使用；
        /// 需要拿到"这一次到底连上没有"的调用方请用同步版 <see cref="Connect"/>。</para>
        /// </summary>
        public void RequestConnect(string connectionName)
        {
            if (string.IsNullOrWhiteSpace(connectionName))
                return;
            if (!_configCache.TryGetValue(connectionName, out var config))
                return;
            if (!_workers.TryGetValue(connectionName, out var worker))
                return;

            // 灌参数 + 编译轮询计划都是本地计算，必须先做：Worker 一被唤醒就能用正确参数与计划工作
            ConfigureWorker(worker, config);
            RebuildPollPlan(connectionName);

            // timeoutMs = 0：登记意图立即返回（与 ConnectAll 同语义）
            _ = worker.Connect(0);
        }

        /// <summary>
        /// <para>发起单条连接的断开（**非阻塞**）：立即停止自动重连，断开动作排队给工作线程执行，
        /// 结果经 <see cref="ConnectionStateChanged"/> 广播。</para>
        /// <para>需要确认"已经断开"的调用方请用同步版 <see cref="Disconnect"/>。
        /// 注意：Worker 若正在做一次阻塞建连，排队中的断开命令要等它返回才执行——这正是本方法
        /// 存在的意义（不让调用线程陪着一起等）。</para>
        /// </summary>
        public void RequestDisconnect(string connectionName)
        {
            if (string.IsNullOrWhiteSpace(connectionName))
                return;
            if (!_workers.TryGetValue(connectionName, out var worker))
                return;

            worker.Disconnect().ContinueWith(t =>
            {
                if (t.IsFaulted)
                    LogWarning($"断开连接异常: {connectionName} - {t.Exception?.GetBaseException().Message}");
            }, TaskScheduler.Default);
        }

        /// <summary>
        /// <para>对"已启用且勾选自动启动"的连接发起建连（<see cref="StartAll"/> 的执行体）。</para>
        /// <para>非阻塞：只向工作线程登记"维持连接"意图，真正的 socket 建连由连接专属线程执行，
        /// 状态变化经 <see cref="ConnectionStateChanged"/> 广播。
        /// 旧实现在这里逐条同步等待 <c>Config.TimeoutMs</c>，现场设备离线时启动会被串行冻结 3N 秒
        /// （且发生在 Splash 显示之前，用户只看到"点了没反应"）。</para>
        /// </summary>
        public void ConnectAll()
        {
            var enabledConfigs = new List<CommunicationConfig>();
            lock (_connectionsListLock)
            {
                enabledConfigs = _connectionsList.Where(c => c.IsEnabled && c.AutoStart).ToList();
            }

            if (enabledConfigs.Count > 0)
                LogInfo($"{enabledConfigs.Count} 条连接标记为自动启动，正在后台建立连接");

            foreach (var config in enabledConfigs)
            {
                try
                {
                    RequestConnect(config.ConnectionName);
                }
                catch (Exception ex)
                {
                    LogError($"启动连接失败: {config.ConnectionName}", ex);
                }
            }
        }

        public void DisconnectAll()
        {
            foreach (var connectionName in _connections.Keys.ToList())
            {
                try
                {
                    Disconnect(connectionName);
                }
                catch (Exception ex)
                {
                    LogError($"停止连接失败: {connectionName}", ex);
                }
            }
        }

        /// <summary>
        /// 测试连接（同步版）：仅供**非 UI 线程**调用（如 CommunicationCheck 在 Task.Run 内）。
        /// 唯一实现在 <see cref="TestConnectionAsync"/>，这里只是硬等它的结果——
        /// 在 UI 线程上调用会冻住界面一个 TimeoutMs（默认 3 秒），UI 一律走异步版。
        /// </summary>
        public bool TestConnection(string connectionName)
            => TestConnectionAsync(connectionName).GetAwaiter().GetResult();

        /// <summary>
        /// 测试连接（异步版，只探测、不改变连接状态）。
        /// 已连上的连接直接判定成功：TestConnection 的实现是"连一次再关掉"，
        /// 对活连接调用会把正在用的 socket 关掉（而 Worker 状态仍是 Connected），属于自毁行为。
        /// </summary>
        public async Task<bool> TestConnectionAsync(string connectionName)
        {
            if (!_workers.TryGetValue(connectionName, out var worker))
                return false;

            if (worker.IsConnected)
            {
                LogDebug($"连接已处于连接状态，跳过测试: {connectionName}");
                return true;
            }

            try
            {
                LogInfo($"正在测试连接: {connectionName}");
                // 走 Worker 线程执行，避免与状态机的建连动作并发操作同一个设备对象。
                // ConfigureAwait(false)：探测耗时全在 Worker 线程上，不应把 UI 线程拽回来等
                bool result = await worker.Invoke(c => c.TestConnection()).ConfigureAwait(false);
                LogInfo($"连接测试结果: {connectionName} = {(result ? "成功" : "失败")}");
                return result;
            }
            catch (Exception ex)
            {
                LogError($"连接测试异常: {connectionName}", ex);
                return false;
            }
        }

        #endregion

        #region 数据读写

        public T Read<T>(string connectionName, string address) where T : struct
        {
            if (string.IsNullOrWhiteSpace(connectionName))
                throw new ArgumentNullException(nameof(connectionName));
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentNullException(nameof(address));
            if (!_connections.ContainsKey(connectionName))
                throw new InvalidOperationException($"连接不存在: {connectionName}");
            if (!_workers.TryGetValue(connectionName, out var worker))
                throw new InvalidOperationException($"连接不存在: {connectionName}");
            if (!worker.IsConnected)
                throw new InvalidOperationException($"连接未建立: {connectionName}");

            try
            {
                // 投递到连接专属线程执行：与轮询、写命令天然串行，不再出现两个线程同时操作一个 socket
                var value = worker.Invoke(c => c.Read<T>(address)).GetAwaiter().GetResult();
                LogDebug($"读取成功: {connectionName}.{address} = {value}");
                DataReceived?.Invoke(this, new CommunicationDataEventArgs(connectionName, address, value));
                return value;
            }
            catch (Exception ex)
            {
                LogError($"读取失败: {connectionName}.{address}", ex);
                OnConnectionError(connectionName, ex);
                throw;
            }
        }

        public void Write(string connectionName, string address, object value)
        {
            if (string.IsNullOrWhiteSpace(connectionName))
                throw new ArgumentNullException(nameof(connectionName));
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentNullException(nameof(address));
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            if (!_connections.ContainsKey(connectionName))
                throw new InvalidOperationException($"连接不存在: {connectionName}");
            if (!_workers.TryGetValue(connectionName, out var worker))
                throw new InvalidOperationException($"连接不存在: {connectionName}");
            if (!worker.IsConnected)
                throw new InvalidOperationException($"连接未建立: {connectionName}");

            try
            {
                // N1：写走"写队列"——优先于轮询与读命令执行，UI 点写值不再排在整轮轮询之后
                worker.InvokeWrite(c => { c.Write(address, value); return true; }).GetAwaiter().GetResult();
                LogDebug($"写入成功: {connectionName}.{address} = {value}");
            }
            catch (Exception ex)
            {
                LogError($"写入失败: {connectionName}.{address}", ex);
                OnConnectionError(connectionName, ex);
                throw;
            }
        }

        public void WriteVariable(string connectionName, string address, object value)
        {
            Write(connectionName, address, value);
        }

        public T ReadVariable<T>(string connectionName, string address) where T : struct
        {
            return Read<T>(connectionName, address);
        }

        public void TriggerWrite(string connectionName, string address, object value, Type valueType)
        {
            if (string.IsNullOrWhiteSpace(connectionName))
                throw new ArgumentNullException(nameof(connectionName));
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentNullException(nameof(address));
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            if (!_workers.TryGetValue(connectionName, out var worker))
                throw new InvalidOperationException($"连接不存在: {connectionName}");

            // B7 真异步：立即返回，写命令排入 Worker 队列按序执行（旧实现直接调同步 Write，调用方照样被网络阻塞）。
            // 调用方没有等待点，故失败只能记录 + 上报，不能回抛。
            _ = worker.EnqueueWrite(address, value).ContinueWith(t =>
            {
                if (!t.IsFaulted) return;

                var ex = t.Exception?.GetBaseException();
                LogError($"异步写入失败: {connectionName}.{address}", ex);
                OnConnectionError(connectionName, ex ?? new InvalidOperationException("异步写入失败"));
            }, TaskScheduler.Default);
        }

        #endregion

        #region 变量管理（完美适配你的 CommunicationVariable）

        public void RegisterVariable(CommunicationVariable variable)
        {
            if (variable == null) throw new ArgumentNullException(nameof(variable));
            if (string.IsNullOrWhiteSpace(variable.ConnectionName))
                throw new ArgumentException("连接名称不能为空", nameof(variable));
            if (string.IsNullOrWhiteSpace(variable.VariableName))
                throw new ArgumentException("变量名称不能为空", nameof(variable));
            if (string.IsNullOrWhiteSpace(variable.Address))
                throw new ArgumentException("地址不能为空", nameof(variable));
            if (string.IsNullOrWhiteSpace(variable.ValueType))
                throw new ArgumentException("值类型不能为空", nameof(variable));

            // H2：注册日志已删除——几万点批量注册（加载方案/RebindAll）会刷爆 UI 日志窗口，
            // 注册成功的可观测性由 RebuildPollPlan 的"轮询计划已更新"汇总 + UI 变量列表本身承担

            var variables = _registeredVariables.GetOrAdd(
                variable.ConnectionName,
                _ => new ConcurrentDictionary<string, CommunicationVariable>());

            // D1：覆盖注册先退订旧转发——旧实现每次注册都往对象挂匿名 lambda，
            // 同名变量重复注册（RebindAll 等路径）会让 OnVarChanged 一次变化转发多遍、旧订阅无法回收。
            // 无论新旧是否同一实例，登记表里有记录就必须摘除，再挂新的
            var forwardKey = variable.ConnectionName + "\\" + variable.VariableName;
            if (variables.TryGetValue(variable.VariableName, out var oldVar)
                && _varForwardHandlers.TryRemove(forwardKey, out var oldHandler))
            {
                oldVar.ValueChanged -= oldHandler;
            }

            EventHandler<object?> handler = (sender, newValue) =>
            {
                // OnVarChanged 当前零订阅者（业务侧全走变量级 ValueChanged 按点订阅）。
                // 必须先判空再构造参数：?.Invoke 不会阻止 new 先执行，
                // 几万点 @1s = 每秒几万个事件参数对象白白进 GC。
                var listeners = OnVarChanged;
                if (listeners == null) return;

                listeners.Invoke(this, new VariableChangedEventArgs(
                    variable.ConnectionName,
                    variable.VariableName,
                    null,
                    newValue));
            };
            variable.ValueChanged += handler;
            _varForwardHandlers[forwardKey] = handler;

            variables[variable.VariableName] = variable;

            // H1：轮询计划标脏（O(1)），不在此重编译——在线时 Worker 下一拍前重建一次，
            // 离线时由 Connect / 建连成功统一编译；批量注册 N 个变量从 O(N²) 降为 O(N)
            RequestPollPlanRebuild(variable.ConnectionName);
        }

        /// <summary>D1：变量转发处理器登记表（key = 连接名\变量名），覆盖注册/注销时用于精确退订</summary>
        private readonly ConcurrentDictionary<string, EventHandler<object?>> _varForwardHandlers = new();

        public void UnregisterVariable(string connectionName, string variableName)
        {
            if (string.IsNullOrWhiteSpace(connectionName) || string.IsNullOrWhiteSpace(variableName))
                return;

            // H2：注销日志已删除（与注册日志同理由）

            if (_registeredVariables.TryGetValue(connectionName, out var variables))
            {
                if (variables.TryRemove(variableName, out var removed)
                    && _varForwardHandlers.TryRemove(connectionName + "\\" + variableName, out var handler))
                {
                    removed.ValueChanged -= handler; // 转发链随变量生命周期一并解除，防事件泄漏
                }

                if (variables.IsEmpty)
                    _registeredVariables.TryRemove(connectionName, out _);

                // H1：标脏即可——在线时 Worker 下一拍前重编译（变量清空时调度器置空，轮询停止最多延后一个周期）
                RequestPollPlanRebuild(connectionName);
            }
        }

        #endregion

        #region 变量轮询（核心功能）

        /// <summary>把连接配置里的运行参数灌进工作线程（重连节奏、是否保持自动重连）</summary>
        private void ConfigureWorker(ConnectionWorker worker, CommunicationConfig config)
        {
            // 注：轮询周期不再灌进 Worker——周期是"扫描组"的属性，由 RebuildPollPlan 编译成 PollScheduler 后整体挂上。
            // 连接级 ReadCycleMs 的唯一去向是"默认组的周期"（见 PollScheduler/ScanGroupTable.Resolve）
            int baseInterval = config.Config?.RetryIntervalMs > 0
                ? config.Config.RetryIntervalMs
                : GlobalReconnectIntervalMs;
            worker.ReconnectBaseIntervalMs = baseInterval;
            worker.MaxReconnectAttempts = MaxReconnectAttempts;

            // 连接级"自动重连"开关 + 管理器总开关：关掉后失败即停在 Error 终态，等外部重新 Connect
            worker.AutoReconnect = AutoReconnectEnabled && config.AutoReconnect;

            // H1：拍前重编译回调——注册/注销变量只标脏（O(1)），Worker 在下一轮轮询拍前回调这里重编译。
            // 重建后脏标记由 RebuildPollPlan 清除；回调异常时脏标记保留，Worker 下一拍自动重试
            worker.PollPlanDirtyHandler = () => RebuildPollPlan(config.ConnectionName);
        }

        /// <summary>
        /// <para>请求更新轮询计划（H1 改造：只标脏，不重建）。</para>
        /// <para>旧实现在线时立即全量重编译——批量注册 N 个变量 = N 次 O(N) 编译 = O(N²)，几万点加载方案分钟级卡死。</para>
        /// <para>现在统一收敛到三个重建落点：<see cref="Connect"/> 建连前、OnWorkerStateChanged(建连成功)、
        /// Worker 轮询拍前消费脏标记（在线场景最迟一个轮询周期后生效）。</para>
        /// </summary>
        private void RequestPollPlanRebuild(string connectionName)
        {
            if (_workers.TryGetValue(connectionName, out var worker))
                worker.MarkPollPlanDirty();
        }

        /// <summary>
        /// <para>把"已注册变量清单 + 连接扫描组表"编译成多周期调度器并整体挂到工作线程的 <see cref="ConnectionWorker.PollScheduler"/>。</para>
        /// <para>编译在注册/连接时发生，轮询热路径上只有"段读 + 内存切片解码"，没有字符串解析也没有反射。</para>
        /// <para>组表来自 <see cref="_configCache"/> 里的活配置对象（改配置即改活对象），故"编辑扫描组"只需标脏重编译。</para>
        /// </summary>
        private void RebuildPollPlan(string connectionName)
        {
            if (!_workers.TryGetValue(connectionName, out var worker))
                return;

            _configCache.TryGetValue(connectionName, out var config); // 取不到则全部走默认组（安全兜底）

            if (!_registeredVariables.TryGetValue(connectionName, out var variables) || variables.IsEmpty)
            {
                worker.PollScheduler = null; // 无变量 → 不空转（连接活性改由读写命令刷新）
                worker.ClearPollPlanDirty();
                LogDebug($"连接 {connectionName} 无已注册变量，已停止轮询");
                return;
            }

            var scheduler = PollScheduler.Create(variables.Values, config, LogWarning);
            worker.PollScheduler = scheduler; // 引用赋值原子：Worker 下一拍自然用新调度器
            worker.ClearPollPlanDirty(); // 重建完成 → 计划已反映最新注册表；放这里保证任何重建落点之后无残留脏标记

            LogInfo(scheduler == null
                ? $"轮询计划已更新: {connectionName} 变量 {variables.Count} 个，但无可轮询项，已停止轮询"
                : $"轮询计划已更新: {connectionName} 变量 {variables.Count} 个 → {scheduler.GroupCount} 个扫描组（{scheduler.Describe()}）");
        }

        /// <summary>
        /// 取某连接各扫描组的运行诊断快照（目标周期 / 实测周期 / 达成率 / 段数）。
        /// <para>只读快照，可由 UI 线程定时调用；连接不存在或无调度器时返回空表。</para>
        /// </summary>
        public IReadOnlyList<ScanGroupStats> GetScanGroupStats(string connectionName)
        {
            if (string.IsNullOrWhiteSpace(connectionName)
                || !_workers.TryGetValue(connectionName, out var worker)
                || worker.PollScheduler is not { } scheduler)
            {
                return Array.Empty<ScanGroupStats>();
            }
            return scheduler.GetStats();
        }

        /// <summary>取某连接可用的扫描组名（含默认组，恒在首位），供变量编辑器的"扫描组"下拉框使用</summary>
        public IReadOnlyList<string> GetScanGroupNames(string connectionName)
        {
            if (string.IsNullOrWhiteSpace(connectionName))
                return new[] { PollScheduler.DefaultGroupName };

            _configCache.TryGetValue(connectionName, out var config);
            return PollScheduler.ResolveGroupNames(config);
        }

        /// <summary>
        /// 取某连接各扫描组的"静态画像"（变量数 / 段数 / 单读数），**不依赖连接是否在线**。
        /// <para>做法：用组表 + 已注册变量临时编译一次调度器，取它的快照（实测周期恒为 0 = 还没测到）。
        /// 复用 <see cref="PollScheduler.Create"/> 而非另写一份统计，保证"编辑器里看到的段数"与"真正轮询时的段数"是同一个口径。</para>
        /// <para>代价：O(N) 编译（几万点约几十毫秒）。调用点限于"打开扫描组编辑器"与"编辑器里改动组表结构"，
        /// 不进轮询热路径。</para>
        /// </summary>
        /// <param name="configOverride">
        /// 传入则用它当组表，而不是连接当前活配置。
        /// 扫描组编辑器编辑的是**副本**（取消不能弄脏活对象），所以它必须能把副本传进来预览，
        /// 否则"刚加了一个组"在预览里永远看不见，直到点确定。
        /// </param>
        public IReadOnlyList<ScanGroupStats> GetScanGroupPreview(
            string connectionName,
            CommunicationConfig? configOverride = null)
        {
            if (string.IsNullOrWhiteSpace(connectionName))
                return Array.Empty<ScanGroupStats>();

            if (configOverride == null)
                _configCache.TryGetValue(connectionName, out configOverride);

            _registeredVariables.TryGetValue(connectionName, out var variables);

            return PollScheduler.Create(variables?.Values, configOverride, null)?.GetStats()
                   ?? Array.Empty<ScanGroupStats>();
        }

        /// <summary>
        /// 扫描组改名级联 / 删组回落：把某连接下 <see cref="CommunicationVariable.ScanGroup"/> 指向
        /// <paramref name="oldName"/> 的**已注册变量**改指到 <paramref name="newName"/>（传 null/空 = 回落默认组），
        /// 并标脏轮询计划。
        /// <para>为什么必须由 Manager 提供：已注册变量表是 Manager 私有状态，且它是"变量归哪个组"在轮询侧的唯一依据。</para>
        /// <para>为什么必须在 <see cref="UpdateConnection"/> <b>之前</b>调用：UpdateConnection 走
        /// Remove → Add → <see cref="RestoreVariables"/>，而 RestoreVariables 复用的正是<b>同一批对象</b>
        /// （<c>keepVariables</c> 是引用拷贝）。先改这里，改的就是"重建后真正参与轮询的那一份"。</para>
        /// <para>注意：调用方还需同步改工作区里的变量模型（<c>NetworkVariableModel.ScanGroup</c>），
        /// 否则下次 RebindAll 会把旧组名又灌回来。两处都改才算完整级联。</para>
        /// </summary>
        /// <returns>受影响的已注册变量数</returns>
        public int ReassignScanGroup(string connectionName, string oldName, string? newName)
        {
            if (string.IsNullOrWhiteSpace(connectionName) || string.IsNullOrWhiteSpace(oldName))
                return 0;

            if (!_registeredVariables.TryGetValue(connectionName, out var variables))
                return 0;

            string target = string.IsNullOrWhiteSpace(newName) ? string.Empty : newName.Trim();
            int affected = 0;

            foreach (var variable in variables.Values)
            {
                if (!string.Equals(variable.ScanGroup?.Trim(), oldName, StringComparison.Ordinal))
                    continue;

                variable.ScanGroup = target;
                affected++;
            }

            if (affected > 0)
                RequestPollPlanRebuild(connectionName); // 在线时 Worker 下一拍前重编译；离线时等 Connect 统一编译

            return affected;
        }

        #endregion

        #region 配置管理

        /// <summary>通信配置文件序列化设置（TypeNameHandling.Auto 支持抽象 Config 多态，$type 白名单防恶意文件）</summary>
        private static readonly Newtonsoft.Json.JsonSerializerSettings _configJsonSettings = new()
        {
            Formatting = Newtonsoft.Json.Formatting.Indented,
            NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
            TypeNameHandling = Newtonsoft.Json.TypeNameHandling.Auto,
            SerializationBinder = new VisionMaster.Communications.ConnectionConfigSerializationBinder()
        };

        public async Task SaveConfigAsync()
        {
            try
            {
                LogInfo($"正在保存配置到: {ConfigFilePath}");

                List<CommunicationConfig> configs;

                lock (_connectionsListLock)
                {
                    configs = _connectionsList.ToList();
                }

                var json = Newtonsoft.Json.JsonConvert.SerializeObject(configs, _configJsonSettings);
                await File.WriteAllTextAsync(ConfigFilePath, json);

                LogInfo($"配置保存成功: {ConfigFilePath}");
            }
            catch (Exception ex)
            {
                LogError("保存配置失败", ex);
                throw;
            }
        }

        /// <summary>
        /// <para>同步加载连接配置（启动链路专用入口）。</para>
        /// <para>启动期的加载发生在 UI 线程**且必须早于自检与界面构造**：通信自检要按配置逐条测连通，
        /// 配置没加载完它只能看到空列表（旧实现在 ShellViewModel 构造函数里加载，自检永远报"无通讯配置"）。
        /// 此处只有一次几 KB 的本地文件读取，同步完成可避免 UI 线程 await 造成的时序不确定。</para>
        /// </summary>
        public void LoadConfig()
        {
            if (!File.Exists(ConfigFilePath))
            {
                LogInfo($"配置文件不存在: {ConfigFilePath}");
                return;
            }

            try
            {
                LogInfo($"正在加载配置: {ConfigFilePath}");
                ApplyConfigJson(File.ReadAllText(ConfigFilePath));
            }
            catch (Exception ex)
            {
                LogError("加载配置失败", ex);
                throw;
            }
        }

        /// <summary>异步加载连接配置：文件读写挪到线程池，供非启动期的重载入口使用</summary>
        public async Task LoadConfigAsync()
        {
            await Task.Run(LoadConfig).ConfigureAwait(false);
        }

        /// <summary>反序列化并逐条登记连接（单条失败只记日志，不影响其余连接）</summary>
        private void ApplyConfigJson(string json)
        {
            var configs = Newtonsoft.Json.JsonConvert.DeserializeObject<List<CommunicationConfig>>(json, _configJsonSettings);
            if (configs == null)
            {
                LogInfo("配置加载完成: 文件内容为空");
                return;
            }

            foreach (var config in configs)
            {
                try
                {
                    AddConnection(config);
                }
                catch (Exception ex)
                {
                    LogError($"加载连接配置失败: {config.ConnectionName}", ex);
                }
            }

            LogInfo($"配置加载完成: 共加载 {configs.Count} 个连接");
        }

        public async Task ExportConfigAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));

            try
            {
                LogInfo($"正在导出配置到: {filePath}");

                var options = _configJsonSettings;
                List<CommunicationConfig> configs;

                lock (_connectionsListLock)
                {
                    configs = _connectionsList.ToList();
                }

                var json = Newtonsoft.Json.JsonConvert.SerializeObject(configs, options);
                await File.WriteAllTextAsync(filePath, json);

                LogInfo($"配置导出成功: {filePath}");
            }
            catch (Exception ex)
            {
                LogError("导出配置失败", ex);
                throw;
            }
        }

        public async Task ImportConfigAsync(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("配置文件不存在", filePath);

            try
            {
                LogInfo($"正在导入配置: {filePath}");

                var json = await File.ReadAllTextAsync(filePath);
                var configs = Newtonsoft.Json.JsonConvert.DeserializeObject<List<CommunicationConfig>>(json, _configJsonSettings);

                if (configs != null)
                {
                    foreach (var config in configs)
                    {
                        try
                        {
                            AddConnection(config);
                        }
                        catch (Exception ex)
                        {
                            LogError($"导入连接配置失败: {config.ConnectionName}", ex);
                        }
                    }
                }

                LogInfo($"配置导入完成: 共导入 {configs?.Count ?? 0} 个连接");
            }
            catch (Exception ex)
            {
                LogError("导入配置失败", ex);
                throw;
            }
        }

        #endregion

        #region 工作线程事件（状态同步 + 故障上报）

        /// <summary>
        /// Worker 状态机是连接状态的唯一真相源：状态一变就同步到配置对象（UI 徽标绑定它）并对外广播。
        /// 旧实现由"谁发起的操作谁改状态"拼凑而成，重连线程改的状态没人通知 UI。
        /// </summary>
        private void OnWorkerStateChanged(string connectionName, ConnectionState oldState, ConnectionState newState)
        {
            if (_configCache.TryGetValue(connectionName, out var config))
            {
                // 【必须回 UI 线程赋值】本方法由连接专属线程回调，而 config 是**直接绑定到界面的模型**
                // （通讯设置对话框的状态徽标等）。赋值会触发 INotifyPropertyChanged，
                // 若在后台线程发出，凡是"直接订阅 PropertyChanged"的控件（如 FlatPropertyGrid）
                // 就会在后台线程读到自己的 DependencyProperty → 抛
                // "调用线程无法访问此对象，因为另一个线程拥有该对象"。
                // 这里是整条链路的源头，改在源头赋值最彻底；控件层的线程兜底只是第二道保险。
                VisionMaster.Helpers.SafeDispatch.BeginInvoke(() =>
                {
                    if (newState == ConnectionState.Connected)
                    {
                        config.UpdateLastConnectedTime();
                        // 连上即故障已过去：清掉最后错误，状态胶囊上的 ⚠ 随之消失。
                        // 不清的后果是"曾经出过错"永久留在界面上，用户无从判断当前是否正常
                        config.ClearLastError();
                    }

                    config.State = newState;
                });
            }

            // 连上即重编译轮询计划：变量可能在"已登记连接意图但尚未连上"期间注册
            // （RegisterVariable 只在 IsConnected 时立即重编译，否则推迟到"建连时"），
            // 这里是"建连时"的唯一可靠落点，否则那批变量会静默不参与轮询。
            // 纯数据结构操作（不动 UI），保持在 Worker 线程同步执行，避免改变建连时序
            if (newState == ConnectionState.Connected)
                RebuildPollPlan(connectionName);

            // 断线/重连中/错误终态：该连接所有变量质量戳打为 Bad（UI 灰点，值不再可信）。
            // 与"连上重编译"对称：离开 Connected 就标记。已是 Bad 的在 SetQuality 内被幂等拦截，
            // 不会重复发通知；重连成功后首轮轮询自动把读到的变量翻回 Good
            if (newState != ConnectionState.Connected && _registeredVariables.TryGetValue(connectionName, out var vars))
            {
                foreach (var variable in vars.Values)
                    variable.MarkBad();
            }

            OnConnectionStateChanged(connectionName, oldState, newState);
        }

        /// <summary>Worker 侧通信故障（建连失败/轮询失败）：限流上报，避免退避重连期间刷屏</summary>
        private void OnWorkerCommunicationError(string connectionName, Exception? ex)
        {
            if (IsThrottled($"commerr:{connectionName}"))
                return;

            string message = ex?.Message ?? "未知错误";

            // 把"最后错误"落到配置对象上，供连接管理的状态胶囊展示（ToolTip + ⚠）。
            // 与 OnWorkerStateChanged 里改 State 同样的理由：本方法跑在连接专属线程，
            // 而 config 是直接绑定界面的模型，赋值会发 PropertyChanged，
            // 后台线程发通知会让直接订阅 PropertyChanged 的控件读到自己的 DependencyProperty 而崩。
            // 放在节流之后：退避重连期间每 5 秒才写一次，不会造成通知风暴。
            if (_configCache.TryGetValue(connectionName, out var cfg))
                VisionMaster.Helpers.SafeDispatch.BeginInvoke(() => cfg.LastError = message);

            LogWarning($"通信故障: {connectionName} — {message}");
            OnConnectionError(connectionName, ex ?? new InvalidOperationException("未知通信故障"));
        }

        /// <summary>同一 key 在 <see cref="ThrottleMs"/> 窗口内的重复调用返回 true（应被抑制）</summary>
        private bool IsThrottled(string key)
        {
            long now = Environment.TickCount64;
            if (_throttleTicks.TryGetValue(key, out long last) && now - last < ThrottleMs)
                return true;

            _throttleTicks[key] = now;
            return false;
        }

        #endregion

        #region 事件触发

        private void OnConnectionStateChanged(string name, ConnectionState oldState, ConnectionState newState)
        {
            LogInfo($"连接状态变化: {name} {oldState} -> {newState}");
            ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(name, oldState, newState));
        }

        private void OnConnectionError(string name, Exception ex)
        {
            ConnectionError?.Invoke(this, new ConnectionErrorEventArgs(name, ex));
            OnCommError?.Invoke(this, new CommunicationErrorEventArgs(name, ex?.Message ?? "未知错误"));
        }

        #endregion

        #region 日志方法

        // ===== 日志（双通道：Console 保留给调试器；LogSink 由 App 启动时挂接 ILogService）=====
        // 旧实现只有 Console.WriteLine——WPF 应用没有控制台，"变量注册/轮询启动/读取失败"
        // 全部诊断黑洞（网络变量当前值不刷新时无从排查的根因），必须接入 UI 日志窗口
        public static global::Core.Interfaces.ILogService? LogSink { get; set; }

        private void LogInfo(string message)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [INFO] {message}");
            LogSink?.Info($"[Comm] {message}");
        }

        private void LogDebug(string message)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [DEBUG] {message}");
        }

        private void LogWarning(string message)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [WARNING] {message}");
            LogSink?.Warn($"[Comm] {message}");
        }

        private void LogError(string message, Exception? ex =null)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [ERROR] {message}");
            LogSink?.Error($"[Comm] {message}{(ex != null ? " | " + ex.Message : "")}");
            if (ex != null)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [ERROR] 异常详情: {ex}");
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            lock (_disposeLock)
            {
                if (_disposed) return;
                _disposed = true;
            }

            LogInfo("正在释放 AdvancedCommunicationManager 资源...");

            // 停止所有连接（Worker.Disconnect 会同时停止自动重连并把状态置为 Disconnected）
            StopAll();

            // 释放所有连接工作线程：Dispose 内部会 Join 线程、断连并释放连接对象。
            // 旧实现这里释放的是"重连/心跳/变量轮询"三套 Timer，它们已随 Worker 状态机整体移除。
            foreach (var worker in _workers.Values)
            {
                try
                {
                    worker.Dispose();
                }
                catch (Exception ex)
                {
                    LogError("释放连接工作线程时发生错误", ex);
                }
            }
            _workers.Clear();

            // 连接对象已随 Worker 一并释放，这里只清理引用
            _connections.Clear();

            // 清空集合
            _configCache.Clear();
            _registeredVariables.Clear();
            _varForwardHandlers.Clear();
            _throttleTicks.Clear();

            lock (_connectionsListLock)
            {
                _connectionsList.Clear();
            }

            LogInfo("AdvancedCommunicationManager 资源释放完成");
        }

        #endregion
    }

    #region 事件参数类

    public class ConnectionStateChangedEventArgs : EventArgs
    {
        public string ConnectionName { get; }
        public ConnectionState OldState { get; }
        public ConnectionState NewState { get; }

        public ConnectionStateChangedEventArgs(string name, ConnectionState oldState, ConnectionState newState)
        {
            ConnectionName = name;
            OldState = oldState;
            NewState = newState;
        }
    }




    #endregion


}