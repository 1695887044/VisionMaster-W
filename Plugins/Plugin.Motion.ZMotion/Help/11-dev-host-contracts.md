# 宿主契约：白拿的基类、七个步骤算子的用法与落盘键

> 分类：运动卡 ｜ 来源：插件 Plugin.Motion.ZMotion ｜ 读者：开发

本页写"驱动作者不必自己实现的部分"与"驱动必须配合宿主的约定"。
驱动代码里看不到队列、门禁、看门狗、故障分级——它们在契约层的 `MotionDeviceBase` 里。

## 1. 必须实现的四个抽象 + 三个可选钩子

| 钩子 | 必须? | 本驱动的写法 |
| --- | --- | --- |
| `ConnectCore()` | 必须 | 建链、读机型、`SetUnits`、`SetCapabilities`、写状态说明 |
| `DisconnectCore()` | 必须 | 置零句柄后 `ZAux_Close`，异常只 `Warn` |
| `PollStatusCore(ct)` | 必须 | 逐启用轴读状态字/空闲/位置/使能，批量读输入 |
| `ExecuteCommandCore(command, ct, out error)` | 必须 | 按命令种类下发，返回 `Done/TimedOut/Failed/NotSupported` |
| `SafeStopCore()` | 可选（有默认空实现） | 逐启用轴减速停（`Single_Cancel(轴, 2)`） |
| `ClassifyOutcome(...)` | 可选（基类给通用分级） | 本驱动未覆写，分级交给 `ZMotionFaults.SeverityOf` 在构造 error 时体现 |
| `ModelName`（虚属性） | 可选 | 返回识别出的机型名，界面与日志用 |

## 2. 白拿的能力（一行没写但一直在用）

| 类别 | 成员 | 语义 |
| --- | --- | --- |
| 状态 | `State` / `StateDetail` / `Capabilities` / `LastFault` / `IsHomed` / `ExecutedCommandCount` / `RejectedCommandCount` / `FaultCount` | 全部加锁读，界面与算子随便调 |
| 事件 | `StateChanged` / `FaultOccurred` | 一律在锁外触发；订阅方（界面）抛异常被吞掉，绝不能影响设备状态机 |
| 生命周期 | `Connect()` / `Disconnect()` / `Dispose()` | 幂等；断开顺序固定（停线程→安全停→清队列→关句柄→清快照） |
| 下发 | `Enqueue(cmd)` / `EmergencyStop()` / `CancelAll(reason)` / `ClearAlarm(out error)` | 门禁、排队、插队、打断、拒绝留痕都在这里面 |
| 读状态 | `GetAxisStatus(物理轴号)` / `ReadInput(点号)` / `NotifyAlive()` | 读缓存，不做通信 |
| 驱动侧工具 | `UpdateAxisStatus` / `UpdateInput` / `SetCapabilities` / `MarkHomed` / `ClearSnapshots` / `SetDetail` / `ReportFault` / `NotifyDeviceLost` / `Params` / `Log` | 只有这些是驱动写状态的手段 |

### 门禁（`CheckGateLocked`，入队时与执行前各跑一次）

集中实现的七条（注释原话：这四类检查每家卡都要做，交给驱动等于让每个作者各写一遍，
漏掉的后果是"对着报警中的轴下发运动命令"，真机上就是撞机）：

1. `ClearAlarm` 在 `Online` 与 `Alarm` 下都放行（报警态下它正是唯一该被允许的命令）。
2. 其余命令一律要求 `Online`，否则按状态给具体文案（未连接/正在连接断开/报警/安全停机）。
3. 运动与使能/回零类命令校验物理轴号（含"配了 3 号轴但卡只有 2 轴"）。
4. `Stop` 允许 `PhysicalAxis = -1`（全部轴）。
5. 回零方式必须在 `Capabilities.SupportedHomeModes` 内。
6. 写输出校验输出点号。
7. 绝对运动校验**软限位**（`AxisMapping.IsWithinSoftLimit`）。

