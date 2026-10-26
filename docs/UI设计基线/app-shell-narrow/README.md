# app-shell 非全屏布局（v0，A 已实施）

> 状态：**A 已确认并实施（2026-10-26）；B 仍为草案**。
> 本目录是设计媒介与实施记录，不是交付物（R126）。

## 一、要解决的问题

非全屏窗口（约 1310×829 DIP）下，中央视觉图像只有约 **604×464**，占窗口面积约 **26%**。
原因是底部控制台默认展开 200、右栏 320，左右栏与控制台把画布挤到不足一半。

## 二、三种画法

| 方案 | 左栏 | 右栏 | 控制台 | 图像区 | 面积变化 | 状态 |
| --- | --- | --- | --- | --- | --- | --- |
| 现状 | 340 | 320 | 200 展开 | 604×464 | — | — |
| **A** | 340 | 300 | 32 收起 | 624×632 | **+41%** | **已实施 2026-10-26** |
| **B** | 隐藏 | 隐藏 | 200 展开 | 1264×464 | **+109%** | 草案，未做 |
| A+B | 隐藏 | 隐藏 | 32 收起 | 1264×632 | +185% | 草案，未做 |

- **A** 是设计基线一致性修正：`app-shell/README.md:35` 的「调试」场景本来就写控制台收起 32；`design.html:2169-2172` 在 ≤1500px 视口下检查器为 300。不需要新交互。
- **B** 是新增「专注图像」入口：隐藏左右栏、保留控制台，适合看图/调焦；退出后恢复原布局。需要设计评审与用户确认（R125）。
- 左栏 340 **不建议降**：2 列胶囊在 280 时名字列约 60px，`ST1_LoadCode` 这类长流程名会截断。

## 三、图

- `evidence/design-narrow-01.png`：三帧竖向排列（现状 / A / B）+ 面积账表。
- `evidence/design-interaction-01.png`：A 控制台收起/展开两态 + B 专注/边缘手柄三态 + 状态表。
- 复现（总览）：

```powershell
& "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" `
  --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 `
  --window-size=1140,2700 `
  --screenshot="D:\C#\VM\docs\UI设计基线\app-shell-narrow\evidence\design-narrow-01.png" `
  "file:///D:/C%23/VM/docs/UI%E8%AE%BE%E8%AE%A1%E5%9F%BA%E7%BA%BF/app-shell-narrow/design.html"
```

- 复现（交互细化）：

```powershell
& "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" `
  --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 `
  --window-size=1140,3000 `
  --screenshot="D:\C#\VM\docs\UI设计基线\app-shell-narrow\evidence\design-interaction-01.png" `
  "file:///D:/C%23/VM/docs/UI%E8%AE%BE%E8%AE%A1%E5%9F%BA%E7%BA%BF/app-shell-narrow/design-interaction.html"
```

## 四、交互细化（A / B）

详见 `design-interaction.html` 与 `evidence/design-interaction-01.png`。

### A 控制台 32 ⇄ 240

**已实施（2026-10-26）**

- 右栏默认宽 **300**：`Shell.xaml` 与 `ShellCopy.xaml` 的 `DockWidth` 同步；冒烟期望值同步改 300。旧 `Layout.xml` 里恰好 320 的右栏由 `EnsureInspectorColumnWidth` 在加载后拉回 300（用户拖过的其它宽度不动）。
- 控制台默认收起：`LayoutHelper.CurrentConsoleState` 默认 `Collapsed`；`LoadFromString` 对没有 `ConsoleState` 属性的老布局按模式默认（调试/自由 32、运行 240、组态隐藏）；`Shell.OnContentRendered` 在没有 `Layout.xml` 时收起；`SetShellMode` 切回调试/自由时收起。
- 状态持久化：`SaveToString` 在布局根写 `ConsoleState="Collapsed|Expanded"`（组态的 Hidden 按 Collapsed 写），`LoadFromString` 读出后恢复；重启保留用户上次的展开/收起。
- 调试/自由分支不再 `RestoreLogPane`：控制台状态只由 `LoadFromString`、`SetShellMode`（切模式收起）、用户 Toggle 维护，避免启动时把收起态洗回 200。
- 证据：`UIThemeSmokeTest` 132 PASS / 0 FAIL（含「隐藏最后一个可见面板：pane 变空、工具箱进 Hidden 区，再显示能恢复」与「右侧检查器列宽兜底：旧布局 320 → 300」）；`ScadaChecks` 3107 / 0。

**未实施（A 的交互细化）**

