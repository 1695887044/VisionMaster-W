# SCADA 状态列表 + `Hmi.StateView` · 设计基线 v1

| 项 | 内容 |
| --- | --- |
| 版本 | **v1（待确认）** |
| 日期 | 2026-10-24 |
| 范围 | P1-1：方案级共享状态列表 + 一个 `Hmi.StateView`（文本 / 图片 / 图文），覆盖图形开关、图形 IO 域、字指示灯、符号 IO 域 |
| 设计文件 | `design.html`（自包含、可操作演示；深色/浅色切换） |
| 证据 | `evidence/01-runtime.png`、`evidence/02-manager.png`（本机浏览器截图） |
| 状态 | **P1-1 已复刻（2026-10-24）**：共享文本列表 + `Hmi.ComboBox` 可选 `StateListId`；`StateView`/其它消费方 P1-2 |

## 〇、范围修订（2026-10-24，用户口径）

**P1-1 只做“共享文本列表 + `Hmi.ComboBox` 接入”。**

- 只有下拉框真正需要共享文本列表：它同时承担“只读文字 / 只读下拉 / 读写下拉”三种形态，且同一组选项常被多页、多控件复用；现在 `Options` 是每图元一份，改一处漏九处。
- **`Button` 不接列表**：按钮标题每个都不一样，继续手填 `Text`；为每个按钮建列表是把“填一个词”变成“建一张表”。
- `SelectorSwitch` / `BitButton` / `Lamp` / `Indicator` / `DataGrid` 列映射：**P1-1 不做**，它们现有的 `OptionNText` / `OnText/OffText` / 静态 `Text` 够用；等出现“同一组状态在 ≥2 个控件/页面复用”的真实需求再接。
- `Hmi.StateView`（图形开关 / 图形 IO 域 / 字指示灯）：**P1-2**。它需要图片、位号、范围，成本高；等第二个真实消费方出现再做，避免为一张列表提前造一个 TypeKey。
- 本稿的 `design.html` 展示的是**完整目标形态**（含 StateView/图片/范围），P1-1 实现只取其中的“ComboBox + 文本条目”部分。

**P1-1 列表模型只保留最小集**：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `StateListId` | Guid | 稳定身份；ComboBox 存它 |
| `Name` | string | 显示名；改名不打断引用 |
| `Entries` | list | `Value`（int，写入值）+ `Text`（显示文字） |
| `DefaultText` | string | 无匹配时的文字；默认 `—` |

> 不建 `Mode`（位/位号/范围）、不存图片、不做 `EndValue`。这些随 P1-2 的 StateView 一起加；加的时候只追加字段，不改 `Value/Text` 的语义。

**P1-1 ComboBox 口径**：

- `StateListId` 为空 → 继续走现有 `Options`（老工程零改动）。
- `StateListId` 非空 → 条目成为下拉候选；`Value` 按 `Value` 精确匹配，选中写 `Entry.Value`。
- `Mode=Output` + `Presentation=Text` → 只读文字；`Mode=InputOutput` + `Presentation=Field` → 读写下拉。
- 列表缺失 → 显示“未找到文本列表”，不静默空白；无匹配 → `DefaultText`。
- 管理器只做列表增删改 + 条目（值/文字）增删改；删除前列引用数。

> 结论：P1-1 从 L 降到 **S/M**，只碰“列表模型 + ComboBox + 管理器 + Web ComboBox 渲染 + 断言”；`StateView` 与其它消费方全部后置。



## 一、为什么是“一个控件 + 一份列表”

手册第 8 章把“文本列表 / 图形列表”做成方案级资源，7.3.3 的符号 IO 域、字按钮、位按钮、文本开关、位指示灯、字指示灯、图形开关、图形 IO 域都引用它。当前实现里 `ComboBox.Options` 和 Lamp 的四色都是**每图元一份**，同一套“设备状态”在十个图元上要配十份，改一处漏九处。

因此本批不新增“图形开关 / 图形 IO 域 / 字指示灯”三个 TypeKey，而是：

- 领域层：`ScadaDocument.StateLists`（方案级、稳定 `StateListId`）。
- 控件层：一个 `Hmi.StateView`，用 `Presentation`（Text/Image/Both）+ 列表 `Mode`（位/位号/范围）组合出上述控件的能力。
- 编辑层：一个状态列表管理器（列表 + 条目表），属性面板行内“管理…”入口。

## 二、R111 六项

