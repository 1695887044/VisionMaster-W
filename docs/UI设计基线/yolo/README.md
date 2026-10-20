# YOLO 目标检测配置窗口 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 日期 | 2026-10-20 |
| 确认日期 / 人 | 待用户确认 |
| 覆盖范围 | `Plugins\Plugin.Yolo\YoloView.xaml`（插件内容区）；宿主外壳 `PluginConfigShellView.xaml` 不改 |
| 窗口尺寸 | 960×680 默认；640×680 窄档；宿主最小 **640×560**（2026-10-20 统一提高） |
| 设计基线文件 | `design.html`（v1，自包含、内联 SVG、可点击） |
| 复刻状态 | 已完成（2026-10-20）；证据见 `evidence/after-render-v1-initial.png`（批量冒烟初始态）与 `docs/UI设计基线/_batch-evidence/Plugin.Yolo-YoloPlugin.png` |
| 参考实现 | `docs/UI设计基线/image-acquisition/` v2 + `docs/code-changes/2026-10-20-图像采集配置窗口V2复刻与FluentChipDanger重复键修复.md` |

## 定位

按 `AGENTS.md` §30.1 与 `插件自定义视图Web化改造SOP.md`：本文件是**第 ① 步（Web 设计基线）**的产物。
Web 只是设计媒介，**不进入构建、不部署**（R126）。确认后按本基线做第 ③ 步（WPF 复刻），
第 ④ 步以「同一屏、同一状态」的截图对照验收（R129/R132）。

## 现状审查（改动理由）

| # | 现状（改前） | 影响 |
| --- | --- | --- |
| 1 | 左栏 `340px` 固定宽 + 五张卡片纵向堆叠，`ScrollViewer` 从头滚到尾 | 模型信息、试算结果都在折叠区外，窗口矮时「试算一下」被推到底部不可见 |
| 2 | 所有参数用 24px 高的小 `TextBox`，标签 11px 灰字 | 控件密度偏小、层级弱；与 V2 基线的 36px 表单节奏不一致 |
| 3 | 模型摘要是裸 `TextBlock`（Consolas 11px），没有容器 | 多行形状/task/类别名与周围参数混在一起，选错模型时第一线索不突出 |
| 4 | 预览区只有 `ImageReadOnly` 铺满，无空态/错误态、无工具条 | 无图时整块白板；模型加载失败时用户不知道去哪查 |
| 5 | 视图内联颜色 `#...`（`PluginTitleTextBrush` 等旧键混用），未统一到 Dialog 令牌 | 换肤/统一调整时改不动 |
| 6 | 试算结果与提示都堆在「试算」卡片里，失败/0 目标/正常三态无视觉区分 | 现场排查（阈值太高、模型选错）时看不出重点 |

## v1 方向

> **输入 → 模型 → 参数 → 试算**，一屏完成；**只做纯 UI 重构，不改功能入口与绑定**。

1. **两栏固定分工**：左 356px 配置轨（输入 / 模型 / 检测参数 / 高级 / 试算），右侧预览工作区自适应。
2. **模型信息卡**：`ModelSummary` 放进等宽只读信息卡，输入/输出形状、`task`、类别名一眼可见。
3. **表单节奏统一**：标签 12px、控件 36px、帮助 11.5px；数值字段右对齐 + tabular-nums。
4. **高级参数折叠**：`InputSize` / `ClassFilterText` / `CropMargin` 收进原生 `Expander`，默认收起。
5. **预览工作区**：`ContentControl` + `DataTemplate` 按需创建 `ImageReadOnly`；空态 / 错误态齐备；工具条只接现有 `FitToWindow()` / `FitToImage()`。
6. **试算结果面板**：`ResultSummary` 独立成结果卡；正常 / 0 个目标 / 失败三态用语义色区分。
7. **全令牌化**：颜色映射 `PluginConfigColors.xaml` / `Colors.xaml`；只在本视图派生样式，不改共享字典。
8. **五态与可达性**：hover / pressed / focus / disabled 全量；图标按钮 30×30 且 `Padding="0"`（R68），焦点环 `DialogFocusVisual`，ToolTip `DialogToolTipStyle`（R135）。

## 设计要点（完整清单）

