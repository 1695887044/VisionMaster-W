# 卡尺测量 · 宿主契约与接口面

> 分类：测量 ｜ 来源：插件 Plugin.CaliperMeasure ｜ 读者：开发

这一页只写**宿主替插件做了哪些事、插件因此不能自己做什么**，以及配置数据在 `.vms` 里的真实形态。
运行时行为与内存归属在上一页，不变量在下一页。

## 一、基类（`VisionPluginBase`）白拿的行为

| 宿主行为 | 具体做法 | 对本插件意味着 |
| --- | --- | --- |
| 预置成功 | `Execute` 进 `RunAlgorithm` 前把 `Success.Value = true`、`ErrorMessage.Value = ""` | 只有真的跑不通才调 `Fail(...)`；判定 NG 也**不许**动 `Success` |
| 失败一行表达 | `Fail(msg)` = 置 `Success=false` + 写 `ErrorMessage` | 本插件 4 个失败点全部走它，不自己拼两条赋值 |
| 轮首回收输出 | `AutoDisposeRoundOutputs`（默认 true）**在 `RunAlgorithm` 之前**释放上一轮 `IDisposable` 输出并置 null；与上游链接当前值同引用的（透传）自动跳过 | `MeasureImage`/`FittedGeometry`/`EdgePoints` 三个 HImage/HXLD 端口不用自己 Dispose；**`MeasureValue`/`Deviation`/`IsOk` 必须自己归零**（double/bool 不是 `IDisposable`，基类不碰） |
| 异常兜底 | 未捕获异常自动 `Fail("{异常类型}: {消息}")` + 记日志 | 本插件仍自己 `try/catch`，为的是加"卡尺测量失败："前缀并把同一句写进日志（比基类的裸异常名好排查） |
| 异常策略 | `RethrowOnException` 默认 false（转失败、流程继续） | 卡尺不重写；纯视觉算子没有"带脏状态继续跑会出事"的互锁需求 |
| 端口发现 | 反射收集所有 `InputPort<T>`/`OutputPort<T>` 公有属性，进 `Inputs`/`Outputs` 字典 | 端口字段必须写成**属性**（`public InputPort<HImage> SrcImage { get; } = new(...)`），写成字段就发现不到 |
| 配置灌值 | `ApplyConfigValues(stepData)` 把 `InputValues` 按属性名反射赋值；类型不匹配**静默跳过**保留默认值 | 老方案/手改文件的坏值不会炸流程，与运行期夹取（`ClampFinite`）叠成两道防线 |
| 确认写回 | `OnConfirm`：未链接端口写当前值、**已链接端口移除固定值键**；16 个 `[StepConfig]` 走 `SnapshotConfigValue`（JSON 往返拷贝） | `CaliperRegions` 存进 `InputValues` 后不再与界面活对象共享引用——确认后继续拖拽不会污染已确认数据 |
| 取消 | `OnCancel()` 基类空实现 | 试运行不落盘，所以本插件不需要还原逻辑 |
| 动态端口 | `AddDynamicOutput`/`RemoveDynamicOutput`/`ClearDynamicOutputs` | **本插件一个都不用**：输出拓扑固定 6 个，卡尺数变化不改端口（否则下游连线会被剪断） |
| 释放 | 基类 `Dispose` 回收输出端口里的非托管值 | 本插件 override 里先停表、清预览、逐个 `info.Dispose()`、`_renderer.Dispose()`，**最后** `base.Dispose()` |

`LastError`（读 `ErrorMessage`）与 `InstanceName`（日志前缀，默认"未赋值"）也由基类给。

## 二、三个接口的分工

| 接口 | 谁实现 | 谁调 |
| --- | --- | --- |
| `IVisionPlugin` | 基类已实现（`Execute`、`Inputs`/`Outputs`、`Success`/`ErrorMessage`、`PluginID`、`InstanceName`） | 流程引擎 / 编译执行 |
| `IPluginConfigView`（`Initialize` / `OnConfirm` / `OnCancel`） | **基类实现**，本插件只 override `Initialize`（播种画布） | 配置 Shell |
| `IPluginCustomViewProvider`（`GetConfigView(IStepConfigData)`） | 本插件实现 | Shell 用它替代通用 DataBindView |

一个容易踩的形态差异：Shell 认两种形态——

```csharp
_pluginView = (viewObj as IPluginConfigView) ?? (viewObj.DataContext as IPluginConfigView);
```

本插件返回的 `CaliperMeasureView` 是**纯 UserControl，不实现 `IPluginConfigView`**，靠第二档 `DataContext` 回落到插件实例。
所以：

