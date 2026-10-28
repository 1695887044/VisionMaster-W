# 流程管理 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | **v1.1（Q1~Q5 已裁定，Q6 已按 A 落地，已复刻）** |
| 日期 | 2026-10-28 |
| 确认日期 / 人 | **2026-10-28 用户裁定 Q1~Q5 + Q6 选 A** |
| 覆盖范围 | `VisionMaster\Views\DialogViews\FlowManagerView.xaml` + `FlowManagerViewModel.cs` + `VisionMaster\Models\InvokeParamEditModel.cs`（1180×620 弹窗，MinWidth 1060） |
| 设计文件 | `design.html`（自包含；A 默认 7 行 / B 说明展开 / C 搜索无结果 / D 最小宽度 1060 四帧，`?view=a..d&shot=1` 可单帧截图） |
| 证据目录 | `evidence/`（设计稿四帧）+ `../app-shell/evidence/flow-manager-v11.png`、`flow-manager-v11-empty.png`（**WPF 真渲染**） |
| 状态 | **已复刻（2026-10-28）**；实现记录 `docs/code-changes/2026-10-28-流程管理v1.1复刻（调用胶囊与未配齐提示）.md`；待用户按 R132 真机验收 |

## 0. 现状与问题（9 条，均带证据）

截图基线：用户提供的真机截图（1180×620，6 条流程，第 4 行被行底裁断）。

| # | 问题 | 证据 | 影响 |
| --- | --- | --- | --- |
| 1 | **行高被撑到 96px** | `FlowManagerView.xaml:239` 的 `WrapPanel`：5 个 `DialogCheckBox`（各约 56px）+ 10px 间距 = 300px，正好等于列宽，实测折成两排；再加 `InvokeSummary`（`:272`）与 `PendingNote`（`:279`）两行 11px 文本 | `42 + 5×96 = 522 > 400`（表格可用高），5 条流程必然滚动；截图里第 4 行沿行底被裁 |
| 2 | **5 个复选框同权重** | `DialogCheckBox`（`Dialog.Selection.xaml:36`）是 15px 方框 + 12 号字，未选/已选只差一个小勾 | 扫一列看不出"这条流程靠什么触发"——这是本表的首要任务 |
| 3 | **「调用关系」列被截断** | 列宽 `150`（`:320`），单元格 padding `12,0,0,0`（`Dialog.DataGrid.xaml:21`）→ 可用 126px；`CallSummaryText` 的「子程序 · 由主流程调用」约 142px → `TextTrimming="CharacterEllipsis"`（`:336`）截成「子程序·由主流程...」 | 这一列存在的意义就是"一眼可见能不能直接拉起"，截断了等于没有 |
| 4 | **互斥动作同时可点** | 「锁定」（`:374`）与「解锁」（`:383`）并排常驻；`ExecuteEncryptFlow`（`FlowManagerViewModel.cs:186`）与 `ExecuteDecryptFlow`（`:216`）各自只判自己的前置条件，点错了才弹「该流程已锁定 / 未锁定」 | 每行都有一个点了必然报错的按钮 |
| 5 | **状态与操作重复表达** | 状态列「✓ 已启用」（`StatusText`，`:355-370`）、操作列「禁用」（`EnabledText`，`:375`） | 用户要跨两列拼出"当前是什么、点了会变什么" |
| 6 | **说明面板 5 行 ≈ 130px** | `:421-457`，5 条 `DialogHintText`；其中第 3 条（`:444`）逐字解释「HTTP / 定时 / 变量 / 子程序 / 整线」 | 说明在复述表格里的勾选框，还挤掉 2 行数据（占弹窗高度 21%） |
| 7 | **骨架流程无标识** | `FlowModel.Role`（`Core\Models\FlowModel.cs:69-82`，Home/Main/End）已存在，视图**完全没用** | "哪三条不能删"只能靠记名字；`MandatoryFlows` 的约束在界面上不可见 |
| 8 | **搜索无空态** | `FilterFlows()`（`FlowManagerViewModel.cs:164-177`）直接 `Flows.Clear()` 再按关键词回填 | 搜不到就是一张空白表，没有任何"没匹配到"的反馈，用户会以为列表加载失败 |
| 9 | **选中行整行浅蓝** | `DialogDataGridRow` 的 `IsSelected` → `DialogAccentLightBrush`（#E0F2FE，`Dialog.DataGrid.xaml:51-53`） | 与「锁定」红字、「子程序」琥珀章抢视线；截图里选中行与交替行几乎分不清 |

## 1. 设计要点

### 1.1 调用方式：5 个复选框 → 5 个可点 chip

