# 图像脚本配置窗口 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 日期 | 2026-10-20 |
| 确认日期 / 人 | 待用户确认 |
| 状态 | 设计基线已产出；同批完成 WPF 复刻（待用户真机验收） |
| 覆盖范围 | `Plugins\Plugin.ImageScript\ImageScriptView.xaml`（插件内容区）+ 宿主 `PluginConfigShellView.xaml` 的 960×680 外壳 |
| 窗口尺寸 | 960×680 默认；640×560 最小档（宿主 2026-10-20 统一 MinHeight=560） |
| 设计基线文件 | `design.html`（自包含、内联 SVG、可点击切换默认 / 空态 / 校验失败 / 长列表） |
| 参考实现 | `docs/UI设计基线/image-acquisition/`（V2 视觉语言与 CSS/布局模板） |

## 定位

按 `AGENTS.md` §30.1 的四步流程，本文件是**第 ① 步（Web 设计基线）**的产物。
Web 只是设计媒介，不进入构建、不部署。确认后按本基线做第 ③ 步（WPF 复刻），
第 ④ 步以「同一屏、同一状态」的截图对照验收。

本插件是**脚本编辑器型**配置视图：左侧过程管理与变量声明，右侧 AvalonEdit 脚本工作区；
没有 HALCON 画布、没有采集模式、没有相机 / 网络推送。

## 现状审查（改动理由）

| # | 现状（改造前） | 影响 |
| --- | --- | --- |
| 1 | 左列固定 `Width="380"`，右侧编辑器吃剩余宽度 | 窄窗口下编辑器被压到 200px 级；宽窗口下左栏也不随窗口伸缩 |
| 2 | 三张 `Border` 卡片 + 12px 内边距，与 V2 的 14px 节奏不一致 | 视觉上与已改造的 ImageAcquisition 不是一套 |
| 3 | 过程下拉 `Height="26"`、变量行高 24~30 | 控件偏小，点击目标不足，与 V2 的 36px 控件不一致 |
| 4 | 输入变量单行 `110/92/*`，输出变量单行 `100/88/84/*` | 380px 下变量名稍长就截断；连线编辑区被压得很窄 |
| 5 | 编辑器标题 `Foreground="#666666"`、校验结果用代码里的 `Brushes.Red` / `FromRgb(0x2E,0x7D,0x32)` | 视图内硬编码颜色，违反 R117；换肤改不动 |
| 6 | 校验结果只有一行文字，无「未校验 / 通过 / 失败」的视觉分层 | 用户不知道当前脚本是否校验过 |
| 7 | 空态（无过程、无变量）只有两行灰色提示 | 新用户不知道下一步该点「新建过程」还是「导入 .hdev」 |
| 8 | 编辑器没有独立的工作区外框，和左栏卡片混在一起 | 脚本编辑是主任务，但视觉权重不足 |

## v1 方向

> 过程管理在左、脚本编辑在右；变量表卡片化；校验状态常驻；**只做纯 UI 重构，不改功能入口与绑定**。

1. **两栏固定分工**：左 356px 配置轨（过程 + 输入变量 + 输出变量），右侧脚本工作区自适应。
2. **过程管理卡片化**：编辑目标 / 运行过程两个下拉 + 六个动作按钮（新建 / 删除 / 导入 / 导出 / 同步 / 恢复默认）。
3. **变量行卡片化**：每个变量一张小卡：变量名 + 类型下拉 + 连线/固定值；输出行多一个「显示窗口」下拉。
4. **编辑器成为主工作区**：保留 AvalonEdit 全部能力（行号、语法高亮、智能提示、右键菜单、错误行标红），只换外框、标题与工具条。
5. **校验状态常驻**：底部状态条显示「尚未校验 / ✔ 通过 / ✘ 失败」，失败时错误行在编辑器内标红。
6. **全令牌化**：颜色映射 `Colors.xaml` 的 `Dialog*` 公共令牌；视图 XAML 零十六进制。
7. **五态与可达性**：hover / pressed / focus / disabled 全量；图标按钮 30×30 且 `Padding="0"`（R68），焦点环走 `DialogFocusVisual`。
8. **边界态**：空态（无过程 / 无变量）、校验失败（错误行 + 红字）、长变量表（左栏滚动）在设计稿内可切换。

