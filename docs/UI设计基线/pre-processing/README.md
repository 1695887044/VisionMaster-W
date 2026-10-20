# 图像预处理配置窗口 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 日期 | 2026-10-20 |
| 确认状态 | 待用户确认（按 SOP §5.2：确认后只读，改动升 v2） |
| 覆盖范围 | `Plugins\Plugin.PreProcessing\PreProcessingView.xaml`（插件内容区）+ `PreProcessingView.xaml.cs`（仅 UI 处理器/转换器） |
| 窗口尺寸 | 默认 1120×680（内容区 1080×520）；窄档 960×680（内容区 920×520）；640×560（内容区 600×400）由根滚动容器兜底，横纵滚动、不裁切 |
| 设计文件 | `design.html`（v1，自包含、内联 SVG、可点击；含正常 / 空链 / 加载警告 / 含停用步四态，表格 / 卡片两种参数布局，1120 / 960 两档宽度） |
| 参考实现 | `docs/UI设计基线/image-acquisition/design.html` + `README.md`（V2 视觉语言） |
| 复刻状态 | 已完成 WPF 复刻；证据见 `evidence/after-render-v1-initial.png`（批量冒烟初始态 PNG）；真实 HALCON 画面需真机窗口 |

## 定位

本文件是 `docs/UI设计基线/插件自定义视图Web化改造SOP.md` 第 ② 步（Web 设计基线）的产物。
Web 只是设计媒介，不进入构建、不部署。确认后按本基线做 WPF 复刻，验收以「同一屏、同一状态」的截图对照为准。

## 现状审查（改动理由）

| # | 现状 | 影响 |
| --- | --- | --- |
| 1 | 三栏固定宽度 `240 / * / 360`，左栏只放「图像输入 + 算子库」，右栏把「处理链」与「参数」上下硬切 `2*/3*` | 预览列被两端挤压；窄窗口下算子库与参数面板同时拥挤 |
| 2 | 视图内仍有硬编码颜色：`#5B8FF9`（算子库分组）、`#AAAAAA`（步号）、`#D83B01`（加载警告）、`#1A7F4B`（操作反馈） | 违反「视图 XAML 十六进制 = 0」；换肤/统一调整时改不动 |
| 3 | 预览头只有「预览」+ 三个 RadioButton + 两个按钮；无预览目标说明、无状态栏 | 用户不知道当前看的是原图/输入图/输出图；`LoadWarning`/`ActionHint` 堆在画布底部，容易被忽略 |
| 4 | 处理链列表行无选中态之外的视觉层级，停用行靠 `Opacity=0.4` | 可读性一般；步号/启用/名称/摘要四层信息没有对齐节奏 |
| 5 | 参数面板标题与选中算子名左右分置，空态只有「在上方选中一个算子」 | 空态没有说明参数从哪来；卡片式切换时列宽 540 与左栏 240 的组合让预览只剩 308px |
| 6 | 图像输入使用 `LinkableValueEditor` 自带标签（内部硬编码 112px），左栏 240 时输入框只剩约 160px | 图像绑定显示不全（旧版已用外置标签缓解，但布局仍紧） |
| 7 | 操作反馈（复制/粘贴/导出）与加载警告都是裸 `TextBlock`，无底色/图标/层级 | 失败与成功同权重，现场容易漏看 |

## v1 方向

> **三栏工作区 + 预览优先 + 参数反射生成不变**；只做纯 UI 重构，不改端口、命令、持久化与业务逻辑。

1. **左 280px 配置轨**：上半「图像输入」（`LinkableValueEditor` + 外置标签），下半「算子库」分组列表；「显示窗口」固定在左栏底部。
2. **中间预览工作区**：预览头（标题 + 当前目标 + 适应窗口 / 1:1 / 刷新 / 导出）+ 目标分段（原图 / 输入图 / 算子输出）+ 警告/空态信息条 + 常驻 `ImageEdit` + 状态栏。
3. **右 360px（卡片式 520px）**：处理链列表 + 结构命令（上移/下移/删除/清空/复制整链/粘贴到链尾）；下方参数卡继续由 `FlatPropertyGrid` / `CardPropertyGrid` 反射生成。
4. **算子库是分组行**：组标题 + 可点击行；点击仍走 `AddOperatorCommand`，参数是 `OperatorDescriptor`。
5. **状态前置**：`LoadWarning` 变为琥珀色横幅；`ActionHint` 落在预览状态栏（空串显示「参数改动会自动重算预览」）。
6. **全令牌化**：颜色映射 `Colors.xaml` / `PluginConfigColors.xaml`；只在本视图派生样式，不改共享字典与共享控件。
7. **五态与可达性**：hover / pressed / focus / disabled 全量；图标按钮 30×30 且 `Padding="0"`（R68）；焦点环 `DialogFocusVisual`；ToolTip `DialogToolTipStyle`。