```text
改前   ☐ 定时  ☐ 变量  ☑ 子程序  ☐ HTTP        ← 300px 折两排
       ☐ 整线
       定时 / 子程序                            ← InvokeSummary

改后   (定时) (变量) [子程序] (HTTP) (整线)      ← 单排 265px，点一下切换
       仅子程序                                  ← 只在需要时出现
```

- **形态**：22px 高胶囊，`padding:0 9px`，字号 11，圆角 11。未选＝白底 + `DialogBorderBrush` + `DialogTextTertiaryBrush`；已选＝`DialogAccentLightBrush` 底 + `DialogAccentBorderBrush` 边 + `DialogAccentTextBrush` 字 + 600 字重。
- **`整线` 单独用绿**（`DialogSuccessSubtleBrush` / `DialogSuccessBorderBrush` / `DialogSuccessTextBrush`）：它不是"外部触发"，而是"随工具栏整线启动"。四种外部触发用同一种青蓝，第五位用绿——这是本稿唯一的语义分色。
- **宽度账**：定时 45 + 变量 45 + 子程序 56.5 + HTTP 50 + 整线 45 + 4×6 间距 = **265.5**，300px 列（可用 276）**单排放得下**，不再折行。
- **绑定不变**：仍是 `IsTimerInvoke` / `IsVariableInvoke` / `IsSubroutineInvoke` / `IsHttpInvoke` / `IsLineStartInvoke` 五个独立 bool（`FlowManagerViewModel.cs:282-315`），**不改 `FlowInvokeType` 位集、不改 `SetFlag` 的 `Refresh()` 语义**。
- **`InvokeSummary` 删除**：它把 5 个勾选框又拼了一遍（"定时 / 子程序"）。`PendingReasonText` **保留**（`:321-333`）——当前 5 位全接通返回空串，那一行自动收起；将来加位没接通时它是唯一的"勾了没反应"解释。
- **新增「未配齐」提示行**：见 §1.8。勾了「定时」但间隔 ≤0、勾了「变量」但触发源为空时，chips 下方出一行琥珀加粗小字（`DialogWarningTextBrush`，10px，600 字重）。这是本稿**唯一新增的语义**，也是 v1 的漏洞修补。

### 1.2 删除「调用关系」列

这一列的 4 种取值完全由 `InvokeType` 位集派生（`FlowModel.cs:318-361`），是同一份数据的第二个投影：

| `CallBadgeText` | 判据（`FlowModel.cs`） | 改后由谁表达 |
| --- | --- | --- |
| 循环 | `IsDirectlyLoopable` = `Role==Main \|\| IsLineStartMember`（`:292`） | 绿色 `整线` chip；`Role==Main` 另由名称旁角色徽标表达 |
| 外部 | `HasExternalTrigger`（`:303`） | 青蓝 chip（定时 / 变量 / HTTP） |
| 子 | `IsSubroutineOnly`（`:312`） | chips 下补一行琥珀小字「仅子程序」 |
| （空） | 手动 | chips 下补一行灰字「手动」 |

**只有后两种需要补小字**——前两种 chips 已经说清楚了，再加一行等于重复。这样每行最多一行小字，且只在需要提醒时出现。

省下 150px 列宽给名称与描述。

### 1.3 状态列四态

| 态 | 视觉 | 判据（`StatusText` 优先级，`FlowManagerViewModel.cs:355-370`） |
| --- | --- | --- |
| 已锁定 | 红锁图标 + 红字，`DialogDangerSubtleBrush` 底 + `DialogDangerBorderBrush` 边 | `StepsEncrypted`（最高优先） |
| 运行中 | 脉冲绿点 + 绿字 600 字重 | `RunState == Running` |
| 已启用 | 绿点 + `DialogTextSecondaryBrush` | `IsEnabled` |
| 已禁用 | 灰点 + `DialogTextMutedBrush` | 其余 |

**只有异常态（运行中、已锁定）给底色**，正常态只用圆点——这是本列的降噪规则：六行里四行都是「已启用」，给它们上底色等于把注意力从真正的异常上挪开。

### 1.4 操作列按状态收敛

```text
已启用    [ 禁用 ]  [ 🔒 ]
已禁用    [ 启用 ]  [ 🔒 ]     ← 启用是恢复动作，用 DialogAccentSoftBrush 主色
已锁定    [ 禁用 ]  [ 🔓 ]     ← 解锁是恢复动作，danger 语义色
运行中    [ 禁用 ]✕ [ 🔒 ]✕    ← 见 §9 Q1
```

- 启用/禁用保留文字（状态动词，图标表达不了）；
- **锁定/解锁合并成一个图标键**：未锁显示锁形、已锁显示开锁形并转 danger 色。互斥动作不再并排。
- 两条命令、两个 `CommandParameter` 全部保留，只是同一时刻只显示其中一个。

### 1.5 说明折叠

