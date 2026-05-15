# VisionMaster 通讯系统

## 概述

VisionMaster通讯系统基于HslCommunication库实现高性能的工业设备通讯，支持Modbus TCP、Modbus RTU、Siemens S7和自由协议等多种通讯方式。

### 主要特性

- **多协议支持**：Modbus TCP、Modbus RTU、Siemens S7、自由协议
- **变量权限控制**：支持只读和读写两种访问模式
- **高性能读取**：批量循环读取所有已注册变量
- **写入触发机制**：本地变量变更自动触发通讯写入
- **连接池管理**：统一管理多个通讯连接
- **错误处理**：完善的通讯错误事件通知

## 架构设计

### 核心组件

```
Communications/
├── CommunicationManager.cs        # 基础通讯类定义
│   ├── 枚举定义
│   │   ├── CommunicationType      # 通讯类型枚举
│   │   └── VariableAccessMode     # 变量访问权限枚举
│   ├── 配置模型
│   │   └── CommunicationConfig    # 通讯连接配置
│   ├── 连接接口
│   │   └── ICommunicationConnection # 连接对象接口
│   ├── 连接实现
│   │   ├── ModbusTcpConnection    # Modbus TCP连接
│   │   └── SiemensS7Connection    # Siemens S7连接
│   └── 事件参数
│       ├── CommunicationErrorEventArgs
│       ├── VariableChangedEventArgs
│       └── VariableWriteRequest
│
└── AdvancedCommunicationManager.cs # 高级通讯管理器
    └── ICommunicationManager      # 通讯管理接口
```

### 核心接口

#### ICommunicationConnection

```csharp
public interface ICommunicationConnection : IDisposable
{
    string ConnectionName { get; }
    CommunicationType Type { get; }
    bool IsConnected { get; }
    bool Connect();
    void Disconnect();
    T? Read<T>(string address);
    void Write(string address, object value);
    byte[] ReadBytes(string address, ushort length);
    void WriteBytes(string address, byte[] data);
}
```

#### ICommunicationManager

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
    void TriggerWrite(string connectionName, string address, object value, Type valueType);
}
```

## 通讯配置

### CommunicationConfig配置项

```csharp
[Serializable]
public class CommunicationConfig
{
    public string ConnectionName { get; set; }      // 连接名称
    public CommunicationType Type { get; set; }    // 通讯类型
    public string IpAddress { get; set; }           // IP地址
    public int Port { get; set; }                   // 端口号
    public int ConnectionTimeout { get; set; }     // 连接超时(ms)
    public byte Station { get; set; }               // 站号/从站地址
    public string SerialPort { get; set; }          // 串口名称
    public int BaudRate { get; set; }               // 波特率
    public string S7CpuType { get; set; }          // S7 CPU类型
    public int ReadCycleMs { get; set; }           // 读取周期(ms)
    public bool IsEnabled { get; set; }            // 是否启用
    public bool IsVisible { get; set; }             // 是否在界面显示
}
```

### 支持的通讯类型

| 类型 | 值 | 说明 |
|------|-----|------|
| ModbusTcp | 0 | Modbus TCP协议 |
| ModbusRtu | 1 | Modbus RTU协议(待实现) |
| SiemensS7 | 2 | 西门子S7协议 |
| FreeProtocol | 3 | 自由协议(待实现) |

### 支持的S7 CPU类型

- Smart200: S7-200 Smart
- S1200: S7-1200
- S1500: S7-1500
- S300: S7-300
- S400: S7-400

## 变量管理

### 通讯变量定义

```csharp
public class CommunicationVariable
{
    public string ConnectionName { get; set; }  // 所属连接名称
    public string VariableName { get; set; }    // 变量名称
    public string Address { get; set; }         // 通讯地址
    public Type ValueType { get; set; }         // 值类型
    public VariableAccessMode AccessMode { get; set; }  // 访问模式
    public object? CurrentValue { get; }        // 当前值
    public DateTime LastUpdateTime { get; }     // 最后更新时间
    
