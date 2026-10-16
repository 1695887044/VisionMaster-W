# 卡尺测量 · 架构与数据流

> 分类：测量 ｜ 来源：插件 Plugin.CaliperMeasure ｜ 读者：开发

源码位置：`Plugins\Plugin.CaliperMeasure\CaliperMeasurePlugin.cs`（约 1930 行，节点与界面 ViewModel 合一）、
`AnnotationRenderer.cs`（只攒烘焙计划）、`CaliperEnums.cs`（五个枚举与 `[Display]` 中文名）、
`Models\CaliperRegion.cs`（搜索区持久模型 + 参数微调行）、`CaliperMeasureView.xaml`（纯绑定层，代码后置只有"就绪通知"一句）。

## 一、一个类兼任两个身份

```csharp
[Display(Name = "卡尺测量", GroupName = "测量",
         Description = "阵列式卡尺测量：自动均布 N 把卡尺做亚像素找边，支持宽度/间隙、两点距、点到线距、圆直径与角度；输出物理值、偏差与 OK/NG",
         ShortName = "\uf545")]
[ParallelSafe] // 二期真并行：纯视觉计算
public partial class CaliperMeasurePlugin : VisionPluginBase, IPluginCustomViewProvider
```

- 作为流程节点：只有 `RunAlgorithm` 一件事——调 `ExecuteMeasureCore`，把结果搬到 6 个输出端口；
- 作为配置界面 ViewModel：承载 16 个 `[StepConfig]` 字段、画布集合、预览图、信息栏；
- 宿主会为"配置"和"编译执行"**各造一个实例**，两个实例共用同一份算法核心（这是"试运行所见 = 运行所得"的物理基础）；
  区分两者的唯一开关是 `_isConfigInstance`。
- 类必须是 `partial`：16 个 `[StepConfig]` 属性里有 15 个写成 `public partial X { get; set; }` + `partial void OnXxxChanged(...)`，
  属性实现由 `Core.PluginGenerators` 生成（R18）。本插件 15 个 changed 回调全部只做一件事：`SchedulePreview()`；
  唯一例外是 `OnMeasureKindChanged`，它还要重发 5 个派生属性通知并 `EnsureRequiredRegions()`。
  **改生成器行为必须整解决方案重建**（`dotnet build VisionMaster.sln`），只 `build` 宿主看不到。

## 二、状态清单（三份，别混）

| 类别 | 字段 | 谁拥有 | 生命周期 |
| --- | --- | --- | --- |
| 持久配置态 | `MeasureKind`、`CaliperRegions`、`CaliperCount`、`Sigma`、`EdgeThreshold`、`Polarity`、`Select`、`Interpolation`、`FitAlgorithm`、`FitMaxError`、`FitMinPoints`、`AnnulusWidth`、`StandardValue`、`UpperTolerance`、`LowerTolerance`、`PixelSizeMm` | `[StepConfig]`，宿主写回 `InputValues` | 随方案存盘；`OnConfirm` 时 `SnapshotConfigValue` 做 JSON 往返拷贝 |
| 画布同步态 | `CanvasRegions`（`ObservableCollection<DrawingObjectInfo>`，VM 是唯一所有者）、`CanvasActiveRegion`、`SelectedRegion`、`_seedingCanvas` | 插件自己 | 不直接落盘；落盘的是 `CaliperRegions`（形状 + 参数数组 + 名字） |
| 配置态专属 | `_isConfigInstance`、`_previewDebounce`（`DispatcherTimer` 200ms）、`_previewImage`、`_statusMessage`/`_statusLevel`、`_renderer` | 插件自己 | 运行实例上这些一律不工作 |

