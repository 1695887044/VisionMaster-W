# 2026-10-20 CodeReader 自定义视图 Web 化

## 1. 范围

| 项 | 内容 |
| --- | --- |
| 插件 | `Plugin.CodeReader`（码读取） |
| 设计基线 | `docs/UI设计基线/code-reader/README.md` v1 + `design.html` v1 |
| 覆盖 | `Plugins\Plugin.CodeReader\CodeReaderView.xaml`、`CodeReaderView.xaml.cs` |
| 附带 | `CodeReaderPlugin.cs` 仅把 `InputPort<HImage>` 的 `null` 改为 `null!`（消除 CS8625，无行为变化） |
| 不在范围 | 插件业务逻辑、端口名、命令名、持久化、帮助手册、共享控件/共享样式/共享字典、其它插件、`PluginConfigShellView` |
| 设计语言 | 沿用 ImageAcquisition V2：两栏 / 36px 控件 / 12px 标签 / 11.5px 帮助 / 8px 圆角 / 公共令牌 / 五态 |

## 2. 耦合矩阵（改造前 → 改造后）

| 区域/元素 | 当前绑定/契约 | 功能依赖 | 可重构级别 | 改造后 |
| --- | --- | --- | --- | --- |
| 根布局 | 无 | 纯布局 | 自由 | 两栏 `Grid`（300~356 + 1.6*），根 `MinWidth=560 / MinHeight=280`，不再写死 900×560 |
| 图像输入 | `cv:LinkableValueEditor Port="{Binding Image}" StepData="{Binding StepData}" Label="输入图像"` | HImage 端口持久化、变量链接、`SetLink/RemoveLink`；`Image` 是唯一图像来源 | **只能包裹**，不能替换 | 保留 `Port`/`StepData`，`Label=""` 外置标签，`Height=36`；不设 `BrowseCommand`（插件没有浏览命令） |
| 码制 | `ComboBox ItemsSource="{Binding SymbologyOptions}" SelectedValue="{Binding Symbology}" SelectedValuePath="HalconName" DisplayMemberPath="DisplayName"` | `Symbology` 是 `[StepConfig]`，存 HALCON 原始名；切码制会重建模型 | 只改外观，必须写 `Symbology` | 保留 `ItemsSource`/`SelectedValue`/`SelectedValuePath`；改用显式 `ItemTemplate`（`DialogCombo` + `DisplayMemberPath` 在选中框显示类型全名，见 §5） |
| 码制提示 | `Text="{Binding SymbologyHint}"` | 由 `Symbology` setter 触发 `OnPropertyChanged` | 只改外观 | 保留绑定；放在信息图标帮助行 |
| 码制家族 | `Is2D`（bool，派生自 `Symbology`） | 控制二维码参数卡可见性 | 只改外观 | 保留 `Is2D`；新增家族徽标（DataTrigger 切「二维码/一维码」） |
| 二维码参数 | `Visibility="{Binding Is2D, Converter={StaticResource BoolToVis}}"`；`ComboBox ItemsSource="{Binding PolarityOptions}" SelectedValue="{Binding Polarity}" SelectedValuePath="Value"` | `Polarity` 是 `[StepConfig]`，仅二维码有效 | 只能包裹 | 保留 `Is2D` 可见性与 `SelectedValue`；改用显式 `ItemTemplate` |
| 搜索超时 | `TextBox Text="{Binding TimeoutMs}"` | `TimeoutMs` 是 `[StepConfig]` int；0 = 不限 | 只能包裹 | 保留 `Text` 绑定；`DialogInput` + 36px |
| 试算 | `Button Click="OnTryRecognizeClick"` → `Plugin.TryPreviewRecognize()` | 与运行共用同一条 `TryRead`；失败/无码结论写入 `ResultSummary` | 只能包裹 | 改为 `RelayCommand` 转发同一方法（`TryRecognizeCommand`），行为不变；命令让宿主/冒烟能按 Command 契约发现 |
| 提示 | `Text="{Binding Hint}"` | 上游图显示/试算前提示；由插件 setter 更新 | 只改外观 | 保留绑定；从试算卡移到右侧预览状态栏，避免与结果混在一起 |
| 结果 | `Text="{Binding ResultSummary}"`（Consolas 11） | 试算输出；成功/无码/失败三态文字 | 只改外观 | 保留绑定；独立结果区，等宽 10.5px；UI 转换器按文本前缀做视觉配色（不新增状态枚举） |
| 预览 | `h:ImageReadOnly HImage="{Binding DisplayImage, Mode=OneWay}"` | HALCON 显示/缩放/右键菜单；`DisplayImage` 是插件持有的上游图副本 | **保留控件**；不能折叠后恢复 | `ContentControl PreviewHost Content="{Binding DisplayImage}"` + `DataTemplate` 内 `h:ImageReadOnly HImage="{Binding Mode=OneWay}"`；有图才创建 |
| 预览工具条 | 无 | `HalconBase.FitToWindow()` / `FitToImage()` 是公开方法 | 可加 UI 入口 | 只加「适应窗口 / 1:1」，从 `PreviewHost` 可视树取当前 `ImageReadOnly`；无图静默空跑 |
| 输出端口 | 无 UI | `Codes` / `Count` / `Text` 运行期写入 | 可加只读说明 | 静态 chips，不新增可编辑控件/绑定 |
| Mode | **不存在** | — | 不添加 | 不添加 |
| SelectedItem | **不存在**（两个下拉都用 `SelectedValue`） | — | 不添加 | 不添加 |
| StatusLevel | **不存在**（只有 `Hint` / `ResultSummary`） | — | 不添加 | 不添加；状态点用 `ResultSummary` 文本做视觉映射 |

