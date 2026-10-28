# 变量管理（全局变量）· 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v3（+ 2026-10-21「显示分组」增量 v3.1，**未走 Web 设计稿**）+ 2026-10-21「批量导入 / 导出」增量 v3.2（**走 Web 设计稿** `variable-batch-io-design.html`，会话可视化目录、不入仓），见文末「v3.1」「v3.2」 |
| 日期 | 2026-10-21 |
| 确认日期 / 人 | 2026-10-21 / 用户（指令：数组编辑弹窗也要走 Web 设计稿优化；显示分组由用户直接提需求；批量导入 / 导出为「对于单个设备 应该建立 批量导入导出的功能」，先出 Web 设计稿再实现） |
| 覆盖范围 | `VisionMaster\Views\DialogViews\GlobalVariableView.xaml` + `GlobalVariableView.xaml.cs`；`VisionMaster\Views\DialogViews\ArrayEditorView.xaml` + `ArrayEditorViewModel`（数组编辑弹窗，v3 专项优化）；左栏二级分组树 / 「分组」列 / 管理分组覆盖层（v3.1 增量）；`DeviceVariableExportView.xaml` / `DeviceVariableImportView.xaml` + 两个 ViewModel（单设备变量表批量导入 / 导出，v3.2 增量） |
| 窗口尺寸 | 变量管理 1300 × 700 默认、最小 1200 × 580、左栏 232px；数组编辑弹窗 **640 × 660、最小 540 × 540**；导出弹窗 **620 × 560、最小 580 × 520**；导入弹窗 **1060 × 800、最小 960 × 700** |
| 设计基线文件 | `design.html`（自包含、无外部依赖、内联 SVG 图标）。**注意：`design.html` 早于 2026-10-21「显示分组」增量**——左栏二级分组、表格「分组」列、管理分组覆盖层**不在设计稿里**；该增量以本 README 与 `evidence\after-render-v1-*.png` 为准，设计稿需要时再重绘。**v3.2 批量导入 / 导出另有独立 Web 稿** `variable-batch-io-design.html`（会话可视化目录，**未复制进仓库**），该增量同样以本 README 与两张 `after-render-v1-export/import.png` 为准 |
| 设计证据 | `evidence\design-v1-*.png`（v1 八态）+ `evidence\design-v2-*.png`（数组摘要 / 弹窗 / 校验失败）+ `evidence\design-v3-*.png`（弹窗分段范围 / 逐行基准 / 校验失败）。v3.2 的 Web 稿不在仓库内，证据只有 WPF 渲染图（见文末 v3.2 证据表） |
| 复刻状态 | **v1 已完成（2026-10-20）**；**v2 已完成（2026-10-21）**；**v3 已完成（2026-10-21）**；**v3.1 显示分组已完成（2026-10-21，实现驱动）**，WPF 证据 `evidence\after-render-v2-*.png`（表格层）、`evidence\after-render-v3-*.png`（弹窗）、`evidence\after-render-v1-group-editor.png`（管理分组覆盖层）；**v3.2 批量导入 / 导出已完成（2026-10-21）**，WPF 证据 `evidence\after-render-v1-export.png`、`evidence\after-render-v1-import.png`；记录 `docs\code-changes\2026-10-20-变量管理GlobalVariableView Web化复刻.md`、`docs\code-changes\2026-10-21-变量管理数组编辑v2（当前值与基准分离+数组编辑弹窗）.md`、`docs\code-changes\2026-10-21-变量管理显示分组（二级树+扫描组入口收敛）.md`、`docs\code-changes\2026-10-21-设备变量表批量导入导出.md` |

## 定位

本文件是「Web 设计基线 → 用户确认 → WPF 复刻 → 同屏截图验收」四步流程的**第 ① 步**产物，
流程口径沿用 `docs\UI设计基线\插件自定义视图Web化改造SOP.md`（R126-R132）。
本页是**宿主弹窗**，不是插件自定义视图，因此没有 `IPluginConfigView` / `LinkableValueEditor` / HALCON 画布；
但「先出完整 Web 设计稿、确认后再改 WPF、零功能改动、断言守门」的纪律完全一致。

Web 只作设计媒介，**不进入构建、不部署**。确认后按本基线做 WPF 复刻，第 ④ 步以同一屏、同一状态的截图对照验收。

## 一句话方向

**保留两栏主从结构，把「来源导航 / 明细行 / 当前值编辑 / 空态」四件事做清楚。**
左栏从「一列图标文字」升级为真正的来源导航；右栏从「一张 30px 高的普通表格」升级为
可扫描的数据工作区：变量名两行、类型有色点、当前值是编辑主入口、系统变量只读语义前置。

## 审查清单（请重点看这几项）

- [ ] 左栏 232px + 「本地与系统 / 通信连接」两组 + 48px 来源卡片（状态副行 + 计数）。
- [ ] 明细行两行式（变量名 + 描述）、根行 54 / 子行 40、类型 chip + 色点。
- [ ] 当前值单元格：质量点 + 提交式输入 + 偏离时的 ↺；数组根是「摘要 + 编辑数组… + 偏离时的 ↺」（v2）。
- [ ] 系统来源只读横幅 + 操作列「只读」chip；「添加变量」保留（点击会切到本地）。
- [ ] 空态与搜索无结果分开；底部状态条 + 键盘提示。
- [ ] 「添加变量」属性网格弹窗是否要在本批一起改（当前列在 D1 延后）。

## 现状审查（改动理由）

| # | 现状 | 影响 |
| --- | --- | --- |
| 1 | 左栏来源节点 38px 单行：图标 + 名称 + 计数；在线状态只有一个 7px 圆点，系统变量与本地变量外观几乎一样 | 用户分不清「本地 / 系统 / 网络」，也看不出系统变量为什么不能删；连接离线只能靠一个红点猜 |
| 2 | 右栏卡片头 52px：标题 + 计数 + 220px 搜索 + 管理扫描组 + 添加变量全挤在一行 | 最小宽度下搜索框与按钮互相挤；没有「当前来源」上下文，切换来源后只能从左栏反推 |
| 3 | 明细表行高约 30-32px，变量名 / 类型 / 来源 / 地址 / 当前值同字重、同灰度 | 变量一多就是一片灰字；描述（`VariableNode.Description`）已有数据但完全没上屏 |
| 4 | 当前值单元格是 110px 裸输入框；质量点只有 5px 且无图例；↺ 只在偏离时出现 | 写值/复位是最高频动作，却最不显眼；黄点/灰点含义只能靠猜 |
| 5 | 表格无空态、无搜索无结果态 | 空来源或搜不到时是一张白板，看起来像加载失败 |
| 6 | 底部是一段 11px 长说明（写值/复位/Runtime.*） | 操作要点不在操作现场；长段落占掉表格高度 |
| 7 | 系统变量行仍显示复制/重命名/删除三颗按钮，点删除才弹「不能删除」 | 给了可点的假象；用户要先点一次才知道这是只读数据 |
| 8 | 来源列表没有分组：本地、系统、每条连接平铺 | 连接一多就分不清「本机变量」与「设备变量」两类信息 |

## v1 方向

1. **两栏不变，左栏 232px**：左栏来源导航，右栏明细工作区；主从关系不重新发明。
2. **来源分组**：「本地与系统」+「通信连接」两组；节点是 48px 卡片：28px 图标块 + 名称 + 状态副行 + 计数。
   选中态 = 白卡 + 主色左条；系统变量用 CPU 图标 + 锁副行；连接用插头图标 + 在线/离线副行。
3. **右栏头部建立上下文**：`变量明细` + 数量徽标 + 当前来源 chip + 一句副标题；搜索 238px、
   管理扫描组（仅网络来源）、`＋ 添加变量` 固定右对齐。
4. **明细行两行式**：根行 54px（变量名 12.5px + 描述 10.5px），子行 40px（缩进 24px）；
   类型是中性 chip + 类型色点；数组根显示「N 项」；地址用等宽字体。
5. **当前值单元格是编辑主入口**：质量点（正常/读取失败/断线）+ 提交式输入框 + 偏离时的 ↺；
   数组根保留「强制覆盖集合...」（**v2 已改为「摘要 + 编辑数组… + ↺」**）；图像与只读存储区显示锁 + 值，不给可编辑假象。
6. **空态与搜索无结果分开**：空来源给「还没有变量 + 添加变量」；搜索无结果给「没有匹配 + 清空搜索提示」。
7. **系统变量只读语义前置**：系统来源顶部常驻只读横幅；行内操作列换成「只读」chip，
   不再显示点了只会弹提示的删除/重命名按钮。
8. **底部状态条**：左侧 `N 个变量 · 来源 · 内存/轮询`；右侧 `Enter 提交 / Esc 取消 / ↺ 恢复初始值`。
9. **全令牌化**：视图零十六进制；类型色复用既有语义族（只追加 3 个数组色键）；图标按钮 30×30 + `Padding=0`（R68）。

