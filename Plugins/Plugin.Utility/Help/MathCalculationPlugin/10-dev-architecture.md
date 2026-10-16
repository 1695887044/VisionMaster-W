# 数学计算 · 开发：结构、契约与不变量

> 分类：数据处理 ｜ 来源：插件 Plugin.Utility ｜ 读者：开发

S 档条目：架构、宿主契约、不变量合在本页。源码只有一个 `MathCalculationPlugin.cs`（88 行），
命名空间 `VisionMaster.Plugins.Util`，程序集是 `Plugin.Util.csproj`（同程序集还有五个插件类，
各自动手册分目录，本页只管这一个）。
篇幅不在大小，价值在把"这十几行 switch 为什么必须这么写、改了会坏在哪"钉死。

## 一、结构：一个类、四个端口、零私有状态

```
MathCalculationPlugin : VisionPluginBase     ← 运行期算子（RunAlgorithm / 端口 / 配置生命周期）
                    [ParallelSafe]            ← 并行门禁白名单标记（注释口径：纯计算类）
```

没有配置视图接口、没有动态端口接口、**没有任何 `[StepConfig]` 属性**、没有私有字段，
`Initialize()` 与 `Dispose()` 都重写成空体。
**全部状态就是三个输入端口的"手填值"槽位**——真值来源在上游连线与存盘字典里，
插件自己不持有东西，所以同一实例被连续调用多少次都不需要清理。

`RunAlgorithm` 的完整流程：

```
1. operation = Operation.GetTypedValue()      ← try 之外
2. a = ValueA.GetTypedValue()                 ← try 之外
3. b = ValueB.GetTypedValue()                 ← try 之外
4. try { Result.Value = operation switch { 17 个分支 + _ => double.NaN } }
5. catch (Exception ex) {
       Logger.Error("{InstanceName} 运算失败: {ex.Message}")
       Result.Value = double.NaN; Success.Value = false; ErrorMessage.Value = ex.Message;
   }
```

第 4 步正常走完就结束了，**不写 `Success`、不打日志、不 `return`**。
"什么都不写"是契约要求的：基类 `Execute` 进 `RunAlgorithm` 前预置 `Success=true` 并清空
`ErrorMessage`，纯计算类不显式失败就等于成功。

**第 2 组要注意 1/2/3 在 `try` 之外**，这不是疏忽，见第四节 A3。

## 二、状态存哪 / 结果类型怎么定 / 谁负责 Dispose

**状态存哪**：三个 `InputPort` 的手填槽位（`_manualValue`）+ 一个 `OutputPort<double>` 的
`Value`。没有轮次缓存、没有 `[ThreadStatic]`、没有 static 可变字段——
这是 `[ParallelSafe]` 能标的依据，也是它能被并行分支同时调用的原因。

**结果类型怎么定**：`Result` 是 `OutputPort<double>`，类型在构造时写死，
不随 `Operation` 变。所以"取整之后当整数用"是下游端口的转换行为，
本节点输出的永远是 `double`（`Round` 的结果是数学上的整数值，但装箱后仍是 `double`）。
`NaN` / `±∞` 都是合法的 `double` 值，**不构成类型错误也不构成失败**。

**谁负责 Dispose**：本插件重写成了空体，且**没有调用 `base.Dispose()`**。
基类 `Dispose` 的实体是"释放所有输出端口承载的 `IDisposable` 旧值"，
本插件输出是 `double`、不是 `IDisposable`，所以扫不到任何东西，不泄漏。
但这段照抄进任何有图像输出的新插件就是真泄漏——基类的轮首回收与终态回收都挂在 `IDisposable` 上。

## 三、宿主白拿的契约（本插件用到的每一条）

