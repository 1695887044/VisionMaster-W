# 几何查找

在 ROI 内做**亚像素找边并拟合**，输出可复用的**几何基元**（直线 / 圆 / 椭圆），供「几何计算」等下游消费。

## 算法

亚像素边缘提取（`edges_sub_pix` / canny）→ 按长度筛选 → 取**最长轮廓** → 拟合（Tukey 抗离群）。

## 输入

| 端口 | 说明 |
| --- | --- |
| `SrcImage` | 待查找图像（必接） |
| `MaskRegion` | 查找区域（可选，接 ROI 的 `Domain_*`）；未接 = 整图 |
| `Kind` | 查找类型：直线 / 圆 / 椭圆 |
| `Sigma` / `LowThreshold` / `HighThreshold` | 边缘提取参数 |
| `MinContourLength` | 参与拟合的轮廓长度下限（px） |
| `FitAlgorithm` | 拟合鲁棒算法（默认 Tukey） |
| `FailOnNotFound` | 找不到是否判失败（默认是） |
| `DisplayViewIndex` | 显示窗口：`0`=不显示，`1~9`=主界面视图窗口号（把结果图推送到该窗口） |

## 输出

| 端口 | 说明 |
| --- | --- |
| `Geometry` | 几何基元（`GeometryParam`：线/圆/椭圆） |
| `Contours` | 参与拟合的最长轮廓（XLD） |
| `IsFound` | 是否找到 |
| `Row` / `Col` / `Phi` / `Radius` | 便捷数值口（线中点/圆心、方向角、半径） |

## 口径

- 基元为**图像像素系**；角度**度**，方向角 `Phi` 归一化 [0,180)；**不乘像素当量**（长度换算交给「几何计算」）。
- 找不到/拟合失败 → 默认**明确失败**并给中文原因；`FailOnNotFound=false` 时置 `IsFound=false` 且流程继续。

## 显示

结果会**画在源图上推送到视图窗口**（`DisplayViewIndex`，1~9）：拟合出的直线/圆/椭圆 + 结果文字（φ / 半径）。`DisplayViewIndex=0` 关闭显示。找不到时也会推一张带"未找到轮廓"的图。

## 接线

```
图像采集.OutputImage ──▶ 几何查找.SrcImage
ROI.Domain_名字 ──────▶ 几何查找.MaskRegion
几何查找.Geometry ────▶ 几何计算.A / B
```
