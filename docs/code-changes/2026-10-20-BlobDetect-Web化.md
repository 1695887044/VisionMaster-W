# 2026-10-20 Plugin.BlobDetect 自定义视图 Web 化

## 1. 范围

| 项 | 内容 |
| --- | --- |
| 设计基线 | `docs/UI设计基线/blob-detect/README.md` v1 + `design.html` |
| 覆盖 | `Plugins\Plugin.BlobDetect\BlobDetectView.xaml`、`BlobDetectView.xaml.cs` |
| 附带 | `BlobDetectPlugin.cs` 的 `Dispose()` 补一行 `_disposed = true;`（消除 CS0649，并让既有"释放后拒绝回填"守卫真正生效） |
| 不在范围 | 业务算法、端口名/类型、命令名、持久化键、`OnConfirm`/`Initialize` 语义、宿主外壳、共享样式/共享控件、其他插件 |
| 批次 | 批次 3（大型多面板）第 1 个插件 |

> 用户口径中的「2 画布 + 98 Numeric + 12 DataGrid」按源码标记计数解释：
> - 2 块画布 = `h:ImageReadOnly` + `h:HistogramPlot`；
> - 98 处 `NumericBox` 文本引用 = **19 个 `ui:NumericBox` 控件**（每个控件含 `<ui:NumericBox>` 与 `<ui:NumericBox.Value>` 两个标签，共 38 个标签）；
> - 12 个 `<DataGrid*>` 标签 = **1 个 `DataGrid` + 8 个 `DataGridTextColumn` + `DataGrid.Columns` 等**；
> - 另有 3 个 `LinkableValueEditor`、4 个 `ComboBox`、2 个 `CheckBox`、1 个命令、2 个预览页签。

## 2. 耦合矩阵（改前盘点）

| 区域/元素 | 当前绑定/契约 | 功能依赖 | 可重构级别 |
| --- | --- | --- | --- |
| 根布局 | `Grid` 两列：参数 330px + 预览 `*` | 纯布局 | 自由 |
| 左栏滚动 | `ScrollViewer` 纵滚 | 纯布局 | 自由 |
| 图像输入（3 行） | `cv:LinkableValueEditor`：`Port=SrcImage/MaskRegion/ExcludeRegion`、`StepData` | 端口持久化、类型转换、变量链接/解除、`ValueChanged` 订阅 | **只能包裹/改外观** |
| 按当前图像适配 | `Button.Command=AdaptToImageCommand` | 现有唯一命令；按当前图重算阈值/面积 | 保留命令绑定 |
| 二值化方式 | `ComboBox ItemsSource=ThresholdModes`、`SelectedItem=ThresholdMode`（枚举） | 切换分区显隐 + `SchedulePreview` | 保留类型与绑定 |
| 固定阈值参数 | `MinGray` / `MaxGray`：`NumericBox.Value` + `NumberValidationRule` | 算法输入；越界/非法输入回退 | 保留控件与规则 |
| 自动阈值说明 | `IsAutoThreshold` → `BoolToVis` | 纯显隐 | 自由 |
| 动态阈值参数 | `VarMaskWidth` / `VarMaskHeight` / `VarStdDevScale` / `VarAbsThreshold` | 算法输入 | 保留控件与规则 |
| 检测目标 | `ComboBox ItemsSource=DetectTargets`、`SelectedItem=DetectTarget`；`Visibility=ShowDetectTarget` | 明暗极性；固定阈值/亮暗同检时隐藏 | 保留类型与显隐 |
| 亮暗同检 | `CheckBox IsChecked=DetectBrightAndDark`；`Visibility=ShowDualPolarity` | 两种极性各做一次分割再取并集 | 保留 bool |
| 特征筛选 | `MinArea` / `MaxBlobArea` / `MinCircularity` / `MaxAspectRatio` / `ExcludeBorderPx` | 面积/形状/触边筛选 | 保留控件与规则 |
| 形态学 | `OpenRadius` / `CloseRadius` / `MergeRadius` + `FillHoles` | 开/闭/合并/填孔 | 保留控件与规则 |
| 判定规格 | `MaxDefectCount` / `MinDefectCount` / `MaxSingleArea` / `MaxTotalArea` | OK/NG 判定 | 保留控件与规则 |
| 输出 | `DisplayViewIndex`（0~9 int）/ `PixelSizeMm` / `SortMode`（枚举） | 运行发布窗口、mm² 换算、输出排序 | 保留类型与绑定 |
| 预览头 | 无（原视图只有标题） | 纯展示 | 自由 |
| 预览页签 | `ListBox ItemsSource=PreviewTabLabels`、`SelectedIndex=SelectedPreviewTab`（int） | 图像/数据两页签 | 保留索引语义 |
| 图像画布 | `h:ImageReadOnly HImage=PreviewImage` | HALCON 显示/缩放/右键菜单；`PreviewImage` setter 释放旧图 | **保留控件与绑定；常驻不折叠** |
| 空态/错误态 | `PreviewImage == null`、`StatusLevel == Error` | 纯覆盖层 | 自由 |
| 缺陷清单 | `DataGrid ItemsSource=DefectRows`、8 列绑定、`SelectionChanged=DefectList_SelectionChanged`、单选、只读 | 行选中 → `HighlightDefect(index)` → 预览套黄框并切回图像页 | **保留 ItemsSource/列/事件** |
| 灰度直方图 | `h:HistogramPlot Data/ShowThresholdMarkers/ThresholdLow/ThresholdHigh/Visibility` | 调阈值依据；取不到时降级提示 | **保留控件与 4 个绑定** |
| 状态栏 | `StatusMessage` + `StatusLevel` 三档 | Info/Warning/Error 颜色；NG 用 Error | 保留三档 |
| 直方图辅助文字 | `HistogramRangeText` / `HistogramTypeText` / `HistogramError` / `HistogramVisible` / `HistogramErrorVisible` | 纯展示 | 保留绑定 |
| 生命周期 | `GetConfigView` → `new BlobDetectView { DataContext = this }`；`Loaded` → `OnViewLoaded()`；`Dispose()` | 宿主取 `IPluginConfigView`、预览、释放 | **不能换 DataContext/独立 VM** |

