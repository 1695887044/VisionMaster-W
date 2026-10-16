# 颜色序列检查 · 开发：宿主契约

> 分类：图像处理 ｜ 来源：插件 Plugin.ColorCheck ｜ 读者：开发

这个插件写的代码很少，大部分行为是**基类与宿主白送的**。本页只讲"这些口由谁填、生命周期归谁、落盘长什么样"，
端口与参数的语义清单在《颜色序列检查 · 端口与参数》，不在此重复。

## 继承与实现

```csharp
[Display(Name = "颜色序列检查", GroupName = "图像处理",
         Description = "在采样区里取一维颜色序列（线序 / 色带 / 排线通用），后续与配方比对",
         ShortName = "\uf53f")]
[ParallelSafe]
public class ColorCheckPlugin : VisionPluginBase, IPluginCustomViewProvider
```

工具箱分组名的**实值是「图像处理」**（页眉"分类"就取它）；工具箱里显示的名字是 **颜色序列检查**，
不是"颜色检查"也不是"线序检查"。`ShortName` 是 FontAwesome 私用区字符，图标不是文字。

| 成员 | 来自 | 本插件拿到什么 |
| --- | --- | --- |
| `VisionPluginBase` | `Shard/Core.Interfaces` | 端口发现（`Inputs` / `Outputs` 反射收集 `InputPort`/`OutputPort` 数据成员）、`Success` / `ErrorMessage` 两个口、`Execute` 生命周期、`Dispose` 回收输出端口、`ApplyConfigValues` / `Initialize` / `OnConfirm` 三件套、`StepData` |
| `ObservableObject` | 同上 | `SetProperty` / `OnPropertyChanged`，界面绑定用 |
| `IPluginConfigView` | 基类已实现 | `PluginConfig` 属性、配置视图契约 |
| `IPluginCustomViewProvider` | `Shard/Core.Interfaces` | **`object GetConfigView(IStepConfigData stepData)`** —— 宿主双击步骤时调它拿视图，本插件在里面先 `Initialize(stepData)` 再 `new ColorCheckView { DataContext = this }` |
| `ParallelSafeAttribute` | 同上 | 允许编译进并行批次 |

## 白拿的宿主行为（逐条对应到源码）

| 行为 | 契约 | 本插件的用法 |
| --- | --- | --- |
| 执行预置 | `Execute` 进 `RunAlgorithm` **之前**把 `Success=true`、清空 `ErrorMessage` | 所以"默认成功、显式失败"：不 `Fail` 就是成功 |
| 失败一行表达 | `protected void Fail(string message)` 置 `Success=false` + 写 `ErrorMessage` | 两处早退 + `Fail(error)` 转发分析器的原话 |
| 异常策略 | `protected virtual bool RethrowOnException => false`：异常转失败、**流程继续** | 没重写。投射那段自己 `catch` 掉了，不会走到这里 |
| 轮首回收输出 | `protected virtual bool AutoDisposeRoundOutputs => true`：`RunAlgorithm` 之前释放上一轮输出端口的非托管值，与上游链接当前值同引用的自动跳过 | 没重写。八个口都是值类型/字符串，等于空转；**但它管不到 `DisplayImage`**，这就是本插件必须重写 `Dispose` 的理由 |
| 统一灌值 | `ApplyConfigValues(stepData)`：遍历 `InputValues`，先按**端口名**找 `Inputs`（`IsLinked` 的键跳过），再按**配置属性名**找 `_configProps`，值走 `ValueConverter.Convert(...)`；类型不匹配的旧数据**宽容跳过** | `RoiParams`（`double[]`）与 `RecipeText`（多行 `string`）都从这条路进来 |
| 配置初始化 | `Initialize(IStepConfigData)`：存 `StepData` + `ApplyConfigValues` | 本插件 `override` 后**先调 base**，再播种画布 |
| 确认落盘 | `OnConfirm`：未链接的端口 `SetInputValue(端口名, 值)`；每个 `[StepConfig]` 属性 `SetInputValue(属性名, SnapshotConfigValue(值))`；已链接的端口**移除固定值键** | 界面上点确认才写；取消 = 活对象改掉但不落盘 |
| 快照脱钩 | `SnapshotConfigValue` 用 JSON 往返拷贝，确认后界面继续改不污染已确认数据 | `RoiParams` 是数组，走的就是这条 |
| 端口即数据 | 设计原则"端口即数据成员"，配置界面绑端口的 `TypedValue`，**不需要草稿属性镜像** | `Image` / `ExpectedSequence` 直接是数据成员 |
| 日志 | `IExecutionContext context.Logger`（`Warn` / `Info`） | 告警逐条 `Warn`，四条 `Info` |
| 推图 | `Shard/Core.Controls` 的扩展方法 `PublishPreview(this IVisionPlugin, HImage, int viewIndex, IEnumerable<MeasureAnnotation>)` | `this.PublishPreview(image, DisplayViewIndex, marks)`。**窗口号原样直传**，消费端按 `ViewIndex == 窗口号` 等值筛选 |
| 图像来源绑定 | `cc:LinkableValueEditor`（`Label` / `Port` / `StepData` 三个属性） | 「图像输入」卡片里就一个它，绑 `Image` |
| 阈值编辑 | `cc:ColorThresholdEditor`（`Shard/Core.Controls`） | 13 个 `TextBox` 直接 `{Binding 属性名}` 绑到插件实例，**共用控件、不在本插件里** |
| 画布 | `h:ImageEdit`（`Core.Halcon`）：`HImage`（OneWay）/ `DrawObjectList` / `ActiveRoi`（TwoWay） | 冷启动一次手势即可拖动手柄、14 像素摘除容差等行为全在这里，见 AGENTS.md R51~R55 |

