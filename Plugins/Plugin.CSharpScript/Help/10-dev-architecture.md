# C#脚本 · 开发章节：架构与执行模型

> 分类：逻辑控制 ｜ 来源：插件 Plugin.CSharpScript ｜ 读者：开发

源码：`Plugins\Plugin.CSharpScript\CSharpScriptPlugin.cs`（壳兼视图模型）、`CSharpScriptEngine.cs`（Roslyn 脚本引擎与引用集）、
`ScriptContext.cs`（脚本唯一可见的宿主 API）、`ScriptVarDef.cs`（本插件自己的类型枚举）、
`CSharpIntelliSense.cs` / `CSharpTemplates.cs` / `CSharpApiDoc.cs` / `CSharpHighlighting.cs` / `CSharpCompletionData.cs`（编辑器周边）。

## 一 脚本什么时候编译、什么时候执行

三段路径，别混：

| 动作 | 走哪条 | 会不会跑脚本 |
| --- | --- | --- |
| 点「校验脚本」 | `ValidateScript()` → `CSharpScriptEngine.Compile(ScriptText, out error, force: true)` | 只编译不执行；`force` 跳过缓存 |
| 流程运行 | `RunAlgorithm` 内先 `Compile(...)`，成功后 `RunAsync(...).GetAwaiter().GetResult()` | 执行 |
| 打字过程中 | 编辑器的语法高亮与补全（`CSharpHighlighting`、`CSharpIntelliSense`） | 不编译，纯文本层 |

`Compile` 把脚本正文包成 Roslyn `Script<object>`，按正文的 SHA256 指纹进 `static Dictionary CallCache`（配 `CacheLock`），
条目超 64 整体清空；包不出脚本对象时报 `脚本创建失败: {ex.Message}`，编译诊断由 `FormatDiagnostic` 拼成人话。
`RunAlgorithm` 是**同步契约**，所以这里用 `GetAwaiter().GetResult()` 阻塞等异步脚本——调度线程本来就是后台线程，
但**脚本里不要写会长时间自旋的代码**，它占的是一整个调度槽。

## 二 执行时的对象流

```
Compile → TakeExternalLibsAudit（一次性投递审计）→ ScriptContext → RunAsync → 看 globals.Failed → PublishIconicOutputs → Success
```

`ScriptContext` 是脚本里 `Context` 那一个对象，构造时给它三个委托：

- `getInput`：`Inputs.TryGetValue(name, out p)` 后取 `p.GetActualValue()`；端口不存在返回 null。
- `setOutput`：`Outputs.TryGetValue(name, out op)` 后 `op.Value = value`；
  **写失败包成 `InvalidCastException($"写输出端口[{name}]失败：{ex.Message}", ex)` 抛出**（让脚本看见真实原因），
  而**端口名不存在时静默忽略**（写错名字不报错，这是脚本类节点的通病，靠 `Detail`/下游为空反查）。
- 显示委托：`showImage`、`showAnnotated`、`showRich` 三个入口都落到基类的 `PublishPreview` 重载上。

`Host` 是 `internal string`，运行时由壳赋成 `InstanceName`，日志一律加前缀 `[宿主名] 正文`。
脚本调 `Context.Fail(msg)` 会把 `Failed` 置真、`FailMessage` 写成 msg（不给消息时是 `脚本调用 Context.Fail()`）；
壳随后 `Success=false`、`ErrorMessage=FailMessage` 并**直接返回**，不抛异常。
这是"脚本内主动失败"的唯一正道，比 `throw` 好在：错误文案是给人读的。

## 三 异常回传到算子结果的路径

| 脚本里发生 | 壳里的处理 | 端口上的表现 |
| --- | --- | --- |
| 编译诊断 | `catch (CompilationErrorException)`，取其 diagnostics（`ImmutableArray`，先判 `IsDefault` 与 `Length`） | `ErrorMessage` 逐条格式化 |
| 单条诊断能定位行列 | `FormatDiagnosticSafe` 输出 `第 {line+1} 行 第 {col+1} 列: {msg}` | 编辑器能按行找 |
| 定位不到行列 | 退化成 `脚本编译错误: {msg}` | 只给文字 |
| 编译失败但没诊断 | `ErrorMessage.Value = compileError ?? "脚本编译失败"` | `Success=false` |
| 运行期任意异常 | `catch (Exception)`：先剥 `AggregateException` 的内层，再 `while` 剥 `TargetInvocationException` | `ErrorMessage = 脚本运行异常: {inner.Message}` |
| `Context.Fail(...)` | 走 `Failed` 分支，不进 catch | `ErrorMessage = FailMessage` |

