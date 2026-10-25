# C#脚本插件 GStack 审查 P1-2：变量重名拦截与孤儿连线归属（2026-10-24）

> 前置：`docs/交接报告-2026-10-24-CSharp脚本插件GStack审查.md` 的 **P1-2**（0 P0 / 2 P1 里的最后一条）。
> 批次 A（取消语义 / 缺端口告警 / 手册）已于同日落地，见
> `docs/code-changes/2026-10-24-CSharp脚本插件GStack审查批次A（取消语义与缺端口告警）.md`。

## 一、缺陷回顾（改前）

两行输入变量同名时，端口按"先到先得"只建一个；改名撞上已在用的名字时，
`MigrateInputKey` 刻意不迁移连线键、也不前进锚点；点「确定」后
`RemoveOrphanLinks` 只认"当前变量表里出现过的名字"，于是仍挂在锚点上的那条连线
被当孤儿**静默删除**。结果是：界面不报错、流程继续跑，`GetInput("名字")` 拿到另一行的值。

输出侧重名的表现不同：`AddDynamicOutput` 见同名静默不建，但 `OutputPortDefinitions`
快照仍写两份 → 下游按名字取端口时行为取决于谁先被读。

审查证据（改前）：`CSharpScriptPlugin.cs` 的 `RebuildDynamicInputs` 重名 `continue`、
`MigrateInputKey` 撞名 `return`、`RemoveOrphanLinks` 只按当前名字建有效集、
`VisionPluginBase.AddDynamicOutput` 同名忽略。置信度 8/10。

## 二、改动

### 1. 确认前校验：重名/空名直接拒绝（`CSharpScriptPlugin.cs`）

`OnConfirm` 在任何写入之前执行：

```csharp
RestoreEmptyNames();
string nameError = GetVariableNamesError();
if (nameError != null) throw new InvalidOperationException(nameError);
base.OnConfirm(stepData);
RemoveOrphanLinks(stepData);
```

- 规则：**同一张表内**名字非空且唯一；输入表与输出表是两个命名空间（脚本分别走
  `GetInput` / `SetOutput`），**允许同名**。
- 比较口径：`StringComparer.Ordinal`（与端口字典一致，大小写敏感）；不做 Trim（不静默改名）。
- 文案带冲突行号（1 基）：
  - `输入变量第 2 行「b」与第 1 行重名：端口只会建一个，另一行的连线会在确认时丢失，请改成不同的名字`
  - `输入变量第 1 行名字为空：请填写名字、改回原名字或删掉该行`
  - 输出表同款，前缀换成「输出变量」。
- 拒绝通道用**宿主既有机制**：`PluginConfigShellViewModel.Confirm()` 与 `ExecutePlugin()`
  都 `try { _pluginView?.OnConfirm(...) } catch (Exception ex) { ... }`，失败时窗口不关、
  状态栏显示「保存失败 - {ex.Message}」。插件不需要新增接口。
- 顺序保证：校验在 `base.OnConfirm` 之前 → 失败时 `InputValues` 一个键都没写；
  `RestoreEmptyNames` 在校验前 → "清空名字后误点确认"仍按锚点恢复（原 CS12 行为不变）。

### 2. 孤儿清理认锚点归属（`RemoveOrphanLinks`）

有效键集合除了"当前变量表里的名字"，再并入**活输入行的锚点**：

```csharp
foreach (var kvp in _linkKeys)
{
    if (kvp.Key == null || string.IsNullOrWhiteSpace(kvp.Value)) continue;
    if (!_varIsInput.TryGetValue(kvp.Key, out bool isInput) || !isInput) continue;
    if (InputVars == null || !InputVars.Contains(kvp.Key)) continue;
    valid.Add(kvp.Value);
}
```

- 作用：撞名改名期间连线键仍挂在旧锚点上，它仍有归属行，**不是孤儿**；
  删掉它是静默丢绑定，留下它最多让流程编译时报「没有名为 X 的输入端口」（响的）。
