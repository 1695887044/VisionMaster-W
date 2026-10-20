# 2026-10-20 C#脚本：声明式共享变量（全局 / 系统，读写）

## 1. 背景与动机

原模型把"全局变量"借**数据流的壳**表达：把输入端口绑到一个全局变量上。三个后遗症：

1. **只能读、不能写**——`ScriptContext` 没有写入口，写全局只能绕「变量赋值」两步；
2. **按名字寻址**——变量管理里改名后，输入行那条绑定靠 `LinkReference.TargetVariableId` 兜底，
   脚本里的字符串键则完全对不上；
3. **编译器瞎**——`[并行全局变量写冲突]` 只认 `VariableAssignmentPlugin`
   （`Engine\FlowCompiler.cs` 按类型名匹配），脚本写全局它看不见；而全局写路径无锁。

用户裁定：**声明一张"共享变量"表，专门读写全局/系统变量；输入只负责链接上游**。
这样"数据流边"与"命名状态"分开，编译器也能从声明表拿到"这个脚本会写哪些全局"。

## 2. 设计

### 2.1 三类变量，三种语义

| 类别 | 寻址 | 脚本 API | 参与连线 | 方向 |
|---|---|---|---|---|
| 输入变量 | 端口名（本步骤内唯一） | `Context.GetInput<T>("名")` | 是（上游） | 只读 |
| 输出变量 | 端口名 | `Context.SetOutput("名", 值)` | 是（下游） | 只写 |
| **共享变量** | **`VariableId`（稳定）** | `Context.GetGlobal<T>("别名")` / `SetGlobal` / `TrySetGlobal` | **否** | 只读 / 读写；系统变量恒只读 |

共享变量行的别名（`Alias`）留空 = 用变量名；脚本里用别名，变量改名时脚本不用动。

### 2.2 运行期契约

- `Core.Interfaces.ISharedVariableAccess`（新增）：`TryRead(Guid, fallbackName, …)` /
  `TryWrite(Guid, fallbackName, …)`。与 `IGlobalVariableWriter` 并存（后者按名、只写、既有契约不动）。
- `IExecutionContext.SharedVariables`（新增）：与 `GlobalVariables` **共用同一个**
  `GlobalVariableWriter` 实例（`Core\ExecutionContext.cs`），类型守门/落值逻辑只有一份。
- `GlobalVariableWriter` 同时实现两个接口：`Resolve` 按"Id 优先、名字兜底"
  （与 `VariableRegistry.ResolveGlobalLink` 同口径）；系统变量不实现 `IWritableVariable`，
  写入返回"是系统变量（只读）"。
- `NullSharedVariableAccess`：无工作区的执行环境（简化上下文/断言夹具）读写即失败并给原因。

### 2.3 编译期闸门

`FlowCompiler.CheckParallelVariableWriteConflicts` 改成：

- **递归**枚举分支内步骤（旧实现只看直接分支步骤，If/For 里嵌一层就漏检）；
- 除「变量赋值」外，收集 **C#脚本「共享变量」声明中方向=读写** 的项，统一计入全局写集合；
- 冲突键：脚本声明按 `VariableId` 解析成**当前变量名**（`VariableRegistry.FindById`），
  与「变量赋值」按名写的键合并比较——同一个变量换个别名写也必须撞上；
- 声明读取兼容两种形态：刚确认过是 `ObservableCollection<ScriptSharedVarDef>`，
  从 `.vms` 加载未确认时是 `JArray`（反射/`JObject` 各一支，见 `TryReadSharedWrite`）。

### 2.4 UI

左栏新增第三张卡片「共享变量」（`CSharpScriptView.xaml`）：

- 变量：候选来自宿主快照 `PluginConfigContext.Variables`（`Scope` = `Global` / `System`），
  选中后落 `VariableId` + 当前名字 + 类型 + `IsSystem`；
