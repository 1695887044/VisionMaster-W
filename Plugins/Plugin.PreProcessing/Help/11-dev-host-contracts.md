# 图像预处理 · 开发：宿主契约与免费行为

> 分类：图像处理 ｜ 来源：插件 Plugin.PreProcessing ｜ 读者：开发

本页回答一个问题：**哪些能力是宿主白送的、哪些是本插件自己实现的、边界在哪里**。
写插件时最贵的错误不是功能没做出来，而是把宿主已经负责的事情又做了一遍，或者以为宿主会做其实不会。
数据流与所有权见 `10-dev-architecture.md`；违反契约的现象见 `12-dev-invariants.md`。

主源码：`Plugins\Plugin.PreProcessing\PreProcessingPlugin.cs`、`Models\`、`PreProcessingView.xaml.cs`。
宿主源码（只读，不许改）：`Shard\Core.Interfaces\VisionPluginBase.cs`、`Shard\Core.Interfaces\StepConfigAttribute.cs`、
`Shard\Core.PluginGenerators\StepConfigGenerator.cs`、`Shard\Core.Controls\Events\PluginPreviewExtensions.cs`、
`UI\Controls\CustomControl\PropertyGrid\IPropertyGridLayoutSwitch.cs`。

---

## 1. 宿主免费提供的行为（本插件一行代码都没写）

`PreProcessingPlugin` 只 override 了 `RunAlgorithm`、`ApplyConfigValues`、`OnConfirm`、`Dispose`，
下列行为全部来自 `VisionPluginBase`，改插件时不要重复实现：

| 免费行为 | 宿主位置 | 对本插件的意义 |
| --- | --- | --- |
| 进入 `RunAlgorithm` 前预置 `Success=true`、清空 `ErrorMessage` | `VisionPluginBase.Execute` | 结尾不写 Success；只有失败路径用 `Fail(...)` |
| 未捕获异常自动转失败 | `Execute` 的 try/catch，`Fail($"{ex.GetType().Name}: {ex.Message}")` | 本插件仍留了业务 catch，为的是带"图像预处理失败："前缀 |
| 异常策略开关 | `protected virtual bool RethrowOnException => false` | 本插件未重写：出错转失败、流程继续 |
| 轮首自动回收上一轮输出端口的非托管值 | `protected virtual bool AutoDisposeRoundOutputs => true` | **本插件未重写、也没有关掉**；与手写的 `DisposeOldOutput()` 时机等价（详见 §3） |
| 失败一行表达 | `protected void Fail(string message)` | `Fail("输入图像为空或未初始化")`、`Fail($"图像预处理失败：{ex.Message}")` |
| 端口字典与反射发现 | `Inputs` / `Outputs` / `EnsurePortsDiscovered` | 五个端口 + `Success` / `ErrorMessage` 自动出现在绑定界面 |
| 动态端口增删 API | `AddDynamicInput/Output`、`RemoveDynamic*`、`ClearDynamicOutputs` | **本插件不用**（算子链长度不进流程拓扑） |
| `Dispose()` 统一释放输出端口里的 `IDisposable` | `VisionPluginBase.Dispose` | 所以插件 `Dispose()` 末尾必须 `base.Dispose()`，且不要自己释放 `Image` 端口的图 |
| `[StepConfig]` 属性 ⇄ `InputValues` 读写 | `ApplyConfigValues` / `OnConfirm` | 键名就是属性名，无需映射表 |
| 复杂配置存盘前的 JSON 往返深拷贝 | `SnapshotConfigValue` + `SnapshotSettings` | `Chain` 落盘后与界面活对象不再共享引用 |
| 类型不匹配的旧数据宽容跳过 | `ApplyConfigValues` 内层 try/catch | 老方案读新插件不会炸，只会退回默认值 |
| 配置视图发现与注入 | `IPluginCustomViewProvider` → `PluginConfigShell` 的 `ContentPresenter` | 主程序不认识 `PreProcessingView` 这个类型 |
| 试运行 | `Engine\PluginTestRunner`（读已保存 `InputValues`，不落盘） | 试运行走的就是 `RunAlgorithm`，与正式运行同一条链 |
| 并行调度 | `Engine\FlowCompiler` 读 `[ParallelSafe]`（**按 FullName 字符串匹配**） | 见 `12-dev-invariants.md` 的 `[ParallelSafe]` 一节 |
| 取消语义 | `virtual void OnCancel()` 空实现 | 本插件不重写：试运行不落盘，无需还原 |

---

## 2. `[StepConfig]` 与源生成器：`DisplayViewIndex` 为什么是 `partial` 属性

```csharp
[StepConfig, DefaultValue(1)]
public partial int DisplayViewIndex { get; set; }
```

- `StepConfigAttribute` 只有一个用途标记：`[AttributeUsage(AttributeTargets.Property)]` 的空特性，
  语义是"参与 `InputValues` 持久化与实例灌值，但**不暴露为端口**（不进数据流、不可变量链接）"。
- `partial` 来自 `Shard\Core.PluginGenerators\StepConfigGenerator.cs`。它在编译期展开成
  后备字段 + `get => _displayViewIndex;` + `set` 里走 `SetProperty`，并声明两个可选钩子
  `partial void OnDisplayViewIndexChanging(ref int value)`（归一化入参）与
  `partial void OnDisplayViewIndexChanged(int value)`（变更后副作用）。**运行期零痕迹**，不产生额外反射。
- `[DefaultValue(1)]` 由生成器读取（常量名 `DefaultValueAttributeName`），用作后备字段初值，
  所以新建步骤就是"显示窗口 1"，不需要构造函数里赋值。
- 生成器的硬性形态要求（违反就是编译错误，不是警告）：
  - 修饰符只允许 **访问修饰符 + `partial`**；`static` / `virtual` / `override` / `new` 会被诊断拒绝，
    诊断标题为"不支持的 [StepConfig] partial 属性形态"；
  - 必须**同时**有 `get;` 与 `set;`，只读/只写配置项保持手写普通属性；
  - 只认"定义声明"：带 `partial`、有访问器列表、且所有访问器都没有实现体。
- 生成物是同一个 `partial class` 的一部分，所以在 `.cs` 里看不到 `_displayViewIndex` 这个字段——
  **不要为了让它"可见"而改回手写属性**：手写的 `[StepConfig]` 属性依然能被反射灌值（宿主走 `GetProperties`），
  但会丢掉 `SetProperty` 的通知与两个钩子。

`Chain` 则**不能**用 partial 方案：

```csharp
[StepConfig]
public List<OperatorStepItem> Chain { get; set; }   // 手写 backing field _chain
```

它是引用类型、带自己的 setter 逻辑（赋值时触发重建判断），且宿主对它做的是 `SnapshotConfigValue` 的 JSON 往返，
不是 `SetProperty` 通知。生成器只服务"值类型/简单可通知属性"这一类。

---

## 3. 持久化：键名、数据形状、兼容规则

`IStepConfigData.InputValues` 是 `Dictionary<string, object>`，本插件写进去的键一共两个：

| 键 | 值类型 | 写入点 | 读取点 |
| --- | --- | --- | --- |
| `DisplayViewIndex` | `int` | `base.OnConfirm` 遍历 `_configProps` | `base.ApplyConfigValues` 经 `ValueConverter.Convert` |
| `Chain` | `List<OperatorStepItem>`（JSON 往返后的深拷贝） | 同上 | 同上，随后 `RebuildOperators()` |

端口名也是同一张表的键（`SrcImage` 未链接时由 `base.OnConfirm` 写固定值；已链接则 `RemoveInputValue`，
避免残留脏数据）。**本插件的输出端口不占 `InputValues` 键。**

三条必须守住的兼容约定：

1. **弱类型快照**：`OperatorStepItem` 只存 `OperatorKey` + `Enabled` + `Dictionary<string,string> Params`，
   不落算子类名。存的是**格式**而不是 CLR 对象，类改名不影响老方案，Key 改名会。
2. **值编码**：枚举存**数字**、布尔存 `"1"`/`"0"`、`double` 用 `"R"` 往返、一律 `InvariantCulture`。
   因此 `OperatorEnums.cs` 的成员数值即存盘格式，只许末尾追加。
3. **读取宽容**：`OperatorStepItem.LoadParams` 解析失败就保留默认值，末尾统一
   `OnPropertyChanged(string.Empty)` + 刷新 `Summary`；认不出的 Key 进 `LoadWarning` 并跳过。
   `ApplyConfigValues` 的类型不匹配同理跳过。**新增字段/删除字段都不需要版本迁移代码。**

`AutoDisposeRoundOutputs` 与手写 `DisposeOldOutput()` 的关系值得说清：
宿主轮首回收时"与上游链接当前值同引用的值自动跳过"，所以严格讲本插件不写也不会误杀上游；
但插件仍然显式调用了 `DisposeOldOutput()`，因为那里额外负责一件事——
**先 `Image.Value = null!` 置空端口再释放**。`TypedValue` 的 setter 走 `SetProperty` 相等判断，
若新值引用恰好与旧值相同就不发通知，下游会读到已销毁的图。这条是插件自己的责任，宿主不代劳。

---

## 4. 配置视图接入：三个接口各自管什么

| 接口 | 谁实现 | 谁调用 | 关键点 |
| --- | --- | --- | --- |
| `IPluginCustomViewProvider` | `PreProcessingPlugin` | 主程序 `PluginConfigShell` | 只一个方法 `object GetConfigView(IStepConfigData)`；返回的视图须同时实现 `IPluginConfigView` |
| `IPluginConfigView` | `PreProcessingView` | Shell 的确认/取消按钮 | `Initialize` / `OnConfirm` / `OnCancel` 三件事，试运行不在这里 |
| `IPropertyGridLayoutSwitch` | `PreProcessingView` | 壳的"卡片式"开关 | 实现了才亮出开关；壳**不去猜内容类型** |

本插件的接线方式：

```csharp
public object GetConfigView(IStepConfigData stepData)
{
    Initialize(stepData);                       // 基类灌值 → 触发 ApplyConfigValues → RebuildOperators
    return new PreProcessingView { DataContext = this };
}
```

`PreProcessingView` 类头是 `UserControl, UI.CustomControl.IPropertyGridLayoutSwitch`，
`UseCardLayout` 直接转写 VM 的 `IsCardLayout`（`IsFlatLayout => !_isCardLayout`）。
接口注释里那条"卡片式自带左侧分组页签条约 128px + 卡片留白约 68px，固定开销远大于表格式，
**参数列要给够宽度（见 PreProcessingView 的 540）**"是本视图 360→540 的直接来源；
VM 侧的 `EnlargeHostWindowIfNeeded` 负责把宿主窗口撑到能满足 1120×620 内容区，三条约束一起算，
不要单独改其中一条。

`OnConfirm` 的顺序也是契约：先 `WriteChain()` 把活链写回快照，再 `base.OnConfirm(stepData)` 落盘。
反过来的话，落盘的是上一次改参数之前的链。

---

## 5. 算子库是怎么"自己长出来"的

`Models\OperatorRegistry.cs` 静态构造扫描**本程序集**所有打了 `[PreprocessOperator]` 的类：

```csharp
[PreprocessOperator("gauss", "高斯滤波", "滤波", Description = "...", Order = 70)]
```

- 构造参数三件套 `key, displayName, category` 必填且只有 getter；
  `Description`（悬浮提示）与 `Order`（组内排序，越小越前）可选。
- `Key` 注释原话："存盘标识：一经发布不要改名，否则旧方案里的算子会加载不到"。
- 每条扫出来的元数据是一个 `OperatorDescriptor`（Key/DisplayName/Category/Description/Order + `Factory`），
  `MenuText => DisplayName`；`Categories` 按**组内最小 Order** 排分类顺序，反射结果缓存成 List，之后纯内存查表。
- `Create(key)` 对未知 Key 返回 `null`，不抛异常——这是 `LoadWarning` 能"跳过并提示"的前提。
- 算子库分组是运行期算出来的，**没有任何 switch/菜单数组需要同步修改**（开闭原则）。
  新增算子只写一个类：继承 `PreprocessOperator`（框类继承 `BoxOperatorBase`）+ 打特性 + 覆写 `Apply` 与 `Normalize`。

参数面板同样靠反射：`PreprocessOperator.GetParamProps()` 的过滤条件是
`[SuperDisplay]` **且** `CanRead && CanWrite` **且** `DeclaringType != typeof(PreprocessOperator)`。
也就是说：只读属性、写在基类上的公共属性一律不出现在界面上。
枚举候选项由自有 FlatPropertyGrid 的 `EnumGenerator` 生成，读的是 `[Description]`；
数值区间由 `RangeValidation` 兜底。**这套面板不属于宿主属性面板，宿主那套一行都没改。**

---

## 6. 端口契约

```csharp
public InputPort<HImage> SrcImage { get; } = new();
public OutputPort<HImage> Image { get; } = new("Image", "预处理后的图像");
public OutputPort<int> StepCount { get; } = new("StepCount", "算子总步数");
public OutputPort<int> ActiveCount { get; } = new("ActiveCount", "启用的算子个数");
public OutputPort<string> ActiveOperators { get; } = new("ActiveOperators", "启用的算子（按执行顺序）");
public OutputPort<string> FailedOperators { get; } = new("FailedOperators", "失败的算子（序号:算子名: 原因）");
public OutputPort<int> FailedCount { get; } = new("FailedCount", "失败算子个数");
```

- `InputPort` 用无参构造 → 端口名取属性名 `SrcImage`；`OutputPort` 显式给 `(name, description)`，
  描述文字直接出现在绑定界面。
- 读上游一律用 `SrcImage.ActualValue`（它负责"链接值 / 固定值 / 变量值"三态归一），
  不要读 `SrcImage.Value`。
- 写 HImage 输出用 `Image.TypedValue`（强类型），写 int/string 统计用 `.Value`。
- `FailedCount` / `FailedOperators` 是 P0-6 新增的失败端口：格式 `序号:算子名: 错误消息`（步号 1 基、`；` 分隔），
  **每轮 `RunAlgorithm` 开头先清零**，避免上一轮的失败残留到"这一轮没跑成"的场合。
  它们与 `Success` 语义不同：默认口径下 `Success=true` + `FailedCount>0` 是合法组合（失败步被跳过）。
- `FailOnOperatorError` 是 `[StepConfig]` 配置属性（不是端口），走 `InputValues` 落盘；
  默认 `false` 保持历史行为，视图左栏的「算子失败即失败」勾选框直接绑它。
- **`null` 是端口的合法状态**：`DisposeOldOutput` 里 `Image.Value = null!;` 就是靠 object 通道置空，
  别为了消警告改成赋空图。
- 本插件**没有** `HRegion` 输出端口，也没有实现 `IDynamicOutputProvider`；
  `IStepConfigData.OutputPortDefinitions` 对它恒为空。

---

## 7. 预览推送与离屏烘焙的边界

- 推图走 `Shard\Core.Controls\Events\PluginPreviewExtensions.cs` 的扩展方法
  `this.PublishPreview(image, DisplayViewIndex)`（另有带 `MeasureAnnotation`、带 `ImageInfoRow` 的重载，本插件只用最简单的一个）。
- 语义：显示层拿到事件后**自己 `CopyImage` 并独占那份副本**，所以插件传出去的仍是端口持有的图；
  无订阅者时整段是 no-op，非 WPF 宿主下会降级（`FlowCanvasChecks\HttpImageSmoke.cs` 覆盖了这条）。
- `FlowCanvasChecks\PreviewCaptureChecks.cs` 记着一次真实缺陷：`PublishPreview` 经事件总线 `BeginInvoke` 到 UI 线程才拷图，
  而插件输出图可能在下一轮被释放——修法是宿主实现 `IImagePreviewCapture`，在**发布线程**上先同步拷走。
  插件侧唯一要做的是把推图包在 try/catch 里并只 `Warn`，见 `TryPublishPreview`。
- **本插件完全不涉及离屏烘焙**：`Plugins\Plugin.PreProcessing` 全目录 `grep` 结果为
  0 处 `OpenWindow`、0 处 `AnnotationBaker`、0 处 `BakePlan`。
  仓库里唯一的离屏烘焙实现是 `Services\Core.Halcon\Helpers\AnnotationBaker.cs`（R36~R39），
  本插件既不开窗也不调它，所以 `AnnotationBakerChecks.cs` 里那八条判据对本插件**不适用**；
  "全仓只有一处开窗"由 `FlowCanvasChecks\ResultDisplayCheck.cs` 的静态扫描兜住。

---

## 8. 工具箱元数据与"中文从哪来"

```csharp
[Display(Name = "图像预处理", GroupName = "图像处理",
         Description = "串接色彩/几何/滤波/形态学/增强/二值化算子，支持逐步预览与单步启停",
         ShortName = "\uf085")]
