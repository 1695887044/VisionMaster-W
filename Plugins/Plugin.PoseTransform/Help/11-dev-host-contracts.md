# 坐标变换 · 开发：宿主契约与白拿的行为

> 分类：定位 ｜ 来源：插件 Plugin.PoseTransform ｜ 读者：开发

这一页列**本插件没写一行代码就从基类/契约程序集拿到的东西**，以及"你以为是自己实现的、其实是宿主给的"那部分。
合并到别的软件、或照它写新插件时，这一页决定你要额外补哪些基建。

## 一、基类 `VisionPluginBase` 白给的东西

`Shard\Core.Interfaces\VisionPluginBase.cs`。**插件工程只引用契约程序集，插件之间互不引用**——
本插件用到的 `CalibrationTransform` / `CalibrationKind` / `CalibrationMesh` 全在 `Core.Interfaces` 里，
不引用「标定」插件。

| 白拿的能力 | 基类位置/成员 | 本插件是否用到 | 注意 |
| --- | --- | --- | --- |
| 身份与命名 | `PluginID`（`Guid.NewGuid().ToString("N")`）、`InstanceName`（默认 `"未赋值"`） | 日志前缀全用 `InstanceName` | `InstanceName` 由宿主在装配步骤时赋值，插件不许自己改 |
| 通用输出 `Success` / `ErrorMessage` | 两个 `OutputPort<bool>` / `OutputPort<string>` | 全流程失败语义都走它们 | 声明在基类，**不在本插件的端口表里**，但界面上游可以连 |
| 失败契约一行 | `protected void Fail(string message)` | 每个失败出口都调它 | = `Success=false` + `ErrorMessage=message`，别自己写两行 |
| 端口自动发现 | `EnsurePortsDiscovered()` 反射 `public` 实例属性里所有 `IInputPort` / `IOutputPort` | 12 个输入 + 7 个输出全部靠它 | **端口字段必须是属性且有 getter**；写成字段就发现不到（不报错，界面上凭空少一个口） |
| 端口字典 | `Inputs` / `Outputs`（`IReadOnlyDictionary<string, IPort>`，键 = 端口 `Name`） | 编译期连线校验、断言的端口面核对 | 键是端口自己声明的 `Name`，**不是 C# 属性名**（本插件两者恰好逐字一致） |
| 配置属性发现 | `EnsureConfigPropsDiscovered()` 反射带 `[StepConfig]` 的 `public` 属性，**显式跳过** `IInputPort` / `IOutputPort` 类型 | 4 个配置项全靠它落盘 | 所以 `[StepConfig]` 标在输入端口上是**无效**的（会被过滤掉） |
| 统一灌值 | `ApplyConfigValues(stepData)`：`InputValues` → 先匹配输入端口（**`IsLinked(key)` 的跳过**）→ 再匹配 `[StepConfig]` 属性 | override 都没写，纯吃默认 | 类型转换失败被 `catch` 吞掉、保留属性默认值（"宽容处理"），所以老数据不会炸、只会静默用默认 |
| 配置初始化 | `Initialize(IStepConfigData)` = 存 `StepData` + `ApplyConfigValues` | override 了，`base` 之后补 `EnsurePreviewLoaded()` + `RefreshConfigState()` | **`base.Initialize` 必须在第一句之后立刻调**（本插件写的是 `_disposed = false; base.Initialize(stepData);`） |
| 确认落盘 | `OnConfirm(stepData)`：未链接的输入端口写回 `SetInputValue`，**已链接的反而 `RemoveInputValue`**，再写全部 `[StepConfig]` | 未 override | 链接口不留固定值 = 运行时以连线为准，避免残留脏数据 |
| 取消 | `OnCancel()` 默认空（试运行读已保存的 `InputValues`、不落盘） | 未 override | 想要"取消回滚"必须自己实现，**默认没有** |
| 快照拷贝 | `SnapshotConfigValue`：非 primitive/string/Enum/decimal 的配置值走一次 JSON 往返再存 | 本插件四个配置项都是标量/枚举 → 原样存 | 这条对本插件不重要，但复制这套写法到"带列表配置"的插件时就重要了 |
| 异常兜底 | `Execute` 里 `catch` → 写 `ErrorMessage` + 记日志；`protected virtual bool RethrowOnException => false` | 未 override ⇒ **异常只记不抛**，步骤判失败 | 本插件在 `RunFollowRoi` 里另有自己的 `try/catch`（为了先 Dispose 半成品再 Fail），两层不冲突 |
| 轮首回收输出 | `protected virtual bool AutoDisposeRoundOutputs => true` | 未 override ⇒ `FollowedRegion` / `FollowedImage` 上一轮的值由基类回收 | 这就是"所有权移交端口"的下一半 |
| 动态端口基建 | `AddDynamicInput` / `RemoveDynamicInput` / `AddDynamicOutput` / `RemoveDynamicOutput` / `ClearDynamicOutputs`、`IDynamicOutputProvider.RebuildDynamicOutputs()` | **本插件不用**（端口面固定） | 要按配置增减输出（如 `Crop_ROI_1`）才实现该接口；实现了宿主在**编译前**调 `RebuildDynamicOutputs` |
| 并行安全声明 | `[ParallelSafe]` 特性 | **有**（注释："二期真并行：纯视觉计算"） | 声明的前提是"同一实例被并发跑也不串状态"。本插件成立：`RunAlgorithm` 不写任何共享可变字段，只写端口；配置态刷新被 `PostToUI` 收敛到 UI 线程。**去掉这个特性 = 该步骤退化为串行**，不报错 |

