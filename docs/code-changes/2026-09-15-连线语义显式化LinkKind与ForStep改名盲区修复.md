# 开发记录：连线语义显式化（LinkKind）+ 编译错误结构化 + 改名级联精确化

- 日期：2026-09-15
- 类型：Bug 修复 + 架构地基（画布引入 M1 阶段前置改造 M1-0 / M1-1 / M1-2 / M1-6a）
- 触发场景：评估引入"UI 画布拖拽（HMI 组态 + 流程节点画布）"时，对图纸→编译→运行链路做代码审查，发现四处必须在动画布之前修掉的隐患。

---

## 一、问题定位与决策

### 问题 1：ForStep 不在改名递归覆盖范围内（Bug）

步骤改名级联 `UpdateReferencesRecursively` 的容器判定写成 `step is ConditionStep`：

- `WhileStep : ConditionStep` → 能被覆盖 ✅
- **`ForStep : StepModel, IContainerStep`** → 不是 ConditionStep，**循环体内的连线地址与表达式永不更新** ❌

后果：For 循环体里的步骤改名后，体内其他步骤指向它的 `LinkedSources` 显示地址、以及体内 If/While 分支的 `Expression` 全部残留旧名。

**决策**：判定条件由 `is ConditionStep` 改为 `is IContainerStep`。已核实 `ForStep.Children` 的循环体是 `BranchType.Default` 分支（`RequiresExpression == false`，`Expression` 恒空），因此纳入递归不会误动表达式替换逻辑。

### 问题 2：连线语义藏在显示串里（正确性隐患，非洁癖问题）

`LinkReference` 用 `TargetStepId` 一个字段承载四种语义，靠哨兵值 + UI 文案前缀反推：

| 数据源 | TargetStepId | TargetPortName 含义 | 编译器判据 |
|---|---|---|---|
| 步骤输出 | 真实 StepID | 端口名（可带 `[索引]`） | 兜底 else |
| 运行时变量 | `RuntimeVariableMarkerGuid` | 变量名 | 哨兵 Guid 相等 |
| **常量** | **`Guid.Empty`** | 常量值字符串 | **`DisplayAddress.StartsWith("常量值: ")`** |
| **全局变量** | **`Guid.Empty`** | 变量名 | 上述前缀不成立 |

常量与全局变量的 `TargetStepId` 都是 `Guid.Empty`（全局变量节点在 `FlowQueryHelper.GetAvailableVariablesTree` 里构造时未设 `Id`），**唯一区别是 `DisplayAddress` 有没有 `"常量值: "` 这个纯 UI 前缀**。

后果：改动一句 UI 文案、或用户手输地址串，常量会被当成全局变量解析（或反之），编译器不报错、静默连错。

**决策**：引入显式 `LinkKind` 枚举作为编译语义，`DisplayAddress` 退回纯展示职责。

关于"绑定内核与现有 LinkReference 是一套还是两套"的架构选择：**扩展 LinkReference + 解析器接口化**——不新建 `BindingRef`，但解析器面向 `IBindingSource` 写，供后续 HMI 组态（M4）复用同一套解析逻辑，避免过早抽象也避免重构债。

`For.Index` 不进枚举：它属于 StepPort 特例，画布侧可从 `ForStep` 模型直接推断出存在该隐藏锚点。

### 问题 3：编译错误是纯字符串，无法定位到节点（M1-2）

`CompilationResult.Errors` 是 `List<string>`，13 处 `errors.Add` 只带一句人话。流程画布要做"错误节点红框 + 双击跳转 + 悬停看原因"，光有文字定位不到是哪个步骤，M2 的画布会直接卡在这。

**决策**：升级为结构化 `CompilationError { Guid? StepId, string? StepName, string Message }`。

关键取舍：
1. **`ToString() => Message`**：让既有消费点（`string.Join`、`$"{item}"` 插值）**零改动**继续工作，只有 `Notifier.ShowError(item)` 这种要求 string 的地方必须显式改成 `.Message`。实测 5 个消费点里 4 个免改。
2. **`StepId` 可空**：系统崩溃级异常发生在步骤遍历之外，无法归属，用 `null` 表示"流程级错误"，画布据此决定是全局提示还是节点红框。
3. 新增私有辅助 `Err(StepModel owner, string message)` 统一构造，避免 13 处各自漏填 `StepId`。
4. `CompileLocalVarParams` / `CompileRuntimeVarRefs` 原本拿不到步骤上下文，加 `StepModel owner` 首参，由调用方传 `model`。

