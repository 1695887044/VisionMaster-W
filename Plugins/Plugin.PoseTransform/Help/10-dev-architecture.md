# 坐标变换 · 开发：内部结构与数据流

> 分类：定位 ｜ 来源：插件 Plugin.PoseTransform ｜ 读者：开发

本页讲**这个插件是怎么组织的、两种模式各走哪条路、一份标定数据怎么从上游走到机械坐标**。
改这个插件之前先读这页；要照它写一个新的"纯计算 + 契约消费"类插件，再读「宿主契约」与「不变量与陷阱清单」。

## 一、一个类兼任两个身份，但只重写三个方法

```
PoseTransformPlugin  :  VisionPluginBase          ← 运行期算子（RunAlgorithm / 端口 / 配置生命周期）
                        IPluginCustomViewProvider ← 设计期：交出配置视图 GetConfigView(stepData)
                        ObservableObject          ← 它同时就是这个配置界面的 ViewModel
```

`PoseTransformView.xaml.cs` 里**只有构造函数**（`DataContext = plugin` + `plugin.Initialize(stepData)` +
`OnViewAttached()` + `Unloaded → OnViewDetached()`）。这是刻意的：VM 就是插件本身，视图是纯绑定层，
不需要额外的 ViewModel 类、也不需要 DI 容器。

代价是"插件实例"活得和配置会话一样长，所以**所有非托管 HALCON 对象都必须在它自己的 `Dispose()` 里收尾**（见第七节）。

全类只重写三个基类方法：`Initialize(IStepConfigData)`、`Dispose()`、`RunAlgorithm(IExecutionContext)`。
**没有**重写 `OnConfirm` / `OnCancel` / `ApplyConfigValues`（基类默认够用，见「宿主契约」页），
**没有**实现 `IDynamicOutputProvider`（端口面固定，不随配置增减），
**没有**重写 `RethrowOnException` / `AutoDisposeRoundOutputs`（两者都吃基类默认值）。

## 二、三层拆分：为什么数学要单独一个类

| 层 | 文件 | 职责 | 依赖 |
| --- | --- | --- | --- |
| 插件层 | `PoseTransformPlugin.cs`（958 行） | 端口、模式分派、失配把关、配置态三联刷新、生命周期 | 下面两层 + HALCON |
| 纯托管数学 | `PoseTransformMath.cs`（450 行） | 标定可用性判定、正/反点换算、角度换算、归一化 | **零 HALCON 依赖** |
| HALCON 作用 | `PoseTransformHalcon.cs`（60 行） | 只对区域/图像做刚体仿射：`FollowRegion` / `FollowImageBack` | `HOperatorSet` |
| 枚举 | `Enum.cs`（41 行） | `TransformMode`（上屏，带 `[Display]`）、`StatusLevel`（内部信息栏配色，不上屏） | — |

`PoseTransformMath` 的类注释把这条拆分的理由写死了：**"零 HALCON 依赖 → 断言可直接构造输入跑，不依赖引擎"**。
`FlowCanvasChecks\PoseTransformChecks.cs` 的数学层分节（起于 `[PT] 正变换按契约展开（(100,200) → (110,220)，手算常量）`、
止于 `[PT] 像素点守卫：(0,0)/NaN 必须失败（与位姿守卫同口径）`）**全部不经插件、直接调 `PoseTransformMath` 的静态方法**，
所以闸门不需要 HALCON 运行环境就能覆盖大半套公式。
新增公式请继续放这一层，别放进插件类——放插件类里就断言不了。

同理 `PoseTransformHalcon` 独立成层，是为了让断言能直呼"给基准 ROI + 一组位姿 → 拿跟随 ROI"，
不必把整块插件实例架起来。这一层的四条是：
`[PT] 跟随姿态敏感：转 90° → 外接框宽高互换（41×21 → 21×41，±1px）`、`[PT] 旋转不丢面积（相对差 < 1e-6）`、
`[PT] 模板态自洽：位姿=参考点、角度 0 → 与基准 ROI 完全重合（面积相对差 < 1e-6）`、
`[PT] 对齐图方向：内容从位姿搬回参考点（T⁻¹，不是推远两倍）`。

## 三、四种标定，两条消费路线

`Transform` 端口的类型是契约程序集里的 `CalibrationTransform`，它带一个 `Kind`（`CalibrationKind`）。
本插件按 `Kind` 分派，**分支必须穷尽、未知值明确拒绝**：

