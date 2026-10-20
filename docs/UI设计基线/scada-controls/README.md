# SCADA 图元控件 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 日期 | 2026-10-20 |
| 确认日期 / 人 | 待用户确认 |
| 覆盖范围 | `Scada.Controls`（27 个内置 TypeKey，含 Phase 1 新增 Tank/KpiBoard 与 Phase 2 新增 SelectorSwitch/TextField/ComboBox/AckButton）+ `Scada.Controls.Charts`（1 个趋势曲线 TypeKey）；另推荐 10 项未实现控件（原 16 项候选中 6 项已落地）+ 7 项补充候选（2026-10-20 补入，见「推荐开发」） |
| 设计文件 | `design.html`（自包含，无外部依赖；右上角可切换深色/浅色画面预览） |
| 证据目录 | `evidence/`（design.html 的分段渲染 PNG；WPF 复刻后的对照图后续补入） |
| 状态 | Phase 1 已复刻（Tank/KpiBoard + 现有控件修复，2026-10-20）；Phase 2 已落地（SelectorSwitch/TextField/ComboBox/AckButton，2026-10-20）；设计基线 v1 待用户确认（含 7 项补充候选） |

## 定位

按 `AGENTS.md` §30.1 的四步流程，本文件是**第 ① 步（Web 设计基线）**的产物：

```text
① 设计基线（本目录） → ② 设计定稿与令牌映射 → ③ WPF 复刻 → ④ 截图验收
```

- Web 只是**设计媒介**，不进入构建、不部署、不作为第二个客户端维护（R126）。
- 设计基线确认后只读；需要调整时升版本（v1 → v2），并同步令牌映射与已复刻页面（R128）。
- 本版**只审查设计**，不改任何 WPF/XAML 代码。

## 审查口径

按《插件自定义视图 Web 化改造 SOP》的视觉语言核对，并补上 SCADA 运行态特有的三条：

| # | 口径 | 来源 |
| --- | --- | --- |
| 1 | 令牌化：视图内 0 个十六进制色值 | R117 / R140 |
| 2 | 五态：Normal / Hover / Pressed / Focus / Disabled | R115 |
| 3 | 刻度：8px 圆角、12px 标签、11.5px 帮助、36px 控件 | SOP §5.2 / §5.3 |
| 4 | 数字：等宽 + `tabular-nums` | SOP §5.3 |
| 5 | 不造死按钮：只接现有属性/命令 | SOP §5.4 |
| 6 | SCADA 特有：状态色只表达状态；过程图元需 Selected/Locked/NoData | 本基线补充 |
| 7 | SCADA 特有：运行画面深色、编辑工作台浅色，一套控件两种令牌 | 本基线补充 |

## 现状审查（14 项）

> 证据均指向磁盘当前代码；行号以 2026-10-20 版本为准。

| # | 问题 | 证据 | 影响 | 本版处置 | 级别 |
| --- | --- | --- | --- | --- | --- |
| 01 | 默认调色板分裂：一半图元浅色皮肤，一半深色皮肤 | `BuiltInElements.cs:61/214` 白底；`:416/469` 深底；`ScadaPage.cs:25` 默认页面 `#FF1E1E1E` | 默认深色画面上出现白块；同一画面像两套软件 | 统一深色运行令牌；浅色作为显式预设 | 高 |
| 02 | 默认文字在深色画面上不可读 | `PropertyPacks.cs:190-191` 默认 `#FF202020`；`BuiltInElements.cs:323/350` 泵/电机位号同色 | 文本、时钟、泵/电机位号几乎看不见 | 运行态默认文字改 `#E6EEF7` | 高 |
| 03 | 表盘指针默认是“报警红” | `BuiltInElements.cs:270` Gauge 指针默认 `#FFE03A2B` | 正常压力表看起来像在报警 | 指针改 accent 青蓝；红色只给越限/故障 | 高 |
| 04 | 数值域“只读/可写”没有视觉差别 | `IOFieldElement.xaml:118-122` 只换鼠标指针；`Mode` 默认 `Output` | 操作员不知道哪些框能点 | 只读/可写/编辑中/校验失败四态 | 高 |
| 05 | 五态不齐：按钮缺按下/焦点，位按钮缺焦点 | `ButtonElement.xaml:59-67` 只有 hover/disabled；`BitButtonElement.xaml:76-88` 无 focus | 键盘焦点不可见；不满足 R115 | 全部交互控件统一五态 | 高 |
| 06 | 圆角刻度不统一：0/2/3/4/12 并存 | `BuiltInElements.cs:64/124/185/392/419/472`；`AxisPanelElement.xaml:72/143` | 控件像来自不同组件库 | 4/6/8/12 四档刻度 | 中 |
| 07 | 字号跨度过大：8/9/10/11/12/13/14/16/30 同屏 | `AxisPanelElement.xaml:558/567/574` 8px；`:183` 9px；`:282` 30px | 8~9px 读不清；数字不统一 | 10/11.5/12.5/14/16/20/28 七档；tabular | 中 |
| 08 | 趋势曲线仍是浅色主题 | `ChartTheme.cs:14-19` 白底；`ChartTrendElement.cs:190` 网格 `#FF3A3A3A` | 深色画面上是一张白卡 | 深色图表令牌 + 空态 | 中 |
| 09 | 底板标题栏圆角写死 3 | `PanelElement.xaml:46` `CornerRadius="3,3,0,0"`，底板圆角可配 0~200 | 圆角调大后标题栏方角露出 | 标题栏跟随底板圆角 | 中 |
| 10 | 硬编码颜色存量 | `hardcoded-colors.baseline.txt`：AxisPanel 20、IOField 1、ScadaCanvas 1、ScadaEditorView 15、RuntimeWindow 14 | 换肤不彻底 | 按令牌迁移清单逐项替换 | 中 |
| 11 | 缺“无数据/离线”状态 | `DeviceState.cs:15-25` 只有 Stopped/Running/Fault | 通信断了仍显示“停机灰” | 设计预留 Unknown；需契约扩展（D2） | 高 |
| 12 | 按钮可聚焦但无键盘激活 | `ButtonElement.cs` 是 `ContentControl`，无 KeyDown/KeyUp；样式 `Focusable=True` | Tab 到按钮按空格/回车无反应 | 补键盘激活或先 `Focusable=False`（D5） | 中 |
| 13 | 选中/锁定/诊断色未令牌化 | `ScadaCanvas.cs:83` `#007ACC`、`:92` `#E3A51B`、`:1382-1384` | 编辑器与运行态两套蓝/橙 | 收进 `HmiSelectionBrush` / `HmiLockedBrush` / 诊断色 | 低 |
| 14 | 报警条默认白底、行距偏挤 | `AlarmBannerElement.xaml:58-66` 内衬 12/6/8/6；默认 Fill 白 | 深色画面上白卡抢视线 | 深色卡片 + 两行 44px 行高 | 中 |

