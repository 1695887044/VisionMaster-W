# 流程胶囊条 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | **v2**（v1 因尺寸与宿主冲突作废，见 §0） |
| 日期 | 2026-10-21 |
| 确认日期 / 人 | 待用户确认 |
| 覆盖范围 | `VisionMaster\Views\FlowListView.xaml`（流程导航顶部的流程胶囊条 + 底部统计 + 右键菜单） |
| 设计文件 | `design.html`（自包含；含默认态 / 超量滚动 / 禁用提示 / 右键菜单） |
| 证据目录 | `evidence/`（待创建；评审后补渲染 PNG） |
| 状态 | **用户已确认（2026-10-22）→ 已复刻**；落地记录 `docs/code-changes/2026-10-22-第1批四视图Web化复刻（日志监视流程条驾驶舱）.md` |

## 0. v1 为什么作废（必读）

v1 把设计画布做成 **380×260**、胶囊做成两行式（`min-height: 46px`）。审查核实宿主是**硬约束**：

| 约束 | 证据 |
| --- | --- |
| `FlowListView` 在流程导航里**固定 112px 高** | `VisionMaster\Views\ProcessView.xaml:290`：`<local:FlowListView Grid.Row="0" Height="112" />` |
| 左栏宽度 **340**（最小 320） | `VisionMaster\Shell.xaml:1052`：`DockWidth="340" DockMinWidth="320"` |

按 v1 的尺寸算：6 条流程 2 列 = 3 行 × 46 + 间距 ≈ 150px，加工具条内边距与 28px 底栏 ≈ **190px**，装进 112px 必然裁切。**v2 改为单行紧凑式（22px 胶囊）**，并在设计稿上直接按 340×112 出图。

> 附带更正：v1 的空态「还没有流程」**在真实模型里不可达**。`Core\Models\SolutionModel.cs:107-113` 预置 Home/Main/End 三条骨架流程，`Core\Models\MandatoryFlows.cs` 在加载时保证补齐，且带 `Role` 的流程禁止删除（`FlowListViewModel.cs:153-156`）——流程数恒 ≥ 3。v2 删除该空态。

## 1. 现状

`FlowListView.xaml` 已在 `app-shell` v2 中被结构性提及（"流程胶囊条"），但**从未单独出过设计稿**。当前实现 223 行、`hex=0`（已令牌化），问题不在颜色而在**信息密度与语义表达**：

| # | 问题 | 证据 | 影响 |
| --- | --- | --- | --- |
| 1 | 状态点兼任"启用/运行"两义（现已拆分） | `FlowListView.xaml:76-83` 绿点 + `:193-217` 的 `RunState` 触发器 | 绿点到底是"能跑"还是"正在跑"，用户无法区分 |
| 2 | 角色（Home/Main/End）无视觉表达 | `FlowModel.Role` 已存在（`Core\Models\FlowModel.cs:59-72`），视图未用 | 三条骨架流程与普通流程长得一样 |
| 3 | 步数用 9.5px + `Consolas` | `:97-100` | 低于可读底线（`scada-controls` D7 同族问题） |
| 4 | "禁用"标签 9px 红字，与"运行中"同格 | `:106-108`、`:110-122` | 两个语义塞一个位置，切换时视觉跳 |
| 5 | 选中禁用流程无提示 | 现状只有胶囊本身变灰 | 用户选中后不知道为何运行按钮没反应 |
| 6 | 2 列固定网格，窄栏下名称截断 | `:29`（`UniformGrid Columns="2"`） | 左栏 340 时单格约 160px |

## 2. 设计要点

### 2.1 单行胶囊（112px 高度决定的唯一解）

```text
┌──────────────────────────────────────────────┐
│ ● 回原点 [零]  6   ● 主检测 [主] ◉ 18        │  22px
│ ● 定位检测 ◉ 11   ● 缺陷复判  9              │  22px
│ ○ 数据上报  4     ● 收尾 [尾]  5             │  22px
├──────────────────────────────────────────────┤
│ 6 条流程                        选中：主检测  │  26px
└──────────────────────────────────────────────┘
```

高度账（border-box 112 − 上下边框 2 = 内容 110）：`6（上内距）+ 22×3（三行）+ 4×2（行距）+ 26（底栏）= 106 ≤ 110`。