- 展开态页签条右端的「⌄ 收起」按钮、`Ctrl+``、双击页签条收起、拖拽 120~480 限制、抽屉条摘要区点击。当前收起路径仍是状态栏「控制台」/ 命令面板 / 抽屉条展开后的再次点击（状态栏）。
- 展开默认高度仍是 XAML 的 200（设计建议 240）；本次未改，避免和现有用户布局冲突。

### B 专注图像：左右栏 0 ⇄ 340/320

- **进入**：文档标签右侧「⤢ 专注图像」/ `Ctrl+Shift+F`；只在视觉图像文档显示。
- **退出**：`Esc` / 图像右上角浮动「退出专注 Esc」/ 文档标签「⤡ 退出专注」/ 左·右边缘手柄。退出恢复进入前的左右栏宽度、页签、滚动位置；点击边缘手柄时该侧成为活动页签。
- **边缘手柄**：左右各 6px 热区；静止只有 4px 分隔线；悬停出 24×72 的 `›`/`‹` 标签 + tooltip；点击 = 退出专注并恢复两侧（不做“只恢复一侧”的半专注态）。
- **控制台不变**：进入前是 32 就还是 32，是 240 就还是 240；A 的机制在专注中照常。
- **切文档 / 切模式**：先退出专注，再切；专注只属于主窗口停靠的视觉图像。
- **重启**：不恢复专注——专注是瞬态视图状态，不写 `Layout.xml`。
- **边界**：进入时某侧已隐藏 → 退出只恢复可见侧；`Esc` 先关最上层弹窗再退专注；浮动文档不进入专注；窗口缩放后退出按记忆宽度恢复，不按当前比例重算。
- **实现落点**：Shell 新增 `IsImageFocusMode`；进入时记左右 pane 的 `DockWidth`、可见页签、选中页，按现有 `HidePanel` 收起两侧全部 anchorable；退出反向 `ShowPanel` 并还原宽度。浮动 pill 与边缘手柄是 Shell 停靠区上的覆盖层，不写 AvalonDock 模板；`Esc` 用窗口级 `KeyBinding` 并给模态让路；退出必须先于 `ApplyShellMode`。

## 五、工具箱隐藏行为（用户反馈，2026-10-26）

用户把工具箱的图钉按下去，期望右栏收起。实测（`ScadaUiProbe --right-pane-hide`，真窗口）：

- 隐藏前：右列 pane 宽 320、工具箱可见、pane 子项 1；
- 隐藏后：pane 子项 0、`IsVisible=False`、pane 控件 `ActualWidth=0`，工具箱 `IsHidden=True`（进右侧 Hidden 区，竖条可点回来）；
- 证据：`artifacts/render/20261026/right-pane-hide-after.png`——中间文档铺到右边缘，右列确实收成 0。

所以图钉本身是 AvalonDock 的 **auto-hide**：整列收起 + 面板停到右侧竖条，不是关闭。看起来"没隐藏"的三种常见情况：

1. **自由模式**：同一 pane 里属性/图层/图元工具箱仍可见，图钉只隐藏工具箱一个标签，列不会收（这是对的；要整列收起用 B 的专注模式）。
2. **点了 X**：`CanClose=False`，关闭按钮不生效；隐藏要用图钉，不是 X。
3. **切模式**：调试↔组态/自由会按模式重新收敛面板，工具箱会回来；这是模式语义，不是隐藏失效。

如果期望"点一下就把整个右侧检查器收起、并留一个带文字的返回入口"，那是 B 的专注模式（未做），或在右栏加一颗「收起检查器」按钮。当前竖条是 AvalonDock 的 Hidden 入口，图标很小、容易被忽略。

## 六、待拍板

| # | 问题 | 建议 |
| --- | --- | --- |
| Q1 | 先做 A 还是直接做 B？ | **已选 A，已实施（2026-10-26）**；B 作为下一步 |
| Q2 | 若做 B，入口放哪？ | 视觉图像文档标签右侧「⤢ 专注图像」；快捷键 `Ctrl+Shift+F` 可选 |
| Q3 | 控制台默认状态是否按模式固定？ | 调试/自由 = 收起 32，运行 = 展开 240，组态 = 隐藏；用户手动展开后按布局文件记忆 |
| Q4 | 专注模式是否保留顶栏/运行条/状态栏？ | 保留（只隐藏左右栏），否则无法退出/观察运行状态 |
| Q5 | 展开态的「收起」按钮放哪？ | `LogView` + `MonitorView` 工具条右端各一颗；不改 AvalonDock 页签模板 |
| Q6 | 边缘手柄点击后恢复一侧还是两侧？ | **恢复两侧**，点击侧为活动页签；不做半专注态 |
| Q7 | 展开默认高度用 240 还是当前 200？ | **240**（设计基线值）；用户拖过则按记忆高度 |
| Q8 | 工具箱图钉是否改成"整列收起 + 带文字竖条"？ | 保留 per-tab 图钉；另加「收起检查器」按钮或走 B 专注模式，不改 AvalonDock 模板 |

## 七、未做

- **A 已实施**：右栏 300、控制台默认 32、`ConsoleState` 持久化。A 的交互细化（「⌄ 收起」按钮 / `Ctrl+`` / 双击页签条 / 拖拽 120~480 / 摘要区点击）未做。
- **B 未做**：专注图像的入口、浮动 pill、边缘手柄、`Esc` 退出。
- 图像区里的板卡/ROI 是示意图形，不是真实检测图；真实图像按视口"适应"缩放。
- 数值来自 `docs/code-changes/2026-10-26-非全屏图像区尺寸分析.md`（截图 1644×1036 @125% 实测）。

