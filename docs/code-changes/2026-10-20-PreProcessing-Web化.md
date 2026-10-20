# 2026-10-20 Plugin.PreProcessing 自定义视图 Web 化

## 1. 范围

| 项 | 内容 |
| --- | --- |
| 设计基线 | `docs/UI设计基线/pre-processing/README.md` v1 + `design.html`（自包含、可点击） |
| 覆盖 | `Plugins\Plugin.PreProcessing\PreProcessingView.xaml`、`PreProcessingView.xaml.cs` |
| 未改 | `PreProcessingPlugin.cs`（Provider / 端口 / 命令 / 持久化 / 预览算法）、`Plugin.PreProcessing.csproj`、所有共享文件 |
| 参考实现 | `Plugin.ImageAcquisition` V2（`ImageAcquisitionView.xaml` + `docs/UI设计基线/image-acquisition/`） |
| 复杂度 | M（单 HALCON 画布 + 1 个 `LinkableValueEditor` + 2 个 `PropertyGrid` + 算子库/链/参数三块） |

## 2. 功能契约保留（逐项）

| # | 契约 | 状态 |
| --- | --- | --- |
| C1 | `DataContext = PreProcessingPlugin` + `IPluginConfigView` | 保留；`GetConfigView` 未改，`OnConfirm` 仍走插件实例 |
| C2 | `LinkableValueEditor`：`Port="{Binding SrcImage}"`、`StepData="{Binding StepData}"` | 保留；只设 `Label=""` 把标签外置，控件内部未动 |
| C3 | `ImageEdit`：`HImage="{Binding DisplayImage, Mode=OneWay}"`、`DrawObjectList="{Binding CanvasRois}"`、`ActiveRoi="{Binding CanvasActiveRoi, Mode=TwoWay}"` | **逐字保留**；画布常驻，未用 `Visibility=Collapsed` 显隐 |
| C4 | `SelectedOperator`（对象）/ `DisplayViewIndex`（int 0~9） | 保留：`ListBox.SelectedItem` / `ComboBox.SelectedIndex`，类型未换 |
| C5 | 命令：`AddOperatorCommand`、`RemoveOperatorCommand`、`MoveUpCommand`、`MoveDownCommand`、`ClearChainCommand`、`RefreshPreviewCommand`、`CopyChainCommand`、`PasteChainCommand`、`ExportChainCommand`、`DeleteBoxCommand`（KeyBinding） | 全部保留；按钮只改位置/样式，命令与 `CommandParameter` 未改 |
| C6 | 预览目标：`ShowSourcePreview` / `ShowInputPreview` / `ShowOutputPreview` | 三个 `RadioButton` 双向绑定保留，`GroupName="PreviewTarget"` |
| C7 | 参数面板：`FlatPropertyGrid` / `CardPropertyGrid` 的 `BindingObject="{Binding SelectedOperator}"` 与 `IsFlatLayout` / `IsCardLayout` 可见性 | 保留；共享控件内部未改 |
| C8 | `LoadWarning` / `ActionHint` | 保留；`LoadWarning` → 琥珀横幅，`ActionHint` → 状态栏（空串显示默认提示） |
| C9 | `IPropertyGridLayoutSwitch.UseCardLayout` | 保留；写 `plugin.IsCardLayout` + 参数列宽 360 ⇄ 520 |
| C10 | `OnViewLoaded()` / `EnlargeHostWindowIfNeeded()` | 保留；窗口目标改为内容区 1080×520（窗口 1120×680） |

> 本插件**没有** `Mode` 与 `StatusLevel`：它不是多模式采集插件，状态只有 `LoadWarning`（加载跳过）与 `ActionHint`（复制/粘贴/导出反馈）。本轮没有新增状态属性或枚举。

## 3. 耦合矩阵（SOP §5.1）

