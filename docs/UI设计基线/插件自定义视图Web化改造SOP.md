# 插件自定义视图 Web 化改造 SOP

| 项 | 内容 |
| --- | --- |
| 版本 | v1.0 |
| 日期 | 2026-10-20 |
| 参考实现 | `Plugin.ImageAcquisition` V2（`docs/UI设计基线/image-acquisition/`） |
| 适用对象 | `Plugins\` 下实现 `Core.Interfaces.IPluginCustomViewProvider` 的插件 |
| 产出物 | WPF 视图/代码、公共令牌、UI 冒烟、渲染证据、设计基线、code-changes 记录、`Modules` 产物 |

## 0. 目的与结果定义

把插件的**自定义配置视图**按 Web 设计基线重做，同时保持**功能入口、数据绑定、持久化与宿主生命周期不变**。
"改完"的定义不是"界面看起来像 Web"，而是：

1. 视觉与交互接近设计基线（布局、间距、颜色、字号、五态）；
2. 原有功能全部仍可用（端口、变量链接、命令、试运行、确认/取消、相机/网络/HALCON 等）；
3. 有**可复跑的断言**证明第 1、2 条，不靠肉眼；
4. 设计基线、改动记录、验证证据、回滚路径齐全。

## 1. 适用范围

### 1.1 适用

- 插件类实现 `IPluginCustomViewProvider`，并通过 `GetConfigView(IStepConfigData)` 返回自带 `UserControl` 的插件。
- 视图内使用 `LinkableValueEditor`、`ImageReadOnly` / `ImageEdit` / 其它 HALCON 控件、`NumericBox`、`PropertyGrid`、`DataGrid`、`TreeView`、`TabControl`、`Expander` 等控件的插件。
- 宿主 `PluginConfigShellView` 只作为**统一外壳**复用；本 SOP 不修改它。

### 1.2 不适用

- 没有自定义视图、走框架 `AutoPortConfigView` 的插件（不改；该兜底面板另有独立设计基线 `docs/UI设计基线/auto-port-config/`）。
- 纯算法、相机驱动、运动驱动、通信驱动等无配置界面的插件（不改）。
- 宿主主界面 `Shell.xaml`、`ProcessView.xaml` 等非插件视图（走各自的全局基线）。
- 需要新增功能/契约才能做的项（加载骨架、尺寸/文件大小、放大缩小、打开文件夹、导航到系统设置等）——**先列入延后清单，不混进纯 UI 改造**。

### 1.3 参考实现

`Plugin.ImageAcquisition` V2 是唯一参考实现：

- 设计基线：`docs/UI设计基线/image-acquisition/README.md` + `design.html`；
- 代码：`Plugins\Plugin.ImageAcquisition\ImageAcquisitionView.xaml` / `.xaml.cs`；
- 记录：`docs/code-changes/2026-10-20-图像采集配置窗口V2复刻与FluentChipDanger重复键修复.md`；
- 证据：`docs/UI设计基线/image-acquisition/evidence/after-render-v2-*.png`；
- 断言：`UIThemeSmokeTest --image-acquisition`（24 条）。

## 2. 前置条件

| # | 条件 | 检查方式 |
| --- | --- | --- |
| P1 | 插件清单已确认（实现 `IPluginCustomViewProvider`，且 `GetConfigView` 有实现） | 两条 grep 交叉核对，见 §5.0 |
| P2 | 该插件的 Web 设计基线已产出并获用户确认（或复用共享 V2 基线 + 插件页 README） | `docs/UI设计基线/<plugin>/` |
| P3 | 公共令牌可覆盖设计颜色 | `UI/Controls/Themes/Colors.xaml`、`PluginConfigColors.xaml`；缺的只许追加 |
| P4 | 构建环境可用；VisionMaster/VS 未锁 `Modules`（或已确认可关闭） | `dotnet build`；`Modules\Plugin.*.dll` 可写 |
| P5 | 已读懂该插件的功能契约 | `GetConfigView`、`Initialize`、`OnConfirm`、`Dispose`、`SetConfigContext`、端口/命令 |
| P6 | 已备份/可回滚 | git 干净；当前视图/代码可 revert |
| P7 | `UIThemeSmokeTest` 与 `FlowCanvasChecks` 可跑 | 参考实现的命令见 §7 |

## 3. 角色与职责

按 `AGENTS.md` §4（R10-R15）：

| 角色 | 职责 |
| --- | --- |
| 主会话 | 盘点、拆批、派工、整合交付、最终汇总；不把多域工作揉成一次请求 |
| `ui` | XAML/样式/令牌/渲染证据；按设计基线复刻 |
| `hmi` / 领域角色 | 插件 VM/端口/命令/宿主契约的核对与实现 |
| `reviewer` | 只读审查：红线（插件位置、公共契约不进 Modules、改插件必重建）、绑定与生命周期 |
| `docs` | 设计基线 README、code-changes、索引维护 |
| 用户 | 设计基线确认、差异接受、真机验收 |

**省请求纪律**：独立读取/搜索同批并行；派工单自带"目标文件、行号、已知结论、验收标准"；不重复读同一文件；构建/验证集中在最后一两轮。

## 4. 输入与输出

### 输入

- 插件项目、`GetConfigView`、视图 XAML/code-behind、插件 VM（常与插件类合一）。
- 现有功能契约：`InputPort`/`OutputPort`、`[StepConfig]`、命令、`IStepConfigData`、宿主上下文。
- 设计基线（`docs/UI设计基线/<plugin>/design.html` + README）。
- 公共令牌字典。

### 输出

- 重写后的 `*.xaml` / `*.xaml.cs`。
- 新增的公共令牌（只追加、不改既有键）。
- `UIThemeSmokeTest` 的 `--<plugin>` / `--<plugin>-render` 模式与断言。
- 渲染 PNG（`docs/UI设计基线/<plugin>/evidence/`）。
- 设计基线 README（v2/复刻状态/偏差）、`docs/code-changes/` 记录、主索引。
- 刷新后的 `Modules\Plugin.<Name>.dll`。

## 5. 逐步操作顺序

### 5.0 盘点与分级（只读）

1. 找出所有自定义视图提供者：

```text
Get-ChildItem Plugins -Recurse -Filter *.cs |
  Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
  Select-String 'IPluginCustomViewProvider'
