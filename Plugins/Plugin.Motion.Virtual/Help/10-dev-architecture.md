# 虚拟运动卡（仿真）· 开发：驱动架构与两线程模型

> 分类：运动卡 ｜ 来源：插件 Plugin.Motion.Virtual ｜ 读者：开发

本页只讲实现层：`VirtualMotionCard.cs`（427 行）覆写了基类的哪几个钩子、每块状态归哪条线程、
"仿真到哪一层"在代码里是怎么定下来的。用户页讲现象与口径，这里讲**为什么这么写、改它会牵连什么**。

## 一、驱动层写什么、契约层给什么

| 关注点 | 提供方 | 本驱动的做法 |
| --- | --- | --- |
| 状态机（Closed/Connecting/Online/Alarm/SafeStopped） | `Shard\Core.Interfaces\Motion\MotionDeviceBase.cs` | 驱动**不判**"现在能不能动"，只把状态素材（报警位、限位位）回填给基类 |
| 队列、急停插队、能力校验、软限位门禁 | 基类 `Enqueue` + `CheckGateLocked` | 驱动不参与，也不许自己重复实现一遍 |
| 失联判据与安全停机 | 基类 `PollLoop` + `DetectWatchdog` + `HandleLost` | 驱动只负责"让轮询失败"，不自己判次数 |
| 与设备说话 | 本文件 6 个覆写：`ConnectCore` / `DisconnectCore` / `PollStatusCore` / `ExecuteCommandCore` / `SafeStopCore` / `ClassifyOutcome` | 全部仿真知识都在这一份文件里 |

判断某段代码该不该写在这里的标准：**换一家卡还要重写，它才属于驱动层**；
否则它就是被放错地方的基类逻辑，后果是每家驱动各漏一遍软限位或状态判断。

## 二、ConnectCore：能力声明是门禁的真源

`ConnectCore` 只做三件事，但每件事都被基类拿去当判据用：

1. **重建仿真世界**：`_axes` 按 `Math.Max(1, SimulatedAxisCount)` 逐根建（初始位置 0、目标 0、未使能），
   `_outputs` / `_inputs` 同样按 `Math.Max(1, …)` 建数组。轴数下限写死为 1：0 根轴的卡会让基类的
   能力校验退化成"无法校验"，测不出配置错误。
2. **`SetCapabilities`**：轴数、DI/DO 点数、`SupportsHardLimit = true`、`SupportsAbsoluteEncoder` 取注入开关、
   不支持插补（两个 `SupportsLine/ArcInterpolation` 恒 false）、`SupportsJog = true`、
   `SupportedHomeModes` 只声明 3 个成员（`NegativeLimitIndex`、`Origin`、`PresetZero`）。
   这份声明就是基类 `CheckGateLocked` 的输入：**声明与实现必须成对改**（守门断言见
   `连接后能力已回填（3 轴 / 8 输入 / 8 输出 / 有硬限位）`，漏声明侧则由
   `★不支持的回零方式在入队时就被拒绝（而不是运行到一半才失败）` 拦住）。
   `SetCapabilities` 内部还会调 `Descriptor.EnsureAxes(AxisCount)`，把方案里没铺满的轴补成默认逻辑名，
   所以"配置了 2 根轴、仿真 3 根轴"时第 3 根也会出现在候选里。
3. **`MarkHomed(SimulatedAbsoluteEncoder)` + `SetDetail`**：注意基类 `Connect()` 成功分支随后还会按
   `_capabilities.SupportsAbsoluteEncoder` 再赋一次 `_isHomed`，两处口径必须一致；
   若只改 `MarkHomed` 而忘记让能力位跟着变，"绝对式重连即位置可信"这条会以能力位为准、驱动这边的赋值白写。
   `SetDetail` 写的两行文案（几根轴 + 绝对/增量 + 是否需先回零）**会被基类保留** ——
   基类只在说明为空或仍是"正在连接"文案时才回退成"已连接"，这是驱动把"连上的到底是哪台、几轴"透给现场的唯一通道。

