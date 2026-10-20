# 变量赋值 · 配置窗口 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 设计日期 | 2026-10-20 |
| 确认日期 | 待用户确认 |
| 确认人 | 待用户确认 |
| 覆盖范围 | `Plugins\Plugin.Utility\VariableAssignmentView.xaml` + `.xaml.cs`（插件内容区）；宿主外壳 `PluginConfigShellView.xaml` 只作为 960×680 参考，不改 |
| 宿主尺寸 | 默认 960×680；最小 640×560；插件内容区约 920×520 / 600×400 |
| 设计文件 | `design.html`（v1，自包含，可点击，内联 SVG） |
| 复刻状态 | 已复刻（2026-10-20）；初始态证据 `evidence/after-render-v1-initial.png`（批量冒烟同名产物在 `_batch-evidence/`）；设计仍待用户确认 |

## 定位

按 `docs/UI设计基线/插件自定义视图Web化改造SOP.md` 的 §5.2：本文件是**Web 设计基线**，Web 只作为设计媒介，不进入构建、不部署。确认后按本基线复刻 WPF 视图；验收以「同一屏、同一状态」的截图对照为准。

## 现状审查（改动理由）

| # | 现状 | 影响 |
| --- | --- | --- |
| 1 | 单张白卡 + `StackPanel`，三行表单（作用域 / 变量名 / 值） | 信息层级平，作用域与目标变量没有「当前写到哪里」的整体感 |
| 2 | 作用域是裸 `ComboBox`（两个 `ComboBoxItem`） | 两个选项的语义差异（本轮有效 / 跨流程存盘）看不出来 |
| 3 | 变量名是裸可编辑 `ComboBox`，提示是一行灰字 | 候选与「写哪个变量、什么类型、找不到会怎样」混在一起，重要提示容易被忽略 |
| 4 | 视图内联 `#FFFFFF / #E5E5E5 / #303133 / #909399 / #606266` | 违反「视图 XAML hex=0」；换肤/统一调整改不动 |
| 5 | 固定 72px 标签列 + 左侧堆叠 | 宽窗口下右侧大片空白，窄窗口下帮助文字换行拥挤 |
| 6 | 「自动创建」是一个孤立 `CheckBox` | 与作用域的关系（仅运行时有效）没有解释 |

## v1 方向

> **一屏完成一次赋值配置**：左侧配置轨负责「写到哪里、写什么名字、写什么值」，右侧目标卡与规则卡负责「现在指向谁、两种作用域差在哪」。只做纯 UI 重构。

1. **作用域卡片化**：两张 56px 卡片（运行时变量 / 全局变量），图标 + 名称 + 一句用途；选中态 = 主色描边 + 主色浅底 + 右侧对勾。
2. **变量名行保持可编辑候选**：仍是可编辑 `ComboBox`；候选来自 `NameCandidates`，选择落成常量，不建立连线。
3. **提示升级为信息条**：`NameHint` 原文进入浅主色信息条，紧贴变量名行；不加第二套状态枚举。
4. **值行只包裹**：`LinkableValueEditor` 保持原样（`Port` / `StepData`），常量与上游链接仍由它完成。
5. **自动创建卡**：勾选框 + 一句「仅运行时变量生效」的说明，仍绑 `CreateIfNotExists.Value`。
6. **右侧目标卡**：显示当前变量名、作用域胶囊、当前作用域候选数；让「写到哪里」始终可见。
7. **右侧规则卡**：运行时 / 全局两种语义 + 三条现场最容易踩的提示；全部是静态说明，不新增功能。
8. **全令牌化**：颜色映射 `Colors.xaml` / `PluginConfigColors.xaml`；视图 XAML 内 0 个十六进制颜色。

## 设计要点（完整清单）

1. 页面节奏：标签 12px / 控件 36px / 帮助 11.5px / 字段间距 14px / 卡片内边距 16px。
2. 作用域卡片：未选中白底 + 浅描边；悬停加深描边；选中主色描边 + `DialogAccentSoftBrush` 浅底 + 右侧 16px 对勾。
3. 变量名输入：36px 高、8px 圆角；右侧 32px 箭头按钮展开候选；候选行 32px，显示「名字 + 类型」。
4. 提示条：浅主色底 + 主色浅描边 + 信息图标；文案完全由 `NameHint` 提供，不在视图里拼业务文案。
5. 值行：输入框 + 36×36 链接按钮；链接后由 `LinkableValueEditor` 自己切换为「链接地址 + 解除」形态（共享控件行为，不在视图里另造）。
6. 自动创建：16px 勾选框 + 12px 文案；下方 11.5px 说明「作用域=全局变量时此选项不生效」。
7. 右栏目标卡：作用域胶囊（运行时=中性灰 / 全局=主色浅底）、20px 变量名、候选数、 一条「名字不是值」的提醒。
8. 右栏规则卡：两行图标 + 标题 + 说明；分隔线后三条提示。
9. 窄档 640：左右栏收窄为 `minmax(0,1fr) 220px`，不堆叠；帮助文字自然换行；左栏可纵向滚动。
10. 五态：hover / pressed / focus / disabled 在本地样式中定义；焦点环走 `DialogFocusVisual`；工具提示走 `DialogToolTipStyle`。
11. 图标按钮 30×30 / 36×36 均 `Padding="0"`（R68），避免 Fluent 隐式 `Padding=11,5,11,6` 切掉字形。
12. 数字等宽：候选数、耗时使用 tabular-nums（WPF 用 `Typography.NumeralAlignment="Tabular"`）。