```

2. 交叉核对所有 `GetConfigView` 实现：

```text
Get-ChildItem Plugins -Recurse -Filter *.cs |
  Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
  Select-String 'GetConfigView'
```

3. 每个插件记录：项目、Provider 文件、视图 XAML、行数、`h:` 控件数、`LinkableValueEditor` 数、`NumericBox`/`PropertyGrid`/`DataGrid`/`TreeView`/`TabControl` 数。
4. 分级：S（纯表单，无 HALCON 画布）/ M（单画布或 ROI）/ L（多画布、数据网格、复杂属性面板）。
5. 明确**不改造清单**（无自定义视图的项目/类），写入计划文档。

### 5.1 读代码与耦合矩阵

对目标插件逐项读：`*.xaml`、`*.xaml.cs`、插件类、宿主 `PluginConfigShellViewModel`、`VisionPluginBase`、`LinkableValueEditor`/`ImageReadOnly` 等共享控件。

产出耦合矩阵（模板）：

| 区域/元素 | 当前绑定/契约 | 功能依赖 | 可重构级别 |
| --- | --- | --- | --- |
| 根布局 | 无 | 纯布局 | 自由 |
| 模式选择 | `IsChecked → Mode` + `EnumConverter` | `OnModeChanged` 清预览/校验/刷目录；`Mode` 是 `[StepConfig]` | 只改外观，必须写 `Mode` |
| 路径/索引/超时 | `LinkableValueEditor` 的 `Port`/`StepData`/`BrowseCommand` | 端口持久化、类型转换、变量链接、解除链接 | 只能包裹，不能替换 |
| 预览 | `ImageReadOnly.HImage` | HALCON 显示/缩放/右键菜单/释放语义 | 保留控件；用按需宿主，不折叠已有图 |
| 相机 | `SelectedItem = SelectedCameraOption` | setter 写 `CameraSerial` + 校验 | 保留对象绑定 |
| 显示窗口 | `SelectedItem = DisplayViewIndex` | `[StepConfig]` int + 夹取 0~9 | 保留 int |
| 命令 | `Browse*/Refresh/Prev/Next/PushLocalImage` | 真实功能入口 | 保留命令绑定 |
| 状态 | `StatusMessage`/`StatusLevel` | Info/Warning/Error 三档 | 保留三档；不加第四档 |

**判定原则**：任何"控件是某个端口/命令/配置属性的唯一入口"的行，标为"只能包裹/改外观"，不许重写。

### 5.2 Web 设计基线

1. 在 `docs/UI设计基线/<plugin>/` 建 `design.html` + `README.md`（自包含、无外部依赖；版本 v1）。
2. 视觉语言沿用 ImageAcquisition V2：两栏/卡片/36px 控件/12px 标签/11.5px 帮助文字/8px 圆角/公共令牌/五态。
3. README 必须写：版本、确认日期/人、覆盖范围、设计要点、令牌映射、复刻约束、已知偏差、未纳入项。
4. **每个插件都出完整 `design.html`**（2026-10-20 用户裁定，严格 R127/R128）：不能只复用一张共享图；可用 ImageAcquisition V2 的 CSS/布局作为模板，但内容、字段、控件、状态必须按该插件实际视图重画。
5. **先确认再改 WPF**（R127）；批量改造中用户已确认 V2 视觉语言与流程，插件页 README 记录"沿用 V2 基线"即可，但设计页仍要逐插件产出。
6. 设计基线确认后只读，改动升 v2/v3。

### 5.3 令牌映射

1. 把设计里的每个颜色映射到 `Colors.xaml` / `PluginConfigColors.xaml` 的公共键。
2. 缺的语义键只**追加**到公共字典；不改既有键/值；不新增第二套同义蓝/灰。
3. 视图 XAML 内 **0 个十六进制颜色**；静态断言必须能证明。
4. 图标按钮 30×30 + `Padding="0"`（R68）；焦点 `DialogFocusVisual`；ToolTip `DialogToolTipStyle`（R135）。
5. 等宽数字用 `Typography.NumeralAlignment="Tabular"`。

### 5.4 XAML 重写

**必须保留**

- 根 `UserControl`、`MinWidth/MinHeight`（不写死 900×500）。
- `DataContext = 插件实例`（宿主靠它取 `IPluginConfigView`）。
- `LinkableValueEditor` 的 `Port`/`StepData`/`BrowseCommand`。
- HALCON 控件（`ImageReadOnly` / `ImageEdit`）及其 HImage/ROI 绑定。
- `Mode`、`SelectedCameraOption`、`DisplayViewIndex` 的绑定与类型。
- 所有 `Command`、`Text`、`SelectedItem` 绑定。
- `StatusMessage`/`StatusLevel` 三档。

**HALCON 预览的硬规则（本轮踩坑）**

- **不要**在无图时把 `ImageReadOnly` 设为 `Visibility=Collapsed`，有图再恢复：控件在折叠态收到 `HImage`，恢复后可能不重绘，表现为"打开图片不刷新"。
- 正解：`ContentControl x:Name="PreviewHost" Content="{Binding PreviewImage}"` + `DataTemplate` 内 `h:ImageReadOnly HImage="{Binding Mode=OneWay}"`。
  **有图才创建控件**，创建时即拿到图；无图时模板不实例化，空态/错误态是纯 WPF 覆盖层。
- `HImage` 依赖属性是 `BindsTwoWayByDefault`：DataTemplate 里写裸 `{Binding}`（无 Path）会抛
  `XamlParseException: 双向绑定需要 Path 或 XPath`；必须显式 `Mode=OneWay`。
- 模板内控件不在视图 namescope：工具条 `FitToWindow()`/`FitToImage()` 用 `FindVisualChild<ImageReadOnly>(PreviewHost)` 取当前实例；无图返回 null，静默空跑。
- 多画布/ROI 视图（Calibration/CreateRoi/CaliperMeasure/Matching/BlobDetect/BeadInspect 等）同样遵守"不要在已设图后折叠再恢复"；优先保持画布常驻或按需创建。

**样式**

- 用 `DialogInput` / `DialogCombo` / `DialogFocusVisual` / `DialogToolTipStyle`；视图内定义局部样式（`LocalButtonStyle`、`ModeCardStyle` 等）。
- **不改共享控件/共享样式**（`LinkableValueEditor`、`ImageReadOnly`、`HalconBase`、`PluginConfigStyles.xaml`、`VerTabControl`）。
- 不做"死按钮"：只接现有命令/公开方法；需要新功能的按钮进延后清单。

### 5.5 code-behind

- 保留构造签名、`DataContext = plugin`、`plugin.Initialize(stepData)`（参考 `ImageAcquisitionView.xaml.cs`）。
- 只加 UI 转换器/事件处理器：文件名/模式/状态文案、Fit/1:1、聚焦、展开折叠区。
- **不引入独立 VM**；若必须分 VM，VM 要实现 `IPluginConfigView` 并委托给插件，否则宿主 `_pluginView` 取不到 `OnConfirm`。
- 模板内控件通过可视树取，不写 `x:Name` 依赖。

### 5.6 构建

```text
dotnet build Plugins\<Project>\<Project>.csproj -v m
```

- 必须 0 错误 / 0 警告（本插件）。
- 检查 `Modules\<Plugin>.dll` 时间戳；若被 VisionMaster/VS 锁定：
  - 关闭宿主/VS 后重试；不要擅自杀进程；
  - 首次失败是环境性，重试成功后记录"首次锁定、重试通过"。
- 改了 `Help\` 内嵌资源要 `--no-incremental` 并核对 DLL 内资源名（R42/R62）；本 SOP 默认不改帮助内容。

### 5.7 UI 冒烟断言

在 `UIThemeSmokeTest` 增加 `--<plugin>` 模式（参考 `--image-acquisition`）：

**静态**

- XAML 可读；`hex=0`；关键绑定字符串齐全；模式卡片仍用 `EnumConverter`；共享样式未被修改（diff/scan）。

**运行时**

- `Application` + 主题资源下实例化视图；`DataContext` 是插件；元素数合理。
- 关键控件存在：`LinkableValueEditor` 数/`Port` 名、HALCON 控件宿主、模式卡片、相机/显示下拉、命令按钮、推送字段、状态栏。
- 绑定类型：`Mode` 枚举、`SelectedCameraOption` 对象、`DisplayViewIndex` int；`EnumConverter` 双向点击验证。
- 功能回环：端口编辑 → `OnConfirm` 写回 `InputValues` 的**键与值**；模式切换清预览；相机序列号；显示窗口 int。
- HALCON 预览：造一张小 `HImage` → 验证宿主 `Content` 绑 `PreviewImage`、`ImageReadOnly` 由模板创建且 `HImage` 引用相等。
- 负向断言：至少一条"改坏就红"的变异反证（R38/R65）。

**批量公共冒烟**

- `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views <ProjectFilter>`：
  从 `Modules\Plugin*.dll` 反射找 `IPluginCustomViewProvider`，真实实例化视图并检查
  `DataContext`/`IPluginConfigView`、`LinkableValueEditor.Port`、`Button.Command`、`ComboBox`/`TextBox` 绑定、
  `OnConfirm` 可执行、项目内 XAML `hex=0`，并渲染初始态 PNG 到 `docs/UI设计基线/_batch-evidence/`。
- 这是批量改造的公共基线；**不替代**该插件的关键业务断言（端口名/命令名/画布交互）与 reviewer 复核。
- 每个插件仍建议补一条插件特定断言或 reviewer 重点项，尤其是多画布/ROI/数值参数插件。

**渲染**

- `--<plugin>-render` 输出 4~5 张 PNG（主态/空态/错误态/其它模式）到 `docs/UI设计基线/<plugin>/evidence/`；
  批量模式可用 `--plugin-views` 的初始态 PNG 作为第一版证据。
- `RenderTargetBitmap` 不捕获 HALCON `HwndHost`：证据图展示空态/错误态布局；真实图像预览需真机窗口。

### 5.8 回归 gate

```text
dotnet build FlowCanvasChecks\FlowCanvasChecks.csproj -v q
dotnet run --project FlowCanvasChecks --no-build
dotnet run --project UIThemeSmokeTest --no-build -- --<plugin>
dotnet run --project UIThemeSmokeTest --no-build -- --<plugin>-render
```

- `FlowCanvasChecks` 0 失败；`UIThemeSmokeTest` 插件冒烟全 PASS。
- `UIThemeSmokeTest` 退出码恒为 1（既有 GridView 红灯，R46）；**以日志行判定**，不看退出码。
- 改过令牌/资源合并链：跑资源断链契约（R30-R32）与 `UIThemeSmokeTest` 默认资源合并。
- 改过断言宿主源码：必须 build 再 run，不许 `--no-build` 跑旧二进制（R65）。

### 5.9 文档与部署

1. 更新 `docs/UI设计基线/<plugin>/README.md`：版本、复刻实现映射、令牌新增、证据、已知偏差、延后项。
2. 新增 `docs/code-changes/YYYY-MM-DD-<plugin>-Web化.md`：改动文件、契约保留、验证、差异、遗留。
3. 更新 `docs/UI设计基线/README.md` 索引。
4. 刷新 `Modules\<Plugin>.dll`；重启 VisionMaster 后真机打开配置窗口，按 §7 手工回环。
5. 单插件记录模板见 §10；最终汇总见 §11。

## 6. 涉及文件与配置

| 类别 | 文件 | 说明 |
| --- | --- | --- |
| 插件视图 | `Plugins\<Project>\<View>.xaml` | 主改造文件 |
| 插件 code-behind | `Plugins\<Project>\<View>.xaml.cs` | 只加 UI 转换器/处理器 |
| 插件类 | `Plugins\<Project>\<Plugin>.cs` | 只在必要时改 `GetConfigView` 返回类型；不改业务逻辑 |
| 工程 | `Plugins\<Project>\<Project>.csproj` | 仅当新增资源/引用；Help 资源不改 |
| 公共令牌 | `UI\Controls\Themes\Colors.xaml`、`PluginConfigColors.xaml` | 只追加语义键 |
| UI 冒烟 | `UIThemeSmokeTest\Program.cs`、`UIThemeSmokeTest.csproj` | 新增 `--<plugin>` / `--<plugin>-render` 与工程引用 |
| 设计基线 | `docs\UI设计基线\<plugin>\design.html`、`README.md`、`evidence\` | 设计媒介，不进构建 |
| 记录 | `docs\code-changes\YYYY-MM-DD-<plugin>-Web化.md` | 改动/验证/差异/遗留 |
| 部署 | `Modules\<Plugin>.dll` | 必须刷新；锁定时关闭宿主重试 |
| 不改 | `VisionMaster\Views\DialogViews\PluginConfigShellView.xaml`、`LinkableValueEditor`、`ImageReadOnly`、`HalconBase`、`PluginConfigStyles.xaml` | 共享件，单独全局基线 |

## 7. 验证方式

| 层 | 判据 | 证据 |
| --- | --- | --- |
| 构建 | 插件工程 0 错误 / 0 警告；`Modules` DLL 时间戳更新 | 构建日志 + `dir` |
| 静态 | 视图 XAML `hex=0`；关键绑定字符串齐全；无共享样式改动 | `--<plugin>` 静态断言 |
| 运行时 | `DataContext` = 插件；关键控件/绑定/命令齐全；端口编辑 → `OnConfirm` 键值正确；模式/相机/显示窗口类型正确 | `--<plugin>` 运行时断言 |
| 渲染 | 4~5 张 PNG 与设计基线逐块对照；HALCON HwndHost 不计入像素 | `evidence/` |
| 回归 | `FlowCanvasChecks` 0 失败；`UIThemeSmokeTest` 插件冒烟全 PASS | gate 日志 |
| 真机 | 打开配置 → 改每个字段 → 确认 → 重开；链接/解除链接；试运行；关闭无崩溃/泄漏 | 用户验收 |
| 差异 | 每处与设计基线的偏差写入 README「已知偏差」 | README |

**"跳过"型断言必须能被反证**（R38）：写"本环境不支持"之前先证明不是代码 bug；能改硬断言就改硬断言。

## 8. 回滚方案

| 场景 | 回滚动作 |
| --- | --- |
| 单个插件改造失败 | `git revert` 该插件的 XAML/code-behind/工程改动；设计基线保留为 v1 参考 |
| 公共令牌导致全局异常 | 只回滚新增键（若其它插件未引用）；保留既有键不动 |
| `Modules` DLL 异常 | 从上一版构建产物/版本库恢复 `Modules\<Plugin>.dll`；重启宿主 |
| UI 冒烟新增模式阻塞构建 | 先回滚该模式（保持插件 UI），或修到 0 错误；不要把失败断言删成"跳过" |
| 并发改动冲突 | 先看文件 `LastWriteTime`；不覆盖并发进程的改动；必要时只做最小键名重命名并记录（参考 `DialogChipDanger` 教训） |
| 设计基线需要调整 | 升版本（v1→v2），同步 README/映射表/已复刻页面；不直接改已冻结基线 |

**禁止**：只回滚一半（XAML 回滚、code-behind 保留）造成绑定/处理器悬空；先确认两者配套。

## 9. 注意事项（踩坑清单）

1. **`DataContext` 必须是插件实例**：宿主 `viewObj.DataContext as IPluginConfigView` 取 `OnConfirm`；独立 VM 不实现接口 = 确认不保存。
2. **`LinkableValueEditor` 只包裹不替换**：`Port`/`StepData`/`BrowseCommand` 是端口持久化、类型转换、变量链接、解除链接的唯一入口。
3. **HALCON 控件不要用 Collapsed 显隐**：有图才创建（`ContentControl` + `DataTemplate`），`HImage="{Binding Mode=OneWay}"`；裸 `{Binding}` 会因 TwoWay 无 Path 抛 `XamlParseException`。
4. **类型不能换**：`Mode` 枚举、`SelectedCameraOption` 对象、`DisplayViewIndex` int；`EnumConverter` 双向。
5. **`StatusLevel` 只有三档**：Info/Warning/Error；加载态要加 `PreviewState`/`IsPreviewLoading`，属于 VM 改动，先延后。
6. **不要造死按钮**：只接现有命令/公开方法；新增功能（放大缩小/打开文件夹/导航）进延后清单。
7. **共享件不动**：`LinkableValueEditor`、`ImageReadOnly`、`HalconBase`、`PluginConfigStyles.xaml`、`VerTabControl`；只在本视图派生/包裹。
8. **图标按钮**：30×30 + `Padding="0"`（R68）；焦点 `DialogFocusVisual`；ToolTip `DialogToolTipStyle`（R135）。
9. **宿主外壳/MinHeight 不改**：`PluginConfigShellView` 是所有插件共用；640×420 的 MinHeight 决策单独做。
10. **`Modules` 锁**：VisionMaster/VS 运行时可能锁 DLL；关闭宿主重试，不擅自杀进程；构建 0 错误 ≠ 产物已投递（红线③）。
11. **渲染证据边界**：`RenderTargetBitmap` 不捕获 HALCON `HwndHost`；证据图只证明 WPF 布局，真实预览需真机窗口。
12. **并发改动**：改共享字典前看 `LastWriteTime`；同一键被两个进程定义会抛 `ArgumentException`（本轮 `DialogChipDanger` 实例）；最小改名并记录。
13. **UI 冒烟退出码**：`UIThemeSmokeTest` 恒为 1（既有 GridView 红灯）；以日志 PASS/FAIL 行为准（R46）。
14. **改断言源码必须 build 再 run**（R65）；`--no-build` 跑旧二进制会假绿。
15. **宿主 MinHeight**：2026-10-20 已统一提高到 **560**（`PluginConfigShellView.xaml`）；640×560 为最小档，640×420 不再允许。设计稿仍以 960×680/640×680 为主档，640×560 需检查左栏滚动与预览高度。
16. **先织网再迁移**：复杂画布插件先补像素级/绑定级断言，再改 UI（R38 的经验）。

## 10. 单插件记录模板

每个插件一份，建议直接复制到 `docs/code-changes/YYYY-MM-DD-<plugin>-Web化.md`：

```markdown
# YYYY-MM-DD <Plugin> 自定义视图 Web 化

