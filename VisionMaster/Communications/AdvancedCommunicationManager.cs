using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VisionMaster.Helpers;

namespace VisionMaster.Communications
{
    /// <summary>
    /// 高级通讯管理器实现类
    /// 提供完整的通讯管理功能，包括连接管理、变量注册、批量读取和写入队列处理
    /// </summary>
    public class AdvancedCommunicationManager : ICommunicationManager
    {
        // 连接对象字典，键为连接名称
        private readonly ConcurrentDictionary<string, ICommunicationConnection> _connections = new();

        // 连接配置字典，键为连接名称
        private readonly ConcurrentDictionary<string, CommunicationConfig> _configs = new();

        // 读取循环取消标记字典
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _readCycles = new();

        // 已注册的通讯变量字典，每个连接对应一个变量列表
        private readonly ConcurrentDictionary<string, List<CommunicationVariable>> _readVariables = new();

        // 写入请求队列字典，每个连接对应一个写入队列
        private readonly ConcurrentDictionary<string, ConcurrentQueue<VariableWriteRequest>> _writeQueue = new();

        // 批量读取锁，保证线程安全
        private readonly object _batchReadLock = new object();

        // 写入处理任务
        private Task? _writeTask;

        // 写入任务取消标记
        private CancellationTokenSource? _writeCts;

        /// <summary>
        /// 通讯错误事件
        /// </summary>
        public event EventHandler<CommunicationErrorEventArgs>? OnCommunicationError;

        /// <summary>
        /// 变量值变化事件
        /// </summary>
        public event EventHandler<VariableChangedEventArgs>? OnVariableChanged;

        /// <summary>
        /// 获取指定连接
        /// </summary>
        /// <param name="connectionName">连接名称</param>
        /// <returns>连接对象，如果不存在则返回null</returns>
        public ICommunicationConnection? GetConnection(string connectionName)
        {
            _connections.TryGetValue(connectionName, out var connection);
            return connection;
        }

        /// <summary>
        /// 添加新连接
        /// </summary>
        /// <param name="config">连接配置</param>
        /// <returns>是否添加成功</returns>
        public bool AddConnection(CommunicationConfig config)
        {
            // 检查是否已存在同名连接
            if (_configs.ContainsKey(config.ConnectionName))
                return false;

            try
            {
                // 根据通讯类型创建对应的连接对象
                ICommunicationConnection connection = config.Type switch
                {
                    CommunicationType.ModbusTcp => new ModbusTcpConnection(config),
                    CommunicationType.SiemensS7 => new SiemensS7Connection(config),
                    CommunicationType.ModbusRtu => throw new NotSupportedException("Modbus RTU暂未实现"),
                    CommunicationType.FreeProtocol => throw new NotSupportedException("自由协议暂未实现"),
                    _ => throw new NotSupportedException($"不支持的通讯类型: {config.Type}")
                };

                // 保存连接对象和配置
                _connections[config.ConnectionName] = connection;
                _configs[config.ConnectionName] = config;
                _readVariables[config.ConnectionName] = new List<CommunicationVariable>();
                _writeQueue[config.ConnectionName] = new ConcurrentQueue<VariableWriteRequest>();

                return true;
            }
            catch (Exception ex)
            {
                // 触发错误事件
                OnCommunicationError?.Invoke(this, new CommunicationErrorEventArgs
                {
                    ConnectionName = config.ConnectionName,
                    ErrorMessage = $"添加连接失败: {ex.Message}",
                    Exception = ex
                });
                return false;
            }
        }