| 契约 | 基类 / 共享件给的行为 | 本插件哪里靠它 |
| --- | --- | --- |
| 端口发现 | 反射本类型公开实例属性，`IInputPort` / `IOutputPort` 自动进 `Inputs` / `Outputs` 两张字典 | 四个端口都是字段初始化即注册，无需登记 |
| 默认成功 | `Execute` 预置 `Success=true`、`ErrorMessage=""`，结束后按 `Success.Value is true` 回报引擎 | 正常路径一行都不写 |
| 异常转失败 | 未捕获异常 → `Fail("{异常类型名}: {消息}")` + 日志 `[{实例名}] 未捕获异常已转为步骤失败：…`；`RethrowOnException`（默认 false）决定是否继续上抛中断当轮 | 1/2/3 步取值在 try 外，靠这条兜底 |
| 轮首回收输出 | `AutoDisposeRoundOutputs`（默认 true）在 `RunAlgorithm` 之前释放输出端口承载的 `IDisposable` 旧值 | 本插件输出 `double`，这条对它是**空转** |
| 统一灌值 | `ApplyConfigValues`：存盘字典 → 输入端口（**已连线端口跳过**）+ `[StepConfig]` 属性；转换失败宽容跳过 | 三个参数全走这条路灌进来（本插件没有配置属性） |
| 默认确认 | `OnConfirm`：未连线端口的手填值逐个写回存盘字典；已连线端口 `RemoveInputValue` | 未重写，面板确认后 `A`/`B`/`Operation` 都落键 |
| 默认取消 | `OnCancel` 空实现（试运行读已保存的 `InputValues`、不落盘） | 未重写 |
| 必填检查 | 编译期判据 `IsRequired && LinkedSource == null && InputValues 无该键或值为 null` → `[参数缺失] … 的必填参数 'X' 未配置！` | 三个输入都吃这条（`IsRequired` 基类默认 `true`，本插件未覆写） |
| 连线类型检查 | `FlowCompiler.IsLinkable`：object 双向放行、同/派生放行、枚举目标 + 字符串源放行、数值族互转、字符串 ⇄ 数值/布尔；**bool 不算数值族、数值不算枚举源** | `A`/`B` 拒 bool 与图形对象；`Operation` 只认字符串源 |
| 枚举下拉 | 端口类型是枚举 → 识别为"只能选"，候选从端口**类型**现场取 `Enum.GetNames`（不依赖创建期快照）；**无链接按钮、无清除按钮** | 体现在 `Operation` 那一行 |
| 基类 `Dispose` | 释放输出端口承载的 `IDisposable` 值，单个失败不打断整体 | 重写成空体且没调 base（第二节解释为什么不泄漏） |
| 并行门禁 | `FlowCompiler` 在并行分支内检查未标注的算子并报 `[并行不安全]`；特性按类型 **FullName 字符串**匹配（绕开"两份 `Core.Interfaces.dll`"的程序集身份不一致） | `[ParallelSafe]` 四条判据全满足 |

**没有自定义视图**：不实现配置视图接口，宿主回退到自动生成的参数面板，
所以"界面长什么样"不由本工程决定——用户页里凡涉及面板形态的描述，口径都在宿主侧。

## 四、不变量清单

A 组：违反就编译红 / 抛 / 闸门红。B 组：违反不报错，只出诡异现象（这一组才是本页的重点）。
每条按"症状 → 机理 → 守门"写。

### A 组

**A1 `Success` / `ErrorMessage` 必须用基类那一对**
症状：自己声明一个同名的 `OutputPort<bool> Success` 之后，节点永远显示失败、错误信息永远空。
机理：属性遮蔽（CS0108）——插件写的是派生端口，`Execute` 读的是基类端口，两个对象。
守门：编译器对未加 `new` 的同名成员出警告；**同程序集的 `DataConversionPlugin` 里留着这条的显式注释**
（那两个插件都因这条返工过）。新写插件时别把那段注释删掉。

**A2 端口名 = 存盘键，不能与属性名混用**
症状：`ApplyConfigValues` 灌不进值（字典里查不到键），或文档写的名字和报错里的对不上。
机理：`InputValues` 的键是**端口名**（`A` / `B` / `Operation`），不是 C# 成员名（`ValueA` / `ValueB`）。
守门：`FlowCanvasChecks\HelpManualCheck.cs` 只核手册与源码的字面一致性（HM 系列），
端口语义本身**没有专属断言，靠人工核对**——改端口名要同步改手册与老方案的兼容预期。

**A3 三行取值必须在 `try` 之外**
症状：上游给了装错类型的对象，日志里是 `运算失败: …` 而不是基类的"未捕获异常已转为步骤失败"。
机理：`GetTypedValue()` 是 `(T)GetActualValue()` 强转，失败抛 `InvalidCastException`。
现在这条异常**由基类接**，`ErrorMessage` 带异常类型名前缀；
挪进 `try` 就变成自家 `catch`，`ErrorMessage` 只剩裸消息（不带类型名）。
守门：无断言（两种落点都是失败，行为差异只在文案前缀）。**要改就同时改用户页「排障」的文案行**。

**A4 `[ParallelSafe]` 的成立条件不许破**
症状：并行分支里编译报 `[并行不安全]`；或者标住了但并行跑出脏结果。
机理：判据是"无未加锁的 static 可变字段 / 不碰进程级独占资源 / 不写全局共享状态 / 长阻塞要分步检查取消令牌"。
本插件四条全满足（无 static、无 IO、无共享写、微秒级返回）。
守门：`Engine\FlowCompiler.cs` 的并行分支检查（报错文案 `[并行不安全] 分支内步骤 …（算子 …）未声明 [ParallelSafe]`）；
匹配按 FullName 字符串——别在自己的命名空间里复制一份同名特性。

**A5 枚举成员只能在末尾追加**
症状：老方案重开后运算类型悄悄换了一个。
机理：枚举进 JSON 落的是**序号**（`Add=0` … `Ceiling=16`），回读时数字串也走 `Enum.Parse`/`Enum.ToObject`。
插队改序 = 同一个数字指向另一个成员，**全仓所有枚举配置同此理**。
守门：无断言。这是存盘格式的硬约束，评审时按"追加还是插入"看 diff。

### B 组（静默故障）

