# C# 脚本配置窗口 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 日期 | 2026-10-20 |
| 确认日期 / 人 | 待用户确认 |
| 覆盖范围 | `Plugins\Plugin.CSharpScript\CSharpScriptView.xaml` + `.xaml.cs`（插件内容区） |
| 设计基线文件 | `design.html`（自包含、无外部依赖、可点击；内联 SVG 图标） |
| 窗口尺寸 | 960×680 默认；640×680 窄档；宿主最小 640×560（2026-10-20 统一） |
| 复刻状态 | 已完成（2026-10-20）；证据 `evidence/after-render-v1-*.png`；设计仍待用户确认 |
| 参考实现 | `docs/UI设计基线/image-acquisition/`（V2 Web 视觉语言） |

## 定位

本文件是 SOP §5.2 的 **Web 设计基线（v1）**。Web 只是设计媒介，**不进入构建、不部署**。
视觉语言沿用 ImageAcquisition V2：两栏、卡片、36px 控件、12px 标签、11.5px 帮助文字、8px 圆角、
公共令牌、五态（默认 / hover / pressed / focus / disabled）。内容、字段、控件与状态按
`Plugin.CSharpScript` 的实际视图重画，不复用采集页的业务内容。

## 现状审查（改动理由）

| # | 现状 | 影响 |
| --- | --- | --- |
| 1 | 左列固定 `460` + 右列 `MinWidth=360`（`CSharpScriptView.xaml:49-52`） | 最小宽度 820，宿主 640 档横向溢出；窄窗口下变量表右侧「显示窗口 / 备注 / 删除」被推出视口 |
| 2 | 输入变量行是 4 列网格（名字 120 / 类型 104 / 连线 * / 删除 30） | 在 460px 内每列都很紧；`LinkableValueEditor` 的链接图标与输入框挤在一起 |
| 3 | 输出变量行是 5 列网格，图形类型才显示「显示窗口」 | 列宽 `Auto` 的显示窗口一出现，备注列被压到 80px；非图形行又留下空洞 |
| 4 | 编辑器标题行 `Foreground="#666666"`（`:272`）；校验结果在 code-behind 里用 `Color.FromRgb(0x2E,0x7D,0x32)` / `Brushes.Red`（`:181-183`） | 违反「视图零硬编码颜色」；换肤改不动 |
| 5 | 校验结果只是一个无边框 `TextBlock`，空串时整行空白 | 没有「尚未校验 / 通过 / 失败」三态视觉，错误信息不醒目 |
| 6 | 变量表空态用 code-behind 手动切 `Visibility`（`:78-81`） | 空态只是灰色小字，没有引导；新增/删除后靠手动刷新 |
| 7 | `InputList` / `OutputList` 的 `ItemsSource` 在 `Loaded` 里赋值（`:75-76`） | 视图脱离 `Loaded` 生命周期（冒烟/离屏）时列表不绑定；批量冒烟看不到变量行 |
| 8 | 卡片内边距 12、标题 13、提示 11 | 与 ImageAcquisition V2 的 14/12.5/11.5 节奏不一致 |

## v1 方向

> **脚本编辑器优先，一屏完成「声明接口变量 → 写脚本 → 校验」。只做纯 UI 重构。**

1. **两栏固定分工**：左 356px 接口变量轨（窄档 280px），右侧编辑器工作区自适应；640 档不再横向溢出。
2. **变量行两段式**：第一行「名字 + 类型 + 删除」，第二行「连线/手填」或「显示窗口 + 备注」。
   `LinkableValueEditor` 仍是唯一链接入口，`Port` / `StepData` / `PortsVersion` 原样保留。
3. **输出按类型收敛**：只有 `IsIconic` 才显示「显示窗口」；非图形输出把宽度留给备注。
   仍绑 `DisplayWindow` int（0~9），仍用原来的 10 个 `ComboBoxItem`。
4. **编辑器是主工作区**：保留 AvalonEdit（行号、`CSharpHighlighting`、`CSharpIntelliSense`、
   `ScriptEditorBehavior`、右键菜单、错误行标红）；只重做外框、标题栏与状态栏。
5. **校验三态**：`ValidationResult` 空串 → 「尚未校验」；`✔` → 成功；`✘` → 错误。
   颜色由 XAML 触发器从公共令牌取，不再在 code-behind 写死。
