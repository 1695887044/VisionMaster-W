# 2026-10-09 NumericBox 数值输入框（含属性网格 NumericGenerator）—— 新控件第 1 个

- 日期：2026-10-09
- 项目：UI（WPF / .NET 9）+ 探针 _LogViewProbe + 样式参考 `docs/UI样式参考/`
- 范围：1 新控件（`UI\Controls\CustomControl\NumericBox.cs`）+ 1 主题（`Themes\NumericBox.xaml`）+ 1 生成器（`PropertyGrid\Generators\NumericGenerator.cs`）+ 注册（`PropertyGridDefaults`、`Generic.xaml`）
- 类型：新增控件（**带真实消费方**：所有属性网格里的数值参数）
- 口径：**构建** = `dotnet build`（UI / VisionMaster / UIThemeSmokeTest，Debug 与 Release）；**冒烟** = `UIThemeSmokeTest.exe`；**探针** = `_LogViewProbe.exe`（行为断言 + 渲染出图）

---

## 一、起因与落点确认

用户问"新控件给我添加了吗"。四个候选新控件里先做 NumericBox —— 它是唯一**立刻能找到真实消费方**的一个（上一轮刚清理过"零消费控件"，新控件不该重复那个坑）。

动手前先纠正了一个我自己说错的判断：

| 我原先的说法 | 实际（读码确认） |
| --- | --- |
| "数值走 `TypeGenerator` 的兜底文本框" | **不对**。`TypeGenerator`(Priority 0) 只处理带 `[PropertyItem]` 的自定义控件；数值实际走 **`StructValueGenerator`(Priority 100)** —— 它用裸 `TextBox` 渲染 `string \| 原始类型 \| decimal` |

裸文本框的四个缺口正是新控件的目标：**没有钳制**（敲 9999 也照收）、**没有小数位**、**没有单位**、
**解析跟着区域设置走**（小数点分隔符不是 `.` 的机器上，用户按习惯敲 `1.5` 会静默失败：值不变、也没有任何提示）。

生成器命中顺序是 `OrderByDescending(Priority)` + `FirstOrDefault`，100 那一档（Enum/OptionSource/StructValue/BoolState）同级靠**列表顺序**分胜负 ——
所以新生成器取 **Priority = 110**，稳定插在 `StructValueGenerator` 前面，同时不与 Enum（枚举不是数值）、Bool（白名单排除）抢。

## 二、NumericBox（`UI\Controls\CustomControl\NumericBox.cs`）

`TextBox` 派生（继承文本编辑的全套行为），只加"数值语义"：

| 依赖属性 | 作用 |
| --- | --- |
| `Value`（double，默认 TwoWay，NaN = 无值） | 当前值（已钳制）。NaN 不参与钳制 —— 否则"清空输入"会被夹成最小值 |
| `Minimum` / `Maximum` | 上下限；**钳制走 `Value` 的 CoerceValueCallback**，所以"外部赋越界值"与"用户敲越界值"走同一条路，代理属性读回来一定是夹过的 |
| `DecimalPlaces`（-1 = 不强制） | 整数类型用 0；浮点/小数用 -1（强制 3 位会把 0.123456 悄悄变成 0.123 —— 那是改用户数据） |
| `Step` | 上下键 / 滚轮步进量 |
| `Suffix` | 单位后缀（模板里右侧一行小字，空串整块收起） |
| `IsInvalid`（只读） | 上次提交不合法 → 模板标红；任何一次成功提交或文本变化都会清掉 |

行为细节：

- **解析**：先当前区域设置、再不变区域设置 —— zh-CN 下 `1.5` 两种都成立；de-DE 下 `1,5`（当前）与 `1.5`（不变）都能认。
- **非法输入**：值不动、文本退回上一个有效值、边框标红（探针里断言成 `Value=7 / IsInvalid=true / Text="7"`）。
- **滚轮**：**必须焦点在本框内**才改值 —— 否则用户滚页面路过就把参数悄悄改了，是表单里最讨厌的误操作。
- **上下键**：从"当前文本"起步而不是从 `Value`（敲了 12 还没提交就按上键，应该得到 13）。

## 三、NumericGenerator（属性网格接线）

- 白名单 `byte/sbyte/short/ushort/int/uint/long/ulong/float/double/decimal`（刻意不用 `IsPrimitive`：那会把 bool/char 也算进来，而 bool 归 `BoolStateGenerator`、char 保持文本更合适）。
- **范围读属性上已有的 `[RangeValidation(min, max)]`** —— 不需要属性再标一套，数值框自动带上上下限（`CoerceValue` 夹回来，而不是弹校验错误）。
- `DecimalPlaces`：整数类型 0，其余 -1。
- 只读属性走 `OneWay`（写回一个没有 setter 的属性会让绑定自己先炸）。

