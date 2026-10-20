# 公共消息弹窗（MessageDialog）· 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 日期 | 2026-10-20 |
| 确认日期 / 人 | 2026-10-20 / 用户（指令：好的 开始复刻吧） |
| 覆盖范围 | UI 库公共组件 `MessageDialog`：信息 / 成功 / 警示 / 危险四种语义，单 / 双按钮（三按钮组合作为规范展示，API 延后见 D3），结构化详情区，键盘与焦点契约；**不含**自定义内容弹窗（属性表格、文本输入）的改造 |
| 设计尺寸 | 卡片宽 480（默认）/ 560（宽档）；高度随内容；圆角 12；内边距 20 |
| 设计基线文件 | `design.html`（自包含、无外部依赖、内联 SVG 图标） |
| 设计证据 | `evidence\design-v1-*.png`（8 张：现场恢复 / 信息 / 成功 / 警示 / 危险 / 560 宽 / 无详情 / 关闭后） |
| 复刻状态 | **已复刻（2026-10-20）**；WPF 证据 `evidence\after-render-v1-*.png`；待用户真机确认 |
| 记录 | 本 README；`docs\code-changes\2026-10-20-公共消息弹窗MessageDialog复刻.md` |

## 定位

本文件是「Web 设计基线 → 用户确认 → WPF 复刻 → 同屏截图验收」四步流程的**第 ① 步**产物（R110/R111/R127）；
第 ③ 步复刻结果见下方「复刻结果」与 `docs\code-changes\2026-10-20-公共消息弹窗MessageDialog复刻.md`。

目标是把两类现存弹窗收成 **UI 库里的一个公共组件**：

- 系统 `MessageBox`：`VisionMaster\` 工程 12 处直接调用（App 现场恢复、布局加载/重置、图像视图、报警历史等），不可样式化；
- `EasyDialog` 的纯代码消息弹窗：约 40 处调用，固定「确定 / 取消」，没有语义图标、没有详情区。

**不新建第二套弹窗引擎**：组件对外是 `MessageDialog`，内部仍走 `EasyDialog` 已验证的透明遮罩窗口、并发锁、跨线程与取消保护（R120 复用优先），只替换卡片的视觉树。

Web 只作设计媒介，**不进入构建、不部署**（R126）。确认后按本基线复刻，第 ④ 步以同一屏、同一状态的截图对照验收。

## 一句话方向

**把「一段话 + 是/否」换成「语义图标 + 结构化详情 + 动作化按钮 + 明确键盘契约」，并让它成为 UI 库里唯一的消息弹窗。**

不新增弹窗引擎、不改业务命令语义；用四种语义、详情行与默认焦点保护，让「发生了什么、要做什么、按错会怎样」一眼可见。

## 审查清单（请重点看这几项）

- [ ] **详情区是否成立**：路径从一段话里拎出来，做成「标签 + 数量 chip + 等宽路径 + 复制」，是否比原对话框更好扫读。
- [ ] **主次是否清楚**：「打开目录」是唯一主色按钮，「稍后再说」是次要按钮；不再出现两颗同宽的「是(Y)/否(N)」。
- [ ] **危险确认是否安全**：红色只给「删除分组」，默认焦点在「取消」，Enter 不会执行删除。
- [ ] **四种语义是否够用**：信息 / 成功 / 警示 / 危险，图标底色与语义一一对应，不再用系统黄三角。
- [ ] **480 宽是否够**：现场恢复的两行详情在 480 下完整可读；560 档留给更长路径或多行说明。
- [ ] **是否保留 Y / N**：界面只显示动作文字，Y/N 作为隐藏快捷键保留，老用户不必改习惯。
- [ ] **是否接受「新弹窗的蓝」与旧弹窗不同**：本组件用 `DialogAccentBrush`（#0EA5E9），既有 `DialogPrimaryButton` 指向 Fluent 的 #0078D4（见 D1）。

## 现状审查（为什么现在这个弹窗丑）

| # | 现状 | 影响 |
| --- | --- | --- |
| 1 | 系统 `MessageBox`：系统字体、方角按钮、黄三角图标 | 与青蓝设计系统不是一套语言；同屏切换像两个软件 |
| 2 | 两套弹窗并存：`MessageBox` 12 处（`VisionMaster\` 工程）+ `EasyDialog` 约 40 处 | 标题、按钮文案、Esc / Y / N 行为各一套，口径无法统一 |
| 3 | `EasyDialog` 按钮固定「取 消 / 确 定」 | 无法表达「打开目录 / 稍后再说」这类动作化文案；用户必须先读正文才知道按钮做什么 |
| 4 | 没有语义图标 | 信息 / 成功 / 警示 / 危险全靠正文判断，扫读成本高 |
| 5 | 路径埋在正文里 | 不可复制、不可悬停看全，长路径直接换行 |
| 6 | 没有详情结构 | 多个文件 / 多个路径只能堆成一段话，数量与对象对不上 |
| 7 | 危险操作没有默认焦点保护 | 回车可能直接执行删除 |
| 8 | 按钮走 Fluent 蓝 `#0078D4`，Dialog 令牌是青蓝 `#0EA5E9` | 仓内两套蓝（既有债，见 D1） |
| 9 | 焦点 / 键盘口径不统一 | `MessageBox` 的 Y/N 与 `EasyDialog` 的 Esc 各一套，焦点环也不一致 |
| 10 | 没有尺寸档位 | `MessageBox` 由系统决定，`EasyDialog` min 350 / max 700，无设计档 |

