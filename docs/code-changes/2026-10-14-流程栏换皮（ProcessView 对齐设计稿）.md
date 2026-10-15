# 2026-10-14 · 流程栏换皮（ProcessView 对齐设计稿）

> 范围：`VisionMaster\Views\ProcessView.xaml`（左侧流程栏 = 步骤树 + 底部执行统计）**按一份前端设计稿纯换皮**。
> 用户拍板的边界：**只换左侧流程栏视觉**——逻辑、绑定、命令、code-behind **一行不动**；颜色一律走现有令牌，**零裸十六进制色值**；**画布（Nodify）不动**（web 是拓扑竖链自动布局 `flowLayout.ts`，桌面是自由拖拽画布，布局算法不移植）；不引入 web 深色 token；不加"完成状态点 / 耗时胶囊"（属加数据，不是换皮）。
> 闭环口径：单文件（视图层）+ `StepModel.Category` 一个属性 + `StepFactory` 六处赋值 → 改 → 断言 + 审查 → 本记录。**R16 不适用**（没动生成器）。
> 一处**语义变更点**：搜索行整行删除后「失败自动跳转」开关一度无 UI 可关 → 2026-10-14 用户裁决「恢复一个开关」，见 §四。

---

## 一、需求与边界

把左侧流程栏按前端设计稿重排视觉。**这是换皮，不是重写**：

| 类目 | 处置 |
| --- | --- |
| `x:Name="StepTree"`（`:136`） | **逐字保留**（行为 / 样式挂点） |
| 7 个 `DataType` 模板（叶子 `ActionStep` `:261` + 容器 `ConditionStep` `:465` / `WhileStep` `:516` / `ForStep` `:564` / `ParallelStep` `:615` / `CaseStep` `:668` / `StepCollection` `:718`） | **全部在座**（R23：容器类均为 `HierarchicalDataTemplate` + `ItemsSource="{Binding Children}"`） |
| 右键菜单（树级 `TreeView.ContextMenu` `:897-985`；并行 / Case 分支胶囊菜单 `ParallelBranchMenu` `:169` / `CaseBranchMenu` `:193`，挂点 `:741-749`） | **逐字保留** |
| 缩进引导线（`ModernStepContainerStyle` 子项区左缘 1px，`:88-94`） | **保留** |
| `dd:DragDrop.*` 拖拽绑定（6 项，`:138-143`）/ `ui:TreeViewBehavior.*`（3 项，`:144-146`） | **逐字保留** |
| `ProcessView.xaml.cs` / `ProcessViewModel.cs` | **零改动**（除 §四末"孤儿成员保留"的说明外） |
| 样式字典 / `App.xaml` | **零改动** |

**刻意偏离设计稿（不做）**：画布（Nodify）换皮；web 布局算法（`flowLayout.ts` 竖链自动布局）；web 深色 token；"完成状态点 / 耗时胶囊"（属加数据，非换皮）。

---

## 二、改动清单（文件级）

| # | 文件 | 变化 |
| --- | --- | --- |
| 1 | `VisionMaster\Views\ProcessView.xaml` | **整文件重写，1059 → 1028 行**（现含补回的开关） |
| 2 | `Core\Models\ProcessStep\StepModel.cs` | `:28-33` 新增 `public string Category { get; set; }`（可空，带 XML 注释） |
| 3 | `VisionMaster\Services\StepFactory.cs` | 6 处分支各加 `Category = tool.Category`（非容器 `ActionStep` `:33`、`While` `:41`、`For` `:47`、`Case` `:54`、`Parallel` `:60`、`default → ConditionStep` `:68`） |
| 4 | `FlowCanvasChecks\ProcessTreeTemplateChecks.cs` | 新增 **U6**（`:86-92`）：静态扫描 `ProcessView.xaml` 必须存在 `IsChecked="{Binding AutoJumpToFailure}"` |
| 5 | `FlowCanvasChecks\RenderProbe.cs` | `TreeProbeViewModel` 桩补 `AutoJumpToFailure` 属性（`:678`，默认 `true`） |

