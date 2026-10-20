# 2026-10-20 ColorRegion 自定义视图 Web 化

## 1. 范围

| 项 | 内容 |
| --- | --- |
| 设计基线 | `docs/UI设计基线/color-region/README.md` v1 + `design.html`（自包含、可点击） |
| 覆盖 | `Plugins\Plugin.ColorRegion\RegionColorView.xaml`、`RegionColorView.xaml.cs` |
| 附带最小修正 | `RegionColorPlugin.cs` 一处 `RoiParams!`（仅消除既有 CS8604 可空性告警，不改行为） |
| 不在范围 | 插件业务逻辑、端口名、命令名、持久化、颜色算法、ROI 交互、共享控件/共享字典、其他插件 |
| 共享文件 | **未修改**：`Colors.xaml`、`PluginConfigColors.xaml`、`UIThemeSmokeTest\Program.cs`/`.csproj`/`PluginViewSmoke.cs`、`PluginConfigShellView.xaml`、`docs/UI设计基线/README.md` |

本次只做 `Plugin.ColorRegion`（`RegionColorPlugin`）一个插件；设计基线先落盘，再按基线复刻 WPF。

## 2. 功能契约保留

| # | 契约 | 状态 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 保留：`GetConfigView` 仍 `new RegionColorView { DataContext = this }`；`Loaded → OnViewLoaded()` 未改 |
| C2 | `LinkableValueEditor`：`Port="{Binding Image}"`、`StepData="{Binding StepData}"` | 保留；只设 `Label=""` 外置标签，未加 BrowseCommand（本插件无文件浏览） |
| C3 | `ImageEdit`：`HImage="{Binding DisplayImage, Mode=OneWay}"`、`DrawObjectList="{Binding CanvasRois}"`、`ActiveRoi="{Binding CanvasActiveRoi, Mode=TwoWay}"` | 逐字保留；控件常驻，不折叠、不换控件 |
| C4 | 配置属性与类型：`ExpectedColors`(string)、`MinShare`(double)、`SamplePoints`(int)、`DisplayViewIndex`(int)、`RoiShape`(string)、`RoiParams`(double[5])、13 项阈值 | 属性名/类型未动；`OnConfirm` 仍由 `VisionPluginBase` 按 `[StepConfig]` 反射写回 |
| C5 | 三个既有公开方法：`ClearRoi()`、`TryPreviewAnalyze()`、`ApplyPreviewAsExpected()` | 保留；视图用 `RelayCommand` 转发，不复制业务逻辑 |
| C6 | 三处只读展示：`Hint`、`ResultSummary`、`RoiSummary` | 保留；本插件没有 `Mode` / `StatusLevel`，未伪造这两种属性 |

**类型口径**：`DisplayViewIndex` 仍是 `int`（0~9）；`RoiShape` 仍是字符串（`Rectangle`/`Circle`/`Ellipse`），没有改成枚举。

## 3. 耦合矩阵（SOP §5.1）