```text
收起（32px）  ⓘ 调用方式 5 位含义 · 锁定与启用的区别 · 骨架流程约束        [展开 ⌄]
展开（128px） 两列 × 三行，左 78px 标签列 + 右正文
```

- 默认**收起**。省下的 98px 全给表格（§2）。
- 展开态 128px，仍比现状的 160px 少 32px —— 因为删掉了"复述勾选框"那条，并把 3 列改成 2 列（列更宽、行更少）。
- 六条文案与现状一一对应（`FlowManagerView.xaml:434-455`），一条没丢。

### 1.6 空态

搜索无结果时：放大镜图标（40px 圆底）+ 「没有匹配「关键词」的流程」+ 一句范围说明 + 「清除搜索」按钮。

> **可达性**：`FilterFlows()` 会 `Flows.Clear()` 后按关键词回填，关键词打错即空表。这条状态**现在就能出现**，只是没有任何视觉表达（R38：跳过型断言必须能被反证——这里不是"跳过"，是"该有却没有"）。

### 1.7 选中态

整行 `DialogAccentLightBrush` → **3px 左侧强调条 + `DialogAccentSoftBrush`（#F0F9FF）**。

`DialogAccentSoftBrush` 比 `DialogAccentLightBrush` 浅一档，选中仍然可辨（左侧条提供硬边界），但不再与「已锁定」红字、「子程序」琥珀字抢同一层注意力。

### 1.8 5 位的运行时归属、并发口径与「未配齐」

> 这一节回答"为什么可以多选、会不会冲突"，以及"定时的时间在哪配"。**结论：多选不冲突，但「定时」和「变量」现在勾了也不会触发，因为没有配置入口。**

**① 每位归谁消费（`FlowModel.cs:135-145` 的 `PendingReason` 注释即领域层口径）**

| 位 | 运行侧消费者 | 触发条件 | 拒绝/跳过行为 |
| --- | --- | --- | --- |
| 定时 | `Engine\FlowTimerScheduler.cs` | `ShouldFire`：`IsEnabled` && 位已勾 && `TimerIntervalMs > 0` && 未锁定 && 距上次 ≥ `ResolveIntervalMs` | 流程在跑 → 跳过（错过的拍**不补跑**） |
| 变量 | `Engine\FlowVariableTriggerService.cs` | `TriggerVariable` 指定的变量**由假变真**（上升沿，`:166`） | 同上，走同一个 `FlowAutoRunner` |
| 子程序 | `FlowInvoker` + `Plugin.RunFlow` | 被其它流程的「调用流程」步骤调用 | 宿主侧查这一位放行 |
| HTTP | `VisionMaster\Services\HttpImageServer.cs:231` | `/flow/{流程名}` 且 `IsHttpCallable()` | 未勾 → **403**；禁用 → 403；不存在 → 404；在跑 → **409** |
| 整线 | `FlowBatchSelector`（工具栏「启动整线」） | 按这一位选流程，各起各的会话 | 一条出问题只停它自己（`:288-292`） |

**② 多选为什么不冲突**

- 5 个位回答的是**"谁能拉起这条流程"**，不是"这条流程怎么跑"。同一条流程既允许 HTTP 触发、又允许被别的流程当子程序调、又随整线启动，是真实需求（`FlowModel.cs:306-316` 明确写了工位流程常同时勾「子程序」+「整线」）。所以落盘是 `[Flags]` 位集，不是单选枚举。
- **同一份流程不会并发跑两遍**：所有入口收口到"这条流程的会话"，会话有锁。
  - 定时/变量 → `FlowAutoRunner.RunOnce` 先查 `session.IsRunning`，在跑就跳过并记 Warn（`FlowAutoRunner.cs:44-49`）；真正执行用 `TryRunSessionOnceAsync`，抢不到锁返回 false 再记一条（`:70-75`）。
  - HTTP → 在跑直接返回 409，请求不受理、不推图（`HttpImageServer.cs:251-254`）。
  - 手动 → 提示"正在运行"。
  - 定时器另有"错过的拍不补跑"（`FlowTimerScheduler.cs:29-30`）：一小时错过 200 拍不会在某一瞬间连跑 200 次。
- **多选的真实后果是"谁先到谁跑，其余被跳过或拒绝"，不是排队也不是并发。** 这是刻意的口径，写在 `FlowAutoRunner` 类注释第 10 行。
- **跨流程是真并行**：每条流程一个独立会话，`LineStart` 的注释（`FlowModel.cs:288-292`）明确"各起各的会话"。多选不会让一条流程跑两遍，但会让多条流程同时在跑——这是整线场景要的形态。

**③ 定时的时间在哪配：现在没有入口**

