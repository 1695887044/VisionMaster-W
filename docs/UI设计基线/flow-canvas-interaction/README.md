# 流程画布 · 交互设计基线（自绘版）

| 项 | 内容 |
| --- | --- |
| 版本 | **v9**（按真实 WPF 模板渲染 + 正交折线零穿越，见 §十八；v8 及更早均留档，见 §十六/§十七） |
| 日期 | 2026-10-24（v9 同日更新） |
| 确认人 | 待用户 / 外部评估方确认 |
| 覆盖范围 | 画布**布局与引用表达方式** + 交互层：树状二维布局、**算子卡 / 容器 / 泳道按 `FlowCanvasView.xaml` 真实模板 1:1 复刻**、**连线只读**（拖线建绑已移除）、**选中算子聚焦入向 + 出向引线**、**正交折线走线避让**、**线上端口名**、**全局变量小节点（紫色虚线）**、非法线常显、底部连线明细、顺序链、平移缩放、点选、拖动改序、右键菜单、双击、小地图 |
| 设计基线文件 | **`design-v9.html`（当前，自包含）**；`design-v8.html`/`design-v7.html`/`design-v6.html`/`design-v5.html` 留档对照 |
| 定位 | Web 只作设计媒介，**不进入构建、不部署**（AGENTS.md R126） |
| 关联记录 | `docs/code-changes/2026-10-24-画布自绘（C2-第0-1步）.md` |

---

## 〇、v0.2 的三条改动（针对 v1 的否决理由）

v1 被否决的两条理由：**曲线凌乱、引用关系难读**；**全量铺开**。

| # | 改动 | 效果（`design-v2.html` 实测） |
| --- | --- | --- |
| ① | **梯形单列排列**：按执行顺序自上而下，容器用「缩进 + 左侧竖线」表达层级，分支并列成列 | 不再把树摊成二维，没有"节点飘在远处" |
| ② | **引用只画正交短线**：左侧单根竖轨 + 短横引线（梯形图式）；跨页/远距离**不画线**，改成目标行上的 `← 源名` 标签，点标签跳到源所在页 | 引用一眼可读，不再满屏曲线 |
| ③ | **限量显示**：一页最多 120 步（可调 40/120/300），只有当前页进入渲染与命中测试，底部翻页 | **3000 步 = 25 页，一页 121 行，一帧 5.6 ms** |

**关于"必须要树状排列吗"**：不需要。数据是树（容器套容器），但**显示不必是树**——
流程栏 `ProcessView` 已经是树，画布走"单列梯形 + 缩进"反而更接近 PLC 的阅读习惯。

### 这个改动对实现的影响（重要）

v0.2 不是"换个画法"，而是**画布语义从"自由二维图"变成"有序单列"**：

| 现在有 | v0.2 后 |
| --- | --- |
| `FlowLayoutStore` 存 X/Y，`AutoLayout` 算二维排位 | **不再需要 X/Y**（顺序即布局），`AutoLayout`/`ComputeContainerGeometry` 大幅简化或删除 |
| 容器框/泳道是二维矩形，`_frameRects` 参与命中与拖拽落点 | 变成"缩进 + 左侧竖轨"，不再有框体几何 |
| 拖动节点 = 改坐标 | 拖动 = **改执行顺序**（插入线），坐标不再可变 |
| 拖线建绑定（自由连两个端口） | 改为"从左侧竖轨/下拉选择源"，或仅允许相邻行连线 |
| 3000 步需要虚拟化才不卡 | **天然只渲染一页**，不需要虚拟化 |

代价：已保存的 X/Y 布局失效（可保留字段但不再使用）；用户不能再自由摆放节点。

---

---

## 一、为什么要先出这份设计稿

现画布是"每个节点一个 Nodify `ItemContainer`"，353 步流程真机一次重建 **7.4 秒**（用户机器日志实测）。
C2 要把它换成"整块自绘"，渲染层已完成并验证（2268 节点快照 25 ms、一帧 11~32 ms），
但**交互层完全没接**：点选/框选/拖动/拖线/右键都还在 Nodify 那边。

按仓库纪律（AGENTS.md §30 R110/R111/R127）：**没有 Web 设计基线不得改桌面端交互**。
所以先出这份可点击原型 + 代码点对照，供外部评估工作量与风险。

---

## 二、可点击原型怎么看

1. 浏览器直接打开 `design.html`（双击即可，无需服务器）。
2. 左上角读数实时显示：`缩放 / LOD 档位 / 绘制数 / 一帧耗时`。
3. 点「注入 3000 节点」→ 看 LOD 自动降到"只画色块"，一帧仍在 **11 ms** 量级（浏览器 Canvas2D，软件）。
4. 点「重建演示图」恢复 16 节点的演示结构（含 If 双泳道、For 单泳道、非法线、失败态、禁用态）。

> 原型里的数字**只用于说明交互模型的可承受性**，不是 WPF 的性能结论；
> WPF 侧的真实数字见 C2 记录（11~32 ms）。

---

## 三、设计要素（R111 六项）

### 3.1 布局与间距（与 `../flow-logic/` 一致，不变）

| 元素 | 尺寸 |
| --- | --- |
| 步骤卡片 | 156 × 58，圆角 10，内边距 10/8 |
| 图标块 | 28 × 28，圆角 8，距左 10 |
| 容器框 | 圆角 12，描边 1.5，头带 46，内边距 16 |
| 泳道 | 圆角 8，头带 28，内边距 14 |
| 分支列间距 | 24 |

### 3.2 配色令牌（全部来自 `UI/Controls/Themes/Colors.xaml`）

`FlowCanvasNodeBackgroundBrush #FFFFFF` · `FlowCanvasNodeBorderBrush #C9D2DE` ·
`FlowCanvasStateRunningBrush #2563EB` · `FlowCanvasStateFailedBrush #EF4444` ·
`FlowCanvasLinkBrush #3B82F6` · `FlowCanvasOrderLinkBrush #94A3B8` ·
`FlowCanvasLinkIllegalBrush #EF4444` · `FlowCanvasFrameBorderBrush #A5C0E8` ·
`FlowCanvasFrameHeaderBrush #EEF4FF` · `FlowCanvasNodeTextBrush #0F172A` ·
`FlowCanvasNodeSubTextBrush #94A3B8` · `FlowCanvasLaneTextBrush #5A6C99`

### 3.3 字号字重

标题 14 SemiBold / 副标题 10.5 Normal / 容器头带 12.5 SemiBold / 泳道头带 9.5 SemiBold / 序号 9 SemiBold。

### 3.4 五态（R115）

| 态 | 视觉 |
| --- | --- |
| 默认 | 白底 + `#C9D2DE` 1px 描边 |
| 悬停 | 连接点浮现（常态不画，减少噪声） |
| 选中 | 描边换 `#2563EB`、2px |
| 禁用 | 整体 45% 透明（**自绘用 alpha 预乘，不用 PushOpacity**，见 C2 记录） |
| 运行 / 失败 | 蓝边 / 红边 + 淡红底 |

### 3.5 交互细节

见原型左下角「交互规格」面板，逐条与下表对应。

### 3.6 三级 LOD（本页新增）

| 缩放 | 绘制内容 | 原型实测（3000 节点） |
| --- | --- | --- |
| ≥ 0.40 | 全细节 | 3000 节点全在视口时约 40~60 ms |
| 0.15 ~ 0.40 | 卡片 + 边框，无文字 | ~20 ms |
| < 0.15 | 只画纯色块 | **11 ms** |

**不做"限制算子数量"的硬上限**：流程是编译/运行单位，硬截断会改业务语义；
裁剪 + LOD 已能线性扩展到万级。建议只在 >1500 步时给状态栏软提示。

