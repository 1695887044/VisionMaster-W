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

---

## 23. SCADA 图元属性源生成器与容器图元（2026-10-17）

**起因**：用户裁定「源生成器 B 也做」「容器图元可以做，一个界面可以有多个底板」「曲线可以参考 `_ChartProbe5`」。三条一起落地：`Shard\Core.ScadaGenerators`（`[ScadaProperty]` → DP + CLR 包装 + 描述符条目）、`Hmi.Panel` 底板容器、`Hmi.ChartTrend` 趋势曲线（`Charts\VM.Charts` 从探针工程提升为正式库）。细节见 `docs\code-changes\2026-10-17-SCADA容器图元与曲线图元与属性源生成器.md`。

| # | 规则 |
| --- | --- |
| R71 | **改 `Shard\Core.ScadaGenerators` 后必须重建全部引用它的图元工程**（`VM.Scada.Controls`、`VM.Scada.Controls.Charts`）——生成物（依赖属性字段 / CLR 包装 / 描述符条目）在 DLL 里编译期展开，不重建则运行的是旧 DLL。与 R16（插件生成器）同族，也与红线③「产物陈旧」同源。判据不能只看构建退出码。 |
| R72 | **图元私有属性走 `[ScadaProperty]`；基类已有的外观属性（Fill / Stroke / FontSize / Text / …）走 `AppearanceProperties` / `TextProperties` 打包助手，绝不许用 `[ScadaProperty]` 再声明一遍**。生成器会报 **SPG0006** 拦住后者——遮蔽基类依赖属性的后果是 `{TemplateBinding Fill}` 绑到子类新属性上、基类那份永远不动（表现是"属性面板改了没反应"），编译期全绿、运行期不报错。诊断 SPG0001~SPG0006 一律 Error、不静默跳过。**同一基类属性在不同图元里叫不同名字**（泵「泵壳色」/ 电机「机身色」/ 管道「管身色」）用 `AppearanceProperties.Rename(Collect(...), ("Key", "标签", "说明"))` 换标签，**只换 DisplayName/Description，不许另写一份描述符**——Key / 默认值 / `TargetProperty` 必须仍只有基类一处事实来源（返回新数组，不就地改：`Collect` 的结果是按类型缓存的实例）。守门人：`ScadaChecks` 的 `[G]` 段（生成器契约，含"故意漏 partial 必报 SPG0001"的变异反证）+ `[R]` 段的 **18 条描述符快照**（全部内置图元的「Key 顺序 + DisplayName + DefaultValue」字面快照；行序或默认值漂移必红，改它必须是有意改快照）。 |
| R73 | **新增图元控件类必须写静态构造覆盖 `DefaultStyleKeyProperty`**（`DefaultStyleKeyProperty.OverrideMetadata(typeof(X), new FrameworkPropertyMetadata(typeof(X)))`）。基类 `ScadaElementBase` 在 Generic.xaml 里有一条隐式样式，派生类的 `DefaultStyleKey` 默认**继承**基类那份——不覆盖就会套上基类的 `ContentPresenter` 模板，于是 `GetTemplateChild("你的部件")` 永远返回 null。**症状极静默**：控件照样显示、属性照样落地、编译期全绿，只有"模板部件相关的行为"不对（本轮 `PanelElement` 就栽在这里：模板"套上了"、`visChildren=1`，但子图元一直装不进去）。 |
| R74 | **把控件挂到新父之前必须先 `ScadaElementBase.DetachFromParent`**。WPF 对**可视**父会自动解绑，但对**逻辑**子元素直接抛「指定的元素已经是另一个元素的逻辑子元素。请先将其断开连接」（`Panel.Children.Add` 走逻辑树）。本仓两处需要它：画布重挂（`RelinkToParents`）与容器认领（`PanelElement.TakeChild`）。另：**不要在布局过程中改正在被枚举的集合**——容器模板是在 `MeasureCore` 里就绪的，那一刻元素层的 `Canvas` 正在枚举子级，从它里面摘孩子会抛「集合已更改，枚举器无效」（实测栈在 `Canvas.MeasureOverride`）。正解是"有父的子图元直接交给容器、不经过元素层"，容器把模板没就绪的孩子排队等 `OnApplyTemplate`。 |
| R75 | **曲线图元在独立程序集，必须由宿主显式调 `ScadaChartElements.Register()`**（`App.cs` 启动处一处、`ScadaChecks` 一处）。跨程序集的类型静态构造**不会**因为"引用了 DLL"就跑，忘了调的表现是"工具箱里少一个曲线图元"且**不报任何错**。守门人：`ScadaChecks` 的 `[V]` 段（注册成功 + 幂等 + 描述符自检 + 能造控件实例）。 |
| R76 | **底板容器：坐标保持画面绝对坐标，"父带动子"由 `ScadaPage.CascadeContainerMove` 在模型层做，且撤销回放时必须跳过**（判据 `ScadaChangeScope.IsReplaying`）。三条不许变形：① 子图元的 X/Y **不**改成相对底板（否则对齐/分布/微调等六条路径全要按父子重算基准）；② 选中簇**不**展开子图元（分组管"选中谁"、底板管"谁跟着走"，两套一起生效就是双重位移）；③ 回放时**不**联动（父与子各自有自己的记录要写回，再联动一次就是同一段位移推两遍）。守门人：`ScadaChecks` 的 `[C]` 段（联动位移 + 走真实 `ScadaEditHistory.Undo/Redo` 验"不重复联动"）与 `[X]` 段（像素级：嵌套位置、裁剪、联动后位置）。 |
| R77 | **曲线图元的视口由它自己推，不许依赖库的 `AutoScroll`**——那条跟随模式只认**定采样率的信号系列**（按 `SampleRate` 外推当前时刻），散点系列不参与；而趋势图走的是散点语义（X 是每个值真实到达的时刻），靠它画面会永远停在默认轴范围上，**数据全在视野外、看起来就是"一条曲线都没画"**。另：**曲线的像素级验证在无头宿主里做不到**——底层 ScottPlot → SkiaSharp 的 `WpfPlot` 在没有真实渲染目标的进程里画不出内容（离屏 `RenderTargetBitmap` 里只有底色，连坐标轴与网格都没有，实测）。所以断言只钉到"数据进了缓冲 + 描述符与绑定通道齐备"，**不钉像素**；曲线的视觉验证交给 `_ChartProbe5` 探针工程与真机。**不许**写一条在无头环境永远为假的像素断言（那比没有断言更糟，会让人习惯性忽略红条，见 R38）。 |

---

## 24. 启动关键路径上的自检项：插桩落日志、只报不拦的挪出去（2026-10-16）

**起因**：用户问「启动加载算子插件很慢，有什么提速方案吗」。先量再动：日志实测插件扫描同机波动 1.07~28.83 秒（153 样本）、极值 90 秒（冷缓存/杀软离群）——**离群值代码救不了，那是运维层的事**（Defender 排除 / SSD / 预读）；代码层能省的是反射与重复物化。本批做了层 0（插桩）+ 层 2（速赢），层 3（清单缓存）留待后续。细节、真机启动日志对比与插桩分解见 `docs\code-changes\2026-10-16-启动提速第一批（插桩与三个速赢）.md`。

| # | 规则 |
| --- | --- |
| R78 | **启动期的插桩必须落 `ILogService`（写日志文件），不许走 `IUserNotifier`（右下角弹泡）**——弹泡 3 秒就消，启动时根本来不及看，第一版插桩因此一行日志都没留下。同一教训适用于一切"事后要查"的启动诊断：弹泡只配给"用户要立刻看到"的事。 |
| R79 | **"全部失败出口都是 Warning"的自检项不许站在启动关键路径上**。本批把帮助手册合并挪出启动链（实测 1.2 秒，只给帮助弹窗供数），与 2026-10-16 挪走的通讯连通性**同一形状、同一先例**（R57/E18）：① 链上删注册；② `OnInitialized` 补后台调用；③ `OnExit` 里 `_postStartupCts.Cancel()`；④ 异常只落一行 Warn。**挪动前必须先确认懒构建兜底在位**（本例 `HelpViewModel.OnDialogOpened` 的 `!IsBuilt → Build()`）——它是"后台没跑完用户就打开帮助"时的行为保底，删了它就该回链上。守门人：`FlowCanvasChecks\FlowEngineFixChecks.cs` 的 **E19 三条**（链上没有它 / OnInitialized 真的跑了 / 懒构建兜底还在），E18 第 ⑤ 条计数同步改为 4。 |
| R80 | **扫描插件程序集时，目录枚举必须带名字过滤（`GetFiles(dir, "Plugin*.dll")`），反射元数据只物化一次**。Modules\ 里躺着 Roslyn（9MB）/MiniExcel 这类插件依赖，不过滤就连"逐个 FileInfo 再 return"都要扫一遍；算子/相机驱动/运动驱动三类扫描各调一次 `GetTypes()` 是启动期最隐蔽的浪费——取一次类型数组、三处各写各的过滤器。 |

