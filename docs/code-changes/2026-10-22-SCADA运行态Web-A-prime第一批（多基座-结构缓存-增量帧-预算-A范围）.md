# SCADA 运行态 Web · A′ 第一批（多基座 / 结构缓存 / 增量帧 / 预算 / A 范围）

- 日期：2026-10-22
- 分支：`feature/scada-web-v4-a-prime`
- 评审记录：`docs/meetings/2026-10-22-SCADA-Web-v4-评审-R1.md` / `-R2.md` / `-R3.md`
- 结论：**第一批已落地并通过断言**；未做项在 §6 明确登记，不把“设计冻结”写成“已实现”。

---

## 1. 背景

R1/R2/R3 三轮评审（9 + 6 + 3 个角色）核对了交接文档与磁盘代码，确认：

1. **A 范围成立，但理由要改**：设备层有 `IMotionDevice.EmergencyStop()`
   （`MotionDeviceBase.cs:497-536`，停整卡、清队列、打断当前命令），缺的是 SCADA/Web 入口、
   停止完成确认、每轴位置有效性；不是“设备没有急停”。
2. **Web 运动/停止默认不可用**：`AllowMotionCommands` 默认 false（`AppConfigModel.cs:423`）；
   但 `StartFlow/RunFlowOnce/ResumeFlow` 能启动含运动步骤的流程，是旁路。
3. **数据面是 go/no-go**：当前每 100ms 在 UI 线程重建整页快照并全量发值帧；
   1000 绑定 ≈ 100KB/帧，16 端 ≈ 128Mbps，已越过 70/100Mbps。
4. **`SnapshotMaxBytes` / `FrameBudgetBytes` 是死配置**：全仓无消费点。
5. **多基座与弹窗有既有缺陷**：resolver 只解析第一只基座；`OpenPopup` 不动 `Page.Version`，
   弹窗结构永远推不出去。

## 2. 本批改动（按文件）

### 2.1 多基座

- `Web/VisionMaster.ScadaWeb/ScadaWebValueResolver.cs`
  - `Resolve` 优先遍历 `page.BasePages`；`BasePage` 仅作旧消息/测试的兼容回退。
  - 修复“第 2 只起的基座绑定永远不刷新”。
- `Web/VisionMaster.ScadaWeb/wwwroot/app.js`
  - `normalizePage` 读 `BasePages`（逐项递归一层），`BasePage` 回退；保留 `basePage` 别名。
  - `renderSnapshot` 按数组序把每只基座画进 `baseLayer`；`state.nodes` 页优先、
    基座从数组末尾（最上层）回填。

### 2.2 结构键与快照缓存

- `Web/VisionMaster.ScadaWeb/ScadaWebHost.cs`
  - 新增 `_structureKey/_structurePage` 与 `StructureBuildCount`。
  - `BuildStructureKey`：`runGeneration | 本页 Id/Version/标量哈希 | 有序基座 Id/Version | 弹窗及弹窗基座`。
  - `CaptureLiveState`：键命中复用原始快照（**不含 caps**），未命中才 `_builder.Build`；
    值仍然每拍解析。
  - `OnRunStarted` / `OnRunStopped` / `Detach` / 停运行分支清空缓存。
  - 修掉 `SendInitialStateAsync` “先推进页键再发送”的静默冻结风险。
- 结构消息成功后 `session.ResetDeltaForNewStructure()`，下一帧必为 keyframe。

### 2.3 增量值帧 + keyframe

- `Web/VisionMaster.ScadaWeb/ScadaWebSession.cs`
  - `LastValues`（`elementId + 属性键 → item JSON`）、`FrameSeq`、`NeedsKeyframe`、
    `LastKeyframeUtc`、`DeltaCursor`、`LastSendSucceededUtcTicks`。
  - `ResetPageKey` 清增量基线；`ResetDeltaForNewStructure` 保留页键、清基线。
  - 发送循环成功后 `MarkSendSucceeded()`。
