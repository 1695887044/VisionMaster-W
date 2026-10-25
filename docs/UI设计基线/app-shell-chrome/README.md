# 主界面 chrome（顶栏 + 底栏）· 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | **v2**（草案）· v1 保留在 `design.html` |
| 出稿日期 | 2026-10-24 |
| 确认人 | **待用户确认**（R132：验收由用户裁定） |
| 覆盖范围 | `VisionMaster/Shell.xaml` 的 **顶部标题栏（Row0=36）** 与 **底部状态栏（Row3=26）**，以及它们之间的 **命令条（Row1=48，仅调试 / 自由模式可见）**。不含三栏工作区内部、停靠面板归属、流程画布/步骤树内容 |
| 设计基线文件 | `design-v2.html`（**v2，按真机状态**）· `design.html`（v1，抽象画布版，保留对照） |
| 设计画布 | 1280 × 853（与用户真机截图同尺寸，1:1 不缩放） |
| 与 `../app-shell/` 的关系 | 本稿是 app-shell v2 的 **chrome 专项增量**，不改其四场景结构（调试 / 运行 / 组态 / 自由）与模式隔离表 |

## 定位

按 `AGENTS.md` §30.1 四步流程，本文件是 **第 ① 步（Web 设计基线）** 的产物。

- Web 只作设计媒介，**不进构建、不部署、不作为第二客户端维护**（R126）。
- 确认后才进第 ③ 步 WPF 复刻；第 ④ 步以「同一屏、同一状态」截图对照验收（R129 / R132）。
- 本稿 **未改任何 WPF 代码**，`Shell.xaml` 与 `UIThemeSmokeTest/ShellCopy.xaml` 均未动。

## 交互说明（可点的部分）

`design.html` 顶部有 6 个状态按钮，也支持 URL 深链与键盘：

| 状态 | 深链 | 看什么 |
| --- | --- | --- |
| 调试 · 运行中 | `design.html?s=debug` | 默认态：顶栏模式分段 + 运行条 + 底栏 KPI 有值 |
| 运行（驾驶舱） | `design.html?s=run` | 左右栏收起、运行条隐去 |
| 组态 | `design.html?s=config` | 底栏左区换成组态信息（页面 / 分辨率 / 网格 / 选中图元） |
| 键盘焦点 | `design.html?s=kbd` | 模式段与底栏项上的 2px 焦点环（R115 第五条） |
| 空方案 | `design.html?s=empty` | 未加载方案 / 新建未保存 |
| 命令面板 | `design.html?s=palette` | Ctrl+K 入口与带文字的命令列表 |

顶栏里 **☰ 文件 / 身份胶囊 / 窗口按钮 / 底栏各可点项** 都有 hover 与按下态；底栏 **问题清单胶囊** 与 **CPU·MEM** 可点开浮窗。

## 优化清单（P0 / P1 / P2 共 12 条）

完整「现状 → 改法 → 理由」表在 `design.html` 卡片 1，行号指向当前 `VisionMaster/Shell.xaml`。摘要：

**P0（误导 / 空间浪费，建议本轮就改）**

1. `:1722-1730` 底栏方案路径 `MaxWidth=400` 截断成 `…综合测试台（测试员版…`（截图即如此）→ 左区只显方案名，悬浮显全路径。
2. `:1613-1680` KPI 未接入时绑 `—`，与真实值混排分不清"0 还是没接入"→ 浅色 `未接入` + 虚线占位。
3. `:1770-1784` 控制台折叠按钮只有一个 `F0120` 图标 → `▁ 控制台` 带文字，展开时箭头翻转。

**P1（密度与可用性）**

4. `:1613-1686` 9 条 `TextBlock "|"` 做分隔、间距 4/6/8/10 混用 → 统一 1px 分隔块（14px 高）。
5. `:302` 底栏 26px、命中区只有文字本身 → 28px + 24px 可点区域。
6. `:1785-1851` CPU/MEM/THD/HND 四组常驻（占底栏约 1/3）→ 常驻 CPU+MEM，其余进浮窗。
7. `:1194-1211` 新建/打开/保存三个纯图标与菜单区同权重 → 收进 `☰ 文件` 菜单。
8. `:1216-1244` 模式按钮只有三套选中色、无快捷键提示 → 分段控件 + `^1…^4` 角标 + 选中态说明。
9. `:1278-1301` 顶栏中央只有"文件夹图标 + 方案名" → 身份胶囊（方案名 + 保存态 + 目录尾段）。

**P2（可选）**

10. 新增底栏右侧问题清单胶囊（`2 错误 · 1 警告`，点开可定位）。
11. `:1322-1436` 运行条右端空着 → 当前步骤进度 + 实时节拍。
12. `:1251-1274` 四个 FontAwesome 图标只有 ToolTip → 命令面板（Ctrl+K）补全文字入口。

