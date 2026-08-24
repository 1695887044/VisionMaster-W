using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using VisionMaster.Communications;

public class DataPointManager : IDisposable
{
    #region 私有字段

    /// <summary>
    /// 所有已注册的数据点字典
    /// Key格式: "{ConnectionName}.{DataPointName}"
    /// </summary>
    private readonly ConcurrentDictionary<string, IDataPoint> _dataPoints = new();

    /// <summary>
    /// 按连接分组的数据点列表
    /// 用于快速获取某连接下的所有数据点
    /// </summary>
    private readonly ConcurrentDictionary<string, List<IDataPoint>> _dataPointByConnection = new();

    /// <summary>
    /// 数据值缓存字典
    /// 用于减少对PLC的频繁访问
    /// Key格式: "{ConnectionName}.{Address}"
    /// </summary>
    private readonly ConcurrentDictionary<string, (long Timestamp, object? Value)> _valueCache = new();

    /// <summary>
    /// 连接级轮询任务字典
    /// 使用异步任务替代定时器，避免并发问题
    /// </summary>
    private readonly ConcurrentDictionary<string, (Task Task, CancellationTokenSource Cts)> _pollingTasks = new();

    /// <summary>
    /// 底层的通讯管理器引用
    /// </summary>
    private readonly AdvancedCommunicationManager _commManager;

    /// <summary>
    /// 线程同步锁
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 释放标志
    /// </summary>
    private bool _disposed = false;

    /// <summary>
    /// 缓存过期时间（100ms）
    /// </summary>
    private const long CacheExpirationTicks = 100 * TimeSpan.TicksPerMillisecond;

    /// <summary>
    /// 缓存清理定时器
    /// </summary>
    private readonly Timer _cacheCleanupTimer;

    /// <summary>
    /// 读取方法缓存（避免重复反射）
    /// Key: 数据类型
    /// Value: 强类型读取方法委托
    /// </summary>
    private static readonly ConcurrentDictionary<Type, MethodInfo> _readMethodCache = new();

    /// <summary>
    /// 高精度计时起点
    /// </summary>
    private static readonly long _startTimestamp = Stopwatch.GetTimestamp();

    #endregion

    #region 事件

    /// <summary>
    /// 数据点值变化事件
    /// </summary>
    public event EventHandler<DataPointChangedEventArgs>? DataChanged;

    #endregion

    #region 属性

    /// <summary>
    /// 获取已注册的数据点总数
    /// </summary>
    public int DataPointCount => _dataPoints.Count;

    /// <summary>
    /// 获取当前缓存的数值条目数
    /// </summary>
    public int CachedValueCount => _valueCache.Count;

    #endregion

    #region 构造方法

