# Plugin.DeformableMatching 设计文档

> 状态：**M1/M2 已实现并通过 HALCON 对拍（2026-10-27）**；模板支持方案资产/本地文件/内嵌三来源；
> 全量 `run_gates.ps1` 14 步绿；M0 四条探针结论见 §3。
> 实施记录：`docs\code-changes\2026-10-26-可形变匹配插件A方案实施.md`。
> 环境：HALCON 23.05 Progress（`C:\Users\16958\AppData\Local\Programs\MVTec\HALCON-23.05-Progress`），
> `halcondotnet.dll` 来自 `DLL\Halcon\`；`HALCONROOT` / `HALCONARCH` 已设置，HALCON `bin\x64-win64` 在 PATH。
> 证据：`artifacts\logs\20261026\deformable-probe.log`；
> 探针源码归档：`artifacts\logs\20261026\deformable-probe\`（一次性工程，按 R166 已从 `tools\oneoff\` 删除）。
> 关联任务书：`docs\code-changes\2026-10-26-计划-可形变匹配插件A方案.md`。

---

## 1. 目标与范围

新增 `Plugin.DeformableMatching`，一个插件两种算法：

- `LocalDeformable`：HALCON `create_local_deformable_model` / `find_local_deformable_model`；
- `PlanarUncalibDeformable`：`create_planar_uncalib_deformable_model` / `find_planar_uncalib_deformable_model`。

v1 范围：

- 单模板（一个步骤一个模板）：参考图 + ROI + 掩膜 + 学习/重学 + 模型随方案落盘。
- 公共输出：`MatchCount / Score / Row / Column / Scores / Rows / Columns / MeasureImage`；
- 局部专有：`DeformedContours`、`RectifiedImages`；
- 平面专有：`HomMat2D`、`DeformedContours`（投影生成）。

v1 明确不做：

- 多模板/配方库（升级路径：`List<DeformableTemplateEntry>` + `TemplateLibraryJson`）；
- `PlanarCalibDeformableModel`（需要相机标定）；
- `max_deformation`（现有 shape model 的参数，另一条线）；
- 实时性优化/并行调参；只保证结果正确与可用。

---

## 2. 为什么是新插件（设计决策）

**A 方案本身已经是"一个插件装两种算法"**（Local + Planar 共用 `Plugin.DeformableMatching`）。
不并进 `Plugin.Matching` 的原因是端口契约，不是 UI：

| 现有 `MatchingPlugin` 端口 | 局部形变 | 平面透视 |
| --- | --- | --- |
| `Angle` / `Angles` | HALCON 不返回 | 透视下角度语义不唯一 |
| `Scales` | HALCON 不返回 | 只能从 HomMat2D 反推 |
| `AlignedImage`（刚性位姿归一化） | 语义不同（局部校正图） | 不适用 |
| — | 需要 `DeformedContours` | 需要 `HomMat2D` |

并进 `Plugin.Matching` 只有两条路：① 让 `Angle/Scales` 在新算法下静默为空/0 —— 下游已连的步骤
会拿到假值；② 改现有端口语义 —— 直接打破存量 `.vms` 与 3745 条断言。两者都不可接受。

反射属性面板（`FlatPropertyGrid` + `[PropertyItem]`/`[SuperDisplay]`，R106/R107 的
"切换字段 + 嵌套子对象 + `RequireRefresh`"）只能生成标量参数控件，生成不了参考图预览、
ROI 画布、涂抹掩膜、学习按钮、结果表；学习工作流仍需要 `IPluginCustomViewProvider` +
`DeformableMatchingView.xaml`。**而且这个视图本身要先走 Web 设计基线（§4.10），不能直接照
`MatchingView.xaml` 写。** `RequireRefresh` 是配置模型的多态方案，不是运行期算法切换，
创建期/查找期参数、模型过期、模型类型校验这些逻辑一行都省不掉。

另外：`MatchingTemplateEntry` 已作为 JSON 落在 `.vms` 里并带旧单模板迁移字段，再加两种模型
要做迁移与回归；`EnsureModelLoaded`/`ReleaseEntryModel` 已按 Shape/NCC 分支，再加两种是四路分支；
`MatchingPlugin` 已有 3745 条基线断言和 L 档手册。独立插件是干净 schema、独立对拍、零存量风险。

---

## 3. M0 探针结论（实测）

### 3.1 探针方法

- 合成 400×400 黑底图 + 100×100 棋盘格（10×10 方格），ROI `GenRectangle1(145,145,254,254)`，
  中心 `(199.5,199.5)`。
- 模型创建：`NumLevels='auto'`，创建期 `AngleStart/AngleExtent/ScaleRMax/ScaleCMax=[]`，
  `ScaleRMin=ScaleCMin=1`，`Optimization='none'`，`Metric='use_polarity'`，
  `Contrast/MinContrast='auto'`。
- 查找：`MinScore=0.3`，`NumMatches=1/2`，`MaxOverlap=0.5`，`NumLevels=0`，`Greediness=0.9`；
  局部 `deformation_smoothness=11, expand_border=0, subpixel=1`。
- 同一进程内直接调 HALCON，不经过插件/宿主。

### 3.2 P1：`SetDeformableModelOrigin` 是相对域重心的偏移

| 设置 | `GetDeformableModelOrigin` | 查找 `Row/Col` |
| --- | --- | --- |
| `(0,0)` | `(0,0)` | `(199.5, 199.5)` = ROI 中心 |
| `(50,30)` | `(50,30)` | `(249.499, 229.5)` = ROI 中心 + 偏移 |

结论：

- **参数是相对模板域重心的偏移，不是图像绝对坐标**；`(0,0)` 表示"重心就是原点"，
  `(50,30)` 表示原点在重心右下 `(50,30)`。
- 对称 ROI（矩形/圆/椭圆）的重心 = ROI 中心 → 传 `(0,0)` 即可。
- 非对称掩膜：`origin = roiCenter - cog`，`cog` 用 `GetDomain` + `AreaCenter` 求。
- **设计定稿**：学习时先算 COG，再
  `SetDeformableModelOrigin(modelId, roiCenterRow - cogRow, roiCenterCol - cogCol)`；
  **不要**照抄 `set_shape_model_origin` 的绝对坐标口径。

### 3.3 P2：`ImageRectified` 是每个命中一张的 image array

`NumMatches=2` 的实测：

- `scores.Length=2`，`rows.Length=2`，`cols.Length=2`；
- `rectified.CountObj=2`；
- `contours.CountObj=972`（每个命中 486 段）；
- `rows=[349.5, 149.5]`，`cols=[399.5, 149.5]`（第二个实例排在前面，两个分数相同）。

结论：

- **`ImageRectified` 是 `image(-array)`，每个命中一张**；端口用
  `OutputPort<HObject> RectifiedImages`。不要用 `OutputPort<HImage>` 装多命中。
- **命中顺序不保证按分数降序**；`Score/Row/Column` 标量端口必须用
  `TupleMax` + `TupleFind` 取最高分索引，不能假设 `index 0` 是最高分。
- **`DeformedContours` 是扁平 XLD 数组，不是"每个命中一个对象"**：
  `MatchCount=2` 时 `CountObj=972`。端口描述/帮助必须写清；下游不能拿 `CountObj` 当命中数。
  v1 只负责原样输出/绘制，不做按命中分组。

### 3.4 P3：`GetDeformableModelContours` 的 level 是 1 基

`NumLevels='auto'` 的 100×100 模板：`num_levels=[3]`（local/planar 相同）。

| level | 结果 |
| --- | --- |
| 0 | FAIL `HALCON error #1302: Wrong value of control parameter 2` |
| 1 | OK，486 个轮廓 |
| 2 | OK，486 个轮廓 |
| 3 | OK，229 个轮廓 |
| 4 | FAIL `#1302` |

