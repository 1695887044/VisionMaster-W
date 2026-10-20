# 2026-10-20 等待节点参数弹窗空引用（ConditionEditor.Branches 未初始化）

## 现象

真机打开「等待_0」的参数弹窗时，应用日志出现：

```text
[WARN] [UI] Object reference not set to an instance of an object.
```

17:24:52 / 17:24:53 / 17:25:01 / 17:25:29 连续四次；弹窗没有正常打开，界面只剩步骤树与日志。
该异常由 `ExceptionGrading` 按 Dispatcher 未处理异常记为 Warning（`AppLifetimeService.cs:64-68`），
只记录了 `ex.Message`，所以应用日志里看不到堆栈。

## 根因

`ConditionEditorViewModel.OnDialogOpened` 的等待模式顺序：

```text
IsWaitMode = true;          // setter 里调用 RefreshDraftDiagnostics()
...
Branches = new ObservableCollection<StepCollection> { waitDraft }; // 实际草稿此时才赋
```

`Branches` 在构造函数里没有初始化（`ConditionEditorViewModel` 只有属性声明）。
`IsWaitMode` 的 setter 立即触发 `RefreshDraftDiagnostics()` → `CollectWaitIssues()` →
`foreach (var br in Branches)`，此时 `Branches == null` → **NullReferenceException**。

分支匹配（Case）模式同样先 `IsCaseMode = true`、后赋 `Branches`，是同一根因的潜在路径；
For 模式不遍历 `Branches`，不受影响。

独立 WPF 探针（`C:\Users\16958\AppData\Local\Temp\WaitProbe`，临时工程）复现出完整堆栈：

```text
System.NullReferenceException
   at ConditionEditorViewModel.CollectWaitIssues() ... line 422
   at ConditionEditorViewModel.RefreshDraftDiagnostics() ... line 335
   at ConditionEditorViewModel.set_IsWaitMode(Boolean value) ... line 94
   at ConditionEditorViewModel.OnDialogOpened(IDialogParameters parameters) ... line 610
```

## 修复

1. `VisionMaster\ViewModels\DialogViewModels\ConditionEditorViewModel .cs`
   - 构造函数里先给空集合：`Branches = new ObservableCollection<StepCollection>();`
   - 这样 `IsWaitMode` / `IsCaseMode` 的 setter 在真实草稿赋值前触发诊断时，
     `CollectWaitIssues` / `CollectCaseIssues` 遍历的是空集合，不再 NRE。
   - 之后 `OnDialogOpened` 仍按原逻辑赋真实草稿（等待模式一条合成分支 / Case 模式 N 条）。
2. `FlowCanvasChecks\ConditionEditorV2Checks.cs`
   - 把原先**未被 `Run()` 调用**的 `CaseModeMatchesDesign()` / `WaitModeMatchesDesign()` /
     `ForModeMatchesDesign()` 接入 `Run()`；本次 NRE 正是这些死测试没跑导致的盲区。
   - Case 测试先 `caseStep.Children.Clear()` 再铺 A/B/默认三条：`CaseStep` 构造器本身预建
     `[Case 1, 默认]`，不清空会得到 5 行，旧期望 3 行会误报。

## 验证

| 层 | 结果 |
| --- | --- |
| 等待弹窗探针 | 修复前：NRE（堆栈见上）；修复后：`VM opened: IsWaitMode=True branches=1` → `WINDOW OK: 1120x760` → `RESULT: OK` |
| `FlowCanvasChecks` | **2817 通过 / 0 失败**；CE2-D8~D14（Case/Wait/For 三模式）全部执行并通过 |
| `UIThemeSmokeTest --auto-port-config` | **33/33 PASS**（自动参数面板未回归） |
| 宿主构建 | **0 错误**（仅既有警告） |

## 说明

- 该异常与「无自定义视图插件自动参数面板」无关：等待节点是内置节点，参数弹窗走
  `ConditionEditor`，不经过 `AutoPortConfigView`。本次是用户打开「等待_0」时暴露的既有缺陷。
- `[UI]` 日志只记录 `ex.Message` 的现状没有改（`ExceptionGrading.Describe` 会展开 InnerException
  但不带堆栈）；本次通过独立探针拿到堆栈。若后续还要快速定位 Dispatcher 异常，可另立
  “UI 异常首次出现时记录完整 ToString”的改进项。
