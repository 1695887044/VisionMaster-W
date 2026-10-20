# ROI 创建配置窗口 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 日期 | 2026-10-20 |
| 确认日期 / 人 | 待用户确认（沿用 ImageAcquisition V2 视觉语言，本页记录插件级设计） |
| 覆盖范围 | `Plugins\Plugin.CreateRoi\CreateRoiView.xaml`（插件内容区） |
| 宿主尺寸 | 960×680 默认；640×680 窄档；宿主最小 640×560（`PluginConfigShellView` 已统一） |
| 设计文件 | `design.html`（自包含、无外部依赖、可点击：960/640 宽度 + 默认 / 掩膜预览 / 涂抹中 / 空态 / 无 ROI 五个场景） |
| 复刻状态 | 已完成（2026-10-20）；证据 `docs/UI设计基线/_batch-evidence/Plugin.CreateRoi-CreateRoiPlugin.png`；设计仍待用户确认 |

## 定位

本文件是 `docs/UI设计基线/插件自定义视图Web化改造SOP.md` §5.2 的插件级产物：
Web 只是设计媒介，不进入构建、不部署。视觉语言沿用 `docs/UI设计基线/image-acquisition/design.html` 的
两栏 / 卡片 / 36px 控件 / 12px 标签 / 11.5px 帮助文字 / 8px 圆角 / 五态，但**字段、控件、状态全部按
`Plugin.CreateRoi` 的实际视图重画**，不是共享图的复制。

## 现状审查（改动理由）

| # | 现状（`CreateRoiView.xaml` 249 行） | 影响 |
| --- | --- | --- |
| 1 | 左列固定 320px，三张卡片（图像输入 / ROI 列表 / 参数微调）上下堆叠 | 宽窗口下左列不随内容伸展；ROI 列表与参数卡争夺高度 |
| 2 | ROI 列表只有 `DisplayMemberPath="DisplayText"` 一行文本 | 名称与形状挤在一行，形状识别弱；列表没有计数 |
| 3 | 参数微调用 `UniformGrid Columns=2`，但每行标签 + 输入框挤在 160px 内 | 角度、半长等长标签被截断，数值可读性差 |
| 4 | 预览工具条只有两组 `RadioButton` + `CheckBox`，没有分组容器 | 原图/掩膜、涂抹模式、笔刷、清除涂抹混在两条分隔线里，层级弱 |
| 5 | 无空态提示；无图时画布是一块纯黑区域 | 用户不知道要先接入图像或右键打开图片 |
| 6 | 无 ROI 选中时参数卡只剩标题 | 没有"先选一个 ROI"的引导 |
| 7 | 颜色依赖 `Plugin*` 旧令牌（#1F1F1F 等），未统一到 Dialog 语义色 | 与 ImageAcquisition V2 的视觉语言不一致 |

## v1 方向

> ROI 编辑优先，一屏完成配置；**只做纯 UI 重构，不改功能入口与绑定**。

1. **两栏固定分工**：左 340px 配置轨（图像输入 + ROI 列表 + 参数微调 + 输出），右侧 ROI 工作区自适应。
2. **ROI 列表成为主区**：占据左栏剩余高度；行内显示形状图标 + 名称 + 形状中文名；顶部显示计数与操作提示。
3. **画布工具条分层**：第一行「原图 / 掩膜预览 + 排除模式」；第二行「正常 / 绘制涂抹 / 擦除涂抹 + 笔刷 + 清除涂抹」。
4. **参数微调双列**：五个参数按形状动态生成，两列排布；无选中时显示引导文案。
5. **空态用提示条**：无图时在画布上方显示提示条，不折叠 `ImageEdit`；画布始终常驻。
6. **输出固定在左栏底部**：显示窗口仍绑 int 的 `DisplayViewIndex`（0~9）。
7. **全令牌化**：颜色映射 `Colors.xaml` / `PluginConfigColors.xaml`；只在本视图派生样式，不改共享字典。
8. **五态与可达性**：hover / pressed / focus / disabled 全量定义；图标按钮 30×30 且 `Padding="0"`（R68），焦点环走 `DialogFocusVisual`。

## 设计要点（完整清单）

