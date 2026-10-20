# 码读取（CodeReader）配置窗口 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 设计日期 | 2026-10-20 |
| 确认日期 / 人 | 待用户确认 |
| 覆盖范围 | `Plugins\Plugin.CodeReader\CodeReaderView.xaml`（插件内容区）+ 宿主 `PluginConfigShellView`（960×680 外壳） |
| 窗口尺寸 | 960×680 默认；640×680 窄档；宿主最小 **640×560**（2026-10-20 统一口径） |
| 设计文件 | `design.html`（v1，自包含、无外部依赖、可点击；内联 SVG 图标与示意预览） |
| 复刻状态 | 已完成（2026-10-20），见「复刻实现」与「验证」 |
| 设计语言 | 沿用 ImageAcquisition V2（`docs/UI设计基线/image-acquisition/`）：两栏 / 卡片与分区 / 36px 控件 / 12px 标签 / 11.5px 帮助文字 / 8px 圆角 / 公共令牌 / 五态 |

## 1. 定位

本文件是「插件自定义视图 Web 化」的第 ① 步产物（Web 设计基线）。Web 只是设计媒介，不进入构建、不部署。确认后按本基线做第 ③ 步（WPF 复刻），第 ④ 步以「同一屏、同一状态」的截图对照验收。

本次只改 `Plugin.CodeReader` 的配置视图；**不改**插件业务逻辑、端口名、命令名、持久化，也不改共享控件/共享字典。

## 2. 现状审查（改动理由）

| # | 现状（改动前） | 影响 |
| --- | --- | --- |
| 1 | 左栏是 5 张 `PluginCard` 竖排，右栏只有一张 `ImageReadOnly` 卡片（`CodeReaderView.xaml:54-166`） | 信息层级平均，试算结果与提示混在一张卡里，扫读慢 |
| 2 | 图像输入 `LinkableValueEditor` 没有外置标签；HImage 端口不可文本输入，实际只显示占位与 🔗 | 用户容易以为「输入框坏了」；需要明确「只能链接上游」 |
| 3 | 码制/极性下拉是 26px 默认 ComboBox，超时是 24px 默认 TextBox | 与宿主 Fluent 外壳、ImageAcquisition V2 的 36px 控件节奏不一致 |
| 4 | 试算结果用 11px Consolas 直接堆在提示下面，没有独立结果区 | 「提示（下一步）」与「结果（事实）」混在一起，失败/无码不显眼 |
| 5 | 预览区没有空态，空白时是一整块白色/黑色画布 | 用户不知道图从哪里来；HALCON 控件在无图时也占着视觉重心 |
| 6 | 视图内使用 `PluginHintText` / `PluginTitleTextBrush` 等旧插件令牌 | 与 Dialog 语义色体系（#0EA5E9 青蓝）不统一 |
| 7 | 根 `UserControl` 写死 `Width=900 Height=560` | 宿主内容区变化时不自适应；640 窄档不可用 |

## 3. v1 方向

> **一屏完成配置，预览只读，结果独立成块；只做纯 UI 重构，不改任何功能入口与绑定。**

1. **两栏固定分工**：左 356px 配置轨（图像输入 / 码制 / 二维码参数 / 通用参数 / 试算 / 输出端口说明），右侧只读预览工作区自适应。
2. **图像输入唯一入口**：`LinkableValueEditor` 绑定 `Image` 端口，保留 `Port` / `StepData`；本插件没有文件浏览功能，所以**没有「浏览」按钮**，也不新增。
3. **码制家族可读**：码制下拉仍存 HALCON 原始名；右侧「二维码 / 一维码」徽标由 `Is2D` 驱动；「二维码参数」卡只在二维时出现。
4. **试算结果独立**：`Hint` 讲下一步，`ResultSummary` 只放试算输出；结果区按成功/无码/失败三态着色（不新增 `StatusLevel`，仅由文本内容与固定语义样式表达）。
5. **预览是只读工作区**：文件头 + 工具条（适应窗口 / 1:1）+ 深色画布 + 状态栏；无图时纯 WPF 空态，有图才创建 `ImageReadOnly`。
6. **全令牌化**：颜色映射 `Colors.xaml` / `PluginConfigColors.xaml`；视图内零十六进制；只在本视图派生局部样式，不改共享字典。
7. **五态与可达性**：hover / pressed / focus / disabled 全量；图标按钮 30×30 且 `Padding="0"`（R68）；焦点环走 `DialogFocusVisual`；ToolTip 走 `DialogToolTipStyle`。
8. **只读信息不冒充配置**：输出端口 `Codes` / `Count` / `Text` 只做静态说明，不新增可编辑控件。

