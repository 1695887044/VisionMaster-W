# 2026-10-20 Plugin.BeadInspect 自定义视图 Web 化

## 1. 范围

| 项 | 内容 |
| --- | --- |
| 设计基线 | `docs/UI设计基线/bead-inspect/README.md` v1 + `design.html` |
| 覆盖 | `Plugins\Plugin.BeadInspect\BeadInspectView.xaml`、`BeadInspectView.xaml.cs` |
| 附带 | `BeadInspectPlugin.cs` 一处防御性判空（消除既有 CS8604/CS8602 警告，见 §5） |
| 不在范围 | 业务逻辑、端口名、命令名、持久化字段、`BeadInspectPluginConfigView.cs` 的配方/学习/提取逻辑、共享控件/共享字典/宿主外壳 |
| 设计基线 | 自包含、无外部依赖、可点击；含默认 / 空态 / 错误 / 警告 / 拾取四点五态与 960/640 两档 |

本插件按实施计划为**批次 3 大型多面板**：2 块 HALCON 画布 + 1 个 DataGrid（4 列；改造前 XAML 中 `DataGrid*` 标记共 18 处）+ 5 个 `LinkableValueEditor`。
先盘点再动手，耦合矩阵见 §3。

## 2. 功能契约保留（逐项）

| # | 契约 | 状态 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 保留；视图构造未改，`GetConfigView` 返回 `new BeadInspectView { DataContext = this }` |
| C2 | 5 个 `LinkableValueEditor`：`SrcImage` / `RecipeName` / `AlignedImageIn` / `RoiRegion` / `Transform` | 保留；`Port` / `StepData` 原绑定；静态检查 5/5、运行期端口面 5/5 |
| C3 | `ImageEdit`：`HImage=DisplayImage`、`Annotations=PathAnnotations`；`ImageReadOnly`：`HImage=PreviewImage` | 保留；控件常驻，未用 `Visibility=Collapsed` 显隐已设图控件 |
| C4 | 点列表格：`ItemsSource=PointRows`、`Index/Row/Col` 列绑定、`CellEditEnding`、行删除 | 保留；行高 28、单元格内边距 8,2 |
| C5 | 配方参数：`EditingEntry.TargetWidth` / `WidthTolerance` / `PositionTolerance` / `Polarity`；目标矩形 `RectTargetR1..C2` | 保留；全部 `[StepConfig]` / 配方字段未改名 |
| C6 | 运行参数：`AlignMode` / `UnitOutput` / `MinErrorLength` / `DisplayViewIndex`；搜索范围 6 个字段 | 保留；`DisplayViewIndex` 仍为 int，0~9 |
| C7 | 命令：学习 / 自动提取 / 撤销 / 清空点列 / 拾取矫正四点 / 清除矫正 / 载入 / 适应图片 / 新增 / 删除 / 设为默认 / 应用目标矩形 | 全部保留；改为视图级 `UiCommand` 转发到插件现有公开方法 |
| C8 | `StatusMessage` / `StatusLevel` 三档 + `ActionBanner` / `ActionBannerLevel` + `BaseImageHint` / `BaseImageLevel` | 原样消费；未加第四档 |
| C9 | 画布鼠标：左键加点/拖点、右键删点、四点模式、Ctrl+Z | 保留；`GrabHSmart` 改为从主画布 `Canvas` 子树查找，避免误挂到结果预览的 `ImageReadOnly` |
| C10 | `RefImagePicker` 测试替身 | 保留；`LoadRef` 仍走同一文件选择与写回逻辑 |

## 3. 耦合矩阵（先盘点后动手）