## v1 方向

1. **一个组件、一个引擎**：`MessageDialog` 对外；内部沿用 `EasyDialog` 的透明遮罩窗口、并发锁、取消兜底与 `DispatcherPriority.Background` 防爆（不改线程语义）。
2. **四种语义**：Info / Success / Warning / Danger；图标块 36 × 36、圆角 10、浅底 + 同族描边。
3. **动作化按钮**：`PrimaryText` / `SecondaryText` 由调用方给，不再固定「确定 / 取消」；主操作永远最右。
4. **结构化详情**：`Details` 集合（标签 / 数量 / 路径），路径等宽 + 复制；最多 3 行，超过改用 560 宽。
5. **危险确认**：红色主按钮 + 默认焦点取消 + Enter 不删除；文案明确动作对象（「删除分组」）。
6. **键盘契约**：Enter 默认、Esc 取消、Y/N 兼容、Tab 循环、焦点环可见。
7. **两档宽度**：480 默认 / 560 宽档；高度随内容，v1 不出现内部滚动条。
8. **全令牌**：只引用 `Colors.xaml` 的 `Dialog*` 键与 Fluent 按钮基类；新增组件按钮族（见「组件归属与 API」）。
9. **迁移分三步**：先接 App 现场恢复（本设计的目标场景）→ 再逐处替换 12 处 `MessageBox` → 最后统一 `EasyDialog` 的消息调用（签名不变，调用点不动）。

## 布局与尺寸

```text
卡片 480（默认）/ 560（宽档），高度随内容
┌──────────────────────────────────────────────────────────────┐
│ 20                                                           │
│  [36×36 图标]  12  标题 16/600                      [30 ×] 20 │
│ 12                                                           │
│  正文 13 / 行高 1.75                                          │
│ 12                                                           │
│  ┌ 详情面板 r10 · p10/12 ──────────────────────────────────┐  │
│  │ 标签 56 │ 数量 chip 20 │ 等宽路径 h24 │ 复制 24        │  │
│  │ 标签 56 │ 数量 chip 20 │ 等宽路径 h24 │ 复制 24        │  │
│  └─────────────────────────────────────────────────────────┘  │
│ 10  提示 11.5                                                 │
│ 14  问题 13/500                                               │
│ 16  ───────────────────────────────────────────────────────  │
│ 14  [快捷键提示 11]                        [次操作] 8 [主操作] │
│ 18                                                           │
└──────────────────────────────────────────────────────────────┘
```

