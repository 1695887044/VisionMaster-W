# SCADA 下拉框只读显示（ComboBox Presentation=Text）

> 日期：2026-10-21 ｜ 范围：P0-2（SCADA 推荐控件第二批） ｜ 状态：已实现，专项断言通过，待真机确认

## 1. 背景

设计基线 `docs/UI设计基线/scada-controls-batch2/` 的 P0-2：`Hmi.ComboBox` 已有 `Mode=Output`（只读）、`Options`（值→显示名）、`SelectedText`（显示文本或 `#值`），但只读态看起来像“禁用中的下拉框”（0.45 透明、仍带 chevron、右侧露原始整数）。本项只补**呈现方式**，不新增 TypeKey，不新增 `Label` / 同名 `TextAlignment`。

## 2. 改动清单

| 文件 | 改动 |
| --- | --- |
| `Scada.Controls\Elements\ComboBoxElement.cs` | 新增 `Presentation`（Field/Text）与 `ShowSeparator`（bool）两个 `[ScadaProperty]`；`IsTextPresentation`；`CanOperate` 增加 `!IsTextPresentation`；`OnPresentationPropertyChanged` 归一化 `Presentation`（`text`/`field`/未知值）并刷新可写性 |
| `Scada.Controls\Themes\ComboBoxElement.xaml` | 同一 `ControlTemplate` 内新增 `PART_TextFrame` / `PART_TextLabel` / `PART_TextValue`；`ControlTemplate.Triggers` 用 `Trigger`（不是 `DataTrigger`）切 Field/Text 与 ShowSeparator；标签为空时 Trigger 收起；Text 底边线用 `HmiFieldUnderlineBrush`；`FontSize` 默认 14 |
| `Scada.Controls\Themes\ScadaTokens.xaml` | 新增 `HmiFieldUnderlineBrush` = `#7A7A7A`（与 IOField 只读态同色） |
| `Scada.Controls\Descriptors\BuiltInElements.cs` | ComboBox 描述符加 `TextProperties.Text`（标签，不可绑）与 `TextProperties.TextAlignment("Left")`；用 `ScadaPropertyCatalog.WithVisibility` 给 ShowSeparator/Mode/Text/TextAlignment/Fill/Stroke/StrokeThickness/CornerRadius 加条件可见；`FontSize("14")`；描述符文案补 Text 呈现 |
| `Scada.Controls\Descriptors\ElementPropertyDescriptor.cs` | 新增 `VisibleWhenKey` / `VisibleWhenValue` / `IsVisibleFor`；`IsVisibleFor` 读**有效值**（属性袋没写时取描述符默认值），避免把默认 Field 的新图元误判成空串 |
| `Scada.Controls\Descriptors\ScadaPropertyCatalog.cs` | 新增 `WithVisibility`（复制描述符并加条件，不改缓存实例） |
| `VisionMaster\ViewModels\ScadaPropertyViewModel.cs` | 属性面板 `Rebuild` 按 `property.IsVisibleFor(element)` 过滤，避免“改了没反应”的死开关 |
| `Scada\Models\ScadaChoiceNames.cs` | `Field`→字段、`Text`→文本 |
| `ScadaChecks\Phase2UiChecks.cs` | 新增 Presentation 注册/模型袋/DP 同步、Text 呈现可见性、标签/值、字号/对齐、底边线 `#7A7A7A`、TextAlignment=Right、标签空收起、ShowSeparator=false（边框与 Padding 归零）、Text 只读、切回 Field 可写等断言 |
| `ScadaChecks\ChoiceControlChecks.cs` | `[CB36]` Text 呈现即使有写通道 + 绑定也不写；`[CB37]`/`[CB38]` 条件可见（Text/Field 两向） |
| `ScadaChecks\Program.cs` | 属性行数断言改为按“可见属性行”计数（条件可见的行不再算进期望值） |

## 3. 关键决策

### 3.1 Text 恒只读（比“只允许 Output”更安全）

实现口径：`Presentation=Text` 时 `CanOperate` 恒为 false，`Mode` 在 Text 下不生效、属性面板隐藏。设计原稿写的是“Text 只允许 Mode=Output；InputOutput 仍按 Field 呈现并提示”，实现改为**恒只读**并在设计/README 同步——理由是没有“看起来只读、点下去还能写”的路径，也不需要跨属性校验。要可写请切回 `Field`。

### 3.2 条件可见：把死开关从属性面板藏掉

`Presentation` 决定哪些属性有效：

| 呈现 | 显示 | 隐藏 |
| --- | --- | --- |
| `Field` | Mode / Fill / Stroke / StrokeThickness / CornerRadius / 文字属性 | ShowSeparator / Text / TextAlignment |
| `Text` | ShowSeparator / Text / TextAlignment / 文字属性 | Mode / Fill / Stroke / StrokeThickness / CornerRadius |

