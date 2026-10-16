# 类型转换 · 开发：结构、契约与不变量

> 分类：数据处理 ｜ 来源：插件 Plugin.Utility ｜ 读者：开发

S 档条目：架构、宿主契约、不变量合在本页。源码 `DataConversionPlugin.cs`（73 行），
命名空间 `VisionMaster.Plugins.Util`，程序集由 `Plugin.Util.csproj` 产出。
它是四个工具插件里**唯一带"契约返工史"**的那个——文件里那段注释是本页最重要的读法。

## 一、结构：一个类、四个端口、零私有状态

```
DataConversionPlugin : VisionPluginBase    ← 运行期算子
                       [ParallelSafe]        ← 注释口径：纯计算类
```

没有配置视图接口、没有动态端口接口、**没有任何 `[StepConfig]` 属性**、没有私有字段，
`Initialize()` / `Dispose()` 重写成空体。
全部状态就是三个输入端口的手填槽位 + 输出端口 `Result`（`object`）里的最后一个值。

`RunAlgorithm` 的完整流程（两条早退 + 一个 catch）：

```
1. input   = InputValue.GetTypedValue()        ← object，什么都能装
2. target  = Target.GetTypedValue()            ← 枚举
3. format  = Format.GetTypedValue() ?? "yyyy-MM-dd HH:mm:ss"    ← ★ 空值回落默认格式
4. if (input == null) { Result.Value = null; Success.Value = true; return; }   ← 早退①
5. try {
       Result.Value = target switch {
           Int32    => Convert.ToInt32(input),
           Double   => Convert.ToDouble(input),
           Boolean  => Convert.ToBoolean(input),
           String   => input.ToString(),
           DateTime => DateTime.ParseExact(input.ToString(), format, null),
           _        => input                    ← 早退②：认不出就原样透传
       };
       Success.Value = true;
   } catch (ex) {
       Logger.Error("{InstanceName} 类型转换失败: {ex.Message}");
       Result.Value = null; Success.Value = false;              ← 注意：没写 ErrorMessage
   }
```

第 5 步的 `Success.Value = true` 是**冗余但无害**的（基类已预置 true）；
真正有语义的是第 4 步与 catch 那三支。

## 二、状态存哪 / 结果类型怎么定 / 谁负责 Dispose

**状态存哪**：同「数学计算」——只有端口。没有轮次缓存、没有 static 可变字段。
`Convert.*` 与 `DateTime.ParseExact` 都是纯函数（区域设置是进程级输入，不是插件状态），
所以 `[ParallelSafe]` 成立。

**结果类型怎么定**：**这是本插件设计上最关键的一点**——
`Result` 声明成 `OutputPort<object>`，装箱类型由 `Target` 在运行期决定：

```
Target=Int32    → Result 里装的是 System.Int32
Target=Double   → System.Double
Target=Boolean  → System.Boolean
Target=String   → System.String
Target=DateTime → System.DateTime
default 透传     → 上游给什么就是什么
input 为 null    → null
```

后果是**编译期无法校验下游**：`object` 目标/来源在连线判据里一律放行，
错配要到运行期取值时才炸。这是"一个节点覆盖五种转换"的必然代价，
也是手册必须写清"转出来是什么 CLR 类型"的原因。
要恢复静态可校验，只能拆成五个插件（每个一个强类型输出端口），
那时工具箱里会多四行条目——当前口径不换。

**谁负责 Dispose**：重写成空体、没调 `base.Dispose()`。
`Result` 装的是 `int`/`double`/`bool`/`string`/`DateTime`，
都不是 `IDisposable`，基类轮首回收与终态回收都扫不到东西，不泄漏。
**但这是有条件的**：如果哪天有人给这个节点加"输出图像"或"输出区域"的路径
（例如把文本转成 `HImage`），空体 `Dispose()` 立刻变成真泄漏，
而 `AutoDisposeRoundOutputs` 会自动接管（前提是新写的输出确实实现了 `IDisposable`）——
届时**不要再手写"开轮释放上一轮输出"**。

## 三、宿主白拿的契约（本插件用到的每一条）

