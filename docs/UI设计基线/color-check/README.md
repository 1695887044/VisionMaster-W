# 颜色序列检查（ColorCheck）配置窗口 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 设计日期 | 2026-10-20 |
| 确认日期 / 人 | 待用户确认 |
| 覆盖范围 | `Plugins\Plugin.ColorCheck\ColorCheckView.xaml`（插件内容区）+ 宿主 `PluginConfigShellView`（960×680 外壳） |
| 窗口尺寸 | 960×680 默认；640×680 窄档；宿主最小 **640×560**（2026-10-20 统一口径） |
| 设计文件 | `design.html`（v1，自包含、无外部依赖、可点击；内联 SVG 图标与线束示意画布） |
| 复刻状态 | 已按本基线完成 WPF 复刻（2026-10-20），见「复刻实现」与「验证」 |
| 设计语言 | 沿用 ImageAcquisition V2：两栏 / 分区与分隔线 / 36px 控件 / 12px 标签 / 11.5px 帮助文字 / 8px 圆角 / 公共令牌 / 五态 |

## 1. 定位

本文件是「插件自定义视图 Web 化」的第 ① 步产物（Web 设计基线）。Web 只作为设计媒介，**不进入构建、不部署**。
确认后按本基线做第 ③ 步（WPF 复刻），第 ④ 步以「同一屏、同一状态」的截图对照验收。

本次只改 `Plugin.ColorCheck` 的配置视图：**不改插件业务逻辑、端口名、命令名、持久化，也不改共享控件/共享字典**。

## 2. 现状审查（改动理由）

| # | 现状（改动前） | 影响 |
| --- | --- | --- |
| 1 | 左栏 5 张 `PluginCard` 竖排（图像输入 / 采样区 / 检查参数 / 配方 / 怎么用 / 本轮范围），右栏只有一张 `ImageEdit` 卡片（`ColorCheckView.xaml:52-329`） | 信息层级平均；「怎么用 / 本轮范围」长期占屏，主操作被推低 |
| 2 | 采样区只有一行 `RoiSummary` + 「清空采样区」，没有「已框选 / 未框选」状态 | 用户无法一眼判断当前有没有可用的采样区 |
| 3 | 检查参数、高级参数、配方、用法说明全部铺开 | 5 项序列参数 + 13 项颜色阈值永远占屏；试算结果与提示混在参数区 |
| 4 | 试算结果是一块 11px Consolas 文本，没有状态语义 | 「尚未试算 / 试算失败 / 合格 / 不合格」不易分辨 |
| 5 | 画布无空态：无上游图时是一整块黑底 | 用户不知道图从哪来；旧「怎么用」还提到已不存在的「示意图路径 / 载入」入口 |
| 6 | 控件高度 24/26px、标签 11px，与宿主 Fluent 外壳及 ImageAcquisition V2 的节奏不一致 | 视觉密度偏旧，窄窗口下更明显 |
| 7 | 根布局 `MinWidth=700 MinHeight=360`，左列固定 330px | 窄档下左栏与画布比例不协调；宿主内容区变化时不自适应 |

## 3. v1 方向

> **一屏完成配置，画布优先；只做纯 UI 重构，不改功能入口与绑定。**

1. **两栏固定分工**：左 356px 配置轨（图像输入 / 采样区 / 检查参数 / 高级参数 / 配方 / 用法 / 输出），右侧采样画布自适应。
2. **图像输入单一入口**：`LinkableValueEditor` 保留 `Port` / `StepData`，`Label=""` 外置标签；不新增「载入本地图」。
3. **采样区独立区块**：已框选 / 未框选 chip + `RoiSummary` + 「清空采样区」；ROI 仍由 `ImageEdit` 右键创建、拖拽改大小。
4. **检查参数表单化**：36px 控件、12px 标签、11.5px 帮助；`ReverseSequence` / `WireCount` / `Regularize` 原属性名不变。
5. **试算结果独立成块**：`ResultSummary` 等宽多行；按文案分 Info / Success / Warning / Error 四色（不新增 `StatusLevel`）。
6. **高级参数折叠**：5 项序列分析参数、`ExtendEnds`、13 项阈值收进 `Expander`；显示窗口移到左栏末尾「输出」（随滚动），仍是 int（0~9）。
7. **画布常驻**：`ImageEdit` 不折叠、不换控件；空态是纯 WPF 覆盖层，`HImage` / `DrawObjectList` / `ActiveRoi` 原绑定。
8. **全令牌化**：颜色映射 `Colors.xaml` / `PluginConfigColors.xaml`；视图 XAML 零十六进制。