| 字段 | 定义 | 谁读 | 谁写 | 真实方案取值 |
| --- | --- | --- | --- | --- |
| `TimerIntervalMs` | `FlowModel.cs:386` | `FlowTimerScheduler` | **无生产代码** | 10 条流程**全是 0** |
| `TriggerVariable` | `FlowModel.cs:377` | `FlowVariableTriggerService:160`、`RecipeService` | **无生产代码** | 全为空串 |

- 全仓 XAML **没有任何** `TimerIntervalMs` 绑定；流程右键菜单 11 项（`FlowListViewModel.cs:292-453`：Create / Delete / Rename / EditComment / ToggleEnabled / BatchDisplayWindow / Compile / Manager / RunOnce / RunContinuous / Stop / VariableEvents）里也没有「定时设置」。
- 而 `ShouldFire`（`FlowTimerScheduler.cs:169-177`）对 `TimerIntervalMs <= 0` **直接返回 false**——注释写得很直白：*"0 = 没配间隔，不是'每 100ms'"*。所以**勾了「定时」但没配间隔 = 永不触发**。
- `EvaluateCore:160` 用 `string.Equals(flow.TriggerVariable?.Trim(), variableName)`，空串/`null` 永不匹配任何变量名——**勾了「变量」但没选触发源 = 永不触发**。
- **`PendingReason` 帮不上忙**：它回答"这一位有没有接通运行侧"（5 位全返回 `null` = 都接通了），不回答"这一条流程有没有配齐参数"。所以界面现在既不触发也不提示，正是这套机制想防的"勾了没反应 = 软件坏了"。

**④ 本稿取法**

新增派生属性（暂名 `InvokeConfigNote`，落在 `FlowItem` 上，不改 `PendingReason` 语义）：

| 条件 | 文案 | 样式 |
| --- | --- | --- |
| 勾了定时 && `TimerIntervalMs <= 0` && 勾了变量 && `TriggerVariable` 空 | `定时未设间隔 · 变量未选触发源，不会触发` | `DialogWarningTextBrush` + 600 |
| 只缺一项 | `定时未设间隔，不会触发` / `变量未选触发源，不会触发` | 同上 |
| 都不缺 | 空串 → 整行收起 | — |

`design.html` 第 6 行「点胶检测_工位」就是这条的证据行。

**⑤ 仍待你选：配置入口**

| 方案 | 形态 | 代价 |
| --- | --- | --- |
| **A** | chip 旁出一个齿轮，点开小窗就地配间隔 / 触发变量 | 改动最小；需 2 个新弹窗（或 1 个复用的小参数窗） |
| **B** | 整行双击 → 「流程属性」弹窗，5 位 + 两个参数 + 描述一起改 | 与 R25「双击出参数窗」的既有习惯一致，但要新 VM + 新视图 |
| **C** | 本版只做 ④ 的提示，配置入口另开一期 | 用户能看见"为什么没跑"，但仍要去改 `.vms` 文件 |

> 另注：`FlowModel.cs:139` 与 `FlowManagerView.xaml:211` 的注释写「下限 100ms」，实际常量是 `FlowTimerScheduler.MinIntervalMs = 20`（`:35`），界面 ToolTip 与底部说明也都写 20ms。**两处注释是陈旧的**，复刻时顺手改掉。

## 2. 尺寸账（弹窗 1180×620 不变）

| 区段 | 改前 | 改后 | 依据 |
| --- | --- | --- | --- |
| 标题栏 | 52 | 52 | `:65`，不动 |
| 工具栏 | 56（16+28+12） | 56 | `:129`，不动 |
| 说明区 | 160（14+130+16） | **62**（14+32+16） | §1.5 |
| 表格可用 | 400 | **438** | 620 − 12（非客户区）− 52 − 56 − 说明 |
| 表头 | 42 | **36** | `DialogDataGridColumnHeader` 的 42 是本表专属覆盖，可降 |
| 单行 | 96 | **56** | 22（chip）+ 4 + 13（小字）+ 8×2（单元格内边距） |
| 可见行数 | 3.7 / 5 | **7 / 7** | `36 + 7×56 = 428 ≤ 438` |

改前 5 条流程需 `42 + 5×96 = 522 > 400` → 必然滚动（截图第 4 行被裁就是这个）；改后 **7 条**一次看全（真实方案 10 条，只多 1 行滚动）。

**窄档 1060**（MinWidth）：内容 1048，表格扣左右边距 40 = 1008。固定列 320 + 120 + 200 = 640，剩 368 分给名称（下限 180）与描述（1.3fr）→ 180 / 188。chip 仍单排（265 ≤ 276），描述按 `CharacterEllipsis` 截断。见 `evidence/design-d-narrow.png`。

## 3. 令牌映射

