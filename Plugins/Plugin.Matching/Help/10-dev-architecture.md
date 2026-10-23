# 模板匹配 · 开发：架构与数据流

> 分类：定位 ｜ 来源：插件 Plugin.Matching ｜ 读者：开发

本页只讲实现：类怎么组织、状态有几份、`RunAlgorithm` 每一步在做什么、内存归谁。
用户口径（怎么用、怎么调）在用户册 01~05 页，这里不重复。

## 一、一个类兼任两个身份

```
[Display(Name = "模板匹配", GroupName = "定位", …, ShortName = "\uf140")]
[ParallelSafe]                       // 二期真并行：纯视觉计算
public partial class MatchingPlugin : VisionPluginBase,        // 宿主契约：端口 / Execute / 灌值 / 落盘
                                    IPluginCustomViewProvider  // 自带配置界面（DataContext = this）
```

- **插件身份**：`RunAlgorithm(IExecutionContext)` 是唯一的执行入口，端口全是 `InputPort<T>` / `OutputPort<T>` 只读属性；
- **ViewModel 身份**：配置界面的绑定目标就是插件实例本身（`MatchingView` 的 `DataContext = this`），
  所以 `Library` / `EditingEntry` / `CanvasRois` / `StatusMessage` 这些"界面属性"与"执行状态"住在同一个对象里。
  代价是要显式区分**配置实例**与**编译实例**（见第三节）。

`partial` 是必需的：`MinScore` 等六个运行期参数用 partial property + `[StepConfig]`，
声明由 `Core.PluginGenerators` 生成（仓库纪律 R16/R18——改生成器要全量重建）。

## 二、三份状态，各自只有一个真相源

| 状态 | 类型 | 角色 | 谁写它 | 谁读它 |
| --- | --- | --- | --- | --- |
| `TemplateLibraryJson` | `[StepConfig] string`，默认 `"[]"` | **落盘真相**：整库的 JSON 序列化结果 | `OnConfirm`（`JsonConvert.SerializeObject(Library)`） | `ApplyConfigValues` → `ParseLibraryAndMigrate()` |
| `Library` | `ObservableCollection<MatchingTemplateEntry>` | **内存真相**：一个实例一个库（上限 20 条） | 解析、`新增`/`删除`、画布回写 `entry.Shape/Rect`、学习写 `entry.Model`/`LearnedSignature` | 配置界面 ListBox、`ResolveEntry`、`Dispose` |
| `EditingEntry` | `MatchingTemplateEntry?` | **当前编辑条目**（区域/涂抹/提取参数的作用对象） | ListBox 选中项 TwoWay 绑定、`Initialize`、`AddTemplateEntry` | 所有 ① 区控件、`CreateTemplate`、预览 |
| `CanvasRois` | `ObservableCollection<DrawingObjectInfo>` | 画布句柄集合（配置会话的**投影**，不是真相） | 控件右键新建/删除、`SeedCanvasFromEntry`、`ClearTemplate` | `WriteBackTemplate` |
| `SmearMask` / `SmearEraseRegion` | `HRegion?` | 涂抹两笔（活对象，setter 里释放旧值并写回条目） | 控件笔画结束以新实例回写 | `BuildSmearExclusion`、`EntrySignature`、序列化 |
| `DisplayImage` | `HImage?` | 配置态参考图（只喂画布底图，**不参与运行**） | `LoadPreviewImage` / 上游桥接 | `ImageEdit.HImage`、涂抹坐标系 |
| `TemplatePreviewImage` | `HImage?` | 左下角"绿色特征边"预览 | `RefreshPreviewForEntry` | 预览控件 |
| `entry.RuntimeModelId` / `RuntimeModelSource` / `RuntimeContours` | `[JsonIgnore]` | **运行期缓存**：句柄、载荷来源、轮廓 | `EnsureModelLoaded` | `RunAlgorithm`、`RenderResultAnnotation` |

三份状态的关系是单向的：`TemplateLibraryJson ⇄ Library ⇄ 画布投影`。
任何"从画布读回来的值"都要落到 `entry`（区域与掩膜），任何"从方案读回来的值"都要落到 `Library`，
**没有任何一份状态有两个写入者**——这是这个类能不出并发问题的前提。

## 三、双向同步的回路与各自的刹车

