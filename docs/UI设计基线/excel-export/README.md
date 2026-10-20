# Excel 报表配置窗口 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 确认日期 | 待用户确认 |
| 确认人 | 待用户确认 |
| 覆盖范围 | `Plugins\Plugin.ExcelExport\ExcelExportView.xaml`（插件内容区） |
| 窗口尺寸 | 960×680 默认；640×680 窄档；宿主最小 640×560（`PluginConfigShellView.xaml`，本插件不改） |
| 设计基线文件 | `design.html`（自包含、无外部依赖、可点击；含默认/导出中/成功/失败/空路径五态） |
| 证据 | `evidence/design-preview.png`（设计稿预览）、`evidence/after-render-v1.png`（WPF 初始态渲染） |
| 当前产物指纹 | `Modules\Plugin.ExcelExport.dll`：**100,864 bytes / 2026-10-20 02:29:33**；SHA256 `62F2FDE9FB10E2EF35F83304188F2363A6954C13929466CFEDA89B972F2ACBFB` |
| 复刻状态 | 已完成（2026-10-20，含 P1 竞态修复）；验证结果见 `docs\code-changes\2026-10-20-ExcelExport-Web化.md` |
| 视觉母版 | `docs\UI设计基线\image-acquisition\design.html`（V2 视觉语言） |

## 定位

按 `docs\UI设计基线\插件自定义视图Web化改造SOP.md` 的四步流程，本文件是**第 ① 步（Web 设计基线）**的产物。
Web 只是设计媒介，**不进入构建、不部署**。本插件属于批次 1「低风险表单型」（清单文档第 5 项，初判 S）：
无 HALCON 画布、无 `NumericBox`、无 `PropertyGrid`/`DataGrid`，核心是 5 个文本属性、1 个枚举、5 个布尔属性、
1 个 `LinkableValueEditor`（`TriggerPort`）和 1 个「立即导出」入口。

## 现状审查（改动理由）

| # | 现状 | 影响 |
| --- | --- | --- |
| 1 | 四张卡片纵向堆叠（来源 / 输出 / 选项 / 立即导出），每张都从顶部开始铺满宽度 | 960 宽下大量横向留白；「立即导出」要滚到底才能看到，动作不前置 |
| 2 | 所有字段都是「左标签 + 右输入框」的固定 72px 标签列 | 路径这类长文本被标签列吃掉宽度；640 窄档更明显 |
| 3 | 账本存在性只有一行 11px 彩字，且颜色在 code-behind 里硬编码 `#2E7D32` / `#C62828` | 违反 R117 令牌化；状态不够显眼，路径配错时要靠导出失败才发现 |
| 4 | `LastResult` 只有一行文字，成功/失败靠前缀 `✔` / `✘` 着色 | 导出中、成功、失败、未导出四态没有形状差异；长路径和失败原因挤在一行 |
| 5 | 触发信号行只在 `OnTrigger` 时出现，但没有解释「为什么现在出现」 | 用户切到触发端口后不知道要连线，切走后不知道为什么会消失 |
| 6 | 五个 `CheckBox` 横向堆在一个 `StackPanel` 里 | 640 窄档第二行会溢出/被裁；「导出失败阻断流程」最容易被裁 |
| 7 | 视图内仍有 `#F5F9FE` 预览底色与 `#2E7D32` / `#C62828` 状态色 | 换肤/统一调整时改不动 |

## v1 方向

> 两栏工作台：左「导什么」（来源 + 输出），右「怎么导 + 现在导」（选项 + 立即导出）；
> 只做纯 UI 重构，不改功能入口、绑定与持久化。

