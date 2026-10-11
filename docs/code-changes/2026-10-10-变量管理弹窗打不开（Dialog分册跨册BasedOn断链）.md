# 2026-10-10 变量管理弹窗打不开（Dialog 分册跨册 BasedOn 断链）

> 上下文：承 2026-09-23 两次同族事故——`DialogShadowColor` 断链（`Freezable` 内 `{StaticResource}`）与
> 模板里的 `Icon` 断链（模板内容），两次都把"三套解析作用域"的认知钉进了 `DialogStyles.xaml` / `Dialog.Misc.xaml`
> 的文件头注释。2026-10-01「弹窗皮肤拆成 `Dialog.*.xaml` 分册」把原本同文件的两支样式拆到两个分册，
> **跨册引用从这一刻成立；2026-10-10 真机一打开「变量管理」弹窗即闪退（第三次同族事故）**。
> 本记录是根因定案、同根因顺带治掉的另外 4 处、以及「静态断言 + 免构建扫描器 + 运行期探针」三层守门的落地。

---

## 1. 需求与决策

### 1.1 现象（用户报的原始异常）

```
System.Windows.Markup.XamlParseException
  Message=在"System.Windows.Markup.StaticResourceHolder"上提供值时引发了异常。行号为"20"，行位置为"4"。
  StackTrace: XamlReader.RewrapException → WpfXamlLoader.Load → XamlReader.LoadBaml → Application.LoadComponent
              → VisionMaster.Views.DialogViews.GlobalVariableView.InitializeComponent()
  内部异常: Exception: 无法找到名为"DialogCombo"的资源。资源名称区分大小写。
```

- 报错对象是 `StaticResourceHolder`（WPF 延迟解析 `{StaticResource}` 时用的壳），崩点在 `InitializeComponent()`——**加载 BAML 的那一刻**，不是某段业务代码；
- 真机表现：**一打开「变量管理」弹窗即闪退**；
- 行号 20 位置 4 指向 `UI\Controls\Themes\Dialog\Dialog.Misc.xaml:20`。

### 1.2 决策

**两条合法出路**（本轮定案，随后固化为 `AGENTS.md` R30/R31）：

1. **能同文件就同文件**——`...InGroup` 这类"基类变体"搬回基类所在分册（本轮 `DialogInputInGroup` / `DialogComboInGroup` 就是这么治的，搬回 `Dialog.Input.xaml`，与 `BasedOn` 基类同文件）；
2. **多分册共用的令牌搬到第一层令牌字典 `UI\Controls\Themes\Colors.xaml`**——每个样式分册都已就地 merge 它（本轮 `DialogIconFont` 与更早的 `AccentBrush` 都是这样治的）。

**禁止**把 `StaticResource` 改成 `DynamicResource` 绕过（把编译期问题推迟成运行期噪音），**也禁止**新增"分册 merge 兄弟分册"的边（会滚出分册依赖网，甚至成环）。

**守门三层（本批新增）**：

1. `FlowCanvasChecks` 的【资源断链】两条契约（`VariableBindingCheck.RunDictionaryScopeContract`，进常规 gate 基线）；
2. 免构建独立扫描器 `tools\audit_resource_scope.ps1`（同一条规则，与断言两处必须同步）；
3. 运行期对照探针 `_ResProbe`（按 App.xaml 顺序真合并字典 + 模板全落地 + 真弹窗 `Measure/Arrange`）。

---

## 2. 根因

### 2.1 分册之间互相看不见

`Dialog.Misc.xaml:20` 的 `DialogComboInGroup` 写：

```xml
<Style x:Key="DialogComboInGroup" BasedOn="{StaticResource DialogCombo}" TargetType="ComboBox">
```

而 `DialogCombo` 就定义在**隔壁分册** `UI\Controls\Themes\Dialog\Dialog.Input.xaml:105`。

规律（2026-09-23 两次同族事故已定案，本轮第三次验证）：

> **写在全局 `ResourceDictionary` 分册里的 `{StaticResource K}`，只在「本分册 + 本分册自己 merge 的子树」里解析；兄弟分册互相看不见**——哪怕它合并顺序在前、哪怕全仓只此一份定义。

落脚点决定后果：

| 落脚点 | 查不到时 | 本次实例 |
| --- | --- | --- |
| `BasedOn` / 模板内容（`DataTemplate` / `ControlTemplate`）/ `Freezable` 内 | **抛 `XamlParseException`**（弹窗直接打不开，编译期全绿） | `Dialog.Misc.xaml:20`（本次崩溃）；同批查出的 `Dialog.Misc.xaml:34` 亦属此类 |
| 普通 `Setter.Value` | **不抛，值静默不生效** | 图标字体拿不到 → 豆腐块（本次 `Dialog.Container.xaml:36,79`、`Dialog.Input.xaml:64` 三处） |