```
画布（控件拥有句柄）──Changed──▶ WriteBackTemplate ──▶ entry.Shape / entry.Rect
        ▲                                                      │
        │                                                      │
        └──── SeedCanvasFromEntry ◀── 订阅切换 / 换条目 ◀───────┘
```

| 回路 | 触发 | 刹车 | 少了刹车会怎样 |
| --- | --- | --- | --- |
| 播种画布 | 换条目、`Initialize`、从方案恢复 | `_seedingCanvas` 置真（`try/finally`） | 播种触发 `CollectionChanged` → 回写条目 → 再通知，**同一区域被重复追加**（重开配置框翻倍一族） |
| 画布回写 | 用户新建/删除/拖句柄 | 只认**最后一个** `DrawingObjectInfo`（一个条目只认一个区域）；`HasTemplateRegion` 形状感知判空 | 多框时"哪个算数"不确定，用户以为前一个还在 |
| HTuple 变化 | 句柄被拖 | `OnRoiTuplesChanged`（订阅/退订随条目切换） | 换条目后旧句柄的拖动仍在写新条目 |
| 涂抹写回 | `SmearMask`/`SmearEraseRegion` setter | `_seedingCanvas` 期间不写回；否则 `WriteBackSmearToEntry` + `TouchModelStaleness` | 重学提示不出现，或反过来播种时误标脏 |
| 配置态隔离 | `Initialize` 里 `MarkAsConfigInstance()`（**必须在 `base.Initialize` 之前**） | `EditingEntry` setter 只在 `_isConfigInstance` 时播种画布与刷预览 | 编译实例也去读参考图、反序列化模型、开离屏渲染窗口 → 每次编译都白干一遍 |

`SeedCanvasFromEntry` 的完整动作：退订旧句柄 → `CanvasRois.Clear()` → 按 `entry.Shape/Rect` 造句柄 →
从 `entry.Mask` 还原 `SmearMask` → 重新订阅；整个动作包在 `_seedingCanvas = true` 的 `try/finally` 里。

## 四、一次配置会话的时间线

```
宿主 PluginConfigShell 打开
   └─ GetConfigView(stepData)
        ├─ Initialize(stepData)                 ← MarkAsConfigInstance() → base.Initialize（灌端口+StepConfig）
        │     └─ ParseLibraryAndMigrate()       ← 库解析 + 老方案迁移
        │     └─ EditingEntry = 默认名命中 ?? 首条 ?? 新建"模板1"
        └─ new MatchingView { DataContext = this }
              └─ Loaded → OnViewLoaded()        ← ShowUpstreamImage() 命中就"已显示上游图像…"，否则 LoadPreviewImage()
用户操作（每个动作都只改内存态）
   新增 / 删除 / 设为默认 / 改配方名 / 右键画框 / 涂抹 / 改②区 / 点「创建模板」/ 预览刷新
点「确定」
   └─ OnConfirm(stepData)
        ├─ WriteBackSmearToEntry(EditingEntry)   ← 当前涂抹先落进条目（否则丢了最后一次笔）
        ├─ TemplateLibraryJson = JsonConvert.SerializeObject(Library)
        └─ base.OnConfirm(stepData)              ← 端口值 + 全部 [StepConfig] 写回 InputValues（JSON 深拷贝快照）
点「取消」或关窗
   └─ Dispose()                                  ← 见第七节；不写盘
```

## 五、运行期：一次 `RunAlgorithm` 干了什么

