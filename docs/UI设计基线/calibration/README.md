# 标定配置窗口 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 日期 | 2026-10-20 |
| 确认状态 | 待用户确认（本轮按 SOP 先产出设计基线，再复刻 WPF） |
| 覆盖范围 | `Plugins\Plugin.Calibration\CalibrationView.xaml`（插件内容区）+ 宿主 `PluginConfigShellView` 的 960×680 外壳 |
| 窗口尺寸 | 960×680 默认；640×680 窄档；宿主最小 640×560 |
| 设计文件 | `design.html`（自包含、无外部依赖、可点击；含正常/空态/错误、四种模式、锁定态、取点态） |
| 复刻状态 | 已完成 WPF 复刻（2026-10-20）；证据 `evidence/after-render-v1-initial.png`；冒烟 `--plugin-views` 11/11、`--calibration` 12/12；详见 `docs/code-changes/2026-10-20-Calibration-Web化.md` |

## 1. 定位与范围

标定插件的配置视图负责四件事：选择标定图、在图上取点、维护标定表/内参、读取质量与锁定状态。
本设计基线**只改视觉与信息架构**，不改业务：

- 保留 `DataContext = CalibrationPlugin`（插件与 ViewModel 合一）；
- 保留 `LinkableValueEditor`（`SourceImagePath` 唯一端口入口）；
- 保留 `h:ImageEdit` 画布与 `HImage` / `DrawObjectList` / `ActiveRoi` / `Annotations` / `IsPickMode` 绑定；
- 保留全部 `Command`、`Text`、`SelectedItem` / `SelectedValue`、`StatusLevel` 绑定；
- 不新增独立 ViewModel，不改端口名、命令名、持久化字段与 HALCON 交互语义。

## 2. 现状审查（改动理由）

| # | 现状 | 影响 |
| --- | --- | --- |
| 1 | 四张卡片纵向堆叠，模式是四个裸 `RadioButton`，无选中态容器 | 模式不可一眼扫读；窄窗口下四个单选项换行后高度不可控 |
| 2 | 标定图、像素当量、标定表、内参、质量卡全部常驻展开 | 左栏很长，内参标定长期占用首屏；用户每次都要滚过二期功能 |
| 3 | 表格 8 列固定宽度（30+60×4+44×2+*） | 左栏窄时横向溢出；行状态只靠点名颜色，缺少表头层次 |
| 4 | 右侧只有画布 + 一行状态，取点待命只有一条橙色横幅 | 画布缺少文件头、来源/只读标识、空态与错误态说明 |
| 5 | 视图内存在硬编码颜色：`#E0E0E0`、`White` 等 | 违反视图 `hex=0` 纪律；换肤时改不动 |
| 6 | 内参标定与质量卡没有视觉分组，按钮和说明文字同级 | 生产参数与诊断信息混在一起，现场不易定位 |

## 3. 设计方向

> 左侧配置轨、右侧标定画布；模式先行、参数分段、内参折叠、状态前置。

1. **两栏固定分工**：左 420px 配置轨（窄档 340px），右侧画布自适应；左栏内部滚动。
2. **模式是卡片**：2×2 卡片（像素当量 / 九点 / 透视 / 网格），图标 + 名称 + 用途 + 选中对勾；WPF 仍是 `RadioButton` + `EnumConverter`。
3. **标定图只包裹**：`LinkableValueEditor` 设 `Label=""` 外置标签；浏览按钮外置，仍绑 `BrowseImageCommand`；**`LinkableValueEditor.IsLinked` 为真（已链接变量）时外置浏览按钮隐藏**（链接态写端口值不会触发 `ValueChanged`，留着就是静默 no-op），链接/解除仍走控件自带 🔗/✕。
4. **像素当量与标定表互斥**：像素当量显示已知长度 + A/B 两点；九点/透视/网格显示网格规模 + 标定表；显隐仍由 `ShowPointTable` 与 `Mode` 驱动。
5. **标定表可读**：表头分层、行状态（有效/半填/空）用点名颜色区分；逐行「取点」「预填」按钮保留，命令与 `CommandParameter` 不变。
6. **内参折叠**：`Expander` 默认收起；展开后标定板、相机模型、像元、图来源、动作、质量与自验证结果与现状一一对应。
7. **画布常驻**：`ImageEdit` 不因无图而 `Collapsed`；空态/错误态改为**画布上方 Auto 行信息条**（Warning 橙 / Error 红；空态固定文案「请选择标定图或接入实时图」，错误态显示 `StatusMessage`；按钮 `BrowseImageCommand`），**不在 `ImageEdit` 的 Grid 内放 WPF 覆盖层**（HALCON `HwndHost` 会盖住 WPF 层，真机按钮点不到）。
8. **取点态前置**：`IsPickingPoint` 为真时画布上方显示橙色横幅与「取消取点」按钮（`CancelPickCommand` 已存在）。
9. **全令牌化**：颜色只用 `Dialog*` 与 `PluginPreview*` 公共令牌；视图 XAML `hex=0`。
10. **五态可达性**：hover / pressed / focus / disabled 均定义；图标按钮 30×30 且 `Padding="0"`；焦点环 `DialogFocusVisual`；ToolTip `DialogToolTipStyle`。

