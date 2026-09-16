# 开发记录：画布布局元数据隔离 + Version 误递增修复

- 日期：2026-09-15
- 类型：架构地基（画布引入 M1 阶段 M1-7 / M1-7b）
- 触发场景：流程节点画布需要持久化节点坐标；同时代码审查发现 `FlowModel.Version` 的排除名单已静默失效，会污染画布运行态显示。

---

## 一、需求与决策

### 问题 1：坐标若进语义模型，拖一下鼠标就重编译一次（M1-7）

`FlowModel.Version` 只有两个递增来源：

| 触发点 | 位置 |
|---|---|
| 步骤集合增删 | `OnStepsCollectionChanged` |
| 步骤属性变更 | `OnStepPropertyChanged`（订阅每个 `StepModel.PropertyChanged`） |

运行前比对 `FlowSession.CompiledVersion` 与 `FlowModel.Version`，不一致就重新编译。所以一旦把坐标写成 `StepModel` 的通知属性：

```
用户拖节点 → Position 通知 → OnStepPropertyChanged → Version++
          → 版本检查失败 → 全量重编译（实例化插件、编译条件 Lambda）
```

后果：节点一多，拖动时 CPU 全在编译，画布卡顿；"只是挪了个位置"还会弹"图纸已修改需重新编译"。

**决策：布局与语义彻底分家。**

```
图纸（语义）  StepModel      → 参与编译，改动必须递增 Version
视图（布局）  FlowLayoutStore → 独立对象，改动绝不碰 Version
              { StepID → (X, Y, Collapsed) }
```

三条设计约束（本项验收标准）：
1. `FlowLayoutStore` / `NodeLayout` **均不继承 `BindableBase`** → 结构上不可能触发 `StepModel.PropertyChanged`；
2. `FlowModel.Layout` 写成**不调 `SetProperty` 的普通属性** → 不进入通知链；
3. `FlowCompiler` 只读 `StepModel`，从不读布局 → 布局改动不影响编译产物。

变更通知走 `LayoutChanged` 显式事件，供画布局部刷新，与流程语义解耦。

### 问题 2：Version 排除名单已静默失效（M1-7b，真 Bug）

原实现：

```csharp
if (e.PropertyName == "IsSelected" || e.PropertyName == "LastRunTime")
    return;
```

两项**全是死的**：
- `"IsSelected"`：`StepModel` 从未拥有该属性（只有 `ToolView.xaml` 注释里提过"需节点 VM 暴露 IsSelected"）；
- `"LastRunTime"`：耗时 Stopwatch 改造后已更名为 `LastRunTimeMs`，并新增 `CurrentRunTimeMs` / `IsRunningFocus` / `LastRunStartTimestamp` / `State`，均未列入。

实测后果：`BeginTiming`(2) + `EndTiming`(2) + `ResetState`(4) = **每个步骤每轮 8 次 `Version++`**，10 步流程一轮约 80 次 → 每次运行前都被迫全量重编译。

对画布的直接影响：M2 要做运行态高亮（改 `State`/`IsRunningFocus`），不修它，流程一跑起来画布就会不停弹"图纸已修改需重新编译"——**虚假脏状态**。

**决策：用 `[RuntimeState]` 特性取代字符串名单。**

理由：名单已经漂移失效一次，说明"哪些属性是运行时的"这个知识不该集中写在 `FlowModel` 里（改属性名的人不会想到去那里更新）。特性把它放在属性旁边，重命名/新增时同屏可见；且新增运行时属性**忘了打标记**的后果是"多编译一次"（可感知），而不是"名单悄悄过期"（不可感知）。

`IsDisEnable` **刻意不标记**——它改变编译行为（`if (model.IsDisEnable) continue;`），必须递增 Version。

### 其他取舍

- **`AutoLayout` 只补缺项**：已有坐标一律不动，避免用户手工布局被覆盖。递归处理容器分支，分支横向错开、子层向下预留高度。
- **步骤删除同步清理布局**（`OnStepsCollectionChanged` 的 `OldItems` 分支），否则残留垃圾项。
- **`Layout` 的 setter 挡 null**：画布侧可无脑 `flow.Layout.XXX`，不必判空。
- **`TryGet` 失败输出 null 而非假坐标**：避免调用方拿 `(0,0)` 当真实位置渲染，与 `Find()` 语义一致。
- **同值写入不发事件**：避免拖动过程中事件刷屏。
- 布局随 `FlowModel` 存盘（`Nodes` 带 public setter 供 Newtonsoft 整体替换）。`FlowLayoutStore`/`NodeLayout` 都是具体类型，`TypeNameHandling.Auto` 不会为它们写 `$type`，因此**不受方案白名单 binder 影响**。

---

## 二、修改文件清单

### 1. `Core\Models\FlowLayoutStore.cs`（新增）
- `sealed class NodeLayout { double X; double Y; bool Collapsed; }`
- `sealed class FlowLayoutChangedEventArgs : EventArgs { IReadOnlyCollection<Guid> AffectedSteps }`
- `sealed class FlowLayoutStore`：`Nodes` 字典（`[JsonProperty("Nodes")]`）、`event LayoutChanged`、`Count`、`TryGet`、`Find`、`Set`、`ToggleCollapsed`（返回切换后状态）、`Remove`、`HasMissing`、`AutoLayout` + 私有 `LayoutLevel` / `EnumerateSelfAndNested`

### 2. `Core\Models\ProcessStep\RuntimeStateAttribute.cs`（新增）
- `[AttributeUsage(AttributeTargets.Property, Inherited = true)] sealed class RuntimeStateAttribute`，注释里写明"为什么不用字符串名单"

