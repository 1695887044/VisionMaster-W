# 2026-10-20 ImageScript 自定义视图 Web 化

## 1. 范围

| 项 | 内容 |
| --- | --- |
| 插件 | `Plugin.ImageScript`（图像脚本） |
| Provider | `Plugins\Plugin.ImageScript\ImageScriptPlugin.cs`（`GetConfigView` 返回 `ImageScriptView`） |
| 视图 | `Plugins\Plugin.ImageScript\ImageScriptView.xaml` / `.xaml.cs` |
| 设计基线 | `docs/UI设计基线/image-script/README.md` v1 + `design.html` |
| 工程 | `Plugins\Plugin.ImageScript\Plugin.ImageScript.csproj`（未改） |
| 覆盖 | 插件内容区（左配置轨 + 右脚本工作区）；宿主外壳 `PluginConfigShellView` 不改 |
| 不在范围 | 业务算法、HDevEngine、端口名、命令名、持久化结构、帮助手册、共享样式/控件、其它插件 |

## 2. 耦合矩阵（改造前）

| 区域/元素 | 当前绑定/契约 | 功能依赖 | 可重构级别 |
| --- | --- | --- | --- |
| 根布局 | `UserControl` + `DataContext = 插件实例` | 宿主 `PluginConfigShell` 取 `IPluginConfigView` | 自由改布局；`DataContext` 不能换 |
| 过程管理 | `EditProcCombo` / `RunProcCombo` + `SelectionChanged` | 编辑目标写 `_editTarget.Body`；运行目标写 `SelectedProcedure` | 只改外观；下拉对象/事件保留 |
| 过程动作 | 6 个 `Click`：新建 / 删除 / 导入 / 导出 / 同步 / 恢复默认 | 过程集合、`.hdev` 导入导出、变量表同步、默认脚本替换 | 只改外观；处理器保留 |
| 输入变量表 | `ItemsControl x:Name="InputList"`，`ItemsSource` 由 code-behind `RefreshLists()` 唯一设置 | 变量行来自过程接口；`ObservableCollection` 增删触发端口重建 | 可重排为卡片；`ItemsSource` 保留在 code-behind |
| 输入类型 | `ComboBox SelectedValue="{Binding Type}"` + `ScriptVarTypes` | `ScriptVarDef.Type` setter → `RebuildDynamicInputs/Outputs` | 只改外观；`SelectedValuePath=Value` 必须保留 |
| 输入连线 | `LinkableValueEditor` + `Port` MultiBinding（Name + 插件 + `PortsVersion`） + `StepData` | 端口持久化、变量链接、解除链接、类型转换 | 只能包裹；`Port`/`StepData` 不能换 |
| 输出变量表 | `ItemsControl x:Name="OutputList"`，`ItemsSource` 由 code-behind `RefreshLists()` 唯一设置 | 输出端口快照 `StepData.OutputPortDefinitions` | 可重排为卡片；`ItemsSource` 保留在 code-behind |
| 输出显示窗口 | `ComboBox SelectedIndex="{Binding DisplayWindow}"`（0~9） | 图形类输出推送主界面视图窗口 | 只改外观；int 绑定不能换 |
| 脚本编辑器 | `avalon:TextEditor x:Name="Editor"` + `TextChanged` | 语法高亮 / 智能提示 / 行为 / 右键菜单 / 过程体回写 | 保留控件；只换外框 |
| 编辑器标题 | `x:Name="EditorTitle"`，代码写入 `GetProcedureMethod()` | 过程签名显示 | 保留 x:Name；只改样式 |
| 校验结果 | `ValidationResult` + `ValidateResultText` | `ValidateScript()` 的通过/失败文案与错误行标红 | 保留属性与 x:Name；只改配色/布局 |
| 状态 | 无 `StatusMessage` / `StatusLevel` | 本插件只有 `ValidationResult` | 不新增第四档；校验状态用三色呈现 |

## 3. 功能契约保留

| # | 契约 | 状态 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 保留；`GetConfigView` 未改，`ImageScriptPlugin` 继承 `VisionPluginBase`（实现 `IPluginConfigView`） |
| C2 | `InputPortConverter` MultiBinding（Name + 插件 + `PortsVersion`） | 保留；默认脚本下 `LinkableValueEditor` count=1、`Port.Name=Image`（冒烟实测） |
| C3 | `avalon:TextEditor x:Name="Editor"` + `TextChanged` | 保留；控件类型、事件、`HalconHighlighting`、`ScriptIntelliSense`、`ScriptEditorBehavior`、右键菜单构建全部未动 |
| C4 | `Procedures` / `SelectedProcedure` / `InputVars` / `OutputVars` | 保留；四个 `[StepConfig]` 属性未改名/改类型 |
| C5 | `ScriptVarDef.Type` / `DisplayWindow` | 保留；类型下拉仍写枚举，显示窗口仍绑 int（0~9），`IsIconic` 决定窗口下拉可见性 |
| C6 | 七个 Click 处理器 + `ValidationResult` | 保留；新建 / 删除 / 导入 / 导出 / 同步 / 恢复默认 / 校验（右键菜单）逻辑未改 |

