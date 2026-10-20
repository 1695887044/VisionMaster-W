# 卡尺测量配置窗口 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 日期 | 2026-10-20 |
| 确认日期 / 人 | 待用户确认 |
| 覆盖范围 | `Plugins\Plugin.CaliperMeasure\CaliperMeasureView.xaml`（插件内容区）；宿主外壳 `PluginConfigShellView.xaml` 不改 |
| 窗口尺寸 | 960×680 默认；640×680 窄档；宿主最小 **640×560**（2026-10-20 统一提高） |
| 设计基线文件 | `design.html`（v1，自包含、内联 SVG、可点击；含默认态 / 关键状态 / 边界态） |
| 复刻状态 | 已完成（2026-10-20）；含 reviewer P1/P2 修复（HwndHost 信息条/预览头胶囊、根 MinWidth 560）；批量冒烟证据见 `docs/UI设计基线/_batch-evidence/Plugin.CaliperMeasure-CaliperMeasurePlugin.png` |
| 参考实现 | `docs/UI设计基线/image-acquisition/` v2 + `docs/code-changes/2026-10-20-图像采集配置窗口V2复刻与FluentChipDanger重复键修复.md` |
| 对应改造记录 | `docs/code-changes/2026-10-20-CaliperMeasure-Web化.md` |

## 定位

按 `AGENTS.md` §30.1 与 `插件自定义视图Web化改造SOP.md`：本文件是**第 ① 步（Web 设计基线）**的产物。
Web 只是设计媒介，**不进入构建、不部署**（R126）。确认后按本基线做第 ③ 步（WPF 复刻），
第 ④ 步以「同一屏、同一状态」的截图对照验收（R129/R132）。

本插件属于 SOP 清单里的 **M 档（单画布 / ROI / 数值参数）**：一个 HALCON `ImageEdit` 画布，
一个 `LinkableValueEditor`（`SrcImage`），10 个 `[StepConfig]` 数值/枚举参数，外加搜索区列表与参数微调。

## 现状审查（改动理由）

| # | 现状（改前） | 影响 |
| --- | --- | --- |
| 1 | 左栏 `340px` 固定宽 + 五张 `PluginCard` 纵向堆叠（图像输入 / ① 测量类型 / ② 搜索区与卡尺 / ③ 找边参数 / ④ 拟合参数 / ⑤ 判定与标定） | 卡片边框层层叠叠、信息密度低；窗口矮时「判定与标定」被推到最底部，必须长距离滚动 |
| 2 | 测量类型是裸 `ComboBox`，类型之间的差异只靠一行 `RegionCountHint` 说明 | 五种类型的用途、需要几个搜索区、是否涉及拟合，不能在选之前一眼看出 |
| 3 | 搜索区列表与参数微调分散在两张卡片里，列表行只有 `DisplayText` | 用户选中搜索区后，参数行与列表的对应关系弱；删除/重置按钮与列表分离 |
| 4 | 数值参数统一 `Height=24` 的小 `TextBox`，标签 11~12px 灰字 | 控件密度偏小、层级弱；与 V2 基线的 36px 表单节奏不一致 |
| 5 | 右侧只有「标注图预览」卡片 + 信息栏，无工作区头、无空态/错误态、无工具条 | 无图时整块白底；找不到边时只有底部一行红字，用户不知道下一步 |
| 6 | 视图内颜色依赖 `Plugin*` 旧令牌与局部语义色（`PluginOkBrush` / `PluginErrorBrush` 等），未统一到 Dialog 令牌 | 换肤/统一调整时改不动；与已完成 V2 的插件视觉不一致 |
| 7 | 状态栏只有一行文字，没有级别点、没有数量摘要 | 判定 OK/NG 与「卡尺 5 把 / 搜索区 1 个」不能同屏确认 |

## v1 方向

> **输入 → 类型 → 搜索区 → 找边 → 拟合 → 判定**，一屏完成；**只做纯 UI 重构，不改功能入口与绑定**。