### 宿主契约（未改）

| 契约 | 状态 |
| --- | --- |
| `CodeReaderPlugin.GetConfigView(stepData)` 返回 `new CodeReaderView { DataContext = this }` | 未改 |
| `CodeReaderView` 构造签名 / `InitializeComponent` / `Loaded → plugin.OnViewLoaded()` | 保留 |
| `DataContext = CodeReaderPlugin`，`DataContext as IPluginConfigView` 可取 `OnConfirm` | 保留（`VisionPluginBase` 实现 `IPluginConfigView`） |
| `Initialize(IStepConfigData)` / `OnConfirm(IStepConfigData)` / `OnCancel()` | 未改 |
| `Image` 端口 `InputPort<HImage>`、`IsRequired=false` | 未改（仅 `null!` 标注） |
| `Symbology` / `TimeoutMs` / `Polarity` 三个 `[StepConfig]` | 未改 |
| `SymbologyOptions` / `PolarityOptions` / `Is2D` / `SymbologyHint` / `Hint` / `ResultSummary` / `DisplayImage` | 未改 |

## 3. 改动点

1. **整页重写 `CodeReaderView.xaml`**：左 356px 配置轨 + 右只读预览工作区；配置轨内部分组（图像输入 / 码制 / 二维码参数 / 通用参数 / 输出端口），**试算固定在左栏底部**（主操作常驻，配置区独立滚动）。
2. **图像输入只保留唯一入口**：`LinkableValueEditor` 绑定 `Image` 端口；`Label=""`、`Height=36`；不新增「浏览」/「载入」按钮（插件没有这些命令）。
3. **码制/极性下拉改用显式 `ItemTemplate`**：保留 `SelectedValue` + `SelectedValuePath`；修复 `DialogCombo` 下 `DisplayMemberPath` 在选中框显示 `Plugin.CodeReader.CodeSymbology` / `PolarityOption { ... }` 的问题（改为 `Text="{Binding DisplayName}"` 的 `DataTemplate`）。
4. **家族徽标**：`Is2D` 驱动「二维码/一维码」文字与底色；二维码参数卡仍由 `Is2D` 控制 `Visibility`。
5. **预览升级为只读工作区**：`ContentControl PreviewHost` + `DataTemplate` 内 `h:ImageReadOnly HImage="{Binding Mode=OneWay}"`；无图时纯 WPF 空态；有图才创建控件，**没有** `Visibility=Collapsed` 显隐已设图控件。
6. **工具条只接既有方法**：`FitWindow_Click` / `FitImage_Click` 从 `PreviewHost` 可视树取当前 `ImageReadOnly`，调用 `FitToWindow()` / `FitToImage()`；无图返回 null 静默空跑。
7. **结果区独立**：`ResultSummary` 放独立等宽结果框；`ResultSummaryBrushConverter` 只按文本前缀返回公共令牌画刷（成功/无码/失败/中性），不新增 `StatusLevel`、不改插件状态。
8. **状态栏**：左侧 `Hint`，右侧当前 `Symbology`；状态点颜色由 `ResultSummary` 视觉映射；不伪造三档状态。
9. **全令牌化**：颜色全部走 `Dialog*` / `PluginPreview*` / `ScadaSecondaryTextBrush` / `PluginMonoFontFamily`；视图 XAML **hex=0**；未修改共享字典。
10. **`CodeReaderPlugin.cs` 一条标注**：`new("Image", null, ...)` → `new("Image", null!, ...)`，消除 CS8625；无运行时行为变化。
11. **`CodeReaderView.xaml.cs`**：保留构造/`DataContext`/`Loaded`；新增 `TryRecognizeCommand`（`Core.Commands.RelayCommand` 转发 `TryPreviewRecognize()`）、`FitWindow_Click` / `FitImage_Click`、`FindVisualChild<ImageReadOnly>`、`ResultSummaryBrushConverter`。**没有引入独立 VM**。

