# 交接报告 · C#脚本插件 GStack 审查（2026-10-24）

> 审查对象：`Plugins/Plugin.CSharpScript`（13 个源文件 + `Help/` 7 页）
> 分支：`feature/scada-web-v4-a-prime`
> 方式：全目录静态审查（非 diff）+ 4 路专项（安全 / 性能 / 可维护性 / 测试）+ 红队反证 + 一次性 Roslyn 探针
> 代码改动：审查轮 0；**批次 A、P1-2 与「预览口径 + 端口释放 + P3 三项」已于 2026-10-24 分三批落地**，分别见 `docs/code-changes/2026-10-24-CSharp脚本插件GStack审查批次A（取消语义与缺端口告警）.md`、`docs/code-changes/2026-10-24-CSharp脚本插件GStack审查P1-2（变量重名与孤儿连线）.md`、`docs/code-changes/2026-10-24-CSharp脚本插件GStack审查预览口径与端口释放与P3三项.md`
> 门禁与真机：审查轮未运行；**批次 A 已跑** `FlowCanvasChecks`（3534/0）/ `HelpProbe`（全过）/ `audit_artifacts` / `audit_root`，均绿；**P1-2 已跑** `FlowCanvasChecks`（3541/0）/ `HelpProbe`（74/0）/ 两项 audit，均绿；**第三批（预览口径/端口释放/P3）跑完整 `tools\run_gates.ps1`**，数字见该批 code-changes 记录；真机未启动。**P1-2 验证期正常全量 `run_gates.ps1` 曾被无关在途改动 `UI/Controls/Themes/Dialog/Dialog.Input.xaml`（XML 注释含 `--`，MC3000）挡住，当时改用 `-p:BuildProjectReferences=false` 单建插件 + 定向宿主复验；该文件已由改动它的会话自行修好，第三批起全量 gate 可正常跑。**

## 一、当前状态（结论）

**0 个 P0；2 个 P1；7 个 P2；10 条 P3（其中 8 条是手册与实现漂移）。**

> **截至 2026-10-24 第三批结束的处理面**：P1-1、P1-2、P2-2、P2-3、P2-4（告警化）、P2-6、P3-1、P3-2、P3-3 已落地并有断言守门；
> **仍未动**：P2-1（白名单定位，批次 B 待拍板）、P2-5（编译失败负缓存）、P2-7（共享变量正则校验改语法树）、P3-4~P3-10。
> 下面各表保留审查原文，已在标题行或行首标注 ✅ 的条目表示"已修"，未标注即仍未处理。

整体工程质量偏上：编译缓存、配置快照回滚、孤儿连线键清理、共享变量按 `VariableId` 寻址、白名单 fail-closed 都有真实实现。问题集中在四类：

1. **失败/取消路径**：取消被记成失败，脚本收不到取消令牌；
2. **字符串即协议**：输入/输出/共享变量都靠名字字符串，重名与拼错没有闸门；
3. **资源释放**：动态端口重建不 `Dispose`，订阅与 HImage 滞留；
4. **文档承诺大于实现**：白名单、预览、错误标红、枚举显示名等多处手册说法与代码相反。

## 二、已做（审查范围与证据）

### 2.1 覆盖范围

- 源文件：`CSharpScriptPlugin.cs`、`CSharpScriptEngine.cs`、`ScriptContext.cs`、`ScriptVarDef.cs`、`ScriptSharedVarDef.cs`、`ScriptSharedAccessor.cs`、`ScriptSharedUsageValidator.cs`、`CSharpApiDoc.cs`、`CSharpCompletionData.cs`、`CSharpIntelliSense.cs`、`CSharpHighlighting.cs`、`CSharpTemplates.cs`、`CSharpScriptView.xaml(.cs)`。
- 宿主契约：`Shard/Core.Interfaces/VisionPluginBase.cs`（`Execute` 取消映射、动态端口移除、`AddDynamicOutput`、`ClearDynamicOutputs`）。
- 手册：`Help/02`、`03`、`04`、`10`、`11`、`12`。
- 断言：`FlowCanvasChecks/CSharpScriptChecks.cs`（确认存在并已在 `FlowCanvasChecks/Program.cs:294` 注册）。

### 2.2 P1 明细

#### P1-1 取消语义失效：OCE 被记成"失败"，脚本也收不到取消令牌

**证据**

