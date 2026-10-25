# 拖拽落点提示 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | **v2（已确认，已复刻到 WPF）** |
| 确认日期 | 2026-10-24（用户会话确认："添加/移动"、"使用 /"、"做"） |
| 确认人 | 用户 |
| 覆盖范围 | 提示条（`EffectText` / `DestinationText` 的呈现层）+ 落点判定文案。放置端两处：流程栏 `ProcessView.xaml` / `ProcessViewModel`、画布 `FlowCanvasView.xaml.cs`（`CanvasDropTarget`）/ `FlowCanvasViewModel.DescribeDropTarget`。**不含**拖拽卡片本体（见 `../toolbox-drag-ghost/`，v2 已冻结），**不含**画布内部拖节点（走视图自己的 Popup） |
| 设计基线文件 | `design.html`（v2，自包含，浏览器直接打开） |
| 证据 | `evidence/overview-model.png`（§01–§02 模型与间距标注）、`evidence/overview-states.png`（§03 五种状态 1:1）、`evidence/overview-spec.png`（§05–§08 规格/交互/实现路径） |
| 状态 | **已复刻**：[DH] 断言 37 条 + 3 条变异反证（见 `docs/code-changes/2026-10-24-拖拽落点提示.md`） |

## 定位

按 `AGENTS.md` §30.1 的四步流程：① Web 设计基线（本目录）→ ② 定稿与令牌映射 → ③ WPF 复刻 → ④ 验收。
Web 只作设计媒介，**不进入构建、不部署**（R126）。

## 版本沿革

- **v1（草案）**：五状态、双浮层模型、文案公式；路径分隔符用 `›`，状态③待定。
- **v2（本稿，已确认）**：用户拍板三件事 —— 动词「添加 / 移动」、分隔符 `/`、状态③（会展开折叠分组）要做。

## 已确认的决定

| 项 | 决定 |
| --- | --- |
| 动词 | 「添加」（Copy）/「移动」（Move），只认拖拽效果，不按来源猜 |
| 路径分隔符 | `/` |
| 状态③ | 做：落点在折叠容器内 → 琥珀提示，且**落下时真的展开**（不能只提示不兑现） |

## 模型：两个浮层

| 浮层 | 回答 | 归属 |
| --- | --- | --- |
| 拖拽卡片（v2） | **是什么**（算子名 + 分类） | 拖拽源 |
| 落点提示条（本稿） | **落到哪**（动词 + 路径 + 位置） | 放置端 |

卡片在上、提示条在正下方，左缘对齐、间隔 6px。

## 五种状态

| # | 状态 | 视觉 | 文案示例 |
| --- | --- | --- | --- |
| ① | 有效 · 顶层 | 天蓝 | `添加「图像采集」→ 主流程 · 末尾` |
| ② | 有效 · 嵌套分支 | 天蓝 | `移动「模板匹配」→ 并行组 / 分支 2 · 末尾` |
| ③ | 需注意（会展开折叠分组） | 琥珀 | `会展开「并行组」后再放入` |
| ④ | 非法 | 红 | `不能放这里：Break / Continue 只能放在 For / While 循环体内…` |
| ⑤ | 运行锁 | 灰 | `流程运行中，禁止编辑；如需修改请先点击"停止"` |
| ⑥ | 无落点 | — | 整条收起，只留卡片 |

## 文案公式

```
[动词] 「名称」→ [路径] · [位置]
```

- 动词只认拖拽效果；「主流程」是顶层位置名（不是流程名）。
- 非法原因必须可执行（跟"怎么才能放"）。
- 深层嵌套（≥3 级）保留末两级，前缀省略为 `…`。

## 规格

**提示条**：高 26 / 圆角 8 / 水平内边距 9 / 图标 11 + 间距 6 / 文字 11.5 Medium /
最大宽 340 单行省略 / 无描边 / 单层软阴影 / 位于卡片正下方（间隔 6）。

**状态色**（全部在 `UI/Controls/Themes/Colors.xaml`，共 12 个 `DragHint*` 键）：

| 状态 | 底 | 文字 | 图标 |
| --- | --- | --- | --- |
| 有效 | `#ECF8FD` | `#0369A1` | `#0EA5E9` |
| 需注意 | `#FEF5E7` | `#B45309` | `#F59E0B` |
| 非法 | `#FCEFEE` | `#B91C1C` | `#DC2626` |
| 锁定 | `#F3F4F6` | `#475569` | `#64748B` |

