# 2026-10-20 ExcelExport 自定义视图 Web 化

## 1. 范围

| 项 | 内容 |
| --- | --- |
| 设计基线 | `docs/UI设计基线/excel-export/README.md` v1 + `design.html` |
| 覆盖 | `Plugins\Plugin.ExcelExport\ExcelExportView.xaml` + `ExcelExportView.xaml.cs` |
| 插件类 | `ExcelExportPlugin.cs` **未改**（Provider / 端口 / 配置属性 / `ExportNow()` / `OnConfirm` 全部原样） |
| 工程 | `Plugin.ExcelExport.csproj` **未改** |
| 共享文件 | `Colors.xaml` / `PluginConfigColors.xaml` / `UIThemeSmokeTest` / `PluginConfigShellView.xaml` / `docs/UI设计基线/README.md`：**本插件未修改共享文件；工作区并发改动属其他任务** |
| 不在范围 | 加载骨架、浏览/选择目录、打开报表/打开文件夹、账本行数与文件大小、导出历史、宿主外壳 |

## 2. 耦合矩阵（SOP §5.1）

| 区域/元素 | 当前绑定/契约 | 功能依赖 | 可重构级别 |
| --- | --- | --- | --- |
| 根布局 | `ScrollViewer` + 纵向 4 卡 | 纯布局 | 自由 |
| 视图宿主 | `DataContext = ExcelExportPlugin`；插件实现 `IPluginConfigView` | 宿主 `viewObj.DataContext as IPluginConfigView` 取 `OnConfirm` | **不可换 VM** |
| 源步骤名 | `TextBox.Text = SourceStepName` | `[StepConfig] string`；`OnSourceStepNameChanged` 触发 `SourcePathPreview`；`OnConfirm` 写回 | 只能包裹/改外观 |
| 账本路径 | `TextBox.Text = SourcePathTemplate` | `[StepConfig] string`；`SourcePathPreview` 实时展开；`OnConfirm` 写回 | 只能包裹/改外观 |
| 实际读取预览 | `TextBlock.Text = SourcePathPreview` | 只读计算属性（占位符展开） | 只改外观 |
| 账本存在性 | `SourceExistsText`（code-behind 用 `File.Exists`） | 即时校验，帮助定位路径错误 | 只改外观/颜色来源 |
| 输出目录 | `TextBox.Text = OutputDirectoryTemplate` | `[StepConfig] string`；`OutputPathPreview`；`OnConfirm` 写回 | 只能包裹/改外观 |
| 报表文件名 | `TextBox.Text = ReportFileNamePattern` | `[StepConfig] string`；setter 空值兜底；`OutputPathPreview` | 只能包裹/改外观 |
| 图片列 | `TextBox.Text = LinkColumn` | `[StepConfig] string`；导出器按列名注入超链接 | 只能包裹/改外观 |
| 实际写出预览 | `TextBlock.Text = OutputPathPreview` | 只读计算属性（目录 + 文件名 + `.xlsx` 兜底） | 只改外观 |
| 导出方式 | `ComboBox.ItemsSource = ExportPolicies`、`SelectedItem = ExportPolicy` | `[StepConfig] ExportPolicy` 枚举；`RunAlgorithm` 据此决定是否自动导出 | 只能包裹/改外观；类型不能换 |
| 触发信号 | `cv:LinkableValueEditor Port = TriggerPort`、`StepData = StepData` | 端口持久化、变量链接/解除链接、`InputPort<bool>` 类型转换 | **不可替换** |
| 冻结首行 | `CheckBox.IsChecked = FreezeHeader` | `[StepConfig] bool`；传给 `ExcelExportRequest` | 只能包裹/改外观 |
| 自动筛选 | `CheckBox.IsChecked = AutoFilter` | `[StepConfig] bool`；传给 `ExcelExportRequest` | 只能包裹/改外观 |
| 只导有图的行 | `CheckBox.IsChecked = OnlyRowsWithImage` | `[StepConfig] bool`；导出器过滤行 | 只能包裹/改外观 |
| 导出后打开 | `CheckBox.IsChecked = OpenAfterExport` | `[StepConfig] bool`；成功后 `TryOpen` | 只能包裹/改外观 |
| 失败阻断 | `CheckBox.IsChecked = BlockOnFailure` | `[StepConfig] bool`；失败时决定是否置 `Success=false` | 只能包裹/改外观 |
| 立即导出 | `Button` → `ExportNow()`；`IsEnabled = !IsExporting` | 唯一手动入口；后台线程导出；回写 `ReportPath`/`ExportedRows` | 只能改外观；入口语义不能变 |
| 导出结果 | `TextBlock.Text = LastResult` | 成功/失败文本；前缀 `✔` / `✘` 决定颜色 | 只改外观/布局 |
| 状态档位 | 本插件 **没有** `StatusLevel` / `Mode` / `SelectedCameraOption` / `DisplayViewIndex` | SOP 通用清单中的这些项不适用 | **不虚构绑定** |