---

## 四、交互 → 代码点对照（外部评估用）

> 行号以 2026-10-24 构建为准，编辑后会漂移；以成员名为准。
> "自绘层落点"一列里标 **待写** 的是第 2 步要新增的代码。

| # | 交互 | 现实现（Nodify 路径） | 自绘层落点 | 风险 |
| --- | --- | --- | --- | --- |
| 1 | 平移 | `FlowCanvasView.xaml` 绑定 `ViewportLocation`（`NodifyEditor`）；`DragGuidesAdorner` 读它 | `FlowCanvasSurface.Pan`（已有）+ 待写 `OnMouseMove` | 低 |
| 2 | 缩放 | `FlowCanvasView.xaml.cs:87 OnZoomCommandRequested`；滚轮由 Nodify 内建 | `FlowCanvasSurface.Zoom`（已有）+ 待写 `OnMouseWheel`（以光标为锚点同步改 `Pan`） | 低 |
| 3 | 适应全图 | `FlowCanvasView.xaml.cs:87` → `Editor.FitToScreen` + `BringIntoView` | `ComputeFitViewport`（已有）+ 待写"应用视口" | 低 |
| 4 | 点选 | `FlowCanvasView.xaml` `ItemContainer.IsSelected` TwoWay → VM `OnNodeSelectionChanged:1502` | `HitTest`（已有）+ 待写 `OnMouseDown` → 仍走 `OnNodeSelectionChanged` 同一条写回 | 低 |
| 5 | 框选 | Nodify 内建 `BeginSelecting` / `SelectionHelper` | 待写：用 `IFlowCanvasGeometry.Nodes` 做矩形相交（**比 Nodify 简单**，无 null 容器问题） | 低 |
| 6 | 多选拖动 | `xaml` `ItemsDragStarted/CompletedCommand` → VM `OnItemsDragStarted:3060` / `OnItemsDragCompleted:3103` | 待写拖动 → 改 `node.Location` → 复用 `OnNodePropertyChanged`（已写回 `FlowLayoutStore`） | 中 |
| 7 | 拖拽改执行顺序 | `OnItemsDragCompleted:3103` → `ResolveOwnerByPoint:3132` / `FindDeepestContainerAt:3325` | 待写"插入点判定" → **复用** `ResolveOwnerByPoint`，不要新写一套落点规则 | **高**（跨容器/跨分支移动语义） |
| 8 | 拖线建绑定 | `xaml` `PendingConnection` 模板；VM `CanvasPendingConnectionViewModel:3723` | 待写 `connect` 手势 → 复用 `ModuleLinkRequested`（与右键"编辑绑定"同一出口） | **高**（类型校验、非法落点） |
| 9 | 右键菜单 | 共享资源 `StepNodeContextMenu` / `ContainerNodeContextMenu` / `ModuleLinkContextMenu`（`FlowCanvasView.xaml` 资源段，2026-10-24 已改共享） | 待写：命中后设 `ContextMenu.PlacementTarget` 再 `IsOpen=true` | 中 |
| 10 | 双击 | `FlowCanvasView.xaml.cs:232 OnEditorMouseDoubleClick` → `OpenModuleParameters` | 待写 `OnMouseDoubleClick` 命中 | 低 |
| 11 | 拖动参考线 | `DragGuidesAdorner`（类 `:38`，`OnRender` 在 `:406`） | 并入 `OnRender` 画线（比 Adorner 简单） | 低 |
| 12 | 小地图 | `FlowCanvasView.xaml` `Minimap` + `ViewportLocation/Size` TwoWay | 新增 `FlowMinimapSurface`，复用 `IFlowCanvasGeometry` | 低 |
| 13 | 工具箱拖入 | gong 拖放：`FlowCanvasView.xaml.cs:47` + `CanvasDropTarget:296` | 需保留一个可命中元素作为 gong 放置端（**不许改裸 Drop**，拖影只由 gong 放置端关闭） | 中 |
| 14 | 键盘（Delete/Ctrl+A/Ctrl+C/V/Z/Y/Esc） | VM 命令：`OnDeleteSelection:2481`、`OnCopyNode:2387`、`OnPaste:2429`、`OnTidyLayout:2622`、Undo/Redo | 自绘层需 `Focusable=true` + `FocusVisualStyle`（R135） | 低 |
| 15 | 粘贴落点 | `FlowCanvasView.xaml.cs:56 OnEditorPointerMoved` + `:244 ToGraphPosition` | `ToGraphPosition` 只需 `Pan/Zoom`，可直接复用 | 低 |
| 16 | 连线右键 | `FlowCanvasView.xaml.cs:213 OnConnectionContextMenuOpening`（顺序链整体拦掉） | 待写：命中连线 → 同一份共享菜单 + 同样的"顺序链不给菜单"判据 | 低 |

### 4.1 渲染层已有、可直接复用的能力

| 成员 | 位置 | 说明 |
| --- | --- | --- |
| `Nodes` / `Links` | `FlowCanvasSurface` | 冻结快照（几何、ZOrder、标记） |
| `HitTest(Point)` | 同上 | 取最高 ZOrder，泳道/容器框都参与 |
| `ContentBounds` / `ComputeFitViewport` | 同上 | 适应全图、小地图共用 |
| `Zoom` / `Pan` | 同上 | 图坐标 ↔ 屏幕的唯一换算入口 |
| `LastDrawnNodeCount` | 同上 | 诊断：证明裁剪生效 |
| `Fade(brush/pen)` | 同上 | 禁用态 alpha 预乘（**禁止改回 PushOpacity**，差 100 倍） |

### 4.2 断言（第 0 步已落地）

`FlowCanvasChecks --surface-guards`：22 条（框选几何 9 / 批量位移 7 / FitToScreen 6），含反向断言。
`FlowCanvasChecks --surface-parity`：65 条几何一致性 + 出图。

第 2 步每接一项交互，都要有对应断言；现有 19 条查可视树/模板的断言（FLC-8/9/10/16/17/21×4/22/23 等）需同步改写。

---

## 五、已知偏差预判（R130）

| 偏差点 | 说明 | 处理 |
| --- | --- | --- |
| 字体度量 | 自绘用 `FormattedText`，与 XAML `TextBlock` 的行高/字距可能有 1~2px 差 | 允许，写入"已知偏差" |
| 图标字形 | FontAwesome 私有区字形在自绘层需自行加载字体，当前用文字占位 | 第 2 步接入字体 |
| 连线路由 | 现 `StepConnection` 是正交折线 + 箭头；自绘是三次贝塞尔 | **需确认**：保留贝塞尔还是复刻正交 |
| 文本截断 | XAML 用 `TextTrimming`，自绘需自己按宽度截断加省略号 | 已按宽度裁剪，省略号待补 |
| 焦点态 | 自绘层是单个元素，键盘焦点只有一圈 | 需自定义 `FocusVisualStyle`（R135） |
| 高 DPI | `FormattedText` 的 `pixelsPerDip` 变化要清字形缓存 | 已实现（`_pixelsPerDip` 变化清 `_textCache`） |

---

## 六、验收方式（R132）

1. 外部评估方按 §四 的 16 行逐条评估工作量与风险，给出"可接受 / 需调整"结论。
2. 第 2 步每完成一项交互：截图 + 对应断言 + 与本节表格逐项对照。
3. 全部接完后用开关让两份画布并存（默认仍走 Nodify），用户真机验收后再翻默认。

---

## 七、v1 被否决的原因（留档）