## 耦合矩阵（SOP §5.1）

| 区域/元素 | 当前绑定/契约 | 功能依赖 | 可重构级别 |
| --- | --- | --- | --- |
| 根布局 | `UserControl`，`MinWidth=560`；`MinHeight` 取 `PluginViewMinHeight`（360） | 宿主内容区尺寸；代码后置按需放大宿主窗口；根 `ScrollViewer` 兜底 | 只改内部布局，`MinWidth=560` 保留 |
| 图像输入 | `cv:LinkableValueEditor Port="{Binding SrcImage}" StepData="{Binding StepData}"` | `InputPort<HImage>` 持久化、变量链接、`ActualValue` 取图、`OnViewLoaded` 预览底图 | **只能包裹**，不改 `Port`/`StepData`/控件内部 |
| 显示窗口 | `ComboBox SelectedIndex="{Binding DisplayViewIndex}"`（int 0~9） | `[StepConfig] int`；运行时 `PublishPreview` 用 0 = 不显示 | 保留 int 与 `SelectedIndex` 绑定，只换外观 |
| 算子库 | `ItemsControl ItemsSource="{Binding OperatorLibrary}"`；按钮 `AddOperatorCommand` + `CommandParameter={Binding}` | `OperatorRegistry` 反射分组；`OperatorDescriptor.Factory` 造算子 | 保留 ItemsSource/命令/参数类型，只换分组与行样式 |
| 处理链列表 | `ListBox ItemsSource="{Binding Operators}" SelectedItem="{Binding SelectedOperator}"`；行内 `CheckBox IsChecked="{Binding Enabled}"`；`AlternationCount=1000` + `StepNumberConverter` | 选中项驱动参数面板与预览目标；`Enabled` 触发 `OnOperatorPropertyChanged` → `WriteChain` + 预览；`IsMuted` 控制停用行 | 保留 ListBox/SelectedItem/CheckBox/Alternation，行模板可重画 |
| 链结构命令 | `MoveUpCommand` / `MoveDownCommand` / `RemoveOperatorCommand` / `ClearChainCommand` | 集合变更 → `OnOperatorsChanged` → 写快照 + 预览 + 画布框同步 | 保留命令绑定，按钮位置可换 |
| 链搬运命令 | `CopyChainCommand` / `PasteChainCommand` / `ExportChainCommand` | 剪贴板 JSON、`SaveFileDialog` + `WriteExport`；`ActionHint` 反馈 | 保留命令与 `ActionHint`，只换按钮层级 |
| 预览目标 | 三个 `RadioButton IsChecked="{Binding ShowSourcePreview / ShowInputPreview / ShowOutputPreview}"` | `ApplyPreviewState` 唯一写入口；带框算子选中时切到输入图，框坐标基准才对 | 保留三个 bool 双向绑定与 GroupName |
| 预览画布 | `h:ImageEdit HImage="{Binding DisplayImage, Mode=OneWay}" DrawObjectList="{Binding CanvasRois}" ActiveRoi="{Binding CanvasActiveRoi, Mode=TwoWay}"` | HALCON 显示/缩放/右键菜单；框拖拽回写 `ApplyBoxTuples`；`DisplayImage` 切换由 VM 通知 | **常驻、不可折叠**；绑定逐字保留 |
| 删除编辑框 | `KeyBinding DeleteBoxCommand` | `CanExecute` 守卫焦点在 TextBox 时不触发 | 保留 InputBinding |
| 参数面板 | `ui:FlatPropertyGrid BindingObject="{Binding SelectedOperator}"` + `ui:CardPropertyGrid` 同一绑定，按 `IsFlatLayout`/`IsCardLayout` 切可见性 | `[SuperDisplay]` 反射生成；`CardPropertyGrid` 自带分组页签 | **不改共享控件内部**；只改容器/标题/空态 |
| 参数布局开关 | `IPropertyGridLayoutSwitch.UseCardLayout` → `plugin.IsCardLayout` + `ParamColumn.Width` | 宿主“卡片式”开关；列宽 360 ⇄ 520 | 保留接口与双向语义 |
| 状态/反馈 | `LoadWarning`（加载跳过提示）、`ActionHint`（复制/粘贴/导出结果） | 无 `StatusLevel`；两字段都是 `INotifyPropertyChanged` 只读属性 | 保留两字段；不加第四档/新状态属性 |
| 窗口尺寸 | `OnLoaded` → `EnlargeHostWindowIfNeeded`（只放大不缩小、不超工作区） | 三栏舒适宽度 | 保留方法；常量随新布局调整 |

