# 图像预处理 · 开发：架构与数据流

> 分类：图像处理 ｜ 来源：插件 Plugin.PreProcessing ｜ 读者：开发

本页只讲三件事：**这个插件由哪些文件构成、运行时数据怎么流、`HImage` 归谁释放**。
参数怎么显示、宿主给了哪些免费能力，见 `11-dev-host-contracts.md`；改坏了会出什么现象，见 `12-dev-invariants.md`。
主源码：`Plugins\Plugin.PreProcessing\PreProcessingPlugin.cs`（约 1210 行）。

---

## 1. 一句话架构

**一条算子链 + 一个共用执行引擎 + 两条调用路径。**

预览和正式运行不是两套代码，都走同一个 `ExecuteChain`，这是"配置界面看到什么、流程跑出来就是什么"的唯一保证。
两条路径的差别只在三处：**要不要留住中间图**（`stepResults` 传数组还是传 null）、**在哪个线程跑**（UI/后台/流程线程）、**要不要给端口赋值**。

```
                 ┌──────────────── 配置界面（UI 线程）────────────────┐
算子库点击 ──► Operators 活链 ──► WriteChain ──► Chain 快照 ──► 落盘 InputValues
                 │                                     ▲
                 │ SchedulePreview(200ms 防抖)         │ ApplyConfigValues
                 ▼                                     │
         RefreshPreview ──► ExecuteChain(stepResults=数组) ──► _stepImages ──► DisplayImage
                                                          
流程线程 ──► RunAlgorithm ──► Operators.ToArray() ──► ExecuteChain(stepResults=null) ──► Image.TypedValue
```

---

## 2. 文件与职责地图

| 文件 | 职责 | 改动风险 |
| --- | --- | --- |
| `PreProcessingPlugin.cs` | 流程节点 + 配置 ViewModel 双身份；端口、命令、预览、画布同步 | 高（全插件的状态都在这里） |
| `Models\PreprocessOperator.cs` | 算子抽象基类：`[PreprocessOperator]` 注册特性、`Apply`、参数元数据、`SaveParams`/`LoadParams` | 高 |
| `Models\OperatorDescriptor.cs` | 注册表里的一条元数据（Key、显示名、分类、排序），用于算子库分组显示 | 中 |
| `Models\OperatorRegistry.cs` | 反射扫描全程序集建注册表，按最小 `Order` 分组出分类 | 中 |
| `Models\OperatorEnums.cs` | 算子参数用的枚举，全部只有 `[Description]`（见 §11） | 高（数值即存盘格式） |
| `Models\OperatorStepItem.cs` | 弱类型存盘单元：`OperatorKey` + `Enabled` + `Dictionary<string,string> Params` | 高（格式即兼容） |
| `Operators\*.cs` | 25 个算子实现，按 color / geometry / filter / morphology / enhance / binary / edge 七类 | 低（单算子局部） |
| `Services\PreprocessHService.cs` | 所有 HALCON 调用与已实测的坑位修正，纯静态、无状态 | 中 |
| `PreProcessingView.xaml(.cs)` | 自有编辑视图：画布 + 算子库 + 处理链 + 参数面板 | 中 |

**分层原则**：算子只做"参数校验 + 调 `PreprocessHService`"，不碰端口、不碰 UI、不碰日志；
`PreprocessHService` 只做 HALCON，不认插件类型；`PreProcessingPlugin` 只做编排与状态。
新加算子时不要把这三层混着写。

**边缘类算子的所有权提醒**：`Sobel 边缘` / `Laplace 边缘` 在 `Process` 里先调 `EnsureMono`——
彩色输入会产生一张临时灰度图，必须用 `ReferenceEquals(mono, input)` 判断该不该释放；
无条件 `Dispose` 会把上游的图一起释放掉（这是本插件历史上最严重的一类崩溃来源）。

---

## 3. 双身份：为什么一个类既跑流程又当 VM

类头注释里写明了职责边界，这是本项目沿用 `CreateRoiPlugin` 的既有做法：

- 主程序给"配置"和"编译执行"**各造一个实例**，所以同一个类被两条路径各用一半，互不干扰；
- 作为流程节点时它只用到 `RunAlgorithm`、端口、`Chain`/`Operators` 快照；
- 作为 VM 时它只用到 `Operators`、`SelectedOperator`、预览状态、`CanvasRois`、命令。

