# 2026-09-23 DialogShadowColor 资源解析崩溃修复

> 上下文：承「弹窗 UI 青蓝风统一」。那次把三张弹窗的皮肤抽到全局字典
> [DialogStyles.xaml](file:///e:/VM/VisionMaster-W-master/UI/Controls/Themes/DialogStyles.xaml)，
> 编译全绿，但**实机一打开「变量管理」就闪退**。本记录是这条崩溃的根因定案与修复。

---

## 1. 需求与决策

### 1.1 现象

打开弹窗即崩，异常是**运行期**的、编译期毫无征兆：

```
System.Windows.Markup.XamlParseException
  Message: 在"System.Windows.Markup.StaticResourceHolder"上提供值时引发了异常。行号"54"，行位置"21"
  StackTrace: XamlReader.RewrapException
              → WpfXamlLoader.Load
              → XamlReader.LoadBaml
              → Application.LoadComponent
              → VisionMaster.Views.DialogViews.GlobalVariableView.InitializeComponent()
  内部异常: Exception: 无法找到名为"DialogShadowColor"的资源。资源名称区分大小写。
```

关键信息有两条：

1. 报错对象是 `StaticResourceHolder` —— 这是 WPF **延迟解析** `{StaticResource}` 时用的壳；
2. 崩溃点在 `GlobalVariableView.InitializeComponent()`，也就是**加载 BAML 的那一刻**，
   而不是某段业务代码。

### 1.2 根因：Freezable 里的 `{StaticResource}` 用的是「声明它的字典」的作用域

同一个 `DialogCardShadow` 样式里，两行紧挨着的 `{StaticResource}` 命运完全不同：

```xml
<Style x:Key="DialogCardShadow" TargetType="Border">
    <Setter Property="Background" Value="{StaticResource DialogSurfaceBrush}" />   <!-- 一直正常 -->
    <Setter Property="CornerRadius" Value="12" />
    <Setter Property="Effect">
        <Setter.Value>
            <DropShadowEffect
                BlurRadius="18" Direction="270" Opacity="0.10" ShadowDepth="3"
                Color="{StaticResource DialogShadowColor}" />                        <!-- 就是这里炸 -->
        </Setter.Value>
    </Setter>
</Style>
```

差别只有一处：**`DialogShadowColor` 被包在 `DropShadowEffect`（一个 `Freezable`）内部**。

两套作用域的分工：

| 写法 | 求值时机 | 查找作用域 | 查不到时 |
| --- | --- | --- | --- |
| `Setter.Value` 上的 `{StaticResource}` | 延迟到「套用到元素那一刻」 | 元素作用域链：元素 → 父级 → **App 资源** | **静默跳过**（不抛异常） |
| `Freezable` 内部的 `{StaticResource}` | 加载该字典时就构造 Freezable 并立即赋值 | **声明它的那个字典**（本文件自己）+ 它自己的 MergedDictionaries | **抛 `XamlParseException`** |

而本文件 [DialogStyles.xaml](file:///e:/VM/VisionMaster-W-master/UI/Controls/Themes/DialogStyles.xaml)
是一个**独立字典**，与 [Colors.xaml](file:///e:/VM/VisionMaster-W-master/UI/Controls/Themes/Colors.xaml)
在 [Generic.xaml](file:///e:/VM/VisionMaster-W-master/UI/Controls/Themes/Generic.xaml#L5-L17) 里只是**兄弟**关系：

```
App.xaml
 └─ MergedDictionaries
     └─ /UI;component/Themes/Generic.xaml
         └─ MergedDictionaries
             ├─ Colors.xaml          ← 令牌在这
             ├─ IconDictionary.xaml
             ├─ …
             └─ DialogStyles.xaml    ← 引用令牌的人在这（兄弟，互相看不见）
```

**兄弟之间不可见** ⇒ `DropShadowEffect` 在构造时去「本文件」找 `DialogShadowColor`，
找不到 ⇒ 抛异常 ⇒ 整个弹窗 BAML 加载失败 ⇒ 闪退。

### 1.3 为什么全仓只有这一处会炸

排查了仓库里所有 `DropShadowEffect`，它们的 `Color` **一律是字面量**：

| 文件 | 行 | 写法 |
| --- | --- | --- |
| [AlarmBar.xaml](file:///e:/VM/VisionMaster-W-master/UI/Controls/Themes/AlarmBar.xaml#L81-L86) | 81-86 | `Color="Black"` |
| [PropertyGrid.xaml](file:///e:/VM/VisionMaster-W-master/UI/Controls/Themes/PropertyGrid.xaml#L316-L321) | 316-321 | `Color="#000000"` |
| [Popover.xaml](file:///e:/VM/VisionMaster-W-master/UI/Controls/Themes/Popover.xaml#L30-L35) | 30-35 | `Color="Black"` |
| [PropergridStyle.xaml](file:///e:/VM/VisionMaster-W-master/UI/Controls/Themes/PropergridStyle.xaml#L24-L28) | 24-28 | 不写 `Color`（用默认） |

也就是说：**「在 `Freezable` 里用 `{StaticResource}` 取色」这个用法在本仓是首次出现，
第一次就踩了坑**。这也解释了为什么编译能全绿 —— XAML 编译期不校验这类跨字典引用。

### 1.4 决策：样式字典自带 merge 令牌字典

**决策：给 `DialogStyles.xaml` 加自己的 `MergedDictionaries` 指向 `Colors.xaml`。**

理由：

- **一次治两处** —— 本文件里 `DropShadowEffect` 内的取色共 2 处
  （`DialogCardShadow` 的卡片投影、`DialogCombo` 的 ControlTemplate 下拉投影），一处修复两处生效；
- **结构性** —— 把「样式字典应当声明自己的令牌依赖」变成显式契约，
  以后任何人往本文件里加带 `Freezable` 的样式都不会再踩；
- **不用改色值** —— 相比把 `DialogShadowColor` 换成字面量，本方案不丢设计令牌的可维护性；
- **不用改 `DynamicResource`** —— `DropShadowEffect.Color` 是可冻结的 `Freezable` 属性，
  换 `DynamicResource` 会破坏冻结、带来额外的运行期开销，且语义上并不需要动态换色。

代价：`Colors.xaml` 被加载两份（一份挂 App 链、一份挂本文件下）。
令牌值相同、无运行期改写，**视觉零差异**，内存开销可忽略。

---

## 2. 修改文件清单

| 文件 | 变更 | 说明 |
| --- | --- | --- |
| [DialogStyles.xaml](file:///e:/VM/VisionMaster-W-master/UI/Controls/Themes/DialogStyles.xaml#L50-L53) | +4 行（含注释共 +50 行） | 根节点新增 `<ResourceDictionary.MergedDictionaries>` → `/UI;component/Themes/Colors.xaml` |
| [DialogStyles.xaml](file:///e:/VM/VisionMaster-W-master/UI/Controls/Themes/DialogStyles.xaml#L30-L47) | +18 行注释 | 固化「两套作用域」的认知与本次踩坑结论，避免后人再犯 |

**源码串必须与 `Generic.xaml` 逐字一致**（都用 `/UI;component/Themes/Colors.xaml`），
否则会被 WPF 当成两个不同 URI 各加载一份。

**未改动**：`Colors.xaml`（令牌定义本就正确）、`Generic.xaml`（兄弟结构本身没问题，
问题在引用方没声明依赖）、`App.xaml`、三张弹窗视图的 XAML。

> `DialogStyles.xaml` 在 git 中为 **untracked（`??`）**，属前序「弹窗 UI 青蓝风统一」任务新建、尚未提交。

---

## 3. 验证结果

### 3.1 编译

| 项 | 命令 | 结果 |
| --- | --- | --- |
| UI 工程 | `MSBuild.exe UI\Controls\UI.csproj` | **`UI_EXIT=0`** |
| 主工程 | `MSBuild.exe VisionMaster\VisionMaster.csproj` | **`VM_EXIT=0`** |
| CommChecks | `MSBuild.exe CommChecks\CommChecks.csproj` | **`COMM_BUILD=0`** |
| ScadaChecks | `MSBuild.exe ScadaChecks\ScadaChecks.csproj` | **`SCADA_BUILD=0`** |

### 3.2 运行期证据（本次重点）

> **为什么要专门做运行期验证**：这次被坑正是因为「编译绿 ≠ 资源能解析」。
> 静态审查与编译都发现不了跨字典的 `{StaticResource}` 断链。

搭了一个一次性 WPF 探针（临时工程，跑完即删）：真实加载
`pack://application:,,,/UI;component/Themes/Generic.xaml`，拍平全部字典键，
然后**逐支实例化并套用**所有 `Dialog*` 样式与模板，把被延迟的 `Freezable` 逼出来解析。

探针踩坑记录（供复现）：裸进程里直接 `new Uri("pack://application:,,,/…")` 会抛
`UriFormatException: Invalid URI: Invalid port specified` —— 因为 WPF 还没把 `pack://`
方案注册进 `UriParser`，`application:,,,` 被当成 `host:port`。
解法：先 `new Application()`（或触碰 `PackUriHelper.UriSchemePack`）再构造 URI。

结果：

| 断言 | 结果 |
| --- | --- |
| 字典加载 | `DICT_OK merged=13` |
| 键总数 | `KEYS total=123 dialog=57` |
| `DialogShadowColor` | `Color #FF0F172A` ✅（正是 `Colors.xaml#L132` 的 `#0F172A`） |
| `DialogSurfaceBrush` / `DialogCardBorderBrush` / `DialogBorderBrush` | 全部解析成功 ✅ |
| **31 支 `Dialog*` 样式套用** | **31/31 `STYLE_OK`，0 失败** |
| **4 支 `Dialog*` 模板实例化** | **4/4 `TPL_OK`，0 失败** |
| **崩溃点 1**：`DialogCardShadow` 的 `DropShadowEffect` | **`SHADOW_OK DialogCardShadow.Color=#FF0F172A Blur=18 Depth=3`** ✅ |
| **崩溃点 2**：`DialogCombo` 模板（内含第二处 `DropShadowEffect`） | **`COMBO_OK`** ✅ |

> **⚠️ 订正（2026-09-23 后续）**：下面这段当时的结论**不完整**，被实机推翻。
> 当时看到 `DialogCardTitleIcon` 等 Setter 里的 `{StaticResource Icon}` 不报错，
> 就推断「`Setter.Value` 静默失效、只有 `Freezable` 会抛」。
> 但紧接着实机打开弹窗又崩了一次，报的正是
> `无法找到名为 Icon 的资源`，崩点在**模板**（`DialogSourceNodeTemplate`）里。
> 真相是**三套作用域**，不是两套：`Setter.Value` 静默跳过；
> `Freezable` 抛；**模板（`DataTemplate`/`ControlTemplate`）内容同样抛**。
> 详见 [2026-09-23-Dialog模板Icon资源崩溃修复.md](file:///e:/VM/VisionMaster-W-master/docs/code-changes/2026-09-23-Dialog模板Icon资源崩溃修复.md)。
> 下面保留原文以记录当时的判断过程。

**同时澄清了一个反直觉现象**（已写进注释）：`DialogCardTitleIcon` 等 3 支样式里的
`FontFamily="{StaticResource Icon}"` 用的是**同样的 `{StaticResource}` 写法却不报错**。
探针实测给出了原因 —— 探针里 App 级没有 `Icon` 时，该 Setter 被**静默跳过**，
`FontFamily` 退回系统默认 `'Microsoft YaHei UI'`；把 `Icon` 补进 App 资源后
解析为 `'Segoe UI'`（探针里塞的假值）。
即：**`Setter.Value` 路径查不到是静默失效，`Freezable` 路径查不到才是抛异常**。

### 3.3 契约回归

| 宿主 | 结果 |
| --- | --- |
| `CommChecks.exe` | **通过 77 / 失败 0**（`>>> 全部断言通过 <<<`） |
| `ScadaChecks.exe` | **通过 1592 / 失败 0**（`>>> 全部断言通过 <<<`） |

---

## 4. 已知边界

1. **`Colors.xaml` 被加载两份**：一份挂 `App.xaml → Generic.xaml` 链，一份挂
   `DialogStyles.xaml` 自己的链。两份都是同一 URI，WPF 按 URI 缓存，实际内存开销可忽略；
   但**若将来 `Colors.xaml` 里有可变状态（如被 `DynamicResource` 改写的令牌），两份会不同步**。
   当前 `Colors.xaml` 全是静态令牌，不成立。
2. **修复是「补依赖」，不是「改写法」**：本文件仍然在 `Freezable` 里用 `{StaticResource}`。
   这个写法**现在成立、将来也成立**，前提是**本文件自己的 `MergedDictionaries` 里始终有令牌字典**。
   若将来新建别的样式字典也用了 `Freezable` + `{StaticResource}`，**必须同样自带 merge**，
   否则会复现同一崩溃。这是本方案唯一的「规矩」，已写进文件头注释。
3. **同类风险未全仓扫描**：本次只治了 `DialogStyles.xaml`。全仓其它字典
   （`Popover` / `AlarmBar` / `PropertyGrid` / `PropergridStyle`）当前 `DropShadowEffect`
   的 `Color` 全是字面量，暂无此风险；但**没有自动断言**能阻止将来有人在那些文件里
   引入 `Freezable` + `{StaticResource}`。若要根治，需要一条「XAML 资源断链检查」的静态规则。
4. **`Icon` 那类「静默跳过」的坑无法被断言捕获**：`Setter.Value` 查不到资源时不抛异常，
   只是值没生效。这类问题只能靠视觉对比发现，属现有测试体系的盲区。
   （**订正**：模板里的同类引用会**抛异常**、能被捕获；真正静默的只有 `Setter.Value` 一路。
   本文件里的 5 处 `{StaticResource Icon}` 已在后续修复中全部改掉，见
   [2026-09-23-Dialog模板Icon资源崩溃修复.md](file:///e:/VM/VisionMaster-W-master/docs/code-changes/2026-09-23-Dialog模板Icon资源崩溃修复.md)。）
5. **实机点验仍待完成**：探针证明的是「资源能解析」，不等于「视觉正确」。
   三张弹窗（连接管理 / 变量管理 / 扫描组编辑）的实际观感仍需人工点验，
   包括：同屏切换不跳色、自绘标题栏空白处可拖、投影层与内容层叠加效果。
