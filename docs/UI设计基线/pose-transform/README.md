# 坐标变换（PoseTransform）配置窗口 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 设计日期 | 2026-10-20 |
| 确认日期 / 人 | 待用户确认 |
| 覆盖范围 | `Plugins\Plugin.PoseTransform\PoseTransformView.xaml`（插件内容区）+ `PoseTransformView.xaml.cs`（仅 UI 转换器/处理器）；宿主 `PluginConfigShellView` 不改 |
| 窗口尺寸 | 960×680 默认；640×560 窄档（宿主最小 640×560，2026-10-20 统一口径） |
| 设计文件 | `design.html`（v1，自包含、无外部依赖、内联 SVG 图标与示意预览；模式/状态/宽度可点击切换） |
| 复刻状态 | 已完成（2026-10-20）；证据 `docs/UI设计基线/pose-transform/evidence/after-render-v1-initial.png`（另见 `_batch-evidence/Plugin.PoseTransform-PoseTransformPlugin.png`）；设计仍待用户确认 |
| 设计语言 | 沿用 ImageAcquisition V2（`docs/UI设计基线/image-acquisition/`）：两栏 / 卡片分区 / 36px 控件 / 12px 标签 / 11.5px 帮助文字 / 8px 圆角 / 公共令牌 / 五态 |

## 1. 定位

本文件是「插件自定义视图 Web 化」的第 ① 步产物（设计基线）。Web 只是设计媒介，**不进入构建、不部署**。
确认后按本基线做第 ③ 步（WPF 复刻），第 ④ 步以「同一屏、同一状态」的截图对照验收。

本次只改 `Plugin.PoseTransform` 的配置视图；**不改**插件业务逻辑、端口名、命令名、持久化，也不改共享控件/共享字典。

## 2. 现状审查（改动理由）

| # | 现状（改动前） | 影响 |
| --- | --- | --- |
| 1 | 左栏是 6 张 `PluginCard` 竖排，右栏只有 `ImageEdit` + 一行状态（`PoseTransformView.xaml:28-313`） | 模式选项、参考图、两种模式输入、标定信息、预览图混在一条长滚动带里，层级平均 |
| 2 | 模式用两个裸 `RadioButton`（`:44-52`） | 模式是两套完全不同的数据流，却没有卡片化的选中态与用途说明 |
| 3 | 11 个 `LinkableValueEditor` 全部靠控件自带的 112px 内联标签 | 表单节奏与 ImageAcquisition V2 的外置标签不一致；窄栏下输入区被标签挤压 |
| 4 | `ImageEdit` 只包一层 `Border`，`BorderBrush="#E0E0E0"` 硬编码（`:269-278`） | 违反颜色令牌纪律；预览区没有标题/模式标签/空态提示 |
| 5 | 状态栏与标定信息都是裸 `TextBlock`，只有颜色触发器（`:208-234`、`:279-311`） | 标定信息是 ToMechanical 的关键判据，却没有独立的信息块层级 |
| 6 | 预览底图路径用 `PluginNumericBox` 固定 `Width=240`（`:241-245`） | 路径是文本而非数值；固定宽度在窄档下与「浏览」按钮争空间 |
| 7 | 根 `MinWidth=620`（`:11-12`） | 与宿主最小 640×560 的内容区（约 584~600px）不匹配，窄档可能横向溢出 |
| 8 | 视图内出现 `#E0E0E0` 硬编码 | 换肤/统一调整时改不动；违反「视图 XAML hex=0」 |

## 3. v1 方向

> **模式先行、端口原样、画布常驻；只做纯 UI 重构，不改功能入口与绑定。**

