# SCADA 控件与事件第三次对标（汇川 InoTouchPad · 按 2026-10-29 当前代码）

日期：2026-10-29
用途：把手册的**控件清单 + 控件事件**重新对一遍当前代码，回答两个问题：**还缺哪些控件、还缺哪些功能**。
口径：**代码优先**。本文所有"现有"结论都从当前源码重新读出，不沿用旧评审的数字。

> **与旧文的关系**：本文**取代**以下两篇里"缺口与优先级"的结论（旧文的数字已过期，不再作为现状依据）：
> - `docs/code-changes/2026-10-23-SCADA控件对标手册缺口清单.md`（当时 30 个 TypeKey、动画 4、事件 10）
> - `docs/方案设计/2026-10-24-SCADA控件二次对标评审（汇川InoTouchPad-威纶通待补）.md`（当时 37 个 TypeKey、动画 5、事件 13）
>
> 两篇仍保留其历史价值：**已决但不建议重开**的条目（见 §九）以它们为准，本文不重复论证。

---

## 一、依据与快照

| 项 | 内容 |
| --- | --- |
| 手册 | `D:\INOVANCE\InoTouchPad\InoTouchPad软件帮助.chm`，43,386,996 字节，mtime 2026-04-21；解出 2597 个文件（`hh.exe -decompile`），目录 `rqn.hhc` 为 GBK |
| 对照范围 | 手册 **7.3 控件分类 68 项**（7.3.1~7.3.6）、**7.5.1 动画 10 项**、**7.5.2 事件 21 条语义**、**17.3 系统函数 20 类**；不含 7.3.7 定制控件 / 7.3.8 收藏（属机制，不是控件） |
| 代码快照 | `Scada.Controls\Descriptors\BuiltInElements.cs` = **50 个 TypeKey**；`ScadaEventType` = **14 个事件**；`ScadaAnimationType` = **7 种动画**；`ScadaActionType` = **28 个动作** |
| 状态定义 | ✓ 直接对应；△ 现有控件加属性/组合可近似；✗ 无对应物；N/A 本产品不适用 |
| 复现 | 见 §十一 |

### 手册的控件属性模型（四段 + 两附加）

手册每个控件页统一给四段：**常规 / 属性（外观·布局·文本·闪烁·过程·缩放·限制·颜色·样式·其它）/ 动画 / 事件**，
外加 **安全（权限、权限控制可见性、操作前确认、密码输入、启用软键盘）/ 操作记录（启用、记录变量、动作描述）**。
另有三个跨控件字段：**图层（0~31）/ z 值 / 透明度（1~100）/ 国际化名称**。

这套模型我方已有对应物（权限 `ScadaPermissionKeys`、二次确认 `ScadaEventHook.RequireConfirmation`、
审计 `ScadaAuditWriter`、图层 `ScadaLayer`、软键盘 `ScadaSoftKeyboard`），**缺的是"国际化名称"这一列**（见 §八）。

---

## 二、我方现状（代码实读）

### 2.1 图元 50 个（`BuiltInElements.cs`）

| 分类 | 数量 | TypeKey |
| --- | --- | --- |
| 基础 | 10 | Rectangle、Ellipse、Text、Panel、ImageView、Picture、Gif、Line、Polygon、Bezier |
| 工艺 | 7 | Valve、Pump、Motor、Pipe、Tank、Symbol、PipeRun |
| 数值 | 9 | ProgressBar、Slider、IOField、Gauge、KpiBoard、RunMeter、MultiTrend、TextField、ComboBox |
| 数据 | 7 | QRCode、Barcode、BitTable、IoMonitor、VariableMonitor、DataGrid、Report |
| 操作 | 5 | Button、BitButton、SelectorSwitch、StateView、RecipePanel |
| 指示 | 4 | Indicator、Lamp、Clock、DynamicLine |
| 运动 | 3 | AxisConsole、AxisPanel、AxisPointTable（`ShowInToolbox=false`，旧键兼容） |
| 报警 | 2 | AlarmBanner、AckButton |
| 图表 | 2 | BarChart、PieChart |
| 嵌入 | 1 | SubPage |

### 2.2 事件：14 个枚举值，实际声明的只有 9 个值

