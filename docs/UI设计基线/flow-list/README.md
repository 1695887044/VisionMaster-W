# 流程胶囊条 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | **v1** |
| 日期 | 2026-10-21 |
| 确认日期 / 人 | 待用户确认 |
| 覆盖范围 | `VisionMaster\Views\FlowListView.xaml`（流程导航顶部的流程胶囊条 + 底部统计 + 右键菜单） |
| 设计文件 | `design.html`（自包含；含默认态 / 300 窄面板 / 空态 / 右键菜单） |
| 证据目录 | `evidence/`（评审后补渲染 PNG） |
| 状态 | **待用户确认**；本轮只出设计稿，未改任何 WPF 代码 |

## 1. 现状

`FlowListView.xaml` 已在 `app-shell` v2 中被结构性地提及（"流程胶囊条"），但**从未单独出过设计稿**：当前实现是 223 行、`hex=0`（已令牌化），主要问题不是颜色而是**信息密度与语义表达**。

| # | 问题 | 证据 | 影响 |
| --- | --- | --- | --- |
| 1 | 2 列固定网格，窄面板下名称挤成两三个字 | `FlowListView.xaml:29`（`UniformGrid Columns="2"`） | 左栏收到 300 宽时，每条胶囊约 140px，中文名 4 字就截断 |
| 2 | 状态点兼任"启用/运行"两义（现已拆分） | `:76-83` 绿点 + `:193-217` 的 `RunState` 触发器 | 绿点到底是"能跑"还是"正在跑"，用户无法区分 |
| 3 | 角色（Home/Main/End）无视觉表达 | `FlowModel.Role` 已存在（`Core\Models\FlowModel.cs:59-72`），视图未用 | 三条骨架流程与普通流程长得一样 |
| 4 | 步数用 9.5px + `Consolas` | `:97-100` | 低于 11px 字号底线（`scada-controls` D7 同族问题） |
| 5 | "禁用"标签 9px 红字 | `:106-108` | 同上；且只有一个红字，与"运行中"同格切换，状态语义混在一起 |
| 6 | 无空态 | — | 没有流程时是一块空白 |
| 7 | 选中禁用流程无提示 | 现状只有胶囊本身变灰 | 用户选中后不知道为什么运行按钮没反应 |

## 2. 设计要点

### 2.1 胶囊结构（两行式）

```text
┌──────────────────────────┐
│ ● 主检测  [主]           │  第一行：状态点 + 名称 + 角色徽标
│   18 步   ● 运行中        │  第二行：步数 + 运行标记
└──────────────────────────┘
```

- **状态点只表达"启用/禁用"**：绿 = 启用，灰 = 禁用。运行态移到第二行的「运行中 / 已暂停」标记（带脉冲点）。
- **两行式**：名称与步数分两行，2 列布局下每格约 175px，中文名 6~8 字可见（现状单行 + 步数 + 标签挤在一行，4 字就截断）。
- **角色徽标**：`回零` / `主` / `收尾` 三个 9.5px 小徽标，颜色借用逻辑容器族（紫/靛/琥珀），**不占用状态语义色**。

### 2.2 五种状态（互斥）

| 状态 | 状态点 | 名称 | 第二行 | 胶囊底 |
| --- | --- | --- | --- | --- |
| 普通启用 | 绿 | 常规 | `N 步` | 白底 + 1px 描边 |
| 选中 | 绿 | **600 字重** | `N 步` | `--selected-soft` + 靛蓝描边 |
| 运行中 | 绿 | 600 字重 | `N 步` + **脉冲「运行中」** | 白底（选中则选中底） |
| 已暂停 | **主色** | 600 字重 | `N 步` + **主色「已暂停」** | 同上 |
| 禁用 | **灰** | 删除线 + 灰 | `N 步`（灰） | `--surface-2` + 右侧「禁用」标签 |

关键规则：**禁用态强制隐藏运行标记**（`[data-enabled="false"] .run { display: none }`）。禁用流程不可能在跑，两者同时出现只会让用户怀疑状态源坏了（现状注释已记录这条判断，设计稿把它视觉化）。

### 2.3 选中禁用流程 → 提示条

