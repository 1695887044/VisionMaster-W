# 图像质量检查 · 宿主契约

> 分类：图像处理 ｜ 来源：插件 Plugin.ImageQuality

开发页。讲本插件依赖宿主的哪些契约、哪些行为是宿主保证的、哪些必须插件自己守。
改代码前先读完这一页。

## 1. 插件基类与执行契约

`ImageQualityPlugin : VisionPluginBase`，基类实现 `IVisionPlugin`：

| 契约 | 宿主行为 | 插件责任 |
| --- | --- | --- |
| `Execute(IExecutionContext)` | 轮首置 `Success=true`、清空 `ErrorMessage`；轮首回收上一轮输出端口的非托管值；捕获未处理异常转 `Fail` | 重写 `RunAlgorithm`，不要重写 `Execute` |
| `Fail(string)` | 置 `Success=false` + 写 `ErrorMessage` | 只用于输入/算法/配置错误；**质量不达标不要调 Fail** |
| `Dispose()` | 释放输出端口上的 `IDisposable`、退订输入端口 | 若持有额外非托管资源，重写并在末尾 `base.Dispose()` |
| `AutoDisposeRoundOutputs` | 默认 true，轮首回收输出端口旧值 | 本插件无图像输出，保持默认即可 |

`RunAlgorithm` 必须是 `public override`（基类是 `public`），否则 `CS0507`。

## 2. 端口契约

### 输入端口

```csharp
public InputPort<HImage> Image { get; } = new("Image", description: "...") { IsRequired = true };
public InputPort<HRegion> Roi { get; } = new("Roi", description: "...") { IsRequired = false };
```

- 业务取值一律 `ActualValue`（强类型、链接优先）；**不要用 `Value`**（那是手动值）。
- 端口名不填时基类按属性名自动补；本插件显式写了名字，避免反射顺序变化影响连线键。
- `IsRequired=true` 的端口未连接时，编译/运行前检查会拦；`RunAlgorithm` 里仍要判空，
  因为试运行/单步执行可能绕过编译期检查。

### 输出端口

```csharp
public OutputPort<double> FocusRaw { get; } = new("FocusRaw", description: "...");
```

- 写值用 `TypedValue` 或 `port.Set(value)`；本插件直接写 `TypedValue`。
- 输出端口的值就是真实值，没有“手动值/链接值”两层。
- 本插件不输出 `HImage`，所以没有“交给端口的图不能提前 Dispose”的问题。

## 3. `[StepConfig]` 与持久化

| 契约 | 说明 |
| --- | --- |
| `[StepConfig]` | 标记参与 `InputValues` 持久化与实例灌值的配置属性，不暴露为端口、不可变量链接 |
| `[DefaultValue(...)]` | 新步骤的默认值；只支持单参数编译期常量 |
| `[Display(Name=..., Description=...)]` | 通用属性面板显示中文名/说明；没标会回退属性名 |
| `partial` 属性 + `OnXxxChanging` | 生成器补后备字段、`SetProperty`、夹取钩子；类必须是 `partial` |
| `ApplyConfigValues(IStepConfigData)` | 宿主在编译/试运行前把 `InputValues` 灌进端口与配置属性 |
| `OnConfirm(IStepConfigData)` | 配置确认时把端口与配置属性写回 `InputValues`；基类已实现，本插件不重写 |

本插件的 14 个配置项全部是标量/布尔，走自动属性 + 夹取钩子，没有手写 setter、没有集合、没有嵌套对象。
新增配置项时：

1. 加 `[StepConfig, DefaultValue(...), Display(...)]`；
2. 数值项加 `partial void OnXxxChanging` 夹取；
3. 更新 03 页参数表；
4. 在 `ImageQualityChecks` 的端口/配置契约断言里补一行。

## 4. 工具库注册

```csharp
[Display(Name = "图像质量检查", GroupName = "图像处理", Description = "...", ShortName = "\uf0e4")]
[ParallelSafe]
public partial class ImageQualityPlugin : VisionPluginBase
```

- `PluginService` 扫描 `Modules\Plugin*.dll`，要求类型 `public`、非抽象、实现 `IVisionPlugin`、
  带 `[Display]`、有公开无参构造；`GroupName` 写入 `ToolItemModel.Category`。