1. **两栏固定分工**：左 356px 配置轨（单一滚动区 + 编号分区），右侧标注图工作区自适应。
2. **测量类型是 5 张卡片**：图标 + 中文名 + 用途/搜索区数量；仍是 `RadioButton` + `EnumConverter` 写 `MeasureKind`。
3. **搜索区与卡尺同卡**：卡尺数、搜索区列表、删除/重置、参数微调四块放在同一分区；列表选中仍绑 `SelectedRegion`。
4. **表单节奏统一**：标签 12px、主控件 36px、帮助 11.5px；参数微调行 30px 保持密度。
5. **标注图升级为工作区**：预览头 + 常驻 `ImageEdit` + 状态栏；空态/错误态是**画布上方的信息条**（不在 `ImageEdit` 的 Grid 内放 WPF 覆盖层），判定胶囊在预览头；画布不折叠。
6. **状态栏承载判定**：`StatusMessage` + `StatusLevel` 三档原样消费；右侧只读摘要 `CaliperCount` / `CaliperRegions.Count`。
7. **全令牌化**：颜色映射 `PluginConfigColors.xaml` / `Colors.xaml`；只在本视图派生样式，不改共享字典。
8. **五态与可达性**：hover / pressed / focus / disabled 全量；图标按钮 30×30 且 `Padding="0"`（R68），焦点环 `DialogFocusVisual`，ToolTip `DialogToolTipStyle`（R135）。

## 设计要点（完整清单）

1. **图像输入**：`LinkableValueEditor` 只包裹、不改内部；`Port="{Binding SrcImage}"`、`StepData="{Binding StepData}"`；外置标签「输入图像」；下方「重置搜索区」绑 `ResetRegionsCommand`。
2. **测量类型**：5 张卡片（宽度/间隙、两点距、点到线距、圆直径、角度）；选中态 = 主色描边 + 主色浅底 + 右侧对勾；每张卡副标题写明「几个区 + 是否拟合」。
3. **类型提示**：`RegionCountHint` 原文显示在蓝色提示条；搜索区数量不足时切换为橙色警示（视图内 DataTrigger 或文本前缀判断，不改 VM）。
4. **搜索区与卡尺**：卡尺数 `NumericBox`（1~1000，整数）；搜索区列表 `ListBox`（`DisplayMemberPath=DisplayText`）；删除/重置按钮；参数微调 `ItemsControl` 绑 `SelectedRegion.ParamEntries`。
5. **找边参数**：平滑 Sigma（0.4~100）、边缘阈值（1~255）、极性、选择、插值方式；全部保留原 `NumberValidationRule` 与枚举绑定。
6. **拟合参数**：拟合方式 / 最大残差 / 最少点数 / 环宽；整段由 `IsFitMeasure` 控制显隐，环宽再由 `IsCircleMeasure` 控制显隐。
7. **判定与标定**：标准值 / 上公差 / 下公差 / 像素当量；保留「接线取值」与「零容忍默认」两段帮助文字。
8. **预览头**：卡尺图标 + 「标注图预览」+ 当前测量类型 + 「可拖拽」+ OK/NG 判定胶囊；工具条只有「适应窗口 / 1:1」。
9. **画布**：`ImageEdit` 常驻，深色底；判定胶囊在**预览头**（HwndHost 之外）；空态/错误态为**画布上方信息条**。
10. **空态**：画布上方橙色信息条显示 `StatusMessage` 原文（「输入图像为空…」），画布保持常驻；不在 `ImageEdit` 上覆盖 WPF 面板。
11. **错误态**：画布上方红色信息条显示 `StatusMessage` 原文（失败原因 + 下一步）；不加重试按钮，画布保持常驻。
12. **状态栏**：状态点 + `StatusMessage`（三档色）+ 右侧「卡尺 N 把 · 搜索区 M 个」；窄档隐藏右侧摘要。
13. **数字等宽**：卡尺数、索引、参数值、状态摘要使用 tabular-nums。
14. **底部宿主栏**：宿主 `StatusText` + 耗时 + 执行 / 取消 / 确认；插件视图不改它。

## 契约保留（v1 一个都不能改）

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 宿主靠 `viewObj.DataContext as IPluginConfigView` 取 `OnConfirm`；换独立 VM = 确认不保存 |
| C2 | `SrcImage` 的 `LinkableValueEditor` | `Port` / `StepData` 是端口持久化、类型转换、变量链接、解除链接的唯一入口 |
| C3 | `ImageEdit` 三条绑定 | `ActiveRoi="{Binding CanvasActiveRegion, Mode=TwoWay}"`、`DrawObjectList="{Binding CanvasRegions}"`、`HImage="{Binding PreviewImage, Mode=OneWay}"`；画布常驻，不许用 `Visibility=Collapsed` 显隐已设图控件 |
| C4 | `MeasureKind` / `SelectedRegion` / `CaliperRegions` | 枚举 / `CaliperRegion` 对象 / `ObservableCollection`；类型不能换 |
| C5 | 全部数值参数 | `CaliperCount` / `Sigma` / `EdgeThreshold` / `FitMaxError` / `FitMinPoints` / `AnnulusWidth` / `StandardValue` / `UpperTolerance` / `LowerTolerance` / `PixelSizeMm` 的绑定、范围与 `NumberValidationRule` 不变 |
| C6 | 命令与生命周期 | `DeleteSelectedRegionCommand` / `ResetRegionsCommand` / Del 快捷键 / `OnViewLoaded()`；不新增业务命令 |
| C7 | `StatusMessage` / `StatusLevel` | 只有 Info / Warning / Error 三档；不扩展、不伪造 |

