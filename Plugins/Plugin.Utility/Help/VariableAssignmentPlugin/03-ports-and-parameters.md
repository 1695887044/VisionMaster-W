# 变量赋值 · 端口与参数

> 分类：变量操作 ｜ 来源：插件 Plugin.Util（工程 `Plugin.Util.csproj`，C# 类 `VariableAssignmentPlugin`）

v3 的权威配置是**行列表 `Rows`**；旧版四个端口仍然保留，只用于旧 `.vms`、旧连线和旧脚本兼容。
这页逐条列端口、行字段、界面控件、提示原文与存盘键。

## 一、旧版输入端口（四个，兼容保留）

| 端口（源码属性名） | 端口名（旧存盘键） | 类型 | 默认 | 必填 | 现在还用吗 |
| --- | --- | --- | --- | --- | --- |
| `VariableName` | `Name` | `InputPort<string>` | `""`（空） | 否（显式 `IsRequired = false`） | 只在 `Rows` 为空时回退执行；迁移行用它承载旧「名字」连线提示 |
| `Value` | `Value` | `InputPort<object>` | `null` | 否 | **迁移行继续用它**承载旧「值」连线/旧常量 |
| `CreateIfNotExists` | `CreateIfNotExists` | `InputPort<bool>` | `false` | 否 | 只在 `Rows` 为空时回退执行 |
| `Scope` | `Scope` | `InputPort<string>` | `Runtime` | 否 | 只在 `Rows` 为空时回退执行；迁移时作为第 1 行的初始作用域 |

### 四条端口纪律（v1 踩过，v3 继续有效）

1. **四个口全部显式 `IsRequired = false`**。`InputPort.IsRequired` 默认是 `true`，而编译期的必填判据只看
   "有没有来源"（连线，或面板里亲手填过常量）——这几个参数天生就是给人手填的，留着默认值会让正常用法
   在流程编译时报 `[参数缺失] '步骤名' 的必填参数 'Name' 未配置！`，这个节点等于用不了。
2. **`Scope` 是 `string` 而不是枚举**。枚举经 `InputValues` 存盘会序列化成 int，读回来赋给端口时
   曾直接抛 `InvalidCastException: Invalid cast from 'System.Int32' to '枚举类型'`（方案一打开就炸）。
   `string` 两端无损往返，取值再做白名单校验（只认 `Runtime` / `Global`，忽略大小写）。
3. **为什么不自动路由**：同名时"运行时变量"与"全局变量"是两个不同的东西，自动路由要么产生歧义、
   要么用户看不出写去了哪。显式选一次，语义就定死。
4. **`Success` / `ErrorMessage` 复用基类端口，本类不自声明同名属性**。曾经自声明过，结果"影子隐藏"基类成员：
   插件写派生端口、引擎读基类端口，于是**永远报失败且错误日志为空**（CS0108 教训）。

## 二、每行的动态输入端口（v3 主体）

| 项 | 规则 |
| --- | --- |
| 端口名 | `Value_<RowId>`（新行）；**迁移行沿用旧 `Value`**（`UsesLegacyValuePort=true`） |
| 类型 | `InputPort<object>` |
| 必填 | 否（显式 `IsRequired = false`；否则正常用法编不过） |
| 生命周期 | 随行创建/删除；`RowId` 稳定，**改名不断线**；删行才失效 |
| 用途 | 只承载这一行的「值」：常量或上游链接 |
| 界面 | 每行一个 `LinkableValueEditor`（`Port` = 本行端口，`StepData` = 当前步骤） |
| 常量回填 | 重建端口后必须按 `ValuePortName` 把旧常量重新填回；漏了会表现为"配置打开后值全没了" |

**名字不是端口**：每行的变量名是 `Rows` 里的配置字段，不是 `InputPort`。所以名字行没有链接图标，
也不该挂通用「变量绑定」弹窗（见《名字与值口径》规则一）。

## 三、行字段（`AssignmentRow`，随方案存盘）

