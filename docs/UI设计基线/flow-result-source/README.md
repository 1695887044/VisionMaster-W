# 运行统计 · 结果源配置（Web 设计基线）

| 项 | 内容 |
| --- | --- |
| 版本 | **v1.2（已复刻）** |
| 日期 | 2026-10-28 |
| 确认人 | 主会话代裁（用户 2026-10-28 授权"你自己评估"） |
| 状态 | **WPF 已复刻**：流程右键「运行统计…」→ `FlowResultSourceDialogView`；后端五模式解析 + DLG-1~17 + 结构闸全绿；**真机截图已出**（`artifacts\render\20261028\result-source-*.png`）；待用户按 R132 验收 |
| 覆盖范围 | 流程级「结果源」配置弹窗：自动 / 结果输出步骤 / 结果变量 / 步骤端口 / 不参与；变量与步骤端口选择、当前生效摘要、校验与只读态 |
| 设计文件 | `design.html`（自包含、可交互） |
| 上游 | `docs/UI设计基线/flow-runtime-kpi/`（底栏 KPI / 浮窗 / 下钻） |
| 后端 | `FlowModel.ResultSource` + `FlowVerdictResolver` 优先级已落，断言 FRS-1~6；记录见 `docs/code-changes/2026-10-28-结果源配置（Web基线-后端优先级）.md` |

## 1. 为什么需要这个弹窗

结果输出步骤是推荐路径，但旧方案里已经有自己的判定变量（样例的 `<工位>_Result`），
也有用判定表 `IsOk` 端口直接判定的流程。没有配置入口时，宿主只能按固定优先级猜：

```
结果输出步骤 > 约定变量 <流程名>_Result > 未判定
```

弹窗把"猜"变成"显式声明"：每条流程可以选自己的判定来源，并在保存前看到"当前生效"和校验结果。

## 2. 五个模式

| 模式 | 行为 | 适用 |
| --- | --- | --- |
| **自动（推荐）** | 结果输出步骤 > 配置的结果变量 > 配置的步骤端口 > 约定变量 > 未判定 | 新方案；不想管细节 |
| **结果输出步骤** | 只用「结果输出」步骤；没有则未判定 | 已按推荐路径画流程图 |
| **结果变量** | 只读指定变量（bool / string / int） | 旧方案已有 `<工位>_Result` |
| **步骤端口** | 读指定步骤的 bool 输出端口（如判定表 `IsOk`） | 判定表/逻辑运算直接给端口 |
| **不参与** | 该流程不计入底栏 OK/NG；轮次仍记录 | Home/End/子程序/辅助流程 |

## 3. 页面与交互

`design.html` 顶部有五个模式切换（评审用），中间是 600×520 的配置弹窗：

- **模式卡片**：五选一，选中项左侧强调条 + 浅底；每个模式一行说明。
- **配置区**（随模式变化）：
  - 自动：显示"当前生效：结果输出步骤「结果输出」"；没有结果步骤时给琥珀色提示，并列出回退顺序。
  - 结果输出步骤：列出检测到的结果输出步骤；没有时提示"保存后该流程 OK/NG 将显示未判定"。
  - 结果变量：下拉只列**可写 bool 变量**（全局/本地；不含系统只读变量），右侧显示当前值；变量缺失/类型不符给红字。
  - 步骤端口：步骤下拉（深表，缩进显示分支）+ 端口下拉（只列 bool 输出端口）；步骤不在主路径/被跳过时给提示。
  - 不参与：红色提示"不计入底栏 OK/NG；仍记录轮次，可在下钻里看"。
- **当前生效**：底部固定摘要，例如"结果变量 · 工位B_Result（当前值 false）"。
- **校验**：保存按钮在变量/步骤端口缺失时禁用，并在配置区给出原因；自动模式允许保存（回退到未判定）。
- **只读态**：无流程编辑权限时所有控件禁用，底部显示"只读：需要流程编辑权限"。
- **保存**：写回 `FlowModel.ResultSource` 等字段；**不改流程图**、不自动插入结果输出步骤。

## 4. 入口（待拍板）

| 选项 | 说明 | 建议 |
| --- | --- | --- |
| A | 流程列表右键 →「运行统计…」 | **推荐**：与运行/停止同入口，不挤流程管理表格 |
| B | 流程管理表格加一列「结果源」 | 表格已很宽，不推荐 |
| C | 流程属性弹窗新增页签 | 目前没有统一的流程属性弹窗 |

## 5. 令牌与复用

沿用 `app-shell-chrome` v2 与 `Dialog*` 分册，不新增颜色：

