# 2026-10-07 日志控制台（LogConsole）控件修复（共享视图过滤 / 分组补应用 / 粘底滚动 / 容量裁剪 / 防抖 / 分组表头配色）

- 日期：2026-10-07
- 项目：UI（WPF / .NET 9）+ VisionMaster（LogView）+ UIThemeSmokeTest
- 范围：1 控件重写（`UI\Controls\CustomControl\LogConsole.cs`，340 行）+ 1 主题（`UI\Controls\Themes\LogConsole.xaml` 分组表头配色）+ 1 视图（`VisionMaster\Views\LogView.xaml` 加 `MaxItems="2000"`）+ 1 检查工程（`UIThemeSmokeTest\Program.cs` 新增 `RunLogConsoleChecks`）；**当日续做**：LogView 过滤工具条 + `LogViewModel` 三个属性（见「七」）
- 类型：代码审查整改（3 处潜伏缺陷 + 2 处性能隐患 + 1 处配色不一致 + 若干健壮性/卫生问题）
- 口径：**构建** = `dotnet build UIThemeSmokeTest\UIThemeSmokeTest.csproj`（连带 UI / VisionMaster）；**冒烟** = `UIThemeSmokeTest.exe`（按 App.xaml 原样含 Fluent 主题）。本次**未**做真机联调（原因见「六」）。

---

## 一、起因（审查发现，不是线上事故）

对 `LogConsole.cs` 做了一次静态审查，共列 12 条问题。其中 3 条是"**一旦真按控件契约使用就静默失灵**"的潜伏缺陷（设 `GroupBy`、设搜索/等级、换数据源），另 2 条是性能隐患，1 条是分组表头配色。

关键背景：`LogView.xaml:9` 目前只绑了 `ItemsSource`，`SearchText` / `FilterLevel` / `GroupBy` / `AutoScroll` **都还没有 UI 消费者**——所以这些缺陷是埋着的，不是现场故障。也正因如此，本次修复的正确性靠检查工程断言（「四」）而不是真机截图来钉。

## 二、逐条问题与根因（改前）

| # | 问题 | 位置（改前） | 后果 |
| --- | --- | --- | --- |
| 1 | 过滤写在**集合的默认视图**上，换源时不解绑 | `.cs:85-93` | 默认视图按集合共享。① 同一集合被第二个控件（或第二个 LogConsole）绑定时，后者的 `Filter` 顶掉前者，前一个控件的搜索静默失效；② 换源/清空后旧集合的视图上仍留着指向本控件的委托 → 旧集合被"幽灵过滤"，本控件也回收不掉 |
| 2 | `GroupBy` 在 `ItemsSource` 之前赋值会被**丢弃** | `.cs:73-82` vs `85-93` | `OnGroupByChanged` 在 `_collectionView == null` 时不做任何事，而 `OnItemsSourceChanged` 只补 Filter、不补分组。XAML 里 `GroupBy="Source"`（字面量）必然先于 `ItemsSource`（等 DataContext）解析 → **分组永远不生效，且无任何提示** |
| 3 | 换源后 `_isUserScrolling` 不复位 | `.cs:22` + `85-93` | 用户在上一份日志里往上翻过 → 换一份新日志（清空重填）后标记仍是 `true` → 自动滚动一直失效，直到用户手动滚到底 |
| 4 | "用户滚动"判定漏判 | `.cs:121-128`（`if (e.ExtentHeightChange == 0)`） | 用户滚轮与日志新增落在同一次布局/滚动事务里时，`ExtentHeightChange != 0` 把用户的滚动吞掉 → 自动滚动保持开启，**把用户正在看的位置拽回底部** |
| 5 | 每条日志排一次 Dispatcher 滚动 | `.cs:137-143` | `OnItemsChanged` 每收一个 Add 就 `InvokeAsync(ScrollToBottom, Background)`。一次爆发 100 条日志 = 100 次滚动+布局，白烧 CPU 且可见卡顿 |
| 6 | 搜索无防抖 | `.cs:66-70` | 每敲一个字全量重算一遍过滤并 Reset 视图；日志集合无上限，几万条起就是每字符几万次谓词调用 |
| 7 | 集合无容量上限 | `VisionMaster\ViewModels\LogViewModel.cs:12` | `SystemLogs` 只增不减（文件通道 `Core\LogService.cs:40` 有 10 万上限，UI 侧没有）→ 7×24 连续跑产线时内存与刷新成本随时长单调增长 |
| 8 | `AutoScroll` 打开时/首屏不立即贴底；`Reset` 完全不触发滚动 | `.cs:133-144` | 只处理 `Add`。`ItemsSource` 被赋一个已填充的集合、批量清空重填（Reset）、模板就绪时集合里已有历史日志——这几种场景自动滚动都不动作；开关 `AutoScroll=true` 也要等下一条日志才见效 |
| 9 | 非 `LogItem` 项被静默过滤掉；`SearchText` 未 `Trim` | `.cs:98`、`:106-108` | 集合里混入别的类型时全部消失（像是丢数据）；搜索词带尾随空格则永远匹配不上 |
| 10 | `OnApplyTemplate` 用匿名 lambda 订阅 `ScrollChanged` | `.cs:121` | 无法退订、`_scrollViewer` 被直接覆盖。当前模板重建会把旧 ScrollViewer 整棵丢弃，所以**暂不漏**，但属易漏写法 |
| 11 | 分组表头是**深色**配色，压在亮色日志列表上 | `LogConsole.xaml:17`（底 `#252526`）、`:34`（字 `#666666`） | 亮底 `#FAFAFA` / 隔行 `#F3F4F6` / 正文 `#333333` 里的黑条；"({0} 条记录)"在 `#252526` 上对比度约 **2.7:1**（WCAG AA 需 4.5:1） |
| 12 | 无用 using、`DispatcherPriority` 全名/using 重复 | `.cs:7-8`、`:12` | 卫生问题 |

