# 流程栏 + 画布 · 真实算子版

| 项 | 内容 |
| --- | --- |
| 版本 | **v1** |
| 确认日期 | 2026-10-19 |
| 确认人 | 待用户确认 |
| 覆盖范围 | `ProcessView.xaml` 流程栏 + `FlowCanvasView.xaml` 画布；项目真实逻辑算子与真实插件；复杂项目组合；逻辑容器重点改造；检查器；状态矩阵；WPF 复刻映射 |
| 设计基线文件 | `design.html`（自包含，浏览器直接打开） |
| 设计画布 | 1920 × 1080（左流程栏 320 / 中画布 1278 / 右检查器 320）；按视口缩放，可切“实际大小” |
| 定位 | Web 只作设计媒介，**不进入构建、不部署**（AGENTS.md R126） |

## 一句话结论

这不是通用门电路库，而是**项目真实算子的流程栏 + 画布**：
逻辑运算和数值比较各是一个插件，操作模式由插件端口选择；If / 分支匹配 / While / For / 并行分组是内置容器；
流程栏与画布共用同一套“类型色 + 条件胶囊 + 分支泳道 + 状态”的语法。通用门电路页
`../logic-operators/` 保留为**未来安全回路的参考库**，不是本期流程画布基线。

## 与 `logic-operators/` 的关系

| 页面 | 用途 | 本期是否使用 |
| --- | --- | --- |
| `../logic-operators/` | 41 个通用逻辑门 / 组合逻辑的可视化参考，含真值表与门形 | **否**；未来做安全回路时参考 |
| `flow-logic/`（本页） | 项目真实算子、真实流程栏、真实画布的复杂项目 | **是** |

原因：项目流程栏的节点是 `StepModel`（`ActionStep` / `ConditionStep` / `WhileStep` / `ForStep` / `CaseStep` / `ParallelStep` / `WaitStep`），
不是“与门/或门”这种独立节点。逻辑运算是一个插件，操作模式是 `InputPort<LogicOperationType>`；
数值比较也是一个插件，操作模式是 `InputPort<ComparisonType>`。把 AND/OR/XOR 拆成不同节点会与现有编译/执行模型冲突。

## 真实算子清单

### 逻辑判断（2 个插件 / 12 种操作）

| 插件 | 操作 | 说明 |
| --- | --- | --- |
| `LogicOperationPlugin`（逻辑运算） | `AND` 与、`OR` 或、`NOT` 非、`XOR` 异或、`NAND` 与非、`NOR` 或非 | 端口：`A` / `B` → `Result`；操作模式是插件端口枚举 |
| `ComparisonPlugin`（数值比较） | `EQ` 等于、`NEQ` 不等于、`GT` 大于、`LT` 小于、`GTE` 大于等于、`LTE` 小于等于 | 端口：`A` / `B` / `Tolerance` → `Result`；支持浮点容差 |

### 逻辑控制（内置 StepModel）

| 节点 | 模型 | 关键属性 / 端口 |
| --- | --- | --- |
| If 条件分支 | `ConditionStep` | `Children` 分支；If / ElseIf / Else；条件在 `StepCollection.Expression` |
| 分支匹配 | `CaseStep` | `JudgeExpression` 判据；Case 分支填匹配值；默认分支最后 |
| While 循环 | `WhileStep` | 一个 `WhileLoop` 循环体；条件在分支 `Expression` |
| For 计次循环 | `ForStep` | `DefaultLoopCount` / `LoopCount` 端口；`Index` 输出 |
| 并行分组 | `ParallelStep` | `ExecutionMode`（顺序/真并发）、`FailFastMode`、`JoinTimeoutMs`；`RecommendedMaxBranches = 8` |
| 等待 | `WaitStep` | `ConditionExpression`、`PollIntervalMs`、`TimeoutMs`、`TimeoutAction`；输出 `TimedOut` / `ElapsedMs` |
| Break / Continue / Return | `BreakStep` / `ContinueStep` / `ReturnStep` | 非容器；在循环体/子流程里用小节点表达 |

