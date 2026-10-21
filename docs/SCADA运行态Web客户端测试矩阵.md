# SCADA 运行态 Web 客户端测试矩阵

> 版本：v1.5（2026-10-21，**M2a 设计定稿**：安全前置六项、命令泵两级队列、四条命令与 `caps` 逐图元、六图元只读/禁用/失败反馈；C 阶段 **67 → 80 条**，全仓 **248 → 261 条**）
> 范围：桌面端组态 → 点“运行” → HTTP 常驻 → 局域网浏览器多用户同时在线、监控、修改。
> 本文只定义**测试**，不修改任何代码或其它文档。
> **单一事实源**：本文与 `docs\SCADA运行态Web客户端方案.md`（v1.8）共用同一套术语、默认值、协议、状态机、权限策略与审计口径；两份文档冲突时**先改方案、再同步本文**，不允许测试私自放宽，也不允许实现照旧矩阵做。
> **M2a 状态（2026-10-21 10:40）**：宿主已接线、Release 已重建；`Web\ScadaWebChecks` **89/89**（含 B4b–B4e 静态资产）；**C68–C80 执行 0 条**；`AllowCommands=false`；M2b 可开工。台账 M2a 行仍记 `阻塞`（矩阵未跑），不得写"M2a 通过"。
> **M1c 状态**：M1b 五条 blocking 已在 M1c 关闭（方案 §17.5）；证据是 `Web\ScadaWebChecks` **33/33**（`_m1c_webchecks33b.log`，含 B20/B21/B23/B24/B36 协议层）、`_WebProbe6` **31/31**、`ScadaChecks` **2276/0**（`_m1c_sc.log`）。**这些都不是矩阵用例；矩阵 248 条仍未执行**——B20/B21/B23/B24/B36 协议层已跑；B33/B34/B39/B40 部分。**M1 只读核心 PASS、M1 验收矩阵未全跑；M2 可开工。** 执行记录见 `docs\SCADA运行态Web客户端\结果台账.md` §2.7。
> **M0 状态**：M0.2 已收口——代码本体 PASS（全量 `ScadaChecks` 2242/0），**门禁未过**：`B40` / `A01–A09` / `F23` / `F24` 需要监听器，已移到 **M1 首周**（方案 §17.1）。**这四项与 A 阶段 P0 未跑完之前，本文任何用例的“通过”都不构成 M0 验收**。
> 所有用例的判据都以第 0 节为准；实现定版若与第 0 节不同，**先改本文再改测试**，不允许测试私自放宽。
> 相关既有事实来源：`Scada\Models\ScadaRuntime.cs`、`VisionMaster\Services\ScadaRuntimeHost.cs`、`VisionMaster\Services\ScadaActionDispatcher.cs`、`VisionMaster\Services\ScadaUserStore.cs`、`VisionMaster\Services\ScadaAuditWriter.cs`、`VisionMaster\Services\HttpImageServer.cs`、`Scada.Controls\Descriptors\BuiltInElements.cs`、`Scada.Controls.Charts\ChartElements.cs`、`pack.ps1`、`_WebProbe1`、`_WebProbe2`、`_WebProbe3`。

---

## 0. 判定基准（公共 Oracle）

### 0.1 架构不变量

| 编号 | 不变量 | 违反后的典型症状 | 锚点用例 |
| --- | --- | --- | --- |
| I1 | **一个方案运行只有一个 `ScadaRuntime` 内核**；N 个 `WebSession` 只持有认证、连接、视图镜像、有界队列，不持有第二套报警/变量/节拍/审计/流程/运动状态 | 两个浏览器看到不同报警、同一变量两个值、流程被启两次 | A25、B16、B21、C29、C30 |
| I2 | **所有会话命令进入一个 `Channel<ScadaWebCommand>`（全局 1024）**，在 UI Dispatcher 上逐条执行；每会话另有入站配额 32（16–64） | 命令交错、跨线程异常、UI 卡死、命令丢一半 | A14-A21、C73、C74 |
| I3 | **`ScadaPageSnapshot` 不可变 + `PageVersion` 只随结构变化**（图元增删/属性变更/图层变更/切页）；**绑定值变化不递增 `pageVersion`**，值走 `frame{seq,pageId,pageVersion,items[]}`（**不带 `baseVersion`、不带 `t/beatMs`**）；**版本判定是 `PageKey = pageId@pageVersion@basePageVersion`**——跨页只比 `pageId`，同页才比 `pageVersion`，回退按跳变处理；M1b 丢帧靠下一拍全量自愈（无 `view.resync`） | 旧值覆盖新值、页面半新半旧、切页后残留上一页图元、正常值更新被当成结构变更、切到低版本页被误判成回退、改基座网页不更新 | B16-B19、B28、B38、B39 |
| I4 | **M1 只读；M2 开放操作；M3 图元补齐**。阶段能力由服务端强制，不由前端隐藏控件决定 | 前端禁用但原始 WebSocket 仍能写值 | B10、C12、C34 |
| I5 | **每条命令在服务端逐条鉴权**，权限判定只有一份实现（角色 + `ClickPolicy` + 页面/图元 `RequiredRole`） | 越权按钮能按、审计没有 Denied 行 | C10-C12、C23-C28 |
| I6 | **`ClickPolicy=AllOperators` 时必须启用监管**：`RequireOnlineUsers=true`、`RequireAudit=true`（固定 true，不可关；写成 `false` 时**加载即夹回 `true` 并写 Error**）、`RequireRateLimit=true`、`RequireConfirmForDangerous=true`、`ShowOnlineUsers=true`；可选操作权租约/接管（`OperationLeaseEnabled` 默认 false），另有 `AllOperatorsRequiresLease=false`（可选加强：为 true 时只有租约 holder 可点，**不是启用闸门**）。**缺任一项 → 拒绝启用 `AllOperators` 并逐项指出缺哪一条** | 两个操作员互不知道对方在按、出事后查不到人、无限速/无二次确认就放开多人写 | C23-C28、C44、C65、C66 |
| I7 | **审计 CSV 保持 7 列**（时间/操作者/对象/事件/动作/结果/说明），**不新增列**；web 的会话与来源 IP 写在 `说明` 列的固定前缀 `[web s=<sessionId> ip=<remoteIp>]`，**前缀只在 `ScadaAuditWriter.Append(string,…)` 一处拼装**（bridge 只传裸 detail）；**一次点击可能 2 行**（命令级 1 行 + 动作级 N 行），计数按层算；失败与被拒绝也要记 | 审计串号、只记成功、CSV 列错位、报表被加列打断、查不到"谁下的命令"、双前缀导致过滤失效 | C19-C21、C43、C49、C67 |
| I8 | **Watson 路由必须 `auth:true` + `AuthenticateApiRequest` 非空**；握手凭证是 `POST /api/ticket` 发放的**一次性 ticket**（30s/单次），**全程不使用 cookie**；重连用 **`sessionToken`（8h）+ `POST /api/ticket {refreshToken}`** 换新 ticket；`/img` 用**独立 `imageToken`**（M1.5，按会话、30–60s、可轮换、只放 query、与元素+版本绑定），**URL 里不出现 `sessionToken`**；登录复用 `ScadaUserStore.TryValidate`（后台线程 + 两把锁）；默认只读；启用前强制改默认密码（M1c）；登录限速/锁定（M1c）；Origin 校验（M1c） | 未登录拿到画面、默认密码可进、跨站 WebSocket 可连、ticket 被重放、断线后只能重新登录、图片 URL 变成可转发的登录凭据 | A10-A13、B02、B12、B37、F02-F11、F23、F24 |
| I9 | **关闭浏览器标签不停设备**：只销毁该 `WebSession`，内核、流程、报警、运动继续运行 | 关标签导致流程停、设备停 | B23-B24 |
| I10 | **页面钩子 `Loaded`/`Unloaded` 每次进入/离开恰好一次**；同页重复加载不重发 | 进入页面执行两遍初始化、离开页面执行两遍收尾 | B15、B22-B23 |
| I11 | **命令身份由服务端派生**：命令对象只持有 `Session` 引用，`sessionId`/`actor`/`role` 一律不采信客户端字段（M2a 的命令 DTO 里根本没有这些字段）；**M2a 不产生 `E_IDENTITY`**——伪造字段被忽略、按真实身份判权（不够则 `E_ROLE`/`E_POLICY`），显式伪造检测归 M2 后续批；审计只用服务端会话。**在线列表只给不透明 `sessionRef`**（不是 `sessionId`），所以"从 presence 取到别人的 sessionId"这条前置不成立 | 伪造 `Role=Administrator` 就能操作、审计署名错人、凭 presence 枚举他人会话 | C50、F04 |
| I12 | **能力缺口必须可见且不崩**：已知但 Web 未实现的 TypeKey 渲染同尺寸中性占位（显示 Text/Name、固定提示文案、每类型一次日志、像素 oracle 排除该块、嵌套按父坐标换算，**M1b 已实现**）；坏值（`NaN`/`±Inf`/不可解析文本）**保留上一次合法显示**，非法/越界枚举**收敛到安全默认态**（与 WPF 同口径），两者都带诊断角标（**M1.5/M2，M1b 只做值帧透传**） | 整页空白、页面崩、屏幕上出现 `NaN`、操作员以为"数据真的掉到零" | B35（M1.5/M2）、B36（M1b） |

### 0.2 会话状态机

客户端必须能区分以下状态；服务端返回的状态名与方案 §6.2 逐字一致。状态迁移没有列出的边一律视为失败。

| 状态 | 进入条件（M1b 消息） | 服务端行为 | 客户端表现 | 允许的消息 |
| --- | --- | --- | --- | --- |
| `NotRunning` | `hello.running=false` + `idle` | 不推 `snapshot`/`frame` | 空态卡片“桌面端未在运行”、在线人数 | `ping`、`hello`（可选确认） |
| `Unauthorized` | `POST /api/ticket` 401（无凭据且 `AllowAnonymousView=false`，或账号/密码错）、握手 401 | 不下发任何页面/变量数据 | 登录卡片（用户名/密码/错误文案；`AllowAnonymousView=true` 时有"以只读身份查看"） | HTTP `POST /api/ticket` |
| `AtCapacity` | 活跃会话 ≥ `MaxSessions` | 先发 `bye{reason:"at-capacity",closeCode:1013}` 再 close 1013；**不驱逐已有会话**；HTTP 基础页仍可打开 | 拒绝页“已达最大连接数 16，请稍后” | 无（没有可用连接） |
| `ReadOnly` | `hello.readOnly=true` + `snapshot`（M1b 全部会话；M2a 的 `AllowCommands=false`、未登录或匿名只读观众） | 推 `snapshot` + `frame` + `caps`（全 false + 原因）；M1b 的 `cmd.*` 回 `E_UNSUPPORTED`，M2a 回 `E_POLICY` | 画面 + 顶部"只读"徽标 + 控件置灰 | `ping`、`hello`；M2a 还收 `caps`/`presence` |
| `Loaded` | **M2a 起**：`hello.readOnly=false` + `caps` 里至少一个图元 `canOperate=true` | M2a | 画面 + 可点控件（其余置灰） | M2a 四条命令 + `ping`/`hello`；`lease.*` 归 M2 后续批 |
| `RunStopped` | 收到 `stopped` | 保留最后一帧（客户端本地）；新运行 → `snapshot` | 半透明遮罩“运行已停止” | `ping` |
| `Disconnected` | 客户端检测到 WS 关闭/心跳超时 | 会话回收（M1c 空闲回收；M1b 靠连接关闭） | 遮罩 + “连接断开，正在重连（第 N 次）”；退避 **1/2/4/8/10s 封顶** | 重连：`sessionToken` 换 ticket → WS → `hello` |

### 0.3 协议与版本判定

| 项 | 判定 |
| --- | --- |
| 端点 | `GET /`、`/index.html` 基础页；`GET /app.js`、`/styles.css`、`/elements-process.js`、`/elements-interactive.js`（M2a）、`/elements-process.css` 静态文件（**没有 `/assets/*` 路由**）；`GET /api/status`；`GET /api/catalog`；`POST /api/ticket`（`{}` 匿名只读 / `{user,password}` / `{refreshToken}`（M1c）；**M2a 三闸**：Origin → 每 IP 限速 → 默认密码 gate）；`GET /img/...` **M1b/M2a 固定 404**（M1.5 才出图）；`WS /ws` 唯一实时通道。**没有 `/login`、`/logout`、`/change-password`、`/version`**：登录在 `POST /api/ticket`；`auth.login`/`auth.logout` 归 M2 后续批；改密只在桌面端（Web 侧 404） |
| WebSocket 握手 | 浏览器先 `POST /api/ticket`（**M2a：Origin 同源 → 每 IP 限速 10/min → 默认密码 gate**）→ `WS /ws?ticket=…`；路由必须 `auth:true` 且 `AuthenticateApiRequest != null`；无/错/过期/已用 ticket → **401 且业务 handler 不执行**；成功 101。**全程不写 `Set-Cookie`** |
| 图片 | M1b：`/img` 一律 **404 `E_IMG_NOT_READY`**。M1.5：只认 `imageToken`（按会话、30–60s、可轮换、只放 query）；校验"token 属于本会话 + `elementId` 在当前快照 + `version` 等于当前 `imageVersion`"；无/过期 → 401，他人会话/元素不在快照/版本过期 → 403；**`sessionToken` 永不出现在任何 URL** |
| 信封 | **`{type, ...}`，没有 `kind`**；M1b 没有 `v` 字段；`id`（`requestId`）是回执配对键与幂等键（**M2a 起按 `(SessionId, RequestId)` 5 分钟去重**；空或超 64 回 `E_BAD_REQUEST`）。M2a 的身份字段 `sessionId`/`actor`/`role` **被忽略**（命令对象只持有服务端 `Session`），**不产生 `E_IDENTITY`**；三闸字段 `runGeneration`/`pageId`/`pageVersion` 验收要求必填 |
| 客户端消息（M1b） | 只有 `ping{t}` 与 `hello`（可选确认）；其它 `type` 一律 `error{E_UNSUPPORTED}`。**M2a 新增四条**：`cmd.element`（`event` = `Pressed`/`Released`）、`cmd.input`、`cmd.alarm.ack`、`cmd.confirm.request`；`auth.*`/`view.*`/`lease.*`/`img.token` 仍 `E_UNSUPPORTED` |
| 服务端消息（M1b） | `hello` / `hello.ok` / `snapshot` / `page` / `frame` / `stopped` / `idle` / `pong` / `error` / `bye`；**M2a 新增** `caps` / `ack` / `presence` / `toast`；`lease`/`img.token` 归 M2 后续/M1.5 |
| `caps` | **M2a as-built**：`{type:"caps", caps:{policyVersion, runGeneration, pageId, pageVersion, default, defaultReason, deny:[[elementId,reason]], allow:[[elementId]]}}`（`ScadaWebHost.cs:2201-2215`）；每 **2 秒**广播一次（与 `presence` 同频）；`policyVersion = Environment.TickCount64`（不是 `+1` 递增）；`deny`/`allow` 覆盖**全部图元**（含基座页）；`default`/`defaultReason` 目前是固定值（`false` + 固定文案），逐图元判定看 `deny`/`allow`；**旧 `elements:[{elementId, canOperate, reason}]` 形状未实现**（M2b 若要再加） |
| `ack` | `ack{id, ok:true, result:{actions, skipped}}` 或 `ack{id, ok:true, result:{confirmToken, expiresIn}}`（**M2a**）；`id` 与请求 `id` 一一对应 |
| 快照 | `snapshot{type,seq,runGeneration,pageId,pageVersion,page:{PascalCase, 含 BasePage/Elements}}`；`page` 同形（同一轮内 `PageKey` 变化）；**没有 `basePageVersion`（M1c 加）、没有 `layers[]`/`catalogVersion`/`caps`**；不可变，客户端不得回写 |
| 值帧 | `frame{type,seq,runGeneration,pageId,pageVersion,items:[{elementId,property,text,number,bool,color}]}`；**不带 `baseVersion`/`t`/`beatMs`/`full`**；M1b 每拍全量，没出现在 `items` 里的绑定保持上一帧 |
| 在线列表 | **M2a as-built**：`{type:"presence", users:[{sessionRef, user, role, ip, connectedAtUtc, lastSeenUtc, canOperate}]}`（`ScadaWebHost.cs:2139-2151`，小驼峰）；每行只有不透明 `sessionRef`；**没有 `SessionId` / `RoleDisplay` / `ReadOnly` / `OperateReason`**；没有 `highestRole` 字段（最高角色由 `caps` 的原因表达）；每 2 秒一拍 |
| 版本规则 | `pageVersion` **只随结构变化**（图元增删、任一属性变更、图层变更、切页），**绑定值变化不递增**；`runGeneration` 每次成功 `RunStarted` +1；**判定按 `PageKey = pageId@pageVersion@basePageVersion`**：先比 `pageId`（不同 = 切页，不许比两页的 `pageVersion` 大小），同页才比 `pageVersion`；不等 → M1b 丢弃该帧等下一拍 `page`，M2 再发 `view.resync` |
| 幂等 | **M2a 已实现 `(SessionId, RequestId)` 去重**（`AckWindow=5 分钟`、`Retention=30 分钟`，`ScadaWebCommandRouter.cs:1060/1063`）：窗口内重放**返回原 `ack`**、不重复执行、不再记一条成功审计；窗口外仍在保留期回 `E_DUPLICATE`；`requestId` 空或超 64 回 `E_BAD_REQUEST`（`ScadaWebHost.cs:1458-1466`）。C02/C17/C74 具备实现前提（矩阵未跑） |
| 顺序 | M1b：每拍全量帧，不要求客户端处理 `seq`；M2a：命令按入队顺序执行（全局单泵 1024 + 会话内 FIFO 32）；**三闸必填**——缺 `runGeneration` / `pageId` / `pageVersion` 任一 → `E_BAD_REQUEST`（不执行、不进权限层），顺序 `runGeneration → pageId → pageVersion`、都错只报第一道（`ScadaWebCommandRouter.cs:546-590`）；C75 已具备实现（矩阵未跑） |
| 租约 | M2 后续批：`lease.acquire` / `renew` / `release` / `request` / `approve` / `reject` / `steal`；`OperationLeaseEnabled` 默认 false，TTL 60s；**M2a 不实现**，`lease.*` 回 `E_UNSUPPORTED` |

> 审计列口径：CSV 固定 **7 列**（时间/操作者/对象/事件/动作/结果/说明），**不新增列**。web 的会话与来源 IP 写在 `说明` 列的固定前缀 `[web s=<sessionId> ip=<remoteIp>]`；过滤按该前缀子串。一次点击可能 **2 行**（命令级 1 行 + 动作级 N 行），C01/C41/C49 按层计数。**线程口径**：`ScadaAuditWriter` 已加 `_writeGate`（任意线程调用不撕裂），但**顺序仍由命令泵保证**——web 侧所有 `Append` 只经泵的 UI 队列（F23 的握手 401 审计也一样）。

### 0.4 权限策略判定

| 策略 | 判定 | 必须同时满足 |
| --- | --- | --- |
| `AllowCommands`（总闸，**默认 false**） | `false` → 所有 `cmd.*` 回 `E_POLICY`（逐字 `当前为只读模式（ScadaWeb.AllowCommands=false）`）、`caps` 全 false；`hello.readOnly=true`。**与 `ClickPolicy` 是两道独立的门**：总闸关着时角色/策略都不再判 | 运行期可改（推 `caps` + `policyVersion++`）；**已在队列里的命令按执行时的值判**；`true` 只表示"允许写"，不表示"允许所有人写"（C79） |
| `HighestRoleOnly`（默认） | 取**在线已认证会话**的最大角色；等于该角色的会话**都可点**。**同角色不做"最早连接/序数最小"裁决**——要单人操作就开租约 | 最高者离开/降权后 2s 内重新计算；非最高角色命令返回 `E_POLICY` 且带原因（"当前最高：管理员"）；**一个已认证会话都没有 → 全员只读**（`caps.default=false`，原因"当前没有已登录的操作者"） |
| `AllOperators` | 所有满足图元 `RequiredRole` 的已认证会话都可操作 | 必须同时满足 `RequireOnlineUsers=true`、`RequireAudit=true`（**固定 true，不可关**；写 `false` 时加载即夹回 `true` 并写 Error）、`RequireRateLimit=true`、`RequireConfirmForDangerous=true`、`ShowOnlineUsers=true`；可选租约（`OperationLeaseEnabled` 默认 false）；`AllOperatorsRequiresLease=false`（可选加强，为 true 时只有 holder 可点，**不参与启用闸门**）；**缺任一项 → 拒绝启用该策略并逐项指出缺哪一条**（C44/C65），全开时六条监管必须可演示（C66） |
| 图元级 | `RequiredRole` 为空 = 不额外限制；非空按角色包含关系判定（高角色含低角色） | **未登录一律不可操作**（web 与桌面不同：桌面"未登录=操作员"，web 只能看基础页/只读）；非法角色值一律拒绝 |
| M1 | 只读 | `hello.readOnly=true`；任何 `cmd.*` 都返回 `error{E_UNSUPPORTED}`（message="M1 只读镜像不处理该消息类型"），即使角色是管理员；**M2a 起**：`AllowCommands=false` 时改回 `E_POLICY`（不是 `E_UNSUPPORTED`），两者不许混用（C12 vs C79） |
| 租约（可选） | `OperationLeaseEnabled=true` 时，`AllOperators` 下只有 holder 可点；`HighestRoleOnly` 下平局也按 holder 收敛 | **默认 false**（不参与 `AllOperators` 启用闸门）；租约只影响"点击"，不影响"看"；holder 断线立即释放（C47） |

### 0.5 指标门槛

参考机：8 核 CPU、16GB 内存、SSD、千兆局域网、Chrome/Edge 最新稳定版、1920×1080@100%。低于参考机时在证据里记录配置，门槛不放宽；高于参考机也不因“机器快”而放宽正确性判据。

| 编号 | 指标 | 门槛 |
| --- | --- | --- |
| P-UI | UI 线程占用（10Hz 帧循环） | **≤ 50ms/拍**（帧组装 + 值帧发送在 UI 线程上的单拍占用；不含单条命令）。**M1 按 10 图元验收；1000 图元的同门槛属 M3/E05**。原编号 M1 改名，避免与里程碑 M1b 冲突 |
| P-CMD | 单条命令端到端 | **≤ 1000ms**（从入队到 `ack`/`error` 回执，含排队；不含登录等慢操作）。**M2 起生效**（M1b 没有命令）。与 P-UI 是**两条独立门槛**，别混成一条。原编号 M1b 改名 |
| M2 | 命令泵 | 1000 条命令全部执行且恰好一次，总耗时 < 10s，p95 排队延迟 < 100ms，执行线程只有 UI 线程 |
| M3 | 10Hz 推送 | 端到端 p95 < 150ms、p99 < 300ms、最大帧龄 < 500ms；不得丢最后一帧（**M1：10 图元；M3：1000 图元**） |
| M4 | 1000 图元快照（**M3/E04**） | JSON ≤ 2MiB，gzip ≤ 300KB；首屏 ≤ 3s；单会话镜像内存 ≤ 20MB。**M1 不按 1000 图元验收**（B09 只判 10 图元首屏 ≤2s） |
| M5 | 图像 | 1080p JPEG q80 ≤ 300KB/帧；4K ≤ 800KB/帧；1080p ≤ 5fps、4K ≤ 1fps；单客户端平均 ≤ 2Mbps；超预算时降帧/降质/丢弃过期帧，不得排队 |
| M6 | 24h 长稳 | **私有内存增长 ≤ 100MB（预热后）；RSS 仅记录、不设门槛**；句柄数 ±20；线程数 ±4；Gen2 GC ≤ 10 次/分钟；无未处理异常 |
| M7 | 退出链 | 停止服务到进程退出 ≤ 5s；端口释放；WebSocket 以 1001 关闭 |
| M8 | 100 客户端 | ≥ 95% 成功加载；p95 < 500ms；超容量必须显式 `AtCapacity`，不得崩溃 |
| M9 | 重连 | 30s 内重连 ≤ 2s 回到可看图状态（M1 为 `ReadOnly`、M2 为 `Loaded`）；20 次循环无会话/句柄泄漏 |
| M10 | 登录限速 | 同一 user+IP 在 `LoginAttemptWindowSeconds`（默认 300s）内 5 次失败后锁定 `LoginLockoutSeconds`（默认 300s）；锁定期间正确密码也拒绝；测试夹具可显式设 60/60，但**默认值口径不许改** |
| M11 | 容量默认值 | **默认 `MaxSessions=16`、`SessionInboundQueueCapacity=32`（范围 16–64）、`FrameBudgetBytes=120KB`（122880）、`SnapshotMaxBytes=2MiB`（2097152）**；四项可配。验收必须覆盖**默认值**与**把 `MaxSessions` 调到 50/100** 两档（E02/E03/C56/C57/C58/C59/E28 必须先显式设到对应值）。**256 不是合法档位**（超出 16–64），任何用例都不许再出现 |
| M12 | 视觉 | 100% DPI 下与 WPF 基准截图像素差 ≤ 0.1%；125/150/200% 下布局不重叠、不裁切 |
| M13 | 监管与限速默认值 | `CommandRatePerSecond=5`（**按会话独立**）、`ElementRatePerSecond=2`（**同一 `elementId` 跨会话共享**）、`OperationLeaseEnabled=false`、`AllOperatorsRequiresLease=false`（**可选加强，不是启用闸门**）、`RequireOnlineUsers=true`、`RequireAudit=true`（固定）、`RequireRateLimit=true`、`RequireConfirmForDangerous=true`、`ShowOnlineUsers=true`；缺项时 `AllOperators` 必须被拒绝启用 |
| M14 | 图片令牌 | `ImageTokenSeconds=45`（允许 30–60）；token 到期前 `TTL/3` 轮换，旧 token 保留到自己的 `expiresAt`，**同一会话同时最多 3 个存活**（E29 的上限按"会话数 × 3"判）；401/403 判据见 §0.3"图片"行；`Cache-Control` 必须是 `private` 且不含 `immutable` |
| M15 | **M2a 安全默认值** | `AllowCommands=false`（总闸）、`TicketRatePerMinute=10`（1–600，每来源 IP）、`ConfirmTokenSeconds=30`（5–300）、`MaxSessionTokens=4096`（令牌表上限，满了拒绝新会话 `E_CAPACITY`）、`AllowAnonymousView=false`、`ClickPolicy=HighestRoleOnly`、`AllowMotionCommands=false`、`OperationLeaseEnabled=false`。**任一项被改动都必须写进证据的"环境"列**；`AllowCommands` 不得在未过 M2a 门槛时打开 |