6. **空态齐备**：输入/输出各给虚线空态；`InputVars.Count == 0` / `OutputVars.Count == 0` 由绑定驱动。
7. **全令牌化**：颜色映射 `Dialog*` / `Plugin*` 既有键；视图 XAML 零十六进制。
8. **不造死按钮**：新增/删除/校验都接现有 Click 处理器；不新增加载骨架、缩放、打开文件夹、系统导航。

## 设计要点（完整清单）

1. 左轨标题「接口变量」+ 两个计数胶囊（输入 N / 输出 N），计数绑定集合 `Count`。
2. 输入卡片：标题 + 「新增」；帮助文字说明「每项生成一个动态输入端口」；行内第二行是连线/手填。
3. 输出卡片：标题 + 「新增」；帮助文字说明「供下游连线 / 连全局变量即改全局」；图形输出才显示「显示窗口」。
4. `Context 速查` 卡片：等宽字体、10.5px、行高 1.65；`Context.*` 主色、`Fail` 用成功色（与原视图语义一致）。
5. 编辑器标题栏：代码图标 + 「C# 脚本」+ 副标题「顶层语句 · 可用 class / 方法 / LINQ / using」+ 右侧「校验脚本」主按钮。
6. 编辑器：浅色纸面、行号槽、13px 等宽；语法配色沿用 AvalonEdit 现有浅色规则（蓝关键字 / 绿注释 / 红字符串 / 紫变量）。
7. 校验状态栏：状态点 + 消息（左）+ 「AvalonEdit · C#」（右）；成功绿底、失败红底、未校验中性。
8. 空变量态：每张卡片一个虚线框，说明「点右上新增」；计数胶囊显示 0。
9. 窄档 640：左轨 280px、编辑器副标题与状态栏右侧标签隐藏；变量行仍保持「名字 + 类型 + 删除」一行。
10. 图标按钮 30×30（设计稿 28×28，WPF 统一 30×30）+ `Padding="0"`；焦点 `DialogFocusVisual`；ToolTip `DialogToolTipStyle`。
11. 数字（计数、窗口号、耗时）使用 tabular-nums；WPF 用 `Typography.NumeralAlignment="Tabular"`。
12. 宿主底栏（状态 / 耗时 / 执行 / 取消 / 确认）属于 `PluginConfigShellView`，设计稿只示意，插件视图不改。

## 契约保留（v1 一个都不能改）

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 宿主靠 `viewObj.DataContext as IPluginConfigView` 取 `OnConfirm`；独立 VM 会让确认不保存 |
| C2 | 输入变量行的 `LinkableValueEditor`：`Port`（`InputPortConverter` 多绑定：`Name` + 插件 + `PortsVersion`）、`StepData` | 端口持久化、类型转换、变量链接、解除链接、手填都在控件里 |
| C3 | `avalon:TextEditor` + `ShowLineNumbers` + `CSharpHighlighting` + `CSharpIntelliSense` + `ScriptEditorBehavior` + 右键菜单 + 错误行标红 | 编辑器不是普通 TextBox；换掉会丢语法高亮、补全、查找替换、注释等全部编辑能力 |
| C4 | `ScriptText` / `InputVars` / `OutputVars`（`[StepConfig]`） | 三者随 `.vms` 持久化；`ScriptText` 由 `TextChanged` 回写；集合变更触发动态端口重建 |
| C5 | `ScriptVarTypes`（`StepConfigOption.ForEnum`）与 `ValidationResult` | 类型下拉显示中文、值仍是成员名；校验结果仍是字符串，不新增状态枚举 |
| C6 | `AddInput_Click` / `AddOutput_Click` / `DelInput_Click` / `DelOutput_Click` / `Validate_Click` | 现有功能入口；只改外观，不换事件语义 |
| C7 | 无 HALCON 控件（本插件 `h:` 控件数为 0） | 不存在 `ImageReadOnly` / `ImageEdit` 的折叠显隐问题；不引入画布 |

