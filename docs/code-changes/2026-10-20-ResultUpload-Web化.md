# 2026-10-20 ResultUpload 自定义视图 Web 化

## 1. 范围

- 设计基线：`docs/UI设计基线/result-upload/README.md` v1 + `design.html`（自包含、可点击）
- 覆盖：`Plugins/Plugin.ResultUpload/ResultUploadView.xaml` + `ResultUploadView.xaml.cs`
- 不在范围：插件业务逻辑、端口名、命令名、持久化格式、`HttpPayloadBuilder`、`UploadQueue`、帮助手册、共享样式/共享控件
- 宿主外壳 `PluginConfigShellView.xaml` 未改；960×680 默认、640×560 最小档只作设计参考
- 本插件**没有 HALCON 控件、没有 `StatusLevel` / `StatusMessage`**；本版不伪造这两类绑定

## 2. 耦合矩阵（SOP §5.1）

| 区域/元素 | 当前绑定/契约 | 功能依赖 | 可重构级别 |
| --- | --- | --- | --- |
| 根布局 | `DataContext = ResultUploadPlugin`；`ScrollViewer + StackPanel` 单列 | 宿主靠 `DataContext as IPluginConfigView` 取 `OnConfirm` | 自由；保留 DataContext |
| 报文预设 | `ComboBox ItemsSource=PayloadKinds` + `SelectedItem=PayloadKind` | `OnPayloadKindChanged` 刷新 `PayloadPreview` / `IsFieldTable` / `IsNotify` / `IsRawJson`；`PayloadKind` 是 `[StepConfig]` | 只改外观，必须写 `PayloadKind` |
| 目标 URL | `TextBox Text=Url`（PropertyChanged） | `[StepConfig] string`；`BuildJob` 读取 | 只能包裹，不能替换 |
| 超时 | `TextBox Text=TimeoutMs` | `[StepConfig] int`；setter ≤0 拉回 3000 | 只能包裹，不能替换 |
| 请求头 | `ItemsControl ItemsSource=Headers`；Key `TextBox`；Value `PasswordBox` + `PasswordBoxHelper.BoundPassword`；`AddHeader()` / `Headers.Remove()` | MES 鉴权；持久化集合 | 保留集合与打码桥 |
| 字段表 | `ItemsControl ItemsSource=Fields`；`Enabled` / `Name` / `Source` / `VariableName` / `BuiltIn`；`AddField()` / `Fields.Remove()` | 字段名/来源/启用变化触发 `RebuildDynamicInputs()`（动态输入端口）；字段表也是 JSON 载荷 | 只能包裹/改外观；不换集合 |
| 通知模板 | `TextBox Text=MessageTemplate`（DingTalkText / WeComText 可见） | `HttpPayloadBuilder.Build` 展开文本 | 只能包裹 |
| 原始 JSON 模板 | `TextBox Text=RawJsonTemplate`（RawJson 可见） | `HttpPayloadBuilder.ExpandRawJson` | 只能包裹 |
| 时间格式 / UTC | `TextBox Text=TimestampFormat`；`CheckBox IsChecked=TimestampUtc` | 内置时间戳与 `{时间}` 占位符格式 | 只能包裹 |
| 报文预览 | `TextBlock Text=PayloadPreview` | 只读、与真实发送共用 `HttpPayloadBuilder` | 只改外观 |
| 上报时机 | `ComboBox ItemsSource=SendTimings` + `SelectedItem=SendTiming` | `RunAlgorithm` 分支：每次 / 仅 NG / 触发端口 | 只改外观，必须写 `SendTiming` |
| 触发端口 | `LinkableValueEditor Port=TriggerPort StepData=StepData` | 端口链接、变量绑定、解除链接 | 只能包裹，不能替换 |
| 仅 NG | `ComboBox IsEditable=True Text=JudgeFieldName ItemsSource=JudgeFieldNames`；`TextBox Text=NgValue` | `IsNg` 数值/布尔/文本三路比对 | 保留可编辑与候选来源 |
| 队列/失败 | `CheckBox IsChecked=AsyncMode` / `BlockOnFailure` / `SpoolEnabled` | 异步入队、阻断、断网续传 | 只改外观 |
| 重试/关键字 | `TextBox Text=RetryCount`（setter 0~10）；`TextBox Text=SuccessKeyword` | 重试退避、业务成功判定 | 只能包裹 |
| 测试发送 | `TestSend()`；`IsTesting`（置灰）；`LastResult` | 真发一条、后台线程、结果着色 | 保留方法与属性 |
| 最近日志 | `RecentLogs`（最多 50 条，`Time` / `Level` / `Message`）；`RefreshRecentLogs()` | 配置面板日志回看 | 保留数据源与刷新 |
| 状态 | `LastResult`（本插件没有 `StatusLevel`） | 成功/失败/未测试三态 | 不新增第四档 |

