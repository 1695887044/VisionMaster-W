# 2026-10-20 Plugin.CSharpScript 自定义视图 Web 化

## 1. 范围

- 设计基线：`docs/UI设计基线/csharp-script/README.md` v1 + `design.html`（自包含、可点击）
- 覆盖：`Plugins\Plugin.CSharpScript\CSharpScriptView.xaml`、`CSharpScriptView.xaml.cs`
- 不在范围：插件业务逻辑、动态端口重建、脚本引擎、帮助手册、共享控件/样式、宿主外壳、`UIThemeSmokeTest` 源码
- 参考实现：`Plugin.ImageAcquisition` V2（`docs/UI设计基线/image-acquisition/`）

## 2. 现状与耦合矩阵（SOP §5.1）

本插件无 HALCON 控件（`h:` 控件数 0）、无 `Mode`、无 `StatusLevel`；核心是「接口变量表 + AvalonEdit 编辑器 + 校验」。

| 区域/元素 | 当前绑定/契约 | 功能依赖 | 可重构级别 |
| --- | --- | --- | --- |
| 根布局 | `UserControl`，`MinWidth/MinHeight` 动态资源；`GetConfigView` 里 `DataContext=插件` | 宿主生命周期 | 保留根与 `DataContext` |
| 输入变量表 | `ItemsControl InputList`（原 code-behind 赋 `InputVars`）；`AddInput_Click` / `DelInput_Click` | 动态输入端口、持久化 | 只能包裹；改为 XAML `ItemsSource` |
| 输入变量行 | `TextBox(Name)`；`ComboBox(ScriptVarTypes, Display/Value)`；`LinkableValueEditor.Port`（`InputPortConverter` 多绑定 `Name + DataContext + PortsVersion`）、`StepData` | 端口链接 / 类型转换 / 手填 / 解除链接 | 只能包裹，不能替换 |
| 输出变量表 | `ItemsControl OutputList`；`AddOutput_Click` / `DelOutput_Click` | 动态输出端口、`StepData.OutputPortDefinitions` 快照 | 只能包裹；改为 XAML `ItemsSource` |
| 输出变量行 | `TextBox(Name)`；`ComboBox(ScriptVarTypes)`；`ComboBox(DisplayWindow, 0~9, SelectedIndex)`；`TextBox(Remark)`；`Visibility` 由 `IsIconic` 驱动 | 持久化、图形输出预览窗口 | 保留对象/索引绑定 |
| Context 速查 | 静态文本（`Context.GetInput/SetOutput/...`） | 仅文档 | 自由 |
| 编辑器 | `avalon:TextEditor x:Name="Editor"`，`ShowLineNumbers`、`TextChanged` | `ScriptText` 持久化 / 执行 | 保留控件与事件 |
| 编辑器初始化 | `Loaded`：`CSharpHighlighting`、`CSharpIntelliSense`、`ScriptEditorBehavior`、右键菜单、`Editor.Text` | 语法高亮 / 补全 / 编辑行为 | 保留；正文初始化提前到 `DataContextChanged` |
| 校验 | `Validate_Click` → `ValidateScript()` → `ValidationResult`；`ScriptEditorBehavior.SetErrorLines` | 编译检查 / 错误行标红 | 保留；增加视图级 `ICommand` 适配器 |
| 校验展示 | 原 `TextBlock.Text=ValidationResult`；code-behind 硬编码 `#2E7D32` / `Brushes.Red` | 反馈 | 只改外观；令牌化三态 |
| `Mode` / `SelectedItem` / `StatusLevel` | **本插件不存在** | — | N/A |
| HALCON 控件 | **本插件不存在** | — | N/A |

## 3. 功能契约保留

| # | 契约 | 状态 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView`（`GetConfigView` 返回 `new CSharpScriptView { DataContext = this }`） | 保留；构造签名未变 |
| C2 | 输入变量 `LinkableValueEditor`：`Port` 多绑定（`Name` + 插件 + `PortsVersion`）、`StepData`、`Label=""` | 保留；`InputPortConverter` 未改 |
| C3 | `avalon:TextEditor` + `ShowLineNumbers` + `TextChanged` + `CSharpHighlighting` + `CSharpIntelliSense` + `ScriptEditorBehavior` + 右键菜单 + 错误行标红 | 保留 |
| C4 | `ScriptText` / `InputVars` / `OutputVars`（`[StepConfig]`） | 保留；`TextChanged` 仍回写 `ScriptText`，集合仍直接绑定 |
| C5 | `ScriptVarTypes`（`StepConfigOption.ForEnum`）与 `ValidationResult` | 保留；类型下拉显示中文、值仍是成员名 |
| C6 | `AddInput_Click` / `AddOutput_Click` / `DelInput_Click` / `DelOutput_Click` / `Validate_Click` | 保留；只改外观与布局 |
| C7 | 输出 `DisplayWindow` int（0~9）与 10 个 `ComboBoxItem` | 保留；`SelectedIndex` 绑定未变 |

