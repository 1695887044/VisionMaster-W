# 操作记录弹窗 · 设计基线

| 项 | 值 |
|---|---|
| 版本 | v0.1 |
| 确认日期 | 待确认 |
| 确认人 | 待确认 |
| 覆盖范围 | 软件级操作记录查看弹窗（只读）：筛选、列表、明细、分页、导出、空/加载/无权限四态 |
| 不覆盖 | 记录写入逻辑（已实现）、方案级审计页签、任何编辑能力 |
| 产物 | `design.html`（可点击，含五态切换器） |

> 本目录不参与构建（R128）。基线只读；改设计必须升版本并同步映射表与已复刻页面。

---

## 1. 布局（1280 × 850，与资产页 / 用户管理同规格）

```
┌ 标题栏 56 ──────────────────────────────────────────────┐
│ ▤ 操作记录   软件级 · 跟机器走，不随方案切换          ✕ │
├ 筛选条 64 ──────────────────────────────────────────────┤
│ 分类▾  操作者▾  时间▾  ⌕搜索…        [导出 CSV][刷新]   │
├ 表头 38 ────────────────────────────────────────────────┤
│ 时间 │ 分类 │ 动作 │ 结果 │ 操作者 │ 对象 │ 明细        │
├ 行区（弹性，可滚动）44/行 ───────────────────────────────┤
│ …                                                       │
├ 明细 96 ────────────────────────────────────────────────┤
│ 明细：完整文本（可复制）                                 │
│ 方案 / 机器 / 版本 / 记录 Id                             │
├ 页脚 56 ────────────────────────────────────────────────┤
│ 共 N 条 · 第 x / y 页        [上一页][下一页][关闭]      │
└─────────────────────────────────────────────────────────┘
```

**为什么是表格 + 下钻，不是卡片流**：这类数据的用法是"按人/按时间/按动作扫一遍找异常"，
列对齐比卡片快；明细放下方固定区，避免每行高度不齐导致纵向扫视断裂。

**列宽**（固定像素，不用星号列——GridView 无星号列宽）：

| 列 | 宽 |
|---|---|
| 时间 | 156 |
| 分类 | 96 |
| 动作 | 130 |
| 结果 | 72 |
| 操作者 | 110 |
| 对象 | 200 |
| 明细 | 剩余（`*`，行内省略号 + 下钻看全文） |

---

## 2. 间距

| 位置 | 值 |
|---|---|
| 弹窗圆角 | 12 |
| 标题栏内边距 | 左 20 / 右 16 |
| 筛选条内边距 | 左右 20，控件间距 10 |
| 表格左右内边距 | 20 |
| 表头高 / 行高 | 38 / 44 |
| 行内列间距 | 12 |
| 明细区内边距 | 12 / 20 |
| 页脚内边距 | 左右 20 |
| 控件高度 | 输入/下拉 30，按钮 32 |

---

## 3. 配色（全部映射到 `UI/Controls/Themes/Dialog` 既有令牌，禁新增 hex）

| 用途 | 令牌 | 值 |
|---|---|---|
| 弹窗底色 | `DialogSurfaceBrush` | `#FFFFFF` |
| 输入/下拉底 | `DialogInputBackgroundBrush` | `#F8FAFC` |
| 描边 | `DialogBorderBrush` | `#E2E8F0` |
| 主文字 | `DialogTextPrimaryBrush` | `#0F172A` |
| 次文字 | `DialogTextSecondaryBrush` | `#475569` |
| 三级文字 | `DialogTextTertiaryBrush` | `#64748B` |
| 弱文字 | `DialogTextMutedBrush` | `#94A3B8` |
| 强调 | `DialogAccentBrush` | `#0EA5E9` |
| 选中底 | `DialogAccentSoftBrush` | `#F0F9FF` |
| 选中描边 | `DialogAccentBorderBrush` | `#BAE6FD` |
| 悬停 | `DialogRowHoverBrush` | `#F1F5F9` |
| 斑马行 / 表头底 | `DialogRowAlternateBrush` | `#F8FAFC` |
| 成功点/字 | `DialogSuccessBrush` / `DialogSuccessTextBrush` | `#22C55E` / `#15803D` |
| 拒绝（警告） | `DialogWarningBrush` | `#F59E0B` |
| 失败 | `DialogDangerBrush` | `#EF4444` |

**分类徽标**三色（左：底/边/字）

- 身份 `#F0F9FF` / `#BAE6FD` / `#0369A1`（复用 Accent 系）
- 安全动作 `#FEF2F2` / `#FECACA` / `#B91C1C`
- 权限 `#F5F3FF` / `#DDD6FE` / `#6D28D9`

> 后两组是**新增语义色**，落地时先进 `Colors.xaml` 登记为令牌（R140），再引用；
> 不在视图里写 hex。

---

## 4. 字号 / 字重

| 元素 | 字号 | 字重 |
|---|---|---|
| 标题 | 15 | SemiBold(600) |
| 标题副标 | 11 | Normal |
| 字段标签 | 10 | Normal |
| 控件文字 / 表格正文 | 12 | Normal |
| 动作名（可扫视列） | 12 | SemiBold(600) |
| 表头 | 11 | Normal |
| 结果文字 | 11 | Normal |
| 明细元信息 | 11 | Normal |
| 徽标 | 11 | Normal |