**判定原则**：凡是某个端口/命令/配置属性的唯一入口（3 个 Linkable、19 个 NumericBox、4 个下拉、2 个勾选、DataGrid、2 块画布、1 个命令）一律只包裹/改外观，不重写内部语义。

## 3. 改动点

1. **整页重写为 V2 两栏工作区**：
   - 左栏 300~356px 配置轨（`DialogRowAlternateBrush` 底 + `PluginCard` 卡片）：图像输入 + ① 二值化 + ② 特征筛选 + ③ 形态学 + ④ 判定规格 + ⑤ 输出；
   - 右侧预览工作区：44px 预览头（标题 + `DefectCountText` 胶囊 + 适应窗口/1:1）+ 38px 页签条（`PreviewTabLabels`）+ 深色画布 + 40px 状态栏。
2. **参数节奏**：字段标签 11.5px、控件 32px、帮助 11px；19 个数值字段按两列紧凑排布，每个字段保留原 `Minimum`/`Maximum`/`DecimalPlaces`/`NumberValidationRule`。
3. **图像画布常驻**：`h:ImageReadOnly x:Name="PreviewCanvas" HImage="{Binding PreviewImage}"` 始终在可视树里；「数据输出」是覆盖层（`SelectedPreviewTab == 1`），不再像旧视图那样用 `Visibility=Collapsed` 折叠图像页签；空态/错误态是纯 WPF 覆盖层。
4. **预览工具条**：`FitWindow_Click` → `PreviewCanvas.FitToWindow()`；`FitImage_Click` → `PreviewCanvas.FitToImage()`；都是 `HalconBase` 既有公开方法，不新增缩放逻辑。
5. **缺陷清单重排**：DataGrid 8 列保留（`Index/Area/Circularity/AspectRatio/Width/Height/Row/Col`），字号 11、行高 30、交替行底色、表头 30px；`SelectionChanged` 事件与 `DefectRows` 绑定原样。
6. **直方图重排**：`HistogramPlot` 保留 `Data` / `ShowThresholdMarkers` / `ThresholdLow` / `ThresholdHigh` / `Visibility`；固定阈值标记提示与范围/位深文字保留。
7. **状态栏**：`StatusMessage` 用只读 `TextBox` 承载（可选中复制错误信息，且满足批量冒烟的 `TextBox.Text` 绑定检查），`StatusLevel` 三档颜色触发器不变；右侧显示 `HistogramTypeText`。
8. **code-behind**：保留构造、`Loaded → OnViewLoaded()`、`DefectList_SelectionChanged`；新增 `FitWindow_Click` / `FitImage_Click` 与 `StatusMessageConverter`（空串→「就绪」）。
9. **插件类一行修正**：`Dispose()` 开头补 `_disposed = true;`。字段注释写明它是"关闭对话框后拒绝在途预览回填"的守卫，但原实现从未赋值，导致守卫恒为 false；补上后既消除 CS0649 警告（构建 0 警告），也让既有预览后台链的释放语义真正成立。**不改任何算法/端口/命令/持久化**。
10. **零共享文件改动**：`Colors.xaml`、`PluginConfigColors.xaml`、`PluginConfigStyles.xaml`、`LinkableValueEditor`、`ImageReadOnly`、`HalconBase`、`PluginConfigShellView.xaml`、`UIThemeSmokeTest\Program.cs`/`.csproj`/`PluginViewSmoke.cs`、其他插件均未修改。