| 契约 | 基类 / 共享件给的行为 | 本插件哪里靠它 |
| --- | --- | --- |
| 端口发现 | 反射公开实例属性，`IInputPort` / `IOutputPort` 自动进两张字典 | 四个端口字段初始化即注册 |
| 默认成功 | `Execute` 预置 `Success=true`、清空 `ErrorMessage`，结束后按 `Success.Value is true` 回报引擎 | 第 4 步与 `Success=true` 都是在**明示**这条，不是必需 |
| 异常转失败 | 未捕获异常 → `Fail("{异常类型名}: {消息}")` + 日志"未捕获异常已转为步骤失败" | 本插件**自己先 catch 了**，所以这条兜不到（见 A4） |
| 轮首回收输出 | `AutoDisposeRoundOutputs`（默认 true）在 `RunAlgorithm` 之前释放输出端口的可释放旧值 | 输出没有可释放值，空转 |
| 统一灌值 | `ApplyConfigValues`：存盘字典 → 输入端口（已连线跳过），转换走 `ValueConverter`，**失败宽容跳过** | 三个参数都靠它灌进来；"跳过"这一面见 B5 |
| 默认确认 | `OnConfirm`：未连线端口逐个写回字典；已连线端口 `RemoveInputValue` | 未重写 |
| 必填检查 | `IsRequired && 无连线 && 字典无该键或值为 null` → `[参数缺失] … 'Input' 未配置！` | `Input` 默认值是 null，**最容易触发这条**（见 B1） |
| 连线类型检查 | `FlowCompiler.IsLinkable`：`object` 双向放行、string 目标放行、枚举目标只认字符串源 | `Input` 全放行、`Target` 拒数值源、`Result` 全放行 |
| 绑定弹窗判据 | `TypeHelper.CanBindTo`：`object` 双向放行（实测来的）、数组可按标量绑、解析不出类型当 object | `Result` 在弹窗里什么都能连；`Input` 什么上游都进得来 |
| 枚举下拉 | 端口类型是枚举 → 只给下拉、不给链接、不给清除，候选现场从端口类型取 | `Target` 那一行 |
| 基类 `Dispose` | 释放输出端口承载的可释放值，单个失败不打断整体 | 空体重写（第二节说明为何不泄漏） |
| 并行门禁 | 并行分支内检查未标注算子，按 FullName 字符串匹配特性 | `[ParallelSafe]` |

**没有自定义视图**：宿主回退到自动生成的参数面板，三行按端口名排序
（`Format` / `Input` / `Target`）。"界面长什么样"不由本工程决定。

## 四、不变量清单

A 组：违反就抛 / 编译红 / 闸门红。B 组：违反不报错、只出诡异现象（本页重点）。

### A 组

**A1 `Success` / `ErrorMessage` 必须用基类那一对——本插件为此返工过**
症状：节点永远显示失败、错误信息永远空。
机理：属性遮蔽（CS0108）。早期版本在派生类里自声明了同名 `Success` 属性，
插件写的是派生端口、`Execute` 读的是基类端口，两个对象，永不相交。
守门：编译器对未加 `new` 的同名成员出警告；**这条的历史就写在源码注释里**
（"`Success` 复用基类端口：此前自声明同名属性会影子隐藏基类 `Success`…"）。
**删掉这段注释 = 把返工史删掉**，下一个改这个文件的人很可能再把同名属性加回来。

**A2 端口名 = 存盘键**
症状：字典里的键与端口名不一致，`ApplyConfigValues` 灌不进值。
机理：键是端口名（`Input` / `Target` / `Format`），C# 成员名 `InputValue` 只是代码里的好听名字。
守门：无断言。改端口名要同步手册与老方案兼容预期。

**A3 `Result` 必须保持 `object`**
症状：转成强类型输出端口之后，历史方案大量连线报 `[连线类型不匹配]`；
或者被迫拆成五个插件。
机理：本插件的转换结果类型运行期才知道，声明成 `object` 是它唯一的合法形态。
守门：无断言。**动它之前先回答"五种目标类型怎么共用一个端口"**。

**A4 catch 里"只写日志不写 ErrorMessage"是当前契约，改动要连着文案**
症状：步骤红了、错误信息列是空的（现场只能翻日志）。
机理：自家 catch 抢在基类兜底之前，且没调语义化的失败方法。
如果要用 `Fail("…")` 补上，必须同时给用户页的「排障」一条确定文案，
否则现场看到的是一句只有异常原文的噪音。
守门：无断言，靠人工核对。**这不是"忘了写"，是当初为了避开 A1 那次返工留下的现状。**

**A5 `DateTime.ParseExact` 的第三个参数保持 `null`**
症状：改成 `CultureInfo.InvariantCulture` 之后，
现场"以前能解析的日期文本现在解析不了"。
机理：`null` 表示按**当前区域信息**解析，与本文件其它转换（`Convert` 家族）保持同一口径
——整个插件都跟着机器区域走，不是跟着不变区域走。
守门：无断言。要改就五个分支一起改口径，并在手册里写明"跨机器行为一致"，别只改一个。

### B 组（静默故障）

**B1 `Input` 默认 `null` 让"必填"这一关变得容易踩**
症状：从工具箱拖出来、只选了 `Target` 就编译，报
`[参数缺失] '…' 的必填参数 'Input' 未配置！`，而用户觉得"输入端口不该要值"。
机理：必填检查要求存盘字典里该键的值**非 null**；`object` 端口本来就没有非空的初值可写。
守门：`Engine\FlowCompiler.cs` 的必填项检查（判据与画布角标逐字对齐）。
**别为了让新节点开箱即用而把 `IsRequired` 关掉**——那等于让"漏接输入"变成静默绿。

