# 轴回零：架构、宿主契约与不变量

> 分类：运动控制 ｜ 来源：插件 Plugin.Motion.Steps ｜ 读者：开发

S 档一页承担三个角色：架构与数据流、白拿的宿主/契约行为、改这个文件时的红线。
用户页讲的"怎么填"这里不重复，只讲**这些行为由谁提供、为什么这么写、写错会怎样**。

## 一、这个插件在分层里的位置

运动控制三层，`MotionHomePlugin.cs`（135 行）属于最薄的那一层：

| 层 | 位置 | 职责 |
| --- | --- | --- |
| 契约层 | `Shard\Core.Interfaces\Motion\` | `MotionDeviceBase`（队列 / 四道门禁 / 看门狗 / 状态机 / `MarkHomed`）、`MotionAxisResolution`（轴名解析）、`MotionCommand`、`MotionCapabilities`、`MotionEnumDisplay` |
| 驱动层 | `Plugin.Motion.ZMotion` / `Plugin.Motion.Virtual` | 只写"这台设备怎么说话"：`ConnectCore` / `PollStatusCore` / `ExecuteCommandCore`，外加厂商模式码映射 |
| 步骤层 | 本工程 7 个插件类 | 把一次设备交互摊成流程节点：解析名字 → 状态门禁 → 能力协商 → 造命令 → 入队 → 等 → 回报 |

步骤层**没有一行 SDK 相关**：它不知道正运动、也不知道雷赛，只认 `IMotionDevice.Enqueue(MotionCommand)`。
这是刻意的高度差 —— 步骤里一旦出现 `ZAux_*`，换一家卡就要改 7 个流程插件。

## 二、RunAlgorithm 逐步（本步骤的执行时间线）

| # | 代码动作 | 为什么必须在这一刻做 |
| --- | --- | --- |
| 1 | `var axisName = Axis.ActualValue ?? string.Empty` | 端口的实际生效值在"读的这一刻"定稿（链接值优先于手填值）；`?? string.Empty` 只为挡 null，**空串刻意不兜成默认值**，让它走到解析器给出"没有指定轴名" |
| 2 | `Homed.Value = false` | **开头清零**：本轮失败时下游不能拿到上一轮的 true。流程可重复执行，输出端口不是"曾经成功过"的存证 |
| 3 | `MotionAxisResolution.TryResolveAxis(...)` | 一次调用同时拿到"设备 + 轴映射"；失败原因文本由解析器给出，步骤**不编自己的措辞**（同一句话在 7 个步骤里必须一致） |
| 4 | `device.State != MotionCardState.Online` → `Fail` | 回零比使能严：报警态**不放行**。这里只判一个"非在线即拒"，冒号后直接用 `StateDetail`，步骤不再自己列举状态文案 |
| 5 | 步骤侧**不做**能力协商；`device.Enqueue` 时由设备门禁 `CheckGateLocked` 检查 `SupportsHomeMode` | 能力协商只留一个落点（设备门禁）：两套判据必然漂移。拒绝原因由门禁拼出"它支持的是：…"清单，步骤原样拼进 `ErrorMessage` |
| 6 | `var timeout = Math.Max(1000, TimeoutMs)` | 下限写在步骤侧，而不是让基类去猜；1000 ms 是"寻零动作的物理下限"，比它小的超时只会变成噪声 |
| 7 | `new MotionCommand { Kind = Home, HomeMode = Mode, WaitsForCompletion = true, Retryable = false }` | 三个语义都在这里定：等（后面必须站在零点上）、**不许重试**（失败时轴可能正压在开关上，再来一次可能撞机械原点）、厂商码由驱动映射 |
| 8 | `device.Enqueue(command)` 后判 `!= Accepted` | `Accepted` 只代表"收进队列"；拒绝时把 `LastFault?.Suggestion` 拼进文案（**闸门的原因就写在这个字段里**，漏拼就等于只报一个成员名） |
| 9 | `command.Completion.Wait(timeout + 1000)` | 多给 1000 ms 余量：命令内部的超时判据与这里的等待不必严丝合缝，**先到者胜** |
| 10 | `switch (command.State)`：`Done` / `Canceled` / `default` | 三条分支各说一件事："等到并完成"、"被打断（不是设备故障）"、"等到的不是完成"（`Failed` / `TimedOut`）。把 `Canceled` 并进 default 会把急停说成回零失败 |
| 11 | `Homed.Value = true; Success.Value = true; Logger.Info(...)` | 只有 `Done` 才落 true；日志带上方式成员名，现场拿这行去对厂商手册最快 |

## 三、状态有几份、谁是真源

| 状态 | 真源 | 本步骤读还是写 |
| --- | --- | --- |
| 卡状态机（Closed / Connecting / Online / Alarm / SafeStopped） | `MotionDeviceBase`（轮询线程 + 看门狗 + 急停） | 读 `State` / `StateDetail` |
| 是否已回零 `IsHomed` | 基类在 `Done` 时 `MarkHomed(true)`；失联/断线时按能力位归 false | **本步骤不读**（本步骤自己报 `Homed`，基类那一份是给调试面板与后续 Move 门禁用） |
| 是否正在运动 `Moving` | 驱动轮询回填的轴状态快照 | **步骤不读**；设备门禁的 Home 前置条件读它（只挡 `Moving`） |
| 命令生命周期 | `MotionCommand`（队列 + `Completion` 事件 + `State` / `Error`） | 造、入队、等，随后 `using` 释放 |
| 能力表（含支持的 `HomeMode` 清单） | 驱动在连接成功后回填的 `MotionCapabilities` | 读（协商） |
| 界面填的值 | 步骤的 `InputValues`（`.vms` 里） | 由基类 `ApplyConfigValues` 灌进端口与配置属性，`OnConfirm` 写回 |
| 本插件自己 | **无**（没有实例字段、静态字段、缓存） | — |

"插件里没有自己的状态"是这 7 个步骤的共同前提：流程可能被并行分组调度，插件实例上的任何可变字段都是竞态源。

**一条容易被忽略的顺序契约**：基类是"先 `MarkHomed(true)`、**再** `CompleteCommand`"。
完成信号的语义是"这条命令的一切都结束了"，顺序反过来时等待方会读到"命令已完成但 `IsHomed` 仍为 false"的中间态
—— 而"回零完成后位置才可信"恰恰是紧接着就要用的结论。改基类这段顺序会把回零后的第一条运动推到错误基准上。

## 四、白拿的宿主 / 基类行为（作者不用写、也别重写）

| 行为 | 由谁提供 | 表现 |
| --- | --- | --- |
| 端口与配置属性发现 | `VisionPluginBase` 反射 public 属性 | 端口名可省略（自动取属性名）；本步骤 `Axis` 显式写了同名，`Mode` / `TimeoutMs` 走 `[StepConfig]` |
| 灌值 / 写回 | `ApplyConfigValues` + `OnConfirm` | 面板显示**已保存值**；`InputValues` 的键 = 端口名（`Axis`）或属性名（`Mode`、`TimeoutMs`），枚举值按成员名往返 |
| 参数面板 | 宿主 `AutoPortConfigView`（`StepParameterDialog` 在没有自定义视图时包一层） | **只列输入端口、不列 `[StepConfig]`** —— 这就是本步骤的 `Mode` 没有编辑入口的原因（不是漏实现，是面板形态的既有约束） |
| "只能选"下拉 | `LinkableValueEditor`：`IsOptionPort = DataType.IsEnum` 或 `IsFunctionalEnum` | `Axis` 因为显式 `IsFunctionalEnum = true` 落进这一档，链接/清除按钮都不出现 |
| 动态候选 | `StepConfigOptionSource` + 宿主 `MotionModule` 注册表查询 | `OptionKind = MotionAxisName` 的候选来自**全局轴注册表**（只列 `Enabled` 且解析得到的名字），扫描期取一次存不下来 |
| 命令排队 / 急停打断 / 能力校验 / 软限位 / 运动前置条件 | `MotionDeviceBase.Enqueue` + `CheckGateLocked` | 步骤侧只需处理 `Accepted` 与否；能力协商与"只挡 Moving"都在门禁里，步骤不再重复 |
| 取消与打断的区分 | 基类的 `_emergencyPending` | 急停打断的命令不记故障、不进安全停机，步骤侧只会看到 `Canceled` |
| 流程失败语义 | `VisionPluginBase.Fail(...)` | `Success = false` + `ErrorMessage`，后续步骤照常执行（要不要停是流程的事） |

## 五、内存所有权

`using var command = new MotionCommand { ... }`：命令对象自带 `ManualResetEventSlim`（`Completion`），
**由步骤 Dispose**，`using` 覆盖所有提前 `return` 分支（本步骤有五个提前出口，少一个就是句柄泄漏）。
基类在命令完成或取消后不再引用它，所以提前释放安全；漏掉则长跑流程累积。
`device` 与 `mapping` **不属于**本插件：设备由 `IMotionProvider` 创建与释放，映射是方案对象的一部分，步骤只读不释放。
`SupportedHomeModes` 是 `IReadOnlyList<HomeMode>`，协商只读、不缓存副本 —— 卡重连换型后必须看到新清单。

## 六、`HomeMode` 的两套码（改这里之前必读）

契约枚举 `HomeMode`（C# 值 0~7）**只是稳定标识**，与厂商码没有任何数值关系：

| 成员 | C# 值 | 正运动 `homemode`（`ZMotionHomeModes.ToSdk`） |
| --- | --- | --- |
| `NegativeLimitIndex` | 0 | 1 |
| `PositiveLimitIndex` | 1 | 2 |
| `Origin` | 2 | 19 |
| `NegativeLimitOrigin` | 3 | 27 |
| `PositiveLimitOrigin` | 4 | 23 |
| `PresetZero` | 5 | 37 |
| `NegativeLimit` | 6 | 17 |
| `PositiveLimit` | 7 | 18 |

三条纪律：① **模式码只能来自厂商手册**，填错一个数字轴就按完全不同的寻零路径走（把"负限位 + Index"写成"正限位 + Index"，
轴直接朝反方向撞）；② 映射表**留在驱动里，不进配置、不进流程**，方案才能跨品牌迁移；
③ `ToSdk` 返回 `null` 时驱动报 `正运动驱动不支持回零方式 {成员名}`（`NotSupported`），
新增契约成员时**必须同时补 switch 分支**，否则漏映射是编译错误而不是静默走错路径。

**为什么 `HomeMode` 成员上没有 `[Display]`**（与 `MotionEnums.cs` 那三个枚举的做法不同，是有意的）：
契约枚举的中文说法统一收在 `MotionEnumDisplay.Text/Hint/AllHomeModes` 这份 switch 字典里 ——
特性要靠反射读、**漏标一个成员是静默的**，switch 表达式漏成员编译器直接报错；
且成员名要保持与厂商手册可对照，中文属于展示层，不塞进契约定义。
后果：界面上凡是读 `[Display]` 的那条路（`StepConfigOption.ForEnum`）在这里拿不到中文，
报错文案里出现的是成员名。调试面板的回零下拉走的是 `MotionEnumDisplay`，所以那边是中文。
**别为了"让下拉显示中文"给契约枚举补 `[Display]` 而留着字典** —— 两份中文会漂移，现场对不上话。

## 七、A 组不变量（违反就编译/闸门红）

| 症状 | 机理 | 守门断言名 |
| --- | --- | --- |
| 流程里"轴由下拉选"失效，候选一片空白 | 端口没标 `OptionKind` / `IsFunctionalEnum`，编辑器认不出这是"方案候选"口 | `★全部 {N} 个运动插件的「轴」参数都标了动态候选`、`★运动插件里不再出现「卡地址」寻址（MotionCardAddress / CardKey 已删除）`（`FlowCanvasChecks\MotionZMotionChecks.cs`） |
| 口声明了却不生效，链接上去读到的还是常量 | 只读常量、从不读 `ActualValue` | `★每个插件的「轴 / 卡」都有可链接的输入端口（只靠 [StepConfig] 常量的话，编辑流程时赋不了值——它不暴露为端口）`、`★端口在运行期真的被读取（有端口却只读常量 = 摆设，链接上去也不生效）` |
| 老流程一编译就报"必填参数未配置" | 轴名口必须 `IsRequired = false` | `轴端口 IsRequired=false（老流程只填常量，不能因为没链接就判编译失败）` |
| 不支持的回零方式跑到一半才失败（轴已经撞过去了） | 设备门禁的能力校验被删掉（P1-A 起唯一落点） | `★不支持的回零方式在入队时就被拒绝（而不是运行到一半才失败）`（`FlowCanvasChecks\MotionChecks.cs`） |
| 轴还在走就发回零，两个目标互相覆盖 | Home 的 `Moving` 前置条件被删 | `★运动中 Home 被拒`（`FlowCanvasChecks\MotionChecks.cs`）；反向由 `★未回零时 Home 不被拒` 钉住"只挡 Moving、不挡未回零" |
| 回零"成功"了位置却不是零点 | 驱动完成判据或基类 `MarkHomed` 顺序被动过 | `回零命令完成`、`回零后位置归零（坐标系重建）`、`回零后 IsHomed=true` |
| 手册被自动生成页顶掉、用户看到的不是我们写的那份 | Help 页没进 DLL / 放错目录 | `HM2 多插件程序集不许放扁平手册（防串页）`、`HM3 Help 子目录名逐字等于插件类名（分册能被认出）`、`HM4 有 Help/*.md 的工程都带 EmbeddedResource（手册进得了 DLL）`、`HM13 已交的开发章节达到所在档位的行数下限（L300/M150/S100，交了就不是随笔）`（`FlowCanvasChecks\HelpManualCheck.cs`） |

## 八、B 组不变量（不报错，只出诡异现象）

| 症状 | 机理 | 守门断言名 |
| --- | --- | --- |
| 本轮失败了，条件分支却按"已回零"走 | 开头没把 `Homed` 清零 | 无断言，靠人工核对（本文件第二节第 2 行就是这条） |
| 回零卡住时急停等它跑完 —— 那就不是急停 | 命令不可取消、或驱动等待不响应 `ct` | `★急停能打断正在执行的命令（回零立刻结束，而不是等它跑完）`（`FlowCanvasChecks\MotionChecks.cs`） |
| 人工按了急停被记成设备故障、卡还进了安全停机 | 把 `Canceled` 与 `Failed` 混成一条出口 | `急停是预期操作：不记成设备故障、也不进安全停机` |
| 断线重连后按旧坐标系继续走，撞限位 | 增量编码器的 `IsHomed` 没有归 false | `★增量编码器失联后 IsHomed 归 false（位置不可信，必须重新回零）` |
| 报错后半句是空白，用户不知道能换成哪种方式 | 拼 `SupportedHomeModes` 时没处理空清单 | 无断言，靠人工核对（空清单必须落到"尚未上报能力"那句） |
| 等待时间与命令内部超时差太小，偶发"步骤超时但卡其实刚完成" | `timeout + 1000` 的余量被改小或删掉 | 无断言，靠人工核对 |
| 把"报警态也允许回零"当成一致性顺手加宽 | 本步骤与「轴使能」的门禁集合**刻意不同**：回零不许在报警态发起 | `★报警态拒绝运动命令（不会对着报警中的轴下发 Move）`（同族判据） |
| 给 Home 顺手加上"必须先使能/必须先回零" | 把 Move 的三条前置条件套到 Home 上 | `★未回零时 Home 不被拒`、`★Home 不受「未使能」门禁影响`（`FlowCanvasChecks\MotionChecks.cs`） |

## 九、并行安全声明

可放进并行分组：插件实例无状态；`Enqueue` 在基类锁内写队列，打断动作在**锁外**执行
（`打断在入队锁之外执行（锁内 Cancel 会与命令线程抢同一把锁，等于自己等自己）`）；
协商读的是能力表快照、不发起通信。**但同一张卡上的回零实际是串行的**（命令 FIFO 执行），
两条轴"并行回零"排到同一队列里不会同时寻零 —— 这是队列语义，不是竞态 bug。

## 十、改完这个文件必须做的动作

| 改了什么 | 必须同步 |
| --- | --- |
| 端口/属性名或默认值 | `01-quickstart.md` 的默认值口径、本页第五节的存档键；`InputValues` 的键会变，老方案里的旧键静默失效 |
| 门禁状态集合（比如放开报警态） | `05-troubleshooting.md` 第二节的分流表与用户页"卡状态要求比别的步骤严"那段 |
| Home 的运动前置条件（只挡 Moving） | `05-troubleshooting.md` 的"正在运动"拒绝原文、`MotionChecks` 的 `★未回零时 Home 不被拒` / `★运动中 Home 被拒`；改判据必须改 `MotionDeviceBase.CheckMotionPreconditionsLocked`（唯一落点） |
| 等待/超时余量或 `Retryable` | 用户页的"本步骤会等待""绝不盲目重试"整段口径 |
| 错误文案 | `05-troubleshooting.md` 的原文对照表（那张表要求逐字一致，改了代码不改手册就是假绿） |
| 新增 `HomeMode` 成员 | 驱动 `ZMotionHomeModes.ToSdk`、各驱动 `SupportedHomeModes`、`MotionEnumDisplay` 三个 switch、本页第六节的码表 |
