# 数值比较 · 开发：结构、契约与不变量

> 分类：逻辑判断 ｜ 来源：插件 Plugin.Util（工程 `Plugin.Util.csproj`，C# 类 `ComparisonPlugin`）｜ 读者：开发

S 档条目：架构、宿主契约、不变量合在本页。源码只有一个 `ComparisonPlugin.cs`（不到 60 行），
本页的价值不在篇幅，而在**把"这个 5 行的 switch 为什么必须这么写"钉死**。
同程序集另有五个插件类（`LogicOperationPlugin` / `MathCalculationPlugin` / `DataConversionPlugin` /
`DelayPlugin` / `VariableAssignmentPlugin`），**共用同一个程序集但各自成册**，
各自的实现细节看各自目录下的开发页；本页只讲基类契约时也会点出"本插件用到了哪一条"。

## 一、结构：一个类、五个端口、零状态

```
ComparisonPlugin : VisionPluginBase        ← 运行期算子（RunAlgorithm / 端口 / 配置生命周期）
                   [ParallelSafe]           ← 编译期并行门禁的白名单标记
```

没有配置视图接口、没有动态端口接口、没有 `[StepConfig]` 属性、没有私有字段、没有 `Dispose` 内容
（`Initialize()` 与 `Dispose()` 都重写成空体）。**全部状态就是四个输入端口的"手填值"槽位**，
真值来源在上游连线与存盘字典里，插件自身不持有任何东西。

`RunAlgorithm` 的完整流程（无早退分支）：

```
1. a = ValueA.GetTypedValue()          ← 取"实际生效值"：连线值优先于手填值
2. b = ValueB.GetTypedValue()
3. type = Type.GetTypedValue()
4. tolerance = Tolerance.GetTypedValue()
5. Result.Value = type switch { 六个分支, _ => false }
   —— 不写 Success、不打日志、不 try/catch、不 return
```

第 5 步之后方法就结束了。**"什么都不写"是契约要求的，不是偷懒**：
基类 `Execute` 进 `RunAlgorithm` 前预置 `Success=true` 并清空 `ErrorMessage`，
纯计算类插件不显式失败就等于成功。反过来，本插件里补一句 `Success.Value = true`
是**冗余但无害**；而"业务失败却不写 false"才是真正的错（见第四节 B1）。

## 二、宿主白拿的契约（本插件用到的每一条）

| 契约 | 基类 / 共享件给的行为 | 本插件哪里靠它 |
| --- | --- | --- |
| 端口发现 | 反射本类型公开实例属性，`IInputPort` / `IOutputPort` 自动进两张字典；端口名为空时以属性名填充；重名用 `TryAdd`（静态属性优先） | 五个端口都显式给了名字（`A`/`B`/`Type`/`Tolerance`/`Result`），属性名与端口名**故意不同**（`ValueA` → `A`），界面看到的是端口名 |
| 默认成功 | `Execute` 预置 `Success=true`、`ErrorMessage=""`，`RunAlgorithm` 结束后按 `Success.Value is true` 回报引擎 | 正常路径一行都不写 |
| 异常转失败 | 未捕获异常 → `Fail("{Type}: {Message}")` + `Error` 日志；`RethrowOnException`（默认 false）决定是否继续上抛中断当轮 | 本插件不 catch，全靠这条兜底 |
| 轮首回收输出 | `AutoDisposeRoundOutputs`（默认 true）在 `RunAlgorithm` 之前释放输出端口承载的 `IDisposable` 旧值 | 本插件输出是 `bool`，不是 `IDisposable`，天然扫不到，这条对本插件是**空转** |
| 统一灌值 | `ApplyConfigValues`：存盘字典 → 输入端口（**已连线端口跳过**）+ `[StepConfig]` 属性；转换走 `ValueConverter.Convert`，类型不匹配宽容跳过 | 四个端口都是"参数即端口"，配置值全走这条路灌进来 |
| 默认确认 | `OnConfirm`：未连线端口的手填值写回存盘字典；已连线端口 `RemoveInputValue` | 未重写 |
| 默认取消 | `OnCancel` 空实现（试运行读已保存的 `InputValues`、不落盘） | 未重写 |
| 基类 `Dispose` | 释放所有输出端口承载的 `IDisposable` 值，单个失败不打断整体 | `Dispose()` 重写成空体且**没调 `base.Dispose()`**——本插件端口值没有 `IDisposable`，所以不泄漏；照抄这段写新插件前先想清楚有没有图像输出 |
| 并行门禁 | `FlowCompiler` 在并行分支内检查未标注的算子并报 `[并行不安全]`；**按类型 FullName 字符串匹配特性**（不是 `typeof` 等值，为的是绕开"两份 `Core.Interfaces.dll`"的程序集身份不一致） | `[ParallelSafe]` 注释写明判据：纯计算类 |

