# 2026-10-20 ColorCheck 自定义视图 Web 化

## 1. 范围

- 设计基线：`docs/UI设计基线/color-check/README.md` v1 + `design.html`（自包含、可点击；默认 / 未框选 / 空态 / 试算失败 / 960·640）。
- 覆盖：`Plugins\Plugin.ColorCheck\ColorCheckView.xaml`、`ColorCheckView.xaml.cs`。
- 另改一处插件文案：`ColorCheckPlugin.cs` 的 `TryPreviewAnalyze()` 空图提示（仅字符串，见 §5）。
- 不在范围：插件业务逻辑、端口名、命令名、持久化、共享控件/共享字典、宿主 `PluginConfigShellView`、`UIThemeSmokeTest` 源码。
- 未修改：`Colors.xaml`、`PluginConfigColors.xaml`、`UIThemeSmokeTest\Program.cs` / `.csproj` / `PluginViewSmoke.cs`、`PluginConfigShellView.xaml`、`docs/UI设计基线/README.md`、其他插件。

## 2. 耦合矩阵（改动前）

| 区域/元素 | 当前绑定/契约 | 功能依赖 | 可重构级别 |
| --- | --- | --- | --- |
| 根布局 | `UserControl MinWidth=700 MinHeight=360`；`Grid` 左 330 / 右 `*` | 纯布局 | 自由（保留最小尺寸） |
| 图像输入 | `cc:LinkableValueEditor Port="{Binding Image}" StepData="{Binding StepData}"` | 端口持久化、类型转换、变量链接 / 解除链接；`Image.PropertyChanged` 驱动 `ShowUpstreamImage()` | 只能包裹，不能替换 |
| 画布 | `h:ImageEdit ActiveRoi="{Binding CanvasActiveRoi, Mode=TwoWay}" DrawObjectList="{Binding CanvasRois}" HImage="{Binding DisplayImage, Mode=OneWay}"` | ROI 新建 / 删除 / 拖拽、`CollectionChanged` 回写 `RoiParams`、`HTuples` 变更回写、`DisplayImage` 释放语义 | 保留控件与三条绑定 |
| 采样区摘要 | `RoiSummary`（只读，插件 `OnPropertyChanged`） | 显示中心/角度/半长/半宽；长度不为 5 时显示「尚未框选采样区」 | 只改外观 |
| 清空采样区 | `Click → Plugin.ClearRoi()` | 清空 `CanvasRois`（播种位保护在插件里） | 只能转发 |
| 序列反向 | `IsChecked="{Binding ReverseSequence}"`（bool，`[StepConfig]`） | `OnConfirm` 按属性名写回 | 只改外观 |
| 线数 | `Text="{Binding WireCount}"`（int，`[StepConfig]`） | 0 = 自动反推；正数 = 强制根数 | 只改外观 |
| 等间距规整化 | `IsChecked="{Binding Regularize}"`（bool，`[StepConfig]`） | 规整补色 | 只改外观 |
| 试算 | `Click → Plugin.TryPreviewAnalyze()` | 用 `DisplayImage` 跑算法，写 `ResultSummary` / `_lastMeasured` | 只能转发 |
| 试算结果 | `Text="{Binding ResultSummary}"`（只读，INPC） | 根数 / 序列 / 线距 / 来源 / 期望 / 判定 | 只改外观（可加展示层着色） |
| 高级参数 | `DarkSpanRatio` / `BrightPercentile` / `MinRun` / `MaxRunRatio` / `RuleWindowRatio`（double/int，`[StepConfig]`） | `SequenceSettings` 入参 | 只改外观 |
| 端点外推 | `IsChecked="{Binding ExtendEnds}"`（bool，`[StepConfig]`） | 两端多探一根 | 只改外观 |
| 颜色阈值 | `cc:ColorThresholdEditor`（DataContext 继承插件；13 个同名属性） | 颜色分类判据；随方案落盘 | 共享控件，原样嵌入 |
| 投射视图窗口 | `Text="{Binding DisplayViewIndex}"`（int，`[StepConfig]`，0~9） | `ProjectResult()` 投射目标 | 只改控件（TextBox → ComboBox），类型不变 |
| 配方 | `Text="{Binding RecipeText, UpdateSourceTrigger=PropertyChanged}"`（string，`[StepConfig]`） | `RecipeTable.Parse` 运行期匹配 | 只改外观 |
| 存成配方 | `Click → Plugin.SavePreviewAsRecipe()` | 把 `_lastMeasured` 追加成一行配方 | 只能转发 |
| 提示 | `Text="{Binding Hint}"`（只读，INPC） | 载入结果 / 错误原因 | 只改外观 |
| 视图就绪 | `Loaded → Plugin.OnViewLoaded()` | `ShowUpstreamImage()` | 保留时序 |
| 状态 | 本插件**没有** `StatusMessage` / `StatusLevel` / `Mode` / `SelectedCameraOption`；状态通道是 `Hint` + `ResultSummary` | 不新增第四档、不伪造三档 | 只做展示层推断 |