预览工具条之外没有新增功能入口；工具栏「校验脚本」只是把既有 `Validate_Click` 包成 `ValidateScriptCommand`（`Prism.Commands.DelegateCommand`），属于纯 UI 入口。

## 4. 改动点

### 4.1 `ImageScriptView.xaml`（整页重写）

1. **两栏固定分工**：左栏 `1*`（Min 300 / Max 380），右侧脚本工作区 `1.7*`（Min 260）；根 `Margin="0"`，由宿主 `ContentPresenter` 提供 20,16。
2. **左栏卡片化**：背景 `DialogRowAlternateBrush`、描边 `DialogBorderLightBrush`、14px 内边距、`PluginCardCornerRadius`；内部按「脚本过程 / 输入变量 / 输出变量」分区，分区之间用 1px 分隔线。
3. **过程管理**：编辑目标 / 运行过程两个 36px `DialogCombo`；六个动作按钮改为 2 列 × 3 行 `UniformGrid`，34px 高；「恢复默认脚本」用 `DialogWarningTextBrush` 警示色，仍弹原确认框。
4. **输入变量**：每个变量一张小卡（`VariableCardStyle`），变量名 + 类型下拉（30px）+ `LinkableValueEditor` 独占一行；`Label=""`，`Port` / `StepData` / `PortsVersion` 原绑定。
5. **输出变量**：每个变量一张小卡，变量名 + 类型下拉 + 「显示窗口」下拉（仅 `IsIconic=True` 显示）+ 备注文本；`SelectedIndex="{Binding DisplayWindow}"` 原样。
6. **编辑器工作区**：52px 标题栏（图标 + 过程签名 + 副标题 + 「校验脚本」按钮）；`avalon:TextEditor` 放在带描边的容器内；底部 42px 校验状态条（状态点 + 文案），状态色由 `ValidationStateConverter` 驱动。
7. **编辑器空态**：`Procedures.Count == 0` 时在编辑器上覆盖「尚无脚本过程」说明（纯 WPF 覆盖层，不新增按钮；新建/导入/恢复默认入口仍在左侧动作区）。
8. **全令牌化**：视图内 0 个十六进制颜色；颜色全部映射 `Dialog*` 公共令牌（映射表见设计基线 README）。
9. **五态**：按钮模板包含 hover / pressed / disabled；`DialogFocusVisual` 焦点环；图标按钮 30×30 且 `Padding="0"`（R68）；ToolTip 走 `DialogToolTipStyle`。

### 4.2 `ImageScriptView.xaml.cs`（只加 UI 转换/处理器）

1. 新增 `ValidateScriptCommand`（`Prism.Commands.DelegateCommand`）→ 调用既有 `Validate_Click` 逻辑；`Validate_Click` 改为一行转发到 `ValidateScript()`。
2. 新增 `OnDataContextChanged`：`RefreshLists()` + 预选编辑/运行过程 + `LoadEditorText(CurrentProcedure)`。目的：断言宿主/预览器不触发 `Loaded` 时也能看到真实初始过程与脚本正文；正式运行仍由 `OnLoaded` 完成语法高亮、智能提示、编辑行为、右键菜单的挂接。
3. `OnLoaded` 在设置 `Editor.SyntaxHighlighting` 后补一次 `UpdateEditorIntel()`，保证 `DataContextChanged` 预置的接口变量高亮不会被覆盖。
4. 新增 `ValidationStateConverter`（空串→Idle；`✔`→Ok；`✘`→Error）与 `ValidationMessageConverter`（空串→「尚未校验…」）。只驱动视觉，不改 `ValidationResult` 字符串语义。
5. 删除原先在 `Validate_Click` 里直接 `new SolidColorBrush(...FromRgb(...))` / `Brushes.Red` 的硬编码颜色，改由 XAML `DataTrigger` + 公共令牌着色。
6. 未改：`InputPortConverter`、`CollectInterfaceVars`、`ParseErrorLines`、`BuildEditorContextMenu`、`InsertTemplate`、`Import_Click`、`Export_Click`、`NewProc_Click`、`DeleteProc_Click`、`SyncVars_Click`、`RestoreDefault_Click` 的业务逻辑。