### 0.6 自动化分层与证据

| 层 | 载体 | 能证明什么 | 不能证明什么 |
| --- | --- | --- | --- |
| 单元 | `ScadaChecks` / 新增 `WebChecks` 的纯领域段 | 状态机、版本、权限、幂等、审计格式 | 浏览器行为、触摸、IME |
| 集成 | `WebChecks` 控制台宿主（STA + Dispatcher + 原始 WebSocket 客户端 + 假值源/假轴/假报警/假流程） | 命令泵、协议、鉴权、并发、泄漏计数、24h 无头 | 真实渲染、真实浏览器缓存/Origin/CSRF |
| E2E | Playwright（Chromium/Edge）两个或三个独立 context | 登录页、渲染、交互、缓存、ticket 握手（无 cookie）、Origin、截图 | 真实触摸屏手感、真实 IME、真实局域网防火墙 |
| 手工 | 真机浏览器/触摸屏/双机局域网 | 触摸、IME、DPI、防火墙、24h 现场观感 | 精确计数（必须配合日志/CSV） |

证据统一放 `evidence/<用例ID>/`：日志片段、CSV、JSON 指标、截图、Playwright trace、HAR、`netstat`、`dotnet-counters`、dump 摘要。**没有证据的“通过”不算通过；“跳过”不算通过**（R38）。每条用例的执行还要在**结果台账**（§13.2）里留一行——台账没有行 = 没跑过。

### 0.7 需求覆盖索引

| 需求 | 对应用例 |
| --- | --- |
| M0 配置关不加载 / 配置开加载 / DLL 缺失 / 依赖解析 / 类型身份 | A01-A05 |
| Watson `auth:true` 与 `auth:false` 反证 / ticket 握手 / `sessionToken` / Origin | A10-A13、A27-A30、F23、F24 |
| 图片鉴权（`imageToken` 绑定、401/403、轮换；M1.5） | B37、C42、F11 |
| UI 线程命令泵：1000 条无交错、无死锁、超时、背压（M2） | A14-A21 |
| M1b 登录入口（无 ticket → 登录卡片 / 匿名只读 / 默认密码 M1c） | B01-B05、F23 |
| 加载状态机 NotRunning/Unauthorized/AtCapacity/ReadOnly/RunStopped/Disconnected | B06-B12、B40 |
| 镜像切页、同页不重发、加载中切页、跨页低版本、基座热更新 | B14、B15、B28、B38、B39 |
| 快照 / 值帧 / 丢帧自愈 / 过期版本 | B16-B19、B38 |
| 断线重连（`sessionToken` 换票）与恢复 | B12、B13、B27 |
| 10 图元 × 10 客户端 10Hz、两浏览器 + 桌面同时在线 | B20、B21 |
| `Loaded`/`Unloaded` 恰好一次、关标签不停设备 | B22-B24 |
| **M1 门槛：10 图元派生量（已实现）+ 视觉对照、未实现图元占位、文本/字体/中文；坏值/图片归 M1.5** | B33、B34、B36、B38-B40、D21 |
| M2 按钮/位按钮/输入框/下拉/旋钮/报警确认 | C01-C09 |
| **M2a 安全前置**（默认密码 gate / Origin / 每 IP 限速 / 匿名令牌不续期 / 会话·用户撤销 / 会话表上限） | C68-C72、C42、F06、F09、F24 |
| **M2a 命令泵**（每会话 32 → 全局 1024、单条异常隔离、`E_BUSY` 两级背压） | C73、C74、C35、A14-A21 |
| **M2a 三闸**（`runGeneration`/`pageId`/`pageVersion` 顺序 + 必填） | C75、C32、C39、C40 |
| **M2a caps 逐图元 + `AllowCommands` 总闸** | C76、C79、C24、C48、C55 |
| **M2a 六图元只读/禁用/失败反馈** | C78、C22、D10、D11 |
| **M2a 动作副本不改原模型** | C80、C54 |
| **M2a `AllOperatorsRequiresLease`（可选加强，不是启用闸门）** | C77、C66 |
| 服务端逐命令鉴权、角色矩阵、M1/M2a 只读 | C10-C12、C55、C79 |
| 写值/流程/切页/报警确认 | C13-C16、C54 |
| 命令幂等与防重 | C02、C17、C37 |
| 审计 actor/session/IP 前缀、不串号、写失败、**两层计数**、**前缀唯一拼装** | C19-C21、C49、C67 |
| 失败反馈矩阵 | C22 |
| `AllOperators` 与 `HighestRoleOnly`（含三种在线组合与全员登出） | C23、C24、C48 |
| AllOperators 监管：五项开关闸门、在线列表、审计过滤、租约、接管、双层限速、二次确认 | C25-C28、C44、C45、C46、C47、C64、C65、C66 |
| 并发写同一变量、同时启停流程、同时确认报警、同按顺序 | C29-C31、C56、C57 |
| **100 客户端同时写 / 按 / 确认** | C61-C63 |
| 越权、伪造命令/身份、过期 pageVersion、非当前页 | C32-C34、C40、C50 |
| 运动命令默认禁用 / 动作白名单 | C51、C52 |
| 重载下的多用户：50 客户端切页、100 客户端 + 1000 图元 + 图像、原始 WS 绕过 caps | C58-C60、E29 |
| M3 29 条注册项覆盖度与序列化 | D01、D02、D25 |
| AlarmBanner/AxisPanel/DataGrid/ChartTrend/ImageView/Pump/Motor/Pipe | D12-D18 |
| 未知 TypeKey 占位 / 已知未实现 TypeKey 占位 | D03、B36 |
| 10 个派生量对 WPF DP（M1 门槛） | B33（D04-D09 是它的分解证据） |
| 图像编码/下采样/带宽 | B32、D31 |
| 触摸/键盘/IME | D26-D28 |
| 10/50/100 客户端 | E01-E03、E28、E29 |
| 1000 图元、大图、图像突发 | E04-E07 |
| 断线重连风暴 | E08 |
| 24h 长稳 | E09、E25 |
| 内存/句柄/订阅/会话泄漏 | E10、E11、E24 |
| 审计/报警历史文件增长 | E12、E13 |
| 退出链 5 秒、端口反复绑定 | E14、E15 |
| 默认配置容量/队列深度/慢消费者 | E25-E27 |
| 未登录/越权/默认密码/暴力破解/Origin/CSRF/路径穿越/缓存旧版本/静态文件缺失 | F01-F22 |
| ticket 生命周期 / 令牌失效与轮换 | F23、F24 |
| 打包两态、DLL/资源缺失或陈旧、`--no-incremental`、`pack.ps1`、`/api/status` | G01-G16 |
| 轮次门禁（每轮启动/完成、评审签字、结果台账、**文档一致性**、**整改闭环**） | X01-X05 |
| 优化阶段（前后对比、基线不变、可回滚、**噪声带**） | O01-O04 |


---

## 1. A 阶段：M0 模块加载、Watson 鉴权、UI 线程命令泵

| ID | 阶段 | 场景 | 前置条件 | 步骤 | 预期结果 | 自动化方式 | 证据 | 优先级 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| A01 | A / M0 | 配置关 = 不加载 | `AppConfig.json` 中 `ScadaWeb.Enabled=false`；`Web\ScadaWeb.dll` 与静态资源都在；无其它 Web 服务 | 启动宿主，等待 10s；枚举 `AppDomain`；`netstat` 查端口；请求 `GET /` | `ScadaWeb` 程序集不在 `AppDomain`；`Assembly.LoadFrom` 调用计数为 0；端口未监听；`/` 连接被拒；日志恰好一行“未启用”；不读取任何静态资源；主界面正常 | 集成 | AppDomain 清单、LoadFrom 计数、netstat、日志 | P0 |
| A02 | A / M0 | 配置开 = 加载 | `ScadaWeb.Enabled=true`；DLL、静态资源、Watson 依赖齐全 | 启动宿主；查 `AppDomain`、模块实例数、`Initialize` 次数；请求 `/api/status` 与 `/` | 模块恰好加载 1 次（`LoadFrom` 1 次）、`Initialize` 恰好 1 次；端口监听；`/api/status` 200 且字段完整；`/` 返回基础页；`VM.Scada` 从宿主目录加载 | 集成 | AppDomain、LoadFrom 计数、模块日志、HTTP 响应 | P0 |
| A03 | A / M0 | DLL 缺失 | 配置开；把 `Web\ScadaWeb.dll` 改名 | 启动宿主；观察 30s；请求 `/` | 宿主不崩溃、主界面可用；日志一条 WARN 含缺失路径；端口不监听；无每秒重试；状态栏/系统信息显示 Web 不可用 | 集成 | 日志、进程存活、netstat | P0 |
| A04 | A / M0 | 依赖解析与类型身份 | `Web\` 下只有 `ScadaWeb.dll`、Watson 依赖和第三方依赖，**没有** `VM.*.dll` | 启动；反射取模块实现的 `IAppModule` 接口；比较 `Assembly` 引用；打印 `VM.Core`/`VM.Scada` 的 `Location` | 模块的 `IAppModule` 与宿主 `typeof(IAppModule).Assembly` 是同一个 `Assembly` 对象；`VM.Core`/`VM.Scada` 均从宿主目录加载；`Web\` 下 `VM.*.dll` 数量为 0 | 集成 | 反射输出、程序集路径 | P0 |
| A05 | A / M0 | 重复契约程序集反证 | 把 `VM.Core.dll`、`VM.Scada.dll` 复制进 `Web\` | 启动；尝试加载模块；检查 `ElementRegistry` 静态实例与类型身份 | 加载器必须识别“同一契约出现两份”并拒绝加载，日志说明原因；不得出现两个 `ElementRegistry`/两套静态状态。若仍加载成功且类型身份不同，本用例失败 | 集成 | 加载日志、类型身份对比 | P0 |
| A06 | A / M0 | 旧 DLL / 协议不匹配 | 用上一版 `ScadaWeb.dll`（协议版本小于宿主）替换 | 启动；请求 `/api/status`；尝试握手 | 模块拒绝启动或拒绝握手，日志含 `协议版本不匹配 host=x module=y`；宿主继续运行；不注册半套路由 | 集成 | 日志、`/api/status` | P1 |
| A07 | A / M0 | 模块初始化异常隔离 | 注入一个在 `Initialize` 抛异常的测试模块 | 启动宿主；观察主界面与启动自检 | 宿主不崩溃、主界面可用；异常只记一次 Error/Warning；Web 功能标记不可用；启动自检不得因此 `Error` 退出 | 集成 | 日志、进程、启动自检结果 | P0 |
| A08 | A / M0 | 重复初始化/重复加载 | 同一进程调用两次加载，或 `Web\` 放两个模块 DLL | 连续加载两次；查监听端口与模块实例 | 只有一个服务实例、一个监听端口；第二次返回“已加载”且不重复 `Initialize`；不产生两套会话表 | 集成 | 模块计数、netstat | P1 |
| A09 | A / M0 | 端口占用 | 先用 `TcpListener` 占用配置端口 | 启动宿主；观察日志与 UI；释放端口后再启动 | 首次启动失败但宿主不崩溃，日志含端口号与占用原因；UI 可用；释放后重启成功；无每秒重试风暴 | 集成 | 日志、netstat | P0 |
| A10 | A / M0 | Watson `auth:true` + ticket 握手 | 服务已启动；`/ws` 注册为 `auth:true`；`AuthenticateApiRequest` 已赋值并带调用计数与业务 handler 进入计数 | ①申请 ticket 后连接；②不带 ticket；③带错 ticket；④带过期 ticket；⑤重复使用同一 ticket 再连 | ①101，且 `authCalls` **恰好 +1**；②③④⑤ 全部 **401**，`authCalls` 各 +1，**业务 handler 进入计数 == 0**（不靠客户端异常推断）；`Sec-WebSocket-Accept` 正确；错误响应不含账号是否存在的信息、不含堆栈 | 集成 | WS 抓包、`authCalls`/handler 计数、HTTP 状态 | P0 |
| A11 | A / M0 | `auth:false` 反证 + fail-open 反证 | 测试专用路由 `auth:false`（不用于生产）；另起一实例不赋值 `AuthenticateApiRequest` | ①无凭证连接测试路由；②再连接生产 `/ws`；③在"未赋值鉴权器"的实例上连 `auth:true` 路由 | ①101 成功且 `authCalls +0`（证明探针能识别 `auth` 标志）；②生产 `/ws` 401；③**连上**（证明 `AuthenticateApiRequest=null` 时 fail-open），所以生产必须在注册路由前断言非空；静态扫描第一方 WS 路由无 `auth:false` | 集成 + 静态扫描 | 两条握手结果、`authCalls`、扫描输出 | P0 |
| A12 | A / M0 | 会话令牌 resume 校验 | 已登录用户持有效 resume 令牌；准备被篡改、已撤销（登出/被踢）、已过期三种 | 分别用四种令牌 `hello{resume}`；再正常登录一次 | 篡改/撤销/过期三种一律 `hello.ok{resume:false}` 且不恢复身份、不推快照；有效令牌恢复成功**并轮换**（旧令牌立即失效）；401/失败响应无用户信息、无堆栈 | 集成 | WS 帧、会话表 | P0 |
| A13 | A / M0 | Origin 校验 | 配置允许来源 `http://192.168.1.10:8080` | 用 `evil.example`、`null`、子域、大小写/端口变体分别握手；再用精确来源握手 | 非白名单全部 403 且不升级；精确来源 101；不允许通配符；错误响应不泄露白名单内容 | 集成 + E2E | WS 抓包 | P0 |
| A14 | A / M0 | 1000 条命令无交错 | 命令泵已启动；2 个生产者各 500 条；**每会话入站队列用默认 32**（另跑一档 64）；全局命令通道沿用探针夹具的 256（**不是生产默认**，生产入站队列默认 32）；每条模拟 1ms 工作 | 并发写入 1000 条；等待完成；严格解析输出 CSV | 1000 条全部执行且 Id 唯一 0..999；只有 UI 线程执行；无 `CheckAccess` 失败；CSV 每行 5 列且含逗号/引号的负载原样还原；总耗时 < 10s；`Debug.Assert` 失败 0；两档队列容量下都不丢命令 | 集成 | 命令 CSV、线程 ID、耗时、配置快照 | P0 |
| A15 | A / M0 | 背压显式拒绝 | 容量 8；消费者每条 20ms；2 个生产者各 500 条 | 用 `TryWrite` 写入；统计 accepted/rejected；客户端重试同一 `requestId` | accepted + rejected = 1000；executed = accepted；rejected 必须向客户端返回 `E_BUSY`，不得静默丢；重试同 `requestId` 不重复执行；生产代码不得使用 `DropWrite` | 集成 | 计数、ack 列表 | P0 |
| A16 | A / M0 | `DropWrite` 静默丢反证 | 独立小探针 | 容量 4，`FullMode=DropWrite`，连续 `TryWrite` 100 次，再 drain | `TryWrite` 返回 true 100 次但只能读出 4 条（证明该模式会静默丢 96 条）；生产配置必须为 `Wait`；静态扫描无 `DropWrite` | 集成 + 静态扫描 | 探针输出、扫描输出 | P0 |
| A17 | A / M0 | 超时语义 | UI 线程有 800ms 阻塞任务 | 取消一个尚未执行的命令；取消一个正在执行的命令；对 3s 工作 `WaitAsync(2s)` | 未执行的可取消且回调不运行；正在执行的取消不缩短执行；`WaitAsync` 超时不等于“没执行”，最终 ack 必须反映真实结果；无孤儿任务 | 集成 | 操作状态、回调计数、ack | P1 |
| A18 | A / M0 | UI 线程被占时的排队延迟 | UI 线程插入 800ms 阻塞 | 随后发 10 条命令；测排队延迟与心跳间隔 | 最大排队延迟 < 3s；心跳间隔 < 1s；阻塞结束后命令全部执行；无死锁；UI 恢复响应 | 集成 | 延迟 CSV、心跳日志 | P1 |
| A19 | A / M0 | 单条命令异常隔离 | 注入一条会抛异常的命令 | 发送该命令；再发送 10 条正常命令 | 只有来源会话收到 `Failed` ack；泵继续执行后续命令；审计一行 Failed；其它会话不受影响；进程存活 | 集成 | 日志、ack、审计 | P0 |
| A20 | A / M0 | 会话内 FIFO 与全局串行 | 两个会话各发 100 条带 `seq` 的命令 | 并发发送；记录执行顺序 | 同一会话严格按 `seq` 执行；不同会话在命令粒度交错，单条命令内部不交错；全局执行顺序与入队顺序一致 | 集成 | 执行序列 CSV | P0 |
| A21 | A / M0 | 停止时的排空/拒绝 | 队列中已有 50 条命令 | 调用停止服务；观察排空策略与退出时间 | 已接受命令要么在 2s 内执行完，要么收到 `RunStopped`；会话关闭后不得再执行；退出 ≤ 5s；无命令执行两次 | 集成 | 关闭日志、ack、耗时 | P1 |
| A22 | A / M0 | 启动顺序 | 内核未运行；Web 服务已启动 | 取票并连接；再启动内核；观察推送 | 第一次 `hello.running=false` + `idle`，命令不排队等待；内核启动后下一拍收到 `snapshot`；Web 不得自动启动内核 | 集成 | 消息序列、内核状态 | P0 |
| A23 | A / M0 | `/api/status` 契约 | 服务已启动 | 未登录请求 `/api/status`；比对程序集版本 | 返回 `protocolVersion`、`appVersion`、`moduleVersion`、`schemaVersion`、`gate`、`loadState`、`onlineUsers`、`pageSize`；与程序集一致；不含密钥/路径/账号；`Content-Type: application/json`；不允许 `Access-Control-Allow-Origin: *`；**不存在 `/version` 路由**（404） | 集成 | JSON、程序集信息 | P1 |
| A24 | A / M0 | 中文与编码 | 中文用户名、图元文本、命令说明 | 登录、发命令、写审计；用 Excel/严格 CSV 解析器打开 | 中文全程无乱码；WS UTF-8 往返一致；审计 CSV 带 BOM、**7 列**不因中文/逗号错位；`说明` 列前缀 `[web s=… ip=…]` 与中文净效果（"变量：21 → 22"）共存且可被前缀过滤 | 集成 + E2E | 截图、CSV | P1 |
| A25 | A / M0 | 会话隔离 | 10 个会话同时在线 | 会话 A 发命令；检查 B..J 的 ack 与 presence；关闭 A | A 的命令只回 A；B..J 不收到 A 的 ack；会话 ID 唯一；关闭 A 不影响 B..J；presence 计数正确 | 集成 | ack 路由表、presence | P0 |
| A26 | A / M0 | 配置热改语义 | 服务运行中修改 `ScadaWeb.Enabled` | 改 false；再改 true；观察是否热生效 | 若设计为启动期读取：日志明确“需重启生效”，不得出现半开半关；若支持热切换：必须先停旧监听再起新监听，端口不冲突、无双重服务。两种实现只能选一种并写进日志 | 集成 | 配置日志、netstat | P2 |

| A27 | A / M0 | `Static.Add` 参数化路径 | 服务已启动；`/img/{elementId}/{version}.png` 走 `RoutingGroup.Static.Add` | 请求合法路径；请求 `../`、`%2e%2e%2f`、反斜杠、绝对路径、超长文件名；再请求一个不存在的 elementId | 合法路径返回**原样字节**（与源文件 `SequenceEqual`）且 `Content-Type` 按扩展名；四类穿越/非法路径全部 400/404，**不读取 Web 根之外任何文件**；不存在的 id 404 且不泄露目录；`WebserverBase.Get` 未被用于二进制（静态扫描） | 集成 | HTTP 响应、字节比对、扫描输出 | P1 |
| A28 | A / M0 | 消息大小上限 64KB | `WebSocketSettings.MaxMessageSize=65536` | 发送 65536 字节命令；发送 65537 字节命令；发送 1MB 命令 | 65536 边界值被接受（或按协议校验拒绝但连接不断）；65537 与 1MB 被拒并按策略关闭（1009 或 `E_PROTO`）；服务端不 OOM、不卡死；日志记录"消息超限"一次 | 集成 | WS 帧、日志、内存 | P0 |
| A29 | A / M0 | `SendTextAsync` 顺序性 | 同一 `WebSocketSession`；4 个线程各发 250 条带序号的文本 | 并发发送 1000 条；接收端按到达顺序记录 | 1000 条全部到达、0 异常；**若到达顺序与"调用顺序"不一致，则判定"库不保证跨线程顺序"，实现必须改为单发送队列**（判据二选一，不许含糊）：单发送队列时到达顺序 == 入队顺序 | 集成 | 到达序列 CSV、实现方式说明 | P0 |
| A30 | A / M0 | 主动断开会话 | 已连接会话（Guid 已知） | 调 `DisconnectWebSocketSessionAsync(guid, NormalClosure, "test")`；再查 `IsWebSocketSessionConnected(guid)` | 客户端收到关闭帧（1000/NormalClosure + reason）；`IsWebSocketSessionConnected` 返回 false；会话在 2s 内从会话表移除；重复调用同一 guid 不抛异常（幂等）；审计/日志各一条 | 集成 | 关闭帧、`IsWebSocketSessionConnected`、会话表 | P0 |

**A 阶段小计：30 条。**

---

## 2. B 阶段：M1 只读多用户、状态机、镜像与快照