1. 图像输入：`LinkableValueEditor` 只包裹、不改内部；`Port="{Binding Image}"`、`StepData="{Binding StepData}"`；外置标签「输入图像」。
2. 模型路径：输入框 + 「浏览」按钮；`ModelPath` 绑定保持 `UpdateSourceTrigger=PropertyChanged`；支持相对路径说明常驻。
3. 模型信息：只读等宽卡，绑 `ModelSummary`；空模型显示「尚未选择模型」，加载失败显示「模型加载失败，详见提示」。
4. 检测参数：置信度阈值 / NMS 重叠阈值 / 最多保留目标数；每项一行标签 + 右对齐数值框 + 帮助文字。
5. 高级参数：输入尺寸（0 = 模型自带）/ 类别过滤（如 `0,2`）/ 裁剪边距；折叠区默认收起。
6. 试算：主按钮「试算一下」接现有 `TryPreviewDetect()`；结果卡绑 `ResultSummary`，最多 132px 高、内部滚动。
7. 预览头：模型文件图标 + 文件名（`ModelPath` 的文件名，空时「检测预览」）+ 「上游图像 · 只读」+ 适应窗口 / 1:1。
8. 画布：深色底 + 网格；左上「只读预览」角标；空态用虚线卡；错误态只给原因与下一步，不加重试按钮。
9. 状态栏：`Hint` 原文（不拆档、不伪造 `StatusLevel`）+ 右侧模型路径；窄档隐藏路径。
10. 底部宿主栏：宿主 `StatusText` + 耗时 + 执行 / 取消 / 确认；插件视图不改它。
11. 数字等宽：检测参数、耗时、索引类数值使用 tabular-nums。
12. 不新增按钮：预览工具条只做「适应窗口 / 1:1」；不做放大/缩小、打开文件夹、系统导航。

## 令牌映射

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--y-surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--y-surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--y-surface-3` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--y-border` | `#E2E8F0` | `DialogBorderBrush` |
| `--y-border-soft` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--y-border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` |
| `--y-text` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--y-text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--y-text-3` | `#94A3B8` | `DialogTextMutedBrush` |
| `--y-text-4` | `#64748B` | `ScadaSecondaryTextBrush`（近似） |
| `--y-primary` | `#0EA5E9` | `DialogAccentBrush` |
| `--y-primary-strong` | `#0284C7` | `DialogAccentDarkBrush` |
| `--y-primary-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--y-primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--y-primary-light` | `#E0F2FE` | `DialogAccentLightBrush` |
| `--y-primary-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--y-success` | `#22C55E` | `DialogSuccessBrush` |
| `--y-success-text` | `#15803D` | `DialogSuccessTextBrush` |
| `--y-success-soft` | `#F0FDF4` | `DialogSuccessSubtleBrush` |
| `--y-success-border` | `#BBF7D0` | `DialogSuccessBorderBrush` |
| `--y-danger` | `#EF4444` | `DialogDangerBrush` |
| `--y-danger-text` | `#B91C1C` | `DialogDangerTextBrush` |
| `--y-danger-soft` | `#FEF2F2` | `DialogDangerSubtleBrush` |
| `--y-danger-border` | `#FECACA` | `DialogDangerBorderBrush` |
| `--y-warning` | `#F59E0B` | `DialogWarningBrush` |
| `--y-warning-text` | `#B45309` | `DialogWarningTextBrush` |
| `--y-warning-soft` | `#FFF4E5` | `DialogWarningSubtleBrush` |
| `--y-warning-border` | `#FDE68A` | `DialogWarningBorderBrush` |
| 预览画布底 | `#0B1220` | `PluginPreviewCanvasBrush` |
| 预览画布表面 | `#1E293B` | `PluginPreviewSurfaceBrush` |
| 预览画布描边 | `#334155` | `PluginPreviewBorderBrush` |
| 预览画布文字 | `#E2E8F0` | `PluginPreviewTextBrush` |
| 预览画布次要文字 | `#94A3B8` | `PluginPreviewMutedTextBrush` |
| 图标字体 | — | `DialogIconFont`（Font Awesome 6 Pro Solid） |
| 等宽字体 | Cascadia Mono / Consolas | `Consolas`（视图内字面量，沿用参考实现） |
| 圆角 6/8/10/12/16 | — | WPF `CornerRadius` 字面量（仓内暂无圆角令牌字典） |
| 间距 4/8/12/16/20/24 | — | WPF `Margin` / `Padding` 字面量 |

**不新增颜色语义**：所有颜色落在既有 `Dialog*` / `PluginPreview*` / `Scada*` 语义域内。
**本插件不需要新增公共令牌**；视图 XAML 静态扫描 `hex=0`。

