# 胶路检测配置窗口 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v2 |
| 日期 | 2026-10-20（v1 同日；v2 为同日增量） |
| 确认状态 | v1 已复刻；v2 待用户确认 |
| 覆盖范围 | `Plugins\Plugin.BeadInspect\BeadInspectView.xaml` + `.xaml.cs`（插件内容区） |
| 宿主外壳 | `VisionMaster\Views\DialogViews\PluginConfigShellView.xaml`（960×680；最小 640×560，本基线不改宿主） |
| 设计文件 | `design.html`（自包含、无外部依赖、可点击；含默认 / 空态 / 错误 / 警告 / 拾取四点五态与 960/640 窄档） |
| 设计证据 | `evidence/design-default.png`、`design-narrow-640.png`、`design-empty.png`、`design-error.png`、`design-warn.png`、`design-quad.png`（浏览器截图，1440×1000）；**v2 新增** `design-v2-runparams.png`（④ 像素当量 + 输出对齐图）、`design-v2-extract-points.png`（① 抽稀目标点数，折叠区展开态）、`design-v2-rail-bottom.png`（⑤ 最低分数 / 金字塔层数） |
| 复刻状态 | v1 已完成（2026-10-20）；v2 已完成（2026-10-20）；WPF 批量冒烟证据 `docs/UI设计基线/_batch-evidence/Plugin.BeadInspect-BeadInspectPlugin.png`；真实 HALCON 预览需真机窗口 |

## 版本差异（v1 → v2）

v1 = 纯 UI 重构（视觉换新、契约零变更）。**v2 = 补 5 个"插件上早就存在、但界面没有入口"的属性**：

| # | 位置 | 属性 | 控件 | 区间 / 类型 | 落盘 |
| --- | --- | --- | --- | --- | --- |
| V1 | ④ 运行参数 第 3 行左 | `PixelSizeMm` | `TextBox` + `BeadInput` + `NumberValidationRule` | 0 ~ 1000 | `[StepConfig]`，落盘 |
| V2 | ④ 运行参数 第 3 行右 | `OutputAlignedImage` | `CheckBox` + `DialogCheckBox`，Content「输出对齐图」 | bool | `[StepConfig]`，落盘 |
| V3 | ⑤ 搜索范围 第 4 行左 | `MinScore` | `TextBox` + `BeadInput` + `NumberValidationRule` | 0 ~ 1 | `[StepConfig]`，落盘 |
| V4 | ⑤ 搜索范围 第 4 行右 | `PlanarNumLevels` | `TextBox` + `BeadInput` + `NumberValidationRule IntegerOnly` | 1 ~ 10 | `[StepConfig]`，落盘 |
| V5 | ① 配方库 →「自动提取输入（可选）」末行 | `ExtractTargetPoints` | `TextBox` + `BeadInput` + `NumberValidationRule IntegerOnly` | 10 ~ 500（默认 40） | **不落盘**（不是 `[StepConfig]`） |

- **不改插件**：5 个属性全部已在 `BeadInspectPlugin` / `BeadInspectPluginConfigView` 上存在，本版只加视图入口；不加 `[StepConfig]`、不改任何属性语义。
- **不改共享 UI**：全部复用视图既有局部样式（`BeadInput` / `BeadFieldLabel` / `BeadHint` / `BeadCard`）与公共 `DialogCheckBox`；**未新增任何令牌**。
- **不改落盘键数**：`InputValues` 仍是 **25** 个键（5 个输入端口 + 20 个 `[StepConfig]`），`ExtractTargetPoints` 不写键。
- **左栏仍可滚动**：`ScrollViewer` 结构未动；新增内容只在既有卡片内加行（④ 由 2 行扩到 3 行、⑤ 由 3 行扩到 4 行、① 折叠区内加 1 行）。

## 定位

按 `插件自定义视图Web化改造SOP.md` 的 §5.2：本文件是 Web 设计基线，**不进入构建、不部署**。视觉语言沿用
ImageAcquisition V2（两栏、卡片、12px 标签、11px 帮助文字、8~10px 圆角、公共令牌、五态）；本插件左栏
330px 且字段较多，表单输入按 **32px**、工具栏按钮按 **28~30px** 落地（见「已知偏差」第 9 条），
内容、字段、控件与状态按 **Plugin.BeadInspect 的实际视图**重画，不套用图像采集的字段。

