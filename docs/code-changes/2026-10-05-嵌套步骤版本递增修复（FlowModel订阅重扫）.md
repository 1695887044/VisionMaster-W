# 2026-10-05 嵌套步骤版本递增修复（FlowModel 订阅重扫）：禁用分支内步骤不再被旧会话照跑，闭环 S3

- 日期：2026-10-05
- 项目：VisionMaster（WPF + Prism 9 + Halcon）
- 范围：1 个文件——`Core\Models\FlowModel.cs`（零插件改动、零公共契约改动、零 UI 改动）
- 类型：缺陷修复（版本链缺口 · 订阅生命周期重构：按事件明细摘挂 → 结构变更后按可达性重扫）
- 前置：`docs/code-changes/2026-10-04-流程栏与程序栏右键菜单清理与新功能.md` 遗留表 **S3**——本记录闭环该条目
- 口径：构建 / 探针 / 审查证据由施工与审查会话给出（出处逐条标注）；本记录落盘时对 `FlowModel.cs` 现状、断言组标签与关键行号做了源码复核（静态核对；行号为 2026-10-05 落盘时点）

---

## 一、背景：S3 的来路与危害

### 1.1 `Version` 是「图纸是否需重编译」的脏检查依据

两个消费点（均以 `flow.Version > session.CompiledVersion` 为判据）：

- `VisionMaster\ShellViewModel.cs` 的 `PreRunCheck()`（:714 起）：运行前发现图纸版本大于已编译版本 → 弹「检测到流程图纸已修改，当前的运行逻辑已过期。是否重新编译？」（:735）；
- `VisionMaster\Services\HttpImageServer.cs`（:292、:340）：HTTP 触发路径同判据。

这条链上任何一环丢版本递增，代价都是「旧逻辑照跑」被静默放行。

### 1.2 缺陷（上一轮 reviewer 登记为 S3）

旧实现只订阅顶层 `Steps` 的 `CollectionChanged` 与**顶层步骤**的 `PropertyChanged`。嵌套步骤（If / While / For 分支 / 循环体内）无人订阅 →

**禁用分支内步骤不递增版本 → 已编译会话被复用，被禁步骤照跑**（`FlowCompiler` 里 `if (model.IsDisEnable) continue;` 只作用于编译产物，图纸脏了却没人重编译时，禁用根本不生效）。

### 1.3 初步修复的四条残留缺口（复查发现）

此前一轮的初步修复是「递归挂 / 摘，按事件 New / OldItems 逐个处理」，复查发现四个残留缺口：

| # | 缺口 | 后果 |
| --- | --- | --- |
| 1 | 往**已存在的分支**里后加入 / 移入的步骤不会被挂订阅（订阅点只覆盖「加入时刻的当时的树」） | 之后禁用 / 改参数静默不生效 |
| 2 | 新增 ElseIf / Else 分支只动 `Children` 集合、无人订阅该集合 | 新分支里的步骤永远挂不上 |
| 3 | **Move（拖动改序）同时带 OldItems 与 NewItems** | 按明细摘挂净摘掉被移动步骤的订阅（先加后减，净效果为减） |
| 4 | **Reset（Clear）无明细** | 既漏摘旧订阅，也没清布局 |

### 1.4 本轮方案（定稿）

**放弃按事件明细摘挂，改为「结构一变就按可达性重扫」**：任何被订阅集合发生结构变更 → 递增版本 + 全树重扫订阅名单 + 清理已不可达步骤的布局条目。重扫代价 O(全树步骤数)，而结构变更由用户操作驱动、不在运行热路径上。

---

## 二、改动清单（唯一文件 `Core\Models\FlowModel.cs`）

本文件是 VM.Core 程序集（命名空间 `VisionMaster.Models`）中的流程模型；本轮全部改动集中于此。行号为 2026-10-05 落盘时点。

