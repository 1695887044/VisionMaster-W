# 标定 · 开发：宿主契约与移植清单

> 分类：标定 ｜ 来源：插件 Plugin.Calibration ｜ 读者：开发

本页列**这个插件依赖宿主的哪些约定**（哪些行为不是它自己实现的，是基类 / 契约程序集 / 控件给的），
以及**契约对象 `CalibrationTransform` 的语义**和**它与下游「坐标变换」的耦合面**——
这两样是改标定时最容易"只改自己、把下游改坏"的地方。

## 一、继承 `VisionPluginBase` 白拿的行为（不要重复实现）

| 契约 | 宿主给的行为 | 本插件哪里靠它 / 哪里刻意偏离 |
| --- | --- | --- |
| `Execute(context)` | 进 `RunAlgorithm` 前预置 `Success=true`、清 `ErrorMessage`；轮首 `DisposePreviousRoundOutputs()`；异常统一转 `Fail("{Type}: {Message}")`；返回 `Success.Value is true` | 本插件**不依赖预置**：`RunAlgorithm` 第一行就把 `Success`/`ErrorMessage` 连同四个输出一起清零，语义是"失败轮必须没有产物" |
| `RethrowOnException`（默认 false） | 异常只落 `ErrorMessage`，不中断当轮 | 保持默认：读图/求解异常不该掀掉整条流程 |
| `AutoDisposeRoundOutputs`（默认 true） | 只回收 **`IDisposable` 的端口值**；`Success`/`ErrorMessage`（bool/string）天然不受影响 | `CalibrationTransform` 不是 `IDisposable` → 每轮 new 一个、旧值由端口自然替换。不要为了"省一次求解"把同一个实例长期挂在端口上 |
| `ApplyConfigValues(stepData)` | 遍历 `[StepConfig]` 成员，经 `ValueConverter.Convert(raw, targetType)` 赋值；**已链接的端口跳过**（连线优先于配置值） | 全部标定数据靠这一条从方案 JSON 回到属性；`partial` 钩子（含 `GridSize` 的 clamp）在装载路径上照样执行 |
| `Initialize(IStepConfigData)` | 基类实现 = 调 `ApplyConfigValues` | 本插件重写：先 `_disposed=false`、`base.Initialize`，再做旧方案迁移 / 对齐签名 / 铺默认表 / 载图 / 刷状态 |
| `OnConfirm(stepData)` | 未链接的输入端口 → `SetInputValue` 落盘；已链接 → `RemoveInputValue`（保留连线）；`[StepConfig]` 走 JSON 深拷贝快照 | 未重写。**注意**：`PointRows` 是引用类型，快照靠基类 JSON 深拷贝，所以界面改表格不会污染别的实例 |
| `OnCancel()` | 基类空实现（不改写步骤配置） | 未重写；关窗时的取点取消由视图 `Unloaded` 负责 |
| `Dispose()` | 释放端口持有的 `IDisposable` 值 | 重写并在末尾调用（顺序见「内部结构」第九节） |
| `InstanceName` / `Logger` | 日志前缀与流程日志通道 | 成功/失败日志都以 `InstanceName` 开头，多步骤方案里能分辨是哪一步 |

**未使用的宿主能力**：`IDynamicOutputProvider`（端口面固定）、`IPluginConfigContextProvider`
（不需要流程级相机/变量快照）、`AddDynamicOutput` / `ClearDynamicOutputs`。

## 二、契约对象语义（`Shard\Core.Interfaces\Calibration\CalibrationTransform.cs`）

契约层**零 HALCON 依赖**、全基元类型——它要随方案落盘、也可能进全局变量，所以 JSON 往返必须安全。
坐标与角度约定（全仓库统一，写死避免静默错位）：像素 `(Row, Col)`（Row 向下、Col 向右，与 HALCON 一致）；
机械 `(X, Y)` 单位 mm，轴方向/镜像/旋转**由矩阵自然吸收**，消费方不做额外假设；多点数组排布 `[x0,y0,x1,y1,…]`。