## v2 数组编辑（2026-10-21）

### 起因

v1 的数组根只有一个「强制覆盖集合...」：弹窗装填 `DefaultValue`，确认时同时写 `Value` 与 `DefaultValue`。
流程写过数组之后，表格显示的是当前值、弹窗显示的是基准，用户点确定等于把屏幕上的运行值换掉；
元素级只有"改当前值"，没有复位、没有摘要，运行时数组长度变化也不重建子行。用户原话：
「关于数组的编辑 现在功能有所欠缺 只有个强制覆盖」。

### 三条语义（本版核心）

1. **装填当前值**：弹窗里看到的就是表格里那组值（`Value`），不是 `DefaultValue`。
2. **默认只写当前值**：勾选「同时写入初始值（基准）」才把新数组写进 `DefaultValue`。
   基准是「恢复初始值」的锚点，改它必须是一次显式选择。
3. **校验在弹窗内**：点确定先逐项转换；出错项标红 + 底部「第 N 项：…」，弹窗不关、其余编辑保留。

### 表格层

| 位置 | v1 | v2 |
| --- | --- | --- |
| 数组根「当前值」 | 「强制覆盖集合...」文字链接 | 等宽摘要（前 2 项 + …，ToolTip 全量）+「编辑数组…」按钮 + 偏离时的 ↺ |
| 数组元素行 | 当前值输入框 | 当前值输入框 + 逐元素 ↺（基准与当前值都含该下标、且值不同才出现） |
| 运行时长度变化 | 子行不重建（行数/徽标停在旧值） | `max(当前值长度, 基准长度)` 与子行数不一致 → 重建树 |

### 数组编辑弹窗（第二个面）

`ArrayEditorView` / `ArrayEditorViewModel`，注册名 `ArrayEditor`：

- 标题「编辑数组 · {变量名}」+ 副标题「{元素类型} · 当前 N 项」；
- 顶部说明条 + 「同时写入初始值（基准）」复选框（默认不勾）+ 右侧基准项数（长度不同会标出来）；
- 列表：序号 / 元素值 / 上移 / 下移 / 删除；底部「添加元素」+ 计数 + 取消 / 确定；
- 校验失败：出错行红框 + 底部错误条（`第 N 项：…`），弹窗保持打开；
- Esc = 取消；确定 = 事务性回传（`Array` / `Variable` / `SyncBaseline`），由 VM 落地。

### 契约变化（v1 契约表之外的新增）

| # | 项 | 说明 |
| --- | --- | --- |
| C11 | `ApplyArrayEdit(model, array, syncBaseline)` | 当前值总是写；基准只在勾选时写。纯静态，便于断言 |
| C12 | `ResetElementCommand` | 元素级 ↺：只恢复 `Value[i] = DefaultValue[i]`，并补广播一次 `ValueChanged` |
| C13 | `IValueChangeNotifier`（VM.Core） | 数组元素就地写入后引用未变，`Value` setter 的 SetProperty 会短路 → 显式广播 |
| C14 | `IsValueDeviated` 逐元素比较 | 数组按引用比会永远判"已偏离"（弹窗写的是新实例） |
| C15 | `ShouldRebuildArrayRows` | 运行时长度变化 → 子行重建（编辑中不重建） |
| C16 | 运行中互锁 | 流程 Running 时禁止编辑数组（与删除变量同口径） |

### 顺带修复（独立审查抓出，已闭合）

- **基准与当前值拆成独立实例**：`VariableFactory.CreateLocal` / `AddLocalVariable` 对数组给 `DefaultValue` 克隆一份。
  同实例时元素就地编辑会同时改掉基准，逐元素比较还会因 `ReferenceEquals` 判"未偏离"。
- **基准更长时的死 ↺**：`IsElementDeviated` 要求当前值也含该下标，与 `ResetElementValue` 对齐。
- **编辑结束补重建**：`VariableNode.ValueEditCompleted`（提交 / 取消都发）→ VM 复查 `ShouldRebuildArrayRows`，
  覆盖"编辑中外部改了数组长度、用户按 Esc"这条没有模型事件的路径。
- **落地前复查运行态**：`OnArrayEditorClosed` 再次检查 `RunState`，弹窗期间流程启动则丢弃本次编辑。

### 已知偏差（v2）

1. 弹窗错误文案沿用 `VariableValueConverter` 的 `ex.Message` 尾巴（如 `The input string 'abc' was not in a correct format.`）；
   关键信息（第几项 / 原值 / 目标类型）在前，英文尾巴不影响定位。
2. 结构改动（增删 / 上下移）只在弹窗里：40px 元素行不挂删除图标，避免误点导致整组下标错位。
3. 字符串数组的"空串"现在原样保留（v1 写成 null）；旧方案里 null 元素再次编辑会变成空串，属有意归一。
4. 元素级 ↺ 只在"基准与当前值都含该下标"时出现：任一侧更短时，"恢复"要么清空元素、要么写不进去，都不给这个假入口。

## v3 数组编辑弹窗优化（2026-10-21）

### 起因

v2 的弹窗功能完整，但信息层级偏平：写入范围是一个孤零零的复选框、改过的行和没改的行长得一样、
底部只有元素总数——用户点确定前看不出"这次会改什么、原值是多少"。

### 设计

| 位置 | v2 | v3 |
| --- | --- | --- |
| 写入范围 | 一行复选框 + 一段说明 | 范围卡：分段选择「只写当前值 / 同时写初始值」+ 右侧基准项数 + 一句话后果说明（随选择变化） |
| 行 | 输入框 + 上移/下移/删除 | 改过的行在输入框下显示「● 基准：原值」，行尾多一颗 ↺（只恢复这一项） |
| 标题 | 「类型 · 当前 N 项」 | 「类型 · 当前 N 项 · 基准 M 项」 |
| 底部 | 添加元素 + 总数 + 取消/确定 | 添加元素 + 总数 + 「N 项已修改」（未改时灰色）+「恢复全部为基准」（无修改时置灰） |
| 尺寸 | 620 × 640 | 640 × 660（最小 540 × 540） |

### WPF 复刻映射

| 设计项 | 实现 |
| --- | --- |
| 分段选择 | 两个 `Button` + 内联 Style 的 `DataTrigger`（绑 `IsCurrentOnly` / `SyncBaseline`）。**不用 ToggleButton**：它会先在本地翻转 `IsChecked`，命令再写回同值时 OneWay 绑定不发通知，段会停在"两段都没选中" |
| 逐行基准 | `ArrayEditorRow.BaselineText` / `IsChanged`；比较按元素类型转换后做（"12.50" 与 "12.5" 视为相同），转换失败按"已修改"处理 |
| 行内 ↺ | `ResetRowCommand`：只把该行文本写回基准对应项 |
| 恢复全部 | `ResetAllCommand`：整张列表换成基准（长度一起变） |
| 修改汇总 | `ChangedCount` / `HasChanges` / `ChangedSummary`；每改一行只重算该行与总数，不重建列表（保住焦点） |
| 24px 图标按钮 | `DialogIconButton` 带 `MinWidth/MinHeight=30`，**必须显式写 `MinWidth="0" MinHeight="0"`**，否则 4 颗按钮合计 120 > 108 列宽，最左那颗会被输入框盖住（首轮出图抓到） |

### 证据

- 设计：`evidence\design-v3-dialog.png`（1 项已修改）、`design-v3-dialog-both.png`（同时写初始值）、`design-v3-error.png`（校验失败）
- WPF：`evidence\after-render-v3-array-editor.png`、`after-render-v3-array-error.png`

**v3 验证结论（2026-10-21）**：`FlowCanvasChecks` **3032 通过 / 0 失败**（含新增的分段范围 / 逐行基准 / 行内 ↺ / 恢复全部 / 修改汇总断言）；`UIThemeSmokeTest --global-variables` 全部通过；渲染场景 K / K2 出图并核对。

## v3.1 显示分组（2026-10-21，实现驱动）

> 本节**没有对应的 Web 设计稿**：`design.html` 的 v1/v2/v3 早于本次改动。需求由用户直接给出
> （「设备下的变量要能做二级分类，比如监控 / 控制，分组可新增可改名、不能重名」），
> 因此本节的基线以 README 文字 + `evidence\after-render-v1-group-editor.png` 为准。

### 起因与边界

改造前左栏只有一级「来源」（本地变量 / 系统变量 / 每条连接）。一条连接挂几十个点时，监控点与控制点混在同一张表里。
本次在**连接行下面**加一层显示分组，只做**归类 + 筛选**：

- **不影响采集 / 轮询 / 写入**——与「扫描组」（连接级调度资源：周期 / 请求配额 / 段合并）是两条正交的轴；
- **本地 / 系统变量不参与**（v1 口径）：它们没有"设备归属"这一层；
- 分组是**方案级**资源，随 `.vms` 落盘；变量按 `Guid` 引用分组，**改名不级联**。

