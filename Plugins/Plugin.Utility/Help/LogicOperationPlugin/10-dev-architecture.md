# 逻辑运算 · 开发：结构、契约与不变量

> 分类：逻辑判断 ｜ 来源：插件 Plugin.Utility ｜ 读者：开发

S 档条目：架构、宿主契约、不变量合在本页。源码 `LogicOperationPlugin.cs`（54 行），
命名空间 `VisionMaster.Plugins.Util`，程序集由 `Plugin.Util.csproj` 产出（六个插件类共用一个程序集，
手册按类名分目录，本页只管这一个）。
用户侧已有「概览」「端口与参数」「排障」三页，本页**不重复其正文**，只写实现约束。

## 一、结构：一个类、四个端口、零状态、零输出副作用

```
LogicOperationPlugin : VisionPluginBase   ← 运行期算子
                       [ParallelSafe]       ← 注释口径：纯计算类
```

没有配置视图接口、没有动态端口接口、**没有任何 `[StepConfig]` 属性**、没有私有字段，
`Initialize()` / `Dispose()` 重写成空体。全部状态就是三个输入端口的手填槽位，
`Result` 每轮被无条件覆写。

`RunAlgorithm` 的完整流程（**没有早退、没有 try/catch、没有日志**）：

```
1. a = InputA.GetTypedValue()           ← bool
2. b = InputB.GetTypedValue()           ← bool
3. operation = Operation.GetTypedValue()← 枚举 LogicOperationType
4. Result.Value = operation switch {
       And   => a && b,   Or   => a || b,   Not  => !a,
       Xor   => a ^  b,   Nand => !(a && b), Nor => !(a || b),
       _     => false };
```

第 4 步之后方法就结束了。**"什么都不写"是契约要求的**：
基类 `Execute` 进 `RunAlgorithm` 前预置 `Success=true` 并清空 `ErrorMessage`，
纯计算类不显式失败就等于成功。在这里补一句 `Success.Value = true` 冗余无害；
反过来"业务失败却不写 false"才是真错（见第四节 B1）。

## 二、状态存哪 / 结果类型怎么定 / 谁负责 Dispose

**状态存哪**：只有端口。`bool` 端口的"空"和 `false` 在 CLR 层无法区分，
所以本插件没有"未配置"这个状态，只有"取到 `false`"这一个状态——这个事实是 B3 的根。

**结果类型怎么定**：`Result` 是 `OutputPort<bool>`，类型在构造时写死，
不随 `Operation` 变。`bool` **不属于数值族**，所以它的下游连线面比 `double` 窄得多
（只能连 `bool` 端口或 `string` 端口，或 `object` 端口），这是它和「数值比较」的实质差别。

**谁负责 Dispose**：重写成空体、没调 `base.Dispose()`。
输出是 `bool`、不是 `IDisposable`，基类"释放输出端口承载的可释放值"扫不到东西，不泄漏。
**照抄这段进任何有图像/区域输出的插件之前，先想清楚有没有可释放的输出。**

## 三、宿主白拿的契约（本插件用到的每一条）