## 4. 设计要点（完整清单）

1. 左栏是「配置轨」：底色 `DialogRowAlternateBrush`、1px `DialogBorderLightBrush`、圆角 8；内部用细分隔线分组，不再层层套卡片。
2. 标签节奏：标签 12px、控件 36px、帮助 11.5px；字段间距 12px；输入区随栏宽伸缩。
3. 图像输入：`LinkableValueEditor` 设 `Label=""`，外置「上游图像」标签；只显示控件自带的 🔗（HImage 端口不可文本输入，也没有清除按钮）。
4. 采样区：标题右侧「已框选 / 未框选」chip；摘要用等宽 10.5px；「清空采样区」为整行次按钮；说明矩形长轴 = 线束走向、短轴 = 采样方向。
5. 检查参数：两行开关卡（序列反向 / 等间距规整化）；线数用「标签 + 92px 数字框」；「试算一下」主按钮整行；结果块独立。
6. 结果块：10.5px 等宽、行高 1.65、最大高度 170；Info 用 `DialogRowAlternateBrush`，Success 用绿色令牌，Warning 用橙色令牌，Error 用红色令牌。
7. 高级参数：`Expander` 承载暗线判据幅度、白线百分位、最小段长、最大段长比例、补色窗口比例、端点外推、13 项阈值。
8. 输出：显示窗口用 0~9 的 `ComboBox`，`SelectedItem` 仍绑 int；放在左栏末尾，随配置轨滚动。
9. 配方：多行等宽 `TextBox` 绑 `RecipeText`；「把试算结果存成配方」按钮；说明运行期按实测根数匹配、`ExpectedSequence` 端口优先。
10. 画布头：图标 chip + 「采样画布」+「上游图像 · ROI 编辑」；工具条只有「适应窗口（`FitToWindow`）」与「1:1（`FitToImage`）」。
11. 画布：深色底 `PluginPreviewCanvasBrush`；左上「ROI 编辑 · 右键新建矩形」角标；左下「长轴 = 线束走向 · 短轴 = 采样方向」图例；空态是虚线卡 + 说明，不提供假按钮。
12. 状态栏：主色信息点 + `Hint`（左，超长省略 + ToolTip 全文）+ `RoiSummary`（右）；本插件没有 `StatusLevel`，状态点固定为主色信息点，不伪造三档。
13. 底部宿主栏：状态胶囊（宿主 `StatusText`）+ 耗时 + 执行 / 取消 / 确认；插件视图不改它。
14. 数字等宽：线数、5 项序列参数、13 项阈值、ROI 摘要使用 tabular / 等宽字体。

## 5. 状态矩阵

| 状态 | 设计表现 | WPF 触发条件 |
| --- | --- | --- |
| 默认（有图 · 已框选 · 合格） | 画布显示上游图 + 矩形 ROI + 采样线；结果块绿色，显示根数 / 序列 / 线距 / 来源 / 判定 | `DisplayImage != null` 且 `RoiParams.Length == 5` 且 `ResultSummary` 含「判定：合格」 |
| 未框选（有图 · 无 ROI） | 画布显示图；采样区摘要「尚未框选采样区」；结果块「尚未试算」 | `DisplayImage != null` 且 `RoiParams.Length != 5` |
| 空态（未绑定上游） | 深色画布上的虚线空态卡；结果块「尚未试算」；状态栏提示绑定入口 | `DisplayImage == null` |
| 试算失败 | 结果块红色；画布仍是空态覆盖层（若同时无图）或保留当前图 | `ResultSummary` 以「试算失败」开头 |
| 判定不合格 | 结果块红色；结论行给出第一位不符 | `ResultSummary` 含「判定：不合格」 |
| 窄档 640 | 左栏 300px；画布头副标题隐藏；状态栏右侧 ROI 摘要隐藏 | 宿主内容区约 600px |

