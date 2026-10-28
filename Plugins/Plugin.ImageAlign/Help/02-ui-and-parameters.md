# 图像对齐 · 界面与参数

> 分类：图像处理 ｜ 来源：插件 Plugin.ImageAlign

本插件有专属配置视图（`ImageAlignView`）：左侧画布取点，右侧点对表格；
标量/枚举参数走「参数…」弹窗（`FlatPropertyGrid` 渲染 `[SuperDisplay]` 模型），不重复手写控件。
点对既可用画布点选，也可在表格里直接改坐标，或继续用 `RefPointsText`/`CurPointsText` 文本。

## 画布与点对表格

| 操作 | 行为 |
| --- | --- |
| 「取参考点」 | 进入一次性取点模式；在左侧图上左键点一下，写入当前选中行的参考 R/C，然后自动退出取点 |
| 「取当前点」 | 同上，写入当前 R/C |
| 没有选中行时取点 | 自动新增一行并选中 |
| 「删除选中」 | 删除当前行，序号重排 |
| 「清空」 | 清空全部点对 |
| 表格内改坐标 | 直接改 R/C；确认时序列化回 `RefPointsText`/`CurPointsText` |
| 「显示基准图」 | 画布切到 `ReferenceImage`（没接基准图时置灰） |
| 「参数…」 | 打开标量/枚举参数弹窗（模式、插值、残差上限、RANSAC 阈值、阈值判定等） |

- 画布用 `ImageReadOnly`：只有缩放/平移/适应图片，**没有“打开图片”菜单**，避免配置态误换预览。
- 取点是**一次性**的：取完一个点自动退出，防止画布长期吃鼠标事件。
- 点对表格是唯一真源：确认时按表格序列化；`RefPointsText`/`CurPointsText` 只用于存盘与脚本/端口输入。
- 验证点仍是 `ValidationPointText`（`refR,refC,curR,curC`），在视图右下角直接填。

## 参数面板（「参数…」弹窗，按声明顺序）

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| `Mode` | `PointPairs` | `PointPairs` 基准点 2~4 对 / `ManualFourPoint` 手动四点 / `TemplatePose` 上游位姿 |
| `Interpolation` | `Bilinear` | `Bilinear` 双线性 / `NearestNeighbor` 最近邻 |
| `RefPointsText` | 空 | 参考点文本：`r,c; r,c; ...`（基准帧坐标） |
| `CurPointsText` | 空 | 当前点文本：`r,c; r,c; ...`（当前图坐标） |
| `ValidationPointText` | 空 | 验证点：`refR,refC,curR,curC`（第 5 对点，强烈建议 4 点时填） |
| `TemplateRefRow` | 0 | 上游位姿模式：模板参考点 Row（像素） |
| `TemplateRefColumn` | 0 | 上游位姿模式：模板参考点 Column（像素） |
| `ResidualLimit` | 5.0 | 验证点残差上限（px）；超过直接失败；0 = 不做硬校验 |
| `ScoreRef` | 2.0 | `Score = 1 − 残差 / ScoreRef`（夹取 0~1） |
| `InlierThreshold` | 2.0 | >4 对点时，残差 ≤ 该值的点算内点（默认 2px） |
| `EnableThreshold` | false | 打开后按 `MinScore` / 残差上限 / 有效区比例判 `IsOk` |
| `MinScore` | 0.0 | `Score` 下限（0~1）；仅启用阈值时生效 |
| `MinValidAreaRatio` | 0.0 | `ValidAreaRatio` 下限（0~1）；仅启用阈值时生效 |

数值参数都挂了 `partial void OnXxxChanging(ref T value)` 夹取钩子：
`ResidualLimit ≥ 0`、`ScoreRef ≥ 0.01`、`MinScore`/`MinValidAreaRatio ∈ [0,1]`。

## 点文本格式

```
RefPointsText: 20,20; 20,180; 180,20; 180,180
CurPointsText: 25,18; 24,183; 178,16; 181,179
```

- 每项 `行,列`；多项用 `;` 或换行分隔；
- 数字用 `.` 小数点（InvariantCulture），不接受千分位；
- 2 对 → 刚性；3 对 → 仿射；4 对 → 透视；5~12 对 → RANSAC（要求 ≥5 内点）；
  `ManualFourPoint` 必须正好 4 对；
- 两组数量必须一致；少于 2 对或多于 12 对直接失败。

也可以接 `RefPoints` / `CurPoints` 端口（`HTuple`，交错 `r,c,r,c,...`）。
**端口优先于文本**：端口接了非空值，文本被忽略。

## 验证点格式

```
ValidationPointText: 100,100,103,99
```

含义：基准帧上的第 5 个点 `(100,100)`，当前图上的对应点 `(103,99)`。
插件用前 2~4 对求变换，再把 `(103,99)` 映射回基准，和 `(100,100)` 比距离；
超过 `ResidualLimit` 直接失败。**4 点透视必须靠它发现错点**。
5~12 点则走 RANSAC，外点自动剔除；`OutlierCount` 报告剔除数量。

## 输出怎么读

| 端口 | 类型 | 读法 |
| --- | --- | --- |
| `AlignedImage` | `HImage` | 搬回基准帧的图；与 Image 同尺寸，画幅外填 0 |
| `ValidRegion` | `HRegion` | 黑边之外的几何有效区；不是靠像素值判的 |
| `HomMat2D` | `HTuple`（9） | 当前图 → 基准帧；直接喂 `projective_trans_image` 得到 AlignedImage |
| `InverseHomMat2D` | `HTuple`（9） | 基准帧 → 当前图；把对齐图上的结果坐标搬回原图 |
| `Score` | double | 有验证点：`1 − 残差/ScoreRef`；无验证点：1.0；位姿模式：PoseScore |
| `Residual` | double | 验证点残差（px）；无验证点/位姿模式：0 |
| `ValidAreaRatio` | double | 有效区面积 / 画幅面积（0~1） |
| `UsedPointCount` | int | 2~12；位姿模式 0 |
| `InlierCount` | int | RANSAC 内点数（≤4 点时等于总点数） |
| `OutlierCount` | int | RANSAC 外点数（被剔除的错配点） |
| `IsOk` | bool | 默认 true；只有启用阈值才可能 false；**不改变 Success** |
| `Reason` | string | 未启用阈值 = metrics-only；启用时写越界项 |
| `Details` | string[] | 模式/点数/残差/分数/有效区/正逆矩阵/提示 |

## `IsOk` 与 `Success`

| 端口 | 含义 | 什么时候 false |
| --- | --- | --- |
| `Success` | 本步骤**执行**是否成功 | 图没接、点对数量不对、点退化、残差超限、有效区为空、尺寸不一致 |
| `IsOk` | 按配置阈值，**对齐质量**是否达标 | 只有 `EnableThreshold=true` 且某项越界 |

残差超 `ResidualLimit` 是**硬失败**（Success=false），因为它通常意味着点错/遮挡；
`MinScore`/`MinValidAreaRatio` 是可选质量门，只影响 `IsOk`，不中断流程。

## 方向约定（最容易错）

| 数据 | 方向 |
| --- | --- |
| 点对 | 参考点在基准帧，当前点在当前图 |
| 上游 `HomMat2D` | **参考 → 当前**（与 `DeformableMatching.HomMat2D` 的 model→search 同向） |
| 输出 `HomMat2D` | **当前 → 基准**（warp 用的方向） |
| 输出 `InverseHomMat2D` | **基准 → 当前**（结果坐标回搬） |

如果对齐图整幅飞出画幅、`ValidRegion` 为空，第一件事就是查方向是不是写反了。
