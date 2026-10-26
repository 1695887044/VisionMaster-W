# 顶栏样式 + 流程胶囊 · 联合改造设计基线 v2

| 项 | 内容 |
|---|---|
| 版本 | **v2（已复刻，2026-10-25）** |
| 出稿日期 | 2026-10-25 |
| 确认人 | 用户（2026-10-25：确认范围「只要顶栏的样式」「流程显示保持胶囊形态」，并指令「按这个去修改顶部栏 还有流程胶囊」；Q2 徽标合并、Q3 24px 按设计采用） |
| 覆盖范围 | ① `VisionMaster/Shell.xaml` Row0 的**顶栏样式**；② `VisionMaster/Views/FlowListView.xaml` 的**流程胶囊**（含流程区高度 112→224）。**不含** `ProcessView.xaml` 步骤树的样式与行高、计数条位置、画布、停靠、底栏 |
| 设计文件 | [`design.html`](design.html)（4 画面：工作区 / 顶栏 / 流程胶囊 / 范围与决定；`?shot=1` 隐藏评审条） |
| 证据 | `evidence/design-v2-0*.png`（4 张）；`evidence/before-*.png`（2 张现状）；v1 五张归档在 `evidence/v1/` |
| 取代 | v1（2026-10-25，11 条 P0/P1/P2）。用户裁定「其它我都不要」→ **v1 除顶栏样式与流程胶囊外的提案全部撤回**，v1 证据保留对照 |

## 一、范围（v2）

### 1.1 本次做

1. **顶栏样式**：白底 + 底部 1px 边界；模式段改一个容器 + 单一强调色；文件三键与全局命令用 1px 分隔归组；图标统一 16px；方案名居中（folder + 名称）；窗口按钮 46×36。
   **不改** 36px 高度、命令集合、窗口控制顺序。
2. **流程胶囊**：保留 2 列胶囊形态；22→24px；**流程区 112~224 自适应（13 条=224）**；角色与调用徽标合并为一枚；选中用 2px 左条 + 浅底 + 1px 主色边；运行/失败点；禁用删除线；底栏加「全部」入口。

### 1.2 本次不做（v1 撤回）

步骤树行高 46→40、编号圆片、类型同名隐藏、筛选/折叠、运行跟随；v1 的 1 列 236px 方案（本稿改为 2 列 224px）；计数条移位到流程区标题；身份胶囊加未保存点/目录尾段；顶栏 36→40；深色顶栏；新建 `Chrome.xaml`（本稿只复用现有令牌与样式）。

> v1 的三位评审结论仍有效（计数归属、行高实测 46、22px 胶囊 5 信号、顶栏四色互撞、无 focus 等），只是**不在本轮落地**；需要时按 v1 证据再开批次。

## 二、顶栏规格

| 项 | 值 |
|---|---|
| 高度 | **36px（不变）** |
| 底色 / 边界 | `DialogSurfaceBrush` + 底部 1px `DialogBorderBrush` |
| 左簇 | ☰ 28×28 → 1px 分隔 → 新建/打开/保存 3×32×28 → 1px 分隔 → 模式段 → 1px 分隔 → 搜索/全局变量/硬件/配方 4×32×28 |
| 模式段 | 容器 24px、4×50px 段；选中：`DialogAccentSoftBrush` + `DialogAccentTextBrush` + 1px `DialogAccentBorderBrush`；未选：`DialogTextSecondaryBrush` |
| 图标 | 16px 线性；颜色 `DialogTextSecondaryBrush`（**不用**截图里 1.63:1 的淡灰） |
| 居中 | 绝对定位在整栏中心；folder 14 + 方案名 12.5px 半粗；**不加**路径尾段 / 未保存点 |
| 窗口按钮 | 46×36；最小/最大/关闭；关闭 hover 红底白字；顺序不变 |
| 焦点 | 所有按钮挂 `DialogFocusVisual`，焦点环不改变布局尺寸 |

**横向预算（1440）**：左簇 ~513（12 + 28 + 13 + 96 + 13 + 210 + 13 + 128）；窗口 138；中间 789 → 方案名上限 420，两侧各 184。1120 最小窗时中间 469，420 + 两侧 24：刚好够。

## 三、流程胶囊规格

