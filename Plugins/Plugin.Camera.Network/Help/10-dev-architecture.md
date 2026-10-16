# 网络相机（虚拟）· 开发：架构、宿主契约与不变量

> 分类：相机 ｜ 来源：插件 Plugin.Camera.Network ｜ 读者：开发

S 档一页承担三个角色：架构与数据流、白拿的基类/宿主行为、改这两个文件时的红线。
用户页讲的"怎么填、怎么接客户端"这里不重复，只讲**行为由谁提供、为什么这么写、写错会怎样**。

## 一、分层位置：本插件是全仓最薄的一层驱动

| 层 | 位置 | 职责 |
| --- | --- | --- |
| 契约层 | `Shard\Core.Interfaces\Camera\`（`ICameraDevice` + `CameraDeviceBase` + `CameraDescriptor` / `CameraSettings` / `CameraFrame` / `CameraTypes`） | 环形队列 + 溢出计数、状态机、心跳看门狗、取图口、身份回填——**全部固化在这里** |
| 驱动层 | 本工程 `NetworkCameraDevice.cs`（109 行） | 只写差异点：八个钩子，**没有一个方法体有代码**（全是表达式体或空体） |
| 宿主层 | `VisionMaster\Services\Camera\NetworkCameraServer.cs`（Watson Webserver）+ `Services\ImageBytesCodec.cs` + `Modules\CameraModule.cs` / `ICameraProvider` | 监听、鉴权、按序列号寻址、解码图片字节、调 `PushFrame` / `NotifyAlive` |
| 消费层 | `Plugins\Plugin.ImageAcquisition`（相机采集模式） | `WaitNextFrame` 取帧、原始像素 → `HImage` |

**csproj 的两条刻意为之**：`TargetFrameworks = net9.0`（**不带 `-windows`**），且只 `ProjectReference` 契约层。
驱动层不碰任何 Windows 专有 API、不引用 WPF、不引用 `halcondotnet`——帧全程以 `byte[]` 流转，
建 `HImage` 留给消费者。收益有二：采集链路将来能整体搬到 Linux 工控机（只重编这个工程），
以及"换一家相机就要和宿主对齐同一个 HALCON 版本"这件事不会发生。

## 二、八个差异点钩子：每个都对应一条"写反了会怎样"

| 钩子 | 本驱动 | 为什么必须是这个值 |
| --- | --- | --- |
| `RequiresHeartbeat` | `true` | HTTP 无连接、没有掉线通知，只能靠"多久没证据"反推。沿用基类默认 `false` 则**看门狗根本不创建**：客户端被杀后界面一直显示 Online/Streaming——现实里相机已经没了，界面却报正常，这是最容易被误信的假象 |
| `StateAfterOpen` | `Connecting` | 真机默认 `Online`（"已连接、未采流"）。网络相机的"打开"只是开始接受该序列号的帧，客户端可能还没接进来。直接标 `Online` 会让界面分不清"没接进来"与"接进来但没送帧" |
| `OpenSuccessDetail` | `"等待客户端接入（序列号 X）"` | 客户端按 URL 里的 `{serial}` 寻址，把序列号打在状态说明上，接入失败时第一眼就能核对最常见的错因 |
| `OpenCore` | `=> true` | 基类据此推进状态并启动看门狗。返回 `false` 会让"等待客户端接入"这条链路根本不开始，明明接得进来却报打不开 |
| `CloseCore` | `{ }`（空） | 基类在 `Close()` **和** `Dispose()` 两条路径都调它。这里抛异常会被记成"关闭设备时异常（已忽略）"，把一次正常断开渲染成故障 |
| `ApplySettingsCore` | `=> true` | 曝光/增益/触发无处可下。返回 `false` 会被基类当"应用参数失败"，用户每改一次曝光就报一次错，而配置其实已经存好了 |
| `StartStreamCore` | `=> true` | 基类在调本钩子**之前**已把状态推到 `Streaming` 并打开收帧闸门；这里再把状态推回去就是界面骗人 |
| `StopStreamCore` | `{ }` | 闸门与清队列由基类落地。**切勿在这里去关宿主的收图服务**——那是全局资源，会影响其他相机与其他流程 |

**本驱动没有任何实例字段、静态字段、缓存、后台线程**：全部可变状态都在基类里，
所以它可以安全放进并行分组（同一序列号的取图仍是"每帧只被取一次"）。

## 三、宿主数据流（一帧进来只发生四件事）

```
POST /camera/{serial}/frame   Authorization: Bearer {Token}   body = 图片文件字节
  → Watson 鉴权委托（Bearer 后取值，与配置 Token 逐字 Ordinal 比较）→ 不过则 401
  → TryResolveCamera：Uri.UnescapeDataString(parameters["serial"]).Trim()
        空 → 400；ICameraProvider.TryGetDeviceBySerial 落空 → 404（message 里列出"当前已配置：…"）
  → DataAsBytes 空 → 400；ImageBytesCodec.Decode 抛 → 400（并记一条 Warn）
  → new CameraFrame { PixelData, Width, Height, Channels, Timestamp = DateTime.Now,
                      SourceName = 头 X-Image-Name（可空）}
  → device.PushFrame(frame, out error) → 假则 409（error 原文就是 message）；真则回 200
