# SCADA 属性面板 · Web 设计基线

| 项 | 内容 |
| --- | --- |
| 当前版本 | **v2.2（已复刻，待 R132 验收）** |
| 日期 | 2026-10-28 |
| v2.1 修订 | 2026-10-28（同日，按用户意见）：① 移除「基本」组「类型」行（类型键只在对象头显示）；② 曲线卡绑定态改为行下轻量小字标注（去边框底色） |
| v2.2 修订 | 2026-10-28（同日，按用户意见）：颜色行**不再显示十六进制文字**，只留色块（40×24）+ 取色；精确值只在取色板的十六进制输入框内出现 |
| 确认日期 / 确认人 | 待用户按 R132 验收 |
| v1 | 2026-10-27 出稿（忠实复刻现有 WPF 的保守稿），已归档为 `design-v1.html`；复审结论与六条结构性问题见 `评审-2026-10-28-v1复审与v2重构方案.md` §一 |
| 覆盖范围 | `VisionMaster\Views\ScadaPropertyView.xaml`（右侧 300px 停靠面板）、`VisionMaster\Views\Controls\ScadaEventEditor.xaml`（事件动作表）、`VisionMaster\ViewModels\ScadaPropertyViewModel.cs`（分组 / 行 / 空态）、`Scada.Controls\Descriptors\ScadaPageProperties.cs` / `ScadaPageEvents.cs` / `BuiltInElements.cs`、`Scada.Controls\Descriptors\ScadaPropertyGroups.cs` |
| 设计文件 | `design.html`（v2.2，自包含、可交互：对象切换 / 卡片折叠 / 折叠全部 / 事件与动画展开 / 取色板 / 搜索过滤 / 绑定演示；`?shot=` 单屏） |
| 证据 | 设计稿：`evidence/design-v2-01~08-*.png`（8 张 1:1）；v1 设计证据保留为 `design-v1-01~08-*.png`；**WPF 复刻证据：`evidence/wpf-v2/after-{indicator,page,button-event,trend,indicator-fluent}.png`**（5 张，260px 断言宿主渲染） |
| 复刻状态 | **已复刻（2026-10-28）**：`ScadaChecks` 3177/0、`FlowCanvasChecks` 4258/0（当前构建，`-p:BuildProjectReferences=false` 避开运行中 VisionMaster 的 Modules 锁）；`run_gates` 15/16，唯一红是 FlowCanvasChecks **构建**被运行中的 `VisionMaster.exe` 锁 `Modules\hdevenginedotnet.dll`（MSB3073 copy，非代码失败），关闭应用后重跑即可；待用户按 R132 验收 |

## 1. v2 一句话

对 v1 的彻底重构：**卡片化分组 + 三层深度 + 混合标签布局 + 绑定 chip + 事件/动画折叠卡**。
信息架构动两处：① 多曲线趋势的 4 条曲线收成「曲线卡」（外观组 8→4、数据组 11→7）；② 「基本」组的「类型」行移除（类型键只在对象头显示，等宽小字）。**落盘键全部不变。**

## 2. v2 设计原则（五条）

1. **三层深度**：雾白面板底 → 纯白卡片（淡蓝灰描边 + 极浅投影）→ 浅灰 filled 输入（无边框，聚焦才亮描边）。
2. **混合标签**：长文本上标签；成对数值一行两列；其余左标签 72px。三条规则消灭截断。
3. **复合配置卡片化**：事件/动画折叠卡——勾选管「配没配」、箭头管展开、摘要常显；动作/档位卡有序号与状态条。
4. **绑定即状态**：绑定后输入框本体变成变量 chip（ƒ + 变量名 + ✕），不另占一行；ƒx 实心。
5. **语义色收敛**：晴空蓝=可交互/焦点/选中；琥珀=待完成；赤红=错误/危险；苔绿=成功。

## 3. 布局结构（300px，纵向）

