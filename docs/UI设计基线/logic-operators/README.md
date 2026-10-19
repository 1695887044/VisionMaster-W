# 逻辑算子 · 可视化设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | **v1** |
| 确认日期 | 2026-10-19 |
| 确认人 | 待用户确认 |
| 覆盖范围 | 流程画布中的逻辑 / 条件 / 并行节点视觉语言；**41 个算子 / 6 族**；3 个组合示例；节点状态矩阵；逐算子 WPF 复刻映射 |
| 设计基线文件 | `design.html`（自包含，浏览器直接打开） |
| 设计画布 | 示例 A 1200×600、B 1200×820、C 1200×940；页面内容最大宽度 1560 |
| 定位 | Web 只作设计媒介，**不进入构建、不部署**（AGENTS.md R126） |

> **适用范围说明（2026-10-19）**：本页是**通用逻辑门参考库**，不是本期 VisionMaster 流程画布的设计基线。
> 项目现有流程栏/画布使用 `LogicOperationPlugin`（6 种操作）+ `ComparisonPlugin`（6 种操作）+ 内置容器，
> 不把 AND/OR/XOR 拆成独立节点。本期真实算子版见 `../flow-logic/`；本页保留给未来的**安全回路**参考。

## 一句话结论

这不是换肤，而是一套**节点语法**：一个节点骨架 + 六族身份色 + 五种线语义 + 四类容器，
41 个算子全部复用同一套骨架。WPF 侧因此只需要**一个节点模板 + 一个 `DataTemplateSelector` +
一组 `Path` 字形**，不需要为每个算子写一套控件。

## 页面结构

| 章节 | 内容 |
| --- | --- |
| 01 设计语言 | 节点解剖、线的语义、端口语义、六族身份色 |
| 02 算子总览 | 41 张算子卡：节点外观、端口、真值表 / 表达式、WPF 摘要 |
| 03 示例 A · 安全联锁 | 13 节点 / 12 连线：与、非、SR 锁存、真/假路径、输出 |
| 04 示例 B · 视觉决策 | 19 节点 + IF 容器 / 16 连线：比较谓词、异或/同或、条件分支 |
| 05 示例 C · 复杂组合 | 21 节点 + SWITCH/PARALLEL 容器 / 18 连线：多数表决、MUX、多路分支、边沿、计数、并行、汇合 |
| 06 节点状态矩阵 | Normal / Hover / Selected / Invalid / Disabled / Running |
| 07 WPF 复刻 | 41 行逐算子映射 + 节点 / 画布 / 状态三条公共骨架 |

## 41 个算子

### 布尔位运算（15）

状态 / 交互：`true → 输出端口实心；false → 空心；invalid → 红边虚线`。

| 算子 | 端口 | WPF 视觉实现 |
| --- | --- | --- |
| 与 AND | 2 入 · 1 出 | `Path` `g-and` D 形门 + 2×`Ellipse` 入 / 1×`Ellipse` 出；`Border` 232×72 |
| 或 OR | 2 入 · 1 出 | `Path` `g-or` 盾形门 + 2×`Ellipse` 入 / 1×`Ellipse` 出 |
| 非 NOT | 1 入 · 1 出 | `Path` 三角形 + 输出圆环；1×`Ellipse` 入 / 1×`Ellipse` 出 |
| 异或 XOR | 2 入 · 1 出 | `Path` `g-xor` 盾形 + 输入侧第二条弧线；2 入 1 出 |
| 同或 XNOR | 2 入 · 1 出 | `Path` `g-xnor` XOR 门 + 输出圆环；2 入 1 出 |
| 与非 NAND | 2 入 · 1 出 | `Path` `g-nand` AND 门 + 输出圆环 |
| 或非 NOR | 2 入 · 1 出 | `Path` `g-nor` OR 门 + 输出圆环 |
| 蕴含 IMPLY | 2 入 · 1 出 | OR 门 + 输入 A 侧圆环；节点副标题写 `A→B` |
| 非蕴含 NIMPLY | 2 入 · 1 出 | AND 门 + 输入 B 侧圆环；2 入 1 出 |
| 缓冲 BUFFER | 1 入 · 1 出 | `Path` 三角形；1 入 1 出 |
| 三输入与 AND3 | 3 入 · 1 出 | `Path` `g-and` + 3×`Ellipse` 入 / 1×`Ellipse` 出；`Border` 232×80 |
| 三输入或 OR3 | 3 入 · 1 出 | `Path` `g-or` + 3×`Ellipse` 入 / 1×`Ellipse` 出；`Border` 232×80 |
| 多数表决 MAJORITY | 3 入 · 1 出 | `Path` 矩形 + `≥2` `TextBlock`；3 入 1 出 |
| 常量真 TRUE | 0 入 · 1 出 | `Border` + `TextBlock` `1`；1×`Ellipse` 出 |
| 常量假 FALSE | 0 入 · 1 出 | `Border` + `TextBlock` `0`；1×`Ellipse` 出 |

