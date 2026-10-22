# 监视面板 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | **v1** |
| 日期 | 2026-10-21 |
| 确认日期 / 人 | 待用户确认 |
| 覆盖范围 | `VisionMaster\Views\MonitorView.xaml`（添加监视工具条 + 分组监视列表 + 底部计数） |
| 设计文件 | `design.html`（自包含；含默认态 / 空态 / 窄面板，分组可折叠） |
| 证据目录 | `evidence/`（评审后补渲染 PNG） |
| 状态 | **用户已确认（2026-10-22）→ 已复刻**；落地记录 `docs/code-changes/2026-10-22-第1批四视图Web化复刻（日志监视流程条驾驶舱）.md` |

## 1. 现状问题（改前）

| # | 问题 | 证据 | 影响 |
| --- | --- | --- | --- |
| 1 | 工具条按钮是 Element-UI 绿 / 红实底 | `MonitorView.xaml:130/138`（`#67C23A` / `#F56C6C`） | 与全局 `Dialog*` 语义色冲突；"清空"用红色实底，看起来像危险操作主按钮 |
| 2 | 23 处硬编码色 | `MonitorView.xaml`（`#909399`、`#303133`、`#409EFF`、`#F5F7FA`…） | 换肤不生效 |
| 3 | 搜索框是"无边框透明 TextBox" | `MonitorView.xaml:72-85`（`BorderThickness="0"` + `Background="Transparent"`） | 看不出这是输入框，用户不知道能打字 |
| 4 | 联想弹层无空态、无分组 | `MonitorView.xaml:99-120` | 输入无匹配时是一片空白，用户以为坏了 |
| 5 | 分组头只有文字 + `(N)` | `MonitorView.xaml:162-179` | 计数与标题同色同字号，扫不出来 |
| 6 | 值列无等宽、无右对齐 | `MonitorView.xaml:249-268`（`FontWeight="Bold"` 但非等宽） | 数值上下跳动，无法纵向对比 |
| 7 | 失效行只有透明度 0.75 + 删除线 | `MonitorView.xaml:206-209`、`:232-236` | 在密集列表里几乎看不出来 |
| 8 | 移除按钮只有 20×20 且常驻 | `MonitorView.xaml:280-296`（`Width=20 Height=20`） | 太小；常驻的红色垃圾桶让整列都是红色噪音 |
| 9 | 无空态 | — | 没有监视项时是白板 |

## 2.0 设计画布 vs 宿主尺寸

| 项 | 值 | 来源 |
| --- | --- | --- |
| 设计画布 | 640 × 520（主档）、420（窄档） | 本稿 |
| 宿主落点 | Shell 底部控制台抽屉的「监控」页签 | `Shell.xaml:1102`（`DockHeight="200"`） |
| **高度矛盾** | 设计 520，宿主展开态 240 | 与 `log-console` 同一处境 |
| 复刻口径 | 240px 下：添加工具条 32px + 列头 28px + 列表约 150px（约 4 行）；**移除按钮常驻可见**（hover 显隐在小高度里不好用），底栏合并进工具条 | 需要按 `ActualHeight` 降级 |

## 2. 设计要点

### 2.1 布局

```text
┌───────────────────────────────────────────────────────────────┐
│ 添加监视 [匹配                        ▾] [添加] [全部变量] [清空] │  32px
├───────────────────────────────────────────────────────────────┤
│ 名称                        类型      当前值   方向             │  28px 列头
│ ▾ 全局变量  2                                                  │  34px 组头
│   Global.OKCount            Int32        1485  全局       ✕   │  36px 行
│   Global.MatchThreshold     Double       0.85  全局       ✕   │
│ ▾ 算子输入  1                                                  │
│   模板匹配_0.InputImage      HImage      HImage  输入       ✕   │
│ ▾ 算子输出  4                                                  │
│   模板匹配_0.Score          Double      0.932  输出       ✕   │
│   模板匹配_0.Angle          Double  — 目标不存在 —  输出   ✕   │  ← 失效行
├───────────────────────────────────────────────────────────────┤
│ ● 1 项失效                              监视项 7              │  28px 状态条
└───────────────────────────────────────────────────────────────┘
```

- **列宽**：`minmax(0,1fr) | 84 | 96 | 48 | 30`。名称列吃掉剩余宽度并省略号截断；类型/值/方向固定，保证跨行对齐。
- **组头 34px**：折叠箭头（9px）+ 标题（12px 600）+ 计数徽标（11px，灰底圆角）。整行可点。
- **行 36px**：比现状的"自适应高度"更紧凑，一屏能多看 3~4 行。
- **失效行**：整行 `--danger-subtle` 底 + 名称删除线 + 值换成「— 目标不存在 —」+ 移除按钮常驻可见。**不靠透明度**——透明度在密集列表里几乎看不出。