## 现状盘点（耦合矩阵摘要）

> 详细逐项耦合矩阵见 `docs/code-changes/2026-10-20-BeadInspect-Web化.md` §3。

| 区域 | 现状 | 本次处理 |
| --- | --- | --- |
| 画布 | **2 块 HALCON**：主画布 `h:ImageEdit`（`DisplayImage` + `PathAnnotations`）、结果预览 `h:ImageReadOnly`（`PreviewImage`） | 两块都常驻，不折叠已设图控件；预览无图时只盖 WPF 空态层 |
| 点列表格 | **1 个 `DataGrid` / 4 列**（# / Row / Col / 删）；改造前 XAML 中 `DataGrid*` 标记共 18 处 | 保留 `ItemsSource=PointRows`、`Index/Row/Col` 列绑定、`CellEditEnding`、行删除 |
| 变量绑定 | **5 个 `LinkableValueEditor`**：`SrcImage` / `RecipeName` / `AlignedImageIn` / `RoiRegion` / `Transform` | 只包裹外观，`Port` / `StepData` 原样保留 |
| 配方库 | `ListBox`（`RecipeRows` / `EditingRow`）+ 新增 / 删除 / 设为默认 + 参考图路径 + 载入 + 自动提取输入折叠区；**v2 追加** 抽稀目标点数 | 保留全部绑定与事件，重排为左栏第一张业务卡；抽稀目标点数进折叠区末行 |
| 配方参数 | 胶宽 / 容差 / 位置 / 极性 + 高级显式目标矩形（4 个字符串值 + 应用按钮） | 保留全部绑定；目标矩形进折叠区 |
| 运行参数 | 对齐模式 / 输出单位 / 碎段过滤 / 显示窗口；**v2 追加** 像素当量 / 输出对齐图 | 保留全部绑定；显示窗口仍绑 int；v2 两个新入口落在第 3 行 |
| 搜索范围 | 起始角 / 角度范围 / 缩放 R/C 最小最大共 6 个；**v2 追加** 最低分数 / 金字塔层数 | 保留全部绑定；v2 后为 2 列 × 4 行 |
| 工具栏 | 学习 / 自动提取 / 撤销 / 清空点列 / 拾取矫正四点 / 清除矫正 / 适应图片 | 保留全部动作；改为视图级 `ICommand` 转发（只调插件现有公开方法） |
| 状态 | `StatusMessage` / `StatusLevel` 三档 + `ActionBanner` / `ActionBannerLevel` + `BaseImageHint` / `BaseImageLevel` | 原样消费；不加第四档 |

## 设计要点

1. **两栏固定分工**：左 330px 设置轨（可滚动），右侧画布工作区自适应；取消左右拖拽条，画布获得稳定余量。
2. **画布是唯一主操作面**：工具栏 + 结论横幅 + 画布标题（点数 / 适应图片）+ 操作提示 + 画布 hero 同卡；
   参数、点列、结果预览全部在左栏，画布不再被下方卡片挤压。
3. **配方库前置**：选型 → 载图 → 拾取的第一步；列表行同时显示名称、点数、学习状态（已学习 / 未学习 / 参数已变·需重学）。
4. **点列是紧凑复核表**：1 个 `DataGrid`，行高 28、单元格内边距 8,2；`Row` / `Col` 可编辑，提交后回写点列缓存。
5. **结果横幅常驻视口**：学习 / 自动提取 / 矫正的结论挂在工具栏正下方；空串折叠；三档颜色复用 `ActionBannerLevel`。
6. **状态条两行**：`StatusMessage`（左）+ `BaseImageHint`（第二行）；底图是输入图兜底时用 Warning 色提示坐标系风险。
7. **结果预览常驻**：`ImageReadOnly` 不再随无图折叠；无图时是纯 WPF 覆盖层，有图时控件从未折叠过，避免恢复后不重绘。
8. **全令牌化**：视图 XAML 零十六进制颜色；颜色全部映射到 `Dialog*` / `Plugin*` 公共令牌。
9. **五态与可达性**：hover / pressed / focus / disabled 全量；图标按钮 30×30 + `Padding="0"`（R68）；
   焦点走 `DialogFocusVisual`；ToolTip 走 `DialogToolTipStyle`（R135）。
