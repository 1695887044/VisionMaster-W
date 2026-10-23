# 图像差分 · 宿主契约

> 分类：图像处理 ｜ 来源：插件 Plugin.ImageDiff

开发页。列清本插件对宿主的每一处依赖：继承了谁的什么、用了谁的扩展方法、
在 `.vms` 里占哪些键、被引擎以什么方式调度。**这些契约改了就编译不过或静默跑错**，
所以每一条都标了"依赖点在哪里"。

## 1. 基类：`VisionPluginBase`

`ImageDiffPlugin : VisionPluginBase`（`Shard/Core.Interfaces/VisionPluginBase.cs`）。

| 成员 | 本插件怎么用 | 契约要点 |
| --- | --- | --- |
| `Execute(IExecutionContext)` | 不重写，由基类实现 | 基类负责：先把 `Success=true`、`ErrorMessage=""` → `DisposePreviousRoundOutputs()` → 调 `RunAlgorithm` → 捕获异常 → 返回 `Success.Value is true` |
| `RunAlgorithm(IExecutionContext)` | **重写（必须是 `public override`）** | 基类里是 `public`，写成 `protected` 会 `CS0507`（不能缩小访问范围） |
| `Fail(string)` | 每个失败分支都调 | 置 `Success=false` + `ErrorMessage=message`；**不会中断执行**，调用方要自己 `return` |
| `DisposePreviousRoundOutputs()` | 不调用 | 每轮自动跑，释放输出端口上所有 `IDisposable` 且**跳过与上游链接值同引用的对象**（透传保护） |
| `AutoDisposeRoundOutputs` | 不重写（默认 `true`） | 本插件的 `DiffImage`/`OverlayImage` 靠它释放，所以本轮不能自己 Dispose |
| `RethrowOnException` | 不重写（默认 `false`） | 异常被基类吞成 `Fail("类型名: 消息")` 并记日志；调试时可临时改 true 看完整栈 |
| `InstanceName` | 日志用 | 由宿主注入，形如「图像差分_1」 |
| `Inputs` / `Outputs` | 不直接碰 | 惰性发现：`EnsurePortsDiscovered()` 按公共属性扫，声明顺序即面板顺序 |

**异常策略**：算法层内部一律抛异常（不返回入参图、不返回 null），由基类统一转成 `Fail`。
这样所有失败路径都有中文 `ErrorMessage`（service 里的异常消息本身就是中文说明），
不需要在 service 里写日志。

## 2. 端口类型：`InputPort<T>` / `OutputPort<T>`

| 依赖 | 事实 | 依赖点 |
| --- | --- | --- |
| `InputPort<T>.Value` | 类型是 **`object?`**（"手动值"的弱类型入口） | 用它做 `is HImage` 判型能编过，但会引出 `HImage.IsInitialized()` 与 `IModuleCatalogCommonExtensions.IsInitialized<T>` 的推断冲突（`CS0411`） |
| `InputPort<T>.ActualValue` | 类型 **`T`**，取值一律走这里 | `InputPort.cs:225` 注释明确"业务代码取上游值一律用此属性" |
| `OutputPort<T>.Value` | 类型 `T`，可写 | 直接赋领域对象即可；赋同一个对象给两个端口时，第二轮回释放两次（本插件不会这么做） |
| `IsRequired` | 本插件 `MaskRegion` 设 `false` | 必填端口为空时基类会把描述写进 `ErrorMessage`，`RunAlgorithm` 里只要判 null |

## 3. 上屏：`PluginPreviewExtensions.PublishPreview`

命名空间 `Core.Events`（`Shard/Core.Controls/Events/PluginPreviewExtensions.cs`）。本插件用的是五参重载：

```csharp
this.PublishPreview(HImage image, int viewIndex, string? title,
                    IEnumerable<ImageInfoRow>? info,
                    IEnumerable<MeasureAnnotation>? annotations);
```

| 契约 | 说明 |
| --- | --- |
| **没有 params 重载** | 想传若干条信息必须自己包 `new List<ImageInfoRow>{...}`；第 4、5 个参数都得显式给 |
| `viewIndex` 就是窗口号 1~9 | 不要 +1；0 = 不上屏（本插件在调用前判 `DisplayViewIndex > 0`） |
| `ImageInfoRow` | `new ImageInfoRow(string label, string value)`；它在 `Core.Halcon.Models`（`Services/Core.Halcon/Models/ImageInfoRow.cs`） |
| 显示层异常不影响流程 | 本插件用 `try { ... } catch { }` 包住，与 `GeometryRelation`/`ResultDisplay`/`ImageScript` 一致 |

**不要试图在图上画文字**：`HOperatorSet.SetColor` / `HOperatorSet.DispText` 的第一个参数是
**窗口句柄**（`HTuple`），不是 `HObject`。在图像对象上写文字必须走离屏烘焙，
而本仓只允许 `Services/Core.HALCON/Helpers/AnnotationBaker.cs` 做（R36；唯一豁免是
`Plugin.ImageScript/HDevDisplayBackend.cs`）。标题与指标交给上屏层就够了。

## 4. `.vms` 方案键

