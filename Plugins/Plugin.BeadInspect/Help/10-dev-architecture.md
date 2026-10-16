# 胶路检测 · 开发：内部结构与数据流

> 分类：缺陷检测 ｜ 来源：插件 Plugin.BeadInspect ｜ 读者：开发

本页讲**这个插件怎么组织的、状态存在几份、一轮检测怎么从配方走到端口**。
改它之前先读这页；要照它写新的"学习—检测"类插件，再读「宿主契约」与「不变量清单」。

## 一、文件分工（算法与界面分离）

```
BeadInspectPlugin.cs              运行内核：[StepConfig]、5 入 16 出端口、RunAlgorithm、
                                  ApplyConfigValues/OnConfirm/Dispose、模型指纹缓存
BeadInspectPluginConfigView.cs    同一个 partial 类的第二部分：全部配置态（画布拾取、点列缓存、
                                  撤销栈、底图四级优先、学习、自动提取、矫正四点、横幅/状态栏）
BeadInspectHalcon.cs              纯算子层：折线构建 / 建 bead 模型 / apply / 平面学习(find+投影) /
                                  胶条分割 / 骨架树直径 / 抽稀 / 偏离度量——不依赖 WPF
Models/BeadRecipeEntry.cs         配方条目：落盘字段 + 运行期句柄槽 + 指纹与 JSON 解析的唯一入口
BeadViewModels.cs                 两个行 VM（点列行、配方行）+ StatusLevel 三态
BeadInspectView.xaml(.cs)         纯绑定层 + 鼠标/按钮转发，无业务
```

分层的**动机写在源码注释里**：将来把「平面可变形对齐」抽成独立算子时，`BeadInspectHalcon` 可以直接搬走。

## 二、配置态与运行态是**两个实例**

```
配置实例：宿主打开配置 → GetConfigView → MarkAsConfigInstance() → Initialize(stepData) → new BeadInspectView{DataContext=this}
运行实例：流程编译器只调 ApplyConfigValues(stepData)，**从不经过 Initialize**
```

因此 `BeadInspectPluginConfigView.cs` 里的全部字段（`_pathRows`、`_undoStack`、`DisplayImage`、`PreviewImage`、
`EditingEntry`、`_rawRefImage`、`_quadPoints`…）只在配置实例上活跃；`RunAlgorithm` 一个都不读。
配置实例还额外订阅 `SrcImage.ValueChanged`（收"上游每帧新图"来兜底铺底图）与 `AnnotatedImage.ValueChanged`
（把试运行的标注图同步进预览面板）——这两个订阅在 `DisposeConfigState()` 里成对退订。

**推论**：`MarkAsConfigInstance()` 是幂等闸门；任何"配置态才有意义"的新字段都要走同一入口，
否则会在运行实例上被读到半初始化的值。

## 三、状态副本与真源

| 状态 | 角色 | 谁写 | 谁读 |
| --- | --- | --- | --- |
| `RecipeLibraryJson` | **存盘态**（`[StepConfig]`） | `OnConfirm` 整表序列化 | `ApplyConfigValues` 反序列化 |
| `Library`（`ObservableCollection<BeadRecipeEntry>`） | **内存真源** | 解析库、新增/删除、画布与表格编辑直接改条目 | `RunAlgorithm` 选配方、`RecipeRows` 摘要 |
| `EditingEntry` / `EditingRow` | 当前编辑条目（列表选中 ↔ 编辑态双向映射） | 列表选中、新增/删除后回退 | 全部拾取/学习/提取入口 |
| `_pathRows` / `_pathCols` | 点列**编辑缓存**（画布与表格都改它） | 拾取、拖动、表格提交、自动提取、撤销 | 叠加层、计数、写回条目 |
| `entry.RefPointsJson` | 点列**存盘文本** | `AfterPathChanged` | `TryParsePoints`（运行期建折线、指纹、摘要） |
| `PointRows`（`ObservableCollection<BeadPointRow>`） | 表格**显示态**（行对象**原位更新**，不重建） | `AfterPathChanged` | DataGrid |
| `_undoStack` | 点列快照栈（上限 100 步） | 每次变更前 `PushUndo` | `UndoLastPick` |
| `entry.LearnedSignature` | 学习指纹（「已学习」的唯一依据） | `LearnRecipe` 成功末尾 | `RefreshRecipeRows` 比对 `CurrentBeadSignature` |
| `entry.RuntimeBeadModel` + `RuntimeBeadSource` | bead 句柄 + 其指纹 | `EnsureBeadModel` | `ApplyBead` |
| `entry.RuntimePlanarModel` + `RuntimePlanarSource` + `RuntimeRowT/ColT` + `RuntimePlane` / `RuntimeRectifiedRef` | planar 句柄与对齐锚点/平面区/矫正参考图 | `EnsurePlanarModel` | `AlignImage`、底图①、自动提取差分 |
| `_rawRefImage` + `_rawRefPath` | 参考图原始缓存（同路径不重复读盘） | `LoadRefImage` | 底图②、矫正四点拾取 |
| `DisplayImage` / `PreviewImage` | 画布底图 / 预览图（setter 释放上一张） | `SetBaseImage`、`RenderLearnPreview`、试运行标注图 | 画布与预览面板 |

