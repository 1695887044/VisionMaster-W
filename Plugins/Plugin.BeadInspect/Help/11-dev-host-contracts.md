# 胶路检测 · 开发：宿主契约与移植清单

> 分类：缺陷检测 ｜ 来源：插件 Plugin.BeadInspect ｜ 读者：开发

本页列**这个插件依赖宿主的哪些约定**（哪些行为不是它自己实现的），以及移植/接手时要带走的清单。

## 一、继承 `VisionPluginBase` 白拿的行为

| 契约 | 宿主给的行为 | 本插件哪里靠它 |
| --- | --- | --- |
| 端口发现 | 反射公开实例属性收集 `IInputPort` / `IOutputPort`；**端口名为空时自动用属性名** | 本插件每个端口都**显式传了名字**（`new("SrcImage", …)`），因为输入侧 `AlignedImage` 与输出侧 `AlignedImage` 同名，属性名只能靠 `In` 后缀区分——名字以构造参数为准 |
| 默认成功 | `Execute` 进 `RunAlgorithm` 前预置 `Success=true`、清 `ErrorMessage` | 正常路径一行 `Success` 都不写；NG 也只是 `IsOk=false` |
| 异常转失败 | 未捕获异常 → `Fail("{Type}: {Message}")`；`RethrowOnException`（默认 false）决定是否继续上抛 | 本插件自己在 `RunAlgorithm` 里 `catch` 成 `Fail("胶路检测失败：" + ex.Message)`，为的是话术里带"胶路检测"主语 |
| 轮首回收输出 | `AutoDisposeRoundOutputs`（默认 true）在 `RunAlgorithm` 前释放输出端口里的 `IDisposable` 旧值并置空；透传防御跳过与上游同引用的输出 | `AnnotatedImage` / `AlignedImage` / `ErrorSegments` / `BeadContours` 全交给它；**标量与 `HTuple` 端口不归它管**，所以轮首要手动重置 12 个 |
| 统一灌值 | `ApplyConfigValues`：`InputValues` → 输入端口（已链接的跳过）+ `[StepConfig]` 属性（类型不匹配宽容跳过） | 本插件**重写**了它（见第二节第 1 条） |
| 默认确认 | `OnConfirm`：未链接端口值回写 `InputValues`、已链接端口 `RemoveInputValue`；`[StepConfig]` 属性逐个快照后写入 | 本插件重写：先序列化 `Library` 再 `base.OnConfirm`，快照的因此是那个 JSON **字符串**（原始值直存，不走深拷贝） |
| 配置快照 | `SnapshotConfigValue`：基元/string/枚举/decimal 直存，其余 JSON 往返拷贝 | `AlignMode` / `Polarity` / `UnitOutput` 是枚举，直存枚举值 |
| 默认取消 | `OnCancel` 空实现 | 未重写：试运行读的是已保存的 `InputValues`、不落盘 |
| 生命周期 | `Dispose` 释放所有输出端口承载的 `IDisposable`，单个失败不影响整体 | 本插件 `Dispose` 先清库句柄、配置态、渲染器，**末尾必须 `base.Dispose()`** |
| 并行安全 | `[ParallelSafe]`；`FlowCompiler` 按类型 **FullName 字符串**匹配该特性（绕开"两份 `Core.Interfaces.dll`"导致身份不一致） | 已标注：纯视觉计算、无 static 可变字段、实例隔离 + HALCON 局部对象 |
| 投图 | `PublishPreview(HImage, int viewIndex)` 扩展方法（`Shard/Core.Controls/Events/PluginPreviewExtensions.cs`），第二参就是窗口号 1~9 **直传** | `DisplayViewIndex` 原值传，不做 `+1` |
| 日志 | `IExecutionContext.Logger`（`ILogService`） | 每轮 Info 结果行、对齐 score、所有 Fail 都补一条 Error；释放句柄失败也走它 |

## 二、本插件自己重写的四个钩子（顺序即语义）

