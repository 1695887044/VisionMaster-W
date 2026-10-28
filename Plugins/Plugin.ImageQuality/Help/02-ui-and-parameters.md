# 图像质量检查 · 界面与参数

> 分类：图像处理 ｜ 来源：插件 Plugin.ImageQuality

本插件没有专属配置视图。参数走宿主通用属性面板（`FlatPropertyGrid` 读 `[StepConfig]` + `[Display]`），
结果走输出端口与 `Details` 文本。少一个视图就少一份 XAML 与 VM 同步成本，也少一类资源作用域坑。

## 参数面板（按声明顺序）

### 归一化参考值

| 参数 | 默认 | 作用 |
| --- | --- | --- |
| `FocusRef` | 200.0 | `FocusScore = clamp(FocusRaw / FocusRef, 0, 1)` |
| `ContrastRef` | 128.0 | `ContrastScore = clamp(ContrastRaw / ContrastRef, 0, 1)` |
| `SaturationRef` | 128.0 | `SaturationScore = clamp(SaturationRaw / SaturationRef, 0, 1)` |
| `MaxTruncationPercent` | 5.0 | `ExposureScore = clamp(1 − ExposureRaw / MaxTruncationPercent, 0, 1)` |
| `MinRoiPixels` | 16 | 有效像素低于该值直接失败，避免小样本指标不可信 |

三个参考值都夹取为 `≥ 0.01`；`MaxTruncationPercent` 夹取为 `≥ 0.001`；`MinRoiPixels` 夹取为 `≥ 1`。
夹取钩子是 `partial void OnXxxChanging(ref T value)`：界面上填越界值、老方案存了越界值，都会在这里被夹回，
不会抛异常，也不会静默走极端分支。

### 可选阈值

| 参数 | 默认 | 作用 |
| --- | --- | --- |
| `EnableThreshold` | false | 关闭时只出指标；打开后按下面四组 Min/Max 判 `IsUsable` |
| `FocusMin` / `FocusMax` | 0 / 1 | 对焦分允许区间（0~1） |
| `ExposureMin` / `ExposureMax` | 0 / 1 | 曝光分允许区间 |
| `ContrastMin` / `ContrastMax` | 0 / 1 | 对比度分允许区间 |
| `SaturationMin` / `SaturationMax` | 0 / 1 | 饱和度分允许区间；灰度图跳过这一组 |

所有 Min/Max 都夹取到 `[0,1]`。**填的是分数，不是原始值**：
要按“对焦原始值 ≥ 150”拦截，先把 `FocusRef` 设成 150，再把 `FocusMin` 设成 1.0；
或者直接把 `FocusMin` 设成 `150 / FocusRef`。

`Min > Max` 是配置错误：运行时会直接 `Fail`，文案列出是哪一组（对焦/曝光/对比度/饱和度），
不会悄悄交换两端，也不会算完再给一个看不懂的 false。

## 输出端口怎么读

| 端口 | 类型 | 单位/范围 | 读法 |
| --- | --- | --- | --- |
| `FocusRaw` | double | 8bit 等效梯度 | sobel_amp('sum_abs') 均值；越大越清晰 |
| `FocusScore` | double | 0~1 | `FocusRaw / FocusRef` 夹取 |
| `ExposureRaw` | double | %（0~100） | 0 与满量程两端截断像素占比之和 |
| `ExposureScore` | double | 0~1 | 1 − 截断占比 / `MaxTruncationPercent` |
| `ContrastRaw` | double | 0~255 | P95 − P5（灰度展宽） |
| `ContrastScore` | double | 0~1 | `ContrastRaw / ContrastRef` |
| `SaturationRaw` | double | 0~255 / NaN | HSV 的 S 通道均值；灰度图为 NaN |
| `SaturationScore` | double | 0~1 | `SaturationRaw / SaturationRef`；灰度图为 0 |
| `HasColor` | bool | — | true = 3/4 通道；false = 灰度 |
| `IsUsable` | bool | — | 默认 true；启用阈值且任一项越界为 false |
| `Reason` | string | — | 未启用阈值 = `metrics-only`；启用时写越界项 |
| `Details` | string[] | — | 逐项明细（见下） |

## `Details` 逐行含义

典型输出（灰度图）：

```
图像: 752×480, 1 通道, 类型 byte, 有效像素 360960
对焦: raw=391.70 score=1.000 (sobel_amp sum_abs 均值, 参考 200.0)
曝光: raw=0.000% score=1.000 (截断 低=0.000% 高=0.000%, 均值=83.9, 范围=0..255)
对比度: raw=126.00 score=0.984 (P5=12.0 P95=138.0, 标准差=41.2, 参考 128.0)
饱和度: 跳过（灰度图；饱和度只对彩色图有意义）
阈值: 未启用（metrics-only；IsUsable 恒 true）
```

- **图像行**：尺寸、通道数、位深类型、有效像素（ROI 内或全图）。
- **对焦行**：`raw` 是 8bit 等效梯度均值；`score` 是除以 `FocusRef` 后的值。
- **曝光行**：`raw` 是低端 + 高端截断百分比之和；后面分别给低/高截断、归一化均值、最小/最大灰度。
- **对比度行**：P5 / P95 / 标准差都在 0~255 尺度；`raw = P95 − P5`。
- **饱和度行**：彩色图给原始值与分数；灰度图明确写“跳过”。
- **阈值行**：未启用时写 metrics-only；启用时写四组区间；有越界时再加一行“判定: …”。

## `IsUsable` 与 `Success` 的区别

这是本插件最容易误读的一点：

| 端口 | 含义 | 什么时候 false |
| --- | --- | --- |
| `Success` | 本步骤**执行**是否成功 | 输入没接、图像类型不支持、ROI 太小、阈值区间填错、HALCON 异常 |
| `IsUsable` | 按配置阈值，**图像质量**是否达标 | 只有 `EnableThreshold=true` 且某项分数越界 |

质量不达标时 `Success` 仍为 true，`IsUsable=false`、`Reason` 写原因。
这样下游还能继续读指标、留证据图、记数据；是否拦截由流程分支决定。

## 没有预览窗口

本插件不输出图像、不调 `PublishPreview`、不打开 HALCON 窗口。原因：

- 它只产数值，没有“需要看图才能理解”的结果；
- 上屏层属于「结果展示」的职责，本插件不重复；
- 离屏窗口在本仓只允许 `AnnotationBaker` 使用（R36），本插件不碰。

需要在界面上看数值时，把 `FocusScore` / `ExposureScore` / `Details` 接到「结果展示」或「数据记录」。