### 4.3 设计基线与证据

| 文件 | 内容 |
| --- | --- |
| `docs\UI设计基线\image-script\design.html` | 自包含、内联 SVG、可点击切换 960/640 与默认 / 空态 / 校验失败 / 长列表四态 |
| `docs\UI设计基线\image-script\README.md` | v1 版本、范围、耦合矩阵、设计要点、令牌映射、契约保留、已知偏差、未纳入项 |
| `docs\UI设计基线\image-script\evidence\after-render-v1-default.png` | 由 `--plugin-views` 初始态渲染复制而来 |
| `docs\UI设计基线\_batch-evidence\Plugin.ImageScript-ImageScriptPlugin.png` | 冒烟自动生成的初始态 PNG |

## 5. 与 ImageAcquisition V2 的差异

| 维度 | ImageAcquisition V2 | ImageScript v1 |
| --- | --- | --- |
| 主工作区 | HALCON `ImageReadOnly` 预览（ContentControl 按需创建） | AvalonEdit `TextEditor` 脚本编辑器（普通 WPF 控件，常驻） |
| 模式/类型 | `Mode` 枚举、`SelectedCameraOption` 对象、`DisplayViewIndex` int | `Procedures` / `SelectedProcedure` / `InputVars` / `OutputVars`、`ScriptVarType` 枚举、`DisplayWindow` int |
| 变量入口 | 4 个 `LinkableValueEditor` 固定字段 | 每个输入变量行一个 `LinkableValueEditor`（动态端口，按 `PortsVersion` 重解析） |
| 状态 | `StatusMessage` / `StatusLevel`（Info/Warning/Error） | 只有 `ValidationResult`；状态条用 Idle / Ok / Error 三档视觉派生，不新增枚举 |
| 预览空态 | 深色画布 + 虚线空态卡 | 编辑器空态覆盖层（无过程时） |
| 预览工具条 | 适应窗口 / 1:1 / 目录刷新 | 仅「校验脚本」（既有 `Validate_Click` 的 UI 包装） |
| 新增令牌 | 10 个（5 个 Dialog + 5 个预览） | **0 个**；只复用既有 `Dialog*` |
| 页面结构 | 左配置轨 + 右预览工作区 | 左配置轨 + 右脚本工作区（同为 356px 左轨，但右侧是编辑器） |

## 6. 验证

### 6.1 构建

```text
dotnet build Plugins\Plugin.ImageScript\Plugin.ImageScript.csproj -v m
```

结果：**0 警告 / 0 错误**；`Modules\Plugin.ImageScript.dll` 已刷新（2026-10-20 02:08:30，447,488 字节）。

### 6.2 插件视图冒烟

```text
dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.ImageScript
```

结果：**全部通过**（11 项 PASS，0 FAIL）：

```text
[PASS] 找到待检插件 DLL  (count=1 filter=Plugin.ImageScript)
[PASS] Plugin.ImageScript.ImageScriptPlugin GetConfigView 返回 FrameworkElement  (ImageScriptView)
[PASS] Plugin.ImageScript.ImageScriptPlugin 视图实例化  (elements=224)
[PASS] Plugin.ImageScript.ImageScriptPlugin DataContext/IPluginConfigView  (ImageScriptPlugin)
[PASS] Plugin.ImageScript.ImageScriptPlugin LinkableValueEditor.Port  (count=1 ports=Image)
[PASS] Plugin.ImageScript.ImageScriptPlugin Button.Command 绑定  (buttons=7 commands=ValidateScriptCommand)
[PASS] Plugin.ImageScript.ImageScriptPlugin ComboBox 绑定  (DataContext.ScriptVarTypes/;EnumOptions/;DataContext.ScriptVarTypes/;DataContext.ScriptVarTypes/)
[PASS] Plugin.ImageScript.ImageScriptPlugin TextBox.Text 绑定  (PortValue,LinkAddress)
[PASS] Plugin.ImageScript.ImageScriptPlugin OnConfirm 可执行  (InputValues=5)
[PASS] Plugin.ImageScript.ImageScriptPlugin XAML hex=0  (xaml=1 hex=0)
[PASS] Plugin.ImageScript.ImageScriptPlugin 渲染 PNG  (...)
```

说明：
- `--no-build` 按派工单执行；**未构建 `UIThemeSmokeTest`**（共享工程，主会话已构建）。
- 冒烟的 `OnConfirm` 回环写入 `InputValues=5`：输入端口 `Image` + 四个 `[StepConfig]`（`Procedures` / `SelectedProcedure` / `InputVars` / `OutputVars`）。
- `XAML hex=0`：项目内仅 1 个 XAML（本视图），十六进制颜色计数为 0。