结论：

- **合法范围是 `1..num_levels`（1 基），`0` 非法**。固定用 level 1（最细层）最稳；
  **不要"修"成 0**。
- `GetDeformableModelParams(modelId, 'num_levels')` 返回 Int64 元组；C# 用 `levels.LArr[0]`，
  `IArr[0]` 会抛 `HTupleAccessException`。
- 平面模式同样 1 基；M2 的 `DeformedContours` 固定用 level 1。

### 3.5 P4：`HomMat2D` 以当前参考点为 `(0,0)`

模板 ROI 中心 `(199.5,199.5)`，搜索图图案中心 `(249.5,229.5)`（平移 `+50,+30`）。

| 设置 | `HomMat2D` 平移项 | `project(0,0)` | `project(ROI 中心)` |
| --- | --- | --- | --- |
| `(0,0)` | `(249.501, 229.501)` | `(249.5, 229.5)` | `(448.999, 428.999)` |
| `(50,30)` | `(299.501, 259.501)` | `(299.5, 259.5)` | `(498.998, 458.998)` |

结论：

- **`HomMat2D` 把"当前参考点为 `(0,0)`"的模型坐标映射到搜索图**；投影 `(0,0)` 得到参考点
  在搜索图中的位置。
- 设原点为 ROI 中心后，`Row/Column = ProjectiveTransPixel(hom, 0, 0)`；
  **不要投影绝对 ROI 中心**。
- 若不设原点（参考点 = 重心），投影 `(roiCenter - cog)` 才得到 ROI 中心。
- `ProjectiveTransPixel` 内部做齐次除法，优先用它；手写 `ProjectiveTransPoint2d` 时防 `qw≈0`。

### 3.6 探针附带确认

- 合成棋盘格（100×100，10×10 方格）足以让 local/planar 两种模型在 `MinScore=0.3` 下命中
  （local score `0.9993`，planar score `0.9999987`）。M1 素材可以走这条合成路线，
  不必依赖本机未安装的 HALCON Matching 示例。
- 创建期 angle/scale 上限传 `[]`、查找期传实际值，探针全程无异常 → 文档 "not used" 的口径可照用。
- `SetDeformableModelOrigin` 偏移 `(50,30)` 后仍能命中，`Row/Col` 按偏移移动 →
  偏移不影响搜索，只影响参考点。

---

## 4. 设计定稿

### 4.1 工程与类

- 工程：`Plugins\Plugin.DeformableMatching\Plugin.DeformableMatching.csproj`（红线①）；
  引用集照 `Plugin.BlobDetect.csproj`：`Core.Halcon` / `Core.Controls` / `Core.Interfaces` / `UI.Controls`
  + `halcondotnet`（HintPath `..\..\DLL\Halcon\halcondotnet.dll`）+ `Help\**\*` 内嵌资源。
- 插件类：

  ```csharp
  [Display(Name = "可形变模板匹配", GroupName = "定位",
           Description = "局部非刚性 / 平面透视形变下的模板定位",
           ShortName = "\uf140")]
  public partial class DeformableMatchingPlugin : VisionPluginBase, IPluginCustomViewProvider
  ```

