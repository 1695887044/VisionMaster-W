# 2026-09-17 绑定解析器内核（VariableRegistry）—— SCADA S0-c

## 一、需求与决策

### 背景
S0-a 把「变量稳定身份」建了起来（`IVariable.VariableId` 随方案落盘、旧方案补发），但当时留了一句
「**Id 目前尚无消费者**：连线、监视项、SCADA 绑定仍按变量名寻址，改名仍会断链」。

S0-c 要做的就是给 Id 找到**第一个消费者**，并把"按名寻址"这件事从散装状态收成一处。改之前，全工程至少有三套各自为政的按名寻址：

| # | 位置 | 寻址方式 |
|---|---|---|
| ① | `FlowCompiler` 编译全局变量连线 | `GlobalVariables.FirstOrDefault(v => v.Name == link.TargetPortName)`，每次 O(n) |
| ② | 监视栏 | `WatchItemModel.GlobalVariableName` + 用户手输的 `"Global.xxx"` 文本语法 |
| ③ | 变量绑定弹窗 | 用 `PortDefinition.Name` 当变量名的隐式约定 |

三套都假设"名字永不改变"，于是变量一改名：连线断、监视项失联、画面绑定全废，而且**没有一处会报错**——静默断链是最难查的一类问题。

### 决策

| 决策点 | 选择 | 理由 |
|---|---|---|
| 解析器形态 | `IVariableRegistry`（放在 `Core/Binding/`） | 只读契约 + 一个默认实现；编译器/监视/画面三种消费者都依赖这个接口，而不是各自遍历集合 |
| 索引结构 | 双字典 `Guid→IVariable` + `string→IVariable`（名字大小写不敏感） | Id 是权威键、Name 是兼容键；`Count` 取 Id 字典（"有多少个可被 Id 寻址的变量"） |
| 寻址优先级 | `Resolve(id, name)` = **Id 优先，Name 兜底** | 新数据只有 Id 有意义；旧数据只有 Name 能读懂。两条路都要，顺序不能反 |
| 旧工程自愈 | `ResolveGlobalLink` 在"只有名字命中"时把 `link.TargetVariableId` 回填 | 迁移不能要求用户重连一次线。补上 Id 后引用改为按 Id 寻址，**下次保存即落盘**，迁移只发生一次 |
| 集合实例被替换 | `WorkspaceContext.GlobalVariables` 的 setter 里同步 `Attach` | 索引是派生数据、集合才是真理。`InitializeCommonVariables()`、方案重载都会整体换掉集合实例，漏挂就会"看得见幽灵变量、看不见新变量" |
| 并发 | 读写都加 `lock`，但**绝不在锁内抛事件** | `Dictionary` 并发扩容会直接损坏结构；事件订阅方会做改名级联、可能触达 UI 线程，握锁执行等于把 UI 钉死 |
| 维护策略 | Add/Remove 走增量；Clear/Replace/Move 走全量 `Rebuild` | 万级变量逐个灌入时增量维护避免 O(n²)；Clear 等动作下增量没有收益且容易漏键 |
| 重复键取舍 | **先出现者赢**（Id 与 Name 两条路径都一致） | 两条路径必须给出同一个答案；"两个入口各给一个答案且都不报错"比解析失败更难查 |

### 两个容易被忽略的点

1. **构造函数里的顺序是有意义的**：`InitializeCommonVariables()` 会**整体替换**变量集合，索引必须建在它之后。写反了会挂在一个随即被丢弃的旧集合上——症状是"索引里只有 12 个演示变量，真实变量一个都找不到"。
2. **`IVariable.Name` 不是 INPC**，索引无法自行感知改名。所以 `NotifyRenamed` 是**主动通知**式契约：改名路径漏调，索引就会留一个指向旧名的死键（比线性扫集合更糟）。这也是 S0-b 必须把改名收敛成单一入口的原因。

## 二、修改文件清单

