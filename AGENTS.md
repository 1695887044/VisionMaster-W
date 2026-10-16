# AGENTS.md — 本仓库的工程纪律

> 本文件记录"**踩过一次、不许再踩**"的硬纪律，给 AI 助手与后来者看。
> 新增条目请**追加到末尾**，并注明日期与起因（细节写进 `docs/code-changes/`）。

---

## 1. PowerShell 与中文/编码（2026-10-04）

**起因**：同一根因连踩三次，其中一次写坏了仓库源文件（`ImageGallery.cs` 被双编码损坏，靠编辑工具快照无损恢复）。
**真相一句话**：本机工具链是 **Windows PowerShell 5.1**（`powershell.exe`）——它把**无 BOM 的脚本文件按系统 ANSI（GBK）读取**，命令行参数也会被 cmd 代码页转换；**只有显式指定 UTF-8 的读写才可靠**。

| # | 规则 |
| --- | --- |
| R1 | **`.ps1` 文件必须纯 ASCII**——代码、字符串、**注释**里都不许出现中文（注释里出现也会被误读成乱码并破坏解析）。需要中文常量时用码点拼：`[string][char]0x6807 + [char]0x5B9A` |
| R2 | **中文"数据"（路径/文件名/搜索词）放 UTF-8 清单文件**，脚本用 `[IO.File]::ReadAllLines($f, [Text.Encoding]::UTF8)` 显式读；**绝不在 `-Command "…"` 里传中文**，用 `-File` + 清单文件 |
| R3 | **不用 `Get-Content`/`Set-Content` 往返编辑仓库文件**（无 BOM UTF-8 会被按 ANSI 读、按另一编码写回 → **静默损坏**）。改文件用编辑工具；PowerShell 只做批量机械操作 |
| R4 | 必须用 PowerShell 写回文本时：`[IO.File]::WriteAllText($p, $text, (New-Object Text.UTF8Encoding($false)))`，且**只用 `String.Replace`**（保换行）；**禁止**在替换串里插 `\r\n` 字面量 |
| R5 | **写完必须自检三步**：① 严格 UTF-8 解码不抛 `(New-Object Text.UTF8Encoding($false,$true)).GetString(...)`；② 行数/大小合理；③ 用 Read 工具回读中文 |
| R6 | **救命绳**：编辑工具在改动前会把整份原文存到 `%USERPROFILE%\.zcode\cli\artifacts\<会话ID>\*-tool-result-*.json` 的 `files[0].beforeContent`；写坏了可从那里无损还原（快照换行是 LF，还原 CRLF 文件时要转回） |

**完整版（含三次事故复盘、安全范式模板）**：`docs/code-changes/2026-10-04-PowerShell中文路径与编码纪律.md`

---

## 2. docs/ 目录结构（2026-10-04）

**起因**：`docs/` 曾有两个进程并行整理，出现"方案说明书被归档 → 又被移回"的来回搬运。

| # | 规则 |
| --- | --- |
| R7 | **动 `docs/` 结构之前，先读 `docs/归档/README.md` 的维护约定与目标目录的 `README.md`**（每个专题目录都有自己的规则与内容清单） |
| R8 | **"未实现插件的方案说明书"属于现行设计文档**，放 `docs/<插件名>/` 持续维护，**不进归档**（归档只收"不再维护的历史件"） |
| R9 | 需要跨进程协调时：**先看文件的 `LastWriteTime`**——若最近几分钟被改过，说明有并发的整理任务在跑，先报告/确认再动，别直接搬 |

---

## 3. 既有红线（引自既有文档，别碰）

