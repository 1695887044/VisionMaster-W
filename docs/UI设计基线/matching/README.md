# 模板匹配配置窗口 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 日期 | 2026-10-20 |
| 确认状态 | 待用户确认 |
| 覆盖范围 | `Plugins\Plugin.Matching\MatchingView.xaml`（插件内容区）；宿主外壳 `PluginConfigShellView` 不在本稿范围 |
| 窗口尺寸 | 960×680 默认；640×680 窄档；宿主最小 640×560 |
| 设计基线文件 | `design.html`（v1，自包含、内联 SVG、可点击演示） |
| 复刻状态 | 已按本基线重写 WPF 视图（2026-10-20）；构建 0/0、批量冒烟 11/11、FlowCanvasChecks 2691/0 |
| 证据 | `evidence/design-v1-960.png`、`evidence/design-v1-640.png`、`evidence/after-render-v1-initial.png` |

## 定位

按 `docs/UI设计基线/插件自定义视图Web化改造SOP.md` 的 §5.2 产出。Web 只是设计媒介，**不进入构建、不部署**。
视觉语言沿用 `image-acquisition` V2 基线：两栏、卡片、36px 控件、12px 标签、11.5px 帮助文字、8px 圆角、公共令牌、五态。
内容、字段、控件与状态按 **模板匹配** 的实际视图重画，不复用图像采集的字段。

## 现状审查（改动理由）

| # | 现状（改造前） | 影响 |
| --- | --- | --- |
| 1 | 左栏一张大卡片里堆「模板库 + 配方名 + 上游图像 + 参考图 + 模板摘要 + 创建/清除 + 涂抹编辑 + 画框要领」 | 首屏没有分组，模板库与提取参数混在一起；滚动很长 |
| 2 | 「提取参数」与「匹配参数」是两张同形卡片，仅靠标题区分 | 用户容易把「改完要重新学习」的创建态参数和「立即生效」的运行期参数混用 |
| 3 | 右侧只有 `ImageEdit` + 底部状态栏；模板预览缩在左栏 120px 高 | 参考图是主工作区，但模板预览（绿色特征边）不醒目 |
| 4 | 模板状态徽标只在左栏卡片头 | 右侧画布滚出视野后看不到「参数已改·需重新学习」 |
| 5 | 视图内仍有旧 `Plugin*` 灰阶样式与硬编码尺寸（`Height=92/120`、`MinWidth=64` 等） | 与 ImageAcquisition V2 的视觉语言不一致；换肤时改不动 |
| 6 | 模板预览 `Visibility` 随 `HasTemplatePreview` 折叠/展开 | 折叠态收到 `TemplatePreviewImage` 后恢复可能不重绘（与 ImageAcquisition 的 HImage 时序坑同族） |

## v1 方向

> 模板库优先、参数分层、参考图工作区；**只做纯 UI 重构，不改功能入口与绑定**。

1. **左栏 368px 配置轨**：模板库 → 提取参数 → 匹配参数 → 涂抹编辑 → 输出 → 怎么用，单列滚动。
2. **模板库成为第一屏**：列表（名称 + 已学习/未学习点）+ 新增 / 删除 / 设为默认 + 配方名 + 上游图像 + 参考图路径 + 当前区域摘要 + 创建/清除。
3. **参数按语义分层**：提取参数（创建态，改完要重新学习）与匹配参数（运行期，立即生效）分开；所有 `NumericBox` 与 `NumberValidationRule` 原样保留。
4. **右侧参考图工作区**：头部（参考图像 + 右键提示 + 模板状态 + 适应窗口/1:1）、深色画布、常驻模板预览条、状态栏。
5. **两个 HALCON 控件常驻**：`ImageEdit`（主画布/ROI/涂抹）与 `ImageReadOnly`（模板预览）都始终在可视树；无图/无预览时用纯 WPF 覆盖层，**不折叠已设图控件**。
6. **状态三态前置**：`未学习 / 已学习 / 参数已改·需重新学习` 由 `IsTemplateCreated` / `IsModelStale` / `TemplateStatusText` 驱动，左栏卡片头与右侧工作区头同时可见。
7. **全令牌化**：颜色映射 `Colors.xaml` / `PluginConfigColors.xaml` 既有公共键；本视图 XAML 零十六进制；不新增令牌。
8. **五态与可达性**：hover / pressed / focus / disabled 全量；图标按钮 30×30 且 `Padding="0"`（R68），焦点环走 `DialogFocusVisual`，ToolTip 走 `DialogToolTipStyle`。

## 设计要点（完整清单）

