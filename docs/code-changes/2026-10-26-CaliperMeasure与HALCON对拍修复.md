# CaliperMeasure 与 HALCON 对拍：布点 / 测量 / 拟合 / 角度

> 日期：2026-10-26。范围：`Plugin.CaliperMeasure`（宽度、两点距、点到线距、圆直径、角度）。
> 方法：临时探针 `%TEMP%\vm-halcon-caliper-compare` 直接引用插件工程，合成已知几何图，
> 与 HALCON 23.05 的 `gen_measure_rectangle2` / `measure_pos` / `measure_pairs` /
> `fit_line_contour_xld` / `fit_circle_contour_xld` / `angle_ll` 对照。
> **本轮改了产品代码**：`Plugin.CaliperMeasure`（measure 句柄重试）。
> 回归：`FlowCanvasChecks` 新增 `RunMeasurementTruth()`，全量 **3708 通过 / 0 失败**。

## 一、当前状态

- **卡尺布点**：沿 L2（垂直于 Phi）均布，已用反射直接验证三种 Phi：
  - Phi=0：中心行 160/200/240/280/320，列恒 320，L1=80、半宽=20；
  - Phi=90：行恒 240，列 240/280/320/360/400；
  - Phi=45：中心与 L1 轴点积 = 0（严格垂直）。
- **宽度/间隙**：40px 亮带 → 插件 40.000，HALCON `measure_pairs` 同 ROI 40.000。
- **两点距**：两条阶跃边相距 200px → 插件 200.000，HALCON `measure_pos + distance_pp` 200.000。
- **点到线距**：点列 200、线列 400 → 插件 200.000。
- **圆直径**：r=100 圆 → 插件 199.926（拟合/亚像素误差 <0.1px）。
- **角度**：
  - 直角 → 90.000°；
  - 两条 30°/80° 亮条、ROI 同侧 → 49.906°；
  - ROI 异侧 → 130.094°。
- **结论**：五种测量类型与 HALCON 口径一致；布点方向、圆环扫描、角度射线取向均正确。

## 二、已做

### 2.1 对拍结果

最终日志：`artifacts\logs\20261026\halcon-caliper-compare5.log`（12 项全 PASS）。

| 项 | 插件实测 | HALCON 对照 | 结论 |
| --- | --- | --- | --- |
| Phi=0 布点 | 行 160/200/240/280/320，列 320 | L2 轴 = (cos0,sin0) | 通过 |
| Phi=90 布点 | 行 240，列 240/280/320/360/400 | L2 轴 = (cos90,sin90) | 通过 |
| Phi=45 布点 | 中心与 L1 轴点积 0 | 垂直 | 通过 |
| 宽度 40px | 40.000 | `measure_pairs` 40.000 | 通过 |
| 两点距 200px | 200.000 | `measure_pos + distance_pp` 200.000 | 通过 |
| 点到线距 200px | 200.000 | 已知线 x=400 | 通过 |
| 圆直径 r=100 | 199.926 | `fit_circle_contour_xld` 真值 200 | 通过 |
| 直角 | 90.000° | `angle_ll` 90° | 通过 |
| 30/80 同侧 | 49.906° | 50° | 通过 |
| 30/80 异侧 | 130.094° | 130° | 通过 |

### 2.2 修复：measure 句柄重试无效

`MeasurePosWithRetry` / `MeasurePairsWithRetry` 原先每次重试都 **新建 measure 句柄**：

```csharp
for (int attempt = 0; attempt < 4; attempt++)
{
    var handle = CreateMeasureHandle(...);   // 每次都是新句柄
    MeasurePos(gray, handle, ...);
    if (rowEdge.Length > 0) return ...;
}
```

而源码注释写的"HALCON 首跑可能空、第二跑正常"是**同一个句柄**的行为；新建句柄后每次都是"首跑"，
重试等于没做。修复：句柄只建一次，4 次重试都落在同一个句柄上；句柄仍由外层 `finally` 统一
`close_measure`。

> **诚实说明**：本轮首跑圆/角度失败的直接原因是**探针没有清掉 `MeasureKind` setter 自动播种的
> 默认搜索区**，不是这个重试 bug；修复默认区后全部通过。重试句柄修复仍保留——原实现与它自己
> 的注释语义不符，是一个独立的正确性问题，已通过全量断言（3708/0）。

### 2.3 永久回归

`FlowCanvasChecks\CaliperMeasureChecks.cs` 新增 `RunMeasurementTruth()`：

- 40px 亮带 → 40px；
- r=100 圆 → 200px；
- 30°/80° 亮条、ROI 同侧 → 50°；
- ROI 异侧 → 130°。

全量 `FlowCanvasChecks`：**3708 通过 / 0 失败**。
日志：`artifacts\logs\20261026\flowcanvas-caliper-truth.log`。

### 2.4 P1 清单的覆盖情况

本轮把剩余 P1 项逐项核对，结论是**已有断言已覆盖，没有发现新 bug**：