## 耦合矩阵（SOP §5.1）

| 区域/元素 | 当前绑定/契约 | 功能依赖 | 可重构级别 |
| --- | --- | --- | --- |
| 根布局 | `UserControl` + `DataContext = 插件实例` | 宿主 `PluginConfigShell` 取 `IPluginConfigView` | 自由改布局；`DataContext` 不能换 |
| 过程管理 | `EditProcCombo` / `RunProcCombo` + `SelectionChanged` | 编辑目标写 `_editTarget.Body`；运行目标写 `SelectedProcedure` | 只改外观；下拉对象/事件保留 |
| 过程动作 | 6 个 `Click`：新建 / 删除 / 导入 / 导出 / 同步 / 恢复默认 | 过程集合、`.hdev` 导入导出、变量表同步、默认脚本替换 | 只改外观；处理器保留 |
| 输入变量表 | `ItemsControl x:Name="InputList"`，`ItemsSource` 由 code-behind `RefreshLists()` 唯一提供 | 变量行来自过程接口；`ObservableCollection` 增删触发端口重建 | 可重排为卡片；`ItemsSource` 保留在 code-behind |
| 输入类型 | `ComboBox SelectedValue="{Binding Type}"` + `ScriptVarTypes` | `ScriptVarDef.Type` setter → `RebuildDynamicInputs/Outputs` | 只改外观；`SelectedValuePath=Value` 必须保留 |
| 输入连线 | `LinkableValueEditor` + `Port` MultiBinding（Name + 插件 + `PortsVersion`） + `StepData` | 端口持久化、变量链接、解除链接、类型转换 | 只能包裹；`Port`/`StepData` 不能换 |
| 输出变量表 | `ItemsControl x:Name="OutputList"`，`ItemsSource` 由 code-behind `RefreshLists()` 唯一提供 | 输出端口快照 `StepData.OutputPortDefinitions` | 可重排为卡片；`ItemsSource` 保留在 code-behind |
| 输出显示窗口 | `ComboBox SelectedIndex="{Binding DisplayWindow}"`（0~9） | 图形类输出推送主界面视图窗口 | 只改外观；int 绑定不能换 |
| 脚本编辑器 | `avalon:TextEditor x:Name="Editor"` + `TextChanged` | 语法高亮 / 智能提示 / 行为 / 右键菜单 / 过程体回写 | 保留控件；只换外框 |
| 编辑器标题 | `x:Name="EditorTitle"`，代码写入 `GetProcedureMethod()` | 过程签名显示 | 保留 x:Name；只改样式 |
| 校验结果 | `ValidationResult` + `ValidateResultText` | `ValidateScript()` 的通过/失败文案与错误行标红 | 保留属性与 x:Name；只改配色/布局 |
| 状态 | 无 `StatusMessage` / `StatusLevel` | 本插件只有 `ValidationResult` | 不新增第四档；校验状态用三色呈现 |

**判定原则**：任何"控件是某个端口/命令/配置属性的唯一入口"的行，标为"只能包裹/改外观"，不重写。

## 设计要点（完整清单）

1. **左栏节奏**：标签 12px / 控件 36px / 帮助 11.5px；区块间距 14px；卡片内边距 14px。
2. **过程下拉**：编辑目标与运行过程分开；运行过程下拉只列 `RunProcedureNameList`（排除 `main`）。
3. **动作按钮**：2 列 × 3 行，34px 高；「恢复默认脚本」用警示文字色，仍有确认框。
4. **变量卡**：变量名（12px Semibold）+ 类型下拉（30px）+ 输出行「显示窗口」（30px，仅图形类可见）；连线/固定值区独立一行。
5. **输入变量**：`LinkableValueEditor` 占满卡片宽度；`Label=""` 外置标签由卡片头部承担。
6. **输出变量**：备注文本只读展示；「显示窗口」仅在 `IsIconic` 为真时出现（沿用原 DataTrigger）。
7. **编辑器**：52px 标题栏（图标 + 过程签名 + 副标题 + 校验按钮）；代码区行号 + 语法着色；底部 42px 校验状态条。
8. **空态**：无过程时编辑器显示「尚无脚本过程」+ 新建/导入/恢复默认的下一步说明；无变量时显示原提示文案。
9. **校验失败**：错误行整行浅红底 + 左侧红色竖线；状态条红字，保留原 `ValidationResult` 文本（含行号与中文提示）。
10. **窄档 640×560**：左栏 300px、右栏最小 260px；根 `MinWidth=576`（列最小和 300+16+260）；编辑器副标题隐藏、状态条 `MinHeight=42` 且允许换行；左栏可滚动。
11. **数字等宽**：行号、窗口编号、耗时使用 tabular-nums。
12. **不做死按钮**：所有按钮都接现有 Click 处理器或公开方法；校验按钮通过视图级 `ICommand` 包装既有 `Validate_Click`。

