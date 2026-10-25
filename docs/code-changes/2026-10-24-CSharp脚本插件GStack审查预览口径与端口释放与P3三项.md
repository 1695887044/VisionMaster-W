# C#脚本插件 GStack 审查：预览口径 + 端口重建释放 + P3 三项（2026-10-24）

> 前置：`docs/交接报告-2026-10-24-CSharp脚本插件GStack审查.md` 里的 **P2-2 / P2-3 / P2-6 / P3-2 / P3-3**
> （批次 A 与 P1-2 已同日先行落地，见
> `docs/code-changes/2026-10-24-CSharp脚本插件GStack审查批次A（取消语义与缺端口告警）.md`、
> `docs/code-changes/2026-10-24-CSharp脚本插件GStack审查P1-2（变量重名与孤儿连线）.md`）。
> 用户拍板：**这三项现在就做**，预览一项**只承诺 HImage**（不给区域/轮廓扩宿主预览通道）。

## 一、缺陷回顾（改前三条）

### 1. 预览只推 `HImage`，但界面按 `IsIconic` 给「显示窗口」那一列（P2-3）

`ScriptVarDef.IsIconic` 为真的类型有四个：`HObject`、`HImage`、`HRegion`、`HXld`。
视图把「显示窗口」下拉绑在 `IsIconic` 上，而 `PublishIconicOutputs` 推送时还要再过一道
`port.Value is HImage`。于是：

- 区域行 / 轮廓行**选得出窗口**（界面给了这个入口）；
- 执行完**什么都不发生**——没有异常、没有 Warn、没有状态栏提示；
- 用户唯一的线索是"图没出来"，而原因看起来像是"我的图没画对"。

承诺（界面）与实现（推送）分家，且分家的方向是**界面比实现更宽**——这类缺陷最贵，
因为它是靠"用户试出来"的，日志里一行都没有。

### 2. 动态端口重建不释放旧端口（P2-2）

`VisionPluginBase.RemoveDynamicInput(name)` / `ClearDynamicOutputs()` 只做一件事：
`_inputs.Remove(key)` / `_outputs.Remove(key)`（见 `Shard/Core.Interfaces/VisionPluginBase.cs:459-466`、
`:491-509`）。摘字典键**不等于**解订阅：

- `InputPort<T>.LinkedSource` 的 setter 里做的是 `_linkedSource.ValueChanged += UpstreamValueChanged`
  （`Shard/Core.Interfaces/DataPort/InputPort.cs:136-166`），只有 `InputPort<T>.Dispose()`
  （同文件 `:330`，走 `LinkedSource = null` 复用退订路径）才会 `-=`；
- 于是 C#脚本每次改变量名 / 改类型（编辑期逐字符都在重建），旧端口连同它缓存的
  `_cachedLinkedValue`（常常是上游那张 `HImage`）都被**长寿命的上游事件委托钉住**——
  上游若是跨 session 的 `RuntimeVariableProxyPort` 那种根，就是逐次累积的托管泄漏 + HALCON 句柄不回收；
- 输出侧镜像问题：旧 `OutputPort` 被摘掉后仍**握着上一轮的值引用**。基类轮首的
  `DisposePreviousRoundOutputs` 只遍历 `Outputs` 里**当前还在**的端口（`VisionPluginBase.cs:175-191`），
  已经被摘键的旧端口它再也看不见。

置信度 9/10（两条都是读基类源码直接确认的，不依赖现场观察）。

### 3. P3 三项

| # | 现象 | 根因位置 |
| --- | --- | --- |
| P3-2 | 共享变量**别名留空**时，脚本里的键实际取 `Name`，而 `Name` 每次开配置都被宿主快照覆盖成当前名 → 变量管理里一改名，脚本里写好的 `GetGlobal("旧名")` 凭空变成"未声明" | `CSharpScriptPlugin.RefreshSharedVarDefs` 覆盖 `Name`；`ScriptSharedVarDef.EffectiveAlias` 回退到 `Name` |
| P3-3a | `Context.Vars` **直接暴露** `IExecutionContext.LocalVariables` 那个可变字典：脚本 `Context.Vars["x"] = 1` 编译得过、跑起来真的写进流程变量池，绕开 `SetVar` 的口径（类型转换、命名约定） | `ScriptContext.Vars` getter |
| P3-3b | 补全关键字表里有 `HXld`，但 `HXld` 只是 `ScriptVarType` 的**枚举成员名**，CLR 类型是 `HXLDCont` → 选中插入即 `CS0246`（"怎么写都不对"那一类） | `CSharpApiDoc.CommonKeywords`、`CSharpHighlighting.KnownTypes` |

## 二、用户拍板：只承诺 `HImage`

预览这一项有两条路，用户选了第 2 条：

1. **扩实现**：给区域/轮廓加合成路径（`PaintRegion` → 推图），让界面那一列对四个 iconic 类型都成立；
2. **收承诺**：宿主预览通道只收 `HImage` 是既有事实（`Shard/Core.Controls/Events/PluginPreviewExtensions.cs`
   的 `PublishPreview` 签名就是 `HImage`），界面也就不再给非图像行开这个入口。

