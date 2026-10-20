# Blob 缺陷检测配置窗口 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 日期 | 2026-10-20 |
| 确认状态 | 待用户确认（本版按 ImageAcquisition V2 视觉语言逐插件重画） |
| 覆盖范围 | `Plugins\Plugin.BlobDetect\BlobDetectView.xaml` + `BlobDetectView.xaml.cs`（插件内容区） |
| 窗口尺寸 | 960×680 默认；640×680 窄档；宿主最小 **640×560**（`PluginConfigShellView.xaml`，本插件不改） |
| 设计基线文件 | `design.html`（v1，自包含、内联 SVG、可点击） |
| 复刻状态 | 已完成（2026-10-20）；证据 `evidence/after-render-v1-initial.png` |
| 参考实现 | `docs\UI设计基线\image-acquisition\`（V2）+ `docs\code-changes\2026-10-20-图像采集配置窗口V2复刻与FluentChipDanger重复键修复.md` |

## 1. 定位与范围

本文件是 SOP §5.2 的产物：**Web 设计基线先行**，确认后再改 WPF。Web 只是设计媒介，不进入构建、不部署。
本插件是批次 3（大型多面板）的第一个插件：2 块画布、19 个数值参数、1 个缺陷清单 DataGrid、3 个输入端口、5 个参数分区。

**只做纯 UI 重构**：不改任何端口名、命令名、持久化键、算法、`OnConfirm`、`Initialize`、`Dispose` 语义。
设计稿的交互只演示状态切换；WPF 复刻只接现有命令与公开方法，不新增功能。

## 2. 现状审查（改动理由）

| # | 现状 | 影响 |
| --- | --- | --- |
| 1 | 左栏 5 张卡片 + 图像输入卡纵向堆叠，参数行统一 `PluginControlHeight=24` | 19 个数值字段在 960 高度下需要长距离滚动；24px 控件低于 V2 的 36px 节奏，窄档可读性差 |
| 2 | 右栏用 `ListBox` 页签；图像页签的外层 `Grid` 用 `Visibility=Collapsed` 切换 | 切到「数据输出」会把已设图的 `ImageReadOnly` 折叠；再切回存在"不重绘"风险（与 ImageAcquisition V2 踩过的坑同族） |
| 3 | 预览只有 `ImageReadOnly` + 一行状态，没有空态/错误态 | 无图时是一块纯黑画布；失败原因只在底部文字里，操作员不知道下一步做什么 |
| 4 | 直方图与缺陷清单挤在「数据输出」的滚动区，且无固定预览头/工具条 | 图像视图独占高度，但缺少标题、计数、适应窗口/1:1 入口；数据视图与图像视图切换成本高 |
| 5 | 缺陷清单 `DataGrid` 使用框架默认样式（字号 12、行高偏大、列头灰底） | 8 列在 550px 宽预览区里显得拥挤；与 V2 的 11px 等宽数字节奏不一致 |
| 6 | 状态栏只有 `StatusMessage` 文本，颜色靠 `DataTrigger` 切换 | 缺少状态点与右侧直方图位深提示；三档语义没有视觉锚点 |
| 7 | 当前 XAML 已经做到 hex=0 与 `Plugin*` 令牌收编（2026-10-04 试点） | 这是本插件的基础优势，本次不得回退；新增颜色只能走公共令牌 |

> 现状标记计数：`h:` 控件 2 个（`ImageReadOnly`、`HistogramPlot`）；`LinkableValueEditor` 3 个；
> `<ui:NumericBox` 开标签 19 个（含 `.Value` 子标签共 38 个标签、98 处 `NumericBox` 文本引用）；
> `<DataGrid*` 标签 12 个（1 个 DataGrid + 8 个 `DataGridTextColumn` + `DataGrid.Columns` 等）；
> `ComboBox` 4 个（二值化方式 / 检测目标 / 输出排序 / 运行显示窗口）；`CheckBox` 2 个（亮暗同检 / 填孔）。

## 3. V1 方向

> 预览优先、一屏调参；参数分区固定顺序；两块画布都常驻；数据输出不抢图像高度。

1. **两栏固定分工**：左 356px 配置轨（图像输入 + ①~⑤ 五区），右侧预览工作区自适应（960 时约 548px）。
2. **参数节奏统一**：标签 11.5px、控件 32px（Web 36px，WPF 为 640×560 最小档压缩 4px）、帮助 11px；字段间距 10px；19 个数值字段按 2 列紧凑排布。
3. **二值化方式驱动分区**：固定阈值显示上下限；自动阈值只显示说明；动态阈值显示 4 个局部参数；检测目标/亮暗同检按 `ShowDetectTarget` / `ShowDualPolarity` 显隐。
4. **预览是工作区**：预览头（标题 + 缺陷计数 + 适应窗口 / 1:1）+ 页签条（图像视图 / 数据输出）+ 深色画布 + 状态栏；空态/错误态齐备。
5. **图像画布常驻**：`ImageReadOnly` 不做 `Visibility=Collapsed`；页签切换只覆盖数据层，不折叠图像控件。空态/错误态是纯 WPF 覆盖层。
6. **直方图保留在数据页签**：`HistogramPlot` 的 4 个绑定原样；固定阈值下仍显示阈值标记带。
7. **缺陷清单可读性**：DataGrid 8 列、11px 等宽数字、行高 30、交替行底色；点行仍触发 `DefectList_SelectionChanged`。
8. **状态三档**：Info=绿 / Warning=橙 / Error=红；空串显示「就绪」；不新增第四档。
9. **全令牌化**：`Dialog*` / `Plugin*` 公共令牌；视图 XAML hex=0；不改共享字典、共享控件、宿主外壳。
10. **五态与可达性**：hover / pressed / focus / disabled 全量；图标按钮 30×30 + `Padding="0"`（R68）；焦点环 `DialogFocusVisual`；ToolTip `DialogToolTipStyle`（R135）。

## 4. 设计要点（完整清单）

1. **图像输入卡**：3 个 `LinkableValueEditor`（`SrcImage` / `MaskRegion` / `ExcludeRegion`），每个只包裹、不改内部；「按当前图像重新适配」绑定 `AdaptToImageCommand`。
2. **① 二值化**：`ThresholdModes` 下拉 + `EnumName` 转换器；固定/自动/动态三个参数块按 `IsFixedThreshold` / `IsAutoThreshold` / `IsDynamicThreshold` 显隐；`DetectTargets` 下拉按 `ShowDetectTarget` 显隐；`DetectBrightAndDark` 勾选按 `ShowDualPolarity` 显隐。
3. **② 特征筛选**：面积下限/上限、圆度下限、长宽比上限、排除触边(px)，5 个字段两列排布；帮助文字说明"筛选 ≠ 判定"。
4. **③ 形态学**：开/闭/合并半径 3 个字段 + 填孔勾选；帮助文字说明填 0 = 跳过。
5. **④ 判定规格**：个数上/下限、单个缺陷面积上限、总面积上限；帮助文字说明任一命中即 NG。
6. **⑤ 输出**：运行显示窗口（0~9 int）、像素当量(mm/px)、输出排序（`DefectSortMode`）；帮助文字说明数组端口逐项对齐。
7. **预览头**：图像图标 + 「标注图预览」+ 缺陷计数胶囊 + 适应窗口 / 1:1（只接 `FitToWindow()` / `FitToImage()`）。
8. **页签条**：`PreviewTabLabels`（图像视图 / 数据输出）+ `SelectedPreviewTab`；右侧「改参数后自动刷新」提示。
9. **图像画布**：深色底（`PluginPreviewCanvasBrush`）+ 左上「标注图 · 只读」chip + 右下判定 chip（NG 红 / OK 绿）+ 空态/错误态覆盖层。
10. **数据页签**：缺陷清单 DataGrid（8 列，`DefectRows`）+ 灰度直方图（`Histogram` / `HistogramVisible` / `HistogramErrorVisible` / 阈值标记）+ 范围/位深文字。
11. **状态栏**：状态点 + `StatusMessage`（`StatusLevel` 三档变色）+ 右侧 `HistogramTypeText`。
12. **数字等宽**：缺陷清单、直方图范围、计数胶囊使用 tabular-nums；WPF 用 `Typography.NumeralAlignment="Tabular"`。
13. **窄档 640**：左栏 300px、右栏约 288px；帮助文字换行、预览头计数胶囊可省略、工具条保留。
14. **宿主外壳**：底部「状态: 待执行 / 耗时 / 执行 / 取消 / 确认」属于 `PluginConfigShellView`，本插件不改，设计稿仅作上下文展示。

## 5. 契约保留（V1 一个都不能改）

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 宿主靠 `viewObj.DataContext as IPluginConfigView` 取 `OnConfirm`；换独立 VM = 确认不保存 |
| C2 | 3 个 `LinkableValueEditor`：`SrcImage` / `MaskRegion` / `ExcludeRegion` | 端口持久化、类型转换、`ValueChanged` 订阅、`StepData.SetLink/RemoveLink`、变量绑定弹窗都在控件里 |
| C3 | `ImageReadOnly.HImage = PreviewImage` 常驻 | 保留 HALCON 显示/缩放/右键菜单；不用 Collapsed 显隐已设图的控件 |
| C4 | `HistogramPlot` 的 `Data` / `ShowThresholdMarkers` / `ThresholdLow` / `ThresholdHigh` / `Visibility` | 直方图是调阈值的唯一依据；绑定与控件类型都不能换 |
| C5 | 缺陷清单 `DataGrid`：`ItemsSource=DefectRows`、单选、8 列、`SelectionChanged=DefectList_SelectionChanged` | 行选中要转发 `HighlightDefect(row.Index)`，并在预览图上套黄框 |
| C6 | 19 个 `NumericBox` 的 `Value` 绑定、`Minimum/Maximum/DecimalPlaces`、`NumberValidationRule` | 数值范围、整数约束、非法输入回退都是既有行为；换控件会改变校验语义 |
| C7 | `ThresholdMode` / `DetectTarget` / `SortMode` 枚举，`DisplayViewIndex` int（0~9），`DetectBrightAndDark` / `FillHoles` bool | 持久化键与类型不能换；`DisplayViewIndex` 0=不显示、1~9=窗口号 |
| C8 | `AdaptToImageCommand`、`StatusMessage` / `StatusLevel` 三档 | 命令是现有功能入口；状态只有 Info / Warning / Error，不扩展 |
| C9 | `SelectedPreviewTab` / `PreviewTabLabels` | 页签导航的既有绑定；不改索引语义（0=图像，1=数据） |
| C10 | `DefectCountText` / `HistogramRangeText` / `HistogramTypeText` / `HistogramError` / `HistogramVisible` / `HistogramErrorVisible` | 纯展示属性，原样消费；不新增 VM 属性 |

## 6. 令牌映射

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--bd-surface` | `#FFFFFF` | `DialogSurfaceBrush` / `PluginSurfaceBrush` |
| `--bd-surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` / `PluginSubtleBackgroundBrush` |
| `--bd-surface-3` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--bd-border` | `#E2E8F0` | `DialogBorderBrush` / `PluginCardBorderBrush` |
| `--bd-border-soft` | `#EEF2F7` | `DialogBorderLightBrush` / `PluginDividerBrush` |
| `--bd-border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` |
| `--bd-text` | `#0F172A` | `DialogTextPrimaryBrush` / `PluginTitleTextBrush` |
| `--bd-text-2` | `#475569` | `DialogTextSecondaryBrush` / `PluginLabelTextBrush` |
| `--bd-text-3` | `#94A3B8` | `DialogTextMutedBrush` / `PluginHintTextBrush` |
| `--bd-text-4` | `#64748B` | `ScadaSecondaryTextBrush`（近似） |
| `--bd-primary` | `#0EA5E9` | `DialogAccentBrush` |
| `--bd-primary-strong` | `#0284C7` | `DialogAccentDarkBrush` |
| `--bd-primary-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--bd-primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--bd-primary-light` | `#E0F2FE` | `DialogAccentLightBrush` |
| `--bd-primary-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--bd-success` | `#22C55E` | `DialogSuccessBrush` / `PluginOkBrush` |
| `--bd-success-text` | `#15803D` | `DialogSuccessTextBrush` |
| `--bd-success-soft` | `#F0FDF4` | `DialogSuccessSubtleBrush` |
| `--bd-success-border` | `#BBF7D0` | `DialogSuccessBorderBrush` |
| `--bd-danger` | `#EF4444` | `DialogDangerBrush` / `PluginErrorBrush` |
| `--bd-danger-text` | `#B91C1C` | `DialogDangerTextBrush` |
| `--bd-danger-soft` | `#FEF2F2` | `DialogDangerSubtleBrush` |
| `--bd-danger-border` | `#FECACA` | `DialogDangerBorderBrush` |
| `--bd-warning` | `#F59E0B` | `DialogWarningBrush` / `PluginWarningBrush` |
| `--bd-warning-text` | `#B45309` | `DialogWarningTextBrush` |
| `--bd-warning-soft` | `#FFF4E5` | `DialogWarningSubtleBrush` |
| `--bd-warning-border` | `#FDE68A` | `DialogWarningBorderBrush` |
| 预览深色画布 | `#0B1220` | `PluginPreviewCanvasBrush` |
| 预览浮层 | `#1E293B` | `PluginPreviewSurfaceBrush` |
| 预览描边 | `#334155` | `PluginPreviewBorderBrush` |
| 预览文字 | `#E2E8F0` | `PluginPreviewTextBrush` |
| 预览弱文字 | `#94A3B8` | `PluginPreviewMutedTextBrush` |
| 直方图填充 | — | `PluginChartFillBrush` / `PluginChartStrokeBrush` |
| 直方图阈值带 | — | `PluginChartBandBrush` / `PluginChartMarkerBrush` |
| 直方图网格 | — | `PluginChartGridBrush` |
| 圆角 6/8/10/12/16 | — | `PluginCardCornerRadius`（8）+ 本地 `CornerRadius` 字面量 |
| 间距 4/8/10/12/16/20/24 | — | WPF `Margin` / `Padding` 字面量（仓内暂无间距令牌） |
| 字体 | Segoe UI / Microsoft YaHei UI | 同族系统字体；等宽用 `Consolas` / `PluginMonoFontFamily` |
| 图标 | 内联 SVG | `DialogIconFont`（Font Awesome 6 Pro Solid，字形语义一致） |