## 4. 改动点

1. **两栏布局**：左接口变量轨（`1*`，280~380，窄档 280）+ 右编辑器工作区（`1.7*`，MinWidth 280）；640 宽不再横向溢出。左轨内部 `ScrollViewer` 横向禁用，避免 `TextWrapping` 失效。
2. **变量行两段式**：第一行「名字 + 类型 + 删除」，第二行「`LinkableValueEditor`」或「显示窗口 + 备注」。`LinkableValueEditor` 仍是唯一链接入口。
3. **列表绑定移到 XAML**：`ItemsSource="{Binding InputVars}"` / `{Binding OutputVars}"`；空态由 `InputVars.Count == 0` / `OutputVars.Count == 0` 触发器驱动（虚线 `Rectangle` + 图标 + 引导文字），不再在 code-behind 手动切 `Visibility`。
4. **输出图形收敛**：`IsIconic == true` 才显示「显示窗口」块；非图形输出该列 `Auto` 宽度归零，备注占满。`DisplayWindow` 仍绑 int。
5. **编辑器标题栏与状态栏**：新增代码图标、标题、副标题与「校验脚本」主按钮；底部状态栏显示 `ValidationResult`，空串显示「尚未校验」，✔/✘ 分别绿/红。
6. **移除硬编码颜色**：删除 code-behind 的 `ValidateResultText.Foreground = ...`（`#2E7D32` / `Brushes.Red`）与 XAML 的 `Foreground="#666666"`；所有颜色走 `Dialog*` / `Plugin*` 公共令牌。
7. **视图级校验命令**：新增 `public ICommand ValidateCommand`（`DelegateCommand`），执行与「校验脚本」按钮同一 `RunValidation()`；满足批量冒烟「至少一条 `Button.Command` 绑定」的公共基线，不新增业务行为。
8. **正文初始化提前**：`DataContextChanged` 里灌入 `ScriptText`（只一次），保证批量冒烟/离屏渲染（不触发 `Loaded`）也能看到正文；`Loaded` 仍负责高亮、补全、编辑行为与右键菜单。
9. **不改共享文件**：`Colors.xaml`、`PluginConfigColors.xaml`、`PluginConfigShellView.xaml`、`PluginConfigStyles.xaml`、`LinkableValueEditor`、`UIThemeSmokeTest\Program.cs`/`.csproj`/`PluginViewSmoke.cs`、其它插件均未修改。
10. **工具提示统一（CS1）**：`CSharpScriptView.xaml` 的 `UserControl.Resources` 增加隐式 `<Style TargetType="ToolTip" BasedOn="{StaticResource DialogToolTipStyle}" />`，不再让 WPF 默认 ToolTip 样式漏进本视图。

## 5. 与 ImageAcquisition V2 的差异

| 维度 | ImageAcquisition V2 | CSharpScript v1 |
| --- | --- | --- |
| 主工作区 | 图像预览工作区（`ContentControl` + `ImageReadOnly`） | AvalonEdit 代码编辑器（浅色纸面） |
| HALCON | `ImageReadOnly` + `ContentControl` 按需创建 | 无 HALCON 控件（`h:` 数 0），不涉及折叠显隐 |
| 左栏 | 采集模式卡片 + 模式参数 + 输出 | 接口变量表（输入/输出）+ Context 速查 |
| 模式 | 4 个 `RadioButton` + `EnumConverter` 写 `Mode` | 无 `Mode`；变量类型下拉写 `ScriptVarType` |
| 状态 | `StatusMessage` + `StatusLevel` 三档 | 无 `StatusLevel`；校验状态由 `ValidationResult` 字符串派生（Idle/Ok/Error） |
| 预览工具条 | 适应窗口 / 1:1 / 刷新 | 无预览工具条；只有「校验脚本」 |
| 窄档 | Web 媒体查询隐藏副标题/状态标签 | WPF 无媒体查询，副标题 `TextTrimming`，状态标签保留 |
| 主题 | 深色预览画布 | 浅色编辑器（`CSharpHighlighting` 是浅色语法配色，未重做） |

## 6. 验证

