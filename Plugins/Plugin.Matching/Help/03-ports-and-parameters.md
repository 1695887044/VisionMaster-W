# 模板匹配 · 端口与参数

> 分类：定位 ｜ 来源：插件 Plugin.Matching

本页是**清单**：端口名与类型逐字照抄源码，参数默认值与合法区间照抄源码里的出厂常量与夹取上下限。
含义与调法在第二、四页，报错原文在第五页。

## 一、输入端口

| 端口 | 类型 | 必填 | 界面标签 | 说明（源码描述原文） |
| --- | --- | --- | --- | --- |
| `Image` | `HImage` | 声明为可选、**实际必填** | 「图像」链接框 | 待定位的图像。运行拿不到图直接判失败："输入图像为空或未初始化"；1 / 3 / 4 通道都能吃（内部统一转灰） |
| `RecipeName` | `string` | 否 | ④ 输出卡的「配方名端口」 | 配方名/产品型号，可接上游变量或上位机写入，也可手填固定名字。留空或纯空格 = 没给（用默认条目）；给了但库里没有 → 失败并列出库里现有的名字 |

`Image` 支持**带 domain 的图**：接「ROI」插件的 `Domain_ROI名`，只在域内搜、域外不命中，返回坐标仍是全局坐标。

## 二、输出端口（全部固定，本插件没有动态端口）

| 端口 | 类型 | 说明（源码描述原文） |
| --- | --- | --- |
| `Score` | `double` | 最高分实例的匹配分数（0~1） |
| `Row` | `double` | 最高分实例的中心行（亚像素）——就是你画的区域中心 |
| `Column` | `double` | 最高分实例的中心列（亚像素） |
| `Angle` | `double` | 最高分实例的角度（度） |
| `MatchCount` | `int` | 命中实例数（已按 `MaxOverlap` 去重） |
| `MatchedTemplate` | `string` | 本帧使用的模板名（配方追溯用） |
| `Rows` | `HTuple` | 各实例中心行数组（亚像素） |
| `Columns` | `HTuple` | 各实例中心列数组（亚像素） |
| `Angles` | `HTuple` | 各实例角度数组（度） |
| `Scores` | `HTuple` | 各实例分数数组（0~1） |
| `Scales` | `HTuple` | 各实例缩放倍率数组（未启用缩放时恒为 1）。**口径**：等比档 = 统一缩放倍率；各向异性档 = **行方向**缩放（列方向见 `ScalesCol`） |
| `ScalesRow` | `HTuple` | 各实例行方向缩放倍率数组（各向异性档有效；等比/未缩放档与 `Scales` 逐位相同） |
| `ScalesCol` | `HTuple` | 各实例列方向缩放倍率数组（各向异性档有效；等比/未缩放档与 `Scales` 逐位相同） |
| `MeasureImage` | `HImage` | 标注图（模板轮廓 + 位姿 + 判定文字）；**未找到目标时也有**。`KeepAnnotatedImage=false` 时为空 |
| `MatchContours` | `HXLD` | 命中实例的模板轮廓（**源图坐标系**，顺序 = 命中顺序；多实例时每个命中一段，对象数 ≥ 命中数）。未命中为空。接「结果显示」的 `OverlayContours` / `rN` 即可，颜色与线宽由显示层决定 |
| `AlignedImage` | `HImage` | 位姿归一化图像（目标在标准位姿）；开关关闭或本轮没找到 → 空 |

另有基类的 `Success`（本步跑没跑通）与 `ErrorMessage`（失败原因）。

**每轮开头的清零口径**：`Score`/`Row`/`Column`/`Angle`/`MatchCount` 归 0、`MatchedTemplate` 归空串、
**七个数组端口**（`Rows`/`Columns`/`Angles`/`Scores`/`Scales`/`ScalesRow`/`ScalesCol`）归空 `HTuple`
（基类只自动回收 `HImage`/`IDisposable` 类端口，数值端口要插件自己清）。
所以失败轮下游读到的是 0/空，不是上一帧的"幽灵结果"。

