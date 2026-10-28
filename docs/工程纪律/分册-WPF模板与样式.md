# 工程纪律分册 · WPF 模板与样式

> 本册是 `AGENTS.md` §6 / §9 / §22 / §33 的**完整判据**（R19-R22 表格视图、R30-R32 资源字典作用域、R68-R70 图标字符按钮、R148 ToggleButton 对齐）；注入层只留指针与「最易踩」。
> 触发条件：写/改任何 `ListView+GridView`、控件模板、资源字典（分册合并）、图标字符按钮、自定义模板里的 `ToggleButton`。
> 起因、异常栈、取证过程见 `2026-10-22-工程纪律全集（原文归档）.md` 的同名章节。
> 读法：接到本域任务先读本册，改完按各条「守门」核对——**不看构建退出码**。

## 6. WPF 表格视图（R19-R22）

根因：`App.xaml` 把 .NET 9 Fluent 并进**应用级**资源，其隐式 `ListView` 样式顶掉框架那份 → **凡 GridView 必坏**。

| # | 判据 |
|---|---|
| R19 | `ListView+GridView` 必须挂 `Style="{StaticResource GridListViewStyle}"`（`UI\Controls\Themes\Controls\GridView.xaml`）。不挂 = 列头消失；**不要**改成隐式样式。 |
| R20 | 视图必须自带 `ItemContainerStyle`，行模板必须是 `GridViewRowPresenter`（`Columns="{TemplateBinding GridView.ColumnCollection}"`），不能用裸 `ContentPresenter`。 |
| R21 | 行容器内边距取 **4**（与列头元素 x4 + Padding 8、`GridViewRowPresenter` 内置 6 同一竖线）。范式 `SolutionListView.xaml`。 |
| R22 | 引用了 `GridListViewStyle` 的视图必须**就地合并** `GridView.xaml`（`/UI;component/...`）；不合并会在无 `Application` 的断言宿主里 XAML 解析期抛异常。 |

守门人：`UIThemeSmokeTest` 静态扫描（出现 `GridView` 就必须挂样式 + 带 `GridViewRowPresenter`）。

## 9. 资源字典作用域（R30-R32）

分册之间**互相看不见**。`BasedOn`/模板内容/`Freezable` 内 → 抛 `XamlParseException`（编译期全绿）；普通 `Setter.Value` → 不抛、值静默不生效。

| # | 判据 |
|---|---|
| R30 | 全局分册里的 `{StaticResource K}`，K 必须能在「本分册 + 本分册 merge 的子树」解析。 |
| R31 | 跨册只有两条出路：能同文件就同文件；共用令牌搬到 `UI\Controls\Themes\Colors.xaml`。**禁止改 `DynamicResource` 绕过，禁止新增分册 merge 兄弟分册的边。** |
| R32 | 守门：`FlowCanvasChecks` 的【资源断链】两条契约（`VariableBindingCheck.RunDictionaryScopeContract`，进常规 gate）；不构建时 `tools\audit_resource_scope.ps1`（两处必须同步）；运行期 `_ResProbe` 出 `RESULT: ALL_OK`。 |

> **"同文件后置键"已不是零实例（2026-10-22 实测）**：元素**自身属性**（`<Window Background="{StaticResource K}">`）在解析期**先于**该元素的 `Resources` 求值，K 即便就定义在同一元素的 Resources 里也查不到 → `XamlParseException: 无法找到名为 K 的资源`，在无 `Application` 的断言宿主里连带崩掉 5 段断言。**修法：把该属性挪到子元素（根 Grid）上**；**不要**改成 `DynamicResource`（R31）。静态扫描的 SILENT 桶仍不覆盖这一格。

## 22. 图标字符按钮（R68-R70）

| # | 判据 |
|---|---|
| R68 | **写了 `Width`/`Height` 的图标字符按钮（✕ × ＋ ↑、FA 私有区码点）必须挂 `Style` 或显式 `Padding`**——否则吃 Fluent 的 `11,5,11,6`（30×30 只剩 6×17，24×24 内容区归零）。不定宽不算违规。守门：`GlyphButtonClipCheck` GB0~GB5（进常规 gate；GB4 把"Padding 写大了"也过账；GB2 反向钉住解析器）。算术前提由 `HelpProbe.RunGlyphClipChecks` 四档钉住。 |
| R69 | **量"文字被切"不许看内层 `TextBlock.ActualWidth`**：需求宽用**不受约束的参照 `TextBlock`**（`Measure(infinite)`）；判据用算式"内容区 = 实得尺寸 − 生效 `Padding` − `BorderThickness`"。`Application.Current.Resources[typeof(Button)]` 读不到 Padding Setter，必须取按钮自己的生效值。 |
| R70 | **标题栏按钮 `Path` 几何不许有零长度线段**（`M x y L x y` 起=终 = 那笔不画）。改 `Shell.xaml` 必须同步 `ShellCopy.xaml`。守门：`RunShellLayoutChecks()` 末尾两条（同一判据跑原件 + 副本）。数据级扫描，与资源级闸正交。 |

## 33. 模板里的 ToggleButton 对齐（R148）

| # | 判据 |
|---|---|
| R148 | **自定义模板里的 `ToggleButton` 必须自己声明 `HorizontalAlignment`（外壳类还要 `VerticalAlignment`）**——应用级 Fluent 把它设成 `Left/Center`，显式模板元素照样吃。三条豁免：显式 `Style=`、自己写了对齐、同时写了 `Width`+`Height`。守门：`ScadaChecks` 的 `[AI]` 静态扫描 + 属性面板探针「**合并 Fluent 再渲染一次**」两条。**凡涉及应用级隐式样式的断言，必须在合并 Fluent 的条件下渲染一次。** |

## 读法

1. 本册四节的共同根因都是**应用级 Fluent 隐式样式**：它顶掉框架默认（§6）、它的 Setter 会漏进显式模板（§33、§22）、它改变资源查找起点（§9）。
2. 凡新增"应用级隐式样式 + 自定义模板"组合的断言，必须**合并 Fluent 后再渲染一次**（R148），无头纯静态扫描会假绿。
3. 改完跑：`UIThemeSmokeTest`（§6/§22 静态段）、`FlowCanvasChecks`（§9 资源断链）、`ScadaChecks`（R148）、`_ResProbe`（运行期资源）。
