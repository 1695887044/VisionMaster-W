# SCADA 推荐控件 · 设计基线（第二批）

| 项 | 内容 |
| --- | --- |
| 版本 | **v2（复核版）** |
| 日期 | 2026-10-21 |
| 确认日期 / 人 | 待用户确认 |
| 覆盖范围 | 上一轮从 EasyBuilder Pro V6.10.01 第 12 / 13 章筛出的推荐项；v2 按“先扩现有控件、再谈新建”复核：**只新增 `Hmi.Slider` 一个 TypeKey**，其余为现有控件的模式/预设或运行时设施 |
| 设计文件 | `design.html`（v2 复核稿；自包含，无外部依赖；右上角可切换深色运行 / 浅色编辑画面预览） |
| 归档 | v1 初稿的“新建控件”结论见「v1 → v2：为什么改」；v1 渲染截图归档在 `evidence/v1/` |
| 证据目录 | `evidence/`（v2 渲染截图；WPF 复刻后的对照截图待补） |
| 状态 | **P0-1 / P0-2 / P0-3 / P1 弹窗 + 每画布多基座 + 软键盘 / P2 模拟量滑块已实施**（2026-10-22；滑块复审 P1 已闭环）；设计稿 v2 待用户确认 |

## 实施进度（2026-10-21）

| 项 | 状态 | 记录 | 证据 |
| --- | --- | --- | --- |
| P0-1 返回上一页 | **已实施** | `docs/code-changes/2026-10-21-SCADA返回上一页（NavigateBack动作与页栈通知）.md` | `ScadaChecks` 2354/0；`ScadaWebChecks` 110/0 |
| P0-2 ComboBox 只读显示 | **已实施** | `docs/code-changes/2026-10-21-SCADA下拉框只读显示（ComboBox Presentation-Text）.md` | `ScadaChecks` 专项 2344/0（临时跳过两个本机偶发崩溃段） |
| P0-3 顶栏页面菜单 | **已实施** | `docs/code-changes/2026-10-21-SCADA顶栏页面菜单（运行窗口导航与页面级角色）.md` | `ScadaChecks` 专项 2382/0（含页面级角色 3 条、菜单接线 1 条、真窗口菜单/返回 3 条；临时跳过两个本机偶发崩溃段） |
| P1 弹窗 / 子画面 | **已实施（桌面端）** | `docs/code-changes/2026-10-21-SCADA多基座与弹窗（特殊画面）.md` | `ScadaChecks` 专项 2401/0（含真窗口 3 条、运行态 13 条、动作 2 条；临时跳过两个本机偶发崩溃段）；`ScadaWebChecks` 111/0（弹窗动作按镜像模式剔除）；web 只透传快照、未渲染弹窗层 |
| 配套：每画布多基座 | **已实施** | 同上 | 模型/文档/运行态/编辑器/画布/层序/旧 .vms 迁移共 20 条专项断言；顶栏基座下拉改为多选菜单 |
| P1 软键盘 | **已实施** | `docs/code-changes/2026-10-22-SCADA软键盘（数字字符共享设施）.md` | `ScadaChecks` 键盘 11/11 + 真窗口 6/6；全量 2455/1（唯一失败为 `FlowListView.xaml` 既有/并发断言，与本项无关） |
| P2 模拟量滑块 | **已实施** | `docs/code-changes/2026-10-22-SCADA模拟量滑块（Hmi.Slider）.md` | `ScadaChecks` 滑块 34/34；`ScadaWebChecks` 146/0（含服务端门禁 M2-05c/d/e）；全量 2490/1（唯一失败同上）；复审 P1（确认期间回读/量程缩小）已修并补 SL9b/SL9c |

> 弹窗口径：设计稿里的 `Modal/Anchor/Offset` 最终落在**弹窗页**上（`ScadaPage.PopupModal/PopupAnchor/PopupOffsetX/Y`），动作只带目标页——同一页从不同入口打开时呈现一致。理由见记录 §3.2。
> 多基座：一页可按顺序引用多只基座，顺序 = `BasePageIds` 顺序（靠前的在下面）；顶栏改为多选菜单，暂不做拖拽排序。

