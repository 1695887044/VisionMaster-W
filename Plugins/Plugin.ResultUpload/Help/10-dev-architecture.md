# 结果上报 · 开发：架构、协议对接面与宿主契约

> 分类：数据处理 ｜ 来源：插件 Plugin.ResultUpload ｜ 读者：开发

M 档两页开发章节：本页讲**代码结构与协议面**（谁在什么线程上干什么、对端收到什么），
`11-dev-invariants` 讲**改这个插件时的红线**。用户页里的"怎么填"这里不重复。

## 一、文件与职责

| 文件 | 职责 |
| --- | --- |
| `ResultUploadPlugin.cs` | 插件本体：端口声明、`[StepConfig]` 配置、动态端口重建、取值/时机判定/组任务、`RunAlgorithm` |
| `HttpPayloadBuilder.cs` | `internal static` 组包器：四种预设 → JSON 文本。**纯内存、无 IO**，预览与真实发送共用 |
| `UploadQueue.cs` | `internal static` 全局上报中枢：有界队列 + 单消费线程 + HTTP + 退避重试 + 断网续传 spool + 日志暂存 |
| `UploadFieldDef.cs` | 四个枚举（`PayloadKind` / `SendTiming` / `FieldSource` / `BuiltInField`）与两个配置行类（`UploadFieldDef` / `HttpHeaderDef`），都继承 `ObservableObject` |
| `ResultUploadView.xaml(.cs)` | 自带配置视图（四张卡片），含 `EnumLabelConverter` / `EnumInToVisibilityConverter` / `InverseBoolConverter` / `LogLevelColorConverter` / `PasswordBoxHelper` |

零第三方依赖：`HttpClient` / `System.Text.Json` 都是 .NET 自带。

## 二、RunAlgorithm 时间线（`ResultUploadPlugin.cs`）

| # | 代码动作 | 为什么必须在这一刻做 |
| --- | --- | --- |
| 1 | 先把 `UploadQueue.TakeLogs()` 暂存的日志按级别转交 `context.Logger` | 静态 Hub 拿不到 `IExecutionContext`，只能靠"下一次执行本步骤"投递。放在最前面 = 本轮日志不会被本轮提前 return 吞掉 |
| 2 | `var now = DateTime.Now` 取一次，整轮共用 | 内置时间戳与 `{时间}` 必须同源，否则同一条报文里两个时间不一致 |
| 3 | `CollectValues(context, now, useTestPlaceholder: false)` 先组值池 | "仅NG"判定要看字段值，所以**值池必须先于时机判定** |
| 4 | 时机判定：`EachRun` 恒发；`OnTrigger` 读 `TriggerPort.GetActualValue() is bool b && b`；`OnlyNG` 走 `IsNg(values)` | `GetActualValue()` 是"读的这一刻"的生效值（连线值优先于手填值）。非 bool 值当作不触发 |
| 5 | 不满足时机 → **直接 `return`**，不写日志 | 静默通过。基类 `Execute()` 已预置 `Success=true`、`ErrorMessage=""`，所以本轮算成功 |
| 6 | `Url` 空白 → 按阻断契约处置（失败文案 `结果上报：未配置目标 URL`）或 WARN 后 return | 时机之后才校验 URL：不该发的件不该因为没配 URL 就报警 |
| 7 | `HttpPayloadBuilder.Build(...)` 组报文；`payload.Error != null` 按同一契约处置 | 组包纯内存，失败是"配置错"而不是"网络错"，文案前缀统一 `结果上报：` |
| 8 | 异步分支：`BuildJob(payload.Json)` + 挂 `job.OnCompleted` 回调 → `UploadQueue.Enqueue(job)`；入队非 Accepted 且勾了阻断才失败 | 入队是 `TryAdd(..., 0)`，微秒级；回调在**队列线程**执行 `ApplyPorts` |
| 9 | 同步分支：`UploadQueue.SendNow(job)` 阻塞到出结果 → `ApplyPorts`；未成功且勾了阻断则失败，未勾则 WARN | 退避 `Thread.Sleep` 就发生在这个调用栈上 = 占流程线程，这是同步模式的明示代价 |
| 10 | 末尾 `Success.Value = true; ErrorMessage.Value = "";` | 本插件不用基类 `Fail(...)`，直接写这两个端口（语义等价，改文案时两处都要看） |