| `Kind` | 携带的数据 | 点换算实现 | 角度换算实现 | 本插件是否接受 |
| --- | --- | --- | --- | --- |
| `PixelScale` | 只有像素当量 | — | — | **拒绝**（没有机械坐标系可映射） |
| `NinePoint` | `Matrix` 6 元 `[a11 a12 a21 a22 a31 a32]` | 仿射直接乘 | 全局线性部分 | 接受 |
| `Perspective` | `ProjectiveMatrix` 9 元 | 投影除法（`w≈0` 失败） | 该点局部雅可比 `J(p)` | 接受 |
| `Mesh` | `MeshNodes` + `MeshSize` | 三角化定位 + 重心插值 | 所在三角形的仿射雅可比 | 接受 |
| 其它（如 `(CalibrationKind)7`） | — | — | — | **拒绝**（`default` 分支防"忘记补 case"静默放行） |

两条消费路线按模式分家，**互不相干**：

- **`FollowRoi` 与标定完全解耦**：走 `PoseTransformHalcon`，只吃 `BaseRegion` + 参考点 + 位姿。
  断言 `[PT] FollowRoi 与标定类型解耦：九点/透视输出完全一致、不接标定照常成功` 就是钉这条的——
  哪天有人"顺手"在跟随分支里加一句标定校验，这条会红。
- **`ToMechanical` 才碰标定**：走 `PoseTransformMath`，四类失配全部过完才写端口。

## 四、`RunAlgorithm` 逐步（含全部早退出口）

```
RunAlgorithm(context)
 ├─ Success.Value = true; ErrorMessage.Value = string.Empty;      // 与基类 Execute 的预置一致（见第九节）
 ├─ 五个 double 输出端口清零：MechanicalX / MechanicalY / MechanicalAngle / PixelEchoRow / PixelEchoCol
 ├─ switch (Mode)
 │    ├─ FollowRoi      → RunFollowRoi(context)
 │    ├─ ToMechanical   → RunToMechanical(context)
 │    └─ default        → Fail($"未知模式：{Mode}（请重新选择模式）")
 └─ if (_viewAttached) PostToUI(RefreshOverlay);                   // 配置对话框开着才刷叠加图
```

### 4.1 `RunFollowRoi`：先算完两个产物，全部成功才写端口

1. `BaseRegion.ActualValue` 为 null 或 `!IsInitialized()` → `Fail("基准 ROI 为空：请把 ROI 插件的 MaskRegion 接过来（并在模板图上画好基准）")`；
2. `TryRegionArea` 取面积，`area <= 0` → `Fail("基准 ROI 是空区域（面积为 0）：请检查基准 ROI 的绘制（要画在模板图上）")`；
3. **定参考点**：`TemplateRefFromRegionCenter == true` → 直接取基准 ROI 中心（`AreaCenter`）；
   `false` → 读 `TemplateRefRow` / `TemplateRefCol` 端口，非有限值 → `Fail("模板参考点无效（NaN/Inf）：…")`，
   两项同时为 0 → `Fail("模板参考点为 (0,0)：…")`；
4. `PoseTransformMath.IsValidPose(PoseRow, PoseCol, PoseAngle)` → 不合格就把它给的文案原样 `Fail`；
5. `try { FollowRegion(...) → 跟随 ROI;  if (EmitFollowedImage) FollowImageBack(...) → 对齐图 }`
   `catch` → **把已算出的两个产物都 `Dispose`** 再 `Fail($"位姿跟随失败：{ex.Message}（请检查基准 ROI、SrcImage 与位姿是否有效）")`；
   ——这就是"失败不留半成品"：中途炸了，端口保持第 0 步的清零值，下游读不到半根 ROI；
6. `FollowedRegion.TypedValue = followed`（**所有权移交端口**），`followedImage != null` 时才写 `FollowedImage`；
7. `EmitFollowedImage` 勾了但 `SrcImage` 为空 → **只记一条 Info 日志、对齐图不输出，跟随 ROI 照常给**
   （这不是失败，`context.Logger?.Info($"{InstanceName} 未接 SrcImage：跳过输出对齐图")`）；