键在**全仓确实存在**，所以编译期全绿、"键有没有定义"的既有扫描（`RunMissingResourceKeyContract`，它只管 `BooleanToVisibilityConverter` 那一类）也放行——这是它三次都能溜到真机的原因。

另有一条旁路：若同名键在**框架主题字典**里也有（如 `ExpandCollapseToggleStyle`），同样不报错，只是**静默落回主题那一支**（箭头换了支，最难发现）。

### 2.2 埋雷时间线

- **2026-10-01**：「弹窗皮肤拆成 `Dialog.*.xaml` 分册」把原本同一个文件 `DialogStyles.xaml` 里的 `DialogComboInGroup` 与 `DialogCombo` 拆到了两个分册——跨册引用从这一刻成立；
- **2026-10-10**：首次被真机点到（本崩溃）。

### 2.3 同根因顺带查出并治掉的另外 4 处（修前定位口径）

| # | 键 | 修前引用点 | 后果类别 |
| --- | --- | --- | --- |
| 1 | `DialogIconFont`（FontFamily 令牌，原定义在 `Dialog.Text.xaml`）被三个分册跨册引用 | `Dialog.Container.xaml:36`、`Dialog.Container.xaml:79`、`Dialog.Input.xaml:64`（均为 `Setter.Value`）；`Dialog.Misc.xaml:34`（模板内） | 前三处**静默**——卡片标题图标/搜索图标退成系统字体；模板那支**会抛** |
| 2 | `AccentBrush` | `LogConsole.xaml:33`（分组表头模板引用；该文件不 merge `Colors.xaml`） | 当前无视图给 LogConsole 设 GroupBy 故未爆；**一启用分组就会抛** |
| 3 | `ExpandCollapseToggleStyle` | `Controls\List.xaml:78`（TreeViewItem 模板引用；定义在 `Controls\Button.xaml`） | 不报错，**静默落回框架主题字典里的同名键**（箭头换了支，最难发现） |
| 4 | `{StaticResource Solid}` | `Extensions\BtnExtension.xaml:3`、`Extensions\BtnExtension.xaml:31` | 该键**全仓不存在**（旧工程遗留名），静默失效；两支图标按钮样式（`IconButon` / `IconToggleButon`，当前无引用）一辈子用系统字体 |

> 注：上表的 `file:line` 是**修前**扫描定位口径；搬动后行号已变（新位置见 §3 清单）。
> 新断言对第 3 类（框架主题同名键）判红是**故意的**：要求把键定义到本分册，不许靠主题字典"接盘"。

---

## 3. 修改文件清单

### 3.1 资源与工具（本批全部已落地）