## 三、协议对接面（对端看到什么）

- **对端**：任何接受 `POST` + JSON 体的 HTTP 服务。四类目标：MES 接口 / 钉钉群机器人 webhook / 企业微信群机器人 webhook / 自定义中转服务。
- **方法与体**：固定 `POST`；`StringContent(json, Encoding.UTF8, "application/json")`，
  所以 `Content-Type` 恒为 `application/json; charset=utf-8`——在请求头里写 `Content-Type` 改不动它。
- **报文构造**（`HttpPayloadBuilder.Build`，四种预设三条分支）：
  - `FieldTable`：`JsonObject`，键 = `UploadFieldDef.Name.Trim()`，值由 `ToJsonNode` 按 CLR 类型映射（数值/布尔保真、`DateTime` 走时间格式、未知类型 `ToString()` 占位、`null` 保留为 JSON `null`）；一个启用字段都没有 → `Error = 字段表没有启用的字段，至少要有一个字段才有东西可上报`。
  - `DingTalkText` / `WeComText`：`{"msgtype":"text","text":{"content":"<ExpandText 展开结果>"}}`，两者同构。
  - `RawJson`：模板文本展开后**原样发出**，合法性由用户负责；空模板 → `Error = 原始 JSON 模板为空`。
  - 任何抛出都收敛成 `Error = 组报文异常：<ex.Message>`；未知预设 → `Error = 未知报文预设：<成员名>`。
- **编码器**：`JavaScriptEncoder.UnsafeRelaxedJsonEscaping`（中文与非 ASCII 不转义）。
  它只是"不转义"，字符串内容照样合法转义；spool 落盘用同一个编码器，账本可读。
- **占位符**：两条正则分工——`\{#([^{}]+)\}`（原样注入，先处理）与 `\{([^{}#]+)\}`（文本注入 / 原始 JSON 里做字符串转义）。
  解析不出来的占位符**原样保留**，让"名写错"在报文里可见。
- **时间与 UTC**：`FormatTime` 是**唯一转换点**，入口的 `now` 与值池里的 `DateTime` 都过它；
  `useUtc` 时先 `ToUniversalTime()`，格式串为空回落到 `yyyy-MM-dd HH:mm:ss.fff`，`ToString(fmt, InvariantCulture)`。
- **鉴权**：请求头由 `BuildHeaders()` 生成（Key 空白跳过、同名首个生效、`OrdinalIgnoreCase`）；
  钉钉加签在 `SendWithRetry` 内部按**每次尝试**重算 `timestamp` + `sign`（`HMACSHA256` → Base64 → `Uri.EscapeDataString`），
  URL 里已有 `?` 就用 `&` 续。
- **幂等**：`X-Upload-Id` 在发送前注入，若用户自己填了同名头则不覆盖（`HasHeader` 大小写不敏感）。
- **重试与失败策略**：`maxAttempts = RetryCount + 1`；2xx 立刻成功返回并触发回调；
  非 2xx 记 `err = "HTTP <码> <状态名>"` 继续下一轮；`OperationCanceledException` → `code = 0`、`err = "超时（<ms>ms 无响应）"`；
  其它异常 → `code = 0`、`err = ex.Message`。循环穷尽 → WARN 日志 → `AllowSpool` 则落盘 → `OnCompleted(false, code, err)`。
- **TLS**：进程级常驻两个 `HttpClient`（普通 / 跳过证书），按每条 job 的标志二选一。
  `SslClientAuthenticationOptions.RemoteCertificateValidationCallback` 恒真是"跳过证书"那一支，
  `SocketsHttpHandler.PooledConnectionLifetime = 10 分钟`，`HttpClient.Timeout = InfiniteTimeSpan`（单次超时由每请求 CTS 控制）。

