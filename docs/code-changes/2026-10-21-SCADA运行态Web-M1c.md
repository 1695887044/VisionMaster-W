# SCADA 运行态 Web 客户端 M1c（2026-10-21）

> 范围：M1b 双评审五条 blocking 的整改——可重连会话令牌、登录入口、基座版本、协议 as-built、证据链；外加 M1c 复评抓到的“结构消息发送失败但页键已推进”静默冻结修复。
> 结论：**五条 blocking 已关闭；M1 只读核心 PASS；矩阵 248 条仍未执行，M1 验收未收口；M2 可开工。** 不写“M1 全绿”。

---

## 1. 改动清单

| 类别 | 改动 |
| --- | --- |
| 重连 | `ScadaWebTicketService` 新增 `ScadaWebSessionGrant`（32 hex / 8h / 可复用 / 不轮换）；ticket 仍 30s + 单次；`hello` 下发 `sessionToken` + `sessionExpiresIn`；`POST /api/ticket {refreshToken}` 换新票；旧票复用仍 401；`Stop()` 清空 ticket 与 session token |
| 登录入口 | `wwwroot` 无 ticket 时显示登录卡片（用户名/密码/登录/匿名只读）；`sessionStorage` 存 ticket/sessionToken；401/`E_SESSION_EXPIRED` 回登录卡片；断线用 refreshToken 换票重连，不复用旧 ticket |
| 基座版本 | `ScadaPageSnapshot` 增加 `BasePageVersion`（无基座 0）；`PageKey = pageId@pageVersion@basePageVersion`；基座版本变化发 `page`，本页 `pageVersion` 不变 |
| 格式串 | 帧 `text` 使用 `Binding.DisplayFormat`（`IFormattable` + InvariantCulture）；`Gain/Offset/FormatType` 留 M1.5 |
| 静态路由 | `/elements-process.js`、`.css` 与带 `?v=` 的请求均 200；白名单外仍 401 |
| 结构同步 | `ScadaWebSession` 出站队列 `FullMode=Wait`（`TryWrite` 满时返回 false）；`PumpFrameAsync` 只有 `TrySend` 成功才推进 `CurrentPageKey`，失败下一拍重试；`stopped` 发送失败直接断开慢消费者 |
| UI | `MIN_SCALE=0.8`（1280 下最小缩放 + 滚动）；`#demo-chip`“演示数据” + 水印；演示棒图 `Fill=#0E7490` + 白字（5.36:1）；罐体深色底带 + 长位号 ellipsis |
| 测试 | `Web\ScadaWebChecks` 正式工程（已入 `VisionMaster.sln`）；33 条协议断言，含 B20/B21/B23/B24/B36 |
| 文档 | 方案 v1.6、矩阵 v1.4、台账 §2.7、`已知偏差.md` 11 条、本记录 |

## 2. 证据

| 项 | 结果 | 证据 |
| --- | --- | --- |
| `Web\ScadaWebChecks`（Release） | **33 PASS / 0 FAIL** | `_m1c_webchecks33b.log`（SHA256 前 12 `C75F4FE9AF80`，28594 B，08:57:29）；DLL `71B953980241`（67584 B，08:57:17） |
| `_WebProbe6`（Debug + Release） | **31 PASS / 0 FAIL** | Agent 报告；仓内未留该轮日志，只锚定源码/产物时间 |
| `ScadaChecks`（Release） | **2276 通过 / 0 失败** | `_m1c_sc.log`（SHA256 前 12 `CCB425F345BD`）；DLL `54A1F9527A99` |
| B20 10Hz | median 110.2ms / p95 111.8ms / 20 帧 / seq 10..29 | `_m1c_webchecks33b.log` B20 |
| B21 双客户端 | 两客户端 snapshot 元素数一致；改值/改页两边都收到 | `_m1c_webchecks33b.log` B21 |
| B23 关标签 | 关 A 后 B 仍收帧；hub/runtime 仍 running；在线数 1 | `_m1c_webchecks33b.log` B23 |
| B24 停运行 | `stopped`；300ms 无 frame；hub false；重复 Stop 不抛 | `_m1c_webchecks33b.log` B24 |
| B36 未实现图元 | snapshot 元素 10→11，含 `Hmi.Unsupported` | `_m1c_webchecks33b.log` B36 |

## 3. 未决问题

1. **矩阵 248 条仍未执行**：`33/33`、`31/31`、`2276/0` 都不是矩阵用例；B33/B34/B39/B40 仍为“部分”，B35/B37 归 M1.5/M2。
2. **M2 前必须补**：`/api/ticket` 默认密码 gate、Origin 校验、每 IP 限速/锁定；匿名 viewer 的 8h 令牌在 `AllowAnonymousView=false` 后仍可续期；令牌不可单独撤销；`RequireLogin` 目前无消费者。
3. **性能**：实测 10Hz 约 9.1Hz（`Delay(100)` + 拍一帧），建议 `PeriodicTimer`；10 客户端 60s 压测与 1000 图元归 M3/E。
4. **E2E**：`Web\ScadaWebE2E` 仍未建；M1c 浏览器截图来自 Agent 实测，未入仓运行日志。
5. **小项**：`/img` 文案仍写 M1b；`ReadRunSnapshot` 在 IO/WS 线程读 `CurrentPage.Version`（只读标量）；`app.js` 匿名按钮 401 隐藏后未重置。

## 4. 结论

M1b 五条 blocking 全部关闭；M1 只读核心在“10 图元 / 10Hz / 10 客户端 / 只读镜像”口径内 PASS。M2 可开工，但第一个迭代必须先处理未决问题 2 的三项安全前置，否则写路径一开就会把只读假设变成可利用面。