本步骤在方案文件里占一个节点，内容是 **12 个参数值 + 端口快照**。参数键名＝属性名：

```
Algorithm           DiffAlgorithm     默认 Absolute
Polarity            DiffPolarity      默认 Both
Aggregation         DiffAggregation   默认 Components
SizeMismatch        SizeMismatchPolicy 默认 Fail
Threshold           double            默认 30
MinArea             double            默认 20
MaxTotalArea        double            默认 0（不判）
MaxComponentArea    double            默认 0（不判）
MaxValueLimit       double            默认 0（不判）
AmbientFloor        double            默认 8
SignedDisplay       double            默认 128
DisplayViewIndex    int               默认 1
```

**枚举存成员名**（`"Absolute"`），不是数字也不是中文。所以：
- 改 `[Display]` 中文名不影响已配流程；
- **删/改枚举成员名会打断存量方案**（反序列化回落默认值，不报错）。
  真要改名，走"加新成员 + 旧成员保留并标 `[Obsolete]`"的路径。

**图不入库**：`Reference`/`Test`/`MaskRegion` 存的是上游端口引用（连线关系），
不是图像数据。所以"换了基准图却没生效"要去上游那一步找，本步骤里没有图。

## 5. 执行上下文：`IExecutionContext`

| 成员 | 本插件怎么用 |
| --- | --- |
| `Logger?.Warn(...)` | `SizeMismatch` 走 `ScaleTest`/`CropCommon` 时 Warn 一句，写清"已按哪个策略处理、代价是什么" |
| `Logger?.Error(...)` | 不直接用（基类 `Execute` 的 catch 会记） |
| `CancellationToken` | 不直接用；基类的 `catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)` 会把取消当取消而非失败抛出 |

日志口径：`$"{InstanceName} {mismatch} → 已按 SizeMismatch=缩放被测图 处理（双线性插值，会引入插值误差）"`。
**改了策略就一定要留一行 Warn**——这是"静默降级"的唯一可见痕迹。

## 6. 调度与并发：`[ParallelSafe]`

`[ParallelSafe]` 标在类上，含义是本步骤可以在并行分支里并发执行。本插件满足这个前提：

- 无静态可变状态（`ImageDiffService` 是 `static` 但**无字段**，所有数据都在局部变量里）；
- 不持有跨轮句柄（模型/窗口/文件句柄一个都没有）；
- 不读写共享文件。

**改代码时注意**：往 service 里加任何 `static` 字段（缓存、计数器、复用缓冲区）都会破坏这条前提。
真要缓存，用 `[ThreadStatic]` 或放到实例上。

## 7. 插件清单与工具箱

| 宿主机制 | 本插件的值 |
| --- | --- |
| `[Display(Name)]` | 「图像差分」——工具箱与步骤树显示的名字 |
| `[Display(GroupName)]` | 「图像处理」——工具箱分组 |
| `[Display(Description)]` | 「把被测图与基准图逐像元相减，按阈值找出差异区域并输出差异面积/片数/最大差异值」 |
| `[Display(ShortName)]` | `\uf24e`（FontAwesome 私有区码点，工具箱图标） |
| 插件发现 | 宿主扫 `Modules\Plugin*.dll` 反射带 `[Display(GroupName=...)]` 的类；本工程必须**登记在 `VisionMaster.sln`**（否则改代码后 `Modules\` 里仍是旧 DLL，且无任何报错） |

## 8. 工程引用与依赖

```xml
ProjectReference
  ..\..\Services\Core.Halcon\Core.Halcon.csproj    HImage / HOperatorSet / ImageInfoRow
  ..\..\Shard\Core.Controls\Core.Controls.csproj   PublishPreview 扩展
  ..\..\Shard\Core.Interfaces\Core.Interfaces.csproj VisionPluginBase / 端口 / IExecutionContext
  ..\..\UI\Controls\UI.csproj                      （与其他插件保持一致，本插件未直接用）
Reference
  ..\..\DLL\Halcon\halcondotnet.dll                HintPath 直引，非 NuGet
```

**插件只引用 `Core.Interfaces` 这一层契约**，不引任何 `Services\Core.*` 的实现（`Core.Halcon` 例外——
它是 HALCON 封装的公共层，所有视觉插件都引）。不要为了用某个工具类去引 `Core.Database` 之类。

## 9. 手册打包

`<EmbeddedResource Include="Help\**\*" />` 写在**本工程的 csproj**里（没有全局 props 规则覆盖）。
资源名形如 `Plugin.ImageDiff.Help.01-quickstart.md`（目录段用点分隔）。

核验（红线③ 家族，改过 `.md` 内容就算脏）：

```bash
ls Plugins/Plugin.ImageDiff/Help/*.md | wc -l
tr '\0' '\n' < Plugins/Plugin.ImageDiff/bin/Debug/net9.0-windows/Plugin.ImageDiff.dll \
  | grep -a "\.Help\." | sort -u
```

两个数必须相等。**别用 `grep -o "Plugin.ImageDiff.Help.[0-9A-Za-z.-]*"` 数**——
中文文件名会在正则的 ASCII 字符类处截断，得到偏小的数（会误判成"漏打包"）。