1. **`ApplyConfigValues(stepData)`**：`base` 灌完 `[StepConfig]` 后，逐条释放 `Library` 里旧句柄 →
   `Clear()` → 反序列化 `RecipeLibraryJson` → 重新 Add。
   为什么必须在这里：流程编译器对运行实例**只调 `ApplyConfigValues`，不调 `Initialize`**；
   解析放别处，运行内核面对的就是空库（`Matching` 实测过的坑，本插件照抄其范式）。
   库 JSON 损坏按**空库**处理（`catch` 掉），方案文件不该因为一段坏文本打不开。
2. **`OnConfirm(stepData)`**：`RecipeLibraryJson = JsonConvert.SerializeObject(Library)` 后再 `base.OnConfirm`。
   顺序反了就会把旧 JSON 快照进 `InputValues`。
3. **`Initialize(stepData)`**：先 `MarkAsConfigInstance()`（"先盖章再灌值"），再 `base.Initialize`，
   然后播种编辑条目：`DefaultRecipeName` 命中的那条 → 库首条 → 空库时新建 `配方1`。
4. **`Dispose()`**：遍历 `Library` 释放两类句柄 → `Clear()` → `DisposeConfigState()`（退订两个 `ValueChanged`、
   清底图/预览/表格/撤销栈/参考图缓存）→ `_renderer.Dispose()` → `base.Dispose()`。

## 三、实现的接口与它们各自的调用时机

| 接口 | 成员 | 说明 |
| --- | --- | --- |
| `IVisionPlugin`（基类给） | `RunAlgorithm` / `Initialize` / `Dispose` / 端口集合 | 运行期算子身份 |
| `IPluginConfigView`（基类给默认实现） | `Initialize(IStepConfigData)` / `OnConfirm` / `OnCancel` | 本插件重写前三个中的两个（`Initialize`、`OnConfirm`） |
| `IPluginCustomViewProvider`（配置态 partial 实现） | `object GetConfigView(IStepConfigData stepData)` | 宿主 `PluginConfigShell` 打开配置时调；返回 `BeadInspectView`，`DataContext` 就是插件本身（VM 即插件，无 DI、无独立 ViewModel） |
| 无 `IDynamicOutputProvider` | — | 端口面**固定** 5 入 16 出，没有动态端口：配方再多也不长端口，`RecipeName` 是数据口而不是端口名 |

> 试运行**不在接口里**：由主程序 `PluginTestRunner` 统一执行 `RunAlgorithm`，插件不必为试运行写第二套代码。

## 四、`IStepConfigData` 提供的能力（配置视图的写路径）

| 成员 | 本插件哪里用 |
| --- | --- |
| `InputValues` | 20 个 `[StepConfig]` 属性与 5 个输入端口的值都落在这个字典里（键 = 名称，无映射层），合计 **25 个键**。**配置会话属性不进这里**（见第五节末条） |
| `SetInputValue` / `RemoveInputValue` | 由基类 `OnConfirm` 调用；写它会触发变更通知 → 流程版本递增 → 重编译 |
| `IsLinked(port)` / `GetLinkedAddress(port)` | 「⓪ 输入绑定」卡的 5 个 `LinkableValueEditor` 依赖它显示"已链接到 Global.XXX"；`ApplyConfigValues`/`OnConfirm` 都按它跳过已链接端口 |
| `GetLink` / `SetLink` / `RemoveLink` | 连线语义（值由对方提供）；`RecipeName` 这种"给个名字字符串"的口正是靠它接上游变量 |
| `OutputPortDefinitions` | 本插件没有动态端口，因此**不写这张表** |

## 五、`[StepConfig]` 的语义边界与本插件的写法

`[StepConfig]` = **参与 `InputValues` 持久化与灌值，但不暴露为端口**（不进数据流、不可变量链接）。

- 写法三选一（仓库纪律 R18）：自动属性 / partial property + 生成器钩子 / 保持手写。
  本插件里 `TargetWidth`、`MinScore`、`PlanarNumLevels`、`FindAngleStartDeg`、`FindAngleExtentDeg`、
  `FindScaleRMin/RMax/CMin/CMax` 是 **partial property**，声明由 `Core.PluginGenerators` 生成。
  前三者带 `On…Changing(ref value)` **夹紧**钩子（6~10000 / 0~1 / 1~10），后四者只标 `[DefaultValue]`。
  ⇒ 改了生成器必须重建所有插件工程（R16）；partial 属性所在类必须声明 `partial`。