**没有自定义视图**：不实现 `IPluginCustomViewProvider`，宿主回退到自动生成的参数面板，
四行按端口排序、每行可以填常量或点链接图标。这决定了界面长什么样不由本插件决定，
也意味着 `01` / `03` / `05` 三页里凡涉及"面板上这一行怎么显示"的描述，口径都在宿主侧而不是本工程。

## 三、枚举端口这条链路（本插件最容易改错的地方）

`Type` 是 `InputPort<ComparisonType>`，它比 `double` 端口多背了三件事：

```
① 界面候选：InputPort.IsFunctionalEnum = typeof(T).IsEnum → true
              PresetOptions = Enum.GetNames(typeof(T))    → 英文成员名，顺序即声明顺序
   ⇒ 下拉显示 Equal / NotEqual / GreaterThan / LessThan / GreaterThanOrEqual / LessThanOrEqual
   ⇒ ComparisonType 没配中文 [Display]，界面就是英文（已知待办，手册不许声称显示中文）

② 存盘：枚举进 JSON 落的是**数字**（Equal=0 … LessThanOrEqual=5）

③ 回读：拆包分支里枚举**优先**处理（`ValueConverter` 在 JToken 分支先判 `targetType.IsEnum`，
   按 `token.ToString()` 走 `Enum.Parse`，数字串一样认）；非 JToken 的整数来源走
   `Enum.ToObject(targetType, Convert.ChangeType(raw, Enum.GetUnderlyingType(targetType)))`
   ⇒ 两条路都是补的：`Convert.ChangeType` 本身不认"数字 → 枚举"，早先在这里抛异常、
     被 `ApplyConfigValues` 的宽容 catch 吞掉，
     症状是"方案重开后枚举配置静默回到默认值"，且**全仓所有枚举配置一起中招**

④ 连线白名单**不认**"数值 → 枚举"：`FlowCompiler.IsLinkable` 的数值族判定不含枚举，
   所以上游 `int` 连到 `Type` 在编译期就报 `[连线类型不匹配]`。
   转换器认、编译器不认是**有意的不对称**（连线语义要显式），别把它当 bug"顺手补上"。
```

**改 `ComparisonType` 的成员顺序 = 改存盘数字**：老方案里的 `4` 会读成新顺序下的另一个成员，
静默换判据。要新增比较类型只能**在末尾追加**。同理，改名会让
"按成员名解析"的文本来源（上游文本连线、手改 `.vms`）失配。

`_ => false` 兜底分支：认不出的枚举值（比如手工往 `.vms` 里塞了 `9`）判 `false` 而不是抛。
删掉它会让这种值直接抛异常、被基类转成步骤失败——从"结果不对"变成"整条流程红"，
对产线是更坏的行为。**这个兜底不许去掉。**

## 四、不变量清单

分 A 组（违反就抛 / 编译或闸门红）与 B 组（违反不报错、只出诡异现象）。
每条按"症状 → 机理 → 守门"写。

### A 组

**A1 `Success` / `ErrorMessage` 必须用基类那一对**
症状：自己声明一个同名的 `OutputPort<bool> Success` 后，节点永远显示失败、错误日志永远空。
机理：属性遮蔽（CS0108）——插件写的是派生端口，`Execute` 读的是基类端口，两个对象。
守门：编译器对未加 `new` 的同名成员出警告；代码评审口径见 `DataConversionPlugin` 里的显式注释
（同程序集的两个插件都因这条返工过，注释留在那里）。

