# SCADA 第二批四批次（SubPage/声音 → 棒图/仪表/动画 → 图形/GIF → 位表/监视）

日期：2026-10-25
范围：用户拍板的第二批，按四个批次串行实施、每批自评审后再进下一批：
A. SubPage 完善 + 播放声音事件；B. 棒图增强 + Gauge Plus + 比例/对角移动；C. 多边形/贝塞尔/动态画线/GIF；D. 位表 / 变量监视 / I/O 监视。
**报警生命周期事件本批不做；Web 运行态仍不同步（渲染器缺口统一登记在 [WR] 例外清单，最后一批清零）。**

## 一、当前状态

- 四个批次全部实现并各自验收，收尾全量 `tools\run_gates.ps1` **13 步全绿**（`ScadaChecks 2853/0`、`FlowCanvasChecks 3657/0`、`UIThemeSmokeTest`/`HelpProbe` exit 0、四项静态审计 clean）。
- 内置图元从 **38 → 45**：新增 `Hmi.Polygon` / `Hmi.Bezier` / `Hmi.DynamicLine` / `Hmi.Gif` / `Hmi.BitTable` / `Hmi.IoMonitor` / `Hmi.VariableMonitor`。
- 动作枚举 26 → 27（`PlaySound`）；动画枚举 5 → 7（`DiagonalMove` / `ProportionalMove`）。
- **未做（有意）**：旋钮 Knob（用户口径"优先圆形进度条、可做 Gauge 样式变体"，本批只做了 Gauge 三样式）；GIF/多边形等的 Web 渲染器；位表的"点格子写位"（写位仍走位按钮，避免两套写入口）；"变量监视"做成 16 位位表形态而非多变量列表（见第四节口径说明）。

## 二、已做

### 批次 A：SubPage P1b + 播放声音

| 文件 | 改动 |
| --- | --- |
| `Scada\Models\IScadaSubPageHost.cs`（新） | `Enter/Exit`：运行态把"当前可见子页"登记给会话，供事件路由放行 |
| `Scada\Models\ScadaRuntime.cs` | 实现 `IScadaSubPageHost`；`_activeSubPages`；`RaiseElementEvent` 允许活跃子页内的事件（弹窗模态仍拦后台）；`Stop()` 清空登记 |
| `Scada.Controls\Runtime\ScadaRuntimeContext.cs` | 可选参数 `subPages` + `SubPages` 属性 |
| `VisionMaster\Services\ScadaRuntimeHost.cs` | 装配时把会话作为子页宿主传进上下文 |
| `Scada.Controls\Elements\SubPageElement.cs` | `PageSource`（Fixed/FollowVariable）、`PageName`（可绑）、`Interactive`；只读上下文 `ToReadOnlyContext`；交互模式登记/退出子页；`Loaded`/`Unloaded` 成对，`DetachEmbedded` 停 binder 并清空；KeepLast 冻结最后一页 |
| `Scada.Controls\Elements\ScadaElementBase.cs` | 加 `PageNameResolver` / `OnPageNameResolverChanged` / `ResolvePageByName`；`ImageAssetResolver` 改带变更钩子 |
| `Scada.Controls\Canvas\ScadaCanvas.cs` / `ScadaEditorViewModel` / 两个宿主视图 | 转发 `PageNameResolver`（按名字切页） |
| `Scada\Models\ScadaActionType.cs` / `ScadaAction.cs` | `PlaySound = 27`（显示名"播放声音"）；`SoundKind`（Info/Warning/Critical）+ Detail/Describe |
| `VisionMaster\Services\ScadaActionDispatcher.cs` | 可选 `IScadaSoundPlayer`；`ExecutePlaySound`：档位映射严重度，无播放器/不可用/未知档位/异常四档回执 |
| `VisionMaster\App.xaml.cs` | 注册 `SystemSoundPlayer` 单例并注入 dispatcher |
| `VisionMaster\ViewModels\ScadaEventEditorViewModel.cs` / `ScadaEventEditor.xaml` | 动作排序 31 + "声音档位"下拉 |
| `ScadaChecks\SoundActionChecks.cs`（新） | [SND1]~[SND8]：枚举/文案/三档映射/未知档位回落/无播放器/不可用/异常 |
| `ScadaChecks\SubPageChecks.cs` | [SP14] 按名字跟变量、[SP15] 交互子页写变量并登记宿主、[SP16] 卸载退出登记 |

### 批次 B：棒图增强 / Gauge Plus / 两个新动画

