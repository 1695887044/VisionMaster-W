# 2026-10-20 公共消息弹窗 MessageDialog 复刻

> 起因：用户 2026-10-20 确认设计基线 `docs\UI设计基线\message-dialog\`（v1），
> 并指令「好的 开始复刻吧」。
> 结论：UI 库新增公共组件 `MessageDialog`（四语义 / 结构化详情 / 键盘契约），
> 引擎仍是 `EasyDialog`；现场恢复改为新卡片，旧 `ShowAsync(title, message)` 约 40 处调用零改动，
> `ShowCustomAsync`（属性表格 / 文本输入）仍走旧卡片。

---

## 1. 背景与设计基线

本批是「Web 设计基线 → 用户确认 → WPF 复刻 → 同屏截图验收」四步流程的**第 ③ 步**（R110/R111/R127）。

| 项 | 内容 |
| --- | --- |
| 设计基线 | `docs\UI设计基线\message-dialog\README.md` + `design.html`（v1） |
| 确认 | 2026-10-20，用户指令「好的 开始复刻吧」 |
| 覆盖 | 信息 / 成功 / 警示 / 危险四种语义；单 / 双按钮；结构化详情（标签 + 数量 chip + 等宽路径 + 复制）；480 / 560 两档宽度；键盘与焦点契约 |
| 不含 | 自定义内容弹窗（属性表格、文本输入）的视觉改造；剩余 10 处 `MessageBox` 的逐处迁移 |
| 引擎口径 | **不新建第二套弹窗引擎**：沿用 `EasyDialog` 的透明遮罩窗口、并发锁、跨线程与取消保护（R120 复用优先），只替换卡片视觉树 |

设计基线里「组件归属与 API」「WPF 复刻映射」「已知偏差」三节已按本批实际落地结果同步
（见 `docs\UI设计基线\message-dialog\README.md`）。

---

## 2. 改动清单

### 2.1 令牌

| 文件 | 改动 |
| --- | --- |
| `UI\Controls\Themes\Colors.xaml:149` | 新增 `DialogTextTertiaryBrush` = `#64748B`（11~12px 说明文字，白底约 4.8:1）。**本批唯一新增颜色键** |

### 2.2 样式

| 文件 | 改动 |
| --- | --- |
| `UI\Controls\Themes\Dialog\Dialog.Message.xaml` | 新增卡片域样式分册，**29 个 `DialogMessage*` 键**：卡片 / 图标基样式 + 四变体 / 图标字形 / 标题 / 正文 / 提示 / 问题 / 详情面板 / 详情行 / 标签 / 路径 / 路径文字 / chip 族（2 基样式 + 4 底色 + 4 文字色）/ 底栏 / 底栏提示。只就地 merge `Colors.xaml` |
| `UI\Controls\Themes\Fluent\FluentAliases.xaml:129/142/155/175/190` | 新增 `DialogMessagePrimaryButton` / `DialogMessageSecondaryButton` / `DialogMessageDangerButton`（`BasedOn FluentButtonStyle`，高 32 / Padding `14,0` / 12.5 / 600）与 `DialogMessageCopyButton`（24×24）/ `DialogMessageCloseButton`（30×30）（`BasedOn DialogIconButton`，`Padding=0`） |
| `UI\Controls\Themes\Generic.xaml:66` | 第四层在 `FluentAliases.xaml`（`:78`）**之前**合并 `Dialog.Message.xaml` |
| `UI\Controls\Themes\Dialog\Dialog.Chip.xaml` | **未新增同名键**。chip 族只存在于 `Dialog.Message.xaml`（原因见 §4） |

### 2.3 组件