```text
对象头 52px：图标块（晨雾蓝底·主色线性图标）+ 名称(13/600) + 类型键(等宽) / 页面尺寸 + 锁定开关
工具行 48px：搜索（filled，全宽）+ 折叠全部/展开全部
滚动区：卡片组（白底·圆角10·淡蓝灰描边·极浅投影，卡距8）
  组头 38px：组名(12.5/600) + 计数胶囊 + 错误点(琥珀/赤红) + 箭头；整头可点折叠
  卡体：三种字段行 —— 左标签行(72px) / 上标签字段 / 成对网格；布尔行不占标签列
  复合卡：事件卡、动画行（勾选+摘要+箭头，配置中晨雾蓝底）；动作/档位卡（序号+状态条）
  曲线卡（趋势数据组）：4 行「颜色点+名称+值+ƒx」+ 时间窗/量程/图例
dock 页签 34px：属性 | 图层 | 图元工具箱（选中主色 + 2px 下划线）
```

三种字段行（规则只有三条）：① 标签 ≤5 字且控件矮 → 左标签行（72px 标签 + 编辑器 + 22px 槽位）；② 长文本（名称/文字/说明）→ 上标签；③ 模型上天然成对 → 成对网格（小标签 10.5px 在上）。

## 4. 配色（名称 / 视觉描述；编码只存在于 design.html 的 CSS 内）

| 名称 | 视觉描述 | 用途 |
| --- | --- | --- |
| 雾白 | 带一点冷调的近白 | 面板底 |
| 纯白 | 无色相的白 | 卡片、弹出层 |
| 浅灰 | 比卡面深半档的中性灰 | filled 输入底 |
| 淡蓝灰 | 极浅、略带蓝的灰 | 卡片描边、发丝分隔 |
| 墨黑 | 近黑的深灰蓝 | 主文字、工具提示底 |
| 石板灰 | 中等偏深的冷灰 | 次级文字、标签 |
| 雾灰 / 银灰 | 浅冷灰 / 更浅的灰 | 占位、单位 / 禁用 |
| 晴空蓝 | 明快沉稳的品牌蓝 | 主色：焦点、选中、主按钮、ƒx 激活 |
| 晨雾蓝 | 主色兑大量白的柔底 | 选中行、配置中的卡头、绑定 chip 底 |
| 赤红 / 琥珀 / 苔绿 | 砖红 / 橙黄 / 沉稳绿 | 错误 / 待完成 / 成功（预留） |
| 深蓝黑 · 浅晴蓝 | 近黑蓝 · 明亮天蓝 | 画布图元域（不进面板） |

## 5. 尺寸与排版

字阶 13 / 12.5 / 12 / 11.5 / 11 / 10.5（等宽只用于类型键）；输入高 28（紧凑 24/22）、卡头 38、对象头约 52、dock 页签 34；卡距 8、卡内字段距 7、卡体水平内边距 11；圆角卡 10 / 输入 7 / chip 6 / 弹出层 12；阴影两级（卡片浮起一层纸、弹出层明显悬浮）；图标 16px 线性 1.5px 圆头；动效 120–160ms ease-out，不做位移动画。

## 6. 状态

焦点（白底+主色描边+柔光环，外层容器换色，行不抖）/ 悬停（输入底加深、卡头雾蓝）/ 已配置（晨雾蓝底）/ 绑定（chip 替换输入 + ƒx 实心）/ 非法（赤红描边+浅红底+行下原因+组头赤红点）/ 待完成（琥珀左条+浅琥珀底+组头琥珀点）/ 只读（浅灰 chip+等宽，系统计算行加锁）/ 禁用（银灰）/ 搜索（命中卡强制展开、计数「命中/总数」、无结果空态卡）。

## 7. WPF 复刻要点