---

## 25. 插件清单缓存：命中路径跳过反射与实例化（2026-10-17）

**起因**：用户裁定层 3（插件清单缓存）按设计落地。查实的关键前提是"缓存命中时连 `Assembly.LoadFrom` 都不能省"——下游三处（相机/运动卡自动连接、流程编译、帮助合并读内嵌资源）都依赖"程序集已在内存里"。所以缓存路径**仍 LoadFrom 全部插件**，只省 `GetTypes()` 反射与 42 个 `Activator.CreateInstance`（插桩实测这两项约 2.5 秒，是大头）。细节、真机两态对比与断言口径收窄过程见 `docs\code-changes\2026-10-17-插件清单缓存（层3-命中路径跳过反射与实例化）.md`。

| # | 规则 |
| --- | --- |
| R81 | **插件清单缓存的正确性全部押在指纹失效判据上，且必须是断言而不是口头约定**。四道全满足才命中（SchemaVersion / 每个 DLL 的大小+mtime / 磁盘上没多出未登记的 `Plugin*.dll` / 宿主契约版本），任一不满足即全量重扫并重写缓存——不做"部分失效部分缓存"（两份状态并存比慢更糟）。失效原因要写进日志（`[插件扫描] 缓存失效（mtime 变了 Plugin.X.dll），全量重扫`），现场要能看出是"指纹对不上"还是"文件损坏"。守门人：`FlowCanvasChecks\PluginCacheChecks.cs` 的 **PC1~PC8**（进常规 gate 基线）。 |
| R82 | **缓存路径跳过实例化的成立前提是"插件构造器无副作用"（只建端口表），这个前提必须由静态扫描钉住**。判据收窄到**构造器体**（不是全文件）：运行期方法里的 `Process.Start`/`HttpClient` 是合法业务（导出后打开文件夹、推送图像），缓存路径只跳过"构造"这一步、运行期照样跑——全文件扫会误伤这两个插件（本轮 PC8 初版就误报了它们）。守门人：PC8（构造器体里出现 进程/网络/程序集加载 调用即红）。 |
| R83 | **缓存产物与全量扫描必须逐项一致，比对时要剔除内置节点**。内置节点（If/Case/While/For/Parallel/Break/Continue/Return）不进缓存（缓存只存 DLL 产物，内置节点每次 `RegisterBuiltInNodes()` 照常跑）——PC6 初版没剔它们，把"46 = 38 插件 + 8 内置"误报成"缓存丢了 8 个"。抽查端口表的对象也必须是**插件**（内置节点不在缓存里，不能当抽查对象）。 |

---

## 26. 枚举中文显示名：先查清上屏路径再补特性（2026-10-18）

**起因**：用户裁定 R40 欠账清单做掉。盘点发现清单里是**三类不同形态**，修法各不相同——盲目全补会白做一半。细节与逐条对账见 `docs\code-changes\2026-10-18-枚举中文显示名欠账核销（R40清单清零）.md`。

| # | 规则 |
| --- | --- |
| R84 | **枚举上屏路径有三条，补特性前先查清目标枚举走哪条**：① `InputPort<T>` → `LinkableValueEditor` → `StepConfigOption.ForEnum` 读 **`[Display(Name)]`**；② `FlatPropertyGrid` 的 `EnumGenerator` 读 **`[Description]`**；③ 视图手写 `LabelConverter` 映射（不走任何特性）。**`ForEnum` 与 `EnumGenerator` 读的不是同一个特性**——PreProcessing 标了 `[Description]`，在 ForEnum 那条路上照样显示英文。只补 `[Display]` 对走 `[Description]` 的枚举无效，反之亦然。 |
| R85 | **裸 `Enum.GetValues` 数组上屏的枚举（如脚本插件的 `ScriptVarType`），光补 `[Display]` 不够，还要把视图改走 `ForEnum`**：`ItemsSource` 改成 `IReadOnlyList<StepConfigOption>`（`ForEnum` 候选），ComboBox 改 `DisplayMemberPath="Display"` + `SelectedValuePath="Value"` + `SelectedValue="{Binding Type}"`（值仍是成员名，R40 口径①）。`StepConfigOption.ToString()` 已兜底成 `Display`，收起状态的选中项不会退回类型全名。 |
| R86 | **断言工程要读插件枚举上的特性，必须让插件工程进引用集**——不在引用集里就反射解析不到类型（`TypeHelper.ResolveType` 返回 null），闸门会假绿或崩掉。本批给 `FlowCanvasChecks.csproj` 补了 `Plugin.CSharpScript` / `Plugin.ImageScript` 两个引用（"生产加载器能打开方案"那条断言也因此恢复——`ScriptVarDef` 的 `$type` 解析需要程序集已加载）。反射解析不到时闸门必须**红着说清是哪个类型没找到**，不许让 `null` 混进 `Enum.GetNames` 让断言自己崩（那就分不清是"缺中文"还是"闸门坏了"）。 |

---

## 27. 组态基座画面：跨页公共层（2026-10-18）

**起因**：用户反馈「底板不符合预期」，要的是"所有页面的统一基座"（实时引用、改一次全部生效），
而 `Hmi.Panel` 是**单页内**的容器图元。两者是不同层的东西，本轮补的是"基座"这一层，
`Hmi.Panel` 保留不动。细节、接入路径与断言台账见 `docs\code-changes\2026-10-18-组态基座画面（跨页公共层）.md`。