派生给界面消费的只读属性：`MeasureKinds`/`Polarities`/`EdgeSelects`/`Interpolations`/`FitAlgorithms`（下拉数据源，
`Enum.GetValues` 一次性物化成数组，避免每次绑定重复反射）、`IsWidthMeasure`/`IsPointToPointMeasure`/`IsFitMeasure`/`IsCircleMeasure`
（分区显隐）、`RequiredRegionCount`、`RegionCountHint`（灰字）。
`RequiredRegionCountOf` 是**唯一真相**：`Width` 与 `CircleDiameter` 要 1 个，其余要 2 个。

## 三、双向同步回路与刹车

```
画布控件（右键新建/删除/拖句柄）
      │ 改 CanvasRegions / info.HTuples
      ▼
OnCanvasRegionsChanged ──Add──▶ CaliperRegions.Add(new CaliperRegion{ Name, ShapeType, Params = HTuples.Select(D) })
                       ──Remove▶ 按 RoiName 找并移除；若正是 SelectedRegion 则置 null
                       ──Reset ─▶ CaliperRegions.Clear()
OnCanvasRegionTuplesChanged（HTuples 的 INPC）──▶ region.Params = info.HTuples.Select(D) → SchedulePreview()
参数微调行（CaliperParamEntry.Value，失焦写回）──▶ CaliperRegion.Params[i] = v → ParamEdited
OnSelectedRegionParamEdited ──▶ info.HTuples = region.Params.Select(new HTuple) → 控件应用句柄 → SchedulePreview()
```

四个必须记住的刹车与口径：

1. **`_seedingCanvas`**：`Initialize` 里灌画布集合时置 true，`OnCanvasRegionsChanged` 第一行就 `return`，
   否则"从方案恢复"会被当成"用户新建"再回写一遍，破坏幂等；`EnsureRequiredRegions` 也带同一个刹车。
2. **按名字对齐**（`RoiName` ↔ `CaliperRegion.Name`）：不是按索引，所以列表重排不会串数据；
   但**改名等于断链**——回写找不到条目，拖拽结果静默丢失。名字由 `NextRegionName()` 生成并跳号保证不重名。
3. **`CaliperRegions` 的 setter 会把 `SelectedRegion` 置 null**：反序列化是整体替换集合，旧选中项已不在新集合里，
   留着会订阅到孤儿对象上。
4. **`SelectedRegion` 的 getter/setter 会增删 `ParamEdited` 订阅**：换选中项时先退订旧项，避免一次编辑触发多次预览。

`OnRegionsChanged()` 只做两件事：重发 `RegionCountHint` 通知 + `SchedulePreview()`。
区数是否够用**不在界面上阻断运行**，而是由 `NormalizedParameters()` 在执行期给中文报错。

## 四、配置会话时间线

```
宿主为配置建新实例
  └─ GetConfigView(stepData)：Initialize(stepData) → new CaliperMeasureView{ DataContext = this }
        Initialize = base.Initialize(灌值) → _seedingCanvas=true → CanvasRegions.Clear/Add(按 CaliperRegions) → false
  └─ 视图 Loaded → OnViewLoaded()
        MarkAsConfigInstance()   // 此刻才订阅 SrcImage.ValueChanged
        EnsureRequiredRegions()  // 缺区才播种（只增不减）
        RefreshPreview()         // 首帧就出标注图
  └─ 用户改任一参数 → OnXxxChanged → SchedulePreview（200ms 防抖，非 UI 线程先 BeginInvoke 回投递）
  └─ 上游换图 → OnSourceImageChanged → 同上
用户点「确定」 → 基类 OnConfirm：未链接端口值 + SnapshotConfigValue 拷出的 16 个配置写回 InputValues
实例 Dispose   → 停表 → PreviewImage=null（setter 释放）→ 逐个 info.Dispose() → _renderer.Dispose() → base.Dispose()
```

`Dispose` 里"先清字段、不走 SetProperty"是刻意的：析构期发 INPC 通知会让已解绑的视图重新读到半死对象。
`CanvasRegions` 的逐个 `Dispose()` 是兜底路径（正常由控件 Unloaded 处置），`DrawingObjectInfo.Dispose` 幂等。

## 五、`RunAlgorithm` 逐步

