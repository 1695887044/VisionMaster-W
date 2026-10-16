# Blob 缺陷检测 · 开发：宿主契约与白拿能力

> 分类：缺陷检测 ｜ 来源：插件 Plugin.BlobDetect ｜ 读者：开发

这一页只讲**这个插件从宿主"白拿"了什么**：它自己没写、但行为由别人定义的那些边。
改插件之前先弄清哪些边是契约（动它要跨工程重建），哪些是本地实现（动它只影响本文件）。

## 一、本类挂上去的契约一览

| 契约 | 定义处 | 本插件的落点 |
| --- | --- | --- |
| `VisionPluginBase` | `Shard\Core.Interfaces\VisionPluginBase.cs:18`（实现 `IVisionPlugin` + `IPluginConfigView`） | `BlobDetectPlugin.cs:32` 继承它 |
| `IPluginCustomViewProvider` | `Shard\Core.Interfaces\IPluginCustomViewProvider.cs` | 同类 `:32`；`GetConfigView(IStepConfigData)` 在 `:1236` |
| `[Display]` 工具箱元数据 | `System.ComponentModel.DataAnnotations` | `BlobDetectPlugin.cs:25-31`：`Name = "Blob 缺陷检测"`、`GroupName = "缺陷检测"`、`Description`、`ShortName = "\uf002"`（FontAwesome 放大镜码点，就是工具箱里那个图标） |
| `[ParallelSafe]` | 宿主并行调度标记 | `BlobDetectPlugin.cs:32` 紧邻类声明上一行；声明"纯视觉计算、无跨步骤共享状态"，流程引擎才敢把它进真并行分组 |
| `[StepConfig]` + `[DefaultValue]` | `Shard\Core.Interfaces` + 源生成器 `Shard\Core.PluginGenerators\StepConfigGenerator.cs` | 25 个 partial property，`BlobDetectPlugin.cs:181-448` |
| `InputPort<T>` / `OutputPort<T>` | `Shard\Core.Interfaces` | 4 入 16 出，`:37-116`；基类再自带 `Success` / `ErrorMessage` 两个输出 |
| `IExecutionContext` | `Shard\Core.Interfaces` | `RunAlgorithm(IExecutionContext)` `:1277` |
| `PublishPreview` 扩展 | `Shard\Core.Controls\Events\PluginPreviewExtensions.cs:27` | `BlobDetectPlugin.cs:1351` |

工具箱条目名、分组名、页眉里的「分类」都是 `[Display]` 的实值——**手册页眉写错分组名就是没对源码**。

## 二、`VisionPluginBase` 白拿了什么（逐项标注用/不用）

| 基类成员 | 出处 | 本插件的态度 |
| --- | --- | --- |
| `PluginID`（`Guid.NewGuid().ToString("N")`） | `:20` | 白拿，不读 |
| `InstanceName`（默认"未赋值"） | `:22` | 白拿；日志行的前缀就是它，宿主创建实例时填 |
| `Success` / `ErrorMessage` 输出端口 | `:32` / `:40` | 白拿。**NG 只写 `ErrorMessage`、绝不写 `Success`**（判定与执行状态分家是硬契约，见 12 页 A1） |
| `Inputs` / `Outputs` 端口字典 | `:48` / `:53` | 反射收集，插件不维护表 |
| `StepData` | `:63` | 白拿（`Initialize(stepData)` 时基类存） |
| `Execute(IExecutionContext)` | `:94` | **不要 override**。它是模板方法：调 `RunAlgorithm`、计时、异常收口 |
| `RethrowOnException`（默认 false） | `:126` | 沿用默认：异常在插件内落成失败轮，不往上抛给流程引擎 |
| `AutoDisposeRoundOutputs`（默认 true） | `:134` | 沿用默认，但它**只回收 `IDisposable` 的端口值**（`HImage`/`HRegion`/`HTuple` 里 `HTuple` 不算）。所以 `RunAlgorithm` 开头还要手工把 14 个非 IDisposable 端口清零（`:1279` 那条注释就是这个意思） |
| `Fail(string)` | `:140` | 唯一的"判失败"出口：写 `Success=false` + `ErrorMessage` |
| `LastError` | `:183` | 白拿，供界面/日志读 |
| `abstract RunAlgorithm` | `:184` | 本插件唯一的算法入口 `:1277` |
| `virtual Initialize()` | `:190` | 未用（没有需要一次性准备的静态资源） |
| `virtual ApplyConfigValues(IStepConfigData)` | `:234` | **未 override**。它同时灌端口与 `[StepConfig]` 属性，链接端口 `IsLinked` 跳过——本插件不需要动态端口，接不动就是接不动 |
| `virtual Initialize(IStepConfigData)` | `:265` | 未 override；配置态由 `GetConfigView` 自己串（见第六节） |
| `virtual OnConfirm(IStepConfigData)` | `:275` | 未 override，走默认回写（见第四节） |
| `virtual OnCancel()` | `:325` | 未 override（默认空实现；取消即丢弃，靠配置态/运行态两份实例隔离） |
| `AddDynamicInput` / `RemoveDynamicInput` / `AddDynamicOutput` / `RemoveDynamicOutput` / `ClearDynamicOutputs` | `:365-405` | 未用（端口全部编译期固定） |
| `IDynamicOutputProvider` | `:426` | 未实现 |
| `Dispose()` | `:74` | `override` 于 `:1245`：释放预览图、检测缓存、`_renderer.Dispose()` 关离屏窗口 |

