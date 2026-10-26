# 全量控件审查（剩余 42 个）· SCADA 应用工程师 × 前端工程师

| 项 | 内容 |
| --- | --- |
| 版本 | **v1** |
| 日期 | 2026-10-26 |
| 覆盖 | 除已审过的 9 个（Button / IOField / Slider / DataGrid / AxisPanel / AxisConsole / AlarmBanner / Report / BarChart / PieChart）之外**全部**工具箱图元，按 8 个族分组 |
| 阅读版 | [review.html](review.html)（页签式：文本与显示 / 数值与仪表 / 状态与操作 / 容器与导航 / 图形与装饰 / 工艺与设备 / 数据与配方 / 媒体与码 / 共性结论） |
| 结论 | **3 条 P1**、20 余条 P2/P3；不建议一次性全改 |

## 覆盖清单（42 个）

- 文本与显示：`Text` `TextField` `Clock` `RunMeter` `Indicator` `Lamp`
- 数值与仪表：`Gauge` `ProgressBar` `KpiBoard` `MultiTrend` `ChartTrend` `BitTable` `IoMonitor` `VariableMonitor`
- 状态与操作：`BitButton` `SelectorSwitch` `StateView` `AckButton` `ComboBox`
- 容器与导航：`Panel` `SubPage` `Picture` `ImageView` `Gif`
- 图形与装饰：`Rectangle` `Ellipse` `Line` `Pipe` `Polygon` `Bezier` `DynamicLine`
- 工艺与设备：`Tank` `Pump` `Valve` `Motor` `Symbol` `PipeRun`
- 数据与配方：`RecipePanel` `AxisPointTable`
- 媒体与码：`QRCode` `Barcode`

## 三条 P1（本轮应改）

| 控件 | 问题 | 判据 |
| --- | --- | --- |
| `Hmi.Indicator`（含 `Lamp`） | **没有"断线/未知"第三态**：通讯断了仍显示上一次状态 | 断言：无值/未解析时状态为 `Unknown`（灰 + 斜纹），不是沿用旧值 |
| `Hmi.StateView` | 一个 TypeKey 三预设，**互斥字段同现属性面板**（撞 R107） | 断言：任一预设下属性面板只出现该预设的字段（查可见属性集，不只查对象类型） |
| `Hmi.PipeRun` | 可拖动 / 可旋转 / X-Y-W-H 可编辑，但会被路由器改写回去 | 断言：管线几何只读、`CanDrag=false`；改尺寸的操作被拒并说明"由系统计算" |

## 四条共性结论

1. **触摸目标尺寸无统一口径**（轴面板 44、点位表 22、位表格子更小）→ 建议全局令牌 `HmiTouchTarget`；
2. **"断线/未知"缺第三态**（指示类、部分设备符号）→ 假状态比无状态更危险；
3. **同一语义两套画法**（`Pump`/`Valve`/`Motor` vs `Symbol`）→ 新工程只用 `Symbol`，旧的标"旧"；
4. **编辑期可改性无统一规则**（`PipeRun` 典型）→ 建议 `IsLayoutLockedByCode` 语义，属性面板据此置灰。

## 下一步（按性价比）

1. `HmiTouchTarget` 令牌 + 可点图元引用（一次解决 22/29/32/44 混用，可加静态闸）；
2. `PipeRun` 锁定几何；
3. 指示类加 `Unknown` 态；
4. `StateView` 互斥字段拆分（R107）；
5. P2 清单按"现场是否在用"排期——**不建议为没人用的控件铺开改**。
