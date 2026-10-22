# SCADA 运行态 Web · A′ 性能测试报告（2026-10-22）

- 分支：`feature/scada-web-v4-a-prime`
- 被测产物：`Web\VisionMaster.ScadaWeb`（A′ 第一批 + 快照硬闸 + 容量预占 + ChartTrend 渲染器）
- 测试夹具：`Web\ScadaWebStress`（2026-10-22 重写）
- 证据目录：`docs/performance/evidence/S1-10pct-final/`、`S1-100pct/`、`S3-slow/`
- 结论：**协议层通过；浏览器级冒烟通过；重连风暴 80/80、120 秒长稳通过**；
  真实网络/分片/16 端长稳未覆盖，见 §7、§11-§13。

---

## 1. 测试目标

回答四个问题：

1. 16 个并发 Web 客户端、单页 3000 图元、1000 个绑定变量、10Hz 值帧，服务端出站带宽是否在 **70Mbps 均值 / 100Mbps 峰值**以内？
2. 单帧增量值帧是否遵守 **46,484B/帧/会话**的预算（70Mbps×0.85/16/10 推导值）？
3. 首屏快照、帧率、ack 延迟、容量拒绝、慢消费者与异常入站是否可用？
4. 100% 变量每拍全变时，系统是“丢值/崩”还是“降刷新率但保持可用”？

## 2. 环境与拓扑

| 项 | 值 |
| --- | --- |
| 机器 | `ADMIN`，16 逻辑处理器 |
| OS | Windows 10.0.26200 |
| .NET | 9.0.20 |
| 构建 | Debug（夹具与被测 DLL 同批重建；**未使用 `--no-build`**） |
| 拓扑 | 同进程：真实 `ScadaWebHost` + N 条真实 `ClientWebSocket`，绑定 `127.0.0.1` 随机端口 |
| 网络 | 回环（**不能代表现场百兆/千兆、交换机、WiFi、丢包与重排**） |
| 浏览器 | 补充冒烟：IAB 浏览器登录真实宿主、渲染 61 图元、值帧刷新、按钮命令（见 §11）；**不测真实网络与长稳** |
| 值源 | `FakeValueSource`：1000 个 double 变量，绑定到前 1000 个图元（`Hmi.ProgressBar.Value`） |
| 页面 | 3000 图元（1000 绑定 + 2000 静态 `Hmi.Text`）+ 1 个带 `Pressed→Log` 钩子的命令按钮 |
| 配置 | `MaxSessions=16`、`AllowCommands=true`、`ClickPolicy=AllOperators`、`RequireConfirmForDangerous=false`、取票/命令/图元限速临时放宽到 10000（压测专用） |

## 3. 测试方案

### 3.1 场景

| 场景 | 参数 | 目的 |
| --- | --- | --- |
| **S1-10%** | 16 客户端 / 3000 图元 / 1000 变量 / 10% 变量每 100ms 变化 / 60s / 每端 5 条命令 / 17 端容量探针 | 常态负载：10% 动态量，验证带宽、10Hz、首屏、容量 |
| **S1-100%** | 同上，但 **100% 变量每 100ms 变化** | 最坏负载：预算裁剪、轮转、峰值带宽 |
| **S3-慢消费者** | 16 客户端 / 同上 / 其中 1 端读完首屏后 **停读 30s** / 40s / 异常探针 | 慢消费者、队列积压、异常入站 |
| **边界/异常** | 17 端连接；坏 JSON；未知 type；66KB 入站帧；错误后 ping | 容量拒绝与入站闸 |

### 3.2 指标与判据

| 指标 | 判据 |
| --- | --- |
| 连接/ack | 16/16 连接；80/80 ack（16×5） |
| 帧率 | `frames / 时长 / 客户端 ≥ 9.5Hz` |
| 增量帧字节 | delta `p95 ≤ EffectiveFrameBudgetBytes + 8`（46,492B） |
| 聚合带宽 | 1s 滑窗均值 ≤70Mbps；1s 峰值 ≤100Mbps |
| 首屏 | snapshot 到达 `p95 ≤ 3s` |
| 容量 | 第 17 个活跃连接收到 `bye{at-capacity,closeCode:1013}` |
| 异常 | 坏 JSON→`E_BAD_JSON`、未知 type→`E_UNSUPPORTED`、>64KB→`E_BAD_REQUEST`，且连接仍存活 |
| 错误 | `errors=0`（不含预期的容量拒绝） |