| 项 | 值 |
|---|---|
| 流程区高度 | **112~224px 自适应**：`h = clamp(6 + 行数×24 + (行数−1)×4 + 26, 112, 224)`；6 条（3 行）=112，13 条（7 行）=224 |
| 高度规则 | 1–2 条按下限 112；3–13 条按内容线性增长；14 条以上锁 224 并出滚动条。**按内容伸缩**：面板变高时多余空间给步骤树，流程区不超过内容高度；若要随面板拉伸到 224，把行改成 `*` + Min/Max |
| 步骤区可用高度 | 13 条时由 752 降到 640（46px 行高约 13.9 行）；**步骤树风格与行高不变** |
| 列 / 间距 | 2 列；列间距 4px；容器左右 6px |
| 胶囊 | 24px 高、6px 圆角、1px `DialogBorderBrush`、padding 0 6、内部间距 5 |
| 徽标 | **角色与调用合一**：零 / 主 / 尾 / 外部 / 子；14px 高、9px 字、1px 边；沿用 `LogicCase*` / `DialogSuccess*` / `DialogAccent*` / `DialogWarning*` |
| 名称 | 11.5px，单行省略；选中半粗；2 列下可用约 98px（≈8 汉字） |
| 步数 | 10px 等宽、右对齐；与名称间距 5 |
| 状态点 | 5px；运行绿、失败红；待机不占位 |
| 选中 | 2px `DialogAccentBrush` 左条 + `DialogAccentSoftBrush` 底 + 1px `DialogAccentBorderBrush` |
| 禁用 | 整枚 55% 透明 + 名称删除线；不显示状态点 |
| 底栏 | 左「13 条流程 · 全部 ›」；右「选中：Main」 |

**宽度账（340）**：内容 328 → 2 列各 162 → 胶囊内容 150 = 徽标 24 + 间距 5 + 名称 98 + 间距 5 + 步数 18。**合并徽标是名称能到 98px 的前提**；保留两枚徽标会回到 55~70px。

## 四、v1 → v2 变更

| 维度 | v1 | v2 |
|---|---|---|
| 范围 | 顶栏 + 流程导航（含步骤树）+ 11 条 P0/P1/P2 | **只做顶栏样式 + 流程胶囊** |
| 流程切换 | 1 列 28px 行 | **2 列 24px 胶囊（保留形态）** |
| 流程区高度 | 112 → 236（v1 的 1 列方案） | **112~224 自适应（2 列，13 条=224）** |
| 步骤树 | 行高 46→40、去编号、筛选/折叠、运行跟随 | **完全不动** |
| 顶栏高度 | 36 → 40 | **36 不变** |
| 身份胶囊 | 方案名 + 未保存点 + 目录尾段 | **只有 folder + 方案名** |
| 证据 | 5 张 | 4 张（v1 归档 `evidence/v1/`） |

## 五、令牌映射

| 用途 | 令牌 |
|---|---|
| 顶栏底 / 边界 | `DialogSurfaceBrush` / `DialogBorderBrush` |
| 模式选中 | `DialogAccentSoftBrush` + `DialogAccentTextBrush` + `DialogAccentBorderBrush` |
| 胶囊选中 | `DialogAccentSoftBrush` + `DialogAccentBrush` 左条 + `DialogAccentBorderBrush` |
| 运行 / 失败 / 禁用 | `DialogSuccessBrush` / `DialogDangerBrush` / `ScadaDisabledTextBrush` |
| 角色徽标 | 沿用 `LogicCase*` / `DialogSuccess*` / `DialogAccent*` / `DialogWarning*`（与 flow-list v2 现有配色一致） |

**不新增颜色令牌**。几何值（36 / 24 / 32×28 / 46×36）先落在视图本地，暂不新建 `Chrome.xaml`。

## 六、冲突检查

| 规范 | 是否触发 | 处理 |
|---|---|---|
| R43 / R47 布局版本 | 不改面板归属/停靠语义，只改 Row0 样式 | **不升** `CurrentLayoutSchemaVersion` |
| R44 / R70 | 改 `Shell.xaml` 必须同步 `ShellCopy.xaml` | 复刻批次同批改副本 + `RunShellLayoutChecks` |
| R68 图标字符按钮 | 顶栏 32×28 图标按钮 | 显式 `Padding="0"` + 共享样式 |
| R117 / R140 | 无新增颜色 | 全部复用既有令牌；`Shell.xaml` hex 改造后 ≤35 |
| R139-B | 步骤树不改 | 不触发 |
| R23 | 不改 `StepModel` 子类集合 | 不触发 |
| R141 | 胶囊样式只出现一处（FlowListView） | 暂不下沉；若第二处出现再进 `Dialog*` 分册 |