1. **两栏固定分工**：左 1.45fr 配置轨（来源 / 输出），右 1fr 选项与动作轨；960 基本一屏；920×520 内容区左栏需少量滚动，640 窄档列内滚动。
2. **字段节奏统一**：标签 12px 在输入框上方、控件 36px、帮助 11.5px；长路径获得整行宽度。
3. **模板与实际路径成对**：每个路径字段下方一个只读预览块（「实际读取 →」「实际写出 →」），仍绑现有 `SourcePathPreview` / `OutputPathPreview`。
4. **账本状态前置**：`File.Exists` 校验升级为带图标的行内状态（绿 / 红 / 灰），颜色走 `DialogSuccessTextBrush` / `DialogDangerTextBrush` / `DialogTextMutedBrush`。
5. **导出方式条件行**：`OnTrigger` 时显示「触发信号」行，仍用 `LinkableValueEditor`；三种方式各有一句帮助文字，`EachRun` 用警示色提示高频流程慎用。
6. **选项自动换行**：五个 `CheckBox` 放进 `WrapPanel`，窄档不会裁字。
7. **立即导出变成动作卡**：主按钮 + 结果面板（空闲 / 导出中 / 成功 / 失败四态），结果面板只消费 `LastResult` 与 `IsExporting`，不新增状态属性。
8. **全令牌化**：颜色全部映射 `Dialog*` / `Plugin*` 公共键；视图 XAML `hex=0`；只在本视图派生样式，不改共享字典。

## 设计要点（完整清单）

1. 两栏：960 宽左 1.45fr / 右 1fr，间距 16；640 窄档间距 12、列内滚动。
2. 卡片：白底 + `DialogBorderBrush` 1px + 8px 圆角 + 16px 内边距；标题带 22px 浅蓝序号块。
3. 字段：标签 12px `DialogTextSecondaryBrush`；输入框 36px、8px 圆角、`DialogInput` 的 hover/focus/disabled 三态。
4. 路径预览：`DialogRowAlternateBrush` 底 + `DialogBorderLightBrush` 描边；「实际读取/写出」10.5px；路径 11px 等宽 `PluginMonoTextBrush`，允许换行。
5. 账本状态：✔ 绿 / ✘ 红 / info 灰，12px 图标 + 11px 文本。
6. 导出方式：`DialogCombo` 36px；下拉条目文案走 `ExportPolicyLabelConverter`，值仍是枚举成员。
7. 触发信号：`LinkableValueEditor` 原样嵌入；未连线时显示「需链接变量」占位（共享控件自带）。
8. 选项：`DialogCheckBox` 15px 方框 + 7px 间距；`WrapPanel` 行距 10、列距 18。
9. 立即导出：主按钮 36px 实心主色，禁用时 45% 透明；结果面板四态（空闲灰 / 导出中蓝 / 成功绿 / 失败红）。
10. 结果面板：24px 图标块 + 标题 11.5px 半粗 + 正文 11.5px；成功/失败同时显示路径或原因。
11. 宿主底栏：状态胶囊 + 耗时 + 执行/取消/确认，插件视图不改它。
12. 五态：hover / pressed / focus / disabled 全量定义；图标按钮 30×30 + `Padding=0`（R68）；焦点环 `DialogFocusVisual`；ToolTip `DialogToolTipStyle`。

## 契约保留（v1 一个都不能改）

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 宿主靠 `viewObj.DataContext as IPluginConfigView` 取 `OnConfirm`；换独立 VM = 确认不保存 |
| C2 | `LinkableValueEditor`：`Port=TriggerPort`、`StepData` | 端口持久化、类型转换、变量链接/解除链接都在控件里；本插件没有 `BrowseCommand`，不造浏览按钮 |
| C3 | `ExportPolicy` / `ExportPolicies` | 枚举值不能换；`SelectedItem` 仍绑枚举；条目显示中文但值仍是成员名 |
| C4 | 五个文本属性 | `SourceStepName` / `SourcePathTemplate` / `OutputDirectoryTemplate` / `ReportFileNamePattern` / `LinkColumn`，`UpdateSourceTrigger=PropertyChanged` |
| C5 | 五个布尔属性 | `FreezeHeader` / `AutoFilter` / `OnlyRowsWithImage` / `OpenAfterExport` / `BlockOnFailure`，默认值与语义不变 |
| C6 | `ExportNow()`（视图本地 `ExportNowCommand` 转发）/ `IsExporting` / `LastResult` | 唯一的动作入口与结果反馈；不新增插件命令、不改后台导出逻辑 |
| C7 | 只读预览属性 | `SourcePathPreview` / `OutputPathPreview` 仍由插件计算，视图只显示 |