- 只改 `ScadaPropertyView.xaml` / `ScadaEventEditor.xaml` 与两个行 VM；**不改模型与 .vms**。
- 卡片 = Border(圆角10+描边+浅投影)；filled 输入 = 无边框模板 + 外层 Border 供底色与聚焦描边；三种行 = 三个 keyed DataTemplate（行 VM 加 `Layout: Left/Staked/Pair`）。
- 绑定 chip = Border+TextBlock+Button，`HasBinding` DataTrigger 替换编辑器（不增高）；事件/动画折叠卡 = Expander（keyed）双状态源（IsConfigured / IsExpanded）。
- 曲线卡 = 新复合行类型（参照 `EditorKind.Event/Animation`）；分组计数与 `ScadaChecks` 快照、手册截图同步（Q4）。
- 类型行移除（v2.1）：`ScadaTypeRow` 不再进面板；类型键由对象头 `HeaderSubtitle` 承担（等宽 10.5px 雾灰）。
- 新增令牌（进 `Colors.xaml`，R140，值由令牌评审定）：卡片底 / 卡片描边 / 输入底 / 输入悬停 / 主色柔底 / 柔光环 / 绑定 chip 三色 / 警告柔底 / 错误柔底 / 成功。
- 工具提示 = keyed `PropertyTooltip`（墨黑底白字 12px/1.45、最大宽 240、圆角 8）；图标按钮补 `AutomationProperties.Name`。
- 断言：卡圆角与间距、成对网格列宽、绑定 chip 替换、折叠卡双状态源、曲线卡 7 项、搜索强制展开、折叠全部（改断言先重建再跑，R65）；改完跑 `tools\run_gates.ps1`。

## 8. 已知偏差（Web vs WPF）

字体度量（微软雅黑 vs 设计稿渲染）、阴影层数（WPF 单层 DropShadow 模拟两级）、滚动条（自绘细滚动条先局部）、动效曲线、focus ring（外层 Border 实现）——逐条由用户按 R132 验收；卡片圆角在 AvalonDock 内无裁切问题（面板自身非圆角容器，卡是独立 Border）。

## 9. 如何查看 / 出图

直接打开 `design.html`；单屏截图 `?shot=<节>&only=<对象>`：

| 截图 | URL | 尺寸 |
| --- | --- | --- |
| 主稿 | `design.html?shot=s1` | 1280×860 |
| 画面属性 | `design.html?shot=s2&only=page` | 300×1100 |
| 按钮属性 | `design.html?shot=s2&only=button` | 300×1700 |
| 多曲线趋势 | `design.html?shot=s2&only=trend` | 300×1420 |
| 布局体系 | `design.html?shot=s3` | 1200×420 |
| 行与状态 | `design.html?shot=s4` | 1320×920 |
| 事件与提示 | `design.html?shot=s5` | 1100×680 |
| 搜索折叠空态 | `design.html?shot=s6` | 1320×560 |

本机命令（Edge 156 实测；**一次只跑一张**，批量循环会被忽略窗口尺寸——台账 H-02）：

```powershell
$url = [System.Uri]::new('D:\C#\VM\docs\UI设计基线\scada-property-panel\design.html').AbsoluteUri
# ↑ D:\C#\VM 的 # 必须编码为 %23（台账 H-01），不要手拼 file://
$psi = [System.Diagnostics.ProcessStartInfo]@{
  FileName='C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'; UseShellExecute=$false
  Arguments="--headless=new --disable-gpu --hide-scrollbars --no-first-run --user-data-dir=""$env:TEMP\edge-$(Get-Random)"" --force-device-scale-factor=1 --window-size=300,1100 --virtual-time-budget=3000 --screenshot=""<输出.png>"" ""$url?shot=s2&only=page"""
}
$p = [System.Diagnostics.Process]::Start($psi); $p.WaitForExit(25000) # 超时即杀，别等整棵树
```

## 10. 待裁定 Q1~Q6（v2 新引入）

