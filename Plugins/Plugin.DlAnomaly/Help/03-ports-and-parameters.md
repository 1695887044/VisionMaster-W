# 深度学习异常检测 · 端口与参数

> 分类：图像处理 ｜ 来源：插件 Plugin.DlAnomaly

本页是**参考手册**：每个端口、每个参数、每个枚举取值的准确口径。改方案前先照这张表对一遍，能省掉大半"跑出来不对"的排查。

## 一、输入端口（1 个）

| 端口 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `Image` | `HImage` | 否 | 待检测的图像。**整幅图检测，不吃 ROI** |

`Image` 标为"非必填"是有意的：不接也能保存方案、能打开配置窗口（否则"先接图还是先配参数"的顺序会把人卡死）。但**执行时**没图会明确失败，不会静默跳过。

## 二、输出端口（7 个）

| 端口 | 类型 | 无数据时的值 | 说明 |
| --- | --- | --- | --- |
| `Score` | `double` | `NaN` | 图像级异常分数。**越大越异常**（方向由模型决定，见下文口径提醒） |
| `HasScore` | `bool` | `false` | 分数是否由模型**直接**给出。`false` = 本节点从热力图取最大值归约而来 |
| `HeatmapMaxValue` | `double` | `NaN` | 热力图最大值。无热力图输出时为 `NaN` |
| `HeatmapMaxRow` | `double` | `-1` | 热力图最大值所在行 |
| `HeatmapMaxCol` | `double` | `-1` | 热力图最大值所在列 |
| `HeatmapImage` | `HImage` | 空 | 热力图渲染图：单通道、`byte`、值域 0~255 |
| `Summary` | `string` | 空串 | 一句话摘要，含分数来源、热力图尺寸、最大点位置与均值 |

### 两个容易误读的口径

1. **`Score` 的方向由模型决定，不由本节点保证**。绝大多数异常检测模型"分越高越异常"，但也有导出的是"相似度/置信度"，越大越像良品。第一次用某个模型，务必拿良品与不良各跑几张对数。
2. **`HasScore = false` 时，分数与训练侧阈值不可比**。因为那是本节点把热力图最大值当作分数，量纲与模型原本的分数输出不是一回事。这种情形下更稳妥的做法是直接用 `HeatmapMaxValue` 做判据。

## 三、配置参数（6 个）

| 参数 | 类型 | 默认 | 取值范围 | 说明 |
| --- | --- | --- | --- | --- |
| `ModelPath` | `string` | 空 | — | ONNX 模型路径。绝对路径直接用；相对路径按**宿主程序目录**解析 |
| `ResizeMode` | `AnomalyResizeMode` | `Letterbox` | `Letterbox` / `Stretch` | 与训练一致最重要 |
| `InputSize` | `int` | `0` | 0~4096（夹取） | `0` = 用模型元数据的静态尺寸；动态形状模型才手填 |
| `HeatmapMax` | `double` | `0` | ≥0（非有限或 ≤0 归 0） | `0` = 本轮热力图自身最大值自适应；`>0` 固定归一化上限 |
| `HeatmapInterpolation` | `AnomalyInterpolation` | `Nearest` | `Nearest` / `Bilinear` | **只影响热力图显示**，不影响分数 |
| `DisplayViewIndex` | `int` | `1` | 0~9（夹取） | 预览窗口号；`0` = 不发布，只算输出端口 |

参数夹取是**在写入时就做掉**的（`OnXxxChanging` 钩子里），所以看不到"填了 12 却生效 9"这种错位：填 12 就显示 9。

## 四、枚举取值与面板文案对照

| 枚举 | 成员 | 面板文案 | HALCON/ONNX 侧含义 |
| --- | --- | --- | --- |
| `AnomalyResizeMode` | `Letterbox` | Letterbox · 等比补边（推荐） | 等比缩放到目标框内，四周补 114 灰 |
| `AnomalyResizeMode` | `Stretch` | Stretch · 拉伸填充 | 直接拉伸到目标宽高，不补边 |
| `AnomalyInterpolation` | `Nearest` | Nearest · 最近邻（默认） | `zoom_image_factor(…,'nearest_neighbor')` |
| `AnomalyInterpolation` | `Bilinear` | Bilinear · 双线性 | `zoom_image_factor(…,'bilinear')` |

**持久化写的是成员名（`Letterbox` / `Nearest`），不是中文**。改显示文案不会打断已配好的流程；反过来说，**改成员名会让旧方案读不回来**（回落默认值），这类改动必须同步迁移。

## 五、输出契约（模型侧，不是参数）

模型被接受的前提是输出形状落在这三格里：

| 形状 | 判为 | 取用方式 |
| --- | --- | --- |
| 秩 2，`dims[1] == 1` | 图像级分数 | `flat[0]` 作为 `Score`，`HasScore = true` |
| 秩 4，`dims[0]==1 && dims[1]==1` | 热力图 | H = `dims[2]`，W = `dims[3]` |
| 秩 3，`dims[0]==1` | 热力图 | H = `dims[1]`，W = `dims[2]` |

同时有分数与热力图时两个都用；只有热力图时 `Score` 由热力图最大值归约、`HasScore = false`。**不在这三格的形状在加载期就被拒绝**，错误信息带真实形状。

## 六、`.vms` 里存什么

| 键 | 存什么 | 说明 |
| --- | --- | --- |
| `ModelPath` | 字符串 | **路径**，不是模型内容。方案拷到别的机器必须一起拷模型，或把模型放到宿主程序目录下用相对路径 |
| `ResizeMode` | 成员名 | 如 `"Letterbox"` |
| `InputSize` | 整数 | |
| `HeatmapMax` | 浮点 | |
| `HeatmapInterpolation` | 成员名 | 如 `"Nearest"` |
| `DisplayViewIndex` | 整数 | |
| `LinkedSources.Image` | 上游连线 | 上游步骤 ID + 端口名 |

**方案文件里不含模型本体，也不含任何凭据**。模型是客户资产，走外部路径。

## 七、想做什么 → 接/改哪个

| 想做的事 | 接/改 |
| --- | --- |
| 只要一个"整图异常程度"的数 | 接 `Score` |
| 要在图上看到异常在哪 | 接 `HeatmapImage` 到「结果显示」，或设 `DisplayViewIndex` 直接上屏 |
| 要把异常位置变成坐标做精定位 | 接 `HeatmapMaxRow` / `HeatmapMaxCol`，用「创建ROI」在热点附近开窗，再用卡尺/Blob 精定位 |
| 要 OK/NG | `Score` → 「结果比较」/「判断」节点 |
| 要记录到 CSV/数据库 | `Summary` 或 `Score` → 「数据记录」 |
| 要同一上限下比较不同批次 | `HeatmapMax` 设固定值（如训练侧的热力图上限） |
| 要窄一点的视野 | 上游「创建ROI」裁好再接 `Image`（本节点不改内部 ROI） |