选 2 的理由（记在这里，避免以后有人"顺手加回去"）：

- 本插件**没有**图像脚本那套 `HDevDisplayBackend` 逐算子显示后端（AGENTS.md R36 的豁免清单只有一条，
  不该为脚本便利再开第二条）；
- 区域/轮廓要上屏，脚本里两行就能自己合成（`HOperatorSet.PaintRegion` / `PaintXld`），
  语义比"插件替我猜怎么合成"更明确；
- 收承诺能做成**一个口径**：界面那一列与推送判据同源于
  `ScriptVarDef.ShowsDisplayWindow`，配不出窗口 = 不可能踩"选了却不显示"。

## 三、改动清单

### 1. 预览口径唯一来源（`Plugins/Plugin.CSharpScript/ScriptVarDef.cs`、`CSharpScriptPlugin.cs`、`CSharpScriptView.xaml`）

`ScriptVarDef.cs:122` 新增属性，`:97` 在 `Type` 的 setter 里一起发通知（换类型要当场重算那一列）：

```csharp
/// <summary>本行是否该出现「显示窗口」下拉（界面与承诺的唯一口径）。</summary>
public bool ShowsDisplayWindow => Type == ScriptVarType.HImage;
```

`DisplayWindow` 的文档注释同步收窄成"**只有 HImage 输出生效**：宿主预览通道（`PublishPreview`）只收 HImage，
区域/轮廓要先在脚本里合成成图像再写输出"。

`IsIconic` **没动**——它的职责是另一件事（输入行必须连线、不能手填文本），
把它顺手改成"等于能否推预览"会打断输入侧语义（CS35 专门钉这条不被牵连）。

`CSharpScriptPlugin.cs:1026-1039` 的 `PublishIconicOutputs`：

```csharp
// 按"界面给了显示窗口的类型"判定 = 只有 HImage。
if (v == null || v.DisplayWindow < 1 || !v.ShowsDisplayWindow) continue;
if (!Outputs.TryGetValue(v.Name, out var port)) continue;
if (port.Value is HImage hi && hi.IsInitialized())
    this.PublishPreview(hi, v.DisplayWindow);
```

`CSharpScriptView.xaml:595-609`：「显示窗口」那一整块（标签 + ComboBox）的 `Visibility`
由 `DataTrigger Binding="{Binding ShowsDisplayWindow}"` 决定（默认 `Collapsed`），
ComboBox 的 ToolTip 补"仅 HImage 输出会推送"。

### 2. 输入端口重建即 Dispose（`CSharpScriptPlugin.cs:453-465`）

```csharp
Inputs.TryGetValue(name, out var oldPort);
RemoveDynamicInput(name);
// 摘掉字典键不等于解订阅：上游 OutputPort 的 ValueChanged 委托仍指着这个旧端口，
// 于是每次改名/改类型都留下一份"旧端口 + 它缓存的上游图像"。…
if (oldPort is IDisposable disposable)
{
    try { disposable.Dispose(); }
    catch { /* 退订失败不该挡住端口重建 */ }
}
```

- 判据用 `is IDisposable` 而不是强转 `InputPort<T>`：端口是按声明类型新建的泛型实例，
  重建点只有 `IInputPort` 接口面；`OutputPort<T>` 不实现 `IDisposable`，所以输出侧不能套同一招。
- `Dispose()` 幂等（`InputPort.cs:330` 有 `_disposed` 门），重复调用无副作用。
- `catch` 吞掉是刻意的：退订失败不该挡住端口重建（重建失败会让连线整体接不回来，那是响的故障，
  比静默泄漏更糟）。

### 3. 输出端口重建只断引用，绝不越权 Dispose（`CSharpScriptPlugin.cs:490-545`）

```csharp
var previous = new Dictionary<string, IOutputPort>(StringComparer.Ordinal);
foreach (var kvp in Outputs) previous[kvp.Key] = kvp.Value;
ClearDynamicOutputs();
...
if (StepData != null) StepData.OutputPortDefinitions = snapshot;
ReleaseRemovedOutputValues(previous);
```

```csharp
private void ReleaseRemovedOutputValues(IReadOnlyDictionary<string, IOutputPort> previous)
{
    foreach (var kvp in previous)
    {
        if (Outputs.TryGetValue(kvp.Key, out var current) && ReferenceEquals(current, kvp.Value)) continue;
        if (kvp.Value.Value is not IDisposable) continue;   // int/string 之类没必要惊动 ValueChanged
        try { kvp.Value.Value = null; }
        catch { /* 端口已失效不该挡住重建 */ }
    }
}
```

三个刻意的设计点：

1. **不 Dispose**。输出值经常就是上游那张图（脚本原样透传）或 `ShowImage` 借来的预览图，
   释放权归生产者。基类自己也是这个口径——`DisposePreviousRoundOutputs` 用私有的
   `IsUpstreamLinkedValue`（`VisionPluginBase.cs:197-205`）跳过透传引用；那个判据是 private，
   插件拿不到，所以这里用更保守的"只断引用"。