| 契约 | 基类 / 共享件给的行为 | 本插件哪里靠它 |
| --- | --- | --- |
| 端口发现 | 反射公开实例属性，`IInputPort` / `IOutputPort` 自动进两张字典 | 四个端口字段初始化即注册 |
| 默认成功 | `Execute` 预置 `Success=true`、清空 `ErrorMessage`，按 `Success.Value is true` 回报引擎 | 正常路径一行都不写 |
| 异常转失败 | 未捕获异常 → `Fail("{异常类型名}: {消息}")` + 日志 `[{实例名}] 未捕获异常已转为步骤失败：…`；`RethrowOnException` 默认 false | 本插件不 catch，全靠这条兜底 |
| 轮首回收输出 | `AutoDisposeRoundOutputs`（默认 true）在 `RunAlgorithm` 之前释放输出端口承载的可释放值 | 输出 `bool`，这条对本插件是**空转** |
| 统一灌值 | `ApplyConfigValues`：存盘字典 → 输入端口（已连线端口跳过）；转换失败**宽容跳过** | 三个参数全走这条路灌进来 |
| 默认确认 | `OnConfirm`：未连线端口逐个写回字典；已连线端口 `RemoveInputValue` | 未重写 |
| 默认取消 | `OnCancel` 空实现（试运行读已保存的 `InputValues`、不落盘） | 未重写 |
| 必填检查 | `IsRequired && 无连线 && 字典无非空值` → `[参数缺失] … 的必填参数 'A' 未配置！` | 三个输入都吃这条（`IsRequired` 基类默认 `true`） |
| 连线类型检查 | `FlowCompiler.IsLinkable`（见 B5 那张表）：数值族互转、string ⇄ 数值/布尔、枚举目标认字符串源；**`bool` 不算数值族** | `A`/`B` 拒数值上游、`Operation` 拒数值上游 |
| 绑定弹窗判据 | `TypeHelper.CanBindTo`（与上面是**两处判据**，一把尺子量两次的原则见注释） | 决定弹窗里哪些上游会被列出来 |
| 枚举下拉 | 端口类型是枚举 → 只给下拉，候选从端口**类型**现场取；**无链接、无清除** | 体现在 `Operation` 那一行 |
| 画布角标 | 必填缺失的画布投影（与编译判据逐字对齐），提示里逐行列端口名 | 三个输入任一缺失都亮叹号 |
| 基类 `Dispose` | 释放输出端口的可释放值，单个失败不打断整体 | 空体重写（第二节说明为何不泄漏） |
| 并行门禁 | 并行分支内检查未标注算子并报 `[并行不安全]`；按类型 **FullName 字符串**匹配特性 | `[ParallelSafe]` 注释写明了判据 |

**没有自定义视图**：不实现配置视图接口，宿主回退到自动生成的参数面板，三行按端口名排序
（`A` / `B` / `Operation`）。所以"界面上这一行长什么样"由宿主决定，不由本工程决定。

## 四、不变量清单

A 组：违反就编译红 / 抛 / 闸门红。B 组：违反不报错、只出诡异现象（这一组才是本页的重点）。

### A 组

**A1 `Success` / `ErrorMessage` 必须用基类那一对**
症状：自声明同名 `OutputPort<bool> Success` 之后节点永远显示失败、错误信息永远空。
机理：属性遮蔽（CS0108）——插件写派生端口、`Execute` 读基类端口，两个对象。
守门：编译器对未加 `new` 的同名成员出警告；同程序集的 `DataConversionPlugin.cs` 里留着这条的
显式返工注释（那两个插件都因这条返工过）。**新写插件时不要把同名 `Success` 加回来。**

**A2 端口名与 C# 成员名不能混用**
症状：`ApplyConfigValues` 灌不进值（字典里查不到键），或报错文案里的参数名和界面标签对不上。
机理：`InputValues` 的键是**端口名**（`A` / `B` / `Operation`），不是成员名（`InputA` / `InputB`）。
守门：手册端口表首列的标识符逐个回源码命中（`FlowCanvasChecks\HelpManualCheck.cs` 只核手册与源码的
字面一致性）；端口语义本身**没有专属断言，靠人工核对**。改端口名 = 改存盘键，老方案静默丢参数。

**A3 新增运算成员必须在末尾追加**
症状：老方案重开后运算换了另一个，判据静默翻转。
机理：枚举进 JSON 落的是**序号**（`And=0` … `Nor=5`），回读时数字串也照样认。
插队改序 = 同一个数字指向另一个成员，全仓所有枚举配置同此理。
守门：**无断言**。这是存盘格式的硬约束，评审时按"追加还是插入"看 diff。

**A4 `[ParallelSafe]` 的成立条件不许破**
症状：并行分支里编译报 `[并行不安全]`；或标住之后并行跑出脏结果。
机理：判据四条——无未加锁 static 可变字段 / 不碰进程级独占资源 / 不写全局共享状态 /
长阻塞要分步检查取消令牌。本插件四条全满足。
守门：`Engine\FlowCompiler.cs` 的并行分支检查（文案 `[并行不安全] 分支内步骤 …（算子 …）未声明 [ParallelSafe]`）；
特性匹配按 FullName 字符串，别在自己的命名空间复制一份同名特性。