## 三、修法

### 3.1 过滤与默认视图（第 1、9 条）

换源前先摘掉旧视图上的过滤委托（`.cs:149-150`，用 `ReferenceEquals(_collectionView.SourceCollection, newValue)` 判定真换源），再挂新视图。

`Filter()` 保留在默认视图上而不是自建私有视图，是**权衡后的选择**：自建视图要 `SetCurrentValue(ItemsSource, view)` 把用户的绑定值改掉（读回来的 `ItemsSource` 不再是原集合、绑定会被反复重设），对一个只有一个消费者的控件不值得。作为补偿，把约束写进类注释（`.cs:16-19`）：**同一个集合不要同时交给第二个带过滤的控件**。`FilterLogic` 对非 `LogItem` 项改为 `return true`（`.cs:170-171`），并先取 DP 现值到局部变量、`Trim()` 后再比（`.cs:174-184`）。

### 3.2 分组补应用（第 2 条）

抽出 `ApplyGrouping()`（`.cs:190-198`），`OnItemsSourceChanged` 里也调一次（`.cs:158`）——`GroupBy` 早于 `ItemsSource` 赋值时照样生效。

### 3.3 粘底滚动（第 4、5、8、10 条）

- **合并排队**：`RequestScrollToBottom()`（`.cs:293-305`）用 `_scrollPending` 把一批日志的滚动请求并成一次，回调里**复核** `AutoScroll && !_isUserScrolling`（排队期间用户可能已经上翻或关掉开关）。
- **判定改为看 `VerticalChange`**（`.cs:269-278`）：`if (e.VerticalChange == 0) return;` —— 只有垂直位置真的动了才算用户操作。内容变高会把 `ScrollableHeight` 撑大但 `VerticalChange == 0`，不再被误判成"用户上翻"；反过来，用户滚动与新增同批到达时也不会被 `ExtentHeightChange != 0` 吞掉。容差常量 `BottomTolerance = 1`（`.cs:36-40`，注释写明单位随 `CanContentScroll` 变）。
- **首屏/换源/开关**都要立即贴底：`OnApplyTemplate` 补一次（`.cs:251-266`）、`OnItemsSourceChanged` 复位状态后补一次（`.cs:160-164`）、`OnAutoScrollChanged` 置 true 时复位并排队（`.cs:108-115`）；`OnItemsChanged` 把 `Reset` 也纳入触发（`.cs:280-291`）。
- 订阅改具名方法 `OnScrollChanged`，重订阅前先 `-=`（`.cs:253-254`）。

