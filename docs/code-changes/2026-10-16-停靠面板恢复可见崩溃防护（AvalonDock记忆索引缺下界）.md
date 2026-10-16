# 停靠面板恢复可见的崩溃防护（AvalonDock 4.74.1 的 Show() 缺索引下界）

- **日期**：2026-10-16
- **触发**：真机调试器直接断在 `System.ArgumentOutOfRangeException`，栈落在
  `VisionMaster.Services.LayoutHelper.ShowPanel`（用户原话"这是什么报警"→ 裁决"直接开始写防护代码"）
- **改动范围**：`VisionMaster\Services\LayoutHelper.cs`（防护本体）、
  `UIThemeSmokeTest\Program.cs`（运行时 10 条，含 2 条库行为反证）、
  `FlowCanvasChecks\LayoutVisibilityCheck.cs`（新增，静态 10 条）、
  `tools\_dump_prev_mem.ps1`（取证脚本，可复算）
- **未改动**：AvalonDock 包版本（仍 4.74.1）、`Shell.xaml` 停靠子树、`CurrentLayoutSchemaVersion`（仍 3）

---

## 1. 一句话结论

AvalonDock **4.74.1** 的 `LayoutAnchorable.Show()` 在把面板插回"上次隐藏前那一列"时，
**只校验索引的上界、不校验下界**——记忆里存的是 `-1`（那一列已经不包含本面板）时，`-1` 被原样塞进
`Collection<T>.Insert(index, item)`，抛 `ArgumentOutOfRangeException`。
更糟的是"清空记忆"那两行赋值排在插入语句**之后**，异常一抛坏记忆永远留着，
**这个面板此后每 Show 一次就炸一次**；而本仓的模式收敛（R56）让"切中央文档区标签"就重收敛一次，
于是把一次性故障放大成"天天闪退"。`App` 里没有 `DispatcherUnhandledException` 兜底（本轮 grep 复核：全仓 0 命中），
所以必退进程。

修法是**在本仓加一层防护**（用户裁决的方案①）：可见性恢复一律走
`LayoutHelper.ShowAnchorable`，先用反射读库自己的两条记忆判一次"这次 Show 落不落得回去"，
判废（或原路抛了 / 抛不了但也没显示出来）就**我们自己把它挂回一个仍挂在布局树上的列**。
不改包版本、不改面板归属，因此**不需要**递增 `CurrentLayoutSchemaVersion`（R43 的触发条件是"面板归属/停靠语义变了"）。

---

## 2. 现场：异常原文与栈的逐帧解读

用户贴进来的原文（调试器第一手，未加工）：

```
System.ArgumentOutOfRangeException
  HResult=0x80131502
  Message=Index was out of range. Must be non-negative and less than or equal to the size of the collection. (Parameter 'index')
  Source=System.Private.CoreLib
  StackTrace:
   at System.ThrowHelper.ThrowArgumentOutOfRange_MustBeNonNegLessThanCount(ExceptionResource resource)
   at System.Collections.Generic.List`1.Insert(Int32 index, T item)
   at AvalonDock.Layout.LayoutGroup`1.InsertChildAt(Int32 index, TContent child)
   at AvalonDock.Layout.LayoutAnchorable.Show()
   at AvalonDock.Layout.LayoutAnchorable.set_IsVisible(Boolean value)
   at VisionMaster.Services.LayoutHelper.ShowPanel(String contentId) in ...\VisionMaster\Services\LayoutHelper.cs:line 468
   at VisionMaster.Services.LayoutHelper.ApplyScadaPanelMode(DockingManager manager, Boolean scadaDesign) in ...\LayoutHelper.cs:line 429
   at VisionMaster.Services.LayoutHelper.ApplyScadaPanelMode(Boolean scadaDesign) in ...\LayoutHelper.cs:line 420
   at VisionMaster.ViewModels.ShellViewModel.ApplyPanelMode() in ...\VisionMaster\ShellViewModel.cs:line 322
   at VisionMaster.ViewModels.ShellViewModel.<AttachDockWatchers>b__..(Object , PropertyChangedEventArgs ) in ...\ShellViewModel.cs:line 302
   at System.ComponentModel.PropertyChangedEventHandler.Invoke(Object sender, PropertyChangedEventArgs e)
   at AvalonDock.Layout.LayoutContent.set_IsSelected(Boolean value)
```

读出来的四件事：

