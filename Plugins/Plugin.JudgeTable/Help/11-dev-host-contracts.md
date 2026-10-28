# 判定表 · 开发：宿主契约

> 分类：逻辑判断 ｜ 来源：插件 Plugin.JudgeTable ｜ 读者：接手本插件、或要照它写"动态端口插件"的人

本页只写**框架侧**的约定：插件必须实现什么、框架什么时候调谁、哪些口子不能自己造。
每条都标了"不这么做会怎样"，因为这一层的失败模式大多是静默的。

## 1. 插件基类契约（`VisionPluginBase`）

| 约定 | 判据 | 违反的后果 |
| --- | --- | --- |
| 端口必须是 `public` **属性** | 编译期扫描 `public` 字段，报 `[端口声明错误]` | 反射发现不到 → 端口"凭空消失" |
| `Success` / `ErrorMessage` 用基类端口 | 不要自声明同名属性 | 影子隐藏：插件写派生端口、引擎读基类端口 → 永远失败且日志为空 |
| 失败用 `Fail("原因")` | 一行 = `Success=false` + `ErrorMessage=原因` | 只写 `Success=false` 会丢原因 |
| 异常不用自己包 try/catch | 基类统一转"本节点失败"并留堆栈 | 自写 catch 容易把异常吞成"成功" |
| `Execute` 开头预置 `Success=true` | 纯判定插件无需每次写 `Success` | — |

本插件额外守住一条：**`RunAlgorithm` 里不重建端口**（见 `10-dev-architecture`）。

## 2. 动态输出端口（`IDynamicOutputProvider`）

| 契约 | 说明 |
| --- | --- |
| 接口 | `void RebuildDynamicOutputs()`（`Core.Interfaces.IDynamicOutputProvider`） |
| 何时被调 | `FlowCompiler` 在 `ApplyConfigValues` **之后**、`LinkPorts` **之前**调一次 |
| 加端口 | `AddDynamicOutput(new OutputPort<T>(name, description))`（基类方法，同名静默不重复加） |
| 清端口 | `ClearDynamicOutputs()` 保留反射发现的固定端口，只清运行时加进来的 |
| 写值 | `Outputs.TryGetValue(name, out var port)` → `port.Set(value)`（`PortExtensions` 免强转） |
| 快照 | 同步写 `StepData.OutputPortDefinitions`（`List<DynamicPortInfo>`） |

`DynamicPortInfo` 三个字段：

```csharp
new DynamicPortInfo
{
    Name = "工位A.外径.结果",
    DataTypeName = typeof(bool).AssemblyQualifiedName,   // 必须程序集限定名
    Description = "规则「外径」的判定结果（同时写运行时变量）"
}
```

**为什么必须有快照**：下游绑定界面与编译器拿的是 `StepModel.OutputPortDefinitions`
（`FlowQueryHelper.GetAvailableVariablesTree` 把它并进上游节点的输出表），
**不依赖配置实例存活**。只加端口不写快照 → 运行期端口存在、界面上选不到、编译期按名连不上。

## 3. 动态输入端口（没有 `ClearDynamicInputs`）

基类只提供 `AddDynamicInput` / `RemoveDynamicInput`，**没有** `ClearDynamicInputs`。
本插件的做法（也建议后来者照抄）：

```
_dynamicInputNames: List<string>           // 自己跟踪"我加过哪些"
先按名字保存旧端口值
foreach name in _dynamicInputNames:
    Inputs.TryGetValue(name, out old)
    RemoveDynamicInput(name)
    (old as IDisposable)?.Dispose()        // 摘字典键 ≠ 退订上游 ValueChanged
_dynamicInputNames.Clear()
重建 → 回填值
```

**为什么必须 `Dispose` 旧端口**：`InputPort<T>` 在 `LinkedSource` setter 里订阅了上游
`OutputPort.ValueChanged`。只从字典里摘掉，委托仍指着旧端口 → 编辑期每敲一个字重建一次，
累积成托管泄漏（`Plugin.CSharpScript` 同口径）。

**不要改 `VisionPluginBase` 加一个 `ClearDynamicInputs`**：契约层改动会影响全部 32 个插件工程
（R16/R62 的连锁重建），收益只是省一个循环。

## 4. 配置灌入顺序（编译期最容易踩）

`FlowCompiler` 对每个插件实例做的是：

```
Activator.CreateInstance(type)          // 构造器里不能依赖 StepData
InstanceName = "{流程名}.{步骤名}"
ApplyConfigValues(model)                // ① 灌 [StepConfig] + 输入端口值
  └─ 本插件 override：base → 记步骤名 → RebuildAll() → ApplyPortValues（补灌动态端口）
if (plugin is IDynamicOutputProvider) RebuildDynamicOutputs()   // ② 再重建一次（幂等）
LinkPorts(...)                          // ③ 按名字把上游输出挂到输入端口
```

两条推论：

1. **动态输入端口必须在 `ApplyConfigValues` 里建好**，否则第 ③ 步按名找不到端口，
   报 `没有名为 X 的输入端口，这条连线不会生效`（整条流程编译失败）。
2. **`ApplyConfigValues` 时 `StepData` 是 null**（框架只传 model，不调 `Initialize`）。
   所以：连线迁移/清理只能在 `StepData != null` 时做；步骤名要从 `ApplyConfigValues(model)` 的
   `model.StepName` 取，不能只依赖 `Initialize`。

## 5. `InstanceName` vs `StepName`