1. **图像输入**：外置标签「源图像」+ `LinkableValueEditor`（`Port=SrcImage`、`StepData`）；帮助文字说明"来自上游端口的 HImage，运行期以链接值为准"。
2. **ROI 列表**：卡片头「ROI 列表 + 计数徽标 + Del 删除选中」；行 = 22px 形状图标 + 名称 + 形状中文名；选中行主色浅底；底部「删除 / 清空」。
3. **参数微调**：卡片头右侧显示当前 ROI 名称；五个参数两列排布（矩形：中心行/中心列/角度/半长/半宽；圆形：圆心行/圆心列/半径；椭圆：圆心行/圆心列/旋转角/长半轴/短半轴）。
4. **输出**：固定在左栏底部，分隔线上方；「显示窗口」下拉 0 = 不显示，1~9 = 主界面视图窗口。
5. **预览头**：画布图标 + 「ROI 编辑画布」+ 「原图/掩膜预览 · N 个 ROI」+ 适应窗口 / 1:1（只接既有 `FitToWindow()` / `FitToImage()`）。
6. **预览工具条**：两行；分段控件（segmented）表达互斥状态；「排除模式（ROI 外有效）」在右侧。
7. **画布**：深色底 + 网格；`ImageEdit` 常驻；左上角「ROI 编辑」chip；无图时上方黄色提示条（不覆盖 HwndHost）。
8. **涂抹态**：右上角显示「绘制涂抹/擦除涂抹 · 笔刷 N px」胶囊；笔刷半径输入框保留 `NumericInput` 数字过滤。
9. **状态栏**：状态点 + 「选中：ROI_x · 形状」/「未选中 ROI」+ 右侧「共 N 个 ROI」；本插件没有 `StatusLevel`，不伪造三档。
10. **底部宿主栏**：状态胶囊 + 耗时 + 执行 / 取消 / 确认；插件视图不改宿主外壳。
11. **数字等宽**：参数值、笔刷、ROI 计数使用 tabular-nums。

## 契约保留（一个都不能改）

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 宿主靠 `viewObj.DataContext as IPluginConfigView` 取 `OnConfirm`；换独立 VM = 确认不保存 |
| C2 | `LinkableValueEditor`：`Port="{Binding SrcImage}"`、`StepData="{Binding StepData}"` | 端口持久化、类型转换、变量链接、`SetLink/RemoveLink` 都在控件里 |
| C3 | `ImageEdit` 常驻 + `HImage="{Binding DisplayImage, Mode=OneWay}"`、`DrawObjectList="{Binding CanvasRois}"`、`ActiveRoi="{Binding CanvasActiveRoi, Mode=TwoWay}"`、`SmearDraw/SmearErase` 双向 | HALCON 画布/ROI 交互、句柄、涂擦、右键菜单都依赖这些绑定；**不得用 `Visibility=Collapsed` 显隐已设图的控件** |
| C4 | `MaskInvert` / `IsMaskPreview` / `SelectedRoi` | `MaskInvert` 是 `[StepConfig]` 布尔；`IsMaskPreview` 是视图态；`SelectedRoi` 是 `RoiItem` 对象；类型不变 |
| C5 | `DeleteSelectedCommand` / `ClearRoisCommand` / `ClearSmearCommand` + `Delete` 键绑定 | 现有功能入口，不新增死按钮 |
| C6 | `DisplayViewIndex` int（0~9） | `[StepConfig]` 持久化类型；下拉 `SelectedItem` 绑 int 数组 |
| C7 | `RoiList` / `SelectedRoi.ParamEntries` | 集合变更回写、参数微调 `ParamEdited`、动态输出端口重建都依赖它们 |

## 令牌映射

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
| `--cr-text-4` | `#64748B` | `ScadaSecondaryTextBrush`（#66708A，近似） |
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
| `--cr-canvas` | `#0B1220` | `PluginPreviewCanvasBrush` |
| `--cr-canvas-2` | `#1E293B` | `PluginPreviewSurfaceBrush` |
| `--cr-canvas-border` | `#334155` | `PluginPreviewBorderBrush` |
| `--cr-canvas-text` | `#E2E8F0` | `PluginPreviewTextBrush` |
| `--cr-canvas-muted` | `#94A3B8` | `PluginPreviewMutedTextBrush` |
| 圆角 6/8/10/12 | — | `PluginCardCornerRadius` / `PluginSmallCornerRadius` + 局部 `CornerRadius` 字面量（仓内暂无圆角令牌字典） |
| 间距 4/8/12/16/20/24 | — | `Margin` / `Padding` 字面量（同上） |
| 字体 | Segoe UI / Microsoft YaHei UI | 同族系统字体；等宽用 Cascadia Mono / Consolas |

**结论：本插件不需要新增公共令牌。** 上表全部键在 `UI\Controls\Themes\Colors.xaml` / `PluginConfigColors.xaml` 中已存在；
视图 XAML 的十六进制颜色必须为 0（由 `--plugin-views Plugin.CreateRoi` 的静态扫描守门）。