**缺失令牌**：本版未发现必须新增的公共令牌。所有颜色都落在既有 `Dialog*` / `Plugin*` / `Scada*` 语义域内。
若复刻时发现 `DialogRowHoverBrush` 在深色 chip 上对比度不足，只报告，不自行追加。

## 7. 复刻实现映射（已完成）

| 设计项 | WPF 实现 |
| --- | --- |
| 两栏 | `Grid` 两列：`1* MinWidth=300 MaxWidth=356` + `1.6* MinWidth=220` |
| 参数卡 | `PluginCard` 样式 + 本地 `SectionHeader` 模板；`PluginNumericBox` 派生 32px 样式 |
| 图像输入 | 3 个 `cv:LinkableValueEditor` + `AdaptToImageCommand` |
| 二值化分区 | `BoolToVis` + `IsFixedThreshold` / `IsAutoThreshold` / `IsDynamicThreshold` / `ShowDetectTarget` / `ShowDualPolarity` |
| 预览工作区 | 预览头 + `ListBox` 页签 + `ImageReadOnly` 常驻 + 空/错误覆盖层 + 状态栏 |
| 数据页签 | `DataGrid`（8 列）+ `h:HistogramPlot`（4 个绑定） |
| 工具条 | `FitWindow_Click` → `PreviewCanvas.FitToWindow()`；`FitImage_Click` → `PreviewCanvas.FitToImage()` |
| 状态栏 | `StatusMessage` + `StatusLevel` 三档；`StatusMessageConverter` 空串→「就绪」 |
| 令牌 | 视图 hex=0；只用公共令牌 |