## 令牌映射（复刻时只认这张表）

视图里**禁止再写 hex**（R117 / R140）。

| 用途 | `design.html` 变量 | WPF 资源 |
| --- | --- | --- |
| 底栏底 | `--surface-2` | `ScadaTitleBarBrush` |
| 顶栏底 | `--surface` | `DialogSurfaceBrush` |
| 分隔线 | `--border` | `DialogBorderBrush` |
| 次要文字 | `--text-2` | `ScadaSecondaryTextBrush` |
| 弱化文字 | `--text-3` | `ScadaFaintTextBrush` |
| 选中底 | `--primary-light` | `DialogAccentSoftBrush` |
| 主色 | `--primary-strong` | `DialogAccentBrush` |
| OK | `--success` | `DialogSuccessBrush` |
| NG / 停止 | `--danger-line` | `DialogDangerBrush` |
| 告警 | `--warning` | `DialogWarningBrush` |
| 圆角 6 | `--radius-sm` | （新增）`TitleBarButtonCornerRadius` |
| 底栏高 28 | `--statusbar-h` | `Shell.xaml` Row3 `Height=28` |
| 顶栏高 40 | `--titlebar-h` | `Shell.xaml` Row0 `Height=40` + `WindowChrome.CaptionHeight=40` |

新增令牌只有"圆角 6"与两个高度常量；**没有新增颜色**。

## 与现有规范的冲突检查

| 规范 | 是否触发 | 处理 |
| --- | --- | --- |
| R43 / R47 布局结构版本 | 不涉及面板归属与停靠语义，只改两行高度 | **不升** `LayoutHelper.CurrentLayoutSchemaVersion`（与 R47"改顶栏不升结构版本"一致） |
| R44 / R70 | 改 `Shell.xaml` 必须同步 `UIThemeSmokeTest\ShellCopy.xaml` | 复刻批次里同批改副本，并跑 `RunShellLayoutChecks()` 9 条 |
| R68 | `.ib` 是 28×28 图标字符按钮 | 复刻时显式 `Padding="0"` + 模板，不靠 Fluent 默认 `11,5,11,6` |
| R117 / R140 | 新增颜色 | 无新增 hex，全部复用既有令牌 |
| §31 R145 | 不涉及 AvalonDock 收起 / 标签 / `ShowAnchorable` | 无需处理 |

## 已知偏差（R130 / R131，逐条列出，待用户裁定是否接受）

1. **字体度量**：Web 用 `system-ui`，WPF 用 `Microsoft YaHei UI`；同字号下中文墨迹宽度与行高不同，顶栏中央身份胶囊在窄方案名下的最小宽度需实测。
2. **阴影层数**：Web 顶栏/浮窗用了 2 层阴影，WPF `DropShadowEffect` 建议只做 1 层（避免 x:Shared 与视觉树开销）。
3. **滚动条宽度**：Web 默认 8px 悬浮滚动条；WPF 是系统滚动条（约 17px），命令面板与问题清单浮窗宽度需 +9。
4. **DPI**：26→28、36→40 的 2/4px 增量在 125% / 150% 缩放下会取整到 2.5/3 与 5/6 物理像素；需按 `dpi` 自适应或接受 1px 误差。
5. **动效曲线**：Web 用 `cubic-bezier(.2,.8,.2,1)` 150ms / 180ms；WPF 对应 `QuadraticEase` 或线性 150ms 近似（R116 只要求 150~200ms）。
6. **文本截断**：Web 用 `text-overflow: ellipsis`；WPF 必须 `TextTrimming=CharacterEllipsis` + `MaxWidth`，且 GridView 无星号列宽不适用本页。
7. **焦点态**：Web 用 `outline` 叠在视觉上；WPF 走 `FocusVisualStyle`（R135 要求弹窗内可聚焦控件挂 `DialogFocusVisual`），顶栏按钮需要同款并保证焦点环不改变布局尺寸。
8. **无障碍**：Web 稿有 `role="tablist"` 语义，WPF 需用 `AutomationProperties.Name` 补齐模式段的名称与快捷键播报。

## 复刻时必须同步的落点（不改，仅登记）

- `VisionMaster/Shell.xaml`：Row0 高度与 `CaptionHeight`、Row3 高度、顶栏三块子树的元素重排、底栏左右两区。
- `UIThemeSmokeTest/ShellCopy.xaml`：R44 结构副本。
- `UI/Controls/Themes/Colors.xaml`：仅"圆角 6"与高度常量（如决定入库为令牌）。
- `UI/Controls/Themes/Dialog/`：`☰ 文件` 菜单按钮、身份胶囊、KPI 气泡、问题清单胶囊——按 R141「第二次出现就下沉」评估是否直接进 `Dialog*` 分册。