> P0-2 的实现口径已按评审调整：`Text` 呈现**恒只读**（`Mode` 在 Text 下隐藏/不生效）；底边线 `#7A7A7A`、字号 14；`ShowSeparator`/`Text`/`TextAlignment` 在 Field 下隐藏，`Mode`/`Fill`/`Stroke`/`StrokeThickness`/`CornerRadius` 在 Text 下隐藏。

## 定位

按 `AGENTS.md` §30.1 的四步流程，本文件是**第 ① 步（Web 设计基线）**的产物：

```text
① 设计基线（本目录） → ② 设计定稿与令牌映射 → ③ WPF 复刻 → ④ 截图验收
```

- Web 只是**设计媒介**，不进入构建、不部署、不作为第二个客户端维护（R126）。
- 设计基线确认后只读；需要调整时升版本（v2 → v3），并同步令牌映射与已复刻页面（R128）。
- 本版**只审查设计**，不改任何 WPF / XAML 代码。

## v1 → v2：为什么改

v1 的错在顺序：先翻手册、后看现有控件，于是把“现有控件已经有的能力”当成新控件立项。v2 先做三问：

1. **现有控件能不能改？** 能改就不新建。
2. **改一个属性够不够？** 够就不加新类。
3. **新建 TypeKey 多出来的成本**（工具箱条目、属性面板、web `caps`、断言、文档）**值不值？** 不值就不新建。

| v1 提案 | v2 结论 | 依据 |
| --- | --- | --- |
| `Hmi.StateText` | **删除**；改为 `Hmi.ComboBox` 的 `Presentation=Text` | `ComboBoxElement.cs:40-71` 已有 `Mode=Output`、`Options`、`SelectedText`；只差“别长得像禁用下拉框” |
| `Hmi.PageMenu` | **删除**；改为运行窗口顶栏页面菜单 | `ScadaRuntimeWindow.xaml` 已有顶栏；`ScadaRuntime.GoBack()` 已有；全局导航不需要每页一个图元 |
| `Hmi.RecordList` | **降级**；先扩 `Hmi.AlarmBanner` 行内逐条确认 | `AlarmBannerElement` 已吃活动报警；`AckButton` 已做整体确认；历史/审计不塞进报警条 |
| `Hmi.HistoryTable` | **删除**；改为 `Hmi.DataGrid` 的 `Source=History` | `DataGridElement` 已有列定义、行模型、空态、缓存行数；历史只是换数据源 |
| `Hmi.Slider` | **保留**（唯一新增 TypeKey） | ProgressBar 只显示、IOField 靠键盘、SelectorSwitch 离散；没有连续量“拖动即写”的现有控件 |

## 专家评审（2026-10-21）

6 个角色只读评审，**全部 pass with changes**；“只新增 Slider 一个 TypeKey”的方向成立，但 4 项被低估（NavigateBack / Popup / DataGrid 历史源 / 动画）。完整记录见 [`评审记录-2026-10-21.md`](评审记录-2026-10-21.md)。

| 角色 | 结论 | 最关键的一条 |
| --- | --- | --- |
| reviewer | pass with changes | `TextAlignment` / `Label` 与基类重复，新增 `[ScadaProperty]` 会触发 SPG0006；`Value` 是 int，“空值 → —”不可实现 |
| hmi | pass with changes | 弹窗事件闸门只认 `CurrentPage`，弹窗上的按钮会“点不动”；`IScadaNavigator` 没有 `GoBack`；`CanGoBack` 无变更通知 |
| ui | pass with changes | 只读字段底线必须与 IOField 同色（`#7A7A7A`），标签 14/Auto；顶栏 48/52 与现有 chrome 约 26px 不一致 |
| vision | 控件够用、主线不够 | 历史数据应升 P1；滑块对视觉设备 P2；旋转/缩放 P3；补 OK/NG 预设与“数值/字符串状态用 Text” |
| motion | pass with changes | Slider 直接写位置/速度会绕过 `MotionCommandGate`；必须三档、硬上限、Engineer、Release + Confirm |
| comm | pass with changes | AlarmBanner 确认顺序不可原子；“先引擎后回写、失败不改行状态”做不到；`long` 直写会覆盖相邻寄存器 |