## 4. 与 ImageAcquisition V2 的差异

| 维度 | ImageAcquisition V2 | BlobDetect v1 |
| --- | --- | --- |
| 主结构 | 2×2 模式卡片 + 模式专属参数 + 预览 | 固定 6 张卡片（图像输入 + ①~⑤），无模式卡片 |
| 预览画布创建 | `ContentControl` + `DataTemplate`，**有图才创建** `ImageReadOnly` | `ImageReadOnly` **常驻**；数据页签是覆盖层，空/错误态是覆盖层（本插件有 2 块画布，且用户硬约束"常驻"） |
| 第二块画布 | 无 | `HistogramPlot`（数据页签内） |
| 数据表格 | 无 | 1 个 DataGrid（8 列）+ 行选中高亮联动 |
| 参数规模 | 4 个 Linkable + 少量字段 | 3 个 Linkable + 19 个 NumericBox + 4 个下拉 + 2 个勾选 |
| 命令 | Browse/Refresh/Prev/Next/PushLocalImage | 仅 `AdaptToImageCommand` |
| 预览头 | 文件名 + 文件/目录/网络/相机标签 | 固定「标注图预览」+ `DefectCountText` 胶囊（无文件路径概念） |
| 状态栏 | `StatusMessage` TextBlock + 路径 | `StatusMessage` 只读 TextBox（可复制）+ `HistogramTypeText` |
| 工具条 | 适应窗口 / 1:1 / 目录刷新 | 适应窗口 / 1:1（无目录刷新） |
| 控件高度 | 36px | 32px（640×560 最小档余量） |

## 5. 功能契约保留（逐项）

| # | 契约 | 状态 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 保留；`GetConfigView` 未改 |
| C2 | 3 个 `LinkableValueEditor`：`SrcImage` / `MaskRegion` / `ExcludeRegion` | 保留；`Port`/`StepData` 原绑定 |
| C3 | `ImageReadOnly.HImage = PreviewImage` | 保留；控件常驻，不折叠 |
| C4 | `HistogramPlot` 的 `Data` / `ShowThresholdMarkers` / `ThresholdLow` / `ThresholdHigh` / `Visibility` | 保留 |
| C5 | `DataGrid` 的 `ItemsSource=DefectRows`、8 列、单选、只读、`SelectionChanged` | 保留 |
| C6 | 19 个 `NumericBox` 的 `Value` 绑定、范围、`DecimalPlaces`、`NumberValidationRule` | 逐项保留 |
| C7 | `ThresholdMode` / `DetectTarget` / `SortMode` 枚举；`DisplayViewIndex` int；`DetectBrightAndDark` / `FillHoles` bool | 保留 |
| C8 | `AdaptToImageCommand`；`StatusMessage` / `StatusLevel` 三档 | 保留 |
| C9 | `SelectedPreviewTab` / `PreviewTabLabels` | 保留（0=图像，1=数据） |
| C10 | `DefectCountText` / `HistogramRangeText` / `HistogramTypeText` / `HistogramError` / `HistogramVisible` / `HistogramErrorVisible` | 保留 |

## 6. 验证

