# Blob 缺陷检测 · 开发：内部结构与数据流

> 分类：缺陷检测 ｜ 来源：插件 Plugin.BlobDetect ｜ 读者：开发

本页讲**这个插件怎么组织的、状态存在哪几份、一轮检测怎么从参数走到端口、谁负责释放**。
改它之前先读这页；要照它写新的"图像进、数值 + 标注图出"类插件，再读「宿主契约」与「不变量清单」。

## 一、文件分工

| 文件 | 行数 | 职责 |
| --- | --- | --- |
| `BlobDetectPlugin.cs` | 约 2140 | 流程节点 + 配置界面 ViewModel 合一（仓库既有插件同一套写法）：端口、`[StepConfig]` 参数、预览与自适应、`RunAlgorithm`、算法核心、判定、缺陷清单行模型 `DefectRow` |
| `BlobDetectEnums.cs` | 83 | `ThresholdMode` / `DetectTarget` / `DefectSortMode` / `StatusLevel`；前三者每个成员带 `[Display(Name)]`，界面中文名只有这一份真相 |
| `AnnotationRenderer.cs` | 90 | **只攒 `BakePlan`**：红边、编号、黄框、左上角横幅四层；开窗/回读/释放全在共享烘焙件 `AnnotationBaker` |
| `EnumDisplayNameConverter.cs` | 30 | 枚举 → `[Display(Name)]`，XAML 里以 `StaticResource EnumName` 使用 |
| `BlobDetectView.xaml(.cs)` | 932 / 34 | 纯绑定层。code-behind 只做两件事：`Loaded` 时通知 ViewModel 取输入图算预览；清单行选中转给 `HighlightDefect` |

一处要留意：算法核心与预览核心是**同一条路**（`DetectBlob` + `ArrangeJudgeRender`），
"所见即所得"靠这个保证——配置界面里看到的标注图与产线上 `DefectImage` 出自同一个渲染器、同一套文案。

## 二、一轮执行怎么跑（`RunAlgorithm` 逐步）

```
1  开轮重置：14 个"基类不会回收"的端口写回 0 / false / new HTuple()
2  SrcImage.ActualValue 取图；null 或未初始化 → Fail("输入图像为空或未初始化") 并 return
3  NormalizedParameters()：把界面值夹进算法能接受的区间，取 MaskRegion / ExcludeRegion 快照，
   并算出生效像素当量（GetEffectivePixelSizeMm）；若上游连线值无效 → PixelSizeError 非空
4  PixelSizeError 非空 → Fail(该消息) + Logger.Error 并 return（绝不静默回退手填值继续跑）
5  try { using var pack = DetectBlob(src, p, logger); result = ArrangeJudgeRender(pack, src, p, logger); }
   catch → Fail("Blob 缺陷检测失败：" + ex.Message) + Logger.Error 并 return
6  端口赋值：AnnotatedImage / Defects 只在非 null 时移交（渲染失败就保持轮首的空值状态）
7  数值与数组端口全量赋值（顺序 = keep 顺序 = 标注图编号顺序）
8  DisplayViewIndex > 0 且标注图非 null → this.PublishPreview(标注图, DisplayViewIndex)
9  !IsOk → ErrorMessage.Value = NgReason（Success 仍为 true：NG 是正常结果）
10 Logger.Info 一行「缺陷数 / 最大面积 / 总面积 / 判定（NG 时带原因）」
```

`DetectBlob`（阶段一，"有没有这些东西"）：

