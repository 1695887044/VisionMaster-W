# 剩余 33 个视图 · 汇总 Web 设计稿

| 项 | 内容 |
| --- | --- |
| 版本 | **v1** |
| 日期 | 2026-10-22 |
| 确认日期 / 人 | 待用户逐页确认 |
| 覆盖范围 | 33 个此前**从未出过 Web 设计稿**的视图（详见 §2） |
| 设计文件 | `design.html`（**单文件**，左侧目录分 4 组，33 条可跳转） |
| 分片源 | `parts/`（33 个 `.html`，每片一个 `<section>`） |
| 写作规范 | `_SPEC.md`（class 白名单、禁止项、分片模板） |
| 共享 CSS | `_shell.html`（令牌 + 组件样式 + 目录骨架） |
| 分组清单 | `_groups.txt`（4 个分组标题，UTF-8） |
| 状态 | **设计稿已出，待用户逐页确认**；本轮未改任何 WPF 代码 |

## 1. 怎么看

直接双击 `design.html`（自包含，无外部依赖）。或起本地预览：

```powershell
powershell -ExecutionPolicy Bypass -File D:\C#\VM\tools\preview_design_server.ps1
# 浏览 http://127.0.0.1:8917/剩余视图设计稿-2026-10-22/design.html
```

左侧目录分 4 组，点条目跳到对应视图。每页结构：标题（视图名 + 真实文件 + 行数/hex）→ 默认态 → 边界态（空/错误/窄档）→ `.notes`（覆盖范围、改动要点、需要新增的接口）。

## 2. 覆盖清单（33 个）

> 盘点是 **33 个视图**。第 34 个 `VisionMaster\Views\DialogViews\MotionBoard\MotionBoardTheme.xaml` 是 `ResourceDictionary`（样式分册，23 处 hex），**不是视图**——它随「运动板卡」那一批一起令牌化，不单独出设计稿。

### 第 2 批 · SCADA 编辑工作台（5）

| # | 视图 | 行数 | 关键结论 |
| --- | --- | --- | --- |
| 08 | `ScadaEditorView.xaml` | 724 | 顶栏一行 44px 两组 WrapPanel；**无常驻格式栏**（选中图元才浮出 7 颗）；无底部状态条，只有左下缩放胶囊；空态覆盖整个画布区 |
| 09 | `ScadaPropertyView.xaml` | 1789 | 一个面板两种对象（图元 / 画面）；行 = 64 标签 + 编辑器 + 26 ƒ 槽；`EmptyTitle` 有四种成因；仅 3 处真实 hex（同一颗按钮） |
| 10 | `ScadaToolboxView.xaml` | 496 | 搜索 42px + 分类分组（8 类）+ 条目 30px（9px TypeKey）；**两处真缺陷**：搜索无结果整块空白、`TotalCount` 搜索时不跟着变 |
| 11 | `ScadaLayerView.xaml` | 351 | 五段结构；行 26px 七列；删除仅空层可用；**26 处硬编码色**（本批最密） |
| 12 | `ScadaRuntimeWindow.xaml` | 499 | 三行壳（顶条 / 画面 / 底条 26）；弹窗与报警抽屉都是覆盖层；**22 处私有色**与 `Hmi*` 不同源，需合并成运行窗口壳令牌 |

### 第 2 批 · SCADA 数据与弹窗（8）

| # | 视图 | 行数 | 关键结论 |
| --- | --- | --- | --- |
| 13 | `ScadaAlarmHistoryView.xaml` | 628 | 720 宽右侧抽屉、刻意无投影；8 列定宽；两组筛选筹码；**两种空态分开**（本次运行无记录 / 筛选无命中） |
| 14 | `ScadaGridColumnsEditor.xaml` | 154 | **独立弹窗 `MinWidth=760`**（不是 340 内嵌）；六列；未绑定变量的行整格标红 |
| 15 | `ScadaBatchInsertTemplateEditor.xaml` | 128 | **独立弹窗 `MinWidth=620`**；七个扁平字段；失败整批不插入 |
| 16 | `ScadaTemplateParameterizeEditor.xaml` | 208 | **独立弹窗 `MinWidth=620`**；三类参数化可单用可同用；状态三态 |
| 17 | `ScadaAlarmConfigDialogView.xaml` | 552 | 920×640 平铺 7 列表；`RowDetails` 承载回差/延时；即时写回，**没有「确定」** |
| 18 | `ScadaLoginView.xaml` | 582 | 820×560；左品牌栏 + 右三态；登录按钮**不禁用**，失败文案来自 `TryValidate`。**✅ 已复刻（2026-10-22）**：品牌色改 `Brand*` 令牌、`hex=0`；后端链路 9 条端到端断言全 PASS |
| 19 | `ScadaUserManagerView.xaml` | 422 | 760 宽左清单 + 右表单三态；删除是同位两态按钮；**42 处 hex 全是 Element-UI 老色** |
| 20 | `ScadaSystemSettingsView.xaml` | 139 | 只有外壳：`CardPropertyGrid` + 错误条 + 取消/确定；**六个页签**（简报只说三个，已按源码更正） |

