# 第三轮控件会审 · 现状包（数据表格 / 轴控台·轴面板 / 报警条）

日期：2026-10-25　用途：**会审前提材料**（配合 `docs/方案设计/2026-10-25-第三轮控件联合评审计划（数据表格-轴控台-报警条）.md`）

> 评审结论见 [评审记录.md](评审记录.md)（含 8 条 P1 裁定与挂起项）；
> **阅读版（推荐）**：[review.html](review.html) —— 按控件分节、按级别筛选、截图内嵌；
> **改版对照（最新，每控件一个页签）**：[review-v2.html](review-v2.html) —— 当前 / 改进后图片 + 理由。

> 说明：本包给出"模板要点 / 属性表 / 断言覆盖"三部分，全部从源码与最近一次断言日志摘录。
> **四态截图尚未产出**——它需要扩展渲染探针（见 §4 规格），本轮不假装有图。

---

## 1. 数据表格 `Hmi.DataGrid`

**定位**：把最近 N 条记录按列摆出来（报警历史 / 日志 / 追溯清单）。默认 720×300，分类「数据」。

### 1.1 模板要点（`Themes/DataGridElement.xaml`）

| 要点 | 现状 | 相关纪律 |
| --- | --- | --- |
| 列表控件 | `ListView + GridView`，**必须挂** `Style="{StaticResource GridListViewStyle}"` | R19：不挂 = 列头消失；不许改成隐式样式 |
| 行模板 | `GridViewRowPresenter`（`Columns="{TemplateBinding GridView.ColumnCollection}"`） | R20：不能用裸 `ContentPresenter` |
| 行内边距 | **4**（与列头 x4 + Padding 8、`GridViewRowPresenter` 内置 6 同一竖线） | R21 |
| 资源作用域 | 视图就地合并 `GridView.xaml` | R22：断言宿主里也要能解析 |
| 行高 | 由 `RowHeight` 属性控制，**禁止 `Auto`** | R159：`Auto` 构造期抛异常 |
| 行为属性 | `NewestFirst`（新行在上）、`Zebra`（斑马纹） | 实时插入不抢滚动 |

### 1.2 属性表（设计期可配）

| key | 显示名 | 默认 | 说明 |
| --- | --- | --- | --- |
| `Columns`（`ScadaGridColumnsCodec.Key`） | 列 | 空 | 规范 JSON：`Header` / `Variable` / `Width` / `Align` / `Format`；`Width=0` 占满剩余 |
| `NewestFirst` | 新行在上 | `True` | 最新一条永远第一行、不抢滚动 |
| `Zebra` | 斑马纹 | `True` | 隔行底色 |
| `RowHeight` | 行高 | 数值 | 不能填 `Auto` |
| `MaxRows` | 最多行数 | 数值 | 缓冲上限 |
| `Text` / `Fill` / `Stroke` / `CornerRadius` | 标题 / 底色 / 边框 / 圆角 | — | 走外观组 |

### 1.3 断言覆盖

- 引用位置：`BackgroundAssetChecks.cs`、`Phase1UiChecks.cs`、`Program.cs`（共 9 处）。
- 已钉住：列 codec 的解析/规范化、`RowHeight` 不接受 `Auto`（构造期）、`GridView` 静态扫描（R19/R20，`UIThemeSmokeTest`）、
  模板命名件与圆角落地（`Phase1UiChecks` 的"外层 Frame 圆角真的落到 Border"同款检查）。
- **未钉住（评审要问）**：5000 行分页性能、实时插入时"正在看历史不被拽回顶部"、空态三义（无数据 / 未接源 / 变量未解析）。

---

## 2. 轴控台 `Hmi.AxisConsole` / 轴面板 `Hmi.AxisPanel`

**定位**：唯一会动设备的图元。`AxisConsole` = 状态 + 操作 + 点位表（320×500）；`AxisPanel` = 状态 + 操作（280×310，无点位表）。

### 2.1 模板要点

| 要点 | 现状 | 相关纪律 |
| --- | --- | --- |
| 外层 | `AxisFrame` 圆角 12（断言验证过真的落到 `Border`） | 外观与命中区分开 |
| 命令按钮 | 六个（使能 / 点动 / 回零 / 停止 / 清报警 / 失能），命名件齐全 | R108：权限在按下前判（绑 `IsEnabled`，每拍重算） |
| 点动 | **按住才动**：Down 起、Up / `MouseLeave` / `LostMouseCapture` 停，要 `CaptureMouse` | R108 |
| 回零 | **长按 0.5s** 才下发 | R108 |
| 点位表（仅 Console） | 每拍 `TryGetPoints` + 指纹比对，**只有变化才重建行** | R138 / AX10：否则换掉用户正在输入的框 |
| 禁用语义 | 不许在 `Control` 子类声明名为 `IsEnabled` 的 DP（遮蔽 `UIElement.IsEnabled`） | R108 |
| 轴寻址 | 按稳定 `AxisId`；名字只做展示与旧数据兜底 | R95 |

### 2.2 属性表

| key | 显示名 | 说明 |
| --- | --- | --- |
| `AxisOptions`（`ScadaAxisOptionsCodec.Key`） | 候选轴 | JSON 字符串：`[{"AxisId":"G(axis:X)","Name":"X"}]`；只能从工程已启用的轴多选 |
| `ShowCommands` | 显示操作区 | `False` 时整块收起（只读监视） |
| `ShowPoints` / `PointsRows`（Console） | 显示点位 / 点位行数 | 仅 `AxisConsole` |
| `Text` / `Fill` / `Stroke` / `CornerRadius` | 标题 / 外观 | — |

