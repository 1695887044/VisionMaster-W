# 架构：一张卡一个实例，句柄、两条线程与命令 FIFO

> 分类：运动卡 ｜ 来源：插件 Plugin.Motion.ZMotion ｜ 读者：开发

本页讲驱动内部"为什么这样摆"：文件分工、句柄与连接生命周期、指令下发与轮询、
多卡多轴映射、状态读取路径。契约与宿主提供的能力见「宿主契约与算子接口」页，
必须守住的规矩见「不变量与断言对照」页。

## 1. 文件分工

| 文件 | 内容 | 为什么这样切 |
| --- | --- | --- |
| `ZMotionCard.cs` | 一张卡"怎么说话"：连接、断开、轮询、命令下发、回零两条路径、SDK 诊断 | 只有这部分是正运动特有的 |
| `ZMotionModels.cs` | 纯函数与静态表：状态字位常量与解析、回零模式码映射、机型能力表、错误码→故障 | 不碰句柄、不碰线程，能被断言逐个钉死而不需要真机 |
| `Sdk\Zmcaux.cs` | 厂商原始声明文件（3558 行，不动它） | 与 native 库同目录成对替换，避免版本错配 |
| `Plugin.Motion.ZMotion.csproj` | 只引用契约层；native 通配投递到宿主目录；`Help\**` 作为嵌入式资源 | 见「宿主契约与算子接口」页第 6 节 |

工程红线：驱动插件**只引用 `Shard\Core.Interfaces`**，不引用宿主 exe。
需要宿主信息（日志、轴注册表、卡服务）时一律走契约层的抽象。

`ZMotionModels.cs` 里被单独抽出来的四类，注释都写了同一句理由：
写错的后果不是"少显示一个灯"，而是**把报警读成到位**或**把限位读成正常**。
这类错误只有真机会暴露，所以用纯函数钉死，而不是塞进 `ZMotionCard` 的方法体里。

## 2. 类与实例的对应关系

```
MotionDescriptor（一张卡的配置）  →  ZMotionCard（一个实例 = 一张卡）
   ├─ Axes: AxisMapping[]              ├─ 逻辑名 ↔ 物理轴号（0 基）
   ├─ Params: MotionParams             ├─ 卡级参数（轮询/看门狗/超时/队列深度…）
   └─ Points（每轴 16 点）              └─ 与别的卡完全无关：每实例一套句柄 + 两条线程
```

- 实例化形状被契约锁死：宿主用 `Activator.CreateInstance(驱动类型, descriptor)`，
  所以驱动**必须有 `ZMotionCard(MotionDescriptor)` 构造函数**（基类构造强制了这一形状）。
- 驱动身份靠 `MotionDescriptor.DriverTypeKey`（类型键为程序集限定名）绑定；
  换驱动 = 换卡 = 必须重建实例，不存在"热切驱动"。
- `[Display(Name/GroupName/Description/ShortName)]` 决定它出现在设备列表的哪一组、叫什么。

## 3. 句柄与连接生命周期

`_handle`（`IntPtr`，0 表示未连接）是本类唯一的设备资源。基类 `Connect()` 的流程：

1. 持锁：`_state != Closed` 直接返回 true（**幂等**），否则置 `Connecting`。
2. **锁外**调 `ConnectCore()`：网口建链可能几百毫秒到几秒，持锁会把所有状态查询堵死。
3. 成功：记最近存活时刻；`_isHomed = Capabilities.SupportsAbsoluteEncoder`；
   保留驱动在 `ConnectCore` 里写的状态说明（机型/地址/轴数/IO），为空才退化成"已连接"；
   置 `Online`；起两条线程。
4. 失败：`_isHomed = false`，置 `Closed`，说明写 `连接失败：{异常信息 ?? "设备未给出原因"}`。

`ConnectCore()` 内部（顺序即依赖，别调换）：