        /// <summary>
        /// 移除连接
        /// </summary>
        /// <param name="connectionName">连接名称</param>
        /// <returns>是否移除成功</returns>
        public bool RemoveConnection(string connectionName)
        {
            // 先停止读取循环
            StopReadCycle(connectionName);

            // 尝试移除并释放资源
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

        /// <summary>
        /// 更新连接配置
        /// </summary>
        /// <param name="config">新的连接配置</param>
        /// <returns>是否更新成功</returns>
        public bool UpdateConnection(CommunicationConfig config)
        {
            RemoveConnection(config.ConnectionName);
            return AddConnection(config);
        }

        /// <summary>
        /// 获取所有连接配置
        /// </summary>
        /// <returns>连接配置列表</returns>
        public List<CommunicationConfig> GetAllConnections()
        {
            return new List<CommunicationConfig>(_configs.Values);
        }

        /// <summary>
        /// 启动所有已启用的连接和读取循环
        /// </summary>
        public void StartAll()
        {
            // 遍历所有配置，连接并启动读取
            foreach (var config in _configs.Values)
            {
                if (config.IsEnabled)
                {
                    var connection = GetConnection(config.ConnectionName);
                    if (connection != null && !connection.IsConnected)
                    {
                        connection.Connect();
                    }

                    // 如果配置了读取周期，启动读取循环
                    if (config.ReadCycleMs > 0)
                    {
                        StartReadCycle(config.ConnectionName, config.ReadCycleMs);
                    }
                }
            }

            // 启动写入处理器
            StartWriteProcessor();
        }

        /// <summary>
        /// 停止所有通讯
        /// </summary>
        public void StopAll()
        {
            // 停止所有读取循环
            foreach (var connectionName in _connections.Keys)
            {
                StopReadCycle(connectionName);
            }

            // 停止写入处理器
            StopWriteProcessor();
        }

        /// <summary>
        /// 注册通讯变量到指定的连接
        /// </summary>
        /// <param name="variable">通讯变量</param>
        public void RegisterVariable(CommunicationVariable variable)
        {
            // 如果连接不存在，先创建列表
            if (!_readVariables.ContainsKey(variable.ConnectionName))
                _readVariables[variable.ConnectionName] = new List<CommunicationVariable>();

            var variables = _readVariables[variable.ConnectionName];

            // 检查是否已存在同名地址的变量，避免重复注册
            if (!variables.Any(v => v.Address == variable.Address && v.VariableName == variable.VariableName))
            {
                variables.Add(variable);
            }
        }

        /// <summary>
        /// 注销通讯变量
        /// </summary>
        /// <param name="connectionName">连接名称</param>
        /// <param name="variableName">变量名称</param>
        public void UnregisterVariable(string connectionName, string variableName)
        {
            if (_readVariables.TryGetValue(connectionName, out var variables))
            {
                variables.RemoveAll(v => v.VariableName == variableName);
            }
        }

        /// <summary>
        /// 触发写入操作，将请求加入写入队列
        /// </summary>
        /// <param name="connectionName">连接名称</param>
        /// <param name="address">变量地址</param>
        /// <param name="value">写入值</param>
        /// <param name="valueType">值类型</param>
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

        /// <summary>
        /// 直接写入变量值(同步写入)
        /// </summary>
        /// <param name="connectionName">连接名称</param>
        /// <param name="address">变量地址</param>
        /// <param name="value">写入值</param>
        public void WriteVariable(string connectionName, string address, object value)
        {
            var connection = GetConnection(connectionName);
            if (connection == null || !connection.IsConnected)
                throw new InvalidOperationException($"连接 {connectionName} 不存在或未连接");

            connection.Write(address, value);
        }

        /// <summary>
        /// 读取变量值
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="connectionName">连接名称</param>
        /// <param name="address">变量地址</param>
        /// <returns>读取的值</returns>
        public T? ReadVariable<T>(string connectionName, string address)
        {
            var connection = GetConnection(connectionName);
            if (connection == null || !connection.IsConnected)
                throw new InvalidOperationException($"连接 {connectionName} 不存在或未连接");

            return connection.Read<T>(address);
        }

        /// <summary>
        /// 启动指定连接的读取循环
        /// </summary>
        /// <param name="connectionName">连接名称</param>
        /// <param name="cycleMs">读取周期(毫秒)</param>
        private void StartReadCycle(string connectionName, int cycleMs)
        {
            // 先停止已存在的读取循环
            StopReadCycle(connectionName);

            // 创建新的取消标记
            var cts = new CancellationTokenSource();
            _readCycles[connectionName] = cts;

            // 启动异步读取循环
            Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        // 执行批量读取
                        await BatchReadVariablesAsync(connectionName);

                        // 等待下一个读取周期
                        await Task.Delay(cycleMs, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        // 正常取消，退出循环
                        break;
                    }
                    catch (Exception ex)
                    {
                        // 发生异常，触发错误事件并继续
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

        /// <summary>
        /// 停止指定连接的读取循环
        /// </summary>
        /// <param name="connectionName">连接名称</param>
        private void StopReadCycle(string connectionName)
        {
            if (_readCycles.TryRemove(connectionName, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }

        /// <summary>
        /// 批量读取所有已注册的变量
        /// </summary>
        /// <param name="connectionName">连接名称</param>
        private async Task BatchReadVariablesAsync(string connectionName)
        {
            // 获取该连接下的所有变量
            if (!_readVariables.TryGetValue(connectionName, out var variables) || variables.Count == 0)
                return;

            var connection = GetConnection(connectionName);
            if (connection == null || !connection.IsConnected)
                return;

            // 在线程池中执行批量读取
            await Task.Run(() =>
            {
                lock (_batchReadLock)
                {
                    foreach (var variable in variables)
                    {
                        try
                        {
                            // 根据类型读取变量值
                            var value = ReadVariableByType(connection, variable.Address, TypeCache.GetType(variable.VariableName));

                            // 触发变量变化事件
                            OnVariableChanged?.Invoke(this, new VariableChangedEventArgs
                            {
                                ConnectionName = connectionName,
                                Address = variable.Address,
                                NewValue = value
                            });

                            // 更新变量当前值
                            variable.UpdateValue(value);
                        }
                        catch (Exception ex)
                        {
                            // 读取失败，触发错误事件
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

        /// <summary>
        /// 根据类型读取变量值
        /// </summary>
        /// <param name="connection">连接对象</param>
        /// <param name="address">变量地址</param>
        /// <param name="valueType">值类型</param>
        /// <returns>读取的值</returns>
        private object? ReadVariableByType(ICommunicationConnection connection, string address, Type valueType)
        {
            // 根据不同类型调用对应的读取方法
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

        /// <summary>
        /// 启动写入处理器
        /// </summary>
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
                        // 处理写入队列
                        await ProcessWriteQueueAsync();

                        // 短暂延迟，避免CPU占用过高
                        await Task.Delay(10, _writeCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        // 处理异常，继续运行
                        OnCommunicationError?.Invoke(this, new CommunicationErrorEventArgs
                        {
                            ErrorMessage = $"写入处理异常: {ex.Message}",
                            Exception = ex
                        });
                    }
                }
            }, _writeCts.Token);
        }

        /// <summary>
        /// 停止写入处理器
        /// </summary>
        private void StopWriteProcessor()
        {
            _writeCts?.Cancel();
            _writeTask = null;
        }

        /// <summary>
        /// 处理写入队列
        /// </summary>
        private async Task ProcessWriteQueueAsync()
        {
            // 遍历所有连接的写入队列
            foreach (var connectionName in _writeQueue.Keys)
            {
                if (_writeQueue.TryGetValue(connectionName, out var queue))
                {
                    // 取出并处理所有写入请求
                    while (queue.TryDequeue(out var request))
                    {
                        try
                        {
                            // 执行写入操作
                            await Task.Run(() => WriteVariable(request.ConnectionName, request.Address, request.Value));
                        }
                        catch (Exception ex)
                        {
                            // 写入失败，触发错误事件
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
