# 深度学习分割 · 端口与参数

> 分类：图像处理 ｜ 来源：插件 Plugin.DlSegment

本篇是**查表用**的：端口叫什么、什么类型、必不必须接、参数默认值是多少、`.vms` 里存的是什么。判断与调参看 04-criteria-and-tuning.md。

## 输入端口

| 端口 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `Image` | `HImage` | 否 | 待分割的图像。允许空着建节点，执行时为空会明确失败（见下"空值文案"） |

只有一个输入。分割**不吃 ROI 端口**：想只看某个区域，请在上游用「创建ROI」裁图再进来——本插件把喂进来的整幅图送进模型。

`Image` 为空时的失败原文：

> 上游没有图像：请把图像节点的输出连到本节点的 Image。整幅图分割需要一个输入。

## 输出端口

| 端口 | 类型 | 无数据时的值 | 说明 |
|---|---|---|---|
| `ClassMap` | `HImage` | 空（null） | 类别图，**byte 单通道**，像素值 = 类别序号，已缩回原图尺寸 |
| `RegionMask` | `HImage` | 空 | 前景掩膜，前景 255 / 背景 0，已缩回原图尺寸 |
| `Region` | `HObject`（区域） | 空 | 前景区域对象，可直接接面积/形状/几何测量 |
| `RegionCount` | `int` | 0 | 前景连通片数（`connection` 后的对象数） |
| `ForegroundRatio` | `double` | 0 | 前景像素占全图比例，0~1 |
| `ClassAreas` | `double[]` | 空数组 | 各类像素数，下标 = 类别序号，长度 = C |
| `ClassRatios` | `double[]` | 空数组 | 各类占比 0~1，长度 = C |
| `DominantClassIndex` | `int` | -1 | 面积最大的**前景类**下标（全背景时 -1） |
| `DominantClassName` | `string` | 空串 | 上面对应的类别名 |
| `Summary` | `string` | 空串 | 人读摘要，形如 `类别 3 类：background 82.1% / scratch 12.4% / dent 5.5%；前景 17.9%，片数 2` |
| `Success` | `bool` | 基类维护 | 本轮是否成功 |
| `ErrorMessage` | `string` | 基类维护 | 失败原因 |

**基类自带 `Success` / `ErrorMessage`**：写断言统计"本插件自有输出"时必须把这两个剔掉，否则数量对不上（DlAnomaly 阶段就踩过这一条）。

## 两个容易误读的口径

1. **`ClassAreas` 算的是像素数，不是面积**。它乘上像素当量才是 mm²；要真面积请把 `Region` 接到带 `MmPerPixel` 的测量节点，或用 `ClassAreas[i] × mmPerPixel²` 自己算。
2. **`DominantClassIndex` 只在前景类里选**。类别 0 一律当背景，即使背景像素最多也不会成为"主导类"。想要"整幅图里最多的是什么"，直接看 `ClassRatios[0]`。

## 配置参数

全部持久化在 `.vms` 的步骤配置里，改成员名会让旧方案读不回来。

| 参数 | 类型 | 默认 | 范围 / 夹取 | 说明 |
|---|---|---|---|---|
| `ModelPath` | `string` | `""` | — | `.onnx` 路径；绝对路径直接用，相对路径按宿主 exe 目录解析 |
| `ResizeMode` | `SegmentResizeMode` | `Letterbox` | 枚举成员名 | 图像压到模型输入尺寸的方式 |
| `InputSize` | `int` | `0` | `≥ 0`（负数夹成 0） | 0 = 用模型的静态尺寸；动态形状模型必须填 |
| `Interpolation` | `SegmentInterpolation` | `Nearest` | 枚举成员名 | 结果缩回原图时的插值 |
| `RegionMode` | `SegmentRegionMode` | `AllClasses` | 枚举成员名 | 前景口径：全部非背景类 / 只取指定类 |
| `ClassIndex` | `int` | `1` | `0 ~ 255` | 只在 `SingleClass` 下生效 |
| `DisplayViewIndex` | `int` | `1` | `0 ~ 9` | 0 = 不发布预览 |

## 枚举取值与面板文案

**面板显示中文、写入 `.vms` 的是成员名**。改成中文写盘会让旧方案读不回枚举值。

| 枚举 | 成员 | 面板文案 |
|---|---|---|
| `SegmentResizeMode` | `Letterbox` | Letterbox · 等比补边（推荐） |
| | `Stretch` | Stretch · 拉伸填充 |
| `SegmentInterpolation` | `Nearest` | Nearest · 最近邻（推荐） |
| | `Bilinear` | Bilinear · 双线性 |
| `SegmentRegionMode` | `AllClasses` | 全部前景（所有非背景类合并） |
| | `SingleClass` | 只取指定类别 |

## 输出契约三档

| 模型给出的输出 | 本插件行为 |
|---|---|
| 秩 4、`[1,C,H,W]`、`C ≥ 2`、`H/W ∈ 1~8192` | **接受**，正常分割 |
| 秩 4 但 `C == 1` | **拒绝**：「C=1 是单类掩膜，用「阈值」或「Blob」更合适」 |
| 其它秩 / 首维 ≠ 1 / 尺寸越界 | **拒绝**，错误里带真实形状 |

`C > 255` 在推理后拒绝（byte 类别图装不下），不会给出被截断的图。

## `.vms` 里存什么

| 键 | 内容 |
|---|---|
| `ModelPath` | 模型路径字符串（**不存模型本身**） |
| `ResizeMode` / `Interpolation` / `RegionMode` | 枚举成员名 |
| `InputSize` / `ClassIndex` / `DisplayViewIndex` | 整数 |
| 端口值 | `Image` 的连线信息（上游步骤 ID + 端口名）；输出端口不存值 |

模型文件、类别名、阈值都**不进方案**：模型是客户资产，方案要能在别的机器上打开。

## 想做什么 → 接/改哪个

| 想做的事 | 怎么做 |
|---|---|
| 判"有没有缺陷"（任意类都算） | `RegionMode = AllClasses`，把 `ForegroundRatio` 接逻辑判断 |
| 判"划痕有多大" | `RegionMode = SingleClass`、`ClassIndex` = 划痕类，`Region` 接面积测量 |
| 数"有几个缺陷块" | `RegionCount` 直接接显示或判断 |
| 出缺陷叠加图给操作员看 | `ClassMap` 接「结果显示」；或 `RegionMask` 接「结果显示」 |
| 传给 SCADA 做趋势 | `ClassRatios` 接数组上屏，或 `Summary` 接变量写入 |
| 只要最快的跑法 | `DisplayViewIndex = 0`，不发布预览 |

## 契约提醒

- 端口取值一律用 `ActualValue` / `GetTypedValue()`；`Value` 是 `object?` 的弱类型入口。
- `OutputPort<double>.Value` 默认是 `0.0` 不是 null——断言里判"没算"不能用 `== null`。
- 断言里必须调 `plugin.Execute(context)` 而不是 `plugin.RunAlgorithm(context)`：`Success` 只在 `Execute` 开头被预置为 `true`。