8. 成功日志：`位姿跟随：基准面积 …px² → 跟随面积 …px²（±X%）`——面积相对差是**运行期的自诊断**，
   正视平面应接近 0%（`nearest_neighbor` 栅化取整的微小差异）。

### 4.2 `RunToMechanical`：四道失配闸门 + 回显自校验

顺序**有意义**（先便宜后昂贵、先"配置错"后"数据错"），别调换：

1. `CheckCalibrationUsable(Transform)` → 失配①未接、②只有像素当量、矩阵/投影/网格损坏、未知 `Kind`；
   二期③起还含**内参四判据**（仅 `*Undistorted` 三个 Kind 生效，见第六节）；
2. **失配③图像尺寸**：只在 `SrcImage` 非空且已初始化时才查（不接就不查），读尺寸本身抛异常 →
   `Fail($"读取图像尺寸失败：{ex.Message}（SrcImage 是否有效？）")`；
3. **失配④相机身份**：`SourceSerial` 与 `Transform.CameraSerial` **两端 Trim 后都非空**才比对，
   `StringComparison.OrdinalIgnoreCase`；任一为空 → 不检查（离线/单相机不打扰，旧标定文件没填序列号也不拦）；
4. `IsUsablePixelPoint(PixelPointRow, PixelPointCol)` → NaN/Inf 与 `(0,0)`；
5. `PoseTransformIntrinsics.TryUndistortForCalibration` → **`*Undistorted` 才真去畸变**（其余 Kind 恒等通过）；
   失败明确 `Fail`，**绝不退回"用原始像素凑合算"**（kappa=-1500 时角点会静默偏 ~23px）；
6. `TryMapPixelToMechanical` → 正向映射（透视分母≈0、网格越界都在这里失败）；
7. `TryMapMechanicalToPixel` → **把刚算出的机械坐标反算回像素**（得到的是**去畸变域**像素）；
8. `PoseTransformIntrinsics.TryDistortBack` → `*Undistorted` 时**再施加逆向畸变**才回到真实图像像素
   （漏这一步的后果实测：回显偏差 14.5px，与去畸变量同量级）；
9. 回显自校验：`max(|echoRow-pixelRow|, |echoCol-pixelCol|) > 容差` →
   `Fail("反向回显不一致（偏差 …px，容差 Tpx）：标定矩阵可能已损坏，请重新标定")`。
   **容差按模型分档**（`PoseTransformIntrinsics.RoundTripTolerancePx`）：
   非 Undistorted / division → `1e-6`，polynomial → `1e-3`（实测往返最坏 1.1e-4px，用 `1e-6` 会**假红**）；
10. **角度是"可选第六关"**：`hasAngle = PixelAngle.LinkedSource != null || pixelAngle != 0.0 || !double.IsFinite(pixelAngle)`。
   三个条件里带 NaN 是故意的——"值为 NaN"必须走**失败**，不能因为"看起来等于 0"被静默跳过；
   `hasAngle` 为 false 时 `MechanicalAngle` 保持清零值 0（**不瞎给**，
   断言 `[PT] 像素角度未接 → MechanicalAngle 不输出（保持 0，不瞎给）` 钉这条）。
   `*Undistorted` 时角度先经 `TryUndistortDirection`（去畸变前后两点差分）换到去畸变域再喂数学层；
11. 统一写出：`MechanicalX` / `MechanicalY` / `PixelEchoRow` / `PixelEchoCol`（+ 有角度时 `MechanicalAngle`）。
   **所有赋值都在全部检查之后**，所以失败轮一定只有第 0 步的清零值。

成功日志形如 `像素 (r, c) → 机械 (x, y)mm，机械角 θ°`，`Kind` 是 `Perspective` / `Mesh` 时追加 `（透视）` / `（网格（分段仿射））`，
三个 `*Undistorted` 追加 `（透视标定（已去畸变））` 等。

## 四·五、内参去畸变（二期③）：像素定义域与四判据

`Kind` 是 `NinePointUndistorted` / `PerspectiveUndistorted` / `MeshUndistorted` 时，
`Matrix` / `ProjectiveMatrix` / `MeshNodes` 里的像素**定义在去畸变后的理想针孔坐标系上**，且 `Intrinsics` 必非 null。
本插件的分工：

