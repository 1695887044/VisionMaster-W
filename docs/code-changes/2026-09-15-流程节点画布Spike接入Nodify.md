# 开发记录：流程节点画布 Spike（Nodify 接入 + 图纸双向映射）

- 日期：2026-09-15
- 类型：功能实现（画布引入 M1 阶段收尾项 M1-8）
- 触发场景：M1 前置改造（LinkKind / 编译错误结构化 / 布局隔离 / Version 误递增）完成后，用最小闭环验证"流程节点画布"这条技术路线是否成立。

---

## 一、需求与决策

### 选型：Nodify 7.3.0，不自研节点画布

核实结论：

| 项 | 结果 |
|---|---|
| 目标框架 | 含 `net9.0-windows7.0` 原生目标，与本项目 `net9.0-windows` 直接匹配 |
| 依赖 | 除 WPF 外**零依赖** |
| 主题 | 程序集带 `ThemeInfoAttribute(SourceAssembly, ...)`，Generic.xaml 自动加载，无需手动合并资源字典 |
| 许可 | MIT |
| 定位 | "为 MVVM 而生的高性能节点编辑器控件"，官方称可承载数百节点 |

关键判断：**无限画布、框选、缩放、平移、拉线、自动平移、撤销友好**这些交互内核不该自研——参考项目 `WPF-Halcon-流程拖拉` 里根本没有节点画布（它的"流程拖拉"就是 TreeView + 手工 DragDrop 事件，与本项目现状等价），没有可抄的代码。

### 三个用反射/文档核实过的事实（纠正了凭印象的写法）

1. **`NodeInput` / `NodeOutput` 直接继承 `Connector`**（不是"包含"Connector）
   → `Anchor` / `IsConnected` / `DisconnectCommand` 可以直接绑在这两个控件上，不需要再套一层 `ConnectorTemplate`。
   注：`NodeInput.ConnectorTemplate` 是"连接点的视觉内容模板"，不是替换 Connector 本身——容易误用。

2. **`NodifyEditor` 提供 `ConnectionStartedCommand` / `ConnectionCompletedCommand`**
   前者参数是 `PendingConnection.Source`，后者是 `Tuple<Source, Target>`。
   → 可以不自建 PendingConnection 模板；但仍需提供 `PendingConnectionTemplate` 才能控制可见性，最终采用 `PendingConnection.CompletedCommand` + 视图模型读两端，并兼容 Tuple 传参。

3. **`GridSpacing` 不是 `NodifyEditor` 的属性**（属于 `NodifyCanvas`）
   → 编译期 `MC3072` 报错，已移除。

### 视图模型的三条设计约束

1. **坐标只进 `FlowLayoutStore`，绝不写 `StepModel`** → 拖动不递增 Version、不触发重编译（M1-7 的隔离在此落地并被断言验证）。
2. **连线只写 `LinkedSources`**，Version 递增由既有统一写路径（`SetLink`/`RemoveLink`）负责，画布不自建脏标记。
3. **只订阅步骤集合增删，不订阅步骤属性变更** → 改名/改参数不会重建画布，避免与写回形成回环。

### 端口映射：三种步骤的输入端口来源各不相同

| 步骤类型 | 输入端口来源 | 寻址键 | 显示名 |
|---|---|---|---|
| 普通算子 | `InputValues.Keys` | 端口名 | 端口名 |
| `ConditionStep` | `LocalVariables` | **变量 Guid 字符串** | 用户别名 |
| `ForStep` | 隐藏的 `LoopCount` | `"LoopCount"` | 循环次数 |

条件节点用 Guid 作键是既有约定（编译器 `Guid.TryParse(myInputName, …)`、`VariableBindingViewModel` 的 `bindKey = Definition.Description`），画布必须沿用，否则连出去的线编译器读不到。

输出端口 = 插件静态定义（`IPluginProvider.ModulePlugins`）+ 图纸动态端口快照（`OutputPortDefinitions`）合并，与 `FlowQueryHelper` 给绑定弹窗构造候选树的口径一致，避免"画布能连但绑定弹窗选不到"。
`ForStep` 额外补一个 `Index` 输出（对应 `CompiledForNode.IndexPort`，它不在任何端口快照里）。

### 跨层与非步骤来源连线：先降级计数，不假装能画

`LinkReference.TargetStepId` 是全局 Guid，可以指向任意层任意步骤；而 Nodify 的连线只能在同一个 `NodifyEditor` 内画。Spike 阶段：

