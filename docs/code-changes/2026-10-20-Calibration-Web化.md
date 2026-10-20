# 2026-10-20 Calibration 自定义视图 Web 化

## 1. 范围

- 设计基线：`docs/UI设计基线/calibration/README.md` v1 + `design.html`（自包含、可点击）。
- 覆盖：`Plugins\Plugin.Calibration\CalibrationView.xaml` + `CalibrationView.xaml.cs`。
- 插件类：`Plugins\Plugin.Calibration\CalibrationPlugin.cs` 仅做 3 处可空性守卫（消除既有 CS8601/CS8604 警告，行为不变）。
- 不在范围：端口/命令/持久化/业务流程、`LinkableValueEditor`、`ImageEdit`/`HalconBase`、`PluginConfigShellView`、公共样式字典、其它插件、`docs/UI设计基线/README.md`。

## 2. 耦合矩阵（改造前逐项核对）

| 区域/元素 | 当前绑定/契约 | 功能依赖 | 可重构级别 |
| --- | --- | --- | --- |
| 根布局 | `Grid Margin=12` 两列；`KeyDown=OnViewKeyDown` | Esc 取消取点；宿主内容区尺寸 | 自由（保留 MinWidth/MinHeight） |
| 模式选择 | 4 个 `RadioButton.IsChecked` → `Mode`，`ui:EnumConverter`，`IsEnabled=IsEditable` | `OnModeChanged` 取消取点、刷新 `ShowPointTable`、`RefreshStatus`；`Mode` 是 `[StepConfig]` | 只改外观，必须写 `Mode` |
| 标定图 | `cv:LinkableValueEditor`：`Port=SourceImagePath`、`StepData`、`BrowseCommand=BrowseImageCommand` | 端口持久化、类型转换、变量链接/解除、浏览 | 只能包裹，不能替换 |
| 标定图尺寸/实时图说明 | `SourceImageSizeText` + 静态说明 | 记录尺寸用于下游失配自证；实时图优先级说明 | 纯文本，可重排 |
| 像素当量参数 | `KnownLengthMm`、`ScaleARow/Col`、`ScaleBRow/Col`、`PickScalePointCommand`（A/B）、`ScaleSpanText`/`ScaleSpanLevel` | 当量计算、取点待命、跨度预警 | 只改外观/布局，绑定原样 |
| 网格规模 | `GridSizeOptions`、`SelectedItem=GridSize`、`RebuildGridCommand`、`ShowPointTable` | 表格行数重建、旧值保留、缩小拒绝 | 保留 int 与命令 |
| 标定表 | `ItemsControl.ItemsSource=PointRows`、`IsEnabled=IsEditable`；行内 `MachineX/Y`、`ImageRow/Col`、`Name`、`IsEmpty/IsPartial/StateText`、`ResidualPx` | 表格编辑、半填/空行语义、残差显示 | 只能包裹/改行模板 |
| 逐行动作 | `PickRowPointCommand`、`PrefillRowFromSourceCommand`，`CommandParameter={Binding}` | 图上取点、上游定位点预填 | 命令/参数逐字保留 |
| 表格动作 | `PasteRowsCommand`、`ClearRowsCommand`、`PushMarkersCommand` | 剪贴板粘贴、清空、画布标记生成 | 保留命令 |
| 内参标定 | `CaltabDescriptorOptions`/`CaltabDescrFile`、`IntrinsicsModel`（EnumConverter）、`IntrinsicsPixelSizeUm`、`IntrinsicsImageFolder/List`、`BrowseIntrinsicsFolderCommand`、`RunIntrinsicsCalibrationCommand`、`RunIntrinsicsSelfVerifyCommand`、`ClearIntrinsicsCommand`、`IsIntrinsicsIdle`、`HasIntrinsics`、各结果文本/级别、`UndistortBeforeCalibration` | 标定板选择、模型、读图、标定链、自验证、去畸变开关 | 只能包裹；默认折叠不改变字段/命令 |
| 质量与标定信息 | `QualityText/QualityLevel/QualityHint`、`CameraSerial`、`IsCalibrationLocked`、`ExportCalibrationCommand`、`ImportCalibrationCommand`、`ResidualThresholdPx`、`MmPerPixelMin/Max` | 质量判读、锁定、归档、护栏 | 保留全部绑定与类型 |
| 画布 | `h:ImageEdit`：`HImage=CanvasImage`（OneWay）、`DrawObjectList=CanvasMarkers`、`ActiveRoi=CanvasActiveRoi`（TwoWay）、`Annotations=ResidualAnnotations`、`IsPickMode=IsPickingPoint`、`ImagePicked=OnImagePicked` | HALCON 缩放/右键菜单/标记拖动/图上取点/残差箭头 | 控件常驻，不折叠；只能包裹 |
| 取点态 | `IsPickingPoint`、`PickHintText`、`CancelPickCommand` | 待命横幅、Esc/按钮取消 | 保留三档状态语义 |
| 状态栏 | `StatusMessage`、`StatusLevel`、`PreviewImagePath` | Info/Warning/Error 三档；路径只读显示 | 保留三档，不新增第四档 |
| 生命周期 | 构造 `(IStepConfigData, CalibrationPlugin)`、`DataContext=plugin`、`plugin.Initialize(stepData)`、`Unloaded` 取消取点 | 宿主取 `IPluginConfigView.OnConfirm`；初始化/释放 | 原样保留 |

