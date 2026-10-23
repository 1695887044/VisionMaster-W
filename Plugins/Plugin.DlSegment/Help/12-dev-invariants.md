# 深度学习分割 · 不变量清单

> 分类：图像处理 ｜ 来源：插件 Plugin.DlSegment ｜ 读者：改这个插件、写断言的人

每条都写"违反后的症状 + 怎么发现"。**没有守门方式的不变量等于不存在**，所以每条末尾都给了判据。

## A. 算法正确性

### A1 差值必须在 float 域做，不许在 byte 域直接算

分割本身不涉及相减，但后处理里凡是"通道间比较 / 缩放的归一化"都必须在 `float` 域：

- `ToNchwFloat` 必须 `bytes[i] / 255f`（float 除法），不是整数除。
- 违反症状：所有像素变成 0 或 1，argmax 全落到同一个类，**不报错**。

**判据**：断言里喂一张已知灰阶（比如全 128），检查张量取值 ≈ 0.502，而不是 0。

### A2 argmax 取"最大值的下标"，不是"最大值"

- 类别是**标签**不是分数。返回 `double` 最大分是错的。
- 违反症状：类别图变成一张灰度渐变图，所有像素落在 0~C 之间且看起来像原图的亮度。

**判据**：构造 C=3、每个像素的 3 个通道为 `[0,0,1]` / `[0,1,0]` / `[1,0,0]`，argmax 必须分别得 2 / 1 / 0。

### A3 C > 255 必须提前拒绝

byte 类别图装得下 0~255。C=300 时 `(byte)index` 会**截断**（300 → 44），得到一张"类别乱掉但看起来正常"的图。

**判据**：`C=300` 的模型加载后执行必须失败，错误里含 `255`。

### A4 缩回原图默认必须 `Nearest`

双线性会对类别序号取平均，产生 1.5 这种**不存在的类**。

**判据**：`Interpolation = Bilinear` 时类别图的像素值集合应仍是原始类别集合的子集（取整后）；若出现介于两个类之间的值，说明实现把"显示插值"与"语义插值"混用了。默认值必须是 `Nearest`（断言 `plugin.Interpolation == Nearest`）。

### A5 补边灰必须是 114

- `PadGrayValue = 114`，不是 0、不是 255。
- 黑边（0）是模型训练时没见过的强边缘，会让画面边缘区域的分类塌掉。
- 违反症状：图像四周一圈分类错误，中间正常。

**判据**：`Assert(SegmentResizePlan.PadGrayValue == 114)`。这是一条**常量断言**，改实现不动断言就会红——刻意如此。

### A6 Letterbox 的几何必须自洽

`scaledWidth = max(1, round(srcW × scale))`、`PadX = (dstW − scaledW) / 2`、`scale = min(dstW/srcW, dstH/srcH)`。

- 违反症状：图被压到一角、或右侧/下侧出现黑带。
- 边界：`round` 后可能超出 `dst` 1 像素，必须夹回。

**判据**：`src=200×100, dst=224×224` → `scaledW=224, scaledH=112, PadX=0, PadY=56`。反向：`src=100×200` → `scaledW=112, PadX=56, PadY=0`。

## B. 资源

### B1 中间 `HObject` 一律 `try/finally` 释放

`ZoomImageSize` / `Compose3` / `ChangeDomain` / `RegionToBin` / `GCHandle.Alloc` 每次都产出新对象。

- 违反症状：跑几千轮后宿主内存持续上涨（HALCON 对象不归 GC 管）。

**判据**：连跑 2000 轮后 `Modules` 侧句柄数（HALCON 自带 `count_seconds` 之外的资源计数）不增长；无法自动断言时列为**人工核对**。

### B2 输出端口上的图不许本插件 Dispose

交给端口后所有权转移给基类，下一轮 `DisposePreviousRoundOutputs` 统一释放。

- 违反症状：下游拿到野指针，或下一轮执行直接崩。
- 特例：`OverpaintGray` 是**就地修改第一个入参**，改完必须把变量换成"新画布"、"旧句柄"置 null，否则 finally 里会释放正在用的对象。

**判据**：断言里连续执行两轮，第二轮结果与第一轮一致（不崩、不空）。

### B3 `GCHandle` 必须 `Free`

`GenImage1` 用固定指针读字节数组，句柄不释放会让数组永远无法被 GC 移动/回收。

**判据**：代码审查 + 2000 轮内存曲线；`TryRestoreToSource` 的 `finally` 里必须有 `handle.Free()`。

## C. 守卫与契约

### C1 只认 `[1,C,H,W]` 且 C ≥ 2

- 秩 4、首维 1、C ≥ 2、H/W ∈ 1~8192。
- 违反症状：检测输出被 argmax 成"看着有模有样但无意义"的图，**不报错**。

**判据**：`[1,84,8400]` 必须被拒；`[1,1,32,32]`（单类掩膜）必须被拒且错误含"阈值/Blob"；`[2,3,32,32]`（batch≠1）必须被拒。