| 区域/元素 | 当前绑定/契约 | 功能依赖 | 可重构级别 |
| --- | --- | --- | --- |
| 根布局 | `MinWidth=560`；`MinHeight` 取 `PluginViewMinHeight`（360） | 宿主内容区；代码后置按需放大窗口；根 `ScrollViewer` 兜底 | 只改内部布局 |
| 图像输入 | `LinkableValueEditor Port=SrcImage StepData=StepData` | `InputPort<HImage>` 持久化、变量链接、`ActualValue` 取图、`OnViewLoaded` | 只能包裹 |
| 显示窗口 | `ComboBox SelectedIndex=DisplayViewIndex` | `[StepConfig] int`；0 = 不显示，1~9 = 视图窗口 | 只改外观 |
| 算子库 | `ItemsControl ItemsSource=OperatorLibrary`；`AddOperatorCommand` + `CommandParameter={Binding}` | `OperatorRegistry` 反射分组、`OperatorDescriptor.Factory` | 保留数据源/命令 |
| 处理链 | `ListBox ItemsSource=Operators SelectedItem=SelectedOperator`；`CheckBox IsChecked=Enabled`；`AlternationCount=1000` + `StepNumberConverter` | 选中项驱动参数/预览；`Enabled` 触发 `OnOperatorPropertyChanged` → `WriteChain` + 预览；`IsMuted` 控制行淡化 | 保留 ListBox/绑定，行模板可重画 |
| 链结构命令 | `MoveUp/MoveDown/Remove/Clear` | 集合变更 → `OnOperatorsChanged` → 快照 + 预览 + 画布框同步 | 保留命令 |
| 链搬运命令 | `Copy/Paste/Export` | 剪贴板 JSON、`SaveFileDialog` + `WriteExport`；`ActionHint` 反馈 | 保留命令 |
| 预览目标 | 三个 `RadioButton IsChecked=Show*Preview` | `ApplyPreviewState` 唯一写入口；带框算子切到输入图 | 保留三个 bool |
| 预览画布 | `ImageEdit HImage/DrawObjectList/ActiveRoi` | HALCON 显示/缩放/右键菜单；框拖拽回写 `ApplyBoxTuples` | **常驻、不可折叠** |
| 删除编辑框 | `KeyBinding DeleteBoxCommand` | `CanExecute` 守卫焦点在 TextBox 时不触发 | 保留 |
| 参数面板 | `FlatPropertyGrid` / `CardPropertyGrid BindingObject=SelectedOperator` | `[SuperDisplay]` 反射生成 | 不改共享控件内部 |
| 参数布局 | `IPropertyGridLayoutSwitch.UseCardLayout` | 宿主“卡片式”开关；列宽 360 ⇄ 520 | 保留接口 |
| 状态/反馈 | `LoadWarning` / `ActionHint` | 无 `StatusLevel`；两字段都是只读 INPC 属性 | 保留两字段 |
| 窗口尺寸 | `OnLoaded` → `OnViewLoaded` + `EnlargeHostWindowIfNeeded` | 三栏舒适宽度；只放大不缩小 | 保留方法，常量随布局调整 |

## 4. 改动点

1. **整页重写 `PreProcessingView.xaml`**：
   - 三栏：左 280px「图像输入 + 算子库」、中「预览工作区」、右 360px（卡片式 520px）「处理链 + 参数」。
   - 预览区：44px 预览头 + 40px 预览目标分段 + 警告/空态信息条 + 常驻 `ImageEdit` + 40px 状态栏。
   - 预览工具条：适应窗口（`FitToWindow()`）、1:1（`FitToImage()`）、刷新（`RefreshPreviewCommand`）、导出（`ExportChainCommand`）；未新增放大/缩小/打开文件夹。
   - 处理链：行模板重画（步号 + 启用勾选 + 名称 + 摘要；选中主色浅底；停用行 `Opacity=0.45`）；结构命令与整链搬运命令分组排列。
   - 参数面板：保留两个 `PropertyGrid` 同格叠放 + 可见性切换；新增标题、选中算子名与空态。
   - 算子库：分组标题 + 可点击行（`+` 号悬停变主色），仍绑定 `AddOperatorCommand` / `OperatorDescriptor`。
   - 状态：`LoadWarning` 琥珀横幅；`ActionHint` 状态栏；`Operators.Count` 步数。
2. **`PreProcessingView.xaml.cs`**：
   - 保留构造、`DataContext`、`OnLoaded` → `OnViewLoaded()`、`EnlargeHostWindowIfNeeded()`。
   - 新增 `FitWindow_Click` / `FitImage_Click`，只调 `PreviewImageEdit.FitToWindow()/FitToImage()`。
   - 新增 `ActionHintConverter`、`StringNotEmptyToVisibilityConverter`、`NullToVisibilityConverter`、`MinSizeConverter`（纯展示/布局）。
   - `UseCardLayout` 列宽 540 → 520（三栏布局下 1120×680 预览实际约 256px；960 下约 250px，不建议窄档使用）。
   - `ContentDesiredWidth/Height` 改为 1080×520（窗口 1120×680）；`ShellChromeHeight` 改为 72+56+16+16=160。
