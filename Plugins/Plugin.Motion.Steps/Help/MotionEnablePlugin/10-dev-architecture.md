# 轴使能：架构、宿主契约与不变量

> 分类：运动控制 ｜ 来源：插件 Plugin.Motion.Steps ｜ 读者：开发

S 档一页承担三个角色：架构与数据流、宿主白拿的行为、改这个文件时的红线。
用户页讲的"怎么填"这里不重复，只讲**这些行为由谁提供、为什么这么写、写错会怎样**。

## 一、这个插件在分层里的位置

运动控制三层，本文件属于最薄的那一层：

| 层 | 位置 | 职责 |
| --- | --- | --- |
| 契约层 | `Shard\Core.Interfaces\Motion\` | `MotionDeviceBase`（队列/门禁/看门狗/状态机）、`MotionAxisResolution`（轴名解析）、`MotionCommand`、`AxisStatus` |
| 驱动层 | `Plugin.Motion.ZMotion` / `Plugin.Motion.Virtual` | 只写"这台设备怎么说话"：`ConnectCore` / `DisconnectCore` / `PollStatusCore` / `ExecuteCommandCore` |
| 步骤层 | 本工程 7 个插件类 | 把一次设备交互摊成流程节点：解析名字 → 状态门禁 → 造命令 → 入队 → 等或不等 → 回报 |

`MotionEnablePlugin.cs` 全文 157 行，**没有一行 SDK 相关**：它不知道正运动、也不知道雷赛，
只认 `IMotionDevice.Enqueue(MotionCommand)`。这是刻意的高度差——步骤层一旦出现 `ZAux_*`，
换一家卡就要改 7 个流程插件。

## 二、RunAlgorithm 逐步（本步骤的执行时间线）

| # | 代码动作 | 为什么必须在这一刻做 |
| --- | --- | --- |
| 1 | `Axis.ActualValue ?? string.Empty` | 端口的实际生效值在"读的这一刻"确定（链接值 优先于 手填值）；取出后存局部变量，整步用同一个值，避免中途有人改常量框导致"前半句按使能、后半句按失能" |
| 2 | `Math.Max(500, TimeoutMs.ActualValue)` | 下限写死在步骤侧（0/负数/极小值都按 500 ms 跑），而不是让基类去猜 |
| 3 | `Enabled.Value = false` | **开头清零**：本轮失败时下游不能拿到上一轮的 true（流程可重复执行，输出端口不是"曾经成功过"的存证） |
| 4 | `MotionAxisResolution.TryResolveAxis(...)` | 一次调用同时拿到"设备 + 轴配置"；失败原因文本由解析器给出，步骤不编自己的措辞 |
| 5 | `ClearAlarmFirst && enable` → `device.ClearAlarm(out …)` | **必须在状态门禁之前**：`CheckEnable` 对「已安全停机」是拒绝的，而清报警成功恰恰会把它拉回在线；顺序反了，这个开关在最需要它的场景里永远走不到。失败即 `Fail`（带着报警上电是最不该发生的事之一） |
| 6 | `MotionCommandGate.CheckEnable(device, enable)` | 使能只放「在线」；失能是安全动作（报警/安全停机也放行）。"其余命令要求 Online"由基类闸门再兜一层，两层判据不同是设计而非疏漏 |
| 7 | `new MotionCommand { WaitsForCompletion = true }` | 本步骤**会等待**：使能失败必须当场知道，否则后面每一步都以"轴未使能"失败，真因被淹没 |
| 8 | `device.Enqueue(command)` 后判结果 | `Accepted` 只代表"收进队列"；拒绝时取 `LastFault?.Suggestion` 拼进文案（**闸门原因就存在这个字段里**） |
| 9 | `command.Completion.Wait(timeout + 500)` | 多给 500 ms 余量：命令内部的超时判据与这里的等待不必严丝合缝，先到者胜 |
| 10 | `command.State != Done` → `Fail` | 区分"没等到"（超时）与"等到的不是完成"（Failed / TimedOut / Canceled） |
| 11 | `device.GetAxisStatus(mapping.PhysicalIndex)?.Enabled` | 成功与否以**设备回报**为准，不是"我发了使能所以它就该是使能"；卡可能延迟生效 |

## 三、状态有几份、谁是真源

| 状态 | 真源 | 本步骤读还是写 |
| --- | --- | --- |
| 使能与否 | 轴状态快照（`MotionDeviceBase` 轮询线程刷新） | 读（`GetAxisStatus`） |
| 命令生命周期 | `MotionCommand`（基类的队列 + `Completion` 事件） | 造、入队、等，随后 `using` 释放 |
| 界面填的值 | 步骤的 `InputValues`（`.vms` 里） | 由基类 `ApplyConfigValues` 灌进端口，`OnConfirm` 写回 |
| 本插件自己 | **无**（没有实例字段、没有静态字段、没有缓存） | — |

"插件里没有自己的状态"是这 7 个步骤的共同前提：流程可能被并行分组调度，
插件实例上的任何可变字段都是竞态源。本步骤的全部可变量都是方法内的局部变量。

## 四、白拿的宿主/基类行为（作者不用写、也别重写）

| 行为 | 由谁提供 | 表现 |
| --- | --- | --- |
| 端口发现与注册 | `VisionPluginBase.EnsurePortsDiscovered`（反射 public 属性） | 端口名可省略（自动取属性名）；本步骤显式写了 `"Timeout"`，与属性名 `TimeoutMs` 不同，存档键随之是 `Timeout` |
| 灌值 / 写回 | `ApplyConfigValues` + `OnConfirm` | 参数面板显示的是**已保存值**而不是默认值；链接的口写回时移除常量键 |
| 参数面板 | 宿主 `AutoPortConfigView`（框架包一层） | 不实现 `IPluginCustomViewProvider` 就自动有面板；反过来**面板只列输入端口**，`[StepConfig]` 没有渲染形态 |
| 命令排队、急停打断、软限位、能力校验 | `MotionDeviceBase.Enqueue` + `CheckGateLocked` | 步骤侧只需处理 `Accepted` 与否 |
| 失联判据与安全停机 | 基类轮询线程 + 看门狗 | 步骤侧只会看到"卡状态不是 Online" |
| 流程失败语义 | `VisionPluginBase.Fail(...)` | `Success=false` + `ErrorMessage`，后续步骤照常执行（要不要停是流程的事） |

## 五、内存所有权

`using var command = new MotionCommand { ... }`：命令对象自带 `ManualResetEventSlim`（`Completion`），
**由步骤 Dispose**（`using` 覆盖所有提前 return 分支）。基类在命令完成或取消后不再引用它，
所以提前释放是安全的；漏掉 `using` 是句柄泄漏，不会立刻出错但长跑流程会累积。
`device` 与 `mapping` 都**不属于**本插件：设备由 `IMotionProvider` 创建与释放，
映射是方案对象的一部分，步骤只读不释放。

## 六、A 组不变量（违反就编译/闸门红）

| 症状（改坏了会看到什么） | 机理 | 守门断言名 |
| --- | --- | --- |
| 流程里"轴由上游决定"失效，链接上去也不生效 | 端口声明了但从不读 `ActualValue`，等于摆设 | `★每个插件的「轴 / 卡」都有可链接的输入端口（只靠 [StepConfig] 常量的话，编辑流程时赋不了值——它不暴露为端口）`、`★端口在运行期真的被读取（有端口却只读常量 = 摆设，链接上去也不生效）`（`FlowCanvasChecks\MotionZMotionChecks.cs`） |
| 老流程一编译就报"必填参数未配置" | 轴名口必须 `IsRequired = false`：有默认值即可运行 | `轴端口 IsRequired=false（老流程只填常量，不能因为没链接就判编译失败）` |
| 候选下拉一片空白/退回手打文本框 | `OptionKind` 没声明，生成器认不出这是"方案候选"口 | `★运动端口声明了 OptionKind（候选项随方案变化，不能在扫描期取一次就存下来）`、`★属性面板共用生成器清单里注册了动态候选生成器（抽到 Defaults 后不可能漏一边）` |
| 手册被自动生成页顶掉、用户看到的不是我们写的那份 | Help 页没进 DLL / 放错目录 | `HM4 有 Help/*.md 的工程都带 EmbeddedResource（手册进得了 DLL）`、`HM2 多插件程序集不许放扁平手册（防串页）`、`HM3 Help 子目录名逐字等于插件类名（分册能被认出）`（`FlowCanvasChecks\HelpManualCheck.cs`） |

## 七、B 组不变量（不报错，只出诡异现象）

| 症状 | 机理 | 守门断言名 |
| --- | --- | --- |
| 输出端口留着上一轮的 true，条件分支"看着就对" | 开头没清零 | 无断言，靠人工核对（本步骤第 3 行就是这条） |
| 使能明明失败却"过了" | 用"我发了命令"当结论，而不是读设备回报 | 无断言，靠人工核对 |
| 报警态下这一步被自己的门禁提前拦掉，导致"清报警前先失能"的写法落不了地 | 步骤层只应拦 Closed/SafeStopped，报警态留给基类闸门统一口径 | `★报警态拒绝运动命令（不会对着报警中的轴下发 Move）`（`FlowCanvasChecks\MotionChecks.cs`） |
| `ClearAlarmFirst=true` 却仍然被"已安全停机"挡住（开关等于没做） | 清报警被写在状态门禁**之后** —— 门禁先拒绝，永远走不到清报警 | `★安全停机下清报警被接受（旧实现连命令都进不了队列）`、`★清报警成功且底层无报警 → 回到在线`、`★恢复后可以再次下发运动命令`（`FlowCanvasChecks\MotionChecks.cs`） |
| 清报警"成功"但底层报警还在，卡却回了在线 | 只看命令完成、不复核报警位（安全停机期间轮询不采状态，缓存是旧的） | `★底层仍报警时清报警不放行：保持安全停机 + 给出原因`、`★底层报警清掉后可以恢复在线（拒绝不是永久的）`（`FlowCanvasChecks\MotionChecks.cs`） |
| 拒绝原因丢失，界面只剩"被拒绝（Rejected_NotConnected）" | 没拼 `LastFault?.Suggestion`（闸门原因就写在这个字段） | 无断言，靠人工核对 |
| 慢上电的驱动器被 500 ms 下限"卡"成超时 | 下限在步骤侧写死；调小 `Timeout` 不会低于 500 | 无断言，靠人工核对 |

## 八、并行安全声明

本步骤可放进并行分组：插件实例无状态；`Enqueue` 在基类锁内做队列写入、
打断动作在**锁外**执行（`打断在入队锁之外执行（锁内 Cancel 会与命令线程抢同一把锁，等于自己等自己）`）；
`GetAxisStatus` 读的是快照引用，不发起通信。
同一根轴被两个并行分支同时使能/失能时，结果取决于队列顺序，流程侧不该这么写。

## 九、改完这个文件必须做的动作

| 改了什么 | 必须同步 |
| --- | --- |
| 端口名/默认值 | 用户页《轴使能》与本页第三节的存档键；`InputValues` 的键会变，老方案里的旧键静默失效 |
| 等待语义（改成不等） | 用户页里"本步骤会等待"的整段口径与超时文案 |
| 门禁状态集合 | `MotionChecks` 里的状态机断言与本页第六节 |
| 错误文案 | `05-troubleshooting.md` 的原文对照表（那份表要求逐字一致，改了不改手册就是假绿） |