### 3.4 容量裁剪（第 7 条）

新增 DP `MaxItems`（`.cs:93-103`，**默认 0 = 不限，行为与旧版一致**）。裁剪逻辑 `TrimToCapacity()`（`.cs:326-339`）裁的是**绑定源集合**（日志只是展示数据，不含业务状态），源不可编辑（只读/固定大小/非 `IList`）时静默跳过。

**必须排到 Dispatcher 上执行**（`.cs:308-323`）：`ObservableCollection` 在派发 `CollectionChanged` 的过程中禁止再次改动自己（`CheckReentrancy` 会抛 `Cannot change ObservableCollection during a CollectionChanged event`），而裁剪正是在那条通知处理链里被请求的。同时用 `_trimPending` 合并成一次。

`LogView.xaml:13` 显式设 `MaxItems="2000"`（注释说明完整历史以文件通道为准）。

### 3.5 防抖与视图忙重试（第 6 条）

- `SearchText` 变化 → `ScheduleFilterRefresh()`（`.cs:202-223`）：200ms `DispatcherTimer`（`DispatcherPriority.Background`），连续输入只在停顿后刷新一次；**等级过滤仍立即生效**（`.cs:126-135`）。
- `RefreshView()`（`.cs:230-247`）捕获 `InvalidOperationException`（视图正在处理集合变更时 WPF 会拒绝 Refresh）并最多重试 3 次，不把异常抛给 UI 线程。

### 3.6 主题与视图（第 11、12 条）

分组表头改成与控件其余部分一致的亮色：底 `#E9EEF5`、边框 `#DCE3EB`、图标 `#64748B`、计数 `#5A6472`（对比度 5.0:1 ✓），标题仍用 `AccentBrush`（`LogConsole.xaml:22-40`，上方注释写明为什么改）。**没有**把这套配色搬进 `Colors.xaml` 令牌：同层的自定义控件主题（`StatusIndicator.xaml`、`AlarmBar.xaml`）都是字面量自包含写法，跟随本层范式、也不引入新的合并顺序依赖。无用 using 一并清掉。

## 四、回归断言（防复发）

`UIThemeSmokeTest\Program.cs` 新增 `RunLogConsoleChecks()`（`:1395`，由 `:499` 与 GridView 检查一起分发，退出码归一为 0/1），共 10 条：

| 块 | 断言 |
| --- | --- |
| 静态扫描 | 分组表头不得回退成 `#252526`（**先剥掉 XML 注释再扫**——首版没剥注释，把"原先这里是一套深色（#252526 底…）"这条说明本身扫成了缺陷，`[FAIL]` 一次即修） |
| 分组 | `GroupBy` 先于 `ItemsSource` 赋值 → `GroupDescriptions == 1` |
| 换源 | 换源前旧视图 `Filter != null` → 换源后 `== null` |
| 过滤 | 等级过滤立即生效（2/3）；关键字在防抖窗口内**不**重算（仍 2/3）、窗口过后命中 1 条 |
| 裁剪 | `MaxItems=3` 加 10 条 → `count=3`、首条 `m7` |
| 滚动 | 拿到模板里的 `PART_ScrollViewer`；首屏贴底 `offset=95/95`；模拟用户上翻后新日志不抢镜 `0/96`；翻回底部后恢复 `97/97` |

滚动那块用 `ScrollToVerticalOffset` 模拟"用户上翻"：它与拖拽/滚轮产生的 `ScrollChanged` 签名一致（`ExtentHeightChange=0`、`VerticalChange≠0`），状态机走同一条路；差别只在触发源。断言文案里如实写明了这一点。

## 五、验证证据

