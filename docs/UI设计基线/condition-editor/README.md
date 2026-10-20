# 条件逻辑 / 计次循环 配置中心 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | **v2** |
| 设计日期 | 2026-10-20 |
| 确认日期 / 人 | 待用户确认 |
| 覆盖范围 | `VisionMaster\Views\DialogViews\ConditionEditorView.xaml` + `.xaml.cs` + `ViewModels\DialogViewModels\ConditionEditorViewModel .cs`（双击逻辑算子弹出的配置窗口） |
| 窗口尺寸 | 1120 × 760（默认）；960 × 760（最小档，与 `Window.MinWidth` 一致） |
| 设计文件 | `design.html`（v2，自包含、无外部依赖、可点击；四种模式 × 六种状态 × 两档宽度） |
| 证据 | `evidence/design-v2-{default,case,wait,for,invalid,case-invalid,wait-invalid,empty-expr,fallback,empty,for-linked,narrow,narrow-case}.png` |
| 复刻状态 | **已复刻（2026-10-20）**：见 `docs/code-changes/2026-10-20-条件逻辑配置中心Web化v2.md`；样式落在 `UI\Controls\Themes\Dialog\Dialog.ConditionEditor.xaml`，断言 `FlowCanvasChecks\ConditionEditorV2Checks.cs`（26 条） |
| 设计语言 | 沿用 ImageAcquisition V2 / 插件视图 Web 化：两栏 + 分区 + 30~34px 控件 / 11.5~12px 标签 / 8~12px 圆角 / 全令牌化 |

## 0. v1 → v2 变更（本轮大换血）

| # | v1 的问题 | v2 的处理 |
| --- | --- | --- |
| 1 | 等待模式「报警变量名」被画成纯输入框，丢了 `AlarmVariableCandidates` 候选下拉 | 改为 **手填 + 候选下拉**（`input` + `datalist` 视觉，WPF 用 `IsEditable=True` 的 `ComboBox`），与 R33「名字语义」口径一致 |
| 2 | 分支匹配「判据」只有纯输入框 | 同样改为 **手填 + 候选下拉**，候选来自 `Variables` 别名；判据本质是变量名 |
| 3 | 类型胶囊与别名输入框抢同一行宽度，960 档别名只剩约 50px | 变量表改 **四列**：`别名 / 类型 / 数据源 / 操作`，类型独立 74px 窄列 |
| 4 | 960 档左栏 220px 与 README「建议 240px」自相矛盾 | 960 档 **同样 240px**，只压右侧内边距与快捷插入高度；README 与设计稿单一口径 |
| 5 | 校验失败时底栏说「修正后才能保存」，但主按钮仍可点 | 文案改为「点「保存并应用」会再次提示并停在弹窗」，与 `OnSave` 的真实行为一致；**不禁用按钮**（禁用会让用户不知错在哪） |
| 6 | 960 档快捷插入 `max-height:none`，编辑器高度随内容浮动 | 快捷插入改 **固定 148px + 内部滚动**，编辑器高度恒定 |
| 7 | For 只有「未连线」一种状态 | 新增 **For 已连线** 状态：数据源显示地址、「解除」按钮出现、底栏改为「循环次数由数据源驱动」 |
| 8 | 缺「必需分支表达式为空」错误态 | 新增 **空表达式** 状态：左栏该分支序号变红、徽标变「缺少条件」、编辑器显示占位提示、卡内红条给出编译后果 |
| 9 | 缺分支匹配错误态 | 新增 **分支匹配校验失败**：判据未声明 + 匹配值重复两条同时上屏；「已用匹配值」列表把重复行标红 |
| 10 | 缺等待参数错误态 | 新增 **等待校验失败**：轮询间隔 < 10ms |
| 11 | 校验状态重复出现两次（卡内 + 底栏） | 卡内**只在有错时**出现（就近提示）；底栏常驻摘要。成功态只有底栏一条 |
| 12 | 兜底分支只给一句说明 | 补「本分支下已有 N 个步骤」「变量表与其它分支共用」「切到上一条分支继续编辑」行动指引 |
| 13 | 分支匹配卡片下方大片空白 | 补「本容器已用匹配值」列表（当前 / 已占用 / 兜底三态），把空白变成防重复的实用信息 |
| 14 | For 整页只有一张居中卡 | 改为顶部对齐的字段网格 + 「循环体 N 步 / 默认次数 / 数据源」摘要条 + 用途说明 |
| 15 | 表达式空态是纯空白 | 加占位提示「在此输入布尔表达式，例如：score > 0.85 && ok」 |
| 16 | 删除变量无提示 | 删除图标加 ToolTip「删除后该别名不再可用，引用它的表达式需同步修改」 |
| 17 | 三个颜色值无公共令牌 | 收敛为只用既有键；`--primary-strong` 仅用于 hover，WPF 由 `DialogPrimaryButton` 提供 |
| 18 | 深链参数没写进 README | 已写入 §11 |

