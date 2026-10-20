# 深度学习分类配置窗口 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 日期 | 2026-10-20 |
| 确认状态 | 待用户确认（本页为 Web 设计基线；WPF 复刻同批完成，验收后冻结） |
| 覆盖范围 | `Plugins\Plugin.DlClassify\ClassifyView.xaml`（插件内容区）+ `ClassifyView.xaml.cs`（仅 UI 转换器/命令转发） |
| 窗口尺寸 | 960×680 默认；640×680 窄档；宿主最小 640×560（2026-10-20 统一） |
| 设计文件 | `design.html`（v1，自包含、内联 SVG、可点击切换默认/空态/模型错误与 960/640 两档） |
| 插件复杂度 | S（单预览 + 1 个 `LinkableValueEditor` + 2 个 `NumericBox`） |
| 参考实现 | `docs/UI设计基线/image-acquisition/README.md` v2 + `design.html`；`docs/code-changes/2026-10-20-图像采集配置窗口V2复刻与FluentChipDanger重复键修复.md` |

## 1. 定位与范围

本文件是 SOP §5.2 的产物：**Web 只是设计媒介，不进入构建、不部署**。视觉语言沿用 ImageAcquisition V2：
两栏、卡片、36px 控件、12px 标签、11.5px 帮助文字、8px 圆角、公共令牌、五态（hover / pressed / focus / disabled / selected）。

**只做纯 UI 重构**，不改：

- 插件业务逻辑（`TryClassify` / `EnsureSession` / `ResolvePath` / 会话引用计数）；
- 端口名与类型（`Image` / `PredictedClassId` / `PredictedClassName` / `Confidence` / `Scores` / `ClassNames` / `TopKText`）；
- 四个 `[StepConfig]` 属性（`ModelPath` / `ResizeMode` / `InputSize` / `TopK`）与持久化；
- `DataContext = 插件实例`、`IPluginConfigView`、`GetConfigView` / `Initialize` / `OnViewLoaded` 生命周期；
- `LinkableValueEditor`、`ImageReadOnly`、`HalconBase` 等共享控件。

## 2. 现状审查（改动理由）

| # | 现状 | 影响 |
| --- | --- | --- |
| 1 | 根布局 `Width=980 Height=620`、左栏固定 340px | 宿主内容区 920×520 时右侧预览被压缩；窄档 640×560 没有响应式布局 |
| 2 | 四张卡片（图像输入 / 模型 / 分类参数 / 试算）平铺，无信息层级 | 「模型摘要」与「试算结果」是排障主线索，却和普通字段混在一起，扫读成本高 |
| 3 | 模型路径 `TextBox Height=24` + 「浏览」按钮 `Width=56` | 控件高度与 36px 基线不一致，窄栏下路径可见长度不足 |
| 4 | 右侧只有裸 `ImageReadOnly`，无预览头 / 空态 / 错误态 | 无图时整块白板；模型加载失败时用户只能从左侧小字猜原因 |
| 5 | `ResultSummary` 是裸 `TextBlock`（Consolas 11px） | TopK 多行结果没有容器，长文本与帮助文字视觉同权 |
| 6 | 视图内 `#` 颜色为 0，但大量使用 `Plugin*` 旧令牌（24px 控件体系） | 与 V2 公共令牌（Dialog*/PluginPreview*）不一致，换肤时两套并存 |
| 7 | 试算 / 浏览只有 `Click`，没有可断言的命令入口 | 批量冒烟的 `Button.Command` 检查无法覆盖；功能回环只能靠肉眼 |

## 3. 设计方向

> 预览优先，一屏完成配置；模型摘要与试算结果升级为「排障卡」；只做纯 UI。