**判定原则**：上述“只能包裹/改外观”的行全部保留原控件与绑定；未新增独立 ViewModel，未改端口名、命令名、持久化字段。

## 3. 改动点

1. **两栏布局**：左侧配置轨（`1.15*`，Min 340 / Max 420，内部滚动），右侧标定画布（`1*`，Min 200）；960×680 下约 420/480，640×560 下约 340/248；根 `MinWidth=560`。
2. **模式卡片**：4 个 `RadioButton` 改为 2×2 卡片（像素当量 / 九点 / 透视 / 网格），图标 + 名称 + 用途 + 选中对勾；`Content` 保持 string（既有 `--calibration` 冒烟按文本识别模式），图标走 `Tag`、副标题走 `AutomationProperties.HelpText`；`IsChecked` 仍由 `ui:EnumConverter` 写 `Mode`。
3. **标定图**：`LinkableValueEditor` 设 `Label=""`，外置「标定图路径」标签与「浏览…」按钮（`BrowseImageCommand`）；`Port`/`StepData`/`BrowseCommand` 原样；保留尺寸说明与实时图借入说明。
4. **像素当量卡**：已知长度 + 「取A点」「取B点」+ 跨度提示（`ScaleSpanLevel` 绿色/橙色）+ A/B 四点两列网格；仍绑定原属性与命令。
5. **标定表卡**：网格规模下拉 + 「重建网格」；表头 8 列（点/机械X/机械Y/像Row/像Col/取点/预填/残差px）；行状态（有效绿 / 半填橙 / 空灰）沿用 `IsEmpty/IsPartial`；逐行「取点」「预填」按钮命令与 `CommandParameter` 原样；底部「粘贴 N 行」「清空表格」「把点画到图上」原命令；外层 `Border` 的 `Visibility` 仍绑 `ShowPointTable`（既有冒烟向上找第一个 `Border` 的判据仍成立）。
6. **内参标定**：改为默认收起的 `Expander`（原卡片常驻展开）；展开后标定板、相机模型、像元、文件夹、图列表、三个动作、质量/结果/自验证、去畸变开关与提示逐项保留，绑定未变。
7. **质量与标定信息卡**：质量文本按 `QualityLevel` 变色；诊断提示保留；相机序列号 + 锁定、导入/导出、三个护栏数值原绑定。
8. **画布工作区**：`ImageEdit` 常驻不折叠；画布头承载文件名 + 「标定图 · 只读」+ 两个画布角标（「标定画布」「滑轮缩放 · 右键适应图片」）；空态/错误态改为**画布上方 Auto 行信息条**（Warning 橙 / Error 红，文本 `StatusMessage`，按钮 `BrowseImageCommand`），**画布 Grid 内不再放任何 WPF 覆盖层/角标**（HALCON `HwndHost` 会盖住 WPF 层）。
9. **取点态前置**：`IsPickingPoint` 为真时画布上方橙色横幅 + `PickHintText` + 「取消取点」（`CancelPickCommand`）；Esc 仍走 `OnViewKeyDown`。
10. **状态栏**：状态点 + `StatusMessage`（左）+ `PreviewImagePath`（右）；`StatusLevel` 三档颜色走 `DialogSuccess/Warning/Danger` 令牌；空消息显示「就绪」（`StatusMessageConverter`）。
11. **令牌化**：视图 XAML `hex=0`；颜色全部使用 `Dialog*` / `PluginPreview*` 既有公共键；未新增/修改共享字典。图标按钮 30×30 + `Padding="0"`；焦点 `DialogFocusVisual`；ToolTip `DialogToolTipStyle`/`ToolTipService.ShowDuration`。
12. **code-behind**：保留构造签名、`DataContext=plugin`、`Initialize`、`OnImagePicked`、Esc 处理；新增 `FileNameConverter`（空路径→「未载入标定图」）、`StatusMessageConverter`（空串→「就绪」）、`InverseBooleanToVisibilityConverter`（链接态→隐藏外置浏览按钮）；无独立 VM。
13. **插件类**：3 处可空性守卫（`matrix == null` / `bands!` / `bandCounts!`），仅消除既有警告，不改变计算与分支语义。