## 6. 契约保留（一个都不能改）

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 宿主靠 `viewObj.DataContext as IPluginConfigView` 取 `OnConfirm`；换独立 VM = 确认不保存 |
| C2 | `LinkableValueEditor`：`Port="{Binding Image}"`、`StepData="{Binding StepData}"` | 端口持久化、类型转换、变量链接、解除链接都在控件里；HImage 端口只能链接 |
| C3 | `ImageEdit`：`HImage="{Binding DisplayImage, Mode=OneWay}"`、`DrawObjectList="{Binding CanvasRois}"`、`ActiveRoi="{Binding CanvasActiveRoi, Mode=TwoWay}"` | ROI 创建 / 删除 / 拖拽、`WriteBackRoi` 回写、`DisplayImage` 释放语义都依赖这三条绑定 |
| C4 | 配置属性与类型：`RoiParams`(double[5]) / `ReverseSequence`(bool) / `WireCount`(int) / `Regularize`(bool) / `DarkSpanRatio`(double) / `BrightPercentile`(double) / `MinRun`(int) / `MaxRunRatio`(double) / `RuleWindowRatio`(double) / `ExtendEnds`(bool) / `DisplayViewIndex`(int) / 13 项阈值 / `RecipeText`(string) | `[StepConfig]` 反射与 `OnConfirm` 按属性名写回 `InputValues`；改类型会破坏 `.vms` 兼容 |
| C5 | 三个既有公开方法：`ClearRoi()` / `TryPreviewAnalyze()` / `SavePreviewAsRecipe()` | 视图只做命令转发，不复制任何业务逻辑 |
| C6 | 三处只读展示：`Hint` / `ResultSummary` / `RoiSummary` | 本插件没有 `StatusLevel`；不新增第四档、不伪造状态 |
| C7 | 视图 `Loaded → OnViewLoaded()` | 把上游图搬进画布的既有信号；不改初始化时序 |

## 7. 令牌映射

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--cc-surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--cc-surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--cc-surface-3` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--cc-border` | `#E2E8F0` | `DialogBorderBrush` |
| `--cc-border-soft` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--cc-border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` |
| `--cc-text` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--cc-text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--cc-text-3` | `#94A3B8` | `DialogTextMutedBrush` |
| `--cc-text-4` | `#64748B` | `ScadaSecondaryTextBrush`（`#66708A`，近似） |
| `--cc-primary` | `#0EA5E9` | `DialogAccentBrush` |
| `--cc-primary-strong` | `#0284C7` | `DialogAccentDarkBrush` |
| `--cc-primary-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--cc-primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--cc-primary-light` | `#E0F2FE` | `DialogAccentLightBrush` |
| `--cc-primary-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--cc-success` | `#22C55E` | `DialogSuccessBrush` |
| `--cc-success-text` | `#15803D` | `DialogSuccessTextBrush` |
| `--cc-success-soft` | `#F0FDF4` | `DialogSuccessSubtleBrush` |
| `--cc-success-border` | `#BBF7D0` | `DialogSuccessBorderBrush` |
| `--cc-danger` | `#EF4444` | `DialogDangerBrush` |
| `--cc-danger-text` | `#B91C1C` | `DialogDangerTextBrush` |
| `--cc-danger-soft` | `#FEF2F2` | `DialogDangerSubtleBrush` |
| `--cc-danger-border` | `#FECACA` | `DialogDangerBorderBrush` |
| `--cc-warning` | `#F59E0B` | `DialogWarningBrush` |
| `--cc-warning-text` | `#B45309` | `DialogWarningTextBrush` |
| `--cc-warning-soft` | `#FFF4E5` | `DialogWarningSubtleBrush` |
| `--cc-warning-border` | `#FDE68A` | `DialogWarningBorderBrush` |
| `--cc-canvas` | `#0B1220` | `PluginPreviewCanvasBrush` |
| `--cc-canvas-2` | `#1E293B` | `PluginPreviewSurfaceBrush` |
| `--cc-canvas-border` | `#334155` | `PluginPreviewBorderBrush` |
| `--cc-canvas-text` | `#E2E8F0` | `PluginPreviewTextBrush` |
| `--cc-canvas-muted` | `#94A3B8` | `PluginPreviewMutedTextBrush` |
| 圆角 6/8/10/12/16 | — | WPF `CornerRadius` 字面量（`PluginCardCornerRadius` = 8 用于卡片） |
| 间距 4/8/12/16 | — | WPF `Margin` / `Padding` 字面量 |
| 字体 | Segoe UI / Microsoft YaHei UI | 同族系统字体；等宽用 Cascadia Mono / Consolas |