**B2 `input == null` 判成功**
症状：漏连线/漏填值时这一步绿着过去，下游拿到 `null` 才炸，报错位置离病根很远。
机理：显式分支 `Result=null; Success=true; return;`。
守门：**无断言**。这是刻意口径（弱类型管道里"空值透传"比"空值即失败"更常用）。
改成失败会影响所有"允许某轮无值"的配方，动它之前要过一轮方案普查。

**B3 认不出的枚举值原样透传、不报错**
症状：手工改坏 `.vms`（塞了 `99`）之后，这一步"什么都没做"却判成功。
机理：`_ => input` 兜底分支。
守门：无断言。**保留它**——抛异常会让"结果没转"升级为"整条流程红"。

**B4 `Format` 端口值为空时回落默认格式**
症状：用户清空 `Format` 想"用默认解析"，实际拿到的是"严格按 `yyyy-MM-dd HH:mm:ss` 解析"。
机理：`?? "yyyy-MM-dd HH:mm:ss"` 写在算法体里，界面上的空值不等于代码里的空值。
守门：无断言。

**B5 灌值阶段的宽容 catch 会吞掉配错**
症状：老方案升级后，`Target` 静默回到默认 `Double`，全程无提示。
机理：`ApplyConfigValues` 对每个键单独 try —— 转换失败就跳过该端口，不报错。
枚举的存盘数字走 `Enum.ToObject` 那条路（`Convert.ChangeType` 本身不认"数字 → 枚举"，
早先在这里抛异常被吞，**全仓所有枚举配置一起中招**）。
守门：`FlowCanvasChecks\PortBindChecks.cs` 与 `MotionZMotionChecks.cs` 的枚举候选断言
覆盖的是**界面侧**；存盘往返侧**无断言，靠人工核对**。

**B6 `bool` 不在数值族，`Convert.ToBoolean` 也不认 `1`/`0` 文本**
症状：PLC 给文本 `"1"` 转 `Boolean` 失败。
机理：`Convert.ToBoolean(string)` 只认 `true`/`false`（大小写不敏感）；
`Convert.ToBoolean(int)` 认 0/非 0。**同一个目标类型，入参是文本还是数值决定成败**。
守门：无断言（CLR 行为）。要支持 `1`/`0` 文本，先转 `Int32` 再转 `Boolean`（两个节点）。

**B7 `[ParallelSafe]` 的成立条件**
症状：并行分支里报 `[并行不安全]`；或标住之后并行跑出脏结果。
机理：判据四条——无未加锁 static 可变字段 / 不碰进程级独占资源 / 不写全局共享状态 /
长阻塞要分步检查取消令牌。本插件四条全满足（无 static、无 IO、无共享写、微秒级返回）。
守门：`Engine\FlowCompiler.cs` 并行分支检查；特性按 FullName 字符串匹配，
别在自己的命名空间复制一份同名特性。

## 五、这个插件在闸门里的位置

档位登记在 `FlowCanvasChecks\HelpManualCheck.cs` 的 S 档表
（用户页 ≥3、开发页 ≥1、开发页 ≥100 行；HM2 禁止往 `Help\` 根放页、HM3 目录名逐字等于类名、
HM12 开发页文件名 `NN-dev-`、HM12b 禁止再套一层、HM13 行数下限）。

本插件**没有专属断言**（全仓搜索 `DataConversionPlugin` 只命中档位表与源码本身），
契约级、对它间接生效的真实断名：

- `FlowCanvasChecks\MotionZMotionChecks.cs` 的 `RunStepConfigOptionContract()`——
  "★枚举端口用下拉""★枚举端口不提供链接按钮""★枚举端口不提供清除按钮"
  "★枚举候选从端口**类型**现场推""没标 [Display] 的成员回退成员名"
  "★两处枚举候选共用同一个工厂（自动参数面板 + 绑定窗口…）"。打在共享控件/VM 上，
  对 `Target` 生效。
- `FlowCanvasChecks\PortBindChecks.cs` 的 `Run()`——`TypeHelper.CanBindTo` 的 14 条判据，
  其中 **"【判据】任意类型 → object 端口可绑"**、**"【判据】object → 具体类型端口可绑（类型待定，
  交给运行期校验）"**、**"【判据】任意类型 → string 端口可绑（字符串化是有意放开的）"**
  三条正是本插件 `Input` / `Result` 弱类型的依据。
  注意它守的是绑定弹窗那把尺子；编译期的 `FlowCompiler.IsLinkable` **没有断言覆盖，靠人工核对**。
- `FlowCanvasChecks\GlobalVariableChecks.cs`——`object` 默认类型、文本按类型解析这些口径
  在变量侧的同族断言（如"【当前值】非法文本提交失败并给出原因、模型保持干净"），
  与"宽进严出"的设计取向同源（AGENTS.md R34）。

结论：改算法体（那五个分支、null 早退、catch 的三行）**当前没有任何断言会红**，
必须人工核对本页与用户页；改 `Shard\Core.Interfaces` 的端口 / 值转换器 / 基类
则按"全量重建 + 跑 gate"走。