## 4. 与 ImageAcquisition V2 的差异

| 维度 | ImageAcquisition V2 | Calibration 本版 |
| --- | --- | --- |
| 画布 | `ContentControl PreviewHost` + 按需创建 `ImageReadOnly`（只读预览） | `ImageEdit` 常驻（ROI/标记拖动/图上取点），空态/错误态用画布上方信息条，绝不折叠/覆盖已设图画布 |
| 端口控件 | 4 个 `LinkableValueEditor` | 1 个 `SourceImagePath`；其余是 `[StepConfig]` 属性 |
| 模式 | 4 个采集模式卡片 | 4 个标定模式卡片；九点/透视/网格共用标定表 |
| 预览工具条 | 适应窗口 / 1:1 / 刷新 | 无工具条（现有视图没有 Fit/zoom 入口，不造新按钮）；保留「取点/取消取点」 |
| 页脚 | 宿主状态胶囊 + 执行/取消/确认 | 不变（宿主外壳，不在插件视图内） |
| 数据表 | 无 | 标定表 8 列 + 逐行命令；窄档列宽压缩到 304px 固定列 + 残差列 |
| 内参 | 无 | 默认折叠的 `Expander`，承载标定板/模型/图来源/自验证 |
| 证据 | 5 张渲染 PNG（`--image-acquisition-render`） | 仅批量冒烟初始态 1 张（`--calibration-render` 不存在，且本轮禁止改 `UIThemeSmokeTest`） |

## 5. 验证

