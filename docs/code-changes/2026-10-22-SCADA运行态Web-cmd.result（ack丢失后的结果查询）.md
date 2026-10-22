# SCADA 运行态 Web：`cmd.result`（ack 丢失后的结果查询）

- 日期：2026-10-22
- 范围：`Web/VisionMaster.ScadaWeb`（协议层 + 客户端）、`Web/ScadaWebChecks`（断言）
- 前序：`docs/meetings/2026-10-22-SCADA-Web-v4-剩余项评审-R2.md` / `-R3.md` 的 P1 项
  「ack 丢失后无法判定命令是否执行」；本批只做查询，不做自动重发。

## 1. 问题

`cmd.*` 的回执只有 `ack{id, ok, result}` 一条。WebSocket 是可靠有序的，但**服务端在
`TrySend` 失败时会静默丢弃**（会话已断、发送缓冲异常），客户端 5 秒超时后只能判
`E_TIMEOUT`——此时命令可能已经执行成功（写变量、确认报警、点按钮），操作员看到
「超时」会再点一次。虽然幂等表能挡住**同一个 requestId** 的重复执行，但客户端超时后
通常换新 id 重试，于是真的执行两遍。

## 2. 服务端

### 2.1 幂等表只读查询

`ScadaWebCommandDedup.TryPeek(sessionId, requestId, out result, out firstSeenUtc)`
（`ScadaWebCommandRouter.cs`）：

- 与 `Check` 共用 `(sessionToken, requestId)` 键；
- **绝不删除过期项**。`Check` 的语义是"取走并判定"，查询若也删除，一次查询就会把
  证据清掉，随后的同 id 重试变成"新命令"而再次执行——正好毁掉幂等表的意义；
- `result == null` 表示"已登记、尚未完成"（`Check` 在真正执行前就登记了条目），
  对应 `running`；
- 超过 `Retention`（30 分钟）不返回结果，由宿主回 `unknown`。

### 2.2 宿主直答

`ScadaWebHost.HandleTextFrame` 的 `case ScadaWebCommandKinds.Result` →
`HandleResultQuery`：

- **在 WS 线程直答，不入命令泵**。入泵会排在待查命令后面（永远看不到 `running`），
  还要吃版本闸/审计 fail-closed/撤销闸；而 ack 丢失后用户最需要的正是"这条到底执行没有"。
- 响应形状：`{type:"result", id, targetRequestId, state, code, message, result, ageMs}`，
  **没有 `ok` 字段**（它不是 ack，客户端不能把它当命令结算）。
- `state` 四态：`succeeded` / `failed` / `running` / `unknown`。
- 缺 `targetRequestId` → `error{code:"E_BAD_REQUEST"}`。
- 撤销即关连接，撤销后不可查；这是"撤销是安全方向"的既有语义，不额外开口子。

## 3. 客户端

`app.js` 的 `sendCommandOnce` 超时分支：

1. 5 秒无 ack：**条目留在 `pendingCommands` 里**（迟到 ack 仍能正常结算），先摘掉定时器；
2. 发 `cmd.result`（新查询 id，`targetRequestId` = 原命令 id）；
3. `succeeded` → 按成功 resolve（ack 只是丢了，命令真做了）；
   `failed` → 按服务端 code/message reject；
   `running` → 每 700ms 追问，最多 3 次；
   `unknown` / 查询超时 → 按 `E_TIMEOUT` reject，文案写明"未查到执行记录"；
4. **绝不自动重发**。重发可能把一条已执行成功的命令再执行一遍。

查询走独立的 `pendingResultQueries`（与命令的 `pendingCommands` 分开），
会话结束/断链时 `rejectAllPending` 一并收尾，避免每个查询空等 2 秒。

## 4. 断言

| 断言 | 覆盖 |
| --- | --- |
| `M2-13h` | 执行成功 → `TryPeek` 首查/复查都命中且 `Ok`；查询后条目仍在；未知 id 返回 false |
| `B54` | 主宿主（`AllowCommands=false`）也能答 `cmd.result`；未知 id → `state=unknown`、`targetRequestId` 原样回、无 `ok` 字段 |
| `B54b` | 缺 `targetRequestId` → `error{E_BAD_REQUEST}` |

验证结果：

- `ScadaWebChecks`：**127 PASS / 0 FAIL**（本轮前 124，新增 3 条）；
- `ScadaChecks`：**2437 PASS / 1 FAIL**，唯一失败是外部进程改过的
  `VisionMaster/Views/FlowListView.xaml`（缺少 `FlowModel.RunState` 绑定），与本批无关；
- `node --check app.js` 通过。

## 5. 边界与未做

- **不做自动重发**：查询只负责"判明事实"，重试仍是用户的动作；将来若做，必须复用
  原 `requestId` 并等 `AckWindow`（5 分钟）内的原回执，不能换 id。
- `cmd.result` 不查审计、不查版本闸：它是只读查询，不改变任何状态。
- 仍未做（见剩余项评审 R2/R3）：快照分片 + `view.resync` + 控制帧优先通道；
  24 槽握手预占；默认值一致性闸；真实浏览器/网络/长稳。