## 1. 定位

本文件是「条件窗口 Web 化」的第 ① 步产物（R127 四步：设计基线 → 令牌映射 → 桌面端复刻 → 验收）。

Web 只作为**设计媒介**，不进构建、不部署；确认后按本基线复刻 WPF，第 ④ 步以「同一屏、同一状态」的截图对照验收。

本轮**只覆盖视觉结构**，下列一个都不改：

- `ConditionEditorViewModel` 全部属性名与命令名（`Branches` / `SelectedBranch` / `SelectedBranchNeedsExpression` / `IsForMode` / `IsWaitMode` / `IsCaseMode` / `Variables` / `LoopCountText` / `LoopCountSourceText` / `HasLoopCountLink` / `JudgeExpressionDraft` / `EditorTitle` / `NoConditionTitle` / `NoConditionHintText` / `ShowBooleanEditor` / `ShowCaseValueEditor` / `TimeoutActionOptions` / `AlarmVariableCandidates` / `WaitHintText`；命令 `AddVariableCommand` / `RemoveVariableCommand` / `BindVariableCommand` / `SaveCommand` / `CancelCommand` / `LinkLoopCountCommand` / `UnlinkLoopCountCommand`）。
- 草稿 → 校验 → 写回 → `Version++` 事务（`OnSave` / `SaveWait` / `SaveForLoop` / `ValidateDraft` / `ValidateCaseBranches` / `WriteBackVariables`）。
- AvalonEdit 的 `x:Name="txtExpression"`、`TextChanged`、`BranchChanged` 订阅、变量着色器、代码补全。
- 每个快捷插入按钮的 `Click="QuickInsert_Click"` 与 `Tag` 精确内容（Tag 就是插入片段）。
- 快捷插入 5 组与全部片段（引用变量 / 逻辑比较 7 / 数字运算 5 / 数学函数 3 / 字符串 7）。

## 2. 现状审查（v1 沿用，改动理由不变）

| # | 现状 | 影响 |
| --- | --- | --- |
| 1 | 标题栏 52px，无模式标识 | 分不清当前是 If / While / Case / Wait / For |
| 2 | 左栏 208px，条目只有「圆点 + 分支名」 | 看不出是否必需条件、当前表达式是什么 |
| 3 | 变量表 `DataGrid`，别名 150px、类型不可见 | 别名与数据源宽度接近；`DataTypeName` 完全没上屏 |
| 4 | 快捷插入 5 组挤在 `MaxHeight=200` 内滚动 | 组名与胶囊对齐不齐；引用变量组结构特殊 |
| 5 | 表达式编辑器无校验结果条 | 校验失败只弹 `EasyDialog`，编辑期间零反馈 |
| 6 | 等待参数 `MaxHeight=132` + 内部滚动 | 高 DPI 下被压成滚动条；5 个参数无单位/范围提示 |
| 7 | For 整卡居中，输入 160px | 与其它三种模式视觉语言不一致 |
| 8 | 底栏左侧空 | 校验结果只在弹窗出现一次 |

## 3. v2 结构