### 比较谓词（8）

状态 / 交互：`比较结果 true/false 驱动端口填充与线型；阈值可编辑`。

| 算子 | 端口 | WPF 视觉实现 |
| --- | --- | --- |
| 等于 EQ | 2 入 · 1 出 | `Path` 六边形 + `=` `TextBlock`；2 入 1 出 |
| 不等于 NEQ | 2 入 · 1 出 | `Path` 六边形 + `≠` `TextBlock`；2 入 1 出 |
| 大于 GT | 2 入 · 1 出 | `Path` 六边形 + `>` `TextBlock`；2 入 1 出 |
| 大于等于 GTE | 2 入 · 1 出 | `Path` 六边形 + `≥` `TextBlock`；2 入 1 出 |
| 小于 LT | 2 入 · 1 出 | `Path` 六边形 + `<` `TextBlock`；2 入 1 出 |
| 小于等于 LTE | 2 入 · 1 出 | `Path` 六边形 + `≤` `TextBlock`；2 入 1 出 |
| 区间 IN RANGE | 1 入 · 1 出 | `Path` 六边形 + `∈`；1 入 1 出；阈值参数在节点内 |
| 空判断 IS NULL | 1 入 · 1 出 | `Path` 六边形 + `∅`；1 入 1 出 |

### 控制流（5）

状态 / 交互：`容器展开/折叠；分支高亮；子节点独立状态`。

| 算子 | 端口 | WPF 视觉实现 |
| --- | --- | --- |
| 条件分支 IF / ELSE | 1 入 · 2 出 | `Border` 容器 + `Grid` 双泳道；TRUE/FALSE 徽标；子节点仍用普通节点模板 |
| 多路分支 SWITCH | 1 入 · 3 出 | `Border` 容器 + N 行 CASE 泳道；每行左侧 CASE 徽标；行高 80 |
| 顺序 SEQUENCE | 1 入 · 1 出 | `Path` 三个方块 + 箭头；1 入 1 出；节点内显示步骤数 |
| 循环 WHILE | 1 入 · 1 出 | `Border` 容器 + 循环体泳道 + 回边 `Path`（虚线箭头）；1 入 1 出 |
| 等待 WAIT UNTIL | 1 入 · 1 出 | `Path` 时钟 + 1 入 1 出；超时端口用第二个 `Ellipse` |

### 状态 / 时序（6）

状态 / 交互：`Q/Q̄ 状态点；复位后保持；S=R=1 非法红边`。

| 算子 | 端口 | WPF 视觉实现 |
| --- | --- | --- |
| SR 锁存 SR LATCH | 2 入 · 2 出 | `Path` 矩形 + `SR` `TextBlock`；2 入 2 出；Q/Q̄ 状态点 |
| D 触发器 D FLIP-FLOP | 2 入 · 1 出 | `Path` 矩形 + 时钟三角 + `D`；2 入 1 出 |
| 上升沿 EDGE RISE | 1 入 · 1 出 | `Path` 脉冲波形；1 入 1 出；节点内显示 `↑` |
| 下降沿 EDGE FALL | 1 入 · 1 出 | `Path` 脉冲波形 + 副标题 `↓`；1 入 1 出 |
| 翻转 TOGGLE | 1 入 · 1 出 | `Path` T 形 + 翻转箭头；1 入 1 出；Q 状态点 |
| 计数器 COUNTER | 1 入 · 1 出 | `Path` 矩形 + `n+1`；1 入 1 出；数字等宽 |