## 令牌映射

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--cs-surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--cs-surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--cs-surface-3` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--cs-border` | `#E2E8F0` | `DialogBorderBrush` |
| `--cs-border-soft` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--cs-border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` |
| `--cs-text` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--cs-text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--cs-text-3` | `#94A3B8` | `DialogTextMutedBrush` |
| `--cs-text-4` | `#64748B` | `ScadaSecondaryTextBrush`（近似） |
| `--cs-primary` | `#0EA5E9` | `DialogAccentBrush` |
| `--cs-primary-strong` | `#0284C7` | `DialogAccentDarkBrush` |
| `--cs-primary-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--cs-primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--cs-primary-light` | `#E0F2FE` | `DialogAccentLightBrush` |
| `--cs-primary-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--cs-success` | `#22C55E` | `DialogSuccessBrush` |
| `--cs-success-text` | `#15803D` | `DialogSuccessTextBrush` |
| `--cs-success-soft` | `#F0FDF4` | `DialogSuccessSubtleBrush` |
| `--cs-success-border` | `#BBF7D0` | `DialogSuccessBorderBrush` |
| `--cs-danger` | `#EF4444` | `DialogDangerBrush` |
| `--cs-danger-text` | `#B91C1C` | `DialogDangerTextBrush` |
| `--cs-danger-soft` | `#FEF2F2` | `DialogDangerSubtleBrush` |
| `--cs-danger-border` | `#FECACA` | `DialogDangerBorderBrush` |
| `--cs-warning` | `#F59E0B` | `DialogWarningBrush` |
| `--cs-warning-text` | `#B45309` | `DialogWarningTextBrush` |
| `--cs-warning-soft` | `#FFF4E5` | `DialogWarningSubtleBrush` |
| `--cs-warning-border` | `#FDE68A` | `DialogWarningBorderBrush` |
| 卡片圆角 8 / 10 / 12 | — | `PluginCardCornerRadius`（8）/ `CornerRadius` 字面量（仓内暂无圆角令牌字典） |
| 间距 4/6/8/12/14 | — | `Margin` / `Padding` 字面量（同上） |
| 字体 | Segoe UI / Microsoft YaHei UI | 同族系统字体；等宽 `PluginMonoFontFamily` / Consolas |

**缺失令牌：无。** 本插件只用既有 `Dialog*` / `Plugin*` 键；不新增颜色语义，不改共享字典。
代码块与编辑器槽复用 `DialogRowAlternateBrush` / `DialogBorderLightBrush`，不另造「代码块」色。

## 复刻约束（WPF 侧）

1. **保留视图构造与生命周期**：`GetConfigView` 里 `Initialize(stepData)` + `new CSharpScriptView { DataContext = this }` 原样保留；视图构造签名不变。
2. **`LinkableValueEditor` 只包裹不改内部**：`Label=""` 外置标签；`Port` / `StepData` 原绑定；`InputPortConverter` 不动。
3. **AvalonEdit 不动**：保留 `x:Name="Editor"`、`ShowLineNumbers="True"`、`TextChanged="Editor_TextChanged"`、`Loaded` 里的高亮/补全/编辑行为/右键菜单初始化。
4. **变量表绑定移到 XAML**：`ItemsSource="{Binding InputVars}"` / `{Binding OutputVars}`；空态用 `Count == 0` 触发器；code-behind 只保留 IntelliSense 刷新。
5. **校验命令适配器**：为批量冒烟提供视图级 `ICommand`（`ValidateCommand`），执行与「校验脚本」按钮同一 `RunValidation()`；不新增业务逻辑。
6. **状态颜色令牌化**：移除 code-behind 的 `Foreground` 赋值；三态由 `ValidationResult` + 转换器 + `DataTrigger` 驱动。
7. **不修改共享件**：`LinkableValueEditor`、`PluginConfigStyles.xaml`、`Colors.xaml`、`PluginConfigColors.xaml`、`PluginConfigShellView.xaml`、`UIThemeSmokeTest` 均不改。
8. **不新增功能**：无加载骨架、无编辑器缩放/字体设置、无打开/保存脚本、无格式化、无变量重命名联动、无试运行按钮。

## 复刻实现（第 ③ 步，已完成）