| 项 | 结果 | 口径 |
| --- | --- | --- |
| 构建 | `Build succeeded. 0 Error(s)`；构建日志里 **`LogConsole` 相关告警 0 条**（该方案既有的 nullable 等告警仍在，本次未新增） | `dotnet build UIThemeSmokeTest\UIThemeSmokeTest.csproj -c Debug`（连带 UI / VisionMaster / Plugin.Calibration） |
| 冒烟（含 Fluent） | `EXIT=ZERO`、`[FAIL]` 零条；日志控制台 10 条全 PASS，原有多格布局 6 条 + GridView 7 条**不回归** | `UIThemeSmokeTest.exe`（输出 `%TEMP%\vm_smoke5.log`） |
| 滚动关键输出 | `AutoScroll 默认开启：首屏就贴在底部 … offset=95/95`；`用户上翻后新日志不抢镜 … 新日志到达后 offset=0/96`；`翻回底部后自动滚动恢复 … offset=97/97` | 同上（`95` 是按**项**计的 `ScrollableHeight`，证明逻辑滚动在位） |
| 过滤关键输出 | `GroupDescriptions=1`；`换源前 attached=True，换源后 filter=null`；`等级过滤…命中 2/3`；`关键字…防抖窗口内 2/3，窗口过后 1/3`；`MaxItems…count=3，首条=m7` | 同上 |
| 编码自检 | 四个改动文件：严格 UTF-8 解码通过、行数正常、中文回读正常；`LogConsole.cs` / `LogConsole.xaml` 写回时补回原 BOM（改写工具会丢 BOM），补 BOM 前后**文本逐字符相同**、行数 `340->340` / `188->188` | `[Text.UTF8Encoding]` 严格解码 + `[IO.File]::WriteAllText` 往返（脚本临时件已删） |
| 产物落点 | 未动插件、未动 `Core.PluginGenerators`，`Modules\` 里没有 `UI.dll`（全仓 `Modules` 目录仅在 bin 下）→ 不存在"产物陈旧"风险；宿主输出 `VisionMaster\bin\Debug\net9.0-windows\UI.dll` 已随构建更新 | `dir /s /b UI.dll`、`dir /s /b /ad Modules` |

> 本次最终验证针对**补 BOM 之后**的字节（即最终文件状态）重跑过一遍构建 + 冒烟。

## 六、未决与观察

- **有一处只有静态推理、没有运行时反例**：第 4 条的新旧差异只在"滚轮与新增落在同一次布局/滚动事务"时显现，公开 API 造不出这种单事件（`ExtentHeightChange≠0` 且 `VerticalChange≠0`）——检查工程里钉的是同一套状态机的其它分支（粘底 / 不抢镜 / 恢复），这一点在断言文案里也如实写了。
- **第二个消费者绑定同一集合仍会共用默认视图的 `Filter`**：这是 WPF `ItemsControl` 的固有行为（除非自建私有视图把数据搬进控件内部集合，属重构）。本期只修掉"换源留幽灵过滤"，并把约束写进类注释。
- **`MaxItems="2000"` 是行为变更**：面板里不再显示更早的历史（完整历史在 `Logs\yyyy-MM-dd.log`，`LogService` 保留 30 天）。数值就在 `LogView.xaml:13` 一处，想改/想去掉随时可以。
- ~~这些 DP 目前没有 UI 消费者~~ → **当日已补上，见「七」**：`LogView` 现在有搜索框 / 等级下拉 /「跟随最新」勾选框（3 个 DP 有入口）；`GroupBy` 仍无入口，原因见 7.2。`MaxItems` 的数值在 `LogView.xaml:91` 一处。
- **本次未动 `AGENTS.md`**：没有发生生产事故，纪律条目是否升格由用户决定。两条值得记的 WPF 硬事实已写进控件注释与本文：
  1. `ScrollViewer.CanContentScroll` **不是**可继承附加属性（实测元数据 `_flags = 0x40000003`，无 `Inherits` 位），但模板内的 `ScrollViewer` 会从 **TemplatedParent** 取值——所以 `LogConsole.xaml` 里那句 Setter 是**生效的**，`ScrollableHeight` 以"项"为单位（实测 100 项 → `offset=95/95`）。哪天有人改成 `CanContentScroll=False`，`BottomTolerance`/阈值类常量会从"项"变成"像素"。
  2. `ObservableCollection` 在派发 `CollectionChanged` 的过程中禁止再次改动自己（`CheckReentrancy`）——凡"收到通知后要修集合"的逻辑都必须排到 Dispatcher 上。
- **落盘时的静态复核**（本次只写文档、未再改代码）：文中 `.cs` 行号（`143-165` / `168-187` / `190-198` / `202-247` / `251-278` / `280-339`）、`LogConsole.xaml:22-40`、`LogView.xaml:13`、`Program.cs:1395` 与 `:499` 全部逐条回读核对；冒烟输出为最终字节下实跑。

---

## 七、续做（当日）：LogView 过滤工具条 —— 让这几个依赖属性在真机上走得到

起因：本次修好的过滤 / 开关这几条路，`LogView` 上原本一个 UI 入口都没有（只有 `ItemsSource`），所以改完在真机上照样走不到；「六」里那条"这些 DP 没有 UI 消费者"当日补掉。

### 7.1 加了什么

| 落点 | 内容 |
| --- | --- |
| `VisionMaster\Views\LogView.xaml` | 日志列表上方一条工具条（浅底 `#F1F4F8` + 底边 `#DCE3EB`，与本控件亮色面板 `#FAFAFA` / 分组表头 `#E9EEF5` 同系；写法照 `MonitorView.xaml:50-86` 的"标签 + 透明 TextBox"）：① 搜索框（`:92` 绑 `SearchText="{Binding SearchText}"`，`UpdateSourceTrigger=PropertyChanged`）；② 等级下拉（`:72` `ItemsSource="{Binding LevelOptions}"`、`:73` `SelectedItem="{Binding LevelOption}"`、`DisplayMemberPath="Text"`）；③「跟随最新」勾选框（`:81` `IsChecked="{Binding AutoScroll}"`）。控件侧对应 `AutoScroll` / `FilterLevel` / `SearchText` 三个绑定（`:87-92`），`MaxItems="2000"` 保留（`:91`） |
| `VisionMaster\ViewModels\LogViewModel.cs` | 新增 `SearchText`（`:28`）、`LevelOptions`（`:35-42`，`LogLevelFilterOption` 对象列表：全部/信息/成功/警告/错误）、`LevelOption`（`:45`）与派生只读属性 `FilterLevel`（`:57`）、`AutoScroll`（`:61`，默认 true） |