1. `GetConfigView` 必须**先 `Initialize(stepData)` 再返回视图**（视图自己没有恢复配置的能力）；
2. 视图的 `DataContext` 一旦被换成别的对象，Shell 的确认/取消链就断了——不要在视图里换 VM；
3. 代码后置只有一行：`Loaded → (DataContext as CaliperMeasurePlugin)?.OnViewLoaded()`。
   配置态身份（`MarkAsConfigInstance`）**只在视图就绪时**才确立，这是"运行实例从不跑预览"的实现方式。

## 三、`IStepConfigData` 用到的面

| 成员 | 用在哪 |
| --- | --- |
| `InputValues` | 16 个配置键 + 2 个端口值（`SrcImage`、`MmPerPixel`）的容器 |
| `IsLinked(name)` | `OnConfirm` 判断端口是否连线（连线则移除固定值键） |
| `SetInputValue(name, value)` / `RemoveInputValue(name)` | 基类 `OnConfirm` 用；插件不直接调 |
| `StepData`（基类属性） | XAML 里 `LinkableValueEditor` 的 `StepData` 绑定源，链接按钮因此可用 |

界面绑定关系（`CaliperMeasureView.xaml`）：「输入图像」行是
`cv:LinkableValueEditor Label="输入图像" Port="{Binding SrcImage}" StepData="{Binding StepData}"`——
插件不需要为"链接上游 / 手填 / 变量"三种来源写任何 UI 逻辑。

## 四、`[StepConfig]` 的语义与 16 个键

写法语义（AGENTS.md R16/R18）：

- `[StepConfig]` = 这个属性要进 `InputValues` 并参与反射灌值/写回；
- `DefaultValue(...)` = 出厂值标注，实际初值由字段常量给（本插件把 16 个默认值全抽成 `private const`，
  界面、灌值、校验三处引用同一份）；
- 属性写成 `public partial T X { get; set; }` + `partial void OnXxxChanged(...)`，
  **实现由 `Core.PluginGenerators` 生成**，源码里只有声明；类必须 `partial`；
- 生成器一改就要整解决方案重建，否则"改了生成器但界面没变"会误导排查。

`.vms` 里的键 = 属性名（逐字符）：

```
MeasureKind  CaliperRegions  CaliperCount  Sigma  EdgeThreshold  Polarity  Select
Interpolation  FitAlgorithm  FitMaxError  FitMinPoints  AnnulusWidth
StandardValue  UpperTolerance  LowerTolerance  PixelSizeMm
SrcImage（端口固定值，未连线时才写）  MmPerPixel（同上；已连线则该键被移除）
```

值形态：

- 枚举存**成员名**（`Width`、`Tukey`、`Bilinear`…），界面中文只存在于 `[Display]`；
- `CaliperRegions` 存数组，每项 `{ Name, ShapeType, Params }`；
  `DisplayText`/`ParamNames`/`ParamEntries` 全部 `[JsonIgnore]`。
  **`ParamEntries` 那个 `[JsonIgnore] 不能删**：`CaliperParamEntry` 无无参构造且持有 owner 引用，
  Newtonsoft 默认会尝试填充 get-only 集合属性，删掉特性反序列化直接抛
  "Unable to find a constructor to use for type CaliperParamEntry"。
- 形状与 `Params` 长度必须匹配（矩形 5 项、圆形 3 项），否则运行期报"参数不完整"。

## 五、画布控件依赖（`h:ImageEdit`，DP 声明在 `HalconBase`）

```xml
<h:ImageEdit ActiveRoi="{Binding CanvasActiveRegion, Mode=TwoWay}"
             DrawObjectList="{Binding CanvasRegions}"
             HImage="{Binding PreviewImage, Mode=OneWay}" />
