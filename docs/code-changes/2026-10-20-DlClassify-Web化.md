# 2026-10-20 Plugin.DlClassify 自定义视图 Web 化

## 1. 范围

| 项 | 内容 |
| --- | --- |
| 设计基线 | `docs/UI设计基线/dl-classify/README.md` v1 + `design.html` |
| 覆盖 | `Plugins\Plugin.DlClassify\ClassifyView.xaml`、`ClassifyView.xaml.cs` |
| 插件类 | `DlClassifyPlugin.cs` 仅一处可空注解清理（`null` → `null!`，无行为变化） |
| 工程 | `Plugin.DlClassify.csproj` 未改（无新增引用/资源） |
| 不在范围 | 插件业务逻辑、端口、`[StepConfig]` 属性、`OnConfirm`、会话/推理、共享控件与共享字典 |
| 参考实现 | `docs/UI设计基线/image-acquisition/` V2；`docs/code-changes/2026-10-20-图像采集配置窗口V2复刻与FluentChipDanger重复键修复.md` |

本插件是清单中的 **S 级**（单预览 + 1 个 `LinkableValueEditor` + 2 个 `NumericBox`），
按 SOP §5.1 → §5.10 执行，一次只改这一个插件。

## 2. 现状耦合矩阵（SOP §5.1）

| 区域/元素 | 当前绑定/契约 | 功能依赖 | 可重构级别 |
| --- | --- | --- | --- |
| 根布局 | `Width=980 Height=620`，两列 `340 / *` | 纯布局；宿主内容区 920×520 时右侧被压 | 自由 |
| 图像输入 | `cv:LinkableValueEditor Port="{Binding Image}" StepData="{Binding StepData}"` | `InputPort<HImage>` 的持久化、类型转换、变量链接/解除链接、`ValueChanged` 订阅全在控件里 | **只能包裹**，不能替换 |
| 模型路径 | `TextBox Text="{Binding ModelPath, UpdateSourceTrigger=PropertyChanged}"` + 「浏览」`Click` | `ModelPath` 是 `[StepConfig]` + `[BrowsePath]`；`OnModelPathChanged` 刷新 `ModelSummary`；`OnViewLoaded` 重载会话 | 保留绑定；可改外观与按钮入口 |
| 模型摘要 | `TextBlock Text="{Binding ModelSummary}"` | getter 调 `EnsureSession()`；模型选错/路径不存在时是唯一线索 | 保留绑定；可改容器与排版 |
| 缩放模式 | `ComboBox ItemsSource="{Binding ResizeModeOptions}" SelectedItem="{Binding ResizeMode}"` | `ResizeMode` 是 `[StepConfig]` 枚举，持久化为成员名 | 保留类型；可加显示转换器 |
| TopK | `ui:NumericBox Value="{Binding TopK}"`，1~100 | `[StepConfig]` int；被夹到 `[1, 类别数]` | 保留控件与范围 |
| 输入尺寸 | `ui:NumericBox Value="{Binding InputSize}"`，0~4096，位于 `Expander` | `[StepConfig]` int；0 = 模型自带尺寸 | 保留控件与范围 |
| 试算 | `Button Click="OnTryClassifyClick"` → `TryPreviewClassify()` | 写 `ResultSummary`；与运行期共用 `TryClassify` | 保留入口；可改为主按钮/命令转发 |
| 提示 | `TextBlock Text="{Binding Hint}"` | 上游图显示、失败提示 | 保留绑定 |
| 试算结果 | `TextBlock Text="{Binding ResultSummary}"` | 预测类别/置信度/耗时/TopK 多行文本 | 保留绑定；可加结果卡 |
| 预览 | `h:ImageReadOnly HImage="{Binding DisplayImage, Mode=OneWay}"` | HALCON 显示/缩放/右键菜单；`DisplayImage` setter 在 UI 线程释放旧 `HImage` | **保留控件**；改按需宿主 |
| 生命周期 | 构造 `InitializeComponent`；`Loaded` → `OnViewLoaded()`；`DataContext = this`（插件） | 上游图显示 + 模型摘要刷新；宿主靠 `DataContext as IPluginConfigView` 取 `OnConfirm` | 保留 |
| 状态 | 无 `StatusMessage` / `StatusLevel`（插件类与基类均无） | 只有 `Hint` / `ResultSummary` | **不新增**第四档状态 |

