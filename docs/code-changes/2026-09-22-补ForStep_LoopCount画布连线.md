# 开发记录：补 ForStep LoopCount 画布连线（关闭「已知边界 2」）

- 日期：2026-09-22
- 类型：功能实现（画布建线 + 参数弹窗入口 + 地基修复 + 执行层断言）
- 触发场景：上一轮（`2026-09-22-流程引擎优化-A1A2A3B1B2B3.md`）在「已知边界 2」里主动放弃了一项——ForStep 的 `LoopCount` 编译期早就支持 `LinkKind.RuntimeVariable`（`FlowCompiler.LinkPorts` 会造 `RuntimeVariableProxyPort` 并挂到 `CompiledForNode.LoopCountLink`），但**画布上没有任何入口能建这条线，建了也没有任何表征能看见它**。用户只能双击 For 节点、在弹窗文本框里手填次数，「循环次数由上游变量决定」这条能力等于不存在。本轮把这条边界闭合。

---

## 一、需求与决策

### 黑洞的准确形状

不是「后端没做」，而是「后端做完了，前端两头都缺」：

| 环节 | 本轮之前状态 | 表现 |
|---|---|---|
| 编译期挂线 | 已支持（`LinkPorts` 认 `RuntimeVariable`） | 手写 JSON 塞进 `.vms` 能生效 |
| 运行期取值 | 已支持（`RuntimeVariableProxyPort` 读 `LocalVariables`） | 同上 |
| 画布建线入口 | 无 | 拖线只会写 `LinkKind.StepPort`，拖不出变量线 |
| 画布可见性 | 无 | 即便线已存在，`BuildConnections` 只认 `StepPort`，一律计入 `DeferredLinkCount`（隐形） |
| 弹窗入口 | 只有次数文本框 | 没有「选数据源」的地方 |

所以本轮必须同时做「建线」和「可见」，否则就是上一轮注释里写的那句：*单独做后端会造出一个「能连但不显示」的黑洞*。

### 五项已确认决策

| 决策点 | 选定方案 | 理由 |
|---|---|---|
| 建线入口 | **两者都做**：画布拖线 + For 参数弹窗「选择数据源」 | 两种心智模型都存在（图形化组态 / 表单式配置），两条入口写**同一条** `LinkReference`，不做两套存储 |
| 画布上「变量来源」的表征 | **变量定义节点动态长出「值输出脚」**，端口名 = 变量名 | 不绑死 For.LoopCount：任何步骤的输入脚都能连变量值脚，一次把「取运行时变量」这件事图形化；且复用插件静态输出口的同一条渲染管线，画布不需要新控件 |
| 已存在变量线的渲染 | **源在本层 → 实线；异层/定义已删 → 降级隐形**（`DeferredLinkCount`） | 下钻进循环体后源节点不可见，画幽灵线或标红都是骗人；降级只影响可见性，**绝不动图纸数据**（不替用户删线、不标非法） |
| 建线期类型校验 | **刻意不做**，交给编译器 `TryValidateLink` | 画布上普通算子输入脚类型多为 `object`，在画布做严格判定会出现「画布拒绝、编译器却允许」的口径矛盾；真不匹配时编译期会以红框 + 错误列表暴露 |
| 弹窗入口路线 | **复用单绑通道 `DataBindView` + 修两处地基** | 不另写一棵候选树、不另写一套写回逻辑；地基不修的话弹窗拿到的候选集合与画布不同源 |

### 关键决策：变量线里为什么不存「定义步骤 Id」

三元组固定为 `TargetStepId = LinkProtocol.RuntimeVariableMarkerGuid`、`TargetPortName = 变量名`、`DisplayAddress = "Runtime.{变量名}"`，**刻意不写定义步骤的 `StepID`**：

> 变量的身份是名字（运行期按名从 `context.LocalVariables` 取），存了 Id 反而多出一套「定义步骤被删 / 被改名」的失效判定，而编译器本来就不看这个字段。

代价是还原连线时**只能按变量名回找源端口**，于是 `BuildConnections` 里多了 `variableProducers` 这张「变量名 → 本层值脚」字典，同名变量按本层枚举顺序后者覆盖前者——与 `FlowQueryHelper` 的「以最后一次定义为准」同一口径（运行期也是覆盖写 `LocalVariables`）。

### 关键决策：识别口径必须只有一份