| # | 规则 |
| --- | --- |
| R87 | **基座 = 组合 + 实时引用，不许做成页面继承**。基座本身就是一份普通 `ScadaPage`（进 `Pages`、能编辑、能落盘），页面只多一个 `BasePageId` 指向它；`ScadaDocument.BasePages` 单独存"可作为基座"的子集（不与 `Pages` 合并）。**悬空不清理**：基座被摘除/删除时 `BasePageId` 留着（撤销能找回），渲染侧 `ResolveBase` 返回 null → 按"无基座"降级，本页内容一条不丢。**为什么不做继承**：基座是运行时数据不是编译期类型，变成类型树要写 `$type`、改名要改类型名、撤销要回退"类型"；而且会把"基座图元"与"本页图元"揉进同一个 `Elements`，之后每次复制/删除/撤销都要重新回答"这图元是谁的"（两份真相）。**集合订阅必须挂在 `[JsonConstructor]` 里那份字段实例上**（`_basePages.CollectionChanged += OnBasePagesChanged`），只在属性 setter 里挂 = 新建文档（不走反序列化）永远收不到变更 → 注册/摘除基座不进撤销栈、删页时集合不摘除，**且不报错**（2026-10-18 被三条断言当场抓到）；注册/摘除基座各自包一层 `BeginEdit`，`TryRemovePage` 在同一个作用域里把 `_pages` 与 `_basePages` 一起摘（一次撤销两边恢复）。 |
| R88 | **一层限制（基座不能再引用基座）必须在两个入口都拦，外加加载期拆链**。`TryAddBasePage`（该页自己已引用基座 → 拒）与 `TrySetBasePage`（引用方本身是基座 → 拒）是同一不变量的两个入口，只拦一边，另一条路径就能造出"模型里有链、画面上只画一层"的静默故障（渲染侧只合成两层：基座压底 + 本页，链上第二层会被**静默丢掉**，现场看到的是"基座改了没反应"）。`EnsureIdentity` 加载期再兜两道：`BasePages` 里不在 `Pages` 里的幽灵条目摘掉、基座画面上的 `BasePageId` 清成 `Empty`。`CreatesCycle` 保留作防御（手改过的 `.vms` 可能带链）。 |
| R89 | **导航口径唯一：`FindNavigablePage` / `FindNavigablePageByName` / `IsNavigable`；`FindPage` 只给"编辑与持久化"用**。基座画面同时也在 `Pages` 里（要落盘、要能进编辑器），所以按 Id 一定找得到它——但它是公共层不是终点，单独显示它 = 一张没有本页内容的画面。五个消费点必须走可导航判据：`ScadaRuntime.Navigate(Guid…)`、`Navigate(page)`、`GoBack()`（运行中某页可能被改成基座）、`ResolveStartupPage()` 的回落、`SetStartupPage()`、以及 `ScadaPagePickerViewModel` 的跳转候选。**"可寻址"与"可导航"是两件事**，混成一个方法的后果是其中一边必然错。目标恰好是基座时给**专属原因**（"目标画面是基座画面（公共层），不能作为跳转目标"），不许报"方案里没有叫 X 的画面"——它明明在列表里。 |
| R90 | **基座图元必须进 `ScadaCanvas.EnumerateItems()`（基座先出、本页后出），这是"数据泵零改动"的前提**。`EnumerateControls()` 枚举同一批 → `ScadaRuntimeBinder` 建表时看到的就是"全量渲染的图元"，基座上的曲线/报警条/公共指示灯才会活。叠放次序由**产出顺序**表达（先画的在下），不要靠 ZIndex 全局重排。**画面级钩子只对 `CurrentPage` 触发**：基座永远不是当前页，所以它的 `Loaded`/`Unloaded` 不随引用页响——断言里必须有一正一反两条（本页命中 1 次 / 基座命中 0 次），否则"钩子机制没跑"会伪装成通过。**底色边界**：画布底色始终取本页的 `Background`，基座的 `Background` 不参与合成（两页各有背景时谁压谁都会静默吃掉对方）；基座要底色就放一个铺满画面的矩形图元。 |
| R91 | **WPF `SelectedItem` 绑定的候选项类型必须与选中项类型一致；禁止用 `CompositeCollection` + `ComboBoxItem` 占位去"清空"强类型属性**。2026-10-18 实测形状：基座下拉用 `ComboBoxItem`（「（无基座）」）占位、`SelectedItem` 绑 `ScadaPage?`，选占位项时 WPF 把 `ComboBoxItem` 往 `ScadaPage` 里塞 → 绑定引擎转换失败 → 源属性**收不到通知**、模型不变、界面还显示「（无基座）」，**不报错**。正解是让候选项与选中项同一个类型（`BasePageOption`，`Page == null` 表示"无基座"，静态单例 `None` 判等），包装项订阅底层画面的 `Name` 变更转发通知（改名即时可见），重建集合时先 `Detach()` 再 `Clear()`（否则旧项被画面钉住）。**同一处连带坑**：`SelectedBasePage` 从 `ScadaPage?` 变成 `BasePageOption?` 之后，画布的 `BasePage` 绑定必须改成 `SelectedBasePage.Page`——漏改的表现是"基座配了但画布不合成"（绑定类型不匹配、静默为 null）。 |
| R92 | **基座图元在"引用它的页面"上必须只读，三个闸门缺一不可**：① `SelectedElementsProperty` 的 Coerce（`TrimForeignBaseElements`）——挡住框选/宿主赋值/绑定回写；② `SelectedElementProperty` 的 Coerce——挡住点选/右键（**只拦集合不拦主选中**会分叉成"集合空、主选中是基座图元"，而属性面板读的是主选中，于是用户在引用页上改到了公共层）；③ `CanEditElement`（拖动/改尺寸手柄/"锁住"样式三处都读它）+ 左右键命中判据（`OnPreviewMouseLeftButtonDown` 命中基座图元按"点空白"处理；右键命中基座图元要**清选中**让菜单退回画面级，不能只是"不选中"——那会让菜单继续挂在上一批选中上，改到别的东西）。判据只有一处 `IsForeignBaseElement`（属于 `BasePage.Elements` 且不属于 `Page.Elements`）；基座自己的编辑器 `BasePage == null` 所以那边照常可编辑，**不要**另开"是否基座编辑态"的开关。守门人：`ScadaChecks` 的 `[X]` 四条（引用页主选中→null / 集合→剔空 / 本页图元照常可选 / 基座自己的编辑界面照常可选）。 |
| R93 | **「新建基座」= 新建一份空白公共层，不是"把当前页提升为基座"**（`ScadaEditorViewModel.AddBasePageCommand`）。理由：基座是独立的公共层，把画好的一页提升为基座会让那页的**业务图元**突然变成所有页面共享的东西——一次误操作影响整个方案。命名走「基座_N」取未被占用的最小号（与 `ScadaDocument.NextPageName` 同一口径）。建完必须**立刻选中它**：基座图元只有基座自己那一页能改（R92），不切过去用户会以为"新建了但画布没变、也放不进东西"。 |
| R94 | **视图渲染探针三条纪律**（`ScadaChecks` 的 `[T]` 段，2026-10-18 三条都是当场踩出来的）：① **`Activate()` 过的视图模型必须 `Deactivate()` 摘干净**——它订阅的是**静态** `ScadaEditHistory.Changed`，探针跑在自己的 STA 线程上，不摘的话后面任何一段改模型都会回调到一条没人泵的 Dispatcher，**一次连红三段**（"调用线程无法访问此对象"）；② **菜单命令的断言要先给 `PlacementTarget` 再泵 Dispatcher**——`ContextMenu` 不在可视树上，`DataContext` 靠 `PlacementTarget.DataContext` 的 Self 绑定拿，而绑定目标值的刷新是排队的（真机上 Dispatcher 一直在跑，探针里必须手动泵）；不泵就会得到"命令全是 null"的假红；③ **视图不许依赖 `App.xaml` 的内联资源**（本例 `Icon` 字体）：断言宿主没有 `Application`，取不到就是**XAML 解析期**整棵视图失败。正解是把令牌搬进 `Colors.xaml`（R31 的第一层令牌字典）并在视图 `<UserControl.Resources>` 里就地合并（`/UI;component/Themes/Colors.xaml`——UI 工程的资源根是 `Themes`，不是 `Controls/Themes`），再用 `{StaticResource DialogIconFont}`；与 R22 同一族，别再往视图里塞字面量字体 URI。 |

---

## 28. SCADA 轴绑定：轴身份、只读投影与运动命令（2026-10-18）

**起因**：用户要"设计期选一组轴、运行期下拉切换、单控件只显示一根轴"的调试台形态，并要求"解析与绑定逻辑在后台解释器里跑"。落地时把需求定为**轴选择器 + 单轴视图**（不是多轴同屏），随后补了运动命令与系统变量两块。细节见
`docs/code-changes/2026-10-18-SCADA轴面板（轴选择器+单轴视图）.md`、
`2026-10-18-SCADA组态运动命令族.md`、`2026-10-18-系统变量分支（运动轴只读投影）.md`。

