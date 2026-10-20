# 2026-10-20 Plugin.CreateRoi 自定义视图 Web 化

## 1. 范围

- 设计基线：`docs/UI设计基线/create-roi/README.md` v1 + `design.html`（本次新建，自包含、可点击、五场景）。
- 覆盖：`Plugins\Plugin.CreateRoi\CreateRoiView.xaml`、`CreateRoiView.xaml.cs`。
- 附带：`CreateRoiPlugin.cs` 仅加 6 处 `!`（null-forgiving）清掉既有 CS8601 警告，**不改任何业务逻辑/端口/命令/持久化**。
- 不在范围：`Plugin.CreateRoi` 的算法、动态输出端口、涂擦持久化、帮助手册；共享令牌/共享控件/宿主外壳/其他插件。
- 本插件没有 `StatusMessage` / `StatusLevel` 属性；本批**没有新增**这两项，状态栏只显示选中 ROI 与数量。

## 2. 耦合矩阵（SOP §5.1）

| 区域/元素 | 当前绑定/契约 | 功能依赖 | 可重构级别 |
| --- | --- | --- | --- |
| 根布局 | `MinWidth/MinHeight` 走 `PluginViewMinWidth/MinHeight`；无业务绑定 | 纯布局 | 自由（已改为左 340 + 右 `*` 两栏） |
| 图像输入 | `LinkableValueEditor Port="{Binding SrcImage}" StepData="{Binding StepData}"` | `InputPort<HImage>` 持久化、变量链接、`SetLink/RemoveLink`、运行期取值 | 只能包裹，不能替换；本插件无 `BrowseCommand`，未伪造 |
| 显示窗口 | 原 `ComboBox SelectedIndex="{Binding DisplayViewIndex}"` | `[StepConfig] int DisplayViewIndex`（0~9） | 改外观；已改为 `ItemsSource` int 数组 + `SelectedItem`，类型不变 |
| 掩膜预览 | `IsMaskPreview` 两个 RadioButton：原图 `InverseBool` + OneWay，掩膜 TwoWay | `RefreshMaskPreview` / `UpdateDisplayImage` / `DisplayImage` 通知 | 只改外观；绑定语义原样 |
| 排除模式 | `CheckBox IsChecked="{Binding MaskInvert}"` | `[StepConfig] partial bool` + `ScheduleMaskPreview` | 只改外观 |
| 涂擦模式 | `SmearMode` 通过 `ElementName=ImageEditor` + `SmearModeConverter`，参数 None/Draw/Erase | `HalconBase` 涂擦鼠标管线；进入涂擦会退出 ROI 编辑 | 只改外观；绑定逐字保留 |
| 笔刷 | `Text="{Binding BrushRadius, ElementName=ImageEditor, UpdateSourceTrigger=PropertyChanged}"` + `NumericInput.Enabled` | 控件 DP；非法输入过滤 | 只改外观 |
| 清除涂抹 | `Button Command="{Binding ClearSmearCommand}"` | 置空 `SmearDraw/SmearErase` 并持久化 | 保留命令 |
| ROI 列表 | `ItemsSource="{Binding RoiList}" SelectedItem="{Binding SelectedRoi}"` + `ListBoxAutoScroll` | 集合增删回写、选中联动、动态输出端口重建 | 可换行模板；绑定与 `SelectedItem` 不能换 |
| 删除/清空 | `DeleteSelectedCommand` / `ClearRoisCommand` + `Delete` KeyBinding | 删除/清空 ROI；清空有二次确认 | 保留命令 |
| 参数微调 | `ItemsSource="{Binding SelectedRoi.ParamEntries}"`，`TextBox Text="{Binding Text, UpdateSourceTrigger=LostFocus}"` | `RoiParamEntry` 写回 `Params` → `ParamEdited` → 画布 `HTuples` / 掩膜 | 可换布局；绑定不能换 |
| 画布 | `ImageEdit`：`HImage="{Binding DisplayImage, Mode=OneWay}"`、`DrawObjectList="{Binding CanvasRois}"`、`ActiveRoi="{Binding CanvasActiveRoi, Mode=TwoWay}"`、`SmearDraw/SmearErase` 双向 | HALCON 句柄、ROI 新建/拖拽/删除、右键菜单、涂擦、`HImage` 释放语义 | 只能常驻，不能折叠/替换 |
| 视图生命周期 | 构造后 `DataContext=this`（`GetConfigView`）；`Loaded → OnViewLoaded()` 回填 `SrcImage.ActualValue` | 打开配置时显示上游图像 | 保留 |
| 确认/取消 | 插件实现 `IPluginConfigView`（`VisionPluginBase`） | 宿主 `viewObj.DataContext as IPluginConfigView` | 不能换独立 VM |