| ID | 阶段 | 场景 | 前置条件 | 步骤 | 预期结果 | 自动化方式 | 证据 | 优先级 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| B01 | B / M1 | 未登录访问 | 服务启动；无 ticket、无令牌 | `GET /`；`POST /api/ticket`；不带 ticket 连 `/ws`；尝试取快照 | `/` 只返回基础页；未带 ticket 的 `/ws` **401 且 handler 不执行**；无任何页面/变量数据；HTML 不含元素 JSON；`/api/status` 可访问但仅最小信息 | 集成 + E2E | HTTP 响应、HTML | P0 |
| B02 | B / M1 | 登录入口（无 ticket → 登录卡片；ticket 换票） | 内核运行；`admin` 正确密码；`AllowAnonymousView` 分别 false/true | ①打开 `/`（无 ticket）→ 登录卡片；②`POST /api/ticket {user,password}`；③`WS /ws?ticket=…`；④收 `hello`；⑤收 `snapshot`；⑥`AllowAnonymousView=true` 时点"以只读身份查看" | ①登录卡片可见（用户名/密码/错误文案），不是"请从桌面端获取链接"；②200 且返回 `{ok,ticket,expiresIn:30,readOnly:true}`、**响应里没有 `Set-Cookie`**；③101；④`hello{sessionRef,readOnly:true,running,pageId,pageName,pageVersion,online}`（M1c 起再带 `sessionToken`）；⑤`snapshot.page` 含 `PageId/Elements/BasePage`；⑥空体取票 200、匿名只读可见；`AllowAnonymousView=false` 时空体取票 401 `E_AUTH_REQUIRED`；浏览器 `document.cookie` 为空 | E2E + 集成 | ticket 响应头、WS 帧、快照、登录卡片截图 | P0 |
| B03 | B / M1 | 登录失败不泄露账号 | 准备不存在用户与错误密码 | 分别登录；比较错误文案与响应时间 | 两种失败文案完全相同（“用户名或密码不正确”）；不创建会话；响应时间差 < 50ms；审计记录失败但不记录密码 | 集成 | 响应、时间、审计 | P0 |
| B04 | B / M1 | 登录限速与锁定 | 同一 user+IP；`MaxLoginFailures=5`、窗口 60s、锁定 60s | 连续 5 次错误；第 6 次用正确密码；等 60s 再试 | 第 5 次后返回 429/锁定；锁定期间正确密码也 429；60s 后成功；审计 5 条失败 + 1 条成功；不同 IP 不受同一计数影响 | 集成 | 状态码、时间线、审计 | P0 |
| B05 | B / M1（默认密码 gate：M1c 未实现，转 M2 前） | 默认密码强制修改（桌面改、Web 只提示） | 全新 `ScadaUsers.json`；`admin` 仍是出厂密码；Web 已启用 | ①`POST /api/ticket {admin,出厂密码}`；②在桌面端改密；③再用新密码取票 + 握手 | **目标（U-13）**：①返回 403 `gate=DefaultPasswordNotChanged`（不是 200 ticket），页面只显示"请先在桌面端修改出厂密码"；②桌面改密成功；③取票 200 + `hello` + `snapshot`；**Web 不提供任何改密接口**（`/change-password` 返回 404）。**M1c 现状**：仍无 gate，①会签发 ticket（只校验账号）——本用例记为失败/阻塞，**不得写"通过"** | E2E + 集成 | 状态码、gate、审计、截图 | P0 |
| B06 | B / M1 | `NotRunning` | 内核未运行；用户已取票 | 连接 WS；等待 5s；启动内核后再看推送 | 第一次 `hello.running=false` + `idle`、无 `snapshot`；不自动启动内核；启动后下一拍收到 `snapshot`；日志各一条 | 集成 | 消息序列、内核状态 | P0 |
| B07 | B / M1 | `Unauthorized` | 未登录（默认 `AllowAnonymousView=false`） | `POST /api/ticket {}`；不带 ticket 连 `/ws`；检查响应 | 取票 **401 `E_AUTH_REQUIRED`**；握手 **401 且 handler 不执行**；响应不含元素、变量、报警数据；登录卡片可见；无每秒重试 | 集成 | 响应、WS 状态、截图 | P0 |
| B08 | B / M1 | `AtCapacity` | `MaxSessions=2`；已有 2 个 `ReadOnly` 会话 | 第 3 个用户取票并连接；关闭一个后再试 | 第 3 个收到 `bye{reason:"at-capacity",closeCode:1013}` 并 close 1013；已有 2 个不受影响、不被驱逐；关闭一个后第 3 个可 `ReadOnly`；无崩溃 | 集成 | 消息、会话数 | P0 |
| B09 | B / M1 | `ReadOnly` 首屏（10 图元） | 内核运行；10 图元页面；局域网 | 取票 → 连接 → 收 `hello`/`snapshot`；记录从连接到首帧的时间 | `hello.readOnly=true`；快照页 ID/名称/版本正确；**10 图元首屏 ≤ 2s**；无空白页闪烁；无控制台错误；**1000 图元首屏 ≤3s 属 M3/E04** | E2E | 截图、Performance trace | P0 |
| B10 | B / M1 | `ReadOnly` | M1b 构建；管理员登录 | 查看控件；用原始 WS 发 `cmd.element` / `cmd.input` / `cmd.alarm.ack` | `hello.readOnly=true`；控件全部只读；原始命令回 `error{code:"E_UNSUPPORTED"}`（message="M1 只读镜像不处理该消息类型"）；无变量写入、无动作级审计；仍持续收到 `frame` | 集成 + E2E | 状态、error 帧、审计 | P0 |
| B11 | B / M1 | `RunStopped` | 已收 `snapshot`；内核运行 | 停止内核；观察推送；发命令；重新启动 | 2s 内推送 `stopped{runGeneration}`；**停止瞬间 `hub.Session`/`hub.Document` 均为 null**（`RunStopped` 在清空之后发布，B1 契约），但客户端保留最后一帧；M1b 的命令回 `E_UNSUPPORTED`（M2 才是 `E_STALE_RUN`）；不自动重启；重新运行后下一拍推 `snapshot`，`runGeneration` 变化 | 集成 | 推送、error、快照 | P0 |
| B12 | B / M1 | `Disconnected` + `sessionToken` 重连 | 已 `ReadOnly`；`hello` 已带 `sessionToken`（M1c） | 断开网络/停止服务；观察客户端状态；恢复网络；再断开 3 次 | 2s 内进入 `Disconnected`；指数退避 **1/2/4/8/10s 封顶**；每次重连都 `POST /api/ticket {refreshToken}` 换新 ticket（旧 ticket 不复用）→ 新 WS → `hello` + `snapshot`；不重新输入密码；`sessionToken` 8h 内有效、过期后回登录卡片；**URL 里不出现 `sessionToken`** | E2E + 集成 | 网络日志、状态时间线、ticket 请求体 | P0 |
| B13 | B / M1 | 断线重连恢复 | 已收 `snapshot`；期间变量变化 5 次；`sessionToken` 有效 | 用 `refreshToken` 换票重连；检查收到的 `hello`/`snapshot`/`frame` | ≤ 2s 回到 `ReadOnly`；新连接**必然收完整 `snapshot`**（新会话没有页键），5 次变化全部在快照/首帧里；不重复执行 `Loaded` 钩子；`pageId` 相同则 `pageVersion` 以服务端当前值为准（**不做"不低于断线前"的断言**）；`pageId` 变了按切页处理（不比较两页版本）；无旧值覆盖 | 集成 + E2E | 版本序列、快照、首帧 | P0 |
| B14 | B / M1 | 镜像切页 | 桌面端与 2 个浏览器都在页 A | 桌面端切到页 B；观察 2 个浏览器 | 两端都收到页 B 快照；`pageId` 变为 B；**跨页不比 `pageVersion` 大小**（页 B 的版本可以比页 A 小，B38 专测这条）；页名/页 ID 为 B；无客户端停留在 A；无混合页面 | E2E | 两端截图、快照 | P0 |
| B15 | B / M1 | 同页导航不重发 | 当前页 A | 桌面端再次导航到 A；观察版本与钩子 | 不产生新快照、不增加 `pageVersion`、`Loaded` 钩子计数不变；客户端不闪烁 | 集成 | 版本、钩子计数 | P1 |
| B16 | B / M1 | 快照完整性 | 页面含 10 图元、底板、基座、隐藏图层、绑定 | 加载；把快照与 WPF 文档模型逐项比对 | `snapshot.page` 是 **PascalCase**；图元数量 == 模型图元数（含隐藏图层，`LayerVisible=false` 保留）；ID/类型/ZIndex/`ParentId`/绑定/基座层全部一致；`page.BasePage` 一层且 `BasePage.BasePage=null`；无 UI 临时字段；无缺失/重复 ID；JSON 可被严格解析 | 集成 | 快照 JSON、模型导出 | P0 |
| B17 | B / M1 | 值帧正确性（`frame`，**无 `baseVersion`/`t`/`beatMs`**） | 已收 `snapshot`；页面有 4 类绑定（文本/数值/布尔/颜色） | 让假值源改值；抓取 `frame`；再抓一帧不变的值 | `items` 是数组，每条 `{elementId,property,text,number,bool,color}`；文本/数值/布尔/颜色四类各至少一条；帧内 `pageId/pageVersion` 与快照一致且**不因值变化递增**；帧里**没有 `baseVersion`/`t`/`beatMs`/`full`**；M1b 每拍全量（同一元素在下一帧仍出现）；解析失败的绑定不出现且客户端保留上一帧 | 集成 | WS 帧、版本 | P0 |
| B18 | B / M1 | 丢帧自愈（**无 `view.resync`**） | 客户端故意丢弃第 N 帧 | 接收 N+1；检查客户端行为 | M1b 每拍全量，N+1 直接覆盖缺失的值；**客户端不发 `view.resync`**（M2 扩展点）；`seq` 缺口不导致画面错位；连续丢 3 帧后最终值仍等于源值 | E2E | 网络帧、最终值 | P0 |
| B19 | B / M1 | 版本跳变 → 等 `page` | 客户端持有 N-5 的 `pageVersion`（期间发生过结构变更） | 收下一帧；再收服务端的 `page` | 客户端**丢弃版本不符的 `frame`**（不套在旧结构上）；服务端下一拍推 `page`（整份 `page` 对象），客户端应用后继续；版本回退同样按跳变处理；期间不渲染半新半旧页面；**M1b 不请求全量快照**（没有请求通道） | 集成 | 帧、`page`、版本 | P0 |
| B20 | B / M1 | 10Hz 推送性能（**10 图元 × 10 客户端只读**） | 10 图元页（≥4 类绑定）；**10 个客户端**；假值源 10Hz；持续 60s | 记录端到端延迟、最大帧龄、**帧循环 UI 占用** | p95 < 150ms；p99 < 300ms；最大帧龄 < 500ms；最后一帧值等于源值；**10Hz 帧循环的 UI 线程占用 ≤ 50ms/拍（P-UI）**；无积压超过 2 帧；**1000 图元/50 客户端的性能属 M3/E05/E02，不在本用例**；单条命令端到端（P-CMD）是 M2，M1b 无命令 | 集成 + E2E | 延迟 CSV、trace | P0 |
| B21 | B / M1 | 两个浏览器 + 桌面同时在线 | 2 个独立浏览器 context + 桌面运行窗口 | 同时加载同一页；观察值、页、版本、generation；桌面切页；桌面停止运行 | 三端 `pageId`/`pageVersion`/`runGeneration` 一致；同一变量值差 ≤ 200ms；桌面切页后两端跟随；**桌面停止后两端收到 `stopped`，此时 `hub.Session`/`hub.Document` 已为 null，但最后一帧仍可见**；无任何一端掉线或分叉 | E2E | 三端截图、版本、时间线 | P0 |
| B22 | B / M1 | `Loaded` 恰好一次 | 页面配了 Loaded 钩子（写变量/记日志） | 桌面进入页 A；第一个浏览器连接；第二个浏览器连接；刷新；重连；统计钩子执行次数 | 每次"内核进入该页"恰好 1 次；新增/关闭 Web 会话、刷新、重连都不重复执行；服务端计数与客户端日志一致 | 集成 | 钩子计数、审计/日志 | P0 |
| B23 | B / M1 | `Unloaded` 恰好一次 | 页面配了 Unloaded 钩子；当前在页 A | 切到页 B；停止运行；关闭一个浏览器标签；关闭全部标签；分别统计 | 切页/停止各恰好 1 次 `Unloaded`；顺序为旧页 Unloaded → 新页 Loaded；**停止运行同时 `RunStopped` 恰一次且其处理器内 `Session`/`Document` 为 null**；关闭任一/全部标签都**不触发** Unloaded、也不触发 `RunStopped`；无重复 | 集成 + E2E | 钩子计数、日志 | P0 |
| B24 | B / M1 | 关标签不停设备 | 流程正在运行；浏览器在监控页 | 直接关闭标签；等待 5s；查内核、流程、设备状态 | `ScadaRuntime.IsRunning=true`；流程/设备继续；不触发 Unloaded；会话在宽限期后清理；审计记录会话关闭；桌面端不受影响 | 集成 + E2E | 内核状态、流程计数、审计 | P0 |
| B25 | B / M1 | 同账号多标签 | 同一用户打开两个标签 | 两个标签各自取票/连接；关闭一个；在另一个操作（M2） | 两个会话 `SessionRef` 不同；关闭一个另一个仍 `ReadOnly`；M2 命令按标签独立幂等（`requestId` 不跨会话去重）；审计可按 `说明` 前缀里的 session 区分 | E2E | 会话表、审计 | P1 |
| B26 | B / **M3（E04）** | 快照体积预算（1000 图元） | 1000 图元页面；局域网 | 加载并抓取快照；gzip 后测量；记录首屏 | JSON ≤ 2MiB；gzip ≤ 300KB；首屏 ≤ 3s；单会话镜像内存 ≤ 20MB；无超时。**M1 不按 1000 图元验收**（10 图元首屏由 B09 判 ≤2s）；若用户要求 M1 即 1000 图元，另立 **M1.5 性能项**，不并回 B 阶段 | 集成 | 字节数、trace、内存 | P0 |
| B27 | B / M1 | 小规模重连风暴（`sessionToken`） | 2 个客户端；每个 20 次断开/重连；60s 内；`sessionToken` 有效 | 循环断网/恢复；每次用 `refreshToken` 换票；统计会话与句柄 | 无重复会话；旧 socket 全部关闭；句柄数回到基线 ±5；每次 ≤ 2s 回到 `ReadOnly`；无端口耗尽；`sessionToken` 表不随重连线性增长（同一令牌复用、不重复签发）；旧 ticket 不复用 | 集成 | 会话表、句柄数、netstat、ticket 计数 | P1 |
| B28 | B / M1 | 加载中切页 | 客户端 A 正在加载页 A；桌面端立即切到页 B | 观察 A 最终状态 | A 要么收到页 B 的 `page`/`snapshot` 并进入 `ReadOnly`，要么明确返回重试；不得把页 A 标记为当前页；不得混合 A/B 图元；**同一 `pageId` 内版本单调**，跨页以 `pageId` 为准（不比较两页版本） | 集成 + E2E | 快照、状态 | P1 |
| B29 | B / M1 | 基座画面组合 | 页 P 的 `BasePageId` 指向基座 B | 加载 P；检查快照与渲染；修改基座后重新加载 | 基座图元在底层、页图元在上层；ID 不重复；z 序与 WPF 一致；基座修改在重新加载后生效；不把基座当导航目标 | 集成 + E2E | 快照、截图 | P0 |
| B30 | B / M1（M1c 未实现，转 M2 前） | 会话空闲超时 | 空闲超时配置为 60s（测试值） | 登录后不发任何命令；等 70s；再发命令 | 服务端关闭 WS（4401 或等价）；客户端 `Unauthorized`；审计记录超时登出；`sessionToken` 同步作废；重连需重新登录。**M1c 仍未实现空闲回收**（只有连接关闭/停服） | 集成 | 关闭码、审计 | P1 |
| B31 | B / M1 | 时钟一致性 | 页面含时钟图元；3 个客户端不同机器 | 同时截图；比较显示时间 | 三端时间与服务器时间差 ≤ 1s；时区口径一致；不因客户端本地时区跳变；跨分钟刷新正常 | E2E + 手工 | 截图、时间戳 | P2 |
| B32 | B / **M1.5** | 图像显示占位与刷新 | 页面含 ImageView；假图像源 5fps | 未绑变量加载；绑定后加载；切页 | 未绑显示“无图像”占位；绑定后按源刷新（`/img/...?t=…` 走 `imageToken`，见 B37）；不出现破图图标；切页后旧图像句柄释放；无内存增长 | 集成 + E2E | 截图、内存 | P1 |

| B33 | B / M1 | **M1 门槛：10 图元已实现派生量（视觉层）** | 10 图元页（`Hmi.Text/Rectangle/Panel/IOField/ProgressBar/Indicator/Lamp/Tank/Valve/Gauge`）；Playwright + WPF 截图 | 每个图元喂三组输入（默认/边界/极值）；在浏览器里读渲染结果的**可见派生量**（灯色、比例、指针角、读数、开度、只读文本），与 WPF 同输入下的可见结果逐项比对 | **已实现（M1b）**：Indicator/Lamp 灯色（`IsOn`/`State`）、ProgressBar 比例、Gauge 指针角（`NormalizeAngle(StartAngle)+NormalizeSweep(SweepAngle)×Ratio()`）、Tank `FillRatio`+`ValueText`（Capacity×ratio+Unit）、Valve 开度+`OpeningText`、IOField 只读文本；`Hmi.Text/Rectangle/Panel` 只比属性袋 + 视觉；**任一不等 → M1 不通过** | 集成 + E2E | 截图、DOM 值、对照表 | P0 |
| B33b | B / **M1.5** | **未实现派生量对 WPF 只读 DP** | 同 B33；STA 线程可离屏构造 WPF 控件 | 每个图元喂三组输入；读 WPF 只读 DP 与 web 派生量逐条比对 | **未实现（M1.5）**：`ProgressBar.FillLength`（像素值，web 用百分比宽度）、`Tank.FillGeometry`（包围盒，web 用圆角裁剪）、`Valve.FillClipGeometry`（几何宽度）、`Gauge.NeedleThickness`、`IOField.DisplayBrush/DisplayStroke/IsEditable`（DP 值；M1 恒只读）；`DisplayFormat`/`ValueFormat`/`Gain`/`Offset`/`FormatType` 的文本格式化；double 1e-6 / 字符串 Ordinal；**本用例不是 M1 门槛** | 集成（`ScadaWebChecks`） | DP 对照表 CSV | P0 |
| B34 | B / M1 | **M1 门槛：10 图元视觉对照** | 同 B33；WPF 运行窗口 1:1（96 DPI）；浏览器 viewport = 页面尺寸、`deviceScaleFactor=1` | 逐状态截图（默认/边界/极值 × 10 图元）；按元素包围盒切块 diff | 几何偏差 ≤1px；填充/描边主色 ΔE ≤3（CIE76）；文字**内容逐字相等**、允许 ≤2px 偏移与 ≤1px 行高差；阴影/发光差异必须进 `已知偏差.md`；除已知偏差外**差异块数 0**；未实现图元块被排除并记录排除数；任一不达标 → **M1 不通过** | E2E（Playwright）+ 手工复核 | 截图、diff 图、已知偏差清单 | P0 |
| B35 | B / **M1.5-M2** | 坏值处理 + 诊断角标（只覆盖 M1 的 10 图元） | 10 图元页（**不含 `Hmi.SelectorSwitch`——它是 M2 图元，坏值由 D11 验证**）；可脚本化假值源 | 依次注入 `NaN` / `+Inf` / `-Inf` / 非法枚举名 / 越界枚举值 / 转不动的文本；再注入合法值 | `NaN`/`±Inf`/不可解析文本 → **保留上一帧合法显示值**（指针角、填充比例、文本都不动）+ 角标；非法/越界枚举 → **收敛到安全默认态**（灯熄灭、阀全关，与 WPF 同口径）+ 角标；角标 8px `#FFB000` 且 `title` 含原因（`数值无效` / `状态值无效：{原值}` / `格式无效：{格式串}`）；下一帧合法值到达后角标消失；屏幕不出现 `NaN`/`undefined`/异常串；服务端按类型去重 Warn。**M1c 也不执行本用例**（只做值帧透传；`Hmi.Lamp` 当前"保持上一次灯色"与"收敛到熄灭"的差异在 M1.5 统一） | 集成 + E2E | 截图、DOM 文本、日志 | P0 |
| B36 | B / M1 | 未实现图元混排占位 | 页面 = 10 图元 + `Hmi.Button` + `Hmi.AlarmBanner` + `Hmi.ImageView`（M1 未实现）+ 1 个嵌在 `Panel` 内的 `Hmi.Button` | 加载；量每个占位块包围盒与快照坐标；读 `title`；查日志；做像素对照 | 占位块 `left/top/width/height/rotate` 与快照一致（±1px）；文字非空（`props.Text` → `props.Name` → 元素名 → `TypeKey`）；`title` 逐字 `网页暂不支持该图元（{TypeKey}）`；每种 TypeKey 日志恰好 1 次；**不出现 0×0 空洞、不崩、不遮挡相邻元素**；嵌在 `Panel` 内的占位不漏出底板；像素对照排除这 3 类块并记录排除数 | 集成 + E2E | 截图、DOM、日志、坐标表 | P0 |
| B37 | B / **M1.5** | **图片鉴权负面（`imageToken`）** | 两个已登录会话 A/B；页面含 ImageView；A 持有效 `imageToken`；另备过期 token、他人 token、错误版本。**M1c 现状：`/img` 一律 404 `E_IMG_NOT_READY`，本用例不执行** | ①不带 `t`；②错 `t`；③用 B 的会话带 A 的 `t`；④用过期 `t`；⑤有效 `t` 但把 `version` 改成旧值/未来值；⑥请求不在当前快照的 `elementId`；⑦A 登出后再用 A 的旧 `t` | ①④⑦ **401**；② **401**；③⑤⑥ **403**；七种都**不返回图像字节**、不泄露 token/路径/内部 ID；有效请求 200 且字节与源一致；响应头 `Cache-Control: private`、**不含 `immutable`/`public`**；拒绝日志按会话每分钟最多一条 Warn | 集成 | HTTP 状态、响应头、字节比对 | P0 |
| B38 | B / M1 | **跨页低版本不误判** | 页 A 多次改属性使 `pageVersion` ≥ 5；页 B 新建（`pageVersion=1`）；客户端在 A 已 `ReadOnly` | 桌面从 A 切到 B；观察客户端判定；在 B 上改一次属性；再切回 A | 客户端按 **`pageId` 变化**处理：接受 B 的 `pageVersion=1`，**不判"版本回退"、不发 `view.resync`**（M1b 无此消息）；B 上改属性 → 同页比较生效（`page` 的 `pageVersion=2`）；切回 A（版本 42）同样不触发回退；全程无全量重载风暴（resync 次数 == 0）。**P0：跨页低版本是镜像正确性的一部分，M1 门槛必须全绿** | 集成 + E2E | 版本序列、resync 计数 | P0 |
| B39 | B / M1（M1c 整改） | **运行中改基座热更新** | 页 P 的 `BasePageId` 指向基座 B；运行中；客户端已收 `snapshot` | 桌面改基座 B 的一个图元属性；计时；观察 `basePageVersion` 与 `page` | 快照信封带 `basePageVersion`（M1c）；`basePageVersion+1`；**下一拍（≤100ms）**推一次 `page`（整份 `page`，不是 `snapshot`）；本页 `pageVersion` **不变**（改的是基座不是本页）；客户端渲染新基座、页图元不动；不触发 `Loaded` 钩子、不整页闪烁；**所有引用该基座的页面都被重建**（不只当前页）。**M1c as-built：信封已带 `basePageVersion`（无基座为 `0`）、`PageKey` 已含基座版本（`ScadaWebChecks` M1c-05/06 实测 2→3 发 `page`）；"所有引用页重建 / 无闪烁 / 不触发 `Loaded`"仍未跑** | 集成 + E2E | 版本、`page` 时间线、钩子计数 | P0 |
| B40 | B / M1 | **晚订阅者拿首帧（`TryGetCurrent`）** | 内核已在运行；web 模块未加载（或模拟"订阅发生在 `RunStarted` 之后"） | 运行中加载模块 → 调 `TryGetCurrent` → 建首帧 → 再停止运行后调一次 | `TryGetCurrent` 返回 true，`session`/`document`/`generation` 与当前轮一致；**1 帧内（≤ 200ms）** 推出 `hello` + `snapshot`（M1b 无 `caps`），`pageId` == `Session.CurrentPage.PageId`；不重复触发 `Loaded`；`RunStopped` 之后返回 **false** 且三个 out 为 null/null/0。**实现已落地（`IScadaRunHub.cs:50` / `ScadaRuntimeHost.cs:285`）；M1c 部分覆盖：会话在 `Start()` 之后连接并收到 `hello`+`snapshot`（`ScadaWebChecks` B9/B10）；"模块运行中加载"与 ≤200ms 未测** | 集成 | 首帧时间、generation、钩子计数 | P0 |

**B 阶段小计：41 条。**

---

## 3. C 阶段：M2 操作、权限、监管、并发与审计

