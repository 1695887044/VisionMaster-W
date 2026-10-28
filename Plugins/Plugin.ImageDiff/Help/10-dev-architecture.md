# 图像差分 · 内部结构

> 分类：图像处理 ｜ 来源：插件 Plugin.ImageDiff

开发页。讲这个插件由哪几个文件组成、一轮执行走哪些步骤、每个决策为什么这么定。
改代码前先读完这一页；改完照 12 页的不变量清单自检。

## 文件构成

```
Plugins/Plugin.ImageDiff/
├── Plugin.ImageDiff.csproj    net9.0-windows / UseWPF / WPF 插件
├── DiffEnums.cs               4 个枚举（算法 / 极性 / 聚合方式 / 尺寸策略）
├── ImageDiffService.cs        纯 HALCON 算法层（internal static，无状态）
├── ImageDiffPlugin.cs         步骤类：端口 + [StepConfig] + RunAlgorithm
└── Help/                      本手册（8 页，内嵌资源）
```

**没有 `XxxView.xaml`**。本插件不提供专属配置视图，参数走宿主通用属性面板
（`FlatPropertyGrid` 读 `[StepConfig]` + `[Display]`），结果走 `PublishPreview` 上屏。
这样做的理由：本插件的 12 个参数全部是标量/枚举，没有"需要在图上点两下才能配"的东西
（对比「模板匹配」要在画布上画框、要涂抹掩膜，所以才需要专属视图）。
少一个视图就少一份 XAML 与 VM 同步成本、少一个 R22/R173 这类资源作用域坑。

## 三层职责

| 层 | 文件 | 能做什么 | 不能做什么 |
| --- | --- | --- | --- |
| 策略层 | `ImageDiffPlugin.RunAlgorithm` | 判空、尺寸/通道守卫、按 `SizeMismatch` 分支、判上限、填端口、上屏 | 不直接调 HALCON 算子 |
| 算法层 | `ImageDiffService` | 调 HALCON 算子、算差异图与统计 | 不碰端口、不记日志、不读 `[StepConfig]` |
| 数据层 | `DiffEnums` | 定义可选值 | 无逻辑 |

这个分层不是洁癖：`ImageDiffService` 是 `internal static` 且不依赖 `IExecutionContext`，
所以断言宿主可以直接调它做像素级对照（喂两张构造图，断言 `DiffArea` 恰好是多少），
不用起流程、不用真窗口。这是它值得保住的唯一理由——**别往 service 里塞端口或日志**。

## 一轮执行：`RunAlgorithm` 的调用链

```
① 取输入（ActualValue，不是 Value）
   Reference.ActualValue / Test.ActualValue
      ↓ 空 → Fail 中文原因，return
② 量尺寸与通道
   ImageDiffService.Shape(reference) → (w, h, channels)
   ImageDiffService.Shape(test)      → (w, h, channels)
      ↓ 尺寸不同 → mismatch 字符串
      ↓ 通道不同且不可灰度化（非 1↔≥3） → mismatch 字符串
③ 按 SizeMismatch 处理 mismatch
   Fail      → Fail(说明 + "不会替你静默缩放")，return
   ScaleTest → zoom_image_size(bilinear) → testAdjusted
   CropCommon→ crop_rectangle1 到 min(w) × min(h)；< 8×8 → Fail，return
④ 转灰度
   ToGray(refUse) / ToGray(testUse)     3 通道走 rgb1_to_gray，1 通道 copy_image
⑤ 算差异图（real 域）
   ComputeDifference(refGray, testGray, Algorithm, Polarity, AmbientFloor, SignedDisplay)
⑥ 分割 + 统计
   Segment(diff, Threshold, Aggregation, MinArea, mask)
     → Threshold → [Intersection mask] → Connection → [SelectShape area]
     → CountObj / AreaCenter
   Range(diff, null) → (min, max)
⑦ 填端口
   DiffImage / DiffCount / DiffArea / MaxDiffComponentArea / MaxDiffValue
⑧ 判上限（三项独立，任一超限即 NG；0 = 不判）
   MaxTotalArea / MaxComponentArea / MaxValueLimit
   → HasDiff → 任一超限 → Fail("图像差分判定 NG：原因1；原因2")
⑨ 出标注图 + 上屏
   RenderOverlay(refUse, diffRegions) → OverlayImage
   DisplayViewIndex > 0 → PublishPreview(overlay, viewIndex, title, info, null)
```

### 资源释放纪律

四层 `try/finally`，每一层只释放自己 new 出来的东西：

| 层 | 释放 | 不释放（有原因） |
| --- | --- | --- |
| 内层 | `refGray`、`testGray`、`diffRegions` | **`diff`** —— 已交给 `DiffImage` 端口，由 `AutoDisposeRoundOutputs` 下一轮释放；提前 Dispose 会让端口持有野指针 |
| 中层 | —— | `OverlayImage` 同理 |
| 外层 | `referenceAdjusted`、`testAdjusted` | `reference`、`test` —— 上游的图 |
| 隐式 | —— | `Reference` 与 `OverlayImage` 同引用时，基类的透传保护会跳过释放 |