| 项 | 命令 | 结果 |
| --- | --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.BlobDetect\Plugin.BlobDetect.csproj -v m` | **0 错误 / 0 警告** |
| Modules 产物 | `dir Modules\Plugin.BlobDetect.dll` | 时间戳 `2026-10-20 03:31:56`，大小 `190,464 bytes`（核查时实际值；本次遗留修复重建后为 `07:44:46 / 189,440 bytes`） |
| UI 冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.BlobDetect` | **10/10 通过** |
| 渲染 | 同上 | `docs/UI设计基线/_batch-evidence/Plugin.BlobDetect-BlobDetectPlugin.png` → `docs/UI设计基线/blob-detect/evidence/after-render-v1-initial.png` |
| 常规 gate（首次） | `dotnet run --project FlowCanvasChecks --no-build` | **2690 通过 / 1 失败**：`[P] ★运行中：两条分支胶囊 IsRunning=true` 时序红项（`分支1=False 分支2=True`），与 BlobDetect UI 无关 |
| 常规 gate（重跑） | 同上 | **2691 通过 / 0 失败**；`[Blob]` 算法/像素段与「视图样式收编契约」全部通过 |
| 设计页 | 浏览器打开 `design.html` | 默认态、640 窄档、数据输出页签均可切换；自包含、无外部依赖 |

UI 冒烟的 10 条覆盖：

- `GetConfigView` 返回 `BlobDetectView`；视图实例化 **574 个元素**；
- `DataContext` 是 `BlobDetectPlugin`（`IPluginConfigView`）；
- 3 个 `LinkableValueEditor.Port` = `SrcImage,MaskRegion,ExcludeRegion`；
- 视图自有按钮 `buttons=3`，命令 `AdaptToImageCommand`；
- `ComboBox` 绑定：`ThresholdModes/ThresholdMode`、`DetectTargets/DetectTarget`、`ViewIndexes/DisplayViewIndex`、`SortModes/SortMode`；
- `TextBox.Text` 绑定：`PortValue` / `LinkAddress` / `StatusMessage`；
- `OnConfirm` 可执行，`InputValues=29`；
- 项目 XAML **hex=0**（1 个 XAML）；
- 渲染 PNG 已生成。

`[Blob]` 段重点（全部通过）：
- 检出 5 个缺陷、五个数组端口逐项对齐、`MaxArea`/`TotalArea` 与逐项值一致；
- 默认零容忍 NG 但 `Success=true`（NG 是正常结果）；
- 排序、触边排除、长宽比、圆度、合并、填孔、存在性下限、总面积、像素当量/直连；
- 开轮重置、空图失败、底图保真（左半 100 / 右半 200）、byte/uint2 位深、多通道、1 基编号锚点；
- 视图样式收编：无硬编码颜色、无本地 `CardBorder/CardTitle/HintText/FieldLabel/NumericBox`、使用 `PluginCard`/`PluginNumericBox`、`h:` 控件无内联 Style、引用的 10 个 `Plugin*` 键全部有定义。

## 7. 已知偏差

1. **控件高度 32px**：Web 36px；为 640×560 最小档压缩 4px，宽度与两列排布不变。
2. **判定 chip**：Web 稿右下角有「NG · 3 个缺陷」chip；WPF 不额外造判定文本，判定由预览头计数胶囊 + 状态栏 `StatusMessage` 表达，避免两份真相。
3. **状态消息控件**：WPF 用只读 `TextBox`（可选中复制），外观按 `TextBlock` 处理；不改变 `StatusLevel` 三档。
4. **DataGrid / 直方图**：Web 用 HTML table + SVG 示意；WPF 用只读 `DataGrid` + `HistogramPlot` 矢量绘制。
5. **HALCON 渲染证据边界**：`ImageReadOnly` 是 `HwndHost`，`RenderTargetBitmap` 不捕获其内容；证据 PNG 展示空态布局，真实标注图预览需真机窗口。
6. **640 WPF 窄档**：设计稿已覆盖 640×680；WPF 批量冒烟固定 920×520，640×560 需真机复核左栏滚动与预览高度。
7. **图标**：Web 内联 SVG（Lucide 风格），WPF `DialogIconFont`（Font Awesome 6 Pro Solid）；语义一致、形状不同。
8. **滚动条**：WPF 左栏滚动条约 17px，窄档可用宽度比 Web 少约 17px。

## 8. 遗留问题