| 区域/元素 | 当前绑定/契约 | 功能依赖 | 可重构级别 |
| --- | --- | --- | --- |
| 根布局 | `UserControl MinWidth=700 MinHeight=360`；根 `Grid Margin=12`，左 330 / 右 `*` | 纯布局 | 自由；改为左 1*（300~356）/ 右 1.6*，保留 `MinWidth/MinHeight` 语义并下调到 560×320 |
| 图像输入 | `LinkableValueEditor Port="{Binding Image}" StepData="{Binding StepData}"` | `InputPort<HImage>`、变量链接、端口持久化、`Image.PropertyChanged` 驱动 `ShowUpstreamImage` | **只能包裹**；保留控件与两条绑定 |
| 采样区画布 | `ImageEdit ActiveRoi="{Binding CanvasActiveRoi, Mode=TwoWay}" DrawObjectList="{Binding CanvasRois}" HImage="{Binding DisplayImage, Mode=OneWay}"` | 右键新建/删除 ROI、拖拽改大小、`CollectionChanged → WriteBackRoi`、`HTuples` 变更回写、`DisplayImage` 释放语义 | **保留控件与绑定**；只改外层布局；空态用覆盖层，不折叠控件 |
| 采样区摘要 | `RoiSummary`（只读，`WriteBackRoi` 内 `OnPropertyChanged`） | 显示当前形状/中心/角度/半长/半径 | 自由（只读展示） |
| 清空采样区 | `Click → ClearRoi()` → `CanvasRois.Clear()` → `WriteBackRoi()` → `RoiParams = []` | 清除采样区并持久化为空 | **入口必须保留**；改为 `RelayCommand` 转发同一方法 |
| 期望颜色 | `TextBox.Text="{Binding ExpectedColors, UpdateSourceTrigger=PropertyChanged}"` | `[StepConfig] string`，`OnConfirm` 写回；`ParseExpected` 用逗号/顿号拆分 | 只改外观；属性名/类型不变 |
| 最小占比 | `TextBox.Text="{Binding MinShare}"` | `[StepConfig] double`（0~1），`Judge` 使用 | 只改外观；控件 36px |
| 试算一下 | `Click → TryPreviewAnalyze()` → `DisplayImage` 采样 → `ResultSummary` | 配置期试算，只分析不判定 | **入口必须保留**；改为 `RelayCommand` 转发 |
| 把主色填成期望颜色 | `Click → ApplyPreviewAsExpected()` → 写 `ExpectedColors` + `Hint` | 用上一次试算主色回填期望 | **入口必须保留**；视图在命令后 `UpdateTarget()` 刷新输入框（属性本身无 INPC） |
| 结果摘要 | `ResultSummary`（INPC，多行文本） | 显示主色/中位色/明细/判定 | 自由；只读多行块 + 文案三态着色 |
| 采样点数上限 | `TextBox.Text="{Binding SamplePoints}"` | `[StepConfig] int`，`ColorSampler.TryBuildRoiPoints` 抽稀 | 只改外观；收进「高级参数」 |
| 投射视图窗口 | `TextBox.Text="{Binding DisplayViewIndex}"` | `[StepConfig] int`，0 = 不投射，1~9 投到视图窗口 | 只改外观；改为 0~9 的 `ComboBox`，`SelectedItem` 仍绑 int |
| 颜色判据阈值 | `cc:ColorThresholdEditor` 继承 DataContext，按 13 个同名属性绑定 | 13 个 `[StepConfig]` double/int；`BuildThresholds` 组装 `ColorThresholds` | **只能包裹**；共享控件未改，收进「高级参数」 |
| 提示 | `Hint`（INPC） | 载入结果/错误原因/操作提示 | 自由；放到画布状态栏 |
| 状态级别 | **不存在** `StatusLevel` | 插件只有 `Hint` / `ResultSummary` / `RoiSummary` | 不新增；状态点固定为主色信息点，不伪造三档 |
| 持久化 | `VisionPluginBase.Initialize/OnConfirm` + `[StepConfig]` + `InputPort` | `.vms` 存取、流程编译 | **未改** |

## 4. 改动点