## 范围
- 设计基线：docs/UI设计基线/<plugin>/README.md vX
- 覆盖：<View>.xaml + <View>.xaml.cs
- 不在范围：...

## 功能契约保留
- DataContext = 插件实例 / IPluginConfigView：...
- LinkableValueEditor：...（列出端口）
- HALCON 控件：...
- 关键绑定：Mode / SelectedItem / Command / Status...
- 命令：...

## 改动点
1. ...
2. ...

## 与 ImageAcquisition V2 的差异
- ...

## 验证
- 构建：0/0
- UI 冒烟：N/N
- 渲染：N 张 PNG
- gate：N 通过 / 0 失败
- 真机：...

## 遗留问题
- ...
```

## 11. 完成定义（DoD）

- [ ] 设计基线 v1 已确认（或共享基线 + 插件 README）；
- [ ] 视图 XAML `hex=0`，公共令牌齐全；
- [ ] `DataContext`/`IPluginConfigView`/`Initialize`/`OnConfirm` 未变；
- [ ] `LinkableValueEditor`/HALCON 控件/端口/命令/类型全部保留；
- [ ] 插件构建 0 错误 / 0 警告，`Modules` DLL 已刷新；
- [ ] UI 冒烟全 PASS（含负向/变异反证）；
- [ ] 渲染证据 4~5 张，设计基线 README 已更新；
- [ ] `FlowCanvasChecks` 0 失败；
- [ ] `docs/code-changes/` 记录完成；
- [ ] 真机打开 → 改 → 确认 → 重开回环通过；
- [ ] 遗留问题已列明并经用户接受。

## 12. 参考实现对照（ImageAcquisition V2）

| SOP 步骤 | 参考实现落点 |
| --- | --- |
| 5.0 盘点 | 22 个自定义视图插件（见清单文档） |
| 5.1 耦合矩阵 | `docs/code-changes/2026-10-20-图像采集配置窗口V2复刻与FluentChipDanger重复键修复.md` §3 |
| 5.2 设计基线 | `docs/UI设计基线/image-acquisition/` |
| 5.3 令牌 | `Colors.xaml` 5 个 Dialog 键 + `PluginConfigColors.xaml` 5 个预览键 |
| 5.4 XAML | `ImageAcquisitionView.xaml`（2×2 卡片/单页/预览工作区/ContentControl 宿主） |
| 5.5 code-behind | `ImageAcquisitionView.xaml.cs`（3 个转换器 + 4 个 UI 处理器 + `FindVisualChild`） |
| 5.6 构建 | 0 错误 / 0 警告；Modules 首次锁、重试通过 |
| 5.7 冒烟 | `UIThemeSmokeTest --image-acquisition` 24/24 |
| 5.8 渲染 | `--image-acquisition-render` 5 张 PNG |
| 5.9 gate | `FlowCanvasChecks` 2691/0 |
| 5.10 文档 | 基线 README v2 + code-changes §1-§9 |
