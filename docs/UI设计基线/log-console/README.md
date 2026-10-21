# 控制台（日志）· 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | **v1** |
| 日期 | 2026-10-21 |
| 确认日期 / 人 | 待用户确认 |
| 覆盖范围 | `VisionMaster\Views\LogView.xaml`（过滤工具条 + 日志区）+ `UI\Controls\Themes\LogConsole.xaml`（日志控件皮肤） |
| 设计文件 | `design.html`（自包含，无外部依赖；含默认态 / 无结果态 / 空态，可点筹码与搜索框） |
| 证据目录 | `evidence/`（评审后补渲染 PNG） |
| 状态 | **待用户确认**；本轮只出设计稿，未改任何 WPF 代码 |

## 1. 现状问题（改前）

| # | 问题 | 证据 | 影响 |
| --- | --- | --- | --- |
| 1 | 过滤工具条全手写十六进制色 | `LogView.xaml` 51 处 `#RRGGBB`（`#F1F4F8`、`#DCE3EB`、`#5A6472`、`#005FB8`…） | 换肤不生效；与 `Dialog*` 令牌体系两套颜色 |
| 2 | 搜索框需要整份自定义模板才"看得出是输入框" | `LogView.xaml:19-58` 的注释与 `OverridesDefaultStyle` | Fluent 隐式样式把 TextBox 画成透明底 + 一条底线，在这条浅色工具条上等于空地 |
| 3 | 等级筹码选中态靠 5 组 `MultiDataTrigger` 硬堆 | `LogView.xaml:244-297`（5 组 × 4 Setter） | 加一级要再写一组；颜色散在视图里 |
| 4 | 日志行硬编码 20+ 色 | `LogConsole.xaml:62-209`（`#FAFAFA`、`#333333`、`#0078D4`、`#107C10`、`#D83B01`、`#D13438`…） | 与工具条同族问题 |
| 5 | 无空态 / 无结果态 | 当前只有一张 ListBox，无数据时是白板 | 用户分不清"没日志"和"筛没了" |
| 6 | 错误行没有整行语义 | 只有文字变色 + 加粗 | 一屏 40 行时，红色文字混在正文里扫不出来 |
| 7 | 无"显示 N / 共 M"计数 | 筹码上只有各级条数 | 用户不知道当前筛选命中了多少 |
| 8 | 无"打开日志文件夹"入口 | — | 完整历史在 `Logs\` 目录，但界面上没有路 |

## 2. 设计要点

### 2.1 布局

```text
┌──────────────────────────────────────────────────────────┐
│ 搜索 [⌕ 正文或来源          ✕]        □ 跟随最新          │  32px 控件，行间距 8
│ 等级 [全部 128] [信息 96] [成功 21] [警告 7] [错误 4]     │  26px 筹码，间距 6
├──────────────────────────────────────────────────────────┤
│ ▍ 19:04:12.318  [相机]  相机 camera-01 连接成功…          │  3px 状态条 + 时间 + 来源 + 正文
│ ▍ 19:04:13.050  [流程]  流程「主检测」执行完成，OK        │
│ …                                                        │
├──────────────────────────────────────────────────────────┤
│ 显示 96 / 共 128 条 · 面板保留最近 2000 条   打开日志文件夹│  28px 状态条
└──────────────────────────────────────────────────────────┘
```

- **两行工具条**：第一行搜索 + 跟随最新，第二行等级筹码。筹码摊开是"一眼看全 5 个等级 + 各自条数"；挤一行时窄面板会竖着堆成 5 行（现状注释已记录该问题）。
- **日志行四列**：`3px 状态条 | 84px 时间 | 78px 来源 | 正文`。时间用等宽 + `tabular-nums`，保证毫秒位竖对齐。
- **来源徽标可空**：`LogService` 目前不填 source，为空时整块收起但**保留列宽**，所以有无来源的行正文起点一致（沿用现状口径）。

### 2.2 等级语义（颜色只表达等级，不做装饰）

| 等级 | 状态条 | 正文 | 整行底 |
| --- | --- | --- | --- |
| 信息 Info | `--info` 强调色 | `--text-1` | 交替行（白 / `--surface-2`） |
| 成功 Success | `--success` | `--success-text` | 交替行 |
| 警告 Warning | `--warning` | `--warning-text` | 交替行 |
| 错误 Error | `--danger` | `--danger-text` + 600 字重 | **`#FEF2F2` 常驻**（不受交替影响） |

错误行给整行底色，是为了"一屏 40 行时红色错误一眼定位"——只靠文字变色在大段正文里扫不出来。

### 2.3 筹码（等级筛选）

