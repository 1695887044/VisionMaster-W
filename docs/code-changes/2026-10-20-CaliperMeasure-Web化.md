# 2026-10-20 CaliperMeasure 自定义视图 Web 化

## 1. 范围

- 设计基线：`docs/UI设计基线/caliper-measure/README.md` v1 + `design.html`（自包含、可点击、含默认态 / 圆直径 / 空态 / 错误态 / 640 窄档）。
- 覆盖：`Plugins\Plugin.CaliperMeasure\CaliperMeasureView.xaml` + `CaliperMeasureView.xaml.cs`。
- 参考实现：`docs/UI设计基线/image-acquisition/` v2 + `docs/code-changes/2026-10-20-图像采集配置窗口V2复刻与FluentChipDanger重复键修复.md`。
- 不在范围：插件类 `CaliperMeasurePlugin.cs`、`Models\CaliperRegion.cs`、`CaliperEnums.cs`、工程文件、帮助手册、公共令牌字典、`PluginConfigShellView.xaml`、`UIThemeSmokeTest` 源码。
- 本轮**未修改任何共享文件**；**未新增公共令牌**（所需颜色全部落在既有 `Dialog*` / `PluginPreview*` / `Scada*` 键内）。

## 2. 功能契约保留（逐项）

| # | 契约 | 状态 |
| --- | --- | --- |
| C1 | `DataContext = CaliperMeasurePlugin` + `IPluginConfigView` | 保留；`GetConfigView` 仍 `new CaliperMeasureView { DataContext = this }`，视图构造签名未变 |
| C2 | `LinkableValueEditor`：`Port="{Binding SrcImage}"`、`StepData="{Binding StepData}"` | 保留；`Label=""` 只把标签外置，控件内部逻辑未动 |
| C3 | `ImageEdit`：`ActiveRoi="{Binding CanvasActiveRegion, Mode=TwoWay}"`、`DrawObjectList="{Binding CanvasRegions}"`、`HImage="{Binding PreviewImage, Mode=OneWay}"` | 逐字保留；`ImageEdit` 常驻，未用 `Visibility=Collapsed` 显隐已设图控件 |
| C4 | `MeasureKind` / `SelectedRegion` / `CaliperRegions` | 保留：枚举 / `CaliperRegion` 对象 / `ObservableCollection`；测量类型仍是 `RadioButton + ui:EnumConverter` 写枚举 |
| C5 | 全部数值参数 | `CaliperCount` / `Sigma` / `EdgeThreshold` / `FitMaxError` / `FitMinPoints` / `AnnulusWidth` / `StandardValue` / `UpperTolerance` / `LowerTolerance` / `PixelSizeMm` 的 `Value` 绑定、`Minimum`/`Maximum`/`DecimalPlaces` 与 `cv:NumberValidationRule` 全部保留 |
| C6 | 命令与生命周期 | `DeleteSelectedRegionCommand` / `ResetRegionsCommand` / Del 快捷键 / `Loaded → OnViewLoaded()` 保留；未新增业务命令 |
| C7 | `StatusMessage` / `StatusLevel` | 保留 Info / Warning / Error 三档；状态栏只换布局与颜色，不扩展枚举 |
| C8 | 预览工具条 | 只接既有 `ImageEditor.FitToWindow()` / `FitToImage()`；未新增缩放/打开文件夹/系统导航 |

## 3. 耦合矩阵（SOP §5.1）

