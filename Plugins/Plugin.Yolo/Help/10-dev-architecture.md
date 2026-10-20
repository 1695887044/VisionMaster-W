# YOLO 目标检测 · 开发章节：架构与推理链路

> 分类：图像处理 ｜ 来源：插件 Plugin.Yolo ｜ 读者：开发

源码：`Plugins\Plugin.Yolo\YoloPlugin.cs`（插件类兼视图模型）、`YoloEngine.cs`（预处理与推理编排）、
`YoloPostProcess.cs`（布局识别、解码、坐标换算、NMS，纯函数）、`YoloSession.cs`（进程级会话缓存 + 引用计数）、
`YoloCrop.cs`（按框裁剪与夹取）、`YoloPreviewState.cs`（配置界面状态枚举与计数映射）。

## 一 分层：把"会静默出错的数学"和"插件壳"分开

`YoloPostProcess` 与 `YoloCrop` 不依赖 ONNX Runtime、也不依赖 `VisionPluginBase`，输入是受控张量或受控框列表。
这么切的原因：这两层写错**不抛异常**，只给"看着正常但全错"的结果，必须能脱离模型单独断言。
`YoloEngine` 只做编排（尺寸决定 → 预处理 → Run → 校验 → 解码），会话生死归 `YoloSession`，端口与配置归 `YoloPlugin`。

## 二 六步链路（`YoloEngine.TryDetect`）

1. **任务元数据守卫先跑**：`YoloPostProcess.IsSupportedTask(Info.Task)` 放在推理**之前**，省一次白跑。
2. **定边长**：`ResolveInputSize`——配置优先，其次模型静态输入形状；两者都没有就报错而不是猜。
3. **letterbox**：等比缩放后居中贴到填 114 灰的方形画布。
4. **张量**：3 通道 byte 图 → NCHW `float[]`，像素除以 255。
5. **Run**：`InferenceSession.Run`，输入项 `IDisposable` 要显式释放。
6. **布局校验 → 解码 → 坐标换算 → NMS → 类别过滤 → 截断**。

失败一律走 `out error`，不抛：`YOLO 推理失败：{ex.Message}`、`预处理（letterbox）失败：{ex.Message}`。

## 三 letterbox 的实现细节（两处实测踩坑）

`LetterboxInfo.Compute` 给 `Scale`、`ScaledWidth/Height`、`PadX/PadY`；`MapBackToOriginal` 做"减补边再除缩放"，**并把框夹回原图范围**
（模型给略微越界的框很常见，不夹会让下游 `crop_part` 拿到越界参数）。

填充为什么是 114 而不是 0：训练时补的就是 114 灰，黑边会被模型当成强边缘，贴边目标系统性偏移。
画布构造用 `change_domain` 加 `overpaint_gray`，不用 `tile_images_offset`（后者填充值固定为 0）。

- **实测坑一**：`overpaint_gray` 要求源图与目标图通道数一致，否则 HALCON #3122
  `Number of channels in the input parameters are different`。
- **实测坑二**（第一次修错了）：不能按"原图是不是 3 通道"决定要不要补画布通道——上游已把单通道 `Compose3` 成 3 通道，
  所以送进 overpaint 的源图**恒为 3 通道**，画布必须**无条件**补成 3 通道。工业灰度相机必然踩这一条。

归一化为什么必须 `/255`：少除或多除都不报错，只是精度静默劣化，所以这一步被单列成可断言的事实。

## 四 布局识别与解码

`TrySniffLayout(dims)`：去掉 batch 维后**取较小的一维当通道数**（通道几十到几百，锚点几千到几万），
据此区分 `V8Transposed`（`[1, 4+C, N]`，通道在前、无 objectness）与 v5 风格 `[1, N, 5+C]`。
识别不出来时报错文本里**把形状原样写成一行**（动态维记成 `N`），方便现场直接把那句贴回来。

v5 风格是**明确拒绝**而不是硬解：维度顺序恰好相反，硬解不报错只出乱框。
`DecodeV8Transposed` 产出的坐标仍在 letterbox 图上，必须再过 `MapBackToOriginal`。

NMS **按类别分别做**：缺陷检测里"异物"和"划伤"的框常常完全重叠，跨类压制会直接吃掉真缺陷。
之后才是置信度阈值过滤、类别过滤、按置信度降序截断到 `MaxDetections`。

## 五 会话：进程级共享加引用计数

`YoloSession.Cache` 是 `static Dictionary`（键 = 模型全路径，`OrdinalIgnoreCase`）配 `CacheLock`，
`TryAcquire` 计数 +1、`Dispose` 计数 -1、**归零才真释放**。
为什么不能缓存在插件实例字段：插件实例是"每次编译一份"，同一模型被 5 个流程引用就加载 5 份（CPU 版每份几十 MB，接 GPU 就是显存按会话占）。
`Plugin.Ocr`、`Plugin.CodeReader` 那种实例字段懒加载的写法在这里**不能照抄**（它们的模型小、纯 CPU）。

