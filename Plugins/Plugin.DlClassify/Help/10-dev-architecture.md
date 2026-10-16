# 深度学习分类 · 开发章节：架构与推理链路

> 分类：图像处理 ｜ 来源：插件 Plugin.DlClassify ｜ 读者：开发

源码：`Plugins\Plugin.DlClassify\DlClassifyPlugin.cs`、`ClassifyEngine.cs`、`ClassifyPostProcess.cs`、`ClassifySession.cs`、`ClassifyView`。
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

## 六 一条刻意的设计差异

**本插件不在 `EnsureSession` 里发 `ModelSummary` 变更通知。** 源码注释给出的理由是两条：
该方法被 `ModelSummary` 的 getter 调用（发通知等于自我重入），运行期还会从非 UI 线程走到。
刷新改由三个明确入口驱动：`OnModelPathChanged`、`OnViewLoaded`、`Initialize`。
`YoloPlugin` 则是在 `EnsureSession` 内部发通知的——两种写法并存是事实，移植时别互相"统一"。

## 七 取消与低置信

`RunAlgorithm` 第一件事是 `if (context.CancellationToken.IsCancellationRequested) { Fail("执行已取消"); return; }`，
在加载会话之前，所以取消不会白跑一次模型加载。
`private const double LowConfidenceThreshold = 0.6;` 命中时写 Warn，句子明确包含"本次结果不可信，请勿直接用于放行"——
Warn 而不是 Fail：判定权在流程，节点只负责把"不可信"这件事留在日志里。