一句话概括取舍：**本插件只 override `RunAlgorithm` 与 `Dispose`**，其余全用基类默认。
基类是**所有插件共用的契约程序集**，任何签名改动都算公共契约变更（要走架构评审 + 重建全部插件工程）。

## 三、`[StepConfig]` partial property 与生成器钩子

25 个配置项一律写成 `[StepConfig, DefaultValue(...)]` + `partial` 属性（`BlobDetectPlugin.cs:181-448`），
实现半边由 `Shard\Core.PluginGenerators\StepConfigGenerator.cs` 在编译期生成。插件侧只写钩子实现、不写声明：

```
partial void OnThresholdModeChanged(ThresholdMode value)        // :184  界面显隐 + 排队预览
partial void OnDetectBrightAndDarkChanged(bool value)           // :227  同上
partial void OnMinGrayChanged(double value) => SchedulePreview();       // :242
... 其余 22 个同形（:257 / :263 / :269 / :275 / :281 / :291 / :303 / :312 / :324 / :336 /
    :346 / :352 / :361 / :373 / :383 / :394 / :404 / :413 / :428 / :437）
```

生成器会同时声明 `On{Name}Changing(ref T value)` 与 `On{Name}Changed(T value)` 两个半边
（`StepConfigGenerator.cs:273` / `:275`），本插件**一个 `Changing` 都没实现**——这是有意的：
夹取放在 `NormalizedParameters()` 里而不是 setter 上。

