# 深度学习分类 · 开发章节：架构与推理链路

> 分类：图像处理 ｜ 来源：插件 Plugin.DlClassify ｜ 读者：开发

源码：`Plugins\Plugin.DlClassify\DlClassifyPlugin.cs`、`ClassifyEngine.cs`、`ClassifyPostProcess.cs`、`ClassifySession.cs`、`ClassifyView`、`DlModelState.cs`。
分层与 YOLO 一致：编排（Engine）、纯数学（PostProcess）、会话（Session）、壳（Plugin）。

## 一 链路五步（`ClassifyEngine.TryClassify`）

1. 任务元数据守卫 `ClassifyPostProcess.IsSupportedTask`。
2. 定输入尺寸：配置优先，其次模型静态形状；都没有报 `无法确定模型输入尺寸：模型是动态形状，且节点配置里没指定「输入尺寸」。`
3. 按 `ResizeMode` 造 `ResizePlan` 并缩放/补边（失败报 `预处理（缩放/补边）失败：{ex.Message}`）。
4. Run 推理（失败报 `分类推理失败：{ex.Message}`）。
5. 布局识别 → 概率归一 → TopK。

## 二 两种缩放模式的差别

`ClassifyResizeMode` 只有 `Letterbox`（等比缩放加居中补 114 灰边）和 `Stretch`（直接拉满，改长宽比）。
`ResizePlan.Compute(...)` 里 `PadGrayValue = 114`，`Describe()` 对 `Stretch` 明确写"无补边"。
选哪一档**取决于训练时用的哪一档**，不是"哪个看着好看"：训练是 stretch 出来的模型，用 letterbox 推理会静默掉精度，反之亦然。
默认值是 `Letterbox`（不破坏长宽比那一档），这也是断言钉住的一条（见 12 页第 9 条）。

## 三 布局识别（`TrySniffLayout`）

把张量压平后取"最后一维是类别数"的口径，接受 `[1,3]`、无 batch 维的 `[1000]`、`[1,1,5]`；
拒绝 `[1,84,8400]`（检测）、`[1,2,224,224]`（语义分割）、`[1,1]`（分类至少 2 类）。
识别不出来时报 `无法识别的输出形状 [...]：请确认这是分类模型，且导出时没有裁剪输出。`，形状原样写进句子方便现场贴回。

## 四 概率归一是这册里最容易写错的一段

`NormalizeToProbabilities(raw)` 先判"是不是已经是概率"：**和≈1 就原样保留**，否则才 softmax。
理由：很多导出模型最后一层已经接了 Softmax，再 softmax 一次不报错，只是分布被压平——
本来 0.9 对 0.05 的明显结论变成 0.6 对 0.3，置信度门禁（0.6）就此失真。
`Softmax(logits)` 减最大值保数值稳定（大 logits 1000/999 不溢出、和为 1；全负 logits 也仍和为 1，不因负值误判成概率）。

`TopK(probabilities, classNames, k)` 把 k 夹到 `[1, 类别总数]`；`ResolveClassNames` 在元数据缺名时用 `class_N` 占位，
并且保证 **`ClassNames` 与 `Scores` 同序**（TopK 小于类别总数时也不许错名——错名比缺名更坏，它会让人相信一个错的类别）。

## 五 会话与缓存

`ClassifySession.Cache` 是进程级 `static` 字典（键为模型全路径，`OrdinalIgnoreCase`）配 `CacheLock`，
`TryAcquire` 加引用计数、`Dispose` 归零才真释放；`CachedSessionCount` 暴露给断言与排障。
报错口径与 YOLO 同源：`模型文件不存在：{path}。请重新选择，或把模型放到该路径`、
`加载 ONNX 模型失败：{ex.Message}。请确认：① 文件是合法的 .onnx 模型；② onnxruntime.dll 已随插件投递到宿主 exe 目录（32 位进程无法加载）`。

## 六 模型状态与摘要刷新（DL1）

**本插件不在 `EnsureSession` 里发 `ModelSummary` 变更通知。** 源码注释给出的理由是两条：
该方法被 `ModelSummary` 的 getter 调用（发通知等于自我重入），运行期还会从非 UI 线程走到。
刷新改由 `RefreshModelState()` 统一驱动，三个明确入口都调它：`OnModelPathChanged`、`Initialize(stepData)`、`OnViewLoaded`。
`YoloPlugin` 现在采用同一套写法（`YoloPreviewState.cs` + `RefreshModelState`），两个插件不再存在"一边发、一边不发"的差异。

