# 工业 HMI 四方案完整界面（hmi-console）

> 设计基线 · v6 · 2026-10-24
> 本文是设计媒介，**不是交付物**：不进构建、不打包、不部署、不作为第二客户端维护（AGENTS.md §30.1 / R126）。

## 一、这份基线是什么

把四套**可导入运行**的 `.vms` 组态方案（A 精密电子 BGA 焊球全检 / B 包装线全检追溯 / C 精密尺寸测量站 / D 精密运动控制工作站）的界面，
从"几张平铺卡片"升级为**工业触摸屏完整界面**；v2 新增**方案 D 精密运动控制工作站**；**v3 把 D 改为「轴控件搭配」**——全部运动操作落在 `Hmi.AxisConsole` / `Hmi.AxisPanel` / `Hmi.AxisPointTable` 三个真实预设上，删掉自绘的四轴卡、3×3 点动盘、独立点位表；**v4 按用户裁定给轴控件加两个属性**（`JogStepMm` 步长、`ShowStepSelector` 步进选择行），并单独出一屏 `V4-01` 把改动前后的三种形态渲染出来：

- 同一套公共壳（A 顶栏 / B 左导航 / C 主工作区 / D 右信息栏 / E 底栏）；
- **v1**：A / B / C 各 6 页 = 18 屏；**v2**：+ D 方案 7 页 = 25 屏；**v3**：屏数不变，D 的 7 屏按轴控件重做；**v4**：+ `V4-01 轴控件步进` 设计专页 = **26 屏**；
- 画布 **1920 × 1080**（现场触摸屏档位，比既有 `scada-suite` 的 1440×900 高一档）；
- 每屏的每个区块都标了对应的真实图元 `TypeKey`，仓库里没有的控件标橙色「候选」。

| 项 | 值 |
|---|---|
| 版本 | v6 |
| 确认日期 | 2026-10-24（按用户裁定把轴控件点动模式定稿，新增 `V6-01`） |
| 确认人 | 待用户确认 |
| 覆盖范围 | A / B / C 三方案 x 6 页 + D 方案 x 7 页 + `V6-01` 设计专页 = **26 屏**；运行态 (Runtime) 界面，不含组态编辑器 |
| 入口文件 | `design.html` |
| 证据目录 | `evidence/`（26 张 1920x1080，`01`~`18` 为 A/B/C，`19`~`25` 为 D； `27-axis-jogmode-v6.png` 为 v6 设计专页（1620x654，同名 `27-onepage.html` 隔离渲染页） |
| 落地代码 | `tools/vms-generator/Program.CcgBgaInspect.cs` / `Program.CcgPackLine.cs` / `Program.CcgMeasureStation.cs`（**D 方案尚未写生成器**）；`JogMode` + `JogStepMm` 已于 2026-10-25 落地到 `Scada.Controls\Elements\AxisPanelElement.cs` 与 `Themes\AxisPanelElement.xaml`，断言 `ScadaChecks` 的 `[AX12]` 段 24 条全绿 |

## 二、怎么打开

直接双击 `design.html`（单文件自包含，无外部依赖，图片按相对路径引用仓库 `Image/images` 原图）。

- **评审视图**：顶部两维页签——先选方案（A / B / C / D / V4），再选页面（1 总览 / 2 点动 / 3 轴参数 / 4 监控 / 5 追溯 / 6 手动 / 7 配方 / 8 报警 / 9 轴控件步进）。
  「2 点动」「3 轴参数」是 D 方案专有页，在 A/B/C 下点了会回落到该方案的总览；「9 轴控件步进」只有 `V4` 方案有，选 A/B/C/D 时点了会回落到第 1 页；