```
TryToGrayImage → GetImageSize →（有掩膜就 ReduceDomain 推域）
→ 二值化（Fixed: Threshold / Auto: BinaryThreshold max_separability / Dynamic: VarThreshold，
   亮暗同检 = 两种极性各跑一次再 Union2）
→（有排除区就 Difference）
→ OpeningCircle(OpenRadius>0) → ClosingCircle(CloseRadius>0)
→ Connection →（MergeRadius>0：Union1 → ClosingCircle → Connection）
→ FillUp(FillHoles)
→ SelectShape（area + 可选 circularity；面积上限 = MaxBlobArea>0 ? MaxBlobArea : 图像像素总数）
→ AreaCenter + SmallestRectangle1 一次算完候选特征
→ BuildKeepOrder（长宽比 / 触边筛选 + 排序）→ 需要重排时 RebuildRegion（SelectObj 索引 1 基 + ConcatObj）
```

`ArrangeJudgeRender`（阶段二，"这些东西合不合格 + 画给人看"）：

```
BuildKeepOrder（同一函数、同样入参，从候选特征再算一遍 keep）→ 计数 CountObj
→ PickTuple / BuildSizeTuple / BuildAspectTuple 出八列数组
→ RegionFeatures(circularity / phi) 包在独立 try/catch 里（失败只丢三列，绝不把成功判成失败）
→ MaxArea=TupleMax、TotalArea=TupleSum → mm² = 像素 × 当量²
→ JudgeCore（纯函数）→ BuildNgReason
→ new HRegion(defectsObj) 交端口
→ 组横幅文字 → 超过 300 个缺陷时只取前 300 个当绘制对象并追加一行提示 → AnnotationRenderer.Render
```

两个阶段用同一个 `BuildKeepOrder`：阶段一按当前参数把候选筛出来，阶段二再对候选做一次"长宽比 / 触边 / 排序"。
长宽比与触边**故意只放在阶段二**（`BuildDetectKey` 里不含它们），所以调这两项和调`输出排序`一样只重排不重检。

## 三、状态清单与真相

| 状态 | 存在哪 | 谁能改 | 什么时候清 |
| --- | --- | --- | --- |
| 25 个配置项 | `[StepConfig]` 属性 → 基类写进 `IStepConfigData.InputValues` | 界面绑定、`ApplyConfigValues`、自适应 | 只有用户改或加载方案；运行实例不改 |
| 出厂默认值 | 本类顶部一组 `private const Default*` | 只有代码 | 一处权威基准，字段初值与"是否默认值"判断都引用它 |
| 本轮检测结果 | 输出端口 | `RunAlgorithm` | 每轮开头显式重置（见不变量） |
| 预览标注图 | `PreviewImage` 属性 | 后台回填 | setter 释放旧图；`Dispose` 置 null |
| 阶段一检测缓存 | `_detectPack` / `_detectPackKey` / `_detectSource` | `PreviewRunCore` | 参数指纹变了、`Dispose` |
| 直方图 | `Histogram` 等六个绑定属性 | `ComputeHistogram` | 无图或失败时 `ClearHistogram` |
| 缺陷清单 | `DefectRows`（`ObservableCollection<DefectRow>`） | `FillDefectRows` | 每次刷新整表重建；失败轮清空 |
| 预览轮次与并发标志 | `_previewRound` / `_previewInFlight` / `_previewRefreshPending` | `RefreshPreview` 及其回调 | `Dispose` 时轮次号推进作废在途结果 |
| 配置态标志 | `_isConfigInstance` | `MarkAsConfigInstance` | 置上就不回退（幂等） |
| 适配说明 | `_adaptNote` | 自适应路径 | 写一次即随信息栏消费掉 |
| 高亮编号 | `_highlightIndex` | `HighlightDefect` | `Dispose` 归 0 |
| 离屏窗口 | `AnnotationBaker` 内部（共享件按尺寸常驻） | `Render` | `Dispose` 时 `_renderer.Dispose()` 关窗 |

## 四、配置态 / 运行态隔离（这条最容易写错）