| # | 成员 | 内容 |
| --- | --- | --- |
| 1 | `_watchedSteps`（:216） | 新增 `HashSet<StepModel>`：已订阅 `PropertyChanged` 的步骤名单（全树，含嵌套分支 / 循环体内）。`StepModel` 未重写 `Equals` / `GetHashCode` → 引用相等语义成立 |
| 2 | `_watchedCollections`（:225） | 新增 `HashSet<INotifyCollectionChanged>`：已订阅 `CollectionChanged` 的集合名单——顶层 `Steps`、每个 `StepCollection.Steps`、每个容器的 `Children`。注释写明「为什么连 `Children` 也要盯」（新增 ElseIf / Else 只动 `Children`，不盯就收不到事件） |
| 3 | `RebuildSubscriptions()`（:236） | 新增：从 `_steps` 出发深扫收集「应订阅名单」，与两张名单**求差后先摘后挂**——不在名单里的 `-=` 摘除；新出现的须 `Add()` 返回 true 才 `+=` 挂上（天然防重复订阅） |
| 4 | `CollectSubscriptions()`（:273） | 新增（`private static`）：**迭代实现**（`Stack` + `HashSet` 兼作访问标记与**环检测**）——防深层嵌套爆栈、防手工编辑成环死循环；null 集合 / null 步骤直接跳过 |
| 5 | `OnStructureChanged`（:309） | 新增统一处理器：被订阅集合的任意结构变更 → `Version++` → 快照旧步骤名单 → `RebuildSubscriptions()` → 对「重扫后不再可达」的步骤 `Layout.Remove(step.StepID)`（判据避开 Move 带 OldItems 的误删陷阱；Move 改序时被移动步骤坐标保留） |
| 6 | `Steps` setter（:188-201） | 整体替换后**只调 `RebuildSubscriptions()`、刻意不递增 `Version`**（反序列化路径；版本由 JSON 赋值；内容变更由集合事件负责）。`[JsonProperty(ObjectCreationHandling = Replace)]` 保持；注释同步改写为「一次重扫即完成挂接」 |
| 7 | 构造函数（:206-211，`[JsonConstructor]`） | 把 `_steps` 加入 `_watchedCollections` 并挂 `OnStructureChanged`（初始空集合也在订阅下） |
| 8 | 运行时属性闸门 | **不变**：`OnStepPropertyChanged`（:361）仍是属性驱动递增的唯一入口；`[RuntimeState]` 反射名单（`s_runtimePropertyNames`，:330 起）照旧过滤——`State` / `IsRunningFocus` / `LastRunStartTimestamp` / `LastRunTimeMs` / `CurrentRunTimeMs` 不递增 |

核心改动段（`OnStructureChanged`，与源码一致、仅去缩进）：

```csharp
private void OnStructureChanged(object? sender, NotifyCollectionChangedEventArgs e)
{
    Version++;

    // 先快照，重扫后不在名单里的就是被移出流程树的步骤，顺带清掉它们保存的坐标。
    // 判据用"是否仍可达"而不是"是否出现在 OldItems 里"：Move（拖动改序）同样带
    // OldItems，但步骤还在树上，误删会让它的位置丢失（下次渲染被迫重新自动布局）。
    var before = _watchedSteps.ToList();
    RebuildSubscriptions();

    foreach (var step in before)
    {
        if (!_watchedSteps.Contains(step))
            Layout.Remove(step.StepID);
    }
}
```

---

## 三、关键决策与理由

1. **为什么放弃「按事件明细摘挂」**——四个缺口（§1.3）逐条命中「按明细永远补不全」的结论：后加入步骤、新分支、Move 双清单、Reset 无明细。改按**可达性**重扫后语义一步归位：**订阅集合 = 当前可达集合；订阅步骤 = 当前可达步骤**。
2. **为什么重扫成本可接受**——O(全树步骤数)，且结构变更由用户操作驱动（增删 / 拖动 / 清空），不在运行热路径上。
3. **为什么布局清理用「重扫后是否仍可达」做判据**——Move 同样会带 OldItems：以「出现在 OldItems 里」为删据，会把**还在树上**被移动步骤的坐标误删；用可达性判据则「真删除 → 不可达 → 清坐标；Move → 仍可达 → 坐标保留」。
4. **为什么 `Steps` setter 不递增 `Version`**——该 setter 是反序列化路径（`ObjectCreationHandling.Replace` 让 Newtonsoft 整体替换集合并走 setter）；打开方案不应被自身标脏，版本号由 JSON 里的 `Version` 赋值，内容变更由集合事件负责。同时一次重扫把新集合（含子树）挂齐，堵住「新集合没订阅 → 通知丢失」。
5. **顺带修正的行为变化（已知，经审查确认）**——拖动改序（Move）此后**保留**被移动步骤的已保存坐标（旧实现会把它清掉）；经审查确认符合「坐标是视图元数据」的设计意图。
6. **防御性细节**——手写栈迭代 + `HashSet` 环检测：防深层嵌套爆栈、防手编 JSON 造环死循环（结构是用户可编辑的 JSON，不假设其良构）。