| 区域/元素 | 当前绑定/契约 | 功能依赖 | 可重构级别 |
| --- | --- | --- | --- |
| 根布局 | `MinWidth/MinHeight` 动态资源；`DataContext = 插件` | 宿主靠 DataContext 取 `IPluginConfigView` | 自由（保留 MinWidth/MinHeight 与 DataContext） |
| 图像输入 | `LinkableValueEditor.Port = SrcImage`、`StepData` | 端口持久化、类型转换、变量链接、解除链接 | 只能包裹/改外观 |
| 测量类型 | `ComboBox.ItemsSource = MeasureKinds`、`SelectedItem = MeasureKind` | `OnMeasureKindChanged` 补足搜索区、刷新预览、显隐拟合参数 | 只改外观；必须写 `MeasureKind` 枚举 |
| 搜索区列表 | `ListBox.ItemsSource = CaliperRegions`、`SelectedItem = SelectedRegion`、`DisplayMemberPath = DisplayText` | 选中项与 `CanvasActiveRegion` 双向联动、驱动参数微调 | 只能包裹/改外观 |
| 参数微调 | `ItemsControl.ItemsSource = SelectedRegion.ParamEntries`、`TextBox.Text = Value, LostFocus` | 写回 `CaliperRegion.Params` → 画布 `HTuples` → 预览刷新 | 只能包裹/改外观 |
| 卡尺数/找边/拟合/判定 | `ui:NumericBox.Value` + `ComboBox.SelectedItem` | `[StepConfig]` 持久化、HALCON 参数映射、校验规则 | 只能包裹/改外观；类型/范围/规则不动 |
| 画布 | `ImageEdit.ActiveRoi`（TwoWay）/ `DrawObjectList` / `HImage`（OneWay） | HALCON 显示、缩放、右键新建/删除、ROI 拖拽 | 保留控件；常驻，不折叠 |
| 命令 | `ResetRegionsCommand` / `DeleteSelectedRegionCommand` / Del KeyBinding | 重建默认搜索区、删除选中搜索区 | 保留命令名与绑定 |
| 状态 | `StatusMessage` / `StatusLevel` | Info/Warning/Error 三档色；预览空/错误态 | 保留三档 |
| 预览工具条 | `FitToWindow()` / `FitToImage()` | 既有 `HalconBase` 公开方法 | 纯 UI 入口 |
| 生命周期 | `Loaded → OnViewLoaded()` | 标记配置实例、补足搜索区、首次预览 | 保留 |

## 4. 改动点

1. **左栏改为单一配置轨**：`1*`（300~356px）+ 右侧 `1.6*` 工作区；左栏一个 `ScrollViewer`，按 01 图像输入 / 02 测量类型 / 03 搜索区与卡尺 / 04 找边参数 / 05 拟合参数 / 06 判定与标定编号分区，去掉五张独立 `PluginCard` 的层层边框。
2. **测量类型改为 5 张卡片**：`RadioButton` + 本地 `ModeCardStyle`，`IsChecked` 仍走 `ui:EnumConverter` 写 `MeasureKind`；卡片图标用内联 `Path` 几何（宽度/两点距/点线距/圆直径/角度），不依赖图标字体字形。
3. **搜索区与卡尺同卡**：卡尺数、搜索区 `ListBox`、删除/重置、参数微调 `ItemsControl` 放在同一分区；列表选中与参数微调绑定保持原样。
4. **表单节奏**：主控件 36px；参数微调行 30px；`ComboBox` 统一 `DialogCombo`；`NumericBox` 统一 `ParamNumericBoxStyle`（基于既有 `PluginNumericBox`，只改高度/字号）。
5. **标注图工作区**：44px 预览头（标题 + 当前类型 + 判定胶囊 + 适应窗口/1:1）+ 画布上方信息条（空态/错误态）+ 常驻 `ImageEdit` + 40px 状态栏；**不在 `ImageEdit` 的 Grid 内放任何 WPF 覆盖层**（HALCON `HwndHost` airspace，真机会被 HWND 盖住）。
6. **状态栏**：`StatusLevel` 驱动状态点与文字颜色；右侧只读摘要 `CaliperCount` 与 `CaliperRegions.Count`，不新增 VM 属性。
7. **全令牌化**：视图 XAML 只引用公共令牌，静态扫描 `hex=0`；未改 `Colors.xaml` / `PluginConfigColors.xaml`。
8. **code-behind 只加 UI 处理器**：保留 `Loaded → OnViewLoaded()`；新增 `FitWindow_Click` / `FitImage_Click` 两个事件处理器，调用既有 `ImageEditor.FitToWindow()` / `FitToImage()`；新增窄档布局收敛 `UpdateNarrowLayout()` 与状态文案空串兜底 `StatusTextConverter`；不引入独立 VM。