| 元素 | 尺寸 / 值 | 说明 |
| --- | --- | --- |
| 卡片宽度 | 480 / 560 | 默认 480；详情路径更长或多行说明时用 560 |
| 卡片圆角 | 12 | 与 `DialogCardShadow` 的既有圆角一致，不另造一档 |
| 卡片内边距 | 左右 20 / 上 20 / 下 18 | 4 的倍数刻度 |
| 图标块 | 36 × 36 · r10 | 字形 18px 描边图标，语义色 + 同族浅底 |
| 图标 ↔ 标题 | 12 | 标题 16px / 600 / 行高 22 |
| 正文 | 13px / 1.75 | `DialogTextSecondaryBrush`；强调词用主文本色 + 600 |
| 详情面板 | r10 · p10/12 | 底 `DialogRowAlternateBrush`，描边 `DialogBorderLightBrush`；行高 28 |
| 路径框 | h24 · 11px 等宽 | 白底 + 1px 描边 + 6px 圆角；超长省略，悬停显示全路径 |
| 问题行 | 13px / 500 | 与上文间隔 14；这是「要用户回答的那句话」，用主文本色 |
| 底栏 | 分隔线 1px + p14/20/18 | 分隔线 `DialogBorderLightBrush`，只做安静的分区 |
| 按钮 | h32 · p0/14 · r8 | 字 12.5 / 600；按钮间距 8；主操作永远最右 |
| 关闭按钮 | 30 × 30 · r8 | 复用 `DialogIconButton`（MinWidth/MinHeight=30）；距右 20、距上 20；悬停出浅灰底，不做红底 |
| 快捷键提示 | 11px | 底栏左侧，`DialogTextTertiaryBrush`（#64748B，4.8:1）；危险场景文案改成「Enter 取消」 |

## 交互与键盘

| 输入 | 行为 |
| --- | --- |
| Enter | 触发默认按钮：普通场景 = 主操作；危险场景 = 取消 |
| Esc | 关闭（= 次操作） |
| Y | 主操作（兼容旧 `MessageBox` 的肌肉记忆，界面上不显示） |
| N | 次操作（同上） |
| Tab / Shift+Tab | 主操作 → 次操作 → 关闭 循环（`KeyboardNavigation.TabNavigation="Cycle"`） |
| 点击遮罩 | **不关闭**（防误触；消息弹窗是模态决策） |
| 点击 × | 关闭（= 次操作） |
| 复制路径 | 图标变勾 1.4s；不弹 toast、不抢焦点；剪贴板被占用时保留图标并提示「复制失败」 |
| 焦点 | 打开即落在默认按钮；焦点环 2px 深蓝 + 2px 间隙，键盘可见 |

## 语义与按钮矩阵

| 场景 | 语义 | 主按钮 | 次按钮 | 默认焦点 |
| --- | --- | --- | --- | --- |
| 现场恢复 | Warning | 打开目录 | 稍后再说 | 主 |
| 流程未编译 | Info | 编译并运行 | 取消 | 主 |
| 方案已保存 | Success | 好 | —（单按钮） | 主 |
| 通讯未连接 | Warning | 打开通讯设置 | 忽略并继续 | 主 |
| 删除分组 | Danger | 删除分组（红） | 取消 | **次** |

## 令牌映射

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--md-surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--md-card-border` | `#E3E9F2` | `DialogCardBorderBrush` |
| `--md-surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--md-surface-3` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--md-border` | `#E2E8F0` | `DialogBorderBrush` |
| `--md-border-soft` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--md-border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` |
| `--md-text` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--md-text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--md-text-3` | `#94A3B8` | `DialogTextMutedBrush`（仅占位 / 禁用） |
| `--md-text-4` | `#64748B` | `DialogTextTertiaryBrush`（**新增**，11~12px 说明文字，4.8:1） |
| `--md-primary` | `#0EA5E9` | `DialogAccentBrush` |
| `--md-primary-strong` | `#0284C7` | `DialogAccentDarkBrush` |
| `--md-primary-deep` | `#0369A1` | `DialogAccentTextBrush` |
| `--md-primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--md-primary-light` | `#E0F2FE` | `DialogAccentLightBrush` |
| `--md-primary-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--md-success*` | `#22C55E / #15803D / #F0FDF4 / #BBF7D0` | `DialogSuccessBrush` / `DialogSuccessTextBrush` / `DialogSuccessSubtleBrush` / `DialogSuccessBorderBrush` |
| `--md-warning*` | `#F59E0B / #B45309 / #FFF4E5 / #FDE68A` | `DialogWarningBrush` / `DialogWarningTextBrush` / `DialogWarningSubtleBrush` / `DialogWarningBorderBrush` |
| `--md-danger*` | `#EF4444 / #B91C1C / #FEF2F2 / #FECACA` | `DialogDangerBrush` / `DialogDangerTextBrush` / `DialogDangerSubtleBrush` / `DialogDangerBorderBrush` |
| 投影 | `0 26 64 rgba(15,23,42,.30)` | `DropShadowEffect`：`BlurRadius=28`、`ShadowDepth=6`、`Opacity=.18`、`Color={StaticResource DialogShadowColor}` |
| 字体 | Segoe UI / 微软雅黑 | 系统字体族；路径用 `Cascadia Mono / Consolas` |