### 路由 / 汇合（5）

状态 / 交互：`选中路径高亮；未选分支降透明；汇合点显示计数`。

| 算子 | 端口 | WPF 视觉实现 |
| --- | --- | --- |
| 并行分支 PARALLEL | 1 入 · 3 出 | `Border` 容器 + `UniformGrid` 泳道；底部 Join 端口；分支独立状态 |
| 选择器 MUX 2:1 | 3 入 · 1 出 | `Path` 梯形 + `MUX`；3 入 1 出；S 端口在下 |
| 首非空 COALESCE | 3 入 · 1 出 | `Path` 漏斗 + `??`；3 入 1 出 |
| 门控 GUARD | 2 入 · 1 出 | `Path` 盾形 + 勾选；2 入 1 出 |
| 汇合 JOIN | 3 入 · 1 出 | `Path` 汇合线；3 入 1 出；节点内显示汇合策略 |

### 输入 / 输出（2）

状态 / 交互：`输入/输出端口；连接状态胶囊；未连接虚线`。

| 算子 | 端口 | WPF 视觉实现 |
| --- | --- | --- |
| 输入 INPUT | 0 入 · 1 出 | `Path` 输入箭头 + `Border`；1×`Ellipse` 出 |
| 输出 OUTPUT | 1 入 · 0 出 | `Path` 输出箭头 + `Border`；1×`Ellipse` 入 |

## 视觉语言

### 节点骨架

- 默认 232×72；二输入门 220×76；比较/时序 200×68；输出 240×56；容器按泳道数增高。
- 左侧 **3px 家族色条**只标类别，不标真假。
- 30×30 字形槽：`Path` 几何或单字符 `TextBlock`。
- 标题 12.5/600，副标题 10.5 等宽（表达式、类型、`A→B`）。
- 右上角 9px 等宽徽标：`AND / XOR / IF / SW / PAR / SR / OUT`。
- 右侧 7px 状态点：绿成功、灰待机、红失败、蓝运行脉冲。
- 端口 10px：输入空心、输出实心（家族色）；命中区 20px（WPF 用透明 `Ellipse` 扩大）。

### 线的语义

| 线型 | 颜色 | 用途 |
| --- | --- | --- |
| 实线 | `#64748B` 石板灰 | 普通数据 / 布尔值 |
| 实线 | `#16A34A` 绿 | 真路径 / 命中 |
| 虚线 | `#94A3B8` 灰 | 假路径 / 未命中 |
| 虚线 | `#EF4444` 红 | 非法 / 未连接 |
| 细实线 | `#A8B3C2` 浅灰 | 执行顺序 |

正交折线，不用斜线；折点用 `StrokeLineJoin=Round`。线标签是 3px 圆角的小底片，压在线身上。

### 容器

| 容器 | 结构 | 视觉要点 |
| --- | --- | --- |
| IF | 头部 + TRUE / FALSE 双泳道 | 靛蓝 `IF` 徽标 + 条件表达式 pill + 分支计数 |
| SWITCH | 头部 + N 行 CASE 泳道 | 琥珀色身份；每行 CASE 徽标；右侧泳道输出端口 |
| PARALLEL | 头部 + N 列泳道 | 翠绿身份；每列底部输出端口；分支独立状态 |
| WHILE | 头部 + 循环体泳道 + 回边 | 回边用虚线箭头，明确“回到条件” |

容器是**背景层**，子节点仍在同一套绝对坐标里（不改成相对坐标）；WPF 用同一个 `Canvas`
配 `Panel.ZIndex` 即可，容器移动时子节点由模型层联动（R76 的口径）。

