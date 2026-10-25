# SCADA 第三批落地（页面背景 / 方案资产 / Picture / Line / 增量减量 / 启用对象）

日期：2026-10-23
设计基线：`docs/UI设计基线/scada-controls-batch3/`（v1）
范围：P0 三项 + P1 除配方外六项（共 10 项，全部落地；对象事件/计时器/二维码/多曲线/声音/历史见 §九）

## 一、背景与目标

按设计基线把“现场最缺的六项”落到 WPF 与 Web：

1. **页面背景图**是页面属性，不是图元：铺满、压底、不参与框选/命中，基座可继承。
2. **方案图片资产**跟着方案文件夹走：拖入即复制进 `Image\`，`.vms` 只记相对路径。
3. `Hmi.Picture` 只负责内容图片（logo/产品图/操作指引）。
4. `Hmi.Line` 负责连线/折线/箭头。
5. **增量/减量**是动作而不是控件：读当前值 ± 步长、夹紧上下限后写回。
6. **启用对象动画**：0 禁用、非 0 启用，禁用时不可交互并给出原因。

## 二、改动清单

### 领域层（VM.Scada）

| 文件 | 改动 |
| --- | --- |
| `Scada\Models\ScadaPage.cs` | 新增 `BackgroundImage` / `BackgroundStretch` / `BackgroundOpacity`；改背景相关属性推高 `Version`（Web 页键失效） |
| `Scada\Models\ScadaPageSnapshot.cs` | 快照新增背景图三字段 |
| `Scada\Models\ScadaPageSnapshotBuilder.cs` | 搬运三字段 |
| `Scada\Models\ScadaActionType.cs` | 追加 `Increment = 21` / `Decrement = 22`（展示名、PendingReason=null） |
| `Scada\Models\ScadaAction.cs` | 新增 `Step` / `Min` / `Max`（`double?`）+ `Detail` / `Describe` 文案 |
| `Scada\Models\ScadaAnimationType.cs` | 追加 `Enable = 5` + 展示名“启用对象” |
| `Scada\Models\ScadaAnimation.cs` | 新增 `Invert` / `DisableReason` + `TryEvaluateEnable` |
| `Scada\Models\ScadaChoiceNames.cs` | 补 Solid/Dash/Dot/DashDot/Start/End/Both/Tile 中文名 |

### 控件层（VM.Scada.Controls）

| 文件 | 改动 |
| --- | --- |
| `Canvas\ScadaCanvas.cs` | 新增 `PageBackgroundImage/Stretch/Opacity/Tile` 四个 DP + `PART_BackgroundImage` 应用（ImageBrush，支持平铺）；新增 `ImageAssetResolver` DP 并注入每个图元 |
| `Canvas\ScadaBackgroundStretch.cs` | 新增：模型成员名 ↔ `Stretch` / 平铺 / 不透明度换算（编辑器与运行窗口共用） |
| `Themes\ScadaCanvas.xaml` | 画面层新增背景图 `Rectangle`（最底、`IsHitTestVisible=False`） |
| `Elements\PictureElement.cs` + `Themes\PictureElement.xaml` | 新增 `Hmi.Picture`（SourcePath/Stretch/PlaceholderText；解析失败回退占位） |
| `Elements\LineElement.cs` + `Themes\LineElement.xaml` | 新增 `Hmi.Line`（Points 0~1 相对坐标、Dash、Arrow；箭头三角形随线宽） |
| `Descriptors\BuiltInElements.cs` | 注册 `Hmi.Picture` / `Hmi.Line` |
| `Descriptors\ScadaPageProperties.cs` | 背景组新增“背景图 / 背景填充 / 背景不透明度”三条 |
| `Descriptors\ElementPropertyDescriptor.cs` | 追加 `ElementPropertyKind.ImageFile`（枚举末尾） |
| `Elements\ScadaElementBase.cs` | 新增 `ImageAssetResolver` 注入点；`TryApplyAnimationValue` 支持 `Enable`；禁用 = `IsEnabled/IsHitTestVisible=false` + 0.55 压暗 + ToolTip 原因；`Refresh` 复位 |

### 宿主（VisionMaster）

| 文件 | 改动 |
| --- | --- |
| `Services\SolutionAssetService.cs` | 新增：导入（复制/哈希查重/重命名/临时文件 Move）、相对路径解析（拒绝绝对路径与越界）、带缓存解码 |
| `Services\ScadaActionDispatcher.cs` | 新增 `ExecuteStepVariable`（读-加-写、整数取整、夹紧、非数值/步长≤0 拒绝） |
| `Services\ScadaRuntimeHost.cs` | 创建运行窗口后注入方案目录 |
| `Views\ScadaRuntimeWindow.xaml.cs` | 背景图解析/应用、方案图片解析器注入 |
| `ViewModels\ScadaEditorViewModel.cs` | `SolutionRoot`、`SelectedPageBackgroundImage/Stretch/Opacity/Tile`、`ResolveImageAsset`；选中页变化时刷新 |
| `Views\ScadaEditorView.xaml` | 画布绑定背景图四属性与 `ImageAssetResolver` |
| `Views\ScadaRuntimeWindow.xaml`（代码侧） | 运行/弹窗画布背景与图片解析 |
| `Views\ScadaPropertyView.xaml` | 新增 `EditImageFile` 模板（缩略图 + 选择/清除）；动画区新增“反转/禁用原因” |
| `ViewModels\ScadaPropertyViewModel.cs` | 页面图片行命令与摘要/缩略图；动画行 `EnableInvert`/`DisableReason`/`IsEnable` |
| `ViewModels\ScadaEventEditorViewModel.cs` | 动作下拉顺序加入增量/减量 |
| `Views\Controls\ScadaEventEditor.xaml` | 增量/减量的变量选择、步长/下限/上限；写变量的“值”框只对 WriteVariable 显示 |

### Web 运行态

| 文件 | 改动 |
| --- | --- |
| `wwwroot\elements-batch3.js` | 新增 `Hmi.Picture` / `Hmi.Line` 渲染器（Picture 走 `ctx.assetUrl`，未接时显示文件名占位） |
| `wwwroot\index.html` | 引入 `elements-batch3.js` |

### 断言

| 文件 | 改动 |
| --- | --- |
| `ScadaChecks\BackgroundAssetChecks.cs` | 新增 `[BA]`/`[BB]`/`[BC]`：模型/属性声明/资产导入查重重命名越界/快照/新图元注册/增量执行与夹紧/启用对象闸门 |
| `ScadaChecks\Program.cs` | 名册加 `Hmi.Picture`/`Hmi.Line`（31 类）；`AnimationRows` 4→5；动作下拉期望序列加增量/减量 |
| `ScadaChecks\WebRendererCoverageChecks.cs` | 未改；`[WR1]` 由 29/29 变 31/31 |

## 三、关键决策

1. **背景图是页面属性**：做成图元会进框选/复制/图层排序，锁图层也挡不住全选。画布用 `Rectangle + ImageBrush` 而不是 `Image`，因为只有 ImageBrush 能表达平铺。
2. **资产只存相对路径**：`SolutionAssetService` 拒绝绝对路径与 `..\` 越界；同名同内容按 SHA-256 复用，同名不同内容 `(2)` 重命名，绝不覆盖。
3. **线的端点是相对坐标（0~1）**：随图元框缩放，拖框即缩放线；代价是“端点吸附到别的图元边缘”留到下一版。
4. **增量是动作**：与写变量共用同一条寻址与写入通道（`IScadaValueSource` → `IWritableVariable.TryWrite`），写租约、失败原因、审计格式不分叉；非数值变量与步长≤0 直接失败，不静默。
5. **启用对象是交互闸门**：不只变灰，`IsEnabled=false` + `IsHitTestVisible=false` 让鼠标/键盘/拖拽全不响应；原因挂 ToolTip。禁用视觉用 0.55 压暗，58% 遮罩留给模板层统一做。
6. **解析器注入而不是控件读方案路径**：`ScadaElementBase.ImageAssetResolver` / `ScadaCanvas.ImageAssetResolver` 由宿主注入，控件库不引用方案服务，断言环境无解析器时显示占位。

## 四、验证

| 项 | 结果 |
| --- | --- |
| `VM.Scada` / `VM.Scada.Controls` / `VisionMaster` / `ScadaChecks` 构建 | 0 错误（既有可空性警告不计） |
| `ScadaChecks` | **2556 通过 / 0 失败**（含 `[BA]` 13 条、`[BB]` 9 条、`[BC]` 5 条、`[BD]` 4 条、`[BE]` 3 条） |
| `UIThemeSmokeTest` | 全部段通过（含“第一方 XAML 不许硬编码颜色”扫描，0 FAIL） |
| Web 渲染器覆盖 `[WR1]` | 工具箱 31 类，缺 0 类 |
| 资源作用域 / 硬编码色 | 新 XAML 只用令牌；`ScadaCanvas.xaml` 仍合并 `ScadaTokens.xaml` |

### 全量 gate 未跑完（外部阻塞）

`tools\run_gates.ps1` 在本次收尾时被两件与本批无关的事情挡住：

1. **`VisionMaster` 正在运行**（PID 30476，9:22 启动，主窗口标题 VisionMaster），锁住 `Modules\*.dll`，插件工程无法投递 → `DatabaseChecks` / `FlowCanvasChecks` 构建失败（MSB3021/MSB3027）。按 R176，不杀用户正在用的进程。
2. **另一条会话正在做配方功能**：9:14 新建了根目录 `RecipeChecks`（`audit_root` 判红），9:16 新建 `VisionMaster\Services\RecipeSaveCoordinator.cs`（缺 `using Core.Interfaces.Result;`，导致 `ScadaChecks` 构建失败）。我只补了这一行 using 解阻，没有改其余内容；该会话可能仍在编辑。

因此本批的收尾证据以 **ScadaChecks 2549/0 + UIThemeSmokeTest 全通过 + 四工程 0 错误** 为准；全量 `run_gates` 需在关闭 VisionMaster、另一条会话收尾后重跑。

## 五、未做与下一步

按设计基线剩余五项，建议顺序：

1. **历史数据**（`IScadaHistorySource` + DataGrid `Source=History`）：先定契约与存储，再改表格。
2. **运行计时/计数器**（`Hmi.RunMeter`）：累计 + 1 分钟落盘 + 复位权限。
3. **对象更改/打开/关闭事件**：在 SelectorSwitch/文本开关/滚动条提交成功后发，只认用户操作。
4. **二维码/条形码**：需引入 ZXing（或等价编码库）；两端共用宿主编码结果。
5. **多曲线趋势**（`Hmi.MultiTrend`）：VM.Charts 多系列 + 图例 + 游标。
6. **声音报警**：宿主级单例 + 级别→音型映射。
7. **Web 方案图片资源路由**：`ctx.assetUrl` 的服务端实现（鉴权 + 方案目录映射），背景图与 Picture 才能真正取图。

已知偏差见设计基线 README「已落地部分的已知偏差」。

## 六、踩过的坑

- **普通 CLR 属性不能绑 XAML**：`ScadaCanvas.ImageAssetResolver` 一开始写成普通属性，`ScadaEditorView.xaml` 绑定直接 `XamlParseException`；改成 DP 后正常。
- **新增动画类型会改所有图元的属性行数**：`AnimationRows` 常量与多处“4 条动画”的期望要同步 +1，否则 8 条面板断言一起红。
- **动作下拉有顺序断言**：新增 `Increment/Decrement` 必须同步 `ActionDisplayOrder` 与断言里的期望序列，否则“类型下拉与枚举显示名同源”会红。
- **WPF 没有 SVG 解码器**：设计稿里“支持 svg”作废；`SolutionAssetService` 只收 png/jpg/jpeg/bmp/gif。
- **资产导入的测试内容**：`TryImport` 只哈希/复制、不解码，用任意字节即可；`TryLoad` 必须用真 PNG（断言里用 `PngBitmapEncoder` 写 4×4）。
- **图片选择命令只加在页面行上**：`Hmi.Picture` 的 `SourcePath` 是**图元属性行**（`ScadaPropertyRow`），而 `PickImageCommand` 当初只加在 `ScadaPagePropertyRow`；XAML 里按钮绑定到 null 命令，表现就是“点了没反应”。修法：把摘要/缩略图/选择/清除抽成 `ScadaImageFileRowSupport`，图元行与页面行共用，并补 `[BE1]`/`[BE2]` 钉住“两类行都有命令且 CanExecute=true”。
- **编辑态画布不显示图片、运行态才显示**：`ScadaEditorView.xaml` 里 `ImageAssetResolver="{Binding ResolveImageAsset}"` 在编辑器初始化顺序下会拿到 null；运行窗口是代码里显式赋值的，所以只有运行态出图。修法：编辑器视图在 `Loaded`/`DataContextChanged` 里显式 `Canvas.ImageAssetResolver = vm.ResolveImageAsset`（与运行窗口同一方式）；补 `[BE3]` 钉住“图元控件能从模型读到 SourcePath（TargetProperty 没漏）”。

## 七、文字图标库内置（2026-10-23 追加）

把 VM 已有的 Font Awesome 6 Pro Solid 内置进 SCADA 界面，支持“用文字写图标”：

| 落点 | 内容 |
| --- | --- |
| `Scada.Controls\VM.Scada.Controls.csproj` | 以 `Resource + Link` 链接 `UI\...\Font Awesome 6 Pro-Solid-900.otf` 到 `Assets\FontAwesomeSolid.otf`（不复制文件） |
| `Scada.Controls\Themes\ScadaIcons.xaml` | `ScadaIconFont`（FontFamily）+ `ScadaIconText`（样式）；合并进 `Scada.Controls\Themes\Generic.xaml` |
| `VisionMaster\App.xaml` | 应用级合并 `ScadaIcons.xaml`，任何视图可用 |
| `Scada.Controls\ScadaIconGlyphs.cs` | 约 60 个常用图标的名字常量 + 别名表；`TryResolve` 认 `Gear` / `f013` / `\uf013` / `&#xf013;` / `0xf013` |
| `Scada.Controls\ScadaIconHelper.cs` | 附加属性 `Glyph`：给 TextBlock 写名字就出图标，自动设字体 |
| `VisionMaster\Views\ScadaPropertyView.xaml` | 新图片选择器的“选择…/清除”按钮改用图标 + 文字 |
| `ScadaChecks\BackgroundAssetChecks.cs` | `[BD1]~[BD4]`：名字解析、四种码点、未知名字、字体真的带 `U+F013` |
| `docs\图标字体使用.md` | 三种写法、名字表、新增图标的步骤、R68 注意事项 |