### 第 3 批 · SCADA 选择器与运动板卡（10）

| # | 视图 | 行数 | 关键结论 |
| --- | --- | --- | --- |
| 21 | `ScadaVariablePickerView.xaml` | 381 | 五列（变量名/类型/来源/当前值/说明）；数组下标区仅数组变量时出现；确认回传变量 Id |
| 22 | `ScadaAxisPickerView.xaml` | 195 | 多选清单；失效轴仍保留一行并保持勾选 |
| 23 | `ScadaFlowPickerView.xaml` | 223 | **原视图没有搜索框**；三列；停用项仍可选（运行侧拒绝） |
| 24 | `ScadaPagePickerView.xaml` | 346 | 四列；「起点」徽标仅起点页显示 |
| 25 | `ScadaVariableEventDialogView.xaml` | 631 | 左 340 清单两段分组；右栏三态互斥；五类事件逐字取自源码；**56 处 hex** |
| 26 | `MotionBoardView.xaml` | 635 | 左 260 卡列表 + 右页签；**`TabLabels` 只有 3 项**（卡设置/手动调试/点位列表）——见 §4 待核实 |
| 27 | `MotionBoardCamView.xaml` | 616 | 表栏 chips + 同步运行卡 + 读数条 + 轮廓预览 + 拐点表；拐点 ≤2 行禁删 |
| 28 | `MotionBoardDebugView.xaml` | 1077 | 三栏写死 230 / 1fr / 270，各自滚动；轴状态优先级 `报警 > 正限位 > 负限位 > 运动中 > 已使能 > 未使能`；IO 各 8 路 |
| 29 | `MotionBoardPointsView.xaml` | 420 | 每轴固定 16 行 P0–P15；曲线候选只有梯形/S曲线；两套精度（F1 / F3） |
| 30 | `MotionBoardSettingsView.xaml` | 542 | 卡头 + 运动参数 3×2 + 轴映射 7 列；连接按钮文案随状态切 |

### 第 3 批 · 设置弹窗与零散（10）

| # | 视图 | 行数 | 关键结论 |
| --- | --- | --- | --- |
| 31 | `CameraSettingsView.xaml` | 785 | 1180×720；左 380 列表 + 右详情；8 个采集参数 + 1 勾选；「溢出丢帧」与「其它丢弃」是两个数 |
| 32 | `CommunicationSettingsView.xaml` | 582 | 三段式；七列；**两种空态必须分开**（没有设备 / 筛选无命中）；批量操作作用于当前筛选结果 |
| 33 | `HardwareManagerView.xaml` | 705 | 1100×820；左 210 导航 + 右 TabControl；概览三域卡 + 当前异常；急停在宿主页头 |
| 34 | `SystemHealthView.xaml` | 495 | 1180×700；摘要条 + 四页签；默认只显示异常项；一个 1s 定时器只刷当前页 |
| 35 | `FlowManagerView.xaml` | 373 | 1180×620（由「调用方式」四个勾选位反推）；五列；状态四态文案 |
| 36 | `ScanGroupEditorView.xaml` | 473 | 920×700 固定尺寸；上卡可编辑 + 下卡 216 只读诊断；VM 直接给 `StatusColor` 十六进制 |
| 37 | `HelpView.xaml` | 662 | 1120×740；52 标题 + 40 工具条 + 目录（可拖）+ 正文 780 居中 + 26 状态栏；页眉来源三档 |
| 38 | `ImageView.xaml` | 16 | 只有 16 行；**真界面在 `ImageGallery.xaml`**（大图 + 4px 分隔 + 112 高缩略图条）；空提示由 code-behind 三段生成 |
| 39 | `SplashScreen.xaml` | 94 | 520×360 深色无边框；五行；启动链 4 项；失败保持窗体打开 |
| 40 | `DebugHitWindow.xaml` | 169 | 430 宽非模态单实例；点 ✕ 只 `Hide()`；三按钮直接绑 Shell 的 `ExecutionCommand` |

