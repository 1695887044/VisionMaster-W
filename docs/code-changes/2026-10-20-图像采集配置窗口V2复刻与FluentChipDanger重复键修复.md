# 2026-10-20 图像采集配置窗口 V2 复刻（含 FluentChipDanger 重复键修复）

## 1. 需求与范围

用户裁定：按 `docs/UI设计基线/image-acquisition/` 的 **V2 Web 基线**改插件界面；
**只做纯 UI 重构**，不改任何现有功能入口/绑定；主题色走公共令牌；自做断言验证渲染与功能。

V2 基线已明确把"需要新增功能/契约"的项移出：加载骨架、尺寸/文件大小、放大/缩小、
打开所在文件夹、前往相机设置、宿主 MinHeight 调整。本轮只落地纯 UI 部分。

## 2. 修改文件清单

| 文件 | 改动 |
| --- | --- |
| `Plugins\Plugin.ImageAcquisition\ImageAcquisitionView.xaml` | 整页重写：2×2 模式卡片、单页、左配置轨 + 右预览工作区、网络推送折叠区、相机状态卡、输出区、空/错误态 |
| `Plugins\Plugin.ImageAcquisition\ImageAcquisitionView.xaml.cs` | 保留构造/`DataContext=plugin`/`Initialize`；新增 `FileNameConverter`、`ModeLabelConverter`、`StatusMessageConverter` 与 `FitWindow_Click`/`FitImage_Click`/`FocusCamera_Click`/`OpenDebug_Click` |
| `UI\Controls\Themes\Colors.xaml` | 追加 5 个公共语义令牌：`DialogBorderStrongBrush`、`DialogSuccessBorderBrush`、`DialogDangerTextBrush`、`DialogDangerBorderBrush`、`DialogWarningBorderBrush` |
| `UI\Controls\Themes\PluginConfigColors.xaml` | 追加 5 个预览工作区令牌：`PluginPreviewCanvasBrush`、`PluginPreviewSurfaceBrush`、`PluginPreviewBorderBrush`、`PluginPreviewTextBrush`、`PluginPreviewMutedTextBrush` |
| `UIThemeSmokeTest\UIThemeSmokeTest.csproj` | 增加 `Plugin.ImageAcquisition` 工程引用（视图冒烟需要直接实例化） |
| `UIThemeSmokeTest\Program.cs` | 新增 `--image-acquisition`（22 条契约断言）与 `--image-acquisition-render`（四模式 PNG） |
| `UI\Controls\Themes\Fluent\FluentAliases.xaml` | **并发改动修复**：`DialogChipDanger` 同键定义两次（Button + Border），把新增 Button 变体改名为 `DialogChipDangerButton` |
| `VisionMaster\Views\DialogViews\IoConfigView.xaml` | 同步引用 `DialogChipDangerButton` |
| `docs\UI设计基线\image-acquisition\README.md` | 升到 v2 复刻状态；补实现映射、令牌新增、证据、偏差 |
| `docs\UI设计基线\image-acquisition\evidence\after-render-v2-*.png` | 四模式渲染证据 |

## 3. 功能契约保留（逐项）

| # | 契约 | 状态 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 保留；`ImageAcquisitionView.xaml.cs` 构造未改 |
| C2 | 4 个 `LinkableValueEditor`（`FilePath`/`FolderPath`/`FileIndex`/`FrameTimeout`） | 保留；`Port`/`StepData`/`BrowseCommand` 原绑定；只设 `Label=""` 外置标签 |
| C3 | `PreviewHost.Content = PreviewImage`；模板内 `ImageReadOnly.HImage = {Binding Mode=OneWay}` | 保留；控件类型/缩放/右键菜单不变；有图才实例化，修复折叠后不重绘 |
| C4 | `Mode` / `SelectedCameraOption` / `DisplayViewIndex` | 保留：枚举 / `CameraOption` 对象 / int |
| C5 | 命令与状态 | `Browse*`/`Refresh`/`Prev`/`Next`/`PushLocalImage` 与 `StatusMessage`/`StatusLevel` 三档全部保留 |