**判定**：`LinkableValueEditor`、`ImageReadOnly`、四个 `[StepConfig]` 属性、`Hint`/`ResultSummary` 是唯一入口，
本轮只改外观/容器/布局；未重写任何业务路径。

## 3. 改动点

### 3.1 `ClassifyView.xaml`（整页重写）

1. **两栏布局**：左配置轨 `1*`（MinWidth 300 / MaxWidth 356）+ 右预览 `1.6*`（MinWidth 240）；根 `MinWidth=560 MinHeight=280`，不再写死 980×620。
2. **配置轨**：`DialogRowAlternateBrush` 底 + `DialogBorderLightBrush` 边 + 8px 圆角；内部 `ScrollViewer`，窄档滚动。
3. **图像输入**：`LinkableValueEditor` 保留 `Port=Image` / `StepData`；`Label=""`，外置「输入图像」标签与帮助文字。
4. **模型**：`TextBox Style=DialogInput Height=36` + 「浏览」按钮（`BrowseModelCommand`）；`ModelSummary` 放进「模型信息」卡，等宽 10.5px；`DataTrigger` 匹配固定失败文案时整卡切红。
5. **分类参数**：`ResizeMode` 用 `DialogCombo` + `ResizeModeLabelConverter` 显示中文（值仍是枚举）；`TopK` / `InputSize` 用 `NumericBox` 保持原范围与 `Value` 绑定；高级参数在 `Expander` 内。
6. **试算**：主按钮「试算一下」走 `TryPreviewCommand`；`Hint` 放进浅底提示条；`ResultSummary` 放进带标题/状态的结果卡，最大高度 132px 内滚动。
7. **预览工作区**：44px 预览头（图标 + 「图像预览」+「只读 · 整幅图」+ 适应窗口/1:1）+ 深色画布 + 40px 状态栏。
8. **HALCON 预览硬规则**：`ContentControl x:Name="PreviewHost" Content="{Binding DisplayImage}"` + `DataTemplate` 内 `h:ImageReadOnly HImage="{Binding Mode=OneWay}"`；**有图才创建**，无图时模板不实例化；没有用 `Visibility=Collapsed` 显隐已设图的控件。
9. **空态/错误态**：空态 = 虚线卡「还没有图像」；错误态 = 无图 + `ModelSummary == 模型加载失败，详见提示` 时显示红色「模型加载失败」覆盖层；两者都是纯 WPF 覆盖层。
10. **颜色零硬编码**：全部 `StaticResource`，`hex=0`；图标按钮 30×30 + `Padding="0"`（R68）；焦点环 `DialogFocusVisual`；ToolTip `DialogToolTipStyle`。
11. **状态栏**：`Hint` + 中性主色圆点 + 「整幅图 · 只读」；不伪造 `StatusLevel` 三档。

### 3.2 `ClassifyView.xaml.cs`

保留构造签名、`DataContext = 插件`（由 `GetConfigView` 设置）、`Loaded → OnViewLoaded()`。

新增（全部是 UI 职责，无业务逻辑）：

| 成员 | 作用 |
| --- | --- |
| `BrowseModelCommand` | 视图级 `ICommand`，委托 `BrowseModel()`：`OpenFileDialog` → `Plugin.ModelPath = ...` → `Plugin.OnViewLoaded()` |
| `TryPreviewCommand` | 视图级 `ICommand`，委托 `TryPreview()`：`Plugin.TryPreviewClassify()` |
| `FitWindow_Click` / `FitImage_Click` | 从 `PreviewHost` 可视树取当前 `ImageReadOnly`，调既有 `FitToWindow()` / `FitToImage()` |
| `FindVisualChild<T>` | 模板内控件不在 namescope，按可视树取；无图返回 null、静默空跑 |
| `UiCommand` | 极简 `ICommand` 包装（无参数、永远可执行），仅用于视图按钮 |
| `ResizeModeLabelConverter` | `ClassifyResizeMode` → 中文显示名；`ConvertBack` 返回 `Binding.DoNothing`，不改变持久化值 |

