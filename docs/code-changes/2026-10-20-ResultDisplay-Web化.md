# 2026-10-20 ResultDisplay 自定义视图 Web 化

## 范围

- 设计基线：`docs/UI设计基线/result-display/README.md` v1 + `design.html`（自包含、可点击、默认/空结果项/边界值三态）
- 覆盖：`Plugins\Plugin.ResultDisplay\ResultDisplayView.xaml` + `ResultDisplayView.xaml.cs`
- 不在范围：插件类 `ResultDisplayPlugin.cs`、`DisplayItemDef`、`TextAnchorKind`、`Services\DisplayDraw`、`AnnotationBaker`、文字九宫格/文字定位（R39）、宿主外壳 `PluginConfigShellView.xaml`、公共令牌与 `UIThemeSmokeTest` 源码
- 本次未修改任何共享文件：`Colors.xaml`、`PluginConfigColors.xaml`、`UIThemeSmokeTest` 的 `Program.cs` / `.csproj` / `PluginViewSmoke.cs`、`PluginConfigShellView.xaml`、其它插件

## 耦合矩阵（改动前）

| 区域/元素 | 当前绑定/契约 | 功能依赖 | 可重构级别 |
| --- | --- | --- | --- |
| 根布局 | `ScrollViewer` + 四张卡片纵向堆叠；无根级绑定 | 纯布局 | 自由（保留根 `UserControl`、`MinWidth/MinHeight`） |
| DataContext / 生命周期 | `GetConfigView` → `new ResultDisplayView { DataContext = this }`；插件实现 `IPluginConfigView` | 宿主靠 `viewObj.DataContext as IPluginConfigView` 取 `OnConfirm` | **不能换独立 VM**；构造与 `Initialize(stepData)` 时序不变 |
| 底图 | `cv:LinkableValueEditor Port="{Binding SrcImage}" StepData="{Binding StepData}"` | `InputPort<HImage>` 的链接、解除链接、类型转换、持久化 | 只能包裹；`Port` / `StepData` 原样 |
| 结果项列表 | `ItemsControl ItemsSource="{Binding Items}"`；每行 `CheckBox Enabled`、`TextBox Label`、行内 `LinkableValueEditor`、`TextBox Format`、`TextBox Unit`、`Button Click` 删除 | `Items` 是 `[StepConfig]`；增删/改名触发 `RebuildDynamicInputs()`；行内端口靠 `PortsVersion` 重取 | 行模板可重排；**行内 `Port` 多绑定不能换** |
| 结果项动态端口 | `MultiBinding`：`Id` + 插件 `DataContext` + `DataContext.PortsVersion` → `InputPortConverter` | 端口名 = 稳定 `Id`（r1/r2…）；`Label` 改名不断线；`PortsVersion` 是端口重建锚点 | 只能包裹；三个绑定值缺一不可 |
| 添加结果项 | `AddItem_Click` → `Plugin.AddItem()` | 生成不重名 `Id`/`Label`，并 `Items.Add` 触发端口重建 | 只转发；不改生成规则 |
| 删除结果项 | `DelItem_Click` → `Plugin.Items.Remove(item)` | 集合变更触发端口重建、判定项候选刷新 | 只转发；按行对象删除 |
| 显示窗口 | `TextBox Text="{Binding DisplayViewIndex}"` | `[StepConfig] int`，setter 夹取 0~9；0 = 只烧录不发布 | 可换下拉；**类型必须仍为 int** |
| 判定项 | 可编辑 `ComboBox ItemsSource="{Binding JudgeItemLabels}" Text="{Binding JudgeItemLabel}"` | 候选来自启用项 Label；R35：换 `ItemsSource` 时保留当前文本；允许手填 | 只能包裹；**必须保持可编辑** |
| NG 值 | `TextBox Text="{Binding NgValue}"` | `[StepConfig] string`，默认 "NG"；空值 setter 回退 "NG" | 只改外观 |
| 图上叠字 | `CheckBox IsChecked="{Binding ShowTextOnImage}"` | `[StepConfig] bool`；关掉只烧图形不烧字 | 只改外观 |
| 九宫格 | `ComboBox ItemsSource="{Binding TextAnchors}" SelectedItem="{Binding TextAnchor}"` + `EnumDisplayNameConverter` | `TextAnchorKind` 成员顺序承重（`(int)/3`、`%3`）；中文来自 `[Display]` | 可换控件外观；**枚举顺序与类型不能动** |
| 边距 / 字号 | `TextBox Text="{Binding TextMargin}"` / `TextScale` | `[StepConfig] int 0~4096` / `double 0.5~3.0`；影响 `DisplayDraw` 排版 | 只改外观；类型与 setter 夹取不变 |
| 绘制上限 | `TextBox Text="{Binding MaxDrawElements}"`（高级 Expander） | `[StepConfig] int 10~2000`；超限只画前 N 个元素 | 只改外观 |
| 状态 | 当前视图**没有** `StatusMessage` / `StatusLevel` 绑定 | 插件基类没有这两个属性；本插件也没有预览状态机 | 不新增、不伪造第四档 |
| HALCON 控件 | 当前视图**没有** `ImageReadOnly` / `ImageEdit` | 结果显示只在运行期用 `AnnotationBaker` 烧录，配置期不预览 | 不新增画布；不改烘焙/文字定位 |
| 命令 | 当前只有两个 `Click` 处理器（添加/删除） | 视图动作入口 | 可改为视图级 `ICommand` 转发既有公开方法 |