推论：**配置实例上的预览代码永远跑不到正式流程里**，正式流程也永远不会去动 `_stepImages`。
不要为了"复用"把 `DisplayImage` 之类绑到端口上，也不要指望 `RunAlgorithm` 里改过的东西会显示在配置界面。

---

## 4. 两份"链"：活链 `Operators` 与存盘快照 `Chain`

| | `Operators` | `Chain` |
| --- | --- | --- |
| 类型 | `ObservableCollection<PreprocessOperator>` | `List<OperatorStepItem>` |
| 定位 | 界面上真正编辑的强类型活链 | 落盘用的弱类型影子 |
| 标记 | 无 | `[StepConfig]`（基类统一读写） |
| 变更通知 | `CollectionChanged` + 每项 `PropertyChanged` | 无 |
| 线程 | UI 线程（除 `ToArray()` 快照） | 任意（基类灌值/取值） |

三条同步路径：

1. **活链 → 快照**：`WriteChain()`，内部先判 `_rebuilding` 再调 `SnapshotOperators()`。
   `OnConfirm` 前、链或参数变更后都会调；`SnapshotOperators()` 是"只造快照、不写 `Chain`"的半截版本，
   被"复制整链"和"导出清单"共用——那两条路径要的就是这份数据，但不该顺手改掉存盘配置。
2. **快照 → 活链**：`RebuildOperators()`，由 `ApplyConfigValues` 在基类灌完值之后调用。
   整个过程用 `_rebuilding = true` 罩住，`finally` 里放开；先给每个旧项解绑 `PropertyChanged`，再 `Operators.Clear()`，
   然后 `AppendStepsCore(Chain, unknown)`；认不出的 Key 收进 `unknown`，拼成 `LoadWarning`
   （`有 N 个算子在当前版本中不存在，已跳过：…`）。
3. **快照 → 活链内核**：`AppendStepsCore` 是唯一一个"按 Key 造算子、灌参数、追加到链尾"的地方，
   读盘重建与"粘贴到链尾"共用它，两者差别只在前面有没有先清链。
   **调用方必须自己罩 `_rebuilding`**，并在放开后统一补一次 `WriteChain` / `SchedulePreview`，
   否则每 Add 一条就回写一遍快照、重算一遍预览。

`_rebuilding` 与 `OnOperatorsChanged` / `OnOperatorPropertyChanged` 的组合是本插件最容易改坏的地方，
具体后果见 `12-dev-invariants.md` 的 A4 与 B20（批量追加要罩 `_rebuilding`）、B19（反射灌参数不会触发通知，末尾要补发）。

---

## 5. 运行时状态字段清单

| 字段 | 线程 | 含义 | 谁写 |
| --- | --- | --- | --- |
| `SrcImage` / `Image` / `StepCount` / `ActiveCount` / `ActiveOperators` | 流程线程 | 端口，见 §6 | `RunAlgorithm` |
| `Operators` | UI | 活链 | 命令、粘贴、`RebuildOperators` |
| `SelectedOperator` | UI | 选中项，setter 会联动预览步骤号 | 界面、`RebuildOperators` |
| `_stepImages` | UI | `Dictionary<int, HImage>` 每步输出，**透传步故意不存** | `ApplyStepResults` |
| `_sourceImage` | UI | 预览底图，指向 `SrcImage.ActualValue`（不拥有） | `RefreshPreviewCore` |
| `_previewTarget` | UI | `Source` / `Input` / `Output` 三选一的画面来源 | `ApplyPreviewState` |
| `_previewStepIndex` | UI | 当前看第几步，初值 -1 | `PreviewStepIndex` |
| `_previewDebounce` | UI | 200ms `DispatcherTimer`，参数框逐字符刷新用 | `SchedulePreview` |
| `_previewRunning` | UI | 后台是否正在跑 | `RefreshPreviewCore` / `CompletePreview` |
| `_previewDirty` | UI | 跑的同时又请求了刷新 → 跑完补一轮 | `RefreshPreviewCore` |
| `_previewGeneration` | UI | 轮次号，回投时不一致就丢弃结果 | `RefreshPreviewCore` |
| `CanvasRois` | UI | `DrawingObjectInfo` 集合，同一时刻至多一个"编辑框" | `RefreshCanvasBox` |
| `_syncingCanvas` / `_fromCanvas` | UI | 两个方向的防回环开关 | 画布同步 |
| `LoadWarning` / `ActionHint` | UI | 提示文案（`SetHint` 写） | 各类命令 |