10. **窄档**：960×680 默认；640×680 下左栏 300px、工具栏自动换行、状态条第二行允许折行；640×560 需真机复核左栏滚动。

## 令牌映射

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--bi-surface` | `#FFFFFF` | `DialogSurfaceBrush` / `PluginSurfaceBrush` |
| `--bi-surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` / `DialogInputBackgroundBrush` |
| `--bi-surface-3` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--bi-border` | `#E2E8F0` | `DialogBorderBrush` |
| `--bi-border-soft` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--bi-border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` |
| `--bi-text` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--bi-text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--bi-text-3` | `#94A3B8` | `DialogTextMutedBrush` |
| `--bi-text-4` | `#64748B` | `ScadaSecondaryTextBrush`（近似） |
| `--bi-primary` | `#0EA5E9` | `DialogAccentBrush` |
| `--bi-primary-strong` | `#0284C7` | `DialogAccentDarkBrush` |
| `--bi-primary-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--bi-primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--bi-primary-light` | `#E0F2FE` | `DialogAccentLightBrush` |
| `--bi-primary-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--bi-success` | `#22C55E` | `DialogSuccessBrush` |
| `--bi-success-text` | `#15803D` | `DialogSuccessTextBrush` |
| `--bi-success-soft` | `#F0FDF4` | `DialogSuccessSubtleBrush` |
| `--bi-success-border` | `#BBF7D0` | `DialogSuccessBorderBrush` |
| `--bi-danger` | `#EF4444` | `DialogDangerBrush` |
| `--bi-danger-text` | `#B91C1C` | `DialogDangerTextBrush` |
| `--bi-danger-soft` | `#FEF2F2` | `DialogDangerSubtleBrush` |
| `--bi-danger-border` | `#FECACA` | `DialogDangerBorderBrush` |
| `--bi-warning` | `#F59E0B` | `DialogWarningBrush` |
| `--bi-warning-text` | `#B45309` | `DialogWarningTextBrush` |
| `--bi-warning-soft` | `#FFF4E5` | `DialogWarningSubtleBrush` |
| `--bi-warning-border` | `#FDE68A` | `DialogWarningBorderBrush` |
| 预览画布底色 | `#0B1220` | `PluginPreviewCanvasBrush` |
| 预览表面 / 描边 / 文字 | `#1E293B` / `#334155` / `#E2E8F0` | `PluginPreviewSurfaceBrush` / `PluginPreviewBorderBrush` / `PluginPreviewTextBrush` |
| 预览弱文字 | `#94A3B8` | `PluginPreviewMutedTextBrush` |
| 卡片 / 表单 / 按钮 | — | `PluginCard` / `PluginCardTitle` / `PluginFieldLabel` / `PluginHintText` / `PluginButton` / `PluginAccentButton` |
| 输入框 / 下拉 | — | `DialogInput` / `DialogCombo` / `PluginNumericBox`（局部派生） |
| 等宽数字 | — | `PluginMonoFontFamily` + `Typography.NumeralAlignment="Tabular"` |

**本插件未新增公共令牌**：所需颜色均落在既有 `Dialog*` / `Plugin*` 语义域内（其中 `DialogBorderStrongBrush`、
`DialogSuccessBorderBrush`、`DialogDangerTextBrush`、`DialogDangerBorderBrush`、`DialogWarningBorderBrush` 与
`PluginPreview*` 由 ImageAcquisition V2 已追加）。如 reviewer 认为需要新的语义键，只报告、不自行追加。

