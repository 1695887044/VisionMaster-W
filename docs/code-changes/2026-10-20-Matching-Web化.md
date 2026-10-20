# 2026-10-20 Plugin.Matching 自定义视图 Web 化

## 1. 范围

| 项 | 内容 |
| --- | --- |
| 设计基线 | `docs/UI设计基线/matching/README.md` v1（`design.html` 自包含、可点击） |
| 覆盖 | `Plugins\Plugin.Matching\MatchingView.xaml` + `MatchingView.xaml.cs` |
| 参考实现 | `Plugin.ImageAcquisition` V2（`docs/UI设计基线/image-acquisition/`） |
| 不在范围 | 插件业务逻辑、端口名、命令语义、持久化、`MatchingPlugin.cs` 的匹配/学习算法 |
| 共享文件 | **未修改** `Colors.xaml`、`PluginConfigColors.xaml`、`UIThemeSmokeTest\Program.cs`/`.csproj`/`PluginViewSmoke.cs`、`PluginConfigShellView.xaml`、`docs/UI设计基线/README.md`、其它插件 |

## 2. 耦合矩阵（SOP §5.1）

| 区域/元素 | 当前绑定/契约 | 功能依赖 | 可重构级别 |
| --- | --- | --- | --- |
| 根 `UserControl` | `MinWidth/MinHeight` = `PluginViewMinWidth/MinHeight`；`DataContext` 由 `GetConfigView` 设为插件实例；`Loaded → OnViewLoaded` | 宿主靠 `DataContext as IPluginConfigView` 取 `OnConfirm`；`OnViewLoaded` 负责把上游图/参考图带上画布 | 只能改布局，保留构造/生命周期 |
| 模板库列表 | `ItemsSource=Library`、`SelectedItem=EditingEntry`（TwoWay） | `EditingEntry` setter：旧条目写回掩膜 → 播种画布 → 恢复掩膜/刷新预览 → 通知过期徽标 | 只能包裹/改外观 |
| 新增 / 删除 / 设为默认 | `AddTemplateEntry()` / `DeleteSelectedEntry()` / `SetAsDefault()` | 模板库增删、默认模板名写回 `DefaultTemplateName` | 保留方法；按钮改为视图命令转发 |
| 配方名 | `EditingEntry.Name`（PropertyChanged） | 配方键；`RecipeName` 端口按名字选模板 | 只能改外观 |
| 上游图像 | `LinkableValueEditor`：`Port=Image`、`StepData=StepData` | 端口持久化、类型转换、变量链接/解除链接 | 只能包裹，不能替换 |
| 参考图路径 | `EditingEntry.RefImagePath` + `LoadPreviewImage()` | 每个条目自己的配置态参考图 | 只能改外观；载入按钮保留 |
| 模板区域摘要 | `TemplateSummary` | 形状感知（矩形/圆/椭圆）的中心/角度/尺寸 | 只能改外观 |
| 创建 / 清除模板 | `CreateTemplate()` / `ClearTemplate()` | 学习 shape model、写入条目、刷新特征预览；清空画布 ROI | 保留方法；按钮改为视图命令转发 |
| 涂抹编辑 | `SmearModeOptions/SelectedIndex=SmearModeIndex`、`BrushSize`（1~50 + `NumberValidationRule`）、`ClearSmear()` | 涂抹模式、笔刷半径、排除域写回条目 | 保留全部绑定/校验 |
| 提取参数 | `EditingEntry.MinAngleDeg/MaxAngleDeg/AngleStepDeg/GradientThreshold/ScaleEnabled/ScaleMin/ScaleMax/NumLevels`；`PolarityOptions/SelectedIndex=PolarityIndex` | 创建态参数；`EntrySignature` 决定「参数已改·需重新学习」 | 保留全部绑定/范围/校验 |
| 匹配参数 | `MinScore` / `MaxMatches` / `MaxOverlap` / `Greediness` | 运行期参数，立即生效 | 保留全部绑定/范围/校验 |
| 输出 | `RecipeName` 的 `LinkableValueEditor`；`ViewIndexes` + `SelectedItem=DisplayViewIndex`（int 0~9）；`OutputAlignedImage` | 配方名端口、运行显示窗口、位姿归一化图像开关 | 保留全部绑定/类型 |
| 主画布 | `h:ImageEdit`：`ActiveRoi=CanvasActiveRoi`（TwoWay）、`BrushRadius=BrushSize`（TwoWay）、`DrawObjectList=CanvasRois`、`HImage=DisplayImage`（OneWay）、`SmearDraw=SmearMask`、`SmearErase=SmearEraseRegion`、`SmearMode=CanvasSmearMode` | HALCON 画框/拖拽/右键菜单/涂抹/ROI 交互全部依赖这些 DP | 只能包裹；控件必须常驻 |
| 模板预览 | `h:ImageReadOnly.HImage=TemplatePreviewImage`；`HasTemplatePreview` | HALCON 显示/缩放/右键菜单；`TemplatePreviewImage` setter 释放旧图 | 保留控件；**不折叠已设图控件** |
| 状态徽标 | `IsTemplateCreated` / `IsModelStale` / `TemplateStatusText` | 未学习 / 已学习 / 参数已改·需重新学习 | 保留三态 |
| 状态栏 | `StatusMessage` / `StatusLevel`（Info/Warning/Error） | 信息栏文案与颜色 | 保留三档，不扩展 |
| code-behind | 构造 `InitializeComponent` + `Loaded → OnViewLoaded`；7 个按钮转发 | 视图就绪、载入、创建、清除、涂抹、库管理 | 只加 UI 命令/转换器/可视树查找 |