`UiDispatcher` 是 `Application.Current?.Dispatcher` 的只读封装；拿不到（关窗后、无 UI 的离线跑流程/单元测试）就走同步分支。

---

## 6. `RunAlgorithm` 逐步（流程线程）

按源码顺序，每一步都对应一个"不能改"的理由：

1. `var src = SrcImage.ActualValue;` —— 取上游图。**不复制**，所有权仍归上游端口。
2. `if (src == null || !src.IsInitialized()) { Fail("输入图像为空或未初始化"); return; }`
   —— 唯一的硬失败入口，此时**没有**释放任何东西（本来也不该释放）。
3. `DisposeOldOutput();` —— 见 §8，先置空端口再释放旧图。
4. `steps = Operators.ToArray();` —— **在流程线程上拍活链快照**。
   注释写得很直白：`ObservableCollection` 不是线程安全的，配置界面可能同时在增删步骤，
   流程线程绝不枚举活链，只读这份数组。后面算端口统计也只用 `steps`。
5. `final = ExecuteChain(src, steps, stepResults: null, context.Logger);` —— `null` 表示不留中间图，跑一步扔一步。
6. `catch (Exception ex)` → `Fail($"图像预处理失败：{ex.Message}")` + `context.Logger.Error($"{InstanceName} {ErrorMessage.Value}")`。
   业务 catch 存在的意义是带上下文前缀，比基类兜底的"异常类型: 消息"好排查。
7. `if (ReferenceEquals(final, src)) final = PreprocessHService.Clone(src);` —— **全禁用/全透传时必须复制**，
   否则输出端口挂的是上游的图，基类 `Dispose()` 会把它释放掉，上游下次拿到的就是一张死图。
8. `Image.TypedValue = final;` —— 唯一给输出图赋值的地方。
9. 端口统计：`StepCount.Value = steps.Length`；`ActiveCount` 取 `steps` 里 `Enabled` 的个数；
   `ActiveOperators.Value = active.Count == 0 ? "(全部禁用)" : string.Join(" → ", active)`（显示名按执行顺序串起来）。
10. `if (DisplayViewIndex > 0) TryPublishPreview(final, context);` —— 推图到主界面视图 1~9。
11. **结尾不写 `Success`**：基类进入本方法时已预置 true，只有失败路径需要显式写 false（`Fail` 已负责）。

`TryPublishPreview` 整个包在 try/catch 里，失败只 `Warn("{InstanceName} 预览推送失败：…")`。
理由是推图属于"锦上添花"，投递失败绝不能把一次正常的预处理判成失败。
显示的图由显示层自己 `CopyImage` 独占副本，所以这里传出去仍是端口持有的那张。

---

## 7. 链执行引擎 `ExecuteChain`

签名：`HImage ExecuteChain(HImage src, IReadOnlyList<PreprocessOperator> steps, HImage?[]? stepResults, ILogService? logger)`

循环内四种走向，顺序不能调整：

1. **算子禁用** → `if (stepResults != null) stepResults[i] = current; continue;`
   原样透传。注释点名了参考实现的坑：那里"禁用就不给输出赋值"，于是下一个算子拿到的是**再上一步的陈旧图像**。
2. **`op.Apply(current)` 抛异常** → `logger?.Warn($"{InstanceName} 算子[{op.DisplayName}] 执行失败，已跳过：{ex.Message}")`，
   该步填 `current`，`continue`。图像继续往下流，**整条流程不会红**。
   这是刻意设计：现场调参时某个算子参数填错，不该让整条链报废。
3. **正常返回** → 先 `stepResults[i] = result`（若非 null），再查 `op is BoxOperatorBase { WasBoxClamped: true }`，
   命中就 `Warn("… 编辑框超出图像范围，已自动夹取到图内执行")`。
   预览路径没有 logger，靠算子摘要里的"（越界已夹取）"提示，两边都有交代。
4. **收口** → `if (ReferenceEquals(result, current)) continue;` 说明该算子对当前输入无从下手（透传）；
   否则 `if (currentOwned && stepResults == null) current.Dispose();` 用完即弃，
   然后 `current = result; currentOwned = true;`。

返回值可能是 `src` 本身（全透传），**是否复制由调用方决定**，见 §6 第 7 步。
`src` 归调用方，本方法一张都不释放——这是本插件所有权约定的第一条。

