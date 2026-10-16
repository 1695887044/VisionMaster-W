# 模板匹配 · 开发：宿主契约与移植清单

> 分类：定位 ｜ 来源：插件 Plugin.Matching ｜ 读者：开发

本页列**这个插件靠宿主/共享件白拿了什么**（不是它自己实现的），以及照它写新插件要带走的清单。
执行流程与状态机在第十页（`10-dev-architecture.md`）。

## 一、继承自 `VisionPluginBase` 白拿的行为（不要重复实现）

| 契约 | 宿主给的行为 | 本插件哪里靠它 / 哪里必须自己补 |
| --- | --- | --- |
| 端口发现 | 反射公开实例属性收集 `IInputPort` / `IOutputPort`；名为空时兜属性名 | 本插件每个端口都显式写了名字（`new("Image", …)`），与属性名一致；改名要同步改断言与方案 |
| 默认成功 | `Execute` 进 `RunAlgorithm` 前预置 `Success=true`、清空 `ErrorMessage` | 正常路径一行 `Success` 都不写；业务失败一律 `Fail("原因")` 一行表达 |
| 异常转失败 | 未捕获异常 → `Fail("{Type}: {Message}")` + Error 日志；`RethrowOnException`（默认 false）决定是否继续上抛 | 本插件自己在 `RunAlgorithm` 外层 `catch` 成 `Fail($"模板匹配失败：{ex.Message}")`，所以走不到基类那条（文案前缀因此是插件给的） |
| 轮首回收输出 | `AutoDisposeRoundOutputs`（默认 true）在 `RunAlgorithm` **之前**释放所有 `IDisposable` 输出并置空；与上游链接当前值同引用的跳过 | `MeasureImage` / `AlignedImage` 交给它；**`double`/`int`/`string`/`HTuple` 它不管**，所以方法开头手写 11 个端口的清零 |
| 统一灌值 | `ApplyConfigValues`：`InputValues` → 输入端口（已链接的跳过）+ `[StepConfig]` 属性（走 `ValueConverter.Convert`，类型不匹配宽容跳过） | 本插件重写它：`base` 之后 `ParseLibraryAndMigrate()` 再选 `EditingEntry`。**编译器只调这个方法、从不调 `Initialize`**——库解析放错地方就是"配置里已学习、运行报尚未创建模板" |
| 默认确认 | `OnConfirm`：未链接端口值写回 `InputValues`，已链接端口 `RemoveInputValue`；`[StepConfig]` 属性逐个 `SnapshotConfigValue` 后写入 | 本插件重写：先 `WriteBackSmearToEntry(EditingEntry)`、再把 `Library` 序列化进 `TemplateLibraryJson`，**最后 `base.OnConfirm`**（顺序反了就存进旧字符串） |
| 配置快照 | 原始值直存，其余 JSON 往返深拷贝 | 所以确认之后继续在界面上改 `MatchingTemplateEntry` 不会污染已确认数据 |
| 默认取消 | `OnCancel` 空实现（试运行读已保存的 `InputValues`，不落盘） | 未重写 |
| 动态端口 | `AddDynamicOutput` / `RemoveDynamicOutput` / `ClearDynamicOutputs` | **本插件不用**：端口面固定 13 个，多实例靠 `HTuple` 数组端口给（改匹配数不会断下游的线） |
| 基类 `Dispose` | 释放所有输出端口承载的 `IDisposable` 值 | `Dispose()` 里先逐条目 `ReleaseEntryModel`、清 `Library`、置空涂抹、`_baker.Dispose()`、释放 `DisplayImage`，**末尾必须 `base.Dispose()`** |

## 二、实现的接口各自意味着什么

| 接口 | 成员 | 谁调、什么时候 |
| --- | --- | --- |
| `IPluginCustomViewProvider` | `object GetConfigView(IStepConfigData stepData)` | 宿主打开配置时调；本插件实现里先 `Initialize(stepData)`，再 `new MatchingView { DataContext = this }` |
| `IPluginConfigView`（基类已实现） | `Initialize` / `OnConfirm` / `OnCancel` | 前两个被重写；`Initialize` 的第一行有效动作是 `MarkAsConfigInstance()`，**必须早于 `base.Initialize`**，否则灌值时按运行实例分支走 |
| 视图 code-behind | `Loaded → plugin.OnViewLoaded()`；七个按钮各自转发一行 | `OnViewLoaded` 先试 `ShowUpstreamImage()`（端口已有图就直接用），没有才 `LoadPreviewImage()` |
| `[ParallelSafe]` | 特性（按类型 FullName 匹配） | 声明"纯视觉计算、不碰界面、不共享跨实例状态"；`EnsureModelLoaded` 是 `static` 但只改**传入条目**的本实例字段，所以声明成立 |

试运行**不在接口里**：主程序 `PluginTestRunner` 统一执行 `RunAlgorithm`，插件不为"试运行"写第二套代码——
这也是"预览所见 = 运行所得"能成立的结构原因。