> **本插件没有** `Mode`、`SelectedCameraOption`、`DisplayViewIndex`、`StatusLevel`；SOP §5.4 的通用清单里这几项不适用，**不虚构绑定**。状态反馈只有 `IsExporting` + `LastResult`，结果颜色仍由 `LastResult` 前缀（`✔` / `✘`）决定，与改造前一致。

## 令牌映射

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--ee-surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--ee-surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--ee-surface-3` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--ee-border` | `#E2E8F0` | `DialogBorderBrush` |
| `--ee-border-soft` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--ee-border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` |
| `--ee-text` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--ee-text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--ee-text-3` | `#94A3B8` | `DialogTextMutedBrush` |
| `--ee-text-4` | `#64748B` | `ScadaSecondaryTextBrush`（#66708A，近似） |
| `--ee-primary` | `#0EA5E9` | `DialogAccentBrush` |
| `--ee-primary-strong` | `#0284C7` | `DialogAccentDarkBrush` |
| `--ee-primary-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--ee-primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--ee-primary-light` | `#E0F2FE` | `DialogAccentLightBrush` |
| `--ee-primary-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--ee-success` | `#22C55E` | `DialogSuccessBrush` |
| `--ee-success-text` | `#15803D` | `DialogSuccessTextBrush` |
| `--ee-success-soft` | `#F0FDF4` | `DialogSuccessSubtleBrush` |
| `--ee-success-border` | `#BBF7D0` | `DialogSuccessBorderBrush` |
| `--ee-danger` | `#EF4444` | `DialogDangerBrush` |
| `--ee-danger-text` | `#B91C1C` | `DialogDangerTextBrush` |
| `--ee-danger-soft` | `#FEF2F2` | `DialogDangerSubtleBrush` |
| `--ee-danger-border` | `#FECACA` | `DialogDangerBorderBrush` |
| `--ee-warning` | `#F59E0B` | `DialogWarningBrush` |
| `--ee-warning-text` | `#B45309` | `DialogWarningTextBrush` |
| `--ee-warning-soft` | `#FFF4E5` | `DialogWarningSubtleBrush` |
| `--ee-warning-border` | `#FDE68A` | `DialogWarningBorderBrush` |
| 路径等宽字体 | — | `PluginMonoFontFamily` |
| 路径预览文字 | — | `PluginMonoTextBrush` |
| 卡片圆角 8 | — | `PluginCardCornerRadius` |
| 输入框 / 下拉 / 复选 | — | `DialogInput` / `DialogCombo` / `DialogCheckBox`（共享样式） |
| 焦点 / 工具提示 | — | `DialogFocusVisual` / `DialogToolTipStyle` |
| 图标字体 | — | `DialogIconFont` |

**缺失令牌：无。** 本插件只消费既有 `Dialog*` / `Plugin*` 键；视图 XAML 内 **0 个十六进制颜色**（静态断言由 `UIThemeSmokeTest --plugin-views Plugin.ExcelExport` 检查）。输入框的 8px 圆角来自共享 `DialogInput` 模板，不是本视图字面量。

## 复刻实现（第 ③ 步）

| 设计项 | WPF 实现 |
| --- | --- |
| 两栏工作台 | 根 `ScrollViewer` → `Grid`（1.45* / 16 / 1*），左右各一个 `StackPanel`；卡片为视图本地 `LocalCard` 样式 |
| 字段节奏 | 标签在输入框上方；`LocalInput`（BasedOn `DialogInput`，Height 36）/ `LocalCombo`（BasedOn `DialogCombo`，Height 36） |
| 路径预览 | `Border` + `PluginMonoFontFamily` / `PluginMonoTextBrush`；绑定 `SourcePathPreview` / `OutputPathPreview` |
| 账本状态 | `SourceExistsText` 保留；code-behind 按 `File.Exists` 设置文本/可见性/前景色（资源查找，失败回退 `Brushes`） |
| 导出方式 | `ComboBox ItemsSource="{Binding ExportPolicies}" SelectedItem="{Binding ExportPolicy}"`；`ItemTemplate` 走 `ExportPolicyLabelConverter` |
| 触发信号 | `Grid` + `EnumEqualsToVisibilityConverter`（`ConverterParameter=OnTrigger`）；`cv:LinkableValueEditor StepData/Port` 原样 |
| 选项 | `WrapPanel` + 五个 `CheckBox`（`DialogCheckBox`），五个布尔绑定原样 |
| 立即导出 | `Button Command="{Binding ExportNowCommand, RelativeSource=...}"` + `IsEnabled="{Binding IsExporting, Converter={StaticResource NotBool}}"`；结果面板 `ResultPanel` / `ResultIcon` / `ResultTitle` / `ResultText` / `ResultHint` |
| 状态色 | `RefreshResultVisual` 资源查找：空闲灰 / 导出中蓝 / 成功绿 / 失败红；不新增 `StatusLevel` |
| 令牌 | 视图零硬编码十六进制；颜色全部 `{StaticResource Dialog* / Plugin*}` |