## 3. 功能契约保留（逐项）

| # | 契约 | 状态 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 保留；`GetConfigView` 仍 `new ColorCheckView { DataContext = this }` |
| C2 | `LinkableValueEditor` 的 `Image` 端口 | 保留：`Port="{Binding Image}"`、`StepData="{Binding StepData}"`；1 个编辑器 |
| C3 | `ImageEdit` 三条绑定 | 逐字保留：`HImage="{Binding DisplayImage, Mode=OneWay}"`、`DrawObjectList="{Binding CanvasRois}"`、`ActiveRoi="{Binding CanvasActiveRoi, Mode=TwoWay}"`；控件常驻，不折叠 |
| C4 | 配置属性与类型 | 全部原属性名/类型：`RoiParams`(double[5])、`ReverseSequence`(bool)、`WireCount`(int)、`Regularize`(bool)、`DarkSpanRatio`/`BrightPercentile`/`MaxRunRatio`/`RuleWindowRatio`(double)、`MinRun`(int)、`ExtendEnds`(bool)、`DisplayViewIndex`(int)、13 项阈值、`RecipeText`(string) |
| C5 | 三个既有公开方法 | `ClearRoi()` / `TryPreviewAnalyze()` / `SavePreviewAsRecipe()`；视图只做 `RelayCommand` 转发 |
| C6 | 三处只读展示 | `Hint` / `ResultSummary` / `RoiSummary` 全部保留；`ResultToneConverter` 只按文案着色，不改变判定 |
| C7 | `Loaded → OnViewLoaded()` | 原样保留 |

## 4. 改动点

1. **整页重写 `ColorCheckView.xaml`**：两栏（左 300~356 配置轨 + 右自适应采样画布）；左栏改为单 `ScrollViewer`，输出区在末尾；去掉原 5 张竖排 `PluginCard` 的平铺结构，改为分区 + 细分隔线。
2. **图像输入**：`LinkableValueEditor` 设 `Label=""`，外置「上游图像」标签；不新增「载入本地图」。
3. **采样区**：新增「已框选 / 未框选」chip（`RoiStateConverter` 只按 `RoiSummary` 文案判断）；`RoiSummary` 等宽显示；「清空采样区」整行次按钮。
4. **检查参数**：`ReverseSequence` / `Regularize` 改为整行开关卡（`CheckCardStyle`）；`WireCount` 用 92px 数字框；「试算一下」主按钮整行；结果块独立，`ResultToneConverter` 按文案分 Info / Success / Warning / Error 四色。
5. **高级参数**：`Expander` 收纳 5 项序列分析参数、`ExtendEnds`、`ColorThresholdEditor`（13 项阈值原样）。
6. **配方**：多行等宽 `TextBox x:Name="RecipeTextBox"` 绑 `RecipeText`；「把试算结果存成配方」按钮；命令后 `UpdateTarget()` 刷新显示（`RecipeText` 是无 INPC 的自动属性）。
7. **输出**：`DisplayViewIndex` 从 TextBox 改为 0~9 `ComboBox`（`SelectedItem` 仍绑 int）；`ViewIndexLabelConverter` 只改显示名。
8. **画布**：`ImageEdit` 常驻；左上「ROI 编辑 · 右键新建矩形」角标；空态为纯 WPF 覆盖层（`DisplayImage == null` 时显示），**不折叠 ImageEdit**；工具条「适应窗口」`FitToWindow()`、「1:1」`FitToImage()` 走 `HalconBase` 既有方法。
9. **状态栏**：主色信息点 + `Hint`（超长省略 + ToolTip 全文）+ `RoiSummary`；本插件没有 `StatusLevel`，不伪造三档。
10. **全令牌化**：视图 XAML `hex=0`；所有颜色走 `Dialog*` / `PluginPreview*` / `ScadaSecondaryTextBrush`；图标按钮 30×30 + `Padding="0"`（R68）；焦点 `DialogFocusVisual`；ToolTip `DialogToolTipStyle`。
11. **code-behind**：保留构造/`Loaded`；新增三个 `Core.Commands.RelayCommand`（`ClearRoiCommand` / `TryAnalyzeCommand` / `SaveRecipeCommand`）与两个工具条 Click 转发；新增三个纯展示转换器（`RoiStateConverter` / `ResultToneConverter` / `ViewIndexLabelConverter`）；不引入独立 VM。
12. **文案修正（非业务逻辑）**：`TryPreviewAnalyze()` 的空图提示由「先点『载入』把示意图读进来再试算」改为「还没有图像：请先在『图像输入』里绑定上游端口或变量，或点『执行』把上游图载入画布，再试算」。原因：当前视图没有「载入」入口，旧文案指向不存在的按钮；新文案保留「载入」二字，`FlowCanvasChecks\ColorCheckSequenceCheck.cs:248` 的 `summary.Contains("载入")` 断言仍通过。