### 问题 4：改名级联三处缺陷（M1-6a）

`WorkspaceContext.OnStepRenamed` 旧实现：

```csharp
if (CurrentFlow == null) return;                                    // 缺陷 A
UpdateReferencesRecursively(CurrentFlow.Steps, args.OldName, args.NewName);
// 内部：
if (linkedAddress.DisplayAddress.StartsWith(oldName + "."))         // 缺陷 B
    ...Replace(oldName + ".", newName + ".");
branch.Expression = Regex.Replace(branch.Expression, $@"\b{oldName}\b", newName);  // 缺陷 C
```

| 缺陷 | 后果 |
|---|---|
| A 只处理 `CurrentFlow` | 未切换流程或跨流程引用时，**整件事什么都不做** |
| B 按显示串前缀匹配 | 两个流程里都有"Blob定位"时**互相误伤**；且 `TargetStepId` 明明能精确定位却没用 |
| C 用 `\b旧名\b` 改表达式 | 条件表达式里的标识符是**用户自起的变量别名**（`LocalVariableItem.Name`），与步骤名无关。若别名恰好等于步骤名，表达式被改成 `Blob定位_v2 > 80` 而 `LocalVariableItem.Name` 仍是 `Blob定位` → `delegateParams` 对不上 → **自造语法错误** |

**决策**：有了 M1-1 的 `Kind`，级联可以做得极干净——

```csharp
if (link.NormalizeKind() != LinkKind.StepPort) continue;   // 全局/运行时/常量地址与步骤名无关
if (link.TargetStepId != args.StepId) continue;            // 按 Guid 精确定位
link.DisplayAddress = $"{args.NewName}.{link.TargetPortName}";
```

- 遍历 `CurrentSolution.Flows` 全部流程，去掉对 `CurrentFlow` 的依赖；
- 判据换成 `StepId`，彻底消除同名误伤；
- **删除对 `Expression` 的整段替换**（缺陷 C 属"多余且有害"，不是"做得不够"）；
- `StepRenamedMessage` 增加 `Guid StepId`，`StepModel.StepName` setter 发布时带上；
- `NormalizeKind()` 在此调用有额外收益：旧工程未回填 Kind 的连线，经一次级联即被补齐（顺带完成数据修复）。

---

## 二、修改文件清单

### 1. `Shard\Core.Interfaces\LinkReference.cs`（协议层，主要改动）
- 新增 `enum LinkKind { Unset=0, StepPort=1, GlobalVariable=2, RuntimeVariable=3, Constant=4 }`
- 新增 `static class LinkProtocol`：`RuntimeVariableMarkerGuid`（从 FlowCompiler 上收）+ `ConstantDisplayPrefix = "常量值: "`
- `LinkReference` 新增 `Kind` 属性（参与存盘）
- 新增 `LinkKind(kind, targetId, targetPort, displayAddress)` 显式构造；旧三参构造保留并内部调用 `InferKind`，保证不产生 `Unset`
- 新增 `static InferKind(Guid, string)`：按旧规则推断，**唯一用途是给无 Kind 字段的旧工程回填**
- 新增 `NormalizeKind()`：`Unset` 时回填，已有值不动

### 2. `Core\IReadOnlyWorkspaceContext.cs`
- `UpdateReferencesRecursively`：`is ConditionStep conditionNode` → `is IContainerStep containerNode`（含决策注释）

### 3. `Engine\FlowCompiler.cs`
- 删除本地 `RuntimeVariableMarkerGuid` 定义（已上收协议层），留注释说明归属原因
- `LinkPorts` 判定链：`if (TargetStepId == marker)` / `else if (TargetStepId == Guid.Empty)` + 内嵌前缀判断 → 改为先 `var linkKind = linkRef.NormalizeKind()`，再按 `LinkKind.RuntimeVariable / Constant / GlobalVariable / else(StepPort)` 平级分派
- 常量分支不再读取 `DisplayAddress`

### 4. `Core\Models\ToolItemModel.cs`
- 新增 `LinkKind DefaultLinkKind { get; init; } = LinkKind.StepPort`（选中该候选节点作数据源时应产出的连线类型）
- 补 `using Core.Interfaces;`

### 5. `Engine\FlowQueryHelper.cs`
- 全局变量节点：`DefaultLinkKind = LinkKind.GlobalVariable`
- 运行时变量节点：`Id` 引用改为 `LinkProtocol.RuntimeVariableMarkerGuid`，并加 `DefaultLinkKind = LinkKind.RuntimeVariable`
- 补 `using Core.Interfaces;`