### 六族身份色

| 族 | 主色 | 浅底 | 描边 | 文字 | 用途 |
| --- | --- | --- | --- | --- | --- |
| 布尔位运算 | `#6366F1` | `#EEF2FF` | `#C7D2FE` | `#4338CA` | AND / OR / NOT / XOR… |
| 比较谓词 | `#0891B2` | `#ECFEFF` | `#A5F3FC` | `#0E7490` | EQ / GT / RANGE… |
| 控制流 | `#D97706` | `#FFFBEB` | `#FDE68A` | `#B45309` | IF / SWITCH / WHILE… |
| 状态 / 时序 | `#E11D48` | `#FFF1F2` | `#FECDD3` | `#BE123C` | SR / DFF / EDGE… |
| 路由 / 汇合 | `#059669` | `#ECFDF5` | `#A7F3D0` | `#047857` | MUX / JOIN / PARALLEL… |
| 输入 / 输出 | `#475569` | `#F1F5F9` | `#CBD5E1` | `#334155` | INPUT / OUTPUT |

**家族色只表示“这是哪一类”，不表示真假**。真假只由端口填充与线型表达；选中态统一用
全局蓝 `#2563EB`，避免状态族的红色节点被误读成“错误”。

## 三个组合示例

### A · 安全联锁（13 节点 / 12 连线）

`光栅 A/B → 安全与 → 联锁与 → 门锁与 → SR 锁存 → 允许启动`
`急停 → 非 → 联锁与`；`手动复位 → SR 的 R`；`Q → 非 → 报警`。

看点：二输入门的上下错位端口、真/假两种线型、SR 的 Q/Q̄ 双输出、SET/RESET 长回边。

### B · 视觉决策（19 节点 + IF 容器 / 16 连线）

`触发 → 上升沿 → 蕴含 → 图像采集 → 模板匹配 → 得分阈值 / 面积区间 → 尺寸与得分`
`缺陷 A/B → 异或`；`一致性 / 配方匹配 → 同或`
`尺寸与得分 ∧ ¬异或 ∧ 同或 → 判定与 → IF`
`IF.TRUE → 数据上报 / 结果上报`；`IF.FALSE → 报警锁存 → 报警输出`。

看点：比较谓词与布尔门的混排、异或/同或的对称布局、IF 容器的条件 pill、两条分支各两个算子。

### C · 复杂组合（21 节点 + SWITCH/PARALLEL 容器 / 18 连线）

`传感器 A/B/C → 多数表决 → SWITCH`
`模式 / 配方 A/B → 选择器 → 阈值判定 → 阈值输出`
`触发 → 上升沿 → 计数器 → 计数输出`
`SWITCH.CASE 2 → PARALLEL → 卡尺测量 / 码读取 → 汇合 → 结果输出`

看点：多数表决的三输入、MUX 的三输入（S 在下）、SWITCH 的三条 CASE 泳道、
PARALLEL 的两条分支与底部输出端口、JOIN 的汇合、状态族的边沿与计数。

## WPF 复刻

### 公共骨架