`ImageDiffService` 内部同样逐层 finally：`ConstantLike` 的 `proto`、`ComputeDifference` 的
`posDiff/negDiff/selected`、`Segment` 的 `thresholded/masked/components/filtered/selected`、
`RenderOverlay` 的 `colorBase/composed`。**service 返回的 `HImage` 是新对象，调用方负责释放**。

## 每个决策为什么这么定

### 为什么独立成插件，而不是「预处理」的一个算子

`OperatorStepItem` 只有 `OperatorKey` + `Enabled` + `Dictionary<string,string> Params`。
参数是字符串字典，**结构上装不下第二张图**。而逐像元差分的价值全在"两张对齐的图相比"上，
所以只能做成有两个图像输入端的独立步骤。

顺带说明：`Plugin.PreProcessing` 的算术运算算子组做的是"单图 × 常数"（加/减/乘增益/转 real/归一化）。
谁想在那里找"图像减图像"是找不到的，也不该往那儿加——加了会让算子链的执行语义变成"要两条数据流"，
那是步骤层的事。

### 为什么默认硬失败而不是自动缩放

静默缩放的代价不可见：插值让边缘变软 → 细线缺陷的差异强度被削 → 漏检；
而且从日志上看不出"图被缩过"。本仓 P1-B 的 S1 裁决（卡尺/Blob 加 `Transform` 守卫）
也是同一条思路：**尺寸/坐标系不自洽时宁可红，不要出"看着有数其实错"的值**。

### 为什么分割用 `select_shape` 而不是 `opening`

`opening` 配圆盘结构元会把细长结构整条腐蚀掉。差分最常见的缺陷恰恰是**划痕**（细长），
用 `opening` 等于把要检的东西先删掉再数。`select_shape('area','and',MinArea,∞)` 按面积筛，
形状不受影响。代价是：一个被噪声撕成几团的真实缺陷会数成几片，`MaxComponentArea` 变小——
所以 `Aggregation=Total` 口径下更稳。

### 为什么先 `connection` 再筛，而不是先筛再 `connection`

必须先拆。`connection` 之前统计只能得到"总面积"和"1 个对象"，数不出片数，
`MaxComponentArea` 也就无从谈起。顺序固定：`threshold → [intersect mask] → connection → select_shape`。

### 为什么差异图用 `real` 域

byte 域 `sub_image` 会饱和截断：`0 − 30 → 0`（而不是 −30）。
结果是"变暗的差异全被吃掉"，但图上仍有大片"变化"，看数会觉得"灵敏度不够"而一路抬阈值。
real 域保住负值，才能分别得到 `pos`（变亮）与 `neg`（变暗）两个分量，极性才有意义。

### 为什么标注图只涂 B 通道

`Compose3(R, G, B)` 里 R、G 用原图、B 拉满 → 差异区偏蓝色。若四通道都覆盖或把 R 压掉，
缺陷本身的明暗细节（尤其深色划痕）会被糊掉，看图就分不清"是真缺陷还是涂色太重"。

### 为什么图上不画文字

`HOperatorSet.SetColor` / `DispText` 第一个参数是**窗口句柄**（`HTuple` 窗口号），不是 `HObject` 图像。
想在图对象上写文字必须走离屏烘焙（`OpenWindow('buffer')` + `DumpWindowImage`），
而这在本仓只允许 `Services/Core.HALCON/Helpers/AnnotationBaker.cs` 里做（R36/R37/R39，含文字宽度换算）。
所以本插件不烘焙文字，标题与指标通过 `PublishPreview(image, viewIndex, title, info, annotations)` 交给上屏层。

## 端口发现与配置持久化

- 端口由 `VisionPluginBase.EnsurePortsDiscovered()` 按**公共属性扫一遍**发现的，声明顺序即面板顺序。
  新增端口只要加一个 `public InputPort<T> X { get; } = new(...)` 属性即可，不用改别处。
- `[StepConfig]` 属性同样自动发现，按 `MetadataToken`（＝源码顺序）排。
  `DefaultValue` 用命名参数传 `null` 会编译错，要传 `DataType.Null`。
- 存盘只存参数值（`OnConfirm` 里由基类遍历 `GetConfigProperties()` 写 `SnapshotConfigValue`），不存图。

## 构建与投递

- 工程必须在 `Plugins\` 下（红线①），`Plugins\Directory.Build.targets` 会在 Build 后把 DLL
  投递到 `$(SolutionDir)Modules\`。
- 宿主开着时会锁住 `Modules\Plugin.ImageDiff.dll`，构建在投递步报
  `MSB3027/MSB3021 ... 文件被"VisionMaster (PID)"锁定`。**不要杀进程**，
  加 `-p:ModulesDir=<暂存目录>\` 把投递改道即可（编译本身是过的）。
- 构建统一加 `-m:1 -nodeReuse:false`（本机多进程构建会静默失败：报"生成失败 / 0 个错误"）。
- 改过任何一页 `Help\*.md` 的内容就算产物脏（红线③家族），必须重建本工程，
  并核对 DLL 里的 `.Help.` 内嵌资源数 = 磁盘 `.md` 数。
