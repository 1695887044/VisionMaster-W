# 图像质量检查 · 不变量清单

> 分类：图像处理 ｜ 来源：插件 Plugin.ImageQuality

开发页。每当有人改这个插件的代码，按这张清单逐条过一遍。
**每条都写了“违反后的症状”和“怎么发现”**——只列结论的清单没人会真的去查。

## A. 算法正确性

### A1 对焦必须在 real 域算 Sobel，不能直接在 byte 域

**判据**：`convert_image_type(gray, "real")` 之后才 `sobel_amp(..., "sum_abs", 3)`。

**违反后的症状**：byte 域 Sobel 的 `sum_abs` 会饱和到 255，清晰图与更清晰图的分差被削平，
`FocusRaw` 在高梯度区“顶住不动”，散焦序列的单调性变差。最坏的是它不报错。

**怎么发现**：合成棋盘图做 Gaussian blur 扫描，`FocusRaw` 必须单调下降；
若高 sigma 与低 sigma 的差明显小于预期，先查是否漏了 real 转换。

### A2 曝光只数两端截断，不是平均亮度

**判据**：`ExposureRaw = 100 × (hist[0] + hist[last]) / total`。

**违反后的症状**：把平均灰度当曝光分，深色良品会被判“欠曝”，而过曝图因为均值居中反而通过。
现场表现是“越调越乱”。

**怎么发现**：构造 10% 像素=0、10% 像素=255、80% 像素=128 的图，`ExposureRaw` 必须 ≈20%；
再构造全 100 的图，`ExposureRaw` 必须是 0（尽管均值只有 100）。

### A3 对比度用 P5~P95，不是 min~max

**判据**：累计直方图取 5% 与 95% 分位，`ContrastRaw = P95 − P5`。

**违反后的症状**：一个坏点就能把 min 拉到 0、max 拉到 255，对比度恒为 255，
低对比图看起来“对比度满分”。

**怎么发现**：在纯灰图上放 1 个 0 和 1 个 255 的像素，`ContrastRaw` 必须仍是 0（或接近 0），
不能变成 255。

### A4 饱和度只对彩色图算；灰度图 NaN / 0

**判据**：`channels == 1` → `HasColor=false`、`SaturationRaw=double.NaN`、`SaturationScore=0`、
`Details` 写“跳过”。只有 3/4 通道才 `decompose3 + trans_from_rgb`。

**违反后的症状**：灰度图强行算饱和度，会得到 0 或某个常数，现场误判“饱和度极低”；
更坏的是把 0 当有效值参与阈值。

**怎么发现**：灰度图跑一遍，`double.IsNaN(plugin.SaturationRaw.Value)` 必须为 true；
彩色纯红图跑一遍，`SaturationRaw` 必须接近 255。

### A5 uint2 必须归一化到 0~255 尺度

**判据**：`FocusRaw`、`ContrastRaw`、`MeanGray`、`P5/P95`、`StdDev` 都乘 `255 / 满量程`
（byte=255，uint2=65535）；uint2 彩色转 byte 用 `scale_image`，不用 `convert_image_type` 直接截顶。

**违反后的症状**：uint2 图的梯度/灰度是 byte 的 256 倍，同一组参考值在换相机后完全失效；
`convert_image_type` 直接截顶会把高亮区压成同一个值，饱和度失真。

**怎么发现**：同一图案分别造 byte 与 uint2 版本，`FocusRaw`/`ContrastRaw` 应在同一量级
（允许量化误差），不能差 256 倍。

### A6 分数必须夹到 [0,1]，参考值必须 >0

**判据**：`Clamp01(raw / max(ref, 0.01))`；四个参考值都有 `OnXxxChanging` 夹取。

**违反后的症状**：`FocusRaw` 超过参考值时分数 >1，阈值写 [0,1] 就永远通过；
参考值为 0 时除零得到 Infinity/NaN，下游比较全乱。