统计口径：

- 字节：**收到的 UTF-8 原始负载长度**（`MemoryStream.Length`），不是 `JObject.ToString().Length`；
- 分位数：nearest-rank（`ceil(p*n)-1`）；
- 帧率：`frames / holdMs / clients`（hold 窗口内）；
- 带宽：所有客户端所有消息的字节按 1 秒桶累加，均值 = 总字节×8/时长，峰值 = 最大 1 秒桶；
- delta/keyframe 分桶统计；keyframe 不裁预算（每 10 秒一次），单列。

## 4. 执行记录

### 4.1 命令

```bat
:: S1-10%
dotnet run --project Web\ScadaWebStress\ScadaWebStress.csproj -c Debug -- ^
  --clients 16 --elements 3000 --variables 1000 --boundElements 1000 ^
  --changePct 10 --changeHz 10 --holdMs 60000 --commands 5 --maxSessions 16 ^
  --capacityProbe --outDir docs\performance\evidence\S1-10pct-final

:: S1-100%
dotnet run --project Web\ScadaWebStress\ScadaWebStress.csproj -c Debug -- ^
  --clients 16 --elements 3000 --variables 1000 --boundElements 1000 ^
  --changePct 100 --changeHz 10 --holdMs 60000 --commands 5 --maxSessions 16 ^
  --capacityProbe --outDir docs\performance\evidence\S1-100pct

:: S3-慢消费者 + 异常
dotnet run --project Web\ScadaWebStress\ScadaWebStress.csproj -c Debug -- ^
  --clients 16 --elements 3000 --variables 1000 --boundElements 1000 ^
  --changePct 10 --changeHz 10 --holdMs 40000 --commands 5 --maxSessions 16 ^
  --slowClientMs 30000 --abnormalProbe --outDir docs\performance\evidence\S3-slow
```

### 4.2 时间与产物

| 场景 | 开始（UTC） | 证据 |
| --- | --- | --- |
| S1-10% | 2026-10-21T18:17:53Z | `S1-10pct-final/meta.json`、`raw.jsonl`、`summary.json` |
| S1-100% | 2026-10-21T18:20Z 左右 | `S1-100pct/` 同三件 |
| S3-慢消费者 | 2026-10-21T18:22Z 左右 | `S3-slow/` 同三件 |

每个目录含：

- `meta.json`：命令行、机器、OS、.NET、`EffectiveFrameBudgetBytes`、`EffectiveSnapshotMaxBytes`、`MaxSessions`、模式；
- `raw.jsonl`：逐条消息（client / tMs / type / bytes / seq / frameSeq / keyframe / items / code）；
- `summary.json`：聚合指标 + `checks` 逐项 pass/fail + `errorSamples`。

## 5. 结果

### 5.1 S1-10%（常态：10% 变量每拍变化）

| 指标 | 结果 |
| --- | --- |
| 连接 / ack | 16/16；80/80 |
| 帧 | 9,674（delta 9,565，keyframe 109） |
| 帧率 | **10.077Hz** |
| 增量帧字节 | p50 10,154 / **p95 10,154** / p99 20,152 / max 30,153（预算 46,484） |
| keyframe 字节 | p50 100,081 / p95 100,153 / max 100,153 |
| 聚合带宽 | **均值 21.426Mbps** / 峰值 40.632Mbps |
| 首屏 | p50 975ms / **p95 1,199ms** / max 1,199ms |
| 快照 | 16 份合计 15,829,893B，平均 **989,368B/端**（硬上限 2,097,152B） |
| 容量 | 第 17 端：`bye{at-capacity,closeCode:1013}`，`rejected=true` |
| 错误 | 0 |
| 判定 | **pass** |

### 5.2 S1-100%（最坏：1000 变量每拍全变）

| 指标 | 结果 |
| --- | --- |
| 连接 / ack | 16/16；80/80 |
| 帧 | 9,640（delta 9,544，keyframe 96） |
| 帧率 | **10.042Hz** |
| 增量帧字节 | p50 46,452 / **p95 46,454** / p99 46,479 / max 46,486（预算 46,484，允许 +8） |
| keyframe 字节 | p50 99,933 / p95 99,933 / max 99,933 |
| 聚合带宽 | **均值 68.09Mbps** / 峰值 79.156Mbps |
| 首屏 | p50 847ms / **p95 1,031ms** / max 1,031ms |
| 快照 | 16 份合计 14,950,454B，平均 **934,403B/端** |
| 容量 | 第 17 端 1013 拒绝 |
| 错误 | 0 |
| 判定 | **pass** |