| 字段 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `RowId` | string | 新 GUID | 稳定身份；动态端口名 `Value_<RowId>` 的唯一锚点 |
| `VariableName` | string | `""` | 目标变量名；是"名字"，不是值 |
| `Scope` | string | `Runtime` | `Runtime` / `Global`；非法值由 `EnsureRowShape` 归一成 `Runtime` |
| `CreateIfNotExists` | bool | `false` | 仅运行时有效；切到全局会自动置 `false` |
| `ValuePortName` | string | `Value_<RowId>` | 本行「值」端口名；迁移行是 `Value` |
| `Enabled` | bool | `true` | 停用行不执行，但保留端口（避免连线键变成孤儿键）；界面暂未提供开关 |
| `UsesLegacyValuePort` | bool | 运行期判定 | `[JsonIgnore]`；`ValuePortName == "Value"` 时为 true |
| `LegacyNameLinkAddress` | string | `""` | `[JsonIgnore]`；旧「名字」连线地址，只用于提示 |

**为什么命名空间是 `Plugin.Util.Models` 而不是 `VisionMaster.Plugins.Util`**：
`InputValues` 是 `Dictionary<string, object>`，Newtonsoft 的 `TypeNameHandling.Auto` 会为集合写 `$type`；
方案反序列化的 `ConnectionConfigSerializationBinder` 只放行 `Plugin.*` / `VisionMaster.Models` /
`VisionMaster.Communications` / `VisionMaster.Scada`。放到 `VisionMaster.Plugins.Util` 会
「存得下、打不开」（整份 `.vms` 反序列化失败）。

## 四、配置界面的控件与绑定

| 界面元素 | 控件（源码） | 绑定/行为 |
| --- | --- | --- |
| 行列表 | `ListBox` + 行模板（宽/窄两套） | `ItemsSource=RowVms`；选中行 → `SelectedRow`；点行内控件也会选中该行 |
| 作用域 | 行内 `ComboBox`（运行/全局） | `SelectedValue=Row.Scope`；切到全局自动 `CreateIfNotExists=false`，候选/提示/模板立即刷新 |
| 变量名（全局） | `ToggleButton` + `Popup` | 显示当前名字/「选择全局变量」；浮层内搜索框过滤 `FilteredCandidates`；点击候选走 `SelectGlobalCandidateCommand`；失效项 `IsEnabled=False` |
| 变量名（运行时） | 可编辑 `ComboBox` | `Text=Row.VariableName`（`UpdateSourceTrigger=PropertyChanged`）、`ItemsSource=Candidates`、`IsTextSearchEnabled=False`；手填给「手填」徽标 |
| 值 | `cv:LinkableValueEditor` | `Port=Port`、`StepData=StepData`；常量/链接/解除都由共享控件完成 |
| 自动创建 | `CheckBox`（运行时）/ 锁图标（全局） | `IsChecked=Row.CreateIfNotExists`；全局显示锁 + tooltip |
| 操作 | 上移/下移/复制/删除 图标按钮 | 命令参数是 `AssignmentRowVm`；复制清空目标名；删除有值链接的行先出确认 |
| 右栏当前行 | 文本 + 徽标 | 第 N 行/共 M 行、作用域、`TypeText`、`Candidates.Count`、`ValueSourceText`、`Hint` |
| 顶部横幅 | 迁移横幅 / 错误横幅 | 迁移可「知道了」关闭；错误横幅「定位」跳到第一条错误行 |
| 640 窄档 | 行模板切换 | 视图 `SizeChanged` 写 `IsNarrow = ActualWidth < 700`；两行式，上移/下移隐藏 |

## 五、默认值与取值范围

| 项 | 默认 | 取值范围 | 改了影响什么 |
| --- | --- | --- | --- |
| 行数 | 0（旧配置迁移后 1） | 0 ~ 32（`MaxRows`） | 每行一个动态端口；到顶禁用添加/复制 |
| `Scope` | `Runtime` | `Runtime` / `Global`（字符串，忽略大小写） | 决定写哪个池、候选来自哪、失败消息长什么样 |
| `VariableName` | 空 | 任意非空变量名 | 目标变量；全局必须命中变量管理，运行时可手填 |
| `Value` | `null` | 常量文本，或连线取上游输出 | 类型要和目标变量的**声明类型**对得上（文本会被解析一次） |
| `CreateIfNotExists` | `false` | `true` / `false` | 仅作用域=Runtime 起作用；为 `true` 且变量不存在时**按当前值的类型新建** |

