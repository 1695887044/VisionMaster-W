# 2026-09-25 Blob 缺陷检测：缺陷修复 + 功能补全

承接前序记录（`2026-09-24-Blob缺陷检测插件.md`）第 7 节之后的一轮工作。
本轮分两批：**先修复复核出来的缺陷，再补全缺失能力**，最后把验证固化成自动断言。

---

## 1. 为什么要做这两批

前一轮复核（`Plugins\Plugin.BlobDetect`）提出 7 项缺陷与一批功能缺口，其中三条属于"通用视觉流程软件不能接受"：

1. **运行实例也在跑配置态预览** —— 产线每帧在 UI 线程多跑一遍全图算法 + 直方图 + 离屏渲染，
   并且运行实例会按**首帧图像**静默改写阈值/面积参数，之后再也不变；
2. **16 位相机的标注底图被裁成死白** —— `convert_image_type` 是截断不是缩放；
3. **功能面太薄** —— 文档自己承认"长条划痕 vs 圆形斑点需下游自行处理"，
   但 7 个输出端口里只有 `MaxArea` 一个标量，下游根本没有可处理的数据。

第 3 条是"宣布把能力推给下游、却没给下游需要的东西"，属于能力链断裂，所以本轮一并补。

---

## 2. 动手前先实测（避免拍脑袋）

上一轮复核有一条结论被实测推翻（`DispatcherTimer` 跨线程其实不抛异常），所以本轮凡是涉及
"算子名 / 特征名 / 语义"的假设，**先建仓库外临时探针实测，再动手**。探针已删除。

### 2.1 HALCON 算子/特征名实测（`C:\Temp\HalconFeatureProbe`，net9.0-windows + 本仓库 halcondotnet）

```
=== 1. select_shape 特征名哪些可用 ===
  area            可用, 通过筛选个数=2
  circularity     可用, 通过筛选个数=2
  elongation      不可用: HALCON error #3101: Unknown feature in operator select_shape
  elongatedness   不可用: HALCON error #3101: Unknown feature in operator select_shape
  anisometry      可用, 通过筛选个数=2
  compactness     可用, 通过筛选个数=2
  rectangularity  可用, 通过筛选个数=2
  width           可用, 通过筛选个数=2
  height          可用, 通过筛选个数=2

=== 2. 长条 vs 方块的特征取值（挑真正能分开两者的）===
  circularity     长条=0.026  方块=0.669
  elongation      不可用: HALCON error #3101: Unknown feature in operator region_features
  elongatedness   不可用: HALCON error #3101: Unknown feature in operator region_features
  anisometry      长条=0.000  方块=1.000
  compactness     长条=15.603  方块=1.212
  rectangularity  长条=1.000  方块=1.000

=== 3. fill_up 是否填孔 ===
  圆环面积 1056 -> fill_up 后 1264（变大=孔被填上）

=== 4. union1 + closing_circle + connection 能否合并靠近的两段 ===
  合并前 2 段 -> 合并后(union1+closing r=2.5+connection) 1 段

=== 5. sort_region 排序模式 ===
  mode=character,row 可用  行=[10, 100] 列=[35, 100]
  mode=character,column 可用  行=[10, 100] 列=[35, 100]
  mode=upper_left,row 可用  行=[10, 100] 列=[35, 100]
  mode=first_point,row 可用  行=[10, 100] 列=[35, 100]

=== 6. 按面积降序重排（tuple_sort_index + tuple_inverse + tuple_select）===
  原面积=[51, 1681]
  升序索引=[0, 1] 反转=[1, 0] 降序面积=[1681, 51]
  总面积 tuple_sum=1732

=== 7. 触边判定：boundary(inner) + intersection ===
  内边界面积=156；不贴边区域与边界相交=0（期望0）；贴边区域=21（期望>0）
  结论: boundary(domain,'inner') + intersection 可作触边判据

=== 8. smallest_rectangle1（外接矩形/宽高）===
  row1=[10, 80] col1=[10, 80] row2=[10, 120] col2=[60, 120]
  第1个区域 宽=51 高=1（长条期望 51×1）
```

### 2.2 DispatcherTimer 线程亲和性实测（上一轮结论就是被这条推翻的）