**判定原则**：`SrcImage`、每行动态端口、`Items`、`DisplayViewIndex`、`JudgeItemLabel`、`NgValue`、`ShowTextOnImage`、`TextAnchor`、`TextMargin`、`TextScale`、`MaxDrawElements` 都是“唯一入口”，本轮只改外观/布局，不换绑定源与类型。

## 功能契约保留

| # | 契约 | 状态 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 保留；`GetConfigView` 未改，视图构造签名未改 |
| C2 | 底图 `LinkableValueEditor`：`Port=SrcImage`、`StepData=StepData` | 保留；只设 `Label=""` 外置标签 |
| C3 | 结果项行内 `LinkableValueEditor`：`Port` 多绑定（`Id` + 插件 + `PortsVersion`）、`StepData` | 保留；`InputPortConverter` 未改 |
| C4 | 全部 `[StepConfig]` 属性 | 保留：`DisplayViewIndex`（int 0~9）、`ShowTextOnImage`、`JudgeItemLabel`、`NgValue`、`MaxDrawElements`、`TextAnchor`、`TextMargin`、`TextScale`、`Items` |
| C5 | `AddItem()` / `Items.Remove(item)` | 保留；视图命令只转发，不改生成/删除规则 |
| C6 | 判定项候选 `JudgeItemLabels` 与 R35 文本保留逻辑 | 保留；仍为可编辑 ComboBox，`Text` 双向绑定 |
| C7 | 烧录与文字定位 | **未改**：`AnnotationBaker`、`DisplayDraw`、`TextAnchorKind` 九宫格顺序、R39 文字宽度/对齐能力 |
| C8 | 状态 | 本插件没有 `StatusLevel` / `StatusMessage`；本版不新增状态栏、不伪造三档 |

> 本插件没有 `Mode`、`SelectedCameraOption`、HALCON 画布；本轮不新增、不伪造。
> `SelectedItem` 仅用于 `DisplayViewIndex`（int）与 `TextAnchor`（枚举）。

## 改动点

1. **两栏布局**：左侧配置轨（底图 / 显示与判定 / 文字位置 / 高级 / 信息条）+ 右侧结果项工作区；根 `MinWidth=560`、`MinHeight=320`，不写死 900×500。
2. **结果项卡片化**：每项一张两行卡；第一行启用勾选 / 名称 / 删除，第二行「取值（`LinkableValueEditor`）/ 格式 / 单位」，并加一行 10.5px 字段标签。窄窗口不再挤成六列窄表。
3. **显示窗口下拉化**：`TextBox` → `ComboBox`（`ViewIndexes` 0~9），仍绑 `DisplayViewIndex` int；0 档文案「不显示（只烧录）」，帮助文字随选择说明后果。
4. **判定项保持可编辑**：改用本视图本地 `JudgeComboStyle`（只设尺寸、不设 `Template`），`Template` 回落框架默认模板（含 `PART_EditableTextBox`），`ItemsSource=JudgeItemLabels`、`Text=JudgeItemLabel`；空值时用覆盖层显示「不判定（空）」，不把空串改成业务值。**不再使用 `PluginModernComboBase`**：它和 UI 库 `Selection.xaml` 的隐式 ComboBox 样式都没有 `PART_EditableTextBox`，`IsEditable=True` 会静默失效。
5. **九宫格中文下拉**：`ComboBox ItemsSource=TextAnchors SelectedItem=TextAnchor`，条目仍由 `EnumDisplayNameConverter` 读 `[Display]`；未改枚举顺序。
6. **添加/删除命令化**：移除两个 `Click` 处理器，新增视图级 `AddItemCommand` / `DeleteItemCommand`（`RelayCommand`），分别转发 `Plugin.AddItem()` 与 `Items.Remove(item)`；满足通用插件视图冒烟的 `Button.Command` 检查，不新增业务。
7. **空态与停用态**：`Items.Count == 0` 显示虚线空态与添加入口；`Enabled=False` 的项整行 `Opacity=0.62` + `DialogRowAlternateBrush` 底，配置保留、仍可编辑。
8. **项数胶囊**：右侧标题旁绑 `Items.Count`（`{0} 项`），用 `Typography.NumeralAlignment="Tabular"`。
9. **全令牌化**：视图 XAML 零十六进制颜色；全部使用 `Dialog*` / `Scada*` / `Plugin*` 既有公共令牌。**没有新增令牌**。
10. **共享件未动**：`LinkableValueEditor`、`ImageReadOnly`、`HalconBase`、`PluginConfigStyles.xaml`、`VerTabControl`、`PluginConfigShellView.xaml` 均未修改。
11. **设计基线**：新建 `docs/UI设计基线/result-display/design.html`（v1，自包含、可点击、内联 SVG、默认/空结果项/边界值三态、960/640 宽度、九宫格与折叠区可交互）与 `README.md`（版本、范围、设计要点、令牌映射、契约保留、已知偏差、未纳入项）；浏览器截图证据 `evidence/design-preview-v1.png`、`evidence/state-empty.png`、`evidence/state-edge.png`。