## 八、已知偏差

- 总览图绘图比例 0.8（1310×829 → 1048×663）；交互细化图的控制台裁切 1:1、专注整帧 0.68；字体在截图里约 8~11px，放大查看细节。
- 右栏 320/300 的差别在图上只有 20px，靠每帧底部图例与顶部指标区分。
- 交互细化图的 B2 帧为了说明，把左右边缘手柄画成常显；真实行为是静止只有 4px 分隔线、悬停才出 24×72 标签。
- 设计稿不替代真机像素级验收（R129/R132）。

## 九、VS 式胶囊（第一版已实施）

用户提出：点击一个按钮，让面板变成胶囊悬浮在侧边栏（VS 的 Auto Hide 语义）。结论：**可行，且不需要换停靠库**——AvalonDock 4.74 的 auto-hide 已经是这套机制（图钉取消固定 → 面板进 Hidden 区 → 右边缘竖条 → 悬停/点击弹出）。现在差三件事：

1. **边缘竖条没做成"有标题的胶囊"**：当前是细条/小图标，用户看不出它是"工具箱"，隐藏后找不到回来的入口。
2. **没有明确的"收起为胶囊"按钮**：现在只有图钉，语义不直观；`CanClose=False` 的 X 是禁用的，点它没反应。
3. **收起状态没从模式收敛里独立**：调试模式启动时 `ApplyScadaPanelMode(false)` 会把工具箱重新 Show 回来，所以"隐藏"不持久。

### 9.1 建议的交互（per-tool-window，VS 同款）

- **钉住态**：面板在右栏 300 列里；页签条右端一颗「收起为胶囊」按钮（图钉图标即可）。
- **胶囊态**：右列收成 0；右侧边缘出现 **28~32px 宽 × 96~120px 高**的竖向胶囊，圆角 8，图标 + 竖排标题（工具箱/属性/图层/图元），可带计数徽标；hover 强调边 + tooltip。
- **浮层**：点击胶囊 → 300px 面板从右侧滑出（覆盖画布，不挤压图像）；浮层右上角图钉可钉回列内；点画布空白 / `Esc` → 收回胶囊。
- **多枚胶囊**：每个面板各自一枚，按顺序竖向堆叠；全部收起时右列 0，图像最宽。
- **持久化**：`Layout.xml` 记 `CollapsedPanels="工具箱,图层"`；加载后**在模式收敛之后**应用，用户收起状态优先；切模式不强制展开；「重置布局」清空。
- **与 B 的区别**：B 是整列临时隐藏（看图/调焦）；胶囊是单面板持久收起。两者可共存。

### 9.2 实现落点与风险

- 改 `VisionMaster/Themes/DockThemes/FluentLight.xaml` 的 auto-hide tab 模板（竖排标题 + 圆角 + 计数），不动停靠树；AvalonDock 的 Hidden/Show 已可用。
- Shell 增加「收起为胶囊」命令（`LayoutAnchorable.IsVisible=false` 的现有语义），浮层钉回用 `ShowAnchorable`。
- `LayoutHelper` 增加 `CollapsedPanels` 持久化，并在 `ApplyScadaPanelMode` 之后应用；这是把"用户偏好"从"模式默认"里拆出来的关键。
- 风险：auto-hide 浮层的滑出动画受库限制（可能只能做到即时/淡入）；胶囊宽度会占 28~32px；模式切换是否保留胶囊态需要拍板（建议保留，重置布局恢复默认）。

### 9.3 待拍板

| # | 问题 | 建议 |
| --- | --- | --- |
| Q9 | 胶囊按面板（工具箱/属性/图层/图元各一枚）还是整列一枚？ | **按面板**（VS 语义；用户可只收工具箱） |
| Q10 | 切模式是否保留胶囊态？ | **保留**（用户偏好优先）；「重置布局」恢复模式默认 |
| Q11 | 浮层覆盖画布还是推开画布？ | **覆盖**（VS 行为；图像不重新布局） |
| Q12 | 胶囊尺寸/位置？ | **20×64~100**（VS 样式），右边缘竖向堆叠，圆角 2、无阴影 |

Q9/Q12 已按建议实施；Q10/Q11 未实现（第一版用"点击钉回"，不做覆盖浮层）。