| 否决点 | 说明 |
| --- | --- |
| 曲线凌乱 | 贝塞尔线跨越大半张画布，多条交叉后无法判断谁引用谁 |
| 引用难读 | 数据依赖只能"顺着线找"，没有文字化表达 |
| 全量铺开 | 与 PLC 的做法相反；3000 节点一次全画，既不必要也不可读 |

v0.2 的对策见 §〇：单列梯形 + 左轨正交短线 + `← 源名` 标签 + 分页限量。

## 八、v0.2 需要重新评估的代码点

§四 的 16 行仍适用，但下面几项**语义变了**，评估时按 v0.2 口径重算：

| 代码点 | v1 口径 | v0.2 口径 |
| --- | --- | --- |
| `FlowLayoutStore` 的 X/Y、`AutoLayout` | 保留，二维排位 | **不再使用**（顺序即布局）；字段可保留但忽略 |
| `ComputeContainerGeometry` / `_frameRects` / 泳道矩形 | 参与命中与落点 | 删除或退化为"缩进层级" |
| 拖动节点 | 改坐标写回 | **改执行顺序**（插入线 + `ResolveOwnerByPoint`） |
| 拖线建绑定 | 自由连两端 | 从左侧竖轨/下拉选源，或只允许相邻行 |
| 框选 / 多选拖动 | 二维矩形框选 | 可去掉（列表式改为 Shift 连选） |
| 虚拟化 | 必须做 | **不需要**（一页 120 行） |
| `FlowCanvasSurface` 现有几何契约 | 二维矩形 + ZOrder | 改为行模型（行高 + 缩进），`HitTest` 变行命中 |

---

## 九、v0.2 也被否决：视觉语言是 PLC 的（2026-10-24 二次评审）

用户评语：**"太丑了，不像现代软件，不要沿用 PLC 的风格"**。

复盘 v0.2 的问题：

| 问题 | 说明 |
| --- | --- |
| 视觉语言错位 | 「左轨竖线 + 缩进 + 等宽行」是电气图纸/组态软件的语汇，不是现代桌面软件的语汇 |
| 把"限量"做成了"翻页表格" | 现代软件用**虚拟滚动 / 视口窗口**表达"只显示一屏"，而不是页码 |
| 丢失空间结构 | 二维关系（谁在谁旁边、哪条支路更长）被压平后，用户反而更难建立全局印象 |

**教训**：用户要的是「**不要一次画几千个**」和「**引用要读得懂**」，不是「改成梯形图」。

---

## 十、v3（当前基线候选）：现代视觉 · 两个方向

文件：`design-v3.html`（自包含，顶部可切换 A/B）。

同一套现代令牌：`#F8FAFC` 底 + 点阵网格、白卡片 `10px` 圆角 + `1px #E2E8F0` 描边 + 极轻阴影、
主色 `#4F46E5`、正文 13/600 + 副标题 10.5 `#94A3B8`、分组用**虚线圆角容器**（不是框体几何）。

| | **A · 节点图** | **B · 流水线列表** |
| --- | --- | --- |
| 布局 | 保留二维空间结构（列 = 深度，纵向 = 顺序） | 按执行顺序的卡片流，分组 = 可折叠小节 |
| 连线 | **正交折线 + 8px 圆角**，不再有贝塞尔 | 相邻行左侧细线；跨页/远距离用 `← 源名` 标签（点击跳页） |
| 引用可读性 | **聚焦高亮**：选中/悬停一个节点，其余降到 40% 透明，相关线高亮 | 文字化：`← 源名` 标签常显 |
| 限量方式 | **视口裁剪**（HUD 显示"本帧绘制 N"） | **分页**：一页 120 步，3000 步 = 25 页 |
| 实现风险 | **低**：沿用现有二维语义，`FlowLayoutStore`/`AutoLayout`/容器几何都保留 | **中高**：画布语义改为有序列表，X/Y 失效 |
| 3000 步实测 | 视口内只画 8~11 个（一帧 <10 ms） | 一页 121 行（一帧 5.6 ms） |

### 我的判断

- 如果目标是「**先让 3000 步不卡，且不动现有编辑习惯**」→ **A**。它只需要把曲线换正交 + 加聚焦高亮 + 视口裁剪，
  现有 C2 自绘层的几何全部可用。
- 如果目标是「**彻底换一种更易读的编排视图**」→ **B**，但代价是画布语义变更（X/Y 失效、拖拽改序、拖线改选源），
  实现量比 A 大，且用户要接受"不能自由摆放"。

**推荐先做 A**：它同时解决"丑"（现代视觉）和"引用难读"（聚焦高亮），风险最低；
B 可以作为 A 之上的**第二个视图模式**（同一份数据、切换展示），不必二选一。

---

## 十一、v3 也被否决 → v4：不换风格，只治引线（2026-10-24 三次评审）

用户评语：**"还是按照现在的风格去试，引线变清晰、不那么乱；还是树状会好一点；基于现有去设计"**。

### 11.1 一个必须先纠正的事实

现有画布**本来就是直角走线**：`FlowCanvasView.xaml:1188` 用的是 `nodify:StepConnection`
（注释写明"台阶式正交走线（海康式直角折线），替换此前 LineConnection"）。

所以"曲线凌乱"不是走线算法的问题，而是**线太多**：用户 `AppConfig.json` 里 `ShowDataLinks=true`，
一条 353 步的流程会同时画出 353 根数据线 + 450 根顺序链。v1/v2/v3 里我加的贝塞尔曲线
**现有产品里根本不存在**，等于在解决一个不存在的问题。

### 11.2 v4 的做法（`design-v4.html`）

**视觉、尺寸、容器/泳道几何、直角走线全部沿用现有实现**，只动两件事：

| 改动 | 说明 | 效果 |
| --- | --- | --- |
| ① 顺序链走"脊柱" | 底缘中点 → 顶缘中点的竖直短线（与真机 `SourcePosition=Bottom / TargetPosition=Top` 同口径） | 长距离绕行消失 |
| ② 数据线三态：关 / **聚焦** / 常显 | 默认**聚焦**：不选任何节点时不画数据线；悬停/选中某节点时只画与它相关的线，其余线降到 12% | 默认态交叉数 **0** |

`design-v4.html` 顶部可实时切换「走线 × 数据线」六种组合，并显示**估算交叉数**：

| 组合 | 连线数 | 交叉 | 观感 |
| --- | ---: | ---: | --- |
| 现状贝塞尔 + 常显 | 14 | — | 满屏曲线交叉（用户的原始抱怨） |
| 正交 + 常显 | 14 | 3 | 直角可追，但仍密 |
| **正交 + 仅聚焦（默认）** | **9** | **0** | 只剩执行脊柱，点谁看谁 |

### 11.3 实现成本（关键）

这是**视图层行为改动，不是渲染重写**：

- `FlowCanvasViewModel.BuildLinks` 里把 `ShowDataLinks` 从 bool 扩成三态（关 / 聚焦 / 常显）；
- 聚焦态由 `SelectedStepModel` / 悬停节点驱动，**只过滤 `Links` 集合**，不碰几何与锚点；
- `CanvasLinkViewModel` 已有 `SourcePosition/TargetPosition`，脊柱改 Bottom→Top 只需在
  `UpdateLinkAnchors` 的顺序链分支里改两个枚举值（现在数据线与顺序链共用左右边）；
- 与 C2 自绘层无关——**现在的 Nodify 画布就能做**，不需要等自绘。

工作量估计 **0.5~1 天**，且不改任何落盘字段。

---

## 十二、v5（当前方案）：选中即聚焦 · 引线可判读