## 契约保留（v1 一个都不能改）

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 宿主靠 `viewObj.DataContext as IPluginConfigView` 取 `OnConfirm`；换独立 VM = 确认不保存 |
| C2 | `LinkableValueEditor`：`Image` 端口 | `Port` / `StepData` 是端口持久化、类型转换、变量链接、解除链接的唯一入口 |
| C3 | `ImageReadOnly.HImage = DisplayImage` | 保留 HALCON 显示/缩放/右键菜单；`DisplayImage` setter 在 UI 线程释放旧 `HImage` |
| C4 | 七个 `[StepConfig]` 属性 | `ModelPath` / `ConfidenceThreshold` / `NmsIoU` / `MaxDetections` / `InputSize` / `ClassFilterText` / `CropMargin`；类型与持久化键不变 |
| C5 | 命令与生命周期 | `OnBrowseModelClick` / `OnTryDetectClick` / `OnViewLoaded` / `TryPreviewDetect` / `ShowUpstreamImage` 行为不变 |
| C6 | `Hint` / `ResultSummary` / `ModelSummary` | 插件原样提供的三块文案；视图只做布局与语义色，不改文案与判定 |

> 本插件**没有** `Mode` / `SelectedCameraOption` / `DisplayViewIndex` / `StatusLevel`；本版不新增、不伪造。

## 复刻实现（第 ③ 步，已完成）

| 设计项 | WPF 实现 |
| --- | --- |
| 两栏布局 | `Grid`：左 `1*`（`MinWidth=300` / `MaxWidth=356`）+ 右 `1.6*`（`MinWidth=240`） |
| 图像输入 | `cv:LinkableValueEditor Label="" Port="{Binding Image}" StepData="{Binding StepData}"` |
| 模型路径 | `TextBox Text="{Binding ModelPath, UpdateSourceTrigger=PropertyChanged}"` + 「浏览」按钮（视图本地 `BrowseModelCommand`，调用既有对话框逻辑） |
| 模型信息卡 | `Border` + `TextBlock Text="{Binding ModelSummary}"`（Consolas 10.5） |
| 检测参数 | 三个 `TextBox` 分别绑 `ConfidenceThreshold` / `NmsIoU` / `MaxDetections` |
| 高级参数 | `Expander` 内三个 `TextBox` 绑 `InputSize` / `ClassFilterText` / `CropMargin` |
| 试算 | 「试算一下」按钮（视图本地 `TryDetectCommand`，调用既有 `TryPreviewDetect()`）+ 结果卡绑 `ResultSummary` |
| 预览工作区 | `ContentControl PreviewHost Content="{Binding DisplayImage}"` + `DataTemplate` 内 `h:ImageReadOnly HImage="{Binding Mode=OneWay}"`；空/错误态为纯 WPF 覆盖层 |
| 预览工具条 | 「适应窗口」→ `FindVisualChild<ImageReadOnly>(PreviewHost)?.FitToWindow()`；「1:1」→ `FitToImage()` |
| 状态栏 | `Hint` 原文 + 右侧 `ModelPath`；无 `StatusLevel`，不伪造第四档 |
| 令牌 | 视图零硬编码十六进制；全部走公共令牌 |

### 复刻约束（已遵守）

1. **保留视图构造与生命周期**：`YoloView()` 默认构造 + `Loaded` 调 `plugin.OnViewLoaded()`；`GetConfigView` 仍 `new YoloView { DataContext = this }`。
2. **`LinkableValueEditor` 只包裹不改内部**：保留 `Port` / `StepData`；未新增 `BrowseCommand`（图像输入没有浏览语义）。
3. **`ImageReadOnly` 保留**：`ContentControl` 按需创建，模板内 `HImage="{Binding Mode=OneWay}"`；未使用 `Visibility=Collapsed` 显隐已设图的控件。
4. **`ModelPath` 与七个参数绑定不变**：属性名、类型、`UpdateSourceTrigger` 策略保持；数值字段仍为 `TextBox`（未换成 `NumericBox`，避免改变输入行为）。
5. **试算与浏览行为不变**：视图本地命令只转发既有 `TryPreviewDetect()` 与文件对话框逻辑，不新增业务。
6. **不修改共享字典/控件**：`LinkableValueEditor`、`ImageReadOnly`、`HalconBase`、`PluginConfigStyles.xaml`、`VerTabControl` 未动。
7. **不新增功能**：加载骨架、尺寸/文件大小、放大/缩小、打开文件夹、系统导航、检测框叠加均延后。
8. **验证**：`dotnet build Plugins\Plugin.Yolo\Plugin.Yolo.csproj` 0/0；`UIThemeSmokeTest --plugin-views Plugin.Yolo` 全 PASS（见 code-changes）。