### 左栏（232px，结构从一级变二级）

```text
变量来源
本地与系统
  本地变量     6
  系统变量     8
通信连接
  PLC_Modbus   [▾]            ← 连接行可折叠
    全部              5
    监控分组          3
    控制分组          1
    未分组            1
  PLC_S7_1     [▸]
```

- 仍是**同一个 `ListBox`**（按 `IsNetwork` 分节，`GroupStyle` 不动），用 `ItemTemplateSelector` 切换两种模板：
  来源卡片 `GvSourceNodeTemplate`（48px）/ 分组行 `GvGroupNodeTemplate`（30px，左边框缩进 26px）。
- 分组行三段：图标 + 名称 + 计数；选中态 = 主色浅底 + 主色文字 + SemiBold；**0 计数的组降低不透明度但仍可点**。
- **空分组也显示**（计数 0）：分组表是方案级资源，用户刚建完就要能在树上看到。
- 只有连接行有展开箭头（`ShowExpand`）；连接**默认展开**，之后按用户折叠状态保留。

### 表格：「来源」列换成「分组」列

| 项 | v3 | v3.1 |
| --- | --- | --- |
| 第 3 列 | 来源 badge（120） | **分组 ComboBox（120）**，列名 `GroupColumn` |
| 来源信息 | 行内 badge | 卡片头 tag（`SelectedSource.DisplayName`）+ 左栏导航 |
| 分组下拉 | — | `GroupOptions` / `SelectedGroup` / `ShowGroup`；仅网络根节点显示；「未分组」恒在首位 |
| 扫描组列 | 104，仅网络 | 不变；列头提示改为「在通讯设置中维护」 |

**宽度契约不变**：分组列仍 120 / MinWidth 120，网络来源固定列合计仍是 736，1200 窄档 `736 + 160 = 896 ≤ 924`。
表格里不再有「来源」列是**有意取舍**（选中来源后整张表都只显示该来源的变量，来源列每行同值），
换来的是每行都可能不同、且需要就地修改的「分组」列。

### 管理分组覆盖层

卡片头「管理分组」按钮（`ManageVariableGroupsButton`，**常显**，不再是"仅网络来源"）→ 弹窗内覆盖层：

- 标题「管理分组」+ 副标题「仅用于归类与筛选，不影响采集、轮询与写入」；
- 列表：分组名称 / 变量数 / 行内操作（改名、删除）；末尾固定一行内置「未分组」（不可改名/删除）；
- **新增分组**：输入框（`MaxLength=20`）+「添加」；重名、空名、超长、保留名都在输入框下方给中文原因，按钮同时置灰；
- **改名**：行内编辑，实时校验；拿别人的名字才算重名；
- **删除**：行内确认条报影响面——「删除「X」？N 个变量将归入「未分组」，变量本身不会被删除。」；
- **即时生效**（与写值 / 删变量同一事务模型），没有"确定/取消"；分组表一变就重建左栏与明细节点；
- **无方案时**（启动早期异常路径）：明确提示「当前没有打开的方案，无法保存分组」并禁用「添加」，
  不再出现"输完名字点一下什么都不发生"。

### 与扫描组的分工（入口收敛）

| | 显示分组（本节） | 扫描组 |
| --- | --- | --- |
| 作用 | 归类 + 筛选 | 轮询节拍 / 请求配额 / 段合并 |
| 作用域 | 方案级（`SolutionModel.VariableGroups`） | 连接级（`CommunicationConfig.ScanGroups`） |
| 变量引用 | `GroupId`（Guid），改名不级联 | 组名，改名要同步轮询侧 |
| 维护入口 | 变量管理 →「管理分组」覆盖层 | **只在**通讯设置 → 连接 → 操作 → 扫描组 |

变量管理**只保留**"给变量选扫描组"的下拉；扫描组编辑器的入口已从变量管理移除——它是连接级调度资源，
改它需要 `IHardwareEditGate` 的"管理员 + 流程未运行"门禁与通讯配置置脏，而变量管理这条入口原本两者都没传。

### 验证（2026-10-21）

- `FlowCanvasChecks` **3066 通过 / 0 失败**（`[G]` 显示分组契约：唯一名 / 筛选 / 搜索跨组 / 删组回落 / 改名不级联 / 持久化往返 / 旧方案 / 编辑器命令 / `ApplyGroupSelection` / 列宽快照 / 扫描组入口静态扫描 / 接线 / 无方案提示）；
- `UIThemeSmokeTest --global-variables` **47 PASS / 0 FAIL**（分组行模板、分组作用域、覆盖层开关、分组列静态清单）；
- `--global-variables-render` 出 **9 张**，新增 `after-render-v1-group-editor.png`；
- 详见 `docs\code-changes\2026-10-21-变量管理显示分组（二级树+扫描组入口收敛）.md`。

## v3.2 批量导入 / 导出（2026-10-21）

> 本节有**独立的 Web 设计稿** `variable-batch-io-design.html`（会话可视化目录，**未复制进仓库**）。
> 需求由用户直接给出（「对于单个设备 应该建立 批量导入导出的功能」），先出设计稿、确认后做 WPF 实现。

### 起因与边界

变量管理原先只有「添加变量」一条录入路径：一条设备挂几十个点时只能逐条填属性网格；`.vms` 是整方案级文件，
拿不出"这台设备的变量表"；换一批地址 / 扫描组也只能一条条改。本批补的是**单设备维度**的整表进出：

- **只处理网络变量**（某条通信连接），本地 / 系统变量没有设备地址表，不参与；
- 导出的是**结构化字段**（存储区 / 偏移 / 位偏移 / 站号 / DB号 …），不是复合地址串；
- 导入**按变量名匹配**，不支持通过 Excel 改名；先出差异预览，再事务应用。

### 入口（仅网络来源）

卡片头右侧、搜索框之后、「管理分组」之前：`导出`（`ExportButton`）/ `导入`（`ImportButton`）。

- `Visibility` 绑 `IsNetworkSource`：本地 / 系统来源**整块收起，不置灰**（置灰会被误读成"权限不够"）；
- `CanExecute = IsNetworkSource && SelectedConnection != null && CurrentSolution != null`；
- 「导入」先弹文件选择框（`*.xlsx`），选完才开预览弹窗。

### 导出弹窗（620 × 560，最小 580 × 520）

| 区块 | 内容 |
| --- | --- |
| 范围 | 两张**等宽单选卡**：「全部变量」（该设备全部网络变量 + 计数）/「当前分组」（分组名 + 计数）；左栏没选中分组时第二张禁用并显示「先在左栏选中一个分组」 |
| 选项 | 「附带『说明』页（协议 / 存储区 / 数据类型 / 扫描组候选）」（默认开）、「附带『地址预览』列（只读，导入时忽略）」（默认开） |
| 保存到 | 只读路径框 +「浏览…」；默认「我的文档」+ `{连接名}_变量表_{yyyyMMdd_HHmm}.xlsx`；目标已存在时二次确认覆盖 |
| 汇总 | 蓝条一句话：`将导出 N 个变量：分组A 3 / 分组B 2`（当前分组档为 `将导出 N 个变量（分组名）`，不再重复组名） |
| 底栏 | 左侧「导入按变量名匹配；地址预览列不会被回读。」+ 取消 / 导 出 |

**范围口径**：全部 = 该连接下全部网络变量；当前分组 = `GroupId` 命中当前显示分组（悬空 Id 归一成「未分组」）。
**地址列**：存储区 / 偏移量 / 位偏移 / 站号 / DB号 / 元素个数 / 每元素字节数 / 地址偏移 / 40001写法 九列结构化；
`地址预览` 只读、导入端明确忽略。

### 导入弹窗（1060 × 800，最小 960 × 700）

| 区块 | 内容 |
| --- | --- |
| 文件条 | 文件名 + `N 行`；「重新选择」「下载模板」 |
| 步骤条 | `① 选择文件 ── ② 预览与确认 ── ③ 完成` |
| 四类计数 | 新增 / 更新 / 无变化 / 错误（四个描边胶囊 + 色点） |
| 差异表 | 行 / 变量名 / 类型 / 地址 / 扫描组 / 显示分组 / 状态 / 说明；按文件顺序；错误行在「说明」列给原因，勾选跳过后整行 `Opacity=0.5` |
| 选项 | 「跳过错误行」（仅有错误时可用）、「删除文件中没有的变量（N 个）」（默认关）、「自动创建未知显示分组」（默认开） |
| 后果提示 | 删除清单（前 3 个 + 总数 + 「流程 / 画面可能断链」）、未知扫描组回落说明、显示分组新建 / 回落说明 |
| 底栏 | `将应用 N 项`（只在有可应用项时出现）/ `有 N 行错误：请修正文件，或勾选「跳过错误行」` / `没有需要应用的变更` + 取消 + `应用 N 项`（只删除时按钮为「应用」） |
| 结果页 | 成功圆 + 「导入完成」+ `新增 x · 更新 y · 删除 z · 跳过 n` + 「关闭后变量管理会自动刷新；新增变量已接入轮询。」+ 完成 |