- **22px 单行**：状态点（6px）+ 名称（11.5px）+ 角色徽标（9px）+ 步数（10px 等宽）。所有元素在一条水平线上。
- **步数只显示数字**（`6` 而不是 `6 步`）：22px 行高里"步"字是纯噪声，列头与 ToolTip 已说明含义。
- **角色徽标压到一个字**：`零` / `主` / `尾`，完整名称进 ToolTip（`title="回零（Home）"`）。这是 112px 下的必要压缩——v1 的"回零/主/收尾"三字徽标在 22px 行里放不下。
- **运行标记压成 6px 脉冲点**（放在步数前），不用文字：单行式没有第二行放「运行中」三个字。语义靠颜色（绿=运行、蓝=暂停）+ ToolTip。

### 2.2 五种状态

| 状态 | 状态点 | 名称 | 运行标记 | 胶囊底 |
| --- | --- | --- | --- | --- |
| 普通启用 | 绿 | 常规 | — | 白底 + 1px 描边 |
| 选中 | 绿 | **600 字重** | — | `--selected-soft` + 靛蓝描边 |
| 运行中 | 绿 | 600 字重 | **绿色脉冲点** | 白底（选中则选中底） |
| 已暂停 | **主色** | 600 字重 | **主色脉冲点** | 同上 |
| 禁用 | **灰** | 删除线 + 灰 | **强制隐藏** | `--surface-2` |

关键规则：**禁用态强制隐藏运行标记**（`.capsule[data-enabled="false"] .run { display: none !important }`）。禁用流程不可能在跑，两者同时出现只会让用户怀疑状态源坏了（现状注释已记录这条判断，设计稿把它视觉化）。

**禁用流程的胶囊仍然可以选中**——用户需要选中它才能右键启用/删除。因此禁用态**不是** Disabled 态，它只是"视觉去饱和"，交互照常（可 hover、可 focus、可选中）。

### 2.3 超量滚动

流程超过 6 条时，胶囊区滚动（`max-height: 74px`），**底栏固定不动**。

现状是 `UniformGrid` 直接撑高，在 112px 固定高里会裁掉底部行。滚动档保证"无论多少条流程，底栏统计始终可见"。

### 2.4 选中禁用流程 → 提示条

```text
⚠ 已禁用：右键「启用」后才能运行
```

- 19px 高、10px 字、红底红字 + `⚠`；完整文案进 ToolTip（`已禁用：不参与自动触发，右键「启用」后才能运行`）。
- 出现时胶囊区从 74px 压到 **48px**（正好两行：22+4+22）并滚动。高度账：`6 + 48 + 24（提示条含外边距）+ 26 = 104 ≤ 110`。
- 现状没有这个提示，用户选中禁用流程后按"运行"没反应只能猜。

> **文案必须与代码行为一致**：`FlowListViewModel.cs:278-282` 对禁用流程是**直接拦截**——
> `if (!flow.IsEnabled) { Notifier.ShowWarning($"流程 [{flow.FlowName}] 已禁用，请先启用再运行"); return; }`；
> 编译同样跳过（`:218-222`），HTTP 触发也拦（`HttpImageServer.cs:228`）。
> v1 稿写的"手动运行仍可用"是**错的**，会把用户引向"反复点运行、不知道为什么没反应"。这条提示的唯一价值就是解释"为什么按运行没反应"，解释错了等于没写。

### 2.5 底部统计

```text
6 条流程                          选中：主检测
```

- 左侧：流程条数（数字等宽加粗）。
- 右侧：当前选中流程名。
- **只显示条数，不显示"总步数"**：跨流程求和需要新属性 + 订阅每条流程的 `Steps` 增删（`AGENTS.md` §31/R140 的通知义务），而 `ProcessView` 自己的底栏已有"共 N 步 · 最深 M 层"（`:1367-1371`，当前流程）。两个"步数"同屏会让人分不清是"当前流程"还是"全部流程"。

### 2.6 右键菜单

结构**保持现状**（`FlowListView.xaml:123-189`）：一个「流程 ▸」子菜单（添加/删除/重命名/编辑注释/禁用/编译/运行一次/循环运行/停止）+ 「流程管理」+「变量事件」两项平铺。

- 200px 宽、每项 27px、图标列 13px。
- 子菜单在稿子里画成**展开态**（左缩进 + 左发丝线），便于评审看清 9 项内容；实际交互仍是 `ContextMenu` 悬浮子菜单。
- 「删除」标红并留在子菜单内（与现状一致，不额外提升到顶层）。

### 2.7 没有空态

**流程数恒 ≥ 3，`Flows.Count == 0` 不可达**（证据见 §0）。因此本设计**不画空态**——画一个永远不会出现的状态，等于给复刻者增加一段永远测不到的死代码。

