# AGENTS.md — 工程纪律（注入层）

> 自动注入每个会话，硬上限 65536 字节；超了静默截断，截断点之后的规则等于不存在（2026-10-22 实测：原文 177,585 → 截到 65,140，§18 之后 115 条从未生效）。
> 只留**判据 + 守门人 + 触发条件**；起因、异常栈、取证过程见 `docs/工程纪律/2026-10-22-工程纪律全集（原文归档）.md`（下称"归档"，各节复盘见归档同名 §N）。
> 新增规则：追加末尾、编号顺延、必须带判据与守门人（无自动闸写"人工评审"）；加完 `(Get-Item AGENTS.md).Length` 必须 **< 56000**。
> **满了就下沉，不压缩既有判据**：低频单域规则搬进 `docs/工程纪律/分册-*.md`，注入层只留"最易踩 + 指针"。**下沉判据：违反会被自动闸红的规则（行内或整节有守门）可下沉——漏看也会被红灯拉回；静默且无闸的必须留在本层。** 规则归属与编号并集由 `tools\audit_rules.ps1` 守门（进 run_gates）。现有分册：`分册-SCADA与图元`（§23/§27/§28/§31/§32）、`分册-通讯与设备`（§34/§35）、`分册-WPF模板与样式`（§6/§9/§22/§33）、`分册-启动与插件加载`（§15/§17/§24/§25）、`分册-帮助手册`（§13/§19）、`分册-停靠与面板`（§18）、`分册-流程栏与画布`（§21/§37）、`分册-属性面板与刷新`（§29）、`分册-HALCON画布交互`（§16）、`分册-AI操作与exe测试踩坑`（§41）。

## 0. 编号冲突（引用时带章节号）

| 编号 | A | B |
|---|---|---|
| R108 | §29 轴面板操作按钮（正文在分册《属性面板与刷新》） | —（§30 范围说明；R109/R110 保留） |
| R139 | §30.4 派生只读属性标 `[RuntimeState]` | §31 树行 `ContentPresenter` 必须 `Stretch` |
| R140 | §30.5 禁 `#RRGGBB` | §31 `ObservableCollection` 不替容器发聚合通知 |
| R141 | §30.5 可复用 UI 下沉 | §31 RenderProbe 豆腐块不是缺陷 |

## 1. 编码与工具（R1-R6）