- **未选中**：白底 + 1px 描边 + 本级色圆点 + 本级色计数。
- **悬停**：描边加深到 `--text-4`。
- **选中**：整枚填成本级色，文字转白，圆点转白，计数转 78% 白。
- **「全部」不带圆点**：它是"不限"，不属于某一级。
- **焦点**：2px 强调色外环 + 2px offset（R115 要求键盘焦点可见）。

选中态从"5 组 MultiDataTrigger"改为**一个 `aria-pressed` + 5 条 `[data-level]` 规则**；WPF 侧对应"容器样式 + `IsSelected` 触发器 + 按 `Value` 换色"三件套，不再逐级硬堆。

### 2.4 三种状态

| 状态 | 触发 | 视觉 |
| --- | --- | --- |
| 有数据 | 默认 | 日志表 + 底部"显示 N / 共 M 条" |
| **无结果** | 搜索/等级筛掉全部 | 放大镜图标 + "没有匹配的日志" + **「清空筛选条件」按钮**（一键回到有数据） |
| **空态** | 真的没有日志（刚启动 / 已清空） | 列表图标 + "还没有日志" + 说明完整历史在 `Logs\` 目录 |

两种"空"必须分开：现状是一个白板，用户无法判断是"没日志"还是"筛没了"。无结果态给出口按钮，空态给去向说明。

### 2.5 搜索框

- 32px 高、白底、1px `--border-strong` 描边、6px 圆角。
- 左侧放大镜图标（12px，`--text-4`），占位文字 `正文或来源`。
- 有内容时右侧出现 `✕` 清空按钮（22×22，`Padding="0"`，符合 R68）。
- 聚焦：描边转 `--accent` + 3px 15% 光环。
- **保留** `UpdateSourceTrigger=PropertyChanged` 与控件侧 200ms 防抖（契约不变）。

### 2.6 窄面板（460）

- 搜索框从固定 220 收成弹性宽度，跟随最新换行到右侧。
- 筹码 `flex-wrap` 自然折行（460 宽时 5 枚折成两行）。
- 时间列收窄到 78，来源列保留但更早截断。

## 3. 令牌映射

| 设计变量 | 值 | WPF 资源键 | 用途 |
| --- | --- | --- | --- |
| `--accent` | `#0EA5E9` | `DialogAccentBrush` | 聚焦描边、信息级状态条 |
| `--accent-dark` | `#0284C7` | `DialogAccentDarkBrush` | 筹码选中底（全部/信息） |
| `--accent-light` | `#E0F2FE` | `DialogAccentLightBrush` | 预留（行选中） |
| `--accent-soft` | `#F0F9FF` | `DialogAccentSoftBrush` | 预留 |
| `--accent-border` | `#BAE6FD` | `DialogAccentBorderBrush` | 预留 |
| `--accent-text` | `#0369A1` | `DialogAccentTextBrush` | 路径链接、状态条链接 |
| `--surface` | `#FFFFFF` | `DialogSurfaceBrush` | 面板底、输入框底 |
| `--surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` | 交替行、底部状态条 |
| `--background` | `#EEF2F7` | `DialogBackgroundBrush` | 设计稿外壳（不进视图） |
| `--border` | `#E2E8F0` | `DialogBorderBrush` | 工具条分隔、行分隔 |
| `--border-light` | `#EEF2F7` | `DialogBorderLightBrush` | 行内发丝线、来源徽标底 |
| `--border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` | 输入框描边、筹码描边 |
| `--text-1` | `#0F172A` | `DialogTextPrimaryBrush` | 正文 |
| `--text-2` | `#475569` | `DialogTextSecondaryBrush` | 筹码文字、来源文字 |
| `--text-3` | `#64748B` | `DialogTextTertiaryBrush` | 时间、字段标签 |
| `--text-4` | `#94A3B8` | `DialogTextMutedBrush` | 占位符、计数 |
| `--success` / `--success-text` | `#22C55E` / `#15803D` | `DialogSuccessBrush` / `DialogSuccessTextBrush` | 成功级状态条 / 正文 |
| `--warning` / `--warning-text` | `#F59E0B` / `#B45309` | `DialogWarningBrush` / `DialogWarningTextBrush` | 警告级 |
| `--danger` / `--danger-text` | `#EF4444` / `#B91C1C` | `DialogDangerBrush` / `DialogDangerTextBrush` | 错误级 |
| `--row-hover` | `#F1F5F9` | `DialogRowHoverBrush` | 行悬停 |

**需要新增的令牌（仅 1 个）**：

| 建议键 | 值 | 用途 |
| --- | --- | --- |
| `DialogDangerRowBrush` | `#FEF2F2` | 错误行整行底（与 `DialogDangerSubtleBrush` 同值，但语义是"行底"而非"提示底"；若裁定复用 `DialogDangerSubtleBrush` 则无需新增） |

