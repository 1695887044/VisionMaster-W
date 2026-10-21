# SCADA 运行态 Web 客户端 M1b（2026-10-21）

> 范围：M1 内核批次（**只读镜像**）——服务端 `ScadaWebHost`、会话/票/值帧解析、`wwwroot` 10 图元渲染器、快照 DTO/构建器；文档同步到方案 **v1.5**、矩阵 **v1.4**，台账补 M1b 行，新建 `已知偏差.md`。
> 结论先行：**M1b 内核 PASS、M1 交付未收口、M1c 整改中**（历史快照；**M1c 已关闭五条 blocking**，见 `2026-10-21-SCADA运行态Web-M1c.md` 与台账 §2.7）。证据是 `_WebProbe6` **19/19**（两次）与全量 `ScadaChecks` **2266/0**；**这两组数字都不是矩阵用例，矩阵 248 条（原 247 + 本轮新增 B33b）仍未执行**。M1b 双评审（协议 / 证据）结论 **FAIL**，五条 blocking 见 §6。
> 上位纪律：`AGENTS.md` R38（跳过型断言必须能被反证）、R65（改断言宿主后不许 `--no-build`）、R42/R62（增量构建会静默漏打包）、R55（Debug/Release 都要重建）；方案 §17.5（M1b blocking 唯一状态来源）。

---

## 1. 背景

M1a 完成了 HTTP + WS 骨架（静态页、ticket、hello/pong），但没有页面快照、没有值帧、没有 10 图元渲染器。M1b 要回答的是"浏览器能不能看到与桌面同一页、同一组值"：

1. 服务端在**运行中**拍出不可变页面快照，并每 100ms 发一帧值；
2. 前端用 `elements-process.js` 渲染 10 个图元（Text / Rectangle / Panel / IOField / ProgressBar / Indicator / Lamp / Tank / Valve / Gauge）；
3. 切页/结构变化用 `page`（整份结构）表达，运行结束用 `stopped`，未运行用 `idle`；
4. 浏览器在 1920×1080 下与桌面同构图，差异逐条登记。

M1b **不做**：命令泵（M2）、`auth.login`/`caps`/`view.attach`（M2）、动画/闪烁（M1.5）、坏值角标（M1.5/M2）、`/img` 与 `imageToken`（M1.5）。这些边界在方案 §10.2.2/§10.3/§10.6 与矩阵 B35/B37 已写死。

---

## 2. as-built 协议（M1b 的唯一口径）

> 以 `_WebProbe6` 的原始帧与 `ScadaWebHost` 为准。**没有 `kind`**；所有消息用 `type`。

| 方向 | `type` | 关键字段 |
| --- | --- | --- |
| S→C（首帧） | `hello` | `protocol` / `server` / `sessionRef` / `readOnly` / `running` / `pageId?` / `pageName?` / `pageVersion?` / `online` |
| S→C | `hello.ok` | `sessionRef` / `readOnly`（对客户端 `hello` 的确认） |
| S→C | `snapshot` | `seq` / `runGeneration` / `pageId` / `pageVersion` / `page`（**PascalCase**，含 `BasePage` / `Elements`） |
| S→C | `page` | 同 `snapshot`；同一轮 `PageKey` 变化时发整份结构 |
| S→C | `frame` | `seq` / `runGeneration` / `pageId` / `pageVersion` / `items:[{elementId,property,text,number,bool,color}]` |
| S→C | `stopped` | `runGeneration?` |
| S→C | `idle` | 无字段（未运行） |
| S→C | `pong` | `t` |
| S→C | `error` | `code` / `message`（`E_BAD_JSON` / `E_UNSUPPORTED`） |
| S→C | `bye` | `reason` / `closeCode` / `message`（容量拒绝：`at-capacity` / 1013） |
| C→S | `ping` | `t` |
| C→S | `hello` | 无必填字段（可选确认） |
| C→S | 其它任何 `type` | 回 `error{E_UNSUPPORTED}`，不执行 |

**HTTP**：`GET /`、`/index.html`、`/app.js`、`/styles.css`、`/elements-process.js`、`/elements-process.css`；`GET /api/status`；`GET /api/catalog`；`POST /api/ticket`（`{}` / `{user,password}` / M1c 的 `{refreshToken}`）；`GET /img/...` **固定 404 `E_IMG_NOT_READY`**；`WS /ws`（`auth:true` + 一次性 ticket）。

