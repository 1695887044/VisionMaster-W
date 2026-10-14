# 2026-10-11 通信模块四项修复（JSON 逐条隔离 · 拒绝识别 · KeepAlive 落地 · Worker 日志）+ 根目录死代码清理

> **背景**：用 DeviceSimulator 靶子（10 Modbus TCP + 10 S7，全部 127.0.0.1）压测 VM 通信代码，确认了 6 个修复点。
> 经用户裁决，本轮修 **1 / 3 / 4 / 5** 四项；**6**（`ReadBytes` 单位语义）与 **2**（模拟器字节序口径——10 端口实测全部 CDAB，与宣称不符）本轮不动、维持开放（§6-⑤）。
> 同轮由主会话执行了仓库根目录死代码清理（12 个 .cs 死副本 + 5 个 obj 生成残留，§2.7）。

---

## 1. 起因与两个关键发现

### 1.1 修复点从哪来

本轮压测延续 `docs\code-changes\2026-09-24-通信库压测与三处缺陷修复（Headless状态·断线短路·构造器顺序）.md` 的路子——自建靶子、真连真断真收发。四项修复与修复点的对应：

| 修复点 | 名称 | 一句话 |
|---|---|---|
| 1 | JSON 配置逐条隔离 | communications.json 里一条坏点，不该把整份配置全废 |
| 3 | 连接拒绝（refused）识别 + 退避加速 | 端口没监听要立刻说清楚、快速重试，而不是陪等超时 + 指数退避 |
| 4 | KeepAlive 落地 | 配置界面上的「开启 KeepAlive / 心跳间隔」此前是个死配置（§1.3） |
| 5 | Worker 吞异常补日志 | Worker 主循环 / 命令执行两处 catch 什么都不打，故障无从排查 |

### 1.2 发现一：回环拒绝 ~2 秒是 OS 层底线，不是 HSL 重试

上轮压测复现出「连一个没有监听的端口，`Manager.Connect` 也要陪等 ~2 秒（实测 2026ms）」，当时归因到"HSL 内部重试"。本轮**裸 socket 实测推翻**：

- 连 `127.0.0.1:1`（无监听）也是 **~2030ms**；连活端口 0-3ms；
- 即：**本机 OS / TUN 类代理层把回环 RST 延迟了 ~2 秒**，socket 层这 2 秒是 OS 底线，HSL 在其中没有重试。

推论：**refused 路径的耗时不可能压进毫秒级**（自己探测一次也要 ~2s）。于是分类策略定为两级——

> **消息文本识别为主（零开销）+ 快速失败探测为辅（仅当本次失败耗时 < min(TimeoutMs-100, 1500) 时才做）**；
> **超时路径永不进探测**（否则探测会把 2 秒变成 4 秒）。

### 1.3 发现二：KeepAlive 的契约默认值本来就是开的

`Core\CommunicationContracts\EthernetConfigBase.cs:36,43` 的契约默认值一直是 `EnableKeepAlive=true / KeepAliveIntervalMs=30000`（UI 默认勾选）——只是此前没有任何代码把它下发到设备，是个**死配置**。

本轮落地后：**默认配置即启用 30s TCP keepalive**，存量用户可感知；显式 `false` 零变化。HSL 落点：`NetworkDoubleBase.ConnectServer()` 成功那一刻对底层 socket 做 `SetKeepAlive`（IOControl，毫秒单位），断线重连时自动重应用（`DLL\HslCommunication\Core\Net\NetworkDoubleBase.cs:414,803`）。

---

## 2. 改动清单

### 2.1 公共契约（只加不改）

| 文件 | 改动 |
|---|---|
| `Core\CommunicationContracts\ICommunicationConnection.cs:73-87` | **新增**可选能力接口 `IConnectionFailureDetail`（`IsLastConnectFailureRefused` / `LastConnectError`），比照 `ILinkHealthProbe` 的可选能力模式（该范式出处即 2026-09-24 压测记录 §1.4(c)）——不具备该能力的实现（串口）不实现本接口，上层按「分类未知」处理，行为与旧版完全一致 |

### 2.2 修复 1：JSON 配置逐条隔离

全部在 `Communication\Communications\Manager\AdvancedCommunicationManager.cs`：

| 位置 | 内容 |
|---|---|
| `:1009` 起（`ApplyConfigJson`，实现区间含注释块约 994-1030） | 重写：`JToken.Parse` → **逐条** `ToObject`（`JsonSerializer.Create(_configJsonSettings)`，**保持 $type 白名单 binder 与 TypeNameHandling.Auto 同源**）。单条坏点只跳过该条；文件级语法错误 → ERROR + 0 条 + **不抛**（唯一生产调用方 `CommunicationModule.cs:30` 的 `LoadConfig` 本就按「配置损坏不阻断启动」兜底） |
| `:1078`（`DescribeConfigLabel`，调用点 `:1057`） | 错误日志带**条目序号 + 连接名**（坏点定位到条） |
| `:974`（`LoadConfig`） | 不再 rethrow（内容级错误尽力而为加载；能到这里的只剩文件读取类异常，`:985` 调用点、`:989` 注释说明分层） |
| `:423`（`Manager.Connect` 失败日志） | 失败日志带上 `worker.LastError`（refused 场景即 `[ConnectionRefused] …`） |