### 3.3 `DlClassifyPlugin.cs`

仅一处：

```diff
- public InputPort<HImage> Image { get; } = new("Image", null, "待分类的图像") { IsRequired = false };
+ public InputPort<HImage> Image { get; } = new("Image", null!, "待分类的图像") { IsRequired = false };
```

原因：插件工程启用 `Nullable`，`InputPort<T>` 构造参数 `T defaultValue` 在 `T=HImage` 下按非空引用类型检查，
原写法产生 **CS8625** 警告（构建 2 警告）。`null!` 只抑制可空分析，运行期值仍是 null，端口语义不变。
这是为满足「插件构建 0 错误 / 0 警告」的最小清理，**不是业务改动**。

## 4. 功能契约保留（逐项）

| # | 契约 | 状态 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 保留；`GetConfigView` 仍 `new ClassifyView { DataContext = this }` |
| C2 | `LinkableValueEditor`：`Port=Image` / `StepData` | 保留；`Label=""` 只影响外观 |
| C3 | `ImageReadOnly.HImage = DisplayImage`（`Mode=OneWay`） | 保留控件类型；改为 `ContentControl` 按需创建 |
| C4 | `ModelPath` / `ResizeMode` / `TopK` / `InputSize` | 四个 `[StepConfig]` 属性绑定与类型全部保留 |
| C5 | 「浏览」→ `ModelPath` + `OnViewLoaded()`；「试算一下」→ `TryPreviewClassify()` | 保留；改为视图级 `ICommand` 转发 |
| C6 | `Hint` / `ResultSummary` | 保留原绑定；只换容器/排版 |
| C7 | `Loaded → OnViewLoaded()` | 保留；上游图与模型摘要刷新时序不变 |
| C8 | 端口名/类型/持久化 | 未改；`PredictedClassId` / `PredictedClassName` / `Confidence` / `Scores` / `ClassNames` / `TopKText` 全部原样 |

## 5. 与 ImageAcquisition V2 的差异

| 维度 | ImageAcquisition V2 | DlClassify V1 |
| --- | --- | --- |
| 复杂度 | L（4 模式卡片 + 4 个 Linkable + 网络/相机） | S（1 个 Linkable + 2 个 Numeric） |
| 左栏 | 356px 配置轨 + 底部固定输出区 | 356px 配置轨；无输出区（插件无显示窗口配置） |
| 模式选择 | 2×2 `RadioButton` + `EnumConverter` 写 `Mode` | 无模式；`ResizeMode` 是普通枚举下拉（中文显示、值不变） |
| 预览 | `ContentControl PreviewHost` + `ImageReadOnly` | **同一模式**；`DisplayImage` 代替 `PreviewImage` |
| 预览工具条 | 适应窗口 / 1:1 / 目录刷新 | 适应窗口 / 1:1（无目录刷新命令） |
| 空态 | 按 Mode 四套空态 + 错误态 | 一套空态 + 模型失败错误态 |
| 状态 | `StatusMessage` / `StatusLevel` 三档 | 插件无 `StatusLevel`；用 `Hint` + 中性圆点 |
| 模型信息 | 无 | **新增「模型信息」卡**（`ModelSummary` 原属性） |
| 试算结果 | 无 | **新增结果卡**（`ResultSummary` 原属性） |
| 令牌 | 追加 5 个 Dialog 键 + 5 个 Preview 键 | **零新增**；全部复用既有键 |
| 共享文件 | 改了 Colors/PluginConfigColors/UIThemeSmokeTest | **未改任何共享文件**（按并发施工约束） |

## 6. 验证

