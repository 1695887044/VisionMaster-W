# 删除变量级 PollIntervalMs（静默失效字段清理）

日期：2026-09-17
范围：Core 模型 / DTO / 持久化 / ViewModel / View

## 需求与决策

**起因**：用户提问"为什么添加变量时显示轮询(ms)，创建连接时也有轮询时间"。

**排查结论**：两个"轮询时间"只有连接级的 `ReadCycleMs` 真实生效（`worker.PollIntervalMs = config.ReadCycleMs`，驱动 Worker 线程按周期批量轮询该连接下所有变量）；变量级 `PollIntervalMs` 是旧架构（每变量独立定时器）的历史遗留，t5/t6 重构改为"每连接一个 Worker + 批量块读"后**引擎侧从未消费**——真正注册进通信引擎的 `CommunicationVariable` 类根本没有该字段。全工程 grep 确认其唯一流向是"存盘 → 读盘"的序列化往返。

**为什么留着有害**：UI 显示"轮询(ms)"输入框会误导用户以为"该变量按此周期刷新"，实际被连接周期覆盖——典型静默失效，现场排查困难。

**用户决策**：方案 B——连模型字段一起删（而非仅隐藏 UI）。

**为什么不做"变量级周期真生效"**：批量化与逐变量周期天然互斥。批量块读靠"地址相近的变量合成一次读"省往返；若每个变量各有周期，同一批变量会在不同时刻要求读/不读，段无法稳定复用，批量化的收益即被瓦解。

## 修改文件清单

| 文件 | 改动 |
| --- | --- |
| `Core/Models/Variable/IVariable.cs` | 删除接口成员 `int PollIntervalMs { get; }` |
| `Core/Models/Variable/NetworkVariableModel.cs` | 删除属性（默认 500） |
| `Core/Models/Variable/LocalVariableModel.cs` | 删除属性（默认 500） |
| `Core/Models/Variable/VariableDto.cs` | 删除 DTO 属性（默认 1000）及 `FromNetwork` 映射 |
| `Core/Models/Variable/VariableFactory.cs` | 删除 `CreateNetwork` 的 `pollIntervalMs` 参数及赋值 |
| `VisionMaster/Services/VariablePersistenceService.cs` | 删除恢复变量时的 `PollIntervalMs = dto.PollIntervalMs` 映射 |
| `VisionMaster/ViewModels/DialogViewModels/GlobalVariableManagerViewModel.cs` | 删除 `NewPollIntervalMs` 属性；`CreateNetwork` 调用去掉该实参 |
| `VisionMaster/Views/DialogViews/GlobalVariableView.xaml` | 删除"轮询(ms)"输入框；偏移量/位偏移 `UniformGrid` Columns 3→2 |

**兼容性**：旧 `.vms` 方案文件中残留的 `"PollIntervalMs": xxx` JSON 字段由 Newtonsoft 默认忽略（反序列化到已无此属性的 DTO 不报错），无需迁移。

**确认安全**：`CommTest` / `ScadaChecks` 等测试工程对 `CreateNetwork` 的全部调用均未传 `pollIntervalMs`（命名实参 grep 零匹配），删参数无破坏。

## 顺带修复（编译阻塞，非本次需求）

- `VisionMaster/Views/ScadaToolboxView.xaml` L21-24：`Grid` 上写了 `BorderBrush`/`BorderThickness`（Grid 无此属性）→ XAML 编译错误 MC3072。修法：外包一层 `Border` 承载右侧 1px 分隔线，内部 Grid 结构（RowDefinitions / Grid.Row 引用）原样保留。**该文件不在本次需求范围内**（可能是用户编辑 SCADA 界面时的产物），修复方式唯一且无功能影响。

## 验证结果

- 全量 MSBuild（VS2022，`/t:Build /m`）：**EXIT=0**，`error` 零匹配（输出仅历史遗留 nullable warning）。
- Grep 复查：`NewPollIntervalMs` / `nv.PollIntervalMs` / `dto.PollIntervalMs` / `pollIntervalMs` 全工程零匹配；保留的 `_pollIntervalMs` 仅剩 `ConnectionWorker`（连接级周期，合法持有者）。

## 已知边界

- 变量级"每变量独立刷新周期"能力自此彻底移除。若未来确有"关键变量快刷、其余慢刷"的需求，正确做法是**按周期分组建多个连接**（同 PLC 建两条不同 ReadCycleMs 的连接）——保持每连接单节奏，批量化不受影响。
- 网络变量创建后刷新节奏 = 所挂连接的 `ReadCycleMs`（连接配置界面可调，默认 1000ms）。
