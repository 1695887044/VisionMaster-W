# 图像采集配置窗口 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v2 |
| 确认日期 | 待用户确认 |
| 确认人 | 待用户确认 |
| 覆盖范围 | `Plugins\Plugin.ImageAcquisition\ImageAcquisitionView.xaml`（插件内容区）+ `VisionMaster\Views\DialogViews\PluginConfigShellView.xaml`（960×680 宿主外壳） |
| 窗口尺寸 | 960×680 默认；640×680 窄档；宿主最小 640×420 不在本版覆盖范围（见「V2 明确延后」D6） |
| 设计基线文件 | `design.html`（当前 v2，自包含，内联 SVG 图标）；`design-v1.html`（历史 v1） |
| 复刻状态 | 已完成（2026-10-20）；证据 `evidence/after-render-v2-*.png`；设计仍待用户确认 |

## 定位

按 `AGENTS.md` §30.1 的四步流程：本文件是**第 ① 步（Web 设计基线）**的产物。
Web 只是设计媒介，**不进入构建、不部署**（R126）。确认后按本基线做第 ③ 步（WPF 复刻），
第 ④ 步以「同一屏、同一状态」的截图对照验收（R129/R132）。

## 现状审查（改动理由）

| # | 现状 | 影响 |
| --- | --- | --- |
| 1 | `TabControl` 两级页签：「基本参数 / 图像参数」，后者只有一个「显示窗体」下拉（`ImageAcquisitionView.xaml:63/383-421`） | 信息架构失衡，为一个下拉单开一页 |
| 2 | 采集模式用 4 个 `RadioButton` + `GroupBox`，上方固定 `Height="140"` 行（`:70-83`） | 默认窗口下大量留白；窄窗口换行后高度不可控；形态陈旧 |
| 3 | 预览区只有 `ImageReadOnly` + 一行状态文字（`:424-478`） | 无文件信息、无工具条、无空/错误态；空白时整块白板 |
| 4 | 网络推送把生产参数、服务状态、推送测试、接口文档堆在一个滚动区（`:148-306`） | 生产配置与调试工具混在一起，主机/端口/令牌这些宿主级参数长期占屏 |
| 5 | 相机模式是裸下拉 + 一行状态（`:308-379`） | 「能不能跑」不够前置；未连接时要等运行失败才知道 |
| 6 | 视图内大量内联灰阶：`#666 / #333 / #999 / #FAFAFA / #DDDDDD / #E0E0E0`（`:138-143/261-269/428`） | 违反 R117；换肤/统一调整时改不动 |
| 7 | 左列 `MinWidth=300 MaxWidth=430`（`:59`） | 表单宽度被硬截断，宽窗口下右侧预览吃掉全部增量 |

## v2 方向

> 预览优先，一屏完成配置；**只做纯 UI 重构，不改功能入口与绑定**。

1. **两栏固定分工**：左 356px 配置轨（模式 + 模式专属参数 + 输出），右侧预览工作区自适应。
2. **模式是卡片，不是单选**：2×2 卡片，图标 + 名称 + 用途 + 选中态同屏；仍是 `RadioButton` + `EnumConverter`，照常写 `Mode`。
3. **取消二级页签**：「显示窗口」并入左栏底部「输出」区，仍绑 int 的 `DisplayViewIndex`。
4. **预览是工作区**：文件信息头 + 工具条 + 深色画布 + 状态栏；空态/错误态齐备；加载骨架不在 V2。
5. **生产与调试分层**：网络推送把服务状态置顶；主机/端口/令牌、推送测试、接口说明收进「调试与接口」。
6. **相机状态前置**：显示名、序列号、连接状态同卡；不做「前往相机设置」按钮。
7. **全令牌化**：颜色映射 `PluginConfigColors.xaml` / `Colors.xaml`；只在本视图派生样式，不改共享字典。
8. **五态与可达性**：hover / pressed / focus / disabled 全量定义；图标按钮 30×30 且 `Padding="0"`（R68），焦点环走 `DialogFocusVisual`（R135）。

## v1 → v2 变更摘要

