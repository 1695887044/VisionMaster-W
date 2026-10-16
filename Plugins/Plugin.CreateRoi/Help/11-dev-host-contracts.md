# ROI · 开发：宿主契约与移植清单

> 分类：常用工具 ｜ 来源：插件 Plugin.CreateRoi ｜ 读者：开发

本页列**这个插件依赖宿主的哪些约定**（哪些行为不是它自己实现的，是基类/控件给的），
以及**要把它移植到别的工程或照它写新插件，需要带走的清单**。

## 一、继承自 `VisionPluginBase` 白拿的行为（不要重复实现）

| 契约 | 宿主给的行为 | 本插件哪里靠它 |
| --- | --- | --- |
| 端口发现 | 反射本类型公开实例属性，`IInputPort` / `IOutputPort` 自动进 `_inputs` / `_outputs`；**端口名为空时自动用属性名**；用 `TryAdd`（静态属性优先，动态注册不覆盖） | `SrcImage` / `RoiCount` 都写 `new()` 不带名字，端口名 = 属性名 |
| 默认成功 | `Execute` 进 `RunAlgorithm` 前预置 `Success=true`、清空 `ErrorMessage` | 正常路径一行 Success 都不写，只有"无图"调 `Fail(...)` |
| 异常转失败 | 未捕获异常 → `Fail("{Type}: {Message}")` + 日志；`RethrowOnException`（默认 false）决定要不要继续上抛中断当轮 | 逐 ROI 的异常在循环内自己 catch 成 Warn（要"跳过这一个"而不是"整步失败"），所以走不到这条 |
| 轮首回收输出 | `AutoDisposeRoundOutputs`（默认 true）在 `RunAlgorithm` 之前释放所有输出端口的非托管旧值并置空；**透传防御**：与某条输入端口上游链接当前值同引用的输出跳过不杀 | `MaskRegion` / `MaskImage` / `Crop_` / `Domain_` 全部交给它，插件里没有 `DisposeOldOutputs` |
| 统一灌值 | `ApplyConfigValues`：`InputValues` → 输入端口（**已链接的端口跳过**）+ `[StepConfig]` 属性（走 `ValueConverter.Convert`，类型不匹配宽容跳过） | 试运行 / 正式运行 / 配置初始化三条路共用同一个入口 |
| 默认确认 | `OnConfirm`：未链接端口值写回 `InputValues`，**已链接端口 RemoveInputValue**（避免残留脏数据）；`[StepConfig]` 属性逐个 `SnapshotConfigValue` 后写入 | 本插件不重写 `OnConfirm`——`RoiList` 的深拷贝靠的就是快照 |
| 配置快照 | `SnapshotConfigValue`：原始值（基元/string/枚举/decimal）直存，其余 **JSON 序列化再反序列化**拷贝一份 | 确认之后界面上继续改 `RoiItem` 不会污染已确认的数据 |
| 默认取消 | `OnCancel` 空实现（试运行读的是已保存的 `InputValues`、不落盘，所以无需还原） | 未重写 |
| 动态端口 | `AddDynamicOutput` / `RemoveDynamicOutput` / `ClearDynamicOutputs`（后者靠"属性反射得到的是固定端口"来区分动态端口） | `RebuildDynamicOutputs` 每次先 `ClearDynamicOutputs()` 再按 `RoiList` 重建 |
| 基类 `Dispose` | 释放所有输出端口承载的 `IDisposable` 值，单个失败不影响整体 | `CreateRoiPlugin.Dispose()` 先清自己的私有对象，**末尾必须调 `base.Dispose()`** |

## 二、实现的三个接口各自意味着什么

| 接口 | 方法 | 谁调、什么时候 |
| --- | --- | --- |
| `IPluginCustomViewProvider` | `object GetConfigView(IStepConfigData stepData)` | 宿主 `PluginConfigShell` 打开配置时调；返回的对象需同时实现 `IPluginConfigView`（本插件让 `this` 兼任，视图 `DataContext = this`） |
| `IPluginConfigView` | `Initialize` / `OnConfirm` / `OnCancel` | 基类给了默认实现；**`Initialize` 被本插件重写**（`base` 之后播种画布 + 恢复涂擦） |
| `IDynamicOutputProvider` | `void RebuildDynamicOutputs()` | 两条路都调：**配置实例**——`RoiList` setter 与画布同步；**编译实例**——`FlowCompiler` 在 `ApplyConfigValues` 之后调，从存盘快照恢复端口供接线 |

