# VisionMaster

工业视觉检测与流程自动化平台 - 基于 WPF + Prism 的现代化机器视觉软件框架

## 目录

- [项目概述](#项目概述)
- [系统架构](#系统架构)
- [通信模块详解](#通信模块详解)
  - [核心接口](#核心接口)
  - [地址配置](#地址配置)
  - [数据点管理](#数据点管理)
  - [通讯管理器](#通讯管理器)
  - [轮询机制](#轮询机制)
  - [缓存机制](#缓存机制)
- [快速开始](#快速开始)
- [API 参考](#api-参考)
- [最佳实践](#最佳实践)

---

## 项目概述

VisionMaster 是一个功能完整的工业视觉检测与流程自动化平台，采用现代化的 WPF + Prism 架构设计。

### 核心特性

| 特性 | 说明 |
|------|------|
| **多协议通讯** | Modbus TCP/RTU、西门子 S7 系列、三菱、欧姆龙、AB、OPC UA |
| **数据转换管道** | 支持 Scale/Offset/Unit 自动转换 |
| **阈值报警** | 四级报警机制（HH/H/L/LL） |
| **通讯质量统计** | 实时监控读写成功率和响应时间 |
| **连接健康检查** | 定时监控连接状态，支持自动重连 |
| **历史数据记录** | 数据变化记录，支持统计查询 |
| **配置持久化** | JSON 格式导入/导出 |

### 技术栈

| 技术 | 用途 |
|------|------|
| .NET 9.0 | 运行时框架 |
| WPF | UI 框架 |
| Prism 9.0 | MVVM 框架 |
| HslCommunication | 工业通讯库 |

---

## 系统架构

```
VisionMaster/
├── Communications/                    # 通讯模块
│   ├── Core/                         # 核心接口
│   │   ├── ICommunicationConnection.cs   # 连接接口
│   │   ├── ICommunicationManager.cs     # 管理器接口
│   │   └── CommunicationVariable.cs      # 通信变量
│   ├── Address/                       # 地址配置
│   │   └── DeviceAddressBase.cs         # 地址基类
│   ├── Data/                         # 数据点
│   │   ├── DataPoint.cs               # 数据点类
│   │   └── DataPointManager.cs         # 数据点管理器
│   ├── Manager/                       # 管理器
│   │   └── AdvancedCommunicationManager.cs
│   ├── Connection/                    # 连接实现
│   │   ├── BaseConnection.cs
│   │   ├── ModbusTcpConnection.cs
│   │   └── SiemensS7Connection.cs
│   └── Monitor/                       # 监控统计
│       ├── ConnectionStatistics.cs
│       └── ConnectionHealthCheck.cs
└── Services/                          # 服务层
```

---

## 通信模块详解

### 核心接口

#### ICommunicationConnection

定义所有通信连接的标准行为：

```csharp
public interface ICommunicationConnection : IDisposable
{
    string ConnectionName { get; }      // 连接名称（唯一标识）
    CommunicationType Type { get; }     // 通信协议类型
    bool IsConnected { get; }           // 是否已连接
    
    bool Connect();                     // 建立连接
    void Disconnect();                  // 断开连接
    bool TestConnection();              // 测试连接
    
    T? Read<T>(string address);         // 读取数据
    void Write(string address, object value);  // 写入数据
    byte[] ReadBytes(string address, ushort length);
    void WriteBytes(string address, byte[] data);
}
```

**位置**: [ICommunicationConnection.cs](file:///e:/VM/VM/VisionMaster/Communications/Core/ICommunicationConnection.cs)

#### ICommunicationManager

定义通信管理的核心功能：

```csharp
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
}
```

---

### 地址配置

#### DeviceAddressBase

设备地址配置基类，是一个**纯配置对象**，只存储"怎么读"的信息，不包含运行时数据：

```csharp
public abstract class DeviceAddressBase : BindableBase
{
    // 核心设置
    public DataValueType DataType { get; set; }      // 数据类型
    public int BitOffset { get; set; }               // 位偏移（0-7）
    public int Length { get; set; }                   // 数据长度
    public string Offset { get; set; } = "0";         // 地址偏移量
    
    // 数据转换管道
    public bool EnableConversion { get; set; }       // 启用转换
    public double? Scale { get; set; }                // 缩放系数
    public double EngineeringOffset { get; set; }      // 工程偏移
    public string? Unit { get; set; }                 // 工程单位
    public int DecimalPlaces { get; set; } = 2;       // 小数位数
    
    // 转换方法
    public object? ConvertToEngineering(object? rawValue);
    public object? ConvertToRaw(object? engineeringValue);
    public string FormatEngineeringValue(object? rawValue);
}
```

**位置**: [DeviceAddressBase.cs](file:///e:/VM/VM/VisionMaster/Communications/Address/DeviceAddressBase.cs)

**数据转换示例**:
```csharp
var address = new ModbusAddress
{
    Offset = "0",
    DataType = DataValueType.Int16,
    EnableConversion = true,
    Scale = 0.01,           // 原始值 × 0.01
    EngineeringOffset = 0,  // 不偏移
    Unit = "°C",
    DecimalPlaces = 2
};

// PLC返回1234 → 显示"12.34 °C"
var engValue = address.ConvertToEngineering(1234);  // 12.34
var formatted = address.FormatEngineeringValue(1234); // "12.34 °C"

// 用户输入 25.0°C → 转换为原始值写入PLC
var rawValue = address.ConvertToRaw(25.0); // 2500
```

**支持的协议地址类**:
- `ModbusAddress` - Modbus 协议地址
- `S7Address` - 西门子 S7 协议地址
- `MitsubishiAddress` - 三菱协议地址
- `OmronAddress` - 欧姆龙协议地址
- `AllenBradleyAddress` - AB 协议地址（标签名格式）
- `OpcUaAddress` - OPC UA 协议地址

---

### 数据点管理

#### DataPoint<T>

运行时数据容器，存储从设备读取的实际数据：

```csharp
public class DataPoint<T> : BindableBase
{
    // 只读属性
    public string Name { get; }                      // 数据点名称
    public DeviceAddressBase Address { get; }        // 关联的地址配置
    public Type DataType => typeof(T);                // 数据类型
    
    // 可绑定属性
    public T? Value { get; set; }                    // 当前值
    public DataQuality Quality { get; set; }          // 数据质量
    public DateTime Timestamp { get; set; }           // 更新时间（UTC）
    public string? ErrorMessage { get; set; }         // 错误信息
    
    // 公共方法
    public void MarkAsBad(string errorMessage);       // 标记为坏值
    public void MarkAsUncertain(string reason);       // 标记为不确定
    public object? GetEngineeringValue();             // 获取工程值
    public string GetDisplayString();                 // 获取显示字符串
    public bool IsStale(int timeoutSeconds = 60);     // 检查是否超时
    public void Reset();                              // 重置到初始状态
}
```

**位置**: [DataPoint.cs](file:///e:/VM/VM/VisionMaster/Communications/Data/DataPoint.cs)

**设计原则**:
- **纯运行时对象**：只存储"读到了什么"
- **关联地址配置**：通过 Address 属性引用 DeviceAddressBase
- **支持 WPF 绑定**：继承 BindableBase，实现 INotifyPropertyChanged

#### DataPointManager

数据点管理器，负责管理多个数据点的注册、轮询和数据关联：

```csharp
public class DataPointManager : IDisposable
{
    public event EventHandler<DataPointChangedEventArgs>? DataChanged;
    
    // 数据点注册
    public DataPoint<T> RegisterDataPoint<T>(
        string name, 
        string connectionName, 
        DeviceAddressBase address);
    
    // 数据点获取
    public DataPoint<T>? GetDataPoint<T>(string name, string connectionName);
    public IReadOnlyList<IDataPoint> GetDataPointsByConnection(string connectionName);
    
    // 轮询控制
    public void StartPolling(string connectionName, int intervalMs = 500);
    public void StopPolling(string connectionName);
    public void StopAllPolling();
    
    // 写入操作
    public async Task<bool> WriteDataPointAsync<T>(
        string name, 
        string connectionName, 
        T engineeringValue);
    
    // 缓存管理
    public void ClearCache();
    public void ClearCache(string connectionName);
}
```

**位置**: [DataPointManager.cs](file:///e:/VM/VM/VisionMaster/Communications/Data/DataPointManager.cs)

**核心职责**:
1. **数据点注册**：将名称、连接、地址关联起来
2. **轮询执行**：使用异步循环替代定时器
3. **数据缓存**：100ms 内使用缓存，减少 PLC 访问
4. **事件通知**：值变化时触发 DataChanged 事件

---

### 通讯管理器

#### AdvancedCommunicationManager

高级通信管理器，提供完整的连接管理和数据读写功能：

```csharp
public class AdvancedCommunicationManager : ICommunicationManager, IDisposable
{
    // 连接管理
    public ICommunicationConnection? GetConnection(string connectionName);
    public bool AddConnection(CommunicationConfig config);
    public bool RemoveConnection(string connectionName);
    public bool Connect(string connectionName);
    public void Disconnect(string connectionName);
    
    // 批量操作
    public void StartAll();
    public void DisconnectAll();
    
    // 数据读写
    public T? Read<T>(string connectionName, string address);
    public void Write(string connectionName, string address, object value);
    
    // 变量管理
    public void RegisterVariable(CommunicationVariable variable);
    public void UnregisterVariable(string connectionName, string variableName);
    
    // 配置持久化
    public Task SaveConfigAsync();
    public Task LoadConfigAsync();
    public Task ExportConfigAsync(string filePath);
    public Task ImportConfigAsync(string filePath);
    
    // 事件
    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;
    public event EventHandler<ConnectionErrorEventArgs>? ConnectionError;
    public event EventHandler<CommunicationDataEventArgs>? DataReceived;
}
```

**位置**: [AdvancedCommunicationManager.cs](file:///e:/VM/VM/VisionMaster/Communications/Manager/AdvancedCommunicationManager.cs)

**关键特性**:

| 特性 | 说明 |
|------|------|
| **线程安全** | 使用 ConcurrentDictionary 存储连接和变量 |
| **自动重连** | 连接断开后自动尝试重连 |
| **心跳检测** | 定时检测连接是否存活 |
| **变量轮询** | 支持注册变量并自动轮询 |
| **配置持久化** | JSON 格式保存/加载配置 |
| **反射优化** | 读取方法缓存，避免重复反射 |

---

### 轮询机制

#### 异步循环 vs 定时器

**旧方案（定时器）的问题**：
- 如果读取时间超过轮询间隔，会导致并发访问
- 异常会导致定时器停止
- 不精确的间隔控制

**新方案（异步循环）**：

```csharp
private async Task PollingLoopAsync(
    string connectionName, 
    int intervalMs, 
    CancellationToken token)
{
    // 异步循环天然保证上一次执行完成后才会开始下一次
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
            // 发生错误时等待一段时间再重试
            await Task.Delay(intervalMs, token);
        }
    }
}
```

**优势**:
- ✅ **绝对顺序执行**：下一次轮询一定在上一次完成后才开始
- ✅ **无并发问题**：即使读取耗时超过间隔也不会触发并发轮询
- ✅ **可取消**：使用 CancellationToken 可以优雅停止
- ✅ **异常安全**：异常被捕获并记录，不会导致轮询停止

#### 轮询流程

```
┌─────────────────────────────────────────────────────────────┐
│                      PollingLoopAsync                       │
│                         (异步循环)                          │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                   PollConnection                            │
│  1. 检查连接状态                                            │
│  2. 获取数据点列表副本                                      │
│  3. 遍历读取每个数据点                                      │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                   ReadSingleDataPoint                       │
│  1. 检查缓存（100ms 内使用缓存）                            │
│  2. 反射调用 Read<T> 方法                                   │
│  3. 更新缓存和数据点                                        │
│  4. 触发 DataChanged 事件                                  │
└─────────────────────────────────────────────────────────────┘
```

---

### 缓存机制

#### 数据缓存策略

```csharp
// 缓存过期时间（100ms）
private const long CacheExpirationTicks = 100 * TimeSpan.TicksPerMillisecond;

// 尝试获取缓存值
private bool TryGetCachedValue(string key, out object? value)
{
    if (_valueCache.TryGetValue(key, out var item))
    {
        long now = Stopwatch.GetTimestamp() - _startTimestamp;
        if (now - item.Timestamp < CacheExpirationTicks)
        {
            value = item.Value;
            return true;  // 命中缓存
        }
        // 过期项立即移除
        _valueCache.TryRemove(key, out _);
    }
    value = null;
    return false;
}
```

**缓存设计原则**:
1. **时间窗口**：100ms 内的重复读取使用缓存
2. **高精度计时**：使用 Stopwatch.GetTimestamp() 避免 DateTime.Now 的精度问题
3. **惰性清理**：读取时发现过期项立即清理
4. **定时清理**：每 10 秒清理一次过期缓存

**缓存 Key 格式**:
```
{ConnectionName}.{Address}
例如：PLC_1.40001
```

---

## 快速开始

### 基本使用流程

```csharp
// 1. 创建通讯管理器
var commManager = new AdvancedCommunicationManager();

// 2. 添加连接配置
var config = CommunicationConfig.CreateModbusTcp("PLC_1", "192.168.1.100", 502);
commManager.AddConnection(config);

// 3. 连接设备
commManager.Connect("PLC_1");

// 4. 创建数据点管理器
var dpManager = new DataPointManager(commManager);

// 5. 配置地址（启用数据转换）
var address = new ModbusAddress
{
    Offset = "0",
    DataType = DataValueType.Int16,
    EnableConversion = true,
    Scale = 0.01,
    Unit = "°C"
};

// 6. 注册数据点
var tempDp = dpManager.RegisterDataPoint<short>("Temperature", "PLC_1", address);

// 7. 启动轮询
dpManager.StartPolling("PLC_1", 500);

// 8. 订阅数据变化
dpManager.DataChanged += (s, e) =>
{
    Debug.WriteLine($"[{e.Timestamp:HH:mm:ss}] {e.DataPointName}: {e.NewValue}");
};

// 9. 写入数据
await dpManager.WriteDataPointAsync("Temperature", "PLC_1", (short)2500); // 25.00°C

// 10. 停止轮询
dpManager.StopPolling("PLC_1");

// 11. 断开连接
commManager.Disconnect("PLC_1");
```

### 变量注册模式

```csharp
// 注册变量（管理器会自动轮询）
var variable = new CommunicationVariable
{
    ConnectionName = "PLC_1",
    VariableName = "Temperature",
    Address = "40001",
    ValueType = typeof(short).FullName
};

commManager.RegisterVariable(variable);

// 订阅变量变化
commManager.OnVariableChanged += (s, e) =>
{
    Debug.WriteLine($"{e.VariableName}: {e.NewValue}");
};
```

### 配置持久化

```csharp
// 保存配置
await commManager.SaveConfigAsync();  // 保存到 communications.json

// 导出配置
await commManager.ExportConfigAsync("backup.json");

// 导入配置
await commManager.ImportConfigAsync("backup.json");

// 加载配置（启动时）
await commManager.LoadConfigAsync();
```

---

## API 参考

### 枚举类型

#### CommunicationType

```csharp
public enum CommunicationType
{
    ModbusTcp,       // Modbus TCP
    ModbusRtu,       // Modbus RTU (串口)
    SiemensS7,       // 西门子 S7
    Mitsubishi,      // 三菱
    Omron,           // 欧姆龙
    AllenBradley,    // AB (罗克韦尔)
    OpcUa            // OPC UA
}
```

#### DataQuality

```csharp
public enum DataQuality
{
    Good,          // 数据有效
    Bad,           // 数据无效
    Uncertain,     // 数据不确定
    NotConnected   // 未连接
}
```

#### ConnectionState

```csharp
public enum ConnectionState
{
    Disconnected,  // 未连接
    Connecting,    // 连接中
    Connected,     // 已连接
    Error          // 错误
}
```

#### DataValueType

```csharp
public enum DataValueType
{
    Boolean,       // 布尔值
    SByte,         // 有符号字节
    Byte,          // 字节
    Int16,         // 16位整数
    UInt16,        // 无符号16位整数
    Int32,         // 32位整数
    UInt32,        // 无符号32位整数
    Int64,         // 64位整数
    UInt64,        // 无符号64位整数
    Float,         // 单精度浮点
    Double,        // 双精度浮点
    String,        // 字符串
    ByteArray      // 字节数组
}
```

### 事件参数类

#### DataPointChangedEventArgs

```csharp
public class DataPointChangedEventArgs : EventArgs
{
    public string DataPointName { get; set; }     // 数据点名称
    public string ConnectionName { get; set; }    // 连接名称
    public object? NewValue { get; set; }        // 新的数据值
    public DateTime Timestamp { get; set; }      // 变化时间戳
}
```

#### ConnectionStateChangedEventArgs

```csharp
public class ConnectionStateChangedEventArgs : EventArgs
{
    public string ConnectionName { get; }         // 连接名称
    public ConnectionState OldState { get; }      // 旧状态
    public ConnectionState NewState { get; }      // 新状态
}
```

#### ConnectionErrorEventArgs

```csharp
public class ConnectionErrorEventArgs : EventArgs
{
    public string ConnectionName { get; }         // 连接名称
    public Exception? Exception { get; }         // 异常对象
}
```

---

## 最佳实践

### 1. 轮询间隔选择

| 场景 | 推荐间隔 | 说明 |
|------|----------|------|
| 高速控制 | 50-100ms | 用于实时控制场景 |
| 过程监控 | 200-500ms | 用于一般监控 |
| 数据显示 | 500-1000ms | 用于显示刷新 |
| 历史记录 | 1000-5000ms | 用于趋势记录 |

### 2. 数据转换配置

```csharp
// 温度传感器：PLC 返回 0-1000 表示 0.00-10.00°C
var tempAddress = new ModbusAddress
{
    Offset = "0",
    DataType = DataValueType.Int16,
    EnableConversion = true,
    Scale = 0.01,        // 除以100
    EngineeringOffset = 0,
    Unit = "°C",
    DecimalPlaces = 2
};

// 压力传感器：PLC 返回 0-10000 表示 0.000-10.000 MPa
var pressureAddress = new ModbusAddress
{
    Offset = "10",
    DataType = DataValueType.Int16,
    EnableConversion = true,
    Scale = 0.001,       // 除以1000
    EngineeringOffset = 0,
    Unit = "MPa",
    DecimalPlaces = 3
};
```

### 3. 异常处理

```csharp
dpManager.DataChanged += (s, e) =>
{
    try
    {
        // 更新UI
        UpdateDisplay(e.DataPointName, e.NewValue);
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"UI更新失败: {ex.Message}");
    }
};
```

### 4. 资源释放

```csharp
// 使用 using 语句确保资源释放
using (var dpManager = new DataPointManager(commManager))
{
    dpManager.StartPolling("PLC_1", 500);
    
    // 业务逻辑...
} // dpManager.Dispose() 自动调用

// 或者手动释放
dpManager.Dispose();
commManager.Dispose();
```

### 5. 性能优化

- **批量注册**：启动时注册所有数据点，避免运行时反射
- **合理缓存**：高频访问的数据使用缓存，减少 PLC 负载
- **异步操作**：写入操作使用异步方法，避免阻塞 UI
- **取消订阅**：不再需要时取消事件订阅，避免内存泄漏

---

## 更新日志

### v3.2.0 (2026-05-17)
- ✅ **修复轮询机制**：使用异步循环替代定时器，避免并发问题
- ✅ **优化缓存策略**：使用 Stopwatch.GetTimestamp() 提高计时精度
- ✅ **改进异常处理**：轮询过程中异常不会导致停止
- ✅ **完善反射缓存**：避免重复反射获取 Read<T> 方法

### v3.1.0
- ✅ **重构目录结构**：按职责分层组织代码
- ✅ **添加详细注释**：所有公开 API 都有完整的 XML 文档注释
- ✅ **完善文档**：更新 README，添加详细的 API 参考

### v3.0.0
- ✅ **数据转换管道**：Scale/Offset/Unit 自动转换
- ✅ **阈值报警机制**：四级报警支持
- ✅ **通讯质量统计**：读写成功率和响应时间监控
- ✅ **连接健康检查**：定时监控连接状态

### v2.0.0
- ✅ **新增 DataPointManager**：数据点管理和轮询
- ✅ **支持批量读写**：提高通讯效率
- ✅ **智能数据缓存**：100ms 内复用

### v1.0.0
- ✅ **初始版本发布**
- ✅ **支持 Modbus TCP/RTU、西门子 S7 协议**

---

## 许可证

本项目仅供学习和研究使用。