# 流程栏 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v2 |
| 确认日期 | 2026-10-19 |
| 确认人 | 待用户确认 |
| 覆盖范围 | `VisionMaster/Views/ProcessView.xaml`（步骤树 + 底部执行统计） |
| 面板宽度 | 默认 340px / 最小 320px（低于 320 组头右侧统计会把实例名挤到只剩两三个字） |
| 设计基线文件 | `design.html`（自包含，浏览器直接打开） |

## 定位

按 `AGENTS.md` §30.1 的四步流程：本文件是**第 ① 步（Web 设计基线）**的产物。
Web 只是设计媒介，**不进入构建、不部署**（R126）。确认后按本基线做第 ③ 步（WPF 复刻），
第 ④ 步以「同一屏、同一状态」的截图对照验收（R129/R132）。

## v2 方向

> 更平、更轻、层级更清楚。

v1 解决了"画错/看不清"的问题，但整体仍是"把现状修好"。v2 把视觉语言换掉：

1. **分组是章节，不是胶囊**：整行 section bar（3px 身份色条 + 类型标签 + 名称 + 右侧分支数/状态图标）。
2. **状态用底色，不用硬描边**：失败 `#FEF2F2`、运行 `#F0FDF4`；去掉 1px 红/绿描边。
3. **分支是子标题，不是窄胶囊**：整行浅蓝底 + 发丝边，计数右对齐；空分支用虚线框。
4. **节奏统一**：算子 40 / 分支 32 / 分组 36，4px 递进。
5. **动效只给运行**：蓝点 1.6s 脉冲扩散；悬停 120ms 底色过渡。
6. **数字等宽**：计数、耗时、统计行 tabular-nums。

## 设计要点（完整清单）

1. **失败态只有一套语言**：状态列红色感叹号 + 名字红 + 类型行红 + 柔和红底；算子、等待节点、容器一致。
   不再用名字前的细线 `×`（中文界面里读作关闭/删除，且会顶偏名字）。
2. **名字竖线对齐**：失败标记占用固定 16px 状态列，不挤占行内宽度。
3. **绿色只给「正在运行」**：运行行浅绿底 + 脉冲点；成功只把状态点变绿，不给整行上色。
4. **计数与耗时右对齐、等宽数字**：11px `ScadaSecondaryTextBrush`，对比度 4.7:1。
5. **分支自己响应悬停**：底色 `#F0F9FF → #E0F2FE`、描边 `#EEF2F7 → #BAE6FD`（拖放落点必须有反馈）。
6. **容器名与算子名同权重**：13px SemiBold；截断后 `ToolTip` 显示全名。
7. **层级引导线**：`#E2E8F0`（原 `#EEF2F7` 对白底 1.12:1，基本不可见），14px 缩进。
8. **状态不是唯一信息载体**：失败同时有红底、红字、感叹号字形；色盲用户也能分辨。

## 令牌映射

完整表见 `design.html` §5。核心：

| 用途 | WPF 资源键 |
| --- | --- |
| 正文 / 实例名 | `ScadaTextBrush` |
| 次要文字 / 类型行 / 计数 / 耗时 | `ScadaSecondaryTextBrush` |
| 层级引导线 | `DialogBorderBrush` |
| 发丝边 / 分隔线 | `DialogBorderLightBrush` |
| 主色（选中条、身份色条、分支点） | `ScadaAccentBrush` |
| 主色（分支悬停描边） | `DialogAccentBrush` |
| 分支底 / 图标块底 | `DialogAccentSoftBrush` |
| 选中行 / 分支悬停底 | `DialogAccentLightBrush` |
| 分支悬停描边 | `DialogAccentBorderBrush` |
| 行悬停 | `DialogRowHoverBrush` |
| 分组底 | `DialogRowAlternateBrush` |
| 失败 | `DialogDangerSubtleBrush` / `DialogDangerBrush` |
| 运行 | `DialogSuccessSubtleBrush` / `DialogSuccessBrush` / `DialogSuccessTextBrush` |

**不新增颜色令牌**——全部取自 `UI/Controls/Themes/Colors.xaml`。

## v1 → v2 变更摘要

| 维度 | v1 | v2 |
| --- | --- | --- |
| 分组头 | 类型 chip + 名称（内容宽度） | 整行 section bar + 右侧分支数/状态图标 |
| 失败态 | 白底 + 1px 红描边 | 柔和红底，无描边；白底图标块 |
| 运行态 | 浅绿底 + 绿描边 | 浅绿底，无描边；脉冲点 |
| 分支 | 150px 最小宽度胶囊 | 整行子标题，计数右对齐；空分支虚线框 |
| 行高 | 32 / 26 / 28 | 40 / 32 / 36 |
| 图标块 | 24px / 圆角 6 | 26px / 圆角 7；失败/运行行白底 |
| 耗时 | 灰底胶囊 | 纯文字 + 小钟表，右对齐 |

