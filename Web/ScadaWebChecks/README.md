# ScadaWebChecks — SCADA 运行态 Web 协议层断言

把 M1b 的临时探针 `_WebProbe6` 提升为仓内正式断言工程，满足“每轮补齐测试”的交付要求。

> **这是协议层断言，不是浏览器 E2E。**
> 它起一个真实的 `ScadaWebHost`（随机端口、假 hub / 假值源），用 HTTP 与 `ClientWebSocket` 打它，
> 校验消息字段、状态码与版本推进。**不渲染像素、不执行 `wwwroot` 里的 JS、不验证视觉效果。**
> 视觉/派生量门槛见测试矩阵 B33/B34（未覆盖，见下文）。

---

## 1. 运行

```bat
dotnet run --project Web\ScadaWebChecks\ScadaWebChecks.csproj
```

- 先 `build` 再 `run`，**不要加 `--no-build`**（R65：断言源码改过而跑旧二进制会静默少几条）。
- 输出每行 `[PASS]/[FAIL] <ID>  <明细>`，最后一行固定：

  ```text
  RESULT: N PASS / M FAIL
  ```

- 退出码：全通过 `0`，任一失败 `1`（`Program.Finish()`）。
- 端口随机（`TcpListener` 取空闲端口），不占用 8080，可与正在运行的桌面端共存。

---

## 2. 覆盖的矩阵 B 用例

工程共 **33 条**断言：19 条 M1b 基线（B1–B19，与 `_WebProbe6` 同名同义）+ 9 条 M1c 新增（M1c-01–09）
+ 5 条 M1 门槛新增（B20/B21/B23/B24/B36）。

| 断言 | 矩阵用例 | 覆盖内容 / 程度 |
| --- | --- | --- |
| B1 | A/B 启动 | 起服务（随机端口）、WS 已启用 |
| B2–B4 | B1（基础页） | `/`、`/app.js`、`/styles.css` 原样字节 + 正确 Content-Type；`/app.js` 不是 base64 |
| M1c-07 / M1c-08 | B1（基础页） | `/elements-process.js`、`/elements-process.css` 200；带 `?v=4` 也 200 |
| B5 | B1（未登录访问） | 白名单外路径（含 `..%2F` 穿越）一律拒绝，不吐磁盘文件 |
| B6 | B9 前置 | `ScadaRuntime.Start()` |
| B7-ticket / B8 | B2（ticket + WS） | `POST /api/ticket` 签发一次性 ticket；WS 握手成功 |
| B9 | B2 / B9 | 首帧 `hello`，`readOnly=true` |
| M1c-01 | B2（M1c 扩展） | `hello.sessionToken` 非空（8 小时可复用的重连凭据），`sessionExpiresIn > 0` |
| B10 | B9 / B16（部分） | 握手后 `snapshot`：10 个元素、元素带 `Bindings` |
| M1c-09 | B33 前置（部分） | `snapshot` 里 10 个 `Hmi.*` 图元全部出现，`ElementId` 与种子逐条一致 |
| B11 / B12 | B17（值帧） | `frame.items` 数 = 10；Text/Number/Bool/Color 四类各一例 |
| B13 | B17（值帧） | 改值后下一帧跟着变（值帧不缓存） |
| B14 | B14 / B15（部分） | 改本页 `Version` → 收到 `page`（不是 frame/snapshot） |
| B15 / B16 | B10（只读拒绝） | `ping→pong`；未知 kind → `error/E_UNSUPPORTED` |
| B17 | B11（RunStopped） | hub 停止 → 收到 `stopped` |
| B18 | B13（重连恢复，部分） | 再次运行 → 重新收到完整 `snapshot`（不是 `page`） |
| M1c-05 / M1c-06 | B29 / B39（部分） | 基座页 `Version` 变化 → 收到 `page`，`basePageVersion` +1，本页 `pageVersion` 不变 |
| M1c-04 | B2 / B13（单次语义） | 已核销的旧 ticket 再次握手 → 401 |
| M1c-02 / M1c-03 | B12 / B13（重连，部分） | 用 `hello.sessionToken` 作 `refreshToken` 调 `POST /api/ticket` 换新 ticket；用新 ticket 重连成功并收到 `hello` |
| B19 | A/B 退出链 | `StopAsync` 幂等（连续两次），停服后 `IsListening=false` |
| B20 | B20（10Hz 节奏） | 同一条连接连收 20 帧（跳过 `page`/`pong` 等非 frame）：到达间隔 median 70–140ms、p95 ≤ 200ms、`seq` 严格递增；实测 median/p95 打进明细 |
| B21 | B21（两客户端） | 两个 ticket 建两条 WS：首屏 `snapshot` 元素数一致（10/10）；改假值源 → 两边都收到对应 `frame`；改页 `Version` → 两边都收到 `page` |
| B23 | B23（关标签） | 关闭客户端 A 后 B 仍收到下一帧，`hub.IsRunning` / `runtime.IsRunning` 仍为 true，服务端在线数降到 1（关标签不停内核） |
| B24 | B24（停运行） | `hub.Stop()` → B 收到 `stopped`；随后 300ms 内不再有 `frame`（先给 250ms 让在途帧落地）；`hub.IsRunning=false`；再 `Stop()` 一次不抛 |
| B36 | B36（未实现图元） | 假页加 `TypeKey="Hmi.Unsupported"`（含 Name/几何）→ `snapshot` 仍包含它、元素数 10→11；客户端占位盒由 `app.js` 负责，本项只验协议不丢 |