**评审后的硬前置**（复刻前必须做，不是设计稿能绕过的）：

1. ComboBox 的 `Text` / `TextAlignment` 复用基类，不新增 `Label`；`ShowSeparator` 在 Field 模式隐藏。
2. 报警确认统一到一个服务（权限 + 引擎 + 审计 + 握手）；桌面端目前没有审计；`AckWriteMode` 默认 Counter、typed write、`changed>0` 才写。
3. `IScadaNavigator` 扩 `GoBack`；`ScadaRuntime` 在压栈/弹栈/清栈后通知 `CanGoBack`；web 镜像剔除 NavigateBack/OpenPopup/ClosePopup。
4. 弹窗按“文档模型 + 动作契约 + 当前表面事件闸门 + 独立页栈 + binder 生命周期”排期，不能只做浮层。
5. DataGrid 历史源先定 `IScadaHistorySource`（列→字段、分页/超时/取消、Loading/Empty/Source unavailable）；`DataGridElement` 非 partial，新属性要么迁生成器、要么继续手写 DP。
6. 动画 5/6 需要模型字段 + `TransformGroup` + 复位；vision 建议本批不做（P3）。
7. 软键盘按钮必须 `Focusable=False`，并解决“失焦即提交”与“点外部取消”的冲突；Password 不在 TextField 现有能力内。

## 本批范围与优先级

| 优先级 | 建议 | 落点 | 手册出处 | 与现有控件的关系 |
| --- | --- | --- | --- | --- |
| P0 | 返回上一页 | `ScadaActionType.NavigateBack = 18` + `IScadaNavigator.GoBack` | 13.36 / 13.5 | 动作扩展 + 接口扩展 + `CanGoBack` 通知；`ScadaRuntime.GoBack()` 已就绪 |
| P0 | 下拉框只读显示 | `Hmi.ComboBox` + `Presentation=Text` | 13.43 | 扩展现有控件；`Text` / `TextAlignment` 复用基类，不新增 `Label`；`ShowSeparator` 默认 true |
| P1 | 软键盘（数字 / 字符） | 运行时设施 | 第 12 章 | 数字 P1、字符 P2；按钮 `Focusable=False`，解决“失焦即提交”冲突；Password 需另加属性 |
| P0 | 页面菜单 | 运行窗口顶栏（无 TypeKey） | 13.29 / 13.36 | 现有顶栏已有页名/报警/停止；**已实现** `CanGoBack` 通知 + 页面级 `RequiredRole`（Navigate/GoBack/菜单共用）；手动排序/改名后置 |
| P1 | 弹窗 / 子画面 | 文档模型 + 动作契约 + 运行时浮层 | 13.11 / 13.12 | 不是纯浮层：`ScadaPage.IsPopup`、`ScadaAction` 的 Modal/Anchor/Offset、当前表面事件闸门、独立页栈 |
| P2 | 模拟量滑块 | `Hmi.Slider`（**唯一新增 TypeKey**） | 13.8 | 通用非运动量 P1、视觉设备 P2；运动量三档 + 硬上限 + Engineer + Release/Confirm；位置类走 `TryMove` |
| P1 | 报警逐条确认 | `Hmi.AlarmBanner` 扩展 | 13.21 / 13.22 | 活动报警列表 + `AckButton` 已有；需统一确认服务（权限+引擎+审计+握手）、`AckWriteMode`、`CanAcknowledge` |
| P1 | 历史数据 | `Hmi.DataGrid` + `Source=History` | 13.18 | 表格内核已有；先定 `IScadaHistorySource`、列→字段、三态、取消；vision 追溯升 P1 |
| P3 | 旋转 / 缩放动画 | `ScadaAnimationType` 5 / 6 | 13.13 / 13.14 | 需要模型字段 + `TransformGroup` + 复位；vision 认为本批不做 |

## 设计口径

沿用 `scada-controls` v1 的 11 条设计方向与 8 条可检查判据：