- **左导航可点**：每屏左侧导航都能跳，和真实运行界面一样（D 各页多出「点动与回零」「轴参数」两项）；
- **单屏截图**：`design.html?shot=1#app-a/overview`（隐藏页头、页签、说明区，只留 1920×1080 画面）；
  锚点命名 `#app-<方案字母>/<页面名>`，页面名取 `overview` `monitor` `trace` `manual` `recipe` `alarm`，
  D 方案另有两个专有页名 `jog` `axisparam`（例：`#app-d/jog`）；v4 专页锚点是 `#app-v4/v4`（即 `v4` 方案的 `v4` 页）。
  **注意**：`V4-01` 那张证据图是从隔离渲染页 `evidence/26-axis-jogstep.html` 截的（把该屏钉成 `body.shot` + 只有它 `display:block`），
  因为带 `?shot=1` 的整页脚本在切到 `v4` 方案时会按默认回落逻辑显示 A 屏。

## 三、壳的分区与硬约束

```text
┌──────────────────────────────────────────────────────────────┐
│ A  TOP BAR 72px   站点 / 运行状态 / 模式 / 当前步 / 节拍 / 报警 / 操作员 / 时钟 │
├────────┬──────────────────────────────────┬──────────────────┤
│ B NAV  │ C WORKSPACE                      │ D RAIL 340px     │
│ 208px  │ 当前页工作面                       │ 报警 → KPI → 明细 │
├────────┴──────────────────────────────────┴──────────────────┤
│ E  STATUS BAR 32px   连接 / 模式 / 方案名 / 版本                │
└──────────────────────────────────────────────────────────────┘
```

| 区 | 宽度 | 只放什么 | 不许放什么 |
|---|---|---|---|
| A 顶栏 | 72px | L1 状态：站点、运行状态胶囊、模式胶囊、当前步、节拍、本件判定、报警 chip、操作员、时钟 | 页面级按钮、参数 |
| B 左导航 | 208px | 固定 8 项 + 分隔 + 退出/返回；报警项带未确认徽标 | 随页面变化的临时入口 |
| C 主工作区 | 剩余 | 当前页的判断与操作，卡片统一「标题 + 右上 chip + 内容」 | 跨页常驻信息 |
| D 右信息栏 | 340px | 报警 → KPI/状态 → 明细/权限/跳转，顺序固定 | 把报警压到最下 |
| E 底栏 | 32px | 连接延迟、模式、方案名、版本、时钟 | 任何按钮 |

左导航 8 项固定：`总览 / 监控画面 / 数据追溯 / 手动操作 / 工艺参数 / 报警中心 / 趋势分析 / 系统维护`。

## 四、信息层级 L1 ~ L4

| 层 | 问题 | 落点 |
|---|---|---|
| L1 | 正不正常 | 顶栏状态 + 总览页判定大字 + 报警 chip |
| L2 | 哪里不正常 | 总览页测点/工位/链路 + 报警中心列表 |
| L3 | 偶发还是趋势 | 趋势图、CPK、报警统计 |
| L4 | 怎么处置、怎么追溯 | 报警 Cause/Remedy + 数据追溯 + 留痕落点 |

## 五、四方案页面矩阵

| 页面 | A · BGA 焊球全检 | B · 包装线全检追溯 | C · 精密尺寸测量站 | D · 精密运动控制站 |
|---|---|---|---|---|
| 1 总览 | 14×14 焊球阵列 + 判定 + 节拍良率 | 三工位链路 + 判定 + 产线 KPI | 四路测点表 + 判定 + CPK 趋势 | 四轴卡片 + 龙门机构示意 + 速度/跟随误差 + 节拍时间轴 |
| 2 监控画面 | 实时图 + 金样对照 + 缺陷列表 | 三工位现场图 + 逐工位参数 | 2×2 四张现场图 + 逐项读数 | 实时图 + 对位六格 + 相机六宫格 + 单步调试 |
| 3 数据追溯 | 8 列 SN 明细 | 8 列件级追溯 + MES 上传 | 10 列测量报表 + CPK | 10 列（SN/型位 XY ZR/对位分/耗时/结果）+ 失败原因 Top5 |
| 4 手动操作 | 三轴点动盘 + 单设备调试 | 工位单动表 + 输送/剔除 | 轴控 + 快测 + 重新标定 | **点动与回零专页**：`AxisConsole` × 2（展开态 + 收起态）+ `AxisPanel` + 轴速倍率与步进（候选）+ 回零四步 + 四轴一览表 |
| 5 工艺参数 | 检测阈值 + 分级规则 | 三工位阈值 + 判定规则 | 公差表 + 判定规则 | **轴参数专页**：轴参数总表 + DI/DO 位图 + 跟随误差/负载率 + `AxisPanel` 参考 + 传动链 + 报警码速查 |
| 6 报警日志 | 报警条 + 列表 + 统计 + 处置 | 同壳，绑产线变量 | 同壳，以"接近公差"预警为主 | 报警清单 + 跟随误差波形 + 处置建议 + 审计 |
| — | — | — | — | 运动配方页（7）：3 套配方 + 配方明细 + 节拍双条对比 + 变更差异 |