    /// <summary>
    /// 初始化数据点管理器的新实例
    /// </summary>
    /// <param name="commManager">底层的通讯管理器</param>
    /// <exception cref="ArgumentNullException">当commManager为null时抛出</exception>
    public DataPointManager(AdvancedCommunicationManager commManager)
    {
        _commManager = commManager ?? throw new ArgumentNullException(nameof(commManager));

        // 每10秒清理一次过期缓存
        _cacheCleanupTimer = new Timer(CleanupExpiredCache, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    #endregion

    #region 数据点注册与获取

    /// <summary>
    /// 注册一个新的数据点
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="name">数据点名称</param>
    /// <param name="connectionName">所属连接名称</param>
    /// <param name="address">地址配置</param>
    /// <returns>数据点实例</returns>
    public DataPoint<T> RegisterDataPoint<T>(
        string name,
        string connectionName,
        DeviceAddressBase address)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("数据点名称不能为空", nameof(name));
        if (string.IsNullOrWhiteSpace(connectionName))
            throw new ArgumentException("连接名称不能为空", nameof(connectionName));
        if (address == null)
            throw new ArgumentNullException(nameof(address));

        lock (_lock)
        {
            // 生成唯一键
            var key = $"{connectionName}.{name}";

            // 如果已存在，返回现有实例
            if (_dataPoints.TryGetValue(key, out var existing))
                return (DataPoint<T>)existing;

            // 创建新的数据点（构造函数会自动验证类型匹配）
            var dataPoint = new DataPoint<T>(name, address);

            // 添加到字典
            _dataPoints[key] = dataPoint;

            // 添加到连接分组
            _dataPointByConnection.AddOrUpdate(
                connectionName,
                _ => new List<IDataPoint> { dataPoint },
                (_, list) => { list.Add(dataPoint); return list; });

            return dataPoint;
        }
    }

    /// <summary>
    /// 获取指定名称的数据点
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="name">数据点名称</param>
    /// <param name="connectionName">连接名称</param>
    /// <returns>数据点实例，不存在返回null</returns>
    public DataPoint<T>? GetDataPoint<T>(string name, string connectionName)
    {
        var key = $"{connectionName}.{name}";
        return _dataPoints.TryGetValue(key, out var dp) ? (DataPoint<T>)dp : null;
    }

    /// <summary>
    /// 获取指定连接下的所有数据点
    /// </summary>
    /// <param name="connectionName">连接名称</param>
    /// <returns>数据点列表</returns>
    public IReadOnlyList<IDataPoint> GetDataPointsByConnection(string connectionName)
    {
        return _dataPointByConnection.TryGetValue(connectionName, out var list)
            ? list.AsReadOnly()
            : Array.Empty<IDataPoint>();
    }

    /// <summary>
    /// 注销指定的数据点
    /// </summary>
    /// <param name="name">数据点名称</param>
    /// <param name="connectionName">连接名称</param>
    /// <returns>是否成功注销</returns>
    public bool UnregisterDataPoint(string name, string connectionName)
    {
        lock (_lock)
        {
            var key = $"{connectionName}.{name}";

            if (!_dataPoints.TryRemove(key, out _))
                return false;

            if (_dataPointByConnection.TryGetValue(connectionName, out var list))
            {
                list.RemoveAll(p => p.Name == name);

                if (list.Count == 0)
                {
                    _dataPointByConnection.TryRemove(connectionName, out _);
                }
            }

            return true;
        }
    }

    #endregion

    #region 轮询控制

    /// <summary>
    /// 启动指定连接的轮询
    /// </summary>
    /// <param name="connectionName">要轮询的连接名称</param>
    /// <param name="intervalMs">轮询间隔（毫秒），默认500ms</param>
    public void StartPolling(string connectionName, int intervalMs = 500)
    {
        if (string.IsNullOrWhiteSpace(connectionName))
            throw new ArgumentException("连接名称不能为空", nameof(connectionName));
        if (intervalMs < 10)
            throw new ArgumentOutOfRangeException(nameof(intervalMs), "轮询间隔不能小于10ms");

        // 防止重复启动
        if (_pollingTasks.ContainsKey(connectionName))
            return;

        // 创建取消令牌
        var cts = new CancellationTokenSource();

        // 启动异步轮询循环
        var pollingTask = PollingLoopAsync(connectionName, intervalMs, cts.Token);

        _pollingTasks[connectionName] = (pollingTask, cts);
    }

    /// <summary>
    /// 异步轮询循环（修复了原信号量bug）
    /// </summary>
    private async Task PollingLoopAsync(string connectionName, int intervalMs, CancellationToken token)
    {
        // 异步循环天然保证上一次执行完成后才会开始下一次
        // 不需要信号量，彻底解决并发问题

        while (!token.IsCancellationRequested && !_disposed)
        {
            try
            {
                // 执行一次轮询
                PollConnection(connectionName);

                // 等待指定间隔
                await Task.Delay(intervalMs, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PollingLoop error for {connectionName}: {ex.Message}");
                // 发生错误时等待一段时间再重试
                try { await Task.Delay(intervalMs, token); } catch { }
            }
        }
    }

    /// <summary>
    /// 停止指定连接的轮询
    /// </summary>
    /// <param name="connectionName">连接名称</param>
    public void StopPolling(string connectionName)
    {
        if (_pollingTasks.TryRemove(connectionName, out var pollingInfo))
        {
            pollingInfo.Cts.Cancel();
            pollingInfo.Cts.Dispose();
        }
    }

    /// <summary>
    /// 停止所有连接的轮询
    /// </summary>
    public void StopAllPolling()
    {
        foreach (var pollingInfo in _pollingTasks.Values)
        {
            pollingInfo.Cts.Cancel();
            pollingInfo.Cts.Dispose();
        }
        _pollingTasks.Clear();
    }

    #endregion

    #region 私有方法

    /// <summary>
    /// 执行一次连接轮询
    /// </summary>
    private void PollConnection(string connectionName)
    {
        if (_disposed) return;

        try
        {
            // 获取连接
            var connection = _commManager.GetConnection(connectionName);
            if (connection == null || !connection.IsConnected)
            {
                // 标记所有数据点为未连接
                MarkAllDataPointsAsBad(connectionName, "Connection not established");
                return;
            }

            // 获取数据点列表副本
            if (!_dataPointByConnection.TryGetValue(connectionName, out var dataPoints))
                return;

            var dataPointsCopy = dataPoints.ToList();

            // 锁外遍历读取
            foreach (var dataPoint in dataPointsCopy)
            {
                if (_disposed) break;
                ReadSingleDataPoint(connection, connectionName, dataPoint);
            }

            // 批量触发数据变化事件
            var changedDataPoints = dataPointsCopy.Where(dp => dp.HasChanged).ToList();
            if (changedDataPoints.Count > 0)
            {
                lock (_lock)
                {
                    foreach (var dp in changedDataPoints)
                    {
                        dp.AcceptChanges();
                        NotifyDataChanged(connectionName, dp);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PollConnection error for {connectionName}: {ex.Message}");
        }
    }

    /// <summary>
    /// 读取单个数据点的值（适配旧的Read<T>方法，无dynamic）
    /// </summary>
    private void ReadSingleDataPoint(
        ICommunicationConnection connection,
        string connectionName,
        IDataPoint dataPoint)
    {
        try
        {
            string address = dataPoint.Address.Address;
            string cacheKey = $"{connectionName}.{address}";

            // 检查缓存（快速路径）
            if (TryGetCachedValue(cacheKey, out var cachedValue))
            {
                dataPoint.UpdateValue(cachedValue);
                return;
            }

            // ✅ 适配旧方案：通过反射调用Read<T>方法
            // 只在第一次调用时反射，后续使用缓存的MethodInfo
            object? rawValue = ReadValueByType(connection, address, dataPoint.ValueType);

            // 更新缓存和数据点
            if (rawValue != null)
            {
                SetCachedValue(cacheKey, rawValue);
                dataPoint.UpdateValue(rawValue);
            }
            else
            {
                dataPoint.MarkAsBad("Read returned null");
            }
        }
        catch (Exception ex)
        {
            dataPoint.MarkAsBad(ex.Message);
        }
    }

    /// <summary>
    /// 根据数据类型调用对应的Read<T>方法（适配旧方案）
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

    /// <summary>
    /// 标记连接下所有数据点为坏值
    /// </summary>
    private void MarkAllDataPointsAsBad(string connectionName, string message)
    {
        if (_dataPointByConnection.TryGetValue(connectionName, out var dataPoints))
        {
            foreach (var dp in dataPoints)
            {
                dp.MarkAsBad(message);
            }
        }
    }

    /// <summary>
    /// 通知数据点值已变化（修复了ConnectionName获取问题）
    /// </summary>
    private void NotifyDataChanged(string connectionName, IDataPoint dataPoint)
    {
        DataChanged?.Invoke(this, new DataPointChangedEventArgs
        {
            DataPointName = dataPoint.Name,
            ConnectionName = connectionName,
            NewValue = dataPoint.Value,
            Timestamp = dataPoint.Timestamp
        });
    }

    #endregion

    #region 缓存管理

    /// <summary>
    /// 尝试获取缓存值
    /// </summary>
    private bool TryGetCachedValue(string key, out object? value)
    {
        if (_valueCache.TryGetValue(key, out var item))
        {
            long now = Stopwatch.GetTimestamp() - _startTimestamp;
            if (now - item.Timestamp < CacheExpirationTicks)
            {
                value = item.Value;
                return true;
            }

            // 过期项立即移除
            _valueCache.TryRemove(key, out _);
        }

        value = null;
        return false;
    }

    /// <summary>
    /// 设置缓存值
    /// </summary>
    private void SetCachedValue(string key, object? value)
    {
        long timestamp = Stopwatch.GetTimestamp() - _startTimestamp;
        _valueCache[key] = (timestamp, value);
    }

    /// <summary>
    /// 清理过期缓存
    /// </summary>
    private void CleanupExpiredCache(object? state)
    {
        long now = Stopwatch.GetTimestamp() - _startTimestamp;
        foreach (var key in _valueCache.Keys.ToList())
        {
            if (_valueCache.TryGetValue(key, out var item) && now - item.Timestamp >= CacheExpirationTicks)
            {
                _valueCache.TryRemove(key, out _);
            }
        }
    }

    /// <summary>
    /// 清空所有缓存
    /// </summary>
    public void ClearCache()
    {
        _valueCache.Clear();
    }

    /// <summary>
    /// 清空指定连接的缓存
    /// </summary>
    /// <param name="connectionName">连接名称</param>
    public void ClearCache(string connectionName)
    {
        var prefix = $"{connectionName}.";
        foreach (var key in _valueCache.Keys.Where(k => k.StartsWith(prefix)).ToList())
        {
            _valueCache.TryRemove(key, out _);
        }
    }

    #endregion

    #region 写入操作

    /// <summary>
    /// 异步写入数据点到设备
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="name">数据点名称</param>
    /// <param name="connectionName">连接名称</param>
    /// <param name="engineeringValue">工程值（会自动转换为原始值）</param>
    /// <returns>是否写入成功</returns>
    public async Task<bool> WriteDataPointAsync<T>(
        string name,
        string connectionName,
        T engineeringValue)
    {
        var key = $"{connectionName}.{name}";

        // 获取数据点
        if (!_dataPoints.TryGetValue(key, out var dataPoint))
            return false;

        // 获取连接
        var connection = _commManager.GetConnection(connectionName);
        if (connection == null || !connection.IsConnected)
        {
            dataPoint.MarkAsBad("Connection not established");
            return false;
        }

        try
        {
            // ✅ 重要：将工程值转换为设备需要的原始值
            object? rawValue = dataPoint.Address.ConvertToRaw(engineeringValue);

            // 在锁外执行IO操作
            await Task.Run(() => connection.Write(dataPoint.Address.Address, rawValue));

            // 更新缓存和数据点
            lock (_lock)
            {
                // 更新缓存
                string cacheKey = $"{connectionName}.{dataPoint.Address.Address}";
                SetCachedValue(cacheKey, rawValue);

                // 更新数据点值
                dataPoint.UpdateValue(rawValue);

                // 通知变化
                NotifyDataChanged(connectionName, dataPoint);
            }

            return true;
        }
        catch (Exception ex)
        {
            dataPoint.MarkAsBad($"Write failed: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// 释放数据点管理器使用的所有资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 停止并清理所有轮询任务
        StopAllPolling();

        // 释放缓存清理定时器
        _cacheCleanupTimer.Dispose();

        // 清空集合
        _dataPoints.Clear();
        _dataPointByConnection.Clear();
        _valueCache.Clear();
    }

    #endregion
}

/// <summary>
/// 数据点值变化事件参数
/// </summary>
public class DataPointChangedEventArgs : EventArgs
{
    /// <summary>
    /// 数据点的名称
    /// </summary>
    public string DataPointName { get; set; } = "";

    /// <summary>
    /// 所属连接的名称
    /// </summary>
    public string ConnectionName { get; set; } = "";

    /// <summary>
    /// 新的数据值（原始值）
    /// </summary>
    public object? NewValue { get; set; }

    /// <summary>
    /// 变化发生的时间戳（UTC）
    /// </summary>
    public DateTime Timestamp { get; set; }
}