# 变量赋值：架构、宿主契约与数据流

> 分类：变量操作 ｜ 来源：插件 Plugin.Util（工程 `Plugin.Util.csproj`，C# 类 `VariableAssignmentPlugin`）｜ 读者：开发

用户页讲的"怎么填"这里不重复，只讲**这些行为由谁提供、状态存在哪几份、跨程序集的那一跳怎么接**。
v3 的核心变化是"单值 → 行列表"，所以下面把行、动态端口、迁移与执行顺序分开写。

## 一、这条链上有五个地方，插件占两个

| 位置 | 文件 | 职责 |
| --- | --- | --- |
| 插件层 | `Plugins\Plugin.Utility\VariableAssignmentPlugin.cs` | 端口、行模型、迁移、候选/提示、执行聚合、失败消息 |
| 行模型 | `Plugins\Plugin.Utility\Models\AssignmentRow.cs` | 持久化的行字段（`RowId` / 名字 / 作用域 / 自动创建 / 值端口名） |
| 行视图模型 | `Plugins\Plugin.Utility\Models\AssignmentRowVm.cs` | 行模板的 DataContext：候选、提示、徽标、命令参数 |
| 插件视图 | `Plugins\Plugin.Utility\VariableAssignmentView.xaml(.cs)` | 行列表 + 右侧详情；**唯一例外职责**是上报"用户动了某行名字" |
| 公共契约 | `Shard\Core.Interfaces\IGlobalVariableWriter.cs`、`IPluginConfigContextProvider.cs`、`VisionPluginBase.cs`、`IPluginCustomViewProvider.cs` | 插件够不到主程序，只能靠这几个接口跨过去 |
| 主程序侧 | `Core\Binding\GlobalVariableWriter.cs`（查表 → 类型守门 → 落值）、`Core\Models\Variable\NetworkVariableModel.cs`（设备侧原因）、`VisionMaster\Helpers\PluginVariableOptions.cs`（拼变量名快照） | 真正认识"变量管理"的是它们 |

**为什么必须分这么开**：插件 DLL 是独立程序集、只引用 `Core.Interfaces`，物理上读不到变量管理所在的集合与注册表；
所以"写全局变量"这件事的**查注册表 / 类型守门 / 落值**三步全在主程序侧，插件只负责把名字和值递过去、
把返回的中文原因原样写进 `ErrorMessage`。改动这三步的顺序或归属，等于改契约。

## 二、`RunAlgorithm` 时间线（v3 行模式）

| # | 代码动作 | 为什么必须在这一刻做 |
| --- | --- | --- |
| 1 | `Rows.Where(r => r != null && r.Enabled)` | 停用行不执行，但端口保留（避免连线键变孤儿键） |
| 2 | 没有启用行 → `RunLegacy(context)` | 兼容旧 `.vms` 与旧脚本；旧四端口仍可单独工作 |
| 3 | 有启用行 → `RunRows(context, rows)` | 行顺序即执行顺序；某行失败不阻断后续行 |
| 4 | 每行取 `name/scope/create/value` 到局部变量 | 整行用同一份，中途有人改常量不会"前半句旧名、后半句新名" |
| 5 | `TryAssign(...)` 统一守门 | 名字空 → 作用域白名单 → 全局写入器 → 运行时类型转换/自动创建 |
| 6 | 成功 → 记 `successCount` + 一条 INFO 日志（带行号） | 日志行是现场检索的锚点：`第 N 行：本地/全局变量 X 已赋值` |
| 7 | 失败 → `failures.Add(label + error)` + 一条 Error 日志 | 行标签 `第N行「X」`；失败不 return，继续下一行 |
| 8 | 全部成功 → `Success.Value = true` | 基类默认也预置 true，这里显式写一次便于阅读 |
| 9 | 有失败 → `Success=false` + `ErrorMessage = "{N}/{M} 行失败：…"` | 最多列前 3 条，其余写「等 N 行」；再写一条整节点 Error 日志 |

`TryAssign` 的顺序（与 v1 相同，只是加了行上下文）：

1. `变量名称不能为空` → 失败；
2. `scope` 白名单（`Global` 忽略大小写；既不是 Global 也不是 Runtime 就报错）→ **不能"不是 Global 就当 Runtime"**；
3. 全局：`context.GlobalVariables` 取写入能力；null → `当前执行环境不支持写全局变量`；
   `writer.TryWrite` 失败 → 原样透出 `writeError`（空串回落 `写全局变量 X 失败`）；
4. 运行时：变量已存在 + 类型不同 → `Convert.ChangeType` 一次；成功写 WARN，失败显式报错；
5. 运行时：变量不存在 + `create` → `Add`；否则 `本地变量 X 不存在`。

## 三、状态有几份、谁是真源