**新增颜色键：1 个。** `DialogTextTertiaryBrush`（`#64748B`）：只给 11~12px 的说明文字（底栏快捷键提示、详情提示），因为既有 `DialogTextMutedBrush`（`#94A3B8`）在白底上只有约 2.6:1、`DialogTextSecondaryBrush`（`#475569`）又太深。`DialogTextMutedBrush` 仍只用于占位 / 禁用。**不造第二套蓝 / 灰 / 红**。

## 组件归属与 API

| 文件 | 职责 |
| --- | --- |
| `UI\Controls\CustomControl\Message\MessageDialogOptions.cs` | 公共 API、`MessageDialogOptions`、`MessageResult`；`MessageDialog` 静态入口也在此文件；内部转调 `EasyDialog` 引擎 |
| `UI\Controls\CustomControl\Message\MessageDialogView.xaml(.cs)` | 卡片视觉树（`DataContext = MessageDialogOptions`） |
| `UI\Controls\Themes\Colors.xaml` | 新增第一层令牌 `DialogTextTertiaryBrush`（#64748B，11~12px 说明文字） |
| `UI\Controls\Themes\Dialog\Dialog.Message.xaml` | 卡片 / 图标 / 标题 / 正文 / 提示 / 问题 / 详情面板 / 路径 / 底栏提示样式；**只就地合并 `Colors.xaml`**，不引用兄弟分册（R30） |
| `UI\Controls\Themes\Dialog\Dialog.Chip.xaml` | **未新增**（chip 族 `DialogMessageChip*` / `DialogMessageChipText*` 在 `Dialog.Message.xaml`，唯一键，避免 `FluentAliases` 对 `DialogChip*` 的覆盖） |
| `UI\Controls\Themes\Fluent\FluentAliases.xaml` | 新增 `DialogMessagePrimaryButton` / `DialogMessageSecondaryButton` / `DialogMessageDangerButton` + `DialogMessageCopyButton` / `DialogMessageCloseButton`（`BasedOn` `DialogIconButton`）——`BasedOn` 指向 Fluent 样式，必须放这里（R30） |
| `UI\Controls\Themes\Generic.xaml` | 第四层合并链加 `Dialog.Message.xaml`（必须在 `FluentAliases.xaml` 之前） |
| `UI\Controls\CustomControl\Message\EasyDialog.cs` | `CreateDialogContent` 在 `customContent == null` 时改用 `MessageDialogView`；`BuildDialogUI` 保留给自定义内容（属性表格 / 文本输入），路径不动 |

```csharp
// UI 库：UI.CustomControl.MessageDialog
// 不新建第二套弹窗引擎：内部仍走 EasyDialog 的遮罩窗口 / 并发锁 / 取消保护，
// 只把消息卡片的视觉树换成这套卡片（自定义内容仍走 BuildDialogUI）。
var result = MessageDialog.Show(new MessageDialogOptions
{
    Severity      = MessageSeverity.Warning,
    Title         = "上次运行的现场",
    Message       = "检测到 1 份未保存的方案草稿，已归档到下面的位置。",
    Details       =
    {
        new("方案草稿", "1 份", @"Autosave\recovered"),
        new("崩溃现场", "1 个", @"crash_reports\20261020-091233"),
    },
    Hint          = "可用「打开方案」载入草稿继续编辑。",
    Question      = "现在打开草稿所在目录吗？",
    PrimaryText   = "打开目录",
    SecondaryText = "稍后再说",
});

if (result == MessageResult.Primary)
    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
```