### 复刻验证

| 项 | 命令 | 结果 |
| --- | --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.BlobDetect\Plugin.BlobDetect.csproj` | **0 错误 / 0 警告**；`Modules\Plugin.BlobDetect.dll` 已刷新 |
| 批量插件视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.BlobDetect` | **10/10 通过**（视图实例化 574 元素；3 个 Port；`AdaptToImageCommand`；`TextBox.Text` 绑定含 `StatusMessage`；`OnConfirm` InputValues=29；hex=0） |
| 渲染证据 | 同上 | `docs/UI设计基线/_batch-evidence/Plugin.BlobDetect-BlobDetectPlugin.png` → 已复制到 `evidence/after-render-v1-initial.png` |
| 常规 gate | `dotnet run --project FlowCanvasChecks --no-build` | 首次 **2690/1**（并行分支胶囊 `IsRunning` 时序红项，非本插件）；重跑 **2691/0**，`[Blob]` 段与视图样式收编契约全部通过 |

> 渲染证据边界：`ImageReadOnly` 是 HALCON `HwndHost`，`RenderTargetBitmap` 不捕获其内容；PNG 展示的是空态布局，真实标注图预览需真机窗口验证。

## 8. 已知偏差（复刻时按 R130 记录）

1. **控件高度**：Web 36px，WPF 用 32px，为 640×560 最小档保留滚动余量；宽度与两列排布不变。
2. **左栏宽度**：Web 356px，WPF 用 `1*` 300~356px 自适应；窄档 300px。
3. **图标**：Web 用内联 SVG（Lucide 风格），WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`）；形状不同、语义一致。
4. **阴影/动效**：Web 窗口多层阴影；WPF 用 `PluginCard` 的 `DropShadowEffect` 与 150~200ms 状态切换。
5. **预览图**：Web 用内联 SVG 工业板件示意；WPF 用真实 `HImage`（`ImageReadOnly`）。
6. **HALCON 截图边界**：`ImageReadOnly` 是 `HwndHost`，`RenderTargetBitmap` 不捕获其内容；渲染证据展示空态/错误态布局，真实图像预览需真机窗口。
7. **DataGrid**：Web 用 HTML table 展示 3 行示例；WPF 用只读 `DataGrid`，列宽按 8 列分配（36/70/60/66/56/56/64/64）。
8. **直方图**：Web 用 SVG 示意；WPF 用 `h:HistogramPlot` 矢量绘制，随窗口缩放重算。
9. **页签**：Web 用按钮组；WPF 用 `ListBox` 自绘 `ListBoxItem`（保留 `SelectedIndex` 双向绑定）。
10. **状态文案**：Web 示例为「预览已更新 · 检出 3 个缺陷，判定 NG」；WPF 由插件 `SetStatus` 写入，空串显示「就绪」。
11. **滚动条**：WPF 左栏滚动条约占 17px，窄档可用宽度比 Web 少约 17px。
12. **宿主外壳**：设计稿底部「执行/取消/确认」属于 `PluginConfigShellView`，插件视图不包含、不修改。
13. **判定 chip**：Web 稿在画布右下角有「NG · 3 个缺陷」chip；WPF 不额外造判定文本，判定由预览头 `DefectCountText` 胶囊 + 状态栏 `StatusMessage`（`StatusLevel` 变色）表达，避免与插件状态文案出现两份真相。
14. **状态消息控件**：WPF 用只读 `TextBox` 承载 `StatusMessage`（可选中复制错误信息），外观按 `TextBlock` 处理；`TextWrapping=Wrap` + 状态栏 `Height=Auto / MinHeight=40`，长消息与换行不会被裁；不改变状态语义与三档颜色。
15. **预览页签**：WPF 用 `ListBox` 自绘项，Web 用按钮组；`SelectedIndex` 仍双向绑 `SelectedPreviewTab`。

## 8.1 遗留问题核销（2026-10-20）

| 项 | 修复 |
| --- | --- |
| BD2 文档 | `docs/code-changes/2026-10-20-BlobDetect-Web化.md` 的 Modules 产物行改为核查实际值 `2026-10-20 03:31:56 / 190,464 bytes`；本次重建后的值同格注明 |
| BD3 状态栏 | 只读 `TextBox` 的 `TextWrapping` 改 `Wrap`，预览工作区状态行 `Height="Auto" MinHeight="40"`；长消息 / 换行不再被固定 40px 裁切 |
| BD4 空态计数 | 预览头计数胶囊增加 `PreviewImage == null` → `Visibility=Collapsed`；空态/错误态不显示「共 0 个」 |
| BD5 disabled 演示 | `design.html` 补 `:disabled` / `[disabled]` 样式（button/input/select/textarea，`opacity:.45; cursor:not-allowed`），与 WPF `IsEnabled=False` 对齐 |
| BD7 自动化语义 | 3 个 `LinkableValueEditor` 补 `AutomationProperties.Name`（输入图像 / 检测区域 / 排除区域），外置标签保留 |
| BD6 生命周期守卫 | `SchedulePreview` / `RefreshPreview` 首条 `if (_disposed) return;`；`Dispose` 置 `_disposed` 后先退订 `SrcImage` / `MaskRegion` / `ExcludeRegion` 的 `ValueChanged`，再停防抖/清缓存。守门：`FlowCanvasChecks\BlobDetectChecks.cs` 的 `[BD6]` 12 条（4 静态 + 8 运行时）。**边界**：入口守卫只阻止新任务，在途预览竞态仍为既有问题（见 `docs/code-changes/2026-10-20-BlobDetect生命周期守卫.md`） |

验证：`Plugin.BlobDetect` 构建 **0 错误 / 0 警告**（仅依赖工程重建时会输出既有 nullable 警告，非本插件新增）；`UIThemeSmokeTest --plugin-views Plugin.BlobDetect` **11/11 PASS**；`Modules\Plugin.BlobDetect.dll` 2026-10-20 08:21:35 / 189,952 字节。

BD6 复跑（2026-10-20）：`Plugin.BlobDetect` 构建 **0 错误 / 0 警告**；`--plugin-views Plugin.BlobDetect` **11/11 PASS**；`FlowCanvasChecks` 的 `[BD6]` **12/12 PASS**（整轮 2749 通过 / 0 失败）。

## 9. 未纳入本版（记录，不改）

- **加载骨架 / 蓝色加载态**：需要 VM 新增 `IsPreviewLoading` 或 `PreviewState`。
- **图像尺寸 / 文件大小**：需要 VM 新增只读属性。
- **放大 / 缩小按钮**：`HalconBase` 只有 `FitToWindow()` / `FitToImage()`，没有公开 zoom 方法。
- **打开所在文件夹 / 系统导航**：需要新增行为或宿主导航契约。
- **第四档状态级别**：`StatusLevel` 保持 Info / Warning / Error。
- **参数卡折叠**：本版所有分区常显；折叠属于交互增强，未纳入。
- **640×560 最小档的完整真机复核**：宿主 MinHeight 已统一 560，需真机检查左栏滚动与预览高度。

## 10. 变更规则

设计基线确认后只读。需要调整设计时升版本（v2），并同步更新已复刻的 `BlobDetectView.xaml`、本映射表与 `docs/code-changes/` 记录。