剥壳这一步不能省：Roslyn 的 `await` 会把真实异常包成 `AggregateException`，反射调用再包一层 `TargetInvocationException`，
直接把外层 `Message` 给用户看到的是"异常被调用"这类无信息文本。

## 四 引用集是四层攒出来的

`CSharpScriptEngine` 里 `ScriptOptions` 的程序集列表按顺序叠加，`OptionsLock` 保护：

1. **TPA**：`TRUSTED_PLATFORM_ASSEMBLIES` 里的框架程序集（`System.*`、`Microsoft.CodeAnalysis.CSharp.Scripting` 自身除外）。
2. **当前已加载的程序集**：宿主跑起来后进程里已有的那些。
3. **宿主根目录扫 `*.dll`**：投放到 exe 目录的第三方与插件 dll。
4. **`ExternalLibs\` 白名单**：只有登记进 `whitelist.json` 且 SHA256 校验通过的才加进来。

第 1~3 层都走同一个 `AddAssembly`：先用 `AssemblyName.GetAssemblyName(path)` 判"是不是托管程序集"，
**原生 dll（`halcon.dll` / `hcanvas.dll` 等）在这里抛 `BadImageFormatException` 并被就地丢弃**——
整目录扫描不判掉就会把原生 dll 塞进引用集。

第 4 层（白名单）**没有这道判断**：哈希通过后直接 `MetadataReference.CreateFromFile(dll)`。
`CreateFromFile` 对原生 dll 不抛，失败推迟到脚本 `Compile`，表现是 **CS0009 打红全部脚本**。
所以白清单里混进一个原生 dll 是"全局编译灾难"，不是"那条库单独不可用"——这条是白名单定位（见 `Help/04`）尚未拍板的实际风险。

## 五 白名单是 fail-closed 的

审计条目写进静态 `ExternalAudit`（`AuditLock`），由 `TakeExternalLibsAudit()` **一次性取走**，
`RunAlgorithm` 在编译之后、执行之前按严重级别（`Error` 走 Error 面、`Warning` 走 Warn 面、其余走 Info）投递给日志一次，
不重复刷屏。审计原文（照源码）：

- `[扩展库白名单] ExternalLibs 目录存在但缺少 whitelist.json：拒绝加载任何扩展 dll（默认拒绝）。`
- `[扩展库白名单] whitelist.json 解析失败({…})：拒绝加载任何扩展 dll（默认拒绝）。`
- `[扩展库白名单] 开关 enabled 非 true：扩展库加载已被手动关闭。`
- `[扩展库白名单] 发现未登记 dll，已忽略：{name}（实际 SHA256=…，如确需使用请登记进 whitelist.json）`
- `[扩展库白名单] {name} 与宿主已有程序集同名：以宿主版本为准，跳过扩展副本。`
- `[扩展库白名单] 哈希不符，疑似被篡改，拒绝加载：{name}（清单=… 实际=…）`
- `[扩展库白名单] 校验通过，已加入脚本引用：{name}（批准人：{approvedBy}，哈希：…）`

"目录存在但没有清单"也拒绝，是刻意的：宁缺勿滥。改名或改哈希都会命中"疑似被篡改"那条。

## 六 画布与窗口交接

推送条件只有一个口径：变量行 `DisplayWindow >= 1` 且 `ShowsDisplayWindow` 为真（等价于"类型是 `HImage`"）、
且端口值确实是 `HImage`。`ScriptVarDef.ShowsDisplayWindow` 是**界面那一列与推送判据共用的唯一来源**，
所以区域/轮廓行根本选不出窗口，不存在"配了却不显示"的静默分支。
`HRegion`、`HXLDCont` 这类要在脚本里自己合成成图像再 `SetOutput`——本插件**没有**图像脚本那套显示后端合成路径。
日志、`ShowImage` 系列、`MarkLine`/`MarkText` 标注（`NewMarks()` 造列表，`MeasureAnnotation` 带 `Points` 与 `Color`，默认 `green`）
都通过宿主出口走，节点自己绝不开窗口（AGENTS.md R36）。