| 设计项 | WPF 实现 |
| --- | --- |
| 两栏布局 | `Grid` 两列：左 `1*`（280~380）、右 `1.7*`（MinWidth 280）；左轨 `ScrollViewer`，640 档不再横向溢出 |
| 接口变量计数 | 两个胶囊 `Border` + `TextBlock` 绑 `InputVars.Count` / `OutputVars.Count` |
| 输入变量行 | `ItemsControl ItemsSource="{Binding InputVars}"`；行内 `TextBox(Name)` + `ComboBox(ScriptVarTypes)` + 删除按钮 + `LinkableValueEditor`（`Port` 多绑定 / `StepData`） |
| 输出变量行 | `ItemsControl ItemsSource="{Binding OutputVars}"`；行内 `TextBox(Name)` + `ComboBox(ScriptVarTypes)` + 删除按钮 + `ComboBox(DisplayWindow, 0~9)` + `TextBox(Remark)` |
| 图形输出收敛 | `IsIconic == true` 才显示「显示窗口」块；非图形输出该列 `Auto` 宽度归零，备注占满 |
| 空态 | `InputVars.Count == 0` / `OutputVars.Count == 0` 触发器显示虚线 `Rectangle` + 图标 + 引导文字；不新增按钮 |
| Context 速查 | 卡片内 `TextBlock` + `Run`，等宽字体 `PluginMonoFontFamily`，`Context.*` 主色、`Fail` 成功色 |
| 编辑器 | `avalon:TextEditor x:Name="Editor"`，`ShowLineNumbers`、`TextChanged`、`FontFamily=Consolas`、`FontSize=14` 原样保留 |
| 编辑器初始化 | `DataContextChanged` 提前灌入 `ScriptText`（离屏渲染/冒烟不触发 `Loaded` 也能看到正文）；`Loaded` 再挂高亮/补全/编辑行为/右键菜单 |
| 校验按钮 | `ValidateCommand`（视图级 `DelegateCommand`）→ 同一 `RunValidation()`；右键菜单仍调 `Validate_Click` |
| 校验状态栏 | `ValidationResult` + `ValidationStateConverter` / `ValidationTextConverter` + `DataTrigger`；空串显示「尚未校验」，✔/✘ 分别绿/红 |
| 工具提示 | 视图资源新增隐式 `<Style TargetType="ToolTip" BasedOn="{StaticResource DialogToolTipStyle}" />`，所有 `ToolTip` 统一走公共样式（CS1） |
| 令牌 | 视图 XAML **0 个十六进制**；颜色全部来自 `Dialog*` / `Plugin*` 公共键 |

### 复刻证据

| 项 | 结果 |
| --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.CSharpScript\Plugin.CSharpScript.csproj --no-dependencies`：**0 错误 / 0 警告**；`Modules\Plugin.CSharpScript.dll` 已刷新（144,896 字节，2026-10-20 02:10:43） |
| 批量插件视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.CSharpScript`：**11/11 PASS**；`elements=130`、`DataContext=插件`、`Button.Command=ValidateCommand`、`OnConfirm` 可执行、`XAML hex=0`、渲染 PNG 成功 |
| 变量行探针（临时离屏） | 填 2 输入 / 2 输出后：`elements=335`、`LinkableValueEditor=2`、`ports=图像,阈值`；证明空态之外的 Port 多绑定真实可用 |
| 校验回环 | 默认脚本 → `✔ 脚本校验通过（编译无误）`；改坏脚本 → `✘ 第 1 行 第 29 列: 应输入 )`，错误行在编辑器标红 |
| 证据 PNG | `evidence/after-render-v1-with-vars.png`、`evidence/after-render-v1-validation-ok.png`、`evidence/after-render-v1-validation-error.png`；批量初始态 `_batch-evidence/Plugin.CSharpScript-CSharpScriptPlugin.png` |

> **构建口径**：全量构建（带依赖）时共享 `UI` 工程被重建，输出 142 条既有 nullable 警告（`UI\Controls\Core\ElementExtention.cs` 等，非本插件代码）；插件工程 `--no-dependencies` 单独构建为 0 错误 / 0 警告。
>
> 批量冒烟的默认步骤没有变量（`InputVars` / `OutputVars` 为空），所以它报告 `LinkableValueEditor count=0`；这是冒烟夹具的默认态，不是绑定缺失。变量行由临时探针单独验证（2 个 Port，名称正确）。

### 设计稿自检（2026-10-20）

`design.html` 通过本地 HTTP 在浏览器中打开并逐项点击：

