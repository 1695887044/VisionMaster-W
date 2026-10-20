# 区域颜色检查（ColorRegion）配置窗口 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 设计日期 | 2026-10-20 |
| 确认日期 / 人 | 待用户确认 |
| 覆盖范围 | `Plugins\Plugin.ColorRegion\RegionColorView.xaml`（插件内容区）+ 宿主 `PluginConfigShellView`（960×680 外壳） |
| 窗口尺寸 | 960×680 默认；640×680 窄档；宿主最小 **640×560**（2026-10-20 统一口径） |
| 设计文件 | `design.html`（v1，自包含、无外部依赖、可点击；内联 SVG 图标与示意画布） |
| 复刻状态 | 已按本基线完成 WPF 复刻（2026-10-20），见「复刻实现」与「验证」 |
| 设计语言 | 沿用 ImageAcquisition V2：两栏 / 卡片与分区 / 36px 控件 / 12px 标签 / 11.5px 帮助文字 / 8px 圆角 / 公共令牌 / 五态 |

## 1. 定位

本文件是「插件自定义视图 Web 化」的第 ① 步产物（Web 设计基线）。Web 只作为设计媒介，**不进入构建、不部署**。
确认后按本基线做第 ③ 步（WPF 复刻），第 ④ 步以「同一屏、同一状态」的截图对照验收。

本次只改 `Plugin.ColorRegion` 的配置视图：**不改插件业务逻辑、端口名、命令名、持久化，也不改共享控件/共享字典**。

## 2. 现状审查（改动理由）

| # | 现状（改动前） | 影响 |
| --- | --- | --- |
| 1 | 左栏是 5 张 `PluginCard` 竖排（图像输入 / 采样区 / 检查参数 / 怎么用 / 分工），右栏只有一张 `ImageEdit` 卡片（`RegionColorView.xaml:51-246`） | 信息层级平均；「怎么用 / 分工」长期占屏，主操作被推低 |
| 2 | 采样区只有一行 `RoiSummary` + 「清空」按钮，没有形状标识 | 用户看不出当前是矩形 / 圆形 / 椭圆，容易把圆形框成方框 |
| 3 | 检查参数、高级参数、用法说明全部铺开 | 13 项颜色阈值永远占屏；试算结果与提示混在参数区 |
| 4 | 试算结果是一块 11px Consolas 文本，没有状态语义 | 「尚未试算 / 试算失败 / 尚未配置期望」不易分辨 |
| 5 | 画布无空态：无上游图时是一整块黑底 | 用户不知道图从哪来；容易寻找不存在的「载入」入口 |
| 6 | 控件高度 24/26px、标签 11px，与宿主 Fluent 外壳及 ImageAcquisition V2 的节奏不一致 | 视觉密度偏旧，窄窗口下更明显 |
| 7 | 根布局 `MinWidth=700 MinHeight=360`，左列固定 330px | 窄档下左栏与画布比例不协调；宿主内容区变化时不自适应 |

## 3. v1 方向

> **一屏完成配置，画布优先；只做纯 UI 重构，不改功能入口与绑定。**

1. **两栏固定分工**：左 356px 配置轨（图像输入 / 采样区 / 检查参数 / 高级参数 / 用法），右侧采样画布自适应。
2. **图像输入单一入口**：`LinkableValueEditor` 保留 `Port` / `StepData`，`Label=""` 外置标签；不新增「载入本地图」。
3. **采样区独立区块**：形状 chip + `RoiSummary` + 「清空采样区」；ROI 仍由 `ImageEdit` 右键创建、拖拽改大小。
4. **检查参数表单化**：36px 控件、12px 标签、11.5px 帮助；`ExpectedColors` / `MinShare` 原属性名不变。
5. **试算结果独立成块**：`ResultSummary` 等宽多行；按文本内容分 Info / Warning / Error 三态着色（不新增 `StatusLevel`）。
6. **高级参数折叠**：`SamplePoints` / `DisplayViewIndex` / 13 项阈值收进 `Expander`；显示窗口仍是 int（0~9）。
7. **画布常驻**：`ImageEdit` 不折叠、不换控件；空态是纯 WPF 覆盖层，`HImage` / `DrawObjectList` / `ActiveRoi` 原绑定。
8. **全令牌化**：颜色映射 `Colors.xaml` / `PluginConfigColors.xaml`；视图 XAML 零十六进制。