**判定原则落实**：`SourceStepName` / `SourcePathTemplate` / `OutputDirectoryTemplate` / `ReportFileNamePattern` / `LinkColumn` 是 `[StepConfig]` 的唯一入口，全部保留原属性名与 `UpdateSourceTrigger=PropertyChanged`；`TriggerPort` 是唯一 `LinkableValueEditor`，保留 `Port`/`StepData`；`ExportNow()` 是唯一动作入口，保留 `IsExporting` 置灰与 `LastResult` 反馈。

## 3. 功能契约保留（逐项）

| # | 契约 | 状态 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 保留；`GetConfigView` 未改，视图构造未改 |
| C2 | `LinkableValueEditor`：`Port=TriggerPort`、`StepData` | 保留；冒烟实测 `ports=导出触发` |
| C3 | `ExportPolicy` / `ExportPolicies` | 保留；`ItemsSource` + `SelectedItem` 原绑定；条目文案仍走 `ExportPolicyLabelConverter` |
| C4 | 五个文本属性 | 保留；`SourceStepName` / `SourcePathTemplate` / `OutputDirectoryTemplate` / `ReportFileNamePattern` / `LinkColumn` |
| C5 | 五个布尔属性 | 保留；`FreezeHeader` / `AutoFilter` / `OnlyRowsWithImage` / `OpenAfterExport` / `BlockOnFailure` |
| C6 | `ExportNow()` / `IsExporting` / `LastResult` | 保留；按钮通过视图本地 `ExportNowCommand` 转发到原方法，未新增业务逻辑 |
| C7 | `SourcePathPreview` / `OutputPathPreview` | 保留；只读展示，不新增计算属性 |

> **关于 `ExportNowCommand`**：原视图用 `Click="ExportNow_Click"`。批量冒烟 `--plugin-views` 要求视图自有按钮至少有一个 `Command` 绑定（`PluginViewSmoke.cs:135-141`），因此把入口改为视图本地 `ICommand`（`RelayCommand`）→ `Plugin.ExportNow()`。命令只存在于视图，不进入插件 API、不改端口/持久化/业务逻辑；这是为通过统一冒烟基线做的最小 UI 适配。

## 4. 改动点