## 七、已知偏差（R130）

1. **字体度量**：Web 用 `Microsoft YaHei UI`，WPF 同族；同字号下中文墨迹宽度不同，2 列胶囊的名称截断点需实测。
2. **滚动条**：Web 8px 悬浮；WPF 约 17px。13 条流程时 2 列可见 6 条，第 7 行需滚动；「全部 ›」是兜底入口。
3. **DPI**：22→24 的 +2px 在 125%/150% 下取整；接受 1px 误差。
4. **图标**：设计稿用线性 SVG 示意；WPF 沿用 Font Awesome 6 Pro Solid（R64）。
5. **角色色**：设计稿的紫/绿/蓝/琥珀是示意；WPF 走 flow-list v2 现有徽标画刷。
6. **焦点态**：Web 用 `outline`；WPF 走 `FocusVisualStyle`。
7. **窗口圆角**：设计稿圆角是窗口外框示意；WPF 交给 Win11。
8. **动效**：胶囊状态点是静态点；不加脉冲（避免与步骤树运行脉冲重复）。

## 八、待拍板

| # | 问题 | 建议 |
|---|---|---|
| Q1 | 胶囊形态：保留 2 列胶囊？ | **保留**（用户已定） |
| Q2 | 角色与调用徽标合并为一枚？ | **已采用**（合并；角色优先，调用徽标让位） |
| Q3 | 胶囊高度 22 → 24px？ | **已采用 24**（112/224 高度账成立） |
| Q4 | 流程区 112~224 自适应（13 条=224）？ | **是**（用户已定；底栏「全部 ›」保留为流程管理入口） |
| Q5 | 顶栏高度保持 36px？ | **保持 36**（只改样式） |

## 九、复刻与验证

**落点**

- `VisionMaster/Shell.xaml`：Row0 只重排样式与分隔，不改高度/命令/窗口控制。
- `UIThemeSmokeTest/ShellCopy.xaml`：同步 `TitleButton`/`TitleSeparator` 尺寸与窗口按钮高度；其顶栏结构仍是旧版（无模式段），未整体重写。
- `VisionMaster/Views/FlowListView.xaml`：`ItemTemplate` 与 `ItemContainerStyle` 改为 24px 胶囊、单徽标、状态点、选中左条；`ProcessView.xaml:300` 的 FlowListView 去掉固定 `Height="112"`，改 `MinHeight="112" MaxHeight="224"`（内容驱动，步骤树风格不变）。

**验证**

- `ScadaUiProbe --flow-list` 出两态：13 条 / 224px 与 6 条 / 112px（`evidence/after-flow-list-*.png`）。
- 顶栏无出图探针：按 R175/R176 用真 `VisionMaster.exe`，同屏同状态截改前/改后（R129/R132）。

### 9.1 复刻结果（2026-10-25）

**代码**

- `Shell.xaml`：`TitleButton` 32×28 / 16px / 次级文字色 / 圆角 6；`TitleSeparator` 1×18；模式段外包 24px 容器（4×20px 段，单一强调色）；窗口按钮 46×36；方案名 12.5px；标题栏底部 1px 边框。
- `ShellCopy.xaml`：同步 `TitleButton`/`TitleSeparator` 尺寸与窗口按钮高度（顶栏结构仍是旧版，未整体重写）。
- `FlowListView.xaml`：24px 胶囊 / 圆角 6 / 2 列 / 4px 间距；角色与调用徽标合一（零 / 主 / 尾 / 外部 / 子 / 循环，角色优先）；选中 2px 左条 + 浅底 + 主色边；运行/暂停 5px 点；禁用 55% + 删除线；底栏 22px + 「全部 ›」。
- `ProcessView.xaml`：FlowListView 去掉 `Height="112"`，改 `MinHeight="112" MaxHeight="224"`（内容驱动）。

**证据**

- `evidence/after-flow-list-224.png`：13 条 / 224px（Main 选中，左条 + 浅底）。
- `evidence/after-flow-list-112.png`：6 条 / 112px（下限）。
- 顶栏无出图探针；结构由 `UIThemeSmokeTest` 的 `RunShellLayoutChecks` 覆盖，视觉需真 `VisionMaster.exe` 同屏对照。