## 4. 与 ImageAcquisition V2 的差异

| 项 | ImageAcquisition V2 | CodeReader v1 |
| --- | --- | --- |
| 模式 | 4 个采集模式卡片（`Mode` 枚举 + `EnumConverter`） | **无 Mode**；只有码制/极性两个下拉 |
| 图像入口 | 文件/目录/网络/相机，4 个 `LinkableValueEditor` + 浏览/刷新/步进命令 | 单一 `Image` 端口，1 个 `LinkableValueEditor`；无浏览/载入命令 |
| 预览控件 | `ImageReadOnly` 按需创建 | 同样按需创建；本插件不做定位，**不用 `ImageEdit`** |
| 预览工具条 | 适应窗口 / 1:1 / 目录刷新 | 适应窗口 / 1:1（无目录刷新） |
| 底部固定区 | 「输出」显示窗口（int） | 「试算」按钮 + 结果区（主操作常驻） |
| 状态 | `StatusMessage` + `StatusLevel` 三档 | **无 StatusLevel**；`Hint` 在状态栏，结果区按 `ResultSummary` 文本做视觉配色 |
| 结果 | 推送结果文本框 / 预览状态 | 独立等宽结果区（`ResultSummary`） |
| 码制下拉 | — | 20 项码制；`ItemTemplate` 显式 `DisplayName`（`DialogCombo` 的 `DisplayMemberPath` 选中框会显示类型全名，本轮踩到并修掉） |
| 输出端口 | 无 | 静态 `Codes` / `Count` / `Text` 说明（只读信息，不新增绑定） |

**与参考实现共用的关键修法**：`ContentControl` + `DataTemplate` + `HImage="{Binding Mode=OneWay}"`，有图才创建；模板内控件不在 namescope，Fit 通过可视树取。

## 5. 踩坑记录

1. **`DialogCombo` + `DisplayMemberPath` 选中框显示类型全名**：`DisplayMemberPath` 在 `DialogCombo` 的 `ContentPresenter`（`SelectionBoxItemTemplate`）路径上没有生成可用模板，选中框直接 `ToString()`。表现为 `Plugin.CodeReader.CodeSymbology` / `PolarityOption { DisplayName = ... }`。修法：两个 ComboBox 都改用显式 `ItemTemplate`（`Text="{Binding DisplayName}"`），保留 `SelectedValuePath`。批量冒烟只检查 `ItemsSource` 非空，不会发现这个显示问题；本轮用渲染 PNG 目视抓到。
2. **`LinkableValueEditor` 的 HImage 行没有 36px 高**：HImage 不可文本输入，内部只显示占位 TextBlock（约 18px），不设高度会比其他控件矮一截。修法：`Height="36"`。
3. **试算区放在滚动区底部会被配置项挤到首屏之外**：改为左栏 `Grid` 第二行固定区；`Hint` 与结果不再重复（`Hint` 只在右侧状态栏）。
4. **`ResultSummary` 没有状态枚举**：不能用 `DataTrigger` 按值枚举三档；用 `IValueConverter` 按文本前缀返回公共令牌画刷，纯视觉、不改业务。

## 6. 验证

| 项 | 命令 / 方法 | 结果 |
| --- | --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.CodeReader\Plugin.CodeReader.csproj -v m` | **0 错误 / 0 警告**；`Modules\Plugin.CodeReader.dll` 已刷新（2026-10-20 01:46:15，94,208 bytes） |
| 冒烟工程构建 | `dotnet build UIThemeSmokeTest\UIThemeSmokeTest.csproj -v m` | **0 错误**；全量重编译显示 **512 个既有 nullable 警告**（`UIThemeSmokeTest\Program.cs` 等，未改该工程源码），另有 `HslCommunication` 的 `System.Resources.Extensions` 解析警告；均与本次改动无关 |
| 插件视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.CodeReader` | **11/11 PASS**：视图实例化 198 elements、`DataContext=CodeReaderPlugin`、`LinkableValueEditor.Port=Image`、`Button.Command=TryRecognizeCommand`、ComboBox/TextBox 绑定、`OnConfirm` 可执行、XAML hex=0、渲染 PNG |
| 补充运行时探针 | 临时探针（`%TEMP%\codereader-probe`，不随仓库） | 3 个 ComboBox：内部枚举框 0 项、码制 20 项选中 `Data Matrix ECC 200` 显示正确、极性 3 项选中 `any` 显示 `自动（两种都试，推荐）`；设置 64×48 `HImage` 后 `ImageReadOnly` 由模板创建且 `HImage` 引用相等；`OnConfirm` 写回 `Symbology=QR Code`、`TimeoutMs=750`、`Polarity=dark_on_light` |
| 设计页 | 本地 `http.server` + 浏览器实测 | 默认/空态/无码/失败、二维码/一维码、960/640 窄档、`试算一下` 均正常；截图见 `evidence/design-default-960.png`、`design-narrow-640.png` |
| 渲染证据 | `docs/UI设计基线/_batch-evidence/Plugin.CodeReader-CodeReaderPlugin.png` 复制到 `docs/UI设计基线/code-reader/evidence/after-render-v1-initial.png` | 初始态布局（空态 + 配置区）；HALCON `HwndHost` 内容不被 `RenderTargetBitmap` 捕获 |
| 常规 gate | `dotnet run --project FlowCanvasChecks --no-build` | **2690 通过 / 1 失败**；失败项为调度类既有偶发断言「跳过留下了 Warn」（`定时真跑流程` 重复触发告警），与 CodeReader 改动无关（本插件未触碰该链路） |