- `[ParallelSafe]` **先不加**：等确认模型缓存是实例级、且没有共享静态状态后再加。
- `[StepConfig]`：需要通知/钩子的用 partial 属性（必须配 `[DefaultValue]`，否则生成器落 `default`，
  `StepConfigGenerator.cs:267`）；纯配置可用自动属性（初始化器生效，但不自动发 `PropertyChanged`）。

### 4.2 模板条目与持久化

`DeformableTemplateEntry`（普通类，实现 INPC；**不引用 `Plugin.Matching`**）：

```csharp
public sealed class DeformableTemplateEntry : INotifyPropertyChanged
{
    public string Name { get; set; } = "模板1";
    public string RefImagePath { get; set; } = "";       // 学习用参考图（绝对路径；只有重学才需要）
    public string Shape { get; set; } = RoiShapeNames.Rectangle; // Rectangle / Circle / Ellipse
    public double[] TemplateRect { get; set; } = [];     // [row, col, phiRad, half1, half2]
    public string TemplateMask { get; set; } = "";       // 掩膜 PNG base64，可空
    public string Model { get; set; } = "";              // 形变模型 base64
    public DeformableAlgorithm Algorithm { get; set; } = DeformableAlgorithm.LocalDeformable;
    public DeformableAlgorithm LearnedAlgorithm { get; set; } = DeformableAlgorithm.LocalDeformable;
    public string LearnedSignature { get; set; } = "";   // 学习时的创建期参数指纹
    public int NumLevels { get; set; } = 0;              // 创建期 0 → 'auto'；查找期 0 合法（默认）
    public double AngleStartDeg { get; set; } = -30;     // 查找期参数（创建期 HALCON 不读）
    public double AngleExtentDeg { get; set; } = 60;     // 查找期参数
    public double ScaleRMin { get; set; } = 1;           // 创建期参数（最小尺度）
    public double ScaleRMax { get; set; } = 1;           // 查找期参数（创建期不读）
    public double ScaleCMin { get; set; } = 1;           // 创建期参数
    public double ScaleCMax { get; set; } = 1;           // 查找期参数（创建期不读）
    public double Smoothness { get; set; } = 11;         // 查找期参数；HALCON 典型 11，最小 3
    public bool UsePolarity { get; set; } = true;        // 创建期参数
    [JsonIgnore] public HTuple? RuntimeModelId;          // 运行期模型句柄（不落盘）
    [JsonIgnore] public string? RuntimeModelSource;      // 句柄对应的 Model 载荷（缓存键）
    [JsonIgnore] public HObject? RuntimeModelContours;   // 平面模式：模型轮廓缓存（随句柄释放）
}
```

- 落盘：整体序列化成一个 `TemplateJson` 字符串，照 `MatchingPlugin.TemplateLibraryJson`：
  `[StepConfig] public string TemplateJson { get; set; } = "{}";`，
  `OnConfirm` 里 `JsonConvert.SerializeObject(entry)`。**不要**把 14 个字段直接摊成插件级
  `[StepConfig]`：属性面板会出现模型内部字段，v2 升多模板还要改 schema。
- **创建期 vs 查找期**：
  - 创建期（改了就重学）：`Algorithm` / `Shape` / `TemplateRect` / `TemplateMask` / `NumLevels` /
    `UsePolarity` / `ScaleRMin` / `ScaleCMin`。
  - 查找期（改了不重学）：`Smoothness` / `MinScore` / `MaxMatches` / `MaxOverlap` / `Greediness` /
    `AngleStartDeg` / `AngleExtentDeg` / `ScaleRMax` / `ScaleCMax`。
  - HALCON 23.05 文档：`create_local_deformable_model` 与
    `create_planar_uncalib_deformable_model` 的 `AngleStart` / `AngleExtent` / `ScaleRMax` /
    `ScaleCMax` **not used**；创建时一律 `[]`，只在 `find_*` 传实际值。
- 模型类型校验：学习时写 `LearnedAlgorithm = Algorithm`；运行期先比 `LearnedAlgorithm` 与当前
  `Algorithm`，不一致就 `Fail("模板是用「X」学的，当前算法是「Y」：请重新学习")`。
  只存一个 `Algorithm` 字段校验不了——用户切换算法时那个字段已经变成新值了。
- 过期判定：`LearnedSignature` 存创建期参数指纹
  （`Algorithm|Shape|Rect|Mask.Length|NumLevels|UsePolarity|ScaleRMin|ScaleCMin`）；
  查找期参数不进指纹。`IsModelStale` 只比这个指纹，照 `MatchingPlugin.cs:1265`。

### 4.3 端口契约

```csharp
public InputPort<HImage> Image { get; } =
    new("Image", null!, "待搜索图像") { IsRequired = false };
public InputPort<HRegion> SearchRegion { get; } =
    new("SearchRegion", null!, "搜索区域（可选，未连=全图）") { IsRequired = false };

public OutputPort<int> MatchCount { get; } = new("MatchCount", "命中实例数");
public OutputPort<double> Score { get; } = new("Score", "最高分实例分数（0~1）");
public OutputPort<double> Row { get; } = new("Row", "最高分实例中心行（亚像素）");
public OutputPort<double> Column { get; } = new("Column", "最高分实例中心列（亚像素）");
public OutputPort<HTuple> Scores { get; } = new("Scores", "各实例分数数组");
public OutputPort<HTuple> Rows { get; } = new("Rows", "各实例中心行数组");
public OutputPort<HTuple> Columns { get; } = new("Columns", "各实例中心列数组");
public OutputPort<HXLD> DeformedContours { get; } = new("DeformedContours",
    "各实例形变后的轮廓（扁平 XLD 数组，每命中多段；CountObj ≠ MatchCount）");
public OutputPort<HTuple> HomMat2D { get; } = new("HomMat2D",
    "平面模式：每命中 9 个 double 的单应矩阵；局部模式为空");
public OutputPort<HObject> RectifiedImages { get; } = new("RectifiedImages",
    "局部模式：每个命中的校正图（image array）；平面模式为空");
public OutputPort<HImage> MeasureImage { get; } = new("MeasureImage",
    "标注图（轮廓 + 分数 + 判定文字；无命中也要出）");
```

