# C#脚本插件 GStack 审查批次 A：取消语义与缺端口告警（2026-10-24）

> 前置文档：`docs/交接报告-2026-10-24-CSharp脚本插件GStack审查.md`（审查台账：0 P0 / 2 P1 / 7 P2 / 10 P3）。
> 本篇只覆盖审查报告里的**批次 A**（低风险三项），批次 B/C 未动。

## 一、起因

GStack 两遍审查 + 红队反证后，批次 A 被定为"先做"的一组：

1. `RunAlgorithm` 的 `catch (Exception)` 把 `OperationCanceledException`（下称 OCE）吞成
   `Success=false` + `脚本运行异常: ...`，而基类 `VisionPluginBase.Execute` 的契约是
   "取消 → `Skipped`（灰行），不是失败（红行）"。用户点停止时，正在跑的脚本节点会先红一下。
2. 脚本拿不到取消令牌，长循环（逐像素遍历、大图匹配）没法配合停止。
3. `Context.SetOutput("拼错的端口", …)` 完全静默；`GetInput` 同样。审查把"静默忽略"记为
   **设计如此**（不能打断流程），但"没有任何提示"让用户无法自查拼写。
4. `Help/` 里 8 处描述与实现不符（详见第五节），属于"手册撒谎"，比缺陷更容易误导现场。

## 二、改前现状（证据）

| 行为 | 改前 | 证据 |
| --- | --- | --- |
| 脚本内 OCE | 被 `catch (Exception)` 吃掉，节点红 | 审查证据 `CSharpScriptPlugin.cs` 执行段 catch 顺序 |
| 取消令牌 | 门面 `ScriptContext` 无成员可拿 | `ScriptContext.cs` 改前只有取输入/写输出/变量/日志/显示 |
| 缺端口 | `SetOutput` 不命中直接 `return`；`GetInput` 返回 null | `ScriptContext.cs` 改前 `_setOutput?.Invoke` 的空实现 |
| 缺失说明 | 手册写"静默忽略"但没写"会告警" | `Help/11`、`Help/12` 改前原文 |

基类口径（`VisionPluginBase.cs:123-131`）：`Execute` 捕获 OCE 且 `context?.CancellationToken.IsCancellationRequested == true`
时设 `Skipped` 并**不再上抛到失败路径**。插件的 `RunAlgorithm` 是 `Execute` 的内层，OCE 必须先原样穿出去。

## 三、改动

### 1. OCE 直抛（`CSharpScriptPlugin.cs:907-911`）

```csharp
catch (OperationCanceledException) when (context?.CancellationToken.IsCancellationRequested == true)
{
    // 取消不是失败（与基类 Execute 同口径）：原样上抛，节点层按"令牌已取消"记 Skipped
    throw;
}
catch (Exception ex) { ... }
```

- 位置在 `catch (CompilationErrorException)` 之后、通用 `catch (Exception)` 之前。
- **带 when 过滤**：脚本自己 `throw new OperationCanceledException()` 但令牌没取消时，
  仍按普通运行异常记失败——不会把"脚本故意抛的 OCE"误判成用户停止。
- 引擎侧不吞：`CSharpScriptEngine.RunAsync` 把 `result.Exception` 原样 `throw`，
  `.GetAwaiter().GetResult()` 解开 `AggregateException` 外壳，OCE 以本类型到达上面的 catch。

### 2. 门面暴露取消（`ScriptContext.cs:30,40,56-63`）

- 构造函数末位新增 `CancellationToken cancellationToken = default`（不破坏既有调用点）。
- 新增两个成员：

| 成员 | 行为 |
| --- | --- |
| `CancellationToken CancellationToken` | 返回本次执行的令牌 |
| `void ThrowIfCancellationRequested()` | 已取消则抛 OCE；未取消什么都不做 |

- 注入点：`CSharpScriptPlugin.cs:872` `cancellationToken: context?.CancellationToken ?? CancellationToken.None`。
- 脚本写法：`Context.ThrowIfCancellationRequested();`（长循环里也可直接查 `Context.CancellationToken.IsCancellationRequested`）。

### 3. 缺端口告警（`CSharpScriptPlugin.cs:823-850`）

```csharp
var warnedMissingPorts = new HashSet<string>(StringComparer.Ordinal);
void WarnMissingPort(string direction, string name)
{
    string key = (name ?? "").Trim();
    if (!warnedMissingPorts.Add(direction + ":" + key)) return;
    context?.Logger?.Warn($"[{InstanceName}] 脚本引用了不存在的{direction}端口「{(key.Length == 0 ? "(空)" : key)}」：请核对左侧变量表与脚本里的字符串");
}
```

