# 2026-10-20 坐标变换（PoseTransform）自定义视图 Web 化

## 0. 范围

- 设计基线：`docs/UI设计基线/pose-transform/README.md` v1 + `design.html`（自包含、可点击、含默认/关键/边界状态）
- 覆盖：`Plugins\Plugin.PoseTransform\PoseTransformView.xaml` + `PoseTransformView.xaml.cs`
- 不在范围：插件业务逻辑（`PoseTransformPlugin.cs`）、端口名、命令名、持久化键、共享控件/共享字典、宿主外壳
- 参考实现：`Plugin.ImageAcquisition` V2（`docs/UI设计基线/image-acquisition/`、`docs/code-changes/2026-10-20-图像采集配置窗口V2复刻与FluentChipDanger重复键修复.md`）

## 1. 耦合矩阵（SOP §5.1）

| 区域/元素 | 当前绑定/契约 | 功能依赖 | 可重构级别 |
| --- | --- | --- | --- |
| 根布局 | `UserControl`，`MinWidth=620`、`MinHeight=300` | 纯布局 | 自由；本轮改为 560×280 以适配宿主 640×560 最小档 |
| 模式选择 | 两个 `RadioButton`，`IsChecked → Mode` + `ui:EnumConverter`（`TransformMode.FollowRoi` / `ToMechanical`） | `OnModeChanged → RefreshConfigState()`；`Mode` 是 `[StepConfig]` | 只改外观，必须继续写 `Mode` |
| 位姿跟随输入 | `BaseRegion`（`InputPort<HRegion>`）、`PoseRow` / `PoseCol` / `PoseAngle`（`InputPort<double>`）经 `LinkableValueEditor` 的 `Port`/`StepData` | 端口持久化、类型转换、变量链接/解除链接；`ValueChanged → RefreshConfigState()` | 只能包裹，不能替换 |
| 模板参考点 | `TemplateRefRow` / `TemplateRefCol`（`InputPort<double>`）；`TemplateRefFromRegionCenter`（`[StepConfig] bool`）；`TakeRefFromRegionCenterCommand` | 派生开启时忽略端口值；一键取 ROI 中心 | 保留控件与命令；置灰只改外观 |
| 输出对齐图 | `EmitFollowedImage`（`[StepConfig] bool`） | 决定 `FollowedImage` 是否输出 | 保留 CheckBox |
| 像素→机械输入 | `Transform`（`InputPort<CalibrationTransform?>`）、`PixelPointRow` / `PixelPointCol` / `PixelAngle`（`InputPort<double>`） | 标定接入、反向回显自校验 | 只能包裹，不能替换 |
| 标定信息 | `CalibrationInfoText` / `CalibrationInfoLevel`（Info/Warning/Error） | 只读展示；失配/残差偏大/未接标定时变色 | 保留三档，不加第四档 |
| 参考图 | `SrcImage`（`InputPort<HImage>`，可选） | 尺寸失配校验 / 输出对齐图 | 只能包裹，不能替换 |
| 预览底图 | `PreviewImagePath`（`[StepConfig] string`）+ `BrowsePreviewCommand` | 配置态读盘、叠加预览；不参与运行 | 保留 TextBox + 命令 |
| 预览画布 | `h:ImageEdit HImage="{Binding DisplayImage, Mode=OneWay}"` | HALCON 显示/缩放/右键菜单；`DisplayImage` setter 释放旧 `HImage` | **控件常驻，不折叠、不替换**；本插件当前未使用 `DrawObjectList` / `ActiveRoi` |
| 状态 | `StatusMessage` / `StatusLevel`（Info/Warning/Error） | 状态栏颜色/文案 | 保留三档 |
| 跟随自检 | `FollowedInfoText` | 配置态面积变化一行 | 保留绑定 |

**判定原则**：11 个 `LinkableValueEditor` 是各自端口的唯一入口，全部标为“只能包裹/改外观”；`ImageEdit` 是 HALCON 交互的唯一入口，保持常驻。

## 2. 功能契约保留（逐项）

