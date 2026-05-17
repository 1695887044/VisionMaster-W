# VisionMaster

工业视觉检测与流程自动化平台 - 基于 WPF 的现代化机器视觉软件框架

## 目录

- [项目概述](#项目概述)
- [系统架构](#系统架构)
- [核心模块](#核心模块)
  - [通信模块](#通信模块)
  - [流程引擎](#流程引擎)
  - [插件系统](#插件系统)
  - [全局变量系统](#全局变量系统)
  - [性能监控](#性能监控)
- [快速开始](#快速开始)
- [API 参考](#api-参考)
- [扩展开发](#扩展开发)

---

## 项目概述

VisionMaster 是一个功能完整的工业视觉检测与流程自动化平台，采用现代化的 WPF + Prism 架构设计，支持：

- **多协议工业通讯**：Modbus TCP/RTU、西门子 S7 系列 PLC
- **可视化流程编排**：拖拽式流程设计，支持条件分支、循环等控制结构
- **插件化架构**：灵活的插件系统，支持自定义功能扩展
- **实时性能监控**：CPU、内存、线程等系统资源监控
- **全局变量管理**：支持本地变量和通讯变量的统一管理

### 技术栈

| 技术 | 版本 | 用途 |
|------|------|------|
| .NET | 9.0 | 运行时框架 |
| WPF | - | UI 框架 |
| Prism | 9.0 | MVVM 框架 |
| HslCommunication | - | 工业通讯库 |
| NLog | 6.0 | 日志系统 |
| DynamicExpresso | 2.19 | 表达式解析 |

---

## 系统架构

```
VisionMaster/
├── App.xaml.cs                 # 应用程序入口，依赖注入配置
├── Shell.xaml                  # 主窗口
├── ShellViewModel.cs           # 主视图模型
├── Communications/             # 通讯模块
│   ├── BaseConnection.cs       # 连接泛型基类
│   ├── ModbusTcpConnection.cs  # Modbus TCP 连接
│   ├── SiemensS7Connection.cs  # 西门子 S7 连接
│   ├── SerialConnection.cs     # 串口连接
│   ├── AdvancedCommunicationManager.cs  # 通讯管理器
│   └── ConnectionFactory.cs    # 连接工厂
├── Services/                   # 服务层
│   ├── FlowCompiler.cs         # 流程编译器
│   ├── FlowEngineService.cs    # 流程执行引擎
│   ├── PluginService.cs        # 插件服务
│   └── SolutionService.cs      # 解决方案服务
├── Models/                     # 数据模型
│   ├── FlowModel.cs            # 流程模型
│   ├── GlobalVariableModel.cs  # 全局变量模型
│   ├── ProcessStep/            # 步骤模型
│   └── Compileds/              # 编译后节点
├── Core/                       # 核心组件
│   ├── PerformanceMonitor.cs   # 性能监控
│   ├── ObjectPool.cs           # 对象池
│   └── RetryPolicy.cs          # 重试策略
├── ViewModels/                 # 视图模型
└── Views/                      # 视图
```

---

## 核心模块

### 通信模块

工业通讯模块提供统一的设备通讯接口，支持多种工业协议。

#### 支持的协议

| 协议 | 类型 | 说明 |
|------|------|------|
| Modbus TCP | `CommunicationType.ModbusTcp` | TCP/IP 上的 Modbus 协议 |
| Modbus RTU | `CommunicationType.ModbusRtu` | 串口 Modbus 协议 |
| 西门子 S7 | `CommunicationType.SiemensS7` | S7-200Smart/S7-1200/S7-1500 等 |

#### 配置类层次结构

```
ConnectionConfigBase (抽象基类)
├── EthernetConfigBase (以太网配置基类)
│   ├── ModbusTcpConfig (Modbus TCP 配置)
│   └── SiemensS7Config (西门子 S7 配置)
└── SerialConfig (串口配置)
```

#### 创建连接配置

```csharp
// Modbus TCP 配置
var modbusConfig = CommunicationConfig.CreateModbusTcp(
    connectionName: "PLC_1",
    ipAddress: "192.168.1.100",
    port: 502
);

// 西门子 S7 配置
var s7Config = CommunicationConfig.CreateSiemensS7(
    connectionName: "S7_PLC",
    ipAddress: "192.168.1.10",
    cpuType: "S1200"  // Smart200/S1200/S1500/S300/S400
);

// 串口 RTU 配置
var rtuConfig = CommunicationConfig.CreateModbusRtu(
    connectionName: "Sensor",
    portName: "COM3",
    baudRate: 115200
);
```

#### 使用通讯管理器

```csharp
// 创建管理器
var manager = new AdvancedCommunicationManager();

// 添加连接
manager.AddConnection(modbusConfig);
manager.AddConnection(s7Config);
manager.AddConnection(rtuConfig);

// 连接所有设备
manager.ConnectAll();

// 读取数据
var value = manager.Read<short>("PLC_1", "HR0");      // 读取保持寄存器
var coil = manager.Read<bool>("PLC_1", "C0");         // 读取线圈
var floatValue = manager.Read<float>("PLC_1", "HR10"); // 读取浮点数

// 写入数据
manager.Write("PLC_1", "HR0", (short)100);
manager.Write("PLC_1", "C0", true);

// 断开连接
manager.DisconnectAll();

// 保存配置到文件
await manager.SaveConfigAsync("communications.json");

// 从文件加载配置
await manager.ImportConfigAsync("communications.json");
```

#### 连接工厂模式

```csharp
// 获取工厂管理器单例
var factoryManager = ConnectionFactoryManager.Instance;

// 获取特定协议的工厂
var modbusFactory = factoryManager.GetFactory(CommunicationType.ModbusTcp);

// 通过工厂创建连接
var connection = factoryManager.CreateConnection(config);

// 查看所有支持的协议
foreach (var type in factoryManager.SupportedTypes)
{
    Console.WriteLine($"支持协议: {type}");
}
```

#### 事件处理

```csharp
// 连接状态变化事件
manager.ConnectionStateChanged += (sender, e) =>
{
    Console.WriteLine($"[{e.ConnectionName}] {e.OldState} -> {e.NewState}");
};

// 连接错误事件
manager.ConnectionError += (sender, e) =>
{
    Console.WriteLine($"[{e.ConnectionName}] 错误: {e.Exception.Message}");
};

// 数据接收事件
manager.DataReceived += (sender, e) =>
{
    Console.WriteLine($"[{e.ConnectionName}] 地址:{e.Address} 值:{e.Value}");
};
```

#### 配置属性详解

**ModbusTcpConfig**
| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| IpAddress | string | "127.0.0.1" | PLC IP 地址 |
| Port | int | 502 | Modbus 端口 |
| TimeoutMs | int | 3000 | 通讯超时(毫秒) |
| RetryCount | int | 3 | 重试次数 |
| EnableKeepAlive | bool | true | 启用 KeepAlive |

**SiemensS7Config**
| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| IpAddress | string | "127.0.0.1" | PLC IP 地址 |
| Port | int | 102 | S7 协议端口 |
| S7CpuType | string | "S1200" | CPU 类型 |
| Rack | byte | 0 | 机架号 |
| Slot | byte | 1 | 插槽号 |

**SerialConfig**
| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| PortName | string | "COM1" | 串口名称 |
| BaudRate | int | 9600 | 波特率 |
| DataBits | int | 8 | 数据位 |
| Parity | ParityMode | None | 校验位 |
| StopBits | StopBitsMode | One | 停止位 |

---

### 流程引擎

流程引擎负责流程的编译、执行和状态管理。

#### 核心组件

- **FlowCompiler** - 将流程图编译为可执行的节点树
- **FlowEngineService** - 管理流程的执行生命周期
- **CompiledNode** - 编译后的执行节点
- **ExecutionContext** - 执行上下文，包含变量、日志等

#### 流程模型

```csharp
public class FlowModel
{
    public string FlowID { get; set; }           // 流程唯一标识
    public string FlowName { get; set; }         // 流程名称
    public string Description { get; set; }      // 流程描述
    public int Version { get; set; }             // 版本号(自动递增)
    public bool IsEnabled { get; set; }          // 是否启用
    public FlowTriggerMode TriggerMode { get; }  // 触发模式
    public ObservableCollection<StepModel> Steps { get; } // 步骤集合
}
```

#### 触发模式

| 模式 | 说明 |
|------|------|
| `Single` | 单次执行，手动触发 |
| `Continuous` | 连续运行，循环执行 |
| `External` | 外部触发，等待 IO 信号 |
| `Timer` | 定时触发，周期执行 |

#### 步骤类型

```csharp
// 步骤基类
public abstract class StepModel : BindableBase
{
    public Guid StepID { get; set; }
    public string StepName { get; set; }
    public string PluginName { get; set; }
    public string Description { get; set; }
    public bool IsDisEnable { get; set; }
    public StepState State { get; set; }
}

// 派生类型
- ActionStep      // 动作步骤
- ConditionStep   // 条件分支
- WhileStep       // While 循环
- ForStep         // For 循环
- BreakStep       // 跳出循环
- ContinueStep    // 继续循环
- ReturnStep      // 返回
```

#### 编译流程

```csharp
// 编译单个流程
var compiler = container.Resolve<FlowCompiler>();
var result = compiler.Compile(flow.Steps, flow.FlowName);

if (result.Success)
{
    var compiledFlow = result.Data;  // 编译后的可执行流程
}
else
{
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"编译错误: {error}");
    }
}
```

#### 执行流程

```csharp
// 获取流程引擎
var engine = container.Resolve<IFlowEngine>();

// 创建会话
var session = new FlowSession
{
    FlowName = "MainFlow",
    ExecutionEngine = compiledFlow,
    CompiledVersion = flow.Version
};

// 单次执行
await engine.RunSessionOnceAsync(session);

// 连续执行
await engine.RunSessionAsync(session);

// 停止执行
engine.StopAll();
```

---

### 插件系统

插件系统采用接口抽象设计，支持动态加载和扩展。

#### 插件接口

```csharp
public interface IVisionPlugin
{
    string PluginID { get; set; }
    string InstanceName { get; set; }
    IReadOnlyDictionary<string, IInputPort> Inputs { get; }
    IReadOnlyDictionary<string, IOutputPort> Outputs { get; }
    
    bool Execute(IExecutionContext context);
    void Initialize();
    void Dispose();
}
```

#### 插件基类

```csharp
public abstract class VisionPluginBase : IVisionPlugin
{
    public string PluginID { get; set; }
    public string InstanceName { get; set; }
    
    // 输入输出端口管理
    protected Dictionary<string, IInputPort> _inputs = new();
    protected Dictionary<string, IOutputPort> _outputs = new();
    
    // 核心执行方法
    public abstract void RunAlgorithm(IExecutionContext context);
    
    public bool Execute(IExecutionContext context)
    {
        RunAlgorithm(context);
        return true;
    }
}
```

#### 内置插件列表

| 插件 | 类别 | 说明 |
|------|------|------|
| DelayPlugin | 常用工具 | 延时等待 |
| CounterPlugin | 常用工具 | 计数器 |
| StopwatchPlugin | 常用工具 | 计时器 |
| MathCalculationPlugin | 数据处理 | 数学运算 |
| DataConversionPlugin | 数据处理 | 数据类型转换 |
| ComparisonPlugin | 逻辑运算 | 数值比较 |
| LogicOperationPlugin | 逻辑运算 | 逻辑运算 |
| ArrayStatisticsPlugin | 数组处理 | 数组统计 |
| ArrayElementPlugin | 数组处理 | 数组元素操作 |
| RandomPlugin | 数据生成 | 随机数生成 |
| RangeCheckPlugin | 数据校验 | 范围检查 |
| StringFormatPlugin | 字符串 | 字符串格式化 |
| FileLoggerPlugin | 文件操作 | 文件日志记录 |
| VariableAssignmentPlugin | 变量操作 | 变量赋值 |
| VariableDefinitionPlugin | 变量操作 | 变量定义 |

#### 开发自定义插件

```csharp
[Display(
    Name = "自定义插件",
    GroupName = "自定义",
    Description = "这是一个自定义插件示例",
    ShortName = "\uf0e7"
)]
public class CustomPlugin : VisionPluginBase
{
    // 定义输入端口
    public InputPort<int> InputValue { get; } = 
        new InputPort<int>("Input", 0, "输入值");
    
    // 定义输出端口
    public OutputPort<int> OutputValue { get; } = 
        new OutputPort<int>("Output", "输出值");
    
    public override void RunAlgorithm(IExecutionContext context)
    {
        int input = InputValue.GetTypedValue();
        
        // 执行处理逻辑
        int result = input * 2;
        
        // 设置输出值
        OutputValue.Value = result;
        
        // 记录日志
        context.Logger.Info($"处理完成: {input} -> {result}");
    }
    
    public override void Initialize() { }
    public override void Dispose() { }
}
```

---

### 全局变量系统

全局变量系统支持本地变量和通讯变量的统一管理。

#### 变量模型

```csharp
public class GlobalVariableModel : BindableBase, IOutputPort
{
    public string Name { get; set; }              // 变量名称
    public VariableType VariableType { get; set; } // 变量类型
    public string? ConnectionName { get; set; }   // 关联连接名
    public string? Address { get; set; }          // 通讯地址
    public Type DataType { get; set; }            // 数据类型
    public object? Value { get; set; }            // 当前值
}
```

#### 变量类型

| 类型 | 说明 |
|------|------|
| `Local` | 本地变量，存储在内存中 |
| `Communication` | 通讯变量，与 PLC/设备关联 |

#### 使用全局变量

```csharp
// 获取工作空间
var workspace = container.Resolve<IWorkspaceManager>();

// 创建本地变量
var localVar = new GlobalVariableModel
{
    Name = "Counter",
    VariableType = VariableType.Local,
    DataType = typeof(int),
    Value = 0
};
workspace.GlobalVariables.Add(localVar);

// 创建通讯变量
var commVar = new GlobalVariableModel
{
    Name = "PLC_Temperature",
    VariableType = VariableType.Communication,
    ConnectionName = "PLC_1",
    Address = "HR100",
    DataType = typeof(float)
};
workspace.GlobalVariables.Add(commVar);

// 读取变量值
var value = workspace.GetVariable("Counter")?.Value;

// 写入变量值
workspace.SetVariable("Counter", 100);
```

---

### 性能监控

性能监控模块提供系统资源和执行性能的实时监控。

#### 系统监控

```csharp
// 获取系统监控实例
var monitor = SystemMonitor.Instance;

// 获取所有指标
var metrics = monitor.GetAllMetrics();
Console.WriteLine($"CPU使用率: {metrics.CpuUsagePercent}%");
Console.WriteLine($"内存使用: {metrics.PrivateMemoryMB} MB");
Console.WriteLine($"线程数: {metrics.ThreadCount}");
Console.WriteLine($"句柄数: {metrics.HandleCount}");
```

#### 性能监控接口

```csharp
public interface IPerformanceMonitor
{
    void StartNodeTiming(Guid nodeId, string nodeName, string sessionId);
    void EndNodeTiming(Guid nodeId);
    void RecordSessionStart(string sessionId, string flowName);
    void RecordSessionEnd(string sessionId);
    PerformanceSnapshot GetSnapshot();
    NodePerformanceStats GetNodeStats(Guid nodeId);
    SessionPerformanceStats GetSessionStats(string sessionId);
}
```

#### 对象池

```csharp
// 创建对象池
var pool = new ObjectPool<List<int>>(
    factory: () => new List<int>(),
    resetAction: list => list.Clear(),
    initialSize: 10,
    maxCapacity: 100
);

// 获取对象
var list = pool.Get();

// 使用对象...

// 归还对象
pool.Return(list);
```

---

## 快速开始

### 1. 创建解决方案

```csharp
var solution = new SolutionModel
{
    SolutionName = "MyProject",
    Description = "我的视觉检测项目"
};
solutionService.Create(solution);
```

### 2. 添加流程

```csharp
var flow = new FlowModel
{
    FlowName = "MainProcess",
    Description = "主检测流程",
    IsEnabled = true,
    TriggerMode = FlowTriggerMode.Continuous
};
solution.Flows.Add(flow);
```

### 3. 添加步骤

```csharp
var delayStep = new ActionStep
{
    StepName = "等待稳定",
    PluginName = "DelayPlugin",
    PluginTypeName = "VisionMaster.Plugins.Utility.DelayPlugin"
};
flow.Steps.Add(delayStep);
```

### 4. 编译并运行

```csharp
// 编译
var result = flowCompiler.Compile(flow.Steps);
if (result.Success)
{
    var session = new FlowSession
    {
        FlowName = flow.FlowName,
        ExecutionEngine = result.Data
    };
    runtimeManager.RegisterSession(session);
    
    // 运行
    await flowEngine.RunSessionAsync(session);
}
```

---

## API 参考

### ICommunicationManager

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

### IFlowEngine

```csharp
public interface IFlowEngine
{
    int ActiveSessionCount { get; }
    event EventHandler<SessionStateChangedEventArgs>? SessionStateChanged;
    
    Task RunSessionAsync(FlowSession session);
    Task RunSessionOnceAsync(FlowSession session);
    void StopAll();
    void StopSession(string flowName);
    void PauseSession(string flowName);
    void ResumeSession(string flowName);
}
```

### IExecutionContext

```csharp
public interface IExecutionContext
{
    CancellationToken CancellationToken { get; }
    ILogService Logger { get; }
    FlowSession? CurrentSession { get; }
    
    T? GetVariable<T>(string name);
    void SetVariable(string name, object? value);
    GlobalVariableModel? GetVariableInfo(string name);
}
```

---

## 扩展开发

### 添加新的通讯协议

1. 创建配置类：

```csharp
public class CustomProtocolConfig : EthernetConfigBase
{
    public override CommunicationType Type => CommunicationType.FreeProtocol;
    public string CustomParameter { get; set; } = "";
    
    public override ICommunicationConnection CreateConnection() 
        => new CustomProtocolConnection(this);
}
```

2. 创建连接类：

```csharp
public class CustomProtocolConnection : BaseConnection<CustomDevice>
{
    protected override void InitializeDevice() { /* ... */ }
    protected override OperateResult ConnectServer() { /* ... */ }
    protected override void CloseConnection() { /* ... */ }
    // 实现其他抽象方法...
}
```

3. 注册工厂：

```csharp
ConnectionFactoryManager.Instance.Register(new CustomProtocolFactory());
```

### 添加新的步骤类型

1. 继承 StepModel：

```csharp
public class CustomStep : StepModel
{
    public string CustomProperty { get; set; }
}
```

2. 创建编译节点：

```csharp
public class CompiledCustomNode : CompiledNode
{
    public override List<CompiledNode> RunAndGetNext(IExecutionContext context)
    {
        // 执行逻辑
        return NextNodes;
    }
}
```

---

## 测试数据

测试配置文件位于 `communications_test_data.json`，包含以下测试连接：

| 名称 | 协议 | 地址 |
|------|------|------|
| Modbus_PLC_1 | Modbus TCP | 192.168.1.100:502 |
| Siemens_S7_1200 | S7 | 192.168.1.10:102 |
| Siemens_S7_1500 | S7 | 192.168.1.11:102 |
| Siemens_Smart200 | S7 | 192.168.1.12:102 |
| Modbus_RTU_Sensor | Modbus RTU | COM3@115200 |
| Backup_PLC | Modbus TCP | 192.168.1.200:502 |

---

## 许可证

本项目仅供学习和研究使用。

---

## 更新日志

### v1.0.0
- 初始版本发布
- 支持 Modbus TCP/RTU、西门子 S7 协议
- 实现流程编排和执行引擎
- 插件化架构设计
- 性能监控和全局变量系统