| 事件 | 谁声明了它 |
| --- | --- |
| `Loaded` / `Unloaded` | **画面级**（`ScadaPageEvents`），不在图元上 |
| `Pressed` / `Released` | Button、BitButton、Symbol |
| `InputCompleted` | IOField、TextField、Slider |
| `ObjectChanged` | BitButton、Slider、ComboBox、StateView |
| `Opened` / `Closed` | BitButton、StateView |
| `ValueChanged` / `ValueBecameTrue` / `ValueBecameFalse` / `ValueOverUpperLimit` / `ValueUnderLowerLimit` | **变量级**（`ScadaVariableEvent`，不挂图元） |
| `ScheduleFired` | **方案级**（计划表，不在手册里，我方自增） |

> 声明纪律写在 `BuiltInElements.cs` 类注释里：**只声明"已经有人发得出来"的事件**，清单里出现发不出来的事件就是"永远不响的钩子"。

### 2.3 动画 7 种（`ScadaAnimationType`）

外观变化 / 水平移动 / 垂直移动 / 可见性 / 启用对象 / 对角线移动 / 比例移动。

### 2.4 动作 28 个（`ScadaActionType`）

写变量、增量、减量、置位/复位/取反、画面跳转/返回/弹窗开关、流程启停/单次/暂停/恢复、
运动九条（使能/失能/绝对/相对/回零/停止/清报警/点动起停）、应用配方、播放声音、语音播报、写日志。

---

## 三、控件差集（手册 68 项 → 32 ✓ / 16 △ / 14 ✗ / 6 N/A）

### 3.1 7.3.1 绘图工具（11）

| 手册项 | 现状 | 说明 |
| --- | --- | --- |
| 文本域 / 矩形 / 椭圆 / 多边形 / 线 / 折线 / 贝塞尔曲线 / 动态画线 | ✓ | Text、Rectangle、Ellipse、Polygon、Line（≥3 点即折线）、Bezier、DynamicLine |
| 表格 | △ | `Hmi.DataGrid` 是**数据表**，不是静态排版表；纯排版可用 Panel + 多个 Text 拼 |
| 流动块 | △ | `Hmi.Pipe` 有流向箭头与状态色，**没有连续流动动画**（无 DashOffset 动画） |
| **定时器** | **✗** | 无定时器图元、无"超时"事件；流程 `WaitStep` 是流程级，替代不了"本页每 5 秒翻一页/自增" |

### 3.2 7.3.2 画像与画面（7）

| 手册项 | 现状 | 说明 |
| --- | --- | --- |
| 画中画 / 二维码 / 条形码 / 图片 / GIF 显示 | ✓ | SubPage（P1a 只读固定页）、QRCode、Barcode、Picture、Gif |
| 画布 | △ | `Hmi.Panel` 是容器，不支持脚本绘制 |
| 增强图片 | △ | Picture 有 Stretch + 旋转；无手册的七点拖拽/透明色键 |

### 3.3 7.3.3 控制与显示（13）

| 手册项 | 现状 | 说明 |
| --- | --- | --- |
| 数值 IO 域 / 字符 IO 域 / 符号 IO 域 | ✓ | IOField / TextField / ComboBox + `ScadaStateList` |
| 按钮 / 位按钮 / 位指示灯 | ✓ | Button / BitButton / Indicator·Lamp |
| 字指示灯 / 图形开关 / 图形 IO 域 | ✓ | **同一个 `Hmi.StateView` 三个预设**：Text+None / Image+Toggle / Image+None |
| 时间日期 | △ | `Hmi.Clock` **只显示不能输入**（属性只有 Format/Interval） |
| 字按钮 | △ | Button + 动作（写定值/增减）可覆盖；无"循环递增/状态递增"预设 |
| 文本开关 | △ | StateView 可配出 Text+Toggle，但**不是预设**，要手工改两项属性 |
| 功能键 | △ | 软键盘是宿主设施；**无自定义键盘画面**（`ScadaSoftKeyboard` 无键盘页概念） |

### 3.4 7.3.4 数据可视化（14）

