# 2026-10-20 C#脚本：改名/删行的连线收尾 + 智能提示插入可编译模板

> **后续变更（2026-10-20）**：本文里"改全局变量要两步（输出行 + 下游「变量赋值」）"的说法，
> 已被声明式「共享变量」表取代（读写在脚本内完成、按 VariableId 寻址、参与并行写冲突检查），
> 见 `2026-10-20-CSharpScript共享变量声明.md`。两条路并存：纯数据流场景仍可用输出行 + 变量赋值。

## 1. 背景（真机事故）

同一张截图里两条互相独立的故障：

1. **编辑器编译错误**：`第 16 行 第 5 列: The name 'index' does not exist in the current context`。
   用户把接口变量表里的 `index` 当 C# 变量直接写（`if (index > 800)`），"怎么写都不对"。
2. **流程编译错误**：`[MainTask] [连线错误] 'C#脚本_0' 没有名为 'iN' 的输入端口，这条连线不会生效。`
   批量编译"成功 2 个、失败 1 个"——整条 MainTask 跑不起来。

自动保存文件（`VisionMaster\bin\Debug\net9.0-windows\Autosave\解决方案-20260829224154-20261020-170358.vms`）
给出了第 2 条的实据：`InputVars` 只有一行 `index`，而 `LinkedSources` 同时挂着 `"iN"` 与 `"index"`——
`iN` 是改名前的旧键。

## 2. 根因

| # | 根因 | 证据 |
|---|---|---|
| 1 | 接口变量只是**端口名/字典键**，脚本引擎只注入 `Context`（`ScriptContext` 作 globals），不会生成 C# 局部变量 | `CSharpScriptEngine.cs:60`、`ScriptContext.cs:54` |
| 2 | 智能提示把变量名**裸插**进代码（`VarCompletionData.Complete` → `_v.Name`），高亮又把裸名染成变量色——补全/高亮都在教用户写错 | `CSharpCompletionData.cs`（改前）、`CSharpHighlighting.cs:169`（改前） |
| 3 | 改名只发 `PropertyChanged("Name")`，插件只处理 `Type`：端口不重建（行内 🔗/✕ 静默失效）、`LinkedSources` 键不迁移 | `CSharpScriptPlugin.cs:249-254`（改前） |
| 4 | 删行只重建端口，不删 `LinkedSources` 键 → 孤儿键；`FlowCompiler` 对未知键是**报错** | `FlowCompiler.cs:1209` |
| 5 | 孤儿键在 UI 上够不到：行内 ✕ 只删当前端口名；全局变量连线在画布只折成节点角标 | `LinkableValueEditor.xaml.cs:392`、`FlowCanvasViewModel.cs:1476-1481` |
| 6 | 手填值在开窗时丢失：`base.ApplyConfigValues` 先灌值、动态端口之后才建，`Inputs` 里找不到键 | `CSharpScriptPlugin.ApplyConfigValues`（改前） |
| 7 | 编辑期直接改活模型（`InputVars` 可能就是 `StepData.InputValues["InputVars"]` 的同一实例；连线键更是直接写模型），而对话框"取消"只丢弃插件实例 | `StepParameterDialog.cs:60`、`PluginConfigShellViewModel.cs:435` |

## 3. 改动清单