| 设计点 | 口径 | 代价与理由 |
| --- | --- | --- |
| 夹取位置 | 算法侧 `NormalizedParameters()`，不动属性值 | 界面上显示的就是用户填的、存进方案的也是那一个值；算法用夹后的值。若改成 setter 夹，用户会看到"我填 2e9、框里自己变成 1000000000"，且**老方案里的越界值一加载就被永久改写** |
| 钩子用途 | 只做两件事：界面显隐/守卫标记、`SchedulePreview()` | 钩子里跑算法或写别的属性 = 递归触发 + UI 线程重算，禁止 |
| 生成器挂接 | `Plugins\Directory.Build.props` 以 `OutputItemType=Analyzer` + `ReferenceOutputAssembly=false` 挂 | 生成器 DLL 不进 `Modules\`（AGENTS.md R17） |
| 违约表现 | 不满足约定报编译错误 `CPG0001~CPG0005`（`StepConfigGenerator.cs:42, 56-88`） | 故意的"生成器失效必须响"，不静默跳过 |
| 静默那一面 | **漏标 `[StepConfig]` 编译期完全无感**：属性存在、界面能绑、就是不进 `InputValues`，关掉配置窗口一改就丢 | 无断言覆盖，靠人工核对：新增配置项后确认它在 `:181-448` 这个区段里、且带特性 |

改 `Core.PluginGenerators` 后必须重建全部插件工程（AGENTS.md R16），生成物在插件 DLL 里是编译期展开的。

## 四、`IStepConfigData` 与 `.vms` 落盘键

`IStepConfigData`（`Shard\Core.Interfaces\IPluginCustomViewProvider.cs:12`）暴露的关键面：
`StepId`(Guid) / `Icon` / `StepName` / `Description` / `InputValues`（`Dictionary<string, object>`）/ `IsLinked(name)` / `SetInputValue` / `RemoveInputValue`。

**`InputValues` 是一张"端口名与配置属性名共用"的平表**，落盘键的生成规则全在基类 `OnConfirm`（`:275-293`）：

```
未链接的输入端口  → stepData.SetInputValue(port.Name, port.Value)
已链接的输入端口  → stepData.RemoveInputValue(port.Name)   （不留固定值键，运行时以连线为准）
每个 [StepConfig] → stepData.SetInputValue(cp.Name, SnapshotConfigValue(cp.Get()))
```

由此得出四条后果：

1. **键 = C# 名字**（端口构造的第一个字符串实参、配置属性的 CLR 属性名），不是界面中文标签。
   重命名 `MaxSingleArea` 或 `SrcImage` = 老方案里那一条键静默读不回来，值退回默认，**不报错**。
2. **枚举存成员名**。`ThresholdMode` 存 `Fixed`/`Auto`/`Dynamic`，`SortMode` 存 `None`/`AreaDescending`/`RowColumn`；
   `[Display(Name = "固定阈值")]` 只管界面（AGENTS.md R40：改显示名绝不能改到存盘值）。
3. 灌值侧 `ApplyConfigValues`（`:234-260`）按 `kvp.Key` 先查端口、再查 `_configProps`，
   命中不了的键 **直接 `continue` 丢弃**；类型转换失败被 `catch` 吞掉、属性保留默认值。
   所以"方案里的配置没生效"在日志里是查不到的——这是 B 组静默项。
4. 复杂类型经 `SnapshotConfigValue`（`:303`）做 JSON 往返拷贝，确认后界面继续改不会污染已确认数据。
   本插件 25 个配置项全是基元/字符串/枚举，**这条路一次都没走到**；
   真要加 `List<...>` 型配置项，就得考虑拷贝语义与老数据兼容。

`MmPerPixel` 端口的 `InputValues` 兜底值是构造里的 `0`（`:61`）：未链接时 `0` 进不了算法，
`GetEffectivePixelSizeMm()` 判"未接线"改用手填 `PixelSizeMm`；**已链接**时 `0` 就是无效值 → 明确失败。
这条"同一个 0 两种含义"的分叉是刻意的，别把它简化成"0 就用配置项"。

## 五、预览发布面（`PublishPreview`）

`BlobDetectPlugin.cs:1351`：`DisplayViewIndex > 0` 且标注图非 null 才 `this.PublishPreview(result.AnnotatedImage, DisplayViewIndex)`。

| 事实 | 出处 | 注意 |
| --- | --- | --- |
| 扩展方法三个重载（图 + 窗口号 / 带 `MeasureAnnotation` / 带 `ImageInfoRow`） | `PluginPreviewExtensions.cs:27/37/48` | 本插件用**第一个**重载：所有标注已经烧进像素，不再走覆盖层（`ResultDisplayCheck` 的 A1 就是罚"既烧像素又走覆盖层 = 画两遍"） |
| `ViewIndex` 语义 | 配置值原样传，不加不减 | 历史上写过 `+1`，后果是"选窗口 1、图进第 2 格"（`ResultDisplayCheck` A1 断言名里记着这条） |
| 事件里的发布方 | `ImageDisplayEvent.cs:46`：`Publisher` 由 `PublishPreview` 自动填 `InstanceName` | 画布列表靠它标注"这张图谁注入的"，插件自己不用管 |
| 图的生命周期 | `ImagePreviewCapture.cs:10-32`：下一轮开始时 `AutoDisposeRoundOutputs` 释放上一轮输出 | 宿主必须在 `PublishPreview` **返回之前**同步拷走像素；插件不许发布之后再复用/释放那张图 |

`DisplayViewIndex = 0` 时**一个事件都不发**，但 `DefectImage` 端口照样赋值——操作员不看、下游照样能存证据图。
NG 也发布：判 NG 时操作员最需要立刻看到缺陷在哪，所以这里没有"只在 OK 时发"的条件。

## 六、配置视图与样式绑定面

`GetConfigView(IStepConfigData stepData)`（`:1236`）三步：**先 `MarkAsConfigInstance()`（`:1240`）再 `Initialize(stepData)`**——
顺序反过来，灌值触发的 25 个 setter 就漏掉预览排队。视图 `Loaded` 里还有第二个盖章入口 `OnViewLoaded()`（`:708`）。
这就是"配置态/运行态隔离"的全部接线，详见 10 页第四节。

`BlobDetectView.xaml` 是纯绑定层（DataContext = 插件实例本身，一个类两顶帽子）：

| 绑定面 | 出处 | 契约 |
| --- | --- | --- |
| 合并 `pack://application:,,,/UI;component/Themes/Generic.xaml` | `:42` | 插件样式来自 UI 库；`Generic.xaml` 已合并 `Plugin*` 两本字典，宿主与插件都靠它拿样式（`BlobDetectChecks` 有同名断言） |
| 令牌与样式在 `UI/Controls/Themes/PluginConfigStyles.xaml` | 视图 `:23` 注释 | 视图里不许硬编码颜色（AGENTS.md R30/R31 同族：分册之间横向看不见） |
| `<local:EnumDisplayNameConverter x:Key="EnumName" />` | `:46`，用于 `:139` `:316` `:701` 三处 `ComboBox` 项模板 | 中文名只有一份真相（枚举上的 `[Display(Name)]`），与 `StepConfigOption.ForEnum` 读同一个特性 |
| `h:HistogramPlot` | `:900` | 原本是**本插件的** `Plugin.BlobDetect.HistogramPlot`，2026-10-08 上提到 `Core.Halcon.Controls` 供各插件共用（`:898` 注释）；`Data` / `HistogramVisible` / `HistogramError` / `HistogramRangeText` / `HistogramTypeText` 五个绑定 |
| 缺陷清单用 `DataGrid` | `:856-879`，八列 `Index/Area/Circularity/AspectRatio/Width/Height/Row/Col` | **不是 `ListView + GridView`**，所以 AGENTS.md 第 6 节 R19-R22（`GridListViewStyle`、`GridViewRowPresenter`、就地合并 `GridView.xaml`）对它不适用。改成 ListView 就必须补齐那三条，否则列头消失、数据行退化成类型全名 |
| code-behind | `BlobDetectView.xaml.cs`（34 行） | 只有两件事：`Loaded` 通知 ViewModel 取图；清单行选中转 `HighlightDefect`。视图里不许有算法 |