## 3. 复刻前必须新增的接口（汇总）

| # | 视图 | 需要新增 | 为什么 |
| --- | --- | --- | --- |
| 10 | 图元工具箱 | `HasGroups` / `EmptyHint`；`TotalCount` 改为随搜索通知 | 搜索无结果时整块空白；"共 N 种"搜索时不跟着变 |
| 11 | 图层面板 | `EmptyTitle` + `EmptyHint` 拆分 | 空态只有一段 hint，没有标题 |
| 12 | 组态运行窗口 | **运行窗口壳令牌组**（22 处私有色） | 与 `ScadaTokens.xaml` 的 `Hmi*` 不同源 |
| 13 / 18 | 报警历史 / 登录 | 深色语境令牌（`RunWindow*` / `Brand*`） | 共享 CSS 只有 Dialog 浅色一套 |
| 23 | 选择流程 | `Keyword` + `ApplyFilter()` + `EmptyHint` 新分支 | 四个选择器要对齐，而这一页原本没有搜索 |
| 26 / 27 | 运动板卡 | `TabLabels` 第 4 项 + `SelectedTabIndex` 上界 2→3 + 宿主 | 若要放出「电子凸轮」页签 |
| 31 | 相机设置 | `CameraRowViewModel.StateKey`（ok/warn/err/idle） | 现在只有 `StateText`，胶囊底色写死 |
| 33 | 硬件管理 | `IsAllIdle` | 无设备时摘要仍说"全部设备状态正常" |
| 34 | 系统健康 | `HealthDomainCard.Count` 与 `Summary` 拆开 | 现在一个字段兼两职，17px 大号渲染整句 |
| 35 | 流程管理 | `IsFilteredEmpty` + `FlowItem.StatusKey` | 搜索空态与状态列取色 |
| 36 | 扫描组 | `ScanGroupStatRow.StatusKey` | VM 直接给十六进制，换肤动不了 |
| 38 | 画布 | `ImageCollectionService.State`（Idle/Waiting/Ready/Unavailable） | 分不清"还没来帧"与"服务没起来" |

## 4. 待核实（智能体自述与源码不一致处）

| # | 事项 | 状态 |
| --- | --- | --- |
| 1 | `MotionBoardView` 的页签数 | 一个智能体报告 `TabLabels` **只有 3 项**（卡设置/手动调试/点位列表），但本批同时给了 27~30 四个页签视图。**27（电子凸轮）可能是未挂上的孤儿视图**，需要确认它是否在 `TabLabels` 里 |
| 2 | `ScadaSystemSettingsView` 的页签数 | 简报写"三个"，源码是**六个**（运行窗口 / 安全与留档 / 画布 / 日志 / 服务与启动 / 并行执行）。稿按源码六个出 |
| 3 | 14/15/16 的宿主 | 简报写"嵌在 340px 检查器里"，实际是 `EasyDialog.ShowSync` 的**独立弹窗**（MinWidth 760/620/620）。稿按真实宽度出 |

## 5. 评审状态

| 轮次 | 做法 | 结果 |
| --- | --- | --- |
| 1 | 4 个 UI 智能体并行出片（每片附 `.notes`） | 33 片齐；2 个智能体用 headless 浏览器自检并修掉 5 处排版缺陷 |
| 2 | 机器校验 `tools\_validate_design.ps1` | 33 片全过：每片一个 `<section>` + 一个 `.notes`；无 `<style>`/`<script>`/`<img>`/外链；`style` 属性里零 hex、零 `rgb()`；`div`/`section` 开闭平衡 |
| 3 | UI 智能体视觉复查（渲染截图逐组看） | 修掉 8 处排版（10 标题断行、14 操作列被切、21 数值右对齐、32/35 列宽截断、36/37 表格右侧空档、39 状态符号被切） |
| 4 | 审查智能体技术复查（字段/命令/状态真实性） | 33 页逐页核过；抓出 **5 处数据硬伤**，已全部修（见下） |

### 评审后已修（客观错误）