| 文件 | 改动 |
| --- | --- |
| `UI\Controls\CustomControl\Message\MessageDialogOptions.cs` | 新增 `MessageSeverity` / `MessageTone` / `MessageChipTone` / `MessageResult`（`:9-42`）、`MessageDetail`（`:48`）、`MessageDialogOptions`（`:79`）、静态入口 `MessageDialog.Show/ShowAsync`（`:150`） |
| `UI\Controls\CustomControl\Message\MessageDialogView.xaml` | 卡片视觉树：Header（36 图标块 + 16/600 标题 + 30×30 关闭）/ Body（正文 + 详情 + 提示 + 问题）/ Footer（11px 快捷键提示 + 右对齐按钮组）。`KeyboardNavigation.TabNavigation="Cycle"`、根容器 `IsTabStop="False"`（`:6-7`） |
| `UI\Controls\CustomControl\Message\MessageDialogView.xaml.cs` | `ApplyOptions`（`:52`）按语义/语气/显隐/默认焦点重排；`FocusDefault`（`:124`）；`RequestPrimary/Secondary/Default`（`:134-146`）；复制成功 1.4s 换勾、失败保留图标并提示（`:217-284`）；chip 样式转换器（`:312/:335`） |

### 2.4 引擎

| 文件 | 改动 |
| --- | --- |
| `UI\Controls\CustomControl\Message\EasyDialog.cs:22/53` | 核心 TCS 从 `bool` 改为 `MessageResult`；`InternalExecuteAsync` 返回 `MessageResult`（`:33`） |
| `EasyDialog.cs:76-77` | 遮罩沿用 `#700F172A`（112/255 ≈ .439，对应设计基线 `rgba(15,23,42,.44)`）；窗口仍 `AllowsTransparency`、`ShowInTaskbar=False`、Owner 跟随与位置同步 |
| `EasyDialog.cs:109` | 窗口被外部关闭 → `Close`（保留原有的"必须收口，否则锁不释放"防爆语义） |
| `EasyDialog.cs:116-150` | 消息卡片在宿主窗口层转发键盘：Esc / Y / N / Enter；焦点已在按钮上时 Enter 交给 `ButtonBase` |
| `EasyDialog.cs:231-269` | `CreateDialogContent`：自定义内容仍走 `BuildDialogUI`；消息走 `MessageDialogView`；视图创建失败退回旧卡片并留 `Debug.WriteLine`；旧签名默认文案「确定 / 取消」 |
| `EasyDialog.cs:482/485` | 旧 `ShowAsync(title, message)` / `ShowSync(title, message)` 签名不变，返回 `bool`（`Primary` = true） |
| `EasyDialog.cs:489/495` | 新增 `ShowMessageAsync` / `ShowMessageSync`，返回 `MessageResult` |
| `EasyDialog.cs:506` | `ShowCustomAsync` 签名与行为不变，仍返回 `Task<bool>` |

### 2.5 应用

| 文件 | 改动 |
| --- | --- |
| `VisionMaster\App.xaml.cs:652-710` | `ShowRecoveryNotice` 从 `MessageBox.Show(..., YesNo, Warning)` 改为 `MessageDialog.Show`（Warning + 两条 `Details` + `Hint` + `Question` + 「打开目录 / 稍后再说」） |

### 2.6 测试

| 文件 | 改动 |
| --- | --- |
| `UIThemeSmokeTest\MessageDialogSmoke.cs` | 新增 `--message-dialog` 冒烟与 `--message-dialog-render` 出图；覆盖视觉树尺寸/颜色/字形、危险焦点、键盘映射、`EasyDialog` 集成、静态资源契约 |
| `UIThemeSmokeTest\Program.cs:204-208` | 注册两个命令行入口 |

---

## 3. 公共 API 契约

### 3.1 `MessageDialogOptions`（`MessageDialogOptions.cs:79`）

| 选项 | 默认 | 说明 |
| --- | --- | --- |
| `Severity` | `Info` | Info / Success / Warning / Danger |
| `Title` | `""` | 一句话说清「这是什么现场」 |
| `Message` | `""` | 正文；支持 `\n`，v1 不做富文本 |
| `Details` | 空 | `IList<MessageDetail>`，每行 `Label / Chip / Path / Tone`；空则整块隐藏 |
| `Hint` | `null` | 正文与问题之间的补充说明 |
| `Question` | `null` | 要用户回答的那句话 |
| `PrimaryText` | `"好"` | 动作化文案 |
| `SecondaryText` | `null` | `null` / 空 = 单按钮 |
| `PrimaryTone` | `Primary` | `Danger` 时主按钮红色，且默认焦点自动落到次按钮 |
| `ShowClose` | `true` | 右上角 × |
| `DefaultFocus` | 主；危险且未显式设置时次 | getter 内解析（`_defaultFocusSet` 区分「没设」与「显式设成 Primary」） |
| `Width` | `480` | 长路径 / 多行说明可传 560 |
| `Owner` | `null` | 沿用 `EasyDialog` 的「当前激活窗口 → 主窗口」取法 |