## 复刻约束（已遵守 / 待复核）

1. **保留视图构造与生命周期**：`CreateRoiView()` 构造签名不变；`Loaded` 仍调用 `(DataContext as CreateRoiPlugin)?.OnViewLoaded()`。
2. **`LinkableValueEditor` 只包裹不改内部**：设 `Label=""` 后外置标签；保留 `Port` / `StepData`；本插件没有 `BrowseCommand`，不伪造。
3. **`ImageEdit` 常驻**：不放在 `ContentControl`、不设 `Visibility=Collapsed`；五个绑定逐字保留。
4. **`MaskInvert` / `IsMaskPreview` 绑定保留**：原图 RadioButton 仍走 `InverseBoolConverter` + `Mode=OneWay`，掩膜 RadioButton 仍 `Mode=TwoWay`。
5. **涂抹模式绑定保留**：`SmearMode` 通过 `ElementName=ImageEditor` + `SmearModeConverter`，参数 `None/Draw/Erase` 不变。
6. **笔刷绑定保留**：`Text="{Binding BrushRadius, ElementName=ImageEditor, UpdateSourceTrigger=PropertyChanged}"` + `NumericInput.Enabled`。
7. **参数微调绑定保留**：`ItemsSource="{Binding SelectedRoi.ParamEntries}"`，`Text="{Binding Text, UpdateSourceTrigger=LostFocus}"`。
8. **命令与状态**：`DeleteSelectedCommand` / `ClearRoisCommand` / `ClearSmearCommand` 与 `Delete` 键绑定原样；状态栏不新增枚举。
9. **不修改共享字典/控件**：`Colors.xaml`、`PluginConfigColors.xaml`、`LinkableValueEditor`、`ImageEdit`、`HalconBase`、`PluginConfigShellView.xaml` 未动。
10. **验证清单**：构建 0/0；`--plugin-views Plugin.CreateRoi` 全 PASS；渲染 PNG 到 `_batch-evidence`；reviewer 只读复核。

## 复刻证据

| 项 | 结果 |
| --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.CreateRoi\Plugin.CreateRoi.csproj -v m`：**0 错误 / 0 警告**；`Modules\Plugin.CreateRoi.dll` 时间戳已刷新（2026-10-20 02:52:45） |
| 视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.CreateRoi`：**10/10 PASS**（XAML hex=0、`DataContext`/`IPluginConfigView`、`LinkableValueEditor.Port`、命令/下拉/TextBox 绑定、`OnConfirm`、渲染 PNG；视图实例化 190 elements） |
| 渲染 | `docs/UI设计基线/_batch-evidence/Plugin.CreateRoi-CreateRoiPlugin.png`（初始空态）；`docs/UI设计基线/create-roi/evidence/after-render-v1-populated.png`（选中 ROI_1、2 个 ROI、5 个参数微调框的填充态）；`after-render-v1-narrow.png`（620×432 窄档，工具栏换行、左栏滚动）；HALCON `HwndHost` 不计入像素 |
| 常规 gate | `dotnet run --project FlowCanvasChecks --no-build`：**2691 通过 / 0 失败**（含 `[CreateRoi]` 段与视图实例化断言） |
| 设计页 | `design.html` 通过 `node --check` 脚本语法校验；五个场景与 960/640 宽度可点击切换 |

## 已知偏差（复刻时按 R130 记录）