被拒的命令会留痕成一条故障：软限位拒绝判为 `Notice`（业务上正常），其余为 `Recoverable`。

### 打断与急停

- `IsInterrupting`：`Stop` 与 `Disable` 入队时**立即打断正在执行的那条**（但不清队列）。
  没有这一条，"停止"会老老实实排在几十秒的回零后面，等轮到它轴早自己走完了。
- 打断动作（`_emergencyCts.Cancel()`）必须在 `_gate` 锁**之外**执行：锁内取消会与命令线程抢同一把锁，等于自己等自己。
- 急停更重一层：清空整个队列 + 插队一条 `Stop(-1, StopMode=3)` + 打断当前命令，
  并且给后续命令换一个新的 `CancellationTokenSource`（旧的已 Cancel，不能复用）。
- 队列深度 `Math.Max(1, MaxQueueDepth)` 满时返回 `Rejected_QueueFull`。

## 3. 与 Plugin.Motion.Steps 七个算子的契约

算子不引用本驱动，只通过 `MotionAxisResolution`（契约层静态入口，宿主启动时 `Attach` 轴注册表与卡服务）
按**逻辑轴名**或**卡名**解析出 `(设备, 轴映射)`，再调用 `IMotionDevice`。

| 算子 | 寻址 | 用到的驱动面 | 对驱动的要求 |
| --- | --- | --- | --- |
| 使能 `MotionEnablePlugin` | 轴名 | `Enqueue(Enable/Disable)` + 等待完成 + 读 `Enabled` | 失能要先撤当前运动再断电（"失能瞬间还带着速度"不可接受）；失能属打断性命令 |
| 回零 `MotionHomePlugin` | 轴名 | `Enqueue(Home)` 阻塞等完成 + `Capabilities.SupportsHomeMode` | 模式码只能来自手册；不支持时给"该卡不支持回零方式…"；默认步骤超时 120s |
| 定位 `MotionMovePlugin` | 轴名 | `Enqueue(MoveAbsolute/MoveRelative)` | 位置模式**下发即返回**；输出 `Accepted` 明确"不代表已到位"；速度 0 表示沿用卡内速度 |
| 等待到位 `MotionWaitPlugin` | 轴名 | `GetAxisStatus` 轮询缓存 | 判据是 `InPosition && !Moving`；`HasCriticalSignal` **优先于**到位（否则把"撞停"当成"走到位"）；状态读不到即判断开 |
| 读状态 `MotionReadStatusPlugin` | 轴名 | `GetAxisStatus` 一整套字段 | 缓存字段没填的一律给假值：`Velocity` 恒 0（驱动不轮询速度），别拿它做判断 |
| IO `MotionIoPlugin` | **卡名** | `Enqueue(SetOutput)` / `ReadInput` | 卡级操作没有轴概念，必须按卡名选卡；点号 0 基；点数可能只是显示上限 |
| 停止 `MotionStopPlugin` | 轴名（留空 = 全部） | `EmergencyStop` / 逐设备 `Enqueue(Stop)` | `-1 = 全部轴`；逐轴下发失败要聚合报出来，不能只报第一条 |

算子侧的稳定错误文案（改动这些字符串会同时影响用户页与断言）：
`运动卡服务尚未就绪：请在主程序内运行流程`、
`找不到名为「{卡名}」的运动卡：请到「运动卡设置」确认卡名，或换一张卡`、
`轴「{轴名}」在卡「{卡名}」上处于停用状态：…`、
`运动卡「{卡名}」当前不可运动：{状态说明}`。

轴名/卡名端口的三条约定：`IsRequired = false`（老流程只填常量，不能因为没链接就判编译失败）、
`IsFunctionalEnum = true` + `OptionKind = MotionAxisName`（候选来自**当前方案**，运行时现取，不能存快照）、
写进流程的**值必须是稳定键**（轴名/卡名本体，不是显示文本也不是地址）。

## 4. 宿主注入与运行环境