### 复刻约束（已遵守）

1. **保留视图构造与生命周期**：`ExcelExportView()`、`DataContext = plugin`（插件 `GetConfigView` 里设置）、`plugin.Initialize(stepData)`（插件 `GetConfigView` 里调用）原样保留。
2. **`LinkableValueEditor` 只包裹不改内部**：`Port=TriggerPort`、`StepData` 原绑定；没有 `BrowseCommand`，因此没有浏览按钮。
3. **不引入独立 VM**：视图仍直接绑定插件实例；`IPluginConfigView` 由插件实现。
4. **不改业务逻辑**：`ExportNow()`、`RunExport()`、模板展开、端口、持久化全部未动。
5. **不造死按钮**：只保留「立即导出」这一个真实入口；浏览/打开文件夹/打开报表都没有新增。
6. **共享件不动**：`LinkableValueEditor`、`DialogInput`、`DialogCombo`、`DialogCheckBox`、`PluginConfigStyles.xaml` 均未改。

## P1 修复（2026-10-20 复核）

**症状**：导出完成后结果文本已显示成功/失败，但结果面板标题与配色仍停在「导出进行中」。

**根因**：
1. `ExcelExportPlugin.ExportNow()` 先写 `LastResult`（触发 `PropertyChanged`），后写 `IsExporting=false`；视图 `OnPluginPropertyChanged` 只监听 `SourcePathPreview` / `LastResult`，没有监听 `IsExporting`，第二次状态变化没有触发重绘。
2. `RefreshResultVisual()` 把 `exporting` 判断放在 `✔` / `✘` 前缀之前，且 `StartsWith` 未指定比较方式；`LastResult` 已是成功/失败时仍被 `exporting=true` 抢先判成「导出进行中」。

**修法**：
1. `OnPluginPropertyChanged` 增加 `IsExporting` 分支 → `RefreshResultVisual()`。
2. `RefreshResultVisual()` 判定顺序改为「`✔` 成功 → `✘` 失败 → `exporting || 非空` 导出中 → 空串未导出」。
3. 前缀判断全部使用 `StringComparison.Ordinal`。

**修复后验证**：插件构建 **0 错误 / 0 警告**；`--plugin-views Plugin.ExcelExport` **11/11 PASS**；DLL 指纹见上表。批量冒烟不覆盖「后台导出完成」时序，本条按代码路径复核；真机回环时重点确认导出完成后面板立即变为成功/失败。

## EE4 修复：手动导出的调度失败兜底（2026-10-20）

**症状（潜在）**：`Task.Run` 调度本身失败（线程池拒单、lambda 尚未进入内层 `try` 就被拆掉）时，原先只有 lambda **内部**的 `catch`，接不住这类异常。后果是异常抛给按钮事件（UI 线程）、`LastResult` 永远停在「正在导出…」、`IsExporting` 一直为 `true` → 按钮**永久置灰**且后续点击被 `if (IsExporting) return` 静默吞掉，界面上看不出任何错误。

**修法**：
1. `ExportNow()` 在 `Task.Run` **外层**加 `try/catch (Exception ex)`；catch 里 `LastResult = "✘ 导出异常：" + ex.GetType().Name + "：" + ex.Message;` + `IsExporting = false;` + `Debug.WriteLine`，**不 rethrow**。
2. lambda 内原有的 `try/catch/finally` **逐字保留**（业务异常与正常路径的 `IsExporting` 复位仍由它负责）。
3. 两层 `catch` 的文案同形（都以「✘ 导出异常：」开头，结果面板的失败判色不用改），调试输出用 `[Excel报表] 后台导出异常` / `[Excel报表] 导出调度失败` 区分是哪一层。