**一句话结论**：不是“控件画得不好看”，而是**没有一套画面级令牌**。默认值各写各的，深色画布与浅色控件互相打架。先把“深色运行 / 浅色编辑”两个上下文定死，再谈每个图元的形状。

## 设计方向（10 条）

1. **双上下文，一套控件**：运行画面深色（`#0E1318`），编辑工作台浅色（`#F7F8FA`）；控件结构、尺寸、状态完全一致，只换令牌。
2. **状态先于装饰**：灰=停、绿=运行、黄=警告、红=故障/报警；accent 青蓝只做交互/指针/选中，不占用语义色。
3. **五态是底线**：按钮、位按钮、数值域、轴按钮全部 Normal/Hover/Pressed/Focus/Disabled；过程图元补 Selected/Locked/NoData。
4. **数值域可写性可辨**：只读=无边框 + 底边线；可写=1px 边框 + hover 铅笔；编辑中=accent 边框 + 焦点环；失败=红边框 + ToolTip。
5. **过程符号统一线宽**：1.5px 线宽、内接几何、标签在预留区；泵/电机/管道的状态色只落在叶轮/M/箭头上，壳体保持中性。
6. **表盘指针不再默认红色**：指针 accent；警告/报警只改指针与读数颜色，盘面保持中性。
7. **字号七档 + 数字等宽**：10 / 11.5 / 12.5 / 14 / 16 / 20 / 28；表盘、棒图、IO 域、轴位置、报警时间全部 `tabular-nums`。
8. **圆角四档**：数据控件 4、按钮 6、卡片/底板 8、控制台 12；过程符号不随意加圆角。
9. **容器与内容对齐**：底板标题栏圆角跟随 `CornerRadius`；图像显示补“无图像 + 下一步”空态；报警条补“系统正常”空态。
10. **令牌化**：新增 `Hmi*` 令牌只追加到 `UI\Controls\Themes\Colors.xaml`；视图内 0 个十六进制色值；新增键先全仓查重（R146）。
11. **分类页签**：控件目录按 基础 / 操作 / 指示 / 数值 / 工艺 / 报警 / 运动 / 数据 / 推荐开发 九个页签浏览；推荐开发页签按 P0/P1/P2 排序。

## 已实现控件清单（28 个 TypeKey）