| 项 | 本稿口径 |
| --- | --- |
| 布局 | 运行态三卡并排（设备状态 / 检测结果 / 工位模式）；管理器左列表 220px + 右条目表；属性说明右栏 360~420px |
| 间距 | 卡片内 14~18px，卡片间 14px，表格行 30px；控件高度 22/28/34 三档 |
| 配色令牌 | 全部复用 `ScadaTokens.xaml` 的 `Hmi*`；不新增 hex（见 §五映射表） |
| 字号字重 | 标题 13/17/22；正文 11/12；数值与 ID 用等宽；状态文字 17~22、700 |
| 五态 | 正常 / 悬停 / 按下 / 焦点 / 禁用；另加“匹配 / 回退 / 列表缺失 / 图片缺失 / 绑定丢失”五种数据态 |
| 交互 | 改值/改条目即时预览；模式切换 150ms；删除列表二次确认；图片选择走方案资产选择器 |

## 三、运行态口径

| 场景 | 列表模式 | 呈现 | 说明 |
| --- | --- | --- | --- |
| 设备状态 | 范围 | 文本 | 0 停止 / 1 待机 / 2 运行 / 3 报警 / 4 维护；5 回退 |
| 检测结果 | 位（0/1） | 图文 | 0 NG（红叉）/ 1 OK（绿勾） |
| 工位模式 | 位号（0~31） | 图文 | 1 上料 / 2 检测 / 4 下料；3（两位同时）回退 |

### 匹配规则

- **位**：变量值精确等于条目 `Value`（0/1）。
- **位号**：变量值必须**恰好一位为 1**（`value == 1 << bit`）；两位同时置位按“未匹配”处理，不猜最低位。
- **范围**：`Value <= 变量值 <= EndValue`，按条目定义顺序取第一个命中；重叠只提示，不自动合并。
- **无匹配**：显示 `DefaultText`（默认 `—`），图片位显示占位框；**不保留上一条的图**。
- **列表缺失**：显示“未找到状态列表”，ToolTip 带 `StateListId` 与名字。
- **图片缺失**：文字照常显示，图片位虚线占位 + “图片缺失”。
- **绑定丢失**：保留最后一次有效匹配的文本/图片，右上角橙点 + ToolTip 原因（与 IOField/ComboBox 同一口径）。

## 四、属性草案

| 属性 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `StateListId` | StateList | 空 | 方案级列表；选择器 + 行内“管理…” |
| `StateListName` | Text（只读） | 空 | 显示名/旧数据兜底；不参与运行匹配 |
| `Value` | Number | 0 | 可绑定变量；位/位号/范围都按它匹配 |
| `Presentation` | Choice | Text | Text / Image / Both |
| `DefaultText` | Text | `—` | 无匹配/空列表时的文字 |
| `ImageStretch` | Choice | Uniform | Uniform / UniformToFill / Fill / None |
| `TextAlignment` / `FontSize` / `Foreground` | 基类 | 继承 | 复用现有文字属性，不另造 |