| 选项 | 默认 | 说明 |
| --- | --- | --- |
| `Severity` | `Info` | Info / Success / Warning / Danger |
| `Title` | 必填 | 一句话说清「这是什么现场」 |
| `Message` | 必填 | 正文；支持 `\n` 换行，v1 不做富文本 |
| `Details` | 空 | 最多 3 行；每行 `Label / Chip / Path / Tone` |
| `Hint` | 空 | 正文与问题之间的补充说明 |
| `Question` | 空 | 要用户回答的那句话；单按钮场景可空 |
| `PrimaryText` | 必填 | 动作化文案（「打开目录」而非「是」） |
| `SecondaryText` | `null` | `null` = 单按钮 |
| `PrimaryTone` | `Primary` | `Danger` 时主按钮红色，且默认焦点自动落到次按钮 |
| `ShowClose` | `true` | 右上角 × |
| `DefaultFocus` | 主 / 危险时次 | 可在调用点覆盖 |
| `Owner` | 当前激活窗口 | 无激活窗口时回退主窗口（沿用 `EasyDialog` 的取法） |

`MessageResult`：`Primary` / `Secondary` / `Close`（× 与 Esc 都归 `Secondary`，与现有 `EasyDialog` 的 `bool` 语义兼容）。

## WPF 复刻映射（第 ③ 步，2026-10-20 已复刻）

| 设计项 | WPF 实现 |
| --- | --- |
| 宿主与遮罩 | 沿用 `EasyDialog.InternalExecuteAsync` 的透明遮罩窗口：`WindowStyle=None`、`AllowsTransparency=True`、`Background=#700F172A`、`ShowInTaskbar=False`、Owner 跟随与位置同步、`Closed` 兜底、`PreviewKeyDown` 接 Esc / Y / N / Enter（仅消息卡片，自定义内容只接 Esc）；只替换卡片视觉树 |
| 卡片 | 新增 `DialogMessageCard`（`Dialog.Message.xaml`）：白底、12px 圆角、1px `DialogCardBorderBrush`、单层 `DropShadowEffect` |
| 图标块 | `DialogMessageIcon` + 四个语义变体（Info/Success/Warning/Danger）：36×36、r10、浅底 + 同族描边；字形走 `DialogIconFont`（FA6 Pro Solid：`\uF05A` / `\uF058` / `\uF071` / `\uF057`，已逐个核对，均有真实字形） |
| 标题 / 正文 / 提示 / 问题 | `DialogMessageTitle`（16/600）、`DialogMessageLead`（13/1.75）、`DialogMessageHint`（11.5，`DialogTextTertiaryBrush`）、`DialogMessageQuestion`（13/500） |
| 详情面板 | `DialogMessageDetails`（Border）+ `ItemsControl`（`ItemsSource=Details`）；行模板 `Grid` 列宽 `56 / Auto / * / 24`；chip 在**视图层**引用组件专用的 `DialogMessageChipAccent/Neutral/Success/Danger` + `DialogMessageChipText*`（唯一键，避开 `FluentAliases` 对 `DialogChip*` 的覆盖）；危险文字取 `DialogDangerTextBrush`（满足 4.5:1） |
| 路径 | `Border Style=DialogMessagePath`（白底 + 1px 描边 + 6px 圆角）+ 内部 `TextBlock Style=DialogMessagePathText`（`Consolas, Cascadia Mono`、11px、`TextTrimming=CharacterEllipsis`）；`ToolTip` 挂全路径（`DialogToolTipStyle`） |
| 复制按钮 | `DialogMessageCopyButton`（24×24、`Padding=0`、`MinWidth/MinHeight=24`）：FA copy 字形；`Click` → `Clipboard.SetText`；成功后 1.4s 换成 check 字形；失败保留图标并提示 |
| 底栏 | `Grid` 两列：左 `DialogMessageFootHint`（11px，`DialogTextTertiaryBrush`），右 `StackPanel` 按钮组；上边 1px `DialogBorderLightBrush` 分隔线 |
| 按钮 | `DialogMessagePrimaryButton`（`DialogAccentBrush` + 白字 600）、`DialogMessageSecondaryButton`（白底 + `DialogBorderBrush` + `DialogTextSecondaryBrush`）、`DialogMessageDangerButton`（`DialogDangerBrush` + 白字）；高 32、左右 14、r8、12.5/600 |
| 键盘 | 默认按钮 `IsDefault=True`；次按钮 `IsCancel=True`（单按钮场景不设，Esc 由宿主收口成 `Secondary`）；危险场景把 `IsDefault` 放到次按钮；`PreviewKeyDown` 接 Esc / Y / N / Enter（焦点已在按钮上时 Enter 交给 `ButtonBase`）；`KeyboardNavigation.TabNavigation="Cycle"` |
| 焦点 | 所有可聚焦控件挂 `DialogFocusVisual`；`Loaded` 后经 `Dispatcher.BeginInvoke(..., DispatcherPriority.Input)` 调 `FocusDefault()` 落到默认按钮，不用 `Focusable=False`（R135） |
| 关闭 | `Button Style=DialogMessageCloseButton`（`BasedOn DialogIconButton`，30×30，`Padding=0`；**不要再写 `Width/Height=28`**，会被 `MinWidth/MinHeight=30` 夹回）；FA xmark 字形；`ToolTip`「关闭（Esc）」 |
| 动效 | 本批未做动效（以稳定为先）；后续如加，卡片 `Opacity 0→1` + `TranslateY 8→0`，160ms `FluentEaseOut`，不稳定就继续不做 |