| 项 | 现有覆盖 |
| --- | --- |
| `GeometryFinder` 线/圆/椭圆 | `GeoDemoCheck` 真实图线/圆 + 本轮新增合成椭圆（30°→150°）；通过 |
| `CreateRoi` 参数语义/区域/Offset | `CreateRoiChecks` 矩形/圆/椭圆面积、裁剪域、Offset、合并/涂擦；通过 |
| `BlobDetect` 特征/排序/当量 | `BlobDetectChecks` 面积、Max/Total、排序、填孔、像素当量；`RegionFeatures` 直接调 HALCON |
| `BeadInspect` 中心线/路径/Hausdorff | `BeadInspectChecks` 13a-d（树合并覆盖 ≥95%）、15（平均偏离 <3px）；通过 |
| `PreProcessing` / `ImageDiff` | `PreProcessingChecks` 顶帽/黑帽/Sobel/Laplace 像素级；`ImageDiffCheck` 面积/尺寸失配/极性；通过 |
| `Ocr` / `CodeReader` | `CodeReaderChecks` QR/MicroQR/Aztec/DotCode/Code128/Code39 样张解码；`OcrChecks` 同类；通过 |

这些插件的核心结果由 HALCON 算子直接给出，风险在参数映射；现有断言已经覆盖了参数映射与像素结果。

## 三、未做

- 没有用真实产线图/真实卡尺 ROI 做端到端对拍；合成图的几何真值更硬，但现场光照/毛刺/遮挡未覆盖。
- 没有覆盖 `CaliperCount` 很大（>100）时的性能/句柄数量上限；当前每把卡尺 1 个句柄，
  36 把圆卡尺 = 36 个句柄，未做压力测试。
- 没有覆盖 `EdgeSelect=Last` 的端到端值（只覆盖了 `First`）；`Last` 走同一 `measure_pos` 参数映射。
- 没有把临时探针登记成 `tools\probes` 的常驻探针（R167）；本轮用完即删。

## 四、下一步

1. 关掉 `VisionMaster.exe` 后跑完整 `tools\run_gates.ps1`（本轮已跑 `FlowCanvasChecks` 3708/0、
   `audit_artifacts` 39 个插件 OK；带构建的 gate 仍被宿主锁 `Modules\`）。
2. 现场有真实卡尺图时，用同一组 ROI 跑插件与 HALCON，复核宽度/圆/角度。
3. 若要压测：`CaliperCount` 100+、圆直径 100+ 把，确认 measure 句柄数量与耗时。

## 五、踩过的坑

- **`MeasureKind` setter 会 `EnsureRequiredRegions()` 自动播种默认搜索区**：探针里先设类型、
  再 `CaliperRegions.Add(自己的区)`，`NormalizedParameters` 取的是**前 N 个默认区**，自己的区
  根本没参与。必须先 `CaliperRegions.Clear()` 再手工加。首跑圆/角度"未检出边缘"就是这个原因。
- **`CaliperRegion.Params` 的角度是弧度**，且 HALCON 矩形长轴是 `(-sinφ, cosφ)`，不是
  `(sinφ, cosφ)`。用插件自己的 `(sin,cos)` 去摆 HALCON 亮条/ROI 会整体镜像，表现为"卡尺没压在边上"。
- **圆直径的卡尺中心在用户画的圆周上，L1=环宽/2 沿径向**；`AnnulusWidth` 太小/圆边不在环内会
  逐把报"未检出边缘"，不是静默跳过。
- **角度是 0~180° 的射线夹角**：ROI 在交点同侧得 50°，异侧得 130°；不要把它当"无向线夹角 [0,90]"。
- **`measure_pairs` 的 `IntraDistance` 是沿扫描方向的间距**；宽度直接用 `intra[0]`，不要再用
  `distance_pp` 算两边缘点（斜边时两者不同）。
- **重试必须复用同一个 measure 句柄**：HALCON 的"首跑空、第二跑正常"是句柄级行为。
- **临时探针要清默认区、要释放 HImage/HObject/measure 句柄**；本轮探针用完已删（R134）。

## 六、证据

| 项 | 结果 |
| --- | --- |
| 布点（Phi=0/90/45） | 全部通过（L2 轴垂直 L1） |
| 宽度 | 40.000 / HALCON 40.000 |
| 两点距 | 200.000 / HALCON 200.000 |
| 点到线距 | 200.000 |
| 圆直径 | 199.926 |
| 角度 | 90.000 / 49.906 / 130.094 |
| FlowCanvasChecks | 3708 通过 / 0 失败 |
| audit_artifacts | 39 个插件工程 OK |

日志：
`artifacts\logs\20261026\halcon-caliper-compare5.log`（12 项对拍）、
`artifacts\logs\20261026\flowcanvas-caliper-truth.log`（3708 条断言）。

源码：
`Plugins\Plugin.CaliperMeasure\CaliperMeasurePlugin.cs`（measure 句柄重试）、
`FlowCanvasChecks\CaliperMeasureChecks.cs`（`RunMeasurementTruth`）。
