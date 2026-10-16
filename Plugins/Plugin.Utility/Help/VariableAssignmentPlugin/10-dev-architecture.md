# 变量赋值：架构、宿主契约与数据流

> 分类：变量操作 ｜ 来源：插件 Plugin.Util（工程 `Plugin.Util.csproj`，C# 类 `VariableAssignmentPlugin`）｜ 读者：开发

用户页讲的"怎么填"这里不重复，只讲**这些行为由谁提供、状态存在哪几份、跨程序集的那一跳怎么接**。

## 一、这条链上有四个地方，插件只占一个

| 位置 | 文件 | 职责 |
| --- | --- | --- |
| 插件层 | `Plugins\Plugin.Utility\VariableAssignmentPlugin.cs` | 端口、时机/作用域判定、候选与提示的界面语义、失败消息 |
| 插件视图 | `Plugins\Plugin.Utility\VariableAssignmentView.xaml(.cs)` | 五行表单；**唯一的例外职责**是上报"用户动了名字行" |
| 公共契约 | `Shard\Core.Interfaces\IGlobalVariableWriter.cs`、`IPluginConfigContextProvider.cs`、`VisionPluginBase.cs`、`IPluginCustomViewProvider.cs` | 插件够不到主程序，只能靠这几个接口跨过去 |
| 主程序侧 | `Core\Binding\GlobalVariableWriter.cs`（查表 → 类型守门 → 落值）、`Core\Models\Variable\NetworkVariableModel.cs`（设备侧原因）、`VisionMaster\Helpers\PluginVariableOptions.cs`（拼变量名快照） | 真正认识"变量管理"的是它们 |

**为什么必须分这么开**：插件 DLL 是独立程序集、只引用 `Core.Interfaces`，物理上读不到变量管理所在的集合与注册表；
所以"写全局变量"这件事的**查注册表 / 类型守门 / 落值**三步全在主程序侧，插件只负责把名字和值递过去、
把返回的中文原因原样写进 `ErrorMessage`。改动这三步的顺序或归属，等于改契约。

## 二、`RunAlgorithm` 时间线

| # | 代码动作 | 为什么必须在这一刻做 |
| --- | --- | --- |
| 1 | `VariableName.GetTypedValue()` / `Value.GetTypedValue()` / `CreateIfNotExists.GetTypedValue()` | 三个值**一次性取成局部变量**，整步用同一份；中途有人改常量框不会让"前半句按旧名、后半句按新名" |
| 2 | 名字空白 → `Success=false` + `变量名称不能为空` + `return` | 不 return 就会继续往下走，把错误消息洗成空（这条历史上真犯过） |
| 3 | `Scope.GetTypedValue()` 白名单判定（`Global` 忽略大小写；既不是 Global 也不是 Runtime 就报错） | **不能"不是 Global 就当 Runtime"**——拼错一个字母会把结果静默写到另一个变量池里，用户看到的是"赋值成功了但值不对" |
| 4 | 全局分支：`context.GlobalVariables` 取写入能力，null → `当前执行环境不支持写全局变量` | 插件不假设执行环境一定有工作区（容器/独立调用没有） |
| 5 | `writer.TryWrite(varName, value, out var writeError)`；失败 → 写 `ErrorMessage`（空则回落 `写全局变量 X 失败`）+ `Logger.Error` + `return` | 短路是必须的：不 return 会在下一行把 `ErrorMessage` 洗成空 |
| 6 | 成功 → `Success=true` + `Logger.Info($"{InstanceName} 全局变量 {varName} 已赋值")` + `return` | 日志行的实例名前缀是现场检索的锚点 |
| 7 | 运行时分支：`context.LocalVariables.ContainsKey(varName)` | 运行时变量池就是执行上下文的字典，本轮跑完即弃 |
| 8 | 已存在 + 类型不同 → `Convert.ChangeType(value, existing.GetType())`；成功写一条 WARN，失败显式报错并带 `值 [{值}]` | 池里的既有类型是**条件表达式的契约**；`double` 变量被塞 `string` 会让 If/While 每次求值都抛、所有分支集体不走而流程照常往下跑 |
| 9 | 不存在 + 勾了自动创建 → `LocalVariables.Add(varName, value)` | 新变量的类型由**当前值的类型**决定，所以第一次赋 `null` 会把变量建成"没有类型信息"的形态 |
| 10 | 不存在 + 没勾 → `本地变量 {名字} 不存在` + `return` | 同样必须短路 |
| 11 | 外层 `catch (Exception ex)` → `Fail(ex.Message)` 形态 + `Logger.Error($"{InstanceName} 变量赋值失败: {ex.Message}")` | 兜底：基类也会把未捕获异常转成失败，这里留一条自己的日志 |

