# 几何计算 · 开发：宿主契约与移植清单

> 分类：几何工具 ｜ 来源：插件 Plugin.GeometryRelation ｜ 读者：开发

这页列**本插件白拿的宿主能力**（自己不写一行代码就得成立的那些约定），
以及照它抄一个"纯计算 + 多模式"插件时必须对齐的清单。

## 一、执行外壳：`VisionPluginBase.Execute()`

`Shard\Core.Interfaces\VisionPluginBase.cs:94-119`。进入 `RunAlgorithm` 之前：

```
Success.Value = true;  ErrorMessage.Value = "";            ← 「默认成功、显式失败」契约
if (AutoDisposeRoundOutputs) DisposePreviousRoundOutputs();  ← 轮首回收 IDisposable 输出（:134 默认 true）
try { RunAlgorithm(context); }
catch (ex) { Fail($"{ex.GetType().Name}: {ex.Message}"); Logger.Error(...); if (RethrowOnException) throw; }
```

本插件**两个策略属性都不覆写**：`AutoDisposeRoundOutputs` 保持 true（虽然它三个输出都不是 `IDisposable`，
回收对本插件是空转），`RethrowOnException` 保持 false（异常转失败、流程继续）。
业务失败一律走 `protected void Fail(string)`（:140-144，只置 Success=false + ErrorMessage，**不 return**，
所以每条 `Fail` 后面必须自己写 `return`——本文件 23 处 `Fail` 调用（:116~:293）里，只有 switch 的 `default`
写成 `break`（后面没有分支要跑，靠 :138 的 `Success` 判断挡住推图），其余全部紧跟 `return`）。

## 二、参数入口：`AutoPortConfigView`（宿主侧兜底面板）

不实现 `IPluginCustomViewProvider` 的插件走这条路径（`VisionMaster\Views\DialogViews\AutoPortConfigView.xaml.cs`）。
它对移植方的三条硬约束：

- **只列输入端口，不列 `[StepConfig]` 常量**（宿主注释原文：这是刻意的，不是遗漏；
  历史上「轴运动」的绝对/相对切换就是这么在界面上永远改不了的）。
  → 写"纯计算 + 参数"类插件时，**能进端口的参数就别留成配置属性**。
- 端口实例必须来自**插件实例**而不是 `PortDefinition` 快照：编辑器要读写实时值、要把这个实例连到上游。
- 每行 = 标签 + 值输入框 + 🔗（连上游，发 `LinkPathEvent` 请主程序呼出绑定弹窗）+ ✕（清除连线）。

配置生命周期全在基类，面板不写逻辑：
`Initialize(IStepConfigData)`（:263）→ `ApplyConfigValues`（:234-256）把 `InputValues` 灌进端口，
**已链接的端口 `IsLinked` 直接跳过**（这是"连线优先于常量"的机制，也是"上游换了值我不跟着变"的反向保证）；
`OnConfirm(IStepConfigData)`（:275-293）把未链接端口 `SetInputValue(端口名, 端口值)`、
已链接端口 `RemoveInputValue`（避免残留脏数据，运行时以链接为准）。

## 三、端口语义：三个取值入口不能混

`Shard\Core.Interfaces\DataPort\InputPort.cs`：

| 入口 | 语义 | 本插件用法 |
| --- | --- | --- |
| `Value`（object，:96-126） | **只是手动值**；写入走 `DefaultConvert` → `ValueConverter.Convert` 做跨类型转换 | 基类灌值/回写用，业务代码不碰 |
| `ActualValue`（:211-217） | **实际生效值**：链接源值 > 手动值 | 业务取值一律用它（:109-112 一次读齐） |
| `TypedValue`（:226-237） | 强类型读写的**手动值**，WPF 绑定专用 | 只在显示层读 `IsOk.TypedValue`（输出端口没有"手动值"这一层，`OutputPort.cs:115-119` 明确它与输入端语义相反，读写的就是真实值） |

`IsRequired` 默认 `true`（:28）：本插件只有 `Op` 与 `A` 保持默认，其余 11 个输入端口全部显式写
`{ IsRequired = false }`——否则"不连当量、不接图像"的正常用法会在编译期被判参数缺失。
移植时这条最容易漏：**可选的一律显式关掉必填**。

`IsFunctionalEnum` / `PresetOptions`（:38 / :44-46）在 `T` 是枚举时自动成立，候选就是 `Enum.GetNames`；
`OptionKind`（:61）是"每次打开现取候选"的口子，本插件用不到（候选是编译期枚举，不随方案变）。

## 四、枚举的显示与存盘（两条路径，别再混）

- **参数面板 / 变量绑定下拉**：`Shard\Core.Controls\LinkableValueEditor.xaml.cs:224-230`
  对枚举端口逐个 `Enum.GetNames` → `StepConfigOption.ForEnum(类型, 成员名)`；
  后者（`Shard\Core.Interfaces\StepConfigOptionSource.cs:60-63`）**只读成员上的 `[Display(Name)]`**，没标就回退成员名。
  `GeometryOp` / `LineRelationKind` / `ConstructKind` 每个成员都标了中文名 → 下拉是中文（距离 / 夹角 / 求交 / 平行垂直 / …）。
  `GeometryOp.LineRelation` 的显示名是「平行/垂直」，斜杠在文字里，不是分隔符。