2. **只在值是 `IDisposable` 时才置 null**。`int`/`string` 之类置 null 会白白发一次
   `ValueChanged`，把下游端口的刷新链惊动一轮（编辑期逐字符改名 = 每次惊动一次）。
3. **同对象跳过**。重建后同名端口如果是同一个实例（当前实现不会，但判据留着不亏），
   把它刚装的默认值抹掉就是真 bug。

### 4. 共享变量空别名在"脚本键还权威"的时刻固化（三处）

`ScriptSharedVarDef.cs:117-130` 新增：

```csharp
public void CaptureAliasIfEmpty()
{
    if (!string.IsNullOrWhiteSpace(Alias)) return;
    string name = (Name ?? "").Trim();
    if (name.Length == 0) return;      // 还没选变量：不把空串锁成键
    Alias = name;
}
```

三个调用点（缺一不可）：

| 位置 | 时机 | 少了它会怎样 |
| --- | --- | --- |
| `CSharpScriptView.xaml.cs:211`（`SharedVar_SelectionChanged` 末尾） | 用户在候选里选定变量的那一刻 | 只靠后两处：中途宿主改名就先漂一次 |
| `CSharpScriptPlugin.cs:850 + 857`（`RefreshSharedVarDefs`，**覆盖 `Name` 前一次、后一次**） | 宿主推快照时 | 第一次绑定没问题，但改名后的那次刷新直接把脚本键换掉 |
| `CSharpScriptPlugin.cs:183-185`（`OnConfirm`，在名校验之后、`base.OnConfirm` 之前） | 点「确定」落盘前 | 手填/旧方案迁移来的行（没走候选选择）别名仍空，下次改名照漂 |

`RefreshSharedVarDefs` 里注释写清了顺序为什么不能反：

```csharp
// 顺序有讲究：先把"脚本此刻用的键"按**旧名**固化，再覆盖成宿主当前名。
// 反过来做就等于让脚本键跟着变量管理里的改名一起漂。
```

`EffectiveAlias` 的语义**没改**（仍是"别名非空用别名，否则用当前名"），改的只是"什么时候把空别名落成实值"。
`Alias` 本来就在 `ScriptSharedVarDef` 的序列化面里，所以「取消」还原、`CloneSharedVars` 快照都自动带上，不需要额外收尾。

### 5. `Context.Vars` 改只读视图（`ScriptContext.cs:147-164`）

```csharp
public IDictionary<string, object> Vars
{
    get
    {
        var dict = _exec?.LocalVariables;
        if (dict == null) return EmptyVars;
        if (_varsCache != null && ReferenceEquals(_varsSource, dict)) return _varsCache;
        _varsSource = dict;
        _varsCache = new ReadOnlyDictionary<string, object>(dict);
        return _varsCache;
    }
}
```

- **签名不变**（仍是 `IDictionary<string, object>`）：老脚本里
  `IDictionary<string, object> v = Context.Vars;`、`foreach (var kvp in Context.Vars)`、
  `Context.Vars.ContainsKey(...)` 全部照编译、照运行；只有写（索引器赋值 / `Add` / `Remove`）
  变成 `NotSupportedException`。
- 按**源字典实例**缓存包装：`LocalVariables` 每轮可能是不同字典，`ReferenceEquals` 变了才换壳，
  不每取一次就 new 一个包装。
- `_exec` 为 null 时返回 `EmptyVars`（也是只读的静态空壳），不返回 null——脚本里 `Vars.Count` 不该 NRE。

`CSharpApiDoc.cs` 里 `Vars` 的一句话说明改成"运行期变量池的**只读视图**（写它会抛
`NotSupportedException`；要写请用 `SetVar`）"，速查卡片与悬浮提示同步。

### 6. 补全/高亮不再给出非 CLR 类型名

| 位置 | 改动 |
| --- | --- |
| `CSharpApiDoc.cs:171-172` | `"HImage", "HObject", "HRegion", "HXLDCont", "HTuple"`，并在上一行写死理由：轮廓的 CLR 类型是 `HXLDCont`，枚举名 `HXld` 编译不过（CS0246） |
| `CSharpHighlighting.cs:40` | 词表里 `HXld` → `HXLDCont`（private 字段，无断言覆盖，靠人工核对；手册 12 页第 12e 条注明） |
| 不动 | `ScriptVarType.HXld` 枚举成员本身——它是 `.vms` 里的序列化值，改名 = 打断已配流程（AGENTS.md R40 同一口径） |

### 7. 手册同步（5 页）