| # | 问题 | 建议 |
| --- | --- | --- |
| Q1 | 卡片化分组（编辑器列约少 11px） | 接受：层次与折叠收益大于宽度损失 |
| Q2 | 混合标签布局（三种行模板） | 接受：截断与行数一次解决 |
| Q3 | 绑定 chip 替换输入（绑定后不显示当前值） | 接受；备选：chip 后附只读小字当前值 |
| Q4 | 曲线卡定稿（外观组 8→4、数据组 11→7；分组快照与手册截图同步） | 定稿：v2 唯一结构变化 |
| Q5 | 事件卡「勾选=存在」与箭头解耦 | 接受：与现有模型一致，只改呈现 |
| Q6 | 控件高 26→28 | 接受；要回 26 只改一个变量 |

v1 遗留的 Q1~Q8（标签列 80、颜色显示方式、事件摘要、动画摘要、组错误聚合、方案 B、滚动条作用域、交互组归属）已在 v2 全部给出默认实现，不再单列；其中颜色行在 v2.2 定为**不显示十六进制**。

## 11. 不做什么

不新增属性、不改分组名与组序（曲线颜色在面板上的归属变化是**呈现层**的复合行，落盘 Group 不变）、不改落盘值与 `.vms`、不做运行时主题切换、不改 Shell 停靠语义、不引入第二套色板或图标字体。

## 12. 相关文件

- `design.html` — v2.2 设计稿本体（自包含）。
- `design-v1.html` — v1 归档（对照用）。
- `评审-2026-10-28-v1复审与v2重构方案.md` — 评审结论、完整方案与代码实现要点。
- `evidence/` — v2 设计八张 + v1 八张 1:1 截图；`evidence/wpf-v2/` — WPF 复刻五张渲染证据。
- `docs/UI设计基线/README.md` — 基线总表。
- `docs/UI设计基线/property-grid/` — 框架级反射属性面板（**不同控件**，勿混淆）。
- `docs/UI设计基线/剩余视图设计稿-2026-10-22/` — 旧版粗略稿；视觉口径以本页 v2 为准。
- `docs/code-changes/2026-10-28-SCADA属性面板v2复刻（WPF）.md` — WPF 复刻落盘记录（R177）。

## 13. WPF 复刻实现记录（2026-10-28）

### 13.1 改动文件

| 文件 | 改动 |
| --- | --- |
| `UI\Controls\Themes\Colors.xaml` | 新增 v2 令牌：`ScadaCardBorderBrush` / `ScadaFieldBackgroundBrush` / `ScadaFieldHoverBrush` / `ScadaTooltipBackgroundBrush` / `ScadaTooltipTextBrush` / `ScadaCardShadowColor`（只在此文件，R140） |
| `VisionMaster\ViewModels\ScadaPropertyViewModel.cs` | 行基类加 `GroupOverride` / `UseStackedLayout` / `HasError` / `HasWarning` / `PairWith` / `PairOwner` / `IsPairedFollow` / `MatchesSearch` / `HasImage`；`ScadaPropertyGroup` 重写（`IsExpanded` / `HasError` / `ShowWarningDot` / `CountText` / `SeriesCard` / `RefreshState`）；新增 `ScadaSeriesCard` / `ScadaSeriesItem`；动画行加 `IsExpanded` / `IsBodyVisible` / `Summary` / `SummaryIsWarning` / `HasWarning`；事件行加 `HasWarning`；面板加 `ToggleFoldAllCommand` / `AllCollapsed` / `FoldGlyph` / `FoldTooltip` / `HeaderMeta` / `PairRows` / `BuildSeriesCard` / `RefreshGroupStates`；移除类型行；`Unit` 补 `%` / `s` / 弹窗偏移 `px` |
| `VisionMaster\ViewModels\ScadaEventEditorViewModel.cs` | 加 `IsExpanded` / `IsBodyVisible` / `ActionCount` / `Summary` / `SummaryIsWarning` / `HasWarning`，并在钩子/动作变更时广播 |
| `VisionMaster\Views\ScadaPropertyView.xaml` | 卡片化分组（白卡 + 圆角 10 + 描边 + 浅投影）、filled 输入、三种行布局（左标签 72 / 上标签 / 成对网格）、绑定 chip、事件/动画折叠卡、曲线卡、组头错误点与计数、搜索 + 折叠全部、对象头、图片空值/图标按钮、只读锁、深色 ToolTip、色块 40×24 |
| `VisionMaster\Views\Controls\ScadaEventEditor.xaml` | 事件折叠卡（勾选/摘要/箭头 + 主色柔底卡头）、动作卡圆角 6、自带深色 ToolTip（独立 UserControl 不吃面板的隐式样式） |
| `Scada.Controls\Descriptors\ScadaPageProperties.cs` | 「背景不透明度」显示名改「不透明度」（组内语义已含"背景"，72px 标签不再截断；落盘键不变） |
| `ScadaChecks\Program.cs` | 断言更新：`UniversalRows` 2→1、基本组 2→1、类型行移除、搜索「宽」带出成对行、绑定 chip 替换编辑器、Fluent 下拉取全宽实例；新增三张证据渲染（按钮+事件、趋势曲线卡） |