**A2 端口名与属性名不能混用**
症状：`ApplyConfigValues` 灌不进值（存盘字典里查不到键），或界面显示的行名和文档不一致。
机理：`InputValues` 的键 = **端口名**（`A`/`B`/`Type`/`Tolerance`），不是 C# 属性名（`ValueA`…）。
守门：手册端口表首列的标识符逐个回源码命中；改端口名等于改存盘键，老方案静默丢参数。

**A3 `[ParallelSafe]` 的成立条件**
症状：并行分组分支内编译报 `[并行不安全]`，或反过来标了之后并行跑出脏结果。
机理：判据是"无无锁保护的 static 可变字段 / 不碰进程级独占资源 / 不写全局共享状态 / 长阻塞要分步检查取消令牌"。
本插件四条全满足（无 static、无 IO、无共享写、`RunAlgorithm` 微秒级返回），所以标得住。
守门：`FlowCompiler` 编译期门禁；匹配按 FullName 字符串，别把特性写在自己的命名空间副本上。

### B 组（静默故障，这一组才是本页的重点）

**B1 "超限"不是失败**
症状：现场看到节点绿、但产品明明超差。
机理：`Result=false` 只是数据；本插件恒判成功。NG 语义要由下游条件分支 / 判定步骤产生。
守门：用户手册「排障」最后一条与「概览」口径段；改造本插件时**不要**擅自把 `Result=false`
变成 `Fail(...)`——那会让"判 NG"直接阻断流程，与现有所有配方语义冲突。

**B2 `NaN` 让六个分支集体为假且不报错**
症状：既不等于、也不不等于、既不大也不小。
机理：C# 里 `NaN` 参与的 `==`/`<`/`>` 一律 `false`，`Math.Abs(NaN) <= tol` 同样 `false`；插件无特判。
守门：无断言（这是语言行为，不是实现 bug）。排障口径写死在手册里；
若要改成"给 `NaN` 一个明确失败"，必须同时给文案，否则现场只看到步骤变红不知道为什么。

**B3 负容差把判定死区变成放松带**
症状：`Equal` 恒假、`GreaterThan` 莫名变松。
机理：六个分支都是 `± tolerance` 形式，`tol` 为负时不等式方向不变、阈值反向平移。
守门：`Tolerance` 没有范围校验（端口是裸 `double`，插件也不夹取）。
**不要**在界面承诺"非负"，也不要在没读过源码的情况下以为它被夹过。

**B4 未连线取声明默认值，不是取空**
症状：只连了 `A`，结果在跟 `0` 比。
机理：`InputPort` 构造时把默认值写进"手填值"槽位，`GetActualValue()` 在无连线时返回它。
默认值（`0.0` / `0.0` / `Equal` / `1E-06`）就在源码的构造调用里，改默认值 = 改所有历史方案的行为。

**B5 已连线时手填值不生效**
症状：参数面板改了数字没反应。
机理：`TypedValue` / `Value` 读写的都只是"手填值"槽位；生效值走 `ActualValue`（连线缓存优先）。
守门：`InputPort` 里那段"★ 语义陷阱"注释；写新插件取一律用 `GetTypedValue()` / `ActualValue`。

**B6 六个判据的容差方向不能凭直觉写**
症状：上下限判定边界差一个 `2×tol`。
机理：`GreaterThan` 用 `b + tol`、`LessThan` 用 `b − tol`（要"明显大于/小于"），
而 `GreaterThanOrEqual` 用 `b − tol`、`LessThanOrEqual` 用 `b + tol`（含死区即算达标）。
**两组方向是相反的**，这是刻意的，不是笔误。
守门：改任何一个分支前，把「概览」里的六行判据表逐字对照一遍。

## 五、这个插件在闸门里的位置

`FlowCanvasChecks\HelpManualCheck.cs` 的档位表登记本类为 **S 档**
（用户页 ≥3、开发页 ≥1、开发章节 ≥100 行）。本插件**没有专属断言文件**——
它属于"由流程引擎断言覆盖"的那一类：端口灌值 / 连线 / 存盘往返的守门在
`FlowCanvasChecks` 的 `PortBindChecks`、`GlobalVariableChecks`、`LogicBruteforceChecks`、
`VariableAssignmentCheck` 里，改基类契约时由它们统一拦。
所以动到 `Shard\Core.Interfaces` 的任何一条，都要按"全量重建 + 跑 gate"走，
不能只看本工程编译通过。