| 维度 | v1 | v2 |
| --- | --- | --- |
| 预览工具栏 | 适应窗口 / 1:1 / 放大 / 缩小 / 打开文件夹 / 刷新 | **适应窗口 / 1:1 / 目录刷新**（只接现有 `FitToWindow()` / `FitToImage()` / `RefreshPreviewCommand`） |
| 预览头 | 文件名 + 尺寸 | 文件名 + 「文件 / 目录 / 网络 / 相机 · 只读」 |
| 预览状态 | 正常 / 空态 / 加载 / 错误 | **正常 / 空态 / 错误**（加载骨架延后） |
| 画布角标 | RGB · 8-bit / 适应窗口 42% | **只读预览**（不显示未暴露的通道/缩放数据） |
| 相机页 | 「前往相机设置」按钮 | **文字提示**，不做按钮 |
| 页头 | 帮助按钮 + 「常用工具」chip | **只保留关闭** |
| 页脚 | 随预览状态变色 | **宿主状态「状态: 待执行」+「耗时: 0 ms」**，不冒充插件状态 |
| 说明 | 关键改动 8 条 | 关键改动 + **契约保留 C1-C5** + **明确延后 D1-D6** |

## 设计要点（完整清单）

1. 模式卡片：选中态 = 主色描边 + 主色浅底 + 右侧对勾；未选中 = 白底 + 浅描边；悬停加深描边。
2. 表单三件套：标签 12px / 控件 36px / 帮助 11.5px；字段间距 12px；输入区随栏宽伸缩。
3. 路径行：输入框 + 「浏览」+ 变量链接按钮；**WPF 里由 `LinkableValueEditor` 承载**，V2 只改外观（设 `Label=""`，外置标签）。
4. 文件目录：路径 + 索引步进（上一张 / 索引 / 总数 / 下一张 / 刷新）+ 当前文件名 + 自然排序说明；索引仍走 `FileIndexPort`。
5. 网络推送：绿色服务横幅置顶；流程名常驻；「调试与接口」折叠区承载连接参数、推送测试、接口示例。
6. 相机：下拉只负责选设备；状态卡负责「显示名 + 序列号 + 连接状态」；超时 0 = 用相机自身超时；不做导航按钮。
7. 输出：显示窗口下拉固定在左栏底部，`0 = 不显示；1~9 = 主界面视图窗口`；仍绑 int。
8. 预览头：文件图标 + 文件名 + 模式/只读；工具条只有适应窗口、1:1、目录刷新；没有放大/缩小/打开文件夹。
9. 画布：深色底 + 网格；左上「只读预览」；空态用虚线卡 + 选择图片/选择文件夹按钮；错误态只给原因与下一步，不加重试按钮。
10. 状态栏：状态点 + 消息（左）+ 路径（右）；只有 Info / Warning / Error 三档；窄档隐藏路径，把宽度让给消息。
11. 底部宿主栏：状态胶囊（宿主 `StatusText`）+ 耗时 + 执行 / 取消 / 确认；插件视图不改它。
12. 数字等宽：索引、总数、耗时使用 tabular-nums。

## 契约保留（V2 一个都不能改）

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 宿主靠 `viewObj.DataContext as IPluginConfigView` 取 `OnConfirm`；换独立 VM = 确认不保存 |
| C2 | 四个 `LinkableValueEditor`：`FilePath` / `FolderPath` / `FileIndex` / `FrameTimeout` | 端口持久化、类型转换、`ValueChanged` 订阅、`StepData.SetLink/RemoveLink`、变量绑定弹窗都在控件里 |
| C3 | `ImageReadOnly.HImage = PreviewImage` | 保留 HALCON 显示/缩放/右键菜单；`PreviewImage` setter 在 UI 线程释放旧 `HImage` |
| C4 | `Mode` / `SelectedCameraOption` / `DisplayViewIndex` | 三种类型不能换：枚举 / `CameraOption` 对象 / int（0~9） |
| C5 | `Browse*` / `Refresh` / `Prev` / `Next` / `PushLocalImage` + `StatusMessage`/`StatusLevel` 三档 | 这些是功能入口；状态只有 Info / Warning / Error，V2 不扩展 |

## V2 明确延后（需新增功能或契约）

| # | 项 | 需要什么 |
| --- | --- | --- |
| D1 | 加载骨架 / 蓝色加载态 | VM 新增 `IsPreviewLoading` 或 `PreviewState`；当前 `StatusLevel` 只有三档 |
| D2 | 尺寸 / 文件大小 | VM 新增只读属性（`HImage.GetImageSize` / `FileInfo.Length`） |
| D3 | 放大 / 缩小按钮 | `HalconBase` 只有 `FitToWindow()` / `FitToImage()`，没有公开 zoom 方法 |
| D4 | 打开所在文件夹 | 新增 `Process.Start("explorer.exe", ...)` 行为 |
| D5 | 前往相机设置 | 新的宿主导航契约 |
| D6 | 宿主 MinHeight 420 → 560 | 影响所有插件；640×420 下内容区约 260px，预览会过矮 |