> **老流程不会断线**：`Scales` 名字与语义没变（等比/未缩放档与升级前逐位一致），
> 只是各向异性档它装的是行缩放。要行、列两路分别用就接新增的 `ScalesRow`/`ScalesCol`；
> 只接了 `Scales` 的存量流程升级后不需要改任何连线。

## 三、参数（`[StepConfig]`，随方案存盘）

### ③ 匹配参数（运行态，改完立即生效，不用重学）

| 参数 | 出厂默认 | 运行期夹取 | 说明 |
| --- | --- | --- | --- |
| `MinScore` | 0.5 | 0.01 ~ 1 | 分数下限，低于它的命中不算找到 |
| `MaxMatches` | 1 | 1 ~ 100 | 最多输出几个实例（1 = 单目标） |
| `MaxOverlap` | 0.3 | 0 ~ 1 | 两个命中允许的最大重叠比例（去重用） |
| `Greediness` | 0.8 | 0.1 ~ 0.9 | 搜索激进度：越大越快、越容易漏 |
| `TrackingLevel`（条目内） | 1 | -1 ~ 10 | 搜索的"最低跟踪层"（HALCON `find_shape_model` 的 `NumLevels` 第二个值）：1 = 跟踪到最细层（默认，精度最高，旧行为）；-1 = 不跟踪到最低层（散焦/高噪声场景更稳，HALCON 例程 `[0,-1]` 的用法）。只影响搜索，改它不用重学；NCC 下不生效 |
| `DisplayViewIndex` | 1 | 0 ~ 9 | 标注图发布到主界面几号视图窗口，0（界面显示"不显示"）= 不发布；**只影响发布，不影响端口输出** |
| `KeepAnnotatedImage` | true | 勾/不勾 | 关掉 = 只输出数值与几何（`MatchContours`），不烧标注图：省一次全幅 dump，也不会与下游「结果显示」各烧一遍造成重影。模板预览不受影响 |
| `OutputAlignedImage` | true | — | 是否输出 `AlignedImage`（关掉省一次整图重采样） |

填坏的离谱数值（含 NaN/Infinity）**不会把流程崩掉**：全部先夹进合法区间再送 HALCON，
最多让结果不对，不该冒出 `#1302`/`#9302` 这类算子错误码。

### ② 提取参数（**跟随每个模板条目**，改任一必须重学）