- `CSharpScriptPlugin.cs:859-860`：`RunAsync(script, globals, context?.CancellationToken ?? CancellationToken.None).GetAwaiter().GetResult()`；
- `CSharpScriptPlugin.cs:886-894`：`catch (Exception ex)` 无差别接收，把 `OperationCanceledException` 转成 `Success=false` + `脚本运行异常: …`；
- `VisionPluginBase.cs:123-131`：基类本来有 `catch (OperationCanceledException) when (context?.CancellationToken.IsCancellationRequested == true) { throw; }`，节点层据此记 `Skipped`（灰行），且明确注释"取消不是失败"；
- `ScriptContext.cs:22`：只持有 `_exec`，没有暴露 `CancellationToken`，也没有 `ThrowIfCancellationRequested`。

**触发与影响**：停止/急停时，长脚本里的同步死循环不会观察令牌，步骤只能等脚本自己退出；退出后又被记成"执行失败"（红行），流程收尾多一条失败记录，可能触发误报警或联锁判断。插件自己的 `catch` 先于基类执行，基类的正确映射永远走不到。

**置信度**：9/10。

**修复方向**：

1. 在通用 `catch` 之前加与基类同款的 `catch (OperationCanceledException) when (context?.CancellationToken.IsCancellationRequested == true) { throw; }`；
2. `ScriptContext` 增加 `CancellationToken CancellationToken`（或 `ThrowIfCancellationRequested()`），允许脚本在长循环里主动退出；
3. 手册补一句"长循环必须自己检查取消"。

#### P1-2 变量重名无任何校验：端口折叠，确认时静默删连线

> **状态：已修复（2026-10-24）**。落地方案：`OnConfirm` 在任何写入之前做非空 + `StringComparer.Ordinal` 唯一性校验（重名/空名直接抛，宿主配置窗口保持打开并显示原因）；`RemoveOrphanLinks` 把"活行的锚点键"也算有效键，不再把撞名未迁移的连线当孤儿删掉；输出快照按端口集合同口径去重；`ValidateScript` 同步这个校验顺序。守门 CS27-CS33（重名/改名后迁移/输出重名/空名四组）。证据见 `docs/code-changes/2026-10-24-CSharp脚本插件GStack审查P1-2（变量重名与孤儿连线）.md`。**保留边界**：旧 `.vms` 里的重名配置仍能加载（打开时不拦），必须在下次确认时改掉；名字仍按 Ordinal 精确比较，不 trim、不强制 C# 标识符。

**证据**

- 输入侧：`CSharpScriptPlugin.cs:409-412` 遇到重名直接 `continue`（先到先得）；
- 输出侧：`VisionPluginBase.cs:471-476` `AddDynamicOutput` 见同名就静默不建；但 `CSharpScriptPlugin.cs:439-444` 仍把两行都写进 `OutputPortDefinitions` 快照；
- 改名撞名：`CSharpScriptPlugin.cs:599-606` 目标名已被连线时"不迁移、也不前进锚点"；
- 确认清理：`CSharpScriptPlugin.cs:361-364` 只保留"当前 `InputVars` 里出现过的名字"，旧锚点键在 `OnConfirm`（:175）被当孤儿删除。

**触发与影响**：两行输入都连着线，把 A 改名成 B 已在用的名字（或停在中间名）后点「确定」——A 的上游连线被静默删除，A/B 共用 B 的端口，脚本 `GetInput("名字")` 拿到 B 的值；不报错、不告警，流程继续用默认值/错值运行。输出侧重名则第二行永远没有端口，但快照里有两个同名项，下游选择时行为取决于谁先被读。

**置信度**：8/10（代码链完整；未在真配置界面复现）。

**修复方向**：

1. `ValidateScript` / `OnConfirm` 前做名字唯一性校验（非空、唯一、建议标识符友好），重名拒绝确认并给出具体行号；
2. `RemoveOrphanLinks` 不应删除"仍有归属行"的锚点键；或改名冲突时强制进入"解决冲突"状态，不允许静默收尾；
3. 输出快照与端口集合保持一一对应（去重或拒绝重名）。

### 2.3 P2 明细

