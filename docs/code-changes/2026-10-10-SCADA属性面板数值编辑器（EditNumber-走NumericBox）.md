# 2026-10-10 SCADA 属性面板数值编辑器（EditNumber，走 NumericBox）

- 日期：2026-10-10
- 项目：VisionMaster（`Views\ScadaPropertyView.xaml`）+ UI（`NumericBox` 加一个属性）+ 探针 _LogViewProbe
- 范围：1 新模板 + 1 新样式（视图内）+ 1 触发器改指向 + 1 控件属性（`RevertOnInvalid`）
- 类型：A 项延伸（把 NumericBox 接到 SCADA 属性面板的数值行）
- 口径：**构建** = `dotnet build … -c Release`（个别构建带 `-p:ModulesDir=%TEMP%\vm_modules_skip\` 绕锁）；**SCADA 门禁** = `ScadaChecks.exe`；**探针** = `_LogViewProbe.exe -c Release`

---

## 一、侦察：数值那一档早就留好了位置

| 发现 | 内容 |
| --- | --- |
| 编辑器怎么挑模板 | `RowEditorHost`（ContentControl 的 keyed 样式）按行 VM 的 **`Kind`**（`ElementPropertyKind`：Text / Number / Bool / Color / Choice / Image）用 DataTrigger 选模板 |
| **`Kind=Number` 早就存在** | 只是当年和 Text 共用 `EditText`（裸文本框）—— 源码注释原话："Text / Number 同模板：现在两者都只是一个文本框。**将来给数值加上下调节按钮时再拆出去**，那时才是真的不同" |
| 行的契约 | `Value`（**字符串**编辑缓冲）+ `IsValid` + `ErrorText`（红框 + 悬停原因）；数值的**校验与提交归行 VM**（`ScadaPropertyRow.Commit` 对 Number 先预检，文本绑定 `UpdateSourceTrigger=LostFocus`） |
| 覆盖范围 | `ElementPropertyKind.Number` 既覆盖普通数值属性，也覆盖**几何键**（X / Y / Width / Height —— 见 `ScadaPropertyRow.Commit` 里的 Number 预检分支），所以这次一改全中 |

**关键冲突**：NumericBox 默认"非法输入就**把文本退回**上一个有效值"；而本面板的设计是"**保留用户敲的内容**、由行 VM 判定 IsValid 并给悬停原因"（它要让人在原处改）。两种都合理，但方向相反。

## 二、改法

1. **`NumericBox` 加 `RevertOnInvalid`（默认 true）** —— 置 false 时非法文本只标红（`IsInvalid`）而不退回。默认值保持原行为，五处既有接入方（属性网格 + 4 个插件表单）不受影响。
2. **`ScadaPropertyView.xaml`**：
   - 加 `xmlns:ui`；
   - 新增 `ScadaNumberBox` 样式（TargetType=NumericBox）：SCADA 配色（`ScadaPanelBorderBrush` / `ScadaDangerBrush` / `ScadaInvalidBackgroundBrush`）+ `RevertOnInvalid=False`；
   - **两条红框来源都接上**：① 行 VM 的 `IsValid=False`（打字母、越界）；② 控件自身的 `IsInvalid=True`（解析不出数字）。最终都由同一个 `ScadaDangerBrush` 标红，观感与原来一致；
   - 新增 `EditNumber` 模板：`ui:NumericBox`，**`Text` 仍绑 `Value`**（行 VM 的编辑缓冲）—— 提交 / 撤销 / 回填规范值那条链路一字未动，控件自身只负责步进与逐步钳制；
   - `Kind=Number` 的触发器从 `EditText` 改指 `EditNumber`。

## 三、验证证据

| 项 | 结果 | 口径 |
| --- | --- | --- |
| 构建 | UI + VisionMaster Release `0 Error(s)` | `dotnet build VisionMaster -c Release` |
| **SCADA 门禁（零回归）** | **通过 1690 / 失败 2** —— 与 `docs/code-changes/2026-10-06-…GridView…` 记录的基线**逐字相同**；那 2 条是 `ImageViewMode` 文档序列化，与本次改动无关 | `ScadaChecks.exe -c Release` |
| 探针（模板级，3 条新增全 PASS） | ① `EditNumber 渲染为 NumericBox，且文本接上行 VM 的 Value（编辑缓冲）` → `NumericBox=True，Text='23.5'`；② `RevertOnInvalid=False`；③ `行 VM 判非法（IsValid=False）时数值框标红`（BorderBrush = `#FFE05B5B`） | `_LogViewProbe.exe -c Release`（模板从视图内层 `Grid.Resources` 取 —— 编辑器模板挂在那里，`view.FindResource` 取不到） |
| 探针全量 | **22 PASS / 0 FAIL**（原 19 + SCADA 3） | 同上 |

## 四、未决与观察

- **真机验证待做**：需要先关掉 VS 2022 与运行中的 VisionMaster 再重建（`Modules\`/`UI.dll` 一直被它们占用，本轮继续用 `-p:ModulesDir=` 绕锁验证）。
- **步进的提交时机**：`Text` 绑的是 `LostFocus`（沿用面板既有口径）—— 上下键/滚轮改完的值要**失焦后**才写回行 VM。这是刻意的（与文本框行为一致），若希望步进即时提交，把该绑定改成 `PropertyChanged` 即可，但那会改变面板既有的"编辑中不落库"语义，需先确认。
- **不设 `Minimum/Maximum`**：行 VM 的 `ElementPropertyDescriptor` 没暴露范围，所以越界仍由行的校验报错（红框 + 悬停原因），**与改前一致**，不做钳制。
- **文本类编辑器（`EditText`）仍用裸 TextBox**：它承载字符串属性（含变量名、文本内容），没有数值语义，不属于本次范围。数值现在只走 `EditNumber`。
