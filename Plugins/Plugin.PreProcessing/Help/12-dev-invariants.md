# 图像预处理 · 开发：不变量与陷阱清单

> 分类：图像处理 ｜ 来源：插件 Plugin.PreProcessing ｜ 读者：开发

**改这个插件前逐条对照**：每条都是"必须成立的事实 + 不成立时的症状 + 现在由谁守"。
分两类：**A 类违反后立刻抛或闸门红**（好查），**B 类违反后静默出错**（难查，多数是仓库实测踩出来的）。

本插件在 `FlowCanvasChecks\` 里**没有专属 checks 文件**（`PreProcessingChecks.cs` 不存在）。
能引到的真实守门断言只有这几处，下面逐条注明：

| 守门文件 | 覆盖的东西 | 对本插件是否有效 |
| --- | --- | --- |
| `FlowCanvasChecks\HelpManualCheck.cs` | 手册打包约定 HM0~HM11、命名 HM12/HM12b、开发页数 HM13、分层装载 HM14、台账 HM15/HM17、档位登记 HM16 | 有效（管手册，不管算法） |
| `FlowCanvasChecks\PortBindChecks.cs` | 端口类型可绑判据（`【判据】…` 系列） | 间接有效：`SrcImage` 是 `InputPort<HImage>`，`HImage → 数值端口不可绑` 等判据保护接线期 |
| `FlowCanvasChecks\ExecutionChecks.cs` | 流程层执行契约（`图纸编译通过`、闸门与会话锁、`运行状态连改 7 次不递增 Version`） | 间接有效：只保证"节点被正常调度"，不看链内部 |
| `FlowCanvasChecks\RoiInteractionChecks.cs` | 共享画布 `ImageEdit` 的冷启动拖拽/命中容差（AGENTS.md R51/R52，本插件是八个消费方之一） | 有效：画布上拖"编辑框"这条路径 |
| `FlowCanvasChecks\HalconControlChecks.cs` | `Core.Halcon` 控件层 | 有效：`h:ImageEdit` 绑定 `CanvasRois` / `CanvasActiveRoi` |
| `FlowCanvasChecks\CreateRoiChecks.cs` | ROI 插件的画布联动 7 条 | **不作本插件守门**，只是同一套 `DrawingObjectInfo` 模式的参照 |
| `FlowCanvasChecks\AnnotationBakerChecks.cs` / `ResultDisplayCheck.cs` | 离屏烘焙件 | **不涉及**：本插件零 `OpenWindow`（grep 复核 0 命中），它显示的是画布、不回读像素；仓库级"只有一处开窗"由 `ResultDisplayCheck` 静态扫描 ④ 守住 |

凡下表写"无专属断言"的条目，**只能靠人工核对**，改动时请自己补一次目视或临时探针。

---

## A 组：违反会当场抛、红或静默失败但可复现

**A1 "没有输入图"是唯一判失败的出口**
`RunAlgorithm` 首行 `src == null || !src.IsInitialized()` → `Fail("输入图像为空或未初始化")` 并 `return`；
链上任何算子抛异常一律降级为"跳过这一步 + 一条 Warn"，整步仍算成功。
症状：改成"某步失败就 Fail" → 现场一个算子参数填错把整条流程拖红，下游全部不执行。
守门：无专属断言（`ExecutionChecks` 只看流程调度，不看链内部）。

**A2 枚举成员数值手写死、只许末尾追加**
`Models\OperatorEnums.cs` 每个成员都显式写值（`Gray = 0`、`Angle90 = 90`、`First = 1`…），
因为存盘走的是**数值**（`PreprocessOperator.ToText`：`Convert.ToInt64(e)`），
`FromText` 用 `int.TryParse` 优先按数字还原。
症状：在中间插入或删掉一个成员 → 老方案里那个数字**静默指向另一个成员**，界面看起来"参数本来就那样"，
出的图却换了算法。这是注释里点名的"最难查的那类事故"。
守门：无专属断言。改枚举只能追加，且新成员的数值必须是全库未用过的。

**A3 算子类必须贴 `[PreprocessOperator]`**
`PreprocessOperator` 构造函数里取不到特性就 `throw new InvalidOperationException("{类名} 没有贴 [PreprocessOperator] 特性，无法作为算子使用")`；
`OperatorRegistry` 静态构造则把"没贴特性的类"直接跳过。
两个效果是有意分开的：抽象中间基类（`BoxOperatorBase`、`RectMaskOperator`）**不贴特性**，
所以既不进算子库菜单、也不会被 `Activator.CreateInstance` 误造；
而派生的具体算子忘了贴 → 造实例当场抛。
症状（另一半）：贴了特性但 Key 和已发布的重复 → `_byKey[key]` 后写覆盖前写，
**前一个算子在方案里被静默换成后一个**。Key 是全库唯一身份，新增时先 grep 一遍。
守门：无专属断言。

**A4 `[StepConfig]` 只许贴在弱类型快照上**
`Chain`（`List<OperatorStepItem>`，字段只有 `OperatorKey` / `Enabled` / `Params`）贴了 `[StepConfig]`；
界面上真正用的 `Operators`（`ObservableCollection<PreprocessOperator>`）**刻意不贴**——
算子对象带 `HImage`、事件、反射缓存，序列化进去要么炸要么存出一堆垃圾。
症状：给 `Operators` 补一个 `[StepConfig]` → 方案文件里出现 CLR 类型名与图像对象，
换版本/改类名后老方案打不开（基类注释里的硬约定就是冲这个来的）。
守门：`HelpManualCheck` 不管这个；无专属断言，靠 `ApplyConfigValues`/`OnConfirm` 的往返测试人工核对。

**A5 参数属性必须"可读可写 + 贴 `[SuperDisplay]`"才会进界面与快照**
`PreprocessOperator.GetParamProps` 的过滤条件是三重：
`GetCustomAttribute<SuperDisplayAttribute>() != null` **且** `CanRead` **且** `CanWrite` **且**
`DeclaringType != typeof(PreprocessOperator)`（基类的"启用"被排除，由 `OperatorStepItem.Enabled` 单独存）。
症状：新加参数忘了贴 `[SuperDisplay]` → 界面看不到、`SaveParams` 也不写，**改完不报错也不落盘**；
写成只读派生属性（如 `Summary`、`WasBoxClamped`）则是**故意**不落盘。
守门：无专属断言。

**A6 流程线程绝不枚举活的 `Operators`**
`RunAlgorithm` 先 `steps = Operators.ToArray()` 拍快照再跑，端口统计（`StepCount` / `ActiveCount` /
`ActiveOperators`）同样只读这份数组。`ObservableCollection` 不是线程安全的。
症状：配置界面正在加算子、流程线程同时在枚举 → 集合版本冲突异常落进业务 `catch`，
表现成一句 `图像预处理失败：…`，本步骤红掉（而不是"只是界面抖一下"）。
守门：`ExecutionChecks` 的闸门/会话锁系列覆盖了"运行中窗口"这一类并发风险，但不针对本插件；无专属断言。

**A7 `Dispose()` 必须以 `base.Dispose()` 收尾，且先停计时器**
本插件那份回收：`_previewDebounce.Stop()`、`DisposeStepImages()`、`_sourceImage = null`、
逐个 `CanvasRois` 里的 `DrawingObjectInfo.Dispose()`（覆盖"控件没挂上"与异常路径，重复调用安全）；
基类那份回收**输出端口**承载的 `HImage`。
症状：漏 `base.Dispose()` → 端口里的图永不释放（下一轮 `DisposeOldOutput` 也接不住，因为对象已经没了）；
漏 `_previewDebounce.Stop()` → 关窗后还有 Tick 回调打进来重算预览。
守门：无专属断言（`CreateRoiChecks` 的 `【释放】Dispose 幂等不抛` 是 ROI 插件的，不覆盖本类）。

**A8 画布集合只能在 UI 线程动**
`RefreshCanvasBox()` 首行 `if (dispatcher != null && !dispatcher.CheckAccess()) return;`。
流程线程上的参数纠偏（`Normalize` 会发属性通知）不需要也不许同步画布。
症状：在流程线程上改 `CanvasRois` → `NotSupportedException`（集合被绑定给界面），
或画面出现"框还在、图已经换了"。
守门：`HalconControlChecks` / `RoiInteractionChecks` 覆盖控件层，不覆盖线程闸；无专属断言。

**A9 手册的四条打包约定（R41）+ 分层命名**
`Help\` 扁平、`NN-` 前缀、每页第一行是 `# 一级标题`、无 BOM 合法 UTF-8 + LF、正文合计 ≥70 行、
不许出现占位标记字样（HM9 逐字扫的那几个）；开发页文件名必须含 `dev-` 段且**不得另立子目录**。
症状：少挂 `EmbeddedResource Include="Help\**\*"` → 宿主悄悄退回"自动生成"页，**不报错**；
开发页放进 `Help\dev\` → 同插件用户页整批被挤掉。
守门：`HelpManualCheck` HM0~HM11（内容与编码）、HM12（`NN-dev-` 命名）、HM12b（不许子目录）、
HM13（L 档开发章节 ≥300 行）、HM14（加载全量、显示分层）、HM16（档位表一一对应）、HM17（骨架台账）。

**A10 增量构建会静默漏打包手册资源（R42）**
判据不是构建退出码，而是 DLL 里的资源名数 == `Help\` 下 .md 数。
症状：`dotnet build` 报 0 错误、csproj 里 `EmbeddedResource` 也在，`Modules\Plugin.PreProcessing.dll`
里却一个 `.Help.` 资源名都搜不到——界面上仍是自动生成页。
守门：R42 的判据命令（围栏见下），主会话收尾用 `--no-incremental`。

```powershell
tr '\0' '\n' < Modules\Plugin.PreProcessing.dll | grep -a "\.Help\."
```

---

## B 组：违反后**静默**出错（历史上真的发生过）

**B1 `crop_part` 的形参顺序是 (row, column, width, height)，不是文档写的 (Height, Width)**
封装层 `PreprocessHService.Crop` 的注释留了实测数据：`CropPart(row:80, col:50, p3:200, p4:70)`
得到 **width=200 height=70**——第三个数管宽度。
症状：按 HDevelop 文档"改回去"，得到一张**宽高互换**的图，而 Halcon 一声不吭。
守门：无专属断言，靠人工核对（这条曾经就是旧版的实际 bug 类别）。

**B2 越界必须自己夹取，而且"先夹尺寸、再夹位置"**
实测 `crop_part` 越界**不报错**：请求 `(col=600, width=200)` 而图只有 640 宽，照样返回一张 200 宽的图，
多出来的是垃圾数据；只有 Row 为负才抛 #1301。`BoxOperatorBase.ClampBox` 因此先
`ClampToInt(BoxWidth, 1, imageWidth)` 再夹左上角。
症状：顺序反了会算出**负的可用宽度**（`imageHeight - height` 为负），`ClampInt` 把上下限颠倒，
框被夹到一个荒谬位置；不夹取则拿到一张"看着正常、实际错位"的图。
守门：无专属断言。`WasBoxClamped` 的 Warn 文案与参数摘要的"（越界已夹取）"是人查的线索。

**B3 `rectangle2` 的 length1 是**水平半宽**、length2 是垂直半高（phi=0 时 W = 2×length1）**
换算只允许出现在 `BoxOperatorBase.ToBoxTuples()` / `ApplyBoxTuples()` 里（四个 double 参数是整宽整高）。
症状：半宽/全宽写反是**静默 bug**——画面看着"差不多"，量出来差一倍；派生类、插件层、XAML 一律不许碰这个细节。
守门：`RoiInteractionChecks`（角点手柄拖拽 8 条，共享画布）、AGENTS.md R54 记的同一条认知坑。

**B4 `zoom_image_factor`：入参叫"行方向因子/列因子"，内部映射到 (scaleWidth, scaleHeight)**
实测 3.0 作用在**宽度**上、2.0 作用在高度上；输出尺寸四舍五入（640×0.333 → 213）。
因子 ≤ 0 抛 #1301，所以 `ZoomFactorOperator` 把下界夹到 0.01（不是 0），上界 20。
症状：按字面把行因子当高度传 → 缩放方向反了；把下界给 0 → 一轮 `执行失败，已跳过` 的 Warn，
画面"什么都没变"，看起来像算子坏了。
守门：无专属断言。

**B5 `paint_region` 的两个死条件：`Type` 只认 `"fill"`，`GrayValue` 长度必须等于通道数**
写成 `"filled"` 直接抛 #1302；三通道图只给一个灰度值抛 #1401。
`MaskOutside` 里 `gen_rectangle1` 用的是 (row1, column1, row2, column2) **闭区间**，右下端要 `-1`。
症状：忘 `-1` → 屏蔽框比用户看到的**大 1 像素一圈**（不报错）；只传单值 → 彩色图上整步被跳过。
守门：无专属断言。

**B6 `illuminate` 只接受开区间 (0,1) 的因子，端点会抛**
`ContrastOperator.Normalize` 夹到 `0.01~0.99`，而界面 `RangeValidation(0, 1)` 是允许 0 和 1 的。
症状：填 0 或 1 **界面不拦**，执行前被静默收敛——参数摘要显示的是收敛后的值。
把这两处口径当成同一个数（"界面允许 0 就是合法"）就会在改代码时误删夹取，换来一轮 Warn 跳过。
守门：无专属断言。同类：`SharpenOperator` 的界面是 0~50、执行前夹 0~50，但说明给的参考范围是 0.3~20。

**B7 灰度形态学族在 Halcon 侧的参数顺序都是 (Height, Width)**
`RectMaskOperator` 把"模板宽度/模板高度"的声明、纠偏、摘要收在基类，派生类只回答"调哪个算子"，
注释明确写了"参考实现的灰度膨胀/灰度腐蚀就是这么接错的"（旧版 switch 里膨胀调腐蚀、腐蚀调膨胀）。
症状：宽高不等的模板（竖条纹干扰用窄高模板）会**转 90 度**，效果看起来"算子没用对"，但不报错。
守门：无专属断言。这也是"五个算子抄五遍参数"的病根，别再退回抄写。

**B8 `median_image` 的第三个参数是 `Margin`（边缘扩充字符串），不是模板高度**
旧版把模板高度传给了 margin；这里 `Median(image, maskType, radius, margin)` 才是正确语义。
症状：半径与形状看着生效、图边上一圈异常（`mirrored` / `cyclic` 被填成了数字）。
守门：无专属断言。同族：`trans_from_rgb` 旧版选 YUV 实跑 hsi，现在按成员 switch 逐项映射真空间名。

**B9 透传必须原样 `return input`，异常路径绝不许偷偷返回入参**
上层用 `ReferenceEquals(result, current)` 判透传；旧版参考实现"算子出错就 `outImage = inImage`"，
结果上层释放输出时把**上游的图**也释放了（表现为莫名其妙的黑图/崩溃）。
本仓库的规矩：`PreprocessHService` 失败就抛，由 `ExecuteChain` 记录并保留原图。
症状：把 `return input` 写进 `catch` → 下一轮基类 `Dispose` 干掉上游图像，危害出现在**别的节点**里。
守门：无专属断言。

**B10 `_stepImages` 里故意不存透传帧**
`ApplyStepResults(results, passthroughBase)` 只收 `!ReferenceEquals(img, passthroughBase)` 的帧。
症状：把透传帧也存进去 → `DisposeStepImages()` 释放的是**别人（上游端口或副本）的图**，
下一句就在画布上访问已销毁句柄。后台预览路径里那个 `passthroughBase` 就是自己拷的副本（马上要释放）。
守门：无专属断言。

**B11 `InputImage` 必须"往前找第一张真正产出了图的步骤"**
`_stepImages` 不存透传帧，所以第 i-1 步查不到时**不能直接退回原图**，要 `for (k = index-1; k >= 0; k--)` 继续往前。
症状：链中间有一步透传（缩放因子 1.0、框正好盖住整图）时，"输入图"态整张画面跳回上游原图，
带框算子的框基准随之错位——看着像框被拖歪了。
守门：无专属断言。

**B12 整链全禁用/全透传时必须 `Clone(src)` 再挂端口**
`RunAlgorithm` 里 `if (ReferenceEquals(final, src)) final = PreprocessHService.Clone(src);`。
症状：直接把上游那张图挂到自己的输出端口 → 基类 `Dispose()` 回收端口值时把**上游的图**释放了，
上游节点下次拿到死图（`copy_image` 会重新分配内存，这正是用它的原因）。
守门：无专属断言。这是本插件最"必须存在但看着多余"的一行。

**B13 `DisposeOldOutput()` 必须先置空再释放**
`Image.Value = null!` 走 object 通道把 `TypedValue` 换成 default，然后才 `old.Dispose()`。
原因写在注释里：`OutputPort.TypedValue` 用 `SetProperty` 做**相等判断**，直接赋新值时
若引用恰好相同就不发通知 → 下游读到旧数据。
症状：连续两轮输出同一张图对象时，界面/下游停在上一轮的结果，看着像"没跑"。
守门：无专属断言。

**B14 预览的三段式不许退回"UI 线程同步跑"**
① UI 线程 `src.CopyImage()` 拷一份副本（后台独占，不与上游/画布争同一句柄）；
② 后台 `Task.Run` 跑快照数组，全程不碰 UI 对象；
③ 回 UI 线程才 `ApplyStepResults`——**此刻才释放旧图**。
症状（原实现的真 bug）：跑之前就释放旧图，那期间画布手里还攥着**已销毁的句柄**；
整链在 UI 线程同步跑则点一个算子僵住一两秒（`illuminate` 默认 101×101 掩膜在大图上是秒级，
而参数框逐字符触发刷新）。
附带三件套不许拆：`_previewGeneration` 轮次号（过期结果直接丢）、`_previewRunning` + `_previewDirty`
（不叠加也不丢最后一次请求）、`CopyImage()` 失败退同步路径（宁可慢一点也要有预览）。
守门：无专属断言。

**B15 导出必须走 `RefreshPreviewSync()`（同步版）**
`WriteExport` 靠 `_stepImages` 按步取图，异步那一轮还没回投就会**导出空清单**。
症状：现场"点了导出，只有 txt 没有图"，或图数远少于步数。
守门：无专属断言。判据：任何新加的一次性消费 `_stepImages` 的路径都必须显式同步。

**B16 导出用"当前图引用比对"判透传步，不是查 `_stepImages` 有没有**
`current` 跟着链走：禁用与透传都不产生新对象。中间透传步在 `_stepImages` 里存的其实是上一步那张，
只查字典会给同一张图**重复写出一个文件**。
症状：清单里同一张图出现两次不同文件名；或反过来把"确实产出了新图"的步误判成透传（丢文件）。
清单里必须如实写 `已禁用，未出图` / `透传，未单独出图`，别让人以为丢了。
守门：无专属断言。

**B17 `write_image` 的格式由 Format 参数决定、文件名不许带扩展名**
`PreprocessHService.SaveImage` 先 `ToHalconFormat(extension)` 认格式（bmp / png / jpg / tiff，
`jpeg→jpg`、`tif→tiff`），再把扩展名剥掉才传；不认识的扩展名抛 `不支持的导出格式：{ext}`。
症状：照 `Core.Halcon` 的 `HalconBase.SaveImage` 那样把全路径直接丢进去，有的版本补成 `a.png`、
有的补成 `a.png.png` —— 行为不可控，且没人报错。
守门：无专属断言。

**B18 参数纠偏要写回字段本身，不要只改局部变量**
`Apply()` 的顺序是 `Normalize(); return Process(input);`，`Normalize` 里 `Width = OddAtLeast(Width, 3)`
是**真的改字段**，所以参数摘要与列表行显示的就是实际生效值。
症状：只在调用点临时收敛 → 界面显示 4、实际跑 5，用户以为"填偶数没生效是 bug"，
调参时来回猜。同类：二值化上下限填反要**交换**（旧版原样丢给 HALCON，结果是整张全黑图且不报错）。
守门：无专属断言。

**B19 反射灌进来的参数不会触发 setter 通知，末尾必须补发**
`LoadParams` 直接 `prop.SetValue(...)`，绕过了 `SetParam` 里的 `OnPropertyChanged(nameof(Summary))`，
所以结尾统一补 `OnPropertyChanged(string.Empty)` 与 `OnPropertyChanged(nameof(Summary))`。
症状：读盘后列表里参数摘要仍是空的/旧的（数据其实对了）——"配置丢了"的错觉，用户会重调一遍。
连带口径：`AppendStepsCore` 里 `op.Enabled = item.Enabled` **必须排在 `LoadParams` 之后**，
否则被那次整体刷新盖掉（灰显状态与摘要错位）。
守门：无专属断言。

**B20 批量追加必须用 `_rebuilding` 罩住**
`OnOperatorsChanged` 对 Add/Remove/Move/Reset 都会 `WriteChain()` + `SchedulePreview()`。
`RebuildOperators()`（读盘）与 `PasteChain()`（一次粘 N 条）都用 `_rebuilding = true` 罩住
`AppendStepsCore`，`WriteChain` 首行 `if (_rebuilding) return;`。
症状：忘了罩 → 每 Add 一条就把整链快照重写一遍、预览重算一遍（读 20 步链 = 20 次全链运算），
界面卡死且中途 `Chain` 是半成品。
反向：放开 `_rebuilding` 之后必须自己补一次 `WriteChain`/`SchedulePreview`（`PasteChain` 就是这么做的），
漏了就是"界面有链、盘上没链"，确认后落盘的仍是旧链。
守门：无专属断言。

**B21 画布自激的两道闸：`_syncingCanvas` 与 `_fromCanvas`**
`_syncingCanvas` 罩住"VM 往画布写参数"，屏蔽一次 `HTuples` 回写；
`_fromCanvas` 罩住"画布拖拽回写算子"，屏蔽一次"参数→画布"的回推。
症状：去掉前者 → 写进去又读回来，无限自激；去掉后者 → 拖框过程中被 `SetParams` 拽住手柄，拖不动。
`OnOperatorPropertyChanged` 里那句 `if (!_fromCanvas && ReferenceEquals(sender, SelectedOperator)) RefreshCanvasBox();`
两个条件都不许省。
守门：`RoiInteractionChecks` 保护的是控件侧手势（冷启动一次拖得动、容差 14px），不覆盖这两道闸；无专属断言。

**B22 框只在"它的坐标基准那张图正显示着"时才画**
`IsBoxBaseDisplayed()`：`_previewTarget == Input`，或 `Source` 且 `_previewStepIndex <= 0`。
`SelectedOperator` 的 setter 里对 `BoxOperatorBase` **强制切到 Input 态**（`force: true`）。
症状：停在输出图上拖框 → 裁剪/缩放改过尺寸，基准对不上，实际生效的框比以为的小（现场高发）。
反向：`ApplyPreviewState(..., force: true)` 的 `force` 不许去掉——换选中算子时状态可能没变，
但框换了"主人"，参数必须重新推一遍。
守门：无专属断言（用户页 02 里有对应的操作说明，两边要一起改）。

**B23 `RefreshPreview()` 里故意不调 `RefreshCanvasBox()`**
跑一遍链不改框参数；而在用户手还按在框上时重建句柄，会把这次拖拽**直接打断**。
症状：拖框时画面一顿、手柄弹回。加预览刷新逻辑的人最容易"顺手补一句同步画布"，别补。
守门：无专属断言。调用时机只有三个：选中项变、链结构变、预览态或参数变。

**B24 画布删框要归零算子的框参数**
`OnCanvasRoisChanged` 的 Remove 与 Reset 分支都调 `ClearBoxParamIfUserRemoved`（置 `BoxWidth = 0`、
`BoxHeight = 0`，即"未框选 = 透传"），且首行判 `_syncingCanvas`（VM 自己撤框不算用户删除）。
症状：只从集合里移除、参数不归零 → 下一次 `RefreshCanvasBox` 又把框长回来，
右键"删除/清空"点了没反应。
`DeleteBoxCommand` 的 `CanExecute` 还有一条：`Keyboard.FocusedElement is not TextBox`——
焦点在数值框里时不许抢 Delete 键（那是正常编辑键）。
守门：`RoiInteractionChecks` 不覆盖；无专属断言。

**B25 `SelectedOperator` 判同值用 `ReferenceEquals`，且删空要置 null**
`RemoveSelected` 之后：还有算子就选相邻那条（`Math.Min(index, Count - 1)`），空链置 `null`。
症状：删空不置 null → 画布上继续挂着**已删算子**的框；参数面板也还显示着它。
`SelectedOperator` 为 null 时 setter 把预览退回 `Source`，这是"清空后画面回到原图"的来路。
守门：无专属断言。

**B26 `OnOperatorPropertyChanged` 必须忽略 `Summary`**
`Summary` 是派生显示属性，`SetParam` 每次都会补发它的通知；不过滤就会 `WriteChain()` +
`SchedulePreview()` 再来一轮（回写→通知→再回写）。
症状：改一个参数预览重算两遍，界面更卡；链复杂时像是"抖两下"。
守门：无专属断言。

**B27 透传语义是"设计行为"，不许当成 bug 去修**
单通道图做「彩色空间转换」、框未画或框正好盖住整图、「按比例缩放」两个因子都是 1、
「修改图像尺寸」与原尺寸相同——这些都**原样返回入参**。
症状：为了让"每一步都产出新图"而给它们补一次 `Clone` → 全链多 N 张整图，
节拍与内存双双变差，用户还是看不出区别。
配套：预览字典不存透传帧（B10），清单写"透传，未单独出图"（B16），三处口径必须一致。
守门：无专属断言。

**B28 算子库的分组顺序靠 `Order`，不靠反射返回顺序**
`OperatorRegistry` 静态构造按"组内最小 Order"排分组、组内再按 Order 排；注释点名
`Assembly.GetTypes()` 的顺序 CLR 并不保证。
症状：新算子随手填一个小 Order（或漏填 = 0）→ 整组跑到菜单最前面，或某个算子插进别人的组里。
全库 Order 是统一递增分配的（color 10、几何 20~43、滤波 50~70、形态学 80~110、增强 120~150、二值化 160~170），
新增请接着族尾取号。
守门：无专属断言。

**B29 读盘认不出的 Key 只跳过并提示，绝不让方案打不开**
`AppendStepsCore` 里 `OperatorRegistry.Create(key)` 返回 null 就 `unknown.Add(key)` 继续走；
`RebuildOperators` 汇总成 `有 N 个算子在当前版本中不存在，已跳过：…` 挂 `LoadWarning`。
`LoadParams` 单个参数解析失败也是 catch 掉留默认值。
症状（反向）：把这里改成抛异常 → 一个脏字段把整份方案卡死，现场升级回退后打不开工程。
守门：无专属断言。

**B30 剪贴板的两条瞬态与两条纪律**
写：`Clipboard.SetText` 偶发 `CLIPBRD_E_CANT_OPEN`（别的进程开着剪贴板，典型是剪贴板管理器与远程桌面），
`TrySetClipboardText` 重试 5 次、每次 `Sleep(40)`，仍失败只给一条提示不弹异常框。
读：`PasteChainCommand` 的 `CanExecute` 恒为真——**不许在 CanExecute 里碰剪贴板**（它会被高频重查）。
症状：忘了 `catch (COMException)` → 一次"复制整链"炸掉配置界面；
把剪贴板判空写进 CanExecute → 按钮闪烁 + 每帧读剪贴板的卡顿。
守门：无专属断言。

**B31 链文本的兼容性口径**
`ChainJson`：`WriteIndented` + `PropertyNameCaseInsensitive`（允许现场手改 JSON）；
序列化对象是 `OperatorStepItem`（弱类型、不含 CLR 类型名）。
症状：改成序列化算子对象或打开 `TypeNameHandling` → 复制出去的文本换版本就粘不回来，
`Chain` 落盘也一起坏掉（与 A4 同一个病根）。中文显示名**不是身份**，改显示名不许动 Key。
守门：无专属断言。

**B32 视图只负责"就绪信号 + 布局开关 + 撑窗口"，别往它塞逻辑**
`PreProcessingView` 是纯绑定层（MVVM），实现 `UI.CustomControl.IPropertyGridLayoutSwitch`：
`UseCardLayout` 写 VM 的 `IsCardLayout`（数据归 VM）同时改 `ParamColumn.Width` 360 → 540（布局归视图）。
`EnlargeHostWindowIfNeeded` 三条硬约束：**只放大不缩小**（用户手动调小就别弹回去）、
**不超过 `SystemParameters.WorkArea`**（否则"确认"按钮永远点不到）、`WindowState != Normal` 时不碰
（最大化时 Width/Height 只是还原尺寸，改了不生效）。
症状：把撑窗逻辑改成"每次都设成目标尺寸" → 用户一拖小就被弹回；不夹工作区 → 窗口超出屏幕。
卡片式宽度是算出来的（1120 − 240 − 540 − 32 = 308，正好卡住预览列 `MinWidth=300`），
改这三个数任何一个都要重算，否则预览列被挤没。
守门：无专属断言。

**B33 `StepNumberConverter.ConvertBack` 必须抛，不许返回假值**
`ListBox.AlternationIndex` 从 0 起算、界面显示 "1."，转换器只做单向。
注释原话：抛 `NotSupportedException` 比返回一个假值更容易在改错时暴露。
症状：改成 `return 0` → 万一有人把这条绑定写成双向，步号静默错位一格，框基准跟着错。
守门：无专属断言。

**B34 枚举上屏有两条互不相干的口径（别把 R40 当成已完成）**
本插件的算子参数枚举（`Models\OperatorEnums.cs`）**只标了 `[Description("中文")]`，没有 `[Display(Name="中文")]`**，
它出现在**自带视图**的 FlatPropertyGrid 里，显示走 `UI\Controls\CustomControl\PropertyGrid\Generators\EnumGenerator`
（优先读成员上的 `[Description]`），所以下拉是中文。
宿主的另外两处生成点（`Shard\Core.Controls\LinkableValueEditor` 与
`VisionMaster\ViewModels\DialogViewModels\VariableBindingViewModel`）走 `StepConfigOption.ForEnum`，
读的是 `[Display(Name)]` —— 这两个枚举**目前不上那两个面板**（算子参数不是端口），所以现在不受影响；
但 AGENTS.md R40 的欠账清单里这一项**仍未补**，哪天真要把枚举搬进自动参数面板，界面就会退回英文成员名。
症状（改错时的样子）：以为"补 `[Display]` 才对"就把 `[Description]` 删了 → 自带视图的下拉当场变
`Square` / `Mirrored` / `Light`（EnumGenerator 取不到 Description 就回退成员名），现场天天用的界面。
要同时补、不要替换；且补完 `Summary`（`PreprocessOperator.Desc` 读 `[Description]`）与下拉会分家，两边都要验。
守门：`HelpManualCheck` 与 R40 的 6 条（`MotionZMotionChecks`）都不覆盖本插件；无专属断言。

---

## 并行安全声明的依据（`[ParallelSafe]`）

类上标了 `[ParallelSafe]`（二期真并行：纯视觉计算）。满足判定口径：无 static **可变**字段
（只有 `ParamCache` 这类并发字典与 `OperatorRegistry` 的一次性只读表）、不碰进程级独占资源、
不写全局共享状态；`RunAlgorithm` 内没有长阻塞，无需分段检查取消令牌。
**匹配口径注意**：`FlowCompiler` 按类型 **FullName 字符串**匹配该特性（绕开"两份 `Core.Interfaces.dll`"
导致程序集身份不一致），所以别在自己命名空间下复制一份同名特性。
预览路径的后台 `Task.Run` 与流程执行是两个场景，别把它们当成"并行"的证据——
链内部算子是**串行**跑的，`ExecuteChain` 没有并发。

## 改这个插件之后的必做动作

| 动作 | 判据 |
| --- | --- |
| 重建插件工程 | 改了源码不重建，`Modules\` 里仍是旧 DLL（红线③）；改到手册也要重建（R41/R42） |
| 收尾那一次用 `--no-incremental` | 增量构建会静默漏打包 `Help` 资源（R42，A10 那条命令对账） |
| 跑 gate | `HelpManualCheck` 全套 + `PortBindChecks` + `ExecutionChecks`；本插件无专属 checks，算法侧靠人工核对 |
| 动了 `Services\Core.Halcon` 或共享画布 | Debug 与 Release 都重建（R55，`pack.ps1` 打 Release） |
| 大改动后重建 CodeGraph 索引 | 不重建 = 影响面查询静默少项（R28）；XAML 与生成器产物一律 grep（R29） |