1. **两栏固定分工**：左 356px 配置轨（图像输入 → 模型 → 分类参数 → 试算），右侧预览工作区自适应。
2. **图像输入只包裹**：`LinkableValueEditor` 保留 `Port=Image` / `StepData`；`Label=""`，外置「输入图像」标签；🔗/✕ 继续用控件自带。
3. **模型信息卡**：`ModelSummary` 原绑定，等宽显示输入/输出形状、task、类别名；加载失败时整卡切红（由既有摘要文本触发，不新增属性）。
4. **参数表单统一**：`ResizeMode` 枚举下拉（显示中文，值仍是 `Letterbox` / `Stretch`）、`TopK` / `InputSize` 数值框；高级参数收进折叠区。
5. **试算结果独立成卡**：`Hint` 与 `ResultSummary` 原绑定；TopK 概率在等宽结果卡内展示。
6. **预览工作区**：预览头（图标 + 「图像预览」+「只读 · 整幅图」）+ 工具条（适应窗口 / 1:1，均接既有 `HalconBase` 方法）+ 深色画布 + 状态栏。
7. **空态/错误态**：无图时虚线空态卡；模型加载失败且无图时显示错误覆盖层；两者都是纯 WPF 覆盖层，不折叠已设图的 HALCON 控件。
8. **全令牌化**：颜色映射 `Colors.xaml` / `PluginConfigColors.xaml`；视图 XAML 零十六进制；图标按钮 30×30 + `Padding="0"`（R68）。

## 4. 页面结构与状态

### 4.1 默认态（模型已选、上游图已显示、试算完成）

- 左栏：模型信息卡显示 `ok-ng-classify.onnx` / `input: 1×3×224×224 output: 1×2` / `task=classify 2 个类别名（OK,NG）`；
- 试算结果卡：`预测：OK（置信度 0.982，耗时 14 ms）` + `OK 0.982` + `NG 0.018`；
- 右栏：深色画布显示零件图，左上角「只读预览 / 整幅图 / 适应窗口」；
- 状态栏：绿色点 + `已显示上游图像…` + 右侧 `整幅图 · 只读`。

### 4.2 空态（未选模型、无图）

- 模型信息卡：`尚未选择模型`；
- 试算结果卡：`尚未试算`；
- 预览：虚线空态卡「还没有图像」，文案指引「在图像输入里点 🔗 绑定上游端口或变量；也可以先点宿主底部执行」；
- 状态栏：橙色点 + 初始引导文案。

### 4.3 模型错误态（路径不存在 / 加载失败 / task 不是 classify）

- 模型信息卡整卡切红：`模型加载失败，详见提示`；
- 试算结果卡：`试算失败：模型文件不存在：…`；
- 预览（无图时）：红色错误覆盖层「模型加载失败」+ 检查路径提示；
- 状态栏：红色点 + 模型不可用提示。

> 错误态不新增属性：`ModelSummary` 在 `ModelPath` 非空且会话加载失败时固定返回「模型加载失败，详见提示」，
> 视图用该文本做 `DataTrigger`；`DisplayImage == null` 时显示错误覆盖层。业务侧没有第四档状态。

### 4.4 边界态

| 边界 | 设计处理 |
| --- | --- |
| 窗口 640×560 | 左栏 300px，内部滚动；右栏最小 240px；预览头隐藏元信息、状态栏隐藏右侧路径 |
| 模型摘要 3 行以上 | 卡片内 `TextWrapping=Wrap`，等宽 10.5px，长路径按字符换行 |
| TopK 结果 10 行以上 | 结果卡最大高度 132px，内部滚动（WPF 用 `MaxHeight` + `ScrollViewer`） |
| 模型路径为空 | 摘要「尚未选择模型」，不弹任何默认模型 |
| 输入尺寸 0 | 帮助文字明确「0 = 用模型自带的」，不显示 0 为非法 |
| 无图点「试算一下」 | 插件原文案「先点『载入』把示意图读进来再试算」（见 §9 已知偏差 6） |

## 5. 设计要点（完整清单）