> ★ #4 / #5 是**守门工装，非产品代码**。#5 的理由：桩缺该属性则出图里勾选框是**空的**，看图的人会以为"默认关"。
> ★ `ProcessView.xaml.cs`、`ProcessViewModel.cs`、样式字典、`App.xaml`：**均零改动**。
> ★ `StepModel.Category` 是**纯视觉附加字段**（XML 注释原文：仅用于流程栏分类上色；老方案 / 程序创建的步骤可能为 `null` → 上色侧回落中性灰，**不影响任何流程语义**）。

---

## 三、视觉改动逐项

### 1. 整行删掉搜索行（原 `:133-171`）

搜索框、「上一个 / 下一个」按钮、「失败跳转」`CheckBox` 三件随行删除；`StepTree` 的 `Grid.Row` **1 → 0**（根 `Grid` 现在只有两行：`*` 步骤树 / `Auto` 底部统计，`:129-133`）。

### 2. 算子行加分类色图标

`ActionStep` 模板：24×24 圆角方块底（`IconChip`，`CornerRadius="6"`，底 `DialogAccentSoftBrush`，`:294-297`），图标前景按分类取色——

```xml
Foreground="{Binding Category, Converter={StaticResource CategoryAccentBrushConverter}}"
```

（`:304`）。转换器在 `:23` 注册，实现是 `VisionMaster\Helpers\ToolboxCategoryVisuals.cs` 的 `BrushFor`（`:76`；未知 / `null` → `FallbackColor` `:37`，已冻结、按分类缓存）。右上角仍叠断点小红点（`:308-328`，逐字保留）。

### 3. 五类容器组头改成类型 chip（身份色搬进 chip）

| 容器 | chip 文案 | chip 底色 / 前景令牌 |
| --- | --- | --- |
| `ConditionStep`（`:473-496`） | 逻辑路由 | `DialogRowAlternateBrush`（灰底灰字） |
| `WhileStep`（`:522-544`） | While 循环 | `DialogWarningSubtleBrush` / `FlowCanvasWarningBrush` |
| `ForStep`（`:570-592`） | For 循环 | `DialogSuccessSubtleBrush` / `FlowCanvasSuccessBrush` |
| `ParallelStep`（`:624-646`） | 并行分组 | `DialogAccentSoftBrush` / `ScadaAccentBrush`（与画布泳道同源蓝） |
| `CaseStep`（`:676-698`） | 分支匹配 | `DialogAccentLightBrush` / `DialogAccentBrush` |

chip = 图标 + 文字同色；`HeaderName` 仍绑 `StepName`，运行态触发器（`State=Running` → 绿字）逐字保留。

### 4. 分支胶囊：圆点 + `DisplayName` + 「N 算子」pill

`StepCollection` 模板：状态指示灯 `Ellipse`（`StatusDot`，`:760-766`）+ 分支文本 `Text="{Binding DisplayName}"`（`:772-777`，**唯一文案来源**，R24——不叠 `Expression` 文本块）+ `Steps.Count` 的"N 算子"pill（`:786-798`，空分支同槽位切"拖入算子"）。胶囊底 `DialogAccentSoftBrush` / 边 `DialogAccentBorderBrush`（`:727-728`），分支级右键菜单（并行 / Case）逐字保留。

### 5. 底部统计条补回「失败自动跳转」勾选框

见 §四。`Border` 内 `StackPanel` → `Grid`（两列 `*` / `Auto`，`:996-1000`）：左列仍是"规模 / 耗时"两行文字（`StepCountText` / `RunTimeText`，`:1001-1012`），右列是新 `CheckBox`（`:1016-1023`，`FontSize="11"`、右对齐）。

---

## 四、用户裁决：失败自动跳转开关补回（本批唯一语义变更点）

**问题**：搜索行整行删除后，`AutoJumpToFailure`（`VisionMaster\ViewModels\ProcessViewModel.cs:464-468`，**默认 `true`**）在全仓 XAML 里再无任何绑定 → **失败自动跳转永久开启、界面无路可关**。本批纪律是"只换视觉、功能不动"，删掉一个功能开关属越界。

**裁决（2026-10-14，用户拍板）**：「**恢复一个开关**」——即在流程栏底部统计条加回一个「失败自动跳转」`CheckBox`（右对齐）；搜索框与上 / 下一个按钮**不恢复**。

**落点**：`ProcessView.xaml` 底部统计 `Border` 内 `StackPanel` 改 `Grid`（两列），右列新 `CheckBox`：