| 字段 | 语义 | 生产端注意 | 消费端注意 |
| --- | --- | --- | --- |
| `Kind` | `PixelScale=0` / `NinePoint=1` / `Perspective=2` / `Mesh=3` | 与 `CalibrationMode` 同序但**不同枚举**（模式是配置、Kind 是产物类型） | 必须 `switch` 全分支 + `default` 明确拒绝 |
| `MmPerPixel` | mm/px | PixelScale 直算；其余由矩阵基向量长度反解（透视取基准点局部值） | 只拿它做毫米换算，别拿它反推坐标 |
| `Matrix` | 6 元 `[a11 a12 a21 a22 a31 a32]`，映射 `(Row,Col)→(X,Y)`：`X=a11·Row+a12·Col+a31`、`Y=a21·Row+a22·Col+a32` | 透视/网格**保持零**（故意的失败设计） | 喂 HALCON 前必须重排（见下） |
| `ProjectiveMatrix` | 9 元 `[h11 h12 h13 h21 h22 h23 h31 h32 h33]` 行主序，`w=h31·Row+h32·Col+h33`，存储前按**带符号 h33 归一**（h33=1）；`|h33| < 1e-9` 在生产端就失败，不会产出 | 仅 `Perspective` 非 null | `w≈0` 是投影退化线，必须明确失败而不是返回 0 |
| `MeshNodes` | 长度 `N·N·4`，每 4 个一组 `[Row, Col, X, Y]`，**行主序**（组下标 = r·N + c）；存的是**实测**像素位置，不假设理想等距网格 | 仅 `Mesh` 非 null，且必须先过 `CalibrationMesh.IsUsable` | 凸包外**必须失败**（网格是插值，不外推） |
| `MeshSize` | N（`< 2` 或长度不符 = 契约损坏） | `Math.Clamp(GridSize, 2, 10)` | 不猜、不降级，直接报错 |
| `SourceImageWidth/Height` | 标定时的图尺寸（0 = 未记录） | 载图时记录、随步骤与文件落盘 | 尺寸失配判据；**两个 0 → 放行**（无从比对，不误报） |
| `CameraSerial` | 这份标定属于哪台相机 | 写入前 `Trim()` | 与上游序列号**两端都非空**才比对，忽略大小写 |
| `CreatedAtUtc` | 标定数据最后改动时间（UTC） | 取 `CalibrationStampUtc`（签名驱动，不是"运行时刻"） | 判"标定多久没更新"用 |
| `SourceTag` | 来源标签（标定图文件名 / 导入文件的标签） | `CurrentSourceTag()`：有路径取文件名，否则用 `_importedSourceTag` | 日志与人工核对 |
| `ResidualRmsPx` / `MaxResidualPx` | 像素口径 | Mesh 填的是**全局仿射基线**的值 | 别当"网格本身有多准"读 |
| `ResidualByRadiusBands` / `ResidualBandCounts` | 3 带（归一化半径 ≤0.4 / 0.4~0.7 / >0.7）均值与点数 | **空带记 0，绝不写 NaN**（NaN 会被序列化成非标准的 `"NaN"` 字符串，跨语言消费有风险） | **先看计数再看均值**，计数为 0 的带不参与形态判读 |

### 与 HALCON 的关系（2026-10-05 按本机 HALCON 23.05 实测订正）

`vector_to_hom_mat2d` 的 6 元排布是行主序 `[h11 h12 h13 h21 h22 h23]` = `[a11 a12 a31 a21 a22 a32]`，
与本契约 6 元**相差一次重排**（平移项 `a31` 提前到索引 2，`a21/a22` 顺移一位，`a32` 仍在索引 5）。
要喂 `affine_trans_point_2d` 一类算子必须先重排——**不存在"直喂不需要任何转换"**。
方向约定 `Px=Row`、`Py=Col` 成立。
9 元与旧 6 元（仿射子集）的换算：`9 元 = [a11, a12, a31, a21, a22, a32, 0, 0, 1]`
（**`a31/a32` 插在索引 2/5，不是拼在尾部**）。透视求解结果与 HALCON
`vector_to_proj_hom_mat2d` / `normalized_dlt` 做过对拍（h33 归一后逐元 < 1e-6）。

## 三、`CalibrationMesh`（契约层静态类）为什么必须两端共用

| 成员 | 语义 |
| --- | --- |
| `ValuesPerNode = 4` | 每组 `[Row, Col, X, Y]` 的固定长度（生产端按它铺数组） |
| `IsUsable(nodes, n, out error)` | 长度 / NaN / 逐格像素三角退化 / 逐格机械三角退化 |
| `TryMapPixelToXY` / `TryMapXYToPixel` | 三角化 2·(N−1)² 个三角形 → 重心坐标插值；出界给出含「不在网格覆盖范围内」的文案 |
| `TryLocalAffine` | 取该点所在三角形的局部仿射（下游要"局部雅可比"时用，别自己再算一套） |
| `TryMeanMmPerPixel` | 逐三角形边长比求平均当量（生产端用它填 `MmPerPixel`） |

