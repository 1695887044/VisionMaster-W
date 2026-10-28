# 结果上报 · 开发：不变量与守门断言

> 分类：数据处理 ｜ 来源：插件 Plugin.ResultUpload ｜ 读者：开发

每条一律写成 **症状 → 机理 → 守门断言名**。分两组：A 组违反会当场红或抛，B 组违反不报错、只出诡异现象。

**先说断言现状**：本插件的专属断言文件是 `FlowCanvasChecks\ResultUploadChecks.cs`
（与 `DataPipelineChecks` 用 `Compile Include` 共用同一份源码），覆盖字段表类型保真、上报时机、
HTTP 重试与幂等 ID、断网续传、业务关键字与阻断、自定义头与加签，以及 **N2 的变量取值域与数组格式化**。
手册闸门 `HelpManualCheck.cs` 的 HM 系列与产物守门 `ParallelExecutionChecks.cs` 的
`标注 [ParallelSafe] 的 18 个插件 DLL 全部含特性元数据（漏重建=门禁误报）` 照旧适用。
本页没列到的面仍按"无断言，靠人工核对"处理，**不要假装有更硬的闸**。

---

## A 组：违反就编译红 / 闸门红 / 当场抛

**A1 手册必须扁平放在 `Help\` 且带 `NN-` 前缀、第一行是 `# 一级标题`**
症状：工具箱点开本算子看到的是"自动生成"页，或者页序乱、页名取不到。
机理：本程序集只有一个插件类，宿主 `HelpCatalogService.NarrowToPlugin` 对扁平目录走"不在任何类型目录下的页"分支；
一旦有人另立 `Help\ResultUploadPlugin\` 或 `Help\dev\`，扁平那批页会被**静默挤掉**。
守门：`HM2 多插件程序集不许放扁平手册（防串页）`、`HM3 Help 子目录名逐字等于插件类名（分册能被认出）`、
`HM5 每页第一行内容就是 # 一级标题（页名取得到）`、`HM6 手册文件名是 NN-xxx.md（页序确定）`、
`HM12b 手册最多一层目录（开发页另立子目录会挤掉用户页）`。

**A2 手册编码与体量**
症状：闸门红；或中文在界面上是乱码。
机理：宿主按 UTF-8 读内嵌资源；M 档要求用户页 ≥4 页、开发章节 ≥150 行。
守门：`HM7 手册都是无 BOM 的合法 UTF-8 且含中文（编码纪律）`、
`HM8 每个插件的手册正文 ≥ 70 行（不是占位页）`、
`HM9 手册里没有占位标记（这条闸门的名字原文自带那五个禁用词，此处刻意不照抄——照抄会让 HM9 自己变红）`、
`HM13 已交的开发章节达到所在档位的行数下限（L300/M150/S100，交了就不是随笔）`、
`HM12 开发页文件名是 NN-dev-xxx.md（宿主认得出这是开发章节）`。

**A3 开发页不许另立子目录、显隐链路四处齐全**
症状：「显示开发章节」开关形同虚设（开发页永远可见 / 永远看不到），编译期全绿。
机理：分层只发生在显示层，加载永远全量；标记来自文件名里的 `dev-` 段。
守门：`HM12 开发页文件名是 NN-dev-xxx.md（宿主认得出这是开发章节）`、
`HM14 开发章节显隐链路四处齐全（标记→节点→裁树→开关→配置）`；
覆盖面自查看 `HM16 档位表与插件类一一对应（无未登记、无过期条目）` 与 `HM17 骨架完整度台账（按档位，欠账不判红）`。