选中一条禁用流程时，胶囊条下方浮出一条：

```text
⚠ 该流程已禁用：不会参与自动触发，手动运行仍可用。
```

- 红底红字（`#FEF2F2` / `--danger-text`）+ `⚠` 图标。
- 只在选中禁用流程时出现；选别的流程即消失。
- 现状完全没有这个提示，用户选中后按"运行"没反应只能猜。

### 2.4 宽度自适应

| 面板宽 | 列数 | 单格宽 | 名称可见字数 |
| --- | --- | --- | --- |
| ≥ 360 | 2 列 | ~170 | 6~8 字 |
| < 360（如 300） | **1 列** | ~280 | 12~15 字 |

现状固定 2 列，在 300 宽左栏下名称只剩两三个字。设计稿给出降级规则：**宽度不足时改单列**，宁可多滚动也不截断名字。

### 2.5 步数字号

- 从 9.5px 提到 **10px**（等宽 + `tabular-nums`）。
- 这是"可读性底线"：`scada-controls` 的 D7 已把 8/9px 判为不可接受，本视图的 9.5px 属同一族。
- 角色徽标与「禁用」标签用 9.5px 是**例外**——它们是 4 字以内的短标签，且有底色衬托，不承担阅读负担。

### 2.6 底部统计

```text
6 条流程 · 53 步                          选中：主检测
```

- 左侧：流程数 + 总步数（数字等宽加粗）。
- 右侧：当前选中流程名（现状没有这一项，用户在多条流程间切换时容易失去上下文）。

### 2.7 空态

```text
▤
还没有流程
新建一条流程开始编排算子。
[ 新建流程 ]
```

按钮接现有 `FlowCommand(Create)`，不是死按钮。

### 2.8 右键菜单

现状的 11 项菜单（流程子菜单 9 项 + 流程管理 + 变量事件）结构合理，设计稿只做**视觉规范化**：

- 208px 宽、4px 内边距、每项 28px、图标列 14px。
- 分组用 1px 发丝线隔开，与现状 `Separator` 对应。
- **「删除」单独放最后一组并标红**：破坏性操作与其它项之间隔一道线，降低误点。
- 子菜单箭头 `▸` 靠右。

## 3. 令牌映射

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--accent` | `#0EA5E9` | `DialogAccentBrush` |
| `--accent-dark` | `#0284C7` | `DialogAccentDarkBrush` |
| `--accent-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
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
| `--disabled` | `#C0C4CC` | `ScadaDisabledTextBrush`（现状已用） |
| `--row-hover` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--selected-soft` | `#EEF2FF` | `LogicIfSoftBrush`（现状已用） |
| `--selected-border` | `#C7D2FE` | `LogicIfBorderBrush`（现状已用） |
| `--selected-text` | `#4338CA` | `LogicIfTextBrush` |

**角色徽标颜色（复用逻辑容器族，不新增键）**：

| 角色 | 文字 | 底 | 描边 | 复用键 |
| --- | --- | --- | --- | --- |
| 回零 Home | `#6D28D9` | `#F5F3FF` | `#DDD6FE` | `LogicCase*` 三键 |
| 主 Main | `#4338CA` | `#EEF2FF` | `#C7D2FE` | `LogicIf*` 三键 |
| 收尾 End | `#B45309` | `#FFFBEB` | `#FDE68A` | `LogicWhile*` 三键 |

**不需要新增任何令牌。**

## 4. 复刻约束