## 契约保留（v1 一个都不能改）

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 宿主靠 `viewObj.DataContext as IPluginConfigView` 取 `OnConfirm`；换独立 VM = 确认不保存 |
| C2 | `InputPortConverter` MultiBinding（Name + 插件 + `PortsVersion`） | 变量名 → 动态输入端口；端口重建后靠 `PortsVersion` 通知重新解析 |
| C3 | `avalon:TextEditor x:Name="Editor"` + `TextChanged` | 脚本编辑、语法高亮、智能提示、右键菜单、过程体回写的唯一入口 |
| C4 | `Procedures` / `SelectedProcedure` / `InputVars` / `OutputVars` | 四个 `[StepConfig]` 属性，随 `.vms` 持久化；不能改名/改类型 |
| C5 | `ScriptVarDef.Type` / `DisplayWindow` | 类型下拉写枚举；显示窗口是 int（0~9）；`IsIconic` 决定窗口下拉可见性 |
| C6 | 七个 Click 处理器 + `ValidationResult` | 新建 / 删除 / 导入 / 导出 / 同步 / 恢复默认 / 校验都是功能入口；状态只有校验结果，不扩展 |

## 令牌映射

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--es-surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--es-surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--es-surface-3` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--es-border` | `#E2E8F0` | `DialogBorderBrush` |
| `--es-border-soft` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--es-border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` |
| `--es-text` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--es-text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--es-text-3` | `#94A3B8` | `DialogTextMutedBrush` |
| `--es-text-4` | `#64748B` | `ScadaSecondaryTextBrush`（近似；设计稿专用） |
| `--es-primary` | `#0EA5E9` | `DialogAccentBrush` |
| `--es-primary-strong` | `#0284C7` | `DialogAccentDarkBrush` |
| `--es-primary-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--es-primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--es-primary-light` | `#E0F2FE` | `DialogAccentLightBrush` |
| `--es-primary-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--es-success` | `#22C55E` | `DialogSuccessBrush` |
| `--es-success-text` | `#15803D` | `DialogSuccessTextBrush` |
| `--es-success-soft` | `#F0FDF4` | `DialogSuccessSubtleBrush` |
| `--es-success-border` | `#BBF7D0` | `DialogSuccessBorderBrush` |
| `--es-danger` | `#EF4444` | `DialogDangerBrush` |
| `--es-danger-text` | `#B91C1C` | `DialogDangerTextBrush` |
| `--es-danger-soft` | `#FEF2F2` | `DialogDangerSubtleBrush` |
| `--es-danger-border` | `#FECACA` | `DialogDangerBorderBrush` |
| `--es-warning` | `#F59E0B` | `DialogWarningBrush` |
| `--es-warning-text` | `#B45309` | `DialogWarningTextBrush` |
| `--es-warning-soft` | `#FFF4E5` | `DialogWarningSubtleBrush` |
| `--es-warning-border` | `#FDE68A` | `DialogWarningBorderBrush` |
| 图标字体 | — | `DialogIconFont`（Font Awesome 6 Pro Solid） |
| 焦点环 | — | `DialogFocusVisual` |
| 工具提示 | — | `DialogToolTipStyle` |
| 下拉 | — | `DialogCombo`（局部覆盖高度 30/32） |
| 圆角 6/8/10/12/16 | — | WPF `CornerRadius` 字面量（仓内暂无圆角令牌字典） |
| 间距 4/8/12/14/16 | — | WPF `Margin` / `Padding` 字面量 |
| 字体 | Segoe UI / Microsoft YaHei UI | 同族系统字体；代码用 Consolas |