`Session.Run` 本身线程安全，但缓存字典的读写来自编译线程、UI 线程、HTTP 线程，所以锁在 `CacheLock` 上。
会话配置：`IntraOpNumThreads` 与 `InterOpNumThreads` 取 1（现场要的是同一张图给同样结果，不是榨干 CPU）；
ORT 日志级别压到 ERROR（默认 WARNING 会刷 `Initializer xxx appears in graph inputs...` 这类**模型导出质量**提醒，把真实日志淹没）。
32 位进程加载 win-x64 原生库会抛难懂的 `DllNotFoundException`，所以在 `TryAcquire` 里先判掉。

## 六 模型自描述

`YoloModelInfo.FromSession` 读输入输出名与形状、元数据里的 `names`、`task`、静态输入尺寸。
`ParseNames` 不能用 JSON 解析：Ultralytics 写的是 Python 字面量 `{0: 'person', 1: 'bicycle'}`（键没引号），
按 JSON 解会抛异常并表现为"读不到类别名"，误导人去怀疑元数据缺失，所以手动按 `{`、`}`、`,` 切。
类别名读不到时用 `class_N` 占位，不编造名字。

## 七 裁剪（BestCrop）

`YoloCrop.CropBest` 取置信度最高者（列表已降序），没目标返回 null 而不是随便裁一块。
顺序是**先夹位置再算可用尺寸**，反过来会算出负宽。实测 `crop_part` **越界不报错**，
只有 Row 为负才抛 #1301，所以指望 HALCON 兜底会得到一张"尺寸看着合理、内容错位"的图。
夹取发生时通过 `note` 回传说明，由插件写 `Logger.Warn($"YOLO 最佳目标裁剪：{note}")`。

## 八 配置界面状态契约（YO3）

配置视图的判据**只认枚举状态**，不再从 `ModelSummary` / `ResultSummary` / `Hint` 的中文文案倒推——
文案是给人看的，改一个字或一个标点就会让 `Contains("失败")` 这类判据静默失效。
状态定义在 `YoloPreviewState.cs`：

| 状态 | 取值 | 写入口 | 界面消费 |
| --- | --- | --- | --- |
| `ModelState` | `NotLoaded / Ready / Failed` | `RefreshModelState()` | 模型信息卡、预览错误态 |
| `PreviewState` | `Idle / Ready / Empty / Error` | `ShowUpstreamImage()` / `TryPreviewDetect()` | 结果卡配色、只读角标、空态/错误态 |

- `RefreshModelState()` 是模型态的**唯一**写入口：`OnModelPathChanged`、`Initialize(stepData)`、`OnViewLoaded` 都调它。
  它先 `EnsureSession()`，再按"有会话 = `Ready` / 路径为空 = `NotLoaded` / 其余 = `Failed`"写 `ModelState`，
  最后发一次 `ModelSummary` 通知。
- `EnsureSession()` **不再**发 `ModelSummary` 通知（该方法会被 `ModelSummary` 的 getter 调到，在 getter 里发通知等于自我重入）。
  这是与旧版的差异；刷新统一由明确入口驱动。
- `ShowUpstreamImage()`：成功 → `PreviewState = Idle` 且 `ResultSummary = "尚未试算"`（换图后旧结论作废）；
  失败（拷贝图抛异常）→ `PreviewState = Error`，`Hint` 保留原始错误。
- `TryPreviewDetect()`：无图 → `Idle`；失败 → `Error`，**只有** `_sessionError` 非空（路径/模型/task 级失败）才把
  `ModelState` 置 `Failed`；成功 → `YoloPreviewStateRules.FromDetectionCount(result.Count)`（0 → `Empty`，>0 → `Ready`），
  `ResultSummary` 的原文案一字不改。
- `ModelState` / `PreviewState` 都是只读运行态（`private set`），**不是 `[StepConfig]`**，`OnConfirm` 不会写进 `InputValues`。
- 视图侧对应关系：`ResultCardStyle` / `ResultTextStyle` 的 Error/Empty 配色、`StatusTextStyle` 的红色提示、
  只读角标、空态、错误态全部改为 `DataTrigger` 绑枚举（`{x:Static local:YoloPreviewState.*}` / `YoloModelState.*`）。
  两个旧转换器 `ResultStateConverter` / `YoloStateConverter` 已删除。

## 九 移植到其它插件时的注意点

`DlClassifyPlugin` 与本插件现在采用**同一套**状态契约（`DlModelState` + `RefreshModelState`），
差别只在分类没有"0 目标"这一档，所以不需要 `PreviewState`。
移植时不要把状态判断重新写回视图：视图只允许读状态，写状态一律留在插件（`RefreshModelState` / 试算方法）。