## 4. 设计要点（完整清单）

1. 左栏是「配置轨」：底色 `DialogRowAlternateBrush`、1px `DialogBorderLightBrush`、圆角 8；内部用细分隔线分组，不再层层套卡片。
2. 标签节奏：标签 12px、控件 36px、帮助 11.5px；字段间距 12px；输入区随栏宽伸缩。
3. 图像输入：`LinkableValueEditor` 设 `Label=""`、`Height=36`，外置「上游图像」标签；只显示控件自带的 🔗（HImage 端口不可文本输入，也没有清除按钮）。
4. 采样区：标题右侧形状 chip（矩形 / 圆形 / 椭圆）；摘要用等宽 10.5px；「清空采样区」为整行次按钮。
5. 检查参数：期望颜色单行输入 + 帮助；最小占比用「标签 + 92px 数字框」；「试算一下」主按钮整行；「把主色填成期望颜色」次按钮整行。
6. 结果块：10.5px 等宽、行高 1.65、最大高度 150；Info 用 `DialogRowAlternateBrush`，Warning 用 `DialogWarningSubtleBrush`，Error 用 `DialogDangerSubtleBrush`。
7. 高级参数：`Expander` 承载采样点数上限、投射视图窗口、13 项阈值；显示窗口用 0~9 的 `ComboBox`，`SelectedItem` 仍绑 int。
8. 画布头：图标 chip + 「采样画布」+「上游图像 · ROI 编辑」；工具条只有「适应窗口（`FitToWindow`）」与「1:1（`FitToImage`）」。
9. 画布：深色底 `PluginPreviewCanvasBrush`；左上「ROI 编辑 · 右键新建」角标；空态是虚线卡 + 说明，不提供假按钮。
10. 状态栏：信息点（主色）+ `Hint`（左）+ `RoiSummary`（右）；本插件没有 `StatusLevel`，状态点固定为主色信息点，不伪造三档。
11. 底部宿主栏：状态胶囊（宿主 `StatusText`）+ 耗时 + 执行 / 取消 / 确认；插件视图不改它。
12. 数字等宽：最小占比、采样点数、阈值、ROI 摘要使用 tabular / 等宽字体。

## 5. 状态矩阵

| 状态 | 设计表现 | WPF 触发条件 |
| --- | --- | --- |
| 默认（有图 · 已框选） | 画布显示上游图 + ROI；结果块显示主色 / 占比 / 明细 / 判定 | `DisplayImage != null` 且 `RoiParams.Length == 5` |
| 空态（未绑定上游） | 深色画布上的虚线空态卡；结果块「尚未试算」；状态栏提示绑定入口 | `DisplayImage == null` |
| 无采样区 | 画布显示图；ROI 摘要「尚未框选采样区」；结果块「尚未试算」 | `RoiParams.Length != 5` 或 `CanvasRois.Count == 0` |
| 试算失败 | 结果块红色（`DialogDangerSubtleBrush` / `DialogDangerBorderBrush` / `DialogDangerTextBrush`） | `ResultSummary` 以「试算失败」开头（视图转换器判定） |
| 试算警告 | 结果块橙色（Warning 令牌） | `ResultSummary` 含「还没有图像 / 尚未配置期望颜色 / 无法判定」 |
| 窄档 640 | 左栏 300px；状态栏右侧 ROI 摘要隐藏（视图 `ActualWidth < 620` 时折叠） | 宿主内容区约 600px |

