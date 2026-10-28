# 开发：契约与不变量

## 1. 类与接口

```
ResultOutputPlugin : VisionPluginBase, IFlowResultSource
IFlowResultSource  （Shard/Core.Interfaces）
    bool IsOk { get; }
    string Reason { get; }
```

宿主（VisionMaster）在编译期不引用插件工程，插件是运行时从 `Modules\` 反射加载的。
宿主只判 `plugin is IFlowResultSource`，**不按类名字符串匹配**——类名或命名空间一改，
字符串匹配就静默失联。接口放在 `Core.Interfaces`，插件与宿主都引用它。

`ResultOutputPlugin` 实现的是**显式接口**（`bool IFlowResultSource.IsOk => ...`），
避免与公开的 `InputPort<bool> IsOkPort` 属性重名；端口名仍然是 `IsOk` / `Reason`。

## 2. 端口与编译器

| 属性 | 端口名 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- | --- |
| `IsOkPort` | `IsOk` | bool | 是 | 本轮业务判定 |
| `ReasonPort` | `Reason` | string | 否 | NG 原因/摘要 |

`IsOkPort.IsRequired = true`。编译器的必填检查（`FlowCompiler` 的 `[参数缺失]`）口径是：

1. 有上游连线 → 通过；
2. 没有连线但手填了常量 → 通过；
3. 两者都没有 → 编译期报错，不会等到运行期静默按 `false` 跑。

手填常量是允许的（测试用），但运行期会记一条 Warn。生产流程应接上游判定。

## 3. 执行语义

`RunAlgorithm` 只做三件事：

1. 读 `IsOkPort.GetTypedValue()` / `ReasonPort.GetTypedValue()`；
2. 记一行日志（未接线时多一条 Warn）；
3. `Success.Value = true`。

**NG 时步骤仍然 Success。** 判定是正常结果；步骤失败只表示"没拿到判定"。
这条不能反过来：如果 NG 时 `Success=false`，父流程和「调用流程」会把产品 NG 当技术故障
（判定表 NG 恰好会 `Fail()`，`FlowInvoker` 现在正是把"有步骤 Failed"当调用失败）。

## 4. 宿主读取时序

宿主在 `FlowRunCompleted`（每轮一次，连续/单次都触发）里同步读取：

1. 取 `session.CompiledFlowId`（= `FlowModel.FlowID`）作为归属键；取不到才按流程名兜底。
2. 遍历 `session.Blueprints`（深表，含分支/循环内步骤）。
3. 只考虑 `step.State == Success` 的步骤：被跳过/未到达的步骤不读，避免沿用上一轮。
4. `session.ExecutionEngine.PluginLookup.TryGetValue(step.StepID, out var plugin)`。
5. 取 `plugin is IFlowResultSource` 的步骤；多个命中时取 `LastRunStartTimestamp` 最大的
   （最后执行的那个），并记一条 Warn。
6. 读 `IsOk` / `Reason`；`IsOk=true` 计 OK，`false` 计 NG。
7. 一个都没命中 → 回退约定变量 `<流程名>_Result`；再读不到 → 计「未判定」。

读取发生在流程线程、轮末同步完成，只读一个 bool/string，不阻塞下一轮。

## 5. 归属与生命周期

- 统计按 `FlowId` 分桶，不维护"最后开轮的那条流程"：多流程并发时后者会把 A 的轮次记到 B 上
  （2026-10-27 图像清屏事故同源）。
- 流程改名：变量按 `FlowId` 生成的确定性 Id 不变；显示名由目录 `UpdateName` 刷新。
- 流程删除：目录按来源快照增删；SCADA 按 Id 绑定的画面在流程删除后自然解析不到。
- 换方案：运行统计清零（系统变量不落盘，重启/换方案从头开始）。
- 调试轮（`session.DebugEnabled == true`）：跳过计数与节拍，不污染生产 KPI。

## 6. 并行、循环与 Return

- **并行分支**：本步骤只读写自己的端口，标了 `[ParallelSafe]`；多个分支各有一个实例，
  宿主取最后执行的那个。若一条流程里出现多个结果输出步骤，编译/保存时应提示。
- **循环**：最后一次执行的值生效；一个轮次多件产品需要另一种计数源（一票/轮 ≠ 一票/件）。
- **Return**：`Return` 会跳过后面的步骤；结果输出放在 `Return` 之前，否则本轮记「未判定」。
- **主路径末尾**：放在最后一个判定步骤之后；放在判定之前会把上一轮/默认值当本轮判定。

## 7. 兼容变量归一化

旧方案没有本步骤时，宿主读约定变量 `<流程名>_Result`，按值归一化：

| 值 | 判定 |
| --- | --- |
| `true` / `false`（bool） | 直接 OK / NG |
| `"OK"` / `"PASS"` / `"TRUE"` / `"合格"` / `"良品"` | OK |
| `"NG"` / `"FAIL"` / `"FALSE"` / `"不合格"` / `"不良"` | NG |
| `1` / `0`（int/long） | OK / NG |
| 空串 / null / 其它文本 | 未判定 |

约定变量模式要求判定变量**每轮无条件写**；若流程可能跳过写入步骤，宿主无法区分
"本轮没写"和"上一轮的值"，应改用本步骤或加轮次戳。

## 8. 为什么不在插件里写变量

- 系统变量 `SystemVariableModel` 是只读派生量，不实现 `IWritableVariable`，插件写不进去。
- 写全局变量会把统计退回用户变量的生命周期问题：改名/删除/复制流程要同步、老方案要迁移、
  用户可删可改、值随方案落盘成幽灵数据。
- 本步骤只声明判定；节拍/当前节点/计数由宿主统计，单一事实源。

## 9. 线程与性能

- 插件自身无状态（除端口值），不启动线程、不订阅事件。
- 宿主读取在流程线程轮末完成；多个流程并发时互不共享可变状态。
- 日志一行，不逐节点打印；不要把本步骤放在高频循环里做重活。

## 10. 失效模式

| 现象 | 原因 | 处理 |
| --- | --- | --- |
| 编译报 `[参数缺失] IsOk` | 没连线也没填常量 | 接上游判定或先填常量 |
| 底栏一直「未判定」 | 步骤被跳过/在 Return 之后/没放步骤 | 放主路径末尾、Return 之前 |
| 明明 NG 却记 OK | `IsOk` 接错（上一轮变量/别的工位） | 检查接线与判定逻辑 |
| 步骤 Failed 但没记 NG | 预期行为：Failed 是技术故障 | 产品 NG 用 `IsOk=false` 表达 |
| 多个结果输出 | 多个步骤互相覆盖 | 每条流程只留一个 |

## 11. 测试矩阵

| 用例 | 期望 |
| --- | --- |
| `IsOk=true` | 接口 true；步骤 Success |
| `IsOk=false` | 接口 false；步骤仍 Success |
| `Reason` 为空 | 接口返回空串，不返回 null |
| 未接线手填常量 | 运行成功 + Warn |
| 分支未走到 | 宿主记「未判定」，不沿用上一轮 |
| 两个结果输出 | 取最后执行，记 Warn |
| 调试轮 | 不计入生产 KPI |
| 流程改名 | 按 Id 仍能读到 |

## 12. 代码落点与构建

- 插件：`Plugins/Plugin.Utility/ResultOutputPlugin.cs`
- 接口：`Shard/Core.Interfaces/IFlowResultSource.cs`
- 宿主读取与统计：`VisionMaster/Services/FlowRuntimeVariableSource.cs`
- 系统变量投影：`Core/Binding/FlowRuntimeVariableCatalog.cs`
- 手册：`Plugins/Plugin.Utility/Help/ResultOutputPlugin/`（本目录）
- 构建：重建 `Plugin.Util`（红线③）；`tools\run_gates.ps1` 会核 DLL 时间与内嵌资源数。