## 四、状态有几份、谁是真源

| 状态 | 真源 | 生命周期 |
| --- | --- | --- |
| 配置（19 个 `[StepConfig]` + 字段表 + 请求头） | `IStepConfigData.InputValues`（`.vms`） | 键 = 属性名；`Fields` / `Headers` 是集合，`OnConfirm` 走基类 JSON 往返**深拷贝**落盘 |
| 字段值（本轮） | 方法内的值池 `Dictionary<string, object>`（`OrdinalIgnoreCase`） | 一次调用，不跨轮 |
| 队列与线程 | `UploadQueue` 静态字段 | 程序集只加载一次 → **所有步骤实例共享一个队列、一条消费线程** |
| spool 账本 | 程序目录 `ResultUploadSpool\spool.jsonl` + 行数缓存 `_spoolCount` | 缓存不可信时置 `null` 重数 |
| 输出端口三个值 | `ApplyPorts` 写入 | 异步模式下是"最近一次"，可能来自别的件、别的轮 |
| 日志 | `UploadQueue.Logs`（转交队列，上限 500）+ `RecentLogBuffer`（回看账本，上限 50，环形） | 两份独立，互不影响 |

## 五、白拿的宿主/基类行为（作者不用写、也别重写）

| 行为 | 由谁提供 | 表现 |
| --- | --- | --- |
| 端口发现 | `VisionPluginBase.EnsurePortsDiscovered`（反射 public 属性） | `上报触发` / `响应` / `状态码` / `已上报` 就是声明出来的；`Success` / `ErrorMessage` 由基类给出 |
| 动态输入端口 | 基类 `AddDynamicInput` / `RemoveDynamicInput` | 本插件用 `RebuildDynamicInputs()` 按字段表增删 `InputPort<object>`；基类 `AddDynamicInput` 内部"已存在同名就不覆盖"，固定端口名天然优先 |
| 灌值顺序 | `ApplyConfigValues`（本插件 override：先 `base` 再 `RebuildDynamicInputs`） | 编译期必须先有端口，`LinkPorts` 才能把连线挂上——顺序写反 = 连线找不到端口 |
| 存盘 | 基类 `OnConfirm` + `SnapshotConfigValue` | 集合属性以 JSON 往返深拷贝写进 `InputValues`，确认后界面继续改不污染已存数据 |
| 默认成功语义 | 基类 `Execute()`：进 `RunAlgorithm` 前预置 `Success = true`、清空 `ErrorMessage` | 所以"静默通过"（时机不满足）算成功；未捕获异常会被自动转成步骤失败 |
| 自定义视图 | `IPluginCustomViewProvider.GetConfigView(stepData)` | 本插件返回 `ResultUploadView`，内部先 `Initialize(stepData)`；`StepData` 供视图里的 `LinkableValueEditor` 读写连线 |
| 并行安全 | `[ParallelSafe]` | `FlowCompiler` 按类型 FullName **字符串**匹配该特性（绕开"两份 `Core.Interfaces.dll`"的程序集身份问题）；真并行靠单消费线程 + `SpoolLock` 兜住 |

## 六、线程与所有权

- 流程线程：取值、组包、入队——**不碰网络、不 Sleep**。
- 队列线程 `ResultUpload.Worker`（`IsBackground = true`，`Interlocked.CompareExchange` 保证只起一次）：
  发送、退避、落盘、补传；`RunJob` 外面还有一层 `catch`，**任何意外都不许杀死消费线程**。
- 后台 → UI：`LastResult` 是普通 CLR 属性 + `PropertyChanged`，赋值发生在队列线程上，
  视图的处理器**必须 `Dispatcher.BeginInvoke`** 再摸 `Foreground` / `ItemsSource`。
- `job` 不持有插件引用（`OwnerName` 只是字符串），所以插件实例被回收不会拖住队列。