## 5. 与 ImageAcquisition V2 的差异

| 维度 | ImageAcquisition V2 | ColorCheck |
| --- | --- | --- |
| 主操作位置 | 宿主底栏「执行」 | 插件内「试算一下」+ 宿主「执行」；主按钮在检查参数区 |
| 模式选择 | 2×2 模式卡片（4 个 `RadioButton`） | 无模式；本插件只有一条处理路径 |
| 预览控件 | `ContentControl PreviewHost` 按需创建 `ImageReadOnly` | `ImageEdit` **常驻**（ROI 编辑必须始终在可视树），空态用覆盖层 |
| 结果区 | 预览状态栏 + 错误态 | `ResultSummary` 独立结果块（等宽多行）+ 四色展示层着色 |
| 输出位置 | 左栏底部固定「输出」 | 左栏末尾「输出」，随配置轨滚动（本插件表单更长，优先让主按钮在最小高度可见） |
| 配方 | 无 | 多行配方文本 + 「存成配方」 |
| 状态 | `StatusMessage` / `StatusLevel` 三档 | 本插件无 `StatusLevel`；`Hint` + `ResultSummary`，状态点固定主色 |
| 画布工具条 | 适应窗口 / 1:1 / 刷新 | 适应窗口 / 1:1（无刷新入口） |

## 6. 验证

| 项 | 命令 | 结果 |
| --- | --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.ColorCheck\Plugin.ColorCheck.csproj -v m` | **0 错误 / 0 警告**；`Modules\Plugin.ColorCheck.dll` 2026-10-20 03:39，167,424 字节 |
| 批量视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.ColorCheck` | **11/11 PASS**（见下） |
| 常规 gate | `dotnet run --project FlowCanvasChecks --no-build` | **2691 通过 / 0 失败**（首次运行有 1 条并行分支 `IsRunning` 偶发失败，与本插件无关；重跑 2691/0） |
| 渲染证据 | `docs/UI设计基线/color-check/evidence/` | 4 张设计稿 + 1 张 WPF 初始态（`initial-render-920x520.png`） |

冒烟明细（`Plugin.ColorCheck.ColorCheckPlugin`）：

- `GetConfigView` 返回 `ColorCheckView`；`elements=227`；`DataContext` 是 `ColorCheckPlugin`。
- `LinkableValueEditor.Port`：`count=1 ports=Image`。
- `Button.Command`：5 个按钮，3 个命令 `ClearRoiCommand` / `TryAnalyzeCommand` / `SaveRecipeCommand`。
- `ComboBox`：`DisplayViewIndex` 绑定；`TextBox.Text`：`WireCount` / `ResultSummary` / `RecipeText` 等。
- `OnConfirm` 可执行：`InputValues=27`。
- `XAML hex=0`：`xaml=1 hex=0`。
- 渲染 PNG：`docs/UI设计基线/_batch-evidence/Plugin.ColorCheck-ColorCheckPlugin.png`。

## 7. 遗留问题

1. **真机未验收**：需要打开配置 → 链接上游 → 执行 → 右键框 ROI → 试算 → 存配方 → 确认 → 重开，确认 `HImage` 刷新、ROI 拖拽、`OnConfirm` 写回与 `DisplayViewIndex` 投射。
2. **渲染证据范围**：`ImageEdit` 是 HALCON `HwndHost`，`RenderTargetBitmap` 不捕获其内容；`initial-render-920x520.png` 只证明 WPF 布局，真实图像预览需真机窗口。
3. **`ColorThresholdEditor` 视觉节奏**：共享控件仍是 22px 输入框与自身灰阶，未改共享件；视觉上与 36px 表单不一致（设计基线已记录）。
4. **窄档主按钮**：640×680 下左栏 300px，帮助文字换行后「试算一下」可能只露出一部分，需要向下滚动一点；这是“配置轨可滚动”的预期行为，不是死按钮。若用户要求 640 宽下主按钮完全常驻，可后续把主按钮移到左栏底部固定条（需要设计基线升 v2）。
5. **`ResultToneConverter` 是展示层文本推断**：按 `ResultSummary` 的「判定：合格 / 判定：不合格 / 试算失败」着色；若后续插件文案改口径，需要同步转换器（不参与业务判定）。
6. **未做**：加载骨架、尺寸/文件大小、放大/缩小、打开所在文件夹、系统导航、载入本地图（均需新增功能或契约，按用户口径延后）。