| 字段（条目内） | 默认 | 夹取/口径 | 创建态还是运行态 |
| --- | --- | --- | --- |
| `Algorithm` | `Shape` | `Shape` = 基于形状（`create_shape_model`）；`Ncc` = 基于灰度相关（`create_ncc_model`）。**换算法 = 模型类型变了（`.shm` ⇄ `.ncm`），必须重新学习**；老方案 JSON 没有这个字段 → 落成 `Shape`，行为与升级前一致 | 创建态 |
| `NumLevels` | 5 | 1 ~ 10；学习与查找取同一个值；信息栏报的是**模型实际生成层数**（撑不出会自动减层） | 创建态 |
| `MinAngleDeg` / `MaxAngleDeg` | -180 / 180 | -180 ~ 180，填反了自动交换；区间外角度**直接找不到** | 创建态 |
| `AngleStepDeg` | 0（= 自动） | 0 = 送 `"auto"`；非 0 时夹 0.1 ~ 30 度 | 创建态 |
| `GradientThreshold` | 0（= 自动） | 0 = 送 `"auto"`；非 0 时夹 1 ~ 255。**NCC 下不生效** | 创建态 |
| `UsePolarity` | true | true → `use_polarity`，false → `ignore_global_polarity`（界面下拉：使用极性 / 忽略极性）。**NCC 下界面灰掉**。⚠ 早期代码送的是 `"ignore_polarity"`，**HALCON 23.05 不认这个值**（三个 create 算子分别报 `#1306`/`#1309`/`#1312`），现已统一走 `ignore_global_polarity` | 创建态 |
| `ScaleEnabled` + `ScaleMin` / `ScaleMax` | 关 / 0.8 / 1.2 | 取 min/max 后夹 0.5 ~ 2.0；上下限相等时学习侧自动撑开极小窗口（±0.005）。**NCC 没有 Scaled 版算子，界面灰掉**。**勾了各向异性时这一档被忽略**（界面灰掉，数值仍保留） | 创建态 |
| `AnisoScaleEnabled` + `ScaleRowMin` / `ScaleRowMax` / `ScaleColMin` / `ScaleColMax` | 关 / 0.9 / 1.1 / 0.9 / 1.1 | 各向异性档（`create_aniso_shape_model`）：行、列**各自**一个范围；取 min/max 后夹 **0.1 ~ 10**（HALCON 只要求 `> 0` 且 `max ≥ min`，23.05 实测该区间可创建，**不是等比那套 0.5~2.0**），上下限填反自动交换、相等时撑开 ±0.005。打开后**忽略**等比档三个字段。**NCC 不支持**（没有对应算子）：学习仍成功，但状态栏转 Warning 并写明"已忽略"。**范围应包含 1.0**：否则参考图自匹配可能失败、原点锚定退回 HALCON 默认原点（位置差 1~2px，不报错） | 创建态 |
| `LearnedAnisoScaleEnabled` | false | 学习那一刻"各向异性是否生效"的快照（**不是**可调参数，随方案落盘）。运行期拿它与当前 `AnisoScaleEnabled` 比对，不一致就落一行 Warn 提醒重学——HALCON 对"模型类型与查找算子不匹配"**不报错**（`find_aniso` 查等比模型 = 行/列恒相等；`find_scaled` 查各向异性模型 = 一个都找不到），不提醒就是静默错结果。老方案没有该字段 → false（升级前的模型都不是各向异性的） | 创建态 |
| `Metric`（极性） | — | 上面 `UsePolarity` 的 HALCON 侧映射，界面上没有这一项 | 创建态 |

### ③ 搜索域（P1-B A1）

| 配置项 | 默认 | 说明 |
| --- | --- | --- |
| `UseSearchRegion` | `false` | 打开后，若 `SearchRegion` 端口连入了区域，就先 `reduce_domain` 再查找，耗时按**面积比**下降。区域为空或未连接 → **回落全图**，并在日志里写"搜索域开关已开但未连区域 → 回落全图"（不静默缩小到零面积，那会变成永远找不到） |
| `SearchRegion`（输入端口） | 未连接 | 类型 `HRegion`，非必需。只在 `UseSearchRegion = true` 时参与运算 |

> **为什么收窄搜索域值得开**：`find_shape_model` / `find_ncc_model` 的耗时与**待搜索区域面积**近似成正比。
> 一个 40×40 的模板在全图 2448×2048 上找，和在一个 600×400 的工位窗口里找，差距是 20 倍量级——
> 这不是"优化一点点"，是能不能跑满节拍的问题。代价是：目标一旦跑出窗口就再也找不到，所以区域要留足余量。

### ④ 库与配方（由配置界面维护，不建议手改）

| 配置项 | 默认 | 说明 |
| --- | --- | --- |
| `TemplateLibraryJson` | `"[]"` | 整个模板库（名字/形状/区域/掩膜/模型/提取参数的 JSON 数组）。**JSON 损坏按空库处理**（界面提示重学），不会把整份方案卡死 |
| `DefaultTemplateName` | 空串 | `设为默认` 按钮写入；`RecipeName` 未连接/为空时使用 |
| `TemplateModel` / `TemplateShape` / `TemplateRect` / `TemplateMask` / `PreviewImagePath` | —（`TemplateShape` 默认矩形） | 单模板时代的**迁移字段**：首次加载自动迁成库里第一条（名字取默认名，否则"模板1"）后清空，新代码不再使用 |