```csharp
private bool _isConfigInstance;                     // 运行实例一律 false
private void MarkAsConfigInstance()                 // 幂等；只有配置态才订阅输入图变化
{
    if (_isConfigInstance) return;
    _isConfigInstance = true;
    SrcImage.ValueChanged    += OnSrcImageValueChanged;
    MaskRegion.ValueChanged  += OnSrcImageValueChanged;
    ExcludeRegion.ValueChanged += OnSrcImageValueChanged;
}
```

两个盖章入口：`GetConfigView` 先 `MarkAsConfigInstance()` 再 `Initialize(stepData)`
（这样灌值触发的 setter 也能正常排队预览），以及视图 `Loaded` 时的 `OnViewLoaded()`。
`SchedulePreview` / `RefreshPreview` / `HighlightDefect` 三个入口都先查 `_isConfigInstance` 再干活。

不隔离会怎样：① 产线每帧在 UI 线程白跑一遍全图算法 + 直方图 + 离屏渲染；
② 更严重的是**运行实例会按"首帧图像"静默改写 `MinGray`/`MaxGray`/`MinArea`/`MaxSingleArea`**，
现场表现为"参数自己变了"——工业软件里这比"参数不理想"严重得多。

## 五、预览的后台化与竞态防护

```
UI 线程只做三件便宜事：取源图引用 → CopyImage 出预览私有副本 → 自动适配（只读写参数，读的是副本）
后台线程：全图检测、直方图、标注渲染；完成后 PostToUI 回填绑定
```

| 防护 | 机制 | 防的是什么 |
| --- | --- | --- |
| 轮次号 | `++_previewRound`，回填时 `round != _previewRound` 就只释放不写 | 连点刷新时旧图盖新图 |
| 在跑标志 | `_previewInFlight` 为真时把新请求记进 `_previewRefreshPending`，跑完再按最新图与参数来一遍 | 并发把缓存替换与渲染缠在一起；中间态白跑 |
| 已释放守卫 | `_disposed`（volatile）+ 回填前检查 | 配置窗口关闭后在途结果往已释放的绑定上写 |
| 所有权交接 | `snapshot` 命中缓存立即 `Dispose`，未命中移交 `_detectSource` | 异常路径补刀释放缓存底图 → 下一轮预览直接崩 |
| 定时器归属 | `_previewDebounce` 惰性 `new DispatcherTimer(..., UiDispatcher)`，不在字段初始化器里 new | 后台线程 new 出来的定时器寄生在永不泵消息的 Dispatcher 上 → 预览静默失效、不报错、不留日志 |
| 无 Application 环境 | `UiDispatcher == null` 时走同步路径 | 离线跑流程 / 单元测试仍能算预览 |

## 六、"按当前图像自适应"的三条路径

| 路径 | 触发条件 | 说明 |
| --- | --- | --- |
| 自动 | `AreAllParametersAtFactoryDefault()` 为真且拿到图 | 25 个配置项**全部**仍等于出厂默认才动；浮点用 `1e-6` 容差比（换算会产生小数，`==` 会永远判成"改过"） |
| 补做 | `_pendingAdaptOnTargetChange` 且 `IsThresholdAtLastAdaptedValue()` | 改 `DetectTarget` 会让"是否默认值"守卫立刻判成"用户调过"，于是阈值停在暗侧、与意图相反；这里记一笔补做一次 |
| 手动 | 「按当前图像重新适配」按钮 | 任何时候都执行，不检查默认值 |

换算规则：阈值取**实际灰度范围**（不是按像素类型硬编码——12 位相机常只用 0~4095，而 `uint2` 存储范围是 0~65535，差 16 倍）
靠暗侧/靠亮侧的 25% 一段；面积以 640×480 为基准按面积比例缩放 `MinArea` 与 `MaxSingleArea`；
只改这四个值，不动二值化方式、检测目标、形态学参数。写值期间用 `_suppressPreviewSchedule` 屏蔽 setter 排队，
避免四个 setter 各触发一次全图重算。

## 七、阶段一缓存的指纹