**关键解释（必须和数字一起看）**：

- 100% 全变时，帧率仍是 10Hz，但**每条变量的刷新率不是 10Hz**：
  9,544 个 delta 帧共 4,505,129 个 item，平均 **472 item/帧**；1000 个变量每拍全变，
  46,484B 预算约装 460 条，因此每条变量约 **每 2.1 帧刷新一次 ≈ 4.7Hz**。
- 这就是“预算裁剪 + 游标轮转”的预期行为：**不丢值、不崩、不超预算，但单变量刷新降频**。
- 若现场要求“1000 个变量每个都 10Hz”，当前 JSON 形状（每条约 100B）需要
  1000×100B×10×16 ≈ **128Mbps**，70Mbps 目标不可能同时成立；需要索引化/二进制编码、
  降变量数、降帧率或提高带宽（见 §7）。

### 5.3 S3-慢消费者 + 异常

| 指标 | 结果 |
| --- | --- |
| 连接 / ack | 16/16；80/80 |
| 帧 | 6,728（delta 6,646，keyframe 82） |
| 帧率 | 10.512Hz |
| 增量帧字节 | p95 10,154 / max 30,098 |
| 聚合带宽 | 均值 22.807Mbps / 峰值 56.432Mbps |
| 首屏 | p95 1,166ms |
| 错误 | 0（慢端未触发断连） |
| 异常探针 | `badJson=true`、`unknownType=true`、`oversized=true`、`aliveAfterErrors=true` → **pass** |
| 判定 | **pass** |

**慢消费者的边界**：1 个客户端读完首屏后停读 30 秒。服务端出站队列（256 条）在停读期间
会满，值帧按 `TrySend=false` 丢弃；客户端恢复后继续收新帧与下一个 10s keyframe，未断连。
本次夹具**没有**单独统计该端的 `DroppedCount`/恢复收敛帧数（见 §7 未覆盖），所以只能下
“没有把连接拖死、没有把其他端带崩”的结论，不能下“丢了多少帧”的精确结论。

### 5.4 优化前后对照（同一 S1-10% 参数）

| 版本 | 帧率 | 增量 p95 | 说明 |
| --- | --- | --- | --- |
| 优化前 | **7.922Hz** | 20,154B | 每会话重复序列化 1000 项（16×1000/拍）；`Task.Delay(100)` 叠加工作耗时 |
| 优化后 | **10.077Hz** | 10,154B | item 每拍只序列化一次、跨会话共享；帧循环改绝对时刻排程 |

两项改动都在 `ScadaWebHost`：`BuildWireItems`（每拍一次）+ 固定节拍 `RunFrameLoopAsync`。
这是本批唯一一处“先量出问题再改”的性能修复，前后都有 S1 证据。

## 6. 结论

1. **协议层达标**：16 客户端 × 3000 图元 × 1000 绑定 × 10Hz，在 10% 与 100% 变化率下
   均值 21.4 / 68.1Mbps、峰值 40.6 / 79.2Mbps，均低于 70/100Mbps；
   增量帧 p95 10.2KB / 46.45KB，均在 46,484B 预算内；帧率 10.08 / 10.04Hz；
   首屏 p95 1.2s / 1.0s；17 端容量拒绝正确；异常入站不拖死连接。
2. **100% 全变的代价是单变量降频，不是丢值/崩溃**：约 4.7Hz/变量；若现场要求每个变量 10Hz，
   当前协议形状在 70Mbps 内不可达，必须改编码或降规模。
3. **优化有效**：共享序列化 + 固定节拍把 7.92Hz 提到 10.08Hz，且增量 p95 从 20KB 降到 10KB。
4. **结论边界**：以上全部是**回环、同进程、协议层**数字；不能外推为“现场百兆网络够用”
   或“16 个浏览器流畅”。浏览器 DOM/布局/绘制、真实交换机/WiFi、1.5MB 单消息 `JSON.parse`、
   重连风暴、停服中压测、分片、长稳、三端一致均未覆盖。§11 的浏览器冒烟只证明
   “客户端能登录、能渲染、值帧会动、按钮能发命令”，不证明高负载下的端到端流畅度。