| 观察 | 结论 |
| --- | --- |
| `List<T>.Insert` 的 `Message` 明确是 **"Must be non-negative"** | 传进去的 index 是**负数**（不是"超过数量"那一支——超过数量那条文案是 `Index and count must refer to a location within the collection`）。库的上界分支挡住了越界，下界**没人管**。 |
| 栈顶进的是 `LayoutAnchorable.Show()` 而不是 `Hide()` | 出事的是**恢复显示**：`Hide()` 只是记下记忆，`Show()` 才消费它。 |
| 入口是 `set_IsSelected` → `PropertyChanged` → `AttachDockWatchers` 的监听器 | 正是 R56 那条"中央文档区切标签就重收敛"的链路：用户**只是在文档区点了另一个标签页**。 |
| `ApplyScadaPanelMode(bool)` → `(manager, bool)` → `ShowPanel(string)` 三层都在栈里 | 收敛动作里那一句 `ShowPanel(组态三面板之一)`；本次崩的那个面板就是它记忆已脏的那个。 |

**行号偏移的说明（避免读者按行号找不到代码）**：报错里 `ShowPanel :468 / ApplyScadaPanelMode :429 / :420`
与崩溃时源码对得上，但与**本轮改前**的工作副本差 **+5 行**（`LayoutHelper.cs` 磁盘时间 00:23 vs Debug 产物 00:44）。
也就是说崩溃的那个进程跑的是**上一轮的构建**，同族于红线③"产物陈旧"与 R42/R55——
本文所有行号一律以**当前工作副本**为准（见 §6 台账）。

---

## 3. 根因机制：库消费记忆的那一段

`Hide()` 把"回哪儿"记成两条：`PreviousContainer`（那一列）+ `PreviousContainerIndex`（那一列里的第几个位置）。
`Show()` 的成功路径等价于：

```csharp
// 4.74.1 的形状（按异常行为反推 + 本仓实测复现，见 §7 的 ⑥a）
var container = PreviousContainer;
if (container != null)
    container.InsertChildAt(PreviousContainerIndex, this);   // ← 只有"越界改成追加"的上界处理
PreviousContainer = null;                                    // ← 清记忆排在插入之后
PreviousContainerIndex = -1;
```

三条后果，本轮都实测或静态复核过：

1. **索引 -1 直接崩**。`PreviousContainerIndex` 的来源是 `IndexOfChild(this)` 一类的查找，
   记忆里那一列已经不含本面板时它就是 `-1`；`Insert(-1, …)` 抛 `ArgumentOutOfRangeException`。
2. **坏记忆是粘的**。异常发生在"清空记忆"之前 → 记忆保持脏，此后每次 `Show()` 都在同一行抛。
   本仓的收敛每次切标签都会 Show 一遍那几个面板（R56），于是**每次切标签都闪退**。
3. **另一头是静默故障**。当 `PreviousContainer` 取不到（为 null）时，库**不抛、也不显示**——
   本轮 ⑦a 用真 `DockingManager` 量到这一态下 `IsVisible` 仍为 `false`。
   这条决定了防护**不许只 `try/catch`**，必须自己把它挂回去。

### 3.1 记忆"怎么会变脏"（现场触发路径：未定位，列为未决）

防护不依赖这条，但记录里必须诚实：本轮**没有**复现出"用户怎么操作把索引搞成 -1"。
能想到的两条来源（都属于"记忆跨过了布局树的形态变化"）：
① 从 `Layout.xml` / `.vms` 内嵌布局反序列化——`PreviousContainerId` 指向的列被恢复成另一棵树上的另一实例；
② 一个列被掏空后又被复用（本仓收起整列时留空列，见 R56 的"先 Show 后 Hide"注释）。
两条都只需要"记忆里的列不再含本面板"这一个事实成立。**结论：不追这条路径也能修好**——
判据放在"Show 之前先看记忆落不落得回去"，任何来源的脏记忆都在同一道闸前被拦。

### 3.2 为什么不靠"升级 AvalonDock"

上游后续版本在同一个判断里补了下界（`PreviousContainerIndex >= 0 && …` 这一类形状）。
本轮**没有**逐字复核上游源码（取证时网络不通），所以这条不作为判据写进纪律，只作为**可选的长期出路**记下：
升级包会顺带带来停靠区主题/序列化格式的差异，代价远大于在本仓加一层。
用户裁决也是"直接开始写防护代码"（方案①），因此：**不动包版本**。
若日后真升级到已修的版本，本仓的防护仍然正确（记忆有效时走原路），而 §7 的两条反证断言会**主动变红**
（"库这一版免疫？防护前提要重审"），提醒重审而不是静默留着。