D 方案专有落点（v3 起：**运动操作全部由轴控件承担**，下表只列"控件之外"的配套）：

| D 的区块 | TypeKey | 说明 |
|---|---|---|
| 轴控主台（完整） | `Hmi.AxisConsole` 320×470 | 默认预设；`ShowPoints` / `PointsRows` / `JogVelocity` / `ConfirmCommands` / `AccentColor` 可配 |
| 轴控副台（紧凑） | `Hmi.AxisPanel` 280×280 | 同控件第二预设（`ShowPoints=false`）；`ShowCommands=false` 可退化为纯监视 |
| 点位表 | `Hmi.AxisPointTable` 340×300 | 同控件第三预设，只出点位表 |
| 轴速倍率与步进 | `Hmi.SelectorSwitch` × 2 + `Hmi.TextField` | **橙色候选**：控件只有 `JogVelocity` 一个速度值，没有步长/倍率界面 |
| 回零顺序与互锁 | `Hmi.Panel` + `Hmi.Indicator` × 4 | 控件只提供"长按 0.5 s 回零"一个入口，顺序编排在流程侧 |
| 紧急停止 | `Hmi.BitButton`（红） | 常驻可见，不做隐藏 |
| 四轴一览表 | `Hmi.DataGrid`（`GridColumns`） | 只读汇总，点击行切轴 |
| 跟随误差/负载率波形 | `Hmi.MultiTrend` | 两条曲线 + 阈值虚线 |
| DO/DI 位图 | `Hmi.Lamp` 或 `Hmi.Indicator` 阵列 | 每位一个布尔变量 |
| 传动链参数 | `Hmi.Panel` + `Hmi.Text` | 导程 / 编码器 / 每脉冲当量 |
| 节拍时间轴 | `Hmi.ProgressBar` × 5 段 + `Hmi.Text` | 每段一个时长变量 |
| 龙门机构示意 | `Hmi.Panel` + SVG | 数值与轴控件同源，不自成一套 |

## 六、令牌映射（CSS → XAML）

设计稿的深色令牌沿用 `docs/UI设计基线/industry-templates/scada-suite/` 的 25 个 `--c-*`，落到 WPF 时按 R117 只引令牌、不写 hex：

| CSS 变量 | 语义 | 建议 XAML 令牌（`UI/Controls/Themes/Colors.xaml`） |
|---|---|---|
| `--c-bg` `#0E1318` | 画面底 | `HmiSurfaceRoot` |
| `--c-surface` `#151C23` | 卡片底 | `HmiSurfaceCard` |
| `--c-surface-2` `#1B242D` | 卡片内小件 | `HmiSurfaceRaised` |
| `--c-border` `#2A3745` | 卡片描边 | `HmiBorderSubtle` |
| `--c-text` `#E6EEF7` | 主文字 | `HmiTextPrimary` |
| `--c-text-2` `#A9B7C6` | 次文字 | `HmiTextSecondary` |
| `--c-text-3` `#7C8A99` | 辅助文字 | `HmiTextTertiary` |
| `--c-accent` `#38BDF8` | 强调/选中 | `HmiAccent` |
| `--c-success` `#22C55E` | 正常/OK | `HmiStateOk` |
| `--c-warning` `#F59E0B` | 警告/复检 | `HmiStateWarn` |
| `--c-danger` `#EF4444` | 故障/NG | `HmiStateAlarm` |
| `--mono` | 等宽数字 | 复用现有数字字体令牌 |