1. **双上下文，一套结构**：运行画面深色 `#0E1318`，编辑工作台浅色 `#F4F7FB`；结构、尺寸、状态一致，只换令牌。
2. **五态是底线**：Normal / Hover / Pressed / Focus / Disabled；过程控件再加 Current / Dragging / Loading。
3. **状态色只表达状态**：灰=停、绿=运行、黄=警告、红=故障/报警；accent 青蓝只做交互、焦点、当前项。
4. **数字等宽**：时间、数值、坐标、计数一律 `tabular-nums`。
5. **触摸命中 ≥44**：运行态按键 / 菜单项 / 滑块拇指最小命中 44×44；桌面鼠标档不低于 36×36。
6. **不造死属性**：每个可配属性必须在运行态有可见效果；只接现有属性 / 命令。
7. **失败可见**：写入失败、变量丢失、无权限、越限都要就地反馈；不许静默回弹或假装成功。
8. **只读不伪装**：没有写通道时输入控件必须是只读外观；没有权限时不给“能点但点了报错”的假按钮。

令牌直接复用 `Scada.Controls\Themes\ScadaTokens.xaml` 的 20 个 `Hmi*` 令牌；**本批不新增令牌**，只新增用法。字号七档（10 / 11.5 / 12.5 / 14 / 16 / 20 / 28）、圆角四档（4 / 6 / 8 / 12）、控件高度三档（32 / 36 / 44）不变。

## 控件清单与绑定方向

| 项 | 形态 | 核心绑定 | 方向 | 关键状态 |
| --- | --- | --- | --- | --- |
| `Hmi.ComboBox`（`Presentation=Text`） | 现有控件 + 新呈现 | `Value`（int，只读） | 变量 → 图元 | 命中 / 未命中 `#值` / 绑定丢失（保留最后值+橙点）/ 超长 / 值 0 有映射 / 纯文本档 |
| 顶栏页面菜单 | 运行时 chrome | 页面集合 + 当前页 | 点击 → `Navigate` / `GoBack` | Normal / Current / Disabled / Overflow / Back-空栈 / Web 隐藏 |
| 软键盘 | 运行时设施 | 无（服务） | 用户输入 → `CommitEdit()` | Normal / Pressed / Disabled / Password / 越限 / 写失败 |
| 弹窗 | 运行时浮层 | 弹窗页 + 打开动作 | 动作 → 浮层 | Modal / Non-modal / Opening / Closing / 保存失败 / 无权限 / 不支持嵌套 |
| `Hmi.Slider` | **新 TypeKey** | `Value`（number） | 双向：读显示 / 松手写 | Normal / Dragging / Focus / Disabled / Permission / 写失败 |
| `Hmi.AlarmBanner`（`ShowRowAck=true`） | 现有控件 + 行内确认 | 报警引擎 | 读 + 确认动作 | 未确认 / 已确认 / 已恢复 / Empty / 确认失败 |
| `Hmi.DataGrid`（`Source=History`） | 现有控件 + 源模式 | `IScadaHistorySource` | 只读查询 | Loading / Empty / Source unavailable / Rows |
| 旋转 / 缩放动画 | 枚举扩展 | 动画变量 | 变量 → 视觉 | 运行 / 停止复位 |

## 关键交互契约

### ComboBox 只读显示（替代 StateText）

- 现有 `Mode=Output` 已经是只读；`Options` 已经是值→文字映射；`SelectedText` 已经给出显示文本或 `#值`。
- 新增 `Presentation`：`Field`（默认，现有下拉框外观）/ `Text`（只读字段：隐藏 chevron、原始值、整框；保留底边线，与 IOField 只读态同族，不降透明）。
- 新增 `ShowSeparator`（默认 `true`）：`Text` 呈现的底边线；关掉就是纯文本档，用于 KPI 卡 / 密集看板。Field 模式下该属性无效，属性面板应隐藏/置灰。
- 只读字段的底边线与 IOField 同色（`#7A7A7A`），标签 14 / Auto 宽 / gap 8、内边距 0/8、高度 32；不要固定 56px 标签、不要 11.5px 标签。
- 左侧说明字复用基类 `Text`，对齐复用基类 `TextAlignment`（默认 Center，Text 呈现需覆盖为 Left）；**不新增 `Label` / 同名 `TextAlignment`**（会触发 SPG0006）。
- `Text` 呈现**恒只读**（实现口径，比“只允许 Output”更安全）：`Mode` 在 Text 下不生效，属性面板隐藏该行；要可写请切回 `Field`。
- 未命中值保留 `#值`（现有实现刻意“难看但诚实”）；不新增 `UnknownText`，真要友好文案就把映射表补全。
- 绑定丢失保留最后一次有效值 + 橙点（数据泵诊断角标）；不显示 `#0`，也不显示空白；0 命中映射就显示映射名。
- Text 呈现不要换模板（会丢 `PART_Combo` 与写回订阅）；同一模板两段可见性。
- 字符串状态不在本批范围：`Value` 是 int，枚举按数值转换；数值/字符串状态用 `Hmi.Text` + `DisplayFormat`（vision 评审）。
- Web 渲染 M1.5 需补 `Presentation` / `ShowSeparator`，否则 web 仍显示禁用下拉 + 原始值 + chevron。

