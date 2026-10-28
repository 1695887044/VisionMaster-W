# 图像对齐 · 宿主契约

> 分类：图像处理 ｜ 来源：插件 Plugin.ImageAlign

开发页。讲本插件依赖宿主的哪些契约、哪些行为是宿主保证的、哪些必须插件自己守。

## 1. 插件基类与执行契约

`ImageAlignPlugin : VisionPluginBase`：

| 契约 | 宿主行为 | 插件责任 |
| --- | --- | --- |
| `Execute` | 轮首置 `Success=true`、清空 `ErrorMessage`；轮首回收输出端口的非托管值；异常转 `Fail` | 重写 `RunAlgorithm`，不重写 `Execute` |
| `Fail` | 置 `Success=false` + 写 `ErrorMessage` | 只用于输入/配置/算法错误；**对齐质量不达标不要调 Fail** |
| `Dispose` | 释放输出端口上的 `IDisposable`、退订输入 | 本插件无额外非托管资源 |
| `AutoDisposeRoundOutputs` | 默认 true；轮首回收 `AlignedImage`/`ValidRegion` 旧值 | 保持默认 |

`RunAlgorithm` 必须是 `public override`。

## 2. 端口契约

### 输入

```csharp
public InputPort<HImage> Image { get; } = new("Image", ...) { IsRequired = true };
public InputPort<HImage> ReferenceImage { get; } = new("ReferenceImage", ...) { IsRequired = false };
public InputPort<HTuple> RefPoints { get; } = new("RefPoints", ...) { IsRequired = false };
public InputPort<HTuple> UpstreamHomMat2D { get; } = new("HomMat2D", ...) { IsRequired = false };
public InputPort<double> PoseRow { get; } = new("PoseRow", 0.0, ...) { IsRequired = false };
```

- 业务取值一律 `ActualValue`；不要用 `Value`（手动值）。
- 输入属性名 `UpstreamHomMat2D` 与输出属性 `HomMat2D` 不同名（C# 不允许同类两个同名属性）；
  **端口名仍是 `HomMat2D`**，连线键不受影响。
- `HTuple` 输入端口在通用面板里只读；手动点对用 `RefPointsText`/`CurPointsText` 文本参数，
  端口只用于上游自动产点。

### 输出

```csharp
public OutputPort<HImage> AlignedImage { get; } = new("AlignedImage", ...);
public OutputPort<HRegion> ValidRegion { get; } = new("ValidRegion", ...);
public OutputPort<HTuple> HomMat2D { get; } = new("HomMat2D", ...);
public OutputPort<HTuple> InverseHomMat2D { get; } = new("InverseHomMat2D", ...);
```

- `AlignedImage` 是 `HImage`：交给端口后**本轮不要再 Dispose**，由基类轮首回收。
- `ValidRegion` 是 `HRegion`：同上。
- `HomMat2D`/`InverseHomMat2D` 是 9 元 `HTuple`，托管对象，不需要 Dispose。

## 3. `[StepConfig]` 与持久化

- 12 个配置项全部是标量/枚举/字符串，走自动属性 + 夹取钩子；
- `[DefaultValue]` 初始化默认值，`[Display(Name, Description)]` 给通用面板中文；
- `ApplyConfigValues` 在编译/试运行前灌值；`OnConfirm` 由基类写回 `InputValues`；
- 文本点对（`RefPointsText` 等）随参数存进 `.vms`，不存点对象。

新增配置项时：加 `[StepConfig, DefaultValue, Display]` → 数值加夹取钩子 → 更新 03 页 → 补断言。

## 4. 工具库注册

```csharp
[Display(Name = "图像对齐", GroupName = "图像处理", Description = "...", ShortName = "\uf0c4")]
[ParallelSafe]
public partial class ImageAlignPlugin : VisionPluginBase
```

- `PluginService` 扫描 `Modules\Plugin*.dll`，要求 `public`/非抽象/`IVisionPlugin`/`[Display]`/公开无参构造；
- `GroupName` 进工具库分类；`ShortName` 是图标码点。

## 5. `[ParallelSafe]`