### 3.1 标题栏 72px

40px 主色圆角图标 + 标题（绑 `Title`）+ **模式胶囊**（图标 + `IF / WHILE` / `CASE` / `WAIT` / `FOR`）+ 副标题 + 右侧关闭。

模式胶囊由 `IsCaseMode` / `IsWaitMode` / `IsForMode` 三个既有布尔推导（视图侧转换器或三个 `DataTrigger`），**只读标识，不可点击**（模式由靶节点类型决定）。

### 3.2 左栏 240px（960 档同样 240）

每条分支：序号方块 + 分支名 + 语义徽标 + 表达式摘要。

| 状态 | 表现 |
| --- | --- |
| 必需分支 | 徽标「必需」，表达式用等宽字体显示 |
| 兜底分支 | 徽标「兜底」，表达式斜体「无需条件 · 系统视为真」 |
| 空表达式（错误） | 序号块红底、徽标「缺少条件」、摘要红字「条件表达式为空 · 编译会报错」 |
| 选中 | 白底 + 深描边 + 3px 主色左指示条 |

### 3.3 变量卡（四列）

`别名（弹性）| 类型 74px | 绑定的上游数据源（1.5fr）| 操作 62px`

- 类型列显示 `DataTypeName` 短名（`DOUBLE` / `BOOL` / `INT32`…），完整名放 ToolTip。
- 数据源为空时斜体「尚未绑定上游端口」。
- 操作列只有「重新绑定」「删除」两个图标按钮；删除按钮带 ToolTip 说明后果。
- 变量行超过约 5 行时卡内滚动（`max-height:200px`；960 档 176px），表头 `sticky`。
- 空集合显示虚线空态卡。

### 3.4 快捷插入

组名 58px + 胶囊 24px，5 组纵向排列；容器 **固定 148px + 内部滚动**（960 档同样 148px），保证下方编辑器高度稳定。

引用变量组用主色浅底胶囊；变量为空时该组显示一句斜体说明而不是空行。

### 3.5 表达式编辑器

`C# 表达式` 小标题条（含 `Ctrl+Space 补全`）+ 行号槽 + 等宽正文；空表达式时显示占位提示。

### 3.6 卡内错误条（只在有错时出现）

红底 + 图标 + 文案，位于编辑器下方。成功态**不显示**这条（摘要归底栏）。

### 3.7 等待条件参数

3 列（轮询间隔 / 最大超时 / 超时处理）+ 2 列（超时说明 / 报警变量名）。

- 报警变量名：手填 + 候选下拉，`small` 说明「候选只列「用户可写的全局 bool 变量」；超时时置为 true」。
- 去掉 v1 的 132px 内层滚动上限。

### 3.8 分支匹配

判据（手填 + 候选下拉）+ 匹配值 + **本容器已用匹配值**列表（当前 / 已占用 / 兜底三态）+ 卡内错误条。

### 3.9 For

顶部对齐字段网格：循环次数（左）+ 数据源（右，只读框 + 选择数据源 / 解除）+ 摘要条（循环体 N 步 / 默认次数 / 数据源）+ 用途说明。

### 3.10 底栏 56px

左：校验摘要（绿「草稿已校验，可保存」/ 红「校验未通过：N 处问题 · 点「保存并应用」会再次提示并停在弹窗」/ 兜底「兜底分支无需条件，可直接保存」/ 已连线「循环次数由数据源驱动」）。右：取消 / 保存并应用（**始终可点**）。

## 4. 状态矩阵