- `Web/VisionMaster.ScadaWeb/ScadaWebHost.cs`
  - `SendFrame(state, session)`：按会话算增量；**baseline 只在 `TrySend` 成功后推进**；
    keyframe = 首帧 / 结构变更后 / 每 10 秒；无变化发空帧保 10Hz；
    预算裁剪用轮转游标，避免 1000 项全变时尾部饿死。
- `Web/VisionMaster.ScadaWeb/ScadaWebValueResolver.cs`
  - `ScadaFrameDto` 增加 `frameSeq` / `keyframe`。
- `wwwroot/app.js`
  - `applyFrame`：先判 `runGeneration`（旧轮帧不写 state）、再判 pageId/pageVersion；
    `keyframe:true` 时 `resetRecords()`，应用后 `refreshInteractionStates()`；
    记录 `frameSeq`；`createRecord` 暴露可选 `reset()`。

### 2.4 帧预算

- `ScadaWebHost` 构造时按 `70Mbps × 0.85 / MaxSessions / 10Hz` 推导上限（约 46KB），
  与 `settings.FrameBudgetBytes` 取较小值并夹在 8KB–256KB；`EffectiveFrameBudgetBytes` 可诊断。
- `SendFrame` 按 UTF-8 字节、整条 item 裁；超预算项留在未送达状态，下一拍继续。
- keyframe 不裁预算（每 10 秒一次，10Hz 预算不该把自愈帧切半）。

### 2.5 A 范围（Web 轴只读）

- `Web/VisionMaster.ScadaWeb/ScadaWebCommandRouter.cs`
  - `BuildAllowedSet` 硬排除 `StartFlow/RunFlowOnce/ResumeFlow`（显式 `allowedActions` 也绕不过）；
  - 保留 `StopFlow/PauseFlow`（安全方向）；运动九条仍由 `AllowMotionCommands` 默认关闭。
- 流程触发闸（追加落地）：见 §2.9。`cmd.input` / `WriteVariable` 写“流程触发变量”
  现在回 `E_FLOW_TRIGGER`；guard 未接线时 fail closed（`E_INTERNAL`）。

### 2.6 45 秒死连接

- `Core/Models/AppConfigModel.cs`：新增 `SessionIdleTimeoutSeconds`（默认 45，0=关闭）。
- `ScadaWebCommandPump`：新增 `_executingBySession` 与 `IsSessionBusy`；
  命令执行中不再被当成死连接。
- `ScadaWebHost.SweepDeadSessions`：在 2 秒 presence 循环里扫；
  判据 `无入站 && 无成功出站` 双条件；跳过 `IsRevoked`；命令排队/执行中豁免；
  关闭码 4408 + `bye{reason:"idle-timeout"}`。

### 2.7 大消息上限

- `ScadaWebHost`：`WebSockets.MaxMessageSize` 64KB → **4MB**（出站快照 3000 图元约 1.5–1.8MB）；
  新增 `MaxInboundCommandBytes = 64KB`，在 `HandleTextFrame` 入口拒绝超大入站命令帧。
- 分片仍未实现（§6 第 3 项）；4MB 只是让大页面先跑起来，不解决单条大消息的 `JSON.parse` 阻塞/HOL。

### 2.8 渲染器覆盖度闸

- 新增 `ScadaChecks/WebRendererCoverageChecks.cs`（[WR0-WR4]）：
  - 扫描 `wwwroot/*.js` 的 `ScadaRenderers[...]` / `registry[...]` 注册；
  - 与 `ElementRegistry.All`（`ShowInToolbox`）比对；
  - 已知缺口 5 项显式例外（ChartTrend/DataGrid/ImageView/AxisPanel/AxisConsole），
    例外清单只许变短；实现一个就红，要求删例外。
- `ScadaChecks/Program.cs` 接线一行。

### 2.9 流程触发闸（残余 P0 收口）

