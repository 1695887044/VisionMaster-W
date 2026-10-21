# SCADA 运行态 Web 客户端 M2a（2026-10-21）

> 范围：M2 第一批——**安全前置六项**（默认密码 gate / Origin / 每 IP 取票限速 / 匿名令牌不续期 / 会话·用户撤销 / 会话表上限）、**命令泵**（每会话 32 → 全局 1024 → 无 `SynchronizationContext` 的线程池消费者 → UI Dispatcher 逐条）、**四条命令与 `caps` 逐图元**、**六个交互图元的只读/禁用/失败反馈**。
> 结论：**M2a 地基 PASS（有条件）**——`Web\ScadaWebChecks` Release **97 PASS / 0 FAIL（第二轮终审整改后）**（33 基线 + 4 静态资产 + 11 动作过滤 + 19 安全 + 26 命令 + 4 回归），`ScadaChecks` Release **2332 通过 / 0 失败**，Release 全量重建 0 错误且符号已核；**矩阵 C68–C80 执行 0 条**，真实浏览器双角色 E2E、WS 4403、`E_CAPACITY` 握手、租约、`caps` 热更新仍无运行时证据。**`AllowCommands` 保持默认 `false`，写路径不对现场开放；M2b 可开工。** 第一轮 **89/89**（`_m2a_webchecks3.log` `CF142B22F99A`）为**历史证据**，见 §3/§4.1。

---

## 1. 范围

| 做 | 内容 |
| --- | --- |
| 安全前置 | `POST /api/ticket` 三闸：每来源 IP 限速（429 `E_RATE_LIMITED`）→ 账号校验（PBKDF2）→ 默认密码 gate（403 `E_DEFAULT_PASSWORD`，响应带 `gate:"DefaultPasswordNotChanged"`）；Origin 同源（403 `E_ORIGIN`）；匿名令牌在 `AllowAnonymousView=false` 后换票 `E_SESSION_EXPIRED`；`RevokeSession`/`RevokeUser` → `E_SESSION_REVOKED`；会话令牌表 `MaxSessionTokens`（默认 4096）满 → `/api/ticket` 503 `E_CAPACITY`、WS 握手 401 |
| 命令泵 | 每会话入站队列 32（16–64，`FullMode=Wait`）→ 全局 `Channel<ScadaWebCommand>` 1024 → `Task.Run` 消费者（无 SyncContext）→ `await _dispatcher.InvokeAsync(() => Execute(cmd))` 逐条；单条异常 → `E_INTERNAL` 且泵继续；两级队列满 → `E_BUSY` + `retryAfterMs`，**不静默丢** |
| 命令 | `cmd.element`（`Pressed`/`Released`）、`cmd.input`、`cmd.alarm.ack`、`cmd.confirm.request`；身份一律从会话对象取，客户端身份字段忽略 |
| 三闸 | `runGeneration` → `pageId` → `pageVersion`；**三项必填**，缺任一 `E_BAD_REQUEST`；都不过只报第一道 |
| 权限/监管 | 权限层四道：`AllowCommands`（默认 false 总闸）→ 会话只读/未登录 → 角色 → `ClickPolicy`；`HighestRoleOnly` 平局都可点、无人登录全员只读；`AllOperators` 五项闸门 + `AllOperatorsRequiresLease` 可选加强；`caps` 逐图元 `canOperate/reason`；`confirmToken` 默认 30s（5–300）/单次/绑定 `(session, element, action)`；在线列表只暴露 `sessionRef` |
| 动作过滤 | 复制 `hook.Actions` → 剔除 `Navigate`（`Skipped/mirror-mode`）→ `RequireConfirmation=false` → 其余保持原顺序；**原模型一个字段都不改** |
| 六图元 | Button / BitButton / TextField / ComboBox / SelectorSwitch / AckButton；只读置灰保留布局、禁用不发命令、失败 toast + 回原态（TextField 保留编辑态） |
| 幂等 | `(sessionId, requestId)` 5 分钟窗口：窗口内重放回原回执、不执行；超出窗口仍在保留期回 `E_DUPLICATE`；空白 `requestId` 不参与去重 |
| 不做（本批） | 租约 `lease.*`、`auth.login`/`auth.logout`、`view.attach`/`view.detach`、`view.resync`、`imageToken`（M1.5）、`E_IDENTITY` 显式伪造检测、100 客户端并发写（M2 全量） |

---

## 2. 改动清单（as-built）

### 2.1 服务端（`Web\VisionMaster.ScadaWeb\`）