**命名**：`snapshot.page` / `page.page` 是 PascalCase（`PageId`/`Elements`/`Properties`/`BasePage`），`frame.items` 是 camelCase（`elementId`/`property`/`text`/`number`/`bool`/`color`）。**不要统一**：前者按 `elements-process.js` 的读取口径，后者按值帧消费口径。

---

## 3. 改动清单

### 3.1 代码（M1b 批次）

| 文件 | 内容 | 锚点 |
| --- | --- | --- |
| `Web\VisionMaster.ScadaWeb\ScadaWebHost.cs` | Watson 监听 + 3 个 HTTP 接口 + `/img` 404 + 5 个静态文件 + `WS /ws`（`auth:true`）；启动断言三连；`hello/snapshot/page/frame/stopped/idle/pong/error/bye`；10Hz 宿主级值帧循环；`TryGetCurrent` 拍快照；`PageKey = pageId@pageVersion`（M1b） | 全文（约 1404 行） |
| `Web\VisionMaster.ScadaWeb\ScadaWebSession.cs` | 每连接：`SessionId`（32 hex，服务端）/ `SessionRef`（16 hex，对外）/ `CurrentPageKey` / 有界发送队列 256（满则丢帧，`DroppedCount`） | 全文 |
| `Web\VisionMaster.ScadaWeb\ScadaWebTicketService.cs` | 一次性 ticket（30s/单次，`_live` → `_handoff` 两表）；M1c 起 `ScadaWebSessionGrant`（8h）+ `IssueSessionLocked` | 全文；`Lifetime` / `SessionLifetime` |
| `Web\VisionMaster.ScadaWeb\ScadaWebValueResolver.cs` | 绑定 → `frame.items` 四类值；同帧按变量缓存；诊断按消息去重；`DisplayFormat` 尚未应用 | 全文 |
| `Web\VisionMaster.ScadaWeb\wwwroot\index.html` | 基础页骨架 + 登录卡片（M1c 整改中）+ `MIN_SCALE` 滚动容器 | 全文 |
| `Web\VisionMaster.ScadaWeb\wwwroot\app.js` | 连接/重连（1/2/4/8/10s）、`requestTicket`、`sessionToken`（M1c）、`showLogin`/`expireSession`、`applySnapshot`/`applyFrame`、`applyScale` | 全文（约 2099 行） |
| `Web\VisionMaster.ScadaWeb\wwwroot\elements-process.js` | 10 图元渲染器 + 占位盒；Tank 深色底带/圆角裁剪；Lamp 未知值保持上一次 | 全文 |
| `Web\VisionMaster.ScadaWeb\wwwroot\styles.css` / `elements-process.css` | 布局/字体栈（`Microsoft YaHei`）/省略号/最小缩放滚动 | 全文 |
| `Scada\Models\ScadaPageSnapshot.cs` | 不可变页面快照（`PageId/PageName/PageVersion/Width/Height/Background/BasePage/Elements`） | 全文 |
| `Scada\Models\ScadaPageSnapshotBuilder.cs` | 快照构建（基座一层、元素全收 + `LayerVisible`、稳定 ZIndex 排序、绑定含 `DisplayFormat`） | 全文 |
| `VisionMaster\Services\ScadaRenderCatalog.cs` | `ElementRegistry.All` → `IScadaRenderCatalog`；`App.xaml.cs:326` 注册 | 全文 |

> **写作时的并发状态**：`ScadaWebTicketService.cs` 与 `app.js` 在 08:07 仍在被修改（`sessionToken`/登录卡片）；`ScadaWebHost.cs` 最后修改是 03:16，**尚未接 `hello.sessionToken` 与 `refreshToken` 分支**。本记录 §4 的构建/断言证据锚定 **03:05 的 `_m1a` 批次**，不覆盖 08:07 的在途改动；**在途文件可能暂不编译，M1c 收尾必须重新构建并记录新 hash**。

### 3.2 文档