## 4. 设计要点（完整清单）

1. 左栏是「配置轨」：底色 `DialogRowAlternateBrush`、1px `DialogBorderLightBrush`、圆角 8；内部用细分隔线分组，不再层层套卡片。
2. 字段节奏：标签 12px、控件 36px、帮助 11.5px；字段间距 12px；输入区随栏宽伸缩。
3. 图像输入：`LinkableValueEditor` 设 `Label=""`、`Height=36`，外置「待读码图像」标签；右侧只显示控件自带的 🔗（HImage 不可文本输入，也不显示清除按钮）。
4. 码制：下拉显示中文/通用名，`SelectedValuePath="HalconName"`；下方帮助文字直接绑定 `SymbologyHint`；家族徽标由 `Is2D` 切换。
5. 二维码参数：整卡用 `Is2D` 控制 `Visibility`；极性下拉 `SelectedValuePath="Value"`；帮助文字固定为极性说明。
6. 通用参数：搜索超时 `TextBox` 绑定 `TimeoutMs`（int）；帮助文字说明「0 = 不限」与无码时的耗时风险。
7. 试算：**固定在左栏底部**（主操作常驻，不随配置区滚动）；主按钮「试算一下」调用 `TryPreviewRecognize()`；`Hint` 显示在右侧预览状态栏；结果区等宽 10.5px、`TextWrapping=Wrap`、最小高度 86。
8. 预览头：图像图标 + 「上游图像」+「只读 · 不参与定位」；工具条只有「适应窗口（`FitToWindow`）/ 1:1（`FitToImage`）」，都是 `HalconBase` 现有公开方法。
9. 画布：深色底 `PluginPreviewCanvasBrush`；左上「只读预览」角标；空态用虚线卡 + 说明，不提供假按钮（图像只能从上游端口来）。
10. 状态栏：状态点 + `Hint`（左）+ 码制名（右）；本插件没有 `StatusLevel`，状态点固定为主色信息点，不伪造三档。
11. 底部宿主栏：状态胶囊（宿主 `StatusText`）+ 耗时 + 执行 / 取消 / 确认；插件视图不改它。
12. 数字等宽：超时、码制计数、结果区使用 tabular / 等宽字体。

## 5. 状态覆盖

| 状态 | 设计表现 | WPF 触发条件 |
| --- | --- | --- |
| 默认（有图 · 读到码） | 预览图 + 结果区绿色「读到 2 个码」+ 状态栏「已载入上游图像」 | `DisplayImage != null`，`ResultSummary` 为成功文案 |
| 空态（未绑定） | 深色画布上的虚线空态卡 + 结果区「尚未试算」 | `DisplayImage == null` |
| 无码（Count = 0） | 预览图保留 + 结果区橙色「读到 0 个码（未读到码）」 | 试算成功但 `codes.Count == 0` |
| 失败（试算异常） | 预览图保留 + 结果区红色「试算失败：…」 | `TryPreviewRecognize()` 走 `TryRead` 失败分支 |
| 一维码 | 二维码参数卡隐藏，家族徽标变灰「一维码」，帮助文字切换 | `Is2D == false` |
| 窄档 640 | 左栏 300px，预览头文件名截断、状态栏右侧码制名隐藏（视图 `ActualWidth < 620` 时折叠） | 宿主内容区约 600px |

> **无图文案（2026-10-20 已修，CRD3）**：原「先点『载入』把示意图读进来再试算」指向界面上不存在的按钮。现改为「先在『图像输入』绑定上游图像，或点宿主『执行』带图进来再试算；本节点没有『载入』按钮，图像只来自上游」——保留「载入」二字以兼容既有文案断言；帮助手册 `02-界面与端口.md` 已同步该口径。

