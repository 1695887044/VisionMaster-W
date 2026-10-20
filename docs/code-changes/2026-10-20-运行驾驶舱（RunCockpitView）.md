# 运行驾驶舱（RunCockpitView）

- 日期：2026-10-20
- 范围：`VisionMaster/Views/RunCockpitView.xaml(.cs)`、`Shell.xaml`、`ShellCopy.xaml`、`Services/LayoutHelper.cs`、`UI/Controls/Themes/Colors.xaml`、`UIThemeSmokeTest/Program.cs`
- 设计依据：`docs/UI设计基线/app-shell/design.html` 的运行场景
- 目标：第 3 步的后续视觉批次——运行模式从“仅视觉图像”升级为深色驾驶舱；不改 Nodify/AvalonDock 几何

## 改动清单

### 1. 驾驶舱视图

- 新增 `RunCockpitView.xaml`：
  - 深色顶条：运行状态点 + `RunStateText` + 当前流程 + 大号“停止运行”（绑定 `ExecutionCommand.Stop`）。
  - 图像区：复用现有 `ImageView`（不复制 ImageGallery/采集链路），放在白色圆角容器里。
  - KPI 条：节拍 / 良率 / 设备在线 / 未确认报警；当前没有运行统计源，显示“—”+“待接入…”，不伪造生产数字。
- `RunCockpitView.xaml.cs`：`Loaded` 时若 DataContext 为空，兜底取 `Application.Current.MainWindow.DataContext`（AvalonDock 浮窗/独立宿主下仍能绑定 ShellViewModel）。

### 2. 文档与模式

- `Shell.xaml` 新增 `LayoutDocument`：`Title="运行驾驶舱"`、`ContentId="Panel_RunCockpitView"`。
- `LayoutHelper.ContentFactories` 增加 `Panel_RunCockpitView` 工厂。
- `DocumentsForMode(Run)` 改为只保留 `Panel_RunCockpitView`；调试/组态/自由隐藏它。
- `CurrentLayoutSchemaVersion` **4 → 5**：文档区新增 ContentId，旧 `Layout.xml` 必须让位（R43）。
- `ShellCopy.xaml` 同步新增驾驶舱文档（R44）。
- 模式断言更新：运行模式检查 `Panel_RunCockpitView` 存在、其它文档不存在。

### 3. 令牌

`Colors.xaml` 新增 `RunCockpit*` 深色子主题：背景 / surface / chrome / border / 文字三级 / 成功 / 危险 / 警告 / 信息 / 停止按钮三态。全部为 `SolidColorBrush`，不引入 CSS 式滤镜。

## 验证

- `dotnet build VisionMaster\VisionMaster.csproj`：**0 错误**。
- `UIThemeSmokeTest`：
  - 多格布局 / Shell 布局结构 / Panel 模式 / **Shell 模式隔离** / GridView / LogConsole **全部通过**。
  - 运行模式断言：驾驶舱文档存在、图像/流程/组态文档均被摘出；左右栏隐藏。
- 未做真机截图对照：驾驶舱的视觉验收需在 WPF 实机跑一次（图像区、KPI 条、停止按钮）。

## 已知边界

1. **KPI 是占位**：没有运行统计源，四张卡显示“—/待接入”，不是最终生产数据；接入后应绑定真实节拍/良率/设备在线/报警统计。
2. **图像区复用 ImageView**：驾驶舱内是第二个 ImageView 实例（运行文档隐藏时仍存在），与调试文档的 ImageView 各自维护 ImageGallery；如果后续要求“同一份图像状态跨模式共享”，应把 ImageView 做成单例内容或共享 VM。
3. **运行态顶条只显示运行状态/流程**：节拍、OK/NG、报警计数在 KPI 卡里占位；深色控制台仍使用 Shell 底部日志面板，未做成驾驶舱内嵌 240px 控制台。
4. **全屏/kiosk**：尚未实现隐藏标题栏的全屏运行；当前仍保留 Shell 标题栏与状态栏。
5. **布局版本**：已升到 5；老用户 `Layout.xml` 会被拒并回退默认布局（用户自拖布局会作废），这是 R43 的本意，需要发布说明。