| 分类 | TypeKey | 默认尺寸 | 设计要点 |
| --- | --- | --- | --- |
| 基础 | `Hmi.Rectangle` | 120×60 | 默认深色画面用 surface 底 + outline；选中 2px accent + 7px 手柄 |
| 基础 | `Hmi.Ellipse` | 100×100 | 与矩形同词汇；标签不裁剪 |
| 基础 | `Hmi.Text` | 120×24 | 16/12.5/11.5 三级；默认文字 `#E6EEF7` |
| 基础 | `Hmi.Panel` | 360×240 | 8px 圆角；标题栏 30px；子图元绝对坐标 + 裁切 |
| 基础 | `Hmi.ImageView` | 200×160 | 有图 Stretch；无图空态图标 + 说明 |
| 操作 | `Hmi.Button` | 88×32 | 30/36/44 三档高度；primary/secondary/danger；五态 |
| 操作 | `Hmi.BitButton` | 88×32 | 0/1 两态色差；On 绿实底；按下压暗；焦点环 |
| 操作 | `Hmi.SelectorSwitch` | 120×36 | 2~4 档保持式转换开关；`Value:int` 写变量；选中档深绿；Phase 2 新增 |
| 数值 | `Hmi.TextField` | 160×32 | 字符串输入；`Value:string` 读写；Enter/失焦提交、Esc 取消；Phase 2 新增 |
| 数值 | `Hmi.ComboBox` | 160×32 | 枚举型下拉；`Value:int` + `Options="0=手动;1=自动"` 映射；Phase 2 新增 |
| 报警 | `Hmi.AckButton` | 120×36 | 一键确认 All/Oldest；待确认计数 chip；权限门禁；Phase 2 新增 |
| 指示 | `Hmi.Indicator` | 40×56 | 圆/方；36px 灯体；2px 描边 |
| 指示 | `Hmi.Lamp` | 40×56 | Off/On/Warning/Alarm 四态；与报警严重度同令牌 |
| 指示 | `Hmi.Clock` | 180×28 | 28px mono 时间；10.5px 日期；11.5px 标签 |
| 数值 | `Hmi.ProgressBar` | 160×20 | 横/纵；12px 轨道；正常/警告/越限三档示例 |
| 数值 | `Hmi.IOField` | 140×28 | 只读/可写/编辑中/失败/越限五态 |
| 数值 | `Hmi.Gauge` | 140×140 | 270° / 5 格；3px accent 指针；13px mono 读数 |
| 工艺 | `Hmi.Valve` | 72×64 | 0/50/100 三态；灰/蓝/绿；位号上、开度下 |
| 工艺 | `Hmi.Pump` | 72×72 | 停/转/故障；叶轮承载状态色 |
| 工艺 | `Hmi.Motor` | 72×80 | 停/转/故障；M 承载状态色；接线盒 |
| 工艺 | `Hmi.Pipe` | 160×32 | 停流/流动/异常；箭头 3px 圆头；流向正/反/无 |
| 报警 | `Hmi.AlarmBanner` | 320×140 | 3px 严重度色条；两行 44px；严重未确认闪烁 |
| 运动 | `Hmi.AxisConsole` | 320×470 | 位置 30px mono；8 状态灯；三列按钮；点位表 |
| 运动 | `Hmi.AxisPanel` | 280×280 | 紧凑预设：状态 + 手动操作，无点位表 |
| 运动 | `Hmi.AxisPointTable` | 340×300 | 旧 TypeKey 兼容；`ShowInToolbox=false`，不在工具箱展示 |
| 工艺 | `Hmi.Tank` | 160×200 | 液位 0~100 可绑；容量×液位读数；圆角罐体 + 底部填充；Phase 1 新增 |
| 数值 | `Hmi.KpiBoard` | 360×180 | 2~4 组数值/标签/单位；2×2 / 3+1 / 4×1；数值 `Viewbox` 自适应；Phase 1 新增 |
| 数据 | `Hmi.ChartTrend` | 360×200 | 深色图表表面；2px accent 曲线；空态“等待数据” |

## 推荐开发（第 9 个分类；Tank / KpiBoard 已于 Phase 1 落地；2026-10-20 补入 7 项补充候选）

> 这 16 项是“下一个控件”的原候选。**P0 中的 `Hmi.Tank` / `Hmi.KpiBoard`（Phase 1）与 `Hmi.SelectorSwitch` / `Hmi.TextField` / `Hmi.ComboBox` / `Hmi.AckButton`（Phase 2）已于 2026-10-20 落地**；其余 10 项未实现（原 16 项中 P0 仅剩 AlarmList / MultiTrend）。另于 2026-10-20 补入 7 项「补充候选」（见下），按现有图元库空白补，不改变原 16 项的优先级。优先级按现场价值 / 复用面 / 实现成本综合排序；每项都给出“现有替代”，避免把“没有这个控件”误当成“做不了”。**每项的核心绑定变量与读写方向（只读 / 读-写 / 事件 / 设计期配置）见 `design.html` 推荐开发页签的「变量联动」行。**

| 优先级 | 建议 TypeKey | 控件 | 解决什么 / 现有替代 | 工作量 | 依赖 |
| --- | --- | --- | --- | --- | --- |
| P0 | `Hmi.SelectorSwitch` | 多档旋钮 ✅ | 2~4 档保持/弹簧返回；手/自动、本地/远程、工位选择；替代：多个位按钮 | M | 事件/动作 |
| P0 | `Hmi.TextField` | 文本输入域 ✅ | 字符串输入 + 长度/格式/权限校验；批号、备注、配方名；替代：无（数值域只收数字） | M | 写变量 + 校验 |
| P0 | `Hmi.ComboBox` | 下拉选择器（枚举型） ✅ | 绑 `int` / 枚举值，`Options = Value + Display` 映射；PLC/Modbus 寄存器直接绑 int；文本模式仅用于字符串变量；替代：按钮组 | M | 枚举映射 / 候选来源 |
| P0 | `Hmi.AckButton` | 报警确认按钮 ✅ | 一键确认当前/最早/全部未确认；替代：打开报警历史抽屉 | S | 报警引擎 API |
| P0 | `Hmi.AlarmList` | 报警列表 | 页内报警表：筛选/逐条确认/导出 CSV；替代：宿主报警历史抽屉 | L | 复用 `ScadaAlarmHistoryView` |
| P0 | `Hmi.MultiTrend` | 多曲线趋势 | 2~8 条曲线、图例、游标、独立量程；PV/SP/OP 对比；替代：多个 ChartTrend 并排 | L | `VM.Charts` 多系列 |
| P0 | `Hmi.Tank` | 罐体/料仓 | 液位填充 + 容量/温度/压力叠加；替代：矩形 + 棒图 | M | 几何绘制 |
| P0 | `Hmi.KpiBoard` | KPI 看板 | OEE/产量/良率/停机，2×2 或 4×1；替代：文本 + 棒图手工拼 | S/M | 表达式/统计 |
| P1 | `Hmi.DateTimeField` | 日期时间域 | 日期/时间输入，格式与范围校验；替代：文本输入 + 手写格式 | M | 写变量 + 格式 |
| P1 | `Hmi.StackLight` | 三色灯柱 | 红/黄/绿 + 可选蜂鸣；设备状态、安灯呼叫；替代：多态灯 + 文字 | S/M | 声音/状态 |
| P1 | `Hmi.RunHourMeter` | 运行计时器 | 累计运行小时/本次时长/节拍，可复位；替代：时钟 + 文本 | S | 节拍源/累计 |
| P1 | `Hmi.SequenceStep` | 顺控步骤指示 | 当前步/下一步/自动手动/跳步；替代：文本 + 状态灯 | M/L | 流程引擎状态 |
| P1 | `Hmi.RecipeTable` | 配方/参数表 | 多行参数表：下载/上传/比对/审计；替代：多个数值域 | L | 配方存储/权限/审计 |
| P2 | `Hmi.PidLoop` | PID 回路面板 | PV/SP/OP 同屏、自动手动、参数整定；替代：表盘 + 数值域组合 | L | PID 控制/写变量 |
| P2 | `Hmi.InterlockMatrix` | 联锁/许可矩阵 | 条件矩阵、旁路、复位；“为什么不能启动”一屏说清；替代：多个指示灯 + 文本 | L | 联锁引擎 |
| P2 | `Hmi.VideoView` | 视频监控 | RTSP/相机流、快照、通道切换；替代：图像显示 + 定时截图 | L | 视频解码/渲染 |

