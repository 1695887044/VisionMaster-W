# 2026-10-06 方案列表 GridView 被 Fluent 隐式样式顶掉（列头消失 / 行渲染成类型全名）

- 日期：2026-10-06
- 项目：VisionMaster（WPF / .NET 9 + Prism 9）
- 范围：1 新增（`UI\Controls\Themes\Controls\GridView.xaml`）+ 1 并入（`UI\Controls\Themes\Generic.xaml:28`）+ 5 视图挂样式（`VisionMaster\Views\DialogViews\SolutionListView.xaml`、`ScadaVariablePickerView.xaml`、`ScadaUserManagerView.xaml`、`ScadaPagePickerView.xaml`、`VisionMaster\Views\ScadaAlarmHistoryView.xaml`）+ 1 检查工程（`UIThemeSmokeTest\Program.cs`）
- 类型：真机渲染缺陷修复 + 回归断言收口 + 4 个同源缺陷顺带修复
- 口径：**真机** = 自动 UIA 打开方案列表弹窗并抓窗；**探针** = 离线渲染真视图 / 真 VM 的 ListProbe，日志落 `%TEMP%\vm-gridview-evidence\*.log`；**冒烟** = `UIThemeSmokeTest.exe`（含 Fluent 与 `--no-fluent` 两轮）。落盘时对文中 file:line 与所引证据做了静态复核（复核清单见「六」末条）。

---

## 一、现象

2026-10-06 真机，「方案列表」弹窗（视图 `VisionMaster\Views\DialogViews\SolutionListView.xaml`，VM `VisionMaster\ViewModels\DialogViewModels\SolutionListViewModel.cs`）同时坏两处：

- 数据行整片渲染成**类型全名** `VisionMaster.Models.AppSolutionEntry`（该类型定义在 `Core\Models\AppConfigModel.cs:13`，命名空间确为 `VisionMaster.Models`）；
- 四个列头（序号 / 名称 / 注释 / 路径）**整体消失**。

与数据无关：`SolutionItems.Items.Count = 3`，三行都在，只是每行画出来的是 `ToString()` 兜底文本。全程不报错、不留日志——属"沉默故障"。（来源：真机截图；探针可视树 dump 见「五」）

## 二、根因

### 2.1 机制链

1. `VisionMaster\App.xaml:68` 把 .NET 9 的 Fluent 主题 `pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml` 合并进**应用级**资源。
2. 该主题自带两条**隐式样式**（隐式样式按类型作键，落点就是应用级）：
   - `Styles/ListView.xaml`：换掉 `ListView` 模板，模板里**没有** `GridViewHeaderRowPresenter` → **凡 GridView，列头全灭**；
   - `Styles/ListViewItem.xaml`：行模板用**裸 `ContentPresenter`** → 列布局塌成一列，数据对象只能 `ToString()` 兜底 → 屏幕上是一行行类型全名。
3. 应用级隐式样式**优先于框架主题样式**（框架主题里那份是支持 GridView 的），于是"**凡 GridView 必坏**"，与视图自己写没写对无关——这几处视图源码一直是好的。

探针实测（`BEFORE-realview-real-view.log`，Fluent 在场）：`ListView.Style` 命中 Fluent 那条（`OverridesDefaultStyle=True`），模板树里 `GridViewHeaderRowPresenter: present=False`、`GridViewColumnHeader count=0`；3 个行容器各挂 `ContentPresenter 690x15.2 content=AppSolutionEntry`；`TextBlocks(3) = VisionMaster.Models.AppSolutionEntry ×3`。

### 2.2 排除项（都不是原因）

- **不是产物陈旧**：视图侧 `SolutionListView.xaml.cs` 与 `SolutionListViewModel.cs` 自 2026-09-27 22:52 未动，出问题的宿主 exe 构建于 10-06 22:16、晚于源码——重建不会改变结果（也正因如此，"只读 XAML 看不出来"）。
- **不是 UI 库那份隐式 `ListBoxItem` 样式**（`UI\Controls\Themes\Controls\List.xaml:17-50`）：隐式样式按类型**精确作键**，不会命中 `ListViewItem`。探针实测普通 ListBox 吃的是 UI 库那份，`ListViewItem` 吃的是 Fluent 那份（`BEFORE-*` 日志里两者各有自己的 `providers` 行）。

## 三、修法

### 3.1 让路样式（新增）

新增 `UI\Controls\Themes\Controls\GridView.xaml`：`GridListViewStyle`（`TargetType="ListView"`、**不含任何 Setter** 的空样式，定义在 `:43`；文件头 `:4-42` 是长注释，写明为什么是空的、为什么不做成隐式、以及"视图必须就地合并本字典"这条引用约束）。挂在 ListView 上即让 Fluent 的隐式 `ListView` 样式**整条让位**，模板回到框架里带列头的那份。