`DisconnectCore` 把三张表全清（轴表清空、IO 数组换成 `Array.Empty`）；轴快照由基类 `ClearSnapshots()` 清，
驱动不该自己去清，否则断开顺序上会先清掉还在跑的轮询线程要用的数据。

## 三、两条线程 + 一张轴表：状态归属

基类连接成功后起两条后台线程（线程名 `MotionCmd-{卡说明}` / `MotionPoll-{卡说明}`），
本驱动的全部可变状态只有三个集合（`_axes`、`_outputs`、`_inputs`），逐字段归属如下：

| 状态 | 写者 | 读者 | 竞态纪律 |
| --- | --- | --- | --- |
| `SimAxis.TargetMm` / `VelocityMmPerS` | 命令线程（Move/Jog/Stop/Disable 落笔） | 轮询线程 | 命令线程**不写** `PositionMm`，只改目标 |
| `SimAxis.PositionMm` | 轮询线程（逐拍推进） | 轮询线程自己、`GetSimulatedPosition` | 只有轮询线程推进位置，所以不会撕裂 |
| `SimAxis.Enabled` / `Alarm` | 命令线程（Enable/Disable/ClearAlarm） | 命令线程判门禁、轮询线程填快照 | 读到的可能是"刚使能、快照还没刷新" |
| `SimAxis.Homing` / `HomeDeadlineTick` | 命令线程起、轮询线程收尾（也可被命令线程的取消/超时收尾） | 两条线程 | 唯一的**跨线程协作位**，见第四节 Home |
| `_inputs` | 任意线程（测试用 `SetInput`） | 轮询线程每拍全量 `UpdateInput` | 值要等下一拍才可见 |
| `_outputs` | 命令线程（`SetOutput`） | 驱动自己不读 | 没有输出回读，写进去就没人校验 |

基类的 `_axisCache` / `_inputCache` 是并发字典，`UpdateAxisStatus` 覆盖式写入并把 `Timestamp` 打成当前时间，
所以步骤与界面读到的是"最近一拍的整轴快照"：**同一根轴内自洽，跨轴之间不保证同一时刻**——
这正是 `MotionReadStatusPlugin` 读到的九个值可能来自相邻两拍的原因。

## 四、ExecuteCommandCore 的分派表

仿真深度就是这张表，每条命令"做到哪一层"在这里定死：

| Kind | 前置校验 | 处理 | 返回 | 等待 |
| --- | --- | --- | --- | --- |
| Enable | 轴号 | 置 `Enabled = true` | Done | 否（即时记账） |
| Disable | 轴号 | 置未使能，并把 `TargetMm` 拉回 `PositionMm` | Done | 否 |
| MoveAbsolute | 轴号、未使能、轴报警 | 写 `TargetMm` 与速度（命令速度大于 0 才用，否则回退卡默认速度） | Done | 否 |
| MoveRelative | 同上 | `TargetMm = 当前位置 + 增量` | Done | 否 |
| Jog | 同上 | 目标设成当前位置加减一百万 mm，靠 Stop 把目标撤回 | Done | 否 |
| Home | 轴号、未使能（文案与 Move 不同） | 起 `Homing` 与到期时刻，**在命令线程 while 等** | Done / TimedOut / 抛取消 | 是 |
| Stop | `PhysicalAxis` 为负数时不清轴、直接停全部 | 目标拉回当前位置、`Homing = false` | Done | 否 |
| SetOutput | 端口范围（超出时报"输出点 … 超出范围（0~…）"） | 写 `_outputs` | Done | 否 |
| ClearAlarm | 无 | 逐轴清 `Alarm`，并**同时把注入开关 `SimulateAlarm` 复位** | Done | 否 |
| 其余 | — | 不处理 | NotSupported，文案 `虚拟卡不支持命令：…` | — |