## 四、主题与注册

- `Themes\NumericBox.xaml`：模板 = 1px 描边圆角 4 的 Border + `PART_ContentHost` + 单位后缀；触发器含 悬停 / 聚焦（蓝边）/ 只读（灰底）/ 非法（红边红字）。
- **配色刻意与 `StructValueGenerator` 原来那套逐字一致**（`#DCDFE6` 描边 / `#606266` 字 / 只读 `#F5F7FA`+`#909399`）：
  它替掉的是网格里现有的输入框，换个皮肤会变成"只有数值格变了样"；这里要的是"数值格终于对了，但看不出换了控件"。
- 颜色不走令牌（与 LogConsole / StatusIndicator 等本层主题同一取舍：自包含、不依赖合并顺序）。
- `Generic.xaml` 第三层加一行合并；`PropertyGridDefaults.CreateGenerators()` 加 `new NumericGenerator()`。

## 五、验证证据

| 项 | 结果 | 口径 |
| --- | --- | --- |
| 行为断言（7 条，全 PASS） | ① 外部设 999→100、-5→0；② 敲 999 提交→100；③ `"12.5"`→12.5；④ 非法输入：值不动(7)+IsInvalid+文本退回；⑤ `DecimalPlaces=2` → `"1.50"`；⑥ **FlatPropertyGrid 里找出 4 个 NumericBox**（翻到第二个分组页后数 —— Tab 是懒加载的，只数当前页会漏）；⑦ `[RangeValidation(200,5000)]` → 框上 `Minimum=200 / Maximum=5000` | 探针 `_LogViewProbe.exe` |
| 渲染 | `docs/UI样式参考/numericbox.png`（六态样式图，落盘时回看过：单位后缀、只读灰底、非法红边、固定小数位都符合预期）；`pg_layouts.png` 重新出图确认**属性网格外观零回归** | 探针（图直接写进样式参考目录） |
| 冒烟回归 | `EXIT=ZERO`、四分区全过（Shell 全树实例化 / 多格布局 / GridView / LogConsole + 33 条 PASS），`NumericBox`/`PropertyGridDefaults` 零告警 | `UIThemeSmokeTest.exe` |
| 构建 | UI / VisionMaster / UIThemeSmokeTest 三工程 Debug 与 Release 均 `0 Error(s)`；`UI.dll`（Debug 23:45 / Release 23:51）与 `VisionMaster.dll`（Release 23:51）已刷新 | `dotnet build` |

> 探针里"模拟用户敲完离开"用的是**真的 `LostKeyboardFocus` 路由事件**
> （`RaiseEvent(new KeyboardFocusChangedEventArgs(...) { RoutedEvent = Keyboard.LostKeyboardFocusEvent })`），
> 所以提交路径（解析 → 钳制 → 回写 → 格式化）走的是生产代码那条路，不是测试专用后门。

## 六、另外三个新控件的落地判断（正面回答"还有吗"）

上一轮列的五个里，本次只落了 NumericBox。其余四个的现状与结论：

| 控件 | 结论 | 依据 |
| --- | --- | --- |
| **FilterableComboBox** | **暂缓** | 它的价值在"把选变量的模态弹窗压回行内"，但仓库里那三处选择器（`ScadaVariablePickerView` / 变量选择 / 扫描组）各自是完整对话框 + VM，改成行内是一次交互重构，不是加个控件。**现在加 = 又一个零消费控件** |
| **ConnectionStatusIndicator** | **可做，但要先定映射** | 落点已探明：`CommunicationSettingsView` 的"状态"列用 `DialogStatusPillTemplate`，而它**全仓只被这一处引用**（改它安全）。但控件在 UI 库、状态是通信域的 `ConnectionState` 枚举（UI 库不引用通信工程），所以要先把 `ConnectionState → 灯色` 的映射放到宿主侧（或给控件一个 `Level` 枚举 + 一个转换器）。属"下一步做"的活 |
| **CycleTimeGauge** | **暂缓（缺数据源）** | `MonitorViewModel` 里没有任何节拍/耗时字段（grep 过 `CycleTime`/`节拍`/`LastElapsed`）。控件的下游数据还不存在，做出来只能喂假数据 |
| **PresetBox** | **暂缓** | 要动 `AppConfig.json` 的存盘 schema（`AppSettingsService` + `AppConfigModel`），属配置线的活，不是纯控件 |

**我建议的下一个**：`ConnectionStatusIndicator`（落点已明确、改动面小、还能顺手把"保留但没接线"的 `StatusIndicator` 用起来）。