> 本插件**没有 `Mode` 属性**（SOP 模板里的 Mode 行不适用）；最接近的“模式”是 `SmearModeIndex` 与 `PolarityIndex` 两个 int 索引，已原样保留。`LinkableValueEditor` 为 **2 个**（Image / RecipeName），不是图像采集的 4 个；原视图这两个编辑器均**未设置 `BrowseCommand`**（两个端口没有浏览语义），本次保持未设置，未新增死按钮，`Port` / `StepData` 原样。视图内实际 `NumericBox` 为 **12 个**（计划文档的 `N60` 是字符串计数口径），全部保留原绑定、范围与 `NumberValidationRule`。原视图的 HALCON 控件实际是 **`ImageEdit`（主画布/ROI/涂抹）+ `ImageReadOnly`（模板预览）各 1 个**，没有第二个 `ImageEdit`；两者均常驻。

## 3. 功能契约保留（逐项）

| # | 契约 | 状态 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 保留；`MatchingView` 构造未改，未引入独立 VM |
| C2 | `LinkableValueEditor`：`Image` / `RecipeName` 的 `Port` + `StepData` | 保留；只设 `Label=""` 外置标签，控件内部未改；原视图未设置 `BrowseCommand`，本次保持（不造死按钮） |
| C3 | `ImageEdit` 全部 DP | 逐字保留；控件常驻，未用 `Visibility` 显隐 |
| C4 | `ImageReadOnly.HImage = TemplatePreviewImage` | 保留；控件常驻，`HasTemplatePreview` 只控制覆盖层，不切控件可见性 |
| C5 | `Library` / `EditingEntry` / `DisplayViewIndex` / `SmearModeIndex` / `PolarityIndex` | 类型未变：对象 / 对象 / int / int / int |
| C6 | 命令与状态 | 新增 / 删除 / 设为默认 / 载入 / 创建模板 / 清除 / 清除涂抹全部可用；`StatusMessage` + `StatusLevel` 三档原样消费 |

## 4. 改动点