| 文件 | 内容 | 状态 |
| --- | --- | --- |
| `ScadaWebActionFilter.cs` | `BuildAllowedCopy(source, allowed, out skipped)`：复制钩子、无条件剔除 `Navigate`、白名单过滤、副本 `RequireConfirmation=false`；原钩子只读 | 已接线，断言 11/11 |
| `ScadaWebSecurityGate.cs` | 默认密码 gate（`E_DEFAULT_PASSWORD`）、Origin 同源（缺头放行 / `Origin: null` 拒绝）、取票限速（`TicketRatePerMinute`，夹取 1–600）、撤销委托 | 已接线，断言 19/19 |
| `ScadaWebCommand.cs` | 四条命令常量、回执码常量、不可变命令对象（持有 `Session`；`RequestId` 只配对回执与幂等） | 已接线 |
| `ScadaWebAccessPolicy.cs` | 权限层四道；`HighestRoleOnly` 平局都可点、无人登录全员只读；caps 与命令共用判据；`ValidateSupervision` + `Normalize` | 已接线 |
| `ScadaWebCommandPump.cs` | 全局 1024 + 单消费者（线程池）+ 每会话待处理计数（默认 32）+ 逐条 `InvokeAsync`；单条异常 `E_INTERNAL` 不退出 | 已接线 |
| `ScadaWebCommandRouter.cs` | 四条命令执行体：三闸必填、图元解析、权限层、动作过滤、命令级审计、`confirmToken`、`(sessionId, requestId)` 5 分钟幂等表 | 已接线，断言 26/26 |
| `ScadaWebPresence.cs` | 在线列表（每行只有 `SessionRef`，无 `SessionId`；字段 `ConnectedAtUtc/LastSeenUtc`） | 已接线 |
| `ScadaWebRateLimiter.cs` | 双层限速：会话 5/s、元素 2/s（跨会话共享） | 已接线 |
| `ScadaWebHost.cs` | ticket 三闸、WS 握手、收帧解析 → 泵、`ack/error` 回执、`caps/presence` 广播、撤销关闭（4403）、容量拒绝回收令牌、`requestId` 校验 | 已接线 |
| `ScadaWebModule.cs` | 构造路由/泵/策略；`AllOperators` 缺项回退；审计缺失 fail closed；退出链停泵 | 已接线 |

### 2.2 配置与前端

| 文件 | 内容 | 状态 |
| --- | --- | --- |
| `Core\Models\AppConfigModel.cs` | `AllowCommands`（:378，默认 `false`）、`ConfirmTokenSeconds`（:388）、`TicketRatePerMinute`（:398）、`MaxSessionTokens`、监管五项、`ElementRatePerSecond`、`ImageTokenSeconds` | 已接入宿主读取路径（`ScadaWebHost.cs:219` 等） |
| `Web\VisionMaster.ScadaWeb\wwwroot\elements-interactive.js` / `.css` | 六图元交互渲染器 | 已加载；静态白名单 7 文件逐字节断言 |
| `Web\VisionMaster.ScadaWeb\wwwroot\app.js` | 四条命令、`caps` 置灰、`ack/error/toast`、`E_CONFIRM` 确认框与 `confirmToken` 重发、4403 回登录不自动重连 | 已落地，真实浏览器 E2E 未跑 |

### 2.3 断言

| 文件 | 内容 | 结果 |
| --- | --- | --- |
| `Web\ScadaWebChecks\Program.cs` | M2 三段 + 回归段并入 `_pass`，最终 `RESULT: 97 PASS / 0 FAIL` | 已跑 |
| `M2SecurityChecks.cs` | 安全前置 19 条（新增 S19：用户名带尾空格仍能被账号级撤销命中） | 19/19 |
| `M2ActionFilterChecks.cs` | 动作过滤 11 条 | 11/11 |
| `M2CommandChecks.cs` | 命令路由/命令泵/第二轮终审整改 26 条（新增 M2-07e / M2-11c / M2-13d） | 26/26 |
| `M2RegressionChecks.cs` | 第一轮终审 P0 回归 4 条（R1 版本基线 / R2 `ArrayIndex` / R3 配置对象身份 / R4 caps 只读态） | 4/4 |
| 静态资产 B4b–B4e | 7 个静态资产逐字节比对 | 4/4 |

---

## 3. 五条 P0 修复

> **历史证据**：本节记录的是**第一轮**（89/89，`_m2a_webchecks3.log` `CF142B22F99A`）的五条 P0；第一轮 **89/89 为历史证据**。第二轮终审新增的两条 P0 与命令面整改见 §8，当前口径为 **97/97**。

