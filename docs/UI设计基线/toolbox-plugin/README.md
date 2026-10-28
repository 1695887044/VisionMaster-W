# 插件工具箱 v2 · 紧凑精修（设计基线）

| 项 | 值 |
| --- | --- |
| 版本 | v2（2026-10-27 确认；胶囊 v2.1 子修订） |
| 基准宽度 | 300px（右侧检查器单列） |
| 覆盖范围 | 主界面「视觉图像 / 流程画布」右侧检查器里的**插件工具箱**（`ToolView`）：工具条、搜索、收藏提示、分组手风琴、列表/网格条目、拖拽/悬停预览、空态，以及它的**收起胶囊** |
| 不覆盖 | 组态模式的「图元工具箱」（`ScadaToolboxView`）；它沿用 `docs/UI设计基线/app-shell/` 与 `剩余视图设计稿-2026-10-22` 的图元基线 |
| 状态 | **已确认（2026-10-27），WPF 已复刻**；真机证据 `evidence/after-toolbox-300.png`、`evidence/after-capsule-16.png`；待用户按 R132 做最终真机验收 |
| 设计媒介 | `design.html` + `evidence/design-01-overview.png` + `evidence/design-02-specs.png`；不进构建、不部署（R126） |

## 一、设计目标

用户口径：**“更精致、小巧一点”**，指的是视觉图像右侧的插件工具箱（不是组态图元工具箱）。本稿只做样式/密度/层级优化，不改变既有行为：

- 拖拽到流程画布（gong 拖拽源）；
- 悬停 ~300ms 预览浮层；
- 星标收藏；
- 分组折叠；
- 列表 / 网格切换；
- 收起后胶囊钉回 300 列。

三个目标：

1. **更紧凑**：列表行 34→30、分组头 30→28、搜索 32→28、收藏提示 30→22；首屏多看到 1~2 个分组。
2. **更精致**：图标进 18/20px 浅色徽章，数量胶囊降一级；收藏星改暖金色；行悬停只换底/描边，不加阴影。
3. **不挡内容**：滚动区右侧留 10px gutter，避开 Fluent 覆盖式滚动条；长名称统一省略号。

## 二、R111 六项

### 2.1 布局

```text
┌──────────────────────────────┐
│ 工具箱（停靠页签，30px，主题管）│
├──────────────────────────────┤
│ 56 个算子              [☰|▦] │ 24px 工具条（去重复标题）
│ [🔍 搜索节点…              ] │ 28px
│ ★ 悬停条目点亮星标收藏        │ 22px 浅底提示
│ ▾ 收藏 1                     │ 28px 分组头
│   [icon] If              ★   │ 30px 行
│ ▾ 逻辑控制 10                │
│   ...                        │
└──────────────────────────────┘
```

- 面板 300px；停靠页签 30px 由 AvalonDock 主题提供，不在 ToolView 内改。
- 工具条 24px：左侧 `56 个算子`（数字用主色浅底 chip），右侧列表/网格分段按钮 42×20。
- 搜索 28px、收藏提示 22px、分组头 28px、列表行 30px、网格卡 38px。
- 滚动区：左边距 6、右边距 0 + 右侧 10px gutter；分组内容左右 4px。
- 收起胶囊：轨道 18px、胶囊 16×48~72、图标 9px、标题 8px；WPF 保留 6px 右移避开窗口 resize border。

### 2.2 间距

| 用途 | 值 |
| --- | --- |
| 水平外边距（工具条/搜索/提示） | 10px |
| 分组头内边距 | 4px 7px |
| 行内边距 | 4px 6px |
| 分组内容外边距 | 0 4px 4px |
| 网格间距 | 6px |
| 滚动区上下 | 0 / 6px |
| 圆角 | 5（徽章/分段）/ 6（行/分组头）/ 7（搜索/网格卡）/ 8（数量胶囊） |

### 2.3 颜色令牌