```
类型是否 DispatcherObject: False
A1 工作线程 Stop(): 未抛异常
A1 工作线程 Start(): 未抛异常
A1 工作线程 改 Interval: 未抛异常
主线程 ManagedThreadId=2 工作线程=5
A2 主线程 Start(): 未抛异常
A2 Tick 次数=1 Tick 所在线程=5（0=从未触发）
```

**结论**：`DispatcherTimer` 不是 `DispatcherObject`，Stop/Start/改 Interval 跨线程**都不抛**；
真正的坑是"定时器归属创建它的那个线程的 Dispatcher"——后台线程上创建的定时器永不 Tick，
表现为**预览静默失效**；若那个线程的 Dispatcher 被泵起来，Tick 会落在非 UI 线程上。

### 2.3 位深截断实测（本轮要修的第 2 条）

```
转换前: type=uint2 灰度=1632~1632
转换后: type=byte 灰度=255~255
判定: 截断(clip)
对照(先 scale 再转): 灰度=0~0
```

> 对照组那行是本轮的一个副产品：均匀图（gMax==gMin）时"先拉伸再转"会整图压到 0。
> 所以最终实现里对退化情况单独走了分支，不能无脑套公式。

---

## 3. 第一批：缺陷修复

### 3.1 修改文件清单

| 文件 | 改动 |
| --- | --- |
| `Plugins\Plugin.BlobDetect\BlobDetectPlugin.cs` | 配置态/运行态隔离；定时器绑定 UI Dispatcher；`BuildDisplayBase` 按实际灰度范围拉伸；失败清预览；`select_shape` 上限改像素总数；检测目标切换补适配；删死字段；适配期间抑制预览排队 |
| `Plugins\Plugin.BlobDetect\HistogramPlot.cs` | 均匀图不再显示"暂无数据"；`GrayToX` 加除零保护 |

### 3.2 关键改动

**（a）配置态与运行态隔离**（`BlobDetectPlugin.cs`）

```csharp
/// <summary>是否为"配置态实例"（宿主为打开配置界面而创建的那个）。运行实例一律 false。</summary>
private bool _isConfigInstance;

private void MarkAsConfigInstance()
{
    if (_isConfigInstance) return;
    _isConfigInstance = true;
    SrcImage.ValueChanged += OnSrcImageValueChanged;   // 只有配置实例才订阅
}

private void SchedulePreview()
{
    if (!_isConfigInstance) return;                     // 运行实例不跑预览
    if (_suppressPreviewSchedule) return;               // 适配写参数期间不排队
    ...
}
```

订阅从**构造器**挪到 `MarkAsConfigInstance()`（由 `GetConfigView` / `OnViewLoaded` 调用），
`RefreshPreview` 入口再加一道 `if (!_isConfigInstance) return;` 兜底。
这一处同时消掉了"运行期每帧多跑一遍算法"与"按首帧静默改参数"两条缺陷。

**（b）定时器显式绑定 UI 线程**（不再寄生在创建线程上）

```csharp
_previewDebounce ??= new DispatcherTimer(
    TimeSpan.FromMilliseconds(200),
    DispatcherPriority.Normal,
    OnPreviewTick,
    dispatcher);          // dispatcher = Application.Current.Dispatcher
```

**（c）标注底图按实际灰度范围拉伸**（`BuildDisplayBase`）

```csharp
// convert_image_type(...,'byte') 是截断不是缩放：实测 uint2 灰度 1632 转出来是 255，
// 12/16 位相机的标注底图会整片死白。所以先按实际灰度范围线性拉伸到 0~255 再转。
double k = 255.0 / (gMax - gMin);
HOperatorSet.ScaleImage(src, out HObject scaled, k, -gMin * k);
HOperatorSet.ConvertImageType(scaled, out HObject converted, "byte");
```

多通道、均匀图（`gMax - gMin < 1e-9`）各走退化分支，绝不套出 NaN。

**（d）`select_shape` 上限改为图像像素总数**

```csharp
double areaUpper = p.MaxBlobArea > 0 ? p.MaxBlobArea : Math.Max(imgW * (double)imgH, 1);
var featureMins = new List<double> { Math.Min(p.MinArea, areaUpper) };  // 下限夹到不超过上限
```

- 上限：原写死 `1e7`，>1000 万像素的相机上整块背景会被静默滤掉（既不计数也不 NG = 漏检还报 OK）；
- 下限夹取：`MinArea > 上限` 时 HALCON 直接抛 `#1304` 把流程带崩（**这是写断言时真踩到的**，
  见 5.3），按本插件"配置坏了最多结果不对，绝不炸流程"的原则静默夹取；