**令牌新增：无。** 本插件只消费既有 `Dialog*` 公共令牌；没有向 `Colors.xaml` / `PluginConfigColors.xaml` 追加任何键。
代码高亮色仍由既有 `HalconHighlighting` 提供（不在视图 XAML 中，也不属于本轮令牌范围）。

## 复刻实现（第 ③ 步）

| 设计项 | WPF 实现 |
| --- | --- |
| 两栏布局 | `Grid`：左 `1*`（Min 300 / Max 380），右 `1.7*`（Min 260）；`Margin="0"` 由宿主 `ContentPresenter` 提供 20,16 |
| 左配置轨 | `Border` + `ScrollViewer`；背景 `DialogRowAlternateBrush`、描边 `DialogBorderLightBrush`、圆角 `PluginCardCornerRadius` |
| 过程管理 | `ComboBox x:Name="EditProcCombo"` / `RunProcCombo`（`DialogCombo`，36px）；`ItemsSource` 由 code-behind `RefreshLists()` 唯一设置（XAML 不再绑定 `Procedures` / `RunProcedureNameList`），选中项也由 code-behind 负责 |
| 动作按钮 | `UniformGrid Columns="2"`，6 个 `Button` + `Click` 原处理器；`LocalButtonStyle` 34px |
| 输入变量 | `ItemsControl x:Name="InputList"`，`ItemsSource` 由 code-behind `RefreshLists()` 唯一设置；卡片内 `LinkableValueEditor` + `Port` MultiBinding + `StepData` |
| 输入类型 | `ComboBox` 绑 `DataContext.ScriptVarTypes`、`DisplayMemberPath=Display`、`SelectedValuePath=Value`、`SelectedValue={Binding Type}` |
| 输出变量 | `ItemsControl x:Name="OutputList"`，`ItemsSource` 由 code-behind `RefreshLists()` 唯一设置；类型 + 显示窗口 + 备注 |
| 显示窗口 | `ComboBox SelectedIndex="{Binding DisplayWindow}"`，0=不显示 / 1~9；`DataTrigger IsIconic=True` 才显示 |
| 编辑器 | `avalon:TextEditor x:Name="Editor"`（行号 / 语法高亮 / 智能提示 / 右键菜单原样）；外框 `Border` 圆角 8 |
| 编辑器标题 | `TextBlock x:Name="EditorTitle"`（代码写 `GetProcedureMethod()`），Consolas 12.5 Semibold |
| 校验按钮 | 视图级 `ValidateScriptCommand`（`RelayCommand`）→ 调用既有 `Validate_Click` 逻辑；仅新增 UI 包装，不改业务 |
| 校验状态条 | `TextBlock x:Name="ValidateResultText"` 绑 `ValidationResult`；空值经 `ValidationMessageConverter` 显示「尚未校验」；成功/失败前景色从公共令牌取；`Border MinHeight=42`、所在行 `Height=Auto`（支持换行） |
| 空态 | 无过程时编辑器上方显示「尚无脚本过程」覆盖层；无变量时保留原 `InputEmptyHint` / `OutputEmptyHint` 文案 |
| 令牌 | 视图 XAML 零十六进制；全部 `Dialog*` 公共令牌 |

### 复刻约束（已遵守）

1. **保留视图构造与生命周期**：`ImageScriptView()` 构造、`DataContext = plugin`（由 `GetConfigView` 设置）、`plugin.Initialize(stepData)` 均未改。
2. **`LinkableValueEditor` 只包裹不改内部**：`Port` / `StepData` 原样；`Label=""` 外置标签；`InputPortConverter` 与 `PortsVersion` 锚点保留。
3. **AvalonEdit 保留**：`TextEditor` 控件类型、`TextChanged`、`HalconHighlighting`、`ScriptIntelliSense`、`ScriptEditorBehavior`、右键菜单构建全部未动。
4. **类型不能换**：`ScriptVarType` 枚举、`DisplayWindow` int、`SelectedProcedure` string 原样。
5. **命令与处理器**：七个 `Click` 处理器全部保留；新增的 `ValidateScriptCommand` 只调用既有校验逻辑，不新增功能。
6. **共享件不动**：`LinkableValueEditor`、`AvalonEdit`、`HalconHighlighting`、`ScriptIntelliSense`、`ScriptEditorBehavior`、`PluginConfigShellView` 均未修改。
7. **不修改共享文件**：`Colors.xaml`、`PluginConfigColors.xaml`、`UIThemeSmokeTest\Program.cs` / `.csproj` / `PluginViewSmoke.cs`、`PluginConfigShellView.xaml`、其它插件均未动。