| 层 | WPF 实现 |
| --- | --- |
| 节点 | `Border` + `Path`（字形）+ `Ellipse`（端口 / 状态点）+ `TextBlock`（标题 / 副标题 / 徽标） |
| 节点选择 | `DataTemplateSelector` 按 `Family` / `Kind` 选模板；容器单独一套模板 |
| 画布 | `ItemsControl` + `Canvas`，`ItemContainerStyle` 绑 `Canvas.Left/Top`；容器与节点同一坐标空间 |
| 连线 | `Path` + `PathGeometry` / `PolyLineSegment`；箭头用 `Polygon` / `Path` 三角形；虚线用 `StrokeDashArray` |
| 层级 | 容器背景 z=0、连线 z=1、节点 z=2、选中 `Adorner` z=3 |
| 状态 | `DataTrigger` 绑 `State`（Normal / Hover / Selected / Invalid / Disabled / Running）；自定义控件可用 `VisualStateManager` |
| 选中光 | `DropShadowEffect`：`ShadowDepth=0`、`BlurRadius=8`、`Opacity=.35`、`Color=#2563EB`；只给选中项，避免全画布阴影开销 |
| 运行脉冲 | `Storyboard` + `DoubleAnimation` 驱动状态点 `Opacity` / `ScaleTransform`，1.6s `ease-out` 循环 |
| 拖拽 | `Thumb` 或 `MouseLeftButtonDown` + `CaptureMouse`；选中框用 `Adorner` |
| 画布缩放 | `Viewbox` / `ScaleTransform`；点阵底用 `DrawingBrush`（24×24 瓦片），不用 `ScrollViewer` 滚动条 |
| 真值表 | `UniformGrid` + `Ellipse`（实心/空心）+ `TextBlock`；不用 `DataGrid` |
| 菜单 | `ContextMenu`，`CommandParameter` 绑 `PlacementTarget.DataContext`；命令按 R27 用 `DelegateCommand<object?>` + `is` 判型 |

### 逐算子映射

完整 41 行见上方“41 个算子”六张表；每行的“WPF 视觉实现”就是复刻时的模板拆分依据。
共性只有三条：

1. **叶子算子** = 一个 `Border` 骨架 + 一个字形 `Path` / 字符 + N 个端口 `Ellipse`；
2. **容器算子** = 一个 `Border` 背景 + `Grid` / `UniformGrid` 泳道 + 子节点继续用叶子模板；
3. **状态 / 时序** = 在叶子上多一个 `Ellipse` 状态点 + `DataTrigger` 或 `Storyboard`。

### 容器与现有流程模型的对应

- `IF / ELSE`、`SWITCH`、`WHILE` 对应现有的 `StepCollection` 分支结构；
- `PARALLEL` 对应现有的 `ParallelStep` + 多条 `StepCollection` 分支；
- 分支卡片**不是算子**，不得进入“按当前选中项执行”的命令链（R25）；
- 新增 `StepModel` 子类时必须同步 `ProcessView.xaml` 的具体类型模板（R23），不能只靠基类模板兜底。

## 令牌映射

| Web CSS 变量 | 建议 WPF 资源键 | 值 | 用途 |
| --- | --- | --- | --- |
| `--bool` | `LogicBoolBrush` | `#6366F1` | 布尔族主色 / 端口 |
| `--bool-soft` | `LogicBoolSoftBrush` | `#EEF2FF` | 布尔族浅底 |
| `--bool-border` | `LogicBoolBorderBrush` | `#C7D2FE` | 布尔族描边 |
| `--bool-text` | `LogicBoolTextBrush` | `#4338CA` | 布尔族文字 |
| `--cmp` | `LogicCompareBrush` | `#0891B2` | 比较族主色 |
| `--cmp-soft` | `LogicCompareSoftBrush` | `#ECFEFF` | 比较族浅底 |
| `--cmp-border` | `LogicCompareBorderBrush` | `#A5F3FC` | 比较族描边 |
| `--cmp-text` | `LogicCompareTextBrush` | `#0E7490` | 比较族文字 |
| `--ctl` | `LogicControlBrush` | `#D97706` | 控制族主色 |
| `--ctl-soft` | `LogicControlSoftBrush` | `#FFFBEB` | 控制族浅底 |
| `--ctl-border` | `LogicControlBorderBrush` | `#FDE68A` | 控制族描边 |
| `--ctl-text` | `LogicControlTextBrush` | `#B45309` | 控制族文字 |
| `--state` | `LogicStateBrush` | `#E11D48` | 状态族主色 |
| `--state-soft` | `LogicStateSoftBrush` | `#FFF1F2` | 状态族浅底 |
| `--state-border` | `LogicStateBorderBrush` | `#FECDD3` | 状态族描边 |
| `--state-text` | `LogicStateTextBrush` | `#BE123C` | 状态族文字 |
| `--route` | `LogicRouteBrush` | `#059669` | 路由族主色 |
| `--route-soft` | `LogicRouteSoftBrush` | `#ECFDF5` | 路由族浅底 |
| `--route-border` | `LogicRouteBorderBrush` | `#A7F3D0` | 路由族描边 |
| `--route-text` | `LogicRouteTextBrush` | `#047857` | 路由族文字 |
| `--io` | `LogicIoBrush` | `#475569` | I/O 族主色 |
| `--io-soft` | `LogicIoSoftBrush` | `#F1F5F9` | I/O 族浅底 |
| `--io-border` | `LogicIoBorderBrush` | `#CBD5E1` | I/O 族描边 |
| `--io-text` | `LogicIoTextBrush` | `#334155` | I/O 族文字 |
| `--wire` | `LogicWireBrush` | `#64748B` | 数据线 |
| `--wire-true` | `LogicWireTrueBrush` | `#16A34A` | 真路径 |
| `--wire-false` | `LogicWireFalseBrush` | `#94A3B8` | 假路径（虚线） |
| `--wire-bad` | `LogicWireBadBrush` | `#EF4444` | 非法 / 未连接 |
| `--wire-order` | `LogicWireOrderBrush` | `#A8B3C2` | 执行顺序 |
| 选中 `#2563EB` | `LogicSelectionBrush` | `#2563EB` | 全局选中描边 / 光 |
| 画布 `#F7F8FA` | `LogicCanvasBrush` | `#F7F8FA` | 逻辑画布底 |
| 点阵 `#C9D2DE` | `LogicGridDotBrush` | `#C9D2DE` | 24×24 点阵 |