| 文件 | 改动 |
| --- | --- |
| `Scada.Controls\Elements\ProgressBarElement.cs` | `ShowTicks`/`TickCount`(0~50)/`TickColor`、`ShowLimits`/`LowLimit`/`HighLimit`/`LowColor`/`HighColor`/`LimitColor`、只读 `DisplayFill`（越限换色）；刻度线与限制线画进两个 Canvas 部件 |
| `Scada.Controls\Themes\ProgressBarElement.xaml` | 填充改绑 `DisplayFill`；新增 `TickLayer` / `LimitLayer` |
| `Scada.Controls\Elements\GaugeElement.cs` | `GaugeStyle`（`[ScadaProperty(Key="Style")]`，避开 `Control.Style`）：Classic/Ring/Plus；`ProgressColor` + 只读 `ProgressGeometry`（从起始角画到当前比例） |
| `Scada.Controls\Themes\GaugeElement.xaml` | 新增进度弧 `Progress`；Ring/Plus 收指针、Classic 收进度弧 |
| `Scada\Models\ScadaAnimationType.cs` / `ScadaAnimation.cs` | `DiagonalMove = 6` / `ProportionalMove = 7`；`TryEvaluateTargetPositionXY` / `TryEvaluateTargetSize`；Detail 文案 |
| `Scada.Controls\Elements\ScadaElementBase.cs` | 动画两分支：对角移动写 `Canvas.Left/Top`；比例移动按中心缩放写 `Width/Height + Left/Top` |
| `ScadaChecks\BatchBVisualChecks.cs`（新） | [B2-PB1~PB4]、[B2-GA1~GA2]、[B2-AN1~AN2] |

### 批次 C：多边形 / 贝塞尔 / 动态画线 / GIF

| 文件 | 改动 |
| --- | --- |
| `Scada.Controls\Elements\ShapeElements.cs`（新） | `ShapePointParser`（0~1 相对坐标点串）、`ShapeGeometry.Size`（NaN 兜底）、`PolygonElement` / `BezierElement` / `DynamicLineElement`；几何只写一个只读 DP，模板 `TemplateBinding` 消费 |
| `Scada.Controls\Themes\{Polygon,Bezier,DynamicLine}Element.xaml`（新） | 一个 `Path` + `Grid Background=Transparent`（细线也要能点中） |
| `Scada.Controls\Elements\GifElement.cs`（新） | `GifBitmapDecoder` + `DispatcherTimer` 逐帧播放；帧延时读 `/grctlext/Delay`（0 兜底 20ms）；`Loaded/Unloaded` 起停；`MaxFrames=256`；`AdvanceFrame()`；Refresh 命中缓存不重解码 |
| `Scada.Controls\Themes\GifElement.xaml`（新） | 底框 + `ImageHost` + 占位（与本地图片同版式） |
| `Scada.Controls\Elements\ScadaElementBase.cs` | 新增 `ImageBytesResolver` / `OnImageBytesResolverChanged` / `ResolveImageBytes`（GIF 要原始字节，位图通道只剩首帧） |
| `Scada.Controls\Canvas\ScadaCanvas.cs` / `SubPageElement.cs` | `ImageBytesResolver` DP + 转发（含嵌套画布） |
| `VisionMaster\Services\SolutionAssetService.cs` | `TryReadBytes`（与 `TryLoad` 共用路径校验） |
| `VisionMaster\Views\ScadaRuntimeWindow.xaml.cs` / `ScadaEditorView.xaml.cs` / `ScadaEditorViewModel.cs` | 注入字节解析器 |
| `Scada.Controls\Descriptors\BuiltInElements.cs` | 注册四个图元 |
| `ScadaChecks\BatchCVisualChecks.cs`（新） | [B3-SH1~SH4]、[B3-BZ1~BZ2]、[B3-DL1~DL3]、[B3-GF1~GF6]（GIF 三帧用 `GifBitmapEncoder` 现场造，不依赖外部资产） |

### 批次 D：位表 / I/O 监视 / 变量监视

| 文件 | 改动 |
| --- | --- |
| `Scada.Controls\Elements\BitTableElement.cs`（新） | `Value`（可绑）+ `BitCount`(1~32) / `StartBit` / `Columns` / `ShowIndex` / `ShowValue` / `ShowLabels` / `Labels` / `OnColor` / `OffColor` / `ValueForeground`；`GetBit` / `CellCount`；结构变更 `Rebuild`、值变更只 `UpdateValues`；外观走 `FindAncestor` 绑定 |
| `Scada.Controls\Themes\BitTableElement.xaml`（新） | 外框 + 标题 + `UniformGrid x:Name="PART_Cells"` |
| `Scada.Controls\Descriptors\BuiltInElements.cs` | `Hmi.BitTable`；`Hmi.IoMonitor`（8 位 + I0.x 标签 + 标题）、`Hmi.VariableMonitor`（16 位 + 标题）两个同控件预设（`ScadaPropertyCatalog.WithDefaults`） |
| `ScadaChecks\BatchDChecks.cs`（新） | [B4-BT1~BT8]、[B4-IO1]、[B4-VM1]：位序/负值补码/起始位/16 位/置位色引用相等/值变不重建/运行态写入/空标题收起/两个预设 |