> 具体键名以 `Colors.xaml` 现有条目为准；新增令牌只加在该文件（R140 第一层令牌）。

## 七、图元映射（已实现）

全部来自 `Scada.Controls/Descriptors/BuiltInElements.cs`（40 个内置图元）：

| 设计区块 | TypeKey | 关键属性 |
|---|---|---|
| 卡片/分区底板 | `Hmi.Panel` | 可嵌套子图元，超界裁切，不支持二层嵌套 |
| 标题、判定大字、SN | `Hmi.Text` | `Text` / `FontSize` / `Foreground` / `FontWeight`；**绑变量时必须给非空占位** |
| 主操作按钮 | `Hmi.Button` | 事件 Hook → Action |
| 单动/位控按钮 | `Hmi.BitButton` | 瞬时或保持 |
| 状态点 | `Hmi.Indicator` | 绑 `IsOn` |
| 三色灯 | `Hmi.Lamp` | 绑 `State` |
| 时钟 | `Hmi.Clock` | — |
| 数值只读 | `Hmi.IOField` | 绑 `Value` + `ValueFormat` + `Unit` |
| 文本输入 | `Hmi.TextField` | 绑 `Value` 或手填 |
| 枚举选择 | `Hmi.ComboBox` | 必须走 `StepConfigOption.ForEnum` 显示中文 |
| 多档旋钮 | `Hmi.SelectorSwitch` | 2~4 档保持式 |
| KPI 四联 | `Hmi.KpiBoard` | `Columns` 2~4 + `Label1..4` / `Value1..4` / `Unit1..4` |
| 多曲线趋势 | `Hmi.MultiTrend` | `MaxSeries` / `SeriesLabel1..` / `WindowSeconds` / `AutoScale` |
| 数据表格 | `Hmi.DataGrid` | **列键是 `GridColumns`**（JSON：Header / Variable / Width / Align / Format / Monospace）；`SampleVariable` 吃上升沿采行；`MaxRows` 默认 100；最多 12 列 |
| 实时图像 | `Hmi.ImageView` | 绑**图像变量**（键 `Source`，可绑），`Stretch` + `PlaceholderText` |
| 静态图 | `Hmi.Picture` | 键 `SourcePath`，**不可绑**，走方案内相对路径 `Image\xxx.png` |
| 轴控（完整） | `Hmi.AxisConsole` | 320×470 预设（`ShowPoints=true`）：轴选择器 + 位置大字 + 8 灯 + 6 命令键 + 点位表。可配 `ShowCommands` / `ShowPoints` / `PointsRows` / `JogVelocity` / `ConfirmCommands` / `AccentColor` 等；**v4 拟加** `JogStepMm` / `ShowStepSelector` |
| 轴控（紧凑） | `Hmi.AxisPanel` | 280×280 预设（`ShowPoints=false`）：同类的监视 + 手动版；`ShowCommands=false` 退化成纯监视面板 |
| 点位表 | `Hmi.AxisPointTable` | 340×300 预设：只出点位表（点位/位置/速度/示教/定位），**不是"旧图元"**，与上两者同一控件类的第三预设 |
| 配方面板 | `Hmi.RecipePanel` | 只读 |
| 报警条 | `Hmi.AlarmBanner` | `MaxRows` / `EmptyText` / Normal/Info/Warning/Critical 四色 |
| 报警确认 | `Hmi.AckButton` | `Scope=All` 或单条 |
| 流程/工艺图 | `Hmi.Tank` `Hmi.Pump` `Hmi.Valve` `Hmi.Pipe` `Hmi.Motor` | B 方案链路图可用 |
| 条码/二维码 | `Hmi.Barcode` `Hmi.QRCode` | 读码页展示 |
| 进度/计时 | `Hmi.ProgressBar` `Hmi.RunMeter` | 班次进度、节拍 |

**候选（仓库暂无，不当作已实现）**：柱状图、工位复合卡（一卡多指标 + 迷你趋势）、软键盘、**多方向点动盘（3×3）**（轴控件只提供 `◀ 点动` / `点动 ▶` 两向）。**注意**：v4 已把「步长 / 倍率」从候选提为**拟新增属性**，见下节。