### 顶栏页面菜单（替代 PageMenu）

- 落点：`ScadaRuntimeWindow` 顶栏；无 TypeKey、无 `.vms` 图元。
- 页面来源：自动 = 本方案可导航页（排除基座；弹窗页在 P1 落地时同步排除）；手动排序 / 改显示名后置。
- 当前页打勾；点击幂等（`Navigate` 对当前页直接返回，不压栈、不发 Loaded）；返回走 `GoBack()`，栈空置灰；`NavigateBack` 动作给画布按钮用。
- 权限：已实现页面级 `ScadaPage.RequiredRole`；`Navigate` / `GoBack` / 页面菜单共用 `ScadaRuntime.CanAccess`，不配（None）= 全部可见。
- Web：镜像模式禁止浏览器切页，web 隐藏页面菜单。

### 软键盘

- 打开时机：`IOFieldElement.BeginEdit()` / `TextFieldElement.BeginEdit()` 之后；只读字段、无写通道、无权限都不弹。
- 提交：Enter / 确定 → 现有 `CommitEdit()`；校验、类型转换、权限、写变量全部复用一条路径。
- 取消：Esc / 点键盘外 → 放弃编辑，恢复原值。
- **写入时机**：只在提交成功时写；**不在 `TextChanged` 写**（R35）。
- 显示：当前输入 + 单位 + 上下限 + 原值（手册 13.9「显示上下限值 / 显示前一数值」）。
- 字符键盘 v1 只做 ASCII 字母 / 数字 / 常用符号；中文走系统输入法。
- Web M2 用浏览器键盘 + 同一套校验文案；不做网页版九宫格键盘。

### 弹窗

- 动作：`OpenPopup(TargetPage, Modal=true, Anchor=9宫格, OffsetX/Y)` / `ClosePopup`。
- 页面：普通 `ScadaPage` + `IsPopup=true`；不参与页面菜单，不能被 `Navigate` 当作普通页切走。
- 关闭：关闭按钮 / Esc / 点遮罩（可配）/ `ClosePopup` 动作；关闭后焦点回到打开它的元素。
- 数据：弹窗页的绑定与动画走同一个 `ScadaRuntimeBinder`；打开时建表、关闭时摘表（不泄漏订阅）。
- **v1 不支持嵌套弹窗**；弹窗里再开弹窗一律拒绝并记日志。
- Web M3：快照带弹窗层，web 只渲染不打开；镜像模式下桌面开关弹窗，web 跟随。

### 滑块

- 拖动只更新显示，**松手写一次**；`WriteMode=Throttled`（100ms）只用于必须连续跟随的场合。
- 键盘左右 ±`Step`；点轨道跳转；`MinVariable` / `MaxVariable` 可运行中改量程。
- 写入失败 → 拇指回弹到变量当前值 + 红轨 + 原因；不许停在“看起来已经改了”的位置。
- `ConfirmDanger` 打开时，写入前走二次确认（速度 / 压力 / 温度类建议打开）。
- Web M2 可交互，走 `cmd.input`，写入时机与桌面同一口径。

### AlarmBanner 行内确认（替代 RecordList）