1. **图标字体**：Web 用内联 Lucide 风格 SVG；WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`），形状不同、语义一致。
2. **阴影/动效**：Web 窗口多层 CSS 阴影；WPF 用宿主现有 `DropShadowEffect`；动效统一 150~200ms。
3. **预览图**：Web 用内联 SVG 工业法兰示意图；WPF 用真实 `HImage`。
4. **空态位置**：Web 在画布中央显示虚线空态卡；WPF 因 `ImageEdit` 是 HALCON `HwndHost`（airspace 限制，WPF 覆盖层无法可靠盖在 HwndHost 上），改为**画布上方提示条 + 状态栏文案**；`ImageEdit` 始终常驻，符合"不折叠已设图画布"的硬规则。
5. **渲染证据范围**：`RenderTargetBitmap` 不捕获 HALCON `HwndHost`；证据 PNG 展示的是 WPF 布局与空态/提示条，真实 ROI 预览需真机窗口验证。
6. **字体度量**：同字号下 Segoe UI / 微软雅黑与 Web 的字宽、行高略有差异，不按 px 硬套。
7. **滚动条**：WPF 左栏 ROI 列表滚动条约占 17px，窄档可用宽度比 Web 少约 17px。
8. **工具提示**：Web 的 `data-tooltip` 由页面自绘；WPF 用 `DialogToolTipStyle`。
9. **参数微调双列**：WPF 用 `UniformGrid Columns=2`，窄档下长标签会按 `TextTrimming` 省略，完整名称在 ToolTip 中。
10. **状态栏**：本插件没有 `StatusMessage` / `StatusLevel`，状态栏显示选中 ROI 与数量；不冒充宿主的执行状态。
11. **图像输入/输出字段标签**：Web 稿有「源图像」「显示窗口」标签与帮助文字；WPF 为在默认高度内给 ROI 列表/参数卡留空间，把图像输入标签与帮助收进字段 ToolTip，输出区保留「输出」标题、去掉「显示窗口」标签（下拉自解释）。
12. **ROI 列表操作提示**：Web 稿单独一行；WPF 收进卡片头右侧「右键新建 · Del 删除」，省出约 28px 给列表。
13. **左栏滚动**：短窗口（640×560 / 920×520 冒烟宿主）下左栏滚动；960×680 默认档实测 `ScrollableHeight=0`（临时探针），参数卡与输出完整可见。ROI 列表保底 200px 是刻意取舍。
14. **画布网格**：Web 稿在深色画布上绘制网格；WPF 的 `ImageEdit` 是 HALCON `HwndHost`，网格必须在共享控件内部绘制（或改 `HalconBase`），不属于低成本纯视图改动，本版**未实现、不伪造**；真实网格需后续共享控件迭代。
15. **涂抹态文字位置**：Web 稿在画布右上角浮层显示「绘制涂抹/擦除涂抹 · 笔刷 N px」；WPF 因 `HwndHost` airspace 限制，改放工具条（笔刷控件右侧），随 `SmearMode` 显示/隐藏。

## 未纳入本版（记录，不改）

- **加载骨架 / 蓝色加载态**：需要 VM 新增 `IsPreviewLoading` / `PreviewState`。
- **图像尺寸 / 文件大小**：需要 VM 新增只读属性（`HImage.GetImageSize` / `FileInfo.Length`）。
- **放大 / 缩小按钮**：`HalconBase` 只有 `FitToWindow()` / `FitToImage()`，没有公开 zoom 方法。
- **打开所在文件夹**：需要新增 `Process.Start("explorer.exe", ...)` 行为。
- **系统导航 / 前往相机设置**：本插件无相机配置。
- **拖拽入图 / 加载骨架**：不在本版；空态文案只承诺"接入上游图像 / 右键打开图片"。
- **ROI 名称内联重命名**：当前插件没有重命名命令；本版只读显示名称。

## 审查修复（2026-10-20）

| 项 | 修复 |
| --- | --- |
| CR1 形状通知 | `RoiItem.ShapeType` setter 补 `OnPropertyChanged(nameof(ShapeTypeCN))`；形状切换后列表行副标题立即刷新 |
| CR2 画布 chip / 涂抹态 | 「ROI 编辑」chip 从 `ImageEdit` 同 Grid 移到画布头（`HwndHost` 之外）；涂抹态文字移到工具条，按 `SmearMode` 显示「绘制涂抹 / 擦除涂抹 · 笔刷 N px」；画布网格未实现，已记入已知偏差 #14 |
| CR3 形状图标 | ROI 行图标按 `ShapeType` 用 DataTrigger 切换：矩形 `&#xf0c8;`、圆形 `&#xf111;`、椭圆用同一圆字形压扁（FA 6 Pro Solid 无独立 ellipse 字形），全部走 `DialogIconFont` |
| CR4 死资源 | 删除无引用的 `FieldLabelStyle` 与 `DisplayViewCombo` 的 `x:Name`（全文件检索确认零引用） |
| CR5 设计稿 | `design.html` 补 `.cr-mask.is-invert{filter:invert(1);}`，勾选「排除模式」时掩膜预览会反转显示 |

验证：`Plugin.CreateRoi` 构建 **0 错误 / 0 警告**；`UIThemeSmokeTest --plugin-views Plugin.CreateRoi` **11/11 PASS**；`Modules\Plugin.CreateRoi.dll` 2026-10-20 07:47:13 / 131,584 字节。

## 变更规则

设计基线确认后只读。需要调整设计时升版本（v2），并同步更新已复刻的 `CreateRoiView.xaml` 与本映射表。