| ID | 阶段 | 场景 | 前置条件 | 步骤 | 预期结果 | 自动化方式 | 证据 | 优先级 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| C01 | C / M2 | 按钮按下（两层审计计数） | M2 构建；按钮配 Pressed 钩子写变量；操作员已登录 | 点击一次；抓 ack、变量值、审计 | 变量只写 1 次；ack `Success`；审计 **2 行**：命令级 1 行（`说明` 以 `[web s=… ip=…]` 开头）+ 动作级 1 行（与桌面同格式、无前缀），**命令级在前**；`操作者`=登录名、前缀里 session/IP 与连接一致；所有会话看到新值；无重复动作 | 集成 + E2E | ack、变量快照、审计 | P0 |
| C02 | C / M2 | 重复 `requestId` 防重 | 同上；客户端双击或断线重发同一 id | 连发两次同一 `requestId`；再发不同 id | 第一次执行；第二次返回**同一 ack 结果**（不重复执行）；不同 id 正常执行；审计只有一条命令级 Success | 集成 | ack、审计计数 | P0 |
| C03 | C / M2 | 位按钮取反 | BitButton 绑 bool 变量；模式 Toggle | 点击两次；观察变量与两态文字/颜色 | false→true→false 各一次；界面文字/颜色同步；**每次点击 = 命令级 1 行（带 `[web s=… ip=…]` 前缀）+ 动作级 1 行（old→new），两次共 4 行**；其他会话同步 | 集成 + E2E | 变量轨迹、截图、审计 | P0 |
| C04 | C / M2 | 位按钮五种模式 | 同一变量；模式 Set/Reset/Toggle/PressOn/PressOff | 每种模式各点击一次，记录最终值 | 最终值分别符合模式定义；每次只产生一次写入；按下/释放不重复触发；失败时显示原因 | 集成 | 变量轨迹、ack | P1 |
| C05 | C / M2 | 数值域输入 | IOField 绑 double 变量；可写 | 输入 `21.5` 回车；输入 `abc` 回车；输入超范围值 | `21.5` 写入且显示一致；`abc` 返回 Failed、保持编辑态、不写入；超范围按图元规则拒绝/夹紧并提示；审计记录成功/失败 | E2E + 集成 | 截图、变量、审计 | P0 |
| C06 | C / M2 | 文本输入域 | TextField 绑 string 变量；最大长度 16 | 输入前后空格；输入 17 字符；输入空串；用 IME 输入中文 | trim 后写入；超长拒绝并提示；空串清除；IME 组合期间不写入，提交只写一次；无拼音串泄漏 | E2E + 手工 | 截图、变量、输入事件 | P1 |
| C07 | C / M2 | 枚举下拉 | ComboBox 绑 int 变量；选项 0=手动/1=自动/2=检修 | 选择“检修”；外部把变量改成 1 | 选择后变量=2；外部改 1 时显示“自动”且不回写；回写环计数 0；**审计 2 行（命令级 1 行 + 动作级 1 行）** | E2E + 集成 | 变量、截图、审计 | P1 |
| C08 | C / M2 | 多档旋钮 | SelectorSwitch 4 档；绑 int | 依次点 0..3；注入写入失败 | 每次档位正确、只有一档高亮；失败时恢复原档并显示原因；**每次点击 = 命令级 1 行 + 动作级 1 行**；无双重触发 | E2E + 集成 | 截图、变量、ack | P1 |
| C09 | C / M2 | 报警确认按钮（全部） | 5 条未确认报警；M2；操作员 | 点“确认全部”；再点一次；与另一用户同时点 | 第一次 5 条变 Acknowledged；反馈“已确认 5 条”；第二次“没有待确认”；并发时每条只迁移一次；**审计 = 命令级 1 行 + 每个被确认报警的动作级行**（按层计数） | 集成 + E2E | 报警状态、ack、审计 | P0 |
| C10 | C / M2 | 服务端逐命令鉴权 | 按钮 `RequiredRole=Engineer`；当前 Operator | 正常点击；再用原始 WS 发同一命令 | 两次都 Denied；无动作、无变量写入；审计 Denied 带“需要工程师/当前操作员”；界面显示原因 | 集成 + E2E | ack、审计、截图 | P0 |
| C11 | C / M2 | 角色包含矩阵 | 三个角色 Operator/Engineer/Administrator；三个 `RequiredRole` | 9 种组合各发一条命令 | 高角色含低角色；Operator 不能做 Engineer/Admin 动作；Admin 全通过；非法角色一律拒绝；无异常 | 集成 | 9 条 ack | P0 |
| C12 | C / M2 | M1 只读拒绝所有命令 | M1 构建；管理员登录 | 发写值/流程/切页/报警确认四类命令 | 全部 `E_POLICY`（message="当前为只读模式"）；无动作、无动作级 Success 审计；前端控件禁用；仍推送值 | 集成 | ack、审计 | P0 |
| C13 | C / M2 | 写变量 | 变量存在且类型匹配；另备不存在变量与类型不匹配 | 写 int/double/string/bool；写不存在变量；写错类型 | 匹配的写入成功且审计 old→new；不存在返回 Failed 带变量名；错类型返回 Failed 带目标类型；无部分写入 | 集成 | ack、变量、审计 | P0 |
| C14 | C / M2 | 流程启停/单次 | 流程已编译；M2 | StartFlow；再次 StartFlow；StopFlow；RunFlowOnce | 首次启动成功；重复启动返回 Conflict 且不产生第二会话；停止成功；单次执行一次；**每条命令 = 命令级 1 行 + 动作级 1 行（4 条命令共 8 行，命令级在前）**；无孤儿运行 | 集成 | 流程状态、ack、审计 | P0 |
| C15 | C / M2 | 切页命令 | 页 A/B 可导航；基座页不可导航 | 导航 B；导航不存在页；导航基座；连续导航 40 次 | B 成功且所有会话镜像；不存在/基座返回明确原因；页栈深度 ≤ 32；无崩溃；无重复 Loaded | 集成 + E2E | 页状态、栈深、审计 | P0 |
| C16 | C / M2 | 报警确认（按 id） | Active、Recovered、Acknowledged、未知 id 各一条 | 分别确认 | Active→Acknowledged；Recovered→Normal；已确认返回 no-op Success；未知 id 返回 Failed；审计结果正确 | 集成 | 报警状态、ack | P1 |
| C17 | C / M2 | 幂等窗口（5 分钟） | 已执行命令的 `requestId` | 4 分钟内重放；6 分钟后重放 | 4 分钟内返回原 ack、不执行；6 分钟后 `error{code:"E_DUPLICATE"}` 且不执行；两种都不产生第二条命令级 Success 审计 | 集成 | ack、审计 | P0 |
| C18 | C / M2 | 序号语义（服务端 `seq` / 命令无 `seq`） | 同一会话；原始 WS 客户端可控制帧顺序 | ①服务端→客户端人为丢一帧（制造 `seq` 缺口）；②客户端并发发两条命令（不带 `seq`） | ①客户端发 `view.resync{lastSeq}`，服务端回全量 `snapshot`，`seq` 基线连续；②命令按**入队顺序**执行、不重排、不丢弃；协议不要求客户端 `seq`，带了也不影响执行顺序 | 集成 | WS 帧、执行序列 | P0 |
| C19 | C / M2 | 审计 7 列 + 前缀 | 两个用户从不同 IP 登录 | 各执行 5 条动作；读取审计 CSV 表头与行 | 表头**恰好 7 列**：时间/操作者/对象/事件/动作/结果/说明（与 `ScadaAuditWriter.Header` 逐字一致）；`操作者`=登录名；`说明` 以 `[web s=<32hex> ip=<ip>] ` 开头且 session/IP 与连接一致；命令级 1 行 + 动作级 N 行（按层计数）；无“未登录”误填；**桌面条目不带该前缀** | 集成 | CSV、表头 | P0 |
| C20 | C / M2 | 审计不串号 | 两个用户交错执行 50 条 | 并发执行；按 actor 与前缀里的 session/IP 分组统计 | 100 条命令 → 命令级 100 行 + 动作级 N 行；每行 actor/session/IP 与来源一致；零串号；严格 CSV 解析 **7 列**；时间戳非递减（同毫秒允许相等） | 集成 | CSV 分组统计 | P0 |
| C21 | C / M2 | 审计写失败不阻断操作 | 用 Excel/独占句柄锁住当日审计文件 | 执行写变量；释放文件；再执行 | 操作仍成功；日志 Warn 一次（不刷屏）；不崩溃；释放后下一行写入成功并报恢复；`IsFaulted` 回到 false | 集成 | 日志、审计、ack | P1 |
| C22 | C / M2 | 失败反馈矩阵 | 准备 `E_UNAUTHENTICATED` / `E_POLICY` / `E_STALE_RUN` / `E_STALE_PAGE` / `E_BUSY` / `E_VALUE` / `E_CONFIRM` / `E_NOT_FOUND` 八种失败 | 逐种触发；观察 UI 与 ack | 每种都有明确中文原因与稳定错误码；UI 不做乐观更新；不静默失败；命令级 ack/error 与审计一致；同一错误的文案不因会话不同而变化。**M2a 口径补充**：`E_IDENTITY` 在 M2a **不产生**（客户端身份字段被忽略，伪造无效但不报错，见 C50）；新增 `E_NOT_FOUND`（图元不在当前页）、`E_SESSION_REVOKED`、`E_CAPACITY`、`E_DEFAULT_PASSWORD` 的反馈见 C68–C72、C75 | E2E + 集成 | 截图、ack、审计 | P0 |
| C23 | C / M2 | `AllOperators` 策略（监管全开） | 策略 AllOperators；`RequireOnlineUsers`/`RequireAudit`/`RequireRateLimit`/`RequireConfirmForDangerous`/`ShowOnlineUsers` 全为 true；两个 Operator | 两人各按同一按钮；再同时按 | 两人都可操作；**每次点击 = 命令级 1 行 + 动作级 N 行（按层计数，不是"每次点击一行"）**；无需租约；presence 显示两人（每行只有 `sessionRef`）；无互相阻塞 | 集成 + E2E | ack、审计、presence | P0 |
| C24 | C / M2 | `HighestRoleOnly` 策略（含平局） | Engineer + Operator 在线；策略 HighestRoleOnly | Operator 按；Engineer 按；Engineer 离线后再按；两个 Engineer 并列各按一次 | Operator 回 `E_POLICY` 并提示当前最高角色；Engineer 成功；Engineer 离开后 2s 内 Operator 获得资格；**两个 Engineer 并列时两人都可点**（无“最早连接优先/序数最小”）；每人各产生自己的一次动作与审计，无双重动作 | 集成 + E2E | ack、presence、审计 | P0 |
| C25 | C / M2 | 在线用户列表 | 3 个用户、2 个标签；策略 AllOperators | 加入/离开/切页/改角色/空闲；观察列表 | 2s 内更新用户名、角色（`Role` + `RoleDisplay`）、**不透明 `SessionRef`**（**不是 `SessionId`**）、IP、连接时间、最后活动、`ReadOnly`、`CanOperate/OperateReason`；**字段白名单**：presence 行里不出现 `SessionId`/`SessionToken`/`ticket`/`UserAgent`；关闭标签后不残留幽灵；同一用户多会话分行显示；租约状态归 M2 后续批（本批 presence 不含 `lease`） | E2E + 集成 | 列表快照、presence 帧 | P0 |
| C26 | C / M2 | 审计按监管维度过滤 | 已有 100 行审计 | 按 actor（`操作者` 列）、session/IP（`说明` 前缀子串）、时间、结果过滤；导出 | 过滤结果与原始行一致；导出保留 **7 列**；命令级行数与命令数一致、动作级行数与动作数一致；无漏行/重复行 | 集成 + E2E | 过滤结果、导出 CSV | P1 |
| C27 | C / M2 | 操作权租约 | AllOperators；租约启用；TTL 60s | A 申请；B 申请；A 续租；A 停止续租 70s；B 再申请 | A 获得；B 被拒并显示持有者；续租成功；TTL 到期自动释放；B 可申请；审计申请/续租/过期；无死锁 | 集成 | 租约状态、审计 | P1 |
| C28 | C / M2 | 租约接管 | A 持有租约；B 是管理员/监管者 | B 接管；A 再发命令 | B 成为唯一持有者；A 收到通知并失去操作权；A 命令 Denied；接管审计含双方；无两个持有者 | 集成 + E2E | 租约、ack、审计 | P0 |
| C29 | C / M2 | 并发写同一变量 | 2 个用户各写同一 int 变量 100 次 | 并发发送不同值；记录全局执行顺序 | 命令被单泵串行化；每个成功写都有 old→new 审计；最终值等于最后一次被接受的命令；无丢写、无交错、无两个值同时成立 | 集成 | 变量轨迹、审计、ack | P0 |
| C30 | C / M2 | 同时启停流程 | 同一流程；2 个用户同时 Start 与 Stop | 同时发送 Start/Stop；重复 Start；重复 Stop | 最终状态与最后一条被接受命令一致；不会出现两个运行会话；重复 Start 返回 Conflict；重复 Stop no-op；审计完整 | 集成 | 流程状态、ack、审计 | P0 |
| C31 | C / M2 | 同时确认同一报警 | 1 条 Active 报警；2 个用户同时确认 | 并发发送确认；检查状态与 ack | 报警只迁移一次；一个 Success、一个 no-op/Skipped；两条 ack 都不报错；审计不出现两次状态迁移 | 集成 | 报警状态、ack、审计 | P0 |
| C32 | C / M2 | 过期 `pageVersion` | 客户端持有 N；服务端已到 N+1 | 带 N 发写值/切页命令 | 返回 `E_STALE_PAGE`；不执行；客户端刷新快照后可重试；审计不记 Success | 集成 + E2E | ack、版本、审计 | P0 |
| C33 | C / M2 | 伪造命令 | 构造不存在的 elementId、未配置的 action、未知 action 类型 | 逐条发送原始 WS 命令 | 全部拒绝；无动作；返回稳定错误码；审计 Failed/Denied；进程不崩溃；不泄露内部堆栈 | 集成 | ack、审计 | P0 |
| C34 | C / M2 | 绕过前端禁用 | 前端控件禁用；直接发原始命令 | 发送写值/流程命令 | 服务端按策略拒绝（`E_POLICY`/`E_ROLE`）；无动作；审计记录；证明安全边界在服务端 | 集成 | ack、审计 | P0 |
| C35 | C / M2 | 会话队列满（默认 32 与调到 64） | ①默认 `SessionInboundQueueCapacity=32`；②显式设 64；消费者故意变慢 | 每档连发 300 条；再重试被拒的 `requestId` | ①前 32 条接受并最终执行、其余回 `E_BUSY`；②前 64 条接受；两档都：**无静默丢**（`TryWrite` 返回 true 却未入队即判失败）；重试同一 `requestId` 在排空后成功且不重复执行；审计只记执行过的 | 集成 | 计数、ack | P0 |
| C36 | C / M2 | 会话撤销/降权 | 管理员踢人；把 Engineer 降为 Operator；改密码 | 分别操作；观察被影响会话 | 踢人：WS 关闭（4403 或等价），状态 Unauthorized；降权：下一条命令 Denied；改密：旧会话失效；均有审计 | 集成 + E2E | 关闭码、ack、审计 | P1 |
| C37 | C / M2 | 多标签同用户防重 | 同一用户两个标签；同一 `requestId` | 两个标签同时发同一命令 | 只执行一次；两个 ack 返回同一结果；presence 仍 2 个会话；审计一条命令级 Success（前缀里的 session 为实际执行者） | 集成 + E2E | ack、审计、presence | P1 |
| C38 | C / M2 | 服务重启时的命令 | 命令执行中杀掉服务进程 | 杀进程；重启；观察客户端是否自动重发；再用同一 `requestId` 重试 | 客户端不得自动重发未确认命令，必须先重连并取快照；若实现持久化幂等，重试不得造成第二次执行；审计最多一条命令级 Success；变量状态与审计一致 | 集成 | 审计、变量、ack | P1 |
| C39 | C / M2 | 运行停止时写值 | 内核已 Stop；会话状态 `RunStopped` | 发写值/流程/切页 | 全部 `E_STALE_RUN`（message="运行已停止"）；无写入、无流程启动；审计 Denied；不自动重启内核 | 集成 | ack、审计 | P0 |
| C40 | C / M2 | 非当前页图元 | 当前页 B；持有页 A 的 elementId | 发页 A 图元命令 | 返回 `E_STALE_PAGE`；不执行；审计 Denied；无跨页副作用 | 集成 | ack、审计 | P0 |
| C41 | C / M2 | 动作链部分失败（两层审计计数） | 一个钩子配 3 条动作：成功、失败、成功 | 触发钩子；抓每步结果 | 按顺序执行；**一条失败不中断后续动作**（既有 `ScadaActionDispatcher` 契约）；ack 列出每步结果；审计 **4 行**：命令级 1 行 + 动作级 3 行（Success/Failed/Success），命令级在前；失败动作有原因；无静默跳过 | 集成 | ack、审计 | P1 |
| C42 | C / M2 | 命令 CSRF/Origin（无 cookie） | 已登录；构造跨站页面与错误 Origin 的 WS | ①跨站页面申请 ticket；②带错误 Origin 握手；③无/错 ticket 握手；④正确 ticket+Origin 发写命令 | ①`POST /api/ticket` 被 Origin 校验拒（403 `E_ORIGIN` + 审计 Denied，不发放 ticket）；②403；③401 且 handler 不执行；④成功；**响应全程无 `Set-Cookie`**；URL 里只有一次性 ticket、**会话令牌不在 URL** | 集成 + E2E | HTTP/WS、响应头、审计 | P0 |
| C43 | C / M2 | 审计 CSV 转义 | actor 含逗号/引号/换行；`说明` 里前缀之后含中文逗号/引号/换行 | 执行动作；严格 CSV 解析；用 Excel 打开 | 每行仍 **7 列**；引号翻倍正确；换行不劈行；Excel 中文正常；回读字段与原文一致（含前缀完整） | 集成 | CSV、Excel 截图 | P1 |
| C44 | C / M2 | `AllOperators` 缺监管配置 → 拒绝启用 | 策略 AllOperators；分别制造四种缺项：`RequireOnlineUsers=false` / `RequireRateLimit=false` / `RequireConfirmForDangerous=false` / `ShowOnlineUsers=false`（每次只坏一项，其余全开；`RequireAudit=false` 由 C65 单独覆盖） | 每次启动服务；尝试登录并操作；读配置校验日志 | 四种情况**各自被拒绝启用 `AllOperators`**（回落到 `HighestRoleOnly`），Error 日志**逐项指出缺哪一条**（含配置键名与实际值）；不静默降级、不静默按 AllOperators 运行；宿主继续可用；四种都不允许出现"操作员能点" | 集成 | 启动日志、配置校验、caps | P0 |

| C45 | C / M2 | 速率限制（每会话 + 每元素） | M2；`CommandRatePerSecond=5`（**按会话独立**）、`ElementRatePerSecond=2`（**同一 `elementId` 跨会话共享**）；同一已认证会话 | ①1 秒内对 **6 个不同元素**各发 1 条（不同 `requestId`）；②对**同一元素** 1 秒内发 3 条；③等 1 秒后再发 1 条 | ①**前 5 条执行、第 6 条 `E_RATE_LIMITED` + `retryAfterMs`**，不执行；②第 3 条 `E_RATE_LIMITED`（元素级 2/s，所有会话合计）；被限速的命令**只有命令级 Denied 审计、无动作级行**；③窗口过后正常执行；不同会话的**会话级**额度互不影响，但**元素级额度共享** | 集成 | ack 序列、审计、时间戳 | P0 |
| C46 | C / M2 | `confirmToken`（无 token / 单次 / 重放 / 过期 / 他人） | M2；`AllowMotionCommands=true`；危险动作清单含 `StopFlow`；操作员有权限 | ①不带 token 触发 `StopFlow`；②申请后 30s 内触发一次；③同一 token 再触发；④申请后等 31s 触发；⑤会话 A 申请、会话 B 使用；⑥换一个 element 使用 A 的 token | ①`E_CONFIRM` + Denied，不执行；②成功；③拒绝（used）；④拒绝（expired）；⑤拒绝（mismatch/session）；⑥拒绝（mismatch/element）；每次拒绝都有命令级 Denied 审计且 detail 带 `reason`；桌面**不弹模态框** | 集成 | ack、审计、时间线 | P0 |
| C47 | C / M2 | 租约全链路（acquire→request→approve/reject→超时→断线释放） | `AllOperators`；`OperationLeaseEnabled=true`；`OperationLeaseSeconds=60`；A/B 两会话 | ①A acquire；②B 直接发命令；③B request；④A approve；⑤B 发命令；⑥A request、B reject；⑦A request、B 不响应 31s；⑧B 断线 | ①A=holder；②`E_LEASE_HELD`；③B=requester 且双方收到 `lease`；④holder 转 B、A 立即失去操作权；⑤成功；⑥回到 holder=B、A 无租约；⑦30s 超时自动回到 holder=B；⑧B 断线后立即释放为 none 并推 `presence`；acquire/request/approve/reject/timeout/release 六类审计齐全 | 集成 | lease 状态序列、presence、审计 | P1 |
| C48 | C / M2 | `HighestRoleOnly` 三种在线组合 + 全员登出 + 2s 接手 | 三账号 Operator/Engineer/Administrator；策略 `HighestRoleOnly`；**不开租约** | ①仅 Operator 在线；②Operator+Engineer；③三者在线；④全部登出；⑤Admin 登出（Engineer 仍在线） | ①Operator 可点；②仅 Engineer 可点、Operator `E_POLICY`；③仅 Admin 可点、其余 `E_POLICY`；④全员只读（`caps.default=false`，原因"当前没有已登录的操作者"）；⑤Admin 登出后 **2s 内** Engineer 的 caps 变 true、`policyVersion+1`，Engineer 命令成功 | 集成 + E2E | caps 序列、ack、审计 | P0 |
| C49 | C / M2 | 审计两层计数 | 一个按钮钩子配 2 条动作（写变量 + Log）；M2 | 点击一次；读审计；再点一次 | 每次点击 = 命令级 1 行 + 动作级 2 行 = **3 行**，两次共 6 行；命令级行带 `[web s=… ip=…]`、动作级不带；命令级行紧邻其动作级行之前；按层统计"命令数 2 / 动作数 4"；**不许**用"每次点击恰好一行"作为断言 | 集成 | CSV 行序、计数 | P0 |
| C50 | C / M2 | 身份伪造与令牌复用 | 已登录 Operator；从 presence 取到 Administrator 的**不透明 `sessionRef`**（**presence 没有 `sessionId`**，F04 也不允许枚举他人会话）；另备另一台机器（不同 IP） | ①把 `sessionRef` 当 `sessionId` 填进命令；②`role` 改成 `Administrator`；③`actor` 改成 admin；④用 A 的 `sessionToken` 在另一条连接换票；⑤同一令牌从另一 IP 换票 | ①-③ **全部不产生提权**：M2a 的命令对象不含身份字段，客户端字段被**忽略**（不报 `E_IDENTITY`），按真实身份判权——不够就 `E_ROLE`/`E_POLICY`；审计 `操作者` 仍是真实登录名、前缀仍是真实 session/IP；④⑤ **允许**（M1c 口径：同一 `sessionToken` 可开多条连接、不轮换、`BoundIp=null` 不绑 IP），但每条连接有独立 `SessionId`/`SessionRef`，审计按各自会话区分；**五条都没有产生"用别人身份执行"的动作级 Success**。**M2a as-built 偏差**：`E_IDENTITY` 不产生（显式伪造检测归 M2 后续批，§9.8）；跨 IP 复用不拦是**已知边界**（无 TLS/网络隔离是唯一边界，R-08） | 集成 | ack、审计、会话表 | P0 |
| C51 | C / M2 | 运动命令默认禁用 | `AllowMotionCommands=false`（默认）；`AllowedActions` 不含运动动作；假轴命令口已就位 | 原始 WS 发使能/定位/回零/点动/清警 5 条命令；再分别打开 `AllowMotionCommands` 与把动作加进 `AllowedActions` 重试 | 第一轮 5 条全部 `E_ACTION_FORBIDDEN` + Denied 审计、假轴命令口记录 **0 次**；只开 `AllowMotionCommands` 仍拒绝（不在白名单）；两者都开才执行且必须带 `confirmToken` | 集成 | ack、假轴命令口、审计 | P0 |
| C52 | C / M2 | `AllowedActions` 白名单 | `AllowedActions` 只含 `Log` 与 `WriteVariable`；钩子配 `StartFlow` | 触发钩子 | `Log`/`WriteVariable` 执行；`StartFlow` 跳过并审计 `Skipped`（detail 含 `not-allowed`）；ack `skipped=1`；流程状态不变；全部动作都被跳过时命令级行 outcome=`Skipped` | 集成 | ack、流程状态、审计 | P1 |
| C53 | C / M2 | 认证类审计口径 | 两个用户；管理员可踢人；可改密 | ①成功登录；②错误密码 1 次；③登出；④空闲超时；⑤被踢；⑥改密后旧会话再发命令 | ①`Web 登录`/Success；②`Web 登录`/Denied（detail 带原因、**不含密码**）；③`Web 登出`/Success（reason=logout）；④reason=timeout；⑤reason=evicted；⑥`E_UNAUTHENTICATED` + 审计；六条都带前缀；**审计与日志里 grep 不到密码明文** | 集成 | CSV、日志 | P1 |
| C54 | C / M2 | `Navigate` 过滤（副本模式） | 钩子配 3 条动作：`Navigate`(目标页 B) + 写变量 + Log；桌面当前页 A | 触发钩子；观察桌面页、变量、审计、ack | 桌面页**仍是 A**（不切页）；写变量与 Log 执行；`Navigate` 审计 `Skipped`（detail `mirror-mode`）；ack `skipped=1`；桌面日志**无切页记录**；副本 `RequireConfirmation=false` → 桌面**不弹模态框**；不产生任何"请桌面端切页"的隐式请求 | 集成 + E2E | 桌面页 ID、审计、ack、桌面日志 | P0 |
| C55 | C / M2 | TOCTOU：执行前复判 | caps 允许（Engineer 为最高）；命令发出前把该用户降为 Operator | 降权后立即发命令（caps 尚未刷新） | 命令按**执行时**角色判 → `E_ROLE`，不执行；随后 `caps` 推送更新、`policyVersion+1`；审计 Denied 带 `Allows` 原文；不存在"caps 说能点、执行也成功"的窗口 | 集成 | ack、caps、审计 | P0 |
| C56 | C / M2 | 20 会话同按：执行顺序 == 入队顺序 | `AllOperators`；**显式 `MaxSessions>=20`**（默认 16 会直接 `AtCapacity`）；20 个已认证会话；同一按钮 | 20 个会话同一时刻各发 1 条命令（各自 `requestId`）；服务端记录执行顺序 | 执行顺序 == 入队顺序（与客户端发送序一致）；命令级审计 20 条、顺序一致；动作级 20 条；变量最终值 = 最后一条被接受命令的值；无交错、无丢命令；指标 CSV 记录 `MaxSessions` 实际值 | 集成 | 执行序列 CSV、审计、配置快照 | P0 |
| C57 | C / M2 | 10/50 客户端同时写/按/确认/启停 | 10 客户端（再跑 50，**50 档必须显式 `MaxSessions>=50`**）；`AllOperators` 监管全开；同一变量、同一按钮、同一报警、同一流程 | 同时写同一变量、同按按钮、同确认报警、同启停流程 | 每类操作最终状态唯一（变量=最后接受值；按钮动作逐条执行；报警只迁移一次；流程只有 0/1 个运行实例）；**命令级审计条数 == 被接受命令数**；无"两个最终状态"；**50 客户端档 p95 ≤ 300ms**；指标 CSV 记录 `MaxSessions` 实际值 | 集成 | 变量/流程/报警状态、审计计数、指标 | P0 |
| C58 | C / M2 | 50 客户端 + 桌面连续切页 20 次 | `MaxSessions` 显式设 ≥50；50 客户端 `Loaded`；桌面在 2 页间切 | 连续切页 20 次（每次间隔 2s） | 50 个会话全部跟随；每次切页恰好一次新 `snapshot`；旧页命令回 `E_STALE_PAGE`；**无混合页**（同一时刻所有会话 pageId 一致，允许 ≤1 帧差）；**无丢页**（20 次切页对应 20 个 pageId 序列）；桌面 `Unloaded`/`Loaded` 各 20 次 | 集成 | 会话 pageId 时间线、快照计数、钩子计数 | P0 |
| C59 | C / M2 | 100 客户端 + 1000 图元 + 10Hz + 图像（只读负载） | `MaxSessions` 显式设 ≥100；1000 图元页；10Hz；2 路 1080p 图像 | 100 客户端同时加载，持续 10 分钟；采样 p95/p99、CPU、内存、帧大小 | ≥95% 成功 `Loaded`（其余显式 `AtCapacity`，不崩）；**p95 ≤ 500ms**（与 M8/E03/E28 同一门槛；p99 只记录、不设门槛）；单帧 ≤ `FrameBudgetBytes`（120KB）；CPU < 80%；内存增长 ≤ 50MB；图像丢帧 < 1%。**本用例只压只读负载；100 客户端的写/按/确认由 C61–C63 覆盖** | 集成 + 手工 | 指标 CSV、trace、进程计数器 | P0 |
| C60 | C / M2 | 原始 WS 忽略 caps | 已认证 Operator；caps 显示按钮不可点；限速 5/s；租约由他人持有（M2 后续批） | 原始 WS 直接发：①超速率命令 ②无 token 的危险动作 ③非 holder 的租约命令 ④伪造身份的写值 | ①`E_RATE_LIMITED`；②`E_CONFIRM`；③`E_LEASE_HELD`；④**按真实身份判权**——不够则 `E_ROLE`/`E_POLICY`（M2a 不产生 `E_IDENTITY`，客户端身份字段被忽略）；四条全部不执行、全部有命令级 Denied 审计；前端置灰与否不影响服务端判定（证明安全边界在服务端） | 集成 | ack、审计 | P0 |
| C61 | C / M2 | **100 客户端同时写同一变量** | `MaxSessions` 显式 ≥100；100 个已认证会话；监管全开；同一 int 变量（同一元素，**元素级 2/s 由 100 个会话共享**） | ①同一秒内 100 个会话各写 1 次（各自 `requestId`）；②把 100 条摊到 100 秒（每秒 1 条）再跑一遍 | ①**恰好 2 条执行**（`ElementRatePerSecond=2`），其余 98 条 `E_RATE_LIMITED` + `retryAfterMs`，不执行、无动作级 Success；**审计 = 命令级 100 条（2 条 Success + 98 条 Denied）+ 动作级 2 条**（被限速命令也留命令级 Denied，见 C45/I7）；无崩溃、无丢命令；变量最终值 = 第 2 条的值；②100 条全部执行、按入队顺序、最终值 = 最后一条；**审计 = 命令级 100 条 + 动作级 100 条**；p95 ≤ 500ms | 集成 | 变量轨迹、ack、审计、指标 | P0 |
| C62 | C / M2 | **100 客户端同按按钮（各自不同元素）** | `MaxSessions` ≥100；100 个会话；页面有 100 个不同按钮（不同 `elementId`，各配 1 条写变量动作） | 100 个会话同一秒各按自己的按钮；记录执行顺序与两层审计 | 100 条全部执行（不同元素，不触发元素级 2/s）、执行顺序 == 入队顺序、无交错；命令级 100 行 + 动作级 100 行；每行 session/IP 与来源一致；无丢动作；p95 ≤ 500ms | 集成 | 执行序列、审计、指标 | P0 |
| C63 | C / M2 | **100 客户端同时确认同一报警** | `MaxSessions` ≥100；1 条 Active 未确认报警；100 个会话 | 100 个会话同一秒各发 `cmd.alarm.ack`（同一 `alarmId`） | 报警**只迁移一次**；恰好 1 条 `Success`，其余 99 条 `no-op/Skipped`（都不是错误）；100 条命令级审计（命令级不受元素限速约束——`cmd.alarm.ack` 没有 `elementId`）；不出现两次状态迁移、不崩、无重复推送 | 集成 | 报警状态、ack、审计 | P0 |
| C64 | C / M2 | **元素级限速的反向（不误伤）** | `ElementRatePerSecond=2`（同一 `elementId` **跨会话共享**）、`CommandRatePerSecond=5`（**按会话独立**）；一个已认证会话 | 1 秒内对 **3 个不同元素各发 2 条**（共 6 条，超过会话级 5/s） | 前 5 条执行、第 6 条因**会话级**限速被拒；**元素级不误伤**（每个元素只发了 2 条，都 ≤ 2/s；本用例单会话，正好验证"共享额度不等于 1/s"）；若把会话级临时调到 10，则 6 条全部执行——证明"元素限速写死成 1/s"这类过度修复会被抓住 | 集成 | ack、审计 | P1 |
| C65 | C / M2 | **`RequireAudit=false` 是非法配置** | `RequireAudit` 固定 true；手工把它写成 false | 启动服务；读日志与配置校验结果；尝试启用 `AllOperators` | **加载时即把值夹回 `true`**（不是只报错留 `false`），并写一条 **Error**（文案含"审计不可关闭"与配置键名）；`AllOperators` 被拒绝启用；不出现"关掉审计还能多人操作"；`HighestRoleOnly` 下也照样报这条 Error（非法配置不是策略问题）；**夹取发生在加载期、不是运行期** | 集成 | 启动日志、配置校验 | P0 |
| C66 | C / M2 | **监管全开 → `AllOperators` 可启用（正向配对）** | 五项监管全为 true；`OperationLeaseEnabled` 分别 false/true 各跑一轮；两个 Operator | 启动服务；两人各按一次；开租约时再跑一遍 acquire/request | 无"缺项"Error；`AllOperators` 生效、两人都能点；六条监管可逐条演示（串行/两层审计/在线列表/双层限速/二次确认/租约）；租约关时"都可点"、开时只有 holder 可点；证明闸门不是"一律拒绝" | 集成 + E2E | 启动日志、caps、ack、审计 | P1 |
| C67 | C / M2 | **审计前缀唯一拼装（不双前缀）** | 一个按钮钩子配 1 条写变量动作；已登录 | 点击一次；读命令级行与动作级行原文；全仓 grep `[web s=` | 命令级行 `说明` 列**逐字**等于 `[web s=<32hex> ip=<ip>] 变量：21 → 22`（前缀只出现 **1 次**）；动作级行**不带**前缀；桌面条目不带前缀；统计 `[web s=` 出现次数 == 命令级行数（不因 bridge 再拼一次而翻倍） | 集成 + 静态扫描 | CSV 原文、grep 计数 | P0 |