**颜色：0 新增。** 全部取自 `UI\Controls\Themes\Colors.xaml` 既有 `Dialog*` 与逻辑容器族。

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--accent` | `#0EA5E9` | `DialogAccentBrush` |
| `--accent-dark` | `#0284C7` | `DialogAccentDarkBrush` |
| `--accent-light` | `#E0F2FE` | `DialogAccentLightBrush` |
| `--accent-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--accent-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--accent-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--bg` | `#EEF2F7` | `DialogBackgroundBrush` |
| `--border` | `#E2E8F0` | `DialogBorderBrush` |
| `--border-light` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` |
| `--text-1` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--text-3` | `#64748B` | `DialogTextTertiaryBrush` |
| `--text-4` | `#94A3B8` | `DialogTextMutedBrush` |
| `--row-hover` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--success` / `-text` / `-soft` / `-border` | `#22C55E` / `#15803D` / `#F0FDF4` / `#BBF7D0` | `DialogSuccessBrush` / `DialogSuccessTextBrush` / `DialogSuccessSubtleBrush` / `DialogSuccessBorderBrush` |
| `--danger` / `-text` / `-soft` / `-border` | `#EF4444` / `#B91C1C` / `#FEF2F2` / `#FECACA` | `DialogDangerBrush` / `DialogDangerTextBrush` / `DialogDangerSubtleBrush` / `DialogDangerBorderBrush` |
| `--warn-text` | `#B45309` | `DialogWarningTextBrush` |

**角色徽标（复用逻辑容器族，不新增键）**：

| 角色 | 文字 | 字 / 底 / 边 | 复用键 |
| --- | --- | --- | --- |
| 回零 Home | `回零` | `#6D28D9` / `#F5F3FF` / `#DDD6FE` | `LogicCaseTextBrush` / `LogicCaseSoftBrush` / `LogicCaseBorderBrush` |
| 主 Main | `主` | `#4338CA` / `#EEF2FF` / `#C7D2FE` | `LogicIfTextBrush` / `LogicIfSoftBrush` / `LogicIfBorderBrush` |
| 收尾 End | `收尾` | `#B45309` / `#FFFBEB` / `#FDE68A` | `LogicWhileTextBrush` / `LogicWhileSoftBrush` / `LogicWhileBorderBrush` |

**样式键**：

| 设计元素 | 落点 | 说明 |
| --- | --- | --- |
| 窗口壳 / 标题栏 / 工具栏 / 搜索框 | 复用现状 | `ui:WindowDragBehavior`、`DialogSearchChrome` / `DialogSearchIcon` / `DialogSearchInput`（`:147-166`）全部不动 |
| 表格 | `DialogDataGrid` | 视图上覆盖 `RowHeight=56`；`ColumnHeaderStyle` 需 42→36（建议在 `Dialog.DataGrid.xaml` 派生 `DialogDataGridColumnHeaderCompact`，本表专用） |
| chip | **新增 `DialogInvokeChip`** | 建议落在 `Dialog.Selection.xaml`（与 `DialogCheckBox` 同册），用 `CheckBox` 换模板而非 `ToggleButton`（见 §9 Q4）。已全仓查重：`InvokeChip` 无同名键 |
| 行内文字键 | `DialogChipButton` | 现状已用（`:378`），保留 |
| 行内图标键 | `DialogIconButton` | 30×30 方形（`FluentAliases.xaml:56-69`），本表需 24×24 派生档 |
| 说明折叠条 | `DialogInfoPanel` 派生 | 现 Padding 14,12；收起态改 12,0 单行高 32 |

**字重**：`SemiBold` = 600；chip 已选、行名、状态「运行中」用 600，其余 400。

## 4. 复刻约束

1. **不改数据契约**：`Flows` / `SelectedFlow` / `SearchText` 绑定保留；`EncryptFlowCommand` / `DecryptFlowCommand` / `ToggleEnabledCommand` / `CloseCommand` 四个命令与 `CommandParameter` 全部保留。
2. **不改 `FlowModel`**：`InvokeType` / `IsEnabled` / `StepsEncrypted` / `RunState` / `Role` 只读消费。`CallSummaryText` / `CallBadgeText` 在删列后**不再被本视图使用**，但**不删**（`FlowListViewModel` 等其它消费者可能引用，删前必须 `grep`）。
3. **`Role` 的中文显示名需要新增映射**：现状只有 `role.ToString()`。按 `AGENTS.md` R40，枚举上屏必须走显示名——建议在 `FlowRole` 上加 `[Display(Name="回零")]` 等特性，视图走 `StepConfigOption.ForEnum` 同族写法，不在 XAML 里写死中文。
4. **`PendingReasonText` 的收起触发器位置不变**：`DataTrigger` 必须留在 `DataTemplate.Triggers` 里（`:288-292` 的既有注释：`ControlTemplate.Triggers` 对模板内元素静默失效）。
5. **chip 的 `IsChecked` 只许两态**：`IsThreeState` 必须为 `false`；`DialogInvokeChip` 模板里不许出现 `IsChecked="{x:Null}"` 相关触发器。
6. **`RowHeight` 写法**：`56`（固定值）。**不要**写 `Auto`——`DataGrid.RowHeight` 是 `double`，XAML 写 `"Auto"` 能过 BAML 编译但构造视图时抛 `ArgumentException`（`:184-194` 既有踩坑记录）。
7. **`hex=0`**：现状 `FlowManagerView.xaml` 已无颜色字面量，改版后必须仍为 0（R140）。
8. **不造死按钮**：不加"复制流程""导出流程""批量启用"等新命令。工具栏不加筛选 chip（需新 VM 属性 + 新筛选语义，见 §8）。

