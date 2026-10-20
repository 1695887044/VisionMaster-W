# 2026-10-20 SCADA 控件 Phase 2 落地（SelectorSwitch / TextField / ComboBox / AckButton）

## 范围

- 设计基线：`docs/UI设计基线/scada-controls/README.md` v1（推荐开发 P0）
- 覆盖：4 个新图元 + 主题 + UI 断言
- 不在范围：`Hmi.AlarmList` / `Hmi.MultiTrend`（P0 剩余两项，Phase 3）；P1/P2 候选

## 新增图元

| TypeKey | 控件 | 默认尺寸 | 核心契约 |
| --- | --- | --- | --- |
| `Hmi.SelectorSwitch` | 多档旋钮 | 120×36 | `Value:int`（0~3）可绑；`OptionCount` 2~4；`Option0..3Text`；`Mode` Output/InputOutput；点击档位写 int；`TrackColor` |
| `Hmi.TextField` | 文本输入域 | 160×32 | `Value:string` 可绑；`Placeholder`/`MaxLength`/`Mode`；Enter/失焦提交、Esc 取消；成功发 `InputCompleted` |
| `Hmi.ComboBox` | 下拉选择器（枚举型） | 160×32 | `Value:int` 可绑；`Options="0=手动;1=自动;2=检修"`；`Mode`；未知值显示 `#n`；只在 `DropDownClosed` 写变量 |
| `Hmi.AckButton` | 报警确认按钮 | 120×36 | `Scope` All/Oldest；`UnacknowledgedCount`/`CanOperate`/`CommandMessage`；All→`AcknowledgeAll`，Oldest→按 `ActivatedAtUtc` 取最早 |

## 关键实现

- `ChoiceOption`：`Value:int` / `Display:string`，`ToString()=>Display`，供 ComboBox 的 `OptionList` 使用。
- SelectorSwitch：`SelectAt(x)` 按 `x / ActualWidth * 档位数` 取档；`Select(index)` 走写通道 + 权限闸门；`EffectiveOptionCount` 收敛 2~4；模板 12 列网格让 2/3/4 档等分。
- TextField：`IsEditable`/`IsReadOnly`/`IsEditing`/`EditText`/`EditError`；提交 trim + `MaxLength`；空串合法（清除文本）；失败留在编辑态。
- ComboBox：`Options` 解析（`值=显示名;...`，非法项跳过、重复值保留第一条、空串不崩）；`SelectedText` 未知值 `#n`；`DropDownClosed` 写变量，`DropDownOpened` 记录打开值，打开后未改选不写。
- AckButton：订阅报警引擎三个事件刷新计数（Dispatcher 切回 UI 线程）；`All`/`Oldest`；权限不足时 `CanOperate=false` 并给中文原因。

## 主题 / 令牌

- 新增 `SelectorSwitchElement.xaml` / `TextFieldElement.xaml` / `ComboBoxElement.xaml` / `AckButtonElement.xaml`；`Generic.xaml` 只加 4 条合并项。
- 新增 `HmiSuccessFillBrush` #15803D（选中档/确认态实心，白字对比足够）。
- 所有主题就地 merge `ScadaTokens.xaml`，hex=0；`Border.CornerRadius` 统一走 `DoubleToCornerRadius`。
- ComboBox 自带深色模板，避免 Fluent 浅色 ComboBox 在深色画面变成白卡。

## 配套改动

- `Scada\Models\ScadaChoiceNames.cs`：追加 `All→全部`、`Oldest→最早一条`（既有“英文 Choice 必须有中文译名”断言要求）。
- `ScadaChecks\Program.cs`：Main 增加 `ChoiceControlChecks.Run()` / `Phase2UiChecks()`；内置图元名册追加 4 个 TypeKey（名册条数 = 注册表全量断言要求）。
- `ScadaChecks\ChoiceControlChecks.cs`：47 条功能断言。
- `ScadaChecks\Phase2UiChecks.cs`：38 条 UI 断言 + 4 张 PNG。

## 审核修正（主会话）

| 问题 | 修正 |
| --- | --- |
| SelectorSwitch 在写成功后 `RaiseScadaEvent(Pressed)`，而画布已对命中图元统一发 Pressed | 去掉控件侧 raise，事件源保持画布唯一；`[CB8]` 改为断言控件不重复发 |
| Selector 主题 Setter Fill 用 `HmiAccentBrush`（浅蓝 + 白字对比不足），与描述符默认 #15803D 不一致 | 新增 `HmiSuccessFillBrush` #15803D，主题 Setter 与描述符对齐；证据图重出为深绿 |
| AckButton `Oldest` 直接取 `ActiveAlarms.First()`，而该列表按严重度优先排序，不是“最早” | 改为 `OrderBy(a => a.ActivatedAtUtc).FirstOrDefault()` |
| ComboBox 打开下拉后未改选、直接关闭也会写一次变量 | `DropDownOpened` 记录打开值，`DropDownClosed` 发现未改选直接返回；直接调用测试入口仍按用户动作处理 |

## 验证

| 项 | 结果 |
| --- | --- |
| `dotnet build Scada.Controls\VM.Scada.Controls.csproj -v q` | **0 错误**（6 条既有警告） |
| `dotnet build Scada.Controls.Charts\VM.Scada.Controls.Charts.csproj -v q` | **0 错误 / 0 警告** |
| `dotnet build ScadaChecks\ScadaChecks.csproj -v q` | **0 错误**（既有可空性警告不计） |
| `dotnet run --project ScadaChecks --no-build` | **2077 通过 / 0 失败** |
| `dotnet build UIThemeSmokeTest` + `dotnet run --no-build` | **exit 0，无 `[FAIL]`** |
| `dotnet build FlowCanvasChecks` + `dotnet run --no-build` | **2797 通过 / 0 失败** |
| 渲染证据 | `docs/UI设计基线/scada-controls/evidence/after-phase2-{selector,textfield,combobox,ack}.png` |

## 已知边界

1. ComboBox 的 `Options` 仍是字符串映射表，不是可视化选项编辑器；复杂候选表可后续升级为列表编辑器。
2. SelectorSwitch 的按下反馈由“写值成功后选中档换色”承担，没有独立 `IsPressed` 动画（ContentControl 派生，避免假按压）。
3. AckButton 的 `Oldest` 按激活时间取最早，与报警条“严重度优先”的排序刻意分开。
4. TextField 的 `PART_Display` 是主题侧非编辑态显示块（代码只翻 `PART_Editor`/`PART_Placeholder`）；后续若改成代码翻显隐，需同步模板契约。
5. ComboBox `NotifyDropDownClosed` 是公开测试入口；直接调用时没有 `DropDownOpened`，仍按“用户动作”写一次（与真实模板路径区分）。

## 回滚

- 删除 4 个控件类 + `ChoiceOption.cs` + 4 个主题 + `Generic.xaml` 4 条合并项 + `ChoiceControlChecks.cs` / `Phase2UiChecks.cs` 调用。
- 回退 `BuiltInElements.cs` 4 条描述符、`ScadaChoiceNames.cs` 两条译名、`Program.cs` 名册 4 项、`ScadaTokens.xaml` 的 `HmiSuccessFillBrush`。
- 主题与控件类必须成对回滚，避免模板部件名悬空。