## 6. 令牌映射

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--cr-surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--cr-surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--cr-surface-3` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--cr-border` | `#E2E8F0` | `DialogBorderBrush` |
| `--cr-border-soft` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--cr-border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` |
| `--cr-text` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--cr-text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--cr-text-3` | `#94A3B8` | `DialogTextMutedBrush` |
| `--cr-text-4` | `#64748B` | `ScadaSecondaryTextBrush`（`#66708A`，近似） |
| `--cr-primary` | `#0EA5E9` | `DialogAccentBrush` |
| `--cr-primary-strong` | `#0284C7` | `DialogAccentDarkBrush` |
| `--cr-primary-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--cr-primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--cr-primary-light` | `#E0F2FE` | `DialogAccentLightBrush` |
| `--cr-primary-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--cr-success` | `#22C55E` | `DialogSuccessBrush` |
| `--cr-success-text` | `#15803D` | `DialogSuccessTextBrush` |
| `--cr-success-soft` | `#F0FDF4` | `DialogSuccessSubtleBrush` |
| `--cr-success-border` | `#BBF7D0` | `DialogSuccessBorderBrush` |
| `--cr-danger` | `#EF4444` | `DialogDangerBrush` |
| `--cr-danger-text` | `#B91C1C` | `DialogDangerTextBrush` |
| `--cr-danger-soft` | `#FEF2F2` | `DialogDangerSubtleBrush` |
| `--cr-danger-border` | `#FECACA` | `DialogDangerBorderBrush` |
| `--cr-warning` | `#F59E0B` | `DialogWarningBrush` |
| `--cr-warning-text` | `#B45309` | `DialogWarningTextBrush` |
| `--cr-warning-soft` | `#FFF4E5` | `DialogWarningSubtleBrush` |
| `--cr-warning-border` | `#FDE68A` | `DialogWarningBorderBrush` |
| 预览画布 | `#0B1220` | `PluginPreviewCanvasBrush` |
| 预览深色表面 | `#1E293B` | `PluginPreviewSurfaceBrush` |
| 预览描边 | `#334155` | `PluginPreviewBorderBrush` |
| 预览正文 | `#E2E8F0` | `PluginPreviewTextBrush` |
| 预览弱文字 | `#94A3B8` | `PluginPreviewMutedTextBrush` |
| 图标字体 | — | `DialogIconFont`（FA 6 Pro Solid） |
| 等宽字体 | — | `PluginMonoFontFamily`（Consolas, Microsoft YaHei） |
| 圆角 6/8/10/12/14/16 | — | 字面量 `CornerRadius`（仓内暂无圆角令牌字典；与参考实现一致） |
| 间距 4/6/8/10/12/14/16/20/24 | — | 字面量 `Margin` / `Padding`（同上） |

**缺失令牌**：无。本视图所需颜色全部落在既有公共令牌内；未新增、未修改任何共享字典键。

## 7. 契约保留（一个都不能改）

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 宿主 `viewObj.DataContext as IPluginConfigView` 取 `OnConfirm`；换独立 VM = 确认不保存 |
| C2 | `LinkableValueEditor`：`Port="{Binding Image}"`、`StepData="{Binding StepData}"` | HImage 端口的链接、变量绑定、`SetLink`/`RemoveLink` 全在控件里；本插件没有 `BrowseCommand`，不新增 |
| C3 | `ImageReadOnly.HImage = DisplayImage`（经 `ContentControl` + `DataTemplate`，`Mode=OneWay`） | 保留 HALCON 显示/缩放/右键菜单；有图才创建，避免折叠后不重绘 |
| C4 | `Symbology`（`SelectedValuePath="HalconName"`）/ `Polarity`（`SelectedValuePath="Value"`）/ `TimeoutMs`（int） | 存盘值是 HALCON 原始名/固定枚举字符串；改类型会打断存量流程 |
| C5 | `TryPreviewRecognize()`、`Hint`、`ResultSummary`、`Is2D`、`SymbologyOptions`、`PolarityOptions` | 试算与运行共用同一条 `TryRead`；提示/结果是插件公开状态 |

> 本插件**没有** `Mode`、`SelectedItem`、`Command`、`StatusLevel` 绑定；视图不凭空添加这些属性，也不新增状态枚举。

## 8. 复刻实现（第 ③ 步）

