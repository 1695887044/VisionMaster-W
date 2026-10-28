# 图像对齐 · 端口与参数

> 分类：图像处理 ｜ 来源：插件 Plugin.ImageAlign

## 输入端口（9 个）

| 端口 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `Image` | `HImage` | **是** | 当前图；空时失败：`输入图像未连接（Image 端口为空）…` |
| `ReferenceImage` | `HImage` | 否 | 基准图；只用于输出尺寸/有效区域，**必须与 Image 同尺寸** |
| `RefPoints` | `HTuple` | 否 | 参考点，交错 `r,c,r,c,...`；非空时优先于 `RefPointsText` |
| `CurPoints` | `HTuple` | 否 | 当前点，交错 `r,c,r,c,...`；非空时优先于 `CurPointsText` |
| `HomMat2D` | `HTuple` | 否 | 上游变换，**参考→当前**，6 或 9 元；仅上游位姿模式使用 |
| `PoseRow` | double | 否 | 上游位姿 Row（像素）；仅上游位姿模式使用 |
| `PoseColumn` | double | 否 | 上游位姿 Column（像素）；仅上游位姿模式使用 |
| `PoseAngle` | double | 否 | 上游位姿 Angle（**弧度**）；仅上游位姿模式使用 |
| `PoseScore` | double | 否 | 上游位姿分数（0~1）；仅上游位姿模式使用 |

取值一律 `ActualValue`。端口链接上游时，手动值不参与生效。

## 输出端口（13 个）

| 端口 | 类型 | 单位 | 说明 |
| --- | --- | --- | --- |
| `AlignedImage` | `HImage` | — | 对齐图；与 Image 同尺寸，画幅外填 0 |
| `ValidRegion` | `HRegion` | — | 有效像素区（几何生成，不是靠像素值判黑边） |
| `HomMat2D` | `HTuple` | 9 元 | **当前 → 基准**；直接喂 `projective_trans_image` |
| `InverseHomMat2D` | `HTuple` | 9 元 | **基准 → 当前**；结果坐标回搬 |
| `Score` | double | 0~1 | 有验证点：`1 − 残差/ScoreRef`；无验证点 1.0；位姿模式 PoseScore |
| `Residual` | double | px | 验证点残差；无验证点/位姿模式 0 |
| `ValidAreaRatio` | double | 0~1 | 有效区面积 / 画幅面积 |
| `UsedPointCount` | int | 对 | 2~12；位姿模式 0 |
| `InlierCount` | int | 个 | RANSAC 内点数（≤4 点时等于总点数） |
| `OutlierCount` | int | 个 | RANSAC 外点数（被剔除的错配点） |
| `IsOk` | bool | — | 可选阈值结论；默认 true；**不改变 Success** |
| `Reason` | string | — | metrics-only 或越界项 |
| `Details` | string[] | — | 模式/点数/残差/分数/有效区/正逆矩阵/提示 |

## 配置项（13 个）

| 属性 | 类型 | 默认 | 取值域 | 说明 |
| --- | --- | --- | --- | --- |
| `Mode` | `AlignMode` | `PointPairs` | 三档 | 基准点 / 手动四点 / 上游位姿 |
| `Interpolation` | `AlignInterpolation` | `Bilinear` | 两档 | 双线性 / 最近邻 |
| `RefPointsText` | string | 空 | — | `r,c; r,c; ...` |
| `CurPointsText` | string | 空 | — | `r,c; r,c; ...` |
| `ValidationPointText` | string | 空 | — | `refR,refC,curR,curC` |
| `TemplateRefRow` | double | 0 | — | 上游位姿模式的模板参考点 Row |
| `TemplateRefColumn` | double | 0 | — | 上游位姿模式的模板参考点 Column |
| `ResidualLimit` | double | 5.0 | ≥0（夹取） | 验证点残差上限；0 = 不做硬校验 |
| `ScoreRef` | double | 2.0 | ≥0.01（夹取） | 分数归一化参考 |
| `InlierThreshold` | double | 2.0 | ≥0.01（夹取） | >4 对点时，残差 ≤ 该值的点算内点 |
| `EnableThreshold` | bool | false | — | 是否按阈值判 IsOk |
| `MinScore` | double | 0.0 | [0,1]（夹取） | Score 下限 |
| `MinValidAreaRatio` | double | 0.0 | [0,1]（夹取） | 有效区比例下限 |

## 坐标与矩阵约定

| 项 | 约定 |
| --- | --- |
| 点 | `(Row, Column)`，像素中心为整数 |
| 参考点 | 基准帧坐标 |
| 当前点 | 当前图坐标 |
| 上游 `HomMat2D` | 参考 → 当前（与 `DeformableMatching.HomMat2D` 的 model→search 同向） |
| 输出 `HomMat2D` | 当前 → 基准（warp 方向） |
| 输出 `InverseHomMat2D` | 基准 → 当前（回搬方向） |
| 矩阵长度 | 统一 9 元；6 元仿射自动补第三行 `0,0,1` |
| 角度 | 弧度（与 HALCON/Matching 一致） |

## 三种模式的口径

### 基准点（2~12 点对）

| 点数 | 变换 | 自由度 | 残差 |
| --- | --- | --- | --- |
| 2 | 刚性（平移 + 旋转） | 3 | 恒 0（精确拟合） |
| 3 | 仿射（平移 + 旋转 + 缩放/剪切） | 6 | 恒 0（精确拟合） |
| 4 | 透视（单应） | 8 | 恒 0（精确拟合） |
| 5~12 | 透视 + RANSAC | 8 | 内点 RMS；要求 ≥5 个内点 |

**2/3/4 点都是精确拟合，残差恒 0**。要检测错点必须加验证点（第 5 对）。
**5~12 点走确定性 RANSAC**：枚举 4 点子集，取内点最多、内点 RMS 最小的模型，再用全部内点重拟合；
外点被剔除。注意：4 点子集总能精确拟合，所以 >4 点时**要求至少 5 个一致点**，
只有 4 个一致点时请改用 4 点 + 验证点。
`ManualFourPoint` 强制正好 4 对；少于 4 对直接失败，不会退化成三点仿射。

### 上游位姿（刚性）

- 接 `HomMat2D`：按**参考→当前**解释，内部求逆做 warp；
- 不接 `HomMat2D`：用 `TemplateRefRow/Column` + `PoseRow/Column/Angle` 构造
  `vector_angle_to_rigid(模板参考点, 0) → (位姿点, 位姿角)`，再求逆；
- `Score` 取 `PoseScore`；`Residual=0`；`UsedPointCount=0`。

### 有效区

- 用**输入图的全画幅矩形**经同一个 `HomMat2D` 做 `projective_trans_region`；
- 再与目标域（`ReferenceImage` 的域，或当前图全画幅）求交；
- `ValidAreaRatio = 有效区面积 / 画幅面积`；
- 有效区为空 → 直接失败（通常是方向写反/点对错到把图移出画幅）。

## `.vms` 里存什么

本步骤只存参数值（13 项），不存图、不存点对象。
`Image`/`ReferenceImage` 来自上游；`RefPoints`/`CurPoints`/`HomMat2D` 若接了上游，由上游持久化；
文本点对存在本步骤参数里。

因此：**换基准点改本步骤文本；换基准图改上游步骤**。