| 状态 | 位置 | 真源？ | 说明 |
| --- | --- | --- | --- |
| 行列表 | `Rows`（`[StepConfig]`，随 `.vms` 存 `InputValues["Rows"]`） | **是**（持久真源） | 非空时以它为准；旧四端口只做兼容 |
| 行字段 | `AssignmentRow`（`RowId`/`VariableName`/`Scope`/`CreateIfNotExists`/`ValuePortName`/`Enabled`） | 是 | 改名不改 `RowId`，所以不断线；删行才失效 |
| 每行值端口 | `Inputs["Value_<RowId>"]`（迁移行是 `Value`） | 是（运行期值来源） | `LinkableValueEditor` 按 `Port.Name` 读写链接 |
| 界面行投影 | `RowVms`（`ObservableCollection<AssignmentRowVm>`） | 否，派生 | 行模板的 DataContext；`Rows` 集合变化时整体重建 |
| 当前选中行 | `SelectedRow` | 否 | 右栏详情卡绑定它；删行后按原索引就近选 |
| 宿主变量快照 | `_configContext`（`PluginConfigContext.Variables`） | 否，**只读快照** | 由 `SetConfigContext` 注入；拿不到时候选留空、界面仍可手填 |
| 行候选 | `AssignmentRowVm.Candidates` / `FilteredCandidates` | 否，派生 | 按作用域过滤；全局失效时首项是只读 `NameCandidate(IsInvalid=true)` |
| 行提示 | `AssignmentRowVm.Hint` / `HintLevel` | 否，派生 | 由"作用域 + 当前名字 + 快照 + 旧连线"四处算出来 |
| 旧名字连线地址 | `AssignmentRow.LegacyNameLinkAddress` | 否，一次性记录 | `[JsonIgnore]`；只给提示用，解除发生在用户动手/确认时 |
| 旧四端口 | `VariableName`/`Value`/`CreateIfNotExists`/`Scope` | 兼容真源 | `Rows` 为空时回退执行；迁移行继续用 `Value` 端口 |
| 删除待确认 | `PendingDelete` | 否 | 只有"该行值端口有链接"时才进入待确认；空行/纯常量行直接删 |
| 窄档标记 | `IsNarrow` | 否 | 视图 `SizeChanged` 写 `ActualWidth < 700` |
| 事件订阅幂等 | `_configEventsHooked` | 否 | `Initialize` 可能被多次调用（开配置窗、试运行） |
| 本插件自己 | **无静态业务字段** | — | 并行安全的前提；`RowVms`/`Rows` 都是实例状态 |

**`AssignmentRow.Scope` 的 setter 必须忽略空值**：作用域 `ComboBox` 在容器生成/回收时会把
`SelectedValue` 推成 `null`；若 setter 回落 `Runtime`，一条「全局」行会在打开窗口的瞬间静默变成
「运行时」行（真机表现为作用域显示"运行"、名字列却还是全局模板）。非法值由 `EnsureRowShape`
在加载/重建时统一归一，不要靠 setter 兜底。

**作用域投影要通知全**：`RefreshHint` 里除了 `Hint`，还要通知 `IsGlobal` / `IsRuntime` / `ScopeText` /
`CanCreate` / `ShowLock`。这些投影不在 `AssignmentRow` 上，只通知 `Row` 不会让模板切换——
会留下"作用域已切、名字列还是旧模板"的半旧状态。

## 四、白拿的宿主/基类行为（作者不用写、也别重写）

| 行为 | 由谁提供 | 本插件的表现 |
| --- | --- | --- |
| 元数据与工具箱条目 | `[Display(Name, GroupName, Description, ShortName)]` | 条目名「变量赋值」、分组「变量操作」 |
| 并行安全声明 | `[ParallelSafe]` | 流程可把它放进并行分组；语义是"运行时写走记账字典、全局写由界面把关"，**不等于同变量并发写安全** |
| 端口发现 | `VisionPluginBase.EnsurePortsDiscovered`（反射 public 属性） | 旧四端口由构造参数显式给定；动态端口由 `AddDynamicInput` 加入 |
| 动态输入端口 | `StepModel.AddDynamicInput` / `RemoveDynamicInput` | 每行一个 `InputPort<object>`，`IsRequired=false`；重建后要重新填常量 |
| 默认成功契约 | `Execute()` 进 `RunAlgorithm` 前预置 `Success=true`、清空 `ErrorMessage` | 成功分支只需写日志；失败分支必须显式 `Success=false` |
| 灌值 / 写回 | `Initialize(stepData)` → `ApplyConfigValues`；点确定 → `OnConfirm` | 视图打开时显示的是**已保存值**；有连线的端口在写回时**移除固定值键** |
| 自定义配置视图 | `IPluginCustomViewProvider.GetConfigView` | 不实现就回退通用「变量绑定」窗口——那正是这条链上所有故障的原点（名字行被当成值口） |
| 宿主快照透传 | `IPluginConfigContextProvider.SetConfigContext` | 宿主在配置窗口打开、视图挂进内容区**之后**调一次；实现方把值存进可绑定属性（DataContext 即插件实例） |
| 全局变量写入能力 | `IExecutionContext.GlobalVariables`（`IGlobalVariableWriter`） | 主程序侧实现做查表/守门/落值三步；插件只消费返回的中文原因 |
| 候选快照的组装 | `VisionMaster\Helpers\PluginVariableOptions.cs` | 名字/作用域/`TypeName`/`Description` 四个字段，**值不进场**（快照纪律：不让插件持有宿主方案里的活对象） |
| 非托管值轮首回收 | `AutoDisposeRoundOutputs`（默认 true） | 把 `HImage` 写进变量/输出端口时，释放权归生产者；透传场景自动跳过不误杀 |