`MessageResult`：`Primary` / `Secondary` / `Close`。`Close` 只表示窗口被外部关闭；
Esc 与右上角 × 都归 `Secondary`（与旧 `EasyDialog` 的 `bool` 语义兼容）。

### 3.2 兼容面

| API | 返回 | 行为 |
| --- | --- | --- |
| `EasyDialog.ShowAsync(title, message)` | `Task<bool>` | 新卡片；`Primary` = true；默认文案「确定 / 取消」、Info 语义、480 宽 |
| `EasyDialog.ShowSync(title, message)` | `bool` | 同上 |
| `EasyDialog.ShowCustomAsync(title, content)` | `Task<bool>` | **不变**，仍走旧卡片（属性表格 / 文本输入） |
| `EasyDialog.ShowMessageAsync(options)` | `Task<MessageResult>` | 新卡片三态结果 |
| `EasyDialog.ShowMessageSync(options)` | `MessageResult` | 同上 |
| `MessageDialog.Show(options)` | `MessageResult` | 无 `Application` 上下文时返回 `Close`，不抛（`MessageDialogOptions.cs:157`） |
| `MessageDialog.ShowAsync(options)` | `Task<MessageResult>` | 同上（`:165`） |

---

## 4. 资源纪律（R30 / R31 / R68 / R135 / R140 / R141）

| 规则 | 本批落地 |
| --- | --- |
| R30 分册作用域 | `Dialog.Message.xaml` 只 merge `Colors.xaml`；不 `BasedOn` `DialogChip*`（`Dialog.Chip.xaml`）、`DialogIconButton`（`FluentAliases.xaml`）、`DialogFocusVisual` / `DialogToolTipStyle`（`Dialog.Misc.xaml`） |
| R31 跨册出路 | 按钮族放 `FluentAliases.xaml`（`BasedOn` Fluent 样式）；chip 族放本分册；没有把 `StaticResource` 改成 `DynamicResource`，也没有新增「分册 merge 兄弟分册」的边 |
| R68 图标按钮 | 复制 24×24、关闭 30×30 都在样式里写死 `Padding="0"`；关闭按钮**不写 28**（会被 `MinWidth/MinHeight=30` 夹回） |
| R135 焦点 / ToolTip | 主 / 次 / 关闭 / 复制四颗按钮都挂 `FocusVisualStyle="{StaticResource DialogFocusVisual}"`；ToolTip 统一 `<ToolTip Style="{StaticResource DialogToolTipStyle}">` |
| R140 硬编码颜色 | `Dialog.Message.xaml` 与 `MessageDialogView.xaml` 均无 `#RRGGBB`；颜色只取 `Dialog*Brush` |
| R141 样式下沉 | 详情行外壳 / 图标字形 / 关闭按钮都下沉为 `DialogMessageDetailRow` / `DialogMessageIconGlyph` / `DialogMessageCloseButton`；视图只留布局与绑定 |

**chip 为什么不用 `DialogChip*`**：`FluentAliases.xaml` 最后合并，会把同名 `DialogChip*` 转发到
`FluentChip*Style`（成功绿 `#0E7A0D` 等），与设计基线的 Dialog 令牌（`DialogSuccessTextBrush` `#15803D`）不一致。
所以新增组件专用的 `DialogMessageChip*` / `DialogMessageChipText*`（唯一键），并在视图层用转换器解析
（`MessageDialogView.xaml.cs:312/:335`）。`Dialog.Chip.xaml` 没有保留同名键。