## 契约保留（一个都不能改）

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 宿主靠 `viewObj.DataContext as IPluginConfigView` 取 `OnConfirm`；换独立 VM = 确认不保存 |
| C2 | 5 个 `LinkableValueEditor`：`SrcImage` / `RecipeName` / `AlignedImageIn` / `RoiRegion` / `Transform` | 端口持久化、类型转换、`ValueChanged` 订阅、`StepData.SetLink/RemoveLink`、变量绑定弹窗都在控件里 |
| C3 | `ImageEdit`：`HImage=DisplayImage`、`Annotations=PathAnnotations`；`ImageReadOnly`：`HImage=PreviewImage` | 保留 HALCON 显示 / 缩放 / 右键菜单；控件常驻，不折叠已设图控件 |
| C4 | 点列表格 `ItemsSource=PointRows`、`Index/Row/Col` 列绑定、`CellEditEnding`、行删除 | 表格与画布双向同步、撤销、学习指纹都依赖点列缓存 |
| C5 | `EditingEntry` 的胶宽 / 容差 / 位置 / 极性、6 个搜索范围、`MinErrorLength`、`DisplayViewIndex`、目标矩形四值；**v2 追加** `PixelSizeMm` / `OutputAlignedImage` / `MinScore` / `PlanarNumLevels` / `ExtractTargetPoints` | 全部是 `[StepConfig]` / 配方持久化字段（`ExtractTargetPoints` 是配置会话属性）；改名 = 静默丢配置 |
| C6 | 命令入口：学习 / 自动提取 / 撤销 / 清空点列 / 拾取矫正四点 / 清除矫正 / 载入 / 适应图片 / 新增 / 删除 / 设为默认 / 应用目标矩形 | 现有功能入口；视图级命令只转发到插件现有公开方法，不新增业务行为 |
| C7 | `StatusMessage` / `StatusLevel` 三档 + `ActionBanner` / `ActionBannerLevel` + `BaseImageHint` / `BaseImageLevel` | 状态语义只有 Info / Warning / Error；横幅与状态栏级别刻意分开 |
| C8 | **落盘键数 = 25**（5 输入端口 + 20 个 `[StepConfig]`）；`ExtractTargetPoints` 不写 `InputValues` | v2 只补界面入口，不许借机把会话态属性升成 `[StepConfig]`（那会改落盘契约与老方案兼容）；由 `[BeadInspect·v2]` 断言逐条钉住 |

## 验证记录（2026-10-20）

| 项 | 结果 |
| --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.BeadInspect\Plugin.BeadInspect.csproj`：**0 错误 / 0 警告**；`Modules\Plugin.BeadInspect.dll` 已刷新 |
| UI 冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.BeadInspect`：**11/11 PASS**（5 个 `LinkableValueEditor.Port`、11 个按钮有 `Command`、`OnConfirm` 可执行、**`InputValues=25`**、XAML hex=0、PNG 已生成）；`TextBox.Text 绑定` 明细已含 `PixelSizeMm` / `MinScore` / `PlanarNumLevels` |
| 常规 gate | `dotnet build FlowCanvasChecks` 后 `dotnet run --project FlowCanvasChecks --no-build`：**2749 通过 / 0 失败**；`[BeadInspect]` 段 **105 通过 / 0 失败**（含 v2 新增 10 条） |

> gate 复跑说明：本轮并发期间曾出现 5 条**与本插件无关**的红（`[M] 报警：状态门禁 → 清报警恢复` ×5、`[Blob] BD6` ×1、`[CC4] P6c` ×1），
> 均属虚拟运动卡的报警轮询时序与其它插件的并发改动；等那些改动落盘后第三次全量复跑为 **2749/0 全绿**。
> `[BeadInspect]` 段三次运行都是 **105/0**，不受影响。
| 行为探针 | `dotnet run --project _BeadViewProbe --no-build -- probe-fix __none__`：**全部通过**（D1-D5 + P1①-⑤；`pickCalls=1/2/3`；路径写回/取消/有效路径/失效路径/ToolTip 全 PASS） |
| 设计稿复核 | 960 默认态、640 窄档、空态、错误态、拾取四点态已浏览器截图核对；v2 三个新入口各补一张（`design-v2-runparams.png` / `design-v2-extract-points.png` / `design-v2-rail-bottom.png`）；状态切换可点击；无外部依赖 |

### v2 增量验证（2026-10-20）