3. **未改共享文件**：`Colors.xaml`、`PluginConfigColors.xaml`、`UIThemeSmokeTest` 的 `Program.cs` / `.csproj` / `PluginViewSmoke.cs`、`PluginConfigShellView.xaml`、`docs/UI设计基线/README.md`、其他插件、`PreProcessingPlugin.cs`、csproj。
4. **设计基线新增**：`docs/UI设计基线/pre-processing/design.html` + `README.md` + `evidence/after-render-v1-initial.png`。

### 4.1 P1 修复：640×560 根布局裁切（reviewer 2026-10-20）

**问题**：三栏自然宽度 280 + 250 + 360 + 24 = **914**（卡片式 1074）；根 `MinWidth` 原为 620，宿主 640 窗口内容区仅约 600，且根布局无滚动 → 640×560 必然裁切。右栏最小高度 240 + 8 + 200 = **448**，内容区高度仅 400，同样纵向裁切。

**修法（优先方案，已落地）**：
- 根 `UserControl MinWidth="560"`；根内容包 `ScrollViewer x:Name="RootScroll"`（横/纵 `Auto`，`CanContentScroll=False`）。
- 内层 `Grid` 的 `Width` 用 `MultiBinding` + `RootWidthConverter`：视口宽与 `IsCardLayout` 取 `max(视口, 表 914 / 卡 1074)`；`Height` 用 `MinSizeConverter` 夹到 `448`。**只加 MinWidth/MinHeight 不够**：外层 ScrollViewer 会给内容无限测量空间，内层星号行列与左栏 ScrollViewer 会把 Extent 撑到 1120×1215（实测），正常窗口也长滚动条。
- **卡片式动态最小宽度**：`UseCardLayout` 把 `ParamColumn.Width` 改为 520 后，`RootWidthConverter` 同步把根最小宽度从 914 切到 1074，否则 960/640 下 Grid 只到视口宽、右侧约 130px 被裁而不滚动。
- 探针结果（临时工程 `%TEMP%\pp_layout_probe`，未入仓）：
  - 600×400 表格式：`view=600×400`，`Extent=914×448`，`HScroll=Visible`、`VScroll=Visible`。
  - 920×520 表格式：`view=920×520`，`Extent=920×520`，`HScroll=Collapsed`、`VScroll=Collapsed`。
  - 920×520 卡片式：`view=920×520`，`Extent=1074×520`，`HScroll=Visible`、`VScroll=Collapsed`。
  - 600×400 卡片式：`view=600×400`，`Extent=1074×448`，`HScroll=Visible`、`VScroll=Visible`。
- **HwndHost 边界**：`ImageEdit` 仍是常驻控件；真机若出现 HALCON 画面在滚动容器中越界/不随滚动，按 reviewer 备选方案退化为 `MinWidth=960` 并在 README 标注“不支持 640 宽”。当前优先方案已通过布局探针（表格式与卡片式均覆盖）。

### 4.2 P3 修复：ShellChromeHeight 与尺寸口径

- `ShellChromeHeight` 由错误的 `50+50+16+16=132` 改为 **`72+56+16+16=160`**（标题栏 72 + 底部栏 56 + ContentPresenter 上下 16+16）。
- `ContentDesiredWidth/Height = 1080×520` → 目标窗口 **1120×680**（旧口径已废弃）。
- README 尺寸口径统一为：1120×680 → 内容 1080×520 → 表格式预览 **416** / 卡片式 **256**；960×680 → 920×520 → 表格式 **256** / 卡片式 **96**（不建议，横向滚动 1074）；640×560 → 600×400 → 表格式滚动 914×448、卡片式滚动 1074×448。
- design.html 的 gap 从 16 改为 12、窄档列宽改为 280/360（卡片 280/520），与 WPF 的 12px gap 和列宽一致；卡片式隐藏预览头元信息/目标提示，避免窄预览把分段挤成竖排。


## 5. 与 ImageAcquisition V2 的差异

