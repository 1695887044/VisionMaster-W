# 流程运行统计 · 底栏 KPI / 工位浮窗 / 轮次下钻（Web 设计基线）

| 项 | 内容 |
| --- | --- |
| 版本 | **v1.1（实机测试修正）** |
| 日期 | 2026-10-28 |
| 确认人 | 主会话代裁（用户 2026-10-28 授权"你自己评估"） |
| 状态 | **部分复刻（2026-10-28）**：底栏 KPI + 工位浮窗 + 轮次下钻 + L2 引擎轮次记录 + 告警 KPI + 步骤时间线 + 结果源配置后端已落地；v1.1 按实机结果修正子程序口径与"停止后节拍消失"；逐流程结果源弹窗、线级拓扑未做 |
| 覆盖范围 | 多流程 / 多工位运行时的底栏 KPI 口径、工位浮窗、轮次下钻、`FlowRoundResult` 架构与 L1/L2/L3 分期 |
| 设计文件 | `design.html`（自包含、无外部依赖、可交互） |
| 上游方案 | `docs/方案设计/2026-10-28-多流程多工位底栏KPI口径-方案草案.md` |
| 令牌来源 | `docs/UI设计基线/app-shell-chrome/` v2（沿用，无新增颜色） |

## 1. 这份稿子回答什么

底栏 `节拍 / OK / NG / 告警` 现在是占位（`VisionMaster/ShellViewModel.cs:250-253` 返回 `—`）。
多流程 / 多工位同时运行时，一条全局数没有定义：

- **节拍**是每流程的，求和/平均都没有物理意义；
- **OK/NG** 在串联线上跨站求和会重复计数；
- **告警**是方案级 SCADA 报警，没有流程/工位归属。

本稿演示的结论：

1. 底栏默认跟**当前选中流程**；多于一条在跑时显示 `N 条在跑`，点开是**工位浮窗**（每流程一行）。
2. **「全部」范围**：节拍显示**瓶颈**（在跑流程最大值）；OK/NG 显示**线级**（主流程汇总），**不跨站求和**；没有线级来源时显示 `—` + "分流程查看"。
3. **未判定**单独一桶：判定步骤没执行、没配判定源、类型不符，都不混进 NG。
4. `Role=Home/End`、调试轮**不计入**生产 KPI；**子程序按工位计入**（本仓样例线的主流程就是调用 7 个工位子流程，一刀切排除会让"多工位同时运行"只剩主流程一条、工位行全部置灰不可下钻；线级 OK/NG 仍只取主流程，不重复计数）。
5. 每轮产出一条 `FlowRoundResult`（L2），点 OK/NG 可下钻到步骤、原因、图像、日志。
6. 统计以**只读系统变量** `System.Flow.<流程名>.*` 投影给 SCADA / 脚本；底栏、SCADA、上报、报表读同一份。

## 2. 四个场景（`design.html`）

| 场景 | 内容 | 交互 |
| --- | --- | --- |
| A · 底栏 + 工位浮窗 | 四条流程同时运行；底栏显示当前流程 KPI；浮窗列 6 条流程（含 2 条不计入） | 切「当前流程 / 全部在跑」；点「4 条在跑」开关浮窗；点行选中；点「详情」下钻 |
| B · 工位 KPI 表 | 浮窗的全宽版 + 右侧口径说明 | 静态；用来看清列定义与"不计入"的呈现 |
| C · 轮次下钻 | `FlowRoundResult` 字段 + 步骤时间线 + NG 原因 + "为什么可信" | 切 Station_B / Station_A / Station_C（变量模式）/ Main（线级） |
| D · 架构与优点 | 引擎事件 → `FlowRoundResult` → 消费者；6 条现有优点；L1/L2/L3 | 静态 |

## 3. 关键口径（复刻时必须保持）

| # | 口径 | 说明 |
| --- | --- | --- |
| K1 | 底栏默认跟选中流程 | 与流程列表、运行/停止命令同一口径；不维护"全局当前流程" |
| K2 | 「全部」节拍 = 瓶颈 | 在跑流程上一轮的最大值，不取平均、不求和；**没有在跑流程时回落到"最近完成过轮次"的流程**（否则跑完一轮节拍立刻变 `—`） |
| K3 | 「全部」OK/NG = 线级 | 主流程汇总（样例 `CurrentResult`）或末站判定；没有线级来源时显示 `—`，不拿各站相加；**未判定同取线级**（各工位自己的未判定在浮窗逐行看） |
| K4 | 未判定独立 | 判定步骤跳过 / 未配置 / 类型不符 → Unknown；不沿用上一轮、不冒充 NG |
| K5 | NG ≠ 步骤失败 | 判定表 NG 时步骤 Failed；卡尺/斑点 NG 时 Success=true；判定必须显式声明 |
| K6 | 不计入 | Home / End / 调试轮；`DebugEnabled=true` 的轮次跳过。**子程序计入**（按工位展示、可选中下钻） |
| K7 | 归属 | 按 `FlowId` / `SessionId` 归属；A 的轮次不改 B 的计数 |
| K8 | 不落盘 | 系统变量是派生只读量，重启清零；跨班次累计另做持久化（L3） |

## 4. 与现有基线的令牌映射

本稿**没有新增颜色**，全部沿用 `app-shell-chrome` v2：