- 行被删除时 `UnhookVarDef` 会移出 `_linkKeys`，所以历史孤儿键（CS10 的真机形状）仍会被清掉。
- 用户把名字改成不冲突的值后，`MigrateInputKey` 照原逻辑把键从锚点迁到新名字（CS29 钉住）。

### 3. 输出快照与端口 1:1（`RebuildDynamicOutputs`）

```csharp
var seenNames = new HashSet<string>(StringComparer.Ordinal);
...
if (!seenNames.Add(v.Name)) continue;   // 不建第二个同名端口，也不写第二份快照
```

- 编辑期就保证 `OutputPortDefinitions` 与实际 `Outputs` 一一对应，避免编译期看到
  "两行同名输出但只有一个端口"。
- 重名本身的用户可见拦截仍由第 1 条负责（`OnConfirm` 拒绝），这里只是不让脏状态扩散。

### 4. 「校验脚本」先查变量表（`ValidateScript`）

顺序改为：变量表名 → 共享变量用法 → 强制重编译。文案与确认时**同一个函数**产出，
用户不用点两次才发现问题。

### 5. 手册同步

| 文件 | 改动 |
| --- | --- |
| `Help/02` | `SetOutput` 缺端口从"静默忽略"改为"忽略并 Warn"（批次 A 漏网）；新增"同一张表内唯一且非空"段；`Name` 行补"必须唯一且非空"；`OutputVars` 补"同名行只保留一份" |
| `Help/03` | 新增 Q/A：两行同名会怎样（拒绝保存 + 行号；改成不冲突后连线自动迁移） |
| `Help/12` | 第 3 条守门断言名补 CS24/CS25；新增不变量 6b（重名拒绝、连线不丢、输出 1:1）；"改后必做动作"新增重名场景的复验步骤 |

## 三、守门断言（`FlowCanvasChecks/CSharpScriptChecks.cs` CS27-CS33）

| 断言 | 判据 | 变异反证 |
| --- | --- | --- |
| CS27 | 两行输入都连线后把 A 改名成 B → `OnConfirm` 抛异常，消息含"第 1 行/第 2 行/重名" | 去掉 `GetVariableNamesError` 调用 → 确认通过，CS27 红 |
| CS28 | 拒绝时 A、B 两条原连线都还在 | 去掉锚点归属 → A 的连线被当孤儿删，CS28 红 |
| CS29 | 改成不冲突的名字后确认通过，连线从锚点 a 迁到 c，a 不残留 | 让 `MigrateInputKey` 在撞名时前进锚点 → 迁移丢失，CS29 红 |
| CS30 | 两行同名输出：`Outputs` 只有 1 个 `res`，`OutputPortDefinitions` 也只有 1 份 | 去掉 `seenNames` → 快照 2 份，CS30 红 |
| CS31 | 输出重名时 `OnConfirm` 抛异常且消息含"输出变量/重名" | 只校验输入表 → CS31 红 |
| CS32 | 空名字（无锚点）确认被拒，消息含"第 1 行/名字为空" | 去掉空名校验 → 确认通过，CS32 红 |
| CS33 | `ValidateScript()` 返回同一条空名错误（先于编译） | 把变量表校验挪到编译后 → 返回编译错误，CS33 红 |

## 四、验证证据（2026-10-24）

| 项 | 命令 | 结果 |
| --- | --- | --- |
| 插件构建 | `dotnet build Plugins/Plugin.CSharpScript/Plugin.CSharpScript.csproj -c Debug -m:1 -nodeReuse:false -p:BuildProjectReferences=false` | 0 警告 / 0 错误，exit 0 |
| 断言宿主构建 | `dotnet build FlowCanvasChecks/FlowCanvasChecks.csproj -c Debug -m:1 -nodeReuse:false -p:BuildProjectReferences=false` | 0 错误，exit 0 |
| 断言首跑 | `FlowCanvasChecks.exe` | **3539 通过 / 2 失败**（失败为 `[M]` 运动段既有 flaky，见下） |
| 断言复跑 | 同一二进制再跑 | **3541 通过 / 0 失败**（含 CS27-CS33、HM0-HM17） |
| 帮助探针 | 重建后 `HelpProbe.exe` | 74 PASS / 0 FAIL，`全部通过`，exit 0 |
| 产物闸 | `tools\audit_artifacts.ps1` | `RESULT: OK`（39 工程；DLL 新于源码、Help 页齐） |
| 根目录闸 | `tools\audit_root.ps1` | `ROOT LAYOUT OK: 8 files / 43 dirs allowed` |
| 打包核对 | `tr '\0' '\n' < Modules/Plugin.CSharpScript.dll \| grep -a "\.Help\."` | 7 条资源名 = 磁盘 7 页 |
| DLL | `Modules/Plugin.CSharpScript.dll` | 175616 字节，2026-10-24 01:22:50 |

