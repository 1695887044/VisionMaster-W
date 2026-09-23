# 2026-09-23 Dialog 模板 Icon 资源崩溃修复

> 上下文：承 [2026-09-23-DialogShadowColor资源解析崩溃修复.md](file:///e:/VM/VisionMaster-W-master/docs/code-changes/2026-09-23-DialogShadowColor资源解析崩溃修复.md)。
> 上一轮把 `DialogShadowColor`（`Freezable` 内的 `{StaticResource}`）修好、编译双绿之后，
> **实机一打开弹窗又崩了第二次**，这次报的是 `Icon`。本记录是这一处的根因定案与修复，
> 同时订正上一轮「只有 Freezable 会抛」的不完整结论。

---

## 1. 需求与决策

### 1.1 现象

```
System.Windows.Markup.XamlParseException
  Message: 在"System.Windows.Markup.StaticResourceHolder"上提供值时引发了异常。行号"955"，行位置"21"
  StackTrace: XamlReader.RewrapException
              → FrameworkTemplate.LoadTemplateXaml
              → FrameworkTemplate.LoadOptimizedTemplateContent
              → FrameworkTemplate.LoadContent
              → StyleHelper.ApplyTemplateContent
              → FrameworkElement.ApplyTemplate
              → FrameworkElement.MeasureCore
              → VirtualizingStackPanel.MeasureChild
              → …
              → Window.ShowDialog()
              → Prism.Dialogs.DialogService.ShowDialogWindow
              → Prism.Dialogs.IDialogServiceExtensions.ShowDialog
              → VisionMaster.ShellViewModel.OnSystemAction   (ShellViewModel.cs:515)
  内部异常: Exception: 无法找到名为"Icon"的资源。资源名称区分大小写。
```

与上一轮的两点关键差异：

1. 栈顶是 **`FrameworkTemplate.LoadTemplateXaml`** —— 崩在**模板加载**，不是 BAML 加载、不是 `Freezable`；
2. 触发时机是 `ApplyTemplate`（弹窗被 `ShowDialog` 真正度量时），
   比上一轮的 `InitializeComponent` 更晚，**编译期和上一轮探针都没能拦住**。

### 1.2 根因：`{StaticResource}` 其实有**三套**解析作用域

上一轮只总结了「Setter 静默 / Freezable 抛」两套，漏了模板这一路：

| 落脚点 | 求值时机 | 查找作用域 | 查不到时 |
| --- | --- | --- | --- |
| `Setter.Value`（普通样式） | 延迟到「套用到元素那一刻」 | 元素作用域链：元素 → 父级 → App 资源 | **静默跳过**（值不生效，不抛） |
| `Freezable` 内部（如 `DropShadowEffect`） | **加载该字典时**立即构造并赋值 | **声明它的那个字典** + 它的 `MergedDictionaries` | **抛 `XamlParseException`** |
| **模板内容（`DataTemplate`/`ControlTemplate`）** | 延迟到 **`ApplyTemplate`** 才加载 | **声明模板的那个字典** + 它的 `MergedDictionaries` | **抛 `XamlParseException`** ← 本次 |

关键：模板和 `Freezable` 的查找作用域是**同一个**（声明它的字典），都**看不到 App.xaml 的 inline 资源**。
`Icon` 恰恰只定义在 [App.xaml#L11](file:///e:/VM/VisionMaster-W-master/VisionMaster/App.xaml#L11) 的
inline 资源里，不在任何可被 merge 的字典中：

```xml
<prism:PrismApplication.Resources>
    <ResourceDictionary>
        <!--  全局图标字体：Font Awesome 6 Pro Solid  -->
        <FontFamily x:Key="Icon">pack://application:,,,/UI;component/Asserts/FontFamilys/Font Awesome 6 Pro-Solid-900.otf#Font Awesome 6 Pro Solid</FontFamily>
        …
```

而 [DialogStyles.xaml](file:///e:/VM/VisionMaster-W-master/UI/Controls/Themes/DialogStyles.xaml)
是**独立字典 + 含模板**，且与 `App.xaml` 之间隔着 `Generic.xaml` 的兄弟结构：

```
App.xaml  (inline Icon 在这)
 └─ MergedDictionaries
     └─ /UI;component/Themes/Generic.xaml
         └─ MergedDictionaries
             ├─ Colors.xaml
             ├─ IconDictionary.xaml      ← 只有 FA.Solid / FA.Light / …，没有 Icon 这个键
             ├─ …
             └─ DialogStyles.xaml        ← 模板在这里引用 Icon ⇒ 找不到 ⇒ 崩
```

> 注：[IconDictionary.xaml](file:///e:/VM/VisionMaster-W-master/UI/Controls/Themes/IconDictionary.xaml)
> 里虽然有一支与 `Icon` **同 URI** 的 `FA.Solid`，但**键名不是 `Icon`**，
> 所以「merge 这个字典」解决不了问题 —— 必须换写法或补一个本地令牌。

### 1.3 为什么上一轮探针没拦住

上一轮探针里 `DialogSourceNodeTemplate` / `DialogStatusPillTemplate` 报过 `TPL_FAIL`，
但当时把它归因为「探针没加载 App 资源的环境差异」而放过 —— **实机证明那是真 bug**。

教训：探针里出现的 `FAIL` **不允许**用「宿主环境不同」当理由降级。
`Icon` 这类「只存在于 App inline 资源」的键，跨字典/模板引用**本身就脆**，
探针报错即真信号。

### 1.4 决策：本文件自带图标字体令牌，不用 `{StaticResource Icon}`

**决策：在 `DialogStyles.xaml` 根节点定义本地令牌 `DialogIconFont`（写真 pack 绝对串），
把文件里 5 处 `{StaticResource Icon}` 全部换成 `{StaticResource DialogIconFont}`。**

理由：

- **自包含、最小爆炸半径** —— 只动一个文件，不碰 `App.xaml`、不碰 `Generic.xaml`、不碰三张弹窗视图；
- **符合仓库既有范式** —— [ScadaLayerView.xaml#L31](file:///e:/VM/VisionMaster-W-master/VisionMaster/Views/ScadaLayerView.xaml#L31)
  早就写明「图标字体**刻意写 pack 绝对串而不是 `{StaticResource Icon}`**」，
  [2026-09-20 记录](file:///e:/VM/VisionMaster-W-master/docs/code-changes/2026-09-20-SCADA图层面板与多选对齐分布.md) 也记过同一条教训；
- **一次治五处、覆盖三套作用域** —— 模板 2 处（会抛）+ Setter 3 处（会静默失效），
  换成本地令牌后**两种路径都稳**；
- **不选「merge IconDictionary.xaml」** —— 那个字典里**没有 `Icon` 这个键**，merge 了也拿不到；
- **不选「把 `Icon` 也塞进 `Colors.xaml`」** —— 会把「颜色令牌字典」变成杂物间，
  且让字体这种与色彩无关的东西混进主题色层，职责不清。

---

## 2. 修改文件清单

| 文件 | 变更 | 说明 |
| --- | --- | --- |
| [DialogStyles.xaml](file:///e:/VM/VisionMaster-W-master/UI/Controls/Themes/DialogStyles.xaml#L67-L69) | +3 行 | 根节点新增 `<FontFamily x:Key="DialogIconFont">pack://…Font Awesome 6 Pro Solid</FontFamily>` |
| [DialogStyles.xaml](file:///e:/VM/VisionMaster-W-master/UI/Controls/Themes/DialogStyles.xaml#L55-L65) | +11 行注释 | 说明「为什么不能用 `{StaticResource Icon}`」，与文件头的作用域说明互相印证 |
| [DialogStyles.xaml](file:///e:/VM/VisionMaster-W-master/UI/Controls/Themes/DialogStyles.xaml#L29-L51) | 改注释 | 把「两套作用域」订正为「**三套**」，补上模板这一路 |
| [DialogStyles.xaml](file:///e:/VM/VisionMaster-W-master/UI/Controls/Themes/DialogStyles.xaml) | 5 处替换 | `{StaticResource Icon}` → `{StaticResource DialogIconFont}`：`DialogCardTitleIcon`(L118)、`DialogSearchIcon`(L231)、`DialogStatusPillTemplate` 内 ⚠(L726)、`DialogEmptyStateIcon`(L750)、`DialogSourceNodeTemplate` 内节点图标(L972) |
| [2026-09-23-DialogShadowColor资源解析崩溃修复.md](file:///e:/VM/VisionMaster-W-master/docs/code-changes/2026-09-23-DialogShadowColor资源解析崩溃修复.md) | 订正 | §3.2 与 §4.4 里「只有 Freezable 会抛」的不完整结论，加订正说明并指向本文 |

**字体串与 `App.xaml#L11` / `IconDictionary.xaml` 的 `FA.Solid` 同源同串**：

```
pack://application:,,,/UI;component/Asserts/FontFamilys/Font Awesome 6 Pro-Solid-900.otf#Font Awesome 6 Pro Solid
```

**未改动**：`App.xaml`（`Icon` 定义保持原样，其它视图仍在用）、`Generic.xaml`、
`IconDictionary.xaml`、三张弹窗视图的 XAML、`ShellViewModel.cs`。

**范围说明**：全仓 `{StaticResource Icon}` 共约 100 处，绝大多数写在**视图**里（元素作用域内，
元素链能爬到 App 资源，一直正常）。本次**只治全局字典 + 模板这一处高危**，
不顺手大改视图 —— 避免把稳定代码卷进风险。

---

## 3. 验证结果

### 3.1 编译

| 项 | 命令 | 结果 |
| --- | --- | --- |
| UI 工程 | `MSBuild.exe UI\Controls\UI.csproj` | **`UI_EXIT=0`** |
| 主工程 | `MSBuild.exe VisionMaster\VisionMaster.csproj` | **`VM_EXIT=0`** |
| CommChecks | `MSBuild.exe CommChecks\CommChecks.csproj` | **`BUILD_EXIT=0`** |
| ScadaChecks | `MSBuild.exe ScadaChecks\ScadaChecks.csproj` | **`BUILD_EXIT=0`** |

### 3.2 运行期证据（本次重点）

> **为什么必须做运行期验证**：这次的崩点在 `ApplyTemplate`，**编译期完全无感**，
> 上一轮的探针也误判过。所以这次专门让探针走「**模板加载**」这条与实机一致的路径。

一次性 WPF 探针（临时工程，跑完即删）：`new Application()` 注册 `pack://` 方案 →
真实加载 `pack://application:,,,/UI;component/Themes/DialogStyles.xaml` →
对每支 `DataTemplate` 调 `LoadContent()`（等价实机 `ApplyTemplate` 那一刻）→
读出模板内 `TextBlock` 的 `FontFamily` 确认解析到位。

结果：

| 断言 | 结果 |
| --- | --- |
| 字典加载 | `DICT_LOAD_OK DialogStyles.xaml` ✅ |
| **崩溃现场**：`DialogSourceNodeTemplate` | **`TPL_OK`，`FontFamily=pack://…Font Awesome 6 Pro Solid`** ✅（修复前此处抛 `无法找到名为 Icon 的资源`） |
| `DialogStatusPillTemplate` | `TPL_OK` ✅（根节点首个 TextBlock 是状态文字，`FontFamily=Microsoft YaHei UI` 属预期；⚠ 图标那支不再抛异常） |
| `DialogCardTitleIcon`（Setter 路径） | `STYLE_OK`，`FontFamily=pack://…Font Awesome 6 Pro Solid` ✅ |
| `DialogSearchIcon`（Setter 路径） | `STYLE_OK`，同上报字体 ✅ |
| `DialogEmptyStateIcon`（Setter 路径） | `STYLE_OK`，同上报字体 ✅ |
| 本地令牌 | `TOKEN DialogIconFont=pack://…Font Awesome 6 Pro Solid` ✅ |
| 汇总 | **`RESULT: ALL_OK`** ✅ |

关键点：**Setter 三支现在也解析到 Font Awesome 字体族了**。
修复前它们是「静默跳过、退回系统默认字体」—— 也就是说图标会显示成豆腐块/默认字形，
即便不崩也是**视觉 bug**。这次一并治好了。

### 3.3 契约回归

| 宿主 | 结果 |
| --- | --- |
| `CommChecks.exe` | **通过 77 / 失败 0**（`>>> 全部断言通过 <<<`） |
| `ScadaChecks.exe` | **通过 1592 / 失败 0**（`>>> 全部断言通过 <<<`） |

---

## 4. 已知边界

1. **`Icon` 仍在 `App.xaml` 里 inline 定义**：本次没有搬动它。
   结论是「跨字典/模板的引用一律自带 pack 绝对串」，而不是「把 `Icon` 挪进可 merge 的字典」。
   若将来有人新建「含模板的独立字典」并写 `{StaticResource Icon}`，**会复现同一崩溃**。
2. **全仓同类风险未做自动断言**：`{StaticResource Icon}` 在视图里还有约 100 处，
   它们当前都正常（元素作用域内能爬到 App 资源），但**没有静态规则能拦住**
   「把这类引用挪进独立字典/模板」。要根治需要一条「XAML 资源断链检查」的静态规则。
3. **`Setter.Value` 的静默失效无法被断言捕获**：查不到资源时不抛异常、只是值没生效，
   只能靠视觉对比发现。本次因为顺手统一了写法才一并消掉，否则会长期潜伏。
4. **字体串三处重复**：`App.xaml` 的 `Icon`、`IconDictionary.xaml` 的 `FA.Solid`、
   本文件的 `DialogIconFont`，三处是同一串。这是「自包含」的代价；
   若将来换图标字体，**三处都要改**（已写进文件头注释提示）。
5. **实机点验仍待完成**：探针证明的是「资源能解析、字体族正确」，
   不等于「图标码点在该字体里有字形」。三张弹窗里的图标是否显示为正确图形
   （而非方框）仍需人工点验，重点看 `f05a` / `f201` / `f0e4` 这几个新码位。