| # | 缺陷形状 | 修复 | 证据 |
| --- | --- | --- | --- |
| P0-1 | `elements-interactive.js/.css` 不在静态白名单：页面能打开、交互图元全 404（只在浏览器控制台可见） | 白名单扩到 7 个文件（`ScadaWebHost.cs:97-108`），B4b–B4e 逐字节比对 | 97/97 中 B4b–B4e（第一轮 89/89 为历史证据） |
| P0-2 | 匿名与总闸口径被"签票那一刻"冻结：`AllowAnonymousView` 改 false 后旧匿名令牌仍可续期；`AllowCommands` 改 false 后旧会话仍可写 | 握手（`:739`）、取票（`:985`）、每帧存活判定（`:1537`）都读当前配置；`AllowCommands` 由 `ExplicitReadOnly` + 当前配置判定（`ScadaWebTicketService.cs:265`、`ScadaWebAccessPolicy.cs:177`）。**false→true 同一会话可操作；true→false 下一条命令立即 `E_POLICY`** | M2SecurityChecks S14；C79 未跑 |
| P0-3 | `AllOperators` 先 `Normalize` 再校验：`RequireAudit` 被夹回 true 后，非法配置永远通过 | 用**原始 settings** 先校验监管项（`ScadaWebModule.cs:146-154`），缺项回退 `HighestRoleOnly` 并逐项记 Error，然后才 `Normalize`（`:158-159`） | M2CommandChecks 策略段；C44/C65/C66 未跑 |
| P0-4 | 审计缺失只 Warn、写路径照开；WS 容量拒绝泄漏 8h 令牌；`requestId` 空/超长共用幂等键 | 缺 `IScadaAuditSink` 时 `AllowCommands` 强制 false（`ScadaWebModule.cs:164-170`），路由第二道兜底回 `E_INTERNAL` 不执行（`ScadaWebCommandRouter.cs:147-157`）；容量拒绝回收本次核销令牌（`ScadaWebHost.cs:1253-1256`）；`/api/ticket` 容量预检 503 `E_CAPACITY`（`:1076-1085`）；`requestId` 空或 >64 回 `E_BAD_REQUEST`（`:1458-1465`） | M2CommandChecks 审计缺失段；M2SecurityChecks 容量段 |
| P0-5 | 安全/握手/撤销路径无审计；撤销只清表不关连接 | `AuditSecurity` 覆盖取票限速、容量、默认密码、握手拒绝、撤销；`RevokeSession` 先失效令牌再按 4403 关匹配 WS（`ScadaWebHost.cs:2247-2266`、`:1677-1688`） | M2SecurityChecks S11/S12；真实浏览器 4403 未跑 |

---

## 4. 验证证据

### 4.1 断言

| 项 | 值 |
| --- | --- |
| 命令 | `Web\ScadaWebChecks`（Release，先 build 再 run，R65） |
| 结果 | **97 PASS / 0 FAIL**（33 基线 + 4 静态资产 + 11 动作过滤 + 19 安全 + 26 命令 + 4 回归） |
| 日志 | `_m2a_webchecks13.log`，14552 B，2026-10-21 12:39:26，SHA256 前 12 `AA8A43733714` |
| 被测 DLL | `Web\ScadaWebChecks\bin\Release\net9.0-windows\ScadaWebChecks.dll`，136192 B，12:37:45，SHA256 前 12 `E91456D97780` |
| 回归套件 | `ScadaChecks` Release **2332 通过 / 0 失败**，`_m2a_scadachecks3.log`，355502 B，12:34:31，SHA256 前 12 `92F0CF616A04` |
| 第一轮（历史证据） | **89 PASS / 0 FAIL**，`_m2a_webchecks3.log`，37204 B，10:39:48，`CF142B22F99A`；DLL 131072 B，10:39:35，`C3E16AED480C` |

### 4.2 Release 全量重建与符号核对

| 工程 | 构建 | 符号核对 |
| --- | --- | --- |
| `VisionMaster` | `--no-incremental`，0 错误 | 含 `IScadaAlarmCommands` |
| `VM.Core` | `--no-incremental`，0 错误 | 含 `AllowCommands` |
| `VM.Scada` | `--no-incremental`，0 错误 | 含 `IScadaAlarmCommands` |
| `Web\VisionMaster.ScadaWeb` | `--no-incremental`，0 错误 | 含 `ScadaWebCommandPump`（另含 `ScadaWebCommandDedup`） |

本轮 Release 全量重建 **0 错误**；宿主目录产物 `VisionMaster\bin\Release\net9.0-windows\Web\VisionMaster.ScadaWeb.dll` **176128 B / 12:32:56**。

`VisionMaster\bin\Release\net9.0-windows\Web\wwwroot` 共 **7 个文件**：`index.html`、`app.js`、`styles.css`、`elements-process.js`、`elements-process.css`、`elements-interactive.js`、`elements-interactive.css`。

### 4.3 宿主接线冒烟（12/12）

> 证据边界：以下为 Agent 报告，**无仓内日志**；只证明"接线后主路径可跑"，不替代矩阵 C68–C80 与真实浏览器 E2E。