- **语义不变**：缺端口仍然"不抛、不中断流程"（`SetOutput` 忽略、`GetInput` 返回 null）。
- **新增可见性**：每轮每个方向每个名字只 Warn 一次；键含方向，所以同名的输入/输出各告警一条。
- 去重集合是**每次 `RunAlgorithm` 新建**：不是全局一次性，下一轮仍会提醒。
- 文案带 `InstanceName`，多节点流程里能定位到具体节点。

### 4. 编辑器侧同步

- `CSharpApiDoc.cs`：`GetInput`/`SetOutput` 摘要补"并记一条 Warn"；新增
  `CancellationToken` 与 `ThrowIfCancellationRequested` 两个 API 条目（后者带插入模板）。
- `CSharpHighlighting.cs`：`ContextApi` 清单补两个成员（输入时不至于被当成未知名）。
- `CSharpScriptView.xaml`：右侧 Context 速查表补一行 `Context.ThrowIfCancellationRequested()  长循环查取消`。

### 5. 手册 8 处按实现改写

| 文件 | 改前（错） | 改后（对） |
| --- | --- | --- |
| `Help/03` 布局图 | 两张卡片（左侧变量 + 右侧编辑器） | 三张：输入/输出/共享变量 + Context 速查 |
| `Help/03` 高亮 | 变量名会被着色 | **不会**；必须写 `Context.GetInput/SetOutput` |
| `Help/03` 错误行 | 所有错误行都标红 | 只标**第一条**；改完再点"校验脚本" |
| `Help/03` 失败契约 | 未提取消 | 取消 → 不算失败，节点灰行；长循环用 `ThrowIfCancellationRequested` |
| `Help/03` 名字对不上 Q/A | 未提告警 | 每轮每名一条 Warn（输入/输出各算一条） |
| `Help/11` 类声明 | 缺 `IPluginConfigContextProvider` | 已补 |
| `Help/11` 枚举显示 | 英文成员名（`Int`/`HImage`） | 中文显示（`ForEnum` + `[Display]`），值仍是成员名 |
| `Help/11` `.vms` 键 | 三键 | 四键：`ScriptText`/`InputVars`/`OutputVars`/`SharedVars` |
| `Help/12` 守门现状 | "没有 CSharpScriptChecks.cs，本条目无人断言" | 文件存在且在 `FlowCanvasChecks/Program.cs:294` 注册 |
| `Help/12` SetOutput | 静默忽略 | 忽略 + Warn |
| `Help/10` 日志级别 | `Warn` 记成 Warning、`Info` 记成 Info | `Error→Error`、`Warning→Warn`、默认→Info |

> 上表 10 行对应审查报告"8 处"的展开（同一页里的两处合并计一行）。

## 四、守门断言（`FlowCanvasChecks/CSharpScriptChecks.cs`）

新增三条，进常规 gate（`FlowCanvasChecks`，与既有 CS1-CS23 同节）：

| 断言 | 判据 | 反证方式 |
| --- | --- | --- |
| CS24 | 拼错端口名：`RunAlgorithm` 不抛、`Success=true` | 把 `WarnMissingPort` 改回 `throw` → 节点失败 |
| CS25 | 每轮每方向每名恰好一条 Warn（输入 1 / 输出 1） | 去掉 `HashSet` 去重 → Warn 变 2；键不含方向 → 1 |
| CS26 | 取消时 `Context.ThrowIfCancellationRequested()` 的 OCE **原样透出** `RunAlgorithm`，且脚本已执行到检查点、`ErrorMessage` 为空 | 删掉 OCE catch 分支 → 被通用 catch 吞成 `脚本运行异常` |

CS26 的两个细节：

- **先预热编译缓存**：本条要在脚本执行途中取消（`CancelAfter(50)` + 脚本 `Sleep(150)`）。
  冷编译耗时会让"取消落在编译期"与"取消落在脚本里"分不开，那样测到的是
  `RunAsync` 的令牌检查而不是门面方法。
- **检查点用输出端口 `Ran` 证明脚本体真的跑了**：第二跑前把它重置为 false，
  只有脚本执行到 `SetOutput("Ran", true)` 才为 true。否则"取消在入口就抛"也会假绿。

## 五、验证证据（2026-10-24）