| 文件 | 变更 |
| --- | --- |
| `UI\Controls\Themes\Colors.xaml` | +`DialogIconFont`（FontFamily，从 `Dialog.Text.xaml` 迁来，现定义于 :186；注释写明"分册互不可见，令牌放第一层，每个样式分册都已就地 merge Colors"） |
| `UI\Controls\Themes\Dialog\Dialog.Text.xaml` | −`DialogIconFont` 定义，留指针注释（:12-:15） |
| `UI\Controls\Themes\Dialog\Dialog.Input.xaml` | +`DialogInputInGroup`（:56）、+`DialogComboInGroup`（:164）——从 Misc 迁来，与各自的 `BasedOn` 基类同文件（`DialogCombo` 定义在同文件 :105） |
| `UI\Controls\Themes\Dialog\Dialog.Misc.xaml` | −上述两支样式；原位置留指针注释（:28-:32）；文件头加"分册作用域纪律"注释块（:7-:18，含"唯一例外：Colors 令牌"与"别改 DynamicResource 绕过"） |
| `UI\Controls\Themes\Controls\List.xaml` | +`ExpandCollapseToggleStyle`（:19；从 Button.xaml 迁来，本文件的 TreeViewItem 模板 :133 在用它） |
| `UI\Controls\Themes\Controls\Button.xaml` | −`ExpandCollapseToggleStyle`，留指针注释（:150） |
| `UI\Controls\Themes\LogConsole.xaml` | +`MergedDictionaries → /UI;component/Themes/Colors.xaml`（:15） |
| `UI\Controls\Extensions\BtnExtension.xaml` | +merge `IconDictionary.xaml`（:9）；`{StaticResource Solid}` → `{StaticResource FA.Solid}`（:12、:40 两处） |
| `FlowCanvasChecks\VariableBindingCheck.cs` | +`RunDictionaryScopeContract()`（:634；两条【资源断链】断言：CRASH 类 / SILENT 类）+ 在 `Run()` 基线里调用（:369，紧跟 `RunMissingResourceKeyContract()`） |
| `tools\audit_resource_scope.ps1` | 新增：同规则的独立扫描器（无构建可用；括号感知、去注释保行号、`{x:Type …}` 豁免、CRASH/SILENT 分桶） |
| `tools\audit_chain_reachability.ps1` | 改为转发壳并写明旧模型错因（见 §5） |
| `AGENTS.md` | +§9（R30-R32）：分册作用域纪律 / 两条合法出路 / 守门人（含两条已知盲区） |
| `_ResProbe\`（新） | 运行期对照探针：`_ResProbe.csproj` + `Program.cs`（按 App.xaml 顺序真合并真实字典 + 复刻 App.xaml 内联资源 + 逐键解析 + 全部模板 `LoadContent` + `Dialog*` 样式套用并读回 FontFamily + 实例化并 `Measure/Arrange` 三个真弹窗 + 分组 LogConsole） |

### 3.2 文档侧同步（本记录落盘时）

| 文件 | 变更 |
| --- | --- |
| `docs\UI样式参考\README.md` | 索引表后加一行注：`DialogIconFont` 已上移第一层令牌字典 `Themes\Colors.xaml`（原在 `Themes\Dialog\Dialog.Text.xaml`），指路本文 |
| `docs\code-changes\2026-09-23-Dialog模板Icon资源崩溃修复.md` | 末尾加一行〔2026-10-10 订正〕：`DialogIconFont` 现定义在 `Colors.xaml:186`（历史记录不改写，只加指路行） |
| `docs\code-changes\2026-10-10-连接状态灯StatusIndicator重写并接线通信设置.md` | §四加一行〔2026-10-10 订正〕：`DialogIconFont` 已上移 `Colors.xaml`，`Dialog.Chip.xaml` 经自身 merge 的 `Colors.xaml`（:10）照常可达 |
| `docs\code-changes\2026-10-09-流程栏并行分组显示为类型全名（流程树模板缺失）.md` | §七加一行〔2026-10-10 订正〕：`ExpandCollapseToggleStyle` 已从 `Themes/Controls/Button.xaml` 挪到 `Themes/Controls/List.xaml` |

---

## 4. 验证结果

### 4.1 静态断言（`FlowCanvasChecks` 门禁）——先红后绿

修前：`通过: 1886  失败: 2`（两条新断言红，其余零回归）：

```
[×失败] 【资源断链】全局字典里没有「跨分册且会抛异常」的引用（BasedOn / 模板内 / Freezable 内）  →  会炸：弹窗打开即抛「无法找到名为 X 的资源」—— List.xaml:78 KEY=ExpandCollapseToggleStyle；LogConsole.xaml:33 KEY=AccentBrush；Dialog.Misc.xaml:7 KEY=DialogInput；Dialog.Misc.xaml:20 KEY=DialogCombo；Dialog.Misc.xaml:34 KEY=DialogIconFont
[×失败] 【资源断链】全局字典里没有「跨分册且静默失效」的引用（Setter.Value → 值不生效）  →  观感 bug：值退回默认（典型：图标字体拿不到 → 图标变豆腐块）—— BtnExtension.xaml:3 KEY=Solid；BtnExtension.xaml:31 KEY=Solid；Dialog.Container.xaml:36 KEY=DialogIconFont；Dialog.Container.xaml:79 KEY=DialogIconFont；Dialog.Input.xaml:64 KEY=DialogIconFont
```

即 CRASH 类 5 条（List.xaml:78 / LogConsole.xaml:33 / Dialog.Misc.xaml:7 / Dialog.Misc.xaml:20 / Dialog.Misc.xaml:34）、
SILENT 类 5 条（BtnExtension.xaml:3,31 / Dialog.Container.xaml:36,79 / Dialog.Input.xaml:64）——与 §2.3 的清单一一对应（行号为修前口径）。

修后：`通过: 1888  失败: 0`，两条断言转绿：

```
[√通过] 【资源断链】全局字典里没有「跨分册且会抛异常」的引用（BasedOn / 模板内 / Freezable 内）  →  已扫 38 个链上字典、546 处 {StaticResource}（豁免：{x:Type …} / DynamicResource / 视图）
[√通过] 【资源断链】全局字典里没有「跨分册且静默失效」的引用（Setter.Value → 值不生效）  →  同上（无静默失效项）
```

最终树（含其它会话并行落地的新断言）：`通过: 1916  失败: 0`。

证据：`_res_check_before.txt` / `_res_check_after.txt` / `_res_check_final.txt`（仓库根；**临时日志，本记录落盘后即删**，关键行已在本节逐字引用）。

### 4.2 运行期探针 `_ResProbe`——与用户异常逐字对上的对照

修前 `RESULT: FAIL count=5`：

- `KEY DialogComboInGroup -> THROW XamlParseException ... 行号 20 位置 4 <-- 无法找到名为 DialogCombo 的资源`——**与用户报的异常逐字一致**；
- `DialogCardTitleIcon` / `DialogSearchIcon` 的 `FontFamily` 落在 `Microsoft YaHei UI`（静默失效的真身）。

修后 `RESULT: ALL_OK`（`_res_probe_after.txt` 逐字）：

```
TEMPLATE sweep          -> 23 templates loaded, 0 failed
STYLE sweep             -> 104 Dialog* styles applied, 0 failed

