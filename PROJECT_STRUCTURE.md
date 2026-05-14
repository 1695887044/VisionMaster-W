# VisionMaster 项目结构说明

## 项目概述

VisionMaster 是一个视觉运动控制平台，支持多流程并发执行。项目采用模块化架构设计，遵循 SOLID 原则和大厂规范。

---

## 目录结构

```
VisionMaster-W/
├── VisionMaster/                    # 主应用程序项目（WPF）
│   ├── Commands/                   # 命令模式实现（MVVM）
│   │   ├── LoadLayoutCommand.cs
│   │   └── SaveLayoutCommand.cs
│   │
│   ├── Common/                     # 通用定义
│   │   └── Enums.cs                # 枚举定义（SessionState 等）
│   │
│   ├── Core/                       # 核心基础设施
│   │   ├── AuditLogger.cs          # 审计日志
│   │   ├── BatchDataWriter.cs      # 批量数据写入
│   │   ├── BlockingQueue.cs        # 阻塞队列
│   │   ├── DebounceThrottle.cs     # 防抖节流
│   │   ├── EventExtensions.cs      # 事件扩展
│   │   ├── GlobalExceptionHandler.cs # 全局异常处理
│   │   ├── HardwareHeartbeatMonitor.cs # 硬件心跳监控
│   │   ├── HighPrecisionTimer.cs   # 高精度定时器
│   │   ├── LockFreeRingBuffer.cs   # 无锁环形缓冲区
│   │   ├── MemoryManager.cs        # 内存管理
│   │   ├── ObjectPool.cs           # 对象池
│   │   ├── PerformanceMonitor.cs   # 性能监控服务
│   │   ├── PerformanceProfiler.cs  # 性能分析器
│   │   └── RetryPolicy.cs          # 重试策略
│   │
│   ├── EventModel/                 # 事件模型
│   │   ├── SessionStateChangedEventArgs.cs
│   │   └── StepRenamedMessage.cs
│   │
│   ├── Helpers/                    # 辅助工具类
│   │   ├── FlowQueryHelper.cs      # 流程查询辅助
│   │   ├── SystemMonitor.cs        # 系统监控
│   │   ├── TypeHelper.cs           # 类型辅助
│   │   └── WatchPortWrapper.cs     # 端口包装器
│   │
│   ├── Models/                     # 数据模型
│   │   ├── Compileds/              # 编译后模型
│   │   │   ├── CompilationResult.cs
│   │   │   ├── CompiledBranch.cs
│   │   │   ├── CompiledBreakNode.cs
│   │   │   ├── CompiledForNode.cs
│   │   │   ├── CompiledIfNode.cs
│   │   │   ├── CompiledNode.cs
│   │   │   └── CompiledWhileNode.cs
│   │   ├── ProcessStep/            # 流程步骤定义
│   │   │   ├── BreakStep.cs
│   │   │   ├── ConditionStep.cs
│   │   │   ├── ForStep.cs
│   │   │   ├── IContainerStep.cs
│   │   │   ├── StepCollection.cs
│   │   │   ├── StepModel.cs
│   │   │   └── WhileStep.cs
│   │   ├── CompiledFlow.cs         # 编译流程
│   │   ├── FlowModel.cs            # 流程模型
│   │   ├── FlowSession.cs          # 流程会话（运行时状态）
│   │   ├── GlobalVariableModel.cs  # 全局变量模型
│   │   ├── InputPortUIModel.cs     # 输入端口UI模型
│   │   ├── LinkReference.cs        # 链接引用
│   │   ├── LocalVariableItem.cs    # 本地变量项
│   │   ├── PortDefinition.cs       # 端口定义
│   │   ├── SolutionModel.cs        # 解决方案模型
│   │   ├── ToolItemModel.cs        # 工具项模型
│   │   ├── TreeNodeModel.cs        # 树节点模型
│   │   ├── VariableNode.cs         # 变量节点
│   │   └── WatchItemModel.cs       # 监视项模型
│   │
│   ├── Services/                   # 服务层
│   │   ├── ExecutionContext.cs     # 执行上下文
│   │   ├── FlowCompiler.cs         # 流程编译器
│   │   ├── FlowEngineService.cs    # 流程引擎服务
│   │   ├── IFlowEngine.cs          # 流程引擎接口
│   │   ├── IPluginProvider.cs      # 插件提供者接口
│   │   ├── IReadOnlyWorkspaceContext.cs # 工作空间只读接口
│   │   ├── IResourceLockService.cs # 资源锁服务接口
│   │   ├── IRuntimeManager.cs      # 运行时管理器接口
│   │   ├── LogService.cs           # 日志服务
│   │   ├── PluginProvider.cs       # 插件提供者
│   │   ├── PluginService.cs        # 插件服务
│   │   ├── ResourceLockService.cs  # 资源锁服务实现
│   │   └── SolutionService.cs      # 解决方案服务
│   │
│   ├── ViewModels/                 # 视图模型（MVVM）
│   │   ├── DialogViewModels/       # 对话框视图模型
│   │   │   ├── ConditionEditorViewModel.cs
│   │   │   ├── GlobalVariableManagerViewModel.cs
│   │   │   ├── GlobalVariableViewModelBase.cs
│   │   │   └── VariableBindingViewModel.cs
│   │   ├── GlobalDataViewModel.cs  # 全局数据视图模型
│   │   ├── LogViewModel.cs         # 日志视图模型
│   │   ├── ModuleOutputViewModel.cs # 模块输出视图模型
│   │   ├── ProcessViewModel.cs     # 流程视图模型
│   │   └── ToolViewModel.cs        # 工具视图模型
│   │
│   ├── Views/                      # 视图层（WPF）
│   │   ├── DialogViews/            # 对话框视图
│   │   │   ├── ConditionEditorView.xaml
│   │   │   ├── ConditionEditorView.xaml.cs
│   │   │   ├── GlobalVariableView.xaml
│   │   │   ├── GlobalVariableView.xaml.cs
│   │   │   ├── VariableBindingView.xaml
│   │   │   └── VariableBindingView.xaml.cs
│   │   ├── GlobalDataView.xaml
│   │   ├── GlobalDataView.xaml.cs
│   │   ├── LogView.xaml
│   │   ├── LogView.xaml.cs
│   │   ├── ModuleOutputView.xaml
│   │   ├── ModuleOutputView.xaml.cs
│   │   ├── ProcessView.xaml
│   │   ├── ProcessView.xaml.cs
│   │   ├── ToolView.xaml
│   │   └── ToolView.xaml.cs
│   │
│   ├── App.xaml                    # 应用程序入口
│   ├── App.xaml.cs                 # 应用程序启动配置
│   ├── Shell.xaml                  # 主窗口
│   ├── Shell.xaml.cs               # 主窗口代码
│   ├── ShellViewModel.cs           # 主窗口视图模型
│   └── VisionMaster.csproj         # 项目文件
│
├── Shard/                         # 共享组件库
│   └── Core.Interfaces/           # 核心接口定义
│       ├── Core.Interfaces.csproj
│       ├── IExecutionContext.cs   # 执行上下文接口
│       ├── ILogService.cs         # 日志服务接口
│       ├── IPortBindingService.cs # 端口绑定服务接口
│       ├── IVisionPlugin.cs       # 视觉插件接口
│       └── ...
│
└── UI/                            # UI 控件库
    └── Controls/
        ├── UI.csproj
        └── ...
```