字体族：`Segoe UI` → `Microsoft YaHei UI`（与 WPF 默认一致）。
时间列用等宽数字（`tabular-nums`），保证纵向对齐。

---

## 5. 五态

| 态 | 表现 |
|---|---|
| 默认 | 斑马行（偶数行 `DialogRowAlternateBrush`） |
| 悬停 | 行底 `DialogRowHoverBrush`，过渡 120ms |
| 选中 | 底 `DialogAccentSoftBrush` + 左侧 3px `DialogAccentBrush` 竖条 |
| 焦点 | 控件 `outline: 2px solid DialogAccentBrush; offset 1px`；行支持键盘上下移动 |
| 禁用 | 首页「上一页」：文字 `DialogTextMutedBrush`、底 `DialogRowAlternateBrush`、光标默认 |

另有三种整页态（右下切换器可看）：

- **空**：图标 + 「没有符合条件的记录」+ 「清空筛选」按钮
- **加载**：三条骨架 + 「正在读取操作记录…」（≥300ms 才显示，避免闪烁）
- **无权限**：锁图标 + 「需要「用户管理」权限」+ 说明为什么敏感

---

## 6. 交互细节

| 动作 | 行为 |
|---|---|
| 打开弹窗 | 默认「近 7 天 + 全部分类」，列表焦点在第一行 |
| 改筛选 | 立即重查（无「查询」按钮）；防抖 250ms |
| 搜索 | 匹配 对象 / 动作 / 明细，防抖 300ms |
| 点行 | 选中 + 下方明细区切换；不弹窗、不跳页 |
| 双击行 | 复制该行明细到剪贴板（提示「已复制」） |
| ↑ / ↓ | 移动选中行，明细跟随 |
| 翻页 | 每页 200 条；翻页保留筛选条件 |
| 导出 CSV | 导出**当前筛选结果**（不是当前页），UTF-8，含表头 |
| Esc / ✕ / 关闭 | 关窗，不写任何记录（查看不算操作） |

**刷新**：手动刷新按钮；不做自动轮询——这是事后追溯用的，不是实时监控。

---

## 7. 已知偏差（WPF 复刻时会出现的，先写在这里）

| 项 | Web | WPF | 是否接受 |
|---|---|---|---|
| 字体度量 | Segoe UI / 雅黑 | 同 | 接受 |
| 行高 | 44 | 44（`MinHeight` + `Padding`，不用 Grid Auto 行） | 接受 |
| 滚动条 | 浏览器默认 8px | WPF 12px，占位 4px | 接受，明细列宽按 `*` 自适应 |
| 阴影 | `box-shadow` 一层 | `DropShadowEffect` 一层（`BlurRadius 14 / Opacity .16`） | 接受 |
| 斑马行 | `:nth-child(even)` | 行容器 `AlternationCount=2` | 接受 |
| 悬停过渡 | CSS 120ms | WPF 无内置过渡（`Trigger` 直接换色） | **接受**：不引入动画，避免和既有表格观感不一致 |
| 徽标新增两色 | `#FEF2F2` 等 | 需先登记 `Colors.xaml` 令牌 | 必须做，否则违反 R140 |

---

## 8. 验收清单（R122，逐项打勾）

- [ ] 弹窗 1280×850，四段高度 56 / 64 / 弹性 / 56，明细区 96
- [ ] 表头列宽与上表一致，明细列自适应
- [ ] 所有颜色来自令牌，视图内无 `#RRGGBB`
- [ ] 五态齐全，焦点可见（键盘 Tab 能走完筛选与翻页）
- [ ] 空 / 加载 / 无权限三态都有文案与出路（不是空白页）
- [ ] 表格挂 `GridListViewStyle` + `GridViewRowPresenter`（R19/R20），列头不消失
- [ ] 翻页与导出遵循"筛选结果"口径
- [ ] 与 Web 稿同屏同态截图对照，差异逐项写进第 7 节

---

## 9. 数据契约（给实现用）
```csharp
// 现有：SqliteSoftwareOperationLog.Query(category, actor, solutionId, limit)
// 需要补：
IReadOnlyList<SoftwareOperationEntry> Query(
    string? category, string? actor, Guid? solutionId,
    DateTime? fromUtc, DateTime? toUtc, string? keyword,
    int offset, int limit, out int total);
```

索引已就绪：`(category, ts_utc)` / `(actor, ts_utc)` / `(solution_id, ts_utc)`。
关键字搜索走 `subject`/`action`/`detail` 的 `LIKE`——90 天量级（几千行）可接受，
真到十万级再上 FTS5，不要现在过度设计。

---

## 10. 渲染证据

用 Edge headless 渲染的四态截图（评审时对照用）：

- `artifacts\render\20261027\operations-log\design-normal.png`（默认 + 选中行 + 明细）
- `design-empty.png`（空态，保留页脚显示「共 0 条」）
- `design-loading.png`（骨架屏）
- `design-denied.png`（无权限）

> 直接看稿：用浏览器打开 `design.html`；`design.html#empty` / `#loading` / `#denied` 可直达对应态。

**渲染时踩的坑（写进设计稿自己的实现里）**：整页态用 `hidden` 属性切换时，
`.state { display:flex }` 会**压过** `hidden`，导致三种态同时显示。
必须补 `[hidden] { display: none !important; }`——WPF 侧对应的是 `Visibility`，
不存在这个问题，但 Web 稿要留着这行，否则评审看到的是三态叠加。
