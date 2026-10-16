# 轴卡 IO：架构、宿主契约与不变量

> 分类：运动控制 ｜ 来源：插件 Plugin.Motion.Steps ｜ 读者：开发

这一页讲三件事：读写两条路径各自的一致性由谁保证、这个步骤白拿了哪些契约层行为、
以及改 `MotionIoPlugin.cs` 时哪些写法一改就会出事。用户页的"怎么填"这里不重复。

## 一、这个插件在分层里的位置

`MotionIoPlugin.cs`（137 行）是步骤层里唯一的**卡级**算子：它不解析轴，直接对着一张卡的端子读写。
全文没有厂商 API，读写分别落在"快照"与"命令队列"两条既有管线上。

| 层 | 位置 | 与本页相关的职责 |
| --- | --- | --- |
| 契约层 | `Shard\Core.Interfaces\Motion\IMotionDevice.cs` | 规定"状态查询一律读轮询缓存"，`ReadInput` 因此是零通信的 |
| 契约层 | `MotionDeviceBase.cs` | `_inputCache` 与 `UpdateInput`（轮询线程写）、`CheckGateLocked` 的输出点校验、命令队列 |
| 契约层 | `MotionCapabilities.cs` | `IsInputValid` / `IsOutputValid`：点数大于 0 且点号在 `0~N-1`，能力未知时两者都返回 false |
| 契约层 | `MotionDescriptor.cs` | `Params.CommandTimeoutMs`（命令超时口径与轴命令同源） |
| 驱动层 | `Plugin.Motion.ZMotion\ZMotionCard.cs` | `GetInMulti` 批量回填输入、`SetOp` 落输出 |
| 驱动层 | `Plugin.Motion.Virtual\VirtualMotionCard.cs` | 8 入 8 出的仿真能力，无卡也能跑通流程 |
| 步骤层 | 本工程 | 读写分岔、Alarm 态放行写入这两条判断**只在步骤层表达** |

## 二、RunAlgorithm 逐步（本步骤的执行时间线）

| 序 | 源码位置 | 动作 | 为什么在这个位置 |
| --- | --- | --- | --- |
| 1 | :71 | `cardName = Card.ActualValue ?? string.Empty` | 只认端口，不认字段快照；链接值优先于手填值 |
| 2 | :73-74 | `Accepted.Value = false` 与 `InputValue.Value = false` | 两个输出口在入口一起清：失败路径也必须让下游拿到明确的假，而不是上一轮的真 |
| 3 | :77 | `MotionAxisResolution.TryResolveDevice(cardName, …)` | 卡级入口，与轴级入口 `TryResolveAxis` 是两扇门，报错文案也不同 |
| 4 | :79 | `Fail(resolveError)` | 解析层已经把"没就绪 / 找不到卡"的处置建议写好，步骤层不再拼句子 |
| 5 | :83 | `ioPort = IoPort.GetTypedValue()` | 端口是非泛型 `InputPort` 持有，取值要显式转一次 |
| 6 | :85 | `Direction == MotionIoDirection.ReadInput` 分岔 | 枚举成员比较；`Direction` 是 `[StepConfig]`，运行期不会变 |
| 7 | :87 | `Capabilities.IsInputValid(ioPort)` | 读路径的唯一合法性校验，越界直接失败 |
| 8 | :89-92 | 按 `DigitalInputCount` 是否为 0 分两种提示后 `Fail` | 能力未上报（未连接）与点号真越界是两种处置，不能混成一句 |
| 9 | :98 | `var readValue = device.ReadInput(ioPort)` | 先落局部变量：`OutputPort<T>.Value` 是弱类型入口，拿它做判断会绕（源码注释在 :96-97） |
| 10 | :99-101 | 赋值、`Success.Value = true`、日志后 `return` | 读路径到此结束，全程不碰队列 |
| 11 | :106 | 写路径状态门禁：Online 或 Alarm 才放行 | 见第六节，这是全家族最宽的一条状态门禁 |
| 12 | :112-119 | `using var command = new MotionCommand { Kind = SetOutput, … }` | `WaitsForCompletion = false`，超时取 `Params.CommandTimeoutMs` |
| 13 | :121-126 | `Enqueue`，非 Accepted 则 `Fail` | 建议文本回落顺序 `LastFault?.Suggestion ?? StateDetail`（见下） |
| 14 | :128-130 | `Accepted.Value = true`、日志 `command.Describe()` | 描述串形如 `输出 5 ← ON`，与调试面板同一口径 |
| 15 | :133, :135 | `Initialize()` / `Dispose()` 空实现 | 本步骤无长生命资源；命令由 `using` 收口 |