> **下拉选择器口径（2026-10-20 用户裁定）**：默认做**枚举型下拉**——核心绑定 `Value : int`（或枚举成员名），`Options` 是 `Value + Display` 映射表，**显示文本不是值**。PLC/Modbus 寄存器是数值，绑 int 最稳；改显示名/换语言不会打断已配好的流程。文本模式只作为字符串变量（配方名、批号等）的扩展，不作为默认。
>
> P3 观察（不排期）：条码/二维码、电子签名/审计、报表嵌入。它们通常由行业合规或上层系统决定，先不占控件库档期。

### 补充候选（2026-10-20 补入，7 项）

> 这 7 项不在原 16 项候选内，按现有图元库的空白补；已在 `design.html` 推荐开发页签新增卡片。优先级按本版建议，落地前逐项评审。

| 优先级 | 建议 TypeKey | 控件 | 解决什么 / 现有替代 | 工作量 | 依赖 |
| --- | --- | --- | --- | --- | --- |
| P0 | `Hmi.Line`（折线/箭头同族） | 直线 / 折线 / 箭头 | 基础类只有矩形/椭圆/文本；连线、标注、流向只能拿管道或细矩形凑 | S/M | 几何绘制 + 命中测试 |
| P1 | `Hmi.Picture` | 本地图片 / 图片框 | `Hmi.ImageView` 只认运行态图像变量（HImage）；选本地图片后复制到方案根目录 `Image\`，`.vms` 内部只存相对路径，方案迁移不断图；属性面板只给缩略图 + 加载/替换/清除，不暴露路径 | S/M | 属性面板图片编辑器（加载/替换/清除） + 方案资产复制/解析 + 图片加载/缓存 |
| P1 | `Hmi.Pipe · Shape` | 管道管件（弯头/三通/异径/法兰） | `Hmi.Pipe` 只有直管；拐弯靠旋转拼接、接缝对不齐 | M | 几何绘制 + 选中框/裁剪重算 |
| P1 | `Hmi.Slider` | 模拟量设定条 | 连续量只能 IOField 键盘输入或 SelectorSwitch 离散档；频繁调温/调速/调流量缺拖动即写 | M | 写变量 + 权限 + 拖动交互 |
| P1 | `Hmi.ChartBar` / `Hmi.ChartPie` | 柱状图 / 饼图 | `VM.Charts` 已有 Bar/Pie/Histogram 系列，但图元库只有趋势曲线；产量/良率/停机分布缺统计图 | M | `VM.Charts` 柱/饼系列 |
| P2 | `Hmi.Fan` / `Hmi.Compressor` / `Hmi.HeatExchanger` / `Hmi.Filter` / `Hmi.Agitator` / `Hmi.Conveyor` | 工艺符号包 | 工艺类只有阀/泵/电机/管道/罐体；P&ID 常用符号缺一批 | 每个 S/M | 几何绘制 + 状态色 |
| P2 | `Hmi.DataTable` | 通用数据表 | AlarmList/RecipeTable 都是专用表；生产记录/历史数据缺只读表格 | M/L | 数据源契约 + 表格内核 |

> **方案资产约定（2026-10-20 用户裁定）**：`Hmi.Picture` 选图时把文件复制到方案根目录的 `Image\`（不存在则创建），`.vms` 内部只存相对路径 `Image\xxx.png`；加载/渲染按方案文件所在目录解析。复制与解析在宿主侧完成（`Scada.Controls` 不碰方案目录），控件只认相对路径。整个方案目录（`.vms` + `Image\`）一起拷贝/迁移，图片不会断。缺文件显示无图占位并提示，不抛异常；同名不同内容需确认或自动改名，同名同内容按哈希去重。另存为/复制方案时必须连同 `Image\` 一起带走。
>
> **属性面板口径（2026-10-20 用户裁定）**：**否决“新增 FilePath 属性类型”**。`Hmi.Picture` 的属性面板只保留「加载图片」功能（已有图片时为「替换图片」，另有「清除」），只显示缩略图与操作按钮，**不显示、不暴露文件路径**；路径只作为方案资产内部数据存在。实现上走 `Hmi.Picture` 的专用图片编辑器（复合行），不新增通用 `ElementPropertyKind.FilePath`。

### Hmi.Picture 属性面板（功能说明）

| 状态 | 面板显示 | 操作 |
| --- | --- | --- |
| 未加载 | 空缩略图 + 「加载图片」 | 打开文件对话框，选图后复制到方案根目录 `Image\` |
| 已加载 | 缩略图 + 「替换图片」「清除」 | 替换或清除；不显示路径与文件名 |
| 文件缺失 | 占位图 + 「图片缺失，请重新加载」 | 重新加载；不抛异常 |
| 多选 | 缩略图区禁用 + 「单选时可加载图片」 | 公共属性仍可编辑 |
| 运行态 | 只显示图片 / 占位 | 不提供加载、替换、清除 |

- 文件对话框只认常见位图格式；选图后由宿主复制到方案根目录 `Image\`（不存在则创建），同名同内容按哈希去重，同名不同内容确认或自动改名。
- `.vms` 内部只记相对路径；属性面板**不显示、不复制、不提供路径编辑**；不新增 `ElementPropertyKind.FilePath`。
- 加载 / 替换 / 清除进撤销栈；撤销只回退图元引用，不删除 `Image\` 里的文件（文件清理另立策略）。
- 运行态只读：运行画面不出现“加载图片/替换/清除”，避免操作员在运行中换图。

## 设计证据

`design.html` 已按 1440×900 视口分段渲染，保存于 `evidence/`：

| 文件 | 内容 |
| --- | --- |
| `design-v1-00-full.png` | 整页（1425×12452） |
| `design-v1-01-cover.png` | 页头 + 审查结论开头 |
| `design-v1-02-audit.png` | 14 项审查表 |
| `design-v1-03-principles.png` | 设计原则 + 双上下文对照 |
| `design-v1-04-tokens.png` | 令牌、字号、规格 |
| `design-v1-05-sheet.png` / `design-v1-05-sheet-full.png` | 控件全家福（视口 / 完整） |
| `design-v1-06-interactive.png` | 按钮 / 位按钮 / 数值域五态 |
| `design-v1-07-indicators.png` | 指示灯 / 多态灯 / 时钟 / 报警条 |
| `design-v1-08-numeric.png` | 棒图 / 表盘 / 阀门 / 泵 / 电机 / 管道 |
| `design-v1-09-containers.png` | 底板 / 图像显示 / 文本 / 形状 |
| `design-v1-10-motion.png` | 轴控台 / 轴面板 / 趋势曲线 |
| `design-v1-11-runtime.png` | 运行态整屏 |
| `design-v1-12-matrix.png` | 状态矩阵 |
| `design-v1-13-mapping.png` | 令牌映射 |
| `design-v1-14-open.png` | 遗留与待确认 |
| `design-v1-15-dev-tab.png` | 推荐开发页签（16 项原候选缩略预览 + 核心绑定变量 / 变量联动方向；7 项补充候选见 `design.html`，未重渲染） |
| `design-v1-16-category-tab.png` | 分类页签示例（工艺） |
| `after-phase1-core.png` | Phase 1 实现：Panel / IOField / AlarmBanner / AxisPanel 修复 |
| `after-phase1-tank-kpi.png` | Phase 1 实现：Tank / KpiBoard |
| `after-phase1-chart.png` | Phase 1 实现：ChartTrend 空态 / LIVE |
| `after-phase2-selector.png` | Phase 2：SelectorSwitch 2/3/4 档与选中态 |
| `after-phase2-textfield.png` | Phase 2：TextField 只读/可写/编辑/失败/占位 |
| `after-phase2-combobox.png` | Phase 2：ComboBox 枚举映射/未知值/禁用 |
| `after-phase2-ack.png` | Phase 2：AckButton 可用/禁用与计数 |

> 2026-10-20 补入的 7 项补充候选只在 `design.html` 推荐开发页签新增卡片，未重渲染 `evidence/` PNG；需要留档时再跑一次分段渲染。

## 令牌映射（新增）

> **落点**：`VM.Scada.Controls` 不引用 `UI` 工程（csproj 只引用 `VM.Scada` 与生成器），所以运行时 `Hmi*` 令牌落在 `Scada.Controls\Themes\ScadaTokens.xaml`，由 `Scada.Controls\Themes\Generic.xaml` 合并；`UI\Controls\Themes\Colors.xaml` 只服务宿主编辑器/弹窗，**当前没有 `Hmi*` 键**（不是“两处同名同值”）。禁止跨工程 `StaticResource`。新增键先全仓查重（R146），不改既有键/值。

| 设计令牌 | 深色运行 | 浅色画面 | WPF 资源键 |
| --- | --- | --- | --- |
| 画面底 | `#0E1318` | `#F4F7FB` | `HmiCanvasBackgroundBrush` |
| 表面 | `#151C23` | `#FFFFFF` | `HmiSurfaceBrush` |
| 浮起表面 | `#1B242D` | `#F8FAFC` | `HmiSurfaceRaisedBrush` |
| 描边 | `#2A3745` | `#D8E0EA` | `HmiBorderBrush` |
| 强描边 | `#3A4A5C` | `#B8C4D4` | `HmiBorderStrongBrush` |
| 主文字 | `#E6EEF7` | `#0F172A` | `HmiTextBrush` |
| 次要文字 | `#A9B7C6` | `#475569` | `HmiTextSecondaryBrush` |
| 弱文字 | `#7C8A99` | `#94A3B8` | `HmiTextMutedBrush` |
| 强调 / 焦点 | `#38BDF8` | `#0284C7` | `HmiAccentBrush` |
| 主按钮 | `#0EA5E9` | `#0EA5E9` | `HmiAccentStrongBrush` |
| 运行 / 正常 | `#22C55E` | `#16A34A` | `HmiSuccessBrush` |
| 成功填充（深） | `#15803D` | `#15803D` | `HmiSuccessFillBrush`（选中档/确认态实心） |
| 警告 | `#F59E0B` | `#D97706` | `HmiWarningBrush` |
| 故障 / 报警 | `#EF4444` | `#DC2626` | `HmiDangerBrush` |
| 提示 / 信息 | `#3B82F6` | `#2563EB` | `HmiInfoBrush` |
| 停机 / 熄灭 | `#64748B` | `#94A3B8` | `HmiOffBrush` |
| 空槽 / 轨道 | `#26313D` | `#E8EDF3` | `HmiTrackBrush` |
| 选中 | `#38BDF8` | `#0284C7` | `HmiSelectionBrush` |
| 锁定 | `#E3A51B` | `#B45309` | `HmiLockedBrush` |
| 诊断警告 | `#E88B1A` | `#B45309` | `HmiDiagnosticWarningBrush` |
| 诊断错误 | `#E03A2B` | `#B91C1C` | `HmiDiagnosticErrorBrush` |