    public event EventHandler<object?>? ValueChanged;
}
```

### 变量访问权限

| 权限 | 值 | 说明 |
|------|-----|------|
| ReadOnly | 0 | 只读，不能写入 |
| ReadWrite | 1 | 可读写 |

### 支持的数据类型

| 类型 | C#类型 | 说明 |
|------|--------|------|
| 布尔 | bool | 位变量 |
| 字节 | byte | 单字节 |
| 短整型 | short/Int16 | 16位整数 |
| 无符号短整型 | ushort/UInt16 | 16位无符号整数 |
| 整型 | int/Int32 | 32位整数 |
| 无符号整型 | uint/UInt32 | 32位无符号整数 |
| 长整型 | long/Int64 | 64位整数 |
| 无符号长整型 | ulong/UInt64 | 64位无符号整数 |
| 单精度浮点 | float/Single | 32位浮点数 |
| 双精度浮点 | double | 64位浮点数 |

## 使用方法

### 1. 创建和管理连接

```csharp
// 创建通讯管理器
var manager = new AdvancedCommunicationManager();

// 创建连接配置
var config = new CommunicationConfig
{
    ConnectionName = "PLC1",
    Type = CommunicationType.SiemensS7,
    IpAddress = "192.168.1.100",
    Port = 102,
    S7CpuType = "S1200",
    ConnectionTimeout = 3000,
    ReadCycleMs = 1000,
    IsEnabled = true
};

// 添加连接
if (manager.AddConnection(config))
{
    Console.WriteLine("连接添加成功");
}

// 获取连接
var connection = manager.GetConnection("PLC1");
if (connection != null)
{
    // 测试连接
    if (connection.Connect())
    {
        Console.WriteLine("连接成功");
    }
}
```

### 2. 变量注册和读取

```csharp
// 创建通讯变量
var variable = new CommunicationVariable
{
    ConnectionName = "PLC1",
    VariableName = "Temperature",
    Address = "DB1.DBD0",  // S7地址格式
    ValueType = typeof(float),
    AccessMode = VariableAccessMode.ReadOnly
};

// 注册变量
manager.RegisterVariable(variable);

// 订阅变量变化事件
variable.ValueChanged += (sender, newValue) =>
{
    Console.WriteLine($"变量更新: {newValue}");
};

// 直接读取变量
var value = manager.ReadVariable<float>("PLC1", "DB1.DBD0");
```

### 3. 写入变量

```csharp
// 直接写入
manager.WriteVariable("PLC1", "DB1.DBD0", 25.5f);

// 触发写入(加入写入队列)
manager.TriggerWrite("PLC1", "DB1.DBD0", 25.5f, typeof(float));
```

### 4. 启动和停止通讯

```csharp
// 启动所有连接和读取循环
manager.StartAll();

// 停止所有通讯
manager.StopAll();
```

### 5. 错误处理

```csharp
// 订阅通讯错误事件
manager.OnCommunicationError += (sender, args) =>
{
    Console.WriteLine($"通讯错误 [{args.ConnectionName}]: {args.ErrorMessage}");
    if (args.Exception != null)
    {
        Console.WriteLine($"异常: {args.Exception.Message}");
    }
};

// 订阅变量变化事件
manager.OnVariableChanged += (sender, args) =>
{
    Console.WriteLine($"变量变化 [{args.ConnectionName}][{args.Address}]: {args.NewValue}");
};
```

## Modbus地址格式

### 位操作

```
格式: {站号}.{功能码}.{地址}{偏移}
示例: 1.01.00001   - 站号1, 功能码01, 地址1
      1.02.00010   - 站号1, 功能码02, 地址10
```

### 寄存器操作

```
格式: {站号}.{功能码}.{地址}
示例: 1.03.00001   - 站号1, 功能码03, 寄存器地址1
      1.04.00010   - 站号1, 功能码04, 寄存器地址10
```

### 常用功能码

| 功能码 | 名称 | 操作类型 |
|--------|------|----------|
| 01 | Read Coils | 读线圈(位) |
| 02 | Read Discrete Inputs | 读离散输入(位) |
| 03 | Read Holding Registers | 读保持寄存器(字) |
| 04 | Read Input Registers | 读输入寄存器(字) |
| 05 | Write Single Coil | 写单个线圈(位) |
| 06 | Write Single Register | 写单个寄存器(字) |
| 15 | Write Multiple Coils | 写多个线圈(位) |
| 16 | Write Multiple Registers | 写多个寄存器(字) |

## Siemens S7地址格式

### DB块数据

```
格式: DB{数据块号}.{数据类型}{偏移}
示例: DB1.DBD0      - DB块1, 双字(DWord), 偏移0
      DB1.DBD4      - DB块1, 双字(DWord), 偏移4
      DB1.DBW0      - DB块1, 字(Word), 偏移0
      DB1.DBB0      - DB块1, 字节(Byte), 偏移0
      DB1.DBX0.0    - DB块1, 位, 字节0位0
