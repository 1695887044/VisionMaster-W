# 深度学习分割 · 宿主契约

> 分类：图像处理 ｜ 来源：插件 Plugin.DlSegment ｜ 读者：接这个插件的人

本篇是"插件与宿主之间到底约定了什么"的清单。改插件、写断言、或者排查"为什么我的值没生效"，先看这里。

## 一、基类成员（`VisionPluginBase`）

| 成员 | 契约 |
|---|---|
| `RunAlgorithm(IExecutionContext)` | **基类里是 `public`**，重写不能降级为 `protected`（会报 CS0507） |
| `Execute(IExecutionContext)` | 入口。顺序固定：`Success.Value = true` → `ErrorMessage.Value = ""` → `DisposePreviousRoundOutputs()` → `RunAlgorithm`；`OperationCanceledException` 且已请求取消时**不算失败**，直接抛 |
| `Fail(string message)` | 置 `Success = false` + `ErrorMessage = message`，**不中断执行**（后续代码照跑） |
| `DisposePreviousRoundOutputs()` | 释放输出端口上所有 `IDisposable`（HImage 等）并置空，**跳过与上游同引用的对象**（透传保护） |
| `RethrowOnException` | 默认 `false`；异常被 `Fail` 吃掉只留日志 |
| `AutoDisposeRoundOutputs` | 默认 `true` |
| `Success` / `ErrorMessage` | 输出端口，**会被算进 `Outputs`** |

**这一条最容易踩**：断言里直接调 `RunAlgorithm()` 而不是 `Execute()`，`Success` 永远是初始 `false`，所有正常用例全假红。DlAnomaly 阶段就是 13 处全改成 `plugin.Execute(NewContext())` 才转绿。

## 二、端口取值

| 位置 | 类型 | 正确读法 |
|---|---|---|
| `InputPort<T>.Value` | `object?` | 弱类型入口，交给 UI 绑定时用 |
| `InputPort<T>.ActualValue` | `T` | **业务代码一律用这个** |
| `InputPort<T>.GetTypedValue()` | `T` | 与 `ActualValue` 等价 |
| `OutputPort<T>.Value` | `object` | 弱类型入口 |
| `OutputPort<T>.TypedValue` | `T` | **强类型读侧入口** |
| `OutputPort<T>` 构造 | 两种 | `(name, description)` 或 `(name, description, initialValue)`——**初值在第三位** |

`OutputPort<double>.Value` 的默认值是 `0.0` 不是 `null`：断言里判"没算"不能用 `== null`，得看有没有别的标志端口或哨兵值（本插件用 `DominantClassIndex = -1`）。

## 三、`IExecutionContext`

| 成员 | 用途 |
|---|---|
| `Logger` | `Info` / `Warn` / `Error`；日志进宿主日志与断言宿主的捕获列表 |
| `CancellationToken` | 每轮开始时检查一次：`if (context.CancellationToken.IsCancellationRequested) { Fail("执行已取消"); return; }` |

本插件不在推理中途轮询取消（一次推理是原子的，轮询也中断不了），只在入口检查。

## 四、`[ParallelSafe]`

本插件标了 `[ParallelSafe]`，前提是：

- `SegmentEngine` / `SegmentSession` 里**没有可变静态状态**（进程级缓存用 `lock` 保护，只增删字典条目）；
- 每个节点实例自己的 `_session` 字段是实例级的；
- HALCON 调用不加共享窗口、不写全局变量。

破坏任一条就会让并行分支互相踩，而症状是"偶尔一次结果错"，几乎无法复现。

## 五、`.vms` 里存什么

| 键 | 值 |
|---|---|
| `ModelPath` | 字符串路径（**不存模型**） |
| `ResizeMode` / `Interpolation` / `RegionMode` | 枚举**成员名** |
| `InputSize` / `ClassIndex` / `DisplayViewIndex` | 整数 |
| 端口连线 | `Image` 的上游步骤 ID + 端口名 |

规则：**方案里不出现模型二进制、不出现阈值**。模型是客户资产；阈值是工艺参数，归下游判断节点。

## 六、`PublishPreview` 五种重载

```csharp
this.PublishPreview(image, viewIndex);                                  // 只要图
this.PublishPreview(image, viewIndex, annotations);                      // 图 + 标注
this.PublishPreview(image, viewIndex, title, info, annotations);         // 五参：标题 + 信息行 + 标注
```