| 文件 | 改动 |
|---|---|
| [Core/Binding/IVariableRegistry.cs](file:///e:/VM/VisionMaster-W-master/Core/Binding/IVariableRegistry.cs) | **新建**：解析契约 `FindById` / `FindByName` / `Resolve` / `ResolveGlobalLink` / `NotifyRenamed` / `Rebuild` / `Count` / `VariableRenamed` 事件；`VariableRenamedEventArgs`（实例 + 旧名） |
| [Core/Binding/VariableRegistry.cs](file:///e:/VM/VisionMaster-W-master/Core/Binding/VariableRegistry.cs) | **新建**：默认实现（`_gate` 锁、`Attach` 重挂、增量维护、锁外抛事件、重复身份 `Debug` 留痕） |
| [Shard/Core.Interfaces/LinkReference.cs](file:///e:/VM/VisionMaster-W-master/Shard/Core.Interfaces/LinkReference.cs) | 新增权威键 `Guid TargetVariableId`（与 `TargetPortName` **并存**：后者是唯一能读懂旧数据的兜底键，不能删） |
| [Core/Models/PortDefinition.cs](file:///e:/VM/VisionMaster-W-master/Core/Models/PortDefinition.cs) | 新增 `Guid VariableId`（绑定弹窗的候选列表本身就是 `PortDefinition`，身份必须跟着端口走到 `DoFinalBind`） |
| [Engine/FlowQueryHelper.cs](file:///e:/VM/VisionMaster-W-master/Engine/FlowQueryHelper.cs) | 变量树生产端：全局变量分组的 `PortDefinition` 补 `VariableId` |
| [VisionMaster/ViewModels/DialogViewModels/VariableBindingViewModel .cs](file:///e:/VM/VisionMaster-W-master/VisionMaster/ViewModels/DialogViewModels/VariableBindingViewModel%20.cs) | `DoFinalBind` 建连线时写入 `TargetVariableId = port.VariableId` |
| [Core/IReadOnlyWorkspaceContext.cs](file:///e:/VM/VisionMaster-W-master/Core/IReadOnlyWorkspaceContext.cs) | `IWorkspaceManager` 暴露 `IVariableRegistry VariableRegistry`；`GlobalVariables` 改为带 setter 的属性（setter 内 `Attach` 重挂）；索引在 `InitializeCommonVariables()` **之后**构造 |
| [Engine/FlowCompiler.cs](file:///e:/VM/VisionMaster-W-master/Engine/FlowCompiler.cs) | `GlobalVariable` 分支改调 `ResolveGlobalLink`（顺带消掉 O(连线数 × 变量数) 的线性扫描） |
| [ScadaChecks/Program.cs](file:///e:/VM/VisionMaster-W-master/ScadaChecks/Program.cs) | 追加 [F]/[G]/[H] 三组常规断言与 [I] 压力组（含 `Rename` 辅助方法与其契约注释） |

## 三、验证结果

### 编译
`dotnet build ScadaChecks\ScadaChecks.csproj`（连带重编 VisionMaster）→ **0 错误**（可空性等历史遗留警告未新增）。

### 断言
- `dotnet run --project ScadaChecks\ScadaChecks.csproj` → **通过 57 / 失败 0**
- `dotnet run --project ScadaChecks\ScadaChecks.csproj -- --stress` → **通过 65 / 失败 0**
- 回归 `dotnet run --project FlowCanvasChecks\FlowCanvasChecks.csproj` → **通过 207 / 失败 0**

| 组 | 覆盖 |
|---|---|
| [F] 索引双路查找 | 构造顺序（索引跟随 12 个演示变量）、按 Id/按名命中同一实例、名字大小写不敏感、`Guid.Empty`/不存在 Id/空名落空、Id 优先于 Name、Id 落空 Name 兜底；增删增量跟随、集合**实例替换**后重挂（旧变量不留幽灵）、`Attach` 幂等、`Clear` 清空；重复身份只索引一份且取舍路径一致 |
| [G] 连线解析 | `link == null` 安全返回；Id 命中（名字字段已失效仍解析）；旧连线按名兜底 + **自愈回填 Id** + 幂等；自愈后改名不断链；**未自愈的老连线改名后落空**（留给 S0-b 的存量场景）；皆落空返回 null 且不写脏数据 |
| [H] 改名契约 | 旧名落空/新名命中/Id 不受影响、事件携带实例与旧名；**反向断言**：漏调 `NotifyRenamed` 时索引保留旧键（证明改名必须走统一入口）；重名场景下不误删他人的名字键 |
| [I] 万级压力（`--stress`） | 10000 变量逐条 Add 建索引 6ms；**200000 次按 Id 解析 6ms（≈30ns/次）**；对照组的线性扫描 2000 次 32ms（≈16µs/次，**慢约 500 倍**）；10000 次改名 1ms |

> 500 倍这个差距不是"优化得好"，而是 O(1) 与 O(n) 的差别——n 还会随变量数继续涨。

## 四、已知边界

1. **改名入口尚未统一**：`IVariable` 继承自 `IOutputPort`，那里的 `Name` 是**只读**的；真正可写的 `Name` 落在 `LocalVariableModel` / `NetworkVariableModel` 上（断言工程里因此需要一个按模型分派的 `Rename` 辅助方法）。散落的 `model.Name = ...` 一旦漏调 `NotifyRenamed`，索引就会留死键。**S0-b 第一件事**：把改名收敛为注册表上的单一 API（写模型 + 修索引 + 广播一体）。
2. **只有连线侧接上了 Id 消费者**：监视项（`WatchItemModel.GlobalVariableName`）与 `IVariableDisplaySource.BindingKey` 仍是纯名字寻址，属 S0-b / S1。
3. **SCADA 画面绑定不得沿用 `BindingKey`（变量名）做键**：S2 图元落盘时应存 `VariableId`，否则改名同样会断链。这条要在 S1 的画面文档模型里定案，不能等 S2 再救。
4. **重复身份的取舍是"先出现者赢"**，且只在 `Debug` 输出留痕（不抛异常、不阻断打开方案）。方案文件被手工改成两变量同 Id 时，靠后的那个将无法被 Id 解析——可通过名字找到。
5. **`Attach` 不在 `IVariableRegistry` 契约上**，属于宿主（`WorkspaceContext` 的 setter）才需要的实现细节；断言工程里是显式转型调用的。
6. **压力测试仍在内存里**：万级变量的 JSON 落盘/读盘往返要等 S1 的画面文档序列化一起压。