---

## 8. `HImage` 所有权与释放矩阵

| 图像 | 谁创建 | 谁释放 | 时机 |
| --- | --- | --- | --- |
| `SrcImage` 上游图 | 上游插件 | 上游/端口体系 | 本插件绝不释放 |
| `ExecuteChain` 里 `result` 且 `stepResults == null` | 算子（HALCON 新图） | `ExecuteChain` 自己 | 被下一步替换掉的瞬间 |
| `ExecuteChain` 里 `result` 且 `stepResults != null` | 算子 | `_stepImages` → `DisposeStepImages` | 下一轮 `ApplyStepResults` 开头 |
| 透传帧（与 `passthroughBase` 同引用） | 后台副本 `working` | 后台 `finally` | `Task.Run` 结束立刻 |
| `final` 交出去的那张 | 算子或 `Clone(src)` | `DisposeOldOutput` / `base.Dispose()` | 下次运行前 / 节点销毁 |
| 预览副本 `working = src.CopyImage()` | `RefreshPreviewCore`（UI 线程） | 后台 `finally` | 后台算完 |
| `_sourceImage` | 不是它创建的 | 不释放 | 只是 `SrcImage.ActualValue` 的引用 |
| 画布 `DrawingObjectInfo` | 画布同步 | 控件正常路径；`Dispose()` 兜底遍历 | 见 §10 |

四条最容易踩的规则：

- **`DisposeOldOutput()` 是"先置空再释放"**：`Image.Value = null!;` 然后 `old.Dispose()`。
  因为 `TypedValue` 走 `SetProperty` 的相等判断，若新值引用与旧值相同就不发通知，端口里会攥着一张已销毁的图。
- **旧预览图只在回投完成的那一刻释放**（`ApplyStepResults` 开头调 `DisposeStepImages`）。
  原实现是"跑之前就释放"，那期间画布手里还攥着已销毁的句柄。
- **`_stepImages` 里故意不存透传结果**：`DisplayImage` 和 `InputImage` 都靠"查不到就继续往前找"的语义工作。
  `InputImage` 从 `_previewStepIndex - 1` 往前找第一张真正产出了图的步骤，找不到才退回 `_sourceImage`；
  直接判第 i-1 步没有就退原图，画面会莫名跳回上游。
- **`Dispose()` 顺序**：`_previewDebounce.Stop()` → `DisposeStepImages()` → `_sourceImage = null` → 遍历 `CanvasRois` 调 `info.Dispose()` → `base.Dispose()`。
  `DrawingObjectInfo.Dispose` 内部有 disposed 标志，重复调用安全；输出端口的图交给 `base.Dispose()` 统一释放，这里不要抢。

`HRegion` 方面：本插件对外只输出图像，**自己不持有区域**。
只有 `PreprocessHService.Threshold` / `VarThreshold` 内部会临时生成区域做 `region_to_bin` 一类转换，
全部在 `finally` 里释放。新增算子若需要区域，照这个写法，不要把 `HRegion` 存进算子字段。

---

## 9. 预览三段式

`RefreshPreviewCore(bool waitForResult)`：

1. `_sourceImage = SrcImage.ActualValue;` + `var steps = Operators.ToArray();`
   —— 快照时刻即结果：后台跑的同时用户还能继续加算子改参数。
2. 空图 / 未初始化 / 链为空 → `DisposeStepImages()` + `OnPropertyChanged(nameof(DisplayImage))` 后返回。
3. `waitForResult` 或 `UiDispatcher == null` → 直接 `ApplyStepResults(ExecuteSteps(src, steps), src)` 同步跑
   （导出路径 `RefreshPreviewSync` 要的就是"此刻链跑完的中间图"，`WriteExport` 会读 `_stepImages`）。
4. `_previewRunning` 已为真 → 只置 `_previewDirty = true` 后返回（不叠加，也不丢最后一次）。
5. `src.CopyImage()` 失败（内存紧张）→ 退回同步跑，宁可慢一点也别没有预览。
6. 正常异步：`_previewRunning = true`，`generation = ++_previewGeneration`，
   `Task.Run(() => ExecuteSteps(working, steps))`，`finally` 释放副本，
   `.ContinueWith` 里 `dispatcher.BeginInvoke(() => CompletePreview(task, generation, working))`。