| 状态 | 设计表现 | WPF 触发条件 |
| --- | --- | --- |
| 条件逻辑 · 正常 | 3 条分支；选中「合格」；3 个变量；2 行表达式 | `IsForMode=false`、`IsCaseMode=false`、`IsWaitMode=false`；`Variables.Count>0` |
| 条件逻辑 · 变量空 | 变量卡虚线空态；引用变量组显示斜体说明 | `Variables.Count==0` |
| 条件逻辑 · 校验失败 | `Var_3` 红框；卡内红条「第 3 行：别名重复」；底栏红字 1 处 | `IDataErrorInfo` 返回非空 |
| 条件逻辑 · 空表达式 | 左栏两条必需分支变红；编辑器占位提示；卡内红条「条件表达式为空」 | `RequiresExpression && IsNullOrWhiteSpace(Expression)` |
| 条件逻辑 · 兜底分支 | 表达式卡换成说明卡（含步骤数 / 共用变量 / 行动按钮） | `SelectedBranchNeedsExpression == false` |
| 分支匹配 · 正常 | 3 条（A 类 / B 类 / 默认）；判据 + 匹配值；已用匹配值列表 | `IsCaseMode=true` |
| 分支匹配 · 校验失败 | 判据未声明 + 匹配值重复；B 类行标红；底栏 2 处 | `ValidateCaseBranches` 返回 2 条 |
| 分支匹配 · 兜底 | 默认分支说明卡（「该分支是默认分支」措辞） | `IsCaseMode && !RequiresExpression` |
| 等待条件 · 正常 | 1 条分支；3+2 参数；表达式编辑器 | `IsWaitMode=true` |
| 等待条件 · 校验失败 | 轮询间隔红条「不能小于 10ms」 | `SaveWait` 的 `pollIntervalMs < 10` |
| 计次循环 · 正常 | 无左栏无变量卡；字段网格 + 摘要 | `IsForMode=true`、未连线 |
| 计次循环 · 已连线 | 数据源显示地址；出现「解除」；底栏改文案 | `HasLoopCountLink == true` |
| 960 最小档 | 左栏仍 240px；右侧内边距压缩；快捷插入仍 148px；参数网格 2 列 | 窗口宽 960 |

## 5. 契约保留（一个都不能改）

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `DataContext = ConditionEditorViewModel`（Prism `IDialogAware`） | 宿主靠 `ShowDialog("ConditionEditor", ...)` 注入 |
| C2 | `Branches` / `SelectedBranch` / `SelectedIndex="-1"` | 进弹窗不自动选中第一项；setter 连带刷新 4 个派生属性并发 `BranchChanged` |
| C3 | `txtExpression` + `TextChanged` + `BranchChanged` | code-behind 靠它们读写表达式 |
| C4 | `QuickInsert_Click` + 每个按钮的 `Tag` 精确内容 | Tag 就是插入片段 |
| C5 | `Variables` + `AliasName` 的 `IDataErrorInfo` | 行内查重与 `ValidateDraft` 都读它 |
| C6 | `AddVariableCommand` / `RemoveVariableCommand` / `BindVariableCommand` | 新增 / 删除 / 打开 `DataBindView` 单绑弹窗 |
| C7 | `IsForMode` / `IsWaitMode` / `IsCaseMode` 三布尔 | 四套编辑区显隐；互斥由 VM 保证 |
| C8 | `LoopCountText` / `LoopCountSourceText` / `HasLoopCountLink` / `LinkLoopCountCommand` / `UnlinkLoopCountCommand` | For 连线草稿；取消即丢弃 |
| C9 | `JudgeExpressionDraft` / `TimeoutActionOptions`（`StepConfigOption`）/ `AlarmVariableCandidates` | 判据草稿、超时处置（显示 `Display` / 值成员名，R40）、报警变量候选 |
| C10 | `SaveCommand` / `CancelCommand` | 保存 = 校验 + 写回 + `Version++`；取消 = 丢弃 |
| C11 | `Title` / `EditorTitle` / `NoConditionTitle` / `NoConditionHintText` | 文案随模式切换，VM 已拼好 |

## 6. 令牌映射（全部落既有公共键，**v2 不新增任何令牌**）