| 区域/元素 | 当前绑定/契约 | 功能依赖 | 可重构级别 / 处理 |
| --- | --- | --- | --- |
| 根布局 | 无绑定 | 纯布局 | 自由；两栏固定（左 330px + 右自适应），取消 6px `GridSplitter` |
| ⓪ 输入绑定 | 5×`LinkableValueEditor`：`Port`/`StepData` | 端口持久化、类型转换、`SetLink/RemoveLink`、变量绑定弹窗 | **只能包裹**；原控件原绑定，只重排卡片与帮助文字 |
| ① 配方库 | `ListBox ItemsSource=RecipeRows`、`SelectedItem=EditingRow`；`EditingEntry.RefImagePath`、`RefNoBeadImagePath`、`ExtractImagePath` | 配方选型、参考图/自动提取输入持久化 | 只改外观；全部绑定与事件保留 |
| ① 配方操作 | 新增 / 删除 / 设为默认 / 载入 | 配方库增删、默认配方、参考图载入 | 只能转发；视图级 `UiCommand` |
| ② 点列表格 | `DataGrid ItemsSource=PointRows`；`Index/Row/Col` 列；`CellEditEnding`；行删除 | 点列缓存、画布双向同步、撤销、学习指纹 | **只能包裹**；保留列绑定与事件，只收窄内边距/行高 |
| ③ 配方参数 | `EditingEntry.TargetWidth/WidthTolerance/PositionTolerance/Polarity`；`RectTargetR1..C2` | 配方持久化、模型指纹、HALCON 参数 | **只能包裹**；绑定逐字保留 |
| ④ 运行参数 | `AlignMode` / `UnitOutput` / `MinErrorLength` / `DisplayViewIndex` | `[StepConfig]` 持久化、运行期对齐/单位/显示窗口 | **只能包裹**；枚举/字符串/int 类型不变 |
| ⑤ 搜索范围 | `FindAngleStartDeg` / `FindAngleExtentDeg` / `FindScaleRMin` / `FindScaleRMax` / `FindScaleCMin` / `FindScaleCMax` | 平面匹配 find 参数 | **只能包裹**；6 个绑定逐字保留 |
| 工具栏 | 学习 / 自动提取 / 撤销 / 清空点列 / 拾取矫正四点 / 清除矫正 | 现有功能入口 | 只改外观与命令转发；`IsEnabled` 仍绑 `HasEnoughPoints` / `HasAnyPoints` |
| 画布标题 | `PointCountText`、`QuadPicking`、`IsCanvasEmpty` | 点数提示、四点模式文案、空态 | 只改外观；绑定保留 |
| 主画布 | `h:ImageEdit`：`HImage=DisplayImage`、`Annotations=PathAnnotations` | HALCON 显示/缩放/右键菜单/拾取/叠加层 | **只能保留**；控件常驻，事件从 `Canvas` 子树取 |
| 结果预览 | `h:ImageReadOnly`：`HImage=PreviewImage` | HALCON 只读显示、学习叠加/试运行标注 | **只能保留**；控件常驻，无图时只盖 WPF 覆盖层 |
| 结论横幅 | `ActionBanner` / `HasActionBanner` / `ActionBannerLevel` | 学习/提取/矫正的即时结论 | 只改外观；三档颜色复用 |
| 状态条 | `StatusMessage` / `StatusLevel`、`BaseImageHint` / `BaseImageLevel` | 三档状态 + 底图来源提示 | 只改外观；两行常驻 |
| 画布鼠标 | `HSmartWindowControlWPF.HMouseDown/Move/Up`、`Canvas.ContextMenu=null` | 加点/删点/拖点/四点拾取 | **只能保留**；`GrabHSmart` 查找根改为 `Canvas` |
| Ctrl+Z | `PreviewKeyDown` → `UndoLastPick` | 撤销 | 保留 |

## 4. 改动点