---

## 4. 为什么这条必须写进 AGENTS.md（失败模式是"退进程"）

| 事实 | 复核方式 | 结果 |
| --- | --- | --- |
| `VisionMaster` 没有全局 `DispatcherUnhandledException` 兜底 | 全仓 grep | **0 命中** → 任何 UI 线程未捕获异常 = 进程退出 |
| 崩溃动作是"切标签"级别的高频动作 | `ShellViewModel.AttachDockWatchers` 的监听器（R56） | 每次切中央文档区标签都重收敛一次 |
| 面板显隐的写法在产线有**两处**（`ShowPanel` + `EnsureCriticalPanelsVisible`） | 本轮 `grep -rn "IsVisible\s*=\s*true"` 第一方目录 | 改前是 2 处，改后收敛为 1 处（都在 `ShowAnchorable` 内） |

也就是说：**症状不是"某个面板打不开"，而是"用户点一下标签页，软件没了"**，
且一旦发生就永久发生（坏记忆粘住）。这种形状必须由静态扫描 + 运行时反证**双层**守住。

---

## 5. 三条候选方案与取舍（用户选了①）

| 方案 | 内容 | 取舍 |
| --- | --- | --- |
| ① **本仓加防护**（采纳） | 可见性恢复收口到 `LayoutHelper.ShowAnchorable`，脏记忆自己挂回 | 不动依赖、不动布局、不改结构版本；代价是多一层反射读取（只读，不写库状态） |
| ② 升级 AvalonDock | 换到已修下界的版本 | 一次性解决，但牵动主题/序列化/浮动窗行为，回归面远超这条崩溃本身 |
| ③ 重排面板归属 | 让组态面板不与工具箱同 pane，靠 pane 生命周期避开脏记忆 | 要动 `Shell.xaml` 停靠子树 = 必须升 `CurrentLayoutSchemaVersion`（R43），**老用户自拖布局会静默作废**；为一条库缺陷付这个代价不值 |

---

## 6. 落地实现（`VisionMaster\Services\LayoutHelper.cs`，行号=当前工作副本）

### 6.1 入口收口

```
:450  public static void ShowPanel(string contentId) => ShowPanel(FindManager(), contentId);
:453  public static void ShowPanel(DockingManager? manager, string contentId)
:464      ShowAnchorable(manager, anchorable);      // ← 改前是 anchorable.IsVisible = true
:623      ShowAnchorable(manager, anchorable);      // ← EnsureCriticalPanelsVisible 里的同一句（强制恢复关键面板）
```

`EnsureCriticalPanelsVisible`（`:614`）另外做了两处配套修正：

- **先快照再遍历**：`EnumerateContents(manager).ToList()`。恢复可见会动布局树（挂回原列 / 新建列），
  拿惰性迭代器边遍历边改就是自毁（改前恰好没炸是因为改前根本不会走到挂回分支）。
- **结果上报**：日志从"强制恢复可见"改成"强制恢复可见（成功 / 失败：仍未显示）"。
  静默失败是本族故障的常态，日志必须能分辨。

### 6.2 防护本体 `ShowAnchorable`（`:485`）

```csharp
private static void ShowAnchorable(DockingManager manager, LayoutAnchorable anchorable)
{
    if (anchorable.IsVisible) return;
    // 既不可见也不隐藏 = Parent 为 null（已从布局上摘干净）：Show() 这一态直接抛 InvalidOperationException
    if (!anchorable.IsHidden) return;

    var remembered = ReadPreviousContainer(anchorable);
    var rememberedIndex = ReadPreviousContainerIndex(anchorable);

    bool memoryUnusable = rememberedIndex < 0
        || (remembered != null && !IsAttachedTo(remembered, manager.Layout));

    if (!memoryUnusable)
    {
        try
        {
            anchorable.IsVisible = true;
            if (anchorable.IsVisible) return; // 真显示出来了才算成功（记忆为空时库会静默跳过插入）
        }
        catch (Exception ex)
        {
            Console.WriteLine($"面板 {anchorable.ContentId} 按记忆恢复失败，改用显式挂回：{ex.Message}");
        }
    }

    AnchorToHomePane(manager, anchorable, remembered);
}
```

