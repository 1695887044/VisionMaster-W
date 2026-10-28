# 可形变模板匹配 · 端口与参数

## 1. 输入端口

| 端口 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `Image` | `HImage` | 否 | 待搜索图像；运行期通常由上游连入 |
| `SearchRegion` | `HRegion` | 否 | 搜索区域；连了且非空才生效，否则全图 |

两个端口都声明为 `IsRequired=false`：配置态可以没有图；运行期没图由插件给出中文失败原因。

## 2. 输出端口

| 端口 | 类型 | 说明 |
| --- | --- | --- |
| `MatchCount` | `int` | 命中实例数 |
| `Score` | `double` | 最高分实例分数（0~1） |
| `Row` | `double` | 最高分实例中心行（亚像素） |
| `Column` | `double` | 最高分实例中心列（亚像素） |
| `Scores` | `HTuple` | 各实例分数数组 |
| `Rows` | `HTuple` | 各实例中心行数组 |
| `Columns` | `HTuple` | 各实例中心列数组 |
| `DeformedContours` | `HXLD` | 各实例形变后的轮廓（扁平 XLD 数组） |
| `HomMat2D` | `HTuple` | 平面模式：每命中 9 个 double |
| `RectifiedImages` | `HObject` | 局部模式：每个命中一张校正图 |
| `MeasureImage` | `HImage` | 标注图；无命中=原图。`KeepAnnotatedImage=false` 时为空（几何走 `DeformedContours`，文字交给下游「结果显示」） |
| `Success` | `bool` | 基类端口：成功/失败 |
| `ErrorMessage` | `string` | 基类端口：失败原因 |

## 3. 标量端口怎么选最高分

HALCON 的命中顺序**不保证按分数降序**。插件内部用最大值 + 索引取最高分，
不会把 `index 0` 当最高分。下游若自己遍历 `Scores`，也要自己取最大值。

## 4. 创建期参数 vs 查找期参数

| 阶段 | 参数 | 改了怎么办 |
| --- | --- | --- |
| 创建期 | `Algorithm`、`Shape`、`TemplateRect`、`TemplateMask`、`NumLevels`、`UsePolarity`、`ScaleRMin`、`ScaleCMin` | 重新学习 |
| 查找期 | `AngleStartDeg`、`AngleExtentDeg`、`ScaleRMax`、`ScaleCMax`、`Smoothness`、`MinScore`、`MaxMatches`、`MaxOverlap`、`Greediness` | 立即生效 |

HALCON 23.05 文档明确：`create_local_deformable_model` 与
`create_planar_uncalib_deformable_model` 的 `AngleStart/AngleExtent/ScaleRMax/ScaleCMax`
**not used**；这四个只在 `find_*` 里生效。插件创建时一律传 `[]`。

## 5. NumLevels

| 场景 | 取值 | 说明 |
| --- | --- | --- |
| 创建期 | `0` 或 `1..10` | 0 映射为 `'auto'` |
| 查找期 | `0..10` | 0 合法，表示默认 |
| 取模型轮廓 | `level=1` | **1 基**；0 非法（HALCON #1302） |

不要把创建期的 `'auto'` 直接传给查找期；查找期只收整数。

## 6. 模板来源与载荷

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `TemplateAssetId` | string | 方案模板资产 Id（`template` 类型） |
| `TemplateFilePath` | string | 本地模板 JSON 路径 |
| `TemplateJson` | string | 内嵌模板（旧方案/单机方案） |

优先级：**方案资产 > 本地文件 > 内嵌 `TemplateJson`**。

- 资产/本地文件内容都是单个 `DeformableTemplateEntry` JSON，不是 Matching 的模板库数组。
- 资产路径相对方案根目录；本地文件支持绝对路径，相对路径按宿主程序目录解析。
- 资产缓存键 = `AssetId + ContentHash + Version`；本地文件缓存键 = `路径 + 文件大小 + 最后写入时间`。
- 显式选了外部来源但读取失败（缺失/归档/类型不符/坏 JSON/算法不符）时直接失败，**不回退到内嵌模板**。
- 外部来源模式下点「确定」只写元数据回 `TemplateJson`，模型 base64 留在资产/文件里。

## 7. 模型载荷

- 学习后用 `write_deformable_model` 写临时文件，再读成 base64 存进 `TemplateJson`；
- 运行期 `read_deformable_model` 读回，按 `Model` 载荷缓存句柄；
- 同一载荷连续运行不会重复读模型；
- 重学/换载荷/`Dispose` 会 `clear_deformable_model`。

## 8. 原点口径

`set_deformable_model_origin` 的参数是**相对模板域重心的偏移**，不是图像绝对坐标：

- 对称 ROI（矩形/圆/椭圆）重心 = ROI 中心 → 传 `(0,0)`；
- 非对称掩膜：`origin = roiCenter - cog`，`cog` 用 `GetDomain + AreaCenter` 求；
- 学习时插件按实际域重心计算，不需要用户填。

## 9. HomMat2D

- 仅平面模式有值；长度 = `9 × MatchCount`；
- 映射「当前参考点为 `(0,0)`」的模型坐标到搜索图；
- `Row/Column` 用 `ProjectiveTransPixel(hom, 0, 0)` 反推；
- 每个命中取 9 个连续 double。

## 10. RectifiedImages

- 仅局部模式有值；
- HALCON 的 `ImageRectified` 是 `image(-array)`，**每个命中一张**；
- 端口类型必须是 `HObject`，不能用 `HImage` 装多张；
- 尺寸是模板域的轴对齐包围盒（`expand_border=0` 时紧贴）。

## 11. DeformedContours

- 局部模式：HALCON 直接返回；
- 平面模式：`get_deformable_model_contours(level=1)` + `projective_trans_contour_xld` 投影；
- 是**扁平 XLD 数组**，每个命中由多段组成（探针实测 486 段/命中）；
- `CountObj` 不等于 `MatchCount`；下游不要按命中数分组。

## 12. MeasureImage

- 有命中：原图 + 绿色形变轮廓；
- 无命中：原图（原因看 `ErrorMessage` / 状态栏）；
- 运行显示窗口 >0 时发布到主界面。

## 13. 默认值

| 参数 | 默认 |
| --- | --- |
| `TemplateAssetId` / `TemplateFilePath` | 空（使用内嵌模板） |
| `MinScore` | 0.5 |
| `MaxMatches` | 1 |
| `MaxOverlap` | 0.5 |
| `Greediness` | 0.9 |
| `DisplayViewIndex` | 1 |
| `NumLevels` | 0 |
| `AngleStartDeg` / `AngleExtentDeg` | -30 / 60 |
| `ScaleRMin/Max`、`ScaleCMin/Max` | 1 / 1 |
| `Smoothness` | 11 |
| `UsePolarity` | true |

## 14. 单位

- 角度：界面与端口一律用**度**；HALCON 算子内部用弧度，插件负责换算；
- 坐标：像素，Row/Column 亚像素；
- 分数：0~1；
- 缩放：倍率，1=原始尺寸。

## 15. 端口语义边界

- 局部模式不返回角度/缩放，不伪造 0；
- 平面模式不直接返回 Row/Column/Angle，Row/Column 由 HomMat2D 反推；
- 无命中时标量端口为 0、数组为空、`Success=false`；
- 下游若只需要“有没有找到”，判 `MatchCount` 或 `Success`，不要判 `Score` 是否为空。