1. **整页 XAML 重写**：左 330px 设置轨（⓪ 输入绑定 → ① 配方库 → ② 路径点列 → ③ 配方参数 → ④ 运行参数 → ⑤ 平面匹配搜索范围 → 结果预览 → 怎么用）+ 右侧画布工作区（工具栏 + 结论横幅 + 画布标题 + 操作提示 + `ImageEdit` hero）+ 两行状态条。
2. **取消 `GridSplitter`**：左栏固定 330px，画布获得稳定余量；不再有 6px 拖拽条。
3. **结果预览常驻**：`ImageReadOnly` 不再随 `PreviewImage == null` 折叠；无图时只显示 WPF 覆盖层「暂无结果图」，控件本身始终在可视树里，避免折叠/恢复后不重绘。
4. **命令改为视图级 `ICommand`**：新增 `LearnCommand` / `AutoExtractCommand` / `UndoCommand` / `ClearPointsCommand` / `ToggleQuadPickCommand` / `ClearQuadCommand` / `FitCommand` / `AddEntryCommand` / `DeleteEntryCommand` / `SetDefaultCommand` / `LoadRefCommand` / `ApplyRectCommand`；全部只调用插件现有公开方法，业务逻辑零改动。
5. **`GrabHSmart` 查找根改为 `Canvas`**：结果预览的 `ImageReadOnly` 也是 `HSmartWindowControlWPF` 宿主；从视图根查找可能先命中预览，把点拾取事件挂错控件。改为从主画布 `ImageEdit` 子树查找。
6. **极性显示转换器**：`PolarityNameConverter` 只把下拉文字 `dark/light` 显示为「暗胶（dark）/ 亮胶（light）」；`SelectedItem` 仍是字符串 `dark/light`，持久化值不变。
7. **显示窗口下拉**：`ItemTemplate` 显示「不显示 / 图像窗口 N」，`SelectedItem` 仍绑 int 的 `DisplayViewIndex`。
8. **令牌化**：视图 XAML 零十六进制颜色；全部使用 `Dialog*` / `Plugin*` 公共令牌；本插件**未新增**公共令牌。
9. **`BeadInspectPlugin.cs` 一处防御性判空**：`EnsureBeadModel` 在 `TryBuildContour` 成功后判 `contour == null`，消除既有 CS8604/CS8602（编译器无法从 out 参数推断非空）。不改变正常路径行为。
10. **设计基线**：新建 `docs/UI设计基线/bead-inspect/design.html` + `README.md`；`design.html` 用浏览器复核 960/640 两档与默认/空态/错误/警告/拾取四点五态，修复了两处 SVG `hidden` 属性不反射导致的状态显示问题（只影响设计稿）。

## 5. 与 ImageAcquisition V2 的差异

| 维度 | ImageAcquisition V2 | BeadInspect v1 |
| --- | --- | --- |
| 画布 | 1 个 `ImageReadOnly` 预览工作区 | 2 个 HALCON 画布：`ImageEdit` 主画布 + `ImageReadOnly` 结果预览 |
| 预览宿主 | `ContentControl` + `DataTemplate`（有图才创建） | 按用户硬约束：`ImageReadOnly` **常驻**，无图时只盖 WPF 覆盖层 |
| 主操作 | 采集配置表单 | 画布逐点拾取（左键加点/右键删点/拖动/Ctrl+Z/四点矫正） |
| 表格 | 无 | 1 个 `DataGrid`（# / Row / Col / 删） |
| 模式选择 | 2×2 采集模式卡片 | 对齐模式下拉（固定相机/匹配位姿/平面可变形） |
| 结论反馈 | 预览状态栏 | 工具栏下方常驻 `ActionBanner`（成功/警告/错误三档） |
| 左栏宽度 | 356px | 330px（共享 `PluginParamsColumnWidth`） |
| 控件高度 | 36~38px | 输入框 32px、工具栏按钮 28~30px（左栏 330px + 多字段密度；见 README 已知偏差） |
| 预览工具条 | 适应窗口 / 1:1 / 目录刷新 | 只有画布「适应图片」；无放大/缩小/打开文件夹 |
| 设计基线版本 | v2（已复刻） | v1（本批新建，待用户确认） |

## 6. 验证