## 3. 令牌映射

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--accent` | `#0EA5E9` | `DialogAccentBrush` |
| `--accent-dark` | `#0284C7` | `DialogAccentDarkBrush` |
| `--accent-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--accent-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--background` | `#EEF2F7` | `DialogBackgroundBrush`（**设计稿外壳，不进视图**） |
| `--border` | `#E2E8F0` | `DialogBorderBrush` |
| `--border-light` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` |
| `--text-1` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--text-3` | `#64748B` | `DialogTextTertiaryBrush` |
| `--text-4` | `#94A3B8` | `DialogTextMutedBrush` |
| `--success` | `#22C55E` | `FlowCanvasSuccessBrush`（现状已用此键） |
| `--success-text` | `#15803D` | `DialogSuccessTextBrush` |
| `--danger-text` | `#B91C1C` | `DialogDangerTextBrush` |
| `--danger-subtle` | `#FEF2F2` | `DialogDangerSubtleBrush` |
| `--danger-border` | `#FECACA` | `DialogDangerBorderBrush` |
| `--disabled` | `#C0C4CC` | `ScadaDisabledTextBrush`（现状已用） |
| `--row-hover` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--selected-soft` | `#EEF2FF` | `LogicIfSoftBrush`（现状已用） |
| `--selected-border` | `#C7D2FE` | `LogicIfBorderBrush`（现状已用） |
| `--selected-text` | `#4338CA` | `LogicIfTextBrush` |

**角色徽标（复用逻辑容器族，不新增键）**：

| 角色 | 文字 | 底 | 描边 | 复用键 |
| --- | --- | --- | --- | --- |
| 回零 Home | `#6D28D9` | `#F5F3FF` | `#DDD6FE` | `LogicCaseTextBrush` / `LogicCaseSoftBrush` / `LogicCaseBorderBrush` |
| 主 Main | `#4338CA` | `#EEF2FF` | `#C7D2FE` | `LogicIfTextBrush` / `LogicIfSoftBrush` / `LogicIfBorderBrush` |
| 收尾 End | `#B45309` | `#FFFBEB` | `#FDE68A` | `LogicWhileTextBrush` / `LogicWhileSoftBrush` / `LogicWhileBorderBrush` |

**不需要新增任何令牌。** 全部 9 个逻辑族键与 20 个 Dialog 键都在 `Colors.xaml` 里已存在。

**半透明派生色**：运行标记的 `box-shadow: 0 0 0 2px rgba(34,197,94,.22)` 与 `rgba(14,165,233,.22)` 是状态色的 alpha 变体。WPF 落地：用 `SolidColorBrush` 带 alpha（`#38` 前缀）或外层 `Border` 的 `Opacity=".22"`。

## 4. 复刻约束

1. **不改数据契约**：`ItemsSource="{Binding Workspace.CurrentSolution.Flows}"`、`SelectedItem="{Binding SelectFlow}"` 保留；`FlowCommand` 的 11 个 `CommandParameter`（`Create`/`Delete`/`Rename`/`EditComment`/`ToggleEnabled`/`Compile`/`RunOnce`/`RunContinuous`/`Stop`/`Manager`/`VariableEvents`）全部保留。
2. **不改 `FlowModel`**：`FlowName` / `Description` / `IsEnabled` / `RunState` / `Steps.Count` / `Role` 只读消费。
3. **`Role` 的中文显示名需要新增映射**：现状只有 `role.ToString()`（`FlowListViewModel.cs:155`），设计稿的 `零 / 主 / 尾` 是全仓不存在的新文案。按 `AGENTS.md` R40 口径，**枚举上屏必须走显示名**——建议在 `FlowRole` 上加 `[Display(Name="回零")]` 等特性，视图侧走 `StepConfigOption.ForEnum` 同族写法，而不是在 XAML 里写死中文。
4. **`RunState` 触发器位置不变**：现状注释明确"`ControlTemplate.Triggers` 里的 `DataTrigger` 对模板内元素静默失效"，运行态触发器必须继续写在 `DataTemplate.Triggers` 里。
5. **右键菜单绑定不变**：`BindingProxy`（`MyViewModelProxy`）+ `PlacementTarget.DataContext` 的靶点机制不动；`FlowToggleHeader` 的动态文案保留。
6. **不改宿主高度**：`ProcessView.xaml:290` 的 `Height="112"` 本版不动。若将来要放两行式胶囊，必须先改这个高度并同步 `ProcessView` 的 `RowDefinition`（那是独立决策，不在本版）。
7. **`hex=0` 保持**：现状已是 0，改版后必须仍为 0（R140）。
8. **不造死按钮**：不加"复制流程""导出流程"等新命令。