| 文件 | 改动 |
|---|---|
| `Shard\Core.Interfaces\IStepLinkInventory.cs`（新增） | 能力接口 `IEnumerable<string> GetLinkedPortNames()`；不给 `IStepConfigData` 加成员（它有 8 个实现者，且只有本插件需要枚举连线键） |
| `Core\Models\ProcessStep\StepModel.cs` | 实现 `IStepLinkInventory`：`GetLinkedPortNames() => LinkedSources.Keys.ToList()`（快照，调用方边遍历边 RemoveLink） |
| `Plugins\Plugin.CSharpScript\ScriptVarDef.cs` | `Name` setter 规范化 null→"" 并新增 `NameChanged(old,new)` 事件；新增 `ScriptVarTypeExtensions.ToClrTypeName()`（Int→int、HXld→HXLDCont…） |
| `CSharpScriptPlugin.cs` | ① `_linkKeys` 锚点 + `_varIsInput`；② 改名迁移连线/手填值（目标名被占用时不迁移、不前进锚点）；③ 删行清键（重名共享键不删）；④ 空名字保留锚点；⑤ `OnConfirm` 清历史孤儿键（有空名字时跳过）；⑥ `ApplyPortValues`：端口建好后补灌手填值；⑦ 端口重建按名字保值；⑧ 打开配置拍快照、`OnCancel` 原样还原；⑨ 集合 `Reset`/`Move` 分支处理 |
| `CSharpIntelliSense.cs` | `VarInfo` 增 `IsOutput`/`ClrType`；`BuildVarInsert` / `DescribeVarUsage` / `BuildVarInsertWithCaret`；悬浮卡加"正确用法"代码行 |
| `CSharpCompletionData.cs` | 补全改为插入 `Context.GetInput<T>("名")` / `Context.SetOutput("名", )`，光标落在逗号后 |
| `CSharpHighlighting.cs` | 去掉"接口变量名紫色高亮"（它们不是 C# 标识符）；`KnownTypes` 补 `HXLDCont` |
| `CSharpScriptView.xaml` | 两处 `Name` 绑定改 `UpdateSourceTrigger=LostFocus`（改名只提交一次）；输出区帮助文案改正 |
| `CSharpScriptView.xaml.cs` | `CollectVarInfos` 带 `ClrType`/`IsOutput`，类型显示走 `StepConfigOption.ForEnum`（中文）；去掉变量名高亮相关的监听 |
| `Help\01-quickstart.md`、`Help\02-ports-and-parameters.md`、`docs\C#脚本\C#脚本插件.md` | 改正"输出端口连全局变量即为改全局"（正解=输出行 + 下游「变量赋值」）；类型表改中文显示名；补改名/删行/取消语义 |
| `FlowCanvasChecks\CSharpScriptChecks.cs`（新增）+ `Program.cs` | CS1~CS23，注册在 `VariableAssignmentCheck.Run()` 之后 |
| `VisionMaster\ViewModels\DialogViewModels\PluginConfigShellViewModel.cs`（顺带修） | 取消顺序：有试运行时先 `_execCts.Cancel()` 并等收尾，`CompleteExecute` 里再 `OnCancel()`。旧顺序先 `OnCancel` 再叫停，会在后台脚本读写 `Inputs/Outputs` 时重建端口字典（`Dictionary` 并发读写未定义） |
| `Core\Models\ProcessStep\StepModel.cs`（顺带修） | `ContainerSummary` / `ContainerSummaryVisibility` 标 `[RuntimeState]`：它们是派生展示值，`RaiseContainerSummary` 每次发两条通知；不标会让 `SetLink`/`RemoveLink` 的版本号从 +1 变 +3（E17④ 断言当场抓到，每次绑定写回白重编译两轮） |

## 4. 关键设计

### 4.1 名字三合一：改名要一次动三样

`变量表.Name` = 动态端口名 = `StepData.LinkedSources` 的键。改名时：

1. **先迁移键**（`GetLink(oldKey)` → `SetLink(newName)` → `RemoveLink(oldKey)`，手填值同理）；
2. **再重建端口**（`RebuildDynamicInputs` 按新名字建，行内 🔗/✕ 立刻指向新端口）；
3. **补灌值**（`ApplyPortValues` 从迁移后的 `InputValues` 读回新端口）。

**锚点 `_linkKeys`** 解决"清空输入框→重打"：名字变成空串时不动键，下一个**没被占用**的名字再把键迁过去。
逐字符改名时中间名与另一行重名（`a → abc` 而表里已有 `ab`）时**不迁移、不前进锚点**——
旧实现会在 `ab` 那一步删掉 `a` 的连线，打到 `abc` 就永久丢了。

### 4.2 删行与历史孤儿

删行时按锚点删键（同名的另一行还在用则跳过，共享键不删）。历史版本留下的孤儿键
（`iN` 这类）在 `OnConfirm` 里按 `IStepLinkInventory` 枚举清理；**存在空名字行时整批跳过**——
"清空名字"可能只是准备重打，不能当成已删除。

### 4.3 取消要还原

打开配置时对 `InputVars`/`OutputVars`（深拷贝）、`LinkedSources`（逐键取 `LinkReference`）、
`InputValues`（浅拷，变量表两个键单独处理）拍快照；`OnCancel` 先写回变量表（走 `SetInputValue`，
与确认同一条落盘路径），再清空/重挂连线，最后恢复其余 `InputValues` 并补灌端口值。
快照不在 `OnConfirm` 清——"先试运行再取消"要回到打开前，而不是试运行写过的状态。

### 4.4 手填值往返

`ApplyConfigValues` 里 `base` 先灌值、动态端口后建，旧实现的手填值必然丢。现在的口径：

- **开窗/取消还原**：两次 `RebuildDynamicInputs/Outputs` 之后 `ApplyPortValues(stepData)`，从 `InputValues` 灌；
- **改类型 / 增删行**：只重建，**不** `ApplyPortValues`——`RebuildDynamicInputs` 已按名字保留各端口当前值，
  再灌一遍会用"上次确认的旧值"覆盖用户刚填的新值（CS21/CS22）；