- `RecipeLibraryJson` 是"整库一坨 JSON"的存盘面，`Library` 是内存真源；
  **`BeadRecipeEntry` 的运行期槽位全部标 `[JsonIgnore]`**（`RuntimeBeadModel`、`RuntimePlanarModel`、
  `RuntimeRowT/ColT`、`RuntimeRectifiedRef`、`RuntimePlane`、`RuntimeContour` 和两个 `…Source`），
  否则 HALCON 句柄会被序列化进方案。
- **改属性名 = 静默丢配置**：`InputValues` 的键就是属性名，没有映射层。
- **配置会话属性（不是 `[StepConfig]`）**：`ExtractImagePath`、`ExtractTargetPoints`、`RectifyQuadJson` 的界面草稿态、
  状态栏与横幅字段等只活在配置实例上。它们**不进 `InputValues`**，`OnConfirm` 也不会写键——
  `ExtractTargetPoints` 因此「重开配置界面回到默认 40」是**设计行为**，不是丢配置。
  给这类属性补界面入口**不需要**改插件，但必须同步：
  ① 视图绑定路径（`FlowCanvasChecks\BeadInspectChecks.cs` 的 `[BeadInspect·v2]` 静态扫描）；
  ② 断言「`OnConfirm` 后 `InputValues` 仍是 25 键且不含该键」——否则有人顺手加个 `[StepConfig]` 就会静默改落盘契约。

## 六、共享件 `AnnotationBaker`（离屏烘焙唯一入口，R36/R37）

```
本插件（OffscreenRenderer）只做两件事：
  AddLayer(plan, referencePath, "orange", 1)   参考路径
  AddLayer(plan, beadContours,  "green",  1)   本轮左右轮廓
  AddLayer(plan, errorSegments, "red",    2)   错误段加粗
  plan.AddTextBlock(lines, ok ? "green" : "red", 12, 12, 16, box: true)
  → _baker.Bake(baseImage, plan)
```

- **本插件不开窗、不 `dump_window_image`、不管窗口复用**：六条硬纪律（`'buffer'` 开窗、
  窗口按尺寸常驻复用防 #9302、每轮无条件 `SetPart`、文字只用 window 坐标、`DispText` 显式给 box、
  回读源对象立刻释放）全在共享件里，禁止再复制第二份烘焙件。
- `Bake` **永不抛**：整体失败返回 null（只回调告警），单个元素画不出来只丢那个元素——
  所以 `AnnotatedImage` 可能为空而判定数值完好。
- 色名走共享件的白名单校验（`green/red/orange/yellow/cyan/magenta/white`），非法色名不会把整张图带走。
- `null`/未初始化的图层**不进计划**（`AddLayer` 首行挡掉）；空对象集不挡，画它是合法的，共享件会静默跳过。

## 七、画布与控件契约

| 依赖 | 用法 | 边界 |
| --- | --- | --- |
| `Core.Halcon` 的 `ImageEdit`（`h:` 命名空间） | 底图 `DisplayImage`、叠加 `PathAnnotations` | 视图**不改共享控件源码**：`Loaded` 后经视觉树找内部 `HSmartWindowControlWPF`，**叠加**订阅 `HMouseDown/Move/Up` |
| `HSmartWindowControlWPF.HMouseEventArgsWPF` | 事件自带图像坐标 `Row/Column`，无需自己做 `SetPart` 缩放换算 | 实例级设 `HMoveContent = false`（左键拖动留给拖点），实例级 `ContextMenu = null`（右键让给删点） |
| `MeasureAnnotation` / `MeasureType.Line` | 路径/点/四角叠加层 | 每次**整体换新 List 实例**触发重画，不直接画 `HWindow`，避免与控件内部重绘互相覆盖 |
| `PathEditor`（`Core.Halcon.Geometry`） | 插点/删点/命中（容差 50px 删、12px 拖） | 纯几何，可断言；"插到最近线段之间、走向不折返"由它保证 |
| `LinkableValueEditor`（`Core.Controls`） | 5 个输入端口的链接编辑器 | XAML 必须声明 `Core.Controls` 命名空间，否则闸门红 |
| `EnumDisplayNameConverter` | 「对齐模式」「输出单位」下拉显示中文（来自枚举 `[Display(Name=…)]`） | 极性下拉显示 `dark`/`light` 是**算法字面量本身**，不是缺 `[Display]` |
| UI 主题 `Generic.xaml` 的 `Plugin*` 样式 | 卡片标题、字段行、按钮 | 视图里不许给 `h:` 控件挂内联 Style、不许硬编码颜色（各有断言） |