- 方向：只读 / 读写；系统变量锁定只读（`AccessSelectable=false`）；
- 别名（留空=变量名）、类型（只读展示）、删除；
- 输入行 `LinkableValueEditor` 显式 `AllowGlobals="False"`：输入只接上游。
- 编辑期快照/取消还原**包含 `SharedVars`**（与输入/输出表同一口径）：点「取消」时
  别名修改与新增行一并丢弃，不会留在活模型上。

宿主快照侧：`VariableOption` 增加 `VariableId` / `IsSystem` / `CanWrite`；
`PluginVariableOptions.Build` 追加 `SystemVariables`（`Scope="System"`，只读）。
用独立 Scope 而不是塞进 `Global`：`变量赋值` 的候选按 Scope 精确过滤，
不会出现"选得到系统变量、写的时候才报只读"。

### 2.5 输入只接上游（阶段 3）

`LinkPathEvent.AllowGlobals`（默认 true，兼容既有插件）+ `LinkableValueEditor.AllowGlobals` DP；
`ProcessViewModel` 透传到绑定弹窗；`VariableBindingViewModel` 用
`FlowQueryHelper.FilterGlobalNodes` 把「全局变量」节点整块摘掉。
旧方案里已经绑在输入上的全局变量仍可读（连线还在），只是不能再新绑。

### 2.6 校验脚本

`ScriptSharedUsageValidator` 在 Roslyn 编译前扫描
`Context.GetGlobal("x")` / `SetGlobal("x", …)` / `TrySetGlobal("x", …)` 的**字面量**：

- 未声明 → "第 N 行: 共享变量「x」未声明（请在左侧「共享变量」表添加一行）"；
- 只读声明被写 → "声明为只读，不能调用 SetGlobal（把方向改成「读写」）"；
- 系统变量被写 → "是系统变量（只读），不能调用 SetGlobal"；
- 声明表自身：空别名、重复别名。

动态拼接的名字扫不到——运行期访问器仍会拒绝未声明别名。

## 3. 改动清单

| 文件 | 改动 |
|---|---|
| `Shard\Core.Interfaces\ISharedVariableAccess.cs`（新增） | 按 Id 读写契约 + `NullSharedVariableAccess` |
| `Shard\Core.Interfaces\IExecutionContext.cs` | 新增 `SharedVariables` 属性 |
| `Shard\Core.Interfaces\IPluginConfigContextProvider.cs` | `VariableOption` 加 `VariableId` / `IsSystem` / `CanWrite` / `Caption` |
| `Shard\Core.Interfaces\LinkPathEvent.cs` | 新增 `AllowGlobals`（默认 true） |
| `Shard\Core.Controls\LinkableValueEditor.xaml.cs` | 新增 `AllowGlobals` DP，并透传进 `LinkPathEvent` |
| `Core\Binding\GlobalVariableWriter.cs` | 实现 `ISharedVariableAccess`；抽出 `Resolve` / `WriteResolved`；系统变量专用错误文案 |
| `Core\ExecutionContext.cs` | `GlobalVariables` / `SharedVariables` 共用同一个写入器实例 |
| `Engine\FlowCompiler.cs` | 并行写冲突：递归枚举 + 收集脚本写声明（POCO/JObject 两形态）+ Id→当前名解析 |
| `Engine\FlowQueryHelper.cs` | 新增 `FilterGlobalNodes`（纯函数，断言可直调） |
| `VisionMaster\Helpers\PluginVariableOptions.cs` | 全局候选补 `VariableId`/`CanWrite`；追加系统变量（`Scope="System"`） |
| `VisionMaster\ViewModels\ProcessViewModel.cs` | 绑定请求透传 `AllowGlobals` |
| `VisionMaster\ViewModels\DialogViewModels\VariableBindingViewModel.cs` | 读 `AllowGlobals` 并用 `FilterGlobalNodes` 过滤候选 |
| `Plugins\Plugin.CSharpScript\ScriptSharedVarDef.cs`（新增） | 声明模型（Id/Name/Alias/Access/Remark/TypeName/IsSystem + 计算属性） |
| `Plugins\Plugin.CSharpScript\ScriptSharedAccessor.cs`（新增） | 别名解析 + 读/写闸门（未声明/只读/系统/存储失败的中文错误） |
| `Plugins\Plugin.CSharpScript\ScriptSharedUsageValidator.cs`（新增） | 「校验脚本」的字面量扫描 |
| `Plugins\Plugin.CSharpScript\ScriptContext.cs` | `GetGlobal` / `GetGlobal<T>` / `SetGlobal` / `TrySetGlobal` |
| `Plugins\Plugin.CSharpScript\CSharpScriptPlugin.cs` | `[StepConfig] SharedVars`、`IPluginConfigContextProvider`、候选快照/按 Id 刷新、`ValidateScript` 前置扫描、默认模板 |
| `Plugins\Plugin.CSharpScript\CSharpScriptView.xaml(.cs)` | 共享变量卡片、`AllowGlobals="False"`、候选选择回填 |
| `Plugins\Plugin.CSharpScript\CSharpApiDoc.cs` / `CSharpHighlighting.cs` | GetGlobal/SetGlobal/TrySetGlobal 补全与着色 |
| `FlowCanvasChecks\CSharpSharedVarChecks.cs`（新增）+ `Program.cs` | SV1~SV30 |
| `Help\01-quickstart.md` / `Help\02-ports-and-parameters.md` / `docs\C#脚本\C#脚本插件.md` | 共享变量用法、Q5、设计要点、断言清单 |