### 2.2 添加监视（核心交互）

- **输入框 32px**：白底 + 1px 描边 + 6px 圆角，聚焦转强调色 + 光环。右侧 `▾` 只是提示"可下拉"，不改变输入行为。
- **联想下拉**：贴输入框下沿 2px，最大高 260px，圆角 8px + 两层阴影。
  - **按来源分组**：`算子端口` / `全局变量` 两个小标题（11px，`--text-4`）。
  - **每项 32px**：类型徽标（`out`/`in`/`gv`，等宽 10px）+ 名称（省略号）+ 悬停 `--row-hover`。
  - **键盘**：上下键移动高亮，Enter 添加，Esc 关闭（与现状 `AddWatchCommand` 的 Enter 行为一致）。
  - **空态**：无匹配时显示「没有匹配项」+ 一句提示，不留白板。
- **三个按钮的语义分层**：
  | 按钮 | 语义 | 样式 |
  | --- | --- | --- |
  | 添加 | 主操作 | 实底 `--accent-dark` 白字 |
  | 全部变量 | 批量次要 | 白底描边 |
  | 清空 | 破坏性但可恢复 | 白底红字描边（**不用红色实底**） |

  现状把「清空」画成红色实底主按钮，是"最危险的动作最显眼"，与 R68 家族同一类问题。

### 2.3 方向徽标

| 方向 | 底色 | 文字色 |
| --- | --- | --- |
| 全局 | `#FFFBEB`（暖） | `--warning-text` |
| 输入 | `--accent-soft` | `--accent-text` |
| 输出 | `#ECFDF5`（冷绿） | `--success-text` |

三色区分是为了"扫一眼就知道这个值是喂进去的还是产出的"，同时不占用状态语义色（绿/黄/红仍只表达健康度）。

### 2.4 值列

- **等宽 + `tabular-nums` + 右对齐**：数值纵向对齐，小数点成列。
- 失效行例外：换成非等宽的中文占位符「— 目标不存在 —」，字体回到正文族——它是说明文字不是数值。
- 数组沿用现有格式化器输出：≤16 个元素全量展开（`[312.5, 480.2, 88.9, 120, 42.1]`），超过 16 个才追加 `, …共N个`（`WatchPortWrapper.cs:38` 的 `MaxElements = 16`、`:63` 的 `arr.Length > shown` 判据）。超长时省略号截断 + ToolTip 全文。

### 2.5 空态

三行引导，直接告诉用户"输入什么"：

```text
· 输入 匹配 找算子，选 算子.端口
· 输入 Global. 列出全部全局变量
· 回车添加第一个联想项
```

比一句"暂无数据"有用——这个面板的全部价值在"怎么加第一个监视项"。

### 2.6 底部状态条

- 左侧：失效项计数（红点 + `1 项失效`），无失效时整段隐藏。
- 右侧：`监视项 7`（数字等宽加粗）。

失效计数放状态条而不是组头，是因为失效项可能散在三个分组里，组头只显示本组数量。

## 3. 令牌映射

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--accent` | `#0EA5E9` | `DialogAccentBrush` |
| `--accent-dark` | `#0284C7` | `DialogAccentDarkBrush` |
| `--accent-light` | `#E0F2FE` | `DialogAccentLightBrush` |
| `--accent-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--accent-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--accent-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--border` | `#E2E8F0` | `DialogBorderBrush` |
| `--border-light` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` |
| `--text-1` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--text-3` | `#64748B` | `DialogTextTertiaryBrush` |
| `--text-4` | `#94A3B8` | `DialogTextMutedBrush` |
| `--success-text` | `#15803D` | `DialogSuccessTextBrush` |
| `--success-soft` | `#ECFDF5` | `LogicForSoftBrush`（输出方向徽标底） |
| `--warning-text` | `#B45309` | `DialogWarningTextBrush` |
| `--warning-soft` | `#FFFBEB` | `LogicWhileSoftBrush`（全局方向徽标底） |
| `--danger` / `--danger-text` | `#EF4444` / `#B91C1C` | `DialogDangerBrush` / `DialogDangerTextBrush` |
| `--danger-subtle` | `#FEF2F2` | `DialogDangerSubtleBrush` |
| `--row-hover` | `#F1F5F9` | `DialogRowHoverBrush` |