**A5 六个分支的运算符不能凭直觉换**
症状：`Xor` 写成 `a || b`、`Nand` 写成 `!(a ^ b)` 这类"看着差不多"的替换。
机理：六个成员里 `And`/`Or` 用的是短路布尔运算符（`&&`/`||`），
`Xor` 用的是非短路的 `^`——**这是刻意的**（两边都得是真表达式）。
`Nand`/`Nor` 是对 `&&`/`||` 取反，不是对 `^` 取反。
守门：**无断言**（真值表本身由「概览」那页给全，改分支要逐行对）。

### B 组（静默故障）

**B1 `Result=false` 不是失败**
症状：节点是绿的，可产品明明不满足条件。
机理：`Result` 只是一个数据；本插件恒判成功。NG 语义要由下游条件分支 / 判定步骤产生。
守门：用户页「排障」与「概览」的口径段。
**不要擅自把 `Result=false` 变成 `Fail(...)`**——那会让"判 NG"直接阻断流程，
与现有所有配方语义冲突（同族判断见「数值比较」开发页 B1）。

**B2 不打日志是常态，不是漏写**
症状：现场想"在日志里看到这一步算了什么"，什么都没有。
机理：算法体里没有一行 `Logger` 调用。
守门：无断言。要加日志请先想清楚节拍量——这个节点常在判定链里高频执行，
每轮一行 Info 会淹掉日志文件。真要留痕应该用步骤状态 + 下游记录节点。

**B3 未连线取声明默认值 `false`，不是取空**
症状：`And` 恒 `false`、`Nand`/`Nor` 恒 `true`（看着像"莫名成功"）。
机理：`InputPort` 构造时把默认值写进手填槽位，`GetActualValue()` 无连线时返回它；
`bool` 端口"空"和 `false` 在 CLR 层无法区分。
守门：无断言。
**这也是"必填检查"存在的理由**：判据看的是"有没有来源"，
但**填过 `False` 也算有来源**，所以必填检查拦不住"想用 `False` 当占位"的误配。

**B4 已连线时手填值不生效**
症状：参数面板改了勾选没反应。
机理：`TypedValue` / `Value` 读写的都只是手填槽位；生效值走 `ActualValue`（连线缓存优先）。
守门：`Shard\Core.Interfaces\DataPort\InputPort.cs` 里那段"★ 语义陷阱"注释；
新代码取值一律 `GetTypedValue()` / `ActualValue`。
另：文本 → `bool` 走 `Convert.ChangeType`，**只认 `True`/`False` 两个词**，
解析失败被 `ApplyConfigValues` 的宽容 catch 吞掉、**保留原值且不弹任何错**（AGENTS.md R35 同族实测）。

**B5 `bool` 与数值、枚举与数值之间的两道闸门方向不同**
症状：绑定弹窗里能选到某个上游，编译时却报 `[连线类型不匹配]`；或反过来。
机理：两处判据不是同一个函数——
编译期 `FlowCompiler.IsLinkable`：`object` 双向放行、同/派生放行、**枚举目标 + 字符串源放行**、
数值族互转放行、`string ⇄ 数值/布尔`放行，其余拒；
弹窗侧 `TypeHelper.CanBindTo`：`object` 双向放行（实测来的）、数组可按标量绑、其余走类型兼容。
两者对"数值 → 枚举""`bool` → `double`"都拒，但对数组、对未知类型名的处理不同。
守门：`FlowCanvasChecks\PortBindChecks.cs` 的 `Run()` 有 14 条**打在 `TypeHelper.CanBindTo` 上**
（"【判据】整型 → 浮点可绑（数值拓宽）""【判据】任意类型 → object 端口可绑""【判据】HImage → 数值端口不可绑"等）；
而 `FlowCompiler.IsLinkable` **没有断言覆盖，靠人工核对**——
改任一边的白名单都要人工确认另一边，否则会出现"弹窗放行、编译拒绝"的自相矛盾。