```
1  Success=true、ErrorMessage 清空由基类 Execute 预置；本方法自己清非 IDisposable 输出端口：
     Score/Row/Column/Angle=0.0、MatchCount=0、MatchedTemplate=""、Rows/Columns/Angles/Scores/Scales = new HTuple()
2  Image.ActualValue 为 null 或 !IsInitialized() → Fail("输入图像为空或未初始化") + Logger.Error，return
3  ResolveEntry(out error)：三级配方选路（RecipeName 有值精确匹配 → DefaultTemplateName → 首条）
     失败文案把库里现有名字列出来；成功后立刻 MatchedTemplate.Value = entry.Name
4  entry.Model 为空 → Fail($"模板「{entry.Name}」尚未学习：请打开配置界面完成学习")，return
5  EnsureModelLoaded(entry) 为 false → Fail($"模板「{entry.Name}」加载失败（载荷损坏？请重新学习）")，return
6  取灰度：TryToGrayImage(src, out gray, out err)
     1 通道 copy_image、3 通道 rgb1_to_gray、4 通道 access_channel×3 + rgb3_to_gray，
     其它通道数 → "不支持的图像通道数：N（仅支持 1 通道灰度、3 通道彩色或 4 通道 RGBA/BGRA）"
7  角度：MinAngleDeg/MaxAngleDeg 各夹 [-180,180] 转弧度，min>max 时交换，
     angleExtent = Math.Min(maxRad - minRad, 2π)
8  运行参数夹取：minScore[0.01,1]、numMatches[1,100]、maxOverlap[0,1]、greediness[0.1,0.9]、numLevels[1,10]
8b 搜索域（P1-B A1）：UseSearchRegion 且 SearchRegion 端口已初始化
     → ReduceDomain(gray, region, out reduced)，后续查找全部用 reduced
     开关开但区域没连 → 回落全图，日志写"搜索域开关已开但未连区域 → 回落全图"
9  查找按 entry.Algorithm 三分支：
     Ncc            → FindNccModel(reduced, id, minAngleRad, angleExtent, minScore, numMatches,
                                    maxOverlap, "true", numLevels, out rows, out cols, out angles, out scores)
                      scales = Enumerable.Repeat(1.0, rows.Length)
                      行/列再加上 (entry.Rect[0]-ModelOriginRow, entry.Rect[1]-ModelOriginCol) 补偿
      级联回退        → 见 §5.1：learned 级联没有时 HALCON 会自动减层，不需要插件处理
     ScaleEnabled   → FindScaledShapeModel(..., "least_squares", numLevels, greediness, …)
     其它            → FindShapeModel(..., "least_squares", numLevels, greediness, …)
     未启用缩放时 scales = Enumerable.Repeat(1.0, rows.Length)（只为保住五个数组一一对应）
10 n == 0 → Fail($"未找到目标：模板「{entry.Name}」没有满足分数 ≥ {minScore:0.00} 的匹配（光照/遮挡变化？可降低分数下限重试）")
     + Logger.Warn（族里唯一 Warn）+ RenderResultAnnotation(src, entry, null, null, null) + PublishIfConfigured()
     + return   ← 失败轮也出标注图，这是刻意的
11 标量端口 = 第 0 个实例：Score/Row/Column 直取，Angles 送端口前 × 180/π（弧度→度）
12 数组端口：Rows/Columns/Angles(度)/Scores/Scales 各包一个 HTuple；Logger.Info 一行汇总
13 OutputAlignedImage 为真时：entry.Rect?.Length != 5 → Fail("模板「X」的区域参数损坏（应为 5 个值，实际 N 个）…")
     VectorAngleToRigid(rows[0], cols[0], angles[0], entry.Rect[0], entry.Rect[1], 0, out hom)
     AffineTransImage(src, out aligned, hom, "constant", "false")   ← 参数顺序坑，见 #3147
     AlignedImage.Value = new HImage(aligned)                        ← 端口持有独立副本
14 catch → Fail($"模板匹配失败：{ex.Message}")
15 finally：释放本次的临时对象（灰度图、拼接的轮廓等）；标注图渲染异常只吞掉、不改成败
```

**为什么第 1 步要插件自己清**：基类 `AutoDisposeRoundOutputs` 只回收 `IDisposable` 输出（`HImage`/`HXLD`），
`double`/`int`/`string`/`HTuple` 会原样留着上一轮的值——失败轮下游就会读到上一件的位姿（"幽灵结果"）。

## 六、学习期：一次 `CreateTemplate` 干了什么

