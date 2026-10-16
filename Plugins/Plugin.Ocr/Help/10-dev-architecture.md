# OCR 文本识别 · 开发章节：架构与算法链路

> 分类：图像处理 ｜ 来源：插件 Plugin.Ocr ｜ 读者：开发

源码：`Plugins\Plugin.Ocr\OcrPlugin.cs`、`CharSegmenter.cs`（切分）、`OcrEngine.cs`（识别与模型解析）、`OcrView`。

## 一 一句话定性：分割占九成

内置分类器只做"给一个区域，说它是哪个字符"。现场 90% 的识别问题出在**切分**：多切把噪声当字符，少切把一个字切成两半。
所以 `TryRecognize` 的结构是"先切后认"，切分失败或空产出不进入识别阶段：
`OcrEngine.TryRecognize` 在 `segment` 无产出时报 `没有可识别的字符（分割阶段未产出结果）`。

## 二 切分（`CharSegmenter.TrySegment`）

输入是原图加 `RoiParams`（识别区参数），输出 `SegmentResult`（字符对象集、灰度图、每个字符的 `Rows`/`Cols`、`UsedThreshold`、`Count`），
`IDisposable`，插件侧用 `using(segment)` 保证释放。三道前置校验的原文错误：
`输入图像为空`、`尚未框选识别区：请在节点配置里于图像上右键 → 新建矩形，框住整行字符`、`识别区的半长/半宽必须大于 0：请重新画一个框`。
兜底 catch 写成 `字符分割失败：{ex.Message}（若为坐标越界，请把识别区画在图像范围内）`——
把最可能的现场原因直接写进错误里，省一次往返沟通。

阈值二选一：`UseFixedThreshold` 为真用 `FixedThreshold`，否则按直方图自动找分界（结果里 `UsedThreshold` 回读实际用的那个值）。
形态学三档 `ClosingRadius`、`ErosionRadius`、`DilationRadius` 是"多数场景不用动"的高级参数，
它们的作用是断笔（闭运算）、粘连（腐蚀）、细化后补偿（膨胀）。`ColumnFirst` 决定按列还是按行排序，竖排文本才需要。
`TextIsDark` 决定二值化极性（暗字亮底 / 亮字暗底），搞反的结果是"切出一整块背景"。

## 三 识别与模型解析

`OcrEngine.BuiltInModelName = "Industrial_0-9A-Z_Rej"`。`TryResolveModel(configured)` 四级顺序：
显式配置路径 → `AppDomain.CurrentDomain.BaseDirectory\Assets\ocr\{名字}.omc` → `%HALCONROOT%\ocr\{名字}`（先试 `.omc` 再试裸名）→
把**短名**交回 HALCON 让它按自己的搜索路径兜底。
最后一级不报死是刻意的：装了 HALCON 但插件资源没投递成功的机器仍能跑，只有真找不到时才由读取算子抛错。
反过来，**显式配了却不存在必须报错**（`指定的 OCR 模型文件不存在：{path}。请修正路径，或清空该配置以使用内置模型（…）。`），
否则用户填了中文模型路径却实际跑着 0-9A-Z 分类器，现场只会说"这 OCR 连中文都认不出来"。

分类器句柄按模型路径缓存：`Dictionary<string, HTuple>`（`OrdinalIgnoreCase`）配 `_lock`；
`read_ocr_class_mlp` 拿的是**进程级非托管资源**，所以 `Dispose` 里必须 `_engine.Dispose()`。
实例字段 `private readonly OcrEngine _engine = new();` 是**饿汉**写法，注释理由：懒初始化反而要处理"配置界面试算与流程运行并发创建"的竞态。

## 四 拒识判据（不要改回去）

`Recognition.IsRejected(cls)`：把"既不含字母也不含数字"的类别名判为拒识，显示统一成 `?`（`Display()`）。
源码注释记录了为什么**不用判空或猜占位符**：实测明细里出现过 `2:(0.99)` 这种看不见字符的项，
猜 HALCON 用哪个占位符表示拒识不稳；改用"这个分类器只可能输出 0-9 与 A-Z"这一事实做反证才可靠。
`RejectedCount`、`AverageConfidence`、`Detail` 都由此而来，`Detail` 里拒识项写 `?`。

## 五 插件壳里的画布状态机

`Initialize(stepData)` 在 `_seedingCanvas = true` 的保护区里把落盘的 `RoiParams` 还原成一个 `DrawingObjectInfo`
（`DrawShapeType.Rectangle`，标签"识别区"），`finally` 里复位。
保护标志存在的理由：还原时会触发 `OnRoiTuplesChanged`，不屏蔽就会"打开界面就把配置改一遍"。
`OnRoiTuplesChanged` 只在 `ReferenceEquals(info, CanvasRois.LastOrDefault())` 时写回，`WriteBackRoi()` 取 `LastOrDefault`——
**多个识别区只认最新一个**，这是设计而不是 bug；`ClearRoi()` 清空。
`RoiSummary` 的显示口径是 `中心 (r, c)  角度 X 度  半长 X  半宽 X`，未框选时是"尚未框选识别区"。

## 六 运行链路与告警

`RunAlgorithm` 上游空图 → `Fail("上游没有图像")`；成功走 `TryRecognize` 后写 Info 日志。
两条 Warn 的原文（一字一句照源码）：出现拒识字时写一条、均值置信低于 `private const double LowConfidenceThreshold = 0.9;` 时写一条"结果不可信"。
**Warn 而不是 Fail**：判定权在流程侧，但错字符串若不留痕就会被当成成功结果放行——这两条是现场唯一能追溯"这一轮其实不可信"的证据。
试算结果文本口径：`识别结果：'{Text}'` 换行接字符数、拒识数、均值置信、实际阈值，再接逐字符明细。
