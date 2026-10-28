# 运行态顶栏设计基线（scada-runtime-topbar）

| 项 | 内容 |
| --- | --- |
| 版本 | v1（草案，**待确认**） |
| 起草日期 | 2026-10-28 |
| 确认日期 / 确认人 | 待填（未确认前不得改 WPF，R127） |
| 覆盖范围 | `VisionMaster/Views/ScadaRuntimeWindow.xaml` 的**第 0 行顶栏**（窗口级固定 chrome）与底栏的信息重排 |
| 不覆盖 | 画面内容、报警历史抽屉（`ScadaAlarmHistoryView`）、弹窗层、软键盘、`AlarmBannerElement` 页面级图元的外观 |
| 设计媒介 | 本目录 `design.html`（可点开评审；不进构建、不部署，R126） |

## 1. 这一版解决什么

运行态顶栏目前只有"页面菜单 / 返回 / 声音 / 报警历史 / 停止运行"，没有任何运行信息。操作员站在设备前，
最先要知道的三件事（有没有报警、这是哪条线、现在谁在操作）都得靠翻面板或看画面角落。

v1 把顶栏改成**固定四段**（所有画面共用同一条，页面元素盖不到）：

```
[ 报警滚动条（弹性） ]  [ 可编辑标题 ]  [ 当前用户 ]  [ 时间 ]   ……  [ ← ][ ♪ 声音 ][ 报警历史 ][ ■ 停止运行 ]
```

同时它还是无边框窗口的**拖动区**：只读段不打标，自动成为 `WindowChrome` 标题栏；只有右侧 4 个控件标
`IsHitTestVisibleInChrome="True"`。

## 2. 已定的取舍（都经过用户拍板）

| # | 决定 | 理由 |
| --- | --- | --- |
| 1 | 顶栏是窗口级 chrome，不是页面级图元 | 只有窗口级才能同时承担拖动区；页面级会随画面缩放跑位 |
| 2 | 标题是**自由文本**，不是方案文件名 | 现场要写"三号线 · 白班"这类信息，文件名表达不了；空值整段不占位 |
| 3 | 页面名与页面菜单**下线**，返回保留为图标按钮 | 页面切换由画面内按钮承担（`ScadaActionType.Navigate`）；`GoBack` 只有顶栏这一个调用方，必须留 |
| 4 | 停止运行**始终显示**，不做显隐配置 | 它是默认形态（无边框最大化）下的唯一出口；隐藏后操作员只能 Alt+F4 |
| 5 | 不做：节拍、设备在线、未确认计数徽标之外的统计、顶栏加高 | 用户明确不做；画面里的"结果统计"已覆盖产量/良率 |
| 6 | 滚动条锁死（不可点） | 属可拖区，整条顶栏都能拖；打开报警历史走右侧按钮 |

## 3. 令牌映射

WPF 侧一律引 `StaticResource`，视图里不出现 `#RRGGBB`（R140）。全部沿用 `UI/Controls/Themes/Colors.xaml`
的「组态运行态窗口」一节，**不新增令牌**：

| 用途 | 令牌 |
| --- | --- |
| 顶栏底 / 分隔线 | `ScadaRunBarBrush` / `ScadaRunBarLineBrush` |
| 正文 / 次要文字 | `ScadaRunTextBrush` / `ScadaRunMutedTextBrush` |
| 控件底 / 描边 / 悬停 | `ScadaRunControlBrush` / `ScadaRunControlBorderBrush` / `ScadaRunControlHoverBrush` |
| 报警历史按下态 | `ScadaRunToggleOnBrush` + `ScadaRunAccentBrush` |
| 停止运行 | `ScadaRunStopBrush` / `ScadaRunStopBorderBrush` |
| 严重度色 | 报警定义自身的严重度色（`AlarmBannerElement.CriticalColor` / `WarningColor` / `InfoColor`） |

## 4. 已知偏差（复刻时允许，但必须写进验收表）

1. **字体度量**：稿内用 `Microsoft YaHei UI` 12px；WPF 12 DIP 的行高略高，26px 内单行仍放得下（无偏差动作）。
2. **滚动条动画**：HTML 稿是静态帧，真实实现为 2.5 s/条的横向位移（复用 `AlarmBannerScroll` 的取模口径）；
   稿里无法体现节奏，验收时以"每条停 2.5 s、不跳行、不抖动"为准。
3. **标题截断**：稿内是 CSS `text-overflow: ellipsis`，WPF 用 `TextTrimming="CharacterEllipsis"`，
   截断位置可能差 1~2 字符。
4. **系统标题栏**：`WindowChrome.CaptionHeight=26` 后，双击顶栏会最大化（Windows 行为）。
   本稿不压制该行为；如需压制，另开一条（`ClickCount==2` 时 `Handled=true`）。
5. **触摸目标**：26 DIP 低于 40 DIP 的触摸建议值。若现场纯触摸，另开 v2 加高到 32~40。

## 5. 复刻清单（WPF 侧要改的地方，供实施时对照）

| # | 文件 | 改动 |
| --- | --- | --- |
| 1 | `ScadaRuntimeWindow.xaml` | `WindowStyle="None"` + `WindowChrome`（`CaptionHeight=26` / `ResizeBorderThickness=6` / `GlassFrameThickness=1`）；顶栏重排为四段；返回降级为 26×26 图标按钮 |
| 2 | 同上（3 个 Style） | `PageMenuButton` / `AlarmToggleButton` / `StopButton` 各加 `WindowChrome.IsHitTestVisibleInChrome=True` |
| 3 | `ScadaRuntimeWindow.xaml.cs` | `ApplyRunWindowMode` 的独立形态改 `WindowStyle=None`（其余落点/尺寸/任务栏不变）；新增 1 s 时钟；标题/用户绑定 |
| 4 | `Scada\Models\ScadaDocument.cs` | 新增方案级字段 `RunTitle`（string，缺省空） |
| 5 | `ScadaPropertyViewModel` 的方案级分组 | 增加「运行标题」一行（与「启动画面」并列） |
| 6 | `ScadaChecks` | 独立形态断言改 `WindowStyle=None`；新增顶栏控件打标扫描、`CaptionHeight>0`、标题空态不占位 |
| 7 | `ScadaRuntimeWindow.xaml` 底栏 | 删除「操作者」，只留 视口 / 适配 / 耗时 |

## 6. 验收判据（R132：由用户裁定）

- 1920 与 1549 两个宽度下：四段都在一行内、无换行、无重叠、无截断（标题超长除外）。
- 拖动：按住滚动条 / 标题 / 用户 / 时间任一区域都能拖动窗口；按 4 个按钮不拖动、点击生效。
- 报警态：有报警时滚动条显示严重度色点 + 名称 + 条件；无报警时显示"无未确认报警"。
- 停止运行按钮任何状态下都可见。