| # | 契约 | 状态 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 保留；构造签名、`plugin.Initialize(stepData)`、`OnViewAttached/OnViewDetached` 未改 |
| C2 | 11 个 `LinkableValueEditor`：`BaseRegion` / `PoseRow` / `PoseCol` / `PoseAngle` / `TemplateRefRow` / `TemplateRefCol` / `Transform` / `PixelPointRow` / `PixelPointCol` / `PixelAngle` / `SrcImage` | 全部保留 `Port` / `StepData`；只设 `Label=""` 外置标签 |
| C3 | `ImageEdit.HImage = DisplayImage`（`Mode=OneWay`） | 逐字保留；控件常驻，未用 `Visibility=Collapsed` 显隐 |
| C4 | `Mode` 是 `TransformMode` 枚举 | 仍走 `ui:EnumConverter`，未改成 int/string |
| C5 | `BrowsePreviewCommand` / `TakeRefFromRegionCenterCommand` + `StatusMessage`/`StatusLevel` 三档 | 全部保留；未新增第四档 |
| C6 | `Mode` / `EmitFollowedImage` / `TemplateRefFromRegionCenter` / `PreviewImagePath` 的 `[StepConfig]` 键名 | 一个未改 |
| C7 | `PreviewCanvas.FitToWindow()` / `FitToImage()` | 只接 `HalconBase` 既有公开方法，用于预览头「适应窗口 / 1:1」；未新增缩放逻辑 |

## 3. 改动点

1. **整页重写为两栏**：左 356px 配置轨（模式卡片 + 模式专属参数 + 公共参考图/预览底图），右叠加预览工作区自适应；960×680 与 640×560 两档可用。
2. **模式改为 2 张卡片**：`RadioButton` + `ModeCardStyle`，图标/标题/副标题/选中对勾；`IsChecked` 仍由 `ui:EnumConverter` 写回 `Mode`。
3. **11 个端口全部外置标签**：`LinkableValueEditor Label=""`，按位姿/参考点/像素点分组为两列栅格；HRegion/HImage/CalibrationTransform 端口仍显示“需链接变量”+链接按钮，未替换成普通输入框。
4. **模板参考点区**：`TemplateRefFromRegionCenter` 复选框 + `TakeRefFromRegionCenterCommand` 按钮 + 两个参考点端口；派生开启时用 `DataTrigger` 置灰（端口与链接入口仍在）。
5. **输出对齐图区**：`EmitFollowedImage` 复选框 + 分工说明。
6. **标定信息独立成块**：`CalibrationInfoText` 只读块，`CalibrationInfoLevel` 驱动 Info 绿 / Warning 橙 / Error 红。
7. **预览工作区**：44px 标题栏（图标 + “叠加预览” + 模式标签 + 适应窗口/1:1）+ 可选提示条 + 深色画布 + 40px 状态栏；`ImageEdit` 常驻，空态用画布上方提示条表达，不折叠 HwndHost。
8. **全令牌化**：视图 XAML 零十六进制；颜色/字体/焦点/提示全部走 `Colors.xaml` / `PluginConfigColors.xaml` 既有键；未追加任何公共令牌。
9. **code-behind 只加 UI 转换器/处理器**：`ModeLabelConverter`、`StatusMessageConverter`、`FitWindow_Click`、`FitImage_Click`；未引入独立 VM、未改业务逻辑。
10. **根最小尺寸**：`MinWidth` 620→560、`MinHeight` 300→280，适配宿主 640×560 最小档；未写死 900×500。

## 4. 与 ImageAcquisition V2 的差异

| 项 | ImageAcquisition V2 | PoseTransform v1 |
| --- | --- | --- |
| 模式数量 | 4 个（2×2） | 2 个（1×2） |
| 端口数 | 4 个 `LinkableValueEditor` | 11 个，按位姿/参考点/像素点分组为两列栅格 |
| HALCON 控件 | `ImageReadOnly`，按需创建（`ContentControl` + `DataTemplate`） | `ImageEdit` 常驻（本插件是配置态叠加预览，不是只读采集预览）；未折叠 |
| 空态 | 画布内虚线空态卡 | 画布上方提示条（避免 HwndHost airspace 与折叠不重绘） |
| 工具条 | 适应窗口 / 1:1 / 目录刷新 | 适应窗口 / 1:1（无目录刷新，本插件无目录概念） |
| 模式标签 | 文件/目录/网络/相机 · 只读 | 位姿跟随 · 配置 / 像素→机械 · 配置 |
| 标定信息 | 无 | ToMechanical 专属只读块，三档颜色 |
| 状态栏右侧 | 路径 | `FollowedInfoText`（跟随面积/相对基准变化） |
| 新增公共令牌 | 5 个 Dialog + 5 个 PluginPreview | **0 个**（复用既有令牌） |

## 5. 验证

| 项 | 命令 | 结果 |
| --- | --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.PoseTransform\Plugin.PoseTransform.csproj -v m` | **0 错误 / 0 警告** |
| Modules 产物 | `Modules\Plugin.PoseTransform.dll` | 已刷新（2026-10-20 02:41:13，182,272 bytes） |
| UI 冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.PoseTransform` | **11/11 全部 PASS** |
| 渲染证据 | `docs/UI设计基线/pose-transform/evidence/after-render-v1-initial.png`（副本来自 `_batch-evidence/Plugin.PoseTransform-PoseTransformPlugin.png`） | 已生成（初始态；HALCON HwndHost 不参与像素捕获） |

