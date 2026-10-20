# 2026-10-20 VariableAssignment 自定义视图 Web 化

## 1. 范围

- 设计基线：`docs/UI设计基线/variable-assignment/README.md` v1 + `design.html`（自包含、可点击、含 6 个状态）。
- 覆盖：`Plugins\Plugin.Utility\VariableAssignmentView.xaml` + `.xaml.cs`。
- 插件：`Plugin.Utility` / `VariableAssignmentPlugin`（**AssemblyName = `Plugin.Util`**，产物 `Modules\Plugin.Util.dll`）。
- 不在范围：`VariableAssignmentPlugin.cs`（业务逻辑/端口/持久化未改）、`Plugin.Utility` 其它插件（数学/逻辑/比较/类型转换走自动参数面板）、共享控件/共享令牌/宿主外壳/其它插件。

### 1.1 修改文件清单

| 文件 | 改动 |
| --- | --- |
| `Plugins\Plugin.Utility\VariableAssignmentView.xaml` | 整页重写：作用域卡片、变量名候选、提示条、值行、自动创建、右栏目标/规则卡 |
| `Plugins\Plugin.Utility\VariableAssignmentView.xaml.cs` | 保留构造与六类用户输入事件；新增 `ScopeEqualsConverter` / `ScopeLabelConverter` / `NameDisplayConverter` |
| `docs\UI设计基线\variable-assignment\design.html` | 新增 v1 设计基线（自包含、可点击、6 状态、960/640 宽档） |
| `docs\UI设计基线\variable-assignment\README.md` | 新增设计基线说明、令牌映射、契约保留、偏差、复刻证据 |
| `docs\UI设计基线\variable-assignment\evidence\after-render-v1-initial.png` | 初始态 WPF 渲染证据（批量冒烟产物副本） |
| `Modules\Plugin.Util.dll` | 插件工程重建产物（2026-10-20 01:40:50，252,416 字节） |
| `docs\code-changes\2026-10-20-VariableAssignment-Web化.md` | 本记录 |

## 2. 耦合矩阵（SOP §5.1）

| 区域/元素 | 当前绑定/契约 | 功能依赖 | 可重构级别 |
| --- | --- | --- | --- |
| 根 `UserControl` | `DataContext = VariableAssignmentPlugin`；宿主取 `IPluginConfigView.OnConfirm` | 配置保存、生命周期 | 自由布局；必须保留 DataContext 与构造签名 |
| 作用域 | `ComboBox SelectedValue="{Binding Scope.Value, Mode=TwoWay}"` + `SelectedValuePath=Tag`（Runtime / Global） | `Scope.ValueChanged` 重算候选与提示；`RunAlgorithm` 做白名单校验；`Scope` 是 `[StepConfig]` 输入端口 | **只能改外观**，必须写 `Scope.Value` 字符串；不能换枚举 |
| 变量名 | 可编辑 `ComboBox`：`ItemsSource=NameCandidates`、`Text=VariableName.Value`、`IsTextSearchEnabled=False` | 名字是「名字」不是「值」；候选来自宿主快照；选择落成常量；旧连线解除依赖真实输入事件 | **只能包裹**；`x:Name="NameBox"`、可编辑、候选、Text 绑定全部保留 |
| 变量名提示 | `TextBlock Text="{Binding NameHint}"` | 插件按作用域/名字/旧连线生成文案；提示里含目标变量类型与失败方式 | 只改容器与排版，文案来源不变 |
| 值 | `cv:LinkableValueEditor Port="{Binding Value}" StepData="{Binding StepData}"` | 端口持久化、常量/链接切换、类型转换、变量绑定弹窗、解除链接 | **只能包裹**；不能替换成普通 TextBox |
| 自动创建 | `CheckBox IsChecked="{Binding CreateIfNotExists.Value}"` | 运行时变量不存在时是否自动创建；全局作用域不使用 | 只改外观；必须保留 bool 端口 |
| 命令 | 本插件无自定义命令 | — | 不新增命令/按钮 |
| 状态 | 本插件无 `StatusMessage` / `StatusLevel` | — | 不新增状态枚举 |
| HALCON | 无（`h:` 控件 0 个） | — | N/A；本轮不涉及预览宿主 |