### 6.3 设计稿

- `design.html` 的脚本块通过 `node --check` 语法检查。
- 设计稿可点击切换：960 / 640 宽度；默认 / 空态 / 校验失败 / 长列表；过程下拉、类型下拉、校验按钮、动作按钮均有交互反馈。

### 6.4 未执行（按派工单）

- `FlowCanvasChecks` 常规 gate：未跑（本批只要求插件构建 + `--plugin-views`；共享 gate 由主会话统一跑）。
- 真机回环：未执行（需重启 VisionMaster 打开配置窗口，改字段 → 确认 → 重开）。

## 7. 遗留问题

1. **真机验收未做**：需在 VisionMaster 中打开「图像脚本」配置窗口，验证：过程下拉、变量表类型切换、连线/解除链接、导入/导出 `.hdev`、恢复默认脚本确认框、校验脚本错误行标红、确认后重开回环。
2. **工具栏「校验脚本」是新增的 UI 入口**：原来只有编辑器右键菜单「编译」。它包装的是同一 `Validate_Click` / `ValidateScript()`，没有新增校验逻辑；若用户认为不需要，可删除按钮和 `ValidateScriptCommand`，不影响任何契约。
3. **`OnDataContextChanged` 预置**：这是为了让断言宿主/预览器在 `Loaded` 不触发时也能看到真实初态。正式运行仍由 `OnLoaded` 完成高亮/智能提示/右键菜单挂接；如果未来宿主改为延迟设置 DataContext，需要确认不会覆盖用户未保存的编辑文本（当前 `DataContext` 只设置一次）。
4. **编辑器配色**：Web 设计稿的代码高亮色是演示色；WPF 使用既有 `HalconHighlighting` 的颜色定义，不逐字复刻。
5. **左栏滚动**：920×520 内容区下，输出变量区可能位于左栏滚动区下方；这是原视图 ScrollViewer 行为的延续。640×560 最小档需真机复核。
6. **未更新主索引**：按硬约束未修改 `docs/UI设计基线/README.md`（主会话统一维护）。
7. **未修改共享文件**：`Colors.xaml`、`PluginConfigColors.xaml`、`UIThemeSmokeTest\Program.cs` / `.csproj` / `PluginViewSmoke.cs`、`PluginConfigShellView.xaml`、其它插件均未动；本插件也**没有缺失公共令牌**（未新增任何令牌）。
8. **设计基线待用户确认**：`docs/UI设计基线/image-script/README.md` 当前为 v1，用户确认后冻结；后续调整升 v2。

## 8. 复跑命令

```text
dotnet build Plugins\Plugin.ImageScript\Plugin.ImageScript.csproj -v m
dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.ImageScript
```

> 说明：本次 `--plugin-views` 模式退出码为 **0**；完整默认冒烟的历史退出码为 1（既有 GridView 红灯，R46），以日志 `[PASS]` / `[FAIL]` 行为准。本次日志为「插件自定义视图批量冒烟：全部通过」。

## 9. C 组遗留修复（2026-10-20）

| ID | 修复 | 说明 |
| --- | --- | --- |
| IS1 | 根 `MinWidth` 560 → **576** | 列最小和 = 左 300 + 间距 16 + 右 260 = 576；选 576 而不是把右列压到 244，是因为右列 260 是脚本编辑器标题栏（图标 + 标题 + 校验按钮）与状态条的可读下限，继续压会先挤坏编辑器主工作区。576 仍小于 640 窗口的宿主内容区约 600。 |
| IS2 | 状态条 `Border MinHeight="42"` | 所在行仍是 `Height="Auto"`，长错误文案可换行撑高，不会被 42 截断。 |
| IS3 | 删除 `EditProcCombo` / `RunProcCombo` / `InputList` / `OutputList` 的 XAML `ItemsSource` 绑定 | `RefreshLists()` 成为唯一来源；`Procedures` 类型未改（仍是 `List<EProcedure>`）。 |
| IS4 | `OnDataContextChanged` 增加 `_dataContextApplied` 一次性 guard | 首次 DataContext 仍预置列表/选中/编辑器正文；后续 DataContext 变化直接 return，不覆盖用户正在编辑的内容。 |

**验证**：插件构建 **0 错误 / 0 警告**；`--plugin-views Plugin.ImageScript` **11/11 PASS**；临时 STA 探针额外验证 `MinWidth=576`、`EditProcCombo.Items.Count>0`、`InputList.Items.Count>0`、状态条 `MinHeight=42`、第二次 `DataContext` 赋值后 `Editor.Text` 仍为 `USER EDIT`。IS5（渲染证据只有 1 张）属 TEST，未做。