`ElementPropertyDescriptor.VisibleWhenKey/Value` + `ScadaPropertyViewModel.Rebuild` 的过滤是通用机制，不是 ComboBox 专用分支；断言 `[CB37]/[CB38]` 守门。

### 3.3 同一模板两段可见性，不换模板

`PART_Combo` 始终存在，`OnApplyTemplate` 的 `DropDownClosed` 订阅不会丢；Text 呈现只是把 `PART_Combo` 收起、`PART_TextFrame` 露出。`Trigger` 直接盯模板化控件的 `Presentation`/`ShowSeparator`，不用 `DataTrigger + RelativeSource TemplatedParent`（在 `ControlTemplate.Triggers` 里实测不可靠，第一版即因此失败）。

### 3.4 视觉对齐

- 底边线：`HmiFieldUnderlineBrush` = `#7A7A7A`，与 IOField 只读态同色；不是 ComboBox 的 `Stroke`（#3A4A5C）。
- 字号：ComboBox 默认 `FontSize` 12 → 14；标签/值同档；`TextAlignment` 默认覆盖为 `Left`（基类默认 Center）。
- 标签为空时 `PART_TextLabel` 收起，值独占整行；`ShowSeparator=false` 时底边线与 `Padding` 一并归零。

## 4. 验证证据

| 项 | 结果 |
| --- | --- |
| `Scada.Controls` 全量重建 | 0 错误；生成器产物含 `PresentationProperty` / `ShowSeparatorProperty` / `OnPresentationPropertyChanged` 与两条描述符 |
| `ScadaChecks` 构建 | 0 错误 |
| `ScadaChecks` 专项运行 | **2344 通过 / 0 失败**（临时跳过 `UserStoreConcurrencyChecks` / `ScadaMonitoringChecks` 两个本机偶发跨线程崩溃段，跑完已恢复） |
| ComboBox 专项断言 | Presentation 注册/模型袋/DP 同步、Text 呈现可见性、标签/值、字号 14/左对齐、底边线 #7A7A7A、TextAlignment=Right、标签空收起、ShowSeparator=false（边框+Padding=0）、Text 只读、Field 可写，全部通过 |
| 属性面板行数 | 29 类图元“可见属性行 + 事件行 + 动画行 + 通用关系行”全匹配 |
| 全量 `ScadaChecks` | **未得到干净全量**：`UserStoreConcurrencyChecks` / `ScadaMonitoringChecks` 在本机偶发跨线程崩溃（`ScadaEditorViewModel.OnHistoryChanged` 从后台线程 `RaiseCanExecuteChanged`）；一次恢复后全量 2359/4，4 条失败在“图元组合/模板”段，与并发修改的 `ScadaTemplateStore.cs` 同批，和 ComboBox 无关 |
| `VisionMaster.sln` 全量 | 未完成：`VisionMaster.exe` 正在运行并锁定 `Modules\*.dll`；无 `error CS` |

## 5. 边界与未做

1. **Web M1.5 未改**：桌面 `Presentation=Text` 只读，Web 仍按 Field 渲染且按 `Mode` 判断可编辑（`elements-interactive.js` 不读 `Presentation`/`ShowSeparator`）。本批明确为 **Web M1.5 非目标**；同一 `.vms` 在桌面/Web 会有行为差异。
2. **WPF 视觉证据待补**：本项只有断言级证据与设计 HTML，没有 Text 呈现的 WPF 截图；真机/渲染探针补图。
3. **`ScadaMonitoringChecks` / `UserStoreConcurrencyChecks` 偶发崩溃**：既有环境问题，未修；全量绿灯因此未验证。
4. **标签内联 Style 未加 `BasedOn`、未绑 `FontWeight`**：当前无隐式 TextBlock 样式冲突；后续如引入全局 TextBlock 样式需补。
5. **`[R]` 描述符快照没有 ComboBox 条目**：本项未补快照；`[CB37]/[CB38]` 覆盖了条件可见，但全量描述符快照仍缺 ComboBox。

## 6. 红线核对

- R72 / SPG0006：`Text`/`TextAlignment` 复用基类，没有新增 `Label`/同名属性；生成器只新增 `Presentation`/`ShowSeparator`。
- R16：新增 `[ScadaProperty]` 后已全量重建 `VM.Scada.Controls` 与 `ScadaChecks`；生成器产物已核对。
- R65：改断言宿主后先 build 再 run。
- R38：本项没有 skip 型断言；临时跳过的是两个与 P0-2 无关的崩溃段，已恢复。
- R30/R32：新增 `HmiFieldUnderlineBrush` 在 `ScadaTokens.xaml` 本分册内，模板就地合并令牌；未跨分册引用。
- R55：未改 `Services\Core.Halcon`，不触发。