为什么用"对象 + `DisplayMemberPath`"而不是裸 `LogLevel?` 列表：`LogLevel?` 里的 `null` 在下拉里没有可显示的文字，要显示"全部"就得引一个值转换器；对象列表免转换器，且与 `ScadaAlarmHistoryView` 的 `StateFilters` / `SeverityFilters` 是同一套做法。`FilterLevel` 是派生属性，所以 `LevelOption` 的 setter 里必须 `RaisePropertyChanged(nameof(FilterLevel))` —— 少这一句，下拉能选、控件那边的依赖属性不会动。

### 7.2 为什么没给 `GroupBy` 也加个开关

两条都是"现在加了反而像坏了"：

1. **分组没有排序配合**：控件的 `ApplyGrouping()` 只设 `GroupDescriptions`、不设 `SortDescriptions`，组头按"出现顺序"排 → 日志是交错到达的（信息/错误/信息/错误…），屏幕上就会一组一换头。要配排序 = 把日志面板从"时间序"改成"分组序"，属产品语义变更，不该顺手做。
2. **按来源分组现在没有内容**：`LogItem.Source` **恒为 null** —— `LogService.PublishLog(level, message, source = null)`（`Core\LogService.cs:106`）的 5 个调用点（`:56/61/66/72/77`）都没传 source，而写好备用的 `GetSource()`（`:80`）**从未被调用**（死代码）。所以行模板里那个"来源"胶囊一直是空的，按来源分组也只会得到一个无名分组。

**顺带记一笔（未修）**：`GetSource()` 的 `[CallerMemberName]` 只有在 `Info/Success/Warn/Error` 里调用才有意义（在 `PublishLog` 里调会得到 `LogService.PublishLog`）；真要填上 source，得在这 5 个方法里各调一次再传下去。那会改日志内容与展示（行里的胶囊会冒出 `LogService.Info` 这类文本），属另一件事，留给决定。