| 用途 | CSS 变量 | WPF 资源 |
| --- | --- | --- |
| 底栏底 | `--surface-2` | `ScadaTitleBarBrush` |
| 分隔线 | `--border-strong` | `DialogBorderBrush` |
| 次要文字 | `--text-2` | `ScadaSecondaryTextBrush` |
| 弱化文字 | `--text-3` | `ScadaFaintTextBrush` |
| OK | `--success` / `--success-line` | `DialogSuccessBrush` |
| NG | `--danger` / `--danger-line` | `DialogDangerBrush` |
| 未判定 / 告警 | `--warning` / `--warning-line` | `DialogWarningBrush` |
| 选中 / 范围胶囊 | `--primary-soft` / `--primary-light` | `DialogAccentSoftBrush` |
| 判定源徽标 | `--primary-soft` / `--surface-3` | 复用既有浅底，不新增 |

## 5. 复刻映射（部分已落地）

已落地（2026-10-28）：
- 底栏 KPI / 范围胶囊 / `N 条在跑`：`Shell.xaml` + `ShellViewModel`；
- 工位浮窗 + 轮次详情：`Shell.xaml` 两个 Popup + `Shell.xaml.cs` 开关；行 VM `FlowStatsRow`；
- 轮次台账：`FlowRoundRecord` + `FlowRoundLedger`（宿主侧，非引擎契约）。

未落地：`FlowModel.ResultSource` 配置、线级拓扑/产品身份。
已落地（第三批）：`FlowRoundResult` + `FlowRoundCompleted` + `FlowSession.LastRound` + `FlowVerdictResolver`；`FlowInvoker` 显式 NG 不再算调用失败。
已落地（第四批）：`RunFlowPlugin` 的 `IsOk`/`Outcome` 端口、底栏告警 KPI（未确认产线告警）、轮次步骤时间线。

| 设计元素 | WPF 落点 | 备注 |
| --- | --- | --- |
| 底栏 KPI + 范围胶囊 + `N 条在跑` | `VisionMaster/Shell.xaml` 底栏左区；`ShellViewModel.cs` 替换 `CycleTimeText/OkCountText/NgCountText` 占位 | 同步 `UIThemeSmokeTest/ShellCopy.xaml`（R44/R70） |
| 工位浮窗 | `Shell.xaml` 新增 `Popup`（复用系统资源浮窗的开关模式） | 行模板可下沉 `UI/Controls/Themes/Dialog/`（R141 第二次出现再下沉） |
| 轮次下钻 | 新视图（建议 `Views/DialogViews/FlowRoundDetailView.xaml`）或右侧抽屉 | 数据来自 `FlowRoundResult`，L2 才有 |
| `System.Flow.*` | `Core/Binding/FlowRuntimeVariableCatalog.cs` + `VisionMaster/Services/FlowRuntimeVariableSource.cs` | 目录在 Core，来源在宿主；注册进 `SystemVariableModule` |
| 结果输出步骤 | `Plugins/Plugin.Utility/ResultOutputPlugin.cs` + `Help/ResultOutputPlugin/` | 普通插件，不新建工程；R41/R42 |

**前置条件**：底栏 KPI 语义属于设计基线变更（R110/R127/R128），需用户确认本稿后再改 WPF。

## 6. 已知偏差 / 未做

1. **示例数据是手写的**：`Main / Station_A/B/C`、OK/NG 计数、轮次 12842、原因文本均为演示值，未接真实引擎。
2. **未做真实渲染截图**：本稿是 HTML 原型；WPF 复刻后需按 R132 补"改前 / 改后"同屏截图。
3. **未实现 `FlowRoundResult`**：场景 C 是 L2 的目标形态；L1 只能做到"系统变量 + 结果输出步骤"，没有轮次记录可下钻。
4. **未表达报警明细**：底栏「告警」只显示未确认数（2）；浮窗里没有按工位拆报警——SCADA 报警定义当前没有流程/工位归属。
5. **未做移动端/窄屏**：应用是桌面端，1200px 以下只做简单单列降级。
6. **未接权限**：清零、查看历史等动作的权限闸门（`AccessPolicy.CanOperate`）未在稿中表达。

## 7. 待用户拍板

| # | 问题 | 本稿默认 |
| --- | --- | --- |
| Q1 | 底栏默认跟选中流程，还是显示全线瓶颈？ | 跟选中流程；「全部」才显示瓶颈 |
| Q2 | 「全部」OK/NG 显示线级（主流程汇总），还是显示"— / 分流程查看"？ | 有线级来源就显示线级，没有就显示 `—` |
| Q3 | 未判定是否要在底栏常驻？ | 常驻（0 时也显示，避免"没接入"与"0"分不清） |
| Q4 | 工位浮窗是 Popup 还是右侧停靠面板？ | Popup（与系统资源浮窗一致，点外面关闭） |
| Q5 | 轮次下钻是弹窗还是右侧抽屉？ | 弹窗（可复用 `EasyDialog` 宿主） |
| Q6 | 清零是全局还是按流程？是否需要权限？ | 全局清零 + 权限闸门（未在稿中展开） |

## 8. 变更记录

| 版本 | 日期 | 变更 |
| --- | --- | --- |
| v1 | 2026-10-28 | 首版：底栏 KPI / 工位浮窗 / 轮次下钻 / L2 架构与 L1-L3 分期 |