`Value` 给 `null`（没连线也没填）时：类型守门只针对非空值，`null` 会**原样下发**——
本地变量是内存赋 `null`、恒成功（但会污染下游条件求值）；网络变量要把 `null` 转成设备原始值，
多半在这一步失败并给出原因。两种作用域都不建议留空。

## 六、每行提示原文（`BuildHint`）

提示不是装饰：这条链上失败的两种方式（名字打错、值类型不符）在界面上都看不出原因，
所以把**声明类型**与**会怎么失败**当场说出来。`{}` 里是实际值。

| 情形 | 级别 | 文案原文 |
| --- | --- | --- |
| 还没填名字（全局） | Error（红） | `还没填名字。作用域=全局变量时，名字要来自变量管理（点右侧箭头选）。` |
| 还没填名字（运行时） | Error（红） | `还没填名字。作用域=运行时变量时，名字通常来自上游「变量定义」节点。` |
| 命中候选（全局） | Info（蓝） | `写入全局变量「{名字}」（{声明类型}）——「值」按它的类型解析（如 Int32 填 21）。` |
| 命中候选（运行时） | Info（蓝） | `写入运行时变量「{名字}」（{声明类型}）· {说明}。` |
| 同名但在另一个作用域（全局行） | Error（红） | `「{名字}」是运行时变量（{类型}），不在变量管理里 —— 全局作用域写不了它；请改选全局变量，或把作用域改成「运行时变量」。` |
| 同名但在另一个作用域（运行时行） | Warn（琥珀） | `「{名字}」是全局变量（{类型}），不是上游声明的运行时变量 —— 要写它请把作用域改成「全局变量」。` |
| 候选里找不到（全局） | Error（红） | `变量管理里没有「{名字}」：运行期会明确报「找不到全局变量」，不会静默新建。` |
| 候选里找不到（运行时，未勾自动创建） | Warn（琥珀） | `「{名字}」不在上游的变量定义里：运行期会报「本地变量 {名字} 不存在」；请在上游「变量定义」声明，或勾选「自动创建」。` |
| 候选里找不到（运行时，已勾自动创建） | Info（蓝） | `「{名字}」不在上游的变量定义里：已勾选「自动创建」，运行期会新建它（类型由值决定）；名字由你负责，拼错会新建另一个变量。` |
| 迁移行带旧「名字」连线 | 前缀 | `该行原由上游提供（{上游地址}）—— 在本行选/输一个变量名，即可改为手填。` |

最后一行**会拼在其它文案前面**：先说旧连线这件事，再说当前名字的状态。

## 七、存盘键与并行

- `.vms` 里的键：
  - `Rows`：行数组（JSON），v3 的权威配置；
  - 每行一个 `Value_<RowId>` 常量（未连线的行）；迁移行仍是 `Value`；
  - 旧四键 `Name` / `Value` / `CreateIfNotExists` / `Scope` 继续写（兼容旧版本/旧脚本）。
- 有连线的端口在确认时**移除固定值键**（运行时以连线为准），不会留下脏数据。
- 本插件标注了并行安全：运行时写走执行上下文的记账字典，全局写由配置界面把关。
  但**同一条并行链上多个实例写同一个全局变量**仍然是竞态，流程侧不该这么写。
- 编译器 `FlowCompiler.CollectAssignmentWrite` 会读 `Rows`，把每行目标加入
  runtime/global 写集合；同一节点多行写同一变量不会误报（按节点去重），跨节点并行写才会被拦。

## 八、旧配置迁移（`MigrateLegacy`）

`Rows` 为空且旧四键/旧连线存在时，首次打开/编译会生成第 1 行：

- `VariableName` = 旧 `Name`；`Scope` = 旧 `Scope`（只认 Runtime/Global，其它归 Runtime）；
- `CreateIfNotExists` = 旧值；`ValuePortName = "Value"`（沿用旧端口，旧「值」连线不动）；
- 旧「名字」连线地址记进 `LegacyNameLinkAddress`，只在提示里显示；用户在该行真实输入/选择名字时解除，
  或点确认时由 `RemoveLegacyNameLink` 解除；
- 顶部显示「已迁移旧配置」横幅，可「知道了」关闭。

迁移只发生一次：`Rows.Count > 0` 时不再迁移；旧四键仍保留在 `InputValues` 里做回退兼容。
