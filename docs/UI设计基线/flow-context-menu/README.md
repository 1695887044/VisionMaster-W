# 流程列表 · 右键菜单 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | **v1** |
| 日期 | 2026-10-27 |
| 确认日期 / 人 | 用户 2026-10-27 裁定「按照 A 开始改」 |
| 覆盖范围 | `VisionMaster\Views\FlowListView.xaml` 的流程胶囊右键菜单（12 项：现有 11 个 `FlowAction` + 新增 `BatchDisplayWindow`） |
| 设计文件 | `design.html`（自包含；含真实上下文 / 四种状态 / 双行变体 / 五态放大 / 规格与令牌表） |
| 证据目录 | `evidence/`：`design-v1-full.png`（整页）、`design-v1-context.png`（真实上下文）、`design-v1-states.png`（四态矩阵）、`design-v1-interaction.png`（五态 + 规格表） |
| 状态 | **已确认并复刻（2026-10-27）**；落地记录 `docs/code-changes/2026-10-27-流程右键菜单复刻（A方案）.md`；待真机验收 |

## 0. 为什么重做

| # | 现状问题 | 证据 | 后果 |
| --- | --- | --- | --- |
| 1 | 菜单**没有挂任何样式**，吃应用级 Fluent 的默认 `ContextMenu` | `FlowListView.xaml:145` 只有 `<ContextMenu FontFamily="{StaticResource Icon}">`，无 `Style` | 13px 灰图标、27px 行、无分组、无状态、无页脚——与同一面板里步骤右键（`StepContextMenuStyle`：340px / 44px 行 / 图标底片 / 双行说明 / 页脚）明显不是一套 |
| 2 | 12 项里 9 项藏在「流程 ▸」二级菜单 | `FlowListView.xaml:146-194` | 每次操作多一次悬停；子菜单向左/右展开还容易挡住画布 |
| 3 | 无分组，配置类与运行类混排 | 同上 | 加进「批量设置显示窗口」后更难扫读 |
| 4 | 无状态表达 | 菜单项全部常亮 | 已禁用流程的「运行一次」点了才提示；骨架流程的「删除」点了才被拒；未运行时「停止本流程」也是一片可点 |
| 5 | 图标只有 13px 单色字 | 截图（用户提供，2026-10-27） | 与工具箱 / 步骤菜单的 28px 图标底片语言脱节 |

## 1. 设计目标（可验收）

1. 与步骤右键菜单**同族**（同一套令牌、图标底片、页脚语言），但密度更紧凑。
2. **12 项一屏可见、不滚动**：320px 宽 / 约 512px 高。
3. 状态可读：普通 / 已禁用流程 / 骨架流程 / 运行中 四种状态各有明确的可用与不可用表达，禁用项**带原因 ToolTip**（不静默变灰）。
4. 不新增颜色令牌、不新增命令、不改绑定机制。

## 2. 结构（信息架构）

**取消「流程 ▸」二级菜单，改四组平铺**：

| 组 | 项 | `FlowAction` |
| --- | --- | --- |
| **流程** | 添加流程 / 重命名 / 编辑注释 / 删除 | `Create` / `Rename` / `EditComment` / `Delete` |
| **配置** | 禁用流程（禁用时显示「启用流程」）/ 编译本流程 / 批量设置显示窗口 | `ToggleEnabled` / `Compile` / `BatchDisplayWindow` |
| **运行** | 运行一次 / 循环运行 / 停止本流程 | `RunOnce` / `RunContinuous` / `Stop` |
| **方案** | 流程管理 / 变量事件（方案级） | `Manager` / `VariableEvents` |

- 分组标题是**不可聚焦**的标签（20px 高、10px 字、字距 0.1em、右侧一条发丝线），不参与键盘导航。
- 删除固定在「流程」组末尾、红色；**不**提升到顶层（与现状一致）。
- 组的顺序按"改流程 → 改配置 → 跑起来 → 方案级"排列；「方案」组放最后，避免和单流程命令混读。

## 3. 尺寸与布局（推荐稿 A）

| 项 | 值 |
| --- | --- |
| 菜单宽 | 320 |
| 内边距 | 6（沿用 `StepContextMenuStyle`） |
| 圆角 | 14 |
| 行高 | 32（行间 `Margin="0,1"`） |
| 行圆角 | 8 |
| 图标底片 | 20×20 / 圆角 7 / 图标 12 |
| 分组标题 | 20 高 / 10px / 字距 .1em |
| 页脚 | 28 高 / 10.5px |
| 总高 | 6×2 + 4×20（组头）+ 12×32（行）+ 3×1（组分隔）+ 28（页脚）≈ **512** |