## 令牌映射

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--cm-surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--cm-surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--cm-surface-3` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--cm-border` | `#E2E8F0` | `DialogBorderBrush` |
| `--cm-border-soft` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--cm-border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` |
| `--cm-text` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--cm-text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--cm-text-3` | `#94A3B8` | `DialogTextMutedBrush` |
| `--cm-text-4` | `#64748B` | `ScadaSecondaryTextBrush`（近似） |
| `--cm-primary` | `#0EA5E9` | `DialogAccentBrush` |
| `--cm-primary-strong` | `#0284C7` | `DialogAccentDarkBrush` |
| `--cm-primary-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--cm-primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--cm-primary-light` | `#E0F2FE` | `DialogAccentLightBrush` |
| `--cm-primary-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--cm-success` | `#22C55E` | `DialogSuccessBrush` |
| `--cm-success-text` | `#15803D` | `DialogSuccessTextBrush` |
| `--cm-success-soft` | `#F0FDF4` | `DialogSuccessSubtleBrush` |
| `--cm-success-border` | `#BBF7D0` | `DialogSuccessBorderBrush` |
| `--cm-danger` | `#EF4444` | `DialogDangerBrush` |
| `--cm-danger-text` | `#B91C1C` | `DialogDangerTextBrush` |
| `--cm-danger-soft` | `#FEF2F2` | `DialogDangerSubtleBrush` |
| `--cm-danger-border` | `#FECACA` | `DialogDangerBorderBrush` |
| `--cm-warning` | `#F59E0B` | `DialogWarningBrush` |
| `--cm-warning-text` | `#B45309` | `DialogWarningTextBrush` |
| `--cm-warning-soft` | `#FFF4E5` | `DialogWarningSubtleBrush` |
| `--cm-warning-border` | `#FDE68A` | `DialogWarningBorderBrush` |
| `--cm-canvas` | `#0B1220` | `PluginPreviewCanvasBrush` |
| `--cm-canvas-2` | `#1E293B` | `PluginPreviewSurfaceBrush` |
| `--cm-canvas-border` | `#334155` | `PluginPreviewBorderBrush` |
| `--cm-canvas-text` | `#E2E8F0` | `PluginPreviewTextBrush` |
| `--cm-canvas-muted` | `#94A3B8` | `PluginPreviewMutedTextBrush` |
| 图标字体 | — | `DialogIconFont`（Font Awesome 6 Pro Solid）；类型卡片图标用内联 `Path` 几何 |
| 等宽字体 | Cascadia Mono / Consolas | `Consolas`（视图内字面量，沿用参考实现） |
| 圆角 6/8/10/12/16 | — | WPF `CornerRadius` 字面量（仓内暂无圆角令牌字典） |
| 间距 4/8/12/16/20/24 | — | WPF `Margin` / `Padding` 字面量 |

**不新增颜色语义**：所有颜色落在既有 `Dialog*` / `PluginPreview*` / `Scada*` 语义域内。
**本插件不需要新增公共令牌**；视图 XAML 静态扫描 `hex=0`。

## 复刻实现（第 ③ 步，已完成）

| 设计项 | WPF 实现 |
| --- | --- |
| 两栏布局 | `Grid`：左 `1*`（`MinWidth=300` / `MaxWidth=356`）+ 右 `1.6*`（`MinWidth=240`） |
| 图像输入 | `cv:LinkableValueEditor Label="" Port="{Binding SrcImage}" StepData="{Binding StepData}"` |
| 测量类型卡片 | 5 个 `RadioButton` + `ModeCardStyle`；`IsChecked` 走 `ui:EnumConverter` 写 `MeasureKind` |
| 搜索区列表 | `ListBox`：`ItemsSource="{Binding CaliperRegions}"`、`SelectedItem="{Binding SelectedRegion}"`、`DisplayMemberPath="DisplayText"` |
| 参数微调 | `ItemsControl ItemsSource="{Binding SelectedRegion.ParamEntries}"`；`TextBox Text="{Binding Value, UpdateSourceTrigger=LostFocus}"` |
| 卡尺数 / 找边 / 拟合 / 判定 | `ui:NumericBox` 保留原 `Value` 绑定、`Minimum`/`Maximum`/`DecimalPlaces` 与 `cv:NumberValidationRule`；枚举下拉保留 `ItemsSource`/`SelectedItem` |
| 拟合参数显隐 | `Visibility="{Binding IsFitMeasure, Converter={StaticResource BoolToVis}}"`；环宽再绑 `IsCircleMeasure` |
| 预览工作区 | `Border` + 44px 头 + 画布上方信息条（Auto 行）+ `h:ImageEdit`（常驻）+ 40px 状态栏；判定胶囊在预览头，空/错误态在信息条 |
| 预览工具条 | 「适应窗口」→ `ImageEditor.FitToWindow()`；「1:1」→ `ImageEditor.FitToImage()`（既有公开方法） |
| 状态栏 | `StatusMessage` / `StatusLevel` 三档；右侧 `CaliperCount` 与 `CaliperRegions.Count` 只读摘要 |
| 令牌 | 视图零硬编码十六进制；全部走公共令牌 |