## 6. 契约保留（一个都不能改）

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 宿主靠 `viewObj.DataContext as IPluginConfigView` 取 `OnConfirm`；换独立 VM = 确认不保存 |
| C2 | `LinkableValueEditor`：`Port="{Binding Image}"`、`StepData="{Binding StepData}"` | 端口持久化、类型转换、变量链接、解除链接都在控件里；HImage 端口只能链接 |
| C3 | `ImageEdit`：`HImage="{Binding DisplayImage, Mode=OneWay}"`、`DrawObjectList="{Binding CanvasRois}"`、`ActiveRoi="{Binding CanvasActiveRoi, Mode=TwoWay}"` | ROI 创建 / 删除 / 拖拽、`WriteBackRoi` 回写、`DisplayImage` 释放语义都依赖这三条绑定 |
| C4 | 配置属性与类型：`ExpectedColors`(string) / `MinShare`(double) / `SamplePoints`(int) / `DisplayViewIndex`(int) / `RoiShape`(string) / `RoiParams`(double[5]) / 13 项阈值 | `[StepConfig]` 反射与 `OnConfirm` 按属性名写回 `InputValues`；改类型会破坏 `.vms` 兼容 |
| C5 | 三个既有公开方法：`ClearRoi()` / `TryPreviewAnalyze()` / `ApplyPreviewAsExpected()` | 视图只做命令转发，不复制任何业务逻辑 |
| C6 | 三处只读展示：`Hint` / `ResultSummary` / `RoiSummary` | 本插件没有 `StatusLevel`；不新增第四档、不伪造状态 |

## 7. 令牌映射

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
| `--cr-canvas` | `#0B1220` | `PluginPreviewCanvasBrush` |
| `--cr-canvas-2` | `#1E293B` | `PluginPreviewSurfaceBrush` |
| `--cr-canvas-border` | `#334155` | `PluginPreviewBorderBrush` |
| `--cr-canvas-text` | `#E2E8F0` | `PluginPreviewTextBrush` |
| `--cr-canvas-muted` | `#94A3B8` | `PluginPreviewMutedTextBrush` |
| 圆角 6/8/10/12/16 | — | WPF `CornerRadius` 字面量（`PluginCardCornerRadius` = 8 用于卡片） |
| 间距 4/8/12/16 | — | WPF `Margin` / `Padding` 字面量 |
| 字体 | Segoe UI / Microsoft YaHei UI | 同族系统字体；等宽用 Cascadia Mono / Consolas |

**缺失令牌：无。** 本视图全部落在既有公共键内，未向 `Colors.xaml` / `PluginConfigColors.xaml` 追加任何键。

## 8. 复刻实现

| 设计项 | WPF 实现 |
| --- | --- |
| 两栏布局 | 根 `Grid`：左 `1*`（300~356）、右 `1.6*`（MinWidth 240）；左栏 `ScrollViewer` 承载全部分区 |
| 图像输入 | `cv:LinkableValueEditor Label="" Port="{Binding Image}" StepData="{Binding StepData}"`；外置「上游图像」标签 |
| 采样区 | 形状 chip（`RoiShapeLabelConverter`；`WriteBackRoi` 写形状后补 `OnPropertyChanged(nameof(RoiShape))`）+ `RoiSummary` + `ClearRoiCommand`；`ClearRoi` 无 ROI 时归一为矩形 |
| 检查参数 | `ExpectedColors` / `MinShare` 原绑定；`TryAnalyzeCommand` / `ApplyExpectedCommand` 转发既有公开方法 |
| 结果块 | 只读 `TextBox` 绑 `ResultSummary`；`ResultToneConverter` 驱动 Info / Warning / Error 三态 |
| 高级参数 | `Expander`：`SamplePoints`、`DisplayViewIndex`（0~9 `ComboBox`）、`ColorThresholdEditor` |
| 画布 | `h:ImageEdit x:Name="ImageEditor"` 常驻；三条绑定原样；空态为覆盖层（不折叠控件） |
| 工具条 | 「适应窗口」`ImageEditor.FitToWindow()`、「1:1」`ImageEditor.FitToImage()`（`HalconBase` 既有方法） |
| 状态栏 | 主色信息点 + `Hint` + `RoiSummary`；不新增 `StatusLevel`；`RoiSummary` 在 `ActualWidth < 620` 时由 code-behind 折叠（窄档把宽度让给 Hint） |
| 令牌 | 视图 XAML 零十六进制；全部 `Dialog*` / `PluginPreview*` / `ScadaSecondaryTextBrush` |

