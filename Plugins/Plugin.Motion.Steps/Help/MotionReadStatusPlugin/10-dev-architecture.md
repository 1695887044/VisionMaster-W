# 读轴状态：架构、宿主契约与不变量

> 分类：运动控制 ｜ 来源：插件 Plugin.Motion.Steps ｜ 读者：开发

S 档一页承担三个角色：架构与数据流、宿主白拿的行为、改这个文件时的红线。
用户页讲的"怎么填"这里不重复，只讲**这些行为由谁提供、为什么这么写、写错会怎样**。

## 一、这个插件在分层里的位置

运动控制三层，本文件属于最薄的那一层：

| 层 | 位置 | 职责 |
| --- | --- | --- |
| 契约层 | `Shard\Core.Interfaces\Motion\` | `MotionDeviceBase`（轮询线程产出 `AxisStatus` 快照）、`MotionAxisResolution`（轴名解析） |
| 驱动层 | `Plugin.Motion.ZMotion` / `Plugin.Motion.Virtual` | 决定快照里**哪些位有人填**（例：正运动不读速度 → `VelocityMmPerS` 恒 0） |
| 步骤层 | 本工程 7 个插件类 | 解析名字 → 读快照 → 按同一份快照写输出 |

`MotionReadStatusPlugin.cs` 全文 110 行，没有一行 SDK 相关。它与「等待到位」同属"纯观察者"：
全程不入队命令、不下发通信。区别是：**单点采样，不循环、不等停稳**。

## 二、RunAlgorithm 逐步（执行时间线）

| # | 代码动作 | 为什么必须在这一刻做 |
| --- | --- | --- |
| 1 | `Axis.ActualValue ?? string.Empty` | 端口取值一次成型（链接值优先于手填值） |
| 2 | 九个输出**全部清零**（数值 0、布尔 false、`StatusText` 空串） | 上一轮的旧值不能伪装成本轮读数；失败路径下游据此可判"全是初始值 = 本轮没读到" |
| 3 | `MotionAxisResolution.TryResolveAxis(...)` | 一次拿到"设备 + 轴配置"；失败文案由解析器统一给 |
| 4 | `device.GetAxisStatus(mapping.PhysicalIndex)` | 读轮询线程刷好的快照引用，**不发起通信**；本轮九个字段全部取自这**同一份**快照，天然自洽 |
| 5 | `status == null → Fail`，文案带 `device.State` 与 `StateDetail` | 快照为空 = 还没轮询到或已断开。给"未知"而不是编一个 0 位置（把未知当 0，下游会拿猜测值做判断且看不出是猜的） |
| 6 | 九个字段逐一写回，`StatusText.Value = status.Describe()` | `Describe()` 是快照上的同一优先级摘要（报警 → 急停 → 正限位 → 负限位 → 运动中 → 未使能 → 已到位/裸位置），与「等待到位」报错里拼的是**同一个方法**——两处说法不会打架 |
| 7 | `FailOnAlarm && status.HasCriticalSignal → Fail`（写在全部输出之后） | **先写数、后判失败**：报警时下游照样能拿到完整现场（位置/限位位），只是本轮成败记 false。顺序颠倒就成了"报警时什么都读不到" |
| 8 | 失败文案拼 `device.LastFault?.Suggestion`（非空才拼） | 处置建议存在故障记录的 `Suggestion` 字段，漏拼就只剩半句话 |

## 三、状态有几份、谁是真源

| 状态 | 真源 | 本步骤读还是写 |
| --- | --- | --- |
| 轴快照九个字段 | `AxisStatus`（`MotionDeviceBase` 轮询线程按卡级 `PollIntervalMs` 刷新，驱动在 `PollStatusCore` 里 `UpdateAxisStatus`） | 只读 |
| "已回零"标记 | **卡级** `IsHomed`（基类维护，失联+增量编码器时归 false） | **不读不输出**——快照里没有这一位，本步骤也不输出它（见第八节 B 组） |
| 界面填的值 | 步骤 `InputValues`（`.vms`）+ `[StepConfig]` 配置段 | 基类 `ApplyConfigValues` 灌端口，`OnConfirm` 写回 |
| 本插件自己 | **无**（无实例字段、静态字段） | — |

**"读到坏消息"与"读取失败"的分界是本步骤的核心语义**：默认只有④（快照为空）算失败；
报警/限位是**数据**，交给流程分流。`FailOnAlarm` 是把这个选择权交给存档的开关，
判据统一用 `HasCriticalSignal`（急停输入/正限位/负限位/报警任一），与「等待到位」同一口径。

## 四、白拿的宿主/基类行为（作者不用写、也别重写）

| 行为 | 由谁提供 | 表现 |
| --- | --- | --- |
| 端口发现/灌值/写回/参数面板 | `VisionPluginBase` + 宿主 `AutoPortConfigView` | 面板只列输入端口；`FailOnAlarm` 这个 `[StepConfig]` 因此暂无界面入口 |
| 快照的产出与时效 | `MotionDeviceBase` 轮询线程（看门狗、失联清快照） | 本步骤只消费；"什么时候没有快照"由基类决定 |
| 一句话描述 | `AxisStatus.Describe()`（契约层） | 中文措辞在契约层统一，界面/日志/本端口三处同源 |
| 轴名解析 | `MotionAxisResolution.TryResolveAxis` | 未注入服务报 `运动卡服务尚未就绪：请在主程序内运行流程` |
| 轴名下拉候选 | `StepConfigOptionKind.MotionAxisName` 动态候选 | 随方案变化，不是扫描期冻结 |
| 流程失败语义 | `VisionPluginBase.Fail(...)` | `Success=false` + `ErrorMessage`，后续步骤照常执行 |

## 五、MotionDescriptor 参数口径（与其他 6 个步骤页同一份说法）

- 卡级（`MotionParams`）：`DefaultVelocityMmPerS` 50、`DefaultAccelMmPerS2` 500、
  `PollIntervalMs` 10（**决定本步骤能多快看到新读数**——快照刷新节奏）、`CommandTimeoutMs` 30000、
  `WatchdogTimeoutMs` 3000（失联判据，之后快照断供）、`AutoReconnect` true、`MaxQueueDepth` 256。
- 轴级（`AxisMapping`）：逻辑名、物理轴号（0 基）、启用位、`UnitsPerMm`（默认 1000，
  正运动连接时下发 `SetUnits`，之后 `Position` 直接是 mm）、软限位 ±1000000；
  轴配置弹窗 7 个低频字段（编码器类型/PPU、驱动器、额定电流、跟随误差上下限、备注）本步骤不读。
- **引用一律用 Id 不用显示名**：`MotionDescriptor.Id`（Guid）是内部稳定身份；
  报错里的卡名取 `Caption`（显示名 → 地址 → "(未命名运动卡)" 回退）。`ResolveAxisIndex` 已退役。
- 卡状态成员名与中文的对照在契约层 `MotionEnumDisplay`（Closed 未连接 / Connecting 连接中 /
  Online 在线 / Alarm 报警 / SafeStopped 已安全停机）；本步骤报错里 `当前为 {State}` 打的是**成员名**，不是中文。

## 六、内存所有权

不创建任何可释放对象。`AxisStatus` 是轮询线程每轮 new 的纯数据快照：步骤只持局部引用、
本轮内读完即丢，不跨轮缓存、不负责 Dispose。设备由 `IMotionProvider` 管，映射属方案对象。

## 七、并行安全声明

纯读 + 无实例状态，可放进并行分组；单点采样不占命令队列、不占等待线程，
多轴并排各放一步是推荐写法。读快照不发起通信，所以"读得频繁"不会打满网口——
限制采样新鲜度的是卡级轮询间隔，不是本步骤。

## 八、A 组不变量（违反就编译/闸门红）

| 症状（改坏了会看到什么） | 机理 | 守门断言名 |
| --- | --- | --- |
| "读哪根轴由上游决定"失效 | 端口没读 `ActualValue` | `★每个插件的「轴 / 卡」都有可链接的输入端口（只靠 [StepConfig] 常量的话，编辑流程时赋不了值——它不暴露为端口）`、`★端口在运行期真的被读取（有端口却只读常量 = 摆设，链接上去也不生效）`（`FlowCanvasChecks\MotionZMotionChecks.cs`） |
| 老流程编译报"必填参数未配置" | 轴名口必须 `IsRequired=false` | `轴端口 IsRequired=false（老流程只填常量，不能因为没链接就判编译失败）` |
| 轴名下拉空白/退回手打 | `OptionKind` 没声明 | `★运动端口声明了 OptionKind（候选项随方案变化，不能在扫描期取一次就存下来）` |
| 快照链路本身坏了（本步骤全红） | 基类轮询/快照回填被改动 | `连接成功且状态为在线`、`连接后能力已回填（3 轴 / 8 输入 / 8 输出 / 有硬限位）`、`轴使能命令完成，且设备回报已使能`（`FlowCanvasChecks\MotionChecks.cs`——三条都靠读快照收尾） |
| 手册被顶掉/串页 | Help 资源没进 DLL | `HM4 有 Help/*.md 的工程都带 EmbeddedResource（手册进得了 DLL）`、`HM2 多插件程序集不许放扁平手册（防串页）`、`HM3 Help 子目录名逐字等于插件类名（分册能被认出）`（`FlowCanvasChecks\HelpManualCheck.cs`） |

## 九、B 组不变量（不报错，只出诡异现象）

| 症状 | 机理 | 守门断言名 |
| --- | --- | --- |
| 上一轮的读数冒充本轮（成功失败都看不出） | 开头没把九个输出清零 | 无断言，靠人工核对（时间线第 2 步） |
| 卡断开时下游读到"位置 0、一切正常" | 快照为空却编造成功值，而不是判失败 | 无断言，靠人工核对（时间线第 5 步；"给未知而不是给 0"是刻意设计） |
| `FailOnAlarm` 打开后报警时**什么都读不到** | 判失败写在了写输出之前 | 无断言，靠人工核对（时间线第 7 步的"先写数、后判失败"） |
| 「等待到位」说报警、「读轴状态」说不报警，现场对不上话 | 两处硬信号判据分家（本应同用 `HasCriticalSignal`，摘要同用 `Describe()`） | 无跨步骤一致性断言；改动 `AxisStatus` 判据/文案时两个文件必须一起回归 |
| 用 `Velocity=0` 判停，真机上永远"停了" | 正运动驱动不读速度寄存器，字段恒 0——**驱动决定字段有没有人填**，仿真给了数会制造"看起来能用"的假象 | `轮询推进后轴真的走到目标（位置 ≈ 20mm 且到位）`只钉仿真推进；`Velocity` 口径靠用户页与本页反复声明，无断言 |
| 想拿"回没回零"往快照里加一位 | "已回零"是**卡级**状态（失联+增量式时由基类归 false），不是轴位；塞进轴快照会造成"每轴各自知道"的假象 | `★增量编码器失联后 IsHomed 归 false（位置不可信，必须重新回零）`、`增量式编码器：连接后位置不可信（IsHomed=false）`、`回零后 IsHomed=true`（卡级语义的三条反证） |

## 十、改完这个文件必须做的动作

| 改了什么 | 必须同步 |
| --- | --- |
| 输出端口增删 | 用户页端口速览与 `03` 逐端口表；下游已连线的老方案会静默失去来源（端口名是连线键） |
| `FailOnAlarm` 判据面 | 「等待到位」的硬信号判据（两处必须同口径）+ 本页第九节第 4 行 |
| `Describe()` 措辞（契约层） | 用户页的原文示例、排障页的对照、`05` 里引用它的所有句子——契约层文案一改，三个步骤的手册都要跟着核 |
| 错误文案 | `05-troubleshooting.md` 原文对照（逐字一致，改了不改手册就是假绿） |
| 新增"驱动不填的字段" | 用户页 `Velocity` 那条 Q&A 的口径要扩列（哪些字段在哪些卡上是摆设） |