## 4. 设计要点（完整清单）

1. 模式卡片：选中 = 主色描边 + 主色浅底 + 右侧对勾；未选中 = 白底 + 浅描边；悬停加深描边；锁定态整体降透明度。
2. 表单节奏：标签 12px / 控件 36px / 帮助 11.5px；字段间距 12px；输入区随栏宽伸缩。
3. 标定图：路径行 + 「浏览」+ 变量链接；下方两行说明（尺寸记录、实时图借入语义）。
4. 像素当量：已知长度 + 「取A点」「取B点」；跨度提示用绿色/橙色（`ScaleSpanLevel`）；A/B 四点两列网格。
5. 标定表：网格规模下拉 + 「重建网格」；表格 8 列；行状态颜色与 `StateText` 提示；底部「粘贴 N 行」「清空表格」「把点画到图上」。
6. 内参：默认折叠；标定板下拉 + 相机模型单选 + 像元 + 文件夹 + 图列表 + 三个动作 + 质量/结果/自验证 + 去畸变开关。
7. 质量卡：质量文本（按 `QualityLevel` 变色）+ 诊断提示；相机序列号 + 锁定；导入/导出；三个护栏数值。
8. 画布头：文件图标 + 文件名 + 「标定图 · 只读」+ 两个画布角标（「标定画布」「滑轮缩放 · 右键适应图片」；窄档隐藏第二个）。
9. 画布：深色底；空态/错误态用画布上方信息条表达（Warning 橙 / Error 红），不把 WPF 覆盖层放进 `ImageEdit`；空态按钮「选择标定图」只在未链接时显示。
10. 状态栏：状态点 + `StatusMessage`（左）+ `PreviewImagePath`（右）；只有 Info / Warning / Error 三档；窄档（`ActualWidth < 720`）隐藏路径。
11. 底部宿主栏：状态胶囊 + 耗时 + 执行/取消/确认；属于宿主外壳，插件视图不改。
12. 数字等宽：A/B 坐标、表格数值、残差、路径使用 tabular-nums。

## 5. 契约保留（一个都不能改）

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 宿主靠 `viewObj.DataContext as IPluginConfigView` 取 `OnConfirm`；换独立 VM = 确认不保存 |
| C2 | `LinkableValueEditor`：`Port=SourceImagePath`、`StepData`、`BrowseCommand=BrowseImageCommand` | 端口持久化、类型转换、`SetLink/RemoveLink`、变量绑定弹窗都在控件里 |
| C3 | `h:ImageEdit`：`HImage=CanvasImage`、`DrawObjectList=CanvasMarkers`、`ActiveRoi=CanvasActiveRoi`、`Annotations=ResidualAnnotations`、`IsPickMode=IsPickingPoint`、`ImagePicked=OnImagePicked` | HALCON 缩放/右键菜单/标记拖动/图上取点全部依赖这些通道 |
| C4 | `Mode` / `GridSize` / `CaltabDescrFile` / `IntrinsicsModel` / `UndistortBeforeCalibration` | 枚举 / int / string / 枚举 / bool；落盘与执行分支依赖类型 |
| C5 | 全部命令：`BrowseImage`、`RebuildGrid`、`PasteRows`、`ClearRows`、`PushMarkers`、`PickRowPoint`、`PickScalePoint`、`CancelPick`、`ExportCalibration`、`ImportCalibration`、`PrefillRowFromSource`、`BrowseIntrinsicsFolder`、`RunIntrinsicsCalibration`、`RunIntrinsicsSelfVerify`、`ClearIntrinsics` | 这些是功能入口，不是装饰 |
| C6 | `StatusMessage` / `StatusLevel` 三档；`QualityLevel`、`ScaleSpanLevel`、`IntrinsicsResultLevel`、`IntrinsicsSelfVerifyLevel` | 状态只有 Info / Warning / Error，本版不扩展 |
| C7 | `IsEditable`（锁定）与 `IsIntrinsicsIdle`（内参忙闲） | 锁定/忙闲是业务护栏，不能用视觉态替代 |

## 6. 令牌映射