### 七之二、轴控件新增「点动模式」（v6 定稿，**已于 2026-10-25 落地到控件**）

**为什么是"一个模式"而不是两个属性**：v4 先做成 `JogStepMm` 步长 + `ShowStepSelector` 步进选择行两个属性，并把三种形态分开渲染；用户当场否掉——「控件上你直接加个模式 连续点动 单步点动不就行了吗 不需要分两个来渲染 档位在0到20之间就行」。v5 改成单属性 + 六颗档位按钮，也被取代：**档位最终是"可输入数值框"，不是按钮组**。

**最终口径（三条，逐字来自用户裁定）**：

| # | 口径 | 落地 |
|---|---|---|
| 1 | 档位是**可输入数值框**，精确到 **0.1 mm**，范围 **0 ~ 50 mm** | `<input>` 52×20 + 上下微调箭头 + `mm` 单位；越界立红框，回读到上一个有效值，不发命令 |
| 2 | 单步点动的目标是 **当前位置 ± 档位** | `TryMove(axisId, 当前位置 ± JogStepMm, JogVelocityMmPerS)` |
| 3 | **运动指令执行中点动一律禁止**（连续与单步都禁） | 只锁两个点动键：`CanJog = CanOperate && !IsMoving`；停止 / 清报警 / 失能 / 回零**照常可点** |

**属性只多两个**（都挂在 `AxisPanelElement` 上，三个预设一起继承）：

| 属性 | 键 | 类型 / 默认 | 范围 | 作用 |
|---|---|---|---|---|
| 点动模式 | `JogMode` | 枚举 `ScadaJogMode { Continuous, Step }` / `Continuous` | — | `Continuous` = 按住走、松开停（现状）；`Step` = 点一下走一格 |
| 点动档位 | `JogStepMm` | Double / `1.0` | 0 ~ 50，步进 0.1 | 只在 `Step` 模式下参与计算；`Continuous` 下输入框灰、不可编辑 |

**旧方案兜底**：`.vms` 里缺 `JogMode` / `JogStepMm` 键时按 `Continuous + 1.0` 补，行为与现在完全一致（老方案打开即用）。

**四种形态（`evidence/27-axis-jogmode-v6.png`，即 `V6-01` 屏）**：

| 形态 | 尺寸 | 样子 |
|---|---|---|
| ① 连续 · 静止 | 320×470（Console 预设） | 点动行：`连续(亮) 单步` + 档位框灰 `1.0 mm` + 右侧 `按住 JogVelocity`；两键无角标 |
| ② 单步 · 1.0 mm | 同上 | 点动行：`连续 单步(亮)` + 框可编辑 `1.0` + `mm` + `−3 格 · 3.000`；两键角标 `−1.0` / `+1.0`；结果行「点动 +1.0 mm 已到位 · 目标 129.480」 |
| ③ 运动中 | 同上 | 点动行整块淡化 + 一行橙字「运动中 · 两个点动键已锁（到位或停止后自动放开）；停止、清报警不受影响」；**只有 ◀点动 / 点动▶ 两键变灰**，失能 / 回零 / 停止 / 清报警保持彩色可点 |
| ④ 紧凑预设 · 档位越界 | 280×280（Panel 预设） | 档位框红边 + `62.5` + 红字 `超出 50`；结果行「档位「62.5」超出 0~50 mm，请改到范围内」；同样只灰两个点动键 |

**落点与约束（图里同表）**：插在 `RootGrid` 的 Row3（速度行）与 Row4（命令按钮区）之间，新一行 `.axp-jogmode`（28 px；运动中再加一行 18 px 的 `.axp-busybar`，仅运行时出现）；Console 470 → **500**、AxisPanel 280 → **310**；档位框 `0.0 ~ 50.0`、步进 `0.1`、方向键 `▲▼` 每次 ±0.1；越界即红框 + 回读上一个有效值，**不发命令**；单步目标是 `当前 ± 档位`，完成才更新大字；连续模式框灰不可编辑，两键无角标；运动锁只作用两个点动键与点位「示教/定位」按钮，`CanOperate` 继续绑外层容器；停止三路松手语义不变。