1. **模板库列表**：每行 30px，圆点表示该条目 `Model` 是否为空；选中行浅蓝底 + 主色文字。列表高 112px，超出滚动。
2. **模板状态胶囊**：未学习 = 中性灰；已学习 = 绿色浅底；参数已改 = 橙色浅底。胶囊同时出现在左栏「模板库」标题行与右侧工作区头部。
3. **表单节奏**：标签 12px / 控件 36px / 帮助 11.5px；字段间距 11px；输入区随栏宽伸缩。
4. **成对数值**：最小/最大角度、角度步长/梯度阈值、最小/最大比例、分数下限/匹配数上限、最大重叠/搜索激进度用两列网格，窄档自动压缩。
5. **上游图像 / 配方名端口**：WPF 由 `LinkableValueEditor` 承载，`Label=""` 外置标签；`Port` / `StepData` 原样保留。
6. **参考图路径**：文本框 + 「载入」按钮（`LoadPreviewImage`）；帮助文字说明「每个模板各自的示意图，仅配置态使用」。
7. **当前模板区域摘要**：等宽字体显示形状/中心/角度/半长半宽；未框选时显示「尚未框选模板区域」。
8. **创建/清除**：创建模板为主操作（实心主色），清除为次操作；不新增「学习全部」「批量重建」。
9. **涂抹编辑**：折叠区承载涂抹模式、笔刷半径（1~50）、清除涂抹；展开/收起不改变任何绑定。
10. **输出区**：配方名端口、运行显示窗口（0 = 不显示，1~9 = 图像窗口）、输出位姿归一化图像复选框。
11. **参考图工作区**：头部 46px；画布深色底 + 网格；左上「可编辑 · 参考图」chip；空态/错误态是覆盖层。
12. **模板预览条**：高 148px（窄档 124px），左侧预览框 + 右侧标题/说明/区域摘要/参数过期横幅；无预览时覆盖「创建模板后显示特征预览」。
13. **状态栏**：状态点 + `StatusMessage`（左）+ 当前条目名（右）；只有 Info / Warning / Error 三档。
14. **数字等宽**：角度、步长、分数、重叠、激进度、窗口编号使用 tabular-nums。
15. **底部宿主栏**：状态胶囊 + 耗时 + 执行/取消/确认，属于宿主外壳，插件视图不改。

## 契约保留（一个都不能改）

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 宿主靠 `viewObj.DataContext as IPluginConfigView` 取 `OnConfirm`；换独立 VM = 确认不保存 |
| C2 | `LinkableValueEditor`：`Image`（上游图像）/ `RecipeName`（配方名端口） | 端口持久化、类型转换、变量链接、`StepData.SetLink/RemoveLink` 都在控件里；只能包裹，不能替换。原视图未设置 `BrowseCommand`（两个端口无浏览语义），本版保持未设置，不造死按钮 |
| C3 | `ImageEdit`：`HImage=DisplayImage`、`DrawObjectList=CanvasRois`、`ActiveRoi=CanvasActiveRoi`、`BrushRadius=BrushSize`、`SmearDraw=SmearMask`、`SmearErase=SmearEraseRegion`、`SmearMode=CanvasSmearMode` | HALCON 画框/拖拽/右键菜单/涂抹全部依赖这些 DP；控件必须常驻，不能折叠 |
| C4 | `ImageReadOnly.HImage = TemplatePreviewImage` | 模板预览控件类型、HALCON 显示/缩放/右键菜单不变；`TemplatePreviewImage` setter 在 UI 线程释放旧图 |
| C5 | `Library` / `EditingEntry` / `DisplayViewIndex` / `SmearModeIndex` / `PolarityIndex` | 对象 / 对象 / int（0~9）/ int / int；类型不能换 |
| C6 | 命令与状态：新增 / 删除 / 设为默认 / 载入 / 创建模板 / 清除 / 清除涂抹 + `StatusMessage` / `StatusLevel` 三档 | 这些是功能入口；状态只有 Info / Warning / Error，本版不扩展 |