| # | 问题 | 证据 | 影响 | 置信度 |
|---|---|---|---|---|
| P2-1 | 外部引用白名单不是"脚本只能引这些"的边界 | `Help/04:15` 明写"不能 `#r "…"` 动态加载"；`CSharpScriptEngine.cs:186-188` 只 `WithReferences/WithImports`，`ScriptOptions.Default` 自带 `ScriptMetadataResolver` + `SourceFileResolver`；探针实测 `#r` 绝对路径、`#load` 绝对路径均 0 错误；`CSharpScriptEngine.cs:314` 白名单 DLL 未过 `IsManagedAssembly`（对比 :127）；`:328-338` 运行期只判 `File.Exists`，不重算哈希 | 白名单实际是"预置引用清单 + 依赖卫生"，不是安全边界；给原生 DLL 登记并哈希匹配后会以 CS0009 拖垮全部脚本编译；`Help/10:66-67` 说第 3、4 层都判托管，与代码不符 | 8/10 |
| P2-2 | ✅ **已修复（2026-10-24 第三批）** 动态端口重建不 `Dispose` 旧端口/值 —— 输入端口重建时 `Dispose()`（唯一退订路径），输出端口只断"值引用"不越权释放；守门 CS36/CS36b/CS37 | `CSharpScriptPlugin.cs:392-424`（输入）、`:427-453`（输出 → `ClearDynamicOutputs`）；`VisionPluginBase.cs:459-466`、`:491-505` 只删字典键；`InputPort<T>.Dispose` 才退订上游 `ValueChanged` | 编辑期每次改名/改类型都重建全部输入端口，旧端口被事件源持有；旧 HImage 滞留原生内存。正常执行轮有基类 `AutoDisposeRoundOutputs`（`VisionPluginBase.cs:115-117`）兜底，**被重建移除的端口不在兜底范围** | 7/10 |
| P2-3 | ✅ **已收口（2026-10-24 第三批，口径=只承诺 HImage）** 不做 Region/XLD 推送，改为界面/代码/手册三处同一口径：`ScriptVarDef.ShowsDisplayWindow` 只在 `HImage` 为真，区域/轮廓行根本选不出「显示窗口」，配不出来了就不存在"静默不显示"；标题面**没做**——`PluginPreviewExtensions.cs:48,60` 本来就有带 `title` 的重载，插件仍调 2 参那个，所以这是"可接未接"，不是宿主缺口 | `CSharpScriptPlugin.cs:897-907` 只有 `port.Value is HImage`，调用无标题的 `PublishPreview(hi, window)` | `Help/02:62` 承诺"HImage 等图形输出"自动推预览、`:76` 承诺 Remark 是"推图标题来源"；实际 HRegion/HXLD/HObject 不推，标题也不传 | 8/10 |
| P2-4 | 端口名拼错静默吞掉 | `ScriptContext.cs:55-67`：`SetOutput` 端口不存在直接忽略；`GetInput` 端口不存在返回 `default` | 输出名打错 → 下游拿默认值且无日志；与共享变量"写失败必须抛"的哲学相反 | 9/10 |
| P2-5 | 编译失败路径没有负缓存 | `CSharpScriptEngine.cs:72-78` 失败直接 `return null`，`:80-84` 才写缓存 | 配置里存在编译错误时，每次执行/试运行都重新 `Create` + `Compile`（全量 Roslyn）。成功路径有缓存，**不是每帧开销**；原性能专项标 CRITICAL，复核后降为 P2 | 9/10 |
| P2-6 | ✅ **已修复（2026-10-24 第三批）** 共享变量空别名随宿主改名漂移 —— 在三个"脚本键还权威"的时刻固化别名（视图选定候选、宿主快照刷新前后、点「确定」），`EffectiveAlias` 语义不变；名字为空的行不固化（不把空串锁成键）。守门 CS38/CS38b。**残留边界**：宿主变量被删除后重建（新 Id）仍停在旧快照，本轮未做 | `ScriptSharedVarDef.cs:114-115` 空别名回退 `Name`；`CSharpScriptPlugin.cs:745-758` 刷新时覆盖 `def.Name`；`:761-776` 仅当 `VariableId == Guid.Empty` 才按名字兜底 | 宿主全局变量改名后，脚本正文仍用旧别名 → 运行期 `GetGlobal("旧名")` 抛"未声明"；宿主变量删后重建（新 Id）声明停在旧快照 | 7/10 |
| P2-7 | 共享变量用法校验是原文正则 | `ScriptSharedUsageValidator.cs:21-23`、`:44` | 注释/字符串里的 `Context.GetGlobal("x")` 会被当成真实调用，未声明时直接挡住「校验脚本」；`GetGlobal(name)` 又扫不到（已在注释中承认） | 8/10 |

### 2.4 P3 明细