文件：`design-v5.html`。**沿用现有画布视觉与尺寸**，只重做"数据引线怎么显示"。

### 12.1 交互规则

| 状态 | 画布 | 右侧面板 |
| --- | --- | --- |
| **未选中** | 只画执行顺序脊柱（灰色虚线）；**不画任何数据引线** | 显示操作提示 |
| **选中某算子** | 只画**指向它**的数据引线；无关节点压到 35% 透明；来源节点加淡蓝环 | 列出全部入向引用：编号 + 「输入端口 ← 源算子.输出端口」+ 来源 |
| **悬停右侧条目** | 该条线加粗高亮，其余降到 25%；线上展开完整文字标签 | 该行高亮 |
| **点空白** | 取消选中，回到"只有脊柱" | 回到提示 |

### 12.2 三条防"分不清"的硬规则

1. **一引线一通道**：第 i 条线走 `目标左缘 - 26 - i×16` 的独立竖直通道，
   从源节点右缘的不同高度出发、进入目标左缘的不同高度 —— **任何两条线都不共线、不合并**。
2. **编号贯穿**：线上徽标 ①②③ 与右侧图例编号一一对应；全局变量用紫色 + 虚线 + 独立编号色，与算子间连线一眼区分。
3. **文字不叠在画布上**：画布默认只画编号；完整文字（端口/源算子/端口）在右侧图例常驻，
   **仅悬停某行时**才在对应线上展开——所以多线汇聚时不会出现文字互相压盖。

### 12.3 标注内容

- 线上/图例主行：`输入端口 ← 源算子名.输出端口`，例：`Score ← 模板匹配_0.Score`
- 图例副行：`来源：模板匹配_0`（全局变量显示 `来源：Global.DelayMs`）
- 与现有 `CanvasLinkBinding.Display` 的口径一致（`$"{DescribeInput} ← {producer.Header}.{producerPort}"`），
  不新增数据字段。

### 12.4 实现映射（仍不需要自绘层）

| 设计点 | 落点 |
| --- | --- |
| 默认不画数据线 | `BuildLinks`：`ShowDataLinks` 由 bool 扩为 `Off / Focus / Always`，`Focus` 且无选中 → 跳过所有非非法数据线 |
| 选中后只画相关线 | 同上：`Focus` 且有选中 → 只保留 `Target.StepId == SelectedStepId` 的线（+ 非法线照常显示） |
| 无关节点压暗 / 来源加环 | `CanvasNodeViewModel` 新增 `IsFocusDimmed` / `IsFocusSource` 两个派生只读属性，模板里 DataTrigger 改 Opacity 与描边 |
| 一引线一通道 | `FlowCanvasViewModel.UpdateLinkAnchors`：对选中节点的入向线按索引给 `TargetAnchor.Y` 与一个新增的 `RouteOffsetX` 递增偏移；`ModuleLinkTemplate` 的 `StepConnection` 用 `TargetPosition=Left` 即可 |
| 编号徽标 | `CanvasLinkViewModel` 新增 `FocusIndex`（0 起），模板里加一个 16px 圆点 + 数字 |
| 右侧图例 | 新增一个 `ItemsControl`（绑定 `SelectedStepLinks`），放在画布右侧或属性面板上方；行 hover 用 `Tag`/附加属性回写 `HoveredLinkIndex` 驱动高亮 |
| 顺序链脊柱 | `UpdateLinkAnchors` 顺序链分支：`SourcePosition=Bottom`、`TargetPosition=Top`（当前是左右） |

工作量估计 **1~1.5 天**（比 v4 多一个图例面板与两个派生属性），不改任何落盘字段、不动 Nodify 配置。

### 12.5 待你确认的三点

1. 图例面板放**画布右侧浮层**（如设计稿）还是**属性面板上方**（不占画布宽度）？
2. 选中时是否也要显示**出向**引用（该算子的结果被谁用了）？设计稿目前只做入向。
3. 全局变量引线用紫色虚线（现稿）还是与普通引线同色、仅靠编号区分？

---

## 十三、v6（当前设计稿）：v5 的代码对齐版（2026-10-24）

文件：`design-v6.html`。**v5 的三个问题在 v6 里改掉**，其余沿用 v5。v5 保留不动（`design-v5.html` 仍可对照）。

### 13.0 v5 与代码不一致的三处（审查结论摘要）

| # | v5 的写法 | 代码现状 | v6 的处理 |
| --- | --- | --- | --- |
| ① | 图例做成**画布右侧 320px 浮层** | 画布顶栏已有 3 条式图例（`FlowCanvasView.xaml:1608-1657`），但**没有**逐条列表面板；右侧浮层会挤掉画布宽度 | 落到**画布底部 / 属性面板上方**的横向明细条，列自适应 1~3 列（本次拍板） |
| ② | 全局变量画"紫色虚线 + 独立编号" | 全局变量/常量**根本不进连线**：`FlowCanvasViewModel.cs:1796-1805` 把它们折成节点左下角标（`NodeExternalBadge`，12px 胶囊），不占画布 | 保持角标；明细条里给"外链"行说明（本次拍板：不画线） |
| ③ | 实现映射里写"顺序链当前是左右，要改 Bottom→Top" | **已经改过了**：`FlowCanvasViewModel.cs:1977-2024` 顺序链已用 Bottom→Top + `SharedVerticalX` 共线拉直 | 从 v6 的待办里删掉 |

另外三处**数值**口径对齐：

| 项 | v5 | 代码事实 | v6 |
| --- | --- | --- | --- |
| `ColumnGap`（列间距） | 64 | `FlowLayoutStore.cs:318` = **24** | 24 |
| 运行中蓝 | `#2563EB` | `Colors.xaml:291` `FlowCanvasRunningBrush` = **#3B82F6** | #3B82F6 |
| 数据线/顺序线/非法线 | 自定 | `Colors.xaml:297-299` `#3B82F6` / `#94A3B8` / `#EF4444` | 同值 |

### 13.1 令牌映射表（R117 口径）

| 设计稿变量 | 代码令牌 | 出处 |
| --- | --- | --- |
| `--link` | `FlowCanvasLinkBrush` `#3B82F6` | `UI/Controls/Themes/Colors.xaml:297` |
| `--run` | `FlowCanvasRunningBrush` `#3B82F6` | 同上 `:291` |
| `--order` | `FlowCanvasOrderLinkBrush` `#94A3B8` | 同上 `:299` |
| `--fail` | `FlowCanvasLinkIllegalBrush` `#EF4444` | 同上 `:298` |
| `--warn-bg` / `--warn-bd` / `--warn-ink` | `FlowCanvasExtBadgeBrush` `#FFF7E6` + 边框/文字色 | 同上 `:419` 起（外链角标一族） |
| `--bad-bg` / `--bad-bd` / `--bad-ink` | 失败态浅底 + `FlowCanvasFailedTextBrush` 一族 | 待落 WPF 时确认具体键名（见 §13.4 待办） |
| `--card` / `--card-bd` / `--ink` / `--ink3` | `FlowCanvasNodeBrush` / `FlowCanvasNodeBorderBrush` / 文本令牌 | `Colors.xaml` 现有节点一族 |
| `--frame-bd` / `--frame-hd` / `--lane-bd` / `--lane-ink` | 容器/泳道现有一族 | `Colors.xaml` |

尺寸常量全部取自 `Core/Models/FlowLayoutStore.cs`：`NodeWidth 156`(:278)、`NodeHeight 58`(:279)、`CollapsedFrameHeight 50`(:283)、`LanePadding 14`(:286)、`FramePadding 16`(:288)、`FrameHeaderHeight 46`(:289)、`RowGap 28`(:315)、`ColumnGap 24`(:318)；另有 `EdgeAnchorInset 12`（`FlowCanvasViewModel.cs:2032`）与 v6 新增的通道间距 `SLOT_STEP 16` / `SLOT_NEAR 26`。