## 三、读写两条路径的一致性由谁给

读与写在这个插件里不是"同一件事的两个方向"，而是**两条管线**，理解这一点才算理解本文件：

| 维度 | 读输入 | 写输出 |
| --- | --- | --- |
| 是否发起通信 | 否，读 `ConcurrentDictionary` 快照 | 是，命令进队列由执行线程落卡 |
| 时效 | 最坏滞后一个 `PollIntervalMs` | 入队即返回，落到端子在将来 |
| 卡状态要求 | 不检查连接状态，只看点号与能力 | 步骤层容 Online 与 Alarm；契约层只容 Online |
| 越界校验落点 | 步骤层调 `IsInputValid` | 契约层调 `IsOutputValid`（步骤层不重复校） |
| 输出信号 | `InputValue` | `Accepted` |
| 可观测到的失败 | 点号非法 | 拒绝、执行失败本步骤都看不到（不等执行） |

由此得出一条容易被误改的结构决定：**读不做连接检查**。快照在断连时会被清空（`_inputCache` 随断开清理），
读出来就是假；此时报"点号无效"比报"卡未连接"更误导人，所以宁可让调用方看到假并靠上游状态自己判断。
若有人给读路径补一条状态检查，就等于把语义换成"连接时才允许查快照"，与调试面板的行为会分叉。

## 四、状态真源与快照的时效

| 状态 | 真源 | 本步骤如何触及 |
| --- | --- | --- |
| 端子实际电平 | 卡侧硬件 | 读只拿到轮询快照；写只提交请求 |
| 输入快照 | `MotionDeviceBase._inputCache`，由驱动在 `PollStatusCore` 里 `UpdateInput` 回填 | `device.ReadInput` |
| 能力（点数） | 连接成功后 `SetCapabilities` 回填一次 | `IsInputValid` / `IsOutputValid` 的范围来源 |
| 队列与命令态 | `MotionDeviceBase` 的队列 | 只有 `Enqueue` 的返回值可见 |
| 步骤成功与否 | `Success` / `Fail` | 断连、越界、拒绝都会置失败；执行侧失败不会 |

`ZMotion` 的输入回填是**批量**的（`GetInMulti` 一次读 `0~count-1`），注释写明理由：10ms 周期逐点问 24 点会把自己打满。
同时它把读输入的异常吞掉且**不判失联**——轴状态才是安全相关信号，输入少刷一轮不影响安全判断。
这意味着"某个输入点长期 OFF"可能是通信抖动而非现场没信号，排查时要跟调试面板对照。

## 五、白拿的宿主 / 契约行为（作者不用写，也别重写）

