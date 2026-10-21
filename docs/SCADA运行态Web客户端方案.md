# SCADA 运行态 Web 客户端方案说明书（M0–M3）

> 状态：**M1c 收尾——M1 只读核心 PASS、M1 验收矩阵未全跑**。证据：`Web\ScadaWebChecks` **33 PASS / 0 FAIL**（`_m1c_webchecks33b.log`，协议层断言宿主，已入 `VisionMaster.sln`）、`_WebProbe6` **31 PASS / 0 FAIL**（Debug + Release，Agent S 报告）、全量 `ScadaChecks` **2276 通过 / 0 失败**（`_m1c_sc.log`）。M1b 五条 blocking 已在 M1c 关闭（§17.5）。**矩阵 261 条仍未执行**，所以**不写"M1 全绿"**；M2 可开工，M1 全量矩阵待补。M0.2 的结论仍见 §17.1（`B40` / `A01–A09` / `F23` / `F24` 的运行时证据仍欠，M1c 只补了 B40 首帧与 F23/F24 的换票/旧票 401）。执行台账见 `docs\SCADA运行态Web客户端\结果台账.md` §2.7。
> **M2a 状态（2026-10-21 12:33）**：**地基 PASS（有条件）**——`Web\ScadaWebChecks` Release **97 PASS / 0 FAIL**（33 基线 + 4 静态资产 + 11 动作过滤 + 19 安全 + 26 命令 + 4 回归；日志 `_m2a_webchecks13.log` `AA8A43733714`，DLL `E91456D97780`），`ScadaChecks` Release **2332 通过 / 0 失败**（`_m2a_scadachecks3.log` `92F0CF616A04`），Release 全量重建 0 错误且符号已核。**两轮终审 FAIL 后六条 P0 + 第二轮 P0-1/P1-1 已关闭；C68–C80 未全执行（C68/C69/C70/C72 已于 13:19 补跑，见 §17.7），`AllowCommands=false` 不变。** 真实浏览器 E2E、WS 4403、`E_CAPACITY` 握手、租约、`caps` 热更新仍无运行时证据。**`AllowCommands` 保持默认 `false`，写路径不对现场开放；M2b 可开工（§17.7）。** 第一轮 89/89（`_m2a_webchecks3.log` `CF142B22F99A`）为历史证据。**（补记：真实浏览器双角色 E2E 与租约已于 13:06/13:25 补证（HighestRoleOnly + AllOperators 两种策略）、C68/C69/C70/C72 于 13:19 补跑，见 §17.7「M2b 进展」；前面「仍无运行时证据」「未全执行」是 12:33 时点状态。）**
> **A′ 第一批（2026-10-22，当前生效口径）**：按 R1–R3 三轮评审落地——多基座（服务端遍历 `BasePages` + 客户端逐层渲染）、结构键/快照缓存（runGeneration/页面标量/有序基座/弹窗）、增量值帧 + keyframe（baseline-on-success）、帧预算（约 45KB/帧/会话，配置 120KiB 会被夹取）、A 范围（硬排除 `StartFlow/RunFlowOnce/ResumeFlow`，Web 轴只读）、45 秒死连接（双条件）、渲染器覆盖度闸。证据：`Web\ScadaWebChecks` **119/0**、`ScadaChecks` **2438/0**。**已封**：Web 写变量触发流程（`IScadaFlowTriggerQuery` + `E_FLOW_TRIGGER`，M2-13f/g）。**未做**：`cmd.result`、快照分片、24 槽准入、4 个复杂图元渲染器（ChartTrend 已用 Value 帧实现）；详见 `docs\meetings\2026-10-22-SCADA-Web-v4-评审-R3.md` 与 `docs\code-changes\2026-10-22-SCADA运行态Web-A-prime第一批（多基座-结构缓存-增量帧-预算-A范围）.md`。**本文后文出现的“50/100 客户端”“2MiB/120KB”以本块为准：当前生效预算是 45KB/帧/会话，`MaxSessions` 仍 16；3000 图元/16 客户端的压测尚未执行。**
> 起草：2026-10-21 ｜ 最后更新：2026-10-22（A′ 第一批：多基座/结构缓存/增量帧/预算/A 范围/45s/覆盖度闸；见上方状态块）｜ 历史：2026-10-21（v1.8：**M2a 地基 PASS（89/89，有条件）**——安全前置六项与命令泵已接线、Release 已重建并核符号；`AllowCommands=false` 默认、`caps` 2 秒广播、`policyVersion` 取 `Environment.TickCount64`、`AllOperators` 缺项回退 `HighestRoleOnly`、`E_CAPACITY` 取票 503/握手 401、`requestId` 空或超 64 回 `E_BAD_REQUEST`；矩阵 C68–C80 未执行，M2b 门槛见 §17.7；v1.7：**M2a 设计定稿**——安全前置六项（默认密码 gate / Origin / 每 IP 10 次/分钟 / 匿名令牌不续期 / 会话·用户撤销 / 会话表 4096 上限）、命令泵（每会话 32 → 全局 1024 → 无 SyncContext 消费者 → UI 逐条、单条异常隔离、`E_BUSY`）、四条命令与 `caps` 逐图元、六图元只读/禁用/失败反馈、`E_NOT_FOUND`/`E_DEFAULT_PASSWORD`/`E_SESSION_REVOKED`/`E_CAPACITY` 码表、M2a 时序、矩阵 C 新增 13 条；**代码已开始落地但未接线/未验证，§17.6 五条 blocking 未关闭**；v1.6：**M1c 收尾**——`sessionToken`（8h，不轮换）+ `POST /api/ticket {refreshToken}` 换票、登录卡片 / `sessionStorage` / 断线重连、`BasePageVersion` 进快照与 `PageKey`、帧 `text` 用 `DisplayFormat`、`/elements-process.*` 与 `?v=` 静态路由全部落地；`Web\ScadaWebChecks` 33/33、`_WebProbe6` 31/31、`ScadaChecks` 2276/0；**矩阵 248 条仍未执行**，B 阶段 B20/B21/B23/B24/B36 协议层已跑，B33/B34/B39/B40 部分。v1.5：**协议按 M1b as-built 定稿**——`type` + `hello/hello.ok/snapshot/page/frame/stopped/idle/pong/error/bye`，`snapshot.page` 用 PascalCase、`frame.items` 用 camelCase，**没有 `kind/state/caps/view.attach/auth.login/view.resync`**；M2 才扩展 `auth.login/caps/view.attach`。M1 范围收窄为 **10 图元、10Hz、只读镜像、10 客户端**，1000 图元/50 客户端归 M3/E 暴力测试；动画/坏值角标/`/img` 归 M1.5/M2；重连改 **`sessionToken`（8h）+ `POST /api/ticket {refreshToken}`**；基座快照带 `BasePageVersion`、`PageKey = pageId@pageVersion@basePageVersion`；帧 `text` 用 `binding.DisplayFormat` 格式化（Gain/Offset/FormatType 留 M1.5）；登录入口=无 ticket 显示登录卡片；UI 偏差见 `docs\SCADA运行态Web客户端\已知偏差.md`；指标编号 `M1/M1b` 改为 `P-UI/P-CMD` 以避开里程碑 M1b 的同名冲突）
> 上位纪律：`AGENTS.md` §30.1 R132A（交付边界例外）、§4 R10–R15（派工与三段式交接）、R38（跳过型断言必须能被反证）、R42/R62（增量构建与不在 sln 里的工程会静默漏打包）、R55（Debug/Release 都要重建）、R57/R79（只报不拦的检查不进启动链）、R65（改断言宿主后不许 `--no-build`）
> 事实来源：本仓源码、`DLL\Watson\WatsonWebserver.xml`，以及 M0 三个探针（`_WebProbe1\README.md`、`_WebProbe2\README.md`、`_WebProbe3\README.md`）。**行号会随代码演进漂移，契约名与文件路径才是锚点**；凡标【待实测】的，都是 M0 尚未覆盖、必须继续量出来再写进实现的东西。
> **单一事实源**：本文与 `docs\SCADA运行态Web客户端测试矩阵.md` 共用同一套术语、默认值、协议、状态机、权限策略与审计口径；两份文档任何一处冲突都按"先改本文、再同步矩阵"处理，**不许两边各写一套**（v1.6 已逐条对齐：**`type` 协议 + as-built 消息集**、ticket 30s/单次 + `sessionToken` 8h 换票、7 列审计 + `Detail` 前缀且**拼装点只有 `ScadaAuditWriter.Append(string,…)` 一处**、`AllOperators` 监管开关闸门、`MaxSessions=16`/入站 32/帧 120KB/快照 2MiB、`PageKey = pageId@pageVersion@basePageVersion`、presence 只给不透明 `sessionRef`、50/100 客户端 p95 300/500ms）。

---

## 0. 阅读约定

本文把每一条陈述分成三类，混在一起写会让后来者分不清"哪些是现状、哪些是决定、哪些还没验"：

| 标记 | 含义 | 处理方式 |
| --- | --- | --- |
| 【事实】 | 本仓源码/第三方库文档里能直接读到的东西 | 附 `file:line`，改代码时同步改本文 |
| 【决策】 | 四角色评审 + 用户已拍板，实现必须照做 | 偏离要走 R125（记录差异 → 评审 → 用户确认） |
| 【待实测】 | 现在无法从源码断定，必须由探针/真机量出来（M0 已覆盖的见 §16；未覆盖的见 §16.7） | **不许在实现里写死猜测值**；实测结论回填本文并留证据 |

---

## 1. 目标与非目标

### 1.1 目标

1. **桌面端设计组态，点「运行」，局域网用户用浏览器看同一页。** 浏览器不是第二个设计器，也不是第二个运行内核，只是"同一份运行态的另一块屏幕"。
2. **HTTP 服务常驻**：模块启用后随软件启动（不跟"运行/停止"启停）。没点运行时浏览器能打开基础页并看到"等待桌面端开始运行"。
3. **多用户同时在线**：同一时刻多个浏览器跟随同一页；M1 只读，M2 起可在浏览器里操作，M3 补齐图元与复杂件。
4. **镜像模式**：所有浏览器跟随内核当前页。切页只发生在桌面端（内核），一次切页只产生一对 `Unloaded`/`Loaded`。
5. **可治理的多用户操作**：桌面端提供参数，同一界面"所有人均可点击"或"仅最高权限用户可点击"；前者必须把监管方式（串行、审计、在线列表、操作租约）写进实现并纳入测试。
6. **以 WPF 运行窗口为视觉 oracle**：同一屏、同一状态、同一尺寸下逐块对照；派生量对 WPF 只读依赖属性逐条对照。

### 1.2 非目标（明确不做，避免"顺手做了"）

| 不做 | 理由 |
| --- | --- |
| **网页编辑组态**（改结构/改属性/存盘/新建画面） | R132A 的边界。浏览器里任何"改文档"的能力都不许出现 |
| **浏览器切页 / 页面导航** | 镜像模式的直接推论；web 侧 `ScadaActionType.Navigate` 一律跳过并记审计 |
| **每个浏览器一个 `ScadaRuntime`** | 会让 `Loaded`/`Unloaded` 按浏览器数量重复执行、关标签触发停机动作（R132A 原文） |
| **把 Web 当设计基准替代 WPF** | R126 的其余口径不变 |
| **公网暴露 / TLS / 反向代理** | M1–M3 只承诺局域网；TLS 见 §14 开放问题 |
| **移动端适配、PWA、离线缓存** | 工业现场用固定终端；先保证桌面浏览器（Edge/Chrome） |
| **历史曲线库、报警库查询、报表** | 运行态只显示"此刻"；历史是另一条数据通路 |
| **浏览器端本地持久化会话** | 会话令牌只放内存/`sessionStorage`，不落 `localStorage`（见 §8.9） |

---

## 2. 治理例外 R132A 与交付边界

【事实】`AGENTS.md:460-466` 已写入例外：

> 允许把 SCADA **运行态** Web 客户端作为交付物部署到局域网，用于多用户同时监控/操作；**设计态** Web 编辑器仍禁止，R126 的其余口径不变。
> 落地必须走"单一运行内核 + N 视图会话"（禁止每浏览器一个 `ScadaRuntime`），并满足：默认只读、服务端逐命令鉴权、审计带操作者/会话/IP、运动命令默认禁用。

### 2.1 允许 / 禁止清单（实现期按这张表审）

| 能力 | 允许 | 说明 |
| --- | --- | --- |
| 查看当前运行页（含基座页、图层可见性；动画是 M1.5） | 是 | M1（静态 + 值帧） |
| 登录 / 登出 / 查看在线用户 | 是 | M1 |
| 写变量、执行图元事件动作、流程启停 | 是（M2） | 逐命令鉴权 + 审计 |
| 报警确认 | 是（M2） | 走 `ScadaAlarmEngine.Acknowledge` |
| 运动命令（使能/定位/回零/点动/清警） | **默认禁用** | `ScadaWeb.AllowMotionCommands=false`；打开需现场明确配置 |
| 切页 / 返回上一页 | **禁止** | 镜像模式；web 侧跳过 `Navigate` 动作 |
| 改图元属性 / 拖拽 / 改尺寸 / 增删图元 / 存盘 | **禁止** | 这是设计态能力 |
| 新建/删除画面、改图层、改报警定义 | **禁止** | 同上 |
| 在浏览器里改账号/改密码 | 不在本方案范围 | 账号管理留在桌面端（M1 只做登录） |

### 2.2 交付物形态

- 源码：`Web\VisionMaster.ScadaWeb\`（第一方工程，进 `VisionMaster.sln`）。
- 产物：`<publish>\Web\VisionMaster.ScadaWeb.dll` + `<publish>\Web\wwwroot\` 下 5 个静态文件（M1b 是**磁盘 Content**，不是内嵌资源；Watson 等第三方依赖沿用主程序基目录，见 §5.2）。
- **不放 `Plugins\`**（红线①：`Plugins\Directory.Build.targets` 会把 DLL 当插件投递到 `Modules\`）。
- **不放 `Modules\`**（红线②：`Modules\` 只放 `Plugin*.dll`；把 `VM.Core`/`VM.Scada` 等公共契约拷进去会出现两份静态状态）。
- 可选加载：主程序**不编译期引用**本工程；`ScadaWeb.Enabled=false` 时连 `Assembly.LoadFrom` 都不做。

---

## 3. 总体架构

### 3.1 一张图

```
┌──────────────────── VisionMaster.exe（桌面端，唯一内核宿主）────────────────────┐
│                                                                                │
│  WPF 设计态：ScadaEditorViewModel / ScadaCanvas（只改文档）                      │
│         │ 点「运行」                                                            │
│         ▼                                                                       │
│  IScadaRuntimeHost.Start(document)   ← 现有契约                                 │
│         │                                                                       │
│  ┌───────┴────────── ScadaRuntimeHost（单例，唯一 owner）──────────────────┐   │
│  │  ScadaRuntime ×1              当前页 / 页栈 / 事件与权限闸门            │   │
│  │  ScadaAlarmEngine ×1          报警定义 / 激活 / 确认 / 历史             │   │
│  │  ScadaVariableEventEngine ×1  变量边沿 → 事件钩子                       │   │
│  │  ScadaBeatSource ×1           200ms 统一节拍（闪烁相位 / 延时判定）     │   │
│  │  ScadaRuntimeBinder ×1        WPF 数据泵（变量 → 图元依赖属性）         │   │
│  │  ScadaAuditWriter ×1          审计落盘（UI 线程单写者）                 │   │
│  │  ScadaFlowRunner ×1           流程启停                                  │   │
│  │  MotionActionExecutor ×1      运动命令执行器（门禁只有一份）            │   │
│  │                                                                         │   │
│  │  新：IScadaRunHub（IsRunning/Session/Document/RunStarted/RunStopped）   │   │
│  └───────┬─────────────────────────────────────────────────────────────────┘   │
│          │ 事件在 UI 线程抛出；web 只订阅，不调用 Start/Stop                    │
│  ┌───────┴──── VisionMaster.ScadaWeb（可选模块，<publish>\Web\）───────────┐   │
│  │  ScadaWebHost（Watson 7.2.0）  /  /app.js  /api/*  /img/*  WS /ws     │   │
│  │  ScadaWebSessionManager          会话表 / 上限 / 空闲回收 / 在线列表    │   │
│  │  ScadaCommandPump                Channel<ScadaWebCommand> → UI 线程逐条    │   │
│  │  ScadaPageSnapshotBuilder        UI 线程构建不可变快照（含基座页）      │   │
│  │  ScadaWebValueResolver           10Hz 共享值帧（所有会话同一份）        │   │
│  │  ScadaWebAuthService             ScadaUserStore.TryValidate（后台）     │   │
│  │  ScadaWebAuditBridge             只在泵里写 ScadaAuditWriter.Append     │   │
│  │  ImageEncoder                    BitmapSource → PNG/JPEG（后台 + 缓存） │   │
│  └───────┬─────────────────────────────────────────────────────────────────┘   │
└──────────┼─────────────────────────────────────────────────────────────────────┘
           │ HTTP（静态资源 / 图片）
           │ WebSocket（JSON 文本帧：状态 / 快照 / 值帧 / 命令 / 回执）
   ┌───────┴───────┬───────────────┬───────────────┐
   ▼               ▼               ▼               ▼
┌───────┐      ┌───────┐      ┌───────┐      ┌───────┐
│浏览器A│      │浏览器B│      │浏览器C│      │浏览器D│
└───────┘      └───────┘      └───────┘      └───────┘
   （每个会话只持有：认证态、连接、视图镜像、有界队列；不持有任何领域对象）
```

> 图里的 `ScadaUserStore.TryValidate` / `ScadaAuditWriter.Append` 写的是**实现类型**；web 实际拿到的是两个跨程序集出口——`IScadaUserAuthenticator` 与 `IScadaAuditSink`（§15.1）。`IScadaRunHub` 的完整成员是六个（含 `RunGeneration` 与带 generation 的两个事件），图里只列了主干。

### 3.2 三条数据流

| 流 | 频率 | 内容 | 谁生产 | 谁消费 |
| --- | --- | --- | --- | --- |
| 结构流 | 切页 / 结构变更（下一拍，≤100ms） | `snapshot`（首次/新一轮）或 `page`（同一轮内 `PageKey` 变化）：页面 + 基座（`page.BasePage`）+ 图元 + 绑定 + 动画；`snapshot.page` / `page.page` 用 PascalCase | UI 线程的 `ScadaPageSnapshotBuilder` | 所有会话（**同一份**，按 `PageKey` 缓存） |
| 值流 | 10Hz（M1b 固定；M1.5 起可配） | `frame`（全量 `items`：`elementId/property/text/number/bool/color`） | UI 线程的 `ScadaWebValueResolver` | 所有会话（**同一份字节**，镜像模式） |
| 图像流 | M1.5 起按变量版本变化 | `GET /img/{elementId}/{version}.{ext}?t={imageToken}` | 后台 `ImageEncoder` | 单个 `<img>`；**M1b 固定 404（`E_IMG_NOT_READY`）** |

**为什么结构流与值流可以"一份广播"**：镜像模式保证所有会话看的是同一页、同一 `PageKey`。M1b **只读**，权限差异不存在（`hello.readOnly=true` 对所有会话一致），所以没有任何"每会话一份"的消息；`caps` 是 M2 的扩展点（§9.3），M1b 不实现。这是本方案最重要的性能取舍：16 个会话不等于 16 份序列化。

### 3.3 三条硬边界

1. **web 不许 `new ScadaRuntime`**，也不许 `new ScadaAlarmEngine` / `new ScadaBeatSource` / `new ScadaRuntimeBinder`。
2. **web 不许直接调 `ScadaRuntime.RaisePageEvent`**：`Loaded` 动作是桌面运行窗口首帧后的语义，web 再报一次就是"开一个页面执行两遍"。
3. **web 不许在非 UI 线程碰领域对象**：`ScadaRuntime` 明确"只在 UI 线程上用，不做加锁"（`Scada\Models\ScadaRuntime.cs:31-32`）；`ScadaAccessPolicy` 同样（`VisionMaster\Services\ScadaAccessPolicy.cs:32`）。审计出口是唯一的例外：`ScadaAuditWriter` 已加 `_writeGate`（任意线程安全，`ScadaAuditWriter.cs:239-248`），但 **web 仍只经命令泵 UI 队列写**——线程安全不等于顺序正确。

---

## 4. 内核 / 视图 / 命令泵 / 快照 / 发布器

### 4.1 内核：`ScadaRuntimeHost`（唯一 owner）

【事实】现有职责见 `VisionMaster\Services\ScadaRuntimeHost.cs` 类注释（`:13-71`）：单实例运行、`Start`/`Stop`、报警引擎、变量事件引擎、节拍、数据泵、导航器、权限出口、审计。`IsRunning => _window != null`（`:271`），`Document => _session?.Document`（`:279`）。

> **2026-10-21 更新**：报警引擎、变量事件引擎、节拍与报警历史已移到方案级 `ScadaMonitoringHost`；
> `ScadaRuntimeHost` 只保留运行会话/窗口/数据泵/导航器。`IScadaRunHub` 仍按“窗口在 = 运行中”，
> 因此 Web 在“窗口关闭但后台监控仍在跑”时会显示未运行。见
> [SCADA 组态与流程解耦（方案级监控）](code-changes/2026-10-21-SCADA组态与流程解耦（方案级监控）.md)。

【决策】`IScadaRunHub`（放 `VM.Scada`，不放在 `VisionMaster`）——**签名以代码为准**（`Scada\Models\IScadaRunHub.cs:20-49`）：

```csharp
namespace VisionMaster.Scada
{
    public interface IScadaRunHub
    {
        bool IsRunning { get; }
        ScadaRuntime? Session { get; }                    // 未运行 = null
        ScadaDocument? Document { get; }                  // = Session?.Document，未运行 = null
        long RunGeneration { get; }                       // 每次成功启动 +1，永不回退
        event Action<ScadaRuntime, ScadaDocument, long>? RunStarted;
        event Action<long>? RunStopped;

        // M0.1 契约（B1）：晚订阅者/运行中才被加载的模块取"当前这一轮"的唯一入口。
        // 运行中 → true 且三个值有效；未运行 → false，session/document 为 null、generation 为 0。
        // 实现必须与 IsRunning/Session/Document 在同一线程（UI）上**原子地取一次**，
        // 不许拆成"先读 IsRunning 再读 Session"两次读（中间可能刚好 Stop）。
        bool TryGetCurrent(out ScadaRuntime? session, out ScadaDocument? document, out long generation);
    }
}
```

> **`TryGetCurrent` 的落地状态（M0.2 已改）**：契约与实现**都已落地**——`IScadaRunHub.cs:50` 声明，`ScadaRuntimeHost.cs:285` 实现（UI 线程上原子取一次；未运行返回 `false` 且三个 out 为 `null / null / 0`）。RH10 / RH11 / RH13 已分别钉住"未启动 / 运行中 / 停止后"三条判据；**B40（晚订阅首帧）仍待跑**——它需要监听器与 web 模块，已移到 **M1 首周**（§17.1）。

`ScadaRuntimeHost` **同时实现** `IScadaRuntimeHost` 与 `IScadaRunHub`；`App.xaml.cs:319-321` 已是"同一单例"三条映射（`IScadaAuditSink` 在 `:309`、`IScadaUserAuthenticator` 在 `:403`）：

```csharp
containerRegistry.RegisterSingleton<ScadaRuntimeHost>();
containerRegistry.RegisterSingleton<IScadaRuntimeHost>(c => c.Resolve<ScadaRuntimeHost>());
containerRegistry.RegisterSingleton<IScadaRunHub>(c => c.Resolve<ScadaRuntimeHost>());
```

**为什么不在 `IScadaRuntimeHost` 上加事件**：`ScadaChecks` 里有 `FakeRuntimeHost` 实现该接口，加成员会让断言工程编译不过；而且 `IScadaRuntimeHost` 的语义是"编辑器让运行态开/停"，不是"旁观者订阅运行态"。

**事件抛出时机（四条都不许改，M0.1 按代码定稿）**：

| 事件 | 抛出点（代码锚点） | 约束 |
| --- | --- | --- |
| `RunStarted(session, document, generation)` | `ScadaRuntimeHost.Start` 里 `StartAlarms` / `StartVariableEvents` / **`window.Show()` 全部成功之后**（`ScadaRuntimeHost.cs:375-391`：`:380` 是 `Show()`，`:390-391` 才 `TryStarted` + 发布） | ① `Show()` 抛异常 → 走 `AbortStart` + `OnWindowClosed` 收尾，**一次都不发**；② `TryStarted` 返回 false（重复点「运行」）→ **不发**且 generation 不递增；③ 发布前 generation 已落（订阅方在处理器里读 `hub.RunGeneration` 必须拿到本轮号）；④ 发布前 `CurrentPage` 已就绪（`ScadaRuntime.cs:284-290`），web 在处理器里**立刻取 `Session.CurrentPage` 建首帧快照** |
| `RunStopped(generation)` | `OnWindowClosed` 收尾**末尾**（`ScadaRuntimeHost.cs:1006-1019`）与 `Stop()` 的兜底分支（`:404-416`） | ① **必须在 `_session = null; _window = null;` 之后发**——处理器里 `hub.Session` 与 `hub.Document` **都是 null**，要"最后一帧"必须用自己缓存的快照；② `TryStopped` 返回 false（未启动 / 启动失败 / 重复 Stop）→ **不发**；③ 幂等：同一轮恰一次 |
| 页面切换 | **不新增**；web 订阅 `session.PageLoaded` | 见 §7 |
| 晚订阅 | `TryGetCurrent(out session, out document, out generation)` | 运行中订阅的模块用它建首帧；返回 false 时按 `NotRunning` 处理（§6.2） |

**web 的缓存纪律**：`RunStarted` 时缓存 `(session, document, generation)` 三个值；`RunStopped` 之后**不许再读** `hub.Session` / `hub.Document`（读到的一定是 null），只用自己的缓存收尾并推 `stopped{runGeneration}`。

### 4.2 视图会话：`ScadaWebSession`

每个浏览器一个，**只持有**：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `SessionId` | `string` | 服务端生成（32 hex），进日志与审计 `说明` 列的 `[web s=…]` 前缀；**只对服务端可见，绝不下发给其它会话** |
| `SessionRef` | `string` | **不透明**的对外标识（16 hex，随连接生成、断开即废），只用于 `presence` 与界面"我在看谁"；**不可用于任何命令/审计/鉴权**，也反查不出 `SessionId`（§9.3） |
| `ConnectionId` | `Guid` | Watson `WebSocketSession` 标识 |
| `UserName` / `Role` / `IsAuthenticated` | `string?` / `ScadaRole` / `bool` | 认证态；未登录 `Role=Operator` 但 `IsAuthenticated=false` |
| `RemoteIp` / `UserAgent` | `string` | 审计用；IP 取 Watson 连接的远端地址；`(ua, ip)` 同时是 resume 的绑定键（§8.9） |
| `LastSeenUtc` / `ConnectedAtUtc` | `DateTime` | 空闲回收 |
| `ViewMirror` | `{ runGeneration, pageId, pageVersion, basePageVersion, pageKey, lastSeq }` | 服务端记住的"这个会话已经拿到哪一版"；M1b 的实现字段是 `CurrentPageKey`（`pageId@pageVersion@basePageVersion`），`basePageVersion` 是 M1c 目标字段（§9.4） |
| `Inbound` | **M2a：每会话入站配额 32（16–64）**——as-built 由 `ScadaWebCommandPump` 的 `_pendingBySession` 计数实现（不是每会话一条 `Channel`），入全局 `Channel` 前先占配额，超了回 `E_BUSY` | M1b 只读：没有命令泵，收到非 `ping`/`hello` 的帧回 `error{E_UNSUPPORTED}`（§9.2） |
| `Outbound` | 有界队列 256；**M1b 满则丢新帧**（值帧可丢，`DroppedCount` 计数）；M2 命令帧改"满则显式 `E_BUSY`/断开" | 慢消费者保护；丢帧不改变页面结构 |
| `PolicyVersion` | `long` | M2 的 caps 重算依据；M1b 不实现 |
| `LeaseState` | `None / Holder / Requester` | 见 §8.6 |
| `ImageToken` | `{ value(32 hex), expiresAtUtc }` | **M1.5 字段**：按会话签发、30–60s（`ImageTokenSeconds` 默认 45）、可轮换；只用于 `/img` 的 query，**不是**会话令牌、不参与 WS 鉴权（§6.1/§8.9）。M1b `/img` 一律 404 |

**不持有**：`ScadaRuntime`、`ScadaDocument`、`ScadaAlarmEngine`、任何 WPF 控件、任何 `IScadaValueHandle` 的长期订阅（值订阅由共享发布器持有，见 §4.5）。

### 4.3 全局单命令泵（M2 扩展点）

> **M1b 不实现命令泵**：M1b 是只读镜像，WS 接收循环只认 `ping` 与 `hello`，其余 `type` 一律回 `error{E_UNSUPPORTED}`（`ScadaWebHost.HandleTextFrame`）。本节从 M2 起生效，保留原设计是为了 M2 不再重新裁定"单读、单执行、审计顺序"。

```
浏览器 A ─┐  每会话入站队列 32（16–64，FullMode=Wait）
浏览器 B ─┼─► Channel<ScadaWebCommand>（全局，容量 1024，单读）
浏览器 C ─┘        │
                   ├─ 消费者：线程池上的 `Task.Run`，**没有 SynchronizationContext**
                   │    （起在 UI 线程会让 await 续体投回 UI，泵退化成"UI 线程上的循环"，§16.4）
                   │
                   └─ 每条：`await _dispatcher.InvokeAsync(() => Execute(cmd))`（逐条、全序）
                           ├─ 路由层三闸/速率 + 权限层四道 + 确认

                          ├─ 领域调用（ScadaRuntime / 值写入 / 报警确认）
                          ├─ 审计（命令级 1 行；动作级由 dispatcher 写）
                          └─ 结果回执（ack / error），单条异常 → E_INTERNAL 且泵继续
```

**两段队列的分工（M2a）**：每会话入站队列 **32** 只做"连接级背压"（`TryWrite` 失败即 `E_BUSY`，不阻塞 WS 接收循环）；全局 `Channel` **1024** 是"跨会话全序"的唯一入口（单消费者 = 命令全序 = 审计顺序）。两级都**禁用** `DropWrite`/`DropOldest`——入站命令丢掉就是"按了没反应"，而 `seq` 缺口只适用于服务端→客户端的值帧（§16.4）。

【决策】命令对象（as-built：`Web\VisionMaster.ScadaWeb\ScadaWebCommand.cs`，不可变，入队后任何线程不改）：

```csharp
public sealed class ScadaWebCommand
{
    public string Kind { get; init; }             // cmd.element / cmd.input / cmd.alarm.ack / cmd.confirm.request
    public string RequestId { get; init; }        // 只用于回执配对（M2a 无幂等去重，见规则 6）
    public ScadaWebSession Session { get; init; } // 身份/只读/IP/回执通道都从这里取（不采信客户端字段）
    public string? ElementId { get; init; }       // Guid "D"；cmd.alarm.ack 的"全部确认"可为 null
    public ScadaEventType? Event { get; init; }   // cmd.element
    public string? Property { get; init; }        // cmd.input（默认 "Value"）
    public string? Value { get; init; }           // cmd.input 原文（不做 trim/预转换）
    public string? PageId { get; init; }          // 三闸（验收要求必填）
    public int PageVersion { get; init; }
    public long RunGeneration { get; init; }
    public string? ConfirmToken { get; init; }    // cmd.element 的危险动作
    public Guid? AlarmId { get; init; }           // cmd.alarm.ack
    public bool All { get; init; }                // cmd.alarm.ack：确认全部
    public DateTime EnqueuedAtUtc { get; init; }  // 泵算排队耗时（>2s 记 Warn）
}
```

**执行规则（十条）**：

1. **单读、单执行**：一个 `Channel` 一个消费者，命令之间**全序**；审计顺序 = 执行顺序。泵必须跑在**没有 `SynchronizationContext` 的线程**（线程池）上——把 `await foreach` 起在 UI 线程会让续体被投回 UI，泵退化成"UI 线程上的循环"（§16.4）。
2. **只在 UI Dispatcher 上执行**：`await _dispatcher.InvokeAsync(() => Execute(cmd))`；执行体内不许 `await` 长任务。
3. **请求级超时用 `WaitAsync`，不用取消 token 截断等待**：`await op.Task.WaitAsync(timeout)`（`catch (TimeoutException)`）给 Web 请求明确答复；`InvokeAsync(ct)` 的 token **只对 Pending 有效**，已开始执行的回调取消不掉（实测 3s 工作仍等 3004ms）。**超时 ≠ 没执行**——所以**审计必须在 `InvokeAsync` 回调内部写**，不能在调用方超时后补写；另配泵侧看门狗（记录超过 N 秒的命令并告警）。
4. **泵不许阻塞**：单条命令在 UI 线程上 ≤ 8ms（超过记 Warn 并继续）；慢操作必须外派：
   - 登录（PBKDF2 十万次）→ `ScadaWebAuthService` 的后台串行 worker，完成后投一条 `auth.result` 回泵；
   - 图像编码 → `ImageEncoder` 后台队列；
   - 帧序列化 → M0 实测后再决定"UI 线程序列化"或"交给 2 个序列化 worker"（§12.2 P0-8 仍未覆盖）。
5. **背压（两级，都不许丢）**：每会话入站队列 **32（可配 16–64）**，`FullMode=Wait`。生产写法二选一：`await Writer.WriteAsync(cmd, ct)`（真实背压、零丢失），或 `TryWrite` 返回 false 时**显式回 `E_BUSY`**。全局 `Channel` 容量 **1024**，`TryWrite` 失败同样回 `E_BUSY`（带 `retryAfterMs`），**不阻塞 WS 接收循环、不丢已接受命令**。**禁用 `DropWrite`/`DropOldest`**——实测容量 4 写 100 条，`TryWrite` 返回 true 100 次、实际只入队 4 条（§16.4）。"靠 `seq` 发现缺口"只适用于**服务端→客户端**的帧，不是入站命令。**`E_BUSY` 不写成功审计**：命令级写一行 `Denied`（detail `reason=busy`），因为它确实到达了服务端。
6. **幂等（M2a 已实现）**：`(SessionId, RequestId)` 5 分钟去重（Router 的 `AckWindow` / `Retention`，`ScadaWebCommandRouter.cs:1060/1063`）；**重复请求返回原 `ack`、不重复执行**，窗口外仍在保留期回 `E_DUPLICATE`；`requestId` 空或超 64 回 `E_BAD_REQUEST`（`ScadaWebHost.cs:1458-1466`）；**M2a-4 已关闭**（§17.6），C02/C17 待矩阵执行。前端"按下期间复用同一 id、`ack` 前不发第二条"（§10.4 共同规则 4）仍保留，但不再是唯一防线。
7. **越权先判、顺序固定**：`Execute` 按 §8.2 的"路由层 + 权限层"判；**三闸在前**（`runGeneration` → `pageId` → `pageVersion`），先于认证/角色/策略/租约；任一不过直接回 `error` + **命令级**审计，**不进领域**。三闸的回执码固定：`E_STALE_RUN`（轮次不符）/ `E_STALE_PAGE`（页不符或版本不符）；**同一个命令三闸都不过时只报第一道**（`E_STALE_RUN`），审计 detail 同时写实际值与期望值。图元不在当前页快照 → `E_NOT_FOUND`（`E_UNKNOWN_ELEMENT` 作废，§9.8）。**as-built（M2a-3 已关闭）**：三闸必填——缺 `runGeneration` / `pageId` / `pageVersion` 任一即 `E_BAD_REQUEST`（`ScadaWebCommandRouter.cs:546-590`），顺序 `runGeneration → pageId → pageVersion`；C75 待执行。
8. **身份由服务端派生，客户端字段一律不采信**：`SessionId` / 用户名 / 角色 / 只读**全部从会话对象取**（`ScadaWebCommand` 只持有 `Session` 引用，`ScadaWebCommand.cs:92-104`）；网络消息里的同名身份字段**被忽略**，只进诊断日志（不含密码）。**as-built 偏差**：M2a 的 DTO 里没有身份字段，所以**不产生 `E_IDENTITY`**——伪造 `role` 的后果是"改了也没用"（按真实身份判权，不够就 `E_ROLE`），而不是被显式拒绝；`E_IDENTITY` 作为"显式伪造检测"归 M2 后续批（C50 按这个口径判，见矩阵 C50 备注）。审计只用服务端会话与用户名。
9. **危险动作必须带 `confirmToken`**：`cmd.confirm.request` 申请、绑定 `(sessionId, elementId, actionKind)`、默认 30s（`ConfirmTokenSeconds`，5–300）、单次（§8.4.1）；缺失/过期/重放/他人 token → `E_CONFIRM` + 审计 `Denied`，不执行（C46）。
10. **单条异常隔离**：`Execute` 整体包在 `try/catch` 里——任何未预期异常只影响**这一条**：回 `error{code:"E_INTERNAL"}`（不带堆栈、不带内部路径）、写命令级审计 `Failed`（detail 记异常类型，不记堆栈）、记一条 Error 日志，**泵继续取下一条**。**不许**让异常冒泡到消费者循环（一次 NRE 就会让所有会话的命令永久静默），也不许因此重试（重试会让"已经执行了一半的动作"再执行一次）。C73 是这条的守门人。

### 4.4 不可变 `ScadaPageSnapshot` + `PageKey`

【事实】`ScadaPage.Version` 就是结构版本：图元增删、任一图元任一属性变更、图层增删与任一图层属性变更都会递增；它刻意不走 `SetProperty`，避免被写守卫误伤、被撤销栈回滚（`Scada\Models\ScadaPage.cs:250-278`）。**单调、不回退**，正好当 `PageVersion`。

【事实】快照 DTO 在 `Scada\Models\ScadaPageSnapshot.cs`（命名空间 `VisionMaster.Scada.Snapshots`），构建器在 `Scada\Models\ScadaPageSnapshotBuilder.cs`。**M1b 的序列化形状以 `_WebProbe6` 的原始帧为准**：`snapshot.page` / `page.page` 是 **PascalCase**（`PageId` / `PageName` / `PageVersion` / `Width` / `Height` / `Background` / `BasePage` / `Elements`），因为前端 `elements-process.js` 按 `el.Properties` / `el.TypeKey` 读；**不要改成 camelCase**（会让每个图元的属性袋与几何全部读成 undefined，编译期全绿、只有真机看得见）。

| 字段 | 类型 | 来源 / 状态 |
| --- | --- | --- |
| `PageId` / `PageName` / `PageVersion` / `Width` / `Height` / `Background` | — | `ScadaPage`；**已落地** |
| `BasePage` | `ScadaPageSnapshot?` | `ScadaRuntime.CurrentBasePage`；**已落地**（基座只递归一层，基座的 `BasePage` 恒为 `null`） |
| `Elements[]` | `ScadaElementSnapshot[]` | 当前页全部图元（含容器子图元，按 `ZIndex` 稳定升序）；**已落地** |
| `BasePageVersion` | `int` | **已落地（M1c）**：快照信封 `basePageVersion = page.BasePage?.PageVersion ?? 0`（无基座为 `0`）；`_m1c_webchecks.log` 的 B10/B14/B18 与 M1c-05/M1c-06 均为原始 JSON 证据 |
| `Layers[]` / `CatalogVersion` | — | **M1b 不输出**：图层可见性落在每个图元的 `LayerVisible` 上；`catalogVersion` 只在 `/api/catalog` 的版本告警里用（M1.5） |

单个图元（M1b 实际字段，PascalCase）：

```json
{
  "ElementId": "e-1", "TypeKey": "Hmi.Gauge", "Name": "1#压力",
  "X": 100, "Y": 80, "Width": 140, "Height": 140, "Rotation": 0, "ZIndex": 10,
  "LayerId": "l-1", "LayerVisible": true, "ParentId": null, "RequiredRole": null,
  "Properties": { "Value": "0", "Minimum": "0", "Maximum": "1.6", "Unit": "MPa", "ValueFormat": "F2" },
  "Bindings": [ { "TargetProperty": "Value", "VariableId": "…", "VariableName": "压力1", "DisplayFormat": "F2", "Enabled": true } ],
  "Animations": [ { "Type": "Appearance", "RangeLow": "0", "RangeHigh": "0.5", "States": [ { "From": "0", "To": "0.5", "Fill": "#FF2D7DD2", "Flash": false } ] } ]
}
```

**构建规则（M1b as-built）**：

1. **只在 UI 线程构建**（`ScadaWebHost.RunOnUiAsync`）；值帧循环每 100ms 拍一次，比较 `PageKey`：会话没有页键 → `snapshot`；同一轮里页键变了 → `page`。**M1b 没有 300ms 防抖**——结构变化在下一拍（≤100ms）就发出；`page` 的载荷是整份 `page` 对象（不是结构增量）。M2 若需要抑制"拖属性时每拍一发"，再加防抖，但不得改变 `PageKey` 语义。
2. **不可变**：构建后只读（DTO 是 `sealed` + `init`，集合是 `ReadOnlyCollection`/`ReadOnlyDictionary`）；`RunGeneration + PageKey` 是缓存键。
3. **元素全收，图层可见性交给渲染器**：`ScadaPageSnapshotBuilder` 收全部图元并给 `LayerVisible`（找不到图层按 `true`），前端对 `LayerVisible=false` 的节点 `display:none`；**不在快照里过滤**（过滤会让"隐藏图元"与"不存在图元"无法区分）。
4. **基座嵌套在本页快照里**：`page.BasePage`（同结构，`BasePage` 恒 `null`），渲染序在 `page` 之前；**没有独立的 `snapshot.base` 字段**。
5. **不包含权限**：M1b 的只读由 `hello.readOnly=true` 表达；`caps` 是 M2 扩展点（§9.3）。
6. **没有逐图元 `frame` 字段名数组**：值帧按绑定平铺（`elementId + property + 四类值`，§9.5）；M1b 每拍全量发送，不做差异帧。
7. **父子的坐标仍是画面绝对坐标**（R76 的既有口径）；web 渲染时按 `child.left = child.x - parent.x - parent.ChildrenOriginX`、`child.top = child.y - parent.y - parent.ChildrenOriginY` 换算（`ScadaElementBase.ApplyPositionToParent`，`Scada.Controls\Elements\ScadaElementBase.cs:1134-1137`；`PanelElement.ChildrenOriginY = TitleHeight`，`PanelElement.cs:86-89`）。
8. **`PageKey = pageId@pageVersion@basePageVersion`（M1c as-built）**：基座页 `Version` 变化 → `basePageVersion` 变化 → 页键变化 → 发 `page`；本页 `pageVersion` 不变。实现 `ScadaWebHost.PageKey`；`_m1c_webchecks.log` M1c-05（`basePageVersion` 0→3、`pageVersion` 保持 12）与 M1c-06（2→3）是运行时证据。

### 4.5 发布器：值帧与图像

**值帧（共享，M1b as-built）**：

1. 宿主级**一条** `Task` 循环（`RunFrameLoopAsync`，`Task.Delay(100)`），不是每会话一个；M1b 的 10Hz 是**常量** `FrameIntervalMs=100`，`FrameRateHz` 配置项要到 M1.5 才接。
2. 解析面：快照里每条 `Bindings` 经 `IScadaValueSource.TryResolve` 取句柄（`Scada\Models\IScadaValueSource.cs`），同一帧内**按变量引用缓存**（Id 优先、名字兜底）；不跨帧缓存。
3. 每拍在 UI 线程拍一次 `LiveState`（页面快照 + 值项），**序列化一次**，扇出给所有会话；镜像模式让这件事成立。
4. **M1b 每拍全量发送**（`items` 含该页全部可解析绑定），不做差异帧、没有 `full` 字段；10 图元页面单帧只有数 KB。差异帧是 M1.5 的带宽优化项（§9.5）。
5. **不读 WPF 控件**：web 不依赖 `ScadaRuntimeBinder` 是否已刷过控件；两条路径各自从值源取数，用 oracle 测试钉住"结果一致"（§12.3 W1-21）。
6. **文本格式化**：**已落地（M1c）**——帧 `text` 用该绑定的 `DisplayFormat` 格式化（`ScadaBindingSnapshot.DisplayFormat` 随快照下发；`IFormattable` + InvariantCulture）。`Gain` / `Offset` / `FormatType`（Decimal/Hex/Binary）留 M1.5（§10.2）；矩阵 W1-22 的专项用例仍未跑。

**图像（M1.5 扩展点）**：

- M1b `/img` 固定 **404 + `E_IMG_NOT_READY`**，只占住路由形状；`imageToken` 未签发（§6.1）。
- M1.5 的链路：`RegistryScadaValueSource.Value` 已经把 `HImage` 换成 `BitmapSource`（`Core\Binding\ScadaValueSource.cs:55-65`），**web 侧不做 Halcon 转换**；UI 线程读值 → `Freeze()` → 后台编码队列（单消费者）→ PNG/JPEG → 按 `(variableId, imageVersion, maxWidth, quality)` 缓存（LRU，默认 32 项）。
- `imageVersion` = 该变量值变化的单调计数；URL 形态固定为 `/img/{elementId}/{version}.{ext}?t={imageToken}`：**版本负责缓存、`t` 负责鉴权**（§6.1），两者缺一即 401/403。
- **同一版本只读一次、只编码一次**；编码队列满时**丢最旧**并计数。

### 4.6 渲染目录：`IScadaRenderCatalog`（web 不引用 `VM.Scada.Controls` 的代价与解法）

**问题**：快照里的 `props` 必须是**解析后的属性袋**（老 `.vms` 里缺键的图元要补上描述符默认值），否则 web 必须自己维护一份"每个图元的默认值表"——那是第二份事实来源，早晚与 `BuiltInElements` 分叉。

**约束**：web 工程只引用 `VM.Core` / `VM.Scada`，不引用 `VM.Scada.Controls`（控件库会把它拖进 WPF 控件依赖，且违反"web 只认领域契约"的边界）。

**解法**：`IScadaRenderCatalog`（放 `VM.Scada`，**契约已落地**，形状以代码为准：`Scada\Models\IScadaRenderCatalog.cs:28-64`），由宿主实现（宿主本来就引用 `VM.Scada.Controls`），web 只消费纯数据 DTO：

```csharp
namespace VisionMaster.Scada
{
    public interface IScadaRenderCatalog
    {
        IReadOnlyList<ScadaElementDescriptorInfo> All { get; }   // 含 ShowInToolbox=false 的兼容别名
    }

    public sealed class ScadaElementDescriptorInfo
    {
        public string TypeKey { get; init; }                    // 如 Hmi.Tank，落盘长期标识
        public string DisplayName { get; init; }
        public string Category { get; init; }
        public double DefaultWidth { get; init; }
        public double DefaultHeight { get; init; }
        public bool ShowInToolbox { get; init; }
        public IReadOnlyList<string> PropertyKeys { get; init; }  // 含几何键 $X 等
        public IReadOnlyList<string> EventNames { get; init; }    // ScadaEventType 成员名
    }
}
```

- **契约与实现（M1b 已补齐）**：`VisionMaster\Services\ScadaRenderCatalog.cs` 已落地，`App.xaml.cs:326` 已注册 `IScadaRenderCatalog`；`/api/catalog` 在未注册时仍回空目录并带 `warning`（防御分支）。
- 宿主侧实现：遍历 `ElementRegistry.All`（公开属性，按"分组 → 显示名"排序），把 `ElementDescriptor` / `ElementPropertyDescriptor` 投影成上面的 DTO；**必须晚于 `ScadaChartElements.Register()`**，否则漏 `Hmi.ChartTrend`。
- `catalogVersion` 由 **web 侧**从这份快照算稳定指纹（`TypeKey + PropertyKeys + DefaultWidth/Height`，Ordinal 排序后拼接），不由宿主提供；这样契约里不需要多一个 `Version` 成员，也不会出现"宿主说 A、web 算出 B"。
- web 用它在 `Initialize` 建一次内存表：补默认值、生成 `/api/catalog`、算 `catalogVersion`；**运行期不再问宿主**。
- **一致性闸门**：`ScadaWebChecks` 有一条断言——`IScadaRenderCatalog.All` 的 `TypeKey` 集合必须等于 `ElementRegistry.Descriptors.Keys`（断言工程可以引用两边）；少一个/多一个都红。

---

## 5. 模块打包、可选加载、配置

### 5.1 工程布局

```
Web\
  Directory.Build.targets                 # 仅本目录：构建后把产物拷到宿主输出 Web\（不碰 Plugins\ / Modules\；计划，M1c 未建）
  VisionMaster.ScadaWeb\
    VisionMaster.ScadaWeb.csproj          # net9.0-windows, UseWPF=true
                                          # 项目引用只有 VM.Core / VM.Scada；
                                          # 二进制引用 Watson（DLL\Watson\*.dll，与主程序同一份）+ Newtonsoft.Json 13.0.4
    ScadaWebModule.cs                     # IAppModule 实现（Name = "SCADA Web 运行态模块"）
    ScadaWebHost.cs                       # Watson 监听、路由、静态资源、WS、快照/值帧/重连
    ScadaWebSession.cs                    # 每连接：发送队列 + 页键（不持有领域对象）
    ScadaWebTicketService.cs              # 一次性 ticket（30s/单次）+ 会话令牌（8h/换票）
    ScadaWebValueResolver.cs              # 绑定 → 值帧 item（四类值）
    wwwroot\index.html  app.js  styles.css  elements-process.js  elements-process.css
                                          # 5 个静态文件（白名单，磁盘读取；M1b 未内嵌）
  ScadaWebChecks\                         # 协议层断言宿主（已落地，M1c；已入 VisionMaster.sln）
  ScadaWebE2E\                            # Playwright 端到端（计划，未建）
  _WebProbe6\                             # M1b 探针（仓库根，不打包）
```

【事实】`IAppModule` 在 `Core\Modules\IAppModule.cs`（编译进 `VM.Core`，命名空间 `VisionMaster.Lifetime`），所以 web 工程**不需要引用 `VisionMaster.exe`** 就能实现模块契约。`AppLifetimeService` / `ExitTask` 同样在 `VM.Core`（`Core\Lifetime\`）。

【事实】`VM.Scada` 刻意 `net9.0`、不引用 WPF、不引用 `VM.Core`（`Scada\VM.Scada.csproj` 注释）；`VM.Core` 是 `net9.0-windows` + `UseWPF`（`Core\VM.Core.csproj`）。所以 web 工程的目标框架取 `net9.0-windows`，引用 `VM.Core` + `VM.Scada`（`VM.Core` 传递 `VM.Scada`）。

### 5.2 可选加载时序（`App.xaml.cs`）

```
RegisterTypes(containerRegistry)
  ├─ 1. ScadaWebModuleLoader.TryLoad(AppContext.BaseDirectory, out diagnostic)   ← 必须在 _modules 循环之前（App.xaml.cs:230）
  │      Enabled=false → 返回 null 且 diagnostic=null（正常状态，不写 Warn；不 LoadFrom、不解析依赖）
  │      Enabled=true  → 先查 Web\ 里的契约副本（VM.Core.dll / VM.Scada.dll / Core.Interfaces.dll / Newtonsoft.Json.dll）
  │                      → Assembly.LoadFrom("<base>\Web\VisionMaster.ScadaWeb.dll")
  │                      → 找 public 非抽象 IAppModule 实现 → Activator.CreateInstance → _modules.Add
  │      任何失败 → 返回 null + diagnostic（此刻容器未建、ILogService 还解析不到），
  │                 App 存进 `_pendingModuleDiagnostics`；catch 里**不 rethrow**，web 起不来不阻断软件启动
  ├─ 2. _modules.Add(CommunicationModule / FlowEngineModule / HttpImageServerModule / CameraModule / MotionModule)
  ├─ 3. foreach (module in _modules) module.Register(containerRegistry)   ← 单个模块异常隔离（App.xaml.cs:238-250）
  └─ 4. 其余注册（含 ScadaRuntimeHost 的 `:319-321` 三条映射、`IScadaAuditSink` `:309`、`IScadaUserAuthenticator` `:403`）

CreateShell()
  ├─ _lifetime = Resolve<AppLifetimeService>()
  ├─ foreach (module in _modules) module.Initialize(Container, _lifetime)
  │      ScadaWebModule.Initialize（**M1b 现状**：解析 `IScadaRunHub` / `IScadaUserAuthenticator` /
  │        `IScadaRenderCatalog` / `IScadaValueSource` / `ILogService` / `AppSettingsService`，
  │        构造 `ScadaWebHost` → `Start()`（端口占用只记 Error、继续启动），
  │        并注册 `ExitTask("停止 SCADA Web 服务", StopAsync, 5000)`；两个入口幂等）
  │      —— 目标形态：
  │         ├─ Resolve<IScadaRunHub>() / IScadaValueSource / IScadaAuditSink / IScadaUserAuthenticator / ILogService / AppSettingsService
  │         ├─ new ScadaWebHost(hub, auth, catalog, log, settings, values, dispatcher) → Start()（绑定+监听，毫秒级）
  │         │     端口占用等运行期失败 → Error 日志 + 继续启动（与 HttpImageServerModule 同口径）
  │         │     **三断言失败**（WebSockets.Enable / auth:true / AuthenticateApiRequest 非空）
  │         │       → **不启动监听** + Error（这是接线错误，fail-open 比不启动更危险）
  │         ├─ 订阅 hub.RunStarted / RunStopped（处理器内再 `Dispatcher.CheckAccess` 兜一层）
  │         └─ _lifetime.RegisterExitTask(new ExitTask("停止 SCADA Web 服务", StopAsync, 8000))
  └─ 启动自检链（web 不注册任何 IStartupCheck）
```

**为什么 loader 自己读 `AppConfig.json`**：`RegisterTypes` 阶段容器尚未构建，`Resolve<AppSettingsService>()` 会抛；而 `AppSettingsService` 的构造函数就是 `Load()`（`Core\AppSettingsService.cs`），直读同一份文件（`AppDomain.CurrentDomain.BaseDirectory\AppConfig.json`，UTF-8）与它口径一致。**只读 `ScadaWeb` 一节**（`ScadaWebConfigProbe`，不反序列化整个 `AppConfigModel`——否则任何无关节的损坏都会变成"Web 不加载"）。缺配置文件 / 解析失败 / 缺 DLL 一律返回 null + 诊断（调用方 Warn），**不抛**。

**加载失败的 Warn 要能被现场看见（矩阵 A03）**：`RegisterTypes` 阶段没有 `ILogService`，所以 `TryLoad` 把"缺失路径 / 异常原文"通过 `out diagnostic` 交回；`App` 先存进 `_pendingModuleDiagnostics`，`CreateShell` 容器就绪后（模块初始化前后均可）统一 `Warn` 一次。只写 `Debug.WriteLine` 在 Release 下是黑洞，A03 的"日志一条 WARN 含缺失路径"就过不了。

**web 模块的 `Register` 只登记自己的服务，不许 `Resolve` 任何宿主服务**：`_modules` 的 `Register` 循环在 `App.xaml.cs:238-250`，而 `ScadaRuntimeHost` 的注册在 `:319-321`（更晚）；在 `Register` 里 Resolve 既会因为容器未构建而失败，也会因为宿主尚未注册而失败（与 `App.xaml.cs` 里 `SolutionService.IoMigrationHook` 那段注释同一条理由）。需要宿主服务一律在 `Initialize` 里 Resolve。

**为什么 `Enabled=false` 时连 `LoadFrom` 都不做**：不用 web 的现场不该为它付"加载 Watson + 网络栈 + 多一个 DLL 被锁"的代价；也避免依赖缺失时在启动期抛异常。【M0 实测】`Enabled=false` 时 `AppDomain` 里既没有模块程序集、也没有 `VM.Scada`（`VM.Core` 是宿主自己引用的，本来就在），`LoadFrom called=False`（`_WebProbe1\README.md` §4-A/F）。

**模块依赖放哪（M0 实测，`_WebProbe1\README.md` §5-G）**：

1. `Assembly.LoadFrom` 会为**被加载程序集自己**探测其所在目录——模块的私有 DLL（含第三方）放 `Web\` 即可解析，**不需要** `AssemblyDependencyResolver` / 自定义 ALC。只有"模块要携带与宿主不同版本、彼此隔离"时才需要后者。
2. **`Web\` 只放模块自己的 DLL 与私有依赖**；`VM.Core.dll` / `VM.Scada.dll` / `Core.*.dll` / `Prism*.dll` / `Newtonsoft.Json.dll` 等**宿主已有的程序集绝不复制进去**。解析顺序是"应用基目录 / deps.json 优先，模块目录兜底"，副本是死文件，永远用不到；更糟的是会造成两份契约类型（红线②同源）。
3. 类型身份实测：模块的 `IAppModule` 与宿主 `typeof(IAppModule).Assembly` 是**同一个 `Assembly` 对象**（`same Assembly object=True`），`VM.Core` / `VM.Scada` 均从宿主目录加载。
4. DLL 缺失 / 加载异常 → 只打一条 Warn（含缺失路径）、**不重试**、进程继续，退出码 0（`_WebProbe1` §4-D）。
5. **ASP.NET Core（Kestrel）是例外**：共享框架程序集不落盘，`Web\` 目录探测救不了；模块用 Kestrel 时**宿主** `VisionMaster.csproj` 必须加 `<FrameworkReference Include="Microsoft.AspNetCore.App" />`，否则 `FileNotFoundException: Microsoft.AspNetCore, Version=9.0.0.0`（`_WebProbe1` §5-H 实测）。**本方案用 Watson，不需要这条 FrameworkReference**；将来若换 Kestrel，这是宿主侧前置改动。

**加载器必须自检"找到类型但不可赋值"这一种失败**：`t.IsAssignableFrom` 为 false 且类型名匹配 `*Module` 时，日志要直说"`Web\` 里出现了 `VM.Core.dll` 副本（两份 `IAppModule` 类型）"，而不是静默跳过。⚠ 测试矩阵 A05（把 `VM.Core.dll`/`VM.Scada.dll` 复制进 `Web\` 做反证）**M0 未跑**，仍属待补项（§16.7）。

### 5.3 打包（`pack.ps1`）

现有脚本补齐三处（Halcon 原生库 / `Modules\` / `ScriptAssets\`）。本方案追加第 4 处：

1. `dotnet publish` 主程序 → `$OutputDir`。
2. `dotnet publish Web\VisionMaster.ScadaWeb`（或 `build -c Release`）→ 拷 `VisionMaster.ScadaWeb.dll` 到 `$OutputDir\Web\`。
3. **校验**（不看退出码，看文件）：
   - `$OutputDir\Web\VisionMaster.ScadaWeb.dll` 存在；
   - `$OutputDir\Web\wwwroot\` 下 5 个文件（`index.html` / `app.js` / `styles.css` / `elements-process.js` / `elements-process.css`）齐全，字节与源码 `wwwroot\` 一致（M1b 是磁盘 Content；R42 的"增量构建静默漏打包"同族判据）；
   - `$OutputDir\Modules\` 里**没有** `VisionMaster.ScadaWeb.dll`（红线②）；
   - `$OutputDir\Plugins\` 不存在或为空；
   - **`$OutputDir\Web\` 里不许出现公共契约 / 宿主已有程序集的副本**：`VM.Core.dll`、`VM.Scada.dll`、`Core.Interfaces.dll`、`Core.Controls.dll`、`Prism*.dll`、`Newtonsoft.Json.dll`（红线②同族——出现副本会让 `Assembly.LoadFrom` 加载出第二份 `IAppModule`，`is IAppModule` 判定失败，表现是"模块加载了但没注册"，且不报错；`_WebProbe1` §5-G 已实测解析顺序是宿主目录优先，副本是死文件）。
4. 受保护用户数据清单不变；新增 `Web\` 属程序文件，升级时整目录覆盖。

【决策】`Web\VisionMaster.ScadaWeb` 必须进 `VisionMaster.sln`（R62：`Plugin.Camera.Network` / `Plugin.ImageAssign` 不在 sln 里，全量重建永远不碰它们）。收尾构建对 web 工程用 `--no-incremental`（R42/R62），并**Debug 与 Release 都重建**（R55 同族：`pack.ps1` 打的是 Release）。

### 5.4 配置节 `ScadaWeb`（`AppConfig.json`）

【决策】新增节 `ScadaWeb`（与 `HttpImageServer` / `NetworkCameraServer` / `ImageGallery` 同级，`Core\Models\AppConfigModel.cs`）。默认值一律取"不暴露、不放开"。

| 字段 | 类型 | 默认 | 取值范围 | 说明 / 生效时机 |
| --- | --- | --- | --- | --- |
| `Enabled` | bool | `false` | — | 总开关。**只在启动时读**；改后需重启。M1 先用 `AppConfig.json` 手工开关（设置页入口是可选项，不做不影响 M1 验收） |
| `BindAddress` | string | `"0.0.0.0"` | IP / `0.0.0.0` / `127.0.0.1` | **监听地址（不叫 `Host`）**；`127.0.0.1` 仅本机调试。**写 `Host` 会被 Newtonsoft 静默忽略、实际仍监听 `0.0.0.0`**——这是最危险的一类配置错误 |
| `Port` | int | `8080` | 1024–65535 | 与 19000（收图）/19100（网络相机）错开 |
| `PublicBaseUrl` | string | `""` | URL / 空 | 生成图片绝对地址与日志提示；空 = 按请求 `Host` 推断 |
| `RequireLogin` | bool | `true` | — | 是否要求登录后才能看画面。关掉 = "网页一打开就是运行画面"，只适合完全隔离的调试网络 |
| `AllowAnonymousView` | bool | `false` | — | 未登录是否允许**只读**看运行画面。**默认 false = 未登录只能看基础页**；打开后未登录进 `ReadOnly`，仍不可操作 |
| `AllowCommands` | bool | **`false`** | — | **M2a 总闸：默认只读**。为 `false` 时所有 `cmd.*` 一律回 `E_POLICY`（逐字 `当前为只读模式（ScadaWeb.AllowCommands=false）`）、`caps` 全 false，**即使 `ClickPolicy`/角色都允许**；改成 `true` 才开放操作。**运行期可改**（改后推 `caps`，`policyVersion` 取 `Environment.TickCount64`），不需要重启。**已落地**：`Core\Models\AppConfigModel.cs:378` |
| `ConfirmTokenSeconds` | int | **`30`** | 5–300 | 危险动作一次性确认令牌的有效期（§8.4.1）；从签发时刻按服务端时钟算。**已落地**：`AppConfigModel.cs:388`（常量 `DefaultConfirmTokenSeconds=30`，`:313`） |
| `TicketRatePerMinute` | int | **`10`** | 1–600 | `POST /api/ticket` 的**每来源 IP 每分钟**请求上限（匿名/登录/换票三种请求合并计数）；超出 **429 + `E_RATE_LIMITED` + `retryAfterMs`**，不签发 ticket、不做 PBKDF2。计数按 `(remoteIp, 60s 滑动窗口)`，**不因用户名不同而分开**（防"换用户名刷密码"）；与 `LoginMaxFailures` 的账号锁定**同时生效、互不替代**。**已落地**：`AppConfigModel.cs:398`。**接线注意**：`ScadaWebSecurityGate.TryCheckTicketRateLimit` 目前用常量 `TicketRateLimitPerWindow=10`（`:42`），**没有读这个键**——接线时必须改成读配置，否则"改了配置不生效"（与 §5.4 的静默忽略同族） |
| `MaxSessions` | int | `16` | 1–128 | 超限直接拒绝 WS（`AtCapacity`，close 1013），不排队；**统计所有 WS 连接（含未认证）** |
| `SessionInboundQueueCapacity` | int | `32` | **16–64** | 每会话入站命令队列；越界按边界夹取 + Warn（不阻断启动）。**容量 = 纯延迟**：实测 256 深度 p50 排队 275ms（`_WebProbe3` §4.1），所以 256 不是合法档位；`FullMode=Wait`，禁用 `DropWrite`/`DropOldest` |
| `SessionOutboundQueueCapacity` | int | `256` | 32–4096 | 每会话出站消息队列；满 → 断开（慢消费者） |
| `FrameRateHz` | int | `10` | 1–20 | 值帧频率；>10 只在 M0 实测通过后允许 |
| `FullRateSessionLimit` | int | `4` | 1–`MaxSessions` | 超出该数量的会话自动降到 2Hz（仍保持镜像，只是刷新慢） |
| `MaxElementsPerPage` | int | `2000` | 100–20000 | 超限拒绝加载并报 `E_PAGE_TOO_LARGE`（不静默截断） |
| `SnapshotMaxBytes` | int | `2097152`（2MiB） | 256KB–64MB | 快照 JSON 上限；超限报错并建议拆页。M1 门槛按 2MiB 判（矩阵 M4/E04）；调大必须同时说明"首屏预算怎么守住" |
| `FrameBudgetBytes` | int | `122880`（120KB） | 32KB–1MB | 单帧值帧 JSON 上限（1000 图元全变、每图元 3 字段的实测预算）；超限先降 `FrameRateHz`、再按图元分组拆帧，**不许截断字段** |
| `ClickPolicy` | enum | `HighestRoleOnly` | `HighestRoleOnly` / `AllOperators` | 见 §8.3；运行期可改（推 `policyVersion`） |
| `RequireOnlineUsers` | bool | `true` | — | **`AllOperators` 的监管开关①**：必须能给出在线列表（`presence`）。为 false 时**拒绝启用 `AllOperators`**（§8.4） |
| `RequireAudit` | bool | `true` | **固定 `true`** | **监管开关②**：命令级 + 动作级两层审计必须可写。配置成 `false` 属于非法配置：**加载时把值夹回 `true` 并写一条 Error**（文案含配置键名），且不启用 `AllOperators`（C65 守门）。夹取发生在**加载期**、不是运行期 |
| `RequireRateLimit` | bool | `true` | — | **监管开关③**：`CommandRatePerSecond` + `ElementRatePerSecond` 必须生效。为 false 时拒绝启用 `AllOperators` |
| `RequireConfirmForDangerous` | bool | `true` | — | **监管开关④**：危险动作必须走 `confirmToken`。为 false 时拒绝启用 `AllOperators` |
| `OperationLeaseEnabled` | bool | `false` | — | 监管开关⑤（可选）：操作权租约（§8.6）。**默认关**——"多人同时操作"要单人接管时再开 |
| `OperationLeaseSeconds` | int | `60` | 10–600 | 租约有效期；到期自动释放 |
| `AllOperatorsRequiresLease` | bool | `false` | — | **`AllOperators` 的可选加强项**（不是启用闸门）：为 `true` 时 `AllOperators` 下必须持租约才能点，未持租约一律 `E_LEASE_HELD`；为 `false` 时退化为"都可点 + 串行 + 审计 + 限速 + 二次确认"。**为 `true` 但 `OperationLeaseEnabled=false` 属非法组合：加载即 Error 并把本项夹回 `false`** |
| `CommandRatePerSecond` | int | `5` | 1–50 | **每会话**命令速率上限；超出 `E_RATE_LIMITED`（0/负数按 1 处理，不许关掉限流） |
| `ElementRatePerSecond` | int | `2` | 1–20 | **每元素**命令速率上限（同一 `elementId`），**跨会话共享**：会话 A 与 B 在同一秒各写同一元素 1 条，合计 2/s 已满，第 3 条（不论来自哪个会话）`E_RATE_LIMITED` + `retryAfterMs`、不执行。会话级 `CommandRatePerSecond=5` 则是**每会话独立**。C45/C61/C64 按这个口径判 |
| `ImageTokenSeconds` | int | `45` | **30–60** | `/img` 的 `imageToken` 有效期（§6.1/§8.9）；每 `TTL/3` 轮换一次，旧 token 保留到自己的 `expiresAt`，**同一会话同时最多 3 个存活**（E29 的上限按"会话数 × 3"判） |
| `LoginMaxFailures` | int | `5` | 1–20 | 窗口内失败次数上限 |
| `LoginAttemptWindowSeconds` | int | `300` | 60–3600 | 失败计数窗口 |
| `LoginLockoutSeconds` | int | `300` | 30–3600 | 锁定时长（按用户名 + IP 双键） |
| `SessionIdleTimeoutSeconds` | int | `1800` | 60–86400 | 无心跳/无命令多久回收 |
| `HeartbeatSeconds` | int | `15` | 5–60 | 服务端要求的最小心跳间隔（客户端更频繁也接受） |
| `OriginAllowList` | string[] | `[]` | 主机名/IP | 空 = 只接受 `BindAddress`/请求 `Host` 同源；非空 = 额外允许的来源 |
| `AllowedActions` | string[] | `["Log","WriteVariable","StartFlow","StopFlow","RunFlowOnce","PauseFlow","ResumeFlow"]` | `ScadaActionType` 成员名 | web 命令可执行的动作白名单；不在表里的动作**跳过并审计**。报警确认**不是** `ScadaActionType` 成员，它由 `cmd.alarm.ack` 单独判权（`AckButton` 的 `RequiredRole`），不受本表控制 |
| `AllowMotionCommands` | bool | `false` | — | R132A"运动命令默认禁用"；`true` 时还要把运动动作加进 `AllowedActions` |
| `AutoFollowNewRun` | bool | `true` | — | 桌面重新点「运行」时，已连接会话自动跟随新运行（新 `runGeneration`） |
| `ImageMaxWidth` | int | `1280` | 64–4096 | 图像编码最大宽（等比缩放） |
| `ImageJpegQuality` | int | `80` | 30–100 | 有损质量；`ImageFormat=Png` 时忽略 |
| `ImageFormat` | enum | `Png` | `Png` / `Jpeg` | 工艺图/文字截图用 PNG；相机图用 JPEG 省带宽 |
| `ImageCacheEntries` | int | `32` | 4–256 | 编码结果 LRU |
| `StaticAssetCacheSeconds` | int | `300` | 0–86400 | 静态资源 `Cache-Control: max-age` |
| `ShowOnlineUsers` | bool | `true` | — | 浏览器是否显示在线用户列表（审计与监管仍照常；为 false 时 `AllOperators` 不可启用） |

**字段落点（M2a as-built，2026-10-21 09:10 复核）**：`ScadaWebSettings` 现在**已有**：`Enabled` / `Port` / `BindAddress` / `RequireLogin` / `AllowAnonymousView` / **`AllowCommands`（`:378`）** / **`ConfirmTokenSeconds`（`:388`）** / **`TicketRatePerMinute`（`:398`）** / `MaxSessions` / `SessionInboundQueueCapacity` / `ClickPolicy` / `AllowMotionCommands` / `OperationLeaseEnabled` / `CommandRatePerSecond` / `SnapshotMaxBytes` / `FrameBudgetBytes` / **`RequireOnlineUsers`（`:465`）/ `RequireAudit`（`:471`）/ `RequireRateLimit`（`:477`）/ `RequireConfirmForDangerous`（`:483`）/ `AllOperatorsRequiresLease`（`:491`）/ `ElementRatePerSecond`（`:497`）/ `ImageTokenSeconds`（`:504`）**（均在 `Core\Models\AppConfigModel.cs`）。**仍未落地**（现在写进 `AppConfig.json` 会被 Newtonsoft 静默忽略、既不报错也不生效）：`PublicBaseUrl`、`SessionOutboundQueueCapacity`、`FrameRateHz`、`FullRateSessionLimit`、`MaxElementsPerPage`、`OperationLeaseSeconds`、`LoginMaxFailures`/`LoginAttemptWindowSeconds`/`LoginLockoutSeconds`、`SessionIdleTimeoutSeconds`、`HeartbeatSeconds`、`OriginAllowList`、`AllowedActions`、`AutoFollowNewRun`、图像编码/缓存四键、`StaticAssetCacheSeconds`、`ShowOnlineUsers`。**落地时必须同步本节与矩阵 M11/M13。**

**M2a 的三个新键已经进配置类**（不再是"目标字段"）：`AllowCommands`（总闸）、`TicketRatePerMinute`（取票限速）、`ConfirmTokenSeconds`（确认令牌有效期）。**不许**用 `RequireLogin` / `AllowAnonymousView` 兼作总闸（语义不同：前两者管"能不能看"，`AllowCommands` 管"能不能写"）；也不许把 `TicketRatePerMinute` 读成"按用户名"（它是**按来源 IP**）。

**M1b 实际读取的字段（as-built，别按上表假设全部生效）**：`Enabled` / `Port` / `BindAddress` / `AllowAnonymousView` / `MaxSessions` 五项真正生效；`RequireLogin` 只作为"无 ticket 时是否显示登录卡片"的目标口径（M1c 接）；**`FrameRateHz` 未接**——值帧固定 100ms（`ScadaWebHost.FrameIntervalMs`），`FrameBudgetBytes` / `SnapshotMaxBytes` 未做上限判定；`SessionInboundQueueCapacity` 在 M1b 无入站命令、不生效。其余字段一律 M1.5/M2。

**默认值口径（M0.1 裁定，两份文档逐字一致）**：`MaxSessions=16`、`SessionInboundQueueCapacity=32`（范围 16–64）、`FrameBudgetBytes=120KB`（122880）、`SnapshotMaxBytes=2MiB`（2097152）、`CommandRatePerSecond=5`、`ElementRatePerSecond=2`、`ImageTokenSeconds=45`。**全部可配**；验收必须同时覆盖"默认值"与"把 `MaxSessions` 调到 50/100 再测"两条分支（矩阵 M11/E02/E03/A14/C35/C56/C57/C59/E28）。**矩阵里旧写的 `MaxSessions=50`、入站 256 已作废**；256 既不是默认值也不是合法档位（范围 16–64）。

**写死在代码里、不提供配置的规则**（避免被现场改成不安全）：

1. 未登录**永远不能**操作（与桌面"未登录 = 操作员"不同，见 §8.1）。
2. 未登录**默认不能**加载运行画面（M1b：无 ticket 只能看登录卡片；M2：不允许 `view.attach`）；`AllowAnonymousView=true` 是显式例外，且仍不可操作。
3. 每条操作命令**必须**在服务端再判一次角色（不信任客户端的 `caps`）。
4. 审计**必须**带 actor / session / IP（actor 进 `操作者` 列；session/IP 进 `说明` 列的 `[web s=… ip=…]` 前缀，§8.7）。
5. `Navigate` 动作在 web 侧**永远**跳过（镜像模式）。
6. 启用前必须已改掉出厂密码（`ScadaUserStore.IsUsingDefaultPassword`，§8.9）。

---

## 6. HTTP 基础页与"申请加载"状态机

### 6.1 路由表

| 方法 | 路径 | 鉴权 | 说明（M1b / M1c as-built） |
| --- | --- | --- | --- |
| GET | `/`、`/index.html` | 无 | `index.html`（磁盘 `Web\wwwroot\`，**不是内嵌资源**）→ `Routes.PreAuthentication.Static.Add` 原样吐字节 |
| GET | `/app.js`、`/styles.css`、`/elements-process.js`、`/elements-process.css` | 无 | 4 个静态文件；白名单是**固定文件名**（没有 `/assets/{file}` 路由，也没有通配兜底） |
| GET | `/api/status` | 无 | `{ok, running, pageId, pageName, pageVersion, online, version, protocol, readOnly}`；未运行也 200 |
| GET | `/api/catalog` | 无 | `{ok, count, items:[ScadaElementDescriptorInfo]}`；未注册目录时 `count=0` + `warning` |
| POST | `/api/ticket` | 无（**M1c 已落 `refreshToken` 分支；Origin 校验与每 IP 限速仍未做** → U-13） | 请求体三种：`{}`（匿名只读，需 `AllowAnonymousView=true`）、`{"user","password"}`、`{"refreshToken"}`（M1c 已落地）；成功回 `{ok, ticket, expiresIn:30, user, role, readOnly}`，M1c 起再加 `sessionToken` / `sessionExpiresIn`（8h） |
| GET | `/img`、`/img/{elementId}`、`/img/{elementId}/{version}` | — | **M1b 固定 404 + `E_IMG_NOT_READY`**；`imageToken` 是 M1.5（见下方"图片鉴权"） |
| WS | `/ws` | `auth: true` + `AuthenticateApiRequest` 非空 | 唯一长连接；JSON 文本帧；ticket 可放 `?ticket=` 或 `Authorization: Bearer` |

**静态资源走 `RoutingGroup.Static.Add` + 磁盘读取（M1b 是 `File.ReadAllBytes`；图片 M1.5 才接）**：

- 【M0 实测】`WebserverBase.Get` 返回 `byte[]` 时会被 `ISerializationHelper.SerializeJson` 序列化成 **base64 JSON**：源 1024B → body 1370B（多一层引号与 base64），`rawMatch=False`。二进制一律走 `Static`（`_WebProbe2` §4-E）。
- 禁止把程序目录当 web root（否则 `AppConfig.json` / `ScadaUsers.json` / `Logs\` 全部可下载）。M1b 的实现是**固定 5 个文件名白名单**，在 `Web\wwwroot\` 或 `wwwroot\` 两个候选根下找；白名单本身就是"拒绝 `..`"的判据（带路径分隔符的输入根本进不来）。
- `Content-Type` 按扩展名固定表（`.html` / `.js` / `.css`）；`X-Content-Type-Options: nosniff`；**M1b 的 `Cache-Control: no-cache`**（`StaticAssetCacheSeconds` 与 `index.html no-store` 是 M1.5 的目标口径，当前未接）。
- **仍未做（U-13）**：`Referrer-Policy` / `X-Frame-Options` / `Origin` 校验 / `/api/ticket` 每 IP 限速。这些是 M1c 之后剩余的整改项，**不许按本节旧版假设它们已生效**。
- 未命中的路径落到鉴权后的默认路由，回 **401**（不是 404）；只有白名单文件返回 200。

**Watson 7.2.0 实测事实（`_WebProbe2`：9 PASS / 0 FAIL；`run_final.txt` / `run_default_ws.txt` / `dump.txt`）**：

| 事实 | 实测结果 |
| --- | --- |
| WS 路由签名 | `WebSocket(string path, Func<HttpContextBase, WebSocketSession, Task> handler, bool auth = false)` |
| `auth:true` | 每次握手**恰好触发 1 次** `Routes.AuthenticateApiRequest`，传入的是升级前的 `GET /ws`；`auth:false` **完全不触发**（默认漏洞） |
| 鉴权失败 | Watson 自动 **401 + `{"Error":"NotAuthorized","StatusCode":401,...}`**，业务 handler 不执行 |
| 鉴权粒度 | **整条路由**，连接建立后不再回调；运行中吊销需宿主 `DisconnectWebSocketSessionAsync` 或 handler 自行校验 |
| 握手头 | handler 内可 `ctx.Request.RetrieveHeaderValue("Authorization")`；身份可存 `WebSocketSession.Metadata` |
| 发送并发 | `SendTextAsync` 内部有 `SemaphoreSlim _SendLock`；4 线程 × 250 = 1000 条零丢失（9–15ms） |
| 接收 | 仍需**单循环**（`ReceiveAsync` / `ReadMessagesAsync`），不要多线程读同一 session |
| `HttpMethod` | 是 `WatsonWebserver.Core.HttpMethod` **枚举**，与 `System.Net.Http.HttpMethod` 同名冲突，必须写全名 |
| `Get` 重载 | 只有 `Func<ApiRequest, Task<object>>` 一种；要原样吐字节只能下沉到 `Routes.PreAuthentication.Static.Add` 或 `Routes.PostAuthentication.Static.Add` |

**三个静默失效点（编译期全绿、运行期不报错；生产必须做启动断言三连）**：

| # | 静默失效点 | 实测表现 | 生产必须做 |
| --- | --- | --- | --- |
| 1 | `settings.WebSockets.Enable` **默认 false** | WS 握手 **400**，但路由注册与 `Start()` 全绿 | 显式 `settings.WebSockets.Enable = true`；启动时断言为 true |
| 2 | `WebSocket(path, handler)` 的 `auth` **默认 false** | 连接完全不经过鉴权器（`authCalls +0`），带不带 token 都一样 | 所有业务 WS 路由显式 `auth: true`；静态扫描禁止 `auth:false` 出现在第一方 WS 路由 |
| 3 | `auth:true` 但 `Routes.AuthenticateApiRequest == null` → **fail-open** | 未配置鉴权器时端点**照常连上并收到 hello** | 启动时断言 `AuthenticateApiRequest != null`；**先赋值鉴权器、再注册路由** |

> 结论：**不必换 BCL `HttpListener` + `System.Net.WebSockets`**（`_WebProbe2` §5）。`auth:true` 在握手阶段就把未授权连接挡在 101 之外，业务 handler 根本不会被调用。

**握手凭证：一次性 ticket（`auth:true` 的落地方式）**

浏览器 `WebSocket` 构造器**不能自定义请求头**，而 `auth:true` 要求握手阶段就通过 `AuthenticateApiRequest`。所以握手凭证只能放 **query** 或 cookie。本方案选 query + 一次性 ticket：

```
浏览器                        服务端
  │  POST /api/ticket ─────────►│ 生成 32 hex ticket（30s TTL、单次）
  │  {user,password} 或 {} ────►│ 有凭据 → 后台 PBKDF2 校验；空体 → 匿名只读（需 AllowAnonymousView）
  │◄────────────────────────────│ {ok, ticket, expiresIn:30, user, role, readOnly}
  │  WS /ws?ticket=… ──────────►│ AuthenticateApiRequest 核销 ticket
  │                             │   有效 → 移入 handoff 表，并签发 sessionToken（8h）
  │                             │   无效/过期/已用 → Watson 401 + 结构化错误体
  │◄────────────────────────────│ 101
  │◄────────────────────────────│ hello {sessionRef, readOnly, running, …}
  │                             │（M1c：hello 再带 sessionToken / sessionExpiresIn）
  │◄────────────────────────────│ snapshot（运行中）或 idle（未运行）
  │  hello ────────────────────►│ 回 hello.ok {sessionRef, readOnly}（客户端确认，可选）
```

五条口径：

1. **ticket 不是登录态**：它只证明"这条 WS 来自本服务发出的页面"；**M1b 的用户身份在 `POST /api/ticket` 时已经校验完**（凭据直接换票），连接建立后没有 `auth.login`。M2 才把"连接内登录"作为扩展点（§9.2）。
2. **单次 + 30s**：握手成功即作废；过期、已用、IP 不符一律 `NotFound/Invalid`（Watson 自动 401）。ticket 出现在 URL 里可能进代理日志，短 TTL + 单次把窗口压到最小；**密码绝不进 URL**。
3. **不用 cookie**：避免 CSRF 与"cookie 作用域/同源"这一整类问题；**会话令牌（`sessionToken`）只在 WS 消息里传**，不落 URL、不落 `localStorage`（放 `sessionStorage`，关标签即失效）。**服务端不写任何 `Set-Cookie`**，浏览器侧也没有可被跨站自动携带的凭据——判据见矩阵 F11/F23。
4. **启动断言三连**（§6.1 上表）：`WebSockets.Enable=true`、所有第一方 WS 路由 `auth:true`、`AuthenticateApiRequest != null`。前两条在启动时检查（`Enable` 值 + 对第一方路由注册代码的静态扫描），第三条在注册路由**之前**运行时断言；任一不满足即拒绝启动并写 Error（这是唯一允许 web 模块"不启动"的情形——fail-open 比不启动更危险）。
5. **申请入口自己也要设门**：`POST /api/ticket` 的 Origin 校验、来源 IP 绑定与每 IP 限速是 **M1c/F 阶段整改项**；M1b 只做账号校验 + `AllowAnonymousView` 开关。ticket 只存内存表（`ticket → (expiresAt, grant)`），用完即删，**日志只记前 6 位与 sessionId**，不记整串。

**图片鉴权：独立 `imageToken`（M1.5 扩展点，M0.1 裁定写死；M1b `/img` 固定 404）**

> M1b 只占住 `/img` 路由形状并回 `404 E_IMG_NOT_READY`，**不签发 `imageToken`**。下表是 M1.5 的验收口径，M1b 不得据此宣称"图片通道已通"。

`/img` 是唯一"浏览器自己拼 URL 去取"的通道，`<img src>` 带不了自定义头、也不走 WS。所以它**不能复用会话令牌**（那等于把登录凭据写进 URL 与代理日志），也**不能裸奔**。裁定用一条独立的短时凭据：

| 项 | 规则 |
| --- | --- |
| 形态 | 32 hex 随机串，**按会话签发**（一个会话一份，不跨会话共享），只放 query：`?t=<imageToken>` |
| 有效期 | **30–60s**（`ImageTokenSeconds` 默认 45）；到期前 `TTL/3` 由服务端推新 token（旧 token 保留到自己的 `expiresAt`，避免正在加载的 `<img>` 集体 401）；**同一会话同时最多 3 个存活**（每 `TTL/3` 一个，旧的自然过期），E29 的上限按"会话数 × 3"判 |
| 轮换 | 支持主动轮换（`img.token` 请求即换）；轮换后旧 token 在宽限期内仍可用，过期即失效；**登出/被踢/空闲超时 → 该会话的 imageToken 立即作废** |
| 绑定 | 校验三件事：① token 属于**本会话**；② `elementId` 在该会话**当前快照**里（`pageId + pageVersion` 一致）；③ URL 里的 `version` 等于该图元**当前** `imageVersion` |
| 状态码 | 无 token / token 过期 / 会话未登录 → **401**；token 属于**他人会话**、`elementId` 不在当前快照、`version` 过期 → **403**（响应体带稳定码 `E_IMG_TOKEN` / `E_IMG_FORBIDDEN` / `E_STALE_PAGE`，不带堆栈与内部路径） |
| 不进 URL 的东西 | **会话令牌永不进 URL**；`/img` 的 URL 里出现的只有 `imageToken` 与元素/版本。两者语义不同，别互相替代（矩阵 F11/B37） |
| 缓存 | `Cache-Control: private, max-age=30`；**不许 `public`**（会被共享代理缓存）**、不许 `immutable`**（token 会换、URL 会变，`immutable` 只会让"换了 token 还拿旧图"更难查） |
| 审计 | 401/403 不逐条写审计（会刷屏）；按会话每分钟最多一条 `Warn`，含会话、元素、状态码，不含 token 原文 |

### 6.2 基础页与状态机（按 M1b 实际消息定义）

浏览器打开 `/` 后**不是先发 `view.attach`**：连接 WS（带 ticket）后，服务端**主动**发 `hello`，紧接着发 `snapshot`（运行中）或 `idle`（未运行）。客户端状态由收到的消息推导，**没有 `state` 消息、没有 `view.attach`**（两者是 M2 扩展点）。

| 状态（客户端） | 进入条件（M1b 消息） | 浏览器看到 | 可发的消息 | 退出条件 |
| --- | --- | --- | --- | --- |
| `NotRunning` | `hello.running=false` + `idle` | 空态卡片："桌面端未在运行"、在线人数 | `ping`、`hello`（可选确认） | 新 `snapshot`（桌面点运行后的下一拍）→ `ReadOnly` |
| `Unauthorized` | `POST /api/ticket` 401（无凭据且 `AllowAnonymousView=false`，或账号/密码错），或握手 401 | 登录卡片（用户名/密码/错误文案）；`AllowAnonymousView=true` 时另有"以只读身份查看" | `POST /api/ticket`（HTTP） | 取票成功 → 握手 → `ReadOnly` |
| `AtCapacity` | 会话数 ≥ `MaxSessions` | 拒绝页；服务端先发 `bye{reason:"at-capacity",closeCode:1013}` 再关 | 无（没有可用连接） | 有会话退出后用户手动重试 |
| `ReadOnly` | `hello.readOnly=true` + `snapshot` | 画面 + 顶部"只读"徽标 | `ping`、`hello` | `stopped` → `RunStopped`；断线 → `Disconnected` |
| `Loaded` | **M2 起**：`hello.readOnly=false` + `caps` 允许操作 | 画面 + 可点控件 | M2 全部命令 | M2 |
| `RunStopped` | 收到 `stopped` | **保留最后一帧** + 半透明遮罩"运行已停止" | `ping` | 新 `snapshot`（重新运行）→ `ReadOnly` |
| `Disconnected` | 客户端检测到 WS 关闭/心跳超时 | 遮罩 + "连接断开，正在重连（第 N 次）" | 无（等重连） | 用 `sessionToken` 换新 ticket → 握手 → `hello` + `snapshot`/`idle`；退避 **1/2/4/8/10s 封顶** |

**`gate` 字段仍是 M2 目标口径**：`/api/status` 与 `hello` 都不带 `gate`；`DefaultPasswordNotChanged` / `NoUsers` 尚未实现（M1c 未做，U-13 保留），当前 `/api/ticket` 只做账号校验与 `AllowAnonymousView` 开关。**关键取舍（目标）**：默认密码未改时仍然监听、只把登录关死——给一页明确的中文原因，比一个连不上的端口好查得多。

**M2 扩展点**：`auth.login` / `auth.logout` / `view.attach` / `view.detach` / `view.resync` / `state` / `caps` / `presence` / `lease` / `ack` 全部在 M2 引入；引入时**仍用 `type` 字段**，不再新增 `kind`（§9.3）。

### 6.3 一次连接/加载的时序（M1b）

```
浏览器                          服务端（Watson 网络线程）           UI Dispatcher（拍快照/取值）
  │  GET /  ───────────────────────►│ 读 Web\wwwroot\index.html（5 个白名单文件）
  │◄────────────────────────────────│
  │  POST /api/ticket ─────────────►│ 账号校验（后台 Task.Run + PBKDF2）/ 匿名只读开关 / refreshToken 换票
  │◄────────────────────────────────│ {ticket, expiresIn:30, readOnly:true, sessionToken, sessionExpiresIn}
  │  WS /ws?ticket=… ──────────────►│ AuthenticateApiRequest 核销 ticket（单次）
  │◄────────────────────────────────│ 101
  │                                │ ────────────────────────────►│ TryGetCurrent → 建快照
  │◄──────────────────────────────────────────────────────────────│ hello{protocol,server,sessionRef,readOnly,running,…}
  │◄──────────────────────────────────────────────────────────────│ snapshot{seq=1,page:{PascalCase}} 或 idle
  │  hello（可选确认）─────────────►│ 回 hello.ok{sessionRef,readOnly}
  │  ping ─────────────────────────►│ 回 pong{t}
  │                                │      每 100ms：共享值帧 ──────►│ 拍一次 → 序列化一次 → 扇出
  │◄──────────────────────────────────────────────────────────────│ frame{seq=2..,items:[{elementId,property,text,number,bool,color}]}
  │                                │  PageKey 变化（同轮）─────────►│
  │◄──────────────────────────────────────────────────────────────│ page{seq=..,page:{…}}（整份结构）
  │                                │  RunStopped ──────────────────►│
  │◄──────────────────────────────────────────────────────────────│ stopped{runGeneration}
```

---

## 7. 镜像模式与切页语义

### 7.1 唯一驱动

【事实】运行态换页只有一个落点：`ScadaRuntime.SwitchTo`（`Scada\Models\ScadaRuntime.cs:438-450`）：

```
旧页 Unloaded 钩子 → CurrentPage = 新页 → PageLoaded 事件
```

`Navigate` / `GoBack` 都汇到 `SwitchTo`；目标就是当前页时**返回 true 但不发事件**（`ScadaRuntime.cs:395`）。`Stop()` 只发旧页 `Unloaded`，不发 `PageLoaded`（`:306-320`）。

### 7.2 一次切页 = 一对事件

| 事件 | 谁发 | web 做什么 |
| --- | --- | --- |
| `PageEventRaised(page, Unloaded)` | 桌面宿主分发器执行钩子 | **什么都不做**（web 不执行页面事件；这是桌面运行态的语义） |
| `PageLoaded(page)` | `ScadaRuntime.SwitchTo` 内 | 记 `pageId`、`pageVersion`；置"待重建快照"；**不调用 `RaisePageEvent`** |
| 快照重建 | web 在下一拍（≤100ms）比较 `PageKey` 后构建 | 切页 → `pageId` 变 → 每会话页键失效 → 推 `page`（整份 `page` 对象）；`runGeneration` 不变。**M1b 没有 `state`/`caps`** |

> **首帧页例外**：`Start()` 内部的 `PageLoaded` 发生在 `RunStarted` 之前，web 那时还没订阅。首帧快照由 `RunStarted` 处理器直接取 `Session.CurrentPage` 构建（§4.1），不靠补发。

**为什么 web 可以在 `PageLoaded` 里立刻建快照**：快照只读模型（`ScadaPage`/`ScadaElement`）与值源，不依赖 WPF 控件树、也不依赖 `ScadaRuntimeBinder` 是否已建表。桌面宿主之所以要等"布局跑完"（`ScadaRuntimeHost.SchedulePageAfterLayout`，`:779-802`）是因为它要**建控件绑定表**；web 没有控件。

### 7.3 硬规则

1. **web 不提供任何切页入口**（按钮、快捷键、URL 参数都不行）。`/api/status` 里的 `pageId` 只读。
2. **web 命令里的 `Navigate` 动作一律跳过，且必须"在副本上过滤"**：`ScadaActionDispatcher.Dispatch` 里 `ScadaActionType.Navigate` 会**真的切桌面页**（`ExecuteNavigate` → `_navigator.Navigate`），`hook.RequireConfirmation` 为真时还会**弹桌面模态框**（`_confirmations.Confirm`，同步阻塞 UI 线程）。所以 web 侧**不能**"交给 dispatcher 再拦"——它没有拦截点。做法：为本次执行复制一份 `hook.Actions`，**剔除 `Navigate`**、把副本的 `RequireConfirmation` 置 `false`（由 web `confirmToken` 替代，§8.4.1），再把副本交给 dispatcher；被剔除的条目逐条审计 `Skipped / mirror-mode`，被替代的确认审计 `Skipped / mirror-confirm`，其余动作照常执行。**不许**把 `Navigate` 翻译成"请桌面端切页"的隐式请求（那会让浏览器获得间接切页能力）。
3. **切页瞬间的在途命令**：命令带 `pageId` + `pageVersion`；执行时若与当前页不符 → `E_STALE_PAGE`，不执行、回 `snapshot`。这防止"点的是上一页的按钮"。
4. **`Loaded` 只执行一次**：web 永不调用 `RaisePageEvent`，所以桌面配的 `Loaded` 动作不会因浏览器数量翻倍。
5. **`Stop()` 时 web 不执行 `Unloaded`**：桌面宿主已经执行过；web 只推 `RunStopped`。

### 7.4 新运行（`AutoFollowNewRun`）

- `RunStopped` → 推 `stopped{runGeneration}`，客户端保留最后一帧；
- **M1b 不再补发 `idle`**（`stopped` 之后一直停在"运行已停止"遮罩）；新连接或重连才拿 `idle`。30 秒转 `NotRunning` 是 M2 目标口径；
- 新 `RunStarted` → 服务端把每个会话页键作废，下一拍推**完整 `snapshot`**（新 `runGeneration`）；
- 若新运行的文档不同（`Document` 引用变化），旧 `pageId` 全部作废，客户端必须丢弃旧快照；
- 审计记一条 `session.reattach`（actor / session / 旧 runGeneration → 新 runGeneration）。

---

## 8. 权限模型与监管

### 8.1 身份与角色

【事实】`ScadaRole` 三档：`Operator=1 < Engineer=2 < Administrator=3`；`Undefined=0` 是非法值，判定侧一律拒绝（`Scada\Models\ScadaRole.cs`）。**高角色含低角色**（`ScadaRoleExtensions.Satisfies`）。唯一的判定实现是 `ScadaRoleExtensions.Allows`。

【决策】**web 的"未登录"与桌面的"未登录"不同**：

| | 桌面运行窗口 | Web 会话 |
| --- | --- | --- |
| 未登录 | `Operator`（"任何在场的人都有的那部分权限"） | **只能看基础页**：`IsAuthenticated=false` → 不加载运行画面（M1b 无 ticket 只能看登录卡片；M2 不允许 `view.attach`）、不允许任何操作命令 |
| 理由 | 现场开机自动运行、无人值守，`Loaded` 钩子要能跑 | R132A"默认只读"；浏览器是远程入口，不能把"没登录"当成"现场有人" |

**未登录只能看基础页**（`/` 的登录卡片；见 §6.2 `Unauthorized`）。`AllowAnonymousView` 默认 **false**；只有现场显式打开它，未登录才允许加载运行画面进 `ReadOnly`（M2 的 `view.attach`；M1b 是空体取票）——这是例外，不是默认口径。

**两套身份互不影响（必须写进实现注释）**：web 登录**不**改桌面 `ScadaAccessPolicy` 的登录态，桌面登录也**不**改任何 web 会话的角色。理由是 `ScadaAccessPolicy` 是"这一台桌面机前坐着谁"的单值状态（`VisionMaster\Services\ScadaAccessPolicy.cs:32`，UI 线程单例），把它当成"所有浏览器共用的登录态"会让 16 个用户互相顶号。代价是：桌面状态栏看不到 web 用户，web 也看不到桌面登录者——两边各自有在线列表/状态栏，审计里靠 `说明` 列的 `[web s=… ip=…]` 前缀区分（§8.7）。

### 8.2 闸门分层与顺序（逐命令、服务端，M2a as-built）

**路由层（`ScadaWebCommandRouter`，先做；`ScadaWebAccessPolicy` 之前）**：

```
⓪ 身份：命令对象只持有 Session 引用；身份字段一律不采信（§4.3 规则 8）
① 三闸：runGeneration → pageId → pageVersion
         E_STALE_RUN / E_STALE_PAGE；都不过时只报第一道
② 速率：每会话 5/s + 每元素 2/s（跨会话共享）→ E_RATE_LIMITED + retryAfterMs
```

**权限层（`ScadaWebAccessPolicy.Evaluate`，四道，顺序是刻意的）**：

```
① 总闸：AllowCommands == true                 否则 E_POLICY（"当前为只读模式（ScadaWeb.AllowCommands=false）"）
② 会话：ReadOnly == false                     否则 E_POLICY（"当前会话为只读（ReadOnly）"）
        IsAuthenticated == true                否则 E_UNAUTHENTICATED（"未登录会话不可操作"）
③ 角色：RequiredRole 经 ScadaRoleExtensions.Allows
                                               否则 E_ROLE（原因逐字来自 Allows）
④ 策略：ClickPolicy
        HighestRoleOnly：在线已认证会话的最高档可点（平局都可点）；无人登录 → 全员只读
        AllOperators：①②③ 过即允许
                                               否则 E_POLICY
```

**顺序不许换**：路由层先答"这条命令是不是打在当前这一轮、这一页、这一版"（否则会拿旧页的按钮去动新页的对象），权限层再答"要不要管"（总闸）、"你是谁"（会话）、"这个对象要求什么"（角色）、"此刻谁有权点"（策略）。**总闸排在角色之前**——"关掉命令"这件事与你是谁无关；反过来先比角色，会把"只读模式"报成"角色不足"，现场照着日志去加权限，越改越乱（`ScadaWebAccessPolicy.cs:13-21` 逐字写了这条理由）。③ 的原因文案必须原样透传（`需要「工程师」权限，当前是「操作员」`），不许在 web 侧重写措辞——那句话是 `ScadaRoleExtensions.Allows` 的唯一出处。

**`AllowCommands` 是总闸，不是 `ClickPolicy` 的一个值**：前者管"这个部署到底开不开写路径"，后者管"多人里谁能点"。M1 是编译期只读（`E_UNSUPPORTED`），M2a 起是配置期只读（`E_POLICY`），两者**不许混用**（C12 vs C79）。**运行期改 `AllowCommands` 后**：所有会话重算 `caps`（`policyVersion` 取当前 `Environment.TickCount64`）、推 `toast`（"已切换为只读/可操作"）；已在队列里的命令**按执行时的值判**（不按入队时）。

**闸门与 `caps` 的关系**：`caps` 只表达权限层 ①–④ 的结果（逐图元 `canOperate` + `reason`，§9.4），**不表达路由层的三闸与速率**——那些只能在执行期判，客户端看到 caps 允许也可能因版本过期/限速被拒。反过来 caps 说不能点，服务端一定也拒绝（两层判据同源，C76/C55 守门）。

### 8.3 `ClickPolicy`

| 值 | 语义 | 判定 |
| --- | --- | --- |
| `HighestRoleOnly`（默认） | 只有**当前在线已认证用户中的最高角色**可点 | 取所有在线已认证会话的最大 `Role`；等于该角色的会话可点；其余只读并显示原因 |
| `AllOperators` | 所有已认证且有操作权限的用户可点 | 只要权限层 ①②③ 过就允许（总闸/会话/角色）；监管靠 §8.4 |

**默认取 `HighestRoleOnly`**（更保守的一档）：现场要"多人同时操作"时再显式改 `AllOperators`，改完必须按 §8.4 的六条监管逐项验收——默认放开等于让一台没人盯的浏览器也能写变量。

**参数在桌面端**（`AppConfig.json` 的 `ScadaWeb.ClickPolicy`；设置页入口是可选交付），**浏览器端不可改**：客户端拿到的只是 `caps` 结果，改不了策略本身。

**判定时机两处，缺一不可**：

1. **快照/策略变化时**算 `caps`（给界面置灰 + 显示原因）；
2. **命令执行时**再判一次（防 TOCTOU：caps 是几秒前的，用户可能刚被降权/别人刚登录）。

`HighestRoleOnly` 下角色变化、登录/登出、租约变化、`AllowCommands` 改动、`ClickPolicy` 改动、**图元 `RequiredRole` 改动或切页** → 重算并给**所有**会话推新的 `caps`（不是重发快照）。**`caps`/`presence` 广播固定 2 秒一拍**（`ScadaWebHost.cs:2097-2113`：在线列表与 `caps` 同一循环），所以上述变化最迟 2 秒内到达客户端；`policyVersion` 每次广播取 `Environment.TickCount64`（`ScadaWebHost.cs:2188`），是**单调时钟戳而不是自增计数**，客户端只应用比本地大的那份。**逐图元下发**：`caps.elements[]` 每条 `{elementId, canOperate, reason}`，`reason` 逐字来自 `Allows` 或固定文案（`当前为只读模式（ScadaWeb.AllowCommands=false）` / `当前会话为只读（ReadOnly）` / `当前没有已登录的操作者，全员只读` / `只读：当前只有「管理员」可以操作（张三 是「工程师」）`）；`reason` 在 `canOperate=true` 时为空串，**不许写"允许"**（客户端只用它做 tooltip，不解析语义）。C76 是这条的守门人。

**`AllowCommands=false` 时的 caps**：所有图元 `canOperate=false`、`reason="当前为只读模式（ScadaWeb.AllowCommands=false）"`、`policyVersion` 仍随每次广播更新（`TickCount64`）；`hello.readOnly=true`（客户端据此显示"只读"徽标）。此时**不隐藏任何控件**——置灰保留布局（§8.5），否则画面与桌面不一致、视觉 oracle 过不了。

**平局**：多个会话同为最高角色 → **都可点**（除非开了租约）。这是刻意的：工业现场常见"两个工程师同时在场"，强行只让一个人点会变成人为故障。**不引入任何"最早连接优先 / `sessionId` 序数最小"的平局裁决**——那等于让"谁先打开浏览器"决定谁能操作，既与"都可点"的语义冲突，也无法在角色变化后稳定重算（矩阵 C24/C48 按"都可点"判定）。要单人操作就开租约（§8.6）。

**无人登录的边界**：`HighestRoleOnly` 下"最高角色"只在**已认证在线会话**里取；一个都没有时 → 所有人只读（`caps.default=false`，原因"当前没有已登录的操作者"）。`AllOperators` 同理——未登录者永远不能操作（§8.1），所以"没人登录 = 全员只读"。

### 8.4 `AllOperators` 的监管方式（必须全部实现并测试）

**启用闸门（M0.1 裁定：没有监管开关就不许启用 `AllOperators`）**

`ClickPolicy=AllOperators` 不是"改一个枚举值"就能生效的——它必须同时满足下面五项配置。**任一不满足 → 拒绝启用该策略**：`ClickPolicy` 回落到 `HighestRoleOnly`、写一条 Error 并**逐项指出缺哪一条**（不许静默降级、也不许"先跑起来再说"）。矩阵 C44/C65 是这条的守门人，C66 是正向配对。

| 配置 | 默认 | 允许值 | 不满足时 |
| --- | --- | --- | --- |
| `RequireOnlineUsers` | `true` | `true` | 拒绝启用 `AllOperators`（"在线列表被关闭：`ScadaWeb.RequireOnlineUsers=false`"） |
| `RequireAudit` | `true` | **只能是 `true`** | 配置成 `false` 视为非法配置：**加载时夹回 `true` 并写 Error**（不是只报错留 `false`）+ 拒绝启用（审计是"谁按的"唯一凭证，不许关） |
| `RequireRateLimit` | `true` | `true` | 拒绝启用（"速率限制被关闭：`ScadaWeb.RequireRateLimit=false`"） |
| `RequireConfirmForDangerous` | `true` | `true` | 拒绝启用（"危险动作二次确认被关闭：`ScadaWeb.RequireConfirmForDangerous=false`"） |
| `ShowOnlineUsers` | `true` | `true` | 拒绝启用（浏览器看不到在线列表 = 操作员互不知道对方在按） |

`OperationLeaseEnabled`（默认 `false`）是**可选项**，不参与闸门：关着时退化为"都可点 + 串行 + 审计"。另有 `AllOperatorsRequiresLease`（默认 `false`）作为**可选加强**：为 `true` 时 `AllOperators` 下只有租约 holder 可点（因此必须同时 `OperationLeaseEnabled=true`，否则加载即 Error 并把本项夹回 `false`）；它**不改变**五项闸门的判定，也不出现在"缺项拒绝启用"的清单里。

| # | 监管手段 | 实现 | 测试 |
| --- | --- | --- | --- |
| 1 | **命令全局串行** | 单 `Channel` + 单消费者（§4.3） | C56：20 个会话（`MaxSessions>=20`）同时按同一按钮 → 执行顺序 == 入队顺序，命令级审计 20 条 |
| 2 | **审计（两层）** | 每条命令一条**命令级** `ScadaAuditEntry` + dispatcher 的**动作级**行；`说明` 列带 `[web s=… ip=…]` 前缀，**前缀只在 `ScadaAuditWriter.Append(string,…)` 一处拼**（§8.7） | C19/C20/C49/C67 |
| 3 | **在线用户列表** | WS `presence` 消息（2 秒一拍）：字段**只有** `sessionRef` / `user` / `role` / `ip` / `connectedAtUtc` / `lastSeenUtc` / `canOperate`（`ScadaWebHost.cs:2123-2146`）；**每行只带不透明 `sessionRef`，不带 `sessionId`**；`ReadOnly`/`OperateReason`/租约状态**不在这一帧**（租约归 M2b） | C25/C55 |
| 4 | **操作权租约（可选）** | §8.6；`OperationLeaseEnabled=false` 时退化为"都可点 + 串行 + 审计" | C47（acquire → request → approve/reject → 30s 超时 → holder 断线释放 全链路） |
| 5 | **速率限制（双层）** | 每会话 `CommandRatePerSecond`（默认 5/s，**按会话独立**）+ **每元素 `ElementRatePerSecond`（默认 2/s，同一 `elementId` 跨会话共享）**；超限 `E_RATE_LIMITED` + `retryAfterMs`，不执行、不写成功审计 | C45（同一会话 1 秒 6 条不同元素 → 前 5 条执行、第 6 条限速；同一元素 1 秒 3 条 → 第 3 条限速）、C61（100 会话写同一元素 → 全秒合计只执行 2 条）、C64（不同元素 2/s 不误伤） |
| 6 | **危险动作二次确认** | web `confirmToken`（§8.4.1），**不用桌面模态框** | C46（无 token / 默认 30s 内单次成功 / 重放 / 过期 / 他人 token） |

### 8.4.1 危险动作的二次确认：web `confirmToken`（不是桌面模态）

【事实】`ScadaActionDispatcher.Dispatch`（`VisionMaster\Services\ScadaActionDispatcher.cs:171`）在 `hook.RequireConfirmation` 为真时调 `_confirmations.Confirm(request)`（`:192-203`）——那是**桌面模态框**，同步阻塞 UI 线程；同一方法里的 `Navigate` 分支（`:353-354`）会**真的切桌面页**（`ExecuteNavigate` → `_navigator.Navigate`，`:399-409`）。

【决策】web 侧分两步：

1. **动作副本过滤（§7.3 规则 2）**：复制 `hook.Actions`，剔除 `Navigate`，副本 `RequireConfirmation = false`，再交给 dispatcher。这样 dispatcher 既不切页、也不弹框，动作语义（顺序、失败不中断、审计格式）仍与桌面同一份实现。
2. **`confirmToken` 前置**：需要确认的动作，命令到达泵时先校验 token；没有 token 就不进 dispatcher。

| 项 | 规则 |
| --- | --- |
| 申请 | `cmd.confirm.request{action, target}` → 服务端回 `ack{id, ok:true, result:{confirmToken, expiresIn:<ConfirmTokenSeconds>}}`（默认 30） |
| 绑定 | `(sessionId, elementId, actionKind)` 三元组；任一不符即无效 |
| 有效期 | **默认 30s**（`ScadaWebSettings.ConfirmTokenSeconds`，5–300，`AppConfigModel.cs:388`）；`expiresAt` 由服务端时钟算，单调时钟判定 |
| 次数 | **单次**：校验通过即作废（先删后执行，避免执行中重放） |
| 缺失/无效 | `E_CONFIRM` + 审计 `Denied`（detail 带 `reason=missing` / `expired` / `used` / `mismatch`），不执行 |
| 谁能申请 | 与执行同一套闸门（认证 → 角色 → 策略 → 租约）；**申请本身也审计**（命令级 `Web 操作` / `Success`） |
| 需要确认的动作清单 | `ScadaWebOptions.DangerousActions`，默认 `StopFlow` + 运动类动作（`AllowMotionCommands=true` 时才可能出现）；清单外的动作不要求 token。**两闸不互相替代**：运动命令先被 `AllowMotionCommands=false` 挡成 `E_ACTION_FORBIDDEN`，打开之后**还要**过 `confirmToken`；`StopFlow` 不需要打开任何开关，但默认就要 token |
| 跨会话/跨元素/跨动作 | token 绑定 `(sessionId, elementId, actionKind)` 三元组，**任一项不符即 `E_CONFIRM`（reason=mismatch）**；重连后 `sessionId` 变了，旧 token 自动失效（不需要显式清理，30s 后自然过期）；**不许**用"同一个图元上的另一个动作"复用（`actionKind` 参与绑定） |

**为什么不用桌面模态**：`Confirm` 同步阻塞 UI 线程（桌面有人在屏幕前，等一会儿没问题），web 用户不在那台机器前——弹框会把**所有会话**的命令泵一起卡住，直到桌面前有人点掉；而且它把"浏览器里谁按的"变成"桌面前的人确认"，审计主体错位。C46 是这条的守门人。

### 8.5 `HighestRoleOnly` 的显示规则

- 不可点的控件：**置灰但保留布局**（不许 `display:none`，否则画面与桌面不一致，视觉 oracle 过不了）；
- 悬停/点击给出原因（tooltip + 顶部 toast），文案逐字取 `caps.elements[].reason`；**前端不许自己拼原因**（拼一份就多一份口径，且与 `Allows` 分叉）；
- 顶部常驻一行"当前：张三（工程师）｜ 可操作者：李四（管理员）"；
- 未登录时显示"未登录（只读）"；
- **禁用判据只认 `canOperate`**：`canOperate=false` → `pointer-events:none` + `aria-disabled="true"` + 置灰类；`canOperate=true` → 恢复。**不许**用"有没有配钩子"决定置灰（没配钩子的图元点击后回 `E_NOT_OPERABLE`，不是"禁用"——两者的用户含义不同）；
- **失败反馈**：命令回 `error` 时 toast 显示 `message`（中文原因）+ 稳定 `code`（小字），**控件回到命令前的状态**（不做乐观更新，所以不需要回滚动画；`TextField` 例外：保留用户输入与编辑态，只显示原因，见 §10.4）；
- **只读图元**（`IOField`/`TextField`/`ComboBox`/`SelectorSwitch` 在 `AllowCommands=false` 或 `canOperate=false` 时）：不显示可编辑边框、不弹软键盘、不发命令；外观仍按桌面只读态渲染（`IOField` 露出底色）。

### 8.6 操作权租约（`OperationLeaseEnabled=true`）

状态机：

```
None ──acquire（无人持有）──► Held(holder=sessionId, expiresAt=now+LeaseSeconds)
Held ──renew（holder 自己，且有操作）──► Held（顺延）
Held ──release / 到期 / holder 断线──► None
Held ──request（他人）──► Requested(requester)
Requested ──holder 批准──► Held(holder=requester)
Requested ──holder 拒绝 / 30s 超时──► Held(holder 原 holder)
Held ──管理员强制接管（Administrator）──► Held(holder=新)
```

规则：

1. **租约只影响"点击"，不影响"看"**；所有人始终能看到同一页。
2. 租约是**服务端状态**，客户端不能自称 holder；所有命令带 `sessionId`，服务端比对。
3. 租约到期由**节拍**检查（复用 10Hz 帧循环的 `beatMs`，不另起定时器）。
4. holder 断线 → 立即释放 + 推 `presence`。
5. 批准/拒绝/强制接管全部落审计。

### 8.7 审计字段与单写者

【事实】`ScadaAuditEntry(actor, subject, eventText, actionText, outcome, detail)`（`VisionMaster\Services\ScadaAuditWriter.cs:76-121`）；CSV 表头是**固定的 7 列** `时间/操作者/对象/事件/动作/结果/说明`（`ScadaAuditWriter.cs:209-211`）；**`Append` 现在由 `_writeGate` 罩住"序列化 + 写盘"（`:239-248`、`:311`），任意线程调用都安全、行不撕裂**（RH18：6 线程 × 40 条 = 240 行、每行 7 列）。Web 侧走 `IScadaAuditSink`（`Scada\Models\IScadaAuditSink.cs`）那条 8 参数重载（`ScadaAuditWriter.cs:375` 起）。**线程安全不等于顺序正确**：web 的审计仍必须投进命令泵的 UI 队列，由泵按"执行顺序 = 审计顺序"逐条写（§4.3/§11.2）。

【决策】**审计 CSV 保持 7 列，不新增列**；web 的会话与来源 IP 写进 `说明`（`detail`）的**固定前缀**：

```
[web s=<sessionId> ip=<remoteIp>] 变量：21 → 22
```

- **为什么不动列**：既有报表/筛选/导入都按 7 列消费（`ScadaAuditWriter.Header` 是唯一表头来源），加列会同时打破旧文件读取、报表模板与用户脚本；而 `说明` 本来就是自由文本列，前缀是**可过滤的结构化标记**。
- 前缀**只在 web 条目上出现**；桌面条目 `detail` 原样不变（无前缀），所以"有没有前缀"本身就是"这条是不是 web 操作的"判据。
- **前缀的唯一拼装点是 `ScadaAuditWriter.Append(string actor, string sessionId, string clientAddress, string subject, string eventText, string action, string result, string detail)`**（`ScadaAuditWriter.cs:375` 起，内部拼 `$"[web s={sessionId} ip={clientAddress}] {detail}"`）。调用方（M1 的 `ScadaWebAuditBridge`）**只传裸 detail**；**禁止**在 bridge / 泵 / 插件里再拼一遍——那会出现 `[web s=… ip=…] [web s=… ip=…] …` 的双前缀，按前缀过滤与人工阅读同时失效（C67 守门）。
- 为什么放在 writer 而不是 bridge：web 模块只引用 `VM.Scada`、拿不到宿主工程里的 writer 实现，唯一能"一次写对所有人"的位置就是那个重载本身；`ScadaChecks` 的 RH8 已逐字断言该重载产出 7 列 + 前缀。
- `sessionId` 是服务端会话 ID（32 hex）、`ip` 是 Watson 连接的远端地址；两者**都取自连接**，不取客户端消息里的字段（§4.3 规则 8）。
- **跨线程的例外只有一处**：WS 握手鉴权回调（`AuthenticateApiRequest`）在 401 时也要留一条 `Denied`（矩阵 F23）。它不在命令泵线程上，**不许**直接调 `Append`——不是怕撕裂（`_writeGate` 已经挡住），而是**怕顺序**：这条审计会插到"当前正在执行的命令"之间，让"命令级行紧邻其动作级行之前"的判据失效。做法是把这条审计**投进命令泵的 UI 队列**，由泵按同一顺序写；这样 writer 的线程安全只作兜底，不承担顺序语义。
- 过滤口径：`说明` 列按前缀子串过滤（`[web s=` / ` ip=`）；矩阵 C19/C20/C26/C43 按 **7 列 + 该前缀**判定，不再要求第 8/9 列。
- 前缀长度可控（`s=32 hex` + `ip` ≤ 45），不截断 `detail`；单行仍受 `ScadaAuditWriter` 既有的引号转义规则保护。

**两层审计（一次点击可能 2 行，计数按层算）**：

| 层 | 谁写 | `对象` | `事件` | 说明 |
| --- | --- | --- | --- | --- |
| **命令级** | web 泵（`ScadaWebAuditBridge`） | 图元名或 `会话` | `Web 操作` / `Web 会话` / `Web 登录` / `Web 登出` | 一行命令一行；**必带** `[web s=… ip=…]` 前缀；`detail` 写净效果（"变量：21 → 22"） |
| **动作级** | `ScadaActionDispatcher`（与桌面同一份实现） | 动作目标 | 动作类型 | 一条钩子里 N 个动作 → N 行；与桌面同格式、**不带** web 前缀（它不知道 web 存在） |

- **计数口径**：C01（按钮按下）按"命令级 1 行 + 动作级 N 行"计；C41（动作链部分失败）按"3 条动作 3 行 + 命令级 1 行"计。**不许**把两层混算成"每次点击恰好一行"，也不许因为"动作级已经记了"就不写命令级行——命令级行是唯一能回答"谁（哪个会话/IP）下的命令"的那一行。
- 两层都在同一个泵里、同一顺序写：命令级行**先于**它的动作级行（先记"谁下了这条命令"，再记"它做了什么"）。
- 所有动作都被跳过时，命令级行照写（`outcome=Skipped`），否则"有人按了但什么都没发生"在审计里没有痕迹。
- 事件列取值固定四种：`Web 登录` / `Web 登出` / `Web 操作` / `Web 会话`。

| 事件 | `eventText` | `outcome` |
| --- | --- | --- |
| 登录成功 | `Web 登录` | `Success` |
| 登录失败 | `Web 登录` | `Denied`（detail 带失败原因，**不带密码**） |
| 登出 / 超时 / 被踢 | `Web 登出` | `Success`（detail 带 `reason=logout` / `timeout` / `evicted`） |
| 越权（未登录/角色不足/策略不允许/租约被占/**身份字段不符**） | `Web 操作` | `Denied` |
| 写变量 | `Web 操作` | `Success` / `Failed`（detail 写"变量：旧值 → 新值"） |
| 流程启停 | `Web 操作` | `Success` / `Failed` |
| 报警确认 | `Web 操作` | `Success` |
| 被跳过的 `Navigate` / 被 web 二次确认替代的桌面确认 | `Web 操作` | `Skipped`（detail 带 `mirror-mode` / `mirror-confirm`） |
| 会话重连/重挂 | `Web 会话` | `Success` |

**单写者**：**web 侧**所有 `Append` 只在命令泵（UI 线程）里发生（writer 本身已线程安全，但 web 不依赖它保序）；`ScadaWebChecks` 有一条静态扫描：web 工程里除 `ScadaWebAuditBridge.cs` 外不许出现 `IScadaAuditSink.Append`（含 `Audit.Append` 这类旧写法）；握手 401 的 `Denied` 也必须经泵的 UI 队列转一手，**不许**在鉴权回调里直接写。

### 8.8 登录限速与锁定

- 双键计数：`userName`（大小写不敏感）+ `remoteIp`；任一超限即锁定（更严的那一个生效）。
- 窗口 `LoginAttemptWindowSeconds`，上限 `LoginMaxFailures`，锁定时长 `LoginLockoutSeconds`。
- 锁定期间 `auth.login` 直接回 `E_RATE_LIMITED` + `retryAfterMs`，**不做 PBKDF2**（否则等于给攻击者一个 CPU 放大器）。
- `gate != Ok`（出厂密码未改 / 无账号）时直接拒绝，**不累计失败次数**（那不是密码猜测，累计只会把管理员也锁在外面）。
- 失败一律审计（含被锁定的尝试）；**绝不记录密码**，也不记录密码长度。
- 成功登录清空该键的失败计数。

### 8.9 密码与账号

- 复用 `ScadaUserStore.TryValidate`（`VisionMaster\Services\ScadaUserStore.cs:184`），**PBKDF2-SHA256 十万次**（`:105`），必须在后台线程。
- **加锁改造（M0.1 已落地，M0.2 补 CAS）**：`ScadaUserStore` 现在是"两把锁"——`_gate` 只护内存清单（锁内只复制/改一条记录或序列化快照），`_writeGate` 把"改内存 → 序列化 → 写盘"整段串起来，锁序固定 `_writeGate → _gate`（`ScadaUserStore.cs:60-75`、`:155`、`:165`）。**锁外算 PBKDF2**（十万次几十毫秒，持锁算会把桌面登录/用户管理一起卡住）；PBKDF2 之后**二次进锁复核**（角色/记录/密码，`:278-341`），改密/重置走 CAS（`:496-543`）。守门人：`ScadaChecks` 的 `[UC]` 段 **UC1–UC18c**（8 条校验线程 × 200 次 + 增删/刷新/枚举线程 + 改密/重置/删号/降权并发；摘锁变异反证会让 UC9 准确变红）。
- **出厂密码门**：`IsUsingDefaultPassword(DefaultAdminName)` 为 true 时 `gate=DefaultPasswordNotChanged`，登录一律失败；桌面端设置页给一条"远程访问需要先改出厂密码"的提示。
- **握手 ticket**（30s、单次、可选绑定 IP）：由 `POST /api/ticket` 发放，放 WS URL query，供 `AuthenticateApiRequest` 校验；它只证明"连接来自本服务页面"，**不是登录态**（§6.1"握手凭证"）。
- **会话令牌 `sessionToken`（M1c 已落地）**：32 hex，**在 ticket 核销成功时签发**（`ScadaWebTicketService.TryRedeem` → `IssueSessionLocked`），有效期 **8 小时**（`SessionLifetime`，固定值、不提供配置），只在服务端内存表；`hello` 带 `sessionToken` 与 `sessionExpiresIn`。**它不能当 ticket 用**（握手只查一次性表），只能用来换新 ticket：`POST /api/ticket {"refreshToken": "<sessionToken>"}` → 新 ticket（30s/单次）+ **同一个** sessionToken（不轮换）。放 `sessionStorage`，不落 `localStorage`，**永不进 URL**。运行时证据：`_m1c_webchecks.log` M1c-01（`sessionExpiresIn=28799`）、M1c-02（换票 200 且回同一 token）。
- **`imageToken`（图片专用，M1.5；与会话令牌完全分开）**：32 hex、**按会话签发**、30–60s（`ImageTokenSeconds` 默认 45）、可轮换，**只放 `/img` 的 query**（`?t=…`）。它**不能**用来 resume、不能发命令、不能换 ticket；反过来会话令牌也**不能**用来取图。M1b `/img` 一律 404。
- **重连流程（M1c 已落地）**：断线后客户端用 `sessionToken` 换新 ticket → 新 WS 连接 → 服务端发 `hello` + **完整 `snapshot`**（新连接没有页键，必然全量）。**没有 `resume` 消息、没有"旧连接续命"**；同一 `sessionToken` 可以开多条连接（每班次内的多标签/刷新），旧连接不因此被踢。若服务端返回 `E_SESSION_EXPIRED` 或握手 401，客户端清空 token、退回登录卡片。运行时证据：M1c-03（新票重连收到 `hello`）、M1c-04（旧票复用 401）。
- **令牌失效条件（M2a as-built）**：① **会话级撤销**（`ScadaWebSecurityGate.RevokeSession` → 关闭码 **4403**）；② **用户级撤销**（`RevokeUser`：该用户全部令牌与未核销 ticket 一起作废）；③ 改密/降权/删号（桌面端调用 `RevokeUser`）；④ 空闲超时（`SessionIdleTimeoutSeconds`，**仍未实现**，B30）；⑤ **匿名令牌不续期**：`AllowAnonymousView` 改为 `false` 之后，匿名 `refreshToken` 换票回 **`E_SESSION_EXPIRED`**（`ScadaWebTicketService.cs:331-335` 逐字："匿名只读观众已被配置关闭"），不签发新 ticket、不做 PBKDF2；已有匿名连接保留到下次断开（**不主动踢**，避免运维改配置时打断正在看画面的人）。`runGeneration` 变化**不**使令牌失效。**登出 `auth.logout` 不在 M2a**（`ScadaWebCommandKinds` 只有四条命令）；M2a 的撤销通道是服务端 API（踢人/改密/降权由宿主调），`auth.logout` 归 M2 后续批。**撤销必须同时清 `sessionToken` 表与在线会话**——只清内存会话会让"换票"把已撤销的会话重新立起来（C70/C71 守门）。
- **会话表上限（M2a as-built）**：`ScadaWebTicketService.MaxSessionTokens` 默认 **4096**（`:158`），**满了拒绝新会话**（`E_CAPACITY`，`ScadaWebTicketService.cs:354-361`：不挤掉在线的、不排队）；过期条目按 `SessionLifetime` 老化清理。**这与"活跃连接数上限 `MaxSessions`（默认 16，超出 `AtCapacity`/1013）是两张表**：前者管"能记住多少枚 8 小时令牌"，后者管"同时能连多少条 WS"。C72 守门。
- 三者的区别：ticket 管"握手准入"、一次性 30s；`sessionToken` 管"断线换票"、8h 可重复；`imageToken` 管"取图"、短命可轮换、只读。**密码永远不进 URL、不进 ticket、不进日志**；**`sessionToken` 永远不进 URL**（URL 里只允许出现 ticket 与 `imageToken`）。

### 8.10 Origin 与会话安全

- **`Origin` 校验覆盖两处，缺一不可**：① WS 升级前（`Origin` 的主机与请求 `Host` 一致，scheme 允许 http/https，端口按 `Host`/scheme 默认端口比）；② **`POST /api/ticket`**（M2a：跨站页面不能替用户取票）。不匹配 → **403 `E_ORIGIN`** + 审计 `Denied`（`Web 会话`），**不签发 ticket**。**两种情况必须分开**：**没有 `Origin` 头**（缺失/空白）→ **放行**（curl/服务端网关/同源导航，不是"被别的页面驱使的浏览器"）；**`Origin: null` 字面量** → **拒绝**（沙箱 iframe / `file://` / 跨站重定向把来源抹掉了）。C42 是守门人。
- **每 IP 限速（M2a）**：`POST /api/ticket` 按 `(remoteIp, 60s 滑动窗口)` 计数，上限 `TicketRatePerMinute`（默认 **10**，1–600）；第 11 次起 **429 + `E_RATE_LIMITED` + `retryAfterMs`**，不签发 ticket、**不做 PBKDF2**（否则等于给攻击者一个 CPU 放大器）。匿名/登录/换票三种请求**合并计数**（换用户名不能绕过）；与账号级 `LoginMaxFailures` 锁定**同时生效、互不替代**。**取不到来源地址时仍限速**（键 `<unknown>`：宁可误伤一条，也不给"无地址 = 无限"的口子）。C69 是守门人。
- 不接受 `Sec-WebSocket-Protocol` 之外的扩展；不启用压缩（M1；压缩是 M3 的带宽优化项）。
- 消息大小上限：**命令帧 64KB**（`WebSocketSettings.MaxMessageSize`，Watson 侧硬上限）；**快照受 `SnapshotMaxBytes`（默认 2MiB）**；**值帧受 `FrameBudgetBytes`（默认 120KB）**。服务端→客户端不受 64KB 命令上限约束，但各自受上面的预算约束。
- 所有字符串字段做长度上限（用户名 64、密码 256、`requestId` 64、`elementId` 36）。
- **不使用 cookie**（避免 CSRF）；握手准入用一次性 ticket（query，30s/单次），会话令牌只在 WS 消息里传。**服务端不写 `Set-Cookie`**，也没有 `HttpOnly`/`SameSite`/`Secure` 这类判据——判据是"响应里根本没有 cookie"（矩阵 F11）。
- **URL 里允许出现的凭据只有两种**：一次性 `ticket`（`/ws?ticket=…`）与只读的 `imageToken`（`/img/...?t=…`，M1.5）。**`sessionToken` 永不进 URL**；`imageToken` 不参与 WS 握手、不参与命令、不写进 `sessionStorage`（每次轮换覆盖内存变量即可），401/403 的响应体不带 token 原文。
- **`auth:true` 不是可选项**：`auth:false` 实测完全不经过鉴权器（§16.3），所以第一方 WS 路由必须 `auth:true`，且 `AuthenticateApiRequest` 必须在注册路由之前赋值（否则 fail-open）。

### 8.11 一次 web 操作的完整执行路径（M2）

```
WS 线程：收 cmd.* → 大小/协议校验（≤64KB）→ 每会话入站队列 32（TryWrite 失败 → E_BUSY，不阻塞接收循环）
   ↓                                  → 全局 Channel<ScadaWebCommand>（1024；TryWrite 失败 → E_BUSY）
泵（线程池消费者，**没有 SynchronizationContext**；每条 `await _dispatcher.InvokeAsync(...)` 逐条执行）：
  ⓪ 身份：命令只持有 Session 引用；客户端身份字段一律忽略（§4.3 规则 8）
  ① 三闸：runGeneration → pageId → pageVersion → E_STALE_RUN / E_STALE_PAGE；**缺省 → E_BAD_REQUEST**（三闸必填已实现，M2a-3 已关闭；C75 待执行）
  ② 速率：每会话 CommandRatePerSecond（5/s）+ 每元素 2/s → E_RATE_LIMITED（+ retryAfterMs）
  ③ 图元解析：当前页快照里找不到 elementId → E_NOT_FOUND
  ④ 权限层（ScadaWebAccessPolicy，四道）：
       AllowCommands=false → E_POLICY（"当前为只读模式（ScadaWeb.AllowCommands=false）"）
       会话 ReadOnly → E_POLICY；未登录 → E_UNAUTHENTICATED
       角色不足 → E_ROLE（原因逐字来自 Allows）；ClickPolicy 不允许 → E_POLICY
  ⑤ 钩子/可输入：图元存在但没配该事件钩子 / 不可输入 → E_NOT_OPERABLE
  ⑥ 租约（M2 后续批）：OperationLeaseEnabled 时判 holder → E_LEASE_HELD
  ⑦ 确认：危险动作校验 confirmToken（绑定 session+element+action、默认 30s、单次）→ E_CONFIRM
  ⑧ 动作副本过滤：复制 hook.Actions → 先按 AllowedActions 过滤（不在表内 = Skipped/not-allowed）
                    → 剔除 Navigate（Skipped/mirror-mode）→ RequireConfirmation=false（Skipped/mirror-confirm），
                    其余动作保持原顺序；**原 hook 一个字段都不改**
  ⑨ 命令级审计 1 行（`说明` 列带 `[web s=… ip=…]` 前缀；先写，确保"谁下的命令"一定留痕）
  ⑩ 逐个动作调 IScadaActionDispatcher.Dispatch(副本, element.Name, session.UserName) → 动作级审计 N 行
  ⑪ 回执：ack{id, ok, result:{actions:N, skipped:M}} / error{id, code, message}
      异常隔离：整段 try/catch，未预期异常 → error{E_INTERNAL} + 命令级审计 Failed + 泵继续下一条（不重试）
```

**审计顺序的落地细节**：命令级行在进入⑩之前写（这样即使 dispatcher 抛异常也留得下"谁下的命令"），动作级行由 dispatcher 在⑩里逐条写；两层之间没有其它写入者，所以文件里就是"命令级行紧跟着它的动作级行"。

**为什么 web 不直接调 `ScadaRuntime.RaiseElementEvent`**：

1. 它内部用的是**桌面全局** `IScadaAccessPolicy`（`ScadaRuntime.cs:206`），会把"浏览器里的操作员"判成"桌面当前登录者"；
2. 它把事件广播给 `ScadaRuntimeHost` 的处理器，而宿主写审计时署名取的是桌面 `_accessPolicy.CurrentUserName`（`ScadaRuntimeHost.cs:387`）——web 用户会以别人的名字进审计。

所以 `MatchElementEvent` 只做"判据 + 返回命中的钩子"，**执行与署名留给调用方**（桌面仍走原路径，web 走自己的署名）。这也是 `ScadaChecks` 能同时钉住"桌面行为不变"与"web 署名正确"的原因。

**审计两层**：命令级（web 写，带 session/IP）+ 动作级（dispatcher 写，与桌面一致）。多记一行远好过"事后分不清是谁按的"；两层都在同一个泵里、同一顺序写。

**`cmd.input` 与 `cmd.alarm.ack` 走同一条泵**，但不经过 dispatcher：

- `cmd.input` → ⓪①②③④⑤⑥ 同前 → `IScadaValueWriter.TryWriteText(element, targetProperty, text, out error)` → **只有命令级审计**（不经过 dispatcher，所以没有动作级行）；失败回 `E_VALUE`（原因逐字来自宿主写入器，R34）。
- `cmd.alarm.ack` → ⓪①②③④⑤ 同前（报警确认不是 `ScadaActionType` 成员，`AllowedActions` 不管它，权限取 `AckButton.RequiredRole`）→ `IScadaAlarmCommands`（宿主实现，委托 `ScadaAlarmEngine.Acknowledge`）→ **只有命令级审计**；未知 `alarmId` 按 §10.4 的 `E_NOT_FOUND` 处理，**已确认 / 当前不可确认统一回 `E_NOT_OPERABLE`**（as-built：`ScadaWebCommandRouter.cs:484-491`；与矩阵 C16/C31/C63 的"no-op / Skipped"口径冲突，M2b 统一）。

### 8.12 M2a 安全前置六项（写路径打开之前必须先过）

【决策】**M2a 的第一批不是"做六个图元"，而是"先把写路径的四周围起来"**：下面六项任一未落地，`AllowCommands` 必须保持 `false`（默认值），M2 不计开工。它们都是**产品级安全边界**，不是"以后补的加固项"——写路径一开，只读假设立刻变成可利用面。**2026-10-21 10:40 状态：六项均已落地并接线；但矩阵 C68–C80 未跑，写路径仍不开放。**

| # | 前置项 | 判据（可判定） | 拒绝时 | 审计 | 守门 |
| --- | --- | --- | --- | --- | --- |
| 1 | **默认密码 gate** | `POST /api/ticket` 的**顺序固定**：每 IP 限速（第 3 项）→ 账号校验 / PBKDF2（`:1100`）→ **默认密码 gate**（`:1115-1125`）→ 签票。gate 判据 `IsUsingDefaultPassword(user)` 为 true → **403 `E_DEFAULT_PASSWORD`**（`ScadaWebSecurityGate.ErrorDefaultPassword`），响应体带 `gate:"DefaultPasswordNotChanged"`；桌面改密后立即恢复；Web 无任何改密接口（`/change-password` 404）。**gate 在 PBKDF2 之后**：密码错的请求先回 401 `E_AUTH_FAILED`，不会因"默认密码"泄露账号存在性；限速在 PBKDF2 之前，避免爆破打满 CPU | **403 `E_DEFAULT_PASSWORD`**（连接都建立不了，不是 WS 命令错误码） | `Web 会话` / `Denied`，detail `reason=default-password`，**不含密码** | C68（与 B05/F06 同源，C 阶段门槛） |
| 2 | **Origin 同源** | `POST /api/ticket` 与 WS 升级两处都校验；判据是"`Origin` 的主机与请求 `Host` 一致"（scheme 允许 http/https，端口按 `Host`/scheme 默认端口比）；`Origin: null` 字面量、`sub.allowed`、端口变体、大小写变体一律拒绝；**没有 `Origin` 头（缺失/空白）放行**（非浏览器客户端，见 §8.10） | **403 `E_ORIGIN`**（不签发 ticket、不升级） | `Web 会话` / `Denied`，detail `reason=origin` | C42（既有） |
| 3 | **每 IP 限速** | `POST /api/ticket` 每 `(remoteIp, 60s 滑动窗口)` ≤ **10**；第 11 次 429；取不到 IP 时按 `<unknown>` 仍限速；**被拒的那一次不消耗额度**（避免越重试越锁死） | **429 `E_RATE_LIMITED` + `retryAfterMs`**，不签发 ticket、不做 PBKDF2 | `Web 会话` / `Denied`，detail `reason=rate-limited` | C69 |
| 4 | **匿名令牌不续期** | `AllowAnonymousView` 改 `false` 后，匿名 `refreshToken` 换票 → **`E_SESSION_EXPIRED`**（"匿名只读观众已被配置关闭"）；已有匿名连接不主动踢，重连必须登录 | **401 `E_SESSION_EXPIRED`** | `Web 会话` / `Denied`，detail `reason=anonymous-disabled` | C70 |
| 5 | **会话/用户撤销** | 会话级撤销：`RevokeSession` 清表 + 记撤销表，宿主先失效令牌再按 **4403** 关匹配 WS（`ScadaWebHost.cs:2247-2266` / `:1677-1688`）；用户级撤销：`RevokeUser` 使该用户**全部**令牌与未核销 ticket 失效；撤销后换票 **`E_SESSION_REVOKED`** | 已建立的连接关闭码 **4403**；后续命令 **`E_SESSION_REVOKED`**（`TryValidateSessionAlive`，`:1545-1549`；不是 `E_UNAUTHENTICATED`） | `Web 登出` / `Success`，detail `reason=revoked`（用户级带 `scope=user`） | C71（与 C36 分工：C36 测踢人/降权/改密的效果，C71 测撤销表本身）。**代码已落地；浏览器实际 4403 未取** |
| 6 | **会话表上限** | `MaxSessionTokens` 默认 **4096**；满了**拒绝新会话**（`E_CAPACITY`），**不挤掉在线的、不排队**；过期条目按 `SessionLifetime` 老化清理 | **`/api/ticket` 503 `E_CAPACITY`**（登录/匿名这两条会新签令牌的路径，`ScadaWebHost.cs:1076-1085`）；**WS 握手 401 `E_CAPACITY`**（Watson 的 `AuthResult` 只有 401 语义，表达不了 503）；活跃 WS 连接超 `MaxSessions` 仍是 `AtCapacity`/1013（两张表，别混）；WS 容量拒绝会**回收**本次核销的 8h 令牌 | `Web 会话` / `Denied`，detail `reason=session-table-full` | C72 |

**M2a 的默认口径（写死在文档里，实现不许改默认值）**：`AllowCommands=false`、`ClickPolicy=HighestRoleOnly`、`AllowMotionCommands=false`、`OperationLeaseEnabled=false`。**要打开 `AllOperators` 仍必须过 §8.4 的五项监管闸门**；`AllowCommands=true` 只表示"允许写"，不表示"允许所有人写"。

**状态口径（2026-10-21 12:33）**：M2a 的六项安全前置与命令面**已全部落地并接线到 `ScadaWebHost`**（`POST /api/ticket` 三闸、WS 握手、收帧 → 泵、`ack/error`、`caps/presence` 广播、撤销 4403、容量拒绝回收令牌）。验证：`Web\ScadaWebChecks` Release **97 PASS / 0 FAIL**（33 基线 + 4 静态资产 + 11 动作过滤 + 19 安全 + 26 命令 + 4 回归；日志 `_m2a_webchecks13.log` `AA8A43733714`，DLL `E91456D97780`）、`ScadaChecks` Release **2332 通过 / 0 失败**（`_m2a_scadachecks3.log` `92F0CF616A04`），Release 全量重建 0 错误且符号已核；宿主接线冒烟 12/12（Agent 报告，无仓内日志）。**两轮终审 FAIL 后六条 P0 + 第二轮 P0-1/P1-1 已关闭**（新 P0 见 §17.6.1）；第一轮 89/89（`CF142B22F99A`）为历史证据。**矩阵 C68–C80 未全跑（C68/C69/C70/C72 已跑，其余 9 条未执行）**，WS 4403、`E_CAPACITY` 握手、`caps` 热更新仍无运行时证据（真实浏览器双角色 E2E 与租约已于 13:06/13:25 补证（HighestRoleOnly + AllOperators）、HTTP 端到端 4/4 于 13:19 补跑，见 §17.7「M2b 进展」）。所以：台账 M2a 行记 **`地基 PASS（有条件）`**，**不许出现"通过"**；矩阵 C68–C80 在跑出证据之前一律按未执行计（R38）。**`AllowCommands` 保持默认 `false`，写路径不对现场开放。**

---

## 9. 数据协议

### 9.1 传输约定（M1b as-built）

- WS 文本帧，UTF-8 JSON；协议版本在 `hello.protocol` 里给（当前 `"1"`），**帧里没有 `v` 字段**，也没有 `E_PROTO` 的版本协商（M2 再加）。
- **所有消息用 `type` 字段**，**没有 `kind`**。M1b 的完整类型集合：`hello` / `hello.ok` / `snapshot` / `page` / `frame` / `stopped` / `idle` / `pong` / `error` / `bye`。
- **M2a 是增量消息，不改既有 9 种消息的形状**：客户端→服务端加 `cmd.element` / `cmd.input` / `cmd.alarm.ack` / `cmd.confirm.request` / `auth.logout`；服务端→客户端加 `caps` / `ack` / `presence` / `toast`。`hello.protocol` 仍为 **`"1"`**（新类型对旧客户端是未知类型，旧客户端 `console.debug` 忽略，不打断渲染）；`E_PROTO` 版本协商留 M2 后续批。
- **M2a 的 `hello.readOnly` = `!(AllowCommands && IsAuthenticated)`**：未登录或总闸关闭时为 `true`；`readOnly=false` 之后才有 `caps`（未登录/只读也发 `caps`，全 false + 原因，客户端据此置灰）。
- **命名分两套，别统一**：`snapshot` / `page` 的 `page` 对象是 **PascalCase**（`PageId` / `Elements` / `Properties` / `BasePage`…，消费者是 `elements-process.js`）；控制字段与 `frame.items` 是 **camelCase**（`type` / `sessionRef` / `pageId` / `pageVersion` / `runGeneration` / `elementId` / `property` / `text` / `number` / `bool` / `color`）。
- **没有时间戳/节拍字段**：M1b 的 `frame` 不带 `t`、不带 `beatMs`（闪烁是 M1.5，§10.6）。
- 回执 `id` 是 M2（命令幂等）才出现；M1b 的 `error` 没有 `id`。

### 9.2 客户端 → 服务端

**M1b 实际支持（只有两条）**：

| `type` | 字段 | 说明 |
| --- | --- | --- |
| `ping` | `t`（任意 JSON 值） | 心跳；服务端原样回 `pong{t}` |
| `hello` | 无必填字段 | 客户端确认；服务端回 `hello.ok{sessionRef, readOnly}`（服务端已经在握手后主动发过 `hello`，这条不是必需的） |

**M1b 收到其它任何 `type`**（含 M2 的 `cmd.element`）→ `error{code:"E_UNSUPPORTED", message:"M1 只读镜像不处理该消息类型：…"}`，**不执行任何动作**。JSON 解析失败 → `error{code:"E_BAD_JSON"}`。

**M2a 实际支持（`AllowCommands=true` 且已认证）**：

| `type` | 字段（camelCase） | 说明 |
| --- | --- | --- |
| `cmd.element` | `id`（requestId，必填，≤64）、`runGeneration`、`pageId`、`pageVersion`、`elementId`（≤36）、`event`（`Pressed` / `Released`）、`confirmToken?` | 按钮类图元的按下/释放。**释放必须命中"按下时的同一个图元"才发**（§10.4）；`event` 缺失/非法 → `E_BAD_REQUEST`；危险动作必须带 `confirmToken`（§8.4.1） |
| `cmd.input` | `id`、三闸字段、`elementId`、`property`、`text` | 文本/数值/枚举提交；服务端走 `IScadaValueWriter.TryWriteText`（R34），**不做二次类型转换** |
| `cmd.alarm.ack` | `id`、三闸字段、`elementId`、`alarmId?` | 报警确认；`alarmId` 省略 = 确认全部（`AcknowledgeAll`）。权限取 `AckButton.RequiredRole`，不受 `AllowedActions` 控制 |
| `cmd.confirm.request` | `id`、三闸字段、`elementId`、`actionKind` | 申请危险动作令牌；回 `ack.result{confirmToken, expiresIn}`（默认 30）。**申请本身也过权限层四道并写命令级审计** |

- **只有四条命令**（`ScadaWebCommandKinds` 逐字：`cmd.element` / `cmd.input` / `cmd.alarm.ack` / `cmd.confirm.request`）；`auth.logout`、`auth.login`、`view.*`、`lease.*`、`img.token` **都不在 M2a**，收到一律 `E_UNSUPPORTED`。
- **身份字段（`sessionId` / `actor` / `role`）即使带了也被忽略**：命令只持有服务端 `Session` 对象（`ScadaWebCommand.cs:92-104`），身份、只读口径、审计地址、回执通道全从它取。M2a **不产生 `E_IDENTITY`**（没有可比对的客户端值）；伪造身份字段的后果是"改了也没用"（按真实身份判权）。
- **缺 `id` / 字段超长 / `event` 非法 / 三闸缺省** → `E_BAD_REQUEST`，不执行、不写命令级审计（还没形成一条可审计的命令），只记一条 Warn。**三闸必填，缺一 `E_BAD_REQUEST`；顺序 `runGeneration → pageId → pageVersion`；M2a-3 已关闭，C75 待执行**。

**M2 后续批（本批不做）**：`auth.login`（连接内登录）、`view.attach` / `view.detach`、`view.resync`、`lease.*`、`img.token`（M1.5）。

> **握手前**：浏览器先 `POST /api/ticket` 拿一次性 ticket，再连 `WS /ws?ticket=…`（§6.1"握手凭证"）。**M1b 的 `hello` 不带密码、也不建立身份**——身份在取票时已经确定；密码永远不进 WS。

### 9.3 服务端 → 客户端

| `type` | 关键字段 | 说明 |
| --- | --- | --- |
| `hello` | `protocol`、`server`、`sessionRef`、`readOnly`、`running`、`pageId?`、`pageName?`、`pageVersion?`、`online`、`sessionToken`、`sessionExpiresIn`（M1c as-built；`user?` 是 M2） | 连接后**服务端主动发的第一帧**；`readOnly=true` 是 M1 的全局只读 |
| `hello.ok` | `sessionRef`、`readOnly` | 对客户端 `hello` 的确认（可选交互） |
| `snapshot` | `seq`、`runGeneration`、`pageId`、`pageVersion`、`page`（PascalCase，见 §9.4） | 首次/新一轮运行的整份结构 |
| `page` | 同 `snapshot` | **同一轮内 `PageKey` 变化**（切页/结构变更/基座版本变化，M1c）发的整份结构；不是增量 |
| `frame` | `seq`、`runGeneration`、`pageId`、`pageVersion`、`items[]`（见 §9.5） | 10Hz 值帧；M1b 每拍全量 |
| `stopped` | `runGeneration?` | 运行结束；客户端保留最后一帧 |
| `idle` | — | 未运行（握手首屏或重连时） |
| `pong` | `t` | 心跳回执 |
| `error` | `code`、`message` | `E_BAD_JSON` / `E_UNSUPPORTED`；`/api/ticket` 的 HTTP 错误码另见 §6.1 |
| `bye` | `reason`、`closeCode`、`message` | 服务端主动断开；M1b 只有容量拒绝（`at-capacity` / 1013） |
| `caps` | 信封 `{type:"caps", caps:{…}}`；`caps` 内：`policyVersion`（`Environment.TickCount64`）、`runGeneration`、`pageId`、`pageVersion`、`default`、`defaultReason`、`deny[]`（`[elementId, reason]`）、`allow[]`（`[elementId]`） | **M2a**：逐图元操作权（§9.4）；**2 秒一拍广播**（`ScadaWebHost.cs:2097-2113`，与 `presence` 同一循环）；快照图元上的 `CanOperate`/`OperateReason` 是另一条通道，客户端优先读它 |
| `ack` | `id`、`ok`、`result?`（`{actions, skipped}` 或 `{confirmToken, expiresIn}`） | **M2a**：命令成功回执；`id` 与请求 `id` 一一对应 |
| `presence` | `users[]`：`sessionRef` / `user` / `role` / `ip` / `connectedAtUtc` / `lastSeenUtc` / `canOperate`（as-built 小驼峰，`ScadaWebHost.cs:2139-2148`） | **M2a**：在线列表，2 秒一拍；**每行只有不透明 `sessionRef`，没有 `sessionId`**（C25/C50/F04）；**没有 `RoleDisplay` / `ReadOnly` / `OperateReason` / `lease`**（租约归 M2b）；`canOperate` 与命令执行共用 `ScadaWebAccessPolicy`；**没有 `highestRole` 字段**（最高角色由 `caps` 的原因文案表达，不另发） |
| `toast` | `level`（`info`/`warn`/`error`）、`message` | **M2a**：服务端主动提示（如"权限已变化"、"配置已切换为只读"）；不是命令回执，不参与幂等 |

**M2 后续批（本批不发）**：`auth.ok` / `auth.fail`（连接内登录）、`lease`、`img.token`（M1.5）。客户端遇到未知 `type` 只记 `console.debug`，不打断渲染（`app.js` 的 default 分支）。

### 9.4 `snapshot` 结构

```json
{
  "type": "snapshot", "seq": 1,
  "runGeneration": 7,
  "pageId": "6b0f...", "pageVersion": 42,
  "page": {
    "PageId": "6b0f...", "PageName": "总览", "PageVersion": 42,
    "Width": 1920, "Height": 1080, "Background": "#FF101418",
    "BasePage": {
      "PageId": "…", "PageName": "公共基座", "PageVersion": 7,
      "Width": 1920, "Height": 1080, "Background": "#00000000",
      "BasePage": null, "Elements": [ /* 基座图元 */ ]
    },
    "Elements": [ /* 本页图元，PascalCase，见 §4.4 */ ]
  }
}
```

- **信封上的 `pageId` / `pageVersion` 是给客户端做"这帧是不是当前页"的快速判据；`page` 对象里的 `PageId` / `PageVersion` 才是结构本身**（两者必须相等，M1b 实测一致）。
- **`basePageVersion`（M1c 已落地）**：信封上 `"basePageVersion": 3`（`page.BasePage?.PageVersion ?? 0`，**无基座为 `0`**），并进入 `PageKey`；`_m1c_webchecks.log` 的 B10/B14/B18/M1c-05 均为原始 JSON 证据。
- **没有 `layers[]`、没有 `catalogVersion`、没有 `caps`**：图层可见性在 `Elements[].LayerVisible`；目录版本走 `/api/catalog`；权限在 M1b 恒为只读（`hello.readOnly=true`）。

**`caps` 结构（M2a as-built 目标形状）**：

```json
{
  "type": "caps", "policyVersion": 5,
  "runGeneration": 3, "pageId": "6b0f...", "pageVersion": 42,
  "readOnly": false,
  "default": false,
  "defaultReason": "只读：当前只有「管理员」可以操作",
  "elements": [
    { "elementId": "e-5", "canOperate": false, "reason": "需要「工程师」权限，当前是「操作员」" },
    { "elementId": "e-6", "canOperate": true,  "reason": "" }
  ]
}
```

> 约定：① `elements` **只列六个交互图元的 TypeKey**（`Hmi.Button` / `Hmi.BitButton` / `Hmi.TextField` / `Hmi.ComboBox` / `Hmi.SelectorSwitch` / `Hmi.AckButton`），其它图元不出现——"不出现 = 不可交互"；② `default` 是"未列出的交互图元的默认值"，`defaultReason` 是它的原因；③ `canOperate=true` 时 `reason` 必须是**空串**（不许写"允许"）；④ M2a 的服务端算 `caps` 时**只按 `RequiredRole` + `ClickPolicy` + `AllowCommands` + 租约**，**不掺"元素是否配了事件钩子"**（没配钩子是执行期 `E_NOT_OPERABLE`，不是禁用）；⑤ **下发通道有两条，前端优先读快照字段**：快照图元上的 `CanOperate`/`OperateReason`（PascalCase，`elements-interactive.js` 的 `canOperate()` 先看它）优先，其次是 `caps.elements[]`，最后是顶层 `default`/`defaultReason`；旧的 `deny`/`allow` 二元组**服务端不再下发**（前端保留读取只作兜底，不构成第二套协议）。

### 9.5 `frame` 结构（`items` 数组）

```json
{
  "type": "frame", "seq": 17,
  "runGeneration": 3, "pageId": "6b0f...", "pageVersion": 42,
  "items": [
    { "elementId": "e-1", "property": "Value", "text": "0.67", "number": 0.67 },
    { "elementId": "e-2", "property": "Value", "text": "42", "number": 42 },
    { "elementId": "e-7", "property": "Visible", "text": "True", "number": 1, "bool": true },
    { "elementId": "e-9", "property": "Fill", "text": "#FF2D7DD2", "color": "#FF2D7DD2" }
  ]
}
```

- **`items` 是数组，不是 `{elementId: 位置数组}` 的字典**；每条是"某图元的某属性此刻的值"，四类值（`text` / `number` / `bool` / `color`）可同时给，客户端按 `property` 取自己认识的那一类。
- **M1b 每拍全量**：没有差异比较、没有 `full` 字段；没出现在 `items` 里的绑定（解析失败/变量不存在）客户端保持上一帧。
- **`frame` 不带 `baseVersion`、不带 `t` / `beatMs`**：每个帧自带 `seq/pageId/pageVersion`；客户端只在 `pageId` 相同且 `pageVersion` 与本地一致时应用，不一致就**丢弃该帧**，等服务端下一拍的 `page`（§9.6）。`beatMs` 是 M1.5 闪烁相位字段。
- 帧大小预算：M1b 的 10 图元页面单帧只有数 KB，**不适用** 120KB 预算；1000 图元全变的 `FrameBudgetBytes=120KB` 是 **M3/E 阶段**（E04/E05）的验收项。
- **`text` 的格式化（M1c 已落地）**：数值/日期按该绑定的 `DisplayFormat` 格式化（`IFormattable` + InvariantCulture）；`Gain`/`Offset`/`FormatType` 留 M1.5。**矩阵 W1-22 的专项用例仍未跑**，现有证据是 B11/B12/B13 的值帧。

### 9.6 序号、版本与重同步

| 版本 | 作用 | 变化条件 |
| --- | --- | --- |
| `seq` | 服务端→客户端消息序号（`snapshot`/`page`/`frame` 消耗） | 每条递增 1；M1b **不要求客户端处理缺口** |
| `runGeneration` | 运行会话代号 | 每次 `RunStarted` +1；`stopped` 带结束轮次 |
| `pageId` / `pageVersion` | **结构版本**（只随结构变化） | 切页 / `ScadaPage.Version` 变化（图元增删、任一图元任一属性变更、图层变更）；**绑定值变化不递增**。判定是 `PageKey`（见下） |
| `basePageVersion` | 基座页结构版本 | `CurrentBasePage.Version` 变化（**M1c 起进 `PageKey`**；无基座为 `0`） |
| `policyVersion` | 权限策略版本 | M2 |
| `catalogVersion` | 图元目录版本 | M1.5（`/api/catalog`） |

**`PageKey` 与重同步规则（M1c as-built）**：

0. **`PageKey = pageId@pageVersion@basePageVersion`**（M1c 已落地；无基座时 `basePageVersion=0`）。`pageVersion` 是**每页各自**的 `ScadaPage.Version`，切到另一页时可能比上一页**更小**（新页从 1 开始）。所以**先比 `pageId`**：不同 = 切页，整份 `page`；**同页才比 `pageVersion`**；**禁止**任何"`pageVersion` 至少 +1 / 不低于断线前"的断言（跨页必然合法失败，B38 专测）。基座版本变化的运行时证据：M1c-05/M1c-06。
1. **M1b 不做 `seq` 缺口检测**：每拍 `frame` 是全量 `items`，丢一帧后下一拍自然补齐；**没有 `view.resync`**（M2 扩展点）。
2. **客户端 `pageId`/`pageVersion` 与帧不一致**（跳变或回退）→ **丢弃该帧**并记一条 `console.warn`，**等服务端下一拍的 `page`**（服务端在下一拍就发现 `PageKey` 变了并推送）。M1b **不请求全量快照**（没有请求通道）；重连会自然拿到 `snapshot`。
3. **M2a 的命令三闸（执行期）**：`runGeneration` → `pageId` → `pageVersion`，**按这个顺序判、只报第一道**。`runGeneration` 不符 = 命令属于上一轮运行（`E_STALE_RUN`）；`pageId` 不符 = 点的不是当前页（`E_STALE_PAGE`）；`pageVersion` 不符 = 页面结构已变（图元可能已被删除/改名，`E_STALE_PAGE`）。**三闸都通过之后**才进认证/角色/策略。三闸不过一律不执行、写命令级 `Denied`，detail 带实际值与期望值（`run 7≠8` / `page 6b0f≠7c1a` / `ver 42≠43`）。
4. **快照永远可以重建**：服务端不依赖客户端确认；缓存命中与否只影响延迟。
5. **值帧没有"增量合并"这回事**：M1b 是全量覆盖；M1.5 引入差异帧后，没出现在 `items` 里的元素保持上一次的值。

### 9.7 背压与丢弃

| 情形 | 行为（M1b as-built） |
| --- | --- |
| 出站队列满（256） | **丢新帧**（`DropWrite`），`DroppedCount` 自增；连接不断（值帧可丢，下一拍全量覆盖） |
| 出站队列 > 80% | M1b 无降频/无 `toast`（M2 目标口径） |
| 入站队列（M2a） | 每会话 **32**（16–64）；`TryWrite` 失败 → `error{E_BUSY, retryAfterMs}` + 命令级 `Denied(reason=busy)`，**不阻塞接收循环**；全局 `Channel` **1024** 满同样 `E_BUSY`；**不丢已接受命令** |
| `E_BUSY` 之后 | 客户端可重试同一 `requestId`（幂等窗口 5 分钟），排空后成功且不重复执行（C35/C74） |
| `ping` 堆积 | 无专门合并；`ping` 直接回 `pong`（不占发送队列顺序语义） |
| 会话空闲 > `SessionIdleTimeoutSeconds` | M1b **未实现空闲回收**（M1c/M2 目标）；连接靠 WS 关闭/停服回收 |

### 9.8 错误码表（`error.code` / `caps` 的 `code`）

**M1b 实际返回的码**：`E_BAD_JSON`（帧不是合法 JSON）、`E_UNSUPPORTED`（M1 只读镜像不处理该 `type`）、`E_AUTH_REQUIRED`（匿名取票但 `AllowAnonymousView=false`）、`E_AUTH_FAILED`（账号/密码错）、`E_ROLE`（账号角色非法）、`E_BAD_REQUEST`（取票体非法）、`E_IMG_NOT_READY`（`/img` 固定 404）、`E_STATUS`/`E_CATALOG`（读运行态/目录异常）。**下表是 M2a 的完整命令/握手错误码表**（阶段列标明归属；`E_PROTO` 与 `E_LEASE_HELD` 的完整链路在 M2 后续批）。

| 码 | 层 | 触发（M2a） | 客户端处理 | 阶段 |
| --- | --- | --- | --- | --- |
| `E_BAD_JSON` | WS | 帧不是合法 JSON | 忽略该帧、`console.warn` 一次 | M1b |
| `E_UNSUPPORTED` | WS | 未实现的 `type`（`auth.login` / `view.*` / `lease.*` / `img.token`） | 提示"当前版本不支持"；不重试 | M1b/M2a |
| `E_BAD_REQUEST` | WS | 缺 `id` / 字段超长（`id`>64、`elementId`>36、`text` 超限）/ `event` 非法 | 修客户端；不重试 | M2a |
| `E_AUTH_REQUIRED` | HTTP 401 | 匿名取票被拒（`AllowAnonymousView=false` 且空体） | 回登录卡片 | M1b |
| `E_AUTH_FAILED` | HTTP 401 | 账号/密码错 | 显示"用户名或密码不正确" | M1b |
| `E_DEFAULT_PASSWORD` | HTTP 403 | 出厂默认密码未改（`gate=DefaultPasswordNotChanged`） | 显示"请先在桌面端修改出厂密码"；不重试 | M2a |
| `E_SESSION_EXPIRED` | HTTP 401 / WS | 换票令牌不存在/已过期；**匿名观众被配置关闭**（`AllowAnonymousView` 改 false） | 清空 `sessionToken`、回登录卡片 | M1b/M2a |
| `E_SESSION_REVOKED` | HTTP 401 / WS | 令牌被 `RevokeSession` / `RevokeUser` 撤销（踢人/改密/降权/删号） | 清 token、回登录卡片；提示"已被管理员下线" | M2a |
| `E_CAPACITY` | HTTP 401 / 503 | 会话令牌表满（`MaxSessionTokens`，默认 4096），拒绝新会话 | 提示稍后重试；**不驱逐在线会话** | M2a |
| `E_ORIGIN` | HTTP 403 | `Origin` 不匹配（`/api/ticket` 与 WS 两处） | 不重试（配置问题） | M2a |
| `E_RATE_LIMITED` | 429 / WS | 登录锁定、`/api/ticket` 每 IP 超限、命令每会话/每元素超限 | 按 `retryAfterMs` 倒计时；**不自动重试** | M1c/M2a |
| `E_AT_CAPACITY` | 503 / 1013 | 活跃会话数 ≥ `MaxSessions` | 显示拒绝页（不驱逐已有会话） | M1b |
| `E_IMG_NOT_READY` | HTTP 404 | `/img` 在 M1b/M2a 未启用 | 显示占位 | M1b |
| `E_STATUS` / `E_CATALOG` | HTTP 500 | 读运行态/目录异常 | 显示重试按钮 | M1b |
| `E_UNAUTHENTICATED` | WS | 未登录发 `cmd.*`；撤销后仍在发命令 | 回登录卡片 | M2a |
| `E_IDENTITY` | WS | 信封里的 `sessionId`/`actor`/`role` 与连接不符（伪造/串包） | 不重试；刷新页面重新登录 | **M2 后续**（M2a 忽略客户端身份字段，不产生该码） |
| `E_STALE_RUN` | WS | `runGeneration` 与当前不符（上一轮的命令） | 丢弃本地状态，等 `snapshot` | M2a |
| `E_STALE_PAGE` | WS | `pageId` 或 `pageVersion` 与当前不符 | 丢弃该命令；等服务端下一拍 `page` | M2a |
| `E_NOT_FOUND` | WS | `elementId` 不在当前页快照（已删除/切页/隐藏图层不是理由） | 忽略该次点击 + 等 `page`；**`E_UNKNOWN_ELEMENT` 作废**（M1 草案，从未实现） | M2a |
| `E_NOT_OPERABLE` | WS | 图元存在，但没配该事件的钩子 / 不可输入 / 图元类型不匹配该命令 | toast（"该控件没有配置动作"） | M2a |
| `E_ROLE` | WS | `RequiredRole` 不满足（原因逐字来自 `Allows`） | 置灰 + 显示 `message` | M2a |
| `E_POLICY` | WS | `AllowCommands=false`（逐字 `当前为只读模式（ScadaWeb.AllowCommands=false）`）、会话只读、或 `ClickPolicy` 不允许 | 置灰 + 顶部只读原因 | M2a |
| `E_LEASE_HELD` | WS | 操作租约在别人手里 | 显示 holder + "申请接管" | M2 后续 |
| `E_ACTION_FORBIDDEN` | WS | 动作不在 `AllowedActions`（含运动默认禁用） | toast + 审计已记 | M2a |
| `E_CONFIRM` | WS | 危险动作缺/错/过期/已用/他人 `confirmToken` | 重新 `cmd.confirm.request` 后重试一次 | M2a |
| `E_VALUE` | WS | 值转换/写变量失败（原因逐字来自宿主写入器） | **保留编辑态** + 显示原因 | M2a |
| `E_DUPLICATE` | WS | `requestId` 超出 5 分钟幂等窗口后重放 | 不重试；换 `requestId` | M2a |
| `E_BUSY` | WS | 每会话入站队列（32）或全局队列（1024）满 | 按 `retryAfterMs` 用**同一 `requestId`** 重试；**不断开** | M2a |
| `E_PAGE_TOO_LARGE` | WS | 图元数/快照超上限 | 显示"画面过大，请联系管理员拆页" | M2 |
| `E_INTERNAL` | WS 1011 / HTTP 500 | 未预期异常（单条命令隔离） | 提示 + 记日志；**泵继续**，客户端可重试一次 | M2a |
| `E_PROTO` | WS 1002 | 协议版本/字段非法 | 提示升级客户端；断开 | M2 后续 |

### 9.9 M2a 一次操作的完整时序（登录 → caps → 命令 → ack/error）

```
浏览器                                        服务端
  │ POST /api/ticket {user,password} ────────►│ Origin 校验(403 E_ORIGIN) → 每 IP 限速(10/min → 429 E_RATE_LIMITED)
  │                                            │ → 默认密码 gate(403 E_DEFAULT_PASSWORD) → 后台 PBKDF2
  │◄── 200 {ticket(30s), sessionToken(8h), user, role, readOnly} ──│（响应无 Set-Cookie）
  │ WS /ws?ticket=… ─────────────────────────►│ 核销 ticket（单次/30s）→ 建会话（SessionId / SessionRef）
  │◄── 101 ───────────────────────────────────│
  │◄── hello {protocol, sessionRef, readOnly, running, …} ─────────│
  │◄── snapshot（运行中）或 idle（未运行）──────│
  │◄── caps {policyVersion, default, elements[]} ───────────────────│ AllowCommands=false → 全 false + "当前为只读模式（ScadaWeb.AllowCommands=false）"
  │ 点击按钮（caps 允许才发）                    │
  │ cmd.element {id, runGeneration, pageId, pageVersion, elementId, event:"Pressed"} ─►
  │                                            │ WS 线程：≤64KB 校验 → 会话配额 32 → 全局 Channel 1024
  │                                            │ 泵（线程池）：⓪身份 ①三闸 ②速率 ③权限层四道 ④租约 ⑤确认
  │                                            │ ⑥动作副本过滤 ⑦命令级审计 ⑧dispatcher ⑨回执
  │◄── ack {id, ok:true, result:{actions:2, skipped:1}} ────────────│
  │                                            │ 失败：error {id, code, message}（不执行；命令级 Denied）
  │                                            │ 异常：error {id, code:"E_INTERNAL"}（泵继续下一条）

危险动作多一步：
  │ cmd.confirm.request {id, elementId, actionKind} ─►│ ⓪–⑤ 同前 + 申请本身审计
  │◄── ack {id, ok:true, result:{confirmToken, expiresIn:30}} ───────│
  │ cmd.element {…, confirmToken} ─────────────►│ 校验 (session,element,action) + 默认 30s + 单次（先删后执行）
```

**五条时序纪律**：

1. **caps 先于命令**：客户端在收到 `caps` 之前点击只显示"权限未就绪"，**不发命令**；`caps` 的 `pageId/pageVersion` 与本地快照不一致时按"过期 caps"丢弃（等下一份），不许拿旧 caps 直接发命令。
2. **`ack.id` 必须等于请求 `id`**：客户端按 `id` 配对，不许按到达顺序配对（命令是并发的，回执顺序=执行顺序但与发送顺序可能不同）。
3. **没有 `ack` 不等于没执行**：客户端超时后**不许自动重发**（`E_BUSY` 明确可重试用同一 `requestId` 除外），先重连拿 `snapshot`，再决定（C38）。
4. **命令级审计先于动作级**：⑦ 在 ⑧ 之前写，保证"谁下的命令"一定留痕（即使 dispatcher 抛异常）；两条都在同一个泵里、同一顺序（§8.7）。
5. **`presence` 与 `caps` 是广播，不是回执**：它们不带 `id`，不参与幂等；`caps` 的 `policyVersion` 单调递增，客户端只应用比本地新的那份（旧帧乱序到达时丢弃）。

---

## 10. 渲染与图元分期

### 10.1 渲染模型

- **DOM + SVG，绝对定位**：页面容器 `position:relative; width:W; height:H`；每个图元一个 `div`（`position:absolute; left/top/width/height; transform: rotate(deg); transform-origin: 50% 50%`），与 WPF `RenderTransformOrigin=(0.5,0.5)` 对齐（`ScadaElementBase.cs:1103-1105` 注释、`:1154` 赋值）。
- **`z-index` = `ZIndex`**；基座页容器在页面容器之前（更低的层）。
- **父子**：容器（`Hmi.Panel`）用 `overflow:hidden` 的 `div`；子元素放在容器内，坐标按 §4.4 的公式换算。**旋转的容器裁剪**：WPF 的 `ClipToBounds` 在 `RenderTransform` 下也生效；web 用容器 `transform` + 内层 `overflow:hidden` 复刻（M1 需要视觉对照确认，见 W1-24）。
- **缩放适配（M1b as-built）**：外层 `transform: scale(k)`，`k = max(min(viewportW/W, viewportH/H), 0.8)`；`MIN_SCALE=0.8`，自然缩放小于 0.8 时（1280×720 只有 0.589）改为**最小缩放 + 容器滚动**（`#stage.is-scroll`），文字保持可读、画面不裁切；1920×1080 自然缩放 0.922 不触发。不用浏览器缩放（会让 1px 线变粗）。页面坐标始终是逻辑像素。
- **设备像素比**：`dpr` 只影响文字抗锯齿，不影响布局；**`vector-effect: non-scaling-stroke` M1b 未实现**（SVG 线宽随 `k` 变粗/变细），记入 `已知偏差.md`，M1.5 补。
- **文字**：M1b 字体栈是 `"Microsoft YaHei", "PingFang SC", "Segoe UI", system-ui, sans-serif`（`elements-process.css`）；桌面主题用的是 **`Microsoft YaHei UI`**，两者度量不同（≤2px 偏移），记入 `已知偏差.md`。
- **不重排**：值帧只改渲染器内部的样式/文本（`fill` / `textContent` / `stroke` 等），不改图元盒的 `left/top/width/height`；移动动画是 M1.5。

### 10.2 M1 十个图元

图元清单与 `TypeKey` 以 `Scada.Controls\Descriptors\BuiltInElements.cs` 为准。派生量对照的只读 DP 名以各控件 `RegisterReadOnly` 为准。

| # | TypeKey | 渲染要点 | 派生量 / 只读 DP 对照 | 难点与口径 |
| --- | --- | --- | --- | --- |
| 1 | `Hmi.Text` | 文本块：`Text`/`Foreground`/`FontSize`/`FontWeight`/`TextAlignment`/`VerticalContentAlignment` | 无派生量（**`Text` 不可绑**，`BuiltInElements.cs:91-110`） | 多行文本换行与 `TextWrapping`；`Stretch` 不列出，按 `Top` 处理 |
| 2 | `Hmi.Rectangle` | 填充/描边/圆角/中间文字 | 无派生量 | 圆角超过短边一半 → WPF 收敛成胶囊；web `border-radius` 同口径（`min(r, min(w,h)/2)`） |
| 3 | `Hmi.Panel` | 底板：标题行 + 子图元区；`TitleText/TitleHeight/TitleFill/TitleForeground` | 无派生量 | **子坐标换算 + 裁剪**（§4.4）；标题行高 = `Math.Max(0, TitleHeight)`（**显式像素，`TitleText` 为空也照样留出这条**；`TitleHeight=0` 时标题栏整体隐藏）；嵌套底板的层序 |
| 4 | `Hmi.IOField` | 左说明 + 右数值/单位；只读态露出底色、可写态 1px 边框 | `ValueText`、`DisplayBrush`、`DisplayStroke`、`IsEditable` | `Gain/Offset`、`ValueFormat`、`FormatType`（Decimal/Hex/Binary）、`HasMinimum/Maximum` + 越限色；**M1 强制 `IsEditable=false`**（即使 `Mode=InputOutput`）。⚠ 对照 `IsEditable` 时 oracle 控件必须无 `RuntimeContext` 或 `Mode=Output`：控件自己的判定还要求"写通道存在 + 绑定有效 + `CanOperate`"（`IOFieldElement.cs:598-601`），拿运行态上下文去比会把"web 强制只读"误判成不一致 |
| 5 | `Hmi.ProgressBar` | 横/竖条 + 可选数值 | `FillLength`、`ValueText` | `Orientation`；`Minimum==Maximum` 时按 0 处理（不除零）；`FillLength` 是像素长，不是比例。**M1b web 用百分比宽度**（等效，像素取整/圆角处有差，见 `已知偏差.md`） |
| 6 | `Hmi.Indicator` | 圆形/方形灯 + 下方标签 | `LampBrush`（`IsOn` → `OnColor`/`OffColor`） | `Shape` 选项；描边与发光层（WPF 有阴影，web 用 `box-shadow`，写进已知偏差）。**M1b 在非正方形图元上按短边内切圆**（桌面按格子拉伸成椭圆），见 `已知偏差.md` |
| 7 | `Hmi.Lamp` | 四态灯：`Off`/`On`/`Warning`/`Alarm`（熄灭/点亮/警告/报警） | `LampBrush`（`State` 0..3 或枚举名） | 目标口径：未知值按"熄灭"收敛（与 `PumpElement` 同口径）；**M1b 现状是"保持上一次灯色 + console.warn"**（`elements-process.js`），与目标不同，M1.5 统一（§10.2.2/`已知偏差.md`）；`State` 可能是字符串枚举名 |
| 8 | `Hmi.Tank` | 罐体 + 液位填充 + 读数 | `FillRatio`、`FillGeometry`（包围盒）、`ValueText` | `Level` 是 **0~100 的百分比**，`FillRatio = clamp(Level/100, 0, 1)`；`ValueText = Capacity × FillRatio + Unit`（`Capacity` 是罐容，不是分母）；填充是"罐体轮廓 ∩ 底部矩形"——web 用**圆角矩形裁剪 + 内部矩形**（不做几何求交），读数/位号带深色底带（桌面无），两条都见 `已知偏差.md` |
| 9 | `Hmi.Valve` | 蝴蝶结阀体 + 开度填充 + 位号 | `ValveBrush`、`OpeningText`、`FillClipGeometry`（宽度） | `Opening` 0..100；三档色（全关/中间/全开）；填充裁剪宽度 = 可画宽度 × 比例 |
| 10 | `Hmi.Gauge` | 表盘 + 刻度 + 指针 + 读数 | `NeedleAngle`（绝对角）、`NeedleThickness`、`ValueText` | `StartAngle`/`SweepAngle`/`TickDivisions`；公式是 `NormalizeAngle(StartAngle) + NormalizeSweep(SweepAngle) × Ratio()`（`GaugeElement.cs:239-243`，**两处归一化必须照抄**）；指针线宽随尺寸缩放 |

**M1 的对照面按"已实现 / 未实现"拆开（B33/B33b，两份文档逐字一致）**：

| 类别 | 图元 | 已实现（M1b，浏览器渲染器里的视觉派生） | 未实现（M1.5，B33b） |
| --- | --- | --- | --- |
| 无派生量 | `Hmi.Text` / `Hmi.Rectangle` / `Hmi.Panel` | 属性袋 + 视觉 | — |
| 灯 | `Hmi.Indicator` / `Hmi.Lamp` | 灯色按 `IsOn` / `State`（Off/On/Warning/Alarm，0..3 或成员名） | 与 WPF `LampBrush` 逐值对照 |
| 棒图 | `Hmi.ProgressBar` | 比例 `(Value-Min)/(Max-Min)` → 百分比宽度 | **`FillLength` 像素值**（web 用百分比宽度，不是像素）、`ValueText` 的 `ValueFormat` |
| 表盘 | `Hmi.Gauge` | 指针角度（`NormalizeAngle(StartAngle) + NormalizeSweep(SweepAngle) × Ratio()`） | `NeedleThickness` 随尺寸缩放、`ValueText` 格式 |
| 罐体 | `Hmi.Tank` | `FillRatio = clamp(Level/100)`、`ValueText = Capacity × ratio + Unit` | `FillGeometry` 包围盒（web 用圆角裁剪，不做几何求交） |
| 阀 | `Hmi.Valve` | 开度比例 → 填充宽度、`OpeningText` | `FillClipGeometry` 的几何值（web 用裁剪宽度） |
| 数值域 | `Hmi.IOField` | 只读文本（`ValueText` 的原始文本） | `DisplayBrush` / `DisplayStroke` / `IsEditable` 的 DP 对照（M1 恒只读）、`Gain`/`Offset`/`ValueFormat`/`FormatType` |

- **M1b 的"已实现"是视觉层实现，不等于 B33 的 DP 逐值对照已通过**：`ScadaWebValueResolver` 只产出值帧 item，不产出 `NeedleAngle` / `FillRatio` 等派生量；DP 对照需要 M1.5 新增"派生量计算 + 与 WPF 只读 DP 逐值比较"的断言（B33b）。
- **`DisplayFormat` 已落地（M1c）**：帧 `text` 用它格式化（`IFormattable` + InvariantCulture）；`Gain/Offset/FormatType` 留 M1.5。
- 少比一个 = 那一块画错没人发现；但**不许把未实现的项写成"已通过"**（M1b 评审抓到的正是"B33 全绿"的口径问题）。

**进度条/表盘的共同派生量**：`ratio = clamp((Value - Minimum) / (Maximum - Minimum), 0, 1)`（`Minimum==Maximum` → 0）；`ValueText` 走 `ValueFormat`（.NET 格式串）→ web 侧需要实现一个**受支持的格式子集**（`F0-F6`、`N0-N3`、`0.0`、`0.##`、`P0-P2`、`X`），不支持的格式串**原样显示格式串并记一条 Warn**（不许静默显示空）。罐/阀的 `ratio` 各自见上表（`Level/100`、`Opening/100`）。

### 10.2.1 未实现图元的降级（M1 必须有，且必须可判定）

"未知 TypeKey"（注册表里根本没有）与"**已知但 web 未实现**"是两种情况，行为不同：

| 情况 | 判据 | 行为 |
| --- | --- | --- |
| 未知 TypeKey | `IScadaRenderCatalog.All` 里没有该 `TypeKey`（契约只有 `All`，没有 `Find`） | 中性占位 + 类型键文本（D03） |
| **已知未实现** | `All` 里有该 `TypeKey`，但前端渲染器表里没有（M1 只有 10 个） | 按下述六条渲染**中性占位** |

六条规则（缺一条就不可判定）：

1. **占位盒与元素同尺寸同位置**：`left/top/width/height/rotate` 与快照一致（`box-sizing:border-box`），不改变布局、不挤动兄弟元素。
2. **尽量显示文字**：优先 `props.Text` → `props.Name` → `n`（元素名）→ `TypeKey`；`overflow:hidden` 裁剪，不换行撑破盒子。
3. **悬停提示固定文案**：`title` 与角标文本逐字为 `网页暂不支持该图元（{TypeKey}）`，供断言匹配；不许各写一种措辞。
4. **每种 TypeKey 只记一次日志**：服务端 `Warn` 一次（含 TypeKey 与元素数）、前端 `console.warn` 一次；**不许每帧刷屏**。
5. **像素 oracle 排除该块**：对照时把该元素包围盒从 diff 中剔除，并在证据里写"已排除 N 块（TypeKey 列表）"；其余块照常判定。
6. **嵌套在 `Panel` 内按父坐标换算**：占位盒同样走 §4.4 的 `child.left = x - parent.x - ChildrenOriginX`；容器裁剪对占位盒生效，**不许漏出底板**。

**为什么必须做**：M1 只渲染 10 个图元，而现场页面里一定有别的（`Hmi.Button`、`Hmi.AlarmBanner`、`Hmi.ImageView`…）。没有这条规则时，未实现图元要么整块空白（看起来像渲染 bug）、要么整页崩掉；有了它，"网页少一块但页面结构还在"，且能明确告诉操作员"这是能力缺口，不是数据缺失"。**不许**用 `display:none` 静默隐藏（会改变布局与命中测试）。

### 10.2.2 坏值：保留上一次 + 诊断角标（M1.5/M2，M1b 不实现）

> **阶段归属**：坏值角标、`NaN`/`±Inf` 的"保留上一次"、非法枚举的"收敛到安全默认态"全部是 **M1.5/M2**（B35 已按此改）。M1b 只做**值帧透传**：`NaN`/`Inf` 会原样进 `text`，非法枚举由各渲染器自行处理（`Hmi.Lamp` 当前是"保持上一次灯色 + console.warn"，与下表的"收敛到熄灭"不同——这是 M1.5 要统一的差异，见 `已知偏差.md`）。M1 的验收只看"静态结构 + 值帧字段正确"，不看坏值。

值帧里可能出现 `NaN`/`Infinity`、非法枚举名、无法按目标类型解析的文本、`null`。**统一口径**：

| 情形 | 行为 |
| --- | --- |
| `NaN` / `±Infinity` | **保留上一次合法显示值**（文字、指针角度、填充比例都不动），右上角诊断角标 + `title="数值无效"` |
| 非法枚举名 / 越界枚举值 | 收敛到"安全默认态"（灯=熄灭、阀=全关、旋钮=无高亮），角标 + `title="状态值无效：{原值}"`。**为什么不保留上一次**：WPF 控件自己就按"未知值 → 熄灭/全关"收敛（§10.2 表第 7/9 行），web 若保留旧值就会与视觉 oracle 分叉 |
| 转不动的文本（`ValueFormat` 解析失败） | 保留上一次合法文本，角标 + `title="格式无效：{格式串}"`；服务端记一次 Warn（按格式串去重） |
| `null` / 字段缺失 | 视为"本帧没有该字段"，保留上一次值，**不打角标**（这是差异帧的正常情形，§9.6 规则 6） |

角标规则：8px 圆点，固定色 `#FFB000`，不参与像素 oracle（与所在块一起剔除）；角标消失条件 = 下一帧拿到合法值。**不许**把 `NaN` 直接写进 `textContent`（屏幕上会出现 "NaN"）、也不许把指针打到 0°（那会被误读成"值真的掉到零"）。

### 10.3 M1.5

| TypeKey / 项 | 内容 | 为什么单独一批 |
| --- | --- | --- |
| `Hmi.KpiBoard` | 大数字 + 标题 + 单位 + 趋势箭头 | 派生量多（格式/阈值色/趋势），但没有新渲染机制 |
| `Hmi.Clock` | 走时文本 | 纯客户端定时器，与值帧无关；要跟桌面同一时区/格式 |
| `Hmi.Ellipse` | 椭圆/圆 | 与 Rectangle 同族，`border-radius:50%` |
| `Hmi.ImageView` + `/img` + `imageToken` | 图像变量 | **单独 spike**：带宽、编码、缓存、`HImage → BitmapSource` 链路（§4.5）；M1b 固定 404 |
| 动画（Appearance / HorizontalMove / VerticalMove / Visibility / 闪烁） | 按值改色/移位/显隐/闪烁 | M1b 只做静态 + 值帧；`beatMs`、相位、动画状态机都是 M1.5（§10.6） |
| 坏值角标 | `NaN`/`±Inf`/非法枚举/格式失败 | M1.5（§10.2.2）；M1b 不实现 |
| 派生量 DP 对照 | `FillLength` / `FillGeometry` / `FillClipGeometry` / `NeedleThickness` / `ValueText` 格式 | M1.5（B33b）；M1b 只有视觉层实现 |
| `DisplayFormat` / `ValueFormat` / `Gain` / `Offset` / `FormatType` | 文本格式化与量程变换 | `DisplayFormat` **已落地（M1c）**；其余 M1.5（B33b） |

> **ImageView 的阶段归属只有一个口径：M1.5**。矩阵 B32（图像占位与刷新）按 M1.5 计，D18 是 M3 阶段的**回归**（M1.5 已交付，M3 不再首次验证）。M0.1 复评抓到 B32 曾同时被算作 M1 与 M1.5，已统一。

### 10.4 M2 六个交互图元

| TypeKey | 交互（客户端） | 服务端语义 | 只读 / 禁用 | 失败反馈 |
| --- | --- | --- | --- | --- |
| `Hmi.Button` | 按下/释放（`pointerdown` → `Pressed`，`pointerup` 且仍在同一图元 → `Released`） | `cmd.element{event:Pressed/Released}` → `MatchElementEvent` 命中钩子 → 动作副本过滤 → `IScadaActionDispatcher.Dispatch`。**释放必须命中"按下时的同一个图元"才发**（`ScadaCanvas.Interaction.cs:246-252`：在 A 上按下、滑到 B 松手 = 这一下没成）；web 不许用 `setPointerCapture` 强行补发 `Released`，否则"人已经移开了、机器还是启动了" | `canOperate=false` → 置灰 + `pointer-events:none`；`caps` 未到 → 不响应点击 | `error.message` toast；**不做乐观更新**，外观不变（它本来就没有状态） |
| `Hmi.BitButton` | 置位/复位/取反/按下ON/按下OFF | 与桌面同一套语义（`BitButtonElement.cs:50-64`）：**置位=释放写 1；复位=释放写 0；取反=释放时取反；按下ON=按下写 1、释放写 0；按下OFF=按下写 0、释放写 1**。滑开松手 = 这一下没成，但**瞬动模式必须复位**（按下ON 写 0 / 按下OFF 写 1），否则会留下"手已移开、量还按着"（现场表现为电机一直转）。web 侧不许自行发明"点一下切换"，必须与桌面共用同一份决策函数（R-09） | 同 Button；**瞬动模式的"按下态"高亮**在 `canOperate=false` 时不出现 | `E_VALUE`/`E_ACTION_FORBIDDEN` → toast；**瞬动模式失败也必须复位视觉按下态**（否则看起来像一直按着） |
| `Hmi.TextField` | 聚焦 → 输入 → `Enter`/失焦提交（`cmd.input`） | 走 `IScadaValueWriter.TryWriteText`（宿主已按变量声明类型"宽进严出"，R34）；**web 不自己 `Convert.ChangeType`**，否则与 R34 的文本解析规则（不变区域性、拒绝千分位）分叉；失败回 `E_VALUE` + 宿主给的原因 | 只读：不显示可编辑边框、不弹软键盘、不发命令 | **保留编辑态与用户输入** + 显示原因（唯一"不回滚"的图元）；成功后才退出编辑态 |
| `Hmi.ComboBox` | 展开 → 选择（`cmd.input`，`property` = 绑定属性） | 选项来自属性（`Items` JSON）；写回选中值。**选项文本只是显示，写回的是 `Value`**（R40：值仍是成员名/原始值，不是显示名） | 只读：`disabled` 外观 + 不可展开 | 失败 → **恢复原选项** + toast（不回写"失败值"） |
| `Hmi.SelectorSwitch` | 点档位（`cmd.input`，`property` = 档位索引） | 档位写入；越界/非法值由宿主写入器拒绝（`E_VALUE`）；**坏值收敛到"无高亮"**（D11） | 只读：无高亮、不可点 | 失败 → 恢复原档 + toast |
| `Hmi.AckButton` | 确认（`cmd.alarm.ack`，`alarmId?`） | `ScadaAlarmEngine.Acknowledge(alarmId)` / `AcknowledgeAll()`；需要 `IScadaAlarmCommands`（§15）；**不走 dispatcher、不受 `AllowedActions` 控制**，权限取 `AckButton.RequiredRole` | 只读：置灰；**没有未确认报警时也不隐藏**（保留布局） | 未知 `alarmId` → `E_NOT_FOUND`；已确认 → **`ack{ok:true, result:{skipped:1}}`（no-op，不是错误）**；失败 → toast |

**M2a 六图元共同规则**：

1. **按下前 `caps` 已置灰；服务端执行前再判一次；执行后审计；失败回 `error` + 前端 toast（不静默）**——三层缺一不可（C34 证明"前端置灰不是安全边界"）。
2. **命中与手势**：`Pressed`/`Released` 用 `pointerdown`/`pointerup` + 命中测试，不用 `click`（`click` 在滑开松手时也会触发，与桌面语义不符）；触摸目标 ≥ 24px（D23 的口径，M2a 先按 24px 实现）。
3. **不做乐观更新**：所有图元的"成功"外观只由下一帧值帧/`ack` 驱动；失败时控件必须回到命令前状态（`TextField` 例外：保留输入与编辑态）。
4. **同一 `requestId` 不重复发**：`pointerdown` 被重复触发（触摸抖动、双击）时复用同一个 `requestId` 直到 `ack` 到达；`ack` 后换新 id（C02/C37）。
5. **`Released` 的唯一判据是"同一元素 + 指针仍在元素内"**：指针移出后松手不发 `Released`，但**瞬动模式按"滑开松手 = 未成"复位**（见 BitButton 行）。

**坏值覆盖按阶段分家（M0.1 裁定）**：M1 的坏值用例（B35）只覆盖 M1 的 10 个图元——**不含 `Hmi.SelectorSwitch`**（它是 M2 图元）；旋钮的"非法/越界枚举 → 无高亮"由 D11 在 M2 验证。B35 里再写"旋钮无高亮"就是把 M2 的行为塞进 M1 门槛。**M2a 不新增坏值用例**：六个交互图元的坏值表现并入 D10/D11 与 C78（只读/禁用/失败反馈），不另立 ID。

### 10.5 M3 复杂件

| TypeKey | 内容 | 关键难点 |
| --- | --- | --- |
| `Hmi.AlarmBanner` | 实时报警条 | 数据源是 `ScadaAlarmEngine` 三事件（`AlarmRaised/AlarmChanged/AlarmCleared`）+ 活动列表；web 需要一条 `alarms` 推送（与值帧同拍）。宿主通过 `IScadaAlarmFeed`（M3，`VM.Scada`）暴露 `ActiveAlarms` / `UnacknowledgedCount` / 三事件；**不许** web 自己 new 引擎 |
| `Hmi.AxisPanel` / `Hmi.AxisConsole` / `Hmi.AxisPointTable` | 轴监控/运动/点位表 | 只读部分是 `IScadaAxisSource` 快照；操作部分走运动命令（默认禁用）；点位表示教/改速度是**写配置**，web 是否允许要单独裁定（M3 开放问题） |
| `Hmi.DataGrid` | 数据表格 | 1000 行虚拟化（只渲染视口 ± 20 行）；行数据来自变量；列定义 JSON |
| `Hmi.ChartTrend` | 趋势曲线 | Canvas 2D；1000 点/10Hz；**无头像素断言不可行**（R77），只钉"数据进了缓冲 + 描述符齐备"，视觉留给真机 |
| `Hmi.Pump` / `Hmi.Motor` / `Hmi.Pipe` | 工艺图元 | 纯几何 + 状态色；像素 oracle 可覆盖 |

### 10.6 动画与闪烁（M1.5，M1b 不实现）

> **阶段归属**：动画（Appearance / HorizontalMove / VerticalMove / Visibility）与闪烁全部是 **M1.5**，**不是 M1 门槛**。M1b 只渲染快照里的静态属性 + 值帧；快照虽然带 `Animations`，前端 `app.js` 目前只把它当数据保留，没有驱动渲染。`beatMs` 也不在 M1b 的 `frame` 里（§9.5）——下面的闪烁相位是 M1.5 的目标口径。

【事实】四类动画：`Appearance`（区间 → 前景/底色/闪烁）、`HorizontalMove`、`VerticalMove`、`Visibility`（`Scada\Models\ScadaAnimationType.cs`）。闪烁半周期 **500ms**（`ScadaElementBase.BlinkHalfPeriodMilliseconds = 500`，`:513`）；基类把相位落到 `UIElement.Opacity`（亮 = 原透明度，暗 = 0），同一画面所有闪烁图元同相（`ScadaElementBase.cs:780-808`）。

**web 复刻规则**：

1. `Appearance`：按 `States` 的区间匹配当前值 → 覆盖 `Foreground`/`Fill`；`IsFlashing=true` 时进入闪烁。
2. `HorizontalMove` / `VerticalMove`：区间 `[RangeLow, RangeHigh]` 线性映射到 `[元素自身 X, EndX]` / `[自身 Y, EndY]`（`TryEvaluateTargetPosition`，起点是模型里的绝对坐标）；**算出来的是绝对坐标，装进底板要减掉底板位置与 `ChildrenOrigin`**（`ScadaElementBase.TryApplyAnimationValue`，M1 第一件事逐行核对，不许凭注释猜）。
3. `Visibility`：值在区间内 → 显示，否则隐藏（`VisibleInRange`）。
4. 闪烁相位：`on = ((beatMs / 500) | 0) % 2 == 0`；`opacity = on ? baseOpacity : 0`。`beatMs` 由服务端下发（§9.5），所有会话同一值 → 同相。与 WPF 的相位差 ≤ 1 帧（100ms），写进已知偏差。

### 10.7 视觉 oracle 对照方法

1. **同屏同状态**：桌面 `ScadaRuntimeWindow` 以 `RunWindowMode=IndependentWindow` 打开，缩放到页面逻辑尺寸（1:1，96 DPI）；浏览器视口 = 页面尺寸 + 顶栏高度，缩放 `k=1`。
2. **WPF 截图**：`RenderTargetBitmap`（96 DPI，`PixelFormats.Pbgra32`）渲染 `ScadaCanvas` 的页面根；必须带 `Application` + 主题字典（与 `HelpProbe`/`UIThemeSmokeTest` 同范式；`ScadaChecks` 那种"不 new Application"的宿主在这里**不适用**）。
3. **浏览器截图**：Playwright（`channel="msedge"`，无需下载浏览器）`page.screenshot()`，`deviceScaleFactor=1`。
4. **逐块对照**：按图元包围盒切块，逐块比较：
   - 几何：包围盒偏差 ≤ 1px；
   - 颜色：填充/描边主色 ΔE ≤ 3（CIE76）；
   - 文字：允许字体度量导致的 ≤ 2px 偏移与 ≤ 1px 行高差；**文字内容必须逐字相等**；
   - 阴影/发光：允许存在差异，但必须写进 `docs/SCADA运行态Web客户端/已知偏差.md`（M1b 建立 9 条、M1c 追加 2 条，共 11 条）。
5. **派生量对照**（比像素更早失败、更好定位；**M1.5 的 B33b，尚未实现**）：在 STA 线程构造 WPF 控件、设值、读只读 DP，与 web 侧 `ScadaWebValueResolver` 的输出逐条比较（double 1e-6，字符串 Ordinal）。`Web\ScadaWebChecks` 当前只做协议层断言，不承担 DP 对照。

---

## 11. 线程模型与退出链

### 11.1 线程表

| 线程 | 谁 | 允许做什么 | 禁止做什么 |
| --- | --- | --- | --- |
| UI Dispatcher | WPF 主线程 | 全部领域调用：`ScadaRuntime`、`ScadaAlarmEngine`、`ScadaVariableEventEngine`、`ScadaAuditWriter.Append`、`ScadaRuntimeBinder`（桌面）、快照构建、帧组装 | 阻塞等待网络/磁盘/PBKDF2 |
| Watson 网络线程（由库的异步 I/O 调度，不假设"每连接一个线程"） | `ScadaWebHost` | **每连接一个接收循环**（`ReceiveAsync`/`ReadMessagesAsync`）、JSON 解析、大小/协议校验、入队；发送可多线程（`SendTextAsync` 内部有 `SemaphoreSlim _SendLock`，实测 4 线程 × 250 条零丢失） | 碰任何领域对象；写审计；读 `IScadaValueSource`；对同一 session 开多个接收循环 |
| 命令泵消费者（M2a） | 线程池 `Task.Run`，**没有 `SynchronizationContext`** | 从全局 `Channel<ScadaWebCommand>`（1024）逐条取命令，`await _dispatcher.InvokeAsync(...)` 投到 UI 线程执行；自身不碰任何领域对象 | 起在 UI 线程（`await` 续体会被投回 UI，泵退化成"UI 线程上的循环"）；`Thread.Sleep`；吞异常；`Dispatcher.Invoke`（同步阻塞） |
| 登录 worker | 1 个串行 `Task` + `SemaphoreSlim(1)` | `ScadaUserStore.TryValidate`（加锁后） | 碰会话表（结果回泵处理） |
| 图像编码 worker | 1 个串行 `Task` | `BitmapSource` → PNG/JPEG；写缓存 | 碰 `HImage`（值已在 UI 线程转成 `BitmapSource` 并 `Freeze`） |
| 序列化 worker（可选，M0 决定） | 2 个 | `FrameDto` → UTF-8 JSON | 碰领域对象（DTO 已是不可变副本） |

### 11.2 五条线程纪律

1. **领域调用全部回 UI 线程**：网络线程只做"解析 → 校验 → 入队"。
2. **图像编码后台、单次转换 + 按版本缓存**：同一个 `(变量, 版本)` 只编码一次；`Freeze()` 之后跨线程安全。
3. **审计单写者**：web 侧只在泵里写（§8.7）；writer 的 `_writeGate` 只保证"任意线程调用不撕裂"，**不提供顺序**，顺序仍由泵保证。
4. **一个节拍**：值帧、租约到期、会话空闲检查共用一个 10Hz 循环；不另起 `System.Threading.Timer`。
5. **退出任务里不许 `Dispatcher.Invoke`**：退出链可能跑在非 UI 线程、或 Dispatcher 已停摆；`Invoke` 会死锁（R48/R49 的退出链教训同族）。

**UI 线程预算（p95）**：

| 工作 | 预算 | 依据 / 超了怎么办 |
| --- | --- | --- |
| 快照构建（M1b：10 图元） | ≤ 30ms | 未实测；超了记 Warn、提示拆页 |
| 值帧组装（M1b：10 图元全量） | ≤ 8ms | 未实测；超了降 `FrameIntervalMs`（M1.5 接 `FrameRateHz`） |
| 单条命令执行 | ≤ 8ms | M2；泵实测 ≈920 条/秒（`_WebProbe3` §3-D），折合 ≈1.09ms/条。**超 8ms 记 Warn 并继续**（不中断命令） |
| 单条命令端到端 | ≤ 1000ms（指标 **P-CMD**） | **M2 起生效**（M1b 没有命令）；从入队到 `ack`/`error` 回执（含排队） |
| 帧序列化 | ≤ 6ms | 未实测；超了外派给序列化 worker（M1.5） |
| 10Hz 循环总占用 | **≤ 50ms/拍（指标 P-UI）**；> 20ms/拍 记 Warn | M1 按 **10 图元** 验收；**1000 图元的同门槛属 M3/E05**（不是 M1）。超过 50ms 立即降频并记 Error；20ms 是内部告警线，不是验收门槛 |

> 这张表是**预算**不是现状：M1b 的 10 图元帧循环未做逐拍插桩；命令泵的 ≈920 条/秒来自 M0 探针（M2 才有产品命令）。**指标编号已从 `M1/M1b` 改为 `P-UI/P-CMD`**，避免与里程碑 M1b 同名。实现时把实测值写进 `docs/code-changes/`，本文只保留阈值。

### 11.3 退出链

```csharp
new ExitTask("停止 SCADA Web 服务", async () =>
{
    _server.Stop();                       // 1. 不再接受新连接（Watson: WebserverBase.Stop）
    _cts.Cancel();                        // 2. 取消帧循环/序列化循环
    _inbound.Writer.TryComplete();        // 3. 结束入站队列
    await Task.WhenAny(_pumpTask, Task.Delay(3000));   // 4. 等泵退出（有界）
    foreach (var s in _sessions.Snapshot())
        await s.CloseAsync("server-shutdown");         // 5. 关 WS（CloseAsync，不 Invoke）
    _hub.RunStarted -= OnRunStarted;      // 6. 退订
    _hub.RunStopped -= OnRunStopped;
    _images.Dispose();                    // 7. 释放编码缓存
}, 5000)
```

> **M1b as-built**：`ScadaWebModule` 注册的退出任务超时是 **5000ms**；`StopAsync` 先 `Detach()`（锁内取走服务/令牌/会话表、退订 hub、取消令牌、清票），再 `Stop()` 监听、关闭会话（最多等 2s）、等值帧循环（最多 2s）。没有命令泵，所以第 3/4 步在 M1b 不存在。

**顺序理由**：先停止"新工作进入"（监听、队列、泵），再收"已连接的人"，最后退订/释放。反过来的话，收尾过程中新连接还能进来，`Stop` 永远追不上。

**在途命令的排空预算（M0 结论，`_WebProbe3` §5.2/§5.4）**：

- 已接受的命令要么在 2s 内执行完，要么回 `RunStopped`；**不许执行两次**（矩阵 A21）。
- **不要指望 `Abort()` 把 UI 线程从慢领域代码里救出来**：实测对 `Executing` 的 op `Abort()` 返回 false、回调照常跑完；对 `Pending` 的 op 才有效。
- 退出链里对泵的等待用 `WaitAsync(timeout)`，超时只表示"不再等"，**不代表命令没执行**——所以退出前不再补写审计（审计已在回调内写过）。
- 退出链**不许 `Dispatcher.Invoke`**；泵本身跑在无 `SynchronizationContext` 的线程上（§16.4），所以 `Task.WhenAny` 能正常收尾。

**与 `App.OnExit` 的关系**：`App` 已在 `OnExit` 里 `_postStartupCts.Cancel()` 后跑退出链；web 的退出任务注册在 `Initialize`（晚于日志落盘任务），逆序执行 → **先停 web，再落日志**，所以 web 的收尾日志能进文件。

### 11.4 命令泵线程落地（M2a）

```csharp
// 消费者：线程池，没有 SynchronizationContext（起在 UI 线程会让 await 续体投回 UI）
_pumpTask = Task.Run(async () =>
{
    await foreach (var cmd in _inbound.Reader.ReadAllAsync(_cts.Token))
    {
        try
        {
            // 一条命令一次 InvokeAsync：不批量（批量会让"审计顺序 = 执行顺序"变模糊）
            await _dispatcher.InvokeAsync(() => Execute(cmd));   // 同步 lambda；内部不许 await 长任务
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested) { break; }
        catch (Exception ex)
        {
            // 单条异常隔离：回 E_INTERNAL + 命令级 Failed + 一条 Error 日志，泵继续取下一条
            FailCommand(cmd, "E_INTERNAL", ex.GetType().Name);
        }
    }
});
```

**六条纪律**：

1. **泵必须起在线程池**（`SynchronizationContext.Current == null`）。M0 实测（`_WebProbe3` §3-D）：起在 UI 线程时"执行线程只有 UI 线程"照样成立，但**排队延迟随 UI 忙闲抖动**，且 UI 一卡泵就一起停。启动时断言一次，断言宿主里再量一次（A 阶段已覆盖线程模型，M2a 复用）。
2. **`InvokeAsync` 用同步 lambda**：`Execute` 内部不许 `await`；慢操作外派（登录 → auth worker、图像 → 编码 worker），结果回投一条消息（§4.3 规则 4）。
3. **`WaitAsync(timeout)` 只给请求明确答复**，不取消已开始的执行；**审计在 `InvokeAsync` 回调内部写**（§4.3 规则 3）——超时 ≠ 没执行。
4. **看门狗**：单条命令 > **1000ms**（P-CMD 上限）记一条 Error（含 `requestId`/`sessionId`/耗时），**不中断**；M2a 只记日志、不做自动降级（自动降级会把"偶发慢"变成"全站只读"，需要单独裁定）。
5. **退出链**：`_cts.Cancel()` → `_inbound.Writer.TryComplete()` → `Task.WhenAny(_pumpTask, Task.Delay(3000))`；在途命令要么 2s 内执行完、要么回 `RunStopped`，**不执行两次**（A21）；退出链里**不许 `Dispatcher.Invoke`**（§11.3）。
6. **`E_BUSY` 判据只在入队侧**（WS 线程）：会话队列 `TryWrite` 与全局队列 `TryWrite`；泵侧**不做**"队列满就丢"——命令一旦进了全局队列就必须执行或明确失败。

---

## 12. 测试策略

### 12.1 三层

| 层 | 工程 | 参考 | 能测什么 |
| --- | --- | --- | --- |
| 服务端契约 | `Web\ScadaWebChecks\`（控制台，`dotnet run`） | 可引用 `VM.Scada.Controls` + `VisionMaster.ScadaWeb` | 快照/帧/权限/审计/状态机/派生量对照（WPF 控件可离屏构造） |
| 端到端 | `Web\ScadaWebE2E\`（Playwright，`channel="msedge"`） | 启动真实 `VisionMaster.exe` + 浏览器 | 登录、加载、切页跟随、断线重连、像素 oracle |
| 回归 | `ScadaChecks` / `FlowCanvasChecks` / `UIThemeSmokeTest` | 既有 | 确认对宿主/领域层的改动没有破坏既有断言 |

**纪律**（R38/R65）：

- 每条闸门必须有**变异反证**：改坏 → 准确变红 → 还原变绿；反证记录写进 `docs/code-changes/`。
- 改过 `ScadaWebChecks` 源码后**先 `dotnet build` 再 `dotnet run`**，不许 `--no-build`；判据看"条数 + 明细行"，不看"全部通过"。
- **不许写跳过型断言**；确实无法测的（如无头环境的 ChartTrend 像素）写成"不测 + 理由 + 真机验收方式"，不许写成 `Check(name, true, "跳过")`。

**测试基线**：`docs\SCADA运行态Web客户端测试矩阵.md`（**v1.5，261 条**：A30 / B41 / C80 / D32 / E29 / F24 / G16 / X5 / O4；P0 191 / P1 65 / P2 5）。本文的 P0/W1/W2/W3 是**设计层闸门**，矩阵是**执行层用例**；编号对照与 M0 覆盖情况见 §16.6。**两份文档的默认值、协议、状态机、权限策略、审计口径必须逐字一致**——v1.5 已同步：M2a 安全前置六项、`AllowCommands` 总闸、`caps` 逐图元、三闸必填、命令泵两级队列与单条异常隔离、C68–C80。

**编号对照**：`W1-xx` ↔ 矩阵 B 阶段（含 B33/B33b/B34/B36/B38–B40）；`W2-xx` ↔ 矩阵 C 阶段（`W2-04` ↔ C47、`W2-07` ↔ C54、`W2-17` ↔ C45、`W2-18` ↔ C46、`W2-19` ↔ C61–C63、`W2-20` ↔ C65/C66）；`W3-xx` ↔ 矩阵 D 阶段。矩阵 **X 阶段是流程证据**（派工 / 评审签字 / 结果台账 / 文档一致性 / 整改闭环）、**O 阶段是优化阶段用例**（前后对比 / 基线不变 / 可回滚 / 噪声带），都不与 W 编号一一对应。

### 12.2 M0 探针（已执行；`Web\_probe\` 用完即删，R134：`net9.0-windows`）

> M0 实际用三个隔离探针完成：`_WebProbe1`（模块加载 6/6）、`_WebProbe2`（Watson 9/0）、`_WebProbe3`（命令泵 18/0，变异 15/3）。完整台账见 §16；下表保留原始 P0 判据与覆盖状态，**未覆盖项继续有效**。⚠ **探针全绿 ≠ M0 通过**：M0.2 的结论是"**地基 PASS（2242/0）、服务端门禁四项转 M1 首周**"（§17），下表任何一行都不构成里程碑验收。

| ID | 断言 | 通过标准 | M0 状态 |
| --- | --- | --- | --- |
| P0-1 | Watson 7.2.0 在 `net9.0-windows` 下监听、`GET /` 返回静态字节 | 200 + 字节逐字相等 | **部分**：`_WebProbe2` 在 `net9.0` 控制台验证了监听与 `Static.Add` 原样字节；`net9.0-windows` 下的同一路径留待 M1 首启复核 |
| P0-2 | WS 回环延迟（localhost，200 条 1KB 消息） | p95 ≤ 20ms | **未覆盖** |
| P0-3 | `auth:true` 可达性（不配 `AuthenticateRequest`） | 必须给出可复现证据 | **部分（M0.1 未关闭）**：`auth:true` 每次握手恰好触发 1 次 `AuthenticateApiRequest`、`auth:false` 完全不触发、`AuthenticateApiRequest=null` 时 fail-open 已实测（`_WebProbe2` §2-A/B）。**仍缺**：`authCalls==1` 的断言化计数、无/错 ticket 必须 **401 且业务 handler 一次都没执行**（当前只观察到客户端收到 401）、`Static.Add` 参数化路径、`MaxMessageSize=64KB`、`SendTextAsync` 顺序性、`DisconnectWebSocketSessionAsync` 生效——见 §16.8 |
| P0-4 | 泵在 UI 线程：16 会话 × 10Hz × 1000 合成图元，10 分钟 | **10Hz × 1000 图元帧循环的 UI 线程占用 ≤ 50ms/拍**（指标 P-UI，M3/E05）；**单条命令端到端 ≤ 1000ms**（指标 P-CMD，M2）；入站队列不持续增长 | **部分**：`_WebProbe3` 验证了 4 会话 × 250 条、单泵、单审计线程、Seq 1..1000、FIFO；**10Hz × 1000 图元的帧组装/渲染争用未测**（50ms 与 1000ms 是两条独立门槛，别混成一条；M1 只按 10 图元验收） |
| P0-5 | 8 个并发登录（PBKDF2 十万次） | UI 线程停顿 0；总墙钟 ≤ max(500ms, 8×单次×1.2) | **未覆盖（UI 停顿）**：`ScadaUserStore` 两把锁已落地、UC1–UC18c 全绿（M0.2），但"8 并发登录时 UI 线程停顿 0"与持锁时长插桩仍未量 |
| P0-6 | 静态资源防穿越 | 路径穿越全部 404，且无文件读取 | **未覆盖** |
| P0-7 | Playwright + `msedge` 打开页面、建 WS、截图 | 成功出图；版本记录 | **未覆盖** |
| P0-8 | 帧大小实测（1000 图元全变） | 单帧 ≤ 120KB、10Hz ≤ 1.2MB/s/会话 | **未覆盖**（命令泵吞吐已测，帧大小未测） |

**P0 的产物**：`_WebProbe1\README.md` / `_WebProbe2\README.md` / `_WebProbe3\README.md` 与各自 `out\*.log`、`*.csv`；探针工程在仓库内保留（不打包、不参与构建），是否删除由用户裁定。

### 12.3 M1 断言（只读）

| ID | 断言 | 通过标准 |
| --- | --- | --- |
| W1-01 | `Enabled=false` 零副作用 | 端口不监听；`Web\VisionMaster.ScadaWeb.dll` 未被 `LoadFrom`（进程模块列表里没有） |
| W1-02 | 启用后随软件启动即监听（未点运行） | `/api/status` 200；`loadState=NotRunning` |
| W1-03 | `NotRunning` 首屏 | 连接后 `hello.running=false` + `idle`；不推 `snapshot`/`frame` |
| W1-04 | 出厂密码未改 | `gate=DefaultPasswordNotChanged`；`auth.login` 必失败；审计一条 Denied |
| W1-05 | 登录成功 | `auth.ok{role}`；审计一条 Success（`说明` 列带 `[web s=… ip=…]` 前缀，§8.7） |
| W1-06 | 登录失败 5 次后锁定 | 第 6 次 `E_RATE_LIMITED` + `retryAfterMs`；5 条 Denied 审计；锁定期间不做 PBKDF2（耗时 < 5ms） |
| W1-07 | 未登录（默认 `AllowAnonymousView=false`） | `POST /api/ticket {}` → 401 `E_AUTH_REQUIRED`；浏览器显示登录卡片；只能看基础页 |
| W1-08 | 未登录 + `AllowAnonymousView=true` | 空体取票成功；握手后 `hello.readOnly=true` + `snapshot`；画面可见 |
| W1-09 | 运行开始 | 所有**已连接**会话在下一拍收到 `snapshot`（不是 `page`），`pageId == Session.CurrentPage.PageId`；`runGeneration` 递增；未连接会话不补发 |
| W1-10 | 切页 | 恰好一次 `snapshot`；旧 `pageId` 的 in-flight 命令回 `E_STALE_PAGE`；桌面 `Unloaded/Loaded` 各只执行一次（用日志计数） |
| W1-11 | 快照内容 | 元素数 == 模型图元数（含隐藏图层，带 `LayerVisible=false`）；`ZIndex` 与模型一致；父子关系与 `ParentId` 一致 |
| W1-12 | 基座页 | `page.BasePage` 非空时，渲染序在 `page` 之前；基座元素坐标与桌面一致；**已落地（M1c）：信封带 `basePageVersion`（无基座为 `0`），M1c-05/06 实测** |
| W1-13 | 10Hz 帧（M1：10 图元） | `seq` 连续；10 图元下 p95 帧间隔 ≤ 120ms；无队列溢出。**1000 图元的帧率/带宽见 E05（M3）** |
| W1-14 | 全量帧 | 每个 `frame.items` 含该页全部可解析绑定；同一拍内所有会话收到**同一份字节**；没有 `full` 字段；解析失败的绑定不出现（客户端保持上一帧） |
| W1-15 | 丢帧自愈（M1b 无 resync） | 人为丢一帧 → 下一拍全量 `items` 覆盖；**没有 `view.resync`**（M2 扩展点）；`seq` 缺口不导致画面错位 |
| W1-16 | 结构变更 | 桌面改一个图元属性 → **下一拍（≤100ms）**推一次 `page`（同一轮内页键变化），其 `pageVersion` == 当前 `ScadaPage.Version`；**M1b 无 300ms 防抖**；连续改属性会连续发 `page`（M2 再加防抖） |
| W1-17 | `RunStopped` | 推 `stopped{runGeneration}`；客户端保留最后一帧；WS 不断；不执行 `Unloaded` |
| W1-18 | 新运行自动跟随 | `AutoFollowNewRun=true` → 新 `snapshot`，`runGeneration+1`；审计 `session.reattach` |
| W1-19 | 退出链 | 关闭软件：退出链 ≤ 8s；端口释放；无死锁；日志完整 |
| W1-20 | 会话泄漏 | 1000 次连接/断开 → 会话数回 0；私有内存增长 ≤ 20MB |
| W1-21 | 派生量对照（10 图元，**已实现/未实现拆开**） | **已实现（M1b 视觉层）**：Indicator/Lamp 灯色、ProgressBar 比例、Gauge 指针角、Tank 比例+读数、Valve 开度、IOField 只读文本；**未实现（M1.5，B33b）**：`ProgressBar.FillLength` 像素值、`Tank.FillGeometry`、`Valve.FillClipGeometry`、`Gauge.NeedleThickness`、`IOField.DisplayBrush/DisplayStroke/IsEditable` 的 DP 逐值对照（M1 恒只读）。**不许把未实现的项写成已通过** |
| W1-22 | 格式串（**M1c 已落地**） | 帧 `text` 按绑定 `DisplayFormat` 输出（`IFormattable` + InvariantCulture）；`ValueFormat`（F/N/0.0/0.##/P/X）与 `Gain/Offset/FormatType` 是 M1.5；不支持的格式串有 Warn。**矩阵用例 W1-22 未跑**（现有证据是 B11/B12/B13 值帧） |
| W1-23 | 闪烁相位（M1.5） | 同一 `beatMs` 下 web 与 WPF 的 on/off 判定一致（连续 20 拍）；M1b 无 `beatMs`、无闪烁 |
| W1-24 | 像素 oracle | 10 图元 × 3 状态（默认/边界/极值）逐块 diff：几何 ≤1px、主色 ΔE ≤3、文字逐字相等；已知偏差 11 条（`已知偏差.md`）内的差异必须显式排除并计数 |
| W1-25 | 静态资源 | 白名单外 401/404；MIME 正确；`Cache-Control: no-cache`（M1b）；`index.html no-store` 是 M1.5 目标 |
| W1-26 | Origin 校验 | 伪造 Origin → 403 + 审计；同源 → 通过 |
| W1-27 | 心跳/空闲 | 超时 → 会话回收 + 审计 `reason=timeout`；重连需重新认证（`sessionToken` 有效时可换票重连，M1c）；**空闲回收本身仍未实现（矩阵 B30）** |
| W1-28 | 容量 | 第 17 个连接 → `bye{reason:"at-capacity",closeCode:1013}` + close 1013；前 16 个不受影响 |
| W1-29 | 渲染目录一致性 | `IScadaRenderCatalog.Types` 的 `TypeKey` 集合 == `ElementRegistry.All.Select(d => d.TypeKey)`；每个类型的属性 Key 集合 == 描述符属性集合（少/多都红） |
| W1-30 | M1 只读闸 | M1b 构建里任何 `cmd.*` 一律 `error{E_UNSUPPORTED}`（message="M1 只读镜像不处理该消息类型"），不执行、不落"成功"审计；`hello.readOnly=true`；前端不渲染输入态（IOField 恒只读） |
| W1-31 | 未实现图元占位（§10.2.1） | 10 图元页 + `Hmi.Button`/`Hmi.AlarmBanner`/`Hmi.ImageView` 混排：占位盒尺寸/位置与快照一致（±1px）；文字非空；`title` 逐字等于 `网页暂不支持该图元（{TypeKey}）`；每种 TypeKey 日志恰好 1 次；像素 oracle 排除该块并记录排除数；嵌在 `Panel` 内的占位不漏出底板 |
| W1-32 | 坏值处理（§10.2.2，**M1.5/M2**） | 依次注入 `NaN` / `±Infinity` / 非法枚举名 / 越界枚举值 / 转不动的文本：`NaN`/`±Inf`/不可解析文本**保持上一帧合法值**，非法/越界枚举**收敛到安全默认态**（与 WPF 同口径）；角标出现且 `title` 含原因；下一帧合法值后角标消失；屏幕上不出现 `NaN`/`undefined`。**M1b 不执行本用例**（只做值帧透传） |

### 12.4 M2 断言（操作）

| ID | 断言 | 通过标准 |
| --- | --- | --- |
| W2-01 | 未登录点击 | `E_UNAUTHENTICATED`；审计 Denied；不执行任何动作 |
| W2-02 | `HighestRoleOnly` | 只有 admin 在线 → 仅 admin `caps` 允许；admin 登出 → 所有会话 `caps` 变 false，`policyVersion+1` |
| W2-03 | `AllOperators` 串行 | 20 个并发点击 → 执行顺序 == 入队顺序；审计 20 条、顺序一致 |
| W2-04 | 租约 | A 持有 → B `E_LEASE_HELD`；B 申请 → A 批准 → B 可点；A 断线 → 自动释放 |
| W2-05 | 角色不足 | 原因逐字等于 `ScadaRoleExtensions.Allows` 的输出 |
| W2-06 | 写变量审计 | `detail` 含"变量：旧值 → 新值"；与桌面同一次操作格式一致 |
| W2-07 | `Navigate` 跳过 | 含 `Navigate` 的钩子：其它动作执行、`Navigate` 不执行、审计 `Skipped/mirror-mode` |
| W2-08 | 运动默认禁用 | `AllowMotionCommands=false` → `E_ACTION_FORBIDDEN` + 审计 |
| W2-09 | 流程启停 | `AllowedActions` 内有则执行、无则拒绝；结果与桌面一致 |
| W2-10 | 报警确认 | `UnacknowledgedCount` 减 1；审计一条；`AlarmChanged` 推送到所有会话 |
| W2-11 | IOField 提交 | 非数字 / 越界 / 无权限 三种拒绝，文案可直接展示；成功写回后值帧回显 |
| W2-12 | 双写 | 两会话同时写同一变量 → 串行、后写覆盖、两条审计（不是"最后写入者赢"的静默行为） |
| W2-13 | 切页竞态 | 命令发出后桌面切页 → 命令 `E_STALE_PAGE`，不执行 |
| W2-14 | 幂等 | 同 `requestId` 重复提交 → 只执行一次，返回同一结果 |
| W2-15 | 重连风暴 | 10 秒内 100 次重连 → 无泄漏、无重复执行、审计条数 == 成功连接数 |
| W2-16 | 在线列表 | `presence` 含用户名/角色/IP/登录时长/租约；与审计可对上 |
| W2-17 | 速率限制 | 超过 `CommandRatePerSecond` → `E_RATE_LIMITED`，不执行 |
| W2-18 | 二次确认 | 危险动作无 `confirmToken` → 拒绝；token 单次使用；默认 30s 过期（`ConfirmTokenSeconds` 5–300） |
| W2-19 | 默认密码 gate（M2a 前置） | `gate=DefaultPasswordNotChanged` → `POST /api/ticket` 403、不签发 ticket、不做 PBKDF2；桌面改密后立即恢复；Web 无改密接口（C68） |
| W2-20 | `/api/ticket` 每 IP 限速 | 同一 IP 60s 内第 11 次 → 429 `E_RATE_LIMITED` + `retryAfterMs`；换用户名不能绕过；另一 IP 不受影响（C69） |
| W2-21 | 匿名令牌不续期 | `AllowAnonymousView` 改 false 后匿名 `refreshToken` 换票 401 `E_SESSION_EXPIRED`；已有连接不主动踢、重连必须登录（C70） |
| W2-22 | 会话/用户撤销 | 会话撤销关 WS 4403、令牌立即失效；用户级撤销使该用户全部会话失效；换票 401 `E_SESSION_REVOKED`（C71） |
| W2-23 | 会话表上限 | `MaxSessionTokens`（默认 4096）满 → 拒绝新会话 `E_CAPACITY`、**不驱逐在线会话**；过期条目被清理；WS 连接数超限仍 `AtCapacity`/1013（C72） |
| W2-24 | 命令泵单条异常隔离 | 注入一条抛异常的命令 → 回 `E_INTERNAL` + 命令级 `Failed`；**后续命令照常执行且顺序不变**；不重试（C73） |
| W2-25 | `E_BUSY` 两级背压 | 会话队列 32 / 全局队列 1024 满 → `E_BUSY` + `retryAfterMs`，不阻塞接收循环、不丢已接受命令；排空后同 `requestId` 重试成功且不重复执行（C74） |
| W2-26 | 三闸顺序与必填 | `runGeneration` → `pageId` → `pageVersion`；三闸都错只报 `E_STALE_RUN`；审计 detail 带实际/期望值；**缺任一项 → `E_BAD_REQUEST`**（三闸必填已实现，M2a-3 已关闭；C75 待执行）（C75） |
| W2-27 | `caps` 逐图元 | `elements[]` 覆盖六个交互图元、`canOperate/reason` 正确；`AllowCommands=false` 全 false + 逐字原因；登录/撤销/降权/切页 `policyVersion+1` 并广播（C76） |
| W2-28 | `AllOperatorsRequiresLease` | 为 true 且 `OperationLeaseEnabled=false` → 加载 Error + 夹回 false；为 true 时只有 holder 可点（`E_LEASE_HELD`）；**不参与五项启用闸门**（C77） |
| W2-29 | 六图元只读/禁用/失败反馈 | `canOperate=false` → 置灰保留布局 + tooltip 原因 + 不发命令；服务端拒绝 → toast + 控件回原态（`TextField` 保留编辑态）；无乐观更新（C78） |
| W2-30 | `AllowCommands=false` 总闸 | M2a 构建默认值下所有 `cmd.*` → `E_POLICY`（逐字 `当前为只读模式（ScadaWeb.AllowCommands=false）`）；`hello.readOnly=true`；`caps` 全 false；在线列表与值帧照常（C79） |
| W2-31 | 动作副本不改原模型 | web 命令后原 `hook.Actions` 仍含 `Navigate`、`RequireConfirmation` 仍为 true；桌面点同一按钮仍切页/弹确认（C80） |

### 12.5 M3 断言（复杂件）

| ID | 断言 | 通过标准 |
| --- | --- | --- |
| W3-01 | AlarmBanner | 3 条报警时行数/颜色/闪烁与桌面一致；确认后行消失 |
| W3-02 | DataGrid 虚拟化 | 1000 行：DOM 节点 ≤ 300；滚动 60fps；列宽与表头对齐 |
| W3-03 | ChartTrend | 1000 点 10Hz：数据进缓冲（服务端断言）；浏览器 p95 帧 ≤ 16ms；**不钉像素**（R77） |
| W3-04 | 轴三件套 | 只读值与 `IScadaAxisSource` 一致；操作命令走与桌面同一条 `MotionActionExecutor` |
| W3-05 | Pump/Motor/Pipe | 像素 oracle（3 状态 × 3 图元） |
| W3-06 | 全图元冒烟 | `ElementRegistry` 全部已注册 TypeKey 逐一渲染，无异常、无"未知类型" |

### 12.6 暴力测试矩阵（M3/E 阶段；**M1 只做 10 客户端只读**）

> **阶段边界**：1000 图元、50 客户端、100 客户端、24h 全部是 **M3/E 暴力测试**，**不是 M1 门槛**。M1 的性能验收只有 B20（10 图元 × 10 客户端 × 10Hz）与 B09（10 图元首屏 ≤2s）；若用户要求"M1 就 1000 图元"，**另立 M1.5 性能项**（单独立项、单独门槛），不许把本表的指标塞回 B 阶段。

| 场景 | 参数 | 通过标准 |
| --- | --- | --- |
| 多用户并发 | 8 会话 × 10Hz × 1000 图元 × 30 分钟 | 无队列溢出；p95 端到端 ≤ 150ms；p99 ≤ 300ms；**10Hz×1000 图元帧循环的 UI 线程占用 ≤ 50ms/拍**；**单条命令端到端 ≤ 1000ms** |
| 满容量 | 16 会话 × 2Hz × 1000 图元 × 2 小时（`FullRateSessionLimit=4`，其余自动降频） | 服务端 CPU ≤ 单核 40%（开发机实测记录）；内存增长 ≤ 50MB |
| 50 客户端 | 显式 `MaxSessions>=50`；50 会话 × 10Hz × 1000 图元 × 10 分钟 | 50/50 `Loaded`（不出现 `AtCapacity`）；**p95 ≤ 300ms**；CPU < 80%；无 OOM |
| 100 客户端（只读） | 显式 `MaxSessions>=100`；100 会话 × 10Hz × 1000 图元 + 2 路 1080p 图像 × 10 分钟 | ≥95% `Loaded`（其余显式 `AtCapacity`）；**p95 ≤ 500ms**（p99 只记录）；单帧 ≤ 120KB；内存增长 ≤ 50MB |
| 100 客户端（写） | 同 100 客户端；监管全开；同时写同一变量 / 按各自按钮 / 确认同一报警 | 元素级限速 2/s 生效（同元素 100 条只执行 2 条：**命令级 100 条 = 2 Success + 98 Denied、动作级 2 条**）；不同元素全部执行（**命令级 100 + 动作级 100**）；报警只迁移一次（**命令级 100 条，无元素限速**）；无静默丢 |
| 图像带宽 | 4 张 1920×1080 @ 5fps，JPEG q80 | 编码队列不积压（丢帧计数 < 1%）；浏览器内存稳定 |
| 断线重连风暴 | 100 次/10s，持续 5 分钟 | 会话表回 0；无重复命令；无端口耗尽 |
| 24h 长稳 | 4 会话 + 1 桌面运行 + 每小时切页 10 次 | **私有内存增长 ≤ 100MB**；**RSS 仅记录、不设门槛**；`seq` 不溢出；审计按天滚动；无句柄泄漏（GDI/Handle 计数稳定） |

### 12.7 变异反证清单（每条都必须做一次）

| 闸门 | 改坏方式 | 期望 |
| --- | --- | --- |
| W1-14 全量帧 | 只发变化图元、不给基线（模拟差异帧但没有 `full`） | 断言红（首帧缺绑定/字段数不足） |
| W1-10 切页一次 | 让 web 也调 `RaisePageEvent` | `Loaded` 执行次数 == 2 → 红 |
| W2-02 策略 | 把 `HighestRoleOnly` 写成 `AllOperators` | caps 断言红 |
| W2-05 角色 | 在 web 侧重写原因文案 | 逐字断言红 |
| W1-21 派生量 | 改 `NeedleAngle` 公式 +1° | 对照红 |
| W1-25 静态资源 | 允许 `..` | 穿越断言红 |
| W1-19 退出链 | 在退出任务里加 `Dispatcher.Invoke` | 超时/死锁 → 红 |
| `PageVersion` 语义 | 让绑定值变化也递增 `pageVersion` / 给值帧加 `baseVersion` | B17/B18 红（值帧不该引发版本跳变；客户端会把正常值更新当成结构变更） |
| 身份派生 | 采信客户端信封里的 `sessionId`/`role` | C50 红（伪造 `Role=Administrator` 的命令被执行） |
| ticket 单次 | 允许同一 ticket 二次握手 | F23 红（第二次应 401） |
| 占位降级 | 把未实现图元渲染成空白（或 `display:none`） | B36/W1-31 红（占位盒尺寸/文字/提示文案判据） |
| 审计两层 | 只写动作级行、不写命令级行 | C49 红（找不到 `[web s=… ip=…]` 的那一行，无法回答"谁下的命令"） |
| 单条异常隔离 | 去掉 `Execute` 的 try/catch（让异常冒泡到消费者循环） | C73 红（后续命令不再执行，泵已死） |
| `AllowCommands` 总闸 | 把默认值写成 `true`（或跳过该判据） | C79 红（默认部署下命令被执行） |
| 三闸顺序 | 先判角色、后判版本 | C75 红（"点的是上一页的按钮"被报成越权） |
| 撤销 | 只清在线会话、不清 `sessionToken` 表 | C71 红（撤销后换票又把会话立起来） |
| 会话表上限 | 去掉淘汰逻辑（只增不减） | C72 红（重连后条目无界增长） |

---

## 13. 里程碑与验收标准

### 13.1 总表

| 里程碑 | 范围 | 交付物 | 通过标准（全部满足才算过） |
| --- | --- | --- | --- |
| **M0** | 探针与决策锁定 | `_WebProbe1/2/3`（保留，不打包）+ §16 实测台账 | **M0.2 收口：地基 PASS、服务端前置转 M1 首周**——三探针已执行（模块加载 6/6、Watson 9/0、命令泵 18/0）；M0.2 全量 `ScadaChecks` **2242 通过 / 0 失败**（`_m02_run.log`，构建 `_m02_build.log`）；`auth` 写法、队列 16–64、禁用 `DropWrite`、`WaitAsync` 已定。**门禁未过**：`B40` / `A01–A09` / `F23` / `F24` 需要监听器与 web 模块，已显式移到 **M1 首周**（§17.1）；**仍未覆盖**：P0-2/P0-5/P0-6/P0-7/P0-8、§16.8 的六项 Watson 补测。**不许写"M0 全绿"** |
| **M0.1** | 整改与门禁重跑 | §17 四条 blocking 的修复 + 补测；矩阵 A 阶段 P0 全绿 | ① `IScadaRunHub` 状态机可判定（`RunStarted` 在 `Show()` 之后、`RunStopped` 在清空之后、幂等、`Session/Document` 生命周期、`TryGetCurrent` 给晚订阅者）；② 契约下沉与 `ScadaUserStore` 加锁（锁内取记录、锁外算 PBKDF2、`Save` 快照原子写）；③ 模块生命周期/加载器（`Enabled=false` 零加载、`Initialize` 幂等、失败只 Warn、双份契约识别）；④ 认证传输与矩阵同步（ticket 无 cookie、独立 `imageToken`、7 列审计且前缀唯一拼装点、平局、默认值、`PageKey` 语义两份文档逐字一致）。**M0.2 复评：代码本体 PASS（2242/0）；①的 `TryGetCurrent`、③的 `Initialize` 幂等与 `Prism*` 拒绝、②的 `ResetPassword` CAS 与 `TryValidate` 复核均已落地；运行时门禁（B40 / A01–A09 / F23 / F24）转 M1 首周**；状态见 §17.1。**M0.2 裁定：A 阶段 P0 的重跑与这四项合并到 M1 首周执行，M0.1 不再单独作为进 M1 的闸门，M0 以"地基 + 2242/0"收口。** |
| **M1b** | M1 内核批次（服务端 + 前端 10 渲染器） | `Web\VisionMaster.ScadaWeb\`（`ScadaWebHost` / `ScadaWebSession` / `ScadaWebTicketService` / `ScadaWebValueResolver`）、`wwwroot\` 5 文件、`_WebProbe6` | **内核 PASS**：`_WebProbe6` 19/19（两次）、`ScadaChecks` 2266/0；10 渲染器/1920 浏览器实测截图；**交付整改已由 M1c 关闭**（五条 blocking，§17.5）；M1b 本身不等于 M1 收口 |
| **M1c** | M1 交付整改（重连 / 登录入口 / 基座版本 / 协议对齐 / 证据链） | `Web\ScadaWebChecks\`（已入 sln）+ 代码 + 文档 + 台账 | **已关闭（M1c）**：五条 blocking 均有可判定证据（§17.5）——`sessionToken`（8h、不轮换）+ `refreshToken` 换票、无 ticket 登录卡片、`BasePageVersion` 进 `PageKey`、协议 as-built、证据链入台账；`Web\ScadaWebChecks` **33/33**、`_WebProbe6` **31/31**、`ScadaChecks` **2276/0**。**矩阵 B 阶段门槛仍未全跑**（B20/B21/B23/B24/B36 协议层已跑，B33/B34/B39/B40 部分）→ M1 收口仍欠矩阵 |
| **M1** | 只读镜像（= M1b 内核 + M1c 整改） | `Web\ScadaWebChecks\`（已落地）、`Web\ScadaWebE2E\`（计划）、`pack.ps1` 追加（设置页开关为可选项） | **只读核心 PASS、M1 验收矩阵未全跑**。已落地：M1b 内核 + M1c 五条整改；`ScadaWebChecks` 33/33、`_WebProbe6` 31/31、`ScadaChecks` 2276/0；`DisplayFormat` 已落地（矩阵 W1-22 用例未跑）。**未完成**：W1-01~W1-31 未按矩阵执行；B 阶段 P0 中 **B20/B21/B23/B24/B36 协议层已跑；B33/B34/B39/B40 部分**；B35（坏值）/B37（图片鉴权）归 M1.5/M2，不作为 M1 门槛；B26 归 M3。**M2 可开工，M1 全量矩阵待补**；`FlowCanvasChecks` 未纳入本批证据链（并发任务另有 `_array_run_fc4.log` 08:35:18 / 3038 通过，不属 M1c 派工） |
| **M1.5** | KpiBoard / Clock / Ellipse / ImageView | 4 个图元 + 图像通道 | KpiBoard/Clock/Ellipse 各自补一条"派生量对只读 DP"对照（W1-21 风格）+ 一条像素对照（W1-24 风格）；ImageView（B32）带宽实测：4 路 1080p@5fps 编码队列丢帧 < 1%，浏览器内存稳定 |
| **M2a** | M2 第一批：安全前置 + 命令泵 + 权限/监管骨架 + 六图元 | `Web\VisionMaster.ScadaWeb\`（`ScadaWebCommand` / `ScadaWebCommandPump` / `ScadaWebCommandRouter` / `ScadaWebSecurityGate` / `ScadaWebAccessPolicy` / `ScadaWebActionFilter` / `ScadaWebPresence` / `ScadaWebRateLimiter`）+ `wwwroot` 六渲染器 + `ScadaWebChecks` 的 C 段 | **安全前置六项全部落地**（默认密码 gate / Origin / 每 IP 10 次/分钟 / 匿名令牌不续期 / 会话·用户撤销 / 会话表上限，§8.12）；**命令泵**每会话 32 → 全局 1024 → 无 SyncContext 消费者 → UI 逐条，单条异常隔离 + `E_BUSY`；**四条命令**（`cmd.element` / `cmd.input` / `cmd.alarm.ack` / `cmd.confirm.request`）与 `caps` 逐图元；`AllowCommands=false` 默认只读有断言；`HighestRoleOnly` 平局/无人登录有断言；`AllOperators` 五项缺项拒绝有断言；`confirmToken` 默认 30s/单次/跨会话/跨元素有断言；`Navigate` 跳过 + 动作副本不改原模型；**W2-01~W2-31 全绿、矩阵 C 阶段 P0 全绿**（C68–C80 为本批新增）。**as-built（2026-10-21 12:33）：地基 PASS（97/97 + `ScadaChecks` 2332/0，Release 已核符号；第一轮 89/89 为历史证据）；两轮终审六条 P0 + 新 P0-1/P1-1 已关闭；C68–C80 未跑；M2b 可开工** |
| **M2** | 操作（全量 = M2a + 租约 + 在线列表 + 100 客户端并发写） | M2a 全部 + 租约/接管 + `presence` 全链路 + 报警确认全链路 | W2-01~W2-31 全绿；矩阵 C45–C67 与 C68–C80 全绿（速率限制双层、`confirmToken`、租约全链路、`HighestRoleOnly` 三种在线组合、身份伪造、`Navigate` 过滤、并发顺序、重载、100 客户端并发写/按/确认、`AllOperators` 监管闸门、前缀唯一拼装）；`AllOperators` 五项监管开关全部可演示；运动命令默认禁用有断言 |
| **M3** | 复杂件与硬化 | AlarmBanner / 轴三件套 / DataGrid / ChartTrend / Pump / Motor / Pipe | W3-01~W3-06 全绿；暴力测试矩阵全部达标（含 E25–E29）；24h 长稳通过；已知偏差清单闭合；**M3 功能冻结后跑一轮 E 全量，再进优化阶段（矩阵 §14 O01–O04）** |

### 13.2 每个里程碑的"Definition of Done"

1. **代码**：`dotnet build` 0 错误；web 工程 Debug + Release 都构建；`pack.ps1` 产物校验通过（§5.3）。
2. **断言**：对应 ID 全绿；每条新闸门有变异反证记录。
3. **文档**：`docs/code-changes/` 一篇记录（改动清单 / 证据 / 未决问题）；本文相应章节更新（尤其是"待实测"项）。
4. **现场可验收**：给用户一段"怎么打开、看到什么、怎么验证"的三步说明；截图/录屏证据附在记录里。
5. **不通过的处理**：不许"降级后宣称完成"。降级必须写进本文 §14，并标注"未达标项 + 原因 + 计划"。
6. **轮次门禁**（矩阵 §13）：每轮启动做需求→用例映射与用例评审（R00）；每轮完成必须"本轮 P0 全绿 + 新增用例有证据 + 每条新闸门有变异反证 + 相关回归 + 未决清单"（R01）；里程碑过门槛要用户验收、P1 豁免必须书面（R02）；进暴力测试前功能冻结且 E 全量至少一次 24h（R03）；优化后 A–G P0 回归且退化按 **O04 的噪声带**判定（噪声带 < 5% 时用 5%），**O02 未跑不得宣称优化完成**（R04）；发布要结果台账完整 + 真机 8 项 + 渗透签字（R05）。
7. **结果台账**：`docs\SCADA运行态Web客户端\结果台账.md`，每轮每用例一行，列为 `用例 ID / 构建 hash / 执行日期 / 执行人 / 环境 / 结果 / 证据路径 / 缺陷 ID / 评审人 / 备注`（矩阵 §13.2）；没有台账行 = 没跑过。

### 13.3 建议排期（人力充足时）

| 阶段 | 工期 | 说明 |
| --- | --- | --- |
| M0 | 3–5 天 | 探针 + 决策；不写产品代码 |
| M0.1 | 2–4 天 | 四条 blocking 整改 + 六项 Watson 补测 + 矩阵同步；**不通过不进 M1** |
| M1b | 已完成（内核） | 服务端 + 前端 10 渲染器 + `_WebProbe6`；**不等于 M1 收口** |
| M1c | 已完成（1 周） | 五条 blocking 关闭（§17.5）；**矩阵 B 阶段待补** |
| M1 | 3 周（含 M1b/M1c） | 服务端 1.5 周 + 前端 1 周 + 对照/修正 0.5 周 |
| M1.5 | 1 周 | 其中 ImageView spike 至少 3 天 |
| M2a | 1.5 周 | 安全前置六项（3 天）+ 命令泵与四条命令（3 天）+ 权限/监管/caps（2 天）+ 六图元（2 天）+ 断言与变异反证（1.5 天） |
| M2 | 3 周（含 M2a） | 命令泵/权限 1 周 + 图元 1 周 + 监管/测试 1 周 |
| M3 | 4 周 | 复杂件 2.5 周 + 暴力测试/长稳 1.5 周 |

---

## 14. 风险与开放问题

| # | 风险 / 问题 | 影响 | 触发条件 | 缓解 | 状态 / 谁拍板 |
| --- | --- | --- | --- | --- | --- |
| R-01 | ~~Watson `auth:true` 在无 `AuthenticateRequest` 时不可达~~ | — | M0 P0-3 | **已关闭（M0）**：`auth:true` 可达，每次握手触发 1 次 `AuthenticateApiRequest`；真正的问题是 `AuthenticateApiRequest=null` 时 **fail-open**，用"启动断言三连 + 一次性 ticket"堵住（§6.1/§16.3） | 已关闭 |
| R-02 | `ScadaUserStore` 加锁改造引入回归 | 桌面登录/账号管理异常 | 加锁范围过大（把 `Save` 与读一起罩住导致长时间持锁） | 两把锁已落地（`_gate` + `_writeGate`，锁内取记录、锁外算 PBKDF2）；`ResetPassword` CAS + `TryValidate` 复核已落地；UC1–UC18c 全绿（含摘锁变异反证：UC9 准确变红）。**仍缺** P0-5 的"8 并发登录时 UI 线程停顿 0"实测与持锁时长插桩 | **部分关闭（M0.2）**；实现 + 测试 |
| R-03 | 10Hz × 1000 图元把 UI 线程拖慢 | 桌面运行窗口掉帧、操作延迟 | 帧组装/序列化在 UI 线程超预算 | 命令泵已实测 ≈920 条/秒（§16.4），但**帧组装/序列化未测**；先补 P0-8，超预算就降 `FrameRateHz` 或外派序列化；**不许**把领域读值挪到后台 | **仍开放**（部分实测）；实现 |
| R-04 | 两套渲染实现（WPF 控件 vs web）漂移 | "桌面能看、网页不对" | 派生量公式/格式串/颜色换算各写一份 | W1-21/W1-24 闸门；公式集中到 `ScadaWebValueResolver` 并逐条对照；不支持项显式 Warn | 仍开放；实现 + reviewer |
| R-05 | 字体度量差异导致像素对照长期红 | 验收无法收敛 | 中文/等宽字体在不同渲染器下的字宽差 | 允许 ≤2px 偏移写进已知偏差；判据优先看"文字内容 + 包围盒"，不逐像素 | 仍开放；用户（验收口径） |
| R-06 | 图像带宽打满局域网 | 画面卡顿、其他系统受影响 | 多路 1080p 同时上屏 | 限 `ImageMaxWidth`、JPEG、按版本缓存、按会话限流；M3 再评估压缩 | 仍开放；用户（现场带宽） |
| R-07 | 运行中桌面改文档 → 快照频繁重建 | 浏览器反复全量重载 | 用户边跑边调属性 | 300ms 防抖 + 只在 `Version` 变化时重建；文档里明确"运行中改结构会触发网页重载" | 仍开放；实现 |
| R-08 | 无 TLS：局域网可嗅探密码 | 账号泄露 | 同网段抓包 | M1 明确"仅局域网"；M3 评估 TLS/反代；文档写进部署须知 | 仍开放；用户 |
| R-09 | `BitButton` 操作语义复刻 | 位按钮行为与桌面不一致 | web 侧自行实现"置位/复位/取反/按下ON/OFF" | 把决策函数抽到 `VM.Scada`（纯函数：`Mode` + 当前值 → 目标值），桌面控件与 web 共用；W2 断言逐模式对照 | **仍开放**；architect（M2 前定） |
| R-10 | 运动命令误开 | 设备意外动作 | 现场把 `AllowMotionCommands` 打开 | 默认 false；打开需同时改 `AllowedActions`；审计 + 二次确认；文档红字 | 仍开放；用户 |
| R-11 | 端口冲突（8080 被占） | web 起不来 | 现场已有服务 | 监听失败只记 Error、不阻断启动；`/api/status` 不可达时给排查清单；配置键是 `Port`/`BindAddress`（写 `Host` 不生效，§5.4） | 仍开放；实现 |
| R-12 | 会话令牌放 `sessionStorage` 仍可能被 XSS 读取 | 会话劫持 | 前端引入第三方脚本 | 不引第三方 JS；CSP `default-src 'self'`；令牌短命 + 空闲回收 | 仍开放；实现 |
| R-13 | `ScadaPage.Version` 在撤销后不回退（单调），但页面被删除/替换 | 快照指向已不存在的页 | 运行中删页 | `PageLoaded` 只对存在页触发；快照构建前校验 `ReferenceEquals(Session.CurrentPage, page)`；失败推 `stopped` | 仍开放；实现 |
| R-14 | 最高角色判定在"管理员离线"瞬间翻转 | 正在点的按钮突然变灰 | admin 登出 | 命令执行前再判一次；前端 toast 说明"权限已变化"；审计记录 | 仍开放；实现 |
| R-15 | `DataGrid`/`ChartTrend` 在无头环境的像素断言不可行（R77 先例） | 验收缺口 | M3 | 只钉数据与描述符；视觉交真机 + 截图证据；**不许**写永远为假的像素断言 | 仍开放；reviewer |
| R-16 | 多实例/多桌面端指向同一 `ScadaUsers.json` | 账号状态不一致 | 两台机器共享程序目录 | 文档写明"账号文件不共享"；M1 不做多实例协调 | 仍开放；用户 |
| R-17 | 握手 ticket 出现在 URL / 代理日志 | 短窗口内被重放 | 反向代理记录完整 URL | 30s TTL + 单次 + 可选绑定 RemoteIp；**不把密码放进 ticket**；M3 评估 TLS | 新增（M0）；实现 |
| R-18 | ~~测试矩阵 M11 与本文默认值不一致（队列 256 vs 16–64；`MaxSessions` 50 vs 16）~~ | — | 直接照矩阵 M11 实现 | **已裁定（M0.1 复评）**：默认 `MaxSessions=16`、`SessionInboundQueueCapacity=32`（16–64）、`FrameBudgetBytes=120KB`、`SnapshotMaxBytes=2MiB`，四项可配；矩阵 **v1.2 起已同步（现 v1.3）**，验收覆盖"默认值"与"调到 50/100"两档（M11/E02/E03/A14/C35/C56/C57/C58/C59/E28）；**256 已从所有用例移除** | 已关闭（M0.1 复评） |
| R-19 | Watson 鉴权粒度是"整条路由"，连接建立后不再回调 | 登出/降权后旧连接仍能发命令 | 会话角色变化 | 每条命令仍逐条鉴权（§8.2）；登出/降权/被踢时服务端主动 `DisconnectWebSocketSessionAsync` 或回 `E_UNAUTHENTICATED` 后关闭 | 新增（M0）；实现 |
| R-20 | 审计 `说明` 前缀与既有报表/筛选相互干扰 | 现场报表按"说明"列做文本搜索时误命中 | 报表把 `[web s=` 当前缀匹配 | 前缀固定在最前、由 `ScadaWebAuditBridge` 唯一拼装；C26 断言按前缀过滤的结果与原始行一致；**不改 CSV 列**是硬约束（§8.7） | 新增（M0.1）；实现 + 测试 |
| R-21 | 身份字段"不一致就拒绝"可能误伤旧前端 | 升级后旧页面命令全被 `E_IDENTITY` | 前端缓存了旧协议 | 协议 `v:1` 内前端只用 `hello.ok` 下发的 `sessionId`；覆盖前比对只在"客户端显式带了不同值"时触发；旧页面由 `index.html no-store` 强制刷新（F13） | 新增（M0.1）；实现 |
| R-22 | 未实现图元占位被现场当成"网页坏了" | 误报缺陷、信任度下降 | M1 只支持 10 图元 | 固定提示文案 + 每类型一次 Warn + 手册写明"网页暂不支持该图元"；像素 oracle 显式记录排除块数 | 新增（M0.1）；用户 + 文档 |
| R-23 | 把 10 图元视觉/派生量提前到 M1 会拉长 M1 工期 | M1 延期 | 字体度量差异导致像素对照长期红 | 判据分层：派生量逐值相等（先红）、几何 ≤1px、主色 ΔE ≤3、文字允许 ≤2px 偏移但内容逐字相等；确实无法消除的差异进 `已知偏差.md`；**不许**把这两条闸门推迟到 M3 | 新增（M0.1）；用户（验收口径） |
| R-24 | `imageToken` 校验漏了"元素/版本绑定" | 拿到一个合法 token 就能枚举/回放别人的图 | 只校验 token 存在与未过期 | 三件事一起判（会话归属 + `elementId` 在当前快照 + `version` 等于当前 `imageVersion`）；他人会话 403、过期版本 403；B37 是守门人 | 新增（M0.1 复评）；实现 + 测试 |
| R-25 | 现场把监管开关关掉后 `AllOperators` 照跑 | "多人可点"退化成无审计/无限速 | 只改 `ClickPolicy` 不看监管配置 | 启用闸门（§8.4）：缺项即拒绝启用并逐项报缺；`RequireAudit` 固定 true；C44/C65 守门，C66 反向确认"全开可启用" | 新增（M0.1 复评）；实现 + 测试 |
| R-26 | 跨页比较 `pageVersion` 造成误判 | 切到低版本页时被当成"版本回退"反复全量重载 | 客户端只存一个 `pageVersion` 数字 | 改 `PageKey`（`pageId@pageVersion@basePageVersion`）联合判定（§9.6）；切页只比 `pageId`；B38 专测跨页低版本 | 新增（M0.1 复评）；实现 + 测试 |
| R-27 | **把"M1b/M1c 断言通过"读成"M1 全绿"** | 交付带病进 M1.5/M2；评审五条 blocking 被静默跳过 | 只看到 `_WebProbe6` 31/31、`ScadaWebChecks` 33/33 与 `ScadaChecks` 2276/0 | §13.1 把 M1 拆成 M1b/M1c/M1 三行；§17.5 逐条列 blocking 与状态；台账 §3 明确"矩阵 248 条未执行"；**任何"M1 全绿"写法不成立** | 新增（M1b）；M1c 复核 |
| R-28 | **协议两份口径并存**（`kind` vs `type`） | 前端按 `kind` 写、后端按 `type` 发，联调时整段消息被忽略 | 文档旧版 `kind` 未清、M2 又引入 `kind` | 全文只认 `type`（§9.1）；M2 扩展也只用 `type`；X04 文档一致性闸门把"出现 `kind`"列为冲突 | 新增（M1b）；docs + 实现 |

### 14.1 M0 探针结论（M0.2 收口：地基 PASS、门禁四项转 M1 首周）

> **口径**：下面五条是"探针量到的事实"，不是"里程碑已关闭"。M0.2 复评结论是：**代码本体 PASS（`ScadaChecks` 2242/0）**；门禁项 `B40` / `A01–A09` / `F23` / `F24` 需要监听器与 web 模块，在 M0 骨架上**无法执行**，已显式移到 **M1 首周**。M0 记为"地基 + 2242/0 收口"，**不是"全绿"**。

**探针已实测（可直接用于实现，但门禁未过）**：

1. `auth:true` 的实际行为——每次握手恰好触发 1 次 `AuthenticateApiRequest`；`auth:false` 完全不触发；`AuthenticateApiRequest=null` 时 fail-open（所以启动必须断言非空）。
2. 模块依赖解析与类型身份——`Web\` 放私有依赖可解析、与宿主共用同一 `VM.Core`、不需要 `AssemblyDependencyResolver`。
3. 命令泵超时/背压语义——`WaitAsync` 截断等待、审计在回调内、禁用 `DropWrite`、队列 16–64（默认 32）。
4. 静态资源供给方式——`RoutingGroup.Static.Add`，不用 `WebserverBase.Get`（base64 陷阱）。
5. Watson 三个静默失效点——`WebSockets.Enable=true`、WS 路由 `auth:true`、`AuthenticateApiRequest` 非空。

**仍须在 M1 前关闭**：

1. 序列化放 UI 线程还是 worker（P0-8，未测）。
2. `FrameRateHz` 默认值（P0-8 实测后定）。
3. `ScadaUserStore` 加锁的最小范围——**已落地**（两把锁 + `ResetPassword` CAS + `TryValidate` 复核，UC1–UC18c 全绿）；仍缺 P0-5 的"8 并发登录时 UI 线程停顿 0"与持锁时长插桩。
4. Playwright 用 `msedge` 还是 `chrome`（P0-7）。
5. 静态资源防穿越（P0-6）与 WS 回环延迟（P0-2）。
6. 帧大小（P0-8）与 10Hz × 1000 图元的 UI 停顿（P0-4 后半）。
7. 矩阵 A05（双份契约反证）、A06–A09（协议版本/初始化隔离/重复加载/端口占用）、A12（会话凭证）、A13（Origin）、A19（单命令异常隔离）、A21（停止排空）、A22–A24/A26（启动顺序/`/api/status`/中文/热改）——M0 未跑（§16.7）。
8. §16.8 的六项 Watson 补测（`authCalls==1` 断言化、401 且 handler 未执行、`Static.Add` 参数化路径、`MaxMessageSize=64KB`、`SendTextAsync` 顺序、`DisconnectWebSocketSessionAsync`）。
9. 四条 blocking（§17）——M0.2 的结论是"**代码本体 PASS、运行时门禁四项（`B40` / `A01–A09` / `F23` / `F24`）转 M1 首周**"；这四项没跑完之前，M0 不许写"全绿"。

### 14.2 必须在 M2 之前关闭的问题

1. `BitButton` 决策函数归属（R-09）。
2. `IScadaAlarmCommands` 的形状（单条/全部确认、返回什么）。
3. ~~运动命令的二次确认 token 语义~~ **已裁定（M0.1）**：`confirmToken` 绑定 `(sessionId, elementId, actionKind)`、30s、单次、先删后执行（§8.4.1）。
4. ~~`AllOperators` 下"操作租约"默认开还是关~~ **已裁定（M0.1 复评）**：`OperationLeaseEnabled` **默认 `false`**（退化为"都可点 + 串行 + 审计 + 限速 + 二次确认"）；现场要单人接管再打开（§5.4/§8.4）。
5. `ScadaWebOptions.DangerousActions` 的最终清单（除 `StopFlow` 与运动类动作，是否还包含"清警"）——实现前与用户确认一次。
6. ~~`presence` 是否显示"桌面端当前登录者"~~ **已裁定（M0.1 复评）**：**不显示**（两套身份互不影响，§8.1）；且 presence 的每行**只有不透明 `sessionRef`，没有 `sessionId`**（§9.3/C50）。

---

## 15. 改动清单（新增 / 修改 / 不改）

> **口径**：本节按"**已落地**"与"**计划（M1/M2/M3）**"两栏写，不再把计划项混成"已新增"。已落地项都附代码锚点，改代码时同步改本节（M0.1 复评抓到的正是"§15 写着 `ScadaAuditWriter 不改`，实际它已经实现了 `IScadaAuditSink`"）。

### 15.1 新增

| 文件 | 状态 | 内容 |
| --- | --- | --- |
| `Scada\Models\IScadaRunHub.cs` | **已落地** | `IsRunning` / `Session` / `Document` / `RunGeneration` / `TryGetCurrent(out session, out document, out generation)` / `RunStarted(session,document,generation)` / `RunStopped(generation)`（`:20-63`，`TryGetCurrent` 在 `:50`）；实现见 `ScadaRuntimeHost.cs:285` |
| `Scada\Models\ScadaRunHubState.cs` | **已落地** | 启停门禁纯状态机：`TryStarted` / `TryStopped` / `Reset`；generation 单调、重复启动不递增、未启动 Stop 不发（`:42-76`） |
| `Scada\Models\IScadaAuditSink.cs` | **已落地** | 审计落盘出口的 8 参数字符串重载（`actor/sessionId/clientAddress/subject/eventText/action/result/detail`），跨程序集解耦；**writer 侧已加 `_writeGate`（任意线程安全）**，但 web 仍必须把审计投进命令泵 UI 队列，以保证"审计顺序 = 执行顺序"（§8.7/F23） |
| `Scada\Models\IScadaUserAuthenticator.cs` | **已落地** | `TryValidate` + `IsUsingDefaultPassword`；供 web 在后台线程校验账号 |
| `Scada\Models\IScadaRenderCatalog.cs` | **已落地（契约 + 实现）** | `All`（`ScadaElementDescriptorInfo`：`TypeKey/DisplayName/Category/DefaultWidth/DefaultHeight/ShowInToolbox/PropertyKeys/EventNames`）；宿主实现 `VisionMaster\Services\ScadaRenderCatalog.cs`，`App.xaml.cs:326` 注册 |
| `VisionMaster\Services\ScadaWebModuleLoader.cs` | **已落地** | `TryLoad(baseDirectory, out diagnostic)`：直读 `AppConfig.json` 的 `ScadaWeb` 一节 → 拒契约副本（`VM.Core.dll`/`VM.Scada.dll`/`Core.Interfaces.dll`/`Newtonsoft.Json.dll` + **`Prism*.dll` 前缀**）→ `Assembly.LoadFrom` → 找 `IAppModule` → 实例化；失败只回诊断、不抛（`:83-200`；`Prism` 前缀规则 `:74-78`、`:240`） |
| `Web\VisionMaster.ScadaWeb\ScadaWebModule.cs` | **已落地（M1b）** | `IAppModule`：`Register` 只登记自己；`Initialize` 解析 5 个出口 → 构造 `ScadaWebHost` → `Start()` → 注册退出任务；**两个入口都幂等**（`_registered` / `_initialized`），`InitializeCount` 只给断言用；`Enabled=false` 不监听 |
| `Web\VisionMaster.ScadaWeb\ScadaWebHost.cs` | **已落地（M1b）** | Watson 监听 + 3 个 HTTP 接口 + `/img` 404 + 5 个静态文件 + `WS /ws`（`auth:true`）；`hello`/`snapshot`/`page`/`frame`/`stopped`/`idle`/`pong`/`error`/`bye`；10Hz 宿主级值帧循环；`TryGetCurrent` 拍快照；启动断言三连 |
| `Web\VisionMaster.ScadaWeb\ScadaWebSession.cs` | **已落地（M1b）** | 每连接：`SessionId`（32 hex，服务端）/ `SessionRef`（16 hex，对外）/ `CurrentPageKey` / 有界发送队列 256（满则丢帧） |
| `Web\VisionMaster.ScadaWeb\ScadaWebTicketService.cs` | **已落地（M1b 票 + M1c 令牌）** | 一次性 ticket（30s/单次，`_live` → `_handoff` 两表）；`sessionToken`（8h）在核销时签发（`IssueSessionLocked`）；换票路径是 `TryResolveSession`（可重复校验、取用不删）+ `BindSession`（挂到新票，**不轮换令牌**）；`_m1c_webchecks.log` M1c-02/M1c-04 |
| `Web\VisionMaster.ScadaWeb\ScadaWebValueResolver.cs` | **已落地（M1b 值帧 + M1c 格式化）** | 绑定 → `frame.items` 四类值（`text/number/bool/color`）；同帧按变量缓存；`DisplayFormat` 已应用（`IFormattable` + InvariantCulture） |
| `Web\VisionMaster.ScadaWeb\wwwroot\` | **已落地（M1b + M1c）** | `index.html` / `app.js` / `styles.css` / `elements-process.js` / `elements-process.css`；10 图元渲染器、占位盒、登录卡片（M1c 已落地）、`sessionStorage` 存凭据、断线换票重连、`MIN_SCALE=0.8` 滚动、`#demo-chip` 演示标记 |
| `Scada\Models\ScadaPageSnapshot.cs` / `ScadaPageSnapshotBuilder.cs` | **已落地（M1a/M1b）** | 不可变页面快照（`PageId/PageName/PageVersion/Width/Height/Background/BasePage/Elements`）；基座只递归一层；`LayerVisible` 逐图元 |
| `_WebProbe6\` | **已落地（M1b 探针，不打包；M1c 扩到 31 条）** | 19 条基线：静态 5 文件、ticket、WS hello、snapshot 10 元素、frame 四类值、`page`、`pong`、`E_UNSUPPORTED`、`stopped`、新运行 snapshot、停服；M1c 加 `sessionToken` / 换票 / 重连 / 基座版本 / 静态 `?v=` 等（31 条，Agent S 报告） |
| `Web\VisionMaster.ScadaWeb\VisionMaster.ScadaWeb.csproj` | **已落地** | `net9.0-windows` + `UseWPF`，项目引用 `VM.Core`/`VM.Scada`，二进制引用 Watson；**不进 `Modules\`** |
| `ScadaChecks\RunHubChecks.cs` | **已落地** | `[RH]` RH1–RH18：状态机门禁、真实宿主"未启动 Stop"兜底、`TryGetCurrent` 三态（RH10/RH11/RH13）、`RunStopped` 清空之后发（RH12）、订阅方抛异常不冒泡（RH14/RH15）、`AbortStart` 两条失败路径（RH16/RH17）、`IScadaAuditSink` 映射（7 列 + 前缀）、结果词映射、审计写锁并发（RH18） |
| `ScadaChecks\ModuleLoaderChecks.cs` | **已落地（M0.2）** | `[ML]` ML1–ML6：无配置 / `Enabled=false` 零 `LoadFrom` / 缺 DLL / 契约副本拒绝（含 `Prism*`）/ 真模块加载 / `Initialize` 幂等 |
| `ScadaChecks\UserStoreConcurrencyChecks.cs` | **已落地** | `[UC]` UC1–UC18c：8 线程 × 200 次校验 + 增删/刷新/枚举线程、并发改密恰好一个成功、磁盘与内存一致；**M0.2 追加** UC16–UC18c（改密/重置 CAS、重置与删号并发、PBKDF2 期间降权/删号复核） |
| `Scada\Models\IScadaAlarmCommands.cs` | **已落地（契约 + 宿主实现）** | `TryAcknowledge(Guid, out string?)` / `AcknowledgeAll(out string?)`；`ScadaRuntimeHost` 实现（`ScadaRuntimeHost.cs:79`），`App.xaml.cs:327` 注册；**不写审计**（由 web 命令层写） |
| `Scada\Models\IScadaValueWriter.cs` | **已落地（契约 + 宿主实现）** | `TryWriteText(element, targetProperty, text, out error)`；实现 `VisionMaster\Services\ScadaValueWriter.cs`，`App.xaml.cs:344` 注册；`cmd.input` 直接用它（R34 的唯一文本解析口径） |
| `Scada\Models\IScadaAlarmFeed.cs` | 计划（M3） | `ActiveAlarms` / `UnacknowledgedCount` + 三事件，供 `Hmi.AlarmBanner` |
| `VisionMaster\Services\ScadaRenderCatalog.cs` | **已落地（M1b）** | 把 `ElementRegistry.All` 投影成 `IScadaRenderCatalog`（晚于 `ScadaChartElements.Register()`）；`App.xaml.cs:326` 注册 |
| `Web\Directory.Build.targets` | 计划（M1c） | 仅本目录：构建后把 web 产物拷到宿主输出 `Web\`（不碰 `Plugins\`/`Modules\`）；M1b 用 csproj 内两条 Copy Target 代替 |
| `Web\VisionMaster.ScadaWeb\**`（服务端/会话/泵/快照/值帧/图像/安全/协议/前端资源） | **部分落地（M1b + M2a）** | 服务端/会话/票/值帧/前端已落地；**命令泵/路由/权限/安全门/动作过滤已写入并接线（M2a）**；图像（M1.5）与租约（M2 后续）仍未做 |
| `Web\VisionMaster.ScadaWeb\ScadaWebActionFilter.cs` | **已落地（M2a，09:07）** | 动作副本过滤：`BuildAllowedCopy(source, allowed, out skipped)` —— 复制 `hook.Actions`、无条件剔除 `Navigate`、不在白名单的剔除、副本 `RequireConfirmation=false`、**原 hook 只读不写**（§7.3/§8.4.1）；断言见 `Web\ScadaWebChecks\M2ActionFilterChecks.cs` |
| `Web\VisionMaster.ScadaWeb\ScadaWebCommand.cs` | **已落地（M2a，09:09）** | 四条命令常量（`ScadaWebCommandKinds`）、回执码常量（`ScadaWebErrorCodes`）、不可变命令对象（持有 `Session` 引用；`RequestId` 用于回执配对与幂等键，去重表在 `ScadaWebCommandRouter`，M2a-4 已关闭） |
| `Web\VisionMaster.ScadaWeb\ScadaWebSecurityGate.cs` | **已落地（M2a，09:19）** | 默认密码 gate（`E_DEFAULT_PASSWORD`）、Origin 同源（缺头放行/`null` 拒绝）、取票每 IP 限速（按 `ScadaWebSettings.TicketRatePerMinute`，默认 10/分钟，生效额度夹到 1~600）、`RevokeSession`/`RevokeUser` 委托；断言见 `M2SecurityChecks.cs` |
| `Web\VisionMaster.ScadaWeb\ScadaWebAccessPolicy.cs` | **已落地（M2a，09:16）** | 权限层四道（`AllowCommands` → 会话只读/未登录 → 角色 → `ClickPolicy`）；`HighestRoleOnly` 平局都可点、无人登录全员只读；caps 与命令共用同一判据 |
| `Web\VisionMaster.ScadaWeb\ScadaWebCommandPump.cs` | **已落地（M2a，09:16）** | 全局 `Channel` 1024 + 单消费者（线程池、无 SyncContext）+ 每会话待处理计数（默认 32）+ 逐条 `InvokeAsync`；单条异常 → `E_INTERNAL` 且泵继续；`E_BUSY` 由 `Enqueue` 返回 false |
| `Web\VisionMaster.ScadaWeb\ScadaWebCommandRouter.cs` | **已落地（M2a，09:19）** | 四条命令的执行体：三闸、图元解析、权限层、动作过滤、命令级审计、`confirmToken` 存储（默认 30s、单次、三元组绑定）；**三闸必填（缺一 `E_BAD_REQUEST`）+ `(SessionId, RequestId)` 幂等去重（M2a-3 / M2a-4 已关闭）** |
| `Web\VisionMaster.ScadaWeb\ScadaWebPresence.cs` / `ScadaWebRateLimiter.cs` | **已落地（M2a，09:10）** | 在线列表（每行只有 `SessionRef`，无 `SessionId`）与双层限速（会话 5/s、元素 2/s 跨会话共享） |
| `Web\ScadaWebChecks\M2SecurityChecks.cs` / `M2ActionFilterChecks.cs` / `M2CommandChecks.cs` | **已写入（M2a，09:06–09:15），未运行** | 安全前置、动作过滤、命令路由的断言；**必须先 build 再 run，判据看条数 + 明细**（R65） |
| `Web\VisionMaster.ScadaWeb\Security\ScadaWebImageTokens.cs` | 计划（M1.5） | `imageToken` 签发/轮换/校验（按会话、30–60s、与元素+版本绑定，§6.1） |
| `Web\VisionMaster.ScadaWeb\wwwroot\`（占位渲染） | **已落地（M1b）** | 未实现 TypeKey 的中性占位 + 固定提示文案 + 每类型一次日志（§10.2.1；实现在 `app.js`/`elements-process.js`，没有独立 `Assets\` 目录） |
| `Web\ScadaWebChecks\**` | **已落地（M1c，已入 sln）** | 协议层断言宿主：33 条（B1–B19 基线 + M1c-01~M1c-09 + B20/B21/B23/B24/B36）；`_m1c_webchecks33b.log` 33 PASS / 0 FAIL |
| `Web\ScadaWebE2E\**` | 计划（M1c 未建） | Playwright 端到端 + 像素 oracle；M1c 的浏览器验证是临时脚本（未入仓、未留日志） |
| `docs\SCADA运行态Web客户端\已知偏差.md` | **已建立（M1b 9 条 + M1c 2 条 = 11 条）** | 视觉对照允许偏差清单（Indicator/Lamp 内切圆、Tank 底带、省略号、字体栈、`non-scaling-stroke`、1280 最小缩放、Tank 圆角裁剪、ProgressBar 百分比、SVG 抗锯齿、登录卡片与演示标记、演示页棒图配色） |
| `docs\SCADA运行态Web客户端\结果台账.md` | **已建立（M0.1）/ 已追加（M0.2、M1b、M1c、M2a 记账）** | 轮次结果台账（列见矩阵 §13.2）；M0 三探针、M0.1（2198/0）、M0.2（2242/0）、M1b（`_WebProbe6` 19/19、`ScadaChecks` 2266/0）、M1c（33/33、31/31、2276/0）、M2a（源码写入、`阻塞` 占位行）均已入账；矩阵 261 条仍未执行 |

### 15.2 修改

| 文件 | 状态 | 改动 | 风险 |
| --- | --- | --- | --- |
| `VisionMaster\App.xaml.cs` | **已落地** | `:230` 调 loader（`out diagnostic`）+ `_pendingModuleDiagnostics`；模块 `Register` 逐项隔离（`:238-250`）；`:319-321` 三条映射（同一 `ScadaRuntimeHost` 单例）；`:309` `IScadaAuditSink`；`:403` `IScadaUserAuthenticator` | 低；loader 失败不影响启动 |
| `VisionMaster\Services\ScadaRuntimeHost.cs` | **已落地** | 实现 `IScadaRunHub`（`Session`/`Document`/`RunGeneration`/`TryGetCurrent`/两事件）；`_hubState` 门禁；`Start` 在 `Show()` 成功后发布 `RunStarted`（`:375-391`）；`OnWindowClosed` / `Stop` 在清空 `_session`/`_window` 后发布 `RunStopped`（`:1006-1019`、`:404-416`）；**失败统一走 `AbortStart`（`:525`，RH16/RH17 钉住"不早退到未显示的窗口"）**；`TryGetCurrent`（`:285`）；订阅方逐个 try/catch | 中；不得改变既有运行语义（RH1–RH18 守门） |
| `VisionMaster\Services\ScadaAuditWriter.cs` | **已落地** | 实现 `IScadaAuditSink`；新增 8 参数 `Append`（`:375` 起）——**`[web s=… ip=…]` 前缀的唯一拼装点**；`MapWebOutcome` 把"成功/拒绝/认不出的词"映射成三档（RH8/RH9）；**`_writeGate`（`:248`）罩住"序列化 + 写盘"，任意线程安全、行不撕裂**（RH18：6 线程 × 40 条 = 240 行、每行 7 列）；**CSV 仍是 7 列、表头不动** | 低；既有报表/断言零影响 |
| `VisionMaster\Services\ScadaUserStore.cs` | **已落地** | 两把锁：`_gate`（内存清单）+ `_writeGate`（改内存→序列化→写盘）；锁序固定 `_writeGate → _gate`；锁外算 PBKDF2；实现 `IScadaUserAuthenticator`（`:60-75`、`:155`、`:165`、`:258-282`）；**`ResetPassword` 走 CAS（`:496-543`，旧密码已变 → "密码已被他人修改"）；`TryValidate` 在 PBKDF2 之后二次进锁复核角色/记录/密码（`:278-341`）** | 中；R-02，UC1–UC18c 守门 |
| `Scada\Models\ScadaPageSnapshot.cs` / `ScadaPageSnapshotBuilder.cs` | **已落地（M1a/M1b）** | 新增不可变快照 DTO 与构建器（`PageId/PageName/PageVersion/Width/Height/Background/BasePage/Elements`；基座一层；元素全收 + `LayerVisible`）；web 直接消费，不再维护第二份默认值表 | 中；模型字段变化要同步快照 |
| `Core\Models\AppConfigModel.cs` | **已落地** | 新增 `ScadaWebClickPolicy` 枚举与 `ScadaWebSettings` 节（`:256-395`），`AppConfigModel.ScadaWeb`（`:586`）。**实际字段以 §5.4 "字段落点"为准**（监管/图片令牌等是 M1 目标字段） | 低；旧配置缺节 → 默认值 |
| `ScadaChecks\ScadaChecks.csproj` | **已落地** | **去掉对 `VM.Scada.Controls` 的直接引用**，只引用 `VisionMaster`（传递引用）。理由：两条引用链各构建一次会让 WPF 的 `_wpftmp` 与主工程抢同一个 `obj`，报 **RG1000「BAML 键重复」** | 低；断言工程仍能拿到控件库类型 |
| `VisionMaster.sln` | **已落地** | 加入 `VisionMaster.ScadaWeb`（`:122`，R62）与 `ScadaWebChecks`（`:126`，M1c；R62 的"不在 sln 里就永远不被重建"同样适用） | 低 |
| `pack.ps1` | **已落地** | 新增 `3b` 步：把 `VisionMaster\bin\<Configuration>\net9.0-windows\Web` 整目录拷到 `$OutputDir\Web`（`:152-162`）；**`3c` 步校验程序集归属**（`:192-246`）：`Web\` 不许有宿主/契约副本（含 `Prism*.dll`）、`Modules\` 不许有 `VisionMaster.ScadaWeb.dll`，命中即抛错（退出码 1） | 低 |
| `Scada\Models\ScadaRuntime.cs`（M2a as-built） | **不改** | 原计划新增 `MatchElementEvent`；**as-built 改为在 web 路由层实现 `CollectHooks`**（`ScadaWebCommandRouter.cs:631-644`，与 `ScadaHookMatcher` 同一口径：`hook.Event` 匹配且 `Actions.Count > 0`）。理由：`ScadaRuntime` 是桌面热路径，为 web 加一个只被 web 用的方法会把"桌面行为不变"的断言面扩大；路由层自己判钩子，执行仍交给 `IScadaActionDispatcher` | 低；`ScadaChecks` 全绿即可 |
| `docs\软件手册.md` | 计划 | 增加"远程访问（SCADA 运行态 Web）"一节 | 低 |

### 15.3 明确不改

- `IScadaRuntimeHost`（不加成员/事件；`IScadaRunHub` 是另一条出口）。
- `ScadaActionDispatcher` 的动作语义、顺序与"失败不中断"契约（web 只在外层做**动作副本过滤**：白名单 + 剔除 `Navigate` + `RequireConfirmation=false`，§7.3/§8.4.1）。
- `ScadaRuntimeBinder`（web 不碰桌面数据泵）。
- `Modules\` / `Plugins\` 的任何构建约定。
- **审计 CSV 的 7 列与 `ScadaAuditEntry` 形状**（`ScadaAuditWriter` 文件本身**已改**——新增 `IScadaAuditSink` 实现与 8 参数重载；**改的是出口，不是列**）。

---

## 16. M0 实测台账（2026-10-21）

> 三个隔离探针，均在仓库根目录、**未修改生产代码/工程/其它文档**。证据以各自 README 与 `out\` 下的日志/CSV 为准；本文只引用结论与关键数字。

### 16.1 探针清单

| 探针 | 覆盖问题 | 结果 | 证据 |
| --- | --- | --- | --- |
| `_WebProbe1` | 可选模块加载：`Enabled` 两态、类型身份、DLL 缺失、私有依赖、AspNetCore 反例 | **6/6 PASS**（A–F），附加观察 G/H | `_WebProbe1\README.md`；`Host\Program.cs`；`out\` |
| `_WebProbe2` | Watson 7.2.0：WS 鉴权、`auth:false` 反证、静态字节、并发发送、默认配置 | **9 PASS / 0 FAIL** | `_WebProbe2\README.md`；`run_final.txt`；`run_default_ws.txt`；`dump.txt` |
| `_WebProbe3` | UI 线程命令泵：1000 条无交错、背压、`DropWrite`、超时、UI 占用、4 会话拓扑 | **18 PASS / 0 FAIL**；变异负对照 **15 PASS / 3 FAIL** | `_WebProbe3\README.md`；`out\run.log`；`out\run-mutation.log`；`out\round1-commands.csv`；`out\round5-audit.csv` |

环境：Windows 10.0.26200 x64、.NET SDK 9.0.318、运行时 9.0.20；`_WebProbe1` 用 `net9.0-windows + UseWPF`，`_WebProbe2` 用 `net9.0` 控制台，`_WebProbe3` 用 `net9.0-windows + UseWPF`。

### 16.2 模块加载（`_WebProbe1`）

| 判据 | 结果 | 关键证据 |
| --- | --- | --- |
| A `Enabled=false` → 不加载 | PASS | `AppDomain` 无 `Probe.ScadaWeb`；`LoadFrom called=False`；`VM.Scada loaded: False` |
| B `Enabled=true` → 加载成功 | PASS | `module type: Probe.ScadaWeb.ScadaWebModule`；`module.Name` 可打印 |
| C 类型身份唯一 | PASS | 模块与宿主 `IAppModule` 的 `Assembly` **同一对象**（`same Assembly object=True`）；`VM.Core` 从宿主目录加载 |
| D DLL 缺失 | PASS | `[WARN] ... dll is missing, skip loading; host continues`；`EXITCODE=0` |
| E `Web\` 无 `VM.*.dll` 仍能加载 | PASS | `VM.*.dll inside WebDir: <none>`；`VM.Core`/`VM.Scada` 均从 baseDir 加载 |
| F `Enabled=false` 且 DLL 存在也不加载 | PASS | `ModuleDll exists` + `Probe.ScadaWeb loaded AFTER: False` |
| G 私有依赖放 `Web\` | 可解析 | `Probe.ExtraDep@1.0.0 @ out\Web\Probe.ExtraDep.dll`；**不需要** `AssemblyDependencyResolver` |
| H Kestrel/AspNetCore 反例 | 宿主缺 FrameworkReference 时失败 | `FileNotFoundException: Microsoft.AspNetCore, Version=9.0.0.0`；给宿主加 `<FrameworkReference Include="Microsoft.AspNetCore.App" />` 后成功 |

**设计落点**：§5.2（依赖放哪、不复制宿主程序集、Kestrel 前置条件）、§5.3（打包禁止清单）。

### 16.3 Watson 7.2.0（`_WebProbe2`）

| 判据 | 结果 | 关键证据 |
| --- | --- | --- |
| A `auth:true` 触发鉴权 | PASS | 每次握手恰好 1 次 `AuthenticateApiRequest`（带/不带 token 都是 +1），传入升级前 `GET /ws` |
| B `auth:false` 不触发 | PASS（漏洞确认） | 两次连接 `authCalls +0`，直接连上收 `hello` |
| C 鉴权成功 | PASS | `connected, recv="hello"`；handler 读到 `Authorization: Bearer test` |
| D 鉴权失败 | PASS | 客户端 `WebSocketException`（401 vs 101）；服务端 `401` + `{"Error":"NotAuthorized","StatusCode":401,...}`；业务 handler 未执行 |
| E 静态字节原样返回 | PASS | `Static.Add`：`200 / 1024B / contentType=application/octet-stream / SequenceEqual=True` |
| F `SendTextAsync` 并发 | PASS | 4 OS 线程 × 250 = 1000 条全收到、0 异常（库内 `SemaphoreSlim _SendLock`） |
| 默认 `WebSockets.Enable=false` | 握手 400 | `run_default_ws.txt`：`status code '400'` |
| `auth:true` + `AuthenticateApiRequest=null` | **fail-open** | 未配置鉴权器时照常连上收 `hello` |
| `WebserverBase.Get` 返回 `byte[]` | base64 JSON | 1024B → 1370B，`rawMatch=False` |

> **口径**：上表的 PASS 只表示"探针量到了这个现象"，**不表示对应矩阵用例已通过**。尤其 `auth:true` 那一行：探针观察到的是"客户端拿到 401"，"业务 handler 一次都没执行""`authCalls` 恰好 1 次"这两条断言尚未写（§16.8），所以 **A10 不能引用本表宣称 PASS**。

**三个静默失效点与生产断言**：`WebSockets.Enable=true`、WS 路由 `auth:true`、`AuthenticateApiRequest != null`——三者任一不满足即拒绝启动（§6.1）。

**设计落点**：§6.1（路由表、`Static.Add`、ticket 握手、三断言）、§8.9/§8.10（ticket vs 会话令牌）、§11.1（单接收循环、发送可并发）。

### 16.4 UI 线程命令泵（`_WebProbe3`）

| 判据 | 结果 | 关键数字 |
| --- | --- | --- |
| A 1000 条全部执行、Id 唯一 | PASS | records=1000，parsedIds=1000，unique=1000（0..999） |
| B 全部在 UI 线程 | PASS | `CheckAccess=True` 1000/1000；执行线程集合 = `{5}`；`Debug.Assert` 失败 0 |
| C CSV 严格解析无交错 | PASS | 列数≠5 的行 0；含逗号/引号负载回读不符 0 |
| D 无死锁、总耗时 <10s | PASS | **1086.29ms** ≈ **920 条/秒** |
| E 队列满显式拒绝 | PASS | 容量 8：accepted=8 / rejected=992 / executed=8；accepted+rejected=1000；异常 0 |
| F 超时语义 | PASS | `InvokeAsync(ct=2s)` + 3s 工作 → **3004.44ms** 才返回（取消对已执行回调无效）；`WaitAsync(2s)` → 2005.82ms 抛超时，但工作仍在 **3013.31ms** 跑完 |
| F1b Pending 取消 | PASS | `Pending → Aborted`，回调一次都没跑 |
| F3/F4 `Abort()` | PASS | Pending 返回 True 且不执行；Executing 返回 False 且照常跑完 |
| F5 同步 `Dispatcher.Invoke` 反证 | PASS（死锁） | 工作线程同步 Invoke + UI 线程阻塞等它 → **1.5s 不返回** |
| D2 队列深度=延迟 | 观察 | 容量 256 + 1ms/条：`TryWrite` 失败重试 **5,494,038** 次；256 深度 ≈ **275ms** 缓冲 |
| E2 `DropWrite` 不可信 | 观察 | 容量 4 写 100 条：`TryWrite` 返回 true **100** 次，实际可读出 **4** 条（静默丢 96） |
| H1 UI 被慢命令占住 | 观察 | 800ms 命令（实测 830.07ms）→ 首条排队 **735.10ms**，后续 0.67ms/条 |
| I1 4 会话 × 250 → 单泵 | PASS | executed=1000，1017.32ms；审计写线程 **1**（tid=5）；全局 Seq **1..1000**；每会话 FIFO 保持 |
| 变异负对照 | 15 PASS / 3 FAIL | 不走 dispatcher + 生产者直写 CSV → A/B/C 精确变红（A records=1035/unique=995；B CheckAccess 0/1035；C 列数≠5 共 39 行） |

**设计落点**：§4.2（队列 32/16–64）、§4.3（`WaitAsync`、审计在回调内、禁用 `DropWrite`、泵跑在无 `SynchronizationContext` 的线程）、§11.2（≈920 条/秒）、§11.3（排空预算、`Abort` 不可依赖）。

### 16.5 对设计的修订（M0 触发）

| 原表述 | 实测后改为 |
| --- | --- |
| 静态资源用 `Get` 路由 + `GetManifestResourceStream` | **`RoutingGroup.Static.Add`**；`WebserverBase.Get` 返回 `byte[]` 会变 base64 JSON |
| 原"`auth:true` 是否可达"待实测项 | **可达**；但 `AuthenticateApiRequest=null` 时 fail-open → 启动断言 + 一次性 ticket |
| "不配置 Watson 认证回调，登录走 WS 首帧" | **必须配置 `AuthenticateApiRequest`**；WS 握手带一次性 ticket，`auth.login` 在连接内升级身份 |
| 每会话队列 64 / 矩阵建议 256 | **默认 32，范围 16–64**；256 深度实测 p50 275ms |
| 超时用 `InvokeAsync(ct)` | **`WaitAsync` 截断等待**；审计必须在回调内；`Abort` 不能中止执行中的命令 |
| `DropWrite` 只影响读命令 | **全面禁用**：`TryWrite` 返回 true 仍会静默丢 |
| 模块私有依赖"若主程序不再引用 Watson 则自带" | `Web\` 放私有依赖可解析；**宿主已有程序集绝不复制**；Kestrel 需宿主 FrameworkReference |

### 16.6 测试矩阵对照（`docs\SCADA运行态Web客户端测试矩阵.md` v1.4）

矩阵规模：**261 条**（A30 / B41 / C80 / D32 / E29 / F24 / G16 / X5 / O4；P0 191 / P1 65 / P2 5）。

| 状态 | 用例 | 说明 |
| --- | --- | --- |
| **探针量到现象**（不等于用例通过） | A04、A14、A16、A17、A20 | A04 = `_WebProbe1` C/E（类型身份、`Web\` 无 `VM.*.dll`）；A14/A16/A17/A20 = `_WebProbe3` A–F/I1（1000 条无交错、`DropWrite` 反证、超时语义、会话 FIFO/全局串行）。**这些是"现象已观察到"，用例仍要按矩阵的步骤与证据重跑一遍才算通过** |
| 部分覆盖 | A01（不加载 PASS；端口/`/` 未测）、A02（模块加载 PASS；`/api/status`/`Initialize` 次数未测）、A03（Warn+继续 PASS；端口/状态栏未测）、A10（401/101 现象有；"handler 一次都没执行"与 `authCalls==1` 的断言化未做）、A11（`auth:false` 反证 PASS；`AuthenticateApiRequest=null` fail-open 现象有；生产静态扫描待 M1）、A15（accepted/rejected PASS；客户端 `E_BUSY` 回执未测）、A18（首条排队 735ms <3s PASS；心跳间隔未测）、A25（4 会话拓扑 PASS；ack 路由隔离未测） | 需 M1 `ScadaWebChecks`/E2E 补齐 |
| 未覆盖 | A05–A09、A12、A13、A19、A21–A30 | 见 §16.7 / §16.8 |

**与矩阵的对齐结果（M0.1 复评后同步，两份文档不再有差异）**：

1. **登录传输**：WS `auth.login` + `POST /api/ticket` 一次性 ticket + `WS /ws?ticket=…`，**无 cookie**（F11/F23/F24）。删除 Cookie/HttpOnly/SameSite/Secure 全部判据。
2. **图片鉴权**：`/img` 用**独立 `imageToken`**（按会话、30–60s、可轮换、只放 query）；URL 里不出现会话令牌；未登录/他人会话/过期版本 → 401/403（F11/B37/C42）。
3. **版本端点**：`GET /api/status`（含 `gate`/`loadState`/在线人数/页面尺寸/版本字段）（A23/G11）；`/version` 404。
4. **容量默认值**：**`MaxSessions=16`、`SessionInboundQueueCapacity=32`（16–64）、`FrameBudgetBytes=120KB`、`SnapshotMaxBytes=2MiB`**；验收覆盖默认值与"显式调到 50/100"两档（M11/E02/E03/A14/C35/C56/C57/C58/C59/E28）。**256 已从所有用例移除**（不是合法档位，E26 只测 16/32/64）。
5. **吞吐门槛**：50 客户端 p95 ≤ 300ms；100 客户端 p95 ≤ 500ms（E03/M8/C59/E28 统一）；10 客户端/200 绑定仍按 p95 < 150ms（M3/B20/E01）。
6. **审计**：**7 列 + `说明` 列 `[web s=… ip=…]` 前缀**；前缀**只在 `ScadaAuditWriter.Append(string,…)` 拼一次**，bridge 传裸 detail（C67）；命令级 + 动作级两层计数（C01/C03/C14/C19/C20/C23/C26/C41/C43/C49）。
7. **`PageKey` 联合判定**：`pageId@pageVersion@basePageVersion`；切页只比 `pageId`；同页才比 `pageVersion`；回退按跳变处理；值帧不带 `baseVersion`（B13/B14/B17–B19/B28/B38/B39）。
8. **平局**：`HighestRoleOnly` 同角色**都可点**（除非开租约），无"最早连接优先/序数最小"（C24/C48）。
9. **监管闸门**：`AllOperators` 必须同时满足 `RequireOnlineUsers`/`RequireAudit`（固定 true）/`RequireRateLimit`/`RequireConfirmForDangerous`/`ShowOnlineUsers`；缺项拒绝启用并指出缺哪一项（C44/C65/C66）。
10. **身份与在线列表**：presence 只给不透明 `sessionRef`（无 `sessionId`）；命令身份由服务端从连接反查（C50/F04/F24）。
11. **M1 视觉闸门**：B33 的 DP 清单与 §10.2 逐字一致（含 `IOField.DisplayStroke`、`ProgressBar.ValueText`、`Tank.FillGeometry`、`Valve.FillClipGeometry`、`Gauge.ValueText`）；`Hmi.Text/Rectangle/Panel` 只比属性袋 + 视觉；D21 按 M1 门槛判；B35 只覆盖 M1 的 10 个图元（`SelectorSwitch` 的坏值由 D11 在 M2 验证）。

### 16.7 M0 仍未覆盖（继续有效）

1. **P0-2** WS 回环延迟 p95 ≤ 20ms（探针 2 只测了并发发送吞吐，未测 RTT）。
2. **P0-4 后半** 10Hz × 1000 图元的帧组装/渲染争用（探针 3 只压命令泵，不压帧循环）。
3. **P0-5** 8 并发登录 + `ScadaUserStore` 加锁（探针未碰账号存储）。
4. **P0-6** 静态资源路径穿越（探针 2 只验了 `Static.Add` 原样字节）。
5. **P0-7** Playwright/`msedge` E2E（探针均为控制台/无浏览器）。
6. **P0-8** 1000 图元帧大小与 10Hz 带宽（决定 `FrameRateHz` 与序列化归属）。
7. **矩阵 A05**（双份契约反证）、**A06–A09**（协议版本、初始化异常隔离、重复加载、端口占用）、**A12**（会话令牌过期/撤销/篡改）、**A13**（Origin 白名单）、**A19**（单命令异常隔离）、**A21**（停止排空）、**A22**（启动顺序）、**A23**（`/api/status` 契约）、**A24**（中文编码与 7 列审计）、**A26**（配置热改语义）、**A27–A30**（`Static.Add` 参数化路径、64KB 消息上限、`SendTextAsync` 顺序、`DisconnectWebSocketSessionAsync`）。
8. **`_WebProbe1` 的环境事故**：执行期间另一个进程在改 `Scada\ScadaEventHook.cs`（CS0103），探针用 `--no-dependencies` 规避；01:05 复查已恢复 0 错误。该事故不影响 A–H 结论，但说明"改探针/构建时先看 mtime"（R9）仍然有效。

### 16.8 M0.1 未覆盖的 Watson 补测（六项，全部标"未覆盖"）

> 评审指出的**假绿**：`_WebProbe2` 的 `auth:true` 只观察到"客户端拿到 401"，没有把"业务 handler 一次都没执行"断言化；也没量 `MaxMessageSize`、`SendTextAsync` 顺序、`DisconnectWebSocketSessionAsync`。下表是必须补的六条，**补完之前不许在文档里写"A10 已 PASS"**。

| # | 补测项 | 判据（可判定） | 对应矩阵 |
| --- | --- | --- | --- |
| 1 | `auth:true` 的鉴权调用计数 | 每次握手 `authCalls` **恰好 +1**（不是"≥1"）；同一连接不重复回调 | A10 |
| 2 | 无 ticket / 错 ticket | 握手返回 **401**，且业务 handler 的进入计数 **== 0**（用静态计数器，不靠客户端异常推断）；响应体不含账号信息 | A10/F23 |
| 3 | `auth:false` 不触发鉴权 | 同一服务上另挂一条 `auth:false` 测试路由：`authCalls` **+0** 且能连上（反证探针能识别 `auth` 标志） | A11 |
| 4 | `AuthenticateApiRequest == null` 的 fail-open | 不赋值鉴权器 + `auth:true` → 连接**成功**（证明 fail-open 存在），所以生产必须在注册路由前断言非空 | A11 |
| 5 | `Static.Add` 参数化路径 | `Static.Add("/img/{id}/{ver}.png", …)` 能按路径段取值并原样吐字节；`../` 与 `%2e%2e` 被拒（与 F12 同一判据） | A27 |
| 6 | 消息上限 / 发送顺序 / 主动断开 | `WebSocketSettings.MaxMessageSize=65536`：65537 字节命令被拒且连接按策略关闭；4 线程 × 250 条 `SendTextAsync` 的接收顺序与单线程调用顺序一致（或明确记录"库不保证跨线程顺序"，则生产改为单发送队列）；`DisconnectWebSocketSessionAsync(guid, NormalClosure, reason)` 后 `IsWebSocketSessionConnected` 为 false、客户端收到关闭帧 | A28/A29/A30 |

**结论口径**：这六项补完并写进 `_WebProbe2` 的 README 之后，§12.2 的 P0-3 才能从"部分"改成"通过"；在那之前，M0 状态保持"未通过"。

### 16.9 M0.2 断言批次（2026-10-21）

> 目的：把 M0.1 的四条 blocking 从"部分关闭"推进到"代码本体可判定"。**本轮包含代码改动**（`ScadaRuntimeHost` 的 `AbortStart` / `TryGetCurrent`、`ScadaAuditWriter` 的 `_writeGate`、`ScadaUserStore` 的 `ResetPassword` CAS 与 `TryValidate` 复核、`ScadaWebModule` 的 `_registered` / `_initialized`、loader 的 `Prism*` 拒绝、`pack.ps1` 的 3c）。逐条执行记录见 `docs\SCADA运行态Web客户端\结果台账.md` §2.5。

| 项 | 结果 | 证据 |
| --- | --- | --- |
| 构建 | **0 错误 / 51 警告**（5.46s） | `_m02_build.log`（SHA256 前 12 `2EC9D54BA218`，18201 B，2026-10-21 02:41:56） |
| 全量断言 | **2242 通过 / 0 失败** | `_m02_run.log`（SHA256 前 12 `4DB2008B7D91`，342957 B，2026-10-21 02:42:40） |
| 被测产物 | `ScadaChecks.dll`（Debug, `net9.0-windows`）**SHA256 前 12 `070DC0A27C7F`**，892928 B，02:41:54 | 构建 02:41:56 → 运行 02:42:40（≈44 秒），产物未被覆盖，**本轮可同时锚定 DLL 与日志** |
| `[RH]` | **18/18 通过**（RH1–RH18） | 同日志 `[RH]` 段；RH10–RH18 覆盖 `TryGetCurrent` 三态、`RunStopped` 清空之后发、订阅方抛异常、`AbortStart` 两条失败路径、审计写锁 |
| `[ML]` | **6/6 通过**（ML1–ML6） | 同日志 `[ML]` 段；`Enabled=false` 零 `LoadFrom`、契约副本（`VM.Core.dll`）拒绝、真模块 `Name`/`Type` 正确、`Initialize` 两次只执行一次 |
| `[UC]` | **22/22 通过**（UC1–UC18c） | 同日志 `[UC]` 段（段内汇总"通过 22 / 失败 0"）；UC16–UC18c 覆盖改密/重置 CAS、重置与删号并发、PBKDF2 期间降权/删号 |

**结论**：代码本体 PASS；**门禁未过**——`B40` / `A01–A09` / `F23` / `F24` 需要监听器与 web 模块，M0 骨架无法执行，已移到 **M1 首周**（§17.1）。M0 记为"地基 + 2242/0"收口，**不是"全绿"**。

### 16.10 M1b 内核证据（2026-10-21）

> **口径**：本节记录 M1b **跑到了什么**，不是里程碑验收。`_WebProbe6` 与 `ScadaChecks` 都**不是矩阵用例**；矩阵 **261 条（v1.5）**仍未执行（§17.5 证据链 blocking）。

| 项 | 结果 | 证据 |
| --- | --- | --- |
| `_WebProbe6` 第一次 | **19 PASS / 0 FAIL** | `_m1b_probe6.log`（SHA256 前 12 `2F1350424F1C`，2714 B，2026-10-21 07:44:29） |
| `_WebProbe6` 第二次 | **19 PASS / 0 FAIL** | `_m1b_probe6b.log`（SHA256 前 12 `CA1269BDF787`，2714 B，2026-10-21 07:45:33） |
| 全量 `ScadaChecks` | **2266 通过 / 0 失败** | `_m1a_run.log`（SHA256 前 12 `3B820D75FBCE`，347924 B，2026-10-21 03:05:45）；构建 `_m1a_build.log`（`F02D4A8C5B78`，0 错误 / 51 警告）；**只锚定日志 hash**——对应 DLL 已被后续重建覆盖 |
| 浏览器 10 渲染器 / 1920 | 演示页 10 图元（含基座 + Panel 子坐标）渲染，截图 `demo-1920x1080.png` / `demo-1366x768-final.png`；Playwright + `msedge` 脚本在临时目录（未入仓，未留运行日志） | `_scada_web_shots\`（2026-10-21 03:15–03:26）；**这是前端实测，不是 M1b 后端联调** |
| 罐体修复 | `Hmi.Tank` 读数/位号深色底带 + 圆角裁剪；浏览器截图同上 | `elements-process.js` 的 `Hmi.Tank` 段；`_scada_web_shots\demo-1920x1080.png` |
| 双评审 | **FAIL**（协议/证据两组），整改项 5 条 | §17.5 |

**`_WebProbe6` 19 条覆盖**：B1 监听 + WS 启用、B2–B4 静态 3 文件（`index/app.js/styles.css`；`elements-process.js/.css` 未单独探针）、B5 未知路径/穿越拒绝、B6 运行启动、B7 ticket、B8 WS 连接、B9 hello、B10 snapshot（10 元素）、B11 frame（10 items）、B12 四类值、B13 值变化、B14 `page`（`pageVersion` 11→12）、B15 `pong`、B16 `E_UNSUPPORTED`、B17 `stopped`、B18 新运行 `snapshot`（`runGeneration` 8）、B19 停服。

**M1b 没有覆盖（M1c 已补）**：登录卡片 / `sessionToken` 换票 / `BasePageVersion` / `DisplayFormat` / `/elements-process.*` 与 `?v=` 静态路由——见 §16.11。**仍未覆盖**：动画 / 坏值 / `/img` / 矩阵 B 阶段任何一条（M1.5/M2 与矩阵轮次）。

### 16.11 M1c 收尾证据（2026-10-21）

> **口径**：本节记录 M1c **跑到了什么**。`Web\ScadaWebChecks` 是**协议层断言宿主**（正式工程、已入 `VisionMaster.sln`），`_WebProbe6` 是探针，**两者都不是矩阵 261 条的执行**。逐条记录见 `docs\SCADA运行态Web客户端\结果台账.md` §2.7。

| 项 | 结果 | 证据 |
| --- | --- | --- |
| `Web\ScadaWebChecks`（Release） | **33 PASS / 0 FAIL** | `_m1c_webchecks33b.log`（SHA256 前 12 `C75F4FE9AF80`，28594 B，08:57:29）；被测 DLL `71B953980241`（67584 B，08:57:17），**DLL 与日志同一轮可锚定** |
| `_WebProbe6`（Debug + Release） | **31 PASS / 0 FAIL** | Agent S 报告；源码 `_WebProbe6\Program.cs` 08:11:05、产物 08:14:08 / 08:14:10；**仓内未留该轮日志**（19 条基线见 §16.10） |
| 全量 `ScadaChecks`（Release） | **2276 通过 / 0 失败** | `_m1c_sc.log`（SHA256 前 12 `CCB425F345BD`，505824 B，08:36:34）；被测 DLL `54A1F9527A99`（893952 B，08:35:59） |
| 重连链路 | ticket 30s/单次；`sessionToken` 8h、可复用、不轮换；`POST /api/ticket {refreshToken}` 换新票；新票重连成功、旧票 401 | `ScadaWebChecks` M1c-01~M1c-04 |
| 基座版本 | 快照信封 `basePageVersion`（无基座为 `0`）；`PageKey = pageId@pageVersion@basePageVersion`；基座 2→3 发 `page`、本页 `pageVersion` 不变 | M1c-05 / M1c-06 |
| 静态路由 | `/elements-process.js` 39628 B、`.css` 4461 B；带 `?v=4` 同为 200 | M1c-07 / M1c-08 |
| 帧 `text` | 用 `Binding.DisplayFormat`（`IFormattable`，InvariantCulture）；`Gain/Offset/FormatType` 留 M1.5 | B11/B12/B13；矩阵 W1-22 未跑 |
| 前端 | 登录卡片、`sessionStorage`、断线换票重连、401 回登录、`MIN_SCALE=0.8`（1280 滚动 / 1920 无滚动）、`#demo-chip` + 水印 | Agent S 浏览器实测（**未留运行日志**）；`已知偏差.md` 第 6/10/11 条 |
| 矩阵覆盖 | **B 阶段仅"部分"5 条**（B21/B33/B34/B39/B40）；B20/B23/B24/B36 未跑；B35/B37 归 M1.5 | 台账 §2.7.5 |

**结论**：M1b 五条 blocking 全部关闭（§17.5）；**M1 只读核心 PASS，M1 验收矩阵未全跑**；**M2 可开工，M1 全量矩阵待补**。

---

## 17. M0.1 / M0.2 / M1b / M1c / M2a 整改清单与轮次门禁

> **本节是 M0 的唯一状态来源**：M0 的探针结论在 §16，M0 的**门禁状态**在这里。**M0.2 复评结论（2026-10-21）**：代码本体 PASS（全量 `ScadaChecks` **2242 通过 / 0 失败**）；四条 blocking 的"文档 / 实现"部分已落地，但**运行时门禁 `B40` / `A01–A09` / `F23` / `F24` 需要监听器与 web 模块，M0 骨架无法执行**，已显式移到 **M1 首周**。因此 M0.2 以"**地基 + 2242/0**"收口，**不写"M0 全绿"**；M1 首周必须补齐这四项并重跑 A 阶段 P0。已落地的部分有断言与构建证据（见 `docs\SCADA运行态Web客户端\结果台账.md` §2.5），未落地的部分逐条写在"状态"列，**不许把"部分关闭"读成"通过"**。

### 17.1 四条 blocking（运行时门禁缺一条 M0 不算通过）

> **M1c 状态（2026-10-21）**：**M1 只读核心 PASS、M1 验收矩阵未全跑**。M1b 的五条 blocking（重连 / 登录入口 / 基座版本 / 协议对齐 / 证据链）已在 **M1c 关闭**（§17.5）；下表 B1–B4 是 M0.1 的历史 blocking，已按"部分关闭"记录，**不要与 M1b 的五条混算**。

| # | blocking | 为什么是 blocking（评审结论） | 整改内容 | 责任人 | 验收（可判定） | 状态 |
| --- | --- | --- | --- | --- | --- | --- |
| **B1** | **`IScadaRunHub` 状态机不可判定** | 现设计只给了 `IsRunning/Session/Document` + 两个事件，没定义"什么时候 Session 还可读""重复 Start/Stop 怎么算""Start 失败发不发事件""web 在运行中才订阅时怎么补首帧"。这些正是"首帧丢内容/重复 Loaded/关窗后读到 null"的来源 | ① 状态机抽成 `ScadaRunHubState`（`TryStarted`/`TryStopped`/`Reset`，generation 单调）；② 事件签名带 generation（`Action<ScadaRuntime,ScadaDocument,long>` / `Action<long>`）；③ `RunStarted` 在 `Show()` 成功之后发、`RunStopped` 在 `_session/_window` 清空之后发（处理器里 `Session/Document` **为 null**）；④ 启动失败/重复启动不发；⑤ 晚订阅者用 `TryGetCurrent(out session, out document, out generation)` | architect + 实现 | `ScadaChecks` **RH1–RH18 全绿**（状态机六条 + 真实宿主"未启动 Stop"兜底 + `TryGetCurrent` 三态 + `RunStopped` 清空之后发 + 订阅方抛异常 + `AbortStart` 两条失败路径 + 审计 sink 映射）；`TryGetCurrent` 有实现且 **B40**（运行中加载模块 → 1 帧内出首帧；`RunStopped` 后返回 false）全绿 | **部分关闭（M0.2 地基 PASS）**：`TryGetCurrent` 已落地（`IScadaRunHub.cs:50` / `ScadaRuntimeHost.cs:285`），RH1–RH18 全绿（`ScadaChecks 2242/0`）；**B40 需要监听器与 web 模块，转 M1 首周** → M0 门禁仍未通过 |
| **B2** | **契约下沉与 `ScadaUserStore` 锁** | 契约放在 `VisionMaster` 会让 web 工程必须引用宿主 exe；`ScadaUserStore` 原来明确"只在 UI 线程读写、不加锁"，后台 PBKDF2 会与桌面登录/用户管理并发读写同一份内存态 | 四个契约（`IScadaRunHub`/`IScadaRenderCatalog`/`IScadaAlarmCommands`/`IScadaAlarmFeed`）一律放 `VM.Scada`（不引用 WPF、不引用 `VM.Core`）；`ScadaUserStore` 加锁：**锁内**只做"找记录 + 复制盐/哈希/迭代次数/角色"，**锁外**算 PBKDF2；`Save` 用"锁内拍快照 → 锁外原子写"；同步改类注释 | architect + 实现 | ①`Scada\VM.Scada.csproj` 仍无 WPF 引用（静态扫描）；②8 并发登录 UI 线程停顿 **0**（P0-5）；③插桩显示持锁时长 < 1ms；④`ScadaChecks` 用户存储断言全绿（UC1–UC18c）；⑤`ScadaUserStore.cs` 里不再有"只在 UI 线程"的表述 | **部分关闭（M0.2 地基 PASS）**：`IScadaRunHub` / `IScadaRenderCatalog` / `IScadaAuditSink` / `IScadaUserAuthenticator` 已进 `VM.Scada`；`ScadaUserStore` 两把锁 + `ResetPassword` CAS + `TryValidate` 复核已落地、**UC1–UC18c 全绿**；**`IScadaAlarmCommands`/`IScadaAlarmFeed`（M2/M3）未落地**；P0-5（UI 停顿）与持锁时长插桩未做 |
| **B3** | **模块生命周期与加载器** | `Enabled=false` 零加载、`Initialize` 幂等、加载失败只 Warn、双份契约识别、端口占用不阻断——这五条各自都有"静默故障"形态（不加载却报了监听、失败被吞、两份 `IAppModule` 类型导致 `is` 判定失败） | loader 直读 `AppConfig.json`（容器未建）；`Enabled=false` **连 `LoadFrom` 都不做**；失败只回 `out diagnostic` + 容器就绪后 Warn 一次、**不 rethrow**；`Web\` 出现契约副本（`VM.Core.dll`/`VM.Scada.dll`/`Core.Interfaces.dll`/`Newtonsoft.Json.dll`）即拒绝加载并说明原因；端口占用记 Error 并继续启动 | 实现 | A01–A09 全绿，其中 **A05（双份契约反证）必须有"拒绝加载 + 日志说明原因"的证据**；A07（初始化异常隔离）与 A08（重复加载）不得出现两套会话表 | **部分关闭（M0.2 地基 PASS）**：loader 已落地（`ScadaWebModuleLoader.cs:83-200`，含 `Prism*.dll` 前缀拒绝；`_WebProbe1` 6/6 支持两态与类型身份）；`Initialize` 幂等已落地（`_registered`/`_initialized`，ML6 连续两次只执行一次）；**ML1–ML6 全绿**；**端口占用与 A01–A09 需要监听器，转 M1 首周**（A05 双份契约反证同批） |
| **B4** | **认证传输与矩阵同步** | 方案写 ticket（无 cookie），矩阵写 `POST /login` + HttpOnly Cookie；两套并存时实现会按矩阵做，最终"CSRF 防护"变成一句空话。另有**四处**同类冲突（审计列、平局、默认值、`pageVersion`） | ①认证按 §6.1/§8.9：`POST /api/ticket`（30s/单次）→ `WS /ws?ticket=…` → `AuthenticateApiRequest` 校验 → **不写任何 `Set-Cookie`**；重连由 `sessionToken`（8h）+ `refreshToken` 换票（M1c）；②图片走独立 `imageToken`（M1.5）；③矩阵 B02/F10/F11/F17/C42 改写、F23/F24 新增；④审计统一 7 列 + 前缀且**拼装点在 `ScadaAuditWriter.Append(string,…)`**；⑤平局统一"都可点"（§8.3）；⑥默认值统一 16/32/120KB/2MiB（§5.4）；⑦`PageKey` 联合判定（§9.6） | docs + 实现 + reviewer | ①两份文档一致性检查：**不得再出现**"默认 `MaxSessions=50`""入站队列 256""9 列审计""`HttpOnly`/`SameSite`/`Secure` 作为判据""`delta.baseVersion`""`Host` 配置键"这六类表述；②第一方代码 `Set-Cookie` 0 处；③F23（ticket 生命周期 + 握手 401 也写 Denied 审计）与 F24（令牌失效与轮换）全绿 | **部分关闭（文档 PASS；M1c 补了 ticket 生命周期与换票）**：①已达成（方案 v1.6 + 矩阵 v1.4 逐条对齐，冲突项 0）；②M1b/M1c 已有 HTTP 服务，第一方代码仍无 `Set-Cookie`（静态扫描 0 处），但**不构成"已验证不写"**；③F23/F24 的**换票与旧票 401 已由 `ScadaWebChecks` M1c-02/M1c-04 覆盖**；**握手 401 的 Denied 审计通道与 A 阶段用例仍待矩阵** → U-06/U-11 |

### 17.2 轮次门禁 R00–R05（与矩阵 §13 同一份口径）

| 编号 | 时点 | 必须做的事 | 不做的后果 |
| --- | --- | --- | --- |
| **R00** | 每轮**启动** | 需求 → 用例映射（哪条需求由哪些用例覆盖）；本轮新增功能**至少 1 正向 + 1 反向 + 1 越权/失败**用例；用例评审（另一角色签字，不许自审）；**派工单留存（并发子智能体 ≤8，含角色/任务/验收标准）** | 需求没人测、只测 happy path、派工无法追溯 |
| **R01** | 每轮**完成** | 本轮 P0 全绿；新增用例有证据（`evidence/<ID>/`）；**每条新闸门做变异反证**（改坏→准确红→还原绿）；跑相关回归；列未决清单 | "全绿"其实是"没跑" |
| **R02** | **里程碑** | M1/M2/M3 阶段门槛（矩阵 §8）+ 用户验收；P1 豁免必须**书面**（原因/影响/补测日期） | 带病进下一阶段 |
| **R03** | 进**暴力测试前** | M3 功能冻结；E 阶段全量跑过；24h 长稳**至少一次**完整绿 | 边改边压测，指标不可比 |
| **R04** | **优化后** | A–G 的 P0 回归全绿；p95 / p99 / 内存 / CPU / 帧大小 / 首屏退化按 **O04 的噪声带**判定（噪声带 < 5% 时用 5% 门槛，噪声带 ≥ 5% 时以噪声带为准并记录）；每个优化项可独立回滚；**O02 未跑不得宣称优化完成** | "优化"变"退化"且无人发现 |
| **R05** | **发布** | 结果台账完整（每用例一行）；真机 8 项（矩阵 §9）；安全渗透签字 | 发布无据可查 |

### 17.3 结果台账（唯一"跑过"的凭证）

| 列 | 填写要求 |
| --- | --- |
| `用例 ID` | 矩阵编号（A01/B17/C45/E25/F23/X01/O01…） |
| `构建 hash` | 被测产物的 **DLL SHA256 前 12 位**（本仓不是 git 仓库，给不出 commit hash）；同一轮所有行必须一致。**无法锚定到产物时**（日志对应的二进制已被重建）必须写明"只锚定日志 hash"并给出日志 SHA256 前 12 位，**不许拿当前 DLL 冒充** |
| `执行日期` | `YYYY-MM-DD HH:mm`（本地时间） |
| `执行人` | 真实姓名/工号，不许写"团队" |
| `环境` | 机器名 + OS 版本 + 浏览器版本 + `MaxSessions`/`FrameRateHz` 等**改过的**配置项 |
| `结果` | `通过` / `失败` / `阻塞`（**不许写"跳过"**——跳过按失败算，R38） |
| `证据路径` | `evidence/<用例ID>/` 下的具体文件（日志/CSV/截图/trace），空 = 没跑 |
| `缺陷 ID` | 失败/阻塞必填；通过可空 |
| `评审人` | 复核证据的人（R01 的评审签字） |
| `备注` | 偏差、豁免编号、复现步骤摘要 |

**台账规则**：只追加、不改写历史行（更正另起一行并引用原行）；每轮结束由 docs 角色汇总一次；`X03` 断言"台账行数 ≥ 本轮应跑用例数"。

### 17.4 与测试矩阵的对应

| 本文 | 矩阵 |
| --- | --- |
| §17.1 B1–B4 | A 阶段 P0（A01–A11）+ M0.1 / M0.2 门禁；其中 `B40` / `A01–A09` / `F23` / `F24` 在 **M1 全量矩阵轮**执行（M1c 已部分覆盖：B40 首帧、F23/F24 换票与旧票 401） |
| §4.1 `IScadaRunHub` / `TryGetCurrent` | B11（RunStopped 后为 null）、B21、B23、B40（晚订阅首帧） |
| §17.2 R00–R05 | §13「轮次门禁与结果台账」（R00–R05 表） |
| §17.3 结果台账 | §13.2 台账列 + X03 + `docs\SCADA运行态Web客户端\结果台账.md` |
| §6.1 `imageToken` | B37（图片鉴权负面，**M1.5**）、C42、F11 |
| §8.4 `AllOperators` 监管闸门 | C44 / C65（缺项拒绝启用）/ C66（全开可启用） |
| §8.4.1 `confirmToken` | C46 |
| §8.7 两层审计 + 前缀唯一拼装 | C49（计数）/ C67（不双前缀）/ C19/C20/C26/C43（格式与过滤） |
| §9.6 `PageKey` | B17–B19、B38（跨页低版本）、B39（基座版本） |
| §10.2.1 未实现图元 | B36 / W1-31 |
| §10.2.2 坏值 | B35（**M1.5/M2**）/ W1-32 |
| §10.2 M1 派生量清单 | B33（已实现，M1b）/ B33b（未实现，M1.5）/ W1-21 / D04–D09（分解证据） |
| §12.6 暴力测试 | E25–E29（**M3**） |
| 优化阶段 | §14「优化阶段」（O01–O04） |
| 文档一致性 / 整改闭环 | X04 / X05 |

### 17.5 M1b 评审 blocking（M1c 已关闭）

> **状态口径**：M1b **内核 PASS**（§16.10），五条 blocking 在 **M1c 全部关闭**（§16.11、台账 §2.7）。**"关闭"指 blocking 本身有可判定证据，不等于"M1 全绿"**——M1 收口仍欠矩阵 B 阶段 P0 全绿（U-15）。

| # | blocking | 为什么是 blocking | 整改内容（M1c） | 当前状态（as-built） |
| --- | --- | --- | --- | --- |
| **M1b-1** | **重连** | 旧口径 `hello.resume` 在 M1b 不存在；ticket 单次 30s，断线后无法重连，用户只能重新登录 | 服务端在 ticket 核销时签发 `sessionToken`（8h）；`hello` 下发；客户端 `POST /api/ticket {"refreshToken"}` 换新 ticket；ticket 仍单次/30s；`sessionStorage` 保存、不进 URL | **已关闭（M1c）**：`hello` 带 `sessionToken`（`sessionExpiresIn=28799`）；`POST /api/ticket {refreshToken}` 换票 200 且返回**同一** token（不轮换）；新票重连成功；旧票复用 401（`ScadaWebChecks` M1c-01~M1c-04）。**空闲超时回收仍未实现**（矩阵 B30） |
| **M1b-2** | **登录入口** | 浏览器无 ticket 时只显示"请从桌面端获取链接"；现场无法在浏览器登录 | 无 ticket → 登录卡片；`POST /api/ticket {user,password}` 取票；`AllowAnonymousView=true` 时"以只读身份查看"；失败显示 401 文案；M1c 补默认密码 gate | **已关闭（M1c，登录 / 换票路径）**：登录卡片（用户名/密码/登录/匿名只读）、`sessionStorage`、断线换票重连、401 回登录均已落地（Agent S 浏览器实测）；服务端 `refreshToken` 分支由 M1c-02 验证。**默认密码 gate / Origin 校验 / 每 IP 限速仍未做 → U-13** |
| **M1b-3** | **基座版本** | 快照没有 `BasePageVersion`，`PageKey` 只有 `pageId@pageVersion`；改基座不触发 `page`，浏览器永远看旧基座 | 快照信封加 `basePageVersion`；`PageKey = pageId@pageVersion@basePageVersion`；基座版本变化 → 下一拍发 `page` | **已关闭（M1c）**：快照信封带 `basePageVersion`（无基座为 `0`）；`PageKey = pageId@pageVersion@basePageVersion`；基座 2→3 发 `page`、本页 `pageVersion` 不变（M1c-05 / M1c-06）。**B39 的"所有引用页重建 / 无闪烁 / 不触发 `Loaded`"仍待矩阵** |
| **M1b-4** | **协议对齐** | 文档旧版是 `kind` + `state/caps/view.attach/auth.login/view.resync`，实现是 `type` + `hello/snapshot/page/frame/stopped/idle/pong/error`；两份口径并存会让联调整段消息被忽略 | 方案 §9、§6.2、§8.9 与矩阵 B02/B10/B11/B16–B19 全量改为 as-built；M2 扩展点单独标注；**禁止再出现 `kind`** | **已关闭（M1c）**：方案 v1.6 / 矩阵 v1.4 与实现同为 `type`；`_WebProbe6` 31/31 与 `ScadaWebChecks` 33/33 证明 as-built 消息集；`app.js` 仍兼容读取 `message.kind`（防御，不构成两份协议） |
| **M1b-5** | **证据链** | `_WebProbe6` 19/19 与 `ScadaChecks` 2266/0 都是**探针/断言**，不是矩阵用例；台账缺 M1b 行；DLL 已被后续重建覆盖，只能锚定日志 hash | 台账补 M1b 行（日志 hash、19/19、2266/0、浏览器截图、罐体修复、双评审 FAIL）；新建 `docs\code-changes\2026-10-21-SCADA运行态Web-M1b.md`；写明"矩阵 248 条未执行" | **已关闭（M1c，台账 / 文档）**：台账 §2.7 记录 33/33、31/31、2276/0 与三组 hash；证据边界（`_WebProbe6` 31/31 无仓内日志、只锚定源码/产物时间）已写明。**矩阵 248 条仍未执行** → U-15，**不得写"M1 全绿"** |

**M1c 退出条件（逐条状态）**：① 五条 blocking 全部有可判定证据——**已达成（M1c）**；② **矩阵 B 阶段 M1 门槛的 P0 全绿**——**未达成**（**261 条执行 0 条**；B20 按 10 图元 × 10 客户端，B26 归 M3、B35/B37 归 M1.5/M2、B33b 归 M1.5，均不在 M1 门槛）；③ `ScadaChecks` 无回归——**已达成（2276/0）**；`FlowCanvasChecks` 未纳入本批证据链（并发任务另有 `_array_run_fc4.log` 08:35:18 / 3038 通过，不属 M1c 派工）；④ 台账行与证据路径齐全（X03/X05）——**部分**（M1c 批次已入账，矩阵行仍空）；⑤ 用户验收（R02）——**未执行**。**结论：M1c 整改项关闭，M1 里程碑未收口；M2 可开工，M1 全量矩阵待补。**

### 17.6 M2a blocking（M2a 地基 PASS，C68–C80 未执行）

> **口径**：M2a **已接线、Release 已重建并核符号**，`Web\ScadaWebChecks` **97 PASS / 0 FAIL**（`_m2a_webchecks13.log` `AA8A43733714`，DLL `E91456D97780`；`ScadaChecks` 2332/0）；第一轮 89/89（`_m2a_webchecks3.log` `CF142B22F99A`，DLL `C3E16AED480C`）为**历史证据**；**矩阵 C68–C80 执行 0 条**。下表"为什么是 blocking"记录的是**整改前**的问题，"当前状态"记录可判定证据——**断言通过不等于矩阵通过**，`AllowCommands` 保持 `false`，M2a 不写"全绿"。

| # | blocking | 为什么是 blocking | 整改内容 | 验收（可判定） | 当前状态（as-built） |
| --- | --- | --- | --- | --- | --- |
| **M2a-1** | **宿主未接线** | 命令路由/泵/安全门都还是"孤岛类"：`ScadaWebHost` 仍只有 M1b/M1c 的 hello/snapshot/frame 路径，`POST /api/ticket` 与 WS 握手没有调用 `ScadaWebSecurityGate`；命令消息仍走 `E_UNSUPPORTED` | 在 `ScadaWebHost` 里接：ticket 三闸（Origin → 限速 → 默认密码）→ `RevokeSession/RevokeUser` → WS 收帧解析 → `CommandPump.Enqueue` → `Completed` 回 `ack/error`；`ScadaWebModule` 构造 `ScadaWebCommandRouter`（注入 dispatcher/valueWriter/alarmCommands/policy） | 端到端：登录 → caps → `cmd.element` → ack；`E_UNSUPPORTED` 不再出现（C79 的反向：`AllowCommands=false` 时是 `E_POLICY`）；`ScadaWebChecks` 新增 C 段断言全绿 | **已关闭（M2a 接线）**：`POST /api/ticket` 三闸 → WS 收帧 → `CommandPump.Enqueue` → `ack/error` 已接（`ScadaWebHost` / `ScadaWebModule`）；`AllowCommands=false` 时回 `E_POLICY`、`E_UNSUPPORTED` 不再出现；证据 = **97/97 + 12 冒烟**（第一轮 89/89 为历史证据） |
| **M2a-2** | **安全前置未验证** | `ScadaWebSecurityGate` 的判据存在，但没有任何运行时证据：默认密码 gate、Origin、每 IP 限速、匿名令牌不续期、撤销、会话表上限都只在类里 | 接线后逐项跑 C68–C72；每条做变异反证（例如把 gate 判据删掉 → C68 必须红） | C68–C72 全绿 + 变异反证记录；`ScadaWebChecks\M2SecurityChecks.cs` 的条数与明细逐条打出来（R65） | **部分（19 安全断言通过；C68–C72 未执行）**：`M2SecurityChecks` 19/19 覆盖默认密码 gate / Origin / 限速 / 匿名不续期 / 撤销 / 会话表上限的类级行为（新增 S19 用户名尾空格 Trim）；**C68–C72 的端到端与变异反证未跑** |
| **M2a-3** | **三闸可被省略** | `ScadaWebCommandRouter` 的三闸是"带了才判"（`RunGeneration > 0`、`PageId` 非空、`PageVersion > 0`，`:523/541/560`）。省略字段 = 三条闸全跳过，直接进权限层——"点的是上一页的按钮"变成合法命令 | 三闸改为必填：缺 `runGeneration`/`pageId`/`pageVersion` → `E_BAD_REQUEST`（或按 0/空判 `E_STALE_*`，但**不许跳过**）；前端 `app.js` 每次命令都带三值 | C75 全绿（含"缺任一项 → `E_BAD_REQUEST`"与"三闸都错只报第一道"）；变异反证：把 `>0` 判据改回去 → C75 红 | **已关闭（三闸必填）**：缺 `runGeneration` / `pageId` / `pageVersion` 一律 `E_BAD_REQUEST`，顺序 `runGeneration → pageId → pageVersion`（`ScadaWebCommandRouter.cs:546-590`）；C75 待执行 |
| **M2a-4** | **幂等去重未实现** | `ScadaWebCommand.cs:112` 逐字："M2 不做幂等去重，重复请求会重复执行"。双击、断线重发、代理重试都会写两次；C02/C17 是 P0 | 补 `(sessionId, requestId)` 5 分钟去重表（有界、按时间清理）；重复请求返回上次 `ack`，不重复执行；表满时按最旧淘汰并 Warn | C02/C17 全绿；`E_BUSY` 后用同一 `requestId` 重试不重复执行（C35/C74 的交叉判据） | **已关闭（幂等去重）**：`(SessionId, RequestId)` 去重，`AckWindow=5 分钟` / `Retention=30 分钟`；窗口内重放回原 `ack`、窗口外回 `E_DUPLICATE`（`ScadaWebCommandRouter.cs:1060/1063`）；C02/C17/C74 待执行 |
| **M2a-5** | **矩阵与断言未执行** | `M2SecurityChecks.cs` / `M2ActionFilterChecks.cs` / `M2CommandChecks.cs` 已存在但**未运行**；矩阵 C 阶段 80 条执行 0 条；M2a 的"全绿"没有任何凭证 | 先 `dotnet build` 再 `dotnet run`（R65，不许 `--no-build`）；跑完把条数 + 明细行入台账；矩阵按 §12 顺序执行 | 台账有 M2a 行（`通过`/`失败`，不许"跳过"）；C 阶段 P0 全绿；X03 行数 ≥ 本轮应跑数 | **部分（97/97 断言；C68–C80 未执行）**：`M2SecurityChecks` 19/19、`M2ActionFilterChecks` 11/11、`M2CommandChecks` 26/26、`M2RegressionChecks` 4/4，加 33 基线 + 4 静态资产（第一轮 89/89 为历史证据）；矩阵 C 阶段 80 条执行 0 条 |

**M2a 的"完成"定义（四条同时满足）**：① 上表五条全部关闭且有证据；② W2-01~W2-31 全绿；③ 矩阵 C 阶段 P0 全绿（C68–C80 是新增门槛）；④ 用户验收（R02）签字。**任一条不满足，`AllowCommands` 保持 `false`，不得对现场开放写路径。**

### 17.6.1 第二轮终审新增 P0（已关闭）

> 2026-10-21 12:33 终审 FAIL 出两条新 P0，均已关闭；证据：`Web\ScadaWebChecks` **97/97**（`_m2a_webchecks13.log` `AA8A43733714`，DLL `E91456D97780`）、`ScadaChecks` **2332/0**（`_m2a_scadachecks3.log` `92F0CF616A04`）。

| # | 缺陷（为什么是 P0） | 修法 | 回归断言 |
| --- | --- | --- | --- |
| 新 P0-1 | `/api/ticket` 解析 `user` / `refreshToken` 后未 Trim：`"admin "` 与 `"admin"` 被当成两个身份，`RevokeUser("admin")` 撤不掉带尾空格的会话 → **改密 / 降权 / 删号后旧会话仍能续期**（8h 令牌继续有效，用户级撤销被一个空格绕过） | 取票解析后 `user = user.Trim(); refreshToken = refreshToken.Trim();`（`ScadaWebHost.cs:1077-1078`）；票层再 `userName.Trim()` 兜底（`ScadaWebTicketService.cs:277`） | **S19**（`M2SecurityChecks.cs:370-381`）：用 `"alice "` 签票后 `RevokeUser("alice")` → 令牌失效并回 `E_SESSION_REVOKED` |
| 新 P0-2（P1-1） | `AllOperators` 的监管项只在启动时校验一次；随后 `ConfigCheck.Load()` 的 `PopulateObject` **原地回写**把非法值写回同一对象 → 运行期 `Evaluate` 只看 `ClickPolicy` 就放行，五项监管闸门形同虚设（不报错、配置看着还是开的） | `ScadaWebAccessPolicy.ValidateSupervision(ScadaWebSettings?, out string)` 静态重载（`:354`）；`Evaluate` 的 `AllOperators` 分支每次判权复核（`:219-224`），缺项回退 `EvaluateHighestRoleOnly` | **M2-07e**（`M2CommandChecks.cs:489-504`）：`RequireAudit=false` 时工程师不可点、管理员可点（回退 `HighestRoleOnly`） |

### 17.7 M2b 门槛

> **口径**：M2a 只到"地基 PASS"（§17.6），**断言通过不等于矩阵通过**。下列六条是 M2b 的收口门槛；**任一条未满足，`AllowCommands` 保持默认 `false`，写路径不对现场开放**。

1. **租约进 `CanOperate`**（C77）：租约状态参与逐图元 `caps` 判定，与命令执行同源。
2. **危险动作确认 UI 已接**：客户端 `cmd.confirm.request {elementId, event}` 取 token 后，用**新 `requestId`** 重发原命令。
3. **AckButton 报警列表 / 未确认计数**：报警确认入口有列表与未确认计数支撑（M2a 只验证单条确认）。
4. **矩阵 C68–C80 全跑**：13 条逐条入台账（`通过`/`失败`，不许"跳过"）。
5. **真实浏览器双角色 E2E + WS 4403**：两个角色的真实浏览器各跑一遍命令面；被撤销会话的关闭码必须是 4403。
6. **`AllowCommands` 仍默认 `false`**：M2b 验收（R02 用户签字）后才可打开。

> **另记（M2b 待办，不计入上面六条门槛）**：**审计异步化**（命令级审计仍在 UI 线程同步写盘）、**完整 HTTP/WS 负向断言**（当前只有类级与协议层断言，缺端到端负向矩阵）。

> **M2b 进展（2026-10-21 13:00；E2E 13:06；HTTP 端到端 13:19）**：操作权租约（`ScadaWebLease.cs`，`acquire/renew/release`；`OperationLeaseEnabled && AllOperatorsRequiresLease` 时 `CanOperate` 要求 holder，caps 下发 lease）与 AckButton 报警列表（`IScadaAlarmFeed` + `ScadaRuntimeHost.Snapshot()` + 每 2 秒 `alarms` 广播 + `UnacknowledgedCount` 注入）已落地；断言 `Web\ScadaWebChecks` **109/109**（`_m3_final_webchecks.log` 18540 B / 13:19:36 / `741A8ABB3234`，DLL 181248 B / 13:19:18 / `8D732441A002`；含新增 **M2HttpChecks 4/4**：C68 默认密码 / C69 取票限速 / C70 匿名不续期 / C72 会话表上限）、`ScadaChecks` **2340/0**（`_m2b_scadachecks1.log` `4B0C8CD94647`）。**§17.7 六条门槛的跑动状态**：租约 C77、确认 UI、AckButton 报警列表、C68–C80（**C78 六图元视觉反馈未逐控件 E2E**）、真实浏览器双角色 E2E（**HighestRoleOnly + AllOperators 两种策略**）+ WS 4403 **均已跑**。真实浏览器双角色 E2E 核心场景：`Web\ScadaWebE2E` 宿主夹具（真实 `ScadaWebHost` + 六个交互图元 + `admin/admin123`、`op/op123`）——admin 登录后 6 图元可点，点「启动按钮」宿主日志 `[E2E] dispatch source=启动按钮 operator=admin actions=1 clicks=1`、审计 `Pressed 启动按钮 成功 hooks=1 actions=1 ok=1 skipped=0 failed=0`；op 登录后按钮 `[disabled]`（原因「只读：当前只有「管理员」可以操作（「op」 是「操作员」）」），点顶栏租约后显示 `租约：我（15s）`，宿主收到 `lease.acquire` 并回 ack（`holder=op remaining=60s`）；截图 `docs\SCADA运行态Web客户端\evidence\m2b-admin-role.png` / `m2b-operator-readonly.png`，日志 `_m2b_e2e_host2.log`。**`Web\ScadaWebE2E --all` 又跑通 `ClickPolicy=AllOperators`**：admin 与 op 两个真实浏览器标签都点击「启动按钮」成功——宿主日志 `_m3_e2e_all_host2.log:17-18`（`operator=admin clicks=1`，审计 `Pressed … ok=1`）、`:20-21`（`operator=op clicks=2`，审计 `Pressed … ok=1`），截图 `docs\SCADA运行态Web客户端\evidence\m3-alloperators-op.png`。**HighestRoleOnly + AllOperators 两种策略都跑通（后者 admin/op 均可点，审计分别记录）**。**仍待办**：C78 逐控件 E2E、审计异步化、完整 HTTP/WS 负向矩阵；`AllowCommands` 仍默认 `false`，**不写"六条门槛全部闭环"**。

> **M3 压力测试（基础档 2026-10-21 13:12–13:16；复杂档 13:44–13:47）**：`Web\ScadaWebStress` 已扩展为**多页面 / 多图元 / 多绑定**，并在命令发完后**真实切页**（客户端收 `page`/`snapshot`/`frame`）。
>
> **基础档（单页 1000 图元）**：5×10 / 50×20 / 100×20 三档；100 客户端 × 1000 图元 × 2000 命令 **`errors=0`**（`acks=2000`、`audit=2100`、`dispatcher=2000`）。延迟/内存：50 客户端 p95=98ms / 324MB；100 客户端优化前 p95=709ms / 721MB，优化后 p95=850–1635ms / 202–294MB（`ApplyCaps` 每页只取一次 `OnlineSnapshot`（`ScadaWebHost.cs:2263-2271`、`ScadaWebAccessPolicy.cs:334`），消除逐图元 `presence.Sessions` 的 O(n²) 复制——**内存显著下降、p95 上升**）。日志 `_m3_stress_5x10.log` / `_m3_stress_50x20.log` / `_m3_stress_100x20.log` / `_m3_stress_100x20_opt.log` / `_m3_stress_100x20_opt2.log`。
>
> **复杂档（多页面 / 多绑定 / 切页）**：5×5（4 页 × 300 × 2 = 1200 图元 / 2400 绑定）`errors=0`、p95=57ms / 102MB；20×10（6 页 × 400 × 3 = **2400 图元 / 7200 绑定**）`errors=0`、`page` 帧 77、**p50/p95/max=2/302/466ms**、快照均值 238KB、175MB；50×10（同 2400/7200）`errors=0`、`page` 131、p95=1119ms / 808MB；100×5（6 页 × 300 × 2 = 1800 图元 / 3600 绑定）`errors=0`、`page` 151、p95=2066ms / 621MB。日志 `_m3_complex_5x5.log` / `_m3_complex_20x10.log` / `_m3_complex_50x10.log` / `_m3_complex_100x5.log`。**补充一档**：20×5 大页（10 页 × 500 × 3 = 5000 图元）`errors=0`、p50/p95/max=12/157/202ms、快照均值 298KB、201MB（`_m3_complex_20x5_big.log`）。
>
> **0 错误不等于 M3 达标**：100 客户端复杂档 p95=2066ms（基础档 709/850/1635ms）均高于矩阵 M8 的 <500ms 门槛；**20 客户端复杂档 p95=302ms 可作现场参考**；p95 口径与 §11 门槛的对应关系待对齐。

---

## 附录 A：事实来源（实现前逐条复核）

| 事实 | 出处 |
| --- | --- |
| 运行态宿主契约（`IsRunning`/`Start`/`Stop`） | `VisionMaster\Services\IScadaRuntimeHost.cs:23-39` |
| 宿主实现（单实例、报警、变量事件、节拍、数据泵、收尾） | `VisionMaster\Services\ScadaRuntimeHost.cs:72`、`:271`（`IsRunning`）、`:291`（`Start`）、`:395`（`Stop`）、`:429`（`PublishStarted`）、`:1006-1019`（`OnWindowClosed` 清空后发 `RunStopped`） |
| `IScadaRunHub`（六成员 + `TryGetCurrent`） | `Scada\Models\IScadaRunHub.cs:20-63`（`TryGetCurrent` `:50`）；实现 `VisionMaster\Services\ScadaRuntimeHost.cs:285` |
| 启停门禁纯状态机（`TryStarted`/`TryStopped`/`Reset`） | `Scada\Models\ScadaRunHubState.cs:42-76` |
| 审计落盘出口（8 参数重载） | `Scada\Models\IScadaAuditSink.cs`；实现 `VisionMaster\Services\ScadaAuditWriter.cs:375` 起（前缀唯一拼装点）、`:374-380`（结果词映射） |
| 审计写锁（`_writeGate`，任意线程安全、行不撕裂） | `VisionMaster\Services\ScadaAuditWriter.cs:239-248`、`:311` |
| 账号校验出口（后台线程）与改密 CAS | `Scada\Models\IScadaUserAuthenticator.cs`；实现 `ScadaUserStore.cs:258-282`；`TryValidate` 复核 `:278-341`；`ResetPassword` CAS `:496-543` |
| 渲染目录契约（`All` + 描述符 DTO） | `Scada\Models\IScadaRenderCatalog.cs:28-64`；宿主实现 `VisionMaster\Services\ScadaRenderCatalog.cs`、`App.xaml.cs:326` |
| 可选模块加载器（直读配置 / 拒契约副本含 `Prism*` / 不抛） | `VisionMaster\Services\ScadaWebModuleLoader.cs:83-200`、`:74-78`、`:240` |
| 模块两入口幂等（`_registered` / `_initialized`） | `Web\VisionMaster.ScadaWeb\ScadaWebModule.cs:37-41`、`:53-78` |
| 打包程序集归属校验（`3c`） | `pack.ps1:192-246` |
| `ScadaWeb` 配置节与默认值 | `Core\Models\AppConfigModel.cs:256-395`、`:586` |
| 宿主启停广播与账号并发断言 | `ScadaChecks\RunHubChecks.cs`（RH1–RH18）、`ScadaChecks\ModuleLoaderChecks.cs`（ML1–ML6）、`ScadaChecks\UserStoreConcurrencyChecks.cs`（UC1–UC18c） |
| `ScadaRuntime` 只在 UI 线程、无锁 | `Scada\Models\ScadaRuntime.cs:31-32` |
| `Start` 先设页再发 `PageLoaded` | `Scada\Models\ScadaRuntime.cs:284-290` |
| `Stop` 只发 `Unloaded`、不发 `Loaded` | `Scada\Models\ScadaRuntime.cs:306-320` |
| 切页唯一落点 `SwitchTo`（Unloaded → 换页 → Loaded） | `Scada\Models\ScadaRuntime.cs:438-450` |
| 图元事件四道闸门（含权限） | `Scada\Models\ScadaRuntime.cs:191-217` |
| `ScadaPage.Version` 语义（单调、不进撤销栈） | `Scada\Models\ScadaPage.cs:250-278` |
| 可见性判据 `IsElementVisible` | `Scada\Models\ScadaPage.cs:1551` |
| 图元几何/绑定/动画/权限字段 | `Scada\Models\ScadaElement.cs:74`、`:186`、`:193`、`:232`、`:274`、`:306`、`:344`、`:383` |
| 角色三档与唯一判定 `Allows` | `Scada\Models\ScadaRole.cs` |
| 权限出口契约（未登录=操作员、只读） | `Scada\Models\IScadaAccessPolicy.cs` |
| 桌面权限实现只在 UI 线程 | `VisionMaster\Services\ScadaAccessPolicy.cs:32` |
| 账号存储、PBKDF2 十万次、线程口径 | `VisionMaster\Services\ScadaUserStore.cs:59-61`、`:105`、`:184` |
| 审计条目形状与线程口径（`_writeGate` 任意线程安全；web 仍经 UI 泵保序） | `VisionMaster\Services\ScadaAuditWriter.cs:76-121`、`:239-248`、`:311`；表头 7 列 `:209-211`；**文件已改**（实现 `IScadaAuditSink` + 8 参数 `Append` `:375` 起），但 **CSV 列不变**、前缀在 writer 内拼（§8.7） |
| 动作分发器：`Navigate` 真切页、`RequireConfirmation` 弹桌面模态 | `VisionMaster\Services\ScadaActionDispatcher.cs:171`、`:192-203`、`:353-354`、`:399-409` |
| 账号存储线程口径（两把锁：`_gate` 内存清单 / `_writeGate` 写事务） | `VisionMaster\Services\ScadaUserStore.cs:60-75`、`:155`、`:165`、`:233`、`:388-391` |
| Watson 消息上限 / 主动断开 | `DLL\Watson\WatsonWebserver.xml:6534`（`WebSocketSettings.MaxMessageSize`）、`:7493`（`DisconnectWebSocketSessionAsync`） |
| 模块契约与生命周期 | `Core\Modules\IAppModule.cs:9`、`Core\Lifetime\AppLifetimeService.cs:56-59`、`Core\Lifetime\IExitTask.cs`（`ExitTask`，含 `TimeoutMs`） |
| 宿主注册点 | `VisionMaster\App.xaml.cs:230`（loader）、`:238-250`（模块注册隔离）、`:309`（`IScadaAuditSink`）、`:319-321`（三条映射）、`:403`（`IScadaUserAuthenticator`） |
| Watson 7.2.0 引用与依赖 | `VisionMaster\VisionMaster.csproj:61-81`、`DLL\Watson\*.dll` |
| Watson `Get` 默认 pre-auth | `DLL\Watson\WatsonWebserver.xml:7279` |
| Watson `WebSocket` 默认 pre-auth | `DLL\Watson\WatsonWebserver.xml:7474` |
| Watson 路由分组与认证回调 | `DLL\Watson\WatsonWebserver.xml:5983-5990` |
| **Watson 实际 API 签名与默认值（反射导出）** | `_WebProbe2\dump.txt`、`_WebProbe2\README.md` §1 |
| **Watson 鉴权/静态资源/并发发送实测** | `_WebProbe2\README.md` §2/§4；`run_final.txt`；`run_default_ws.txt` |
| **模块加载/类型身份/私有依赖/Kestrel 反例** | `_WebProbe1\README.md` §4/§5；`Host\Program.cs`；`out\` |
| **UI 线程命令泵/背压/超时/拓扑** | `_WebProbe3\README.md` §3/§4；`out\run.log`；`out\run-mutation.log`；`out\round1-commands.csv`；`out\round5-audit.csv` |
| **M1b 服务端（协议/路由/值帧/静态）** | `Web\VisionMaster.ScadaWeb\ScadaWebHost.cs`（`hello`/`snapshot`/`page`/`frame`/`stopped`/`idle`/`pong`/`error`/`bye`；`PageKey`；10Hz 循环）；`ScadaWebSession.cs`；`ScadaWebTicketService.cs` |
| **M1b 值帧四类值** | `Web\VisionMaster.ScadaWeb\ScadaWebValueResolver.cs`（`ScadaFrameDto` / `ScadaFrameItemDto`：`elementId/property/text/number/bool/color`） |
| **M1b 快照 DTO / 构建器** | `Scada\Models\ScadaPageSnapshot.cs`、`ScadaPageSnapshotBuilder.cs`（`BasePage` 一层、`LayerVisible`、`Bindings[].DisplayFormat`） |
| **M1b 前端渲染器** | `Web\VisionMaster.ScadaWeb\wwwroot\elements-process.js`（10 图元）、`app.js`（连接/重连/登录卡片/缩放）、`elements-process.css`（字体栈/省略号） |
| **M1b 探针证据** | `_m1b_probe6.log`（`2F1350424F1C`）、`_m1b_probe6b.log`（`CA1269BDF787`）；`_m1a_build.log`（`F02D4A8C5B78`）、`_m1a_run.log`（`3B820D75FBCE`，2266/0）；`_scada_web_shots\` |
| **测试矩阵（261 条）** | `docs\SCADA运行态Web客户端测试矩阵.md`（v1.5） |
| **M0.1 构建/断言证据** | `_m01_sc_build6.log`（0 错误）、`_m01_sc_run2.log`（通过 2198 / 失败 0；含 RH1–RH9、UC1–UC15；**只锚定日志 hash**） |
| **M0.2 构建/断言证据** | `_m02_build.log`（0 错误 / 51 警告，SHA256 前 12 `2EC9D54BA218`）、`_m02_run.log`（通过 2242 / 失败 0，SHA256 前 12 `4DB2008B7D91`）；被测 DLL `ScadaChecks.dll` SHA256 前 12 `070DC0A27C7F` |
| **结果台账** | `docs\SCADA运行态Web客户端\结果台账.md` |
| 节拍默认 200ms | `Scada.Controls\Runtime\ScadaBeatSource.cs:41` |
| 数据泵"按变量订阅 + 脏值合并" | `Scada.Controls\Runtime\ScadaRuntimeBinder.cs:32-42` |
| 图元类型键与描述符 | `Scada.Controls\Descriptors\BuiltInElements.cs` |
| 图元注册表（`All` 公开属性在 `:93-97`） | `Scada.Controls\Descriptors\ElementRegistry.cs:9-97` |
| 父子坐标换算 | `Scada.Controls\Elements\ScadaElementBase.cs:1134-1137` |
| 旋转中心 (0.5, 0.5) | `Scada.Controls\Elements\ScadaElementBase.cs:1103-1105`（注释）、`:1154`（赋值） |
| 闪烁半周期 500ms、落到 `Opacity` | `Scada.Controls\Elements\ScadaElementBase.cs:513`、`:780-808` |
| 底板子原点 = 标题高度 | `Scada.Controls\Elements\PanelElement.cs:86-89` |
| `HImage → BitmapSource` 在值源里完成 | `Core\Binding\ScadaValueSource.cs:55-65` |
| 写通道契约（按文本、目标类型现问） | `Scada\Models\IScadaValueWriter.cs`（`TryWriteText`） |
| 值通道契约（解析/订阅/读/写） | `Scada\Models\IScadaValueSource.cs` |
| 值转换四个入口 | `Scada\Models\ScadaValueConvert.cs:38-111` |
| 报警引擎确认接口 | `Scada\Models\ScadaAlarmEngine.cs:418`、`:447` |
| 动作类型全表 | `Scada\Models\ScadaActionType.cs` |
| 事件类型全表 | `Scada\Models\ScadaEventType.cs` |
| 动画类型全表 | `Scada\Models\ScadaAnimationType.cs` |
| 治理例外原文 | `AGENTS.md:460-466` |

---

## 附录 B：术语

| 术语 | 含义 |
| --- | --- |
| 内核 | 桌面端的 `ScadaRuntime` 及其引擎集合（一个进程一份） |
| 视图会话 | 一个浏览器连接对应的 `ScadaWebSession`（只有认证/连接/镜像/队列） |
| 镜像模式 | 所有会话跟随内核当前页；切页只在内核 |
| `runGeneration` | 一次"点运行"的代号；跨运行的所有缓存/命令都以它为准 |
| `pageVersion` | `ScadaPage.Version`，**每页各自**的结构版本（同页内单调）；只随结构变化，绑定值变化不递增。**跨页不比大小**——判定用 `(pageId, pageVersion)` 联合（§9.6） |
| `(pageId, pageVersion)` | 结构版本的**联合键**：先比 `pageId`（不同 = 切页），同页才比 `pageVersion`（不等 = 跳变/回退 → 全量快照） |
| `ticket` | 一次性握手准入凭据（`POST /api/ticket` 发放，30s、单次）；**不是登录态**、不放 cookie |
| `sessionToken` | 重连凭据（8h、可重复），在 ticket 核销时签发、`hello` 下发；只用于 `POST /api/ticket {refreshToken}` 换新 ticket，**不能当 ticket 用、不进 URL** |
| `imageToken` | 图片专用短时凭据（M1.5；按会话、30–60s、可轮换、**只放 `/img` 的 query**）；与会话令牌完全分开，不能 resume/发命令 |
| `sessionRef` | 对外的**不透明**会话标识（16 hex）；只给界面看，不能用于命令/审计/鉴权，反查不出 `sessionId` |
| `confirmToken` | 危险动作的一次性确认凭据（绑定 session+element+action、默认 30s、单次），替代桌面模态框（M2a） |
| `hello` | 服务端连接后主动发第一帧：`protocol/server/sessionRef/readOnly/running/pageId/pageName/pageVersion/online/sessionToken/sessionExpiresIn`（M1c as-built） |
| `snapshot` / `page` | 整份结构：`{type, seq, runGeneration, pageId, pageVersion, page}`；`page` 是 PascalCase；`snapshot` 用于首次/新一轮，`page` 用于同一轮 `PageKey` 变化 |
| `frame` | 值帧：`{type, seq, runGeneration, pageId, pageVersion, items[]}`；`items` 是 `{elementId, property, text, number, bool, color}` 数组，**不带 `baseVersion`、不带 `t/beatMs`** |
| `idle` / `stopped` | 未运行 / 运行结束；`stopped` 带 `runGeneration`，客户端保留最后一帧 |
| 命令级 / 动作级审计 | 一条 web 命令写 1 行命令级（带 `[web s=… ip=…]`）+ N 行动作级（dispatcher 写，与桌面同格式）；计数按层算 |
| 占位图元 | 已知但 web 未实现的 TypeKey 渲染成同尺寸中性块 + 固定提示文案（§10.2.1） |
| 轮次门禁 | 每轮启动/完成、里程碑、暴力前、优化后、发布这六个时点的强制检查（R00–R05） |
| 结果台账 | 每轮每用例一行的执行记录（用例 ID / 构建 hash / 证据路径…），没有台账行 = 没跑过 |
| M0.1 | M0 的整改阶段：四条 blocking 闭环 + Watson 六项补测 + 矩阵同步；不通过不进 M1 |
| `policyVersion` | 权限策略版本（角色/登录/租约变化时递增） |
| `caps` | 某会话对某页每个图元"能不能点"的映射 |
| 值帧 | 10Hz 的差异值消息（位置数组） |
| 共享帧 | 镜像模式下所有会话同一份的值帧字节 |
| 只读 | 能看不能操作；M1 的全局状态 |
| 操作租约 | `AllOperators` 下"同一时刻一个主操作员"的可选机制 |
| oracle | 验收基准：WPF 运行窗口的同一屏同一状态 |

---

## 附录 C：变更记录

| 日期 | 版本 | 内容 |
| --- | --- | --- |
| 2026-10-21 | v1.0 | 初稿：基于四角色评审结论与用户裁定，覆盖 M0–M3 架构、契约、协议、权限、图元分期、测试与验收 |
| 2026-10-21 | v1.1 | 回填 M0 三个探针（§16）；关闭 Watson auth 待实测项；修正静态资源路由、WS 握手 ticket、队列 16–64、`WaitAsync`/审计在回调内、禁用 `DropWrite`；引用 196 条测试矩阵并标注 M0 覆盖 |
| 2026-10-21 | v1.2 | **M0.1 整改**：M0 状态改为"三探针已执行 / 四角色评审 4/4 FAIL / 整改中"；新增 §17（四条 blocking + R00–R05 + 结果台账）；认证统一 ticket 无 cookie（§6.1/§8.9/§8.10）；审计回到 **7 列 + `说明` 前缀 `[web s=… ip=…]`** + 命令级/动作级两层（§8.7/§8.11）；`HighestRoleOnly` 平局都可点（§8.3）；默认值 16/32/120KB/2MiB 并可配（§4.2/§5.4）；`PageVersion` 只随结构变化、值帧不带 `baseVersion`（§9.5/§9.6）；新增未实现图元占位与坏值保留（§10.2.1/§10.2.2）；身份由服务端派生（§4.3 规则 8）；动作副本过滤 + `confirmToken`（§7.3/§8.4.1）；补 §16.8 六项 Watson 补测；矩阵同步到 v1.1（232 条） |
| 2026-10-21 | v1.3 | **M0.1 复评整改（契约/矩阵冲突清零）**：①B1 契约按代码定稿——`IScadaRunHub` 六成员（事件带 generation）、`RunStarted` 在 `Show()` 之后、`RunStopped` 在 `_session/_window` 清空之后（处理器内为 null）、新增 `TryGetCurrent` 给晚订阅者（§4.1/§17.1）；②`/img` 改**独立 `imageToken`**（按会话、30–60s、可轮换、只放 query），URL 不出现会话令牌（§6.1/§8.9/§8.10）；③`AllOperators` 新增五项监管闸门配置（`RequireOnlineUsers`/`RequireAudit` 固定 true/`RequireRateLimit`/`RequireConfirmForDangerous`/`ShowOnlineUsers`），缺项拒绝启用；速率改双层（`CommandRatePerSecond=5` + `ElementRatePerSecond=2`）（§8.4）；④容量/阈值统一：50 客户端 p95 ≤ 300ms、100 客户端 p95 ≤ 500ms（§12.6/§13.1）；⑤`pageVersion` 改 `(pageId, pageVersion)` 联合判定，跨页只比 `pageId`、回退按跳变（§9.6）；⑥presence 只给不透明 `sessionRef`，审计前缀**唯一拼装点**改到 `ScadaAuditWriter.Append(string,…)`（§8.7/§9.3）；⑦M1 视觉闸门补齐 5 个派生量、`Text/Rectangle/Panel` 只比属性袋、D21 进 M1、B35 去掉 `SelectorSwitch`、B32 归 M1.5（§10.2/§10.3/§10.4）；⑧§5.4 配置按代码改 `BindAddress`/`Port=8080` 并标注"字段落点"；⑨§15 与实现对齐（新增 `IScadaAuditSink`/`IScadaUserAuthenticator`/`ScadaRunHubState`/loader/RH/UC 断言；`ScadaAuditWriter` 已改、`ScadaChecks.csproj` 去掉直接引用）；⑩§17.1 四条 blocking 全部改为"部分关闭 / 整改中"；矩阵同步到 v1.2（247 条，P0 177 / P1 65 / P2 5），新增 B37–B40、C61–C67、E29、X04/X05、O04；新建结果台账 |
| 2026-10-21 | v1.4 | **M0.2 收口与 8 处口径冲突裁定**：①UI 停顿拆成两条——**10Hz×1000 图元帧循环的 UI 线程占用 ≤50ms/拍**（矩阵 M1）与**单条命令端到端 ≤1000ms**（矩阵 M1b），§11.2 的 20ms 降为告警线（§11.2/§12.2/§12.6）；②24h 内存统一"**私有内存增长 ≤100MB、RSS 仅记录不设门槛**"（§12.6，矩阵 M6/E09）；③C61 审计计数写死为"①100 条命令级（2 Success + 98 Denied）+ 2 条动作级；②100 条命令级 + 100 条动作级"；④`ElementRatePerSecond` 明确**同一 `elementId` 跨会话共享**、`CommandRatePerSecond` 按会话（§5.4/§8.4）；⑤`imageToken` 每 `TTL/3` 轮换、旧 token 保留到 `expiresAt`、**同时最多 3 个**（§5.4/§6.1）；⑥B38 升 **P0**，M1 门槛为 B33–B40 全绿（§13.1）；⑦`RequireAudit=false` **加载即夹回 `true` 并写 Error**（§5.4/§8.4）；⑧基座页 `Version` 变化也触发 **300ms 防抖 + 1 帧（10Hz 下 ≤100ms）** 重建所有引用页（§4.4）。另：`TryGetCurrent` 与 `_writeGate` 的落地状态同步（§4.1/§8.7/§15/附录 A）；§5.4 补 `AllOperatorsRequiresLease`；§15 补 M0.2 新增/改动（`AbortStart`、`_writeGate`、`ResetPassword` CAS、`TryValidate` 复核、`_registered/_initialized`、`Prism*` 拒绝、pack 3c、RH10–18/ML/UC16–18c）；新增 §16.9（M0.2 2242/0）；§17.1 标 `B40`/`A01–A09`/`F23`/`F24` 为 M1 首周；矩阵同步到 **v1.3（247 条，P0 178 / P1 64 / P2 5）** |
| 2026-10-21 | v1.5 | **M1b 协议/范围/证据链对齐（M1c 整改中）**：①§9 全量改为 as-built——`type` + `hello/hello.ok/snapshot/page/frame/stopped/idle/pong/error/bye`，`snapshot.page` PascalCase、`frame.items` camelCase，**删除 `kind/state/caps/view.attach/auth.login/view.resync`**，M2 扩展点单独标注；②M1 范围收窄为 10 图元/10Hz/只读/10 客户端，1000 图元与 50 客户端归 M3/E（B20/B26 改写）；③动画/坏值角标/`/img` 归 M1.5/M2（§10.2.2/§10.6/B35/B37）；④重连改 `sessionToken`（8h）+ `refreshToken` 换票（§8.9/§9.2）；⑤基座 `BasePageVersion` 进 `PageKey`（§4.4/§9.6/B39，M1c 落地）；⑥帧 `text` 用 `DisplayFormat`（M1c），B33 拆已实现/未实现（新增 B33b）；⑦登录入口=无 ticket 登录卡片（§6.2/B02/B05）；⑧新建 `已知偏差.md`（9 条）；⑨台账补 M1b 行、新建 M1b 记录；⑩§13.1 拆 M1b/M1c/M1，§17.5 列五条 blocking；指标编号 `M1/M1b` → `P-UI/P-CMD` |
| 2026-10-21 | v1.6 | **M1c 收尾（M1 只读核心 PASS、验收矩阵未全跑）**：①`sessionToken`（8h、不轮换）与 `POST /api/ticket {refreshToken}` 换票落地（§8.9/§9.3），旧票复用 401；②无 ticket 登录卡片 + `sessionStorage` + 断线换票重连 + 401 回登录；③快照信封 `basePageVersion`（无基座为 `0`）与 `PageKey = pageId@pageVersion@basePageVersion`（§4.4/§9.4/§9.6，B39 主体落地）；④帧 `text` 用 `DisplayFormat`（`IFormattable` + InvariantCulture，§4.5/§9.5）；⑤`/elements-process.js` 与 `.css` 及带 `?v=` 的静态路由均 200；⑥证据：`Web\ScadaWebChecks` **33/33**（`_m1c_webchecks33b.log` `C75F4FE9AF80`，DLL `71B953980241`）、`_WebProbe6` **31/31**（无仓内日志）、`ScadaChecks` **2276/0**（`_m1c_sc.log` `CCB425F345BD`，DLL `54A1F9527A99`）；⑦§13.1 M1c 改"已关闭"、M1 改"只读核心 PASS / 矩阵未全跑"，新增 §16.11，§17.5 五条全部关闭；⑧已知偏差 9 → 11 条（登录卡片与演示标记、演示页棒图配色）；⑨**矩阵 248 条仍未执行**，B 阶段仅部分覆盖 5 条，**M2 可开工但 M1 全量矩阵待补** |
| 2026-10-21 | v1.7 | **M2a 设计定稿（代码已开始落地、未接线/未验证）**：①§8 权限/监管按 as-built 重写——权限层四道（`AllowCommands` → 会话只读/未登录 → 角色 → `ClickPolicy`）、`AllowCommands` 默认 `false` 总闸、`caps` 逐图元 `canOperate/reason`、`HighestRoleOnly` 平局都可点/无人登录全员只读、`AllOperators` 五项闸门 + `AllOperatorsRequiresLease` 可选加强；②§8.12 新增**安全前置六项**（默认密码 gate `E_DEFAULT_PASSWORD` / Origin / `TicketRatePerMinute=10` / 匿名令牌不续期 `E_SESSION_EXPIRED` / `RevokeSession`·`RevokeUser` → `E_SESSION_REVOKED` / `MaxSessionTokens=4096` → `E_CAPACITY`）；③§9 协议：四条命令（`cmd.element`/`cmd.input`/`cmd.alarm.ack`/`cmd.confirm.request`）、`caps`/`ack`/`presence`/`toast`、`E_NOT_FOUND` 取代 `E_UNKNOWN_ELEMENT`、错误码表按层重写、新增 §9.9 时序（登录 → caps → 命令 → ack/error）；④§4.3/§11.4 命令泵：每会话 32 → 全局 1024 → 线程池消费者（无 SyncContext）→ UI 逐条、单条异常隔离 `E_INTERNAL`、两级 `E_BUSY`；**as-built 不做幂等去重（M2a-4）**；⑤§10.4 六图元补只读/禁用/失败反馈；⑥§12.4 断言 W2-19~W2-31、§13.1 拆出 M2a 行、§15 补 8 个已落地类、§17.6 五条 blocking（宿主未接线/安全未验证/三闸可省略/无幂等/矩阵未跑）；⑦矩阵 C 阶段 **67 → 80 条**（新增 C68–C80），全仓 **248 → 261 条**（P0 191 / P1 65 / P2 5）；⑧**M2a 不计通过，`AllowCommands` 保持 `false`** |