| 设计变量 | WPF 资源键 |
| --- | --- |
| 表面 / 卡片 | `DialogSurfaceBrush` |
| 左栏底 / 行交替 | `DialogRowAlternateBrush` |
| 悬停底 | `DialogRowHoverBrush` |
| 细描边 / 分隔 | `DialogBorderLightBrush` |
| 常规描边 | `DialogBorderBrush` |
| 强描边 | `DialogBorderStrongBrush` |
| 主文字 / 次文字 / 弱文字 | `DialogTextPrimaryBrush` / `DialogTextSecondaryBrush` / `DialogTextMutedBrush` |
| 主色 / 深主色 / 浅主色 / 主色浅底 / 主色描边 / 主色文字 | `DialogAccentBrush` / `DialogAccentDarkBrush` / `DialogAccentLightBrush` / `DialogAccentSoftBrush` / `DialogAccentBorderBrush` / `DialogAccentTextBrush` |
| 成功 / 成功文字 / 成功浅底 / 成功描边 | `DialogSuccessBrush` / `DialogSuccessTextBrush` / `DialogSuccessSubtleBrush` / `DialogSuccessBorderBrush` |
| 警告 / 警告文字 / 警告浅底 / 警告描边 | `DialogWarningBrush` / `DialogWarningTextBrush` / `DialogWarningSubtleBrush` / `DialogWarningBorderBrush` |
| 错误 / 错误文字 / 错误浅底 / 错误描边 | `DialogDangerBrush` / `DialogDangerTextBrush` / `DialogDangerSubtleBrush` / `DialogDangerBorderBrush` |
| 画布底 / 画布表面 / 画布描边 / 画布文字 / 画布弱文字 | `PluginPreviewCanvasBrush` / `PluginPreviewSurfaceBrush` / `PluginPreviewBorderBrush` / `PluginPreviewTextBrush` / `PluginPreviewMutedTextBrush` |
| 图标字体 | `DialogIconFont` |
| 数值输入框 | `PluginNumericBox`（保留既有校验红框/悬停原因） |
| 下拉 | `DialogCombo` / `DialogComboItem` |
| 复选框 | `DialogCheckBox` |
| 焦点环 / ToolTip | `DialogFocusVisual` / `DialogToolTipStyle` |

**缺失令牌**：无。视图只消费既有公共键；未新增、未修改任何共享字典。

## 7. 复刻实现映射

| 设计项 | WPF 实现 |
| --- | --- |
| 2×2 模式卡片 | 4 个 `RadioButton` + 局部 `ModeCardStyle`；`IsChecked` 仍走 `ui:EnumConverter` 写 `Mode` |
| 单页两栏 | `Grid` 两列（左 `1.15*` Min 340 Max 420；右 `1*` Min 200），左栏 `ScrollViewer`；根 `MinWidth=560` |
| 标定图 | `LinkableValueEditor Label=""` + 外置标签 + 外置「浏览」按钮（`BrowseImageCommand`）；`LinkableValueEditor.IsLinked` 为真时按钮经 `InverseBooleanToVisibilityConverter` 隐藏 |
| 像素当量 | `EnumToVisibilityConverter` 绑 `Mode`；A/B `NumericBox`；跨度文本按 `ScaleSpanLevel` 触发颜色 |
| 标定表 | `ItemsControl` 绑 `PointRows`；表头 Grid；逐行 `PickRowPointCommand` / `PrefillRowFromSourceCommand`；外层 `Border` 的 `Visibility` 绑 `ShowPointTable` |
| 内参 | `Expander` 默认收起；字段/命令/结果绑定原样 |
| 画布 | `h:ImageEdit` 常驻；空态/错误态信息条在画布上方 Auto 行（`CanvasImage == null` 显示固定文案 / `StatusLevel == Error` 显示 `StatusMessage`），画布 Grid 内无 WPF 覆盖层 |
| 画布头 | 文件名 + 两个角标（第二个 `CanvasHintChip` 在 `ActualWidth < 720` 时隐藏） |
| 取点横幅 | `Border Visibility=IsPickingPoint` + `PickHintText` + `CancelPickCommand` |
| 状态栏 | `StatusMessage` / `StatusLevel`；`PreviewImagePath` 右侧（`StatusPath` 在 `ActualWidth < 720` 时隐藏） |
| 令牌 | 视图零十六进制；无新增公共令牌 |

## 8. 复刻证据

