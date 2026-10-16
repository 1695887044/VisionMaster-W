# 标定 · 开发：内部结构与数据流

> 分类：标定 ｜ 来源：插件 Plugin.Calibration ｜ 读者：开发

本页讲**这个插件怎么组织、一份标定数据从表格走到端口再落盘的完整路径、图像与原生句柄归谁管**。
改这个插件前先读这页；写新的"配置态编辑 + 运行期求解"类插件，再读「宿主契约」与「不变量与陷阱清单」。

## 一、一个类兼任两个身份

```
CalibrationPlugin    : VisionPluginBase            ← 运行期算子（RunAlgorithm / 端口 / 配置生命周期）
                       IPluginCustomViewProvider   ← 设计期：交出配置视图（Plugin 与 ViewModel 合一）
CalibrationView      : UserControl                 ← 视图（DataContext = 插件实例本身）
```

没有独立的 ViewModel：界面绑的 `Mode` / `PointRows` / `QualityText` 全是插件实例上的属性。
这带来一条纪律——**插件对象的生命周期 = 配置窗口的生命周期**，所以 `_disposed` 与 `_previewLoadId`
两个守卫必须在 `Dispose()` 里先置位再清资源，异步读图的回程必须两个都查。

它**不实现** `IDynamicOutputProvider`（端口面固定：5 入 4 出 + 基类 2 个）也**不实现**
`IPluginConfigContextProvider`（标定图路径的"浏览 / 链接变量"由 `LinkableValueEditor` 直接吃 `StepData`，
不需要流程级相机/变量快照）。

## 二、文件与职责

| 文件 | 行数量级 | 职责 | 依赖 HALCON？ |
| --- | --- | --- | --- |
| `CalibrationPlugin.cs` | ~2450 | 端口 / `[StepConfig]` / 界面状态与命令 / `ComputeCalibration` 分支 / 配置态取点与标记 / 导入导出 | 是（`HImage`、`HTuple`、`DrawingObjectInfo`） |
| `CalibrationMath.cs` | ~870 | 纯数学：`TryPixelScale` / `TrySolveAffine` / `TrySolveProjective` / 残差与径向带 / 局部当量 / 可用性判定 | **否**（可脱离 HALCON 单测，断言直接调） |
| `Enum.cs` | ~60 | `CalibrationMode`（带 `[Display]` 中文名）、`StatusLevel`（绿/橙/红） | 否 |
| `Models/CalibPointRow.cs` | ~120 | 表格一行：四个可空坐标 + `IsEmpty` / `IsFilled` / `IsPartial` / `StateText` + 旧数据迁移 | 否 |
| `Models/CalibrationFile.cs` | ~110 | 导入导出文件的 DTO（`CalibrationExportFile` / `Point` / `Snapshot`） | 否 |
| `CalibrationView.xaml(.cs)` | 562 / 46 | 布局、绑定、取点事件转交、Esc 取消、`Unloaded` 取消待命 | 视图侧 `ImageEdit` |
| `Shard\Core.Interfaces\Calibration\CalibrationTransform.cs` | 158 | **跨插件契约**（四模式产物 + 失配自证字段） | 否（全基元类型，JSON 安全） |
| `Shard\Core.Interfaces\Calibration\CalibrationMesh.cs` | ~310 | 网格三角化 / 重心插值 / 局部仿射 / 平均当量 / 可用性判定（生产端与消费端**共用**） | 否 |

## 三、唯一一份计算

`ComputeCalibration()` 是**全站唯一**的求解入口，三个调用方都走它：

| 调用方 | 时机 | 拿什么 |
| --- | --- | --- |
| `RunAlgorithm(IExecutionContext)` | 运行期每轮 | 只用 `Succeeded`：成功才发布到端口，失败只写 `ErrorMessage` |
| `RefreshStatus()` | 任一配置项/表格/标记/路径变化 | 用 `Transform` 里的数字显示质量区（**闸门未过也显示**） |
| `BuildCalibrationExportJson()` | 点「导出标定」 | 先跑一次，让时间戳按签名落定 + 拿质量快照 |

所以"界面说能跑、一跑就失败"在结构上不可能发生。
`CalibrationAttempt` 把**"数学解出"与"闸门通过"分开**：

```
BlockingError != null  → 没有任何产物（输入不成立 / 求解退化）
GateError     != null  → 有 Transform，但判失败（残差超阈值 / 当量越界）
Succeeded              => BlockingError == null && GateError == null
Error                  => BlockingError ?? GateError   // 对外文案：阻塞优先
```

