# VisionMaster - 视觉运动控制平台

## 概述

VisionMaster是一个面向工业视觉检测的运动控制平台，支持多流程并发执行。本项目采用MVVM架构，通过流程建模、编译和执行三个核心阶段实现可视化编程。

---

## 核心架构

```
┌─────────────────────────────────────────────────────────────────┐
│                    VisionMaster 架构                          │
├─────────────────────────────────────────────────────────────────┤
│  ┌─────────────┐    ┌─────────────┐    ┌───────────────────┐   │
│  │   Model层   │───▶│  Compiler   │───▶│   Execution      │   │
│  │  (流程图)   │    │  (编译)     │    │   Engine         │   │
│  └─────────────┘    └─────────────┘    └───────────────────┘   │
│       │                    │                    │              │
│       ▼                    ▼                    ▼              │
│  StepModel,           CompiledNode         FlowSession        │
│  FlowModel            CompiledFlow          ExecutionContext  │
│  ConditionStep                                          │    │
│  WhileStep, ForStep                                     │    │
│                                                         ▼    │
│                                               ┌─────────────┐│
│                                               │  Plugin执行  ││
│                                               │ (IVisionPlugin)│
│                                               └─────────────┘│
└─────────────────────────────────────────────────────────────────┘
```

---

## 一、Model层：流程图建模

### 1.1 核心Model类型

#### StepModel - 步骤模型

```csharp
public class StepModel : BindableBase
{
    public Guid StepID { get; set; }           // 步骤唯一标识
    public string StepName { get; set; }       // 步骤名称
    public string PluginTypeName { get; set; }  // 插件类型全名
    public Dictionary<string, object> InputValues { get; set; }  // 静态参数
    public Dictionary<string, LinkReference> LinkedSources { get; set; }  // 连线引用
}
```

#### 容器步骤（Control Flow）

| 类型 | 说明 | 特点 |
|------|------|------|
| `ConditionStep` | 条件分支（If-Else） | 支持多个分支，每个分支有条件表达式 |
| `WhileStep` | 条件循环 | 继承ConditionStep，单分支循环 |
| `ForStep` | 计次循环 | 固定次数循环，暴露Index输出端口 |

#### FlowModel - 流程模型

```csharp
public class FlowModel : BindableBase
{
    public string FlowID { get; set; }
    public string FlowName { get; set; }
    public ObservableCollection<StepModel> Steps { get; set; }  // 步骤列表
    public FlowTriggerMode TriggerMode { get; set; }           // 触发模式
    public int Version { get; set; }                          // 版本号（自动递增）
}
```

### 1.2 连线机制

步骤之间通过`LinkReference`建立数据连接：

```csharp
public class LinkReference
{
    public Guid TargetStepId { get; set; }    // 源步骤ID
    public string TargetPortName { get; set; } // 源端口名
    public string DisplayAddress { get; set; } // 显示地址（如 "Global.VarName"）
}
```

---

## 二、编译阶段：FlowCompiler

### 2.1 编译流程

`FlowCompiler.Compile()` 方法将流程图转换为可执行的编译树：

```
输入: StepModel 列表
    │
    ▼
┌──────────────────────────────────┐
│ 阶段1: CompileSteps()           │
│  - 遍历所有步骤                 │
│  - 实例化插件 (IVisionPlugin)   │
│  - 创建 CompiledNode 树         │
│  - 编译条件表达式 (DynamicExpresso)│
└──────────────────────────────────┘
    │
    ▼
┌──────────────────────────────────┐
│ 阶段2: LinkPorts()              │
│  - 解析连线引用                 │
│  - 绑定端口 LinkedSource        │
│  - 数组索引代理 (ArrayIndexProxyPort)│
│  - 必填参数检查                 │
└──────────────────────────────────┘
    │
    ▼
输出: CompiledFlow
```

### 2.2 编译节点类型

| CompiledNode | 对应Model | 功能 |
|--------------|-----------|------|
| `CompiledPluginNode` | 普通插件 | 封装IVisionPlugin实例 |
| `CompiledIfNode` | ConditionStep | If-Else分支执行 |
| `CompiledWhileNode` | WhileStep | 条件循环 |
| `CompiledForNode` | ForStep | 计次循环，暴露Index |
| `CompiledBreakNode` | BreakStep | 跳出循环 |
| `CompiledContinueNode` | ContinueStep | 跳过当前迭代 |
| `CompiledReturnNode` | ReturnStep | 终止流程 |