| 设计项 | WPF 实现 |
| --- | --- |
| 两栏 | `Grid` 两列：`1*`（300~356）+ `1.6*`（MinWidth 240） |
| 配置轨 | 左 `Border`：`DialogRowAlternateBrush` + `DialogBorderLightBrush` + 圆角 8；内部 `ScrollViewer` |
| 图像输入 | `LinkableValueEditor Label="" Height=36 Port="{Binding Image}" StepData="{Binding StepData}"` |
| 码制 | `ComboBox ItemsSource="{Binding SymbologyOptions}" SelectedValue="{Binding Symbology}" SelectedValuePath="HalconName"` + 显式 `ItemTemplate`（`Text="{Binding DisplayName}"`） |
| 家族徽标 | `TextBlock` + `DataTrigger Binding="{Binding Is2D}"` 切换「二维码/一维码」与底色 |
| 二维码参数 | `StackPanel Visibility="{Binding Is2D, Converter={StaticResource BoolToVis}}"` |
| 极性 | `ComboBox ItemsSource="{Binding PolarityOptions}" SelectedValue="{Binding Polarity}" SelectedValuePath="Value"` + 显式 `ItemTemplate` |
| 超时 | `TextBox Text="{Binding TimeoutMs}"` |
| 试算 | 左栏 `Grid` 第二行固定区：`Button Command="{Binding TryRecognizeCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"`，code-behind 的 `RelayCommand` 只转发 `Plugin.TryPreviewRecognize()` |
| 结果区 | `Border` + `TextBlock Text="{Binding ResultSummary}"`，等宽 10.5px、`TextWrapping=Wrap` |
| 预览 | `ContentControl PreviewHost Content="{Binding DisplayImage}"` + `DataTemplate` 内 `h:ImageReadOnly HImage="{Binding Mode=OneWay}"` |
| 工具条 | `FitWindow_Click` / `FitImage_Click` 从 `PreviewHost` 可视树取当前 `ImageReadOnly`，调用现有 `FitToWindow()` / `FitToImage()` |
| 状态栏 | 状态点 + `Hint` + `Symbology`（等宽；经 `CodeSymbologyDisplayConverter` 显示 `DisplayName`，查不到时回退存盘值；`ActualWidth < 620` 时折叠） |
| 输出端口 | 静态 `Codes` / `Count` / `Text` 说明，不新增绑定 |

### 复刻约束（已遵守）

1. 保留 `CodeReaderView` 构造签名与 `DataContext = this`（`CodeReaderPlugin`）；`Loaded` 仍调用 `plugin.OnViewLoaded()`。
2. `LinkableValueEditor` 只包裹、不改内部；不设 `BrowseCommand`（插件没有浏览命令）。
3. `ImageReadOnly` 由 `DataTemplate` 承载；**不用** `Visibility=Collapsed` 显隐已设图的控件；`HImage` 显式 `Mode=OneWay`。
4. `Symbology` / `Polarity` / `TimeoutMs` 绑定类型与路径不变；`Is2D` 仍只控制二维码参数卡可见性。
5. 试算仍调用 `TryPreviewRecognize()`；没有新增「载入」「打开文件夹」「放大/缩小」等按钮。
6. 不修改共享控件、共享样式、共享字典；不修改 `PluginConfigShellView`。
7. 视图内零十六进制颜色；局部样式只引用公共令牌。

## 9. 已知偏差