### 13.2 v6 相对 v5 的规则修正（都是"能落地"的修正）

1. **明细行 = 一条绑定，连线 = 一对（生产,消费）**。现状 `CanvasLinkViewModel` 就是一对一步骤、多绑定折成 `×N`（`:105-120`），
   所以 v6 的编号是**明细行编号**，线徽标显示该线承载的**起始编号**（多绑定显示 `①×2`），不假装一条线只对应一条绑定。
2. **通道占位只按"不同连线"递增**：`x = 目标左缘 − 26 − 连线序号×16`，入口 y 按连线序号均分 `目标.y + 高×(i+1)/(n+1)`；
   同源同目标的多条绑定**共用一条线**（不另开通道），避免"同一对节点的两条线只差 16px"再次变乱。
3. **文字只在悬停行展开**，且整屏只有一根线时直接展开（否则用户看不到任何文字）。
4. **点空白取消选中**要新增代码路径：现在选中态唯一真相是 `workspace.CurrentStep`（`FlowCanvasViewModel.cs:1507-1589` 双向同步），
   `NodifyEditor` 空白区目前没挂"清选中"。

### 13.3 已知偏差（R130 预判，逐条写明是否可接受）

| # | 偏差 | 原因 | 是否可接受 |
| --- | --- | --- | --- |
| 1 | 画布上是 Canvas 2D 绘制，WPF 是 Nodify 连接件 | 设计媒介不同 | 可接受（形状/层级一致即可） |
| 2 | 明细条最高 172px，条目多时滚动 | 属性面板上方空间有限 | 可接受，需在实现时给最大高度 |
| 3 | 数字徽标 9px 圆 | 与现有 8px 角标（`NodeExternalBadge`）同一量级 | 可接受 |
| 4 | 设计稿用 `requestAnimationFrame` + `dirty` 标志，WPF 走绑定 | 实现机制不同 | 可接受 |
| 5 | 失败卡片浅底用变量 `--bad-bg` | 需落 WPF 时确认对应令牌键 | **待办**（不得写 hex，R140） |

### 13.4 落地前必须一起改的地方（不改就红）

| 项 | 位置 | 说明 |
| --- | --- | --- |
| `[Z3]` 断言 | `FlowCanvasChecks/CanvasVisualizationChecks.cs:143-175` | 现在钉住"多绑定 → `×2` 不回退"，v6 一旦拆成逐条编号必须同步改写，否则构建红 |
| `ShowDataLinks` 类型 | `Core/Models/AppConfigModel.cs:847`（bool，默认 false）+ `Core/AppSettingsService.cs:142-151` | v6 的"关 / 聚焦 / 常显"若为三态，是设置项 schema 变更（bool → enum + 迁移） |
| 断言里的开关赋值 | `FlowCanvasChecks` 约 25 处 `canvas.ShowDataLinks = true` | 三态化后全部要改 |
| 图例重复定义 | `FlowCanvasView.xaml:1608-1657`（三条线手写的粗细/虚线） | 与 `ModuleLinkTemplate`（`:1337-1404`）值重复，改模板必须同步两处；建议落 WPF 时收成令牌 |

### 13.5 仍需你拍板

1. **出向引用**要不要显示（v6 暂只做入向，与 v5 一致）。
2. "关 / 聚焦 / 常显"三态化要不要做，还是**保持 bool 开关 + 选中聚焦自动生效**（后者不动落盘 schema，工作量小一半）。
3. 明细条默认展开还是折叠（占 172px 高）。

---

## 十四、版本沿革

| 版本 | 文件 | 结论 |
| --- | --- | --- |
| v1 | `design.html` | 否决：曲线凌乱、全量铺开 |
| v0.2 | `design-v2.html` | 否决：梯形/PLC 视觉 |
| v3 | `design-v3.html` | 否决：换风格方向不对 |
| v4 | `design-v4.html` | 只治引线、不动风格；顺序链已按此落地 |
| v5 | `design-v5.html` | 否决：图例落点/全局变量/数值与代码不符（后衍生出 v6） |
| v6 | `design-v6.html` | 作废：用户判定"所有设计稿都不满意"；方向仍是常显 + 悬停展开，未采用 |
| **v7** | `design-v7.html` | **当前设计稿**：连线只读（拖线建绑移除）+ 选中聚焦入向/出向 + 全局变量小节点（紫色虚线）+ 非法线常显 |

---

## 十五、v7（当前设计稿）：连线只读 · 选中聚焦（2026-10-24）

文件：`design-v7.html`（自包含）。**方向由用户拍板**，v1~v6 全部作废（见 §十六）。

### 15.0 用户拍板的五条（原话口径，按条留档）

| # | 决定 | 出处（用户原话摘录） |
| --- | --- | --- |
| 1 | **取消拖动绑定**，建绑只在双击算子的数据绑定弹窗里做 | "我现在不想要拖动绑定，都是在双击后的弹窗进行的绑定" |
| 2 | **画布对连线只读**，只要能把数据之间的连线显示出来 | "我想只读 可以显示出数据间的连线就行" |
| 3 | **只显示选中算子的连线**（省性能） | "而且只显示选择的算子的连线 这样就可以节省性能了" |
| 4 | **入向 + 出向都显示** | "入向 + 出向都显示" |
| 5 | **多选只生效最后一个**；**选中容器不画** | "多选只生效最后一个 / 选中容器时不画，只在选中具体步骤时才画。" |
| 6 | **非法线保留常显** | "保留常显" |
| 7 | **全局变量一根紫色虚线连到"全局变量"小节点** | "一根紫色虚线连到"全局变量"小节点" |

### 15.1 令牌映射表（R117 口径，Hex 只在本节出现）

| 设计稿令牌 | 值 | WPF 落地去处 |
| --- | --- | --- |
| `--link` / `--run` / `--accent` | `#3B82F6` | `FlowCanvasLinkBrush` / `FlowCanvasRunningBrush`（`UI/Controls/Themes/Colors.xaml:291,297`，同名同值，无需新增） |
| `--order` | `#94A3B8` | `FlowCanvasOrderLinkBrush`（`Colors.xaml:299`） |
| `--fail` | `#EF4444` | `FlowCanvasLinkIllegalBrush`（`Colors.xaml:298`） |
| `--warn-*` | `#FFF7E6` / `#F3D19E` / `#8A5A00` | `FlowCanvasExtBadgeBrush` 一族（`Colors.xaml:419` 起）——v7 不再用于画布线，仅底部状态条沿用 |
| `--global` | `#7C3AED` | **需新增** `FlowCanvasGlobalLinkBrush`（代码里现无紫色系令牌） |
| `--global-soft` / `--global-bd` | `#F5F3FF` / `#DDD6FE` | **需新增** `FlowCanvasGlobalNodeBackgroundBrush` / `...BorderBrush` |
| `--dir-in` 一族 | `#0E7490` / `#ECFEFF` / `#A5F3FC` | **需新增**（明细行"入向"标记）；若嫌令牌过多，可退化为 `--ink3` 单色 |
| 尺寸 | Node 156×58 / RowGap 28 / ColumnGap 24 / LanePadding 14 / FrameHeader 46 / FramePadding 16 / EdgeAnchorInset 12 | `Core/Models/FlowLayoutStore.cs:278-318` + `FlowCanvasViewModel.cs:2032`，全部复用，不新增 |
| 全局变量小节点 | 104×34，右距 104，纵向间距 8 | **新增常量**，建议进 `FlowLayoutStore`（与 `NodeWidth` 同处），不要写死在视图里 |