### 2.3 表达式编译

使用 **DynamicExpresso** 库编译条件表达式：

```csharp
// 编译条件表达式（如 "Score > 80"）
Lambda compiledCondition = _interpreter.Parse(
    "Score > 80",           // 用户输入的表达式
    typeof(bool),            // 返回类型
    delegateParams.ToArray() // 参数列表（如 Score: double）
);
```

### 2.4 端口连线

```csharp
// LinkPorts() 核心逻辑
foreach (var linkKvp in model.LinkedSources)
{
    var sourcePort = FindSourcePort(linkKvp.Value);
    
    if (targetNode is CompiledPluginNode pluginNode)
    {
        // 普通插件：直接绑定到输入端口
        pluginNode.ExternalPlugin.Inputs[inputName].LinkedSource = sourcePort;
    }
    else if (targetNode is CompiledIfNode ifNode)
    {
        // If/While：通过 Guid 映射
        ifNode.UpstreamLinks[varId] = sourcePort;
    }
}
```

---

## 三、执行阶段：FlowEngineService

### 3.1 会话管理

每个流程实例对应一个`FlowSession`：

```csharp
public class FlowSession : BindableBase
{
    public string SessionID { get; }
    public string FlowName { get; }
    public CompiledFlow ExecutionEngine { get; set; }  // 编译后的执行引擎
    public SessionState State { get; set; }           // 运行状态
    public bool IsRunning { get; set; }               // 是否正在运行
    public CancellationTokenSource CancellationTokenSource { get; set; }
    public ManualResetEventSlim PauseLock { get; }    // 暂停控制
}
```

### 3.2 执行入口

#### 连续运行模式

```csharp
public async Task RunSessionAsync(FlowSession session)
{
    session.IsRunning = true;
    session.State = SessionState.Running;
    session.CancellationTokenSource = new CancellationTokenSource();
    
    await Task.Run(() =>
    {
        while (!token.IsCancellationRequested)
        {
            session.PauseLock.Wait(token);  // 支持暂停
            
            var context = new ExecutionContext(...);
            session.ExecutionEngine.Run(context);  // 执行一次流程
            
            Thread.Sleep(10);  // 防止CPU占用过高
        }
    }, token);
}
```

#### 单次运行模式

```csharp
public async Task RunSessionOnceAsync(FlowSession session)
{
    var context = new ExecutionContext(_logService, session, _workspaceManager, token);
    session.ExecutionEngine.Run(context);  // 只执行一次
}
```

### 3.3 ExecutionContext - 执行上下文

```csharp
public class ExecutionContext : IExecutionContext
{
    public ILogService Logger { get; init; }              // 日志服务
    public IPortBindingService PortBindingService { get; init; }
    public CancellationToken CancellationToken { get; init; }
    public FlowControlState CurrentFlowState { get; set; }  // 控制流状态
    public FlowSession CurrentSession { get; init; }
    public IDictionary<string, object> LocalVariables { get; } = new Dictionary<string, object>();
}
```

### 3.4 控制流状态

```csharp
public enum FlowControlState
{
    Normal,    // 正常执行
    Break,     // 跳出循环
    Continue,  // 跳过迭代
    Return     // 终止流程
}
```

---

## 四、完整调用流程示例

### 4.1 编程方式创建流程

```csharp
// 1. 创建流程
var flow = new FlowModel { FlowName = "检测流程" };

// 2. 添加视觉算子步骤
var cameraStep = new StepModel
{
    StepName = "相机采集",
    PluginTypeName = "VisionMaster.Plugins.CameraPlugin",
    InputValues = new Dictionary<string, object>
    {
        { "ExposureTime", 25.0 },
        { "Gain", 1.0 }
    }
};
flow.AddStepWithAutoName(cameraStep);

// 3. 添加模板匹配步骤并建立连线
var matchStep = new StepModel
{
    StepName = "模板匹配",
    PluginTypeName = "VisionMaster.Plugins.TemplateMatch",
    LinkedSources = new Dictionary<string, LinkReference>
    {
        { "InputImage", new LinkReference(cameraStep.StepID, "OutputImage", "相机采集_1.OutputImage") }
    }
};
flow.AddStepWithAutoName(matchStep);
```

### 4.2 编译流程