1. **整页重写 `RegionColorView.xaml`**：从「5 张竖排 `PluginCard` + 右侧一张 `ImageEdit` 卡片」改为两栏——左 356px 配置轨（图像输入 / 采样区 / 检查参数 / 高级参数 / 怎么用），右侧采样画布工作区（44px 画布头 + 画布 + 40px 状态栏）。
2. **采样区独立区块**：形状 chip（`RoiShapeLabelConverter` 把 `Rectangle/Circle/Ellipse` 显示为矩形/圆形/椭圆）+ `RoiSummary` 等宽摘要 + 「清空采样区」整行按钮。
3. **检查参数表单化**：`ExpectedColors` / `MinShare` 原绑定；「试算一下」主按钮、「把主色填成期望颜色」次按钮；结果块改为只读多行 `TextBox`，`ResultToneConverter` 按文案给出 Info / Warning / Error 三态底色（仅视觉，不新增 `StatusLevel`）。
4. **高级参数折叠**：`SamplePoints`、`DisplayViewIndex`（0~9 `ComboBox`）、`ColorThresholdEditor` 收进 `Expander`；13 项阈值仍由共享控件按 DataContext 同名属性绑定。
5. **画布常驻**：`ImageEdit` 仍是原控件、原三条绑定；无图时只在上面覆盖一层 WPF 空态卡，**没有**对 `ImageEdit` 设 `Visibility=Collapsed`。工具条只保留「适应窗口（`FitToWindow`）」与「1:1（`FitToImage`）」。
6. **code-behind**：新增 `ClearRoiCommand` / `TryAnalyzeCommand` / `ApplyExpectedCommand`（`Core.Commands.RelayCommand`，只转发插件公开方法）；新增 `RoiShapeLabelConverter`、`ResultToneConverter`、`ViewIndexLabelConverter`；`FitWindow_Click` / `FitImage_Click` 直接调 `ImageEditor` 的既有方法。没有独立 VM。
7. **`RegionColorPlugin.cs` 最小修正**：`ParamsForCanvas(RoiShape, RoiParams!)` 消除既有 CS8604 可空性告警；没有改任何运行时行为、端口或算法。
8. **P1 形状 chip 同步修复**：`WriteBackRoi()` 写 `RoiShape` 后通过 `SetRoiShape()` 补 `OnPropertyChanged(nameof(RoiShape))`；`ClearRoi()` 无 ROI 时把 `RoiShape` 归一为 `Rectangle` 并通知；`Initialize` 的 `hasRoi == false` 分支同样归一。详见 §10。
9. **未修改共享文件**：颜色全部走 `Dialog*` / `PluginPreview*` / `ScadaSecondaryTextBrush`；视图 XAML `hex=0`；未向 `Colors.xaml` / `PluginConfigColors.xaml` 追加键。

## 5. 与 ImageAcquisition V2 的差异

| 维度 | ImageAcquisition V2 | ColorRegion v1 |
| --- | --- | --- |
| 信息架构 | 4 个采集模式 2×2 卡片 + 模式专属面板 | 单模式单算子：图像输入 / 采样区 / 检查参数 / 高级参数 / 用法，无模式卡片 |
| 图像预览 | `ContentControl PreviewHost` 按需创建 `ImageReadOnly`（有图才创建） | **`ImageEdit` 必须常驻**（ROI 右键/拖拽/`ActiveRoi` 需要真实控件与句柄）；空态是覆盖层，控件不折叠 |
| 工具条 | 适应窗口 / 1:1 / 目录刷新 | 只有适应窗口 / 1:1（无目录、无刷新命令） |
| 输入 | 4 个 `LinkableValueEditor`（路径/目录/索引/超时）+ 浏览命令 | 1 个 `LinkableValueEditor`（Image）；无 BrowseCommand，图像只来自上游 |
| 结果 | 预览状态栏 `StatusMessage` + `StatusLevel` 三档 | 结果块 `ResultSummary` 文案三态着色 + 状态栏 `Hint`；插件没有 `StatusLevel`，不伪造 |
| 状态栏 | 状态点 + 消息 + 路径 | 主色信息点 + `Hint` + `RoiSummary`（无路径） |
| 底部宿主栏 | 宿主 `StatusText` + 耗时 + 执行/取消/确认 | 同左，插件视图不改宿主栏 |
| 令牌 | `Dialog*` + `PluginPreview*` | 同一套；无新增令牌 |

## 6. 设计基线

- `docs/UI设计基线/color-region/design.html`：v1，自包含、无外部依赖；内联 SVG 画布示意；可点击验证：
  - 窗口宽度 960 / 640；
  - 画布状态 默认 / 空态 / 试算失败；
  - 采样区形状 矩形 / 圆形 / 椭圆（chip、摘要、SVG 采样区同步）；
  - 「清空采样区」「试算一下」「把主色填成期望颜色」均有真实页面反馈。
- `docs/UI设计基线/color-region/README.md`：版本、范围、现状审查、设计要点、状态矩阵、契约保留、令牌映射、复刻实现、已知偏差、未纳入项、验证。
- 浏览器验证：在本地 `http://127.0.0.1:8899/design.html` 打开，确认默认/空态/试算失败、三种形状、960/640 切换均生效（页面脚本返回 `canvasShape=circle`、`emptyVisible=true`、`resultClass=cr-result is-err`、`narrow=true`）。