## 3. 功能契约保留（逐项）

| # | 契约 | 状态 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 保留；`ResultUploadView.xaml.cs` 构造未改，`GetConfigView` 仍 `new ResultUploadView { DataContext = this }` |
| C2 | `TriggerPort` 的 `LinkableValueEditor`（`Port` / `StepData`） | 保留；只在「上报时机=触发端口」显示，`Label=""` 外置标签 |
| C3 | `Fields` / `Headers` 两个 `ObservableCollection` | 保留；`AddField()` / `AddHeader()` / `Remove` 均走既有公开成员 |
| C4 | `PayloadKind` / `SendTiming` / `FieldSource` / `BuiltInField` 枚举；`TimeoutMs` / `RetryCount` int | 保留；`EnumConverter` 写枚举，setter 夹取规则未动 |
| C5 | `TestSend()` / `IsTesting` / `LastResult` / `RecentLogs` / `RefreshRecentLogs()` | 保留；「立即发送」「刷新」只转发 |
| C6 | `PasswordBoxHelper.BoundPassword` | 保留；请求头值仍打码显示、明文持久化（既有拍板） |

### 新增的视图级命令（不新增业务）

`ResultUploadView` 增加 6 个 `ICommand`，全部只转发既有公开方法：

| 命令 | 转发 |
| --- | --- |
| `AddFieldCommand` | `Plugin.AddField()` |
| `RemoveFieldCommand` | `Plugin.Fields.Remove(parameter as UploadFieldDef)` |
| `AddHeaderCommand` | `Plugin.AddHeader()` |
| `RemoveHeaderCommand` | `Plugin.Headers.Remove(parameter as HttpHeaderDef)` |
| `TestSendCommand` | `Plugin.TestSend()` |
| `RefreshLogsCommand` | 视图私有 `RefreshRecentLogs()` |

原因：通用插件视图冒烟要求"视图自身按钮至少有一条 `Command` 绑定"（参考 `Plugin.ResultDisplay` 的同类处理）；
`DataContext` 仍是插件实例，未引入独立 VM，`OnConfirm` 路径不变。

## 4. 改动点

1. **单列四卡片 → 两栏工作区**：左 280~340px 配置轨（报文预设 / 目标与连接 / 发送策略，可滚动），右报文工作区（上报内容 / 实际发出 / 测试发送）。
2. **报文预设改 2×2 卡片**：4 个 `RadioButton` + `ModeCardStyle`，`IsChecked` 仍走 `ui:EnumConverter` 写 `PayloadKind`；选中态主色描边 + 浅底 + 对勾。
3. **字段表升级为 5 列宽表**：启用 / 字段名 / 来源 / 取值 / 删除；来源=端口显示「画布上连线供值」，变量/内置显示各自输入；字段表空时显示虚线空态。
4. **报文预览独立为深色工作区**：`PluginPreviewCanvasBrush` 底 + `PluginMonoFontFamily`；顶部显示目标 URL（空时「未配置目标 URL」）与报文类型；`PayloadPreview` 以「（无法预览：」开头时红色显示。
5. **连接参数分层**：URL / 超时常驻；请求头、钉钉加签、证书校验收进「高级连接」`Expander`。
6. **测试发送闭环**：`IsTesting` 置灰按钮；`LastResult` 成功绿 / 失败红 / 未测试灰（背景同步走 success/danger subtle 令牌）；`RecentLogs` 与刷新按钮同卡。
7. **令牌化**：视图 XAML 零十六进制；`LogLevelColorConverter`、`RefreshResultColor` 改为 `Application.Current.TryFindResource` 取 `DialogDangerTextBrush` / `DialogWarningTextBrush` / `DialogTextMutedBrush` / `DialogSuccessTextBrush` / `DialogSuccessSubtleBrush` / `DialogDangerSubtleBrush` / `DialogRowAlternateBrush`，取不到时退回命名色（`Brushes.Firebrick` 等），保证无 `Application` 的断言宿主也不崩。
8. **五态**：本地 `LocalButtonStyle` / `LocalPrimaryButtonStyle` / `IconButtonStyle` / `RowRemoveButtonStyle` / `ModeCardStyle` 均定义 hover / pressed / focus / disabled；图标按钮 30×30 且 `Padding="0"`（R68），焦点 `DialogFocusVisual`，ToolTip `DialogToolTipStyle`。
9. **删除旧 Click 事件**：6 个按钮改绑视图级命令，行为不变。