1. **两栏固定分工**：左 356px 配置轨（模式卡片 + 模式专属参数 + 公共参考图/预览底图），右侧叠加预览工作区自适应。
2. **模式是卡片，不是单选**：2 张卡片（位姿跟随 / 像素→机械），图标 + 名称 + 用途 + 选中态同屏；仍是 `RadioButton` + `EnumConverter`，照常写 `Mode`。
3. **11 个端口逐项保留**：`BaseRegion` / `TemplateRefRow` / `TemplateRefCol` / `PoseRow` / `PoseCol` / `PoseAngle` / `Transform` / `PixelPointRow` / `PixelPointCol` / `PixelAngle` / `SrcImage`；`Port`/`StepData` 原样绑定，**不替换成普通输入框**。
4. **ImageEdit 常驻**：不折叠、不换成静态图；`HImage="{Binding DisplayImage, Mode=OneWay}"` 逐字保留。空态只在画布上方显示提示条（避免 HwndHost 折叠后不重绘）。
5. **模板参考点可读**：派生开启时两个参考点输入置灰（仍保留端口与链接入口）；「取基准 ROI 中心」按钮接现有 `TakeRefFromRegionCenterCommand`。
6. **标定信息独立成块**：ToMechanical 的 `CalibrationInfoText` 用三档颜色（Info 绿 / Warning 橙 / Error 红），与 `CalibrationInfoLevel` 一一对应。
7. **状态栏三档**：`StatusMessage` + `StatusLevel`（Info / Warning / Error）原样消费；空串显示「就绪」，不新增第四档。
8. **全令牌化 + 五态**：颜色全部走 `Colors.xaml` / `PluginConfigColors.xaml`；图标按钮 30×30 + `Padding="0"`（R68），焦点环 `DialogFocusVisual`，ToolTip `DialogToolTipStyle`（R135）。

## 4. 设计要点（完整清单）

1. 左栏是「配置轨」：底色 `DialogRowAlternateBrush`、1px `DialogBorderLightBrush`、圆角 8；内部用卡片分组，不再层层套卡片。
2. 表单节奏：标签 12px、控件 36px、帮助 11.5px；字段间距 10~12px；输入区随栏宽伸缩。
3. 端口行：`LinkableValueEditor` 设 `Label=""`，外置标签；保留控件自带的 🔗/✕；HImage/HRegion/CalibrationTransform 这类不可文本输入端口只显示「需链接变量」+ 链接按钮。
4. 位姿 Row/Col、参考点 Row/Col、像素点 Row/Col 各用两列栅格；角度/路径整行。
5. 模板参考点：勾选「由基准 ROI 中心派生」时两个参考点输入置灰；「取基准 ROI 中心」是现有命令，不是新功能。
6. 输出：`EmitFollowedImage` 是现有 `[StepConfig]` 复选框；帮助文字说明与「模板匹配」对齐图的分工。
7. 标定信息：`CalibrationInfoText` 只读块；Info 绿 / Warning 橙 / Error 红；不新增字段。
8. 预览工作区：44px 标题栏（图标 + 「叠加预览」+ 模式标签 + 适应窗口/1:1）+ 可选提示条 + 深色画布 + 40px 状态栏。
9. 画布：`ImageEdit` 常驻；空态用画布上方提示条表达；右下角图例说明绿/橙/黄三种叠加色。
10. 状态栏：状态点 + `StatusMessage`（左）+ `FollowedInfoText`（右）；只有 Info / Warning / Error 三档。
11. 底部宿主栏：状态胶囊（宿主 `StatusText`）+ 耗时 + 执行 / 取消 / 确认；插件视图不改它。
12. 数字等宽：位姿、像素点、残差等使用 tabular-nums（WPF 用 `Typography.NumeralAlignment="Tabular"`）。

## 5. 状态与边界

| 状态 | 设计表现 | WPF 触发条件 |
| --- | --- | --- |
| 默认（位姿跟随 · 就绪） | 基准 ROI 已链接、位姿有效；绿/橙 ROI + 黄十字；状态栏绿色「就绪」 | `Mode=FollowRoi` 且 `BaseRegion` 有效、位姿有效 |
| 警告（基准 ROI 未接） | 基准 ROI 行显示「需链接变量」；画布只显示底图；状态栏橙色 | `Mode=FollowRoi` 且 `BaseRegion` 为空 |
| 错误（预览图加载失败） | 画布上方红色提示条「预览底图加载失败」；状态栏红色 | `DisplayImage == null` 且 `StatusLevel == Error` |
| 空预览 | 画布上方中性提示条「预览底图未加载」；`ImageEdit` 仍常驻 | `DisplayImage == null` |
| 像素→机械 · 就绪 | 标定信息绿色；画布叠加绿/橙 ROI + 黄参考点十字（当前实现没有青色像素点/机械轴，见 §9 已知偏差）；状态栏绿色 | `Mode=ToMechanical` 且 `Transform` 可用 |
| 像素→机械 · 残差偏大 | 标定信息橙色；状态栏仍绿色（运行可用，但提示复核） | `CalibrationInfoLevel == Warning` |
| 像素→机械 · 未接标定 | 标定信息红色；状态栏橙色；画布上方中性提示条（当前实现没有橙色警示条，见 §9 已知偏差） | `Transform == null` 或 `CheckCalibrationUsable` 失败 |
| 窄档 640×560 | 左栏 300px、右栏自适应；预览头模式标签保留，状态栏右侧 `FollowedInfoText` 可截断 | 宿主内容区约 600px |