## 5. 状态矩阵（R115 逐控件）

| 控件 | Normal | Hover | Pressed | Focus | Disabled |
| --- | --- | --- | --- | --- | --- |
| 胶囊 | 白底 + 描边 | `--row-hover` + 描边加深 | `--border-light` | 2px 强调色环 | **不适用**（禁用流程仍可选中，见 §2.2） |
| 右键菜单项 | 透明 | `--row-hover` | 系统默认 | 2px 强调色内环 | 不适用（11 项全部可用） |

## 6. 三态（R116）

| 态 | 是否适用 | 理由 |
| --- | --- | --- |
| 加载 | **不适用** | 流程列表是内存集合（`Workspace.CurrentSolution.Flows`），同步可得，无异步加载阶段 |
| 空 | **不适用** | 流程数恒 ≥ 3（骨架流程不可删），`Flows.Count == 0` 不可达 |
| 错误 | **不适用** | 集合读取不会失败；单条流程的运行错误由胶囊状态色与 ToolTip 表达，不是列表级错误态 |

> 这三条都是"跳过"，按 `AGENTS.md` R38 必须能被反证——反证依据已写进上表（内存集合 / 骨架流程约束 / 无 I/O）。

## 7. 已知偏差（Web vs WPF，预判）

1. **字体度量**：Web 用 Segoe UI Variable；WPF 用微软雅黑。22px 行高下 11.5px 中文名的实际可见字数会少 1~2 个。
2. **脉冲动画**：Web 用 CSS `@keyframes`；WPF 用 `Storyboard` + `DoubleAnimation`，1400ms 循环，`Opacity` 1→0.3。
3. **删除线**：WPF `TextDecorations="Strikethrough"` 的线位置与 CSS 略有差异，不影响可读性。
4. **徽标圆角**：Web 2px；WPF `CornerRadius="2"`，需 `SnapsToDevicePixels="True"` 避免半像素模糊。
5. **滚动**：Web 用 `overflow-y: auto` + 10px 滚动条；WPF 占位约 17px，340 宽下胶囊可用宽度会再少 17px。
6. **`box-shadow` 光晕**：Web 的脉冲点是 `box-shadow` 外环；WPF 用外层 `Ellipse` + `Opacity`，或 `DropShadowEffect`。
7. **右键子菜单**：Web 稿里画成展开态（便于评审），实际 WPF 是 `MenuItem` 的原生悬浮子菜单，位置由系统决定。

## 8. 未纳入本版

| 项 | 原因 |
| --- | --- |
| 流程拖拽排序 | 当前顺序即 `Flows` 集合顺序，拖拽需要持久化字段与命令 |
| 流程复制 / 另存 | 需要新命令 |
| 胶囊上的触发方式徽标（手动/定时/HTTP） | `FlowInvokeType.DisplayText()` 已有文案，但 22px 单行没有位置；进 ToolTip |
| 流程搜索 / 过滤 | 流程数通常 < 10，加搜索框反而占地方 |
| 流程分组（按角色/按启用） | 三条骨架流程已用徽标区分，再分组会让 6 条流程占 3 个组头 |
| 跨流程总步数统计 | 需要新属性 + 订阅每条流程的 `Steps` 增删（R140 通知义务）；且会与 `ProcessView` 底栏的"当前流程步数"混淆 |

## 9. 验证计划（复刻后）

| 层 | 判据 |
| --- | --- |
| 静态 | `FlowListView.xaml` 的 `hex=0`；11 个 `FlowAction` 的 `CommandParameter` 齐全；`DataTemplate.Triggers` 里的 `RunState` 触发器仍在 |
| 运行时 | 胶囊数 = `Flows.Count`；选中胶囊 `SelectFlow` 同步；禁用流程胶囊**不显示**运行标记；角色徽标只在 `Role != None` 时出现；面板高度 112 时底栏完整可见 |
| 功能回环 | 右键 11 项全部可点；流程 > 6 条时胶囊区滚动且底栏不动；选中禁用流程出现提示条且不溢出 |
| 渲染 | 默认态（340×112）/ 超量滚动 / 禁用提示 / 右键菜单 四张 PNG |
| 回归 | `FlowCanvasChecks` 0 失败；`ProcessView` 的步骤树不受影响 |

## 10. 变更规则

确认后只读。需要调整时升 `v3`，并同步：`design.html`、本 README 版本号、令牌映射表、`docs/UI设计基线/README.md` 索引行。