**缺失令牌：无。** 本视图全部落在既有公共键内，未向 `Colors.xaml` / `PluginConfigColors.xaml` 追加任何键。

## 8. 复刻实现

| 设计项 | WPF 实现 |
| --- | --- |
| 两栏布局 | 根 `Grid`：左 `1*`（300~356）、右 `1.6*`（MinWidth 240）；左栏单个 `ScrollViewer` 承载全部配置，输出区在末尾 |
| 图像输入 | `cc:LinkableValueEditor Label="" Port="{Binding Image}" StepData="{Binding StepData}"`；外置「上游图像」标签 |
| 采样区 | 「已框选 / 未框选」chip（绑只读 `HasRoi` = `RoiParams.Length == 5`，`RoiStateConverter` 只做 bool→文案）+ `RoiSummary` + `ClearRoiCommand` |
| 检查参数 | `ReverseSequence` / `Regularize` 开关卡；`WireCount` 数字框；`TryAnalyzeCommand` 转发 `TryPreviewAnalyze()` |
| 结果块 | 只读 `TextBox` 绑 `ResultSummary`；`ResultToneConverter` 驱动 Info / Success / Warning / Error 四色 |
| 高级参数 | `Expander`：5 项序列参数、`ExtendEnds`、`ColorThresholdEditor`（13 项阈值） |
| 输出 | 左栏末尾 `ComboBox`：`ItemsSource={StaticResource ViewIndexes}`、`SelectedItem={Binding DisplayViewIndex}`（int） |
| 配方 | 多行 `TextBox x:Name="RecipeTextBox"` 绑 `RecipeText`；`SaveRecipeCommand` 转发 `SavePreviewAsRecipe()`，命令后 `UpdateTarget()` 刷新显示 |
| 画布 | `h:ImageEdit x:Name="ImageEditor"` 常驻；三条绑定原样；空态为覆盖层（不折叠控件） |
| 工具条 | 「适应窗口」`ImageEditor.FitToWindow()`、「1:1」`ImageEditor.FitToImage()`（`HalconBase` 既有方法） |
| 状态栏 | 主色信息点 + `Hint` + `RoiSummary`；不新增 `StatusLevel` |
| 令牌 | 视图 XAML 零十六进制；全部 `Dialog*` / `PluginPreview*` / `ScadaSecondaryTextBrush` |

### 复刻约束（已遵守）

1. **保留视图构造与生命周期**：`ColorCheckView()`、`DataContext = 插件实例`（`GetConfigView` 里赋值）、`Loaded → OnViewLoaded()` 原样保留。
2. **`LinkableValueEditor` 只包裹不改内部**：`Port` / `StepData` 原绑定；没有 BrowseCommand（本插件无文件浏览）。
3. **`ImageEdit` 保留**：控件常驻，`HImage` / `DrawObjectList` / `ActiveRoi` 三条绑定逐字保留；无图时用 WPF 覆盖层，不设 `Visibility=Collapsed`。
4. **配置属性与类型不变**：`RoiParams` / `ReverseSequence` / `WireCount` / `Regularize` / 5 项序列参数 / `ExtendEnds` / `DisplayViewIndex` / 13 项阈值 / `RecipeText`；`OnConfirm` 仍由 `VisionPluginBase` 统一写回。
5. **命令只转发**：`RelayCommand` 包装 `ClearRoi` / `TryPreviewAnalyze` / `SavePreviewAsRecipe`，不复制业务逻辑；无死按钮。
6. **不修改共享件**：`LinkableValueEditor`、`ImageEdit`/`HalconBase`、`ColorThresholdEditor`、`PluginConfigStyles.xaml`、`Colors.xaml`、`PluginConfigColors.xaml` 均未改。
7. **`RecipeText` 显示刷新**：`RecipeText` 是普通自动属性（无 INPC），命令执行后对 `RecipeTextBox` 调 `UpdateTarget()`，只刷新显示、不改模型。