| C68 | C / M2a | **默认密码 gate 阻断写路径** | 全新 `ScadaUsers.json`；`admin` 仍是出厂密码；`AllowCommands=true`；Web 已启用 | ①`POST /api/ticket {admin,出厂密码}`；②用旧 ticket/令牌连 WS 发 `cmd.element`；③桌面端改密；④再取票 + 握手 + 发命令 | ①**403 `E_DEFAULT_PASSWORD`**（响应带 `gate:"DefaultPasswordNotChanged"`），**不签发 ticket、不做 PBKDF2**；②连接建立不了（无 ticket）→ 无命令可发；③改密成功；④取票 200 + `hello` + `caps` + 命令成功；**Web 无改密接口**（`/change-password` 404）；审计一条 `Web 会话`/`Denied`（`reason=default-password`，**不含密码**） | 集成 | HTTP 状态、响应体、审计 | P0 |
| C69 | C / M2a | **`/api/ticket` 每 IP 限速** | `TicketRatePerMinute=10`；同一来源 IP；另备第二台机器（不同 IP） | ①同一 IP 60s 内连打 11 次取票（匿名/登录/换票混合）；②第 12 次换用户名再试；③另一 IP 打 11 次；④等 60s 再打 | ①前 10 次正常（按凭据返回 200/401），**第 11 次 429 `E_RATE_LIMITED` + `retryAfterMs`**，不签发 ticket、不做 PBKDF2（耗时 < 5ms）；②换用户名**仍被限**（按 IP 合并计数）；③另一 IP 不受影响；④窗口过后恢复；每次拒绝有 `Web 会话`/`Denied`（`reason=rate-limited`） | 集成 | 状态码、时间线、审计 | P0 |
| C70 | C / M2a | **匿名令牌在 `AllowAnonymousView=false` 后不续期** | `AllowAnonymousView=true`；匿名观众已取票并拿到 `sessionToken`；配置改为 `false`（运行期/重启均可） | ①匿名连接继续收帧；②用匿名 `refreshToken` 换票；③断开后重连；④改用真实账号登录 | ①已有连接**不主动踢**（继续只读收帧）；②换票 **401 `E_SESSION_EXPIRED`**（"匿名只读观众已被配置关闭"），不签发新 ticket；③重连必须登录；④真实账号正常；审计 `Web 会话`/`Denied`（`reason=anonymous-disabled`） | 集成 | HTTP 状态、WS 帧、审计 | P0 |
| C71 | C / M2a | **会话/用户撤销** | A、B 两个已登录会话；同一账号另开第三个标签；桌面端可踢人/改密/降权 | ①撤销 A 的会话（踢人）；②A 用旧 `sessionToken` 换票；③撤销 B 所属**用户**（改密/降权/删号）；④B 与第三标签继续发命令；⑤未受影响的其它账号发命令 | ①A 的 WS 关闭（**4403**，已接线：`ScadaWebHost.cs:83-84` 关闭码、`:2243-2266` 撤销后关连接）+ 令牌立即失效；②换票 **401 `E_SESSION_REVOKED`**；③该用户**全部**令牌与未核销 ticket 作废；④两个会话都 **`E_SESSION_REVOKED`**、不执行、审计 Denied；⑤正常执行；撤销/被踢各一条 `Web 登出`/`Success`（`reason=revoked`，用户级带 `scope=user`） | 集成 + E2E | 关闭码、ack、审计 | P0 |
| C72 | C / M2a | **会话表上限（`MaxSessionTokens`）** | 断言宿主把 `MaxSessionTokens` 调到 4（**产品默认 4096 不改**）；4 个已登录会话 | ①第 5 个新会话取票/核销；②关闭一个会话后重试；③把 4 枚令牌的过期时间快进过 8h 后再试；④检查表内条目数 | ①第 5 个在 **`/api/ticket` 预检**被拒 **503 `E_CAPACITY`**（"会话令牌表已满"，`ScadaWebHost.cs:1068-1085`）+ 审计 `取票`/`会话表容量`/`拒绝`；**WS 握手仍 401**（Watson `AuthResult` 无 503 语义，拒绝日志与审计带 `code=E_CAPACITY`，`:772-781`）；**不挤掉在线会话、不排队**；②空出后成功；③过期条目被清理、可再建；④`SessionCount ≤ MaxSessionTokens`，不随重连线性增长；活跃 WS 连接超 `MaxSessions` 仍是 `AtCapacity`/1013（两张表分开） | 集成 | 会话表计数、状态码、审计 | P0 |
| C73 | C / M2a | **命令泵单条异常隔离** | 断言宿主注册一条"必抛异常"的测试命令（或注入让 handler 抛异常的图元）；随后正常命令 | ①发异常命令；②立刻发 3 条正常写值命令；③再发 100 条混合命令 | ①回 `error{E_INTERNAL}`（**无堆栈**）+ 命令级审计 `Failed`（detail 只记异常类型）+ 一条 Error 日志；②3 条**照常执行且顺序不变**（泵未退出）；③100 条全部执行、无丢失、无重试；进程不崩 | 集成 | ack、审计、日志、执行序列 | P0 |
| C74 | C / M2a | **`E_BUSY` 两级背压** | 断言宿主把全局容量按 1024 跑（产品值）；消费者故意变慢（handler 延迟）；多会话并发 | ①单会话连发 300 条（每会话 32）；②10 个会话合计灌满全局 1024；③停止灌入等消费者排空；④用被拒的同一 `requestId` 重试 | ①前 32 条接受、其余 `E_BUSY` + `retryAfterMs`；②全局满后同样 `E_BUSY`；**`TryWrite` 返回 true 却未入队 = 失败**（禁用 DropWrite/DropOldest）；③排空后全部执行、顺序 == 入队顺序、无静默丢；④重试成功且**不重复执行**（依赖幂等，M2a-4 已关闭）；`E_BUSY` 各写一条命令级 `Denied(reason=busy)` | 集成 | 计数、ack、执行序列 | P0 |
| C75 | C / M2a | **三闸顺序与必填** | 当前 `runGeneration=8`、页 B（`pageVersion=12`）；客户端持有旧值 | ①带 `runGeneration=7` + 旧页 + 旧版本；②带当前轮次 + 页 A + 旧版本；③带当前轮次 + 页 B + `pageVersion=11`；④**省略** `runGeneration`/`pageId`/`pageVersion` 各发一条；⑤三闸全对 | ①`E_STALE_RUN`（**只报第一道**，不报页错误）；②`E_STALE_PAGE`；③`E_STALE_PAGE`；④**全部 `E_BAD_REQUEST`**（不许"缺省=跳过"）；⑤进入权限层/执行；每条拒绝的命令级审计 detail 带 `expected/actual`；**三闸必填已实现（`ScadaWebCommandRouter.cs:546-590`）；C75 待执行** | 集成 | ack、审计、执行计数 | P0 |
| C76 | C / M2a | **`caps` 逐图元下发与重算** | 页面含六个交互图元（不同 `RequiredRole`）+ 若干只读图元；`AllowCommands=true`；`ClickPolicy=HighestRoleOnly`；两个账号 | ①Operator 登录看 `caps`；②Engineer 登录；③Engineer 登出；④把某图元 `RequiredRole` 改成 Administrator；⑤切页；⑥`AllowCommands` 改 false | ①帧形 `{type:"caps", caps:{policyVersion, runGeneration, pageId, pageVersion, default, defaultReason, deny:[[elementId,reason]], allow:[[elementId]]}}`（第 0.3 节口径）；`deny`/`allow` 覆盖**全部图元**（含基座页），**不是只列六个交互图元**；Operator 非最高档 → 该图元进 `deny` + 逐字原因；②Engineer 上线后该图元从 `deny` 移到 `allow`，`policyVersion` 随之变化（as-built 取 `Environment.TickCount64`，**不是 `+1` 递增**）；③回落 `deny`；④该图元变 `deny`（其余不变）；⑤新页重新下发；⑥**全部进 `deny` + 逐字"当前为只读模式（ScadaWeb.AllowCommands=false）"**；`caps` 每 **2 秒**广播一次（同策略下 `policyVersion` 也会随拍变化，客户端按 `deny`/`allow` 覆盖旧值） | 集成 + E2E | caps 帧序列、policyVersion | P0 |
| C77 | C / M2a | **`AllOperatorsRequiresLease` 非法组合与租约加强** | `ClickPolicy=AllOperators`；五项监管全开；两个 Operator | ①`AllOperatorsRequiresLease=true` + `OperationLeaseEnabled=false` 启动；②改成两者都 true 启动；③A 不持租约发命令；④A acquire 后发命令 | ①**加载即 Error 并把本项夹回 `false`**（文案含配置键名）；②正常启动、**不报"缺项"**（它不参与五项启用闸门）；③`E_LEASE_HELD`；④成功；证明"可选加强"与"启用闸门"是两回事。**归属：租约全链路归 M2b；M2a 只做配置校验/回退（①② 可执行，③④ 待 M2b）** | 集成 | 启动日志、ack、审计 | P1 |
| C78 | C / M2a | **六图元只读/禁用/失败反馈** | 页面含 Button/BitButton/TextField/ComboBox/SelectorSwitch/AckButton；分别制造 `canOperate=false`、服务端拒绝（`E_VALUE`/`E_POLICY`/`E_NOT_OPERABLE`） | ①逐图元在 `canOperate=false` 时点击/输入；②服务端拒绝后看控件状态；③TextField 输入非法值；④BitButton 瞬动模式被拒 | ①**置灰但保留布局**、`aria-disabled=true`、tooltip 显示 `reason`、**不发任何命令**（原始 WS 计数 0）；②toast 显示 `message` + 稳定 `code`，控件回到命令前状态（**无乐观更新**）；③**保留编辑态与用户输入** + 显示原因；④视觉按下态复位（不残留"一直按着"）；六图元全部覆盖，无静默失败 | E2E + 集成 | 截图、ack、命令计数 | P0 |
| C79 | C / M2a | **`AllowCommands=false` 总闸（M2a 默认）** | M2a 构建；`AllowCommands` 用默认值 `false`；管理员已登录 | ①发四条命令各一次；②看 `hello.readOnly`、`caps`、在线列表、值帧；③运行期把 `AllowCommands` 改 `true`；④再发一条命令 | ①四条全部 `E_POLICY`（逐字 `当前为只读模式（ScadaWeb.AllowCommands=false）`），不执行、命令级 Denied；②`hello.readOnly=true`、`caps` 全 false、`presence`/`frame` 照常；③`caps` 重算 + `policyVersion+1` + `toast`，**不重启**；④成功执行；证明"默认只读"不是靠前端隐藏 | 集成 + E2E | ack、caps、审计、截图 | P0 |
| C80 | C / M2a | **动作副本不改原模型** | 一个按钮钩子配 3 条动作：`Navigate`(页 B) + 写变量 + Log；`RequireConfirmation=true`；桌面当前页 A | ①Web 点按钮；②检查原 hook；③桌面点同一按钮；④再读 `.vms` 文件 | ①写变量与 Log 执行、`Navigate` 跳过（审计 `Skipped/mirror-mode`）、ack `skipped=1`、桌面页仍是 A、**不弹桌面模态框**；②原 `hook.Actions` **仍含 `Navigate`、条数不变、`RequireConfirmation` 仍为 true**；③桌面仍切页 + 弹确认（桌面行为未变）；④`.vms` 里钩子配置未被 web 改动 | 集成 + E2E | ack、审计、桌面页、.vms 快照 | P0 |

**C 阶段小计：80 条。**

**C 阶段覆盖对账（M2a，2026-10-21）**：逐项核对"方案 §8/§9/§10.4/§11 要求的 M2a 行为"与 C01–C80。**C01–C67 未覆盖 13 项，已全部补为 C68–C80；其余 8 项由既有用例覆盖，不重复新增**（避免虚增用例）。

| 要求 | 覆盖用例 | 来源 |
| --- | --- | --- |
| 默认密码 gate | C68 | **新增**（与 F06/B05 同源，C 阶段门槛） |
| Origin 同源 | C42 | 既有 |
| 每 IP 取票限速（10/分钟） | C69 | **新增** |
| 匿名令牌在 `AllowAnonymousView=false` 后不续期 | C70 | **新增** |
| 会话/用户撤销 | C71（撤销表本身）+ C36（踢人/降权/改密的效果） | **新增** + 既有 |
| 会话表上限 | C72 | **新增** |
| 命令泵单条异常隔离 | C73 | **新增** |
| `E_BUSY` 两级背压 | C74（全局 1024）+ C35（会话 32） | **新增** + 既有 |
| `pageId/pageVersion/runGeneration` 三闸 | C75（顺序 + 必填）+ C32/C39/C40（单闸） | **新增** + 既有 |
| `caps` 逐图元 `CanOperate/OperateReason` | C76 + C24/C48/C55（策略/TOCTOU） | **新增** + 既有 |
| `HighestRoleOnly` 平局 / 无人登录 | C24（平局）、C48（全员登出） | 既有 |
| `AllOperators` 五项闸门缺项拒绝 | C44（四项）、C65（`RequireAudit`）、C66（全开正向） | 既有 |
| 在线列表不暴露 `SessionId` | C25（字段白名单）、C50、F04 | 既有（C25 判据已改） |
| `confirmToken` 30s/单次/跨会话/跨元素 | C46 | 既有 |
| `Navigate` 跳过审计 | C54 | 既有 |
| 六图元只读/禁用/失败反馈 | C78 + C22（码表口径） | **新增** + 既有 |
| 审计 actor/session/IP | C19/C20/C43/C49/C67 | 既有 |
| 动作级 + 命令级两层计数 | C01/C41/C49 | 既有 |
| `AllowCommands=false` 总闸 | C79 | **新增** |
| 动作副本不改原模型 | C80 | **新增** |
| `AllOperatorsRequiresLease`（可选加强） | C77 | **新增** |

> **不新增的两条**：①"身份字段伪造"由 C50 覆盖（M2a 口径改为"忽略客户端字段、按真实身份判权"，不产生 `E_IDENTITY`）；②"运动命令默认禁用"由 C51 覆盖（M2a 不打开 `AllowMotionCommands`）。**C68–C80 一条都没执行**，任何"通过"都不成立。

---

## 4. D 阶段：图元覆盖、派生量、渲染与输入（M1/M2/M3 分摊）

> 覆盖度对账：`BuiltInElements.All` 当前 **28 条**（其中 `Hmi.AxisPointTable` 为 `ShowInToolbox=false` 的兼容别名），`ChartElements.All` **1 条**（`Hmi.ChartTrend`），注册表合计 **29 条**；工具箱可见 **28 条**。本阶段按 **29 条注册项全测**，不把隐藏别名漏掉。
>
> **阶段归属（M0.1 复评裁定）**：M1 的 10 个图元对应的派生量/视觉用例 **D04-D09、D21 归 M1**（M1 门槛，另由 B33/B34 聚合守门）；**D04–D09 是 B33（派生量）/B34（视觉）的分解证据**——逐图元的失败定位靠它们，但门槛判定以 B33/B34 聚合为准；`Hmi.BitButton`/`Hmi.SelectorSwitch` 的派生量与坏值（D10/D11）归 **M2**；`Hmi.Pipe`/`Hmi.Pump`/`Hmi.Motor`（D12/D13）与 29 条视觉基线（D25）留在 **M3**；`Hmi.ImageView` 归 **M1.5**（D18 是 M3 回归）。**M1 不许以"这些是 M3 用例"为由跳过 Tank/Gauge 画错。**

| ID | 阶段 | 场景 | 前置条件 | 步骤 | 预期结果 | 自动化方式 | 证据 | 优先级 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| D01 | D / M3 | 29 条注册/28 条可见对账 | 已注册内置图元与曲线图元 | 枚举 `ElementRegistry.All`；统计 `ShowInToolbox`；检查 TypeKey 唯一；检查 `BuiltInErrors` | 注册 29 条；可见 28 条；隐藏 1 条且为 `Hmi.AxisPointTable`；TypeKey 无重复；`BuiltInErrors` 为空；`Hmi.ChartTrend` 只注册一次 | 单元 | 注册表清单 | P0 |
| D02 | D / M3 | 29 条逐一序列化/重载 | 29 条 TypeKey 清单 | 每条创建元素与控件；序列化 `.vms`；重载；生成快照；比对默认值 | 29/29 无异常；TypeKey 原样保留；属性默认值与描述符一致；控件可实例化；快照含该图元；无未知类型降级 | 集成 | 29 条结果表 | P0 |
| D03 | D / M3 | 未知 TypeKey 占位 | 页面含 `Hmi.Unknown` 与 5 个正常图元 | 加载；观察渲染与保存 | 未知图元显示占位（类型键/“未知图元”），其余 5 个正常渲染；不崩溃；警告只记一次；保存后 TypeKey 不被抹掉 | 集成 + E2E | 截图、日志、.vms | P0 |
| D04 | D / M1（门槛，**B33/B34 分解证据**） | 派生量：指示灯 | Indicator 绑 bool；输入 false/true | 分别加载；读取颜色/透明度/文字 | 两种状态的填充色、透明度、标签与 WPF 控件同输入下的 DP/画刷值一致；像素差 ≤ 0.1% | 集成 + E2E | DP 对照表、截图 | P0 |
| D05 | D / M1（门槛，**B33/B34 分解证据**） | 派生量：棒图 | ProgressBar min=0 max=200；输入 0/50/100/200/250 | 读取填充比例、显示文本、夹紧 | 填充比例 0/25/50/100/100%；文本按格式；超上限夹紧；负值夹紧到 0；与 WPF 同公式 | 集成 + E2E | 比例、截图 | P0 |
| D06 | D / M1（门槛，**B33/B34 分解证据**） | 派生量：表盘 | Gauge min=0 max=100；输入 0/50/100/-10/110 | 读取指针角度、读数、夹紧 | 指针角度与 WPF 映射逐值相等（0=最小角、50=中间、100=最大角）；越界夹紧；读数格式一致 | 集成 + E2E | 角度、截图 | P0 |
| D07 | D / M1（门槛，**B33/B34 分解证据**） | 派生量：阀门 | Valve 开度 0/50/100 | 读取阀杆角度/颜色/位号 | 0=全关、50=中间、100=全开；角度与 WPF 同公式；颜色/位号一致；越界夹紧 | 集成 + E2E | 截图、属性 | P0 |
| D08 | D / M1（门槛，**B33/B34 分解证据**） | 派生量：罐体 | Tank 液位 0/50/100；容量 1000L | 读取填充高度与读数 | 填充 0/50/100%；读数 0/500/1000；液位越界夹紧；与 WPF 同公式 | 集成 + E2E | 截图、读数 | P0 |
| D09 | D / M1（门槛，**B33/B34 分解证据**） | 派生量：多态灯 | Lamp 状态 0/1/2/3/99 | 读取四态颜色与标签 | 0..3 对应停机/运行/警告/报警四色；99 按停机收敛；不抛异常；颜色与 WPF 一致 | 集成 + E2E | 颜色、截图 | P0 |
| D10 | D / M2 | 派生量：位按钮 | BitButton 值 false/true；五种模式 | 读取两态文字、填充、边框 | 值与模式对应的文字/颜色正确；未知模式回退安全默认；与 WPF 一致；点击后状态翻转一次 | 集成 + E2E | 截图、变量 | P0 |
| D11 | D / M2 | 派生量：多档旋钮 + 坏值 | SelectorSwitch 4 档；索引 0/1/2/3/9；另注入非法枚举名与越界值 | 读取高亮档、文字、状态 | 0..3 只有对应档高亮；9 无高亮并显示无效状态（**B35 已把它移出 M1，旋钮坏值在这里验**）；与 WPF 一致；点击写入正确索引 | 集成 + E2E | 截图、变量 | P0 |
| D12 | D / M3 | 派生量：管道 | Pipe 状态 0/1/2/99；流向正向/反向/无 | 读取箭头颜色与方向 | 0=停流灰、1=流动绿、2=异常红；99 按停流收敛；方向与配置一致；与 WPF 一致 | 集成 + E2E | 截图、颜色 | P0 |
| D13 | D / M3 | 派生量：泵/电机 | Pump、Motor 状态 0/1/2/99 | 读取叶轮/M 颜色、泵壳/机身色、位号 | 0=灰、1=绿、2=红；99 按停机收敛；两种图元各自颜色属性生效；与 WPF 一致 | 集成 + E2E | 截图、颜色 | P0 |
| D14 | D / M3 | 报警条 | 5 条不同严重度报警；其中 2 条未确认 | 加载；确认一条；恢复一条；清空 | 按严重度降序；仅严重且未确认闪烁；表头未确认/总数正确；空态显示“系统正常”；确认/恢复后 2s 内更新 | 集成 + E2E | 截图、报警状态 | P0 |
| D15 | D / M3 | 轴面板/轴控台 | 假轴源 3 根；单轴页与多轴页各一 | 切换轴；使能/点动/回零/停止/清报警；点位示教/定位/改速度；注入失败 | 下拉顺序正确；单轴时下拉收起；状态字段与源一致；命令落到选中轴；确认框按配置出现；失败原因显示在图元上；无“看 X 动 Y” | 集成 + E2E | 截图、命令记录、ack | P0 |
| D16 | D / M3 | 数据表格 | 12 列配置；采行信号；AckVariable；MaxRows=10 | 产生 12 次上升沿；检查行数/顺序/格式/确认计数 | 最多 12 列；宽度 0 吃剩余；采行只在上升沿；只保留最近 10 行；`AckVariable` 12 次采样累加 +12（即使只保留 10 行）；新行在上不抢滚动；格式用不变区域性 | 集成 + E2E | 表格截图、变量、行数 | P0 |
| D17 | D / M3 | 趋势曲线 | 绑 double 变量；时间窗 60s；固定范围 | 以真实到达时刻喂 1000 点；切换范围；空数据 | X 轴用真实时间戳而非采样率外推；时间窗滚动；固定范围生效；空态有提示；不依赖库 `AutoScroll`；数据缓冲有上限 | 集成 + E2E | 截图、缓冲计数 | P0 |
| D18 | D / **M3**（M1.5 回归） | 图像显示 | ImageView 绑图像变量；假图源 5fps | 未绑加载；绑定；切页；恢复 | 未绑显示“无图像”；绑定后刷新；宽高比保持；切页释放旧图；无破图、无内存增长。**ImageView 首次交付在 M1.5（B32），本用例是 M3 回归** | 集成 + E2E | 截图、内存 | P1 |
| D19 | D / M3 | 底板容器 | Panel 含 3 个子图元；另一个 Panel 尝试嵌套 | 加载；拖动底板；把子图元移出边界；尝试嵌套 | 子图元裁剪在底板内；拖动底板子图元整体位移；嵌套被拒绝并提示；坐标仍为页面绝对坐标；无双重位移 | 集成 + E2E | 截图、坐标 | P1 |
| D20 | D / M3 | z 序/图层/基座 | 页面含多图层、基座、重叠图元 | 加载；切换图层可见性；检查叠放 | 渲染顺序与 WPF 一致；基座在底层；隐藏图层不显示但仍在快照；点击命中最高可见层；无重复图元 | 集成 + E2E | 截图、快照 | P0 |
| D21 | D / M1（门槛） | 文本/字体/中文（**B34 的分解证据**） | 中文、长文本、数字、单位、不同字号 | 加载；缩放；检查换行/省略/对齐 | 中文无豆腐块；长文本按规则换行/省略；对齐与字号正确；不溢出图元边界；与 WPF 视觉差 ≤ 0.1%。**M1 门槛（P0）**：B34 按块 diff，本用例专门压中文/字号/换行——两条都要过 | E2E | 截图 | P0 |
| D22 | D / M3 | 图元动画 | 四种动画类型各一个 | 启动/停止动画；切页；重复进入 | 动画按配置播放；切页停止；重复进入不叠加定时器；无内存/句柄增长；减少动态效果设置可关闭（若实现） | 集成 + E2E | 截图、定时器计数 | P2 |
| D23 | D / M3 | 命中测试 | 重叠图元、透明区域、禁用命中图元、触摸目标 | 点击各区域；记录命中对象 | 命中最高可见且可命中图元；透明区域不误命中；禁用命中图元穿透；触摸目标 ≥ 24px；无“点空白选中背后对象” | E2E + 手工 | 命中日志、截图 | P1 |
| D24 | D / M3 | 未知属性 | 快照含客户端不认识的属性 | 加载；保存；再加载 | 客户端忽略未知属性不崩溃；保存不丢未知属性；不把它当动作执行；日志可诊断一次 | 集成 | 快照、.vms | P2 |
| D25 | D / M3 | 29 条视觉基线 | 29 条图元页；100% DPI；固定主题 | 逐页截图；与 WPF 基准/已批准基线做像素 diff | 每条都有基线；像素差 ≤ 0.1%；无布局错位；无字体替换；差异图存证 | E2E + 手工 | 截图、diff | P1 |
| D26 | D / M3 | 触摸 | 触摸屏/移动 context | 单击、双击、长按、拖动、滚动 | 单击即触发（无 300ms 延迟）；双击不误触发两次动作；长按按配置；拖动不与滚动冲突；多点触控不产生重复命令 | 手工 + E2E | 录屏、命令计数 | P0 |
| D27 | D / M3 | 键盘 | 桌面浏览器 | Tab 遍历；Enter/Space 触发；Esc 取消输入；方向键切换档位 | 焦点顺序合理；Enter/Space 只触发一次；Esc 放弃输入不写入；方向键按预期；焦点可见；无焦点陷阱 | E2E | 截图、变量、命令计数 | P1 |
| D28 | D / M3 | 中文 IME | 真实中文输入法 | 在 TextField/IOField 输入拼音并选词；取消组合；提交 | 组合期间不写入、不显示拼音串；选词后一次提交；取消不写入；光标/选区正常；无重复字符 | 手工 | 录屏、变量 | P0 |
| D29 | D / M3 | 高 DPI/缩放 | 100/125/150/200% | 逐档加载 29 条页与 1000 图元页 | 布局不重叠、不裁切；文字可读；触摸目标随 DPI 合理放大；画布不模糊；无横向滚动条（除设计需要） | E2E + 手工 | 截图 | P1 |
| D30 | D / M3 | 响应式布局 | 1920×1080、1366×768、1024×768 | 逐分辨率加载关键页 | 关键控件不重叠；可滚动到全部内容；字号不小于 12；轴面板/表格不溢出；无遮挡 | E2E | 截图 | P1 |
| D31 | D / M3 | 图像编码/下采样/带宽 | 1080p 与 4K 假图源；1080p 5fps、4K 1fps | 测量帧大小、编码耗时、客户端带宽、过期帧 | 1080p ≤ 300KB/帧；4K ≤ 800KB/帧；下采样到图元显示尺寸；1080p ≤ 5fps、4K ≤ 1fps；单客户端平均 ≤ 2Mbps；过期帧丢弃不排队；UI 不卡顿 | 集成 + E2E | 帧大小、带宽、trace | P0 |
| D32 | D / M3 | 曲线 1 万点 | ChartTrend；1 万点；时间窗 5 分钟 | 喂点；测量渲染帧时间与内存；切换页面 | 有降采样/抽稀；单帧渲染 < 30ms；内存有上限；切页释放；不因点数增加而线性卡死 | 集成 + E2E | 帧时间、内存 | P1 |