## 令牌映射

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--ia-surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--ia-surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--ia-surface-3` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--ia-border` | `#E2E8F0` | `DialogBorderBrush` |
| `--ia-border-soft` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--ia-border-strong` | `#CBD5E1` | **待补**：新增 `DialogBorderStrongBrush`，或复用 `ControlBorderBrush`（#D1D1D1） |
| `--ia-text` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--ia-text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--ia-text-3` | `#94A3B8` | `DialogTextMutedBrush` |
| `--ia-text-4` | `#64748B` | `ScadaSecondaryTextBrush`（#66708A，近似） |
| `--ia-primary` | `#0EA5E9` | `DialogAccentBrush` |
| `--ia-primary-strong` | `#0284C7` | `DialogAccentDarkBrush` |
| `--ia-primary-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--ia-primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--ia-primary-light` | `#E0F2FE` | `DialogAccentLightBrush` |
| `--ia-primary-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--ia-success` | `#22C55E` | `DialogSuccessBrush` |
| `--ia-success-text` | `#15803D` | `DialogSuccessTextBrush` |
| `--ia-success-soft` | `#F0FDF4` | `DialogSuccessSubtleBrush` |
| `--ia-success-border` | `#BBF7D0` | **待补**：新增 `DialogSuccessBorderBrush`，或派生自 `DialogSuccessBrush` 低透明度 |
| `--ia-danger` | `#EF4444` | `DialogDangerBrush` |
| `--ia-danger-text` | `#B91C1C` | **待补**：新增 `DialogDangerTextBrush`（与 `DialogWarningTextBrush` 对称） |
| `--ia-danger-soft` | `#FEF2F2` | `DialogDangerSubtleBrush` |
| `--ia-danger-border` | `#FECACA` | **待补**：新增 `DialogDangerBorderBrush`，或派生自 `DialogDangerBrush` 低透明度 |
| `--ia-warning` | `#F59E0B` | `DialogWarningBrush` |
| `--ia-warning-text` | `#B45309` | `DialogWarningTextBrush` |
| `--ia-warning-soft` | `#FFF4E5` | `DialogWarningSubtleBrush` |
| `--ia-warning-border` | `#FDE68A` | **待补**：新增 `DialogWarningBorderBrush`，或派生自 `DialogWarningBrush` 低透明度 |
| 窗口阴影 | — | 宿主已有 `DropShadowEffect` + `DialogShadowColor`（`PluginConfigShellView.xaml:130-137`） |
| 圆角 6/8/10/12/16 | — | WPF `CornerRadius` 字面量（仓内暂无圆角令牌字典；后续建立令牌再替换） |
| 间距 4/8/12/16/20/24 | — | WPF `Margin` / `Padding` 字面量（同上） |
| 字体 | Segoe UI / Microsoft YaHei UI | 同族系统字体；等宽用 Cascadia Mono / Consolas |

**不新增颜色语义**：所有颜色仍落在 `Dialog*` / `Scada*` 既有语义域内；上表 4 个「待补」是**同一语义的描边/文字档**，不是新语义。

## 复刻实现（第 ③ 步，已完成）

| 设计项 | WPF 实现 |
| --- | --- |
| 2×2 模式卡片 | 4 个 `RadioButton` + `ModeCardStyle`；`IsChecked` 仍走 `ui:EnumConverter` 写 `Mode` |
| 单页布局 | 删除 `TabControl`；「显示窗口」并入左栏底部「输出」；`DisplayViewIndex` 仍绑 int |
| 表单节奏 | 标签外置 + `LinkableValueEditor Label=""`；路径/目录各加一个「浏览」按钮绑 `BrowseCommand` |
| 预览工作区 | `ContentControl PreviewHost` 按需创建 `ImageReadOnly`（有图才实例化）；`FitToWindow()` / `FitToImage()` / `RefreshPreviewCommand`；深色画布 + 空/错误态 |
| 网络推送 | 服务横幅 + `Expander`「调试与接口」；四个 Push 属性与命令绑定不变 |
| 相机 | `ComboBox` + 状态卡；`SelectedItem=SelectedCameraOption` 不变 |
| 状态栏 | `StatusMessage` / `StatusLevel` 三档；`StatusMessageConverter` 把空串显示为「就绪」 |
| 令牌 | 视图零硬编码十六进制；新增公共令牌见下 |