> 图片只存方案 `Image\` 下的相对路径；复用 `SolutionAssetService` 的复制/查重/缺失回退。

### 4.1 先分清“静态文案”和“状态映射”

文本列表只解决**同一个变量的一组状态 → 不同文字/图片**，不是用来存按钮标题的。每个按钮的文字本来就不一样，必须继续手填；为每个按钮建一张列表，是把“填一个词”变成“建一张表”，维护成本反而更高。

**静态文案（手填，不接列表）**

- `Hmi.Button.Text`、`Hmi.Text`、`Hmi.TextField`、`Hmi.IOField` 的标签、页面标题、配方名、轴名等。
- 每个按钮的文字独立，永远不建列表。

**状态映射（可选接列表，按需）**

| 控件 | 什么时候用列表 | 什么时候继续手填/用现有属性 |
| --- | --- | --- |
| `Hmi.ComboBox` | 同一组选项被多页/多控件复用；或需要“中文显示名 ↔ 写入值”分离 | 一次性选项继续用 `Options`（`0=手动;1=自动`） |
| `Hmi.SelectorSwitch` | 档位文字是共享状态集，或档位超过现有 4 档 | 2~4 档且文案只此一处，继续用 `Option0Text..3` |
| `Hmi.BitButton` | 两个状态文字在多个按钮/页面复用 | 单个按钮的 ON/OFF 文案，继续用 `OnText/OffText` |
| `Hmi.Lamp` / `Hmi.Indicator` | 灯旁文字随状态变，且状态集共享 | 静态标签，继续用基类 `Text` |
| `Hmi.StateView` | **必须**用列表（它的存在就是状态映射） | — |
| `Hmi.DataGrid` 列 | 代码列要显示成文字（P2） | 普通数值/文本列不用 |
| `Hmi.Button` | **不接** | 按钮标题永远手填；需要状态文案用 `BitButton`（两态）或 `StateView` |
| `Hmi.IOField` / `Hmi.TextField` | 不接 | 数值输入/静态文本保持原语义 |

**决策规则（三问）**

1. 这段文字会随变量变吗？**不会 → 手填，不建列表。**
2. 只有 2 个状态，且只在这个控件用？**→ 用现有 `OnText/OffText` / `OptionNText`，不建列表。**
3. 同一组状态在 **≥2 个控件/页面**出现，或状态数 **>4**？**→ 建列表，控件引用 `StateListId`。**

> 同一个列表可以被多个控件以不同呈现方式引用：总览页用 `ComboBox(Output+Text)` 只看文字，工程师页用 `ComboBox(InputOutput+Field)` 可改，大屏用 `StateView` 显示图文。**但按钮的静态标题不在此列。**

### 4.2 写入语义

写控件只有三类：`ComboBox(Field+InputOutput)`、`SelectorSwitch`、`BitButton`；普通 `Button` 通过动作表写值，不需要列表。

| 列表模式 | 读 | 写 | 说明 |
| --- | --- | --- | --- |
| 位（0/1） | `value == 条目.Value` | 写 `条目.Value`（0/1） | 最常用；文本开关、位按钮 |
| 位号（0~31） | `value == 1 << 条目.Value` | 写 `1 << 条目.Value` | 条目.Value 是位号，不是位掩码 |
| 范围（start~end） | `start <= value <= end` | **仅当 `start == end` 可写**，写 `start` | 真正的区间（0~60）只读；离散值建议用「范围 0-0/1-1」或新增离散值模式 |
| 离散值（Word，建议新增） | `value == 条目.Value` | 写 `条目.Value` | 0=停止/1=运行/2=故障这类任意整数；见 Q6 |

写控件选到**只读列表**（真区间）时，属性面板应拒绝或明确提示“该列表仅用于显示”，不能运行到一半才报错。

### 4.3 共享列表的删除与引用

- 管理器删除列表前必须显示**引用数**（多少控件/页面在用）；有引用时二次确认，删除后引用控件进入“列表缺失”态。
- 列表改名不影响引用（控件存 `StateListId`）；`StateListName` 只做显示与旧数据兜底。
- 列表条目的增删改要通知所有引用控件刷新（设计期）；运行态列表视为静态配置。

## 五、令牌映射（Web → WPF）

| 用途 | Web 变量 | WPF 资源 |
| --- | --- | --- |
| 卡片底 | `--surface` | `HmiSurfaceBrush` |
| 标题/工具栏底 | `--raised` | `HmiSurfaceRaisedBrush` |
| 描边 | `--border` | `HmiBorderBrush` |
| 强描边/占位 | `--border2` | `HmiBorderStrongBrush` |
| 主文字 | `--text` | `HmiTextBrush` |
| 次文字 | `--text2` | `HmiTextSecondaryBrush` |
| 弱文字 | `--muted` | `HmiTextMutedBrush` |
| 交互/焦点 | `--accent` | `HmiAccentBrush` |
| OK/匹配 | `--success` | `HmiSuccessBrush` |
| 回退/丢失 | `--warning` | `HmiWarningBrush` |
| 配置错误 | `--danger` | `HmiDangerBrush` |
| 失败横幅底 | `#3B2A12` | `HmiWarningBannerBrush`（已存在） |

不新增颜色令牌；圆角用 8/12，间距用 8/10/12/14/16。

## 六、实现拆解（确认后按此施工）