### 口径

| 项 | 口径 |
| --- | --- |
| 匹配 | 按变量名（不区分大小写）；同名 = 更新；**不支持通过 Excel 改名**；文件内重名、与本地 / 系统 / 其它设备撞名都是错误行 |
| 默认 | **全有或全无**：有错误行时「应用」置灰；勾「跳过错误行」才允许只应用合法行 |
| 删除 | **默认不删**文件中没有的变量；显式勾选才删，并列出将删名字 |
| 扫描组 | 未知组名回落「默认组」，**不自动建组**（扫描组是连接级调度资源） |
| 显示分组 | 未知组名按选项**自动建组**（默认开；复用分组编辑器 `ValidateGroupName`，空名 / 超长 / 保留名 / 重名都拒绝）；关闭则回落「未分组」 |
| 事务 | 新增 / 更新 / 删除 / 自动建组同一事务；地址（含地址类型）/ 扫描组 / 区域合并读 / 类型变化必须 ReRegister，**返回 false 即抛错并回滚**；任一步失败回滚，回滚本身失败会逐项报出（「回滚未完全成功：…」） |
| 数组 | 「初始值」列按设计忽略（Excel 里为空），更新时**保留已有基准** `DefaultValue`；**更新行空白初始值 = 保持原值（含 null）**，新增行留空才用类型默认值 |
| 运行锁 | 流程运行中禁止导入（VM 点「应用」时拦截，服务层 `Apply` 再守一次；预览是纯读，允许在运行中打开） |
| 关闭 | 确认 / 结果页 × / **尝试过「应用」（含失败）**后的取消与关闭都返回 OK → 主 VM 重建左栏并回到该设备「全部」；失败关闭也刷新，只是不弹成功提示。**未应用过的取消**保持原选择、不重建 |
| 写盘 | 先写同目录临时 `.xlsx` 再 `File.Move(overwrite: true)`，失败不丢用户原文件 |

### 验证（2026-10-21）

- 构建：`dotnet build VisionMaster\VisionMaster.csproj` **0 错误**（冒烟复跑走 Release 通道：Debug 输出目录被运行中的 `VisionMaster` 锁定，`-c Release -p:BuildProjectReferences=false`，逻辑与 Debug 一致）；
- `FlowCanvasChecks` **3150 通过 / 0 失败**（`[IO]` 段 19 组：导出列契约 / xlsx 往返无变化 / 计划校验 / 自动建组与回落 / 初始值语义 / 空格名 / 字符串标量跨度 / 应用新增 / 更新重注册 / 数组基准保留 / 跳过错误行 / 损坏默认值 / scalar→array / 协议切换 / 删除缺失 / 全量回滚 / S7 DB 号 / 三菱 MC / 欧姆龙 Fins）；
- `UIThemeSmokeTest --global-variables` **54 PASS / 0 FAIL**（入口三态显隐、两个弹窗关键绑定、`RowHeight` NaN、计数 `Run` OneWay、`App` 注册、网络数组子行按 `AddressConfig.Length` 显示 5 行）；
- `--global-variables-render` **16 PASS / 0 FAIL**（原有 9 张渲染 + 在线/离线副行；本批新增导出 / 导入两档、范围卡等宽与 3 条弹窗数据判据）；
- 详见 `docs\code-changes\2026-10-21-设备变量表批量导入导出.md`。
- **数字口径**：以上 `3150 / 19 组 / 54 / 16` 是**上一次全量 gate 的快照**；当前源码 `Run()` 已到 **23 组**（新增 `RunAreaOptionContract` / `RunBitOffsetRange` / `RunExplicitInitialUpdate` / `RunTypeChangeRollback`），冒烟另有 5→3 子行断言（P2-3）未进入 `54` 快照，待下次全量 gate 一并更新。

### 边界（v3.2）

1. 不支持通过 Excel 改名（改名只能删旧建新，或直接在变量管理里改）；
2. 复合地址串不回读——`地址预览` 只给人看，导入只认结构化列；
3. 协议专属列：导出时对不适用协议留空；导入时非空且不适用 → **明确报错**（站号 / 40001 仅 Modbus、DB 仅 S7、位偏移仅 bool 且 **MC / 线圈 / 离散输入不适用**、每元素字节仅 `string[]`）；
4. 结果页**没有独立渲染证据**（`--global-variables-render` 只到预览步）；
5. 「下载模板」只有表头 + 一行空白示例，不按当前设备预填；
6. 运行锁落在「应用」而不是入口按钮（服务层 `Apply` 另有同样一道）；
7. 删除缺失只提示影响面（前 3 个名字 + 总数），**不做流程 / 画面引用扫描**；
8. 大文件同步执行会卡 UI；导出不覆盖 `HImage` / `byte[]` 等网络不支持类型；服务层复用 VM 静态助手（层次取舍）；导入 / 导出无日志审计；组编辑器覆盖层在导入期间保持关闭（`Open` 会重载）；
9. 不支持在线切换 MC 厂商（计划级厂商差异检测不到），需删旧建新；系统变量撞名 / 缺「变量表」页 / 删除回滚 / 双击 Apply 等路径代码已改，但**暂无专属断言**。

## v4 网络数组写入重构（2026-10-21，实现驱动）

> 用户反馈：「写入数组设计不合理（50 项只能整组覆盖、没有改了什么的概念）」「数组工程量换算共用一个不太好」。
> 本版只动 `ArrayEditorView` 的**网络模式**与换算配置；本地数组编辑（v3）语义不变。
> 记录：`docs\code-changes\2026-10-21-数组写入重构与逐元素换算.md`。

| 位置 | v3 | v4 |
| --- | --- | --- |
| 写入范围（网络） | 无分段，确认即整组替换 | 分段「只写修改项 / 整组写入」；默认只写修改项 |
| 对照值 | 无 | 打开时快照 = 「原值」；改过的行显示「原值：…」+ 行内 ↺；底部「撤销全部修改」 |
| 第三列 | 「操作」列（网络模式下全空） | 网络模式改「换算」：`×0.1 +5 ℃` + 行内 Σ；本地仍是操作按钮 |
| 换算 | 数组不支持，地址级字段对数组也显示 | 逐元素档案（每个元素一份），行内 Σ / 底部整组换算；**仅数值数组**显示，字符串/布尔/byte[] 不给入口；添加变量草稿对数组给只读说明 |
| 预警 | 无 | 质量（Uncertain/Bad → 先刷新，订阅模型实时刷新）、整组写入超单请求上限（Modbus 120 寄存器 / S7 约 110 字节；只在整组模式显示，MC/FINS 给"拆变量"结论） |
| 执行 | 同步阻塞 | `Task.Run` + 忙碌态（列表/粘贴/撤销/刷新/换算/模式切换全部冻结）；逐项失败列出下标，成功项推进原值可重试 |
| 底部 | 「共 N 个元素」+「共 N 项」重复；确认叫「确定」 | 单一 `共 N 项 · 已改 M 项`；确认叫「写入修改项 / 整组写入」，只改换算时叫「保存换算」 |
| 行密度 | 固定 48px | 网络行 34px 起（改过的行展开「原值」子行）；`ListBox` 虚拟化 |
| 粘贴 | 无 | Ctrl+V / 底部剪贴板图标：每行一个元素，Excel 一列可直接粘；尾换行与空单元格跳过（网络模式，不静默清空设备值） |
| 尺寸 | 640 × 660（最小 540 × 540） | **640 × 680（最小 600 × 560）** |

**证据**：`%TEMP%\array-editor-render-K.png`（本地正常）、`array-editor-error-render-K2.png`（本地校验失败）、`array-editor-network-render-K3.png`（网络：只写修改项 / 质量预警 / 换算列 / 原值）。
**边界**：未做“跳到第 N 项”；多列 Excel 粘贴只取每行第一格；错误文案仍带转换器英文尾巴（既有）；MC/FINS 无元素地址 → 默认整组写入。

## 布局与尺寸

```text
窗口 1300 × 700（最小 1200 × 580）
┌──────────────────────────────────────────────────────────────────────┐
│ 标题栏 64：图标 36 + 标题/副标题 + 关闭 30                            │
├──────────────┬───────────────────────────────────────────────────────┤
│ 左栏 232     │ 右栏自适应（1300 下约 1024）                          │
│              │ ┌───────────────────────────────────────────────────┐ │
│ 变量来源     │ │ 卡片头 64：标题/数量/来源 chip/分组 chip + 搜索   │ │
│ 本地与系统   │ │           + 管理分组 + 添加变量                   │ │
│  本地变量 6  │ ├───────────────────────────────────────────────────┤ │
│  系统变量 8  │ │ 表头 34（变量名/类型/分组/扫描组/地址/当前值/操作）│ │
│ 通信连接     │ │ 根行 54 / 子行 40                                 │ │
│  PLC_Modbus  │ │ ...                                               │ │
│   全部    5  │ │                                                   │ │
│   监控分组 3 │ │                                                   │ │
│   控制分组 1 │ ├───────────────────────────────────────────────────┤ │
│   未分组   1 │ │ 状态条 40                                         │ │
│  PLC_S7_1    │ └───────────────────────────────────────────────────┘ │
│ 轮询镜像说明 │                                                       │
│ 采集质量图例 │                                                       │
└──────────────┴───────────────────────────────────────────────────────┘
```