### 2.3 修复 2：连接拒绝（refused）识别 + 退避加速

| 文件 | 位置 | 内容 |
|---|---|---|
| `Communication\Communications\Core\HslHelper.cs` | `:274`（`ProbeConnectionRefused`）/ `:303`（`MessageIndicatesConnectionRefused`） | 两级判定的公共件（§1.2 策略：消息识别主通道、探测只在耗时够短时做） |
| `Communication\Communications\Connection\ModbusTcpConnection.cs` | `:12`（类声明实现 `IConnectionFailureDetail`）、`:100`（Connect 失败分类调用）、`:120`（`ClassifyConnectFailure`，实现区间 87-130） | 两级判定；**超时路径永不进探测** |
| `Communication\Communications\Connection\SiemensS7Connection.cs` | `:11`、`:87`、`:103`（区间 74-113） | 同上（与 Modbus 同一套规则） |
| `Communication\Communications\Manager\ConnectionWorker.cs` | `:75`（`_connectFailureSerial` 字段）/ `:115`（refused 间隔常量）/ `:415-433`（失败分类落 LastError 与 `[ConnectionRefused]` 前缀，实测锚点 `:419-425`）/ `:478-483`（重连等待的 refused 判定）/ `:530-543`（`WaitStateAsync` 的 refused 快速失败，签名 `:527`）/ `:73`、`:439`（唤醒机制） | refused **固定 1s 重试、不走指数退避**；`WaitStateAsync` 等待期间发生 refused **立即返回 false**（`Manager.Connect` 不再陪等满 TimeoutMs）；**重连意图不变**（只是节奏变了） |

### 2.4 修复 3：KeepAlive 落地

| 文件 | 位置 | 内容 |
|---|---|---|
| `Communication\Communications\Connection\ModbusTcpConnection.cs` | `:80-83` | 构造器下发 `_device.SocketKeepAliveTime`（**`EnableKeepAlive && IntervalMs>0` 才设**——HSL 默认 -1=不启用，未启用时零行为变化） |
| `Communication\Communications\Connection\SiemensS7Connection.cs` | `:67-70` | 同一机制，与 Modbus 对称 |

### 2.5 修复 4：Worker 吞异常补日志

全部在 `Communication\Communications\Manager\ConnectionWorker.cs`：

| 位置 | 内容 |
|---|---|
| `:344-352`（实测锚点 `:350`） | 主循环兜底 catch 补限频 ERROR（此前主循环异常完全静默） |
| `:492-501`（实测锚点 `:499`） | `ExecuteItem` 同上 |
| `:565-596`（`:588` 定义） | 限频设施 `LogErrorThrottled`：**同 key 5 秒一条**，日志走 LogSink + Console 双通道 |
| `LogErrorThrottled` 体（**审查后追加**） | **try/catch 兜底**——它在 catch 块内被调，若日志订阅者 / Console 自己抛异常，会从 catch 逃逸**杀死 Worker 线程**（reviewer 抓出的本轮新引入残余风险；修后 CommChecks 复跑 174/0） |

### 2.6 测试（CommChecks / CommPerf）

| 工程 | 用例 | 内容 |
|---|---|---|
| `CommChecks\Program.cs` | **C16**（`:1625` 起，5 条） | KeepAlive 下发：反射读 `SocketKeepAliveTime`——契约默认值（true/30000）已下发、显式 false 保持 HSL 默认 -1、自定义间隔 5000 如实下发（防「恰好等于默认值的假通过」）、S7 对称两条 |
| `CommChecks\Program.cs` | **C18**（`:1683` 起，8 条） | JSON 逐条隔离，**走真实 `LoadConfig`**：截断坏 JSON 不抛 0 条；$type 越权条在前 / 在后都只跳坏条（顺序无关）；未实现协议条仍按 AddConnection 阶段隔离（旧语义保持）；缺字段兜底默认值不算坏点；顶层非数组、空数组 |
| `CommChecks\Program.cs` | **C17**（`:1746` 起，8 条） | refused 分类 + 退避：真连 `127.0.0.1:1`（Modbus/S7 对称）+ `AlwaysRefusedConnection` 桩（`:1836`）度量退避节奏——**1.3s 内 6 次尝试**（间隔 200ms；旧指数退避 base 1s 只有 1-2 次）；`Manager.Connect` refused 即返回 false；`LastError` 带 `[ConnectionRefused]` 标记；refused 失败耗时 < TimeoutMs（分类不额外加探测耗时） |
| `CommPerf\Program.cs` | fix 阶段 **F1**（`:803`）/ **F2**（`:826`） | F1：本机 TcpListener 靶子上**运行时读回 `SO_KEEPALIVE`**（不只看配置）；F2：refused 三连测（Modbus / S7 / Manager）；另加 `[J-$type越权+好条]` JSON 用例 |