**合并顺序（R30）**：`Dialog.Message.xaml` 只合并 `Colors.xaml`，样式里的 `StaticResource` **不跨分册**——它不能 `BasedOn` `DialogChip*`（兄弟分册 `Dialog.Chip.xaml`）、也不能 `BasedOn` `DialogIconButton`（`FluentAliases.xaml` 在它之后合并）。因此：chip 族 `DialogMessageChip*` / `DialogMessageChipText*` 定义在 `Dialog.Message.xaml`，由**视图层**转换器引用（视图加载时全部字典已合并）；三个按钮与复制 / 关闭按钮放 `FluentAliases.xaml`；`Generic.xaml` 的第四层在 `FluentAliases.xaml` 之前合并 `Dialog.Message.xaml`。`Dialog.Chip.xaml` 未新增同名键。

## 迁移清单

**第 1 批（本设计的目标场景，2026-10-20 已落地）**

| 位置 | 现状 | 改法 |
| --- | --- | --- |
| `VisionMaster\App.xaml.cs:652-710` `ShowRecoveryNotice()` | `MessageBox.Show(..., YesNo, Warning)` | `MessageDialog.Show` + 两条 `Details`（方案草稿 / 崩溃现场）；`Primary` 才 `Process.Start` 打开目录 |

**第 2 批（`MessageBox.Show`：本批迁 2 处，剩余 10 处）**

| 文件 | 行 | 场景 | 建议语义 |
| --- | --- | --- | --- |
| `VisionMaster\App.xaml.cs` | 615 / 617 | 视觉引擎不可用 | Danger（单按钮「好」） |
| `VisionMaster\Commands\LoadLayoutCommand.cs` | 19 | 加载布局提示 | Info |
| `VisionMaster\Commands\ResetLayoutCommand.cs` | 16 / 27 / 37 | 重置布局确认 / 失败 | Warning / Danger |
| `VisionMaster\Views\ImageView.xaml.cs` | 170 / 176 | 图像操作提示 / 错误 | Info / Danger |
| `VisionMaster\Views\ScadaAlarmHistoryView.xaml.cs` | 95 / 96 | 报警历史确认 | Warning（OKCancel） |

**第 3 批（`EasyDialog` 约 40 处消息调用，2026-10-20 已随引擎改造自动生效）**

`EasyDialog.ShowSync(string title, string message)` / `ShowAsync` 的签名不变，内部改为 `MessageDialog`，返回值仍映射为 `bool`（`Primary` = true）；调用点零改动。`ShowCustomAsync`（属性表格、文本输入）**不在本批**，见 D2。

## 复刻结果（2026-10-20）

| 项 | 结果 |
| --- | --- |
| 组件 | `MessageDialogOptions.cs`（含 `MessageDialog` 入口）+ `MessageDialogView.xaml(.cs)`；四语义、单 / 双按钮、结构化详情、复制、焦点与键盘契约 |
| 引擎 | `EasyDialog` 核心 TCS 改为 `MessageResult`；旧 `ShowAsync/ShowSync(title, message)` 仍返回 `bool`（`Primary` = true），约 40 处旧调用零改动；`ShowCustomAsync` 仍走旧卡片 |
| 应用迁移 | `App.xaml.cs` 的 `ShowRecoveryNotice` 已改用 `MessageDialog.Show`；`MessageBox.Show` 12 处中已迁 2 处，剩余 10 处见第 2 批清单 |
| 构建 | `UI.csproj` / `VisionMaster.csproj` 均 0 错误（仅既有警告） |
| 冒烟 | `UIThemeSmokeTest.exe --message-dialog` → **58 PASS / 0 FAIL**；全量 → exit 0、0 FAIL |
| 断言宿主 | `FlowCanvasChecks` → **2932 通过 / 0 失败**（含 R30 资源断链与 R68 图标按钮裁切契约） |
| 证据 | `evidence\after-render-v1-recovery.png`、`evidence\after-render-v1-danger.png` |
| 记录 | `docs\code-changes\2026-10-20-公共消息弹窗MessageDialog复刻.md` |