| 项 | 命令 | 结果 |
| --- | --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.BeadInspect\Plugin.BeadInspect.csproj` | **0 错误 / 0 警告** |
| Modules 产物 | `Modules\Plugin.BeadInspect.dll` | 已刷新：**197,120 bytes / 2026-10-20 03:34:01**（与 `bin\Debug` 一致） |
| UI 冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.BeadInspect` | **11/11 PASS**；元素 592；5 个 `LinkableValueEditor.Port`；11 个按钮有 `Command`；`OnConfirm` InputValues=25；XAML hex=0；PNG 已生成 |
| 渲染证据 | 批量冒烟输出 | `docs/UI设计基线/_batch-evidence/Plugin.BeadInspect-BeadInspectPlugin.png` |
| 设计证据 | 浏览器截图 | `docs/UI设计基线/bead-inspect/evidence/design-default.png`、`design-narrow-640.png`、`design-empty.png`、`design-error.png`、`design-quad.png` |
| 常规 gate | `dotnet run --project FlowCanvasChecks --no-build` | **2691 通过 / 0 失败**；`[BeadInspect]` 段全绿；`[UI]` 22 个 `Plugin*` 键齐全 / h: 无内联 Style / 无硬编码颜色；`[UI·绑定]` 5 个端口、链接回显/解绑/绑定/OnConfirm/演示方案回显全绿；16d 6 个搜索参数；17a-17e 横幅与学习反馈全绿 |
| 行为探针（P1 载入回环） | `dotnet run --project _BeadViewProbe --no-build -- probe-fix __none__` | **全部通过**：D1-D5 + P1①-⑤；`pickCalls=1/2/3`；路径写回 / 取消 / 有效路径不弹框 / 失效路径弹框 / ToolTip 全 PASS |
| 设计稿复核 | 本地 HTTP + 浏览器 | 960 默认态、640 窄档、空态、错误态、拾取四点态均已截图核对；状态切换可点击；无外部依赖 |

> `UIThemeSmokeTest` 退出码恒为 1（既有 GridView 红灯，R46），本次以日志行判定：`=== 插件自定义视图批量冒烟：全部通过 ===`。
> `FlowCanvasChecks` 汇总行：`通过: 2691  失败: 0`。

### 验证工具修复（2026-10-20 追加）

**问题（reviewer P1）**：`_BeadViewProbe\Program.cs` 原来用
`loadBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))` 驱动「载入」。Web 化后「载入」改为
`Command="{Binding LoadRefCommand}"`，`RaiseEvent` 不会执行 `Command`，`pickCalls` / `RefImagePath` 等
P1 断言会静默失效（测试假绿）。

**修法（只改验证工具，不改生产代码）**：
- 新增 `ClickLoadButton(Button loadBtn)`：直接执行
  `loadBtn.Command.Execute(loadBtn.CommandParameter)`；**Command 为空直接抛错**，不回退
  `RaiseEvent(Button.ClickEvent)`——回退会把"Command 绑定失效"伪装成通过，正是本次要消除的假绿。
- 四处调用点全部改为 `ClickLoadButton(loadBtn)`；P1-a 断言名由「Click 接线的真实入口」改为
  「Command 接线的真实入口」；断言内容与期望值未改。

**实际输出（节选）**：

```text
=== P1「载入」文件选择自检 ===
  [PASS] P1-a 视图里能定位「载入」按钮（Command 接线的真实入口）  (实际宽=46)
     [PICK] 替身被调用（第 1 次，传入路径 = (空)）→ 返回 D:\C#\VM\Image\bead\adhesive_bead_ref.png
  [PASS] P1① 空路径点「载入」→ 走文件选择分支（1 次）  (pickCalls=1)
  [PASS] P1① 选中路径写回 EditingEntry.RefImagePath（路径框随之刷新）  (D:\C#\VM\Image\bead\adhesive_bead_ref.png)
  [PASS] P1① 参考图真的上了画布（底图 = 参考图）  (底图：参考图（原始）——未设矫正四点（或非「平面可变形」模式），点列存原始参考图坐标)
     [PICK] 替身被调用（第 2 次，传入路径 = (空)）→ 返回 (取消)
  [PASS] P1② 取消选文件 → 选择分支确实被走到（2 次）  (pickCalls=2)
  [PASS] P1② 取消 → 路径仍为空（不写回）
  [PASS] P1② 取消 → 状态栏仍是「还没有参考图路径」那条（没被当成载入成功）
  [PASS] P1③ 路径有效点「载入」→ 不弹框（pickCalls 仍为 2）  (pickCalls=2)
  [PASS] P1③ 路径有效 → 直接载入画布（底图 = 参考图）
     [PICK] 替身被调用（第 3 次，传入路径 = D:\C#\VM\Image\bead\not_exist_2026.png）→ 返回 (取消)
  [PASS] P1④ 路径失效点「载入」→ 走文件选择分支（3 次）  (pickCalls=3)
  [PASS] P1⑤ 「载入」的 ToolTip 说明了弹框行为（悬停可自证）
=== PROBE: 全部通过 ===
```