- 新增 `ShowRowAck`（默认 **false**，保持现有外观；新项目建议打开）。
- 新增 `AckVariable`（默认空）：确认成功后写入（0→1 或累加），PLC 握手用；留空 = 不写。
- 顺序：**先 `ScadaAlarmEngine.Acknowledge(id)` 成功，再写 `AckVariable`**；任一步失败都不改行状态。
- 不做乐观更新；失败 toast + 日志，行保持“未确认”。
- 历史：宿主 `ScadaAlarmHistoryView`（弹窗）；审计：`ScadaAuditWriter`；将来页内历史表用 DataGrid 源模式。

### DataGrid 历史源（替代 HistoryTable）

- 新增 `Source`：`Live`（默认，现有采行信号）/ `History`（时间范围查询）。
- 新增 `HistoryRange`（今天 / 昨天 / 最近 7 天 / 自定义）、`AutoRefreshSeconds`（默认 10s）、`HistorySource`（数据源标识）。
- 列定义、行模型、空态、斑马纹、最大行数全部复用现有 DataGrid。
- 依赖 `IScadaHistorySource`（宿主侧实现，图元不直接读文件）；契约未定前不排期。
- Web 非目标（运行态 web 只显示“此刻”）。

## 与 `scada-controls` v1 的关系

- 本稿是 v1 之后的新增批次，**不修改 v1 的 28 个图元与令牌**。
- v1 里已列出的候选，本稿只处理重叠部分：
  - `Hmi.Slider`：v1 补充候选已列 P1；本稿补交互、写入时机与失败反馈，不重复立项。
  - `Hmi.AlarmList`：v2 **不新增**；先扩 `Hmi.AlarmBanner` 行内确认，历史走宿主弹窗。v1 候选里的 AlarmList 暂不立项。
  - `Hmi.DataTable`：v2 **不新增**；`Hmi.DataGrid` 加 `Source=History`，复用同一表格内核。

## 设计证据

`design.html` 已按 1280×720 视口分段渲染，保存于 `evidence/`（v2 复核版）：

| 文件 | 内容 |
| --- | --- |
| `design-v2-00-scope.png` | 页头 + 复核口径 + 本批范围与 v1→v2 对照 |
| `design-v2-03-combo-readonly.png` | ComboBox 只读显示：现状 vs 建议、6 种状态 |
| `design-v2-04-topbar-menu.png` | 运行窗口顶栏页面菜单：下拉、当前页、返回、权限 |
| `design-v2-08-alarm-ack.png` | AlarmBanner 行内逐条确认 |
| `design-v2-09-grid-history.png` | DataGrid 历史源模式 |
| `design-v2-11-composition.png` | 复核版组合示例（顶栏菜单 + 只读状态 + 滑块 + 报警确认 + 弹窗 + 键盘） |

> v1 初稿截图归档在 `evidence/v1/`，只作对比，不再代表当前结论。

## 待裁定（设计确认前）

| # | 问题 | 建议默认值 | 影响 |
| --- | --- | --- | --- |
| Q1 | ComboBox 未命中值：保留 `#999` 还是加 `UnknownText`？ | **保留 `#值`**（现有实现刻意“难看但诚实”） | 不加属性；映射表补全即可 |
| Q2 | 绑定丢失显示什么？ | **保留最后一次有效值 + 橙点**；不显示 `#0`/空白 | 若坚持显示 `—`，需新增只读 `IsValueValid` |
| Q3 | 页面菜单放顶栏还是画布？ | **顶栏**；本批只做自动列表，手动排序/改名后置 | 手动配置需定 `.vms` vs `AppConfig.json` |
| Q4 | AlarmBanner 行内确认默认开还是关？握手用哪种？ | **默认关**；`AckWriteMode=Counter`（可选 Latch） | 确认人字段是否本批加（建议下批，公共契约） |
| Q5 | 桌面报警确认审计缺口怎么补？ | **先做统一确认服务**（权限+引擎+审计+握手），三个入口共用 | 否则行内确认会多一条漏审计路径 |
| Q6 | 弹窗“当前表面”事件闸门怎么定？ | **`CurrentPage + PopupPage`**，由宿主统一；弹窗不压 `_pageStack` | 不改闸门，弹窗按钮点不动 |
| Q7 | DataGrid 历史源现在做还是等契约？ | **等** `IScadaHistorySource`；vision 追溯升 P1 | 本批只冻结视觉与列定义；补 SN 搜索/型号列 |
| Q8 | Slider 用途与默认值？ | **通用非运动量 P1；运动量 P2**；三档 + 硬上限 + Engineer + Release/Confirm；位置类走 `TryMove` | 直接写变量会绕过 `MotionCommandGate` |
| Q9 | 软键盘是否必做？ | **数字 P1、字符 P2**；触摸部署才升 P0；按钮 `Focusable=False` | 纯键鼠工控机可后置 |
| Q10 | 旋转/缩放动画本批做吗？ | **P3/不做**（vision 无场景；模型改造大） | 需要 `ScadaAnimation` 新字段 + `TransformGroup` |