## 5. 状态矩阵（R115 逐控件）

| 控件 | Normal | Hover | Pressed | Focus | Disabled |
| --- | --- | --- | --- | --- | --- |
| chip（未选） | 白底 + `DialogBorderBrush` + `DialogTextTertiaryBrush` | `DialogAccentSoftBrush` 底 + `DialogAccentBorderBrush` 边 + `DialogAccentTextBrush` 字 | 同 Hover（再深一档可选） | 2px `DialogAccentBrush` 外环，`outline-offset:1` | 不适用（5 位始终可改） |
| chip（已选·外部） | `DialogAccentLightBrush` + `DialogAccentBorderBrush` + `DialogAccentTextBrush` + 600 | 底色加深一档 | 同 Hover | 同上 | 同上 |
| chip（已选·整线） | `DialogSuccessSubtleBrush` + `DialogSuccessBorderBrush` + `DialogSuccessTextBrush` + 600 | 底色加深一档 | 同 Hover | 同上 | 同上 |
| 行 | 白底 / 交替 `#FCFDFE` | `DialogRowHoverBrush` | — | — | — |
| 行（选中） | `DialogAccentSoftBrush` + 左侧 3px `DialogAccentBrush` | 略深 | — | — | — |
| 行（已禁用） | 名称删除线 + `DialogTextTertiaryBrush` | 同普通行 | — | — | — |
| 文字键（禁用/启用） | 白底 + `DialogBorderBrush` | `DialogRowHoverBrush` + `DialogBorderStrongBrush` | 系统默认 | 2px 外环 | 40% 不透明 + `DialogSurface2` 底 |
| 图标键（锁） | 白底 + `DialogBorderBrush` | `DialogRowHoverBrush` | 系统默认 | 2px 外环 | 同上 |
| 图标键（已锁·解锁） | `DialogDangerSubtleBrush` + `DialogDangerBorderBrush` + `DialogDangerTextBrush` | 底色加深 | 系统默认 | 2px 外环 | 同上 |
| 搜索框 | `DialogBorderBrush` | — | — | `DialogAccentBorderBrush` + 3px `rgba(14,165,233,.12)` 环 | — |
| 说明折叠条「展开」 | 透明 | `DialogAccentLightBrush` | — | 2px 外环 | — |

**键盘**：chip 必须是可 Tab 到的独立焦点点（5 个/行）；行选中与 chip 切换不冲突（chip 在行内，点击不改变选中行 —— 现状 VM 无行选中语义，本稿只做视觉选中）。

## 6. 三态（R116）

| 态 | 是否适用 | 依据 |
| --- | --- | --- |
| 加载 | **不适用** | `Flows` 来自 `WorkspaceContext.CurrentSolution.Flows`，内存集合同步可得，无异步阶段（`FlowManagerViewModel.cs:115-127`） |
| 空（无流程） | **不适用** | `SolutionModel` 预置 Home/Main/End 三条骨架，`MandatoryFlows` 加载时补齐且禁止删除 → `Flows.Count ≥ 3` |
| 空（搜索无结果） | **适用，本版新增** | `FilterFlows()` 会 `Flows.Clear()` 后按关键词回填，打错关键词即空表（§1.6） |
| 错误 | **不适用** | 集合读取不会失败；单条流程的运行错误由状态列与 ToolTip 表达，不是列表级错误态 |

> 三条"跳过"都能被反证（内存集合 / 骨架流程约束 / 无 I/O），符合 R38。

## 7. 已知偏差（Web vs WPF，预判）