## 二、`IPluginCustomViewProvider`：设计期的唯一入口

```
object GetConfigView(IStepConfigData stepData) => new PoseTransformView(stepData, this);
```

宿主拿到的是个 `object`（实际是 `UserControl`），**插件类自己就是 DataContext**，所以：

- 视图 code-behind 里没有任何业务逻辑，只有 `InitializeComponent` + `DataContext = plugin`；
- 视图构造函数里做三件事：`plugin.Initialize(stepData)`（灌值）、`plugin.OnViewAttached()`（起三联刷新）、
  `Unloaded += (_, _) => plugin.OnViewDetached()`（停刷新）。
  **`Initialize` 在构造里完成，不在 `Loaded`**——这是与「图像采集」「标定」同一模式，别改。
- `OnViewAttached` / `OnViewDetached` **不属于任何接口**，是本插件自己的公开方法，只被自己的视图调。
  它存在的唯一理由是 `RunAlgorithm` 末尾那句 `if (_viewAttached) PostToUI(RefreshOverlay)`：
  对话框关了就不该再为"没人看的图"做一次重采样与轮廓绘制。

## 三、`[StepConfig]` 与 partial 属性钩子

四个配置项都是 **partial property + 源生成器**写法（AGENTS.md R18）：

| 配置属性 | 声明 | 默认值 | 钩子 | 落盘键 |
| --- | --- | --- | --- | --- |
| `Mode` | `[StepConfig, DefaultValue(TransformMode.FollowRoi)]` | `FollowRoi` | `partial void OnModeChanged(TransformMode) => RefreshConfigState()` | `Mode` |
| `EmitFollowedImage` | `[StepConfig]`（无 `DefaultValue`） | `false` | `OnEmitFollowedImageChanged(bool)` | `EmitFollowedImage` |
| `TemplateRefFromRegionCenter` | `[StepConfig, DefaultValue(true)]` | `true` | `OnTemplateRefFromRegionCenterChanged(bool)` | `TemplateRefFromRegionCenter` |
| `PreviewImagePath` | `[StepConfig, DefaultValue("")]` | 空串 | `OnPreviewImagePathChanged(string) => EnsurePreviewLoaded()` | `PreviewImagePath` |

生成器给的三条硬约定（违反了报 **CPG0001~CPG0005** 编译错误）：

1. 配置属性所在类必须声明 `partial`（本类是 `public partial class PoseTransformPlugin`）；
2. **钩子只写实现、不写声明**——`partial void OnXxxChanged(T value) { ... }` 的声明由生成器产出；
3. 属性必须是**自动属性 / partial property**，不许退回"后备字段 + 手写 setter"的老写法。

`PreviewImagePath` 的钩子是**唯一不进 `RefreshConfigState` 的**：它直接进 `EnsurePreviewLoaded()`（要读盘、领轮次号），
因为换图这件事本身就是刷新的起因，绕回三联刷新会少掉轮次号守卫。

