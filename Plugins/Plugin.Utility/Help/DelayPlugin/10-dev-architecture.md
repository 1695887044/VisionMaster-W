# 延时 · 开发：结构、契约与不变量

> 分类：常用工具 ｜ 来源：插件 Plugin.Utility ｜ 读者：开发

S 档条目：架构、宿主契约、不变量合在本页。源码 `DelayPlugin.cs`（98 行），
命名空间 `VisionMaster.Plugins.Util`，程序集由 `Plugin.Util.csproj` 产出（同程序集六个插件类，
手册按类名分目录，本页只管这一个）。
这是四个工具插件里**唯一会占住执行线程**的那个，所以本页的重点全在"取消"与"时长"上。

## 一、结构：一个类、一个输入、两个输出、零私有状态

```
DelayPlugin : VisionPluginBase      ← 运行期算子
              [ParallelSafe]         ← 注释口径：纯计算类（50ms 步进时协作取消范式）
```

没有配置视图接口、没有动态端口接口、**没有任何 `[StepConfig]` 属性**、没有私有字段。
`Initialize()` / `Dispose()` 重写成空体；构造函数是空的（`public DelayPlugin() { }`，
留着只为可读性，不做任何事——反射式端口发现不依赖它）。

`RunAlgorithm` 的完整流程（三条出口）：

```
1. Logger.Info("{InstanceName} 开始执行延时...")            ← 无条件打，包括 Delay=0 的那一轮
2. targetDelayMs = DelayTimePort.GetTypedValue()            ← try 之外
3. ElapsedTime.Value = 0; State.Value = false; Success.Value = false;   ← ★ 三件事一起预置
4. try {
       elapsed = 0; const int step = 50;
       while (elapsed < targetDelayMs) {
           if (context.CancellationToken.IsCancellationRequested) {
               Logger.Warn("{InstanceName} 收到急停信号，已强制中断！");
               Success.Value = false; return;               ← 出口①：中断，不写 State、不写 ErrorMessage
           }
           currentStep = Math.Min(step, targetDelayMs - elapsed);
           Thread.Sleep(currentStep);
           elapsed += currentStep;
           ElapsedTime.Value = elapsed;                     ← 每片刷新，中断时留下诚实读数
       }
       State.Value = true; Success.Value = true;             ← 出口②：跑满
       Logger.Info("{InstanceName} 延时 {N}ms 完成。");
   } catch (Exception ex) {
       State.Value = false; Success.Value = false;
       Logger.Error("{InstanceName} 发生异常: {ex.Message}");
       throw;                                                ← 出口③：不吞，交引擎
   }
```

第 3 步那三行预置是**这段代码最容易"看起来多余"的地方**，也是它必须保留的理由，见第四节 A1。

## 二、状态存哪 / 结果类型怎么定 / 谁负责 Dispose

**状态存哪**：只有一个输入端口的手填槽位（`Delay`）和两个输出端口的值（`ET` / `State`）。
`elapsed` 是**局部变量**，不落字段——所以不存在"上一轮残留"，
同一实例被并行分支同时调用也不共享任何可变状态（这是 `[ParallelSafe]` 能标的真正依据，
注释里那句"纯计算类"其实说轻了：**它的并行安全性来自"无共享可变状态"，不是来自"算得快"**）。

**结果类型怎么定**：`ET` 是 `int`（毫秒计数）、`State` 是 `bool`（本轮是否等满）。
两者都在方法开头被重置，所以**不存在"沿用上一轮值"的窗口**。
`Success` 是基类的 `OutputPort<bool>`，本插件在出口①③显式写 `false`、出口②显式写 `true`
——四个工具插件里唯一在正常路径上也显式写 `Success=true` 的就是它（原因见 A2）。

**谁负责 Dispose**：重写成空体且**没调 `base.Dispose()`**。
输出是 `int` / `bool`，不是 `IDisposable`，基类那段"释放输出端口承载的可释放值"扫不到东西，
不泄漏。但 `Thread.Sleep` 占住的线程在本轮返回后才归还——
**"释放资源"这件事在本插件里其实是"什么时候结束循环"，而不是 `Dispose()`**。

## 三、宿主白拿的契约（本插件用到的每一条）