用法：

```xml
xmlns:scada="clr-namespace:VisionMaster.Scada.Controls;assembly=VM.Scada.Controls"

<TextBlock scada:ScadaIconHelper.Glyph="Gear" />
<TextBlock FontFamily="{StaticResource ScadaIconFont}" Text="{x:Static scada:ScadaIconGlyphs.Play}" />
```

> 验证状态：`VM.Scada.Controls` 构建 0 错误；`ScadaChecks` **2556 通过 / 0 失败**（含 `[BD1]~[BD4]`、`[BE1]~[BE3]`）。
> 图标字体已在真 `Application` 与无 `Application` 的纯宿主下分别验证 `TryGetGlyphTypeface` 含 `U+F013`；临时探针已删。

## 八、真实渲染验收（2026-10-23 补）

数据断言挡不住“控件在真窗口里长什么样”，所以另起离屏 WPF 窗口（`Left=-4000`，STA + Dispatcher 泵，R53/R134）把新控件真渲染出来看。证据在
`artifacts/render/20261023/scada-batch3/`：

| 文件 | 内容 | 结论 |
| --- | --- | --- |
| `01-picture-line.png` | 页面背景图 + `Hmi.Picture`（真图）+ 直线箭头 + 折线双向箭头 + 按钮 | 图片显示、箭头方向正确、虚线折线正常、背景在元素之下 |
| `02-button-disabled.png` | 同页，按钮应用“启用对象”=0 | 按钮压暗（0.55）、`IsEnabled/IsHitTestVisible=false`、ToolTip=联锁原因 |
| `03-no-resolver-placeholder.png` | 同页，但画布不注入图片解析器 | 显示“未设置图片”占位卡片（对照组成立） |