### M1 门槛用例的口径（断言实际使用的形状）

- **B20 的 `seq`** 是宿主级单调计数（`snapshot`/`page`/`frame` 共用），所以只断言相邻 `frame` 的
  `seq` 严格递增，**不断言连续**；帧间隔用到达时刻算，不靠固定 `sleep` 判"没收到"
  （每条都是带超时的 `ReceiveAsync`，总期限 = 帧数 × 250ms + 3s）。
- **B24 的"不再出帧"分两步判**：先给 250ms 让**停止瞬间的在途帧**落地（会话队列是 FIFO，
  那一帧会排在 `stopped` 之后），再要求随后 300ms 静默——判的是"停了之后不再持续出帧"，
  不是"停止瞬间一帧都没有"。
- **B36 只验服务端协议**（`snapshot.Elements` 里保留未知 `TypeKey`），不验客户端占位盒
  （占位盒由 `wwwroot\app.js` 渲染，属于浏览器 E2E）。

### M1c 协议口径（断言实际使用的形状）

- 首次 `POST /api/ticket`（空体 / 匿名）**响应里没有** `sessionToken`：会话令牌在 ticket 核销时才签发，
  经 `hello.sessionToken` 下发。
- 刷新走**同一个入口**：`POST /api/ticket`，请求体 `{"refreshToken":"<hello.sessionToken>"}`；
  响应回新 `ticket` 并带回同一枚 `sessionToken`（续期不轮换令牌）。
- `snapshot` / `page` 顶层带 `basePageVersion`（`int`；无基座时为 0）；页键是
  `pageId@pageVersion@basePageVersion`，所以“只改基座”也会推一条 `page`。
- `M1c-05/06` 在 B18 之后才建立基座（先加基座页、注册、让本页引用，吃掉由此产生的一条 `page` 作为基线，
  再只改基座 `Version`）。这样不会影响 B10–B18 的既有断言。

---

## 3. 当前结果（2026-10-21 实测）

```text
RESULT: 33 PASS / 0 FAIL
退出码：0
```

关键明细（令牌/票据为随机值，下表只写可复算的判据）：

| 断言 | 实测 |
| --- | --- |
| M1c-01 | `sessionToken` 非空（32 hex），`sessionExpiresIn=28799` |
| M1c-02 | `POST /api/ticket {"refreshToken":…}` → 200；新 ticket 与旧 ticket 不同；响应 `sessionToken` 与 `hello.sessionToken` 一致 |
| M1c-03 | 用新 ticket 重连成功，收到 `hello` |
| M1c-04 | 旧 ticket 再次握手 → `WebSocketException: ... status code '401'` |
| M1c-05 | 改基座 `Version` 2→3 后收到 `page`（`basePageVersion=3`） |
| M1c-06 | `pageVersion=12` 不变，`basePageVersion` 2→3 |
| M1c-07 / M1c-08 | `elements-process.js` 39628 字符、`elements-process.css` 4461 字符；带 `?v=4` 同为 200 |
| M1c-09 | `elements=10/10 allHmi=True idsMatch=True`，distinctTypes=`Hmi.Gauge,Hmi.Indicator,Hmi.Rectangle,Hmi.Tank,Hmi.Text,Hmi.Valve` |
| B20 | `frames=20 median=110.1ms p95=112.1ms min=107.8ms max=112.1ms seq=10..29 单调=True`（代表性一跑；归档 `_m1c_webchecks33b.log` 为 median 110.2ms / p95 111.8ms / seq 10..29；连跑三次 median 109.8–110.1ms、p95 111.2–112.1ms） |
| B21 | `elementsA=10 elementsB=10 一致=True frameA=True frameB=True（温度 12.5→77.5） pageA=True pageB=True（pageVersion→13）` |
| B23 | `关闭 A 后 B 收到下一帧=True hubIsRunning=True runtimeIsRunning=True online=1` |
| B24 | `stopError=<none> stopped=True 在途帧=False 300ms静默=True hubIsRunning=False reStopError=<none>` |
| B36 | `added=True restarted=True elements=11（期望 11） found=True name=True geometry=True` |