### 6. `VisionMaster\ViewModels\DialogViewModels\VariableBindingViewModel .cs`
- `DoFinalBind`：`targetId == FlowCompiler.RuntimeVariableMarkerGuid` → `SelectedNode.DefaultLinkKind == LinkKind.RuntimeVariable`；构造改为显式传 `kind`
- `Confirm` 常量分支：`$"常量值: {ConstantValue}"` → `$"{LinkProtocol.ConstantDisplayPrefix}{ConstantValue}"`；构造改为 `new LinkReference(LinkKind.Constant, Guid.Empty, ConstantValue, displayName)`

### 7. `Core\Models\Compileds\CompilationResult.cs`（M1-2，重写）
- 新增 `sealed class CompilationError { Guid? StepId; string? StepName; string Message; override ToString() => Message }`
- `Errors` 类型 `List<string>` → `List<CompilationError>`
- `NG(string)` 内部改为构造 `CompilationError`（StepId 为 null，流程级）
- 顺带清掉文件头重复的 UTF-8 BOM 字节，删掉未使用的 using

### 8. `Engine\FlowCompiler.cs`（M1-2）
- 新增私有 `static CompilationError Err(StepModel owner, string message)`
- `CompileSteps` / `LinkPorts` / `CompileRuntimeVarRefs` / `CompileLocalVarParams` 四处 `List<string> errors` → `List<CompilationError> errors`
- 后两个方法加 `StepModel owner` 首参，两个调用点（While / ConditionStep）传 `model`
- 11 处 `errors.Add($"…")` → `errors.Add(Err(model, $"…"))`，覆盖：安全拦截×2、While 条件空/语法×2、If 分支空/语法×2、算子加载失败、运行时变量名为空、节点类型不支持、找不到全局变量、上游无该输出、致命断连、必填参数缺失
- 系统崩溃级 catch：显式构造 `CompilationError`（StepId 留 null）

### 9. `VisionMaster\ShellViewModel.cs`（M1-2）
- L605 `Notifier.ShowError(item)` → `Notifier.ShowError(item.Message)`（唯一必须改的消费点）
- L670 / L721 / L790 三处 `$"[{flow.FlowName}] {item}"` 走插值，靠 `ToString()` 免改

### 10. `Core\EventModel\StepRenamedMessage.cs`（M1-6a）
- 新增 `Guid StepId { get; }`，构造函数签名改为 `(Guid stepId, string oldName, string newName)`
- 类注释由"更新连线和表达式"改为"更新连线显示地址"

### 11. `Core\Models\ProcessStep\StepModel.cs`（M1-6a）
- `StepName` setter 发布消息时带上 `StepID`

### 12. `Core\IReadOnlyWorkspaceContext.cs`（M1-6a）
- `OnStepRenamed`：改为遍历 `CurrentSolution.Flows` 全部流程，去掉 `if (CurrentFlow == null) return;`
- 新增私有静态 `RefreshStepPortDisplayAddresses(IEnumerable<StepModel>, StepRenamedMessage)`：递归容器分支，按 `NormalizeKind() == StepPort && TargetStepId == args.StepId` 精确匹配后重算 `DisplayAddress`
- **删除** 旧 `UpdateReferencesRecursively`（含对 `branch.Expression` 的正则替换）
- using 调整：移除已无引用的 `System.Text.RegularExpressions`，新增 `Core.Interfaces`（`LinkKind`）

---

## 三、验证结果

- `dotnet build VisionMaster\VisionMaster.csproj`：**0 错误**，成功生成（唯一 warning 为 `HslCommunication.csproj` 存量 `System.Resources.Extensions` 引用解析失败，与本次改动无关）。M1-1、M1-2 各编译一次，均通过。
- 此前 `dotnet build Core\VM.Core.csproj`：0 错误，202 个存量 nullable 警告。
- M1-2 兼容性验证：`PluginTestRunner` 的 `string.Join("\n", result.Errors)` 与 `ShellViewModel` 三处插值消费点在类型变更后仍编译通过且输出文本不变，证明 `ToString()` 兜底策略有效。
- 向后兼容路径：旧工程 JSON 无 `Kind` 字段 → 反序列化为 `Unset(0)` → 编译器 `NormalizeKind()` 按原哨兵+前缀规则回填 → 行为与改动前完全一致，**无需迁移脚本**。

### M1-6a 运行时验证（临时控制台项目，验证后已删除）