```
1  三重前置校验（各自一条橙字，界面上就是这一句）：
     列表里没有条目 → "请先在模板列表里新增一个条目"
     没有模板区域   → "请先在右侧图上右键 → 新建矩形/圆形/椭圆，框住模板特征，再创建模板"
     没有参考图     → "请先载入参考图像（或试运行把上游图带进来），再创建模板"
2  GenTemplateRegion：按 Shape 走 gen_circle / gen_ellipse / gen_rectangle2
3  BuildSmearExclusion =（绘制 − 擦除）∩ 区域，再 Difference + ReduceDomain
   ← 掩膜只在**学习时**参与求交；条目里存的是**原始涂抹**（原因见本节末与第十节）
4  转灰度、参数夹取：角度步长非 0 时夹 [0.1,30] 否则送 "auto"；梯度阈值非 0 时夹 [1,255] 否则 "auto"；
   metric = use_polarity / ignore_polarity；缩放窗口退化（hi-lo < 1e-6）时撑开 ±0.005（限 [0.5,2]）
5  按 Algorithm 分叉：
     Ncc            → CreateNccModel(gray, numLevels, minAngleRad, angleExtent, angleStep, metric)
                      只有 6 个入参：**没有对比度、没有极性、没有缩放**（HALCON 无 create_scaled_ncc_model）
     ScaleEnabled   → CreateScaledShapeModel(gray, numLevels, minAngleRad, angleExtent, angleStep,
                                             scaleLo, scaleHi, "auto", "auto", metric, contrast, "auto")
     其它            → CreateShapeModel(gray, numLevels, minAngleRad, angleExtent, angleStep,
                                        "auto", metric, contrast, "auto")
6  原点锚定（**仅形状模型**）：用新模型在参考图上自匹配一次（minScore 0.8、numMatches 1、maxOverlap 0.5、
    greediness 0.9），再 SetShapeModelOrigin(modelId, sr[0] - CanvasRect[0], sc[0] - CanvasRect[1])
    ← 依据：found_after = found_default − offset；实测圆形区域默认原点偏 +1.5,+1.5；**自匹配失败就静默不锚**
    ← NCC 没有 set_ncc_model_origin，这一步整体跳过；代价是输出点恒为模型重心，
      输出侧用 (Rect[0]-ModelOriginRow, Rect[1]-ModelOriginCol) 补偿（当前实现里这两个差是 0，
      保留方法只为把"为什么 NCC 不需要偏移"留在代码里）
7  写临时文件：形状 .shm / NCC .ncm（**扩展名必须跟着算法走**，HALCON 靠它判类型）
    WriteShapeModel / WriteNccModel → 读字节 → base64 → entry.Model；掩膜按原始涂抹编码 entry.Mask
8  GetShapeModelParams（9 个 out）/ GetNccModelParams（**5 个 out：levels/minAngle/angleExtent/angleStep/metric**）
    取**实际**层数（不是请求值），拼成功文案；清理临时文件与句柄
9  entry.LearnedSignature = EntrySignature(entry)（**算法|**形状|区域|层数|梯度|角度|步长|极性|缩放|Mask.Length）
    ← 算法放第一位：换算法等于换了模型类型，指纹必变，徽标立刻转橙"需重新学习"
10 失败 → "创建模板失败：{ex.Message}（区域里特征太少？换一块纹理更丰富的区域试试）"
```

`EntrySignature` 把 `Mask.Length` 放进去是**修正后的口径**：早期版本把"与区域求交后的掩膜"长度放进签名，
于是每次学习都会让签名与存盘值不一致，界面天天误报"需重新学习"。存原始涂抹、签原始长度，两端才对称。

## 七、内存所有权表（谁 new、谁 dispose）

| 对象 | 谁创建 | 谁释放 | 备注 |
| --- | --- | --- | --- |
| 输入 `HImage`（`Image.ActualValue`） | 上游 | **不归本插件** | 基类透传防御也会跳过与上游同引用的输出 |
| 灰度图 / 临时区域（每次运行） | 本方法 | 同方法 `finally` | 每轮临时对象集中在一个 `temp` 列表里统一释放 |
| `MeasureImage.Value` | `RenderAnnotated` 返回的新 `HImage` | 基类下一轮回收或 `Dispose` | 渲染失败时该端口为 null，不算失败 |
| `AlignedImage.Value` | `new HImage(aligned)`（独立副本） | 基类 | 用副本是因为 `affine_trans_image` 出的 `HObject` 生命周期不归端口 |
| `RuntimeContours` | `GetShapeModelContours` | `ReleaseEntryModel` | 与句柄同生命周期，换载荷时一起重建 |
| `entry.RuntimeModelId` | `ReadShapeModel`（临时 .shm） | `ClearShapeModel` | 删除条目时**必须先释放句柄**——条目已从 `Library` 摘掉，`Dispose` 够不着它 |
| `SmearMask` / `SmearEraseRegion` | 控件回写的新实例 | setter 里释放被替换的旧实例 | VM 是唯一所有者，控件回写总是新对象 |
| `DisplayImage` | 各载入路径 | **`Dispose()` 末尾 `DisplayImage?.Dispose()`** | 它的 setter 是普通 `SetProperty`，不释放旧值，换图路径靠各调用点手动 Dispose |
| `TemplatePreviewImage` | 预览渲染 | setter 释放旧值 | 与 `DisplayImage` 口径不同，别照抄 |
| `_baker` | 字段初始化 | `Dispose()` 里 `_baker.Dispose()` | 离屏窗口按尺寸常驻，见第八节 |