1. **图标字体**：Web 用内联 SVG（Lucide 风格），WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`）；形状不同，语义一致。
2. **预览图**：Web 用内联 SVG 工业读码示意图；WPF 用真实 `HImage`，无图时为空态。
3. **预览头文件名**：插件没有 `PreviewImagePath` / 文件路径属性，WPF 固定显示「上游图像」与「只读 · 不参与定位」；Web 稿同样不伪造文件名。
4. **状态栏**：本插件没有 `StatusLevel`，WPF 状态点由 `ResultSummaryBrushConverter` 按结果文本做视觉映射（成功/无码/失败/中性），左侧显示 `Hint`；不新增状态属性或枚举。
5. **结果区三态**：WPF 结果区颜色由 `ResultSummaryBrushConverter` 按 `ResultSummary` 文本前缀映射公共令牌画刷（绿/橙/红/中性）；只做视觉表达，不新增状态枚举/属性。Web 稿的绿/橙/红结果区与之一致。
6. **加载骨架 / 尺寸 / 放大缩小 / 打开文件夹 / 系统导航**：均需新增功能或契约，见 §10。
7. **渲染证据范围**：`ImageReadOnly` 是 HALCON `HwndHost`，`RenderTargetBitmap` 不捕获其内容；批量冒烟 PNG 展示的是 WPF 布局（空态/配置区），真实图像预览需真机窗口。
8. **窄档滚动条**：WPF 左栏滚动条约占 17px，640 窄档可用宽度比 Web 少约 17px。
9. **链接入口**：WPF 沿用 `LinkableValueEditor` 自带的 🔗 图标，没有另造 Web 稿里的方形链接按钮；`Port`/`StepData`/`SetLink`/`RemoveLink` 行为原样。
10. **无图文案（已修）**：2026-10-20 遗留项 CRD3 已把无图试算提示改为「先在『图像输入』绑定上游图像，或点宿主『执行』带图进来再试算；本节点没有『载入』按钮，图像只来自上游」；帮助手册已同步。

## 10. 未纳入本版（需新增功能或契约）

| # | 项 | 需要什么 |
| --- | --- | --- |
| D1 | 加载骨架 / 蓝色加载态 | VM 新增 `IsPreviewLoading` 或 `PreviewState`；当前只有 `Hint` / `ResultSummary` |
| D2 | 图像尺寸 / 文件大小 | VM 新增只读属性（`HImage.GetImageSize` / `FileInfo.Length`） |
| D3 | 放大 / 缩小按钮 | `HalconBase` 只有 `FitToWindow()` / `FitToImage()`，没有公开 zoom 方法 |
| D4 | 打开所在文件夹 / 系统导航 | 新增 `Process.Start` 或宿主导航契约；本插件图像来自上游端口，没有文件路径 |
| D5 | 试算三档状态 / 耗时 | VM 新增 `StatusLevel` / 耗时属性；本版不扩展状态枚举 |

> 本插件**不做定位**，因此没有 ROI / 框选；`ImageReadOnly` 保持只读，刻意不用 `ImageEdit`。

## 11. 验证

| 项 | 结果 |
| --- | --- |
| 设计基线 | `design.html` v1（自包含、可点击；默认/空态/无码/失败/一维/窄档六种演示状态）；浏览器实测 1D/2D、空态、失败、窄档与「试算一下」交互均正常 |
| 插件构建 | `dotnet build Plugins\Plugin.CodeReader\Plugin.CodeReader.csproj`：**0 错误 / 0 警告**；`Modules\Plugin.CodeReader.dll` 已刷新 |
| 批量冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.CodeReader`：**11/11 PASS**（视图实例化 198 elements、`DataContext=CodeReaderPlugin`、1 个 `LinkableValueEditor.Port=Image`、`Button.Command=TryRecognizeCommand`、ComboBox/TextBox 绑定、`OnConfirm` 可执行、XAML hex=0、渲染 PNG） |
| 补充运行时探针 | 临时探针（不随仓库）：3 个 ComboBox（内部枚举框/码制 20 项选中 `Data Matrix ECC 200`/极性 3 项选中 `any`）；设置 64×48 `HImage` 后 `ImageReadOnly` 由模板创建且 `HImage` 引用相等；`OnConfirm` 写回 `Symbology=QR Code`、`TimeoutMs=750`、`Polarity=dark_on_light` |
| 渲染证据 | `evidence/after-render-v1-initial.png`（批量冒烟初始态）；`evidence/design-default-960.png`、`evidence/design-narrow-640.png`（设计页浏览器截图） |
| gate | `dotnet run --project FlowCanvasChecks --no-build`：**2690 通过 / 1 失败**；失败项为调度类的既有偶发断言「跳过留下了 Warn」（`定时真跑流程` 重复触发告警），与 CodeReader 视图改动无关。本插件只改视图与一条 nullability 标注，未触碰该链路。 |

## 12. 变更规则

设计基线确认后只读。需要调整设计时升版本（v2），并同步更新已复刻的 `CodeReaderView.xaml`、本映射表与 `docs/code-changes/` 记录。

## 13. 2026-10-20 遗留项修复（CRD1 / CRD2 / CRD3）

- **CRD1（窄档状态栏）**：状态栏右侧码制 `TextBlock` 命名 `SymbologyText`；code-behind 在 `SizeChanged` 中按 `ActualWidth < 620` 折叠，把宽度让给 `Hint`。
- **CRD2（码制显示名）**：新增 `CodeSymbologyDisplayConverter`，状态栏 `Text` 走 `CodeSymbologyTable.TryGet(value, out var s) ? s.DisplayName : value`；存盘值仍是 HALCON 原始名，界面显示中文/通用名，查不到时原样回退（不隐藏配置错误）。
- **CRD3（无图文案）**：`CodeReaderPlugin.TryPreviewRecognize()` 的无图提示改为「先在『图像输入』绑定上游图像，或点宿主『执行』带图进来再试算；本节点没有『载入』按钮，图像只来自上游」，保留「载入」二字以满足既有断言。
- **验证**：`dotnet build Plugins\Plugin.CodeReader\Plugin.CodeReader.csproj -v m` → 0 错误 / 0 警告；`UIThemeSmokeTest --plugin-views Plugin.CodeReader` → 11 条 PASS / 0 条 FAIL。