| # | 项目 | 结果 |
| --- | --- | --- |
| 1 | 起服 | 通过 |
| 2 | 登录取票 | 通过 |
| 3 | `hello.readOnly=false` | 通过 |
| 4 | `caps` 逐图元 | 通过 |
| 5 | `cmd.element` → `ack` | 通过 |
| 6 | 同一 `requestId` 只执行一次 | 通过 |
| 7 | 缺 `runGeneration` → `E_BAD_REQUEST` | 通过 |
| 8 | 撤销后 `E_SESSION_REVOKED` | 通过 |
| 9 | 停服幂等 | 通过 |
| 10 | `presence` 无 `SessionId` | 通过 |
| 11 | 只读部署 `E_POLICY` | 通过 |
| 12 | `AllowCommands=false` 下写命令不执行 | 通过 |

---

## 5. 仍未执行（不得写成通过）

| 项 | 状态 | 说明 |
| --- | --- | --- |
| 矩阵 C68–C80 | **执行 0/13** | 台账 §3 无对应用例行；C68–C80 的判据仍按矩阵 v1.5 |
| 真实浏览器双角色 E2E | 未跑 | 两个独立浏览器 context、登录/置灰/确认框/回登录 |
| WS 4403 | 仅代码落地 | `CloseRevokedSession` 已实现；浏览器实际关闭码未取 |
| `E_CAPACITY` 握手 | 仅代码落地 | 握手恒 401（Watson 的 `AuthResult` 只有 401 语义）；`/api/ticket` 预检 503 |
| 租约 | 未实现 | C27/C28/C47/C77 归 M2b |
| `caps` 热更新 | 仅代码落地 | 2s 广播 + `policyVersion=Environment.TickCount64`；运行时序列未取 |
| `ScadaWebE2E` 工程 | 未建 | 矩阵 §10.3 的 Playwright 夹具仍缺 |

---

## 6. 未决问题

1. **矩阵 C68–C80 未跑**：M2a 的"完成"定义仍缺这一条；任何"M2a 通过"不成立，只能写"地基 PASS（有条件）"。
2. **真实浏览器证据缺口**：双角色 E2E、4403 关闭码、`E_CAPACITY` 握手 401、`caps` 热更新、租约均无浏览器侧证据。
3. **`E_CAPACITY` 的握手 401 体验**：Watson 握手只能表达 401，客户端会把"会话表满"显示成"登录失败"；`/api/ticket` 的 503 是唯一能给出"稍后重试"的入口。需要客户端按 401 + 无 ticket 文案区分，M2b 定文案。
4. **`RequireLogin` 无消费者**：`Core\Models\AppConfigModel.cs:359` 有声明，全仓无读取点。二选一：删除，或在 `ScadaWebHost` 取票/握手处实现"必须登录"（与 `AllowAnonymousView` 的优先级需先裁定）。当前按"预留但未实现"记录，不许当已生效。
5. **`ScadaWebE2E` 未建**：矩阵 §10.3 的 Playwright 工程不存在，E2E 用例只能手工替代。
6. **幂等表是进程内内存表**：停服即清空（`ScadaWebCommandDedup.Clear`）；C38"服务重启时重试"的跨重启幂等没有实现，M2b 决定是否需要持久化。
7. **报警确认的已确认口径**：路由对 `TryAcknowledge` 失败统一回 `E_NOT_OPERABLE`（`ScadaWebCommandRouter.cs:484-491`），与矩阵 C16/C31/C63 的"no-op / Skipped"口径冲突；M2b 统一（要么改实现回 `skipped`，要么改矩阵判据）。
8. **`AllowCommands` 仍为默认 `false`**：写路径不对现场开放；打开前必须先跑 C68–C80 与真实浏览器 E2E。

---

## 7. 结论

- **M2a 地基 PASS（有条件）**：**97/97（第二轮终审整改后；第一轮 89/89 为历史证据）**、`ScadaChecks` **2332/0**、Release 符号已核、12 项接线冒烟通过；第一轮六条 P0 + 第二轮两条新 P0（用户名未 Trim / 监管项被 `ConfigCheck.Load` 回滚）与命令面整改全部关闭（§8）。
- **M2a 不计"通过"**：矩阵 C68–C80 执行 0 条，真实浏览器 E2E 与租约未做；台账记"地基 PASS"，不记"通过"。
- **M2b 可开工**：门槛见方案 §17.7——租约、确认 UI、报警列表、C68–C80 执行、`ScadaWebE2E` 建立。

---

## 8. 第二轮终审整改（2026-10-21 12:33）

> **口径**：第一轮 **89/89**（`_m2a_webchecks3.log` `CF142B22F99A`）为**历史证据**，§3/§4.1 原样保留。第二轮终审 FAIL 出 **2 条新 P0 + 1 条 P1-1**，另有命令面 **P0-3 第三条**（部分成功误记「跳过」）。整改后 `Web\ScadaWebChecks` Release **97 PASS / 0 FAIL**（`_m2a_webchecks13.log` `AA8A43733714`，DLL `E91456D97780`）、`ScadaChecks` Release **2332 通过 / 0 失败**（`_m2a_scadachecks3.log` `92F0CF616A04`）、Release 全量重建 0 错误。**`AllowCommands` 仍默认 `false`；M2a 是「地基 PASS（有条件）」，不是「全绿」。**