**结论**：这是一个纯表单型（S 级）插件；所有功能入口都集中在 4 个端口与 1 个共享编辑器上，UI 可重构但绑定不可换。

## 3. 功能契约保留（逐项）

| # | 契约 | 状态 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 保留；`GetConfigView` 仍 `new VariableAssignmentView { DataContext = this }`，视图构造签名未改 |
| C2 | `Scope.Value`（string，Runtime/Global） | 保留；作用域卡片经 `ScopeEqualsConverter` TwoWay 写回同一字符串 |
| C3 | `VariableName.Value`（string）+ `NameCandidates` + `NameHint` | 保留；`NameBox` 仍是可编辑 ComboBox，`Text`/`ItemsSource` 绑定原样 |
| C4 | `Value`（`InputPort<object>`）+ `LinkableValueEditor` 的 `Port` / `StepData` | 保留；只设 `Label=""`，控件内部不改 |
| C5 | `CreateIfNotExists.Value`（bool） | 保留；CheckBox TwoWay 绑定原样 |
| C6 | `NotifyNameEditedByUser()` 真实输入事件 | 保留；键入/退格/剪切/粘贴/拖拽/选择六类事件处理器逐字保留 |

## 4. 改动点

1. **整页重写 `VariableAssignmentView.xaml`**
   - 两栏布局：左「赋值设置」配置轨（作用域卡片 / 变量名 / 提示条 / 值 / 自动创建），右「目标变量 + 作用域规则」信息栏。
   - 作用域从裸下拉改为 2 张 56px 卡片（`RadioButton` + `ScopeCardStyle`），选中态主色描边 + 主色浅底 + 对勾。
   - 变量名行保留可编辑 `ComboBox`，候选与 `Text` 绑定不变；`NameHint` 移入浅主色信息条。
   - 值行保留 `LinkableValueEditor`；自动创建保留 `CheckBox` 并补一句作用域说明。
   - 右栏目标卡显示 `VariableName.Value` / `Scope.Value` / `NameCandidates.Count`；规则卡为静态语义说明。
   - 颜色全部走公共令牌；视图 XAML **hex=0**。
2. **`VariableAssignmentView.xaml.cs` 只加 UI 转换器**
   - `ScopeEqualsConverter`：`RadioButton.IsChecked`（bool?）↔ `Scope.Value`（string）；取消选中返回 `Binding.DoNothing`。
   - `ScopeLabelConverter`：Scope → 「运行时变量 / 全局变量」只读展示。
   - `NameDisplayConverter`：空变量名 → 「未命名变量」只读占位。
   - 原有六类真实输入事件处理器逐字保留，未改业务逻辑。
3. **设计基线**
   - 新增 `docs/UI设计基线/variable-assignment/design.html`（自包含、无外部依赖、内联 SVG、可点击；含默认/运行时已选/全局已选/跨作用域/找不到/旧连线 6 个状态）。
   - 新增 `docs/UI设计基线/variable-assignment/README.md`（版本、范围、设计要点、令牌映射、契约保留、已知偏差、未纳入项、复刻证据）。
4. **未改共享文件**：`Colors.xaml`、`PluginConfigColors.xaml`、`UIThemeSmokeTest\Program.cs` / `.csproj` / `PluginViewSmoke.cs`、`PluginConfigShellView.xaml`、`LinkableValueEditor`、其它插件。

## 5. 与 ImageAcquisition V2 的差异