### 跨批次：Web 渲染器例外清单

`ScadaChecks\WebRendererCoverageChecks.cs` 的 `KnownMissingWebRenderers` 从 2 项扩到 9 项（+ 批次 C 四件 + 批次 D 三件），`[WR3]` 上限同步 2 → 9，并在清单里逐条写明"Web 统一批次补、补完删掉、上限收回"。
**这是本批唯一的"闸门放宽"**，理由：用户明确"先不同步 Web，最后统一"；不放宽则每加一个桌面图元都要先写 Web 渲染器，与拍板顺序相反。

## 三、未做

- **旋钮 Knob**：用户口径是"优先圆形进度条、可做 Gauge 样式变体"，本批已交付 Ring/Plus 两样式；Knob 需要拖拽交互与角度→值换算，留待"仪表交互"批次。
- **Web 渲染器/快照**（9 项）：统一批次。
- **位表写位**：监视类只读；写位仍走位按钮（权限判定 + 写回通道 + 回执只有一份）。
- **多边形顶点可视化编辑**：当前是点串输入；画布级顶点手柄属于画布交互批次。
- **GIF 在 Web/帮助手册**：Web 同上；手册按 R41 需在插件工程内，本批是内置图元，`HelpProbe` 覆盖的是插件手册，不受影响。
- **Web 设计基线**：本批没有独立的设计基线页（用户直接下的是功能清单）。新图元的视觉一律沿用既有 SCADA 令牌（`ScadaTokens.xaml`）与既有图元的版式语言，**没有引入新的视觉语言**；若要与 Web 端逐像素对齐，按 R127 先补基线再调样式，不在本批静默降级。

## 四、下一步

1. **Web 统一批次**：补齐 9 个渲染器并把 `KnownMissingWebRenderers` 清空、`[WR3]` 上限收回 0。
2. **旋钮 Knob**：若确认要做，按 Gauge 的样式枚举扩展（`GaugeStyle=Knob`）而不是新 TypeKey，避免工具箱再长一个近义图元。
3. **位表写位**：如现场确需，应复用 `BitButton` 的写路径与权限判定，不在 BitTable 里另开一条。
4. **"变量监视"多变量形态**：若现场要的是"多变量列表"而不是"一个字的 16 位"，应作为新图元（列定义 + `IScadaValueSource` 按名取数，参考 DataGrid），不要塞进位表。

## 五、踩过的坑

| 坑 | 现象 | 处置 |
| --- | --- | --- |
| 几何键名不是 `Width` | `SetProperty("Width","100")` 静默无效，几何仍按默认 140×120 算，断言红在"起点 70,0" | 几何键是保留键 `$Width`/`$Height`；但**断言里更该直接写 `model.Width = 100`**（强类型字段），`SetProperty` 对几何键不是主路径 |
| 模型改属性不会自动刷新控件 | 单独 `model.SetProperty("Points", …)` 后几何不变（无画布订阅） | 断言里跟一次 `control.Refresh()`；真机由画布订阅负责 |
| `Path` 二义 | `using System.IO;` + `using System.Windows.Shapes;` → CS0104 | `using Path = System.Windows.Shapes.Path;` |
| 只读 DP 与模板 | 几何写进代码又写进模板会打架 | 只写只读 DP，模板 `TemplateBinding` 消费；断言额外钉一条"模板里 Data 非空" |
| GIF 每次 Refresh 重解码 | 拖动图元会让动画闪回首帧，且大图每次全解 | 路径 + 解析器未变时走缓存分支，只重贴当前帧；[B3-GF6] 钉住"帧序不倒退" |
| GIF 内存 | 每帧像素常驻 | `MaxFrames=256` + `BitmapCacheOption.OnLoad` + `Freeze`（流可立刻关，不占文件句柄） |
| 位表值变重建 | 每拍重建 32 个 UI 元素 | 结构变更 `Rebuild`、值变更只改文字/底色；[B4-BT3] 用引用相等钉住"没重建" |
| 十六进制字面量手算错 | `0x1234` 的高字节是 `0x12`，bit9 为 1 而不是 bit8；`0x0100d` 是 `0x100D` | 断言明细里带 `start/count/value`，一眼看出是期望写错而不是实现错 |
| 并发会话改同一仓库 | `VisionMaster\Views\FlowCanvasSurface.cs` 处于半改状态时 `dotnet build` 报 CS1503，`Modules` 被运行中的 `VisionMaster.exe` 锁住 | 不碰别人的文件；等 mtime 稳定后重试；`run_gates.ps1` 前确认没有 `VisionMaster.exe`/断言宿主残留 |
| `cmd` 里 `& echo EXIT=%errorlevel%` | 变量在整行解析期展开，打印的是上一条命令的退出码，会误判"构建成功" | 构建判据只看日志里的"0 个错误 / 已成功生成"，或 PowerShell `$LASTEXITCODE` |