| 手册项 | 现状 | 说明 |
| --- | --- | --- |
| 滚动条 / 进度条 / 量表 / 仪表 / 表针 | ✓ | Slider / ProgressBar / Gauge（表针是 Gauge 的一种外观） |
| 趋势 | ✓ | ChartTrend + MultiTrend（最多 4 条曲线） |
| 棒图 | △ | ProgressBar 有量程与数值，**没有刻度、上下限颜色、限制线** |
| 3D 饼图 | △ | PieChart 是 2D（环形/实心） |
| 数组表格 | △ | DataGrid 列按变量名取值；无"整数组一列"的配置 |
| **圆形进度条** | **✗** | 环形进度 + 中心数值；OEE/节拍看板常用 |
| **旋钮** | **✗** | 圆弧刻度 + 拖动写值；参数微调的真实交互 |
| **跑马灯** | **✗** | 一圈灯按速度/位置旋转 |
| **XY 曲线** | **✗** | 两组量做 X/Y；视觉测量相关性分析用得上 |
| **仪表 Plus** | **✗** | 量表高级外观（分段色、渐变、指针自适应） |

### 3.5 7.3.5 生产管理（20）

| 手册项 | 现状 | 说明 |
| --- | --- | --- |
| 报警条 / 报表 / 变量监视 / I/O 监视 | ✓ | AlarmBanner / Report（明细·汇总·打印·导出）/ VariableMonitor（16 位）/ IoMonitor（8 位） |
| 报警记录 | △ | AlarmBanner 是**跑马灯式条**（只看不操作）+ 宿主右侧抽屉；**没有页内汇总表，没有行内逐条确认**（`ShowRowAck` 全仓 0 命中） |
| 配方控件 | △ | `Hmi.RecipePanel` **只读**；有 `ApplyRecipe` 动作，但没有页内"选配方/下载/上传/保存" |
| 数据记录 | △ | DataGrid `Source=History` 能查；**没有"开始/停止记录"的画面控件或动作** |
| 组数据视图 | △ | DataGrid 历史源接近；无"变量组 + 记录周期 + 选中行输出"的组记录模型 |
| **故障码查询** | **✗** | 汇川伺服/PLC 专用码表；通用化可做 CSV 码表，优先级低 |
| **操作记录** | **✗** | 宿主有审计 CSV（`ScadaAuditWriter`）；无页内视图 |
| **用户管理** | **✗** | 宿主有登录弹窗 + 用户库；无页内用户管理视图 |
| **文件浏览** | **✗** | PC 端可由宿主对话框替代 |
| **PDF 视图** | **✗** | 作业指导书/图纸上屏 |
| **浏览器** | **✗** | WebView2 容器；安全边界要单独定 |
| **数据库视图控件** | **✗** | 已有 Database 插件，但 SCADA 页内视图没有 |
| 物联网卡 / 网络信号栏 / WiFi / 设置 WiFi 地址 / USB 摄像头 | N/A | 嵌入式 HMI 专用；PC 端不适用（相机走插件与图像变量） |

### 3.6 7.3.6 PC 专用（3）

| 手册项 | 现状 | 说明 |
| --- | --- | --- |
| 海康网络摄像机视图 | △ | ImageView 显示插件采集的图像变量，不需要海康 SDK 图元 |
| 流程图 | N/A | 流程编辑在流程栏，不在 SCADA 画面 |
| 软件容器 | ✗ | 把第三方 exe 主窗口嵌进画面；本产品不适用 |

### 3.7 汇总

| 手册分类 | 项数 | ✓ | △ | ✗ | N/A |
| --- | --- | --- | --- | --- | --- |
| 7.3.1 绘图工具 | 11 | 8 | 2 | 1 | 0 |
| 7.3.2 画像与画面 | 7 | 5 | 2 | 0 | 0 |
| 7.3.3 控制与显示 | 13 | 9 | 4 | 0 | 0 |
| 7.3.4 数据可视化 | 14 | 6 | 3 | 5 | 0 |
| 7.3.5 生产管理 | 20 | 4 | 4 | 7 | 5 |
| 7.3.6 PC 专用 | 3 | 0 | 1 | 1 | 1 |
| **合计** | **68** | **32** | **16** | **14** | **6** |

---

## 四、事件差集（手册 21 条语义 → 已实现 14 条，缺 7 条）

手册 7.5.2 那张表有 21 行（"激活/取消激活/更改"各出现两次，语义不同：一次给报警、一次给按钮）。