- 两个输入端口必须 `IsRequired = false`：`InputPort.IsRequired` 默认 `true`
  （`InputPort.cs:27`），不写就会被流程编译器判成必填；`Image` 也一样，否则"上游无图 → Fail"
  永远不会执行。
- `RectifiedImages` 必须是 `HObject`：HALCON `ImageRectified` 类型是
  `(multichannel-)image(-array)`，每个命中一张；`OutputPort<HImage>` 装不下多张。
- `DeformedContours` 用 `new HXLDCont(deformedContours)`（照 `BeadInspectPlugin.cs:487`）；
  端口声明 `OutputPort<HXLD>`。
- `Score/Row/Column` = 最高分实例：用 `TupleMax` + `TupleFind` 取索引，不假设 HALCON 按分数降序
  （P2 实测 `rows=[349.5,149.5]`）。
- 不声明 `Angle` / `Scales`：局部模式 HALCON 不返回，平面模式要反推且透视下角度语义不唯一。
- 补 `[StepConfig] DisplayViewIndex`（1~9，0=不发布），照 `MatchingPlugin.cs:187-192`。

### 4.4 学习（模型创建）

```csharp
// 1) 读参考图 → 灰度（照抄 BlobDetect 的 TryToGrayImage）
HOperatorSet.ReadImage(out HObject img, entry.RefImagePath);
// 2) 按 entry.Shape 生成 ROI：GenRectangle2 / GenCircle / GenEllipse
HOperatorSet.ReduceDomain(gray, roi, out HObject reduced);
// 3) 掩膜（可选）：TemplateMask 解成 HRegion，Difference(reduced, mask, reduced2)
// 4) 建模型（创建期参数；AngleStart/AngleExtent/ScaleRMax/ScaleCMax 传 []）
var createNumLevels = entry.NumLevels <= 0 ? new HTuple("auto") : new HTuple(entry.NumLevels);
if (Local)
    HOperatorSet.CreateLocalDeformableModel(reduced, createNumLevels,
        new HTuple(), new HTuple(), "auto",
        entry.ScaleRMin, new HTuple(), "auto",
        entry.ScaleCMin, new HTuple(), "auto",
        "none", entry.UsePolarity ? "use_polarity" : "ignore_local_polarity",
        "auto", "auto", new HTuple(), new HTuple(), out modelId);
else
    HOperatorSet.CreatePlanarUncalibDeformableModel(reduced, createNumLevels,
        new HTuple(), new HTuple(), "auto",
        entry.ScaleRMin, new HTuple(), "auto",
        entry.ScaleCMin, new HTuple(), "auto",
        "none", entry.UsePolarity ? "use_polarity" : "ignore_local_polarity",
        "auto", "auto", new HTuple(), new HTuple(), out modelId);
// 5) 原点：相对域重心的偏移，不是图像绝对坐标
HOperatorSet.GetDomain(reduced, out HObject domain);
HOperatorSet.AreaCenter(domain, out HTuple area, out HTuple cogRow, out HTuple cogCol);
double originRow = roiCenterRow - cogRow.D;
double originCol = roiCenterCol - cogCol.D;
HOperatorSet.SetDeformableModelOrigin(modelId, originRow, originCol);
// 6) 落盘 + 指纹 + 清临时句柄
HOperatorSet.WriteDeformableModel(modelId, tempFile);
entry.Model = Convert.ToBase64String(File.ReadAllBytes(tempFile));
entry.LearnedAlgorithm = entry.Algorithm;
entry.LearnedSignature = EntrySignature(entry);
HOperatorSet.ClearDeformableModel(modelId);
```

- `roiCenterRow/roiCenterCol` 来自 `entry.TemplateRect[0]/[1]`。
- 矩形/圆/椭圆的重心 = ROI 中心 → `SetDeformableModelOrigin(modelId, 0, 0)` 即可；
  只有非对称掩膜需要 `roiCenter - cog`。
- 学习/重学/`Dispose` 都要 `ClearDeformableModel`；学习临时模型写完立即清掉。
- 所有 HObject 临时对象（`ReadImage` / `ReduceDomain` / `Rgb*ToGray` / `GetDomain`）要 `Dispose`。

### 4.5 查找

**局部非刚性**：

```csharp
HOperatorSet.FindLocalDeformableModel(
    gray, out HObject rectified, out HObject vectorField, out HObject deformedContours,
    modelId,
    angleStartRad, angleExtentRad,
    entry.ScaleRMin, entry.ScaleRMax, entry.ScaleCMin, entry.ScaleCMax,
    minScore, maxMatches, maxOverlap, entry.NumLevels, greediness,
    new HTuple("deformed_contours", "image_rectified"),
    new HTuple("deformation_smoothness", "expand_border", "subpixel"),
    new HTuple(entry.Smoothness, 0, 1),
    out HTuple score, out HTuple row, out HTuple col);
```