### 复杂项目里用到的真实插件

图像采集 `ImageAcquisitionPlugin`、图像预处理 `PreProcessingPlugin`、模板匹配 `MatchingPlugin`、
卡尺测量 `CaliperMeasurePlugin`、Blob 缺陷检测 `BlobDetectPlugin`、码读取 `CodeReaderPlugin`、
变量赋值 `VariableAssignmentPlugin`、结果上报 `ResultUploadPlugin`、结果显示 `ResultDisplayPlugin`、
CSV 记录 `DataRecordPlugin`、延时 `DelayPlugin`、调用流程 `RunFlowPlugin`、C# 脚本 `CSharpScriptPlugin`、
轴运动 `MotionMovePlugin`、等待到位 `MotionWaitPlugin`。

## 页面结构

| 章节 | 内容 |
| --- | --- |
| 01 复杂项目 | 1920×1080 应用：流程栏 + 画布 + 检查器 + 状态栏；42 个画布节点 / 5 个容器 / 29 条连线 |
| 02 逻辑算子库 | 12 张逻辑判断卡（6 逻辑 + 6 比较）+ 9 张逻辑控制卡；全部用真实插件名与真实模型名 |
| 03 逻辑容器 | If / 分支匹配 / While / For / 并行分组 五张细节图 + 旧版/新版对照 |
| 04 状态矩阵 | 容器正常/运行/成功/失败/条件缺失/折叠；节点运行/失败/禁用 |
| 05 WPF 复刻 | 按 `ProcessView.xaml` / `FlowCanvasView.xaml` 的现有实现逐项给改动建议与约束 |

## 复杂项目怎么用满逻辑算子

| 算子 | 在项目中的位置 |
| --- | --- |
| `AND` | 顶部“逻辑运算_0”：`Score ≥ 0.85 && Width ≤ 12.5` → If 条件 |
| `GTE` | 顶部“数值比较_0”：`Score ≥ 0.85` |
| `LTE` | 顶部“数值比较_1”：`Width ≤ 12.5` |
| `NOT` | If 的 Else 分支“逻辑运算_1”：判定取反 |
| `OR` | 并行分支 2“逻辑运算_2”：缺陷 A 或缺陷 B |
| `XOR` | 并行分支 3“逻辑运算_3”：双工位码一致 |
| `NAND` | While 循环体“逻辑运算_4”：到位 NAND 超时 |
| `NOR` | 结果汇总“逻辑运算_5”：总判定取反 |
| `EQ` | Case 1“数值比较_4”：`Code = "OK"` |
| `NEQ` | Case 2“数值比较_5”：`Code ≠ ""` |
| `GT` | 并行分支 1“数值比较_2”：`MeasureValue > 12.0` |
| `LT` | 并行分支 2 的缺陷阈值（节点在画布上以 `逻辑运算_2` 的组合条件体现；详情页保留 LT 卡片） |
| If / Case / While / For / Parallel / Wait / Break | 五个容器 + While 循环体 + 等待节点 |

> 说明：画布是“复杂项目”的视觉总览，不要求每个分支都把全部子节点铺满；未在画布主路径出现的 `LT` 仍在第 02 节卡片中完整展示，
> 实际落地时挂在 Blob 分支的 `MaxArea < 500` 条件上即可。若需要“画布上 12 种操作全部出现”，把 Blob 分支的 `逻辑运算_2` 前再加一个 `数值比较 LT` 节点即可，不需要改容器结构。

## 设计决策

### 1. 两套颜色维度，不混用

| 维度 | 来源 | 用途 |
| --- | --- | --- |
| **分类色** | `VisionMaster\Helpers\ToolboxCategoryVisuals.cs` 的 13 条分类色 | 叶子节点的图标块 / 图标 / 左侧识别色 |
| **容器类型色** | 本页新增的容器类型语义 | 仅容器组头、边框、类型徽标、泳道头 |