```

`Name` 是工具箱条目名，`GroupName` 是分组名（帮助手册页头部的"分类"必须与它逐字一致），
`ShortName` 存的是 FontAwesome 私用区字符即图标位，`Description` 是悬浮说明。

**两条显示路径要分清**（这是 AGENTS.md R40 那条欠账的实际情况）：

| 场景 | 读哪个特性 | 本插件现状 |
| --- | --- | --- |
| 工具箱条目、自动参数面板的下拉候选 | `[Display(Name=...)]`（`StepConfigOption.ForEnum`） | 只作用于插件类与端口；**看不到算子参数枚举** |
| 本插件自有 FlatPropertyGrid 的枚举下拉 | `[Description(...)]`（`EnumGenerator`） | `OperatorEnums.cs` 全部只有 `[Description]`，界面显示中文 |

结论：`Models\OperatorEnums.cs` **至今没有 `[Display(Name="中文")]`**，R40 的欠账未补；
因为算子参数不是端口、走的是 `[Description]` 那条路径，当前界面不受影响。
若日后把这些枚举搬进宿主自动参数面板，会立刻退回英文成员名。

---

## 9. 帮助手册的打包与闸门

- `Plugin.PreProcessing.csproj` 已有 `<EmbeddedResource Include="Help\**\*" />`，新增/删除页面无需改 csproj。
- 手册页受 `HelpManualCheck` 的 HM0~HM17 静态校验：首行必须是 `# H1`、页头分类须与 `[Display].GroupName` 逐字一致、
  开发页加"｜ 读者：开发"、文件名 `NN-` 前缀、不得出现占位标记字样（HM9 逐字扫的那几个）、UTF-8 无 BOM、LF。