## 设计要点（完整清单）

1. **三栏固定分工**：左 280px（输入 + 算子库）、中自适应预览、右 360px（链 + 参数）；卡片式参数列 520px。
2. **预览是工作区**：44px 预览头 + 40px 目标行 + 警告/空态信息条 + 深色画布 + 40px 状态栏。
3. **预览目标分段**：原图 / 输入图 / 算子输出；仍绑定三个 bool；带框算子提示「切到输入图拖框」。
4. **工具条只接现有入口**：适应窗口 `FitToWindow()`、1:1 `FitToImage()`、刷新 `RefreshPreviewCommand`、导出 `ExportChainCommand`；不做放大/缩小/打开文件夹。
5. **处理链行**：步号（1 起算）+ 启用勾选 + 名称 + 参数摘要；选中行主色浅底 + 主色描边；停用行整行 `Opacity=0.45`。
6. **链结构命令**：上移/下移/删除/清空一行；复制整链/粘贴到链尾一行；按钮 30px 高、字号 11.5。
7. **参数面板**：标题 + 选中算子名；表格式与卡片式共用同一 `SelectedOperator`；空态「在上方选中一个算子」。
8. **算子库分组**：7 个实际分组（色彩与通道 / 几何调整 / 滤波 / 灰度形态学 / 图像增强 / 二值化 / 边缘检测）；行悬停主色浅底，`+` 号从灰底变主色底。
9. **状态层级**：`LoadWarning` 琥珀横幅（有内容才显示）；`ActionHint` 状态栏（空串显示默认提示）；`Operators.Count` 在状态栏右侧显示「共 N 步」（启用数需 VM 新增只读属性，本版不做）。
10. **表单节奏**：标签 12px / 控件 36px / 帮助 11.5px；字段间距 12px；数字使用 tabular-nums。
11. **图标按钮**：30×30、`Padding="0"`；焦点环 `DialogFocusVisual`；ToolTip `DialogToolTipStyle`。
12. **窗口与滚动**：默认 1120×680；窄档 960×680；640×560 由根 `ScrollViewer` 横纵滚动兜底（见下节）。

## 尺寸与滚动（P1 修复）

**根布局自然尺寸**：三栏 `280 + 250 + 360 + 12 + 12 = 914`（卡片式 `280 + 250 + 520 + 24 = 1074`）；右栏最小高度 `240 + 8 + 200 = 448`。

| 宿主窗口 | 内容区（扣除 chrome 160 / 左右 40） | 表格式预览宽 | 卡片式预览宽 | 根滚动 |
| --- | --- | --- | --- | --- |
| 1120×680 | 1080×520 | **416** | **256** | 无（内容按视口拉伸） |
| 960×680 | 920×520 | **256** | **96**（不建议，窄档请用表格式） | 无（表格式）/ **有横向**（卡片式 1074 > 920） |
| 640×560 | 600×400 | —（内容 914 宽） | —（内容 1074 宽） | **横纵都有**（表 914×448 / 卡 1074×448） |

**实现**：
- 根 `UserControl MinWidth="560"`，根内容包 `ScrollViewer x:Name="RootScroll"`（`HorizontalScrollBarVisibility=Auto`、`VerticalScrollBarVisibility=Auto`、`CanContentScroll=False`）。
- 内层 `Grid` 的 `Width` 用 `MultiBinding` + `RootWidthConverter`：视口宽与 `IsCardLayout` 取 `max(视口, 表 914 / 卡 1074)`；`Height` 用 `MinSizeConverter` 夹到 `448`。视口更大时按视口拉伸，视口更小时保持自然尺寸并由外层滚动。**不能只加 `MinWidth/MinHeight`**：外层 ScrollViewer 会给内容无限测量空间，内层星号行列与左栏 ScrollViewer 会把 Extent 撑到 1120×1215（实测），正常窗口也长滚动条。
- `ShellChromeHeight = 72 + 56 + 16 + 16 = 160`（标题栏 72 + 底部栏 56 + ContentPresenter 上下 16+16）；`ShellChromeWidth = 20 + 20 = 40`；`ContentDesiredWidth/Height = 1080×520` → 目标窗口 **1120×680**。
- **HwndHost 边界**：640×560 下根滚动容器会滚动（表格式 914×448 / 卡片式 1074×448），`ImageEdit` 仍是常驻控件；WPF 的 HwndHost 在滚动容器中的裁剪由宿主窗口负责，真机若出现 HALCON 画面越界/不随滚动，按 reviewer 备选方案退化为 `MinWidth=960` 并在本 README 标注“不支持 640 宽”。当前按优先方案落地，布局探针已证明 600×400 视口不抛异常、双向滚动条可见。