### 复刻约束（已遵守）

1. **保留视图构造与生命周期**：`RegionColorView()`、`DataContext = 插件实例`（`GetConfigView` 里赋值）、`Loaded → OnViewLoaded()` 原样保留。
2. **`LinkableValueEditor` 只包裹不改内部**：`Port` / `StepData` 原绑定；没有 BrowseCommand（本插件无文件浏览）。
3. **`ImageEdit` 保留**：控件常驻，`HImage` / `DrawObjectList` / `ActiveRoi` 三条绑定逐字保留；无图时用 WPF 覆盖层，不设 `Visibility=Collapsed`。
4. **配置属性与类型不变**：`ExpectedColors` / `MinShare` / `SamplePoints` / `DisplayViewIndex` / `RoiShape` / `RoiParams` / 13 项阈值；`OnConfirm` 仍由 `VisionPluginBase` 统一写回。
5. **命令只转发**：`RelayCommand` 包装 `ClearRoi` / `TryPreviewAnalyze` / `ApplyPreviewAsExpected`，不复制业务逻辑；无死按钮。
6. **不修改共享件**：`LinkableValueEditor`、`ImageEdit`/`HalconBase`、`ColorThresholdEditor`、`PluginConfigStyles.xaml`、`Colors.xaml`、`PluginConfigColors.xaml` 均未改。
7. **`ExpectedColors` 显示刷新**：`ApplyPreviewAsExpected()` 写入后由插件补 `OnPropertyChanged(nameof(ExpectedColors))`（2026-10-20 遗留项 CRG2），视图保留 `ExpectedColorsBox.UpdateTarget()` 作为无害兜底；只刷新显示、不改模型。

## 9. 已知偏差（按 R130 记录）

1. **图标字体**：Web 用内联 Lucide 风格 SVG，WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`）；形状不同，语义一致。
2. **画布示意**：Web 用内联 SVG 工业面板；WPF 用真实 `HImage`，由上游图像决定。
3. **深色画布**：`ImageEdit` 的 HALCON 窗口背景由控件决定；外层容器用 `PluginPreviewCanvasBrush`（`#0B1220`），图像区域可能呈近黑 letterbox。
4. **阈值编辑器**：`ColorThresholdEditor` 是共享控件（22px 输入框、自身 `#555555` 标签），本轮不改共享件；视觉上与 36px 表单节奏不一致。
5. **字体度量**：同字号下 Segoe UI / 微软雅黑与 Web 的字宽、行高略有差异，不按 px 硬套。
6. **滚动条**：WPF 左栏滚动条约占 17px，窄档可用宽度比 Web 少约 17px。
7. **工具提示**：Web 的 `data-tooltip` 由页面提供；WPF 用 `DialogToolTipStyle`。
8. **渲染证据范围**：`ImageEdit` 是 HALCON `HwndHost`，`RenderTargetBitmap` 不捕获其内容；批量冒烟 PNG 只证明 WPF 布局，真实图像预览需真机窗口验证。
9. **状态点**：本插件没有 `StatusLevel`；状态点固定为主色信息点，颜色不随 `Hint` 文本变化（不伪造三档）。
10. **形状 chip 与清空**：无采样区时 `RoiShape` 归一为默认 `Rectangle`（`ClearRoi` / `WriteBackRoi` 都会通知），chip 与摘要一致；有 ROI 时 chip 随 `ShapeType` 更新。此前「画了圆 chip 仍显示矩形」的 P1 已修复（见 §12 与 code-changes §10）。
11. **共享右键菜单**：`ImageEdit` 自带「打开图像」等控件级菜单项（共享控件既有行为）；它只影响画布显示，不写插件的 `DisplayImage` / `RoiParams`。本算子的图像来源仍以上游 `Image` 端口为准，本轮不改共享控件。

## 10. 未纳入本版（记录，不改）