`DlModelState` 三态：`NotLoaded`（`ModelPath` 为空）/ `Ready`（会话已建）/ `Failed`（选了模型但加载失败）。
`RefreshModelState()` 的判据是"`EnsureSession()` 返回 null 且 `ModelPath` 非空 = `Failed`"，**不看任何中文文案**。
`TryPreviewClassify()` 失败时只有 `_sessionError` 非空（路径/模型/task 级失败）才写 `ModelState=Failed`；
成功跑完一次推理则确保 `ModelState=Ready`（防"换路径后状态残留"）。
`ModelState` 是 `private set` 的只读运行态，**不是 `[StepConfig]`**，不落盘。

## 七 取消与低置信

`RunAlgorithm` 第一件事是 `if (context.CancellationToken.IsCancellationRequested) { Fail("执行已取消"); return; }`，
在加载会话之前，所以取消不会白跑一次模型加载。
`private const double LowConfidenceThreshold = 0.6;` 命中时写 Warn，句子明确包含"本次结果不可信，请勿直接用于放行"——
Warn 而不是 Fail：判定权在流程，节点只负责把"不可信"这件事留在日志里。

## 八 预处理画布：1 通道 → 3 通道 → NCHW

`ClassifyEngine.TryBuildCanvas` 的固定顺序（每一步都有实测理由）：

1. **统一成 byte**：`GetImagePointer1` 按 byte 取缓冲，uint2 / real 图不转就会取错像素。
2. **通道归一**：1 通道 `Compose3` 复制成 3 通道；3 通道原样；其它通道数明确报错（提示上游先转灰度/RGB）。
3. **按模式缩放**：`Stretch` 直接 `ZoomImageSize` 到目标宽高；`Letterbox` 先等比缩到 `ScaledWidth×ScaledHeight`。
4. **补边**：`GenImageConst` 造目标画布，`GenImageProto` 填 114 灰，再 `Compose3` 成 3 通道；
   `ChangeDomain` 把缩放图搬到 `(PadX,PadY)`，最后 `OverpaintGray` 覆写。
5. **转张量**：按通道 `AccessChannel` + `GetImagePointer1` + `Marshal.Copy`，每个像素 `/255f`，
   按 NCHW（`[1,3,H,W]`）摆放。

为什么补边是 114 而不是 0：黑边是模型训练时没见过的强边缘，贴边目标会系统性偏移。
为什么画布必须**无条件** 3 通道：`OverpaintGray` 要求源图与目标图通道数一致，否则 HALCON #3122；
源图已 `Compose3` 恒为 3 通道，所以画布也必须 3 通道（与 Plugin.Yolo 同款坑）。

## 九 错误分层：会话级 vs 本次试算级

`TryClassify` 的失败分两层，界面状态必须区分：

- **会话级失败**：`EnsureSession()` 返回 null（路径为空 / 文件不存在 / ONNX 打不开 / task 不是 classify）。
  此时 `_sessionError` 非空，`TryPreviewClassify()` 把 `ModelState` 置 `Failed`——模型本身不可用。
- **本次试算级失败**：会话已建，但本次输入/参数/输出布局有问题（类别过滤、图像通道、动态尺寸、输出形状不认识）。
  此时 `_sessionError` 为空，只把 `ResultSummary` 写成"试算失败：…"，`ModelState` 保持 `Ready`——
  模型没坏，坏的是这一次的输入或参数。

`RunAlgorithm` 对两层失败都 `Fail(error)`，但界面状态只反映"模型能不能用"，不把一次坏输入误判成模型损坏。

## 十 线程与资源

- `Session.Run` 线程安全，`ClassifySession.Cache` 的字典读写锁在 `CacheLock` 上；插件标 `[ParallelSafe]` 的前提是
  "推理无共享可变状态"，不要把上一次结果缓存到实例字段供界面复用。
- 输入 `NamedOnnxValue` 内部持有 `OrtValue`，必须 `Dispose`；`TryBuildCanvas` 里的临时 `HObject` 全部进
  `temps` 列表，`finally` 统一释放，异常路径也不漏。
- 预处理用指针 + `Marshal.Copy` 而不是逐像素 `get_grayval`：640×640×3 是 122 万像素，
  逐个算子调用的开销会超过推理本身。