| Web 变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--surface-3` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--border` | `#E2E8F0` | `DialogBorderBrush` |
| `--border-soft` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` |
| `--text` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--text-3` | `#94A3B8` | `DialogTextMutedBrush` |
| `--primary` | `#0EA5E9` | `DialogAccentBrush` |
| `--primary-strong` | `#0284C7` | 由 `DialogPrimaryButton` 的 hover 提供（不单独取键） |
| `--primary-text` | `#0369A1` | 近似 `DialogAccentBrush`；变量胶囊文字可用 `DialogAccentBrush` |
| `--primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--success` / `--success-text` | `#22C55E` / `#15803D` | `DialogSuccessBrush` / `DialogSuccessTextBrush` |
| `--success-soft` / `--success-border` | `#F0FDF4` / `#BBF7D0` | `DialogSuccessSubtleBrush` / `DialogSuccessBorderBrush` |
| `--danger` / `--danger-text` | `#EF4444` / `#B91C1C` | `DialogDangerBrush` / `DialogDangerTextBrush` |
| `--danger-soft` / `--danger-border` | `#FEF2F2` / `#FECACA` | `DialogDangerSubtleBrush` / `DialogDangerBorderBrush` |
| `--warning` / `--warning-text` | `#F59E0B` / `#B45309` | `DialogWarningBrush` / `DialogWarningTextBrush` |
| `--mono` | `Cascadia Mono, Consolas` | `PluginMonoFontFamily`（`Consolas, Microsoft YaHei`） |
| 卡片圆角 | 12 | `PluginCardCornerRadius`（8）或宿主既有 12 圆角键；见 §7 #3 |

## 7. 已知偏差（R130）

1. **字体度量**：Web 用 Segoe UI Variable / 微软雅黑；WPF 用 `Microsoft YaHei`，同字号行高字宽略有差异。
2. **窗口圆角与阴影**：Web 14px + 双层阴影；WPF 由宿主弹窗壳统一，不在本视图内画。
3. **圆角 12 vs 8**：Web 卡片 12px；WPF 若用 `PluginCardCornerRadius` 是 8px。若必须 12，需确认是否有既有 12 圆角键，否则登记为偏差（不新增令牌）。
4. **语法高亮配色**：Web 用 `tk-var` / `tk-num` / `tk-op` 近似色；WPF 由 AvalonEdit `C#` 高亮 + `VariableColorizer`（变量亮蓝 `#1890FF` 硬编码）提供，不逐 token 对齐。
5. **代码补全弹窗**：Web 不演示；WPF 由 `CompletionWindow` 提供，样式随 AvalonEdit 默认。
6. **卡内实时校验**：Web 稿的错误条文案来自 `ValidateDraft` / `ValidateCaseBranches` / `SaveWait` 的**保存时**结果。若要在编辑期实时显示，需新增视图侧解析（见 §9）。
7. **`DataGrid` → 自绘行**：Web 用四列 grid；WPF 建议 `ItemsControl` + 行 `Grid`，避免 `DataGrid` 列宽与 `MinRowHeight` 差异。
8. **动效**：Web 有 150ms hover/focus 过渡与光标闪烁；WPF 只保留焦点环（`FluentButtonFocusVisual`，内缩两层）与既有 hover 触发器。
9. **滚动条**：Web 8px 细条；WPF 系统滚动条约 17px，960 档可用宽度少约 17px。
10. **DPI**：125% / 150% 下 1px 线变 1.25/1.5 物理像素；按 100% 设计。
11. **`IsPressed` / `:active`**：Web 有按下态；WPF 由共享按钮样式提供，不在本视图重写。
12. **候选下拉的视觉**：Web 用 `input` + `datalist`（原生下拉）；WPF 用 `IsEditable=True` 的 `ComboBox`，外观由 `DialogCombo` 提供，下拉箭头位置与 Web 略有差异。
13. **分支匹配「已用匹配值」列表**：Web 是设计态示意；WPF 若要做需在视图侧遍历 `Branches` 生成只读列表（数据都在 `Branches[i].Expression` 里，不改 VM）。
14. **焦点环位置与双色**：Web `:focus-visible` 是 `outline: 2px solid primary; outline-offset: 2px`（外扩）；WPF 用 `FluentButtonFocusVisual` **内缩 2px**（Adorner 外扩会与相邻控件/底栏边缘重叠），并在 Accent 主按钮上加 1px 白色内圈——否则蓝环叠蓝底不可见。悬停叠加层已铺满整颗按钮（2026-10-20 修正）。