### 3. `Core\Models\ProcessStep\StepModel.cs`
- 5 个运行时属性加 `[RuntimeState]`：`State`、`IsRunningFocus`、`LastRunStartTimestamp`、`LastRunTimeMs`、`CurrentRunTimeMs`

### 4. `Core\Models\FlowModel.cs`
- `using System.Reflection;`
- 新增 `Layout` 属性（普通 get/set，不调 `SetProperty`；setter 内 `value ?? new FlowLayoutStore()`）+ 私有字段 `_layout`
- 新增 `static HashSet<string> s_runtimePropertyNames` + `CollectRuntimePropertyNames()`：遍历 `typeof(StepModel).Assembly` 中所有 `StepModel` 派生类型的 `DeclaredOnly` 公共实例属性，收集带 `[RuntimeState]` 的名字
- `OnStepPropertyChanged`：字符串比较 → `s_runtimePropertyNames.Contains(e.PropertyName)`
- `OnStepsCollectionChanged` 的 `OldItems` 分支：单行 `foreach` 改为块，新增 `Layout.Remove(item.StepID)`

---

## 三、验证结果

### 编译
`dotnet build VisionMaster\VisionMaster.csproj` → **0 错误**，已成功生成。

过程中修正两处自身问题：
1. `[JsonIgnore]` 标注事件触发 `CS0592`（该特性不适用于事件声明）→ 移除，事件成员本就不被序列化；
2. `TryGet` 原实现找不到时返回 `new NodeLayout()` 且做两次字典查找 → 改为标准 TryGet 语义 + 单次查找。

### 运行时验证（临时控制台项目，反射读取私有静态字段 + 断言 Version 变化）

**22 项断言全部通过，退出码 0：**

```
运行时属性集合 = 5 项: State, IsRunningFocus, LastRunStartTimestamp, LastRunTimeMs, CurrentRunTimeMs
[1]  集合含 5 个运行时属性                                  OK ×5
[2]  改语义属性 Description → +1                    1 -> 2   OK
[3]  BeginTiming/EndTiming/ResetState(8 次通知) → +0 2 -> 2   OK   ← 虚假脏状态已消除
[4]  布局写入 3 次 → Version +0                      2 -> 2   OK   ← 拖拽不触发重编译
[5]  布局回读 = 最后一次写入                    X=300 Y=480 Collapsed=True  OK
[6]  重复写同值 → 不发事件                       eventCount=0  OK
[6]  写不同值 → 发一次事件                       eventCount=1  OK
[7]  有缺项时 HasMissing = true                   True        OK
[7]  AutoLayout 只补 1 项                         added=1     OK
[7]  补齐后 HasMissing = false                    False       OK
[7]  原有坐标未被覆盖                            X=300 Y=500  OK
[7]  新步骤获得坐标                              X=0 Y=590    OK
[8]  删除步骤 → 布局项同步移除                     Count 2 -> 1 OK
[9]  方案 JSON 含 Nodes 段                                                  OK
[9]  序列化往返保留布局                          X=300 Y=500 Collapsed=True  OK
[9]  往返后 Version 保持                          4 -> 4      OK
[10] 旧方案无 Layout 字段 → 非空可用               Layout.Count=0 OK
[10] 旧方案补布局后 Version 不变                   Version=7    OK
```

第 [1] 项专门用于防"修复静默失效"：若反射收集逻辑有 bug 导致集合为空，[3] 会立刻失败。
第 [10] 项确认旧 `.vms` 方案（无 `Layout` 字段）加载不报错、可直接补布局。

验证脚本为临时项目 `_verify\`，验证后已删除，未纳入解决方案。

### 人工待验（需真机跑流程）
1. 循环运行一次流程 → 观察 `FlowModel.Version` 不再每轮暴涨（可在流程栏绑定显示）；
2. 画布接入后拖动节点 → 不应出现"图纸已修改"提示（M1-8 覆盖）。

---

## 四、已知边界

| 项 | 说明 | 归属 |
|---|---|---|
| `State` 仍参与存盘 | 它没有 `[JsonIgnore]`，即步骤执行状态会被写进方案文件，重启后残留上一轮状态。语义可疑但不影响本次修复（已不递增 Version） | 待确认 |
| `[RuntimeState]` 靠人工标记 | 新增运行时属性若忘打标记，后果是"多编译一次"（可感知），优于旧方案的静默失效；如需更强约束可加一条单元测试断言属性清单 | 待定 |
| 反射收集有首次调用成本 | 遍历 Core 程序集全部类型一次，静态缓存。类型数约百级，成本可忽略 | 设计如此 |
| 布局不参与撤销栈 | `LayoutChanged` 与画布 Undo 是两套。工业软件通常不把"移动节点"纳入撤销，避免误撤销导致布局混乱 | M2 |
| 无缩放/视口持久化 | 只存节点坐标，未存画布 `Viewport`（平移量、缩放比） | M2 |
| `AutoLayout` 参数硬编码 | 行高 90 / 列距 260 / 缩进 220。节点尺寸不统一时会重叠，M2 需按实际高度计算 | M2 |
| 折叠态不参与编译 | `Collapsed` 纯视图，折叠容器不影响执行顺序（顺序仍由 `SortId` 决定） | 设计如此 |
| 布局增大方案体积 | 每步骤约 3 个数值，数百步骤时明显。可考虑压缩或分文件 | 待定 |
| 反序列化顺序依赖 | 若 JSON 中 `Layout` 出现在 `Steps` 之后，`Steps` setter 的清理逻辑操作新 `Layout` 实例。`Layout` 由字段初始化器保证非空，且旧方案无该字段，实际无风险 | 观察 |