## 7. 验证

| 项 | 命令 / 判据 | 结果 |
| --- | --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.ColorRegion\Plugin.ColorRegion.csproj -v m` | **0 错误 / 0 警告** |
| Modules 产物 | `Modules\Plugin.ColorRegion.dll` 时间戳 | **2026-10-20 02:58:16**，与 `bin\Debug` 一致，102,912 字节 |
| 批量视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.ColorRegion` | **全部通过（11 条 PASS）** |
| 视图实例化 | 元素数 | `elements=190` |
| 端口 | `LinkableValueEditor.Port` | `count=1 ports=Image` |
| 命令 | `Button.Command` | `buttons=5 commands=ClearRoiCommand,TryAnalyzeCommand,ApplyExpectedCommand` |
| 绑定 | `TextBox.Text` | `PortValue,LinkAddress,ExpectedColors,MinShare,ResultSummary` |
| 确认 | `OnConfirm` | `InputValues=20`（含 13 项阈值 + 6 个配置属性 + 端口） |
| 静态 | XAML `hex=0` | `xaml=1 hex=0` |
| 渲染 | 批量证据 PNG | `docs/UI设计基线/_batch-evidence/Plugin.ColorRegion-RegionColorPlugin.png` |
| 设计稿 | 浏览器交互验证 | 默认/空态/试算失败、矩形/圆形/椭圆、960/640 均通过 |
| P1 形状回环 | 独立探针（见 §10） | 加圆形 ROI → `RoiShape=Circle`、`RoiParams=5`、通知 `RoiShape/RoiSummary`；`ClearRoi()` → `RoiShape=Rectangle`、`RoiParams=0`、通知 `RoiShape/RoiSummary` |
| 常规 gate | `FlowCanvasChecks` | 本轮按任务口径未跑（任务只要求插件构建 + `--plugin-views`）；建议 reviewer 在合入前补跑 |

> 冒烟的 `ComboBox 绑定 (EnumOptions/)` 来自 `LinkableValueEditor` 内部的下拉；本视图的「投射视图窗口」`ComboBox` 在折叠的 `Expander` 里，初始态不在可视树，因此批量冒烟不会实例化它。它的绑定 `SelectedItem="{Binding DisplayViewIndex}"` 在 XAML 静态核对中确认，真机展开「高级参数」后使用。

## 8. 遗留问题

1. **真机验收未做**：需要打开配置窗口 → 链接上游 Image → 执行 → 右键画矩形/圆形/椭圆 → 试算 → 确认 → 重开，确认值与 ROI 都回环。`ImageEdit` 是 HALCON `HwndHost`，批量 PNG 不捕获画布内容。
2. **设计基线待用户确认**：`docs/UI设计基线/color-region/README.md` 的「确认日期/人」仍为待确认；确认后基线只读，后续调整升 v2。
3. **`ColorThresholdEditor` 视觉节奏不一致**：共享控件仍是 22px 输入框与自身灰阶（`#555555`），本轮按硬约束未改；若要做全令牌化/36px 节奏，需要单独改共享控件并回归所有颜色算子。
4. **`ExpectedColors` 无 INPC**：插件属性是普通自动属性，「把主色填成期望颜色」后由视图 `UpdateTarget()` 刷新显示；如果后续给插件属性补 `OnPropertyChanged`，可删掉这条 UI 侧补丁。
5. **无 `StatusLevel`**：结果块的三态颜色是视图按 `ResultSummary` 文案推断的（`试算失败` → Error；`还没有图像 / 尚未配置期望颜色 / 无法判定` → Warning），不是插件状态枚举；如果以后新增真正的状态属性，应改绑真实状态。
6. **640×560 最小档**：宿主 MinHeight 已统一 560，左栏会滚动；需真机复核「高级参数」展开后的滚动与画布高度。
7. **未纳入项**：载入本地图、放大/缩小、加载骨架、尺寸/文件大小、阈值编辑器换肤（详见基线 README §10）。
8. **`ImageEdit` 共享右键菜单**：控件自带的「打开图像」等菜单项仍在（共享控件既有行为），它只影响画布显示，不写插件的 `DisplayImage` / `RoiParams`；本算子图像来源仍以上游 `Image` 端口为准，本轮不改共享控件。

