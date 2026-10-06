# VisionMaster 视觉检测系统概览

## 1. 目标与定位
VisionMaster 是一套基于 **WPF + Prism + Halcon** 的工业视觉在线检测平台。它将**可视化流程画布**、**插件化算子**、**通信模块** 与 **变量系统**三大核心无缝结合，帮助用户从画布局到实时读写 PLC、系统监控一路完成。

> **核心理念**：
> - 设计 → 编译 → 运行 三层分离；
> - 画布只存字符串与连线，保持轻量；
> - 插件只需遵守 `IVisionPlugin` 接口，主框架保持闭合。

## 2. 整体架构
```
┌───────────────────────┐
│      主程序（Shell）    │   WPF + Prism + AvalonDock
│    ShellViewModel／Views │   负责 UI、命令、导航
└─────┬─────┬─────┬───────┘
      │ UI   │ Engine │ Comm   │ Services
      │ (Core.Controls)│ (FlowEngine)│ (AdvancedCommunicationManager)│(Variable, Solution, Log, …)
      └─────┴─────┴───────┘
┌───────────────────────┐
│          Core           │   变量模型、DTO、通用工具
└───────────────────────┘
```

- **UI 层**：负责窗体、布局、插件拔样式、脚本编辑、图像查看。使用 AvalonDock 让面板可拖拽、停靠，支持多框架示例。
- **Engine 层**（FlowEngine）：编译蓝图为可执行树，并提供栈式深度优先迭代器实现执行；负责插件实例化、连线解析、表达式求值。
- **Comm 层**（AdvancedCommunicationManager）：实现 MBC、Modbus、S7 等协议的连接工厂、轮询、心跳与变量读取。
- **Core**：包含模型（StepModel、CompiledNode）、接口（IVisionPlugin、IExecutionContext 等）与通用工具。
- **Plugins**：每个插件是 DLL，继承 `VisionPluginBase`，通过反射自动收集 `InputPort`/`OutputPort`。插件目录为 `Modules/`，扫描后挂载到工具箱。
- **Services**：变量管理、方案持久化、日志、生命周期、通知等辅助服务。

## 3. 工作流程（从图到跑）
1. **绘制图纸**：拖拽插件，连线，配置属性；`StepModel` 只记录插件类型名（字符串）与连线指针。
2. **编译**：
   - `FlowCompiler` 把蓝图反射实例化插件，构造 `CompiledNode` 树；
   - 把连线解析为四类源（运行时变量、全局变量、常量、上游输出）。
3. **运行**：
   - `FlowEngineService` 负责创建 `FlowSession`、线程/任务、取消/暂停；
   - `CompiledFlow` 用显式栈执行，返回节点决定下一个动作；
   - 插件实际调用 `Execute`，业务失败写 Success = false，不抛异常。

## 4. 关键设计决策
| 决策 | 原因 |
|------|------|
| 设计/编译/运行分层 | 先拆分关注点，避免相机/算法的重量级实例影响布局编辑。
| 零依赖插件扫描 | 通过 `IVisionPlugin` 接口与 `Display` 特性，主框架不需改动即可接入新插件。
| 端口即字段 | `InputPort`/`OutputPort` 用属性反射发现，省去额外配置文件；利于可视化对接。
| 栈式迭代器执行 | 防止递归栈溢出，支持深层嵌套 `if/while/for`；还能轻松实现暂停/取消。
| `CurrentFlowState` 控制旗 | 把 `break/continue/return` 统一为状态标识，简化控制流逻辑。
| 变量镜像 | 网络变量读取聚合到内存镜像，UI 绑定不频繁读设备，避免卡顿。
| 表达式白名单 | `DynamicExpresso` 只允许运行安全的数值/字符串/布尔，防止恶意代码。
| 业务失败不抛异常 | 视觉检测失败视业务状态，而不是系统崩溃，让流程稳健可继续。

## 5. 代码路径亮点
- `VisionPluginBase.Execute` → `RunAlgorithm` → `Success` 端口判定
- `FlowCompiler.Compile` → `CompiledNode` 构造 → `FinalCompiledFlow`
- `CompiledFlow.Run` 通过 `stack.Push/Pop` 实行深度优先；`CompiledIfNode` 返回分支列表。
- `AdvancedCommunicationManager` 通过 `ConcurrentDictionary` + `Timer` 聚合变量。

## 6. 如何快速上手
1. **打开 Visual Studio**，编译项目；
2. **运行**后会弹出 Splash；主窗口显示四大面板（左侧工具箱、中央画布、右侧属性、底部日志）。
3. **新增流程**：拖拽插件到画布，连线，然后点击 **Compile**，再 **Run**。
4. **查看变量**：右键**Variable Manager**，添加 PLC 变量即可。

## 7. 未来扩展
- **新增协议**：实现 `ConnectionConfigBase` 子类 + `IConnection` 即可接入；不需要改主程序。
- **新算子**：继承 `VisionPluginBase` 并使用 `[Display]` 即可；编译时自动实例化。
- **更丰富 UI**：基于 `Prism Application` 的高阶导航、弹窗等已可扩展。

> 以上文档旨在让开发者快速进入项目、理解各层职责和协作核心。若需详细的 API/代码，参阅 `docs/设计思想.md` 与《软件手册》。

---

Author: VisionMaster Team
Date: 2026-10-05