| # | 页 | 问题 | 修法 |
| --- | --- | --- | --- |
| 1 | 09 | 给 `Hmi.Pump` 画了 5 行「事件」组，但泵的描述符没声明 `Events` | 删掉该组；notes 写明"描述符没声明事件的图元整组不出现"，并指出要看事件行应换 `Hmi.Button` / `Hmi.TextField` |
| 2 | 10 | 「共 29 种」实为 28（`Hmi.AxisPointTable` 的 `ShowInToolbox=false`）；「运动 3」实为 2 | 全页改 28 / 运动 2，notes 补过滤依据与逐分类计数 |
| 3 | 10 | 空态的「清空搜索」按钮没有对应命令 | notes 明确列为需新增 `ClearSearchCommand` |
| 4 | 28 | 已连接正运动卡的 IO 画成 8 进 8 出（实际 24 进 16 出，8/8 只是未连接默认值） | 改 24/16，notes 改成"路数取自 `Capabilities.DigitalInputCount/DigitalOutputCount`" |
| 5 | 37 | 「宿主 77 页」实为 4 页（`VisionMaster\Help\*.md` 只有 4 个）；「变量管理 [自动生成]」节点不存在 | 改 4 页；删掉该节点 |
| 6 | 31 | 「Basler Pylon」驱动全仓不存在 | 改「海康相机」（仓库里真实存在的驱动） |
| 7 | 26 / 30 | 「正运动 ECI2828」不是真实机型（已知为 ECI3428 / ECI3828 / ZMC408SCAN） | 驱动名统一「正运动运动控制卡」，机型改 ECI3828 |
| 8 | 38 | 右键菜单漏了「锁定跟随」（实际 6 项） | 补上并改 notes |
| 9 | 17 | 报警重名文案与代码不符 | 改成代码里的「已存在同名报警 [x]，请更换名称」 |
| 10 | 14 | 「整行标红」不准确（只有变量框标红） | 改成「变量框标红」 |
| 11 | 08 | notes 写「7 颗」实为 6 颗 | 改 6 颗 |
| 12 | 18 | 同一登录窗三态宽度 820/520/520 不一致 | 统一 820 |
| 13 | 19 | 成功消息「角色 → 工程师」与表单「管理员」矛盾 | 消息改「管理员」 |
| 14 | 13 | 状态列 `已恢复未确认` 被截断 | 改「恢复待确认」（同义、能放下） |
| 15 | 18 | 品牌区 8 处颜色写死在视图里（R140） | **已复刻（2026-10-22）**：品牌色上移为 `Colors.xaml` 的 `Brand*` 六令牌，`ScadaLoginView.xaml` 已 `hex=0`；并补登录链路端到端断言 9 条（含改密落盘回读）。见 `docs/code-changes/2026-10-22-用户登录（品牌令牌收口与端到端断言）.md` |

### 评审后仍未修（需产品决策 / 属版式取舍）

| # | 页 | 事项 | 为什么没改 |
| --- | --- | --- | --- |
| 1 | 26~30 | 运动板卡主窗（26）与四个页签内容（27~30）**壳层不一致**：26 有 console 顶栏 + 左卡列表 + 60px 页头，27~30 只有 44px 标题条 | 需要决策：页签内容是否要带完整壳层；另 27「电子凸轮」当前**未挂进页签栏**（`TabLabels` 只有 3 项），要不要放出来是产品决定 |
| 2 | 26 | 「卡设置」页签在 26 里画成三张摘要卡，而 30 画成完整设置表单 | 同上，需确认 26 是"入口枢纽"还是"页签内容" |
| 3 | 39 / 40 / 26 / 12 / 13 / 37 | 若干设计说明写在 mock 正文里，与 UI 文案同字号同色 | 属内容归属取舍（搬进 notes 还是保留），改动面大；审阅时请把它们当注释看 |
| 4 | 18 | 登录窗左品牌栏用浅色代替真实的深青渐变 | 需先补 `Brand*` 深色令牌（已在 §3 登记） |
| 5 | 13 | 表格右侧约 227px 空档；空态缺图标 | 版式取舍 |
| 6 | 09/10/11 | 三个停靠面板宽度 330/300/330 不统一 | 建议统一 330，属微调 |

## 6. 变更规则

确认后只读。需要调整时升 `v2`：改 `parts/<NN>-*.html` → 重跑 `tools\_assemble_design.ps1` → 重跑 `tools\_validate_design.ps1`（必须 `problems = 0`）→ 同步 `docs/UI设计基线/README.md` 索引行。

**不要直接改 `design.html`**——它每次都由 `_shell.html` + `parts/*.html` 重新生成，手改会被覆盖。