| 行为 | 提供方 | 位置 |
| --- | --- | --- |
| 卡候选实时生成、值等于卡名本体 | `MotionModule.RegisterStepConfigOptions` 的 `MotionCardName` 分支 | `VisionMaster\Modules\MotionModule.cs:155-191` |
| 同名卡只列一项、按卡名解析回设备 | 同上（候选值用 `g.Key.Trim()`） | 同上 |
| 卡名找不到时给中文处置建议 | `MotionAxisResolution.TryResolveDevice` | `Shard\Core.Interfaces\Motion\MotionAxisResolution.cs` |
| 输出点越界拒绝、状态门禁、队列上限 | `MotionDeviceBase.CheckGateLocked` | `MotionDeviceBase.cs:632-636` 等 |
| 日志里的命令描述串 | `MotionCommand.Describe()` | `MotionCommand.cs:104-118` |
| 下拉形态、隐藏链接与清除按钮 | `LinkableValueEditor` 的 `IsOptionPort` | `Shard\Core.Controls\LinkableValueEditor.xaml.cs:225, :345, :356` |
| 无自定义视图时自动生成参数面板 | `AutoPortConfigView`（只列输入端口） | `VisionMaster\Views\DialogViews\AutoPortConfigView.xaml.cs` |
| 写输出落硬件 | `ZMotionCard.ExecuteCommandCore` 的 `SetOp` 分支；虚拟卡直接改内存态 | 驱动层 |

`Card` 端口在 `LinkableValueEditor` 里被判成选项口（`IsFunctionalEnum` 为真），因此链接与清除按钮都隐藏：
卡级操作连"由上游变量给卡名"都不允许，这是刻意的收紧，不是遗漏。

## 六、写路径状态门禁为什么比契约层宽

步骤层 :106 放行的状态是 Online 与 Alarm 两态，而契约层 `CheckGateLocked` 只让 Online 通过。
这不是矛盾，是**分工**：报警态下清报警前的输出动作（例如先把夹具松开、把有料信号打出去）在现场是需要的，
卡侧通信也仍然可用；但真正运动相关的命令必须在契约层被统一挡下。于是：

- 步骤层宽 → 让输出在报警态仍可下发；
- 契约层严 → 报警态下 `Enqueue` 返回 `Rejected_NotConnected`，本步骤报 `写输出命令被拒绝（Rejected_NotConnected）：…`。

改这条判断前先看守门断言 `★报警态拒绝运动命令（不会对着报警中的轴下发 Move）`：
把契约层的门禁改成"也容 Alarm"会让那条断言红，而把步骤层改成"只容 Online"会让报警态松开夹具的现场动作消失。
拒绝文案的回落链 `LastFault?.Suggestion ?? StateDetail` 同样不能简化成只取一个：报警态下 `LastFault` 有建议，
未连接时只有状态说明，缺一个分支就会打印空白。

## 七、`PhysicalAxis` 留默认值这件事

命令对象只填 `IoPort` 与 `IoValue`，`PhysicalAxis` 保持 `MotionCommand.cs:33` 的默认 `-1`。
这是安全的，因为 `CheckGateLocked` 的轴号校验枚举里**不含** `SetOutput`（只列 MoveAbsolute、MoveRelative、Home、Enable、Disable）。
一旦有人"顺手把 SetOutput 也加进轴号校验"，本插件所有写输出都会以 `轴号 -1 超出范围…` 被拒。
`Stop` 的注释（"允许 PhysicalAxis = -1 表示全部轴"）说明负一是这套模型的合法取值，而不是本步骤漏填。

## 八、A 组不变量（违反就编译失败或闸门报红）

| 不变量 | 机理 | 守门断言 |
| --- | --- | --- |
| 必须有 `InputPort<string> Card` | IO 是卡级操作，没有轴名可借，缺它就无从配置 | `★每个插件的「轴 / 卡」都有可链接的输入端口（只靠 [StepConfig] 常量的话，编辑流程时赋不了值——它不暴露为端口）`（该断言对 `MotionIoPlugin.cs` 特殊处理：要求 Card 而非 Axis） |
| 必须真的读 `Card.ActualValue` | 只读字段快照时链接不生效，端口成摆设 | `★端口在运行期真的被读取（有端口却只读常量 = 摆设，链接上去也不生效）` |
| 卡上不得引入地址类寻址 | 换网段或换卡即失效，且改设备名会打断已配流程 | `★运动插件里不再出现「卡地址」寻址（MotionCardAddress / CardKey 已删除）` |
| 候选值必须是稳定键 | 显示可带设备名，写进流程的值要能解析回设备 | `★卡候选的值仍是稳定键（按卡名寻址；显示可以带设备名，但写进流程的必须能解析回设备）` |
| 无自定义视图也要能配参数 | 本插件没有 `GetConfigView`，靠框架自动面板列出输入端口 | `★没有自定义视图的插件也能拿到输入端口（拿不到的话参数面板就是一片空白）` |
| `MotionIoDirection` 成员要有中文名 | 该枚举已标 `Display(Name)`；缺了会退回成员名 | `★枚举下拉显示 [Display] 的中文名`、`★几何两插件 + 运动三步骤的枚举成员逐个有中文显示名` |
| 手册结构 | 目录名逐字等于类名、页号前缀、每页首行一级标题、开发页行数达标 | `HelpManualCheck` 的 HM2 / HM3 / HM4 / HM5 / HM6 / HM12 / HM12b / HM13 |