## 四、模板库条目里有什么（`TemplateLibraryJson` 的字段表）

一条 = 下面这些字段（JSON 数组的一个元素）：

| 字段 | 类型 | 含义 |
| --- | --- | --- |
| `Name` | string | 条目名 = **配方键**（逐字符匹配，大小写与空格都算） |
| `Shape` | string | `Rectangle` / `Circle` / `Ellipse`（存成员名，界面显示中文） |
| `Rect` | double[] | 固定 5 个数：矩形 `[中心行, 中心列, 角度(弧度), 半长, 半宽]`；圆 `[中心行, 中心列, 半径, 0, 0]`；椭圆 `[中心行, 中心列, 角度(弧度), 半径1, 半径2]` |
| `Mask` | string | 涂抹掩膜，base64 编码的 PNG（像素值 255 = 挖掉）。**存的是原始涂抹形状**，不是"与区域求交后的有效排除域" |
| `Model` | string | 模型本体（`write_shape_model` / `write_ncc_model` 出来的字节流的 base64）；为空 = 未学习。**形状模型与 NCC 模型不是同一种文件，装载器按 `Algorithm` 决定读哪个** |
| `RefImagePath` | string | 学习时那张参考图的本机绝对路径（只影响重开界面时画布显示与重学） |
| `NumLevels` / `MinAngleDeg` / `MaxAngleDeg` / `AngleStepDeg` / `GradientThreshold` / `UsePolarity` / `ScaleEnabled` / `ScaleMin` / `ScaleMax` | 见上表 | 提取参数，跟着条目走 |
| `AnisoScaleEnabled` / `ScaleRowMin` / `ScaleRowMax` / `ScaleColMin` / `ScaleColMax` / `LearnedAnisoScaleEnabled` | 见上表 | 各向异性档的开关、四个范围与学习快照，跟着条目走。**老 JSON 没有这几个字段 → 关 + 0.9/1.1 + false**（升级前保存的条目照旧打开、行为不变） |
| `LearnedSignature` | string | 学完那一刻的**参数指纹**（形状、区域、层数、梯度、角度、步长、极性、缩放、**各向异性开关与四个范围**、掩膜长度拼成的串）。当前指纹 ≠ 该串 → 徽标转橙"参数已改·需重新学习"。**升级后老模板第一次打开会显示橙色**：老指纹没记录过各向异性字段，重新学习一次即恢复绿色（不是误报） |

## 五、类型与显示口径

- **枚举界面显示中文、流程里存成员名**（`Shape` 存 `Rectangle`/`Circle`/`Ellipse`）；
  导出或手改方案时按成员名填，别填中文；
- **角度**：界面与端口一律**度**；存进条目区域参数（`Rect[2]`）和送 HALCON 时是**弧度**，
  插件在学习/查找两侧各自换算，不需要你乘除；
- **分数**：0~1，本质是"对上了几成特征"；
- **极性下拉两项**：使用极性 / 忽略极性；**涂抹模式三项**：正常显示 / 绘制涂抹 / 擦除涂抹；
- **运行显示窗口**下拉给 1~9 与"不显示"（对应 0），第二个参数就是窗口号，直传不偏移。

## 六、日志与文案（运行期）

| 场景 | 级别 | 文案要点 |
| --- | --- | --- |
| 找到目标 | Info | `模板「X」匹配 N 个实例，最高分 …，位置 (r, c)，角度 a°` |
| 未找到目标 | **Warn**（不是 Error） | 失败原因里带分数阈值，标注图仍输出 |
| 图像为空 / 未知产品 / 未学习 / 载荷损坏 / 区域参数损坏 | Error | 全量原文见第五页第二节 |
| 标注图渲染失败 | Warn | 只丢 `MeasureImage`，端口数据照常输出——一次成功的定位不会因为画不出图而判失败 |