| 场景 | `InstanceName` | `StepName` |
| --- | --- | --- |
| 配置态实例（宿主 `Activator.CreateInstance`） | `未赋值`（默认值，没人赋值） | 从 `stepData.StepName` 取 |
| 编译态实例（`FlowCompiler`） | `{流程名}.{步骤名}` | 从 `ApplyConfigValues(model)` 取 |
| 试运行（`PluginTestRunner`） | 视宿主实现 | 同上 |

端口名要求"配置态快照 == 编译态实例"，所以本插件**只用 `StepName`**；
`InstanceName` 只在拿不到步骤名时兜底。任何"端口名里带 InstanceName"的写法都会导致
下游按快照连上的线在编译期找不到端口。

## 6. 运行时变量（`IExecutionContext.LocalVariables`）

| 契约 | 说明 |
| --- | --- |
| 类型 | `IDictionary<string, object>`，一次执行一份（同一个 `ExecutionContext` 全程共享） |
| 生命周期 | 执行期创建、执行结束即丢；**不落盘**、不进变量管理 |
| 谁读得到 | 下游「变量赋值」Scope=Runtime、`If`/`While` 条件的运行时变量引用、C#脚本 `Context.GetVar` |
| 并行分支 | 上下文可能是"记账字典"（`BranchVariableRegistry`），写入即登记 key，join 后影子合并 |
| 写入失败 | 只记 Warn，不改判定、不中断流程（与"系统变量 setter 静默忽略"同口径） |

**本插件不写全局变量**：`RunAlgorithm` 里没有 `context.GlobalVariables` 的调用，
也不实现 `IGlobalVariableWriter` 相关逻辑。要全局由下游「变量赋值」Scope=Global 映射。
理由：① 全局是方案级共享状态，逐轮写 N 个会让"谁改的"不可追溯；
② 写全局需要宿主能力面，塞进纯判定算子会引入无谓的权限依赖。

## 7. 端口名约束（编译器侧）

| 约束 | 来源 | 本插件怎么守 |
| --- | --- | --- |
| 名字里不许有 `[` / `]` | `FlowCompiler` 用 `^([^\[]+)(\[(\d+)\])?$` 拆下标 | `Sanitize` 把非法字符变 `_` |
| 同名端口只保留先到的一个 | `AddDynamicOutput` 静默去重 | `ComputeNames` 主动加 `_2`/`_3` 后缀 |
| 端口名 = 连线键 = 运行时变量名 | 三处都按字符串寻址 | 三者同源（`rule.ResultPortName`） |
| 数组端口用 `端口名[下标]` | 编译器解析后包 `ArrayIndexProxyPort` | `Results` / `Details` 走这条 |

## 8. 配置持久化

| 环节 | 行为 |
| --- | --- |
| `[StepConfig] Rules` | 属性名即 `InputValues` 的键；**改名 = 静默丢配置** |
| `OnConfirm` | 基类把每个 `[StepConfig]` 值做 JSON 往返快照（`SnapshotConfigValue`），与界面活对象脱钩 |
| 反序列化 | `ApplyConfigValues` 用 `ValueConverter.Convert` 写回属性；集合类型按原类型反序列化 |
| 运行期字段 | 全部 `[JsonIgnore]`：`LastActual` / `LastOk` / `LastReason` / `LastFormula` / `IsExpanded` / `InputPortName` / `ResultPortName` / `SourceText` |

`JudgeRule` 只含基元类型 + 枚举 + 字符串，Newtonsoft 往返无歧义。
`RuleId` 用 Guid 字符串（稳定身份），改规则名不影响它。

## 9. 元数据与手册

| 项 | 值 |
| --- | --- |
| `[Display(Name)]` | `判定表` |
| `[Display(GroupName)]` | `逻辑判断`（工具箱分组） |
| `[Display(Description)]` | 一句话能力说明（工具箱悬停） |
| `[Display(ShortName)]` | Font Awesome 码点 `\uf0ce`（表格图标） |
| `[ParallelSafe]` | 已标注：纯托管判定，只写 `LocalVariables`，不碰硬件与全局 |
| 手册 | `Help\*.md` 经 csproj 的 `<EmbeddedResource Include="Help\**\*" />` 编进 DLL |

**改手册 = 重建插件工程**（手册是内嵌资源，不重建跑的还是旧 DLL）。

## 10. `[RuntimeState]` 为什么没标

`RuntimeStateAttribute` 定义在 `VisionMaster.Models`（VM.Core 程序集），
插件只引用 `Core.Interfaces`，物理上拿不到。

它要解决的问题（"派生只读属性别把 `FlowModel.Version` 推高"）在这里不成立：
`JudgeRule` 是插件自己的配置模型，不是 `StepModel`，它的 `PropertyChanged` 不会进
`FlowModel.OnStepPropertyChanged`。本插件对运行期字段只守两条：
**不落盘**（`[JsonIgnore]`）+ **不参与端口重建**（`OnRulePropertyChanged` 只对
`Name` / `Enabled` / `GenerateResult` 重建，其余只刷摘要）。

## 11. 与其它插件的对照

| 插件 | 相似点 | 差异 |
| --- | --- | --- |
| `Plugin.CreateRoi` | `IDynamicOutputProvider` + 快照 | ROI 没有动态输入；本插件输入也随配置生成 |
| `Plugin.CSharpScript` | 动态输入 + 动态输出 + 改名迁移 + 孤儿清理 | 脚本用"变量表"驱动，本插件用"规则表"驱动；脚本有取消快照，本插件没有（编辑即生效） |
| `Plugin.Utility/VariableAssignment` | Scope=Runtime/Global 的下游映射 | 变量赋值**是**写全局的那一端；本插件只写运行时变量 |