| # | 规则 |
| --- | --- |
| R95 | **轴必须有稳定 `AxisId`，按 Id 寻址、名字只做展示与旧数据兜底**（与变量 `VariableId` 同一口径）。新建轴 `Guid.NewGuid()`；**历史轴**（旧 `.vms` 没有该字段）由 `MotionDescriptor.EnsureAxisIds()` 按 `(卡 Id, 物理轴号)` 生成**确定性 Guid**（`MotionAxisId.ForLegacy`，`SHA1("motion-axis\|{cardId:N}\|{physicalIndex}")` 前 16 字节）——同一份旧方案在任何机器、任何次打开都得到同一个 Id，"第一次打开就建好的绑定"重启后仍命中。补齐必须在**算索引指纹之前**（`MotionAxisRegistry.ComputeFingerprintLocked` / `RebuildLocked` 各调一次，幂等），否则索引会停在"补之前"那一版。**轴改名/换物理轴号/换卡都不动 Id**。守门人：`ScadaChecks` AX1（确定性、幂等、改名/换轴号身份不变）。 |
| R96 | **SCADA 的"轴候选清单"是设计期配置，不是 `ScadaBinding`**。它落在属性袋里的规范 JSON（`ScadaAxisOptionsCodec.Key = "AxisOptions"`，`ScadaAxisRef = {AxisId, Name}`），属性行 `Kind = ElementPropertyKind.AxisList` 且 **`IsBindable=false`**（不挂 ƒx）。塞进 `ScadaBinding` 的后果是运行态数据泵会把它当变量去 `IScadaValueSource.TryResolve`，必然解析失败、且属性面板会给出一个"绑了却不生效"的入口。写模型只走 `ScadaPage.TrySetAxisOptions`（归属校验 + `BeginEdit` 撤销作用域）。解析规则（去重、保序、坏 JSON 不抛）只有 `ScadaAxisOptionsCodec` 一处。 |
| R97 | **轴面板只认 `IScadaAxisSource`，绝不直接拿 `IMotionProvider`**——`VM.Scada.Controls` 不认识运动模块（依赖方向约束），而且图元直接读设备会把"谁能碰设备"这条边界打穿。宿主侧 `MotionStatePublisher` 是**唯一**接触 `GetAxisStatus` 的地方：100ms 一拍、按物理轴去重读缓存（不发通信）、整表交换快照；`Version` 只在**拓扑**（身份/名字/可用性）变化时递增，量值变化不算（否则面板每拍重建下拉项）。**拉模式不是推模式**：卡 10ms 轮询，按值推事件会把 UI 线程刷满；面板在统一节拍（`ScadaBeatSource`，200ms）里读当前轴。守门人：AX4（订阅/摘订阅计数、切轴换源、离线"—"）、AX5（拓扑没变不推版本）。 |
| R98 | **运行期"当前显示哪根轴"是控件自己的状态，不写进 `.vms`**；单候选时下拉框收起只显轴名（`SelectorVisibility`/`SingleAxisVisibility`/`EmptyVisibility` 三个由控件算好、模板直绑——本环境 `ControlTemplate` 里的 `DataTrigger` 不触发，见 R24 同族）。**轴被删/停用不自动跳到别的轴**：保留用户选中的 Id、项上标"（已移除）/（不可用）/（已停用）"、量值显示"—"。静默换轴比显示不可用危险得多——下一批加命令后，操作员会对着错误的对象按按钮。**离线不编 0**：`ScadaAxisSample.Invalid(error)` 给 `IsValid=false`，0 是合法位置，会把"不知道"伪装成"在原点"。 |
| R99 | **组态运动命令走动作表 + `MotionActionExecutor`，绝不由图元直接下发**。九条动作（`ScadaActionType` 数值 7~15，只许末尾追加）→ 执行器 → `MotionCommandGate`（唯一门禁实现）→ `IMotionDevice.Enqueue`。三条硬口径：① **"下发 ≠ 到位"**，`Enqueue` 的 `Accepted` 只代表收进队列，不许报完成、不许阻塞等到位；② **有 `AxisId` 就只认 Id，不落到名字兜底**——"删旧轴、又建同名新轴"在产线是常态，按名兜底会驱动到另一根物理轴（错靶比"点了没反应"危险得多）；只有 Id 为空的旧数据才按名解析，且解析成功当场回填 Id 自愈；③ **点动结束只在卡真的收下停止之后才清租约**——先清后发的话，停止被拒（队列满/参数非法）时看门狗与 `Dispose` 都不会再碰这根轴，而它还在动。点动看门狗 200ms 一扫、阈值 `MotionJogGuard.PulseTimeoutMs × 2`，判据复用 `MotionJogGuard`（安全判断只有一份）。守门人：AX7（含"Id 失效时不按名兜底"的反向断言）。 |
| R100 | **系统变量 = 只读、派生、确定性身份的第二变量源，绝不进 `GlobalVariables` / `.vms`**。`VariableType.System`（末尾追加）；名字 `System.Motion.{轴名}.{字段}`（12 个轴级字段）；身份 `SHA1("motion-var\|{AxisId:N}\|{Field}")` 前 16 字节；**不实现 `IWritableVariable`**（写入由现有 `GlobalVariableWriter` / `RegistryScadaValueSource.TryWrite` 如实拒绝），也不实现 `IRenameableVariable`（名字是派生量）。`VariableRegistry` 用**两张表 + 查找时用户优先**（不做一张合并表：合并后两套优先级会在一张表里打架）。**`SystemVariableModel` 的 `IPort.Value` setter 必须静默忽略而不是抛**：端口契约要求它可写，正规写路径已在 `IWritableVariable` 那层拒绝；能走到这个 setter 的只剩"控件 TwoWay 误绑"，而它跑在 UI 线程、本仓 `App` 没有 `DispatcherUnhandledException` 兜底 → 抛就是退进程。守门人：AX8（12 个字段、模拟重启同 Id、实例复用、不落盘、不可写、离线不编 0）。 |
| R101 | **系统变量名是派生量：轴改名时集合不增不减，必须主动重扫名字索引**。`SystemVariableCatalog.Rebuild` 对"同一轴同一字段"复用模型实例（重建实例会让所有 `ValueChanged` 订阅方静默失联），改名只改 `Name` → **一个 `CollectionChanged` 都不会发** → `VariableRegistry` 的名字索引永久停在旧名上（新名查不到、旧名一直命中），而且下一拍"名字看起来已一致"还会让重建被短路，**不会自愈**。修法：`Rebuild` 里记账 `renamed`（比对上一次轴名 + 模型名），改名后调 `IVariableRegistry.RefreshSystemNames()`（只重扫系统表，不遍历用户集合——后者可能正被 UI 线程改）。守门人：AX8 的反向断言**"改名后新名可查、旧名查不到"**——改回"只改 Name 不重扫"它必红。 |
| R102 | **轴改名级联有三跳，注册表只管前两跳**：`MotionAxisRegistry.Rename` 级联点位表 + 凸轮表（它刻意不认识 `ScadaDocument`）；SCADA 画面这一跳由宿主补——`ScadaDocument.RefreshAxisReferences` → `ScadaPage` → `ScadaEventHook.RefreshAxisReferences`（`ScadaAction.MatchesAxis` + `BindAxis` 回填展示名），触发点在 `MotionBoardSettingsTabViewModel` 的注册表改名成功之后（行 VM 通过 `CascadeScada` 回调注入，不让行直接拿工作区）。整条链套 `ScadaWriteGuard.Suspend()`：改名级联是数据自愈，不是用户编辑，不进撤销栈、也不该被写守卫拦（否则 Strict 模式下改名会抛，而一半已经改成功了）。**已知残余**：`AxisOptions`（轴面板候选清单）里的名字不回填——运行期面板按 Id 取得到新名，属性行摘要要等重开选轴弹窗。 |
| R103 | **"设计期配置 + 运行期选择"的图元，断言必须分两段测**：设计段只验"候选清单 → 下拉项 / 单轴收起 / 空态 / 不编造量值"（无运行上下文时位置是"—"、状态是"设计预览"）；运行段才装 `ScadaRuntimeContext` 验"订阅节拍 / 默认选第一根 / 量值落地 / 切轴换源 / 删轴不跳 / 摘上下文订阅归零"。把两段混成一段，最容易得到"设计期看起来对、运行期拿不到值"的假绿——`AxisPanelElement.ApplyCustomProperty`（属性袋 → 候选）与 `OnRuntimeContextChanged`（装/摘节拍）是两条独立入口，必须各测一次。另外**模板必须真的 `ApplyTemplate` 一次并检查命名件**（`AxisCombo`）：XAML 写坏在编译期全绿，只有套模板时才炸；断言宿主没有 `Application`，得显式把 `Generic.xaml` 里对应类型的样式赋给控件（与画布/报警条那段同一处理）。 |

---

## 29. `RequireRefresh` = 整表重建，不许收窄成"只重建该属性所在的页签"（2026-10-19）

**起因**：真机反馈「通讯设置里选择 ModbusRtu 不显示串口、选择 Tcp 不显示网口，原来动态反射配置属性的功能怎么没了」。
模型层一直是对的（`CommunicationConfig.Protocol` 的 setter → `TryCreateConfig` 按协议 new 出对应 `*Config`），
坏的是属性面板的刷新范围：`PropertyGridBase` 把 `RequireRefresh` 的消费点改成了"只重建该属性所属的页签"
（`RebuildTabForProperty` + `_tabByKey`），而**切换开关与被它换掉的子对象不在同一个页签**——
`Protocol` 在「1. 基本设置」，`Config` 在「2. 链路配置」，于是链路配置里的嵌套子网格永远绑着旧实例。
细节、探针实测三场景与变异反证见 `docs\code-changes\2026-10-19-通讯设置协议切换后链路参数不刷新（属性面板只重建单页签）.md`。