## 9. 已知偏差（按 R130 记录）

1. **图标字体**：Web 用内联 Lucide 风格 SVG，WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`）；形状不同，语义一致。
2. **画布示意**：Web 用内联 SVG 彩色线束；WPF 用真实 `HImage`，由上游图像决定。
3. **深色画布**：`ImageEdit` 的 HALCON 窗口背景由控件决定；外层容器用 `PluginPreviewCanvasBrush`（`#0B1220`），图像区域可能呈近黑 letterbox。
4. **阈值编辑器**：`ColorThresholdEditor` 是共享控件（22px 输入框、自身 `#555555` 标签），本轮不改共享件；视觉上与 36px 表单节奏不一致。
5. **字体度量**：同字号下 Segoe UI / 微软雅黑与 Web 的字宽、行高略有差异，不按 px 硬套。
6. **滚动条**：WPF 左栏滚动条约占 17px，窄档可用宽度比 Web 少约 17px。
7. **工具提示**：Web 的 `data-tooltip` 由页面提供；WPF 用 `DialogToolTipStyle`。
8. **渲染证据范围**：`ImageEdit` 是 HALCON `HwndHost`，`RenderTargetBitmap` 不捕获其内容；批量冒烟 PNG 只证明 WPF 布局，真实图像预览需真机窗口验证。
9. **状态点**：本插件没有 `StatusLevel`；状态点固定为主色信息点，颜色不随 `Hint` 文本变化（不伪造三档）。
10. **结果四色是展示层推断**：`ResultToneConverter` 只按 `ResultSummary` 文案着色（试算失败 / 判定不合格 → 红；合格 → 绿；其它提示 → 橙或中性），不改变任何判定结果。
11. **共享右键菜单**：`ImageEdit` 自带「打开图像」等控件级菜单项（共享控件既有行为）；它只影响画布显示，不写插件的 `DisplayImage` / `RoiParams`。本算子的图像来源仍以上游 `Image` 端口为准，本轮不改共享控件。

## 10. 未纳入本版（记录，不改）

- **载入本地图**：当前图像只来自上游 `Image` 端口；不做文件选择旁路。
- **放大 / 缩小按钮**：`HalconBase` 只有 `FitToWindow()` / `FitToImage()`；本版只保留这两个工具条入口。
- **加载骨架 / 蓝色加载态**：插件没有 `IsPreviewLoading` / `PreviewState`；不新增第四种状态。
- **尺寸 / 文件大小**：插件没有对应只读属性；画布头只显示「采样画布 / 上游图像 · ROI 编辑」。
- **阈值编辑器换肤**：共享控件 `ColorThresholdEditor`，本轮不改。
- **640×560 最小档的完整布局**：宿主 MinHeight 已统一 560，需真机复核左栏滚动与画布高度。
- **配方下拉 / 配方管理**：当前只有多行文本，不新增配方选择器或文件导入。

## 11. 耦合矩阵（摘要）

完整矩阵见 `docs/code-changes/2026-10-20-ColorCheck-Web化.md` §2。