| 令牌 | 值 | 用途 |
| --- | --- | --- |
| `ScadaPanelBackgroundBrush` | #F7F8FA | 面板底 |
| `ScadaSurfaceBrush` | #FFFFFF | 搜索框 / 网格卡 / 胶囊 |
| `ScadaPanelBorderBrush` | #D7E0EF | 面板外框 / 搜索框 |
| `ScadaActionCardBorderBrush` | #E4E7ED | 网格卡 / 数量胶囊 |
| `ScadaTextBrush` | #303133 | 主文字 |
| `ScadaLabelTextBrush` | #606266 | 分组名/标签 |
| `ScadaSecondaryTextBrush` | #66708A | 次级文字 / 计数 |
| `ScadaFaintTextBrush` | #A8ABB2 | 占位 / 拖拽手柄 |
| `ScadaHoverBrush` | #E3EEF9 | 行/分组头悬停底 |
| `ScadaAccentBrush` | #3B82F6 | 主色 / 选中 / 搜索命中 |
| `ScadaAccentSoftBrush` | #1A3B82F6 | 选中底 / 搜索高亮底 |
| `ScadaOnAccentTextBrush` | #FFFFFF | 主色底上的文字 |
| `ScadaFavoriteBrush`（**新增提案**） | #F59E0B | 收藏星（语义色） |
| 分类色 | `CategoryAccentBrushConverter` / `CategorySoftBrushConverter` / `CategoryBorderBrushConverter` | 分组/算子徽章；沿用现有转换器，不新增分类色表 |

> 新增 `ScadaFavoriteBrush` 只影响收藏星；若评审不同意新增令牌，可回退到 `ScadaAccentBrush`（蓝色星），其余规格不变。

### 2.4 字体

| 用途 | 字号 / 字重 |
| --- | --- |
| 工具条计数 | 10.5 / 600；数字 9.5 / 700 |
| 分组名 | 12 / 600 |
| 算子名（列表/网格） | 12 / 400 |
| 搜索文字 / 占位 | 11.5 / 400 |
| 数量胶囊 | 9.5 / 500 |
| 收藏提示 | 10.5 / 400 |
| 空态标题 / 说明 | 12 / 600；10.5 / 400 |
| 胶囊标题 | 8 / 400 |

WPF 用 Microsoft YaHei；Web 设计稿同字体族，字宽差异按 R130 记入已知偏差。

### 2.5 五态 + 领域状态

| 状态 | 表现 |
| --- | --- |
| Normal | 行透明；星标/拖拽手柄隐藏 |
| Hover | 行底 `ScadaHoverBrush`；手柄、星标淡入；分组头同样换底 |
| Pressed/Drag | 行保持悬停底；拖拽预览仍用 `DragGhostStack`（不改尺寸）；落点提示沿用现有 |
| Focus | 搜索框主色边 + 2px `ScadaAccentSoftBrush` 外环；胶囊键盘焦点可见 |
| Disabled | 搜索无结果时清空按钮仍可用；禁用态仅用于未来只读模式（本稿不改） |
| 收藏 | 星标常显暖金 `#F59E0B`，不因悬停消失 |
| 搜索 | 命中片段主色浅底 + 主色字；输入后出现清空按钮；无结果出空态 |
| 分组 | 展开/收起 chevron 90° 旋转；保持现有展开状态持久化 |
| 视图 | 列表/网格分段；选中段主色底；保持现有 ViewMode 持久化 |
| 收起 | 胶囊 16×48~72、轨道 18；图标 9px、标题 8px；悬停内侧 2px 主色竖线；点击钉回 300 列 |

### 2.6 交互

| 动作 | 口径 |
| --- | --- |
| 悬停行 | 底/边 120~160ms；不位移、不加阴影 |
| 拖拽 | 保持 gong 拖拽源；拖拽预览/落点提示不变 |
| 悬停预览 | 保持 ~300ms 弹层；浮层内容/宽度不变 |
| 星标 | 点击切换；不触发拖拽；收藏后常显 |
| 搜索 | 即时过滤；命中高亮；清空按钮一键还原；无结果空态 |
| 分组 | 点标题折叠/展开；保持展开状态持久化 |
| 列表/网格 | 分段按钮切换；保持 ViewMode 持久化 |
| 胶囊 | 点击钉回 300 列；悬停 2px 主色竖线；键盘焦点可见 |

### 2.7 胶囊 v2.1 子修订（2026-10-27，已复刻）

用户要求“收起胶囊再小巧一点”。三档对比（设计稿 `design-02-specs.png` 里有 1:1 实物）：

| 档 | 胶囊 | 轨道 | 图标 / 标题 |
| --- | --- | --- | --- |
| 现状 | 20×64~100 | 24 | 11 / 9 |
| v2 | 18×56~88 | 20 | 10 / 8.5 |
| **v2.1（本次）** | **16×48~72** | **18** | **9 / 8** |

16px 是建议下限：再窄到 14px 时 7.5px 竖排标题在 100% DPI 下难读，且点击热区太小。WPF 保留 6px 右移避开窗口 resize border（点击胶囊左半区即可）；3 字标题在 48px 高内完整显示，5 字标题自动长到 72px。