| # | 手册事件 | 可组态对象 | 我方 | 判定 |
| --- | --- | --- | --- | --- |
| 1 | 加载 | 画面/画布 | `Loaded` | ✓ |
| 2 | 清除 | 画面/画布 | `Unloaded` | ✓ |
| 3 | **激活** | 模拟量/离散量报警 | — | **✗** |
| 4 | **取消激活** | 报警（清除时） | — | **✗** |
| 5 | **确认** | 报警（用户确认时） | — | **✗** |
| 6 | 输入完成时 | 数值/字符/日期时间 IO 域 | `InputCompleted` | ✓ |
| 7 | 更改（对象状态） | 图形开关/文本开关/滚动条/量表 | `ObjectChanged` | ✓ |
| 8 | 关闭 | 文本开关/图形开关 | `Closed` | ✓ |
| 9 | 打开 | 文本开关/图形开关 | `Opened` | ✓ |
| 10 | **单击** | 按钮 | — | **✗**（现在要配"按下+释放"两条，且不判"同一控件内按下并释放"） |
| 11 | 按下 | 按钮/字按钮/位按钮 | `Pressed` | ✓ |
| 12 | 释放 | 按钮/字按钮/位按钮 | `Released` | ✓ |
| 13 | **激活（按钮）** | 按钮被激活（启用） | — | **✗** |
| 14 | **取消激活（按钮）** | 按钮被取消激活（禁用） | — | **✗** |
| 15 | 更改（按钮状态） | 按钮对象状态改变 | `ObjectChanged` | ✓ |
| 16 | 上限 | 非 BOOL 变量 | `ValueOverUpperLimit` | ✓ |
| 17 | 下限 | 非 BOOL 变量 | `ValueUnderLowerLimit` | ✓ |
| 18 | 更改数值 | 变量 | `ValueChanged` | ✓ |
| 19 | 值为真 | BOOL 变量 | `ValueBecameTrue` | ✓ |
| 20 | 值为假 | BOOL 变量 | `ValueBecameFalse` | ✓ |
| 21 | **超时** | 定时器 | — | **✗** |

**缺 7 条**：报警激活 / 报警取消激活 / 报警确认 / 单击 / 按钮激活 / 按钮取消激活 / 超时。
我方另有手册没有的 `ScheduleFired`（计划表触发），不计入缺口。

> **报警三条为什么值得补**：`ScadaAlarmEngine` 已经真发 `AlarmRaised` / `AlarmChanged` / `AlarmCleared` 三个事件
> （`ScadaAlarmEngine.cs:201/204/207`），缺的只是"接到组态钩子上"这一层。补上之后
> "报警时自动切报警页 / 启动录像 / 确认后复位按钮"就能配出来，不必再靠变量值间接推。

---

## 五、动画差集（10 → 7）

| 已做 | 未做 |
| --- | --- |
| 外观变化、水平移动、垂直移动、可见性、启用对象、对角线移动、比例移动 | **直接移动**（目标点表）、**自定义移动**（多段折线）、**动态移动**（运行期改目标） |

剩余三种都是"移动"的进阶档：需要目标位置表 + 多段插值，`ScadaAnimation` 现有字段（起点/终点/范围）不够，属模型改造。
按旧文已决口径：P3，vision 场景少用。

---

## 六、动作 / 系统函数差集（手册 20 类 → 我方覆盖 9 类）

| 手册 17.3 分类 | 我方 | 说明 |
| --- | --- | --- |
| 画面 / 编辑位 / 用于画面对象的操作 | ✓ | Navigate / NavigateBack / OpenPopup / ClosePopup、SetBit·ResetBit·ToggleBit、WriteVariable·Increment·Decrement |
| 计算 | △ | 无算术动作；可由 C# 脚本插件或流程步骤替代 |
| 报警 | △ | 只有 `AckButton` 图元；**无"确认报警"动作**，也无"静音"动作 |
| 配方 | △ | `ApplyRecipe` 有；**保存/上传/下载到设备没有** |
| 记录 | ✗ | 无"开始/停止记录"动作 |
| 用户管理 | ✗ | 无"登录/注销"动作（宿主有登录弹窗，但画面按钮配不出来） |
| 设置（语言） | ✗ | 无语言切换（能力本身也缺，见 §八） |
| 打印 | ✗ | Report 图元自带打印按钮；无独立"打印"动作 |
| 定时器 | ✗ | 无定时器启停（图元也没有） |
| 数组读写 | ✗ | 无数组批量读写动作 |
| 连接 / 通信 / 自由协议 / 数据服务 / 文件传输 | ✗ | 通信层能力在插件与通讯模块；SCADA 画面无入口（多数场景不需要） |
| HMI 日期时间 | △ | 无"设置系统时间"动作 |

---