「哪个步骤是变量定义节点、它声明的变量叫什么、类型是什么」这条规则，画布（长值脚）、绑定弹窗（候选树）、断言工程三处都要用。原来只在 `FlowQueryHelper` 内部私有实现，本轮抽成公开方法 `FlowQueryHelper.TryGetDefinedVariable(step, out name, out type)`，画布与弹窗同调。识别规则本身仍是**类型名字符串包含 `VariableDefinitionPlugin`**（不反射外部插件 DLL），因此断言工程在没引用 `Plugins/Plugin.Utility` 的情况下也能造出合法桩。

---

## 二、修改文件清单

### 产品侧

| 文件 | 状态 | 内容 |
|---|---|---|
| [CanvasConnectorViewModel.cs](file:///e:/VM/VisionMaster-W-master/VisionMaster/ViewModels/CanvasConnectorViewModel.cs) | 新增属性 | `IsRuntimeVariablePort`。为什么要有显式标记而不是「从 `Owner.Model` 反查」：建线时要立刻决定写哪种 `LinkKind`，反查等于把识别规则再实现一遍（还要处理同名），而**端口自己最清楚自己是哪一类** |
| [FlowCanvasViewModel.cs](file:///e:/VM/VisionMaster-W-master/VisionMaster/ViewModels/FlowCanvasViewModel.cs) | 改 4 处 | ①`GetOutputPorts` 返回值加第三项 `IsVariablePort`，末尾按 `TryGetDefinedVariable` 追加值脚（变量名与插件输出口同名时不重复长脚）；②`ConfigurePorts` 挂端口时把标记带到 `CanvasConnectorViewModel`；③`OnCompleteConnection` 按源端口标记分派 `LinkKind`，值脚走 `RuntimeVariable` 三元组；④`BuildConnections` 先建 `variableProducers` 字典，`RuntimeVariable` 线按名回找 → 命中画实线、未命中 `DeferredLinkCount++` |
| [FlowQueryHelper.cs](file:///e:/VM/VisionMaster-W-master/Engine/FlowQueryHelper.cs) | 地基① | `GetUpstreamNodes` 由「只判 `ConditionStep`」改为「判 `IContainerStep` 接口并递归」。**旧实现的漏洞**：While 靠继承侥幸覆盖，For 的子层整个漏掉，表现是「循环体里定义的变量在下游绑定弹窗里选不到，而运行期 `LocalVariables` 明明有值」；②`TryGetDefinedVariable` 提为 `public static`，`GetRuntimeVariableDefinitions` 改为调用它 |
| [VariableBindingViewModel .cs](file:///e:/VM/VisionMaster-W-master/VisionMaster/ViewModels/DialogViewModels/VariableBindingViewModel%20.cs) | 地基② | 新增 `DialogParameters` 键 `TargetStep`（优先级高于 `Workspace.CurrentStep`）。注释写明原因：**下钻进容器时 `CurrentStep` 指向外层容器而不是被双击的步骤**，拿到的上游集合是错的（循环体外定义的变量反而选不到）；顺带把常量文案改用 `LinkProtocol.ConstantDisplayPrefix` |
| [ConditionEditorViewModel .cs](file:///e:/VM/VisionMaster-W-master/VisionMaster/ViewModels/DialogViewModels/ConditionEditorViewModel%20.cs) | For 模式弹窗入口 | ①构造时回填草稿：`LoopCountText = DefaultLoopCount`、`LinkedSources.TryGetValue("LoopCount", out _loopCountLinkDraft)`、`LoopCountSourceText`（修掉「打开弹窗即丢连线显示」）；②`LinkLoopCountCommand → OnLinkLoopCount()` 以单绑模式打开 `DataBindView`（`TargetPortName="LoopCount"`，键名必须与画布输入脚、编译期挂接三方一致）；③`UnlinkLoopCountCommand`；④`SaveForLoop()` 已连线时放宽文本校验（留空 = 沿用模型原有默认值），并在草稿 ≠ 活模型现值时才 `SetLink/RemoveLink`，最后 `CurrentFlow.Version++` |
| [ConditionEditorView.xaml](file:///e:/VM/VisionMaster-W-master/VisionMaster/Views/DialogViews/ConditionEditorView.xaml) | For 面板 | 加「数据源」只读框（空值提示 *未连线（用左侧默认次数）*）+「🔗 选择数据源」+「✖ 解除」（`HasLoopCountLink` 控制可见）。读/写都是弹窗草稿，**点「保存并应用」才落到活模型，取消即丢弃** |

**编译器与运行时零改动**：`FlowCompiler` / `CompiledForNode` / `RuntimeVariableProxyPort` 本轮一行未动，只是第一次被前端入口真正喂到——这也是判定「上一轮确实是缺 UI 而非缺后端」的依据。

### 断言侧

| 文件 | 状态 | 内容 |
|---|---|---|
| [Harness.cs](file:///e:/VM/VisionMaster-W-master/FlowCanvasChecks/Harness.cs) | 修改 | 新增 `Variable(变量名, 类型关键字, 步骤名)` 桩步骤（用假类型名，理由见上节「识别口径只有一份」）；`Node/Out/In` 端口查找辅助保持公开 |
| [Program.cs](file:///e:/VM/VisionMaster-W-master/FlowCanvasChecks/Program.cs) | 新增 `[R]` 段 | `RuntimeVariablePortAndLink()` 22 条：值脚存在性 → 拖线建线 → 撤销重做 → 存盘往返 → 异层降级 → 全量重建回找 → 候选树递归子层 |
| [ExecutionHarness.cs](file:///e:/VM/VisionMaster-W-master/FlowCanvasChecks/ExecutionHarness.cs) | 新增（未入库） | `ExecHarness.Prepare` 编译 + 起引擎的最小夹具、`StubLog`（可按片段查 Warn）、`CountingPlugin`、`LoopVarPlugin`（往 `LocalVariables` 写 `loopN`）。**刻意不继承 `CountingPlugin`**，否则 `Runs` 会被变量定义那一笔污染 |
| [ExecutionChecks.cs](file:///e:/VM/VisionMaster-W-master/FlowCanvasChecks/ExecutionChecks.cs) | 新增 `[E7]` 段 + 接入 `Program.Main` | 12 条真跑断言：编译挂线、圈数来自变量而非默认值、脏值回落 + Warn、负数按 0 次 + Warn、缺键现行为 |

---

## 三、验证结果

### 编译

```
dotnet build FlowCanvasChecks/FlowCanvasChecks.csproj
已成功生成。 1 个警告（HslCommunication 的 System.Resources.Extensions 存量引用告警） 0 个错误
```

### 断言

`dotnet run --project FlowCanvasChecks`：**通过 278 / 失败 0**（本轮新增 `[R]` 22 + `[E7]` 12 = **34 条**）。

| 分组 | 条数 | 钉住的事实 |
|---|---|---|
| R1 值脚存在性 | 4 | 变量定义节点长出 `Outputs=[loopN]`、带 `IsRuntimeVariablePort`、类型取自 `Type` 参数（`Int32`）；节点自身输入仍是 `Name/Type`；**全画布只有这一个值脚**（普通步骤不长脚） |
| R2 拖线建线 | 8 | `CanConnect/CreateConnection` 放行；落成 `Kind=RuntimeVariable`、`TargetStepId` 是 marker Guid 而非定义步骤 Id、`DisplayAddress=Runtime.loopN`；走统一写路径 → `Version 2→3`；两端端口点亮；本层定义 → `deferred=0 illegal=0` |
| R3 撤销重做 | 2 | Undo 后图纸引用与画布连线一起消失（`count=0`）；Redo 恢复 Kind/变量名/marker 三要素 |
| R4 存盘往返 | 3 | 落盘文本里 `"Kind": 3`（**不再靠 `DisplayAddress` 前缀猜**）；往返后连线身份不变；往返后定义步骤仍能长出值脚（`Name/Type` 存成裸字符串 → `loopN/Int32`） |
| R5 异层降级 | 2 | 定义节点被删 → `deferred=1 illegal=0`（不画线、不标红、不告警）；且**降级不动图纸**，画布不替用户删线 |
| R6 通用性 | 2 | 值脚同样能喂普通算子输入口（不限 For）；全量重建后仍能按变量名回找回实线（`count=1 deferred=0`，不依赖建线那一刻的即时 `Connections.Add`） |
| R7 地基 | 1 | 候选树递归进 For 子层，拿到循环体内定义的变量（旧实现只认 If） |
| E7 执行层 | 12 | 见下表 |

### 逐字契约核对（[E7] 执行层）

| 断言 | 实测 detail |
|---|---|
| 编译期把变量线接成代理端口 | `loopN/Int32` |
| 圈数由变量值决定（3 圈），不是弹窗默认 99 | `实际 3` |
| 弹窗默认次数仍留在节点上 | `99` |
| 脏值回落默认次数且不抛穿流程 | `实际 99` + `Warn:For节点 '变量圈数For' 的 LoopCount 输入值 [abc] 无法转为整数，回落默认次数 99：The input string 'abc' was not in a correct format.` |
| 负数圈数按 0 次处理 | `实际 0` + `Warn:For节点 '变量圈数For' 循环次数为负 (-1)，按 0 次处理` |
| 变量缺键（现行为，非理想行为） | `实际 0` |

### 断言有效性佐证（防假绿）

- **[R]/[E7] 不是空跑**：R4 首版断言写 `reloaded.Flows.Single()` 直接抛 `Sequence contains more than one element`——因为 `SolutionModel.Flows` 的字段初始化器预置了 `GoHome/MainTask`，而 `ObjectCreationHandling.Replace` 只把 JSON 里的项接进集合、**不清空预置项**，往返后是 3 条流程。定位后改断言为 `Single(f => f.FlowName == "画布断言流程")` 并注释根因。**这是断言假设错，不是产品缺陷**，故未改产品代码。
- **编译期反证**：`ExecutionChecks` 首版用属性模式 `RuntimeVariableProxyPort { DataType: int }` 报 `CS8121`——`DataType` 是 `Type`，`int` 会被当成常量值比较。改成 `as` + `== typeof(int)`，顺带说明这类断言若写成模式形式会静默失真。
- **未做「摘掉修复看变红」反证的项**：R2/R6 只断言了结果集合，没有回滚产品代码复跑。风险有限（同段有 `deferred` 计数交叉约束，单方面放宽会立刻与 R5 冲突），但严格意义上属于「逻辑闭环」而非「实验闭环」。

---

## 四、已知边界

| # | 项 | 说明 | 归属 |
|---|---|---|---|
| 1 | **变量缺键时 For 跑 0 圈，不回落默认次数** | `RuntimeVariableProxyPort.Value` 在未 bind / 缺键且目标为值类型时返回 `Activator.CreateInstance(int)` = **0，不是 null**；`CompiledForNode` 只在 `Convert.ToInt32` **抛异常**时才 Warn 回落 `DefaultLoopCount`。于是「连了线但上游没执行」= 循环 0 次，与用户直觉（回到弹窗默认次数）不一致。已被 `[E7]` 末条按现行为钉死 | 需要产品决策：给代理端口加 `TryGetValue` 语义，或在 For 侧区分「取到 0」与「取不到」 |
| 2 | **建线期不做类型校验**（本轮明确选择） | `CanConnect` 只做结构判定；类型不匹配要到编译期以红框 + 错误列表暴露。画布输入脚多为 `object`，画布比编译器更严会造成矛盾 | 保持现状 |
| 3 | **弹窗保存后画布不会立刻长出新线** | `StepModel.SetLink` 只是属性级变更，`OnNodePropertyChanged` 只处理 `Location`，不触发 `Rebuild`。表现为「弹窗里已显示 Runtime.loopN，画布上还得等下一次重建才看见线」 | 后续可让 `DataBindView` 写回路径显式调 `RebuildLayer` |
| 4 | **改量名后值脚可能滞后** | 端口在**每次渲染**全量重挂（刻意不订阅步骤属性变更），所以「在变量定义节点里改变量名」本身不触发渲染，旧名字的值脚会残留到下一次重建 | 与 #3 同一条约束，改名级联（2026-09-17 记录）只覆盖全局变量存储层，不覆盖这条画布路径 |
| 5 | **下钻进 For 子层后看不到外层连线** | 进入子层后 For 自身成为层根，它的 `LinkedSources` 不参与该层 `BuildConnections`；这是分层视图固有约束，只能靠 `DeferredLinkCount` 提示「本层有隐形线」 | 保持现状（与「异层降级」决策一致） |
| 6 | **Undo 的连线复原依赖显式重建** | 撤销后 `Connections` 归零不是 Undo 命令自己摘线，而是 `Undo()` 里补调的 `RebuildInPlace()`（`ReorderCommand` 等会二次重建，冗余但幂等）。以后若新增「只改 `LinkedSources` 的命令」，忘了这句就会出现图纸已回滚、画布仍连线 | 结构性约束，注释已写在 `Undo()` 里 |
| 7 | **常量线 / 全局变量线仍只走弹窗** | `BuildConnections` 只对 `StepPort` 与 `RuntimeVariable` 两类还原实线，其余 Kind 一律计入 `DeferredLinkCount`。本轮只补了变量一类 | 待同类需求出现再做 |