**焦点样式**：新增 `HmiFocusVisual`（2px accent + 2px offset），供运行态交互控件使用；弹窗内仍用 `DialogFocusVisual`（R135）。

**编辑工作台令牌复用**：`ScadaPanelBackgroundBrush`、`ScadaSurfaceBrush`、`ScadaTextBrush`、`ScadaSecondaryTextBrush`、`DialogAccentBrush`、`DialogBorderBrush`、`DialogTextPrimaryBrush`、`DialogTextSecondaryBrush`；不另造第二套。

## 复刻约束（必须遵守）

1. 不改任何 `TypeKey`、默认尺寸、属性名与绑定类型。
2. 不改 `DataContext`、`Element`、运行时绑定通道。
3. 不新增功能属性（主题、阈值、Unknown 态另立 D 项）。
4. 只用 WPF 可还原能力：`Border` / `Path` / `Ellipse` / `TextBlock` / `Grid` / `Canvas` / `DropShadowEffect`；不使用 CSS 滤镜、混合模式、遮罩、Web 字体。
5. 动效 150~200ms，缓动与 Web 基线同档。
6. 图标按钮 30×30 + `Padding="0"`（R68）；焦点走显式样式（R135）。
7. 视图 XAML `hex=0`；新增键先全仓查重（R146）。
8. 改过 `Scada.Controls` / `Scada.Controls.Charts` 必须重建 `Modules` 产物（红线③；R71 同族）。