- `ShortName` 是工具库图标（FontAwesome 码点）；本插件用 `\uf0e4`（仪表盘）。
- 改 `Display.Name` 会影响工具库显示，不影响已配流程（流程存的是类型名）。

## 5. `[ParallelSafe]`

本插件只调 `HOperatorSet`，`ImageQualityService` 是 `internal static` 且**没有 static 字段**，
每个方法只用自己的局部 `HObject`。因此可以标 `[ParallelSafe]`，允许放进并行分支。

违反这个前提（加 static 缓冲、共享 `HImage`、复用可变 `HTuple`）会让并行分支偶发串图/错数；
这类 bug 概率性出现、极难复现。改 service 时先看 12 页 D4。

## 6. 工程与投递

| 项 | 要求 |
| --- | --- |
| 工程位置 | `Plugins\Plugin.ImageQuality\`（红线①；放别处 `Directory.Build.targets` 不投递） |
| csproj | `net9.0-windows`、`UseWPF`、四引用（Core.Halcon / Core.Controls / Core.Interfaces / UI）、HALCON HintPath、`<EmbeddedResource Include="Help\**\*" />` |
| `Modules\` | 构建后由 `Plugins\Directory.Build.targets` 自动复制；宿主只扫 `Modules\Plugin*.dll` |
| `VisionMaster.sln` | **必须登记**（R62）；不登记则改代码后 `Modules\` 仍是旧 DLL 且无报错 |
| `HelpManualCheck.Tiers` | 必须登记 `["ImageQualityPlugin"] = ManualTier.M`（HM16 未登记直接红） |
| 手册 | 8 页（5 用户 + 3 开发），开发页合计 ≥150 行；首行一级标题、`NN-` 前缀、无 BOM UTF-8 |
| 断言 | `FlowCanvasChecks\ImageQualityChecks.cs` + `ProjectReference` + `Program.cs` 一行 `Run()` |

## 7. 全局变量：不写

本插件**不**调用 `context.GlobalVariables.TryWrite`，也不持有变量注册表。
需要把指标写到全局时，由下游「变量赋值」步骤 `Scope=Global` 从本插件输出端口映射。

这是仓内新口径：插件只输出端口/运行时变量，全局映射走下游。
理由：插件拿不到完整变量表，直接写全局会绕过类型守门与权限；下游映射可审计、可复用。

## 8. `IsUsable` 与 `Success` 的宿主语义

| 端口 | 由谁写 | 下游怎么用 |
| --- | --- | --- |
| `Success` | 基类/插件 | 流程引擎据此标记节点状态；false 表示本步骤没算成 |
| `IsUsable` | 本插件 | 只是普通 bool 输出；接「逻辑判断」/分支，不参与引擎状态 |

本插件刻意让“质量不达标”只影响 `IsUsable`，不影响 `Success`。
如果改成 `Fail`，流程会在质量告警时中断，且现场无法区分“图不好”和“插件坏”。

## 9. HALCON 可用性

- HALCON 原生库/许可是机器级依赖；启动自检 `HalconCheck` 只 Warning 不阻断。
- 本插件在 `RunAlgorithm` 里第一次调 HALCON 时才会真正加载原生库；不可用会抛异常，
  由基类转成 `Fail`（`ErrorMessage` 带异常类型与消息）。
- 断言宿主用 `ProbeHalconUsable()` 先造一张 4×4 图做前置；不可用则整段判红并说明原因（R38），
  不许静默跳过。

## 10. 不做自定义视图

本插件不实现 `IPluginCustomViewProvider`。宿主 `StepParameterDialog` 在拿不到自定义视图时，
会 `Initialize(stepData)` 后构造 `AutoPortConfigView`，逐行渲染输入端口 + `[StepConfig]` 配置项。
14 个参数都是 bool/double/int，自动面板足够；没有画布点选、没有复杂集合。

如果将来要加“在图上圈 ROI 预览”之类的交互，再实现 `GetConfigView`，
并遵守 R19-R22（GridView 样式）、R30-R32（资源作用域）、R141（可复用 UI 下沉）。
