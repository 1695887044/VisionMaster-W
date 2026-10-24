# VisionMaster 详细模块拆解

> 目标：为新手与内部开发者提供全貌与细节。

## 1. Core（核心）
Core 只提供模型、接口、工具，位于 `Core` 目录。
- **DTO**：`StepModel`、`CompiledNode`、`VariableDto` 等。
- **接口**：`IVisionPlugin`、`IExecutionContext`、`IConnection`、`IAppModule` 等。
- **工具**：`DynamicExpresso`、`FileOps`、`Logger` 等。

## 2. Engine（流引擎）
负责蓝图 → 编译 → 执行。
- **FlowCompiler**：递归解析蓝图，实例化插件，生成 `CompiledFlow`。
- **CompiledFlow**：维护节点映射，提供 `Run`。
- **CompiledNode**：抽象基类，子类实现：`CompiledPluginNode`、`CompiledIfNode`、`CompiledWhileNode`、`CompiledForNode`、`CompiledBreakNode`、`CompiledContinueNode`、`CompiledReturnNode`。
- **FlowEngineService**：公开 `RunSessionAsync`，管理 `CancellationToken` 与暂停逻辑。
- **执行方式**：使用显式栈实现深度优先，避免递归栈溢出，支持暂停/取消，节点执行前后通过事件高亮。

## 3. Communication（通信）
多协议连接、轮询、心跳、变量同步。
- **AdvancedCommunicationManager**：统一管理连接、定时轮询与重连。
- **IConnection**：通信接口，`ModbusTcpConnection` 与 `SiemensS7Connection` 实现。
- **NetworkVariableBridge**：将本地变量与 PLC 变量绑定，周期性读取更新。

## 4. Services（服务层）
方案、变量、日志、生命周期等。
- **SolutionService**：读写 `.vms` 配置文件。
- **VariableManager**：管理全局与局部变量。
- **LogService**：线程安全日志写入。
- **AppLifetimeService**：启动链 & 退出链，负责插件扫描、连接检查。

## 5. Plugins（插件）
放在 `Modules` 目录，插件实现 `IVisionPlugin` 并使用 `DisplayAttribute` 指定分类。
- **VisionPluginBase**：基类，封装 `InputPort`、`OutputPort`、`Success` 与 `ErrorMessage`。
- **DisplayAttribute**：定义插件显示名与分组。
- **常用插件**：`ImageAcqPlugin`、`ROIPlugin`、`ScriptPlugin`、`HalconFilterPlugin`。

## 6. UI（前端）
基于 WPF + Prism + AvalonDock。
- **MainWindow**：主窗口，包含 `DockingManager`。
- **CanvasView**：自定义 `FlowCanvas`，支持节点拖拽与连线。
- **PropertyGridView**：属性编辑面板。
- **LogView** 与 **VariablePanelView**：分别显示日志与变量。
- **交互**：插件拖拽、连线、属性编辑，编译与运行通过 `FlowEngineService`，节点状态通过事件更新 UI。

## 7. 关键流程
1. **拖拽**：用户拖拽插件到画布，生成 `StepModel`（仅包含插件类型与连线信息）。
2. **编译**：`FlowCompiler` 递归实例化插件，解析连线，构造 `CompiledFlow`。
3. **运行**：`FlowEngineService` 创建 `FlowSession`，使用显式栈进行深度优先执行，节点调用插件 `Execute`。
4. **变量同步**：`NetworkVariableBridge` 轮询 PLC，更新全局变量；日志通过 `LogService` 推送。

---