## 4. 验证（按阶段，每阶段带变异反证）

| 阶段 | 内容 | 断言 | 变异反证 |
|---|---|---|---|
| 1 | 只读声明（别名/未声明/类型转换/候选/持久化/取消还原） | SV1~SV17、SV31 | 未声明时回退到第一条 → SV4 红；刷新只按名字（丢 Id 优先）→ SV16 红；共享表不进快照 → SV31 红 |
| 2 | 写 + 并行闸门 | SV18~SV25 | 去掉只读/系统闸门 → SV19/20/21 红；不递归容器 → SV25 红 |
| 3 | 输入只接上游 | SV26~SV30 | 过滤忽略 `allowGlobals` → SV27 红；XAML 去掉 `AllowGlobals="False"` → SV29 红 |

全量 gate：**2896 通过 / 0 失败**（含原有 CS1~CS23 与 SV1~SV31）。
插件/引擎/断言工程均 0 错误；插件视图冒烟 10/10；内嵌手册资源 7/7。

> 构建期注意：`VisionMaster.exe` 运行时 `Modules\` 与宿主 bin 被锁，
> 插件 PostBuild 的 `Microsoft.CodeAnalysis*.dll` 拷贝会报 `MSB3073`；
> 断言验证走 `Plugins\...\bin` + `--no-dependencies` 不受影响，收尾需关闭宿主再全量构建一次。

## 5. 边界与遗留

- **旧方案兼容**：输入端口绑全局变量的老连线仍可读（`LinkedSources` 不动），
  但绑定弹窗不再提供全局节点；建议迁到「共享变量」表。
- **并行闸门的静态口径**：脚本里动态拼接的别名（`SetGlobal(name, …)`）无法静态判定，
  运行期访问器会拒绝未声明别名，但"两个分支动态写同一变量"仍可能漏检——图纸纪律同「变量赋值」。
- **输出变量改名**：下游要重选端口（既有边界，未变）。
- **共享变量表不做唯一性 UI 校验**：重复别名由「校验脚本」报错（`SV12`）。
- **系统变量 Id**：由 `SystemVariableCatalog` 确定性推导（`motion-var` 前缀 + 轴 Id），
  重启后稳定；未配置运动轴时该变量不存在，声明会按"找不到共享变量"报运行期错误。