## 已知偏差（WPF 复刻时按 R130 记录）

1. **字体度量**：同字号下 Segoe UI / 微软雅黑与 Web 的字宽、行高略有差异；不按 px 硬套字体。
2. **图标**：Web 用 Lucide 描边 SVG；WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`），形状不同、语义一致。
3. **阴影**：Web 是两层 `box-shadow`；WPF 单层 `DropShadowEffect`（`BlurRadius=28`、`ShadowDepth=6`、`Opacity=.18`）。
4. **遮罩**：Web 是纯色 `rgba(15,23,42,.44)`；WPF 透明窗口用 `#700F172A`，**不做背景模糊**（`AllowsTransparency` 窗口加模糊的代价与兼容性未验证）。
5. **动效**：Web 180ms `cubic-bezier(.2,0,0,1)`；WPF 可选 160ms Storyboard，或不做动效（以稳定为先）。
6. **焦点环**：Web 是 2px outline + 2px offset；WPF 普通按钮走 `DialogFocusVisual`，危险主按钮走同形的红色 `DialogMessageDangerFocusVisual`（0.45 透明）。
7. **滚动**：v1 详情最多 3 行、不出现内部滚动条；超过 3 行改用 560 宽（D6）。
8. **复制**：Web 用 `navigator.clipboard`；WPF 用 `Clipboard.SetText`，剪贴板被其它进程占用时保留图标并提示「复制失败」。
9. **字形码点**：FA6 Pro Solid 的 info / success / warning / danger / xmark / copy 需逐个核对；缺字形时改用 `IconDictionary` 的路径图标（不引入第二套图标字体）。
10. **高 DPI**：本批按 96 DPI 出证据；125% / 150% 下 1px 线与图标块需真机复核。
11. **按钮蓝**：新组件用 `DialogAccentBrush`（#0EA5E9），既有 `DialogPrimaryButton` 是 Fluent 蓝 #0078D4；同屏并排时能看出差别（D1）。
12. **详情 chip**：用组件专用 `DialogMessageChip*` / `DialogMessageChipText*`（唯一键），不复用 `DialogChip*`——`FluentAliases.xaml` 最后合并会把同名 `DialogChip*` 转发到 Fluent 令牌（成功绿 #0E7A0D 等），与设计基线的 Dialog 令牌不一致；危险文字取 `DialogDangerTextBrush`（深红，约 4.9:1）。
13. **无障碍**：按钮 / 关闭 / 复制挂 `AutomationProperties.Name`；路径复制结果通过状态文本或 `LiveSetting` 播报（v1 可延后）。
14. **窗口尺寸**：480 / 560 固定宽；高度随内容；超长正文换行，不滚动（v1）。

### WPF 复刻实测偏差（2026-10-20）

