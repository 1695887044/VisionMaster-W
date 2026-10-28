# 流程运行统计第四批（RunFlow 判定透出 / 告警 KPI / 步骤时间线）

| 项 | 内容 |
| --- | --- |
| 日期 | 2026-10-28 |
| 范围 | `RunFlowPlugin` 的 `IsOk`/`Outcome` 端口、底栏告警 KPI、轮次步骤时间线 |
| 前序 | 第一/二/三批（结果输出、系统变量、浮窗/下钻、L2 引擎记录） |
| 设计基线 | `docs/UI设计基线/flow-runtime-kpi/` |

## 1. 当前状态

这一批把三个"收口项"补齐：

1. **子流程判定透出**：`调用流程` 步骤新增 `IsOk`（bool）与 `Outcome`（string）输出端口。
   子流程显式 NG 时 `Success=true / Invoked=true / IsOk=false / Outcome="Ng"`，父流程可以用 `IsOk`
   做 `If` 分支；门禁失败仍是 `Success=false`。
2. **告警 KPI 接上**：底栏「告警」不再恒为 `—`，读 `ScadaMonitoringHost.Context.Alarms.UnacknowledgedCount`
   （未确认的**产线告警**，不是日志警告）；后台 1s 采样循环里更新。
3. **步骤时间线**：`FlowRoundResult.Steps`（`FlowStepRecord[]`：步骤名 / State / 耗时）由引擎在轮末快照；
   轮次下钻弹窗新增时间线列表（Success/Failed/Skipped 三色点 + 耗时）。

## 2. 已做

| # | 文件 | 行为 |
| --- | --- | --- |
| 1 | `Plugins/Plugin.RunFlow/RunFlowPlugin.cs` | 新增 `IsOk` / `Outcome` 输出端口；`Invoked` 语义改为"跑完即 true"；NG 不失败 |
| 2 | `Plugins/Plugin.RunFlow/Help/03-ports-and-parameters.md` | 输出端口表从 3 个补到 5 个；写明"产品 NG 不是调用故障" |
| 3 | `FlowCanvasChecks/RunFlowPortChecks.cs` + csproj | RF-1~4：Ng → Success/Invoked/IsOk/Outcome；门禁失败四态 |
| 4 | `Core/Models/FlowRoundResult.cs` | 新增 `FlowStepRecord`（StepId/StepName/State/ElapsedMs/ElapsedText）与 `Steps` |
| 5 | `Engine/FlowEngineService.cs` | `BuildRoundResult` 快照 `session.Blueprints`（耗时取 `LastRunTimeMs`） |
| 6 | `VisionMaster/Shell.xaml` | 轮次详情新增「步骤时间线」列表（状态点/步骤名/State/耗时） |
| 7 | `VisionMaster/ShellViewModel.cs` | 注入 `ScadaMonitoringHost`；后台循环更新 `AlarmCountText` |
| 8 | `VisionMaster/Shell.xaml` | 告警 ToolTip 改为「未确认的产线告警（≠ 日志警告）」 |
| 9 | `FlowCanvasChecks/FlowAutomationChecks.cs` | L2-7：步骤时间线 1 步 / Success / 耗时 ≥ 0 |

## 3. 未做（以及为什么不能"顺手"做）

1. **逐流程结果源配置（`FlowModel.ResultSource` + 变量/步骤端口选择）**：
   需要新增一个配置弹窗/表格列，属于 UI 变更；按 R127 必须先有 Web 设计基线。
   当前已有两条可用路径：结果输出步骤（推荐）、约定变量 `<流程名>_Result`。
   下一批先出配置弹窗的 Web 稿，再复刻。
2. **HTTP / 结果上报 / 数据记录 / Excel 报表迁到 `FlowRoundCompleted`**：
   这些是**流程内插件**，物理上拿不到引擎事件；要让它们消费轮次记录，需要新的宿主契约
   （例如 `IExecutionContext` 上挂"本轮记录"）或把它们改成宿主服务。这不是一个字段替换，
   而是数据源语义重设计，单独一批做。
3. **线级拓扑 / 产品身份（SN/工单）**：
   需要方案级模型（工位顺序、串联/并联、线级结果策略）+ 产品身份贯穿各工位；
   当前底栏「全部」已经按主流程汇总给线级 OK/NG，没有产品身份时不跨站求和是刻意的诚实口径。
4. **按工位拆告警**：SCADA 报警定义目前没有流程/工位归属；底栏只做全局未确认数。

## 4. 下一步

1. 重启 VisionMaster，确认：底栏告警数随 SCADA 报警变化；轮次详情出现步骤时间线；
   子流程判定表 NG 时父流程「调用流程」步骤不再变红，且 `IsOk` 可分支。
2. 出「流程运行统计 / 结果源配置」Web 稿 → 复刻配置弹窗（变量/步骤端口选择）。
3. 设计"轮次记录 → 数据落盘"的宿主契约（上报/数据记录/报表共用）。
4. 线级拓扑与产品身份（SN/工单）单独立项。

## 5. 踩过的坑

1. **`RunFlowPlugin` 的 `Invoked` 旧语义**：以前"跑完但有失败步骤 = false"；L2 后显式 NG 也算跑完，
   必须同时给 `IsOk`/`Outcome`，否则父流程失去 NG 信号。文档已同步改口径。
2. **告警 KPI 的线程**：后台 1s 采样循环本来就在 Task 线程上写 `CpuUsagePercent` 等属性；
   `AlarmCountText` 沿用同一模式。`ScadaMonitoringHost.Context` 在未加载方案时为 null，用 `?.` 保底；
   方案切换/宿主释放的窗口再加一层 try/catch，读不到就显示 `—`，不让一次告警读取失败打断整个采样循环。
3. **步骤耗时取 `LastRunTimeMs`**：跳过/未执行的步骤是 0；时间线里显示 `0 ms` 是真实值，不是没取到。
4. **FlowCanvasChecks 直接引用 Plugin.RunFlow**：以前只从 `Modules\` 反射加载；为了断言新端口，
   csproj 加了 `ProjectReference`，测试用编译期类型（同时也会把 DLL 投递到 Modules）。
5. **全量 gate 的抖动**：本批首跑 16/16 绿；期间若出现 `ScadaWebChecks`/`FlowCanvasChecks` 单条时序红，
   先单跑复验（本仓既有空载抖动），不要直接归因到本批。

## 6. 验证

- `VisionMaster` / `FlowCanvasChecks` / `ScadaChecks` / `UIThemeSmokeTest` 构建 → **0 个错误**。
- `ScadaChecks`：**3195 通过 / 0 失败**。
- `FlowCanvasChecks`：**4277 通过 / 0 失败**（新增 RF-1~4、L2-7）。
- `UIThemeSmokeTest`：**exit 0**（Shell 全树实例化含步骤时间线）。
- `tools\run_gates.ps1`：**16 步全绿**（`gates-final-batch3.log`；`gates-final-batch.log` / `gates-final-batch2.log` 同样 16 步绿）。
- 真机需重启：`Modules\Plugin.RunFlow.dll` 已更新（mtime 16:37:17 > 源码 16:30:13）。