`BuildDetectKey` 把"检测组参数 + 三个对象的引用标识"拼成串（分隔符是竖线）：

```
ThresholdMode|DetectTarget|DetectBrightAndDark
|MinGray|MaxGray|VarMaskWidth|VarMaskHeight|VarStdDevScale|VarAbsThreshold
|MinArea|MaxBlobArea|MinCircularity
|FillHoles|OpenRadius|CloseRadius|MergeRadius
|RuntimeHelpers.GetHashCode(source)|GetHashCode(MaskRegion.ActualValue)|GetHashCode(ExcludeRegion.ActualValue)
```

- 用引用标识而不是内容哈希：图像像素变了必然换对象，值变了不换对象的场景本插件不存在。
- `MaxAspectRatio` / `ExcludeBorderPx` / `SortMode` **不在指纹里**，因为它们由阶段二承担。
- `RegionFeatures` 那三列失败也不影响指纹：它们不是缓存的输入。

## 八、内存所有权

| 对象 | 谁创建 | 谁释放 |
| --- | --- | --- |
| 输入图 `src` | 上游端口 | 本类只读，**任何路径都不释放**（含 `DetectBlob(src, …)` 的 `src` 形参） |
| 预览私有副本 `snapshot` | `src.CopyImage()` | 命中缓存 → 本轮立即 `Dispose`；未命中 → 移交 `_detectSource` |
| `DetectBlob` 里的中间 `HObject` | 各算子 | 全部登记进 `temp`，`finally` 统一释放；产物 `Candidates` 返回前从 `temp` 摘除 |
| `DetectPack` | `DetectBlob` | `Dispose()` 释放 `Candidates`；运行期由 `using var pack` 收口，配置态由缓存管 |
| `Defects` 端口值 | `new HRegion(defectsObj)`（独立句柄） | 基类 `AutoDisposeRoundOutputs` 或 `Dispose` |
| `DefectImage` 端口值 | `AnnotationBaker.Bake` 返回的新图 | 同上（所有权随端口赋值移交） |
| 借用值 `Mask` / `Exclude` | 上游 | 端口所有，算法里绝不 `Dispose` |
| 预览图 | `Render` | `PreviewImage` setter 释放旧值 |
| 离屏窗口 | `AnnotationBaker` | `Dispose` 里 `_renderer.Dispose()` |

## 九、为什么这样实现

- **两阶段拆分**：判定参数（个数/面积规格、排序、长宽比、触边）与检测参数（阈值、形态学、筛选）解耦，
  调判定时只重排重渲染，大图调参的交互延迟从"全图一遍"降到"排序 + 渲染"。
- **NG 不写 `Fail`**：判定是业务结果，执行状态是技术结果，混起来会让下游用执行状态当分流条件、
  把 NG 当异常走。这条是硬契约（见不变量）。
- **静默夹取而不是抛错**：方案文件可能来自老版本、也可能被手工改过；越界值喂给算子会抛 HALCON 异常把整条流程带崩。
  原则是"配置坏了最多结果不对，绝不炸流程"。
- **`MaxBlobArea` 与 `MaxSingleArea` 分成两项**：一个是"这东西算不算缺陷"，一个是"这个缺陷合不合格"。
  合一时代的筛选上限写死 1e7，在 >1000 万像素相机上整块背景被静默滤掉——漏检还报 OK。
- **长宽比自己算**：本仓 HALCON 23.05 的 `select_shape` / `region_features` 都不认 `elongation`（报 `#3101`），
  仓库脚本模板里教用户写它本身就是错的。
- **合并必须先 `union1`**：直接对区域数组做形态学，HALCON 是逐对象处理，段与段合不到一起。
- **渲染失败不改判**：`AnnotatedImage` 为 null 时端口保持轮首空值，判定与数值端口照常输出全量。
- **编号锚点用外接矩形中心而不是质心**：细长 / L 形区域的质心可能漂到区域外甚至图外。