### 8.1 第二轮终审新增 P0（2 条，已关闭）

| # | 缺陷形状（为什么编译期全绿） | 修法 | 锚点 | 回归断言 |
| --- | --- | --- | --- | --- |
| 新 P0-1 | `/api/ticket` 解析出 `user` / `refreshToken` 后**没有 Trim**。`"admin "` 与 `"admin"` 被当成两个身份：签票/核销用带空格的名字，`RevokeUser("admin")` 撤不掉 `"admin "` 那条会话 → **改密 / 降权 / 删号后旧会话仍能续期**（用户级撤销被一个尾空格绕过，8 小时令牌继续有效；编译期、静态扫描全绿，只有真登录一次带空格的名字才看得见） | 取票解析后立刻 `user = user.Trim(); refreshToken = refreshToken.Trim();`；票层再 `userName.Trim()` 兜底，保证任何调用方（探针 / 网关）都得到同一身份 | `ScadaWebHost.cs:1077-1078`；`ScadaWebTicketService.cs:277` | **S19**（`M2SecurityChecks.cs:370-381`）：用 `"alice "` 签票后 `RevokeUser("alice")`，令牌必须失效并回 `E_SESSION_REVOKED` |
| 新 P0-2（P1-1） | `AllOperators` 的监管项**只在启动时校验一次**。`ScadaWebModule` 启动时把非法项夹回并记 Error，但随后 `ConfigCheck` 的 `Load()` **原地回写**配置（正是第一轮 P0 ① 为保持对象身份引入的 `PopulateObject`），把 `RequireAudit=false` 之类的非法值又写回同一个对象 → 运行期 `Evaluate` 只看 `ClickPolicy` 就放行，**五项监管闸门形同虚设**（配置看着是开的、实际少一层护栏，且不报错） | 新增静态重载 `ValidateSupervision(ScadaWebSettings?, out string)`；`Evaluate` 的 `AllOperators` 分支**每次判权都复核**，缺项回退 `EvaluateHighestRoleOnly`（fail-safe：只有最高角色可点，不是全员可点） | `ScadaWebAccessPolicy.cs:354`（静态重载）、`:219-224`（用时复核 + 回退）；调用点 `ScadaWebModule.cs:149` | **M2-07e**（`M2CommandChecks.cs:489-504`）：`RequireAudit=false` 时工程师不可点、管理员可点（回退 `HighestRoleOnly`） |

### 8.2 命令面整改（P0-3 第三条 + 同批 7 项，已关闭）

| # | 缺陷形状 | 修法 | 锚点 | 回归断言 |
| --- | --- | --- | --- | --- |
| 命令 P0-3 ③ | 命令级审计的最终结果按「有 skipped 就记跳过」：**部分动作成功、部分被跳过**时也记「跳过」——已经发生的成功动作在审计里变成"没执行"（按审计追责会漏掉真正写下去的那一笔） | 结果改三档：`failed > 0` → **失败**；`ok == 0 && skipped > 0` → **跳过**；其余 → **成功**。一次命令只留一行，details 带 `hooks=/actions=/ok=/skipped=/failed=/details=` | `ScadaWebCommandRouter.cs:389-393`（三档）、`:385-397`（只留一行） | **M2-13d**（`M2CommandChecks.cs:818-825`）：1 成功 + 1 跳过 → 审计 `成功`、`ok=1 skipped=1`、命令级恰好 1 行 |
| ② 确认判据 | 危险判据只看过滤后的副本。副本的 `RequireConfirmation` 被过滤器强制置 `false`，于是"用户勾了执行前确认、动作类型本身不危险（只有日志/写变量）"的钩子**不再要令牌** | `hooks.Any(h => h.RequireConfirmation) \|\| copies.Any(HookIsDangerous)`——原钩子管"用户勾了确认"，副本管"动作类型危险"（被白名单/运动总闸挡掉的不算） | `ScadaWebCommandRouter.cs:320-326` | **M2-11c**（`M2CommandChecks.cs:753-768`）：confirm-only 钩子无令牌 → `E_CONFIRM` 且分发 0 次 |
| ③ 审计列语义 | 事件列写事件类型、动作列写操作摘要，事后按列过滤分不清"这是 web 命令" | 命令级审计统一 **事件=`Web 操作`**、**动作=事件/操作摘要**（如 `Pressed`）；异常路径同口径 | `ScadaWebCommandRouter.cs:376`、`:394` | M2-13d 的明细断言（`hooks=` 前缀行） |
| ④ 结果映射 | `MapWebOutcome` 不认「跳过」——Router 已算出三档，写盘时却落成「失败」 | 映射补 `跳过` / `未执行` / `skipped` → `Skipped`；认不出的词仍按「失败」 | `ScadaAuditWriter.cs:403-425`（`:419-422` 为「跳过」分支） | 随 M2-13d 的审计行结果一并验 |
| ⑤ 泵入口 | `Enqueue` 只判 `session == null`，不判 `command.Session == null`：无会话命令会进泵，后续 `command.Session.IsRevoked` / `SessionId` 在 UI 线程上空引用（崩的是泵消费者，不是请求线程） | `command.Session == null` 直接拒绝入队 | `ScadaWebCommandPump.cs:153` | 无独立断言 ID（见 §8.5） |
| ⑥ 幂等键 | 键用 `SessionId`：断线重连 / 同账号多标签会换连接、换 `SessionId` → 同一 `requestId` 被当成新请求（C02 断线重发、C37 多标签防重失效） | 键改为 **`Session.Session?.Token ?? Session.SessionId`**（8 小时稳定令牌；无令牌才回退） | `ScadaWebCommandRouter.cs:176-182` | 无独立断言 ID（见 §8.5） |
| ⑦ 续期回收 | 容量拒绝时无条件回收令牌：`refreshToken` **续期**命中的是已存在会话，回收它等于把正在续期的会话令牌删掉 | `ScadaWebTicketGrant.SessionReused` 标记"本次是复用而非新签"，宿主只回收**新签**令牌 | `ScadaWebTicketService.cs:85/416/421`；`ScadaWebHost.cs:1305` | 无独立断言 ID（S18 为相邻路径：撤销只挡旧令牌、不该让人登不住） |
| ⑧ 分发结果 | `DispatchWithResult` 默认接口实现返回 `Empty`（"没给明细"）被当成成功：有动作却拿不到任何计数 → 回执/审计显示成功 | `Total == 0 && Actions.Count > 0` → 记失败（让现场去补实现）；分发器抛异常同样记失败，不显示成功 | `ScadaWebCommandRouter.cs:355-363`、`:370-383` | 无独立断言 ID（见 §8.5） |