**14 项断言全部通过，退出码 0。** 测试全程只调 `SwitchSolution` 不调 `SwitchFlow`，即 `CurrentFlow == null` —— 旧实现在该状态下直接 `return` 什么都不做，新实现照常完成级联，**跨流程覆盖得到实证**。

| 断言 | 结果 |
|---|---|
| 前置：两个流程里"Blob定位"步骤的 Guid 互不相同 | OK |
| [1] A 流程内 StepPort 地址更新为 `Blob定位_v2.中心X` | OK |
| [2] **B 流程同名步骤未被误伤**（仍为 `Blob定位.中心X`） | OK |
| [3] **For 循环体内地址更新为 `Blob定位_v2.ROI`**（M1-0 修复验证） | OK |
| [4] 全局变量地址 `全局变量 (Global).触发频率` 未受扰动 | OK |
| [5] 常量地址 `常量值: 3.14` 未受扰动 | OK |
| [6] 运行时变量地址 `Runtime.Score` 未受扰动 | OK |
| [7] **条件表达式 `Blob定位 > 80` 未被破坏**（缺陷 C 已消除） | OK |
| [8] 变量别名 `Blob定位` 未被改动 | OK |
| [9] 旧式三参构造 Kind 自动回填为 `StepPort` | OK |
| [9] 裸对象初始化器（未设 Kind）初始为 `Unset` | OK |
| [9] 裸对象经级联归一化后地址正确更新 | OK |
| [9] 归一化副作用生效：其 `Kind` 已被补齐 | OK |
| [10] 一次改名 → Version 只 +1（不因引用数放大） | OK |

- 人工待验（回归四类连线 + 数组索引）：
  1. 绑常量 → 编译日志 `actualUpstreamName == "常量"`，`ConstantOutputPort` 生效；
  2. 绑全局变量 → 正常取到 `IVariable`，改名后编译报"找不到全局变量"；
  3. 绑运行时变量（变量定义插件产出）→ `RuntimeVariableProxyPort` + `BindContext` 生效；
  4. 绑上游步骤输出（含 `Port[2]` 数组索引）→ `ArrayIndexProxyPort` 生效；
  5. For 循环体内步骤改名 → 体内**连线显示地址**同步更新（已由断言 [3] 覆盖）；体内**分支表达式保持不变**（已由断言 [7] 覆盖，属预期行为，非遗漏）。

---

## 四、已知边界

| 项 | 说明 | 归属 |
|---|---|---|
| 全局变量仍按 Name 寻址 | `GlobalVariables.FirstOrDefault(s => s.Name == ...)`，变量无稳定 Id，改名即断链且无级联 | M4 前置 |
| 变量改名完全没有级联 | 全局变量、`ConditionStep.RuntimeVariableRefs`、分支 `Expression` 里的变量别名，改名后均无任何同步机制（连事件都不存在）。步骤改名已修，**变量改名仍是空白** | M4 前置 |
| 存在第三套地址语法 | `MonitorViewModel` 用 `Global.变量名` / `算子.端口` 字符串前缀独立解析，未与 `LinkKind` 统一。绑定内核落地时应一并收编 | M4 前置 |
| `Unset` 不报错 | 手工对象初始化器构造且未设 `Kind` 的新连线会被静默回填，不阻断编译。若需强约束可改为编译期告警 | 待定 |
| `StepId` 已落数据但无消费方 | 错误红框、双击跳转要等 M2 画布实现；当前 UI 仍只显示 `Message` 文本，行为与改造前一致 | M2 |
| 级联只更新显示串，不校验语义 | 改名后 `DisplayAddress` 重算，但 `TargetStepId` 指向的步骤若被删除，只有编译时才报"致命断连"，编辑期无提示 | M2 |

### 本次已闭环（原列为边界，现已修复）

| 原边界 | 处置 |
|---|---|
| 改名级联按字符串前缀匹配、同名步骤误伤 | M1-6a 改为按 `TargetStepId` 精确定位，断言 [2] 验证 |
| 改名只覆盖 `CurrentFlow` | M1-6a 改为遍历 `CurrentSolution.Flows`，断言在 `CurrentFlow == null` 下通过 |
| For 循环体不在改名递归覆盖范围内 | M1-0 判定条件改 `is IContainerStep`，断言 [3] 验证 |
| 步骤改名误改条件表达式（自造语法错误） | M1-6a 删除该段替换逻辑，断言 [7][8] 验证 |
| 坐标未与语义隔离 | M1-7 新增 `FlowLayoutStore`，见同日另一份记录 |