| 项 | 命令 | 结果 |
| --- | --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.Calibration\Plugin.Calibration.csproj -v m` | **0 错误 / 0 警告**；`Modules\Plugin.Calibration.dll` 已刷新 |
| 通用插件视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.Calibration` | **11/11 PASS**；`elements=951`；`LinkableValueEditor.Port=SourceImagePath`；`OnConfirm InputValues=28`；`XAML hex=0`；PNG 已生成 |
| 既有标定视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --calibration` | **12/12 PASS**；ImageEdit 恰 1 个；逐行取点/预填各 9 个且命令绑定；锁定禁用表格；模式切换表格显隐正确 |
| 常规 gate | `dotnet run --project FlowCanvasChecks --no-build` | **2691 通过 / 0 失败**；`[CAL]` 标定插件段与 `[IC]` 内参串联段全部通过 |
| 渲染证据 | `docs/UI设计基线/calibration/evidence/after-render-v1-initial.png` | 初始态（像素当量 + 空画布）；`ImageEdit` 是 HALCON `HwndHost`，`RenderTargetBitmap` 不捕获其内容，真实图需真机窗口 |

> **`--no-build` 产物说明**：`UIThemeSmokeTest` 通过 `ProjectReference` 直接引用 `Plugin.Calibration`，其 `bin\Debug\net9.0-windows\Plugin.Calibration.dll` 是旧副本；`--no-build` 不会刷新它，且 `Assembly.LoadFrom(Modules\...)` 会先命中已加载的同名程序集。为保证冒烟确实跑新视图，本轮**未构建 `UIThemeSmokeTest`、未改其源码**，只把新 `Modules\Plugin.Calibration.dll`（及 PDB）同步到其输出目录后再跑；首次同步前曾出现“冒烟通过但渲染仍是旧视图”的假绿，已用 `elements` 从 1016 变 951、渲染图变为新模式卡片确认。
>
> **构建产物说明**：为排除增量构建残留的旧 BAML，本轮对插件工程执行过 `dotnet clean` + `--no-incremental` 全量重建（依赖工程因此输出 409 条既有可空警告，非本插件代码）；随后普通 `dotnet build` 复跑为 **0 警告 / 0 错误**，`Modules` DLL 与 `bin` DLL SHA256 一致。

## 6. 遗留问题

1. **设计基线待用户确认**：`calibration/README.md` v1 已产出，确认后按规则冻结；若需调整升 v2。
2. **无 `--calibration-render` 模式**：本轮只拿到批量冒烟的初始态 1 张 PNG；4~5 张主态/空态/错误态/其它模式证据需后续在 `UIThemeSmokeTest` 增加模式（共享文件，本轮禁止改）。
3. **内参控件未在冒烟中实例化**：`Expander` 默认收起，`CaltabDescriptorOptions` 等 ComboBox/TextBox 不在可视树，`--plugin-views` 的运行时绑定清单只覆盖可见控件；静态 XAML 已保留全部绑定，reviewer 需展开内参卡复核。
4. **真机未验收**：HALCON 画布真实图像、标记拖动、图上取点、右键适应图片、Esc 取消取点需真机走查；`RenderTargetBitmap` 不捕获 `HwndHost`。
5. **640 窄档未单独渲染**：WPF 根 `MinWidth=560`，两栏 Min 340 + 200；`UpdateNarrowLayout()` 在 `ActualWidth < 720` 时隐藏状态栏路径与第二个画布角标。设计稿有 640 档，建议真机或后续渲染模式复核左栏表格与滚动条。
6. **`docs/UI设计基线/README.md` 未更新**：按硬约束不改共享索引，由主会话统一追加 `calibration` 条目。
7. **缺失令牌**：无。视图只消费既有 `Dialog*` / `PluginPreview*` 公共键，未新增令牌。

## 7. 复跑命令

```text
dotnet build Plugins\Plugin.Calibration\Plugin.Calibration.csproj -v m
dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.Calibration
dotnet run --project UIThemeSmokeTest --no-build -- --calibration
dotnet run --project FlowCanvasChecks --no-build
```

## 8. Reviewer P1/P2/P3 修复（2026-10-20 追加）

| 项 | 修复 |
| --- | --- |
| **P1** | 删除 `ImageEdit` 同一 Grid 内的空态/错误态覆盖层与两个画布角标；改为画布上方 **Auto 行信息条**（`CanvasImage == null` → Warning 橙；`StatusLevel == Error` → 红，文本 `StatusMessage`，按钮 `BrowseImageCommand`）；两个角标移到画布头（第二个 `CanvasHintChip`）。`ImageEdit` 常驻，六条绑定逐字未动。 |
| **P2** | `LinkableValueEditor`（`x:Name=SourceImageEditor`）的 `IsLinked` 为真时，外置「浏览…」与空态信息条「选择标定图」按钮**隐藏**（`Visibility` 绑 `ElementName=SourceImageEditor, Path=IsLinked` + `InverseBooleanToVisibilityConverter`）；链接/解除仍走控件自带链接图标，链接语义未变。未采用“BrowseImage 后显式 EnsurePreviewLoaded”方案（避免改业务方法）。 |
| **P3** | 根 `MinWidth` 620→**560**；两栏 MinWidth 360/210→**340/200**，间距 14→12；code-behind 新增 `UpdateNarrowLayout()`（`ActualWidth < 720` 隐藏 `StatusPath` 与 `CanvasHintChip`）；`Expander Background="Transparent"` 保留并在 README/XAML 注释记录；新增局部隐式 `Style TargetType="ToolTip" BasedOn="{StaticResource DialogToolTipStyle}"`，覆盖视图内全部 ToolTip。 |

**修复后验证**：

| 项 | 结果 |
| --- | --- |
| 插件构建 | **0 错误 / 0 警告** |
| `--plugin-views Plugin.Calibration` | **11/11 PASS**；`elements=951` |
| `--calibration` | **12/12 PASS** |
| `FlowCanvasChecks --no-build` | **2691 通过 / 0 失败** |
| 渲染 | `evidence/after-render-v1-initial.png` 已更新；空态为画布上方橙色信息条，画布内无 WPF 覆盖层 |

**P3 证据口径**：640 窄档未单独渲染，已写入 README「已知偏差」第 10 条；设计稿含 640 档（`design.html` 的 640 切换 + `.cal-canvas-hint`/`.cal-status-path` 隐藏）。

## 9. A 组遗留问题修复（2026-10-20 追加）

| ID | 改动 |
| --- | --- |
| CAL1 | 空态信息条文案拆分：`CanvasImage == null && StatusLevel != Error` 固定「请选择标定图或接入实时图」；`StatusLevel == Error` 才显示 `StatusMessage`。不改插件状态。 |
| CAL3 | `CalibrationView.xaml` 左栏 `MaxWidth` 480→**420**；同步 README §7 的 `1.15*` / `340` / `420` 口径。 |
| CAL4 | README §7 的「左 `1*`」改为「左 `1.15*`」；§9 #8 改为「Web 画布内居中；WPF 因 HwndHost airspace 改为上方信息条」。 |
| CAL5 | 根 `Grid` 加 `Typography.NumeralAlignment="Tabular"`；`ModeCardStyle` 在 `IsChecked` 前补 `IsPressed=True` 触发器（`DialogRowHoverBrush`）。 |

**未做**：CAL2 经核查不属实，按清单约定不修改错误态按钮文案。

**验证**：`dotnet build Plugins\Plugin.Calibration\Plugin.Calibration.csproj -v m` → **0 错误 / 0 警告**；`--plugin-views Plugin.Calibration` → **11/11 PASS**（`elements=951`、`XAML hex=0`、`OnConfirm InputValues=28`）。