> 除上表外，本设计不新增任何颜色键；现状 `LogView` 的 51 处与 `LogConsole` 的 20+ 处硬编码全部由既有 `Dialog*` 令牌承接。

## 4. 复刻约束

1. **不改数据契约**：`SystemLogs` / `SearchText` / `LevelOption` / `FilterLevel` / `AutoScroll` / `MaxItems=2000` 全部保留。
2. **不改 `LogItem` 模型**：`Time` / `Level` / `Source` / `Message` 四字段不变；来源为空时收起徽标但保留列宽。
3. **`LogConsole` 是共享控件**（`UI\Controls\Themes\LogConsole.xaml`），改它会影响所有宿主。本设计只改**颜色与行模板**，不改 `AutoScroll` 智能滚动逻辑、不改 `MaxItems` 裁剪、不改虚拟化设置（`IsVirtualizing` + `Recycling`）。
4. **视图 XAML `hex=0`**（R140）：改完后 `LogView.xaml` 与 `LogConsole.xaml` 都不得出现 `#RRGGBB`；`hardcoded-colors.baseline.txt` 里这两项应清零并删行。
5. **图标按钮 30×30 + `Padding="0"`**（R68）：搜索清空按钮实际 22×22（在 32px 输入框内），必须显式写 `Padding="0"`，否则 Fluent 隐式样式的 `11,5,11,6` 会把字形削掉。
6. **焦点与 ToolTip**：筹码、清空按钮、底部链接都要挂 `DialogFocusVisual` 与 `DialogToolTipStyle`（R135）。
7. **不造死按钮**：「打开日志文件夹」需要现有命令支撑；若当前没有该命令，则**本轮不画这个按钮**，改为文字说明路径（见 §6 未纳入项）。

## 5. 已知偏差（Web vs WPF，预判）

1. **字体度量**：Web 用 Segoe UI Variable / 微软雅黑；WPF 用微软雅黑。等宽列（时间）WPF 用 Consolas，字宽与 Cascadia Mono 略有差异。
2. **滚动条**：Web 覆盖式 10px；WPF 占位约 17px。窄面板（460）下正文可用宽度会少约 17px。
3. **行底交替 + 错误行底色叠加**：Web 用 `nth-child` 覆盖；WPF 需要在 `ItemContainerStyle` 里同时判 `AlternationIndex` 与 `Level`，用 `MultiDataTrigger` 保证错误行不被交替色盖掉。
4. **阴影**：Web 面板有两层柔和阴影；WPF 停靠面板无窗口级阴影，保留 1px 描边。
5. **筹码折行**：Web 用 `flex-wrap`；WPF 用 `WrapPanel`，换行点会因字体度量略有差异。
6. **聚焦光环**：Web `box-shadow: 0 0 0 3px rgba(...)`；WPF 用 `DialogFocusVisual` 的 2px 描边 + 2px offset，光环不发光。

## 6. 未纳入本版

| 项 | 原因 |
| --- | --- |
| 日志分组（`GroupBy`） | `LogConsole` 有分组模板但当前无人设置 `GroupBy`；启用它需要新的交互决策（按来源分？按等级分？），不在纯视觉改造范围 |
| 日志导出 / 复制选中行 | 需要新命令，属功能新增 |
| 「打开日志文件夹」按钮 | 需要确认是否已有命令；若没有则本轮只保留路径文字 |
| 日志级别第四档（Debug/Trace） | `LogLevel` 只有 Info/Success/Warning/Error 四档，不加档 |
| 时间列宽自适应 | 固定 84px（`HH:mm:ss.fff` 的最坏宽度）；改成自适应会在滚动时抖动 |

## 7. 验证计划（复刻后）

| 层 | 判据 |
| --- | --- |
| 静态 | `LogView.xaml` / `LogConsole.xaml` 的 `hex=0`；`hardcoded-colors.baseline.txt` 两项删行 |
| 运行时 | `UIThemeSmokeTest` 资源合并无异常；`LogView` 实例化后 `LogConsole` 存在且 `ItemsSource` 绑定 |
| 功能回环 | 搜索 → 列表过滤；筹码切换 → `FilterLevel` 变化；「跟随最新」→ `AutoScroll` 双向 |
| 渲染 | 默认态 / 无结果态 / 空态 / 460 窄面板 四张 PNG |
| 回归 | `FlowCanvasChecks` 0 失败 |

## 8. 变更规则

确认后只读。需要调整时升 `v2`，并同步：`design.html`、本 README 版本号、令牌映射表、`docs/UI设计基线/README.md` 索引行。