### 8.3 第一轮六条 P0（复核锚点）

| # | 修法 | 锚点 | 回归断言 |
| --- | --- | --- | --- |
| ① 配置重载保持对象身份 | `Load()` 改 `JsonConvert.PopulateObject(json, Current, …)`（原地填充，不替换 `Current`） | `Core\AppSettingsService.cs:39-71`（`:57`） | **R3**（`M2RegressionChecks.cs:65-67`、`:80-122`） |
| ② 账号写操作撤销 Web 会话 | `IScadaUserChangeNotifier` + `ScadaUserStore` 事件 + `App.xaml.cs` 注册同一单例 + `ScadaWebModule` 订阅并 `host.RevokeUser` | `Scada\Models\IScadaUserChangeNotifier.cs:16`；`VisionMaster\Services\ScadaUserStore.cs:101`；`VisionMaster\App.xaml.cs:449`；`Web\VisionMaster.ScadaWeb\ScadaWebModule.cs:206-245` | S18（撤销后旧令牌失效、重新登录仍可续期） |
| ③ 撤销标记与执行前复查 | `ScadaWebSession.IsRevoked` + Host 标记 + Router 执行前复查 | `ScadaWebSession.cs:101`；`ScadaWebCommandRouter.cs:146` | S18 / S19 |
| ④ 新画面版本基线 | `ScadaPage` 构造 `_version = 1` | `Scada\Models\ScadaPage.cs:412` | **R1**（`M2RegressionChecks.cs:39-42`） |
| ⑤ 动作副本保留 `ArrayIndex` | `CloneAction` 逐字段复制并补 `ArrayIndex`（漏了 = "写数组元素"变成"写整个变量"） | `ScadaWebActionFilter.cs:116-135`（`:122-124`） | **R2**（`M2RegressionChecks.cs:44-63`） |
| ⑥ 审计结果映射与列语义 | `MapWebOutcome` 认「跳过」；Router 事件列 `Web 操作` | `ScadaAuditWriter.cs:403-425`；`ScadaWebCommandRouter.cs:394` | M2-13d |

> **R4**（`M2RegressionChecks.cs:69-77`）不在这六条内：它钉的是客户端 `app.js` 读 `caps.readOnly` 并刷新顶栏——否则 `AllowCommands` 热切换后按钮永远不可点。

### 8.4 证据（2026-10-21 12:33 口径）

| 项 | 值 |
| --- | --- |
| `Web\ScadaWebChecks` Release | **97 PASS / 0 FAIL**（M2ActionFilter 11 / M2Security 19 / M2Command 26 / M2Regression 4 + 33 基线 + 4 静态资产） |
| 日志 | `_m2a_webchecks13.log`，14552 B，2026-10-21 12:39:26，SHA256 前 12 `AA8A43733714` |
| 被测 DLL | `Web\ScadaWebChecks\bin\Release\net9.0-windows\ScadaWebChecks.dll`，136192 B，12:37:45，SHA256 前 12 `E91456D97780` |
| `ScadaChecks` Release | **2332 通过 / 0 失败**，`_m2a_scadachecks3.log`，355502 B，12:34:31，SHA256 前 12 `92F0CF616A04` |
| Release 全量重建 | 0 错误；`VisionMaster\bin\Release\net9.0-windows\Web\VisionMaster.ScadaWeb.dll` 176128 B / 12:32:56 |