| 项 | ImageAcquisition V2 | PreProcessing V1 |
| --- | --- | --- |
| 主布局 | 两栏（配置轨 356 + 预览） | **三栏**（280 + 预览 + 360/520）；本插件多一个「处理链 + 参数」功能区 |
| 模式选择 | 2×2 模式卡片（`Mode` 枚举 + `EnumConverter`） | 无模式；左栏改为「图像输入 + 算子库」 |
| 预览目标 | 无（预览只有一张图） | 三段式「原图 / 输入图 / 算子输出」，绑三个 bool |
| 画布控件 | `ImageReadOnly`，按需由 `ContentControl` 创建 | `ImageEdit` **常驻**（ROI 拖拽、`DrawObjectList`、`ActiveRoi` 都要保留） |
| 空态 | `ContentControl` 无图时不实例化控件，空态覆盖层 | 不能覆盖 `HwndHost`：空态改为画布上方信息条 + 状态栏；画布常驻 |
| 参数面板 | 无 | `FlatPropertyGrid` / `CardPropertyGrid` 反射生成，仅改容器/标题/空态 |
| 状态 | `StatusMessage` + `StatusLevel` 三档 | 无 `StatusLevel`；`LoadWarning` + `ActionHint` |
| 工具条 | 适应窗口 / 1:1 / 目录刷新 | 适应窗口 / 1:1 / 刷新 / 导出图片 |
| 列宽 | 左 356，预览自适应 | 左 280，右 360/520，预览 `*`（MinWidth 250） |

## 6. 验证

| 项 | 命令 | 结果 |
| --- | --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.PreProcessing\Plugin.PreProcessing.csproj -v m` | **0 错误 / 0 警告**；`Modules\Plugin.PreProcessing.dll` 已刷新（2026-10-20 04:04，239,104 字节） |
| 批量视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.PreProcessing` | **全部通过**（11 条 PASS） |
| 静态 hex | 同冒烟 | `XAML hex=0 (xaml=1 hex=0)` |
| 视图实例化 | 同冒烟 | 488 个元素；`DataContext=PreProcessingPlugin`；`LinkableValueEditor.Port` 非空 |
| 命令绑定 | 同冒烟 | 9 个命令路径：`AddOperatorCommand`、`RefreshPreviewCommand`、`ExportChainCommand`、`MoveUpCommand`、`MoveDownCommand`、`RemoveOperatorCommand`、`ClearChainCommand`、`CopyChainCommand`、`PasteChainCommand` |
| `OnConfirm` | 同冒烟 | 可执行，`InputValues=3`（`DisplayViewIndex` / `Chain` / 可能其它基类项） |
| 布局探针（表/卡） | 临时 WPF 探针（`%TEMP%\pp_layout_probe`，未入仓） | 600×400 表：`Extent=914×448`、双向滚动；920×520 表：`Extent=920×520`、无滚动；920×520 卡：`Extent=1074×520`、横向滚动；600×400 卡：`Extent=1074×448`、双向滚动。证据 `evidence/after-layout-probe-640x560.png`、`after-layout-probe-960x680-card.png`、`after-layout-probe-640x560-card.png` |
| 渲染 | 同冒烟 | `docs/UI设计基线/_batch-evidence/Plugin.PreProcessing-PreProcessingPlugin.png`；已复制到 `docs/UI设计基线/pre-processing/evidence/after-render-v1-initial.png` |

> 未运行 `UIThemeSmokeTest` 的 `--pre-processing` 专属模式（该模式不存在，且本轮硬约束不允许修改 `Program.cs`）。批量 `--plugin-views` 的 11 条是公共基线；本插件的端口名/命令名/画布绑定由 reviewer 对照 XAML 逐条复核（见 §7）。

## 7. Reviewer 重点