## 七、工程与打包契约

| 项 | 值 / 出处 | 后果 |
| --- | --- | --- |
| 工程必须位于 `Plugins\` 下 | 红线①：`Plugins\Directory.Build.targets` 按目录把 DLL 投递到 `Modules\` | 放别处宿主加载不到 |
| 目标框架 | `net9.0-windows` + `UseWPF=true`（`Plugin.BlobDetect.csproj`） | — |
| 工程引用 | `Services\Core.Halcon`、`Shard\Core.Controls`、`Shard\Core.Interfaces`、`UI\Controls\UI` | 这四个是契约/公共件 |
| HALCON | `<Reference Include="halcondotnet">` + `HintPath = ..\..\DLL\Halcon\halcondotnet.dll` | 不进 `Modules\`；`Private=false` 语义由公共约定保证（红线②：不许把 `Core.Interfaces.dll`/`Core.Controls.dll` 拷进 `Modules\`，否则两份静态状态） |
| 手册 | `<EmbeddedResource Include="Help\**\*" />` | **没有全局 props 规则**，每个工程自己挂；少挂一条宿主就当没有手册、静默退回自动生成页（AGENTS.md R41） |
| 资源名 | `Plugin.BlobDetect.Help.{NN}-{name}.md`，页序 = 资源名 Ordinal 序 | `NN-` 前缀重复时宿主不报错，只是显示顺序由 Ordinal 决定（本轮踩到的就是 `01-` 撞 `01-`） |
| 重建口径 | 改源码/加手册后必须重建**插件工程**，收尾那一次用 `--no-incremental` | 红线③ + AGENTS.md R42：增量构建会静默漏打包内嵌资源；判据是数 DLL 里的 `.Help.` 资源名，不能只看构建退出码 |

## 八、动哪个契约要连带做什么

| 改动 | 连带范围 |
| --- | --- |
| 端口名 / 配置属性名改名 | 老 `.vms` 静默丢值（无断言）；下游连线断、取数脚本失效；必须做迁移或保留旧别名读取 |
| 端口增删（尤其 1-7 号老端口） | 顺序契约写在 03 页；`BlobDetectChecks` 的【核心】逐项对齐断言要跟着改夹具 |
| `[StepConfig]` 属性加一个 | 必须同时：`[DefaultValue]` + 出厂默认常量 + `AreAllParametersAtFactoryDefault()` 的清单 + 界面卡 + 03 页表 + 25→26 的文案 |
| 实现 `On…Changing` 做夹取 | 破坏"界面值 = 存盘值"口径（第三节），先和 owner 确认再动 |
| 动 `Shard\Core.Interfaces` 或 `Core.PluginGenerators` | 公共契约变更：架构评审 + 重建全部插件工程 + CodeGraph 查爆炸半径（AGENTS.md R16/R29） |
| 视图里加样式/颜色 | 令牌必须进 UI 库那本分册；跨分册 `StaticResource` 会抛或静默失效（AGENTS.md R30/R31，守门人 `VariableBindingCheck.RunDictionaryScopeContract`） |