> 本轮产品代码是**并发编辑**状态：首次构建时 `ScadaWebHost.cs` 还停在半成品（`ShortToken` 未定义，CS0103），
> 待其落定后重建通过。期间有一次中间态运行让 `M1c-05/06` 变红（基座页版本推进尚未生效），
> 最终态复跑 28/0——上表是最终态结果。

> 2026-10-21 补入 5 条 M1 门槛用例（B20/B21/B23/B24/B36）后连跑三次均为 `33 PASS / 0 FAIL`。
> 变异反证：把 B20 的中位数区间改成 200–240ms、B36 的期望元素数改成 `+2` → 两条准确变红
> （`31 PASS / 2 FAIL`），还原后复绿——证明这两条断言真的在执行判据，不是恒真。

> `M1c-09` 的种子页是 `_WebProbe6` 原有的 10 个元素（6 种 `Hmi.*` 类型），
> 不是“10 种不同图元”。10 种 M1 图元的派生量与视觉对照属于 B33/B34，未覆盖。
>
> `sessionToken` / `BasePageVersion` 在本轮运行前已由产品代码落地，因此 9 条 M1c 断言全绿。
> 断言本身按“字段缺失即 FAIL、不跳过”编写；产品回退时这些条目会如实变红。

---

## 4. 未覆盖项（明确不做，避免误读为“M1 全绿”）

以下矩阵用例**不在本工程内**，需要浏览器 E2E、性能夹具、多客户端或桌面端联动：

`B27`、`B33`、`B34`、`B35`、`B37`、`B39`、`B40`

补充说明：

- **B20/B21/B23/B24/B36 已按协议层覆盖**（见 §2）：10Hz 节奏、两客户端共享页面、关标签不停内核、
  停运行后不再出帧、未实现图元不被服务端丢弃。**仍未覆盖的是它们的端到端/桌面侧**：
  B20 的浏览器实际帧率与渲染耗时、B21 的“两个浏览器 + 桌面三端画面一致”、
  B23/B24 的桌面运行窗口 `Unloaded` 恰好一次、B36 的客户端占位盒。
- **B27**：重连风暴（多客户端并发断连/重连）——需要故障注入夹具。
- **B33/B34**：10 图元派生量对 WPF 只读 DP、逐块视觉 diff——需要 STA 离屏控件与 Playwright。
- **B35**：坏值保留 + 诊断角标——需要真实渲染器与 DOM/像素检查。
- **B37**：`imageToken` 图片鉴权负面——`/img` 仍是固定 404 占位。
- **B39**：本工程只覆盖“基座版本变化 → `page` + `basePageVersion`”，**不覆盖** 300ms 防抖、
  “所有引用该基座的页面都重建”、不触发 `Loaded`、不整页闪烁。
- **B40**：晚订阅者首帧（模块在运行中加载）——需要走 `ScadaWebModule` 装配路径，本工程直接 `new ScadaWebHost`。

其它未列入上表但同样未覆盖的：B12/B13 的真实断网重连（本工程只覆盖 ticket 刷新/重连握手）、
B18/B19 的客户端丢帧与版本跳变处理（客户端行为，不在服务端协议层）。

---

## 5. 工程约束

- 位置：`Web\ScadaWebChecks\`（**不**放 `Plugins\` / `Modules\`，不参与 `pack.ps1`）。
- 引用：`Web\VisionMaster.ScadaWeb`、`Core\VM.Core`、`Scada\VM.Scada`；**不引用 `VisionMaster.exe`**。
- Watson 五个 DLL 以 `Private=true` 自带一份（Web 工程里是 `Private=false`，模块从宿主目录解析）。
- 静态资源兜底投递 `wwwroot\**\*`（`ScadaWebHost.ResolveStaticFile` 的第二个候选根）。
- 已通过 `dotnet sln add ... --solution-folder Web` 加入 `VisionMaster.sln`（`Web` 解决方案文件夹）。
- 断言只依赖公开契约（`ScadaWebHost` / `ScadaWebSettings` / `ScadaDocument` / `ScadaRuntime` / `ScadaElement`），
  不反射产品私有成员。