| 项 | 命令 | 结果 |
| --- | --- | --- |
| 插件构建 | `dotnet build Plugins/Plugin.CSharpScript/Plugin.CSharpScript.csproj -c Debug -m:1 -nodeReuse:false` | 0 警告 / 0 错误，EXIT=0 |
| 断言宿主 | `tools\run_gates.ps1 -Only FlowCanvasChecks` | **3534 通过 / 0 失败**（含 CS24-CS26、HM0-HM17） |
| 帮助探针 | `tools\run_gates.ps1 -Only HelpProbe` | `=== HelpProbe: 全部通过 ===`（74 PASS / 0 FAIL） |
| 产物闸 | `tools\audit_artifacts.ps1` | `RESULT: OK`（39 个插件工程；DLL 新于源码，Help 页齐） |
| 根目录闸 | `tools\audit_root.ps1` | `ROOT LAYOUT OK: 8 files / 43 dirs allowed` |
| 打包核对 | `tr '\0' '\n' < Modules/Plugin.CSharpScript.dll \| grep -a "\.Help\."` | 7 条资源名 = 磁盘 7 页（R42） |

### 一次红行的归因（不是本次改动）

第一次跑 `FlowCanvasChecks` 时 `[M]` 运动段出现 2 条红：

```
[×失败] 前置：假设备命令失败后进入安全停机  →  状态=Online 说明=断言用假运动卡
[×失败] ★底层仍报警时清报警不放行：保持安全停机 + 给出原因  →  清报警=True 状态=Online 原因=
```

- 失败点在**运动控制内核**，与 C# 脚本无调用关系；同一二进制 `-SkipBuild` 复跑即 **3534/0**。
- 归因：假运动卡的轮询时序抖动（前置条件"进入安全停机"尚未生效就被断言）。
- **未修**：这属于既有的 flaky 断言，不在批次 A 范围；已在此记录，供后续单独立项。

## 六、影响面

| 文件 | 性质 |
| --- | --- |
| `Plugins/Plugin.CSharpScript/ScriptContext.cs` | 新增成员（向后兼容） |
| `Plugins/Plugin.CSharpScript/CSharpScriptPlugin.cs` | 执行段 catch 顺序 + 端口 lambda 告警 |
| `Plugins/Plugin.CSharpScript/CSharpApiDoc.cs` | API 速查条目 |
| `Plugins/Plugin.CSharpScript/CSharpHighlighting.cs` | 着色清单 |
| `Plugins/Plugin.CSharpScript/CSharpScriptView.xaml` | 速查表一行 |
| `Plugins/Plugin.CSharpScript/Help/03,10,11,12` | 文案按实现改写 |
| `FlowCanvasChecks/CSharpScriptChecks.cs` | 新增 CS24-CS26 |

- 端口名/类型/持久化键均未变，旧 `.vms` 不受影响。
- `ScriptContext` 构造函数加的是**可选参数**，仅插件内部一处调用，无外部调用点。
- `.vms` 不落取消令牌（运行时值），不涉及序列化。

## 七、未做（批次 B/C，需另行批准）

以下条目**本次未动**，仍在审查报告台账里：

- 白名单第 4 层缺 `IsManagedAssembly` 判定（P1）；
- 预览只推 `HImage`，`HRegion`/`HXLDCont` 选了窗口也不显示（P2，需产品口径）；
- 重复变量名静默合并端口、确认时可能删活连线（P1）；
- 重命名撞名不做迁移（P2）；
- `RemoveDynamicInput`/`ClearDynamicOutputs` 不 `Dispose` 旧端口 → 上游 `ValueChanged` 订阅与 HImage 泄漏（P2）；
- 编译失败不进负缓存（P2）；
- `#r`/`#load` 实际可用但 `Help/04` 写"禁止"（P3，需策略裁决：开放还是禁用）；
- 共享变量空别名在宿主改名后漂移、`Context.Vars` 暴露可变字典、补全给出非 CLR 的 `HXld` 等 P3。

## 八、踩坑记录

1. **OCE catch 必须带 when**：只写 `catch (OperationCanceledException) { throw; }` 会把脚本
   主动抛的 OCE 也当"用户停止"，节点灰行而流程实际没停——比吞掉更难查。
2. **`Success.Value` 是 `object`**：断言里直接传给 `Check(bool)` 编译不过，要 `is true`；
   `ErrorMessage.Value` 同理，判空用 `Convert.ToString(...)`。
3. **预热编译缓存是必要的**：CS26 第一版没预热，取消落在 `RunAsync` 入口，虽然也是 OCE，
   但证明不了 `Context.ThrowIfCancellationRequested` 真的执行过。
4. **gate 的 `-File` 路径在 Git Bash 下会被吃掉反斜杠**：必须写成 `-File 'tools\run_gates.ps1'`
   （单引号）或正斜杠，否则 PowerShell 报"不存在 toolsrun_gates.ps1"。
5. **HelpProbe 偶发文件占用**：首次运行报 `raw-HelpProbe.err 正由另一进程使用`，
   复跑即绿；不是断言失败，但 gate 会按 R183 判 `NO OUTPUT` 红，需要认得出这一档。
