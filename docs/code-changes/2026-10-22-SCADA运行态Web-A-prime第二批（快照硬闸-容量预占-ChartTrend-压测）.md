# SCADA 运行态 Web · A′ 第二批（快照硬闸 / 容量预占 / ChartTrend / 压测）

- 日期：2026-10-22
- 分支：`feature/scada-web-v4-a-prime`
- 评审：`docs/meetings/2026-10-22-SCADA-Web-v4-剩余项评审-R1.md` / `-R2.md`（R3 决策见性能报告 §10）
- 性能报告：`docs/performance/2026-10-22-SCADA-Web-A-prime-性能测试报告.md`

---

## 1. 本批改动

### 1.1 快照硬闸（`SnapshotMaxBytes` 从死配置变成真闸）

- `Web/VisionMaster.ScadaWeb/ScadaWebHost.cs`
  - 新增 `_effectiveSnapshotMaxBytes`（`min(配置, 3.5MB)`，默认 2MB）与公开
    `EffectiveSnapshotMaxBytes`；
  - 新增 `SendStructure(session, json)`：发送前按 UTF-8 `GetByteCount` 判；
    超限回 `error{E_SNAPSHOT_TOO_LARGE, bytes, limit}` 并按 **1009** 关闭，
    **不推进 `CurrentPageKey`**（避免 10Hz 重试风暴）；
  - `BuildSnapshotMessage`/`BuildPageMessage` 的三个调用点统一走 `SendStructure`。
- `Web/VisionMaster.ScadaWeb/ScadaWebCommand.cs`：新增 `E_SNAPSHOT_TOO_LARGE`。
- 意义：旧实现里 `SendTextAsync` 超限抛在发送循环被吞，连接会变成
  “在线但永远收不到结构”的僵尸，只能等 45 秒判死。

### 1.2 容量 TOCTOU 修复

- `ScadaWebHost` 新增 `_admittedSessions` 原子预占与 `AdmittedCount`；
  `HandleWebSocket` 用 `Interlocked.Increment > MaxSessions` 判定，
  替代“先数 `_sessions.Count` 再 `TryAdd`”的竞态；所有失败出口与 finally 幂等释放，
  `Detach` 清表时归零。
- 第 17 个活跃连接的**实际 WS 关闭码**从 1001 改为 **1013**（与 `bye.closeCode` 一致）；
  容量拒绝仍只回收“本次新签”的令牌，续期令牌保留。
- 未做：8 个握手预留槽（见 §4）。

### 1.3 幂等表容量修正

- `ScadaWebCommandRouter.ScadaWebCommandDedup.MaxEntries` 65536 → **262144**。
  旧值按 16 会话×5 条/秒只够 819 秒（13.6 分钟），表满后 `Complete` 静默不记，
  命令照执行但重放会被当新命令——R3 红队抓到的“幂等窗口静默缩短”缺陷。

### 1.4 值帧序列化优化 + 固定节拍

- `ScadaWebHost.SendFrame` 改为接收 `IReadOnlyList<WireFrameItem>`：
  item 每拍只 `JsonConvert.SerializeObject` 一次，16 个会话共享；旧写法 16×1000 = 1.6 万次/拍。
- `RunFrameLoopAsync` 改为绝对时刻排程（`Stopwatch`），吸收每拍工作耗时；
  旧写法 `Task.Delay(100)` 叠加工作耗时，实测只有 7.92Hz。
- 结果（S1-10% 同参数）：**7.922Hz → 10.077Hz**，delta p95 **20,154B → 10,154B**。

### 1.5 ChartTrend 渲染器（第 5 个复杂图元补齐）

- `wwwroot/elements-process.js` 新增 `registry['Hmi.ChartTrend']`：
  - 数据来自绑定的 `Value` 值帧，不新增服务端通道；
  - 4096 点环形缓冲；同值 100ms 内不重复追加（避免 keyframe 造出假点）；
  - Canvas 2D 绘制网格/量程/曲线/最后一点；`TimeWindow`/`Minimum`/`Maximum`/`FixedRange`/
    `CurveBrush`/`AxisBrush`/`Title` 按描述符默认值；
  - **不声明 `reset`**：keyframe 每 10 秒会调 `resetRecords`，声明了就会清空曲线。