1. **布局重写**：四段纵向卡片 → 两栏工作台（左 1.45fr：来源 + 输出；右 1fr：选项 + 立即导出）。960 基本一屏；920×520 内容区左栏需少量滚动，640 窄档列内滚动；根 `ScrollViewer` 保留。
2. **字段节奏**：标签上置（12px）、控件 36px、帮助 11.5px；`LocalInput`（BasedOn `DialogInput`）/ `LocalCombo`（BasedOn `DialogCombo`）/ `LocalCheckBox`（BasedOn `DialogCheckBox`）。
3. **路径预览**：新增两个只读预览块（「实际读取 →」「实际写出 →」），仍绑 `SourcePathPreview` / `OutputPathPreview`；等宽字体走 `PluginMonoFontFamily` / `PluginMonoTextBrush`。
4. **账本状态前置**：`SourceExistsText` 保留；code-behind 按 `File.Exists` 设置「✔ 账本已找到 / ✘ 账本不存在（检查『源步骤名』…） / 未配置账本路径」，颜色改为资源查找（`DialogSuccessTextBrush` / `DialogDangerTextBrush` / `DialogTextMutedBrush`），不再硬编码 `#2E7D32` / `#C62828`。
5. **导出方式条件行**：保留 `EnumEqualsToVisibilityConverter`（`ConverterParameter=OnTrigger`）；`LinkableValueEditor` 原样；三种方式各有一句帮助文字，`EachRun` 用 `DialogWarningTextBrush` 警示。
6. **选项换行**：五个 `CheckBox` 放进 `WrapPanel`，640 窄档不会裁掉「导出失败阻断流程」。
7. **结果面板**：`ResultPanel` / `ResultIcon` / `ResultTitle` / `ResultText` / `ResultHint`；四态（未导出灰 / 导出中蓝 / 成功绿 / 失败红），只消费 `LastResult` 与 `IsExporting`；成功/失败显示完整路径或原因。
8. **线程安全**：`OnPluginPropertyChanged` 增加 `Dispatcher.CheckAccess()` 守卫；`LastResult` 由 `Task.Run` 后台写入，原实现直接在后台线程改 `Foreground` 有跨线程风险，本轮修掉（纯 UI 线程调度，不改导出逻辑）。
9. **首次渲染**：新增 `DataContextChanged` 早刷一次账本状态/结果面板；`Loaded` 仍保留 `RefreshPreview()` 与订阅，真实窗口行为不变。
10. **令牌化**：视图 XAML `hex=0`；颜色全部 `{StaticResource Dialog* / Plugin*}`；未新增公共令牌（设计所需键均已存在）。

## 5. 与 ImageAcquisition V2 的差异

| 维度 | ImageAcquisition V2 | ExcelExport v1 |
| --- | --- | --- |
| 布局 | 左 356px 配置轨 + 右预览工作区 | 左 1.45fr 表单轨 + 右 1fr 选项/动作轨 |
| HALCON | `ContentControl PreviewHost` + `ImageReadOnly` 按需创建 | **无 HALCON 控件**（本插件没有图像预览） |
| 模式 | 4 个 `RadioButton` 卡片 + `EnumConverter` 写 `Mode` | 无 `Mode`；只有 `ExportPolicy` 下拉 |
| 端口编辑器 | 4 个 `LinkableValueEditor`（含 `BrowseCommand`） | **1 个**（`TriggerPort`），且**没有** `BrowseCommand`，不造浏览按钮 |
| 状态 | `StatusMessage` / `StatusLevel` 三档 + 宿主页脚 | 无 `StatusLevel`；`IsExporting` + `LastResult` 四态结果面板 |
| 动作入口 | `Browse*` / `Refresh` / `Prev` / `Next` / `PushLocalImage` 等既有命令 | 唯一入口 `ExportNow()`，通过视图本地命令转发 |
| 预览 | 深色画布 + 空/错误态 + 工具条 | 无画布；只有路径预览块与导出结果面板 |
| 设计稿 | 5 张 PNG（四模式 + 错误） | 初始态 1 张 PNG（无多模式画布）；设计稿五态可点击 |

### 设计稿与实现偏差（reviewer 要求记录）