## 三、状态有几份、谁是真源

| 状态 | 位置 | 真源？ | 说明 |
| --- | --- | --- | --- |
| 界面填的值 | `InputValues`（`.vms`） | **是**（持久真源） | `ApplyConfigValues` 灌进端口、`OnConfirm` 写回 |
| 端口的当前值 | `VariableName` / `Value` / `CreateIfNotExists` / `Scope` | 是（**界面这一行的真值来源**） | 候选重算不能改它；改了就必须顶回去 |
| 宿主变量快照 | `_configContext`（`PluginConfigContext.Variables`） | 否，**只读快照** | 由 `SetConfigContext` 注入；拿不到时候选留空、界面仍可手填 |
| 名字行候选 | `NameCandidates`（`ObservableCollection<string>`） | 否，派生 | 用会通知界面的集合：普通 `List` 清空重填不通知，换作用域后下拉一直是旧的 |
| 名字行提示 | `NameHint` | 否，派生 | 由"作用域 + 当前名字 + 快照 + 旧连线"四处算出来 |
| 旧连线地址 | `_legacyNameLink` | 否，一次性记录 | `Initialize` 时用 `stepData.GetLinkedAddress("Name")` 记下，**只给提示用**；解除发生在用户动手时 |
| 事件订阅幂等 | `_configEventsHooked` | 否 | `Initialize` 可能被多次调用（打开配置窗、试运行），订阅必须先判重 |
| 本插件自己 | **无业务字段**（没有静态字段、没有缓存） | — | 并行安全的前提 |

**只订 `Scope.ValueChanged`，绝不订 `VariableName.ValueChanged`**：端口值的来源不止"用户键入"——
试运行 `PluginTestRunner.BridgeInputs` 会把上游实际值灌进配置实例的同名端口。拿"值变了"当"用户改了名字"，
就会在那条路径上**误解除用户什么都没动的旧连线**（解除的是活模型，点"取消"也退不回来）。
用户编辑由视图经 `NotifyNameEditedByUser()` 显式上报。

## 四、白拿的宿主/基类行为（作者不用写、也别重写）

| 行为 | 由谁提供 | 本插件的表现 |
| --- | --- | --- |
| 元数据与工具箱条目 | `[Display(Name, GroupName, Description, ShortName)]` | 条目名「变量赋值」、分组「变量操作」 |
| 并行安全声明 | `[ParallelSafe]` | 流程可把它放进并行分组；语义是"运行时写走记账字典、全局写由界面把关"，**不等于同变量并发写安全** |
| 端口发现 | `VisionPluginBase.EnsurePortsDiscovered`（反射 public 属性） | 端口名由构造参数显式给定（`Name` / `Value` / `CreateIfNotExists` / `Scope`），与属性名不同 → 存盘键用**端口名** |
| 默认成功契约 | `Execute()` 进 `RunAlgorithm` 前预置 `Success=true`、清空 `ErrorMessage` | 所以成功分支只需写日志；失败分支必须显式 `Success=false` |
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
| `Name` | 输入端口 `VariableName`（端口名） |
| `Value` | 输入端口 `Value` |
| `CreateIfNotExists` | 输入端口 `CreateIfNotExists` |
| `Scope` | 输入端口 `Scope`（值是 `Runtime` / `Global` 字符串） |

本插件**没有 `[StepConfig]` 配置属性**——所有可配项都是端口，所以参数面板/自定义视图写回的都是上面四个键。
改端口名 = 老方案该项静默失效；改 `Scope` 的字面量 = 老方案的作用域全部落到"无法识别"分支（显式报错，不会静默）。

## 六、视图与插件的分工（唯一一处例外）

`VariableAssignmentView` 不写业务逻辑，数据全来自 `DataContext`（插件实例）暴露的端口与派生属性；
点确定时由主程序调插件的 `OnConfirm` 回写。唯一的例外是名字行那六个真实输入事件的采集
（键入 / 退格与删除 / `Ctrl+X` / 粘贴 / 命令预览里的剪切与删除 / 拖拽落文 / 下拉选中新增项），
统一转成一次 `NotifyNameEditedByUser()`。

三条实现细节值得记：

- 右键菜单的「剪切 / 删除」**既没有键盘事件也不走 `Pasting`**，必须用 `CommandManager.PreviewExecuted` 兜住；
  它只在**用户手势**触发命令时发，程序改文本不发——正合"用户意图只能由真实输入判定"。
- 拖拽落文要 `handledEventsToo: true`（`TextBox` 内部已把 `Drop` 标 Handled）。
- `SelectionChanged` 只在 `AddedItems.Count > 0` 时算编辑：候选重算会让 `SelectedItem` 变 `null`，
  那是"移除项"事件、不是用户在选。
