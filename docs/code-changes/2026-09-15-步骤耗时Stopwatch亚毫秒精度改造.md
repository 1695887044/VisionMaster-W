# 开发记录：步骤耗时 Stopwatch 亚毫秒精度改造（C 方案）

- 日期：2026-09-15
- 类型：精度改造（承接《延时插件Success契约修复与耗时徽章优化》中"耗时精度仍为整数 ms"边界项）
- 动机：`DateTime.Now` 墙钟 + `(long)` 截断导致亚毫秒步骤显示为 0、徽章消失；墙钟还受系统校时影响

---

## 一、需求与决策

| 决策点 | 结论 | 理由 |
|---|---|---|
| 计时源 | `Stopwatch.GetTimestamp()/GetElapsedTime()`（.NET 7+ API，本项目 net9.0） | 高精度性能计数器、单调不回拨，不受 NTP/改表影响 |
| 存储类型 | `double` 毫秒（曾评估 float：32 位天然原子、平台免疫，但全链路要 `(float)` 强转且与 Stopwatch/TimeSpan 返回的 double 生态衔接差） | x64 是铁前提（Halcon 无 32 位运行时），撕裂风险为零；double 留统计余量 |
| 显示格式 | `{0:0.#} ms`（一位小数、尾零省略：`0.4 ms`、`498.2 ms`、`2 ms`） | 防虚假精度（调度抖动 ±0.1ms 级，不显示两位小数） |
| 上一轮的 `<1 ms` MultiDataTrigger | **移除** | 亚毫秒直接显示真值，兜底触发器退役 |
| 计时原语归属 | StepModel 新增 `BeginTiming()/EndTiming()/LiveElapsedMs()`，三个调用方收敛到一处 | 消除 CompiledNode/ProcessViewModel/PluginTestRunner 三处重复的减法运算 |
| `LastRunStartTime` 改名 | → `LastRunStartTimestamp`（long?，Stopwatch 原始读数） | 语义诚实：存的不是"时刻"是"计数器读数"；确认无 XAML 绑定，仅 4 处代码引用 |

## 二、修改文件清单

| 文件 | 改动 |
|---|---|
| `Core\Models\ProcessStep\StepModel.cs` | `LastRunStartTime(DateTime?)→LastRunStartTimestamp(long?)`；`LastRunTimeMs/CurrentRunTimeMs(long)→(double)`；新增 BeginTiming/EndTiming/LiveElapsedMs；ResetState 同步；字段注释锁死 x64 原子性前提（撕裂读风险书面化） |
| `Core\Models\Compileds\CompiledNode.cs` | Running→`step.BeginTiming()`；Success/Failed→`step.EndTiming()` |
| `VisionMaster\ViewModels\ProcessViewModel.cs` | TickRunningTimes 改用 `LiveElapsedMs()`（200ms 定时器实时值） |
| `Engine\PluginTestRunner.cs` | MarkStepState 参数 `long→double`；Running→BeginTiming；调用点 `sw.ElapsedMilliseconds→sw.Elapsed.TotalMilliseconds`（试运行同步拿到亚毫秒） |
| `UI\Controls\Converters\GreaterThanZeroConverter.cs` | **补 double/float 分支**——原实现只认 long/int，double 会一律返回 false 导致徽章全灭（本次最大暗坑，已排除） |
| `VisionMaster\Views\ProcessView.xaml` | 两处 StringFormat 改 `{0:0.#} ms`；移除 `<1 ms` MultiDataTrigger |

未动：`PluginExecuteResult` 的 long 毫秒（配置窗试运行结果框显示，非步骤徽章链路）。

**后续清理（同日追加）**：`FlowModel.CurrentRunTimeMs`（流程级死代码，全库零读写，XAML 绑定经核实均指向 StepModel 同名属性）已删除；`ProcessViewModel` 中引用旧属性名 `LastRunStartTime` 的注释同步更正为 `LastRunStartTimestamp`。Core/VisionMaster 重编通过。

## 三、验证结果

- 编译：VM.Core / VM.FlowEngine / UI / VisionMaster 全部成功（Debug）；GetDiagnostics 无错误。
- 插件二进制兼容确认：全库 grep 无任何插件引用被改类型的三个属性，Modules 下插件无需重编。
- 人工待验（重启软件）：
  1. 循环运行 → 图像脚本徽章显示真实亚毫秒值（如 `0.4 ms`），不再是 `<1 ms` 或消失；
  2. 延时显示 `498.x ms` 一位小数；
  3. 运行中绿色实时计数正常跳动、完成后冻结；
  4. 配置窗"试运行"后步骤徽章数值正常。

## 四、已知边界

| 项 | 说明 |
|----|------|
| 语义仍是"算法线程耗时" | UI 线程的 CopyImage/渲染不计入；端到端出图延迟需另行打时间戳 |
| double 原子性依赖 x64 | 已写入字段注释；32 位宿主出现即触发红线（须 Interlocked/lock） |
| 调度抖动 | 显示一位小数，但 0.1ms 内的波动是 OS 调度噪声，不宜作为算子 A/B 对比依据；对比请用多次运行的均值（未来统计需求是选 double 不选 float 的主因） |