**A4 `Help\` 加了文件必须真进 DLL**
症状：改了手册、界面还是旧页，且不报错。
机理：`Plugin.ResultUpload.csproj` 里挂的是 `<EmbeddedResource Include="Help\**\*" />`，
增量构建会把"新挂上的内嵌资源项"判成不脏（AGENTS.md R42）。
守门：`HM4 有 Help/*.md 的工程都带 EmbeddedResource（手册进得了 DLL）` 只核 csproj 那一行；
**产物对账无断言，靠人工核对**——`tr '\0' '\n' < Modules\Plugin.ResultUpload.dll | grep -a "\.Help\."` 的资源名数要等于 `Help\` 下 .md 数。

**A5 改了源码必须重建插件工程**
症状：界面上中文变英文、日志文案对不上手册、"上一轮明明修好了"。
机理：`Modules\` 不由宿主工程带出；本插件标了 `[ParallelSafe]`，产物陈旧时并行门禁还会误报"并行不安全"。
守门：`标注 [ParallelSafe] 的 18 个插件 DLL 全部含特性元数据（漏重建=门禁误报）`（P31）。

**A6 动态端口必须在 `base.ApplyConfigValues` 之后重建**
症状：老方案的连线打不开/接不上，字段口在画布上不出现。
机理：`ApplyConfigValues` 是 virtual，本插件 override 成"先 `base`（灌 `[StepConfig]`，含 `Fields`）再 `RebuildDynamicInputs()`"，
必须在 `LinkPorts` 之前把端口建出来。顺序写反 = 端口还没生成就去找它连线。
**构造器里也必须补一次 `RebuildDynamicInputs()`**：`PluginService` 扫描插件时拿的是"空配置实例"的 `Inputs`
当 `InputDefinitions` 快照，构造期那三行 `Fields.Add` 发生在 `HookFields` 之前（集合通知一次都不触发），
漏补的表现是**扫描期快照里没有 `sn`/`result`**——画布拖线打开的完整绑定弹窗左侧只剩「上报触发」。
守门：无断言，靠人工核对（`ResultUploadPlugin.ApplyConfigValues` 只有两行，改它时对照本页第二节第 8 步）。

**A6b 字段端口只能从行内 🔗 进，画布上没有字段级锚点**
症状：用户按"画布上连线"的直觉去画布找 `sn`/`result` 端口，只看到每节点一根流程连接脚。
机理：画布连线是**模块级**的（一根线 = 两个步骤有数据依赖，端口在绑定弹窗里选，见 `FlowCanvasViewModel`
类注释）；而完整绑定弹窗的左侧端口表读 `ToolItemModel.InputDefinitions`（扫描期空配置快照），
**用户新加的字段一条都不在里面**。所以字段行必须自带绑定入口——**一律用公共控件
`Core.Controls.LinkableValueEditor`（R189）**，它自带 🔗→单绑定弹窗→`SetLink`、✕→`RemoveLink`、
`LinkAddress` 回显与类型驱动渲染，不许自己 `Publish(new LinkPathEvent{...})` 手搓。
动态端口的 `Port` 用 `MultiBinding` + `InputPortConverter`（字段名 + 宿主插件 + `PortsVersion` 三路），
每次 `RebuildDynamicInputs` 后推一格 `PortsVersion` 让编辑器重取新端口对象；
`Port` 可能为 null 的行用 `ContentControl` + `FieldPortEditorTemplate` 按需实例化，不许常驻 + `Visibility`。
删掉这个入口 = 新字段的端口永远绑不上（用户只会看到"当前步骤没有可绑定的输入端口"）。
守门：无断言，靠人工核对。

**A7 字段表变动必须同时驱动端口重建与候选刷新**
症状：勾掉「启用」后端口还在画布上、改完字段名"判定字段"下拉不更新、重开方案端口数量翻倍。
机理：`HookFields` 订阅集合与每行的 `PropertyChanged`，且订阅前先 `-=`（幂等防重复订阅）；
`Name` / `Source` / `Enabled` 三个属性触发 `RebuildDynamicInputs()` + `OnPropertyChanged(nameof(JudgeFieldNames))`。
少订一个属性 = 那一列改了界面不动；忘了 `-=` = 一次改动收到多次通知、反复重建端口。
守门：无断言，靠人工核对。

**A8 变量字段只认本轮局部变量（N2 之前的旧口径）**
症状：来源=变量，填的是「变量管理」里的全局变量 / 通讯变量 → 报文里永远是 `null`；或数组字段上报成 `System.Int32[]`。
机理：取值必须走 `TryResolveVariable`——**本轮 `LocalVariables` 优先 → `context.SharedVariables.TryRead(Guid.Empty, 名字)` 兜底**
（注册表覆盖全局 / 通讯 / 系统变量，取镜像值）；数组在 `CollectValues` 里经 `FormatPayloadValue` 转文本
（`[a,b,c]` / `byte[]` 十六进制），组包器对"认不出的类型"只会 `ToString()`（那就是类型名）。
缺名不静默：按名字限次 WARN（每个名字最多 3 条），走 `context.Logger`，别塞 `UploadQueue` 的日志队列。
要真正的 JSON 数组：用「原始JSON」+ `{#字段名}`（文本 `[1,2,3]` 不带引号落进去就是数组）。
守门：`结果上报 N2：变量字段取到全局变量（数值仍保真）`、`结果上报 N2：变量字段取到通讯变量（网络变量镜像值）`、
`结果上报 N2：数组字段格式化成 [1,2,3]（不是 System.Int32[]）`、`结果上报 N2：缺名变量留 WARN（不是静默发空）`、
`结果上报 N2：缺名 WARN 有上限（4 轮只提示 3 条）`。

---

## B 组：违反不报错，只出诡异现象

**B1 值池必须"大小写不敏感 + Trim 键名"，且 JSON 键区分大小写**
症状：模板写 `{SN}` 命中不了字段 `sn`；同一张表里 `sn` 与 `SN` 两行值永远一样；JSON 里冒出带空格的怪键。
机理：`CollectValues` 用 `StringComparer.OrdinalIgnoreCase` 建字典、键写 `f.Name.Trim()`；
`HttpPayloadBuilder.BuildFieldTable` 直接把 Trim 后的名字当 JSON 键（区分大小写）。
守门：无断言，靠人工核对。

**B2 `object` 端口 + `IsRequired = false` 不许改**
症状：只要没连线就报"参数缺失"、编译不过；或类型校验把图像/数组类字段挡在门外。
机理：`FlowCompiler` 对 `InputPort<object>` 不做静态类型校验；动态口允许"先定义、后连线"。
守门：无断言，靠人工核对。

**B3 时机判定必须在 URL 校验与组包之前**
症状：不该发的件也报"未配置目标 URL"，高频流程被 WARN 淹没。
机理：`RunAlgorithm` 里 `shouldSend == false` 直接 `return`，后面一切都不发生。
顺序改成"先校验 URL"就破坏了"静默通过"的契约。
守门：无断言，靠人工核对。

**B4 静默通过时不要写日志**
症状：日志里全是噪声，真故障看不见。
机理：不上报的分支只 `return`；基类 `Execute()` 已预置成功语义，不需要补任何日志。
守门：无断言，靠人工核对。

**B5 异步回写发生在队列线程**
症状：界面抛"调用线程无法访问此对象"，或结果行/日志列表不刷新。
机理：`job.OnCompleted` 由消费线程调用 `ApplyPorts` 并写 `LastResult`；
视图对 `LastResult` 的响应必须 `Dispatcher.BeginInvoke`（`ResultUploadView.OnPluginPropertyChanged` 就是这么写的）。
另外 `ApplyPorts` 写的三个口在异步模式下**不代表本轮**——手册 03 页已按这个口径写。
守门：无断言，靠人工核对。

**B6 `X-Upload-Id` 注入前必须查重（不覆盖用户自填）**
症状：MES 侧去重失效，或用户填的业务单号被 GUID 顶掉。
机理：`SendWithRetry` 首段 `HasHeader(job.Headers, "X-Upload-Id")` 为真时跳过注入。
守门：无断言，靠人工核对。

**B7 加签必须在每次尝试内重算**
症状：长重试后钉钉判"时间戳过期"拒收，看起来像"配了密钥也发不出去"。
机理：`SignUrl` 用的是 `DateTimeOffset.Now.ToUnixTimeMilliseconds()`，写在 for 循环**内部**；
把它挪到循环外就是复用旧时间戳。
守门：无断言，靠人工核对。

**B8 `HttpClient` 只能进程级常驻、且必须两份**
症状：高频上报后端口耗尽/连接僵死；或运行期改证书选项根本不生效。
机理：TLS 选项在首次请求后不可变，所以按 job 标志在两个实例里选；
`Timeout` 置无限 + 每请求 CTS，才不会打断别的件；连接池 10 分钟换一次防 DNS/路由变更僵死。
在 `SendWithRetry` 里 `new HttpClient()` 是最常见写法，但会累积 TIME_WAIT 端口。
守门：无断言，靠人工核对。

**B9 spool 文件操作必须在 `SpoolLock` 内、网络重发必须在锁外**
症状：断网期间整个上报卡死，或账本半行半行地坏掉。
机理：同步模式失败落盘的调用方是**流程线程**，与消费线程并发；
`TryDrainSpool` 只在锁内 `LoadSpoolBatch()`，重发在锁外，最后按 ID 剔除重写。
把发送塞进锁里 = 一条网络等待挡住所有落盘。
守门：无断言，靠人工核对。

**B10 补传的三条边界（`AllowSpool = false` / 连续失败 2 条中止 / 累计超过 50 次放弃）不许合并成一条**
症状：网络没恢复时空转刷屏；或 URL 写错的毒记录永远压着后面的件；或测试数据污染真实账本。
机理：三者各管一件事，判据分别是 `AllowSpool`、`consecutiveFails >= 2`、`DrainFailures[key] > MaxDrainFailures`（**严格大于**，第 51 次才放弃）。
守门：无断言，靠人工核对。

**B11 行数缓存 `_spoolCount` 的失效点必须都维护**
症状：积压上限判定失效（永不裁剪 → 磁盘打爆，或过早裁剪 → 无故丢件）。
机理：只有 `SpoolAppend` 的 `count + 1`、补传重写后的 `kept.Count`、以及写失败时置 `null` 三处维护它；
新增任何直接改文件的分支若不更新缓存，判定就基于假数字。
守门：无断言，靠人工核对。

**B12 测试发送不落 spool、但会写输出端口**
症状：① 测试一次失败，真实 spool 里冒出一条发不出去的记录；② 流程里 `已上报` 突然变 `true`（本轮根本没发）。
机理：`BuildJob(..., allowSpool: false)` 掐断落盘，而 `TestSend` 末尾照样调 `ApplyPorts(bizOk, code, resp)`。
这是刻意取舍（让用户看到"和流程一样的读端口行为"），改它前先想清楚第 ② 条会不会变成陷阱。
守门：无断言，靠人工核对。

**B13 报文预览与真实发送必须共用 `HttpPayloadBuilder`**
症状："看着对、发出去错"——手册里最响的一句承诺失效。
机理：`PayloadPreview` 与 `RunAlgorithm` 都调 `HttpPayloadBuilder.Build(...)`，
唯一差别是 `useTestPlaceholder: true` 时把 `null` 换成 `(空)`。
给预览另写一份拼接逻辑（或给组包器加"只在预览里跳过"的开关）就是回归。
守门：无断言，靠人工核对。

**B14 请求头值"打码不加密"**
症状：外发方案包把生产 token 带出去。
机理：`PasswordBoxHelper` 只是把 `PasswordBox` 的输入桥接到 `Value` 属性，
基类 `OnConfirm` 存进 `InputValues` 的是明文；spool 文件里还额外存了**整份 Headers**。
这是已记录的取舍（用户页 03/04 与排障页都写明），不要误当成"已加密"。
守门：无断言，靠人工核对。

**B15 四个枚举成员上目前没有 `[Display]`**
症状：界面某处出现 `FieldTable` / `OnlyNG` 这类英文——但**只有在本插件自带视图之外**才会出现。
机理：中文是 `EnumLabelConverter` 的字典给的，不是读特性给的。R40 的 `[Display]` 欠账清单里点名的就是本工程；
补 `[Display]` 时**只能改显示、不能改成员名**（`.vms` 与流程存的是成员名，写回走 `Enum.Parse`）。
守门：机制侧由 `FlowCanvasChecks\MotionZMotionChecks.cs` 那套中文闸门守几何+运动六个枚举，**本插件的四个枚举不在其覆盖面内** → 本条无断言，靠人工核对。

## 改完这个插件之后的必做动作

| 动作 | 判据 |
| --- | --- |
| 重建插件工程（Release 也要，`pack.ps1` 打的是 Release） | 只重建宿主跑的是旧 DLL（红线③、R16、R55） |
| 核 DLL 真带了 `Help` 资源 | 见 A4 的代码块判据（R42） |
| 跑 gate | `HelpManualCheck` HM0~HM17 + `ParallelExecutionChecks` P31 |
| 大改动后重建 CodeGraph 索引 | 不重建 = 影响面查询静默少项（R28） |
