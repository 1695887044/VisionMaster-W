# 深度学习异常检测 · 宿主契约

> 分类：图像处理 ｜ 来源：插件 Plugin.DlAnomaly ｜ 读者：改这个插件或做宿主集成的人

本页把"宿主（Core.Interfaces / VisionMaster）给插件什么、插件必须还什么"写清楚。越权或误用都表现为运行期异常。

## 一、基类 `VisionPluginBase` 的成员与纪律

| 成员 | 类型 | 插件侧用法 |
| --- | --- | --- |
| `RunAlgorithm(IExecutionContext)` | **`public`**（不是 protected） | 重写必须是 `public override`；写成 `protected` 报 `CS0507`（降低访问性） |
| `Success` | `OutputPort<bool>`，初值 `false` | **只在 `Execute()` 开头被置 `true`** |
| `ErrorMessage` | `OutputPort<string>` | 由 `Fail(msg)` 写 |
| `Fail(string)` | `protected void` | 设 `Success=false` + `ErrorMessage=msg`，**不中断执行**，调用方需自己 `return` |
| `DisposePreviousRoundOutputs()` | `protected` | `Execute()` 里自动调用；释放输出端口上的 `IDisposable`，**跳过与上游链接值同引用的对象**（透传保护） |
| `AutoDisposeRoundOutputs` | `protected virtual bool`，默认 `true` | 关掉就要自己管输出对象的释放 |
| `RethrowOnException` | `protected virtual bool`，默认 `false` | 默认吞异常并转成 `Fail`，异常原文进日志 |
| `Initialize(IStepConfigData)` | `public override` | 本插件重写后追加 `RefreshModelState()` |
| `GetConfigProperties()` | `public` | 返回 `[StepConfig]` 描述符，供通用属性面板使用 |

`Execute()` 的顺序（必须记住）：

```
Success.Value = true; ErrorMessage.Value = "";
DisposePreviousRoundOutputs();
try { RunAlgorithm(context); }
catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested) { throw; }  // 取消不是失败
catch (Exception ex) { Fail($"{ex.GetType().Name}: {ex.Message}"); context.Logger.Error(ex); }
return Success.Value is true;
```

**这条顺序有一个直接后果**：断言里如果要验证"正常返回时 `Success == true`"，必须调 `plugin.Execute(context)`，**不能**直接调 `plugin.RunAlgorithm(context)`——后者跳过了预置，`Success` 仍是初始的 `false`，会让所有正常用例假红，且 `ErrorMessage` 为空（因为没走 `Fail`）。

## 二、端口契约

| 规则 | 说明 |
| --- | --- |
| `InputPort<T>.Value` | 类型是 **`object?`**，是"手动值"的弱类型入口，内部做跨类型转换。**断言/代码里赋值用它** |
| `InputPort<T>.ActualValue` / `GetTypedValue()` | **读侧唯一正确入口**。上游连线有值取上游，否则取手动值 |
| `OutputPort<T>.Value` | `T` `null!` 表示"本轮无数据"；引用类型的 `null` 与值类型的默认值不等价（如 `double` 是 `0.0` 而不是 `null`） |
| 端口发现 | 基类反射发现 `InputPort<>` / `OutputPort<>` 字段，**顺序即声明顺序** |
| `IsRequired` | 只影响校验提示；非必填端口在执行期仍要自己判空并给出明确文案 |

## 三、`IExecutionContext`

| 成员 | 用途 |
| --- | --- |
| `Logger.Info / Warn / Error` | 落 `ILogService`。**可见性靠它**，不要用弹泡（3 秒就没了） |
| `CancellationToken` | 协作取消。`[ParallelSafe]` 的插件必须在起手（及长循环内）检查 |

本插件的做法：`RunAlgorithm` 首行查一次取消；推理是单次阻塞调用，不再细分。

## 四、`[ParallelSafe]` 的依赖条件

加了 `[ParallelSafe]` 就意味着可以被放进并行分组。这要求：

- `AnomalySession` 的静态字典有 `CacheLock` 保护，引用计数在锁内改；
- `AnomalyEngine` 每次请求都新建（无共享可变状态）；
- 会话对象本身是线程安全的（ONNX Runtime `InferenceSession.Run` 可并发，但本插件把 `IntraOpNumThreads=1` 设为单线程以求确定性）。

**新增静态可变字段前先问：并行两条分支同时进来会不会打架。**

## 五、`.vms` 键清单

| 键 | 类型 | 默认 | 备注 |
| --- | --- | --- | --- |
| `ModelPath` | string | `""` | 路径，不是模型内容 |
| `ResizeMode` | 成员名 | `"Letterbox"` | 改成员名 = 旧方案读不回来 |
| `InputSize` | int | `0` | |
| `HeatmapMax` | double | `0` | |
| `HeatmapInterpolation` | 成员名 | `"Nearest"` | |
| `DisplayViewIndex` | int | `1` | |
| `LinkedSources.Image` | 连线 | 无 | 上游步骤 ID + 端口名，宿主写 |