| 项 | 结果 |
| --- | --- |
| 新增断言 | `FlowCanvasChecks\BeadInspectChecks.cs` 的 `Check18_NewParamEntries()`（**10 条**，`[BeadInspect·v2]` 前缀）：① 5 个绑定路径在位；② 4 个数值框各带 `NumberValidationRule`（Min/Max/IntegerOnly 逐条对齐）；③ 控件 → `UpdateSource` → 插件属性；④ 越界/非整数被规则挡下（源属性保持上次合法值）；⑤ `OnConfirm` 4 个 `[StepConfig]` 全落盘；⑥ `ExtractTargetPoints` 不写键；⑦ `InputValues` 仍是 25 键；⑧ 重开配置界面 `ExtractTargetPoints` 回到 40；⑨ 重开时 4 个 `[StepConfig]` 从 `InputValues` 回灌 |
| 落盘键数 | `InputValues` = **25**（5 输入端口 + 20 个 `[StepConfig]`），与 v1 一致 |
| 帮助手册 | `Help\02/03/04/11` 已同步；`--no-incremental` 重建后 `.Help.` 资源数 = **8**（详见 `Plugin.BeadInspect.csproj` 的 `Help\` 目录） |

**验证工具修复（P1，v1 沿革）**：Web 化后「载入」由 Click 改为 `Command="{Binding LoadRefCommand}"`；
`_BeadViewProbe` 原来用 `RaiseEvent(Button.ClickEvent)` 驱动，不会执行 Command，导致 `pickCalls` /
`RefImagePath` 断言假绿。已改为 `ClickLoadButton`：直接执行
`loadBtn.Command.Execute(loadBtn.CommandParameter)`；Command 为空直接抛错，不回退 RaiseEvent。
本次只改验证工具，未改生产代码。

## 遗留（v2）

1. **640×560 真机复核**：左栏在 v2 后多了 3 行（④ +1 行、⑤ +1 行、① 折叠区 +1 行 + 提示行），640×560 下左栏滚动可达性需真机确认（Web 稿只验证 640×680）。
2. **`ExtractTargetPoints` 的会话态语义**：重开配置界面回到 40 是既有行为（属性在插件上、不在 `InputValues`），v2 只在提示文案里写明；若将来要落盘，属**契约变更**，需另开版本（会动 `InputValues` 键数）。
3. **真机截图未做**：本批只交 Web 设计基线的浏览器证据与 WPF 批量冒烟 PNG；HALCON `HwndHost` 内容与真实交互态需真机窗口（`tools\capture-vm-window.ps1`）验收。

## 已知偏差（复刻时按 R130 记录）

1. **图标字体**：Web 用内联 SVG；WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`）。本视图按钮以文字为主，未额外造图标按钮。
2. **阴影 / 动效**：Web 窗口阴影为 CSS 多层阴影；WPF 用宿主现有 `DropShadowEffect`；动效统一 150~200ms。
3. **画布图像**：Web 用内联 SVG 工业零件 + 胶路示意；WPF 用真实 `HImage`。
4. **深色画布**：`#0B1220` 是设计选择；WPF 的 `ImageEdit` / `ImageReadOnly` 模板背景为黑色，外层容器用 `PluginPreviewCanvasBrush`，图像区域呈近黑。
5. **字体度量**：同字号下 Segoe UI / 微软雅黑与 Web 的字宽、行高略有差异，不按 px 硬套。
6. **滚动条**：WPF 左栏滚动条约占 17px，窄档可用宽度比 Web 少约 17px。
7. **`LinkableValueEditor` 内联标签**：WPF 控件自带 112px 左标签列（`Label` 属性），Web 稿用上方标签 + 输入框；本视图保留控件自带标签（不设 `Label=""`），因此输入框起点比 Web 稿靠右。
8. **点列表格**：WPF 使用共享 `DataGrid` 主题 + 局部 `BeadPointCell` / `BeadPointHeader` / `BeadPointEditBox` 收窄内边距；Web 表格是设计表达。
9. **控件高度**：ImageAcquisition V2 的输入框为 36~38px；本插件左栏 330px、字段多，输入框按 **32px**、
   工具栏按钮按 **28~30px**（Web 稿同），避免左栏过长与窄档工具栏过度换行。