## 验证记录

| 项 | 结果 |
| --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.ImageScript\Plugin.ImageScript.csproj -v m`：**0 警告 / 0 错误**；`Modules\Plugin.ImageScript.dll` 已刷新（2026-10-20 02:08:30，447,488 字节） |
| UI 冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.ImageScript`：**11 项 PASS / 0 FAIL**（`LinkableValueEditor` count=1 port=Image、`ValidateScriptCommand`、`OnConfirm` InputValues=5、XAML hex=0） |
| 渲染证据 | `evidence/after-render-v1-default.png`（由 `docs/UI设计基线/_batch-evidence/Plugin.ImageScript-ImageScriptPlugin.png` 复制）；冒烟日志见 `evidence/smoke-plugin-views.txt` |
| 设计稿 | `design.html` 可直接浏览器打开；默认 / 空态 / 校验失败 / 长列表四态可切换；脚本块通过 `node --check` |
| 常规 gate | 未执行（按派工单只跑插件构建 + `--plugin-views`；`FlowCanvasChecks` 由主会话统一跑） |
| 真机 | 待验收（打开配置 → 改字段 → 确认 → 重开回环） |

## 已知偏差

1. **图标字体**：Web 用 Lucide 线性图标，WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`）；形状不同，语义一致。
2. **代码高亮色**：Web 设计稿使用 `#64748B / #0284C7 / #B45309` 等演示色；WPF 使用既有 `HalconHighlighting` 的颜色定义，不逐字复刻。
3. **编辑器行高**：Web 设计稿 22px/行；WPF AvalonEdit 按 12.5~14px 字号与系统字体度量排版，不按 px 硬套。
4. **变量类型下拉高度**：Web 30px；WPF `DialogCombo` 基于 38px 样式局部覆盖为 30/32px，保证卡片内节奏。
5. **滚动条**：WPF 左栏滚动条约占 17px，窄档可用宽度比 Web 少约 17px。
6. **工具提示**：Web 的 `data-toast` 只是演示；WPF 用 `DialogToolTipStyle`。
7. **页脚**：Web 设计稿展示宿主 `状态: 待执行` 与执行/取消/确认；插件视图不包含页脚，宿主外壳负责。
8. **空态覆盖层**：WPF 编辑器控件始终存在（AvalonEdit 不能按需创建，否则智能提示/行为初始化会丢），空态用纯 WPF 覆盖层；与 ImageAcquisition 的 `ContentControl` 按需创建 HALCON 控件不是同一问题。
9. **校验按钮**：原视图只有右键菜单「编译」入口；本轮新增一个工具栏按钮包装同一 `Validate_Click`，属于纯 UI 入口，不新增校验逻辑。若用户认为不需要该按钮，可删除而不影响任何契约。
10. **渲染证据范围**：AvalonEdit 是 WPF 控件，`RenderTargetBitmap` 能捕获；但不同 DPI/字体下文字换行位置可能略有差异。

## 未纳入本版（记录，不改）

- **加载骨架 / 编译中状态**：需要新增 `IsValidating` 或状态枚举；当前 `ValidationResult` 是同步结果。
- **脚本行数 / 字数统计**：需要新增只读属性。
- **编辑器字体缩放按钮**：`ScriptEditorBehavior` 已有快捷键，但没有公开按钮入口。
- **打开所在文件夹**：需要新增 `Process.Start("explorer.exe", ...)` 行为。
- **变量增删 / 重命名 UI**：变量表由过程接口同步；手工增删需要新的过程接口编辑契约。
- **系统导航 / 帮助页**：需要新的宿主导航契约。
- **640×560 的完整布局**：宿主 MinHeight 已统一 560，需真机复核左栏滚动与编辑器高度。

## 变更规则

设计基线确认后只读。需要调整设计时升版本（v2），并同步更新已复刻的 `ImageScriptView.xaml` 与本映射表。