| # | 问题 | 证据 |
|---|---|---|
| P3-1 | 手册陈旧 8 处 | `Help/03:30` 说变量名紫色高亮（`CSharpHighlighting.cs:19-21` 明确不染）；`Help/03:55` 说所有错误行标红（`CSharpScriptEngine.cs:72-78` 只回第一条，`CSharpScriptView.xaml.cs:251-260` 最多解析到 1 行）；`Help/03:14` 布局表缺共享变量卡片；`Help/11:10` 类声明漏 `IPluginConfigContextProvider`（`CSharpScriptPlugin.cs:40`）；`Help/11:47`、`Help/12:61-63` 说枚举显示名英文/未实现（`ScriptVarDef.cs:15-33` 已有中文 `[Display]`）；`Help/11:78` 说 `.vms` 只有三个键（`SharedVars` 是第四个 `[StepConfig]`）；`Help/12:5` 说没有 `CSharpScriptChecks.cs`（实际存在且已注册）；`Help/10:72` 日志映射写反（实际 Error→Error、Warning→Warn、其余→Info） |
| P3-2 | ✅ **已修复（2026-10-24 第三批）** `Context.Vars` 文档说"只读遍历用"，实际可直接改 —— 现返回 `ReadOnlyDictionary` 只读视图（签名仍是 `IDictionary<string,object>`，老脚本照编），写报 `NotSupportedException`，指回 `SetVar`；视图按源字典实例缓存，不做每拍 new。守门 CS39/CS39b（含"写失败后池里没多出 b"的反证） | `CSharpApiDoc.cs:95-97`；`ScriptContext.cs:126` 返回 `_exec.LocalVariables` 本体 |
| P3-3 | ✅ **已修复（2026-10-24 第三批）** 补全给出非 CLR 类型 `HXld` —— `CSharpApiDoc.CommonKeywords` 改给 `HXLDCont`，`HXld`（枚举成员名）不再出现在补全表。守门 CS40。**注意**：`CSharpHighlighting.KnownTypes` 是私有着色表、同类已同步改掉，但**没有断言覆盖**（已在 `Help/12` 的 12e 注明） | `CSharpApiDoc.cs:158`、`CSharpHighlighting.cs` 的 `KnownTypes`；真实类型是 `HXLDCont`（`ScriptVarDef.cs` 映射）。采纳补全后 CS0246 |
| P3-4 | `CSharpApiDoc` 两个 `ShowImage` 重名，`Find` 只回第一条 | `CSharpApiDoc.cs:127-140`；`:162-169`。4 参重载的悬浮/参数提示永远不出现 |
| P3-5 | 高亮 API 清单与 `ScriptContext` 不同步 | `CSharpHighlighting.cs:29-35` 缺 `NewMarks` / `MarkLine` / `MarkText`，但类注释声称"与 ScriptContext.cs 同步维护" |
| P3-6 | 输出行改 Remark 不刷新端口描述 | `CSharpScriptPlugin.cs:531-538` 只有 `Type` 变化触发重建；`Remark` 改动要等下一次重建才进 `OutputPortDefinitions` |
| P3-7 | 引擎注释自相矛盾 | `CSharpScriptEngine.cs:122-125` 说 `CreateFromFile` 对原生 DLL 不抛；`:162-163` 说会抛。前者才是事实（后者被 `AddAssembly` 的 `IsManagedAssembly` 提前挡住，不影响行为，但会误导维护者） |
| P3-8 | 断言缺口 | `CSharpScriptChecks.cs` 已覆盖的之外，缺：`RunAlgorithm` 编译失败/脚本抛异常/`Fail()` 三分支；`OperationCanceledException` 应记 Skipped；`OutputVars` → 下游取值的端到端；确认后再取消的模型还原；`ValidateScript` 的共享变量分支；重名行；`PortsVersion` 通知；`SetOutput` 类型不符；白名单 enabled/未登记/哈希不符；编译缓存命中与失效 |
| P3-9 | 输出改名不迁移下游 | 设计已知（`Help/02:55-56` 已说明下游需重选），编译器会报"上游不存在输出"，不是静默错误；仅提示 |
| P3-10 | 配置界面/试运行路径未做真机回归 | 本轮未启动 exe，快照回滚、行内链接显示等交互结论均来自代码阅读 |

### 2.5 已验证事实（可复算）

**Roslyn 探针（一次性，已删除）**