```csharp
// 获取编译器（通过DI容器）
var compiler = ContainerLocator.Container.Resolve<FlowCompiler>();

// 编译流程
CompilationResult result = compiler.Compile(flow.Steps);

if (result.Success)
{
    // 创建会话
    var session = new FlowSession(flow.FlowName, result.Data);
    
    // 注册到运行时
    runtimeManager.RegisterSession(session);
}
else
{
    foreach (var error in result.Errors)
    {
        Logger.Error($"编译错误: {error}");
    }
}
```

### 4.3 执行流程

```csharp
// 获取流程引擎
var engine = ContainerLocator.Container.Resolve<IFlowEngine>();

// 方式1: 连续运行
await engine.RunSessionAsync(session);

// 方式2: 单次运行
await engine.RunSessionOnceAsync(session);

// 控制操作
engine.PauseSession(session);   // 暂停
engine.ResumeSession(session);  // 恢复
engine.StopSession(session);    // 停止
```

---

## 五、插件系统

### 5.1 IVisionPlugin 接口

```csharp
public interface IVisionPlugin
{
    string InstanceName { get; set; }
    Dictionary<string, IInputPort> Inputs { get; }
    Dictionary<string, IOutputPort> Outputs { get; }
    void Execute(IExecutionContext context);
}
```

### 5.2 插件执行流程

```
1. 编译阶段：反射实例化插件
   Type type = Type.GetType(model.PluginTypeName);
   IVisionPlugin plugin = (IVisionPlugin)Activator.CreateInstance(type);

2. 灌入静态参数
   foreach (var kvp in model.InputValues)
   {
       plugin.Inputs[kvp.Key].Value = kvp.Value;
   }

3. 运行阶段：执行插件
   plugin.Execute(context);
```

---

## 六、状态管理

### 6.1 SessionState 状态机

```
     Idle
       │
       ▼
   Running ◄─────────────┐
      │                  │
      ├─► Paused ────────┤
      │       │          │
      │       ▼          │
      │   Running        │
      │                  │
      ▼                  │
   Stopped ──────────────┤
      │                  │
      ▼                  │
   Faulted ──────────────┘
```

### 6.2 状态变更通知

```csharp
public event EventHandler<SessionStateChangedEventArgs> SessionStateChanged;

// 订阅状态变更
engine.SessionStateChanged += (sender, args) =>
{
    Console.WriteLine($"会话 {args.SessionId} 状态: {args.OldState} -> {args.NewState}");
};
```

---

## 七、性能监控

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

## 八、线程安全

### 8.1 关键同步机制

| 组件 | 同步方式 |
|------|----------|
| `FlowSession` | `_stateLock` 锁保护状态属性 |
| `RuntimeManager` | `BindingOperations.EnableCollectionSynchronization` |
| `ResourceLockService` | `SemaphoreSlim` 实现资源互斥 |

### 8.2 暂停机制

使用`ManualResetEventSlim`实现零开销暂停：

```csharp
public ManualResetEventSlim PauseLock { get; } = new ManualResetEventSlim(true);

// 暂停
session.PauseLock.Reset();

// 恢复
session.PauseLock.Set();

// 执行时等待
session.PauseLock.Wait(token);
```

---

## 设计亮点

1. **编译与执行分离**：流程图先编译为中间表示，执行时无需重复解析
2. **表达式安全**：只允许值类型和字符串用于表达式计算，防止代码注入
3. **数组索引支持**：通过`ArrayIndexProxyPort`支持数组元素访问（如 `Result[0]`）
4. **自动命名**：`FlowModel.AddStepWithAutoName()` 自动生成唯一名称
5. **版本跟踪**：流程图结构变化时自动递增版本号

---

## 项目结构

```
VisionMaster/
├── Models/           # 数据模型
│   ├── FlowModel.cs
│   ├── StepModel.cs
│   ├── FlowSession.cs
│   └── ProcessStep/   # 控制流步骤
├── Services/         # 服务层
│   ├── FlowCompiler.cs      # 编译器
│   ├── FlowEngineService.cs # 执行引擎
│   └── IRuntimeManager.cs   # 运行时管理
├── Core/             # 核心组件
│   ├── PerformanceMonitor.cs # 性能监控
│   └── ResourceLockService.cs # 资源互斥
└── Plugins/          # 插件目录
    └── Plugin.Delay/
```