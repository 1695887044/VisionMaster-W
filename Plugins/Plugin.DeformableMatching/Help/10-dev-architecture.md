# 可形变模板匹配 · 开发架构

## 1. 工程结构

```text
Plugins\Plugin.DeformableMatching\
  Plugin.DeformableMatching.csproj
  DeformableAlgorithm.cs
  DeformableTemplateEntry.cs
  DeformableMatchRow.cs
  DeformableMatchingPlugin.cs
  DeformableMatchingView.xaml
  DeformableMatchingView.xaml.cs
  Help\01-quickstart.md ... 12-dev-invariants.md
```

引用集：

- `Core.Halcon`：HALCON 控件/帮助/颜色常量；
- `Core.Controls`：`PublishPreview` 等宿主扩展；
- `Core.Interfaces`：`VisionPluginBase`、端口、`[StepConfig]`；
- `UI.Controls`：`NumericBox` 等控件；
- `halcondotnet`：HALCON 算子。

## 2. 类职责

| 类 | 职责 |
| --- | --- |
| `DeformableMatchingPlugin` | 生命周期、配置、学习、查找、缓存、端口、状态 |
| `DeformableTemplateEntry` | 单模板数据（INPC、JSON 落盘） |
| `DeformableAlgorithm` | 两种算法枚举 + `[Display]` |
| `AlgorithmOption` | 算法卡片视图模型 |
| `DeformableMatchRow` | 结果表行 |
| `DeformableMatchingView` | 纯绑定视图 + 点击转发 |
| `InverseBooleanToVisibilityConverter` | 视图辅助 |

## 3. 数据流

```text
配置界面
  → Entry（参考图/ROI/掩膜/算法/参数）
  → OnConfirm: 内嵌模式写完整 Entry；外部模式只写元数据（Model 置空）
  → InputValues["TemplateJson"] + TemplateAssetId / TemplateFilePath
  → 运行实例 ApplyConfigValues: 解析 TemplateJson → Entry（内嵌快照）
  → RunAlgorithm: TryApplyExternalTemplate
      资产 → IExecutionContext.Assets → 绝对路径
      本地 → 绝对路径或宿主程序目录相对路径
      都没有 → 用内嵌 Entry
  → EnsureModelLoaded: base64 → 临时文件 → read_deformable_model
  → Find*: 搜索 → 端口
  → MeasureImage / 结果表 / 状态
```

## 4. 学习流程

1. `LearnTemplate()` 检查路径并 `read_image`；
2. `LearnTemplateFromImage()`：
   - `BuildRoiRegion`：矩形/圆/椭圆；
   - 掩膜：`Base64ToMaskRegion` + `difference`；
   - `AreaCenter` 求域重心；
   - `reduce_domain` + 灰度化；
   - `create_local_deformable_model` 或 `create_planar_uncalib_deformable_model`；
   - `set_deformable_model_origin(roiCenter - cog)`；
   - `write_deformable_model` → base64；
   - `clear_deformable_model`；
   - `ReleaseEntryModel` + `EnsureModelLoaded` 缓存句柄；
   - `RefreshPreview`（配置态）。
3. 设置 `LearnedAlgorithm`、`LearnedSignature`；
4. 状态变「已学习」。

## 5. 查找流程

### 5.1 局部

```csharp
FindLocalDeformableModel(
    gray, out rectified, out vectorField, out deformed,
    modelId,
    angleStartRad, angleExtentRad,
    scaleRMin, scaleRMax, scaleCMin, scaleCMax,
    minScore, maxMatches, maxOverlap, numLevels, greediness,
    ["deformed_contours","image_rectified"],
    ["deformation_smoothness","expand_border","subpixel"],
    [smoothness, 0, 1],
    out scores, out rows, out cols);
```

- `RectifiedImages` = `rectified`（每命中一张）；
- `DeformedContours` = `deformed`（扁平 XLD 数组）；
- `HomMat2D` = 空。

### 5.2 平面

```csharp
FindPlanarUncalibDeformableModel(
    gray, modelId,
    angleStartRad, angleExtentRad,
    scaleRMin, scaleRMax, scaleCMin, scaleCMax,
    minScore, maxMatches, maxOverlap, numLevels, greediness,
    [], [],
    out homMat2D, out scores);
```