- **载入本地图**：当前图像只来自上游 `Image` 端口；不做文件选择旁路。
- **放大 / 缩小按钮**：`HalconBase` 只有 `FitToWindow()` / `FitToImage()`；本版只保留这两个工具条入口。
- **加载骨架 / 蓝色加载态**：插件没有 `IsPreviewLoading` / `PreviewState`；不新增第四种状态。
- **尺寸 / 文件大小**：插件没有对应只读属性；画布头只显示「采样画布 / 上游图像 · ROI 编辑」。
- **阈值编辑器换肤**：共享控件 `ColorThresholdEditor`，本轮不改。
- **640×560 最小档的完整布局**：宿主 MinHeight 已统一 560，需真机复核左栏滚动与画布高度。

## 11. 耦合矩阵（摘要）

完整矩阵见 `docs/code-changes/2026-10-20-ColorRegion-Web化.md` §3。

| 区域/元素 | 当前绑定/契约 | 功能依赖 | 可重构级别 |
| --- | --- | --- | --- |
| 根布局 | `MinWidth=700 MinHeight=360` | 纯布局 | 自由（保留最小尺寸） |
| 图像输入 | `LinkableValueEditor.Port=Image` / `StepData` | 端口持久化、变量链接 | 只能包裹 |
| 采样区 | `ImageEdit.DrawObjectList=CanvasRois` / `ActiveRoi=CanvasActiveRoi` | ROI 创建 / 拖拽 / 回写 | 保留控件与绑定 |
| 预览 | `ImageEdit.HImage=DisplayImage`（OneWay） | HALCON 显示 / 缩放 / 释放 | 保留控件与绑定 |
| 检查参数 | `ExpectedColors` / `MinShare` | `[StepConfig]` 持久化 | 只改外观 |
| 高级参数 | `SamplePoints` / `DisplayViewIndex` / 13 阈值 | `[StepConfig]` + `ColorThresholdEditor` | 只改外观 |
| 命令 | `ClearRoi` / `TryPreviewAnalyze` / `ApplyPreviewAsExpected` | 真实功能入口 | 只做命令转发 |
| 状态 | `Hint` / `ResultSummary` / `RoiSummary` | 只读展示 | 自由（不新增档位） |

## 12. 验证

| 项 | 结果 |
| --- | --- |
| 设计基线 | `design.html` v1（自包含、可点击、默认 / 空态 / 试算失败 / 三种形状 / 960·640 切换） |
| 设计证据 | `evidence/design-v1-default-960.png`、`evidence/design-v1-result-960.png`、`evidence/design-v1-cleared-960.png`、`evidence/design-v1-narrow-empty-640.png`（浏览器页面截图） |
| 插件构建 | `dotnet build Plugins\Plugin.ColorRegion\Plugin.ColorRegion.csproj -v m`：**0 错误 / 0 警告**；`Modules\Plugin.ColorRegion.dll` 已刷新（2026-10-20 02:58:16，102,912 字节） |
| P1 形状回环 | 独立探针：加圆形 ROI → `RoiShape=Circle`、`RoiParams=5`、通知 `RoiShape/RoiSummary`；`ClearRoi()` → `RoiShape=Rectangle`、`RoiParams=0`、通知 `RoiShape/RoiSummary` |
| 批量视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.ColorRegion`：**11 条 PASS / 0 条 FAIL**（2026-10-20 遗留项修复后复跑） |
| 冒烟要点 | `elements=190`；`LinkableValueEditor.Port` = `Image`；3 个命令（`ClearRoiCommand`/`TryAnalyzeCommand`/`ApplyExpectedCommand`）；`OnConfirm` 写回 `InputValues=20`；XAML `hex=0` |
| 渲染证据 | `docs/UI设计基线/_batch-evidence/Plugin.ColorRegion-RegionColorPlugin.png`（初始态；HALCON 画布不计入像素） |
| 真机 | 待用户验收：打开配置 → 链接上游 → 执行 → 框 ROI → 试算 → 确认 → 重开 |

## 变更规则

设计基线确认后只读。需要调整设计时升版本（v2），并同步更新 `RegionColorView.xaml`、本映射表与 `design.html`。