**B1 `NaN` 不是失败，且会传染**
症状：节点绿、下游全绿，最后上报出一个 `NaN` 字符串；或者某个条件分支"永远不成立"。
机理：`Divide` 显式判 `b == 0` 给 `NaN`，`Sqrt`/`Log`/`Log10` 的非法域由 `Math.*` 返回 `NaN`，
`NaN` 参与任何算式还得 `NaN`；本插件不特判、不写失败。
守门：**无断言，靠人工核对**（语言行为，不是实现 bug）。
若将来改成"含 `NaN` 就失败"，必须同时给确定文案，否则现场只看到步骤变红不知道为什么。

**B2 `double` 溢出不抛、得 `±∞`**
症状：`Exp` / `Power` / `Multiply` 之后值变成无穷大，但没有任何异常记录。
机理：IEEE `double` 溢出是值语义不是异常，`catch` 那一支根本不会触发。
守门：无断言。要拦就得给"是否有限"的判定手段（本节点成员表里没有），现实做法是下游「数值比较」跟大数比。

**B3 未连线取声明默认值，不是取空**
症状：只连了 `A`，结果一直在跟 `0` 做运算（`Multiply` 恒 0、`Add` 恒等）。
机理：`InputPort` 构造时把默认值写进手填槽位，`GetActualValue()` 在无连线时返回它。
默认值就在源码的构造调用里，**改默认值 = 改所有"从没填过这一行"的历史方案的行为**。
守门：无断言。

**B4 已连线时手填值不生效**
症状：参数面板改了数字没反应。
机理：`TypedValue` / `Value` 读写的都只是手填槽位；生效值走 `ActualValue`（连线缓存优先）。
守门：`Shard\Core.Interfaces\DataPort\InputPort.cs` 里那段"★ 语义陷阱"注释；
新代码取值一律用 `GetTypedValue()` / `ActualValue`，别直接读 `Value`。
（运行期灌值与"用户编辑"不可混判，这条在 AGENTS.md R35 有实测事故记录。）

**B5 一元运算忽略 `B`，但 `B` 照样必填**
症状：选 `Sqrt` 之后一直报"必填参数 'B' 未配置"，看着莫名其妙。
机理：必填检查只看"有没有来源"，不看算法体用不用得上；三个输入的 `IsRequired` 都是基类默认 `true`。
守门：`Engine\FlowCompiler.cs` 的必填项检查。
**这是刻意保留的宽松失败还是设计缺口，目前口径是保留**——要改就得给"按运算类型动态必填"的机制，
那是端口级的事，不该在算法体里补。

**B6 `_ => double.NaN` 兜底不许去掉**
症状：手工往 `.vms` 里塞了非法序号（比如 `99`）之后，整条流程红。
机理：认不出的枚举值走默认分支给 `NaN`（判"结果不对"）而不抛（判"整轮失败"）。
删掉它，行为从"结果不对"升级为"流程中断"——对产线是更坏的结果。
守门：无断言。注释与本页都要求保留。

## 五、这个插件在闸门里的位置

档位登记在 `FlowCanvasChecks\HelpManualCheck.cs` 的 S 档表里
（用户页 ≥3、开发页 ≥1、开发页 ≥100 行；HM2 禁止往 `Help\` 根放页、HM3 要求目录名逐字等于类名、
HM12 要求开发页文件名 `NN-dev-`、HM12b 禁止再套一层子目录、HM13 钉行数下限）。

本插件**没有专属断言文件**，它属于"由框架契约断言顺带覆盖"的那一类，能查到的真实断名只有这几条：

- `FlowCanvasChecks\MotionZMotionChecks.cs` 的 `RunStepConfigOptionContract()`——
  枚举端口的整套形态（"★枚举端口用下拉""★枚举端口不提供链接按钮""★枚举端口不提供清除按钮"
  "★枚举候选从端口**类型**现场推""没标 [Display] 的成员回退成员名"）。
  这些打在 `LinkableValueEditor` / `VariableBindingViewModel` 上，**对所有枚举端口生效**，
  包括本插件的 `Operation`。
- `FlowCanvasChecks\PortBindChecks.cs` 的 `Run()`——连线判据 `TypeHelper.CanBindTo` 的 14 条
  （"【判据】整型 → 浮点可绑（数值拓宽）""【判据】任意类型 → object 端口可绑"等）。
  注意它守的是**绑定弹窗**那把尺子，与编译期的 `FlowCompiler.IsLinkable` 是两处判据；
  `IsLinkable` **没有断言覆盖，靠人工核对**。
- `FlowCanvasChecks\ExecutionChecks.cs` / `LogicBruteforceChecks.cs`——容器执行语义、
  取消与重复启动（如"停止后执行任务退出""B2：看门狗自检（三层 9999 计次循环 → 2s 内被协作取消打断并如实记超时）"）。
  它们跑的是自己的桩算子（`TracePlugin` / `CountingPlugin`），**不直接调用本插件**。

所以：动到 `Shard\Core.Interfaces`（端口 / 基类 / 值转换器）的任何一条，
都要按"全量重建 + 跑 gate"走，不能只看本工程编译通过；
而改动本插件算法体（那 17 个分支），**当前没有任何断言会红**，只能靠人工核对手册与真值表。
