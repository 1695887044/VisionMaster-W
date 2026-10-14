# 2026-10-12 SCADA 事件动作新增流程启停（StartFlow / StopFlow / RunFlowOnce）

- 日期：2026-10-12
- 范围：`Scada`（`Models\ScadaActionType.cs`、`Models\ScadaAction.cs`、`Models\IScadaFlowRunner.cs` **新契约**）+ `VisionMaster`（`Services\ScadaFlowRunner.cs` / `Services\IScadaFlowPicker.cs` / `Services\ScadaFlowPicker.cs` **新增**、`Services\ScadaActionDispatcher.cs`、`ViewModels\ScadaEventEditorViewModel.cs`、`ViewModels\ScadaPropertyViewModel.cs`、`ViewModels\DialogViewModels\ScadaVariableEventDialogViewModel.cs`、`ViewModels\DialogViewModels\ScadaFlowPickerViewModel.cs` **新增**、`Views\DialogViews\ScadaFlowPickerView.xaml(.cs)` **新增**、`Views\Controls\ScadaEventEditor.xaml`、`App.xaml.cs`）+ `ScadaChecks`（`Program.cs`：四个假件 + FlowActionDispatchChecks 14 条 + ⑨组 4 条 + 7 处编辑器装配点补桩）
- 类型：SCADA 事件动作能力扩展（+3 种动作类型、+1 条领域契约、+1 个宿主桥接服务、+1 个选择流程弹窗、事件编辑器补流程参数面板）
- 起因：SCADA 评审（2026-10-12，多角色）发现事件动作只有 Log / WriteVariable / Navigate 三种——「点按钮跑检测流程」配不出来，SCADA 面板触不到流程引擎；用户裁决语义为「**点一次就启动，循环流程点一次一直跑**」；架构确认引擎侧已有全套原语、无需新增（见 §一）
- 事实来源：实现 / 架构 / reviewer 交接（文中数字均为实测）；docs 落盘前核对——改动清单所列文件全部在盘（`Scada\Models\` 三件、`VisionMaster\Services\` 五件、ViewModels / DialogViewModels 四件、`Views\DialogViews\` 与 `Views\Controls\` 三件、`ScadaChecks\Program.cs`），本文件名与当日既有记录（`2026-10-12-B包十五项收尾…`）不冲突

---

## 一、背景：评审缺口与架构确认

SCADA 评审（2026-10-12，多角色）盘点事件动作：只有 Log / WriteVariable / Navigate 三种，最典型的现场需求「点一个按钮跑一遍检测流程」配不出来——SCADA 面板触不到流程引擎。

用户裁决动作语义：**「点一次就启动，循环流程点一次一直跑」**——即 SCADA 的「启动流程」对齐主界面「连续运行」的语义，而不是"单跑一遍就停"。

架构确认（引擎已有全套原语，本批零引擎改动）：

| 引擎已有 | 说明 |
| --- | --- |
| `FlowEngineService.RunSessionAsync` | 连续循环 |
| `FlowEngineService.TryRunSessionOnceAsync` | 单次运行 |
| `FlowEngineService.StopSession` | 停止 |
| `TryOccupySession` 抢锁 | 重复启动被抢锁拒绝；且 **`IsRunning` 置位在首个 await 之前同步完成**——启动调用返回后立刻读快照即可判成败，没有竞态窗口 |
| `FlowModel.FlowID` | **string 型稳定键**，可直接作 SCADA 动作的绑定目标 |

另一个裁决：`FlowTriggerMode` **全仓无运行期消费（死字段），本批不复活它**——不给 SCADA 动作另立一套触发模式，启动语义直接对齐主界面「连续运行」。

## 二、设计与裁决

### 2.1 三种动作类型：幂等口径分三档（枚举末尾追加，落盘纪律）

| 值 | 动作 | 语义 | 幂等口径 |
| --- | --- | --- | --- |
| 4 | StartFlow「启动流程」 | 连续运行（对齐主界面） | **已在运行 = 幂等成功**——与 `IScadaNavigator`"目标页本来就在显示 → 返回 true"同一哲学 |
| 5 | StopFlow「停止流程」 | 停止会话 | **未运行 = 幂等成功**——要的终态已达成 |
| 6 | RunFlowOnce「执行一次」 | 单次运行 | **不幂等**：已在运行返回 false 并说清楚（`流程正在运行，本次未执行`）——单次语义下重复点击必须可见，静默吞掉第二次点击等于少跑一次检测 |

枚举同步补 `DisplayName` / `PendingReason`。

### 2.2 新领域契约 IScadaFlowRunner

- 放 `Scada\Models`（领域层），**零 WPF、零引擎依赖**——SCADA 域不感知流程引擎的任何类型；
- 三方法一律 `out reason`、**不抛异常**；
- 寻址 **Id 优先、名字兜底**（`FlowModel.FlowID` 本就是 string），比较 `OrdinalIgnoreCase`。

### 2.3 Stop 豁免停用门（2026-10-12 架构裁决）

停用/加密只挡"**让它跑**"（`CheckStartable` 只守 StartContinuous / RunOnce 两路），**不挡"让它停"**；存在性门（流程不存在 / 名字对不上）三方法都保留。

理由：现场急停入口永远要能按——流程被停用后可能停在半路，SCADA 按钮必须还能把它停下来。

### 2.4 模型携带流程目标 + 分发器注入

- `ScadaAction` 加 `FlowId` / `FlowName`（**string 型**——`FlowModel.FlowID` 本就是 string，区别于变量/画面的 Guid）+ `HasFlow` / `BindFlow`；`Detail` / `Describe` 补流程目标描述；
- `ScadaActionDispatcher` 构造**第 5 参可选注入** `IScadaFlowRunner`——保住 `ScadaChecks` 既有 16 处构造点不破；
- Ok / Skipped / Failed 三档结果与**审计绑定同处**产出；RunOnce 的**完成回执只记日志、不落审计**（`ScadaAuditWriter` 非线程安全、仅 UI 线程）。

### 2.5 宿主桥接：ScadaFlowRunner（VisionMaster 新服务）

- 会话准备走既有 `FlowSessionFactory.TryEnsureSession`；
- 启动配方与 `ShellViewModel.RunAllEnabledContinuous`（`ShellViewModel.cs:1103-1183`）**同源**——同一套"哪些步骤会跑、怎么跑"的口径，**两处必须同步改**；
- 连续启动**火忘**（不 await 跑完），调用返回后**立刻读 `IsRunning` 快照判成败**——架构确认置位在首个 await 前同步完成，快照无竞态。

### 2.6 编辑 UI 与选择流程弹窗

- `ScadaEventEditorViewModel` 加 `PickFlowCommand`（构造**第 7 参** `IScadaFlowPicker`）；
- 流程参数面板加在**共享件** `Views\Controls\ScadaEventEditor.xaml`——图元事件 / 画面事件 / 变量事件三个弹窗宿主自动生效；
- 新弹窗 `ScadaFlowPickerView` **刻意用 ListBox**、避开 `ListView + GridView`——不进 R19-R22 的契约面（不用挂 `GridListViewStyle`、不需要 `GridViewRowPresenter` 行模板与就地合并字典三件套）；
- `App.xaml.cs` 注册全套（runner / picker / 弹窗 VM）。

## 三、改动清单

| 文件 | 改动 |
| --- | --- |
| `Scada\Models\ScadaActionType.cs` | 枚举末尾追加 StartFlow=4 / StopFlow=5 / RunFlowOnce=6 + `DisplayName` / `PendingReason` |
| `Scada\Models\ScadaAction.cs` | 加 `FlowId` / `FlowName` / `HasFlow` / `BindFlow`；`Detail` / `Describe` 补流程目标描述 |
| `Scada\Models\IScadaFlowRunner.cs` | **新增**：流程启停领域契约（零 WPF 零引擎依赖；out reason、不抛异常；Id 优先名字兜底 OrdinalIgnoreCase） |
| `VisionMaster\Services\ScadaFlowRunner.cs` | **新增**：宿主桥接服务——`TryEnsureSession` 会话准备 + 与 `ShellViewModel.RunAllEnabledContinuous` 同源的启动配方 + 火忘 / `IsRunning` 快照判成败 |
| `VisionMaster\Services\IScadaFlowPicker.cs` + `ScadaFlowPicker.cs` | **新增**：流程选择器契约与宿主实现 |
| `VisionMaster\Services\ScadaActionDispatcher.cs` | 构造第 5 参可选注入；`ExecuteFlowAction` 四档分发（启动 / 停止 / 单次 / 未接入） |
| `VisionMaster\ViewModels\ScadaEventEditorViewModel.cs` | `PickFlowCommand` + 流程行；构造第 7 参 `IScadaFlowPicker` |
| `VisionMaster\ViewModels\ScadaPropertyViewModel.cs` | ctor / 建行点透传 |
| `VisionMaster\ViewModels\DialogViewModels\ScadaVariableEventDialogViewModel.cs` | 5 处装配点透传 |
| `VisionMaster\ViewModels\DialogViewModels\ScadaFlowPickerViewModel.cs` | **新增**：选择流程弹窗 VM |
| `VisionMaster\Views\DialogViews\ScadaFlowPickerView.xaml(.cs)` | **新增**：选择流程弹窗（刻意 ListBox，避开 R19-R22 契约面） |
| `VisionMaster\Views\Controls\ScadaEventEditor.xaml` | 流程参数面板（三宿主共享件） |
| `VisionMaster\App.xaml.cs` | 注册 runner / picker / 弹窗 VM |
| `ScadaChecks\Program.cs` | FakeFlowRunner / FakeFlowPicker / StubRuntimeManager / StubFlowEngine 四个假件；FlowActionDispatchChecks 14 条；⑨组 4 条；7 处编辑器装配点补 FakeFlowPicker |

## 四、测试侧修正（与本功能无关，同批落地）

- `ScadaChecks` [PI] 段 `PackagingChecks` 的方案往返断言改为**不清空 `solution.Flows`**。
- 起因：`Core\SolutionService.cs:158` 的 `MandatoryFlows.Ensure`（2026-10-12 凌晨**另一任务**接入）在加载时补齐 Home / Main / End 骨架流程——断言里 `Flows.Clear()` 后存出 `"Flows": []`，读回必被补齐，往返断言的前提被打破。
- **不是产品 bug，产品代码零改动**（reviewer 已核：`SolutionService.cs` 时间戳 01:00，早于本批全部文件）。
- **附记（待办）**：`MandatoryFlows.Ensure` 接入本身至今没有 code-changes 记录，归昨晚任务名下待补。

## 五、验证证据

- 构建：`dotnet build VisionMaster.csproj` / `dotnet build ScadaChecks.csproj` 均 **0 错误**（仅既有的 HslCommunication `MSB3245` 版本冲突警告）。
- 断言：`dotnet run` ScadaChecks —— **通过 1710 / 失败 0，全部断言通过**（新增 FlowActionDispatchChecks 14 条 + ⑨组 4 条）。
- reviewer 独立复跑同计数（其间两次遇 `AppConfig.json` `File.Replace` 环境性文件锁中断，重跑消除——非代码问题）。
- 断言输出样例（幂等 / 单次拒绝 / 未接入三档各有据）：

```text
…,李四,启动按钮,按下,启动流程 主检测,成功,已在运行，未重复启动
流程正在运行，本次未执行
流程执行能力未接入
```

## 六、本批钉死的口径（守门点）

1. **幂等分三档**：StartFlow 已在运行 = 成功、StopFlow 未运行 = 成功、RunFlowOnce 已在运行 = **失败且说清楚**（FlowActionDispatchChecks 守门；单次语义不许吞第二次点击）。
2. **Stop 豁免停用门**：停用/加密只挡"让它跑"、不挡"让它停"；存在性门三方法都保留（⑨组 4 条守门：停用挡启动 / 停用不挡停止 / 停用未运行幂等 / 存在性门保留）。
3. **启动配方同源**：`ScadaFlowRunner` 与 `ShellViewModel.RunAllEnabledContinuous`（`ShellViewModel.cs:1103-1183`）是同一套口径的两份落点，**改一处必须同步另一处**。
4. **RunOnce 回执不落审计**：只记日志（`ScadaAuditWriter` 非线程安全、仅 UI 线程）。
5. **契约零依赖**：`IScadaFlowRunner` 放 `Scada\Models`，零 WPF、零引擎依赖；不抛异常，一律 out reason。
6. **流程选择弹窗刻意 ListBox**：不进 `ListView + GridView` 的 R19-R22 契约面（无 `GridListViewStyle` / 行模板 / 就地合并字典三件套负担）。
7. **FlowTriggerMode 不复活**：死字段维持死状态，SCADA 启动语义对齐主界面「连续运行」。

## 七、未决与已知限制（如实登记）

1. **运行中改名边界（reviewer P2-3）**：会话按 `FlowName` 注册（`_runtime.GetSessionByName`），运行中的流程被改名后，Stop 按新名查不到旧会话 → **假幂等**"本就未在运行"（旧会话还在跑）；StartContinuous 会重建新名会话而旧会话继续跑。这是会话按名注册的既有边界，本批是第一个"按名单停"的消费方；将来根治方向：会话注册补 FlowID 侧键。
2. **连续路径文案盲区（reviewer P2-2 家族）**：StartContinuous 快照失败文案「未启动：会话正被其它触发占用（HTTP / 定时 / 手动）」与 `ShellViewModel.cs:1167` **逐字同源**（刻意取舍），同样不覆盖"流程被锁定"档；要改须连 shell 一起改，超出本批"不动 ShellViewModel"约束，记账待办。
3. **真机未验证**：选择流程弹窗与三处参数面板的开窗手感需实机确认（样式已静态核验自包含，无 R22 类资源断链风险）；引擎侧端到端（真起一个流程）建议真机点一次「启动流程」动作。
4. **P2-1 已收尾**：火忘任务注释补明「前置 ArgumentException 档经本类不可达（`TryEnsureSession` 保证 ExecutionEngine 非空）」。
