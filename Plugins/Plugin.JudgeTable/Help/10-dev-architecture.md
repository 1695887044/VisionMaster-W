# 判定表 · 开发：内部结构

> 分类：逻辑判断 ｜ 来源：插件 Plugin.JudgeTable ｜ 读者：改这个插件、或照它写新插件的人

本页讲"代码怎么组织、一次重建/一次执行分别经过哪些步骤"。
宿主契约（哪些接口必须实现、编译器什么时候调谁）在 `11-dev-host-contracts.md`，
必须守住的不变量与断言在 `12-dev-invariants.md`。

## 文件与职责

| 文件 | 职责 | 不做什么 |
| --- | --- | --- |
| `JudgeTablePlugin.cs` | 端口声明、动态端口重建、连线迁移、命令、摘要、执行 | 不实现判定算法 |
| `Models/JudgeRule.cs` | 单条规则的配置 + 运行期回显（INPC，可 JSON 持久化） | 不读端口、不写变量 |
| `Models/JudgeMode.cs` | 两个枚举（`JudgeMode` / `SetSemantics`），成员带中文 `[Display]` | 无逻辑 |
| `Models/JudgeEngine.cs` | 七种模式的纯函数判定 + 数值期望解析 + 编辑距离 | **不碰端口、不碰上下文、不写变量** |
| `JudgeTableView.xaml(.cs)` | 表格 / 展开行 / 结果输出卡片；双击展开；视图就绪信号 | 无业务逻辑 |

这条边界是刻意的：判定口径是本插件唯一"算错了也没人发现"的地方（判错只表现为结果不对），
所以它必须是**能脱离流程逐条对拍的纯函数**。`JudgeTableChecks` 里的模式断言全部直接调
`JudgeEngine.Evaluate`，不经过流程引擎。

## 数据流

```
配置文件 (.vms)                    配置态实例                        编译态实例
──────────────────────────────────────────────────────────────────────────────
InputValues["Rules"]  ──Initialize/ApplyConfigValues──▶  Rules 集合
                                                          │
                                          RebuildAll() ───┼──▶ 动态输入端口 实际_{规则名}（全部规则）
                                                          ├──▶ 动态输出端口 {表名}.{规则名}.结果（启用 && 生成）
                                                          ├──▶ StepData.OutputPortDefinitions（快照）
                                                          └──▶ SyncInputLinks（改名迁移 / 孤儿清理）

运行时：FlowCompiler 把上游输出挂到 Inputs[...].LinkedSource
        RunAlgorithm → JudgeEngine.Evaluate → 写端口 + 写 LocalVariables
```

## 重建管线（`RebuildAll`）

```
RebuildAll()
  ├─ RebuildDynamicInputs()   先按名保值 → 逐个 RemoveDynamicInput + Dispose 旧端口 → 重建 → 回填值
  ├─ RebuildDynamicOutputs()  ClearDynamicOutputs → 只建 Enabled && GenerateResult 的 → 写快照
  ├─ SyncInputLinks()         改名迁移 + 孤儿键清理（仅配置态，StepData != null）
  ├─ RefreshSourceText()      每行「数据源」显示串
  └─ RefreshSummary()         摘要 / 徽标 / 计数通知
```

两个 `Rebuild*` 都调用 `ComputeNames()`，它是**端口名的唯一计算处**：

```
foreach rule in Rules（按表内顺序）:
    stem   = Sanitize(rule.Name, "规则")
    input  = Unique("实际_" + stem, usedInputs)
    output = Unique($"{TableName}.{stem}.结果", usedOutputs)
    rule.InputPortName  = input      // 运行期字段，供界面显示与取值
    rule.ResultPortName = output
```

三条关键约定：

1. **去重集合覆盖全部规则**（含停用行）→ 停用/启用不会改变其它行的端口名。
2. **两个 Rebuild 共用同一份计算**（都调 `ComputeNames`）→ 输入与输出的后缀序号不会漂移。
3. **名字写回规则对象**（`InputPortName` / `ResultPortName`）→ 执行期与界面读同一份事实。

## 端口名清洗（`Sanitize`）

保留：字母、数字、下划线、连字符、点（`char.IsLetterOrDigit` 对中文为真，所以中文名原样保留）。
其余（空白、方括号、逗号、斜杠…）一律变 `_`；首尾的点去掉；结果为空则用兜底名。

**为什么必须洗**：编译期 `FlowCompiler` 用 `^(?<port>[^\[]+)(\[(?<idx>\d+)\])?$` 解析端口名，
名字里带 `[` / `]` 会让整条连线解析失配；运行时变量名直接当 `LocalVariables` 的字典键，
带空白/逗号会让下游「变量赋值」按名寻址失败。

## 表名前缀（`TableName`）

优先级：`_stepName`（从 `Initialize` / `ApplyConfigValues` 的 `stepData.StepName` 取）
→ `InstanceName`（且不等于默认值 `未赋值`）→ `判定表`。