---

## 架构分层

### 1. 表示层（Presentation Layer）
- **Views/** - WPF 用户界面
- **ViewModels/** - MVVM 视图模型
- **Commands/** - 命令模式实现

### 2. 业务逻辑层（Business Logic Layer）
- **Services/** - 核心业务服务
  - `FlowEngineService` - 流程执行引擎
  - `FlowCompiler` - 流程编译器
  - `ResourceLockService` - 资源锁管理
- **Models/** - 业务数据模型

### 3. 基础设施层（Infrastructure Layer）
- **Core/** - 基础组件
  - 线程安全集合
  - 高性能数据结构
  - 异常处理
  - 性能监控
- **Helpers/** - 辅助工具

### 4. 接口层（Interface Layer）
- **Shard/Core.Interfaces/** - 跨项目共享接口

---

## 核心模块说明

### 流程执行引擎

| 组件 | 职责 |
|------|------|
| `IFlowEngine` | 定义流程执行契约 |
| `FlowEngineService` | 管理多会话并发执行 |
| `FlowSession` | 单个流程实例的运行时状态 |
| `ExecutionContext` | 执行上下文（日志、端口、取消令牌） |
| `CompiledFlow` | 编译后的可执行流程 |

### 资源管理

| 组件 | 职责 |
|------|------|
| `IResourceLockService` | 资源互斥访问接口 |
| `ResourceLockService` | 基于 SemaphoreSlim 的资源锁实现 |
| `IWorkspaceManager` | 全局变量管理接口 |

### 性能监控

| 组件 | 职责 |
|------|------|
| `IPerformanceMonitor` | 性能监控接口 |
| `PerformanceMonitor` | 节点/会话执行统计 |

---

## 设计模式

| 模式 | 应用场景 |
|------|----------|
| **MVVM** | 视图与业务逻辑分离 |
| **依赖注入** | Prism Unity 容器 |
| **命令模式** | 按钮操作解耦 |
| **工厂模式** | 插件创建 |
| **策略模式** | 流程控制 |
| **观察者模式** | 状态变更通知 |

---

## 并发设计

1. **线程安全** - `FlowSession` 使用 `lock` 保护状态属性
2. **资源互斥** - `ResourceLockService` 基于信号量实现
3. **优雅终止** - `CancellationToken` 支持取消操作
4. **暂停/恢复** - `ManualResetEventSlim` 控制流程暂停

---

## 依赖关系

```
Views → ViewModels → Services → Models → Core
                            ↓
                       Interfaces (Shard)
```

---

## 命名规范

| 类型 | 命名规则 | 示例 |
|------|----------|------|
| 接口 | I + PascalCase | `IFlowEngine` |
| 类 | PascalCase | `FlowEngineService` |
| 方法 | PascalCase | `RunSessionAsync` |
| 属性 | PascalCase | `ActiveSessionCount` |
| 私有字段 | camelCase | `_runtimeManager` |
| 枚举 | PascalCase | `FlowPriority` |
| 常量 | UPPER_SNAKE_CASE | `MAX_ITERATIONS` |

---

## 代码注释规范

- 使用 XML 文档注释
- 接口和类提供 `<summary>` 说明
- 方法提供 `<param>` 和 `<returns>`
- 复杂逻辑添加注释说明
- 公共 API 必须有完整注释
