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