### 15.2 v7 的四条新规则（相对 v6）

1. **连线只读**：删除 `NodeInput`/`NodeOutput` 连接点、`FlowConnectorStyle`、`PendingConnectionTemplate`、`StartConnectionCommand`/`CompleteConnectionCommand`、`UpdateConnectableFeedback`/`ResetConnectableFeedback` 以及 `CanvasConnectorViewModel.IsConnectable`/`IsDragInProgress`。建绑只剩 `ModuleLinkRequested` 一族入口（双击算子 → `DataBindView`，`FlowCanvasView.xaml.cs:189` 只吃 `TargetStep`，不依赖拖线快照）。
2. **选中聚焦 = 入向 ∪ 出向**：入向 `refs where Target.StepId == SelectedStepId`，出向 `refs where Producer.StepId == SelectedStepId`。两份都要在 `BuildLinks` 的同一遍里收集，**不许再遍历两次全量**（否则性能收益减半）。
3. **容器不画**：`Kind != CanvasNodeKind.Step` 时不产生任何 `SelectedXxxLinks`（容器是 `Lane`，不是步骤）。这条同时挡住"选中框把一整条泳道的线全画出来"的爆炸场景。
4. **全局变量走小节点**：不再折成节点左下角外链角标（现 `NodeExternalBadge`，`FlowCanvasView.xaml:658-688`），改为在**消费算子右侧一列**画紫色虚线连到"全局变量"小标签（104×34，紫色 `#7C3AED`）。角标可保留作为"未选中时的存在提示"，但选中时必须换成线。

### 15.3 编号与方向

- **入向徽标 ①②③…**（圆形蓝底白字），**出向徽标 Ⓐ Ⓑ ⒞…**（同色，右上角带缺口环以示区别），**全局变量徽标 G1/G2…**（紫底）。
- 编号只在**选中该算子期间有效**，重新选中即重排；因此编号不进模型、不落盘、不做稳定 id——它是"这一屏这一次"的定位符。
- 明细行（底部面板）与画布徽标**一一对应**，悬停行 → 对应线加粗到 2.8、其余降到 25%；文字只在悬停行展开（默认画布上只有数字，见 v5 §12.2 的"文字不叠在画布上"）。

### 15.4 落地前必须一起改的地方（不改就红）

| # | 位置 | 为什么 |
| --- | --- | --- |
| 1 | `FlowCanvasChecks/CanvasVisualizationChecks.cs:143-175`（[Z3]） | 现钉住"单绑定 = `In ← 采集.Out`""多绑定 = `×2` 不回退"。v7 改成一对多线，Z3 必须重写为"入向 1 条 + 出向 1 条 + 编号与明细对应" |
| 2 | `Core/Models/AppConfigModel.cs:847` `ShowDataLinks`（bool，默认 false） | 语义从"全显开关"变成"选中聚焦"，与 `Core/AppSettingsService.cs:142-151` 的 `SyncFlowCanvasSettings` 一起改；旧指纹会静默失效，需迁移或保留键名只改语义 |
| 3 | `FlowCanvasChecks` 约 25 处 `canvas.ShowDataLinks = true` | 全都要迁移到新入口，另有 6 处"默认态数据线一条不画"的反向断言可保留 |
| 4 | `FlowCanvasView.xaml:1608-1657` 顶栏三视角图例 + `:378 FlowConnectorStyle` + `:1773-1810 PendingConnectionTemplate` | 图例要加"全局变量紫虚线"，连接点与拖影整段删除 |
| 5 | `FlowCanvasViewModel.cs:1977-2024 UpdateLinkAnchors` | 出向线的锚点同样要走 `EdgeMidpoint`（**容器的左右锚点取头带中心**，`:2062-2077`），并复用 `SharedVerticalX` 的直连规则 |
| 6 | 颜色令牌 | 见 §15.1 四处"需新增"，落 `Colors.xaml`；视图里不许出现 hex（R140） |

### 15.5 仍需你拍板

1. 选中时**顺序链要不要跟着压暗**？（v7 里保留原样，只把无关节点压到 32%）
2. **出向线要不要也画箭头**？（v7 画了：入向箭头指向我、出向箭头指向下游；若嫌噪，可只留入向箭头）
3. 全局变量小节点是**每个消费算子一份**（v7 现在这样），还是在画布右侧放**一个全局变量栏**统一挂？

---

## 十六、版本沿革（含否决原因）

| 版本 | 文件 | 结论 |
| --- | --- | --- |
| v1 | `design.html` | 否决：曲线凌乱、全量铺开 |
| v0.2 | `design-v2.html` | 否决：梯形/PLC 视觉 |
| v3 | `design-v3.html` | 否决：换风格方向不对 |
| v4 | `design-v4.html` | 只治引线、不动风格；顺序链已按此落地 |
| v5 | `design-v5.html` | 否决：图例落点/全局变量/数值与代码不符 |
| v6 | `design-v6.html` | 作废：用户判定"都不满意"，方向（常显 + 悬停展开）未被采用 |
| v7 | `design-v7.html` | 否决：用户原话「你自己不审查吗 这么乱 用户能接受？」——贝塞尔大弧横跨全图、编号贴卡片外沿、紫标签戳出容器边 |
| **v8** | `design-v8.html` | **当前设计稿**：正交折线 + 走线避让（零穿越）+ 线上端口名 + 全局变量紫虚线 |

---

## 十七、v8（当前设计稿）：正交折线 + 走线避让（2026-10-24）

### 17.0 v8 相对 v7 改了什么（针对 v7 被否决的三条根因）

v7 被否决不是配色问题，是**走线没有任何避让**。v8 逐条对着改：

| v7 的问题 | v8 的做法 |
| --- | --- |
| 贝塞尔大弧横跨全图、横穿容器内区 | **全部改正交折线**，拐点只在通道上；`route()` 打分挑穿越最少的通道 |
| 连线从同列兄弟算子身上竖穿过去 | **列间通道**：竖向走线 X 落在节点带外侧（`CH_GAP=28`），不再落在列内 |
| 编号贴卡片左外侧、与图标争位 | 徽标移到**卡片内侧**：入向在右上角（圆）、出向在右上角（方）、执行序号在左上角 |
| 全局变量紫标签戳出容器边 | 全局变量改成**小节点**（112×34），挂在消费算子所在容器的右侧列外，纵距 8 自动避让 |
| 顺序链太淡、看不清 | 顺序链加**白衬边 3.6px**（打底）+ 灰虚线 1.4px `[5,4]` |
| 无关节点压到 32% 几乎看不见 | 压暗改为**只压卡片填充与文字**，容器底色不动；关联节点加 3px 蓝环 |

### 17.1 走线纪律（这是 v8 的核心，实现时必须照抄）

1. **同列且目标在下方 → B→T 直连**，中间不许再加横拉（v7 的 `my=(a.y+b.y)/2` 就是横拉元凶）。
2. **同行 → R→L 直连**。
3. **其他情况 → 三候选通道打分取优**：右侧列外 `bandX2+max(CH_GAP,24)`、左侧列外 `bandX1-max(CH_GAP,24)`、带中点。打分函数：

   ```
   score = 与实体相交段数 × 1000 + 折数 × 6 + 总长 × 0.02
   实体判定：步骤矩形内缩 2（pad=-2）视为实心；容器只判"整体落进框内"，贴左右边框外不算穿
   ```