**布局探针（临时工程，未入仓）**：

```text
640x560 表格式 (content 600x400)
  view Actual = 600 x 400
  Extent = 914 x 448; Viewport = 600 x 400
  HScroll = Visible; VScroll = Visible

960x680 表格式 (content 920x520)
  view Actual = 920 x 520
  Extent = 920 x 520; Viewport = 920 x 520
  HScroll = Collapsed; VScroll = Collapsed

960x680 卡片式 (content 920x520)
  view Actual = 920 x 520
  Extent = 1074 x 520; Viewport = 920 x 520
  HScroll = Visible; VScroll = Collapsed

640x560 卡片式 (content 600x400)
  view Actual = 600 x 400
  Extent = 1074 x 448; Viewport = 600 x 400
  HScroll = Visible; VScroll = Visible
```

## 令牌映射

> 全部为**既有公共令牌**，本插件**未新增任何令牌**。视图 XAML 十六进制颜色 = 0。

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--pp-surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--pp-surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--pp-surface-3` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--pp-border` | `#E2E8F0` | `DialogBorderBrush` |
| `--pp-border-soft` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--pp-border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` |
| `--pp-text` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--pp-text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--pp-text-3` | `#94A3B8` | `DialogTextMutedBrush` |
| `--pp-text-4` | `#64748B` | `ScadaSecondaryTextBrush`（近似；本视图只用于帮助文字） |
| `--pp-primary` | `#0EA5E9` | `DialogAccentBrush` |
| `--pp-primary-strong` | `#0284C7` | `DialogAccentDarkBrush` |
| `--pp-primary-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--pp-primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--pp-primary-light` | `#E0F2FE` | `DialogAccentLightBrush` |
| `--pp-primary-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--pp-success` | `#22C55E` | `DialogSuccessBrush` |
| `--pp-success-text` | `#15803D` | `DialogSuccessTextBrush` |
| `--pp-warning` | `#F59E0B` | `DialogWarningBrush` |
| `--pp-warning-text` | `#B45309` | `DialogWarningTextBrush` |
| `--pp-warning-soft` | `#FFF4E5` | `DialogWarningSubtleBrush` |
| `--pp-warning-border` | `#FDE68A` | `DialogWarningBorderBrush` |
| `--pp-danger` | `#EF4444` | `DialogDangerBrush` |
| `--pp-danger-text` | `#B91C1C` | `DialogDangerTextBrush` |
| 画布底 | `#0B1220` | `PluginPreviewCanvasBrush` |
| 画布浮层 | `#1E293B` | `PluginPreviewSurfaceBrush` |
| 画布描边 | `#334155` | `PluginPreviewBorderBrush` |
| 画布文字 | `#E2E8F0` | `PluginPreviewTextBrush` |
| 画布次要文字 | `#94A3B8` | `PluginPreviewMutedTextBrush` |
| 圆角 8/10/12 | — | `PluginCardCornerRadius`（8）与 `CornerRadius` 字面量（10/12） |
| 字体 | Segoe UI / Microsoft YaHei UI | 系统同族；等宽 `Consolas` |
| 图标 | Lucide（Web） | `DialogIconFont`（Font Awesome 6 Pro Solid） |

**缺令牌报告**：无。设计中的每个颜色都能落到既有 `Dialog*` / `Scada*` / `PluginPreview*` 键；未发现需要新增的语义色。