- **属性网格（Flat / Card）的 `EnumGenerator`**：显示优先读成员上的 `[Description]`，没标才回退成员名。
  本插件**没有属性网格**，所以这条路走不到；靠 `[Description]` 显示中文的是 `Plugin.PreProcessing` 的算子参数面板。
- **存盘**：`.vms` 里枚举端口写的是**数值**（同一族的「几何查找」实测 `"Kind": 1`），
  回读时 `ApplyConfigValues` → `port.Value = object` → `ValueConverter.Convert` 把 Int64 / 字符串都还原成枚举。
  这三段链路各自有断言（见「不变量」A4）。

**改中文显示名是安全的**（存盘不写名字）；**改成员名或成员顺序不安全**（数值直接进方案文件，
插成员会让旧方案的数值指向别的模式）。要加模式**只在末尾追加**。

## 五、跨插件数据契约：`GeometryParam`

`Shard\Core.Interfaces\Geometry\GeometryParam.cs`，契约层类型，**不依赖 HALCON、不依赖 UI**：

- 单类 + `Kind` 分派（`Point` / `Line` / `Circle` / `Ellipse` / `Contour`），不做继承体系：
  JSON 往返安全、连线类型检查只认一个类型。代价是"线端口连到要圆的输入"编译期不红，
  由本插件在分派里按 `Kind` 校验并明确失败（这是 :170 / :184 / :220 / :234 / :246 / :263 那一批"实际收到 X 与 Y"文案的来源）。
- 属性全 `init`（不可变），只能走工厂；`Line` / `Ellipse` 工厂内部调 `NormalizePhi`（[0,180)）。
- 有公共无参构造（:57）专供 JSON 反序列化，正常代码不要用它。
- 移植新算子（任何"吃基元"的插件）时：**不要自己再定义一份几何结构**，
  也不要为了少一个端口就把基元摊平成 4 个 `double` 输出——那样 Kind 语义与不可变性同时丢失，
  而这两个正是本插件 22 条断言钉住的东西。

## 六、发布到视图窗口

`Shard\Core.Controls\Events\PluginPreviewExtensions.cs`：
`this.PublishPreview(src, viewIndex, "几何计算", info, ann)`（:60-84）→ 组 `ImageDisplayEvent<HImage>`
→ 先 `ImagePreviewCaptureHub.Current?.Capture(e)`（要留帧的宿主在**发布线程**上同步拷走）
→ `GlobalEventBus.PublishOnUIThread(e)`。

三条契约：**窗口号直传，不 +1**（:11-13 记着历史上写过 +1、被一批插件照抄，症状是"选窗口 1 图进第 2 格"）；
**图像只是引用**，发布方下一轮就可能释放它，需要留存必须走捕获钩子；本插件从不新建图，所以两条都天然满足。

## 七、并行安全

`[ParallelSafe]`（`Shard\Core.Interfaces\ParallelSafeAttribute.cs`）判定口径四条：
无无锁 static 可变字段、不碰进程级独占资源、不写全局共享状态、`RunAlgorithm` 对取消令牌协作。
本插件全中：纯托管算术、无静态可变状态、耗时微秒级不需要分段检查令牌。
**匹配口径是类型 FullName 字符串**（不是 `typeof` 等值），为的是绕开"两份 `Core.Interfaces.dll`"
导致程序集身份不一致、明明标了却报 `[并行不安全]`——所以别在自己的命名空间副本上定义这个特性。

## 八、照抄一个"多模式纯计算"插件的清单

| 项 | 本插件的写法 | 抄的时候别漏 |
| --- | --- | --- |
| 类身份 | `[Display(Name, GroupName, Description, ShortName)]` + `[ParallelSafe]` | `GroupName` 直接决定手册页眉与界面分组，写错就是两处口径不一致 |
| 模式选择 | 枚举输入端口 `Op`，一个 switch | 不要用 `[StepConfig]`（无自定义视图时界面上没有入口） |
| 失败 | `Fail("算子名：原因（该怎么改）")` + `return` | 文案带"算子名："前缀，多个步骤同屏时靠它认是谁 |
| 输出清零 | 轮首显式重置每个非 `IDisposable` 输出 | 基类只管 `IDisposable`，标量/引用值一律自管 |
| 单位 | 数值一律"未标定就是像素"，并单独做当量校验 | 禁止"≤0 就当 1 用"的静默回退，连线状态下要失败 |
| 判定 | 独立 `Judge`，默认零容忍 | 明确哪些模式不判定，并在手册里写死"`IsOk` 恒 false" |
| 显示 | 成功轮才推图，输入绿 / 结果红 | 决定要不要在 catch 里留 Warn（本插件没留，是一个已记录的差异） |
| 手册 | `Help\NN-*.md` 扁平，开发页 `NN-dev-*.md` | `.csproj` 要有 `<EmbeddedResource Include="Help\**\*" />`（本工程已有） |