```csharp
MeasureValue.Value = 0; Deviation.Value = 0; IsOk.Value = false;   // ① 轮首归零
var src = SrcImage.ActualValue;
if (src == null || !src.IsInitialized()) { Fail("输入图像为空或未初始化"); return; }   // ②
try { result = ExecuteMeasureCore(src, context.Logger); }
catch (Exception ex) { Fail($"卡尺测量失败：{ex.Message}"); context.Logger?.Error($"{InstanceName} {ErrorMessage.Value}"); return; }  // ③
if (result.Failed) { result.DisposeOutputs(); Fail(result.FailMessage); ...Error(...); return; }   // ④
if (result.MeasureImage  != null) MeasureImage.TypedValue  = result.MeasureImage;    // ⑤⑥⑦ 所有权移交
if (result.FittedGeometry != null) FittedGeometry.TypedValue = result.FittedGeometry;
if (result.EdgePoints     != null) EdgePoints.TypedValue     = result.EdgePoints;
MeasureValue.Value = result.MeasureValueMm; Deviation.Value = result.Deviation; IsOk.Value = result.IsOk;  // ⑧⑨⑩
if (!result.IsOk) ErrorMessage.Value = result.NgReason;   // ⑪ NG 不算失败，但原因必须留痕
context.Logger?.Info($"{InstanceName} {result.Summary}");  // ⑫
```

三个设计点：

- **① 是必须的**：基类的 `AutoDisposeRoundOutputs` 只回收 `IDisposable` 端口（HImage/HXLD），
  double/bool 会把上一轮值原样留下 → 失败轮下游读到脏数（极易漏的镜像 bug）。
- **③ 的前缀**：`Fail` 前自己拼"卡尺测量失败："，比基类兜底的"异常类型: 消息"更好排查；同一句写进日志。
- **⑤ 的"非空才赋值"**：渲染失败时 `result.MeasureImage` 是 null，端口保持轮首的空状态，
  而不是把一个已释放的句柄塞给下游。

## 六、`ExecuteMeasureCore`（预览与运行共用）逐步

```
A NormalizedParameters()  → 夹取全部参数 + 校验搜索区；任何不合法 → result.Failed / FailMessage，立即返回
B TryToGrayImage          → count_channels：1→copy_image，3→rgb1_to_gray，其它→中文报错（不猜通道）
C GetImageSize            → imgW/imgH（后面所有越界判定的基准）
D 逐矩形中心越界预检      → 中心在图外时算子会抛英文码（滤波核非法），这里提前用中文指名是哪个区
E switch (p.Kind)         → 五个分支，见下节；每分支产出 measureXld（青）与可选 fittedXld（品红），失败即返回
F 物理值 / 偏差 / 判定    → 角度不乘当量；Deviation = MeasureValue − StandardValue；IsOk 带 1e-9 容差
G 组装叠加层              → gen_rectangle2_contour_xld（批量，ToTuple 抽第 0~4 列）+ gen_cross_contour_xld(12, 0.785398)
                          → result.EdgePoints = new HXLDCont(crossXld)
                          → result.FittedGeometry = new HXLDCont(fittedXld ?? measureXld)
H 渲染标注图              → try { _renderer.Render(src, caliperXld, measureXld, fittedXld, crossXld, 三行文字, OK?green:red) }
                          → catch → logger.Warn("…标注图渲染失败，MeasureImage 将为空：…") + 置 null（**不影响成功判定**）
I BuildSummary            → 一行摘要（信息栏/日志同源）
finally                   → 每个测量句柄 close_measure；temp 里每个 HObject Dispose；两处都 try/catch 吞掉不阻断
```

`MeasureParams.ValidationError` 是**唯一的配置错误通道**：A 阶段任何一条不合法都走它，
所以 E/F/G/H 之后不会再有"配置类失败"，分支里只剩算法类失败。

## 七、五个分支的取数与几何