- 查找期 `NumLevels` 是 **int 0..10**（0=默认），不能传创建期的 `'auto'`。
- `Score/Row/Column` 标量取最高分实例（`TupleMax` + 索引）；数组保持 HALCON 顺序。
- `DeformedContours` = `deformedContours`（扁平 XLD 数组，每命中 486 段——P2 实测；
  `new HXLDCont(...)`）。
- `RectifiedImages` = `rectified`（**image array，每个命中一张**；P2 实测 `CountObj=2`；
  `OutputPort<HObject>`）。
- `vectorField` 不需要就 `Dispose`。
- `ImageRectified` 尺寸是模板域的轴对齐包围盒（`expand_border=0` 时紧贴），不是旋转 ROI。

**平面透视**：

```csharp
HOperatorSet.FindPlanarUncalibDeformableModel(
    gray, modelId,
    angleStartRad, angleExtentRad,
    entry.ScaleRMin, entry.ScaleRMax, entry.ScaleCMin, entry.ScaleCMax,
    minScore, maxMatches, maxOverlap, entry.NumLevels, greediness,
    new HTuple(), new HTuple(),
    out HTuple homMat2D, out HTuple score);
```

- `homMat2D` 长度 = `9 * MatchCount`；原样写入 `HomMat2D` 端口。
- 每个命中取 `hom = homMat2D.TupleSelectRange(i*9, i*9+8)`；用参考点 `(0,0)` 投影：

  ```csharp
  HOperatorSet.ProjectiveTransPixel(hom, new HTuple(0.0), new HTuple(0.0),
      out HTuple row, out HTuple col);
  ```

  手写 `ProjectiveTransPoint2d` 时 `row=qx/qw`、`col=qy/qw`，`qw≈0` 明确失败。
- **原点语义**：设成 ROI 中心后，`project(0,0)` 就是 ROI 中心（P4 实测）；
  不设原点时参考点是重心，要投影 `(roiCenter - cog)`。
- `DeformedContours`：**模型加载后**（`EnsureModelLoaded` 读到 `RuntimeModelId` 后）调
  `GetDeformableModelContours(out modelContours, modelId, 1)` 并缓存到 `RuntimeModelContours`；
  **不要在学习临时模型上取完就清掉**（`ClearDeformableModel` 后轮廓对象不再可用）。
  （**level 是 1 基**：0 非法 #1302；合法 1..num_levels；P3 实测 3 层模型 1/2/3 OK、0/4 #1302。）
  运行期对每个 `hom` 调 `ProjectiveTransContourXld(RuntimeModelContours, out one, hom)`，
  `ConcatObj`，最后 `new HXLDCont(concat)`。
- `RectifiedImages` 置空（v1 不做）；`MeasureImage` 仍画投影轮廓 + 分数。

**搜索域**：`SearchRegion` 已连接且非空 → `ReduceDomain(gray, region, searchGray)`；
为空/未连 → 全图，并写一条 Info。v1 采用"连了就用"，帮助里必须写清；若要与
`MatchingPlugin` 完全一致，可加 `UseSearchRegion` 开关（默认 false），但 v1 不强制。

### 4.6 失败语义

| 情况 | 行为 |
| --- | --- |
| 上游无图 / 图未初始化 | `Fail("输入图像为空或未初始化")` |
| `RefImagePath` 为空 | `Fail("还没设置参考图：请打开配置界面选择参考图并学习模板")` |
| `Model` 为空 | `Fail("模板还没学习：请打开配置界面点「学习模板」")` |
| 模型损坏 / `ReadDeformableModel` 失败 | `Fail("模板加载失败（载荷损坏？请重新学习）")`，并清掉 `RuntimeModelId` |
| `LearnedAlgorithm` 与当前 `Algorithm` 不符 | `Fail("模板是用「X」学的，当前算法是「Y」：请重新学习")` |
| 学习时 ROI 越界/退化 | 明确失败，点名哪个参数 |
| 学习时 `NumLevels` 填 0 | 创建期映射成 `'auto'`；查找期 0 合法（默认），不要混用 |
| 未找到目标 | `Fail("未找到目标：分数 ≥ … 的形变匹配为 0（可降低分数下限/扩大角度缩放范围）")`，`MatchCount=0` |
| HALCON 算子抛异常 | `Fail($"可形变匹配失败：{ex.Message}")`，不让异常穿出 |

无命中时：`MatchCount=0`、`Score/Row/Column=0`、`Scores/Rows/Columns` 空、`HomMat2D` 空、
`DeformedContours` 空、`RectifiedImages` 空；`MeasureImage` 仍输出原图 + 红字原因。

### 4.7 模型缓存与生命周期

- `EnsureModelLoaded(entry)`：`RuntimeModelId != null && RuntimeModelSource == entry.Model`
  就复用；否则 `ReleaseEntryModel` → `File.WriteAllBytes` → `ReadDeformableModel` → 缓存；
  临时文件 `Path.GetTempFileName()`，`finally` 删除。**绝不每帧重读模型**。
- `ReleaseEntryModel`：`ClearDeformableModel` + Dispose `RuntimeModelContours` +
  清 `RuntimeModelId/RuntimeModelSource/RuntimeModelContours`。
- `ApplyConfigValues`：解析 `TemplateJson`、选 entry、准备模型缓存。
  **运行实例只走 `ApplyConfigValues`，不走 `Initialize`**（`MatchingPlugin.cs:1409` 实测）；
  `Initialize` 只用于配置态（打开视图/预览）。
- `RunAlgorithm` 先 `TryApplyExternalTemplate`：资产/本地文件命中时替换 `Entry`；
  缓存键见 §4.11；外部来源失败直接 `Fail`，不回退内嵌。