| 契约 | 基类 / 共享件给的行为 | 本插件哪里靠它 |
| --- | --- | --- |
| 端口发现 | 反射公开实例属性，`IInputPort` / `IOutputPort` 自动进两张字典 | 三个端口字段初始化即注册 |
| 取消令牌 | `IExecutionContext.CancellationToken` 由会话持有，停止/急停时置位；重复启动被拒时不会踩坏它 | 每片醒来判一次，是"协作式取消"的唯一入口 |
| 默认成功 | `Execute` 进 `RunAlgorithm` 前预置 `Success=true`、清空 `ErrorMessage` | 本插件**反着用它**：开局就写 `false`，只在跑满时写 `true`（见 A2） |
| 异常转失败 | 未捕获异常 → `Fail("{异常类型名}: {消息}")` + 日志 `[{实例名}] 未捕获异常已转为步骤失败：…`；`RethrowOnException`（默认 false）决定是否继续上抛中断当轮 | 本插件 `throw;` 之后由这条接住，`ErrorMessage` 因此**有**内容（与中断路径相反） |
| 轮首回收输出 | `AutoDisposeRoundOutputs`（默认 true）在 `RunAlgorithm` 之前释放输出端口承载的可释放值 | 输出是 `int`/`bool`，这条对本插件是**空转** |
| 统一灌值 | `ApplyConfigValues`：存盘字典 → 输入端口（已连线端口跳过） | `Delay` 的值全走这条路进来 |
| 默认确认 | `OnConfirm`：未连线端口写回存盘字典；已连线端口 `RemoveInputValue` | 未重写；填过 500 之后必填检查才放行 |
| 必填检查 | `IsRequired && 无连线 && 字典无非空值` → `[参数缺失] … 的必填参数 'Delay' 未配置！` | 源码显式写了 `IsRequired = true`（基类默认本就是 `true`，写不写都一样） |
| 连线类型检查 | `FlowCompiler.IsLinkable`：数值族互转放行、`string` ⇄ 数值放行、**`bool` 不算数值族** | `Delay` 拒布尔上游 |
| 日志器 | `context.Logger` 走 `LogService` 的队列，并发写安全 | Info / Warn / Error 三条都在这里 |
| 并行门禁 | 并行分支内未标注的算子报 `[并行不安全]`；特性按类型 **FullName 字符串**匹配 | `[ParallelSafe]` 成立条件是"无共享可变状态 + 分步检查取消" |

**没有自定义视图**：不实现配置视图接口，宿主回退到自动生成的参数面板（只有一行）。
`FlowCanvasChecks\MotionZMotionChecks.cs` 里有一条**真跑本插件**的断言（见第五节），
"没有自定义视图的插件也能拿到输入端口"这件事就是拿 `DelayPlugin` 验的。

## 四、不变量清单

A 组：违反就抛 / 编译红 / 闸门红。B 组：违反不报错，只出诡异现象。
每条按"症状 → 机理 → 守门"写。

### A 组

**A1 方法开头必须同时重置 `ET` / `State` / `Success`**
症状：上一轮跑满、这一轮被急停打断，界面上 `State` 却还是 `true`（"看着等完了其实没等完"）。
机理：输出端口值**跨轮保留**；不重置就等于把上一轮的终值当本轮的初值。
尤其 `State` 只有一条写 `true` 的路径，漏了预置就会保留旧值。
守门：**无断言，靠人工核对**。同理 `Success` 若不预置，
会走"默认成功"的预置值 `true`，中断路径的 `return` 就变成"静默成功"（见 A2）。

**A2 中断路径必须显式写 `Success = false`**
症状：按急停后流程"正常收尾"，取消信号像被吞掉。
机理：基类预置 `Success=true`，`RunAlgorithm` 里 `return` 也算"方法跑完了"；
不显式写 `false` 就是"中断了但判成功"。源码注释留了同族教训：
框架以 `Success.Value is true` 判定业务成功，**此前只写了自家 `State`、没写 `Success`，
被引擎每轮标记"执行失败"**（写反方向的同一枚硬币）。
守门：`FlowCanvasChecks\ExecutionChecks.cs` 的停止相关断言
（"停止后执行任务退出""停止后 IsRunning=false 且状态归为 Stopped""停止后会话锁干净归还"）——
注意它们跑自己的桩算子，**不直接调用本插件**，所以本条实质仍是靠人工核对。

**A3 分片 sleep 的粒度不许换成一次性 `Thread.Sleep(targetDelayMs)`**
症状：急停要等满整个延时才生效（"急停不急"）。
机理：`Thread.Sleep` 不可中断，唯一的响应点是两片之间。50ms 就是"急停响应上限"。
守门：无断言。**这是本插件存在的理由之一**，改它等于把「延时」变成阻塞源。
要缩短上限就减小步长（代价：`ET` 刷新更频繁、循环开销略增）；**不要改成无限大**。

**A4 最后一片必须按余量截断**
症状：`Delay=120` 时 `ET` 报 150，比设定值多等 30ms。
机理：`Math.Min(step, targetDelayMs - elapsed)`；写成固定 `step` 就会超过设定值。
守门：无断言，靠手册与"ET 约等于设定值"这条口径核对。

**A5 `catch` 里必须 `throw;`（不是 `throw ex;`、也不是吞掉）**
症状：引擎以为这一步成功，或调用栈信息全丢。
机理：`State=false; Success=false;` 之后上抛，让引擎按执行异常处理当轮；
`throw ex;` 会重置堆栈。本插件选了"自己记一条带实例名的日志 + 交引擎中断当轮"。
守门：基类 `Execute` 的异常转失败链。改吞异常要先想清楚"等待中出错还要不要继续跑流程"。