1. **字体度量**：Web 用 Segoe UI Variable；WPF 用微软雅黑。11px chip 里「子程序」三字在 WPF 下约宽 1~2px，265.5 → 约 268，300 列仍单排。
2. **chip 圆角**：Web `border-radius:11px`（半高）；WPF `CornerRadius="11"` 需 `SnapsToDevicePixels="True"` 避免半像素模糊。
3. **脉冲动画**：Web 用 `@keyframes`（1.4s，opacity 1→0.35）；WPF 用 `Storyboard` + `DoubleAnimation`，同周期，`RepeatBehavior="Forever"`。运行态圆环的 `box-shadow` 在 WPF 用外层 `Ellipse` + `Opacity`。
4. **选中行左侧条**：Web 用 `box-shadow: inset 3px 0 0`；WPF 用行模板里一层 `Border` 的 `BorderThickness="3,0,0,0"`（不能用 `DataGridRow` 的 `BorderThickness`，会被 `DialogDataGridRow` 的 `BorderThickness=0` 覆盖）。
5. **滚动条**：Web 10px；WPF 约 17px。1180 宽下表格可用宽度会再少 7px，仍在余量内。
6. **删除线**：WPF `TextDecorations="Strikethrough"` 的线位置与 CSS 略有差异。
7. **说明展开动效**：Web 用 `transition` 0.18s；WPF 若做展开动画需 `GridLengthAnimation`（框架无内置），建议**直接切换 Visibility 不做动画**——这与 R116「动效 150~200ms」的偏差在此登记。

## 8. 未纳入本版

| 项 | 原因 |
| --- | --- |
| 工具栏筛选 chip（全部 / 已启用 / 已锁定 / 子程序） | 需新增 VM 筛选属性与"筛选 + 搜索"的复合语义；流程数通常 < 10，收益不明显 |
| 批量启用 / 批量锁定 | 需新命令与多选语义（现 `SelectionMode=Single`） |
| 行内直接改描述 / 改名 | 需要写回 `FlowModel` + 撤销栈 + 与流程栏同步，属功能变更 |
| 定时间隔 / 触发变量的**编辑入口** | **勘误（v1.1）**：原写"入口在流程栏右键菜单与属性面板"是错的——两处都没有（见 §1.8）。本版只做「未配齐」提示，配置入口是 §9.2 的 Q6 |
| 锁定/解锁的二次确认 | 锁定是可逆操作（`StepsEncrypted` 可清），加确认会拖慢批量整理；若现场要求再议 |
| 深色主题 | 本仓现无运行时主题切换（R140 边界①） |

## 9. 裁定记录

### 9.1 已裁定（2026-10-28，用户）

| # | 问题 | 裁定 | 落地要求 |
| --- | --- | --- | --- |
| **Q1** | 运行中是否禁用「锁定 / 禁用」 | **要，补上对应的行为** | 两条命令的 `CanExecute` 加 `RunState != Running`；`ExecuteEncryptFlow`（`:182`）/ `ExecuteToggleEnabled`（`:239`）执行体内**再判一次**（按下前判 + 执行前判，同 R136 权限闸门口径）。ToolTip「运行中不可变更，请先停止」。**属行为变更**，须在 `docs/code-changes/` 记录 |
| **Q2** | 说明面板默认收起 | **默认收起** | 32px 一行目录，展开 128px（§1.5） |
| **Q3** | 删除「调用关系」列 | **按推荐删** | 6 列 → 5 列，信息并入 chips + 角色徽标（§1.2） |
| **Q4** | chip 的控件语义 | **按推荐用 `CheckBox` 换模板** | `IsThreeState` 必须为 `false`；模板里不出现 `{x:Null}` 触发器 |
| **Q5** | 角色徽标文案 | **可以** | `回零 / 主 / 收尾` 落 `FlowRole` 的 `[Display(Name=…)]`（R40），视图走 `StepConfigOption.ForEnum` 同族写法 |

### 9.2 已落地：Q6（定时 / 变量 勾了也不会触发）→ 选 A

**问题**：`TimerIntervalMs`（`FlowModel.cs:386`）与 `TriggerVariable`（`:377`）**都没有 UI 入口**，真实方案取值全是 0 / 空串；而 `ShouldFire` 把 0 判成"没配间隔"直接不触发、`EvaluateCore` 的空串永不匹配变量名。详见 §1.8。

**用户裁定（2026-10-28）：A —— chip 旁齿轮 → 就地小窗配间隔 / 触发变量。**

落地形态（与设计稿的差异，复刻后补记）：

| 项 | 设计稿 | 落地 | 原因 |
| --- | --- | --- | --- |
| 小窗载体 | 未指定 | `EasyDialog.ShowPropertyGridSync` + `InvokeParamEditModel` | 与「并行分组参数」「批量设置显示窗口」同一条路（R26）：弹窗自己建窗口、自己收口成 bool，不需要新 Prism 视图注册名、不需要新 XAML |
| 齿轮位置 | 未画 | 胶囊行尾（勾了定时或变量才出现） | 与胶囊同一条基线，不额外占行 |
| 调用方式列宽 | 320 | **330** | 5 胶囊 228 + 4×6 间距 + 齿轮 22 + 6 = 约 280，320 列可用 296 太紧；330 留 26px 余量 |
| 生效说明 | 未画 | 小窗内「当前调用方式 / 生效情况 / 可选变量」三条只读 | 当场说清"填了也不生效"，而不是等用户发现流程不动 |