## 5. 与 ImageAcquisition V2 的差异

| 维度 | ImageAcquisition V2 | ResultUpload v1 |
| --- | --- | --- |
| 主工作区 | 右侧图像预览（`ContentControl` + `ImageReadOnly`） | 右侧字段表 + 报文预览 + 测试发送；无 HALCON 控件 |
| 模式 | 4 个采集模式卡片（`Mode` 枚举） | 4 个报文预设卡片（`PayloadKind` 枚举） |
| LinkableValueEditor | 4 个（FilePath / FolderPath / FileIndex / FrameTimeout） | 1 个（`TriggerPort`，仅触发端口时机） |
| 预览 | `PreviewImage` + 空态/错误态 + Fit/1:1 | `PayloadPreview` 文本 + 深色代码工作区；无缩放 |
| 状态 | `StatusMessage` / `StatusLevel` 三档 | 无 `StatusLevel`；用 `LastResult` / `IsTesting` 表达测试结果 |
| 测试 | 无「测试发送」 | `TestSend()` + `LastResult` + `RecentLogs` |
| 左栏宽度 | 356px（`1*` 300~356） | 340px 上限（`1*` 280~340），给字段表更多宽度 |
| 可编辑下拉 | 无 | 判定字段 `IsEditable=True`，使用框架默认可编辑模板（`DialogCombo` 模板不含 `PART_EditableTextBox`，套上会丢失手填能力；见偏差 10） |

## 6. 验证

| 项 | 命令 | 结果 |
| --- | --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.ResultUpload\Plugin.ResultUpload.csproj -v m` | **0 错误 / 0 警告** |
| Modules 产物 | `dir Modules\Plugin.ResultUpload.dll` | **2026/10/20 04:05，145,408 字节**（与 `bin\Debug\net9.0-windows` 一致；M1-M3 复审修复后刷新） |
| 通用插件视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.ResultUpload` | **11/11 PASS**（全部通过） |
| 渲染 PNG | 同上（批量模式） | `docs/UI设计基线/_batch-evidence/Plugin.ResultUpload-ResultUploadPlugin.png` |
| 设计稿 | 本地 HTTP + 浏览器 | 已打开验证：默认态渲染正常；点击「钉钉文本」后 badge 变「值池」、预览切为 `msgtype=text`、通知模板出现；640 窄档类生效（宽度 640px） |
| FlowCanvasChecks | 未运行 | 本任务只要求插件构建 + `--plugin-views`；见遗留 |

冒烟 11 条明细：

1. 找到待检插件 DLL（count=1 filter=Plugin.ResultUpload）
2. `GetConfigView` 返回 `FrameworkElement`（ResultUploadView）
3. 视图实例化（elements=496）
4. `DataContext/IPluginConfigView`（ResultUploadPlugin）
5. `LinkableValueEditor.Port`（count=1 ports=上报触发）
6. `Button.Command` 绑定（6 个按钮，命令：RemoveFieldCommand / AddFieldCommand / TestSendCommand / RefreshLogsCommand）
7. `ComboBox` 绑定（SendTimings/SendTiming；JudgeFieldNames；FieldSources/Source；BuiltInFields/BuiltIn）
8. `TextBox.Text` 绑定（Url、TimeoutMs、NgValue、RetryCount、SuccessKeyword、Name、VariableName、MessageTemplate、RawJsonTemplate、TimestampFormat 等）
9. `OnConfirm` 可执行（InputValues=22）
10. `XAML hex=0`（xaml=1 hex=0）
11. 渲染 PNG