> 编号说明：`AGENTS.md` 里 R139 / R140 / R141 各出现两次（不同章节重号）。本记录按内容引用：
> R140 = 第一方 XAML 禁止硬编码颜色；R141 = 可复用 UI 下沉 `Dialog\` 分册。

---

## 5. 危险确认键盘契约

| 输入 | 行为 | 落点 |
| --- | --- | --- |
| 默认焦点 | 普通场景 = 主按钮；`PrimaryTone=Danger` 且未显式设 `DefaultFocus` = 次按钮 | `MessageDialogOptions.cs:122-132`、`MessageDialogView.xaml.cs:109-112` |
| Enter | 走 `RequestDefault()`：普通场景主操作，危险场景取消 | `EasyDialog.cs:138-148` |
| Enter（焦点在某颗按钮上） | **不拦截**，交给 `ButtonBase` 自己收 | `EasyDialog.cs:143-144` |
| Y | 主操作 | `EasyDialog.cs:128-132` |
| N | 次操作 | `EasyDialog.cs:133-137` |
| Esc | 次操作 | `EasyDialog.cs:118-124` |
| 右上角 × | 次操作（`Secondary`） | `MessageDialogView.xaml.cs:215` |
| 窗口被外部关闭 | `Close` | `EasyDialog.cs:109` |
| 单按钮 | 次按钮隐藏、`IsCancel` 不设、底栏提示「Enter 关闭」 | `MessageDialogView.xaml.cs:117-120` |

危险场景的完整路径：默认焦点在「取消」→ Enter 只会取消；用户 **Tab 到红色主按钮**后按 Enter，
因为焦点已在 `Button` 上，宿主窗口的 `PreviewKeyDown` 直接 `return`，由 `ButtonBase` 触发「删除分组」。
这条路径由代码契约保证；**真按键验收见 §9 未验证项**。

---

## 6. 现场恢复迁移前后对照

| 项 | 迁移前（`MessageBox.Show`） | 迁移后（`MessageDialog.Show`） |
| --- | --- | --- |
| 语义 | 系统黄三角 Warning | `MessageSeverity.Warning` + 同族图标块 |
| 正文 | 一段话拼草稿数 / 崩溃现场 | 按「两者都有 / 只有草稿 / 只有现场」三种文案 |
| 详情 | 无 | `Details`：`方案草稿 / N 份 / Autosave\recovered`（Accent chip）、`崩溃现场 / 1 个 / crash_reports\<时间戳>`（Neutral chip） |
| 补充 | 无 | `Hint`「可用「打开方案」载入草稿继续编辑。」（仅有草稿时） |
| 问题 | 无 | `Question`「现在打开所在目录吗？」 |
| 按钮 | 是(Y) / 否(N) | 主「打开目录」/ 次「稍后再说」 |
| 打开目录 | 任一 Yes 分支各自 `Process.Start` | **仅 `MessageResult.Primary`** 才 `Process.Start`；有草稿优先开草稿目录，无草稿开 `crash_reports`（`App.xaml.cs:696-703`） |
| 失败 | 提示本身失败可能影响启动 | 整段 try/catch，异常只记一行 `Warn`（`App.xaml.cs:705-709`） |

---

## 7. 验证证据

| 项 | 结果 |
| --- | --- |
| `dotnet build UI\Controls\UI.csproj -v:q` | exit 0，0 错误 |
| `dotnet build VisionMaster\VisionMaster.csproj -v:q` | exit 0，0 错误（1 个既有 MSB3245 警告 + `PluginConfigShellViewModel.cs` 的 CS8604 既有警告，均与本批无关） |
| `UIThemeSmokeTest.exe --message-dialog` | **58 PASS / 0 FAIL** |
| `UIThemeSmokeTest.exe`（全量） | exit 0，**0 FAIL** |
| `FlowCanvasChecks` | **2932 通过 / 0 失败**（含 R30 资源断链契约与 R68 图标按钮裁切契约） |
| WPF 证据 | `docs\UI设计基线\message-dialog\evidence\after-render-v1-recovery.png`、`...-danger.png` |
| 文档同步 | `docs\UI设计基线\message-dialog\README.md`（复刻状态 / 组件映射 / 已知偏差 / 证据）与 `docs\UI设计基线\README.md`（基线索引状态）已更新 |

`--message-dialog` 覆盖的关键断言（均带实际输出）：

- 卡片 480 / 圆角 12 / 1px 描边；图标块 36×36 / 圆角 10；标题 16 / SemiBold；详情 2 行且行模板已实例化。
- 主按钮高 32 / 背景 = `DialogAccentBrush`；次按钮白底 + `DialogBorderBrush`；关闭 30×30 / `Padding=0`。
- recovery 默认焦点 = 主按钮；danger 默认焦点 = 次按钮（真 `Window` 里 `IsKeyboardFocused`）。
- `Y/N/Enter` 映射；`EasyDialog` 集成：`ShowMessageSync` 主按钮 → `Primary`、旧 `ShowSync` 次按钮 → `false`、Esc → `Secondary`、Y → `Primary`、N → `Secondary`、Enter → 默认按钮。
- 静态契约：`Dialog.Message.xaml` 29 个键齐全、只 merge `Colors.xaml`、无硬编码颜色、不跨册引用；`Generic.xaml` 在 `FluentAliases` 之前合并；旧 `EasyDialog` 签名未变；FA6 七个字形码点都有真实字形。

审查结论：reviewer = **pass-with-issues**。已修：

| 编号 | 问题 | 修法 |
| --- | --- | --- |
| P3 | 中性 chip 文字色偏浅 | 改 `DialogTextTertiaryBrush`（`Dialog.Message.xaml:232-236`） |
| P3b | 危险主按钮焦点环是蓝色 | 新增 `DialogMessageDangerFocusVisual`（红，0.45），仅危险主按钮使用；普通按钮仍用 `DialogFocusVisual` |
| P4 | 详情行 / 图标字形 / 关闭按钮外观值散在视图里（R141） | 下沉为 `DialogMessageDetailRow` / `DialogMessageIconGlyph` / `DialogMessageCloseButton` |
| P7 | `Options=null` 时残留上一次的语义 / 宽度 / 默认键 | `ApplyOptions` 显式复位（`MessageDialogView.xaml.cs:55-77`） |
| P8 | 视图创建失败静默退回旧卡片，无法定位断链 | 保留 `Debug.WriteLine`（`EasyDialog.cs:248-255`） |
| P10 | 根容器可被 Tab 聚焦 | `IsTabStop="False"`（`MessageDialogView.xaml:7`） |

剩余问题均为已知偏差或延后项，见 §8 / §10。

---

## 8. 已知偏差

| # | 偏差 | 现状 / 原因 | 处理 |
| --- | --- | --- | --- |
| 1 | 复制按钮圆角 8 vs 设计 6 | `BasedOn FluentButtonStyle` 的圆角为 8 | 接受；如需 6 需在 `DialogMessageCopyButton` 里覆盖 `CornerRadius`（Fluent 模板是否吃该 Setter 需另验） |
| 2 | 按钮 hover / pressed 沿用 Fluent 黑色叠加层（5% / 10%） | 按钮族 `BasedOn FluentButtonStyle` | 接受；设计稿的 Dialog 状态色未复刻 |
| 3 | 危险底栏提示用泛化文案「危险操作需点击或 Tab 到「删除分组」」 | 组件不知道业务动作，只能拼主按钮文案 | 接受；设计稿是「删除需点击…」 |
| 4 | 富文本 / 加粗未支持（D7） | `Message` 是纯 `string` | 危险 lead 里的 `<b>` 片段按纯文本渲染 |
| 5 | 旧 `EasyDialog.ShowAsync(title, message)` 现在统一 Info 图标、480 宽 | 第 3 批有意行为（签名不变，视觉统一） | 接受；调用点零改动 |
| 6 | `Details` 超过 3 行不截断 | 视图不做截断，超过 3 行会把卡片撑高 | 调用方契约，已写 XML 注释（`MessageDialogOptions.cs:93-96`） |
| 7 | recovery 证据图未包含主按钮焦点环 | 渲染宿主是 `Border` 不是 `Window`（`MessageDialogSmoke.cs:783`） | 真实窗口焦点由 danger 场景 `Window` 断言 + `IsDefault` 覆盖 |
| 8 | `GlyphButtonClipCheck` 静态扫描看不到 Style 里的 24 / 30 尺寸 | 判据只扫视图内联尺寸 | 复制 / 关闭按钮由运行期断言覆盖 |
| 9 | 两套蓝 D1 未动 | 既有 `DialogPrimaryButton` `#0078D4` vs 新 `DialogMessagePrimaryButton` `#0EA5E9` | 留独立批次统一 |