| Kind | 采集 | 取数 | 青层（measureXld） | 品红层（fittedXld） |
| --- | --- | --- | --- | --- |
| Width | 1 矩形 → `DistributeCalipers` → 每把 `MeasurePairsWithRetry` | `intra[0]` 逐把收集后 `.Average()` | 每对边两点连段 | null（端口沿用 measureXld） |
| PointToPoint | 2 矩形各自 `CollectRegionEdgePoints` | 每区点集行/列各取平均 → `DistancePp` | 两代表点连线 | null（同上） |
| PointToLineDistance | 区 1 出代表点、区 2 `TryFitLine` | `DistancePl`（**点到无限直线**垂距） | 点→垂足（t 由直线参数式解出） | 拟合线段 |
| CircleDiameter | `CollectCircleEdgePoints`（圆周径向）→ `TryFitCircle` | 2 × 拟合半径 | 过圆心的水平直径线段 | `GenCircleContourXld(…, "positive", clamp(r/100, 0.05, 5))` 整圆 |
| Angle | 两区各 `TryFitLine` → 解交点 → 射线归一化 → `AngleLl` | `abs(angle) × 180/π` | 以交点为圆心的夹角弧（半径 = clamp(min(两端距)/3, 5, 40)） | 两条拟合线 `ConcatObj` |

`default:` 分支不是死代码：它给 `measureXld` 赋一个 `GenEmptyObj`，好让编译器对"确定赋值"满意（枚举 5 个成员已全覆盖）。

**判平行**用的是叉积模与模长积的比值：`|cross| < 0.0017 × norm`（约 0.1°），另加 `norm < 1e-12` 兜零向量。
**圆退化**的三条几何判据（半径必须有限/为正/不超过图像对角线 10 倍、点集跨度 < 2px、直径 < 跨度 60%）
是为了挡"鲁棒算法不报错但收敛到小圆"这条路——异常拦截拦不住它，中位数残差也可能因降权而达标。

## 八、内存所有权

| 对象 | 谁创建 | 谁释放 | 踩坑表现 |
| --- | --- | --- | --- |
| `src`（`SrcImage.ActualValue`） | 上游 | **本插件绝不释放**（只读借用） | 释放了下一轮上游同款引用直接崩 |
| `gray`、所有 XLD 中间量、`poly`、`arcXld`、`caliperXld`、`crossXld` | 本方法 | `temp` 列表，finally 统一 Dispose | 漏登记 = 每轮泄漏一批非托管对象 |
| measure 句柄（`GenMeasureRectangle2` 的 HTuple） | 每把卡尺每次重试各一个 | `measureHandles` 列表，finally `CloseMeasure` | 漏关 = 每轮泄漏 N×重试次数 个测量对象；**算子名是 `close_measure`，不存在 `clear_measure`** |
| `result.EdgePoints` / `FittedGeometry` | `new HXLDCont(源对象)` 独立句柄 | 运行路径交基类回收；预览路径 `DisposeGeometry()` | 直接包装源对象会被 finally 释放掉——这里已实测"包装后释放源对象不影响包装结果" |
| `result.MeasureImage` | 烘焙件产出的新 HImage | 运行：端口 → 基类轮首回收；预览：`PreviewImage` setter 先切绑定后释放旧图 | setter 里顺序反了会让 UI 读到已释放句柄 |
| 失败路径的 `result` 对象 | — | `DisposeOutputs()`（运行④与预览失败都调） | 忘了调 = 失败轮的非托管残留 |
| `_renderer` 内的离屏窗口 | 共享烘焙件 | `Dispose` 里 `_baker.Dispose()` | 不在本插件里，但必须转交 |

## 九、渲染：本插件只攒计划，不开窗

`AnnotationRenderer.Render` 的全部工作就 12 行：