**为什么不用 `InstanceName` 优先**：运行期 `FlowCompiler` 把 `InstanceName` 拼成
`{流程名}.{步骤名}`，而配置态实例由宿主 `Activator.CreateInstance` 创建后**不再赋值**（仍是 `未赋值`）。
端口名必须"配置态快照 == 编译期实例"才能被下游按名连上，所以两边都取 `StepName`。

## 连线迁移（`SyncInputLinks`）

`_inputAnchors: Dictionary<JudgeRule, string>` 记住每条规则**上一次**用过的输入端口名。

```
for each rule:
    newName = rule.InputPortName
    if anchor != newName && StepData.IsLinked(anchor):
        if !StepData.IsLinked(newName):  把连线从 anchor 搬到 newName，提示"已迁移"
        else:                            移除 anchor（提示"新端口已有连线，请检查"）
    anchor = newName
清理：StepData 里所有不在 valid 集合中的连线键 → RemoveLink + 提示
```

`IStepLinkInventory.GetLinkedPortNames()` 给的是**快照**（`ToList`），所以可以边遍历边删。

**为什么非做不可**：孤儿连线键在 `FlowCompiler.LinkPorts` 里是**错误**
（`没有名为 X 的输入端口，这条连线不会生效`），一条就能让整条流程编译失败；
而它没有同名端口，画布与行内都够不到它。

## 执行（`RunAlgorithm`）

```
enabled = Rules.Where(r => r.Enabled)          // 顺序 = 表内顺序 = Results 下标
for i in enabled:
    actual = ResolveActual(rule, out missing)   // 端口缺失 / 未连线 / 上游 null 三种原因分开
    outcome = actual == null ? 失败(missing) : JudgeEngine.Evaluate(rule, actual)
    rule.LastActual/LastOk/LastReason/LastFormula = outcome
    results[i] = outcome.IsOk
    details.Add($"规则名: 期望 … / 实际 … / 原因 …")
    if rule.HasResultPort:
        Outputs[rule.ResultPortName].Set(isOk)         // 动态端口
        context.LocalVariables[rule.ResultPortName] = isOk  // 运行时变量（同名）
IsOk/FailedCount/Results/Details/Reason ← 汇总
整表 NG → Fail(summary)；否则记一行 Info
```

**这里绝对不重建端口**：编译期已经把手上的上游输出挂到端口对象上，
重建会换掉端口对象、把连线一起丢掉（下游永远读到默认值）。
重建只发生在配置态（`Initialize` / `ApplyConfigValues` / 视图就绪 / 规则变化）。

**单条规则异常**：`JudgeEngine.Evaluate` 内部已 catch 成"该条不通过"；
`RunAlgorithm` 里还有一层 catch 兜"取值/写端口"这类外围异常。两层都不让异常穿透。

## 摘要与状态

`RefreshSummary()` 只发通知、不改数据：`EnabledCount` / `DisabledCount` / `PassCount` /
`FailCount` / `GeneratedCount` / `SummaryText` / `OverallBadge` / `OverallOk` / `TablePortText`。
其中 `PassCount` / `FailCount` 读的是每行**上一次运行**的 `LastOk`，
所以"摘要"与"结果列"永远同源（不会一个显示 OK、另一个显示 NG）。

## 命令族

| 命令 | 参数 | 空参/错型行为 |
| --- | --- | --- |
| `AddRuleCommand` | 无 | 追加 `规则N` 并展开 |
| `RemoveRuleCommand` | `JudgeRule` | 非目标类型**静默空跑**（不抛） |
| `MoveUpCommand` / `MoveDownCommand` | `JudgeRule` | 同上；到边界时 `CanExecute` 为 false |
| `ExpandAllCommand` / `CollapseAllCommand` | 无 | 只改展开态 |
| `CopyTextCommand` | `string` | 空/非字符串静默空跑；剪贴板被占用只写状态消息 |

参数类型全部是 `object?` + 首行 `is` 判型（AGENTS §7 R27：绑定来源是 `PlacementTarget.DataContext`
或行模板 DataContext 时不检查类型，强类型泛型会静默空跑）。

## 视图

- `DataContext` = 插件实例（宿主靠它取 `IPluginConfigView.OnConfirm`）。
- 表格 `ItemsSource=Rules`，`SelectedItem=SelectedRule`（工具条三个按钮用它）。
- 行模板是 `ListViewItem` 的 `ControlTemplate`：`GridViewRowPresenter` + 展开详情块。
  `R19-R22` 三件套（`GridListViewStyle` / `ItemContainerStyle` / `GridViewRowPresenter`）
  与就地合并 `GridView.xaml` 一个都不能少。
- 视图只做绑定；唯一 code-behind 是"双击行展开"与"视图就绪 → 插件重建端口"。
- 空态提示由 `ZeroToVisibleConverter` 看 `Rules.Count`。