**不需要新增任何令牌。** 方向徽标的两个底色复用逻辑容器族（`LogicForSoftBrush = #ECFDF5`、`LogicWhileSoftBrush = #FFFBEB`，见 `Colors.xaml`），语义上它们是"分类色底"而非"成功/警告提示底"，复用比新增更合适——新建 `DialogSuccessSoftBrush` 会在库里多出一对同值异名的键（R141 明令禁止）。

> 审查核实这两个值在 `Colors.xaml` 已有同值键，故本版改为复用。另修正审查抓到的两处野色：主按钮 hover 原为 `#026AA7`（库中不存在）→ 改 `DialogAccentTextBrush`；失效行 hover 原为 `#FDE8E8`（不存在）→ 改为同底色 + 1px 内描边，不引入第二个红。

## 4. 复刻约束

1. **不改数据契约**：`DisplayPorts` / `SuggestedItems` / `GroupedPorts` / `SearchText` / `IsDropDownOpen` / `SelectedSuggestion` / `RemoveCommand` / `ClearAllCommand` / `QuickAddCommand` / `AddWatchCommand` 全部保留。
2. **不改分组机制**：`GroupedPorts` 用 `PropertyGroupDescription(nameof(Direction))` + `DirectionToGroupConverter`（全局 0 / 输入 1 / 输出 2）。设计稿的分组标题来自 `GroupKeyToLabelConverter`，不改这两个转换器。
3. **不改 `WatchPortWrapper` 的只读属性**：`DisplayName` / `Direction` / `TypeName` / `DisplayValue` / `IsMissing` / `StatusTooltip` 只读消费。
4. **需要新增的三件事（本版设计的前提，必须在复刻时一并落地）**：
   | # | 要新增的 | 为什么 | 现有状况 |
   | --- | --- | --- | --- |
   | ① | **失效项计数 `MissingCount`** | 底栏"1 项失效"是跨 `DisplayPorts` 的 `IsMissing` 聚合 | 现有只有 `DisplayPorts.Count`（`MonitorView.xaml:314`）；`IsMissing` 是每项自己的属性（`WatchPortWrapper.cs:134-145`）。实现需订阅每项的 `PropertyChanged`（`AGENTS.md` §31/R140 的通知义务） |
   | ② | **`DisplayText` 拆成「名称 + 类型徽标」两段** | 设计稿的名称列与 `out`/`in`/`gv` 徽标分列显示 | 现状 `DisplayText = "名 [输出]"`（`MonitorViewModel.cs:301/306/314/321/328`）是拼好的单串。`IsInput` / `IsGlobalVariable` 字段够用，但**必须改构造串或改绑定**——这是对 VM 的改动，不是纯视图改动 |
   | ③ | **HImage 值格式化器（可选）** | 设计稿如实把 HImage 画成 `HImage`（`ToString()` 结果） | `WatchPortWrapper` 只注册了 `ArrayWatchValueFormatter`（`:94`）；HImage 分支在 `:33-34` 只是"可注册"的设想。若要显示 `640×480`，需新增格式化器并调 `RegisterFormatter` |
5. **联想下拉仍是 `Popup`**：`IsOpen="{Binding IsDropDownOpen}"`、`StaysOpen="False"`、宽度绑输入框 `ActualWidth` 均保留。**不要改成 `ComboBox`**——`IsDropDownOpen` 是 VM 属性，改成 ComboBox 会引出"输入即过滤"与"下拉选中"两套状态打架。
6. **视图 XAML `hex=0`**：改完后 `MonitorView.xaml` 的 23 处清零，`hardcoded-colors.baseline.txt` 对应行删除。
7. **图标按钮 `Padding="0"`**（R68）：移除按钮 24×24 必须显式写 `Padding="0"`；设计稿 CSS 已对应。
8. **焦点与 ToolTip**：按钮、移除、组头都要 `DialogFocusVisual` + `DialogToolTipStyle`（R135）。设计稿的组头已从 `div` 改为 `button`，键盘可达。
9. **不造死按钮**：三个按钮都接现有命令；不加"导出监视列表""暂停刷新"等新功能。

## 4d. 状态矩阵与三态（R115 / R116）

**五态（逐控件）**：

