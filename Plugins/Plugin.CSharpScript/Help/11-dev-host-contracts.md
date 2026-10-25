# C#脚本 · 开发章节：宿主契约与配置模型

> 分类：逻辑控制 ｜ 来源：插件 Plugin.CSharpScript ｜ 读者：开发

## 一 类声明

```
[Display(Name = "C#脚本", GroupName = "逻辑控制",
         Description = "用完整 C# 语法编写脚本，可取模块参数、读写变量、输出日志、显示图像、控制流程", ShortName = "\uf0ac")]
partial class CSharpScriptPlugin : VisionPluginBase, IPluginCustomViewProvider, IDynamicOutputProvider, IPluginConfigContextProvider
```

类上**没有 `[ParallelSafe]`**（对照：YOLO、深度学习分类、OCR 三个条目都带），所以调度器按串行执行本节点。
真要并行，前提是 `CSharpScriptEngine` 的 `CallCache` 与 `OptionsLock` 已覆盖全部静态态，
且脚本里不写宿主全局——后者根本没法静态保证，所以这一条别急着开。

## 二 配置项

| 键 | 写法 | 默认值 |
| --- | --- | --- |
| `ScriptText` | `[StepConfig, DefaultValue(DefaultScript)] partial string` | `public const string DefaultScript` 那段模板（取 `Context.GetInput<HImage>("Image")` 到 `Context.SetOutput("Result", "ok");`） |
| `InputVars` | `[StepConfig] ObservableCollection<ScriptVarDef>` | 空集合 |
| `OutputVars` | 同上 | 空集合 |
| `SharedVars` | `[StepConfig] ObservableCollection<ScriptSharedVarDef>` | 空集合 |

`partial void OnScriptTextChanging(ref string value) => value ??= "";` 挡的是"清空脚本"时宿主传 null——
不挡就会在缓存指纹那一步 `NullReferenceException`，症状是节点红但看不到原因。
两个集合属性在换实例时要重挂/卸载项级 PropertyChanged（`HookVars` 一族），否则表格编辑不触发端口重建。

## 三 类型口径：本插件的 `ScriptVarType` 与图像脚本的**不是同一个枚举**

`Plugins\Plugin.CSharpScript\ScriptVarDef.cs` 里定义：`Object`、`Int`、`Double`、**`Bool`**、`String`、`HTuple`、`HImage`、`HRegion`、`HXld`、`HObject`（10 个成员，默认 `Object`）。
图像脚本那份在 `Plugins\Plugin.ImageScript\ScriptVarDef.cs`，只有 8 个成员、默认 `Double`、**没有 `Bool`**。
两份同名不同类型，脚本与手册里都别说成"同一个枚举"。

端口映射（`CreateInputPort` 与 `CreateOutputPort`）：

| 声明类型 | CLR 类型 | 输入默认值 |
| --- | --- | --- |
| `Int` | `int` | 0 |
| `Double` | `double` | 0.0 |
| `Bool` | `bool` | false |
| `String` | `string` | 空串 |
| `HTuple`、`HObject`、`HImage`、`HRegion` | 同名 HALCON 类型 | null |
| `HXld` | `HXLDCont` | null |
| 其余（含 `Object`） | `object` | null |

类型下拉显示的是**中文**（`ScriptVarType` 成员带 `[Display(Name)]`，视图经 `StepConfigOption.ForEnum` 上屏）；
值仍是成员名（`Int`、`HImage`…），改显示名不影响已保存的 `.vms`。
注意 `HXld` 只是**枚举成员名**，脚本里写它会直接 CS0246——所以编辑器补全/高亮给的类型名是 CLR 那份
（`HXLDCont`），两边口径以本表为准。

## 四 动态端口与接口声明