**边界**：只改调度兜底，不改 `RunExport`、模板展开、端口、持久化与视图绑定；结果面板四态判据不变。

**断言**：`FlowCanvasChecks\FlowEngineFixChecks.cs` 的 `[EE4]` 6 条（Task.Run 在外层 try 内 / 外层 catch 写 `LastResult` / 外层 catch 复位 `IsExporting` / 文案带 `ex.GetType().Name` / 内层 `finally` 仍在且 `IsExporting = false` 恰好两处）。变异反证：删掉外层 catch → ②③④ 准确变红（`IsExporting = false` 只剩 1 处）。

## 已知偏差（复刻时按 R130 记录）

1. **图标字体**：Web 用 Lucide 线性图标，WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`）；形状不同，语义一致。
2. **字体度量**：同字号下 Segoe UI / 微软雅黑与 Web 的字宽、行高略有差异，不按 px 硬套。
3. **滚动条**：WPF 两栏各自是 `StackPanel`，由外层 `ScrollViewer` 统一滚动；Web 稿两栏各自 `overflow-y:auto`。960 默认档右栏基本一屏，左栏输出卡需少量滚动；640 窄档纵向滚动更明显。
4. **卡片阴影**：Web 稿无卡片阴影；WPF `LocalCard` 也不加 `DropShadowEffect`，与宿主弹窗的阴影层级保持简单。
5. **下拉条目**：WPF 的 `DialogCombo` 选中项显示 `ExportPolicyLabelConverter` 的中文标签；Web 稿用原生 `select`，视觉由浏览器决定，展开态不作为验收像素。
6. **复选框**：Web 用原生 `input[type=checkbox]` + `accent-color`；WPF 用共享 `DialogCheckBox`（15px 方框 + 对勾 Path），选中态一致。
7. **结果面板图标**：Web 用 Lucide；WPF 用 Font Awesome（info / check / times / spinner）。导出中动画在 WPF 里不做旋转（避免新增动画代码），只显示静态 spinner 图标 + 文案。
8. **账本存在性**：WPF 在 `Loaded` 与 `SourcePathPreview` 变化时同步 `File.Exists`；Web 稿是演示状态切换。真实行为以 WPF 为准。
9. **宿主底栏**：设计稿里的执行/取消/确认是宿主 `PluginConfigShellView` 的静态示意，插件视图不实现、不改。
10. **导出按钮入口**：设计稿写的是 `ExportNow_Click`（Click 事件）；WPF 实际为视图本地 `ExportNowCommand`（`RelayCommand` → `Plugin.ExportNow()`）。原因是批量冒烟要求视图自有按钮至少有一个 `Command` 绑定；插件 API 与业务逻辑未改。
11. **卡片圆角**：设计稿卡片圆角 **12px**（`--ee-radius-xl`）；WPF 用 `PluginCardCornerRadius` = **8px**。公共令牌没有 12px 圆角键，按现有插件卡片统一，不新增共享令牌。
12. **内容区高度**：设计稿是 960×680 窗口（内容区约 920×552）；宿主 `PluginConfigShellView` 固定 72 标题 + 56 页脚 + 32 边距，WPF 实际内容区 **920×520**，左栏输出卡在默认档需要少量滚动（外层 `ScrollViewer` 统一滚动）。

## 未纳入本版（记录，不改）

- **浏览 / 选择目录**：插件没有 `BrowseCommand`，需要新增契约；本轮不造按钮。
- **打开报表 / 打开所在文件夹**：需要新增 `Process.Start` 行为；只有「导出后打开报表」布尔配置，不额外造按钮。
- **账本行数 / 文件大小 / 最近导出历史**：需要新增只读属性或持久化字段。
- **加载骨架 / 进度条**：现有只有 `IsExporting` 布尔，没有进度/阶段信息；用「正在导出…」结果态代替。
- **宿主 MinHeight**：已由 2026-10-20 统一提高到 560，本插件不再单独调整。

## 变更规则

设计基线确认后只读。需要调整设计时升版本（v2），并同步更新已复刻的 `ExcelExportView.xaml` 与本映射表。