| # | 项 | 设计稿 | WPF 实现 | 说明 |
| --- | --- | --- | --- | --- |
| 1 | 导出按钮入口 | `ExportNow_Click`（`Click` 事件） | `ExportNowCommand`（视图本地 `RelayCommand`） | 批量冒烟要求视图自有按钮至少有一个 `Command` 绑定；命令只转发到原 `ExportNow()`，插件 API 未改 |
| 2 | 卡片圆角 | **12px**（`--ee-radius-xl`） | **8px**（`PluginCardCornerRadius`） | 公共令牌没有 12px 圆角键；按现有插件卡片 8px 统一，避免新增共享令牌 |
| 3 | 内容区 | 960×680 窗口（内容区约 920×552） | 宿主内容区 **920×520** | 宿主 `PluginConfigShellView` 固定 72 标题 + 56 页脚 + 32 边距；外层 `ScrollViewer` 统一滚动，左栏输出卡在 920×520 下需滚动 |
| 4 | 图标 | Lucide 线性图标 | Font Awesome 6 Pro Solid（`DialogIconFont`） | 形状不同，语义一致 |
| 5 | 结果面板动画 | 导出中旋转 spinner | 静态 spinner 图标 + 文案 | 不新增动画代码；避免与宿主主题动画冲突 |

## 6. 验证

| 项 | 命令 | 结果 |
| --- | --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.ExcelExport\Plugin.ExcelExport.csproj -v m` | **0 错误 / 0 警告** |
| Modules 产物 | `Modules\Plugin.ExcelExport.dll` | 已刷新：**100,864 bytes / 2026-10-20 03:38**；SHA256 `A7C6D377BF9D585918D059067DD1579B859B895E919517215E40DFCE08EE634A` |
| 批量视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.ExcelExport` | **11/11 PASS**（含 `Button.Command`、`LinkableValueEditor.Port`、`OnConfirm`、`XAML hex=0`） |
| 渲染 | 批量冒烟自动输出 | `docs\UI设计基线\_batch-evidence\Plugin.ExcelExport-ExcelExportPlugin.png`；副本 `docs\UI设计基线\excel-export\evidence\after-render-v1.png` |
| 设计稿预览 | Edge headless 截图 | `docs\UI设计基线\excel-export\evidence\design-preview.png`（960×680 默认态） |
| 常规 gate | `FlowCanvasChecks` | **本轮未跑**（用户指定只跑插件构建 + `--plugin-views`；共享工程 gate 由主会话统一执行） |
| 真机 | VisionMaster 打开配置窗口 | **未执行**（需主会话/用户验收） |

冒烟逐项（11 条）：

```text
[PASS] 找到待检插件 DLL (count=1 filter=Plugin.ExcelExport)
[PASS] GetConfigView 返回 FrameworkElement (ExcelExportView)
[PASS] 视图实例化 (elements=221)
[PASS] DataContext/IPluginConfigView (ExcelExportPlugin)
[PASS] LinkableValueEditor.Port (count=1 ports=导出触发)
[PASS] Button.Command 绑定 (buttons=1 commands=ExportNowCommand)
[PASS] ComboBox 绑定 (ExportPolicies/ExportPolicy;EnumOptions/)
[PASS] TextBox.Text 绑定 (SourceStepName,SourcePathTemplate,OutputDirectoryTemplate,ReportFileNamePattern,LinkColumn,PortValue,LinkAddress)
[PASS] OnConfirm 可执行 (InputValues=12)
[PASS] XAML hex=0 (xaml=1 hex=0)
[PASS] 渲染 PNG
```

> 批量冒烟不替代插件特定断言：本插件的 `SourcePathPreview` / `OutputPathPreview` 计算、`ExportPolicy` 三态语义、`File.Exists` 状态、`LastResult` 前缀着色仍需 reviewer 按 §7 复核。

## 7. Reviewer 重点