## 契约保留（一个都不能改）

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `DataContext = PreProcessingPlugin` + `IPluginConfigView` | 宿主靠 `viewObj.DataContext as IPluginConfigView` 取 `OnConfirm`；独立 VM = 确认不保存 |
| C2 | `LinkableValueEditor Port="{Binding SrcImage}" StepData="{Binding StepData}"` | `InputPort<HImage>` 持久化、变量链接、`ActualValue` 取图的唯一入口 |
| C3 | `ImageEdit` 的 `HImage` / `DrawObjectList` / `ActiveRoi` 三条绑定 | HALCON 显示/缩放/右键菜单；画布框拖拽回写 `ApplyBoxTuples`；**常驻，不折叠** |
| C4 | `SelectedOperator`（对象）/ `DisplayViewIndex`（int 0~9） | 选中项驱动参数面板与预览目标；显示窗口是 `[StepConfig] int` |
| C5 | 全部命令 + `LoadWarning` / `ActionHint` | 功能入口；本插件没有 `StatusLevel`，不扩展状态枚举 |

## 复刻实现（第 ③ 步，已完成）

| 设计项 | WPF 实现 |
| --- | --- |
| 三栏工作区 | 根 `Grid`：左 280px / 中 `*`（MinWidth 250）/ 右 `ParamColumn`（表格式 360、卡片式 520） |
| 图像输入 | `cv:LinkableValueEditor Label="" Port="{Binding SrcImage}" StepData="{Binding StepData}"` |
| 显示窗口 | `ComboBox ItemsSource={StaticResource ViewIndexes} SelectedIndex="{Binding DisplayViewIndex}"`，0 显示「不显示」 |
| 算子库 | `ItemsControl ItemsSource="{Binding OperatorLibrary}"`，组内按钮 `AddOperatorCommand` + `CommandParameter={Binding}` |
| 处理链 | `ListBox ItemsSource="{Binding Operators}" SelectedItem="{Binding SelectedOperator}"`，行内 `CheckBox IsChecked="{Binding Enabled}"`，`IsMuted` → `Opacity=0.45` |
| 链结构命令 | `MoveUpCommand` / `MoveDownCommand` / `RemoveOperatorCommand` / `ClearChainCommand` 四个 `SmallButtonStyle` 按钮 |
| 链搬运命令 | `CopyChainCommand` / `PasteChainCommand` 两个按钮；结果落 `ActionHint` |
| 预览目标 | 三个 `RadioButton`（`SegmentRadioStyle`）绑定 `ShowSourcePreview` / `ShowInputPreview` / `ShowOutputPreview` |
| 预览工具条 | `FitToWindow()` / `FitToImage()` 走 `PreviewImageEdit`；刷新 `RefreshPreviewCommand`；导出 `ExportChainCommand` |
| 预览画布 | `h:ImageEdit` 常驻，`HImage` / `DrawObjectList` / `ActiveRoi` 三条绑定逐字保留；空态只用上方信息条 |
| 参数面板 | `ui:FlatPropertyGrid` / `ui:CardPropertyGrid` 同绑 `SelectedOperator`，按 `IsFlatLayout` / `IsCardLayout` 切可见性 |
| 参数布局开关 | `IPropertyGridLayoutSwitch.UseCardLayout` → `plugin.IsCardLayout` + `ParamColumn.Width` 360 ⇄ 520 |
| 状态/反馈 | `LoadWarning` → 琥珀横幅（`StringNotEmptyToVisibilityConverter`）；`ActionHint` → 状态栏（`ActionHintConverter` 空串显示默认提示） |
| 窗口尺寸 | `OnLoaded` → `OnViewLoaded()` + `EnlargeHostWindowIfNeeded()`（内容区 1080×520，窗口 1120×680，只放大不缩小）；640×560 由根 `ScrollViewer` 横纵滚动兜底 |
| 转换器 | `StepNumberConverter`（步号 1 起算）、`ActionHintConverter`、`StringNotEmptyToVisibilityConverter`、`NullToVisibilityConverter` |

## 复刻证据

| 项 | 结果 |
| --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.PreProcessing\Plugin.PreProcessing.csproj -v m`：**0 错误 / 0 警告**；`Modules\Plugin.PreProcessing.dll` 已刷新（2026-10-20 04:04，239,104 字节） |
| 批量视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.PreProcessing`：**全部通过**（11 条 PASS：视图实例化 488 元素、DataContext=插件、LinkableValueEditor.Port 非空、命令绑定 9 个、`OnConfirm` 可执行、XAML hex=0、PNG 已生成） |
| 渲染证据 | `evidence/after-render-v1-initial.png`（920×520 初始态；HALCON `HwndHost` 不参与 `RenderTargetBitmap`，画布为黑底） |
| 640×560 表格式探针 | `evidence/after-layout-probe-640x560.png`（600×400 视口；Extent 914×448，双向滚动条可见，不抛异常） |
| 960×680 卡片式探针 | `evidence/after-layout-probe-960x680-card.png`（920×520 视口；Extent 1074×520，横向滚动条可见、纵向折叠） |
| 640×560 卡片式探针 | `evidence/after-layout-probe-640x560-card.png`（600×400 视口；Extent 1074×448，双向滚动条可见） |
| 设计稿 | `design.html`（自包含、可点击；四态 + 两种参数布局 + 两档宽度） |