| # | 规则 |
| --- | --- |
| R104 | **`[SuperDisplay(RequireRefresh = true)]` 的消费动作必须是整表重建（`UpdatePropertyGrid(preserveSelectedTab: true)`），不许"只重建触发者所在的页签"**。判据是"**被驱动的对象会不会在别的页签**"——只要可能，单页签重建就一定漏。`preserveSelectedTab` 负责按页签标题保住选中项（整表重建换掉全部 `TabItem` 实例，不保就跳回第一页）；分组整体消失时落回第一页，不保留已不存在的页签。`RebuildTabForProperty` 与 `_tabByKey` 一并删除（这个能力本身就是陷阱，留着迟早有人再用一次）。**普通属性仍不触发重建**（走 WPF 绑定），50ms 防抖 / 异步投递 / 跨线程封送一律不动。守门人：`FlowCanvasChecks\PropertyGridRefreshCheck.cs` 的 **PG0~PG8**（进常规 gate 基线）——PG2 必须调 `UpdatePropertyGrid(`、PG3 必须带 `preserveSelectedTab: true`、**PG4 反向**禁止 `RebuildTabForProperty` 复现、PG5 公开签名保留，PG6~PG8 钉住"`CommunicationConfig` 的 `Protocol` 仍标 `RequireRefresh`、`Config` 仍标 `[PropertyItem]`"（前提没了闸就变空跑，R38）。变异反证两批：去掉 `preserveSelectedTab` → PG3 红；改回单页签重建 → PG2/PG3/PG4 三条同时红。 |
| R105 | **这类"只在特定操作顺序下坏"的刷新缺陷，静态闸挡不住，必须按真实操作顺序各跑一遍**。本轮的两种顺序给出完全相反的结论：**先改协议再第一次点开子页签 → 正常**（懒加载那刻才构建，取到新实例）；**先点开过子页签再改协议 → 永远不刷新**，而且来回切页签也不自愈（旧页签仍在 `_builtTabs` 里，懒构建不会重跑）。所以"我手动试了一下是好的"不构成证据——**要先问"用户会以什么顺序操作"**，把每种顺序都测一遍。范式：临时探针工程引用 `UI` + `Core`，用真 `CommunicationConfig` + 真 `FlatPropertyGrid` 挂在**屏幕外真窗口**（`Left=-4000`）+ 手动 `Dispatcher` 泵（`ApplicationIdle` 每轮 15ms）里跑；探针用完即删，不留在仓库。 |
| R106 | **多态配置不许专门搭窗口**：给切换字段标 `RequireRefresh = true`、给被切换对象标 `[PropertyItem(Type = typeof(Control))]`、**换成一个新实例**（原地改字段不会换绑，子网格构建时抓的是当时那个实例）——三条齐了就自动出"选 ModbusRtu 显示串口、选 Tcp 显示网口"的效果。用法已沉淀为 skill `.zcode/skills/vm-property-grid/SKILL.md`（含生成器 Priority 表、枚举显示名两条路 R84、草稿/取消语义、常见坑速查、以及"什么时候确实该手写视图"的判据：界面元素与字段一一对应 → 属性面板；要表达关系/图形/时序 → 手写视图）。**判断"要不要手写视图"时先查这份 skill，别再为一张字段表单新建 `.xaml`。** |
| R107 | **互斥的字段集不许平铺在同一个 `[SuperDisplay]` 类上——必须做成"切换字段 + 嵌套子对象"**。属性面板只按"这个类上有没有 `[SuperDisplay]`"决定渲染什么，**没有任何"按另一个字段隐藏同行字段"的能力**（`Visible=false` 是编译期固定的，不是条件显示）。所以把 TCP 的 `Host/Port` 与串口的 `PortName/BaudRate` 平铺在同一个 `IoBusEditModel` 上，界面必然两种参数同时出现；把 Modbus 的 `UnitId` 与 S7 的 `Rack/Slot` 平铺在同一个 `IoDeviceEditModel` 上，同理。**正解（与 `CommunicationConfig` 同一范式，三条缺一不可）**：① 切换字段（`TransportKind` / `DriverId`）标 `RequireRefresh = true` 且**走 `SetProperty`**（普通自动属性不发 `PropertyChanged`，`RequireRefresh` 等于没写——这是本轮第二个错误，改模型时最容易漏）；② 被切换对象（`Transport` / `DriverConfig`）标 `[PropertyItem(Type = typeof(Control))]` 且**每次切换换一个新实例**；③ 每种取值的字段集各自独立成类（`TcpLinkEditModel` / `SerialLinkEditModel`、`ModbusDriverEditModel` / `S7DriverEditModel`）。配套两条：**切换时保留另一种取值的草稿实例**（来回切不丢已填参数），**写回描述符时两种都保留**（描述符本就并存两套，运行期只看当前那一套）。守门人：`IoChecks` 的 `TestBusLinkSwitching` / `TestDriverConfigSwitching`（**18 条**，进常规 gate 基线）——判据直接调 `PropertyGridDefaults.GetVisibleProperties(子对象)` 看**网格实际会渲染哪些字段**，断言"TCP 链路的可见属性不含 `PortName`、串口链路的可见属性不含 `Host`、Modbus 驱动不含 `Rack`、S7 驱动不含 `UnitId`"，并反向钉住"切走再切回保留原值""写回 JSON 只含当前驱动字段"。**断言必须查可见属性集，不能只查对象类型**——`Transport is SerialLinkEditModel` 通过而字段仍平铺的话，界面照样错。 |
| R108 | **轴面板自带的操作按钮与组态动作表必须走同一个执行器，且三件事缺一不可**：① **命令出口** `IScadaAxisCommandSink` 由宿主注入（`MotionActionExecutor` 同时实现它），面板不直接拿 `IMotionProvider`——否则门禁与点动看门狗会长出第二份；② **权限在按下之前就判**（`RuntimeContext.AccessPolicy.CanOperate(Element.RequiredRole)`），`CanOperate` 直接绑按钮容器的 `IsEnabled`——灰掉比"按下去没反应"诚实；每拍重算一次（登录/登出会改当前角色），`OnElementRefreshed` 也补一次（属性面板改 `RequiredRole` 后立刻生效）；③ **点动必须按住才动**：`PreviewMouseLeftButtonDown` 起、`PreviewMouseLeftButtonUp`/`MouseLeave`/`LostMouseCapture` 停（要 `CaptureMouse`，否则按住后移出按钮收不到 Up → 轴一直走）；**回零必须长按 0.5s**（`DispatcherTimer`，松开即取消），与参考模板一致、防误触。`ShowCommands=false` 时整块操作区收起，退化成纯监视面板。**六个命名件**（`BtnEnable`/`BtnStop`/`BtnHome`/`BtnJogForward`/`BtnJogReverse`/`BtnClearAlarm`）由断言钉住——`GetTemplateChild` 拿不到就是"永远点不动的按钮"，编译期全绿。**另：不要在 `Control` 子类上声明名为 `IsEnabled` 的依赖属性**（会遮蔽 `UIElement.IsEnabled`），轴使能状态用 `AxisEnabled`。守门人：AX9（出口/权限/收起）+ AX3 的命名件检查。 |

---

## 30. UI 工作流：Web 基准 → WPF 复刻 → 通用入库（2026-10-19）

**起因**：用户裁定——桌面端界面过于陈旧，此后 UI 一律以 Web 端为设计基准，用 WPF 复刻，通用样式沉淀入库，避免各页面自行发挥导致风格不统一。

**适用范围**

- **R108（适用）**：新增或改版任何界面（页面、视图、弹窗、面板、工具栏、主题与控件样式），以及 UI 库/组件库建设。
- **R109（不适用）**：纯业务逻辑、数据层、算法与非可视代码改动。
- **R110（基准唯一）**：Web 端（设计稿或已上线的 Web 版本）是本仓 UI 的**唯一设计基准**。无 Web 基准时，先产出设计稿或指定参考界面再动手，不许直接按桌面习惯发挥。

**执行顺序（默认三步，不可跳步）**

- **R111（第一步 · Web 设计）**：先落纸六项——布局结构（分区/栅格/层级）、间距刻度、配色令牌、字号字重、组件五态（Normal/Hover/Pressed/Focus/Disabled）、交互细节（动效时长与缓动、加载/空/错误三态、键盘可达性）。
- **R112（第二步 · WPF 复刻）**：用 WPF 原生能力按上述基准实现，以"接近 Web 设计稿"为唯一目标；不因平台差异自行改布局、改间距、改控件形态。
- **R113（第三步 · 通用入库）**：出现跨页面共用、同一界面重复 ≥2 次、或属于基础控件的样式，必须沉淀进 `UI/Controls/Themes` 资源字典或 `UI/Controls` 组件库，以可复用样式/组件引用，**禁止在各页面重复手写**。

**复刻要求**

- **R114（视觉一致）**：布局分区、间距、圆角、阴影层级、颜色、字号字重与设计稿一致。允许的差异仅限平台强制项（系统字体度量、DPI 缩放），且须在交接中逐项列明。
- **R115（状态一致）**：每个可交互控件五态齐全，键盘焦点必须可见；状态缺失视同未完成。
- **R116（交互一致）**：动效时长与缓动与 Web 基准同档（一般 150~200ms）；加载/空/错误三态必须有明确视觉。
- **R117（令牌化）**：颜色、间距、圆角、阴影、字号只能引用资源令牌；视图内禁止硬编码数值与十六进制色值。
- **R118（禁止自行发挥）**：桌面端不得以"更适合桌面"为由擅自改变布局、信息密度、控件形态或交互路径；确有必要时按 R125 升级决策。