| 项 | 结果 |
| --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.Calibration\Plugin.Calibration.csproj -v m`：**0 错误 / 0 警告**；`Modules\Plugin.Calibration.dll` 已刷新 |
| 通用插件视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.Calibration`：**11/11 PASS**（`elements=951`、`Port=SourceImagePath`、`OnConfirm InputValues=28`、`XAML hex=0`） |
| 既有标定冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --calibration`：**12/12 PASS**（ImageEdit 1 个、逐行取点/预填 9+9、锁定/模式显隐） |
| 常规 gate | `dotnet run --project FlowCanvasChecks --no-build`：**2691 通过 / 0 失败**（含 `[CAL]` / `[IC]` 段） |
| 渲染证据 | `evidence/after-render-v1-initial.png`（初始态；HALCON `HwndHost` 不被 `RenderTargetBitmap` 捕获，真实图需真机） |
| Reviewer 修复 | P1 空态/错误态改为画布上方信息条；P2 链接态隐藏外置浏览/空态按钮；P3 `MinWidth=560` + `UpdateNarrowLayout`；详见 `docs/code-changes/2026-10-20-Calibration-Web化.md` §8 |

## 9. 已知偏差
1. **图标字体**：Web 用 Lucide，WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`）；形状不同，语义一致。
2. **画布内容**：Web 用内联 SVG 标定板示意图；WPF 是真实 `HImage`（`RenderTargetBitmap` 不捕获 HALCON `HwndHost`）。
3. **阴影/动效**：Web 窗口阴影为 CSS 多层阴影；WPF 用宿主现有 `DropShadowEffect`；动效统一 150~200ms。
4. **字体度量**：同字号下 Segoe UI / 微软雅黑与 Web 的字宽、行高略有差异，不按 px 硬套。
5. **滚动条**：WPF 左栏滚动条约占 17px，窄档可用宽度比 Web 少约 17px。
6. **内参折叠**：Web 用 `<details>`，WPF 用 Fluent `Expander`；展开/收起行为一致，头样式不完全自绘。
7. **表格行高**：WPF `NumericBox` 高度 30px（Web 26px），保证输入可点；表格列宽按左栏 340px 最小档压缩。
8. **画布空态**：Web 画布内居中；WPF 因 HwndHost airspace 改为上方信息条（**不在 `ImageEdit` 的 Grid 内放任何 WPF 覆盖层**）。
9. **渲染证据范围**：`ImageEdit` 是 HALCON `HwndHost`，`RenderTargetBitmap` 不捕获其内容；证据 PNG 展示的是空态布局，真实图像预览需真机窗口验证。
10. **640 窄档未单独渲染**：设计稿含 640 档；WPF 根 `MinWidth=560`，`UpdateNarrowLayout()` 在 `ActualWidth < 720` 时隐藏状态栏路径与第二个画布角标。建议真机或后续渲染模式复核。
11. **`Expander Background="Transparent"`**：结构透明（不参与配色），不是硬编码颜色令牌；已在 XAML 注释与本条记录。
12. **ToolTip 样式**：视图内所有 `ToolTip` 通过局部隐式 `Style TargetType="ToolTip" BasedOn="{StaticResource DialogToolTipStyle}"` 统一；未逐个改 `ToolTip` 属性。

## 10. 未纳入本版（记录，不改）

- **加载骨架 / 蓝色加载态**：`StatusLevel` 只有三档，需新增 `PreviewState`/`IsPreviewLoading`。
- **文件大小**：需新增只读属性（`FileInfo.Length`）。
- **放大 / 缩小按钮**：`HalconBase` 只有 `FitToWindow()` / `FitToImage()`，没有公开 zoom 方法；本视图不新增。
- **打开所在文件夹**：需新增 `Process.Start("explorer.exe", ...)` 行为。
- **系统导航**：无宿主导航契约。
- **拖拽入图**：当前无此功能。
- **ROI 工具新增**：标定板/ROI 工具与标定参数不改业务语义；不新增 ROI 类型。

## 11. A 组遗留问题修复（2026-10-20）

| ID | 修复 |
| --- | --- |
| CAL1 | 空态信息条文案拆分：`CanvasImage == null && StatusLevel != Error` 固定显示「请选择标定图或接入实时图」；`StatusLevel == Error` 显示 `StatusMessage`。只改视图，不改插件状态。 |
| CAL3 | 左栏 `MaxWidth` 480→**420**；README 的 `1.15*` / `340` / `420` 口径统一（本文件为唯一事实源）。 |
| CAL4 | 本 README §7 由「左 `1*` Max 480」改为「左 `1.15*` Min 340 Max 420」；§9 #8 改为「Web 画布内居中；WPF 因 HwndHost airspace 改为上方信息条」。 |
| CAL5 | 根 `Grid` 加 `Typography.NumeralAlignment="Tabular"`（端口/表格/状态数字继承）；`ModeCardStyle` 补 `IsPressed=True` 触发器（`Background=DialogRowHoverBrush`，位于 `IsChecked` 之前）。 |

**未做**：CAL2（核查不属实，不改错误态按钮文案）。

验证：`dotnet build Plugins\Plugin.Calibration\Plugin.Calibration.csproj -v m` **0 错误 / 0 警告**；`--plugin-views Plugin.Calibration` **11/11 PASS**（`elements=951`）。

## 12. 变更规则

设计基线确认后只读。需要调整设计时升版本（v2），同步更新本 README、令牌映射与 `CalibrationView.xaml` 的复刻映射。