"一个属性都不设"是**有意的**——不是漏写，"让回框架默认"这个动作本身就是内容。

`UI\Controls\Themes\Generic.xaml:28` 并入该字典（排在 `Controls/List.xaml` 之后）。

### 3.2 五个 GridView 视图挂上（方案列表另加行样式）

| 视图 | 落点 | 说明 |
| --- | --- | --- |
| `DialogViews\SolutionListView.xaml` | `:142` 挂 `Style="{StaticResource GridListViewStyle}"`；新增 `SolutionRowStyle`（`:19-66`，样式元素 `:25`，`ItemContainerStyle` 挂在该文件 `:138`）；`UserControl.Resources` 里就地合并 `GridView.xaml`（`:10-18`） | 行容器：深色行底 `#2A2A31` / 悬停同色 / 选中 `#33334A` + 蓝紫描边，行模板内是 `GridViewRowPresenter`（`:47-52`）+ `Padding="4,6"` |
| `DialogViews\ScadaVariablePickerView.xaml` | `:205`（`UserControl.Resources` 就地合并见 `:30-36`） | 此前一直缺列头，本次一并修好；行/配色未动 |
| `DialogViews\ScadaUserManagerView.xaml` | `:191`（就地合并见 `:29-35`） | 同上（该视图的行样式是本仓既有范式） |
| `DialogViews\ScadaPagePickerView.xaml` | `:217`（就地合并见 `:30-36`） | 同上 |
| `ScadaAlarmHistoryView.xaml` | `:439`（就地合并见 `:38-44`） | 同上 |

### 3.3 两条实测边界（后人别改成别的写法）

- **不能做成隐式样式**：隐式生效的变体实测让**没有 GridView 的普通 ListView 凭空多出空表头**（`FIX-synthetic.log:106` `[noview ListView] present=True ; headers=2`）；420×200 的弹窗列表 **18505/84000 个像素点不同**、bbox `(0,0)-(419,108)`（即 22% 的画面被顶掉）。现行方案下同一对照为 **0 像素差**（本次复核：`fc /b BEFORE-noview-noview.png AFTER-noview-noview.png` → "找不到差异"；像素级重算 diffPixels=0）。
- **不能手写模板**：手写模板时列头元素落在 **x=3**（`FIX-synthetic.log:79` `Index@3`），而框架原装模板是 **x=4**（`AFTER-realview-real-view.log:78` `序号@4`）——列头会整体左移 1px，破坏本仓"列头文字与单元格文字同 x"这条对齐口径（实测两边文字都落在 12/62/212/432）。
- **引用它的视图必须"就地合并"本字典**：`<UserControl.Resources>` → `<ResourceDictionary>` → `<ResourceDictionary.MergedDictionaries>`，Source 用**相对 pack 写法** `/UI;component/Themes/Controls/GridView.xaml`（范式 `MotionBoardSettingsView.xaml:13-19`）。首版只挂 `{StaticResource GridListViewStyle}` 而未自合并，`ScadaChecks` 立刻多红 **4 条**渲染断言（内层异常 `无法找到名为"GridListViewStyle"的资源`，抛在 XAML **解析期**——该断言宿主没有 `Application`，App 级字典取不到；`:9428`/`:9548` 是直接 `new 视图()`）；补齐自合并后这 4 条消失（`_scada_verify3.out`）。`pack://application:,,,/...` 的绝对写法在那类宿主里同样取不到，必须用相对写法。

结论：只能"**视图显式挂 + 用框架原装模板**"。

## 四、回归断言（防复发）

`UIThemeSmokeTest\Program.cs` 新增 `RunGridViewChecks()`（`:1250`，由 `:498` 分发），两块内容：