本插件只调 `HOperatorSet`，`ImageAlignService` 是 `internal static` 且**没有 static 字段**，
每个方法只用自己的局部 `HObject`。因此可标 `[ParallelSafe]`。

违反前提（加 static 缓冲、共享 `HImage`）会让并行分支偶发串图；改 service 时先看 12 页 D4。

## 6. 工程与投递

| 项 | 要求 |
| --- | --- |
| 工程位置 | `Plugins\Plugin.ImageAlign\`（红线①） |
| csproj | `net9.0-windows`、`UseWPF`、四引用、HALCON HintPath、`<EmbeddedResource Include="Help\**\*" />` |
| `Modules\` | 构建后自动复制；宿主只扫 `Modules\Plugin*.dll` |
| `VisionMaster.sln` | **必须登记**（R62） |
| `HelpManualCheck.Tiers` | 必须登记 `["ImageAlignPlugin"] = ManualTier.M` |
| 手册 | 8 页（5 用户 + 3 开发），开发页合计 ≥150 行 |
| 断言 | `FlowCanvasChecks\ImageAlignChecks.cs` + `ProjectReference` + `Program.cs` 一行 `Run()` |

## 7. 全局变量：不写

不调用 `context.GlobalVariables.TryWrite`，不持有变量注册表。
需要把 `HomMat2D`/`Residual` 写全局时，由下游「变量赋值」`Scope=Global` 映射。

## 8. `IsOk` 与 `Success`

| 端口 | 由谁写 | 下游怎么用 |
| --- | --- | --- |
| `Success` | 基类/插件 | 流程引擎据此标记节点；false = 没算成 |
| `IsOk` | 本插件 | 普通 bool 输出；接「逻辑判断」/分支，不参与引擎状态 |

`Residual > ResidualLimit` 是**硬失败**（Success=false），因为通常意味着点错/遮挡；
`MinScore`/`MinValidAreaRatio` 只影响 `IsOk`，不中断流程。

## 9. 输入图像所有权

- `Image` / `ReferenceImage` 是上游对象，本插件不 Dispose；
- `AlignedImage` / `ValidRegion` 是新建对象，交给输出端口后由基类轮首回收；
- `ReferenceImage` 只读尺寸/域，不读像素；与 `Image` 同尺寸是硬要求。

## 10. 自定义视图（画布点选）

插件实现 `IPluginCustomViewProvider`：

```csharp
public object GetConfigView(IStepConfigData stepData)
{
    Initialize(stepData);                 // 灌配置 + 把文本点对解析成 PointRows
    return new ImageAlignView { DataContext = this };
}
```

- 视图是普通 `UserControl`；`Initialize/OnConfirm` 仍由插件（基类契约）负责，视图只做事件桥接；
- 画布用 `h:ImageReadOnly`：`HImage` 绑 `DisplayImage`、`IsPickMode` 绑 `IsPicking`、
  `ImagePicked` 转给 `ApplyPickedPoint(Row, Col)`；
- 取点一次性：`ApplyPickedPoint` 写完立即 `IsPicking=false`；
- 点对表格绑 `PointRows`（`ObservableCollection<AlignPointRow>`），确认时序列化回
  `RefPointsText`/`CurPointsText`；
- 「参数…」用 `EasyDialog.ShowPropertyGridSync("图像对齐参数", ImageAlignConfigModel)`，
  模型带 `[SuperDisplay]`，由 `FlatPropertyGrid` 渲染；确认后 `ApplyTo(plugin)` 写回；
- XAML 必须就地合并 `/Core.Halcon;component/Generic.xaml` 与 `UI;component/Themes/Generic.xaml`（R22/R30-R32）；
- 不写死颜色（R140）；不用 `GridView`（R19-R22）；可复用视觉结构第二次出现就下沉 `Dialog\`（R141）。

视图不持有/不释放图像：只读 `Image.ActualValue`/`ReferenceImage.ActualValue` 的引用。

## 11. HALCON 可用性

- 原生库/许可是机器级依赖；启动自检只 Warning；
- 第一次调 HALCON 时加载原生库；不可用会抛异常，由基类转 `Fail`；
- 断言宿主用 `ProbeHalconUsable()` 先造小图；不可用则判红并说明（R38）。