## 八、标注是怎么画上去的（本插件只攒 plan）

```
private readonly AnnotationBaker _baker = new();
…
var plan = new BakePlan();
if (overlay != null && overlay.IsInitialized())
    plan.Objects.Add(new BakeObj(overlay, overlayColor, 1, "margin"));
plan.AddTextBlock(lines, textColor, 12, 12, 16, box: true);
return _baker.Bake(baseImage, plan);
```

- **本插件里没有 `OpenWindow`**：全仓库只有一个离屏开窗实现（`Services\Core.Halcon\Helpers\AnnotationBaker.cs`），
  插件侧只组装 `BakePlan`（图层顺序 `Shapes → Objects → Texts`、`box:` 大写 B、色名走白名单）；
- 轮廓搬运是**本插件自己**做的：`PlaceContoursAtPoses(contours, rows, cols, anglesDeg)` 对每个实例
  `VectorAngleToRigid(0,0,0,row,col,deg*π/180)` + `AffineTransContourXld` + `ConcatObj`（逐步释放中间对象）；
  它是 `public static` 且刻意公开，是为了让断言能直接喂已知位姿验算子（第十节）；
- 文字两行：`模板「X」匹配 N 个实例，最高分 …` + `位置 (r, c)  角度 a°`；未找到时一行
  `未找到目标（要求分数 ≥ 0.50）`，色名 found → `green`、not found → `red`。

## 九、投图与运行实例的分工

```
if (DisplayViewIndex > 0 && MeasureImage.Value is HImage anno)
    this.PublishPreview(anno, DisplayViewIndex);
```

`DisplayViewIndex` 就是窗口号（1~9 直传，0 = 不发布），事件消费端按等值筛选——
**写 `+1` 会把"窗口1"投进第 2 格、把"窗口9"投进不存在的格子**。
运行实例与配置实例共用 `RunAlgorithm`，差别只在配置态才有画布、预览与参考图。

## 十、几个"为什么这样实现"

| 问题 | 答案 |
| --- | --- |
| 库为什么存 JSON 字符串而不是复杂属性？ | `string` 经 `InputValues` 往返最稳（与模型 base64 同口径），不依赖宿主快照器能否深拷贝条目集合 |
| 模型解析为什么放在 `ApplyConfigValues` 而不是 `Initialize`？ | **编译器只调 `ApplyConfigValues`，从不调 `Initialize`**；放错地方就是"配置里明明已学习，运行却报尚未创建模板" |
| 为什么允许 `EnsureModelLoaded` 是 `static`？ | 它虽然静态，但读写的都是**传入条目**的本实例字段（`RuntimeModelId` 等），没有跨实例可变状态，所以 `[ParallelSafe]` 成立 |
| 掩膜为什么要"存原始、用交集"？ | 存交集会让签名与实际内容不对称（见第六节），且重开界面时编辑笔没了原样；用 `Mask.Length` 判定"变没变"必须与存的内容一致 |
| 为什么"未找到目标"用 Warn 而其余失败用 Error？ | 它不是故障，是现场：工件没来、被挡住、光照变了。产线日志不该被它淹掉，但流程仍要判失败 |
| `PlaceContoursAtPoses` 为什么单独抽出来并公开？ | 历史缺陷：端口数值正确而画出的轮廓不跟着转（角度被双重转换）。抽成纯函数才能用已知位姿直接验算，不必起流程 |
| 对齐图为什么用 `"constant"` 边界模式 + `"false"` 同幅面？ | 语义就是"画幅不变、出界填黑"，让下游固定坐标的算子零改动；参数顺序写反会撞 `#3147`（插值模式位） |
| 涂抹为什么要求显示图在？ | 掩膜要按参考图绝对坐标编码（`gen_image_proto` + `paint_region` + PNG），没有确定的图幅就没法编码 |