## 七、评审结论：建议添加的控件（按本项目排序）

本项目定位是**视觉检测机上位机（视觉 + 运控 + 组态 + Web 运行态）**，不是通用 HMI 面板。
排序依据：现场画面能不能干活 > 客户验收会不会问 > 像不像商业 HMI。

### P0 —— 不补现场画面就缺一块

**1. `Hmi.AlarmTable` 报警汇总表（成本 M）**
- 现状：只有 `AlarmBanner`（跑马灯条，"知道有事"）+ 宿主右侧抽屉（不在画面里）。**"处理掉"这一半在画面上是空的**。
- 前置已齐：`ScadaAlarmEngine.ActiveAlarms` / `Acknowledge(alarmId)` / `AcknowledgeAll()`（`IScadaAlarmCommands` 已定契约）、
  静音状态在 `ScadaSoundAlarmService`（`MuteMinutes` / `IsMuted`）。
- 设计基线已出：`docs/UI设计基线/scada-chart-alarmtable/README.md`（v1，待用户裁定 7 条，**未开始实现**）。
- 关键实现纪律：**每拍重读 + 指纹短路**，只有变化才重建行，否则会换掉操作员正在点的"确认"按钮（AX10/R138 同款）。
- 同时补：**行内逐条确认**（旧文 `ShowRowAck` 未实现，本条一并解决）。

**2. `Hmi.Timer` 定时器 + `Timeout` 超时事件（成本 S/M）**
- 现状：手册 7.3.1 唯一完全缺失的绘图工具；`ScadaEventType` 也没有 `Timeout`。
- 为什么流程 `WaitStep` 替代不了：定时器是**画面级、不占流程、不阻塞采集**的语义（轮播、自动翻页、超时弹窗、心跳自增）。
- 实现面：新图元（无外观，设计期一个方块）+ `ScadaEventType.Timeout`（追加枚举值）+ 运行态按页面生命周期启停的节拍器。
- 注意：枚举值**只能追加**（`ScadaEventType` 数值已写入 `.vms`）。

**3. 时间日期可输入（成本 S，不新增 TypeKey）**
- 现状：`Hmi.Clock` 只有 `Format` / `Interval`，纯显示。
- 场景：班次切换、排产日期、校时。做法：给 Clock 加 `Mode = Display / Edit`，Edit 走现有 `InputCompleted` 与写变量通道。
- 比新增 `Hmi.DateTimeField` 便宜，也不会多一个 TypeKey（少一份 Web 渲染器欠账）。

### P1 —— 客户验收会问，且现在补最便宜

**4. 报警三事件（激活 / 取消激活 / 确认）+ `AcknowledgeAlarm` 动作（成本 S）**
- 引擎三事件已存在，只差枚举值 + 钩子接线 + 面板行。
- 动作侧补"确认报警"（单条/全部）与"静音"，让画面按钮也能确认，不必只靠 `AckButton` 图元。

**5. `Clicked` 单击事件（成本 S）**
- 按钮最常用的语义。现在配一个"点一下做事"要写两条动作（按下+释放），而且按下后拖出去再松手也会触发释放动作。
- 语义：**按下与释放在同一图元内**才发（手册原话"按下和释放都要落在按钮上，否则无效"）。

**6. 配方控件升级：页内选配方 / 下载 / 上传 / 保存（成本 M）**
- 现状：`Hmi.RecipePanel` 只读展示 + `ApplyRecipe` 动作；**没有"选配方"的页内入口**，"把当前参数存成配方"也没有。
- 宿主已有 `ScadaRecipePickerView` / 配方服务，画面侧缺的是：下拉/列表选型 + 下载/上传/保存三个动作。

**7. 圆形进度条 + 旋钮（成本 M，两个独立控件）**
- 圆形进度条：OEE/节拍看板的门面，`KpiBoard` 目前只有数字。
- 旋钮：**有真实交互价值**（圆弧刻度 + 拖动写值），参数微调比滑块更像设备面板。
- 两者都可做成 `Gauge` 的 `Style` 变体先试，站不住再拆独立 TypeKey（避免无谓增加 TypeKey 欠账）。

### P2 —— 能用现有控件顶，或场景窄