### 复刻约束（已遵守）

1. **保留视图构造与生命周期**：`CaliperMeasureView()` 默认构造 + `Loaded` 调 `plugin.OnViewLoaded()`；`GetConfigView` 仍 `new CaliperMeasureView { DataContext = this }`。
2. **`LinkableValueEditor` 只包裹不改内部**：保留 `Port` / `StepData`；未新增 `BrowseCommand`（图像输入没有浏览语义）。
3. **`ImageEdit` 常驻**：不折叠、不替换；`ActiveRoi` / `DrawObjectList` / `HImage` 三条绑定逐字保留；空态/错误态是画布上方信息条，不隐藏画布；**不在 `ImageEdit` 的 Grid 内放 WPF 覆盖层**（HALCON `HwndHost` airspace，真机会被 HWND 盖住）。
4. **全部数值参数绑定不变**：属性名、类型、`Minimum`/`Maximum`/`DecimalPlaces`、`UpdateSourceTrigger` 与校验规则保持；只改外观与排列。
5. **搜索区交互不变**：列表选中、参数微调、删除/重置、Del 快捷键均走既有命令/属性；画布右键新建/删除仍由 `ImageEdit` 提供。
6. **不修改共享字典/控件**：`LinkableValueEditor`、`ImageEdit`、`HalconBase`、`PluginConfigStyles.xaml`、`VerTabControl` 未动。
7. **不新增功能**：加载骨架、尺寸/文件大小、放大/缩小、打开文件夹、系统导航均延后；预览工具条只接既有 `FitToWindow()` / `FitToImage()`。
8. **验证**：`dotnet build Plugins\Plugin.CaliperMeasure\Plugin.CaliperMeasure.csproj` 0/0；`UIThemeSmokeTest --plugin-views Plugin.CaliperMeasure` 结果见 code-changes。

## 已知偏差（复刻时按 R130 记录）

1. **图标**：Web 用 Lucide 内联 SVG；WPF 类型卡片用内联 `Path` 几何，工具条/状态图标用 Font Awesome 6 Pro Solid（`DialogIconFont`）。形状不同，语义一致。
2. **表单高度**：Web 主控件 36px；WPF 主控件 36px，参数微调行 30px（列表密度），搜索区列表行 8×5 padding。
3. **HALCON HwndHost airspace（必要偏差）**：`ImageEdit` 是 `HwndHost`，WPF 覆盖层会被 HWND 盖住（真机不可见/不可点击）。因此空态/错误态改为**画布上方信息条**，判定胶囊放在**预览头**，状态摘要放在**状态栏**；`ImageEdit` 的 Grid 内不再放任何 WPF 覆盖层/角标。Web 稿画在画布上的空态/错误态/判定胶囊在 WPF 中按此调整。
4. **预览图**：Web 用内联 SVG 工业零件示意图；WPF 用真实 `HImage`。`RenderTargetBitmap` 不捕获 HALCON `HwndHost`，证据 PNG 展示信息条/状态栏布局。
5. **深色画布**：`ImageEdit` 模板背景为黑色，外层容器用 `PluginPreviewCanvasBrush`，图像区域呈近黑。
6. **字体度量**：同字号下 Segoe UI / 微软雅黑与 Web 的字宽、行高略有差异，不按 px 硬套。
7. **滚动条**：WPF 左栏滚动条约占 17px，窄档可用宽度比 Web 少约 17px。
8. **工具提示**：Web 的 `title` 由浏览器提供；WPF 用 `DialogToolTipStyle`。
9. **状态栏右侧摘要**：Web 稿显示「卡尺 N 把 · 搜索区 M 个」；WPF 用两个 `TextBlock` 分别绑 `CaliperCount` 与 `CaliperRegions.Count`，不新增 VM 属性。
10. **类型提示的橙色警示**：已按 A 组遗留问题 CM1 落地——视图用 `IMultiValueConverter`（`CaliperRegions.Count` 与 `RequiredRegionCount`）判定数量不足，提示条切橙色（`DialogWarningSubtle/BorderBrush`）、图标切警告；不改 VM、不改测量逻辑。
11. **窄档 640×560**：根 `MinWidth` 改为本视图局部 `560`（宿主 640 内容区约 600）；`ActualWidth < 720` 时隐藏预览类型文字与状态摘要（code-behind 纯布局处理）。设计证据含 `evidence/design-preview-v1-640.png`（640×680 展示）；640×560 需真机复核左栏滚动与画布高度。