> 试运行**不在接口里**：由主程序 `PluginTestRunner` 统一执行插件的 `RunAlgorithm`，插件不需要为"试运行"写第二套代码。

## 三、`IStepConfigData` 提供的能力（配置视图的写路径）

| 成员 | 用途 | 注意 |
| --- | --- | --- |
| `InputValues` | 端口名/配置属性名 → 值（**同一个字典**，键名 = 名称，无映射层） | 写它要走 `SetInputValue` / `RemoveInputValue`，实现方会触发变更通知 → 流程版本递增 → 重编译 |
| `IsLinked(port)` / `GetLinkedAddress(port)` | 该端口是否已被变量链接、显示地址（如 `Global.CT`） | `ApplyConfigValues` 与 `OnConfirm` 都按它跳过链接端口 |
| `GetLink` / `SetLink` / `RemoveLink` | 连线引用对象（目标步骤/端口/显示地址） | 语义是"值由对方提供"的**连线**——名字类字段（变量名、端口名）不要挂它 |
| `OutputPortDefinitions` | `List<DynamicPortInfo>{ Name, DataTypeName(程序集限定名), Description }` 的**存盘快照** | 本插件在 `RebuildDynamicOutputs()` 末尾整表回写；编译器和绑定界面**只读这张表**，不依赖配置实例存活 |

**为什么快照要存类型限定名**：配置实例可能已经被关掉，而绑定界面要列出"这个步骤能连哪些端口、各是什么类型"。
没有这张表，动态端口在重开方案后对编译器就是隐形的。

## 四、`[StepConfig]` 的语义边界

`[StepConfig]` = **参与 `InputValues` 持久化与实例灌值，但不暴露为端口**（不进数据流、不可变量链接）。
标在属性上，且属性类型不能是 `IInputPort`/`IOutputPort`（那两类走端口发现，会被 `EnsureConfigPropsDiscovered` 排除）。

本插件的五个：`DisplayViewIndex`、`MaskInvert`、`RoiList`、`SmearDrawData`、`SmearEraseData`。

- **写法三选一**（仓库纪律 R18）：自动属性 / partial property + 生成器钩子 / 保持手写。
  本插件的 `MaskInvert` 用的是 **partial property**（`public partial bool MaskInvert { get; set; }` + `partial void OnMaskInvertChanged(bool)`），
  声明由 `Core.PluginGenerators` 生成——**所以改了生成器必须重建所有插件工程（R16），partial 属性所在类必须声明 `partial`**。
- **`SmearDrawData` / `SmearEraseData` 是"配置属性的持久化 companion"**：界面上没有它们的框，
  活对象 `SmearDraw` / `SmearErase` 不标 `[StepConfig]`（`HRegion` 进不了 JSON），由 setter 里的 `SyncSmearData()` 把编码文本同步进可存盘的那两个。

## 五、画布控件 `Core.Halcon.ImageEdit` 的绑定面

`ImageEdit : HalconBase`，本插件用到 `HalconBase` 上这些依赖属性（全部在 XAML 里绑，无 code-behind）：

| DP | 本插件怎么绑 | 语义 |
| --- | --- | --- |
| `HImage` | `{Binding DisplayImage, Mode=OneWay}` | 底图；`DisplayImage` 是计算属性（预览模式给掩膜抠图，否则给原图） |
| `DrawObjectList` | `{Binding CanvasRois}` | 句柄集合；控件右键新建 / 删除直接改这个集合，VM 侧靠 `CollectionChanged` 回写 |
| `ActiveRoi` | `{Binding CanvasActiveRoi, Mode=TwoWay}` | 当前可编辑对象；双向联动列表选中态 |
| `SmearMode` | 三个 RadioButton + `SmearModeConverter`（`None`/`Draw`/`Erase`） | 画笔模式；取消勾选回写 `Binding.DoNothing`，不污染源值 |
| `BrushRadius` | `{Binding ElementName=ImageEditor}` + `NumericInput` 附加属性 | 笔刷半径，键盘只放行数字与一个小数点 |
| `SmearDraw` / `SmearErase` | `Mode=TwoWay` | 控件笔画结束**以新实例回写** → VM 释放被替换的旧实例并持久化（**VM 是唯一所有者**） |

其它可用但本插件没用的：`IsDrawing`、`TopText`/`BottomText`、`DisplayImageInfo`、`HWindow`、`IsPickMode`、`Annotations`。