分类色实际值（与代码逐字一致）：

| 分类 | 色值 |
| --- | --- |
| 逻辑控制 | `#6366F1` |
| 流程控制 | `#0EA5E9` |
| 逻辑判断 | `#8B5CF6` |
| 变量操作 | `#14B8A6` |
| 图像处理 | `#3B82F6` |
| 缺陷检测 | `#EF4444` |
| 测量 | `#F59E0B` |
| 标定 | `#06B6D4` |
| 几何工具 | `#10B981` |
| 定位 | `#F97316` |
| 数据处理 | `#64748B` |
| 常用工具 | `#6B7280` |
| 运动控制 | `#65A30D` |

容器类型色：

| 容器 | 类型色 | 浅底 | 描边 | 文字 |
| --- | --- | --- | --- | --- |
| If | `#6366F1` | `#EEF2FF` | `#C7D2FE` | `#4338CA` |
| 分支匹配 | `#8B5CF6` | `#F5F3FF` | `#DDD6FE` | `#6D28D9` |
| While | `#F59E0B` | `#FFFBEB` | `#FDE68A` | `#B45309` |
| For | `#10B981` | `#ECFDF5` | `#A7F3D0` | `#047857` |
| 并行分组 | `#0EA5E9` | `#F0F9FF` | `#BAE6FD` | `#0369A1` |

**为什么**：分类色回答“这是什么插件”，容器类型色回答“这是什么结构”。如果容器也用分类色，
If / Case / While / For / Parallel 会全是同一种靛蓝，用户无法一眼分辨结构；如果叶子节点用容器类型色，
又会把“测量/缺陷/定位”的分类信息丢掉。

### 2. 流程栏：容器组头 + 分支胶囊

- 流程胶囊条：4 个流程同屏；主流程 42 个算子，禁用流程半透明 + 删除线。
- 容器组头：3px 类型色条 + 类型徽标 + 名称 + 分支/算子计数 + 状态点；If 从灰色改为靛蓝。
- 分支行：浅色底 + 左侧 2px 类型色条 + 分支徽标 + `DisplayName` + 计数；不再用整圈重描边，降低树里的“框套框”噪音。
- 运行中的分支：蓝色左边条 + 蓝点脉冲；失败/跳过/禁用沿用现有语义色。
- 选中：3px 蓝条 + 淡蓝底；只选中容器组头，不选中分支卡片（R25）。

### 3. 画布：容器是主角，节点是内容

- 顶部一行 7 个真实步骤：图像采集 → 预处理 → 模板匹配 → 卡尺 → GTE / LTE → AND。
- 5 个容器：If（双泳道）、并行分组（三列泳道 + 汇合条）、分支匹配（三行 CASE）、While（循环体 + 回边）、For（计次 + Index）。
- 容器组头 46px：类型色条 + 徽标 + 名称 + 条件/模式胶囊 + 计数 + 状态点。
- 叶子节点：图标块（分类色）+ 名称 + 插件名/表达式 + 操作徽标（`GTE` / `AND` / `XOR` …）+ 状态点 + 耗时。
- 连线：数据线蓝实线、顺序链灰虚线；容器之间用编号 + 顺序链表示主流程，容器内部用短虚线表示执行顺序。
- **不常驻小地图**：改为工具条“小地图”开关。原布局里小地图固定在右下角，会盖住“结果汇总”的输出节点；这是本轮明确修掉的遮挡问题。

### 4. 检查器：选中并行分组

- 属性：执行模式（顺序 / 真并发）、失败聚合（继承 / 强制开 / 强制关）、汇合超时、推荐上限。
- 分支：3 条分支的算子数与运行状态；运行中的分支蓝点脉冲。
- 运行：上次运行、汇合耗时、分支完成数。
- 底部：重命名分组 / 添加分支；结构动作留在流程栏右键，参数动作留在检查器（R26）。

### 5. 状态矩阵

