# 图像脚本 · 开发章节：宿主契约与配置模型

> 分类：图像处理 ｜ 来源：插件 Plugin.ImageScript ｜ 读者：开发

## 一 白拿的基类行为（不要自己重写）

`VisionPluginBase` 给了这些，写脚本类插件时**别去覆盖**：

| 基类给的 | 对本插件意味着 |
| --- | --- |
| 反射收集 `InputPort` 与 `OutputPort` 属性 | 静态端口只有基类的 `Success`、`ErrorMessage`；业务端口全是动态的 |
| `Execute` 收尾把 `Success` 预置为 true | `RunAlgorithm` 只写失败分支，成功路径不用手动置 |
| `Fail(msg)` | 统一失败出口：置 `Success=false` 并写 `ErrorMessage` |
| `ApplyConfigValues` | 把 `.vms` 里的键灌进 `[StepConfig]` 属性，本插件 `override` 后先 `base` 再重建端口 |
| `AddDynamicInput`、`RemoveDynamicInput`、`AddDynamicOutput`、`ClearDynamicOutputs` | 动态端口重建的四个动作 |
| `PublishPreview(...)` | 效果图与随图信息的唯一出口（R36：窗口由宿主管） |
| `AutoDisposeRoundOutputs` | 每轮结束释放上一轮图形输出，本插件靠它避免句柄堆积 |
| `OnConfirm`、`SnapshotConfigValue` | 配置界面向流程提交时走这里，不要自己找时机落盘 |

## 二 实现的两个接口

- `IPluginCustomViewProvider.GetConfigView(IStepConfigData)`：返回 `ImageScriptView`，进去先 `Initialize(stepData)`。
  宿主传进来的 `IStepConfigData` 就是 `.vms` 的读写面，插件不直接碰文件。
- `IDynamicOutputProvider.RebuildDynamicOutputs()`：宿主在编译与配置期调它，用来把动态输出端口"预告"给连线系统。
  本插件的实现同时写 `StepData.OutputPortDefinitions`（`if (StepData != null)` 保护），
  每条 `DynamicPortInfo` 的 `DataTypeName` 存的是 **`AssemblyQualifiedName`**——宿主靠它把上游类型对上，
  写成 `Name` 或 `FullName` 会让连线面板认为类型未知。

没有实现 `IPluginConfigContextProvider`；上下文取用的是 `IExecutionContext`（`Logger`、`LocalVariables`、`CancellationToken`）。
本插件**没有 `[ParallelSafe]`**，所以调度器按串行执行它——显示后端用静态窗口态，这是刻意的（见 12 页 B 组）。

## 三 `[StepConfig]` 与源生成器

四个键由 `[StepConfig]` 标出，属性体由 `Core.PluginGenerators` 源生成（`partial` 声明，不手写背垫）：

| 键 | 内容 | 生成器口径 |
| --- | --- | --- |
| `Procedures` | 全部过程的 XML 字符串（`EProcedure` 编组，常量 `ProceduresConfigKey`） | 字符串，直接落盘 |
| `SelectedProcedure` | 本轮运行的过程名 | 字符串 |
| `InputVars` | `ObservableCollection<ScriptVarDef>` | 可序列化集合，内嵌进 `.vms` |
| `OutputVars` | 同上 | 同上 |

`ScriptVarDef` 带 `[Serializable]`，字段是 `Name`、`Type`（默认 `ScriptVarType.Double`）、`ManualValue`、`Remark`、`DisplayWindow`，
`IsIconic` 是派生属性（`HObject`、`HImage`、`HRegion`、`HXld` 四类）。
集合属性要在换实例时**重挂** PropertyChanged（`HookVars` / 卸载旧项），否则用户在表格里改类型插件收不到通知。

## 四 类型到端口 CLR 类型的映射（`CreateInputPort` / `CreateOutputPort`）

| `ScriptVarType` | 输入端口 | 默认值 |
| --- | --- | --- |
| `Int` | `InputPort<int>` | 0 |
| `Double` | `InputPort<double>` | 0.0 |
| `String` | `InputPort<string>` | 空串 |
| `HTuple` | `InputPort<HTuple>` | null |
| `HObject` | `InputPort<HObject>` | null |
| `HImage` | `InputPort<HImage>` | null |
| `HRegion` | `InputPort<HRegion>` | null |
| `HXld` | `InputPort<HXLDCont>` | null |
| 兜底分支 | `InputPort<object>` | null |

`ScriptVarType` 只有这 8 个成员，**没有 `Bool`**（C# 脚本那套有）。
两个插件的枚举是各自文件里各写一份的同名类型，别以为它们共用。
类型下拉显示的是英文成员名，这是 AGENTS.md R40 记在账上的显示欠账，**尚未实现中文**。

## 五 端口重建的通知链

`OnVarDefPropertyChanged` 里判 `nameof(ScriptVarDef.Type)` → `RebuildDynamicInputs` 加 `RebuildDynamicOutputs` →
`NotifyPortsRebuilt()` 让宿主刷新节点上的端口条 → `PortsVersion` 递增，供校验与编译期判断"端口是否已就绪"。
改名不触发重建：名字只是端口 `Name`，类型变了才需要换掉整个端口对象。

## 六 打包与装载

- `.csproj` 里已有 `.csproj` 里那条把 `Help` 目录全部 Markdown 收进 DLL 的 `EmbeddedResource` 通配项；手册是本插件的用户页加开发页共九页，**扁平放在 `Help\` 一层**。
- 单插件程序集不建子目录：宿主 `NarrowToPlugin` 的规则是"本类型子目录有页就只收子目录"，一旦建 `Help\ImageScriptPlugin\`，扁平用户页会被静默挤掉（HM2、HM12b 判红）。
- 页序由文件名 `NN-` 前缀决定（宿主按资源名 Ordinal 排）；开发页判据是文件名里的 `dev-` 段，宿主全量加载、按开关裁显示。
- 每页第一行内容必须是 `# 一级标题`，页眉 `> 分类：图像处理 ｜ 来源：插件 Plugin.ImageScript`，开发页追加 `｜ 读者：开发`。
- 脚本资源目录随插件投递：`ScriptAssets\images`、`ScriptAssets\ocr`、`ScriptAssets\procedures`（`.hdvp`）。
  过程目录存在才会被 `AddProcedurePath`，所以缺目录不是错误。