## 8. Reviewer 重点

- `ColorCheckView.xaml` 中 `ImageEdit` 是否仍常驻且三条绑定逐字未变（`ActiveRoi` / `DrawObjectList` / `HImage`）。
- `LinkableValueEditor` 是否仍只有 `Port=Image` / `StepData`，没有新增/替换端口入口。
- 所有 `[StepConfig]` 属性名与类型是否原样（尤其 `DisplayViewIndex` 从 TextBox 改 ComboBox 后仍是 int）。
- `RecipeTextBox` 的 `UpdateTarget()` 是否只刷新显示、没有反向写模型。
- `ResultToneConverter` / `RoiStateConverter` 是否只做展示推断，不影响 `OnConfirm` / 运行期结果。
- 插件文案改动是否只限 `TryPreviewAnalyze()` 的空图提示，且 `FlowCanvasChecks` 的「载入」断言仍绿。

## 9. 审查修复（2026-10-20）

- **H1**：根 `MinWidth` 700→560；`SizeChanged` 在 `ActualWidth<640` 时收起画布头副标题 `CanvasSubtitle` 与状态栏 `RoiSummaryText`，640 宿主内容区 600 可容纳。
- **M1**：画布头补图例「长轴 = 线束走向 · 短轴 = 采样方向」（设计稿为画布左下，位置偏差已记录）。
- **M3**：角标/空态覆盖层 DataTrigger 由 `DisplayImage` 改为 `ImageEditor.HImage`，右键「打开图片」后覆盖层随控件 HImage 消失。
- **M2 遗留**：批量冒烟仍固定 920×520，未断言 `ImageEdit` 三条绑定、`DisplayViewIndex` 为 int、`RecipeTextBox` 只 `UpdateTarget`；约定下次改本视图前补插件专属断言。
- 是否误改共享文件（应为零）。

## 10. 遗留问题核销（2026-10-20，CC5）

| 项 | 改动 |
| --- | --- |
| `ColorCheckView.xaml.cs` | `ResultToneConverter` 删除不可达的 `"先点「载入」"` 分支（当前空图提示统一以「还没有图像」开头，已由 `StartsWith("还没有图像")` 覆盖）；`RoiStateConverter` 入参从 `string RoiSummary` 改为 `bool HasRoi`，只做 bool→「已框选 / 未框选」 |
| `ColorCheckPlugin.cs` | 新增只读属性 `public bool HasRoi => RoiParams != null && RoiParams.Length == 5;`，在 `Initialize` / `OnRoiTuplesChanged` / `WriteBackRoi` 三处与 `RoiSummary` 同步 `OnPropertyChanged`。不改 `RoiParams` 类型、长度约定、ROI 回写逻辑 |
| `ColorCheckView.xaml` | 采样区 chip 的 `Text` 改绑 `HasRoi`；`RoiChipStyle` / `RoiChipTextStyle` 的 DataTrigger 由 `RoiSummary == "尚未框选采样区"` 改为 `HasRoi == False`，彻底移除对中文精确文案的依赖 |

验证（本次修复批）：

| 项 | 结果 |
| --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.ColorCheck\Plugin.ColorCheck.csproj -v m`：**0 错误 / 0 警告** |
| 插件视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.ColorCheck`：**11/11 PASS**（elements=227；3 个命令；`InputValues=27`；hex=0） |
| Modules 产物 | `Modules\Plugin.ColorCheck.dll`：2026-10-20 **07:44:42 / 166,400 bytes** |

## 11. CC4：试算使用画布当前图（2026-10-20）

完整记录见 `docs/code-changes/2026-10-20-颜色序列检查试算使用画布当前图.md`，此处只列与视图相关的部分：

| 项 | 内容 |
| --- | --- |
| 视图命令 | `TryAnalyzeCommand = new RelayCommand(_ => Plugin?.TryPreviewAnalyze(ImageEditor?.HImage));`（原为无参调用） |
| 插件 | 新增 `internal void TryPreviewAnalyze(HImage? image)`（图源 = 传入图优先，否则 `DisplayImage`；只读，不改所有权）；`public void TryPreviewAnalyze()` 转发 null |
| 文案 | 「试算一下」ToolTip 与画布空态补「右键 → 打开图片」这条入口（仅试算，不改上游图像来源） |
| 契约 | `ImageEdit` 三条绑定未变；`DisplayImage` 所有权/上游语义/持久化未变；无独立 VM |
| 断言 | `ColorCheckSequenceCheck` 新增 `[CC4]` 7 条（P6a/P6b/P2/P4/P3/P5/P6c） |
| 变异反证 | 视图改回无参 → **P6c 准确变红**；恢复后全绿 |