**手势链路的两个既有约定**（改画布行为前必读，见 AGENTS.md 第 16 节）：
① 未选中对象"一次手势就能拖"依赖控件在换绑 `ActiveRoi` 后**补发一次 down**；
② 摘除活动对象的容差是 14px（`RoiHitToleranceScreenPx`），比 HALCON 手柄（约 8px）宽，
而**挑选其它对象的命中测试保持无容差**（否则相邻框互相误选）。

## 六、投图与样式依赖

| 依赖 | 出处 | 用法 |
| --- | --- | --- |
| `PublishPreview(HImage, int viewIndex)` | `Shard/Core.Controls/Events/PluginPreviewExtensions.cs` | 扩展方法挂在 `IVisionPlugin` 上；**第二个参数就是窗口号，1~9 直传**，事件消费端按 `ViewIndex == 窗口号` 等值筛选（写 `+1` 会选"窗口1"进第 2 格、选"窗口9"哪格都不显示） |
| `IExecutionContext.Logger` | `Core.Interfaces` | 逐 ROI 跳过时写 Warn（`Warn` 不改变步骤成败，只留痕） |
| `Core.Halcon;component/Generic.xaml` | 视图 XAML 里就地合并 | `ImageEdit` 的控件模板；**不合并就没有模板，鼠标处理根本不注册** |
| UI 主题 `Generic.xaml` | 视图 XAML 里就地合并 | `Plugin*` 系列卡片/按钮样式 |
| `ObservableObject` / `RelayCommand` | `Core`（基类已继承 `ObservableObject`） | `SetProperty` 判同值、命令的 `CanExecute` 守卫 |
| Newtonsoft.Json | 基类快照 + `RoiItem` | `ParamEntries` 必须 `[JsonIgnore]`，否则反序列化抛"Unable to find a constructor" |

## 七、`.vms` 方案里本插件落下来的键

`InputValues` 里的键（名字 = 端口名/属性名，无映射）：

```
SrcImage            ← 端口值；若已变量链接则这个键会被 OnConfirm 移除
DisplayViewIndex    ← int，默认 1
MaskInvert          ← bool，默认 false
RoiList             ← [{ Name, ShapeType, Params[] }, ...]（JSON 深拷贝快照）
SmearDrawData       ← "行,起列,止列;…" 或 "gz:"+base64
SmearEraseData      ← 同上
```

另有步骤级的 `OutputPortDefinitions`：`Crop_x` / `Domain_x` / `OffsetRow_x` / `OffsetCol_x` 的
"名字 + `HImage`/`double` 的程序集限定名 + 描述"。

## 八、打包与工程约定（新增/改动必读）

| 约定 | 判据 |
| --- | --- |
| 插件工程必须在 `Plugins\` 下 | `Directory.Build.targets` 按目录把 DLL 投递到 `Modules\`，放别处加载不到（红线①） |
| 不得把 `Core.Interfaces.dll` 等公共契约程序集拷进 `Modules\` | 会出现两份静态状态（红线②）；`ParallelSafe` 按 FullName 字符串匹配就是为了绕开这个坑 |
| 改了插件源码必须**重建插件工程** | 只重建宿主跑的还是旧 DLL（红线③）；改生成器相关还要全量重建（R16） |
| 手册四条：`Help\` 内嵌资源、多插件类分册 `Help\{类名}\`、文件名 `NN-` 前缀、首行 `# 标题` | csproj 里要自己挂 `<EmbeddedResource Include="Help\**\*" />`（没有全局规则）；开发页用 `NN-dev-xxx.md` |
| **加完手册必须核 DLL 真的带资源** | 增量构建会静默漏打包（R42）；判据见下方代码块 |

```powershell
# 资源名数必须等于 Help\ 下的 .md 数（只看构建退出码不算数）
tr '\0' '\n' < Modules\Plugin.CreateRoi.dll | grep -a "\.Help\."
```

## 九、移植到新插件时的取舍

- **想复用"画布编辑"这套**：把 `CanvasRois` / `SelectedRoi` / `CanvasActiveRoi` 三件套和 `_seedingCanvas` 刹车一起搬，
  少一条就是"重开配置框翻倍"或"参数微调断链"。
- **不想做动态端口**：不实现 `IDynamicOutputProvider`、不 `AddDynamicOutput` 即可，其余契约照用。
- **纯配置项**（不进数据流的开关/模式）用 `[StepConfig]`，参与数据流的参数**一律声明为 `InputPort<T>`**——
  不要再写"草稿属性镜像端口"那一套。
