# CSV记录 · 开发架构

> 分类：数据处理 ｜ 来源：插件 Plugin.DataRecord ｜ 读者：开发

本页讲实现结构、执行时序与宿主契约。界面口径与参数含义在用户册（01/03/04/05），此处不重复。

## 一、文件职责

| 文件 | 职责 | 改动须知 |
| --- | --- | --- |
| `DataRecordPlugin.cs` | 配置载体（13 个 `[StepConfig]`）、动态端口重建、三遍式 `RunAlgorithm`、图片判定与命名、`ImageSaveMode` 枚举定义 | 属性名就是 `.vms` 存盘键，改名等于老方案失配 |
| `RecordingHub.cs` | 进程级静态中枢：两条有界队列 + 两条后台线程、序列号边车、磁盘水位、旁路文件、日志暂存 | 所有「CSV记录」实例共享同一份，改这里等于改全局落盘时序 |
| `RecordColumnDef.cs` | 列模型与 `ColumnSource` / `BuiltInField` 两个枚举 | 两个枚举都**没有** `[Display]`，中文由视图侧字典提供 |
| `DataRecordView.xaml` / `.xaml.cs` | 四卡片面板、行内连线编辑器、三个转换器 | 面板怎么改都不能动端口名与存盘键 |

## 二、`RunAlgorithm` 执行时间线（源码顺序）

| 序 | 动作 | 关键判据与坑 |
| --- | --- | --- |
| 1 | 挂即时日志 sink + 补投暂存日志 | 静态类拿不到 `IExecutionContext`，所以把 `RecordingHub.LogSink` 指向本次的 `ILogService`（只捕获 logger 引用，不捕获 context）；后台写线程的 WARN/ERROR 当场进日志，sink 未生效时暂存队列由 `TakeLogs()` 兜底 |
| 2 | `EnabledColumns()` | 过滤 `Enabled` 且列名非空白。空集 → `Fail` 文案 `CSV记录：未配置任何启用的列` 并直接返回 |
| 3 | `BuildTarget(cols, now)` | **每次执行新建** target 对象；Hub 按引用分组，所以改配置自然走新目标，不需要通知 Hub 也不需要缓存失效 |
| 4 | `needSeq` 判定 | 仅当存在内置序号列，或图片模板含 `{序号}`/`{Sequence}` 时才 `NextSequence()`；否则 `seq = 0`，不消耗全局号 |
| 5 | 第一遍：逐列取值 + 格式化 | `GetRawValue` → `FormatCell`；`ImagePath` 列先置空串占位并记录 `imagePathIndex`；同时攒 `colValues`（列名 → 已格式化文本）供图片命名用 |
| 6 | 第二遍：图片处置 | 端口值 `string` → 只抄路径；`HImage` 且 `ShouldSaveImage` → `HOperatorSet.CopyImage` 出独立副本（`new HImage(copied)`，`copied.Dispose()` 放在 `finally`） |
| 7 | 回填 ImagePath 列 | 只有真存图或上游给路径时非空；`colValues` 里也要同步，否则后续列引用它会取到空 |
| 8 | 第三遍：`Enqueue` | `TryAdd(..., 0)` 非阻塞。返回非 `Accepted` 且 `BlockOnFailure` → **先释放未入队的图片副本**再写 `ErrorMessage` 返回 |
| 9 | `EnqueueImage` | 账本已入队，图失败只 WARN，绝不回改账本行 |
| 10 | 异步写盘阻断 | `BlockOnFailure` 时 `TryFlush(max(2000, 2×批窗))`：队列排不空，或本次步骤期间 `WriteErrorSeq` 变化 → `Fail("CSV记录：数据写入失败：…")`（错误含时间与目标文件） |
| 11 | 收尾 | `Success.Value = true` 且清空 `ErrorMessage`；基类 `Execute()` 进门已预置过这两项 |

## 三、动态端口重建链路（最容易踩的一段）

- **触发点**：`Columns` setter、集合增删、列的 `Name` / `Source` / `Enabled` 属性变化、`ImageSaveMode` setter。
- **顺序纪律**：`ApplyConfigValues` 覆写成"先 `base.ApplyConfigValues(stepData)` 再 `RebuildDynamicInputs()`"。
  基类会把 `[StepConfig]`（含 `Columns`）灌进来，端口重建必须在它之后；同时整个 `ApplyConfigValues` 必须早于编译期 `LinkPorts`，否则连线找不到端口。
- **端口形态**：`new InputPort<object>(col.Name, null, col.Name) { IsRequired = false }`。
  `IsRequired = false` 是硬要求（列允许"先定义后连线"，否则 FlowCompiler 编译期报 `[参数缺失]`）；
  类型用 `object` 是因为 FlowCompiler 连线不做类型静态校验，赋值经 `ValueConverter` 兜底。
- **列名不做 Trim**（与「结果上报」的字段表不同，那边会 Trim 并去重）：`col.Name` 原样进端口名与表头。
- **重名列**：`_dynamicInputNames.Contains(col.Name)` 去重，只建一个端口，但表头仍会重名。
- **`图片` 端口**：固定名由 `public const string ImagePortName = "图片"` 提供，描述文本区分 `HImage` 与 `string` 两种语义；const 不能当绑定路径，所以视图另开了 `ImagePortNameValue` 实例属性。
- **绑定锚点**：`PortsVersion` 自增 + `OnPropertyChanged(nameof(PortsVersion))`，让行内连线 `MultiBinding`（Name + DataContext + PortsVersion，走 `InputPortConverter`）重新取到新端口对象。去掉这个计数器不会报错，只会"连不上/连了旧的"。

## 四、状态真源