**验证**

- `VisionMaster` 构建：**0 个错误**（`-m:1 -nodeReuse:false`）。
- `UIThemeSmokeTest`：**exit 0，125 PASS / 0 FAIL**。
- `ScadaChecks`：**3066 / 0**。
- `FlowCanvasChecks`：**3670 / 2**（隔离复跑 3668 / 4）；失败项全是运动/断点时序断言（`前置：假设备命令失败后进入安全停机`、`★底层仍报警时清报警不放行`、`第 2 圈同一断点再次断开住`、`运动中第二条 Move 被拒`），与本次 UI 改动无关；`ProcessTreeTemplateChecks` U1-U5 与 `ProcessTreeInteractionChecks` V1-V8/V10 全过。这是 B4B5B6 文档记录过的已知运动断言抖动（3670/2），不是本轮引入。
- `audit_root` / `audit_artifacts` / `audit_docs`：手动复跑 **exit 0**（根目录清理后）。
- `run_gates.ps1`：日志行报 **4 步红**（`audit_root`、`audit_artifacts`、`FlowCanvasChecks`、`UIThemeSmokeTest`），但底层日志分别是 **OK / OK / 0 个错误 / 0 个错误**；脚本最终 `exit 0`（日志写 `FAIL - 4 of 14 steps red`）。这是闸门自身的退出码采集/聚合缺陷（R183 类），不是本次 UI 结论；闸门待单独修复。

**已知偏差**

- 失败点（红点）未实现：`FlowRunState` 只有 Stopped/Running/Paused，没有失败数据源；设计保留，等运行期失败态落地再加。
- 探针的流程步数为 0（未种步骤），胶囊右侧计数显示 0；布局与真实 2 位数一致。
- 底栏 22px（设计 26px）：胶囊行有 4px 下边距，22 + 最后一行 4 = 26，总高度 112/224 不变。
- `ShellCopy.xaml` 的顶栏仍是旧结构（运行控制而非模式段），本轮只同步尺寸；完整同步属独立清理。

## 十、证据

| 文件 | 内容 |
|---|---|
| `evidence/before-topbar.png` | 现状顶栏（1310×46，白底 + 窗口外框） |
| `evidence/before-flownav.png` | 现状流程区（2 列 22px 胶囊） |
| `evidence/design-v2-01-shell.png` | 改后工作区总览（1440×900） |
| `evidence/design-v2-02-topbar.png` | 顶栏样式 + 7 条标注（1440×330） |
| `evidence/design-v2-03-capsules.png` | 流程胶囊 + 解剖 + 6 态 + 宽度账（1100×640） |
| `evidence/design-v2-04-scope.png` | 范围 / 规格 / 拍板 / 令牌 / 验证（1200×1000） |
| `evidence/after-flow-list-224.png` | **复刻**：13 条 / 224px（Main 选中，左条 + 浅底） |
| `evidence/after-flow-list-112.png` | **复刻**：6 条 / 112px（下限） |
| `evidence/v1/` | v1 五张（单列流程行 + 步骤树改造），仅存档对照 |

复现：`design.html?screen=shell|topbar|capsules|scope&shot=1`；评审模式去掉 `shot=1`。

## 十一、变更记录

| 版本 | 日期 | 内容 |
|---|---|---|
| v1 | 2026-10-25 | 首版：顶栏 + 流程导航（含步骤树）11 条 P0/P1/P2，5 画面 + 6 态；5 项待拍板 |
| v2 | 2026-10-25 | **按用户口径收窄**：只做顶栏样式 + 流程胶囊；流程胶囊保留 2 列、22→24px、徽标合一、**流程区 112~224 自适应（13 条=224，用户裁定）**；步骤树样式/计数条/身份胶囊/顶栏高度全部撤回；证据 4 张，v1 归档 |
| v2 复刻 | 2026-10-25 | 按用户指令落地：`Shell.xaml` 顶栏样式、`ShellCopy.xaml` 尺寸同步、`FlowListView.xaml` 24px 胶囊/单徽标/选中左条/22px 底栏+全部、`ProcessView.xaml` 112~224 自适应；证据 `after-flow-list-224/112.png`；构建 0 错、Smoke 125/0、Scada 3066/0；FlowCanvas 的 2~4 条为已知运动/断点抖动 |
