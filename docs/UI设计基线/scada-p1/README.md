# SCADA P1 控件与能力 · Web 设计基线 v1

日期：2026-10-24
状态：**StateView / SubPage P1a / 位操作已复刻（2026-10-25）**；报警生命周期事件按用户决定本批不做；Web 运行态最后统一（WR 例外清单登记 StateView/SubPage）。
范围：P1 剩余四项——`Hmi.StateView`（图形开关 / 图形 IO 域 / 字指示灯）、`Hmi.SubPage`（画中画）、报警生命周期事件、位操作动作。
前置：P1-1（方案级文本列表 + ComboBox）已落地并验证，见 `docs/code-changes/2026-10-24-SCADA文本列表与ComboBox接入（P1-1）.md`。
实现记录：`docs/code-changes/2026-10-25-SCADA-P1三项实现（StateView-SubPage-位操作）.md`。

> 实现状态：StateView 一个 TypeKey（Text/Image/TextAndImage；`Both` 因与 Line 的 `Both=双向` 撞译名，落盘值改用 `TextAndImage`）；SubPage P1a 固定页 + 只读 + 活数据；位操作 `SetBit/ResetBit/ToggleBit` + `BitIndex 0~31`。`ScadaChecks` 2802/0，`UIThemeSmokeTest` 0 FAIL，`FlowCanvasChecks` 3657/0。

## 一、设计文件

| 文件 | 内容 |
| --- | --- |
| `design.html` | 可交互设计稿：StateView 三场景、SubPage 固定/变量/缺页/无权、报警激活/恢复/确认、位操作置位/复位/取反；深色/浅色切换 |
| `evidence/01-overview.png` | 首屏总览 |
| `evidence/02-interactions.png` | 交互验证后的整页截图（28 条断言全绿） |
| `evidence/03-stateview.png` | StateView 区域 |
| `evidence/04-subpage.png` | SubPage 区域 |
| `evidence/05-alarm.png` | 报警事件区域 |
| `evidence/06-bit.png` | 位操作区域 |
| `evidence/07-light.png` | 浅色主题首屏 |
| `../../方案设计/2026-10-24-SCADA-P1代码评审与设计切分.md` | 代码评审：复用面、契约改动、风险、建议批次 |

## 二、R111 六项

### 1. 布局

- 页面最大宽 `1480px`，左右 `28px` 安全边距；顶部品牌区 + 吸顶目录 + 七个章节（评审结论 + 四个能力 + 实施切分 + 待拍板）。
- 每个能力一节：左侧 `1.55fr` 运行态预览，右侧 `.85fr` 属性面板/动作编辑器；`1180px` 以下折为单列。
- 预览区统一“卡片 + 网格底 + 舞台”结构；StateView 用大预览 + 两个变体卡；SubPage 用 1.2fr/0.8fr 双页并排；报警用横幅 + 三事件卡 + 时间线；位操作用 8 位字 + 动作编辑器。

### 2. 间距

- 间距阶：`4 / 6 / 8 / 10 / 12 / 14 / 16 / 18 / 24 / 32`；卡片内边距 `16`，卡片头 `13 16`，舞台 `18`。
- 圆角：按钮/输入 `6~8`，卡片 `10~12`，胶囊 `999`。
- 网格底：`22px × 22px`，线色 `--grid`；只在预览舞台出现，正文不用网格。

### 3. 配色令牌

| 令牌 | 深色 | 浅色 | 用途 |
| --- | --- | --- | --- |
| `--bg` / `--bg2` | `#0B1016` / `#0E1318` | `#EEF3F9` / `#F4F7FB` | 页面 / 舞台底 |
| `--surface` / `--raised` | `#151C23` / `#1B242D` | `#FFFFFF` / `#F8FAFC` | 卡片 / 卡片头 |
| `--border` / `--border2` | `#2A3745` / `#3A4A5C` | `#D8E0EA` / `#B9C6D6` | 分隔 / 强边 |
| `--text` / `--text2` / `--muted` | `#E6EEF7` / `#A9B7C6` / `#7C8A99` | `#0F172A` / `#475569` / `#64748B` | 主 / 次 / 弱文字 |
| `--accent` | `#38BDF8` | `#0284C7` | 主操作、焦点、链接 |
| `--success` | `#22C55E` | `#15803D` | 运行、正常、置位 |
| `--warning` | `#F59E0B` | `#B45309` | 警告、缺页、恢复未确认 |
| `--danger` | `#EF4444` | `#B91C1C` | 故障、报警、复位 |
| `--purple` | `#A78BFA` | `#7C3AED` | 预留（配方/权限） |