揉成一个 `bool` 的旧实现会把超阈值时的残差数字一起清掉——现场调点就失去收敛反馈（已修，有断言钉）。
**闸门顺序**：先残差、后当量（与历史实现一致，错误文案的优先级因此固定）。
PixelScale 分支里当量护栏是 `BlockingError`，NinePoint/Perspective/Mesh 分支里是 `GateError`——
因为前三者的数字是"结果属性"，后三者的当量是"派生读数"。

## 四、四条分支各自填什么

| 模式 | `Kind` | `Matrix`(6) | `ProjectiveMatrix`(9) | `MeshNodes`/`MeshSize` | `MmPerPixel` 来源 | 残差来源 | 闸门 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| PixelScale | 0 | `[mm,0,0,mm,0,0]`（占位） | null | null | `TryPixelScale` 直接算 | 无（不填） | 护栏判 `BlockingError` |
| NinePoint | 1 | 最小二乘解 | null | null | `DeriveMmPerPixel(Matrix)` | 求解器 RMS/最大/三带 | 残差 + 护栏（Gate） |
| Perspective | 2 | **保持默认零**（故意） | DLT + h33 归一 | null | 图像中心（或点集质心）的局部当量 | 求解器 RMS/最大/三带 | 残差 + 护栏（Gate） |
| Mesh | 3 | **保持默认零**（故意） | null | N·N·4 行主序 | `CalibrationMesh.TryMeanMmPerPixel` | **全局仿射基线**的 RMS/最大/三带 | **不查残差**，只查护栏 |

三条设计红线：

1. **透视/网格的 6 元 `Matrix` 必须是零**：未升级的消费者按 `det=0` 明确失败。
   把它"顺手填成近似仿射"会让边缘区域整体错位而无人报错。
2. **网格的基线只求不拦**：`TryMeshAffineBaseline` 失败也不阻断网格产物（基线只在判读畸变时有意义），
   `_meshAffineBaseline` 留 null → 逐行残差与箭头都不画。
3. **网格质量数字与逐行残差同口径**（都来自基线），否则界面与表格互相打脸。

## 五、输入签名与 `CalibrationStampUtc`

`BuildInputSignature()` 拼：`(int)Mode`、`GridSize`、`KnownLengthMm`、A/B 四个可空值（`null` 拼 `-`，
所以 null 与 0 必然不同串）、`CameraSerial`、`CurrentSourceTag()`、`W x H`、以及每行四个值。
**`SourceTag` 与尺寸在签名里**：换同尺寸的另一张标定图也算"标定数据变了"（标签会进产物）。

时间戳三条路径（`ComputeCalibration` 开头）：

| 路径 | 条件 | 动作 |
| --- | --- | --- |
| 首轮 | `_lastInputSignature == null` | **只记签名**；`CalibrationStampUtc == default` 时才补一个 now |
| 改动 | 已知且签名不同 | 推进为 `DateTime.UtcNow` |
| 重复运行 | 已知且签名相同 | 不动 |

为什么首轮不能推进：流程编译出的**运行实例只走 `ApplyConfigValues`、不经过配置界面的 `Initialize`**，
不区分的话每次运行的首个 `RunAlgorithm` 都会把方案里的标定时间刷成"刚刚"，下游"标定多久没更新"永远读不到真值。
两个配套点：`Initialize` 末尾 `_lastInputSignature = BuildInputSignature()`（打开方案不推时间戳）；
`ImportCalibrationJson` 置 `_lastInputSignature = null` 并以文件时间为准（导入后的首轮只记签名）。

## 六、配置态图像数据流

```
SourceImagePath.ValueChanged
      └─ EnsurePreviewLoaded()            // 与 PreviewImagePath OrdinalIgnoreCase 相同则直接返回（不重复读盘）
            ├─ 路径为空 → 只在「此前确有文件预览」时清 W/H；_previewLoadId++ 丢弃在途结果
            └─ LoadPreview(path)          // File.Exists 先行，后台 Task.Run new HImage().ReadImage
                  └─ PostToUI(...)        // loadId != _previewLoadId || _disposed → Dispose 丢弃
                        └─ 成为当前底图时 RefreshCanvasImageState()：记尺寸(>0 才写) + 清标记 + RefreshStatus
SourceImage.ValueChanged
      └─ RefreshOnlineImage()             // ActualValue.IsInitialized() 才认；同一引用不重复处理
            └─ RefreshCanvasImageState()  // 实时图优先；断开时回落文件预览（尺寸保留）
```