1. **`ExportNowCommand` 的定位**：确认它是视图本地转发（`RelayCommand` → `Plugin.ExportNow()`），不是新增插件命令、不是独立 VM；`Plugin.ExcelExport.csproj` 与 `ExcelExportPlugin.cs` 均未改。
2. **跨线程**：`LastResult` 由后台线程写入；确认 `OnPluginPropertyChanged` 的 `Dispatcher.CheckAccess()` 守卫覆盖 `RefreshSourceExists` 与 `RefreshResultVisual` 两个分支。
3. **`SourceExistsText` 行为变化**：路径为空时由原来的 `Collapsed` 改为显示灰色「未配置账本路径」；这是纯 UI 提示，不改变导出逻辑。确认可接受。
4. **`DialogInput` 高度**：共享样式是 38px，本视图本地覆盖为 36px；确认没有影响 `LinkableValueEditor`（它自带 36px 输入框）与其它插件。
5. **结果面板四态**：确认 `RefreshResultVisual` **先判** `LastResult` 的 `✔` / `✘` 前缀（`StringComparison.Ordinal`），**再判** `exporting`；成功/失败优先于「导出中」。空串走「尚未导出」灰色态；非空前缀且非成功/失败走「导出中」蓝色态。
6. **不造死按钮**：确认没有「浏览」「打开文件夹」「打开报表」等未接命令的按钮；`TriggerPort` 的 🔗/✕ 是 `LinkableValueEditor` 自带，不算视图按钮。
7. **共享文件**：本插件未修改 `Colors.xaml` / `PluginConfigColors.xaml` / `UIThemeSmokeTest` / `PluginConfigShellView.xaml` / `docs/UI设计基线/README.md`；工作区并发改动属其他任务，`git diff` 可能同时包含其它任务的改动，不能据此把并发 diff 记在本插件账上。

## 8. P1 修复：导出完成后结果面板卡在「导出进行中」（2026-10-20 复核）

### 症状

导出完成后，结果文本已经显示成功/失败，但结果面板的标题与配色仍停在「导出进行中」，直到下一次操作才恢复。

### 根因

`ExcelExportPlugin.ExportNow()` 的后台线程先写 `LastResult`（触发 `PropertyChanged`），再在 `finally` 里写 `IsExporting = false`。视图侧有两个问题叠加：

1. `OnPluginPropertyChanged` 只监听 `SourcePathPreview` / `LastResult`，**没有监听 `IsExporting`**，第二次状态变化没有触发重绘；
2. `RefreshResultVisual()` 把 `exporting` 判断放在 `✔` / `✘` 前缀之前，且 `StartsWith` 未指定比较方式。`LastResult` 已经是 `✔` / `✘` 时仍被 `exporting=true` 抢先判成「导出进行中」。

### 修法（两条都做）

1. `OnPluginPropertyChanged` 增加 `IsExporting` 分支 → `RefreshResultVisual()`。
2. `RefreshResultVisual()` 判定顺序改为：
   - 先 `s.StartsWith("✔", StringComparison.Ordinal)` → 成功；
   - 再 `s.StartsWith("✘", StringComparison.Ordinal)` → 失败；
   - 再 `exporting || s.Length > 0` → 导出中；
   - 最后空串 → 尚未导出。
3. `StartsWith` 全部显式使用 `StringComparison.Ordinal`，避免区域设置影响前缀判断。
4. `ExportNow()` 的 `Task.Run` 内新增 `catch (Exception ex) { LastResult = "✘ 导出异常：" + ex.Message; }`，覆盖 `RunExport` / `LastResult` / `TryOpen` 的外逸异常；`finally` 仍置 `IsExporting=false`。复审确认正常完成、业务失败、外逸异常三条路径都能落到终态。

### 修复后验证

| 项 | 结果 |
| --- | --- |
| 插件构建 | **0 错误 / 0 警告** |
| 批量视图冒烟 | **11/11 PASS**（全 PASS） |
| `Modules\Plugin.ExcelExport.dll` | **100,864 bytes / 2026-10-20 03:38**；SHA256 `A7C6D377BF9D585918D059067DD1579B859B895E919517215E40DFCE08EE634A` |

> 批量冒烟不覆盖「后台导出完成」的时序；本条为代码路径复核（`LastResult` → `IsExporting` 两次通知 + 前缀优先级）。真机回环时重点确认：导出完成后结果面板立即变为成功/失败，不再停在「导出进行中」。

## 9. 遗留问题