| 区域/元素 | 当前绑定/契约 | 功能依赖 | 可重构级别 |
| --- | --- | --- | --- |
| 根布局 | `MinWidth=560 MinHeight=360`（审查修复：原 700 在 640 宿主内容区 600 下会裁切；`ActualWidth<640` 时收起副标题/ROI 摘要） | 纯布局 | 自由（保留最小尺寸） |
| 图像输入 | `LinkableValueEditor.Port=Image` / `StepData` | 端口持久化、变量链接 | 只能包裹 |
| 采样区 | `ImageEdit.DrawObjectList=CanvasRois` / `ActiveRoi=CanvasActiveRoi` | ROI 创建 / 拖拽 / 回写 | 保留控件与绑定 |
| 预览 | `ImageEdit.HImage=DisplayImage`（OneWay） | HALCON 显示 / 缩放 / 释放 | 保留控件与绑定 |
| 检查参数 | `ReverseSequence` / `WireCount` / `Regularize` | `[StepConfig]` 持久化 | 只改外观 |
| 高级参数 | 5 项序列参数 / `ExtendEnds` / 13 阈值 | `[StepConfig]` + `ColorThresholdEditor` | 只改外观 |
| 输出 | `DisplayViewIndex`（int） | `[StepConfig]` + 投射 | 只改控件（TextBox → ComboBox） |
| 配方 | `RecipeText`（string） | `[StepConfig]` 持久化 | 只改外观 |
| 命令 | `ClearRoi` / `TryPreviewAnalyze` / `SavePreviewAsRecipe` | 真实功能入口 | 只做命令转发 |
| 状态 | `Hint` / `ResultSummary` / `RoiSummary` | 只读展示 | 自由（不新增档位） |

## 12. 验证

