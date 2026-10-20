# SCADA P0 交互 · 二次确认与流程暂停/恢复

| 项 | 内容 |
| --- | --- |
| 版本 | **v1** |
| 日期 | 2026-10-21 |
| 确认日期 / 人 | 待用户确认 |
| 覆盖范围 | ① 事件钩子级「执行前确认」；② 流程「暂停流程 / 恢复流程」两个动作；4 个 UI 面：事件编辑器、确认弹窗、自动页暂停/恢复（模板）、动作编辑器与状态矩阵。自动页是模板，已暂停状态由同钩子 `WriteVariable(Paused=1/0)` 驱动，P0 不新增流程状态源 |
| 设计文件 | `design.html`（自包含，无外部依赖；顶部页签切换 4 个 UI 面） |
| 证据目录 | `evidence/`（4 张设计稿渲染 PNG） |
| 关联基线 | `docs/UI设计基线/message-dialog/`（确认弹窗视觉与键盘契约）、`docs/UI设计基线/scada-controls/`（SCADA 令牌）、`docs/UI设计基线/industry-templates/scada-suite/`（自动页模板） |
| 状态 | 设计待确认；**未开始 WPF 复刻** |

## 定位

本文件是 UI-SOP「Web 设计基线 → 用户确认 → WPF 复刻 → 同屏截图验收」四步流程的**第 ① 步**产物（R110/R111/R127）。

- Web 只是设计媒介，**不进入构建、不部署**（R126）。
- 本版只覆盖 P0 的两个交互能力；不改图元库、不新增 TypeKey。
- 确认后按本基线复刻 WPF；第 ④ 步以同一状态截图对照验收。

## 背景与问题

| # | 问题 | 现状 |
| --- | --- | --- |
| 1 | 普通按钮的启动/停止没有二次确认 | `IScadaConfirmationService` 目前只被 `AxisPanelElement`（示教/定位）使用；事件动作表直接执行 |
| 2 | 自动流程只能启动/停止，不能暂停 | `IScadaFlowRunner` 只有 `StartContinuous` / `RunOnce` / `Stop`；`ScadaActionType` 没有 `PauseFlow` / `ResumeFlow` |
| 3 | 自动 → 手动切换会丢当前批次进度 | 只能 `Stop`，没有“暂停后恢复”的语义 |

## 设计要点

### 1. 事件编辑器：执行前确认

- 在现有「事件 + 动作表」编辑器的事件勾选框下方新增一行 **执行前确认**：
  - 默认**关闭**；关闭时与现有行为完全一致。
  - 勾选后出现可选文本框「确认提示」，留空走默认提示。
  - 动作表含 `停止流程` / `轴停止` / `轴失能` 时显示「危险动作」标记。
  - 事件未勾选时不出现确认行；勾选确认但动作表为空时显示“不会有任何反应，也不会弹确认框”。
- 确认挂在**事件钩子**上，不是单条动作上；一次确认覆盖整串动作。

### 2. 确认弹窗

- 复用已确认的 `MessageDialog` 基线，不新造弹窗：
  - 普通动作：Warning 图标 + 主色主按钮。
  - 危险动作：Danger 图标 + 红色主按钮。
  - 默认焦点**始终在「取消」**；Enter = 取消，Esc = 取消。
  - 取消 = 一条动作都不执行，记 Warn 日志 + 一条审计 `Skipped`。
  - 无弹窗环境（断言/离线）→ 返回 false，按取消处理，**绝不静默放行**。

### 3. 自动页暂停/恢复

- 自动页是**用户组态页面模板**，不是固定 WPF 视图；本页定义推荐状态：
  - 运行中：「启动」禁用、「暂停」可用、「恢复」隐藏/禁用、「停止」可用、「复位」禁用。
  - 已暂停：「恢复」变主按钮、「暂停」禁用、「启动」禁用、「停止」仍可用、「复位」禁用。
  - 已停止：「启动」可用（许可满足）、「复位」可用。
  - 故障：「停止」可用（安全停止）、「复位」清故障后可用。
- 暂停在**节点边界**生效，不是立即冻结当前节点；恢复从当前步继续；停止后不能恢复。
- **只有连续运行可暂停**：单次执行（RunOnce）不支持暂停，动作跳过并记 Warn，不能报“已暂停”假成功。
- **状态驱动**：推荐在「暂停」钩子里同时放 `PauseFlow` + `WriteVariable(Paused=1)`，「恢复」钩子放 `ResumeFlow` + `WriteVariable(Paused=0)`；页面用该变量 + 可见性/外观动画显示“已暂停”。P0 不新增流程状态源。

### 4. 动作编辑器

- `ScadaActionType` 末尾追加：
  - `PauseFlow = 16`（暂停流程）
  - `ResumeFlow = 17`（恢复流程）
- 两者与启动/停止/执行一次共用同一个「目标流程」编辑块（`FlowId` / `FlowName`）。
- 暂停/恢复语义：
  - 暂停：流程存在且运行中 → 请求引擎暂停；未运行 → 跳过并记 Warn；已暂停 → 幂等成功。
  - 恢复：流程存在且运行中 → 请求引擎恢复；未运行 → 跳过并记 Warn；未暂停 → 幂等成功（本就运行）。

