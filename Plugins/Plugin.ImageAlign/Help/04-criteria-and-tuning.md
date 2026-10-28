# 图像对齐 · 口径与调参

> 分类：图像处理 ｜ 来源：插件 Plugin.ImageAlign

## 先选模式：2/3/4 点还是上游位姿

| 场景 | 推荐 | 原因 |
| --- | --- | --- |
| 前面已有模板匹配/位姿 | **上游位姿** | 不重复跑匹配；位姿分数可直接透传 |
| 有 4 个可靠标记点、需要透视 | **手动四点** | 透视能吸收倾斜/非等比 |
| 只有 2~3 个点 | **基准点** | 2 点刚性、3 点仿射；别硬凑第 4 点 |
| 点会遮挡/错配 | **4 点 + 验证点** | 4 点无冗余，验证点才能发现错点 |

## 点怎么选

1. **分布要开**：4 点尽量覆盖画幅四角；点挤在一小块时，外推区域误差会被放大。
2. **不要共线**：3 点共线 → 仿射退化；4 点里有 3 点共线 → 透视不稳定。
3. **选稳定特征**：PCB 标记点、标签角点、印刷套准十字；不要选会变形的边缘/反光。
4. **同物理点**：参考点和当前点必须是同一个物理位置；点错一个，4 点透视仍能精确拟合。
5. **先过质量检查**：散焦/过曝图上的点定位会漂；前面接「图像质量检查」拦掉坏图。

## 4 点透视的“残差恒 0”问题

4 对点刚好定 8 个自由度（单应矩阵 8 个独立参数），方程数 = 8，**总是能精确解**。
所以：

- `Residual` 对 4 点没有诊断力；
- 第 4 点挪 5px，仍能拟合出一个“看起来正常”的透视矩阵；
- 图上可能只表现为轻微错位，差分/Blob 却整片报差异。

**解决办法**：填 `ValidationPointText`（第 5 对点）。插件用前 4 对求变换，再看第 5 对的映射误差；
超过 `ResidualLimit` 直接失败。2/3 点同理。

## >4 点：RANSAC 自动剔除错配

5~12 对点时走确定性 RANSAC：

- 枚举所有 4 点子集（C(12,4)=495），每个子集拟合一个单应；
- 以“内点数最多、内点 RMS 最小”选最优模型；
- 用全部内点重拟合；外点写入 `OutlierCount`，内点数写入 `InlierCount`。

**要求至少 5 个内点**：4 点子集总能精确拟合，只有 4 个一致点时无法区分“真对齐”和“巧合”，
此时请改用 4 点 + 验证点。`InlierThreshold` 默认 2px，点定位噪声大时可放宽，错配多时可收紧。

典型用法：6~8 个标记点，允许 1~2 个点被遮挡/错配；`OutlierCount` 告诉你剔除了几个。

## 残差、分数、阈值

| 指标 | 公式 | 用法 |
| --- | --- | --- |
| `Residual` | 验证点映射误差（px） | 硬校验：> `ResidualLimit` 直接失败 |
| `Score` | `clamp(1 − Residual / ScoreRef, 0, 1)` | 有验证点才有区分度；无验证点恒 1.0 |
| `ValidAreaRatio` | 有效区面积 / 画幅面积 | 太小说明变换把图移出画幅太多 |
| `IsOk` | 可选：`Score ≥ MinScore`、`Residual ≤ ResidualLimit`、`ValidAreaRatio ≥ MinValidAreaRatio` | 只影响 IsOk，不中断流程 |

`ResidualLimit` 默认 5px：合成/真图正常对齐应在 1px 内；5px 是“点可能错了”的硬门槛。
`ScoreRef` 默认 2px：残差 0 → 1.0，2px → 0。

## 有效区与黑边

对齐后画幅外是 0（黑）。**不能靠“像素是不是 0”判断黑边**——黑色工件本身也可能是 0。
本插件用几何方式生成 `ValidRegion`：全画幅矩形经同一个变换搬过去，再与目标域求交。

下游必须用它限制检测区：

| 下游 | 接哪个端口 |
| --- | --- |
| `ImageDiff` | `MaskRegion` |
| `BlobDetect` | `MaskRegion` / `ExcludeRegion` |
| `Matching` | `SearchRegion`（`UseSearchRegion=true`） |
| `BeadInspect` | `RoiRegion` |
| `DeformableMatching` | `SearchRegion` |
| `GeometryFinder` | `MaskRegion` |

`ColorCheck` / `ColorRegion` / `Ocr` / `CodeReader` / `CaliperMeasure` / `DlAnomaly` **不吃上游 ROI**；
要用有效区，先裁图（「创建 ROI」）或改造下游。

## 四个典型配方

### 配方 A：PCB 平移/旋转（2 点刚性）

- 两个标记点，尽量相距远；
- `Mode=PointPairs`，2 对点；
- 下游：`AlignedImage` → `ImageDiff`，`ValidRegion` → `MaskRegion`；
- 注意：2 点不能吸收缩放/透视；有倾斜时用 4 点。

### 配方 B：标签透视（手动四点）

- 四个角点；`Mode=ManualFourPoint`；
- 填 `ValidationPointText`（第 5 点，例如中间的文字角）；
- 下游：`AlignedImage` → OCR/读码；`ValidRegion` → `MaskRegion`；
- 注意：标签边缘会卷曲，点选在稳定区域。

### 配方 C：已有模板匹配（上游位姿）

- 「模板匹配」输出 `Row/Column/Angle` → 本插件 `PoseRow/PoseColumn/PoseAngle`；
- `TemplateRefRow/Column` 填模板参考点；
- `Score` 接匹配的 `Score` → `PoseScore`；
- 注意：这是刚性对齐，不还原缩放/透视；需要时改用 4 点。

### 配方 D：批量差分（对齐 + 有效区）

- 当前图与基准图同尺寸；点对求变换；
- `AlignedImage` → `ImageDiff.Test`，基准图 → `ImageDiff.Reference`；
- `ValidRegion` → `ImageDiff.MaskRegion`；
- 验收：对齐后差异面积应比“不对齐”下降 ≥80%；把矩阵故意错 2px，面积应回升。

## 常见调参错误

| 现象 | 原因 | 处理 |
| --- | --- | --- |
| 对齐图整幅飞出画幅 | 点对/上游矩阵方向写反 | 点对和上游矩阵都是“参考→当前”；输出 `HomMat2D` 才是“当前→基准” |
| 有效区为空 | 同上，或点对错到把图移出画幅 | 查方向；查点对是否点错 |
| 4 点对齐“看起来正常”但下游整片差异 | 第 4 点点错，4 点无冗余 | 加验证点；或改用 3 点仿射（如果透视不重要） |
| 残差很大但点看起来对 | 参考点/当前点不是同一物理点，或图散焦 | 核对点位；先过质量检查 |
| 下游黑边被当缺陷 | 没接 `ValidRegion` | 接下游 `MaskRegion`/`SearchRegion` |
| 换产品后对齐变差 | 点对/模板参考点没重标 | 按产品重填点对；上游位姿模式重设模板参考点 |

## 性能

5MP（2448×2048）4 点透视、含 warp + 有效区 + 端口赋值：**Release 实测中位数 3.4ms**
（min 3.2 / max 5.0；Debug 3.9~4.9ms；2026-10-27 本机）。目标 ≤100ms 已满足量级；
复跑命令 `FlowCanvasChecks.exe --align-perf`（正式数字用 Release）。

耗时主要在 `projective_trans_image`；点对拟合本身是微秒级。需要更快时先裁 ROI 再对齐。
