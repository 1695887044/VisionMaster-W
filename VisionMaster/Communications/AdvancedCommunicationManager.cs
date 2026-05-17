using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VisionMaster.Communications
{
    public class AdvancedCommunicationManager : ICommunicationManager, IDisposable
    {
        private readonly Dictionary<string, ICommunicationConnection> _connections = new();
        private readonly Dictionary<string, Timer> _reconnectTimers = new();
        private readonly Dictionary<string, Timer> _heartbeatTimers = new();
        private readonly ConnectionFactoryManager _factoryManager = ConnectionFactoryManager.Instance;
        private bool _disposed = false;

        public ObservableCollection<CommunicationConfig> ConnectionsList { get; } = new();
        public int ConnectionCount => ConnectionsList.Count;
        public int ConnectedCount => _connections.Count(c => c.Value.IsConnected);
        public bool IsRunning { get; private set; } = false;
        public string ConfigFilePath { get; set; } = "communications.json";
        public bool AutoReconnectEnabled { get; set; } = true;
        public int GlobalReconnectIntervalMs { get; set; } = 5000;
        public int HeartbeatIntervalMs { get; set; } = 30000;

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

        public ICommunicationConnection? GetConnection(string connectionName) =>
            _connections.TryGetValue(connectionName, out var conn) ? conn : null;

        public List<CommunicationConfig> GetAllConnections() => ConnectionsList.ToList();

        public void StartAll() => ConnectAll();

        public void StopAll() => DisconnectAll();

        public void WriteVariable(string connectionName, string address, object value) => Write(connectionName, address, value);

        public T? ReadVariable<T>(string connectionName, string address) => Read<T>(connectionName, address);

        public void RegisterVariable(CommunicationVariable variable) { }

        public void UnregisterVariable(string connectionName, string variableName) { }

        public void TriggerWrite(string connectionName, string address, object value, Type valueType) => Write(connectionName, address, value);

        public bool UpdateConnection(CommunicationConfig config)
        {
            RemoveConnection(config.ConnectionName);
            return AddConnection(config);
        }

        public bool AddConnection(CommunicationConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrWhiteSpace(config.ConnectionName)) throw new ArgumentException("名称不能为空");
            if (ConnectionsList.Any(c => c.ConnectionName == config.ConnectionName)) throw new InvalidOperationException("已存在同名连接");
            if (!config.Validate(out string error)) throw new InvalidOperationException($"验证失败: {error}");

            try
            {
                var connection = _factoryManager.CreateConnection(config);
                _connections[config.ConnectionName] = connection;
                ConnectionsList.Add(config);
                config.State = ConnectionState.Disconnected;
                OnConnectionStateChanged(config.ConnectionName, ConnectionState.Disconnected, ConnectionState.Disconnected);
                return true;
            }
            catch (Exception ex)
            {
                OnConnectionError(config.ConnectionName, ex);
                throw;
            }
        }

        public bool RemoveConnection(string connectionName)
        {
            if (string.IsNullOrWhiteSpace(connectionName)) return false;
            StopReconnectTimer(connectionName);
            StopHeartbeatTimer(connectionName);

            if (_connections.TryGetValue(connectionName, out var connection))
            {
                connection.Disconnect();
                if (connection is IDisposable disposable) disposable.Dispose();
            }
            _connections.Remove(connectionName);

            var config = ConnectionsList.FirstOrDefault(c => c.ConnectionName == connectionName);
            if (config != null) ConnectionsList.Remove(config);
            return true;
        }

        public bool Connect(string connectionName)
        {
            if (!_connections.TryGetValue(connectionName, out var connection)) throw new InvalidOperationException($"连接不存在: {connectionName}");
            var config = ConnectionsList.FirstOrDefault(c => c.ConnectionName == connectionName);
            if (config == null) return false;

            try
            {
                if (connection.IsConnected) return true;
                config.State = ConnectionState.Connecting;
                OnConnectionStateChanged(connectionName, ConnectionState.Disconnected, ConnectionState.Connecting);

                var result = connection.Connect();
                config.State = result ? ConnectionState.Connected : ConnectionState.Error;
                if (result)
                {
                    config.UpdateLastConnectedTime();
                    OnConnectionStateChanged(connectionName, ConnectionState.Connecting, ConnectionState.Connected);
                    if (AutoReconnectEnabled && config.AutoReconnect) StartHeartbeatTimer(connectionName);
                }
                else
                {
                    OnConnectionStateChanged(connectionName, ConnectionState.Connecting, ConnectionState.Error);
                    if (AutoReconnectEnabled && config.AutoReconnect) StartReconnectTimer(connectionName, config.Config.RetryIntervalMs);
                }
                return result;
            }
            catch (Exception ex)
            {
                config.State = ConnectionState.Error;
                OnConnectionStateChanged(connectionName, ConnectionState.Connecting, ConnectionState.Error);
                OnConnectionError(connectionName, ex);
                if (AutoReconnectEnabled && config.AutoReconnect) StartReconnectTimer(connectionName, config.Config.RetryIntervalMs);
                return false;
            }
        }

        public void Disconnect(string connectionName)
        {
            if (!_connections.TryGetValue(connectionName, out var connection)) return;
            var config = ConnectionsList.FirstOrDefault(c => c.ConnectionName == connectionName);
            StopReconnectTimer(connectionName);
            StopHeartbeatTimer(connectionName);

            try
            {
                connection.Disconnect();
                if (config != null)
                {
                    config.State = ConnectionState.Disconnected;
                    OnConnectionStateChanged(connectionName, ConnectionState.Connected, ConnectionState.Disconnected);
                }
            }
            catch (Exception ex) { OnConnectionError(connectionName, ex); }
        }

        public void ConnectAll() { foreach (var config in ConnectionsList.Where(c => c.IsEnabled && c.AutoStart)) Connect(config.ConnectionName); }
        public void DisconnectAll() { foreach (var name in _connections.Keys.ToList()) Disconnect(name); }
        public bool TestConnection(string connectionName) { if (!_connections.TryGetValue(connectionName, out var connection)) return false; try { return connection.TestConnection(); } catch { return false; } }

        public T? Read<T>(string connectionName, string address)
        {
            if (!_connections.TryGetValue(connectionName, out var connection)) throw new InvalidOperationException($"连接不存在: {connectionName}");
            if (!connection.IsConnected) throw new InvalidOperationException($"连接未建立: {connectionName}");
            return connection.Read<T>(address);
        }

        public void Write(string connectionName, string address, object value)
        {
            if (!_connections.TryGetValue(connectionName, out var connection)) throw new InvalidOperationException($"连接不存在: {connectionName}");
            if (!connection.IsConnected) throw new InvalidOperationException($"连接未建立: {connectionName}");
            connection.Write(address, value);
        }

        public async Task SaveConfigAsync()
        {
            var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var json = JsonSerializer.Serialize(ConnectionsList.ToList(), options);
            await File.WriteAllTextAsync(ConfigFilePath, json);
        }

        public async Task LoadConfigAsync()
        {
            if (!File.Exists(ConfigFilePath)) return;
            var json = await File.ReadAllTextAsync(ConfigFilePath);
            var configs = JsonSerializer.Deserialize<List<CommunicationConfig>>(json);
            if (configs != null) { foreach (var config in configs) { try { AddConnection(config); } catch { } } }
        }

        public async Task ExportConfigAsync(string filePath)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(ConnectionsList.ToList(), options);
            await File.WriteAllTextAsync(filePath, json);
        }

        public async Task ImportConfigAsync(string filePath)
        {
            if (!File.Exists(filePath)) throw new FileNotFoundException("配置文件不存在", filePath);
            var json = await File.ReadAllTextAsync(filePath);
            var configs = JsonSerializer.Deserialize<List<CommunicationConfig>>(json);
            if (configs != null) { foreach (var config in configs) { try { AddConnection(config); } catch { } } }
        }

        private void StartReconnectTimer(string name, int intervalMs)
        {
            StopReconnectTimer(name);
            _reconnectTimers[name] = new Timer(_ => Connect(name), null, intervalMs, Timeout.Infinite);
        }

        private void StopReconnectTimer(string name)
        {
            if (_reconnectTimers.Remove(name, out var timer)) timer.Dispose();
        }

        private void StartHeartbeatTimer(string name)
        {
            StopHeartbeatTimer(name);
            if (HeartbeatIntervalMs <= 0) return;
            _heartbeatTimers[name] = new Timer(_ => { if (!_connections.TryGetValue(name, out var c) || !c.IsConnected) Connect(name); }, null, HeartbeatIntervalMs, HeartbeatIntervalMs);
        }

        private void StopHeartbeatTimer(string name)
        {
            if (_heartbeatTimers.Remove(name, out var timer)) timer.Dispose();
        }

        private void OnConnectionStateChanged(string name, ConnectionState oldState, ConnectionState newState) => ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(name, oldState, newState));
        private void OnConnectionError(string name, Exception ex) => ConnectionError?.Invoke(this, new ConnectionErrorEventArgs(name, ex));

        public void Dispose()
        {
            if (_disposed) return;
            DisconnectAll();
            foreach (var timer in _reconnectTimers.Values) timer.Dispose();
            _reconnectTimers.Clear();
            foreach (var timer in _heartbeatTimers.Values) timer.Dispose();
            _heartbeatTimers.Clear();
            foreach (var connection in _connections.Values) if (connection is IDisposable d) d.Dispose();
            _connections.Clear();
            ConnectionsList.Clear();
            _disposed = true;
        }
    }

    public class ConnectionStateChangedEventArgs : EventArgs
    {
        public string ConnectionName { get; }
        public ConnectionState OldState { get; }
        public ConnectionState NewState { get; }
        public ConnectionStateChangedEventArgs(string name, ConnectionState oldState, ConnectionState newState) { ConnectionName = name; OldState = oldState; NewState = newState; }
    }

    public class ConnectionErrorEventArgs : EventArgs
    {
        public string ConnectionName { get; }
        public Exception Exception { get; }
        public ConnectionErrorEventArgs(string name, Exception ex) { ConnectionName = name; Exception = ex; }
    }

    public class CommunicationDataEventArgs : EventArgs
    {
        public string ConnectionName { get; }
        public string Address { get; }
        public object? Value { get; }
        public CommunicationDataEventArgs(string name, string address, object? value) { ConnectionName = name; Address = address; Value = value; }
    }
}
