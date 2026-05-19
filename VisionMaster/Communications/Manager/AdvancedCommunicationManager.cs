using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VisionMaster.Communications
{
    public class AdvancedCommunicationManager : ICommunicationManager, IDisposable
    {
        #region 私有字段

        // ✅ 全部改为线程安全集合
        private readonly ConcurrentDictionary<string, ICommunicationConnection> _connections = new();
        private readonly ConcurrentDictionary<string, CommunicationConfig> _configCache = new();
        private readonly ConcurrentDictionary<string, Timer> _reconnectTimers = new();
        private readonly ConcurrentDictionary<string, Timer> _heartbeatTimers = new();
        private readonly ConcurrentDictionary<string, Timer> _variablePollingTimers = new();

        // ✅ 完美适配你的 CommunicationVariable 类
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, CommunicationVariable>> _registeredVariables = new();

        // ✅ ObservableCollection 专用锁（防止非UI线程操作崩溃）
        private readonly object _connectionsListLock = new();
        private readonly ObservableCollection<CommunicationConfig> _connectionsList = new();

        // ✅ 读取方法缓存（避免重复反射）
        private static readonly ConcurrentDictionary<Type, MethodInfo> _readMethodCache = new();

        // ✅ 恢复使用你的 ConnectionFactoryManager
        private readonly ConnectionFactoryManager _factoryManager = ConnectionFactoryManager.Instance;

        private readonly object _disposeLock = new();
        private bool _disposed = false;

        #endregion

        #region 公共属性

        public ObservableCollection<CommunicationConfig> ConnectionsList => _connectionsList;
        public int ConnectionCount => _connections.Count;
        public int ConnectedCount => _connections.Count(c => c.Value.IsConnected);
        public bool IsRunning { get; private set; } = false;
        public string ConfigFilePath { get; set; } = "communications.json";
        public bool AutoReconnectEnabled { get; set; } = true;
        public int GlobalReconnectIntervalMs { get; set; } = 5000;
        public int HeartbeatIntervalMs { get; set; } = 30000;
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

            try
            {
                LogInfo($"正在添加连接: {config.ConnectionName} ({config.Protocol})");

                // ✅ 使用你的 ConnectionFactoryManager 创建连接
                var connection = _factoryManager.CreateConnection(config);
                _connections[config.ConnectionName] = connection;
                _configCache[config.ConnectionName] = config;

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
                LogError($"添加连接失败: {config.ConnectionName}", ex);
                OnConnectionError(config.ConnectionName, ex);
                throw;
            }
        }

        public bool RemoveConnection(string connectionName)
        {
            if (string.IsNullOrWhiteSpace(connectionName))
                return false;

            LogInfo($"正在移除连接: {connectionName}");

            // 停止所有定时器
            StopReconnectTimer(connectionName);
            StopHeartbeatTimer(connectionName);
            StopVariablePollingTimer(connectionName);

            // 断开并释放连接
            if (_connections.TryRemove(connectionName, out var connection))
            {
                try
                {
                    connection.Disconnect();
                    if (connection is IDisposable disposable)
                        disposable.Dispose();

                    LogInfo($"连接已断开并释放: {connectionName}");
                }
                catch (Exception ex)
                {
                    LogError($"断开连接时发生错误: {connectionName}", ex);
                }
            }

            // 移除配置缓存
            _configCache.TryRemove(connectionName, out _);

            // 移除注册的变量
            _registeredVariables.TryRemove(connectionName, out _);

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

            RemoveConnection(config.ConnectionName);
            return AddConnection(config);
        }

        public bool Connect(string connectionName)
        {
            if (string.IsNullOrWhiteSpace(connectionName))
                throw new ArgumentNullException(nameof(connectionName));
            if (!_connections.TryGetValue(connectionName, out var connection))
                throw new InvalidOperationException($"连接不存在: {connectionName}");
            if (!_configCache.TryGetValue(connectionName, out var config))
                return false;

            try
            {
                if (connection.IsConnected)
                    return true;

                LogInfo($"正在连接: {connectionName}");
                config.State = ConnectionState.Connecting;
                OnConnectionStateChanged(connectionName, ConnectionState.Disconnected, ConnectionState.Connecting);

                var result = connection.Connect();

                if (result)
                {
                    config.State = ConnectionState.Connected;
                    config.UpdateLastConnectedTime();
                    config.ReadCycleMs = 0; // 重置重连计数
                    OnConnectionStateChanged(connectionName, ConnectionState.Connecting, ConnectionState.Connected);

                    LogInfo($"连接成功: {connectionName}");

                    if (AutoReconnectEnabled && config.AutoReconnect)
                        StartHeartbeatTimer(connectionName);

                    // ✅ 连接成功后自动启动变量轮询（使用连接配置的ReadCycleMs）
                    StartVariablePollingTimer(connectionName, config.ReadCycleMs);
                }
                else
                {
                    config.State = ConnectionState.Error;
                    OnConnectionStateChanged(connectionName, ConnectionState.Connecting, ConnectionState.Error);

                    LogError($"连接失败: {connectionName}", null);

                    if (AutoReconnectEnabled && config.AutoReconnect)
                        StartReconnectTimer(connectionName, config.Config.RetryIntervalMs);
                }

                return result;
            }
            catch (Exception ex)
            {
                config.State = ConnectionState.Error;
                OnConnectionStateChanged(connectionName, ConnectionState.Connecting, ConnectionState.Error);
                OnConnectionError(connectionName, ex);

                LogError($"连接异常: {connectionName}", ex);

                if (AutoReconnectEnabled && config.AutoReconnect)
                    StartReconnectTimer(connectionName, config.Config.RetryIntervalMs);

                return false;
            }
        }

        public void Disconnect(string connectionName)
        {
            if (string.IsNullOrWhiteSpace(connectionName))
                return;
            if (!_connections.TryGetValue(connectionName, out var connection))
                return;
            if (!_configCache.TryGetValue(connectionName, out var config))
                return;

            LogInfo($"正在断开连接: {connectionName}");

            // 停止所有定时器
            StopReconnectTimer(connectionName);
            StopHeartbeatTimer(connectionName);
            StopVariablePollingTimer(connectionName);

            try
            {
                connection.Disconnect();
                config.State = ConnectionState.Disconnected;
                OnConnectionStateChanged(connectionName, ConnectionState.Connected, ConnectionState.Disconnected);

                LogInfo($"连接已断开: {connectionName}");
            }
            catch (Exception ex)
            {
                LogError($"断开连接异常: {connectionName}", ex);
                OnConnectionError(connectionName, ex);
            }
        }

        public void ConnectAll()
        {
            var enabledConfigs = new List<CommunicationConfig>();
            lock (_connectionsListLock)
            {
                enabledConfigs = _connectionsList.Where(c => c.IsEnabled && c.AutoStart).ToList();
            }

            foreach (var config in enabledConfigs)
            {
                try
                {
                    Connect(config.ConnectionName);
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

        public bool TestConnection(string connectionName)
        {
            if (!_connections.TryGetValue(connectionName, out var connection))
                return false;

            try
            {
                LogInfo($"正在测试连接: {connectionName}");
                bool result = connection.TestConnection();
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

        public T? Read<T>(string connectionName, string address)
        {
            if (string.IsNullOrWhiteSpace(connectionName))
                throw new ArgumentNullException(nameof(connectionName));
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentNullException(nameof(address));
            if (!_connections.TryGetValue(connectionName, out var connection))
                throw new InvalidOperationException($"连接不存在: {connectionName}");
            if (!connection.IsConnected)
                throw new InvalidOperationException($"连接未建立: {connectionName}");

            try
            {
                var value = connection.Read<T>(address);
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
            if (!_connections.TryGetValue(connectionName, out var connection))
                throw new InvalidOperationException($"连接不存在: {connectionName}");
            if (!connection.IsConnected)
                throw new InvalidOperationException($"连接未建立: {connectionName}");

            try
            {
                connection.Write(address, value);
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

        public T? ReadVariable<T>(string connectionName, string address)
        {
            return Read<T>(connectionName, address);
        }

        public void TriggerWrite(string connectionName, string address, object value, Type valueType)
        {
            Write(connectionName, address, value);
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

            LogInfo($"注册变量: {variable.ConnectionName}.{variable.VariableName} ({variable.Address})");

            var variables = _registeredVariables.GetOrAdd(
                variable.ConnectionName,
                _ => new ConcurrentDictionary<string, CommunicationVariable>());

            variables[variable.VariableName] = variable;

            // 订阅变量自身的ValueChanged事件，转发到全局OnVarChanged事件
            variable.ValueChanged += (sender, newValue) =>
            {
                OnVarChanged?.Invoke(this, new VariableChangedEventArgs(
                    variable.ConnectionName,
                    variable.VariableName,
                    null,
                    newValue));
            };
        }

        public void UnregisterVariable(string connectionName, string variableName)
        {
            if (string.IsNullOrWhiteSpace(connectionName) || string.IsNullOrWhiteSpace(variableName))
                return;

            LogInfo($"注销变量: {connectionName}.{variableName}");

            if (_registeredVariables.TryGetValue(connectionName, out var variables))
            {
                variables.TryRemove(variableName, out _);

                if (variables.IsEmpty)
                    _registeredVariables.TryRemove(connectionName, out _);
            }
        }

        #endregion

        #region 变量轮询（核心功能）

        private void StartVariablePollingTimer(string connectionName, int intervalMs)
        {
            StopVariablePollingTimer(connectionName);

            if (intervalMs <= 0)
            {
                LogWarning($"连接 {connectionName} 的轮询周期无效，跳过变量轮询");
                return;
            }

            LogInfo($"启动变量轮询定时器: {connectionName}, 间隔: {intervalMs}ms");

            var timer = new Timer(_ =>
            {
                try
                {
                    if (_disposed) return;
                    if (!_connections.TryGetValue(connectionName, out var connection) || !connection.IsConnected)
                        return;

                    PollVariables(connectionName, connection);
                }
                catch (Exception ex)
                {
                    LogError($"变量轮询异常: {connectionName}", ex);
                }
            }, null, intervalMs, intervalMs);

            _variablePollingTimers[connectionName] = timer;
        }

        private void StopVariablePollingTimer(string connectionName)
        {
            if (_variablePollingTimers.TryRemove(connectionName, out var timer))
            {
                using (timer)
                {
                    timer.Change(Timeout.Infinite, Timeout.Infinite);
                }
                LogDebug($"变量轮询定时器已停止: {connectionName}");
            }
        }

        private void PollVariables(string connectionName, ICommunicationConnection connection)
        {
            if (!_registeredVariables.TryGetValue(connectionName, out var variables))
                return;

            foreach (var variable in variables.Values)
            {
                try
                {
                    // 跳过只写变量
                    if (variable.AccessMode == VariableAccessMode.WriteOnly)
                        continue;

                    // 将ValueType字符串转换为Type
                    Type valueType = Type.GetType(variable.ValueType);
                    if (valueType == null)
                    {
                        LogError($"变量 {variable.VariableName} 的值类型无效: {variable.ValueType}");
                        continue;
                    }

                    // 反射调用Read<T>方法
                    object? rawValue = ReadValueByType(connection, variable.Address, valueType);

                    // 更新变量值（会自动触发ValueChanged事件）
                    variable.UpdateValue(rawValue);
                }
                catch (Exception ex)
                {
                    LogError($"读取变量失败: {connectionName}.{variable.VariableName}", ex);
                }
            }
        }

        /// <summary>
        /// 根据数据类型调用对应的Read<T>方法
        /// </summary>
        private object? ReadValueByType(ICommunicationConnection connection, string address, Type valueType)
        {
            // 处理可空值类型
            Type underlyingType = Nullable.GetUnderlyingType(valueType) ?? valueType;

            // 从缓存获取读取方法
            if (!_readMethodCache.TryGetValue(underlyingType, out var readMethod))
            {
                // 反射获取Read<T>方法
                readMethod = typeof(ICommunicationConnection)
                    .GetMethod(nameof(ICommunicationConnection.Read))!
                    .MakeGenericMethod(underlyingType);

                // 缓存方法信息
                _readMethodCache[underlyingType] = readMethod;
            }

            // 调用Read<T>方法
            return readMethod.Invoke(connection, new object[] { address });
        }

        #endregion

        #region 配置管理

        public async Task SaveConfigAsync()
        {
            try
            {
                LogInfo($"正在保存配置到: {ConfigFilePath}");

                var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                List<CommunicationConfig> configs;

                lock (_connectionsListLock)
                {
                    configs = _connectionsList.ToList();
                }

                var json = JsonSerializer.Serialize(configs, options);
                await File.WriteAllTextAsync(ConfigFilePath, json);

                LogInfo($"配置保存成功: {ConfigFilePath}");
            }
            catch (Exception ex)
            {
                LogError("保存配置失败", ex);
                throw;
            }
        }

        public async Task LoadConfigAsync()
        {
            if (!File.Exists(ConfigFilePath))
            {
                LogInfo($"配置文件不存在: {ConfigFilePath}");
                return;
            }

            try
            {
                LogInfo($"正在加载配置: {ConfigFilePath}");

                var json = await File.ReadAllTextAsync(ConfigFilePath);
                var configs = JsonSerializer.Deserialize<List<CommunicationConfig>>(json);

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
                            LogError($"加载连接配置失败: {config.ConnectionName}", ex);
                        }
                    }
                }

                LogInfo($"配置加载完成: 共加载 {configs?.Count ?? 0} 个连接");
            }
            catch (Exception ex)
            {
                LogError("加载配置失败", ex);
                throw;
            }
        }

        public async Task ExportConfigAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));

            try
            {
                LogInfo($"正在导出配置到: {filePath}");

                var options = new JsonSerializerOptions { WriteIndented = true };
                List<CommunicationConfig> configs;

                lock (_connectionsListLock)
                {
                    configs = _connectionsList.ToList();
                }

                var json = JsonSerializer.Serialize(configs, options);
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
                var configs = JsonSerializer.Deserialize<List<CommunicationConfig>>(json);

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

        #region 定时器管理

        private void StartReconnectTimer(string name, int intervalMs)
        {
            // 先停止旧的定时器
            StopReconnectTimer(name);

            if (intervalMs <= 0)
                intervalMs = GlobalReconnectIntervalMs;

            LogInfo($"启动重连定时器: {name}, 间隔: {intervalMs}ms");

            var timer = new Timer(_ =>
            {
                try
                {
                    if (_disposed) return;
                    if (!_configCache.TryGetValue(name, out var config)) return;

                    // 检查重连次数限制
                    if (MaxReconnectAttempts > 0 && config.ReadCycleMs >= MaxReconnectAttempts)
                    {
                        LogError($"连接 {name} 达到最大重连次数 {MaxReconnectAttempts}，停止重连");
                        StopReconnectTimer(name);
                        return;
                    }

                    config.ReadCycleMs++;
                    LogInfo($"正在尝试第 {config.ReadCycleMs} 次重连: {name}");

                    var result = Connect(name);
                    if (result)
                    {
                        LogInfo($"重连成功: {name}");
                        StopReconnectTimer(name);
                    }
                }
                catch (Exception ex)
                {
                    LogError($"重连异常: {name}", ex);
                }
            }, null, intervalMs, intervalMs);

            _reconnectTimers[name] = timer;
        }

        private void StopReconnectTimer(string name)
        {
            if (_reconnectTimers.TryRemove(name, out var timer))
            {
                using (timer)
                {
                    timer.Change(Timeout.Infinite, Timeout.Infinite);
                }
                LogDebug($"重连定时器已停止: {name}");
            }
        }

        private void StartHeartbeatTimer(string name)
        {
            StopHeartbeatTimer(name);
            if (HeartbeatIntervalMs <= 0) return;

            LogInfo($"启动心跳定时器: {name}, 间隔: {HeartbeatIntervalMs}ms");

            var timer = new Timer(_ =>
            {
                try
                {
                    if (_disposed) return;

                    if (!_connections.TryGetValue(name, out var c) || !c.IsConnected)
                    {
                        LogWarning($"连接 {name} 心跳检测失败，启动重连");
                        StartReconnectTimer(name, GlobalReconnectIntervalMs);
                        StopHeartbeatTimer(name);
                    }
                    else
                    {
                        LogDebug($"心跳检测正常: {name}");
                    }
                }
                catch (Exception ex)
                {
                    LogError($"心跳检测异常: {name}", ex);
                }
            }, null, HeartbeatIntervalMs, HeartbeatIntervalMs);

            _heartbeatTimers[name] = timer;
        }

        private void StopHeartbeatTimer(string name)
        {
            if (_heartbeatTimers.TryRemove(name, out var timer))
            {
                using (timer)
                {
                    timer.Change(Timeout.Infinite, Timeout.Infinite);
                }
                LogDebug($"心跳定时器已停止: {name}");
            }
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

        private void LogInfo(string message)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [INFO] {message}");
        }

        private void LogDebug(string message)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [DEBUG] {message}");
        }

        private void LogWarning(string message)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [WARNING] {message}");
        }

        private void LogError(string message, Exception? ex =null)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [ERROR] {message}");
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

            // 停止所有轮询
            StopAll();

            // 释放所有定时器
            foreach (var timer in _reconnectTimers.Values)
            {
                using (timer)
                {
                    timer.Change(Timeout.Infinite, Timeout.Infinite);
                }
            }
            _reconnectTimers.Clear();

            foreach (var timer in _heartbeatTimers.Values)
            {
                using (timer)
                {
                    timer.Change(Timeout.Infinite, Timeout.Infinite);
                }
            }
            _heartbeatTimers.Clear();

            foreach (var timer in _variablePollingTimers.Values)
            {
                using (timer)
                {
                    timer.Change(Timeout.Infinite, Timeout.Infinite);
                }
            }
            _variablePollingTimers.Clear();

            // 释放所有连接
            foreach (var connection in _connections.Values)
            {
                try
                {
                    connection.Disconnect();
                    if (connection is IDisposable disposable)
                        disposable.Dispose();
                }
                catch (Exception ex)
                {
                    LogError("释放连接时发生错误", ex);
                }
            }
            _connections.Clear();

            // 清空集合
            _configCache.Clear();
            _registeredVariables.Clear();

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