四个判据各自的理由（少一条都对应一个真实故障形状）：

| 判据 | 为什么必须写 |
| --- | --- |
| `if (!anchorable.IsHidden) return;` | `IsVisible==false && IsHidden==false` 意味着 `Parent == null`，这一态库的 `Show()` **无条件抛** `InvalidOperationException`（不是本条崩溃，是另一条）。挡在前面比 catch 干净。 |
| `rememberedIndex < 0` | 就是本次事故的那一支（`Insert(-1)`）。 |
| `remembered != null && !IsAttachedTo(...)` | 索引合法但那**一列已经不在当前布局树上**（反序列化换树、或列被摘掉）——插进去等于插进一棵死树，面板照样显示不出来（静默故障）。 |
| `if (anchorable.IsVisible) return;`（Show 之后再量一次） | 库在 `PreviousContainer == null` 这一态**不抛也不插**（§7 的 ⑦a 实测）。只看"没抛异常"会把静默故障放过去。 |
| `rememberedIndex` 是 `int?`，取不到算"不知道" | 反射口径哪天随库版本失效 → 退回"让库自己试，抛了照样走修复"，而不是拿 null 当 0 判成"合法"。 |

### 6.3 读记忆（只读反射，绝不写库状态）

| 成员 | 出货程序集里的真实形状 | 读取写法 |
| --- | --- | --- |
| `PreviousContainer` | `LayoutContent` 上的 **protected 属性**（get/set 都是 `Family`） | `ReadPreviousContainer`（`:522`）沿基类链 `GetProperty(NonPublic \| Public \| Instance)` |
| `PreviousContainerIndex` | `LayoutContent` 上的 **public 属性** | `ReadPreviousContainerIndex`（`:539`），返回 `int?` |
| `ILayoutPreviousContainer` | **internal 接口**（`IsPublic=False / IsNotPublic=True`） | **不能用**——外部程序集强转过去就是 `CS0122`（本轮踩过，见 §8） |

两个读取函数**只 `GetValue`、不 `SetValue`**：本仓不在库对象上留任何私有状态，
修复之后再 Hide 一次，库会自己把有效索引记回去（自愈）。

`IsAttachedTo`（`:551`）：`ReferenceEquals((container as ILayoutElement)?.Root, root)`——
容器还挂在这棵 `LayoutRoot` 上才算"落得回去"。

### 6.4 挂回 `AnchorToHomePane`（`:565`）：落点四档

```
① 记忆里那一列，且它现在是空的      ← 被 Hide 掏空的原列，位置最准
② 树上第一个空的 LayoutAnchorablePane ← 本仓设计里收起的面板都会留下空列（Hide 不摘 pane 本身）
③ 记忆里那一列（非空也认）          ← 宁可当标签页，至少还在同一侧
④ 根面板末尾新建一列（DockWidth=260）← 兜底：宁可宽度不对、位置偏右，也不能让面板显示不出来
```

三条实现口径：

- **进门先判 `if (anchorable.IsVisible) return;`**——库可能在抛异常**之前**已经把它插进去了
  （异常出在插入之后的语句），再插一次就是同一个 pane 两个副本。
- 统一走 `home.InsertChildAt(home.ChildrenCount, anchorable)`：与库自己成功路径**同一个动作**，
  `Parent` 由集合变更回调赋值，`Hidden` 的收尾照库自己的路子走；不自己 `Children.Add`（绕过回调）。
- 挂回后补 `IsSelected = true; IsActive = true;`，让 `AttachDockWatchers` 与活动状态照常刷新。

---

## 7. 运行时断言台账（`UIThemeSmokeTest\Program.cs` 的 `RunPanelModeChecks()`，`:1384`）

宿主对着 `ShellCopy.xaml` 实例化出来的**真 `DockingManager`**（STA 线程、无 WPF `Application`）跑。
改前 5 条（①–⑤），改后 10 条（+⑥a/⑥b/⑦a/⑦b/⑧）。本轮实测全部 `[PASS]`，逐字输出：