- `ScriptOptions.Default` 的两个解析器：`MetadataResolver = Microsoft.CodeAnalysis.Scripting.ScriptMetadataResolver`，`SourceResolver = Microsoft.CodeAnalysis.SourceFileResolver`。
- `#r "绝对路径.dll"`：编译 0 错误。
- `#load "绝对路径.csx"`：编译 0 错误（相对路径报 CS1504，是探针工作目录问题）。
- 结论：`Help/04:15` 的"不能 `#r`"不成立；白名单的 SHA256 闸只覆盖宿主扫描那一条路。

**代码直读（带行号）**

- 基类取消映射：`VisionPluginBase.cs:123-131`。
- 动态端口移除不释放：`VisionPluginBase.cs:459-466`、`:491-505`。
- 编译失败早退：`CSharpScriptEngine.cs:72-78`。
- 重名折叠与孤儿键删除：`CSharpScriptPlugin.cs:409-412`、`:599-606`、`:361-364`。

### 2.6 值得肯定的设计

- 成功脚本按正文指纹缓存（64 条封顶），避免每帧编译；
- 打开配置拍模型快照，取消能还原变量表/连线/手填值；
- 确认时清孤儿连线键，避免 `FlowCompiler` 因历史键整条流程编译失败；
- 共享变量按 `VariableId` 寻址，系统变量恒只读，并行写冲突有声明面；
- 白名单解析失败/缺清单/开关关闭全部 fail-closed，并留审计日志；
- 错误文案中文化程度高，脚本异常剥 `AggregateException`/`TargetInvocationException` 外壳。

## 三、未做

1. 未构建、未跑 `tools\run_gates.ps1`、未启动真机——本报告没有任何"已通过/已修复"的结论。**（审查轮口径；后续批次 A / P1-2 / 第三批的验证证据与假绿说明见各自 code-changes 记录；三批都未启动真机）**
2. 未在配置界面手工复现重名改名丢连线（P1-2 为静态推演；已用 CS27-CS33 断言覆盖代码路径，仍未做真机界面回归）。
3. ~~未验证预览标题/Region 推送的宿主能力边界~~（第三批已收口：口径定为**只承诺 HImage**，不再扩推送面；标题面复核后确认宿主**有**带 `title` + `ImageInfoRow` 的 `PublishPreview` 重载（`PluginPreviewExtensions.cs:48,60`），插件调的是 2 参那个，"Remark 当标题"属可接未接，`Help/02` 已把承诺改到与实现一致。真机预览落格仍未验）。
4. 未评估白名单"真限制"方案的改造成本（`WithMetadataResolver` + 反射仍可绕，需先定威胁模型）。
5. 未改任何代码、未动 `.vms`、未跑插件重建。**（审查轮口径；批次 A、P1-2 与第三批的改动不在此列）**

## 四、下一步（建议顺序）

### 批次 A：低风险、先做

> **状态：已完成（2026-10-24）**。四条全部落地，并新增 CS24-CS26 三条守门断言；证据见同名 code-changes 记录。

1. `CSharpScriptPlugin.cs` 通用 `catch` 前补 OCE 直抛（对齐基类 `when` 判据）；
2. `ScriptContext` 暴露 `CancellationToken` / `ThrowIfCancellationRequested()`；
3. `SetOutput` 端口不存在时 `Warn`（或提供 `TrySetOutput`）；`GetInput` 只在"表里没有这个名字"时 `Warn`，未连线仍返回默认；
4. 手册 8 处陈旧说明按实现改写（P3-1）。

### 批次 B：先定口径

> **状态（2026-10-24）**：第 2 项已拍板并落地（**只承诺 HImage**，见 P2-3 与第三批 code-changes）；第 1 项白名单**仍未拍板**，等你选"承认是预置引用清单（改 `Help/04`）"还是"做真限制"。

1. **白名单定位**：承认它是"预置引用清单 + 依赖卫生"（改 `Help/04`），还是做真限制（受限 `MetadataResolver` + 禁 `#load` + `Resolving` 重校验哈希）。建议前者；
2. ~~**预览能力**：只承诺 HImage，还是补齐 HRegion/HXLD + 标题~~（**已定：只承诺 `HImage`**，不扩推送面；`ShowsDisplayWindow` 是界面与推送共用的唯一口径，非 HImage 行选不出窗口，所以不存在"配了却不显示"的静默态）。

### 批次 C：有回归风险

> **状态（2026-10-24）**：第 1 项（P1-2）与第 2、4 项（P2-2、P2-6）已完成；第 3、5 项（编译失败负缓存、共享变量校验改语法树）未动。