**规则写在契约程序集里，生产端与消费端调同一份实现**：两处各写一套三角化/插值 = 同一份标定算出两套坐标，
而且只有现场才会发现。改这里的判据必须同时跑 `CalibrationChecks` 与 `PoseTransformChecks`。

## 四、与「坐标变换」的耦合面（改标定的产出前必看）

下游 `PoseTransformMath.CheckCalibrationUsable` 的分发**完全按 `Kind`**：

| 情形 | 下游文案（原文） |
| --- | --- |
| `Transform` 为空 | `未接标定：请把「标定」插件的 Transform 输出接到本步骤` |
| `Kind = PixelScale` | `当前标定只有像素当量、不含机械坐标：坐标换算请用九点标定 / 透视标定 / 网格标定（像素当量模式只给 mm/px）` |
| NinePoint 且 6 元不可用 | `标定数据损坏（矩阵缺失或退化）：请重新运行「标定」步骤` |
| Perspective 且 9 元不可用 | `标定数据损坏（投影矩阵缺失或退化）：请重新运行「标定」步骤` |
| Mesh | 由 `CalibrationMesh.IsUsable` 的文案决定（含"重新运行「标定」步骤"，下游不追加尾巴避免重复） |
| 未知 `Kind`（如测试造的 `(CalibrationKind)7`） | `不支持的标定类型（{Kind}）：请升级软件或重新标定` —— 这条是"防忘记分支静默放行"的闸门 |

再加两条失配（下游自己判，标定侧只提供元信息）：

- 尺寸：`标定与当前图像尺寸不符（标定时 {W}×{H}，当前 {w}×{h}）——换分辨率/换相机后需重新标定`，
  标定侧没记尺寸（≤0）则**放行**；
- 身份：`相机身份不符：这份标定属于「…」，但当前图像来自「…」；请换用该相机的标定文件（或检查 SourceSerial 接线）`。

**同一条序列号失配在两侧各查一次**（标定侧 `ComputeCalibration` 与坐标变换侧 `RunToMechanical`），文案不同：
标定侧失败在标定步骤，坐标变换侧失败在换算步骤。这不是重复劳动——标定步骤失败时下游根本拿不到 `Transform`，
两条检查覆盖的是"接线位置不同"的两种现场。改任何一条文案都要同时看 `CalibrationChecks` 与 `PoseTransformChecks`。

**新增模式的联动清单**（加 `CalibrationKind` 成员时逐条做）：契约 `Kind` → 生产端分支与产物字段填充 →
消费端 `switch` 分支与守卫文案 → 两侧各自的尺寸/身份自证字段是否仍然有效 → 两组断言 → 手册（本页表 + 用户册模式表）。

## 五、`[StepConfig]` + partial 属性钩子（R16 / R18 语境）

本插件的 `[StepConfig]` 成员多数写成 `partial` 属性，钩子由源生成器补出（约定见 AGENTS.md R16/R18，
生成器报错 CPG0001~CPG0005 一族）。钩子清单：

| 属性 | 钩子 | 作用 |
| --- | --- | --- |
| `Mode` | `OnModeChanged` | `CancelPick()`（取点目标语义已变）+ 通知 `ShowPointTable` + `RefreshStatus()` |
| `GridSize` | `OnGridSizeChanging(ref int)` | `Math.Clamp(value, 2, 10)`——**写前夹取**，装载/导入/界面三条路径统一生效 |
| `KnownLengthMm` / `ResidualThresholdPx` / `MmPerPixelMin` / `MmPerPixelMax` | `On…Changed` | 一律 `RefreshStatus()`（质量区与运行口径同步） |
| `ScaleARow` / `ScaleACol` / `ScaleBRow` / `ScaleBCol` | `On…Changed` | `SyncScaleMarker("A"/"B")` + `RefreshStatus()`（表格 → 画布的另一半） |
| `IsCalibrationLocked` | `OnIsCalibrationLockedChanged` | 通知派生属性 `IsEditable` + 写状态文案（锁定 = Warning） |
| `SourceImageWidth` / `SourceImageHeight` | `On…Changed` | 通知 `SourceImageSizeText`（只读展示，失配自证要让人核对） |
| `CameraSerial` / `CalibrationStampUtc` / `PointRows` | 无 partial 钩子 | `PointRows` 用自定义 setter + 集合/逐行订阅刷新 |