| 项 | 命令 | 结果 |
| --- | --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.DlClassify\Plugin.DlClassify.csproj -v m` | **0 错误 / 0 警告**；`Modules\Plugin.DlClassify.dll` 已刷新（最终 2026-10-20 01:49:04，97,280 bytes）。补充：`--no-incremental` 会重编依赖链，`Core.Interfaces` / `UI` 等既有工程有 409 个历史警告；**本插件工程自身 0 警告** |
| 冒烟工程构建 | `dotnet build UIThemeSmokeTest\UIThemeSmokeTest.csproj -v m` | **0 错误**；1 个既有 `HslCommunication` 的 `MSB3245`（`System.Resources.Extensions` 未解析），与本插件无关 |
| 插件视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.DlClassify` | **11/11 PASS**（见下） |
| 渲染证据 | 批量模式自动输出 | `docs/UI设计基线/_batch-evidence/Plugin.DlClassify-DlClassifyPlugin.png`；已复制到 `docs/UI设计基线/dl-classify/evidence/initial-render-920x520.png` |
| 设计稿验证 | 浏览器打开 `design.html`，点击 960/640 与默认/空态/模型错误 | 4 张截图：`design-default-960.png` / `design-narrow-640.png` / `design-empty-960.png` / `design-error-960.png`；状态切换与 640 窄档生效 |
| 常规 gate | `dotnet build FlowCanvasChecks\FlowCanvasChecks.csproj -v q` → `dotnet run --project FlowCanvasChecks --no-build` | 构建 0 错误（218 个既有警告，与本插件无关）；**2691 通过 / 0 失败**（含 `ClassifyChecks`）。复跑记录：其中一次出现 1 条既有并行分支 `IsRunning` 时序断言（`分支1=False 分支2=True`），重跑 2691/0；与本插件改动无关，建议 reviewer 知悉 |
| XAML 静态 | 冒烟内置 | `xaml=1 hex=0` |
| 真机 | 未执行（需用户重启 VisionMaster 后打开配置窗口回环） | 待验收 |

冒烟 11 条：

```text
[PASS] 找到待检插件 DLL (count=1 filter=Plugin.DlClassify)
[PASS] GetConfigView 返回 FrameworkElement (ClassifyView)
[PASS] 视图实例化 (elements=209)
[PASS] DataContext/IPluginConfigView (DlClassifyPlugin)
[PASS] LinkableValueEditor.Port (count=1 ports=Image)
[PASS] Button.Command 绑定 (buttons=6 commands=BrowseModelCommand,TryPreviewCommand)
[PASS] ComboBox 绑定 (ResizeModeOptions/ResizeMode)
[PASS] TextBox.Text 绑定 (ModelPath)
[PASS] OnConfirm 可执行 (InputValues=5)
[PASS] XAML hex=0 (xaml=1 hex=0)
[PASS] 渲染 PNG
=== 插件自定义视图批量冒烟：全部通过 ===
```

> 说明：批量冒烟只验证公共契约（视图可实例化、DataContext、Port、命令/文本/下拉绑定、OnConfirm、hex=0、渲染）。
> 本插件的关键业务断言（`ModelPath` 写回、`ResizeMode` 枚举值、`TopK`/`InputSize` 写回、`OnConfirm` 键值）尚未单独补；
> 建议 reviewer 按 §8 重点复核，或在下一轮给 `UIThemeSmokeTest` 加 `--dl-classify` 单插件模式（本轮禁止改共享文件）。

## 7. 已知偏差