### 复刻约束（已遵守）

1. **保留视图构造与生命周期**：`ImageAcquisitionView(IStepConfigData, ImageAcquisitionPlugin)`、`DataContext = plugin`、`plugin.Initialize(stepData)` 原样保留（`ImageAcquisitionView.xaml.cs:16-21`）。宿主 `PluginConfigShellViewModel` 还会再调一次 `Initialize`，时序未改。
2. **`LinkableValueEditor` 只包裹不改内部**：设 `Label=""` 后外置标签；保留 `Port` / `StepData` / `BrowseCommand`；「浏览」按钮绑 `BrowseCommand`；链接按钮继续用控件自带的 `PART_LinkBtn`。
3. **`ImageReadOnly` 保留**：`PreviewHost.Content` 绑 `PreviewImage`，模板内 `HImage="{Binding Mode=OneWay}"`；有图才创建，避免折叠/恢复不重绘；工具条从 `PreviewHost` 可视树取当前控件，「适应窗口」调 `FitToWindow()`、「1:1」调 `FitToImage()`、「刷新」只给文件目录模式并绑 `RefreshPreviewCommand`。
4. **相机下拉保持 `SelectedItem = SelectedCameraOption`**；显示窗口下拉保持 `SelectedItem = DisplayViewIndex`（int）。
5. **推送表单保持四个属性 + `PushLocalImageCommand` + `PushResultText` 绑定**；只把它们移进 `Expander`。
6. **状态栏保持 `StatusMessage` / `StatusLevel` 绑定**；只换颜色/布局，未新增枚举值。
7. **不修改共享字典的既有键/控件**：`LinkableValueEditor`、`ImageReadOnly`、`VerTabControl` 未动；新增令牌只追加到公共字典（见下）。
8. **宿主外壳和 MinHeight 未改**；640×420 是否可接受，仍单独决策（D6）。
9. **验证清单已执行**：见「复刻证据」；4 个端口、`Mode`、相机序列号、`DisplayViewIndex`、推送字段均有回环断言。

## 令牌新增（公共字典）

为消除视图内硬编码颜色，按 R119 在公共字典追加以下键（不改既有键/值）：

| 字典 | 新增键 |
| --- | --- |
| `UI/Controls/Themes/Colors.xaml` | `DialogBorderStrongBrush`、`DialogSuccessBorderBrush`、`DialogDangerTextBrush`、`DialogDangerBorderBrush`、`DialogWarningBorderBrush` |
| `UI/Controls/Themes/PluginConfigColors.xaml` | `PluginPreviewCanvasBrush`、`PluginPreviewSurfaceBrush`、`PluginPreviewBorderBrush`、`PluginPreviewTextBrush`、`PluginPreviewMutedTextBrush` |

视图 XAML 静态扫描断言 **hex=0**（`UIThemeSmokeTest --image-acquisition` 第 2 条）。

## 复刻证据

