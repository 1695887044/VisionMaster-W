# 2026-10-09 插件配置壳：参数面板"表格 ⇄ 卡片"布局切换（CardPropertyGrid 首次接线）

- 日期：2026-10-09
- 项目：UI（WPF / .NET 9）+ VisionMaster（插件配置壳）+ Plugin.PreProcessing（首个接入方）+ 探针 _LogViewProbe
- 范围：1 新契约（`UI\Controls\CustomControl\PropertyGrid\IPropertyGridLayoutSwitch.cs`）+ 1 主题减重（`UI\Controls\Themes\PropertyGrid.xaml`）+ 壳 VM/视图各 1 处 + 插件 3 文件
- 类型：待接线控件落地（CardPropertyGrid 从"零消费"变成有真实入口）+ 一处布局度量缺陷修复
- 口径：**构建** = `dotnet build`（UI / VisionMaster / Plugin.PreProcessing，Debug 与 Release）；**冒烟** = `UIThemeSmokeTest.exe`；**探针** = `_LogViewProbe.exe`（真视图渲染 + 控制台断言）

---

## 一、先测后做：卡片式原先是"接不进去"的

上一轮的待接线清单里写着"CardPropertyGrid → 插件配置壳可切卡片式"。动手前先用探针把**表格 vs 卡片**在同一参数列宽度下渲染出来（`_LogViewProbe\pg_layouts.png`），结果卡片式在 360px 参数列里**基本是坏的**：内容被压成一条窄带、标签竖排（"启/用"上下叠）。

量出来的原因是**卡片式的固定开销 292px**：

| 开销 | 位置 | 原值 |
| --- | --- | --- |
| 左侧分组页签条 | `PropertyGrid.xaml` 的 TabControl 模板 `ColumnDefinition` | **180** |
| 内容区留白 | 同文件 `TabControl.ContentTemplate` 的 ScrollViewer `Padding` | 24 × 2 = 48 |
| 卡片内边距 | 同模板里卡片 Border `Padding` | 32,24 → 64 |

360px 的面板切过去只剩 68px 放参数；即使把参数列加宽到 540（插件配置壳能挤出的上限），也只剩 **246px** —— 比表格式的 360 全宽还窄。**照原样接线只会交付一个更丑的界面。**

## 二、先修度量：卡片式 chrome 减重 292 → 196

| 项 | 原值 | 改后 | 理由 |
| --- | --- | --- | --- |
| 页签条宽 | 180 | **128** | 分组名是 2~4 个汉字（算子/编辑框/连接…），128 足够宽松 |
| 内容区 Padding | 24 | **14** | |
| 卡片 Padding | 32,24 | **20,16** | |

改后 540px 的面板剩 **344px** 给参数（≈ 表格式 360 全宽），重新渲染确认：540 与 360 两种宽度下卡片式都站得住了（`pg_layouts.png` 第二、三张）。这一步是**对 CardPropertyGrid 本体的改进**，与接不接壳无关 —— 它原先只在宽面板（≥900px）上才好看。

## 三、接线（五处）

| 落点 | 内容 |
| --- | --- |
| `UI.CustomControl.IPropertyGridLayoutSwitch`（新） | 可选契约，只有一个 `bool UseCardLayout { get; set; }`。**视图实现它，壳才亮开关**；不实现就只有表格式 —— 壳不猜内容类型（与 `AutoPortConfigView` 的"窗口尺寸由视图自己负责"同一条原则）。放在 UI 库而非 Core.Interfaces：它是"视图 ⇄ 壳"的界面约定，两边都引用本库 |
| `PluginConfigShellViewModel` | `CanSwitchLayout`（有没有实现契约）+ `UseCardLayout`（写下去直接落到 `_layoutSwitch`）；`OnDialogOpened` 里读一次视图现值（免得开关状态和视图实际布局对不上），`OnDialogClosed` 里清引用 |
| `PluginConfigShellView.xaml` | 头部（执行中胶囊旁）加 `CheckBox Content="卡片式"`，`Visibility` 绑 `CanSwitchLayout` —— 不支持的插件界面完全不显示 |
| `PreProcessingPlugin`（首个接入方） | `IsCardLayout`（INPC）+ 派生 `IsFlatLayout`（两个网格各绑一个，省掉"取反"转换器） |
| `PreProcessingView` | 实现契约；参数网格从"一个 FlatPropertyGrid"改成**两个网格叠在同一格**、按 `IsFlatLayout`/`IsCardLayout` 切可见性（双份的代价可忽略：算子参数 2~6 项且 Tab 内容懒加载，换来不用在代码里搬 `BindingObject`）；`UseCardLayout` 的 setter 里把参数列 **360 ⇄ 540** |