## 3. 功能契约保留（逐项）

| # | 契约 | 状态 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 保留；`GetConfigView` 与 `CreateRoiView()` 构造未改 |
| C2 | `LinkableValueEditor`：`Port="{Binding SrcImage}"`、`StepData="{Binding StepData}"` | 保留；只设 `Label=""` 并外置标签；无 `BrowseCommand` |
| C3 | `ImageEdit` 常驻 + `HImage`/`DrawObjectList`/`ActiveRoi`/`SmearDraw`/`SmearErase` 五个绑定 | **逐字保留**；未用 `Visibility=Collapsed` 显隐已设图控件 |
| C4 | `MaskInvert` / `IsMaskPreview` / `SelectedRoi` / `RoiList` | 保留；类型分别为 bool / bool / `RoiItem` / `ObservableCollection<RoiItem>` |
| C5 | `DeleteSelectedCommand` / `ClearRoisCommand` / `ClearSmearCommand` + `Delete` 键 | 保留；无死按钮 |
| C6 | `DisplayViewIndex` int（0~9） | 保留；下拉改为 int 数组 `SelectedItem` |
| C7 | `SelectedRoi.ParamEntries` + `Text` 编辑 | 保留；`LostFocus` 写回 |
| C8 | 状态 | 本插件无 `StatusMessage`/`StatusLevel`；未新增，状态栏只读显示 `SelectedRoi.Name` / `RoiList.Count` |

## 4. 改动点

1. **两栏布局**：左 340px 配置轨（图像输入 / ROI 列表 / 参数微调 / 输出），右侧 ROI 编辑工作区；去掉原三张卡片在 320px 固定列里的高度争夺。
2. **ROI 列表升级**：卡片头显示「ROI 列表 + 计数徽标 + Del 删除选中」；行模板显示形状图标 + 名称 + 形状中文名；列表占左栏剩余高度；无 ROI 时显示空态文案；删除/清空按钮保留命令。
3. **参数微调双列**：五个参数按形状动态生成，`UniformGrid Columns=2`；无选中时显示引导文案；保留 `SelectedRoi.ParamEntries` 与 `Text` 绑定。
4. **预览头**：新增「ROI 编辑画布」标题 + 原图/掩膜 + ROI 数量 + 适应窗口 / 1:1；`FitToWindow()` / `FitToImage()` 都是 `HalconBase` 既有公开方法，未新增缩放逻辑。
5. **工具条分层**：第一行「原图 / 掩膜预览」分段控件 + 「排除模式（ROI 外有效）」；第二行「正常 / 绘制涂抹 / 擦除涂抹」+ 笔刷 + 清除涂抹。所有 `IsChecked`/`Text` 绑定原样。
6. **空态提示条**：无图时在画布上方显示黄色提示条「尚未载入图像：在画布上右键 → 打开图片，或接入上游图像」；`ImageEdit` 始终在可视树中，画布区域不折叠。
7. **状态栏**：状态点 + 「选中：ROI_x · 形状」/「未选中 ROI」+ 右侧「共 N 个 ROI」；不伪造 `StatusLevel` 三档。
8. **显示窗口**：`ComboBox` 改为 `ItemsSource` int 数组 + `SelectedItem="{Binding DisplayViewIndex}"`，仍写 int 0~9；item 模板显示「不显示 / 窗口 N」。
9. **颜色令牌化**：视图内 0 个十六进制颜色；全部走 `Dialog*` / `Plugin*` 公共令牌；未改共享字典。
10. **code-behind**：新增 `PreviewModeLabelConverter`（bool → 原图/掩膜预览）与 `FitWindow_Click` / `FitImage_Click`；保留 `InverseBoolConverter`、`SmearModeConverter`、`ListBoxAutoScroll`、`NumericInput`、`Loaded → OnViewLoaded()`。
11. **构建 0 警告**：`CreateRoiPlugin.cs` 6 处既有 CS8601 用 `!` 清除（`MaskImage.Value`、4 处 `AssemblyQualifiedName`、`typed.Value = image`），无行为变化。