v1 的 8 条功能性修正（失败统一、名字对齐、计数对比度、分支悬停、容器名权重、容器名 ToolTip、
等待节点图标去重、引导线可见）**全部保留**，见 `design.html` §4。

## 复刻实现（第 ③ 步，已完成）

| # | 设计意图 | WPF 实现 |
| --- | --- | --- |
| 1 | 整行 section bar | `ModernStepContainerStyle` 的 `ContentPresenter` 改 `Stretch`；三种行都铺满整行 |
| 2 | 色条延伸到展开箭头下方 | 容器/分支模板用 `Margin="-22,…"` 左移，模板里的 `Expander` 设 `Panel.ZIndex="2"` 压在色条上；内容再内缩回 30px 竖线 |
| 3 | 分组左侧 3px 身份色条 | `ContainerAccentBarStyle`（3px、上下内缩 9px、右端圆角），每类容器各给身份色，Running/Failed 触发器改绿/红 |
| 4 | 右侧「N 分支 · M 算子」 | `Children.Count`（集合自带通知）+ 模型新增只读 `TotalStepCount`（`ContainerStepCountWatcher` 订阅分支与步骤增删）；循环体显示「循环体 · N 算子」 |
| 5 | 运行脉冲 | 状态点外的 `Ellipse` + `DoubleAnimation`（Scale 1→2.6、Opacity 0.45→0，1.6s 循环）；只在 `State=Running` 启动，避免双触发器抢同一动画 |
| 6 | 等宽数字 | `Typography.NumeralAlignment="Tabular"`（组头统计、分支计数、耗时、底部统计） |
| 7 | 空分支虚线框 | 叠加 `Rectangle`（`StrokeDashArray="3,3"`、`RadiusX/Y=8`），空态同时把实线 `BorderThickness` 归零 |
| 8 | 分支悬停 | 分支内一层 `HoverLayer`（`DialogRowHoverBrush`、Opacity 0→0.6）+ 描边提到 `DialogAccentBorderBrush`，保住 Else/循环的身份色 |
| 9 | 等待节点不再重复图标 | 改成与算子行同构：第二行「**等待** · 条件」，不再单独占一个 chip（比 v1 的"chip 只留文字"更彻底，名称与算子行对齐） |
| 10 | 失败标记不顶偏名字 | 状态列里放感叹号 `TextBlock`（与圆点同 16px 列），失败时圆点收起 |

**R23 守门**：本次不改 `StepModel` 子类集合，所有 `DataType` 模板原样保留（U1-U5 全绿）。

**复刻证据**：`evidence/after-render-F.png`（`FlowCanvasChecks --render` 场景 F，460×1320）。
注：探针把 `Icon` 直接设成 Segoe MDL2（见 AGENTS.md R141），图里的图标空框是探针环境现象；
真机走 App.xaml 的 Font Awesome 字体。

## 已知偏差（复刻时按 R130 记录）

1. **字体度量**：Web 用 Segoe UI Variable / 微软雅黑，WPF 同一族字体；同字号下字宽与行高略有差异，不按 px 硬套。
2. **滚动条**：WPF 树自带约 17px 纵向滚动条占位，窄宽度下可用宽度比本页少约 17px。
3. **动效**：Web 用 CSS `ease` 120ms；WPF 用 150ms `CubicEase`（近似）。运行脉冲用 `DoubleAnimation`，不是 CSS 阴影。
4. **阴影**：面板外框的 1px 阴影只是示意，WPF 面板无窗口级阴影。
5. **面板标题栏**：`流程栏 ▾ 📌 ✕` 由 AvalonDock 宿主提供，不属于 `ProcessView.xaml`。
6. **拖放**：基线无法表达拖拽 adorner 与落点高亮，复刻后需真机验证。

## 未纳入本版（记录，不改）

- **失焦选中**：`DialogRowHoverBrush` 底 + `ScadaSwatchBorderBrush` 竖条；不再用几乎不可见的 `#F8FAFC`。
- **分组头 `Margin="4,8,4,0"`**：改为整行 bar 后由行高与 4px 间距统一控制，不再单独留 8px 顶距。

## 变更规则

设计基线确认后只读。需要调整设计时升版本（v3），并同步更新已复刻的 `ProcessView.xaml` 与本映射表。