## 6. 契约保留（一个都不能改）

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 宿主靠 `viewObj.DataContext as IPluginConfigView` 取 `OnConfirm`；换独立 VM = 确认不保存 |
| C2 | 11 个 `LinkableValueEditor`：`BaseRegion` / `TemplateRefRow` / `TemplateRefCol` / `PoseRow` / `PoseCol` / `PoseAngle` / `Transform` / `PixelPointRow` / `PixelPointCol` / `PixelAngle` / `SrcImage` | 端口持久化、类型转换、`ValueChanged` 订阅、`StepData.SetLink/RemoveLink`、变量绑定弹窗都在控件里 |
| C3 | `ImageEdit.HImage = DisplayImage`（`Mode=OneWay`） | 保留 HALCON 显示/缩放/右键菜单；本插件当前未使用 `DrawObjectList` / `ActiveRoi`，也不得在本轮新增/替换 |
| C4 | `Mode` 是 `TransformMode` 枚举 | `ui:EnumConverter` 双向；不能改成 int/string |
| C5 | `BrowsePreviewCommand` / `TakeRefFromRegionCenterCommand` + `StatusMessage`/`StatusLevel` 三档 | 这些是功能入口；状态只有 Info / Warning / Error，V1 不扩展 |
| C6 | `Mode` / `EmitFollowedImage` / `TemplateRefFromRegionCenter` / `PreviewImagePath` 的 `[StepConfig]` 键名 | 落盘键名一个不改，旧方案可继续加载 |

## 7. 令牌映射

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--pt-surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--pt-surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--pt-surface-3` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--pt-border` | `#E2E8F0` | `DialogBorderBrush` |
| `--pt-border-soft` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--pt-border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` |
| `--pt-text` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--pt-text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--pt-text-3` | `#94A3B8` | `DialogTextMutedBrush` |
| `--pt-text-4` | `#64748B` | `ScadaSecondaryTextBrush`（`#66708A`，近似） |
| `--pt-primary` | `#0EA5E9` | `DialogAccentBrush` |
| `--pt-primary-strong` | `#0284C7` | `DialogAccentDarkBrush` |
| `--pt-primary-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--pt-primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--pt-primary-light` | `#E0F2FE` | `DialogAccentLightBrush` |
| `--pt-primary-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--pt-success` | `#22C55E` | `DialogSuccessBrush` |
| `--pt-success-text` | `#15803D` | `DialogSuccessTextBrush` |
| `--pt-success-soft` | `#F0FDF4` | `DialogSuccessSubtleBrush` |
| `--pt-success-border` | `#BBF7D0` | `DialogSuccessBorderBrush` |
| `--pt-danger` | `#EF4444` | `DialogDangerBrush` |
| `--pt-danger-text` | `#B91C1C` | `DialogDangerTextBrush` |
| `--pt-danger-soft` | `#FEF2F2` | `DialogDangerSubtleBrush` |
| `--pt-danger-border` | `#FECACA` | `DialogDangerBorderBrush` |
| `--pt-warning` | `#F59E0B` | `DialogWarningBrush` |
| `--pt-warning-text` | `#B45309` | `DialogWarningTextBrush` |
| `--pt-warning-soft` | `#FFF4E5` | `DialogWarningSubtleBrush` |
| `--pt-warning-border` | `#FDE68A` | `DialogWarningBorderBrush` |
| 深色画布 | `#0B1220` | `PluginPreviewCanvasBrush` |
| 画布浮层/深色按钮 | `#1E293B` | `PluginPreviewSurfaceBrush` |
| 画布描边 | `#334155` | `PluginPreviewBorderBrush` |
| 画布文字 | `#E2E8F0` | `PluginPreviewTextBrush` |
| 画布弱文字 | `#94A3B8` | `PluginPreviewMutedTextBrush` |
| 图标字体 | Font Awesome 6 Pro Solid | `DialogIconFont` |
| 焦点环 | — | `DialogFocusVisual` |
| 工具提示 | — | `DialogToolTipStyle` |
| 圆角 8 | — | `PluginCardCornerRadius` |