**为什么点列要三份**（缓存 / 条目 JSON / 表格行）：画布拖动是高频路径，直接改 JSON 会让每帧做一次序列化；
整表重建行对象又会让 DataGrid 的滚动位置与选中态跳动。所以缓存是唯一的编辑真源，
`AfterPathChanged()` 是它对外扩散的**唯一出口**——写回 JSON、原位刷新表格行、重画叠加层、更新计数四件事一起做。

## 四、三条同步回路与各自的刹车

```
回路 ①  画布鼠标 → AddPoint / DeleteNearestPoint / MovePointDrag → 改缓存 → AfterPathChanged
        刹车：BeginPointDrag 命中 12px 内的已有点就**不再加点**；拖动期间撤销快照只在
              "真正移动的第一下"入栈（_dragUndoPushed），否则一次拖动塞进几十步撤销。
              插点失败时把刚 PushUndo 的那一步**弹掉**，不留假撤销。

回路 ②  表格 CellEditEnding（Dispatcher 投递，等 Binding 提交完）→ CommitTableToCache → 缓存
        刹车：_syncingTable 防重入；行数与缓存数不等直接放弃；逐项比对无变化就**不推撤销**也不写回。

回路 ③  换配方（EditingEntry 变）→ SeedFromEntry：解析 JSON → 缓存 → 表格 → 叠加层 → 载参考图 → 底图
        刹车：矫正四点拾取进行中（QuadPicking）时上游图变化**不许换底图**——那一步的源四点吃的是
              原始参考图坐标系，中途换底图会让点选坐标与记录坐标对不上。
```

底图与横幅也有联动：`SetBaseImage` 是底图/空态/来源文案三者的唯一出口，
且"底图不再是输入图兜底"时会**自动折叠**上一轮的坐标系警告横幅（否则横幅与画布自相矛盾）。

## 五、一次 `RunAlgorithm` 干了什么

```
0. 轮首手动重置 12 个标量/数组端口（IsOk、NgReason、ErrorCount、四个数组、TotalErrorLength、四类计数）
   —— 基类只自动回收 IDisposable 的端口值，double/int/string/HTuple 不归它管，脏值会骗下游一整轮
1. src = SrcImage.ActualValue；null 或未初始化 → Fail("输入图像为空或未初始化") → return
2. ResolveEntry：RecipeName 端口 trim → 库内按名精确匹配（找不到就把库里的名字列出来）→
   DefaultRecipeName → Library 首条；空库 → Fail("未找到可用配方：…") → return
3. 参考路径预检：TryParsePoints（空/坏点/语法给中文原因）+ TryBuildContour（<2 点、近似重合=退化）
   —— 预检用的临时折线立刻 Dispose，正式的 RuntimeContour 归条目所有
4. 对齐（按 AlignMode）：
   PoseFromMatching → AlignedImageIn 必须有图，否则 Fail（点名两条对策）；有图则 CopyImage 一份
   PlanarDeformable → EnsurePlanarModel（缺/无参考图先 Fail）→ AlignImage（find + 投影搬图），
                      成功再 Info 一条 score
   None             → aligned = src（**引用复用，不复制**）
5. RoiRegion 有效 → ReduceDomain（只缩域提速，坐标系不变）
6. 折算有效参数（配方 > 0 优先，否则插件级；极性 trim 或按枚举转 dark/light）→ EnsureBeadModel
7. ApplyBead → left / right / errSeg / errType；三个对象进 temp 统一回收
8. 逐段过滤与统计：SelectObj(i) → length_xld；< MinErrorLength 的段**就地 Dispose 并 continue**；
   保留段 concat 进 keptSegs（concat 后立刻释放两个输入，所有权归 keptSegs），
   并行累积 keptTypes/Lengths/Rows/Cols（中心取 area_center_points_xld）与四类计数
9. UnitOutput=Mm → ResolveMmPerPixel（Transform 优先并校验图幅；回退 PixelSizeMm；皆无 Fail）→ 乘当量
   ⚠ 校验用的是**原始 src** 的尺寸，不是对齐图
10. 写端口：四个数组、ErrorCount、四类计数、TotalErrorLength、IsOk、NgReason；
    ErrorSegments = new HXLDCont(keptSegs)（无保留段就不写）；
    BeadContours = left+right concat（只有一侧就只给那一侧）；OutputAlignedImage 时 AlignedImage = new HImage(aligned)
11. 标注图：_renderer.Render(aligned, RuntimeContour, beadPair, keptSegs, lines, ok) → 非 null 才写端口
12. DisplayViewIndex > 0 且端口里有图 → PublishPreview(图, 窗口号)（直传窗口号，不做 +1 或换算）
finally：temp 里所有中间对象逐个 try Dispose（单个失败不阻断）
catch  ：任何未预料异常 → Fail("胶路检测失败：" + ex.Message)
```

**早退点的共同特征**：`Fail` 之后一律 `return`，且都在 `finally` 之前——所以失败轮的输出端口停在第 0 步的重置值，
下游读到的是"空/0/false"，不是上一轮残值。

