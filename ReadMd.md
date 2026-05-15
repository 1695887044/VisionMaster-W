# VisionMaster 技术文档

## 项目简介

VisionMaster是一个面向工业视觉检测的运动控制平台，支持多流程并发执行、可视化编程、插件扩展等高级特性。项目采用MVVM架构，通过流程建模、编译和执行三个核心阶段实现完整的视觉处理流程。

---

## 项目架构

### 技术栈

- **UI框架**: WPF (Windows Presentation Foundation)
- **架构模式**: MVVM (Model-View-ViewModel)
- **依赖注入**: Prism.Unity
- **表达式解析**: DynamicExpresso
- **通讯库**: HslCommunication
- **串口通讯**: System.IO.Ports

### 核心模块

```
VisionMaster/
├── VisionMaster/           # 主应用程序
│   ├── Models/            # 数据模型层
│   ├── ViewModels/       # 视图模型层
│   ├── Views/            # 视图层
│   ├── Services/         # 业务服务层
│   ├── Core/             # 核心基础设施
│   ├── Communications/    # 通讯模块
│   └── Helpers/          # 辅助工具
├── Shard/                # 共享接口库
│   └── Core.Interfaces/  # 核心接口定义
└── UI/                   # UI控件库
```

---

## 核心功能模块

### 1. 流程系统

#### 1.1 流程建模

流程系统支持多种类型的步骤节点：

**普通步骤 (StepModel)**
- 对应具体的视觉算子插件
- 支持静态参数配置
- 支持输入输出端口连线

**控制流步骤**
- `ConditionStep`: 条件分支（If-Else）
- `WhileStep`: 条件循环
- `ForStep`: 计次循环
- `BreakStep`: 跳出循环
- `ContinueStep`: 跳过当前迭代
- `ReturnStep`: 终止流程

#### 1.2 流程编译

使用 `FlowCompiler` 将流程图编译为可执行的编译树：

```
输入: StepModel 列表
    ↓
CompileSteps(): 实例化插件，创建编译节点树
    ↓
LinkPorts(): 解析连线引用，绑定端口
    ↓
输出: CompiledFlow
```

**表达式编译**: 使用 DynamicExpresso 库编译条件表达式，支持算术运算、逻辑运算和函数调用。

#### 1.3 流程执行

`FlowSession` 管理流程的运行时状态：

```csharp
public class FlowSession
{
    public string SessionID { get; }
    public string FlowName { get; }
    public CompiledFlow ExecutionEngine { get; set; }
    public SessionState State { get; set; }
    public bool IsRunning { get; set; }
    public CancellationTokenSource CancellationTokenSource { get; set; }
    public ManualResetEventSlim PauseLock { get; }  // 暂停控制
}
```

**执行模式**:
- 连续运行模式: 循环执行流程
- 单次运行模式: 执行一次后停止

---

### 2. 通讯系统

通讯系统基于 HslCommunication 库实现，支持多种工业通讯协议。

#### 2.1 支持的通讯协议

| 协议 | 状态 | 说明 |
|------|------|------|
| Modbus TCP | ✅ 已实现 | 基于TCP的Modbus通讯 |
| Modbus RTU | 🔜 待实现 | 基于串口的Modbus通讯 |
| Siemens S7 | ✅ 已实现 | 西门子S7系列PLC通讯 |
| 自由协议 | 🔜 待实现 | 自定义协议支持 |

#### 2.2 架构设计

```
Communications/
├── CommunicationType.cs              # 枚举定义
├── CommunicationConfig.cs             # 配置模型
├── CommunicationEventArgs.cs          # 事件参数
├── CommunicationVariable.cs           # 通讯变量
├── ICommunicationConnection.cs        # 连接接口
├── ICommunicationManager.cs          # 管理器接口
├── ModbusTcpConnection.cs            # Modbus TCP实现
├── SiemensS7Connection.cs            # S7连接实现
└── AdvancedCommunicationManager.cs    # 管理器实现
```

#### 2.3 核心接口

**ICommunicationConnection**
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

**ICommunicationManager**
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

#### 2.4 配置说明

```csharp
public class CommunicationConfig
{
    public string ConnectionName { get; set; }      // 连接名称
    public CommunicationType Type { get; set; }    // 通讯类型
    public string IpAddress { get; set; }           // IP地址
    public int Port { get; set; }                   // 端口号
    public int ConnectionTimeout { get; set; }      // 连接超时(ms)
    public byte Station { get; set; }                // 站号
    public string SerialPort { get; set; }          // 串口名称
    public int BaudRate { get; set; }               // 波特率
    public string S7CpuType { get; set; }          // S7 CPU类型
    public int ReadCycleMs { get; set; }           // 读取周期(ms)
    public bool IsEnabled { get; set; }            // 是否启用
    public bool IsVisible { get; set; }            // 是否显示
}
```