| 项 | 结果 |
| --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.ImageAcquisition\Plugin.ImageAcquisition.csproj`：**0 错误 / 0 警告**；`Modules` 拷贝已成功（首次尝试曾被运行中的 VisionMaster / VS 短暂锁定，重试后通过） |
| 视图冒烟 | `dotnet run --project UIThemeSmokeTest -- --image-acquisition`：**24/24 通过**（XAML hex=0、4 个 `LinkableValueEditor.Port`、`ImageReadOnly.HImage`、`Mode` 点击/`SelectedCameraOption` 下拉/`DisplayViewIndex`、命令/推送字段、4 个端口编辑 → `OnConfirm` 值与键、`FitToWindow`/`FitToImage`） |
| 渲染 | `--image-acquisition-render`：**5 张 PNG**（见下） |
| 常规 gate | `FlowCanvasChecks`：**2691 通过 / 0 失败**（含 `[IA]` 图像采集段） |
| 截图 | `evidence/after-render-v2-SingleFile.png`、`...Folder.png`、`...Hub.png`、`...Camera.png`、`...Error.png` |

> **构建附注**：`FlowCanvasChecks` 首次构建时 `Plugin.Yolo` 的 `copy OnnxRuntime` 步骤曾因文件被占用报环境性失败（退出码 4）；重试后构建 **0 错误**，gate 以 `--no-build` 重跑得到 **2691/0**。`Modules\Plugin.ImageAcquisition.dll` 已由插件工程刷新，`[IA]` 段加载的是新产物。
>
> **并发改动附注**：冒烟期间发现 `UI/Controls/Themes/Fluent/FluentAliases.xaml` 里 `DialogChipDanger` 被并发改动定义了两次（`Button` 与 `Border` 同键），应用资源合并直接抛 `ArgumentException`。已将新增的 Button 变体改名为 `DialogChipDangerButton`，并同步 `VisionMaster/Views/DialogViews/IoConfigView.xaml` 的引用；Border 语义不变。详见 `docs/code-changes/2026-10-20-图像采集配置窗口V2复刻与FluentChipDanger重复键修复.md`。
>
> **预览刷新修复（2026-10-20）**：V2 第一版用 `Visibility=Collapsed` 隐藏无图时的 `ImageReadOnly`，图片到达后从折叠态恢复不重绘，表现为「打开图片后界面没刷新」，空态覆盖层还让人误以为 Halcon 控件被换掉。已改为 `ContentControl PreviewHost` + DataTemplate：**有图才创建 `ImageReadOnly`**，模板内 `HImage="{Binding Mode=OneWay}"`；控件类型、HALCON 缩放与右键菜单均未变。详见 code-changes §7。

## 已知偏差（复刻时按 R130 记录）

1. **图标字体**：Web 用 Lucide，WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`）；形状不同，语义一致。
2. **阴影/动效**：Web 窗口阴影为 CSS 多层阴影；WPF 用宿主现有 `DropShadowEffect`；动效统一 150~200ms。
3. **预览图**：Web 用内联 SVG 工业零件示意图；WPF 用真实 `HImage`。
4. **深色画布**：`#0B1220` 是设计选择。若 Halcon 窗口背景色不可改，允许用宿主 `Border` 深色底 + 图像 letterbox，需在验收时说明。
5. **字体度量**：同字号下 Segoe UI / 微软雅黑与 Web 的字宽、行高略有差异，不按 px 硬套。
6. **滚动条**：WPF 左栏滚动条约占 17px，窄档可用宽度比 Web 少约 17px。
7. **工具提示**：Web 的 `data-tooltip` 由预览宿主提供；WPF 用 `DialogToolTipStyle`。
8. **页脚状态**：V2 页脚显示宿主 `StatusText`（「状态: 待执行」），不随预览状态变色；插件状态只在预览状态栏。
9. **渲染证据的范围**：`ImageReadOnly` 是 HALCON `HwndHost`，`RenderTargetBitmap` 不捕获其内容；证据 PNG 展示的是四种模式的**空态/错误态**布局，真实图像预览需真机窗口验证。
10. **模式卡片高度**：WPF 用 52px（Web 56px），以便 920×520 内容区里相机页的帮助文字不被裁掉；宽度与 2×2 排布不变。
11. **相机下拉**：WPF 的 `ComboBox` 选中项显示 `Caption`（显示名 + 序列号），状态文字在下方状态卡；Web 稿的下拉同时显示状态。两者信息量一致，位置不同。
12. **链接入口**：WPF 沿用 `LinkableValueEditor` 自带的 🔗/✕ 图标，没有另造 Web 稿里的方形链接按钮；`Port`/`StepData`/`SetLink`/`RemoveLink` 行为原样。
13. **折叠区**：「调试与接口」用 Fluent 默认 `Expander` 头（外加卡片边框），未复刻 Web 稿的自绘 header；展开/收起行为一致。
14. **预览头文件名**：配置态下相机/网络模式没有 `PreviewImagePath`，标题显示「预览」，模式标签显示「相机 · 只读 / 网络 · 只读」；真实取图后按路径显示。

## 未纳入本版（记录，不改）

> 纯 UI 部分（2×2 模式卡片、单页、预览工作区、网络推送分层、相机状态卡、令牌化）已在 WPF 落地；以下为**需要新增功能/契约**的项，仍延后。

- **加载骨架 / 蓝色加载态**：见 D1。
- **尺寸 / 文件大小**：见 D2。
- **放大 / 缩小按钮**：见 D3。
- **打开所在文件夹**：见 D4。
- **前往相机设置**：见 D5。
- **640×420 最小高度下的完整布局**：见 D6。
- **拖拽入图**：当前清单 B5 未实现；本版空态文案只承诺「选择图片/文件夹」。
- **相机实时预览**：仍在「系统 → 相机设置」。
- **ROI / 测量叠加**：属于算子视图，不属于采集配置。

## 变更规则

设计基线确认后只读。需要调整设计时升版本（v3），并同步更新已复刻的 `ImageAcquisitionView.xaml` 与本映射表。