三条实现层的约定：

- **取消按契约抛异常**：Home 的等待循环发现 `ct` 已取消时，先把 `Homing` 置回 false，再抛
  `OperationCanceledException`。基类 `ExecuteOne` 抓住它后按 `_emergencyPending` 分别写成
  "被急停中断"或"已取消（设备断开或流程中断）"，**既不报故障也不进安全停机**。
  这里改成返回 Failed 的后果是把人工按下的急停记账成一次设备故障，现场排查会被带偏
  （守门断言 `急停是预期操作：不记成设备故障、也不进安全停机`）。
- **只有 WaitsForCompletion 的命令才在这里等**：Move 立即返回是刻意的（守门断言
  `★Move 下发成功 ≠ 已到位（命令已 Done，但轴仍在途中）`，口径见用户册《仿真相位差与延时口径》），
  等待到位由流程里的等待步骤负责；驱动把 Move 改成同步等待，整条异步语义就废了。
- **失败前置门只卡三类命令**：`SimulateCommandFailure` 为真时只让 MoveAbsolute / MoveRelative / Home 返回 Failed，
  Jog、Stop、Enable 不在其中——这是仿真覆盖面的一部分，写冒烟测试时别以为开关能注入一切失败。

## 五、PollStatusCore 的推进算法

1. 注入优先：`SimulatePollException` 为真时抛 `InvalidOperationException("模拟：与运动卡的通信中断")`。
   单次抛异常只算"一次轮询失败"，基类累计到连续 3 次才判失联，看门狗是另一条独立判据。
2. `intervalMs` 取 `Descriptor.Params.PollIntervalMs` 并夹到 1~1000，`dtSec` 由它除 1000 得到。
   这条 clamp 与基类 `PollLoop` 的完全一致——两边不同步改就会出现"位置推进速度是配置的两倍"这种诡异现象。
3. 逐轴：先 `ct.ThrowIfCancellationRequested()`，再建快照，报警位取"轴级 Alarm 与全局注入开关的或"，
   有报警时同时填 `AlarmMessage`（基类 `DetectCriticalSignals` 直接把这句当报警原因上报）。
4. 回零中的轴：没到到期时刻只置 Moving，到期那拍才把位置与目标归零并置 InPosition——
   所以"回零完成"这件事在快照里比命令线程的 Done 晚不到一个节拍，基类特意把 `MarkHomed` 放在发完成信号之前。
5. 正常运动的轴：目标与位置的差的绝对值大于 1e-9 才推进；步长取速度乘 `dtSec`，
   步长本身不大于 1e-9 时回退用卡的默认速度乘 `dtSec`；剩余距离不大于一个步长就直接吸附到目标。
   Moving / InPosition / VelocityMmPerS 都由"吸附后是否还有剩余"这同一个判据推出，不会出现"既到位又在动"。
6. 限位位：`Descriptor.FindAxis(index)` 拿到映射、且该映射 `Enabled` 且位置越出软限位区间时才置
   PositiveLimit / NegativeLimit ——**仿真里没有独立的硬限位参数，借软限位当硬限位用**。
   基类看到限位位是"立刻 EmergencyStop 插队 + 转 Alarm + RequiresReset 级故障"。
7. 结尾按 `_inputs` 长度逐个 `UpdateInput`，全量刷新、不做变化检测。

`SafeStopCore` 与 Stop 的收尾动作相同（目标拉回当前位置、清 Homing），被基类在断开、失联、
流程中断三条路径上调用；真机驱动在这里要下实际停止命令，虚拟卡只需记账，
但**不许删**——不重写的话失联时仿真世界还会按最后一条命令继续推进位置。

## 六、ClassifyOutcome：错误文案是分级输入