### 9.4 第一版实现（2026-10-26）

**已实施**

- `Shell.xaml` 新增 `InspectorCapsuleRail`：右侧 24px 轨道，20×64~100 胶囊（圆角 2、1px 边、无阴影、9px 常规字重；悬停内侧 2px 主色竖线），竖排标题，点击钉回 300 列。
- `ShellViewModel.InspectorCapsules` / `HasInspectorCapsules` / `ShowInspectorCapsuleCommand` / `UpdateInspectorCapsules`：按模式只列"本应显示但被用户收起"的面板（调试=工具箱，组态=属性/图层/图元，自由=四个，运行=无）。
- `AttachDockWatchers` 监听右侧面板 `IsVisible`，收起/钉回即时更新胶囊。
- `ApplyScadaPanelMode` / `ApplyShellMode` 增加 `respectUserHidden`：启动/布局加载/切文档不把已收起的工具箱强行 Show；显式切模式仍按模式默认恢复。
- 左侧流程导航 + 右侧 4 个面板加 `CanDockAsTabbedDocument="False"`，阻止双击/拖拽把检查器页签变成中央文档；旧布局里已经落成 `LayoutDocument` 的检查器面板由 `EnsureInspectorPanelsAreAnchorables` 在加载时迁回右侧空 pane。
- 自动隐藏（图钉）第三态：`EnumerateContents` 补 `LayoutRoot` 四个 `LayoutAnchorSide`，`IsPanelVisible` 改为 `IsVisible && !IsAutoHidden`，`ShowAnchorable` 先走 `ToggleAutoHide()` 收回；`respectUserHidden` 判据改用 `IsPanelVisible`（否则重启会把图钉收起强行拉回）。
- AvalonDock 用法评审修复（2026-10-26）：恢复布局后 `ApplyPanelPolicies` 补回 `CanDockAsTabbedDocument=False` / `AutoHideWidth=300` / `CanFloat=False`；隐藏记忆只认 `LayoutAnchorablePane`（拒绝文档 pane）；胶囊按面板收回同组其它面板；检查器列宽 260/304/320 → 300。
- 验证：`VisionMaster` 0 错误；`UIThemeSmokeTest` 135/0（含「点胶囊钉回：工具箱回右侧 LayoutAnchorablePane」「图钉自动隐藏后点胶囊」「respectUserHidden 尊重图钉收起」三条）；设计稿 `design-capsule.html` / `evidence/design-capsule-01.png`。

**真 exe 实测（2026-10-26，修复后）**

- 通过：胶囊显示（VS 样式，24px 轨道 / 20×64~100）、图钉收起 → 胶囊出现、点胶囊钉回右侧 300 列、重启后自动隐藏布局与胶囊保留、A/右栏 300。
- 日志证据：`13:45:45 [胶囊] Click 到达` → `[ShowPanel] autoHidden=True` → `auto=False parent=LayoutAnchorablePane` → `visible=True`；`13:48:37` 重启后启动日志没有对工具箱的 `ShowPanel`，说明 `respectUserHidden` 生效。
- 断言：`UIThemeSmokeTest` 135/0（新增图钉自动隐藏 → 点胶囊收回、`respectUserHidden` 两条）；`FlowCanvasChecks` LV4/LV5 已绿；`tools\run_gates.ps1` **14/14 全绿**（`FlowCanvasChecks` 3704/0）。
- 修复细节与并发会话说明见 `docs/code-changes/2026-10-26-VS式胶囊实测修复（自动隐藏第三态与点击链路）.md`；失败现场见 `docs/code-changes/2026-10-26-VS式胶囊实测（未通过）.md`。

**未做 / 偏差**

- **点击胶囊是"钉回 300 列"，不是 300 覆盖浮层**。AvalonDock 的 auto-hide 页签是库内部控件，实测 `LayoutAnchorable.TabItem=null`、视觉树里只有左栏页签，拿不到 auto-hide 的 `LayoutAnchorableTabItem` 实例换模板；第一版改用 Shell 覆盖层胶囊，放弃库的 auto-hide 浮层。
- 没有滑出动画、没有 `Esc`/点外部收回（胶囊点击即钉回）。
- 切模式按模式默认恢复（`SetShellMode` 传 `respectUserHidden=false`），胶囊不跨模式保留；Q10 未实现。
- 胶囊占 24px 轨道；计数徽标未做。
- 第一版按 VS 样式收窄：20×64~100、圆角 2、无阴影、9px 常规字重；悬停内侧 2px 主色竖线；标题旋转 -90°（VS 自下而上），不是 CSS 的 upright 竖排。
- auto-hide 的 6px 竖条被胶囊轨道覆盖，但库的 auto-hide 状态仍在；胶囊点击用 `ShowPanel` 钉回。