| 状态 | 唯一真源 | 说明 |
| --- | --- | --- |
| 账本内容 | 磁盘 CSV 文件 | 内存不留行，落盘即结束 |
| 全局序号 | `RecordingHub._sequence`（`Interlocked`）+ 边车 `DataRecord_seq.json` | 边车每前进 50 号落一次；退出与关停强刷 |
| 端口集合 | `Columns` + `ImageSaveMode` | 由 `RebuildDynamicInputs` 单向派生，别试图在端口上反向改配置 |
| 中枢日志 | `RecordingHub.LogSink`（即时）+ `Logs` 队列（兜底，上限 2000，溢出丢最旧） | sink 由插件每次执行时替换；已即时投递的条目（`LogItem.Delivered`）不会再被 `TakeLogs` 重复转交 |
| 写盘失败 | `LastWriteError` + `WriteErrorSeq` | 每次写盘失败：seq 自增，`LastWriteError` 记时间/目标/原因；步骤用"开始快照 vs flush 后"比较 |
| 清理进度 | `ProcessBatch` 里的 `cleanupDone`（target → 已清理日期） | 进程内状态，重启后当天会再扫一次 |

## 五、白拿的宿主行为（不要在本插件里重做）

| 宿主行为 | 来源 | 本插件的依赖点 |
| --- | --- | --- |
| 未捕获异常转成 `Fail("类型名: 消息")` | `VisionPluginBase.Execute` | 列空与入队被拒之外的异常不需要自己兜 |
| `Success` / `ErrorMessage` 输出端口 | 基类 | 下游分支与"显示"类算子直接消费 |
| `InstanceName` 编译期注入（`流程名.步骤名`，面板实例上为"未赋值"） | 基类 + FlowCompiler | `{StepName}` 与内置「步骤名」列都读它 |
| 同名动态端口不覆盖 | `AddDynamicInput` | 重名列去重的实际执行者 |
| `[ParallelSafe]` 由 FlowCompiler 按FullName 字符串匹配 | 门禁 P31 | 改完源码**必须重建 DLL**，陈旧产物会让宿主门禁把已标注算子误报不安全 |

## 六、线程与内存所有权

- **流程线程**：只组字符串数组 + 入队，微秒级，绝不碰磁盘。
- **`DataRecord.CsvWriter`**：`TryTake(500ms)` 攒批窗口（节拍快时合并、空闲时不空转），单批上限 5000 行；
  两级分组"target → 展开日期后的文件路径 → 行"，所以跨天批次也能各归各文件；每 20 批 `PersistSeq()` 一次。
- **`DataRecord.ImageLayouter`**：逐张 `WriteImage`，`finally` 里 `Dispose` 副本。
- **句柄纪律**：每批"开→写→关"，`FileMode.Append` + `FileShare.ReadWrite`，句柄只持有几毫秒，最大限度给 Excel 让路。
- **IOException 处置**：退避 `120 * attempt`（120/240/360 毫秒）共 3 次 → 旁路 `_conflictN.csv`（N 试到 99，都占则用不带序号的 `_conflict.csv`）。
- **flush 契约**：`TryFlush(timeout, out error)` 等两条队列排空 + `_busy` 归零（空/闲连看两次、间隔 20ms，避开"刚取走最后一条但还没置 `_busy`"的窗口）；`FlushForTests` 只是它的旧名转发。Excel 报表经 `Core.Interfaces.DataRecordFlushBridge` 调它。
- **关停**：`AppDomain.CurrentDomain.ProcessExit` → `Shutdown()`：置 `_stopping`，轮询到队列空或 3 秒预算耗尽，最后 `PersistSeq(true)`。
  `DataLoop` 见到 `_stopping` 且队列空才退出循环，退出前再把残留冲一遍。
- **图片副本三态**：`EnqueueImage` 成功 → 归 Hub；入队被拒 → Hub 立即 `Dispose`；行入队失败且阻断 → 插件自己 `Dispose`。任何一条路径漏掉就是内存泄漏或双重释放。
- **`NextSequence()` 里调 `EnsureStarted()`**：取过号就必须挂上退出冲刷钩子，否则"只取号未入队"的进程退出时边车不落盘，重启序号回绕导致图片撞名。

## 七、并行安全口径

标注 `[ParallelSafe]` 的依据：入队是 `BlockingCollection` 线程安全操作，后台**只有一条** CSV 写线程串行落盘，
所以同一文件的行不会交织成半行。多个实例并发时各自持有不同 `RecordingTarget`（默认目录模板含 `{StepName}`，天然分开）；
如果用户把两个实例的目录与文件名模板配成同一个绝对路径，那仍是同一 target 分组之外的两个对象——
写线程按"批内分组"处理，不会撕裂，但**逻辑上是两本账混写**，这一点没有闸门，属于配置事故。

## 八、改完必须同步的地方

| 改了什么 | 必须同步 |
| --- | --- |
| 新增或改名 `[StepConfig]` | 用户册 03 的参数表 + `.vms` 兼容说明 |
| 新增内置字段 | `BuiltInField` 枚举 + 视图 `CnEnumLabelConverter` 字典 + 用户册 04 内置字段口径 |
| 改日志/界面文案 | 用户册 05 的原文表（逐字改，日志检索靠它） |
| 改队列/重试/水位/批量常量 | 用户册 04 容量表 + 本页第四、六节 |
| 改 flush / 写盘失败口径 | 用户册 03/04/05 + 本页第二、四、六节 |
| 改 `FormatCell` 分支 | 用户册 04 第三节格式化表 |
| 改 Help 目录任何一页 | 闸门 HM 系列，见 `11-dev-invariants.md` |