**B6 `Operation` 那一行没有链接按钮、也没有清除按钮**
症状：现场问"怎么让上游决定走哪种运算"，界面上做不到。
机理：枚举被当作**封闭集合**——"清除"之后没有任何"空枚举"可以表示，
"绑定"则等于把任意上游值塞进封闭集合。
守门：`FlowCanvasChecks\MotionZMotionChecks.cs` 的
"★枚举端口不提供链接按钮（上游值未必是合法枚举，绑上去等于把非法值塞进封闭集合）"、
"★枚举端口不提供清除按钮（清掉之后没有任何「空枚举」可以表示）"。
**要"由上游决定走哪个分支"应当用普通 `string`/`int` 端口，那是另一种表达能力**，
不要为此把 `Operation` 改成 `InputPort<string>`（改完存盘类型也变了，老方案全废）。

**B7 `_ => false` 兜底不许去掉**
症状：手工往 `.vms` 里塞了 `99` 之后，整条流程红。
机理：认不出的枚举值判 `false`（"结果不对"）而不抛（"整轮失败"）。
删掉它会把行为从"结果不对"升级为"流程中断"，对产线是更坏的结果。
守门：无断言。注释与本页都要求保留。

**B8 中文显示名是欠账，不许在手册里声称已解决**
症状：下拉里是 `And`/`Or`/`Not`/`Xor`/`Nand`/`Nor`，与手册写的中文对不上。
机理：`LogicOperationType` 六个成员**一个 `[Display]` 都没标**（源码逐字确认），
显示回退成员名。机制侧（`StepConfigOption.ForEnum`）已经修好，只差补特性。
守门：`FlowCanvasChecks\MotionZMotionChecks.cs` 的
"没标 [Display] 的成员回退成员名（不是回退成空白 —— 空白等于下拉里看不出选了什么）"
就是这条回退的守门断言；而"成员逐个有中文"那条闸门目前只覆盖几何 + 运动六个枚举，
`Plugin.Utility` 的运算枚举**仍在欠账清单上**（AGENTS.md R40）。
补 `[Display(Name="与")]` 是安全的：**显示换中文，存盘值仍是成员名**。

## 五、这个插件在闸门里的位置

档位登记在 `FlowCanvasChecks\HelpManualCheck.cs` 的 S 档表
（用户页 ≥3、开发页 ≥1、开发页 ≥100 行；HM2 禁止往 `Help\` 根放页、HM3 要求目录名逐字等于类名、
HM12 要求开发页文件名 `NN-dev-`、HM12b 禁止再套一层子目录、HM13 钉行数下限）。

全仓搜索 `LogicOperationPlugin` 只命中档位表与源码本身——**本插件没有专属断言文件**。
容易误认的是 `FlowCanvasChecks\LogicBruteforceChecks.cs`（38 条，B1~B4 分组）：
它名字里有"逻辑"，但它跑的是自己的桩算子（`TracePlugin` / `IndexEchoPlugin`），
穷举的是**流程容器的分支语义**（If/While/For/Parallel），与本插件的布尔运算无关。
对本插件间接生效的只有契约级断言：

- `FlowCanvasChecks\MotionZMotionChecks.cs` 的 `RunStepConfigOptionContract()`：枚举端口的整套形态
  （下拉、无链接、无清除、候选现场推、`[Display]` 回退、值与显示分离）。
- `FlowCanvasChecks\PortBindChecks.cs` 的 `Run()`：绑定弹窗侧的 14 条类型判据。
- `FlowCanvasChecks\ExecutionChecks.cs`：容器执行语义、状态回报、重复启动闸门
  （如"For 容器跑完落 Success""重复启动被忽略并留下 Warn"）。

结论：改算法体（那六个分支、兜底、默认值）**当前没有任何断言会红**，
只能靠人工核对本页与用户页的真值表；
动到 `Shard\Core.Interfaces`（端口 / 基类 / 值转换器）或 `Engine\FlowCompiler.cs` 的判据，
要按"全量重建 + 跑 gate"走，不能只看本工程编译通过。
