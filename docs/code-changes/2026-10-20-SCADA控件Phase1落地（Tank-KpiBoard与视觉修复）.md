# 2026-10-20 SCADA 控件 Phase 1 落地（Tank / KpiBoard + 现有控件视觉修复）

## 范围

- 设计基线：`docs/UI设计基线/scada-controls/README.md` v1（Phase 1 落地范围）
- 覆盖：`Scada.Controls` 运行时图元库 + `Scada.Controls.Charts` 趋势曲线主题
- 不在范围：`SelectorSwitch` / `TextField` / `ComboBox` / `AckButton` / `AlarmList` / `MultiTrend` / `RecipeTable` / `PidLoop` 等输入、报警、多曲线控件（Phase 2）
- 不改：`TypeKey`、既有属性名/绑定类型、`VisionMaster\Views\**`、`UI\Controls\Themes\**`

## 新增图元

| TypeKey | 控件 | 默认尺寸 | 说明 |
| --- | --- | --- | --- |
| `Hmi.Tank` | 罐体/料仓 | 160×200 | `Level` 0~100 可绑；`Capacity`/`Unit`/`ShowValue`/`TrackColor`；只读 `TankBodyGeometry`/`FillGeometry`/`ValueText`/`FillRatio`；液位 50% × 容量 1000 L = 500 L |
| `Hmi.KpiBoard` | KPI 看板 | 360×180 | `Value1..4` 可绑；`Label1..4`/`Unit1..4`/`Columns`（2~4，模板绑 `EffectiveColumns`）；2×2 / 3+1 / 4×1 |

## 现有控件修复

| 项 | 文件 | 内容 |
| --- | --- | --- |
| D8 底板标题栏圆角 | `Scada.Controls\Themes\PanelElement.xaml` | 写死的 `3,3,0,0` 改为跟随 `CornerRadius` 的 `Rectangle` 圆角 + 底部同色矩形；命名件与坐标语义保留 |
| D9 数值域四态 | `Scada.Controls\Themes\IOFieldElement.xaml` | 只读透明底 + 底边线；可写 1px 边框 + hover 铅笔；编辑 accent 边框 + 焦点环；失败红边框；`PART_Editor`/`EditText`/`Mode`/`IsEditable` 逻辑未动 |
| D10 报警条两行式 | `Scada.Controls\Themes\AlarmBannerElement.xaml` | 名称 + 条件/值两行，行高 44；严重度胶囊描边；深色卡片；`Rows`/`SummaryText`/`EmptyText`/`AccentBar` 保留 |
| D7 轴控台字号底线 | `Scada.Controls\Themes\AxisPanelElement.xaml` | 8/9px → 10px；点位行高 23→26；六个命名件与命令绑定保留 |
| D6 图表空态/LIVE + 深色表面 | `Scada.Controls.Charts\Themes\ChartTrendElement.xaml`、`Elements\ChartTrendElement.cs`、`Charts\VM.Charts\ChartView.cs` | 新增只读 DP `HasSamples`；空态“等待数据…” + 绑定说明；LIVE 徽标；`ChartView.SetSurfaceColors` 实例级深色表面（不改全局 `ChartTheme`）；数据通道未动 |
| D12 选中/锁定/诊断色 | `Scada.Controls\Canvas\ScadaCanvas.cs`、`Canvas\ScadaDiagnosticOverlay.cs` | 颜色来源改为与 `ScadaTokens.xaml` 同值常量；交互逻辑未动 |
| 令牌 | `Scada.Controls\Themes\ScadaTokens.xaml`、`Themes\Generic.xaml` | 20 个 `Hmi*` 令牌 + `HmiFocusVisual`；本地字典，不引用 `UI` 工程；`Generic.xaml` 只加合并项 |

## reviewer 复核后的修正（同日）

首轮 reviewer 只读审查判定“视觉验收 Fail”，核心是 `Border.CornerRadius` 的 `double → CornerRadius` 静默落 0。已修：