**未做**：触发变量的候选下拉（目前只在说明区列出前 8 个变量名）。真正的候选下拉要接 `IPluginConfigContextProvider` 同族设施，另开一期。

## 12. 复刻记录（2026-10-28）

| 项 | 内容 |
| --- | --- |
| 改动文件 | `Core\Models\FlowModel.cs`（`FlowRole` 加 `[Display]`）、`UI\Controls\Themes\Dialog\Dialog.Selection.xaml`（+3 键）、`Dialog.DataGrid.xaml`（+1 键）、`FlowManagerViewModel.cs`（重写）、`Models\InvokeParamEditModel.cs`（新建）、`Views\DialogViews\FlowManagerView.xaml`（重写） |
| 断言 | `FlowCanvasChecks\FlowManagerChecks.cs`（新建，`[FM1]~[FM6]` 25 条）；`FlowCanvasChecks` **4337 通过 / 0 失败** |
| 真渲染 | `../app-shell/evidence/flow-manager-v11.png`（5 行示例，含未配齐提示行）+ `flow-manager-v11-empty.png`（搜索无结果） |
| 闸门 | `tools\run_gates.ps1 -SkipBuild` **16/16 全绿**（FlowCanvasChecks pass=4337 fail=0）。全量重建那轮 15/16：唯一红是 FlowCanvasChecks 的**构建**在 `copy → Modules` 上退出 1——用户正开着 `VisionMaster.exe` 锁住 `Modules\`，非代码失败 |
| 令牌 | 颜色 **0 新增**；新增 4 个样式键：`DialogInvokeChipTemplate` / `DialogInvokeChip` / `DialogInvokeChipLine` / `DialogDataGridColumnHeaderCompact`（已全仓查重无同名） |
| 实现记录 | `docs/code-changes/2026-10-28-流程管理v1.1复刻（调用胶囊与未配齐提示）.md`（含 6 条踩坑） |

**复刻时发现的两处与设计稿的偏差**（已按实现落地，回写于此）：

1. **「仅子程序 / 手动」需要独立的派生小字。** 删掉「调用关系」列后，这两种情况没有别的表达。落地拆成两个属性：
   `InvokeConfigNote`（琥珀加粗，管"缺参数"）+ `DerivedNote`（中性灰，管"自己不会跑起来"），
   两者互斥让位，同一时刻只出一行——否则行高会被撑成两行，破坏 56px 的账。
2. **图标字体必须用 `DialogIconFont`，不能用 App 级的 `Icon`。** 两者指向同一个 otf，
   但探针宿主里 App 级 `FontFamily` 解析不到，整排图标渲染成豆腐块 □（截图证据见实现记录 §5 第 2 条）。

## 13. 变更规则

## 10. 验证计划（复刻后）

| 层 | 判据 |
| --- | --- |
| 静态 | `FlowManagerView.xaml` 的 `hex=0`；4 个命令 + `CommandParameter` 齐全；`PendingReasonText` 的 `DataTrigger` 仍在 `DataTemplate.Triggers` 内；`RowHeight="56"` 不是 `"Auto"`；`InvokeConfigNote` 的 `DataTrigger` 同样在 `DataTemplate.Triggers` 内 |
| 运行时 | 7 条流程在 1180×620 下**全部可见不滚动**；chip 单排不折行；窄档 1060 下 chip 仍单排 |
| 功能回环 | 5 个 chip 逐个点击 → `InvokeType` 位集正确翻转且 `Refresh()` 触发；锁定/解锁图标形态随 `StepsEncrypted` 切换；启用/禁用文字随 `IsEnabled` 切换；**运行中两条命令 `CanExecute == false`（Q1）**；搜索打错关键词出空态、点「清除搜索」恢复 |
| 未配齐提示 | 勾「定时」且 `TimerIntervalMs = 0` → 出「定时未设间隔，不会触发」；填上间隔后提示消失；勾「变量」且 `TriggerVariable` 空 → 出「变量未选触发源，不会触发」；两项都缺 → 合并成一句 |
| 视觉 | 四态 PNG：默认（7 行，含未配齐提示行）/ 说明展开 / 搜索无结果 / 1060 窄档（对照 `evidence/design-*.png`） |
| 回归 | `UIThemeSmokeTest` 0 FAIL；`run_gates.ps1` 全绿 |

## 11. 变更规则

确认后只读。需要调整时升 `v2`，并同步：`design.html`、本 README 版本号、令牌映射表、`docs/UI设计基线/README.md` 索引行。