## 八、工具箱显示

插件类上的 `[Display]`：`Name = "胶路检测"`、`GroupName = "缺陷检测"`、`ShortName` 是一个图标字形。
手册页眉的「分类」必须与 `GroupName` 实值一致（本页与其余 7 页都写「缺陷检测」）。

## 九、`.vms` 方案里本插件落下来的键

`InputValues` 的键（名字 = 属性名/端口名，共 **25** 个）：

```
RecipeLibraryJson     配方库整表 JSON（默认 "[]"）
DefaultRecipeName     默认配方名
AlignMode / Polarity / UnitOutput        枚举值
TargetWidth / WidthTolerance / PositionTolerance / MinErrorLength / MinScore / PlanarNumLevels
FindAngleStartDeg / FindAngleExtentDeg / FindScaleRMin / FindScaleRMax / FindScaleCMin / FindScaleCMax
DisplayViewIndex / OutputAlignedImage / PixelSizeMm
SrcImage / RecipeName / AlignedImage / RoiRegion / Transform   端口值（已变量链接的会被 OnConfirm 移除）
```

**不在 `.vms` 里的配置态属性**（有界面入口但不落盘）：

```
ExtractImagePath       自动提取用的良品图路径（教学会话数据）
ExtractTargetPoints    抽稀目标点数（10~500，默认 40）
```

这两个都由配置界面持有、重开配置界面即回到默认；加它们**不改变 25 个键**。
`BeadInspectChecks` 的 `[BeadInspect·v2]` 断言同时钉住「4 个 `[StepConfig]` 落盘」与「`ExtractTargetPoints` 不写键」。

`RecipeLibraryJson` 里每条配方的字段名（落盘的唯一口径）：
`Name`、`RefImagePath`、`RefNoBeadImagePath`、`RefPointsJson`、`RectifyQuadJson`、
`TargetWidth`、`WidthTolerance`、`PositionTolerance`、`Polarity`、`LearnedSignature`。
**模型句柄不落盘**——换机器、换路径、覆盖参考图后第一次运行按指纹重建，属预期行为。

## 十、移植与工程约定

| 约定 | 判据 |
| --- | --- |
| 插件工程必须在 `Plugins\` 下 | `Directory.Build.targets` 按目录把 DLL 投递到 `Modules\`（红线①） |
| 不得把 `Core.Interfaces.dll` 等公共契约拷进 `Modules\` | 两份静态状态（红线②） |
| 改了插件源码必须**重建插件工程** | 只重建宿主跑的是旧 DLL（红线③） |
| 手册已在 csproj 内嵌 | csproj 里已有一条 `EmbeddedResource`，把 `Help\` 目录整棵子树编进本程序集；单插件程序集**扁平一层**，不许开子目录 |
| 加完手册必须核 DLL 真带资源 | 增量构建会静默漏打包（R42），判据见下方代码块 |
| 开发章节行数档位 | 本插件是 **L 档**：用户页 5（含界面页）+ 开发页 3，开发合计 ≥300 行（HM13） |

```powershell
tr '\0' '\n' < Modules\Plugin.BeadInspect.dll | grep -a "\.Help\."
```

**照它写"学习—检测"类插件要一起搬的三件事**：
① 句柄按指纹惰性重建 + 计数器诊断面（别把模型塞进方案）；
② 配置态 `MarkAsConfigInstance` 与 `ApplyConfigValues` 分离（否则运行实例拿到空库）；
③ 失败话术"现象 + 下一步"、NG 不算失败这套判定语义。