**缺失令牌：无。** 本设计所需的颜色/字体/焦点/提示键均已在公共字典中存在，无需追加。

## 7.1 复刻实现（第 ③ 步，已完成）

| 设计项 | WPF 实现 |
| --- | --- |
| 两栏布局 | `Grid` 三列：左 `1*`（MinWidth 300 / MaxWidth 356）+ 16px 间隔 + 右 `1.5*`（MinWidth 240）；左栏 `ScrollViewer` 承载配置轨 |
| 2 张模式卡片 | 2 个 `RadioButton` + `ModeCardStyle`；`IsChecked` 仍走 `ui:EnumConverter` 写 `Mode` |
| 11 个端口 | 11 个 `cv:LinkableValueEditor`，全部保留 `Port` / `StepData`，`Label=""` 外置标签；位姿/参考点/像素点各用两列栅格 |
| 模板参考点置灰 | `StackPanel` + `DataTrigger Binding="{Binding TemplateRefFromRegionCenter}" Value="True"` → `IsEnabled=False` |
| 标定信息 | `TextBlock` + `CalibrationInfoTextStyle`，`CalibrationInfoLevel` 驱动绿/橙/红 |
| 预览工作区 | 44px 标题栏 + 可选提示条 + `h:ImageEdit` 常驻画布 + 40px 状态栏 |
| 空态 | 两个 `Border` 提示条（中性/错误），`MultiDataTrigger` 绑定 `DisplayImage == null` 与 `StatusLevel` |
| 状态栏 | `StatusDotStyle` / `StatusMessageStyle` + `FollowedInfoText`，三档不扩展 |
| 工具条 | 「适应窗口」`FitWindow_Click` → `PreviewCanvas.FitToWindow()`；「1:1」`FitImage_Click` → `PreviewCanvas.FitToImage()` |
| 令牌 | 视图零十六进制；复用既有 `Dialog*` / `PluginConfig*` / `PluginPreview*` 键，未追加公共令牌 |

验证：插件构建 **0 错误 / 0 警告**；`UIThemeSmokeTest --plugin-views Plugin.PoseTransform` **11/11 PASS**（含 11 个端口、`PreviewImagePath`、`OnConfirm`、XAML hex=0）；`ImageEdit.HImage` 绑定由静态核对确认（`HImage="{Binding DisplayImage, Mode=OneWay}"` 逐字保留、控件无 `Visibility` 折叠）；初始态渲染 PNG 已生成。详见 `docs/code-changes/2026-10-20-PoseTransform-Web化.md`。

## 8. 复刻约束（已遵守）

1. 保留视图构造签名 `PoseTransformView(IStepConfigData, PoseTransformPlugin)`、`DataContext = plugin`、`plugin.Initialize(stepData)`、`OnViewAttached/OnViewDetached` 生命周期。
2. 不引入独立 VM；若必须分 VM，VM 必须实现 `IPluginConfigView` 并委托给插件。
3. `LinkableValueEditor` 只包裹、不改内部；`Port` / `StepData` / `BrowseCommand` 原样保留。
4. `ImageEdit` 常驻，**不要**用 `Visibility=Collapsed` 显隐已设图的控件；`HImage` 绑定保持 `Mode=OneWay`。
5. `Mode` 仍用 `ui:EnumConverter`；`TemplateRefFromRegionCenter` 的置灰逻辑仍用 `DataTrigger`。
6. `StatusLevel` 只有三档；不新增 `IsLoading` / `PreviewState`。
7. 共享件不动：`LinkableValueEditor`、`ImageEdit`/`HalconBase`、`PluginConfigStyles.xaml`、`PluginConfigShellView.xaml`。
8. 视图 XAML 内 **0 个十六进制颜色**；颜色全部走公共令牌。
9. 图标按钮 30×30 + `Padding="0"`（R68）；焦点 `DialogFocusVisual`；ToolTip `DialogToolTipStyle`。
10. 不造死按钮：只接 `BrowsePreviewCommand`、`TakeRefFromRegionCenterCommand`、`FitToWindow()`、`FitToImage()` 这些现有入口；放大/缩小、打开文件夹、系统导航不做。

