# 图像质量检查 · 内部结构

> 分类：图像处理 ｜ 来源：插件 Plugin.ImageQuality

开发页。讲这个插件由哪几个文件组成、一轮执行走哪些步骤、每个决策为什么这么定。
改代码前先读完这一页；改完照 12 页的不变量清单自检。

## 文件构成

```
Plugins/Plugin.ImageQuality/
├── Plugin.ImageQuality.csproj   net9.0-windows / UseWPF / WPF 插件
├── ImageQualityService.cs       纯 HALCON 算法层（internal static，无状态）
├── ImageQualityPlugin.cs        步骤类：端口 + [StepConfig] + RunAlgorithm
└── Help/                        本手册（8 页，内嵌资源）
```

**没有 `XxxView.xaml`**。本插件不提供专属配置视图，参数走宿主通用属性面板
（`FlatPropertyGrid` 读 `[StepConfig]` + `[Display]`），结果走输出端口与 `Details`。
理由：14 个参数全部是标量/布尔，没有“要在图上点两下才能配”的东西
（对比「模板匹配」要画框、要涂抹掩膜，所以才需要专属视图）。
少一个视图就少一份 XAML 与 VM 同步成本，也少一类 R22/R173 资源作用域坑。

## 三层职责

| 层 | 文件 | 能做什么 | 不能做什么 |
| --- | --- | --- | --- |
| 策略层 | `ImageQualityPlugin.RunAlgorithm` | 判空、ROI 取值、配置校验、填端口、按可选阈值判 `IsUsable` | 不直接调 HALCON 算子 |
| 算法层 | `ImageQualityService` | 调 HALCON 算子、算四项指标与明细 | 不碰端口、不记日志、不读 `[StepConfig]` |
| 数据层 | `ImageQualityService.QualityMetrics` | 承载一轮指标与明细 | 无逻辑 |

分层不是洁癖：`ImageQualityService` 是 `internal static` 且不依赖 `IExecutionContext`，
断言宿主可以通过公开插件面喂合成图、断言端口值；service 本身不碰端口，改算法不会连带改契约。

## 一轮执行：`RunAlgorithm` 的调用链

```
① 取输入（ActualValue，不是 Value）
   Image.ActualValue / Roi.ActualValue
      ↓ 空 → Fail 中文原因，return
② 配置校验（只在 EnableThreshold=true 时）
   Min > Max → Fail 列出哪一组，return（不自动交换）
③ 调 service
   ImageQualityService.Measure(image, roi, FocusRef, ContrastRef, SaturationRef,
                               MaxTruncationPercent, MinRoiPixels)
      ↓ 不支持的类型/通道/过小 ROI → InvalidOperationException
        → 插件层 catch → Fail(ex.Message)，return
④ 填原始值与分数
   FocusRaw / ExposureRaw / ContrastRaw / SaturationRaw
   FocusScore / ExposureScore / ContrastScore / SaturationScore
   HasColor
⑤ 可选阈值判定（EnableThreshold=true）
   四组 Min/Max 各判一次；灰度图跳过饱和度；
   任一越界 → reasons；IsUsable = reasons.Count == 0
⑥ 填 Reason / Details
   Reason = metrics-only 或越界项
   Details = 图像/对焦/曝光/对比度/饱和度/阈值（/判定）逐行
⑦ 结束
   **不调 Fail**（质量不达标 ≠ 执行失败）
```

## 算法层内部流程

```
① 通道数 / 图像类型
   byte / uint2 + 1/3/4 通道，其余抛 InvalidOperationException
② 有效域
   Roi 非空 → area 检查 → reduce_domain
   Roi 为空 → copy_image（不碰上游图）
   get_domain → area_center → PixelCount
③ 灰度化
   1 通道 → copy
   3/4 通道 byte → rgb1_to_gray / rgb3_to_gray
   3/4 通道 uint2 → 三通道 scale_image(255/65535) → byte → rgb1_to_gray
④ 对焦
   convert_image_type(gray, 'real')
   sobel_amp(real, 'sum_abs', 3)
   intensity(edge, edge) → mean
   FocusRaw = mean × (255 / 灰度满量程)
⑤ 曝光 / 对比度
   gray_histo_range(domain, gray, 0, 满量程, 箱数) → byte 256 箱 / uint2 65536 箱
   low/high 箱 → 截断百分比
   累计分布 → P5/P95
   intensity(domain, gray) → 标准差
   ContrastRaw = (P95 − P5) × (255 / 灰度满量程)
⑥ 饱和度（仅彩色）
   ColorToByte（uint2 用 scale_image 线性缩到 byte，不截顶）
   decompose3 → trans_from_rgb('hsv') → intensity(S)
   灰度：HasColor=false，SaturationRaw=NaN，SaturationScore=0
⑦ 明细
   图像 / 对焦 / 曝光 / 对比度 / 饱和度 五行
```