| # | 红线 | 出处 |
| --- | --- | --- |
| 1 | **插件工程必须放 `Plugins\` 下**（`Directory.Build.targets` 按目录把 DLL 投递到 `Modules\`，放别处加载不到） | `docs\图像采集\README.md` §3、两份插件方案说明书 |
| 2 | **不要把 `Core.Interfaces.dll` / `Core.Controls.dll` 等公共契约程序集拷进 `Modules\`**（会出现两份静态状态，图投进去取不出来） | `docs\图像采集\图像采集插件技术文档.md` §7.1 |
| 3 | **改了插件源码必须重建插件工程**（`Modules\` 不由宿主工程带出，只重建宿主跑的仍是旧 DLL） | `docs\code-changes\2026-09-24-HTTP收图服务端与网络推送采集.md`（"产物陈旧"事故） |

---

## 4. 智能体团队与派工约定（2026-10-04）

**起因**：仓库建立 8 个角色子智能体（`.zcode/agents/`：architect 架构师、hmi 上位机、vision 视觉、motion 运控、comm 通信、ui 界面、reviewer 审查、docs 文档），约定"**用户只发需求、主会话负责派工**"。

| # | 规则 |
| --- | --- |
| R10 | **派工是主会话的职责**：用户发需求后，由主会话判定涉及哪些角色、串行还是并行、先后依赖；用户不需要指名角色。角色定义在 `.zcode/agents/*.md`（新建/修改后需**新开会话**才会注册）。 |
| R11 | **按需派工，不小题大做**：单域需求直接派单角色；跨域需求拆解后分发；一句话级小改动主会话直接做。派出的角色按"改动清单 / 验证证据 / 未决问题"三段式交接。 |
| R12 | **大改动默认闭环**：涉及公共契约、多模块或流程语义变更时——architect 评审 → 实现 → reviewer 审查 → docs 补 `docs/code-changes/` 记录；reviewer 只报告、docs 只落盘。 |
| R13 | **对用户的汇总口径**：主会话整合各角色产出为一段交付（改动清单 / 验证证据 / 未决问题）并注明来源角色；未经构建或审查确认的内容，不得表述为"已验证"。 |
| R14 | **省请求约定（中转站按请求次数计费）**：派工优先单角色、并发角色尽量少；派工指令自带"地图"（目标文件路径、行号、已知结论、验收标准），减少子智能体自行摸索的轮次；角色文件已设 `maxTurns` 上限（架构/审查/文档 12，实现类 20），若频繁出现 `error_max_turns` 截断就调大对应角色。 |
| R15 | **省请求作业纪律（本节规则自动注入所有会话与子智能体）**：① 相互独立的读取/搜索/命令在同一回合并行批量发出，先列清单再批量动手，不边走边看；② 读过的文件不重读，小任务不建 todo，构建/验证/联调集中在最后一两轮；③ 最终汇报一次性写全（改动清单 / 证据 / 未决问题）；④ 上下文与汇报**宁全勿简**——计费按请求次数而非内容量，不为省篇幅牺牲完整性。 |

---

## 5. 插件源生成器（Core.PluginGenerators，2026-10-04）

**起因**：`[StepConfig]` 配置属性按旧写法（后备字段 + 手写 setter）在插件里占大量样板（普查 ~205 个属性 / ~1550 行）；落地 partial property 源生成器方案压缩（本轮 7 个工程 97 个属性、净减 ~350 行）。引入生成器带来两条"不重建就静默陈旧"的风险点，立此纪律。细节见 `docs/code-changes/2026-10-04-插件配置属性源生成器（StepConfig-partial）.md`。

| # | 规则 |
| --- | --- |
| R16 | **改动 `Shard\Core.PluginGenerators` 后必须重建全部插件工程**——生成物（属性实现/钩子声明）在插件 DLL 里编译期展开，不重建则运行的是旧 DLL（与红线③"产物陈旧"同一类坑）。改钩子命名/字段规则/诊断、升级 Roslyn 引用，都算"改动"。 |
| R17 | **生成器工程与产物不得进入 `Plugins\` 或 `Modules\`**：放 `Plugins\` 下会被构建约定当插件投递；`Modules\` 只放 `Plugin*.dll`。生成器以 `OutputItemType=Analyzer` + `ReferenceOutputAssembly=false` 挂接（`Plugins\Directory.Build.props`），不进任何输出目录。 |
| R18 | **`[StepConfig]` 属性统一走「三选一」写法**（自动属性 / partial property + 钩子 / 保持手写），写法与钩子契约见 `docs\新插件端口速查.md` 对应章节；partial 属性所在类必须声明 `partial`，钩子**只写实现、不写声明**（声明由生成器产出）；不满足约定报编译错误 CPG0001~CPG0005。 |

---

## 6. WPF 表格视图（ListView + GridView，2026-10-06）

**起因**：2026-10-06 真机「方案列表」弹窗的数据行整片渲染成类型全名 `VisionMaster.Models.AppSolutionEntry`、四个列头（序号/名称/注释/路径）整体消失。根因是 `VisionMaster\App.xaml:68` 把 .NET 9 Fluent 主题合并进**应用级**资源，它自带的隐式 `ListView` / `ListViewItem` 样式顶掉了框架里支持 GridView 的那份（应用级隐式样式优先于框架主题样式，故"**凡 GridView 必坏**"，与视图源码写没写对无关；同时坏了 4 个组态弹窗视图）。细节与证据见 `docs\code-changes\2026-10-06-方案列表GridView被Fluent隐式样式顶掉.md`。

| # | 规则 |
| --- | --- |
| R19 | **`ListView + GridView` 视图必须给 ListView 挂 `Style="{StaticResource GridListViewStyle}"`**（定义在 `UI\Controls\Themes\Controls\GridView.xaml` 的空样式，作用是让 Fluent 的隐式 `ListView` 样式整条让位、模板回到框架原装那份）。不挂 = **列头整体消失**；**不要把它改成隐式样式** = 没有 GridView 的普通 ListView 会凭空多出空表头。 |
| R20 | **同一视图必须自带 `ItemContainerStyle`，且行模板里必须是 `GridViewRowPresenter`**（`Columns="{TemplateBinding GridView.ColumnCollection}"`），不能用裸 `ContentPresenter`——否则列布局塌成一列，数据对象只能 `ToString()`，屏幕上就是**一行行类型全名**。 |
| R21 | **行容器内边距取 4**（列头元素 x4 + 列头 Padding 8，与行 4 + `GridViewRowPresenter` 内置的 6 落在同一条竖线）；范式照 `ScadaUserManagerView.xaml` / `SolutionListView.xaml`。守门人是 `UIThemeSmokeTest` 的静态扫描——"凡 `.xaml` 里出现 `GridView` 就必须挂 `GridListViewStyle` 且行模板带 `GridViewRowPresenter`"。 |
| R22 | **引用了 `GridListViewStyle` 的视图必须就地合并 `UI\Controls\Themes\Controls\GridView.xaml`**（`<UserControl.Resources>` → `<ResourceDictionary>` → `<ResourceDictionary.MergedDictionaries>`，用相对 pack 写法 `/UI;component/...`；范式照 `SolutionListView.xaml` / `ScadaUserManagerView.xaml`）。只挂 `{StaticResource GridListViewStyle}` 而不自合并：在没有 `Application` 的断言宿主（`ScadaChecks` 直连 `new 视图()`，`Application` 那档资源取不到）里会在 **XAML 解析期**抛"无法找到名为 `GridListViewStyle` 的资源"——2026-10-06 实测让 `ScadaChecks` 多红 4 条渲染断言。`pack://application:,,,/...` 的绝对写法在这里同样不可用，必须用相对写法。 |

---

## 7. 流程栏步骤树：模板与分支卡片交互（2026-10-09）

**起因**：2026-10-09 真机两连击——① 左侧流程栏里并行分组那两行整行显示 `VisionMaster.Models.ParallelStep`，图标/名字/两条分支全丢；② 追问"点击单个分支还会出弹窗"。① 的根因是 `VisionMaster\Views\ProcessView.xaml` 的步骤树按**模型类型键**挂隐式模板（ActionStep / ConditionStep / WhileStep / ForStep / StepCollection 都有），新增的 `ParallelStep` 没跟上——WPF 找不到模板就回退默认模板把 `ToString()` 上屏，且缺 `HierarchicalDataTemplate` 意味着没有 `ItemsSource`、分支一个都不渲染。与第 6 节 GridView 事故同族（症状都是"屏幕上是类型全名"）；② 的根因是"分支卡片能进按选中项取参的命令链 + 对话框兜底分派"，见 R25。细节见 `docs\code-changes\2026-10-09-流程栏并行分组显示为类型全名（流程树模板缺失）.md`。

| # | 规则 |
| --- | --- |
| R23 | **新增/改名的 `StepModel` 子类，必须同步给 `ProcessView.xaml` 补 `DataType="{x:Type models:<类型名>}"` 模板**——按**具体类型**逐个给（别指望基类模板兜底）；容器类必须是 `HierarchicalDataTemplate` + `ItemsSource="{Binding Children}"`，否则流程栏里就是**类型全名一行、分支整体消失**。守门人：`FlowCanvasChecks` 的 `ProcessTreeTemplateChecks`（U1-U5，进常规 gate 基线；例外清单 `BreakStep`/`ContinueStep`/`ReturnStep` 显式声明 + 反向断言防过期）。视觉复核：渲染探针场景 F（`--render` → `process-tree-render-F.png`）。 |
| R24 | **流程栏分支胶囊的文案只有 `StepCollection.DisplayName` 一个来源**（它已把"分支名 + [ 表达式 / 未绑定条件 ]"拼好）——不要再挂独立的 `Expression` 文本块，否则条件已绑定的分支显示成 `If [ Score > 80 ] [ Score > 80 ]`（2026-10-09 出图抓到）。另：给分支做"按父容器类型"的换色，用 `RelativeSource AncestorType=TreeViewItem, AncestorLevel=2` 绑父容器独有属性（并行分支的蓝身份就是这么做的，见 `ProcessView.xaml`），零转换器且不影响其它容器。 |
| R25 | **流程栏"分支卡片"（`StepCollection`：分支 1/分支 2/If/Else/循环体）不是算子，不许进"按当前选中项执行"的命令链**。三道闸缺一不可：① 视图侧 `ProcessView.xaml.cs` 在 Preview 鼠标事件上拦（**左键**不选中、不派发双击；非并行分支的**右键**也不出菜单）；② VM 侧 `ProcessViewModel.SelectStep` 收到 `StepCollection` **清空选中**——留旧值就是"看着选中了分支、命令却打在旧算子上"的错靶（删除/重命名都会误伤）；③ `StepParameterDialog.Open` 只对 `ActionStep`（插件参数）/ `ConditionStep`/`ForStep`（条件编辑器）三个白名单出口开窗，其余返回 false、由调用方给非模态提示（不许"其余全弹条件编辑器"——编辑器认不出节点就是**空窗**）。守门：`FlowCanvasChecks` V1-V8（IDialogService 形状桩）+ 渲染探针场景 F 的点击拦截自检行。2026-10-09 事故与修法见 `docs\code-changes\2026-10-09-流程栏并行分组显示为类型全名（流程树模板缺失）.md` §5-§7。**分支卡片自己的展开/折叠箭头必须放行**（判据里给 `ToggleButton` 开豁免——箭头就在分支卡片的容器模板里，不豁免就是"鼠标点不开分支、分支内算子看不见"；2026-10-09 审查抓到的回归，现由渲染探针场景 F 的三条差异判据守门：分支卡片拦 / 箭头放行 / 算子卡片不拦）。**唯一例外：并行分组的分支胶囊右键**——出它自己的两项菜单（重命名/删除本条分支），**菜单锚点=胶囊、命令靶=命中的那条 `StepCollection`**（`CommandParameter` 绑 `PlacementTarget.DataContext`），与"当前选中项"无关，所以不违反三道闸；放行条件必须同时满足"命中元素祖先链里有 ContextMenu"（否则右键分支行空白背景会漏到父级 TreeView 菜单）。**若将来让 `SelectStep` 接纳 `StepCollection`，必须同时给 `TreeViewBehavior.TreeView_MouseDoubleClick` 补"数据项是 StepModel"的守卫**——它无条件 `item.IsSelected = true`，本批只在 VM 侧接住。 |
| R26 | **并行分组的编辑入口按"结构 vs 参数"分工**：**结构动作**（添加分支 / 重命名分支 / 删除分支 / 重命名分组）在**流程栏右键**——组头右键 →「添加分支 / 重命名分组」（靶=选中组头，`SelectStep is ParallelStep` 才显示，上限 `ParallelStep.RecommendedMaxBranches` 由命令内守卫提示），分支胶囊右键 →「重命名/删除本条分支」（靶=命中分支）；都走 `ProcessViewModel` 的结构命令族，都过运行锁。**版本号口径**：分支名与分组名不一样——分支名（`StepCollection.StepName`）不在 FlowModel 监听面里，命令**手动 `Version++`**；分组名（`StepModel.StepName`）走 setter 自动推，**不手动**（W7 两条对照断言防搞混）；增删分支由容器 `Children` 集合变更自动监听。**参数动作**（执行模式 / 失败聚合 / 汇合超时）**不再有专属视图**（2026-10-10 用户裁决"没必要新建一个视图"）：双击组头（或右键→模块参数）→ `StepParameterDialog` 出口 `ParallelStep → EasyDialog.ShowPropertyGridSync("并行分组参数", new ParallelGroupEditModel(...))`——**FlatPropertyGrid 反射 `[SuperDisplay]` 特性生成**（草稿类在 `VisionMaster\Models\ParallelGroupEditModel.cs`），确认才 `ApplyToModel()`（只写不同值、全走语义 setter）+ `Version++` 兜底，取消=草稿丢弃；**没有 `[SuperDisplay]` 的属性一律不出现**（PropertyGridDefaults 契约）。守门：V1/V6（无旧视图残留）/V8（结构命令）/W1-W7（EditModel 事务性）。 |
| R27 | **`ICommand` 的参数类型必须按"绑定来源"选，不许想当然强类型化**：`CommandParameter` 绑的是 **object 型属性**（如 `SelectStep`）、或 XAML 里 `PlacementTarget.DataContext` 这类**编译器不检查**的表达式时，命令一律用 **`DelegateCommand<object?>` + 处理器首行 `is` 判型**（拿错类型 = 静默空跑）；`DelegateCommand<T>` 只在"参数类型编译期可证"时用（常量枚举、绑定到强类型属性）。**踩坑实录（2026-10-10 真机崩溃）**：结构命令写成 `DelegateCommand<ParallelStep?>`、菜单 `CommandParameter="{Binding SelectStep}"`（object），选中项从并行组切到普通算子时 WPF 触发 `MenuItem.UpdateCanExecute()` 把 `ActionStep` 塞进 `CanExecute` → Prism 强转 `ParallelStep` → `InvalidCastException`（栈落在 `ProcessViewModel.set_SelectStep` 的属性通知链，看起来像"选中项赋值炸了"，其实炸在菜单命令）。守门：`FlowCanvasChecks` V8 的"结构命令拿到非目标类型 → 不抛异常、静默空跑"（把命令改回强类型泛型它必红）。 |

---

## 8. CodeGraph 索引使用纪律（2026-10-10）

**起因**：2026-10-10 接入 CodeGraph MCP（v0.21.0，独立安装于 `C:\Users\16958\.codegraph`，以 `codegraph-server.exe --mcp --workspace "D:\C#\VM"` 供数）。实测它对 C# 源码层的结构查询好用（当日新落的代码已在索引内、符号查询 20~40ms），但有三个盲区：**XAML 不在索引**（搜 `GridListViewStyle` 只回 `.cs` 引用点，`GridView.xaml` 本体无节点）、**源生成器产物看不到**（`[StepConfig]` partial property 的生成半边无节点）、**第三方源码稀释结果**（`DLL\HslCommunication`、`VM3.0-main` 已入索引，`find_entry_points` 15/15 全是第三方，宽泛词命中率极低）。另：索引新鲜度**取决于重建**——陈旧时是"静默少返回"（新调用方查不到），比报错更隐蔽，与红线③"产物陈旧"、R16 同类坑。细节与实测证据见 `docs/code-changes/2026-10-10-CodeGraph索引使用纪律.md`。

| # | 规则 |
| --- | --- |
| R28 | **大改动后跑一次 `codegraph_reindex_workspace` 保持索引新鲜**。判据同 R12 闭环范围：公共契约、多模块、大量文件增删/改名（如 `StepModel` 子类新增、契约签名变更）落地后重建（增量即可；解析器/索引数据异常时 `force`）。单文件小改可用 `codegraph_index_files` 点对点更新。**不重建 = 后续影响面查询漏项**——索引是给"改契约前的爆炸半径"当参谋的，陈旧了反而误导。 |
| R29 | **查询分工：公共契约影响面优先 CodeGraph，XAML 与生成器相关一律 grep**。① **优先 CodeGraph**——"谁引用了 X / 谁调用了 X / 影响面 / 调用链 / 循环依赖"（`analyze_impact` / `get_callers` / `find_by_imports` / `symbol_search`），面向公共契约/核心类型；接口实现、重载、跨工程引用是它的强项（grep 易漏），服务 R12 大改动闭环的影响面盘点。② **一律 grep/Read、不用 CodeGraph**——XAML 层（`.xaml` 模板/样式/绑定/资源；R19-R22 全在此层）、源生成器相关（`[StepConfig]` partial property、钩子、生成产物；R16-R18 相关排查）、`.ps1`/清单/JSON/文档等非代码文件。③ **查询词带项目前缀或具体类型名**——第三方源码已入索引，宽泛词（如 `Process`）会返回大量同名第三方结果。 |

---

## 9. 全局资源字典的引用作用域：分册之间互相看不见（2026-10-10）

**起因**：真机一打开「变量管理」弹窗即闪退 —— `XamlParseException 在"System.Windows.Markup.StaticResourceHolder"上提供值时引发了异常。行号为"20"，行位置为"4"`，内层 **无法找到名为"DialogCombo"的资源**。崩点行号指向 `UI\Controls\Themes\Dialog\Dialog.Misc.xaml:20`（`DialogComboInGroup` 的 `BasedOn="{StaticResource DialogCombo}"`），而 `DialogCombo` 就定义在隔壁分册 `Dialog.Input.xaml` —— **键在全仓确实存在**，所以编译期、"键有没有定义"的静态扫描（`RunMissingResourceKeyContract`）与探针的形状检查全都放行；这是 2026-09-23 两次同族事故（`DialogShadowColor` 的 Freezable、模板里的 `Icon`）之后的**第三次**，也是 10-01「皮肤拆分成 `Dialog.*.xaml` 分册」埋下的跨册引用第一次被真机点到。同一轮由新的静态断言顺带查出并治掉 4 处同类：`DialogIconFont`（三个分册跨册引用：两支 `Setter.Value` 静默退成系统字体 = 卡片标题图标变豆腐块，模板那支会抛）、`LogConsole.xaml` 的 `AccentBrush`（分组模板一启用就会抛）、`ExpandCollapseToggleStyle`（不抛，静默落回框架主题的同名键 → 箭头换了支）、`BtnExtension.xaml` 的 `{StaticResource Solid}`（该键**全仓不存在**，静默失效）。细节与前后证据见 `docs/code-changes/2026-10-10-变量管理弹窗打不开（Dialog分册跨册BasedOn断链）.md`。

| # | 规则 |
| --- | --- |
| R30 | **写在全局 `ResourceDictionary` 分册里的 `{StaticResource K}`，K 必须能在「本分册 + 本分册自己 merge 的子树」里解析**。兄弟分册互相看不见（哪怕它合并顺序在前、哪怕全仓只此一份定义）。落脚点决定后果：`BasedOn` / 模板内容（`DataTemplate`/`ControlTemplate`）/ `Freezable` 内 → **抛 `XamlParseException`**（弹窗直接打不开，编译期全绿）；普通 `Setter.Value` → **不抛，值静默不生效**（典型：图标字体拿不到 → 豆腐块）。若同名键在框架主题字典里也有（如 `ExpandCollapseToggleStyle`），也不报错、只是**静默换了一支**——新断言会判它红，这是故意的：要求把键定义到本分册。 |
| R31 | **跨分册引用只有两条合法出路**：① **能同文件就同文件**（`...InGroup` 这类"基类变体"搬回基类所在分册 —— `DialogInputInGroup`/`DialogComboInGroup` 就是这么治的）；② **多分册共用的令牌搬到第一层令牌字典 `UI\Controls\Themes\Colors.xaml`**（每个样式分册都已就地 merge 它 —— `DialogIconFont` 与更早的 `AccentBrush` 都是这么治的）。**禁止把 `StaticResource` 改成 `DynamicResource` 绕过**（把编译期问题推迟成运行期噪音），**也禁止新增"分册 merge 兄弟分册"的边**（会滚出分册依赖网，甚至成环）。 |
| R32 | **守门人**：`FlowCanvasChecks` 的【资源断链】两条契约（`VariableBindingCheck.RunDictionaryScopeContract`，进常规 gate 基线）——从 `App.xaml` 走合并链，逐分册算"窄作用域键集"，引用不在集合里即红（豁免：视图、`{StaticResource {x:Type …}}`、`DynamicResource`、注释）。**动过资源合并结构 / 搬过键必须跑**；不构建时用 `tools\audit_resource_scope.ps1`（同一条规则，**两处必须同步**；老的 `audit_chain_reachability.ps1` 已作废为转发壳——它的旧模型"先前已合并的兄弟分册算可达"实测会误报 52 条、且漏掉本次真断链）。运行期对照用 `_ResProbe`（按 App.xaml 顺序真合并字典 + 落地全部模板 + `Measure/Arrange` 真弹窗视图，`RESULT: ALL_OK` 才算过）。**两条已知盲区（复核记账，仓内零实例）**：① 同文件内"后置键"（`BasedOn` 指向本文件后面才定义的键，WPF 同样抛）不判——按"整文件键并集"处理，要根治需给 `BasedOn` 加顺序判据；② 引用与模板/`Setter.Value` 开标签同行时归类会落 SILENT 桶（不影响红绿，两桶都要空）。 |

---

## 10. 端口语义决定"能用哪个控件"：名字≠值（2026-10-10）

**起因**：真机反馈「变量赋值」作用域=全局变量时，**在变量绑定弹窗里选中全局变量 → 编译不通过**（图像/数组这类塞不进 string 的类型走编译期 `[连线类型不匹配]`；数值型能编过但运行期"名字"变成了那个变量的**当前值** → `找不到全局变量「1485」`），**只能在弹窗的常量框里手打变量名**；照做之后值填 `21` 仍然报 `全局变量「OKCount」的类型是 Int32，无法写入 String` —— 两条同一个根：**「变量名」那一行要的是"名字"，而通用变量绑定弹窗给的是"值"**（`LinkKind.GlobalVariable` 连线的语义是"运行期由该变量提供值"）；而界面上的手填框只能产出**文本**，宿主写入器却按变量声明类型硬守门。细节、复现输出与修法见 `docs/code-changes/2026-10-10-变量赋值写全局变量（名字行候选与文本值按类型解析）.md`。

| # | 规则 |
| --- | --- |
| R33 | **一个端口/字段的语义决定它能挂什么入口，不许"能用就行"地挂通用控件**：语义是**值**的（要接上游结果）才配通用「变量绑定」弹窗 / `LinkableValueEditor` 的 🔗；语义是**名字**的（变量名、步骤名、卡地址、端口名）**不许挂绑定弹窗**——它只会落一条"值由对方提供"的连线，后果要么**编译期类型不匹配**、要么运行期**拿值当名字**（"写成功了但写去别的变量"这类静默故障）。名字类端口的正解是**手填 + 候选下拉**，候选由宿主经 `IPluginConfigContextProvider` / `PluginConfigContext` 透传快照（范式：`Cameras` → `Variables`；组装在 `VisionMaster\Helpers\PluginVariableOptions.cs`，插件侧自己够不到变量管理与流程图纸）。**老方案里已经挂过这种连线的**：视图要在提示里说明"该行原由上游提供（地址）"，并在用户改动该行时 `RemoveLink` —— 否则运行期"有连线就以连线为准"，键盘敲下去不生效（这条已在 `FlowCanvasChecks\VariableAssignmentCheck.cs` 的 `[A4]` 里钉住，含"名字行不许再接回通用绑定弹窗"的静态扫描）。 |
| R34 | **手填界面只能产出文本**（`InputPort<object>` / JSON 快照 / 文本框都一样）——所以**写入侧**必须按目标声明类型解析一次（`Convert.ChangeType`；`GlobalVariableWriter.TryCoerceText` 是范例），否则"给 Int32 变量填 21"在界面上**永远不可能成功**。口径是**宽进严出**：能转就转、转不了报错并把**被拒的原值**写进消息；**只放宽文本**——类型化的值（double 塞进 int 变量、HImage 塞进 string）仍按原样报错，那是上游接线错了，静默截断/转换会把错因推迟到很远的地方。数值文本另加两条：**不变区域性**（别让区域设置改结果）、**拒绝千分位分隔符**（默认解析会把 `2,5` 读成 `25`）。 |
| R35 | **"用户意图"只能由真实输入事件判定，不许从"值变了"倒推**：端口/属性的变更来源包括程序灌值（试运行 `PluginTestRunner.BridgeInputs` 把上游实际值写进配置实例端口、候选重算后的回填、`ApplyConfigValues`），拿 `ValueChanged` 当"用户编辑"会在这些路径上**误触用户动作**（典型：用户什么都没动、老连线被解除，改的是活模型，点"取消"也退不回来）。判据要接在真实输入事件上（`PreviewTextInput` / `PreviewKeyDown` / `SelectionChanged`，或控件的 `TextChanged` 由视图自己上报给插件一个 `NotifyXxxEditedByUser()`）。另：**给绑定集合重填候选时先记住端口里的值、重填后顶回去**——可编辑 `ComboBox` 在有选中项时 `ItemsSource.Clear()` 会把 `Text` 清空并沿 TwoWay **把空串推回端口**（"选个名字→切作用域"就静默清名）。此两条与 R33/R34 同批实测，见 `docs/code-changes/2026-10-10-变量赋值写全局变量（名字行候选与文本值按类型解析）.md` §复核。 |

---

## 11. 离屏烘焙件只许有一份：`AnnotationBaker`（2026-10-13）

**起因**：为「结果显示」插件（`Plugin.ResultDisplay`，把任意算子输出烧进图像像素）落地共享烘焙件 `Services\Core.Halcon\Helpers\AnnotationBaker.cs` 时，按用户裁定把仓库里既有的四份同源实现迁了进去（BlobDetect / CaliperMeasure / BeadInspect / Matching 的离屏渲染器）。**迁移当场抓出两条真 bug，不是纯重构**：BlobDetect 与 CaliperMeasure 那两份**都漏了 `SetPart`**，标注底图整片塌成窗口底色——实测 `(150,20)=0 (150,180)=0`（期望 100/200）、`(470,320)=0`（期望 255）。这两条之所以能存活到今天，是因为 `BlobDetectChecks` 里有一道**误判的"环境闸门"**：底部条带跨度 ≤50 就把断言记成"跳过：本环境离屏窗口不渲染底图层次"，而 `AnnotationBakerChecks` B1 早就实测过 buffer 窗口能正常铺满底图（四角像素 == 底图灰度 100）——**闸门把一个真 bug 伪装成了环境不支持**。迁移后同一条断言直接变绿（跨度=100 / uint2 拉伸后跨度=255）。细节、六条纪律的实测数字与前后对照见 `docs/code-changes/2026-10-13-结果显示插件.md` 与 `docs/结果显示/结果显示插件方案说明书.md` 第 14 章。

| # | 规则 |
| --- | --- |
| R36 | **禁止再复制离屏烘焙件**。全仓 `OpenWindow` **只许出现在 `Services\Core.Halcon\Helpers\AnnotationBaker.cs` 一个文件里**，外加**一份永久豁免**：`Plugins\Plugin.ImageScript\HDevDisplayBackend.cs`（它不是烘焙件——HDevEngine 按 `dev_*` 算子**逐条驱动**它，窗口状态是 `[ThreadStatic]` 的、还要 `HandoutWindow` 把窗口交给脚本，"画什么"在脚本跑完之前根本不存在，`BakePlan`（跑完才攒得出来）表达不了它）。**豁免清单只许变短、不许变长**。要往图上烧东西就 `new AnnotationBaker()` + 攒 `BakePlan`（`Shapes`/`Objects`/`Texts`）+ `Bake(底图, plan)`；四个插件迁移后各自只剩"攒 plan 的薄壳"（BlobDetect 210→90 行、CaliperMeasure 175→62 行、BeadInspect ~120→~30 行、Matching 删掉 ~78 行渲染体 + 4 个窗口字段）。守门人：`FlowCanvasChecks\ResultDisplayCheck.cs` 静态扫描 **④**（全仓只有一处开窗）/ **④b**（豁免清单没有过期项——迁完的文件必须从清单删掉）/ **④c**（`Plugin.ResultDisplay` 自己零开窗/dump/SetPart）。 |
| R37 | **六条硬纪律 + 两条状态纪律，只在 `AnnotationBaker` 里实现一遍**（每条都有仓库实测事故）：① `OpenWindow` 的 mode **只能 `'buffer'`**（其它值报 **#1305**）；② 窗口**按尺寸常驻复用**、`Dispose()` 才关（反复开关 → **#9302 死锁**，HALCON 不支持改窗口尺寸）；③ 每轮**无条件** `SetPart(win,0,0,h-1,w-1)`——漏了就是**底图缩在左上角、其余全黑**（本轮实测抓到的正是这条），常驻窗口复用时历史 `dev_set_part` 还会**串轮污染**；④ 文字坐标系**只用 `"window"`**（无显示会话下 `"image"` 整段画不出来）；⑤ `DispText` 必须**显式**给 `box`——实测 **`box` 默认就是开的**（18303px vs 3754px），所以真正的风险不是"画不出框"而是"把决定权交给默认值"，`BakeText.Box` 因此是 **record struct 的必填位置参数**（⚠ 名字是**大写 `Box`**，写 `box:` 会 CS1739）；字号只能走窗口级 `SetFont`；⑥ `DumpWindowImage` 的**源对象立刻 `Dispose`**（`new HImage(shot)` 是独立句柄，但 `shot` 是源对象；500 次不释放 ≈ **漏 160MB**，B7 实测 500 轮私有内存增量 **−4.4MB**）。外加：**整段 `lock`** 罩住"改窗口状态 → 画 → 回读"（窗口状态是全局的，两路绘制会互相串色）；**`SetLineWidth` 用完还原 1、`SetDraw` 还原 `"margin"`**（都是窗口级状态、跨渲染保留）。**绘制顺序固定为 `Shapes → Objects → Texts`**，要改层序就改 plan 的入列顺序、**不要改 baker**（顺序不固定 = 上面那些窗口级状态纪律没法只写一遍）。 |
| R38 | **"跳过"型断言必须能被反证，否则它就是给未来回归留的静默放行口**。写 `Check(name, true, "跳过：本环境不支持 X")` 之前，必须先回答"**谁证明了是本环境不支持、而不是代码有 bug**"，并把这个反证写进跳过理由里（本轮的正解是删掉闸门改硬断言，因为 `AnnotationBakerChecks` B1 已经反证了"环境不支持"）。同类风险还有：**迁移/重构之前先织网**——本轮的做法是先给四份各补一条像素级"底图保真"断言（共用尺子 `FlowCanvasChecks\PixelProbe.cs`；⚠ `min_max_gray` 的**第一个入参是区域不是图像**，且只吃单通道），**再动手改**，网当场兜住了那两条 bug。"零断言覆盖所以不敢动"不是理由，是**先补断言**的理由。 |
| R39 | **文字宽度只有烘焙件能量——"对齐"是 `AnnotationBaker` 的能力，调用方不许估宽度**（2026-10-14，起因：给「结果显示」加九宫格文字锚点，实现时发现水平与垂直分量必须分家）。三条：① 文字像素宽取决于**窗口当前字体**，而窗口只存在于 baker（R36 与扫描 ④c 禁止插件自己 `OpenWindow`/`SetPart`）；调用方凭字符数估宽必歪（等宽字体也有小数点/负号/中文的差异），歪的结果是"贴边对不上、极端时整行推出画布"。② 所以 `BakeText` 用 **`HAlign`**（`Left`/`Center`/`Right`，**默认 `Left` = 历史行为**，用 `init` 属性加、不动位置参数）表达"哪一边贴边"，`Col` 的含义随之变成左边缘/中心/右边缘，换算在 baker 内做并**夹回 `[0,w-1]`**（量不到就退回左对齐 + 一条 Warn，宁可贴边也不许丢字）。③ **`GetStringExtents` 必须排在 `TrySetFont` 之后**——字号是窗口级状态，没设就量，量到的是上一号字体的宽。⚠ 本仓 HALCON **没有** `GetTextExtents`，能用的名字只有 `GetStringExtents`。**反过来，垂直分量必须留给调用方**：块高 = 行数 × 行距，行数只有攒完 plan 才知道，baker 看到的是一堆独立 `BakeText`、不知道"这一堆是不是一组"。守门人：`FlowCanvasChecks\AnnotationBakerChecks.cs` 的 **B8**（墨迹包围盒实测：默认 == 显式 `Left`、右缘 378/目标 380、中心 199/200，同时反证 buffer 窗口无显示会话下**量得到**字宽）+ `ResultDisplayCheck.cs` 的 **U1~U9**。细节见 `docs\code-changes\2026-10-14-结果显示文字位置.md` |

---

## 12. 枚举上屏必须走中文显示名（2026-10-14）

**起因**：真机一句"几何查找的下拉框应该是中文，不要英文"——截图里「查找类型」显示 `Circle`、「拟合鲁棒算法」显示 `Regression`。**根因不在插件**：成员上本来就标了 `[Display(Name = "圆")]`，是框架的**两处候选生成点**（`Shard\Core.Controls\LinkableValueEditor.xaml.cs` 的自动参数面板 + `VisionMaster\ViewModels\DialogViewModels\VariableBindingViewModel.cs` 的绑定窗口）各写一份 `new StepConfigOption(成员名)`，把已经写好的中文丢掉——ComboBox 侧的 `DisplayMemberPath="Display"` / `SelectedValuePath="Value"` 一直是通的，**管道没问题，没人灌值**。顺带查出「轴运动」`Plugins\Plugin.Motion.Steps\MotionEnums.cs` 三个枚举**一个 `[Display]` 都没有**，界面上就是 `Absolute` / `Relative`（同一个坑，且是现场天天用的那个）。细节与实测见 `docs\code-changes\2026-10-14-枚举下拉中文显示名.md`。

| # | 规则 |
| --- | --- |
| R40 | **枚举候选一律走 `StepConfigOption.ForEnum(枚举类型, 成员名)`，不许再出现 `new StepConfigOption(Enum.GetNames(...) 的元素)`**（显示取成员上的 `[Display(Name)]`、没标就回退成员名）。四条口径：① **显示走中文、值必须仍是成员名**——`.vms` 与流程存的是成员名，写回走 `Enum.Parse` 按名字解析；拿"圆"当值 = 改一次显示名打断一条已配好的流程（与 `MotionDescriptor` 的"引用一律用 Id 不用显示名"同坑，也是 `StepConfigOption` 这个类存在的全部理由）。② **回退到成员名，绝不回退成空白**（空白等于下拉里看不出选了什么，比英文更糟）。③ **HALCON / SDK 字面量不进界面**：`'regression'` / `'bilinear'` / `'tukey'` 只留在注释与文档里，映射由代码按成员 switch 生成（改显示名不会改到传给算子的参数）；`Huber` / `Tukey` 是人名、无通用译名，保留原字。④ **新增枚举成员必须同时补 `[Display(Name="中文")]`**，否则守门断言红。**插件自带视图**（CaliperMeasure / ResultDisplay 的 `EnumDisplayNameConverter`）读的是同一个特性，所以补一次 `[Display]` 两条路径同时生效。守门人：`FlowCanvasChecks\MotionZMotionChecks.cs` 的 6 条——STA 线程真开一个 `LinkableValueEditor` 挂 `InputPort<GeometryFindKind>` 量出 `Line→直线 Circle→圆 Ellipse→椭圆`、"下拉选完写进端口的仍是 `Circle`"（防串位）、没标 `[Display]` 的回退（`StringComparison.Ordinal`）、两处生成点共用 `ForEnum` 的静态扫描、几何+运动六个枚举逐个过中文闸门（`23/23`）。**还欠的清单**（机制已修好，补 `[Display]` 即可）：`Plugin.PreProcessing\Models\OperatorEnums.cs`、`Plugin.Utility`（数学/逻辑/比较/类型转换四个插件的运算枚举）、`Plugin.DataRecord`、`Plugin.ExcelExport`、`Plugin.ResultUpload\UploadFieldDef`、`Plugin.ImageScript`/`Plugin.CSharpScript` 的 `ScriptVarType`；各插件的 `StatusLevel`（Info/Warning/Error）是内部日志级别、不上屏，不算欠账。 |

---

## 13. 插件自带帮助手册的打包约定（2026-10-14）

**起因**：用户要求"把没有帮助手册的补齐，要特别详细的"。普查口径：**工具箱条目 = 带 `[Display(GroupName=…)]` 的插件类**，共 **42 个 / 31 个程序集**，其中 **26 个程序集一份手册都没有**——界面上它们走的是第三层兜底（宿主用 `[Display]` 元数据 + 注册期端口表自动生成一页，页脚带"自动生成"徽标），现场看到的就是一张干巴巴的端口清单。本轮把 42 个条目全部补成自带手册（**77 页 / 10088 行**）。细节见 `docs\code-changes\2026-10-14-插件帮助手册补齐.md`。

| # | 规则 |
| --- | --- |
| R41 | **新增插件类（工具箱条目）必须同时交自带手册**，且四条打包约定一条不许少：① 手册放本工程 `Help\` 下、csproj 里挂 `<EmbeddedResource Include="Help\**\*" />`（**没有全局 props 规则**，每个工程自己挂）；② **一个程序集里有多个插件类时，各插件的页放 `Help\{插件类名}\` 子目录**，目录名逐字等于 C# 类名——否则同程序集的插件会显示同一批页面（宿主 `HelpCatalogService.NarrowToPlugin` 先按 `类型名.` 收窄，收不到才回退"不在任何类型目录下的扁平页"）；③ **文件名必须 `NN-` 前缀**（页序 = 资源名的 Ordinal 序），**每个文件第一行内容必须是 `# 一级标题`**（页标题就取它，且只在正文开始前扫描）；④ 无 BOM 的合法 UTF-8、正文合计 **≥70 行**、不许出现 `TODO / 待补充 / 待完善 / （略） / (略) / TBD`。守门人：`FlowCanvasChecks\HelpManualCheck.cs` 的 **HM0~HM11**（进常规 gate 基线）。**这套约定所有失败模式都是静默的**——少挂一条宿主就当没有手册、悄悄退回自动生成页，不报错也不提示，所以只有静态断言能守。手册的**事实来源只有源码**：端口名/默认值/取值范围/错误文案逐条对，厂商码之类映射要指着驱动的 switch 抄（例：`HomeMode` 的 C# 值是 0~7，正运动 `homemode` 是 1/2/17/18/19/23/27/37，写手册时列名必须写明"正运动模式码"）。 |
| R42 | **加完手册必须验证 DLL 真的带了资源，增量构建会静默漏打包**（2026-10-14 实测：`dotnet build VisionMaster.sln` 报 0 错误、csproj 里 `EmbeddedResource` 也在，但 `Modules\Plugin.ImageAssign.dll` 里一个 `Help` 资源名都搜不到；对该工程 `--no-incremental` 之后 `Plugin.ImageAssign.Help.01-overview.md` 才进去）。**根因是 MSBuild 的增量判定没把"新挂上的内嵌资源项"当脏**（与红线③"产物陈旧"、R16/R28 同一族）。判据（不能只看构建退出码）：`tr '\0' '\n' < Modules\Plugin.X.dll \| grep -a "\.Help\."` 数一下资源名，**必须等于该工程 `Help\` 下的 .md 数**。给 CI/收尾用的稳妥做法：**收尾那一次全量重建用 `--no-incremental`**（或先 `dotnet clean`）。 |

---

## 14. 主界面停靠布局改版必须升结构版本（2026-10-15）

**起因**：真机主界面默认停靠布局重排——左栏「程序 / 流程栏」由**并排两列**改成**一列上下堆叠**，「流程画布」从左侧栏的标签页**搬进中央文档区**（`Shell.xaml` 停靠子树，约 `:813-970`）。这次三个面板的 **ContentId 一个没改**，只靠既有 `LayoutXmlCoversAllPanels` 覆盖度校验**完全拦不住**磁盘上的旧布局。细节见 `docs\code-changes\2026-10-15-主界面布局重排（左栏程序流程栏上下堆叠+画布搬中央文档区）.md`。

| # | 规则 |
| --- | --- |
| R43 | **改了 `Shell.xaml` 的面板归属 / 停靠语义（增删面板、换 pane、anchorable↔document、改标签页分组），必须同时递增 `VisionMaster\Services\LayoutHelper.cs` 的 `CurrentLayoutSchemaVersion`**。只靠 `LayoutXmlCoversAllPanels` 覆盖度校验拦不住"ContentId 全不变的重排"——`LayoutHelper.ContentFactories` 里那几个 `ContentId` 全都还在旧 `Layout.xml` 里，覆盖度照常放行，于是磁盘上旧的 `Layout.xml` / `DefaultLayout.xml` 会把**老排法原样恢复回来**，改造在老用户机器上**等于没发生**（与红线③"产物陈旧"、R16/R28/R42"不重建就静默陈旧"同一族）。`CurrentLayoutSchemaVersion` 是 **ContentId 之外的结构指纹**（`LayoutHelper.cs:33-45`）；`IsLayoutXmlUsable`（`:258-288`）对 `version < 当前` 返回 false → `LoadFromString` 放弃本次恢复、**只跳过不删用户的 `Layout.xml`** → 回退 `Shell.xaml` 声明的默认布局；`Reset()`（`:293-327`）对过期出厂快照同样作废删除、下次启动 `CaptureDefaultLayoutSnapshot` 重捕获。**升级后老布局被拒是机制的本意**，但要在记录里写清后果：**用户自拖的布局（属性/图层位置、浮动窗口）会静默作废**，属"升级必读"级信息。 |
| R44 | **`UIThemeSmokeTest\ShellCopy.xaml` 是 `Shell.xaml` 的结构副本（手工维护、无任何断言）**——改 `Shell.xaml` 的 docking 子树**必须同步它**，否则冒烟的"全树模板实例化"覆盖不到新结构。这份副本**有意剥掉了** `Window.InputBindings` / 全部 `commands:*` 绑定 / 3 个标题栏按钮，这份"有意偏差"没有任何断言守（既有隐患）。 |
| R45 | **`AttachDockWatchers` 必须两条路径都挂**：① `LayoutHelper.LayoutLoaded` 事件（**成功恢复布局后**才触发，`LayoutHelper.cs:225`）；② `Shell.OnContentRendered` 里 `LayoutHelper.Load()` 之后**无条件补挂一次**（`Shell.xaml.cs:98`）。只在事件里挂 = 布局恢复失败时（首次启动没有 `Layout.xml`，或旧布局被结构版本闸拒绝）事件不触发 → 活动栏 5 个 `Toggle` 停在初值 `false`、面板都在却**一个高亮都没有**，直到用户点一次开关才恢复。`AttachDockWatchers` 实现先解绑再挂、幂等（`ShellViewModel.cs:266`，`public`），重复调用安全。 |
| R46 | **守门人**：`UIThemeSmokeTest\Program.cs` 的 `RunShellLayoutChecks()`（`:1252-1358`，**9 条**，进冒烟基线，`Main` `:499` 计入返回码）。判据只认"元素类型 + 谁是谁的父/兄弟"，不写死行号与空白，所以改缩进 / 改注释不误伤；像素宽度（左栏 `20*` / 中央 `70*` / 程序 `DockHeight="180"`）**刻意不钉**，避免正常调参被误判为回归。变异反证已做（`Vertical`→`Horizontal` 准确变红）。**注意冒烟退出码恒为 1**（GridView 扫描命中 vendored 目录的既有红灯），新闸成败以日志行为准。 |
| R47 | **活动栏整条下线（2026-10-15 三度改版后的最终形态）；组态专属面板由「模式」自动收敛、不给手工开关**。**沿革**：① 最早一条竖条塞 5 个开关，其中 3 个管的面板在最右边（点最左边图标、面板从最右边冒出来，指示器失效——更早「工具箱从左侧 pane 搬到右侧常驻停靠栏」那次留下的；这个错配**编译期全绿、任何单个视图的源码里都看不出来**）→ ② 拆成左右两条 → ③ 左条下线、右条只留「工具箱」→ ④ **现状：整条下线**（用户"左侧的工具栏还需要吗"的直接结果；`Shell.xaml` 主体 Grid 只剩一列 `*`，文件里没有任何 `TogglePanelCommand` 开关）。**没有面板落点的命令不许进活动栏**（全局变量 / 通讯设置已上移到顶部横条，仍是 `SystemCommand` + `SystemAction.GlobalVariables` / `CommSettings`，命令语义一字未改）。**为什么组态三面板不给手工开关**：「属性」/「图层」/「图元工具箱」只在"处于组态界面设计"时该出现，再留手工开关就会与自动收敛打架（手工点开图层 → 与"非组态只显示算子工具箱"直接冲突）。**改 `Shell.xaml` 的活动栏 / 顶栏结构必须同步 `UIThemeSmokeTest\ShellCopy.xaml`**（R44：结构副本手工维护、无断言）。**边界（R43 的例外）**：顶栏与（曾经的）活动栏**不在 `LayoutRoot` 里**——改它们**不改变任何面板的归属或停靠语义**，所以按 R43 的口径**不需要**递增 `CurrentLayoutSchemaVersion`；**R43 的触发条件是"面板归属 / 停靠语义变了"，不是"`Shell.xaml` 变了"**（判据：问"旧 `Layout.xml` 恢复出来会不会把改造抵消掉"）。守门人：`RunShellLayoutChecks()` 的活动栏三条（`Program.cs`）——**"主体 Grid 只有一列" + "文件里没有任何 `Panel_*` 开关" + "两个全局命令在停靠区之前的顶栏"**；"图标与落点同侧"那条判据已随下线一并作废（判据只认 `LocalName == "ToggleButton"` / `"Button"`，所以「视图」菜单里那批同名 `MenuItem` 不会误入）。**⚠ 2026-10-15 晚的教训**：本轮先把活动栏做成"右条只留工具箱"，用户在 23:58 又把整条删了——**改 `Shell.xaml` 结构务必以最新文件为准，动手前先看 mtime**（我据此把 `ShellCopy.xaml`、三条断言、本规则全部重新对齐到"无活动栏"）。细节见 `docs\code-changes\2026-10-15-活动栏拆分（左右各一条+全局命令上顶栏）.md` 与 `docs\code-changes\2026-10-15-组态面板按模式显示（组态三面板与算子工具箱互斥）.md`。 |
| R56 | **「当前是否处于组态界面设计」的判据必须是文档的 `LayoutDocument.IsSelected`，不许用 `IsPanelActive`（`LayoutContent.IsActive`）**。**为什么**：AvalonDock 里 `IsActive` 表示"**整个 DockingManager 的当前活动内容**"——用户点一下右侧的「属性」或「图层」面板，文档的 `IsActive` 就被抢走，判据翻转成"退出组态" → 随即把用户刚点的那个面板隐藏掉，**自毁循环**。`IsSelected` 只随"文档区的选中标签"变化，点停靠面板不动它（判据入口 `LayoutHelper.IsDocumentSelected`，含指定宿主重载供断言用；语义已由 reviewer 反编译 `Dirkster.AvalonDock 4.74.1` 逐路径核实：`IsActive=true` → `IsSelected=true` 是**单向**联动，`IsActive=false` 不清 `IsSelected`；点停靠面板只设该 anchorable 自己，不碰文档窗格的 `SelectedContentIndex`）。**收敛动作统一走 `LayoutHelper.ApplyScadaPanelMode(manager, scadaDesign)`**：组态 → 显示 属性/图层/图元工具箱 + 隐藏 算子工具箱；非组态 → 反之（属性 260 与图层 240 两列随之收成 0px、画布变宽）。**务必"先 Show 目标、再 Hide 对方"**：工具箱与图元工具箱同处一个 pane 做标签页，先 Hide 会造出"该 pane 两个子项都不可见"的瞬态（空 pane 列宽置 0 → 整列闪一下）。**触发点三处，缺一不可**：① 启动 `Shell.xaml.cs:OnContentRendered`（`AttachDockWatchers()` 之后 `ApplyPanelMode()`）；② `LayoutHelper.LayoutLoaded`（**必须走 `OnLayoutLoaded` 而不是直接挂 `AttachDockWatchers`**——方案切换的 `.vms` 内嵌布局、加载布局、恢复默认布局都要重收敛）；③ 中央文档区切标签（`AttachDockWatchers` 的监听器，**只认 `Panel_ScadaEditorView` 的 `IsSelected` 变化，绝不挂在四个面板自身的 `IsVisible` 上**——Hide 动作会再触发一轮、来回抖动）。**磁盘 `Layout.xml` 会把上次退出时的可见性原样恢复，所以每次都必须重收敛**，否则这条改造对老布局等于没发生（这也是本改动**不需要**升 `CurrentLayoutSchemaVersion` 的原因：模式收敛在 `Load()` 之后必然重跑、旧布局抵消不掉它；且旧 `.vms`/旧 `Layout.xml` 都在版本闸那一关就被拒，补 `ContentFactories` 不会新增拒绝）。**已知边界（不是缺陷，别当 bug 回炉）**：「视图」菜单的「图元工具箱/属性/图层」三项仍走 `ShowPanelCommand`（只 Show 不 Hide），是**刻意保留的手工覆盖入口**——非组态模式下点它能临时调出该面板，直到下一次收敛（切标签/加载布局/重启）才归位。**另一处边界**：`IsSelected` 是**窗格局部**语义，把「组态画面」拖成浮动窗或拆到第二个文档窗格后可能与主窗格选中的文档**同时为 true**（判据保持"组态"）。守门人：`UIThemeSmokeTest\Program.cs` 的 `RunPanelModeChecks()`（**5 条**，对着 `ShellCopy.xaml` 实例化出的真实 `DockingManager` 真调 `ApplyScadaPanelMode` 验可见性矩阵 + 幂等 + 判据可用性 + `ContentFactories` 登记）；变异反证已做（**把两组 Show/Hide 对调 → 前 3 条准确变红**——这种"写反了"的错误编译期与静态扫描全绿，只有运行时断言拦得住）。**附带修掉的既有缺陷**：`LayoutHelper.ContentFactories` 原先漏登记 `Panel_ScadaLayerView`，反序列化后 `FillContents` 回填不到 → 每次"从 `Layout.xml` 恢复布局"之后**图层面板变空壳**，而覆盖度校验只遍历该表的 Keys、不会因此拦截（属"静默少一个面板"）；现已补登记并由断言 ⑤ 钉住。 |

---

## 15. 启动自检超时：级别逐项声明 + 判定不许只看 WhenAny 赢家（2026-10-15）

**起因**：真机「**用 VS 启动，第一次会退出、第二次才能成功**」。查 Windows 事件日志**一条 `VisionMaster.exe` 崩溃都没有**（近 7 天 46 条全部来自断言宿主 / 开发工具——`FlowCanvasChecks.exe` 29 条、`ScadaChecks.exe` 2 条、`LogViewProbe.exe` 1 条、`dotnet.exe` 1 条，其余为 WER / Application Error 伴生条目；`VisionMaster.exe` 命中 0 条），转查应用日志才见 `[ERR ] [启动自检] 插件模块扫描 失败：检查超时（30 秒）`——18 个日志文件共 6 次（9-29 / 9-30 / 10-14 各 1 次、10-15 三次，全是既有间歇缺陷，不是某轮改动引入）。根因链：`PluginScanCheck` 冷读 `Modules\`（37 个 DLL / 12.25 MiB，含 6.25 MiB 的 `Microsoft.CodeAnalysis.CSharp.dll`）实测 **1.07 s ~ 28.83 s**（153 个样本，相差 27 倍），30 秒默认超时正好落在波动带中间 → 超时判 `Error` → `App.xaml.cs:122 Environment.Exit(2)` **干净退出**（所以事件日志无记录、`crash_reports\` 无留档，只能查应用日志 `Logs\*.log` 的 `[ERR ] [启动自检]`）→ 第一次启动虽被杀但 DLL 已被 `Assembly.LoadFrom` 读进系统文件缓存 → 第二次扫描只要 1.59~1.68 秒、必成。细节、6 次事件的完整时间表、探针实测与 reviewer 复核见 `docs\code-changes\2026-10-15-启动自检超时误杀（插件扫描冷启动30秒超时导致首次启动退出）.md`。

| # | 规则 |
| --- | --- |
| R48 | **启动自检的兜底超时必须走 `IStartupCheck.TimeoutMs`（逐项声明，默认 `DefaultTimeoutMs` = 30 秒），不许在 `AppLifetimeService` 里写死**（`Core\Lifetime\IStartupCheck.cs:40/:52`；消费点 `Core\Lifetime\AppLifetimeService.cs:107`）。**超时的处置级别同样逐项声明**（`IStartupCheck.TimeoutLevel`，`:49`，默认 `CheckLevel.Error`）——"该项缺失也不影响主界面可用"的自检必须同时声明 `TimeoutLevel => CheckLevel.Warning`，典型就是插件扫描（`VisionMaster\Lifetime\Checks\PluginScanCheck.cs:24/:38`：`TimeoutMs => 120_000` + `Warning`）。**为什么必须逐项、不许全局放宽**：自检链里 `SingleInstanceCheck`（双开）/ `ConfigCheck`（`AppConfig.json` 读不出 = 工作目录坏了）必须保持 Error——"任何自检超时都放行"的后果比误杀更严重；默认口径**仍是 Error**，且有一条反向断言钉死（R50）。**为什么"磁盘慢"不能判 Error**：它会被 `App.xaml.cs:122 Environment.Exit(2)` 干净退出，用户看到的是"第一次启动自己退了、第二次才成功"这种玄学现象，**事件日志里查不到**（不是崩溃），只能查应用日志的 `[ERR ] [启动自检]` 行。`TimeoutMs` 取值**必须为正**（0 = `CancelAfter` 立即取消、每轮都报超时；负数 = 永不取消、兜底失效）。超时文案走 `AppLifetimeService.FormatTimeout`（`:158-159`，不足 1 秒显示毫秒——旧写法会把断言夹具的 200ms 显示成"0 秒"，看不出是哪里配的）。**残留风险（120 秒仍未跑完时才会发生）**：`PluginScanCheck` 的 `Task.Run(..., ct)` 不响应取消，后台扫描继续，但 `PluginProvider` 四个插件字典返回的是防御性副本（`Engine\PluginProvider.cs:35/43/51/59` 的 `new Dictionary<>(...)`）、迟到注册不通知已绑定界面，且 `HelpCatalogCheck` 已把帮助目录一次性建成（`HelpCatalogService.Build()` 幂等、不会自动重跑）→ 表现为"工具箱为空 + 帮助缺插件页"，**重启一次即可恢复**，远好于进程直接退出。 |
| R49 | **"任务超时"的判定不许只看 `Task.WhenAny` 的赢家**——尊重 `CancellationToken` 的实现（典型 `await Task.Delay(..., ct)`）在超时取消后会**以"已取消"状态收尾**，此时赢家**仍是 `execTask`** 但它并没有跑完；照直取 `.Result` 抛的是 **`AggregateException`**（**不是** `OperationCanceledException`，故 `catch (OperationCanceledException) when (ct.IsCancellationRequested)` 过滤器**不匹配**），落进 `catch (Exception)` 被记成 **`Error「检查异常」`**——把 `TimeoutLevel` 声明的级别**整个架空**。必须同时看"已取消且没跑完"（判据写法见 `Core\Lifetime\AppLifetimeService.cs:115-117`：`winner != execTask \|\| (linked.IsCancellationRequested && !ct.IsCancellationRequested && execTask.Status != TaskStatus.RanToCompletion)`；第三个子条件是"赢家是 execTask 但没跑完"，第二个子条件保证不把外部取消误判成超时）。**这不是理论边角**：独立 .NET 9 探针 300 轮里 98~138 轮命中（33%~46%，三次重复；评审另测 26/40），而且是**实现方自己写的断言在自测时抓到的**。**边界**：退出链的 `ExecuteExitChainAsync` 虽然也是"`WhenAny` + 只看赢家"，但它**不取 `.Result`**（只写日志），所以不受本坑的异常路径影响——本坑的触发条件是"看赢家之后还要取结果"。 |
| R50 | **守门人**：`FlowCanvasChecks\FlowEngineFixChecks.cs` 的 `StartupChainTimeoutIsNotFatal()`（`:154-193`，5 条，注册在 `ExitChainCanTimeOut` 之后，进常规 gate 基线）——① 慢项声明 Warning → 超时后 `result.Success == true`（不阻断）；② 超时进 `result.Warnings`（不是静默吞掉）；③ 超时项之后的检查照常执行；④ 慢项按自己的 200ms 处置、链耗时 < 5 s；⑤ **反向**：未声明 `TimeoutLevel` 的项超时仍按 Error 阻断（**默认口径没被改掉**）。变异反证已做（删 `SlowWarningCheck.TimeoutLevel` → 前 3 条准确变红）。**新增自检项时的口径**：要放宽超时/级别就在项内声明这两个属性（接口默认实现，不动任何既有实现者），**别去改 `AppLifetimeService` 的默认值**。 |

---

## 16. HALCON 画布交互：手势链路顺序与命中容差（2026-10-15）

**起因**：真机反馈"使用 Core.Halcon 创建 ROI 后，拖拽改变已创建 ROI 大小的时候很不方便、改不了大小"。根因是 `HSmartWindowControlWPF` 的鼠标链路「控件内部**先** `SendMouseDownEvent`（交给 HALCON 做 drawing object 句柄交互）→ **之后**才触发 `HMouseDown` 事件」，而 `HalconBase` 只给 `ActiveRoi` 挂句柄——在**未选中**的 ROI 上起手时，HALCON 收到 down 的那一刻还没有任何句柄，整段手势作废（用户必须"点一次再拖一次"）；叠加摘除容差 8px 与 HALCON 手柄（约 8px 见方）同宽，瞄偏一点就丢选中。影响面：`ImageEdit` 是共享画布，8 个插件（CreateRoi / CaliperMeasure / ColorCheck / ColorRegion / Ocr / Matching / PreProcessing / Calibration）继承同一行为。细节、探针实测与 reviewer 复核见 `docs\code-changes\2026-10-15-ROI画布拖拽改大小（冷启动手势与命中容差）.md`。

| # | 规则 |
| --- | --- |
| R51 | **"未选中对象上一次手势就拖得动"必须靠换绑后补发 down**：`HalconBase.HSmart_MouseDownForRoi` 在换绑 `ActiveRoi` 之后调 `ReplayMouseDownForFreshHandle`（`hWindow.SendMouseDownEvent(new HTuple(e.Row), new HTuple(e.Column), 1)`，button 编码与控件内部 `MouseButtonEventToInt` 的 `Left→1` 一致）。**只在换绑分支调用**——"命中已选中对象"已在更前面 `return`（`x != ActiveRoi` 的挑选也已排除同引用），重复补发会打断正常拖拽。补发失败要**静默退化**为旧行为（先选中、再拖一次），不许让"选中"这件事失败。**必然的行为变更**：未选中对象"内部按下并拖动"从"只选中"变成"**移动**"，必须用断言钉住，免得日后被当成新缺陷。 |
| R52 | **摘除容差必须盖住 HALCON 手柄尺寸**：手柄约 8px 见方，容差取同宽（旧值 `8.0`）= 瞄偏一点就判"点空白"、当场摘除选中、要重新点选才能再拖。`RoiHitToleranceScreenPx = 14.0` **只作用于"是否摘除已选中对象"这一处**；**挑选其它对象的那段命中测试保持无容差**（否则相邻对象互相误选）。代价是活动对象周围形成"遮蔽带"（贴得比容差还近的两个对象，切换前要先在空白处点一下结束编辑）——这是有意的取舍，要写进注释。 |
| R53 | **画布交互断言只能用「可见窗口」**：`open_window(..., "buffer", ...)` 的 buffer 窗口上**只有"点中对象内部"能命中，角点手柄与拖拽全失效**，"补发 down"在它上面也无效（实测）。要验手柄交互必须开真窗口——**屏幕外**的 WPF 窗口（`Left=-4000`）即可，HwndHost 仍创建真窗口、手柄照常响应。夹具自带 STA 线程 + 手动 `Dispatcher.PushFrame` 泵 + **窗口级**主题字典（`/Core.Halcon;component/Generic.xaml`——断言宿主不是 WPF `Application`，没有应用级资源，不合并则控件没模板、鼠标处理根本不注册）。⚠ 与 R37-①"烘焙件的窗口 mode 只能 `buffer`"**方向相反**：烘焙=离屏 `buffer`，交互=可见窗口，别记混。 |
| R54 | **`rectangle2` 的 length1 沿 column 轴**（phi=0 时），不是 row——算角点/边中点坐标时极易记反，会得出"手柄命中失败"的**假结论**（本轮第一遍探针就是这么被骗的）。同理 `ellipse` 是 radius1 沿 column。 |
| R55 | **改了 `Services\Core.Halcon` 必须 Debug 与 Release 都重建**：`pack.ps1` 打的是 **Release** 产物，只重建 Debug 则真机拿到的仍是旧行为（与红线③"产物陈旧"、R42"增量构建静默漏打包"同族）。判据不能只看构建退出码——核 DLL 里有没有新符号（如 `ReplayMouseDownForFreshHandle`）。 |

**守门人**：`FlowCanvasChecks\RoiInteractionChecks.cs`（8 条，进常规 gate 基线，注册在 `HalconControlChecks.Run();` 之后）——①已选中拖角点（基线兼环境反证）②**冷启动一次手势拖角点即可改大小**③冷启动只点不拖→只选中且尺寸不变（防假通过）④点空白结束编辑（回归）⑤已选中内部拖=移动（回归）⑥多 ROI 冷启动拖第二个角点（切换+改大小）⑦起手偏出 ~11px 不摘除选中 ⑧冷启动内部拖=移动（R51 的行为变更钉住）。**改前 ①③④⑤ 绿 / ②⑥⑦ 红 → 改后 8 条全绿**，构成断言有效性的反证。

---

## 17. 启动关键路径只留"拦得住启动"的自检项（2026-10-16）

**起因**：用户看着 `VisionMaster\App.xaml.cs` 那段按序注册六个自检项的代码问"这个可以并行加载吗 串行太慢了"。逐项查下来：链是纯串行 `foreach`（`Core\Lifetime\AppLifetimeService.cs:97-150`），耗时 `sum` 全在**插件扫描**（R48 实测 1.07~28.83 s）和**通讯连通性**（3 秒追赶窗口 + 每条最多 3 秒，总预算 12 秒；`EnableCommunicationStartupCheck` 默认 true 所以它默认真跑）两处；而后者**全部失败出口都是 Warning**（`CommunicationCheck.cs:105/:112`），从来拦不住启动，只是在 Splash 警告列表多一行（那一行本来就转瞬即逝）。并行的收益上限是 `max` 不是 `sum`，还要连带改 Splash 的 `done/total` 进度口径（`SplashScreen.xaml.cs:71-81` 直接拿它算 `Progress.Value`，并发完成顺序不定就会来回跳），所以第一刀砍"只报不拦的不该在关键路径上"。细节、逐项耗时表与静态复核台账见 `docs\code-changes\2026-10-16-通讯连通性自检移出启动链（并行不如挪走）.md`。

| # | 规则 |
| --- | --- |
| R57 | **"全部出口都是 Warning"的自检项不许注册进启动自检链**——改由 `App.OnInitialized`（主窗口已显示、Splash 已关）后台跑一次，结果按级别写日志（`App.RunCommunicationCheckInBackground`）。**理由**：链上每一项的耗时都直接加在"用户看见主界面"之前，而 Warning 项决定不了任何事；把它留在链上等于让一个只报不拦的诊断项守启动门。**三条不许变形**：① **口径不变**——日志文案仍走 `[启动自检] {名称}: …` / `… 警告：…`，现场检索词是同一个；② **开关不变**——配置开关（如 `EnableCommunicationStartupCheck`）留在检测类自己的首行，别挪到调用点；③ **时序前提不变**——`AppLifetimeService` 的 `Error` 短路语义是"跑到那条才停"，挪出去的项自然享受不到，所以只能挪**全 Warning** 的项（这也是为什么 `CommunicationCheck` 仍实现 `IStartupCheck`：留一条"以后要入门禁"的退路，不是当前调用路径）。**挪动的四件事必须同时到位，少一件都是静默故障**：链里删注册、`OnInitialized` 补后台调用、`OnExit` 里 `_postStartupCts.Cancel()`（**先叫停再跑退出链**，否则关闭过程中后台检测还在碰连接管理器）、其它异常只落一行 Warn（后台诊断绝不能把应用带下去）。守门人：`FlowCanvasChecks\FlowEngineFixChecks.cs` 的 **[E18] 五条**——①链里不许再出现 `RegisterCheck(new CommunicationCheck`、②`OnInitialized()` 体内必须有后台调用（只删注册不补 = 通讯从此不被检测）、③`OnExit()` 体内必须有 `Cancel()`、④**反向钉住前提**：`CommunicationCheck.cs` 里 `CheckLevel.Error` 必须 0 次（哪天它开始拦启动就该回链上，这条红着提醒）、⑤链上 `RegisterCheck(` 恰好 5 次（防"为了过①多删几行"）。判据用"签名 + 花括号配对取方法体"（`MethodBody()`），**不写行号与空白**，改缩进加注释不误伤（与 R46 同思路）。**同类待办**（本轮没做，别当已完成）：`HalconCheck` ∥ `PluginScanCheck` 是唯一可并发的一对（互不依赖、各自 `Task.Run`、日志走 `BlockingQueue` 所以并发写安全，`Core\LogService.cs:28,40`），但要先解决 Splash 进度口径，且**动手前应先量一版逐项实际耗时**——若插件扫描占大头，并行只省 HALCON 那几秒。 |

---

## 18. AvalonDock 停靠面板恢复可见必须走 `LayoutHelper.ShowAnchorable`（2026-10-16）

**起因**：真机调试器直接断在 `System.ArgumentOutOfRangeException`，栈是
`LayoutContent.set_IsSelected` → `ShellViewModel.AttachDockWatchers` 的监听器 → `ApplyPanelMode` →
`LayoutHelper.ApplyScadaPanelMode` → `ShowPanel` → **`AvalonDock.Layout.LayoutAnchorable.Show()` → `LayoutGroup<T>.InsertChildAt` → `List<T>.Insert`**。
异常文案是 **"Index was out of range. Must be non-negative…"**（负数那一支，不是"超过数量"）。
根因在库：**4.74.1 的 `Show()` 消费 Hide 时记下的"回哪一列、第几个位置"时只判上界不判下界**——
记忆里那一列已不含本面板时 `IndexOfChild` 返回 `-1`，`-1` 被原样塞进 `Collection<T>.Insert`；
而"清空记忆"两行排在插入**之后**，异常一抛**坏记忆永久留着**，本面板此后每 Show 一次炸一次。
叠加两个本仓事实把它从"一次性故障"放大成"天天闪退"：① R56 让"切中央文档区标签"就重收敛一次；
② `App` **没有** `DispatcherUnhandledException` 兜底（grep 复核 0 命中），UI 线程未捕获异常必退进程。
另一头更阴：`PreviousContainer` 为 null 时库**不抛、也不显示**（静默故障）——所以防护不许只 `catch`。
细节、异常原文逐帧解读、取证输出与复算步骤见 `docs\code-changes\2026-10-16-停靠面板恢复可见崩溃防护（AvalonDock记忆索引缺下界）.md`。

| # | 规则 |
| --- | --- |
| R58 | **恢复停靠面板可见一律走 `LayoutHelper.ShowPanel` / `ShowAnchorable`，别处不许直接写 `anchorable.IsVisible = true`**（`LayoutHelper.cs:485`）。**四条判据少一条都对应一个真实故障形状**：① `!IsHidden` 先 `return`（`Parent==null` 那一态库的 `Show()` 无条件抛 `InvalidOperationException`）；② `PreviousContainerIndex < 0` 判废（就是本次崩溃那一支）；③ 记忆里那一列还挂在当前 `LayoutRoot` 上（反序列化换树时索引合法但插进死树 = 面板照样显示不出来，静默）；④ **Show 之后再看一次 `IsVisible`**——库在 `PreviousContainer==null` 是"不抛也不显示"，只判"没抛"会放过静默故障。判废或原路失败就走 `AnchorToHomePane` 自己挂回，落点四档**顺序不许改**：空的记忆列 → 树上第一个空 `LayoutAnchorablePane` → 非空的记忆列 → 根面板末尾新建列（`DockWidth=260`）；宁可位置偏、宽度不对，也不能让面板显示不出来。**必须走 `InsertChildAt(ChildrenCount, …)`**（与库成功路径同一个动作，`Parent` 由集合变更回调赋值），不许自己 `Children.Add`（绕过回调）。**挂回进门先判 `IsVisible`**——库可能已在插入**之后**的语句才抛，此时它已经进去了，再插就是同一 pane 两个副本。修复后本仓不留任何状态：再 Hide 一次库会把有效索引重新记回去（自愈）。**反射只读、绝不写库的私有状态**。守门人两层：运行时 = `UIThemeSmokeTest\Program.cs` 的 `RunPanelModeChecks()` 后 5 条（**⑥a/⑦a 是库行为反证**：⑥a 绕过防护直接写 `IsVisible=true` 必须当场抓到 `ArgumentOutOfRangeException`，⑦a 必须抓到"静默不显示"——这两条红了就说明前提没了、防护该重审，而不是继续挂绿灯）；静态 = `FlowCanvasChecks\LayoutVisibilityCheck.cs` 的 LV0~LV7（**10 条，进常规 gate 基线**：全仓引用 AvalonDock 的文件里除 `LayoutHelper.cs` 不许有 `.IsVisible = true` / 那一处必须仍在 `ShowAnchorable` 体内 / 防护三要素齐备 / 不许 `GetField` 读记忆 / `ApplyScadaPanelMode` 只做 Show-Hide 编排）。变异反证两批都做过的：注入 `ShellViewModel.cs` 一条绕过 → **LV3 准确变红并给出文件:行号**（还原后 2455/0）；断言第一版写 `GetField(...)` → 假红。**不改包版本、不改面板归属，所以不需要递增 `CurrentLayoutSchemaVersion`**（R43 的触发条件是"面板归属/停靠语义变了"，不是"`LayoutHelper` 变了"）；升级到已修版本后 ⑥a/⑦a 与 LV3b 会主动红，那是"摘防护"的提醒。 |
| R59 | **读 AvalonDock 的隐藏记忆只有一条合法口径：`LayoutContent` 上的同名**属性**，走 `GetProperty`**（`LayoutHelper.cs:522/:539`）。2026-10-16 用 `tools\_dump_prev_mem.ps1` 对出货二进制（`AvalonDock, Version=4.74.1.0`）逐成员取证，结论逐字：`ILayoutPreviousContainer` 是 **internal 接口**（`IsPublic=False`）→ 外部程序集强转过去是 `error CS0122`；`PreviousContainer` / `PreviousContainerId` 是 **protected 属性**（get/set 都是 `Family`）→ 必须 `GetProperty(..., NonPublic \| Public \| Instance)`；`PreviousContainerIndex` 是 **public 属性**；底层字段叫 `_previousContainer` / `_previousContainerIndex`，**与属性不同名** → `GetField("PreviousContainer")` 取到 `null`。两个坑的后果不对称：断言里写错 = **假红**（看得见），产线代码里写错 = **判废分支静默失效**（永远不红，坏记忆照样放行，就是 R58 那条崩溃回来）。所以取不到属性时 `ReadPreviousContainerIndex` 返回 `int?`，**null 算"不知道"而不是 0**（退回"让库自己试、抛了照样挂回"）；断言侧口径必须与产线逐字一致、取不到就**故意判红不判跳过**（库换版本正是防护该重审的时刻，R38）。同族教训：`VM3.0-main`、`DLL\HslCommunication` 是随仓第三方源码，全仓静态扫描一律只扫第一方目录并排除 `bin\obj`（沿用 `ResultDisplayCheck` 扫描④ 的口径）；`IsVisible` 这个名字好几套控件都有（Nodify `PendingConnection`、图表轴、组态图层），一刀切会误伤，所以判据收在"去注释后代码里仍含 `AvalonDock` 的文件"，**不开豁免清单**（R36 的教训：能不收就不收）。 |

---

## 19. 插件自带手册的分层、厚度与渲染前提（2026-10-16）

**起因**：把 42 个工具箱条目全部铺成"用户页 + 开发页"两层自带手册（31 个程序集 / 251 页 / 29,071 行 / 86 张开发页），并把"骨架完整度"从台账翻成红线（HM17）。过程中抓到**两个编译期全绿、只有运行期看得见**的静默缺陷：① 多插件程序集的开发页 `dev-` 标记被宿主的取名方式吃掉——目录树收到 72 页、磁盘上其实有 86 页，那 14 页在"显示开发章节"关着的时候**照样出现在用户目录里**（根因：MSBuild 把内嵌资源的目录分隔符换成**点**，`Path.GetFileNameWithoutExtension` 剥不掉粘在页名前面的分册目录名 `Plugin.Utility.Help.DelayPlugin.10-dev-architecture.md`）；② 手册正文里未闭合的 `**` 直接上屏成字面星号，其中一处形状阴险——`dev_display`、`dev_set_window` 这类算子名**裸写在反引号外面**，渲染器的行内扫描把 `_` 当强调标记，起了一个**幻影斜体**、把后面那对真 `**` 吞进斜体内部（第一遍只把 `dev_*` 包进反引号，泄漏照旧）。细节、可复算台账与三处变异反证见 `docs\code-changes\2026-10-16-插件帮助手册分层铺开（42条目档位闸门+两处静默缺陷）.md`，骨架规范见 `docs\方案设计\插件帮助手册分层铺开方案说明书.md`。

| # | 规则 |
| --- | --- |
| R60 | **一页手册的"身份"只有「最后一个点之后」那一段文件名**（`HelpCatalogService.GetPageFileName`，`HelpCatalogService.cs:408`；消费点 `:366`）。宿主从资源名取页名时必须先剥分册目录段：MSBuild 用**点**表达目录，多插件程序集的分册目录名（逐字等于插件类名，R41②）会粘在页名前面，不剥掉 `IsDeveloperPage`（`:419`，要求"去掉 `NN-` 后以 `dev-` 起头"）就认不出标记——后果是**开发页混进用户目录**（不报错、不缺页，只是读者分层整条失效）。**这条修法的隐含前提由 HM6 钉住：手册文件名里除扩展名那一点外不许再出现点**（`01-overview.v2.md` 这种名字会静默错位）。反向要求：新增分册目录名必须逐字等于 C# 类名，且**不许含点**。 |
| R61 | **厚度按档位下限判，判据是红线不是台账**（`HelpManualCheck.cs:56-75` + `:410` 的 HM17）：L 档 用户页≥5 / 开发页≥3 / 开发页行≥300，M 档 4/2/150，S 档 3/1/100；**未登记条目按 M 档要求**（`DefaultTier = ManualTier.M`，`:54`——漏登记只会更严，不会变成零要求）。**派工单必须把三个数字抄进去**，只写"按 L 档"交回来的册子会"页数够但开发页只有 1 张"（本轮返工 5 个条目）。档位判据本身：源码体量 + 是否自带画布/视图 + 是否设备驱动或脚本引擎 + 断言密度。变异反证已做（L 档用户页 5→6 → HM17 准确红并逐条列出 12 个 L 档）。 |
| R62 | **改过任何一页 `.md` 的"内容"，产物就算脏**（把 R42 从"新增手册"扩到"改文案"）：增量 `dotnet build VisionMaster.sln` 会 0 错误、csproj 里 `EmbeddedResource` 也在，但 DLL 里仍是旧正文——本轮实测改一个字符后探针**照旧报同一处泄漏**，对该工程 `--no-incremental` 才生效。判据不看退出码，看 `tr '\0' '\n' < Modules\Plugin.X.dll \| grep -a "\.Help\."` 数出的资源名**等于该工程 `Help\` 下的 .md 数**。**另有一个同族坑**：`Plugin.Camera.Network` 与 `Plugin.ImageAssign` **不在 `VisionMaster.sln` 里**，所以"全量重建 sln"永远不会碰它们（本轮对账 21 项不符 → 2 项 → 0 项，最后那 2 项就是这么来的）；在把它们补进 sln 之前，收尾必须对这两个工程单独 `--no-incremental`。 |
| R63 | **手册正文的排版前提只有渲染级对照守得住**，静态数星号奇偶会误报（`Help\**\*` 这类通配本来就该字面显示）。两道闸都在 `HelpProbe` 里：① 渲染后正文残留字面 `**` 即红，额度只从"代码上下文"（fence + 行内反引号）扣；② **代码块字体族必须显示得了代码上下文里的每个字符**——字体族从渲染结果里认（`FindCodeFont`：正文族挂在 `FlowDocument` 上，代码块段落是唯一改 `FontFamily` 的块），判据是"与一定没有字形的 U+E000 位图逐像素相同"。**两条口径纪律**：跨行 `**…**` 是**支持的**（`BuildParagraph` 先 `SmartJoin` 拼行再解析，`MarkdownToFlowDocument.cs:246/:283-299`），别去"修"它；测字形**必须按 `Rune` 不许按 `char`**（按码元会把 🔗 的半个代理对报成豆腐块——假阳性的坏处是让人养成"这条闸常红"）。诊断消息要带 `U+XXXX`：缺字形字符本身常常不可见。变异反证：注入 U+F000 → 准确红。 |

---

## 20. 图标资产只有一个来源；验证工具也会陈旧（2026-10-16）

**起因**：真机 Alt-Tab 上帮助弹窗与主窗口都是系统默认图标。盘点下来**改动前仓内零图标资产**（`.ico` 0 个 / `ApplicationIcon` 0 处 / 第一方 `.xaml` 里 `Property="Icon"` 0 处——只有 vendored `VM3.0-main` 的 `{h:Logo}`，不算本仓事实），所以这不是"换一张图"而是"从没交过图标"。补两枚（主程序 + 只给帮助弹窗），并给 `Window.Icon` 那条 Setter 配运行期闸（Setter 的字符串转换只在样式应用到窗口时发生，URI 写错 = **编译期全绿、帮助弹窗打不开**，与 R30 那族同形）。细节、逐档字节台账与变异反证见 `docs\code-changes\2026-10-16-窗口图标烘焙（帮助弹窗Alt-Tab图标+主程序exe图标）.md`。

| # | 规则 |
| --- | --- |
| R64 | **`.ico` 只许由 `tools\bake_icons.ps1` 烘焙（STA 运行），禁止手塞单尺寸 ico 或"一张 256 缩到底"**。ICO 是**多档尺寸表**，shell 按场景挑档（Alt-Tab / 任务栏用 16~24 那几档），所以正解是**矢量源写一遍、对每档尺寸各渲染一次**（16px 那档是画出来的，缩出来的取景框描边在 16px 直接散成灰块）。两条容器口径：条目**全部用 PNG 压缩**（Vista+ shell 认）、**256 在宽高字节里写 0**。两条 WPF 侧实测：`.otf` 的绝对 URI **不是** `FontFamily` 键，要拼 `"<文件 uri>#<家族名>"`（本仓根含 `#`，`Uri.AbsoluteUri` 已把 `%23` 编码好，**别改成手拼**）；字形取几何用 `FormattedText.BuildGeometry`（`GlyphTypeface.CharacterGlyphIndices` 在 PowerShell 里取到 null），且要**居中墨迹盒不是排版盒**（先探 `Bounds` 再按偏移重画）。接线只有两处、各管各的范围：`ApplicationIcon`（`VisionMaster.csproj:15`，喂 exe + **没显式设 `Icon` 的全部窗口**）与帮助弹窗自己的 `Icon` Setter（`HelpView.xaml:34`，走 `<Resource>` 才解得开 pack URI）。**改过的验证口径**：exe 里逐条比对 ico 的 9 个 PNG 条目字节（`ApplicationIcon` 进 PE 图标目录）、`help.ico` 进 `VisionMaster.dll` 的资源流（**exe 里 0/9 是对的**，它不是 `ApplicationIcon`）。守门人 = `HelpProbe` §3c：用与 Setter **逐字相同**的 pack URI 走 `BitmapDecoder` 解一遍并断言 `16x16` 那一档在（变异反证：URI 改 `helpX.ico` → 准确红）。**顺带实测纠正一条想当然**：WPF 资源名查找**不区分大小写**（报错文案里已是全小写），所以这条闸拦"没打进包 / 程序集名错 / 文件名拼错 / 少档"，**拦不住大小写**。**现场判读**：同路径 exe 换图标后资源管理器/任务栏可能仍显示旧图（按路径缓存），先怀疑缓存再改代码。 |
| R65 | **改过断言宿主（`HelpProbe` / `FlowCanvasChecks` / `ScadaChecks` / `UIThemeSmokeTest`）的源码，验证命令里不许用 `--no-build`；"全部通过"不等于覆盖**。2026-10-16 实测：给 `HelpProbe` 加了一条图标断言后跑 `dotnet run --project HelpProbe --no-build`，输出仍是旧的 `69/0` 且**照样打"全部通过"**——新断言**根本没执行**（跑的是加断言之前的陈旧二进制），把 70 行 `[PASS]` 逐行拉出来才找不到的那条。补 `dotnet build` 后才出现第 70 条并打出真实明细（`9 档：16x16 … 256x256`）。**这一族与红线③ / R42 / R16 / R28 同源，但对象是验证工具本身，所以更隐蔽：绿灯是真的，只是少了一条。** 三条口径：① 加/改 `Check()` 后必须**先 build 再 run**；② 判据落在**条数 + 明细行文本被原文打出来**上，不看"全部通过"那一句；③ 新闸必须做变异反证（改坏判据 → 准确变红 → 还原变绿），否则"这条闸今天跑到了吗"无人能答。 |

---

## 21. 流程栏"按当前选中项执行"的命令：删完别无条件清靶（2026-10-16）

**起因**：真机反馈「流程栏选中算子右键删除没反应，**尤其只剩下最后一个算子的时候**」——截图里选中竖条亮着、菜单项也亮着，点下去毫无反应。根因是 `ProcessViewModel.ModuleActionAsync` 的 `Delete` 分支**先删、后无条件 `CurrentSelectedStepModel = null`**：删除引起的集合变更会让流程栏 `TreeView` **自动改选邻近行**（WPF `Selector` 保选），那次改选经双向绑定把命令靶换成了幸存的那一步，紧接着的 `= null` 又把靶抹掉——界面高亮着、公开属性（`SelectStep` / `Workspace.CurrentStep`）也指着它，**命令链取参的私有字段却是 null**，于是删除/改名/禁用全部静默空跑；最阴的是"再点同一行"救不回来（它本来就选着，不再触发 `SelectedItemChanged`）。只剩最后一步时必然踩中。细节与实测见 `docs\code-changes\2026-10-16-流程栏删除没反应（删后命令靶被清空抹掉）.md`。

| # | 规则 |
| --- | --- |
| R66 | **结构变更（删除/移动）之后不许无条件清空"命令靶"，判据要问"这次变更有没有把选中交给别人"**。范式（`ProcessViewModel.cs` 的 `Delete` 分支）：**先把靶存进局部变量**（既当删除入参、又当"有没有换人"的比对面），删完只在 `ReferenceEquals(CurrentSelectedStepModel, target)` 时清空——靶换成幸存者 = 用户看得见的高亮已交接，**必须保留**；靶没动 = 它还停在刚被移出图纸的对象上，**必须清**（否则后续改名/禁用打在孤儿对象上，是另一类错靶）。**清空一律走公开属性（`SelectStep = null`）而不是只写私有字段**：只写字段会让公开属性（以及它带动的 `Workspace.SwitchStep` 与 `IsXxxNodeSelected` 那批菜单可见性通知）继续指着已删除对象，命令靶与界面各说各话。**这一族只在"命令靶与界面选中态是两份状态"的地方存在**——画布侧（选中态就是节点 VM 自己的 `IsSelected`）与程序栏流程列表（删完不清选中）都没有这个坑。 |
| R67 | **"树的自动改选会回写 VM"这类症状，断言必须在真 `TreeView` 上跑**。纯 VM 直调（`vm.SelectStep = step`）产生不出那次回写——缺了树这一环就是**假绿**，这正是本坑藏了这么久的原因。做法（`FlowCanvasChecks\ProcessTreeInteractionChecks.cs` 的 V10 五条，进常规 gate 基线）：建真 `TreeView` 并用 `BindingOperations.SetBinding` 复刻 `ProcessView.xaml` 那两条绑定（`BindableSelectedItem` 双向），**`Measure`/`Arrange`/`UpdateLayout` 之后容器就生成了，不需要窗口、不需要 `Application`、不需要主题资源**（判据全在数据侧：命令靶字段 / 步骤集合 / 树选中项）。⚠ **不 `new Application`**（同 `BlobDetectChecks` / `ParallelContainerChecks` 纪律）；STA 线程 + `Dispatcher.PushFrame` 手动泵照 `RoiInteractionChecks`。**五条判据里必须有一条反向**（本例 ④"删掉唯一的一步 → 靶清空，不留孤儿"）——只写"删除后靶要跟着走"的话，"一律不清空"这种过度修复会全绿通过。变异反证已做（退回无条件清空 → ②③④⑤ 准确变红）。 |

---

## 22. 图标字符按钮的紧格子：Fluent 隐式内边距会切掉字形（2026-10-16）

**起因**：真机三连报——① 帮助窗「关闭按钮的 X 显示不全」；② 主窗口标题栏「Shell 上面的关闭也显示不全」（截图里三个按钮是 `— ▢ ＼`）；③「我其它视图的关闭也有这种情况」。**两条完全不同的根因**，别当成一件事治：

- **②是几何串位**：`WindowControlButton` 模板把 `Content` 当 `Path.Data` 用（`UI\Controls\Themes\Controls\Window.xaml:30`），Close 那颗写的是 `Content="M 0 0 L 10 10 M 0 10 L 0 10"`——第二笔**起点=终点**（零长度线段），于是只画出 `\`。样式、模板、资源键、绑定**全对**，错在几何数据里，所以"资源断链/模板同源"那一族闸（R30/R32）一条都不会红，只有把 `Content` 当数据扫才拦得住。
- **①③是格子算术**：`VisionMaster\App.xaml:68` 把 .NET 9 Fluent 主题并进**应用级**资源，它自带一支隐式 `Button` 样式 `Padding="11,5,11,6"`。于是"固定格子 + 不挂样式 + 不写 Padding"的图标按钮，内容区 = 格子 − 22（宽）/ − 11（高）：30×30 只剩 **6×17**，而「✕」在 14 号字下要 **11.4×17.8** → 模板里那圈带 `CornerRadius` 的 Border 把左右削掉；24×24 那颗更狠，内容区宽 **0**，「×」整根不见。离屏对照图上看得最清楚：30×30 裸按钮渲染成一个「>」，24×24 裸按钮是一个空框。
- 命中面（本轮逐个量过）：帮助窗关闭（已随换肤挂 `ToolButtonStyle`）、`MotionBoardView.xaml:149`、`PluginConfigShellView.xaml:220`（两颗 30×30 标题栏）、`ScadaAlarmHistoryView.xaml:327`（24×24 收起）、`MonitorView.xaml:280`（20×20「移除监视」FA 图标——**静态闸自己抓出来的第四处，肉眼盘点时漏了**）。**没毛病的**：24×24 且已写 `Padding="0"` 的插件行内删除钮（CSharpScript/DataRecord/ResultDisplay）、不定宽的（`ResultUpload`、`ScadaPropertyView:747`——自动长开会把内边距吸收掉，所以不切）。

| # | 规则 |
| --- | --- |
| R68 | **写了 `Width`/`Height` 的图标字符按钮（✕ × ＋ ↑ 这类单个非字母字符，含 FA 私有区码点），必须挂 `Style` 或显式写 `Padding`**——两者都不写就是吃 Fluent 隐式样式的 `11,5,11,6`。两条修法都对：`Padding="0"`（本轮标题栏/收起钮用的，格子多大字就占多大）或挂 `DialogIconButton`（内边距 6,4,6,4，`CameraSettingsView` 等五颗标题钮走这一档）。**不定宽的不算违规**（自动长开吸收内边距）。守门人：`FlowCanvasChecks\GlyphButtonClipCheck.cs` 的 **GB0~GB5**（进常规 gate 基线）——GB3 全仓扫违规、GB4 把"写了 Padding 但写大了"也算过账（内容区 ≥ 字形需求，需求按实测比例 宽≈0.85Fs、高≈1.30Fs）、GB5 反向钉住五处事故点仍带防护、**GB2 反向钉住解析器本身**（命中数与固定格子子集必须非空，否则"0 违规"可能只是"0 命中"的假绿）。算术前提由 `HelpProbe.RunGlyphClipChecks` 四档运行时对照钉住（裸 30×30 **必切** / `Padding=0` **必完整** / 裸 24×24 内容区**归零** / `DialogIconButton` **必完整**）——Fluent 换版本或主题不再合并时这几条会红，那是"判据该重审"的提醒而不是继续挂绿灯（R38）。变异反证已做：摘掉 `MotionBoardView` 的 `Padding="0"` → **GB3 + GB5 准确变红**、其余绿灯。 |
| R69 | **量"文字被切"不许看内层 `TextBlock` 的 `ActualWidth`**——它被父级挤了也**不会改自己的实得宽**（本轮第一版量具因此把全部形状判成"完整"，白跑一轮）。正解两条：① 需求宽由一个**不受约束的参照 `TextBlock`**（`Measure(infinite)`）量出来，`DesiredSize` 记录的是"上一次受限 Measure 下要了多少"，不能当参照；② 判据用**算式**"内容区 = 按钮实得尺寸 − 生效 `Padding` − `BorderThickness`"，与 `ActualWidth` 无关。另：`Application.Current.Resources[typeof(Button)]` 取到的那支隐式样式**读不到 Padding Setter**（值在合并链更深处），所以判据必须从**按钮自己**的生效值（`button.Padding`）取，别从样式 Setter 取。 |
| R70 | **标题栏按钮的 `Path` 几何不许有零长度线段**（`M x y L x y` 且起=终 = 那一笔根本不画）。改 `Shell.xaml` 的 `WindowControlButton` 按钮 `Content` 必须同时改 `UIThemeSmokeTest\ShellCopy.xaml`（R44：结构副本手工维护）。守门人：`UIThemeSmokeTest\Program.cs` 的 `RunShellLayoutChecks()` 末尾两条——同一道 `CaptionGeometryOffenders()` 判据跑两遍（原件 + 副本），命中"零长度线段"即红。**这条闸是数据级扫描**，与 R30/R32 那族资源级闸正交：资源键全对、模板同源也能切/缺笔画，两族都要过。 |