外边距 16×2 + 左栏 232 + 间隔 12 = 276；最小窗口 1200 下右栏视口 **924**。

### WPF 列宽计划

| 列 | 宽度 | 说明 |
| --- | --- | --- |
| 变量名 | **`1.6*`（MinWidth 220）** | 2026-10-20 曾定稿 220 固定；**2026-10-28 改弹性**：固定值在宽窗口下把余量全让给「当前值」，系统变量描述行 8/8 被省略号截断。下限仍 220，1200 窄档不变；两行：名称 + 描述；子行缩进 22 |
| 类型 | **Auto（MinWidth 104 / MaxWidth 220）** | 中性 chip + 类型色点；**2026-10-28** 去掉 chip 的 `MaxWidth=92`（它把 `FlowRuntimeSnapshot` 截成 `FlowRun…`），列宽上限 220 = 该类型名整词宽度；短类型仍落在 104 |
| 分组 | **120（MinWidth 120）** | 仅网络来源根节点；本地/系统整列收起；单元格默认 Padding，内容 104（下拉 104 宽）。**v3.1 起本列替换原「来源」列**（列名 `GroupColumn`）；来源仍在卡片头 tag 与左栏 |
| 扫描组 | 104（MinWidth 104） | 仅网络来源；根节点下拉（单元格 Padding `6,0,0,0`，内容 98） |
| 地址 | 88（MinWidth 88） | 仅网络来源；等宽 |
| 当前值 | `*` MinWidth 160 | 星号列；单元格 Padding `8,0,0,0`（仅左内边距）；内容 152 = 质量点 6 + 间距 6 + 输入 104 + 间距 6 + ↺ 30 |
| 操作 | 100（MinWidth 100） | 单元格 `DialogDataGridCellFlush`（Padding 0）；三颗 30×30 + 间距 2×2 = 94 |

> **实施定稿（2026-10-20，用户确认）**：早期计划的 280/88/100 已废弃；按 1200 最小窗口实测，变量名 280→220、来源 100→120、类型 88→104、地址 84→88。以下合计按定稿值计算。
> **v3.1 变更（2026-10-21）**：第 3 列的**语义**从「来源」换成「分组」，**宽度与 MinWidth 都仍是 120**，所以下面两组合计一个数字都不用改；代码后置的收放列表也同步换成 `GroupColumn`。
> **v3.2 变更（2026-10-28）**：变量名 220 固定 → `1.6*`（下限 220）；类型 104 固定 → `Auto`（104~220）；行高由固定 `Height` 改 `MinHeight`（54/40），与 design.html 的 `min-height` 对齐——内容在 150% DPI / 字体回退下更高时行长高，不裁描述；名称与描述间距 1 → **3**（基线 1px 在 150% DPI 下两行贴死，用户按"挤压"反馈）。本地/系统来源按类型列 **MaxWidth** 计最坏 = 220 + 220 + 100 = **540**（+160 = 700 ≤ 924）；网络来源类型均为基元/数组（≤8 字符），Auto 不会超过 MinWidth，合计仍 **736**（+160 = 896 ≤ 924）。

本地/系统来源固定列（类型按 MaxWidth）= 220 + 220 + 100 = **540**；网络来源固定列（类型按 MinWidth）= 220 + 104 + 120 + 104 + 88 + 100 = **736**；
540 + 160 = 700 ≤ 924，736 + 160 = 896 ≤ 924。满足 `GlobalVariableChecks` 的列宽契约，
视口常数为 276（左栏 232 + 间距 12 + 外边距 16×2），本地/网络两档都要断言。

## 契约保留（一个都不能改）

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `DataContext = GlobalVariableManagerViewModel`（Prism `AutoWireViewModel` + 宿主 `RegisterDialog`） | 换独立 VM = 命令、对话框生命周期、宿主注册全部要重接 |
| C2 | `SourceNodes` / `SelectedNode` / `SelectedSource` / `FilteredDisplayNodes` / `SearchText`；分组作用域 `SelectedGroupId` / `IsGroupScope` / `SelectedGroupName` | 来源 + 分组筛选与数组子行跟随父级的语义在 VM 里，视图只换外观。**v3.1 起左栏 `ListBox.SelectedItem` 绑 `SelectedNode`**（`SelectedSource` 退为派生属性：分组行返回父连接），两者不许并存两套选中真相 |
| C3 | `AddCommand` / `DeleteCommand` / `ResetCommand` / `EditArrayCommand` / `CopyNameCommand` / `RenameCommand` / **`OpenGroupManagerCommand` / `CloseGroupManagerCommand`** / `ToggleSourceExpandCommand` / `CloseCommand` / `ToggleNodeCommand` | 全部是功能入口；不允许出现死按钮，也不允许把入口藏掉。**`ManageScanGroupsCommand` 已下线**（扫描组编辑器只在通讯设置开）；「管理分组」按钮（`ManageVariableGroupsButton`）**常显**，不再按"是否网络来源"显隐 |
| C4 | `IsNetworkSource` 驱动「**分组** / 扫描组 / 地址」三列显隐；`GlobalVariableView.xaml.cs` 构造器直调 `HookViewModel()` | 2026-10-11「变量明细显示不全」事故的修法；只靠 `DataContextChanged` 会一次都不执行。v3.1 把第 1 列从 `SourceColumn` 换成 `GroupColumn`，收放判据与时机不变 |
| C5 | `CellValueEditBehavior` / `CurrentValueText` / `IsValueDeviated` / `IsValueReadOnly` / 数组元素行写回 | 当前值就地编辑的提交、Esc 放弃、编辑中暂停轮询、失败红框全部在这里 |
| C6 | `ScanGroupOptions` / `ScanGroupText` / `ShowScanGroup` / `ScanGroupChanged` | 扫描组是轮询节拍的唯一入口；改名/删组后要重注册。**变量管理只保留"给变量选组"这一处**，扫描组编辑器的入口不在本弹窗 |
| C7 | DataGrid 横向滚动 `Auto`；当前值列 `Width="*"` 且 `MinWidth ≥ 80`（本版 160） | 固定列吃光视口的守门；宁可横向滚动也不许星号列变 0 |
| C8 | 系统 / IO 变量只读：`IsSystemOwned` / `IsEditableNode` / `IsValueReadOnly` | 系统变量由宿主维护，删了会回来且会断引用；VM 侧拦截必须保留。操作列只隐藏「重命名 / 删除」，「复制变量名」仍保留（HMI 组态入口） |
| C9 | `AddCommand` 仍打开 `EasyDialog.ShowPropertyGridAsync`（本地 / 网络草稿） | 「添加变量」框架弹窗不在本批；主窗口只换入口外观 |
| C10 | `CloseCommand` + 自绘标题栏拖动 | 无边框窗口的唯一关闭/拖动入口 |

## 允许的 VM 改动（显示属性 + v3.1 显示分组 + v3.2 批量进出）