- `Dispose`：`ReleaseEntryModel` + 释放所有缓存 HObject。
- 学习时创建的临时模型、`GetDeformableModelContours` 返回的 contours、
  `ProjectiveTransContourXld` 的中间对象、`ConcatObj` 的中间对象都要 `Dispose`。
- 输出端口的值由基类 `AutoDisposeRoundOutputs` 管；局部变量必须自己管。

### 4.8 线程安全

- **v1 先不加 `[ParallelSafe]`**。确认以下三条后再加：
  ① 模型缓存是实例级，不是 static；② 运行期不调用 `SetDeformableModelOrigin`（它会修改模型状态，
  文档要求跨线程访问 ModelID 同步）；③ `FindLocalDeformableModel` / `FindPlanarUncalibDeformableModel`
  只读同一模型句柄，且插件实例不被并行分支共享。
- 若必须并行：每个插件实例一份模型句柄，模型访问加锁；不要共享 static `HTuple`。

### 4.9 帮助与验收

- 帮助手册 L 档：用户页 ≥5、开发页 ≥3、开发页行数 ≥300；**每页 ≥70 行、首行 `# 一级标题`**；
  文件名除扩展名外不许再有点（R60）；`HelpManualCheck` 登记 `["DeformableMatchingPlugin"] = ManualTier.L`。
- `VisionMaster.sln` 必须登记新工程（R62）；`FlowCanvasChecks.csproj` 加 ProjectReference；
  `FlowCanvasChecks/Program.cs` 调 `DeformableMatchingChecks.Run()`。
- 产物判据：`Modules\Plugin.DeformableMatching.dll` mtime ≥ 源码 mtime；
  `Help` 内嵌资源数 = 磁盘 `.md` 数（`audit_artifacts.ps1`）。
- 收尾只走 `tools\run_gates.ps1`；构建一律 `-m:1 -nodeReuse:false`，判据 `0 个错误` 且退出码 0。

### 4.10 UI 设计基线（R110/R111/R127）