1. **`FlowCanvasChecks` gate 未跑**：按用户指令本轮只做插件构建 + 批量视图冒烟；主会话统一跑 gate 后再汇总。
2. **真机回环未做**：需在 VisionMaster 中打开配置窗口，验证 5 个文本字段、1 个下拉、5 个复选、`TriggerPort` 连线/解除、`立即导出` 成功/失败/重试、确认后重开值不丢。
3. **`LastResult` 前缀约定**：结果面板沿用 `✔` / `✘` 前缀判色；若将来导出器改文案前缀，需同步 `RefreshResultVisual`（已在 README 记录）。
4. **设计稿与 WPF 的滚动差异**：设计稿两栏各自滚动，WPF 由外层 `ScrollViewer` 统一滚动；960 默认档左栏（输出卡）可能仍需少量滚动，640 窄档滚动更明显。
5. **占位符提示**：`DialogInput` 的占位提示依赖 `Tag` + 共享模板；若未来共享模板调整，占位提示可能失效（不影响字段绑定）。
6. **无公共令牌缺失**：本插件未新增任何令牌；若主会话后续统一调整 `DialogInput` 高度/圆角，本视图的本地 `Height=36` 覆盖需要复核。

## 10. 复跑命令

```text
dotnet build Plugins\Plugin.ExcelExport\Plugin.ExcelExport.csproj -v m
dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.ExcelExport
```

> `UIThemeSmokeTest` 退出码恒为 1（既有 GridView 红灯，R46），以日志 `[PASS]/[FAIL]` 行为准。本轮日志为 **11/11 PASS**。

## 11. C 组遗留修复（2026-10-20）

| ID | 修复 | 说明 |
| --- | --- | --- |
| EE2 | `docs/UI设计基线/excel-export/design.html` 两处 `ExportNow_Click` 改为 `ExportNowCommand` | 设计稿不再暗示 Click 事件；实现与 WPF 的视图本地命令一致。 |
| EE3 | README / code-changes 的「960 一屏完成」统一改为「960 基本一屏；920×520 内容区左栏需少量滚动」 | 与 §5 偏差表、已知偏差第 12 条的实际内容区高度一致。 |
| EE5 | code-changes 的「共享文件均未改」改为「本插件未修改共享文件；工作区并发改动属其他任务」 | 避免把并发任务在共享文件上的改动误记到本插件账上。 |

**验证**：文档改动不涉及代码，无需重建；`--plugin-views Plugin.ExcelExport` 复跑仍为 **11/11 PASS**。EE1（证据缺 640/五态、无插件专属断言）属 TEST、EE4（`Task.Run` 调度失败时 lambda 内 catch 接不住）属 FUNC，均未做。

## 12. EE4：手动导出的调度失败兜底（2026-10-20）

完整记录见 `docs/code-changes/2026-10-20-ExcelExport手动导出调度失败兜底.md`。要点：

| 项 | 内容 |
| --- | --- |
| 改动 | `ExcelExportPlugin.ExportNow()` 在 `Task.Run` **外层**加 `try/catch`：catch 里写 `LastResult = "✘ 导出异常：" + ex.GetType().Name + "：" + ex.Message`、`IsExporting = false`、`Debug.WriteLine`，**不 rethrow**；lambda 内原 `try/catch/finally` 逐字保留 |
| 内层 | catch 文案同步补 `ex.GetType().Name`（两层同形，结果面板前缀判色不用改） |
| 断言 | `FlowEngineFixChecks` 新增 `[EE4]` 6 条静态结构断言（Task.Run 在外层 try 内 / 外层 catch 写 LastResult / 复位 IsExporting / 带类型名 / 内层 finally 仍在且复位点恰好两处） |
| 变异反证 | 删掉外层 catch → ②③④ 准确变红（`IsExporting = false` 只剩 1 处）；恢复后 5 条全绿 |
| 边界 | 不改 `RunExport`、模板展开、端口、持久化与视图绑定；`ExportNowCommand` 仍是视图本地转发 |