| 项 | 命令 | 结果 |
| --- | --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.CSharpScript\Plugin.CSharpScript.csproj --no-dependencies` | **0 错误 / 0 警告**；`Modules\Plugin.CSharpScript.dll` 已刷新（144,896 字节，2026-10-20 02:10:43） |
| 批量插件视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.CSharpScript` | **11/11 PASS**：`GetConfigView` / `视图实例化 (elements=130)` / `DataContext=插件` / `LinkableValueEditor.Port (count=0，默认空变量)` / `Button.Command=ValidateCommand` / `ComboBox` / `TextBox` / `OnConfirm (InputValues=3)` / `XAML hex=0` / `渲染 PNG` |
| 变量行探针 | 临时离屏宿主，填 2 输入 / 2 输出 | `elements=335`、`LinkableValueEditor=2`、`ports=图像,阈值`；证明空态之外的 Port 多绑定可用 |
| 校验回环 | 默认脚本 → `ValidateCommand` | `✔ 脚本校验通过（编译无误）` |
| 错误回环 | 改坏脚本 → `ValidateCommand` | `✘ 第 1 行 第 29 列: 应输入 )`；编辑器第 1 行标红 |
| 设计稿自检 | 本地 HTTP 打开 `design.html`，点击状态/宽度/新增/校验 | 默认 2 输入 2 输出；空变量 0/0 + 两张空态；校验通过绿栏；校验失败红栏 + 第 12 行标红；640 档 `width=640px`；新增输入计数 2→3；校验按钮通过 |
| 证据 PNG | — | `docs/UI设计基线/csharp-script/evidence/after-render-v1-with-vars.png`、`...validation-ok.png`、`...validation-error.png`；批量初始态 `docs/UI设计基线/_batch-evidence/Plugin.CSharpScript-CSharpScriptPlugin.png` |

> **构建口径说明**：全量构建（带依赖）时共享 `UI` 工程被重建，输出 **142 条既有 nullable 警告**（`UI\Controls\Core\ElementExtention.cs` 等，非本插件代码）；本插件工程用 `--no-dependencies` 单独构建为 **0 错误 / 0 警告**。按主会话要求：**未构建 `UIThemeSmokeTest`**（共享工程由主会话构建）；本次冒烟使用 `--no-build`。插件工程构建已 0/0，且 `Modules` 产物已刷新。

## 7. 遗留问题

1. **批量冒烟的默认空变量**：`SmokeStepData` 没有 `InputVars` / `OutputVars`，所以公共冒烟报告 `LinkableValueEditor count=0`；变量行由临时探针验证（2 个 Port）。没有修改共享冒烟工程（按约束）。
2. **插件特定断言未进共享 gate**：`UIThemeSmokeTest` 的 `--plugin-views` 是公共基线，未新增 `--csharp-script` 专用模式（共享文件不可改）。建议 reviewer 重点核对：输入行 `Port` 多绑定、输出 `DisplayWindow` int、`ValidateCommand` 与右键菜单共用 `RunValidation()`。
3. **编辑器语法配色**：`CSharpHighlighting` 内部仍是硬编码浅色 XSHD 颜色（`#0000FF` / `#008000` 等），不属于视图 XAML；本轮未重做，换深色主题时需单独处理。
4. **设计基线待确认**：`csharp-script/README.md` v1 的确认日期/人仍为「待用户确认」；确认后按 R127 只读，改动升 v2。
5. **真机未验收**：需要用户重启 VisionMaster 后打开「C#脚本」配置窗口，验证新增/删除变量、链接/解除链接、类型切换、校验通过/失败、确认后重开回环。
6. **延后项**：加载骨架、编辑器字体/缩放、打开/保存脚本文件、代码格式化、变量重命名联动、试运行按钮（见设计基线 D1-D6）。

## 8. 回滚

- 回滚 `CSharpScriptView.xaml` / `CSharpScriptView.xaml.cs` 到本次提交前版本；设计基线 `docs/UI设计基线/csharp-script/` 可保留为 v1 参考。
- 重新构建 `Plugin.CSharpScript` 刷新 `Modules\Plugin.CSharpScript.dll`；无需回滚任何共享字典（本次未新增/修改公共令牌）。

## 9. 缺失令牌

**无。** 本插件全部颜色落在既有 `Dialog*` / `Plugin*` 语义域内；未新增颜色键，未修改共享字典。

## 10. C 组遗留修复（2026-10-20）

| ID | 修复 | 说明 |
| --- | --- | --- |
| CS1 | 视图资源增加隐式 `ToolTip` 样式 | `<Style TargetType="ToolTip" BasedOn="{StaticResource DialogToolTipStyle}" />`；构建 0/0，`--plugin-views Plugin.CSharpScript` 11/11 PASS，运行时探针确认 `Resources[typeof(ToolTip)]` 存在且 `BasedOn` 非空。 |

CS2（插件专属负向断言）属 TEST、CS3（Context.ShowImage 示例缩减）属 DOC，均未做。