1. **不改数据契约**：`ItemsSource="{Binding Workspace.CurrentSolution.Flows}"`、`SelectedItem="{Binding SelectFlow}"` 保留；`FlowCommand` 的 11 个 `CommandParameter`（`Create`/`Delete`/`Rename`/`EditComment`/`ToggleEnabled`/`Compile`/`RunOnce`/`RunContinuous`/`Stop`/`Manager`/`VariableEvents`）全部保留。
2. **不改 `FlowModel`**：`FlowName` / `Description` / `IsEnabled` / `RunState` / `Steps.Count` / `Role` 只读消费。`Role` 目前视图未用，本设计**启用**它（只读展示，不新增写入路径）。
3. **`RunState` 触发器位置不变**：现状注释明确"`ControlTemplate.Triggers` 里的 `DataTrigger` 对模板内元素静默失效"，因此运行态触发器必须继续写在 `DataTemplate.Triggers` 里。设计稿的分状态视觉不改变这个技术约束。
4. **右键菜单绑定不变**：`BindingProxy`（`MyViewModelProxy`）+ `PlacementTarget.DataContext` 的靶点机制不动；`FlowToggleHeader` 的动态文案保留。
5. **宽度降级**：`UniformGrid Columns="2"` 改为"宽 ≥ 360 用 2 列、否则 1 列"。WPF 无 CSS 媒体查询，用 `DataTrigger` 绑 `ActualWidth` 或自定义 `Panel` 实现；**若实现成本高，退化为固定 1 列**（可读性优先于密度）。
6. **`hex=0` 保持**：现状已是 0，改版后必须仍为 0（R140）。
7. **图标按钮 `Padding="0"`**（R68）：胶囊本身不是图标按钮，但右键菜单若加图标列需注意。
8. **不造死按钮**：空态的「新建流程」接 `FlowCommand(Create)`；不新增"复制流程""导出流程"等。

## 5. 已知偏差（Web vs WPF，预判）

1. **字体度量**：Web 用 Segoe UI Variable；WPF 用微软雅黑。两行式在 WPF 下行高略高（约 44px vs 42px）。
2. **脉冲动画**：Web 用 CSS `@keyframes`；WPF 用 `Storyboard` + `DoubleAnimation`，1500ms 循环，`Opacity` 1→0.35。
3. **删除线**：WPF `TextDecorations="Strikethrough"` 的线位置与 CSS 略有差异，不影响可读性。
4. **徽标圆角**：Web 3px；WPF `CornerRadius="3"` 一致，但 WPF 边框在 1px 时会有半像素模糊，建议 `SnapsToDevicePixels="True"`。
5. **2 列 / 1 列自适应**：Web 用 `grid-template-columns` 断点；WPF 需 `DataTrigger` 绑宽度，或用 `WrapPanel` + `ItemWidth` 固定值（后者更简单但换行点不受控）。
6. **阴影**：右键菜单 Web 两层阴影；WPF `Popup` 单层 + 1px 描边。

## 6. 未纳入本版

| 项 | 原因 |
| --- | --- |
| 流程拖拽排序 | 当前流程顺序即 `Flows` 集合顺序，拖拽需要持久化字段与命令 |
| 流程复制 / 另存 | 需要新命令 |
| 胶囊上的触发方式徽标（手动/定时/HTTP） | `FlowInvokeType.DisplayText()` 已有文案，但胶囊空间有限；建议放 ToolTip（本版已用 ToolTip 承载 `Description`，可扩展） |
| 流程搜索 / 过滤 | 流程数通常 < 10，加搜索框反而占地方 |
| 流程分组（按角色/按启用） | 三条骨架流程已用徽标区分，再分组会让 6 条流程占 3 个组头 |

## 7. 验证计划（复刻后）

| 层 | 判据 |
| --- | --- |
| 静态 | `FlowListView.xaml` 的 `hex=0`；11 个 `FlowAction` 的 `CommandParameter` 齐全；`DataTemplate.Triggers` 里的 `RunState` 触发器仍在 |
| 运行时 | 胶囊数 = `Flows.Count`；选中胶囊 `SelectFlow` 同步；禁用流程胶囊显示「禁用」且**不显示**「运行中」；角色徽标只在 `Role != None` 时出现 |
| 功能回环 | 右键 11 项全部可点；空态「新建流程」→ `Flows` +1；宽度降到 300 → 单列 |
| 渲染 | 默认态（380）/ 窄面板（300）/ 空态 / 右键菜单 四张 PNG |
| 回归 | `FlowCanvasChecks` 0 失败；`ProcessView` 的步骤树不受影响 |

## 8. 变更规则

确认后只读。需要调整时升 `v2`，并同步：`design.html`、本 README 版本号、令牌映射表、`docs/UI设计基线/README.md` 索引行。