## 8. 未纳入本版（需新增功能或契约）

| 项 | 原因 |
| --- | --- |
| 表达式实时语法校验 | 需在 `TextChanged` 里跑 `DynamicExpresso.Interpreter.Parse`（VM 目前只在保存时校验）；属新增行为，需确认性能可接受 |
| 分支表达式摘要实时刷新 | 当前摘要只在切分支时重读；要实时需订阅当前分支 `PropertyChanged` |
| 变量行内红框悬浮提示 | 自绘行需重新接 `Validation.ErrorTemplate` + ToolTip |
| 「已用匹配值」列表的实时去重 | 需视图侧遍历 `Branches`；可行但属新增展示逻辑 |
| 640 宽档 | 窗口与视图 `MinWidth` 均为 960 |
| 模式胶囊点击切换 | 模式由靶节点类型决定，只读 |
| 分支增删 / 重命名 | 归流程栏右键结构命令 |

## 9. 复刻映射（确认后照此改 WPF）

| Web 区块 | WPF 落点 | 改法 |
| --- | --- | --- |
| 标题栏 72 + 图标 + 模式胶囊 | `ConditionEditorView.xaml:74-125` | 52→72；补 40px 图标；标题右侧加胶囊（三个 `DataTrigger`） |
| 左栏 240 + 分支条目 | `:141-192` | 208→240；`DataTemplate` 补序号块、`RequiresExpression` 徽标、`Expression` 摘要、错误态触发器 |
| 变量卡四列 | `:206-360` | `DataGrid` → `ItemsControl` + 行 `Grid`；新增类型列（`DataTypeName` 短名转换器） |
| 快捷插入固定高度 | `:542-803` | 组名 64→58；`MaxHeight` 200→148 且改为固定高度 + 内部滚动 |
| 表达式编辑器 | `:811-829` | 外框加小标题条；空态占位由 code-behind 控制 |
| 卡内错误条 | 新增 | 编辑器下方 `Border`，`Visibility` 绑校验结果 |
| 等待参数 3+2 | `:430-535` | 去 `MaxHeight=132`；3 列 + 2 列；报警变量改可编辑 `ComboBox` |
| 分支匹配判据 | `:399-420` | `TextBox` → 可编辑 `ComboBox`（候选 `Variables` 别名） |
| 分支匹配已用值列表 | 新增 | `ItemsControl` 只读列表 |
| For 字段网格 + 摘要 | `:894-1002` | 输入 160→150；数据源只读框 + 解除按钮；摘要条 |
| 底栏摘要 | `:1007-1024` | 左侧加状态位（`Text` + 图标 + 颜色触发器） |

## 10. 验收口径

- 同一屏、同一状态截图对照（R129）。
- 必查 6 个状态：正常 / 变量空 / 校验失败 / 空表达式 / 兜底 / For 已连线；两个宽度：1120 / 960。
- 差异必须逐条写进 §7，未关闭或未经用户接受的不得宣称完成（R132）。

## 11. 深链（评审用）

`design.html` 支持 query 参数直达状态：

| 参数 | 取值 | 例 |
| --- | --- | --- |
| `mode` | `condition` / `case` / `wait` / `for` | `?mode=case` |
| `state` | `normal` / `empty` / `invalid` / `empty-expr` / `fallback` / `for-linked` | `?state=for-linked` |
| `width` | `960` | `?width=960` |

组合例：`?mode=case&state=invalid&width=960`。

注意：`state=for-linked` 会自动切到 For 模式；`state=empty-expr` 会自动切到条件模式。

## 12. 变更规则

- 本基线确认后**只读**；改动升版本（`v2` → `v3`），同步更新本 README 与已复刻视图。
- 复刻遇到不可实现项按 R125 走升级决策，不得静默降级（R131）。