**渲染发现并修掉的问题**：占位文字原本只绑 `Foreground`（深色），压在深色画布上几乎看不见；改成
`HmiSurfaceBrush` 底 + `HmiBorderStrongBrush` 描边的占位卡片 + `HmiTextMutedBrush` 文字。修后 `03` 图里“未设置图片”清晰可读。

> 结论：新控件在真窗口里能出图；`Hmi.Picture` 的“编辑态不显示、运行态才显示”已由编辑器视图显式注入解析器修掉。

## 九、第二批扩展（对象事件 / 计时器 / 二维码 / 多曲线 / 声音 / 历史）

日期：2026-10-23
范围：设计基线里剩下的六项。至此 **10 项全部落地**（配方除外，配方由另一条会话并行交付）。

### 9.1 改动清单

#### 领域层（VM.Scada）

| 文件 | 改动 |
| --- | --- |
| `Scada\Models\ScadaEventType.cs` / `Extensions.cs` | 追加 `ObjectChanged = 10` / `Opened = 11` / `Closed = 12` 与中文名 |
| `Scada\Models\ScadaHistory.cs` | **新增契约**：`IScadaHistorySource`、`ScadaHistoryQuery`、`ScadaHistoryResult`、`ScadaHistoryStatus`（Ok/Empty/Unavailable/InvalidRange/Timeout/Cancelled） |
| `Scada\Models\ScadaChoiceNames.cs` | 补 QR/条码码制、纠错等级、RunMeter 格式、MultiTrend 量程、DataGrid 数据源/范围的中文名 |