## 7. 未覆盖与风险

| 未覆盖项 | 影响哪个结论 | 说明 |
| --- | --- | --- |
| 浏览器 DOM/布局/绘制 | 端到端流畅度、首屏体感 | §11 做了回环冒烟（登录/渲染/值帧/按钮），但**不覆盖** 16 端 × 3000 图元下的绘制与主线程阻塞 |
| 1.5MB 单消息 `JSON.parse` | 首屏主线程阻塞 | 快照平均 0.93–0.99MB，最坏接近 2MB；浏览器冒烟页只有 61 图元，未测大快照解析 |
| 真实网卡/交换机/WiFi/丢包重排 | 现场带宽与延迟 | 回环无速率限制、无 RTT/MTU/重传 |
| 重连风暴 / 停服中压测 | 恢复时间、会话回收 | 重连风暴**已覆盖**（`S4-reconnect-storm`：10 轮 × 8 端 = 80/80 成功，459ms）；停服中压测未覆盖 |
| 24 握手槽 | 并发握手容量 | **已实现**（第五批 `ScadaWebAdmissionGate`）；`B55/B55b/B55c/B56/B56b` 覆盖槽满、TTL、幂等释放、第 17 端 1013 |
| 分片 | 超大页面 | 未实现；用 4MB WS 上限 + 2MB 快照硬闸替代 |
| 慢消费者精确丢帧/恢复 | “慢端丢多少、多久收敛” | 只观察到不断连；未逐端统计 Dropped/收敛帧数 |
| 长稳 >60s | 内存/句柄/GC 长期行为 | **已补 120 秒**（`S5-long120s`：8 端 × 1000 图元，fps 10.02、errors 0、pass）；仍未做 16 端 × 3000 图元的 120 秒 |
| DataGrid 行 / Axis 实时数据 | 已覆盖 | B52/B53 协议级：`axis` 5Hz、`grid` 2Hz；浏览器视觉/大表格渲染未测 |
| 默认值一致性视觉 | 两端外观一致 | **已实现归一化 + `[WR5]` 读键闸 + `B57`**；仍缺“WPF 截图 vs Web 截图”的逐图元视觉对照 |
| 三端一致 / TLS / 反代 / 审计磁盘 I/O | 部署验收 | 未覆盖 |

## 8. 复现

1. 先构建（**不要 `--no-build`**；R65）：
   ```bat
   dotnet build VisionMaster.sln -c Debug
   dotnet build Web\ScadaWebStress\ScadaWebStress.csproj -c Debug
   ```
2. 按 §4.1 的命令运行；每次换一个 `--outDir`（不要覆盖旧证据）。
3. 从 `raw.jsonl` 复算：按 `tMs/1000` 分桶算 1s 字节、按 `type=frame` 分 delta/keyframe、
   用 nearest-rank 算 p50/p95/p99；结果应与 `summary.json` 逐字段一致（整数精确，浮点 1e-9）。
4. 对照 `meta.json` 的 `effectiveFrameBudgetBytes`（应为 46,484）与
   `effectiveSnapshotMaxBytes`（应为 2,097,152）。

## 9. 证据清单

```
docs/performance/evidence/
├─ S1-10pct-final/   meta.json  raw.jsonl  summary.json   （pass=true）
├─ S1-100pct/        meta.json  raw.jsonl  summary.json   （pass=true）
└─ S3-slow/          meta.json  raw.jsonl  summary.json   （pass=true；含 abnormalProbe）
```

## 10. 附：本批未完成的优化项（不冒充已完成）

- `cmd.result`（WS 查询 ack 丢失后的结果）：**已实现**（`docs\code-changes\2026-10-22-SCADA运行态Web-cmd.result（ack丢失后的结果查询）.md`；
  `M2-13h` + `B54/B54b`）；仍不做跨刷新/跨重连的在飞命令持久化。
- 快照分片/resync/控制通道：未实现；用 4MB WS 上限 + 2MB 快照硬闸替代（作为不可拆单位延后，见 R3 §三）。
- 24 握手槽：**已实现**（第五批 `ScadaWebAdmissionGate`；`B55/B55b/B55c/B56/B56b`）。
- ImageView `/img` + token：**已实现**（第三批；`B51/B51b` 验 PNG 字节与坏 token 401）；
  仍缺浏览器 `<img>` 视觉验收、按显示尺寸降采样/JPEG、`ETag/304` 与 0.5Mbps 令牌桶。