高度对照：现状约 430px（27×12 + 分隔 + 内边距）；变体 C（双行）约 660px。
流程条本身只占 112px，512px 的菜单在 768px 高的窗口里也能完整展开。

## 4. 视觉与语气

图标底片与悬停底色都按"语义族"着色（与 `StepMenuItemStyle` 的 `Tone` 一一对应）：

| 语气 | 项 | 底片 | 悬停底 | 文字 |
| --- | --- | --- | --- | --- |
| 强调 Accent | 添加流程 | `DialogAccentSoftBrush` | `DialogAccentSoftBrush` | `DialogAccentTextBrush` |
| 配置靛蓝 Indigo | 批量设置显示窗口 | `LogicIfSoftBrush` | `LogicIfSoftBrush` | `LogicIfTextBrush` |
| 运行绿 Success | 运行一次 / 循环运行 | `DialogSuccessSubtleBrush` | `DialogSuccessSubtleBrush` | `DialogSuccessTextBrush` |
| 警示琥珀 Amber | 禁用 / 启用流程 | `DialogWarningSubtleBrush` | `DialogWarningSubtleBrush` | `DialogWarningTextBrush` |
| 危险红 Danger | 删除 / 停止本流程 | `DialogDangerSubtleBrush` | `DialogDangerSubtleBrush` | `DialogDangerTextBrush`（标题）/ `DialogDangerBrush`（图标） |
| 中性 Slate | 其余 5 项 | `ScadaBadgeBackgroundBrush` | `ScadaToolbarHoverBrush` | `ScadaTextBrush` |

> 悬停用**同族浅底**而不是统一灰：语气色在悬停时也成立，扫读时"红色那两项是删除和停止"一眼可见。
> 代价是 WPF 每个语气要多一条 `Trigger`（现有样式只有 Danger 一条 `MultiTrigger`）。

## 5. 状态（四种真实状态）

| 状态 | 变灰的项 | 文案变化 | 徽标 |
| --- | --- | --- | --- |
| **B1 默认**（启用、未运行） | 停止本流程 | — | 禁用流程：`当前：已启用`；停止：`未运行` |
| **B2 流程已禁用** | 编译 / 批量设置 / 运行一次 / 循环运行 / 停止 | 「禁用流程」→「启用流程」 | `当前：已禁用` |
| **B3 骨架流程**（Main/Home/End） | 删除 | — | 同 B1 |
| **B4 运行中** | 编译 / 批量设置 / 运行一次 / 循环运行 | — | 停止：`运行中` |

- **暂停态**（`MainRunState.Paused`）：「批量设置显示窗口」恢复可用——与 `EditGate.ModuleParameters`（暂停中放行参数编辑）同一口径。
- 每个变灰项都必须有 `ToolTip` 说明原因（例：`骨架流程不允许删除` / `流程运行中，禁止编辑；如需修改请先停止`），否则等于"点了没反应"。
- 已禁用流程的胶囊仍可选中、仍可右键（这是它唯一的恢复入口），所以 B2 不是整菜单禁用，只是逐项变灰。

## 6. 交互

- 左键单击执行；**不显示快捷键**（当前流程列表没有真实快捷键，不造）。
- 键盘：↑↓ 移动、Enter 执行、Esc 关闭；焦点环 2px 内嵌（`DialogAccentBrush`，R115）。分组标题跳过。
- 动效：Web 稿 150ms 背景过渡；**WPF 菜单项不做过渡**（R116 的 150~200ms 只约束有过渡的控件）。
- 菜单打开位置由系统决定（锚在胶囊上、向右下展开）；不做自定义定位。
- 没有二级菜单（12 项一屏可见）。

## 7. 图标映射（Font Awesome 6 Pro Solid）

| 项 | 码点 | 现状 |
| --- | --- | --- |
| 添加流程 | `f067` | 新增 |
| 重命名 | `f044` | 已用（现状菜单） |
| 编辑注释 | `f075` | 已用 |
| 删除 | `f2ed` | 已用 |
| 禁用流程 | `f204`（toggle-off） | 新增 |
| 启用流程 | `f205`（toggle-on） | 新增 |
| 编译本流程 | `f0e7` | 已用 |
| 批量设置显示窗口 | `f2d0`（window-maximize） | 本次已用 |
| 运行一次 | `f04b` | 已用 |
| 循环运行 | `f021` | 已用 |
| 停止本流程 | `f04d` | 已用 |
| 流程管理 | `f09c` | 已用 |
| 变量事件（方案级） | `f0f3` | 已用 |