| 页 | 改动 |
| --- | --- |
| `Help/02-ports-and-parameters.md` | 「显示窗口」段改为**只有 `HImage` 输出有**，并给出区域/轮廓的上屏做法（`PaintRegion` / `PaintXld` 合成）；共享变量段新增"别名留空 = 点「确定」时固化成当时的变量名"；参数表 `DisplayWindow` 行由"仅图形类输出可见"改成"只有 `HImage` 输出行出现这一项"；**`Remark` 行删掉"推图标题来源"**（插件实际调的是 `PublishPreview(图, 窗口)` 这个 2 参重载，标题一直是空的；注意 `Shard/Core.Controls/Events/PluginPreviewExtensions.cs:48,60` **确实有**带 `title` + `ImageInfoRow` 的重载，所以"接上 Remark 当标题"是插件侧一行改动，不是宿主能力缺口——本轮按"只承诺 HImage"的范围**没做**，`Help/02` 只把承诺改到与实现一致）；「口径与失败策略」里"输出端口不存在：忽略，**不报错**"改成"忽略并记一条 Warn（每轮每名一次）"——那一句是批次 A 改代码时漏在这页上的第二处漂移（同页 :34 与 `Help/03:125` 当时已改对，同一页自相矛盾） |
| `Help/03-editor-and-errors.md` | 界面布局表：输出行列构成写"显示窗口（**只有 `HImage` 行出现这一项**）"；共享变量行写"别名（留空则选定/确认时固化为当时的变量名）" |
| `Help/10-dev-architecture.md` | 第六节「画布与窗口交接」的推送条件改写成 `ShowsDisplayWindow` 唯一口径 + "界面配不出 = 不存在静默分支"；**第四节「引用集四层」按源码改写**：原文说"第 3、4 层都要过托管判断"，实际第 1~3 层走 `AddAssembly`（`CSharpScriptEngine.cs:127` 判托管并 catch），**第 4 层白名单是直接 `CreateFromFile`（:314），没有这道判断**，于是把"原生 dll 混进白名单 → 全部脚本 CS0009"这条真实后果写清（P2-1 白名单定位仍未拍板，这里只纠事实、不下策略） |
| `Help/11-dev-host-contracts.md` | 第三节末尾写清 `HXld` 是枚举成员名、补全给 `HXLDCont`；第四节新增"**重建即释放**"两条（输入 `Dispose` / 输出只断引用，含"绝不越权 Dispose"的理由）；`Vars` 行改"只读视图" |
| `Help/12-dev-invariants.md` | 开头守门现状改成 CS1–CS40 并列出本批新断言；不变量 12 重写（CS34/CS35），新增 12b（端口释放，CS36/CS36b/CS37）、12c（别名固化，CS38/CS38b）、12d（`Vars` 只读，CS39/CS39b）、12e（补全类型名，CS40，并注明高亮词表无断言）；「改后必做动作」补 5)/6)/7) 三条并把 Help 那条顺延成 8) |

## 四、守门断言（`FlowCanvasChecks/CSharpScriptChecks.cs` CS34–CS40，共 11 条）

新增 `using Core.Events;` / `using HalconDotNet;`，`Run()` 末尾挂 6 个新段落，
section 标题与类注释同步；辅助件两个：

- `ColumnRuleViolations()`：把 8 个 `ScriptVarType` 成员逐一走一遍 `ShowsDisplayWindow`，
  返回违例清单（空 = 判据与承诺一致）；
- `CountingOutputPort`：只数订阅往返的假 `IOutputPort`（`SubscribeCalls` / `UnsubscribeCalls` /
  `LiveSubscribers`）。为什么必须自己造：`OutputPort<T>` 的事件委托链在类型外部读不到。

| 断言 | 判据 | 变异反证（实测） |
| --- | --- | --- |
| CS34 | 输入灌一张 `HImage` + 一个 `HRegion`；`Img`(HImage, 窗口2) 推**恰好一帧**且 `ViewIndex==2`；`Reg`(HRegion, 窗口1) **一帧都不推** | `PublishIconicOutputs` 的 `DisplayWindow < 1` 改成 `< 99` → `窗口2=0` 红 |
| CS35 | `ShowsDisplayWindow` 只在 `HImage` 为真（逐类型）；区域行 `IsIconic` **仍为真**（连线语义没被牵连） | `ShowsDisplayWindow => IsIconic` → `逐类型违例=HRegion、HXld、HObject` 红 |
| CS36a | 前置：链接确实建立（上游挂 1 个活订阅），否则 CS36b 是空转 | 前置件，本身不反证（若重建逻辑连链接都不建立，它会先红） |
| CS36 | 改名重建后旧输入端口 `LinkedSource == null`、新端口是另一个对象 | 注释掉 `disposable.Dispose()` → `仍指着上游` 红 |
| CS36b | 上游 `-= ` 恰好 1 次、`LiveSubscribers` 归零（泄漏本体） | 同上一条 mutation → `退订 -=0；仍挂在上游的订阅者=1` 红 |
| CS37 | 被摘掉的旧输出端口 `Value == null`，而那张图 `IsInitialized()` 仍为真（没被越权释放） | 注释掉 `ReleaseRemovedOutputValues(previous)` → `旧端口值=仍持有` 红 |
| CS38 | 绑定候选 → 刷新；再把候选改名 → `EffectiveAlias` 不动、`Name` 跟着宿主走 | 注释掉 `RefreshSharedVarDefs` 里两次 `CaptureAliasIfEmpty()` → `改名后 EffectiveAlias=检测结果` 红 |
| CS38b | `OnConfirm` 兜底固化手填行；名字为空的行**不**固化 | 注释掉 `OnConfirm` 里的固化循环 → `第1行 Alias=`（空）红 |
| CS39 | 脚本里 `Context.Vars["b"] = 2` 抛 `NotSupportedException`；`SetVar` + `Vars.ContainsKey` + `GetVar<int>` 照常 | `Vars` 改回直接返回 `dict` → `写被挡=False` 红 |
| CS39b | 反证半写：写失败后池子里确实没多出 `b` | 同上 mutation → `池中无 b=False` 红 |
| CS40 | `CSharpApiDoc.CommonKeywords` 含 `HXLDCont`、不含 `HXld` | 把 `"HXld"` 加回词表 → `含 HXld=True` 红 |