| 用途 | 令牌 |
| --- | --- |
| 弹窗底 / 边框 | `DialogSurfaceBrush` / `DialogBorderBrush` |
| 模式卡片选中 | `DialogAccentSoftBrush` + `DialogAccentBrush` |
| 提示 / 警告 / 错误 | `DialogWarningTextBrush` / `DialogDangerBrush` |
| 次要文字 | `ScadaSecondaryTextBrush` / `ScadaFaintTextBrush` |
| 输入框 / 下拉 | `DialogInput`（既有） |

复用 `DialogColumnCard` / `DialogOptionItem` / `DialogTag` / `DialogEmptyState` 等既有控件；
不复制模板（R141）。

## 6. 已拍板（2026-10-28）

| # | 问题 | 结论 |
| --- | --- | --- |
| Q1 | 入口用右键「运行统计…」还是流程管理列？ | **右键「运行统计…」**：与运行/停止同入口，不挤流程管理表格 |
| Q2 | 结果变量候选类型 | **bool / string / int / double 都列**，bool 排前并默认选中；string 只认 OK/PASS/TRUE/合格/良品 与 NG/FAIL/FALSE/不合格/不良，数值非 0 为 OK。理由：后端 `Normalize` 本来就接受这四类，只列 bool 会让旧方案（写字符串/整型判定）无路可走；候选里用类型徽标 + 一行归一化说明即可，不增加后端成本 |
| Q3 | 「不参与」是否允许主流程选？ | **允许**，保存时警告"主流程不参与，底栏 OK/NG 将无来源" |
| Q4 | 自动模式没有结果步骤时，保存是否阻断？ | **不阻断**，给琥珀提示；Auto 仍会回退到变量/端口/约定变量，阻断会误伤旧方案 |
| Q5 | 步骤端口是否允许选分支里的步骤？ | **允许**，提示"分支未走到时本轮未判定"（后端已按 `State==Success` 判定） |
| Q6 | 配置变更是否需要重启流程？ | **不需要**，下一轮生效（解析器轮末读取，不触发重编译） |

> 与 v1 草案的差异只有 Q2：由"只列 bool"放宽为"四类都列、bool 优先"。

## 7. 复刻映射（WPF 已落地，2026-10-28）

| 设计元素 | WPF 落点 |
| --- | --- |
| 弹窗 | `VisionMaster\Views\DialogViews\FlowResultSourceDialogView.xaml` + `FlowResultSourceDialogViewModel` |
| 入口 | `FlowListView` 右键「运行统计…」（`FlowAction.ResultSource`，配置组）→ `FlowListViewModel.ShowResultSourceDialog` |
| 只读态 | `IFlowConfigAccess`（默认实现走「编辑流程结构」权限 Key），VM 置 `IsReadOnly` / `IsEditable` |
| 变量候选 | `IWorkspaceManager.GlobalVariables` 里 bool/string/int/long/double，排除系统变量，bool 排前 |
| 步骤/端口候选 | 深表步骤（含分支，标「分支内」）+ `IPluginProvider.ModulePlugins[type].OutputDefinitions` 与动态端口快照里的 bool（排除 `Success`/`ErrorMessage`） |
| 字段 | `FlowModel.ResultSource` / `ResultVariableId/Name` / `ResultStepId/PortName` |
| 解析 | `FlowVerdictResolver` 按配置优先；Auto 保持回退链（结果步骤 > 变量 > 端口 > 约定变量） |
| 保存 | `Apply()` 写字段后关窗；主流程选「不参与」在 WPF 宿主里再确认一次（Q3） |

## 8. 已知偏差 / 未做

1. 本稿是设计原型，未接真实方案/变量/步骤数据；示例值是手写的。
2. 未做窄屏/移动端；桌面弹窗固定 600×520。
3. 未表达权限判定的具体实现（`AccessPolicy.CanOperate`），只画只读态。
4. 未做"变量被删除/改名"的实时失效提示；保存时校验，运行期未判定。
5. 未做批量配置（多条流程一次改结果源）。

## 9. 变更记录

| 版本 | 日期 | 变更 |
| --- | --- | --- |
| v1 | 2026-10-28 | 首版：五模式、变量/步骤端口选择、当前生效、校验与只读态 |
| v1 后端 | 2026-10-28 | `FlowModel.ResultSource` 五模式字段 + `FlowVerdictResolver` 显式优先级落地；FRS-1~6 绿 |
| v1.1 | 2026-10-28 | Q1~Q6 拍板：Q2 放宽为 bool/string/int/long/double 四类，其余保持 |
| v1.2 | 2026-10-28 | WPF 复刻：弹窗 + 右键入口 + 只读探针 + 候选；DLG-1~17 与结构闸绿；`FlowCanvasChecks 4337/0`、`UIThemeSmokeTest exit 0` |\r\n| v1.2 验收 | 2026-10-28 | 真机打开/切模式/校验/取消全通过，截图入 `artifacts\render\20261028\`；修复「预选模式未发通知 → 左栏不高亮」 |