## 与 ImageAcquisition V2 的差异

| 维度 | ImageAcquisition V2 | ResultDisplay v1 |
| --- | --- | --- |
| 右侧主区 | 预览工作区（`ContentControl` + `DataTemplate` 按需创建 `ImageReadOnly`） | **结果项工作区**（`ItemsControl` + 每项两行卡）；本插件没有 HALCON 画布，配置期不预览 |
| 模式选择 | 2×2 模式卡片（`RadioButton` + `EnumConverter`） | 无模式；左栏是单页表单 |
| 表单控件 | 4 个 `LinkableValueEditor`（路径/目录/索引/超时） | 1 个底图 `LinkableValueEditor` + 每行 1 个动态端口 `LinkableValueEditor` |
| 下拉 | 相机/显示窗口 `DialogCombo` | 显示窗口/九宫格 `DialogCombo`；判定项必须可编辑，使用本视图本地 `JudgeComboStyle`（默认模板，含 `PART_EditableTextBox`） |
| 状态 | `StatusMessage` / `StatusLevel` 三档 + 预览状态栏 | 插件没有状态属性，**不设状态栏**；右侧脚注是静态绘制说明 |
| 命令 | 插件命令（`Browse*` / `Refresh` / `Prev` / `Next` / `PushLocalImage`） | 视图级 `AddItemCommand` / `DeleteItemCommand` 转发既有公开方法 |
| 预览刷新 | 明确避免 `Visibility=Collapsed` 显隐已设图控件 | 无 HALCON 控件，不涉及该规则 |
| 令牌 | 新增 10 个公共令牌（ImageAcquisition V2） | **零新增**；全部复用既有令牌 |
| 滚动条 | 左栏滚动条占 17px | 左栏滚动条为覆盖式：分节提示改左对齐、底图行 `MaxWidth=280`，避免图标被压住 |
| 证据 | 4 模式 5 张 PNG | 批量冒烟初始态 1 张 PNG（`_batch-evidence/Plugin.ResultDisplay-ResultDisplayPlugin.png`） |

## 验证

| 项 | 命令 | 结果 |
| --- | --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.ResultDisplay\Plugin.ResultDisplay.csproj -v m` | **0 错误 / 0 警告** |
| `Modules` 产物 | `Modules\Plugin.ResultDisplay.dll` | 已刷新：2026-10-20 02:02:27，115,200 字节 |
| 通用插件视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.ResultDisplay` | **11/11 PASS** |
| 冒烟明细 | 同上 | `GetConfigView` 返回 `ResultDisplayView`；视图实例化 **282 个元素**；`DataContext` 是 `ResultDisplayPlugin`；`LinkableValueEditor` **2 个**（`SrcImage`、`r1`）且 `Port` 非空；`Button.Command` **3 个按钮 / 2 条命令**（`AddItemCommand`、`DeleteItemCommand`）；`DisplayViewIndex` / `JudgeItemLabels` / `TextAnchors` 绑定齐全；`TextBox.Text` 绑定 `PortValue,LinkAddress,NgValue,TextMargin,TextScale,Label,Format,Unit`；`OnConfirm` 可执行（`InputValues=11`）；**XAML hex=0** |
| 渲染证据 | 批量冒烟输出 | `docs/UI设计基线/_batch-evidence/Plugin.ResultDisplay-ResultDisplayPlugin.png`（920×520 初始态） |
| 设计基线 | `design.html` | 自包含、可点击；默认/空结果项/边界值三态、960/640 宽度、九宫格、折叠区、添加/删除/停用交互 |
| `UIThemeSmokeTest` 构建 | **未构建**（按用户约束，共享工程由主会话统一构建） | `--no-build` 运行现有二进制；未修改其源码，符合 R65 的前提 |
| `FlowCanvasChecks` | 未运行 | 本轮未改公共契约/业务代码；按用户约束只做插件构建 + `--plugin-views`。主会话统一 gate 时复跑 |