**支持的S7 CPU类型**: Smart200, S1200, S1500, S300, S400

#### 2.5 变量管理

```csharp
public class CommunicationVariable
{
    public string ConnectionName { get; set; }
    public string VariableName { get; set; }
    public string Address { get; set; }
    public Type ValueType { get; set; }
    public VariableAccessMode AccessMode { get; set; }  // ReadOnly / ReadWrite
    public object? CurrentValue { get; }
    public DateTime LastUpdateTime { get; }

    public event EventHandler<object?>? ValueChanged;
}
```

#### 2.6 使用示例

**连接管理**
```csharp
var manager = new AdvancedCommunicationManager();

var config = new CommunicationConfig
{
    ConnectionName = "PLC1",
    Type = CommunicationType.SiemensS7,
    IpAddress = "192.168.1.100",
    Port = 102,
    S7CpuType = "S1200",
    ReadCycleMs = 1000
};

manager.AddConnection(config);
manager.StartAll();
```

**变量读写**
```csharp
// 注册变量
var variable = new CommunicationVariable
{
    ConnectionName = "PLC1",
    VariableName = "Temperature",
    Address = "DB1.DBD0",
    ValueType = typeof(float),
    AccessMode = VariableAccessMode.ReadOnly
};
manager.RegisterVariable(variable);

// 订阅变化事件
variable.ValueChanged += (sender, newValue) =>
{
    Console.WriteLine($"变量更新: {newValue}");
};

// 手动读写
var value = manager.ReadVariable<float>("PLC1", "DB1.DBD0");
manager.WriteVariable("PLC1", "DB1.DBD0", 25.5f);
```

**错误处理**
```csharp
manager.OnCommunicationError += (sender, args) =>
{
    Console.WriteLine($"通讯错误 [{args.ConnectionName}]: {args.ErrorMessage}");
};
```

#### 2.7 地址格式

**Modbus地址**
```
位操作: {站号}.{功能码}.{地址}
示例: 1.01.00001, 1.02.00010

寄存器: {站号}.{功能码}.{地址}
示例: 1.03.00001, 1.04.00010
```

**Siemens S7地址**
```
DB块数据: DB{块号}.{类型}{偏移}
示例: DB1.DBD0, DB1.DBW0, DB1.DBB0, DB1.DBX0.0

输入输出: I{地址}, Q{地址}, M{地址}
示例: I0.0, Q1.0, M0.0, IB0, IW0, ID0
```

#### 2.8 性能优化

**批量读取**: 系统在每个读取周期内一次性读取所有已注册的通讯变量，减少网络通讯次数。

**写入队列**: 写入操作通过队列异步处理，避免阻塞主线程。

**连接复用**: 同一个连接的多个变量共享同一个连接对象，减少TCP连接建立开销。

---

### 3. 插件系统

#### 3.1 插件接口

```csharp
public interface IVisionPlugin
{
    string InstanceName { get; set; }
    Dictionary<string, IInputPort> Inputs { get; }
    Dictionary<string, IOutputPort> Outputs { get; }
    void Execute(IExecutionContext context);
}
```

#### 3.2 端口定义

**输入端口 (IInputPort)**
- 支持静态参数值
- 支持连线绑定
- 支持数组索引访问

**输出端口 (IOutputPort)**
- 暴露计算结果
- 供下游步骤使用

#### 3.3 执行流程

```
编译阶段
    ↓
1. 反射实例化插件
2. 灌入静态参数
3. 解析连线引用
    ↓
运行阶段
    ↓
4. 执行插件逻辑
5. 暴露输出结果
```

---

### 4. 状态管理

#### 4.1 会话状态机

```
     Idle
       ↓
   Running ←──────────────┐
      │                   │
      ├─→ Paused ─────────┤
      │       ↓            │
      │   Running         │
      │                   │
      ↓                   │
   Stopped ───────────────┤
      ↓                   │
   Faulted ───────────────┘
```

#### 4.2 状态变更通知

```csharp
public event EventHandler<SessionStateChangedEventArgs> SessionStateChanged;
```

---

### 5. 资源管理

#### 5.1 资源锁服务