- `Kind != StepPort`（全局变量/运行时变量/常量）→ 计入 `DeferredLinkCount`，不画线；
- `StepPort` 但目标不在本层（跨层、或上游已被删除）→ 同样计入降级；
- 画布顶部诊断条显示该数量，避免"线凭空消失"的困惑。

M2 再用「假锚点节点 + 虚线 + 双击跳转来源层」降级表达。

---

## 二、修改文件清单

### 新增

| 文件 | 内容 |
|---|---|
| `VisionMaster\ViewModels\CanvasConnectorViewModel.cs` | 端口锚点：`Owner` / `PortName`（寻址键） / `DisplayLabel`（显示名） / `DataType` / `IsInput` / `Anchor` / `IsConnected` |
| `VisionMaster\ViewModels\CanvasNodeViewModel.cs` | 节点：持有 `StepModel`、`StepId`、`Header`、`Location`（双向）、`IsSelected`、`Inputs`/`Outputs`、`IsContainer`、`IsNested` |
| `VisionMaster\ViewModels\CanvasConnectionViewModel.cs` | 连线：只有 `Input`/`Output` 两个端口引用，不复制 StepID（避免同一事实两处真相） |
| `VisionMaster\ViewModels\FlowCanvasViewModel.cs` | 主 VM：`Rebuild` / `BuildNodes` / `BuildConnections` / `GetInputPorts` / `GetOutputPorts` / `CanConnect` / `CreateConnection` / `OnDisconnectConnector` / `RemoveExistingConnection` + `CanvasPendingConnectionViewModel` |
| `VisionMaster\Views\FlowCanvasView.xaml(.cs)` | `NodifyEditor` + `ItemContainerStyle`（Location/IsSelected 双向）+ `ItemTemplate`（`Node` + 输入/输出连接器模板）+ `ConnectionTemplate`（`LineConnection`）+ `PendingConnectionTemplate` + 诊断条 + 空状态提示 |

### 修改

1. **`VisionMaster\VisionMaster.csproj`** — 新增 `<PackageReference Include="Nodify" Version="7.3.0" />`
2. **`VisionMaster\Shell.xaml`** — 流程栏 pane 内新增 `Panel_FlowCanvasView` 停靠面板（与流程栏同 pane 成标签页，可拖出分屏对照）
3. **`VisionMaster\Services\LayoutHelper.cs`**
   - `ContentFactories` 注册 `Panel_FlowCanvasView`
   - `LoadFromString` 在 **Deserialize 之前**校验布局 XML 是否覆盖全部面板，不覆盖则放弃恢复（否则新版本新增的面板永远出不来；且必须在应用前判断——一旦应用旧布局，现场已被改坏，返回 false 也无法回滚）
   - 新增 `LayoutXmlCoversAllPanels`，以 `ContentFactories.Keys` 为权威清单，**以后新增面板注册即生效，无需再改这里**
   - `Reset()` 对过期的 `DefaultLayout.xml` 快照做同样校验，过期则作废删除，让下次启动从当前 XAML 重新捕获

---

## 三、验证结果

### 编译

`dotnet build VisionMaster\VisionMaster.csproj` → **0 错误**，已成功生成。

过程中修掉三处自身问题：

1. `IWorkspaceManager` / `IPluginProvider` 找不到 → 它们在 `VisionMaster.Services` 而非 `Core.Interfaces`，补 using。
2. `GridSpacing` 不是 `NodifyEditor` 属性 → `MC3072`，移除。
3. **订阅对象错误（真 bug）**：`Rebuild` 里把 `PropertyChanged` 挂在了 **FlowModel** 上，但判据是 `CurrentFlow` / `CurrentSolution`——那是 **WorkspaceContext** 的属性名，永远匹配不上，**切换流程画布不会刷新**。改为构造时对 `_workspace as INotifyPropertyChanged` 订阅一次（一次订阅终身有效，也避免反复挂解遗漏），并由断言 [11] 回归验证。

### 运行时验证：42 项断言全部通过

**为什么不用 GUI 冒烟**：本机环境下 VisionMaster 启动后主窗口句柄恒为 0，且日志里
`[WARN] An unexpected error occurred while resolving 'VisionMaster.Shell'` 在**摘掉画布的基线上同样出现**（06:46:41 与 06:50:58 两次运行一致），属既有状态——GUI 测试在此环境无法区分成败。
而 `FlowCanvasViewModel` 不依赖任何 View 类型（只用到 `System.Windows.Point` 这类纯数据），因此映射与写回逻辑可完整非 GUI 验证。

