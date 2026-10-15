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
| R47 | **活动栏的图标必须与它所管面板同侧**：左侧活动栏（`Shell.xaml:775-795`）**只放左侧停靠栏**面板的开关（`Panel_FlowListView` / `Panel_ProcessView`）；右侧活动栏（`:960-990`，主体 Grid 的 `Grid.Column="2"`）**只放右侧停靠栏**面板的开关（`Panel_ToolView` / `Panel_ScadaToolboxView` / `Panel_ScadaLayerView`）；**没有面板落点的命令不许进活动栏**（全局变量 / 通讯设置已上移到顶部横条 `:698-714`，仍是 `SystemCommand` + `SystemAction.GlobalVariables` / `CommSettings`，命令语义一字未改）。**为什么**：活动栏图标是"它所管面板在哪一侧"的**落点指示器**——历史上三个右侧面板（工具箱 / 图元工具箱 / 图层）的开关留在左侧竖条里，点最左边图标、面板从最右边冒出来，指示器失效（更早「工具箱从左侧 pane 搬到右侧常驻停靠栏」那次留下的，`Shell.xaml:928-933` 的注释自己写了这次搬家），且这个错配**编译期全绿、任何单个视图的源码里都看不出来**。**「属性」(`Panel_ScadaPropertyView`) 是"选中图元才有内容"的上下文面板，历来没有活动栏项，不许顺手新增**。**改 `Shell.xaml` 的活动栏 / 顶栏结构必须同步 `UIThemeSmokeTest\ShellCopy.xaml`**（R44：结构副本手工维护、无断言，本轮同步点：顶栏 2 个 `TitleButton` `:665`/`:671`、左条 2 个 `ActivityToggle` `:739`/`:746`、右条 3 个 `ActivityToggle` `:921`/`:929`/`:937`）。**边界（R43 的例外）**：活动栏与顶栏**不在 `LayoutRoot` 里**（它们在主体 Grid 的第 0 / 2 列与第 0 行，`LayoutRoot` 整个包在 `DockingManager` 里）——改它们**不改变任何面板的归属或停靠语义**，所以按 R43 的口径**不需要**递增 `CurrentLayoutSchemaVersion`（磁盘 `Layout.xml` 仍有效、用户自拖布局不作废）；**R43 的触发条件是"面板归属 / 停靠语义变了"，不是"`Shell.xaml` 变了"**（判据：问"旧 `Layout.xml` 恢复出来会不会把改造抵消掉"）。守门人：`RunShellLayoutChecks()` 的 **⑦⑧⑨ 三条文档序断言**（`Program.cs:1335-1347`）——判据用 `doc.Descendants()` 的**文档序索引**与 `DockingManager` 的位置比较（左条开关索引 < 停靠区、右条开关索引 > 停靠区、两个命令索引 < 停靠区），且**只认 `LocalName == "ToggleButton"` / `"Button"`**，所以「视图」菜单里那批同名 `MenuItem`（`:241/250/259/268/322/331/340`）不会误入判据。变异反证已做（把右条「工具箱」的 `CommandParameter` 改成 `Panel_LogView` → ⑧ 准确变红，`Panel_ToolView=-1`）。细节见 `docs\code-changes\2026-10-15-活动栏拆分（左右各一条+全局命令上顶栏）.md`。 |

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