4. **同侧多线错开**：同一目标的入向按 `(i-(n-1)/2) × CH_STEP(=14)` 沿目标边平移端点，改完端点再走同一套 `route()`。
5. **非法线永远画**（红色 `#EF4444` 虚线 `[7,4]`），且**不参与压暗**——它是错误提示不是噪声。

### 17.2 令牌映射表（v8 增量，Hex 只在本节出现）

| 设计令牌 | 值 | 归属 |
| --- | --- | --- |
| `--link` / `--accent` | `#3B82F6` | `UI/Controls/Themes/Colors.xaml:297 FlowCanvasLinkBrush` / `:291 FlowCanvasRunningBrush` |
| `--order` | `#64748B` | **改自 v7 的 `#94A3B8`**：顺序链带白衬边后需加深，落 `FlowCanvasOrderLinkBrush`（`:299`，需调值） |
| `--fail` | `#EF4444` | `Colors.xaml:298 FlowCanvasLinkIllegalBrush` |
| `--global` | `#7C3AED` | **需新增** `FlowCanvasGlobalLinkBrush` |
| `--global-soft` / `--global-bd` | `#F5F3FF` / `#DDD6FE` | **需新增** 全局变量小节点底色/边框 |
| 出向线 `C.outLink` | `#1D4ED8` | **需新增** `FlowCanvasLinkOutBrush` |
| 顺序链衬边 `C.halo` | `#FFFFFF` | 画布底色之上打底，可写死为 `Brush` 常量 |
| 非法标签 `C.badBg/badBd/badInk` | `#FFF5F5` / `#FCA5A5` / `#B91C1C` | **需新增**（`#B91C1C` 若 `Colors.xaml` 已有 `FailedText` 一族则复用） |
| 尺寸常量 | 全部照抄 `Core/Models/FlowLayoutStore.cs:278-318`（156/58/28/24/16/46/14） | 容器侧锚取头带中心，与 `FlowCanvasViewModel.cs:2062-2077 EdgeMidpoint` 同口径 |
| v8 新增的排版常量 | `CH_GAP=28`、`CH_STEP=14`、`GV_W=112`、`GV_H=34`、`GV_COL_GAP=48`、`LBL_MIN_LEN=54` | 建议进 `FlowLayoutStore`（画布几何的唯一事实源） |

### 17.3 验收证据（可复跑）

自检脚本 `tools/oneoff/v8-check.js`（DOM 桩跑页面逻辑）与 `tools/oneoff/v8-wires.js`（走线巡检）：

```
node tools/oneoff/v8-check.js          -> ALL OK（21 项断言）
node tools/oneoff/v8-wires.js "记录_1"  -> 折线 29 · 真穿越 0
node tools/oneoff/v8-wires.js "累计_2"  -> 折线 26 · 真穿越 0
node tools/oneoff/v8-wires.js "判断_1"  -> 折线 26 · 真穿越 0（选中容器不画数据线）
node tools/oneoff/v8-wires.js ""       -> 折线 26 · 真穿越 0（未选中只剩顺序链 + 1 根非法红虚线）
```

断言覆盖：`listsOf(记录_1) = 入 2 / 出 1 / 变量 2`、明细标题「入向 2 · 出向 1 · 全局变量 2 · 合计 5 条」、明细含 `Score`/类型/`Global.DelayMs`、`数据线穿越步骤 0`、`累计_2 含非法绑定`、`非法线在选中时仍画出（常显）`、`选中容器不画任何数据线`、`容器明细为空态`、`hex 审计：C 令牌块之后裸 hex 0 个`。

### 17.4 v8 自检抓到的两个真实缺陷（写在这里防复发）

1. **`anchorOf` 入参口径不一**：`route()` 传的是 `pos` 条目对象，而 `anchorOf(id, side)` 内部 `pos.get(id)` 只认 id 字符串 → 返回 `null` → `route()` 返回 `[]` → `drawIllegal` 静默 `continue`，**非法红虚线永远不显示**，并且不报错。修法：`anchorOf` 同时接受 id 与 pos 条目；`route()` 直接传对象。
2. **`pos` 条目没有 `id` 字段**（`layout()` 只存 `{x,y,w,h,kind}`）：任何 `pos.get(x.id)` 风格的写法都恒为 `undefined`。**结论：画布几何的入参一律传对象，不要从几何条目反查 id。**

> 两条都属于"静默失效"——不抛异常、不掉帧，只是线不出现。这正是 `v8-wires.js` 存在的意义：**判据是"画出来的折线有没有穿实体"，不是"代码看起来对不对"。**

### 17.5 落地前必须一起改的地方（v8 新增项，接在 §15.4 之后）

| # | 位置 | 为什么 |
| --- | --- | --- |
| 7 | `FlowCanvasViewModel.cs` 新增走线计算 | 正交路由 + 通道打分不能在 XAML 里做，必须在 VM 里算好折点后交给自绘层；Nodify 的 `StepConnection` 只能表达直线/贝塞尔 |
| 8 | `FlowCanvasView.xaml:1337-1404 ModuleLinkTemplate` | 模板级 Trigger 无法表达"一目标多线错开 + 正交折点"，v8 落地时该模板应退化为**只承载端点与标签**，折线走自绘层（`FlowCanvasSurface`） |
| 9 | `tools/oneoff/v8-check.js` / `v8-wires.js` | 两个脚本目前是**设计稿验证器**，落地时应把判据搬进 `FlowCanvasChecks`：`数据线穿越步骤 == 0` 可做断言（用 `pos` 矩形与折点做几何判定），这是 WPF 端唯一能自动化的"乱不乱"判据 |

### 17.6 仍需你拍板（v8）

1. 出向线用**虚线**（现状 `[6,4]` + 深蓝 `#1D4ED8`）区分入向实线——接受还是希望同色同线型只靠箭头方向区分？
2. 全局变量小节点**每个消费算子一份**（现状），是否接受页面上出现多个同名的 `DelayMs` 小节点？
3. 明细条固定在画布底部（`max-height 196px` 可滚动）——是否接受，或希望可折叠成一行标签？

---

## 十八、v9（当前设计稿）：按真实模板渲染（2026-10-24）

**触发**：用户看 v8 稿的原话「你现在的UI没有使用我项目上的吗 怎么算子框这么丑」「按照我真实模版去渲染」。
**结论**：v8 的**画布机制整体保留**（连线只读、选中聚焦入向 + 出向、正交走线、底部明细条、非法线常显），只把**算子卡 / 容器 / 泳道**从"我臆造的简化方块"换成项目真实模板的 1:1 复刻。文件：`design-v9.html`（新文件，v8 留档）。

### 18.1 取材位置（全部逐项抄自源码，不是估的）

| 复刻对象 | 唯一事实源 |
|---|---|
| 算子卡 | `VisionMaster/Views/FlowCanvasView.xaml` `StepCardStyle`(294) + `StepNodeTemplate`(438-759) |
| 容器框 + 组头 | 同上 `ContainerNodeTemplate`(762-1108) |
| 泳道（分支列） | 同上 `LaneNodeTemplate`(1111-1325) |
| 连线 | 同上 `ModuleLinkTemplate`(1337-1404) |
| 颜色令牌 | `UI/Controls/Themes/Colors.xaml`(251-438) |
| 尺寸常量 | `Core/Models/FlowLayoutStore.cs`(278-318) |
| 分类色 + 图标码点 | `VisionMaster/Helpers/ToolboxCategoryVisuals.cs`(48-67) |
| 文案口径 | `VisionMaster/ViewModels/CanvasNodeViewModel.cs`（`OpBadge` 263 / `SubTitle` 303 / `IconGlyph` 433 / `TypeBadge` 444 / `ContainerMetaText` 498 / `BranchBadge` 556 / `LaneName` 596 / `LaneCountText` 614 / `CollapseGlyph` 644） |
| 图标字体 | `UI/Controls/Asserts/FontFamilys/Font Awesome 6 Pro-Solid-900.otf`（`App.xaml:11` 的 `Icon` 资源指向它） |