- 状态色只从列表条目 `Color` 取；无匹配/缺失统一用 `--muted` + `--warning`，不拿相邻状态色顶上。
- WPF 复刻时映射到 `UI/Controls/Themes/Colors.xaml` 的 `Hmi*` 令牌；本页色值只是设计基线，不是 WPF 资源键。

### 4. 字号与字重

| 层级 | 字号 | 字重 | 用途 |
| --- | --- | --- | --- |
| 页面标题 | 24 | 800 | 顶栏 H1 |
| 章节标题 | 17 | 700 | `01 StateView` 等 |
| 卡片标题 | 13 | 700 | 卡片头 |
| 正文 | 12~13.5 | 400 | 说明、列表 |
| 辅助 | 10.5~11.5 | 400/600 | 标签、状态、表格 |
| 数值/路径 | 10.5~13 | 600/700 | 等宽字体（Cascadia Mono/Consolas） |
| 状态大字 | 24 | 800 | StateView 文本态 |

- 中文用 `Microsoft YaHei`，西文回退 `Segoe UI`；等宽只用于值、路径、bit 号、日志。
- 数字与单位分开排版（`12.4` + `s`），避免比例字体下数字跳动。

### 5. 五态 + 数据态

交互控件统一：默认 / 悬停（边框转 accent、背景 `--hover`）/ 按下（下移 1px）/ 焦点（2px accent 外框）/ 禁用（opacity .45、`cursor:not-allowed`）。

数据态：

- **StateView**：正常 / 列表缺失（“未找到文本列表” + 告警条）/ 无匹配（`DefaultText` + 虚线图标）/ 图片缺失（虚线占位，不拿别的图）/ 权限拒绝（只读 + 原因）。
- **SubPage**：正常 / 未选择画面 / 目标页删除 / 变量值无对应页 / 当前角色无权；后四者都不显示空白页。
- **报警事件**：激活未确认 / 已恢复未确认 / 已确认仍激活 / 正常；事件卡在触发时高亮 900ms。
- **位操作**：整型 Word / 布尔 Bool / BitIndex 越界 / 变量缺失 / 无写入权限；后两者按钮置灰并显示原因。

### 6. 交互细节

- 统一过渡 `160ms cubic-bezier(.2,.7,.2,1)`；`prefers-reduced-motion` 下关闭。
- StateView 预览在 `Interaction != None` 时可用鼠标点击，也支持 `Enter` / `Space`；`None` 时 `role=group` 且不可聚焦。
- 图形开关点击立即切换 0/1，并在日志里写“Value 0 → 1，触发 ObjectChanged”。
- SubPage 切换 `PageSource` / 变量值 / 异常复选框即时更新；目标页下拉在 `FollowVariable` 时禁用。
- 报警按钮按状态禁用：激活态只能恢复/确认；恢复态只能确认；确认后恢复直接到正常。
- 位操作按钮按变量类型切换：Bool 隐藏 BitIndex；整型按位号读-改-写；清零是演示辅助。
- 主题切换只改 `data-theme`，不重载页面；设计稿无外部图片/字体依赖。

## 三、代码评审摘要