- 默认态：2 输入 / 2 输出；输入行含「名字 + 类型 + 连线/手填」，输出行含「显示窗口 + 备注」。
- 状态切换：`空变量` → 计数 0/0 + 两张虚线空态；`校验通过` → 绿色状态栏；`校验失败` → 红色状态栏 + 第 12 行行号标红。
- 宽度切换：`640` → 窗口 `width=640px`、内容列 `280px + 1fr`；`960` → 恢复 960px。
- 增行：点「新增」输入 → 计数 2→3、列表新增一行。
- 校验：点「校验脚本」→ 默认脚本显示「✔ 脚本校验通过（编译无误）」。

> 设计稿的状态按钮只是演示开关；WPF 中同样的视觉由真实 `InputVars.Count` / `ValidationResult` 驱动。

## 已知偏差（复刻时按 R130 记录）

1. **图标字体**：Web 用内联 Lucide SVG；WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`），形状不同、语义一致。
2. **编辑器实现**：Web 用 `textarea` + 高亮 `pre` 叠加模拟；WPF 用真实 AvalonEdit，语法高亮 / 补全 / 右键菜单 / 错误行标红均保留。
3. **编辑器字号**：Web 13px；WPF 沿用现有 `FontSize=14`（Consolas），避免改变既有编辑体验。
4. **编辑器配色**：Web 按 AvalonEdit 现有浅色规则示意；WPF 实际颜色来自 `CSharpHighlighting` 的硬编码 XML 颜色（`#0000FF` / `#008000` 等）。这是共享编辑器内部规则，本轮不重做，故 WPF 视图不写这些十六进制。
5. **链接入口**：Web 画了「已链接」胶囊 + 链接/解除按钮；WPF 沿用 `LinkableValueEditor` 自带的 🔗/✕ 图标，不另造胶囊。信息量一致，位置不同。
6. **窄档隐藏**：Web 在 640 宽隐藏编辑器副标题与状态栏右侧标签；WPF 没有媒体查询，副标题用 `TextTrimming` 收尾，状态栏标签保留（或由列宽自然压缩）。这是 Web 稿的增强，不影响功能。
7. **默认态数据**：Web 默认态用 2 输入 / 2 输出示例展示行结构；实际首次打开时 `InputVars` / `OutputVars` 为空，对应「空变量」态。
8. **空态按钮**：Web 空态只给文案与「新增」入口（卡片右上）；WPF 不额外造空态按钮，避免与右上「新增」重复。
9. **宿主底栏**：Web 稿示意了 `PluginConfigShellView` 的状态 / 耗时 / 执行 / 取消 / 确认；插件视图不实现这些按钮。
10. **圆角与阴影**：Web 卡片 10/12px、窗口 16px；WPF 卡片用 `PluginCardCornerRadius`（8）或 10px 字面量，窗口阴影由宿主提供。
11. **图标按钮尺寸**：Web 行内图标按钮 28×28；WPF 统一 30×30 且 `Padding="0"`（R68），避免 Fluent 隐式内边距切字形。
12. **状态切换**：Web 顶部的「默认 / 空变量 / 校验通过 / 校验失败」是设计稿的状态演示开关；WPF 没有这个开关，状态由真实 `ValidationResult` 与变量集合驱动。
13. **编辑器正文初始化**：WPF 在 `DataContextChanged` 提前灌入 `ScriptText`（因为批量冒烟/离屏渲染不触发 `Loaded`）；真机 `Loaded` 仍负责高亮、补全、编辑行为与右键菜单，两者不冲突。

## 未纳入本版（记录，不改）

- **加载骨架 / 编译中状态**：`ValidateScript()` 是同步调用，需要新增 `IsValidating` 或异步校验契约（D1）。
- **编辑器字体 / 缩放设置**：AvalonEdit 支持缩放，但插件没有公开设置项（D2）。
- **打开 / 保存脚本文件**：需要文件对话框与额外持久化路径（D3）。
- **代码格式化**：需要引入 Roslyn Formatter 或第三方格式化器（D4）。
- **变量重命名联动脚本正文**：需要跨变量表与编辑器文本的引用分析（D5）。
- **试运行按钮**：由宿主外壳统一提供（D6）。
- **HALCON 预览 / ROI**：本插件没有 HALCON 控件，不适用。

## 变更规则

设计基线确认后只读。需要调整设计时升版本（v2），并同步更新已复刻的 `CSharpScriptView.xaml`、`.xaml.cs` 与本映射表。