1. ~~变量名唯一性校验 + `RemoveOrphanLinks` 归属判定（P1-2）~~（已完成）；
2. ~~端口重建 `Dispose`~~（已完成：输入端口 `Dispose()` 是唯一退订路径；输出端口**只断值引用、不越权 Dispose**——`OutputPort<T>` 根本不实现 `IDisposable`，要让它可释放必须改 `Core.Interfaces` 契约，属 R12 大改动，本轮按"所有权归生产者"收口）；
3. 编译失败负缓存（与现有 64 条清理策略一致）；
4. ~~共享变量空别名改名联动~~（已完成）；Id 丢失兜底（变量删后重建）**未做**；
5. 共享变量校验从"原文正则"改为"语法树过滤注释/字符串"。

### 验收判据（建议补进 `CSharpScriptChecks.cs`）

> **落地情况（2026-10-24）**：第 2 条 = CS26；第 3 条 = CS27/CS28/CS29；第 4 条 = CS30/CS31；第 5 条 = CS24/CS25；第 6 条按"只承诺 HImage"落地 = CS34/CS35。**第 1 条只做到"插件层 OCE 直抛"（CS26），节点层记 `Skipped` 的端到端没有断言**；**第 7、8 条没有断言**（对应改动未做）。

1. 脚本 `while(true){}` + 取消令牌 → 步骤结果 `Skipped`，不是 `Failed`；
2. `Context.CancellationToken` 在脚本内可读，`ThrowIfCancellationRequested` 能中断循环；
3. 两行同名输入 + 各自连线 → 配置校验拒绝确认，不产生孤儿键；
4. 输出重名 → 端口数与快照数一致，或直接拒绝；
5. `SetOutput("不存在", 1)` → 有 Warn/异常，不再是静默；
6. `HRegion` 输出 + 显示窗口=1 → 预览收到该对象（或手册明确只支持 HImage）；
7. 编译失败连续执行两次 → Roslyn `Compile` 只调用一次（负缓存命中）；
8. 白名单开关关闭/未登记/哈希不符 → 脚本 `using` 该库时报 CS0246，且审计日志三条原文可查。

## 五、踩过的坑（口径陷阱）

1. **"编译失败每帧重编译"是夸大**：成功路径有缓存，只有失败/正文变更才重编译；本次按 P2 记，不按性能专项的 CRITICAL 记。
2. **白名单不是沙箱**：脚本本身在进程内有完整权限（反射也能 `Assembly.LoadFrom`），`#r` 只是让"文档承诺"失真，不是逃逸。报告口径按"控制/文档不成立"写。
3. **`#r` 探针第一次运行报 `requires access to global variables`**：那是 `globalsType` 与空 globals 的探针用法问题，不是编译失败；只看 `Compile()` 诊断。
4. **`#load` 相对路径 CS1504**：探针工作目录不同，改绝对路径即 0 错误；不要据此说 `#load` 不可用。
5. **端口重建的 HImage 滞留不是每轮**：正常轮有基类 `AutoDisposeRoundOutputs` 兜底，只有"被重建移除"的端口例外。
6. **`Help/12:5` 的"没有 CSharpScriptChecks"已过期**：文件存在且已在 `FlowCanvasChecks/Program.cs:294` 注册，不要再按手册判断"无断言"。
7. **`Help/03:55` 的"所有错误行标红"做不到**：`ValidateScript` 只回第一条诊断，`ParseErrorLines` 再全也只能标 1 行。
8. **"旧端口还被上游钉住"不能靠 `InputPort<T>.Value` 判断**（第三批实测踩过）：`Value` 读的是手填值，连线值在 `_cachedLinkedValue` 里；把 `Dispose()` 删掉这种断言照样绿。判据必须数**上游侧的活订阅**（`+=`/`-=` 配平），并先加一条"链接确实建立"的前置断言，否则整条判据空转。
9. **`run_gates.ps1` 的"陈旧 exe"判据把内容文件当源码**：`-Include *.cs,*.xaml,*.csproj` 实测仍会命中 `hardcoded-colors.baseline.txt`，于是一次纯清单改动让 `UIThemeSmokeTest` 永久判"STALE EXE"，而增量 `dotnet build` 不会重链 exe、自愈不了——要么 `--no-incremental` 重链一次，要么改那条过滤器（属工具域，未顺手动）。