**沉淀要求**

- **R119（入库位置）**：主题令牌进 `Colors.xaml`；基础控件与样式进 `UI/Controls/Themes` 对应分册；跨工程复用组件进 `UI/Controls`。
- **R120（复用优先）**：新页面先检索 UI 库，能复用不新建；确有差异用样式派生或附加属性扩展，不复制模板。
- **R121（反向清理）**：改造中发现页面内的重复样式与魔法值，同批迁入 UI 库并删除原处副本，避免两套并存。

**验收标准（缺一条不算完成）**

- **R122（对照设计稿）**：逐项核对布局、间距、配色、字体、五态与交互细节，并提供改前/改后截图；每处差异写明原因与是否可接受。
- **R123（工程闸门）**：无硬编码颜色与尺寸；通用样式已入库、页面无重复手写；`UIThemeSmokeTest` 通过，资源断链契约（R30-R32）与图标按钮裁切契约（R68）不回归。
- **R124（判定标准）**：最终判定以"接近 Web 设计稿"为准，不以"桌面端看起来还行"为准。

**例外与升级**

- **R125（无法复刻）**：平台无法复刻或复刻成本明显不合理时，先记录差异点与替代方案，交 architect 评审，经用户确认后方可偏离，并在 `docs/code-changes/` 留档。

### 30.1 交付边界与实施流程（2026-10-19）

**起因**：用户裁定——Web 端只作设计媒介，**不把 Web UI 实现作为交付内容**；先在 Web 形式下完成设计与确认，再把同一套设计复刻到桌面端。

| # | 规则 |
| --- | --- |
| R126 | **Web 端产物是设计媒介，不是交付物**。允许产出可点击 HTML / 截图 / 原型用于评审，但不得进入仓库构建、不得部署、不得作为"另一个客户端"维护。产品交付物只有四类：**WPF 代码、令牌映射表、截图证据、差异清单**。 |
| R127 | **流程固定四步，不可跳步**：① 设计基线（Web 形式，含默认态 / 关键流程 / 边界态）→ ② 设计定稿与令牌映射（Web token ↔ WPF 资源键，标注允许偏差）→ ③ 桌面端复刻（按 30 节批次：令牌层 → 基础控件 → 壳层 → 弹窗/视图）→ ④ 验收（截图逐块对照 + 差异清单）。**没有设计基线不得直接改桌面端样式**。 |
| R128 | **设计基线存放在 `docs/UI设计基线/<页面>/`**，由目录内 `README.md` 记录版本号、确认日期、确认人与覆盖范围；该目录**不参与任何工程构建**。基线只读：变更必须升版本，并同步映射表与已复刻页面（防止设计与实现漂移）。 |
| R129 | **对应关系的判定口径**：布局分区、信息层级、控件形态、文案位置、状态语义、交互路径必须一致；字体度量、滚动条宽度、阴影层数、动效曲线的细微差异允许存在，但必须逐条写进该页面 README 的"已知偏差"。判定以"同一屏、同一状态"的截图对照为准。 |
| R130 | **复刻差异必须在设计阶段预判并写进基线**，至少覆盖：字体度量、DPI 缩放（125%/150% 下 1px 线）、滚动条占位、flex/grid 与 WPF 布局差异、多层阴影不可复刻、动效曲线近似、文本截断、表格列宽（GridView 无星号列宽）、焦点态（显式样式会丢默认焦点框）、无障碍（`AutomationProperties`）。 |
| R131 | **禁止用"桌面端实现更方便"反向修改设计**。设计变更只能由设计基线升版发起；实现侧遇到不可复刻项，按 R125 走升级决策，不得静默降级。 |
| R132 | **验收由用户裁定**：每批交付必须附"改前/改后截图 + 与设计基线的逐项对照表 + 差异清单"；差异清单未关闭或未经用户接受的，不得宣称完成。 |

### 30.2 自定义控件模板的高度表达（2026-10-19）

**起因**：IO 健康弹窗的页签文字被裁成一条（真机截图）。模板用 `Grid` 的 `Auto` 行承载文字、`2px` 行承载指示条，靠 `ContentPresenter` 的上下 `Margin` 撑开高度；在对话框宿主里高度分配不稳定，文字被压没。细节与探针证据见 `docs\code-changes\2026-10-19-IO健康页签文字被裁（TabItem模板依赖Grid行高分配）.md`。**排查流程已固化为 Skill `.zcode/skills/wpf-ui-debug/SKILL.md`——改主题/模板后"没生效"先照它走，不要凭截图猜。**

| # | 规则 |
| --- | --- |
| R133 | **自定义 `TabItem` / 列表项模板不许用"Grid Auto 行 + 子元素 Margin"来表达上下留白**。高度必须由样式显式决定：`MinHeight` + 模板根 `Border.Padding`；指示条用 `Border.BorderThickness` 的底边表达，不要再加一行 `RowDefinition`。判据：渲染探针量 `TabItem.ActualHeight ≥ 36` 且文字完整可见。 |
| R134 | **验证 .NET 9 的 WPF 程序集必须用 `net9.0-windows` 探针工程**（`dotnet run`），**不许用 PowerShell 5.1**：它是 .NET Framework 宿主，`Assembly.LoadFrom` 看似成功，但资源字典加载为 0 条目、`LoadComponent` 直接报 `System.Runtime, Version=9.0.0.0` 找不到——据此得出"样式没生效"是假结论（本轮据此白排查两轮）。探针工程放临时目录，用完即删，不进仓库。 |
| R135 | **弹窗内的可聚焦控件必须挂 `FocusVisualStyle="{StaticResource DialogFocusVisual}"`，ToolTip 必须用 `<ToolTip Style="{StaticResource DialogToolTipStyle}">`**。默认 `FocusVisualStyle` 画的是黑色/虚线矩形（真机截图里关闭按钮外那圈方框），默认 `ToolTip` 是方角系统灰边——两者都会把现代圆角界面瞬间拉回旧风格。两个样式定义在 `UI/Controls/Themes/Dialog/Dialog.Misc.xaml`（显式键控，因为 Fluent 在 App 级最后合并会顶掉隐式样式）。**注意**：对话框打开时键盘焦点默认落在第一个可聚焦元素上（通常是标题栏关闭按钮），所以焦点环会"自己出现"；若不想让关闭按钮抢初始焦点，用 `FocusManager.FocusedElement` 指到内容区，**不要用 `Focusable="False"`**（键盘用户会够不到它）。 |
| R136 | **运行期"改配置"必须走"权限闸门 + 同一个命令出口"，且写的是方案活对象（随保存落盘、不进撤销栈）**。用户裁定「有足够权限就可以改」后，点位表的示教/改速度直接写 `MotionDescriptor.Points`。三条口径：① **权限在按下之前判**（`AccessPolicy.CanOperate(Element.RequiredRole)`），按钮 `CanExecute`、行内输入框 `CanEdit`、命令执行前各判一次——只判其中一处，绕过按钮直接调命令就漏了；② **示教写"当前轴位置"而不是行上原位置**，所以必须先有有效快照（离线明确拒绝，不写一个猜的值）；③ **定位与动作表走同一条路径**（`TryMove` → `ScadaActionType.MotionMoveAbsolute` → `CheckMove`），软限位/未使能/未回零/忙的判据只有一份；**示教/改速度不做门禁**（它们不动轴），但要校验数值有限且速度 ≥ 0。**图元侧不直接拿 `IMotionProvider`**：点位读 `IScadaAxisSource.TryGetPoints`，写 `IScadaAxisCommandSink.TryTeach/TrySetPointSpeed`，宿主侧同一个 `MotionActionExecutor` 实现两件事。**模板纪律**：行 `DataTemplate` 里没有 `TemplatedParent`，颜色/可见性/可编辑性一律用 `RelativeSource AncestorType=图元类型`（写 `TemplateBinding` 拿到 null）；ComboBox 样式**不许跨分册 `StaticResource`**（R30），本分册自带一份（键名加前缀）。守门人：AX7 的点位三条 + AX10 十条。 |

### 30.3 Modbus 地址串的语义（2026-10-19）

**起因**：给自写 RTU 从站做互通测试时，客户端把 `"40001"` 发成了 `9C 40`（= 40000），而不是 0。查 HSL 源码确认：`ModbusInfo.AnalysisAddress` 只按 `AddressStartWithZero` 决定**是否减 1**，**不会**把 `4xxxx` 剥成寄存器偏移。细节见 `docs/code-changes/2026-10-18-IO接入层（方案级配置+系统变量+Modbus-S7驱动）.md` 第十批。