| # | 偏差 | 现状 / 原因 | 处理 |
| --- | --- | --- | --- |
| 1 | 复制按钮圆角 8（`FluentButtonStyle`）vs 设计 6 | 按钮族 `BasedOn FluentButtonStyle` | 接受；如需 6 需在 `DialogMessageCopyButton` 覆盖 `CornerRadius`，Fluent 模板是否吃该 Setter 需另验 |
| 2 | 按钮 hover / pressed 沿用 Fluent 黑色叠加层（5% / 10%） | 按钮族 `BasedOn FluentButtonStyle` | 接受；设计稿的 Dialog 状态色未复刻 |
| 3 | 危险底栏提示用泛化文案「危险操作需点击或 Tab 到「删除分组」」 | 组件不知道业务动作，只能拼主按钮文案 | 接受；设计稿是「删除需点击…」 |
| 4 | 富文本 / 加粗未支持（D7） | `Message` 是纯 `string` | 危险 lead 的 `<b>` 片段按纯文本渲染 |
| 5 | 旧 `EasyDialog.ShowAsync(title, message)` 现在统一 Info 图标、480 宽 | 第 3 批有意行为（签名不变，视觉统一） | 接受；调用点零改动 |
| 6 | `Details` 超过 3 行不截断 | 视图不做截断，超过 3 行会把卡片撑高 | 调用方契约，已写 XML 注释 |
| 7 | recovery 证据图未包含主按钮焦点环 | 渲染宿主是 `Border` 不是 `Window` | 真实窗口焦点由 danger 场景 `Window` 断言 + `IsDefault` 覆盖 |
| 8 | `GlyphButtonClipCheck` 静态扫描看不到 Style 里的 24 / 30 尺寸 | 判据只扫视图内联尺寸 | 复制 / 关闭按钮由运行期断言覆盖 |
| 9 | 两套蓝 D1 未动 | 既有 `DialogPrimaryButton` `#0078D4` vs 新 `DialogMessagePrimaryButton` `#0EA5E9` | 留独立批次统一 |

## v1 明确延后

| # | 项 | 需要什么 |
| --- | --- | --- |
| D1 | 统一两套蓝（#0078D4 vs #0EA5E9） | 独立批次：改 `DialogPrimaryButton` 的转发目标或全量迁移，影响 100+ 页面 |
| D2 | 自定义内容弹窗（属性表格 / 文本输入）收编到同一卡片壳 | 保持 `ShowPropertyGrid*` / `ShowTextInput*` 契约；需要 `MessageDialogView` 支持自定义内容槽 |
| D3 | 三按钮「保存 / 不保存 / 取消」 | `MessageDialogOptions` 增加 `TertiaryText` 与 `MessageResult.Tertiary` |
| D4 | 「不再提示」checkbox | `MessageDialogOptions` 增加 `CheckboxText`，结果带回勾选状态 |
| D5 | 动效与遮罩模糊 | WPF 性能 / 兼容性验证 |
| D6 | 详情区滚动（> 3 行） | 560 宽 + `ScrollViewer` 的设计与键盘可达性 |
| D7 | 富文本 / 链接 | 需要 Inline 内容模型，不用纯 `string` |
| D8 | 声音 / 系统通知 | 不在本组件职责内 |

## 证据清单

| 文件 | 状态 |
| --- | --- |
| `design.html` | 已产出（自包含，可直接浏览器打开；支持 `?scene=` / `?w=` / `?details=` / `?open=` 深链接） |
| `evidence\design-v1-recovery.png` | 已产出（现场恢复：480 宽 + 两行详情 + 主按钮焦点） |
| `evidence\design-v1-info.png` | 已产出（信息：无详情，详情开关禁用） |
| `evidence\design-v1-success.png` | 已产出（成功：单按钮「好」） |
| `evidence\design-v1-warning.png` | 已产出（警示：两行离线状态 chip） |
| `evidence\design-v1-danger.png` | 已产出（危险确认：红色主按钮，默认焦点在取消） |
| `evidence\design-v1-width560.png` | 已产出（560 宽档） |
| `evidence\design-v1-nodetails.png` | 已产出（详情区关闭） |
| `evidence\design-v1-closed.png` | 已产出（关闭后：遮罩淡出 + 结果提示 + 重新打开） |
| `evidence\after-render-v1-recovery.png` | 已产出（WPF 现场恢复：480 宽 + 两行详情 + 主按钮） |
| `evidence\after-render-v1-danger.png` | 已产出（WPF 危险确认：红色主按钮，默认焦点在取消） |

> 证据来自 `design.html` 的 headless 渲染（Edge `--headless=new`，1200×900，`--virtual-time-budget=2000` 等动效结束后截图）。
> 设计稿只证明布局与状态；WPF 复刻证据已产出（`after-render-v1-*.png`，由 `UIThemeSmokeTest --message-dialog-render` 离屏渲染），
> 真机交互验收待用户确认。

## 变更规则

设计基线确认后只读。需要调整时升版本（v2），并同步更新：

1. 本目录 `README.md` 的版本 / 日期 / 确认人；
2. `design.html` 的布局、令牌与状态；
3. `docs\UI设计基线\README.md` 的基线索引；
4. 已复刻的 WPF 组件、令牌映射表与 `docs\code-changes\` 记录。