**为什么运动中也禁止「连续点动」**：连续点动本质是"按住就一直走"，与正在执行的定位/回零是**两条命令抢同一根轴**——谁后到谁改写速度，先到的可能永远到不了位，日志还会显示两条都"成功"。所以在 `CheckCanRun` 里加一条运动锁（读轴快照的 `Moving`），比让操作员记"这轴正忙着别按"可靠。

**改动边界（图右栏）**：`AxisPanelElement.cs` +1 枚举 + 1 数值 + 运动锁；`Themes/AxisPanelElement.xaml` +1 行 + 2 段样式；`BuiltInElements.cs` 两个预设默认高度；`ScadaChecks` +5 断言；其它图元 0 影响。枚举 `ScadaJogMode { Continuous, Step }`；键 `JogMode` / `JogStepMm`（Number，Min 0，Max 50，步进 0.1）。单步走 `IScadaAxisCommandSink.TryMove(axisId, target, JogVelocityMmPerS)`——**这个接口已经存在，不需要给命令枚举加新成员**。

**五条断言（`ScadaChecks`）**：① 连续模式：按下收到 `JogStart(value=+JogVelocity)`，松手 `JogStop`（反面：档位填 5 不影响速度）；② 单步 +1.0：按一次 → **恰好一次** `TryMove`，`target = 当前位置+1.0`，不松手也不重复发；③ 运动中（快照 `Moving=true`）：**两个点动键禁用、零命令**，`CanJog=false` 而 `CanOperate=true`；④ 档位 0 / 62.5：只灰点动两键、零命令，填 50.0 边界值可发；⑤ 运动中「停止」「清报警」「失能」「回零」**仍为 enabled**，且「停止」点击必须发出 `Stop`（反向钉住"别把安全动作锁掉"）。

**证据**：`evidence/27-axis-jogmode-v6.png`（1620×654，`--force-device-scale-factor=0.9` 下整屏可见）+ `evidence/27-onepage.html`（隔离渲染页）。

**落地实况（2026-10-25，代码已改，断言已绿）**：

| 项 | 实际落点 | 说明 |
|---|---|---|
| 枚举 | `Scada\Models\IScadaAxisCommandSink.cs` → `public enum ScadaJogMode { Continuous = 0, Step = 1 }` | 与命令枚举同文件，避免为它单独开一个文件 |
| 属性 | `AxisPanelElement.JogMode`（`Key="JogMode"`，`Kind=Choice`，`DefaultValue="Continuous"`）、`AxisPanelElement.JogStepMm`（`Key="JogStepMm"`，`Kind=Number`，`DefaultValue="1"`，`Min=0`，`Max=50`） | 三个预设一起继承 |
| 档位归一 | `NormalizeJogStep(double)` + `JogStepMaxMm = 50` + `JogStepQuantumMm = 0.1` | 只负责量化到 0.1 粒度 |
| 越界判据 | `CanStepJog` 判**原始值** `JogStepMm > 0 && JogStepMm <= 50` | **不能用夹取后的值**，否则 62.5 会被静默夹成 50 照发命令 |
| 运动锁 | `CanJog = CanOperate && !IsMoving`，只绑两个点动键 | `CanOperate` 继续绑外层容器 |
| 单步执行 | `StepJog(direction)` → `RuntimeContext.AxisCommands.TryMove(axisId, 当前 ± 档位, JogVelocityMmPerS)` | 单步本身就是一条完整定位，**不发 `JogStop`** |
| 位置回读 | `TryParsePositionMm`（`InvariantCulture`） | 位置是 `"—"` 时明确拒绝，不猜 |
| 预设尺寸 | `Hmi.AxisConsole` 470→**500**、`Hmi.AxisPanel` 280→**310** | `Hmi.AxisPointTable` 340×300 不动（只出点位表） |
| 断言 | `ScadaChecks\AxisPanelChecks.cs` 的 `[AX12]` 段，**24 条** | 含两条反向断言 |