## 令牌映射

**不新增令牌。** 全部复用既有语义键：

| 区域 | 令牌 |
| --- | --- |
| 事件/动作编辑器（亮色） | `ScadaTextBrush`、`ScadaSecondaryTextBrush`、`ScadaWarningBrush`、`ScadaPanelBorderBrush`、`ScadaActionCardBorderBrush`、`ScadaPanelBackgroundBrush`、`ScadaSurfaceBrush`（**没有 `ScadaBorderBrush`**） |
| 确认弹窗 | `Dialog*` 令牌 + `MessageDialog` 既有样式（`DialogMessagePrimaryButton` / `DialogMessageDangerButton` / `DialogMessageSecondaryButton`） |
| 自动页（暗色） | `HmiSurfaceBrush`、`HmiBorderBrush`、`HmiTextBrush`、`HmiTextSecondaryBrush`、`HmiWarningBrush`（已暂停）、`HmiDangerBrush`（停止） |
| 按钮五态 | 现有 `ScadaButton` / `DialogButton` / `Hmi` 按钮样式，不新造 |

**字号 / 圆角映射（Web 设计稿是密集排版，WPF 按 SCADA 基线收敛）**

| Web 设计稿 | WPF 复刻 | 说明 |
| --- | --- | --- |
| 8 / 8.5 / 9 / 9.5 px | **10 px** | SCADA 图元字号下限 10px；属性面板/动作行统一抬到 10 |
| 10.5 / 11 px | 11.5 px | 帮助/说明文字 |
| 12 / 12.5 / 13 px | 12.5 / 13 px | 正文/标题 |
| 17 / 21 / 24 px | 16 / 20 / 24 px | 数字/标题展示档 |
| 圆角 2 / 3 / 5 px | 4 px | 数据控件档 |
| 圆角 6 / 7 px | 6 px | 按钮档 |
| 圆角 8 / 9 / 10 px | 8 px（卡片）/ 12 px（控制台） | 卡片/控制台档 |

> 设计稿的亮色面板色值是视觉近似；WPF 以 `UI\Controls\Themes\Colors.xaml` 的实际 `Scada*` 键为准，不按设计稿 hex 取色。

## 复刻约束

1. **不新增图元/TypeKey**；只改事件编辑器、动作类型、运行态行为。
2. **事件编辑器只加一行**：确认开关 + 可选提示文本框；不改变现有事件勾选/动作表的布局与顺序。
3. **确认弹窗走 `MessageDialog`**：替换 `ScadaConfirmationService` 里的 `EasyDialog`；沿用其焦点/键盘契约。
4. **动作枚举末尾追加**：`PauseFlow=16`、`ResumeFlow=17`；不改既有数值，不插中间值。
5. **确认闸门在动作分发器执行前**：取消时整串动作都不执行；不要做成单条动作。
6. **自动页只是模板**：WPF 不新增固定自动页；P0 提供动作类型与运行态行为，页面由用户组态。
7. **XAML `hex=0`**；只用既有令牌。
8. **失败安全**：无确认服务/无 WPF 上下文 → 不执行。

## WPF 映射

| Web 设计面 | WPF 落点 |
| --- | --- |
| 事件编辑器「执行前确认」 | `ScadaEventHook.RequireConfirmation` / `ConfirmationMessage`；`ScadaEventEditor.xaml` + `ScadaEventEditorViewModel`；`ConfirmationMessage` 落 `MessageDialogOptions.Hint` |
| 确认弹窗 | `IScadaConfirmationService.Confirm(title, message, danger)`（默认接口方法，2 参调用回落 Warning）；`ScadaConfirmationService` → `MessageDialog.Show(options)`（Title/Message/Hint/Question，Warning/Danger，默认焦点取消） |
| 危险动作判定 | 领域层 `ScadaActionTypeExtensions.IsDangerous()`（`StopFlow` / `MotionStop` / `MotionDisable` 等）；编辑器与分发器共用，不在视图硬编码 |
| 动作下拉「暂停/恢复流程」 | `ScadaActionType.PauseFlow` / `ResumeFlow`；`DisplayName/PendingReason`；`ActionTypeChoices` **显示顺序按类别分组**（流程类相邻），枚举值仍在末尾 16/17 |
| 动作行目标流程 | `ScadaAction.Detail/Describe` 增加两条；`ScadaEventEditor.xaml` 流程块增加两个 `DataTrigger` |
| 动作执行 | `ScadaActionDispatcher.Dispatch` 前置确认闸门；取消时合成一条 `ScadaAuditEntry`（动作「执行前确认：取消」，`Skipped`）；`ExecuteFlowAction` 增加两个 case |
| 流程暂停/恢复 | `IScadaFlowRunner.Pause/Resume`；`ScadaFlowRunner` 调 `IFlowEngine.PauseSession/ResumeSession`；**仅连续运行**（`FlowSession.IsContinuousRun`），单次执行跳过 + Warn |
| 自动页状态 | 模板；P0 不提供固定视图/流程状态源；“已暂停”由同钩子 `WriteVariable(Paused=1/0)` + 页面可见性/外观动画驱动 |
| 弹窗键盘 | `EasyDialog` 的 Y 键改为 `RequestDefault()`（危险场景 = 取消）；底栏文案按 `PrimaryTone` 分支，不再把所有“默认焦点在次按钮”都标成危险 |