| 文件 | 改动 |
| --- | --- |
| `docs\SCADA运行态Web客户端方案.md` | v1.4 → **v1.5**：§9 全量改 `type` as-built；§4.4 快照/`PageKey`；§4.5 值帧全量；§6.1 路由/静态文件；§6.2 状态机；§6.3 时序；§8.9 `sessionToken`；§10.2/§10.2.2/§10.3/§10.6 阶段归属；§11.2 指标改名 `P-UI`/`P-CMD`；§12.3 W1 行；§13.1 拆 M1b/M1c/M1；§16.10 M1b 证据；§17.5 五条 blocking；附录 A/B/C |
| `docs\SCADA运行态Web客户端测试矩阵.md` | v1.3 → **v1.4**：§0.1 I3/I8/I12；§0.2 状态机；§0.3 协议；§0.4 M1 只读；§0.5 指标；§0.7 索引；B02/B05/B06–B13/B16–B20/B22/B25–B27/B30/B33/B33b/B35/B37/B39/B40；A22；F03/F06；§8 门槛；§11 失败判据；§12 计数 **248（P0 179 / P1 64 / P2 5）**；X04；§15 |
| `docs\SCADA运行态Web客户端\结果台账.md` | 头部状态 + §2.6 M1b 批次 + §4.2 M1b 复评 + U-12–U-15 + 变更记录 |
| `docs\SCADA运行态Web客户端\已知偏差.md` | **新建**：9 条已知偏差（逐条写"允许偏差/需复刻/后续处理"） |

---

## 4. 证据（只写实际跑到的）

| 项 | 结果 | 证据 / hash |
| --- | --- | --- |
| 构建（`ScadaChecks` 及依赖链） | **0 错误 / 51 警告** | `_m1a_build.log`，SHA256 前 12 **`F02D4A8C5B78`**，18201 B，2026-10-21 03:05:08 |
| 全量 `ScadaChecks` | **2266 通过 / 0 失败** | `_m1a_run.log`，SHA256 前 12 **`3B820D75FBCE`**，347924 B，2026-10-21 03:05:45 |
| 被测产物 | **只锚定日志 hash** | 03:05 的 `ScadaChecks.dll` 已在 07:52:56 被重建（现 `CC5D12AC4E3B`），**不能拿现 DLL 冒充** |
| `_WebProbe6` 第一次 | **19 PASS / 0 FAIL** | `_m1b_probe6.log`，SHA256 前 12 **`2F1350424F1C`**，2714 B，2026-10-21 07:44:29 |
| `_WebProbe6` 第二次 | **19 PASS / 0 FAIL** | `_m1b_probe6b.log`，SHA256 前 12 **`CA1269BDF787`**，2714 B，2026-10-21 07:45:33 |
| 浏览器 10 渲染器 / 1920 | 演示页 10 图元（含基座 + Panel 子坐标）；1366×768 缩放 + 1920×1080 构图 | `_scada_web_shots\demo-1920x1080.png`、`demo-1366x768-final.png`、`mock-live.png`、`no-ticket.png`、`missing-elements-process.png`（03:15–03:26）；Playwright + `msedge` 脚本在 `%TEMP%\scada-web-test\run-tests.js`（临时目录，未入仓，**未留运行日志**）；`no-ticket.png` 是登录卡片之前的旧行为 |
| 罐体修复 | `Hmi.Tank` 读数/位号深色底带 + 圆角裁剪 | `elements-process.js` 的 `Hmi.Tank` 段；`demo-1920x1080.png` |
| 双评审 | **FAIL**（协议 / 证据） | 五条 blocking 见 §6 |

**`_WebProbe6` 的 19 条**：B1 监听 + WS 启用、B2–B4 静态 3 文件（`index/app.js/styles.css`；`elements-process.js/.css` 未单独探针）、B5 未知路径/穿越拒绝、B6 运行启动、B7 ticket、B8 WS 连接、B9 `hello`、B10 `snapshot`（10 元素）、B11 `frame`（10 items）、B12 四类值、B13 值变化、B14 `page`（11→12）、B15 `pong`、B16 `E_UNSUPPORTED`、B17 `stopped`、B18 新运行 `snapshot`（generation 8）、B19 停服。

**没有覆盖**：登录卡片 / `sessionToken` 换票 / `BasePageVersion` / `DisplayFormat` / 动画 / 坏值 / `/img` / 矩阵 B 阶段任何一条。

---

## 5. 口径裁定（本轮同步到两份文档）