## 六、模型生命周期（句柄不进方案文件）

HALCON 的 bead / planar 模型都不可序列化，所以**落盘只存输入**，句柄按指纹惰性重建：

```
bead 指纹   BeadRecipeEntry.BuildBeadSignature(target, tol, pos, polarity)
            = "bead|{RefPointsJson}|{target:0.###}|{tol:0.###}|{pos:0.###}|{polarity}"
planar 指纹 = "planar|{RefImagePath}|{文件字节数}|{LastWriteTimeUtc.Ticks}|{RectifyQuadJson}"
```

- bead 指纹的**唯一出处是条目上的那个方法**：运行期缓存键与配置界面「已学习」比对必须逐字符同口径，
  两处各自手写会让"已学习"永远对不上。
- planar 指纹带 mtime，是为了兜住"同路径覆盖文件但长度碰巧不变"这种情况。
- 命中指纹就直接复用句柄；不匹配才 `Release…` 再建，并让 `BeadModelRebuildCount` / `PlanarModelRebuildCount` 自增
  （这两个计数器是给断言 9 用的诊断面，同参数连续两轮必须不重建——planar 重建实测 100~360ms）。
- 释放：`RuntimePlanarModel` 走 `clear_deformable_model`（本机 halcondotnet 23.05 没有
  `ClearPlanarUncalibDeformableModel` 入口，反射核实过）；`RuntimeBeadModel` 走 `ClearBeadInspectionModel`；
  随句柄释放的还有 `RuntimeContour` / `RuntimeRectifiedRef` / `RuntimePlane`。
- 三个释放入口：`ApplyConfigValues`（重解库前逐条释放）、`DeleteSelectedEntry`（条目摘出 `Library` 后
  `Dispose` 再也够不着它，**必须就地释放**）、`Dispose()`（遍历 + 清库 + 配置态 + 渲染器 + base）。
- 平面句柄释放失败**不再静默**：有日志通道就 `log.Error`，没有（配置期/Dispose 期）落 `Debug.WriteLine`。

## 七、内存所有权

| 对象 | 创建处 | 谁释放 |
| --- | --- | --- |
| `IsOk` 等 12 个标量/数组端口 | 每轮重置与赋值 | 插件自己（基类不管非 IDisposable） |
| `AnnotatedImage` / `AlignedImage` / `ErrorSegments` / `BeadContours` | `RunAlgorithm` 每轮 | 基类轮首自动回收（`AutoDisposeRoundOutputs`） |
| `temp` 里的对齐图/缩域图/左右轮廓/错误段/保留段/concat 中间量 | 各算子输出 | `finally` 统一释放；**交接给端口或条目的那一份不进 temp**（或进 temp 但端口拿的是新包装句柄） |
| `entry.Runtime*` 句柄与对象 | `Ensure…Model` | `Release…`（换配方/换指纹/删条目/Dispose） |
| `_rawRefImage`、`DisplayImage`、`PreviewImage` | 配置态 | 各自 setter / `DisposeConfigState()` |
| 上游 `SrcImage.ActualValue` | 流程 | **本插件绝不释放**；配置态兜底铺底图时 `new HImage(upstream)` 只读复制一份 |
| 预检折线 `probeContour` | 第 3 步 | 立刻 Dispose（正式参考路径归条目） |

## 八、几个"为什么这样实现"

- **判定 NG 不走 Fail**：NG 是产线要的正常结果，下游必须拿到标注图与逐段明细；只有"这一轮没法算"才失败。
  话术统一按"现象 + 下一步"给（缺什么就点名补什么，不写"未知错误"）。
- **`None` 模式连 `CopyImage` 都不做**：固定相机时搬图纯属浪费；代价是 `aligned` 与 `src` 同引用，
  所以第 10 步给 `AlignedImage` 端口时用的是 `new HImage(aligned)` 独立包装。
- **角度用度、只在调算子处乘 `DegToRad`**：界面与存盘都是人话，HALCON 的弧度口径被封在算子层。
- **`TargetWidth` 下限 6 在赋值处夹**：`create_bead_inspection_model` 在 `target_thickness < 6` 时报
  HALCON #3716（英文，现场看不懂）。与其等算子炸，不如在 `[StepConfig]` 的 `On…Changing` 钩子里夹进合法域，
  算子层再守一道把异常翻译成中文。
- **碎段直接丢弃而不是标记**：短段的误报率远高于长段，保留会让 `ErrorCount` 失去产线意义；
  要看到碎段就调小 `MinErrorLength`，这是口径开关而不是数据缺失。
- **标注只攒计划不开窗**：离屏烘焙的六条硬纪律（`'buffer'` 开窗、窗口按尺寸常驻防 #9302、
  每轮无条件 `SetPart`、文字只用 window 坐标、显式 box、回读源对象立刻释放）全部落在共享件
  `AnnotationBaker` 里；本插件只声明"画哪三层、什么颜色、字号 16 带底框的文字块从 (12,12) 起"。
  烘焙失败返回 null，判定与数值端口不受影响。