第一版 CS36b 写成"上游随后改成 42，旧端口的值不跟着变"，**变异跑下来仍是绿的**——
`InputPort<T>.Value` 读的是**手动值**，链接值走 `_cachedLinkedValue`，所以那条判据碰不到订阅本身。
按 R38 重写成 `CountingOutputPort` 数 `+=`/`-=`，才做成能被反证的判据。这一条的踩坑记在第六节。

## 五、变异反证实录（两轮）

第一轮（6 处 mutation 同时生效，`gate-FlowCanvasChecks-MUTPROBE.log`）：

```
[×失败] CS34 预览口径：…  →  窗口2=0；窗口1=0（该行 DisplayWindow=1）；总帧数=0；Success=True；Error=
[×失败] CS35 唯一口径：…  →  Img(=HImage)=True；Reg(=HRegion)=True；Reg.IsIconic=True；逐类型违例=HRegion、HXld、HObject
[×失败] CS36 改名重建：…  →  新端口=有；同对象=False；旧端口 LinkedSource=仍指着上游
[√通过] CS36b 反证订阅真的解了：…  →  重建后旧端口值=0；上游改成 42 后=0        ← 假绿，已重写
[×失败] CS37 摘掉的旧输出端口：…  →  旧端口值=仍持有；新端口=有；图像 IsInitialized=True
[×失败] CS38 别名留空：…  →  绑定时键=结果；改名后 EffectiveAlias=检测结果；Name=检测结果
[×失败] CS38b 确认时兜底固化：…  →  第1行 Alias=；第2行 Alias=「」
[×失败] CS39 Context.Vars 写不进…  →  写被挡=False；仍可读=True；Success=True；Error=
[×失败] CS39b 半写没发生：…  →  池中无 b=False；Error 条数=0；Error=
[√通过] CS40 补全关键字给 HXLDCont…  →  含 HXLDCont=True；含 HXld=False        ← 该轮未注入 mutation
```

该轮汇总 `通过: 3544  失败: 8`——**8 条红正好是 8 条被 mutation 打掉的新判据**，没有连带打红既有断言。

第二轮（只保留两处 mutation：`Dispose` 注释 + `"HXld"` 加回，用来验新写的 CS36b）：

```
[√通过] CS36a 前置：链接确实建立了…  →  订阅次数=1；活订阅=1；LinkedSource=已接
[×失败] CS36 改名重建：…  →  旧端口 LinkedSource=仍指着上游
[×失败] CS36b 反证订阅真的注销了：…  →  订阅 +=1；退订 -=0；仍挂在上游的订阅者=1
[×失败] CS40 补全关键字给 HXLDCont…  →  含 HXld=True；全表=…HXLDCont,HXld,HTuple…
```

该轮汇总 `通过: 3550  失败: 3`——3 条红**全部是本批新闸**，`[M]` 运动段那两条这一轮是绿的（印证 flaky 判定）。

两轮之后所有 `MUT-PROBE` 标记已逐处还原，全仓 `grep MUT-PROBE` 无残留：

```
Grep(path=D:\C#\VM, pattern=MUT-PROBE) → No files found
```

## 六、验证证据（2026-10-24）