浅色底、文字、描边继续复用现有的 `Dialog*` / `Scada*` 令牌，不新增同义键。
若 `DialogAccentBrush` 已经是 `#2563EB`，选中色直接复用它，不再定义 `LogicSelectionBrush`（R117）。

## 实现取舍

1. **一套骨架 vs 每个算子一套控件**：选一套骨架 + 字形 + 端口数量差异；WPF 用一个
   `DataTemplateSelector` 即可覆盖 41 个算子。代价是 SR/MUX/容器需要额外的“部件槽”，
   但视觉语言保持一致。
2. **全局选中蓝 vs 家族色选中**：选全局蓝。家族色继续标类别；否则状态族的红色节点被选中时
   会像“错误态”，与 Invalid 的红边混淆。
3. **正交折线 vs 贝塞尔曲线**：选正交。密集逻辑里正交线更容易追踪，WPF `Path` 也更容易
   精确复刻；曲线更柔和，但折点/命中测试/自动路由都更复杂。
4. **容器背景 + 绝对坐标子节点 vs 嵌套布局**：选绝对坐标。容器移动时子节点由模型层联动，
   坐标不重算；WPF `Canvas` 天然匹配。代价是容器要自己管裁剪与 z-order。
5. **隐藏滚动条 vs 保留滚动条**：设计画布固定 1200 宽；页面在 ≤1580px 时把右侧说明栏折到画布下方，
   ≤1260px 时画布才出现横向滚动条。WPF 用 `Viewbox` / `ScaleTransform`，不要用 `ScrollViewer`
   的 17px 滚动条占位。应用内若需要平移，沿用现有画布的 pan/zoom，不另造滚动条。
6. **真值表用 `UniformGrid` vs `DataGrid`**：选 `UniformGrid` + `Ellipse`。4~8 行不需要
   虚拟化与表格 chrome；视觉也更接近“位”的语义。
7. **端口 10px vs 命中区 20px**：视觉 10px，命中区 20px。WPF 必须加透明命中 `Ellipse`，
   否则鼠标/触摸很难点中。
8. **静态示例 vs 交互**：Web 页面只展示静态组合；WPF 实现要覆盖状态矩阵里的六种状态，
   并保留拖拽、选中、右键菜单、运行态。状态矩阵是视觉契约，示例只是组合展示。

## 已知偏差（复刻时按 R130 记录）

