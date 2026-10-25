# C#脚本 · 开发章节：不变量与守门现状

> 分类：逻辑控制 ｜ 来源：插件 Plugin.CSharpScript ｜ 读者：开发

**先说守门现状**：`FlowCanvasChecks\CSharpScriptChecks.cs` 已存在并已随 `FlowCanvasChecks` 进常规 gate，
现在共 CS1–CS40（2026-10-24 起含预览口径 CS34/CS35、端口重建释放 CS36/CS36b/CS37、别名固化 CS38/CS38b、
`Vars` 只读 CS39/CS39b、补全类型名 CS40）；
但本页列的不变量多数仍只靠人工核对——下表"守门断言名"写"无断言，靠人工核对"的都是真的没钉住，别当成已覆盖。

## A 组：违反就报错（症状是响的）

1. **症状** 节点红，`ErrorMessage` 是 `第 N 行 第 M 列: CS1002: ...` 这种可定位文本。
   **机理** `CompilationErrorException` 的 diagnostics 是 `ImmutableArray`，先判 `IsDefault` 与 `Length` 再取，
   拿不到行列时退化成 `脚本编译错误: {msg}`。
   **守门断言名** 无断言，靠人工核对（故意少写一个分号，看是否给出编辑器行号）。
2. **症状** 运行期异常被报成"异常被调用"这类无信息文本。
   **机理** 没剥 `AggregateException` 与 `TargetInvocationException` 就取 `Message`。正确写法是剥到内层再
   `脚本运行异常: {inner.Message}`。
   **守门断言名** 无断言，靠人工核对（脚本里 `throw new InvalidOperationException("测试")`，看提示是否是"测试"）。
3. **症状** 脚本写 `Context.SetOutput("NotAPort", 1)`，值没写进去，日志里有一条 Warn。
   **机理** 名字不存在时忽略并 Warn（同一轮同名一次，避免长循环刷屏）；类型不符才抛并包成
   `写输出端口[X]失败：...`。
   **守门断言名** CS24 / CS25（不抛且成功、每轮每方向每名只一条 Warn）。
4. **症状** 脚本调 `Context.Fail("来料异常")` 后节点失败，原因就是这句。
   **机理** `Failed` 与 `FailMessage` 走成功返回路径之前判定；不调 `Fail` 而直接 `return` 视为成功。
   **守门断言名** 无断言，靠人工核对。
5. **症状** 清空脚本后节点崩在缓存处。
   **机理** `OnScriptTextChanging` 把 null 归一成空串；去掉它就是一条看不懂的 NRE。
   **守门断言名** 无断言，靠人工核对。
6. **症状** 换了工程后脚本节点连线全丢。
   **机理** `ApplyConfigValues` 里"先 base 后重建端口"的顺序被调换，宿主按名接不回来。
   **守门断言名** 无断言，靠人工核对（存旧工程、改代码、重开）。
6b. **症状** 两行输入同名（或名字为空）时点「确定」被拒绝，状态栏给出冲突行号；「校验脚本」同样先报。
   **机理** `OnConfirm` 在任何写入之前跑 `GetVarNamesError()`；重名/空名直接抛 `InvalidOperationException`，
   宿主的 `Confirm()` 与试运行入口都 catch 并保持窗口打开。重名时端口只会建一个（先到先得），
   另一行的连线随后会被 `RemoveOrphanLinks` 当孤儿删掉——这正是本条要挡的静默故障。
   撞名改名期间 `MigrateInputKey` 不迁移、不前进锚点；`RemoveOrphanLinks` 把活行的锚点也算有效键，
   所以连线不丢，改成不冲突的名字后自动迁过去。输出侧同名行不建端口、也不进 `OutputPortDefinitions`
   （快照与端口 1:1），确认照样被拒。
   **守门断言名** CS27-CS33（输入重名拒绝 + 两条连线都保留 + 改名后迁移、输出端口/快照 1:1、空名拒绝、校验脚本先报）。

## B 组：违反不报错，只是行为静默变坏