## 已知偏差（复刻时按 R130 记录）

1. **图标字体**：Web 用 Lucide，WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`）；形状不同，语义一致。
2. **阴影/动效**：Web 窗口阴影为 CSS 多层阴影；WPF 用宿主现有 `DropShadowEffect`；动效统一 150~200ms。
3. **预览图**：Web 用内联 SVG 工业零件示意图；WPF 用真实 `HImage`。`RenderTargetBitmap` 不捕获 HALCON `HwndHost`，证据 PNG 展示空态/错误态布局。
4. **深色画布**：`#0B1220` 是设计选择；`ImageReadOnly` 模板背景为黑色，外层容器用 `PluginPreviewCanvasBrush`，图像区域呈近黑。
5. **字体度量**：同字号下 Segoe UI / 微软雅黑与 Web 的字宽、行高略有差异，不按 px 硬套。
6. **滚动条**：WPF 左栏滚动条约占 17px，窄档可用宽度比 Web 少约 17px。
7. **工具提示**：Web 的 `title` 由浏览器提供；WPF 用 `DialogToolTipStyle`。
8. **模型摘要**：Web 稿为静态示例；WPF 由 `ModelSummary` 实时计算（`EnsureSession()`），选错模型时显示「模型加载失败，详见提示」。
9. **试算结果三态**：WPF 用 `ResultSummary` 文本前缀（`试算失败`）判断错误色；0 目标仍由插件文本原样表达，视图不新增 `StatusLevel`。
10. **窄档 640×560**：宿主 MinHeight 已统一 560；本设计以 640×680 展示，640×560 需真机复核左栏滚动与预览高度。

## 未纳入本版（记录，不改）

- **加载骨架 / 蓝色加载态**：需要 `IsPreviewLoading` / `PreviewState`，当前 `StatusLevel` 只有三档且本插件没有。
- **图像尺寸 / 文件大小**：需要 VM 新增只读属性。
- **放大 / 缩小按钮**：`HalconBase` 没有公开 zoom 方法。
- **打开所在文件夹**：需要新增 `Process.Start("explorer.exe", ...)`。
- **系统导航**：无此需求，也不新增宿主契约。
- **检测框叠加**：需要把 `Detections` 画进预览图，属业务/渲染改动；本版预览只显示上游原图。
- **类别名列表 / 类别选择器**：当前只做只读摘要；做可选列表需要新增 VM 只读集合。
- **拖拽入图**：图像输入来自上游端口/变量，不支持拖拽。

## 证据

| 项 | 结果 |
| --- | --- |
| 设计基线 | 本目录 `design.html`（v1） |
| 设计预览 | `docs/UI设计基线/yolo/evidence/design-preview-v1.png`（design.html 1360×900 截图） |
| 复刻证据 | `docs/UI设计基线/yolo/evidence/after-render-v1-initial.png`（初始态 920×520，批量冒烟生成） |
| 批量冒烟 PNG | `docs/UI设计基线/_batch-evidence/Plugin.Yolo-YoloPlugin.png`（初始态，920×520 宿主） |
| 真机 | 待用户按 SOP §7 手工回环：打开配置 → 改每个字段 → 确认 → 重开；链接/解除链接；试运行；关闭无崩溃/泄漏 |

## 变更规则

设计基线确认后只读。需要调整设计时升版本（v2），并同步更新已复刻的 `YoloView.xaml` 与本映射表。

## 2026-10-20 遗留项修复（YO1）

- 错误态标题「无法加载预览」的 `Foreground` 从 `DialogDangerTextBrush`（深红文字令牌）改为 `DialogDangerBrush`（`#EF4444` 危险色）：错误态画布是深色底（`PluginPreviewCanvasBrush`），深红文字对比度不足；图标已使用同一支 `DialogDangerBrush`，标题与其统一。只改颜色令牌，不改错误态布局与判定。
- 验证：`dotnet build Plugins\Plugin.Yolo\Plugin.Yolo.csproj -v m` → 0 错误 / 0 警告；`UIThemeSmokeTest --plugin-views Plugin.Yolo` → 11 条 PASS / 0 条 FAIL。