## 实现形态

gong 4.0.0 没有 5 态的可定制浮层钩子（内置 `DropHintState` 只有 None/Active/Error，
模板 DataContext 只有 `HintText` + `HintState`），要表达"需注意 / 锁定"两档就得自己写 adorner。
因此复刻时把提示条与卡片放进**同一个**拖拽 adorner 模板（`DragGhostStack`，纵向 StackPanel），
视觉与位置和本稿一致，只是不再由两个独立 adorner 承载。

落点状态经一个单写者的临时状态对象传给模板：

| 部件 | 职责 |
| --- | --- |
| `Services/DragHintPresenter` | 进程级单例状态（`Text` / `Kind` / `Owner`），`Show(owner, text, kind)` / `Hide(owner)` / `Hide()` |
| `Services/DragHintText` | 文案公式纯函数（Add / Move / Path / Illegal / WillExpand / InsertBefore、After） |
| `Controls/DragHintPill` | 提示条视觉（Kind 驱动四档配色与图标，Hidden 时 Collapsed） |
| `Controls/DragGhostStack` | 卡片 + 提示条组合；`Loaded` 时 `Reset()`（= 新一轮拖拽开始） |

**Owner 标记是必需的**：gong 的 `DragLeave` 走 `Dispatcher.BeginInvoke` 延迟派发，
从流程栏移到画布时流程栏那条 DragLeave 可能排在画布 DragOver 之后——不带 owner 的 Hide
会把画布刚写的提示抹掉。`DragLeave` 只撤自己写的。

**两处放置端必须同源判定**：
- 流程栏：`ProcessViewModel.ResolveProcessDrop` 同时供 `DragOver` 与 `Drop` 使用
  （gong 在 TreeViewItem 上下 25% 把 TargetCollection 设为父集合、中间 50% 设为条目自己的
  Items，两种落点不同，只看 TargetItem 类型会撒谎）；
- 画布：`FlowCanvasViewModel.DescribeDropTarget` 复用 `ResolveInsertTarget` + `AllowsLoopControl`，
  与 `DropToolAt` 同一份判据。

## 已知偏差（R130）

- **单 adorner 承载**：见上「实现形态」，视觉一致、实现合并。
- **无淡入 / 无下移动效**：本稿 120ms 淡入 + 2px 下移；WPF 未做（与 v2 卡片同一处理）。
- **阴影单层近似**：WPF 每个元素只有一个 `Effect`。
- **动词未单独加粗**：本稿动词 600、其余 500；WPF 整句统一 `FontWeight=Medium`（拆分 Run 需要把整句结构化，暂不做）。
- **名称未单独截断**：本稿"截名称、不截路径尾部"；WPF 整行 `CharacterEllipsis`，超长时会从尾部截断（即先丢"位置"）。
- **画布内部拖节点不覆盖**：那条路径用视图自己的 Popup，不经过 gong adorner。
- **状态③的展开不可撤销**：`DropToolAt` 落进折叠容器后调 `ExpandIfCollapsed`，Ctrl+Z 只撤销插入、不把容器折回去（视图态本就不进撤销栈）。

## 断言与验证

- `FlowCanvasChecks` 的 `[DH]` 组 37 条：文案公式 / 状态通道生命周期（含 owner 隔离）/
  画布四档落点判定（含状态③真展开、null 步骤安全）/ XAML 接线扫描（精确模板键 + 12 个令牌）。
- 变异反证 3 条（均判红后还原）：分隔符改回 `›`、去掉折叠判断、流程栏漏换一个模板。
- 全量：`FlowCanvasChecks` 3608 通过 / 0 失败；`UIThemeSmokeTest` 108 项 0 失败。

## 未做

- 流程栏的 Break/Continue 闸门此前只在 `Drop` 里判；本次在 `DragOver` 补了同一判据（提示与落子一致），
  但没有改 `Drop` 的判定锚点（仍用 `TargetItem`）——若后续要改，必须两处同改。
- 分支卡片（`StepCollection`）作为拖拽源的落点提示：`DragOver` 只认 `ToolItemModel` / `StepModel`，
  分支卡片拖动仍无提示（模板已换成 `DragGhostStack`，接入是后续工作）。
