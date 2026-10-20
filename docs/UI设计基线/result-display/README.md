# 结果显示配置窗口 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 设计日期 | 2026-10-20 |
| 确认日期 / 人 | 待用户确认 |
| 覆盖范围 | `Plugins\Plugin.ResultDisplay\ResultDisplayView.xaml` + `.xaml.cs`（插件内容区）；宿主外壳 `PluginConfigShellView.xaml` 只作为 960×680 / 640×560 参考，不改 |
| 宿主尺寸 | 默认 960×680；最小 640×560；插件内容区约 920×520 / 600×400 |
| 设计文件 | `design.html`（v1，自包含、无外部依赖、可点击、内联 SVG） |
| 复刻状态 | 已复刻（2026-10-20）；初始态证据见 `_batch-evidence/Plugin.ResultDisplay-ResultDisplayPlugin.png`；设计仍待用户确认 |
| 参考实现 | `docs/UI设计基线/image-acquisition/` v2 + `docs/code-changes/2026-10-20-图像采集配置窗口V2复刻与FluentChipDanger重复键修复.md` |

## 定位

按 `docs/UI设计基线/插件自定义视图Web化改造SOP.md` §5.2：本文件是**第 ① 步（Web 设计基线）**的产物。
Web 只是设计媒介，**不进入构建、不部署**。确认后按本基线复刻 WPF 视图；验收以「同一屏、同一状态」的截图对照为准。

本插件**没有 HALCON 画布、没有配置态预览**：结果显示只在运行期把标注烧进像素（`AnnotationBaker`），
配置期不出预览是有意的（离屏烧录必须开 HALCON 窗口，只能在流程线程上做）。因此本基线把右侧主工作区
留给**结果项列表**，不伪造一个预览面板。

## 现状审查（改动理由）