| 控件 | Normal | Hover | Pressed | Focus | Disabled |
| --- | --- | --- | --- | --- | --- |
| 添加（主） | `--accent-dark` 实底 | `--accent-text` 实底 | 同 hover（实底按钮不再压暗） | 2px 强调色环 | 无候选时可禁用（半透明 + `not-allowed`） |
| 全部变量 / 清空 | 白底 + 描边 | `--row-hover` 底 + 描边加深 | `--border-light` 底 | 2px 强调色环 | 不适用（恒可用） |
| 组头 | `--surface-2` 底 | `--row-hover` 底 | `--border-light` 底 | 2px 强调色内环 | 不适用 |
| 移除 ✕ | 透明（悬停/聚焦才显形） | `--danger-subtle` 底 + 红字 | 同 hover | 2px 强调色环 | 不适用 |
| 联想下拉项 | 透明 | `--row-hover` 底 | 系统默认 | 由上下键高亮（`.active`） | 不适用 |

> **移除按钮的 Hover 显隐**：CSS 用 `opacity: 0 → 1`；WPF 侧**必须**改用 `Visibility` 切换或让 `Opacity=0` 时 `IsHitTestVisible=False`——否则"看不见但点得到"（见 §5 第 5 条）。

**三态（R116）**：

| 态 | 是否适用 | 理由 / 视觉 |
| --- | --- | --- |
| 加载 | **不适用** | 监视项来自内存集合（`DisplayPorts`），值由运行期推送（`CurrentValue` setter），没有"列表加载中"阶段 |
| 空 | **适用** | 「还没有监视项」+ 三行输入引导（`design.html` 的 p2） |
| 无匹配 | **适用**（本设计新增） | 联想下拉无结果：「没有匹配项」+ 一句提示（p4） |
| 错误 | **部分适用** | 单条监视目标失效 → 失效行视觉（`--danger-subtle` 底 + 删除线 + 「— 目标不存在 —」）；**没有面板级错误态**，因为集合读取不会失败 |

## 5. 已知偏差（Web vs WPF，预判）

1. **字体度量**：值列 WPF 用 Consolas，与 Web 的 Cascadia Mono 字宽略异；`tabular-nums` 在 WPF 用 `Typography.NumeralAlignment="Tabular"`。
2. **滚动条**：WPF 占位约 17px；640 宽面板下名称列会少约 17px 可用宽度。
3. **组头折叠箭头**：Web 用 `transform: rotate`；WPF 用 `Path` + `RotateTransform`，动画曲线 150ms `CubicEase`。
4. **联想下拉阴影**：Web 两层 `box-shadow`；WPF `Popup` 无窗口级阴影，改 1px 描边 + 单层 `DropShadowEffect`。
5. **移除按钮的 hover 显隐**：Web 用 `opacity` 过渡；WPF 用 `Trigger` 切 `Visibility`（避免 `Opacity=0` 仍可点击），键盘聚焦时必须显示。
6. **`minmax(0,1fr)`**：WPF `Grid` 的星号列在内容超长时不会自动收缩，需给名称列 `TextTrimming="CharacterEllipsis"` + 父级 `MinWidth=0` 等价处理。

## 6. 未纳入本版

| 项 | 原因 |
| --- | --- |
| 监视项排序 / 拖动重排 | 当前分组顺序由 `Direction` 固定（全局→输入→输出），组内顺序即添加顺序；改成可拖需要新的持久化字段 |
| 值变化高亮（闪一下） | 需要新增"值变更时间戳"与动画；属功能增强，先记录 |
| 历史曲线 / 值趋势 | 需要环形缓冲与新面板，属功能新增 |
| 值列宽自适应 | 固定 96px（覆盖 `-12345.678` 量级）；自适应会在滚动时抖动 |
| 分组记忆（折叠状态持久化） | 当前 `Expander IsExpanded="True"` 硬编码；持久化需要配置项 |
| 监视项导出 / 导入 | 需要新命令与文件格式 |

## 7. 验证计划（复刻后）

| 层 | 判据 |
| --- | --- |
| 静态 | `MonitorView.xaml` 的 `hex=0`；三个按钮 `Command` 绑定齐全；移除按钮 `Padding="0"` |
| 运行时 | 实例化后 `GroupedPorts` 三个分组存在；`WatchPortWrapper` 数 = `DisplayPorts.Count`；失效项 `IsMissing` 行有 `missing` 视觉 |
| 功能回环 | 输入 → `SuggestedItems` 非空 → Enter → `DisplayPorts` +1；移除 → −1；清空 → 0；折叠组头 → 行隐藏 |
| 渲染 | 默认态 / 空态 / 420 窄面板 三张 PNG |
| 回归 | `FlowCanvasChecks` 0 失败 |

## 8. 变更规则

确认后只读。需要调整时升 `v2`，并同步：`design.html`、本 README 版本号、令牌映射表、`docs/UI设计基线/README.md` 索引行。