## 九、B 组不变量（不报错，只出诡异现象）

| 不变量 | 破坏后的现象 |
| --- | --- |
| 入口先清 `Accepted` 与 `InputValue` | 本步骤失败时下游拿到上一轮的真值，于是"失败流程仍然驱动了夹紧动作" |
| `Accepted` 只表示入队成功 | 把它当"端子已动作"用于互锁，会在真正落卡前就放行下一步 |
| 读值取进局部变量再赋出口 | 直接拿 `OutputPort<T>.Value` 判断，弱类型入口会让比较悄悄失效 |
| 读的是快照 | "未采到"与"现场为 OFF"同为假；需要区分时得看轮询是否健康，不能改本步骤去现场问 |
| 点号 0 基，范围来自能力 | 按丝印 1 基填号会整体错位一个点；能力未回填时范围校验直接判非法 |
| 归档键是端口名 `Value`，属性名是 `WriteValue` | 有人为命名整洁把属性改成 `Value`，会与基类成员撞车，解析到的不是这个端口，且老流程的键会失联 |
| `Direction` 是 `[StepConfig]` | 参数面板看不到它，用户会以为"只能写不能读"；这是已记录的界面欠账，不是流程坏了 |
| 写不等执行、也不看执行结果 | 驱动侧 `SetOp` 失败只在设备日志里出现（`写输出失败（正运动错误码 N）`），本步骤仍是绿的 |

## 十、并行安全声明

| 关注点 | 现状 |
| --- | --- |
| 步骤实例 | 每次运行在各自线程执行，插件实例不跨流程共享，本文件无静态可变状态 |
| 快照读写 | `_inputCache` 是并发字典，轮询线程写、步骤线程读，无锁也无撕裂 |
| 命令对象 | 局部生成、`using` 收口；`WaitsForCompletion` 为假时执行线程只读它 |
| 队列 | `Enqueue` 在契约层的入队锁内做门禁，多步骤并发写输出天然串行 |
| 顺序保证 | 与轴命令同一条队列，"先动轴再夹紧"由队列顺序天然成立（源码注释 :14-15 就是这条契约） |

## 十一、改完这个文件必须同步的地方

| 改动 | 必须同步 |
| --- | --- |
| 增删端口或改端口名 | 归档兼容（`.vms` 里存的是端口名）、`03-ports-and-parameters.md`、若属卡级候选再核 `MotionModule` |
| 调整状态门禁宽严 | 第六节的分工说明与 `★报警态拒绝运动命令` 断言、`05-troubleshooting.md` 的拒绝条目 |
| 改 `Direction` 语义或增加第三种动作 | 枚举 `Display`、`LinkableValueEditor` 的下拉候选、用户页两张表 |
| 让写路径改等待执行结果 | 语义从"下发"变成"到位"，与 `MotionMovePlugin` 的"下发不等"口径分叉，两页文案都要改 |
| 换读路径为现场通信 | 会破坏"状态查询一律读缓存"的契约层约定（`IMotionDevice.cs:22`），且 10ms 轮询下拖慢流程 |
| 页面本身 | 首行 `# `、无 BOM 的 UTF-8、LF 换行、页号前缀、目录名与类名逐字一致 |
