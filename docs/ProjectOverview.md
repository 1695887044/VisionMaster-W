# VisionMaster 视觉检测系统项目文档

## 1. 项目定位
VisionMaster 是一套 **工业视觉在线检测上位机**平台，基于 **WPF + Prism + Halcon**。核心功能是让用户通过可视化画布拖拽插件、设置参数、连线，即可完成从相机采集、图像处理到 PLC 通信、日志监控的一条完整检测流程。

> 目标：降低视觉系统的上手门槛，快速交付可复用的视觉检测方案。

## 2. 体系结构
```
┌───────────────────────────────────────────────────────────────────────┐
│                   VisionMaster 主程序 (WPF Shell)                   │
│  ShellViewModel / UI 视图 / Prism Application / 事件总线               │
├───────────────────────┬───────────────────────┬───────────────────────┬───────────────────────┤
│      UI/控件层       │        Engine        │        Comm        │      Services         │
│(Core.Controls/… )     │  (FlowEngine)         │  (AdvancedCommunicationManager)│(Variable, Solution, Log, …)│
├───────────────────────┴───────────────────────┴───────────────────────┴───────────────────────┤
│                        Core 核心层（DTO、接口、工具）                       │
│  不反向依赖任何上层，仅由上层引入                                 │
└───────────────────────────────────────────────────────────────────────┘
```

- **Core**：`StepModel`、`CompiledNode` 等模型；`IVisionPlugin`、`IExecutionContext` 等接口。 
- **Engine**：负责蓝图 → 编译 → 运行。核心：`FlowCompiler`、`CompiledFlow`、`FlowEngineService`。 
- **Comm**：多协议连接工厂、轮询、心跳、变量同步。 
- **Services**：方案持久化、变量管理、日志、生命周期。 
- **Plugins**：放在 `Modules/` 的 DLL，继承 `VisionPluginBase`。 
- **UI**：WPF + Prism + AvalonDock、AvalonEdit，提供画布、属性、日志、变量面板。

## 3. 关键模块与职责
| 模块 | 主要职责 | 核心类 | 备注 |
|------|----------|--------|------|
| Core | 提供模型、接口、工具 | `StepModel`, `CompiledNode`, `IVisionPlugin`, `IExecutionContext` | 只提供数据结构 & 协议 |
| Engine | 蓝图解析、节点实例化、执行 | `FlowCompiler`, `CompiledFlow`, `FlowEngineService` | 栈式迭代器、`CurrentFlowState` 控制旗 |
| Comm | 连接管理、轮询、心跳、变量同步 | `AdvancedCommunicationManager`, `ConnectionFactory`, `VariableBridge` | 多协议统一入口 |
| Services | 方案 & 变量持久化、日志、生命周期 | `SolutionService`, `VariablePersistenceService`, `LogService`, `AppLifetimeService` | 负责全局数据与生命周期 |
| Plugins | 视觉算子实现 | `VisionPluginBase`, `ImageAcqPlugin`, `ROIPlugin`, `ScriptPlugin` |
| UI | 窗口、停靠、编辑器、控件 | `MainWindow`, `CanvasView`, `PropertyGrid`, `VariablePanel` |

## 4. 运行流程（图 → 编译 → 运行）
1. **创建流程**：用户拖拽插件到画布，连线，设置属性。`StepModel` 只保存 `PluginTypeName` 与 `LinkedSources`，不实例化插件。 
2. **编译**：
   - `FlowCompiler` 读取 `StepModel` 树。
   - 通过 `Activator.CreateInstance` 实例化插件并配置。
   - `LinkPorts` 将连线解析为四类数据源（运行时变量、全局变量、常量、上游输出）。
   - 打包成 `CompiledFlow`，包含 `NodeLookup` 与 `DependencyMap`。 
3. **执行**：
   - `FlowEngineService` 启动一个 `FlowSession`，关联 `CancellationToken` / `PauseLock`。
   - `CompiledFlow.Run` 采用显式栈实现深度优先遍历。
   - `CompiledNode.RunAndGetNext` 负责业务逻辑与控制转移；插件通过 `Execute` 返回 `Success` 端口。
   - 变量同步与 PLC 读写由 `NetworkVariableBridge` 与 `AdvancedCommunicationManager` 轮询完成。 