- **没有 `params` 数组版**：五参版的第 4 参必须是 `IEnumerable<ImageInfoRow>`，第 5 参必须显式给（可传 `null`）。
- `viewIndex` **就是窗口号 1~9**，不要 +1，也不要 0（0 表示不发布）。
- 调用必须包 `try/catch`：显示层异常不许拖垮流程。本插件写 `catch { }` 并留注释，与 `GeometryRelation` / `ResultDisplay` 一致。
- `ImageInfoRow` 在 `Core.Halcon.Models`，`PublishPreview` 扩展方法在 `Core.Events` —— **两个 using 都要有**，缺一个是 CS0246。

## 七、工程引用清单

| 引用 | 为什么 |
|---|---|
| `Services\Core.Halcon` | HImage / HObject / ImageReadOnly |
| `Shard\Core.Interfaces` | `VisionPluginBase` / 端口 / `IExecutionContext` |
| `UI\Controls` | `LinkableValueEditor` / `DialogInput` 等样式与控件 |
| `halcondotnet`（**显式 Reference + HintPath**） | 本工程直接 `using HalconDotNet`，传递引用不带 HintPath，编译期解析不到类型 → CS0246 |
| `Microsoft.ML.OnnxRuntime` + `System.Numerics.Tensors`（HintPath 指 `DLL\OnnxRuntime\`） | ONNX 推理与 `DenseTensor<float>` |
| `onnxruntime.dll` + `onnxruntime_providers_shared.dll`（`None` + PreserveNewest） | 原生库随输出走 |
| `DeployOnnxRuntimeToHost` 目标 | 把原生库 `xcopy` 到**宿主 exe 目录**——Windows 不递归搜 `Modules\` |

## 八、`[Display]` 四件套

类上必须有：`Name`（中文名）、`GroupName`（分组，本插件用「图像处理」）、`Description`（一句话说清边界，**含"不猜"这类反例说明**）、`ShortName`（字体图标码点）。缺 `GroupName` 的类不计入手册统计口径，也不进算子工具箱。

## 九、手册打包

```bash
ls Plugins/Plugin.DlSegment/Help/*.md | wc -l                                  # 磁盘页数
tr '\0' '\n' < .../Plugin.DlSegment.dll | grep -a "Plugin\.DlSegment\.Help\." | sort -u | wc -l
```

两个数必须相等。改了任何一页 `.md` 内容，产物就算脏，收尾必须 `--no-incremental` 重建。

## 十、断言宿主的五个接入点

新插件要进常规 gate，五处都要动：

| # | 文件 | 改什么 |
|---|---|---|
| 1 | `FlowCanvasChecks/SegmentChecks.cs` | 新建，四段：契约嗅探 / 会话守卫 / 插件壳 / 模型态（照 `AnomalyChecks.cs`） |
| 2 | `FlowCanvasChecks/Program.cs` | 注册 `SegmentChecks.Run();` |
| 3 | `FlowCanvasChecks/FlowCanvasChecks.csproj` | `ProjectReference` 到本工程 + `Microsoft.ML.OnnxRuntime` 显式 Reference（断言要直接构造形状元数据） |
| 4 | `FlowCanvasChecks/HelpManualCheck.cs` | `Tiers` 里登记 `["DlSegmentPlugin"] = ManualTier.M`（或 L） |
| 5 | `VisionMaster.sln` | 1 条 Project + 12 条 Config + 1 条 NestedProjects，`grep -c GUID` 应为 14 |

**第 4 条最容易忘**：HM16 的判据是"档位表与插件类一一对应"，新增插件类不登记就红。

## 十一、常见契约误区

| 误区 | 事实 |
|---|---|
| `RunAlgorithm` 可以写 `protected` | 基类是 `public`，降级报 CS0507 |
| `InputPort.Value` 就是 T | 是 `object?`，业务读用 `ActualValue` |
| 输出图用完要自己 Dispose | 不要，基类下一轮统一释放；提前释放会让端口留野指针 |
| `Outputs` 只有自己声明的 | 还含 `Success`/`ErrorMessage` |
| 空着端口就不该建节点 | 允许空着建，执行时明确失败更好排查 |
| `[StepConfig]` 可以随便改成员名 | 旧 `.vms` 会读不回来，等于静默丢配置 |
| 枚举可以写中文进盘 | 存成员名，中文只用于显示 |