- 配置视图属 UI 新增，**必须先出 Web 设计基线**，存 `docs\UI设计基线\可形变模板匹配\`：
  `README.md`（版本/确认日期/确认人/覆盖范围/已知偏差 + 令牌映射）、
  `design.html`（自包含可点击设计稿）、`evidence\`（五态与关键交互截图）；
  并在 `docs\UI设计基线\README.md` 的「现有基线」表加一行。
- 算法/端口/模型持久化属非可视代码（R109），可与基线并行；**WPF 视图必须等基线确认**。
- `MatchingView.xaml` 只做功能参考，不做视觉基准（R124）；差异按 R129/R130 写进"已知偏差"；
  验收按 R132（改前改后截图 + 逐项对照 + 差异清单）。
- 基线只读；变更必须升版本并同步映射表与已复刻页面（R128）。

### 4.11 模板来源：方案资产 / 本地文件 / 内嵌

优先级固定：**方案资产 > 本地模板文件 > 内嵌 `TemplateJson`**。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `TemplateAssetId` | `[StepConfig]` string | 方案模板资产 Id（`IAssetIdConfig.AssetKind = "template"`） |
| `TemplateFilePath` | `[StepConfig]` string | 本地 `DeformableTemplateEntry` JSON |
| `TemplateJson` | `[StepConfig]` string | 内嵌模板/外部模式元数据快照 |

- 资产与本地文件格式相同：单个 `DeformableTemplateEntry` JSON（含 `Model` base64）。
- 资产通过 `IExecutionContext.Assets.TryResolve` 解析；文件资产用 `AbsolutePath`，
  归档/缺失/越界由 `SolutionProjectAssetResolver` 直接拒绝。
- 本地文件支持绝对路径；相对路径按 `AppContext.BaseDirectory` 解析（与 Yolo `ModelPath` 同口径）。
- 外部来源必须校验 `Model` 非空、`LearnedAlgorithm == Entry.Algorithm`；失败直接 `Fail`，
  **不回退内嵌**，避免“看着用新资产、实际跑旧模型”。
- 缓存键：资产 `AssetId|ContentHash|Version|算法`；本地 `路径|大小|mtime|算法`。
- 外部模式 `OnConfirm` 只写元数据（`Model` 置空），模型留在资产/文件里，`.vms` 不再重复 base64。
- 外部模式禁用学习；要改模板先取消资产/本地来源，或导出新 JSON 后重新导入资产。
- 配置壳按 `IAssetIdConfig` 自动列模板资产；本地文件行在插件视图内；
  「导出模板 JSON」用于生成可导入资产的 JSON。
- 资产页引用识别：`SolutionProjectAssetCatalog.MatchesStoredAsset` 显式识别
  `DeformableMatchingPlugin + TemplateAssetId`；不要依赖 `DeformableMatchingPlugin` 里
  恰好包含 `MatchingPlugin` 子串。
- **不拆成 `ModelAsset`**：模板是“ROI + 算法 + 模型 + 指纹”的一整包；拆开会出现
  元数据/模型两份真源，且 `IAssetIdConfig` 只能暴露一个 AssetId。
- **未做**：学习完“一键保存为方案资产”需要宿主写资产 API（`ProjectAssetService`），
  插件只能读资产；v1 走“导出 JSON → 项目资产页导入”。

---

## 5. 验证计划

### 5.1 M1 局部非刚性

- 素材：合成棋盘格 + `GenArbitraryDistortionMap` + `MapImage`（探针已证明棋盘格可命中）；
  或 `OpticalFlowMg` + `RealToVectorField` + `UnwarpImageVectorField`。
- 对拍：同一进程内，插件结果 vs HALCON 参考实现（`CreateLocalDeformableModel` +
  `SetDeformableModelOrigin(roiCenter - cog)` + `FindLocalDeformableModel`）。
- 判据：`MatchCount` 相等；`Score` 差 ≤1e-6；`Row/Column` 差 ≤1e-3 px；
  `DeformedContours` 点数/坐标差 ≤1e-6。
- 反向断言：模板 0°、搜索图旋转 30°，find 角度范围 0/0 → 两边 0 命中；
  角度范围 -180/180 → 两边 ≥1 命中。

### 5.2 M2 平面透视

- 素材：`Image\images\brake_disk\brake_disk_bike_01..11.png`，模板用 `01` 的 ROI。
- 对拍：`CreatePlanarUncalibDeformableModel` + `SetDeformableModelOrigin(roiCenter - cog)` +
  `FindPlanarUncalibDeformableModel`。
- 判据：`HomMat2D` 长度 = 9×N，逐值差 ≤1e-9；`Score` 差 ≤1e-6；
  派生 `Row/Column` 差 ≤1e-3 px；11/11 张命中数一致。
- 派生 `Row/Column` 用 `ProjectiveTransPixel(hom, 0, 0)`（P4 实测）。

### 5.3 回归

- 现有 `MatchingPlugin` 断言全绿；`FlowCanvasChecks` 失败 0（最新
  `run-gates-assets5.log` 记 3951/0）；
  `HelpProbe` 退出 0；`audit_artifacts.ps1` OK；`audit_docs.ps1` 全 0。

### 5.4 UI 基线/复刻

- U0：六项齐全、README 的令牌映射无"待定"、README 有版本/确认人/日期。
- 复刻：逐项对照基线；五态齐全；`UIThemeSmokeTest` 无新增红；
  附改前改后截图 + 差异清单（R122/R132）。

---

## 6. 实施状态与下一步

1. **M0 骨架 — 已完成（2026-10-26）**：csproj + 插件类 + 端口 + 视图 + sln / FlowCanvasChecks /
   HelpManualCheck 登记；插件构建 0/0。
2. **U0 Web 设计基线 — 已出 v0.1，待用户确认**：`design.html` + `README.md`（含令牌映射）+
   `evidence/`；与 M1 核心并行。
3. **M1 局部核心 — 已完成**：`TemplateJson` + 学习/查找/缓存/失败语义 + 合成素材 + 对拍；
   `FlowCanvasChecks.exe --deformable` 181/0（含样例流程与资产/本地来源）。
4. **M1 视图 — 已完成**：按基线实现；`UIThemeSmokeTest --plugin-views` 全通过；
   模板来源行（方案资产提示 / 本地文件 / 导出 JSON）已加；视觉细调 / 差异清单待用户确认后补。
5. **M2 平面 — 已完成**：`HomMat2D` / 派生 Row/Col / 投影 contours；刹车盘 11/11，
   `HomMat2D` maxDiff=0。
6. **M3 UI/可视化 — 部分完成**：`MeasureImage` / `DisplayViewIndex` 已做；
   画布 ROI / 涂抹掩膜编辑器待做（v1 数值 ROI + 只读预览）。
7. **M4 收尾 — 已完成（2026-10-27）**：`tools\run_gates.ps1` **14 步全绿**
   （`FlowCanvasChecks` 3951/0、`UIThemeSmokeTest`/`HelpProbe` 退出 0、两个 audit 与
   `docs_index`/`sync_constraints` 均 exit 0）；两条 `.vms` 样例流程已加入并通过
   Load→Compile→Execute 断言；模板已支持方案资产/本地文件/内嵌三来源。
   剩余项只有 M3 画布 ROI/掩膜编辑器，以及“学习完一键保存为方案资产”（需宿主写资产 API）。

---

## 7. 实施与对拍结果（2026-10-26）

### 7.1 专项断言

`FlowCanvasChecks.exe --deformable`：**通过 181 / 失败 0**（139 条算法/样例 + 42 条资产/本地来源）。

> 本机 HALCON 23.05 未安装 `Matching\Deformable\*.hdev`（只装了 Deep-Learning / Image 示例），
> 因此对拍不是照抄例程文件，而是**在同一进程内用相同算子、相同参数、相同图像**写一份参考实现
> （`CreateLocalDeformableModel` / `CreatePlanarUncalibDeformableModel` +
> `SetDeformableModelOrigin(roiCenter - cog)` + `Find*`），逐值比较插件输出。
> 这与例程核心代码等价，且能直接定位参数差异（例如 `deformation_smoothness` 必须是整数）。

| 项 | 结果 |
| --- | --- |
| 端口契约 | 7/7 |
| 局部同图 | MatchCount=1；Score/Row/Column 逐值一致；DeformedContours=496 段；RectifiedImages=1 张 |
| 局部平移 | MatchCount=1；Score/Row/Column 逐值一致；DeformedContours=496 段；RectifiedImages=1 张 |
| 平面 11 图 | 11/11 命中数一致；HomMat2D maxDiff=0；Score/派生 Row/Column 逐值一致 |
| 反向断言 | 0/0 → 两边 0 命中；±180 → 两边 ≥1 命中 |
| 序列化 | TemplateJson 往返后 Model/LearnedAlgorithm 保留，结果一致 |
| 缓存 | 连续两帧 ModelLoadCount=1 |
| 失败语义 | 无模型/无图/算法不符/无命中/坏模型/ROI 退化全部中文失败 |
| 方案资产/本地文件 | 资产与本地各 1 命中、分数与内嵌一致；优先级/类型/缺失/归档/坏 JSON/算法不符/升版重载/来源状态全过 |
| 样例流程 | 两条 `.vms` 均 LoadAsync → Compile → 真插件执行；局部 1 命中/Score=1，平面 1 命中/Score=0.999675 |

### 7.2 其他验证

- `HelpManualCheck --help-check`：20/0；HM15 已计入 54 个已交插件类。
- `HelpProbe`：全部通过；新插件 8 页进入目录。
- `UIThemeSmokeTest --plugin-views Plugin.DeformableMatching`：全部通过；
  视图 540 元素、Button.Command 10 个 / 8 个命令绑定、XAML hex=0、渲染 PNG 已出。
- `audit_artifacts.ps1`：OK（40 个插件工程）。
- 全量 `FlowCanvasChecks.exe`：**通过 3951 / 失败 0**（本插件 181 条在最后，既有基线未回退）。
- 完整 `run_gates.ps1`（`run-gates-assets5.log`）：**14 步全绿**，
  `FlowCanvasChecks` 3951/0、`UIThemeSmokeTest`/`HelpProbe` exit 0、两个 audit 与
  `docs_index`/`sync_constraints` 均 exit 0。

### 7.4 样例流程（`.vms`）

- 两条流程插在 `Home/Main/End` 之前：
  `形变匹配_局部非刚性`（棋盘格）与 `形变匹配_平面透视`（刹车盘）。
- 图片资产：`Image\checkerboard_400.png`、`Image\brake_disk_bike_01.png`；
  `.vms` 从 551,589 增至 1,279,006 bytes。
- 模型载荷：局部 95,984 chars、平面 616,768 chars（`TemplateJson.Model` base64）。
- 断言：`DeformableMatchingChecks.RunSampleFlows()` 走生产加载器 + 编译器 + 真插件；
  两条流程各 1 命中，`ModelLoadCount=1`。

### 7.5 方案资产 / 本地模板文件（2026-10-27）

`FlowCanvasChecks.exe --deformable` 中新增 **42 条**：

- 资产模式与内嵌模式：命中数、Score 逐值一致；连续两帧 `ModelLoadCount=1`；
- 资产升版（Version/ContentHash 变化）后 `ModelLoadCount=2`；
- 本地文件模式与内嵌一致；文件 mtime 变化后重新加载；
- 优先级：资产 + 本地文件同时配置时资产胜出（本地放平面模板，若本地赢会算法不符）；
- 失败语义：类型不符（model 当 template）、文件缺失、已归档、坏 JSON、本地缺失、算法不符；
- 错格式：选中 Matching 模板库数组时明确报「模板库数组，不是形变模板」；
- 来源状态：资产模式禁用学习/本地输入、状态胶囊「方案资产」、清除按钮「清除内嵌」；
  本地模式禁用学习、状态胶囊「本地模板」；
- `OnConfirm` 外部模式只写元数据（`Model` 为空）且保留 `TemplateAssetId`；
- 资产页：`SolutionProjectAssetCatalog` 能显示形变模板的流程引用。

### 7.3 实施期踩坑

- `deformation_smoothness` 必须是整数；`double` 会报 HALCON #9465。
- `ImageReadOnly.HImage` 绑只读属性必须 `Mode=OneWay`。
- 视图按钮至少一个 `Command` 绑定（UI 冒烟契约）；本插件用本地 `UiCommand`。
- 局部对拍必须与参考实现用同一角度范围；0/0 与 -30/60 会得到不同 Score。
- `.vms` 样例流程用一次性工具生成（`LearnTemplateFromImage` + Newtonsoft 克隆流程骨架）；
  宿主在跑时锁 `Modules\`/宿主 `bin`，一次性校验工具改用对已构建 DLL 的
  `<Reference Private=true>` + `AssemblyResolve`，不重建产品。
- 资产页引用识别里 `DeformableMatchingPlugin` **恰好包含** `MatchingPlugin` 子串：
  旧代码会“碰巧”显示引用；必须显式识别，不能依赖这个巧合。
- 外部来源 `OnConfirm` 若仍序列化完整 `Entry`，`.vms` 会同时存资产和 base64；
  已改成外部模式只写元数据。
- 本地文件相对路径按宿主程序目录解析，不是方案目录；样例流程保持内嵌/资产，避免机器相关路径。

---

## 8. 证据与复现

- 探针日志：`artifacts\logs\20261026\deformable-probe.log`（含 P1–P4 全部实测值）。
- 探针源码：`artifacts\logs\20261026\deformable-probe\Program.cs.txt` /
  `DeformableProbe.csproj.txt`（一次性工程，按 R166 已从 `tools\oneoff\` 删除）。
- 复现：新建 `net9.0-windows` 控制台工程，引用 `DLL\Halcon\halcondotnet.dll`；
  HALCON `bin\x64-win64` 在 PATH；按 §3.1 的合成棋盘格与参数重跑即可。
- 探针输出中的关键行：
  - `[P1] origin(0,0): Row=199.5 Col=199.5` / `origin(50,30): Row=249.499 Col=229.5`
  - `[P2] rectified.CountObj=2 contours.CountObj=972`
  - `[P3] level 0 FAIL #1302` / `level 1 OK` / `level 3 OK` / `level 4 FAIL`
  - `[P4] origin(0,0): project(0,0)=(249.5,229.5)` / `origin(50,30): project(0,0)=(299.5,259.5)`