### 4.1 Reviewer P1/P2 修复（2026-10-20）

- **P1（HALCON HwndHost airspace）**：第一版把空态/错误态/「可拖拽搜索区」角标/OK-NG 胶囊放在 `ImageEdit` 同一 Grid 内。`ImageEdit` 是 `HwndHost`，WPF 覆盖层会被 HWND 盖住（真机不可见/不可点击），该前提是错误的。已改为：
  - 空态/错误态 → **画布上方信息条**（Auto 行，`PreviewImage == null` 时显示；Warning 橙色 / Error 红色；文本 `StatusMessage`）；
  - OK/NG 判定胶囊 → **预览头**（`PreviewImage != null` 时显示）；
  - 删除画布内所有 WPF 覆盖层/角标；`ImageEdit` 的 Grid 内只剩 `ImageEdit` 本身；
  - `ImageEdit` 三条绑定逐字未动，控件仍常驻。
- **P2（窄档 MinWidth）**：根 `MinWidth` 从 `{DynamicResource PluginViewMinWidth}`（620）改为本视图局部 `MinWidth="560"`，匹配宿主 640 内容区约 600；code-behind `ActualWidth < 720` 时隐藏预览类型文字与状态摘要（纯布局，不改业务属性）。
- **证据**：设计页 `design.html` 同步改为「画布上方信息条 + 预览头判定胶囊」，新增 `evidence/design-preview-v1-640.png`。

## 5. 与 ImageAcquisition V2 的差异

| 维度 | ImageAcquisition V2 | CaliperMeasure v1 |
| --- | --- | --- |
| 画布宿主 | `ContentControl PreviewHost` + DataTemplate，有图才创建 `ImageReadOnly` | **`ImageEdit` 常驻**（ROI 拖拽是核心交互，不能按需销毁）；空/错误态是画布上方信息条（HwndHost 之外），不折叠画布 |
| 模式 | 4 种采集模式卡片（文件/目录/网络/相机） | 5 种测量类型卡片（宽度/间隙、两点距、点到线距、圆直径、角度） |
| 表单 | 路径/索引/超时 + 相机 + 输出 | 搜索区列表 + 参数微调 + 10 个数值参数 + 5 个枚举下拉 |
| 预览工具条 | 适应窗口 / 1:1 / 目录刷新 | 适应窗口 / 1:1（没有刷新命令，预览由参数变更自动防抖刷新） |
| 状态栏右侧 | 路径 | `卡尺 N 把 · 搜索区 M 个` |
| 空/错误态 | 按模式显示不同空态；错误态在 `PreviewImage == null && StatusLevel == Error` | `PreviewImage == null` 时显示画布上方信息条：Warning 橙色（空图）/ Error 红色（失败）；NG 但有预览图时预览头红色判定胶囊 + 红状态栏 |
| 图标 | Font Awesome + Lucide 语义 | 类型卡片用内联 `Path` 几何；工具条/状态图标用 Font Awesome |
| 新功能 | 无 | 无（仅新增既有 `FitToWindow/FitToImage` 的 UI 入口） |

## 6. 验证