| 改动 | 理由 | 风险 |
| --- | --- | --- |
| `VariableSourceNode` 增加 `Subtitle`（计算属性）+ `ProtocolText`（显示名） | 三态公式固定：网络 = `{ProtocolText} · 在线/离线`；本地 = `内存变量 · 可编辑`；系统 = `宿主维护 · 只读`。`IsOnline` 变化时副行必须跟着变 | 纯显示属性，不参与筛选、持久化、命令；`IsOnline` setter 补 `RaisePropertyChanged(nameof(Subtitle))` |
| `SelectedSource` setter 忽略 null | 分组视图刷新/集合重建时 ListBox 可能把选中项写回 null，导致右栏错位 | 只拦 null，不改变合法选中项语义 |
| `IsNetworkSource` setter 去掉「切本地/网络」副作用 | 2026-10-15 分段胶囊撤掉后，它只由 `SelectedSource` 驱动；保留 else 分支会把「系统变量」选择强制改回「本地变量」 | 这是**行为修复**：网络 → 系统 → 网络必须可达；补运行时断言 |
| **v3.1**：`VariableSourceNode` 增加 `Level` / `ParentKey` / `GroupId` / `IsGroup` / `ShowExpand` / `IsExpanded` | 二级树需要"来源行 vs 分组行"的区分；仍是同一个 `ListBox`，不换 `TreeView` | 纯显示 + 筛选作用域；`Level=1` 的行不进"来源"语义（`SelectedSource` 返回父连接） |
| **v3.1**：新增 `SelectedNode`（显式 `nameof(SelectedNode)` 通知）+ `SelectedGroupId` / `IsGroupScope` / `SelectedGroupName` / `SelectedScopeLabel` / `VisibleRootCount` | 左栏 `ListBox` 要双向绑定选中项；分组作用域要驱动筛选、卡片头 tag 与状态条 | `SetProperty` 的 `[CallerMemberName]` 在 `ApplySelection` 里会报成 `ApplySelection`，**必须显式传属性名**，否则绑定收不到通知 |
| **v3.1**：新增 `GroupEditor`（`VariableGroupEditorViewModel`）+ `OpenGroupManagerCommand` / `CloseGroupManagerCommand`；删除 `ManageScanGroupsCommand` | 显示分组的增删改是方案级操作；扫描组编辑器按 §2.6 收敛到通讯设置 | `Changed` 事件必须同时订阅与退订；分组表变更后要重建节点（`GroupOptions` 是建节点时的快照） |
| **v3.1**：`CreateRootNode` 填 `GroupOptions` / `SelectedGroup`；`AddNetworkVariable` 写入 `GroupId` | 明细表下拉与"当前分组下新建直接归组" | 只在网络根节点填；本地/系统不参与（`ShowGroup=false`） |
| **v3.2**：`ExportCommand` / `ImportCommand` + `ExecuteExport` / `ExecuteImport` / `OnExportClosed` / `OnImportClosed` | 批量进出入口与关窗回执；导入是**即时落地**，关窗无论确认还是取消都要重建左栏与明细 | 命令只做"开弹窗 + 刷新"；差异计划与事务在 `DeviceVariableIoService` 与两个弹窗 VM 里。`IsNetworkSource` / `SelectedConnection` 的 setter 各补一次 `RaiseCanExecuteChanged` |

除此之外**不改 VM 的既有语义**：不加 `IsLoading`、不加排序/列状态、不改写值/删变量/数组编辑/持久化路径；
v3.1 对 `AddNetworkVariable` 的改动只有一处——新建时按 `_selectedGroupId` 写 `GroupId`，创建校验与地址构造一字未动；
v3.2 只在管理器上新增两个命令与两个关窗回执，上述路径同样一字未动。

## 令牌映射

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--gv-surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--gv-surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--gv-surface-3` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--gv-border` | `#E2E8F0` | `DialogBorderBrush` |
| `--gv-border-soft` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--gv-border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` |
| `--gv-text` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--gv-text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--gv-text-3` | `#94A3B8` | `DialogTextMutedBrush` |
| `--gv-text-4` | `#64748B` | `ScadaSecondaryTextBrush`（近似） |
| `--gv-primary` | `#0EA5E9` | `DialogAccentBrush` |
| `--gv-primary-strong` | `#0284C7` | `DialogAccentDarkBrush` |
| `--gv-primary-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--gv-primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--gv-primary-light` | `#E0F2FE` | `DialogAccentLightBrush` |
| `--gv-primary-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--gv-success*` | `#22C55E / #15803D / #F0FDF4 / #BBF7D0` | `DialogSuccessBrush` / `DialogSuccessTextBrush` / `DialogSuccessSubtleBrush` / `DialogSuccessBorderBrush` |
| `--gv-danger*` | `#EF4444 / #B91C1C / #FEF2F2 / #FECACA` | `DialogDangerBrush` / `DialogDangerTextBrush` / `DialogDangerSubtleBrush` / `DialogDangerBorderBrush` |
| `--gv-warning*` | `#F59E0B / #B45309 / #FFF4E5 / #FDE68A` | `DialogWarningBrush` / `DialogWarningTextBrush` / `DialogWarningSubtleBrush` / `DialogWarningBorderBrush` |
| `--gv-violet*` | `#8B5CF6 / #6D28D9 / #F5F3FF` | `LogicCaseBrush` / `LogicCaseTextBrush` / `LogicCaseSoftBrush` |
| `--gv-cyan*` | `#06B6D4 / #0E7490 / #ECFEFF` | **新增** `DialogTypeArrayBrush` / `DialogTypeArrayTextBrush` / `DialogTypeArraySoftBrush` |
| `--gv-slate*` | `#64748B / #475569 / #F1F5F9` | `DialogTextSecondaryBrush` / `DialogTextSecondaryBrush` / `DialogRowHoverBrush` |
| 圆角 6/8/10/12/16 | — | WPF `CornerRadius` 字面量（仓内暂无圆角令牌） |
| 间距 4/8/12/16 | — | WPF `Margin` / `Padding` 字面量 |
| 字体 | Segoe UI / Microsoft YaHei UI | 同族系统字体；等宽用 Cascadia Mono / Consolas |

**只新增 3 个颜色键**（`Colors.xaml`，追加不改既有键）：`DialogTypeArrayBrush` / `DialogTypeArrayTextBrush` / `DialogTypeArraySoftBrush`。
类型色其余五档全部复用既有语义族，不新造第二套蓝/灰。

## 设计块 → 现有样式键（R141/R117 对照）

| 设计块 | 复用键 | 说明 |
| --- | --- | --- |
| 窗口 / 卡片 | `DialogCardShadow` / `DialogCardBorder` / `DialogCardHeader` | 卡片头局部覆盖 `Height=60`（设计 64，实施按一屏行数取舍），不改共享键 |
| 标题 / 图标 | `DialogCardTitle` / `DialogCardTitleIcon` | 右栏卡片头保留标题图标（与设计稿的差异记入已知偏差） |
| 搜索 | `DialogSearchChrome` / `DialogSearchInput` | 宽度 238，高度 32 |
| 主按钮 / 图标按钮 | `DialogPrimaryButton` / `DialogIconButton` | 图标按钮 30×30 + `Padding=0`（R68） |
| 表格 / 行 / 表头 / 单元格 | `DialogDataGrid` / `DialogDataGridRow` / `DialogDataGridColumnHeader` / `DialogDataGridCell` / `DialogDataGridCellFlush` | 视图内仅用无 `x:Key` 的内联 Style 覆盖尺寸；不改共享键 |
| 胶囊 / 标签 | `DialogChip*` / `DialogTag` / `DialogTagText` | 类型 chip、来源 badge、只读 chip、数量徽标 |
| 输入 / 下拉 | `DialogCellInputQuiet` / `DialogCombo` | 当前值输入、扫描组下拉 |
| 焦点 / ToolTip | `DialogFocusVisual` / `DialogToolTipStyle` | 共享按钮/输入样式已带焦点；所有 ToolTip **显式**写 `<ToolTip Style="{StaticResource DialogToolTipStyle}" Content="…"/>`（不依赖视图内隐式 Style） |
| 空态 / 状态条 | 视图内联（无共享键） | 仅本弹窗使用；不新增共享键 |

**样式纪律**：`GlobalVariableView.xaml` 属于 `CollectedDialogViews`，**禁止本地 `<Style x:Key=...>`**（`VariableBindingCheck.RunCollectedViewStyleContract`）；本视图只用无 `x:Key` 的内联 Style、`DataTemplate`、`ControlTemplate`。共享字典只追加 `Colors.xaml` 的 3 个 `DialogTypeArray*` 键。

## WPF 复刻映射（第 ③ 步计划）

