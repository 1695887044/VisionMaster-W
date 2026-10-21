# 工具箱拖拽浮层 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | **v2（已确认，替代 v1）** |
| 确认日期 | 2026-10-21（用户会话确认："好的 把效果做到WPF上"） |
| 确认人 | 用户 |
| 覆盖范围 | `VisionMaster/Controls/DragGhostCard.xaml`（卡片视觉）；三处拖拽源：工具箱 `ToolView.xaml`、流程栏 `ProcessView.xaml`、画布 `FlowCanvasView.xaml`（节点拖动 Popup） |
| 设计基线文件 | `design.html`（v2，自包含，浏览器直接打开）；`design-v1.html`（v1，已归档只读） |
| 证据 | `evidence/design-v2-overview.png`（v2 整页，1.5×）；`evidence/design-v1-overview.png`（v1）；`evidence/before-wpf-v0.png`（v0 实拍） |
| 状态 | **已复刻到 WPF**（三处共用同一控件）；WPF 实拍见 §实现记录 |

## 定位

按 `AGENTS.md` §30.1 的四步流程，本目录是**第 ① 步（Web 设计基线）**的产物。
Web 只作设计媒介，**不进入构建、不部署**（R126）。确认后按本基线做第 ③ 步（WPF 复刻），
第 ④ 步以「同一状态」的 WPF 截图与本稿逐块对照验收（R129/R132）。

## 版本沿革

- **v0（现状）**：34 高白胶囊 + `⋮` + 固定蓝图标 + 不截断名称，指针偏移 `(-4,-4)`。
- **v1**：去 `⋮`、换 26×26 分类色图标块、名称加粗限宽、偏移 `(14,10)`；但整卡仍是 1px 高饱和蓝描边，像表单控件。
- **v2（本稿）**：在 v1 上做三件事——**去描边**（改发丝边 `#E6E9EF`）、**加层次**（双层阴影、圆角 12、图标块渐变）、**补信息**（名称 / 分类两行）。

## v2 方案

> 一张浮起来的小卡片：中性白、发丝边、软阴影；分类身份只在图标块与分类行。

1. **卡片**：46 高 / 168~268 宽 / 圆角 12 / 白底 98% / 1px 发丝边 `#E6E9EF` /
   阴影 `0 2px 6px rgba(15,23,42,.06)` + `0 14px 30px -10px rgba(15,23,42,.22)`。
2. **图标块**：28×28 / 圆角 9 / 分类色 18%→7% 渐变底 / 分类色 30% 描边 / 14px 分类色字形。
3. **两行文本**：名称 13 SemiBold `#0F172A`；分类 10 Regular `#64748B`；各自限宽 165 + 省略。
4. **指针偏移 (16,12)**：箭头完整落在卡片外。
5. **动效**：150ms 淡入 + 4px 上浮（WPF 只能复刻 Fade，见已知偏差）。

完整规格与令牌映射见 `design.html` §05。

## 令牌映射（Web ↔ WPF）

| 用途 | Web（本稿） | WPF |
| --- | --- | --- |
| 卡片描边 | `#E6E9EF` | `DialogBorderLightBrush`（同值发丝边） |
| 阴影 | 双层（见上） | 单层近似 `DropShadowEffect BlurRadius=28 ShadowDepth=8 Opacity=0.16` |
| 图标块底 | 分类色 18%→7% 渐变 | 新增 `CategoryChipBrushConverter` → `LinearGradientBrush` |
| 图标块描边 | 分类色 30% + 白 | `CategoryBorderBrushConverter` |
| 图标字形 | 分类色 | `CategoryAccentBrushConverter` |
| 名称 | `#0F172A` 13 SemiBold | `TextPrimaryBrush` + `FontSize=13 FontWeight=SemiBold` |
| 分类行 | `#64748B` 10 Regular | `ScadaSecondaryTextBrush` + `FontSize=10` |
| 指针偏移 | `(16, 12)` | `dd:DragDrop.DragAdornerTranslation="16,12"` |

## 已知偏差（R130）