| 层 | 文件 | 内容 | 批次 |
| --- | --- | --- | --- |
| 领域 | `Scada\Models\ScadaStateList.cs` | `ScadaStateList`（Id/Name/Entries[Value:int, Text]/DefaultText）+ 精确匹配 | **P1-1** |
| 领域 | `Scada\Models\ScadaDocument.cs` | `StateLists` 集合 + 变更记录（照 `Alarms` 范式） | **P1-1** |
| 控件 | `Scada.Controls\Elements\ComboBoxElement.cs` | 可选 `StateListId`：只读文字 / 只读下拉 / 读写下拉；为空继续用 `Options` | **P1-1** |
| 控件 | `Scada.Controls\Runtime\ScadaRuntimeContext.cs` | `StateLists`（运行态解析） | **P1-1** |
| 控件 | `Scada.Controls\Elements\ScadaElementBase.cs` + `Canvas\ScadaCanvas.cs` | `StateListResolver` 注入（设计期/运行态共用） | **P1-1** |
| 编辑 | `ViewModels\ScadaPropertyViewModel.cs` + `Views\ScadaPropertyView.xaml` | `StateList` 行（选择器 + 管理…） | **P1-1** |
| 编辑 | `Views\DialogViews\StateListManagerView.xaml` + VM | 列表增删改 + 条目（值/文字）增删改 + 引用数 | **P1-1** |
| 编辑 | `ScadaEditorViewModel` / `ScadaEditorView.xaml.cs` | 注入 resolver、管理器关闭后刷新画布 | **P1-1** |
| 运行 | `ScadaRuntimeWindow` | 注入 resolver | **P1-1** |
| Web | `ScadaPageSnapshot` + `wwwroot\elements-*.js` | 快照带列表；ComboBox 文本态渲染 | **P1-1** |
| 断言 | `ScadaChecks` | 列表序列化、ComboBox 三态、缺失/回退、管理器 VM | **P1-1** |
| 控件 | `StateViewElement` + 主题 + 描述符 | 文本/图片/图文、位/位号/范围、图片解析 | P1-2 |
| 控件 | `SelectorSwitchElement` / `BitButtonElement` / `LampElement` / `IndicatorElement` | 可选状态文字/档位 | P1-2 |
| 控件 | `ButtonElement` | **不改**：按钮标题继续手填 `Text` | — |

## 七、待确认（实现前拍板）

| # | 问题 | 本稿建议 |
| --- | --- | --- |
| Q1 | 管理器入口：顶部“资源”菜单，还是属性面板行内“管理…”？ | **行内“管理…”为主 + 顶部“资源”菜单为次**；两处都进同一个对话框 |
| Q2 | 条目文本是否要支持多语言列？ | **v1 单列**；模型留扩展位（条目文本后续可换字典），不在本批做 |
| Q3 | 图片缺失时保留上一次有效图？ | **不保留**；显示占位，避免旧图被当成新状态 |
| Q4 | 范围重叠：只提示还是拒绝保存？ | **只提示**；不自动合并，保存时列出重叠条目 |
| Q5 | 一个 StateView 同时绑文本列表和图形列表？ | **不允许**；一份列表一种模式，需要图文就列表条目同时带 Text/Image |
| Q6 | 是否新增「离散值（Word）」模式？ | **P1-1 的列表本身就是离散值（Value:int + Text）**，不需要单独的模式字段；位/位号/范围随 P1-2 的 StateView 再加，只追加字段 |
| Q7 | 哪些控件默认接列表？ | **P1-1 只有 `ComboBox` 接**；`Button` 永远手填；`SelectorSwitch` / `BitButton` / `Lamp` / `DataGrid` 列 / `StateView` 全部 P1-2，等真实第二消费方 |

## 八、已知偏差

- Web 演示里的图片是内联 SVG 示意；WPF 走方案 `Image\` 相对路径。
- Web 管理器删除用 `confirm()`；WPF 用统一确认对话框。
- 不做导入/导出、不做多语言分列、不做条目拖拽排序（v1）。
- Web 运行态图片态在资产路由接通前显示占位；文本态可用。

## 九、P1-1 实施状态（2026-10-24）

- 已落地：`ScadaStateList` + `ScadaDocument.StateLists`、`Hmi.ComboBox.StateListId`、属性面板选择器 + 管理器、画布/编辑器/运行窗口解析器、Web 快照与 ComboBox 文本态渲染。
- 断言：`ScadaChecks` `[BL1]~[BL19]`；全量 **2710 / 0**。
- 渲染证据：`artifacts/render/20261024/scada-state-list/01-combobox.png`、`02-manager.png`。
- 实现记录：`docs/code-changes/2026-10-24-SCADA文本列表与ComboBox接入（P1-1）.md`。
- 本批**未做**：`StateView`、图形开关/图形 IO/字指示灯、SelectorSwitch/BitButton/Lamp 状态文字、从 Options 导入、只读下拉（`Output+Field` 仍整体禁用）、多语言列。

## 十、验收清单（复刻后）

- [ ] `Hmi.StateView` 在工具箱出现，属性面板能选状态列表、改呈现方式。
- [ ] 三种模式匹配正确；位号 3（两位同时）走回退。
- [ ] 列表缺失/无匹配/图片缺失/绑定丢失四种状态都有可见文案，不空白。
- [ ] 管理器能增删列表、增删条目、改模式、选图片；改完画布即时刷新。
- [ ] `.vms` 往返后列表与引用不丢；另存为后图片资产仍在。
- [ ] Web 文本态渲染正确；图片态有占位/资产两种路径。
- [ ] `ScadaChecks` 新增断言全绿；渲染探针截图存 `artifacts/render/`。