## 令牌映射

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--mt-surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--mt-surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--mt-surface-3` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--mt-border` | `#E2E8F0` | `DialogBorderBrush` |
| `--mt-border-soft` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--mt-border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` |
| `--mt-text` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--mt-text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--mt-text-3` | `#94A3B8` | `DialogTextMutedBrush` |
| `--mt-text-4` | `#64748B` | `ScadaSecondaryTextBrush`（近似） |
| `--mt-primary` | `#0EA5E9` | `DialogAccentBrush` |
| `--mt-primary-strong` | `#0284C7` | `DialogAccentDarkBrush` |
| `--mt-primary-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--mt-primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--mt-primary-light` | `#E0F2FE` | `DialogAccentLightBrush` |
| `--mt-primary-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--mt-success` | `#22C55E` | `DialogSuccessBrush` |
| `--mt-success-text` | `#15803D` | `DialogSuccessTextBrush` |
| `--mt-success-soft` | `#F0FDF4` | `DialogSuccessSubtleBrush` |
| `--mt-success-border` | `#BBF7D0` | `DialogSuccessBorderBrush` |
| `--mt-danger` | `#EF4444` | `DialogDangerBrush` |
| `--mt-danger-text` | `#B91C1C` | `DialogDangerTextBrush` |
| `--mt-danger-soft` | `#FEF2F2` | `DialogDangerSubtleBrush` |
| `--mt-danger-border` | `#FECACA` | `DialogDangerBorderBrush` |
| `--mt-warning` | `#F59E0B` | `DialogWarningBrush` |
| `--mt-warning-text` | `#B45309` | `DialogWarningTextBrush` |
| `--mt-warning-soft` | `#FFF4E5` | `DialogWarningSubtleBrush` |
| `--mt-warning-border` | `#FDE68A` | `DialogWarningBorderBrush` |
| 预览画布 `#0B1220` | — | `PluginPreviewCanvasBrush` |
| 预览表面 `#1E293B` | — | `PluginPreviewSurfaceBrush` |
| 预览描边 `#334155` | — | `PluginPreviewBorderBrush` |
| 预览文字 `#E2E8F0` | — | `PluginPreviewTextBrush` |
| 预览弱文字 `#94A3B8` | — | `PluginPreviewMutedTextBrush` |
| 圆角 6/8/10/12/16 | — | WPF `CornerRadius` 字面量（仓内暂无圆角令牌字典） |
| 间距 4/8/12/16/20/24 | — | WPF `Margin` / `Padding` 字面量 |
| 字体 | Segoe UI / Microsoft YaHei UI | 同族系统字体；等宽用 Cascadia Mono / Consolas |

**缺失令牌：无。** 本视图需要的颜色全部落在既有 `Dialog*` / `PluginPreview*` / `ScadaSecondaryTextBrush` 语义域内；未新增公共键。

## 复刻实现映射

| 设计项 | WPF 实现 |
| --- | --- |
| 左栏配置轨 | 单 `ScrollViewer` + `StackPanel`；各 section 用 1px 分隔线，不嵌套卡片 |
| 模板库列表 | `ListBox` + 本地 `TemplateListBoxStyle` / `TemplateListItemStyle`；`ItemsSource=Library`、`SelectedItem=EditingEntry` |
| 模板状态胶囊 | 本地 `StatusPillStyle` / `StatusPillTextStyle`；`DataTrigger` 读 `IsTemplateCreated` / `IsModelStale` |
| 上游图像 / 配方名端口 | `cv:LinkableValueEditor`，`Label=""` + 外置标签；`Port` / `StepData` 原绑定 |
| 参考图路径 + 载入 | `TextBox`（`DialogInput`）+ 按钮命令 `LoadImageCommand` → `LoadPreviewImage()` |
| 创建/清除 | `CreateTemplateCommand` / `ClearTemplateCommand` → 现有方法 |
| 提取/匹配参数 | `ui:NumericBox` + 本地 `LocalNumericBoxStyle`（保留 `NumberValidationRule`）；`DialogCombo`；`CheckBox` |
| 涂抹编辑 | `Expander` + `SmearModeOptions/Index`、`BrushSize`、`ClearSmearCommand` |
| 输出 | `RecipeName` 的 `LinkableValueEditor`、`ViewIndexes` + `DisplayViewIndex`、`OutputAlignedImage` |
| 主画布 | `h:ImageEdit` 常驻；全部 DP 逐字保留；空态/错误态为覆盖层 |
| 模板预览 | `h:ImageReadOnly` 常驻；`HImage=TemplatePreviewImage`；无预览时覆盖层，不切 `Visibility` |
| 状态栏 | `StatusMessage` / `StatusLevel` 三档；`StatusMessageConverter` 把空串显示为「就绪：载入参考图并框选模板区域」 |
| 工具栏 | 适应窗口 / 1:1：`FindVisualChild<ImageEdit>(MainCanvasHost)?.FitToWindow()/FitToImage()` |
| 颜色 | 视图 XAML 零硬编码十六进制；只引用公共令牌 |

## 复刻证据

| 项 | 结果 |
| --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.Matching\Plugin.Matching.csproj -v m`：**0 错误 / 0 警告** |
| 批量插件视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.Matching`：**11/11 PASS**（2 个 LinkableValueEditor.Port、10 个按钮/7 个命令、`OnConfirm` 15 个 InputValues、XAML hex=0） |
| 常规 gate | `dotnet run --project FlowCanvasChecks --no-build`：**2691 通过 / 0 失败** |
| 设计稿 | 浏览器实测 960/640 两档与三态切换；`evidence/design-v1-960.png`、`evidence/design-v1-640.png` |
| 视图初始态 | `evidence/after-render-v1-initial.png`（`--plugin-views` 输出；HALCON `HwndHost` 不计入像素） |