两条纪律：

- **尺寸"只增不清"是标定快照语义**：`RefreshCanvasImageState` 里 `w > 0` 才写；
  但**显式清空路径**（用户把标定图删掉）且没有实时图时**要**清成 0——"你主动把图标废了"与"图暂时不在场"是两回事。
- **换底图必清标记**：旧标记坐标对新图没有意义（`CanvasMarkers.Clear()`，被移除项由集合事件 `Dispose` 原生句柄）。

## 七、表格 ↔ 画布标记双向同步

| 方向 | 入口 | 写法 |
| --- | --- | --- |
| 拖标记 → 表格 | `DrawingObjectInfo.PropertyChanged(HTuples)` → `OnMarkerChanged` | 按 `RoiName` 找行（或 A/B）写 `ImageRow/ImageCol` |
| 表格 → 标记 | `CalibPointRow.PropertyChanged` → `OnPointRowChanged` → `SyncMarkerFromRow` / `SyncScaleMarker` | 值真的变了才写 `marker.HTuples`（别无谓动原生句柄） |

闭环靠 `_syncingMarkers` 布尔闸门（任何一方写入前置 true，`finally` 复位），否则一次拖动会来回触发多轮
`RefreshStatus`。`OnPointRowChanged` **只认四个坐标值**：`ResidualPx` 回写与派生属性变更不得再触发一轮求解
（那会变成"求解 → 回写 → 再求解"的自激）。

`PointRows` 集合本身的订阅要注意两条：集合 setter 里反序列化进来的行没有 `Add` 事件，必须
`RebindPointRowSubscriptions()` 先摘后挂；构造函数里给字段初始化的默认表补挂 `CollectionChanged`
（**字段初始化不走 setter**，缺这一步"改一格即时刷新"在从未过 setter 的实例上静默失效）。

## 八、取点通道（一次性）

`HalconBase.IsPickMode` 是**默认关闭的 opt-in** 通道，只有本插件在取点待命时开启：

```
PickRowPoint(row) / PickScalePoint("A"|"B") → ArmPick(target)   // 锁定态、无底图都拒绝
    → IsPickingPoint = true（画布 PickMode + 橙色横幅 + PickHintText）
    → 视图 ImagePicked 事件转 ApplyPickedPoint(row, col)          // 一次性：写完即 IsPickingPoint = false
CancelPick()：再点一次按钮 / Esc / 切模式 / 导入 / 重建 / 关窗（Unloaded）都会调
```

`ApplyPickedPoint` 直接写数据（守卫已过），行对象走 setter 顺带把画布标记同步过去。

## 九、图像与句柄的所有权链

| 对象 | 谁 new | 谁 Dispose | 说明 |
| --- | --- | --- | --- |
| `DisplayImage`（文件预览） | 本插件 `LoadPreview` | 本插件：属性 setter 换图后释放被换下的旧图；`Dispose()` 里置 null | 所有权在插件 |
| `OnlineImage`（= `SourceImage` 端口值） | 上游（采集/图像赋值） | **本插件绝不 Dispose**：只断引用 | 借入引用，`ReferenceEquals` 相同不重复处理 |
| `CanvasMarkers` 里的 `DrawingObjectInfo` | `CreateMarker` | `OnCanvasMarkersChanged` 对 `OldItems` 逐个 `Dispose` | 换图/重建/粘贴/清空都靠这里兜住，否则每换一次图漏一批原生句柄 |
| `Transform.TypedValue`（`CalibrationTransform`） | `ComputeCalibration` 每轮 new | 非 `IDisposable`，由基类换值/`Dispose` 释放端口 | 下游拿到的是**每轮新对象**，不是共享可变实例 |
| 残差箭头 `ResidualAnnotations` | `RebuildResidualAnnotations` new `List<MeasureAnnotation>` | 交给 `ImageEdit.Annotations` 通道，插件不持有 HALCON 对象 | 只读派生数据 |

`Dispose()` 顺序（照抄即对）：`_disposed = true` → `_previewLoadId++` → 摘逐行 / 集合 / 两个端口订阅 →
摘标记订阅 + `CanvasMarkers.Clear()`（借集合事件释放句柄）→ `OnlineImage = null`（只断引用）→
`DisplayImage = null`（setter 释放）→ `base.Dispose()`。
**摘订阅这步不是洁癖**：`CalibPointRow` 可能被配置快照外部引用，留着订阅会让已释放的插件被回调。