7. **症状** 日志里出现"拒绝加载任何扩展 dll（默认拒绝）"。
   **机理** `ExternalLibs\` 存在但缺 `whitelist.json`，或清单解析失败——**fail-closed**。
   这不是故障，是安全默认；要真用扩展库就按清单登记（批准人与哈希都进审计）。
   **守门断言名** 无断言，靠人工核对（审计文本逐字照源码，见 10 页第五节）。
8. **症状** 脚本里 `using` 一个第三方库失败，但 dll 明明在目录里。
   **机理** 引用集四层之一没覆盖到它（TPA、已加载、宿主根目录扫描、白名单）。
   根目录扫描时原生 dll 会被 `AssemblyName.GetAssemblyName` 抛 `BadImageFormatException` 挡掉（CS0009 那一族）——
   **这个判断不能省**，省了整个引用集构建失败。
   **守门断言名** 无断言，靠人工核对。
9. **症状** 同名程序集两个版本，脚本用的是宿主那份。
   **机理** 白名单里"与宿主已有程序集同名：以宿主版本为准，跳过扩展副本"。
   **守门断言名** 无断言，靠人工核对。
10. **症状** 改了脚本内容但行为像没改。
    **机理** 指纹缓存按 SHA256 命中旧编译结果；「校验脚本」用 `force: true` 强制重编译，运行路径不会。
    **守门断言名** 无断言，靠人工核对（改一个字符串常量后先点校验再运行）。
11. **症状** 反复改脚本内存上涨。
    **机理** `CallCache` 只在条目数超 64 时整体清空，不做逐条淘汰。
    **守门断言名** 无断言，靠人工核对。
12. **症状** 区域或轮廓输出"选了窗口却没显示"。
    **机理** 承诺与实现同源：`ScriptVarDef.ShowsDisplayWindow`（= 类型是 `HImage`）同时决定界面那一列
    和 `PublishIconicOutputs` 的推送判据，宿主预览通道（`PublishPreview`）只收 `HImage`。
    用 `IsIconic` 判会让区域/轮廓行配得出窗口却什么都不发生——所以直接把那一列收掉，配不出就不可能踩。
    要显示区域/轮廓，脚本里 `PaintRegion`/`PaintXld` 合成成 `HImage` 再写输出。
    **守门断言名** CS34（HImage 行推一帧到指定窗口；HRegion 行选了窗口一帧都不推）、
    CS35（逐类型核对 `ShowsDisplayWindow` 只在 HImage 为真，且区域行 `IsIconic` 仍为真——连线语义没被牵连）。
12b. **症状** 反复改变量名/类型后内存与 HALCON 句柄一直涨。
    **机理** 重建动态端口时，摘掉的旧**输入**端口必须 `Dispose()`——`RemoveDynamicInput` 只从字典里删键，
    不解上游 `OutputPort.ValueChanged` 的订阅，旧端口和它缓存的上游图像会永久留着。
    旧**输出**端口只**断引用**（值是 `IDisposable` 才置 null）：那张图可能是上游直通值或借来的预览图，
    所有权不归本插件，重建时 `Dispose` 它等于替别人释放正在用的对象。
    **守门断言名** CS36（改名重建后旧输入端口 `LinkedSource == null`、新端口是另一个对象）+
    CS36b（反证：上游随后改成 42，旧端口的值不再跟着变——订阅确实解了）、
    CS37（被摘掉的旧输出端口值已置 null，而图像本身仍 `IsInitialized()`——没被越权释放）。
12c. **症状** 共享变量别名留空，之后变量在变量管理里改名，脚本 `GetGlobal("旧名")` 取不到值。
    **机理** 别名是**脚本里的键**，必须在那个键还权威的时刻固化：视图选定变量时、
    宿主快照刷新时（覆盖 `Name` **之前**先按旧名补一次）、以及 `OnConfirm` 落盘前，三处都调
    `CaptureAliasIfEmpty()`；空名（还没选变量）不固化，避免把一个空串锁死。
    `EffectiveAlias` 的语义没改——仍是"别名非空用别名，否则用当前名"。
    **守门断言名** CS38（绑定候选后把候选改名并刷新：`EffectiveAlias` 不动，`Name` 跟着宿主走）、
    CS38b（`OnConfirm` 兜底固化手填行；名字为空的行**不**固化——不把空串锁成键）。
12d. **症状** 脚本里 `Context.Vars["x"] = 1` 编译得过、跑起来把流程变量池改坏。
    **机理** `Vars` 返回 `ReadOnlyDictionary` 视图（按源字典实例缓存，源换了才重建），写它抛
    `NotSupportedException`；签名仍是 `IDictionary<string, object>`，老脚本 `foreach`/`ContainsKey`/取值不受影响。
    写走 `SetVar`。
    **守门断言名** CS39（脚本里写 `Context.Vars[...] = …` 抛 `NotSupportedException`，`SetVar` + `Vars.ContainsKey` 照常可用）、
    CS39b（反证半写：写失败后池子里确实没多出那个键）。
12e. **症状** 补全插入 `HXld` 后编译报 CS0246。
    **机理** `HXld` 是 `ScriptVarType` 的枚举成员名，不是 CLR 类型；补全关键字表只收真实类型名
    （`HXLDCont`）。枚举成员名不动（它是 `.vms` 里的序列化值，改 = 打断已配流程，见 R40）。
    高亮词表（`CSharpHighlighting.KnownTypes`）同批去掉了 `HXld`，但它是 private，**没有断言覆盖**，改它靠人工核对。
    **守门断言名** CS40（`CSharpApiDoc.CommonKeywords` 含 `HXLDCont`、不含 `HXld`）。
13. **症状** 随图信息条数比预期少。
    **机理** `ToInfoRows` 跳过标签为空的条目。
    **守门断言名** 无断言，靠人工核对。
14. **症状** 脚本里 `GetVar<T>` 拿到默认值（0 或 null）而不是异常。
    **机理** 转换失败返回默认值是有意的宽松；要判"到底有没有这个变量"，先看 `Vars.ContainsKey(...)`。
    **守门断言名** 无断言，靠人工核对。
15. **症状** 类型下拉出现英文成员名（`Int`、`HImage`…）。
    **机理** 视图经 `StepConfigOption.ForEnum` 读 `[Display(Name)]`；只有新增枚举成员漏标 `[Display]` 时才会回退成员名。
    **守门断言名** 无断言，靠人工核对；相关通用显示检查在项目级 R40 台账里。

## 静态扫描里能借到的力

- AGENTS.md R36 / R37：全仓窗口纪律（只有 `AnnotationBaker` 允许 `OpenWindow`），脚本里自己 `new HWindow` 或开窗口都会被这类静态扫描与代码评审抓住。
- `ResultDisplayCheck` 的画布与窗口静态扫描覆盖"发布路径"，不覆盖脚本内容。
- 手册层由 `HelpManualCheck` 的 HM 系列守（打包、扁平一层、H1、`NN-` 前缀、编码、页数与行数分档）。

## 改后必做动作

```
1) 改 ScriptContext 任一成员：跑一次带完整用法的样例脚本（取图、标注、随图信息、Fail、SetOutput、取消各来一遍）
2) 改引用集四层或白名单校验：故意放一个未登记 dll 与一个改过内容的 dll，确认两条审计文本都出现且都没被加载
3) 改端口映射或类型枚举：用旧 .vms 开一次，核对变量表每行的类型与端口 CLR 类型仍对得上
4) 改变名校验 / 孤儿键清理：造两行同名且都已连线 → 点「确定」必须被拒且两条线都在；改成不冲突的名字再确认，线要跟过去
5) 改动态端口重建路径：改名/改类型各跑一次，确认旧输入端口已解订阅、旧输出端口的图像没被本插件释放（CS36/CS36b/CS37）
6) 改显示窗口/预览判据：`ShowsDisplayWindow` 与 `PublishIconicOutputs` 必须同一口径，界面配得出但推不出去的组合不许存在（CS34/CS35）
7) 改共享变量别名时机（视图选择 / 快照刷新 / 确认）：三处都要调 `CaptureAliasIfEmpty`，漏一处就会在宿主改名时漂（CS38/CS38b）
8) 改 Help\ 页面：HM5 HM6 HM7 HM8 HM9 HM12b HM13（M 档开发章节 ≥150 行）
```