**落地时发现并修掉的两个真缺陷（设计稿看不出来的）**：

1. **`OnElementRefreshed()` 只刷权限、不刷数据快照**。`panel.Refresh()` 走基类 `RefreshCore → OnElementRefreshed`，而运动锁是从 `IsMoving` 推导的——只补 `RefreshCanOperate()` 会让 `IsMoving` 停在旧值，于是**轴其实在动、点动键还亮着**。这属于会让机构抢轴的错误，不是观感问题。修法：`OnElementRefreshed` 里补 `RefreshFromSource()` + `RefreshJogAvailability()`。
2. **断言匹配的文案要对得上控件原文**。控件写的是「轴正在**运动**，点动已锁定（到位或停止后自动放开）」，断言原本写 `Contains("运动中")` 永远为假。教训：写 `Contains` 前回源码确认原文。

**断言顺序的一条教训**：反向断言（清掉 `moving` 后同一个键必须能发出去）必须放在段**末尾**。放中间会把状态清掉，后面「`CanJog` 关闭」「橙色提示条」两条读到清除后的值，变成假红。

### 七之一、方案 D 的「轴控件搭配」落点（v3）

D 方案的全部运动操作都落在**同一个控件类的三个预设**上，不另画一份：

| 画面位置 | 落点 | 预设 / 关键属性 |
|---|---|---|
| D-01 主区左 | `Hmi.AxisConsole` 320×470 | 默认预设（`ShowPoints=true`，`PointsRows=6`） |
| D-01 主区中 | `Hmi.AxisPanel` 280×280 | `ShowCommands=true`（可操作），`ShowPoints=false` |
| D-01 右栏 | `Hmi.AxisPointTable` 340×300 | 只出点位表，与左侧 Console 同一份点位数据 |
| D-02 左 / 右 | `Hmi.AxisConsole` × 2 | 左 = 展开态（`ShowPoints=true`），右 = 收起态（`ShowPoints=false`），演示"同控件两态" |
| D-02 右栏 | `Hmi.AxisPanel` 280×280 | 报警轴的紧凑监视 |
| D-03 右栏 | `Hmi.AxisPanel` 200×200 级 | 参数页只做"当前轴状态"参考，不做第二套操作台 |
| D-04 右栏 | `Hmi.AxisPanel` 280×280 | 视觉页的轴位置回显 |
| D-05 右栏 | `Hmi.AxisPanel` 280×280 | 配方页的"安全抬升位"回显 |
| D-06 右栏 | `Hmi.AxisPanel` 280×280 | 报警详情里的故障轴（`CommandMessage` 显示拒绝原因） |
| D-07 右栏 | `Hmi.AxisPanel` 280×280 | 历史回放：`CanOperate=false`，命令键全灰 |

**不重复画的（控件自带）**：轴选择下拉、位置大字 + 单位、8 个状态灯（使能/到位/运动/原点/报警/正限位/反限位/急停）、6 个命令键（使能/点动反/点动正/回零/停止/清报警）、操作结果文案、点位表（名称/位置/速度/示教/定位）。

**控件没有、本稿标橙色候选的**：多方向点动盘、步长×倍率×目标位置输入、点位表批量编辑。

## 八、图像采集怎么处理