## Phase 1 落地范围（2026-10-20 用户授权）

本轮先落“视觉 + 显示类”，把设计系统和新图元的骨架跑通；输入/报警/图表类涉及写变量、报警引擎、多系列图表契约，放 Phase 2。

| 范围 | 内容 |
| --- | --- |
| 现有控件修复 | D6 图表空态、D7 轴控台字号、D8 底板圆角、D9 数值域四态、D10 报警条密度、D12 选中/锁定/诊断色令牌化 |
| 新增图元 | `Hmi.Tank`（罐体/料仓）、`Hmi.KpiBoard`（KPI 看板）——均为显示类，不新增写通道 |
| 令牌 | 新增 `Scada.Controls\Themes\ScadaTokens.xaml`（运行时 Hmi* 令牌），由 `Generic.xaml` 合并 |
| 测试 | `ScadaChecks` 功能断言 + `UIThemeSmokeTest`/渲染探针 + 构建 gate |
| 不在 Phase 1 | `SelectorSwitch` / `TextField` / `ComboBox` / `AckButton` / `AlarmList` / `MultiTrend` / `RecipeTable` / `PidLoop` 等（需写变量/报警/图表契约） |

## Phase 1 实施结果（2026-10-20）

| 项 | 结果 |
| --- | --- |
| 新增图元 | `Hmi.Tank`（160×200，工艺）、`Hmi.KpiBoard`（360×180，数值）；均已注册进 `ElementRegistry` 与工具箱 |
| 现有控件修复 | D6 图表空态/LIVE + 深色表面、D7 轴控台字号底线、D8 底板标题栏/外层圆角、D9 数值域四态、D10 报警条两行式、D12 选中/锁定/诊断色令牌化 |
| 令牌 | 新增 `Scada.Controls\Themes\ScadaTokens.xaml`（20 个 `Hmi*` 令牌 + `HmiFocusVisual`），由 `Generic.xaml` 合并 |
| 默认值调整 | `Hmi.IOField`：Fill `#00FFFFFF`、Foreground `#FFE6EEF7`；`Hmi.AlarmBanner`：Fill `#FF151C23`、Stroke `#FF2A3745`、CornerRadius `8`、Foreground `#FFE6EEF7` |
| 功能断言 | `ScadaChecks`：**1992 通过 / 0 失败**（含 `[TK]` 18 条、`[P1]` 36 条） |
| UI 冒烟 | `UIThemeSmokeTest`：**exit 0，无 `[FAIL]`**；`Scada.Controls` 主题 hex=0（`ScadaTokens.xaml` 为唯一豁免） |
| 常规 gate | `FlowCanvasChecks`：**2794 通过 / 0 失败** |
| 构建 | `VM.Scada.Controls` / `VM.Scada.Controls.Charts` / `ScadaChecks` / `UIThemeSmokeTest` / `FlowCanvasChecks`：**0 错误**（既有可空性警告不计） |
| 渲染证据 | `evidence/after-phase1-core.png`、`after-phase1-tank-kpi.png`、`after-phase1-chart.png` |
| reviewer 修正 | 首轮 reviewer 抓到 `Border.CornerRadius` 的 double 绑定静默落 0；已新增 `DoubleToCornerRadiusConverter`（含 Top 变体）覆盖 Panel/AlarmBanner/KpiBoard/IOField/AxisPanel/ProgressBar/ImageView/Tank，并补外层圆角断言 |
| 构建期顺带修复 | `Core\Models\Variable\NetworkVariableModel.cs` 补 `using VisionMaster.Services;`（`WriteLeaseRegistry` 引用缺失，否则 `UIThemeSmokeTest`/`VisionMaster` 无法编译） |

