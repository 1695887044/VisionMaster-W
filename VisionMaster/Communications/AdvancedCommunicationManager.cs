using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VisionMaster.Communications
{
    public interface ICommunicationManager
    {
        event EventHandler<CommunicationErrorEventArgs>? OnCommunicationError;
        event EventHandler<VariableChangedEventArgs>? OnVariableChanged;
        
        ICommunicationConnection? GetConnection(string connectionName);
        bool AddConnection(CommunicationConfig config);
        bool RemoveConnection(string connectionName);
        bool UpdateConnection(CommunicationConfig config);
        List<CommunicationConfig> GetAllConnections();
        void StartAll();
        void StopAll();
        void WriteVariable(string connectionName, string address, object value);
        T? ReadVariable<T>(string connectionName, string address);
        void RegisterVariable(CommunicationVariable variable);
        void UnregisterVariable(string connectionName, string variableName);
        void TriggerWrite(string connectionName, string address, object value, Type valueType);
    }

    public class AdvancedCommunicationManager : ICommunicationManager
    {
        private readonly ConcurrentDictionary<string, ICommunicationConnection> _connections = new();
        private readonly ConcurrentDictionary<string, CommunicationConfig> _configs = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _readCycles = new();
        
        private readonly ConcurrentDictionary<string, List<CommunicationVariable>> _readVariables = new();
        private readonly ConcurrentDictionary<string, ConcurrentQueue<VariableWriteRequest>> _writeQueue = new();
        
        private readonly object _batchReadLock = new object();
        private Task? _writeTask;
        private CancellationTokenSource? _writeCts;

        public event EventHandler<CommunicationErrorEventArgs>? OnCommunicationError;
        public event EventHandler<VariableChangedEventArgs>? OnVariableChanged;

        public ICommunicationConnection? GetConnection(string connectionName)
        {
            _connections.TryGetValue(connectionName, out var connection);
            return connection;
        }

        public bool AddConnection(CommunicationConfig config)
        {
            if (_configs.ContainsKey(config.ConnectionName))
                return false;

            try
            {
                ICommunicationConnection connection = config.Type switch
                {
                    CommunicationType.ModbusTcp => new ModbusTcpConnection(config),
                    CommunicationType.SiemensS7 => new SiemensS7Connection(config),
                    CommunicationType.ModbusRtu => throw new NotSupportedException("Modbus RTU暂未实现"),
                    CommunicationType.FreeProtocol => throw new NotSupportedException("自由协议暂未实现"),
                    _ => throw new NotSupportedException($"不支持的通讯类型: {config.Type}")
                };

                _connections[config.ConnectionName] = connection;
                _configs[config.ConnectionName] = config;
                _readVariables[config.ConnectionName] = new List<CommunicationVariable>();
                _writeQueue[config.ConnectionName] = new ConcurrentQueue<VariableWriteRequest>();

                return true;
            }
            catch (Exception ex)
            {
                OnCommunicationError?.Invoke(this, new CommunicationErrorEventArgs
                {
                    ConnectionName = config.ConnectionName,
                    ErrorMessage = $"添加连接失败: {ex.Message}",
                    Exception = ex
                });
                return false;
            }
        }

        public bool RemoveConnection(string connectionName)
        {
            StopReadCycle(connectionName);
            
            if (_connections.TryRemove(connectionName, out var connection))
            {
                connection.Dispose();
                _configs.TryRemove(connectionName, out _);
                _readVariables.TryRemove(connectionName, out _);
                _writeQueue.TryRemove(connectionName, out _);
                return true;
            }
            return false;
        }

        public bool UpdateConnection(CommunicationConfig config)
        {
            RemoveConnection(config.ConnectionName);
            return AddConnection(config);
        }

        public List<CommunicationConfig> GetAllConnections()
        {
            return new List<CommunicationConfig>(_configs.Values);
        }

        public void StartAll()
        {
            foreach (var config in _configs.Values)
            {
                if (config.IsEnabled)
                {
                    var connection = GetConnection(config.ConnectionName);
                    if (connection != null && !connection.IsConnected)
                    {
                        connection.Connect();
                    }
                    
                    if (config.ReadCycleMs > 0)
                    {
                        StartReadCycle(config.ConnectionName, config.ReadCycleMs);
                    }
                }
            }

            StartWriteProcessor();
        }

        public void StopAll()
        {
            foreach (var connectionName in _connections.Keys)
            {
                StopReadCycle(connectionName);
            }

            StopWriteProcessor();
        }

        public void RegisterVariable(CommunicationVariable variable)
        {
            if (!_readVariables.ContainsKey(variable.ConnectionName))
                _readVariables[variable.ConnectionName] = new List<CommunicationVariable>();

            var variables = _readVariables[variable.ConnectionName];
            if (!variables.Any(v => v.Address == variable.Address && v.VariableName == variable.VariableName))
            {
                variables.Add(variable);
            }
        }

        public void UnregisterVariable(string connectionName, string variableName)
        {
            if (_readVariables.TryGetValue(connectionName, out var variables))
            {
                variables.RemoveAll(v => v.VariableName == variableName);
            }
        }

        public void TriggerWrite(string connectionName, string address, object value, Type valueType)
        {
            if (_writeQueue.TryGetValue(connectionName, out var queue))
            {
                queue.Enqueue(new VariableWriteRequest
                {
                    ConnectionName = connectionName,
                    Address = address,
                    Value = value,
                    ValueType = valueType
                });
            }
        }

        public void WriteVariable(string connectionName, string address, object value)
        {
            var connection = GetConnection(connectionName);
            if (connection == null || !connection.IsConnected)
                throw new InvalidOperationException($"连接 {connectionName} 不存在或未连接");

            connection.Write(address, value);
        }

        public T? ReadVariable<T>(string connectionName, string address)
        {
            var connection = GetConnection(connectionName);
            if (connection == null || !connection.IsConnected)
                throw new InvalidOperationException($"连接 {connectionName} 不存在或未连接");

            return connection.Read<T>(address);
        }

        private void StartReadCycle(string connectionName, int cycleMs)
        {
            StopReadCycle(connectionName);

            var cts = new CancellationTokenSource();
            _readCycles[connectionName] = cts;

            Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await BatchReadVariablesAsync(connectionName);
                        await Task.Delay(cycleMs, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        OnCommunicationError?.Invoke(this, new CommunicationErrorEventArgs
                        {
                            ConnectionName = connectionName,
                            ErrorMessage = $"读取循环异常: {ex.Message}",
                            Exception = ex
                        });
                    }
                }
            }, cts.Token);
        }

        private void StopReadCycle(string connectionName)
        {
            if (_readCycles.TryRemove(connectionName, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }

        private async Task BatchReadVariablesAsync(string connectionName)
        {
            if (!_readVariables.TryGetValue(connectionName, out var variables) || variables.Count == 0)
                return;

            var connection = GetConnection(connectionName);
            if (connection == null || !connection.IsConnected)
                return;

            await Task.Run(() =>
            {
                lock (_batchReadLock)
                {
                    foreach (var variable in variables)
                    {
                        try
                        {
                            var value = ReadVariableByType(connection, variable.Address, variable.ValueType);
                            
                            OnVariableChanged?.Invoke(this, new VariableChangedEventArgs
                            {
                                ConnectionName = connectionName,
                                Address = variable.Address,
                                NewValue = value
                            });

                            variable.UpdateValue(value);
                        }
                        catch (Exception ex)
                        {
                            OnCommunicationError?.Invoke(this, new CommunicationErrorEventArgs
                            {
                                ConnectionName = connectionName,
                                ErrorMessage = $"读取变量 {variable.VariableName} 失败: {ex.Message}",
                                Exception = ex
                            });
                        }
                    }
                }
            });
        }

        private object? ReadVariableByType(ICommunicationConnection connection, string address, Type valueType)
        {
            if (valueType == typeof(bool))
                return connection.Read<bool>(address);
            else if (valueType == typeof(short))
                return connection.Read<short>(address);
            else if (valueType == typeof(ushort))
                return connection.Read<ushort>(address);
            else if (valueType == typeof(int))
                return connection.Read<int>(address);
            else if (valueType == typeof(uint))
                return connection.Read<uint>(address);
            else if (valueType == typeof(long))
                return connection.Read<long>(address);
            else if (valueType == typeof(ulong))
                return connection.Read<ulong>(address);
            else if (valueType == typeof(float))
                return connection.Read<float>(address);
            else if (valueType == typeof(double))
                return connection.Read<double>(address);
            else if (valueType == typeof(byte))
                return connection.Read<byte>(address);
            
            return null;
        }

        private void StartWriteProcessor()
        {
            StopWriteProcessor();
            _writeCts = new CancellationTokenSource();
            _writeTask = Task.Run(async () =>
            {
                while (!_writeCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await ProcessWriteQueueAsync();
                        await Task.Delay(10, _writeCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        OnCommunicationError?.Invoke(this, new CommunicationErrorEventArgs
                        {
                            ErrorMessage = $"写入处理异常: {ex.Message}",
                            Exception = ex
                        });
                    }
                }
            }, _writeCts.Token);
        }

        private void StopWriteProcessor()
        {
            _writeCts?.Cancel();
            _writeTask = null;
        }

        private async Task ProcessWriteQueueAsync()
        {
            foreach (var connectionName in _writeQueue.Keys)
            {
                if (_writeQueue.TryGetValue(connectionName, out var queue))
                {
                    while (queue.TryDequeue(out var request))
                    {
                        try
                        {
                            await Task.Run(() => WriteVariable(request.ConnectionName, request.Address, request.Value));
                        }
                        catch (Exception ex)
                        {
                            OnCommunicationError?.Invoke(this, new CommunicationErrorEventArgs
                            {
                                ConnectionName = request.ConnectionName,
                                ErrorMessage = $"写入变量 {request.Address} 失败: {ex.Message}",
                                Exception = ex
                            });
                        }
                    }
                }
            }
        }
    }
}