> `UIThemeSmokeTest` 退出码仍为 1（既有 GridView 红灯，R46）；本次以日志行判定为 **全部通过**。
> 未构建 `UIThemeSmokeTest`（按任务要求），运行的是现有二进制；批量模式通过 `Modules` 反射加载新 DLL。

## 7. 已知偏差

1. **图标字体**：Web 用内联 Lucide SVG；WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`）。
2. **字体度量**：Segoe UI / 微软雅黑与 Web 的字宽、行高略有差异。
3. **折叠区**：WPF 用默认 `Expander` 头（外加卡片边框），未复刻 Web 自绘 `details` header。
4. **字段表宽度**：640 最小档字段名/取值列压到较窄，信息仍可读；无横向滚动。
5. **日志列表**：WPF `ListBox` 行高略大，固定区域显示约 3 条；Web 设计稿为 54px 高度。
6. **请求头值**：WPF `PasswordBox`，Web `type=password`，打码行为一致。
7. **报文预览高亮**：WPF 单色等宽文本，未做 JSON 语法高亮（D3）。
8. **测试发送结果**：WPF 显示真实 `LastResult`；Web 用模拟数据。
9. **空字段态**：WPF 由 `Fields.Count == 0` 触发器显示虚线空态；添加字段后自动消失。
10. **判定字段下拉**：`IsEditable=True` 使用框架默认可编辑 ComboBox 模板；`DialogCombo` 模板没有 `PART_EditableTextBox`，套用会丢失手填能力，因此该控件不挂 `DialogCombo`，外观与其它下拉略有差异。
11. **无 HALCON 预览**：本插件没有 `ImageReadOnly` / `ImageEdit`，无需 `ContentControl` 宿主；SOP 的 HALCON 硬规则不适用。

## 8. 遗留问题

1. **设计基线待用户确认**：`result-upload/README.md` 标注"待用户确认"；确认后本页只读，改动升 v2。
2. **真机回环未做**：需要打开配置窗口 → 改 URL / 超时 / 字段 / 请求头 / 发送策略 → 确认 → 重开；并用真实 URL 点「立即发送」验证成功/失败与日志。当前环境无法发真实 HTTP。
3. **插件特定 UI 断言缺失**：`UIThemeSmokeTest` 是共享文件（任务禁止修改），本插件只跑通用 `--plugin-views`；没有像 ImageAcquisition 那样的 24 条插件专属断言（模式点击 / 字段增删 / OnConfirm 键值 / 测试发送状态）。
4. **`PayloadPreview` 不随字段名编辑即时刷新**：插件 `OnFieldDefPropertyChanged` 只通知 `JudgeFieldNames`，未通知 `PayloadPreview`（既有行为，本版未改业务逻辑）；用户改字段名后需触发其它预览相关变更才刷新。建议后续在插件侧补一条 `OnPropertyChanged(nameof(PayloadPreview))`。
5. **FlowCanvasChecks 未跑**：本任务只要求插件构建 + 通用冒烟；若进入批次 gate，需补跑 `dotnet run --project FlowCanvasChecks --no-build`。
6. **640 最小档未真机复核**：左栏滚动与字段表列宽在 600px 内容区下的实际观感需真机确认。
7. **无「加载骨架 / 发送进度条」**：`IsTesting` 只能置灰 + 文案，属于 D1 延后项。

## 9. 复跑命令

```text
dotnet build Plugins\Plugin.ResultUpload\Plugin.ResultUpload.csproj -v m
dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.ResultUpload
```

> 注意：`UIThemeSmokeTest` 退出码恒为 1（既有 GridView 红灯，R46），以日志 `[PASS]` / `### 插件自定义视图批量冒烟：全部通过` 为准。
> 若改了 `UIThemeSmokeTest` 源码，必须先 build 再 run（R65）；本次未改该工程。