> Web 稿用 12px 线性 SVG 示意；WPF 用 `DialogIconFont` 字形，尺寸与底片一致，形状略有差异（见 §12）。

## 8. 令牌映射

全部键都已在 `UI\Controls\Themes\Colors.xaml` 存在，**不新增令牌**：

`ScadaSurfaceBrush`、`DialogBorderLightBrush`、`ScadaTextBrush`、`ScadaSecondaryTextBrush`、`ScadaFaintTextBrush`、`ScadaBadgeBackgroundBrush`、`ScadaToolbarHoverBrush`、`DialogAccentBrush`、`DialogAccentSoftBrush`、`DialogAccentTextBrush`、`LogicIfSoftBrush`、`LogicIfTextBrush`、`DialogSuccessSubtleBrush`、`DialogSuccessTextBrush`、`DialogWarningSubtleBrush`、`DialogWarningTextBrush`、`DialogDangerSubtleBrush`、`DialogDangerBrush`、`DialogDangerTextBrush`。

逐项色值与用途见 `design.html` 的「E · 规格与令牌映射」两张表。

## 9. 复刻约束

1. **不改命令与绑定机制**：`MyViewModelProxy` + `PlacementTarget.DataContext` 靶点、`FlowCommand` 的 12 个 `CommandParameter`、动态标题 `FlowToggleHeader` 全部保留。
2. **样式落点**：在 `UI\Controls\Themes\Controls\StepContextMenu.xaml` 里**追加**三个键：
   `FlowContextMenuStyle`（320 宽）、`FlowMenuItemStyle`（32 行 / 20 底片 / 徽标 / 语气悬停）、`FlowMenuGroupHeaderStyle`（`Separator` + `Tag`）。
   `FlowListView.xaml` 就地 merge 该文件（R22 同一手法）。**不改** `StepMenuItemStyle` 的既有外观——步骤右键不能跟着变。
3. **`hex=0`**：`FlowListView.xaml` 现状是 0，改版后仍须为 0（R140）；新增样式片段只用 `{StaticResource}`，不写字面色值。
4. **分组标题用 `Separator` + `Tag`**，不要用 `IsEnabled=False` 的 `MenuItem`——后者仍会被菜单键盘导航命中，也会吃掉高亮。
5. **禁用态需要新增只读属性（P1）**：
   - `CanDeleteFlow` ← `MandatoryFlows.IsMandatory(SelectFlow)`（切换流程时刷新）；
   - `CanCompileFlow` / `CanRunFlow` ← `SelectFlow.IsEnabled`（`IsEnabled` 已有转发）且非运行中（`OnMainRunStateChanged` 已有）；
   - `CanStopFlow` ← `SelectFlow.RunState != Stopped`（需在 `OnSelectFlowPropertyChanged` 补 `RunState` 转发）；
   - `CanBatchDisplayWindow` ← `_runState` 非 RunningOnce/RunningContinuous（暂停放行）。
   这些只影响 `IsEnabled`，不改命令语义；即使不做，VM 里原有的拦截提示仍然生效（行为不变，只是少了"提前变灰"）。
6. **状态徽标（P2 可选）**：`FlowStateBadge`（当前：已启用/已禁用）与 `FlowRunBadge`（未运行/运行中）。后者需要订阅 `RunState`；第一版可以只做前者或都不做。
7. **页脚**：`仅作用于「{SelectFlow.FlowName}」`，切换流程刷新（沿用 `OnSelectFlowPropertyChanged` 的转发）。
8. **不动宿主**：`ProcessView.xaml:290` 的 112px、胶囊条样式（`flow-list` v2）均不改。
9. **不造死按钮**：不加"复制流程""导出流程"等新命令；12 项与 `FlowAction` 一一对应。

## 10. 状态矩阵（R115 逐控件）

| 控件 | Normal | Hover | Pressed | Focus | Disabled |
| --- | --- | --- | --- | --- | --- |
| 菜单项 | 透明底 | 同语气浅底 | 无独立态（WPF 菜单按下由 `IsPressed` 驱动，可复用 Hover 底） | 2px 内嵌强调环 | 灰字（`ScadaFaintTextBrush`）+ 底片 50% 透明 + 不响应悬停 |
| 分组标题 | 10px 灰字 + 发丝线 | **不响应** | 不响应 | **不可聚焦** | 不适用 |
| 菜单外框 | 白底 + 1px 浅边 + 阴影 | 不响应 | — | — | 不适用 |
| 页脚 | 10.5px 灰字 + 上边线 | 不响应 | — | — | 不适用 |