| # | 规则 |
| --- | --- |
| R137 | **Modbus 点位的 `Address` 是"原始协议地址"，不是 PLC 变量表里的 `4xxxx` 编号**：`"40001"` 落到第 40001（0 基）或第 40000（1 基）号寄存器，**不是**"第一个保持寄存器"。三条口径：① 按原始地址写就直接写 `"0"`/`"1"`；要显式指定区域用 HSL 的 `x=3;0`（保持寄存器）/ `x=1;0`（线圈）；② `ModbusDeviceConfig.AddressStartWithZero` 只控制**是否减 1**，默认 `true`（HSL 默认、与既有方案一致）；按 1 基编号配置时必须关掉，否则会**静默读写到相邻寄存器**（不报错、值不对，最难查的一类故障）；③ 新增 Modbus 类驱动必须把该开关透传到客户端（`ModbusTcpNet` / `ModbusRtu` / `ModbusRtuOverTcp` 都有该属性），UI 的 `ModbusDriverEditModel` 必须同步 `ToJson`/`From`——否则"界面上能改、运行时无效"（R33 同族）。守门：`IoChecks` M.6~M.10（同一从站、同一地址串，1 基与 0 基必须落到不同寄存器）。 |
| R138 | **配置型数据的运行期展示必须"每拍重读 + 指纹短路"，不能只靠"自己写的时候刷一次"**。点位是 `List<MotionPoint>`（不是可观察集合），外部（「运动卡设置 → 点位列表」）改了它**没有任何变更通知**；只在自己示教/改速度后刷新，运行中的表就永远看不到外部改动。正解：在统一节拍里每拍 `TryGetPoints` 重读一次，用**指纹**（轴 Id + 每行 序号/名字/位置/速度，数值用 `"R"` 往返格式）比对，**只有变化才重建行**——无条件重建会把用户正在输入的速度框连行一起换掉（WPF 会丢掉焦点与未提交文本）。同理适用于任何"配置在别处改、运行画面要跟"的列表（报警定义、变量事件、配方表）：**先问"谁还会改它、改完有没有通知"**，没有通知就必须轮询 + 短路。守门人：AX10 的两条（外部改点位后自动刷新 / 没变化时不重建行——后者用引用相等断言，防"每拍都重建"的过度修复）。 |

### 30.4 派生只读属性与版本号（2026-10-19）

**起因**：`FlowCanvasChecks` clean build 实测 8 条 `Version +1` 断言全红（实际 +2）。根因是容器里加步骤时，`ContainerStepCountWatcher` 会主动 `RaisePropertyChanged(nameof(TotalStepCount))`；而 `TotalStepCount` 是从 `Children`/`Steps` 派生的只读计数、没被 `[RuntimeState]` 排除，`FlowModel.OnStepPropertyChanged` 于是又计了一次版本。细节见 `docs/code-changes/2026-10-19-系统健康面板阶段0（门禁可信化与三处前置修复）.md`。

| # | 规则 |
| --- | --- |
| R139 | **任何由代码主动 `RaisePropertyChanged` 的派生只读属性，只要它挂在 `StepModel` 子类上，就必须标 `[RuntimeState]`**。`FlowModel.OnStepPropertyChanged` 的判据是"属性名不在 RuntimeState 名单里 → Version++"，它**不看属性有没有 setter**；而"派生值变了"的根因通常已经由结构变更计过一次版本，再计一次就是**一次操作涨 2**（表现为"改一下要重编译两轮"）。三条口径：① 典型成员：`TotalStepCount`（For/Condition/Parallel 各一份）、任何由 watcher 驱动的 `Count`/`Summary`；② **不要**改成在 `FlowModel` 里加字符串名单——那正是 `RuntimeStateAttribute` 要取代的旧写法（重命名即静默失效）；③ 新增 watcher 时先问一句"我 raise 的这个属性会被 FlowModel 当成语义变更吗"。守门人：`FlowModelVersionChecks` 的 E14 八条（容器/分支/往返四类各两条，去掉 `[RuntimeState]` 立刻红）。 |

### 30.5 颜色令牌：第一方 XAML 不许硬编码（2026-10-19）

**起因**：核对"后期切主题能不能只改令牌"时实测——全仓 **1458 处 `{StaticResource *Brush}`、0 处 `{DynamicResource *Brush}`**，另有 **64 个第一方 XAML 文件共 ~700 处 `#RRGGBB`**（`LogView` 46、`ScadaVariableEventDialogView` 56、`Shell.xaml` 35…）。颜色令牌化此前**没有任何自动闸**，新界面写死一个 hex 编译期全绿、切主题时才花屏。