| 层 | 文件 | 职责 |
| --- | --- | --- |
| 数学层（零 HALCON） | `PoseTransformMath.cs` | `IsUndistortedKind` **单一真源**；三个 `*Undistorted` 在点换算里与对应的非 Undistorted 成员**走同一条分支**；内参四判据；内参尺寸失配 |
| HALCON 包装层 | `PoseTransformIntrinsics.cs` | `TryUndistortForCalibration`（正向）/ `TryDistortBack`（反向）/ `TryUndistortDirection`（角度）；全部走共享件 `Core.Halcon.Helpers.CameraDistortion`，**不自己拼 HALCON tuple** |
| 插件层 | `PoseTransformPlugin.cs` | `RunToMechanical` 里"先去畸变 → 映射 → 反算 → 加畸变 → 回显"；分档容差；信息块"去畸变已启用" |

**内参四判据**（`CheckCalibrationUsable`，全部明确失败、不许静默放行；同时被配置态调用，所以还没跑流程就能在状态栏看到）：

| # | 判据 | 为什么 |
| --- | --- | --- |
| ① | `Intrinsics != null` 且过 `CameraIntrinsics.IsUsable` | 复用契约判据，**不另写一份**（两处各写一遍迟早分叉） |
| ② | 内参图尺寸与标定图尺寸**两边都 >0 且不等** → 失败 | 两者不是同一次拍摄的产物 |
| ③ | 内参序列号与标定序列号**两边都非空且不等**（忽略大小写）→ 失败 | 拿错相机的内参 |
| ④ | **反向**：非 `*Undistorted` 的 Kind 带非 null `Intrinsics` → 失败 | 数据自相矛盾（矩阵按原始像素定义、却让人以为做过去畸变） |

尺寸/序列号"只记一边"一律放行——历史数据与离线场景的常态，报错只会让人把插件删掉（与失配③/④同一口径）。

## 五、单位与方向口径（谁负责换算）

- **角度一律"度"**：插件层、端口、界面、存盘全是度。**只有 `PoseTransformHalcon` 里那一行 `× Math.PI / 180.0` 转弧度**。
  新增 HALCON 调用请继续把换算收在这一层。
- **不做任何"方向校正"**：轴交换、镜像、旋转全部由标定矩阵吸收。
  `TryPixelAngleToMechanical` 的注释把这条写成纪律："镜像/轴交换标定会让转角**取反**：这是矩阵的自然结果（手性翻转），
  不做任何额外校正"——因为"负负得正"这类错误全部出在有人又校正了一次。
- **机械角配方**（刻意不推三角函数）：① 图像 x 轴方向 `(Row=0, Col=1)` 经矩阵线性部分映射得基准方向 φ0；
  ② 对象转 θ 后的方向向量 `(Row=sinθ, Col=cosθ)` 同样映射得 φ(θ)；③ 机械角 = φ(θ) − φ0，
  再 `NormalizeDegrees` 归一化到 `(−180, 180]`。对象没转时严格为 0。
- **透视/网格下角度与位置相关**：基准方向与对象方向都用**同一点 p** 的局部雅可比 `J(p)`
  （网格取所在三角形的仿射雅可比）；九点忽略 row/col（全局线性），行为与旧版一致。

## 六、配置态"三联刷新"与它的线程纪律

`RefreshConfigState()` 是**唯一**的界面刷新入口，被三类东西触发：
构造里的九个 `端口.ValueChanged`、三个 `[StepConfig]` 钩子（`OnModeChanged` / `OnEmitFollowedImageChanged` /
`OnTemplateRefFromRegionCenterChanged`）、`Initialize` / `OnViewAttached`。它做三件事（顺序固定）：

| 输出 | 属性 | 内容 |
| --- | --- | --- |
| ① 状态栏 | `StatusMessage` + `StatusLevel` | 当前模式"还差什么 / 已就绪"（同一份判据与 `RunAlgorithm` 的闸门同源，所以界面提示就是运行结论的预演） |
| ② 标定信息块 | `CalibrationInfoText` + `CalibrationInfoLevel` | 类型 / 来源 / 相机 / 标定图尺寸 / 创建时间 / 残差；与预览图尺寸不符 → 整块红字 |
| ③ 叠加预览 | `DisplayImage` + `FollowedInfoText` | 底图 + 基准绿轮廓 + 跟随橙轮廓 + 参考点黄十字 |