## 每个决策为什么这么定

### 为什么独立成插件，而不是「预处理」的一个算子

`PreProcessing` 的链内算子只有 `OperatorKey + Enabled + 字典参数`，输出是一张处理后的图，
**没有数值指标端口**。成像质量的价值是“给下游一个可配阈值的数”，不是“再出一张图”。
做成独立步骤，指标才能直接接「数据记录」「逻辑判断」「判定表」。

### 为什么 v1 不做脏污

脏污检测在仓内已有五条路：`BlobDetect`（主力）、`DlAnomaly`（图像级异常分）、
`DlSegment`（异色/脏污分割）、`DlClassify`（OK/脏污/划伤分类）、`ImageDiff`（与基准差分）。
再做一个“脏污分”是重复建设，而且没有参考图时只能给“疑似”，容易误导。
v1.1 再评估“有 `ReferenceImage` 时的差分前置分”，并明确它只做前置提示、不替代上述五条路。

### 为什么默认 metrics-only，且阈值不达标不 Fail

质量分是“测量结论”，不是“算法执行失败”。如果 `IsUsable=false` 就 `Fail`：

- 流程会在最需要看后续数据的时候中断；
- 现场无法区分“图不好”和“插件坏了”；
- 判定策略被写死在插件里，换产品要改插件。

所以 v1 只输出 `IsUsable`，拦截交给下游「逻辑判断」/「判定表」。
`Success=false` 只留给输入/算法/配置错误。

### 为什么对焦用 Sobel 而不是 Laplacian

见 04 页：`pcb_focus` 121 张实测 Tenengrad 平滑、Laplacian 方差在近焦段几乎不动。
主指标要能区分“接近焦点的散焦”，所以选 Sobel。

### 为什么灰度指标归一化到 0~255

uint2 图的原始梯度/灰度是 byte 的 256 倍。如果直接输出原生尺度，
同一组参考值在 byte/uint2 之间不可比，换相机就要整套重标。
统一乘 `255 / 满量程` 后，`FocusRef`/`ContrastRef` 的语义跨位深一致；
`Details` 里仍保留“类型”与归一化后的均值/范围，便于排查。

### 为什么 uint2 彩色要先 scale 再转 byte

`convert_image_type(uint2 → byte)` 是**截顶**（>255 直接变 255），不是缩放。
拿它做饱和度会把高亮区全压成同一个值，S 通道失真。
`scale_image(255/65535)` 是线性缩放，保住相对关系，再转 byte。

### 为什么 ROI 太小直接失败

16 个像素也能算出一个“看起来正常”的均值/方差，但统计上没有意义。
直接红比给假分数好；`MinRoiPixels` 可调，但不建议低于 16。

### 为什么图上不画文字 / 不开窗

本插件不输出图像，也没有“必须看图才能理解”的结果。
离屏窗口在本仓只允许 `AnnotationBaker` 使用（R36/R37/R39）；本插件不碰窗口 API。

## 资源释放纪律

`ImageQualityService.Measure` 用一层 `try/finally` 统一释放自己 new 出来的对象：

| 对象 | 释放 | 说明 |
| --- | --- | --- |
| `work` | 是 | ROI 裁剪副本或全图副本 |
| `domain` | 是 | `get_domain` 结果 |
| `gray` / `colorByte` | 是 | 灰度化/彩色转 byte 的中间图 |
| `real` / `edge` | 是 | 对焦计算的中间图 |
| `saturation` + `hue`/`value` + `r/g/b` | 是 | HSV 分解的中间对象 |
| 输入 `image` / `roi` | **否** | 上游的对象，本插件不拥有 |

本插件没有输出图像端口，所以不存在“交给端口的图不能提前 Dispose”的问题；
但中间对象仍必须逐层释放，否则跑批会持续吃非托管内存。

## 构建与投递

- 工程必须在 `Plugins\` 下（红线①），`Plugins\Directory.Build.targets` 会在 Build 后把 DLL
  投递到 `$(SolutionDir)Modules\`。
- 宿主开着时会锁 `Modules\Plugin.ImageQuality.dll`，构建在投递步报 `MSB3027/MSB3021`。
  不要杀进程；加 `-p:ModulesDir=<暂存目录>\` 把投递改道。
- 构建统一加 `-m:1 -nodeReuse:false`（本机多进程构建会静默失败）。
- 改过任何一页 `Help\*.md` 的内容就算产物脏，必须重建本工程，
  并核对 DLL 里的 `.Help.` 内嵌资源数 = 磁盘 `.md` 数。