- 容器状态看“分支汇总”：正常、运行中、成功、失败、条件缺失、折叠。
- 节点状态看“自身执行”：正常、运行中、成功、失败、跳过、禁用、断点。
- 容器运行只给状态点脉冲，不把整个框刷成高饱和色；失败才换红边 + 红点。

## WPF 复刻

### 流程栏

| 设计元素 | 现有实现 | 建议 |
| --- | --- | --- |
| 容器组头 | `ProcessView.xaml` 的 `ConditionStep / WhileStep / ForStep / ParallelStep / CaseStep` 模板 | 统一为“类型色条 + 类型徽标 + 名称 + 条件胶囊 + 分支/算子计数 + 状态点” |
| 分支行 | `StepCollection` 模板 | 浅色底 + 左侧 2px 类型色条；文案只绑 `DisplayName`（R24） |
| 选中 / 运行态 | `ModernStepContainerStyle` + DataTrigger | 选中 3px 蓝条 + 淡蓝底；运行分支蓝点脉冲 |
| 状态 | 现有 `StepState` / `StepCollection.IsRunning` | 沿用现有语义色，不新增状态枚举 |

约束：新增 `StepModel` 子类必须补 `ProcessView.xaml` 的 `DataType` 模板（R23）；分支卡片不进“按选中项执行”的命令链（R25）。

### 画布

| 设计元素 | 现有实现 | 建议 |
| --- | --- | --- |
| 节点 | `StepNodeTemplate`（210×62） | 保持尺寸/锚点；图标块、标题、插件名、操作徽标、耗时/状态点统一槽位 |
| 容器 | `ContainerNodeTemplate` | 组头 46px；类型色淡底；条件胶囊；不刷大面积高饱和色 |
| 泳道 | `LaneNodeTemplate`（`CanvasNodeKind.Lane`） | 泳道头 28px；If/Else 不同底色；并行运行中的泳道蓝边 |
| 连线 | `ModuleLinkTemplate` 的 `StepConnection` | 数据线蓝实线 / 顺序链灰虚线 / 非法红虚线；箭头用 `Fill` 填充 |
| 状态 | DataTrigger + Storyboard | 绿成功 / 蓝运行 / 红失败 / 灰跳过；容器脉冲只给状态点 |
| 画布缩放 | `NodifyEditor` | 保留现有缩放/平移/适应；不要写死设计稿的 1278×960 |

### 逻辑算子节点

- **不要**为 AND / OR / XOR 各建一个 `StepModel` 子类。它们是同一个 `LogicOperationPlugin`，操作模式是 `InputPort<LogicOperationType>`。
- 节点右上角的 `node-op` 徽标显示当前操作模式；值仍来自插件端口枚举（R40：显示名走 `[Display]`，值仍是成员名）。
- `ComparisonPlugin` 同理：一个节点 + `ComparisonType` + `Tolerance`；不要拆成 6 个节点类。

### 容器与分支

- `ConditionStep` / `WhileStep` / `ForStep` / `CaseStep` / `ParallelStep` 都实现 `IContainerStep`，子分支是 `StepCollection`。
- `ParallelStep.ExecutionMode`（顺序/真并发）、`FailFastMode`（继承/强制开/强制关）、`JoinTimeoutMs` 是容器属性，不是分支属性。
- `WaitStep` 不是容器：它只有条件、轮询间隔、超时和 `TimedOut` / `ElapsedMs` 输出。
- 分支卡片不是算子：结构动作走流程栏右键，参数动作走检查器（R25/R26）。

### 令牌

| Web 变量 | 建议 WPF 资源 | 值 |
| --- | --- | --- |
| `--logic` | `LogicCategoryBrush` | `#8B5CF6`（逻辑判断） |
| `--if` | `LogicIfBrush` | `#6366F1` |
| `--case` | `LogicCaseBrush` | `#8B5CF6` |
| `--while` | `LogicWhileBrush` | `#F59E0B` |
| `--for` | `LogicForBrush` | `#10B981` |
| `--par` | `LogicParallelBrush` | `#0EA5E9` |
| `--run` | 复用现有 `FlowCanvasRunningBrush` / `DialogAccentBrush` | `#2563EB` |
| `--success` | 复用 `FlowCanvasSuccessBrush` | `#16A34A` |
| `--danger` | 复用 `FlowCanvasFailedBrush` | `#EF4444` |