| 维度 | ImageAcquisition V2 | VariableAssignment v1 |
| --- | --- | --- |
| 插件类型 | 采集配置（4 模式 + 预览） | 变量赋值（单表单） |
| 主布局 | 左 356px 配置轨 + 右预览工作区 | 左 `2*` 配置轨 + 右 `1*` 目标/规则栏 |
| HALCON | `ImageReadOnly` 按需宿主 + 空/错误态 | 无 HALCON 控件（h=0），不涉及预览宿主 |
| 模式选择 | 2×2 采集模式卡片 | 2 张作用域卡片（运行时 / 全局） |
| 路径/索引/超时 | 4 个 `LinkableValueEditor` | 1 个 `LinkableValueEditor`（`Value`） |
| 变量名 | 无 | 可编辑 `ComboBox` + `NameCandidates` + `NameHint` |
| 命令 | `Browse*` / `Refresh` / `Prev` / `Next` / `PushLocalImage` | 无自定义命令 |
| 状态 | `StatusMessage` / `StatusLevel` 三档 | 无状态属性，不新增 |
| 证据 | 5 张模式渲染 PNG | 1 张初始态 PNG（批量冒烟） |

## 6. 验证

| 项 | 命令 | 结果 |
| --- | --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.Utility\Plugin.Util.csproj -v m`（全量）与 `-v q`（最终增量） | **0 错误**；全量 143 个警告、最终增量 20 个警告，全部来自既有文件（`VariableAssignmentPlugin.cs`、`DataConversionPlugin.cs` 等）与依赖工程；本次改动的 `.xaml` / `.xaml.cs` **无新增警告** |
| Modules 产物 | `Get-Item Modules\Plugin.Util.dll` | 已刷新：2026-10-20 01:40:50，252,416 字节 |
| UIThemeSmokeTest 构建 | `dotnet build UIThemeSmokeTest\UIThemeSmokeTest.csproj -v m` | **0 错误**（798 个既有警告，来自全解决方案） |
| 通用插件视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.Util` | 当时 **8 PASS / 2 FAIL**（脚本路径/计数假设，见下）；**2026-10-20 遗留项修复后复跑 11 PASS / 0 FAIL** |
| 视图静态 | 正则扫描 `VariableAssignmentView.xaml` | `hex=0`；`NameBox` / `Scope.Value` / `NameCandidates` / `NameHint` / `CreateIfNotExists.Value` / `cv:LinkableValueEditor` 全部在位 |
| 初始态渲染 | 批量冒烟输出 | `docs/UI设计基线/variable-assignment/evidence/after-render-v1-initial.png`（同时有 `_batch-evidence/Plugin.Util-VariableAssignmentPlugin.png`；920×520，149 个元素） |
| 设计稿浏览器 | 本地 HTTP 打开 `design.html` | 渲染正常；默认态、全局已选态、640 窄档均已实机打开并截图（截图见会话记录） |

**8 条 PASS 明细**：DLL 可装载；`GetConfigView` 返回 `VariableAssignmentView`；视图实例化 149 个元素；`DataContext` 是 `VariableAssignmentPlugin`；`LinkableValueEditor.Port=Value`；`ComboBox` 绑定 `NameCandidates`；`TextBox.Text` 绑定 `PortValue/LinkAddress`；`OnConfirm` 可执行（InputValues=4）。

**2 条 FAIL 的原因（不改共享文件，按用户约束只报告）**：

1. `XAML hex=0`：`PluginViewSmoke` 用程序集名拼项目目录（`Plugins\Plugin.Util`），而本工程目录是 `Plugins\Plugin.Utility`；`Directory.Exists=false` → `xaml=0` 被判定失败。实际扫描 `Plugins\Plugin.Utility\*.xaml` 得到 **0 处十六进制颜色**。
2. `Button.Command 绑定`：本视图唯一的两个 `Button` 是 `LinkableValueEditor` 内部的 `PART_LinkBtn` / `PART_UnlinkBtn`（共享控件用 `Click` 事件）；本视图自身没有命令按钮。冒烟规则是“只要存在 Button 就至少有一条 Command 绑定”，因此误报。