验证方式：临时控制台项目引用 VisionMaster，构造真实 `WorkspaceContext` + `SolutionModel` + `FlowModel`，直接驱动命令并断言图纸与视图模型两侧状态。**验证后已删除，未纳入解决方案。**

| 组 | 覆盖点 | 结果 |
|---|---|---|
| [1] | 步骤→节点映射、`HasFlow`/空提示、节点持有 StepID 与显示名 | OK ×3 |
| [2] | 输入端口取自 `InputValues`、输出端口取自快照、类型解析为 `double` | OK ×3 |
| [3] | 预置连线画出、两端标记已连接、方向正确、无降级 | OK ×4 |
| [4] | **拉线写回 `LinkedSources`**：`Kind=StepPort`、`TargetStepId` 正确、显示地址 `定位.Mark`、**Version 恰好 +1** | OK ×4 |
| [5][6][7] | 反向拉线规范化、同节点/输入连输入被拒、非法连线不改 Version | OK ×5 |
| [8] | **拖动写入 `FlowLayoutStore` 且 Version 不动**（5 → 5） | OK ×2 |
| [9][10] | 断线移除连线与 `LinkedSources`、Version 递增、输出侧断开被忽略 | OK ×5 |
| [11] | **切换流程自动重建**、For 节点 `LoopCount` 输入与 `Index` 输出、容器标记 | OK ×4 |
| [12] | 条件节点端口键 = 变量 Guid、显示名 = 用户别名 | OK ×3 |
| [13][14] | 跨层连线降级计数、全局变量/常量连线降级计数 | OK ×2 |
| [15] | 同一输入重复连线只保留一条、图纸同步、被替换输出端口标记未连接 | OK ×3 |
| [16] | 步骤增删自动重建、删除后布局垃圾项被清理 | OK ×2 |

### 人工待验（需在真实桌面环境启动）

1. 打开「流程画布」面板，确认节点渲染、缩放/平移/框选手感；
2. 拖动节点后保存方案，重开确认坐标保留；
3. 拉线后切到流程栏确认参数面板地址同步，运行确认编译通过；
4. 确认 Nodify 默认样式与项目浅色主题（Fluent）的观感是否可接受，需要时统一配色；
5. 若旧 `Layout.xml` 导致面板未出现，查看是否按预期回退默认布局（日志会打印"已回退默认布局"）。

---

## 四、已知边界

| 项 | 说明 | 归属 |
|---|---|---|
| 只画顶层 | 子画布 + 面包屑未实现，容器节点仅标记 `IsContainer`，双击进入子层是 M2 主体工作 | M2 |
| 跨层/变量/常量连线只计数不表达 | 无假锚点节点、无虚线、无双击跳转，用户看到"少了几根线"需靠诊断条解释 | M2 |
| 输入端口类型恒为 object | 普通算子输入端口类型需从 `IPluginProvider` 的 `InputDefinitions` 取；当前只取 `InputValues.Keys`，因此**画布刻意不做类型校验**（否则会出现"画布拒绝、编译器允许"的矛盾） | M2 |
| 无运行态可视化 | `FocusedStep`/`CurrentNodeId` 高亮、跨层活跃祖先、跟随执行、运行中只读保护全部未做 | M2 |
| 无撤销重做/多选删除/复制粘贴/小地图 | Nodify 提供了能力（`Ready for undo/redo`），但本项目未接 | M2 |
| 全量重建而非增量 | 步骤增删触发整图重建，会丢失当前选中态与视口位置；节点多时有闪感 | M2 |
| 无自动布局按钮 | `FlowLayoutStore.AutoLayout` 已具备能力，UI 未暴露入口 | M2 |
| 布局无缩放/视口持久化 | 只存节点坐标，未存 `Viewport` | M2 |
| 双视图选中不联动 | TreeView 与画布各自选中，`Workspace.SwitchStep` 未与画布打通 | M2 |
| 运行锁未接 | 运行中画布仍可编辑（`ProcessViewModel` 有 `IsRunLocked` 模式可复用），会产生"改了图纸以为改了运行实例"的误解 | M2 |
| `DeferredLinkCount` 是临时诊断字段 | 顶部提示条为 Spike 期产物，M2 应换成降级锚点节点后移除 | M2 |
| 编译期告警噪音 | 存量 200+ nullable 警告与 `HslCommunication` 的 `System.Resources.Extensions` 引用解析失败，均与本次无关 | 存量 |
| 本机 GUI 无法验证 | 主窗口句柄恒为 0 且 Shell 解析 WARN 在基线上同样存在，属既有环境问题；**画布的视觉呈现尚未被任何自动化手段覆盖** | 待人工 |