#### 控件层（VM.Scada.Controls）

| 文件 | 改动 |
| --- | --- |
| `Elements\RunMeterElement.cs` + `Themes\RunMeterElement.xaml` | `Hmi.RunMeter`：运行计时/计数器；节拍驱动；每 5 秒经 `Element.SetProperty` 落盘累计值（`_persisting` 防回环） |
| `ScadaCodeRenderer.cs` | ZXing.Net：QR（L/M/Q/H）/ Code128 / Code39 / EAN13 真编码 + 解码回读；空值/超长/EAN13/Code39 字符集校验 |
| `Elements\CodeElementBase.cs` + `QRCodeElement.cs` / `BarcodeElement.cs` + 主题 | `Hmi.QRCode` / `Hmi.Barcode`：白底黑码、静区、占位与超长错误态、绑定丢失保留末值 |
| `Elements\TrendSurface.cs` + `MultiTrendElement.cs` + 主题 | `Hmi.MultiTrend`：4 曲线、时间窗、共享/独立量程、图例、游标；`OnRender` 用 StreamGeometry |
| `Elements\DataGridElement.cs` + 主题 | `Source=Live/History`：范围循环按钮、查询/分页命令、Loading 骨架、Empty/Unavailable/InvalidRange 三态、失败横幅 + 重试、标题计数（历史=总数）；历史模式不采行；SizeChanged 重算列宽 |
| `Runtime\ScadaRuntimeContext.cs` | 新增可选 `History`（`IScadaHistorySource?`） |
| `Descriptors\BuiltInElements.cs` | 注册 3 个新 TypeKey；给位按钮/选择开关/滑块/下拉框补对象事件；DataGrid 补 `Source`/`HistoryRange`/`HistoryPageSize`/`HistoryTimestampColumn`/`HistoryKeyword` |
| `VM.Scada.Controls.csproj` | 引入 `ZXing.Net 0.16.9` |
| `Themes\ScadaTokens.xaml` | 新增 `HmiWarningBannerBrush`（失败横幅底） |