- **双层阴影不可复刻**：WPF 每个元素只有一个 `Effect`，真机用单层近似（比本稿略"平"）。
- **渐变图标块**：需要新增 `CategoryChipBrushConverter`；若实现成本被否决，回退为 12% 纯色底（差异仅图标块内部）。
- **字体度量**：本稿用系统 UI 字体，WPF 用应用默认字体，截断点可能差 1~2 个字符。
- **动效**：本稿 150ms + 上浮；WPF 只有库的 Fade，无上浮。
- **DPI**：本稿按 100% 出图；125%/150% 下按 DIP 缩放。
- **分类行文案**：直接用 `ToolItemModel.Category` / `StepModel.Category`；为空时第二行收起（不显示"未分类"，避免给未知分类编一个名字）。

## 边界态（已在 design.html §03/§04 出图）

- 默认（短名） / 长名称（两行各自截断） / 不同分类（卡片本体不变） / 未知分类（灰 + cubes 回退）。
- 指针关系：热点 → 卡片左上角 = (16, 12)，箭头不压卡片。
- 浮层不表达"能不能放"：落点合法性由光标效果与状态栏文案负责（Break/Continue 的循环归属闸门）。

## 实现记录（2026-10-21，第 ③ 步）

| 位置 | 实现 |
| --- | --- |
| `VisionMaster/Controls/DragGhostCard.xaml(.cs)` | 卡片本体，只暴露 `Icon` / `Title` / `Category` 三个依赖属性（不假设 DataContext 类型） |
| `VisionMaster/Helpers/ToolboxCategoryVisuals.cs` | 新增 `CategoryChipBrushConverter`（分类色 → 18%→7% 渐变，带缓存） |
| `VisionMaster/Views/ToolView.xaml` | 工具箱拖拽源：模板换成卡片 + `DragAdornerTranslation="16,12"` |
| `VisionMaster/Views/ProcessView.xaml` | 流程栏内部拖动：`UseDefaultDragAdorner=False` + `DragAdornerTemplateSelector`（步骤卡 / 分支卡） |
| `VisionMaster/Helpers/DragGhostTemplateSelector.cs` | 按 `StepModel` / `StepCollection` 分派模板（树里两种条目混排） |
| `VisionMaster/Views/FlowCanvasView.xaml(.cs)` | 画布节点拖动：`Popup` + 跟随指针（`(16,12)`），`IsOpen` 必须 `Mode=OneWay`（Popup.IsOpen 默认 TwoWay，只读属性会抛） |
| `VisionMaster/ViewModels/FlowCanvasViewModel.cs` | `IsDragGhostVisible` / `DragGhostTitle` / `DragGhostCategory` / `DragGhostIcon`；`ItemsDragStarted/Completed` 发布，`Deactivate` 兜底收起 |

**实现侧偏差（R130 清单的落地值）**：

| 项 | 设计稿 | 实现 | 说明 |
| --- | --- | --- | --- |
| 卡片描边 | `#E6E9EF` | `DialogBorderLightBrush` = `#EEF2F7` | 复用既有令牌，肉眼差 1 级；不新增令牌 |
| 阴影 | 双层 | 单层 `BlurRadius=28 / ShadowDepth=8 / Opacity=0.16` | WPF 每元素一个 Effect |
| 名称/分类色 | `#0F172A` / `#64748B` | `DialogTextPrimaryBrush` / `DialogTextTertiaryBrush` | 同值令牌 |
| 分类为空 | — | 第二行收起 | 不显示"未分类" |

**验证**：

- `FlowCanvasChecks` 常规 gate：**3189 通过 / 0 失败**（新增 [M2] 6 条：画布浮层的显示/内容/抬手收起/多选计数/Deactivate 收起）。
- `FlowCanvasChecks --render`：`[render][check] 流程栏拖拽浮层：步骤卡=True；分支卡=True`；出图 `process-tree-drag-ghost.png`。
- `_ToolboxProbe`：**22 通过 / 0 失败**；出图 `artifacts/render/<yyyyMMdd>/toolbox-drag-ghost.png`（含阴影留白）。
- `--measure-canvas`：`[measure] ok`（真机画布 XAML + Popup 解析通过）。

**未覆盖（需真机复验）**：画布 Popup 跟随指针的体感（无头环境无法模拟真实拖动）；拖拽过程中切换面板/最小化等极端路径。