预览工具条只接既有公开方法：`FitToWindow()`、`FitToImage()`、`RefreshPreviewCommand`；
未新增放大/缩小、打开文件夹、前往相机设置。

## 4. 验证结果

| 项 | 命令 | 结果 |
| --- | --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.ImageAcquisition\Plugin.ImageAcquisition.csproj` | **0 错误 / 0 警告**；`Modules` 拷贝已成功（首次尝试曾被运行中的 VisionMaster / VS 短暂锁定，重试后通过） |
| UI 冒烟 | `dotnet run --project UIThemeSmokeTest -- --image-acquisition` | **24/24 通过** |
| 渲染 | `dotnet run --project UIThemeSmokeTest -- --image-acquisition-render` | 5 张 PNG（SingleFile/Folder/Hub/Camera/Error） |
| 常规 gate | `dotnet run --project FlowCanvasChecks --no-build` | **2691 通过 / 0 失败**（含 `[IA]` 段） |

UI 冒烟的 24 条覆盖：
- XAML 可读、**hex=0**（视图无硬编码颜色）、关键绑定文本齐全、4 个模式卡片仍用 `EnumConverter` 写 `Mode`；
- 视图可实例化（386 个元素）、`DataContext` 是插件、4 个 `LinkableValueEditor.Port` 正确、`PreviewHost.Content=PreviewImage` 且模板内 `ImageReadOnly` 拿到图；
- 相机下拉 `CameraOptions/SelectedCameraOption`、显示窗口 `DisplayViewIndex`、命令与推送字段绑定齐全、状态栏 `StatusMessage`；
- 4 个端口编辑分别写回 `FilePathPort`/`FolderPathPort`/`FileIndexPort`/`FrameTimeoutMs`；显示窗口写回 int；相机写回 `CameraSerial`；`Mode` 切换清预览；`OnConfirm` 写回 `InputValues` 的键与值（含 `Mode`/`DisplayViewIndex`/`CameraSerial`）；
- `FitToWindow`/`FitToImage` 可调用。

## 5. 已知偏差

1. **渲染证据范围**：`ImageReadOnly` 是 HALCON `HwndHost`，`RenderTargetBitmap` 不捕获其内容；证据 PNG 展示四种模式的空态布局，真实图像预览需真机窗口验证。
2. **模式卡片高度**：WPF 52px（Web 56px），以便 920×520 内容区里相机页帮助文字不被裁掉；2×2 排布与宽度不变。
3. **相机下拉**：选中项显示 `Caption`（显示名 + 序列号），状态在下方状态卡；Web 稿下拉同时显示状态。
4. **链接入口**：沿用 `LinkableValueEditor` 自带 🔗/✕，未另造方形链接按钮。
5. **折叠区**：`Expander` 用 Fluent 默认头（外加卡片边框），未复刻 Web 自绘 header。
6. **深色画布**：`ImageReadOnly` 模板背景是黑色；外层容器用 `PluginPreviewCanvasBrush`（#0B1220），图像区域呈近黑。

## 6. 并发改动冲突（必须知会）

`FluentAliases.xaml` 与 `IoConfigView.xaml` 的 `LastWriteTime` 为 **2026-10-20 00:34**，
与本轮构建/冒烟时间重叠；该并发改动在 `FluentAliases.xaml` 里新增了
`x:Key="DialogChipDanger"` 的 **Button** 样式，而文件末尾已有同键的 **Border** 样式，
应用资源合并直接抛：

```text
ArgumentException: Item has already been added. Key in dictionary: 'DialogChipDanger'
```

处理：把新增的 Button 变体改名为 `DialogChipDangerButton`，并同步 `IoConfigView.xaml`
的 `Style="{StaticResource DialogChipDanger}"` → `DialogChipDangerButton`。
**Border 的 `DialogChipDanger` 语义不变**；当前全仓只有 `IoConfigView` 一处 Button 引用。
若并发进程原本打算让 Button 继续叫 `DialogChipDanger`，请以那份改动为准合并命名。

## 7. 追加修复：打开图片后预览不刷新（2026-10-20）

### 症状

用户反馈两件事：
1. 「这个控件你为什么把我的 Halcon 控件换掉了？」
2. 「我打开图片后界面没有刷新。」

### 根因

**控件没有被换掉**，仍然是 `h:ImageReadOnly`，HImage 绑定、HALCON 缩放、右键菜单都在。
V2 第一版为了显示空态，把 `ImageReadOnly` 在 `PreviewImage == null` 时设为 `Visibility=Collapsed`，
有图时再恢复显示。问题出在时序：

- 图片到达时控件处于折叠态，`HImageChangedCallBack` 可能在 HALCON 窗口/模板尚未就绪时执行；
- 控件恢复可见后，`HImage` 已设置，但窗口没有可靠地重绘那一帧；
- 空态覆盖层又挡住了预览区，看起来就像「Halcon 控件被换成了静态面板」。

### 修法

- 改为 `ContentControl x:Name="PreviewHost" Content="{Binding PreviewImage}"`，
  在 `ContentTemplate` 里放 `h:ImageReadOnly HImage="{Binding Mode=OneWay}"`。
  **有图才创建控件**：控件创建时 DataContext 就是 HImage，`HImageChangedCallBack` 直接拿到图，
  不存在「折叠→恢复」的时序问题；无图时模板不实例化，空态/错误态是纯 WPF 覆盖层。
- `HImage` 绑定必须显式 `Mode=OneWay`：`HImage` 依赖属性是 `BindsTwoWayByDefault`，
  在 DataTemplate 里写裸 `{Binding}`（无 Path）会抛
  `XamlParseException: 双向绑定需要 Path 或 XPath`。这是本轮实际踩到的编译期全绿、运行期解析异常。
- 工具条 `FitToWindow()` / `FitToImage()` 改为从 `PreviewHost` 的可视树里取当前
  `ImageReadOnly`（`FindVisualChild<ImageReadOnly>`），因为模板内元素不在视图 namescope；
  无图时返回 null，按钮静默空跑，不改变原有缩放逻辑。

### 验证

- UI 冒烟新增一条：造 64×48 灰度 `HImage` → 验证 `PreviewHost.Content` 绑定路径是 `PreviewImage`、
  `ImageReadOnly` 由模板创建且 `preview.HImage` 与源图引用相等；**24/24 通过**。
- 5 张渲染 PNG 重新生成；`FlowCanvasChecks` **2691/0**。
- 真实图像预览仍需真机窗口：`ImageReadOnly` 是 HALCON `HwndHost`，`RenderTargetBitmap` 不捕获其内容；
  证据 PNG 展示的是空态/错误态布局。

## 8. 未做（延后）

加载骨架 / 尺寸与文件大小 / 放大缩小 / 打开所在文件夹 / 前往相机设置 / 宿主 MinHeight
（均需新增功能或契约，见基线 README 的 D1-D6）。

## 9. 复跑命令

```text
dotnet build Plugins\Plugin.ImageAcquisition\Plugin.ImageAcquisition.csproj
dotnet build UIThemeSmokeTest\UIThemeSmokeTest.csproj
dotnet run --project UIThemeSmokeTest --no-build -- --image-acquisition
dotnet run --project UIThemeSmokeTest --no-build -- --image-acquisition-render
dotnet run --project FlowCanvasChecks --no-build
```

> `FlowCanvasChecks` 首次构建时 `Plugin.Yolo` 的 `copy OnnxRuntime` 步骤曾因文件被占用
> 报环境性失败（退出码 4）；重试后构建 **0 错误**，gate 以 `--no-build` 重跑得到 **2691/0**。
> `Modules\Plugin.ImageAcquisition.dll` 已由插件工程刷新，`[IA]` 段加载的是新产物。