| 项 | 命令 | 结果 |
| --- | --- | --- |
| 断言宿主构建（含插件传递重建） | `dotnet build FlowCanvasChecks/FlowCanvasChecks.csproj -m:1 -nodeReuse:false` | 243 警告（全是断言宿主既有 CS86xx 可空性告警）/ **0 错误**，exit 0 |
| 断言首跑（新闸落地） | `FlowCanvasChecks\bin\Debug\net9.0-windows\FlowCanvasChecks.exe` | **3550 通过 / 2 失败**（2 条为 `[M]` 运动段既有 flaky，原文见下）；CS34–CS40 全绿 |
| 变异反证第一轮 | 同一宿主（6 处 mutation） | 3544 通过 / 8 失败——红的就是被 mutation 的 8 条 |
| 变异反证第二轮 | 同一宿主（2 处 mutation） | 3550 通过 / 3 失败——红的就是 CS36/CS36b/CS40；`[M]` 两条这一轮绿 |
| Help 资源打包核对 | `tr '\0' '\n' < Modules/Plugin.CSharpScript.dll \| grep -a "Plugin\.CSharpScript\.Help\.[^ ]*\.md" \| sort -u` | **7 条资源名 = 磁盘 7 页**（R42） |
| 产物内容核对（不看退出码） | `grep -ac ShowsDisplayWindow Modules/Plugin.CSharpScript.dll` = 7；DLL 内嵌 Help 里能搜到"现在共 CS1" | 代码与 5 页手册**都**已进 DLL（R62：改过 .md 内容产物就算脏） |
| mtime 判据（收尾重建后） | `Modules/Plugin.CSharpScript.dll` **07:44:34**（182272 字节）vs 插件目录最晚源码 `Help/10-dev-architecture.md` 07:44:04 | DLL 晚于全部源码（红线 ③）；变异还原造成的那条 `STALE_DLL` 已消除，第三轮 `audit_artifacts` 出 `clean` |
| 全量 gate（唯一收尾入口，R182） | `powershell -ExecutionPolicy Bypass -File 'tools\run_gates.ps1'` | **三轮**，原始输出 `artifacts/logs/2026-10-24/run_gates-final{,2,3}-raw.txt`，逐宿主日志在 `artifacts/logs/20261024/`：<br>① 第一轮 **3/10 红**——`audit_artifacts` 判 `STALE_DLL Plugin.CSharpScript dll=07:26:56 src=07:29:28 newest=CSharpApiDoc.cs`（**真的是我的**：变异还原后没重建插件，见踩坑 9）；`FlowCanvasChecks` 3552/1（`[E16] 跳过留下了 Warn`，另一会话在途改动区的时序偶发）；`UIThemeSmokeTest` 判 `STALE EXE (source newer: hardcoded-colors.baseline.txt)`（清单文件被别的会话 07:27 改过，见踩坑 11）。<br>② 处置：`--no-incremental -p:BuildProjectReferences=false` 重建插件 + 同样只重链 `UIThemeSmokeTest.exe`（**不动他人源码**）。<br>③ 第二轮 **1/10 红**——只剩 `FlowCanvasChecks` 3551/2，红的是既有 `[M]` 运动段那两条 flaky（原文见下）；`audit_root`/`audit_artifacts`/其余 5 个宿主/`UIThemeSmokeTest`/`HelpProbe` 全绿。<br>④ 第三轮（本批最后一次，Help 页补完两处漂移并重建插件之后）：见下条 |
| 第三轮（收尾轮，Help 页补完两处漂移并重建插件之后） | 同上命令 | **`[run_gates] OK - 10 steps green`**：`audit_root` clean / `audit_artifacts` clean / CommChecks 336·0 / DataPipeline 97·0 / Database 134·0 / Scada 2685·0 / Recipe 138·0 / **FlowCanvasChecks 3553·0（`>>> 全部断言通过 <<<`）** / UIThemeSmokeTest exit 0 / HelpProbe exit 0（`=== HelpProbe: 全部通过 ===`）。第二轮那两条 `[M]` 运动段 flaky 本轮绿，佐证其偶发性质 |
| 产物内容复核（收尾轮跑完后） | `Modules/Plugin.CSharpScript.dll` = 07:44:34 / 182272 字节；DLL 内嵌 Help 资源名 **7 条 = 磁盘 7 页**；`grep -a -c` 命中 `只有 \`HImage\` 输出行出现这一项`=1、`同一轮同一名字只提醒一次`=2、`没有这道判断`=1、`ShowsDisplayWindow`=7、`CaptureAliasIfEmpty`=3 | 代码与手册**都**进得去（R42/R62 的判据是产物内容，不是构建退出码） |
| 真机 | 未启动 `VisionMaster.exe` | 「显示窗口」列的实际收合、区域行不再出现该下拉、预览上屏，仍需真机复验 |

`[M]` 运动段 flaky 原文（首跑，与本次改动无关，已在项目记忆里登记）：

```
[×失败] 前置：假设备命令失败后进入安全停机  →  状态=Online 说明=断言用假运动卡
[×失败] ★底层仍报警时清报警不放行：保持安全停机 + 给出原因  →  清报警=True 状态=Online 原因=
```

上一批 P1-2 记录里那条挡全量构建的无关在途改动（`UI/Controls/Themes/Dialog/Dialog.Input.xaml`
的 XML 注释含 `--`，MC3000）**本轮已不存在**（该会话已改成 `（基线 r-sm）`），
所以这次 `run_gates.ps1` 走的是正常全量路径，没有用 `-p:BuildProjectReferences=false`。

## 七、影响面与边界