- DataGrid 行数据 / Axis 实时数据：**已实现**（第四批；`B52/B53` 协议级）；
  仍缺浏览器视觉验收、大表格 DOM 成本与轴点位数上限的现场压测。
- 默认值一致性闸：**已实现**（第五批；catalog 归一化 + `[WR5]` 读键闸 + `B57`）；
  仍缺 WPF/Web 逐图元截图对照。

## 11. 浏览器级冒烟（2026-10-22 补充）

目的：证明“协议层通过”不是客户端根本没跑起来。详细记录见
`docs\performance\2026-10-22-SCADA-Web-A-prime-浏览器验证记录.md`。

- 宿主：`ScadaWebStress --serve`（真实 `ScadaWebHost`，`127.0.0.1` 随机端口，
  60 图元 / 12 变量 / 100% 变化 / 5Hz，`AllowCommands=true`）。
- 浏览器：ZCode IAB（Chromium），登录 `stress/pw`。
- 结果：
  - 登录卡片 → 运行画面（标题「压测页」、在线 1、已连接、可操作）；
  - 61 个 `.scada-el`、`0` 个 `.scada-placeholder`、29 个渲染器注册；
  - 值帧刷新：1.2s 内前 6 个绑定值从 `580..585` → `802..807`，`frameSeq` 296→358；
  - 静态资源 `/`、`/app.js`、`/elements-process.js`、`/elements-interactive.js`、`/api/catalog` 全部 200；
  - 点击「按钮」发出 `cmd.element` `Pressed` + `Released`（带 pageId/pageVersion/runGeneration），
    无 toast、无 `warned` 记录、WS 保持 open；
  - 页面背景切白并 `rerender()` 后 48 个 `Hmi.Text` 与 12 个数值条全部可见。
- 已知观察（不是本批回归）：默认 `Hmi.Text.Foreground=#FF202020` 与默认页面底色
  `#FF1E1E1E` 对比度极低（`ScadaPage.cs:25` / `PropertyPacks.cs:191`），
  默认配色的文本在深色页面上几乎不可见；压测夹具未设置页面底色，故首次截图只见数值条。
  是否调整默认配色属于 UI 设计决策，本批不改。
- 未覆盖：16 端 × 3000 图元下的浏览器绘制/主线程阻塞、真实网卡、长稳。

## 12. 重连风暴（2026-10-22 补充）

- 命令：`ScadaWebStress --reconnectStorm 10 --clients 8 --elements 40 --variables 8
  --changePct 10 --changeHz 5 --holdMs 1000 --maxSessions 8`
- 每轮 8 个客户端各自"取票 → 握手 → 收 hello/snapshot → 正常关闭"，连续 10 轮。
- 结果：**attempts=80，ok=80，failed=0，elapsedMs=459，pass=true**；
  证据 `docs/performance/evidence/S4-reconnect-storm/reconnect-storm.json`。
- 意义：24 槽准入在持续 churn 下没有漏名额（漏一个，后续轮次会出现 401/1013）。
  本项**不覆盖**停服中重连（host 不停止）。

## 13. 120 秒长稳（2026-10-22 补充）

- 命令：`ScadaWebStress --clients 8 --elements 1000 --variables 500 --boundElements 500
  --changePct 10 --changeHz 10 --holdMs 120000 --commands 0 --maxSessions 16`
- 结果（`docs/performance/evidence/S5-long120s/summary.json`，`pass=true`）：

| 指标 | 值 |
| --- | --- |
| 连接 / 快照 | 8 / 8 |
| 帧数（delta / keyframe） | 9,619（9,523 / 96） |
| 帧率 | **10.02Hz** |
| errors | **0** |
| 均值 / 1s 峰值 | 4.735 / 12.327Mbps |
| delta p95 | **5,155B**（预算 46,484B） |
| 首屏 p95 | 406ms |
| 总字节 / 快照字节 | 71,027,864 / 3,428,680 |

- 意义：120 秒内帧率、预算、错误数都稳定，没有随时间劣化；仍未测 16 端 × 3000 图元
  的 120 秒，也未记录进程私有内存/句柄曲线（夹具没有采）。