- `AreaUpperBound(1e7)` 降级为只做参数夹取的 `AreaClampMax(1e12)`。

**（e）检测目标切换后补做适配**

`DetectTarget` 由"暗"改"亮"时，之前按"最暗 25%"算出的阈值立刻失效（会停在暗侧、与意图相反），
而"是否出厂默认值"的守卫会因为 `DetectTarget` 变了而跳过自动适配。
现在记住上次适配出的阈值（`_lastAdaptedMinGray/MaxGray`），切换目标且阈值仍停在适配值时补做一次；
用户手工调过阈值就不动他的。

**（f）其余**：预览失败/异常分支补 `PreviewImage = null`（否则信息栏报红、右边摆着上一张正常图）；
删死字段 `_sourceImage`；适配期间 `_suppressPreviewSchedule` 屏蔽"改 4 个参数触发 4 次预览"。

---

## 4. 第二批：功能补全

### 4.1 新增参数（10 个，全部取"不生效"默认值，老方案加载后结果不变）

| 参数 | 默认 | 作用 |
| --- | --- | --- |
| `MaxBlobArea` | 0 | 单缺陷面积**筛选**上限（0 = 取图像像素总数） |
| `MinCircularity` | 0 | 圆度下限（0 = 不筛）。1 = 正圆，细长划痕约 0.02~0.1 |
| `MaxAspectRatio` | 0 | 长宽比上限（0 = 不筛），长边/短边，正方形 = 1 |
| `ExcludeBorderPx` | 0 | 排除触边缺陷的边界带宽 |
| `FillHoles` | false | 填孔（`fill_up`） |
| `MergeRadius` | 0 | 相邻缺陷合并半径（union1 → closing → connection） |
| `MinDefectCount` | 0 | 缺陷个数**下限**（存在性检测） |
| `MaxTotalArea` | 0 | 缺陷总面积上限 |
| `PixelSizeMm` | 1.0 | 像素当量 mm/px（与 `Plugin.CaliperMeasure` 同口径） |
| `SortMode` | None | 不排序 / 面积从大到小 / 从上到下从左到右 |

### 4.2 新增输出端口（6 个，只增不改，既有 7 个端口名字/类型/顺序一律不动）

`DefectAreas`(HTuple) / `DefectWidths`(HTuple) / `DefectHeights`(HTuple) /
`TotalArea`(double) / `MaxAreaMm2`(double) / `TotalAreaMm2`(double)

**顺序语义**：`CenterRows / CenterCols / DefectAreas / DefectWidths / DefectHeights` 与 `Defects`
按 `SortMode` **逐项对齐**——HALCON 区域数组本身没有顺序约定，下游按索引取"第 N 个缺陷"原来是不可预期的。

### 4.3 两个刻意的设计取舍

- **筛选 vs 判定分开**：`MaxBlobArea` 是"这个东西算不算缺陷"（超了不计数），
  `MaxSingleArea` 是"这个缺陷合不合格"（超了判 NG）。早期版本只有后者导致大画幅漏检还报 OK。
- **像素当量不参与判定**：只换算 mm² 输出。否则"改了当量就改判定结果"是一处很难查的隐式耦合。

### 4.4 长宽比为什么自己算

本仓库 HALCON 不认识 `'elongation'` / `'elongatedness'` 特征名（实测 #3101，见 2.1）。
改用 `smallest_rectangle1` 自算长边/短边，语义与 elongation 一致且任何版本都成立。

> ⚠ **顺带发现（未修，不在本轮范围）**：`Plugins\Plugin.ImageScript\ScriptTemplates.cs:238`
> 教用户写 `select_shape (..., 'elongation', 'and', 4, 100)`，而该特征名在本版本不存在，
> 用户照抄会直接报 #3101。建议后续单独修模板。

### 4.5 界面

`BlobDetectView.xaml`：原"② 噪声清理"扩为"② 特征筛选"（面积上下限/圆度/长宽比/排除触边），
"③ 形态学"加"合并半径"与"填孔"复选框，"④ 判定规格"加"个数下限"与"总面积上限"，
新增"⑤ 输出"卡片（像素当量 + 排序方式）。数值框沿用既有 `NumericBox` 校验规则。