**改了这些属性的写法必须重建插件工程**（R16：生成物在插件 DLL 里编译期展开）；
生成器工程本身**不许放进 `Plugins\` 或 `Modules\`**（R17）。

## 四、契约程序集提供的标定语义

本插件消费的东西全部来自 `Core.Interfaces`，**不引用「标定」插件**：

| 契约成员 | 用途 | 出处 |
| --- | --- | --- |
| `CalibrationTransform.Kind` (`CalibrationKind`) | `CheckCalibrationUsable` 的分支键；`PixelScale` 拒绝、未知值拒绝 | `Shard\Core.Interfaces\` |
| `CalibrationTransform.Matrix` | 九点 6 元 `[a11 a12 a21 a22 a31 a32]`，映射 `(Row,Col) → (X,Y)` | 语义由契约注释锁死 |
| `CalibrationTransform.ProjectiveMatrix` | 透视 9 元，投影除法 | 同上 |
| `CalibrationTransform.MeshNodes` / `MeshSize` | 网格节点与规模（`N×N` 节点） | 同上 |
| `CalibrationTransform.SourceImageWidth` / `Height` | 失配③尺寸比对；**两个都是 0 = 标定没记尺寸 → 放行** | 同上 |
| `CalibrationTransform.CameraSerial` | 失配④身份比对（`Trim` 后非空才查，忽略大小写） | 同上 |
| `CalibrationTransform.SourceTag` / `CreatedAtUtc` / `ResidualRmsPx` / `MaxResidualPx` | 只进「标定信息」只读块，不参与任何判定 | 同上 |
| `CalibrationMesh.IsUsable(double[]? nodes, int n, out string? error)` | 网格可用性判定 | `Shard\Core.Interfaces\Calibration\CalibrationMesh.cs:36` |
| `CalibrationMesh` 的三角化 / 重心插值 | 网格点换算（正/反） | **同一份实现**，生产端与消费端共用 |

**为什么网格必须走契约程序集而不是本插件再写一遍**：`PoseTransformMath.cs:70` 的注释写死了——
"生产端与消费端必须是同一份实现，分叉 = 同一份标定算出两套坐标"。
**但点/角度的仿射公式是刻意各写一份的**（`PoseTransformMath.cs:16-21`）：公式语义由 `CalibrationTransform`
的契约注释锁死，两个插件各自实现、各自的断言套件把同一组已知矩阵钉住——这是"契约锁语义"而不是"共享一个 DLL"。
移植时这条别搞反：**网格走共享，仿射走各自实现**。

`CalibrationMesh` 的错误文案尾巴自带（含"重新运行「标定」步骤（网格标定模式）"），所以 `CheckCalibrationUsable`
只在该给 `null` 时补一句 `error ??= "标定数据损坏（网格缺失或退化）：请重新运行「标定」步骤"`，**不追加、避免重复**。
它内部还有一条值得注意：长度校验用 `long` 比，防 `MeshSize` 被损坏成极大值时 `int` 乘法回绕"通过"校验、随后下标溢出。

## 五、工具箱与界面元数据

```
[Display(Name = "坐标变换",
         GroupName = "定位",
         Description = "位姿跟随与像素↔机械换算：把定位位姿变成下游能用的跟随 ROI 与机械坐标",
         ShortName = "\uf047")]
```

- **工具箱实名为「坐标变换」**（不是"位姿变换"，也不是"坐标转换"）；分组 `定位`——
  少了 `GroupName` 就拖不进工具箱（断言 `[PT] 插件带 [Display]（GroupName=定位，否则工具箱里拖不出来）` 钉这条）。
- `ShortName` 是 FontAwesome 私有区字符，界面上当图标用。
- `TransformMode` 两个成员都带 `[Display(Name="位姿跟随")]` / `[Display(Name="像素→机械")]`，
  所以下拉/候选显示中文、**流程与 `.vms` 里存的仍是成员名 `FollowRoi` / `ToMechanical`**（R40-①）。
  自带视图里那两个 `RadioButton` 的 `Content` 是硬编码中文字面量，不走 `StepConfigOption.ForEnum`。
- `StatusLevel`（`Info` / `Warning` / `Error`）**不上屏当数据**，只驱动信息栏颜色（绿/橙/红），按 R40 不算枚举中文欠账。

## 六、落盘键一览（`.vms` 里 `InputValues` 的键）

`InputValues` 是"端口名或配置属性名 → 值"的扁平字典，**端口与配置共用一个命名空间**，所以：

| 键 | 语义 | 什么时候不存在 |
| --- | --- | --- |
| `Mode` / `EmitFollowedImage` / `TemplateRefFromRegionCenter` / `PreviewImagePath` | 四个 `[StepConfig]` 配置项 | 从没改过就可能整个键都不写（读侧走 `DefaultValue`） |
| `SrcImage` … `PixelAngle` 等 12 个输入口名 | **只存"手填的固定值"** | 该口一旦连线（`IsLinked` 为真），`OnConfirm` 会 `RemoveInputValue` 把固定值键删掉 |

两条后果要记住：

1. **连线信息不在 `InputValues` 里**，在步骤的连线表里；`InputValues` 只剩常量。所以"端口名 == 配置属性名"会撞车
   ——本插件刻意让 `PreviewImagePath` 这类配置项与所有端口名互不重复。
2. `PreviewImagePath` 存的是**绝对路径**（`OpenFileDialog` 的 `FileName`）。它不参与运行，但会随方案一起搬机器——
   换机器后路径失效属预期行为，`EnsurePreviewLoaded` 会走 `File.Exists` 那支报"预览图不存在"。

## 七、断言宿主拿到的"无 Application"运行环境

`FlowCanvasChecks\PoseTransformChecks.cs` 是**没有 WPF `Application` 的进程**，直连 `new PoseTransformPlugin()`。
本插件的三处写法专门为它留了路：

- `PostToUI` 取不到 `Application.Current?.Dispatcher` 时**就地执行**，不抛；
- `RunAlgorithm` 自己预置 `Success` / `ErrorMessage`（断言绕过 `Execute` 直接调它，见架构页第八节）；
- `DisplayImage` 构造后必须是 `null`——**构造期绝不碰 HALCON 原生库**
  （断言 `[PT] 构造后 DisplayImage 为 null（构造期不碰 HALCON 原生库）` 钉这条；违反了会让"无 HALCON 环境也能装配插件"变成奢望）。