1. **真机回环**：打开配置 → 接图 → 调 19 个参数 → 切数据页签 → 点缺陷行 → 确认 → 重开，需在 VisionMaster 真机验收；`ImageReadOnly` 的实际标注图预览无法由离屏证据替代。
2. **640×560 最小档**：需真机确认左栏滚动、预览高度、DataGrid 横向滚动是否可接受。
3. **判定 chip**：如果用户坚持要 Web 稿右下角 chip，需要新增一个只读判定属性（或 `MultiBinding` 组合 `StatusLevel` + `DefectCountText`），属于 UI 增强，未纳入本版。
4. **加载骨架 / 图像尺寸 / 放大缩小 / 打开文件夹 / 系统导航**：均需新增功能或契约，按 SOP 延后。
5. **`_disposed` 一行修正**：属生命周期正确性修复，reviewer 需确认没有其他路径依赖"Dispose 后仍可回填预览"的旧行为（按字段注释，这种依赖不应存在）。

## 9. 回滚

| 场景 | 动作 |
| --- | --- |
| 视图回滚 | `git checkout -- Plugins\Plugin.BlobDetect\BlobDetectView.xaml Plugins\Plugin.BlobDetect\BlobDetectView.xaml.cs`（若用版本库） |
| 插件类回滚 | 删除 `Dispose()` 里的 `_disposed = true;` 一行；重新构建 |
| 设计基线 | 保留 `docs/UI设计基线/blob-detect/` 作为 v1 参考；不删除证据 |
| 产物 | 重新构建插件工程，确认 `Modules\Plugin.BlobDetect.dll` 时间戳刷新 |

## 10. 复跑命令

```text
dotnet build Plugins\Plugin.BlobDetect\Plugin.BlobDetect.csproj -v m
dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.BlobDetect
dotnet run --project FlowCanvasChecks --no-build
```

> `FlowCanvasChecks` 首次 2690/1 是并行分支胶囊 `IsRunning` 的时序红项（与本次 UI 改动无关）；重跑 2691/0。若再次出现，先看该行是否仍为 `分支1=False 分支2=True`，并按既有并发红项处理，不要把它归因到 BlobDetect 视图。

## 11. 遗留问题核销（2026-10-20，BD2/BD3/BD4/BD5/BD7）

| 项 | 改动 |
| --- | --- |
| BD2 文档时间戳 | 本文件 §6 的 Modules 产物行改为核查时实际值 `2026-10-20 03:31:56 / 190,464 bytes`；本次修复重建后的实际值 `07:44:46 / 189,440 bytes` 同格注明 |
| BD3 状态栏裁切 | `StatusMessageBoxStyle.TextWrapping` 由 `NoWrap` 改为 `Wrap`；预览工作区第 4 行由固定 `Height="40"` 改为 `Height="Auto" MinHeight="40"`。只改显示布局，`StatusMessage` 来源、`StatusLevel` 三档、`StatusMessageConverter`（空串→「就绪」）均未动 |
| BD4 空态计数胶囊 | 预览头计数胶囊的样式增加 `DataTrigger Binding="{Binding PreviewImage}" Value="{x:Null}"` → `Visibility=Collapsed`；空态/错误态不再显示「共 0 个」。数据页签里的 `DefectCountText` 文本不受影响（它是清单表头说明，不是胶囊） |
| BD5 disabled 演示 | `design.html` 增加 `:disabled` / `[disabled]` 样式（`opacity:.45; cursor:not-allowed`），覆盖 button/input/select/textarea，与 WPF `IsEnabled=False` 的 0.45 透明度对齐 |
| BD7 自动化语义 | 3 个 `LinkableValueEditor` 补 `AutomationProperties.Name`：`输入图像` / `检测区域` / `排除区域`；外置 `TextBlock` 标签保留，屏幕阅读器不再只拿到空 `Label` |

验证（本次修复批）：

| 项 | 结果 |
| --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.BlobDetect\Plugin.BlobDetect.csproj -v q`：**0 错误 / 0 警告**（仅依赖工程重建时会输出既有 nullable 警告，非本插件新增） |
| 插件视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.BlobDetect`：**11/11 PASS**（elements=574；3 个 Port；`InputValues=29`；hex=0） |
| Modules 产物 | `Modules\Plugin.BlobDetect.dll`：2026-10-20 **07:44:46 / 189,440 bytes** |