| # | 规则 |
| --- | --- |
| R140 | **第一方 XAML 里除"颜色定义文件"外不许出现 `#RRGGBB`/`#AARRGGBB`**。颜色定义文件只有四个：`UI\Controls\Themes\Colors.xaml`（第一层令牌，**新令牌只加这里**）、`UI\Controls\Themes\Fluent\FluentTokens.xaml`、`UI\Controls\Themes\PluginConfigColors.xaml`、`VisionMaster\Themes\DockThemes\*`（AvalonDock 调色板）。其余一律用 `{StaticResource Dialog*Brush}` 等令牌。**存量按基线"只减不增"**：`UIThemeSmokeTest\hardcoded-colors.baseline.txt` 记录 `路径|数量`，**新文件必须是 0、老文件数量不许上涨**；某个文件清零或下降时日志会提示"基线可收缩"，把该行删掉/改小即可逐步清零。守门人：`UIThemeSmokeTest\Program.cs` 的 `RunHardcodedColorChecks()`（进冒烟返回码）。变异反证：给 `SystemHealthView.xaml` 加一个 `#FF123456` → 准确红并指出文件与数量；去掉 → 绿（实测 126 个第一方 XAML / 基线 64 个文件）。**两条相关边界**：① 现在**没有运行时主题切换**——`StaticResource` 是加载期一次性解析，切主题的现行口径是"换字典 + 重启"；要做**点按钮立刻变**，得把颜色引用迁到 `DynamicResource`（1458 处，属独立批次），并且先把基线里那 64 个文件清掉，否则会出现"大部分变了、几处还是浅色"。② `Shell.xaml.cs` 里"切深色主题把 Source 换成 DarkTheme.xaml"说的是 **AvalonDock 的 `dockManager.Theme`**，不是 WPF 资源字典，别按它去换字典。 |
| R141 | **可复用的 UI 一律抽进 `UI\Controls\Themes\Dialog\` 的样式分册，不许在视图里复制一份**。判据：同一段视觉结构（卡片外壳、列表行、工具栏按钮组、计数徽标、空态、状态点/胶囊）**第二次出现**时就必须下沉——先查现有键（`DialogColumnCard` / `DialogColumnHeader` / `DialogColumnTitle` / `DialogOptionItem` / `DialogTag` / `DialogChip*` / `DialogEmptyState*` / `DialogIconButton` / `DialogInput` / `DialogCheckBox`），缺什么补到对应分册（容器类 → `Dialog.Container.xaml`，按钮/胶囊 → `Dialog.Chip.xaml`，列表 → `Dialog.List.xaml`）。**三条口径**：① **跨分册 `StaticResource` 仍然禁止**（R30）——新样式只能引用 `Colors.xaml` 与本分册已合并的子树，需要共享的颜色令牌加进 `Colors.xaml`；② 视图侧只允许写"布局 + 绑定"，`Background/BorderBrush/Foreground/CornerRadius/Padding` 这类外观值要么来自样式、要么来自令牌，**不许在视图里就地写魔法数字**（与 R140 同一族）；③ 抽公共控件时**不改既有键的语义**——需要变体就加 `...Accent` / `...Danger` 后缀的新键，`BasedOn` 基础键，避免"改一处、全局变样"。**判据落地方式**：新界面评审时对照 Web 原型逐块问一句"这块在库里叫什么键"；答不出来的就是该补的样式。守门人暂为人工评审（`UIThemeSmokeTest` 只能查"有没有硬编码"，查不出"该抽没抽"）。 |

---

## 31. 流程栏 v2 视觉改版：整行 section bar 与聚合计数（2026-10-19）

**起因**：用户裁定按 v2 设计基线（`docs/UI设计基线/process-panel/`）重做流程栏。三件事有复用价值：整行 section bar 的做法、聚合计数的通知、渲染探针的图标陷阱。细节与证据见 `docs/code-changes/2026-10-19-流程栏视觉改版（v2设计基线复刻）.md`。

| # | 规则 |
| --- | --- |
| R139 | **树行要"铺满整行"时，`ContentPresenter` 必须 `Stretch`，且 section 色条要靠负边距 + 箭头 `ZIndex` 延伸到展开箭头下方**。默认 `HorizontalAlignment="Left"` 会让所有行按内容宽度排，状态底色/分组条都横贯不了。做法：数据模板根 `Margin="-22,…"`（把条左移到行左缘）、内容再用 19/22 的左内边距拉回 30px 竖线、模板里的 `Expander` 设 `Panel.ZIndex="2"` 压在条上（否则不透明的条会盖住箭头）。**改 TreeViewItem 模板的 Padding/箭头列宽时，-22 与 19/22 要同步**，否则三种行的竖线错位。守门：`RenderProbe` 场景 F 出图 + 三条点击拦截判据（分支卡拦/箭头放行/算子卡不拦）。 |
| R140 | **`ObservableCollection` 只替自己发 `Count` 通知，不替容器发聚合值**。容器组头要显示「N 分支 · M 算子」时，`Children.Count` 可以直接绑（自带通知），但"各分支 Steps.Count 之和"必须自己订阅：`Children` 增删 + 每个分支 `Steps` 增删 → `RaisePropertyChanged`。本仓已封装 `ContainerStepCountWatcher`（`Core/Models/ProcessStep/`），`Children` 被反序列化整体替换时由 `Rebind` 摘挂旧集合。**没有通知的只读计算属性 = 拖入/删除算子后界面永远停在旧值**（编译期全绿，只有真机看得出）。 |
| R141 | **`RenderProbe` 出图里的"豆腐块"不是产品缺陷**：探针把 `app.Resources["Icon"]` 直接设成 `Segoe MDL2 Assets`，它优先于场景 F 后合并的 App.xaml 内联资源（Font Awesome），所以 `&#xf06a;`、插件图标在探针图里显示为空框。**判据**：看探针图只判布局/底色/对齐，字形必须回真机或按 App.xaml 的 `Icon` 资源核对；不要把探针的 tofu 当字体 bug 修（会改坏真机）。 |
| R142 | **同一类图元的多个"预设"只能是一个控件类 + 多套描述符默认值，不许复制成多个控件类**。轴控台（监控+运动+点位）、轴面板（只操作）、旧点位表（兼容）共用 `AxisPanelElement`：**一个 `AxisOptions` + 一个 `SelectedAxisId`**，预设只差 `ShowCommands` / `ShowPoints` 两个默认值。复制成三个类 = 三份轴选择状态，现场就会出现"监控看 X、点动动 Y"的错靶（用户裁定合并的直接原因）。默认值覆盖走 `ScadaPropertyCatalog.WithDefaults(...)`（复制描述符、不改缓存实例）；旧 TypeKey 保留注册但 `ShowInToolbox=false`，工具箱按该位过滤。**合并控件的切轴必须"先对旧轴收手"**：`SelectedAxisId` 变化时用 `e.OldValue` 直呼出口发 `JogStop`（**不能用按当前选中轴的 `EndJog()`**——那时已经是新值，会把停止发给新轴、旧轴继续走到看门狗）；同时停回零长按计时器、清操作文案。`CanEdit = CanOperate && ShowCommands` 控制行内速度框；点位区收起时**不读** `TryGetPoints`（懒加载）。守门人：AX11（三预设默认值 / 一个选择器同时驱动监控与点位 / 切轴停旧轴）+ AX10（合并后的点位写入与自动刷新）。 |
| R143 | **改面板默认宽度/尺寸必须同时处理已保存的 `Layout.xml`**：AvalonDock 恢复布局时用文件里的**绝对像素**（实测流程栏被拖成 230）完整覆盖 Shell.xaml 的默认值，只改 XAML 对老用户等于没发生（与 R43"旧布局抵消改造"同族）。正解 = 反序列化后按 `ContentId` 找到面板所在的列并**夹紧**（`LayoutHelper.EnsureProcessColumnWidth`：`DockMinWidth=320`、绝对宽度 `<320` 拉回 `340`），只动这一列，**不升结构版本**（R43 的触发条件是"面板归属/停靠语义变了"；只修宽度不该丢掉用户其它布局）。守门人：`UIThemeSmokeTest.RunPanelModeChecks` 第 ⑨ 条——把列改成 230 → 调夹紧 → 必须回到 340/320（变异反证）。 |
| R144 | **出厂布局快照（`DefaultLayout.xml`）必须每次启动重捕获，且"加载布局"命令不许刷新它**。`LayoutHelper.Load(bool refreshDefaultSnapshot)`：启动路径（`OnContentRendered`）传 true——此刻 `LoadFromString` 还没跑，内存里必然是 Shell.xaml 默认布局；「加载布局」命令传 false——那时内存里是用户布局，刷新会把用户布局写成"出厂默认"。**"只捕获一次"会让历史错误快照永久留存**：实测 2026-10-18 的 `DefaultLayout.xml` 里流程栏 `DockWidth="25"`、两个左栏 pane 连 `DockHeight` 都没有，点"恢复默认布局"等于恢复成一条缝。捕获前先 `UpdateLayout()`，宽高才是实测值。**恢复命令必须"先确认、失败也说话"**：`Reset()` 在快照缺失/过期时只删文件返回 false，命令不提示就是"点了没反应"（现场会当成功能坏了）。守门人：`UIThemeSmokeTest.RunPanelModeChecks` 第 ⑩ 条——序列化 ShellCopy 后必须含 `DockWidth="340"` 与 `DockHeight="180"`（变异反证：改 230 / 去掉 180 → 红）。 |
| R145 | **AvalonDock 的"让空间"与标签页有三条硬语义，写模式收敛前必须知道**：① **`DockHeight = 0` 会被夹回原值**（实测 set0 读回 200），要收起一个 pane 用 **1px**；幂等判据不能用高度（收起后是 1 不是 0），用"是否已保存过原高度"的字段。② **同一 pane 的多个 anchorable 是标签页**：切标签看 `IsSelected`，`IsActive` 是"整个 DockingManager 的活动内容"——`ShowPanel` 里少一句 `anchorable.IsSelected = true`，会停在最后 Show 的那个标签上（属性/图层合并后实测）。③ **`ShowAnchorable` 会重挂 LayoutContent**，模式切换后**旧引用会失效**——断言/后续代码必须重新 `FindPanel`，不能拿切换前的对象读状态（本轮两条断言假红就是拿旧实例量高度）。**组态模式的"让空间"收敛**（`ApplyScadaPanelMode`）：左栏（程序/流程栏）收起 + 底部日志收 1px；退出恢复。属性/图层合并属于**面板归属变更**，必须同时升 `CurrentLayoutSchemaVersion`（3→4，R43）；左栏/日志的收起是模式收敛，不升版本。守门人：`UIThemeSmokeTest.RunPanelModeChecks` ③b 三条（左栏收起/恢复、日志 1px/恢复 200、属性图层同 pane 且属性选中）。 |
| R146 | **新增库样式键（`UI\Controls\Themes\**` 里的 `x:Key`）之前，必须全仓查同名，且键名要带用途后缀**。**2026-10-20 事故**：给 IO 设置补主/危险按钮时新加了 `DialogChipDanger`（`TargetType="Button"`），而库里**早就有**同名键——它是 `Border` 样式（`BasedOn FluentChipDangerStyle`，状态胶囊用）。新键把旧键顶掉后，视图把 Border 样式套到 Button 上，真机打开 IO 设置直接抛 `XamlParseException: "{DependencyProperty.UnsetValue}" 不是属性"BorderBrush"的有效值`（栈落在 `FrameworkTemplate.LoadTemplateXaml` → `Border.MeasureOverride`，看起来像模板/资源断链，实际是**键名撞车**）。**三条口径**：① 加键前 `grep 'x:Key="你的键"'` 扫 `UI\Controls\Themes` 全目录（不是只看目标分册——`FluentAliases.xaml` 里有一大批同名不同 TargetType 的键）；② 键名带用途后缀：按钮用 `...Button`（`DialogChipDangerButton` / `DialogChipPrimaryButton`），胶囊/徽标用 `DialogTag`/`DialogChip*`（Border），**同名不同 TargetType 一律视为冲突**；③ 改完必须真机开一次对应弹窗——`XamlParseException` 只在模板实例化时才抛，编译期与静态扫描全绿（R65 同族：绿灯是真的，只是没跑到那条路径）。 |