4. **输出**：UI 绑定本地变量镜像，日志记录，结果回写 PLC。 

## 5. 关键设计决策
| 决策 | 目的 | 说明 |
|------|------|------|
| **张三 设计/编译/运行分层** | 解耦编辑与执行，避免重资源实例化 | 蓝图只存字符串，编译时才实例化，运行时统一管理。
| **端口即字段 & 反射收集** | 省去配置文件，插件透明 | `InputPort/OutputPort` 通过属性反射发现，主框架无需改动。
| **插件化与 Display 特性** | 开闭原则，零改动可接入新算子 | 通过 `[Display(GroupName…)]` 自动分类；`Modules/` DLL 自动扫描。
| **栈式深度优先执行** | 防止递归栈溢出，支持取消/暂停 | 阂列使用显式栈，动态推送子序列。 |
| **单一状态旗 `CurrentFlowState`** | 简化控制流 | `break/continue/return` 统一写状态，子节点读取。
| **业务失败不抛异常** | 稳定流程 | 插件失败写 `Success=false`，日志记录，流程继续。
| **安全表达式白名单** | 防止恶意代码 | `DynamicExpresso` 仅允许数值、字符串、布尔。 |
| **变量镜像** | UI 与 PLC 解耦 | 变量读写聚合到内存镜像，UI 绑定频繁读设备可避免卡顿。
| **多协议统一入口** | 易扩展 | 基于工厂模式 + 轮询 + 事件回调，添加新协议只需实现 `IConnection`。

## 6. 演示与快速上手
1. 打开脚本目录 `VisionMaster.sln`，编译项目。 
2. 运行后主窗口弹出 Splash，加载完成后显示四大面板。 
3. **新增流程**：
   - 在左侧工具箱拖拽 `相机采集`、`ROI`、`图像统计`、`PLC 写入`。
   - 连线：相机采集 → ROI → 图像统计 → PLC 写入。
   - 配置属性，点击 **Compile** → **Run**。 
4. 在 **Variable Manager** 添加 PLC 变量，查看实时值。 
5. 日志面板实时显示检测结果与错误。 

> 若想快速浏览插件实现：
> - `PluginService` 只扫描 `Modules/` DLL；查询插件类型与族分类。
> - `VisionPluginBase` 说明端口属性与 `Execute` 逻辑。

## 7. 已知缺点 & 维护
- **写设备异常吞没**：目前 `WriteToDevice` 异常被捕获后不抛，导致不可追踪。建议在 `AdvancedCommunicationManager` 中加入可配置的异常回调。
- **动态端口渲染延迟**：`ROI` 动态端口生成后 UI 绑定稍有延迟，待 `AvalonDock` 事件优化。
- **变量轮询频率不精准**：`ReadCycleMs` 仅在连接配置中设置，建议在 `AdvancedCommunicationManager` 添加系统时钟同步。
- **缺少单元测试**：核心逻辑如 `FlowCompiler`、`CompiledFlow` 仍缺乏覆盖。

## 8. 扩展方向
| 方向 | 说明 |
|------|------|
| **添加新协议** | 实现 `ConnectionConfigBase` 与 `IConnection`，随后更新 `ConnectionFactory`。
| **新算子** | 继承 `VisionPluginBase`，声明 `[Display(GroupName="自定义", Name="SamplePlug")]`，编译后自动出现在工具箱。
| **UI 丰富** | 增加主题切换、移动窗格全窗口化，使用 `CustomStyles`。
| **调试工具** | 在工程中加入断点支持 `CompiledNode` 等，展示运行状态。
| **性能监控** | 在 `FlowEngineService` 中记录节点执行时间，导出为实时曲线。 

---

本文档结合 `docs/设计思想.md` 与 `软件手册`，为初学者与开发者提供全景式的理解与快速上手路径。 

作者：VisionMaster 开发组
日期：2026-10-05