10. **结果预览高度**：WPF 160px（Web 160px），比旧视图的 200px 略矮，以给左栏滚动留余量。
11. **拖拽分栏条**：旧视图有 6px `GridSplitter`；本版取消，左栏固定 330px，画布获得稳定余量。
12. **渲染证据范围**：`ImageEdit` / `ImageReadOnly` 是 HALCON `HwndHost`，`RenderTargetBitmap` 不捕获其内容；证据 PNG 展示的是 WPF 布局与空态，真实图像预览需真机窗口验证。
13. **极性显示**：WPF 下拉的 `ItemsSource` 仍是字符串 `dark` / `light`（持久化值），仅用显示转换器显示「暗胶」/「亮胶」；选中值仍是成员名。
14. **显示窗口**：仍绑 int 的 `DisplayViewIndex`；下拉项用 `ItemTemplate` 显示「不显示 / 图像窗口 N」，值不变。
15. **640×560**：宿主最小档已统一为 640×560；本稿主要验证 640×680，640×560 的左栏滚动与画布高度需真机复核。
16. **输出对齐图勾选框（v2）**：WPF 用 `DialogCheckBox`（15×15 方框、1.5px 描边、4px 圆角、选中填 `DialogAccentBrush` + 白勾）；
    Web 稿按同一几何复刻（`.bi-check-box`），但 Web 的勾是内联 SVG、WPF 是模板 `Path`，笔画端点细节略有差异。
17. **像素当量 / 最低分数 / 金字塔层数（v2）**：WPF 数值框是 `TextBox + BeadInput`（32px、等宽字体、Tabular 数字、`NumberValidationRule` 红框 + 中文提示）；
    Web 稿的 `input[type=text]` 无校验态表现，**校验态只在 WPF 侧成立**（设计稿不表达红框）。
18. **④ 第 3 行对齐（v2）**：右列是勾选框（内容高约 15px），用 `VerticalAlignment="Bottom"` 与左列输入框底对齐；Web 稿用 `justify-content:flex-end` 达到同一效果。
    勾选框本身**不带上方字段标签**（内容「输出对齐图」即标签），这是与左列「标签 + 输入框」结构的有意差异。
19. **抽稀目标点数（v2）**：WPF 在折叠区末行，下方常驻一行 `BeadHint`（11px 次要色）写「仅本次配置会话生效，不落盘」；
    Web 稿把同一句话并进字段帮助段（`bi-help`），文案一致、换行位置不同。

## 未纳入本版（记录，不改）

> 以下为**需要新增功能 / 契约**或**既有视图未暴露**的项，本版不混入纯 UI 改造。

- **加载骨架 / 蓝色加载态**：需要 VM 新增 `IsPreviewLoading` / `PreviewState`。
- **图像尺寸 / 文件大小**：需要 VM 新增只读属性。
- **放大 / 缩小按钮**：`HalconBase` 只有 `FitToWindow()` / `FitToImage()`，没有公开 zoom 方法。
- **打开所在文件夹 / 系统导航**：需要新增 `Process.Start` 或宿主导航契约。
- **拖拽分栏条**：本版取消；如需恢复，作为后续 UI 迭代单独评估。
- **真实 HALCON 预览像素**：需真机窗口验收。

> **v2 起已交付**（原列在此处，现移到上方「版本差异」表）：自动提取目标点数输入框（`ExtractTargetPoints`）、
> `OutputAlignedImage` / `PixelSizeMm` / `MinScore` / `PlanarNumLevels` 的界面入口。

## 审查修复（2026-10-20）

| 项 | 修复 |
| --- | --- |
| BI1 警告态截图 | 补 `evidence/design-warn.png`（1440×1000，浏览器渲染 `design.html` 的「警告」场景）；`design.html` 的五态现在都有独立截图 |
| BI3 数字等宽 | `BeadInput` 与 `BeadPointEditBox` 补 `FontFamily="{StaticResource PluginMonoFontFamily}"` + `Typography.NumeralAlignment="Tabular"`，覆盖全部配方参数 / 运行参数 / 搜索范围输入框与点列 `Row`/`Col` 编辑框；不改任何数值绑定或校验规则 |

验证：`Plugin.BeadInspect` 构建 **0 错误 / 0 警告**；`UIThemeSmokeTest --plugin-views Plugin.BeadInspect` **11/11 PASS**；`Modules\Plugin.BeadInspect.dll` 2026-10-20 07:44:44 / 196,096 字节。

## 变更规则

设计基线确认后只读。需要调整设计时升版本（v3），并同步更新已复刻的 `BeadInspectView.xaml`、本映射表与
`docs/code-changes/2026-10-20-BeadInspect-Web化.md`。

v2 的升版范围仅限「补 5 个既有属性的界面入口」，不改 v1 确立的布局、令牌与交互；v1 的视觉决策全部继续有效。