#### 宿主（VisionMaster）

| 文件 | 改动 |
| --- | --- |
| `Services\ScadaSoundAlarmService.cs` | **新增**：`IScadaSoundPlayer` / `ScadaSoundAlarmService` / `SystemSoundPlayer` / `ScadaAlarmTone`。状态机：紧急连续（950ms）、警告间歇（2s）、提示单次；确认最后一条即停；静音 5 分钟自动恢复；紧急不可静音；设备不可用落诊断；音量烘进 WAV 振幅 |
| `Services\CsvFolderHistorySource.cs` | **新增**：读 `<方案根>\History\*.csv`；时间列自动识别（找不到报错不猜）、按行时间过滤、最新在前、只返回当前页、坏文件/坏行计数、5 秒超时、取消 |
| `Services\ScadaMonitoringHost.cs` | 创建声音服务（在 `Attach()` 之前，初判报警也能响）并持久化 `AlarmSoundEnabled`；创建 `CsvFolderHistorySource` 装进上下文 |
| `Services\ScadaRuntimeHost.cs` | `window.SoundAlarm = _monitoring.SoundAlarm` |
| `Views\ScadaRuntimeWindow.xaml(.cs)` | 顶条新增声音开关（左键开关、右键试听/静音），状态文字随静音倒计时/设备不可用变化 |
| `Core\Models\AppConfigModel.cs` | `AlarmSoundEnabled`（默认 false）/ `AlarmSoundVolume`（55）/ `AlarmSoundMuteMinutes`（5） |
| `Core\SolutionPaths.cs` | 新增 `History` 目录常量与 `GetHistoryDirectory`，纳入保存时自动创建 |
| `Web\...\wwwroot\elements-batch3.js` | 新 TypeKey 渲染器（Picture/Line/RunMeter/QR/Barcode/MultiTrend）；MultiTrend 的 8 个键逐条写字面量（静态闸 WR5 只认字面键） |