| 问题 | 修正 |
| --- | --- |
| `Border.CornerRadius="{TemplateBinding CornerRadius}"` 源是 double，WPF 静默落 0 | 新增 `Scada.Controls\Converters\DoubleToCornerRadiusConverter.cs`（`DoubleToCornerRadius` + `DoubleToCornerRadiusTop`），在 `ScadaTokens.xaml` 注册；Panel 外层/标题栏、AlarmBanner 卡片/色条、KpiBoard 外框、IOField 可写框、AxisPanel 外框、ProgressBar 轨道/填充、ImageView 外框、Tank 外框全部改用转换器 |
| Panel 标题栏大圆角时底部遮罩盖成直角 | `TitleBar` 改为 `Border` + 只圆上两角的 `DoubleToCornerRadiusTop`，去掉底部同色矩形；`TitleHost` 继续裁剪 |
| KpiBoard 4×1 默认 360×180 裁数值/单位 | 每格“数值 + 单位”改为 `Grid`（数值 `*`、单位 `Auto`），数值加 `Viewbox StretchDirection=DownOnly`；4×1 下 “12,840 件” 完整可见 |
| KpiBoard `FontSize`/`FontWeight` 死属性 | 从 `Hmi.KpiBoard` 描述符移除这两项；数值 `Foreground` 改为 `TemplateBinding Foreground`，标签/单位用 `HmiTextMutedBrush` |
| AlarmBanner `FontSize`/`FontWeight` 被子项显式字号覆盖 | 标题改 `TemplateBinding FontSize/FontWeight`；行名用 `RelativeSource AncestorType=local:AlarmBannerElement` 取字号/字重；次要行保持固定小字号 |
| Tank `FontWeight` 写死 | `PART_Value` 改 `TemplateBinding FontWeight` |
| ChartTrend 只做空态、图表本体仍是白底 | `ChartView` 增加实例级 `SetSurfaceColors(figure/data/grid/tick)`（不改全局 `ChartTheme`）；`ChartTrendElement.ApplyChartAppearance()` 用 `HmiSurface/HmiBorder/HmiTextSecondary` 同值覆盖 |
| 外层圆角断言缺失 | `Phase1UiChecks` 增加 PanelFrame/TitleBar/BannerFrame/AxisFrame/TankFrame/KpiFrame 圆角断言；Tank/Kpi 测试显式设置 CornerRadius |

修正后 `ScadaChecks`：**1992 通过 / 0 失败**。

## 默认值调整

| 图元 | 项 | 旧值 | 新值 |
| --- | --- | --- | --- |
| `Hmi.IOField` | Fill | `#FFFFFFFF` | `#00FFFFFF`（只读透明，可写靠边框） |
| `Hmi.IOField` | Foreground | `#FF202020` | `#FFE6EEF7` |
| `Hmi.AlarmBanner` | Fill | `#FFFFFFFF` | `#FF151C23` |
| `Hmi.AlarmBanner` | Stroke | `#FF7A7A7A` | `#FF2A3745` |
| `Hmi.AlarmBanner` | CornerRadius | `4` | `8` |
| `Hmi.AlarmBanner` | Foreground | `#FF202020` | `#FFE6EEF7` |

对应 `ScadaChecks` 的描述符快照与旧视口像素断言已同步更新：
- `Hmi.IOField` 快照：Foreground/Fill 两项；
- `Hmi.AlarmBanner` 快照：Fill/Stroke/CornerRadius/Foreground 四项；
- `ViewportRenderChecks` 数值域断言：从“白底压深底”改为“只读态透明底，露出画面底色”。

## 构建期顺带修复

`Core\Models\Variable\NetworkVariableModel.cs` 补 `using VisionMaster.Services;`：该文件引用 `WriteLeaseRegistry`，但缺少命名空间导入，导致 `Core` 编译失败（`UIThemeSmokeTest` / `VisionMaster` 无法构建）。属构建阻塞修复，不改变行为。

## 验证