### 18.2 复刻到的具体数值（WPF 复刻时按这张表核对）

**算子卡**（156×58，圆角 **10**，边 `FlowCanvasNodeBorderBrush` 1px，内距 `10,8`）
- 图标块 28×28 圆角 8：底 = 分类色混白 **0.88**、边 = 混白 **0.65**、图标 FA6 **14px** 分类色
- 标题 **11.5 SemiBold**，`MaxWidth=72` 截断（v8 按字符截，v9 改按像素）
- 类型徽标 H14 / Padding 4,0 / 圆角 3 / 底 `#3B5B8C` / 白 8px Bold（WAIT 等）
- 操作徽标 H14 / 圆角 4 / 底 `LogicCaseSoft` / 边 `LogicCaseBorder` / Consolas 8 Bold（EQ·GT·AND…）
- 副标题 9px，右侧留 30px
- 右上竖槽：状态点 7×7（`Margin 0,7,8,0`）、断点圈 8×8（边 `#C0C4CC`）、必填缺失叹号 14×14（FA `&#xf06a;` 10px `#E6A23C`）
- 左上执行序号 16×14 圆角 4（`Margin -7,-7`，底 `#0F172A`，Consolas 8 Bold 白）
- 左下外链角标 12 高 / Padding 4,0 / 圆角 6（底 `#FFF7E6` / 边 `#E6C98A` / 字 `#9A6B0F`）
- 右下耗时 Consolas 8
- 禁用 = 标题删除线 + 整根 0.45 透明度；选中 = 边 `#2563EB` 2px

**容器**（圆角 **12**，边框 **1.5px** `#A5C0E8`，底白）
- 组头 **46** 高，底 = 类型渐变 `stop 0 = 类型 soft`、**`stop 0.76 = #FFFFFF`**，底边 1px，`CornerRadius 10,10,0,0`
- 组头六列（`Margin 9,0,6,0`）：4×26 色条 → H20 类型徽标 → 12.5 标题 → H20 条件胶囊（Consolas 9 `#3B5B8C`）→ 8.5 统计「N 分支 · M 算子」→ 状态点
- 折叠钮 18×18 圆角 5；容器序号 24×18 圆角 5（`Margin -9,-9`）
- 并行汇合条 22 高（仅 Parallel），底 `#F0F9FF` / 边 `#BAE6FD`

**泳道**（圆角 **8**，底 `#0F3B82F6`、边 `#264B82F6` 1px）
- 头 **28** 高，底 `#B3FFFFFF`（70% 白），底边 1px，`CornerRadius 7,7,0,0`
- 徽标 H**16** 圆角 4（IF / ELSEIF / ELSE / BODY / A·B·C），名 **9.5 SemiBold**，计数 Consolas **8.5**

**连线**（`ModuleLinkTemplate` 的台阶式正交）
- 数据线：Stroke **1.5** + 外轮廓 `#F7F8FA` **1.6**（交叉断口）
- 顺序链：`#94A3B8` **1.2** dash **2.5,2.5**
- 非法：`#EF4444` **1.5** dash **4,3**
- 标签：底 `#F2FFFFFF`、圆角 **4**、Padding **5,1**、**10.5 SemiBold**、字 `#1D4ED8`

### 18.3 v9 走掉的三个真实缺陷（写在这里防复发）

1. **流水线步骤没带 `group`**：`layout()` 登记分支步骤的 `pos` 时漏了 `group` 字段，`dataPorts()` 靠它判断"在不在容器里"，漏了就把容器内算子当顶层节点，数据线直接从容器内部横穿出去。一条错导致 19 根线穿越。
2. **"只看两端"的通道 x 不够**：单列竖排下，一条从顶层算子出发、跨过中间容器的线，若只按两端自己的框算竖通道 x，竖段会正好落在中间容器内部（`结果汇总_0` 的入向线 x=184 穿过 `循环_2`）。修法：通道 x 要**把 y 区间重叠的所有容器右边界一起纳入 max**。
3. **容器内目标不能取卡片右边缘**：卡片右边缘在容器内部，从那里进线会横穿泳道；必须取**容器右边框中线**，再沿泳道中线横入。

### 18.4 验收证据（可复跑，命令用 `pwsh`）

```powershell
node tools/oneoff/v9-check.js     # 19 项断言 + hex 审计 → ALL OK —— 19 passed, 0 failed
foreach($s in @('','记录_1','累计_2','判断_1','结果汇总_0','循环体_2','几何查找_0','OK','通过_1','图像采集_0','循环_2','异常_1','模板匹配_0')){ node tools/oneoff/v9-wires.js $s | Select-String '有穿越' }
# 13 种选中态全部：折线总数 166~183 · 有穿越 0
```

- `v9-check.js` 断言覆盖：13 节点 + 5 泳道、分支步骤在容器内、容器高 `46+100×2+16=262`、`记录_1` 入2/出1/变量2、明细标题「入向 2 · 出向 1 · 全局变量 2」、选中容器全 0 + 空态、`累计_2` 非法 1 条、顺序链 / 非法 / 入向 / 全局变量四类线型、外轮廓、六处常量、遍历全部节点不抛异常、hex 审计（C 块内 75 个、`:root` 内 37 个、其余 0）。
- `v9-wires.js` 白名单：`#FFFFFF`/`#F7F8FA`（外轮廓）、`#94A3B8`（顺序链）、`#EF4444`（非法，设计允许跨容器）、五类容器的 `d` 边色与 `failBd` 且线宽 ≤1.01（泳道头底线/容器边框）。判定 pad：步骤 −2、容器/泳道 −6。

### 18.5 落地前必须一起改的地方（接在 §17.5 之后）

1. `FlowCanvasView.xaml` 的 `ModuleLinkTemplate`（1337-1404）要退化为"只承载端点 + 标签"，正交折线由 VM 算（和 v8 的结论一致）。
2. 走线函数（`dataPorts` / `routeBetween` / `pathScore`）是纯函数，建议原样搬进 `FlowCanvasViewModel`，并用 `v9-wires.js` 同款"逐段 4px 采样判相交"做成 `FlowCanvasChecks` 的一条契约（`数据线穿越步骤 == 0`）。
3. 真实模板已经带 `OutlineThickness 1.6` 交叉断口，自绘层若走 `DrawingContext`，画两遍（先轮廓后本色）即可，别在 VM 里存两套线。
4. 图标字体走 `Application` 资源 `{StaticResource Icon}`，**不要在视图里写字体路径**。
5. 本文复刻的 8 处尺寸（卡内距 10,8 / 图标块 28 圆角 8 / 组头 46 / 泳道头 28 / 徽标 16 / 折叠钮 18 / 序号胶囊 24×18 / 外链角标 12）在 WPF 侧已经是常量，**不要把 HTML 里的数字再抄一遍到 VM**，改为引用既有常量。

### 18.6 仍需你拍板（v9）

1. 图例面板要不要加一行"外链角标 / 执行序号 / 断点"的说明（现在只在卡片上画，没有图例）。
2. 空分支、折叠容器这两种形态 v9 稿里**没有演示**，要不要补一个 `?sel=` 场景。
3. 走线一律"右出右入"（单列布局下最省通道），如果将来改成多列布局，通道策略要重新定——是否现在就按多列预留。