## 三、与现状的差异

| 项 | 现状 | 方案 | 理由 |
| --- | --- | --- | --- |
| 标题 | 「工具箱」14px + 56 胶囊 | `56 个算子` + 列表/网格 | 停靠页签已有标题，去掉重复信息 |
| 搜索 | 32px / 12.5px | 28px / 11.5px + 清空按钮 | 更紧凑；清空是常用动作 |
| 收藏提示 | 30px 卡片 | 22px 浅底条 | 一次性引导，不抢主内容 |
| 分组头 | 30px，裸彩色图标 | 28px，18px 浅色徽章 | 保留分类识别，视觉更安静 |
| 列表行 | 34px | 30px | 首屏多 1~2 行；拖拽仍好点 |
| 星标 | 主色蓝，22px | 暖金 #F59E0B，18px | 语义色；与主色选中区分 |
| 网格 | 2 列，8,7 | 2 列，38px 高，6 间距 | 更紧凑，不抢列表 |
| 滚动 | 8 外边距，无 gutter | 右侧 10px gutter | 避开 Fluent 覆盖式滚动条 |
| 空态 | 仅收藏引导 | 搜索无结果也出空态 | “搜不到”和“没数据”分开 |
| 胶囊 | 20×64~100，轨道 24 | 16×48~72，轨道 18 | 收起时给画布多让 6px |

## 四、证据与出图

| 文件 | 内容 |
| --- | --- |
| `design.html` | 可交互设计板；默认显示全部；`?view=top` 只留对比+状态，`?view=specs` 只留规格/差异/交互 |
| `evidence/design-01-overview.png` | 上半页：目标、现状 vs 方案、六个改动点、关键状态（1505×1320） |
| `evidence/design-02-specs.png` | 下半页：尺寸/字体/令牌、**收起胶囊三档 1:1 对比**、差异表、交互与已知偏差（1505×1800） |
| `evidence/after-toolbox-300.png` | 真机改后：右侧 300px 工具箱（60 个算子、28px 搜索、收藏组、28px 分组头、30px 行） |
| `evidence/before-capsule-overlap.png` | 改前缺陷：库自动隐藏页签 + 自定义胶囊并排，左侧“工具箱”被遮一半（4× 放大） |
| `evidence/after-capsule-16.png` | 真机改后：16px 收起胶囊（4× 最近邻放大），库页签已隐藏；原图 `artifacts/render/20261027/capsule-tab-clean.png` |

出图方式：本地 HTTP 服务 + 浏览器 viewport 1520×1450，截图后按页面宽度裁到 1505；设计稿不参与构建。

> 设计板里的「现状」是按 `VisionMaster/Views/ToolView.xaml`（2026-10-27）的当前尺寸重建的示意，不是真机截图；WPF 验收时必须用真机改前/改后截图（R132）。

## 五、已知偏差（Web → WPF，R130）

1. **字体度量**：Web 与 WPF 的 Microsoft YaHei 行高/字宽略有差异；以 WPF 实测截图验收，不按 Web 像素死扣。
2. **滚动条**：Fluent 是覆盖式 12px；WPF 用 10px 右侧 gutter + `TextTrimming`，不改成全局细滚动条（避免影响日志/表格/组态）。
3. **图标**：设计稿用 Font Awesome 6 Pro Solid 码点；WPF 复用现有 `Icon` 字体与分组图标，不新增图标资产。
4. **标题移除**：ToolView 内不再重复「工具箱」；浮动窗口仍有窗口标题，不影响识别。
5. **清空按钮 / 搜索空态**：已在 `ToolViewModel` 实现 `ClearSearchCommand` / `HasSearchText` / `HasNoSearchResults`；空态只在“有搜索词但无命中”时出现。
6. **胶囊尺寸**：20→16 / 轨道 24→18 已随 WPF 批次落地（`Shell.xaml`），保留 6px 右移避开 resize border；16px 宽下实际可点区域约 10px（左半区）。
7. **分类色**：设计稿用 12% 透明底 + 分类色图标；WPF 走现有 `CategorySoftBrushConverter`/`CategoryAccentBrushConverter`，不新增分类色表；分组头浅底由 `ToolboxCategoryVisuals.SoftBrushFor` 计算。
8. **改前真机截图缺失**：设计稿里的“现状”是按旧 `ToolView.xaml` 尺寸重建的示意，不是真机截图；本轮补的是改后真机图。验收对照以“设计稿现状示意 vs 改后真机图”为准。
9. **胶囊点击热区**：16px 是建议下限（再窄 7.5px 竖排标题难读、热区太小）；点击胶囊左半区即可，真机 (1267,120) 已验证。
10. **库自动隐藏页签与自定义胶囊二选一**：图钉收起后 AvalonDock 会生成自己的 33px `LayoutAnchorControl`，与 18px 胶囊轨道重叠成两条“工具箱”。现在只要面板有自定义胶囊，就隐藏库页签（只改 `Visibility`，不动 `IsAutoHidden`）；代价是**没有库的悬停飞出层**，恢复入口只剩胶囊。流程导航等无胶囊面板仍保留库页签。改前/改后证据 `before-capsule-overlap.png` / `after-capsule-16.png`。
11. **右边缘 6px 空 group**：库的 `LayoutAnchorGroupControl` 在页签隐藏后仍留约 6px 宽（压在 18px 胶囊轨道底下，视觉不可见）；本轮不动库的 group 宽度，避免影响其它自动隐藏面板。