### 8.5 本批未新增独立断言（记录在案，不得当成已验证）

§8.2 的 ⑤⑥⑦⑧ 只有代码锚点、没有新增 `Check` ID（本批命令面断言 23 → 26，新增的三条正是 M2-07e / M2-11c / M2-13d）。这四项的失效不会让 97/97 变红，按 R38 的口径记在这里，M2b 补断言前不得表述为"有回归保护"：泵入口空会话拒绝、幂等键取稳定令牌、续期不回收令牌、`Empty` 分发结果记失败。

### 8.6 仍归 M2b（不得写成已做）

审计异步化（命令级审计仍在 UI 线程同步写盘）、完整 HTTP/WS 负向断言、矩阵 C68–C80、真实浏览器双角色 E2E、WS 4403/4401 运行时观测、`E_CAPACITY` 握手、`ShowOnlineUsers`、`basePageVersion` 进三闸、`caps` 的 `elements[]` 协议对齐、`RequireLogin` 死配置。**`AllowCommands` 仍默认 `false`，写路径不对现场开放。**（租约与 WS 4403 断言已在 M2b 落地，见下节；真实浏览器证据仍缺。）

---

## M2b 进展（2026-10-21 13:00）

> **口径**：M2b 已落地 **两项**——操作权租约、AckButton 报警列表；**C68/C69/C70/C72 的 HTTP/WS 端到端 4/4 已补跑**。断言 `Web\ScadaWebChecks` **109 PASS / 0 FAIL**（`_m3_final_webchecks.log` `741A8ABB3234`，DLL `8D732441A002`）、`ScadaChecks` **2340 通过 / 0 失败**。§17.7 六条门槛均已跑动（**C78 六图元视觉反馈未逐控件 E2E**）。**仍待办**：C78 逐控件 E2E、审计异步化、完整 HTTP/WS 负向矩阵；`AllowCommands` 仍默认 `false`，**不写"六条门槛全部闭环"**。

### 1. 操作权租约

| 项 | 内容 | 锚点 |
| --- | --- | --- |
| 实现 | `ScadaWebLease`：`TryAcquire` / `TryRenew` / `Release` / `Snapshot`（TTL 60s；holder 断线即释放） | `Web\VisionMaster.ScadaWeb\ScadaWebLease.cs:39/67/95/136` |
| 判权 | `CanOperate` 在 `OperationLeaseEnabled && AllOperatorsRequiresLease` 时要求 holder，否则 `E_LEASE_HELD` 并带当前持有者 | `ScadaWebAccessPolicy.cs:119-134` |
| 协议 | `lease.acquire` / `lease.renew` / `lease.release` 走真实 WS 回执；caps 每轮下发 `lease{enabled, required, held, holder, holderSessionRef, remainingSeconds}` | `ScadaWebHost.cs:1487-1596`、`:2544-2551` |
| 非法组合 | `AllOperatorsRequiresLease=true` 但 `OperationLeaseEnabled=false` → 校验报缺项并夹回 `false` | `ScadaWebAccessPolicy.cs:410-411`、`:446-449` |

### 2. AckButton 报警列表

| 项 | 内容 | 锚点 |
| --- | --- | --- |
| 数据源 | `IScadaAlarmFeed`（不可变快照，不把报警引擎交给 Web）；`ScadaRuntimeHost.Snapshot()` 返回 `Alarms` + `UnacknowledgedCount` | `Scada\Models\IScadaAlarmFeed.cs:14/:32`；`VisionMaster\Services\ScadaRuntimeHost.cs:811-831` |
| 广播 | `BroadcastAlarms()` 每 **2 秒**推 `{type:"alarms", alarms[], unacknowledgedCount}`（与 `caps`/`presence` 同频） | `ScadaWebHost.cs:2409`、`:2457-2482` |
| 图元 | AckButton 从运行上下文注入 `UnacknowledgedCount`（只读 DP） | `Scada.Controls\Elements\AckButtonElement.cs:195` |
| 接线 | 宿主把同一个 `ScadaRuntimeHost` 注册为 `IScadaAlarmFeed` | `VisionMaster\App.xaml.cs:359` |

### 3. 断言与证据（2026-10-21 13:00）