## 待用户拍板（复刻前必须定）

| # | 问题 | 建议 |
| --- | --- | --- |
| Q1 | 顶栏 36 → 40？影响 `CaptionHeight` 与双击标题栏最大化命中区 | **40** |
| Q2 | 底栏 26 → 28？画布各损失 2px 高 | **28** |
| Q3 | 新建/打开/保存收进 `☰ 文件` 菜单，还是保留三个图标？ | **收进菜单** |
| Q4 | 系统资源常驻 CPU+MEM（THD/HND/GC 进浮窗），还是继续四项常驻？ | **常驻两项** |
| Q5 | 问题清单本轮是否要做（需新增聚合逻辑，非纯视觉）？ | 可先只做视觉占位 |
| Q6 | 未接入 KPI 显示 `未接入` 还是继续 `—`？ | **未接入** |

## v2 与 v1 的差别（2026-10-24）

v1 用的是抽象画布（1920×1080，示意内容），v2 改成**按用户真机截图 1:1 出稿**，同一屏内容做「现状 / 改后」两栏对照：

- 底稿状态：调试模式，`Test_Motion` 已选中（37 步 · 最深 2 层），`admin（管理员）`已登录，日志 108 条（信息 37 / 警告 71），CPU 0.1% · MEM 391 MB · THD 100。
- 两屏的流程树、步骤列表、画布节点、日志行、工具箱分类计数、底栏读数**完全一致**，差异只来自 chrome。

v2 相对 v1 撤回 / 修正的三处（v1 属过度设计）：

| # | v1 的做法 | v2 改为 | 为什么撤回 |
| --- | --- | --- | --- |
| 1 | 新建 / 打开 / 保存 收进 `☰ 文件` 菜单 | **保留三个图标**，只做视觉归组 + 分隔 | 真机上这三个是高频操作，藏进菜单增加一次点击，收益不抵成本 |
| 2 | 命令条引入蓝色主按钮「运行主流程」 | 沿用现有**绿=运行 / 红=停止 / 灰=不可用**，只补不可用对比度与命中区 | 既有颜色语言已经是语义，换成蓝色主按钮等于推翻一套用户已熟悉的编码 |
| 3 | 底栏加「⚠ 警告 21」胶囊（取日志警告数） | **撤回**，底栏「告警」保持产线告警 KPI；日志警告改为在日志面板内做成可点筛选项 | 两个指标同名不同义（产线告警 ≠ 日志警告），合并会让底栏读数失去可信度 |

v2 保留的改动：顶栏 36→40、底栏 26→28、模式改分段控件（选中色仍是现有紫色）、中央方案胶囊补目录尾段、9 条 `TextBlock "|"` 换成 1px 分隔块、方案名取代截断路径、24px 可点区域。

## 截图证据

| 文件 | 版本 | 状态 | 看什么 |
| --- | --- | --- | --- |
| `evidence/design-v2-before.png` | v2 | 现状 | 与你真机截图同内容：Test_Motion 选中、37 步列表、108 条日志、admin（管理员）、391 MB |
| `evidence/design-v2-after.png` | v2 | 改后 | 同一内容下的 chrome：文件图标归组、模式分段带 `^1..^4`、方案胶囊、警告 71 可点、底栏只显方案名 |
| `evidence/design-v1-debug-top.png` | v1 | 调试态顶部 | v1 抽象画布版（保留对照） |
| `evidence/design-v1-debug-bottom.png` | v1 | 调试态底部 | v1 底栏 + 12 条优化清单表 |
| `evidence/design-v1-config-bottom.png` | v1 | 组态态底部 | v1 底栏左区切换为组态信息 |

复现方式：`python -m http.server 8765 --directory docs/UI设计基线/app-shell-chrome`，
浏览器打开 `http://127.0.0.1:8765/design-v2.html`（v1 为 `design.html?s=debug`）。

## 变更记录

| 版本 | 日期 | 内容 |
| --- | --- | --- |
| v1 | 2026-10-24 | 首版：顶栏 / 运行条 / 底栏三段重排，12 条优化清单，令牌映射与已知偏差，6 项待拍板 |
| v1（补证） | 2026-10-24 | 补 `evidence/` 三张渲染截图；设计本身未改，`design.html` 未动 |
| v2 | 2026-10-24 | 按真机截图 1:1 重出（1280×853，Test_Motion 已选中 / admin 已登录 / 108 条日志）；撤回 v1 三处过度设计（文件操作藏菜单、蓝色主按钮、告警 KPI 与日志警告合并）；证据 `evidence/design-v2-{before,after}.png` |