**宽度算式（改动前必须重算）**：内容区 1120（视图打开时会把宿主窗口撑到 1120×620）− 算子库 240 − 参数列 540 − 三处边距 32 = **308**，预览列 `MinWidth=300` 兜得住 —— 正好卡在线上。

## 四、验证证据

| 项 | 结果 | 口径 |
| --- | --- | --- |
| 构建 | UI / VisionMaster / **Plugin.PreProcessing** 三工程 Debug 与 Release 均 `0 Error(s)`；`PluginConfigShellViewModel` / `PreProcessing*` 无新增告警（该文件既有 nullable 告警照旧） | `dotnet build <工程> -c Debug\|Release` |
| 插件产物投递 | `Modules\Plugin.PreProcessing.dll` 与插件 bin 同时间戳（23:14，Release）—— 红线③"改了插件必须重建插件工程"已满足 | `dir Modules\Plugin.PreProcessing.dll`（投递规则见 `Plugins\Directory.Build.targets`） |
| 端到端切换链路 | `[PASS] 卡片式开关：切前表格可见=True，切后卡片可见=True，卡片网格实际宽=530（期望 >500，即参数列已 360→540）` —— 真 `PreProcessingView` + 真 `PreProcessingPlugin`，走的就是壳写 `UseCardLayout` 那条路 | 探针 `_LogViewProbe.exe` |
| 真视图渲染 | `preprocess_flat.png` / `preprocess_card.png`（1120×620 各一张，落盘时回看过）：切卡片后参数列变宽、**预览列仍占 ~385px**（表格态 ~575px），三栏没有互相挤压 | 同上 |
| 布局渲染对照 | `pg_layouts.png`：表格/360、卡片/540、卡片/360 三档 —— 减重前卡片式在 360 是坏的，减重后三档都能看 | 同上 |
| 冒烟回归 | `EXIT=ZERO`、四分区全过（Shell 全树实例化、多格布局、GridView、LogConsole + 33 条 PASS） | `UIThemeSmokeTest.exe` |
| 编码自检 | 9 个改动文件严格 UTF-8 解码通过、行数正常、中文回读正常 | `[Text.UTF8Encoding]` 严格解码 |

## 五、未决与观察

- **真机观感未验**：只有无窗口渲染。真机上还要看"卡片式"开关嵌在弹窗头部（执行中胶囊旁）是否协调、以及宿主窗口 1120 宽在用户屏幕上是否够（小屏会被工作区钳制，届时预览列会被压到 MinWidth 以下）。
- **选择不持久化**：每次打开插件配置都回到表格式（`_useCardLayout` 是弹窗实例的状态）。要做"记住上次选择"得落 `AppConfig.json`（`AppSettingsService`），本轮没动 —— 那是另一条"配置文件 schema"的线。
- **目前只有 PreProcessing 一个接入方**：它的参数是 `[SuperDisplay]` 反射渲染的（全仓唯一）。其它插件的配置界面是手写的，接这个开关没有意义；将来若有插件把参数交给属性网格渲染，实现 `IPropertyGridLayoutSwitch` 即可（**别忘了同时给参数列加宽** —— 卡片式比表格式多约 196px 固定开销，这是本次最容易被漏掉的点）。
- **卡片式的宽度下限**：减重后约 500px 面板可看，< 420px 仍会明显发挤。窄面板（如 SCADA 属性面板）不建议切卡片。
