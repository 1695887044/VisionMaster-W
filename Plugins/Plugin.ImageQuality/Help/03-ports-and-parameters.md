# 图像质量检查 · 端口与参数

> 分类：图像处理 ｜ 来源：插件 Plugin.ImageQuality

## 输入端口（2 个）

| 端口 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `Image` | `HImage` | **是** | 待检查图像。空或未初始化时失败：`输入图像未连接（Image 端口为空）…` |
| `Roi` | `HRegion` | 否 | 只检查区。非空时先 `reduce_domain`，所有指标只在该区域内算；不接则用图像自带域 |

取值一律走 `ActualValue`（强类型）。`Value` 是“手动值”的弱接口，
端口链接上游时它可能是默认值而不是上游值。

### 支持的图像

| 项 | 支持范围 | 不支持时的行为 |
| --- | --- | --- |
| 位深类型 | `byte`、`uint2` | 明确失败：`图像类型 {type} 不受支持；v1 只支持 byte / uint2…` |
| 通道数 | 1（灰度）、3（RGB）、4（RGBA） | 明确失败：`图像通道数为 {n}，v1 只支持 1 / 3 / 4` |
| 有效像素 | ≥ `MinRoiPixels`（默认 16） | 明确失败：`ROI 有效像素 … 小于下限 …` |

不支持的类型**不会静默转换**：`real` / `int4` 请先在上游「预处理」转成 `byte`。
静默缩放或截顶会得到“看着有数其实错”的指标，比直接红更坏。

## 输出端口（12 个）

### 原始值（给现场调阈值）

| 端口 | 类型 | 单位 | 口径 |
| --- | --- | --- | --- |
| `FocusRaw` | double | 8bit 等效梯度 | `sobel_amp(gray_real, 'sum_abs', 3)` 的均值；uint2 图按 `255/65535` 归一化 |
| `ExposureRaw` | double | %（0~100） | `(直方图 0 箱 + 满量程箱) / 总像素 × 100` |
| `ContrastRaw` | double | 0~255 | `P95 − P5`（灰度直方图累计分布） |
| `SaturationRaw` | double | 0~255 / NaN | HSV 的 S 通道均值；灰度图为 `NaN` |

### 归一化分（0~1，给阈值用）

| 端口 | 类型 | 公式 | 灰度图 |
| --- | --- | --- | --- |
| `FocusScore` | double | `clamp(FocusRaw / FocusRef, 0, 1)` | 正常计算 |
| `ExposureScore` | double | `clamp(1 − ExposureRaw / MaxTruncationPercent, 0, 1)` | 正常计算 |
| `ContrastScore` | double | `clamp(ContrastRaw / ContrastRef, 0, 1)` | 正常计算 |
| `SaturationScore` | double | `clamp(SaturationRaw / SaturationRef, 0, 1)` | 固定 0（无意义） |

`clamp` 都是夹到 `[0,1]`；参考值 ≤0 时按 0.01 处理（夹取钩子保证实际不会 ≤0）。

### 判定与明细

| 端口 | 类型 | 说明 |
| --- | --- | --- |
| `HasColor` | bool | true = 3/4 通道；false = 灰度。饱和度是否有效看它 |
| `IsUsable` | bool | 默认 true；启用阈值且任一项越界为 false。**不改变 `Success`** |
| `Reason` | string | 未启用阈值 = `未启用阈值判定（metrics-only）`；启用时写越界项 |
| `Details` | string[] | 逐项明细；灰度图的饱和度行会明确写“跳过” |

> `Success` / `ErrorMessage` 由基类提供：执行成功 `Success=true`；输入/算法/配置错误时
> `Success=false` 且 `ErrorMessage` 写明原因。质量分不达标**不会**让 `Success=false`。

## 配置项（14 个，按属性面板顺序）

| 属性 | 类型 | 默认 | 取值域 | 说明 |
| --- | --- | --- | --- | --- |
| `FocusRef` | double | 200.0 | ≥0.01（夹取） | 对焦参考值：`FocusRaw` 达到它时对焦分记 1 |
| `ContrastRef` | double | 128.0 | ≥0.01（夹取） | 对比度参考值（0~255 尺度） |
| `SaturationRef` | double | 128.0 | ≥0.01（夹取） | 饱和度参考值（0~255 尺度） |
| `MaxTruncationPercent` | double | 5.0 | ≥0.001（夹取） | 截断总占比达到它时曝光分记 0 |
| `MinRoiPixels` | int | 16 | ≥1（夹取） | 有效像素下限；低于它直接失败 |
| `EnableThreshold` | bool | false | — | 是否启用四组 Min/Max 判定 |
| `FocusMin` / `FocusMax` | double | 0 / 1 | [0,1]（夹取） | 对焦分允许区间 |
| `ExposureMin` / `ExposureMax` | double | 0 / 1 | [0,1]（夹取） | 曝光分允许区间 |
| `ContrastMin` / `ContrastMax` | double | 0 / 1 | [0,1]（夹取） | 对比度分允许区间 |
| `SaturationMin` / `SaturationMax` | double | 0 / 1 | [0,1]（夹取） | 饱和度分允许区间；灰度图跳过 |

所有数值参数都挂了 `partial void OnXxxChanging(ref T value)` 夹取钩子：
界面上填越界值、老方案里存了越界值，都会在这里被夹回合法区间。

`Min > Max` 不在 setter 里自动交换——那是掩盖配置错误。运行时会 `Fail` 并列出哪一组非法。

## 三个“原始值 → 分数”的换算例子

| 场景 | 参数 | 结果 |
| --- | --- | --- |
| `FocusRaw=150`，`FocusRef=200` | 默认 | `FocusScore=0.75` |
| `ExposureRaw=2.5%`，`MaxTruncationPercent=5%` | 默认 | `ExposureScore=0.5` |
| `ContrastRaw=64`，`ContrastRef=128` | 默认 | `ContrastScore=0.5` |
| `SaturationRaw=255`，`SaturationRef=128` | 默认 | `SaturationScore=1.0`（夹取） |

## 分数阈值的写法

阈值填的是**分数**（0~1），不是原始值。两种等价写法：

1. **先标参考值，再填分数**：良品 `FocusRaw` P5≈150 → `FocusRef=150`，要“低于良品下限就拦” →
   `FocusMin=1.0`（因为良品下限对应分数 1.0）。
2. **固定参考值，反算分数**：`FocusRef=200`，要“原始值 ≥150” → `FocusMin=150/200=0.75`。

第一种更好维护：换产品只改参考值，阈值语义不变。

## `.vms` 里存什么

本步骤只存**参数值**（上表 14 项），不存图。
`Image` / `Roi` 的值来自上游步骤：图随上游持久化或每轮刷新，ROI 几何形状存在「创建 ROI」那一步里。

因此：**换 ROI 改的是上游步骤，不是本步骤**。排查“换了 ROI 指标没变”时先看上游。