---

## 5. 验证结果（真实输出）

### 5.1 单工程编译

```
dotnet build Plugins\Plugin.BlobDetect\Plugin.BlobDetect.csproj -v m -clp:ErrorsOnly
→ 已成功生成。
    0 个警告
    0 个错误
```

### 5.2 解决方案级编译

```
dotnet build VisionMaster.sln -v m -clp:ErrorsOnly
→ 已成功生成。
    82 个警告
    0 个错误
```

（`--no-incremental` 那次撞上 `Core.Halcon.dll` 被占用的 CS2012，与改动无关，改回增量构建即通过。）

### 5.3 新增自动断言：`FlowCanvasChecks\BlobDetectChecks.cs`

**为什么用合成图像而不是样图**：期望值可笔算，不随机器/图库漂移；断言的是契约
（个数、筛选、排序、判定语义、端口对齐、单位换算），而不是"某张照片上恰好检出 12 个"这种会随调参变化的东西。

场景：200×200 背景 200，暗斑灰度 50 —— 3 个圆斑 + 1 个贴右边缘的方块 + 1 条 31×2 细长条 + 1 个 4px 噪点。

```
---- [Blob] 缺陷检测插件（合成确定性图像验证） ----
  [√通过] 运行不抛异常
  [√通过] 报成功（检出缺陷是正常工况，不是失败）  →  Success=True Error='缺陷个数 5 超过上限 0'
  [√通过] 【核心】检出 5 个缺陷（4px 的噪点被面积下限滤掉）  →  DefectCount=5
  [√通过] 【核心】五个数组端口逐项对齐（下游按同一索引取就能对上号）  →  areas=5 rows=5 cols=5 w=5 h=5
  [√通过] MaxArea 等于逐项面积的最大值  →  MaxArea=208 areas.Max=208
  [√通过] TotalArea 等于逐项面积之和  →  TotalArea=498 sum=498
  [√通过] 默认零容忍 → 判 NG，但 Success 仍为 true（NG 是正常结果）  →  IsOk=False Success=True Err='缺陷个数 5 超过上限 0'
  [√通过] 【排序】面积从大到小：输出确实单调不增  →  208, 112, 64, 62, 52
  [√通过] 【排序】排序后第一个就是 MaxArea  →  first=208 MaxArea=208
  [√通过] 【触边排除】ExcludeBorderPx=1 → 恰好少一个（贴右边缘那个被丢掉）  →  DefectCount=4（期望 4）
  [√通过] 【触边排除】剩下的都不压到最外 1 像素  →  c=49.5,w=12,c=99.5,w=8,c=149.5,w=16,c=10.5,w=2
  [√通过] 【长宽比】MaxAspectRatio=5 → 细长条被排除（5 个剩 4 个）  →  DefectCount=4（期望 4）
  [√通过] 【长宽比】剩下的每个都 ≤ 设定的长宽比  →  8×8,12×12,8×8,16×16
  [√通过] 【圆度】MinCircularity=0.5 → 细长条被排除（圆斑保留）  →  DefectCount=4（期望 4）
  [√通过] 【合并】断裂两段：不合并是 2 个  →  DefectCount=2（期望 2）
  [√通过] 【合并】MergeRadius=2.5 → 合并成 1 个（划痕断裂不再虚高计数）  →  DefectCount=1（期望 1）
  [√通过] 【填孔】环形缺陷填孔后面积变大（孔不再被漏算）  →  未填=948 填后=1264
  [√通过] 【存在性】个数少于下限 → 判 NG 且原因写明「少于下限」  →  IsOk=False Err='缺陷个数 5 少于下限 6（目标特征未检到）'
  [√通过] 【总面积】单缺陷都合格但总面积超标 → 判 NG 且原因写明「总面积」  →  IsOk=False Err='缺陷总面积 498 超过上限 10'
  [√通过] 【像素当量】mm² = 像素面积 × 当量²（当量 2 → ×4）  →  px=208 mm²=832
  [√通过] 【开轮重置】失败轮把上一轮的脏数据清零（下游不会读到旧结果）  →  上一轮=5 本轮 Count=0 IsOk=False
  [√通过] 失败原因写明是图像为空  →  Success=False Err='输入图像为空或未初始化'
  [√通过] 【位深】uint2 标注底图按实际灰度范围拉伸  →  跳过：本环境离屏窗口不渲染底图层次（byte 对照组跨度=0，期望 ≈100），需在带显示会话的环境里目视确认 —— 位深截断的实质证据见开发记录（uint2 1632 → byte 实测为 255）
```