---

## 四、验证证据

| 项 | 结果 | 口径 |
| --- | --- | --- |
| 全解决方案构建 | `dotnet build VisionMaster.sln -v q --nologo` → 退出码 0 / **0 错误**（2026-10-05 18:22 复跑） | 构建验证（施工会话复跑） |
| 独立探针（临时工程，不在仓库内） | **26 项断言全绿**。覆盖：顶层 / 嵌套禁用；后加入分支的步骤；跨分支移动；Move 改序后订阅保留；Clear 后旧步骤不惊动 / 新步骤仍生效；新增 ElseIf 分支及新分支内步骤；三层嵌套；无重复订阅（一次变更恰好 +1）；运行时属性不惊动；**`TypeNameHandling.Auto` 序列化往返**（往返后嵌套 / 顶层步骤禁用均递增、新加顶层 / 新分支步骤仍被订阅）——此条为反序列化路径的关键验证 | 探针验证（施工会话） |
| 既有断言集 FlowCanvasChecks——本修复相关组 | 全绿：`[M]` 拖拽改序与 Version（Redo / Undo 各递增 1 次；`FlowCanvasChecks\Program.cs:967-971`）；`[E5]` 运行状态连改 7 次不递增、语义变更仍触发（`ExecutionChecks.cs:211-258`） | 断言集运行（施工会话） |
| 当次断言总计 | 通过 1017 / 失败 19 | 同上 |
| 19 项失败归因 | **与本修复无关的存量**：虚拟相机 VIRTUAL-001 未注册、onnxruntime_providers_shared.dll 无托管元数据、UI 资源键 `Dialog*` 计数、属性面板候选生成器、线序检测 / 预览帧等；失败清单与本修复无交集 | 排查结论（施工会话） |
| 审查复核期并发扰动 | 另有**并发会话**在改 `FlowCanvasChecks\CalibrationChecks.cs` 与 `Plugins\Plugin.Calibration\CalibrationPlugin.cs`（20:27 / 20:28 时间戳），引入 5 项临时失败（合计 24），同样与本修复无关 | 排查结论（施工会话） |
| 独立审查（reviewer） | **pass**。要点：摘挂自洽；Move / Clear 处理正确；布局清理语义正确（Move 保留坐标、真删除清理）；反序列化往返订阅完整；运行时闸门未放宽；无重复订阅；无热路径集合变异；无悬空引用。三红线（插件工程位置 / `Modules\` 无公共契约程序集 / 无插件重建义务）全过——本轮只改 Core，且已核实插件工程不引用 VM.Core | 审查结论（reviewer） |
| 落盘复核（静态核对） | `FlowModel.cs` 现状与 §二 清单逐项一致；`[M]` / `[E5]` 断言组标签在位；`Plugins\*.csproj` 全量扫描无 `VM.Core` 引用（仅 `Core.Interfaces`） | docs 落盘会话 |

---

## 五、遗留与后续

| # | 说明 | 状态 / 方向 |
| --- | --- | --- |
| 1 | 探针为**会话级临时工程**（不在仓库内），能力未固化为常设断言 | 建议后续把关键场景折入 FlowCanvasChecks 常设断言（场景清单见 §四） |
| 2 | S3 闭环登记 | `docs/code-changes/2026-10-04-流程栏与程序栏右键菜单清理与新功能.md` 遗留表 S3（原文含「根治方向：`FlowModel` 递归挂接嵌套集合」）**由本篇闭环**；按仓库惯例不在历史文件中改动，闭环登记于此 |