## 六、验收

- 本稿 **v2 已确认（2026-10-27）并完成 WPF 复刻**；实现记录见 `docs/code-changes/2026-10-27-插件工具箱v2 WPF复刻（紧凑工具条与胶囊）.md`。
- 已验证：`VisionMaster` 构建 0 错误；`_ToolboxProbe` 33/0；`UIThemeSmokeTest` 143/0（含胶囊/库页签去重 2 条）；`tools\run_gates.ps1` 14/14 全绿（首次 13/14 为并发会话的 `Plugin.ImageAlign` 旧 DLL，重建后复跑）；真 exe 胶囊点击恢复（日志 `16:56:29` 三条齐全）。
- 待用户按 R132 做最终真机验收：对照 `design-01-overview.png` / `design-02-specs.png` 与 `after-toolbox-300.png` / `after-capsule-16.png`，重点看 28px 搜索、30px 行、38px 网格卡、暖金收藏星、16px 胶囊是否“够小巧且好点”。
- 若验收不通过：只回退对应项（例如胶囊回 18px 轨道/18px 宽，或收藏星回 `ScadaAccentBrush`），其余规格不变；不得用“桌面端更方便”反向改设计（R131）。
- 基线只读：后续变更必须升版本（v3）并同步本 README 与已复刻页面（R128）。

## 七、WPF 复刻记录

| 文件 | 改动 |
| --- | --- |
| `VisionMaster/Views/ToolView.xaml` | 24px 工具条、28px 搜索（焦点环 + 清空）、22px 收藏提示、28px 分组头、30px 列表行、38px 网格卡、10px 滚动 gutter、搜索无结果空态、暖金星标 |
| `VisionMaster/ViewModels/ToolViewModel.cs` | `HasSearchText` / `HasNoSearchResults` / `ClearSearchCommand`；分组 `SoftBrush` |
| `VisionMaster/ViewModels/ToolboxGroupViewModel.cs` | 新增 `Brush SoftBrush` |
| `VisionMaster/Helpers/ToolboxCategoryVisuals.cs` | `FavoriteColor` / `FavoriteSoftBrush` / `SoftBrushFor` / `Blend` |
| `VisionMaster/Services/LayoutHelper.cs` | `SyncAutoHideTabs`：有自定义胶囊的面板隐藏库 `LayoutAnchorControl`，其余保留 |
| `VisionMaster/ShellViewModel.cs` | `UpdateInspectorCapsules` 末尾立即 + Loaded/ApplicationIdle 补同步 |
| `VisionMaster/Shell.xaml` | 胶囊轨道 18px；胶囊 16×48~72；图标 9px、标题 8px；悬停 2px 主色竖线 |
| `UI/Controls/Themes/Colors.xaml` | 新增 `ScadaFavoriteBrush` #F59E0B |
| `UIThemeSmokeTest/Program.cs` | v2.1 胶囊静态检查 + 胶囊/库页签去重 2 条运行时断言（含反证） |
| `tools/probes/_ToolboxProbe/Program.cs` | 搜索/滚动/分组头/行/网格卡/清空/空态/图标色等 v2 断言 |

验证：`VisionMaster` 0 错误；`_ToolboxProbe` 33/0；`UIThemeSmokeTest` 143/0（含胶囊/库页签去重 2 条 + 变异反证）；`run_gates.ps1` 14/14；真机胶囊点击日志 `16:56:29` 三条齐全。