1. **`ImageEdit` 三条绑定逐字一致**：`HImage="{Binding DisplayImage, Mode=OneWay}"`、`DrawObjectList="{Binding CanvasRois}"`、`ActiveRoi="{Binding CanvasActiveRoi, Mode=TwoWay}"`；且没有把 `ImageEdit` 放进 `Visibility=Collapsed` 的容器。
2. **`LinkableValueEditor`**：`Port="{Binding SrcImage}"`、`StepData="{Binding StepData}"`；`Label=""` 只是外观。
3. **`DisplayViewIndex`**：仍是 `ComboBox.SelectedIndex` 绑定 int，不是 `SelectedItem`；`ItemsSource` 是 0~9 的 `x:Array`。
4. **`SelectedOperator`**：仍是 `ListBox.SelectedItem` 对象绑定；行内 `CheckBox IsChecked="{Binding Enabled}"` 未改成命令。
5. **参数面板**：两个 `PropertyGrid` 的 `BindingObject` 与可见性转换器未变；`UseCardLayout` 仍写 `plugin.IsCardLayout`。
6. **命令**：9 个按钮命令 + `DeleteBoxCommand` KeyBinding 全部存在；没有新增死按钮（导出/刷新/适应/1:1 都接现有入口）。
7. **共享文件零改动**：`git diff --name-only` 应只出现 `PreProcessingView.xaml`、`PreProcessingView.xaml.cs`、`docs/UI设计基线/pre-processing/*`、本记录。
8. **`Modules` 产物**：确认 `Modules\Plugin.PreProcessing.dll` 时间戳与 `bin\Debug\net9.0-windows\Plugin.PreProcessing.dll` 一致。
9. **根滚动容器**：`MinWidth=560` + `RootScroll` + `RootWidthConverter`（表 914 / 卡 1074）+ `MinSizeConverter`（高 448）；不要把内层 Grid 的 `Width/Height` 绑定去掉（去掉后 920×520 下 Extent 会变成 1120×1215，正常窗口也长滚动条），也不要把宽度固定回 914（卡片式 960/640 会裁掉右侧约 130px）。

## 8. 遗留问题

1. **画布空态/覆盖层**：`ImageEdit` 是 HALCON `HwndHost`，WPF 元素无法可靠叠加；设计稿的居中空态在 WPF 里改为画布上方信息条。属已知偏差，非缺陷。
2. **无 `StatusLevel`**：本插件没有 Info/Warning/Error 三档；`LoadWarning` / `ActionHint` 已覆盖现有语义。若后续要统一状态栏，需要 VM 新增状态属性（延后）。
3. **卡片式参数列宽 520**：1120×680 下预览实际约 **256px**；960×680 下约 **250px**，不建议窄档使用卡片式。若现场反馈卡片内容拥挤，可在主会话统一评估是否加宽窗口或调整 `CardPropertyGrid` 内部（共享控件，单独基线）。
4. **渲染证据只有初始态**：批量 `--plugin-views` 只渲染一张 920×520 初始态；四态（正常/空链/加载警告/含停用步）目前只在 `design.html` 中可点击演示，WPF 侧未新增专属渲染模式（硬约束不允许改 `Program.cs`）。真机验收需按设计稿逐态对照。
5. **真实图像预览**：`RenderTargetBitmap` 不捕获 `HwndHost`，证据 PNG 画布为黑底；需真机打开配置窗口验证 HALCON 图像/ROI 拖拽。
6. **算子库滚动**：左栏「图像输入 + 算子库」共用一个 `ScrollViewer`；窄档 960×680 时算子库需要滚动，属预期。
7. **设计稿确认**：README 的确认状态仍为「待用户确认」；确认后基线只读，改动升 v2。
8. **640×560 的 HwndHost 滚动边界**：根滚动容器已按优先方案落地，布局探针证明 600×400 视口不抛异常且双向滚动条可见；但 `ImageEdit` 是 HALCON `HwndHost`，真机滚动时若出现画面越界/不随滚动，按 reviewer 备选方案退化为 `MinWidth=960` 并在 README 标注“不支持 640 宽”。

## 8.1 A 组遗留问题修复（2026-10-20，PP1）

- 文档口径修正（不改代码）：README 与本文中「960×680 卡片式预览 96px」统一改为实测约 **250px**（1120 档约 256px）。
- 涉及位置：`docs/UI设计基线/pre-processing/README.md` 已知偏差第 4 条；本文 §4 第 2 条与 §8 第 3 条。

## 9. 复跑命令

```text
dotnet build Plugins\Plugin.PreProcessing\Plugin.PreProcessing.csproj -v m
dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.PreProcessing
```

> `UIThemeSmokeTest` 默认模式退出码恒为 1（既有 GridView 红灯，R46）；`--plugin-views` 模式按 failures 返回 0/1，本轮为 0，以日志 `PASS/FAIL` 行为准。
> 本轮未构建 `UIThemeSmokeTest`（按硬约束），`--no-build` 跑的是既有二进制；插件 DLL 已由插件工程刷新，冒烟从 `Modules\` 反射加载的是新产物。