```csharp
var plan = new BakePlan();
AddLayer(plan, caliperRects,  "yellow", 1);   // 缺席的图层（null / 未初始化）不进计划
AddLayer(plan, measureLines,  "cyan",   2);
AddLayer(plan, fittedGeometry,"magenta",2);
AddLayer(plan, edgePoints,    "green",   2);
plan.AddTextBlock(lines, textColor, 12, 12, 20, box: true);
return _baker.Bake(baseImage, plan);
```

- 清单顺序 = 叠放顺序；四律全部 `"margin"`（描边不填充，盖不住被检特征）；
- `AddLayer` 的 null/未初始化挡板是必要的：烘焙件对未初始化对象会逐条告警，空对象集反而合法（会自己跳过且不告警）；
- 底图**直接给原图**：通道数/位深归一化（4 通道丢 alpha、非 byte 按实际灰度范围拉伸）由共享烘焙件负责，
  本插件不再自己转一遍（历史上这里缺过一次拉伸，12/16 位相机整片死白）；
- 2026-10-13 迁入共享件之前，本类自带一份约 90 行、与另三个插件逐字相同的窗口基础设施，而且**漏了 SetPart**
  → 标注底图整片塌成窗口底色，实测 `(470,320)=0`（亮带内期望 255）。迁移后同一条断言转绿。
- 判据：`grep -rn "OpenWindow" Plugins\Plugin.CaliperMeasure` 必须为空（R36，全仓库只允许一处开窗）。

## 十、预览的线程模型

- `SchedulePreview` 第一行 `if (!_isConfigInstance) return;`（纵深防御：订阅本身已按配置态收口）；
- `DispatcherTimer` 只能在自己的创建线程上启停，所以非 UI 线程调用先 `UiDispatcher.BeginInvoke` 投递回去；
  没有 `Application`（单测/离线跑流程）时 `UiDispatcher` 为 null，按"就在 UI 线程"处理；
- `RefreshPreview` **在 UI 线程同步执行**——这是刻意的：异步化会引入"图被流程线程释放掉"的竞态，
  同步跑就不存在；代价是最坏一次完整测量占一个 200ms 后的 tick，可接受；
- 预览异常**不弹框、不抛出**，只把原因写进信息栏（`预览失败：…`）；
- 预览失败时 `result.DisposeOutputs()` 后再 `PreviewImage = null`：失败轮的标注图不需要，必须立刻回收。

## 十一、几个"为什么长这样"

1. **为什么 N 把卡尺不生成 N 个动态输出**：端口拓扑必须与卡尺数无关，否则改 N 会把下游连线剪断；
   几何全部合成一个 XLD 对象集。
2. **为什么 `FittedGeometry` 对宽度/两点距沿用测量连线**：历史语义（第一批只有这两种类型），
   改成 null 会打断已在用它的下游；文档与端口描述都明写了这层差异。
3. **为什么残差用中位数**：`FitMaxError` 若按单点最大值判，一个毛刺就能把 Tukey 已经正确拟合的结果判死，
   抗离群形同虚设。
4. **为什么"必须全检"**：用检到的几把平均出一个"看似合理"的数是计量最危险的失败模式（错了但不报错）。
5. **为什么重试 4 次**：同参首跑偶尔返回 0 条边（实测 0/1/1），只兜这个；真没边时第 4 次仍空、照样失败。
6. **为什么 `GetEffectivePixelSizeMm` 是 public**：回归断言与界面都要读同一个口径，不能再实现一遍三分支。
7. **为什么默认值抽成常量**：16 个出厂值是"通用平台起点"，集中一处便于复核，
   也避免"字段初值与校验基准各写一份"造成静默失配。
8. **为什么 `EdgeSelect.First` 的注释写着"默认"而实际默认是 `All`**：字段初值以
   `private const EdgeSelect DefaultSelect = EdgeSelect.All;` 为准；改注释或改默认只能有一处真相。
9. **为什么枚举显示名与算子字面量分离**：界面中文走 `[Display]`，算子参数由 `switch` 按成员名生成，
   改显示名不会改到传给 HALCON 的参数（R40）。