1. **图标字体**：Web 内联 Lucide 风格 SVG；WPF `DialogIconFont`（Font Awesome 6 Pro Solid）。
2. **预览图**：Web 内联 SVG 零件示意；WPF 真实 `HImage`。
3. **渲染证据范围**：`ImageReadOnly` 是 HALCON `HwndHost`，`RenderTargetBitmap` 不捕获其内容；PNG 只证明 WPF 布局与空态。
4. **模型失败判据**：WPF 用 `ModelSummary` 固定文案「模型加载失败，详见提示」触发红色卡/错误覆盖层；不新增 VM 属性。若插件改文案，视图与文档需同步。
5. **空图试算文案**：`TryPreviewClassify` 仍返回「先点『载入』把示意图读进来再试算」，新界面没有「载入」按钮；本轮不改业务文案，列为遗留。
6. **`ResizeMode` 中文显示**：视图转换器映射；持久化值仍是 `Letterbox` / `Stretch`，未改枚举定义、未加 `[Display]`。
7. **折叠区**：`Expander` 用 Fluent 默认头（外加卡片边框），未复刻 Web 自绘 summary。
8. **状态栏**：插件无 `StatusMessage` / `StatusLevel`；用 `Hint` + 中性圆点，不冒充三档状态。
9. **宿主页脚**：设计稿底部「执行/取消/确认」属于 `PluginConfigShellView`，插件视图不实现。
10. **窄档**：640×560 下左栏需滚动；本轮只做了 920×520 冒烟渲染，未做 640 宽真机截图。
11. **错误态状态栏**：WPF 状态栏仍绑 `Hint`；模型失败原因在模型信息红卡、错误覆盖层与 `ResultSummary`，设计稿的红色状态栏文案只是示意。
12. **左栏滚动**：960×680 下「试算」区仍在左栏可视区下方，需要滚动；本轮不新增吸底操作栏。
13. **结果卡状态标签**：Web 稿右上角显示「完成 / 尚未试算 / 失败」；WPF 固定显示「TopK」，状态由正文表达，不新增 `ResultState` 属性/转换器。
14. **`NumericBox` 模板颜色**：`ui:NumericBox` 的模板在共享主题 `NumericBox.xaml` 里自带颜色（未令牌化）；按约束不改共享控件，视图只设高度/范围/绑定。
15. **`LinkableValueEditor` 内部样式**：链接按钮与输入区内部由共享控件决定，视图只包裹不改内部。

## 8. 遗留问题与 reviewer 重点

| # | 项 | 建议 |
| --- | --- | --- |
| R1 | 空图试算文案提到不存在的「载入」按钮 | 下一轮把 `TryPreviewClassify` 文案改为「先在『图像输入』绑定上游图像，或点宿主『执行』带图进来」；属插件文案，需主会话确认 |
| R2 | 批量冒烟未覆盖 `ModelPath` / `ResizeMode` / `TopK` / `InputSize` 的 `OnConfirm` 回环 | 下一轮加 `--dl-classify` 单插件模式（本轮禁止改 `UIThemeSmokeTest` 共享文件） |
| R3 | 模型失败态依赖 `ModelSummary` 固定文案 | reviewer 检查 `DlClassifyPlugin.ModelSummary` 的失败返回值是否仍为「模型加载失败，详见提示」；若改文案，三处 `DataTrigger` 需同步 |
| R4 | `ModelSummary` getter 在绑定求值时可能触发模型加载 | 既有行为；本轮未改。reviewer 确认不会在 UI 线程造成可感知卡顿（大模型首次加载） |
| R5 | `LinkableValueEditor` 的链接/解除链接未在冒烟中点击验证 | reviewer 真机点 🔗/✕，确认 `SetLink` / `RemoveLink` 与原行为一致 |
| R6 | `ImageReadOnly` 真实图像刷新 | 真机打开配置 → 点宿主「执行」带图 → 确认预览显示；再换一张图确认刷新（本轮只验证了空态渲染） |
| R7 | 640×560 最小档 | 真机缩放窗口，确认左栏滚动、右栏最小 240px、状态栏不溢出 |

## 9. 未修改的共享文件（并发施工约束核对）

本轮**未修改**：

- `UI\Controls\Themes\Colors.xaml`
- `UI\Controls\Themes\PluginConfigColors.xaml`
- `UIThemeSmokeTest\Program.cs`
- `UIThemeSmokeTest\UIThemeSmokeTest.csproj`
- `UIThemeSmokeTest\PluginViewSmoke.cs`
- `VisionMaster\Views\DialogViews\PluginConfigShellView.xaml`
- 其他插件目录

> 因为未新增公共令牌，设计基线 README 的「令牌映射」全部落在既有键上；不存在需要主会话统一追加的缺失令牌。

## 10. 复跑命令

```text
dotnet build Plugins\Plugin.DlClassify\Plugin.DlClassify.csproj -v m
dotnet build UIThemeSmokeTest\UIThemeSmokeTest.csproj -v m
dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.DlClassify
```

> `Modules\Plugin.DlClassify.dll` 由 `Plugins\Directory.Build.targets` 的 `CopyPluginToModules` 自动刷新；
> 本次构建未被 VisionMaster/VS 锁定（首次即成功）。