1. 配置轨背景 `DialogRowAlternateBrush`，1px `DialogBorderLightBrush`，圆角 8；内部 14px 内边距。
2. 区块标题 12px SemiBold `DialogTextSecondaryBrush`；右侧 10.5px 提示 `DialogTextMutedBrush`。
3. 字段标签 12px `DialogTextSecondaryBrush`；控件高 36px；帮助 11.5px `DialogTextMutedBrush`，带 12px 信息图标。
4. 模型路径行：`TextBox` + 「浏览」按钮（图标 + 文字，高 36）；输入框 `UpdateSourceTrigger=PropertyChanged`。
5. 模型信息卡：`DialogRowAlternateBrush` 底 + `DialogBorderLightBrush` 边 + 8px 圆角；文件名 12px SemiBold；形状/元信息等宽 10.5px。
6. 缩放模式：`ComboBox` 高 36；显示「Letterbox · 等比补边（推荐）」/「Stretch · 拉伸填充」，值仍是枚举成员。
7. TopK：`NumericBox` 高 36，范围 1~100，整数；帮助说明会被夹到 `[1, 类别数]`。
8. 高级参数：`Expander`「高级参数（多数场景不用动）」，内含输入尺寸 0~4096。
9. 试算：主按钮「试算一下」全宽 36px；`Hint` 用 `DialogRowHoverBrush` 浅底提示条；结果卡带「试算结果 / 状态」头。
10. 预览头：26px 文件图标 + 「图像预览」+「只读 · 整幅图」；工具条 30×30 图标按钮 + 「1:1」文本按钮。
11. 画布：`PluginPreviewCanvasBrush` 深色底；左上角三枚半透明 chip（只读预览 / 整幅图 / 适应窗口）。
12. 状态栏：7px 圆点 + 11.5px `Hint` 文案 + 右侧等宽「整幅图 · 只读」；只有中性/提示色，不冒充三档状态。
13. 图标按钮 30×30、`Padding=0`、`FocusVisualStyle=DialogFocusVisual`；ToolTip 走 `DialogToolTipStyle`。
14. 数字（TopK / 输入尺寸 / 耗时）使用 tabular 数字；WPF 用 `Typography.NumeralAlignment="Tabular"` 或等宽字体。

## 6. 令牌映射

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--dc-surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--dc-surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--dc-surface-3` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--dc-border` | `#E2E8F0` | `DialogBorderBrush` |
| `--dc-border-soft` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--dc-border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` |
| `--dc-text` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--dc-text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--dc-text-3` | `#94A3B8` | `DialogTextMutedBrush` |
| `--dc-text-4` | `#64748B` | `ScadaSecondaryTextBrush`（近似）或 `DialogTextMutedBrush` |
| `--dc-primary` | `#0EA5E9` | `DialogAccentBrush` |
| `--dc-primary-strong` | `#0284C7` | `DialogAccentDarkBrush` |
| `--dc-primary-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--dc-primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--dc-primary-light` | `#E0F2FE` | `DialogAccentLightBrush` |
| `--dc-primary-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--dc-success` | `#22C55E` | `DialogSuccessBrush` |
| `--dc-success-text` | `#15803D` | `DialogSuccessTextBrush` |
| `--dc-success-soft` | `#F0FDF4` | `DialogSuccessSubtleBrush` |
| `--dc-success-border` | `#BBF7D0` | `DialogSuccessBorderBrush` |
| `--dc-danger` | `#EF4444` | `DialogDangerBrush` |
| `--dc-danger-text` | `#B91C1C` | `DialogDangerTextBrush` |
| `--dc-danger-soft` | `#FEF2F2` | `DialogDangerSubtleBrush` |
| `--dc-danger-border` | `#FECACA` | `DialogDangerBorderBrush` |
| `--dc-warning` | `#F59E0B` | `DialogWarningBrush` |
| `--dc-warning-text` | `#B45309` | `DialogWarningTextBrush` |
| `--dc-warning-soft` | `#FFF4E5` | `DialogWarningSubtleBrush` |
| `--dc-warning-border` | `#FDE68A` | `DialogWarningBorderBrush` |
| 预览画布 | `#0B1220` | `PluginPreviewCanvasBrush` |
| 预览表面 | `#1E293B` | `PluginPreviewSurfaceBrush` |
| 预览描边 | `#334155` | `PluginPreviewBorderBrush` |
| 预览文字 | `#E2E8F0` | `PluginPreviewTextBrush` |
| 预览弱文字 | `#94A3B8` | `PluginPreviewMutedTextBrush` |
| 图标字体 | — | `DialogIconFont`（Font Awesome 6 Pro Solid） |
| 圆角 6/8/10/12/16 | — | `PluginCardCornerRadius`（8）/ 局部 `CornerRadius` 字面量 |
| 间距 4/8/12/14/16 | — | `Margin` / `Padding` 字面量（仓内暂无间距令牌） |

**结论：本插件不需要新增公共令牌。** 设计用到的颜色全部落在既有 `Dialog*` / `PluginPreview*` 语义域内；
视图 XAML 的静态扫描 `hex=0` 可证明。