| # | 项 | 裁定 |
| --- | --- | --- |
| 1 | 协议 | **`type`** + `hello/hello.ok/snapshot/page/frame/stopped/idle/pong/error/bye`；`snapshot.page` PascalCase；`frame.items` camelCase；**没有 `kind/state/caps/view.attach/auth.login/view.resync`**；M2 扩展点单独标注 |
| 2 | M1 范围 | **10 图元、10Hz、只读镜像、10 客户端**；1000 图元/50 客户端归 **M3/E**（B20/B26 改写）；若用户要求 M1 即 1000 图元，另立 **M1.5 性能项** |
| 3 | 动画/坏值/图片 | 动画（Appearance/Move/Visibility/闪烁）、B35 坏值角标、B37 `/img` 全部 **M1.5/M2**；M1 只做静态 + 值帧 |
| 4 | 重连 | `sessionToken`（8h，ticket 核销时签发，`hello` 下发）+ `POST /api/ticket {refreshToken}` 换新 ticket；ticket 仍单次/30s；退避 1/2/4/8/10s |
| 5 | 基座版本 | 快照信封带 `BasePageVersion`；`PageKey = pageId@pageVersion@basePageVersion`；基座版本变化 → 下一拍 `page`（**M1c 落地**） |
| 6 | `DisplayFormat` | 帧 `text` 用 `binding.DisplayFormat` 格式化（**M1c**）；`Gain/Offset/FormatType` 留 M1.5；B33 拆 **B33（已实现视觉派生量）** + **B33b（未实现 DP 对照）** |
| 7 | 登录入口 | 无 ticket → 登录卡片；`POST /api/ticket` 取票；匿名只读看 `AllowAnonymousView`；重连用 `sessionToken` |
| 8 | UI 偏差 | 9 条登记在 `已知偏差.md`；B34 只排除这些块并记录排除数 |
| 9 | 指标编号 | 原 `M1`（UI 占用）/`M1b`（命令端到端）改名 **`P-UI`/`P-CMD`**，避免与里程碑 M1b 同名 |

---

## 6. 双评审 FAIL 与五条 blocking（M1c）

| # | blocking | 状态 | 缺口 |
| --- | --- | --- | --- |
| M1b-1 | 重连 | 整改中 | `ScadaWebHost` 的 `hello.sessionToken` 与 `refreshToken` 分支未接完；无运行时证据 |
| M1b-2 | 登录入口 | 整改中 | 前端登录卡片已落地；服务端 `refreshToken` 分支、默认密码 gate、Origin/限速未完成 |
| M1b-3 | 基座版本 | 未落地 | 快照无 `basePageVersion`；`PageKey` 未含基座版本；B39 必红 |
| M1b-4 | 协议对齐 | 文档已对齐 | 方案 v1.5 / 矩阵 v1.4 已改；X04 未执行 |
| M1b-5 | 证据链 | 文档已补齐 | 台账/本记录已补；**矩阵 248 条仍未执行** |

**M1c 退出条件**：五条 blocking 全部有可判定证据 + **矩阵 B 阶段 M1 门槛的 P0 全绿**（B20 按 10 图元 × 10 客户端；B26 归 M3、B35/B37 归 M1.5/M2、B33b 归 M1.5）+ 回归无红 + 台账齐全 + 用户验收。

---

## 7. 验证与未决问题

**已验证（有证据）**：

- 服务端 19 条探针（监听、静态、ticket、hello、snapshot、frame、page、pong、E_UNSUPPORTED、stopped、新运行、停服）两次独立运行 19/19；
- 全量 `ScadaChecks` 2266/0（03:05 批次，日志 hash 锚定）；
- 浏览器 10 渲染器在 1920×1080 的截图（03:15–03:26，前端实测）。

**未验证（不许写成已验证）**：

1. `sessionToken` 重连、登录卡片端到端、默认密码 gate、Origin/限速；
2. `BasePageVersion` / `PageKey` 含基座版本；
3. `DisplayFormat` 帧文本格式化；
4. 动画/闪烁、坏值角标、`/img` 与 `imageToken`；
5. 矩阵 248 条任何一条；
6. 10 图元 × 10 客户端 10Hz 的延迟/UI 占用（B20）；
7. 与 WPF 的逐块像素对照（B34）与派生量 DP 对照（B33b）。

**未决问题**：

- `app.js` 仍兼容 `message.kind` 读取（防御），是否在 M1c 删除由实现决定；服务端只发 `type`。
- 浏览器实测脚本在临时目录、未入仓；M1c 应把它移进 `Web\ScadaWebE2E\` 并留运行日志，否则证据链仍不完整。
- `_m1a_run.log` 对应的 DLL 已被重建；若要"同轮 DLL + 日志"双锚定，M1c 需按"构建 → 立即跑 → 记录 DLL hash"重跑一次。

---

## 8. 变更记录

| 日期 | 内容 |
| --- | --- |
| 2026-10-21 | 建立 M1b 记录：as-built `type` 协议、9 条口径裁定、代码/文档改动清单、19/19 与 2266/0 证据（日志 hash）、浏览器 10 渲染器/1920 截图、罐体修复、双评审 FAIL 与五条 blocking、未验证清单 |