## 三、`IStepConfigData` 提供的能力

| 成员 | 用途 | 本插件的用法 / 注意 |
| --- | --- | --- |
| `InputValues` | 端口名与配置属性名**共用一个字典**（键名 = 名称，无映射层） | 读写都走基类；写要经 `SetInputValue`（触发变更通知 → 流程版本递增 → 重编译） |
| `IsLinked(name)` / `GetLinkedAddress(name)` | 该键是否已被变量链接、显示地址 | `ApplyConfigValues` 与 `OnConfirm` 都按它跳过链接项——所以 `RecipeName` 接了线就不再落固定值 |
| `GetLink` / `SetLink` / `RemoveLink` | 连线引用对象 | 语义是"值由对方提供"；模板条目名这类字符串键**不要挂连线** |
| `OutputPortDefinitions` | 动态端口存盘快照 | 本插件不写（无动态端口） |

## 四、`[StepConfig]` 的语义边界

`[StepConfig]` = **参与 `InputValues` 持久化与灌值，但不暴露为端口**（不进数据流、不可变量链接）。
标在属性上，属性类型不能是端口类型。本插件一共 13 个：

```
运行态（partial property + 生成器钩子，带 [DefaultValue]）
  MinScore 0.5   MaxMatches 1   MaxOverlap 0.3   Greediness 0.8
  DisplayViewIndex 1   OutputAlignedImage true
库与配方（自动属性）
  TemplateLibraryJson "[]"   DefaultTemplateName ""
老方案迁移字段（自动属性，导入后清空）
  TemplateModel ""   TemplateShape = RoiShapeNames.Rectangle   TemplateRect = []
  TemplateMask ""    PreviewImagePath ""
```

- 六个运行态用的是**partial property**：`public partial double MinScore { get; set; }` +
  `[StepConfig, DefaultValue(DefaultMinScore)]`，**声明由 `Core.PluginGenerators` 生成，只写实现**——
  所以类必须 `partial`，且改生成器后要重建所有插件工程（R16/R18）；
- 提取参数（角度/层数/梯度/极性/缩放）**不是 `[StepConfig]`**：它们住在条目里，随 `TemplateLibraryJson` 一起走。
  这是"每项跟着配方走"的实现前提，别把它们提为步骤级属性；
- `PreviewImagePath` 名字与「坐标变换」的同名配置是**巧合**：两边都是"配置态预览底图路径、不参与运行"，
  各自独立存盘，没有共享实现。

## 五、画布控件 `Core.Halcon.ImageEdit` 的绑定面

`ImageEdit : HalconBase`，本插件用到的依赖属性（全部在 XAML 里绑，无 code-behind）：

| DP | 本插件怎么绑 | 语义 / 注意 |
| --- | --- | --- |
| `HImage` | `{Binding DisplayImage, Mode=OneWay}` | 底图；配置态才有值 |
| `DrawObjectList` | `{Binding CanvasRois}` | 句柄集合：控件右键新建/删除**直接改这个集合**，VM 靠 `CollectionChanged` 回写条目 |
| `ActiveRoi` | `{Binding CanvasActiveRoi, Mode=TwoWay}` | 当前可编辑对象，与列表选中态双向联动 |
| `SmearMode` | `{Binding CanvasSmearMode, Mode=TwoWay}` | 三个选项 `正常显示 / 绘制涂抹 / 擦除涂抹`（`SmearModeType`，界面用索引下拉转） |
| `BrushRadius` | `{Binding BrushSize, Mode=TwoWay}` | 默认 10，合法 1~50 px |
| `SmearDraw` / `SmearErase` | `{Binding SmearMask / SmearEraseRegion, Mode=TwoWay}` | 笔画结束控件**以新实例回写** → VM setter 释放被替换的旧实例并写回条目（VM 是唯一所有者） |

画布手势的三条既有约定（改画布行为前必读，AGENTS.md 第 16 节）：

1. 未选中对象"一次手势就能拖"依赖控件在换绑 `ActiveRoi` 后**补发一次 down**（冷启动回放）——
   换条目后第一次拖不动，多半是这条被改动；
2. 摘除活动对象的屏幕容差是 14px（`RoiHitToleranceScreenPx`），比 HALCON 手柄（约 8px）宽；
   **挑选其它对象的命中测试保持无容差**，否则相邻框互相误选；
3. 断言要验"拖句柄"必须有**可见窗口**：`buffer` 模式下拖不动句柄，所以那类断言只能验参数回写，不能验手势。

另有一条几何口径（R54）：`rectangle2` / `ellipse` 的 `length1` 沿**列轴**，
所以条目 `Rect` 里的 `[角度, 半长, 半宽]` 与画布句柄 `HTuple` 是同一套约定，别在插件里再转一次。

## 六、标注/显示依赖：本插件只攒 `BakePlan`