1. **字体度量**：Web 用 Segoe UI Variable / Microsoft YaHei UI；WPF 用 Microsoft YaHei UI。
   同字号字宽/行高有差异，节点宽度以“文字不截断”为准，不硬抠 232。
2. **阴影**：Web 的卡片阴影是 `box-shadow`；WPF 节点只在选中/悬停用 `DropShadowEffect`，
   不给全部节点加阴影（性能与视觉噪声都不划算）。
3. **点阵底**：Web 用 `radial-gradient` 画 1px 点；WPF 用 `DrawingBrush` 瓦片。非整数 DPI 下
   点会略糊，需 `UseLayoutRounding` / `SnapsToDevicePixels`。
4. **滚动条**：Web 隐藏；WPF `ScrollViewer` 默认占约 17px，必须用 `Viewbox`/缩放替代。
5. **选中光**：Web `box-shadow` 3px；WPF `DropShadowEffect` 的 `BlurRadius`/`Opacity` 需微调，
   允许有轻微差异。
6. **运行脉冲**：Web CSS `@keyframes`；WPF `Storyboard`。周期同为 1.6s，缓动曲线可能略不同。
7. **箭头**：Web 用 SVG `marker`；WPF 用 `Polygon`/`Path` 三角形，尺寸约 6×6，视觉重量略轻。
8. **画布尺寸**：本页示例是 1200 宽组件级画布，不是 1920 Shell；嵌入 Shell 时按现有画布的
   1.18 放大或真实缩放，不写死 1200。
9. **页面装饰**：页面背景的径向渐变与点阵只属于设计稿展示，不是 WPF 画布的一部分。
10. **分类口径**：`PARALLEL` 归在“路由 / 汇合”，不是“控制流”；这是刻意的分类
    （fork/join 是路由），视觉不变。若产品 taxonomy 要求全部容器归控制流，只搬分组、不改样式。

## 验证记录

- 静态：41 张算子卡、41 行 WPF 表、6 个族分组；示例 A 13 节点 / 12 连线、
  B 19 节点 + 1 容器 / 16 连线、C 21 节点 + 2 容器 / 18 连线；3 条 `ctl` 泳道。
- DOM：无重复 ID、无缺失 SVG 引用、无外部 URL 依赖、无节点越界、无 JS 异常。
- 浏览器（1600×1100）：首屏设计语言 + 算子总览、示例 A/B/C、状态矩阵、WPF 表逐屏截图复核。
- 本轮修掉的问题：
  - 泳道输出端口按容器坐标重复偏移，落到泳道外（改为相对泳道定位）；
  - 示例 C 的 PARALLEL 子节点输出未接到泳道底部端口（补两条内部线）；
  - 示例 B 的 D1/D2 与 CONS/RM 走线重叠/穿过 XOR 节点（改为分离的显式折线）；
  - 示例 A 的 D 与 M 在 x=240 共线、Q→NOT2 穿过 OUT1 节点（改为绕行）；
  - 示例 B 删除未使用的 IMG/RECIPE/TH 输入，D1/D2/CONS/RM 上移；
  - SWITCH 泳道从路由绿改为控制琥珀；选中态统一为全局蓝；
  - 设计画布隐藏滚动条。

## 未覆盖（下一批）

- WPF 实际实现：`DataTemplate`、字形 `ResourceDictionary`、逻辑画布控件、拖拽/选中/右键菜单。
- 与 Shell v2 流程画布的整合（运行条、检查器、控制台联动）。
- 嵌套组合示例：IF 内嵌 PARALLEL、WHILE + 超时、异常/重试路径。
- 触摸/笔、键盘导航、读屏名称与焦点顺序。
- 深色 / 高对比主题下的逻辑画布。

## 变更规则

设计基线确认后只读。需要调整设计时升版本（v2），并同步更新：

1. 本目录 `README.md` 的版本/日期/确认人；
2. `design.html` 的令牌与页面；
3. 已复刻的 WPF 页面与令牌映射表；
4. `docs/UI设计基线/README.md` 的现有基线索引；
5. v1 的截图/证据只作对照，不再修改。