- `GetConfigView(IStepConfigData)` → `Initialize(stepData)` → `new CSharpScriptView{DataContext=this}`。
- `override ApplyConfigValues`：**先 `base` 灌 `[StepConfig]`，再 `RebuildDynamicInputs(); RebuildDynamicOutputs();`**。
  顺序不能换——宿主要按名字把连线接回来，端口不存在时接不上（表现为"工程打开后线全丢"）。
- `IDynamicOutputProvider.RebuildDynamicOutputs()` 让宿主在编译期知道有哪些动态输出；
  输出声明写进 `StepData.OutputPortDefinitions`（`DynamicPortInfo.DataTypeName` 是 `AssemblyQualifiedName`）。
- `PortsVersion` 与 `ValidationResult` 供校验与编译期判断端口是否已就绪；变量类型变更后要一起推进去。
- **重建即释放**：`RebuildDynamicInputs` 里被摘掉的旧输入端口必须 `Dispose()`，否则它挂在上游
  `OutputPort.ValueChanged` 上的订阅解不掉，每次改名/改类型都留下"旧端口 + 它缓存的那张图"。
  `RebuildDynamicOutputs` 里被摘掉的旧输出端口只**断引用**（值是可 `IDisposable` 的类型才置 null）：
  那张图可能仍是上游的直通值或借来的预览图，所有权不归本插件，绝不能在重建时 `Dispose` 它。

## 五 脚本能看见的宿主 API（`ScriptContext` 成员全表）

| 成员 | 行为 |
| --- | --- |
| `Context` | 返回自身，模板里 `Context.XXX` 的 `Context` 就来自这里 |
| `GetInput(string)` | 端口不存在返回 null（宿主记一条 Warn），不抛 |
| `GetInput<T>(string)` | 需要时用 `Convert.ChangeType`，转不了抛 `InvalidCastException` |
| `SetOutput(string, object)` | 端口不存在**忽略并记一条 Warn**（同一轮同名一次）；类型不符抛（被壳包成"写输出端口[X]失败：…"） |
| `CancellationToken` / `ThrowIfCancellationRequested()` | 本次执行的取消令牌；长循环主动检查，取消按已取消（Skipped）处理 |
| `GetVar` / `GetVar<T>` / `SetVar` | 读写 `IExecutionContext.LocalVariables`（流程变量）；`GetVar<T>` 转换失败**返回默认值而不是抛** |
| `Vars` | `LocalVariables` 的**只读视图**（`ReadOnlyDictionary`）：`ContainsKey`/枚举/取值照旧，写它抛 `NotSupportedException`——要写走 `SetVar` |
| `Info` / `Warn` / `Error` / `Success` | 写 `Logger`，统一加 `[Host] ` 前缀 |
| `ShowImage` 四个重载 | 纯图 / 加标注 / 加标题与 `(string Label, object Value)[]` 随图信息 / 三者全给 |
| `NewMarks()`、`MarkLine(row1,col1,row2,col2,color="green")`、`MarkText(row,col,text,color="green")` | 造标注列表，落 `MeasureAnnotation`（`Type`、`Points`、`Color`） |
| `Fail(string message)`、`Failed`、`FailMessage` | 脚本内主动失败的正规通道 |
| `Host` | `internal`，由壳赋成实例名；脚本侧不该改 |

`ToInfoRows` 会跳过标签为空的条目——所以随图信息里"少一条"通常是标签没写，不是数据丢了。

## 六 落盘与打包

`.vms` 有 `ScriptText`、`InputVars`、`OutputVars`、`SharedVars` 四个键，`ScriptVarDef` 带 `[Serializable]` 内嵌存储。
手册四页用户册加三页开发章节（M 档，开发合计 ≥150 行），扁平在 `Help\` 下，
由工程已有的 `.csproj` 里那条把 `Help` 目录全部 Markdown 收进 DLL 的 `EmbeddedResource` 通配项 打进 `Plugin.CSharpScript.dll`；不建子目录。
`Microsoft.CodeAnalysis.CSharp.Scripting` 及其依赖要随宿主投递——缺它时症状是脚本节点连编译入口都进不去。