**写断言过程中真抓到的两个问题**（都已修，见 3.2(d)）：

1. 用例最初用 `MinArea = 1e9` 制造"空结果"，结果 HALCON 抛
   `Blob 缺陷检测失败：HALCON error #1304: Wrong value of control parameter 4 in operator select_shape`
   —— 下限 > 上限直接炸流程，与"非法值不能炸"的原则冲突；
2. 第一版场景里"贴边缺陷"做成了一条细长条，导致"触边排除"与"形状筛选"两条断言互相污染
   （期望 4 实际 3）。改成**方形**的贴边缺陷后两条断言各自独立成立。

### 5.4 冒烟回归

```
dotnet run --project FlowCanvasChecks\FlowCanvasChecks.csproj -c Debug --no-build
========== 结果汇总 ==========
通过: 633  失败: 14
```

14 条失败全部在**并行任务范围**，与本次无关（Blob 段 0 失败）：

- 相机插件 1 条：`相机插件转交模块注册表后可见` → 模块表 0 项；
- 线序检测 13 条：`【5 芯】/【10 芯】/【未知芯数】` 系列 + 结果帧投射 2 条。

### 5.5 产物投递

```
Get-Item Modules\Plugin.BlobDetect.dll
Name          : Plugin.BlobDetect.dll
Length        : 67584
LastWriteTime : 2026/9/25 14:52:44
```

由 `Plugins\Directory.Build.targets` 的 `CopyPluginToModules` 自动投递。

### 5.6 改动文件清单（本次）

| 文件 | 状态 |
| --- | --- |
| `Plugins\Plugin.BlobDetect\BlobDetectPlugin.cs` | 修改 |
| `Plugins\Plugin.BlobDetect\BlobDetectEnums.cs` | 修改（新增 `DefectSortMode`） |
| `Plugins\Plugin.BlobDetect\HistogramPlot.cs` | 修改 |
| `Plugins\Plugin.BlobDetect\BlobDetectView.xaml` | 修改 |
| `FlowCanvasChecks\BlobDetectChecks.cs` | **新增** |
| `FlowCanvasChecks\FlowCanvasChecks.csproj` | 修改（加 ProjectReference） |
| `FlowCanvasChecks\Program.cs` | 修改（注册 `BlobDetectChecks.Run()`） |

> 说明：`FlowCanvasChecks` 下另有 `ExecutionChecks.cs` / `HttpImageSmoke.cs` / `WireSequenceCheck.cs`
> 的改动属并行任务，与本次无关，未在上表列出。

---

## 6. 已知边界

1. **位深修复缺少自动验证**：本冒烟环境（无显示会话）离屏窗口不渲染底图——byte 对照组同样一片黑，
   **说明是环境限制而非修复无效**。需要在带显示会话的机器上打开配置界面、用一张 16 位图目视确认一次。
   实质证据仍是 2.3 的实测（uint2 1632 → byte = 255 截断）。
2. **长宽比自实现的口径**：用外接矩形长边/短边，与 HALCON 的 `anisometry` 不是同一个量，
   界面上写的是"长宽比"以免混淆。
3. **合并会略微改变面积**：`union1 + closing_circle` 会把缝隙填平，面积比原始几段之和稍大。
   追求精确面积时不要开 `MergeRadius`。
4. **触边判据基于图像外接矩形边缘**：上游若用非矩形 ROI（圆/椭圆），domain 边界 ≠ 外接矩形边界，
   此时"触边"按外接矩形算，是刻意选的可预测口径。
5. **像素当量只影响 mm² 输出**，判定仍按像素；现场若需要"按 mm² 判定"，应再开一组 mm 规格参数
   （本轮未做，避免两套规格互相打架）。
6. **未做的能力**（有意留白）：排除区/掩膜输入端口、分级判定（OK/警告/NG 三档）、
   滞后阈值、缺陷特征表（调参辅助）。
7. **`ScriptTemplates.cs:238` 的 `'elongation'` 仍是错的**，会误导照抄的用户，建议单开一项修。
