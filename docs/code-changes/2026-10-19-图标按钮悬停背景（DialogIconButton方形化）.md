# 图标按钮悬停背景：从"窄竖胶囊"改回方形

日期：2026-10-19
范围：`UI/Controls/Themes/Fluent/FluentAliases.xaml`（`DialogIconButton`）、`VisionMaster/Views/DialogViews/IoHealthView.xaml`（关闭按钮 ToolTip）

## 现象

真机截图（IO 健康弹窗）里，鼠标悬停右上角关闭按钮时，按钮背景不是一块方形高亮，而是**一根竖着的浅灰胶囊**（约 8×21.5 逻辑像素），看着像错位的滚动条或渲染残片。

## 根因

`DialogIconButton` 派生自 `FluentButtonSubtleStyle`，只覆盖了 `Padding="6,4,6,4"`，没有约束宽高：

- 按钮内容是一个 13px 的 FontAwesome 字形 → 按钮实际尺寸 **22.1 × 32.0**（`MinHeight=32` 生效）；
- `FluentButtonStyle` 的悬停叠加层铺在**模板根 Border 的内容区**（即扣除 Padding 之后）：`22-12 = 10` 宽、`32-8 = 24` 高；
- 叠加层 `CornerRadius = FluentRadiusMedium = 8`，10×24 的圆角矩形在圆角半径 ≥ 半宽时退化成**竖胶囊**。

设计基线 `docs/UI设计基线/io-health/design.html` 的 `.icon-btn` 是 `width:30px; height:30px`，本来就不会出现这个问题。

## 修法

`DialogIconButton`：

```xml
<Setter Property="Padding" Value="0" />
<Setter Property="MinWidth" Value="30" />
<Setter Property="MinHeight" Value="30" />
```

`Padding=0` 让叠加层铺满整个按钮，30×30 + 圆角 8 = 正常的圆角方块（与 Win11 标题栏按钮同形）。

## 影响面核对

`DialogIconButton` 全仓 24 处引用，逐处确认都是**纯图标按钮**（标题栏关闭 7 处 + DataGrid 行内动作 + `DialogArrayEditor` 上移/下移/删除）：

| 位置 | 结论 |
| --- | --- |
| `CommunicationSettingsView` 操作列（`Width=190`） | 5 个按钮：5×30+12(右边距)=162 ≤ 190−12(单元格内边距)=178，不溢出 |
| `GlobalVariableView` 操作列（`Width=126`） | 该列已收敛为 复制/重命名/删除 3 个：3×30=90 ≤ 126−12=114，不溢出 |
| `ConditionEditorView` / `ScadaAlarmConfigDialogView` / `DialogArrayEditor` | 列宽 Auto 或单按钮，无溢出 |
| 7 个弹窗标题栏 | 只影响外观，30×30 更接近设计基线 |

## 附带修复：关闭按钮 ToolTip 被屏幕切掉

原写法 `<ToolTip Content="关闭" Style="…"/>` 用默认 `Placement=Mouse`，提示框跟着光标往右下走；按钮本身在窗口右上角，靠近屏幕边缘时提示被屏幕切掉（真机截图里只剩半个框）。改为 `Placement="Bottom"`，锚定按钮本身，由 WPF 的 Popup 在贴边时自动翻转。

## 验证

- `dotnet build UI\Controls\UI.csproj` → **0 错误**。
- 临时探针（`net9.0-windows`，R134；位于 `%TEMP%\TabProbe`，用完即删）加载真实 `UI.dll` 的 `DialogIconButton` + `DialogFocusVisual`，2 倍 DPI 离屏渲染：

| 状态 | 按钮尺寸 | 悬停叠加层纯色区 |
| --- | --- | --- |
| 改前 | 22.1 × 32.0 | 10 × 24（竖胶囊） |
| 改后 | 30.0 × 30.0 | 28 × 28（30×30 圆角方块扣掉四角抗锯齿） |

- `UIThemeSmokeTest`：仅剩**既有**的 vendored `GridView` 红灯（`TreeListView.xaml` / `MainWindow.xaml`，属 `VM3.0-main` 第三方目录，非本次改动引入），其余全部 PASS。

## 未完成 / 阻塞

- `dotnet build VisionMaster` 当前**失败**，原因与本改动无关：`Scada.Controls/Elements/AxisPanelElement.cs:601-606` 存在一段没有方法签名的孤立代码块（mtime 17:58，疑为另一进程正在编辑，按 AGENTS.md R9 未触碰）。该文件修好后需复跑一次全量构建确认。
- `DialogIconButton` 的悬停底色仍是 Fluent 模板的 `#0D000000` 叠加（白底上等于 `#F2F2F2`），设计基线是 `--row-hover = #F1F5F9`；圆角是 8，基线是 6。两者差异极小，暂记为已知偏差，未改动共享模板。

## 相关

- 设计基线：`docs/UI设计基线/io-health/README.md`（`.icon-btn` 30×30）
- 规则：R68（图标按钮内边距）、R124（以设计稿为准）、R134（.NET 9 探针必须 `net9.0-windows`）