## 11. 三态（R116）

| 态 | 是否适用 | 反证（R38） |
| --- | --- | --- |
| 加载 | **不适用** | 菜单是静态命令列表，无异步数据；`FlowCommand` 的参数在 XAML 里写死 |
| 空 | **不适用** | 方案恒有 Home/Main/End 三条骨架流程（`MandatoryFlows.Ensure`），不存在"没有流程可右键" |
| 错误 | **不适用** | 命令失败由既有 `IUserNotifier` 弹提示（`FlowListViewModel` 每个 case 都已接），不是菜单级错误态 |

## 12. 已知偏差（Web vs WPF，预判）

1. **图标形状**：Web 用 12px 线性 SVG 示意；WPF 用 FA6 Pro Solid 字形，尺寸/底片一致，笔画粗细略有差异。
2. **字体度量**：Web Segoe UI / WPF 微软雅黑；12.5px 下中文宽度差 1~2px，靠 `TextTrimming="CharacterEllipsis"` 兜底。
3. **阴影**：Web 两层 `box-shadow`；WPF 单个 `DropShadowEffect`（`BlurRadius=28 ShadowDepth=6 Opacity=.18`，沿用 `StepContextMenuStyle`）。
4. **动效**：Web 150ms 背景过渡；WPF 无（不做）。
5. **徽标圆角**：Web `border-radius: 999px`；WPF `CornerRadius` 取高度一半（8）。
6. **组头细线**：Web 用 `::after` 自动填充；WPF 用 `Grid` 两列（Auto + `*`）里的 `Border Height=1`。
7. **子菜单**：现状是 WPF 原生悬浮子菜单；本版取消，改为平铺分组（这是 IA 变更，见 Q2）。

## 13. 验证计划（复刻后）

| 层 | 判据 |
| --- | --- |
| 静态 | `FlowListView.xaml` `hex=0`；12 个 `CommandParameter` 齐全；新样式片段无字面色值；`UIThemeSmokeTest` 的 FlowAction 清单 12 项 |
| 运行时 | 四种状态各右键一次：变灰项与 ToolTip 正确；分组标题键盘跳过；菜单不滚动（512 ≤ 可视高）；页脚流程名随选中切换 |
| 渲染 | `evidence/` 四张 PNG（B1~B4）+ 一张上下文图 |
| 回归 | `FlowCanvasChecks` / `ScadaChecks` / `UIThemeSmokeTest` 全绿；**步骤右键菜单外观不变**（`StepMenuItemStyle` 未被改动） |

## 14. 裁定结果（2026-10-27）

| # | 问题 | 结果 |
| --- | --- | --- |
| Q1 | 采用 A（320 / 32px 紧凑单行）还是 C（340 / 44px 双行说明）？ | **A** |
| Q2 | 是否去掉「流程 ▸」二级菜单、改四组平铺？ | **去掉** |
| Q3 | 是否做逐项禁用态（B2/B3/B4 灰项 + ToolTip）？ | **做**（新增 5 个 `Can*` 只读属性 + 5 个原因 ToolTip，命令语义不变） |
| Q4 | 是否做状态徽标？ | **只做启用/禁用徽标**；停止徽标后置 |
| Q5 | 页脚显示「仅作用于『流程名』」？ | **做** |

## 15. 复刻落地（2026-10-27）

| 项 | 落地 |
| --- | --- |
| 样式 | `StepContextMenu.xaml` 追加 `FlowContextMenuStyle` / `FlowMenuItemStyle` / `FlowMenuGroupHeaderStyle`；`StepMenuItemStyle` 未改 |
| 视图 | `FlowListView.xaml` 就地合并菜单字典 + 12 项重写（逐项挂样式/语气/禁用/徽标/页脚） |
| VM | `FlowListViewModel` 新增 13 个只读展示属性 + `RefreshFlowMenuState()` |
| 附加属性 | `MenuItemTone.Accent`；`MenuItemEx.Badge` |
| 断言 | `UIThemeSmokeTest`：15 处静态契约 + 真实加载字典套模板（300×34，图标/标题/徽标进可视树） |
| 图标 | 13 个码点在 `Font Awesome 6 Pro-Solid-900.otf` 中逐一验证存在 |
| 未做 | 停止徽标（Q4 后置）；真机截图验收 |

## 16. 变更规则

确认后只读。需要调整时升 `v2`，并同步：`design.html`、本 README 版本号、`docs/UI设计基线/README.md` 索引行、已复刻的 `FlowListView.xaml`。