## 六、验证证据

- `ScadaChecks`：**2853 通过 / 0 失败**（批次 A 后 2813、B 后 2821、C 后 2840、D 后 2853）。
- `FlowCanvasChecks`：**3657 / 0**。
- `UIThemeSmokeTest`：exit 0；`HelpProbe`：exit 0。
- `tools\run_gates.ps1`：**13 步全绿**（docs_index / audit_root / audit_artifacts / audit_docs / sync_constraints / CommChecks 336 / DataPipelineChecks 97 / DatabaseChecks 134 / ScadaChecks 2853 / RecipeChecks 138 / FlowCanvasChecks 3657 / UIThemeSmokeTest / HelpProbe）。
- 日志：`artifacts\logs\20261025\{build-checks-batchC,build-checks-batchD,checks-batchC,checks-batchD,gate-batchD}.log`。

## 七、追加（同日）：运行态取景——滚轮不缩放 + 底条「适配」按钮

**问题（用户现场提问"运行后为什么还随滚轮放大缩小"）**：`ScadaCanvas.Interaction.cs` 的 `OnMouseWheel` 没有 `IsReadOnly` 判断——左键/右键都写了"运行态直接 return"，唯独滚轮把编辑器行为原样带进了运行窗口；而且它还 `e.Handled = true` 把滚轮吞掉。运行窗口只设了 `IsReadOnly="True"`，`FitToScreen` 只在首帧与窗口尺寸变化时调用，底条那行 `适配 x%` 是纯文本、点不了 → 操作员滚一下滚轮画面就放大偏移，**唯一复位办法是拖窗口边框或重开运行**。顺带把报警条自己的 `ScrollViewer` 也废了：滚轮在列表上滚到尽头后事件冒到画布，被画布吃掉并缩放。

| 文件 | 改动 |
| --- | --- |
| `Scada.Controls\Canvas\ScadaCanvas.Interaction.cs` | `OnMouseWheel` 首行加 `if (IsReadOnly) return;`，**不标 Handled**（运行态既不缩放也不拦，报警条这类自带 ScrollViewer 的图元照常滚；中键平移保持原样） |
| `VisionMaster\Views\ScadaRuntimeWindow.xaml` | 新增 `FitButton` 样式（透明底 + 描边 + hover 高亮）；底条只读的 `适配 x%` 文本换成按钮 `FitButton`，`Click=OnFitClick` |
| `VisionMaster\Views\ScadaRuntimeWindow.xaml.cs` | `OnFitClick` → `FitCanvas()` + `UpdateStatusBar()`；`UpdateStatusBar` 改写 `FitButton.Content` |
| `ScadaChecks\Program.cs` | `[T-W1]` 只读画布 `RaiseEvent(MouseWheelEvent)` → 倍率不变且 `Handled=false`；`[T-W2]` 正例：可编辑画布同一路径 zoom=1.1 且 `Handled=true`（防止 W1 是"事件根本没到处理器"的假绿） |
| `ScadaChecks\Phase1UiChecks.cs` | AlarmBanner 模板契约加 `VerticalScrollBarVisibility="Auto"`——把"报警条行数超出时有竖滚动条"从口头结论变成静态闸 |

**验证**：`ScadaChecks` **2855 / 0**（新增 [T-W1]/[T-W2]）；`tools\run_gates.ps1` **13 步全绿**（`gate-wheel2.log`）。
**坑**：首次复跑 gate 时 `UIThemeSmokeTest` / `HelpProbe` 构建红在 `RecipeManagerView.xaml(1249): MC3072 SelectingCells 不存在`——是并发会话正在改的中间态，与本改动无关；等该文件 mtime 稳定 90s 后复跑即绿。
**未做**：报警条目前只有"列表 + 竖滚动条"，没有横向走马灯（ticker）形态，也没有页内报警表与行内逐条确认（`ShowRowAck`）；要的话按"AlarmBanner 加 `ScrollMode=List/Ticker`"扩展，不新建 TypeKey。