```
[PASS] 组态设计模式：属性/图层/图元工具箱 都显示，算子工具箱 收起  组态三面板=[可见,可见,可见]，工具箱=隐藏
[PASS] 非组态模式：只留算子工具箱，属性/图层/图元工具箱 全部收起（两列随之收成 0、画布变宽）  组态三面板=[隐藏,隐藏,隐藏]，工具箱=可见
[PASS] 来回切换多次后两个模式仍各自收敛到同一条基线（幂等、无累积错）  切回组态=True，切回非组态=True
[PASS] 「组态画面」是 LayoutDocument（判据走 IsSelected；LayoutAnchorable 没有这个语义）  实际类型=LayoutDocument
[PASS] 图层面板登记在 ContentFactories 里（缺它 = 每次恢复布局后图层面板变空壳，且覆盖度校验不会拦）  登记项 11 个
[PASS] 脏索引下直接写 IsVisible=true 当场抛 ArgumentOutOfRangeException（真机那条栈的复现，证明防护不是空转）  ArgumentOutOfRangeException
[PASS] 记忆索引 = -1 时 ShowPanel 不抛异常、图层面板照样显示回来（库的 Show() 缺下界判据，真机闪退那一条）  未抛、已显示
[PASS] 记忆为空时库自己 Show() 是静默不显示（所以防护不许只 catch 不挂回）  确认静默不显示
[PASS] 整条记忆被抹平时 ShowPanel 不抛异常、图层面板仍显示回来（旧行为是静默显示不出来）  未抛、已显示
[PASS] 两轮脏记忆修复后，两种模式的收敛仍各自回到基线（修复没留下坏状态）  切组态=True，切非组态=True，组态三面板=[隐藏,隐藏,隐藏]，工具箱=可见
=== Panel 模式：全部通过 ===
```

### 7.1 两条反证（R38：绿灯必须有可反证的前提）

| 断言 | 它证明的是 | 它挡住的是 |
| --- | --- | --- |
| **⑥a** 把 `PreviousContainerIndex` 写成 `-1`，然后**绕过防护**直接 `dirtyPanel.IsVisible = true` | 出货二进制里**库自己就会抛** `ArgumentOutOfRangeException`（真机那条栈的复现） | 如果哪天这条红了，说明"脏索引会崩"这个前提不再成立 → ⑥b 的防护就是空转，必须重审/摘掉，而不是继续挂绿灯 |
| **⑦a** 把 `PreviousContainer` 抹成 `null`、索引 `-1`，再绕过防护 `IsVisible = true` | 这一态库**不抛、也不显示**（`silent7 = !IsVisible` 为真） | 挡住"防护只写 try/catch 就完事"的错误实现：只 catch 的结果是面板永远显示不出来（静默故障），所以必须真的挂回去 |

⑥b/⑦b 是防护本体的正证（不许抛 + **必须**显示回来，两个条件写在同一条 `Check` 里）；
⑧ 是回归反证（两轮修复后 Hide 会重新记下有效索引，两种模式的收敛都回基线 = 修复没留下坏状态）。

### 7.2 造脏的口径必须与产线逐字一致（本轮踩了一次假红）

断言用反射读写记忆，写法必须对齐 `LayoutHelper.ReadPreviousContainer`：

```csharp
var prevContainerProp = typeof(AvalonDock.Layout.LayoutContent)
    .GetProperty("PreviousContainer", BindingFlags.NonPublic | BindingFlags.Instance);   // protected 属性
var prevIndexProp = typeof(AvalonDock.Layout.LayoutContent)
    .GetProperty("PreviousContainerIndex", BindingFlags.Public | BindingFlags.Instance);  // public 属性
```

两个坑（都已写成反向断言，见 §9 的 LV6）：

1. 第一版写成 `GetField("PreviousContainer")` / `GetField("PreviousContainerIndex")` → 取到 `null`，
   断言直接**假红**（`字段=(缺)/(缺)`）。真相是字段名叫 `_previousContainer` / `_previousContainerIndex`。
   产线同款写法更阴：**不会红**，只会让"记忆判废"整条分支静默失效。
2. 第二版写成接口强转 `dirtyPanel as AvalonDock.Layout.ILayoutPreviousContainer` → `error CS0122`
   "不可访问，因为它具有一定的保护级别"（两处：强转 + `previousContainer.PreviousContainer = null`）。
   该接口是 **internal**，外部程序集转不过去；上一节那条"VisionMaster 里同样的强转能编过"是**误记**，
   产线代码从来没有强转过，只有反射读——本轮用 §8 的取证脚本把这三条形状钉成了事实。

取不到属性时断言**故意判红而不是跳过**：反射口径失效 = 库换版本 = "防护该重审/该摘"的时刻，
静默放行等于给未来回归留门。