| # | 现状（改前） | 影响 |
| --- | --- | --- |
| 1 | 四张卡片纵向堆叠（底图 / 结果项 / 显示与判定 / 文字位置 / 高级），结果项表夹在中间 | 主次不分：最常编辑的结果项只占一条窄表，窗口一矮就被上下卡片挤走 |
| 2 | 结果项是六列窄表（26/110/*/62/46/28） | 右侧「取值」列被压到很窄，`LinkableValueEditor` 的链接地址与解除按钮容易挤在一起；窗口窄时基本不可用 |
| 3 | 显示窗口是裸 `TextBox`，0 与 1~9 的语义只靠 ToolTip | 新手把 0 当「第一个窗口」，运行后不显示才发现 |
| 4 | 判定项是裸可编辑 `ComboBox`，NG 值、图上叠字、文字位置分散在两卡 | 「判定 → 颜色」这条因果链看不出来；文字位置与字号/边距也不在同一视觉组 |
| 5 | 没有空态：结果项删空后只剩表头与空白 | 不知道下一步该点哪里 |
| 6 | 视图内联 `Plugin*` 旧键与零散字号（11/12/24 高按钮） | 与 V2 基线的 12/36/11.5 节奏不一致；换肤/统一调整改不动 |
| 7 | 「+ 添加一项」「✕」用 `Click` 事件，没有可绑定的命令 | 通用插件视图冒烟要求视图自身按钮至少有一条 `Command` 绑定；本轮用视图级命令转发既有公开方法 |

## v1 方向

> **左配置、右结果项**：左侧只回答「底图是谁、发到哪、怎么判定、文字放哪」，
> 右侧整块用于结果项列表（一行 = 一个输入端口 = 图上一段标注）。
> 只做纯 UI 重构，不改端口名、命令名、持久化与烧录逻辑。

1. **两栏固定分工**：左 356px 配置轨（可滚动）+ 右侧结果项工作区自适应。
2. **结果项卡片化**：每项一张两行卡（启用 / 名称 / 删除 + 取值 / 格式 / 单位），窄窗口不挤成一条缝。
3. **显示窗口下拉化**：`0 = 只烧录不发布` 与 `1~9 = 图像窗口` 明确分档，仍绑 int。
4. **判定与文字位置分组**：显示窗口 / 判定项 / NG 值 / 图上叠字同组；九宫格用 3×3 可点按钮表达，仍写 `TextAnchor` 枚举。
5. **边界值就地解释**：显示窗口 0、边距 0、字号 1.0、绘制上限 10~2000 都在字段下方写明后果。
6. **空态 / 停用态**：结果项为 0 时给虚线空态与添加入口；停用项保留配置、整行降透明度。
7. **全令牌化**：颜色映射 `Colors.xaml` / `PluginConfigColors.xaml`；视图 XAML 内 0 个十六进制颜色。
8. **五态与可达性**：hover / pressed / focus / disabled 全量；图标按钮 30×30（行内删除 26×26）且 `Padding="0"`（R68），焦点环 `DialogFocusVisual`，ToolTip `DialogToolTipStyle`。

## 设计要点（完整清单）

1. 左栏顺序：底图 → 显示与判定 → 文字位置 → 高级（默认收起）→ 「宁缺显示不停线」信息条。
2. 底图：外置标签「图像 SrcImage *」+ `LinkableValueEditor`；帮助文字说明「不改原图，产出全新的 AnnotatedImage」。
3. 显示窗口：10 档下拉（不显示 + 图像窗口 1~9）；帮助文字随选择变成「发布到主界面第 N 个图像窗口」或「0 = 只烧录不发布」。
4. 判定项：可编辑候选下拉（候选来自 `JudgeItemLabels`），首项「不判定」；帮助文字说明命中 NG 值后文字转红。
5. NG 值：文本输入；帮助文字说明数值 / 真假 / 文本三路比对口径。
6. 图上叠字：开关 + 说明「关掉只烧图形，文字仍进信息栏」；不新增状态枚举。
7. 九宫格：3×3 按钮，选中态主色描边 + 浅底 + 右上角对勾（WPF 用 `RadioButton` 卡片或 ComboBox + 预览格，见复刻映射）。
8. 边距：0 = 自动（画布高×2%）；字号倍率 0.5~3.0，1.0 = 按图高自适应；两者均保持原类型。
9. 高级：绘制上限 10~2000；说明轮廓点集另有 2000 点抽稀上限。
10. 结果项头：标题 + 一行说明 + 项数胶囊 + 「添加一项」主按钮。
11. 结果项卡：第一行启用勾选 / 名称 / 删除；第二行取值（`LinkableValueEditor`）/ 格式 / 单位。链接态显示浅蓝链接地址与改链/解除；手填态显示输入框与链接/清空。
12. 结果项空态：虚线卡 + 「还没有结果项」+ 添加入口；停用项整行 62% 透明度，仍可编辑。
13. 结果项脚注：说明哪些类型会变成文字、哪些画成图形、图像值请连 `SrcImage`。
14. 窄档 640：左栏 300px、右栏自适应；结果项卡第二行缩到「取值 1fr / 格式 50 / 单位 42」；标题副行与项数胶囊隐藏。
15. 底部宿主栏：宿主 `StatusText` + 耗时 + 执行 / 取消 / 确认；插件视图不改它。

## 状态清单（design.html 可点击）

| 状态 | 触发 | 界面表现 |
| --- | --- | --- |
| 默认 | 打开配置窗口 | 4 个结果项（3 启用 + 1 停用）；显示窗口 1；判定项「判定」；NG「NG」；锚点左上；边距 0；字号 1.0；绘制上限 300 |
| 空结果项 | 删空结果项 | 右侧虚线空态「还没有结果项」+「添加一项」；项数胶囊 0 项 |
| 边界值 | 点「边界值」 | 显示窗口 0（只烧录不发布）；判定项空（不判定）；边距 4096；字号 3.0；绘制上限 2000；锚点右下 |
| 停用项 | 取消某行「启用」 | 整行降透明度，配置保留、仍可编辑；运行期不建端口、不参与烧录 |
| 图上叠字关 | 关「图上叠字」 | 脚注改为「只烧图形，文字仍进信息栏」；几何/区域/XLD 照常画 |
| 窄档 640 | 点「640」 | 左栏 300px、右栏自适应；结果项卡第二行收窄；副标题与项数胶囊隐藏 |

> 这些状态全部来自插件现有属性/行为，不是设计稿自造的业务状态；本插件没有 `StatusLevel` / `StatusMessage`，设计稿不伪造状态栏。

## 契约保留（一个都不能改）

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 宿主靠 `viewObj.DataContext as IPluginConfigView` 取 `OnConfirm`；换独立 VM = 确认不保存 |
| C2 | `SrcImage`（`InputPort<HImage>`） | 底图唯一入口；`LinkableValueEditor` 的 `Port` / `StepData` 是链接、解除链接与类型转换的唯一入口 |
| C3 | 结果项动态端口（`r1/r2…`）+ 行内 `LinkableValueEditor` 的 `Port` 多绑定 | 端口名 = 稳定 `Id`，改名不断线；`PortsVersion` 是增删/改名后的重取锚点，不能换成普通 Binding |
| C4 | 全部 `[StepConfig]` 属性 | `DisplayViewIndex`（int 0~9）、`ShowTextOnImage`、`JudgeItemLabel`、`NgValue`、`MaxDrawElements`、`TextAnchor`、`TextMargin`、`TextScale`、`Items`；类型、默认值、持久化键不变 |
| C5 | `AddItem()` / `Items.Remove(item)` | 「添加一项 / 删除」只转发既有公开方法，不新增业务、不改集合语义 |
| C6 | 烧录与文字定位 | `AnnotationBaker`、`DisplayDraw`、`TextAnchorKind` 九宫格顺序与 R39 文字宽度能力一律不改 |

> 本插件**没有** `Mode` / `SelectedCameraOption` / `StatusLevel` / `StatusMessage` / HALCON 画布；
> 本版不新增、不伪造这些绑定。`SelectedItem` 仅用于 `DisplayViewIndex`（int）与 `TextAnchor`（枚举）。

## 令牌映射

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--rd-surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--rd-surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--rd-surface-3` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--rd-border` | `#E2E8F0` | `DialogBorderBrush` |
| `--rd-border-soft` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--rd-border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` |
| `--rd-text` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--rd-text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--rd-text-3` | `#94A3B8` | `DialogTextMutedBrush` |
| `--rd-text-4` | `#64748B` | `ScadaSecondaryTextBrush`（`#66708A`，近似） |
| `--rd-primary` | `#0EA5E9` | `DialogAccentBrush` |
| `--rd-primary-strong` | `#0284C7` | `DialogAccentDarkBrush` |
| `--rd-primary-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--rd-primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--rd-primary-light` | `#E0F2FE` | `DialogAccentLightBrush` |
| `--rd-primary-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--rd-success` | `#22C55E` | `DialogSuccessBrush` |
| `--rd-success-text` | `#15803D` | `DialogSuccessTextBrush` |
| `--rd-success-soft` | `#F0FDF4` | `DialogSuccessSubtleBrush` |
| `--rd-success-border` | `#BBF7D0` | `DialogSuccessBorderBrush` |
| `--rd-danger` | `#EF4444` | `DialogDangerBrush` |
| `--rd-danger-text` | `#B91C1C` | `DialogDangerTextBrush` |
| `--rd-danger-soft` | `#FEF2F2` | `DialogDangerSubtleBrush` |
| `--rd-danger-border` | `#FECACA` | `DialogDangerBorderBrush` |
| `--rd-warning` | `#F59E0B` | `DialogWarningBrush` |
| `--rd-warning-text` | `#B45309` | `DialogWarningTextBrush` |
| `--rd-warning-soft` | `#FFF4E5` | `DialogWarningSubtleBrush` |
| `--rd-warning-border` | `#FDE68A` | `DialogWarningBorderBrush` |
| 卡片圆角 8 | — | `PluginCardCornerRadius` |
| 图标字体 | — | `DialogIconFont`（FA6 Solid） |
| 焦点环 | — | `DialogFocusVisual` |
| 工具提示 | — | `DialogToolTipStyle` |
| 输入框 36px | — | `DialogInput`（TextBox）；`LinkableValueEditor` 内部固定 36px |
| 等宽数字 | — | `Typography.NumeralAlignment="Tabular"`（WPF 文本） |
| 间距 / 字号 / 圆角 6/10/12 | — | WPF 字面量（仓内暂无间距令牌字典） |

**缺失令牌：无。** 本设计只使用既有 `Dialog*` / `Scada*` / `Plugin*` 语义键；
`DialogBorderStrongBrush`、`DialogSuccessBorderBrush`、`DialogDangerTextBrush`、`DialogDangerBorderBrush`、
`DialogWarningBorderBrush` 已由 ImageAcquisition V2 追加到 `Colors.xaml`，本视图直接复用。
视图 XAML 静态扫描目标 **hex=0**。

## 复刻实现映射（第 ③ 步，已完成 2026-10-20）

| 设计项 | WPF 实现 |
| --- | --- |
| 两栏布局 | `Grid`：左 `1*`（`MinWidth=280` / `MaxWidth=356`）+ 右 `1.45*`（`MinWidth=250`）；间隔 16 |
| 底图 | `cv:LinkableValueEditor Label="" Port="{Binding SrcImage}" StepData="{Binding StepData}"` + 外置标签 |
| 显示窗口 | `ComboBox Style="{StaticResource DialogCombo}" ItemsSource="{StaticResource ViewIndexes}" SelectedItem="{Binding DisplayViewIndex}"`（int 不变） |
| 判定项 | `ComboBox IsEditable="True" Style="{StaticResource JudgeComboStyle}" ItemsSource="{Binding JudgeItemLabels}" Text="{Binding JudgeItemLabel}"`；`JudgeComboStyle` 只设尺寸、不设 `Template`，回落到框架默认可编辑模板（含 `PART_EditableTextBox`），保持可手填 |
| NG 值 | `TextBox Style="{StaticResource DialogInput}" Text="{Binding NgValue, UpdateSourceTrigger=PropertyChanged}"` |
| 图上叠字 | `CheckBox IsChecked="{Binding ShowTextOnImage}"` + 说明文字 |
| 九宫格 | `ComboBox Style="{StaticResource DialogCombo}" ItemsSource="{Binding TextAnchors}" SelectedItem="{Binding TextAnchor}"`，条目中文由 `EnumDisplayNameConverter` 读 `[Display]` |
| 边距 / 字号 / 绘制上限 | `TextBox` 绑 `TextMargin` / `TextScale` / `MaxDrawElements`，保持原类型与 `UpdateSourceTrigger` |
| 结果项列表 | `ItemsControl ItemsSource="{Binding Items}"` + 每项两行卡：`CheckBox Enabled`、`TextBox Label`、`LinkableValueEditor`（`Port` 多绑定 + `PortsVersion`）、`TextBox Format`、`TextBox Unit`、删除按钮 |
| 添加 / 删除 | 视图级 `AddItemCommand` / `DeleteItemCommand`（`ICommand`）分别转发 `Plugin.AddItem()` / `Items.Remove(item)`；只做 UI 转发 |
| 空态 | `Items.Count == 0` 时显示虚线空态；按钮同样绑 `AddItemCommand` |
| 停用态 | `DataTrigger Enabled=False` → 整行 `Opacity=0.62` + `DialogRowAlternateBrush` 底 |
| 令牌 | 视图零硬编码十六进制；全部走公共令牌 |
| 窄档 | 左栏 `MinWidth=280`、右栏 `MinWidth=250`；结果项卡第二行 `* / 58 / 46`；标题副行与项数胶囊在窄档仍保留（WPF 不做断点隐藏，靠 TextTrimming） |

### 复刻约束（已遵守）

1. **保留视图构造与生命周期**：`ResultDisplayView()` 默认构造；`GetConfigView` 仍 `new ResultDisplayView { DataContext = this }`，`Initialize(stepData)` 时序不变。
2. **`LinkableValueEditor` 只包裹不改内部**：底图与每行取值都保留 `Port` / `StepData`；行内 `Port` 仍是 `InputPortConverter` 多绑定（`Id` + 插件 + `PortsVersion`），改名不断线。
3. **全部 `[StepConfig]` 属性绑定不变**：`DisplayViewIndex` 仍 int（下拉 0~9），`TextAnchor` 仍枚举，`TextMargin` / `TextScale` / `MaxDrawElements` 仍原类型。
4. **添加 / 删除行为不变**：视图命令只转发 `AddItem()` 与 `Items.Remove(item)`，不新增业务。
5. **不引入独立 VM**：`DataContext` 仍是插件实例；新增的只有视图级 `ICommand` 与既有转换器。
6. **不改烧录 / 文字定位**：`AnnotationBaker`、`DisplayDraw`、`TextAnchorKind`、R39 九宫格与文字宽度逻辑一律未动。
7. **不修改共享文件**：`Colors.xaml`、`PluginConfigColors.xaml`、`PluginConfigShellView.xaml`、`UIThemeSmokeTest` 的 `Program.cs` / `.csproj` / `PluginViewSmoke.cs`、其它插件均未改。
8. **不造死按钮**：视图只有「添加一项 / 删除」两个真实动作；没有放大/缩小、打开文件夹、系统导航等无落点按钮。

## 已知偏差（复刻时按 R130 记录）

1. **图标字体**：Web 用内联 SVG（Lucide 风格），WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`）；形状不同，语义一致。
2. **九宫格控件**：Web 稿是 3×3 可点按钮；WPF 用 `ComboBox` + 中文条目（`EnumDisplayNameConverter`），避免为 9 个位置写 9 个 `RadioButton` 与额外转换器；写入的仍是 `TextAnchor` 枚举。
3. **判定项下拉**：Web 稿是标准 select；WPF 用可编辑 `ComboBox` + 本视图本地 `JudgeComboStyle`（只设尺寸、不设 `Template`，回落框架默认模板）以保留「手填名字」能力。**不要改回 `PluginModernComboBase` / UI 库隐式 ComboBox 样式**——两者模板都没有 `PART_EditableTextBox`，`IsEditable=True` 会静默失效；外观与 `DialogCombo` 略有差异。
4. **链接态外观**：Web 稿用浅蓝 `rd-link-field` 模拟；WPF 由 `LinkableValueEditor` 内部渲染链接地址（浅蓝下划线）与 🔗/✕ 按钮，外观以共享控件为准。
5. **结果项卡布局**：Web 稿第二行是「取值 / 格式 / 单位」三列；WPF 同构，但 `LinkableValueEditor` 自带 36px 高度与两个图标按钮，实际可用宽度比 Web 少约 50px。
6. **项数胶囊**：Web 稿按当前 DOM 行数实时更新；WPF 绑 `Items.Count`，由 `ObservableCollection` 通知。
7. **字体度量**：同字号下 Segoe UI / 微软雅黑与 Web 字宽、行高略有差异，不按 px 硬套。
8. **滚动条**：WPF 左栏与结果项列表滚动条约占 17px，窄档可用宽度比 Web 少约 17px。
9. **阴影/动效**：Web 窗口多层 CSS 阴影；WPF 用宿主现有 `DropShadowEffect`；动效统一 150~200ms。
10. **宿主外壳**：Web 稿包含标题栏与底部按钮条，仅用于展示上下文；插件视图只负责中间内容区，外壳不改。
11. **640×560 最小档**：宿主已统一 `MinHeight=560`；本稿 640 宽档按两列收窄展示，真机需复核左栏滚动与结果项卡换行。
12. **无配置态预览**：Web 稿没有伪造标注预览；右侧是结果项工作区。运行期烧录效果需真机执行后查看。
13. **分节提示对齐**：Web 稿的「唯一必接 / 发布·着色 / 九宫格」在节标题右侧；WPF 左栏有覆盖式滚动条，右对齐会被滚动条压住，改为紧跟节标题左对齐。
14. **底图行宽度**：Web 稿的 `LinkableValueEditor` 撑满栏宽；WPF 给底图行 `MaxWidth=280`，保证 🔗/✕ 两个图标不被左栏滚动条覆盖；结果项行内编辑器仍撑满卡片。

## 未纳入本版（记录，不改）

- **配置态实时预览 / 标注示意**：烧录要开离屏 HALCON 窗口，只能在流程线程执行；属功能变更。
- **加载骨架 / 进度态**：插件没有 `StatusLevel` / `StatusMessage`，也没有预览状态机；不新增第四档。
- **图像尺寸 / 文件大小**：配置态不加载底图，没有可绑定属性。
- **放大 / 缩小 / 打开文件夹 / 系统导航**：本插件没有 HALCON 画布或文件路径，均无落点。
- **结果项拖拽排序**：当前 `Items` 没有排序命令；排序会改变端口顺序与图上文字顺序，属功能变更。
- **判定项失效检测**：插件只按 Label 匹配，找不到时运行期不判定；没有可绑定的「失效」状态，视图不伪造警告。
- **批量启用 / 停用 / 清空**：属功能扩展，不在本次范围。

## 证据

| 项 | 结果 |
| --- | --- |
| 设计基线 | 本目录 `design.html`（v1，自包含、可点击） |
| 设计预览 | `evidence/design-preview-v1.png`（1280×1100 浏览器截图）；`evidence/state-empty.png`（空结果项态）；`evidence/state-edge.png`（边界值态）；也可直接用浏览器打开 `design.html`（960/640 宽度、默认/空结果项/边界值三态、九宫格与折叠区可点） |
| 复刻证据 | `docs/UI设计基线/_batch-evidence/Plugin.ResultDisplay-ResultDisplayPlugin.png`（批量冒烟初始态，920×520） |
| 真机 | 待用户按 SOP §7 手工回环：打开配置 → 改每个字段 → 确认 → 重开；链接/解除链接；试运行；关闭无崩溃/泄漏 |

## 变更规则

设计基线确认后只读。需要调整设计时升版本（v1 → v2），并同步更新已复刻的 `ResultDisplayView.xaml` 与本映射表。