VIEW 变量管理 GlobalVariableView            -> CREATED+LAID OUT
VIEW 连接管理 CommunicationSettingsView     -> CREATED+LAID OUT
VIEW 相机设置 CameraSettingsView            -> CREATED+LAID OUT

LOGCONSOLE grouped      -> OK (GroupDescriptions=1)

RESULT: ALL_OK
```

- 6 处图标字体（`IconButon` / `IconToggleButon` / `DialogCardTitleIcon` / `DialogSearchIcon` / `DialogEmptyStateIcon` / `DialogColumnTitleIcon`）全部解析到 Font Awesome（例：`STYLE DialogCardTitleIcon -> TextBlock FontFamily=pack://…Font Awesome 6 Pro Solid [icon-font OK]`）；
- 三个真弹窗（变量管理 / 连接管理 / 相机设置）构造并 `Measure/Arrange` 通过；分组 LogConsole 通过（`GroupDescriptions=1`）。

### 4.3 免构建扫描器（本记录落盘前复跑，仓库当前树）

```
chain dictionaries: 38   refs scanned: 546   type-key exempted: 2
RESULT: CLEAN (no cross-book reference)
```

### 4.4 独立复核（reviewer 子智能体，只读）

- 结论 pass with nits；确认 4 处搬动键在新位置可达、无重复键、无假绿假红；
- 三条红线未触碰：未碰插件工程 / `Modules\` 未混入公共契约程序集（落盘复核：`dir Modules\Core.*.dll` 为空）/ 无插件源码改动。

---

## 5. 旧工具为何作废（`tools\audit_chain_reachability.ps1` → 转发壳）

旧模型把"先前已合并的兄弟分册"当成可达，且在累积本文件自身 merge 子树**之前**就检查引用。实测（2026-10-10）：

- 它报 52 条问题**全是误报**（`FluentAliases.xaml` → `FluentChip*` 等，定义就在它自己 merge 的 `FluentControls.xaml` 里）；
- 而本次真断链（`Dialog.Misc.xaml` → `DialogCombo` / `DialogInput` / `DialogIconFont`）它**一条都没报**。

现已改为转发壳（头部写明旧模型错因，指向 `tools\audit_resource_scope.ps1` 与 `FlowCanvasChecks` 的 `RunDictionaryScopeContract`）。

> 教训：扫描器的"作用域模型"本身错了，比不扫描更危险——它用 52 条误报掩盖了 1 条真断链，还把"绿"发给真机。

---

## 6. 已知边界 / 未决

1. **真机点验待做**：修复的"弹窗能打开"由无头探针（构造 + `Measure/Arrange` + 模板全落地）与静态断言证实；**尚未在真实桌面交互式打开「变量管理」**走一遍（新建网络变量 / 扫描组 / 写值）。
2. **新断言的两条盲区（仓内零实例）**：① 同文件"后置键"（`BasedOn` 指向本文件后面才定义的键，WPF 同样抛）不判——按"整文件键并集"处理；② 引用与模板/`Setter.Value` 开标签同行时归类落 SILENT 桶（两桶都要求为空，不影响红绿）。
3. **框架主题字典里的同名键**（如 `ExpandCollapseToggleStyle`）引用**不报错**而是静默落到主题那一支；新断言会判它红，这是故意的（要求把键定义到本分册）。
4. **`Dialog.Text.xaml:23` 的 `DialogFormLabel` 写 `FontSize="4"`（疑似笔误）**：**非本批引入**（编辑快照可证；落盘复核：该行现仍是 `FontSize="4"`），当前被 `FluentAliases.xaml` 的同键转发（`FluentTextCaptionStyle`）盖住所以看不出来；若将来 Fluent 转发层撤掉会变成 4px 小字。仅记账，未改。
5. **批外红项（另立单，不要算到本批）**：① `ScadaChecks` 1690/2——两条都是设置往返断言（`ImageViewMode` 这个新字段），来自另一会话在途的模型改动；② `UIThemeSmokeTest` 1 红——GridView 三件套静态扫描从 `D:\C#\VM` 全盘扫，把 10-09 落库的第三方 `VM3.0-main\...` 视图（`TreeListView.xaml` / `MainWindow.xaml` 等 6 个文件）也扫进来，本仓自己的 5 个视图全部合规。