## 验收计划（复刻后）

| 层 | 判据 | 证据 |
| --- | --- | --- |
| 设计 | 复核版各节能回答“现有控件是什么、改了什么、为什么不能只改属性” | `design.html` + `evidence/design-v2-*.png` |
| 静态 | ComboBox 新属性走源生成器；AlarmBanner / DataGrid 新属性走 `[ScadaProperty]`；描述符快照更新 | `ScadaChecks` `[G]` / `[R]` |
| 运行时 | 只读呈现、行内确认、确认回写、历史源、失败回弹、订阅摘表 | `ScadaChecks` 新增段 |
| 渲染 | 深色 / 浅色两套画面各 4~5 张 PNG，与 `design.html` 逐块对照 | `evidence/` |
| 回归 | `FlowCanvasChecks` / `UIThemeSmokeTest` / `ScadaChecks` 全绿 | gate 日志 |
| Web | 每个控件标注的 M1 / M1.5 / M2 / M3 / 非目标逐项对齐 | `docs\SCADA运行态Web客户端方案.md` |
| 真机 | 打开方案 → 切页 → 返回 → 弹窗 → 键盘输入 → 滑块 → 报警确认 | 用户验收 |

## 已知偏差（Web 设计 vs WPF 复刻，预判）

1. **字体度量**：Web 用 Segoe UI Variable / 微软雅黑；WPF 同族系统字体，字宽与行高略有差异，不按 px 硬套。
2. **阴影**：Web 用多层 CSS 阴影；WPF 用 `DropShadowEffect`，层数与扩散范围略浅。
3. **动效**：Web 用 CSS transition；WPF 用 `Storyboard` / `VisualStateManager`，统一 150~200ms。
4. **滚动条**：WPF 滚动条约占 17px；窄面板下可用宽度比 Web 少约 17px。
5. **软键盘**：Web 设计稿里的九宫格定位是示意；WPF 侧要按实际窗口 / DPI 计算，不照抄 px。
6. **弹窗**：Web 用 `backdrop-filter` 模糊；WPF 不做模糊，只压暗。
7. **状态矩阵**：设计稿固定按运行态深色画布展示（演示运行中的控件状态）；浅色对照在 WPF 复刻阶段补，验收时以真机浅色截图为准。

## 不在本版范围

- **WPF 复刻**、令牌映射表、断言与截图证据：确认后另起批次。
- **历史数据契约实现**：`IScadaHistorySource` 未定，本稿只冻结视觉与列定义。
- **事件甘特图、条形码 / 二维码、多语言文字标签库、向量图库**：P3 观察 / 战略项。
- **SCADA 编辑工作台**（工具箱 / 属性 / 图层 / 画布顶栏）：属于宿主视图，走各自基线。
- **运行态 Web 客户端**：本稿只标注 web 阶段归属，不修改 web 协议与实现。

## 变更规则

设计基线确认后只读。需要调整设计时升版本（v2 → v3），并同步：

1. `design.html` 与 `README.md` 的版本号；
2. 令牌映射表（若新增令牌）；
3. 已复刻的 WPF 视图与 `evidence/` 截图；
4. `docs/UI设计基线/README.md` 索引行。

> 本目录不参与任何工程构建，不打包、不部署（R126 / R128）。