## 十、持久化与装载（标定数据怎么进出磁盘）

两层持久化，别混淆：

**① 随方案落盘（步骤配置）**：所有 `[StepConfig]` 成员 → 步骤 JSON → 装载时基类 `ApplyConfigValues`
逐属性调 `ValueConverter.Convert(rawValue, targetType)`：

| 成员 | 落盘形态 | 装载注意点 |
| --- | --- | --- |
| `Mode`（枚举） | JSON **数字** | 枚举以数字落盘，回读是 `Int64`：`ChangeType` 不认"数值→枚举"，转换器里专门处理 |
| `GridSize` / `KnownLengthMm` / 阈值 / 护栏 | 数字 | `OnGridSizeChanging` 的 `Math.Clamp(2,10)` 在**装载路径上也生效**（partial 钩子拦 setter） |
| `ScaleARow` … `ScaleBCol` | `double?`（可为 `null`） | null 与 0 必须落盘可区分，否则"未取点"与"边缘坐标 0"混淆（本轮修的核心语义） |
| `PointRows` | 对象数组（行含四个可空值 + `Name` + `ResidualPx`） | 走 `JToken.ToObject(类型)`；`[JsonIgnore]` 的 `IsFilled` 等只读判定不参与往返 |
| `IsCalibrationLocked` / `CameraSerial` / `CalibrationStampUtc` / `SourceImageWidth` / `SourceImageHeight` | 值类型/字符串 | 锁定与尺寸**也落盘**——它们是配置，不是本机临时态 |

旧方案迁移放在 `Initialize` 里（`NormalizeLegacyAllZero()` 把"四值全 0"还原成空行并计数提示），
**不放进 `ValueConverter`**：迁移语义只属于标定插件，不该污染全局转换器。
运行实例不经过 `Initialize` ⇒ 迁移只在配置态发生；因此运行期读到的 `PointRows` 若仍是旧格式，
"四值全 0"的行会被当成**有效点**（坐标 0）而不是空行——这条差异写在断言里，改迁移逻辑前先跑 `CalibrationChecks`。

**② 标定文件（跨设备）**：`BuildCalibrationExportJson` / `ImportCalibrationJson` 两个 public 方法
（public 只为让断言直接调，命令入口另走文件对话框）。
`ExportFormatVersion = 1`，字段面 = 标定数据 + 质量策略 + 元信息，**不含**锁定开关 / 预览图路径 / 画布状态。
导入端"拒绝面先于任何改动"：空内容、非法 JSON、`Version` 越界、`Mode` 不在 0~3、缺 `Points`、
点里含 null 行、阈值或护栏非有限/负数——全部在改本机数据**之前**查完，失败不留半填。
成功路径的三个不显然动作：

- `_importedSourceTag = file.SourceTag`：导入端没有图路径，来源标签得跟着文件走，本机载新图后由路径接管；
- 尺寸回落顺序：文件有 → 采信文件；没有 → 当前画布底图尺寸 → 本机原值。**绝不写 0**
  （0 会让下游 `CheckImageSizeMatch` 直接放行，换错分辨率的静默错位从此无人拦）；
- 行数 ≠ `GridSize²` 时以行为准归一（`Ceiling(Sqrt(count))`），行名统一重排 P1..PN
  （文件里的旧名不保留，避免重名时按名匹配错行）。

## 十一、装载与 `Modules\`（红线②在本插件为什么特别致命）

`CalibrationTransform` / `CalibrationMesh` 在 `Core.Interfaces`（契约程序集），插件与「坐标变换」都只引用契约。
一旦 `Modules\` 里多出第二份 `Core.Interfaces.dll`（AGENTS.md 红线②禁止），后果是：

- `OutputPort<CalibrationTransform?>` 的类型身份在两份程序集里不同 → 跨插件连线在运行期对不上；
- `FlowCompiler` 的 `[ParallelSafe]` 匹配按 FullName 字符串（正是因为这个场景才不用 `typeof` 等值）；
- 网格的三角化/插值出现两套实现 → 同一份标定在生产端与消费端算出两套坐标。

改完手册或代码后按 R42 验一遍（增量构建会漏内嵌资源）：

```bash
tr '\0' '\n' < "Modules/Plugin.Calibration.dll" | grep -a "\.Help\." | sort
# 资源名条数必须等于 Help\ 下的 md 条数（本工程 8 页）
```