7. `CompletePreview`：先 `_previewRunning = false`；
   仅当 `task.IsCompletedSuccessfully && generation == _previewGeneration` 才 `ApplyStepResults`；
   最后若 `_previewDirty` 则清标志并补跑一轮。

`ExecuteSteps` 把整轮包在 try/catch 里，**预览路径的异常不弹框**，能显示多少步就显示多少步，剩下的步留空。

`DisplayImage` 的来源三分支：`Source` 给 `_sourceImage`；`Input` 给 `InputImage`；
默认（`Output`）查 `_stepImages[_previewStepIndex]`，查不到退回 `_sourceImage`——画面不会"空白"。

**为什么全图运算必须防抖**：对比度算子默认 101×101 掩膜，一次 `Illuminate` 在大图上就是秒级，
而参数框是逐字符触发刷新的。200ms `DispatcherTimer` + `_previewRunning/_previewDirty` 是同一问题的两半，
拆掉任意一半都会退回"点一下卡一下"。

---

## 10. 画布编辑框同步

同一时刻画布上只有一个可编辑框，固定名字 `BoxRoiName = "编辑框"`（链上算子再多也一样）。

- `RefreshCanvasBox()`：选中项是 `BoxOperatorBase` 且参数齐全 → 保证 `CanvasRois` 里有那一个框；否则清空。
- `OnCanvasRoisChanged`：集合被控件改动 → 回写参数或调 `ClearBoxParamIfUserRemoved`。
- `OnCanvasRoiTuplesChanged`：框的 `Tuples` 变了 → 把坐标灌回算子参数并触发预览。
- `_syncingCanvas`：VM 主动改集合时置位，避免自己触发的 `CollectionChanged` 再回写参数。
- `_fromCanvas`：响应控件回传时置位，避免参数 setter 再把框画回去。

`IsBoxBaseDisplayed()` 用来判断当前是否需要显示框（只有选中的框类算子需要）。
`DeleteBoxCommand` 是画布右键/工具条上的删除入口，删完要把算子的框参数清掉。
这三个防回环标志属于"违反即静默出错"的典型，展开在 `12-dev-invariants.md` 的 B 组。

---

## 11. 参数元数据与显示路径（要点）

- 算子参数的可见性由 `PreprocessOperator.GetParamProps()` 过滤：`[SuperDisplay]` + `CanRead` + `CanWrite` + `DeclaringType != typeof(PreprocessOperator)`。
- 枚举下拉的中文来自 `Models\OperatorEnums.cs` 上的 `[Description("中文")]`，走本插件自有 FlatPropertyGrid 的 `EnumGenerator`；
  宿主自动参数面板的 `StepConfigOption.ForEnum` 读的是 `[Display]`，而算子参数不是端口，所以碰不到那条路径。
  **AGENTS.md R40 列的枚举 `[Display]` 欠账在 `OperatorEnums.cs` 上仍未补**，现状不影响本插件界面，
  但这些枚举一旦被搬进自动参数面板就会退回英文成员名。
- 存盘里枚举值存的是**数字**，布尔存 `"1"`/`"0"`，double 用 `"R"` 往返，全部 `InvariantCulture`。
  因此枚举成员只允许往末尾追加，改名/改序/改数值都会让老方案里的参数读成新含义。

---

## 12. 改动定位表

| 想做的事 | 动哪里 | 必看页面 |
| --- | --- | --- |
| 加一个算子 | `Operators\` 新类 + `[PreprocessOperator]` 特性 + `PreprocessHService` 方法 | 12 页 A3、B28、B29 |
| 加一个参数 | 算子类属性 + `[SuperDisplay]` + `[DefaultValue]` + `Normalize` 夹取 | 12 页 A5、B 组夹取条目 |
| 加一个枚举成员 | `OperatorEnums.cs` 末尾追加 | 12 页 A2 |
| 改端口 | `PreProcessingPlugin.cs` 端口区 + `RunAlgorithm` 赋值 | 11 页端口契约 |
| 改预览节奏 | `_previewDebounce` / `_previewRunning` / `_previewGeneration` | 9 节、12 页 B 组预览条目 |
| 改画布交互 | `RefreshCanvasBox` 与两个防回环标志 | 10 节、12 页 B 组画布条目 |
| 改存盘格式 | `OperatorStepItem` 与 `SaveParams`/`LoadParams` | 11 页持久化一节 |