| # | 判据 |
|---|---|
| R1 | `.ps1` 纯 ASCII（含注释）；中文常量用码点：`[string][char]0x6807`。 |
| R2 | 中文路径/文件名/搜索词放 UTF-8 清单，脚本 `[IO.File]::ReadAllLines($f,[Text.Encoding]::UTF8)`；**不在 `-Command` 里传中文**，用 `-File`。 |
| R3 | 不用 `Get-Content`/`Set-Content` 往返编辑仓库文件（无 BOM UTF-8 静默损坏）；改文件用编辑工具。 |
| R4 | 必须写回时 `[IO.File]::WriteAllText($p,$text,(New-Object Text.UTF8Encoding($false)))`，只用 `String.Replace`；替换串里禁 `\r\n`。 |
| R5 | 写完自检：严格 UTF-8 解码不抛、行数合理、Read 回读中文。 |
| R6 | **改文件前先确认可回退来源**：`git diff` 能取回 / 归档里有原文 / 编辑工具可撤销。**不要依赖旧工具路径**——ZCode 时代的 `%USERPROFILE%\.zcode\cli\artifacts\` 在 DSH 下不存在。 |

## 2. docs/ 结构（R7-R9）

| # | 判据 |
|---|---|
| R7 | 动 `docs/` 结构前先读 `docs/归档/README.md` 与目标目录 `README.md`。 |
| R8 | 未实现插件的方案说明书属现行设计文档，放 `docs/<插件名>/`，**不进归档**。 |
| R9 | 跨进程协调先看 `LastWriteTime`——最近几分钟被改过就先报告确认。 |
| R9A | **`docs/` 结构由闸门管，不靠记忆**：白名单唯一事实源 `tools\docs-layout.json`（`topDirs` 全量登记 + `rootFiles` 只允许 `项目全景说明.md`）。`docs/` 根级新增 `.md` 或新增顶层目录**必须先登记**，否则 `tools\audit_docs.ps1` 判红。日志式记录（`code-changes`/`meetings`/`performance`/`归档`）按 `orphanPolicy` 豁免人工 README。**找文档从 `docs/README.md`（文档中心，含四类分工与目录地图）进**；日志目录的 `INDEX.md` 由 `tools\gen_docs_index.ps1` 生成，**新增日志条目后必须重跑，否则 `run_gates.ps1` 的 `docs_index` 步判红**（判据：重跑后无任何目录被重写）。守门：`run_gates.ps1` 的 `docs_index` + `audit_docs` 两步。 |
| R9B | **双客户端（DSH + ZCode）共用一套约束**：唯一事实源仍是本文件；`tools\sync_constraints.ps1` 把「违反即回滚 / 症状静默 / 验收口径」7 类单向镜像进 `%USERPROFILE%\.zcode\AGENTS.md` 的 `DSH-SYNC` 标记块（块外内容原样保留、幂等、`-Check` 检漂移）。**改规则只改本文件再重跑脚本，不许手改镜像侧**；镜像范围外的章节（UI/HALCON/SCADA 等）ZCode 仍须回读本文件。守门：`run_gates.ps1` 的 `sync_constraints` 步（`-Check`）。 |

## 3. 三条红线（违反即回滚）

| # | 红线 | 后果 |
|---|---|---|
| ① | 插件工程必须放 `Plugins\` 下 | `Directory.Build.targets` 按目录投递到 `Modules\`，放别处加载不到 |
| ② | 契约程序集（`Core.Interfaces`/`Core.Controls` 等）不许拷进 `Modules\` | 两份静态状态，图投进去取不出来 |
| ③ | 改了插件源码必须重建该插件工程 | `Modules\` 不由宿主带出，只重建宿主仍是旧 DLL |

> 红线 ③ 家族：R16/R42/R55/R62/R71（生成器与资源）、R28（索引）、R65（验证工具自身）。判据统一"看产物内容，不看构建退出码"：**DLL mtime ≥ 该工程源码 mtime；`Help` 内嵌资源数 = 磁盘 .md 数**。**自动闸已落地（2026-10-22）**：`tools\audit_artifacts.ps1` 逐插件工程核这两条，`tools\run_gates.ps1` 是唯一收尾入口（见 §39 R182）。

## 4. 派工与协作（R10-R15）

| # | 判据 |
|---|---|
| R10 | **派工是主会话职责**：用户只发需求，主会话判定角色、串并行与依赖；用户不指名角色。 |
| R11 | **按需派工**：单域派单角色，跨域拆解，一句话级改动主会话直接做；交接三段式（改动清单/验证证据/未决问题）。 |
| R12 | **大改动闭环**：公共契约、多模块或流程语义变更 → 架构评审 → 实现 → 独立审查 → 补 `docs/code-changes/` 记录；审查只报告、文档角色只落盘。 |
| R13 | **汇总口径**：主会话整合为一段交付并注明来源角色；**未经构建或审查确认不得说"已验证"**。 |
| R14 | **派工指令自带地图**：路径、行号、已知结论、验收标准一次给全，并说明"哪些文件不许动"。 |
| R15 | **作业纪律**：独立读取/搜索并行批量；读过的文件不重读，小任务不建 todo；构建验证集中最后一两轮；汇报一次性写全；上下文与汇报宁全勿简。 |

> 2026-10-22 环境说明：角色定义分 `.zcode/agents/*.md`（ZCode）与 AgentTeams（DSH）两处；`maxTurns`、"按请求计费"是 ZCode 机制，DSH 下不适用。

## 5. 插件源生成器（R16-R18）

| # | 判据 |
|---|---|
| R16 | **改 `Shard\Core.PluginGenerators` 后必须重建全部插件工程**（生成物在插件 DLL 编译期展开；改钩子/字段规则/诊断、升级 Roslyn 都算改）。 |
| R17 | 生成器工程与产物不得进 `Plugins\`/`Modules\`：`OutputItemType=Analyzer` + `ReferenceOutputAssembly=false`（`Plugins\Directory.Build.props`）。 |
| R18 | `[StepConfig]` 三选一（自动属性 / partial + 钩子 / 手写）；partial 类必须 `partial`，钩子只写实现；违规报 CPG0001~CPG0005。 |

> 速查 `docs\工程纪律\新插件端口速查.md`（§6 动态端口：按配置生成端口必须用 `IDynamicOutputProvider` + `AddDynamicOutput`，并回写 `StepModel.OutputPortDefinitions`；范式 `Plugin.CreateRoi`。**禁止另造变量声明接口**）。

## 6. WPF 表格视图（R19-R22）→ 分册《WPF模板与样式》

**最易踩**：根因是 `App.xaml` 把 .NET 9 Fluent 并进**应用级**资源，隐式 `ListView` 样式顶掉框架那份——**凡 GridView 必坏**；`ListView+GridView` 必须挂 `GridListViewStyle` 并**就地合并** `GridView.xaml`，行模板必须是 `GridViewRowPresenter`、行容器内边距取 **4**。

> 完整判据（R19-R22 与守门）见 `docs/工程纪律/分册-WPF模板与样式.md`。

## 7. 流程栏步骤树（R23-R27）

| # | 判据 |
|---|---|
| R23 | **新增/改名 `StepModel` 子类必须给 `ProcessView.xaml` 补 `DataType="{x:Type models:<类型名>}"` 模板**（逐类型给，别指望基类）；容器类用 `HierarchicalDataTemplate` + `ItemsSource="{Binding Children}"`。守门：`ProcessTreeTemplateChecks` U1-U5（进常规 gate；例外清单 `Break/Continue/ReturnStep` + 反向断言防过期）。 |
| R24 | 分支胶囊文案**只有 `StepCollection.DisplayName` 一个来源**，不挂独立 `Expression`；按父容器换色用 `RelativeSource AncestorType=TreeViewItem, AncestorLevel=2`。 |
| R25 | **分支卡片（`StepCollection`）不进"按选中项执行"的命令链**。三道闸：视图拦左键/双击与非并行分支右键；VM 的 `SelectStep` 收到 `StepCollection` 清空选中；`StepParameterDialog.Open` 只对 `ActionStep`/`ConditionStep`/`ForStep`/`WaitStep`/`ParallelStep` 开窗，其余返回 false 由调用方提示。**例外**：并行分支胶囊右键出自己两项菜单（`CommandParameter` 绑 `PlacementTarget.DataContext`，且要求祖先链有 ContextMenu）。**若让 `SelectStep` 接纳 `StepCollection`，必须同时给 `TreeView_MouseDoubleClick` 补"数据项是 StepModel"守卫。** 守门：V1-V8 + 探针场景 F 三条（分支拦/箭头放行/算子不拦）。 |
| R26 | **并行分组按"结构 vs 参数"分工**：结构动作在流程栏右键走 `ProcessViewModel` 命令族、过运行锁；**分支名（`StepCollection.StepName`）不在 FlowModel 监听面，命令手动 `Version++`；分组名走 setter 自动推，不手动**。参数动作无专属视图：双击组头 → `EasyDialog.ShowPropertyGridSync("并行分组参数", new ParallelGroupEditModel(...))`，确认才 `ApplyToModel()`。守门：V1/V6/V8、W1-W7。 |
| R27 | **`ICommand` 参数类型按绑定来源选**：绑 object 型属性或 `PlacementTarget.DataContext` 这类不检查的表达式 → `DelegateCommand<object?>` + 首行 `is` 判型（拿错类型 = 静默空跑）；`DelegateCommand<T>` 只用于编译期可证的参数。**灰区判据（2026-10-22 补）**：参数来自 XAML `{Binding 强类型属性}` 或 `{x:Static 常量}`、且**除改 XAML 外无法换成别的类型**时，强类型泛型可接受；来源是 object 型属性或 `PlacementTarget.DataContext` 时必须回到 `object?`。守门：V8"非目标类型不抛、静默空跑"。 |

## 8. 影响面查询（R28-R29）

| # | 判据 |
|---|---|
| R28 | **大改动后重建索引**（触发条件同 R12）。**CodeGraph 有无按客户端判**：ZCode 端已挂 → 跑 `codegraph_reindex_workspace`；未挂的会话交付必须写明"影响面为 grep/read 结论，非索引结论"，**不许含糊成"影响面已核"**。 |
| R29 | **影响面查询：先 `grep` 具体类型名/接口名，再 `read` 命中处**；XAML、源生成器、脚本、清单、文档一律 grep/Read。查询词带项目前缀，别用泛词。有 CodeGraph 时再补 `analyze_impact`/`get_callers`。 |

## 9. 资源字典作用域（R30-R32）→ 分册《WPF模板与样式》

**最易踩**：分册之间**互相看不见**——`BasedOn`/模板内容/`Freezable` 内抛 `XamlParseException`（编译期全绿），普通 `Setter.Value` 静默不生效；跨册只有"同文件"或"令牌进 `Colors.xaml`"两条路，**禁止改 `DynamicResource` 绕过**；"同文件后置键"在**元素自身属性**上照样查不到，属性要挪到子元素。

> 完整判据（R30-R32、后置键实例与守门）见 `docs/工程纪律/分册-WPF模板与样式.md`。

## 10. 端口语义（R33-R35）

| # | 判据 |
|---|---|
| R33 | **语义是"值"的端口才配绑定弹窗/🔗；"名字"类（变量名、步骤名、卡地址、端口名）不许挂绑定**——只会落"值由对方提供"的连线，后果是类型不匹配或拿值当名字。名字类用"手填 + 候选下拉"（宿主经 `IPluginConfigContextProvider` 透传，组装在 `VisionMaster\Helpers\PluginVariableOptions.cs`）；老方案已挂过的要在视图提示并在用户改动时 `RemoveLink`。守门：`[A4]`（含"名字行不许接回通用绑定"静态扫描）。 |
| R34 | **手填只产文本**，写入侧必须按声明类型解析一次（`Convert.ChangeType`；范例 `GlobalVariableWriter.TryCoerceText`）。**宽进严出**：能转就转，转不了报错并写清被拒原值；只放宽文本，类型化的值仍报错。数值文本另加：不变区域性、拒绝千分位。 |
| R35 | **用户意图只能由真实输入事件判定，不许从"值变了"倒推**（程序灌值/候选重填/`ApplyConfigValues` 都会触发）。判据接 `PreviewTextInput`/`PreviewKeyDown`/`SelectionChanged` 或控件自报 `NotifyXxxEditedByUser()`。重填候选前先记住端口值、填后顶回（可编辑 ComboBox 有选中项时 `ItemsSource.Clear()` 会清 `Text` 并 TwoWay 推空串）。 |

## 11. 离屏烘焙件只许一份（R36-R39）

| # | 判据 |
|---|---|
| R36 | 全仓 `OpenWindow` 只许在 `Services\Core.Halcon\Helpers\AnnotationBaker.cs`，外加**永久豁免** `Plugins\Plugin.ImageScript\HDevDisplayBackend.cs`（逐算子驱动、`[ThreadStatic]`、`BakePlan` 表达不了）。**豁免清单只许变短。** 用法：`new AnnotationBaker()` + 攒 `BakePlan` + `Bake(底图, plan)`。守门：`ResultDisplayCheck` ④/④b/④c。 |
| R37 | **六条 + 两条状态纪律只在 baker 里实现一遍**：① `OpenWindow` mode 只能 `'buffer'`（否则 #1305）；② 窗口按尺寸常驻复用、`Dispose()` 才关（反复开关 → #9302 死锁）；③ 每轮无条件 `SetPart(win,0,0,h-1,w-1)`（漏了底图缩左上角；复用会串轮污染）；④ 文字坐标只用 `"window"`；⑤ `DispText` 必须显式给 `box`（默认就是开的），`BakeText.Box` 是必填位置参数（⚠ 大写 `Box`），字号走窗口级 `SetFont`；⑥ `DumpWindowImage` 源对象立刻 `Dispose`。外加：整段 `lock` 罩"改状态→画→回读"；`SetLineWidth` 还原 1、`SetDraw` 还原 `"margin"`。绘制顺序固定 `Shapes → Objects → Texts`（改层序改 plan 顺序，不改 baker）。 |
| R38 | **"跳过"型断言必须能被反证**：写 `Check(name, true, "跳过：环境不支持 X")` 前必须回答"谁证明了是环境不支持而不是代码有 bug"，并把反证写进理由。**迁移/重构前先织网**。 |
| R39 | **文字宽度只有 baker 能量**：`BakeText.HAlign`（`Left`/`Center`/`Right`，默认 `Left`）表达贴边，换算在 baker 内做并夹回 `[0,w-1]`。`GetStringExtents` 必须排在 `TrySetFont` 之后；本仓 HALCON **没有** `GetTextExtents`。**垂直分量留给调用方**（块高 = 行数 × 行距）。守门：`AnnotationBakerChecks` B8 + `ResultDisplayCheck` U1~U9。 |

## 12. 枚举上屏（R40）

| # | 判据 |
|---|---|
| R40 | 候选一律 `StepConfigOption.ForEnum(类型, 成员名)`，不许 `new StepConfigOption(Enum.GetNames(...))`。① 显示中文、**值仍是成员名**（拿"圆"当值 = 改显示名打断已配流程）；② 回退成员名，绝不空白；③ HALCON/SDK 字面量不进界面；④ 新增成员必须补 `[Display(Name="中文")]`。守门：`MotionZMotionChecks` 6 条（含"选完写回仍是 `Circle`"与两处生成点共用 `ForEnum` 的扫描）。 |

## 13. 帮助手册打包（R41-R42）→ 分册《帮助手册》

**最易踩**：新增插件类必须同时交手册（`Help\{插件类名}\`、`NN-` 前缀、首行 `# 一级标题`、无 BOM、正文 ≥70 行、禁 TODO）；**计数口径按"带 `[Display(GroupName=…)]` 的类"算（含 4 个设备驱动）**；R42 加完必须验证 DLL 真带了资源（增量构建静默漏打包）。**失败模式全是静默的。**

> 完整判据（R41/R42 与守门 HM0~HM17）见 `docs/工程纪律/分册-帮助手册.md`。

## 14. 停靠布局（R43-R47、R56）

| # | 判据 |
|---|---|
| R43 | **改 `Shell.xaml` 的面板归属/停靠语义必须递增 `LayoutHelper.CurrentLayoutSchemaVersion`**。判据：问"旧 `Layout.xml` 恢复会不会抵消改造"。后果要写进记录（用户自拖布局静默作废）。 |
| R44 | `UIThemeSmokeTest\ShellCopy.xaml` 是 `Shell.xaml` 的结构副本（手工维护、无断言）——改 docking 子树必须同步。 |
| R45 | **`AttachDockWatchers` 两条路径都挂**：`LayoutHelper.LayoutLoaded` 事件 + `Shell.OnContentRendered` 里 `Load()` 之后无条件补挂。先解绑再挂、幂等。 |
| R46 | 守门：`RunShellLayoutChecks()` 9 条（进冒烟基线）；只认"元素类型 + 父子/兄弟"，不写死行号与空白，像素宽度刻意不钉。**冒烟退出码恒为 1（既有红灯未清；旧"GridView 命中 vendored"归因已作废，具体哪组红待实跑定位）——以日志行为准，这是待还技术债。** |
| R47 | **活动栏已整条下线**；组态三面板由"是否处于组态设计"自动收敛，不给手工开关；全局变量/通讯设置在顶部横条。**没有面板落点的命令不许进活动栏。** 改顶栏/活动栏**不升**结构版本。守门：`RunShellLayoutChecks()` 三条（主体 Grid 一列 / 无 `Panel_*` 开关 / 两命令在停靠区之前顶栏）。**改 `Shell.xaml` 前先看 mtime。** |
| R56 | **判"是否处于组态设计"只认文档的 `LayoutDocument.IsSelected`，不许用 `IsActive`**（`IsActive` 是 DockingManager 当前活动内容，点右侧面板就翻转 → 自毁循环）。收敛走 `LayoutHelper.ApplyScadaPanelMode(manager, scadaDesign)`，**先 Show 目标再 Hide 对方**。触发三处：启动 `OnContentRendered`、`LayoutHelper.LayoutLoaded`（走 `OnLayoutLoaded`）、中央文档区切标签（**只认 `Panel_ScadaEditorView` 的 `IsSelected`**）。磁盘 `Layout.xml` 会恢复旧可见性 → 每次必须重收敛（故不升版本）。**已知边界**：「视图」菜单三项只 Show 不 Hide，是刻意的手工覆盖入口。守门：`RunPanelModeChecks()` 5 条。 |

## 15. 启动自检超时（R48-R50）→ 分册《启动与插件加载》

**最易踩**：超时与级别逐项声明（`TimeoutMs`/`TimeoutLevel`），不许写死在 `AppLifetimeService`；**超时判定不许只看 `Task.WhenAny` 赢家**——必须同时判"已取消且没跑完"，否则 `TimeoutLevel` 被架空（探针 300 轮命中 98~138 轮）。

> 完整判据（R48-R50 与守门）见 `docs/工程纪律/分册-启动与插件加载.md`。

## 16. HALCON 画布交互（R51-R55）→ 分册《HALCON画布交互》

**最易踩**：R53 画布交互断言只能用**屏幕外可见窗口**——`buffer` 窗口上只有"点中对象内部"能命中，手柄与拖拽全失效（⚠ 与 §11 R37① 方向相反）；R54 `rectangle2` 的 length1 沿 **column** 轴，记反会得"手柄命中失败"假结论；R55 改 `Services\Core.Halcon` 必须 Debug 与 Release 都重建（红线③家族）。

> 完整判据（R51/R52 与守门）见 `docs/工程纪律/分册-HALCON画布交互.md`。

## 17. 启动关键路径（R57）→ 分册《启动与插件加载》

**最易踩**："全部出口都是 Warning"的自检项不许进启动链，改由 `App.OnInitialized` 后台跑；**四件事同时到位**（链里删注册 / `OnInitialized` 补后台调用 / `OnExit` 里 `Cancel()` / 异常只落一行 Warn），守门数 `RegisterCheck(` 恰好 **4** 次。

> 完整判据（R57 与守门 [E18]）见 `docs/工程纪律/分册-启动与插件加载.md`。

## 18. 停靠面板恢复可见（R58-R59）→ 分册《停靠与面板》

**最易踩**：恢复可见一律走 `LayoutHelper.ShowPanel`/`ShowAnchorable`，别处不许写 `anchorable.IsVisible = true`；`PreviousContainerIndex < 0` 判废、Show 后**再看一次 `IsVisible`**；读隐藏记忆只有一条口径：`LayoutContent` 同名属性走 `GetProperty`，**反射只读，绝不写库私有状态**。

> 完整判据（R58/R59 四条判据与守门）见 `docs/工程纪律/分册-停靠与面板.md`。

## 19. 手册分层与厚度（R60-R63）→ 分册《帮助手册》

**最易踩**：一页手册的身份只有「最后一个点之后」那段文件名（先剥分册目录段再判 `IsDeveloperPage`）；厚度按档位下限判（HM17），**派工单必须抄三个数字**；**R62：新增插件工程必须同时登记 sln**，否则 `Modules\` 里的 DLL 永久停在旧版且无任何报错（红线③家族）。

> 完整判据（R60-R63 与守门）见 `docs/工程纪律/分册-帮助手册.md`。

## 20. 图标资产与验证工具陈旧（R64-R65）

| # | 判据 |
|---|---|
| R64 | **`.ico` 只许 `tools\bake_icons.ps1` 烘焙（STA）**；条目全部 PNG 压缩、256 在宽高字节写 0。两条 WPF 实测：`.otf` 绝对 URI 不是 `FontFamily` 键（要拼 `"<文件 uri>#<家族名>"`，别手拼）；字形几何用 `FormattedText.BuildGeometry` 且**居中墨迹盒**。接线两处：`ApplicationIcon` 与帮助弹窗的 `Icon` Setter（走 `<Resource>`）。**资源名查找不区分大小写**，闸拦不住大小写。守门：`HelpProbe` §3c。 |
| R65 | **改过断言宿主源码，验证命令里不许 `--no-build`；"全部通过"不等于覆盖**。① 加/改 `Check()` 后必须先 build 再 run；② 判据落在**条数 + 明细原文**；③ 新闸必须做变异反证（改坏→红→还原→绿）。**绿灯是真的，只是少了一条。** |

## 21. 流程栏命令靶（R66-R67）→ 分册《流程栏与画布》

**最易踩**：结构变更（删/移）后不许无条件清空命令靶——只在 `ReferenceEquals(CurrentSelectedStepModel, target)` 时清，**清空走公开属性 `SelectStep = null`**；**"树的自动改选会回写 VM"必须在真 `TreeView` 上跑**（纯 VM 直调 = 假绿），五条判据里必须有一条反向。

> 完整判据（R66/R67 与守门）见 `docs/工程纪律/分册-流程栏与画布.md`。

## 22. 图标字符按钮（R68-R70）→ 分册《WPF模板与样式》

**最易踩**：写了 `Width`/`Height` 的图标字符按钮必须挂 `Style` 或显式 `Padding`——否则吃 Fluent 的 `11,5,11,6`，内容区归零；量"文字被切"不许看内层 `TextBlock.ActualWidth`，要用**不受约束的参照 `TextBlock`** + 算式；改 `Shell.xaml` 必须同步 `ShellCopy.xaml`。

> 完整判据（R68-R70 与守门）见 `docs/工程纪律/分册-WPF模板与样式.md`。

## 23. SCADA 图元（R71-R77）→ 分册《SCADA与图元》

**最易踩**：R73 新图元控件类必须静态 `OverrideMetadata(DefaultStyleKeyProperty, …)`——不覆盖就继承基类模板，`GetTemplateChild` 永远 null，**症状极静默**；R72 基类外观属性走 `AppearanceProperties`/`TextProperties`，重复声明报 SPG0006；R71 改 `Core.ScadaGenerators` 后必须重建图元工程。

> 完整判据（R74-R77 与各条守门）见 `docs/工程纪律/分册-SCADA与图元.md`。

## 24. 启动插桩与提速（R78-R80）→ 分册《启动与插件加载》

**最易踩**：启动插桩必须落 `ILogService`，不许走 `IUserNotifier`（弹泡 3 秒就消）；全 Warning 自检项不许站在启动关键路径（先例 R57，挪动前确认懒构建兜底在位）；扫插件程序集必须带名字过滤 `Plugin*.dll`，反射元数据只物化一次。

> 完整判据（R78-R80 与守门）见 `docs/工程纪律/分册-启动与插件加载.md`。

## 25. 插件清单缓存（R81-R83）→ 分册《插件加载与缓存》

**最易踩**：R81 四道指纹（SchemaVersion / 每 DLL 大小+mtime / 无未登记 `Plugin*.dll` / 宿主契约版本）**全满足才命中，任一不满足即全量重扫并重写**，不做部分缓存；R82 缓存跳过实例化的前提是"构造器无副作用"，必须静态扫描钉住。

> 完整判据（R83 与守门）见 `docs/工程纪律/分册-启动与插件加载.md`。

## 26. 枚举显示名上屏路径（R84-R86）

| # | 判据 |
|---|---|
| R84 | **三条上屏路径，补特性前先查清**：① `InputPort<T>` → `LinkableValueEditor` → `ForEnum` 读 **`[Display(Name)]`**；② `FlatPropertyGrid` 的 `EnumGenerator` 读 **`[Description]`**；③ 视图手写 `LabelConverter`（不走特性）。**两者读的不是同一个特性。** |
| R85 | **裸 `Enum.GetValues` 上屏的枚举光补 `[Display]` 不够，还要把视图改走 `ForEnum`**：`ItemsSource` 改 `IReadOnlyList<StepConfigOption>`，ComboBox 加 `DisplayMemberPath`/`SelectedValuePath`/`SelectedValue`。 |
| R86 | **断言工程要读插件枚举特性，必须让插件工程进引用集**；解析不到时必须**红着说清是哪个类型**。 |

## 27. 组态基座画面（R87-R94）→ 分册《SCADA与图元》

**最易踩**：R87 集合订阅必须挂在 `[JsonConstructor]` 里那份字段实例上——只挂 setter = 新建文档永不收通知且不报错；R89 导航口径唯一 `FindNavigablePage`，`FindPage` 只给编辑与持久化；R92 基座图元只读三闸门缺一不可（只拦集合不拦主选中会分叉）。

> 完整判据见分册。

## 28. SCADA 轴绑定（R95-R103）→ 分册《SCADA与图元》

**最易踩**：R95 轴按稳定 `AxisId` 寻址，名字只做展示与旧数据兜底；R99 运动命令只走动作表 → `MotionCommandGate` → `Enqueue`，**下发 ≠ 到位**；R100 系统变量的 `IPort.Value` setter 必须**静默忽略而不是抛**（UI 线程 + App 无全局异常处理 → 抛就退进程）。

> 完整判据见分册。

## 29. `RequireRefresh` = 整表重建（R104-R108）→ 分册《属性面板与刷新》

**最易踩**：`RequireRefresh = true` 的消费动作必须是**整表重建** `UpdatePropertyGrid(preserveSelectedTab: true)`；多态配置不许搭窗口（切换字段 + `[PropertyItem]` + **换新实例**）；互斥字段集必须"切换字段 + 嵌套子对象"；"只在特定操作顺序下坏"的缺陷必须按真实操作顺序各跑一遍——**"我手动试是好的"不构成证据**。

> 完整判据（R104-R108 与守门）见 `docs/工程纪律/分册-属性面板与刷新.md`。

## 30. UI 工作流：Web 基准 → WPF 复刻 → 通用入库（R111-R125）

**R108/R109/R110（范围）**：**适用**＝任何界面新增/改版与 UI 库建设；**R109 不适用**＝业务逻辑、数据层、算法、非可视代码；**R110 基准唯一**＝Web 端（设计稿或已上线 Web）是唯一设计基准，无基准先出设计稿。
**三步不可跳**：① Web 设计（R111 落纸六项：布局/间距/配色令牌/字号字重/五态/交互细节）→ ② WPF 复刻（R112 以"接近设计稿"为唯一目标）→ ③ 通用入库（R113 跨页共用、重复 ≥2 次、基础控件必须沉淀，**禁止页面重复手写**）。

| # | 判据 |
|---|---|
| R114 | 视觉一致：分区/间距/圆角/阴影层级/颜色/字号字重一致；允许差异仅限平台强制项且逐项列明。 |
| R115 | 状态一致：五态齐全、键盘焦点可见；缺失视同未完成。 |
| R116 | 交互一致：动效 150~200ms；加载/空/错误三态有明确视觉。 |
| R117 | 令牌化：颜色/间距/圆角/阴影/字号只引资源令牌；视图禁硬编码数值与 hex。 |
| R118 | 禁止自行发挥：不得以"更适合桌面"改布局/密度/形态/交互路径；必要时走 R125。 |
| R119 | 入库位置：令牌进 `Colors.xaml`；基础控件与样式进 `UI/Controls/Themes` 分册；跨工程组件进 `UI/Controls`。 |
| R120 | 复用优先：先检索 UI 库；差异用样式派生或附加属性，不复制模板。 |
| R121 | 反向清理：同批把页面内重复样式与魔法值迁入 UI 库并删原处副本。 |
| R122 | 验收：逐项核对 + 改前改后截图；每处差异写原因与是否可接受。 |
| R123 | 工程闸门：无硬编码颜色与尺寸；通用样式已入库；`UIThemeSmokeTest` 通过，R30-R32 与 R68 契约不回归。 |
| R124 | 判定以"接近 Web 设计稿"为准，不以"桌面端看起来还行"为准。 |
| R125 | 无法复刻：记录差异点与替代方案 → architect 评审 → 用户确认 → `docs/code-changes/` 留档。 |

### 30.1 交付边界（R126-R132、R132A）

| # | 判据 |
|---|---|
| R126 | **Web 产物是设计媒介不是交付物**：可点击 HTML/截图/原型可评审，但不得进构建、不得部署、不得作为第二客户端维护。交付物只有四类：WPF 代码、令牌映射表、截图证据、差异清单。 |
| R127 | **四步不可跳**：设计基线 → 定稿与令牌映射 → 桌面复刻 → 验收。**没有设计基线不得改桌面端样式。** |
| R128 | **基线存 `docs/UI设计基线/<页面>/`**，README 记版本/确认日期/确认人/覆盖范围；目录不参与构建。基线只读，变更必须升版本并同步映射表与已复刻页面。 |
| R129 | **对应关系判定**：分区/层级/控件形态/文案位置/状态语义/交互路径必须一致；字体度量/滚动条宽度/阴影层数/动效曲线允许差异但写进"已知偏差"。以"同一屏同一状态"截图对照为准。 |
| R130 | **差异必须设计阶段预判**：字体度量、DPI、滚动条占位、flex/grid 与 WPF 布局差、多层阴影、动效曲线、文本截断、表格列宽（GridView 无星号列宽）、焦点态、无障碍。 |
| R131 | **禁止用"桌面端更方便"反向改设计**；变更只能由基线升版发起，不可复刻项按 R125 升级，不得静默降级。 |
| R132 | **验收由用户裁定**：每批附"改前改后截图 + 逐项对照表 + 差异清单"；未关闭或未经接受不得宣称完成。 |
| R132A | **SCADA 运行态 Web 客户端是交付物**（2026-10-21 例外）：可进构建、可部署局域网、可独立维护；仅限"已设计画面的运行态查看/操作"，**不含**浏览器编辑组态。必须"单一运行内核 + N 视图会话"，且默认只读、服务端逐命令鉴权、审计带操作者/会话/IP、运动命令默认禁用。 |

### 30.2 模板高度（R133-R135）

| # | 判据 |
|---|---|
| R133 | **`TabItem`/列表项模板不许用"Grid Auto 行 + 子元素 Margin"表达留白**；高度由 `MinHeight` + 模板根 `Border.Padding` 决定，指示条用 `Border.BorderThickness` 底边。判据：探针量 `ActualHeight ≥ 36` 且文字完整。 |
| R134 | **验 .NET 9 WPF 必须用 `net9.0-windows` 探针工程**，**不许 PowerShell 5.1**（资源字典加载 0 条目、`LoadComponent` 报找不到 `System.Runtime 9.0.0.0` → "样式没生效"是假结论）。探针放临时目录，用完即删。 |
| R135 | **弹窗可聚焦控件必须挂 `FocusVisualStyle="{StaticResource DialogFocusVisual}"`，ToolTip 必须用 `DialogToolTipStyle`**（`UI/Controls/Themes/Dialog/Dialog.Misc.xaml`，显式键控）。初始焦点用 `FocusManager.FocusedElement`，**不要 `Focusable="False"`**。 |

### 30.3 Modbus 与运行期改配置（R136-R138）

| # | 判据 |
|---|---|
| R136 | **运行期改配置必须"权限闸门 + 同一命令出口"，写方案活对象**（随保存落盘、不进撤销栈）：① 权限在按下前判（`AccessPolicy.CanOperate`；按钮 `CanExecute`/行内 `CanEdit`/命令执行前各判一次）；② **示教写"当前轴位置"**（离线明确拒绝）；③ 定位与动作表同一路径（`TryMove` → `MotionMoveAbsolute` → `CheckMove`），软限位/未使能/未回零/忙判据只有一份。**模板纪律**：行 `DataTemplate` 没有 `TemplatedParent`，颜色/可见性/可编辑性一律 `RelativeSource AncestorType=图元类型`；ComboBox 样式不许跨册 `StaticResource`。守门：AX7 点位三条 + AX10 十条。 |
| R137 | **Modbus `Address` 是原始协议地址，不是 `4xxxx` 编号**：① 原始地址写 `"0"`/`"1"`，指定区域用 `x=3;0`/`x=1;0`；② `AddressStartWithZero` 只控制是否减 1，默认 `true`；按 1 基配置必须关掉，否则**静默读写相邻寄存器**；③ 新增 Modbus 驱动必须透传该开关，`ModbusDriverEditModel` 同步 `ToJson`/`From`。守门：`IoChecks` M.6~M.10。 |
| R138 | **配置型数据的运行期展示必须"每拍重读 + 指纹短路"**（点位是 `List<MotionPoint>`，外部改动无通知）：每拍 `TryGetPoints` + 指纹比对，**只有变化才重建行**（否则换掉用户正在输入的框）。**先问"谁还会改它、改完有没有通知"**。守门：AX10 两条（含"没变化不重建"的引用相等断言）。 |

### 30.4 派生只读属性（R139）

| # | 判据 |
|---|---|
| R139 | **主动 `RaisePropertyChanged` 的派生只读属性，只要挂在 `StepModel` 子类上就必须标 `[RuntimeState]`**（`FlowModel.OnStepPropertyChanged` 判"属性名不在名单 → Version++"，**不看 setter**）。典型 `TotalStepCount`、watcher 驱动的 `Count`/`Summary`。不要改成字符串名单。**新增 watcher 先问"我 raise 的会被当成语义变更吗"。** 守门：`FlowModelVersionChecks` E14 八条。 |

### 30.5 颜色令牌与样式下沉（R140-R141）

| # | 判据 |
|---|---|
| R140 | **第一方 XAML 除颜色定义文件外不许 `#RRGGBB`/`#AARRGGBB`**。颜色文件只有**五个**（口径以 `UIThemeSmokeTest/Program.cs:3055-3138` 的豁免表为准，别按记忆数）：`Colors.xaml`（第一层令牌，**新令牌只加这里**）、`Fluent\FluentTokens.xaml`、`PluginConfigColors.xaml`、`Scada.Controls\Themes\ScadaTokens.xaml`、`VisionMaster\Themes\DockThemes\*`。**存量只减不增**：`hardcoded-colors.baseline.txt` 记 `路径\|数量`，新文件必须 0、老文件不许上涨。守门：`RunHardcodedColorChecks()`（进冒烟返回码）。**边界**：① 现无运行时主题切换（`StaticResource` 加载期解析；要即时变需迁 `DynamicResource`，独立批次）；② `Shell.xaml.cs` 换 `DarkTheme.xaml` 是 **AvalonDock 的 `dockManager.Theme`**，不是 WPF 资源字典。 |
| R141 | **可复用 UI 一律抽进 `UI\Controls\Themes\Dialog\` 分册**：同一视觉结构**第二次出现**就下沉。先查 `DialogColumnCard`/`DialogColumnHeader`/`DialogColumnTitle`/`DialogOptionItem`/`DialogTag`/`DialogChip*`/`DialogEmptyState*`/`DialogIconButton`/`DialogInput`/`DialogCheckBox`。① 跨册 `StaticResource` 仍禁止；② 视图只写布局与绑定；③ 不改既有键语义（变体加 `...Accent`/`...Danger`，`BasedOn` 基础键）。守门：人工评审。 |

## 31. 流程栏 v2 视觉（R139/R140/R141 的 B 义 + R142-R146）→ 分册《SCADA与图元》

**最易踩**：R139 树行 `ContentPresenter` 必须 `Stretch`，改 Padding/箭头列宽时 -22 与 19/22 要同步；R145 AvalonDock 三条硬语义（收起用 **1px**、切标签看 `IsSelected`、`ShowAnchorable` 后旧引用失效）；R146 新增库样式键前必须全仓查同名。

> 完整判据见分册。

## 32. SCADA 属性分组（R147）→ 分册《SCADA与图元》

**判据**：组名只能来自 `ScadaPropertyGroups`；**新增分组 = 先登记**（常量 + 进 `Order`），否则 `ElementDescriptor.Validate` 判失败、图元进不了工具箱。

## 33. 模板里的 ToggleButton 对齐（R148）→ 分册《WPF模板与样式》

**最易踩**：自定义模板里的 `ToggleButton` 必须自己声明 `HorizontalAlignment`（外壳类还要 `VerticalAlignment`）——应用级 Fluent 把它设成 Left/Center，显式模板元素照样吃；**凡涉及应用级隐式样式的断言，必须在合并 Fluent 的条件下渲染一次**。

> 完整判据（R148、三条豁免与守门）见 `docs/工程纪律/分册-WPF模板与样式.md`。

## 34. 通讯数组与地址偏移（R149-R155）→ 分册《通讯与设备》

**最易踩**：R149 数组元素个数必须落盘，且 `BuildAddressConfig` 要在 `DataType` **之后**写 `Length`（`DataType` setter 会重置它）；R152「40001 写法」是每变量开关、默认关闭，绝不自动识别 5 位地址；R153 跨度超单请求上限（Modbus 120 寄存器 / S7 110 字节）直接拒绝，不做分块。

> 完整判据（R150/R151/R154/R155 与守门）见 `docs/工程纪律/分册-通讯与设备.md`。

## 35. 设备变量表导入导出（R156-R165）→ 分册《通讯与设备》

**最易踩**：R156 只按变量名匹配，**空白单元格默认值按列语义给**（`TryParseInt/Double` 对空白返回 `true/0`，拿 0 会把 DB 号/元素个数/缩放读错）；R159 `DataGrid.RowHeight` 写 `Auto` 构造期抛异常，`Run.Text` 默认 TwoWay；R161 xlsx 导出先写临时文件再 `File.Move(overwrite: true)`。

> 完整判据见分册。

## 36. 仓库分区（R166-R170）

| # | 判据 |
|---|---|
| R166 | **根目录白名单唯一事实源 `tools\root-layout.json`**：允许根文件 8 个（`VisionMaster.sln`/`AGENTS.md`/`NuGet.config`/`.editorconfig`/`.gitattributes`/`.gitignore`/`.zcodeignore`/`pack.ps1`），根目录见 JSON。**新增顶层条目必须先登记**，否则 `tools\audit_root.ps1` 判红。**产物落点**：日志 `artifacts\logs\<yyyyMMdd>\`、渲染 `artifacts\render\<yyyyMMdd>\`；禁止 `> xxx.log` 落根；一次性脚本放 `tools\oneoff\`，用完删或归档 `_Retired\`。 |
| R167 | **探针路径是任务契约，唯一入口 `tools\probes\probes.json` + `run-probe.ps1`**：`-List` 看登记名、`-Name` 运行、`-BuildOnly` 只构建（执行策略 `Restricted`，必须带 `Bypass`）。**搬迁三步原子**：新址 `-BuildOnly` 通过 → 更新 json 与 README → 才能删旧路径。禁止"先搬后补"；历史 `code-changes` 里的旧路径保留原文。 |
| R168 | **退役件进 `_Retired\<yyyy-MM-dd>-<topic>\` 且必须带 README**（为什么下线/怎么恢复/何时可删），不参与构建、不进 `.sln`。`_Retired` 是根目录**唯一允许的下划线目录**；`_XxxProbe`/`_render*`/`_verify` 不再允许。 |
| R169 | **`packages\` 不许删**：`tools\DeviceSimulator\DeviceSimulator.csproj` 用 HintPath 直引 `..\..\packages\Newtonsoft.Json.13.0.4\...`（非 PackageReference）。删根目录前先 `findstr /s /i "..\..\packages\" *.csproj`，命中必须保留。 |
| R170 | **迁移验收（不看退出码）**：① 根文件恰好 8 个、`audit_root.ps1` 退出 0；② 全部登记探针 `-BuildOnly` 通过；③ `ResProbe` 出 `RESULT: ALL_OK`；④ 解决方案 `--no-incremental` 重建通过；⑤ 断言宿主跑完记条数（无基线要求 0 FAIL 且条数非 0）。**清 bin/obj 后必须重建**（否则 `Modules\` 与 `bin` 是空的，红线③）。 |

## 37. 工具箱拖放与循环控制（R171-R174）→ 分册《流程栏与画布》

**最易踩**：gong 拖拽源必须配 gong 放置端（裸 `Drop` 会让拖影胶囊永久残留，**不许反射写其私有静态字段**）；Break/Continue"只能在循环里"只收在落点（判据 `StepFactory.AllowsLoopControl`），拒绝要写状态栏不许静默空跑；`DataTemplate` 里的 `{StaticResource K}` 必须定义在同一字典该模板**之前**；`Popup.IsOpen` 等 `BindsTwoWayByDefault` 的 DP 绑只读属性必须显式 `Mode=OneWay`。

> 完整判据（R171-R174 与守门）见 `docs/工程纪律/分册-流程栏与画布.md`。

## 38. 验证手段与会话边界（2026-10-22）

| # | 判据 |
|---|---|
| R175 | **验证分层**：断言宿主优先（快、无副作用、不依赖桌面）；只有它证明不了的才用真 `VisionMaster.exe`（真实渲染 / 启动链 / 停靠布局 / HTTP 端到端），且**用前必须重建**并核对 Debug 与 Release。 |
| R176 | **跑真 exe 前**：备份 `Layout.xml`/`DefaultLayout.xml`（启动会覆盖用户布局）；确认 19000/19100/8080 未被占；确认没有第二个实例（`SingleInstanceCheck` 会踢掉后来的）。日志落 `artifacts\logs\<yyyyMMdd>\`。 |
| R177 | **上下文不是记忆，工作区文件才是**：会话会被压缩，早期推理会丢 → **每轮结束必须留落盘物**（`docs/code-changes/` 或 `docs/交接报告-<日期>-<主题>.md`，五段：当前状态 / 已做 / 未做 / 下一步 / 踩过的坑）。没留 = 这轮结论等于没发生。 |
| R178 | **按交付物边界拆会话，不按时间**；**一条构建串行的任务链不许中途换会话**；AgentTeams 团队绑会话，换会话前必须收尾或归档，不能带着团队换。 |

## 39. 构建、产物与收尾验证（R179-R183）

| # | 判据 |
|---|---|
| R179 | **本机 `dotnet build`/`restore` 默认多进程会静默失败**（报"生成失败 / 0 个错误"、退出码 1）。**一律加 `-m:1 -nodeReuse:false`**。判据：`0 个错误` **且退出码 0** 才算构建通过。 |
| R180 | **`File.Replace` 在受限环境一律被拒**（Move/Copy/Delete 正常）。`AppSettingsService.WriteReplacing` 只 `catch (IOException)` → `UnauthorizedAccessException` **带崩断言宿主**（ScadaChecks/FlowCanvasChecks 各崩一次）。替换式写文件必须同时 catch `IOException` + `UnauthorizedAccessException`。 |
| R181 | **AgentTeams 成员可能写不了子目录**（沙箱只在仓库根授权、子目录不继承）→ **构建与跑测归主会话**，成员只做读/编辑/静态分析。成员报 `MSB3491 … is denied` 时**不要重试、不要换路径绕过**，直接交回主会话。 |
| R182 | **收尾验证只有一个入口：`tools\run_gates.ps1`**。它按顺序跑 `audit_root.ps1`（R166/R168 根目录白名单）→ `audit_artifacts.ps1`（红线③产物）→ `audit_rules.ps1`（规则归属与编号并集）→ `vms_verify`（R187 方案布局，构建 `tools\vms-generator` 后跑 `--verify 解决方案`）→ 六个断言宿主**逐个按自己的 csproj 构建再跑**（`UIThemeSmokeTest`/`HelpProbe` **不在 sln**，`dotnet build VisionMaster.sln` 根本不重建它们），最后聚合退出码：任一红即 exit 1。日志 `artifacts\logs\<yyyyMMdd>\{build,gate}-<名>.log`（UTF-8 无 BOM）。**它自带陈旧 exe 检测**：exe 比该宿主源码旧直接判红，不跑旧 exe（R65）。`-SkipBuild` 只用于"刚构建完立刻复跑"，不是日常用法。 |
| R183 | **"没跑"和"跑绿了"必须能分开**。① 捕获子进程输出**不要走管道**：`& $exe 2>&1 \| Out-String` 按控制台代码页（本机 936）解码 .NET 宿主的 UTF-8 → 日志成了"合法 UTF-8 的乱码"，统计正则永不命中，每个宿主回落打印"全部通过"（**没人能验证的汇总行比没有汇总行更坏**）。回读**严格 UTF-8 优先、失败回落 936**（`powershell.exe` 子进程确实吐 936）。② **子进程零输出 = 判红**。`Start-Process -ArgumentList ''` 抛参数绑定异常，而 `$ErrorActionPreference='Continue'` 会把它变成 `Exit=0` —— 2026-10-22 实测整个 gate 报 `8 steps green` 而六个宿主**一个都没跑**。③ **别用 `Start-Process` 跑被测进程**（2026-10-22 又踩两次）：`-Wait` 等的是**整棵进程树**，一个残留后代就把 gate 挂死（实测空转 11 分钟、无子进程、无日志）；去掉 `-Wait` 后 `-PassThru` 的 **`ExitCode` 恒为 `$null`**（`WaitForExit` 后、`Refresh()` 后都还是空），`$null -ne 0` 判真 → 走进失败分支，`[int]$null` → 0 → **印出 exit=0 报全绿**。正解：`[Diagnostics.Process]::Start(ProcessStartInfo)` + 有界 `WaitForExit(ms)` + `StandardOutput.BaseStream.CopyToAsync` 把**原始字节**直接落盘；`Add-Row` 的退出码参数**不许做强类型**，`$null` 一律判红。守门：`run_gates.ps1` 的 `NO OUTPUT` 分支、`exit 124`（超时）、`exit 127`（启动失败）、`exit code unknown` 判红。 |

---

## 40. 输入框模板内缩（R184）

| # | 判据 |
|---|---|
| R184 | **`TextBox`/`PasswordBox`/`NumericBox` 模板里的 `PART_ContentHost` 不许再吃一层"由 `Padding` 换算的 `Margin`"**：控件内部已按 `Padding` 内缩一次，二次内缩把固定高度的小框压扁（`Height=38` 只剩 `viewportH=12` < 行高 16.5 → 中文上下被切）。判据是 `PART_ContentHost.ViewportHeight ≥ ExtentHeight` + 数墨迹行数，**别看截图**（禁用态 `Opacity=0.5` 让抗锯齿顶行变淡，像被切）；数字墨迹矮（9~10px）常被误判成"正常"。存量四处已于 2026-10-23 全部修好（运动板卡 `MbTextBox`、`NumericBox.xaml`、`Controls/Input.xaml` 的 PasswordBox、`Plugin.ImageAcquisition/ImageAcquisitionView.xaml`，见 `docs/code-changes/` 同日两篇）；新增代码不得再引入。守门：**人工评审**（无自动闸）。 |

---

## 41. AI 操作电脑 / 调用 exe 测试的踩坑台账（R185）

| # | 判据 |
|---|---|
| R185 | **凡操作电脑（GUI/进程/键鼠）或调用 exe 做测试：开工前先查、收工前必须补** `docs\工程纪律\分册-AI操作与exe测试踩坑.md`。每条五段：现象 / 复现 / 原因 / 解法 / 规避（带命令参数）；重复问题合并更新，不新增重复条目；状态 ✅ 已解决 / ⚠️ 仍存在 / 🗑️ 已失效。**收工汇报必须写「台账已更新：新增 N 条 / 合并 M 条」**。守门人：人工评审。 |
| R186 | **本项目不注册、不调用 `vm-tools` MCP**（源码留 `tools\vm-mcp\`）；测试走 §39。守门：`register-vm-mcp.mjs` 默认拒绝。 |

---

## 42. AI 生成 `.vms` 方案文件（R187）

| # | 判据 |
|---|---|
| R187 | **生成/改 `.vms` 前先读 `docs\方案设计\AI生成VMS文件指南.md`**（§1.1 文件夹布局、§1.1.1 字段作用域）。判据分两级，`tools\vms-generator --verify <目录>` 是唯一实现：**ERROR**＝`..\` 命中数 0；资产引用（`SourcePath`/`SourceImagePath`/`*Assets[].RelativePath`）必须方案内相对、平铺在 `Image\`/`Assets\Templates\`/`Assets\Models\`、文件存在；`AssetId` 是稳定 Guid 且 `TemplateAssetId`/`ModelAssetId` 能反查。**WARN（已知边界）**＝步骤级 `InputValues.FilePath`/`FolderPath`/`DirectoryTemplate` 的盘符路径——`IExecutionContext` 没有方案根成员，采集插件只在 `ImageAssetId` 非空时走 resolver，**关掉它要改公共契约（R12 大改动）**。守门：`run_gates` 的 `vms_verify` 步（构建 `tools\vms-generator` 后跑 `--verify 解决方案`）；生成期另有 `WriteFolderSolution`/`AssertNoExternalPath`。 |

---

## 43. 变量链接只走绑定弹窗（R188）

| # | 判据 |
|---|---|
| R188 | **端口取值只有一条通道：变量绑定弹窗**（`DataBindView` / `VariableBindingViewModel`）。**画布连线已弃用为取值入口**——它只表达流程顺序与模块级数据依赖（一根线 = 两个步骤有数据依赖），不得新增"画布连线即绑值"或"按端口拆开的画布锚点"。两种弹窗模式分工：① 完整模式（带 `TargetStep`）绑"这一步有哪些端口"；② **单绑定模式**（`LinkPathEvent.InputPortName`）给插件自定义视图做逐行绑定入口——**动态输入端口只能走这条**，因为完整模式的左侧端口表读 `ToolItemModel.InputDefinitions`（`PluginService` 用空配置实例拍的扫描期快照，用户新加的端口一条都不在里面，表现为"当前步骤没有可绑定的输入端口"）。三条判据：① 插件视图里出现"去画布上连线"的引导文案 = 违反（必须给行内绑定入口，实现一律用 `LinkableValueEditor`，见 R189）；② 画布节点渲染 `Inputs[0]`/`Outputs[0]` 之外的端口锚点 = 违反（`FlowCanvasView.xaml` 里这两个下标只许 0）；③ 插件新增动态输入端口却没有绑定入口 = 违反（该端口永远绑不上）。核对命令：`findstr /s /c:"画布上连线" Plugins\*.xaml` 命中必须为 0。守门：**人工评审**（无自动闸；`audit_rules` 只核规则归属与字节上限）。 |

---

## 44. 绑定控件必须用 LinkableValueEditor（R189）

| # | 判据 |
|---|---|
| R189 | **端口取值/连线一律用公共控件 `Core.Controls.LinkableValueEditor`，禁止在插件里自己 `GlobalEventBus.Publish(new LinkPathEvent{...})` 手搓绑定按钮。** 它已经把整条链路做完了：🔗 → 单绑定弹窗 → `StepData.SetLink`；✕ → `RemoveLink`；`LinkAddress` 回显（重开配置自动从 `GetLinkedAddress` 恢复）；按 `Port.DataType` 决定"输入框 / 下拉 / 不可编辑占位"；封闭集合端口（枚举、`IsFunctionalEnum`）自动隐藏 🔗（R33）；空候选态给原因（§26）。自己再写一遍必然漏其中几项，典型是**漏回显**和**漏封闭集合禁绑**。两条用法判据：① 最小用法 = 只给 `Port` + `StepData` 两个依赖属性（`Label` 可空；`AllowGlobals=false` 只给纯数据流插件如 C# 脚本）；② **动态端口两种合法接法，按行对象形态选一种**：(a) 端口对象直接挂在行/单元格对象上 → `Port={Binding Port}`，行对象随重建整体替换（`Plugin.Database` 的 `ParameterRows`、`Plugin.Utility.VariableAssignment` 的 `ValueCellTemplate`）；(b) 端口按名字现取 → `MultiBinding` + 各插件自己的 `InputPortConverter`（端口名 + 宿主插件 + `PortsVersion` 三路），每次重建端口后推一格 `PortsVersion`——端口对象每次重建都是新实例，少了这一路编辑器会一直指着已丢弃的旧端口（`Plugin.DataRecord` / `CSharpScript` / `ImageScript` / `JudgeTable` / `ResultDisplay` / `ResultUpload`）；`Port` 可能为 null 的行要用 `ContentControl` + `DataTemplate` 按需实例化，**不许常驻 + `Visibility`**（会留下 `Port=null` 的空编辑器）。范式：`Plugin.DataRecord`（`ColumnPortEditorTemplate`）、`Plugin.CSharpScript`、`Plugin.ResultUpload`（`FieldPortEditorTemplate`）、`Plugin.JudgeTable`（`RulePortEditorTemplate`，**已于 2026-10-28 从自建「绑定…」按钮迁移到本控件**）。**例外**：整行不是"端口编辑器"语义、控件塞不进去时才允许手写 `LinkPathEvent`（当前**无此类例外**），且必须同时实现回显 + 解绑 + 封闭集合端口禁绑，并在 `docs/code-changes/` 写明理由。守门：**人工评审**（无自动闸）。 |

---

## 45. 端口取值只许两种形态，手输只留给运行时变量（R190）

| # | 判据 |
|---|---|
| R190 | **端口取值入口只许两种形态，不许第三种**：① 值型端口 → `LinkableValueEditor`（固定值输入框 + 🔗 + ✕，见 R189）；② **只有"运行时变量"来源**允许一个纯文本 / 可编辑下拉框手打**变量名**——它按名在运行期解析（本轮局部变量 → 全局/通讯/系统变量），编译期没有可连的端口对象，弹窗也表达不了。除这两者之外**不许任何手输**：不许手打端口名、步骤名、上游输出名来"代替绑定"，也不许自造可编辑框去填卡地址/轴名/枚举（那些是封闭集合，`LinkableValueEditor.IsOptionPort` 已渲染成下拉，空候选给原因——R33/§26）。三条判据：① 插件视图里出现绑定到 `InputPortName` / `PortName` / `SourcePort` / `LinkedAddress` 的 `TextBox` = 违反；② 手输"变量名"的框只许出现在 `Source==Variable` 分支，ToolTip 必须写清"按名解析、不是连线"；③ 封闭集合端口不许自造可编辑框。范式：`Plugin.DataRecord`（变量名列是可编辑 ComboBox，注释写明"C# 脚本 `SetVar` 的名字不在宿主候选快照里"）、`Plugin.Utility.VariableAssignment`（视图头三条契约："值行用 `LinkableValueEditor` / 全局名字只能选 / 运行时名字可编辑"）、`Plugin.ResultUpload`（`VariableName` 是唯一手输列，其余走控件）。**字段名/列名/变量名这类"名字定义"不算取值**（它们是端口名或 JSON 键的来源，按 R33 手填 + 候选）。守门：**人工评审**；核对命令 `findstr /s /n /c:"Binding InputPortName" Plugins\*.xaml`、`findstr /s /n /c:"Binding LinkedAddress" Plugins\*.xaml` 命中项的宿主属性若是 `Text`/`SelectedItem`（可写）即违反；`ToolTip`/`CommandParameter`/`Content` 的只读用途不算。 |---

## 附录：常见症状 → 章节

列头消失/类型全名 → §6；流程栏类型全名/分支点不开 → §7；找不到资源名 → §9；图标字被切 → §22；面板不见/切标签闪退 → §18；下拉英文 → §12/§26；属性面板不刷新/两套参数同现 → §29+skill `vm-property-grid`；改了插件仍旧逻辑 → §5/§13/红线③；启动第一次退出 → §15；拖拽胶囊残留 → §37；界面改了没生效 → §30.2+skill `wpf-ui-debug`；输入框文字上下被切 → §40；操作电脑 / 跑 exe 测试踩坑 → §41；想给端口连值却去画布找端口 → §43；端口取值自己画了输入框/按钮 → §44/§45。

