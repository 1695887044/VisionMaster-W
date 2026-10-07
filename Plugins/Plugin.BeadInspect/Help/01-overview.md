# 胶路检测（Bead 检测）

> 分类：缺陷检测 ｜ 来源：插件 Plugin.BeadInspect

沿一条**参考路径**检查胶路：检出「缺胶 / 太细 / 太粗 / 位置偏移」四类缺陷段，
输出逐段长度与坐标、错误段轮廓与标注图；支持多配方（按产品型号选）与毫米输出。

## 原理（三段）

1. **对齐**——把待检图对齐到参考位姿。`AlignMode` 三档：
   - `None`：不对齐。相机与工件位置固定、来料姿态稳定的场合用；
   - `PlanarDeformable`（默认）：平面可变形对齐，适合印刷、柔性件这类有轻微形变的工件；
   - `PoseFromMatching`：接上游匹配插件的 `AlignedImage`，按刚性位姿对齐。**产线首选**，最快也最稳。
2. **建模型**——按参考路径与目标胶宽建 bead 检测模型；同参数连续运行时复用缓存，不重复重建。
3. **检测**——沿路径提取左右胶路轮廓，按宽度与位置容差分段判定，汇总四类错误段。

## 输入端口

| 端口 | 必填 | 说明 |
| --- | --- | --- |
| `SrcImage` | 是 | 待检测图像 |
| `RecipeName` | 否 | 配方名 / 产品型号。不连或为空 → 用「默认配方」，仍无 → 用库里第一条 |
| `AlignedImage` | 否 | 仅 `PoseFromMatching` 模式使用（接匹配插件同名输出） |
| `RoiRegion` | 否 | 可选检测范围（缩小处理区提速）。不连 = 整图；坐标不因它改变 |
| `Transform` | 否 | 毫米换算的首选来源（接标定插件的 `Transform` 输出） |

## 输出端口

| 端口 | 说明 |
| --- | --- |
| `IsOk` | 判定结果（true = 合格） |
| `NgReason` | NG 原因（如「缺胶 2 段、太细 1 段」）；OK 时为空串 |
| `AnnotatedImage` | 标注图：对齐图 + 参考路径 + 左右轮廓 + 错误段红标 + 判定文字 |
| `ErrorCount` | 错误段总数（已按 `MinErrorLength` 过滤） |
| `ErrorTypes` / `ErrorLengths` / `ErrorRows` / `ErrorCols` | 各错误段的类型 / 长度 / 中心坐标，逐项对齐 |
| `TotalErrorLength` | 错误段总长度（单位随 `UnitOutput`：像素或毫米） |
| `NoBeadCount` / `TooThinCount` / `TooThickCount` / `MispositionCount` | 四类错误的段数：缺胶 / 太细 / 太粗 / 位置偏移 |
| `ErrorSegments` / `BeadContours` | 错误段轮廓集合 / 检出的左右胶路轮廓（可直接给下游或存图） |
| `AlignedImage` | 对齐后的图（供下游复用；`OutputAlignedImage` 关闭时输出空） |

## 关键参数

> 完整参数与取值范围以流程中该步骤的「模块参数」界面为准，这里只解释含义。

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| `TargetWidth` | 15 | 目标胶宽（像素）。下限 6 是硬约束（低于它 HALCON 建模型直接报错），赋值处会夹紧 |
| `WidthTolerance` | 8 | 胶宽容差（像素）：宽于/细于此幅度的段判「太粗 / 太细」 |
| `PositionTolerance` | 30 | 位置容差（像素） |
| `MinErrorLength` | 5 | 错误段长度下限：短于它的碎段不计数（过滤小误报） |
| `Polarity` | Dark | 胶的明暗极性（深色胶 / 浅色胶）；配方未单独指定时用它 |
| `MinScore` | 0.4 | 平面匹配最低分数（0~1），低于它判对齐失败 |
| `PlanarNumLevels` | 5 | 平面匹配金字塔层数（1~10） |
| `FindAngleStartDeg` / `FindAngleExtentDeg` | -22.35 / 44.69 | 平面匹配的起始角 / 角度范围（度） |
| `FindScaleRMin` / `RMax` / `CMin` / `CMax` | 1.0 | 行向 / 列向缩放搜索范围（1 = 不允许缩放） |
| `UnitOutput` | Pixel | 长度输出单位：像素 / 毫米 |
| `PixelSizeMm` | 0 | 像素当量（mm/px）；`UnitOutput=Mm` 且未接 `Transform` 时用它兜底 |
| `DisplayViewIndex` | 1 | 标注图发布到主界面几号视图窗口（1~9），0 = 不发布 |
| `OutputAlignedImage` | true | 是否输出对齐后的图 |
| `RecipeLibraryJson` / `DefaultRecipeName` | — | 配方库与默认配方名，由配置界面维护，不建议手改 |

## 典型用法（配合模板匹配）

1. 上游接「模板匹配」做定位，它的 `AlignedImage` 输出连到本算子；
2. 本算子 `AlignMode` 选 `PoseFromMatching`，`AlignedImage` 端口接上一步；
3. 下游用 `IsOk` / `NgReason` 分流（合格放行、NG 报警），或接 `ErrorSegments` 存图留证。