**快照纪律要守住**：`PluginConfigContext` 是"照着当前配置拼一次"的只读拷贝，
不是活对象——直接给 `VariableDescriptor` 等于让插件在窗口关闭后仍能改宿主方案里的东西，
这类问题只在下次保存方案时以"配置怎么变了"的形式暴露。同理 `VariableOption.Scope` 的字面量与
本插件 `Scope` 端口**共用同一套字符串**（`Global` / `Runtime`），界面按它过滤候选；改字面量要同时改两边。

## 五、存盘键

| 键 | 来源 |
| --- | --- |
| `Rows` | `[StepConfig] ObservableCollection<AssignmentRow>`（JSON 数组；行字段见《端口与参数》） |
| `Value_<RowId>` | 每行的动态输入端口（未连线时写常量） |
| `Value` | 迁移行沿用的旧端口（旧连线/旧常量） |
| `Name` / `CreateIfNotExists` / `Scope` | 旧四端口（兼容旧版本/旧脚本；`Rows` 为空时回退执行） |

改 `RowId` 的生成规则 = 旧方案的动态端口名全部失效；改 `ValuePortName` = 该行旧连线失效。
`Rows` 里 `UsesLegacyValuePort` / `LegacyNameLinkAddress` 标了 `[JsonIgnore]`，不落盘。

## 六、视图与插件的分工（唯一一处例外）

`VariableAssignmentView` 不写业务逻辑，数据全来自 `DataContext`（插件实例）与行模板的
`AssignmentRowVm`；点确定时由主程序调插件的 `OnConfirm` 回写。唯一的例外是名字行的真实输入事件采集：
视图沿可视树上溯 `Tag="NameEditor"` 找到该行的名字编辑框，把六类真实输入
（键入 / 退格与删除 / `Ctrl+X` / 粘贴 / 命令预览里的剪切与删除 / 拖拽落文 / 下拉选中新增项）
统一转成一次 `NotifyRowNameEditedByUser(rowId)`。

三条实现细节值得记：

- 右键菜单的「剪切 / 删除」**既没有键盘事件也不走 `Pasting`**，必须用 `CommandManager.PreviewExecuted` 兜住；
  它只在**用户手势**触发命令时发，程序改文本不发——正合"用户意图只能由真实输入判定"。
- 拖拽落文要 `handledEventsToo: true`（`TextBox` 内部已把 `Drop` 标 Handled）。
- `SelectionChanged` 只在 `AddedItems.Count > 0` 时算编辑：候选重算会让 `SelectedItem` 变 `null`，
  那是"移除项"事件、不是用户在选。

**全局名字为什么不用可编辑 ComboBox**：v3 要求"只能选"，而可编辑 ComboBox 天然允许手填；
所以改成 `ToggleButton + Popup + ItemsControl`，按钮内容直接绑 `Row.VariableName`，
彻底避开 ComboBox 在 `ItemsSource` 重建时的选中时序问题。失效项用 `IsEnabled=False` 的候选按钮，
点击命令再判一次 `candidate.IsInvalid`。

**窄档不是第二套视图**：同一份 XAML 用 `IsNarrow` 切换行模板（宽档单行、窄档两行式）；
视图 `SizeChanged` 只写一个布尔，不重建控件树。

## 七、并行安全与编译器

- `[ParallelSafe]` 只声明"本插件实例无共享可变状态"；**同一并行分支里两个节点写同一个全局变量仍是竞态**，
  流程侧不该这么写。
- `Engine\FlowCompiler.cs` 的 `CollectAssignmentWrite` 先读 `InputValues["Rows"]`：
  每行按 `VariableName`/`Scope`/`Enabled` 加入 runtime/global 写集合，用 `list.Contains(step)` 防同节点多行误报；
  `Rows` 为空才回退旧 `Name`/`Scope`。这样并行写冲突检查对 v3 行模型仍然生效。
- 动态端口名只由 `RowId` 决定，所以改行名不会改变端口名；并行检查看的是**变量名**，不是端口名。