---

## 9. 未验证项

1. **真机 DPI**：125% / 150% 下 1px 线、图标块、文字裁切未实测（证据图按 96 DPI 出）。
2. **剪贴板被占用**：失败分支只做了代码审查，未在真机用占用剪贴板的进程复现。
3. **Alt-Tab / 任务栏**：遮罩窗口 `ShowInTaskbar=false` + Owner 跟随的实际表现未验收。
4. **危险场景 Tab + Enter 真按键**：代码契约与默认焦点已断言，但"Tab 到红色主按钮再按 Enter"未用真实键盘事件跑通。
5. **异步路径**：`MessageDialog.ShowAsync` 只断言了签名与返回类型，未在真实应用中走完整异步调用链。
6. **560 宽档**：本轮证据只出了 480 的 recovery / danger 两张；560 只在 API / 静态层覆盖。
7. **长路径省略与 ToolTip**：真机悬停看全路径未验收。
8. **无障碍**：`AutomationProperties.Name` 已挂，但屏幕阅读器 / 高对比度未验证。

---

## 10. 后续批次

1. **剩余 10 处 `MessageBox`**（`VisionMaster\` 工程口径）：`App.xaml.cs:615/617`、`LoadLayoutCommand.cs:19`、`ResetLayoutCommand.cs:16/27/37`、`ImageView.xaml.cs:170/176`、`ScadaAlarmHistoryView.xaml.cs:95/96`。
   （`Core\` / `Plugins\` / `Services\` 里另有 `MessageBox` 调用，未纳入本批清单。）
2. **`EasyDialog` 约 40 处消息调用**：已随引擎改造自动换成新卡片；后续逐处核对文案是否该动作化（「确定」→「保存并继续」这类）。
3. **D1**：统一两套蓝（`DialogPrimaryButton` `#0078D4` vs `DialogMessagePrimaryButton` `#0EA5E9`）。
4. **D2**：自定义内容弹窗（属性表格 / 文本输入）收编到同一卡片壳。
5. **D3**：三按钮「保存 / 不保存 / 取消」（`TertiaryText` + `MessageResult.Tertiary`）。
6. **D6**：详情区滚动（> 3 行）。
7. **D7**：富文本 / 链接（Inline 内容模型）。

---

## 11. 给后来者的口径

1. **不要再造第二个消息弹窗**：对外 `MessageDialog`，对内 `EasyDialog`；需要新语义就扩 `MessageSeverity` / `MessageTone`，不要新建窗口类。
2. **消息卡片不要复用 `DialogChip*`**：`FluentAliases.xaml` 最后合并会把它们转发到 Fluent 令牌；要改 chip 外观改 `DialogMessageChip*`。
3. **关闭按钮不要写 28**：`DialogIconButton` 的 `MinWidth/MinHeight=30` 会夹回；固定格子按钮必须 `Padding=0`（R68）。
4. **旧签名是兼容面**：`ShowAsync(title, message)` / `ShowSync` / `ShowCustomAsync` 的返回类型与调用点不要动；新能力走 `ShowMessageAsync` / `ShowMessageSync`。
5. **危险动作的默认焦点只在没有显式设置时自动落到次按钮**：调用点显式设 `DefaultFocus = Primary` 是"我知道自己在做什么"的覆盖，不要在后来的重构里把它改回去。