| 项 | 结论 | 关键代码 |
| --- | --- | --- |
| StateView | 复用 P1-1 的列表/解析器/缺失口径；条目加 `Image`/`Color`/`DefaultImage`；一个 TypeKey + 三个预设 | `ScadaStateList.cs:18-38`、`ComboBoxElement.cs:303-340`、`ScadaElementBase.cs:304-337` |
| SubPage | 最大成本是嵌套画布的数据泵与事件路由；P1a 固定页 + 只读 + 活数据，P1b 再变量切页/交互 | `ScadaRuntimeBinder.cs:121`、`ScadaCanvas.cs:1050-1092`、`ScadaRuntime.cs:253-269` |
| 报警事件 | 引擎已有三事件；补三条页面事件 + `AlarmId/AlarmName` 过滤 + UI 线程路由；“取消激活”不能只靠 `AlarmChanged` 反推 | `ScadaAlarmEngine.cs:201-207`、`ScadaAlarmEngine.cs:620-725`、`ScadaEventHook.cs:26-106` |
| 位操作 | `BitButtonElement` 已有五种模式；动作表补 `SetBit/ResetBit/ToggleBit` + `BitIndex`；复用 `IScadaValueSource` + `TryWrite` | `BitButtonElement.cs:52-65`、`ScadaActionType.cs:15-109`、`ScadaActionDispatcher.cs:459-520` |

完整评审与契约改动清单见 `../../方案设计/2026-10-24-SCADA-P1代码评审与设计切分.md`。

## 四、范围与不做

- **一个 StateView TypeKey**，不新增 `GraphicSwitch` / `GraphicIO` / `WordLamp` 三个 TypeKey；预设只改默认值。
- **SubPage P1a 只读**；子页按钮事件、变量切页交互放 P1b。
- **报警事件只做页面级**；图元级（AlarmBanner 自己）本批不做。
- **位操作不做确认框**，与 `WriteVariable` 同级；如现场要求再单独加 `IsDangerous`。
- 不做报警历史表、行内确认、故障码、报表、用户管理、文件浏览、PDF/浏览器/数据库视图。
- 不改 P1-1 已落地的 ComboBox 行为与列表管理器语义。

## 五、待拍板（Q1~Q6）

1. StateView 一个 TypeKey + 预设，还是三个独立 TypeKey？（本稿建议一个）
2. 列表条目是否要 `Color`，还是只做 `Image` + `Text`？（字指示灯需要状态色）
3. SubPage P1 是否接受只读？（接受 = M；不接受 = L）
4. 报警事件只做页面级，还是也要图元级？
5. 位号范围先固定 0~31，还是按变量类型动态给上界？
6. Web 运行态本批同步到哪一步？StateView 建议同步；SubPage 视快照递归成本再定。

## 六、验收清单

- [ ] 设计稿在深/浅两主题下逐节对照，无溢出、无截断、无外部资源。
- [ ] StateView：Text / Image / TextAndImage 三态（落盘值用 TextAndImage，避免与 Line 的 `Both=双向` 撞译名）；列表缺失、无匹配、图片缺失、权限拒绝；点击 Toggle / WriteValue。
- [ ] SubPage：固定页、变量切页、未选、缺页、无权；只读边界明确。
- [ ] 报警事件：激活、恢复、确认各一次；全部 vs 指定报警；非当前页不响；确认后不重复。
- [ ] 位操作：Bool、整型 bit0/bit31、越界、变量缺失、写失败；审计文案。
- [ ] 用户确认 Q1~Q6 后，再按 R111/R126 进入 WPF 复刻；未确认前不改桌面端样式。

## 七、验证记录

| 手段 | 结果 |
| --- | --- |
| Edge 无头渲染 | 首屏、四个章节、浅色主题均正常；无外部资源请求 |
| 交互探针（临时脚本，已删） | **28/28 通过**：StateView 6、SubPage 4、报警 5、位操作 5、主题 2、页面/异常 3、其余 3；控制台 0 错误。追加“五态与数据态”卡后结构复检 **6/6**（7 个章节、四个能力节 ID、五态卡、初始值、开关点击、0 异常） |
| 截图 | `evidence/01~07`，见上表 |

> 临时探针脚本按 R105/R167 用完即删；本页不参与构建、不部署，不作为第二客户端维护。