UI 冒烟逐条：

1. 找到待检插件 DLL（count=1）
2. `GetConfigView` 返回 `FrameworkElement`（`PoseTransformView`）
3. 视图实例化（elements=487）
4. `DataContext` / `IPluginConfigView`（`PoseTransformPlugin`）
5. `LinkableValueEditor.Port`（count=11，端口名逐个正确）
6. `Button.Command` 绑定（`TakeRefFromRegionCenterCommand`、`BrowsePreviewCommand`）
7. `ComboBox` 绑定（11 个内部枚举下拉，均来自 `LinkableValueEditor`）
8. `TextBox.Text` 绑定（`PortValue`、`LinkAddress`、`PreviewImagePath`）
9. `OnConfirm` 可执行（`InputValues=16`）
10. XAML `hex=0`（xaml=1，hex=0）
11. 渲染 PNG 生成成功

> 未运行 `FlowCanvasChecks`：本任务验收口径为插件构建 + 批量插件视图冒烟；本轮未改公共契约/共享控件，且其它插件可能并发改动，gate 由主会话统一回归。**未构建 `UIThemeSmokeTest`**（按要求 `--no-build`）。

## 6. 已知偏差

1. **图标字体**：Web 设计用内联 SVG，WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`）。
2. **空态表达**：Web 画布内画虚线空态卡；WPF 因 `ImageEdit` 是 HALCON `HwndHost` 且必须常驻，空态改为画布上方提示条。
3. **图例**：Web 右下角有图例；WPF v1 为窄档预览头留空间，未单独放图例，绿/橙/黄含义写在 `ImageEdit` ToolTip 与帮助文字里。
4. **渲染证据范围**：`RenderTargetBitmap` 不捕获 HALCON `HwndHost`，证据 PNG 只证明 WPF 布局与提示条；真实图像预览需真机窗口验证。
5. **模式卡片高度**：WPF 56px 与 Web 一致；窄档下模式标题用 12px、图标 24px，保证“像素→机械”不截断。
6. **根最小尺寸**：620×300 → 560×280，属于适配宿主 640×560 的布局调整，已在设计基线 README §9 记录。

## 7. 遗留问题

- **真机回环未做**：需要打开 VisionMaster → 坐标变换配置 → 逐个编辑 11 个端口 / 切换两种模式 / 试运行 / 确认 / 重开，验证 HALCON 叠加预览与变量链接。本环境只完成构建 + 批量冒烟 + 初始态渲染。
- **设计基线待用户确认**：`docs/UI设计基线/pose-transform/README.md` v1 的“确认日期/人”仍为待确认。
- **`docs/UI设计基线/README.md` 索引未更新**：按本次硬约束“不修改 `docs/UI设计基线/README.md`”，索引由主会话统一维护。
- **FlowCanvasChecks 未跑**：见 §5 说明；若主会话要求全量 gate，请单独执行。
- **未新增功能**：加载骨架、尺寸/文件大小、放大/缩小、打开所在文件夹、系统导航均未纳入（见设计基线 README §10）。

## 8. A 组遗留问题修复（2026-10-20）

| ID | 改动 |
| --- | --- |
| PT1 | `PoseTransformView.xaml` 根 `Grid` 加 `Typography.NumeralAlignment="Tabular"`，端口/标定信息/状态数字统一继承等宽数字。 |
| PT2 | `design.html` 的 `.pt-window.is-narrow` 补 `height:560px`（640 档设计稿高度对齐宿主 640×560）。 |

**验证**：`dotnet build Plugins\Plugin.PoseTransform\Plugin.PoseTransform.csproj -v m` → **0 错误 / 0 警告**；`--plugin-views Plugin.PoseTransform` → **11/11 PASS**（`elements=487`、11 个端口、XAML hex=0）。

## 9. 复跑命令

```text
dotnet build "D:\C#\VM\Plugins\Plugin.PoseTransform\Plugin.PoseTransform.csproj" -v m
dotnet run --project "D:\C#\VM\UIThemeSmokeTest" --no-build -- --plugin-views Plugin.PoseTransform
```

设计稿本地预览（任意静态服务器）：

```text
python -m http.server 8907 --directory "D:\C#\VM\docs\UI设计基线\pose-transform"
# 打开 http://127.0.0.1:8907/design.html
```