---

## 8. 取证：出货二进制里的真实形状（可复算）

脚本：`tools\_dump_prev_mem.ps1`（纯 ASCII，符合 R1；中文只在控制台输出，不经命令行传参）。
复算命令：

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File tools/_dump_prev_mem.ps1
```

原样输出（2026-10-16）：

```
asm=AvalonDock, Version=4.74.1.0, Culture=neutral, PublicKeyToken=3e4669d2f30244f4
iface public=False nested=False notpublic=True
prop PreviousContainer | get get_PreviousContainer:PrivateScope, Family, HideBySig, SpecialName | set set_PreviousContainer:PrivateScope, Family, HideBySig, SpecialName
prop PreviousContainerId | get get_PreviousContainerId:PrivateScope, Family, HideBySig, SpecialName | set set_PreviousContainerId:PrivateScope, Family, HideBySig, SpecialName
prop PreviousContainerIndex | get get_PreviousContainerIndex:PrivateScope, Public, HideBySig, SpecialName | set set_PreviousContainerIndex:PrivateScope, Public, HideBySig, SpecialName
field _previousContainer Private, NotSerialized
field <AvalonDock.Layout.ILayoutPreviousContainer.PreviousContainerId>k__BackingField Private
field _previousContainerIndex Private, NotSerialized
```

读法：`Family` = protected（属性本身可用、但外部拿不到接口）、`iface public=False` = internal 接口、
最后三行 = 字段名与属性名**不同名**（所以 `GetField("PreviousContainer")` 必取不到）。

---

## 9. 静态扫描台账（新增 `FlowCanvasChecks\LayoutVisibilityCheck.cs`，10 条，进常规 gate 基线）

注册点：`FlowCanvasChecks\Program.cs`，`HelpManualCheck.Run();` 之后。
扫描范围只取第一方目录（`Plugins / Services / Shard / Engine / Core / VisionMaster`）并排除 `bin\obj`，
与 `ResultDisplayCheck` 扫描④同一套口径（`DLL\HslCommunication`、`VM3.0-main` 是随仓第三方源码，R29 同族噪音）。

```
[√通过] LV0 定位到仓库根  →  D:\C#\VM
[√通过] LV1 前置：扫到第一方 .cs 源文件  →  530 个文件
[√通过] LV2 前置：VisionMaster\Services\LayoutHelper.cs 存在
[√通过] LV3【全仓】引用 AvalonDock 的文件里，除 LayoutHelper.cs 之外不许有 `.IsVisible = true`  →  (无违规)
[√通过] LV3b 反向钉住前提：LayoutHelper 里那一处必须还在（0 处 = 库已免疫、防护该重审）  →  1 处（期望 1）
[√通过] LV4 唯一允许的那处赋值落在 ShowAnchorable 方法体内（花括号配对取方法体，不写行号）  →  在体内
[√通过] LV5 防护三要素齐备：索引下界判废 + 容器仍在树上 + 判废后显式挂回  →  下界=True，挂载判据=True，挂回=True
[√通过] LV6 读库记忆用 GetProperty，不许 GetField("PreviousContainer…")  →  (无 GetField)
[√通过] LV6b 两条记忆都有读取路径  →  两条都在
[√通过] LV7 ApplyScadaPanelMode 只做 Show/Hide 编排，不许自己碰 IsVisible  →  走 Show/Hide=True，自写可见性=False
```

三条判据口径值得单独说：

- **LV3 的接收者范围收在"代码里去注释后仍含 `AvalonDock` 的文件"**。`IsVisible` 这个名字好几套控件都有
  （Nodify 的 `PendingConnection`、`_ChartProbe*` 的图表轴、组态图层），全仓一刀切会误伤。
  实测复核：`VisionMaster\ViewModels\FlowCanvasViewModel.cs:1773` 的 `PendingConnection.IsVisible = true`
  是该文件里唯一一处命中，而它的 `AvalonDock` 字样只出现在**注释**里（`:223`），去注释后自然出局——
  不需要为它开豁免清单（R36 那条"豁免只许变短"的教训：能不收就不收）。
- **LV3b 是反向断言**：`LayoutHelper` 里那一处必须恰好 1 处。0 处不是"更干净"，而是"前提没了"，
  跟 LV4/⑥a 一样是给未来重审留的门铃。
- **判据一律按"签名 + 花括号配对取方法体"**（`MethodBody()`），不写行号与空白：改缩进加注释不误伤（R46/R57 同思路）。

### 9.1 变异反证（LV3 能不能红）

| 步骤 | 动作 | 结果 |
| --- | --- | --- |
| 基线 | 还原态跑 gate | `通过: 2455  失败: 0` |
| 注入 | 在 `VisionMaster\ShellViewModel.cs` 的 `ApplyPanelMode` 后面加一段**从不被调用**的探针：<br>`private void MutProbe() { if (FindPanelForProbe() is AvalonDock.Layout.LayoutAnchorable mut) mut.IsVisible = true; }` | — |
| 变异跑 | 重建 + 跑 gate | `通过: 2454  失败: 1`，红灯逐字：`[×失败] LV3 …  →  VisionMaster\ShellViewModel.cs:329`（文件:行号定位准确） |
| 还原 | 删掉探针，重建 + 跑 | `通过: 2455  失败: 0` |

对照：注入点选在 `ShellViewModel.cs`（本来就重度引用 AvalonDock）而不是 `Shell.xaml.cs`，
是为了证明"命中靠的是接收者所在文件真引用 AvalonDock"，而不是"探针自己带进去那行 using"。

---

## 10. 数字台账（可复算）

| 项 | 改前 | 改后 | 复算方式 |
| --- | --- | --- | --- |
| `LayoutHelper.cs` 行数 | 526（`git show HEAD` 那份） | 736 | `git show HEAD:VisionMaster/Services/LayoutHelper.cs \| wc -l`；`wc -l VisionMaster/Services/LayoutHelper.cs`（差量里含并发会话的 `ApplyScadaPanelMode` / 指定宿主重载，见 §13） |
| 全仓"写 `.IsVisible = true`"的位置（第一方目录） | 2（`ShowPanel` + `EnsureCriticalPanelsVisible`） | 1（都在 `ShowAnchorable` 内） | `git show HEAD:…/LayoutHelper.cs \| grep -c "IsVisible = true"` = 2；`grep -rn "IsVisible\s*=\s*true" --include=*.cs VisionMaster Services Shard Engine Core Plugins` |
| `RunPanelModeChecks` 条数 | 5 | 10 | 数 `[PASS]` 行（§7） |
| `LayoutVisibilityCheck` 条数 | —（文件不存在） | 10 | `wc -l` = 174 行 |
| FlowCanvasChecks 常规 gate | 2445 / 0 | 2455 / 0 | 跑 exe，看"结果汇总" |
| UIThemeSmokeTest Panel 段 | 5 条绿 | 10 条绿（含 2 条反证） | `UIThemeSmokeTest.exe` 日志 |
| AvalonDock 包版本 | 4.74.1 | **4.74.1（未动）** | `ls ~/.nuget/packages/dirkster.avalondock` |
| `CurrentLayoutSchemaVersion` | 3 | **3（未动）** | `LayoutHelper.cs:41` |

构建：`dotnet build UIThemeSmokeTest/UIThemeSmokeTest.csproj` 与 `dotnet build FlowCanvasChecks/FlowCanvasChecks.csproj`
均 **0 错误**（后者把 `VisionMaster` 一起带编）。
⚠ 冒烟 exe 的**退出码恒为 1**（GridView 扫描命中 vendored 目录的既有红灯，R46 已记），
新闸成败一律以日志行判定，不看退出码。

---

## 11. 已知边界（别当 bug 回炉）

1. **落点位置可能与用户上次摆放不同**。走②/③/④档修复时，面板可能回到"另一个空列"或"新建的 260 宽列"而不是原列。
   这是刻意的优先级（宁可位置偏，也不能显示不出来）；下一次 Hide 会把有效记忆重新记回去，
   再 Show 就走原路，所以只有第一次修复会看到位置变化。
2. **兜底新列宽度写 260**（与属性栏同宽）。真机上如果经常撞到第④档，说明布局树形态与假设不符，
   那时应该回头查"为什么没有空列"，而不是调这个数。
3. **浮动窗口内的面板不在修复优先级里**：`IsAttachedTo` 只认当前 `manager.Layout` 这棵树，
   记忆指向浮动窗时判为"落不回去"→ 直接走挂回。后果是浮动状态丢失（面板回到主窗），可接受。
4. **反射只读、只有一条写入路径在断言里**（⑥/⑦ 造脏）。产线不写库的私有状态，
   所以库升级改了内部实现时最坏结果是"判据退回让库自己试"，不是行为异常。
5. **真机未验证**。本轮只做到"对着 `ShellCopy.xaml` 实例化出的真 `DockingManager` 复现并修好"。
   真机待验清单见 §12。

---

## 12. 未决问题 / 真机待验

| # | 事项 | 判据 |
| --- | --- | --- |
| U1 | **脏记忆在现场怎么产生的**（§3.1）未定位 | 真机复现时抓 `Layout.xml`：切标签闪退是否仍发生；若仍发生，在 `ShowAnchorable` 的 catch/挂回分支已有一行 `Console.WriteLine`，看日志能分辨走了哪一档 |
| U2 | 真机"切中央文档区标签"多次 | 不应再出现 `ArgumentOutOfRangeException`，且图层/属性面板每次都能显示回来 |
| U3 | 真机"加载布局 / 恢复默认布局 / 切换 .vms 方案"之后 | 关键面板恢复日志应打"（成功）"而不是"（失败：仍未显示）" |
| U4 | 上游修复版（升级包）那条出路 | 若升级，⑥a/⑦a 与 LV3b 会**主动变红**提醒重审，届时再决定摘防护 |
| U5 | 并发会话对同一批文件的重叠 | 见 §13 |

---

## 13. 与并发会话的重叠声明

本轮编辑期间，本仓另有一个进程在改同一族文件（R9 口径，逐字记清楚免得把别人的改动算到我头上）：

| 文件 | 谁的改动 |
| --- | --- |
| `VisionMaster\Services\LayoutHelper.cs` | 我：`ShowAnchorable` / 两个反射读取 / `IsAttachedTo` / `AnchorToHomePane` / `EnsureCriticalPanelsVisible` 改造。另有一份**不是本轮**的改动已在文件里（`ContentFactories` 补登记图层面板、`ApplyScadaPanelMode`、指定宿主重载、`CurrentLayoutSchemaVersion` 注释精简） |
| `UIThemeSmokeTest\Program.cs` | 我：`RunPanelModeChecks` 的 ⑥a/⑥b/⑦a/⑦b/⑧。另有并发会话把活动栏断言改成"活动栏整条下线"两条（属 R47 的改版） |
| `VisionMaster\Shell.xaml` / `Shell.xaml.cs` / `ShellViewModel.cs` / `ShellCopy.xaml` | **不是我改的**（活动栏下线改版）；`Shell.xaml.cs:93-97` 那句"五个 Toggle 停在初值 false"的注释与新形态已经不符，留给那个会话处理 |
| `AGENTS.md` | R47 改写 + 第 17 节（R57）由并发会话落地；本轮**只追加第 18 节 R58** |
| `FlowCanvasChecks\LayoutVisibilityCheck.cs` / `Program.cs` 注册行 / `tools\_dump_prev_mem.ps1` | 全部我加的 |

---

## 14. 复算步骤（照着跑一遍就能验完）

```bash
# ① 取证：库成员的真实形状（internal 接口 / protected 属性 / 字段不同名）
powershell -NoProfile -ExecutionPolicy Bypass -File tools/_dump_prev_mem.ps1

# ② 运行时 10 条（含 ⑥a 的"库自己确实抛"反证）
dotnet build UIThemeSmokeTest/UIThemeSmokeTest.csproj -v q -nologo
UIThemeSmokeTest/bin/Debug/net9.0-windows/UIThemeSmokeTest.exe > /tmp/smoke.log 2>&1
iconv -f GBK -t UTF-8 /tmp/smoke.log | grep -A11 "Panel 模式"     # 冒烟输出是 GBK（控制台代码页）

# ③ 静态 10 条 + 常规 gate 全量
dotnet build FlowCanvasChecks/FlowCanvasChecks.csproj -v q -nologo
FlowCanvasChecks/bin/Debug/net9.0-windows/FlowCanvasChecks.exe > /tmp/gate.log 2>&1
grep -a "LV" /tmp/gate.log ; tail -3 /tmp/gate.log               # 期望 通过: 2455  失败: 0

# ④ 变异反证（跑完必须还原）
#   在 VisionMaster/ShellViewModel.cs 的 ApplyPanelMode 之后插入一条 `...IsVisible = true;`，
#   重跑 ③ → LV3 变红并给出 文件:行号；删掉探针 → 回到 2455 / 0。
```