**D 阶段小计：32 条。**

---

## 5. E 阶段：暴力测试、长稳与资源泄漏

| ID | 阶段 | 场景 | 前置条件 | 步骤 | 预期结果 | 自动化方式 | 证据 | 优先级 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| E01 | E / 暴力 | 10 客户端在线 10 分钟 | 10 个原始 WS 客户端；200 绑定；10Hz | 同时加载；持续 10 分钟；每分钟采样延迟、CPU、内存 | 10/10 保持 `Loaded`；p95 < 150ms；无断线；内存增长 ≤ 20MB；无错误日志 | 集成 | 指标 CSV、日志 | P0 |
| E02 | E / 暴力 | 50 客户端在线 | **必须显式把 `MaxSessions` 设到 ≥50**（默认 16 会直接 `AtCapacity`，那样测的不是 50 客户端）；50 个客户端；10Hz；10 分钟 | 同时加载；采样连接成功率、p95、CPU | 50/50 成功 `Loaded`（不允许出现 `AtCapacity`）；p95 ≤ 300ms；CPU < 80%；无 OOM；指标 CSV 里记录 `MaxSessions` 实际值 | 集成 | 指标、进程计数器、配置快照 | P0 |
| E03 | E / 暴力 | 100 客户端/容量边界（三档） | ①默认 `MaxSessions=16`；②显式 50；③显式 100 | 每档都用 100 个客户端同时连接；观察接纳/拒绝 | ①16 个 `Loaded`、84 个显式 `AtCapacity`（close 1013），已有会话不被驱逐；②50 个 `Loaded`、50 个 `AtCapacity`；③≥95% `Loaded`、p95 < 500ms；三档都不得崩溃、不得静默丢连接、不得驱逐已有会话 | 集成 | 会话表、指标、配置快照 | P0 |
| E04 | E / 暴力 | 1000 图元快照/增量 | 1000 图元页；1 个客户端 | 加载；测快照字节、gzip、首屏；改 10 个值测增量 | 快照 ≤ 2MiB、gzip ≤ 300KB；首屏 ≤ 3s；增量 ≤ 50KB/帧；版本单调；无超时 | 集成 | 字节数、trace | P0 |
| E05 | E / 暴力 | 1000 图元 × 10 客户端 30 分钟 | 10 客户端；1000 图元；10Hz | 持续 30 分钟；采样内存、队列深度、CPU | 内存增长 ≤ 30MB；每会话队列深度 < 50；CPU < 80%；无丢最后一帧；无断线 | 集成 | 指标 CSV | P0 |
| E06 | E / 暴力 | 4K 大图 | 3840×2160 假图源；源 3fps；客户端预算 2Mbps | 推 5 分钟；测帧大小、编码耗时、实际帧率、客户端内存 | 每帧 ≤ 800KB；带宽超预算时自适应到 ≤ 1fps 或降质；编码不阻塞 UI > 1s；客户端内存增长 ≤ 30MB；无解码失败；不排队 | 集成 + E2E | 帧大小、trace、实际帧率 | P1 |
| E07 | E / 暴力 | 图像突发 | 图源 20fps；3 个客户端；持续 60s | 推图；统计发送/丢弃/排队 | 每客户端只保留最新帧；队列有界；丢弃计数可见；无 OOM；UI 不卡；最终显示最后一帧 | 集成 | 丢弃计数、截图 | P1 |
| E08 | E / 暴力 | 断线重连风暴 | 50 客户端；每个 20 次断开/重连；60s | 循环断网；统计会话、句柄、端口、恢复时间 | 无会话泄漏；无端口耗尽；每次恢复 ≤ 2s；句柄回到基线 ±20；无重复会话 | 集成 | 会话表、句柄、netstat | P0 |
| E09 | E / 暴力 | 24 小时长稳 | 10 客户端；10Hz；1000 图元；流程/报警持续 | 连续跑 24h；每小时采样**私有内存**、RSS、句柄、线程、GC、队列；结束时抓 dump | **私有内存增长 ≤ 100MB（预热后）**；**RSS 仅记录、不设门槛**；句柄 ±20；线程 ±4；Gen2 ≤ 10/min；无未处理异常；无持续增长队列；审计/报警文件按天滚动 | 集成 + 手工 | 24h 指标 CSV、dump 摘要 | P0 |
| E10 | E / 暴力 | 订阅泄漏 | 10 客户端；页面可反复切换 | 100 次切页；每次统计订阅数、事件处理器数、内存 | 订阅数回到基线；无重复处理器；内存增长 ≤ 20MB；无“上一页还在收值” | 集成 | 订阅计数、内存 | P0 |
| E11 | E / 暴力 | 会话泄漏 | 1000 次连接/断开 | 循环登录/加载/关闭；统计 `WebSession`、socket、定时器 | 结束后 `WebSession=0`、socket=0、定时器=0；句柄回到基线；无幽灵在线用户 | 集成 | 会话表、句柄 | P0 |
| E12 | E / 暴力 | 审计文件增长 | 模拟 10 万条动作/天；保留期 90 天 | 生成动作；检查按天文件、单文件大小、清理 | 每天一个文件；单文件不超过 50MB；超过保留期的文件被清理；不误删非本类文件；无丢行 | 集成 | 文件列表、行数 | P1 |
| E13 | E / 暴力 | 报警历史增长 | 1 万条报警；按天滚动 | 生成报警/恢复/确认；检查文件与内存 | 文件按天滚动；保留期生效；内存不随历史线性增长；无丢事件 | 集成 | 文件、内存 | P1 |
| E14 | E / 暴力 | 退出链 ≤ 5s | 10 客户端在线；流程运行；报警活跃 | 触发退出；记录关闭顺序与耗时 | 停止接收新命令；关闭 WS（1001）；释放会话/端口；流程停止；进程 ≤ 5s 退出；无异常弹窗 | 集成 | 关闭日志、耗时、netstat | P0 |
| E15 | E / 暴力 | 端口反复绑定 | 固定端口 | 启动/停止 20 次；每次测绑定耗时 | 20/20 绑定成功；每次 < 2s；无 TIME_WAIT 导致的持续失败；句柄不增长 | 集成 | 绑定日志、netstat | P1 |
| E16 | E / 暴力 | 进程被强杀 | 10 客户端在线；有审计写入 | `taskkill /F`；重启；检查文件与会话 | 客户端 2s 内 `Disconnected`；重启后无损坏审计/账号/方案；临时文件清理；无半行 CSV | 集成 + 手工 | 文件校验、日志 | P1 |
| E17 | E / 暴力 | 网络分区 30s | 客户端与服务端之间阻断 | 阻断 30s；恢复；检查命令与版本 | 客户端 `Disconnected`；恢复后 ≤ 2s `Loaded`；断线期间命令不执行；重连不重复执行；版本连续 | 集成 | 网络日志、ack | P0 |
| E18 | E / 暴力 | 慢客户端隔离 | 一个客户端限速 1KB/s；另两个正常 | 持续 10Hz 推送 60s；观察三个客户端 | 慢客户端队列有界，超过阈值被断开；正常客户端 p95 不受影响；服务端内存不增长；断开有原因 | 集成 | 带宽、队列、指标 | P1 |
| E19 | E / 暴力 | CPU 饥饿 | 宿主 CPU 被占满 10s | 期间发命令/心跳；恢复后观察 | 无死锁；命令排队或显式 `E_BUSY`；心跳恢复；UI 停顿 < 3s；恢复后状态一致 | 集成 + 手工 | 时间线、日志 | P1 |
| E20 | E / 暴力 | 磁盘满 | 审计/报警目录所在盘写满 | 执行动作；恢复空间；再执行 | 操作仍成功；审计/报警写失败只 Warn 一次；不崩溃；恢复后继续写入；无半行文件 | 集成 | 日志、ack、文件 | P1 |
| E21 | E / 暴力 | 系统时钟跳变 | 运行中把时钟 +1h 再 -1h | 观察租约、超时、审计时间、报警延时 | 超时/租约用单调时钟，不因跳变提前失效；审计用墙钟但顺序可解释；报警延时不被跳过；无负时长 | 集成 | 时间线、审计 | P2 |
| E22 | E / 暴力 | 10Hz 抖动/乱序 | 假值源 10Hz，抖动 0..200ms，偶尔乱序到达 | 持续 5 分钟；检查时间戳、顺序、最终值 | 时间戳单调；乱序帧按到达序处理或丢弃并计数；最后一帧等于源最新值；p95 达标；无回退 | 集成 | 帧序列、指标 | P1 |
| E23 | E / 暴力 | 极端组合 | 1000 图元；50 客户端；10Hz；图像 | 同时施加；持续 10 分钟 | 优雅降级：显式 `E_BUSY`/`AtCapacity`；不崩溃；内存/句柄不失控；正常客户端仍可用；日志有容量原因 | 集成 + 手工 | 指标、日志 | P0 |
| E24 | E / 暴力 | 泄漏取证 | 完成 E08/E10/E11 后 | 抓 `dotnet-dump`/`gcdump`；查根引用 | 无 `WebSession`/`PageSnapshot`/`ScadaRuntime` 被静态事件或定时器意外根住；泄漏对象数 0；附 dump 摘要 | 集成 | dump 摘要 | P1 |

| E25 | E / 暴力 | 默认容量 16 会话 × 2 小时长稳 | **默认配置**（`MaxSessions=16`、入站 32、`FrameRateHz=10`、`FullRateSessionLimit=4`）；16 会话；1000 图元 | 持续 2 小时；每分钟采样 p95、内存、CPU、队列深度 | 16/16 保持 `Loaded`；p95 ≤ 150ms；内存增长 ≤ 50MB；CPU ≤ 单核 40%；队列深度不持续增长；超出 `FullRateSessionLimit` 的会话自动降到 2Hz 且仍保持镜像（画面页 ID 与主会话一致） | 集成 | 指标 CSV、配置快照 | P0 |
| E26 | E / 暴力 | 队列深度 → 排队延迟对照（16 / 32 / 64） | 三档 `SessionInboundQueueCapacity` = **16 / 32 / 64**（合法范围 16–64）；消费者 1ms/条 | 每档灌满队列；测 p50/p95 排队延迟；另把配置写成 256 检查校验行为 | 16/32/64 三档各自记录 p50/p95（32 档 p95 < 100ms）；三档都不得丢命令；配置写 **256 → 按边界夹取到 64 并 Warn**（或拒绝该值，二选一并写日志），**不许静默接受**；**不再设"256 必须复现 ≥250ms"这类条件式**（256 已不是合法档位） | 集成 | 延迟 CSV、配置日志 | P1 |
| E27 | E / 暴力 | 出站队列与慢消费者隔离 | 一个客户端限速 1KB/s；另两个正常；10Hz 推送 | 持续 60s；观察降频、跳过、断开 | 出站 >80% 时跳过本帧并给该会话 `toast{level:Warn}`；连续 3 次降频 → 2Hz，队列回落到 25% 以下恢复；队列满 → `bye{closeCode:1011}` + 审计 `session.evicted`；正常客户端 p95 不受影响；服务端内存不增长 | 集成 | 带宽、队列深度、指标、审计 | P1 |
| E28 | E / 暴力 | 100 客户端 + 1000 图元 2 小时长稳（只读） | `MaxSessions` 显式设 ≥100；100 客户端；1000 图元；10Hz | 连续 2 小时；采样 RSS、句柄、线程、队列、p95 | ≥95% 保持 `Loaded`；内存增长 ≤ 50MB；句柄 ±20；线程 ±4；**p95 ≤ 500ms**（与 M8/E03/C59 统一）；无 OOM；审计文件按天滚动；无持续增长队列 | 集成 | 指标 CSV、dump 摘要 | P0 |
| E29 | E / 暴力 | **100 客户端读+写+图片令牌 2 小时长稳** | `MaxSessions` ≥100；100 客户端；1000 图元；10Hz；2 路 1080p 图像；`imageToken` 每 45s 轮换；监管全开 | 连续 2 小时：每客户端每分钟 12 条写命令（分散到不同元素）+ 每 45s 换 `imageToken` + 持续收图；采样 p95、CPU、内存、token 表条目数、审计行数 | ≥95% 保持 `Loaded`；**p95 ≤ 500ms**；写命令全部有 ack（`Success` 或显式 `E_RATE_LIMITED`），无静默丢；**`imageToken` 表条目数 ≤ 会话数 × 3**（每 `TTL/3` 轮换、旧 token 保留到自己的 `expiresAt`、同时最多 3 个；到期即清，不泄漏）；401/403 计数不随运行时长线性增长（没有"token 永远轮换不上"）；内存增长 ≤ 50MB；无 OOM；审计文件按天滚动 | 集成 | 指标 CSV、token 表计数、审计计数 | P0 |

**E 阶段小计：29 条。**

---

## 6. F 阶段：安全

| ID | 阶段 | 场景 | 前置条件 | 步骤 | 预期结果 | 自动化方式 | 证据 | 优先级 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| F01 | F / 安全 | 未登录静态资源 | 服务启动；无 ticket、无令牌 | 请求 `/`、`/app.js`、`/styles.css`、`/api/status`、常见备份路径 | 只返回基础页与公开静态资源；不返回快照/配置/账号/审计；`/api/status` 仅最小信息；目录列表关闭 | 集成 + E2E | HTTP 响应 | P0 |
| F02 | F / 安全 | 未登录 WebSocket | 无 ticket；另备伪造的 `Authorization: Bearer …` 与伪造 Cookie | 连接 `/ws`；带伪造 Authorization 头；带伪造 Cookie | 全部 **401 且不升级**（cookie 不参与鉴权，带了也没用）；业务 handler 不执行；无 `WebSession`；错误不泄露账号是否存在 | 集成 | WS 状态 | P0 |
| F03 | F / 安全 | 未登录访问（**WS-only，无 REST 管理接口**） | 无凭证 | 请求 `/api/status`、`/api/catalog`、`/api/ticket`；再请求加载/命令/审计/用户管理等 REST 路径；不带 ticket 连 WS；带 ticket 后发 `cmd.*` | `/api/status`、`/api/catalog` 只返回最小公开信息；`/api/ticket` 受 Origin + 限速（M1c）；**所有 REST 管理路径一律 404**；不带 ticket 的 WS **401 且 handler 不执行**；M1b 的 `cmd.*` 回 `E_UNSUPPORTED`（M2 才回 `E_UNAUTHENTICATED`）；404/401 响应无堆栈、无路径、无内部 ID | 集成 | HTTP 响应、WS 帧 | P0 |
| F04 | F / 安全 | 水平越权（**无 sessionId 可枚举**） | 用户 A 已登录；用户 B 有会话 | A 尝试读 B 的快照/审计/会话；用 presence 里的 `sessionRef` 当会话键去猜；伪造 B 的 `sessionId` 发命令 | **没有"按 sessionId 读别人快照/审计"的接口（一律 404）**；presence 只给不透明 `sessionRef`，据此拿不到他人会话；WS 连接只代表自己、命令身份由连接反查；伪造他人会话 → `E_IDENTITY`（或 404）+ 审计 Denied；不泄露 B 的用户名/IP | 集成 | 响应、审计 | P0 |
| F05 | F / 安全 | 垂直越权（**无管理接口**） | Operator 已登录 | 调用用户管理/改角色/踢人/改配置/读全量审计等 REST 路径；再用 WS 发越权命令 | **所有管理类 REST 路径一律 404**（Web 不提供用户管理/改角色/踢人/改配置）；WS 命令按角色拒绝（`E_ROLE`，原因逐字来自 `Allows`）；审计 Denied；错误文案不提示内部信息 | 集成 + E2E | 响应、审计 | P0 |
| F06 | F / 安全 | 默认密码绕过 | Web 启用；admin 仍是默认密码 | 直接取票 / 连 WS 发 `cmd.*`；伪造"已改密"标志 | **目标（U-13）**：取票 403 `gate=DefaultPasswordNotChanged`，登录一律失败；不能通过 API 参数/本地存储/重放绕过；桌面改密后正常。**M1c 现状：仍无 gate，出厂密码可取票**——本用例记为失败/阻塞 | 集成 | 响应、审计 | P0 |
| F07 | F / 安全 | 登录暴力破解 | 同一 user+IP；另备多个 IP | 连续失败；换 IP 继续；测响应时间 | 5 次/60s 锁定 60s；锁定期间正确密码也拒绝；按 user+IP 计数；失败响应时间差 < 50ms；审计保留 | 集成 | 时间线、审计 | P0 |
| F08 | F / 安全 | 改密（**Web 不提供，桌面端做**） | 已登录用户；桌面端可改密 | 请求 `/change-password` 与任何改密表单/API；在桌面端改密后再用旧会话发命令 | **Web 侧一律 404**（不存在改密接口，也不接受改密参数）；改密只在桌面端；桌面改密后旧 Web 会话失效（`E_UNAUTHENTICATED`，与 F24③ 同一判据）；审计记录旧会话失效；Web 不缓存/不回显任何密码 | 集成 + E2E | 响应、审计、会话表 | P0 |
| F09 | F / 安全 | WebSocket Origin | 配置精确白名单 | `evil.example`、`null`、`sub.allowed`、`allowed:8080` 变体、大小写变体 | 非精确匹配全部 403 且不升级；精确匹配 101；不允许通配符；不因 `Sec-WebSocket-Protocol` 绕过 | 集成 + E2E | WS 抓包 | P0 |
| F10 | F / 安全 | CSRF（无 cookie 形态） | 已登录；另开一个跨站页面（不同 origin） | 跨站页面尝试：①申请 ticket；②直接开 WS 带已知 ticket；③用 `fetch` 调 `/api/status` / `/api/ticket` | ①③被 Origin 校验拒（403 `E_ORIGIN` + 审计 Denied）；②握手 403；**浏览器没有可被自动携带的凭据**（无 cookie）；所有状态变更只能经 WS 消息、且必须已认证；不依赖"仅前端 token" | E2E + 集成 | 网络、响应头、审计 | P0 |
| F11 | F / 安全 | 凭据形态与存储（**无 cookie**） | 已登录；完成一次完整登录/刷新/重连/看图 | 检查全部 HTTP 响应头；检查 `document.cookie`/`localStorage`/`sessionStorage`；检查 URL | 所有响应**没有 `Set-Cookie`**；`document.cookie` 为空；`localStorage` 里没有任何令牌；`sessionStorage` 只有**会话令牌**（关标签即失效）；**URL 里只有一次性 ticket（`/ws?ticket=…`）与只读 `imageToken`（`/img/...?t=…`）**，**会话令牌从不出现在任何 URL**；`imageToken` 不写 `sessionStorage`（内存变量，轮换即覆盖）；登出后两者立即失效 | E2E + 集成 | 响应头、浏览器存储、URL 记录 | P0 |
| F12 | F / 安全 | 路径穿越 | 静态资源服务 | 请求 `../AppConfig.json`、`%2e%2e%2f`、`..\`、双重编码、绝对路径 | 全部 400/404；不返回 Web 根之外任何文件；日志记录尝试；不泄露真实路径 | 集成 | HTTP 响应、日志 | P0 |
| F13 | F / 安全 | 静态资源缓存旧版本 | 升级前后两个版本 | 旧页面缓存加载新 JS；新页面加载旧 JS；检查 `Cache-Control`/hash | `index.html` 不缓存或 `no-store`；带 hash 资源长缓存；版本不匹配时强制刷新；不得出现旧 JS 操作新协议 | E2E | HAR、响应头 | P0 |
| F14 | F / 安全 | 静态文件缺失 | 删除 `Web\wwwroot\app.js`（M1b 是磁盘 Content，不是内嵌资源） | 启动；请求该资源；打包 | 返回 404 且有明确日志；不 500、不泄露堆栈；启动自检 Warning；打包检查报缺口 | 集成 + E2E | 响应、日志、pack 输出 | P1 |
| F15 | F / 安全 | XSS | 用户名、图元文本、报警文本含 `<script>`/事件属性 | 登录、渲染、确认；检查 DOM 与 CSP | 全部按文本转义；不执行脚本；CSP 禁止内联脚本（若启用）；无 `innerHTML` 直插；审计保留原文 | E2E | DOM、截图、CSP 头 | P0 |
| F16 | F / 安全 | 注入与 CSV 注入 | 变量名/流程名/说明含 `';--`、`{}`、逗号、引号、`=cmd` | 执行动作；导出审计；Excel 打开 | 不产生 SQL/JSON/命令注入；审计文件正确转义；**导出层**对以 `= + - @` 开头的字段做防公式处理（前缀或文本化），但审计原始文件保留原文；无列错位 | 集成 | CSV、Excel 截图 | P1 |
| F17 | F / 安全 | 会话固定 / 令牌轮换 | 登录前已有匿名会话与匿名 ticket | 记录登录前 `sessionId` 与 resume 令牌；登录；再 resume；比较 | 登录成功后**轮换 `sessionId` 与 resume 令牌**；旧令牌立即失效（再用 → `resume:false`）；旧 ticket 不能复用；审计记录新会话；不存在“登录前后同一会话 ID”的固定窗口 | 集成 | 会话表、WS 帧、审计 | P0 |
| F18 | F / 安全 | 重放 | 抓取一条已执行命令 | 4 分钟内重放；6 分钟后重放；改时间戳/内容重放 | 窗口内返回原 ack 不执行；窗口外 `error{code:"E_DUPLICATE"}`；改时间戳/内容无效；无第二次动作 | 集成 | ack、审计 | P1 |
| F19 | F / 安全 | TLS 与绑定地址 | 启用 TLS；分别配 `127.0.0.1` 与 `0.0.0.0` | 测 TLS 版本/加密套件/HSTS；两种绑定各启动一次；从另一台机器访问 | TLS 1.2+、无弱套件、HSTS；绑定地址必须来自显式配置并在启动日志打印；`127.0.0.1` 时局域网不可连；`0.0.0.0` 时启动日志必须 Warn 并提示默认密码/防火墙；不存在未声明的隐式全网监听 | 集成 + 手工 | 扫描、配置、日志 | P0 |
| F20 | F / 安全 | 错误泄露 | 注入 500/异常 | 触发异常；看响应与日志 | 响应只有通用错误码/文案；无堆栈、路径、SQL、账号、密钥；详细原因只进服务端日志；日志不记录密码/令牌 | 集成 | 响应、日志 | P0 |
| F21 | F / 安全 | 审计防篡改 | 已登录管理员/操作员 | 通过 Web 尝试删除/改名/清空审计；检查 API 与权限 | 无删除/清空审计的 Web API；文件权限限制；追加写；篡改检测（若有哈希/校验）可发现；操作被审计 | 集成 | API 清单、文件属性 | P1 |
| F22 | F / 安全 | WebSocket 压缩炸弹 | 启用 permessage-deflate | 发送超大解压帧/畸形压缩帧 | 解压后大小受限（≤ 1MiB）；超限关闭连接；不 OOM；不崩溃；日志记录原因 | 集成 | WS 帧、内存 | P1 |