| 项 | 值 |
| --- | --- |
| `Web\ScadaWebChecks` Release | **109 PASS / 0 FAIL**（38 基线含 B40–B44 + 4 静态资产 + 11 动作过滤 + 19 安全 + 29 命令含 M2-14/15/16 + 4 回归 + 4 HTTP 端到端 C68/C69/C70/C72） |
| 日志 | `_m3_final_webchecks.log`，18540 B，2026-10-21 13:19:36，SHA256 前 12 `741A8ABB3234` |
| 被测 DLL | `Web\ScadaWebChecks\bin\Release\net9.0-windows\ScadaWebChecks.dll`，181248 B，13:19:18，SHA256 前 12 `8D732441A002` |
| `ScadaChecks` Release | **2340 通过 / 0 失败**，`_m2b_scadachecks1.log`，356544 B，12:56:17，SHA256 前 12 `4B0C8CD94647` |
| 历史（13:00 口径） | **105 PASS / 0 FAIL**，`_m2b_webchecks13.log`，15832 B，13:00:46，`E76A9DA86AB1`；DLL 143872 B，13:00:34，`69C401A32C4C` |

新增覆盖：

| 断言 | 覆盖 |
| --- | --- |
| B40 | `alarms` 广播：1 条报警 + `unacknowledgedCount=1` + `alarmId` 一致（`Program.cs:309-321`） |
| B41 | `lease.acquire` 走真实 WS `ack`，回执带 `lease.held=true`（`:323-333`） |
| B42 | `caps` 逐图元 `deny`/`allow` + `lease.enabled`；匿名会话全 deny（`:335-350`） |
| B43 | 撤销会话 → 真实 WS `closeCode=4403`（`:709-717`） |
| B44 | `AllowCommands=false` → `E_POLICY`（`:700-707`） |
| M2-14 | 租约进 `CanOperate`（`AllOperatorsRequiresLease` + `OperationLeaseEnabled`，`M2CommandChecks.cs:524`） |
| M2-15 | 三闸缺字段 → `E_BAD_REQUEST`（`:828`） |
| M2-16 | 幂等重放只执行一次（`:861`） |
| C68–C72 | HTTP/WS 端到端 4/4（`M2HttpChecks`）：C68 出厂密码 → 403 `E_DEFAULT_PASSWORD`、拒绝路径不签票/不建会话（拒绝排在 PBKDF2 之后是产品顺序，`M2HttpChecks.cs:81-176`）；C69 每 IP 限速（`TicketRatePerMinute=2`，第 3 次 429 `E_RATE_LIMITED`、不跑 PBKDF2、按 IP 合并，`:190-264`）；C70 匿名关闭后旧连接可 ping、续期 401 `E_SESSION_EXPIRED`（`:277-382`）；C72 `MaxSessionTokens=2` 满 → 503 `E_CAPACITY`、在线会话不被挤掉（`:396-528`） |

### 4. 真实浏览器双角色 E2E（已跑通核心场景）

| 角色 | 场景 | 证据 |
| --- | --- | --- |
| admin | 登录后 6 图元可点；点「启动按钮」→ 宿主日志 `[E2E] dispatch source=启动按钮 operator=admin actions=1 clicks=1`，审计 `Pressed 启动按钮 成功 hooks=1 actions=1 ok=1 skipped=0 failed=0` | `_m2b_e2e_host2.log:17-18`；`docs\SCADA运行态Web客户端\evidence\m2b-admin-role.png` |
| op | 按钮 `[disabled]`，原因「只读：当前只有「管理员」可以操作（「op」 是「操作员」）」；点顶栏租约后显示 `租约：我（15s）`，宿主收到 `lease.acquire` 并回 ack（`holder=op remaining=60s`） | `_m2b_e2e_host2.log:26`；`docs\SCADA运行态Web客户端\evidence\m2b-operator-readonly.png` |
| admin + op（`--all`，`ClickPolicy=AllOperators`） | 两个真实浏览器标签都点「启动按钮」成功：admin `dispatch … operator=admin clicks=1` + 审计 `ok=1`；op `dispatch … operator=op clicks=2` + 审计 `ok=1`（两种策略都有证据） | `_m3_e2e_all_host2.log:17-18`、`:20-21`；`docs\SCADA运行态Web客户端\evidence\m3-alloperators-op.png` |

> 夹具：`Web\ScadaWebE2E`（真实 `ScadaWebHost` + 六个交互图元 + `admin/admin123`、`op/op123`）。**这证明的是"核心双角色场景 + HighestRoleOnly/AllOperators 两种策略跑通"，不是矩阵 C68–C80 全跑。**

### 5. 仍在 M2b/M3 待办

C78 逐控件 E2E、审计异步化、完整 HTTP/WS 负向矩阵。**`AllowCommands` 仍默认 `false`，写路径不对现场开放。** M3 压力测试已跑（见矩阵 v1.8 基础档 / v1.9 复杂档）：基础档 100 客户端 / 1000 图元 / 2000 命令 `errors=0`；复杂档 20×10（2400 图元 / 7200 绑定）p95=302ms、100×5 p95=2066ms，均 `errors=0`；100 客户端两档 p95 均未达 M8 <500ms，**不写 M3 达标**。