```

| 归属 | 谁做 | 关键口径 |
| --- | --- | --- |
| 图片字节 → 原始像素 | 宿主 `ImageBytesCodec` | **规则只写这一处**：灰度保持单通道，彩色一律 BGR24 交错。客户端不必关心行填充与字节序 |
| 帧身份与帧号 | 基类 `PushFrame` | 回填 `CameraId` / `SerialNo` / `FrameId`（`Interlocked` 自增）；`Timestamp` 为 `default` 才补 `Now`——服务端已显式填了 `Now`，所以它表达"主软件收到的时刻" |
| 合法性校验 | 基类 `PushFrame`（`frame.IsValid`） | 尺寸与字节数不自洽的帧必须在**入口**拦下：放进队列后只能在消费者侧以 `GenImage` 抛异常炸出来，那时已看不出是哪一帧 |
| 入队 / 溢出 / 唤醒 | 基类 | 队列满丢最旧并计溢出；用 `SemaphoreSlim`（容量 1）事件驱动唤醒，没人等时 `Release` 抛 `SemaphoreFullException` 被吞——那正是"无需唤醒" |
| 回包时机 | 服务端 | **不等流程**。解出像素、入队、立刻回包，所以这里没有 `HttpImageServer` 那套会话获取 / 超时 / 结果收集逻辑 |

## 四、两条 HTTP 服务是两套语义，不许互相复用

| | `NetworkCameraServer`（本驱动对接） | `HttpImageServer`（网络推送采集） |
| --- | --- | --- |
| 默认端口 | `19100`（源码注释写明"刻意与 HttpImageServer 的 19000 错开，避免两个服务抢同一端口"） | `19000` |
| 路径 | `/camera/{serial}/frame`、`/camera/{serial}/heartbeat` | `/flow/{流程名}` |
| 图像归属 | **环形队列**，消费语义（`WaitNextFrame` 出队），有溢出/丢弃计数 | **只留最新一帧**的图像槽，一请求一帧一次执行，同步回结果 |
| 并发 | 多台相机共享一个监听，按序列号分流 | 同一流程同时只允许一次执行，并发推 → 409 |
| 存活判定 | 心跳 + 帧都算证据（见第五节） | 无此概念（请求即执行） |

写手册或改代码时最容易混的就是这两条：端口、路径、回包结构都不同，`Fail(req, code, message)` 的形状也不同。

## 五、状态与时钟：谁在刷新"还活着"

| 事件 | 基类动作 | 对本驱动的表现 |
| --- | --- | --- |
| `Open()` 成功 | 刷 `_lastAliveUtcTicks`、`_acceptFrames = true`、状态落 `StateAfterOpen`、`StartWatchdogLocked` | `Connecting` + "等待客户端接入（序列号 X）" |
| 收到心跳 → `NotifyAlive()` | 刷时钟；**只有 `Connecting` 才往前推**：`_streaming` 为真 → `Streaming`（"设备已恢复，采流中"），否则 `Online`（"设备已连接"） | 掉线恢复无需人工 |
| 收到帧 → `PushFrame()` 成功 | 刷时钟 + 入队 + **无条件**把状态写成 `Streaming`（说明"采流中（已收 N 帧）"） | 所以"只推图、不发心跳"也能进并保持 `Streaming`；一旦停止推图就完全靠心跳判掉线 |
| 看门狗到点 | `interval = Clamp(HeartbeatTimeoutMs / 3, 200, 1000)`；`Closed` 与 `Connecting` **不报**（已在等待态不重复报）；超时 → `HandleLost("已 X.Xs 未收到心跳")` | 状态回 `Connecting` + 清空队列 + 计入丢弃 + 一条 Warn |
| `StopStream()` | 关闸门、状态回 `Online`、`StopStreamCore`、清队列（原因写"停止采流"） | 之后送来的帧一律 409"相机已停止采流，帧被拒绝" |
| `Close()` / `Dispose()` | 先改状态与闸门 → 再 `CloseCore` → 再清队列 | 顺序刻意：反过来会在关闭期间再吃进一帧 |

**掉线时为什么必须清空队列**：积压的旧帧在恢复后立刻被取走，会让流程把"几秒前的旧图"当成
"当前工件"检测——比明确失败危险得多。丢弃量记 `DroppedFrameCount`，与溢区分开，便于现场分辨根因。

## 六、日志去抖（两处，形状不同但理由相同）

| 位置 | 机制 | 反例后果 |
| --- | --- | --- |
| 服务端 `_lastRejectReason`（按序列号，`OrdinalIgnoreCase`） | 同一拒绝原因连续出现只在**原因变化**时记一条；推帧成功或收到心跳即清除 | 客户端 33 帧/秒持续被拒 → 一秒刷满日志，真正的第一条被淹没 |
| 基类 `_overflowWarned` | 溢出**首次**告警一次，之后只计数；`ResetCounters()` 重新武装 | 同上；且逐帧告警会让人忽略"溢出"这件事本身 |

## 七、A 组不变量（违反就编译 / 闸门红）

| 症状 | 机理 | 守门断言名 |
| --- | --- | --- |
| 工具箱/相机设置里选不到这台驱动，且无任何报错 | 类上缺 `[Display]` 或 `Name` 不等于「网络相机（虚拟）」——宿主扫描 `Modules\` 时静默跳过无 `[Display]` 的类型 | 无针对本驱动的断言（同族判据只有海康侧的 `驱动带 [Display]（没有它 PluginService 会静默跳过，相机设置里选不到）`），靠人工核对 |
| 宿主反射构造不出设备 | 必须有**接受单个 `CameraDescriptor` 的公开构造函数** | 同上（海康侧对应 `实现 ICameraDevice 的非抽象类型存在`、`按 (CameraDescriptor) 构造成功，身份正确透传`） |
| 驱动 DLL 根本没被发现 | 工程不在 `Plugins\` 下 / 没重建插件工程，`Modules\` 里没有 `Plugin.Camera.Network.dll` | 无断言，靠人工核对（海康侧有 `驱动 DLL 在 Modules 目录里`） |
| 手册被"自动生成页"顶掉、页序乱、开发章节混进用户册 | `Help\` 整目录未挂进 `EmbeddedResource`；文件名缺 `NN-`；开发页没用 `NN-dev-` 或另立子目录 | `HM4 有 Help/*.md 的工程都带 EmbeddedResource（手册进得了 DLL）`、`HM6 手册文件名是 NN-xxx.md（页序确定）`、`HM5 每页第一行内容就是 # 一级标题（页名取得到）`、`HM12 开发页文件名是 NN-dev-xxx.md（宿主认得出这是开发章节）`、`HM12b 手册最多一层目录（开发页另立子目录会挤掉用户页）`（`FlowCanvasChecks\HelpManualCheck.cs`） |
| 手册编码坏 / 成占位页 | BOM 或非法 UTF-8；正文太短；出现占位标记 | `HM7 手册都是无 BOM 的合法 UTF-8 且含中文（编码纪律）`、`HM8 每个插件的手册正文 ≥ 70 行（不是占位页）`、`HM9`（扫描手册正文里的占位词；那条断言的原文案本身含禁用词，此处不复述） |
| 采集步骤取不到帧号与来源 | 输出端口被改名（消费侧，不在本插件） | `输出端口含 FrameId / ReceivedCount（相机模式精确帧号，不靠语义重载）`、`输出端口含 SourceSerial（多相机身份；相机模式=配置序列号，其余模式为空）`（`FlowCanvasChecks\ImageAcquisitionChecks.cs`） |

## 八、B 组不变量（不报错，只出诡异现象）

| 症状 | 机理 | 守门断言名 |
| --- | --- | --- |
| 客户端被杀后界面仍显示"采流中" | `RequiresHeartbeat` 被改回 `false` → 看门狗不创建 | 无断言，靠人工核对 |
| "没接进来"与"接进来没送帧"分不清 | `StateAfterOpen` 改回默认 `Online` | 无断言，靠人工核对 |
| 明明能接入却报"打不开" | `OpenCore` 被写成返回 `false`（"没句柄可开，那就失败"的直觉） | 无断言，靠人工核对 |
| 每次改曝光都弹一次错 | `ApplySettingsCore` 返回 `false`。注意：`FrameTimeoutMs` / `BufferCapacity` / `HeartbeatTimeoutMs` **确实生效**（基类消费），但它们也不需要"下发"，返回值仍应为 `true` | 无断言，靠人工核对 |
| 点一次停止采流，旧图还在队列里被下一轮取走 | `StopStreamCore` 里自己动了队列，或基类的 `DropAllQueued("停止采流")` 被跳过 | 无断言，靠人工核对 |
| 停一台相机导致别的相机收不到图 | 在 `CloseCore` / `StopStreamCore` 里去关宿主的收图服务（全局资源） | 无断言，靠人工核对 |
| 暂停推图 3 秒就报掉线 | 客户端把心跳和推图放在同一个循环里，暂停时心跳也停了。协议要求**每秒一次心跳独立发送** | 无断言，靠人工核对 |
| 掉线判定比 `HeartbeatTimeoutMs` 明显慢 | 检查频率被夹在 200~1000 ms，最坏情况一个整周期后才生效；填极大值也会放大这个滞后 | 无断言，靠人工核对 |
| 日志只剩一条拒绝原因，但现象持续 | 服务端按"原因变化"记日志——这是设计。要定位就去问客户端的返回码 | 无断言，靠人工核对 |
| 中文/特殊字符序列号连不上 | Watson 路由参数不做 URL 解码，服务端自己补了一次 `Uri.UnescapeDataString`；但**令牌比较是逐字 Ordinal、不 Trim 配置值**，两边写法必须完全一致 | 无断言，靠人工核对 |
| 彩色图红蓝互换 | 绕过 `ImageBytesCodec` 另写一份解码，或绕过基类唯一的"像素 → HImage"路径 | 无断言（通道约定由消费者侧的图像转换守住），靠人工核对 |
| 帧号与客户端日志对不上 | `FrameId` 由**设备**权威生成（入队时自增），客户端给的编号只是文本；`ResetCounters()` 不清累计收帧与帧号，重启进程才回 1 | 无断言，靠人工核对 |

## 九、改这两个文件时必须一起动的东西

| 改了什么 | 必须同步 |
| --- | --- |
| 任一钩子的返回值或落点状态 | 本插件 XML 注释（每条都写了"写反了会怎样"）+ `10-dev-architecture.md` 第二节 + 用户册《网络相机（虚拟）》的状态机段 |
| 服务端路由、状态码、回包字段 | `03-ports-and-parameters.md` 第五/六节 + `05-troubleshooting.md` 第二节的**逐字文案表** |
| 参数生效矩阵（新增一项基类消费的配置） | 用户册《端口与参数》第三节（"哪三项生效、哪三项不生效"是本驱动的核心口径） |
| 与 `HttpImageServer` 的边界（端口/路径变更） | 两本手册的"两条通道"表；`AppConfig.json` 的默认值；`Tools\VirtualCameraClient` 的默认填值 |
| 手册 | 该工程 `--no-incremental` 重建，并核对 DLL 里 `.Help.` 资源名数 == `Help\` 下 .md 数（增量构建会静默漏打包） |