**线程纪律**：`RefreshConfigState` 可从任意线程调用（端口的 `ValueChanged` 可能在运行线程上触发），
所以它整体包在 `PostToUI(...)` 里，回 UI 线程后再依次判 `_disposed` 才动界面属性。
`PostToUI` 就是 `Application.Current?.Dispatcher` + `CheckAccess()`，取不到 Application 时**就地执行**
——这条正是断言宿主（没有 WPF `Application`）能直接跑插件的原因。

`DisplayImage` 的 setter 里带 `old?.Dispose()`：**换图即释放旧叠加图**，所以刷新预览不会每轮漏一幅整图。

## 七、内存所有权：谁 new 谁 Dispose

| 对象 | 谁创建 | 谁释放 |
| --- | --- | --- |
| `FollowedRegion` / `FollowedImage` 的端口值 | `PoseTransformHalcon` 里 `new HRegion(...)` / `new HImage(...)` | **移交端口**，由基类下一轮/`Dispose` 回收（`AutoDisposeRoundOutputs` 默认开） |
| 中途异常时的半成品 | `RunFollowRoi` 的 `try` 块 | **`catch` 里自己 `Dispose`**（两个产物都要，少一个就是漏一幅图/一个区域） |
| `_previewImage` 预览底图 | `LoadPreview` 后台线程 `new HImage()` + `ReadImage` | `SetPreviewImage`（换图释放旧的）与 `Dispose()` |
| `DisplayImage` 叠加图 | `RefreshOverlay` 末尾 `new HImage(current)` | `DisplayImage` setter 释放前一份；`Dispose()` 里赋 null |
| `RefreshOverlay` 里的中间对象 | `CopyImage` / `Compose3` / `GenContourRegionXld` / `PaintXld` / `GenCrossContourXld` | **全部登记进局部 `temp` 列表，`finally` 里逐个 `Dispose`，且单个释放失败不阻断**（`catch { }`） |
| `RefreshOverlay` 里跟随预览的 `using var followed` | `PoseTransformHalcon.FollowRegion` | `using` 声明（这是配置态临时量，**不进端口**） |

`RefreshOverlay` 必须先把底图合成三通道再画彩色轮廓（`CountChannels` ≥3 就 `CopyImage`，否则 `Compose3` 复制三份灰度），
因为 `PaintXld` 的 RGB 颜色只有画在 3 通道图上才成立；**不许直接往 `src` 上画**（那是上游端口的图，画脏了下游就拿到绿线）。

## 八、`RunAlgorithm` 开头为什么要自己重复清零

基类 `Execute` 已经预置 `Success=true` / 清空 `ErrorMessage`，并只负责回收 `IDisposable` 型输出（`HImage` / `HRegion`）。
**`double` 型端口没人清**：上一轮的 `MechanicalX=405` 会原样留到这一轮，本轮失败时下游读到的就是那个脏 405。
所以本插件在 `RunAlgorithm` 第一件事就是把五个 double 输出归零。

`Success`/`ErrorMessage` 那两行同样不是冗余：**断言与试运行可以绕过 `Execute` 直接调 `RunAlgorithm`**，
没有这两行，上一轮的 `ErrorMessage` 会被当成本轮错误、`Success` 停在上一轮的 `false`（源码注释：假红/假绿都出现过）。

## 九、预览载入的轮次号守卫

`LoadPreview` 后台读盘、回 UI 线程赋值，中间用户完全可能又换了一张图。三道闸：

1. `var loadId = ++_previewLoadId;` 起手领号，回程时 `if (loadId != _previewLoadId || _disposed) { loaded?.Dispose(); return; }`
   ——**过期结果只释放、绝不赋值**（否则旧图"复活"）；
2. 路径先判 `File.Exists`，不存在就同步清空预览并 `SetStatus($"预览图不存在: {path}", Error)`，**不起后台任务**；
3. `_loadedPreviewPath` 与 `PreviewImagePath` 用 `OrdinalIgnoreCase` 比对，同一张图不重复读盘；
   路径清空时 `++_previewLoadId`（把在途的载入全部作废）。

`Dispose()` 里顺序是固定的：`_viewAttached = false` → `_disposed = true` → `_previewLoadId++` →
`DisplayImage = null` → `SetPreviewImage(null)` → `base.Dispose()`。
先置标志再放对象，就是为了让任何还在路上的 `PostToUI` 回程能自己判掉。
