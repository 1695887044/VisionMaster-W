# 轴停止：架构、宿主契约与不变量

> 分类：运动控制 ｜ 来源：插件 Plugin.Motion.Steps ｜ 读者：开发

S 档一页承担三个角色：架构与数据流、宿主白拿的行为、改这个文件时的红线。
用户页讲的"怎么填"这里不重复，只讲**这些行为由谁提供、为什么这么写、写错会怎样**。

## 一、这个插件在分层里的位置

运动控制三层，本文件属于最薄的那一层：

| 层 | 位置 | 职责 |
| --- | --- | --- |
| 契约层 | `Shard\Core.Interfaces\Motion\` | `MotionDeviceBase`（队列/门禁/打断/急停/看门狗/状态机）、`MotionAxisResolution`（轴名解析与设备集合） |
| 驱动层 | `Plugin.Motion.ZMotion` / `Plugin.Motion.Virtual` | 把"停止"翻译成厂商动作（正运动：逐轴 `Single_Cancel`；虚拟卡：把目标撤回当前位置） |
| 步骤层 | 本工程 7 个插件类 | 解析名字 → 选通道（急停/普通）→ 造命令 → 入队 → 回报 |

`MotionStopPlugin.cs` 全文 140 行，没有一行 SDK 相关；`MotionStopMode` 的数值 2/3 只是"与正运动取消模式对齐"，
真正的映射在驱动侧——改显示名/换厂商都不会波及这里。

## 二、RunAlgorithm 逐步（执行时间线）

| # | 代码动作 | 为什么必须在这一刻做 |
| --- | --- | --- |
| 1 | `(Axis.ActualValue ?? string.Empty).Trim()` | 端口取值一次成型；`Trim` 之后"空白 = 全停"这条语义才对链接变量也成立 |
| 2 | `Accepted.Value = false` | **开头清零**：部分卡失败时下游不能拿上一轮的 true |
| 3 | `stopAll = axisName.Length == 0` 分岔 | 两种形态的判据就是"轴名留不留空"，**没有第三个开关**；留空 = 对每张卡下"物理轴号 -1"的停止 |
| 4 | 留空走 `MotionAxisResolution.TryGetAllDevices`；填名走 `TryResolveAxis`（失败时文案补 `（若要停全部轴，请把轴名留空）`） | 设备集合只从"确实被解析到"的卡推出——不遍历配置表，避免"配置里有、设备没建起来"的空引用 |
| 5 | `UseEmergencyStop` 为 true：对每张目标卡调 `device.EmergencyStop()`，任一拒绝立即 `Fail` | 急停不走命令队列构造，直接调设备通道；**第一张被拒就停手**——逐张发下去会让"半急停"看起来像成功 |
| 6 | 急停分支成功后 `Warn` 日志 + `Accepted=true` + 返回 | 急停是有机械后果的动作，日志必须留下（Warn 级，比普通下发的 Info 重一档） |
| 7 | 普通分支 `physicalAxis = mapping?.PhysicalIndex ?? -1` | -1 = 全部轴，这是契约约定（`MotionCommand.PhysicalAxis` 的语义），驱动层各自解释 |
| 8 | 造 `MotionCommand`：`Kind=Stop`、`StopMode=(int)Mode`、`WaitsForCompletion=false`、`Retryable=false`、`Timeout=卡级 CommandTimeoutMs`，并 `using` | **不等**：等停稳会把"停止"变成阻塞操作，与"停止要最快返回"矛盾；不可重试：停一次没停住，重试前的一切状态都可能已变 |
| 9 | 逐卡 `device.Enqueue(command)`，任一非 `Accepted` 立即 `Fail`（拼 `LastFault?.Suggestion ?? StateDetail`） | 拒绝原因就存在 `LastFault.Suggestion` 里，漏拼就只剩"被拒绝（Rejected_…）"半句话 |
| 10 | 每卡入队成功记一条 Info（`command.Describe()`） | 多卡全停时，现场要能数出"几张卡各下了一条" |

## 三、状态有几份、谁是真源

| 状态 | 真源 | 本步骤读还是写 |
| --- | --- | --- |
| 轴是否在动 / 停没停住 | 轴状态快照（基类轮询线程） | **不读**——本步骤不等停稳，确认是「读轴状态」的事 |
| 命令队列 / 急停插队 / 打断 | `MotionDeviceBase`（队列、`IsInterrupting` 判定、急停取消源） | 只触发，不实现 |
| 卡状态机（未连接/连接中/在线/报警/已安全停机） | `MotionDeviceBase` 状态机 + 闸门 | 只读结果（拒绝时的 `StateDetail`） |
| 界面填的值 | 步骤 `InputValues` + `[StepConfig]` 配置段 | 基类 `ApplyConfigValues` 灌端口，`OnConfirm` 写回 |
| 本插件自己 | **无**（无实例字段、无静态字段） | — |

**停止为什么能打断回零**（步骤层不用写、但必须知道的机理）：`MotionDeviceBase.IsInterrupting`
把 `Stop`（与失能）归为**打断性命令**——入队即 Cancel 正在执行的那条，但不清队列；
打断动作在**入队锁之外**执行（锁内 Cancel 会与命令线程抢同一把锁，等于自己等自己）。
急停则是另一条通道：清空整个队列 + 插队"停全部轴立即停" + 打断当前命令，三者一次做完。

## 四、白拿的宿主/基类行为（作者不用写、也别重写）

| 行为 | 由谁提供 | 表现 |
| --- | --- | --- |
| 端口发现/灌值/写回/参数面板 | `VisionPluginBase` + 宿主 `AutoPortConfigView` | 面板只列输入端口；`Mode` / `UseEmergencyStop` 两个 `[StepConfig]` 因此暂无界面入口 |
| 急停语义 | `MotionDeviceBase.EmergencyStop()` | 清队 + 打断 + 停全部轴；"被急停打断的命令"标记为取消（**预期行为，不记故障、不进安全停机**） |
| 报警态闸门差异 | 基类命令门禁 | 普通命令要求「在线」；清报警与急停两条通道允许「在线」与「报警」。步骤层不重复实现这两条判据 |
| 轴名解析 / 设备集合 | `MotionAxisResolution` | 未注入服务报 `运动卡服务尚未就绪`；空集合报 `当前没有任何可用的运动卡…` |
| 轴名下拉候选 | `StepConfigOptionKind.MotionAxisName` 动态候选 | 随方案变化，不是扫描期冻结的清单 |
| 流程失败语义 | `VisionPluginBase.Fail(...)` | `Success=false` + `ErrorMessage`，后续步骤照常执行 |

## 五、MotionDescriptor 参数口径（与其他 6 个步骤页同一份说法）

- 卡级（`MotionParams`）：`DefaultVelocityMmPerS` 50、`DefaultAccelMmPerS2` 500、`PollIntervalMs` 10、
  `CommandTimeoutMs` 30000（本步骤唯一直接消费的卡级参数——造停止命令时的 `Timeout`）、
  `WatchdogTimeoutMs` 3000、`AutoReconnect` true、`MaxQueueDepth` 256。
- 轴级（`AxisMapping`）：逻辑名（流程唯一引用）、物理轴号（0 基）、启用位（**全停只遍历已启用的轴**）、
  `UnitsPerMm` 1000、软限位 ±1000000；轴配置弹窗的 7 个低频字段本步骤不读。
- **引用一律用 Id 不用显示名**：`MotionDescriptor.Id`（Guid）是内部稳定身份；
  报错里的卡名取 `Caption`（显示名 → 地址 → "(未命名运动卡)" 回退），只给人看。
  `ResolveAxisIndex` 已退役，跨卡解析必须走注册表。

## 六、内存所有权

普通分支 `using var command = new MotionCommand{...}`（在 foreach 内逐卡新建，`using` 覆盖提前 return）：
命令对象自带 `Completion` 事件句柄，由步骤 Dispose；基类在命令收尾后不再引用它，提前释放安全。
急停分支**不造命令对象**（直接走 `device.EmergencyStop()`），没有可释放资源。
`targets` 里的设备与 `mapping` 都不属于本插件：设备由 `IMotionProvider` 创建与释放，映射属方案对象。

## 七、并行安全声明

可放进并行分组：插件实例无状态；`Enqueue` 在基类锁内写队列、打断在锁外；
`EmergencyStop` 由基类保证多次调用的幂等语义。要提醒编排者的是：
两个并行分支各下"停自己的轴"没问题，但**任何一侧勾了急停且 `Axis` 留空**，停的是所有卡的全部轴
（包括另一分支刚下发的运动）——这是"全停"的定义，不是竞态 bug。

## 八、A 组不变量（违反就编译/闸门红）

| 症状（改坏了会看到什么） | 机理 | 守门断言名 |
| --- | --- | --- |
| "停哪根轴由上游决定"失效 | 端口没读 `ActualValue` | `★每个插件的「轴 / 卡」都有可链接的输入端口（只靠 [StepConfig] 常量的话，编辑流程时赋不了值——它不暴露为端口）`、`★端口在运行期真的被读取（有端口却只读常量 = 摆设，链接上去也不生效）`（`FlowCanvasChecks\MotionZMotionChecks.cs`） |
| 老流程编译报"必填参数未配置"；或"留空 = 全停"被必填校验拦死 | 轴名口必须 `IsRequired = false` | `轴端口 IsRequired=false（老流程只填常量，不能因为没链接就判编译失败）` |
| 轴名下拉空白/退回手打 | `OptionKind` 没声明 | `★运动端口声明了 OptionKind（候选项随方案变化，不能在扫描期取一次就存下来）` |
| 急停只会清队、打断不了正在跑的回零 | 基类急停取消源被改动 | `★急停能打断正在执行的命令（回零立刻结束，而不是等它跑完）`、`★急停清空待执行队列（排队的 Move 全部被取消）`、`急停被接受`（`FlowCanvasChecks\MotionChecks.cs`） |
| 急停把卡打进安全停机、后续全被拒 | "被急停打断"被错记成设备故障 | `急停是预期操作：不记成设备故障、也不进安全停机` |
| 手册被顶掉/串页 | Help 资源没进 DLL | `HM4 有 Help/*.md 的工程都带 EmbeddedResource（手册进得了 DLL）`、`HM2 多插件程序集不许放扁平手册（防串页）`、`HM3 Help 子目录名逐字等于插件类名（分册能被认出）`（`FlowCanvasChecks\HelpManualCheck.cs`） |

## 九、B 组不变量（不报错，只出诡异现象）

| 症状 | 机理 | 守门断言名 |
| --- | --- | --- |
| "停止"排在回零后面、轮到它时轴早走完了 | 停止不再是打断性命令（基类 `IsInterrupting` 集合被改） | 无直接断言钉步骤侧；队列打断链路由 `回零执行期间，后续命令在排队等待` + `★急停能打断正在执行的命令…` 反证，改动基类时必须重审 |
| 报警态下本步骤永远被拒，"异常分支 → 轴停止"形同虚设 | 有人把"报警态也放行"的逻辑挪进步骤层、或把急停判据改错 | `★报警态拒绝运动命令（不会对着报警中的轴下发 Move）`（普通队列口径）；急停放行侧靠人工核对，改动门禁时两条都要回归 |
| 部分卡失败但 `Accepted=true` | 逐卡循环里漏了"任一拒绝立即 Fail"，或 `Accepted` 没在开头清零 | 无断言，靠人工核对（时间线第 2、5、9 步） |
| 拒绝原因只剩"被拒绝（Rejected_NotConnected）"半句话 | 没拼 `LastFault?.Suggestion ?? StateDetail` | 无断言，靠人工核对 |
| 把本步骤改成"等停稳"（WaitsForCompletion=true） | 停止变阻塞，异常分支里"越急越慢"；且等待用的是命令超时，停稳与否其实要看快照 | 无断言（语义变更），改前必须回读用户页"不等停稳"的整段口径并同步 |
| `Mode` 存档被写成中文显示名 | 显示与值必须分家（存档成员名、`Enum.Parse` 解析）；写中文 = 改一次显示名打断所有老流程 | `★属性面板共用生成器清单里注册了动态候选生成器（抽到 Defaults 后不可能漏一边）`（值/显示分家的机制闸门） |

## 十、改完这个文件必须做的动作

| 改了什么 | 必须同步 |
| --- | --- |
| "留空 = 全停"的判据 | 用户页三处形态说明、本页第三节、排障页的 `（若要停全部轴，请把轴名留空）` 文案表 |
| 急停的目标语义 | 用户页第 4 条原理（`Mode` 被忽略、`Axis` 只决定打哪几张卡）与并行安全声明 |
| `MotionStopMode` 成员/数值 | 驱动侧映射（正运动 `Single_Cancel` 模式号）、03 页存档口径、帮助手册 |
| 错误文案 | `05-troubleshooting.md` 的原文对照（逐字一致，改了不改手册就是假绿） |
| 基类门禁/打断集合 | `MotionChecks` 的急停与报警断言簇（改基类必跑该文件） |