| 项 | 结果 |
| --- | --- |
| 设计基线 | `design.html` v1（自包含、可点击、默认 / 未框选 / 空态 / 试算失败 / 960·640 切换） |
| 设计证据 | `evidence/design-v1-default-960.png`、`design-v1-norei-960.png`、`design-v1-error-960.png`、`design-v1-narrow-empty-640.png` |
| 插件构建 | `dotnet build Plugins\Plugin.ColorCheck\Plugin.ColorCheck.csproj -v m`：**0 错误 / 0 警告**；`Modules\Plugin.ColorCheck.dll` 已刷新（2026-10-20 03:39，167,424 字节） |
| 批量视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.ColorCheck`：**11 条 PASS / 0 条 FAIL** |
| 冒烟要点 | `elements=227`；`LinkableValueEditor.Port` = `Image`；5 个按钮 / 3 个命令（`ClearRoiCommand`/`TryAnalyzeCommand`/`SaveRecipeCommand`）；`OnConfirm` 写回 `InputValues=27`；XAML `hex=0` |
| WPF 渲染证据 | `evidence/initial-render-920x520.png`（批量冒烟初始态；HALCON 画布不计入像素） |
| 常规 gate | `FlowCanvasChecks`：**2691 通过 / 0 失败**（首次运行有 1 条并行分支 `IsRunning` 偶发失败，与本插件无关；重跑 2691/0） |
| 真机 | 待用户验收：打开配置 → 链接上游 → 执行 → 框 ROI → 试算 → 存配方 → 确认 → 重开 |

## 13. 审查修复（2026-10-20）

| 项 | 修复 |
| --- | --- |
| H1 640 窄档 | 根 `MinWidth` 700→560；`SizeChanged` 在 `ActualWidth<640` 时收起画布头副标题与状态栏 ROI 摘要；640 宿主内容区 600 可容纳 |
| M1 画布图例 | 在画布头补「长轴 = 线束走向 · 短轴 = 采样方向」；位置为画布头而非设计稿左下，已记录为已知偏差 |
| M3 空态覆盖层 | 角标/覆盖层 DataTrigger 从 `DisplayImage` 改为 `ImageEditor.HImage`，右键「打开图片」后覆盖层会随控件 HImage 消失 |
| M2 插件专属断言 | 仍为遗留（批量冒烟固定 920×520，未断言 `ImageEdit` 三绑定 / `DisplayViewIndex` int / `RecipeTextBox.UpdateTarget`）；约定下次改本视图前补一条专属断言 |

## 14. 遗留问题核销（2026-10-20，CC5）

| 项 | 修复 |
| --- | --- |
| CC5-a 死分支 | `ResultToneConverter` 删除已不可能命中的 `text.Contains("先点「载入」")` 分支；当前插件空图文案只有「还没有图像：请先在『图像输入』里绑定上游端口或变量，或点『执行』把上游图载入画布，再试算」，走 `StartsWith("还没有图像")` 的 Warning 分支 |
| CC5-b 中文文案依赖 | 插件新增只读 `HasRoi => RoiParams != null && RoiParams.Length == 5`（在 `Initialize` / `OnRoiTuplesChanged` / `WriteBackRoi` 三处随 `RoiSummary` 一起通知）；采样区 chip 文案改绑 `HasRoi`，chip 的两种配色 DataTrigger 也从 `RoiSummary == "尚未框选采样区"` 改为 `HasRoi == False`。`RoiStateConverter` 现在只做 bool→「已框选 / 未框选」，不再解析任何中文 |
| 验证 | `Plugin.ColorCheck` 构建 **0 错误 / 0 警告**；`UIThemeSmokeTest --plugin-views Plugin.ColorCheck` **11/11 PASS**；`Modules\Plugin.ColorCheck.dll` 2026-10-20 07:44:42 / 166,400 字节 |

> 只改展示层判据与死分支，不改 ROI 业务、`RoiParams` 类型/长度约定、`OnConfirm` 或运行期判定。

## 15. CC4：试算使用画布当前图（2026-10-20）

| 项 | 内容 |
| --- | --- |
| 问题 | 画布右键「打开图片」换的是共享控件 `ImageEdit.HImage`（`HalconBase.OpenImage()` 直接写 `this.HImage`），而「试算一下」用的是插件的 `DisplayImage`（上游图副本）——两者分叉，**右键打开的图永远不参与试算** |
| 插件侧 | `ColorCheckPlugin` 新增 `internal void TryPreviewAnalyze(HImage? image)`：`source = image != null && image.IsInitialized() ? image : DisplayImage`；`public void TryPreviewAnalyze() => TryPreviewAnalyze(null)` 保持旧入口。**只读**：不持有、不 `Dispose`、不赋值给 `DisplayImage`；`TryAnalyze` 只读。无图提示补「也可以在画面上右键 → 打开图片，再试算（只影响试算，不改上游图像来源）」，原「载入」二字保留（旧断言不失效） |
| 为什么 internal | 两个 `public` 同名重载会让 `Type.GetMethod("TryPreviewAnalyze")` 抛 `AmbiguousMatchException`（断言宿主与所有反射调用方都会炸）；`internal` 不在默认绑定标志里 |
| 视图侧 | `TryAnalyzeCommand = new RelayCommand(_ => Plugin?.TryPreviewAnalyze(ImageEditor?.HImage));`——把画布当前图交给插件，插件不接管所有权 |
| 文案 | 「试算一下」ToolTip 改为"用画布当前这张图跑一遍（含右键 → 打开图片临时换的图）…只试算，不改上游图像来源"；画布空态补第二行"只想先看效果：在画面上右键 → 打开图片，再点「试算一下」（仅试算，不改上游图像来源）" |
| 契约 | 不改 `DisplayImage` 所有权/上游语义/持久化；`ImageEdit` 三条绑定逐字未变；视图仍无独立 VM |
| 断言 | `FlowCanvasChecks\ColorCheckSequenceCheck.cs` 的 `[CC4]` 7 条：P6a（无参入口可反射取到）/ P6b（重载 internal）/ **P2 传入图优先**（10 芯赢过 5 芯）/ **P4 只读**（`DisplayImage` 同一引用+尺寸不变）/ **P3 无参回退** / P5（提示含"打开图片"与"载入"）/ **P6c 静态扫描**（视图必须传 `ImageEditor?.HImage` 且无无参调用） |
| 变异反证 | 视图改回 `TryPreviewAnalyze()` → **P6c 准确变红**（其余 5 条仍绿）；恢复后全绿 |
| 已知偏差 | 无新增视觉偏差；空态多一行说明文字，仍居中在虚线卡内 |

## 变更规则

设计基线确认后只读。需要调整设计时升版本（v2），并同步更新 `ColorCheckView.xaml`、本映射表与 `design.html`。