### 9.2 口径

- **对象事件**：只在用户操作提交成功后发（选择开关/文本开关/滑块/下拉框）；程序灌值不发。
- **RunMeter**：计时按节拍累计；计数器在 `Source` 上升沿 +1；累计值随方案落盘，重启续算。
- **二维码/条码**：ZXing 真编码，断言做“编码→解码回读”；超长显示错误态并保留上一次有效图形；不反色、不加圆角。
- **MultiTrend**：默认共享量程；独立量程每条曲线各自 0~max；游标吸附最近采样点；缺采样画断口。
- **声音**：默认关闭；紧急连续且不可静音；警告可静音 5 分钟；确认最后一条即停；设备不可用留一行诊断、不静默。
- **历史**：数据源 = 方案目录 `History\*.csv`；时间列自动识别（找不到报 InvalidRange）；最新在前；分页只返回当前页；坏文件显式计数；超时 5 秒；不可用不清空上一次结果。

### 9.3 验证

| 手段 | 结果 |
| --- | --- |
| `ScadaChecks`（Debug） | **2637 通过 / 0 失败**（含 `[BF]` 对象事件、`[BG]` 二维码、`[BH]` 多曲线、`[BI]` 声音 17 条、`[BJ]` 历史 23 条） |
| 真实渲染（离屏真窗口，R53/R134） | `artifacts/render/20261023/scada-batch3/04-object-events.png`、`05-runmeter.png`、`06-qrcode-barcode.png`、`07-multitrend.png`、`08-history-ok.png`、`09-history-loading.png`、`10-history-empty.png`、`11-history-unavailable.png` |
| 声音真机播放 | 临时探针用 `SystemSoundPlayer` 真放三级提示音，并跑“真引擎 + 真播放器”重复/确认停声；日志 `artifacts/logs/20261023/soundprobe.log`（`available=True` / `PROBE OK`） |
| 二维码可扫 | `[BG]` 断言把生成的 QR/Code128/Code39/EAN13 解码回原文；渲染图 `06` 可肉眼核对 |
| `tools\run_gates.ps1 -SkipBuild`（10 步，最终） | **10 步全绿**：`audit_root` / `audit_artifacts` / `CommChecks` 336 / `DataPipelineChecks` 97 / `DatabaseChecks` 134 / `ScadaChecks` **2637** / `RecipeChecks` 138 / `FlowCanvasChecks` 3501 / `UIThemeSmokeTest` / `HelpProbe`。日志 `artifacts/logs/20261023/`。**过程记录**：前两次全量跑分别出现 `ScadaChecks` 3 条“数值域焦点”与 `FlowCanvasChecks` 2 条“假运动卡”超时判红；两条 exe 空载重跑分别 2637/0 与 3501/0，最终全量跑也全绿——是机器负载下的既有 flake（数值域点击模拟/假设备等待对负载敏感），不是本批引入。 |
| 方案目录 | `SolutionPaths.StructureFolderNames` 与 `SolutionAssetFolders.AssetFolderNames` 均加入 `History`；“另存为”会把 `Image/Alarms/History/Logs` 一起带走；`[SF10][SF18][SF19]` 已同步 |
| 临时探针 | `SoundProbe`、`HistoryProbe` 用完即删（R105/R166） |

