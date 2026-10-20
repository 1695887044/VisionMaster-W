# 插件自定义视图 Web 化改造总览（2026-10-20）

## 1. 结论

- 盘点口径：`Plugins\` 下实现 `Core.Interfaces.IPluginCustomViewProvider` 且有 `GetConfigView` 的插件，共 **22 个**。
- 截至 2026-10-20：**22/22 已完成 Web 化**（`Plugin.ImageAcquisition` 为参考实现，其余 21 个按 SOP 逐批完成）。
- 每个插件均产出：`design.html` + `README.md`（设计基线）、WPF 视图/必要 code-behind、`docs/code-changes/2026-10-20-<插件>-Web化.md`。
- 统一约束：`DataContext=插件`、`IPluginConfigView`、`LinkableValueEditor` 的 `Port/StepData/BrowseCommand`、HALCON 画布控件与绑定、命令/持久化/业务逻辑均未改；视图 XAML `hex=0`，颜色走公共令牌；宿主 `MinHeight` 统一 **560**。
- 用户延后项：加载骨架、图像尺寸/文件大小、放大/缩小、打开文件夹、系统导航均未做。

## 2. 变更清单

| # | 插件 | 视图文件 | 设计基线 | 改造记录 | 审核状态 |
| --- | --- | --- | --- | --- | --- |
| 1 | `Plugin.ImageAcquisition` | `ImageAcquisitionView.xaml/.xaml.cs` | `image-acquisition/`（v2） | `2026-10-20-图像采集配置窗口V2复刻与FluentChipDanger重复键修复.md` | 参考实现；24/24 + 5 张渲染 |
| 2 | `Plugin.Utility` / VariableAssignment | `VariableAssignmentView.xaml/.xaml.cs` | `variable-assignment/` | `2026-10-20-VariableAssignment-Web化.md` | 审核通过（P2/P3 文档/视觉） |
| 3 | `Plugin.CodeReader` | `CodeReaderView.xaml/.xaml.cs` | `code-reader/` | `2026-10-20-CodeReader-Web化.md` | 审核通过（P2/P3） |
| 4 | `Plugin.DlClassify` | `ClassifyView.xaml/.xaml.cs` | `dl-classify/` | `2026-10-20-DlClassify-Web化.md` | 审核通过（P2/P3） |
| 5 | `Plugin.Yolo` | `YoloView.xaml/.xaml.cs` | `yolo/` | `2026-10-20-Yolo-Web化.md` | 审核通过（P2/P3） |
| 6 | `Plugin.ExcelExport` | `ExcelExportView.xaml/.xaml.cs` | `excel-export/` | `2026-10-20-ExcelExport-Web化.md` | P1 修复；复审 PASS |
| 7 | `Plugin.ImageScript` | `ImageScriptView.xaml/.xaml.cs` | `image-script/` | `2026-10-20-ImageScript-Web化.md` | 审核通过（P2/P3） |
| 8 | `Plugin.CSharpScript` | `CSharpScriptView.xaml/.xaml.cs` | `csharp-script/` | `2026-10-20-CSharpScript-Web化.md` | 审核通过（P2/P3） |
| 9 | `Plugin.ResultDisplay` | `ResultDisplayView.xaml/.xaml.cs` | `result-display/` | `2026-10-20-ResultDisplay-Web化.md` | 审核通过（P2/P3） |
| 10 | `Plugin.DataRecord` | `DataRecordView.xaml/.xaml.cs` | `data-record/` | `2026-10-20-DataRecord-Web化.md` | 审核通过（P2/P3） |
| 11 | `Plugin.ResultUpload` | `ResultUploadView.xaml/.xaml.cs` | `result-upload/` | `2026-10-20-ResultUpload-Web化.md` | 审核通过；M1~M3 修复；M4 断言遗留 |
| 12 | `Plugin.ColorRegion` | `RegionColorView.xaml/.xaml.cs` | `color-region/` | `2026-10-20-ColorRegion-Web化.md` | P1 修复；复审通过 |
| 13 | `Plugin.Ocr` | `OcrView.xaml/.xaml.cs` | `ocr/` | `2026-10-20-Ocr-Web化.md` | 审核通过（P3） |
| 14 | `Plugin.CreateRoi` | `CreateRoiView.xaml/.xaml.cs` | `create-roi/` | `2026-10-20-CreateRoi-Web化.md` | 审核通过（P2/P3） |
| 15 | `Plugin.ColorCheck` | `ColorCheckView.xaml/.xaml.cs` | `color-check/` | `2026-10-20-ColorCheck-Web化.md` | H1/M1/M3 修复；复审通过；M2 遗留 |
| 16 | `Plugin.PoseTransform` | `PoseTransformView.xaml/.xaml.cs` | `pose-transform/` | `2026-10-20-PoseTransform-Web化.md` | P2 修复；复审 PASS |
| 17 | `Plugin.PreProcessing` | `PreProcessingView.xaml/.xaml.cs` | `pre-processing/` | `2026-10-20-PreProcessing-Web化.md` | P1（含卡片式）修复；复审 PASS |
| 18 | `Plugin.CaliperMeasure` | `CaliperMeasureView.xaml/.xaml.cs` | `caliper-measure/` | `2026-10-20-CaliperMeasure-Web化.md` | P1/P2 修复；复审 PASS |
| 19 | `Plugin.Matching` | `MatchingView.xaml/.xaml.cs` | `matching/` | `2026-10-20-Matching-Web化.md` | P2 修复；复审 PASS |
| 20 | `Plugin.Calibration` | `CalibrationView.xaml/.xaml.cs` | `calibration/` | `2026-10-20-Calibration-Web化.md` | P1/P2/P3 修复；复审 PASS |
| 21 | `Plugin.BlobDetect` | `BlobDetectView.xaml/.xaml.cs` | `blob-detect/` | `2026-10-20-BlobDetect-Web化.md` | 审核通过（P2/P3） |
| 22 | `Plugin.BeadInspect` | `BeadInspectView.xaml/.xaml.cs` | `bead-inspect/` | `2026-10-20-BeadInspect-Web化.md` | 生产代码审核通过；探针 P1 修复并全 PASS |

## 3. 验证口径与结果

| 项 | 口径 | 结果 |
| --- | --- | --- |
| 插件构建 | 每个插件工程 `dotnet build` | 各插件 **0 错误 / 0 警告**；`Modules\Plugin*.dll` 已刷新并与 `bin\Debug` 产物一致 |
| 通用视图冒烟 | `UIThemeSmokeTest --plugin-views <filter>` | **22/22 插件全 PASS，每个 11 条**（`[FAIL]` 计数均为 0；ImageAcquisition 另跑专属 24 条） |
| 插件专属冒烟 | `--calibration` / `--image-acquisition` 等 | Calibration 12/12；ImageAcquisition 24/24；其余按各插件既有断言 |
| 常规 gate | `FlowCanvasChecks --no-build` | **2749 通过 / 0 失败**（最终一次全量运行，含 CC4/EE4/BI4/BD6/YO3/DL1 新增 58 条） |
| 整解决方案构建 | `dotnet build VisionMaster.sln` | **0 错误**（747 条既有警告，来自既有工程；插件工程自身 0/0） |
| 渲染证据 | 批量冒烟 PNG + 各插件 `evidence/` | 每个插件至少 1 张初始态；设计基线含 960/640 与关键状态切换；HALCON `HwndHost` 内容不被 `RenderTargetBitmap` 捕获，真实图像需真机 |
| 红线 | 插件位置 / 公共契约不进 Modules / 改插件必重建 / 不复制 AnnotationBaker | 逐插件复核通过 |

## 4. 遗留问题（统一口径）

1. **设计基线确认**：22 个 `README.md` 仍为「待用户确认」；本轮只完成设计稿与 WPF 复刻，未取得用户逐页确认。确认后基线冻结，改动升 v2。
2. **真机验收**：所有 HALCON 画布/ROI/预览、右键菜单、拖拽、试算、确认后重开、关闭无泄漏均需在 VisionMaster 真机走查；离屏 PNG 不能替代。
3. **640×560**：复杂三栏视图（PreProcessing 根滚动、Calibration/CaliperMeasure/Matching 窄档隐藏）需真机复核滚动与 HwndHost 表现；简单表单已按 560/600 适配。
4. **插件专属断言**：通用 `--plugin-views` 是基线，不替代关键绑定断言；ColorCheck M2、部分插件（PreProcessing/Calibration 内参折叠区等）仍缺插件专属/负向断言，已登记在各 code-changes。
5. **发布配置**：本轮验证以 Debug 为主；`pack.ps1` 打 Release，发布前需按 R55/R62 做 Release 重建并核对 `Modules` 时间戳/大小（尤其 `Plugin.Camera.Network`、`Plugin.ImageAssign` 不在 `VisionMaster.sln` 内）。
6. **P2/P3 与 FUNC 核销**：已按 4 组只读审查逐条核查 91 项，确认 79 项属实、11 项部分属实、1 项不属实；已修复 55 项（49 项 UI/DOC + 6 个 FUNC ID）。其中 FUNC 追加修复 CC4（试算使用画布当前图）、EE4（调度失败兜底）、BI4（BeadInspect 5 个入口）、BD6（BlobDetect 生命周期守卫）、YO3/DL1（Yolo/DlClassify 状态枚举）。其余 UI/DOC 修复含 ResultUpload M1~M3、DataRecord 空列名、ImageScript 列宽/状态条/双来源、CSharpScript ToolTip、ResultDisplay 可编辑判定项、Yolo 错误标题、Ocr Hint、CreateRoi ShapeTypeCN/ROI chip、Calibration 空态/左栏宽度、PreProcessing 文档 96px、ColorCheck HasRoi 等。剩余 TEST/真机/发布项见 [`插件遗留问题核查与修复结果.md`](插件遗留问题核查与修复结果.md)。
7. **设计证据**：多数插件只有批量冒烟 920×520 初始态；4~5 张主态/空态/错误态证据需后续扩展 `UIThemeSmokeTest` 专属 render 模式（共享文件，本轮未改）。

## 5. 回滚

- 单插件回滚：还原该插件 `*View.xaml` / `*View.xaml.cs`（以及个别 nullable 注解）到改造前版本，重建该插件工程；设计基线/记录可保留。
- 共享层未改：`Colors.xaml`、`PluginConfigColors.xaml`、`PluginConfigShellView.xaml`、`UIThemeSmokeTest` 的 Program/csproj/PluginViewSmoke 均未由插件改造修改（宿主 `MinHeight=560` 是主会话统一改动，单独回滚即可）。
- 不涉及 `.vms` / 端口名 / 持久化字段，回滚不会破坏已保存方案。