| 文件 | 性质 | 行数 |
| --- | --- | --- |
| `Plugins/Plugin.CSharpScript/ScriptVarDef.cs` | 新增 `ShowsDisplayWindow`；`Type` setter 补发通知；`DisplayWindow` 文档收窄 | 131 |
| `Plugins/Plugin.CSharpScript/CSharpScriptPlugin.cs` | 推送判据换口径；输入端口 `Dispose`；输出端口断引用；别名固化两处 | 1048 |
| `Plugins/Plugin.CSharpScript/ScriptSharedVarDef.cs` | 新增 `CaptureAliasIfEmpty()` | 146 |
| `Plugins/Plugin.CSharpScript/ScriptContext.cs` | `Vars` 只读视图 + 按源实例缓存 + `EmptyVars` | 270 |
| `Plugins/Plugin.CSharpScript/CSharpApiDoc.cs` | `Vars` 说明；关键字 `HXLDCont` | 200 |
| `Plugins/Plugin.CSharpScript/CSharpHighlighting.cs` | 词表去掉 `HXld` | 174 |
| `Plugins/Plugin.CSharpScript/CSharpScriptView.xaml` | 「显示窗口」列绑 `ShowsDisplayWindow`；两处 ToolTip | 954 |
| `Plugins/Plugin.CSharpScript/CSharpScriptView.xaml.cs` | 选定变量即固化别名 | 356 |
| `Plugins/Plugin.CSharpScript/Help/{02,03,10,11,12}` | 文案与不变量台账 | 5 页 |
| `FlowCanvasChecks/CSharpScriptChecks.cs` | CS34–CS40（11 条）+ 两个辅助件 | 435 → 692（+257） |

影响面口径（AGENTS.md R28/R29）：**本会话无 CodeGraph 工具，以上是 grep/Read 结论，不是索引结论。**

边界（有意留下的）：

- **区域/轮廓要上屏仍得自己合成**，本批不给它们加通道（用户拍板）。脚本里的写法：
  `HOperatorSet.PaintRegion(region, image, out HObject painted, 255.0, "fill")` → 写进 `HImage` 输出行。
- **旧方案兼容**：老 `.vms` 里若存着"区域行 `DisplayWindow=1`"这种配置，属性还在（`DisplayWindow`
  没删），只是界面不再显示那一列、运行也不再试图推送——**不会报错**，也不会自动改用户的值。
  要清掉就把类型改回 `HImage` 或手工删掉该行的窗口选择。
- **`IsIconic` 语义不变**：它仍表示"图形类输入必须连线、不能手填文本"。
  把"能否推预览"重新绑回 `IsIconic` 会让区域/轮廓行又长出那个下拉（CS35 就是拦这个回退的）。
- **`Vars` 只读是行为变更**：以前脚本可以 `Context.Vars["x"] = 1` 绕过 `SetVar` 写池子。
  现网脚本若有这种写法，运行会抛 `NotSupportedException` → 节点失败（响的），
  改法就是换成 `Context.SetVar("x", 1)`。签名保持 `IDictionary<string, object>`，
  所以只读枚举/索引器读/`ContainsKey` 都不受影响。
- **别名固化的取舍**：留空 = 固化成**当时**的名字。以后用户在变量管理里改了名，
  又希望脚本跟着改（"我要的就是当前名"），需要把别名手工清空——但下一次确认会再固化一次。
  真实需求是"脚本键稳定"，所以选了固化；手册 02/03 页写明"留空 = 确认时固化"。
- **输出端口没做 Dispose**：`OutputPort<T>` 根本没实现 `IDisposable`，基类也没有"摘键即回收"的钩子；
  真要收，得改 `Shard/Core.Interfaces` 的契约——那是公共契约面，走 R12 的架构评审，不在本批。
- **高亮词表没有断言**：`CSharpHighlighting.KnownTypes` 是 private，
  从外部只能拿到 `IHighlightingDefinition`（`FlowCanvasChecks` 没引 AvalonEdit 引用），
  手册 12e 条已注明"改它靠人工核对"，不含糊成已覆盖（R13）。
- **标题面"可接未接"**：用户拍的是"只承诺 HImage"（类型面收窄），标题不在这一句里。复核确认
  `PluginPreviewExtensions.cs:48,60` 有带 `title` + `ImageInfoRow` 的重载，把输出行的 `Remark` 递进去
  是插件侧一行改动；本批**没做**（做了就等于扩大承诺面，且 `PublishIconicOutputs` 得决定"备注当标题"这条
  隐含规则是否用户想要）。手册先把"Remark 是推图标题来源"这句假承诺删了，实现与文档此刻一致。
- **未做**：批次 B 的白名单定位一项、批次 C 其余项（`CallCache` 逐条淘汰、
  正则改语法树校验共享变量用法、预览帧的留存/裁剪策略）——本批只碰用户点名的三项。

## 八、踩坑

1. **"承诺比实现宽"是静默故障的温床**。区域行选得出窗口却不推，日志一行都没有。
   修法不是"补 Warn"，而是**让界面配不出这个组合**——判据同源（`ShowsDisplayWindow`）之后，
   这个分支在配置期就不存在。