| 项 | 建议 | 成本 |
| --- | --- | --- |
| 棒图扩刻度/上下限色/限制线 | **扩 `ProgressBar` 属性，不新增 TypeKey** | S/M |
| XY 曲线 | 视觉测量相关性分析；`VM.Charts` 加一个双值序列 | M |
| 流动块连续动画 | `Pipe` 加 `DashOffset` 动画（`IsAnimated` 属性） | S |
| 跑马灯 | 一圈灯按变量位置/速度旋转 | M |
| 仪表 Plus | Gauge 的高级外观（分段色/渐变/自适应指针） | M |
| 按钮激活/取消激活事件 | 联锁/权限导致按钮变灰时能配动作；与 `Enable` 动画配对 | S |
| 文本开关预设 | `StateView` 加第四个预设 Text+Toggle | S |

### P3 / 不做（附理由）

| 项 | 结论 |
| --- | --- |
| 操作记录 / 用户管理 / 文件浏览 / PDF / 浏览器 / 数据库视图 页内图元 | **不做图元**。宿主已有对应能力；做成图元会把权限、文件对话框、WebView2 安全边界带进画面模型。客户明确要再评估 |
| 故障码查询 | 通用 CSV 码表可做，但汇川专用码表对本产品无意义；P3 |
| 组数据视图 / 数组表格 | DataGrid + 历史源 + 数组元素绑定已覆盖 80%；等真有"整数组一列"的需求再补 |
| 3D 饼图 | 2D 饼图够用；3D 只增加实现与可读性成本 |
| 增强图片（七点拖拽/透明色键） | 视觉机的图片来自相机，不做手工修图 |
| 画布（脚本绘制） | 动态画线 + C# 脚本插件已覆盖；脚本绘制会变成第二套绘图 API |
| 静态排版表格 | Panel + Text 可拼；专用排版表收益低 |
| 功能键 / 自定义键盘画面 | 软键盘已是宿主设施；自定义键盘页是嵌入式小屏需求 |
| 直接移动 / 自定义移动 / 动态移动动画 | 旧文已决 P3；需要目标点表 + 多段插值，模型改造大 |
| 物联网卡 / 网络信号栏 / WiFi 系列 / USB 摄像头 / 流程图 / 软件容器 | N/A（嵌入式专用或已有替代） |

---

## 八、比控件更重要的横切功能（这些决定"像不像商业 HMI"）

**1. 多语言 / 国际化（完全缺失，成本 L）**
- 全仓**没有** `ScadaLanguage` / i18n / 本地化类；在 `Scada`、`Scada.Controls`、`VisionMaster` 三个目录里 grep
  `多语言|Localization|Localizable|CurrentLanguage|LanguageSwitch|i18n` 只有 **1 处命中**，
  而且是 `ScadaChoiceNames.cs:46` 的一句注释："多语言本来就是独立于『图元有哪些属性』的另一件事"——
  即代码里已经承认它是独立能力，但**没有实现**。
- 手册每个控件都有"国际化名称"字段，我方没有。
- 现状：所有文案写死在图元属性里，出口或双语客户要重做一套画面。
- 已有地基：`ScadaStateList` 是方案级共享列表，可承载多语言文本；缺的是"语言表 + 当前语言 + 属性取值按语言解析"。

**2. 状态列表补三种模式（成本 M）**
- 手册第 8 章：**位（0/1）/ 位号（0~31）/ 范围** 三种模式，被符号 IO 域、字/位按钮、文本开关、位/字指示灯、图形开关、图形 IO 域、I/O 监视共用。
- 现状：`ScadaStateList.Find(int value)` **只做精确值匹配**，没有范围与位模式。现场"0~50 正常、51~80 预警、81+ 报警"这类分段配色配不出来。

**3. 动作补齐（成本 M，按需）**
登录/注销、语言切换、打印、记录启停、配方保存/上传、定时器启停、延时、数组读写、报警确认/静音。
其中**登录/注销**与**报警确认**优先级最高（画面按钮现在配不出这两件事）。

**4. 报警闭环**：`AlarmTable`（P0-1）+ 报警三事件（P1-4）+ 确认/静音动作，三件一起才闭环。

---

## 九、已决但不建议重开（沿用旧文裁定）