## 5. 与 ImageAcquisition V2 的差异

| 维度 | ImageAcquisition V2 | CreateRoi v1 |
| --- | --- | --- |
| 配置结构 | 4 个采集模式卡片 + 模式专属参数 | 无模式卡片；固定「图像输入 → ROI 列表 → 参数微调 → 输出」 |
| 主输入 | 4 个 `LinkableValueEditor`（文件/目录/索引/超时） | 1 个 `LinkableValueEditor`（`SrcImage`，HImage） |
| 预览控件 | `ContentControl` + DataTemplate，有图才创建 `ImageReadOnly` | `ImageEdit` **常驻**（ROI 编辑必须保留画布/句柄）；空态用画布上方提示条 |
| 预览工具条 | 适应窗口 / 1:1 / 目录刷新 | 适应窗口 / 1:1 + 原图/掩膜 + 涂抹模式/笔刷/清除涂抹 |
| 状态 | `StatusMessage` / `StatusLevel` 三档 | 无 `StatusMessage`/`StatusLevel`；显示选中 ROI + 数量 |
| 宿主外壳 | 不修改 | 不修改 |
| 设计场景 | 单文件 / 目录 / 网络 / 相机 / 错误 | 默认 / 掩膜预览 / 涂抹中 / 空态 / 无 ROI |

## 6. 验证

| 项 | 命令 | 结果 |
| --- | --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.CreateRoi\Plugin.CreateRoi.csproj -v m` | **0 错误 / 0 警告**；`Modules\Plugin.CreateRoi.dll` 已刷新（2026-10-20 02:52:45） |
| 插件视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.CreateRoi` | **10/10 PASS**（`GetConfigView`、视图实例化 190 elements、`DataContext/IPluginConfigView`、`LinkableValueEditor.Port`、Button.Command、ComboBox、TextBox、OnConfirm、XAML hex=0、渲染 PNG） |
| 渲染证据 | `docs/UI设计基线/_batch-evidence/Plugin.CreateRoi-CreateRoiPlugin.png`（初始空态）；`docs/UI设计基线/create-roi/evidence/after-render-v1-populated.png`（选中 ROI_1、2 个 ROI、5 个参数框的填充态）；`after-render-v1-narrow.png`（620×432 窄档） | 空态布局 + 填充态布局；HALCON `HwndHost` 不计入像素 |
| 常规 gate | `dotnet run --project FlowCanvasChecks --no-build` | **2691 通过 / 0 失败**（含 `[CreateRoi]` 段与视图实例化断言） |
| 设计页脚本 | `node --check` 提取的 `<script>` | 语法通过；五场景/宽度按钮可点击 |
| 共享文件 | 未修改 `Colors.xaml`、`PluginConfigColors.xaml`、`UIThemeSmokeTest\Program.cs/.csproj/PluginViewSmoke.cs`、`PluginConfigShellView.xaml`、`docs/UI设计基线/README.md`、其他插件 | 无 |

## 7. 已知偏差 / 遗留