## 7. 契约保留（V1 一个都不能改）

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 宿主靠 `viewObj.DataContext as IPluginConfigView` 取 `OnConfirm`；独立 VM = 确认不保存 |
| C2 | `LinkableValueEditor`：`Port=Image` / `StepData` | 端口持久化、类型转换、变量链接、解除链接都在控件里 |
| C3 | `ImageReadOnly.HImage = DisplayImage`（`Mode=OneWay`） | 保留 HALCON 显示/缩放/右键菜单；`DisplayImage` setter 在 UI 线程释放旧 `HImage` |
| C4 | `ModelPath`（string）/ `ResizeMode`（枚举）/ `TopK`（int）/ `InputSize`（int） | 四个 `[StepConfig]` 属性；类型换了 = 持久化与编译端口不匹配 |
| C5 | 「浏览」→ `ModelPath` + `OnViewLoaded()`；「试算一下」→ `TryPreviewClassify()`；`Hint` / `ResultSummary` | 现有功能入口与状态文本；V1 不新增命令语义 |
| C6 | `Loaded` → `OnViewLoaded()` | 上游图显示 + 模型摘要刷新；删了就是「打开配置不显示试运行图」 |

## 8. 复刻实现映射（WPF）

| 设计项 | WPF 实现 |
| --- | --- |
| 两栏 356px + 自适应 | `Grid` 两列：`1*`（MinWidth 300 / MaxWidth 356）+ `1.6*`（MinWidth 240） |
| 左栏卡片 | `Border` 背景 `DialogRowAlternateBrush`、1px `DialogBorderLightBrush`、`PluginCardCornerRadius` |
| 图像输入 | `cv:LinkableValueEditor Label="" Port="{Binding Image}" StepData="{Binding StepData}"` |
| 模型路径 | `TextBox Style=DialogInput Height=36 Text="{Binding ModelPath, UpdateSourceTrigger=PropertyChanged}"` + 「浏览」按钮 |
| 模型信息卡 | `Border` 局部 Style；`DataTrigger` 匹配 `ModelSummary == 模型加载失败，详见提示` 时切红 |
| 缩放模式 | `ComboBox Style=DialogCombo Height=36 ItemsSource=ResizeModeOptions SelectedItem=ResizeMode` + `ResizeModeLabelConverter` |
| TopK / 输入尺寸 | `ui:NumericBox` 保持 `Value` 绑定与范围；输入尺寸在 `Expander` 内 |
| 试算 | 主按钮 `Command` 走视图级 `TryPreviewCommand`（委托 `TryPreviewClassify`）；`Hint` / `ResultSummary` 原绑定 |
| 预览宿主 | `ContentControl x:Name="PreviewHost" Content="{Binding DisplayImage}"` + `DataTemplate` 内 `h:ImageReadOnly HImage="{Binding Mode=OneWay}"` |
| 预览工具条 | `FitWindow_Click` → `FindVisualChild<ImageReadOnly>(PreviewHost)?.FitToWindow()`；`FitImage_Click` → `FitToImage()` |
| 空态 / 错误态 | 纯 WPF 覆盖层；`DisplayImage == null` 时空态可见；`ModelSummary` 错误文本 + 无图时错误态可见 |
| 状态栏 | 中性圆点 + `Hint` + 「整幅图 · 只读」；**不伪造 StatusLevel** |
| 颜色 | 全部 `StaticResource`；视图 XAML `hex=0` |

## 9. 复刻证据

| 项 | 结果 |
| --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.DlClassify\Plugin.DlClassify.csproj -v m`：**0 错误 / 0 警告**（本插件工程；`--no-incremental` 重编依赖链时 `Core.Interfaces` / `UI` 等有 409 个既有警告）；`Modules\Plugin.DlClassify.dll` 已刷新 |
| 插件视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.DlClassify`：**11/11 PASS** |
| 渲染 | `evidence/initial-render-920x520.png`（批量模式初始态；HALCON `HwndHost` 内容不进入 `RenderTargetBitmap`） |
| 设计稿截图 | `evidence/design-default-960.png`、`design-narrow-640.png`、`design-empty-960.png`、`design-error-960.png`（浏览器实测，状态切换可点击） |
| 常规 gate | `FlowCanvasChecks`：**2691 通过 / 0 失败**（一次复跑出现 1 条既有并行分支 `IsRunning` 时序抖动，重跑通过） |

## 10. 已知偏差（复刻时按 R130 记录）