| 层 | 命令 | 结果 |
| --- | --- | --- |
| 控件构建 | `dotnet build Scada.Controls\VM.Scada.Controls.csproj -v q` | 0 错误 / 0 警告 |
| 图表构建 | `dotnet build Scada.Controls.Charts\VM.Scada.Controls.Charts.csproj -v q` | 0 错误 / 0 警告 |
| 断言宿主构建 | `dotnet build ScadaChecks` / `UIThemeSmokeTest` / `FlowCanvasChecks` | 0 错误（既有可空性警告不计） |
| 功能断言 | `dotnet build ScadaChecks\ScadaChecks.csproj -v q` + `dotnet run --project ScadaChecks --no-build` | **1992 通过 / 0 失败** |
| UI 冒烟 | `dotnet build UIThemeSmokeTest\UIThemeSmokeTest.csproj -v q` + `dotnet run --project UIThemeSmokeTest --no-build` | **exit 0，无 `[FAIL]`**；`Scada.Controls` 主题 hex=0 |
| 常规 gate | `dotnet build FlowCanvasChecks\FlowCanvasChecks.csproj -v q` + `dotnet run --project FlowCanvasChecks --no-build` | **2794 通过 / 0 失败** |
| 渲染证据 | `ScadaChecks` Phase 1 探针 | `docs/UI设计基线/scada-controls/evidence/after-phase1-core.png`、`after-phase1-tank-kpi.png`、`after-phase1-chart.png` |

关键断言：
- `[TK6]` Tank Level 0/50/100 → FillRatio 0/0.5/1；
- `[TK9]` Level=NaN 不抛异常、FillRatio=0、填充高度非负（反向）；
- `[TK14]` 真主题下 `PART_TankBody`/`PART_TankFill`/`PART_Value` 存在；
- `[P1]` Panel 标题栏 RadiusX/RadiusY=12；IOField 只读 `0,0,0,1`、失败红边、编辑态焦点环；AlarmBanner `AccentBar=3px`；Tank `500 L`；KpiBoard `Columns=2/4`；ChartTrend `HasSamples` 空态切换。

## 并发阻塞（非本批）

2026-10-20 14:54-14:58，工作区有并行改动同时进行：`Engine\FlowEngineService.cs` 曾引用未定义的 `IIoProvider`，`VisionMaster\Shell.xaml.cs` 的 `Execute` 调用签名与当前命令接口不匹配，导致 `VM.FlowEngine` / `VisionMaster` / `FlowCanvasChecks` / `UIThemeSmokeTest` 的新构建被阻塞。上表的 1992/0、0 FAIL、2794/0 产生于这些改动之前；需相关并行任务收口后再跑一次全量 gate。Phase 1 自身的 `Scada.Controls` / `Scada.Controls.Charts` 构建与 `ScadaChecks` 已通过。

## 已知偏差 / 未决

1. `Tank.ValueText` 是“容量 × 液位比例 + 单位”，不是百分比；如需百分比只改 `TankElement.FormatValue()`。
2. `KpiBoard.Columns` 允许 2~4（3 = 3+1 换行），设计主推 2×2 / 4×1。
3. 图表曲线像素受 ScottPlot → SkiaSharp 无头限制（R77）；证据图只验证空态/LIVE，真实曲线由 `_ChartProbe5` / 真机验收。
4. IOField hover 铅笔依赖运行态写通道 + 变量绑定 + 权限；静态离屏只验证 `IsEditable` 触发器，建议真机点选一次。
5. 其余默认文字（文本/时钟/泵/电机）仍为 `#FF202020`，属 D1 画面主题/默认调色板 Phase 2。
6. Phase 1 未做输入/报警/多曲线控件，需写变量、报警引擎、多系列图表契约。

## 回滚

- 单个图元：删除 `TankElement.cs`/`KpiBoardElement.cs` + 两条描述符 + 两个主题文件 + `Generic.xaml` 两条合并项 + `TankKpiChecks.cs` 调用。
- 视觉修复：按文件 revert 对应 `.xaml` / `ScadaCanvas.cs` / `ChartTrendElement.*`；注意与 `ScadaTokens.xaml` 的引用配套回滚。
- 默认值：恢复 `BuiltInElements.cs` 两处 Fill/Foreground 与 `ScadaChecks` 快照/像素断言。