| 步 | 动作 | 失败时 |
| --- | --- | --- |
| 1 | 地址非空校验 | `SetDetail("未填写控制器 IP 地址")` 返回 false |
| 2 | `ZAux_OpenEth(地址, out handle)` | `DllNotFoundException` → 点名缺哪个 native 文件 + 上报"需现场处理"级故障；`BadImageFormatException` → 单独提示位数不符；返回码非 0 或句柄为 0 → 走 `ZMotionFaults.Interpret("连接控制器", 码)` |
| 3 | `ZAux_GetControllerInfo` 读机型与固件 | 失败只 `Warn`，退到配置机型或兜底 |
| 4 | `ZMotionModels.Resolve(配置机型, 上报机型)` | 都不认识 → `Recognized=false` + 兜底 4 轴并 `Warn` 提示核对 |
| 5 | 逐根**启用轴** `ZAux_Direct_SetUnits`，下限 0.0001 | 失败只 `Warn`（位置与目标将按控制器原单位解释） |
| 6 | `SetCapabilities(...)`（轴数、IO 数、硬限位、点动、回零方式集合、机型 IO 是否核实值） | — |
| 7 | `MarkHomed(false)` | 注意：随后被基类按绝对编码器能力位重算，见「坐标系与单位口径」页 |
| 8 | `SetDetail("已连接 {机型} @ {IP}（固件 …）｜{N} 轴｜IO {i} 入 / {o} 出…")` | — |

`Disconnect()` 的顺序**不能变**：先停线程（否则命令线程可能正在执行命令）→ 安全停止 →
清队列 → `DisconnectCore()` 关句柄 → 清快照 → 置 `Closed`、`_isHomed = false`。
`DisconnectCore()` 把 `_handle` 先置 0 再 `ZAux_Close`，关闭异常只 `Warn` 不上抛。
线程 Join 带 2000ms 超时：线程卡在 SDK 调用里时不能把调用方（可能是 UI 线程）拖死。

各方法进入时的共同写法是 `var handle = _handle;` 先取快照再使用，
`handle == IntPtr.Zero` 一律视为"控制器未连接/句柄无效"。

## 4. 两条后台线程

`StartThreads()` 在建链成功后起两条后台线程（名字带卡名，便于日志区分）：
`MotionCmd-{卡名}` 与 `MotionPoll-{卡名}`。

### 命令线程（单线程 FIFO + 紧急插队）

- 数据结构：`LinkedList<MotionCommand>` 尾进头出；`_emergency` 槽位优先于队列。
- `TakeNextLocked()`：先看紧急槽（取出即结束"急停打断"阶段），否则取队首。
- 空转时用 `ManualResetEventSlim` 等 50ms 唤醒，不忙等。
- **一个命令队列 = 一张卡**：同卡命令串行，不同卡互不相干。这是"多轴同时起步"
  能成立的原因——定位命令下发即返回，真正的等待交给上层「等待到位」步骤。

`ExecuteOne()` 的关键顺序：

1. 已取消的命令直接跳过。
2. **执行前再校验一次门禁**：命令可能在队列里等了一会儿，期间卡可能进报警/断开。
3. 把"设备令牌"与"急停令牌"链接成一个 `ct` 交给 `ExecuteCommandCore`；
   驱动只要尊重 `ct`（等待循环用 `ct.WaitHandle`），就同时获得"断开/退出"与"急停打断"两种中断。
4. `OperationCanceledException` 分成两种表述：`被急停中断`（预期，不报故障、不进安全停机）
   与 `已取消（设备断开或流程中断）`。
5. 结果映射：`Done→Done`、`TimedOut→TimedOut`、`NotSupported→Failed`、其它 `→Failed`。
6. **先落副作用再发完成信号**：回零成功时先 `MarkHomed(true)` 再 `CompleteCommand`。
   顺序反了，等待方被唤醒会读到"命令已完成但 IsHomed 仍为 false"的中间态。
7. 失败/超时（且不在急停打断中）：分级上报 → 清队列 → 置 `SafeStopped` → `SafeStopCore()`。
   理由是"运动类命令失败后轴卡在什么状态不知道"，继续接受下一条等于在不知道位置时继续走。

### 轮询线程（状态读取的唯一路径）

- 周期 `Math.Clamp(PollIntervalMs, 1, 1000)`；仅在 `Online`/`Alarm` 两种状态下真的去读。
- 每轮四步：`PollStatusCore(ct)` → `NotifyAlive()` → `DetectCriticalSignals()` → `DetectWatchdog()`。
- 异常处理：单次失败只累计 `_pollFailStreak`，**连续 3 次**才判失联（`Recoverable`）。
  所以驱动里读轴状态失败必须抛（`读取轴 {逻辑名} 状态失败（码 {N}）`），
  吞掉异常会让"轴状态一直不变"看起来像设备正常。

`PollStatusCore` 每根启用轴四次往返：