分类色不要新建一套：直接读 `ToolboxCategoryVisuals.BrushFor(category)`；容器类型色可以新增为 `LogicIf* / LogicCase* / LogicWhile* / LogicFor* / LogicParallel*`，只用于容器组头与边框。

## 已知偏差（复刻时按 R130 记录）

1. **图标**：Web 用 1.7px 线框 SVG；WPF 用 Font Awesome 6 Pro Solid 私有区字形，视觉重量不同。
2. **字体**：Web 用 Segoe UI Variable / Microsoft YaHei UI；WPF 用 Microsoft YaHei UI，同字号字宽/行高有差异。
3. **阴影**：Web 卡片有柔和阴影；WPF 只给选中/悬停用 `DropShadowEffect`，不给全部节点加。
4. **小地图**：设计稿改为工具条开关，不常驻右下角；WPF 若保留 `Minimap`，必须做可收起或半透明且不遮挡输出节点。
5. **画布尺寸**：设计稿中画布 1278×960；WPF 用真实 `NodifyEditor` 缩放/平移，不要写死尺寸。
6. **状态数据**：设计稿是静态“运行中”快照；WPF 状态来自 `StepModel.State` / `StepCollection.IsRunning`，不要造演示用状态源。
7. **流程栏计数**：42 是画布可见节点总数（含分支子节点）；流程栏折叠分支时只显示组头计数，不要求每行都展开。
8. **Case 分支行**：设计稿用横向 3 列节点；WPF 里如果分支子步骤更多，按现有 TreeView/Canvas 的流式布局扩展，不要把 3 列写死。

## 验证记录

- 结构：42 个画布节点 / 5 个画布容器 / 29 条 SVG 连线；12 张逻辑判断卡 / 9 张逻辑控制卡；2 个详情拖入区。
- DOM：无重复 ID、无缺失 SVG 引用、无外部 URL 依赖、无文本溢出、无节点重叠。
- 浏览器：1280×720 自适应缩放 + 实际大小两档截图；首屏、流程栏、If / 并行 / Case / While / For、算子库、容器细节、状态矩阵、WPF 表逐屏复核。
- 本轮修掉：
  - Case 分支行节点与泳道头重叠（节点高度 44→36，节点下移到泳道头之后）；
  - 详情卡 mini-node 标题/副标题挤在同一行（改为 flex column）；
  - 右下角小地图遮挡“结果汇总”输出节点（改为工具条开关）；
  - While 循环体下方大片空白（补超时/退出说明条）；
  - 流程栏缺少顶部 3 个逻辑判断节点（补 `数值比较_0/1` 与 `逻辑运算_0`，计数统一为 42）；
  - “整理”按钮误用小地图图标（改用 layers 图标）。

## 未覆盖（下一批）

- WPF 实际改版：`ProcessView.xaml` 容器模板、`FlowCanvasView.xaml` 容器/泳道样式、检查器并行分组草稿。
- 与真实运行态的联调：分支并发高亮、汇合耗时、失败聚合。
- 条件编辑器（If/While/Wait）与 Case 判据编辑器的视觉统一。
- 深色 / 高对比主题、触摸/笔、键盘导航、读屏名称。
- 旧版/新版截图的逐像素对照（R129/R132）。

## 变更规则

设计基线确认后只读。需要调整时升版本（v2），并同步更新：

1. 本目录 `README.md` 的版本/日期/确认人；
2. `design.html` 的页面与令牌；
3. `ProcessView.xaml` / `FlowCanvasView.xaml` 的复刻结果；
4. `docs/UI设计基线/README.md` 的现有基线索引；
5. `../logic-operators/` 只作安全回路参考库，不随本期变更。