### 9.4 已知偏差（本批新增）

- **声音没有设置页**：音量/静音时长走 `AppConfig.json`（`AlarmSoundVolume` / `AlarmSoundMuteMinutes`）；运行窗口提供开关、试听、静音，不提供滑杆。
- **历史导出 CSV 未接**：设计稿有“导出当前列表”，本版先交付查询/分页/三态；导出留待与通用导出通路一起做。
- **历史范围用循环按钮而不是下拉**：ComboBox 弹出层受应用级主题控制，深色 HMI 画面上会出现白底下拉；按钮文字即当前范围，点击切下一档并立即查询。
- **Web 端历史/声音/二维码/趋势仍是占位**：R132A 只要求“已设计画面的运行态查看”，历史查询在 Web 是非目标；`Hmi.Picture`/背景图的 Web 资源路由仍未接（与上一批同一条偏差）。
- **历史只认 CSV**：数据记录插件的账本目录要指到 `<方案根>\History`；不自动发现插件配置里的其他目录。

## 十、时钟“设了居中却不在中间”修复（2026-10-24 用户反馈）

### 10.1 现象与定位

用户截图：时钟 `Hmi.Clock`（X=1410 / Y=180 / 宽 360 / 高 44 / 字号 22），属性面板「水平对齐 = 居中」，但时间看起来贴在上沿、不在框中间。

先量像素排除“水平对齐没生效”：截图里选中框 x=362..695、时间文字 x=427..630，**左右留白都是 65px，文字中心 = 框中心 = 528.5**——水平方向其实已经居中。真正偏的是**垂直**方向：文字 y=464..475，框 y=458..499，上方 6px、下方 24px。

根因在 `ClockElement.xaml` 的两行布局：

- 第 0 行（时间）是 `*`，第 1 行（标签）是 `Auto`；
- 模板注释假设“`Text` 为空时 TextBlock 量出来的高度是 0，整行自然塌掉”——**这个假设是错的**：TextBlock 即使 `Text=""`，只要设了 `FontSize`，量出来的仍是一整行行高（22px 字约 28px）；
- 于是 44 高的框里，标签行白占 28px，时间行只剩 16px；28px 的字在 16px 行里垂直居中 → 溢出到框外，视觉上就是“贴着上沿”。

### 10.2 修法

| 文件 | 改动 |
| --- | --- |
| `Scada.Controls\Themes\ClockElement.xaml` | 标签 TextBlock 加 Style：`Text` 为 `""` 或 `null` 时 `Visibility=Collapsed`，让 Auto 行真正塌掉；时间行拿到整高后 `VerticalAlignment=Center` 才落在 22px 中心 |
| 同上 | 标签行原来硬编码 `TextAlignment="Center"`，改为 `TemplateBinding TextAlignment`——用户把水平对齐改成左/右时，标签不再和设定打架 |

### 10.3 验证

- 临时探针（已删）在屏幕外真窗口里量到：修复前时间 TextBlock `at=(0,-6)`（溢出框外），修复后 `at=(0,8)`、高 28 → 中心 22 = 44/2；空标签 TextBlock `0×0`。
- `ScadaChecks` 新增 `[BK1]~[BK6]`（`BackgroundAssetChecks.RunClockLayoutChecks`）：空标签收起 / 时间垂直居中 / 模型 Left→控件 Left / 属性面板写模型 / 已挂载控件自动刷新 / 有标签时两行都在框内。
- 全量 `ScadaChecks`：**2691 通过 / 0 失败**。
- 注：运行中的 `VisionMaster.exe` 需要**重启**才会加载新 `VM.Scada.Controls.dll`（DLL 已在 19:51 复制到 bin）。