使用 `SemaphoreSlim` 实现资源互斥访问：

```csharp
public interface IResourceLockService
{
    Task<IDisposable> AcquireAsync(string resourceId, CancellationToken token = default);
    bool IsLocked(string resourceId);
}
```

#### 5.2 线程安全

| 组件 | 同步方式 |
|------|----------|
| `FlowSession` | `_stateLock` 锁保护状态属性 |
| `RuntimeManager` | `BindingOperations.EnableCollectionSynchronization` |
| `ResourceLockService` | `SemaphoreSlim` 实现资源互斥 |

---

### 6. 性能监控

```csharp
// 记录会话执行
_performanceMonitor.RecordSessionStart(sessionId, flowName);
_performanceMonitor.RecordSessionEnd(sessionId);

// 记录节点执行时间
_performanceMonitor.StartNodeTiming(nodeId, nodeName, sessionId);
// ...执行节点...
_performanceMonitor.EndNodeTiming(nodeId);

// 获取统计信息
var stats = _performanceMonitor.GetSessionStats(sessionId);
Console.WriteLine($"执行次数: {stats.TotalExecutions}, 平均耗时: {stats.AverageDurationMs}ms");
```

---

### 7. 核心基础设施

#### 7.1 线程安全集合

- `BlockingQueue<T>`: 线程安全的阻塞队列
- `LockFreeRingBuffer<T>`: 无锁环形缓冲区
- `ObjectPool<T>`: 对象池，减少GC压力

#### 7.2 高性能组件

- `HighPrecisionTimer`: 高精度定时器
- `DebounceThrottle`: 防抖节流
- `RetryPolicy`: 重试策略

#### 7.3 异常处理

- `GlobalExceptionHandler`: 全局异常捕获
- `HardwareHeartbeatMonitor`: 硬件心跳监控

---

## 设计亮点

1. **编译与执行分离**: 流程图先编译为中间表示，执行时无需重复解析
2. **表达式安全**: 只允许值类型和字符串用于表达式计算，防止代码注入
3. **数组索引支持**: 通过 `ArrayIndexProxyPort` 支持数组元素访问
4. **高性能通讯**: 批量读取和写入队列机制，提高通讯效率
5. **插件化架构**: 灵活扩展视觉算子功能
6. **状态机管理**: 完善的会话状态管理和通知机制
7. **资源保护**: SemaphoreSlim 实现资源互斥访问

---

## 开发规范

### 命名规范

| 类型 | 命名规则 | 示例 |
|------|----------|------|
| 接口 | I + PascalCase | `IFlowEngine` |
| 类 | PascalCase | `FlowEngineService` |
| 方法 | PascalCase | `RunSessionAsync` |
| 属性 | PascalCase | `ActiveSessionCount` |
| 私有字段 | _camelCase | `_runtimeManager` |
| 枚举 | PascalCase | `FlowPriority` |
| 常量 | UPPER_SNAKE_CASE | `MAX_ITERATIONS` |

### 代码注释

- 使用 XML 文档注释
- 接口和类提供 `<summary>` 说明
- 方法提供 `<param>` 和 `<returns>`
- 复杂逻辑添加行内注释

### 架构原则

- **SOLID原则**: 单一职责、开闭原则、里氏替换、接口隔离、依赖倒置
- **MVVM模式**: 视图与业务逻辑分离
- **依赖注入**: 使用 Prism Unity 容器管理依赖
- **高内聚低耦合**: 模块职责清晰，依赖关系简单

---

## 未来扩展

### 计划功能

1. **Modbus RTU通讯支持**: 实现基于串口的Modbus通讯
2. **自由协议支持**: 提供自定义协议解析器接口
3. **数据缓存**: 减少通讯次数，提高响应速度
4. **通讯监控**: 实时显示连接状态和通讯质量
5. **自动重连**: 网络异常时自动尝试重连
6. **数据加密**: 通讯数据加密传输

### 优化方向

- 性能监控增强，统计读写成功率和响应时间
- 连接池管理，支持负载均衡
- 分布式架构，支持多机协同
- Web界面，支持远程监控

---

## 技术支持

如有问题，请检查：
1. 网络连接是否正常
2. IP地址和端口是否正确
3. 设备是否支持对应的通讯协议
4. 通讯地址格式是否正确
5. 防火墙设置是否允许通讯

---

## 版本信息

- 项目版本: 1.0.0
- 文档更新: 2026-05-15
- 核心框架: .NET 8.0
- UI框架: WPF