- `Scada/Models/IScadaFlowTriggerQuery.cs`：只读查询接口（Id 优先、名字兜底）。
- `VisionMaster/Services/ScadaFlowTriggerQuery.cs`：判据逐字复刻
  `FlowVariableTriggerService`（启用 + `InvokeType.Variable` + `TriggerVariable.Trim()` 相等）。
- `App.xaml.cs` 注册实现；`ScadaWebModule` 解析并传给 `ScadaWebHost`；
  `ScadaWebHost` 传给 `ScadaWebCommandRouter`。
- 路由：`HandleInput` 在 writer 之前拒绝（`E_FLOW_TRIGGER`）；
  `HandleElement` 在动作副本过滤前扫 `WriteVariable` 动作并整条拒绝；
  guard 未接线时 fail closed（`E_INTERNAL`）。
- 断言：M2-13f（`cmd.element`）、M2-13g（`cmd.input`）。

## 3. 断言

新增/修改：

- `Web/ScadaWebChecks/Program.cs`
  - B11b：首帧 keyframe、随后无变化帧是增量空帧；
  - B11c：同页连续帧 `StructureBuildCount` 不增加（缓存命中）；
  - B13b：改一个值 → 增量帧只带 1 项；
  - B14b：结构变更后的第一帧是 keyframe；
  - B45：1 秒判死阈值下，停止运行后的空闲连接收到 4408。
- `Web/ScadaWebChecks/M2CommandChecks.cs`
  - M2-13e：显式 allowedActions 塞入流程启动三类 → 仍被硬排除；Stop/Pause 保留。
- `ScadaChecks/WebRendererCoverageChecks.cs`
  - WR0 解析器非空（防“0 条 = 全都有”假绿）；
  - WR1 例外之外不许缺；WR2 例外不许过期；WR3 只许变短；WR4 例外不许悬空。

## 4. 验证结果

| 命令 | 结果 |
| --- | --- |
| `dotnet build VisionMaster.sln -c Debug` | 0 错误 |
| `dotnet run --project Web\ScadaWebChecks` | **119 PASS / 0 FAIL** |
| `dotnet run --project ScadaChecks` | **2438 PASS / 0 FAIL**（含 WR0-WR4） |

## 5. 关键设计口径（避免下一批返工）

1. **缓存只缓存 raw structure**：caps/未确认报警数/在线列表/租约逐会话或逐拍重算，绝不入缓存。
2. **结构键必须含 runGeneration**：`Page.Version` 是 `[JsonIgnore]`，重载后可能归零。
3. **baseline 只在发送成功后推进**：入队失败时下一拍重发，不能按“已构建”推进。
4. **keyframe 不裁预算**：10Hz 预算只约束增量帧；自愈帧要完整。
5. **无变化也发空帧**：保 10Hz 心跳与 frameSeq 连续；空帧约 150B。
6. **A 范围是“直接入口”保证**：流程启动已封；变量触发旁路见 §6。

## 6. 未做项（下一批，按优先级）

1. **结果查询 `cmd.result`**：WS 线程直接答、`targetRequestId`、`Dedup.TryPeek`（只读不删）；
   撤销后不可查。
2. **快照分片**：`snapshot.chunk{snapshotId,index,total,data,...}`；按 Rune/base64 切；
   客户端 `handleRawMessage` 重组；分片期间暂停 frame；5s 超时/resync。
3. **24 槽准入**：16 活跃 + 8 握手；ticket 核销预留、入册转活跃、失败释放、5s TTL；
   `MaxSessions` 当前仍是 16。
4. **默认值一致性闸 + 5 个复杂图元渲染器**：
   ChartTrend/DataGrid/ImageView/AxisPanel/AxisConsole；
   复杂图元需要运行态数据通道（表格行/曲线采样/图像 token/轴快照），不是单个渲染器函数能补齐。
5. **客户端多基座/增量/预算的浏览器级验证**：本批只做协议层与静态扫描；
   B33/B34 视觉对照、16 端真实浏览器压测仍属测试矩阵未覆盖项。