| F23 | F / 安全 | ticket 生命周期（无/过期/已用/IP 不符/跨站申请）+ 握手拒绝审计 | 服务已启动；能拿到合法 ticket；`IScadaAuditSink` 已接线 | ①无 ticket 握手；②错 ticket；③等 31s 用过期的；④握手成功后再用同一 ticket；⑤换一台机器（不同 IP）用同一 ticket；⑥从跨站页面 `POST /api/ticket` | ①-⑤ 握手 **401** 且业务 handler 不执行；⑥ 403 `E_ORIGIN` + 审计 Denied、不发放 ticket；ticket 单次、30s、可绑定 IP；**握手 401 也必须留一条 Denied 审计：由鉴权回调经 `IScadaAuditSink` 写，且写入必须投进命令泵的 UI 队列**（writer 的 `_writeGate` 只保证任意线程调用不撕裂，**顺序仍由泵保证**；不许在网络线程直接写）；日志只记前 6 位与 sessionId（**不记整串**） | 集成 | WS 状态、HTTP 状态、审计、日志 | P0 |
| F24 | F / 安全 | 会话令牌失效与复用拒绝 | 已登录会话 A；另备第二台机器（不同 IP）；桌面端可改密 | ①`auth.logout` 后用旧令牌 resume；②管理员踢人后 resume；③**桌面端**改密后 resume；④空闲超时后 resume；⑤同一令牌在另一条连接 resume；⑥从另一 IP resume | ①-⑥ 全部 `resume:false`（或握手被拒）+ 审计；令牌**单连接、绑定 `(ua, ip)`、成功即轮换**；不出现“登出了还能 resume”“改了密码旧连接还能写”“换个 IP 就能续上”；改密只发生在桌面端（Web 侧 `/change-password` 404，F08） | 集成 | WS 帧、会话表、审计 | P0 |

**F 阶段小计：24 条。**

---

## 7. G 阶段：打包与交付

| ID | 阶段 | 场景 | 前置条件 | 步骤 | 预期结果 | 自动化方式 | 证据 | 优先级 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| G01 | G / 打包 | 配置关两态之一 | `ScadaWeb.Enabled=false`；不部署 Web 目录 | `pack.ps1`；启动交付目录 | 打包成功（Web 非必需）；启动后**无 Web 端口监听**（`netstat` 无 8080）；主界面可打开、桌面登录可用、无 `ScadaWeb` 相关 Error/Warn；不因缺 Web 报错 | 集成 + 手工 | pack 输出、netstat、日志 | P0 |
| G02 | G / 打包 | 配置开 | `Enabled=true`；Web 目录、DLL、静态资源齐全 | `pack.ps1`；启动；登录 | 打包成功；端口监听；基础页 200；`/api/status` 正常；无缺失依赖 | 集成 | pack 输出、HTTP | P0 |
| G03 | G / 打包 | DLL 缺失 | 配置开；打包后删除 `Web\ScadaWeb.dll` | 启动；观察；再跑 `pack.ps1` | 宿主可用；WARN 明确；Web 不可用；不崩溃；**打包检查必须报缺口并以退出码 1 结束** | 集成 | 日志、pack 输出 | P0 |
| G04 | G / 打包 | 旧 DLL | 用上一版 DLL 覆盖 | 启动；请求 `/api/status`；登录 | 协议不匹配被拒绝并记录；宿主继续；不注册半套路由；客户端看到明确“版本不匹配” | 集成 | 日志、`/api/status` | P1 |
| G05 | G / 打包 | 旧静态资源 | 新 DLL + 旧 `index.html`/旧 JS | 启动；强制刷新；检查 hash/版本 | 版本不匹配时客户端刷新或拒绝；不出现旧 JS 调新协议；`index.html` 不缓存（M1.5 目标）；资源 hash 正确 | E2E | HAR、截图 | P0 |
| G06 | G / 打包 | `--no-incremental` | 修改一个 `wwwroot` 静态文件 | 普通增量构建；检查 `Web\wwwroot` 产物；再 `--no-incremental` | **通过条件是 `--no-incremental` 后 `Web\wwwroot` 字节更新**；增量构建若仍陈旧，只作为已知现象记录，不得据此判通过；`Web\wwwroot` 文件数等于源码 `wwwroot` 文件数；判据不看退出码 | 集成 | 文件清单、构建日志 | P0 |
| G07 | G / 打包 | `pack.ps1` 存在性校验 | 分别删除 Web DLL、静态资源、Watson 依赖 | 每次跑 `pack.ps1` | 退出码 1；打印缺口清单；不静默成功；补齐后退出码 0 | 集成 | pack 输出、退出码 | P0 |
| G08 | G / 打包 | 保护用户数据 | 交付目录已有 `AppConfig.json`、`ScadaUsers.json`、`Audit`、`Logs`、`.vms`、`Layout.xml` | 跑 `pack.ps1`；检查文件 | 全部保留；程序文件更新；不删用户数据；不覆盖账号/审计 | 集成 | 文件清单、hash | P0 |
| G09 | G / 打包 | Web 目录不含契约 DLL | 打包完成 | 检查 `Web\VM.*.dll` | 数量 0；模块依赖从宿主目录解析；类型身份一致（同 A04） | 集成 | 文件清单、反射 | P0 |
| G10 | G / 打包 | Watson 依赖 | 打包完成 | 检查 `WatsonWebserver.dll`、`IpMatcher`、`RegexMatcher`、`Timestamps`、`UrlMatcher`；版本 | 5 个文件齐全；版本与宿主引用一致；不是 6.x http.sys 版本；启动可监听 | 集成 | 文件清单、版本 | P0 |
| G11 | G / 打包 | `/api/status` 契约 | 交付目录启动 | 请求 `/api/status`；比对程序集/构建号；再请求 `/version` | `protocolVersion`、`appVersion`、`buildHash`、`schemaVersion`、`gate`、`loadState` 与产物一致；客户端版本不匹配时拒绝连接并提示升级；无敏感信息；**`/version` 返回 404**（旧端点已废弃） | 集成 + E2E | JSON、程序集 | P0 |
| G12 | G / 打包 | 静态资源清单 | 打包完成 | 校验 manifest 中每个文件存在；页面无 404；hash 匹配 | 清单完整；无 404；hash 与文件一致；无未引用孤儿文件（或允许但记录）；打包检查覆盖 | 集成 | manifest、HTTP | P1 |
| G13 | G / 打包 | Debug/Release | 修改 Web 模块源码 | 分别 Debug/Release 构建；跑 `pack.ps1`（Release） | 两种构建均成功；Release 产物含新符号/新字符串；`pack.ps1` 使用 Release；无 Debug-only 行为 | 集成 | 构建日志、DLL 符号 | P1 |
| G14 | G / 打包 | 升级/回滚 | 旧版本已安装且有用户数据 | 覆盖升级；启动；回滚旧版本；启动 | 升级保留用户数据；配置默认值兼容；旧客户端被协议版本拒绝；回滚后旧数据可读；无重复文件/双份 DLL | 手工 + 集成 | 文件、日志 | P1 |
| G15 | G / 打包 | 干净机器/防火墙 | 干净 Windows VM；无开发环境 | 按发布默认配置部署；首次启动；从另一台机器访问 | 依赖齐全可启动；启动日志打印实际绑定地址；若默认仅本机，按发布文档开启局域网后第二台机器可登录；若默认局域网，首次启动必须已强制改默认密码且有明确安全提示；两种情况都不得出现未声明的暴露 | 手工 | 截图、录屏 | P0 |
| G16 | G / 打包 | 自定义/非法端口 | 配置自定义端口；再配 0/70000/占用端口 | 启动；观察 | 自定义端口监听；非法端口回落默认并 Warn；占用端口明确失败；不崩溃；日志含实际端口 | 集成 | 日志、netstat | P1 |

**G 阶段小计：16 条。**

---

## 8. 阶段通过门槛

> 门槛口径：**P0 全绿才允许进入下一阶段**；P1 可在有书面豁免（写清原因、影响、补测日期）的前提下进入下一阶段，但**发布前必须清零**；P2 不阻断阶段，但发布前必须有结论。任何“跳过/未运行”都按未通过处理（R38）。

| 阶段 | 必须全绿的用例 | 额外门槛 | 不允许进入下一阶段的情形 |
| --- | --- | --- | --- |
| A / M0 | A01-A05、A07、A09-A16、A19、A20、A22、A25、A28-A30（A27 为 P1，可豁免但发布前清零） | 配置关/开两态都有证据；`auth:true` 与 `auth:false` 反证都跑过（含 `authCalls==1`、401 时 handler 不执行）；1000 条命令无丢失/无交错；类型身份一致；**方案 §17 四条 blocking 的运行时门禁（`B40` / `A01–A09` / `F23` / `F24`）在 M1 首周补跑并全部"已验收"** | 出现双份契约程序集、静默丢命令、未认证可连、命令不在 UI 线程执行；**四条 blocking 的运行时门禁任一条未闭环** |
| B / M1 | B01-B24、B29、**B33、B34、B36、B38、B39、B40** | **10 图元 × 10 客户端只读、10Hz**；2 个浏览器 context + 桌面同时在线；状态机 6 态（NotRunning/Unauthorized/AtCapacity/ReadOnly/RunStopped/Disconnected）全部覆盖；`Loaded`/`Unloaded` 恰好一次；关标签不停设备；10Hz p95 达标；**10 图元已实现派生量（B33）与视觉对照（B34）必须全绿——这是 M1 门槛**；未实现图元占位（B36）、**跨页低版本（B38，P0）**、基座热更新（B39，M1c）、晚订阅首帧（B40）有证据；**D21（文本/字体/中文）也按 M1 门槛判**。**B26（1000 图元快照）、B20 的 1000 图元部分归 M3/E；B35（坏值）/B37（图片）归 M1.5/M2；B33b（DP 对照）归 M1.5** | 任一状态无法区分、关标签停设备、快照/值帧版本错乱、重连丢值、**跨页低版本被误判成回退**；**Tank/Gauge 等 10 图元画错或派生量不一致仍宣称 M1 通过**；**把 M1b 内核 PASS 写成 M1 全绿** |
| C / M2 | C01-C05、C09-C20、C22-C34、C38-C40、C42、C44-C46、C48-C51、C54-C63、C65、C67-C80（C47/C52/C53/C64/C66/C77 为 P1，可豁免但发布前清零） | 两种 `ClickPolicy` 都跑；AllOperators **五项监管开关闸门**与六条监管手段全部可演示；**M2a 安全前置六项（C68-C72）全绿**；命令泵单条异常隔离与两级 `E_BUSY`（C73/C74）；三闸必填（C75）；caps 逐图元（C76）；`AllowCommands` 默认只读（C79）；动作副本不改原模型（C80）；并发写/启停/确认都有最终状态证据；**100 客户端同时写/按/确认（C61-C63）有指标与审计计数**；越权/伪造身份/过期版本全部被拒；50 与 100 客户端档有指标 | 任一越权命令被执行、审计串号/双前缀、并发出现两个最终状态、AllOperators 缺项仍可启用、**三闸可省略仍放行**、**幂等缺失（C02/C17/C74 红）仍宣称通过**、**`AllowCommands` 默认值被改成 true**、伪造身份成功 |
| D / M3 | D01-D17、D20、D26、D28、D31（D04-D09/D21 已在 M1 跑过，这里只做回归；D18/D19/D23/D25/D27/D29/D30/D32 为 P1，可豁免但发布前清零） | 29 条注册项全测；10 个派生量逐一与 WPF DP 对照；未知 TypeKey 有占位；图像预算达标；触摸与 IME 真机验收 | 任一条注册项无法实例化、派生量与 WPF 不一致、未知类型导致整页崩溃、图像超预算 |
| E / 暴力 | E01-E05、E08-E11、E14、E17、E23、**E25、E28、E29**（E26/E27 为 P1，可豁免但发布前清零） | 10/50/100 客户端都跑（50/100 档必须显式调高 `MaxSessions`）；**1000 图元/10Hz 长稳（E05/E09/E23/E25）——这是 1000 图元与 50/100 客户端的归属阶段，不是 M1**；24h 有一次完整绿；退出 ≤ 5s；端口 20 次绑定；默认容量 16 档 2 小时长稳；**100 客户端读+写+图片令牌 2 小时长稳（E29）** | 出现 OOM、会话/句柄泄漏、退出超时、100 客户端下崩溃、队列无界、`imageToken` 表无界增长 |
| F / 安全 | F01-F13、F15、F17、F19、F20、**F23、F24**（F14/F16/F18/F21/F22 为 P1/P2，可豁免但发布前清零） | 未登录/越权/默认密码/暴力破解/Origin/路径穿越/缓存旧版本全部通过；**ticket 生命周期与令牌失效全部通过（含握手 401 的 Denied 审计）**；渗透测试签字 | 任一高危安全问题可利用；默认密码可绕过；跨站 WS 可连；ticket 可重放；登出/改密后旧连接仍能写；图片可被他人会话取到 |
| G / 打包 | G01-G11、G15（G12 为 P1，可豁免但发布前清零） | 配置关/开两态都能交付；缺 DLL/旧 DLL/旧资源都有明确失败；`--no-incremental` 验证；`/api/status` 对齐；干净机器可启动 | 打包静默缺文件、旧资源混入、Web 目录带契约 DLL、干净机器起不来 |
| X / 流程 | X01-X05 | 派工记录、独立评审签字、结果台账、**文档一致性、整改闭环**五者齐全且可追溯到构建 hash | 任何一轮没有评审签字/台账缺行/两份文档口径冲突/整改项没有证据路径 |
| O / 优化 | O01、O02、O04（P0）+ O03（P1，可豁免但发布前清零） | 优化前后同构建同夹具对比；基线不漂移；**退化按噪声带判定（O04）**；优化项可独立回滚 | 无法证明"优化没让正确性或性能退化"；**O02 未跑却宣称优化完成** |

**发布总门槛**：A-G 的 P0 全绿 + X01-X03 全绿 + P1 清零（书面豁免除外）+ 24h 长稳至少一次完整通过 + 安全渗透签字 + 结果台账完整 + 打包产物在干净机器上完成一次登录/监控/操作/退出全流程。

---

## 9. 真机浏览器/触摸屏验收 vs 断言宿主

| 验收项 | 必须真机浏览器 | 可在断言宿主 | 说明 |
| --- | --- | --- | --- |
| 协议、鉴权、命令泵、状态机、快照/增量、权限、审计、幂等、并发 | 否 | 是 | `WebChecks` 用原始 WS 客户端，能精确计数；这是主战场 |
| 登录页/渲染/布局/字体/中文 | 是（Playwright + 真机各一次） | 部分（截图 diff 可在 Playwright） | 断言宿主没有真实排版引擎 |
| WebSocket Origin、ticket 握手（无 cookie）、缓存旧版本 | 是 | 部分（原始 WS 可伪造 Origin，但浏览器对 Origin/缓存的真实行为必须真机） | 浏览器对 Origin 与 HTTP 缓存的处理无法用裸 WS 替代；ticket 走真实浏览器 `fetch` + `WebSocket` 才能验证 |
| 触摸（单击/双击/长按/拖动/滚动） | 是（真实触摸屏） | 否 | Playwright 触摸事件只能覆盖大部分，手感与 300ms 延迟必须真机 |
| 中文 IME | 是（真实输入法） | 否 | 组合事件、候选框、提交时机只有真机可信 |
| 高 DPI/缩放/多分辨率 | 是（至少 100/150/200% 各一次） | 部分（Playwright 可设 deviceScaleFactor） | 字体回退与像素对齐需真机 |
| 10Hz/1000 图元/100 客户端/24h | 是（至少 24h 与 100 客户端各一次） | 是（无头可做主体） | 无头用于回归；真机用于验收 |
| 退出链/端口释放/防火墙/干净机器 | 是 | 部分 | 防火墙与干净机器只能真机 |
| 图像编码/带宽/下采样 | 是（真实网络） | 是（可测字节与耗时） | 带宽需真实局域网/限速 |
| 29 条图元视觉基线 | 是（至少一轮） | 是（Playwright 截图 diff） | 基线需人工批准 |

**真机验收最小集合**：D26（触摸）、D28（IME）、D29（DPI）、B21（两浏览器+桌面）、E09（24h）、E03（100 客户端）、G15（干净机器/防火墙）、F13（缓存旧版本）。这 8 项没有真机证据，不得宣布“验收通过”。

---

## 10. 测试夹具清单

### 10.1 固定方案与页面

| 夹具 | 内容 | 用途 |
| --- | --- | --- |
| `WebTest-Base.vms` | 固定方案，页面 ID/名称稳定，含 `P_Home`、`P_Detail`、`P_Alarm`、`P_Axis`、`P_Grid`、`P_Chart`、`P_Image`、`P_All29`、`P_1000`、`P_Unknown`、`P_Base`（基座） | 所有用例的基准；页面切换、基座、29 图元、1000 图元、未知类型 |
| `P_Home` | 10 个派生量图元 + 按钮/位按钮/输入框/下拉/旋钮/确认按钮 + 报警条 | B/D/C 的日常回归 |
| `P_All29` | 29 条注册项各一个（含 `Hmi.AxisPointTable` 隐藏别名） | D01/D02/D25 |
| `P_1000` | 1000 个混合图元（矩形/文本/指示灯/绑定），ID 可预测 | E04/E05/E09 |
| `P_Unknown` | 1 个 `Hmi.Unknown` + 5 个正常图元 | D03 |
| `P_Unsupported` | 10 个 M1 图元 + `Hmi.Button` + `Hmi.AlarmBanner` + `Hmi.ImageView` + 1 个嵌在 `Panel` 内的 `Hmi.Button` | B36（已知未实现 TypeKey 占位） |
| `P_Base` | 公共页眉/导航/背景；被 `P_Detail` 通过 `BasePageId` 引用 | B29/D20 |

### 10.2 假数据源

| 夹具 | 行为 | 用途 |
| --- | --- | --- |
| 假值源 `FakeScadaValueSource` | 确定性种子；10Hz 正弦/计数；可脚本化阶跃；支持 int/double/bool/string/图像；可注入“变量不存在/类型不匹配/延迟/乱序”；**可注入 `NaN`/`±Infinity`/非法枚举名/越界枚举值/不可解析文本** | B20/B17/C13/E22/B35 |
| 假轴源 `FakeAxisSource` | 3 根轴 X/Y/Z；位置/速度/使能/到位/限位/报警；可脚本化运动完成/失败/超时 | D15/C41 |
| 假轴命令口 `FakeAxisCommandSink` | 记录每次命令（轴、类型、参数、时间）；可注入拒绝/延迟 | D15 |
| 假报警引擎 `FakeAlarmEngine` | 20 条定义，覆盖 Critical/Warning；激活延时/恢复/确认；确定性 `Tick` | B14/C09/C16/C31/D14 |
| 假流程执行器 `FakeFlowRunner` | 记录 Start/Stop/RunOnce；可长跑/失败/并发；返回状态 | C14/C30/C38 |
| 假图像源 `FakeImageSource` | 1080p/4K 帧，变化图案；1080p 5fps、4K 1fps、突发 20fps；带时间戳 | D18/D31/E06/E07 |
| 假确认服务 `FakeConfirmationService` | 记录示教/定位确认；可返回 false | D15 |
| 假时钟 `FakeClock` | 可推进的墙钟 + 单调钟；用于租约/超时/保留期 | C27/E21/E12 |

### 10.3 客户端与网络

| 夹具 | 内容 | 用途 |
| --- | --- | --- |
| Playwright context A/B/C | 三个独立 `BrowserContext`（独立存储；**无 cookie 可依赖**），Chromium 与 Edge 各跑一轮 | B21/B25/C23/C24/F10 |
| 移动/触摸 context | `hasTouch=true`，viewport 1024×768 / 1920×1080 | D26/D29/D30 |
| 原始 WS 客户端 `RawWsClient` | 可构造任意帧、伪造 Origin、丢弃指定帧、重放 `requestId`、篡改/跳过 `seq`、复用 ticket、伪造身份字段（含**把 presence 的 `sessionRef` 当 `sessionId`**）、复用/篡改/过期 `imageToken` | A/B/C/F 的负面用例 |
| 网络限速/阻断 | Chrome DevTools throttling、Clumsy/防火墙、代理 | E17/E18/E19 |
| 双机局域网 | 一台宿主、一台客户端；固定 IP；防火墙可控 | G15/F19/B21 |

### 10.4 宿主与证据工具

| 夹具 | 内容 | 用途 |
| --- | --- | --- |
| `WebChecks`（新增断言宿主） | STA 线程 + Dispatcher 泵 + 假源 + 原始 WS + 指标 CSV；参考 `_WebProbe3` 的线程模型 | A/B/C/E/F 的集成用例 |
| `_WebProbe3` | 既有线程模型探针（1000 命令、背压、DropWrite、超时、UI 占用） | A14-A18 的基线/反证 |
| `_WebProbe1` | 既有模块加载探针（配置关/开、DLL 缺失、类型身份） | A01-A05 的基线/反证 |
| `ScadaChecks` | 领域层断言（页面、报警、权限、审计、图元注册） | D01/D02、C16 等 |
| `UIThemeSmokeTest` | Shell/主题/布局冒烟 | 桌面端同屏对照、B21 |
| `pack.ps1` | 交付打包与存在性校验 | G 阶段 |
| 证据工具 | `dotnet-counters`、`dotnet-dump`、`Get-Process`、`netstat`、Playwright trace、HAR、像素 diff | 所有阶段的证据 |
| 结果台账 | `docs\SCADA运行态Web客户端\结果台账.md`（列见 §13.2） | X03、每轮 R01、发布 R05 |

---

## 11. 失败判据

出现以下任一情况，该用例判 **失败**，不得用“偶发/环境问题”口头放过；必须记录证据并开缺陷：

1. **丢行/丢命令**：已接受（有 ack 或已入队）的命令没有执行、没有 ack、没有审计；或 1000 条命令少一条。
2. **重复动作**：同一 `requestId` 执行两次；双击/重连导致变量写两次、流程启两次、报警确认两次。
3. **审计串号/缺层**：`操作者` 或 `说明` 前缀里的 session/IP 与真实来源不一致；两条命令的字段互换；失败/拒绝没有**命令级**审计行；把两层混算成“每次点击恰好一行”。
4. **页面钩子重复**：同一次进入/离开 `Loaded`/`Unloaded` 执行次数 ≠ 1；刷新/重连重复执行。
5. **版本错乱**：`pageVersion` 回退、或被**绑定值变化**推动；旧 `runGeneration` 的帧/命令被接受；`frame` 里出现 `baseVersion`/`t`/`beatMs`（协议漂移）；版本跳变后把旧帧套到新结构上；页面半新半旧；**M1b 的 `snapshot.page` 被改成 camelCase**（前端属性袋全 undefined）。
6. **越权执行**：未登录、低角色、只读、非当前页、伪造/过期命令产生任何实际动作。
7. **静默丢弃**：队列满时 `TryWrite` 返回 true 但实际丢弃（`DropWrite`）；客户端没有收到 `E_BUSY`。
8. **资源泄漏**：24h 后**私有内存增长 > 100MB**（RSS 只记录、不判）；句柄/线程超阈值；`WebSession`/socket/定时器/订阅在关闭后不为 0；端口未释放。
9. **关标签停设备**：关闭浏览器导致内核停止、流程停止、设备状态改变，或触发 `Unloaded`。
10. **性能不达标**：10Hz p95 > 150ms 或 p99 > 300ms；**10 图元 10Hz 帧循环的 UI 线程占用 > 50ms/拍（P-UI，M1）**；**单条命令端到端 > 1000ms（P-CMD，M2）**；1000 图元首屏 > 3s / 快照超预算（M3/E04）；**把 1000 图元指标当成 M1 门槛**同样判失败（口径错误）。
11. **渲染错误**：29 条中任一条无法实例化/渲染；10 个派生量与 WPF DP 不一致；未知 TypeKey 导致整页崩溃；中文豆腐块。
12. **安全失败**：未登录可连 WS；默认密码可绕过；跨站 Origin 可连；路径穿越成功；XSS 执行；旧版本静态资源被继续使用；错误响应泄露堆栈/路径/密钥。
13. **打包失败**：配置开却缺 DLL/资源；Web 目录含 `VM.*.dll`；`/api/status` 与产物不一致；`pack.ps1` 静默成功；干净机器起不来。
14. **退出失败**：停止服务到进程退出 > 5s；端口仍被占用；WebSocket 未正常关闭；退出过程中仍有命令执行。
15. **不可复现的“通过”**：没有日志/CSV/截图/trace 之一作为证据；或测试实际未执行（`--no-build` 跑旧二进制、断言未注册、条数对不上）；或**结果台账里没有这一行**。
16. **身份被伪造**：伪造 `sessionId`/`role`/`actor` 的命令**被执行成超出真实权限的动作**，或审计署名与真实会话不符。**M2a 口径**：客户端身份字段被忽略、按真实身份判权**不算失败**（`E_IDENTITY` 归 M2 后续批）；但"改了字段就变成管理员"或"审计写成别人的名字"一定失败。
17. **凭据重放**：同一 ticket 二次握手成功；同一 `confirmToken` 二次执行成功；登出/改密/踢人/超时/撤销后旧令牌仍能换票或写值。**M2a 口径**：同一 `sessionToken` 开多条连接是**允许**的（多标签/刷新），不算重放；跨 IP 复用也不拦（`BoundIp=null`，已知边界，R-08）。
18. **能力缺口静默**：已知未实现图元渲染成空白/0×0/整页崩；坏值把 `NaN`/`undefined` 直接上屏，或把指针打到 0°（被误读成"值真的掉到零"）。
19. **三闸被绕过**：省略 `runGeneration`/`pageId`/`pageVersion` 仍执行；或先判角色后判版本（把"点的是上一页的按钮"报成越权）。**三闸必填已实现；C75 待执行。**
20. **命令泵被打死**：单条命令抛异常后后续命令不再执行；或 `E_BUSY` 时静默丢命令（`TryWrite` 返回 true 却未入队）；或命令在非 UI 线程上执行。

---

## 12. 执行顺序与用例计数

| 顺序 | 阶段 | 何时跑 | 阻塞关系 |
| --- | --- | --- | --- |
| 1 | A / M0 | 每次 Web 模块/宿主启动路径改动后；**M0.2 起 A 阶段 P0 的重跑与 B40/A01–A09/F23/F24 合并到 M1 首周** | 阻塞 B-G |
| 2 | B / M1 | A 的 P0 全绿 + 方案 §17.1 四条 blocking 已验收 + **§17.5 五条 M1b blocking 闭环**后 | 阻塞 C |
| 3 | C / M2 | B 的 P0 全绿后 | 阻塞 D/E 的操作类用例 |
| 4 | D / M3 | B 的渲染基线可用后，可与 C 并行 | 不阻塞 C，但发布前必须完成 |
| 5 | E / 暴力 | B/C 基本稳定后，nightly | 发布前必须有 24h 绿 |
| 6 | F / 安全 | A 之后即可并行，发布前必须完成 | 任何高危失败阻塞发布 |
| 7 | G / 打包 | 每个候选版本 | 发布前最后一道 |
| 8 | X / 流程 | 每轮启动与完成、每个里程碑、发布前 | 与所有阶段并行；缺签字/缺台账行即阻塞该轮 |
| 9 | O / 优化 | M3 功能冻结、E 全量跑过之后 | 优化后必须回归 A-G 的 P0 |