### C2 拒绝文案必须带真实形状

错误里要有「读到的输出：{名字}（秩 n，形状 [...]）」+「不会替你猜」。

**判据**：断言检查错误串同时含三个要素：输出名、`秩`、`形状`。

### C3 空输入必须有明确文案

> 上游没有图像：请把图像节点的输出连到本节点的 Image。整幅图分割需要一个输入。

**判据**：`Image` 留空执行 → `Success == false` 且 `ErrorMessage` 含 `Image`。

### C4 未选模型必须有明确文案

> 尚未选择模型：请在节点配置里选择一个 .onnx 语义分割模型。

**判据**：路径为空执行 → 失败且含「尚未选择模型」。

### C5 会话加载失败不许污染缓存

建会话成功但元数据解析失败时，必须就地 `Dispose` 且**不写进缓存字典**。

- 违反症状：第一次失败后永远失败（缓存里存了个坏条目）。

**判据**：用一个非法 `.onnx` 连续 `TryAcquire` 三次，`CachedSessionCount` 不变。

### C6 路径变了必须换会话

`EnsureSession` 比较路径字符串；变了先 `ReleaseSession()` 再 `TryAcquire`。

- 违反症状：换了模型文件，跑起来还是旧模型的结果（**极难排查**）。

**判据**：连续两次设不同 `ModelPath` 执行，`ModelSummary` 里的输入尺寸/类别数应跟着变。

## D. 插件壳与工程

### D1 `RunAlgorithm` 必须是 `public override`

基类里是 `public`，降级报 CS0507。

**判据**：编译即守门。

### D2 自有输出端口恰好 10 个

`ClassMap` / `RegionMask` / `Region` / `RegionCount` / `ForegroundRatio` / `ClassAreas` / `ClassRatios` / `DominantClassIndex` / `DominantClassName` / `Summary`。

**判据**：`plugin.Outputs.Keys` 剔除 `Success` / `ErrorMessage` 后按名称排序，与期望数组 `SequenceEqual`。

### D3 默认参数固定

`ResizeMode=Letterbox`、`InputSize=0`、`Interpolation=Nearest`、`RegionMode=AllClasses`、`ClassIndex=1`、`DisplayViewIndex=1`、`ModelPath=""`。

**判据**：新建实例逐项断言。

### D4 夹取必须生效

`InputSize` 负数夹 0；`ClassIndex` 夹 0~255；`DisplayViewIndex` 夹 0~9。

**判据**：设 `-5` / `9999` / `42` 后读回，分别是 `0` / `255` / `9`。

### D5 `[Display]` 四件套齐全

`Name=深度学习分割`、`GroupName=图像处理`、`Description` 含"不猜"、`ShortName` 非空。

**判据**：反射读特性逐项断言。

### D6 手册内嵌资源数 == 磁盘 `.md` 数

改了任何一页就要 `--no-incremental` 重建。

**判据**：`ls Help/*.md | wc -l` 与 `tr '\0' '\n' < dll | grep -a "Plugin\.DlSegment\.Help\." | sort -u | wc -l` 相等。

### D7 在 `VisionMaster.sln` 登记

`grep -c "GUID" VisionMaster.sln` 应为 14（1 Project + 12 Config + 1 NestedProjects）。

**判据**：不登记则"VS 生成解决方案"不编它，`Modules\` 里永久停旧版且**没有任何报错**。

### D8 在 `HelpManualCheck.Tiers` 登记档位

**判据**：不登记则 HM16 红（"插件类 N 个 / 表内 M 项；未登记：DlSegmentPlugin"）。

## E. 建议的断言集（SegmentChecks）

| # | 断言 | 反向判据（改坏应红） |
|---|---|---|
| 1 | `PadGrayValue == 114` | 改成 0 |
| 2 | Letterbox 200×100 → 224×112，补边 (0,56) | 把 `Min` 改成 `Max` |
| 3 | `[1,84,8400]` 被拒且错误含 `秩`+`形状`+输出名 | 放宽秩判定 |
| 4 | `[1,1,32,32]` 被拒且错误含"阈值" | 把 `C ≥ 2` 去掉 |
| 5 | `[1,3,32,32]` 接受，`ClassCount == 3` | 把 C 读成 H |
| 6 | 空路径 → 「尚未选择模型」 | 让 `TryAcquire` 空路径返回成功 |
| 7 | 不存在路径 → 「模型文件不存在」+完整路径 | 吞掉异常返回 null 而不给原因 |
| 8 | 默认参数七项全对 | 改任一默认值 |
| 9 | 夹取三处生效 | 删掉 `OnXxxChanging` |
| 10 | 自有输出端口 10 个且名字一致 | 多声明或少声明一个端口 |
| 11 | 空输入 → 失败且含 `Image` | 让空输入直接 return 成功 |
| 12 | Help 内嵌资源数 == 8 | 少写一页 `.md` 不重建 |