### 7.3 新增断言（`RunLogConsoleChecks` 内）

| 断言 | 位置 | 钉什么 |
| --- | --- | --- |
| 接线点到点 | `Program.cs:1521-1549` | `LogView.xaml` 必须出现 `SearchText="{Binding SearchText}"` / `FilterLevel="{Binding FilterLevel}"` / `AutoScroll="{Binding AutoScroll}"` / `ItemsSource="{Binding LevelOptions}"` / `SelectedItem="{Binding LevelOption}"` **共 5 处**（逐字体匹配，不是只查属性名），且这些名字在 `LogViewModel` 上真的存在；外加 `DisplayMemberPath="Text"` 要落在 `LogLevelFilterOption.Text` 上。属性名写错时 XAML 照样编译通过、运行时绑定静默失效——这条就是钉这个 |
| 端到端初值 | `Program.cs:1553-1574` | 真 `LogView` + 真 `LogViewModel`（构造传 `null` 服务 → 不订阅日志，只测接线）：控件 `AutoScroll=True` / `FilterLevel=null` / `SearchText=''`，下拉选中「全部」、勾选框 True |
| 端到端联动 | `Program.cs:1576-1588` | 改 VM（`SearchText="abc"`、`LevelOption=错误`、`AutoScroll=false`）→ 控件三个依赖属性跟着变，且搜索框文本显示 `abc`（证明 TwoWay 方向与派生属性通知链都通） |

构造 `LogView` 会走 Prism 的 `ViewModelLocator` 自动装配（检查工程里没有容器），所以端到端那段包了 try/catch：万一装配炸了，退化成打印一行参考说明、**不算失败**（本次实跑装配没有抛异常）。

### 7.4 验证证据

- **构建**：Debug 与 Release 均 `Build succeeded. 0 Error(s)`；`LogView` / `LogViewModel` / `LogConsole` 相关告警 **0 条**。
- **冒烟** `UIThemeSmokeTest.exe`：`EXIT=ZERO`、无 `[FAIL]`；`=== OK: Shell.xaml 全树模板实例化无异常 (Fluent=True) ===` —— `Shell.xaml:878` 就挂着 `<views:LogView />`，所以新工具条随"全树模板实例化"一起被验到（渲染层没炸）；日志控制台 **13 条断言全 PASS**，新增三条的实际输出：
  ```
  [PASS] LogView 的三处过滤/开关接线 + 等级下拉都点到点  5 处绑定 + DisplayMemberPath 全部对得上
  [PASS] 端到端：真视图 + 真 VM，控件的 AutoScroll/FilterLevel/SearchText 与下拉/勾选初值都对  AutoScroll=True FilterLevel=null SearchText='' 下拉选中=LogLevelFilterOption 勾选=True
  [PASS] 端到端：改 VM 后控件三个依赖属性跟着变，搜索框也显示出新词  SearchText='abc' FilterLevel=Error AutoScroll=False 搜索框文本=abc
  ```
- **编码自检**：`LogView.xaml`（保留 BOM，95 行）/ `LogViewModel.cs`（85 行）/ `Program.cs`（1812 行）严格 UTF-8 解码通过、中文回读正常。
- **产物**：宿主 Debug 与 Release（`VisionMaster\bin\Release\net9.0-windows\VisionMaster.dll`，10:35）都已重建。

### 7.5 仍未验的

- **真机观感**：工具条在真机上是否与周围 Dock 面板协调（浅色条嵌在可能深色的外壳里），只有截图能定；本次只有无窗口渲染断言。
- **交互手感**：等级下拉的宽度（`MinWidth=86`）与文案（全部/信息/成功/警告/错误）是照 `LogLevel` 枚举顺序写的，没做 i18n；
- **分组那条路**：`GroupBy` 至今仍然只在检查工程里被验过（`GroupDescriptions==1`），没有界面入口，也就没有真机使用路径（原因见 7.2）。