> 探针工程 `Nullable=disable`，构建有 15 条既有 CS8632 nullable 注释警告、0 错误；本次未新增警告。
> 生产插件构建仍为 **0 错误 / 0 警告**。

## 7. 遗留问题

1. **真实 HALCON 预览像素未验证**：`ImageEdit` / `ImageReadOnly` 是 `HwndHost`，`RenderTargetBitmap` 不捕获其内容；证据 PNG 只证明 WPF 布局与空态，真实图像/叠加层需真机窗口验收。
2. **640×560 最小档**：宿主 MinHeight 已统一 560；本稿主要验证 640×680，640×560 下左栏滚动与画布高度需真机复核。
3. **未暴露的 `[StepConfig]` 入口**：`ExtractTargetPoints`、`OutputAlignedImage`、`PixelSizeMm`、`MinScore`、`PlanarNumLevels` 当前视图没有绑定，属于既有缺口；本版按"不新增功能"未加，已记录在设计基线 D5。
4. **无插件专属 `--bead-inspect` 渲染模式**：按本轮硬约束未修改 `UIThemeSmokeTest` 的 `Program.cs`/`PluginViewSmoke.cs`；使用通用 `--plugin-views` + `FlowCanvasChecks` 的 `[BeadInspect]`/`[UI]`/`[UI·绑定]` 断言覆盖，P1 载入回环由 `_BeadViewProbe` 行为探针覆盖。后续如需 4~5 张专属渲染 PNG，需单独扩展 UI 冒烟工程。
5. **取消 `GridSplitter`**：旧视图可手动拖宽画布；本版固定 330px。若现场需要，可后续恢复一个样式化的拖拽条（纯 UI 迭代）。
6. **设计基线待用户确认**：v1 已落盘，确认后冻结；改动升 v2 并同步 README/本记录。
7. **设计基线索引未改**：按本轮硬约束「不修改 `docs/UI设计基线/README.md`」，bead-inspect 的索引条目由主会话统一补登。

## 8. 复跑命令

```text
dotnet build Plugins\Plugin.BeadInspect\Plugin.BeadInspect.csproj
dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.BeadInspect
dotnet run --project FlowCanvasChecks --no-build
dotnet run --project _BeadViewProbe --no-build -- probe-fix __none__
```

> 若 `Modules\Plugin.BeadInspect.dll` 被 VisionMaster/VS 锁定：先关闭宿主/VS 再重试，不要杀进程；
> 构建 0 错误 ≠ 产物已投递（红线③），以 `Modules` DLL 时间戳与大小为准。

## 9. 遗留问题核销（2026-10-20，BI1 / BI3）

| 项 | 改动 |
| --- | --- |
| BI3 数字等宽 | `BeadInspectView.xaml` 的 `BeadInput` 与 `BeadPointEditBox` 两个样式补 `FontFamily="{StaticResource PluginMonoFontFamily}"` + `Typography.NumeralAlignment="Tabular"`；前者覆盖配方参数 / 运行参数 / 搜索范围 / 路径等全部单行输入框，后者覆盖点列表格的 `Row`/`Col` 编辑框。未改 `Text` 绑定、`NumberValidationRule`、`DataGrid` 列定义或任何数值语义 |
| BI1 警告态证据 | 用 Edge headless 渲染 `docs/UI设计基线/bead-inspect/design.html` 的「警告」场景，生成 `evidence/design-warn.png`（1440×1000）。`README.md` 设计证据清单已补该文件；不再是"warning 只能点、没有截图"的状态 |

验证（本次修复批）：

| 项 | 结果 |
| --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.BeadInspect\Plugin.BeadInspect.csproj -v m`：**0 错误 / 0 警告** |
| 插件视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.BeadInspect`：**11/11 PASS**（elements=592；5 个 `LinkableValueEditor.Port`；11 个按钮命令；`InputValues=25`；hex=0） |
| Modules 产物 | `Modules\Plugin.BeadInspect.dll`：2026-10-20 **07:44:44 / 196,096 bytes** |