## `[StepConfig]` 的写法（本插件选了「三选一」的第一种）

`StepConfigAttribute` 是**空标记特性**（没有任何属性），含义只有一个：**这个公共属性参与存盘与灌值**。

本插件 25 个配置项**全部是自动属性**：

```csharp
[StepConfig]
public double DarkSpanRatio { get; set; } = 35;
```

- **没有用 partial property + 钩子**（AGENTS.md R18 的另外两种写法），所以本文件不受源生成器改动的影响路径约束；
  但只要 `Core.PluginGenerators` 被改，插件工程仍要**全部重建**（R16），否则跑的是旧 DLL。
- 副作用逻辑**不写在 setter 里**，而是显式调 `OnPropertyChanged(nameof(RoiSummary))`——
  `RoiSummary` 是只读派生属性，`RoiParams` 一变就手动推通知。
- 界面上要即时响应输入，配方框显式写了 `UpdateSourceTrigger=PropertyChanged`（其余数字框走默认）。

**不要加的东西**：`Hint` / `ResultSummary` / `RoiSummary` / `CanvasRois` / `CanvasActiveRoi` / `DisplayImage`
都**故意不标** `[StepConfig]`——它们是视图态，标了就会往 `InputValues` 里塞 JSON 化不了的 `ObservableCollection` / `HImage`。

## `.vms` 落盘键（步骤的 `InputValues` 字典）

| 键 | 类型 | 何时有 |
| --- | --- | --- |
| `RoiParams` | `double[]`（5 个数，角度是**弧度**） | 画过框并确认 |
| `ReverseSequence` / `Regularize` / `ExtendEnds` | `bool` | 确认后 |
| `WireCount` / `MinRun` / `DisplayViewIndex` / `BrownWarmth` | `int` | 确认后 |
| `DarkSpanRatio` / `BrightPercentile` / `MaxRunRatio` / `RuleWindowRatio` + 12 个色相/明度阈值 | `double` | 确认后 |
| `RecipeText` | `string`（内含换行，随 JSON 转义） | 确认后 |
| `Image` | 上游图值 | **只有没接线时**才写；接了线走 `RemoveInputValue`，避免残留脏数据 |
| `ExpectedSequence` | `string` | 同上：没接线时是固定值，接线后移除该键 |

三条要记的口径：

1. **键名就是属性名/端口名**，没有前缀、没有版本号；`Inputs` 先查、`_configProps` 后查，同名会**先命中端口**。
2. **链接与固定值互斥**：`IsLinked(名字)` 为真时 `OnConfirm` 删掉那个键，运行期以链接为准。
   所以"端口里手打了值 + 又接了线"不会共存，界面上得先断开。
3. **宽容读**：`ApplyConfigValues` 里类型转换失败被 `catch` 掉、保留属性默认值——旧方案的坏数据不会炸，但会**静默变回默认**。

输出端口（`Result` / `BadIndex` / `Verdict` / `Expected` / `Count` / `Sequence` / `Detail` / `Rows`）
以及基类的 `Success` / `ErrorMessage` **都不落盘**，每轮重算。

## 生命周期钩子的实际顺序

```
宿主双击步骤
  → GetConfigView(stepData)
      → Initialize(stepData)            // base: 存 StepData + ApplyConfigValues
          → _seedingCanvas = true
          → 全量退订旧 DrawingObjectInfo 的 PropertyChanged
          → CanvasRois.Clear()
          → RoiParams 是 5 个数就 Add 一个 "采样区" 矩形
          → _seedingCanvas = false      // finally
          → OnPropertyChanged(RoiSummary)
      → new ColorCheckView { DataContext = this }
          → Loaded → OnViewLoaded() → ShowUpstreamImage()   // 把端口里已有的图拷上屏
用户在窗口里改（只改活对象）
点确认 → OnConfirm(stepData)（基类那一份：端口 + 25 个配置属性写进 InputValues）
关窗口 → Dispose()
      → Image.PropertyChanged -= OnImagePortChanged
      → _seedingCanvas = true → CanvasRois.Clear() → finally false    // 压刹车，避免回写抹掉 RoiParams
      → DisplayImage?.Dispose(); DisplayImage = null
      → base.Dispose()                                                 // 输出端口交给基类
```

试运行路径另外走一条：`PluginTestRunner.BridgeInputs` 会**把上游实际值写进配置实例的端口**，
于是 `Image.PropertyChanged` 触发 → 图自动上屏。**这条路径不是"用户编辑"**，
所以任何"从值变了倒推用户意图"的逻辑都会在这里误触（AGENTS.md R35）。本插件在这条链路上只做一件事：拷图显示，不写配置。