```

| DP | 方向 | 契约 |
| --- | --- | --- |
| `HImage` | 单向（VM → 控件） | 控件不负责释放；**换图必须由 VM 侧释放旧图**（本插件在 `PreviewImage` setter 里"先切绑定后释放旧"） |
| `DrawObjectList` | VM 拥有集合，控件改内容 | 右键新建/删除直接改动集合 → 插件的 `CollectionChanged` 回写持久模型；插件不许再实现一套句柄系统 |
| `ActiveRoi` | 双向 | 控件选中/拖拽换绑都写这里；插件按 `RoiName` 找对应持久条目 |

插件侧必须知道的三条控件口径（R51~R54）：

1. **冷启动回放**：新绑定的句柄第一次按下会 `ReplayMouseDownForFreshHandle`，所以"第一下就能拖"，
   插件不要为此加任何"先激活"逻辑；
2. **命中容差 14px 只用于去激活**（判"点的是不是当前活动 ROI 以便取消选中"），
   对其他对象的命中测试**不带容差**——别把 14px 当通用吸附；
3. `rectangle2`/`ellipse` 的 **`length1` 沿列轴方向**（Phi=0 时水平）。
   本插件"L1 = 扫描方向、测竖直边用 Phi=0"正是建立在这条上；
   `DistributeCalipers` 里 L2 轴单位向量取 `(cosφ, sinφ)`（而不是 `(cosφ, −sinφ)`）是同一实证的第二批修正。
4. **拖句柄的断言必须真机看**（闸门口径 R52）：无头环境没有窗口，句柄拖拽类校验跑不了。

## 六、渲染件依赖

- 本插件**不碰窗口**：只有 `AnnotationRenderer` 攒 `BakePlan`，开窗/按尺寸复用窗口/`SetPart`/回读/释放
  六条纪律全在 `Services\Core.Halcon\Helpers\AnnotationBaker.cs`（R36~R39）；
- 因此本插件里没有 `disp_obj`/`get_window_extents`/`dump_window` 这一类调用，也不该有；
  判据：`grep -rn "OpenWindow" Plugins\Plugin.CaliperMeasure` 必须为空；
- 需要改文字位置/对齐时，用 `BakeText` 的显式 `Box`（首字母大写）与 `HAlign`（Left/Center/Right），
  宽度测量只能走 `GetStringExtents`；本插件目前固定 `12, 12, 20` + `box: true`。

## 七、两份数值口径必须同步

| 处 | 在哪 | 谁兜 |
| --- | --- | --- |
| 界面区间 | `CaliperMeasureView.xaml` 的 `ui:NumericBox Minimum/Maximum` + `cv:NumberValidationRule`（另有 `DecimalPlaces="0"` 让 N 与最少点数只收整数） | 挡住明显手滑，红字提示 |
| 运行期夹取 | `NormalizedParameters()` 的 `ClampFinite`（NaN/Infinity 一律按 min） | 老方案、手改 `.vms`、别的宿主灌值 |

两边数字来自同一批 `private const`（如 `SigmaClampMin = 0.4` 与 XAML 的 `Minimum="0.4"`），
**改一处不改另一处 = 静默失配**（界面允许 0.2，运行时被夹回 0.4，用户看不出为什么）。
Sigma 下限 0.4 不是审美：算子对高斯核尺寸有最小要求，填 0 会直接抛参数错。

## 八、打包与内嵌资源

- `Plugin.CaliperMeasure.csproj` 已有 `<EmbeddedResource Include="Help\**\*" />`（不需要改）；
- 产物 DLL 必须真嵌上手册（R41/R42），核对命令：

```bash
tr '\0' '\n' < Modules\Plugin.CaliperMeasure.dll | grep -a "\.Help\."
```

- 宿主从 `Core.PluginAssembly` 按 `Help/{page}.md` 读内嵌页，文件名 `NN-` 前缀决定顺序，
  开发页固定 `NN-dev-xxx.md` 段位（闸门 HM6/HM12/HM12b）。

## 九、移植取舍（要把这套搬到别的插件时）

| 可原样复用 | 必须重写 |
| --- | --- |
| `DistributeCalipers`（纯算术，无 HALCON 调用，可直接抄）、`CollectCircleEdgePoints` 的圆周/径向几何、`Median`/`Dist`/`BuildSegmentXld` | 五分支的取数与端口赋值、失败文案（要按新对象改名与规格重写） |
| `TryToGrayImage`（1/3 通道之外的都不吃）、`ClampFinite`、`NormalizedParameters` 的"先校验后计算"骨架 | 搜索区形状校验规则（别的新算子可能要椭圆/多边形） |
| `AnnotationRenderer` 的"只攒 plan + 缺席图层不进计划"写法、`_isConfigInstance` + `DispatcherTimer` 防抖预览 | 图层清单（顺序/颜色/字号按新算子语义重排） |
| `MeasureParams`/`MeasureResult` 两个私有内部类的分工（配置快照 vs 结果载体） | `GetEffectivePixelSizeMm` 的三分支只在"当量类"算子有意义 |
| 圆退化三条几何判据（任何"拟合几何"类算子都该抄） | 判定三分支（标准值/上下公差）与 NG 语义只在测量类算子成立 |