| 原提案 | 已决结论 |
| --- | --- |
| `Hmi.StateText` | 删除，改用 `StateView` |
| `Hmi.PageMenu` | 删除，改用运行窗口顶栏页面菜单 |
| `Hmi.RecordList` | 降级，扩 AlarmBanner 行内确认（**至今未实现**，本文归入 `Hmi.AlarmTable`） |
| `Hmi.HistoryTable` | 删除，改用 DataGrid `Source=History`（已实现） |
| `Hmi.AlarmList` | 不新增，扩 AlarmBanner + 宿主抽屉（**已被 `Hmi.AlarmTable` 基线取代**，见 `scada-chart-alarmtable`） |
| `Hmi.DataTable` | 不新增，复用 DataGrid |
| 旋转 / 缩放动画 | P3/不做（vision 无场景，模型改造大）；比例移动已作为缩放替代落地 |

---

## 十、成本与硬约束（新增一个 TypeKey 的真实代价）

按仓库现有约束，新增一个 TypeKey 要同时落地 **5 件事**（缺一不可）：

1. `Elements\` 控件类 + `Themes\` 样式 + `Generic.xaml` 合并 + `BuiltInElements.cs` 描述符（三处缺一，工具箱为空）；
2. `[ScadaProperty]` 走源生成器（或手写 DP），属性分组必须先在 `ScadaPropertyGroups` 登记（未登记 = 注册失败、图元进不了工具箱）；
3. **Web 运行态渲染器 + `caps` 声明** —— **R191 Web 方向暂停中**：暂停期新增的 TypeKey 在 Web 端会落到 `Hmi.Unsupported` 兜底，
   必须在本文/实施记录里登记为"Web 欠账"，恢复 Web 后补；
4. `ScadaChecks` 断言段 + 渲染证据；
5. 设计基线（R111 六项）+ 截图验收（R122/R132）。

**所以"能不能用现有控件加属性解决"永远先问一遍**：
本文 P0-3（时间日期）、P1-7（圆形进度条/旋钮）、P2（棒图/文本开关）都给了"扩属性/加预设"的便宜路线，优先于新增 TypeKey。

---

## 十一、复现

```powershell
# 1. 解包（hh.exe 在 C:\Windows）
hh.exe -decompile <outDir> "D:\INOVANCE\InoTouchPad\InoTouchPad软件帮助.chm"

# 2. 目录 rqn.hhc 是 GBK，按 <param name="Name"> + Local 解析
#    本轮脚本：tools\oneoff\parse_chm_toc.ps1
powershell -ExecutionPolicy Bypass -File tools\oneoff\parse_chm_toc.ps1 `
  -Hhc <outDir>\rqn.hhc -Out artifacts\logs\20261029\chm_toc.txt

# 3. 正文按 Local 文件名转文本（GBK → UTF-8）
powershell -ExecutionPolicy Bypass -File tools\oneoff\chm_batch_convert.ps1 `
  -Base <outDir> -List artifacts\logs\20261029\chm_controls.txt -OutDir artifacts\logs\20261029\chm_txt

# 4. 抽每个控件的"事件"小节
powershell -ExecutionPolicy Bypass -File tools\oneoff\chm_events.ps1 `
  -Dir artifacts\logs\20261029\chm_txt -List artifacts\logs\20261029\chm_controls.txt `
  -Out artifacts\logs\20261029\chm_events.txt

# 5. 抽我方 TypeKey 清单
powershell -ExecutionPolicy Bypass -File tools\oneoff\extract_scada_elements.ps1 `
  -Src Scada.Controls\Descriptors\BuiltInElements.cs -Out artifacts\logs\20261029\scada_elements.txt
```

中间产物在 `artifacts\logs\20261029\`（chm_extract / chm_txt / chm_toc.txt / chm_events.txt / scada_elements.txt），
脚本在 `tools\oneoff\`（一次性脚本，**为复现保留**，确认不再需要时按 R166 删除或归档 `_Retired\`）。

---

## 十二、结论（一句话版）

- **控件**：68 项里 32 直接对应、16 可近似、14 无对应（其中 6 项本产品不适用）。真正该补的只有 **3 个 P0**：
  **报警汇总表**（设计基线已出、API 已齐、只差实现）、**定时器+超时**、**时间日期可输入**。
- **事件**：21 条语义已实现 14 条，缺 7 条；其中**报警三事件**成本最低价值最高（引擎已发事件，只差接线），
  **单击**是按钮最常用语义，**超时**随定时器一起来。
- **决定上限的不是图元数量，是四块横切能力**：多语言（完全缺失）、状态列表三种模式（只有精确值）、
  报警闭环（条有表无、确认动作缺）、动作表（登录/注销/打印/记录启停/配方保存缺）。