| 设计项 | WPF 实现 |
| --- | --- |
| 左栏来源导航 | `ListBox` + 视图内 `DataTemplate`（不改 `Dialog.Misc.xaml` 的共享 `DialogSourceNodeTemplate`）；**v3.1 起** `ItemTemplateSelector="{StaticResource GvSourceNodeSelector}"`（来源卡片 / 分组行两套模板）、`SelectedItem="{Binding SelectedNode, Mode=TwoWay}"`、`IsSynchronizedWithCurrentItem=False`；`ItemsSource` 由 code-behind 设为分组视图（幂等，不重复加 `GroupDescriptions`）；`ItemContainerStyle` 内联模板 + `DialogFocusVisual`，DataTemplate 触发器覆盖 Hover/Selected/KeyboardFocus/Disabled 五态 |
| 来源分组标题 | `CollectionViewSource` 按既有 `IsNetwork` 分组（false = 本地与系统，true = 通信连接），`GroupStyle.HeaderTemplate` 用 `DataTrigger` 出中文组标题；不新增分组字段。分组行 `IsNetwork=true`，所以自动落在「通信连接」这一节里，跟随父连接折叠 |
| 左栏二级分组行 | `GvGroupNodeTemplate`：30px、左边框缩进 26px、图标 + 名称 + 计数；选中态主色浅底 + 主色文字 + SemiBold；`Count=0` 时整行 `Opacity=0.65` 但仍可点；连接行的展开箭头用 `ToggleSourceExpandCommand`（`ShowExpand` 只对连接行为真） |
| 右栏卡片头 | `Grid` 两行：标题/徽标/来源 chip/分组 chip + 副标题；搜索、**管理分组**、添加变量右对齐（管理分组常显） |
| 分组列（v3.1） | `DataGridTemplateColumn x:Name="GroupColumn"` 120/120；单元格 `ComboBox`（`DisplayMemberPath="Name"`、`ItemsSource="{Binding GroupOptions}"`、`SelectedItem="{Binding SelectedGroup, Mode=TwoWay}"`、`Visibility="{Binding ShowGroup}"`）；替换原「来源」列 |
| 管理分组覆盖层（v3.1） | 弹窗根 `Grid` 的 `GroupEditorOverlay`（`Panel.ZIndex=20` + `DialogScrimBrush` 遮罩 + 580×560 卡片）；`Visibility="{Binding GroupEditor.IsOpen}"`；列表 `ItemsControl` 绑 `GroupEditor.Rows`，行内改名 / 删除确认 / 新增输入都绑 `GroupEditor.*` 命令（`CommandParameter="{Binding}"`） |
| 两行式变量名 | `DataGridTemplateColumn` + `Grid`（名称 / 描述）；描述为空时第二行 `Collapsed`；数组项数绑 `Children.Count` |
| 类型 chip + 色点 | `DialogTag` + `Ellipse`；内联 `Style.Triggers` 按 `TypeName` 切换 `DialogAccent*` / `LogicCase*` / `DialogWarning*` / `DialogSuccess*` / `DialogTypeArray*` / `DialogTextMuted*`；类型缩写走 `TypeAbbreviationConverter` |
| 数组展开 | 保留 `ToggleNodeCommand` + `IsExpanded` + `LevelToIndentConverter`；`ToggleButton.IsChecked="{Binding IsExpanded, Mode=OneWay}"` + `Command="{Binding DataContext.ToggleNodeCommand, RelativeSource={RelativeSource AncestorType=DataGrid}}"` + `CommandParameter="{Binding}"`（避免双翻转；DataContext 是 VariableNode，必须走 DataGrid 的 DataContext），`Padding=0`、16×16；`DataGrid.RowHeight="{x:Static sys:Double.NaN}"` + `MinRowHeight="40"`；内联 `DataGrid.RowStyle BasedOn=DialogDataGridRow`，默认 `Height=54`，`IsChildNode=True` → `Height=40`；`AlternationCount=1` |
| 表头 | 内联 `DataGrid.ColumnHeaderStyle BasedOn=DialogDataGridColumnHeader`：`Height=34`、`FontSize=11`；不改共享字典 |
| 当前值 | 一个 `Grid` + `GlobalVariableValueModeConverter` 的 `MultiBinding`，按优先级 `IsSystemOwned > HImage > IsValueReadOnly > (IsArrayRootNode && OriginalModel is LocalVariableModel) > IsChildNode > (IsEditableNode && !IsValueReadOnly)` 分六态；**网络可写标量也走可编辑态**；可编辑根保留 `CellValueEditBehavior` + `DialogCellInputQuiet` + 质量点 + ↺；只读根显示锁 + `CurrentValueText`；图像显示「图像由流程写入」；数组根仅本地变量显示「强制覆盖集合...」（**v2 已改为「摘要 + 编辑数组… + ↺」**） |
| 系统只读 | `IsSystemOwned` 触发：操作列保留复制 + 「只读」chip（Border），重命名/删除单独 Collapsed；当前值显示锁 + 值；顶部横幅按 `SelectedSource.Key == System` |
| 空态 / 搜索无结果 | DataGrid 上层两个 `Grid` 覆盖（`Margin="0,34,0,0"` 让表头保持可见）；`MultiBinding` + `EmptyStateVisibilityConverter` 输入 `FilteredDisplayNodes.Count` 与 `SearchText`，分别返回空来源/搜索无结果两态 |
| 状态条 | 左侧 `VisibleRootCount` + `SelectedScopeLabel`（v3.1 起；搜索时标签为「全部分组」）；右侧静态键盘提示 |
| 列收放 | 保留 `ApplyDetailColumnMode()` / `HookViewModel()`；**v3.1 起三列为 `GroupColumn` / `ScanGroupColumn` / `AddressColumn`**（原 `SourceColumn` 已移除），本地/系统整列收起 |
| 标题栏 | 保留 `WindowDragBehavior` + `CloseCommand`；高度 52 → 64 |

## 验证计划（WPF 复刻时执行）

