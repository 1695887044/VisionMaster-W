# 流程栏与画布样式整合（flow-logic）

- 日期：2026-10-20
- 范围：`ProcessView.xaml`、`FlowListView.xaml`、`FlowCanvasView.xaml`、`CanvasNodeViewModel.cs`、`Colors.xaml`、`ToolboxCategoryVisuals.cs`
- 设计依据：`docs/UI设计基线/flow-logic/design.html`；Shell 布局/模式隔离不变
- 目标：先做设计迁移的第 1 步（令牌）与第 2 步（流程栏 + 画布），不动 AvalonDock 主布局
- A 方案全量续篇：`docs/code-changes/2026-10-20-画布A方案全量落地（节点156x58与泳道收口）.md`

## 改动清单

### 1. 令牌与转换器

- `UI/Controls/Themes/Colors.xaml`
  - 新增逻辑容器类型色：`LogicIf* / LogicCase* / LogicWhile* / LogicFor* / LogicParallel*`，以及 `LogicSelectionSoftBrush`。
  - `FlowCanvasLinkBrush` 由石板灰改为 `#3B82F6`（数据线蓝实线）；`FlowCanvasOrderLinkBrush` 改为 `#94A3B8`（顺序链灰虚线）。
- `VisionMaster/Helpers/ToolboxCategoryVisuals.cs`
  - 新增 `CategorySoftBrushConverter`（分类色 → 88% 白混合浅底）与 `CategoryBorderBrushConverter`（分类色 → 65% 白混合描边），带缓存并冻结画刷。
  - 分类色仍以 `ToolboxCategoryVisuals` 为唯一来源，未在 XAML/插件模板里硬编码。

### 2. 流程栏

- `VisionMaster/Views/FlowListView.xaml`
  - 由竖排列表改为 **2 列流程胶囊**：状态点 + 流程名 + 步数（`Steps.Count`）；禁用流程灰点 + 删除线 + “禁用”标签。
  - 选中态改为 `LogicIfSoftBrush` 底 + `LogicIfBorderBrush` 描边；右键菜单保留。
  - 白底/悬停底改用 `DialogSurfaceBrush` / `DialogRowHoverBrush`，不新增硬编码颜色。
- `VisionMaster/Views/ProcessView.xaml`
  - 容器组头统一为 **类型徽标 + 名称 + 分支/算子计数 + 状态点**：
    - If → `IF`，While → `WHILE`，For → `FOR`，并行分组 → `PAR`，分支匹配 → `CASE`。
    - 左侧 3px 身份条分别使用 `LogicIf/While/For/Parallel/CaseBrush`。
  - 分支行：最小高度 32→28；`BorderThickness="2,0,0,0"` 做左侧类型色条；If/Else/For/While/Case/Parallel 分别使用对应 soft/border 色；分支运行中改蓝（不再整行绿）。
  - 算子行：图标块 26→24；分类色浅底 + 分类色描边 + 分类色图标（`CategorySoft/Border/Accent` 三个转换器）；名称 13→11.5、类型 11→10.5。
  - 选中态：`LogicSelectionSoftBrush` 底 + `FlowCanvasNodeSelectedBorderBrush` 竖条。

### 3. 画布

- `VisionMaster/Views/FlowCanvasView.xaml`
  - `StepNodeTemplate` 已按 flow-logic A 方案全量重做：**156×58**、图标 28×28/圆角 8、标题 11.5、副标题 9、`node-op` 操作徽标、表达式/操作摘要、右上 7×7 状态点、右下耗时、左上 16×14 深色序号、失败底色 `#FFF7F7`、选中 2px 运行色、禁用整节点 45% 透明。
  - `ContainerNodeTemplate`：组头 **46px**、类型色条 4×26、类型徽标高 20、条件胶囊高 20、右侧 `N 分支 · M 算子` + 7×7 状态点、序号 24×18；失败/条件缺失红框红头红点。
  - `LaneNodeTemplate`：泳道头 **28px**、分支徽标/名称/计数、If/Case/While/For/Parallel 类型分色；Else/Case 默认分支中性灰；父容器失败/缺条件时整列红染；While/For 说明条、并行汇合条。
  - 小地图改为 **180×118**、工具条开关、默认关闭。
  - 数据线/顺序链颜色由 `Colors.xaml` 令牌统一（蓝实线 / 灰虚线）。
- `VisionMaster/ViewModels/CanvasNodeViewModel.cs`
  - `TypeBadge` 改为短代码：`IF / WHILE / FOR / PAR / CASE / WAIT`。
  - 新增 `ContainerSummary` / `ContainerSummaryVisibility`，以及 `OpBadge`/`OpBadgeVisibility`、比较/逻辑表达式副标题、`ContainerMetaText`、`BranchBadge`、`LaneName`、`LaneCountText`、`LaneNoteText`、`JoinBarText`、`IsNeutralLane`、`IsLaneParentFaulted`。
  - 容器摘要的实时通知链在 `StepModel`（自身属性 + 分支 `Expression`/`BranchType`），画布节点只转发。

### 4. 断言同步

- `FlowCanvasChecks/Program.cs`、`ParallelContainerChecks.cs`
  - 类型徽标期望从 `If/For/Parallel` 改为 `IF/FOR/PAR`（设计变更，不是放宽断言）。

## 验证证据

- `dotnet build VisionMaster.sln -m:1`：**0 错误**（仅既有警告；`-m:1` 规避 `Plugin.Yolo`/`Plugin.DlClassify` 的 OnnxRuntime xcopy 并发抖动）。
- `UIThemeSmokeTest`：多格布局 / Shell 布局结构 / Panel 模式 / Shell 模式隔离 / GridView / LogConsole 全部通过；硬编码颜色扫描通过。
- `FlowCanvasChecks`：**2863 通过 / 0 失败**。
  - 新增 `FLC-1..36`：操作徽标（GTE/AND/XOR）、节点实量 156×58、小地图 180×118/默认关闭、泳道中性灰/失败红染、容器组头 46/泳道头 28、禁用整节点透明度、单泳道长条件不溢出。
  - 子进程实测：`card=156x58`、`minimap=180x118`、`while-header=269x46`、`step/container-disabled-opacity=0.45`。

## 已知边界 / 后续

1. **Case 分支仍为列布局**：设计稿是三条上下堆叠的横向分支行；当前沿用 `FlowLayoutStore`/`FlowCanvasViewModel` 的“分支=列”通用几何。`flow-logic/README.md` 已知偏差 #8 允许按现有 Canvas/TreeView 流式布局扩展，不把 3 列写死；若后续要严格按设计稿，需要给 `CaseStep` 单独实现行堆叠几何。
2. **未实现分支内 150×46 compact 节点**：全画布统一 156×58；主节点尺寸与设计一致，分支内节点比设计的 `node-sm` 略大。
3. **小地图开关是会话内视图态**，不落盘；需要跨重启记忆时接 `AppConfig`/`AppSettingsService`。
4. **断点圆点仍常驻**：状态点下方保留 14×14 命中区，右侧竖槽有两个小圆（设计稿没有断点入口）；这是保留现有调试交互的代价。
5. **折叠钮是浮层**：设计稿没有折叠控件；保留 18×18 浮层以维持现有折叠与双击豁免判据。
6. `ContainerSummary` 的实时通知链在 `StepModel`（自身属性 + 分支 `Expression`/`BranchType`），`CanvasNodeViewModel` 只转发；条件编辑后流程栏与画布同步刷新。