- **改名**：旧名字对不上，先把旧端口的 `Value` 取出来、重建后写到新端口（CS23）；
  只有"从空名字恢复/首次建立"没有旧端口可搬时才回落到 `InputValues`。

### 4.5 空名字与取消边界

- 空名字行在 `OnConfirm` 里先由 `RestoreEmptyNames` 用锚点名字补回：既保住绑定，也避免留下
  "没有端口"的孤儿键（否则流程编译必失败）。真的想删变量请用行尾 ✕。
- 取消：快照在 `Initialize` 拍，`OnCancel` 还原；**试运行中点取消**必须先叫停后台执行、
  等收尾后再还原（`PluginConfigShellViewModel` 的取消顺序已改），否则还原时的端口重建会与
  后台脚本读写 `Inputs/Outputs` 撞车。

## 5. 验证

| 项 | 命令 | 结果 |
|---|---|---|
| 插件构建 | `dotnet build Plugins\Plugin.CSharpScript\Plugin.CSharpScript.csproj --no-incremental` | 0 错误 |
| 宿主构建（含取消顺序修复） | `dotnet build VisionMaster\VisionMaster.csproj` | 0 错误 |
| 断言宿主 | `dotnet run --project FlowCanvasChecks` | **2863 通过 / 0 失败**（CS1~CS23 全绿） |
| 版本号回归 | E17④ `SetLink/RemoveLink 递增 Version` | 首次全量重建后红（`Version 1 → 4`）：`ContainerSummary*` 两条通知各推一次版本；标 `[RuntimeState]` 后恢复 `1 → 2` / `2 → 3` |
| 变异反证（第一批） | 补全改回裸名 + 改名迁移直接 return | 恰好 7 条红（CS1/2/3/5/10/11/12），还原后全绿 |
| 变异反证（第二批） | 去掉手填值回灌 / 去掉取消还原 / 去掉中间重名保护 / 光标位恒 0 | 恰好 7 条红（CS6/7/13/14/15/16/19），还原后全绿 |
| 变异反证（第三批） | 去掉 `RestoreEmptyNames` / 改类型后补回 `ApplyPortValues` | 恰好 2 条红（CS12/CS21），还原后全绿 |
| 视图冒烟 | `dotnet build UIThemeSmokeTest --no-dependencies` + `dotnet run --no-build -- --plugin-views` | CSharpScript 10/10 PASS（含 `OnConfirm 可执行`） |
| 内嵌手册 | `Modules\Plugin.CSharpScript.dll` 字节扫描 | `.Help.` 资源 **7 个 = Help 下 .md 数**；新文案「变量改名 / 删行会自动收尾」在 DLL 里 |
| Release 产物 | `dotnet build VisionMaster\VisionMaster.csproj -c Release` | 0 错误；Release `Core.Interfaces.dll` / `VM.Core.dll` 均含 `IStepLinkInventory` |

> 构建期出现一次 `Plugin.DlClassify` 的 `xcopy ... 已退出，代码为 4`（与本次改动无关的既有
> PostBuild 拷贝），改用 `--no-dependencies` 构建插件/断言宿主后正常；同一轮里
> `[S4] HttpImageServer.Start()` 曾因端口占用红过一次，复跑即绿（环境性）。

## 6. 边界与遗留

- **输出变量改名**：下游引用的是端口名，插件拿不到别的步骤的模型，改不动它——编译器会报
  "上游不存在输出"，需要下游重选。
- **历史孤儿键不自动清**：只在"打开配置并确认"时清。不在 `ApplyConfigValues` 里清，是因为
  编译线程也会走那条路径，编译期写模型会引入线程安全问题。
- **输出端口重建不 Dispose 旧 `HImage`**：`ClearDynamicOutputs` 的既有行为，本轮未改。
- **试运行期间仍可编辑变量表**：取消路径已改成"先叫停再还原"，但运行中直接改名/增删行仍会重建
  端口字典（未加运行锁）；极端情况下与后台脚本读写端口存在窗口。
- **标题栏关闭（Alt+F4）不走 `OnCancel`**：编辑期的连线迁移/清理会留在活模型上；请用「取消」按钮。
  这是 `PluginConfigShellViewModel` 的既有行为，本轮未扩大改动面。

## 7. 回滚

- 回滚 `Plugin.CSharpScript` 五个源文件 + `CSharpScriptView.xaml`；删除 `CSharpScriptChecks.cs`
  与 `Program.cs` 的注册行；回滚 `IStepLinkInventory.cs` 与 `StepModel` 的两处实现；
  帮助页/专题文档按 diff 回退。
- 重建插件（`--no-incremental`）刷新 `Modules\Plugin.CSharpScript.dll`；`StepModel`/接口回滚后
  需重建宿主与 `FlowCanvasChecks`。