## 已知偏差（复刻时按 R130 记录）

1. **三栏 vs ImageAcquisition 两栏**：本插件有「算子库 + 预览 + 链/参数」三个功能区，三栏是必要差异；视觉语言（卡片、间距、令牌、五态）沿用 V2。
2. **画布空态不做 WPF 覆盖层**：`ImageEdit` 是 HALCON `HwndHost`，WPF 元素无法可靠叠加在其上。WPF 复刻把「尚无预览」放进画布上方信息条与状态栏；`ImageEdit` 始终常驻，**不折叠、不换控件**。设计稿里的居中空态只表达信息层级。
3. **参数面板内部不复刻 Web 表单**：`FlatPropertyGrid` / `CardPropertyGrid` 是共享控件，按 `[SuperDisplay]` 反射生成；本视图只改外层卡片、标题、空态，不改内部行样式。
4. **卡片式参数列宽 520px**（旧版 540px）：1120×680 下预览实际约 **256px**；960×680 下约 **250px**，不建议在窄档使用卡片式（请切回表格式）。卡片式切换后 `RootWidthConverter` 会把根内容最小宽度从 914 切到 1074，960 下出现横向滚动、640 下横纵滚动；三栏布局的宽度账见「尺寸与滚动（P1 修复）」。
5. **预览头没有文件名/尺寸**：本插件没有 `PreviewImagePath` / 文件大小属性；预览头显示「预处理预览 + 当前预览目标 + 当前算子名」，不新增只读属性。
6. **状态语义**：本插件没有 `StatusLevel`（Info/Warning/Error）；`LoadWarning` 用琥珀横幅、`ActionHint` 用状态栏文字表达，不新增第四档。
7. **图标字体**：Web 用 Lucide，WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`），形状不同、语义一致。
8. **阴影/动效**：Web 窗口多层阴影；WPF 用宿主既有 `DropShadowEffect`；动效 150~200ms。
9. **深色画布**：`PluginPreviewCanvasBrush` 是外层容器底色；`ImageEdit` 模板背景可能是黑色，图像区域呈近黑，属 HALCON 控件既有行为。
10. **渲染证据范围**：`RenderTargetBitmap` 不捕获 HALCON `HwndHost`；批量冒烟 PNG 展示的是 WPF 布局，真实图像预览需真机窗口。
11. **设计稿参数值**：设计稿展示的是代表值（对比度 101×101 / 0.7、二值化 128~255 等），实际值由算子默认值与 `FlatPropertyGrid` 反射决定。
12. **算子库滚动**：WPF 左栏「图像输入 + 算子库」共用一个 `ScrollViewer`；设计稿为了展示分组把库区画得更长，实际窄档需滚动。

## 未纳入本版（记录，不改）

- **加载骨架 / 蓝色加载态**：需要 VM 新增 `IsPreviewLoading` 或 `PreviewState`。
- **图像尺寸 / 文件大小**：需要 VM 新增只读属性（`HImage.GetImageSize` / `FileInfo.Length`）。
- **放大 / 缩小按钮**：`HalconBase` 只有 `FitToWindow()` / `FitToImage()`，没有公开 zoom 方法。
- **打开所在文件夹 / 系统导航**：需要新增 `Process.Start` 或宿主导航契约。
- **参数面板内联搜索**：属于共享 `PropertyGrid` 的能力，本视图不改其内部。
- **拖拽入图 / 拖拽排序**：当前清单未实现；本版不新增交互契约。
- **实时预览节流指示**：`RefreshPreview` 的后台轮次已在 VM 内合并，但无公开 loading 状态，本版不显示。

## 变更规则

设计基线确认后只读。需要调整设计时升版本（v2），并同步更新 `PreProcessingView.xaml`、本 README 的「复刻实现」与 `docs/code-changes/2026-10-20-PreProcessing-Web化.md`。