## 9. 已知偏差（复刻时按 R130 记录）

1. **图标字体**：Web 用 Lucide 风格内联 SVG，WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`）；形状不同，语义一致。
2. **阴影/动效**：Web 窗口阴影为 CSS 多层阴影；WPF 用宿主现有 `DropShadowEffect`；动效统一 150~200ms。
3. **预览图**：Web 用内联 SVG 工业零件示意图；WPF 用真实 `HImage` 叠加（基准 ROI 绿、跟随 ROI 橙、参考点黄）。
4. **空态表达**：Web 画布内绘制虚线空态卡；WPF 因 `ImageEdit` 是 HALCON `HwndHost` 且必须常驻，空态改为**画布上方提示条**，画布本身保持常驻。
5. **图例位置**：Web 右下角图例是设计示意；WPF v1 为窄档预览头留出空间，**不单独放图例**，绿/橙/黄的含义写在 `ImageEdit` 的 ToolTip 与预览底图帮助文字里。若后续加图例，应放在预览头或提示条，不要覆盖 HwndHost。
6. **字体度量**：同字号下 Segoe UI / 微软雅黑与 Web 的字宽、行高略有差异，不按 px 硬套。
7. **滚动条**：WPF 左栏滚动条约占 17px，窄档可用宽度比 Web 少约 17px。
8. **渲染证据范围**：`RenderTargetBitmap` 不捕获 HALCON `HwndHost`；证据 PNG 展示 WPF 布局与空态提示，真实图像预览需真机窗口验证。
9. **模式卡片高度**：WPF 用 52~56px，与 ImageAcquisition V2 同档；窄档下帮助文字可滚动。
10. **根最小尺寸**：WPF 根 `MinWidth` 从 620 调整为 560、`MinHeight` 从 300 调整为 280，以适配宿主 640×560 最小档；不写死 900×500。
11. **像素→机械叠加**：设计稿的青色像素点/机械轴未实现；`RefreshOverlay` 只画绿/橙 ROI 与黄参考点十字，状态表已按实际实现修正。
12. **未接标定提示**：设计稿的橙色警示条未实现；WPF 用中性提示条 + 状态栏 `StatusLevel=Warning` 表达，状态表已按实际实现修正。

## 10. 未纳入本版（记录，不改）

- **加载骨架 / 加载态**：需要 VM 新增 `IsPreviewLoading` 或 `PreviewState`；当前 `StatusLevel` 只有三档。
- **尺寸 / 文件大小**：需要 VM 新增只读属性（`HImage.GetImageSize` / `FileInfo.Length`）。
- **放大 / 缩小按钮**：`HalconBase` 只有 `FitToWindow()` / `FitToImage()`，没有公开 zoom 方法。
- **打开所在文件夹**：需要新增 `Process.Start("explorer.exe", ...)` 行为。
- **系统导航**（如跳转标定插件）：需要新的宿主导航契约。
- **拖拽入图 / 相机实时预览**：不属于坐标变换配置视图。
- **`DrawObjectList` / `ActiveRoi` 绑定**：本插件当前未使用，本轮不新增 ROI 绘制功能。

## 11. A 组遗留问题修复（2026-10-20）

| ID | 修复 |
| --- | --- |
| PT1 | 根 `Grid` 加 `Typography.NumeralAlignment="Tabular"`，端口数值 / 标定信息 / 状态栏数字随继承生效（不再只覆盖 `FollowedInfoText`）。 |
| PT2 | `design.html` 的 `.pt-window.is-narrow` 补 `height:560px`，640 档设计稿与宿主 640×560 口径一致。 |

验证：`dotnet build Plugins\Plugin.PoseTransform\Plugin.PoseTransform.csproj -v m` **0 错误 / 0 警告**；`--plugin-views Plugin.PoseTransform` **11/11 PASS**（`elements=487`）。

## 12. 变更规则

设计基线确认后只读。需要调整设计时升版本（v1→v2），并同步更新已复刻的 `PoseTransformView.xaml` 与本映射表。