**方案文件不含任何凭据，也不含模型本体。**

## 六、`PublishPreview` 签名

`Core.Events.PluginPreviewExtensions` 上的扩展方法，**只有五个重载**：

```csharp
PublishPreview(this IVisionPlugin plugin, HImage image, int viewIndex)
PublishPreview(this IVisionPlugin plugin, HImage image, int viewIndex, IEnumerable<MeasureAnnotation> annotations)
PublishPreview(this IVisionPlugin plugin, HImage image, string title, params /* 视重载而定 */)
PublishPreview(this IVisionPlugin plugin, HImage image, int viewIndex, string? title,
               IEnumerable<ImageInfoRow>? info, IEnumerable<MeasureAnnotation>? annotations)
```

- **没有"params 数组"重载**：要带摘要就显式传 `IEnumerable<ImageInfoRow>`，且第 5 参必须给（传 `null`）。
- `viewIndex` 就是**窗口号 1~9**，不要 +1。
- 调用必须包 `try/catch`：显示层异常不许拖垮流程。本插件 catch 后 `Warn`。

## 七、工程引用与原生库投递

| 项 | 内容 |
| --- | --- |
| 目标框架 | `net9.0-windows` + `UseWPF` + `ImplicitUsings` + `Nullable` |
| 项目引用 | `Services\Core.Halcon`、`Shard\Core.Interfaces`、`UI\Controls` |
| HALCON 托管程序集 | **必须显式** `<Reference Include="halcondotnet"><HintPath>..\..\DLL\Halcon\halcondotnet.dll</HintPath></Reference>`——经 `ProjectReference` 传递的 `Core.Halcon` **不带 HintPath**，本工程直接 `using HalconDotNet` 时解析不到 `HImage`/`HObject`，报一串 `CS0246` |
| ONNX 托管程序集 | `Microsoft.ML.OnnxRuntime` + `System.Numerics.Tensors`，`HintPath` 指 `DLL\OnnxRuntime\`，`<Private>true</Private>` |
| ONNX 原生库 | `onnxruntime.dll` / `onnxruntime_providers_shared.dll` 两个 `<None>`，`CopyToOutputDirectory=PreserveNewest` |
| 投宿主 | `Target DeployOnnxRuntimeToHost`：`xcopy DLL\OnnxRuntime → $(HostBinDir) /Y /Q /I`，`ContinueOnError=true` |
| 手册 | `<EmbeddedResource Include="Help\**\*" />` |

**为什么原生库要单独投递**：Windows 加载原生 DLL 时先看 exe 目录与 PATH，**不会递归搜 `Modules\` 子目录**。只拷托管 DLL 过去，运行期是 `DllNotFoundException` 或 `BadImageFormatException`，且报的路径往往让人以为托管 DLL 有问题。

## 八、插件清单（`[Display]`）与分组

```csharp
[Display(Name = "深度学习异常检测", GroupName = "图像处理",
         Description = "用 ONNX 异常检测模型只学良品，输出异常分数与可选热力图；形状不认识的模型直接拒绝，不猜",
         ShortName = "\\uf1e2")]
```

- `GroupName` 决定工具箱里的分组，必须与既有分组名一致（本插件归「图像处理」）。
- `ShortName` 是字形码点（`\uf1e2`），不是 emoji。
- `[ParallelSafe]` 与 `[Display]` 同在类上。

## 九、手册打包核验

```
ls Plugins/Plugin.DlAnomaly/Help/*.md | wc -l                                  # 磁盘页数
tr '\0' '\n' < Plugins/Plugin.DlAnomaly/bin/Debug/net9.0-windows/Plugin.DlAnomaly.dll \
  | grep -a "\.Help\." | sort -u                                               # 程序集内嵌资源名
```

两者条数必须相等。**改过任何一页 .md 内容都要重建**（产物即脏），增量构建会静默漏打包，且不会有任何报错。

## 十、断言宿主的接入点

- `FlowCanvasChecks/AnomalyChecks.cs`：本插件的断言（四段：契约嗅探 / 会话守卫 / 插件壳与端口 / 模型态）。
- `FlowCanvasChecks/Program.cs`：注册 `AnomalyChecks.Run();`。
- `FlowCanvasChecks/FlowCanvasChecks.csproj`：加 `<ProjectReference Include="..\Plugins\Plugin.DlAnomaly\Plugin.DlAnomaly.csproj" />`。
- `FlowCanvasChecks/HelpManualCheck.cs` 的 `Tiers`：加 `["DlAnomalyPlugin"] = ManualTier.L`（HM16 要求一一对应，不登记就红）。
- `VisionMaster.sln`：登记工程（1 条 Project + 12 条 Config + 1 条 NestedProjects = 共 14 处 GUID）。