1. **整页重写 `MatchingView.xaml`**：左栏 368px 配置轨（模板库 → 提取参数 → 匹配参数 → 涂抹编辑 → 输出 → 怎么用），右侧参考图工作区（头部 + 画布 + 模板预览条 + 状态栏）。
2. **模板库成为第一屏**：`ListBox` 保持 `Library` / `EditingEntry` 双向绑定；每行增加 `Model` 空/非空驱动的绿/灰状态点与「已学习/未学习」文字（只读展示，不新增属性）。
3. **状态徽标三态前置**：`StatusPillStyle` 用 `IsTemplateCreated` / `IsModelStale` 触发绿/橙；左栏标题行与右侧工作区头同时显示。
4. **提取/匹配参数分层**：两组参数拆开，所有 12 个 `NumericBox` 的 `Minimum/Maximum/DecimalPlaces/ValidationRules` 与绑定路径逐项保留。
5. **主画布常驻**：`h:ImageEdit` 不再被任何容器包裹成可折叠/可替换控件；空态与错误态改为覆盖层（`DisplayImage == null` + `StatusLevel` 触发）。
6. **模板预览常驻**：`h:ImageReadOnly` 始终在可视树；无预览时用覆盖层显示「创建模板后显示特征预览」，**不再用 `Visibility=Collapsed` 显隐控件**，规避折叠态收到 `TemplatePreviewImage` 后恢复不重绘。
7. **工具条只接现有能力**：适应窗口 / 1:1 通过 `FindVisualChild<ImageEdit>(MainCanvasHost)` 调 `FitToWindow()` / `FitToImage()`；未新增放大/缩小。
8. **按钮命令化**：视图 code-behind 提供 7 个 `ICommand`（`AddEntryCommand` 等），只转发到插件现有公开方法；绑定用 `RelativeSource AncestorType=UserControl`，`DataContext` 仍是插件。这样批量冒烟的 `Button.Command` 契约可被证明，且不造死按钮。
9. **状态文案兜底**：新增 `StatusMessageConverter`，`StatusMessage` 为空时显示「就绪：载入参考图并框选模板区域」；不改变 `StatusLevel` 三档。
10. **零硬编码颜色**：视图 XAML `hex=0`，全部引用 `Dialog*` / `PluginPreview*` / `ScadaSecondaryTextBrush` 既有公共令牌；**未新增令牌**。
11. **根 MinWidth 调整为 600**：原视图用 `PluginViewMinWidth`（620），宿主 640 宽窗口的内容区约 600；按参考实现与窄档列最小宽改为 `MinWidth="600"`（左栏 320 + 间距 14 + 右栏最小 260 = 594，600 可容纳），`MinHeight` 仍走 `PluginViewMinHeight`（360）。
12. **窄档触发器**：视图新增 `IsNarrow` DP（`SizeChanged` 阈值 `<700`）——右侧工作区头隐藏「右键 → 新建 ROI」与状态胶囊、状态栏隐藏当前条目名、模板预览条 `148 → 124px`；左栏保持列最小宽 320px（设计稿 300px 为 Web 演示口径）。
13. **Nullable 警告清理（最小改动）**：`MatchingPlugin.cs` 两处原有 CS8601/CS8625 告警用 `annotated!` 与 `string? textColor` 消除，运行期语义不变（`RenderAnnotated` 失败仍返回 null；`AddTextBlock` 在 `lines == null` 时本来就不使用颜色）。

## 5. 与 ImageAcquisition V2 的差异

| 维度 | ImageAcquisition V2 | Matching V1 |
| --- | --- | --- |
| 信息架构 | 2×2 模式卡片 + 模式专属面板 + 输出 | 模板库 + 提取参数 + 匹配参数 + 涂抹 + 输出（单列滚动） |
| 左栏宽度 | 356px（1*，300~356） | 368px（1*，320~380） |
| 画布数量 | 1 个 `ImageReadOnly` 预览宿主（按需创建） | 2 个 HALCON 控件常驻：`ImageEdit` + `ImageReadOnly` |
| 预览宿主策略 | `ContentControl` + DataTemplate，有图才创建 | 控件常驻 + 纯 WPF 覆盖层（本插件 ROI/涂抹要求控件常驻） |
| 模板预览 | 无 | 148px 常驻条（窄档 124px），绿色特征轮廓 + 参数过期横幅 |
| 状态徽标 | 相机/推送状态 | 模板三态（未学习/已学习/参数已改） |
| 工具条 | 适应窗口 / 1:1 / 目录刷新 | 适应窗口 / 1:1 |
| 端口数 | 4 个 LinkableValueEditor | 2 个（Image / RecipeName） |
| 命令 | 现有 `Browse*/Refresh/Prev/Next/PushLocalImage` 命令 | 7 个视图命令转发现有公开方法（插件本身无 ICommand 属性） |
| 共享令牌 | 新增 5 + 5 个 | **无新增**，全部复用既有键 |