### 13.2 关键决策

- **成对网格是 view-only**：右行仍留在 `_rows`（`Rows.Count` 与既有断言不变），只由左行的模板渲染、自己的模板收起；`PairWith/PairOwner` 只影响渲染与"搜索命中一个带出两个"。
- **曲线卡也是 view-only**：12 条系列行仍在 `_rows`（校验 / 绑定 / 撤销 / 搜索全沿用），4 个曲线颜色通过 `GroupOverride` 在呈现层移入「数据」组；组头 `DisplayCount=7`（4 曲线 + 3 设置），`Rows.Count` 仍 15。
- **类型行移除**：`ScadaTypeRow` 保留但不再生成；类型键只在对象头显示；`UniversalRows` 2→1。
- **绑定 chip 替换输入**：未绑时 `Hidden`（保留 ✕ 按钮实例，命令可用性与断言都认它）；Bool 行绑定时勾选框收起、标签回到左列（否则整行没有名字）。
- **事件/动画双状态源**：`IsConfigured` / `HasAnimation` 管"配没配"，`IsExpanded` 管展开；摘要 `Summary`（事件「N 个动作」/「未配置动作」，动画 `Detail`/「未选变量」）。
- **`RowEditorHost` 按 `Content.Kind` 选模板**：成对右列与曲线卡子行的 DataContext 不是行本身，按 `Kind` 会选错编辑器（字重被当成数值框显示 `Normal`）。

### 13.3 已知偏差 / 未做

- 未实现 8px 细滚动条（全局 `ScrollBar` 样式，Q7 待定）。
- 焦点只有主色描边，没有 Web 稿的 2px 柔光环（避免外层容器与行高抖动）。
- 动作卡未加序号徽标（`ScadaAction` 无序号，`AlternationIndex` 从 0 起；避免污染模型）。
- 260px 断言渲染下「背景图」空值摘要被挤成「未…」；300px 实际停靠宽度可显示「未选择」。
- 曲线卡未做逐曲线搜索高亮（命中任一系列时整卡显示）。

### 13.4 验证

- `ScadaChecks` **3177/0**；`FlowCanvasChecks` **4258/0**（当前构建，用 `-p:BuildProjectReferences=false` 避开运行中 VisionMaster 的 Modules 锁）。
- `run_gates` **15/16**：唯一红是 `FlowCanvasChecks` **构建**被运行中的 `VisionMaster.exe` 锁 `Modules\hdevenginedotnet.dll`（MSB3073 copy），不是代码失败；关闭应用后重跑 `tools\run_gates.ps1` 即可。
- WPF 渲染证据：`evidence/wpf-v2/after-indicator.png` / `after-page.png` / `after-button-event.png` / `after-trend.png` / `after-indicator-fluent.png`。