```

### 数据类型

| 类型 | 说明 | 大小 |
|------|------|------|
| DBX | 位 | 1 bit |
| DBB | 字节 | 8 bits |
| DBW | 字 | 16 bits |
| DBD | 双字 | 32 bits |

### 输入输出

```
格式: I{地址}       - 输入
     Q{地址}       - 输出
     M{地址}       - 标志位
示例: I0.0          - 输入位0.0
     Q1.0          - 输出位1.0
     M0.0          - 标志位0.0
     IB0           - 输入字节0
     IW0           - 输入字0
     ID0           - 输入双字0
```

## 性能优化

### 1. 批量读取机制

系统采用批量读取方式，在每个读取周期内一次性读取所有已注册的通讯变量，减少网络通讯次数。

```csharp
// 读取周期配置
config.ReadCycleMs = 1000;  // 每秒读取一次

// 系统内部实现
await Task.Run(() =>
{
    lock (_batchReadLock)
    {
        foreach (var variable in variables)
        {
            // 批量读取所有变量
            var value = ReadVariableByType(connection, variable.Address, variable.ValueType);
            variable.UpdateValue(value);
        }
    }
});
```

### 2. 写入队列机制

写入操作通过队列异步处理，避免阻塞主线程：

```csharp
// 触发写入(非阻塞)
manager.TriggerWrite(connectionName, address, value, valueType);

// 后台处理
private async Task ProcessWriteQueueAsync()
{
    foreach (var connectionName in _writeQueue.Keys)
    {
        while (queue.TryDequeue(out var request))
        {
            await Task.Run(() => WriteVariable(request.ConnectionName, request.Address, request.Value));
        }
    }
}
```

### 3. 连接复用

同一个连接的多个变量共享同一个连接对象，减少TCP连接建立开销。

## 错误处理策略

### 1. 连接失败

```csharp
public bool Connect()
{
    var result = _device.ConnectServer();
    _isConnected = result.IsSuccess;
    if (!result.IsSuccess)
    {
        OnCommunicationError?.Invoke(this, new CommunicationErrorEventArgs
        {
            ConnectionName = ConnectionName,
            ErrorMessage = $"连接失败: {result.Message}",
            Exception = new Exception(result.Message)
        });
    }
    return result.IsSuccess;
}
```

### 2. 读取失败

```csharp
try
{
    var value = ReadVariableByType(connection, variable.Address, variable.ValueType);
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
```

### 3. 写入失败

```csharp
try
{
    connection.Write(address, value);
}
catch (Exception ex)
{
    OnCommunicationError?.Invoke(this, new CommunicationErrorEventArgs
    {
        ConnectionName = connectionName,
        ErrorMessage = $"写入变量 {address} 失败: {ex.Message}",
        Exception = ex
    });
}
```

## 未来扩展

### 待实现功能

1. **Modbus RTU支持**
   - 需要添加ModbusRtuConnection类
   - 使用HslCommunication.ModBus.ModbusRtu

2. **自由协议支持**
   - 需要自定义协议解析器
   - 提供字节数组读写接口

3. **连接状态监控**
   - 自动重连机制
   - 连接状态指示器

4. **性能监控**
   - 通讯响应时间统计
   - 读写成功率统计

### 扩展建议

1. 添加连接池管理，支持负载均衡
2. 实现数据缓存，减少通讯次数
3. 添加数据转换器，支持自定义数据类型
4. 实现数据加密，保证通讯安全
5. 添加日志记录，便于故障排查

## 注意事项

1. **连接超时**：建议设置为3000ms左右，避免长时间等待
2. **读取周期**：根据实际需求设置，过短会增加网络负载，过长会影响数据实时性
3. **错误处理**：务必订阅OnCommunicationError事件，及时处理通讯异常
4. **资源释放**：使用完管理器后调用StopAll()释放资源
5. **线程安全**：批量读取使用lock保证线程安全

## 技术支持

如有问题，请检查：
1. 网络连接是否正常
2. IP地址和端口是否正确
3. 设备是否支持对应的通讯协议
4. 通讯地址格式是否正确
5. 防火墙设置是否允许通讯