| 项 | 命令 | 结果 |
| --- | --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.CaliperMeasure\Plugin.CaliperMeasure.csproj -v m` | **0 错误 / 0 警告**；`Modules\Plugin.CaliperMeasure.dll` 已刷新 |
| 批量视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.CaliperMeasure` | **11/11 通过**（见下） |
| 常规 gate | `dotnet run --project FlowCanvasChecks --no-build` | **2691 通过 / 0 失败**（含 `[CALM]` 卡尺像素当量段） |
| 设计页交互 | 本地 HTTP 打开 `design.html`，点击 5 种类型 / NG / 空态 / 错误 / 640 窄档 | 状态切换、拟合参数显隐、圆直径环宽、参数行、画布场景、信息条、状态栏均正确；截图见 `docs/UI设计基线/caliper-measure/evidence/design-preview-v1-*.png`（含 `-640.png`） |
| 渲染 PNG | 批量冒烟生成 | `docs/UI设计基线/_batch-evidence/Plugin.CaliperMeasure-CaliperMeasurePlugin.png` |
| 共享文件 | 静态核对 | `Colors.xaml` / `PluginConfigColors.xaml` / `PluginConfigShellView.xaml` / `UIThemeSmokeTest` 源码 / 其他插件均未改 |

批量冒烟 11 条明细：

1. 找到待检插件 DLL（`count=1 filter=Plugin.CaliperMeasure`）
2. `GetConfigView` 返回 `FrameworkElement`（`CaliperMeasureView`）
3. 视图实例化（`elements=439`）
4. `DataContext/IPluginConfigView`（`CaliperMeasurePlugin`）
5. `LinkableValueEditor.Port`（`count=1 ports=SrcImage`）
6. `Button.Command` 绑定（`buttons=5 commands=ResetRegionsCommand,DeleteSelectedRegionCommand`）
7. `ComboBox` 绑定（`Polarities/Polarity`、`EdgeSelects/Select`、`Interpolations/Interpolation`、`FitAlgorithms/FitAlgorithm`）
8. `TextBox.Text` 绑定（`PortValue,LinkAddress`，来自 `LinkableValueEditor` 内部）
9. `OnConfirm` 可执行（`InputValues=18`）
10. `XAML hex=0`（`xaml=1 hex=0`）
11. 渲染 PNG

> 说明：`UIThemeSmokeTest` 退出码恒为 1（既有 GridView 红灯，R46）；本次以日志 `全部通过` 与 11 条 `[PASS]` 为准。未构建 `UIThemeSmokeTest`（按本次指令 `--no-build`）。

## 7. 遗留问题

1. **真机验收未做**：需按 SOP §7 打开配置 → 改每个字段 → 确认 → 重开；验证 `SrcImage` 链接/解除链接、画布右键新建/删除、ROI 拖拽、试运行、关闭无崩溃/泄漏。
2. **`FlowCanvasChecks` 已跑**：`dotnet run --project FlowCanvasChecks --no-build` → **2691 通过 / 0 失败**（含 `[CALM]` 卡尺像素当量段）。
3. **搜索区不足的橙色警示未在 WPF 落地**：设计稿在数量不足时切橙色；WPF 第一版仍用 `RegionCountHint` 原文 + 蓝色提示条（VM 未暴露「是否足够」的布尔属性，视图不新增业务判断）。见基线 README「已知偏差 10」。
4. **`StatusMessage` 初始空串**：已加 `StatusTextConverter` 兜底为「等待输入图像…」；真机 `OnViewLoaded()` 会立即写入插件原文（「输入图像为空」或测量结果），兜底只在 Loaded 前生效。
5. **设计基线索引未更新**：`docs/UI设计基线/README.md` 是共享文件，按本次硬约束未改；请主会话在批次汇总时补 `caliper-measure/` 一行。
6. **图标字体**：类型卡片用内联 `Path`，与 Web 稿 Lucide 形状不完全一致；工具条/状态图标沿用 Font Awesome。语义一致，已写入基线 README 偏差 1。
7. **窄档 640×560**：根 `MinWidth` 已改 560；设计证据含 `design-preview-v1-640.png`（640×680 展示）；640×560 下左栏滚动与画布高度仍需真机复核。

## 8. Reviewer 重点