1. **空态形态**：Web 稿在画布中央显示虚线空态卡；WPF 因 `ImageEdit` 是 HALCON `HwndHost`（airspace 限制），改为画布上方提示条 + 状态栏文案，`ImageEdit` 始终常驻。这是遵守「不折叠已设图画布」硬规则的取舍，已写入设计 README 偏差 #4。
2. **渲染证据范围**：`RenderTargetBitmap` 不捕获 `HwndHost`，证据 PNG 只证明 WPF 布局/提示条；真实 ROI 预览、句柄拖拽、涂擦需真机窗口验证。
3. **有 ROI 选中态的 DataTemplate**：公共 `--plugin-views` 冒烟在空 ROI 下实例化；本批额外用临时 STA 探针验证了「2 个 ROI + 选中 ROI_1」的填充态（列表 2 项、参数框 5 个、状态栏文案正确），证据 `create-roi/evidence/after-render-v1-populated.png`。reviewer/真机仍需复核句柄拖拽与涂擦交互。
4. **640×560 最小档**：宿主 MinHeight 已统一 560；左栏改为 `ScrollViewer`（ROI 列表 `MinHeight=200`、`MaxHeight=320`），短窗口下滚动查看参数卡/输出。960×680 默认档参数卡接近完整可见；640×560 需真机复核滚动体验。
5. **图像输入/输出标签压缩**：Web 稿的「源图像」标签与独立操作提示在 WPF 中收进 ToolTip/卡片头；输出区保留「输出」标题、去掉「显示窗口」标签（下拉自解释）。共省约 70px，960×680 默认档实测 `ScrollableHeight=0`（临时 STA 探针）。已在设计 README 偏差 #11/#12/#13 记录。
6. **未纳入**：加载骨架、图像尺寸/文件大小、放大/缩小、打开所在文件夹、系统导航、ROI 内联重命名（均需新增功能/契约）。
7. **设计基线**：v1 仍待用户确认；确认后按规则只读，改动升 v2。
8. **`LinkableValueEditor` 的 HImage 占位**：`InputPort<HImage>` 不可文本输入，控件显示「需链接变量」占位（共享控件既有行为）；本视图只加了外层字段边框，未改控件内部。

## 8. 复跑命令

```text
dotnet build Plugins\Plugin.CreateRoi\Plugin.CreateRoi.csproj -v m
dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.CreateRoi
dotnet build FlowCanvasChecks\FlowCanvasChecks.csproj -v q
dotnet run --project FlowCanvasChecks --no-build
```

> `UIThemeSmokeTest` 未重新构建（按本次任务约束），使用现有二进制；插件 DLL 已由插件工程刷新到 `Modules`。
> `FlowCanvasChecks` 构建有既有 186 条警告（断言宿主自身），本次 gate 以 `--no-build` 运行，结果 2691/0。

## 9. 遗留问题核销（2026-10-20，CR1~CR5）

| 项 | 改动 |
| --- | --- |
| CR1 | `Models\RoiItem.cs` 的 `ShapeType` setter 补 `OnPropertyChanged(nameof(ShapeTypeCN))`；只加通知，不改形状枚举/参数重建逻辑 |
| CR2 | `CreateRoiView.xaml` 把「ROI 编辑」chip 从 `ImageEdit` 同 Grid 移到画布头（HwndHost 之外）；涂抹态文字放到工具条（笔刷右侧），按 `ImageEdit.SmearMode` 显示「绘制涂抹 / 擦除涂抹 · 笔刷 N px」；画布网格未实现，已写入设计 README 已知偏差 #14，不伪造 |
| CR3 | ROI 行图标按 `ShapeType` DataTrigger 切换：矩形 `&#xf0c8;`（FA square）、圆形 `&#xf111;`（FA circle）、椭圆用 `&#xf111;` + `ScaleTransform(1.55, 0.72)`（该字体无独立 ellipse 字形）；全部走 `DialogIconFont` |
| CR4 | 删除无引用的 `FieldLabelStyle` 样式与 `DisplayViewCombo` 的 `x:Name`（`findstr` 全文件确认零引用） |
| CR5 | `docs/UI设计基线/create-roi/design.html` 补 `.cr-mask.is-invert{filter:invert(1);}`，勾选「排除模式」时掩膜预览反转 |

验证（本次修复批）：

| 项 | 结果 |
| --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.CreateRoi\Plugin.CreateRoi.csproj -v m`：**0 错误 / 0 警告** |
| 插件视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.CreateRoi`：**11/11 PASS**（elements=193；1 个 Port；5 个按钮命令；`InputValues=6`；hex=0） |
| Modules 产物 | `Modules\Plugin.CreateRoi.dll`：2026-10-20 **07:47:13 / 131,584 bytes** |