## 已知偏差（按 SOP R130 记录）

1. **图标字体**：Web 用内联 Lucide 风格 SVG；WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`）；形状不同，语义一致。
2. **输入高度**：Web 36px；WPF `DialogInput` / `DialogCombo` 为 38px（公共样式既定值），`NumericBox` 本地样式为 36px。
3. **模板预览**：Web 用内联 SVG 示意；WPF 用真实 `HImage`，由 `ImageReadOnly` 显示。`RenderTargetBitmap` 不捕获 HALCON `HwndHost`，证据 PNG 只证明 WPF 布局。
4. **画布右键菜单**：Web 不模拟；WPF 用 HALCON 原生右键菜单（新建矩形/圆形/椭圆、删除等），本视图不改菜单项。
5. **模板库状态点**：WPF 用 `Model` 是否为空驱动绿/灰点（`DataTrigger Value=""` 与 `Value="{x:Null}"` 两条同效果触发器，覆盖空串与 null），不新增「已学习」属性；设计稿展示同样的语义。
6. **折叠区**：涂抹编辑/怎么用使用 WPF `Expander` / 原生 `Expander` 头，未复刻 Web 的自绘 `<details>` 头。
7. **窄档**：640 宽度下右侧工作区头部隐藏「右键 → 新建 ROI」与状态胶囊、状态栏隐藏当前条目名、模板预览条 148→124px（WPF 由视图 `IsNarrow` DP + `SizeChanged` 阈值 `<700` 实现）；左栏 WPF 为 320px（列最小宽；设计稿 300px 为 Web 演示口径），内容仍可滚动。根 `MinWidth=600`，对应宿主 640 窗口内容区（640−2×20 边距）。
8. **工具提示**：Web 的 `data-tooltip` 由演示层提供；WPF 用 `DialogToolTipStyle`。
9. **模板预览高度**：Web 148px；WPF 148px（窄档 124px），实际可用高度随宿主内容区变化。
10. **状态栏右侧条目名**：Web 固定「当前条目：模板1」；WPF 绑定 `EditingEntry.Name`，未选中时为空（实际至少有一条默认条目）。
11. **数字显示**：Web 输入框展示格式化值；WPF `NumericBox` 按 `DecimalPlaces`/绑定值显示，未额外做千分位。
12. **「当前模板区域」摘要**：Web 用固定示例；WPF 绑定 `TemplateSummary`，按矩形/圆/椭圆三种形状输出不同文案。

## 未纳入本版（记录，不改）

- **加载骨架 / 蓝色加载态**：需要 VM 新增 `IsPreviewLoading` 或 `PreviewState`；`StatusLevel` 只有三档。
- **图像尺寸 / 文件大小**：需要 VM 新增只读属性。
- **放大 / 缩小按钮**：`HalconBase` 只有 `FitToWindow()` / `FitToImage()`，没有公开 zoom 方法。
- **打开所在文件夹**：需要新增 `Process.Start("explorer.exe", ...)` 行为。
- **系统导航（相机设置等）**：需要新的宿主导航契约。
- **模板库导入 / 导出 / 批量复制**：需要新的持久化/命令契约。
- **撤销 / 重做**：画布 ROI 与参数没有撤销栈。
- **模板库条目拖拽排序**：需要新的集合操作契约。
- **模型学习进度 / 取消学习**：`CreateTemplate()` 同步执行，无进度与取消契约。
- **多画布并排 / 图像缩放比例显示**：需要新的视图状态或 HALCON 公开属性。

## A 组遗留问题修复（2026-10-20）

| ID | 修复 |
| --- | --- |
| MA1 | 根 `Grid` 加 `Typography.NumeralAlignment="Tabular"`，数值输入与统计数字统一等宽。 |
| MA2 | 模板库两处 `DataTrigger Binding="{Binding Model}" Value=""` 各补一条 `Value="{x:Null}"` 同效果触发器（灰点 / 「未学习」），不改 `Model` setter。 |

**未做**：MA4（图标装饰）按约定不做。

验证：`dotnet build Plugins\Plugin.Matching\Plugin.Matching.csproj -v m` **0 错误 / 0 警告**；`--plugin-views Plugin.Matching` **11/11 PASS**（`elements=495`）。

## 变更规则

设计基线确认后只读。需要调整设计时升版本（v2），并同步更新 `MatchingView.xaml`、本 README 的「复刻实现映射」与「已知偏差」。