1. **图标字体**：Web 用内联 Lucide 风格 SVG，WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`）；形状不同、语义一致。
2. **窗口阴影/动效**：Web 为多层 CSS 阴影；WPF 用宿主现有 `DropShadowEffect`；动效 150ms 量级。
3. **预览图**：Web 用内联 SVG 工业零件示意；WPF 用真实 `HImage`。
4. **渲染证据范围**：`ImageReadOnly` 是 HALCON `HwndHost`，`RenderTargetBitmap` 不捕获其内容；证据 PNG 只证明 WPF 布局，真实图像预览需真机窗口。
5. **模型摘要错误态的判据**：WPF 用 `ModelSummary` 的固定文案「模型加载失败，详见提示」触发红色卡与错误覆盖层；这是**视图侧文本判据**，不新增 VM 属性。若未来插件改文案，需同步本视图与冒烟断言。
6. **空图试算文案**：`TryPreviewClassify` 的既有提示仍是「先点『载入』把示意图读进来再试算」，而新界面没有「载入」按钮。本轮不改插件业务文案，按已知问题记录；建议后续把文案改为「先在『图像输入』绑定上游图像，或点宿主『执行』带图进来」。
7. **`ResizeMode` 中文显示**：视图用 `ResizeModeLabelConverter` 映射为「Letterbox · 等比补边（推荐）」/「Stretch · 拉伸填充」；持久化值仍是枚举成员名，未改 `ClassifyResizeMode` 定义、未加 `[Display]`。
8. **折叠区**：`Expander` 用 Fluent 默认头（外加卡片边框），未复刻 Web 自绘 summary。
9. **状态栏**：插件没有 `StatusMessage` / `StatusLevel` 属性；V1 用 `Hint` + 中性圆点，不新增第四档状态。
10. **宿主页脚**：设计稿底部的「执行 / 取消 / 确认」属于 `PluginConfigShellView`，插件视图不实现；实际窗口由宿主统一提供。
11. **滚动条**：WPF 左栏滚动条约占 17px，窄档可用宽度比 Web 少约 17px。
12. **模式/输出下拉**：本插件没有采集模式、相机、显示窗口；设计稿中不存在这些控件，WPF 也不补。
13. **错误态状态栏**：WPF 状态栏仍绑 `Hint`（插件既有文本）；模型失败的具体原因在「模型信息」红卡、错误覆盖层与 `ResultSummary` 结果卡里，设计稿状态栏的红色错误文案只是示意。
14. **左栏滚动**：960×680 下「试算」区仍在左栏可视区下方，需要滚动；与参考实现一样，主操作不在首屏，本轮不新增吸底操作栏。
15. **结果卡状态标签**：Web 稿右上角显示「完成 / 尚未试算 / 失败」；WPF 固定显示「TopK」，状态由正文表达（不新增 `ResultState` 属性/转换器）。
16. **`NumericBox` 模板颜色**：`ui:NumericBox` 的模板在共享主题 `NumericBox.xaml` 里自带一套颜色（未令牌化）；本轮按约束不改共享控件，视图只设 `Height=36`、范围与 `Value` 绑定。视觉上可能与 Web 稿的 36px 输入框有细微差异。
17. **`LinkableValueEditor` 内部样式**：链接/解除链接按钮与输入区的内部颜色/间距由共享控件决定，视图只包裹不改内部。

## 11. 未纳入本版（需新增功能/契约，延后）

| # | 项 | 需要什么 |
| --- | --- | --- |
| D1 | 加载骨架 / 推理进度 | VM 新增 `IsPreviewLoading` / `PreviewState`；当前只有 `Hint` / `ResultSummary` |
| D2 | 图像尺寸 / 文件大小 | VM 新增只读属性（`HImage.GetImageSize`） |
| D3 | 放大 / 缩小按钮 | `HalconBase` 只有 `FitToWindow()` / `FitToImage()`，没有公开 zoom 方法 |
| D4 | 打开所在文件夹 | 新增 `Process.Start("explorer.exe", ...)` 行为 |
| D5 | 模型库 / 系统导航 / ROI 画框 | 模型仍是外部文件路径；相机/系统设置不进入本插件；分类整幅图，不吃 ROI |
| D6 | 拖拽入图 | 当前 `Image` 端口没有拖拽契约 |
| D7 | 分类结果叠图 | 需要在 HALCON 画布上绘制文字/标签，属于新增显示能力 |

## 12. 变更规则

设计基线确认后只读。需要调整设计时升版本（v1 → v2），同步更新：

- 本 README 的版本、映射表、已知偏差；
- `ClassifyView.xaml` / `ClassifyView.xaml.cs`；
- `docs/code-changes/2026-10-20-DlClassify-Web化.md` 的验证记录。