## 状态清单（design.html 可点击）

| 状态 | 作用域 | 变量名 | 提示语义 |
| --- | --- | --- | --- |
| 默认 | 运行时 | 空 | 还没填名字，名字通常来自上游「变量定义」 |
| 运行时已选 | 运行时 | `OKCount` | 写入运行时变量（Int32），本轮累计良品数 |
| 全局已选 | 全局 | `OKCount` | 写入全局变量（Int32），值按类型解析 |
| 跨作用域 | 运行时 | `LineName` | 同名变量在全局，提示切换作用域 |
| 找不到 | 全局 | `OKCountX` | 变量管理里没有，运行期明确报错，不静默新建 |
| 旧连线 | 运行时 | `OKCount` | 该行原由上游提供，编辑后改为手填 |

> 这 6 个状态都来自插件现有的 `NameHint` 分支，不是设计稿自造的业务状态。

## 契约保留（一个都不能改）

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 宿主靠 `viewObj.DataContext as IPluginConfigView` 取 `OnConfirm`；换独立 VM = 确认不保存 |
| C2 | `Scope.Value`（string，Runtime/Global） | 作用域白名单校验、候选过滤、`NameHint` 都依赖它；不能换成枚举 |
| C3 | `VariableName.Value`（string）+ `NameCandidates` + `NameHint` | 变量名是「名字」不是「值」；候选由宿主快照过滤；提示由插件生成 |
| C4 | `Value`（`InputPort<object>`）+ `LinkableValueEditor` 的 `Port` / `StepData` | 常量、变量链接、解除链接、类型转换都在共享控件内部 |
| C5 | `CreateIfNotExists.Value`（bool） | 运行时变量不存在时是否自动创建；全局作用域不使用 |
| C6 | `NotifyNameEditedByUser()` 的真实输入事件 | 只在键盘/粘贴/剪切/拖拽/选择时上报；程序灌值不触发，避免误解除旧连线 |

## 令牌映射

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--va-surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--va-surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--va-surface-3` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--va-border` | `#E2E8F0` | `DialogBorderBrush` |
| `--va-border-soft` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--va-border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` |
| `--va-text` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--va-text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--va-text-3` | `#94A3B8` | `DialogTextMutedBrush` |
| `--va-text-4` | `#64748B` | `ScadaSecondaryTextBrush`（`#66708A`，近似） |
| `--va-primary` | `#0EA5E9` | `DialogAccentBrush` |
| `--va-primary-strong` | `#0284C7` | `DialogAccentDarkBrush` |
| `--va-primary-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--va-primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--va-primary-light` | `#E0F2FE` | `DialogAccentLightBrush` |
| `--va-primary-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--va-success` | `#22C55E` | `DialogSuccessBrush` |
| `--va-success-text` | `#15803D` | `DialogSuccessTextBrush` |
| `--va-success-soft` | `#F0FDF4` | `DialogSuccessSubtleBrush` |
| `--va-success-border` | `#BBF7D0` | `DialogSuccessBorderBrush` |
| `--va-danger` | `#EF4444` | `DialogDangerBrush` |
| `--va-danger-text` | `#B91C1C` | `DialogDangerTextBrush` |
| `--va-danger-soft` | `#FEF2F2` | `DialogDangerSubtleBrush` |
| `--va-danger-border` | `#FECACA` | `DialogDangerBorderBrush` |
| `--va-warning` | `#F59E0B` | `DialogWarningBrush` |
| `--va-warning-text` | `#B45309` | `DialogWarningTextBrush` |
| `--va-warning-soft` | `#FFF4E5` | `DialogWarningSubtleBrush` |
| `--va-warning-border` | `#FDE68A` | `DialogWarningBorderBrush` |
| 卡片圆角 | — | `PluginCardCornerRadius`（=8，通用卡片）；作用域卡片 / 提示条 / 自动创建卡为 10（2026-10-20 遗留项 VA2） |
| 图标字体 | — | `DialogIconFont`（FA6 Solid） |
| 焦点环 | — | `DialogFocusVisual` |
| 工具提示 | — | `DialogToolTipStyle` |
| 输入框 36px | — | `DialogInput`（TextBox）；`LinkableValueEditor` 内部固定 36px |
| 间距 / 字号 / 圆角 6/10/12 | — | WPF 字面量（仓内暂无间距令牌字典） |