> **并发阻塞（2026-10-20 14:54-14:58，非 Phase 1 改动）**：工作区有并行改动同时进行——`Engine\FlowEngineService.cs` 曾引用未定义的 `IIoProvider`，`VisionMaster\Shell.xaml.cs` 的 `Execute` 调用签名与当前命令接口不匹配。Phase 1 的测试结果（1992/0、0 FAIL、2794/0）产生于这些改动之前；需相关并行任务收口后再跑一次全量 gate（`FlowCanvasChecks` / `UIThemeSmokeTest`）。Phase 1 自身的 `Scada.Controls` / `Scada.Controls.Charts` 构建与 `ScadaChecks` 已通过。

**Phase 1 已知偏差**：

1. `Tank.ValueText` 采用「容量 × 液位比例 + 单位」（50% × 1000 L = 500 L），不是百分比；如需百分比只改 `TankElement.FormatValue()` 一处。
2. `KpiBoardElement.Columns` 允许 2~4（3 = 3+1 换行），设计基线主推 2×2 / 4×1；3 作为中间档保留。
3. 图表曲线像素仍受 ScottPlot → SkiaSharp 无头限制（R77）；证据图只验证空态/LIVE，真实曲线由 `_ChartProbe5` 与真机验收。
4. `IOField` 的 hover 铅笔依赖运行态 `RuntimeContext.Writer + 变量绑定 + 权限`；静态离屏只能验 `IsEditable` 触发器，建议真机点选一次。
5. 其余默认文字（文本/时钟/泵/电机）仍是 `#FF202020`，属于 D1 画面主题/默认调色板的 Phase 2 范围，本批未改。

**Phase 1 未做**：`SelectorSwitch`、`TextField`、`ComboBox`、`AckButton`、`AlarmList`、`MultiTrend`、`RecipeTable`、`PidLoop` 等输入/报警/多曲线控件——需写变量、报警引擎或多系列图表契约，放 Phase 2。

## Phase 2 实施结果（2026-10-20）

| 项 | 结果 |
| --- | --- |
| 新增图元 | `Hmi.SelectorSwitch`（2~4 档，`Value:int`）、`Hmi.TextField`（字符串读写）、`Hmi.ComboBox`（枚举型：`Value:int` + `Options="值=显示名;..."`）、`Hmi.AckButton`（All/Oldest 确认） |
| 交互口径 | Selector 点击档位写 int；TextField Enter/失焦提交、Esc 取消、超长/失败留编辑态；ComboBox 只在 `DropDownClosed` 写变量（打开后未改选不写）；AckButton All/Oldest（Oldest 按 `ActivatedAtUtc` 真取最早） |
| 令牌 | 新增 `HmiSuccessFillBrush`（#15803D，选中档/确认态实心，白字对比足够） |
| 功能断言 | `ScadaChecks`：**2077 通过 / 0 失败**（含 `[CB]` 47 条、`[P2]` 38 条） |
| UI 冒烟 | `UIThemeSmokeTest`：**exit 0，无 `[FAIL]`** |
| 常规 gate | `FlowCanvasChecks`：**2797 通过 / 0 失败** |
| 渲染证据 | `evidence/after-phase2-selector.png`、`after-phase2-textfield.png`、`after-phase2-combobox.png`、`after-phase2-ack.png` |
| 审核修正 | 去掉 Selector 控件自己重复 raise 的 `Pressed`（画布已统一发，避免动作双响）；Selector 默认填充改深绿 `HmiSuccessFillBrush`；AckButton `Oldest` 改按激活时间取真正最早；ComboBox 打开后未改选不写变量 |

**Phase 2 已知边界**：

1. ComboBox 的 `Options` 仍是字符串映射表（`0=手动;1=自动;2=检修`），不是可视化选项编辑器；复杂候选表后续可升级为列表编辑器。
2. Selector 的按下反馈由“写值成功后选中档换色”承担，没有独立 `IsPressed` 动画（ContentControl 派生，避免假按压）。
3. AckButton 的 `Oldest` 语义是“激活时间最早”，与报警条“严重度优先”的排序不同；两者刻意分开。
4. TextField 的 `PART_Display` 是主题侧非编辑态显示块（代码只翻 `PART_Editor`/`PART_Placeholder`）；后续若改成代码翻显隐，需同步模板契约，避免双写。

## 复刻批次建议

| 批次 | 内容 | 风险 | 验证 |
| --- | --- | --- | --- |
| 第 1 批（纯模板） | D8 底板圆角、D9 数值域四态、D7 轴控台字号、D10 报警条密度、D6 图表空态 | 低 | `UIThemeSmokeTest` + `ScadaChecks` + 渲染 PNG |
| 第 2 批（令牌迁移） | D11 硬编码颜色、D12 选中/锁定/诊断色 | 中 | 资源断链契约 + `UIThemeSmokeTest` 硬编码扫描 |
| 第 3 批（契约扩展） | D1 画面主题、D2 Unknown 态、D3 表盘阈值、D5 键盘激活 | 高 | 逐项 architect 评审 + 用户确认 |