- **R41**：帮助页面必须随插件打包，改完要核对嵌入清单。
- **R42**：增量构建对嵌入资源存在静默漏检——改过 `Help\` 后必须 Debug + Release 各完整重建一次，
  再用 `12-dev-invariants.md` A 组末尾那条 `tr '\0' '\n' … grep -a ".Help."` 的命令核验清单，
  只看构建成功不算做完。
- 页名被别处引用过（方案说明书 §8 就点过本插件"承诺过但不存在的页"）：
  **动页面文件名之前先全仓 `grep`**，删除旧页必须同时确认事实内容已并入新页。

---

## 10. 明确不归本插件管的宿主职责

| 事项 | 由谁负责 | 本插件做了什么 |
| --- | --- | --- |
| 步骤拓扑、连线求值、并行分层 | `Engine\FlowCompiler` | 只声明 `[ParallelSafe]`，不感知上下游 |
| 通用参数面板（无自定义视图的插件） | `UI\...AutoPortConfigView` | 用 `IPluginCustomViewProvider` 绕开，不改它 |
| 属性网格的表格/卡片切换 UI | 插件配置壳 | 只提供 `UseCardLayout` 的实现与列宽 |
| 结果显示窗口的视图渲染 | 主界面 + 事件订阅者 | 只 `PublishPreview`，不碰渲染 |
| ROI 交互画布的通用手势 | `Core.Controls` 的 `h:ImageEdit`（R51~R55） | 只喂 `CanvasRois` / `CanvasActiveRoi` 两个绑定源 |
| 方案存盘格式与版本 | `Core\Models\ProcessStep\StepModel` | 只提供两个 `InputValues` 键 |

接入任何一条新宿主能力前，先确认它不是上面某一行"已经有人负责"的事——
本插件的迁移硬约束写在类头注释第 2 条：**所有对外接入只走 `VisionPluginBase` + `IPluginCustomViewProvider` + `[StepConfig]`
这三个既有扩展点，不改主程序、不改属性面板、不改 `Core.Interfaces`。**