默认分级（基类）是 TimedOut → RequiresReset、Failed → Recoverable。本驱动只多一条：
`error` 里含"未使能"时给出可恢复级与建议"在流程里把「轴使能」步骤放在运动步骤之前"。
注意判据是**文案子串**，Move 与 Home 两条不同的未使能文案都能命中（都含"未使能"三个字）。
所以改错误文案等于改故障分级：这句子串与 `05-troubleshooting.md` 的原文对照表是绑在一起的。

## 七、描述符字段在驱动里的消费矩阵

| 字段 | 谁读 | 改了不生效说明什么 |
| --- | --- | --- |
| `Params.PollIntervalMs` | 基类节拍与本驱动 `dtSec` | 只改节拍不改速度：推进量随 `dtSec` 一起变，速度不变 |
| `Params.DefaultVelocityMmPerS` | 命令速度不大于 0 时、步长过小时的兜底 | 命令没给速度时轴"慢慢走"而不是"不走" |
| `Params.CommandTimeoutMs` | Home 的等待 deadline（命令自带 Timeout 不大于 0 时） | 步骤显式给了超时就以步骤的为准 |
| `Params.WatchdogTimeoutMs` / `MaxQueueDepth` | 只被基类读 | 驱动完全不感知 |
| `Params.AutoReconnect` | **没人读** | 运动层没有自动重连实现，勾了也一样（真机驱动同理） |
| `Params.DefaultAccelMmPerS2` | 不读 | 仿真是匀速，加减速段与跟随误差永远验不到 |
| `Address` / `CardModel` | 不读 | 仿真无通信层，地址只是界面与稳定键的素材 |
| `AxisMapping.Enabled` / `SoftLimitMinMm` / `SoftLimitMaxMm` | 限位位（本驱动）+ 软限位门禁（基类） | 同一个映射被两处读：关掉 Enable 会让入队侧和执行侧一起失去限位 |
| `AxisMapping.LogicalName` | 基类故障文案的轴名 | 改名的级联见 `MotionDescriptor.RenameAxis` |
| `AxisMapping.UnitsPerMm` | 不读 | 仿真直接以 mm 记账、没有脉冲层，脉冲当量类问题验不到 |

## 八、生命周期与所有权

- 三张表的生命周期跟连接走：`ConnectCore` 建、`DisconnectCore` 清，不存在跨连接的仿真残留。
- 命令对象归调用方（流程步骤用 `using` 释放自带的完成事件）。驱动只在执行期内读它的字段，
  **不把它存进任何集合**，Home 的等待循环也只读 `Timeout`——这是步骤能提前释放命令的前提。
- 设备对象由宿主（运动提供方）创建与释放，本驱动的构造函数只接一个 `MotionDescriptor`；
  签名变了宿主就反射构造不出来。
- 线程是后台线程且 `StopThreads` 用带 2 秒上限的 Join、不强杀。仿真里 `PollStatusCore` 不会长阻塞；
  真机驱动如果在轮询里卡在 SDK 调用上，`Disconnect()` 会先返回、线程稍后自己退，别指望它同步收尾。

## 九、加一条仿真能力要同时动哪几处

| 想加 | 必须同时动 | 漏一处的后果 |
| --- | --- | --- |
| 新的命令种类 | `ExecuteCommandCore` 的 switch 分支 | 落到 default 变 NotSupported，而 NotSupported **不会**触发安全停机，队列会照常继续跑 |
| 新的回零方式 | `SupportedHomeModes` 数组 + 实际实现 | 只加声明则门禁放过、行为却与真卡不同；只加实现则入队就被拒绝 |
| 新的故障注入开关 | 属性 + 生效点 + 一条断言 | 冒烟测试注入不进去，开关成了摆设 |
| 新的错误文案 | `ClassifyOutcome` 的子串判据 + `05-troubleshooting.md` 的逐字对照表 | 故障分级与手册一起失真 |
| 仿真参数默认值 | 用户册《配置项与参数》的仿真开关表 | 界面读不到这些开关，默认值只能靠手册交代 |