### 2.3 断言覆盖

- 引用位置：`AxisPanelChecks.cs`（AX 系列主体）、`Phase1UiChecks.cs`（命名件/圆角），共 24 处。
- 已钉住：六个命令按钮与点位区命名件、`AxisFrame` 圆角落地、`[AX3]/[AX7]/[AX9]/[AX10]/[AX12]`（门禁、点位、改名、指纹）、
  `[AX13]`（**分发器不再下发事件里的轴控动作**，2026-10-25 纠偏后的反向断言）。
- **未钉住（评审要问）**：三种"不能动"（未使能 / 未回零 / 超软限位）在界面上是否各自可辨；"已下发"与"已到位"是否同屏可区分；
  停止按钮在任何状态下是否都可见可点。

---

## 3. 报警条 `Hmi.AlarmBanner`

**定位**：操作员第一眼看到的报警面。默认 320×140，分类「报警」。与「报警汇总表」分工：**条=知道有事，表=处理掉**。

### 3.1 模板要点

| 要点 | 现状 | 相关纪律 |
| --- | --- | --- |
| 外层 | `BannerFrame` 圆角 8（断言验证过） | — |
| 严重度 | 左侧 **3px 色条**（命名件保留，断言验证过），行内还有级别圆点与文字 | 三重编码，不只靠颜色 |
| 空态 | `EmptyText`（默认「系统正常」） | 空态与"未接报警源"要能区分（评审要问） |
| 数据 | 读运行态活动报警列表，按严重度排序 | 与汇总表共用同一份数据，不各存一份 |
| 闪烁 | 严重且未确认时色条闪（节拍源驱动） | 闪烁只给严重级 |

### 3.2 属性表

| key | 显示名 | 默认 | 说明 |
| --- | --- | --- | --- |
| `MaxRows` | 最多显示 | 数值 | 超出后的行为见 §3.3 未钉住项 |
| `EmptyText` | 无报警提示 | 「系统正常」 | 空态文案 |
| `NormalColor` | 正常色 | 令牌色 | 无报警时的状态色 |
| `InfoColor` | 提示色 | 令牌色 | 提示级 |
| `WarningColor` | 警告色 | 令牌色 | 警告级 |
| `CriticalColor` | 严重色 | 令牌色 | 严重级（不可静音由声音服务决定） |
| `Text` / `Fill` / `Stroke` / `FontSize` / `FontWeight` | 标题与外观 | — | 行内次要信息按七成不透明度 |

### 3.3 断言覆盖

- 引用位置：`Program.cs`（16 处）。
- 已钉住（最近一次断言日志可见）：`3px 严重度色条命名件保留`、`外层 BannerFrame 圆角真的落到 Border（8）`、
  `空态显示 EmptyText（系统正常）`。
- **未钉住（评审要问）**：新报警插入是否打断正在阅读的那一条；`MaxRows` 超出时堆叠还是轮播；
  条上是否允许确认（当前分工是"不确认"）；静音期间条上是否显示"已静音"。

---

## 4. 四态截图：已出 8 张（只覆盖"不依赖运行态"的状态）

**已出图**（`docs/UI设计基线/app-shell/evidence/third-round/`，探针 `_ScadaUiProbe --elements` 渲染）：

| 文件 | 展示的状态 | 可用性 |
| --- | --- | --- |
| `axispanel-unconfigured.png` | 未配置轴：状态区 8 点 + 命令区 6 按钮 + 点动模式 + 设计预览提示 | **最有用**：布局、危险分级、命中区、信息密度都能评 |
| `axispanel-disabled.png` | 同上 + 整体禁用 | 可用 |
| `axisconsole-unconfigured.png` | 轴控台未配置（含点位区） | 可用 |
| `datagrid-design.png` | 配了 3 列但**未绑定采集信号** → 仍显示"还没有配置采集" | **部分**：暴露了"空态文案不区分"的问题，但看不到列头 |
| `datagrid-empty.png` / `datagrid-disabled.png` | 未配置 / 禁用 | 可用 |
| `alarmbanner-empty.png` | 空态「系统正常」 | 可用 |
| `alarmbanner-disabled.png` | 禁用 | 可用 |

**未覆盖（必须补运行态才能出）**：表格有行、报警条有多条报警、轴在动/已使能。
原因：三者都要 `ScadaRuntimeContext`——表格要采集信号 + 数据泵，报警条要活动报警列表，轴面板要轴源 + 权限。
本轮没有装配（那是另一个量级的依赖），所以评审记录里这类条目全部标了 `[需运行态图]` 并**挂起定级**。

**探针用法**：`tools\probes\run-probe.ps1 -Name ScadaUiProbe -Args --elements`
（`ElementStates.cs` 里是状态清单，加状态就往那个数组里加一行）。

---

## 5. 会审时每栏看什么（映射表）

| 评审点 | 看哪里 |
| --- | --- |
| 表格密度/行高/斑马纹 | 模板要点 §1.1 + 截图（待补） |
| 表格"看历史不被拽回顶部" | 属性 `NewestFirst` + 断言缺口 §1.3 |
| 轴控三种"不能动"是否可辨 | 属性 `ShowCommands` + 断言缺口 §2.3 |
| 点动按住/回零长按 | 模板要点 §2.1（已有断言，改外观不影响） |
| 报警条严重度编码 | 模板要点 §3.1 + 四个颜色属性 |
| 报警条与汇总表分工 | §3 定位 + 计划文档 §2.3 |