```xml
<CheckBox
    Grid.Column="1"
    Margin="8,0,0,0"
    VerticalAlignment="Center"
    FontSize="11"
    Content="失败自动跳转"
    IsChecked="{Binding AutoJumpToFailure}"
    ToolTip="运行失败时自动把流程栏选中切到失败的步骤；取消勾选则只报警、不切选中" />
```

（`:1016-1023`）

**VM 零改动**：`AutoJumpToFailure` 属性、`DetectNewFailures`、`RunRuntimeTick` 一行未动（`ProcessViewModel.cs:252` 的 `if (!AutoJumpToFailure) return;` 即既有语义——关闭时扫描照跑、基线照记，只是不切选中）。

**守门 U6**（本批新增，`:86-92`）：删除是**静默的**（界面少个框，构建 / 断言 / 出图全绿），所以只能静态钉住——`ProcessTreeTemplateChecks.cs` 断言 `ProcessView.xaml` 必须存在 `IsChecked="{Binding AutoJumpToFailure}"`（"搜索行下线后这是唯一入口；缺它 = 失败强制切选中且无法关闭"）。

**保留的孤儿 VM 成员**：`SearchText` / `SearchNextCommand` / `SearchPreviousCommand`（`ProcessViewModel.cs:443-454`）——搜索功能 **UI 已下线、命令保留**：它们仍被 VM 级断言调用（`FlowCanvasChecks\Program.cs:2215-2240` 的 G3 搜索定位断言），**非死代码**；同文件 `:2302-2309` 是 `AutoJumpToFailure` 的 G4 开关断言（本次补回的开关同样有 VM 级覆盖）。后人不要误以为漏接线。

---

## 五、关键约束与决策理由

### 1. 分类名从工具箱一路带到流程树，靠 `StepFactory` 六处赋值

`ToolItemModel.Category`（插件 `DisplayAttribute.GroupName`）→ `StepModel.Category`，由 `StepFactory.CreateFromTool` 的 6 个分支逐一携带（含 `ParallelStep` 分支——它在同一初始化器里还写了 `ExecutionMode`）。**不新增任何采集 / 映射**：上色复用工具箱那套 `ToolboxCategoryVisuals`（同一份"13 分类 → 图标 + 专属色"表），插件侧零改动。

### 2. `.vms` 序列化兼容：老方案读进来是 `null`、回落中性灰

序列化走 `Core\SolutionService.cs` 的 Newtonsoft（`NullValueHandling = NullValueHandling.Ignore`，`:27`；`MissingMemberHandling` 默认 `Ignore`）：

- **老 `.vms` 无 `Category`** → 反序列化为 `null` → 上色回落中性灰（`ToolboxCategoryVisuals.BrushFor` → `FallbackColor`）；
- **新 `.vms` 仅非 `null` 时才写该字段**（Ignore 空值）→ 旧程序读新文件安全。

### 3. 画布不动、布局算法不移植

web 设计稿的画布是拓扑竖链自动布局（`flowLayout.ts`），桌面端是自由拖拽画布（Nodify），**布局算法不移植**——本批只动左侧流程栏。同理不做"完成状态点 / 耗时胶囊"：那要新增数据源，超出换皮口径。

### 4. 零裸十六进制色值

`ProcessView.xaml` 全文 `#` 只出现在图标码点（`&#xf1ce;` `:241` / `&#xf00c;` `:246` / `&#xf06a;` `:251` / `&#xf00d;` `:341` / `&#xf017;` `:380`）与格式串 `{}{0:0.#}`（`:395` / `:401` / `:418`）里，**没有一处颜色字面量**；所有新观感元素（chip 底、图标色、pill 底、分隔线）一律走既有令牌。

### 5. R23 / R24 / R25 三条红线未动

- **R23**：7 个 `DataType` 模板齐全（容器均 `HierarchicalDataTemplate` + `ItemsSource` 到位，见 §一表）；
- **R24**：分支胶囊文案只有 `StepCollection.DisplayName` 一个来源（`:772-777`）；
- **R25**：三道闸（视图 Preview 拦 / VM `SelectStep` 清空 / `StepParameterDialog` 白名单）未动——本批只改模板观感，未碰事件与命令链。

---

## 六、验证证据（全部实测）