## 评审记录（UI-SOP 第 ② 步）

| 日期 | 评审 | 结论 | 处置 |
| --- | --- | --- | --- |
| 2026-10-21 | `ui` 智能体（只读） | **有条件通过** | 7 条阻断项已在本版收口：B1 自动页状态改为同钩子写变量驱动；B2 确认服务加 danger 重载；B3 弹窗收敛到 Title/Message/Hint/Question；B4 Y 键改走默认焦点、底栏文案按 PrimaryTone；B5 令牌映射改用真实 `ScadaPanelBorderBrush`/`ScadaActionCardBorderBrush`；B6 补字号/圆角映射并抬到 10px 下限；B7 补“权限不足”矩阵行并改 RequiredRole 表述 |

> 复刻 WPF 前需再确认：`EasyDialog` 的 Y 键改动与 `MessageDialog` 底栏文案改动属于共享组件，必须补负向断言；轴控台长文案的 480/560 宽度需真机实测。

## 已知偏差

1. **自动页是模板**：WPF 没有固定的“自动页”视图；P0 只保证“暂停流程/恢复流程”动作可用，页面由用户组态。
2. **“已暂停”由变量驱动**：P0 不新增流程状态源；推荐在同一条钩子里放 `PauseFlow` + `WriteVariable(Paused=1/0)`，页面用变量 + 可见性/外观动画显示“已暂停”。模板演示该配置，真机需用户按现场变量命名落地。
3. **确认粒度是钩子级**：一次确认覆盖整串动作；不支持“逐条动作确认”。
4. **确认弹窗只承诺 MessageDialog 现有槽**：`Title` / `Message` / `Hint` / `Question` / 双按钮；**不承诺副标题、结构化详情、任意富文本**。设计稿已按此收敛。
5. **确认弹窗替换影响轴控台**：`ScadaConfirmationService` 从 `EasyDialog` 换成 `MessageDialog` 后，轴控台示教/定位的确认外观也会变化；长文案在 480 宽下需实测，不够时用 560 宽档。
6. **暂停是节点边界**：不是立即冻结；已下发的动作不撤回。
7. **没有“记住选择”/按角色跳过**：每次命中都问；权限在**图元**的 `ScadaElement.RequiredRole` 上，先于确认闸门（权限不足不弹确认、不执行）。
8. **没有“暂停全部流程”/急停**：急停仍走停止/轴停止，不纳入 P0。
9. **单次执行不可暂停**：`RunOnce` 的 `FlowSession.IsContinuousRun=false`；暂停动作跳过并记 Warn，不报“已暂停”。
10. **字号/圆角按 WPF 收敛**：设计稿为同屏密集排版，WPF 按 10px 下限与 4/6/8/12 圆角档复刻（见上方映射表）。

## 未纳入

- 流程状态源 / `SequenceStep` 图元。
- 逐条动作确认、按角色跳过确认、记住选择。
- 暂停全部流程、急停。
- 操作审计查看器（现有 `ScadaAuditWriter` 只有写端）。

## 验证计划（复刻后）

| 层 | 判据 | 证据 |
| --- | --- | --- |
| 构建 | `dotnet build` 0 错误（既有可空性警告不计）；`Modules` 产物时间戳更新 | 构建日志 |
| 契约 | `ScadaChecks`：`RequireConfirmation` 默认关闭/旧 .vms 兼容；动作枚举追加；事件编辑器 VM 读写；确认/取消/无服务三条路径；`Confirm(..., danger)` 重载；暂停/恢复幂等与未运行/单次执行分支 | `ScadaChecks` 输出 |
| 运行 | `FlowCanvasChecks`：动作分发器确认闸门；流程 runner 暂停/恢复；取消不执行任何动作；取消审计条目；`ActionTypeChoices` 显示顺序 | `FlowCanvasChecks` 输出 |
| UI | `UIThemeSmokeTest`：事件编辑器 XAML 加载、`hex=0`、MessageDialog 可用；**Y 键危险场景不触发主操作**；底栏文案按 PrimaryTone 分支 | 冒烟日志 |
| 视觉 | WPF 同屏截图：事件编辑器确认行、确认弹窗 Warning/Danger、动作下拉暂停/恢复 | `evidence/after-*.png` |
| 回归 | `ScadaChecks` / `FlowCanvasChecks` 0 失败 | gate 日志 |

## 变更规则

设计基线确认后只读。需要调整设计时升版本（v1 → v2），并同步：

1. `design.html` 与 `README.md` 的版本号；
2. `evidence/` 的设计稿截图；
3. WPF 复刻与同屏截图；
4. `docs/UI设计基线/README.md` 索引行；
5. `docs/code-changes/` 记录。

> 本目录不参与任何工程构建，不打包、不部署。