## 7. 遗留问题

1. **设计基线待用户确认**：`docs/UI设计基线/code-reader/README.md` v1 的「确认日期/人」仍为空；确认后按 v1 只读，改动升 v2。
2. **无图试算的遗留文案**：插件现有 `TryPreviewRecognize()` 在无图时返回 `先点「载入」把示意图读进来再试算`；界面上没有「载入」按钮（图只能来自 `Image` 端口）。本版按「不改业务逻辑」保留，帮助手册 `02-界面与端口.md` 已有说明；后续清理文案需同步该帮助页并重建插件（R42/R62）。
3. **加载骨架 / 图像尺寸 / 放大缩小 / 打开文件夹 / 系统导航**：均需新增 VM 属性或契约，未纳入（见设计基线 D1-D5）。
4. **试算结果没有三档状态 / 耗时**：本插件没有 `StatusLevel`；结果区只用文本前缀做视觉配色，不扩展状态枚举。
5. **窄档（640×560）**：左栏配置区需要滚动才能看到二维码参数/通用参数；试算按钮固定可见。真实宿主内容区高度需真机复核。
6. **渲染证据边界**：`ImageReadOnly` 是 HALCON `HwndHost`，批量冒烟 PNG 不包含真实图像；真实预览需真机窗口。
7. **FlowCanvasChecks 1 条既有失败**：非本次改动引入，建议主会话在汇总时按基线复核；若需清零，另派调度/退出链角色处理。
8. **`Modules` 锁**：本轮构建未遇到锁定；若用户机器上 VisionMaster/VS 正在运行，重新构建前先关闭宿主（不要杀进程）。

## 8. 复跑命令

```text
dotnet build Plugins\Plugin.CodeReader\Plugin.CodeReader.csproj -v m
dotnet build UIThemeSmokeTest\UIThemeSmokeTest.csproj -v m
dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.CodeReader
dotnet run --project FlowCanvasChecks --no-build
```

> `UIThemeSmokeTest` 退出码恒为 1（既有 GridView 红灯，R46）；以日志 PASS/FAIL 行为准。
> 改过断言宿主源码必须 build 再 run（R65）；本轮未改 `UIThemeSmokeTest` 源码。

## 9. 遗留项修复（2026-10-20，CRD1 / CRD2 / CRD3）

- **CRD1（窄档状态栏）**：状态栏右侧码制 `TextBlock` 命名 `SymbologyText`；`CodeReaderView.xaml.cs` 在 `SizeChanged` 中按 `ActualWidth < 620` 折叠，把宽度让给 `Hint`。
- **CRD2（码制显示名）**：新增 `CodeSymbologyDisplayConverter`，状态栏 `Text` 走 `CodeSymbologyTable.TryGet(value, out var s) ? s.DisplayName : value`；存盘值仍是 HALCON 原始名，查不到时原样回退（不隐藏配置错误）。
- **CRD3（无图文案）**：`CodeReaderPlugin.TryPreviewRecognize()` 的无图提示改为「先在「图像输入」绑定上游图像，或点宿主「执行」带图进来再试算；本节点没有「载入」按钮，图像只来自上游」，保留「载入」二字以满足既有断言；`docs/UI设计基线/code-reader/README.md` §5/§9 与帮助手册 `Help\02-界面与端口.md` 已同步。
- **产物**：改过 Help 页后按 R62 对 `Plugin.CodeReader` 做 `--no-incremental` 重建；`Modules\Plugin.CodeReader.dll` 内 `.Help.` 资源名 **6 个 = `Help\` 下 6 个 .md**。
- **验证**：`dotnet build Plugins\Plugin.CodeReader\Plugin.CodeReader.csproj -v m` → **0 错误**（警告均来自既有依赖工程，改动的 `.cs`/`.xaml` 无新增）；`dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.CodeReader` → **11 条 PASS / 0 条 FAIL**。