## 10. 复审修复记录（M1-M3，2026-10-20）

reviewer 判定通过，但留下 3 条必改项；本轮只改本插件，未动业务/端口/命令/持久化/共享文件。

### M1：26 处裸 `ToolTip` 统一挂 `DialogToolTipStyle`

- 改动前：`ResultUploadView.xaml` 有 **26 处** `ToolTip="..."` 裸属性（其中 1 处是 `ToolTip="{Binding Message}"`）。
- 改动后：全部改为 `<X.ToolTip><ToolTip Style="{StaticResource DialogToolTipStyle}" Content="..." /></X.ToolTip>` 元素写法；
  连同原有 URL 输入框的 1 处样式 ToolTip，当前共 **27 处** `DialogToolTipStyle`。
- 静态复核：`findstr /c:"ToolTip=" ResultUploadView.xaml` **0 命中**（不再有裸属性）。
- 说明：`ToolTip` 元素写法与 ImageAcquisition V2 一致；未改任何 ToolTip 文案语义。

### M2：`SignSecret` 改为密码输入

- 改动：`TextBox Text="{Binding SignSecret, UpdateSourceTrigger=PropertyChanged}"` → `PasswordBox` +
  `local:PasswordBoxHelper.BoundPassword="{Binding SignSecret, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"`。
- 持久化：`SignSecret` 仍是 `[StepConfig] public partial string SignSecret`，**属性名/类型/默认值未变**；
  `PasswordBoxHelper` 只是把 `PasswordBox.PasswordChanged` 中转回 string 属性，`.vms` 仍是明文随方案保存（既有拍板）。
- 验证：临时反射检查 `typeof(ResultUploadPlugin).GetProperty("SignSecret").PropertyType == typeof(string)` →
  `SignSecretType=System.String signSecretIsString=True`。
- 说明：仓库没有 `DialogPasswordBox` 共享样式；沿用 `PasswordBoxHelper`（与请求头值同一方案），未新增共享控件。

### M3：`PayloadPreview` 在字段定义增删/改类型后即时刷新

- 只加通知，不改预览计算：
  - `Fields` setter：`OnPropertyChanged(nameof(PayloadPreview))`
  - `OnFieldDefPropertyChanged`：`Name` / `Source` / `Enabled` / `VariableName` / `BuiltIn` 变化时通知 `PayloadPreview`
    （前三个仍照旧触发 `RebuildDynamicInputs()` 与 `JudgeFieldNames`）
  - `OnFieldsCollectionChanged`：增删字段时通知 `PayloadPreview`
- 运行期验证（临时控制台工程，引用插件工程，未改仓库文件）：

```text
before={"sn":"(空)","result":"(空)","time":"2026-10-20 04:06:23.175"}
afterName={"sn2":"(空)","result":"(空)","time":"2026-10-20 04:06:23.195"}
nameApplied=True firedAfterName=1
afterSource={"sn2":"(空)","result":"2026-10-20 04:06:23.195","time":"2026-10-20 04:06:23.195"} firedAfterSource=2
firedAfterAdd=4 firedAfterRemove=5
SignSecretType=System.String signSecretIsString=True
RESULT: PASS
```

- 结论：改字段名后预览键名立即从 `sn` 变 `sn2`；改来源后内置值立即出现；增删字段均触发通知。

### 复审后复跑

| 项 | 命令 | 结果 |
| --- | --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.ResultUpload\Plugin.ResultUpload.csproj -v m` | **0 错误 / 0 警告** |
| 通用插件视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.ResultUpload` | **11/11 PASS**（日志“全部通过”） |
| M3 运行期验证 | 临时控制台工程（`%TEMP%\ru_m3_check`，引用插件工程） | **RESULT: PASS** |

### M4（遗留，未改）

插件专属断言仍为遗留：`UIThemeSmokeTest` 是共享文件，本任务禁止修改；没有新增 `--result-upload` 模式。
本插件目前只跑通用 `--plugin-views`（11 条），模式点击 / 字段增删 / OnConfirm 键值 / 测试发送状态等业务断言
建议后续由主会话统一在共享冒烟里补。