| 层 | 判据 |
| --- | --- |
| 构建 | `dotnet build VisionMaster.sln` 0 错误；**再显式** `dotnet build UIThemeSmokeTest\UIThemeSmokeTest.csproj`（它不在 sln 里，R65） |
| 静态 | `FlowCanvasChecks\GlobalVariableChecks` 扩展：视图 XAML `hex=0`（剥注释）；关键绑定齐全；列宽契约（定稿 220/104/120/104/88/*160/100，本地 424+160、网络 736+160、视口 924、逐列 `MinWidth` 锚定）；`HookViewModel()` 双路调用；`TypeAbbreviationConverter`；空态/系统横幅/来源模板；`VariableBindingCheck` 无 keyed Style |
| 运行时 | `UIThemeSmokeTest --global-variables`：真实 `GlobalVariableManagerViewModel` + 真实视图（构造后 `view.DataContext = vm`，确保 `DataContextChanged → HookViewModel`）；断言 DataContext、分组数、双向选中、列显隐、系统横幅、系统行复制/只读、当前值六态、行高 54/40、空态/搜索无结果、命令绑定；渲染前显式设置网络节点 `IsOnline=true`，断言在线/离线副行不同 |
| 渲染 | `UIThemeSmokeTest --global-variables-render` 或 `FlowCanvasChecks --render` 场景 I：本地 / 网络在线 / 网络离线 / 系统 / 空 / 搜索无结果 / 写入失败 / 管理分组覆盖层 / 1200 窄档，至少 9 张 PNG，存 `evidence/after-render-v1-*.png`；写入失败态用 `Validation.MarkInvalid` 模拟真实提交失败红框；改前截图存 `before-render-v1-local.png`；**v3.2 起追加导出 / 导入两个弹窗档**（共 11 张、15 条判据） |
| 回归 | `FlowCanvasChecks` 0 失败；默认 `UIThemeSmokeTest`（含 `RunHardcodedColorChecks`）与新 `--global-variables` 段全 PASS；以日志明细为准（R46/R65） |
| 资源 | `FlowCanvasChecks` 资源断链契约 + `ResProbe` `RESULT: ALL_OK`（新增 3 个颜色键后必须跑）；`hardcoded-colors.baseline.txt` 删除 `GlobalVariableView.xaml|4`；视图 hex 清零同一批次删除 `VariableBindingCheck.cs` 的 `AllowedColorLiterals["GlobalVariableView.xaml"]` 白名单条目 |
| 变异反证 | 至少 2 条：① 删 `PropertyGroupDescription` → 分组断言红；② 摘掉系统行复制按钮 → 系统行断言红；还原后复绿 |
| 真机 | 打开 → 切本地/系统/连接 → 搜索 → 改当前值（成功/失败）→ ↺ → 数组展开/覆盖 → 添加 → 重命名 → 删除 → 管理分组（覆盖层新增/改名/删除）→ 关闭；扫描组编辑改在「通讯设置 → 连接 → 操作 → 扫描组」验证 |

## 已知偏差（WPF 复刻时按 R130 记录）

1. **图标**：Web 用 Lucide 描边 SVG；WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`），形状不同、语义一致。
2. **字体度量**：同字号下 Segoe UI / 微软雅黑与 Web 的字宽、行高略有差异；不按 px 硬套。
3. **滚动条**：WPF 约占 17px，窄档可用宽度比 Web 少约 17px；网络来源允许横向滚动。
4. **DataGrid 选择态**：Web 用浅蓝行底 + 2px 左条；WPF 走 `DialogDataGridRow` 的选中态，不另造一套。
5. **质量点颜色**：现有视图硬编码 `#FF67C23A / #FFE6A23C / #FFC0C4CC`，复刻改为 `DialogSuccessBrush` / `DialogWarningBrush` / `DialogTextMutedBrush`（同语义、略有色差）。
6. **类型色**：Web 的紫色/青色分别映射到 `LogicCase*` / 新增 `DialogTypeArray*`，不是新造第二套数据色。
7. **行高**：Web 根行 54 / 子行 40；WPF 受 DataGrid 单元格 Padding 影响可能有 1-2px 差异。
8. **空态覆盖层**：WPF 用 DataGrid 上层 Grid 覆盖，表头仍可见（与设计稿一致）；不是替换整个 DataGrid。
9. **「添加变量」弹窗**：仍为框架属性网格，外观不在本批；主窗口只保留入口。
10. **状态条计数**：v3.1 起用 `VisibleRootCount`（当前作用域可见根行数）+ `SelectedScopeLabel`；搜索时标签为「全部分组」，不再是来源总数。
11. **控件高度**：数据密集型弹窗使用 32px 头部按钮 / 30px 行内输入，不套用插件表单的 36px；这是为了一屏多看几行，属于有意取舍。
12. **窗口圆角 / 阴影**：Web 是 16px 圆角 + 多层阴影；WPF 无边框窗口的圆角由宿主决定，内容卡片保留 12px 圆角，不强行做透明窗口圆角。
13. **1200 窄档网络来源**：定稿固定列 736 + 星号 160 = 896，理论上 ≤ 905 有效视口；实测 DataGrid 的边框/竖向滚动条/单元格边距仍会额外占用，截图中出现横向滚动条。星号列仍保 160 下限，不会变 0；设计稿允许横向滚动，真机需确认可接受。
14. **行高**：`DialogDataGridRow` 的 `Margin` 已在视图内覆盖为 0，根行实测 54、子行 40，与设计一致。
15. **类型 chip 描边**：`DialogTag` 有 1px 描边 + 圆角 4，设计稿是无描边圆角 6；统一走共享 Tag 键，不另造一套。
16. **卡片头标题图标**：WPF 保留 `DialogCardTitleIcon`（设计稿右栏卡片头没有图标），属信息密度差异。
17. **空态组头**：WPF 分组视图在没有通信连接时不会生成空的「通信连接」组头；设计稿固定显示两组。无连接时只显示「本地与系统」组，属正常分组行为。
18. **DPI / 焦点 / 无障碍**：本批按 96 DPI 渲染证据；125%/150% 与高对比度、`AutomationProperties` 需真机复核，不在本批自动化范围。
19. **卡片头 / 状态条高度**：设计稿为 64 / 40，实施为 60 / 34（为 1300×700 下多留一行明细）；视觉差 4 / 6px，属有意取舍，已在渲染证据中体现。

## v1 明确延后（需新增功能或契约）

| # | 项 | 需要什么 |
| --- | --- | --- |
| D1 | 「添加变量」弹窗改版 | 新的弹窗视图 + 草稿渲染契约；当前是 `EasyDialog.ShowPropertyGridAsync` |
| D2 | 列选择 / 密度切换 / 排序 | VM 新增列状态与排序命令 |
| D3 | 多选批量删除 | VM 新增选中集合与批量命令 |
| D4 | 变量详情检查器 | VM 新增 `SelectedNode` 与详情字段 |
| D5 | 导入 / 导出变量表 | 新的文件契约与校验 |
| D6 | 历史趋势 / 迷你曲线 | 按变量采样与环形缓冲 |
| D7 | 来源节点右键菜单（刷新连接 / 查看连接） | 新的宿主导航契约 |

> D5（导入 / 导出变量表）已于 2026-10-21 落地，见本文「v3.2 批量导入 / 导出」。

## 证据清单

| 文件 | 状态 |
| --- | --- |
| `evidence\design-v1-overview.png` | 整页总览（产品窗口 + 关键改动 + 契约保留 + 延后清单） |
| `evidence\design-v1-local.png` | 本地来源（默认态，含数组展开、图像只读、偏离 ↺） |
| `evidence\design-v1-network.png` | 网络来源（在线连接，含扫描组、地址、质量三态） |
| `evidence\design-v1-network-offline.png` | 网络来源（离线连接：红徽标 + 灰质量点 + 上次镜像值） |
| `evidence\design-v1-system.png` | 系统来源（只读横幅、只读 chip） |
| `evidence\design-v1-empty.png` | 空来源（0 个变量 + 添加变量） |
| `evidence\design-v1-search-empty.png` | 搜索无结果（`XYZ`） |
| `evidence\design-v1-write-error.png` | 网络写值失败（红框） |
| `evidence\design-v1-narrow.png` | 1200 窄档（网络来源） |

### WPF 复刻证据（2026-10-20）

| 文件 | 说明 |
| --- | --- |
| `evidence\before-render-v1-local.png` | 改前旧两栏布局（2026-10-15 渲染探针） |
| `evidence\after-render-v1-local.png` | 本地来源（真实 VM；6 变量 + 数组展开 + 图像只读） |
| `evidence\after-render-v1-network.png` | 网络在线（5 变量，质量三态/扫描组/地址） |
| `evidence\after-render-v1-network-offline.png` | 网络离线（3 变量，红徽标 + 灰质量点） |
| `evidence\after-render-v1-system.png` | 系统来源（只读横幅 + 只读 chip + 锁值） |
| `evidence\after-render-v1-empty.png` | 空来源（0 个变量 + 添加变量） |
| `evidence\after-render-v1-search.png` | 搜索无结果 |
| `evidence\after-render-v1-write-error.png` | 写值失败红框（Validation.MarkInvalid 模拟） |
| `evidence\after-render-v1-group-editor.png` | 管理分组覆盖层（网络来源 + 打开覆盖层） |
| `evidence\after-render-v1-narrow.png` | 1200 窄档（横向滚动） |

> v3.2 新增的 `after-render-v1-export.png` / `after-render-v1-import.png` 见下文「设计 / WPF 证据（v3.2 批量导入 / 导出，2026-10-21）」。

### 设计 / WPF 证据（v2，2026-10-21）

| 文件 | 说明 |
| --- | --- |
| `evidence\design-v2-local.png` | 设计稿：数组摘要 + 编辑数组… + 根 / 元素 ↺ |
| `evidence\design-v2-array-dialog.png` | 设计稿：数组编辑弹窗（正常态，基准 0 项提示） |
| `evidence\design-v2-array-error.png` | 设计稿：校验失败（出错行红框 + 错误条） |
| `evidence\after-render-v2-array-cell.png` | WPF：变量管理（数组根摘要 + 编辑数组…） |
| `evidence\after-render-v2-array-editor.png` | WPF：数组编辑弹窗（正常态） |
| `evidence\after-render-v2-array-error.png` | WPF：数组编辑弹窗（校验失败） |

验证结论：`FlowCanvasChecks` **2754 通过 / 0 失败**；`UIThemeSmokeTest --global-variables` **全部通过**；默认 `UIThemeSmokeTest` 颜色令牌 **无新增硬编码颜色**；`_ResProbe` **ALL_OK**。详见 `docs\code-changes\2026-10-20-变量管理GlobalVariableView Web化复刻.md`。

**v2 验证结论（2026-10-21）**：`FlowCanvasChecks` **3027 通过 / 0 失败**（此前一次运行的 7 项失败为 Excel 文件占用 / 调度器节拍 / 并行取消 Warn 等环境性抖动，复跑与独立审查复跑均全绿）；`UIThemeSmokeTest --global-variables` **全部通过**（含新增的数组根摘要 / 编辑数组… / 元素级 ↺ 三条）；渲染场景 I / K / K2 出图并逐张核对。详见 `docs\code-changes\2026-10-21-变量管理数组编辑v2（当前值与基准分离+数组编辑弹窗）.md`。

### 设计 / WPF 证据（v3，2026-10-21）

| 文件 | 说明 |
| --- | --- |
| `evidence\design-v3-dialog.png` | 设计稿：弹窗正常态（分段写入范围、行 [2] 基准 33 + 行内 ↺、1 项已修改） |
| `evidence\design-v3-dialog-both.png` | 设计稿：切到「同时写初始值」后的后果说明 |
| `evidence\design-v3-error.png` | 设计稿：校验失败（行内红框 + 基准行 + 错误条） |
| `evidence\after-render-v3-array-editor.png` | WPF：弹窗正常态 |
| `evidence\after-render-v3-array-error.png` | WPF：弹窗校验失败 |

### 设计 / WPF 证据（v3.2 批量导入 / 导出，2026-10-21）

| 文件 | 说明 |
| --- | --- |
| `evidence\after-render-v1-export.png` | WPF：导出弹窗（默认选中「全部变量」，全部 3 / 当前分组 2；汇总「监控分组 2 / 未分组 1」） |
| `evidence\after-render-v1-import.png` | WPF：导入弹窗预览步（新增 1 / 更新 1 / 无变化 2 / 错误 1；四类计数、差异表、三个选项、错误行说明） |

**v3.2 验证结论（2026-10-21）**：`FlowCanvasChecks` **3150 通过 / 0 失败**；`UIThemeSmokeTest --global-variables` **54 PASS / 0 FAIL**；`--global-variables-render` **16 PASS / 0 FAIL**。详见 `docs\code-changes\2026-10-21-设备变量表批量导入导出.md`。

## 变更规则

设计基线确认后只读。需要调整时升版本（v2），并同步更新：
1. 本目录 `README.md` 的版本/日期/确认人；
2. `design.html` 的布局、令牌与状态；
3. 已复刻的 `GlobalVariableView.xaml` 与令牌映射表；
4. `docs\UI设计基线\README.md` 的基线索引；
5. `docs\code-changes\` 的复刻记录。