| 依赖 | 出处 | 用法 |
| --- | --- | --- |
| `AnnotationBaker` | `Services\Core.Halcon\Helpers\AnnotationBaker.cs` | 字段 `_baker`；`Bake(底图, plan)` 返回新图或 null |
| `BakePlan` | 同上 | `Shapes` / `Objects` / `Texts` 三个列表，**绘制顺序固定 Shapes → Objects → Texts** |
| `BakeObj` | 同上 | `new BakeObj(overlay, "green", 1, "margin")`：第 4 参是绘制模式 |
| `AddTextBlock` | 同上 | `plan.AddTextBlock(lines, textColor, 12, 12, 16, box: true)`——`Box` 是**大写 B** 的命名参数 |
| `PublishPreview(HImage, int viewIndex)` | `Shard/Core.Controls/Events/PluginPreviewExtensions.cs` | 第二参就是窗口号，1~9 直传，0 不发布 |
| `ColorNames` 白名单 | `Core.Halcon.Color` | 色名非法时烘焙件回落 `green` 并告警，**绝不把用户字符串当色名传下去** |

六条窗口纪律（R36~R39）**全在烘焙件里**，插件侧一条都不许自己实现：
`'buffer'` 模式（#1305）、按尺寸常驻窗口（#9302）、无条件 `SetPart`、文字坐标按"窗口"、
`DispText` 显式 `box`、`DumpWindowImage` 后源图 `Dispose`。
判据很简单：**本插件源码里 `grep OpenWindow` 为空**。尺寸测量只准用 `GetStringExtents`（没有 `GetTextExtents`），
水平对齐只有 `Left / Center / Right`。

## 七、`.vms` 方案里本插件落下来的键

`InputValues` 里的键（名字 = 端口名/属性名，无映射）：

```
Image                 ← 端口值；已变量链接时这个键会被 OnConfirm 移除
RecipeName            ← 同上（手填时是固定字符串）
TemplateLibraryJson   ← "[{Name,Shape,Rect[],Mask,Model,RefImagePath,NumLevels,MinAngleDeg,MaxAngleDeg,
                          AngleStepDeg,GradientThreshold,UsePolarity,ScaleEnabled,ScaleMin,ScaleMax,
                          LearnedSignature}, …]"
DefaultTemplateName   ← string
MinScore / MaxMatches / MaxOverlap / Greediness / DisplayViewIndex / OutputAlignedImage
TemplateModel / TemplateShape / TemplateRect / TemplateMask / PreviewImagePath  ← 迁移字段（导入后为空）
```

要点：`Rect` **恒为 5 个数**（圆形只填前 3 个，第 4、5 个是占位 0）；`Mask` 与 `Model` 是 base64 字符串；
`LearnedSignature` 是插件自己算的指纹串，手改方案时会立刻触发"需重新学习"。

## 八、打包与工程约定

| 约定 | 判据 |
| --- | --- |
| 插件工程必须在 `Plugins\` 下 | `Directory.Build.targets` 按目录把 DLL 投递到 `Modules\`，放别处加载不到 |
| 不许把 `Core.Interfaces.dll` 等公共契约程序集拷进 `Modules\` | 会出现两份静态状态；`ParallelSafe` 按 FullName 字符串匹配就是为了绕开这个坑 |
| 改了插件源码必须**重建插件工程** | 只重建宿主跑的还是旧 DLL；改生成器相关要全量重建 |
| 手册扁平（单插件程序集）、文件名 `NN-` 前缀、开发页 `NN-dev-xxx.md`、首行 `# 标题` | csproj 已挂 `<EmbeddedResource Include="Help\**\*" />`（本工程在第 26 行） |
| **加完手册必须核 DLL 真的带资源** | 增量构建会静默漏打包（R42），判据见下方命令 |

```powershell
tr '\0' '\n' < Modules\Plugin.Matching.dll | grep -a "\.Help\."
```

## 九、移植到新插件时的取舍

- **想复用"多条目库 + 配方选路"**：把 `TemplateLibraryJson` 存 JSON 字符串、库解析放 `ApplyConfigValues`、
  确认时先序列化再 `base.OnConfirm` 这三件事一起搬；少一条就是"配置里有、运行没有"。
- **想复用"画布编辑 + 涂抹"**：`CanvasRois` / `CanvasActiveRoi` / `SmearMask` / `SmearEraseRegion` 四件套
  加 `_seedingCanvas` 刹车一起搬，并保留"控件回写总是新实例"的所有权约定。
- **不想输出归一化图**：删掉 `OutputAlignedImage` 与 `AlignedImage` 端口即可，其余不动；
  但要注意"区域参数损坏"那条校验挂在这个分支上，删了就没人在运行期抓坏区域。
- **配置态能力（读盘、反序列化、渲染预览）必须用 `_isConfigInstance` 拦**：
  这是编译期耗时与句柄泄漏的第一现场。