1. 流程侧统一用**图像采集插件的 Folder 模式**（`FolderPath` + `FileIndex` 逐张轮播，每轮重新列目录），一个图库文件翻一页就是一条产环节拍；
2. 画面上用 `Hmi.ImageView` 绑**图像变量**显示实时图，由运行态数据泵把 `HALCON HImage` 转成位图刷新（`ScadaValueSource` 那一层做转换）；
3. 静态对照图（金样、标定板）用 `Hmi.Picture` 绑方案内相对路径——`Image\bga_14x14_model.png` 这一级，**绝不写绝对路径、绝不引用方案目录之外的文件**；
4. 整包方案目录（`.vms` + `Image\` + `Alarms\` + `History\` + `Logs\`）可整体拷贝搬迁，路径不断链。

## 九、已知偏差（逐项列明，按 R129/R130 要求）

| # | 偏差 | 影响 | 是否可接受 |
|---|---|---|---|
| 1 | 字体度量：浏览器与 WPF 渲染中文行高略有差异 | 卡片高度可能有 ±2px 出入 | 可接受，复刻时以 `MinHeight` 兜底 |
| 2 | 滚动条宽度：设计稿用浏览器原生滚动条 | 表格右侧占位不同 | 可接受，WPF 套自定义样式 |
| 3 | `Hmi.ImageView` 在探针离屏渲染里恒显示"无图像"占位 | 设计稿无法证明"真出图" | **需真机验证**（`VisionMaster.exe`） |
| 4 | `Hmi.KpiBoard` 设计态数值为空串 | 本稿用真实数字示意 | 可接受 |
| 5 | `Hmi.AxisConsole` / `AxisPanel` / `AxisPointTable` 的内部布局按 `Scada.Controls/Themes/AxisPanelElement.xaml` 1:1 复刻（位置大字 30px、8 灯、2×3 命令键、点位行 22px、`PointsHeight` 不含表头） | 本稿已是像素级示意；真机字体度量差异仍在 | 可接受 |
| 6 | 趋势图 / 链路图 / 焊球阵列为 SVG 示意 | 真机是运行时绘制 | 可接受 |
| 7 | D 的龙门机构示意是 SVG 几何拼图，不是 `Hmi.Panel` 内嵌真实 3D | 真实设备需按机械图重绘 | **需用户裁定**是否要做 |
| 8 | D 的「轴速倍率与步进」「回零顺序与互锁」「紧急停止与操作审计」三块是 `SelectorSwitch` + `Panel` 自建，标橙色候选 | 轴控件没有步长/倍率界面 | 可接受，真机可只用 AxisConsole 自带的点动 |

## 十、待拍板

1. **表格样式**：多工位/多信号表格是否统一成"深色等宽默认列 + 状态色 chip"？
2. **主题**：深色（本稿，沿 scada-suite）还是浅色？车间强光环境是否需要浅色主题？
3. **左导航 8 项**：是否够用，是否需要加"配方管理 / 用户管理"？
4. **报警确认**：确认后是否允许"一键全部确认"？（现场一般要求逐条）
5. **手动操作页**：是否需要实体硬按键与屏幕按钮一一对应？
6. **D 方案要不要落成 `.vms`**？要的话是独立一套「CCG-精密运动控制站」，还是并入 A/B/C 之一？
7. **D 的机构示意**：SVG 几何示意够用，还是要按真实机械图做成可拆解的分层图？
8. **v3 的轴控件改动已按你的裁定做完**；v4 按裁定**已把 `JogStepMm` 步长、`ShowStepSelector` 渲染出来**（`V4-01` / `#app-v4/v4`），三个新问题要你拍：
9. **档位刻度**：`0.01 / 0.1 / 1 / 10 / 100` 五档够不够？要不要改成 `1/10/100 µm` 精细档（机械上 ZMotion 每脉冲 0.0763 µm 支持，但触摸屏上 0.001 mm 的点动看不太出位移）？
10. **手感分界**：单击走一格 / 按住超过 **300 ms** 转连续——300 ms 对现场操作员是否合适？（回零长按用的是 0.5 s，两者不一致是否要统一？）
11. **倍率要不要独立属性**（`JogRateOptions`）？现在倍率是派生值（`×2` 由「1 档 ×2」算出来），只是显示；如果要让操作员改倍率，就必须再加一个属性，`[ScadaProperty]` 从 2 个变 3 个。

## 十一、变更规则

- 确认后冻结 v4；改设计必须**v4→v5 并同步 `design.html` 版本徽标、`evidence/` 截图、上级 `docs/UI设计基线/README.md` 索引、以及已经复刻的 `.vms` 页面；
- 本目录不参与构建、不进 `.sln`、不进打包脚本；
- 与仓库其它约束（R30~R32 资源作用域、R68 图标按钮、R117 令牌化、R140 五档颜色豁免）冲突时，以 AGENTS.md 为准。