- `CaliperMeasureView.xaml` 的 `ImageEdit` 三条绑定是否逐字保留、是否仍常驻（无 `Visibility` 绑定）。
- **P1 复核**：`ImageEdit` 的 Grid 内是否只剩 `ImageEdit`（无空态/错误态/角标/判定胶囊等 WPF 覆盖层）；空/错误态是否在画布上方信息条、判定胶囊是否在预览头。
- **P2 复核**：根 `MinWidth` 是否为局部 `560`；`ActualWidth < 720` 时 `PreviewKindText` / `PreviewKindSeparator` / `StatusSummary` 是否 Collapsed。
- 10 个数值参数的 `Value` 绑定、`Minimum`/`Maximum`/`DecimalPlaces`、`NumberValidationRule` 是否与改前一致。
- `LinkableValueEditor` 是否仍 `Port=SrcImage` + `StepData`，是否只设 `Label=""`。
- 5 个 `RadioButton` 是否仍用 `ui:EnumConverter` 写 `MeasureKind`（不是新 VM 属性）。
- `DeleteSelectedRegionCommand` / `ResetRegionsCommand` / Del KeyBinding 是否仍在。
- `StatusLevel` 是否仍只有 Info/Warning/Error 三档；`StatusTextConverter` 是否只做显示层空串兜底。
- 视图 XAML 是否 `hex=0`；是否误改共享字典/共享控件/其他插件。
- `Modules\Plugin.CaliperMeasure.dll` 时间戳是否晚于本次 XAML/code-behind 修改。

## 9. 复跑命令

```text
dotnet build Plugins\Plugin.CaliperMeasure\Plugin.CaliperMeasure.csproj -v m
dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.CaliperMeasure
dotnet run --project FlowCanvasChecks --no-build
```

设计页复看（可选，任意静态服务器）：

```text
python -m http.server 8947 --bind 127.0.0.1 --directory "D:\C#\VM\docs\UI设计基线"
# 打开 http://127.0.0.1:8947/caliper-measure/design.html
```

## 10. A 组遗留问题修复（2026-10-20）

| ID | 改动 | 文件 |
| --- | --- | --- |
| CM1 | 新增 `RegionCountWarningConverter`（`IMultiValueConverter`：`CaliperRegions.Count` + `RequiredRegionCount`）；搜索区不足时提示条 `Background/BorderBrush` 切 `DialogWarningSubtleBrush` / `DialogWarningBorderBrush`，图标切 `f071` + `DialogWarningTextBrush`。仅视图层，VM 与测量逻辑未动。 | `CaliperMeasureView.xaml` / `.xaml.cs` |
| CM2 | 状态点默认 `DialogTextMutedBrush`；仅 `Info` 且 `StatusMessage` 非空时绿色（`StatusDotSuccessConverter`）；Warning/Error 触发器保留。 | 同上 |
| CM3 | 删除死资源 `LocalPrimaryButtonStyle`、`EmptyStateTitleStyle`、`EmptyStateSubStyle`、`EmptyStateIconStyle`；删除插件类 `MeasureKinds` / `IsWidthMeasure` / `IsPointToPointMeasure` 及 `OnMeasureKindChanged` 内对应通知（全仓 grep 无外部引用）。 | `CaliperMeasureView.xaml` / `CaliperMeasurePlugin.cs` |
| CM4 | `ToolbarTextButtonStyle` 的 `ContentPresenter` 补 `Margin="{TemplateBinding Padding}"`；`SelectedRegion.Name` 绑定补 `FallbackValue=选中搜索区后可编辑`。 | `CaliperMeasureView.xaml` |
| CM5 | 删除 `design.html` 中 `.cm-canvas-chip` 两条死 CSS；640×560 真机项按约定不在本轮。 | `docs/UI设计基线/caliper-measure/design.html` |

**验证**：`dotnet build Plugins\Plugin.CaliperMeasure\Plugin.CaliperMeasure.csproj -v m` → **0 错误 / 0 警告**；`dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.CaliperMeasure` → **11/11 PASS**（`elements=439`、`XAML hex=0`、`OnConfirm InputValues=18`）。