> **FlowCanvasChecks 未跑成（环境问题，非本插件）**：`dotnet build FlowCanvasChecks\FlowCanvasChecks.csproj -v q` 连续两次在 `Plugin.DlClassify.csproj(63,5)` 的 `xcopy DLL\OnnxRuntime → VisionMaster\bin\Debug\net9.0-windows\` 步骤报 `MSB3073`（退出码 4）。该步骤与本插件无关，疑似目标目录被运行中的 VisionMaster/VS 占用；按约定未杀进程。gate 未得到可信结果，需主会话在宿主关闭后重跑。

## 7. 遗留问题

1. **通用冒烟脚本的两条假设**（见 §6）：`PluginViewSmoke.cs` 用程序集名当项目目录、把共享控件内部按钮计入命令绑定检查。需主会话决定是否修共享冒烟；本轮按约束未改。
2. **构建警告**：插件工程 143 个警告为既有基线（Nullable/依赖工程），本次改动的两个文件没有新增警告；若要求“0 警告”需另开清理任务，不应在本 UI 改动里顺手改业务文件。
3. **设计基线待用户确认**：`design.html` v1 已产出并复刻；确认后冻结，改动升 v2。
4. **WPF 渲染证据只有初始态 1 张**：批量冒烟只输出初始态；6 个状态的设计证据在 `design.html` 里可点击查看。若要 WPF 多状态 PNG，需要给 `UIThemeSmokeTest` 增加 `--variable-assignment-render`（共享文件，本轮不改）。
5. **640×560 最小档**：宿主 MinHeight 已统一 560；本视图 640 宽档按两列收窄，真机需复核左栏滚动与右栏文字换行。
6. **真机未验收**：打开配置 → 切换作用域 → 选候选 → 改值 → 确认 → 重开回环，需用户真机验证。
7. **未纳入**：变量浏览器/打开变量管理、值类型校验器/转换预览、加载骨架/运行日志/试运行可视化、深色主题、批量赋值（均需新增功能或契约）。

## 8. 回滚

- 回滚 `VariableAssignmentView.xaml` + `.xaml.cs` 到上一版即可；设计基线 `docs/UI设计基线/variable-assignment/` 保留为 v1 参考。
- 未新增公共令牌，无需回滚共享字典。
- `Modules\Plugin.Util.dll` 由插件工程重建；若需回滚，恢复上一版构建产物并重启宿主。

## 9. 遗留项修复（2026-10-20，VA1 / VA2 / VA3 / VA4）

- **VA1**：`NameBox.Background` 从 `DialogInputBackgroundBrush` 改为 `DialogSurfaceBrush`。
- **VA2**：作用域卡片（`ScopeCardStyle` 模板 Border）、提示条（`HintBorderStyle`）、自动创建卡三处圆角 8 → 10；图标 chip 的 8 保持不变。
- **VA3（低成本只读方案）**：新增 `NameCandidateItems`（`NameCandidateItem`：`Name` / `TypeName` / `Note`，与 `NameCandidates` 同源同序）；`NameBox` 的 `ItemsSource` 仍是 `NameCandidates`（字符串集合，`Text` 绑定不变），候选行用 `NameCandidateDisplayConverter` 显示「名字 + 声明类型」。选择落下的仍是名字常量，不改业务契约；若改为对象集合，需同步 `FlowCanvasChecks\VariableAssignmentCheck.cs` 的 `ItemsSource="{Binding NameCandidates}"` 静态断言。
- **VA4**：`ScopeLabelConverter` 对非空且非 Runtime/Global 的值返回「未知作用域（值）」；插件 `CurrentScope` 不再静默回退 Runtime（返回空串 → 无候选；`NameHint` 明确写「作用域取值无法识别」，运行期既有白名单校验照常报错）。校验逻辑未改。
- **帮助手册**：`Help\VariableAssignmentPlugin\03-ports-and-parameters.md` 的作用域控件描述由旧的「ComboBox + Tag」改为实际的两张 `RadioButton` 卡片，并补充候选行「名字 + 声明类型」；改过 Help 页后按 R62 对 `Plugin.Util` 做 `--no-incremental` 重建（`VariableAssignmentPlugin.Help.*` 资源名 6 个 = 该目录下 6 个 .md）。
- **验证**：`dotnet build Plugins\Plugin.Utility\Plugin.Util.csproj -v m` → **0 错误**（20 个既有警告，与改动前基线一致，改动的 `.cs`/`.xaml` 无新增）；`dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.Util` → **11 条 PASS / 0 条 FAIL**。