- `ScadaChecks/WebRendererCoverageChecks.cs` 例外清单 5 → **4**（移除 ChartTrend）；
  `[WR3]` 上限 5 → 4。
- 已知偏差：横轴是“值帧到达时刻”而非变量真实采样时刻；刷新/重连后缓冲从空开始；
  不支持桌面上的拖动/缩放。

### 1.6 压测夹具重写

- `Web/ScadaWebStress/Program.cs`：
  - 参数：`--clients/--elements/--variables/--boundElements/--changePct/--changeHz/
    --holdMs/--commands/--maxSessions/--slowClientMs/--capacityProbe/--abnormalProbe/--outDir`；
  - **值真的会变**（旧夹具值恒定，A′ 的增量路径从未执行）；
  - `MaxSessions` 用生产值（旧夹具 `Math.Max(64, clients+8)` 把预算夹成 11.6KB）；
  - 字节按收到的 UTF-8 原始负载统计（旧夹具用字符数）；
  - 单收循环 + pending ack 表（旧夹具 `ReceiveUntil` 会丢帧）；
  - 输出 `meta.json` / `raw.jsonl` / `summary.json`；
  - 容量探针（16 活跃 + 第 17 端）与异常探针（坏 JSON/未知 type/66KB 入站/错误后 ping）。
- 未做：两进程、重连风暴、停服中压测、浏览器（见性能报告 §7）。

## 2. 验证

| 项 | 结果 |
| --- | --- |
| `dotnet build VisionMaster.sln -c Debug` | 0 错误 |
| `Web\ScadaWebChecks` | **119 PASS / 0 FAIL** |
| `ScadaChecks` | **2438 PASS / 0 FAIL**（WR 例外 4 项） |
| `node --check elements-process.js` | 0 |
| S1-10%（16×3000×1000×10Hz×60s） | 均值 21.426Mbps / 峰值 40.632 / delta p95 10,154B / 10.077Hz / 首屏 p95 1,199ms / 17 端 1013 / 0 错误 → **pass** |
| S1-100%（全变） | 均值 68.09Mbps / 峰值 79.156 / delta p95 46,454B / 10.042Hz / 首屏 p95 1,031ms / 0 错误 → **pass**；单变量刷新约 4.7Hz |
| S3-慢消费者（1 端停读 30s）+ 异常探针 | 均值 22.807Mbps / 峰值 56.432 / 10.512Hz / 0 错误；坏 JSON/未知 type/66KB/错误后 ping 全通过 → **pass** |

## 3. 性能结论

- 16×3000×1000×10Hz 在 10% 与 100% 变化率下均满足 70Mbps 均值 / 100Mbps 峰值；
- 增量帧遵守 46,484B/帧/会话预算；100% 全变时靠“预算裁剪 + 游标轮转”把单变量刷新降到约 4.7Hz，
  **不丢值、不崩、不超预算**；
- 首屏快照平均 0.93–0.99MB，p95 1.0–1.2s；17 端容量拒绝正确；异常入站不断连；
- 边界：回环、同进程、协议层；不能外推为“现场百兆够用”或“16 个浏览器流畅”。

## 4. 未完成（下一批，不冒充已完成）

1. `cmd.result`（WS 查询 ack 丢失后的结果）：设计已冻结，未实现。
2. 快照分片 + resync + 控制通道：未实现；用 4MB WS 上限 + 2MB 快照硬闸替代。
3. 24 槽的 8 个握手预留槽：未实现；只做了 16 活跃名额的原子预占。
4. ImageView `/img` + imageToken：未实现；`ScadaWebValueResolver` 尚未跳过图像绑定
   （每 10Hz 全尺寸 HImage→BitmapSource 的隐藏开销仍在，下一批必须先修）。
5. DataGrid 行数据、AxisPanel/AxisConsole 实时轴数据：未实现；ChartTrend 已用 Value 帧完成。
6. 默认值一致性闸：未实现；23 个现有渲染器仍硬编码回退值。
7. 浏览器级验证、真实网络、重连风暴、停服中压测、长稳、三端一致：未覆盖。