**缺失令牌**：无颜色令牌缺口。`DialogBorderStrongBrush`、`DialogSuccessBorderBrush`、`DialogDangerTextBrush`、`DialogDangerBorderBrush`、`DialogWarningBorderBrush` 已由 ImageAcquisition V2 追加到 `Colors.xaml`，本视图直接复用。

## 复刻实现映射（第 ③ 步，已完成 2026-10-20）

| 设计项 | WPF 实现 |
| --- | --- |
| 作用域卡片 | 2 个 `RadioButton` + `ScopeCardStyle`；`IsChecked` 经 `ScopeEqualsConverter` 写回 `Scope.Value`（string） |
| 变量名可编辑候选 | `ComboBox x:Name="NameBox"`：`IsEditable=True`、`IsTextSearchEnabled=False`、`ItemsSource=NameCandidates`、`Text=VariableName.Value`；候选行经 `NameCandidateDisplayConverter` 显示「名字 + 声明类型」（同源 `NameCandidateItems`），选择落下的仍是名字常量 |
| 提示条 | `Border` + `TextBlock Text="{Binding NameHint}"`；不新增状态枚举 |
| 值行 | `cv:LinkableValueEditor Label="" Port="{Binding Value}" StepData="{Binding StepData}"` |
| 自动创建 | `CheckBox IsChecked="{Binding CreateIfNotExists.Value, Mode=TwoWay}"` + 说明文字 |
| 右栏目标卡 | `VariableName.Value` + `Scope.Value`（`ScopeLabelConverter`）+ `NameCandidates.Count` |
| 右栏规则卡 | 静态文本 + 图标；不绑定业务数据 |
| 窄档 | `Grid` 两列 `2*` / `1*`，`MinWidth=320` / `220`；左栏 `ScrollViewer` |

## 复刻证据

| 项 | 结果 |
| --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.Utility\Plugin.Util.csproj`：**0 错误**；全量 143 / 最终增量 20 个警告全部来自既有文件与依赖，本次改动的 `.xaml` / `.xaml.cs` 无新增警告 |
| `Modules` 产物 | `Modules\Plugin.Util.dll` 已刷新（2026-10-20 01:40:50，252,416 字节） |
| 视图静态 | 视图 XAML 十六进制颜色 **0 处**；`NameBox` / `Scope.Value` / `NameCandidates` / `NameHint` / `CreateIfNotExists.Value` / `cv:LinkableValueEditor` 均在位 |
| 通用冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.Util`：**11 PASS / 0 FAIL**（2026-10-20 遗留项修复后复跑；早期记录的 8 PASS / 2 FAIL 是当时冒烟脚本的路径/计数假设，现已不复现） |
| 常规 gate | `FlowCanvasChecks` 构建连续两次在 `Plugin.DlClassify.csproj(63,5)` 的 `xcopy OnnxRuntime → VisionMaster\bin` 报 `MSB3073`（退出码 4，环境占用，与本插件无关）；按约定未杀进程，gate 待宿主关闭后由主会话重跑 |
| 初始态渲染 | `evidence/after-render-v1-initial.png`（同时由批量冒烟输出到 `_batch-evidence/Plugin.Util-VariableAssignmentPlugin.png`，920×520，149 个元素） |

**早期冒烟两条限制（已不复现）**：

1. `XAML hex=0`：早期 `PluginViewSmoke` 把程序集名当项目目录名（`Plugins\Plugin.Util`），现按前缀解析到 `Plugins\Plugin.Utility`；本次复跑 `xaml=1 hex=0`。
2. `Button.Command 绑定`：本视图自身没有命令按钮（`LinkableValueEditor` 内部按钮不计入），冒烟按 `ownButtons.Count == 0` 放行；本次复跑 PASS。

> 2026-10-20 遗留项修复后的实际输出：11 条 PASS / 0 条 FAIL（见 `docs/code-changes/2026-10-20-VariableAssignment-Web化.md` §9）。

## 复刻约束（已写入 XAML/code-behind 的设计纪律）