## 未纳入本版（记录，不改）

- **加载骨架 / 蓝色加载态**：需要 `IsPreviewLoading` / `PreviewState`，当前 `StatusLevel` 只有三档。
- **图像尺寸 / 文件大小**：需要 VM 新增只读属性（`HImage.GetImageSize`）。
- **放大 / 缩小按钮**：`HalconBase` 只有 `FitToWindow()` / `FitToImage()`，没有公开 zoom 方法。
- **打开所在文件夹**：需要新增 `Process.Start("explorer.exe", ...)`。
- **系统导航**：无此需求，也不新增宿主契约。
- **搜索区自动编号/重命名 UI**：当前 `CaliperRegion.Name` 由画布新建时生成，视图不新增重命名入口。
- **参数微调的撤销/重做**：属于业务/历史栈能力，不在 UI 改造范围。
- **拖拽入图**：图像输入来自上游端口/变量，不支持拖拽。

## 证据

| 项 | 结果 |
| --- | --- |
| 设计基线 | 本目录 `design.html`（v1） |
| 设计预览 | `evidence/design-preview-v1-default.png`（默认态）、`...-ng.png`（NG 判定）、`...-circle.png`（圆直径 + 拟合参数）、`...-empty.png`（空态信息条）、`...-error.png`（错误态信息条）、`...-640.png`（640 窄档） |
| 批量冒烟 PNG | `docs/UI设计基线/_batch-evidence/Plugin.CaliperMeasure-CaliperMeasurePlugin.png`（初始态，920×520 宿主） |
| 复刻证据 | 同上（批量冒烟生成）；`RenderTargetBitmap` 不捕获 HALCON `HwndHost`，真实图像预览需真机窗口 |
| 真机 | 待用户按 SOP §7 手工回环：打开配置 → 改每个字段 → 确认 → 重开；链接/解除链接；试运行；关闭无崩溃/泄漏 |

## A 组遗留问题修复（2026-10-20）

| ID | 修复 |
| --- | --- |
| CM1 | 新增 `RegionCountWarningConverter`（`IMultiValueConverter`），提示条 `Border.Style` 用 `MultiBinding` 判定 `CaliperRegions.Count < RequiredRegionCount`：不足时 `Background/BorderBrush` 切 `DialogWarningSubtleBrush/DialogWarningBorderBrush`，图标与文字切 `DialogWarningTextBrush`、图标字形 `f05a→f071`。只改视图层。 |
| CM2 | `StatusDotStyle` 默认 `Fill=DialogTextMutedBrush`；仅 `StatusLevel=Info` 且 `StatusMessage` 非空（`StatusDotSuccessConverter`）时绿色；Warning/Error 触发器保留。 |
| CM3 | 删除无引用资源 `LocalPrimaryButtonStyle`、`EmptyStateTitleStyle`、`EmptyStateSubStyle`、`EmptyStateIconStyle`；删除插件类无引用属性 `MeasureKinds` / `IsWidthMeasure` / `IsPointToPointMeasure` 及其通知（全仓 grep 确认仅定义处出现）。 |
| CM4 | `ToolbarTextButtonStyle` 的 `ContentPresenter` 补 `Margin="{TemplateBinding Padding}"`；`SelectedRegion.Name` 绑定补 `FallbackValue=选中搜索区后可编辑`（保留 `TargetNullValue`）。 |
| CM5 | 删除 `design.html` 的 `.cm-canvas-chip` 死 CSS 两行（640×560 真机项按约定不在本轮）。 |

验证：`dotnet build Plugins\Plugin.CaliperMeasure\Plugin.CaliperMeasure.csproj -v m` **0 错误 / 0 警告**；`--plugin-views Plugin.CaliperMeasure` **11/11 PASS**（`elements=439`）。

## 变更规则

设计基线确认后只读。需要调整设计时升版本（v2），并同步更新已复刻的 `CaliperMeasureView.xaml` 与本映射表。