| 块 | 内容 |
| --- | --- |
| 真渲染断言（6 条） | ① 取到 `GridListViewStyle`；② 列头画出来了（`Index/Name/Path` 三列；Fluent 隐式样式在位时这里是 0 个）；③ 列头由 `GridViewHeaderRowPresenter` 摆放；④ 每行都带 `GridViewRowPresenter`；⑤ 全树没有任何一处渲染出类型全名；⑥ 三列各自真的接到数据。另打印一条**不作断言**的参考行："不挂 `GridListViewStyle` 时列头 = N 个"（若哪天 App.xaml 去掉 Fluent 合并，这里会变 3——那不是失败，只说明根因不在了） |
| 静态扫描 | 递归扫全仓 `*.xaml`（跳过 `\obj\` / `\bin\` / 参考工程），**凡出现 `<GridView` 的文件**必须同时含 `GridListViewStyle` 与 `GridViewRowPresenter`（`:1346-1366`），扫描数量打进断言文案 |

顺带修好 `--no-fluent`：解析 App.xaml 的正则原先是贪婪匹配（一路吃到最后一个 `</ResourceDictionary>`），导致**内联块里夹带的合并字典没被剥掉**，Fluent 仍会从那条路径再合并一次，`--no-fluent` 形同虚设。现在会剥掉并在日志首行打印条数："App.xaml 内联块里夹带的合并字典 3 个已剥掉（否则 --no-fluent 无效）"。

## 五、验证证据

| 项 | 结果 | 口径 |
| --- | --- | --- |
| 真机端到端 | 自动 UIA 打开方案列表并抓窗：四个列头在，行 `1 ｜ 解决方案-202610011221 ｜ D:\C#\VM\解决方案\解决方案-20260829224154.vms`（注释列空） | 真机 `realmachine-windows\win1.png`（本次落盘时回看核对过图面） |
| 真视图 + 真 VM 可视树 | 改前：`present=False`、`GridViewColumnHeader count=0`、3 行 `ContentPresenter 690x15.2 content=AppSolutionEntry`、`TextBlocks(3)= 类型全名 ×3`；改后：`GridViewHeaderRowPresenter present=True vis=Visible`、`count=6`（序号/名称/注释/路径 + 2 个填充列）、每行 `GridViewRowPresenter Columns=4`、`contains a type full name? no` | 探针 `BEFORE-/AFTER-realview-real-view.log` |
| 列头与单元格对齐 | `header text X = 12/62/212/432`，`row1 cell text X = 12/62/212/432` | 探针（同日志） |
| 4 个组态视图 | 改后（Fluent 在场）`present=True`，列头数 VAR 7 / USER 4 / PAGE 6 / ALARM 10；改前同环境实测样本 `BEFORE-scadaVAR` 为 `present=False ; headers=0` | 探针 `AFTER-scadaVAR/USER/PAGE/ALARM`、`BEFORE-scadaVAR` |
| 修后无 Fluent 环境 | 同样正常（列头在、无类型全名） | 探针 `AFTERNF-realview-real-view.log` |
| 反例自检（不该被改动的东西） | 普通 ListBox 与"无 GridView 的 ListView"改动前后**逐字节相同**（本次复核 `fc /b` 两组均"找不到差异"；像素级 diffPixels=0） | 探针 |
| 边界（隐式化） | 隐式变体让普通 ListView 多出空表头：420×200 中 18505 px 不同（本次复核重算：数值一致，bbox `(0,0)-(419,108)`） | 探针 |
| 主题冒烟 | `UIThemeSmokeTest.exe`（含 Fluent）与 `--no-fluent` 两轮**均通过**：6 条 GridView 断言全 PASS + 静态扫描"5 个含 GridView 的 xaml 全部齐全" | 冒烟 `smoke-final-default.log` / `smoke-final-nofluent.log` |

反例自检为什么重要：本次改的是**全局主题字典 + 五个视图**，必须证明"没挂样式的普通列表"一字未动（字节级 0 差异），否则就是拿修 A 换坏 B。

## 六、未决与观察

- **两套检查已补跑**（`Engine\VM.FlowEngine` 与插件 `Plugin.Calibration` 的编译错误此后由并发改动落定，两边均可构建）：`ScadaChecks` **1690 通过 / 2 失败**（`_scada_verify3.out`）——其中 4 条 `XamlParseException` 由首版缺"就地合并"引入，**已修并复验消失**；剩 2 条是 `ImageViewMode` 文档序列化（`往返文本稳定 @121`、`内存与磁盘逐字一致 → 草稿 1 份`），与本次改动无关。`FlowCanvasChecks` **1453 通过 / 6 失败**（`_suite_verify.out`）——5 条与 2026-10-05 基线逐字相同（相机插件注册表 / Yolo 类别过滤 / `Dialog*` 键普查 / 两个候选生成器），1 条 `[S11] 流程运行中 → 409` 为基线之后新增、属 HTTP 收图域，均非本次引入。
- **观察到但未采纳**：Fluent 在场时，UI 主题里那份隐式 `ListBoxItem` / `TreeViewItem` 样式**实际是死代码**（应用级 Fluent 那份胜出；实测 padding 由 `12,8` 变 `12,12`、margin 由 `4,2` 变 `0`；对照 `UI\Controls\Themes\Controls\List.xaml:17-50` 与探针 `providers` 行）。涉及全仓列表观感，**未擅自改**，留待决定。
- **落盘时的静态复核**（本次只写文档、未改代码）：`App.xaml:68`、`Generic.xaml:28`、`GridView.xaml:38`、五个视图的行号、`UIThemeSmokeTest` 的 `RunGridViewChecks`( `:1250` )与静态扫描( `:1346-1366` )全部逐条回读核对；`fc /b` 与像素重算为本次实跑。
- **溯源细节**：`SolutionListView.xaml` 在证据抓取之后还有一次**注释措辞**改动（列头对齐那段注释，10-06 23:57），不改变渲染结果（XAML 注释不参与编译产物）；行样式与样式挂点未再变动。