- `Row/Column`：`ProjectiveTransPixel(hom, 0, 0)`；
- `DeformedContours`：`get_deformable_model_contours(level=1)` + `projective_trans_contour_xld`；
- `RectifiedImages` = 空。

## 6. 模型缓存

- 键：`RuntimeModelSource == Entry.Model`；
- 命中：直接复用 `RuntimeModelId`；
- 未命中：`ReleaseEntryModel` → 临时文件 → `read_deformable_model` → 缓存；
- 平面模式同时缓存 `RuntimeModelContours`（level=1）；
- `ModelLoadCount` 用于断言“连续两帧只读一次”。

## 6.1 外部模板来源

- 插件实现 `IAssetIdConfig`：`AssetKind = "template"`，`AssetId` 映射到 `TemplateAssetId`；
- 配置壳按 `AssetKind` 自动列出方案模板资产，选中后写回 Id；
- `TryApplyExternalTemplate` 的优先级：资产 → 本地文件 → 内嵌；
- 资产通过 `IExecutionContext.Assets.TryResolve` 拿 `AbsolutePath/ContentHash/Version`；
- 本地文件用 `TemplateFilePath`，相对路径按 `AppContext.BaseDirectory` 解析；
- 外部条目必须通过 `ValidateExternalEntry`：`Model` 非空、`LearnedAlgorithm == Entry.Algorithm`；
- 外部来源失败直接 `Fail`，不回退内嵌；
- 缓存键：资产 `AssetId|ContentHash|Version|算法`；本地 `路径|大小|mtime|算法`；
- `OnConfirm` 外部模式只写元数据，避免 `.vms` 再存一份 base64。

## 7. 预览

- `_referenceImage` 缓存参考图；
- `RefreshPreview`：`Gen*ContourXld` + `PaintXld`（RGB 绿色；失败回退灰度）；
- `PreviewImage` setter 释放旧 `HImage`；
- 视图 `ImageReadOnly` 常驻，不折叠。

## 8. 失败语义

- 无图/无模型/算法不符/加载失败/无命中 → `Fail(中文原因)`；
- 外部来源：类型不符/文件缺失/已归档/坏 JSON/算法不符 → `Fail`，不回退内嵌；
- 异常 → 基类 `Execute` 捕获或插件 `Fail`；
- 无命中仍输出 `MeasureImage`（原图）；
- 失败原因写 `ErrorMessage` + `context.Logger`。

## 9. 线程与 UI

- `RunAlgorithm` 可能在工作线程；
- `MatchRows`/`StatusMessage` 通过 `UpdateViewState` 派发到 UI 线程；
- 无 `Application` 的断言宿主直接执行；
- v1 不加 `[ParallelSafe]`，确认实例级缓存/无共享静态状态后再加。

## 10. 构建与部署

```text
dotnet build Plugins\Plugin.DeformableMatching\Plugin.DeformableMatching.csproj -m:1 -nodeReuse:false
```

- 判据：`0 个错误` 且退出码 0；
- `Directory.Build.targets` 把 DLL 投递到 `Modules\`；
- 改插件源码必须重建该工程（红线③）；
- 改帮助也必须重建（内嵌资源）。

## 11. 断言与对拍

- `FlowCanvasChecks\DeformableMatchingChecks.cs` 直接引用插件工程；
- 同进程 HALCON 参考实现逐项比对；
- 局部合成图 + 平面刹车盘 11 图；
- 反向断言证明“两边都会变”；
- 容差见 `04-criteria-and-tuning.md`。

## 12. 扩展点

- 多模板：把单模板字段收进 `List<DeformableTemplateEntry>` + `TemplateLibraryJson`；
- 标定平面：`create_planar_calib_deformable_model` + `Pose` 输出；
- VectorField：`find_local_deformable_model` 的 `vector_field` 输出；
- 画布 ROI：M3 增量，复用 `ImageEdit`；
- 资产化：已实现方案模板资产 + 本地模板文件；模型仍以 base64 存在模板 JSON 内，
  后续若模型体积继续增大，可再拆成 `ModelAsset`（需要额外维护元数据/模型两份真源）。

## 13. 关键依赖版本

- HALCON 23.05 Progress；
- .NET 9 / WPF；
- `halcondotnet.dll` 来自 `DLL\Halcon`；
- 原生 `halcon.dll` 由 HALCON `bin\x64-win64`（PATH/HALCONROOT）提供。