### 2.7 死代码清理（主会话执行）

删除仓库根目录 **12 个字节级相同的 .cs 死副本**（正本在 `Communication\` 树与 `CommTest\`，均不参与任何编译）：

`ModbusTcpConnection` / `SiemensS7Connection` / `SerialConnection` / `HslHelper` / `PollAddressResolver` / `ModbusAddress` / `S7Address` / `ConnectionFactory` / `ConnectionWorker` / `AdvancedCommunicationManager` / `CommunicationModule` / `Program`

再删 **5 个 obj 生成残留**：`CommChecks.AssemblyInfo.cs`、`CommChecks.GlobalUsings.g.cs`、`CommStress.AssemblyInfo.cs`、`CommStress.GlobalUsings.g.cs`、`.NETCoreApp,Version=v9.0.AssemblyAttributes.cs`。

清理后根目录仅剩未验证的 `demo.cs`（本轮未动）。已回读确认：`dir /b D:\C#\VM\*.cs` 只剩 `demo.cs`。

---

## 3. 有意变更的旧行为（单列）

1. **JSON 单条坏点只跳过该条**；文件级语法错误不抛（旧行为：整份 0 条 + 抛异常）。
2. **`LoadConfig` 内容级错误不再向上抛**（尽力而为加载）。
3. 加载汇总日志改「成功 X 条，跳过 Y 条」**真实口径**。
4. **默认配置即启用 30s TCP keepalive**（契约默认值本就是 true，本轮起真正生效——§1.3）。
5. **幽灵端口 `Manager.Connect` refused 后立即返回 false**（不再陪等满 TimeoutMs）。
6. refused 在 `LastError` / 日志带 `[ConnectionRefused]` 前缀；重试退避固定 1s（不走指数退避）。
7. Worker 两处吞异常点补有限频 ERROR。

---

## 4. 验证证据

| 项 | 结果 |
|---|---|
| 构建 | `VM.Communication` / `CommChecks` / `CommPerf` 全部 **0 错误** |
| `CommChecks` 全量 | **通过 174 / 失败 0**（审查者独立重建复现同数；审查后主会话追加 `LogErrorThrottled` 兜底修补，**再次复跑 174/0**） |
| `CommPerf` fix 阶段 | **F1**：[开启] `SO_KEEPALIVE=True` / [关闭] `False`（运行时读回）；**F2**：Modbus / S7 / Manager 三连测 `Refused=True`（耗时 2032 / 2016 / 2274ms——与 §1.2 的 OS 层 ~2s 底线吻合，分类本身零额外开销） |
| `CommPerf` json 用例 | `[J-$type越权+好条]` 加载 1 条（好条存活）；`[J-截断坏JSON]` 0 条不抛；其余 6 例符合口径 |

**审查者红线核查**：`Plugins\` 构建文件未动；`Modules\` 无契约 / 通信 DLL；产物时间戳新于源文件。

---

## 5. 审查结论与过程教训

reviewer 结论 **pass**。过程中 reviewer 抓出两件事，均当场处理：

1. **实现者自测产物陈旧窗口一次**：某轮自测跑在旧 DLL 上（产物时间戳早于源文件），重建后消除——「`已成功生成` ≠ 产物刷新，判据是产物时间戳」（与红线③、`docs\code-changes\2026-10-10-变量赋值写全局变量（名字行候选与文本值按类型解析）.md` §3.1 的证据卫生教训同一族）。
2. **`LogErrorThrottled` 本身会抛的残余风险**（§2.5 末条）：日志设施在 catch 块内被调，它自己必须永不抛——修补后 CommChecks 复跑 174/0。

---

## 6. 遗留 / 未决

1. **`ImportConfigAsync`**（`AdvancedCommunicationManager.cs:1120`）仍是整份反序列化（UI 导入路径，异常即反馈给用户，暂可接受），留待后续拉齐 `ApplyConfigJson` 的逐条隔离口径。
2. **refused 消息识别只覆盖 zh-CN / en**，其他区域回退探测分支——「OS 延迟 RST（~2s）+ 非中英文区域」的组合无法分类，按旧行为处理。
3. **真实 PLC / 拔网线的半开长时 keepalive 未实测**（本机靶子只能证明 socket 参数已生效，即 F1 口径）。
4. **VisionMaster 宿主正被并发会话重构**，并发会话落定后建议重跑一次 `CommChecks` 确认 174/0。
5. 修复点 **6**（`ReadBytes` 单位语义）与 **2**（模拟器字节序口径——10 端口实测全部 CDAB，与宣称不符）仍开放。