2. **`Dispose()` 才是退订的唯一通道**。`RemoveDynamicInput` 听名字像"清理"，
   实际只 `_inputs.Remove(key)`；而退订藏在 `InputPort<T>.Dispose()` 里（它复用
   `LinkedSource = null` 的 setter 去 `-=`）。只看基类公开面会以为已经清理过了。
3. **`InputPort<T>.Value` 不是"生效值"**。它读的是手动值，链接值在 `_cachedLinkedValue` 里。
   第一版 CS36b 拿它做判据 → mutation 跑了仍是绿（假绿）。判"订阅还在不在"，
   得自己造一个能数 `+=`/`-=` 的假上游端口。
4. **输出侧不能套同一招**。`OutputPort<T>` 无 `IDisposable`，而且值可能是上游的图。
   越权 `Dispose` 的代价是"下游拿不到图"这种要现场才能发现的坏法，
   所以只做"断引用"，并在注释里把为什么不 Dispose 写死。
5. **别名固化的时机必须选在"键还权威"的那一刻**。在覆盖 `Name` **之后**补，
   等于让脚本键跟着宿主改名漂——正是这条 bug 的形状。所以 `RefreshSharedVarDefs` 里
   两次 `CaptureAliasIfEmpty()`（前一次保旧名、后一次兜首次绑定），顺序是判据不是风格。
6. **只读化要保住签名**。把 `Vars` 的返回类型改成 `ReadOnlyDictionary<string, object>`
   会让 `IDictionary<string, object> v = Context.Vars;` 这种老脚本编译失败。
   返回类型不动、只换实例，才是"窄开写口、不破坏读口"。
7. **枚举成员名 ≠ CLR 类型名**。`HXld` 是 `.vms` 里的序列化值（改 = 打断已配流程，R40），
   脚本里它是类型名（写了必 CS0246）。两套口径分别落在两处代码，手册 11 页第三节写清，
   否则下一个改的人只会"顺手统一"成其中一个。
8. **`**` 与 `--` 都能把构建或渲染弄崩**：`--` 在 XML 注释里非法（MC3000，上一批就是它挡了全量构建），
   `**` 落单会让手册渲染残留字面星号（R63）。本批改 Help 时逐条配对检查。
9. **还原源码 ≠ 还原产物**。变异反证跑完把 6 处 mutation 改回原样，插件 DLL 却还停在 mutation 那一次构建
   （`dll=07:26:56` vs `src=07:29:28`，newest=`CSharpApiDoc.cs`）。全量 gate 的 `audit_artifacts` 第一件事就把它判红
   ——**这是闸在正常工作**，别手工"顺手忽略"。教训：变异轮的最后一动作必须是"重建插件"，不是"改回源码"。
10. **同一页手册会对同一行为写两次**。批次 A 把 `SetOutput` 缺端口改成 Warn，`Help/02:34` 改对了、
    同页 `:97` 的"忽略，不报错"没动，于是**一页自相矛盾**；这轮又顺手抓到 `Remark` 的"推图标题来源"
    （P2-3 的另一半，上一批只按"只承诺 HImage"改了类型面）。改口径的复验动作应当是**整页 grep 关键词**
    （`标题`/`不报错`/`静默`/`图形类`），不是"改我这次记得的那一条"。
11. **闸自己的判据也会错，但不在本轮修**：`run_gates.ps1:243` 用 `-Include *.cs,*.xaml,*.csproj` 找"最新源码"，
    实测把内容清单 `UIThemeSmokeTest/hardcoded-colors.baseline.txt` 也算进去了，于是任何人改一次清单、
    该宿主就永久判 `STALE EXE`，而 gate 自带的增量 build 不重链 exe、自愈不了。本轮只做最小解（单独
    `--no-incremental -p:BuildProjectReferences=false` 重链那个宿主），**没改工具**——工具属公共面，改它要走 R12。

## 九、复现与后续

- 复现本批断言：`dotnet build FlowCanvasChecks/FlowCanvasChecks.csproj -m:1 -nodeReuse:false`
  后直接跑 `FlowCanvasChecks\bin\Debug\net9.0-windows\FlowCanvasChecks.exe`，
  看 `[CS]` 段（标题已含"预览口径与端口释放"）末尾 11 行。
- 待真机复验（不能由断言宿主证明的部分）：
  ① 输出行选 `HRegion` 时「显示窗口」那一块确实收起、不占位；
  ② `HImage` 行的预览仍进对格（R36 的 viewIndex 口径没动）；
  ③ 逐字符改名时不再有"旧端口 + 图"累积（任务管理器句柄数长时观察）；
  ④ 变量管理改名后，共享变量行的别名框显示的是固化下来的旧名（不是新名）。
- 下一批（需用户点头）：批次 B 白名单定位一项；批次 C 的 `CallCache` 逐条淘汰与
  共享变量用法校验改走语法树；**以及一个一问一答的小项**——输出行 `Remark` 是否真的当预览标题上屏
  （宿主重载已支持，只差插件那一行；要先确认用户要的是"备注=标题"还是"另开一列标题"）。