## 6. 验证

| 项 | 命令 | 结果 |
| --- | --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.Matching\Plugin.Matching.csproj -v m` | **0 错误 / 0 警告** |
| Modules 产物 | `Modules\Plugin.Matching.dll` 与 `bin\Debug\net9.0-windows\Plugin.Matching.dll` | 时间戳/大小一致：`2026-10-20 03:41:48`，189,952 bytes |
| 批量插件视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.Matching` | **11/11 PASS**（见下） |
| 常规 gate | `dotnet run --project FlowCanvasChecks --no-build` | **2691 通过 / 0 失败** |
| 设计稿 | 浏览器实测 `design.html` | 960/640 两档、未学习/已学习/参数已改、正常/空态/错误均可切换；证据见 `evidence/design-v1-960.png`、`design-v1-640.png` |
| 视图初始态渲染 | `--plugin-views` 输出 | `docs/UI设计基线/matching/evidence/after-render-v1-initial.png` |

### 6.1 批量冒烟逐条

```text
[PASS] 找到待检插件 DLL  (count=1 filter=Plugin.Matching)
[PASS] Plugin.Matching.MatchingPlugin GetConfigView 返回 FrameworkElement  (MatchingView)
[PASS] Plugin.Matching.MatchingPlugin 视图实例化  (elements=495)
[PASS] Plugin.Matching.MatchingPlugin DataContext/IPluginConfigView  (MatchingPlugin)
[PASS] Plugin.Matching.MatchingPlugin LinkableValueEditor.Port  (count=2 ports=Image,RecipeName)
[PASS] Plugin.Matching.MatchingPlugin Button.Command 绑定  (buttons=10 commands=AddEntryCommand,DeleteEntryCommand,SetDefaultCommand,LoadImageCommand,CreateTemplateCommand,ClearTemplateCommand,ClearSmearCommand)
[PASS] Plugin.Matching.MatchingPlugin ComboBox 绑定  (EnumOptions/;PolarityOptions/;SmearModeOptions/;EnumOptions/;/DisplayViewIndex)
[PASS] Plugin.Matching.MatchingPlugin TextBox.Text 绑定  (EditingEntry.Name,PortValue,LinkAddress,EditingEntry.RefImagePath)
[PASS] Plugin.Matching.MatchingPlugin OnConfirm 可执行  (InputValues=15)
[PASS] Plugin.Matching.MatchingPlugin XAML hex=0  (xaml=1 hex=0)
[PASS] Plugin.Matching.MatchingPlugin 渲染 PNG
=== 插件自定义视图批量冒烟：全部通过 ===
```

> `UIThemeSmokeTest` 按任务要求**未构建**，以 `--no-build` 运行现有二进制；它从 `Modules\Plugin.Matching.dll` 反射加载本插件，产物已刷新。
>
> `FlowCanvasChecks` 首次重跑曾出现 1 条既有波动断言（「★运行中：两条分支胶囊 IsRunning=true」→ 分支1=False 分支2=True）；以 UTF-8 控制台重跑后汇总为「通过: 2691 失败: 0 / 全部断言通过」，该断言与 Matching 改动无关。

## 7. 遗留问题