> 说明：通用冒烟只验证“视图可实例化 / 端口存在 / 命令存在 / `OnConfirm` 可执行 / hex=0”，
> **不逐项验证每个字段写回 `InputValues` 的键值**。本插件的业务字段（`DisplayViewIndex`、`JudgeItemLabel`、`NgValue`、`TextAnchor`、`TextMargin`、`TextScale`、`MaxDrawElements`、`Items`）仍需 reviewer 按绑定逐项复核 + 真机回环。

## 遗留问题

1. **真机未验收**：打开配置 → 改每个字段 → 确认 → 重开；底图/结果项链接与解除链接；试运行烧录；关闭无崩溃/泄漏。`RenderTargetBitmap` 只证明 WPF 布局，不证明运行期烧录。
2. **未生成 4~5 张分状态渲染 PNG**：本批按用户指定只跑 `--plugin-views` 初始态；空结果项/边界值状态由 `design.html` 可点击展示，WPF 端如需逐状态证据，需主会话另加 `--result-display-render` 模式（本插件未改 `UIThemeSmokeTest` 源码）。
3. **判定项可编辑下拉的模板（RD1 已修复）**：原 `PluginModernComboBase` 与 UI 库 `Selection.xaml` 的隐式 ComboBox 样式都没有 `PART_EditableTextBox`，`IsEditable=True` 静默失效。现改用本视图本地 `JudgeComboStyle`（只设尺寸、不设 `Template`，显式样式挡掉隐式样式，`Template` 回落框架默认模板），不改共享 `PluginConfigStyles.xaml`。运行时探针已实测：`Template.FindName("PART_EditableTextBox")` 非空、`IsEditable=True`、手填文本写回 `JudgeItemLabel`。
4. **左栏滚动条为覆盖式**：分节提示改左对齐、底图行 `MaxWidth=280`，属与 Web 稿的已知偏差；真机 640×560 下需复核底图行与 🔗/✕ 图标是否完整可见。
5. **`Format` / `Unit` 的 watermark**：`DialogInput` 的 `Tag` 占位在冒烟宿主里未显示（只显示白框）；WPF 端已在第二行上方补 10.5px 字段标签「格式 / 单位」，不依赖 watermark。
6. **无配置态预览**：这是插件既有设计（烧录只能在流程线程执行），不是本轮遗漏；设计稿也没有伪造预览。
7. **未新增公共令牌**：本视图没有颜色令牌缺口；若后续要做深色主题/更细的语义色，需主会话统一追加，不在本插件里写十六进制。
8. **`FlowCanvasChecks` 未跑**：主会话统一 gate 时复跑；本轮未改 `AnnotationBaker` / `DisplayDraw` / `TextAnchorKind`，预期不影响既有 RD 断言。

## 复跑命令

```text
dotnet build Plugins\Plugin.ResultDisplay\Plugin.ResultDisplay.csproj -v m
dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.ResultDisplay
```

> `UIThemeSmokeTest` 退出码按 R46 可能恒为 1（既有 GridView 红灯）；本轮以日志行判定，
> 输出为 **11 PASS / 0 FAIL** 且末尾打印「插件自定义视图批量冒烟：全部通过」。

## C 组遗留修复（2026-10-20）

| ID | 修复 | 说明 |
| --- | --- | --- |
| RD1 | `JudgeCombo` 去掉 `ModernCombo`，改用本视图本地 `JudgeComboStyle`（只设尺寸、不设 `Template`） | 共享 `PluginModernComboBase` 与 UI 库 `Selection.xaml` 的隐式 ComboBox 样式都没有 `PART_EditableTextBox`，`IsEditable=True` 会静默失效；显式本地样式挡掉隐式样式后，`Template` 回落框架默认模板（含 `PART_EditableTextBox`），不改共享 `PluginConfigStyles.xaml`。 |

**验证**：插件构建 **0 错误 / 0 警告**；`--plugin-views Plugin.ResultDisplay` **11/11 PASS**（elements=291）；临时 STA 探针实测 `Template.FindName("PART_EditableTextBox")` 为 `TextBox`、`IsEditable=True`、`combo.Text="手填判定项"` 经绑定写回 `JudgeItemLabel`。RD2（渲染证据数量偏少）/RD3（视图重复实现 RelayCommand）未做。