派生（非持久化）属性：`IsEditable => !IsCalibrationLocked`、
`ShowPointTable => Mode is NinePoint or Perspective or Mesh`、`CanvasImage => OnlineImage ?? DisplayImage`、
`PickHintText`、`SourceImageSizeText`、`GridSizeOptions = Enumerable.Range(2, 9)`（覆盖 clamp 区间，
否则载入旧方案 6×6 时下拉框空白）。

## 六、`[ParallelSafe]`：本插件声明了，凭什么

类上带 `[ParallelSafe] // 二期真并行：纯视觉计算`。宿主判据（`ParallelSafeAttribute` 注释里的四条口径）：
无未加锁的 `static` 可变字段、不碰进程级独占资源、不写全局共享状态（全局变量 / 全局图像槽）、
长阻塞要协作 `CancellationToken`。标定四条都满足：纯计算、微秒级、不阻塞。

两点必须写清：

- **并行安全 ≠ 运行期不写对象**。`RunAlgorithm` 会回写 `PointRows[i].ResidualPx` 与
  `ResidualAnnotations`；安全的前提是"每个步骤实例各自持有一份配置对象"，两个并行分支跑的是**不同实例**。
  要是有人把标定数据改成 `static` 缓存或跨实例共享，这条声明立刻变成谎话；
- 特性匹配按 **FullName 字符串**（不是 `typeof` 等值），就是为了容忍"两份 `Core.Interfaces.dll`"
  的程序集身份差异——那是 `Modules\` 多副本的产物，属红线②要禁的东西。

## 七、控件给的能力（画布侧）

| 通道 | 控件契约 | 本插件用法 |
| --- | --- | --- |
| `ImageEdit.HImage` | 绑定 `CanvasImage`（OneWay） | 实时图优先，文件预览兜底 |
| `ImageEdit.DrawObjectList` | `ObservableCollection<DrawingObjectInfo>`；移除项由订阅方释放 | `CanvasMarkers`（圆标记，`RoiName` = 行名/A/B） |
| `ImageEdit.ActiveRoi` | TwoWay，控件负责拖动手势与 `HTuples` 回传 | `CanvasActiveRoi`；拖动经 `OnMarkerChanged` 回写表格 |
| `ImageEdit.Annotations` | `MeasureAnnotation` 列表，随帧覆盖渲染 | `ResidualAnnotations`（残差箭头 ×50） |
| `HalconBase.IsPickMode` | **默认 false 的 opt-in** 一次性取点通道 | 仅 `IsPickingPoint` 为真时开启；`ImagePicked` 事件无法绑定，在 code-behind 转交 `ApplyPickedPoint` |
| Esc / `Unloaded` | 视图侧 | 都走 `CancelPick()`，防"待命态悬着、下一次点击写进孤儿行" |

标记半径 `Math.Clamp(Min(w,h)/60, 8, 48)`：固定像素半径在 5000px 宽的图上缩放后几乎看不见；
它只影响"看得见"，不参与任何计算。

## 八、试运行与"同一份代码"

配置窗口的「执行」与流程运行调的是**同一个 `ComputeCalibration()`**；试运行时宿主把当前图桥接进
`SourceImage`，于是"在实时图上取点"与"用这套数据跑一遍"共用一条路径。
**不要在配置态另写一份简化求解**（历史事故：界面绿、运行红的根源）。

## 九、移植清单（照它写新插件要带走的）

1. `VisionPluginBase` + `[Display]`（`Name` / `GroupName` / `Description` / `ShortName`）+
   `IPluginCustomViewProvider`；
2. 契约程序集 `Core.Interfaces`（`CalibrationTransform` / `CalibrationMesh`）——**只引用，不复制 DLL**；
3. 纯数学层与插件分文件（本插件 `CalibrationMath` 无 HALCON 依赖，断言可直接调用）；
4. `[StepConfig]` 的 partial 属性 + 源生成器（R16/R18）；集合型配置照 `PointRows` 的
   setter 重订阅 + `[JsonIgnore]` 只读判定写法；
5. 画布交互只用控件既有通道（`DrawObjectList` / `ActiveRoi` / `Annotations`），
   需要一次性取点才用 `IsPickMode`；
6. 导入/导出的两个 public 方法（供断言）+ 文件对话框包一层（供人）；
7. 与 `FlowCanvasChecks` 的对应断言文件（`CalibrationChecks.cs` 与消费侧 `PoseTransformChecks.cs` 成对维护）；
8. `Help\` 八页 + csproj 里的 `<EmbeddedResource Include="Help\**\*" />`。
