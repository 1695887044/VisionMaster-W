# SCADA 运行态 Web A′ · 第五批：24 槽准入 + 默认值归一化

- 日期：2026-10-22
- 分支：`feature/scada-web-v4-a-prime`
- 评审依据：`docs/meetings/2026-10-22-SCADA-Web-v4-剩余项评审-R2.md` / `-R3.md`
- 范围：`Web/VisionMaster.ScadaWeb`、`Web/ScadaWebChecks`、`ScadaChecks/WebRendererCoverageChecks.cs`

## 1. 24 槽准入（16 活跃 + 8 在途握手）

### 问题

取票（HTTP `/api/ticket`）与 WS 入册是两次往返。原实现只在 WS 入册时按活跃数判容量：

- 16 个活跃时，第 17 个客户端仍能取票成功；票被核销后进不了 WS，
  客户端拿到的是一枚"已经死掉"的 8 小时令牌；
- 并发握手在"Count 与 TryAdd 之间"有 TOCTOU（第二批已用原子计数修掉一半）。

### 实现

新增 `ScadaWebAdmissionGate`（`Web/VisionMaster.ScadaWeb/ScadaWebAdmissionGate.cs`）：

| 动作 | 语义 |
| --- | --- |
| `TryReserveHandshake(ticket, ticketExpires, now)` | 票核销成功后占握手槽；同一张票幂等；槽满返回 false |
| `TryPromote(ticket, now)` | WS 入册前把握手槽转活跃槽；活跃满返回 false（**预留已被消耗**） |
| `ReleaseHandshake(ticket)` | 不存在时为空操作 |
| `ReleaseActive()` | 减到 0 为止（重复调用安全） |
| `SweepExpired(now)` | 清 5 秒 TTL 过期项（`min(5s, 票剩余寿命)`） |

接入点：

1. `ScadaWebHost.Authenticate`：`TryRedeem` 成功后 `TryReserveHandshake`；
   失败 → 回收本次新签令牌（续期令牌不回收）+ 审计 + 401。
2. `ScadaWebHost.HandleWebSocket`：`TryPromote` 替代原 `Interlocked.Increment`；
   失败 → **WS 1013 + bye**（不是 HTTP 503）。
3. `RunPresenceLoopAsync`：每拍先 `_admission.SweepExpired(DateTime.UtcNow)`，再清 45 秒死连接。
4. `Stop()`：`_admission.Clear()`。
5. 诊断：`AdmittedCount`（活跃）、`HandshakeCount`（在途）。

### 断言

- `B55`：8 槽满 → 第 9 个拒绝；两批晋升到 16；第 17 个活跃转不过去；释放一个后立即可晋升。
- `B55b`：TTL 清扫 2 个；TTL 后晋升失败；`ReleaseActive` 三次只减一次。
- `B55c`：正常连接关闭后 `AdmittedCount` / `HandshakeCount` 归零。
- `B56` / `B56b`：`MaxSessions=1` 时第二个客户端能过握手、入册被 1013 拒绝；关闭后归零。

## 2. 默认值归一化（目录是唯一事实源）

### 问题

`/api/catalog` 已经给出 `Properties[].DefaultValue`，但客户端从未拉取；渲染器各自写
字面回退（`prop(el,'Format','HH:mm:ss')`）。模型没写某个键时，Web 与 WPF 可能画出不同结果，
且没有任何闸能发现"渲染器读了一个描述符不认识的键"。

### 实现（`wwwroot/app.js`）

1. `loadCatalog()`：`init()` 里与连接并行拉一次 `/api/catalog`（不阻塞握手）。
2. `applyCatalogDefaults(props, typeKey)`：只补**缺失键**，**跳过空串默认值**
   （空串 = 描述符没给默认值，不是"默认值就是空串"）。
3. `normalizeElement` 在返回前补键——先于 `createRecord`，渲染器读属性袋时默认值已就位。
4. 目录晚于首帧：`applyCatalogDefaultsToPage(state.page)` + `renderSnapshot(state.page)` 重放一次。
   只重放不补键没用，因为 `elements-process.js` 的 `rawProp` 不查目录。
5. `ctx.default(typeKey, key)`：新渲染器优先用它，别再写第二份字面默认值。
6. `prop()` 的目录兜底：首帧已画、重放还没发生时仍能返回正确默认值。

### 顺带修掉的真缺陷

`[WR5]` 首次运行抓到 `UnacknowledgedCount`：`AckButtonElement` 的只读运行态 DP
（`AckButtonElement.cs:61-68`），不在模型属性袋里。Web 渲染器原来只从
`el.Properties` / 值帧取它 → 真实运行态永远显示 0。
修复：`createContext.getUnacknowledgedCount()` 读 `state.unacknowledgedCount`
（由 `alarms` 广播维护），AckButton 在字段与值帧都缺失时用它兜底。

### 闸

`[WR5]`（`ScadaChecks/WebRendererCoverageChecks.cs`）：扫描 `prop/rawProp/boolProp/...`
的读键，断言每个键都在 `ElementRegistry` 的描述符里声明。
豁免 3 个并写明理由：`DemoConfirm` / `DemoFail`（演示页夹具）、`UnacknowledgedCount`
（只读运行态 DP）。实测：**55 个读键全部有声明**。

`B57` / `B57b`（`ScadaWebChecks`）：
- `/api/catalog` 原样发出 `DefaultValue`（含空串形态）；
- `applyCatalogDefaults(el.Properties, el.TypeKey)` 的位置在 `createRecord` 之前，
  且 `loadCatalog()` 真的被调用。

## 3. 验证

| 套件 | 结果 |
| --- | --- |
| `ScadaWebChecks` | **134 PASS / 0 FAIL**（本轮前 127，新增 B55/B55b/B55c/B56/B56b/B57/B57b 共 7 条） |
| `ScadaChecks` | **2438 PASS / 1 FAIL**；唯一失败是外部进程改过的 `FlowListView.xaml`（缺 `FlowModel.RunState` 绑定），与本批无关 |
| `node --check` | `app.js` / `elements-interactive.js` / `elements-process.js` 全部通过 |

## 4. 未做（明确记录）

- 分片 + `view.resync` + 控制通道：作为不可拆单位延后（见 R3 §三）。
- 客户端跨刷新/跨重连的"在飞命令"持久化。
- 浏览器 DOM/布局/绘制、真实网卡/交换机/WiFi、重连风暴、>60 秒长稳。
- 既有的 `FlowListView.xaml` 失败不是本批引入，留给 UI 批次处理。