| 调用 | 得到 | 失败时 |
| --- | --- | --- |
| `ZAux_Direct_GetAxisStatus` | 状态字 → `ZMotionStatusParser.Parse` | **抛出**（通信问题的判据） |
| `ZAux_Direct_GetIfIdle` | `Moving = (idle == 0)` | 保留上一值 |
| `ZAux_Direct_GetMpos` | `PositionMm`（已是 mm，**不再除脉冲当量**） | 保留上一值 |
| `ZAux_Direct_GetAxisEnable` | `Enabled` | 保留上一值 |

之后一次 `ZAux_Direct_GetInMulti(0, 输入数-1, values)` 批量读输入。
这里刻意不逐点问：10ms 周期下逐点读 24 个点 = 每周期 24 次往返，网口卡会被自己打满。
输入读失败被吞掉不判失联——轴状态才是安全相关信号，输入少刷一次不影响安全判断（真断了轴状态那边会先抛）。

**速度不在轮询里**：因此 `AxisStatus.Velocity` 恒为 0，任何"看速度"的逻辑都不可用。

## 5. 安全停机路径（SafeStopCore 与 EmergencyStop 的分工）

| 路径 | 停法 | 由谁触发 | 备注 |
| --- | --- | --- | --- |
| `SafeStopCore()` | 逐根启用轴 `Single_Cancel(轴, 2)`（**减速停**） | 断开、失联、命令失败、取消全部 | 收尾用，不是急停；此路径不再上抛异常 |
| `EmergencyStop()` | 一条 `Stop` 命令，`PhysicalAxis = -1`（全部轴）、`StopMode = 3`（立即停），进紧急槽 | 界面急停、以及基类发现硬信号（急停输入/正/负限位） | 同时清空普通队列并打断当前命令 |

正运动没有"停所有轴"的单一调用，`ExecuteStop` 见 `-1` 时逐轴下发取消并聚合失败轴名。
命令里 `StopMode <= 0` 时驱动兜底取 2（减速停）。

硬信号到来时基类的动作是：立即急停 → 置 `Alarm` 并写 `{轴名}：{原因}` →
上报"需人工复位"级故障（急停/限位与建议"排除触发原因…"，伺服报警与建议"检查驱动器报警原因…"）。
同一轴同一原因只报一次（去重字典），信号恢复后才清记录——否则 10ms 轮询会把日志刷爆。

## 6. 回零的两条路径

判据来自机型表的 `IsBusType`：

- 总线型：`ZAux_BusCmd_Datum(轴, 模式码)` 启动 → 轮询 `ZAux_BusCmd_GetHomeStatus`，
  **只认 `1 = 完成`**。其它值（含"正在回零"与"失败"）继续等到超时——这里刻意不猜每个状态码的含义。
- 脉冲型：`ZAux_Direct_Single_Datum(轴, 模式码)` → 等 `GetIfIdle` 显示空闲。
  正运动脉冲卡的回零参数在控制器内设定，所以"空闲"就当作流程走完。

两条路径共同点：超时用 `command.Timeout`，为空时兜底 `max(1000ms, CommandTimeoutMs)`；
每次等待 20ms（`ct.WaitHandle.WaitOne(20)`）；**超时前先 `Single_Cancel(轴, 2)` 停轴**，
不把一根还在寻零的轴丢在那儿去执行下一条命令。

回零速度与回零偏移**不下发**：现场通常已用厂商调试软件调好，驱动擅自改会破坏已调好的机构行为。

## 7. 状态读取的完整路径（谁什么时候填的）

```
真机/仿真器
  → ZAux_Direct_* （轮询线程，按周期）
  → UpdateAxisStatus / UpdateInput（基类缓存 _axisCache / _inputCache）
  → GetAxisStatus(物理轴号) / ReadInput(点号)   ← 无锁读缓存
  → 界面（每轴状态标签、IO 表） / 步骤算子（读状态、等待到位）
```

要点：

- 上层**从不**在 UI 线程上同步等命令；读的都是缓存，所以界面不会因为队列里有回零而冻住。
- 缓存的"新鲜度"等于轮询是否成功。断开时 `ClearSnapshots()` 清空，避免读到上一世的值。
- 物理轴号是这条路径的唯一索引：逻辑名 → 物理轴号在 `MotionDescriptor.FindAxis` /
  轴注册表里完成，驱动只在入口 `LogicalNameOf(command.PhysicalAxis)` 反查一次用于文案（查不到退化成 `轴{N}`）。