### 为什么用 `-p:BuildProjectReferences=false`

工作树里 `UI/Controls/Themes/Dialog/Dialog.Input.xaml` 在 01:20 被**另一个会话**改成
含 `--` 的 XML 注释（`（基线 --r-sm）`），XML 注释不允许出现 `--`，导致 UI 工程
`MC3000` 编译失败，从而挡住所有依赖 UI 的工程。该文件不属于本次任务，**未改动**；
本次验证复用了 01:10 前成功构建的 UI 产物，只重建 C#脚本插件与两个断言宿主。

正常全量 gate 需等该 XAML 修好后重跑：`tools\run_gates.ps1`。

### 既有 flaky（与本次无关）

首跑 2 条红均在 `[M]` 运动控制段：

```
[×失败] 前置：假设备命令失败后进入安全停机  →  状态=Online 说明=断言用假运动卡
[×失败] ★底层仍报警时清报警不放行：保持安全停机 + 给出原因  →  清报警=True 状态=Online 原因=
```

同一二进制复跑即全绿；批次 A 当时也出现过同两条，已记在 `docs/code-changes/2026-10-24-...批次A...md`。

## 五、影响面与边界

| 文件 | 性质 |
| --- | --- |
| `Plugins/Plugin.CSharpScript/CSharpScriptPlugin.cs` | 确认前校验、孤儿清理、输出快照、校验顺序 |
| `Plugins/Plugin.CSharpScript/Help/02,03,12` | 文案与不变量台账 |
| `FlowCanvasChecks/CSharpScriptChecks.cs` | CS27-CS33 |

- **旧工程兼容**：已保存的重名配置仍能打开、能运行（不在 `Initialize` 拒绝），
  只是下次点「确定」/试运行会要求先改名；不会破坏 `.vms`。
- **输入与输出同名**保持合法；**大小写不同**视为不同名字（与端口字典一致）。
- 名字首尾空格未做 Trim（避免静默改名）；`Help/02` 仍建议"字母开头、不含空格"。
- 输出改名后，**下游**步骤存的旧端口名仍需重选——这条不在本插件能改的范围（审查报告已注明）。
- 未做：批次 B（白名单定位、预览范围）与批次 C 其余项（端口 Dispose、负缓存、共享变量别名漂移、
  正则校验改语法树）。

## 六、踩坑

1. **`OnConfirm` 是唯一可用的拒绝点**：基类签名返回 `void`，没有 `bool`/校验结果通道；
   宿主把异常当"保存失败"并保持窗口打开，正好符合"拒绝确认"的语义。抛之前不能有任何写入。
2. **校验必须放在 `RestoreEmptyNames` 之后**：清空名字的行要先按锚点恢复，否则"清空后误点确认"
   会被新加的空名校验挡住，破坏原有"保绑定"行为（CS12）。
3. **孤儿清理不能只看当前名字**：撞名改名期间键挂在旧锚点上是**有意设计**（CS6/CS7），
   清理把锚点算有效键，才不会把"还没迁走"误判成"没人要"。
4. **输出快照是编译期输入**：只修 `AddDynamicOutput` 不够，快照里的同名项会让下游看到幻影端口，
   必须两处同口径。
5. **无关 XAML 能把整个仓的构建挡住**：`--` 在 XML 注释里非法（MC3000）；用
   `-p:BuildProjectReferences=false` 只验证本域，别顺手改别人的在途文件。