| 注入点 | 谁做的 | 驱动要知道的 |
| --- | --- | --- |
| `MotionDeviceBase.Log` | 宿主 `MotionProvider` 在建实例后赋值 | `Log` 可为 null，基类内部都用 `Log?.`，驱动照此写法 |
| 执行上下文里的 `IMotionProvider` | 宿主（正式运行与试运行**两条路径都要注入**，共用同一组装规则） | 漏一条的表现是"算子永远拿不到卡" |
| `Activator.CreateInstance(类型, descriptor)` | 宿主按 `DriverTypeKey` 解析类型后创建 | 构造函数形状固定；解析失败文案 `运动卡「{卡名}」的驱动「{键}」不可用（插件未加载或被删除）` |
| 类型解析 | 先 `Type.GetType`，失败再遍历已加载程序集按全名找 | 只靠 `Type.GetType` 会表现为"插件加载成功但卡建不出来" |
| `AutoConnect` | 宿主连接阶段只挑 `AutoConnect = true` 的卡 | 每方案只连一次；断线重连不由它驱动（该开关只是配置意图） |
| 界面线程 | 调试面板不在 UI 线程同步等命令 | 任何"点一下等结果"的界面改动都要走异步 |

## 5. 落盘键（跟着 `.vms` 走的东西）

| 位置 | 内容 | 口径 |
| --- | --- | --- |
| `SolutionModel.MotionCards` | `MotionDescriptor` 集合（卡配置 + 轴映射 + 卡级参数 + 每轴点位表） | 与相机配置同源：声明**具体类型 + 具体集合**，`TypeNameHandling.Auto` 不写 `$type`，因此不需要进反序列化白名单；`ObjectCreationHandling.Replace` 整体替换避免与初始化器叠加 |
| `SolutionModel.CamTables` | 电子凸轮表（**全局实体，不挂在任何卡下**） | 删卡不动它；主/从轴存「卡名 · 轴名」标签，启动同步时才解析 |
| `MotionDescriptor.Axes[]` | `AxisMapping` 全字段 | 纯配置；`Id` 是内部稳定身份，`DriverTypeKey` 绑驱动，`DisplayName` 可随时改 |
| 每轴点位表 | 16 点 P0~P15 | **固定行数**，让"点位号"有稳定指代，随方案落盘 |
| 运行态 | 句柄、命令队列、轴状态缓存、计数器 | **从不序列化**，由宿主 `MotionProvider` 另存 |

命名空间坑：`SolutionModel` 在 `VisionMaster.Models` 下，属性里写 `Core.Interfaces.*`
必须带 `global::` 前缀，否则被解析成 `VisionMaster.Core.Interfaces`（CS0234）。

## 6. 工程与打包约定（改坏了不报错、只在现场炸）

| 约定 | 实现位置 | 破坏后的现象 |
| --- | --- | --- |
| 只引用 `Core.Interfaces` | `csproj` 的 `ProjectReference` | 驱动反向依赖宿主，插件无法独立测试 |
| native 库用通配 `Sdk\*.dll` 投递 | `csproj` + `CopyZMotionNativeToHost`（`AfterTargets=Build`） | 逐个列文件名的话，SDK 升级加文件就会漏；`Error` 条件保证 `Sdk\` 为空时编译期就拦下 |
| 声明文件与 native 库同在 `Sdk\` | 目录约定 | 分开放早晚版本错配 |
| `NoWarn` 只挡厂商文件带入的三类（CS1591/CS0169/CS0649） | `csproj` | 全局关告警会把驱动自身新代码的问题一起挡掉 |
| 手册 `Help\**` 作为 `EmbeddedResource` | `csproj` | 手册跟着 dll 走，不会出现"换机器手册没拷过去"；**改手册 = 重建本插件工程** |
| 工程登记进 `.sln` | 解决方案 | 全量构建不产出，`Modules\` 里缺 dll，表现为列表里没有这个驱动 |

宿主侧手册发现约定：`HelpCatalogService` 按 `{程序集名}.Help.` 前缀取嵌入式资源，
**文件名顺序即页序**（`NN-` 前缀），页标题取正文第一个一级标题。