**怎么发现**：把 `FocusRef` 设成 0.001（会被夹到 0.01），`FocusScore` 必须 ≤1；
把 `FocusRef` 设成 1，`FocusScore` 必须正好 1（不是 391）。

### A7 ROI 必须真正缩小计算域

**判据**：`Roi` 非空时先 `reduce_domain`，后续所有指标都用缩小后的 `work` / `domain`。

**违反后的症状**：只在最后把结果乘个系数、或只对某些指标用 ROI，
会出现“改了 ROI 指标不变”或“部分指标按全图算”。

**怎么发现**：同一张图，ROI 只框左半与只框右半，指标应不同；
ROI 完全在图外时，必须失败（`ROI 有效像素 … 小于下限`）。

## B. 资源与句柄

### B1 中间对象逐层释放，输入对象不碰

**判据**：`Measure` 的 `finally` 释放 `work/domain/gray/colorByte/real/edge/saturation/hue/value/r/g/b`；
不释放入参 `image` / `roi`。

**违反后的症状**：跑批时非托管内存持续上涨；或把上游图释放掉，下游拿到野指针（`#4051`/崩溃）。

**怎么发现**：连续跑 500 轮，看进程私有内存是否回落；串一个下游「结果展示」读端口，连跑不崩。

### B2 没有输出图像端口，不存在“交端口后不能 Dispose”

**判据**：本插件所有输出都是数值/字符串/字符串数组；没有 `OutputPort<HImage>`。

**违反后的症状**：若将来加图像输出却照抄“中间图 finally 释放”，端口会挂野指针。

**怎么发现**：`grep "OutputPort<HImage>"` 必须零命中；新增图像输出时按基类
`AutoDisposeRoundOutputs` 的约定，交端口后不再手动释放。

## C. 守卫与判定

### C1 不支持的类型/通道必须明确失败

**判据**：类型不是 byte/uint2、通道不是 1/3/4、灰度化后类型异常 → `InvalidOperationException`，
插件层 `Fail(ex.Message)`，文案说清支持范围。

**违反后的症状**：静默转 byte（截顶）或静默当灰度，得到“看着有数其实错”的指标。

**怎么发现**：造一张 `real` 图、一张 2 通道图，`Success` 必须 false 且文案命中。

### C2 `Min > Max` 必须在算之前失败

**判据**：`EnableThreshold=true` 时先校验四组区间；非法则 `Fail` 并列出哪一组，`return`。

**违反后的症状**：自动交换两端会掩盖填错；算完再给 false 会让现场以为“图不好”。

**怎么发现**：`FocusMin=0.9`、`FocusMax=0.1`，`Success` 必须 false，文案含“阈值区间非法”。

### C3 `IsUsable=false` 不许改 `Success`

**判据**：阈值判定只写 `IsUsable`/`Reason`，不调 `Fail`。

**违反后的症状**：质量告警中断流程；现场无法区分“图不好”和“插件坏”。

**怎么发现**：设一个必然越界的阈值，`Success` 必须仍为 true、`IsUsable=false`。

### C4 默认必须 metrics-only

**判据**：`EnableThreshold` 默认 false；此时 `IsUsable=true`、`Reason="未启用阈值判定（metrics-only）"`。

**违反后的症状**：新装的插件默认就拦截，良品率“看起来暴跌”，现场第一反应是关插件。

**怎么发现**：新建步骤直接跑，`IsUsable` 必须 true。

### C5 ROI 太小必须失败

**判据**：`area < MinRoiPixels`（默认 16）→ `InvalidOperationException`。

**违反后的症状**：几个像素也能算出“正常”的均值，指标不可信但没有任何提示。

**怎么发现**：造一个 2×2 的 ROI，`Success` 必须 false。

## D. 契约与工程

### D1 输入端口取值一律 `ActualValue`

**判据**：`Image.ActualValue` / `Roi.ActualValue`。

**违反后的症状**：`Value` 是手动值，端口链接上游时可能是默认值；会出现“接了图却报未连接”或“用了旧图”。