## 验证计划（复刻后）

| 层 | 判据 | 证据 |
| --- | --- | --- |
| 构建 | `dotnet build` 0 错误（既有 warnings 不计）；`Modules` DLL 时间戳更新 | 构建日志 + `dir` |
| 静态 | 视图 XAML `hex=0`；关键绑定/命名件齐全；共享样式未改 | `UIThemeSmokeTest` 对应模式 |
| 运行时 | 控件实例化、`Element` 绑定、五态可触发、IO 编辑回环 | `ScadaChecks` / 新增断言 |
| 渲染 | 深色/浅色两套画面各 4~5 张 PNG，与 `design.html` 逐块对照 | `evidence/` |
| 回归 | `FlowCanvasChecks` 0 失败；`ScadaChecks` 全绿 | gate 日志 |
| 真机 | 打开方案 → 切页 → 改值 → 报警 → 运行窗口 | 用户验收 |

> 注意：`UIThemeSmokeTest` 本轮实际 exit 0（无 `[FAIL]`）；历史上曾因既有 GridView 红灯恒为 1，**以日志 PASS/FAIL 行为准**。改过断言源码必须 build 再 run（R65）。

## 已知偏差（Web 设计 vs WPF 复刻，预判）

1. **字体度量**：Web 用 Segoe UI Variable / 微软雅黑；WPF 同族系统字体，字宽与行高略有差异，不按 px 硬套。
2. **阴影**：Web 用多层 CSS 阴影；WPF 用 `DropShadowEffect`，层数与扩散范围略浅。
3. **动效**：Web 用 CSS transition；WPF 用 `Storyboard`/`VisualStateManager`，统一 150~200ms。
4. **SVG 几何**：Web 用 SVG 路径；WPF 用 `PathGeometry`，坐标按控件尺寸重算（现有代码已如此）。
5. **HALCON 画布**：`RenderTargetBitmap` 不捕获 `HwndHost`；图像显示图元的真实预览需真机验证。
6. **趋势曲线**：ScottPlot → SkiaSharp 在无头宿主画不出内容；像素级验证交给 `_ChartProbe5` 与真机，断言只钉数据进缓冲与绑定通道（R77）。
7. **滚动条**：WPF 滚动条约占 17px；窄面板下可用宽度比 Web 少约 17px。
8. **焦点环**：Web `:focus-visible`；WPF 用显式 `HmiFocusVisual`，键盘/鼠标焦点策略需在复刻时逐控件确认。

## 不在本版范围

- **SCADA 编辑工作台**（`ScadaToolboxView` / `ScadaPropertyView` / `ScadaLayerView` / `ScadaEditorView` 顶栏与格式工具栏）：属于宿主视图，走各自的全局基线；本版只审“图元控件”本身。
- **SCADA 弹窗族**（变量选择、报警配置、用户管理、登录、系统参数等）：走 Dialog 基线，另行盘点。
- **运行时窗口壳**（`ScadaRuntimeWindow` 顶/底条）：本版只在整屏示例中示意；其硬编码色另列 D11。
- **新增功能**：主题切换、Unknown 态、表盘阈值、过程动画、键盘激活均需契约扩展，见 D1~D12。

## 遗留与待确认（D1~D12）

| # | 项 | 需要什么 | 类型 |
| --- | --- | --- | --- |
| D1 | 画面级主题（深色/浅色） | `ScadaPage.Theme` + 控件默认值解析/页面令牌注入 | 契约 |
| D2 | 无数据/通信断线状态 | `DeviceState.Unknown` + 运行态断线写入 | 契约 |
| D3 | 表盘警告/报警阈值区 | `WarningMin/Max`、`AlarmMin/Max` 或复用报警定义 | 契约 |
| D4 | 过程动画（泵叶轮/管道流动） | 统一节拍源 + 性能预算 | 视觉增强 |
| D5 | 按钮键盘激活 | Space/Enter 处理，或先 `Focusable=False` | 行为契约 |
| D6 | 趋势曲线空态 / LIVE 徽标 | `SampleCount` 已公开；模板加空态覆盖层 | 纯 UI |
| D7 | 轴控台字号底线 | 8/9px → 10px；点位行高 23→26 | 纯 UI |
| D8 | 底板标题栏圆角 | 跟随 `CornerRadius` 的绑定/裁剪 | 纯 UI |
| D9 | 数值域只读/可写视觉 | 只改模板与样式，不动编辑/提交逻辑 | 纯 UI |
| D10 | 报警条密度与两行行高 | 行模板两行式，时间/状态靠右 | 纯 UI |
| D11 | 硬编码颜色迁移 | AxisPanel 20、IOField 1、ScadaCanvas 1、ScadaEditorView 15、RuntimeWindow 14 | 纯 UI / 令牌 |
| D12 | 选中/锁定/诊断色令牌化 | `HmiSelectionBrush` / `HmiLockedBrush` / 诊断色 | 纯 UI |

## 变更规则

设计基线确认后只读。需要调整设计时升版本（v1 → v2），并同步：

1. `design.html` 与 `README.md` 的版本号；
2. 令牌映射表与 `UI\Controls\Themes\Colors.xaml` 的实际键；
3. 已复刻的 WPF 视图与 `evidence/` 截图；
4. `docs/UI设计基线/README.md` 索引行。

> 本目录不参与任何工程构建，不打包、不部署（R126/R128）。
