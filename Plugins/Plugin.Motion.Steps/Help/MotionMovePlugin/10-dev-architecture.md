# 轴运动：架构、宿主契约与不变量

> 分类：运动控制 ｜ 来源：插件 Plugin.Motion.Steps ｜ 读者：开发

S 档一页承担三个角色：架构与数据流、白拿的宿主/契约行为、改这个文件时的红线。
用户页讲的"怎么填"这里不重复，只讲**这些行为由谁提供、为什么这么写、写错会怎样**。

## 一、这个插件在分层里的位置

`MotionMovePlugin.cs`（126 行）是步骤层里最能体现"高度差"的一个：它把一次点位运动摊成
解析 → 门禁 → 造命令 → 入队 → 回报，**全文没有任何厂商 API**。

| 层 | 位置 | 与本页相关的职责 |
| --- | --- | --- |
| 契约层 | `Shard\Core.Interfaces\Motion\` | `MotionCommand`（含 `Describe()` 的日志口径）、`MotionDeviceBase.CheckGateLocked`（状态 / 轴号 / 能力 / 软限位四道闸）、`MotionAxisResolution` |
| 契约层（端口） | `Shard\Core.Interfaces\DataPort\InputPort.cs` | `ActualValue` 的优先级（链接值 > 手填值）、`GetTypedValue()`、`IsFunctionalEnum`、`OptionKind` |
| 驱动层 | `Plugin.Motion.ZMotion` / `Plugin.Motion.Virtual` | `SetSpeedIfNeeded` / `ApplyAccelAndCurve` / `Single_MoveAbs` / `Single_Move`；速度、当量、S 曲线的容错策略 |
| 步骤层 | 本工程 | "下发不等到位"这条语义**只在步骤层表达**（`WaitsForCompletion` 不置真） |

## 二、RunAlgorithm 逐步（本步骤的执行时间线）

| # | 代码动作 | 为什么必须在这一刻做 |
| --- | --- | --- |
| 1 | `Accepted.Value = false` | **开头清零**：本轮被拒绝时下游不能拿到上一轮的 true（输出端口不是"曾经成功过"的存证） |
| 2 | `var axisName = Axis.ActualValue ?? string.Empty` | 生效值在"读的这一刻"定稿；`?? string.Empty` 只挡 null，**空串刻意不兜默认值**，交给解析器说"没有指定轴名" —— 把未知当成一个坐标是最危险的做法 |
| 3 | `AxisUsed.Value = axisName`（在任何校验之前） | 输出的 `Axis` 是"本次实际用到的轴名"，**失败时也要写**：下游排障要能看出这一轮打在哪根轴上。挪到成功分支里就等于失败时下游拿到上一轮的旧轴名 |
| 4 | `MotionAxisResolution.TryResolveAxis(...)` | 一次调用拿到"设备 + 轴映射"；错误文案由解析器统一给，7 个步骤共享同一句 |
| 5 | `device.State != MotionCardState.Online` → `Fail` | 点位运动在报警/安全停机态**一律不放行**（与「轴使能」允许报警态发起是刻意的差别） |
| 6 | `Target.GetTypedValue()` / `Velocity.GetTypedValue()` | 强类型入口：把弱类型 `Value` 转成 `double`。`OutputPort<T>.Value` 是 object，不参与判断；输入侧同理，别拿 `port.Value` 当生效值 |
| 7 | `Kind = Mode.ActualValue == Relative ? MoveRelative : MoveAbsolute` | 界面上的二选一在这里**翻译成命令种类** —— 这是 `MotionMoveMode` 与 `MotionCommandKind` 分成两个枚举的理由：命令种类是给驱动看的，将来加插补不该塞进这个下拉 |
| 8 | `Timeout = TimeSpan.FromMilliseconds(device.Descriptor.Params.CommandTimeoutMs)` | 超时取**卡级**值（默认 30000 ms），只约束"写命令"这一段；到位超时是「等待到位」的事，两者混用会让人把 30 s 当节拍 |
| 9 | 不置 `WaitsForCompletion`（保持默认 false） | **本步骤的全部意义在这里**：`Enqueue` 返回即结束。置真就把两轴同时运动退化成串行，而且现场只看得见"慢了" |
| 10 | `device.Enqueue(command)` 后判 `!= Accepted` | 拒绝时取 `LastFault?.Suggestion` 拼进文案（**闸门的原因就存在这个字段**，漏拼就只剩一个英文成员名） |
| 11 | `Accepted.Value = true; Success.Value = true` | 成功语义严格限定为"收进队列"，不越过半步 |
| 12 | `Logger.Info($"{InstanceName} 已下发：{command.Describe()}（未等到位；…）` | 日志里**明写"未等到位"** —— 这是最容易误解的一步，宁可啰嗦。文案由 `MotionCommand.Describe()` 统一给，改它就是改 7 个步骤与卡的执行线程日志的共同口径 |

## 三、状态有几份、谁是真源

| 状态 | 真源 | 本步骤读还是写 |
| --- | --- | --- |
| 卡状态机 | `MotionDeviceBase`（轮询线程 + 看门狗 + 急停） | 读 `State` / `StateDetail` |
| 命令生命周期 | `MotionCommand`（`State` / `Error` / `Completion`） | 造、入队；**不等、不读后续状态** |
| 当前位置 / 速度 / 使能 | 驱动轮询回填的轴状态快照 | **完全不读**（本步骤不报位置，位置归「读轴状态」「等待到位」） |
| 界面填的值 | 步骤 `InputValues`（`.vms`） | 基类 `ApplyConfigValues` 灌进端口，`OnConfirm` 写回 |
| 本插件自己 | **无实例字段、无静态字段、无缓存** | — |

## 四、白拿的宿主 / 基类行为（作者不用写、也别重写）

| 行为 | 由谁提供 | 表现 |
| --- | --- | --- |
| 端口发现 | `VisionPluginBase` 反射 public 属性 | 四个口全部自动注册；输出 `AxisUsed` 的**端口名**由构造函数显式给成 `Axis` |
| 灌值 / 写回 | `ApplyConfigValues` + `OnConfirm` | 面板显示已保存值；链接中的口在确认时**移除**常量键 |
| 参数面板 | 宿主 `AutoPortConfigView`（`StepParameterDialog` 在无自定义视图时包一层） | 只渲染输入端口，所以本步骤四项全能改 —— 这也是"该做成端口的别做成 `[StepConfig]`"的根据 |
| 轴名候选 | `StepConfigOptionSource` + 宿主 `MotionModule` 查 `MotionAxisRegistry` | 只列 `Enabled` 且**解析得到**的名字；重名被影子化的名字刻意不列（列了就是递给用户一个"选了必然失败"的选项） |
| 枚举候选与中文显示 | `LinkableValueEditor` 从**端口类型**现场推 + `StepConfigOption.ForEnum(类型, 成员名)` | `Mode` 显示 `绝对（走到坐标点）` / `相对（再走一段）`（来自 `MotionEnums.cs` 成员上的 `[Display(Name=…)]`），**写进端口的仍是成员名** `Absolute` / `Relative` |
| "只能选"档位 | `IsOptionPort = DataType.IsEnum` 或 `IsFunctionalEnum` → 隐藏 🔗 与 ✕ | `Axis`（显式 `IsFunctionalEnum = true`）与 `Mode`（枚举）都没有链接入口；`Target` / `Velocity` 有 |
| 命令排队、急停打断、软限位、轴号校验、队列深度 | `MotionDeviceBase.Enqueue` + `CheckGateLocked` | 步骤侧只处理 `Accepted` 与否；软限位拒绝记 **Notice 级**（业务上正常，不进报警也不进安全停机） |
| 厂商语义 | `Plugin.Motion.ZMotion` | 速度 ≤ 0 不覆盖（沿用控制器内 SPEED）；`AccelMmPerS2 ≤ 0` 不覆盖；`SetSramp` 每次定位前重置（梯形 = 0、S 曲线 = 200 ms），**上一条命令的 S 设置会被清掉**；脉冲当量在连接时 `SetUnits` 一次，之后 API 收发都是 mm |
| 流程失败语义 | `VisionPluginBase.Fail(...)` | `Success = false` + `ErrorMessage`，后续步骤照常执行 |

## 五、内存所有权（本文件与同族步骤的一处**不对称**，改动前必读）

`MotionMovePlugin` 里的命令对象**没有 `using`**：

- 等待类步骤（「轴回零」「轴使能」）必须 `using var command` —— 它们自己 `Completion.Wait(...)`，
  用完就地释放 `ManualResetEventSlim`，且要覆盖所有提前 `return` 分支。
- 本步骤**不等待**：命令交给队列持有，执行线程完成后不再引用它，`Completion` 由 GC 终结路径回收。
  这也是"下发即返回"的另一半代价：**不要在步骤里加 `using`**，
  否则可能在执行线程还没置完成信号时就把 `ManualResetEventSlim` 释放掉（`Set` 会抛，队列执行线程被带崩）。
- 想改成立即释放的话，正确做法是给基类加"执行完统一释放"的归属，而不是在调用侧 `using`。
- `device` 与 `mapping` 不属于本插件：设备由 `IMotionProvider` 创建与释放，映射是方案对象的一部分。

## 六、A 组不变量（违反就编译/闸门红）

| 症状 | 机理 | 守门断言名 |
| --- | --- | --- |
| 参数面板一片空白 | 没自定义视图的插件靠反射拿输入端口，端口没注册就没有行 | `★没有自定义视图的插件也能拿到输入端口（拿不到的话参数面板就是一片空白）`、`★建界面前先把已保存的配置灌进端口（否则界面显示默认值，看着像「改完没保存」）`（`FlowCanvasChecks\MotionZMotionChecks.cs`） |
| 目标/速度无法由上游给（视觉引导落不了地） | 把 `Target` 写成 `[StepConfig]`：配置属性不暴露为端口、不可链接 | `★每个插件的「轴 / 卡」都有可链接的输入端口（只靠 [StepConfig] 常量的话，编辑流程时赋不了值——它不暴露为端口）` |
| 口存在却从不生效 | 从不读 `ActualValue` / `GetTypedValue()` | `★端口在运行期真的被读取（有端口却只读常量 = 摆设，链接上去也不生效）` |
| 轴名下拉空白或退回手打文本框 | 没标 `OptionKind` / `IsFunctionalEnum`，编辑器认不出"方案候选"口 | `★轴名端口被识别为「只能选」且候选来自方案（写进流程的就是轴名本身）`、`★全部 {N} 个运动插件的「轴」参数都标了动态候选` |
| 界面上 `Mode` 显示英文成员名 | `[Display(Name=…)]` 被删，或候选生成点改回 `new StepConfigOption(成员名)` | `★枚举下拉显示 [Display] 的中文名（现场不该看到 Circle 这种认不出含义的英文）`、`★两处枚举候选共用同一个工厂（自动参数面板 + 绑定窗口，不再各写一份 new StepConfigOption(成员名)）` |
| 存进 `.vms` 的是中文显示名（换一次显示名打断一条流程） | 显示与值串位 | `★显示中文不串位：下拉选完写进端口的仍是成员名 Circle（存进 .vms 的是稳定键，不是显示名）`、`★卡候选的值仍是稳定键（按卡名寻址；显示可以带设备名，但写进流程的必须能解析回设备）` |
| 两轴并行运动退化成串行 | `WaitsForCompletion` 被置真、或步骤里加了 `Completion.Wait` | `★Move 下发成功 ≠ 已到位（命令已 Done，但轴仍在途中）`（`FlowCanvasChecks\MotionChecks.cs`）—— 该断言正是靠"下发完立刻 Done、轴还在途中"成立的 |
| 轴往限位方向走之前没有拦阻 | 软限位判据被挪出闸门 | `目标超出软限位时被拒绝（不让轴往限位方向走）`、`软限位拒绝记为 Notice 级（业务上正常，不该当成设备故障）`、`被软限位拒绝后设备仍是可用的（不进报警/安全停机）` |
| 手册被自动生成页顶掉 / 串页 | Help 页没进 DLL、放错目录、目录名不等于类名 | `HM2 多插件程序集不许放扁平手册（防串页）`、`HM3 Help 子目录名逐字等于插件类名（分册能被认出）`、`HM4 有 Help/*.md 的工程都带 EmbeddedResource（手册进得了 DLL）`、`HM13 已交的开发章节达到所在档位的行数下限（L300/M150/S100，交了就不是随笔）`（`FlowCanvasChecks\HelpManualCheck.cs`） |

## 七、B 组不变量（不报错，只出诡异现象）

| 症状 | 机理 | 守门断言名 |
| --- | --- | --- |
| 输出 `Axis` 在失败那一轮留着上一轮的旧轴名 | 回填位置挪到了校验之后 | 无断言，靠人工核对（第二节第 3 行就是这条） |
| 本轮被拒绝、下游条件分支却按"已下发"走 | `Accepted` 没在开头清零 | 无断言，靠人工核对 |
| "改当量"之后所有流程行程按错误倍率走 | 脉冲当量在连接时一次 `SetUnits`，运行期不重取；改完没重连就还是旧当量 | 无断言，靠人工核对（用户页写在「换卡/改接线」那条） |
| 上一条命令的 S 曲线影响到这一条 | `SetSramp` 必须**每次定位前重设**，否则窗口级/轴级状态跨命令串味 | 无断言，靠人工核对（驱动侧注释写明） |
| 速度填 0 在真机与仿真跑出不同节拍 | 真机"不覆盖"、仿真回落卡级默认 50 | 无断言，靠人工核对 —— 用户页把它列为最易踩的差异 |
| 相对运动冲出软限位 | 闸门只对 `MoveAbsolute` 查软限位（刻意：相对是"再走一段"，基准是当前位置） | 无断言；改这条判据要连带改 `MotionChecks` 的软限位断言 |
| 拒绝原因只剩一个英文成员名 | 没拼 `LastFault?.Suggestion` | 无断言，靠人工核对 |

## 八、并行安全声明

可放进并行分组：插件实例无状态；`Enqueue` 在基类锁内写队列，打断动作在锁外执行
（`打断在入队锁之外执行（锁内 Cancel 会与命令线程抢同一把锁，等于自己等自己）`）。
注意"两条轴同时运动"的物理并行度取决于**是否同一张卡**：同卡的命令 FIFO 下发（下发本身极快，
随后各自执行），跨卡才是真正两条独立队伍。

## 九、改完这个文件必须做的动作

| 改了什么 | 必须同步 |
| --- | --- |
| 端口名/默认值 | `03-ports-and-parameters.md` 的表格与存档键清单；`InputValues` 的键会变，老方案里的旧键静默失效 |
| 等待语义（改成等到位） | `01-quickstart.md` 的"只下发不等到位"整段与日志文案；`MotionChecks` 的异步语义断言会红 |
| 门禁状态集合 | `05-troubleshooting.md` 的分流表、`MotionChecks` 的状态机断言与本页第七节 |
| `Describe()` 文案 | 用户页引用的两行日志原文（要求逐字一致，改了代码不改手册就是假绿） |
| 给 `MotionMoveMode` 增删成员 | `MotionEnums.cs` 的 `[Display(Name=…)]`（R40：新增成员必须补中文，否则中文闸门断言红）、`03` 页的取值表 |