**怎么发现**：`grep -n "\\.Value" ImageQualityPlugin.cs`，输入端口处出现 `Value` 就是违规。

### D2 不写全局变量

**判据**：全工程无 `GlobalVariables.TryWrite`、无 `IExecutionContext.GlobalVariables` 使用。

**违反后的症状**：绕过类型守门与权限；下游无法审计；换方案时全局变量残留。

**怎么发现**：`grep -rn "GlobalVariables" Plugins/Plugin.ImageQuality/` 必须零命中。

### D3 不碰窗口 API

**判据**：全工程无 `OpenWindow` / `DispText` / `SetColor(HObject, ...)`。

**违反后的症状**：违反 R36（离屏烘焙只许 `AnnotationBaker`）；或在断言宿主里无窗口可开，直接崩。

**怎么发现**：`grep -rn "OpenWindow\\|DispText" Plugins/Plugin.ImageQuality/` 必须零命中。

### D4 service 不许有 static 字段

**判据**：`ImageQualityService` 只有方法和局部变量；`QualityMetrics` 是实例类。

**违反后的症状**：`[ParallelSafe]` 的前提被破坏——并行分支共享缓冲，偶发串图/错数。

**怎么发现**：`grep -n "static " ImageQualityService.cs`，命中处必须是方法或类声明。

### D5 工程必须在 `Plugins\` 下并登记 sln / Tiers

**判据**：工程在 `Plugins\Plugin.ImageQuality\`；`VisionMaster.sln` 有 Project 条目；
`HelpManualCheck.Tiers` 有 `["ImageQualityPlugin"]`。

**违反后的症状**：
- 不在 `Plugins\`：不投递 `Modules\`，宿主扫不到；
- 不在 sln：改代码后 `Modules\` 还是旧 DLL，且构建无报错；
- 不在 Tiers：HM16 判红，手册闸门过不去。

**怎么发现**：`grep -c "Plugin.ImageQuality" VisionMaster.sln` ≥1；
`grep -n "ImageQualityPlugin" FlowCanvasChecks/HelpManualCheck.cs` 命中；
构建后 `Modules\Plugin.ImageQuality.dll` mtime ≥ 源码 mtime。

### D6 改过 `.md` 必须重建并核资源数

**判据**：`Help\` 下 `.md` 数 = DLL 内 `.Help.` 内嵌资源数；DLL mtime ≥ `.md` mtime。

**违反后的症状**：帮助弹窗还是旧文案，或新页没打进去（增量构建静默漏打包）。

**怎么发现**：`ImageQualityChecks` 的 D6 断言直接数 `GetManifestResourceNames()`；构建后跑一次。

## E. 断言映射（`FlowCanvasChecks/ImageQualityChecks.cs`）

| 断言组 | 钉住的不变量 | 反向判据 |
| --- | --- | --- |
| 对焦单调 | A1、A7 | 去掉 real 转换/去掉 Sobel → 单调性变差 |
| 曝光截断 | A2 | 改成平均灰度 → 全 100 的图会变非 0 |
| 对比度 P5/P95 | A3 | 改成 min/max → 单坏点图会变 255 |
| 饱和度跳过 | A4 | 灰度图强行算 → NaN 断言红 |
| uint2 归一化 | A5 | 去掉 255/65535 → 数值差 256 倍 |
| 分数夹取 | A6 | 去掉 Clamp01 → 分数 >1 |
| 类型/通道守卫 | C1 | 放行 real/2 通道 → Success 仍 true |
| 阈值语义 | C2、C3、C4 | 把 IsUsable=false 改成 Fail → Success 变 false |
| 端口/配置契约 | D1、D5、D6 | 改端口类型/漏登记 → 断言红 |
| 真图趋势 | A1、A7 | `pcb_focus` 以 061 为锚，Spearman ≤ −0.95 |

> 写断言时的通用纪律：**每一条都要能反向证伪**——“改坏了会不会变红”。
> 一条断言如果改坏代码仍然绿，它就不是断言，只是装饰。