## 9. 复跑命令

```text
dotnet build Plugins\Plugin.ColorRegion\Plugin.ColorRegion.csproj -v m
dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.ColorRegion
```

> `UIThemeSmokeTest` 退出码恒为 1（既有 GridView 红灯，R46），以日志 PASS/FAIL 行为准；本轮日志为 11 条 PASS、0 条 FAIL。

## 10. P1 修复（reviewer 反馈，2026-10-20）

### 问题

`RegionColorPlugin.RoiShape` 是 `[StepConfig]` 普通自动属性，`WriteBackRoi()` 写入后只通知 `RoiSummary`，没有 `OnPropertyChanged(nameof(RoiShape))`；视图的形状 chip 直接绑 `RoiShape`，所以画圆/椭圆后 chip 仍显示「矩形」，与 `RoiSummary` 的「圆形 / 椭圆」矛盾。`ClearRoi()` 也没有重置 `RoiShape`。

### 修法

1. **`WriteBackRoi()`**：新增 `SetRoiShape(string shape)` 辅助方法——值变化时写 `RoiShape` 并 `OnPropertyChanged(nameof(RoiShape))`；`info == null` 时归一为 `RoiShapeNames.Rectangle`，有 ROI 时按 `ShapeType` 写入 `Circle`/`Ellipse`/`Rectangle`。
2. **`ClearRoi()`**：集合非空时 `CanvasRois.Clear()` 走 `OnCanvasRoisChanged → WriteBackRoi`；集合为空时直接 `WriteBackRoi()`，保证 `RoiShape=Rectangle`、`RoiParams=[]`、`RoiSummary` 刷新，且只通知一次。
3. **`Initialize()`**：`hasRoi == false` 分支同样 `SetRoiShape(RoiShapeNames.Rectangle)`，避免旧配置里「有形状名、没有有效参数」时 chip 与摘要不一致。
4. 未改 ROI/颜色业务逻辑、端口名、命令名、持久化；未改共享文件。

### 实际输出（独立探针）

```text
initial RoiShape=Rectangle RoiParams=0
after circle add RoiShape=Circle RoiParams=5 changed=RoiShape,RoiSummary
after clear RoiShape=Rectangle RoiParams=0 changed=RoiShape,RoiSummary
```

### 复验

- `dotnet build Plugins\Plugin.ColorRegion\Plugin.ColorRegion.csproj -v m` → **0 错误 / 0 警告**；`Modules\Plugin.ColorRegion.dll` → 2026-10-20 02:58:16，102,912 字节。
- `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.ColorRegion` → **11 条 PASS / 0 条 FAIL**（与修复前一致）。
- 设计稿同步：`design.html` 的「清空采样区」现在会隐藏 ROI 覆盖层并把 chip 归一到「矩形」；证据 `docs/UI设计基线/color-region/evidence/design-v1-cleared-960.png` 已补。

## 11. 遗留项修复（2026-10-20，CRG2 / CRG4）

- **CRG2**：`ApplyPreviewAsExpected()` 写入 `ExpectedColors` 后补 `OnPropertyChanged(nameof(ExpectedColors))`；视图的 `ExpectedColorsBox.UpdateTarget()` 保留为无害兜底。只加通知，不改颜色算法、属性类型与持久化。
- **CRG4**：状态栏右侧 `RoiSummary` 的 `TextBlock` 命名 `RoiSummaryText`；`RegionColorView.xaml.cs` 在 `SizeChanged` 中按 `ActualWidth < 620` 折叠（窄档把宽度让给 `Hint`），不改 `RoiSummary` 本身与任何业务状态。README §5/§8 已同步实际口径。
- **验证**：`dotnet build Plugins\Plugin.ColorRegion\Plugin.ColorRegion.csproj -v m` → **0 错误 / 0 警告**；`dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.ColorRegion` → **11 条 PASS / 0 条 FAIL**。