| 工装 | 命令 | 结果 |
| --- | --- | --- |
| 构建 | `dotnet build VisionMaster.sln -c Debug` | **0 错误**（377 警告，均为既有 CS8604 / MSB3245 System.Resources.Extensions） |
| 常规 gate | `FlowCanvasChecks.exe`（Debug） | **2419 通过 / 0 失败**（含新增 U6） |
| SCADA 断言 | `ScadaChecks.exe`（Debug） | **1710 通过 / 0 失败** |
| 渲染探针 | `FlowCanvasChecks.dll --render _renderout` | 场景 F 出图正常（`process-tree-render-F.png`）；三条差异判据（分支卡片拦 / 箭头放行 / 算子卡片不拦）与胶囊菜单自检**全 True**；底部勾选框渲染为**已勾选**（与真机默认 `true` 一致） |

**断言总数口径**：从上一批的 **2406** 涨到 **2419** = 帮助手册批次新增的 **HM0-HM11 共 12 条** + 本批 **U6 共 1 条**。

**U6 反向验证已做**（"守门断言真能红"的证据）：把 XAML 里的绑定名临时改成 `AutoJumpToFailureXX` → **U6 准确变红（2418 / 1）**，还原后复绿。

**首跑出现 1 条抖动**：`[E23] 并行分支胶囊运行态`（`FlowCanvasChecks\FlowContainerChecks.cs:96-134`）报 `分支1=False 分支2=True`，连跑第二次即 2418/0。**该断言与本次改动无关**（XAML 勾选框不触及引擎）：根因是共享闸门 `SafeGatePlugin.Reached`（`:122`）由**任一条**分支置位、而断言（`:124-126`）要求**两条都**已被引擎打标——确定性窗口依赖线程调度（与上一批 `ScadaChecks` 的焦点抖动同族）。

**红线核查（reviewer 独立复核）**：**R23** 7 个 `DataType` 模板齐全；**R24** 分支胶囊文案只有一个来源；**R25** 三道闸未动；**零裸色值**；三条红线（插件工程在 `Plugins\`、公共契约 DLL 不进 `Modules\`、改插件必重建）本批未触及——本批**未改插件源码**，故不触发"改插件必重建"红线，`Modules\` 未动。

---

## 七、未决问题 / 注意

| # | 事项 | 状态 |
| --- | --- | --- |
| 1 | `UIThemeSmokeTest` 因扫描到第三方 `VM3.0-main\` 长期有 1 条 FAIL | **既有、非本次引入** |
| 2 | Case 模式 `ConditionEditorView.xaml:755` 附近「默认分支」判据行与 Else 灰条在 `Grid.Row=1` 重叠 | **既有、非本次引入**（改动前逐字相同） |
| 3 | 流程栏搜索功能 **UI 已下线**（搜索框 / 上一下一个按钮随搜索行删除），VM 命令保留 | 见 §四"孤儿成员保留"——**不是漏接线** |

---

## 八、纪律对照

| 条目 | 本轮的处置 |
| --- | --- |
| **R19~R22**（GridView 隐式样式） | 不适用（本视图无 `<GridView>`；流程栏走 `TreeView` + 类型键模板） |
| **R23~R26**（流程树模板 / 分支卡片交互） | ✅ 模板面**逐条守住**：7 个 `DataType` 模板齐全、容器全 `HierarchicalDataTemplate` + `ItemsSource`、分支胶囊文案单来源（见 §五-5）；新增 **U6** 补"开关必须有界面入口"的静态钉（见 §四） |
| **R27**（`ICommand` 参数类型按绑定来源选） | 不适用（未新增命令；既有命令的 `Command` / `CommandParameter` 逐字未动） |
| **R30~R32**（资源分册作用域） | 不适用（未新增 / 搬移资源键；引用的全部是既有令牌） |
| **R33~R35**（端口语义 / 文本值解析 / 用户意图判定） | 不适用（纯视觉，不改任何绑定与写入语义） |
| **R36~R38**（离屏烘焙件 / 跳过型断言） | 不适用（不碰 HALCON 渲染） |
| **R28~R29**（CodeGraph） | XAML 层一律 grep / Read，未走 CodeGraph（索引不含 XAML） |
| 红线①②③ | 未新增工程；未碰 `Modules\` 契约程序集；未改插件源码，无插件重建 |
| **R16**（生成器） | 不适用（没动 `Core.PluginGenerators`） |