| 阶段 | 用例数 | P0 | P1 | P2 |
| --- | --- | --- | --- | --- |
| A / M0 | 30 | 21 | 8 | 1 |
| B / M1 | 41 | 34 | 6 | 1 |
| C / M2 | 80 | 61 | 19 | 0 |
| D / M3 | 32 | 22 | 8 | 2 |
| E / 暴力 | 29 | 15 | 13 | 1 |
| F / 安全 | 24 | 19 | 5 | 0 |
| G / 打包 | 16 | 11 | 5 | 0 |
| X / 流程 | 5 | 5 | 0 | 0 |
| O / 优化 | 4 | 3 | 1 | 0 |
| **合计** | **261** | **191** | **65** | **5** |

> 计数说明：上表按各节末“小计”汇总；P0/P1/P2 为用例优先级分布。若后续增删用例，必须同步改本节与对应小节小计，避免“文档说有 261 条、实际只跑了 190 条”这类静默缺口。**新增用例必须同时进 §0.7 需求覆盖索引与 §13 轮次门禁的"本轮新增"清单。**
>
> **v1.2 的增量对账**：B +4（B37–B40：P0×3 / P1×1）、C +7（C61–C67：P0×5 / P1×2）、E +1（E29，P0）、X +2（X04–X05，P0）、O +1（O04，P0）；**用例总数不变的两处优先级调整**：D21 由 P1 升 P0（M1 门槛）、O02 由 P1 升 P0（与 R04 解冲突）。合计 232 + 15 = **247**；P0 163 + 14 = **177**；P1 64 + 1 = **65**；P2 5（不变）。
>
> **v1.3 的增量对账**：**不新增用例**，只做口径修正与一处优先级调整——B38 由 P1 升 **P0**（跨页低版本是镜像正确性的一部分，保留在 M1 门槛）。合计 **247 条不变**；P0 **177 → 178**、P1 **65 → 64**、P2 5（不变）。其余七项（UI 停顿拆两条、24h 内存、C61 计数、元素限速跨会话、token 上限、`RequireAudit` 夹取、基座页版本）都是**判据改写**，不改变用例数量。
>
> **v1.4 的增量对账**：**新增 B33b**（M1.5 未实现派生量 DP 对照，P0），B 阶段 40 → **41**，合计 247 → **248**；P0 178 → **179**，P1 64 / P2 5 不变。其余都是判据改写：B02/B05 改登录入口、B10/B11/B16–B19 改 `type` 协议、B12/B13/B27 改 `sessionToken`、B20/B26 改 10 图元/1000 图元归属、B35/B37 改 M1.5/M2、B39 改 `basePageVersion`；指标编号 `M1/M1b` → `P-UI/P-CMD`。**没有新增 B33b 以外的用例**。
>
> **v1.5 的增量对账**：**新增 C68–C80 共 13 条**（P0×12 / P1×1）——C68 默认密码 gate、C69 每 IP 取票限速、C70 匿名令牌不续期、C71 会话/用户撤销、C72 会话表上限、C73 单条异常隔离、C74 `E_BUSY` 两级背压、C75 三闸顺序与必填、C76 `caps` 逐图元、C77 `AllOperatorsRequiresLease`、C78 六图元只读/禁用/失败反馈、C79 `AllowCommands=false` 总闸、C80 动作副本不改原模型。C 阶段 67 → **80**，合计 248 → **261**；P0 179 → **191**，P1 64 → **65**，P2 5 不变。**判据改写（不新增）**：C22 补 M2a 码表口径（`E_IDENTITY` 不产生）、C25 明确 presence 只给 `sessionRef` 且字段白名单、C50 按 M1c 令牌语义重写（同一令牌可多连接、不绑 IP）、§0.2 `ReadOnly`/`Loaded`、§0.3 消息集与幂等、§0.4 增加 `AllowCommands` 总闸、§0.5 新增 M15。**C68–C80 一条都没执行**（M2a 代码未接线、未构建），不得写"通过"。

---

## 13. 轮次门禁与结果台账

> 本节的规则与方案 §17 逐字一致。**门禁不是"建议"**：任何一轮只要缺一项，该轮结论记为"未完成"，不许用"下轮补"口头带过。

### 13.1 六个门禁时点（R00–R05）

| 编号 | 时点 | 必须做的事 | 证据 | 不做的后果 |
| --- | --- | --- | --- | --- |
| **R00** | 每轮**启动** | ①需求 → 用例映射（哪条需求由哪些用例覆盖，逐条列 ID）；②本轮新增功能**至少 1 条正向 + 1 条反向 + 1 条越权/失败**用例；③用例写清前置/步骤/预期/自动化方式/证据；④用例评审由**非作者**的另一角色签字 | 映射表、新增用例清单、评审记录 | 需求没人测、只测 happy path、失败路径永远没人写 |
| **R01** | 每轮**完成** | ①本轮 P0 全绿；②新增用例有证据（`evidence/<ID>/`）；③**每条新闸门做变异反证**（改坏 → 准确红 → 还原绿）；④跑相关回归（`ScadaChecks`/`FlowCanvasChecks`/`UIThemeSmokeTest`）；⑤列未决清单（含豁免编号） | 台账行、证据目录、变异反证记录、回归输出、未决清单 | "全绿"其实是"没跑"或"跑的是旧二进制" |
| **R02** | **里程碑**（M1/M1.5/M2/M3） | ①对应阶段 P0 全绿（§8 门槛）；②用户验收（真机或截图/录屏 + 三步说明）；③P1 豁免必须**书面**（原因/影响/补测日期/批准人） | 阶段门槛结果、用户验收记录、豁免单 | 带病进下一阶段，缺陷越晚越贵 |
| **R03** | 进**暴力测试前** | ①M3 功能冻结（不再合入功能改动）；②E 阶段全量跑过；③24h 长稳**至少一次**完整绿 | 冻结声明、E 全量结果、24h 指标 CSV | 边改边压测，指标不可比、回归无法归因 |
| **R04** | **优化后** | ①A–G 的 P0 回归全绿；②p95 / p99 / 内存 / CPU / 帧大小 / 首屏退化按 **O04 的噪声带**判定（噪声带 < 5% 时用 5% 门槛；噪声带 ≥ 5% 时以噪声带为准并记录）；③每个优化项可独立回滚（O03）；④**O02 未跑不得宣称优化完成** | 前后对比表、回归结果、回滚记录 | "优化"变"退化"且无人发现；用"还没跑对比"宣称优化完成 |
| **R05** | **发布** | ①结果台账完整（每用例一行）；②真机 8 项（§9）全有证据；③安全渗透签字；④打包产物在干净机器完成一次全流程 | 台账、真机证据、渗透报告、干净机器记录 | 发布无据可查，出问题无法定位是哪一轮引入 |

### 13.2 结果台账（唯一"跑过"的凭证）

台账文件：`docs\SCADA运行态Web客户端\结果台账.md`（**M0.1 已建立、M0.2 已追加、M1b 已追加**：M0 三探针、M0.1/M0.2 构建与断言、M1b 的 `_WebProbe6` 19/19 与 `ScadaChecks` 2266/0、复评结论与未关闭项均已入账；**矩阵 261 条仍未执行，M2a 行只能记 `阻塞`**）。**每轮每用例一行**，列固定为：

| 列 | 填写要求 |
| --- | --- |
| `用例 ID` | 矩阵编号（A01/B17/C45/E25/F23/X01/O01…） |
| `构建 hash` | 被测产物的 **DLL SHA256 前 12 位**（本仓不是 git 仓库，给不出 commit hash）；无法锚定到产物时写"只锚定日志 hash"（日志 SHA256 前 12 位）；**同一轮所有行必须一致** |
| `执行日期` | `YYYY-MM-DD HH:mm`（本地时间） |
| `执行人` | 真实姓名/工号，不许写"团队" |
| `环境` | 机器名 + OS 版本 + 浏览器版本 + **改过的**配置项（`MaxSessions`/`FrameRateHz`/`SessionInboundQueueCapacity`…） |
| `结果` | `通过` / `失败` / `阻塞`（**不许写"跳过"**；跳过按失败算，R38） |
| `证据路径` | `evidence/<用例ID>/` 下的具体文件；空 = 没跑 |
| `缺陷 ID` | 失败/阻塞必填；通过可空 |
| `评审人` | 复核证据的人（R01/X02 的签字人） |
| `备注` | 偏差、豁免编号、复现步骤摘要 |

**台账规则**：

1. **只追加、不改写历史行**；更正另起一行并在 `备注` 引用原行。
2. 同一轮的所有行构建 hash 必须一致；出现两种 hash = 这一轮混跑了两个版本，**整轮作废重跑**。
3. `失败`/`阻塞` 行必须带 `缺陷 ID`，否则视为台账不完整（X03 判红）。
4. 每轮结束由 docs 角色汇总一次；发布前由 reviewer 抽查 ≥20% 的证据路径是否可打开（X02）。

### 13.3 流程证据用例（X01–X03）

| ID | 阶段 | 场景 | 前置条件 | 步骤 | 预期结果 | 自动化方式 | 证据 | 优先级 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| X01 | X / 流程 | 每轮启动：需求→用例映射 + 新增用例齐备 + 用例评审 + 派工记录 | 本轮需求清单（变更点/缺陷单）；本文 v1.5；派工单（角色/任务/验收标准） | ①逐条需求映射到用例 ID；②为本轮每个新增功能补"1 正向 + 1 反向 + 1 越权/失败"；③写清前置/步骤/预期/自动化/证据；④交非作者角色评审；⑤核对派工记录 | 映射覆盖 100% 需求（无孤儿需求）；每个新增功能至少 3 条用例（正/反/越权）；评审意见逐条闭环（"已改"或"不改+理由"）；签字人 ≠ 作者；**派工并发子智能体 ≤8 且派工单可追溯（角色/任务/验收标准）**；**缺任一项该轮不得开始** | 手工（文档评审） | 映射表、新增用例清单、评审记录与签字、派工单 | P0 |
| X02 | X / 流程 | 每轮完成：独立评审签字（含变异反证与抽查） | 本轮 P0 已跑完、证据齐全、台账已填 | ①reviewer 抽查 ≥20% 用例的证据（P0 必须全查）；②核对每条新闸门的变异反证记录；③核对台账行与证据路径；④签字或退回 | 抽查证据与台账一致（构建 hash 一致、路径可打开）；每条新闸门都有"改坏→准确红→还原绿"记录；reviewer 签字；不通过项列缺陷 ID 并回炉；**未签字 = 本轮未完成** | 手工 + 脚本抽查 | 评审记录、抽查清单、变异反证记录 | P0 |
| X03 | X / 流程 | 结果台账完整性校验 | 本轮所有用例已执行 | ①统计台账行数与本轮应跑用例数；②校验每行 10 列齐全；③核对构建 hash 唯一；④打开抽查的证据路径 | 台账行数 ≥ 本轮应跑用例数；10 列齐全；同一轮 hash 唯一；失败/阻塞行都有缺陷 ID；证据路径全部可打开；**缺行/缺列/多 hash = 该轮未完成** | 脚本 + 手工复核 | 台账、校验脚本输出、抽查记录 | P0 |
| X04 | X / 流程 | **文档一致性闸门（方案 ↔ 矩阵 ↔ 台账）** | 方案 v1.8、矩阵 v1.5、结果台账三者齐全 | 按"口径清单"逐条比对：①默认值（16/32/120KB/2MiB/5/2/45/**10/30/4096**）；②端点与鉴权（**`type` 协议**、ticket 30s/单次 + `sessionToken` 8h 换票、`imageToken` M1.5、无 cookie、无 `/login`、`/change-password` 404）；③状态机 6 态（M1b）与 `stopped` 时 `Session/Document` 为 null；④权限（M1 只读 `E_UNSUPPORTED`；M2a **`AllowCommands` 默认 false**、权限层四道、平局都可点、`AllOperators` 五项闸门、caps 逐图元、三闸必填、**幂等 `(SessionId, RequestId)` 5 分钟去重（M2a-4 已关闭）**）；⑤审计（7 列 + 前缀唯一拼装 + 两层计数）；⑥版本（`PageKey = pageId@pageVersion@basePageVersion`）；⑦阈值（**P-UI 50ms 按 10 图元**；P-CMD 1000ms 是 M2；1000 图元/50 客户端归 M3；24h 私有内存 100MB）；⑧配置键（`BindAddress`/`Port`，无 `Host`；M2a 键名 `AllowCommands`/`TicketRatePerMinute`/`ConfirmTokenSeconds`）；⑨**全文不得出现 `kind` 作为消息字段**、`snapshot.page` 必须是 PascalCase、`frame.items` 必须是数组；⑩错误码（`E_NOT_FOUND` 取代 `E_UNKNOWN_ELEMENT`；`E_DEFAULT_PASSWORD`/`E_SESSION_REVOKED`/`E_CAPACITY` 只在方案 §9.8 定义） | 十类口径**逐条一致**；**任一类冲突 = 该轮未完成**；冲突清单与修订提交 hash 一并留档 | 脚本（grep/正则）+ 人工复核 | 口径清单、比对输出、修订记录 | P0 |
| X05 | X / 流程 | **整改项闭环（M0.1 blocking 证据链）** | 方案 §17.1 的 B1–B4 状态列 | ①逐条读状态（必须是"已验收 / 部分关闭 / 未开始"之一，不许写"整改中"就完事）；②每条"部分关闭"必须列出已落地部分与未落地部分；③每个"已落地"必须有断言 ID 或构建/运行证据路径；④未关闭项必须有责任人与复评日期 | 四条 blocking 状态与证据一一对应；**"部分关闭"不得被读成"通过"**（M0 门禁仍为未通过）；未关闭项全部进结果台账"未关闭项"表；缺证据路径 = 该条判红 | 人工复核 + 台账比对 | 状态表、证据路径、台账未关闭项 | P0 |

**X 阶段小计：5 条。**

---

## 14. 优化阶段

> 进入条件：M3 功能冻结 + E 阶段全量跑过 + 24h 长稳至少一次（R03）。优化阶段**不许改功能语义**，只允许改实现与配置默认值（默认值改动要同步方案 §5.4 与本文 §0.5）。

### 14.1 优化阶段的四条纪律

1. **先有基线再动手**：O01 的 `baseline.json` 必须来自"同一构建 + 同一夹具 + 同一参考机"，否则前后不可比。
2. **退化按噪声带判定（O04）**：p95 / p99 / 内存 / CPU / 帧大小 / 首屏六项逐项比；先在同一构建上重复采样 ≥3 次得到**噪声带**（各项 p95 的样本间极差），再比"优化后 vs 基线"：变化量 ≤ max(5%, 噪声带) 视为无退化；超出必须回滚或给出书面理由 + 用户确认。**不许拿一次采样的抖动当"优化成功"或"退化"。**
3. **正确性优先**：优化后 A–G 的 P0 必须全绿；任何"为了快而放宽判据"直接判失败；**O02 未跑不得宣称优化完成**。
4. **可独立回滚**：每个优化项一个独立提交（或独立开关），O03 要能只回滚它而不牵连其它项。

### 14.2 优化阶段用例（O01–O04）

| ID | 阶段 | 场景 | 前置条件 | 步骤 | 预期结果 | 自动化方式 | 证据 | 优先级 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| O01 | O / 优化 | 优化前基线采集 | M3 功能冻结；E 全量已跑过；同一构建、同一夹具、同一参考机 | 采集 p95/p99、CPU、内存、帧大小、首屏、登录耗时；**同一构建重复采样 ≥3 次**；存 `baseline.json`；记录配置快照 | 基线含每项的均值/分位数、采样次数与**样本间极差（噪声带）**；夹具版本与配置快照一并存档；构建 hash 固定且写进文件名；**基线采集期间不许合入任何改动** | 集成 | `baseline.json`、指标 CSV、配置快照 | P0 |
| O02 | O / 优化 | 优化后对比（**P0：不跑不得宣称优化完成**） | 优化项已合入；同一构建流程/夹具/参考机；`baseline.json` 已就绪 | 重跑同一套指标（同样重复 ≥3 次）；与 `baseline.json` 按 O04 噪声带逐项比较；跑 A–G 的 P0 回归 | 六项指标变化均 ≤ max(5%, 噪声带)；A–G P0 全绿；任一项超限 → 回滚该优化项或出具书面理由 + 用户确认；对比表逐项列出"前 / 后 / 变化率 / 噪声带 / 判定" | 集成 | `after.json`、对比表、回归结果 | P0 |
| O03 | O / 优化 | 优化项可独立回滚 | 每个优化项是独立提交或独立开关 | 单独回滚该优化项；重跑指标与正确性用例 | 回滚后指标回到基线 ±max(5%, 噪声带)；正确性用例全绿；不依赖其它优化项；回滚命令/步骤写进记录且可复现 | 集成 | 回滚记录、指标、回归结果 | P1 |
| O04 | O / 优化 | **重复采样与噪声带** | 同一构建、同一夹具、同一参考机；至少一个可比的指标集 | 对同一构建连续采样 ≥3 轮（每轮间隔 ≥60s，环境不变）；对每项算"样本间极差 / 中位数"；用该噪声带复核 O02 的判定 | 每项都给出噪声带（绝对值 + 百分比）；**噪声带 < 5% 时 5% 门槛生效；噪声带 ≥ 5% 时以噪声带为准**并写明"该指标在当前参考机上不可判 5%"；采样轮次、时间、环境全部入档；不许只采一轮就下结论 | 集成 | `noise.json`、采样明细 | P0 |

**O 阶段小计：4 条。**

---

## 15. 变更记录

| 日期 | 版本 | 内容 |
| --- | --- | --- |
| 2026-10-21 | v1.0 | 初稿：196 条（A26 / B32 / C44 / D32 / E24 / F22 / G16） |
| 2026-10-21 | v1.1 | **M0.1 整改同步**：认证改为 ticket（无 cookie），删除 Cookie/HttpOnly/SameSite/Secure 判据；审计回到 7 列 + `说明` 前缀 `[web s=… ip=…]` + 命令级/动作级两层计数；`HighestRoleOnly` 平局改为"都可点"；默认值统一 `MaxSessions=16`/入站 32/帧 120KB/快照 2MiB（并要求 50/100 档显式调高）；`PageVersion` 只随结构变化、值帧无 `baseVersion`；新增未实现图元占位与坏值保留（B35/B36）；身份服务端派生（C50）；动作副本过滤 + `confirmToken`（C46/C54）；新增 A27-A30、B33-B36、C45-C60、E25-E28、F23-F24、X01-X03、O01-O03；新增 §13 轮次门禁与结果台账、§14 优化阶段；总数 232 条（P0 163 / P1 64 / P2 5） |
| 2026-10-21 | v1.2 | **M0.1 复评整改（契约/矩阵冲突清零）**：①`/img` 改独立 `imageToken`（按会话、30–60s、可轮换、只放 query、与元素+版本绑定，401/403 分开），新增 B37；②`AllOperators` 五项监管闸门（`RequireOnlineUsers`/`RequireAudit` 固定 true/`RequireRateLimit`/`RequireConfirmForDangerous`/`ShowOnlineUsers`），C44 改写为四缺项扫描、新增 C65（`RequireAudit=false` 非法）/C66（全开正向）；③速率双层（5/s + 元素 2/s），C45 改为"同一会话 1 秒 6 条不同元素 → 前 5 条执行、第 6 条限速"，新增 C64 反向；④容量/阈值统一：C56 显式 `MaxSessions>=20`、C57 显式 ≥50 且 p95 ≤300ms、C59/E28 p95 ≤500ms；新增 C61–C63（100 客户端同时写/按/确认）、E29（100 客户端读+写+令牌 2 小时长稳）；⑤`(pageId, pageVersion)` 联合判定：B13/B14/B28 改写、新增 B38（跨页低版本）/B39（基座热更新）；⑥presence 只给 `sessionRef`：C50 前置改写、F03/F04/F05/F08 改为 WS-only/404；⑦审计两层计数：C03/C14/C23 改写、新增 C67（前缀唯一拼装）；⑧M1 视觉闸门：B33 补 5 个派生量、D21 升 P0、D04–D09 标注为 B33/B34 分解证据、B35 去掉 `SelectorSwitch`（移到 D11）、B32 归 M1.5；⑨E26 改 16/32/64、删除 256 条件式；⑩O02 升 P0、新增 O04（噪声带），R04 同步；⑪新增 B40（晚订阅首帧）、X04（文档一致性）、X05（整改闭环）；总数 232 → **247 条（P0 177 / P1 65 / P2 5）** |
| 2026-10-21 | v1.3 | **M0.2 复评（8 处口径冲突裁定，不新增用例）**：①UI 停顿拆成 **M1（10Hz×1000 图元帧循环 UI 占用 ≤50ms/拍）+ M1b（单条命令端到端 ≤1000ms）**，B20/§11 同步；②24h 内存统一 **私有内存增长 ≤100MB、RSS 仅记录**（M6/E09/§11）；③C61 审计计数写死为"①100 条命令级（2 Success + 98 Denied）+ 2 条动作级；②100 条命令级 + 100 条动作级"；④`ElementRatePerSecond` 明确**同一 `elementId` 跨会话共享**（C45/C61/C64/M13）；⑤`imageToken` 同时最多 **3 个**，E29 上限改"会话数 × 3"（M14）；⑥**B38 升 P0**，§8 的 M1 门槛改 B33–B40 全绿；⑦C65 明确"**加载时夹回 `true` 并写 Error**"；⑧B39 时序改"**300ms 防抖 + 1 帧（10Hz 下 ≤100ms）**"并补"所有引用页重建"；另：B40 去掉"实现前必红"的陈旧说明（`TryGetCurrent` 已落地）、F23 的 writer 线程口径改"`_writeGate` 任意线程安全、顺序由泵保证"、§13.2 构建 hash 改 **DLL SHA256 前 12 / 日志 hash**、X04 口径清单补新阈值。合计 **247 条不变；P0 177 → 178 / P1 65 → 64 / P2 5** |
| 2026-10-21 | v1.4 | **M1b 协议/范围/证据链对齐（M1c 整改中）**：①§0.3 全量改 `type` 协议（`hello/hello.ok/snapshot/page/frame/stopped/idle/pong/error/bye`），删除 `kind/state/caps/view.attach/auth.login/view.resync` 作为 M1 消息；②B02/B05 改登录入口（无 ticket → 登录卡片；匿名只读；默认密码 gate 归 M1c）；③B10/B11/B16–B19 改 as-built（`E_UNSUPPORTED`、`stopped`、PascalCase 快照、items 数组、无 resync、等 `page`）；④B12/B13/B27 改 `sessionToken`（8h）+ `refreshToken` 换票、退避 1/2/4/8/10s；⑤B20 改 10 图元 × 10 客户端只读；B26 移 M3/E04；⑥B33 拆 **B33（M1b 已实现视觉派生量）** + **B33b（M1.5 DP 对照）**，新增 1 条；⑦B35 改 M1.5-M2、B37 改 M1.5、B39 改 `basePageVersion`；⑧指标编号 `M1/M1b` → `P-UI/P-CMD`；⑨§8 B 门槛重写、§12 计数 248（P0 179/P1 64/P2 5）、X04 九类口径。合计 **248 条** |
| 2026-10-21 | v1.5 | **M2a 设计定稿（代码已开始落地、未验证）**：①§0.2 `ReadOnly`/`Loaded` 按 M2a 重写；②§0.3 消息集加四条命令与 `caps`/`ack`/`presence`/`toast`，`caps` 行改逐图元，**幂等改为"M2a 未实现"**（C02/C17 归后续批），三闸改"缺省必须 `E_BAD_REQUEST`"；③§0.4 新增 `AllowCommands` 总闸行、M1 行改 `E_POLICY`；④§0.5 新增 **M15**（`AllowCommands=false`/`TicketRatePerMinute=10`/`ConfirmTokenSeconds=30`/`MaxSessionTokens=4096`）；⑤§0.7 新增 7 条 M2a 需求映射；⑥新增 **C68–C80 共 13 条**（安全前置 5 条、泵 2 条、三闸/caps/总闸/副本 4 条、六图元反馈 1 条、`AllOperatorsRequiresLease` 1 条）；⑦C22/C25/C50 判据改写（`E_IDENTITY` 不产生、presence 只给 `sessionRef`、令牌可多连接不绑 IP）；⑧§8 C 门槛加 C68–C80 与"M2a 安全前置全绿"；⑨§12 计数 67 → **80**、合计 **261**（P0 191/P1 65/P2 5）+ v1.5 增量对账；⑩X04 扩到十类口径（方案 v1.7 / 矩阵 v1.5）。**C68–C80 一条都没执行，不得写"通过"** |
| 2026-10-21 | v1.6 | M2a 第二轮终审整改：97/97 + 2332/0；六条 P0 + 新 P0-1/P1-1 关闭；C68–C80 仍未执行 |
| 2026-10-21 | v1.7 | M2b 109/109（含 C68–C72 HTTP 端到端 4/4）+ 真实浏览器双角色 E2E PASS（HighestRoleOnly：admin 可点/op 只读/租约 UI；AllOperators：admin+op 均可点、审计分别记录）；租约（`lease.acquire/renew/release`）+ AckButton 报警列表（`alarms` 每 2 秒广播）落地；`ScadaChecks` 2340/0；C78 未逐控件 E2E，C68–C80 其余已跑；`AllowCommands=false` |
| 2026-10-21 | v1.8 | M3 暴力测试（基础档）：`Web\ScadaWebStress` 100 客户端 / 1000 图元 / 2000 命令 `errors=0`；50 客户端 p95=98ms；100 客户端优化前 p95=709ms/721MB → 优化后 850–1635ms/202–294MB（`ApplyCaps` 每页只取一次 `OnlineSnapshot`，消除 O(n²) 复制）；100 客户端 p95 未达 M8 <500ms，**不写 M3 达标** |
| 2026-10-21 | v1.9 | M3 复杂档（多页面/多绑定/真实切页）：5×5（1200 图元/2400 绑定）p95=57ms/102MB；20×10（2400/7200）`errors=0`、`page` 77、p50/p95/max=2/302/466ms、快照均值 238KB、175MB；50×10（2400/7200）p95=1119ms/808MB；100×5（1800/3600）p95=2066ms/621MB；补充 20×5 大页（5000 图元）p95=157ms/201MB；100 客户端复杂档未达 M8 <500ms，**不写 M3 达标** |