1. **多状态渲染证据**：批量冒烟只输出一张初始态 PNG；设计稿的未学习/参数已改/空态/错误态/640 窄档由 `design.html` 演示开关覆盖，WPF 侧多状态截图需真机窗口（`ImageEdit`/`ImageReadOnly` 是 HALCON `HwndHost`，`RenderTargetBitmap` 不捕获其内容）。
2. **真机回环未做**：需要打开 VisionMaster → 模板匹配配置 → 载入参考图 → 画 ROI → 创建模板 → 改参数 → 确认 → 重开，验证掩膜/模型/预览/`RecipeName` 端口回显；本轮只做构建 + 冒烟 + gate。
3. **模板库列表没有排序/拖拽**：需要新的集合操作契约，未纳入。
4. **加载骨架 / 尺寸 / 放大缩小 / 打开文件夹 / 系统导航**：均需新增功能或契约，按 SOP 延后。
5. **`ImageReadOnly` 的模板预览初始黑底**：无图时 HALCON 窗口可能显示黑底，当前用 `PluginPreviewSurfaceBrush` 覆盖层盖住；真机上若 HALCON 窗口在覆盖层下仍透出，需要真机确认（不影响绑定/功能）。
6. **`Expander` 视觉**：涂抹编辑/怎么用使用 WPF 原生 `Expander` 头，未复刻 Web 的自绘 `<details>` 头；行为一致。
7. **设计基线待用户确认**：`docs/UI设计基线/matching/README.md` 的「确认状态」仍为待确认；确认后按基线只读，改动升 v2。
8. **Release / 打包产物未刷新**：本次只构建 Debug 并把 Debug DLL 投递到 `Modules\`；`pack.ps1` 打的是 Release 产物，真机发布前必须再执行 `dotnet build -c Release`（或 `pack.ps1`）并核对 `Modules\Plugin.Matching.dll` 的时间戳/大小，否则现场运行的仍是旧 Release 行为（与红线③/R55 同族）。

## 8. Reviewer P2 修正（2026-10-20）

1. **状态胶囊颜色刷新**：`MatchingPlugin.TouchModelStaleness()` 增加 `OnPropertyChanged(nameof(IsTemplateCreated));`。`IsTemplateCreated` 是计算属性，创建模板 / 切换条目后 `Model` 变化会走 `TouchModelStaleness()`；现在 `IsTemplateCreated` / `IsModelStale` / `TemplateStatusText` 三条通知一起发，胶囊文字与底色同步刷新。只加通知，不改业务语义。
2. **640 窄档实现**：视图新增 `IsNarrow` DP（`SizeChanged` 阈值 `<700`）——右侧工作区头隐藏「右键 → 新建 ROI」与状态胶囊、状态栏隐藏当前条目名、模板预览条 `148 → 124px`。根 `MinWidth=600`；右栏最小宽 260、模板预览两列最小宽 110/110，保证宿主 640 窗口（内容区约 600）不横向溢出。左栏 WPF 最小列宽 320（设计稿 300 为 Web 演示口径）。
3. **产物时间戳**：最终 Debug DLL 为 `2026-10-20 03:41:48`，189,952 bytes；`Modules` 与 bin 一致。
4. **验证输出**：构建 **0 错误 / 0 警告**；`--plugin-views Plugin.Matching` **11/11 PASS**；`FlowCanvasChecks --no-build` 首次出现 1 条既有波动断言失败（「★运行中：两条分支胶囊 IsRunning=true」→ 分支1=False 分支2=True），重跑 **2691 通过 / 0 失败**；该断言与 Matching 改动无关。
5. **Release 提示**：见 §7-8，本次只构建 Debug，发布前需重建 Release。

## 9. A 组遗留问题修复（2026-10-20）

| ID | 改动 |
| --- | --- |
| MA1 | `MatchingView.xaml` 根 `Grid` 加 `Typography.NumeralAlignment="Tabular"`。 |
| MA2 | 模板库两处 `DataTrigger Binding="{Binding Model}" Value=""` 各补 `Value="{x:Null}"` 同效果触发器（灰点 / 「未学习」）；`Model` setter 未改。 |

**未做**：MA4（状态胶囊圆点、按钮装饰图标、帮助 info 图标）按约定不做。

**验证**：`dotnet build Plugins\Plugin.Matching\Plugin.Matching.csproj -v m` → **0 错误 / 0 警告**；`--plugin-views Plugin.Matching` → **11/11 PASS**（`elements=495`、XAML hex=0、`OnConfirm InputValues=15`）。

## 10. 复跑命令

```text
dotnet build Plugins\Plugin.Matching\Plugin.Matching.csproj -v m
dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.Matching
dotnet run --project FlowCanvasChecks --no-build
```

## 11. 回滚

- 视图/代码回滚：`git revert` 本插件的 `MatchingView.xaml` / `MatchingView.xaml.cs`；`MatchingPlugin.cs` 的 nullable 注解改动可一并回滚（不影响运行期）。
- 设计基线：保留 `docs/UI设计基线/matching/` 作为 v1 参考，不回滚。
- `Modules\Plugin.Matching.dll`：从上一版构建产物恢复后重启宿主。