### B 组（静默故障）

**B1 急停中断不写 `ErrorMessage`**
症状：步骤红、错误信息列是空的，现场只能猜。
机理：出口① 只写日志（Warn）与 `Success=false`，没走 `Fail(...)`。
守门：**无断言，靠人工核对**。要补文案就补一句明确的（例如"延时被急停中断，已流逝 N ms"），
别只靠日志——日志级别是 Warn，界面上不显眼。

**B2 `Delay=0` / 负数不报错、直接判成功**
症状：配方里时间是空/0，流程"跑得飞快还全绿"。
机理：`while (elapsed < 0)` 一次不进 → 走成功出口。
守门：无断言。这是"等 0ms 也是等完了"的口径；
若要"缺时间就停流程"，判据应该在上游参数校验或必填检查里做，不是这里。

**B3 `Thread.Sleep` 会占住一条工作线程**
症状：并行分组里几条分支都放大延时，线程池排队、别的算子跟着变慢。
机理：`[ParallelSafe]` 让它可以进并行分支，但"并行安全"不等于"不占资源"——
它睡的是真线程，不是定时器。
守门：无断言。**并发量大时要盯线程占用**，节拍表里的"并行"收益会被大延时吃掉。

**B4 已连线时手填值不生效**
症状：面板改了数字没反应。
机理：`GetTypedValue()` → `GetActualValue()`：有连线时返回连线缓存值，手填槽位不参与。
守门：`Shard\Core.Interfaces\DataPort\InputPort.cs` 里"★ 语义陷阱"注释；
新代码取值一律 `GetTypedValue()` / `ActualValue`。（"用户编辑"与"程序灌值"不可混判，AGENTS.md R35 有实测事故。）

**B5 默认值 `500` 不等于"已配置"**
症状：拖出新节点直接编译，报 `[参数缺失] … 'Delay' 未配置！`，
用户反复检查"我明明看到框里是 500"。
机理：必填检查读存盘字典（`InputValues`），不读端口初值；500 只在端口槽位里。
守门：`Engine\FlowCompiler.cs` 的必填项检查（注释里写明了这条判据的来历与踩坑）。
**别为了让新节点"开箱即用"就把默认值写进字典**——那等于把必填检查作废。

**B6 `IsRequired = true` 是冗余写法**
症状：以为"只有延时必填，其它工具的参数不填也行"，给别的插件删掉必填。
机理：`InputPort` 基类初值就是 `true`，本插件只是显式重复。
守门：**无断言**，靠本页与「端口与参数」把"三个工具的输入同样必填"写清楚。

## 五、这个插件在闸门里的位置

档位登记在 `FlowCanvasChecks\HelpManualCheck.cs` 的 S 档表
（用户页 ≥3、开发页 ≥1、开发页 ≥100 行；HM2 禁止往 `Help\` 根放页、HM3 目录名逐字等于类名、
HM12 开发页文件名 `NN-dev-`、HM12b 禁止再套一层、HM13 行数下限）。

**唯一真跑本插件的断言**（全仓搜索 `DelayPlugin` 只命中这一处）：

- `FlowCanvasChecks\MotionZMotionChecks.cs` 的 `RunStepConfigOptionContract()`：
  `new VisionMaster.Plugins.Util.DelayPlugin()`，断言名
  **"★没有自定义视图的插件也能拿到输入端口（拿不到的话参数面板就是一片空白）"**，
  判据是 `delayPlugin is not IPluginCustomViewProvider && delayPlugin.Inputs.Count > 0`。
  它的来历是真实现象"弹窗建出来了、里面一片空白"——根因是旧逻辑对无视图插件直接返回 null。
  **本插件被删掉输入端口、或不小心实现了配置视图接口，这条会红。**

其余是契约级、对本插件间接生效的断言（不点名本插件）：

- `FlowCanvasChecks\ExecutionChecks.cs`：`DuplicateStartIsIgnored()` 的
  "停止后执行任务退出" / "停止后 IsRunning=false 且状态归为 Stopped" /
  "停止后会话锁干净归还" / "重复启动没有踩坏会话的取消令牌源"——
  它们验的是"取消令牌能被观察到的前提下会话干净收尾"，正是本插件依赖的前提。
- `FlowCanvasChecks\LogicBruteforceChecks.cs` 的
  "B2：看门狗自检（三层 9999 计次循环 → 2s 内被协作取消打断并如实记超时）"——
  与 B3/A3 同族的"分步检查取消"范式守门，用的是自己的 `TracePlugin` 桩。

本插件**没有专属断言文件**：改算法体（分片粒度、三条出口的赋值、日志文案）
**当前没有任何断言会红**，只能靠人工核对本页与用户页。
动到 `Shard\Core.Interfaces`（端口 / 基类 / `IExecutionContext`）则要按
"全量重建 + 跑 gate"走，不能只看本工程编译通过。