1. 根 `UserControl`、`MinWidth="560"`、`MinHeight="320"`；不写死 900×500。
2. `DataContext = 插件实例`；构造签名、`Initialize(stepData)` 时序不变。
3. `LinkableValueEditor` 只设 `Label=""`，`Port` / `StepData` / `BrowseCommand`（本插件无 Browse）原样。
4. 不引入独立 VM；只新增 `ScopeEqualsConverter`、`ScopeLabelConverter`、`NameDisplayConverter` 三个 UI 转换器。
5. 不修改共享文件：`Colors.xaml`、`PluginConfigColors.xaml`、`LinkableValueEditor`、`PluginConfigShellView.xaml`、其它插件。
6. 视图 XAML 内 0 个十六进制颜色；静态断言由 `--plugin-views Plugin.Util` 的 `XAML hex=0` 检查。
7. 图标按钮：30×30 / 36×36 + `Padding="0"`（R68）；焦点 `DialogFocusVisual`；ToolTip `DialogToolTipStyle`。
8. 不造死按钮：本视图没有新增命令按钮；作用域卡片是 `RadioButton`，值行的链接/解除由共享控件提供。

## 已知偏差（复刻时按 R130 记录）

1. **图标字体**：Web 用内联 SVG（Lucide 风格），WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`）；形状不同，语义一致。
2. **变量名下拉**：Web 稿是自绘候选面板；WPF 沿用可编辑 `ComboBox` 的 Fluent 下拉，候选行显示「名字 + 声明类型」（`NameCandidateItems`），箭头形状与行高略有差异。
3. **值链接形态**：Web 稿用浅蓝 chip 模拟；WPF 由 `LinkableValueEditor` 内部渲染链接地址与解除按钮，外观以共享控件为准。
4. **输入框 placeholder**：Web 稿的「输入或从右侧候选选择」「输入常量…」是 CSS placeholder；WPF 的 `ComboBox` / `LinkableValueEditor` 不提供 watermark，改用下方 `NameHint` 信息条与帮助文字表达，不改共享控件。
5. **右栏候选数**：Web 稿按预设写死；WPF 绑定 `NameCandidates.Count`，无候选时显示 0。
6. **字体度量**：同字号下 Segoe UI / 微软雅黑与 Web 字宽、行高略有差异，不按 px 硬套。
7. **滚动条**：WPF 左栏滚动条约占 17px，窄档可用宽度比 Web 少约 17px。
8. **阴影/动效**：Web 窗口多层 CSS 阴影；WPF 用宿主现有 `DropShadowEffect`；动效统一 150~200ms。
9. **宿主外壳**：Web 稿包含标题栏与底部按钮条，仅用于展示上下文；插件视图只负责中间内容区，外壳不改。
10. **640×560 最小档**：宿主已统一 MinHeight=560；本稿 640 宽档按两列收窄展示，真机需复核左栏滚动与右栏文字换行。

## 未纳入本版（记录，不改）

- **变量浏览器 / 打开变量管理**：需要新的宿主导航契约。
- **值类型校验器 / 转换预览**：配置态没有目标变量类型元数据可绑定；写入侧仍在运行期按声明类型解析。
- **加载骨架 / 运行日志 / 试运行可视化**：需要新增 VM 状态或宿主契约。
- **深色主题**：本稿只覆盖浅色设计系统。
- **批量赋值 / 多目标**：属于功能扩展，不在本次范围。

## 变更规则

设计基线确认后只读。需要调整设计时升版本（v1 → v2），并同步更新已复刻的 `VariableAssignmentView.xaml` 与本映射表。

## 2026-10-20 遗留项修复（VA1 / VA2 / VA3 / VA4）

- **VA1**：`NameBox.Background` 从 `DialogInputBackgroundBrush` 改为 `DialogSurfaceBrush`，与卡片底色区分，层次更清楚。
- **VA2**：作用域卡片（`ScopeCardStyle` 模板 Border）、提示条（`HintBorderStyle`）、自动创建卡三处圆角 8 → 10；图标 chip 的 8 保持不变。
- **VA3**：新增只读 `NameCandidateItems`（`Name` / `TypeName` / `Note`，与 `NameCandidates` 同源同序），候选行显示「名字 + 声明类型」；`NameCandidates`（字符串集合）仍是 `ItemsSource`/Text 绑定的唯一来源，选择落成名字常量，不改业务契约。若后续要改成对象集合，需同步 `FlowCanvasChecks\VariableAssignmentCheck.cs` 的候选断言。
- **VA4**：`ScopeLabelConverter` 对非空且非 Runtime/Global 的值返回「未知作用域（值）」；插件 `CurrentScope` 不再把未知值静默回退 Runtime（返回空串 → 无候选，`NameHint` 明确写「作用域取值无法识别」，运行期既有白名单校验照常报错）。
- **验证**：`dotnet build Plugins\Plugin.Utility\Plugin.Util.csproj -v m` → 0 错误（20 个既有警告，与改动前基线一致）；`UIThemeSmokeTest --plugin-views Plugin.Util` → 11 条 PASS / 0 条 FAIL。
