# 图像差分 · 端口与参数

> 分类：图像处理 ｜ 来源：插件 Plugin.ImageDiff

## 输入端口（3 个）

| 端口 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `Reference` | `HImage` | **是** | 基准图。空或未初始化时直接失败：`基准图未连接（Reference 端口为空）…` |
| `Test` | `HImage` | **是** | 被测图。空时失败：`被测图未连接（Test 端口为空）。` |
| `MaskRegion` | `HRegion` | 否 | 屏蔽区。非空时差异区会与它求交，屏蔽区外一律不参与统计。用于排除工艺上允许变化的区域（打标、LOGO、日期戳、治具反光）。 |

取值一律走 `ActualValue`（强类型）。`Value` 是"手动值"的弱类型入口，
端口链接上游时它可能是默认值而不是上游值——写下游脚本时尤其注意这一点。

## 输出端口（7 个）

| 端口 | 类型 | 单位 | 说明 |
| --- | --- | --- | --- |
| `DiffImage` | `HImage` | — | 差异强度图，单通道 byte：0 = 一样，越大越不一样。可直接接「预处理」继续做形态学、「Blob」再做一次区域筛选。 |
| `OverlayImage` | `HImage` | — | 标注图：基准图打底 + 差异区青色涂色。已被 `Reference` 链接透传保护排除，可安全上屏。 |
| `DiffCount` | `int` | 片 | 差异片数（8 连通域个数）。`Aggregation=Total` 时退化成 0 或 1。 |
| `DiffArea` | `double` | 像素 | 差异总面积（筛过 `MinArea`、也交过 `MaskRegion` 之后的面积和）。 |
| `MaxDiffComponentArea` | `double` | 像素 | 最大单片的面积；无差异时为 0。 |
| `MaxDiffValue` | `double` | 0~255 | 差异强度图的最大灰度值（**未过阈值**——是全图最大值，含被阈值滤掉的噪声）。 |
| `HasDiff` | `bool` | — | 判定结果：命中任一上限即 true。它等于 `Success` 取反的语义化表达，接「逻辑判断」时更好读。 |

> `Success` / `ErrorMessage` 由基类提供：执行成功 `Success=true`；判定 NG 或异常时 `Success=false`
> 且 `ErrorMessage` 写明原因。

### 两个容易误读的口径

1. **`MaxDiffValue` 不受 `Threshold` 影响**。它是整张差异图的最大灰度，用来回答"最深的那一下有多深"。
   一片噪声（面积 3 像素，被 `MinArea` 丢掉）也可能把 `MaxDiffValue` 顶到 200。
   所以「最大强度上限」这一项**不适合配 `MinArea` 很小**的场合——那时它基本在测噪声。
2. **`DiffArea` 是"片"的和，不是"整幅像素中不一样的个数"吗？** 是的，两者相同：
   阈值分割出来再按面积筛，剩下的区域总面积就是"被认为不一样的像素个数"。区别只在
   `DiffCount` 是否把它们拆开计数。

## 配置项（12 个，按属性面板顺序）

| 属性 | 类型 | 默认 | 取值域 | 说明 |
| --- | --- | --- | --- | --- |
| `Algorithm` | `DiffAlgorithm` | `Absolute` | `Absolute` / `Signed` / `Relative` | 差分算法。换算法＝换口径，必须重调阈值。 |
| `Polarity` | `DiffPolarity` | `Both` | `Both` / `Brighter` / `Darker` | 极性：双侧 / 只算变亮（被测比参考亮）/ 只算变暗。 |
| `Aggregation` | `DiffAggregation` | `Components` | `Components` / `Total` | 连通域按片统计 / 只给总面积（片数退化成 0/1）。 |
| `SizeMismatch` | `SizeMismatchPolicy` | `Fail` | `Fail` / `ScaleTest` / `CropCommon` | 两图尺寸不一致时的策略。默认硬失败，绝不静默缩放。 |
| `Threshold` | `double` | `30` | 0~255（夹取） | 差异阈值：差异强度高于此值才算"不一样"。 |
| `MinArea` | `double` | `20` | ≥0（夹取） | 单片面积下限（像素）：小于此值的差异整片丢弃。 |
| `MaxTotalArea` | `double` | `0` | ≥0（夹取） | 总面积上限（像素）：超过即 NG。0 = 不判。 |
| `MaxComponentArea` | `double` | `0` | ≥0（夹取） | 单片面积上限（像素）：任一单片超过即 NG。0 = 不判。 |
| `MaxValueLimit` | `double` | `0` | 0~255（夹取） | 最大差异强度上限：超过即 NG。0 = 不判。 |
| `AmbientFloor` | `double` | `8.0` | ≥0（夹取） | 相对差的环境亮度下限（分母 = `max(参考灰度, 本值)`）。仅 `Relative` 生效。 |
| `SignedDisplay` | `double` | `128.0` | 0~255（夹取） | 有符号差的显示零点。仅 `Signed` 生效。 |
| `DisplayViewIndex` | `int` | `1` | 0~9（夹取） | 结果上屏窗口序号；0 = 不上屏。 |

所有数值参数都挂了 `partial void OnXxxChanging(ref T value)` 夹取钩子：
**界面上填越界值、老方案里存了越界值，都会在这里被夹回合法区间**，不会抛异常也不会静默走极端分支。

## 枚举取值与上屏文案

属性面板显示中文（读 `[Display(Name)]`），存盘与脚本里用的是**成员名**（`Absolute` 等）。
改名不影响已配流程——值仍是成员名。

| 枚举 | 成员 → 面板文案 |
| --- | --- |
| `DiffAlgorithm` | `Absolute` → 绝对差（\|被测 − 基准\|）；`Signed` → 有符号差（被测 − 基准）；`Relative` → 相对差（按参考图亮度归一化） |
| `DiffPolarity` | `Both` → 双侧（变亮变暗都算）；`Brighter` → 只算变亮（被测比参考亮）；`Darker` → 只算变暗（被测比参考暗） |
| `DiffAggregation` | `Components` → 连通域（按片统计）；`Total` → 总面积（不分片） |
| `SizeMismatchPolicy` | `Fail` → 硬失败（推荐）；`ScaleTest` → 缩放被测图到参考图尺寸；`CropCommon` → 裁到公共区域 |

**新增枚举成员必须补 `[Display(Name="中文")]`**，否则面板会显示成员名。

## 端口与参数的对应关系（想清楚再接）

| 想做的事 | 接/改哪个 |
| --- | --- |
| 只关心工件中心那块区域 | 接 `MaskRegion`（用「创建 ROI」画一块矩形接进来） |
| 只关心"有没有多出来东西"（不允许变亮） | `Polarity = Darker` |
| 同一批件整体亮度会漂 | `Algorithm = Relative` + `AmbientFloor` 抬到 30 以上 |
| 要数"有几处脏污" | `Aggregation = Components` + `MinArea` 抬到能滤噪的程度 |
| 只要"累计脏了多少像素" | `Aggregation = Total` + `MaxTotalArea` |
| 下游要按瑕疵轮廓再分析 | 接 `DiffImage` 给「预处理」做形态学，或直接接「Blob 缺陷检测」 |
| 判定要落在流程分支上 | 接 `HasDiff`（bool）到「逻辑判断」，比读 `Success` 更直观 |
| 现场要留证据图 | 接 `OverlayImage` 到「结果展示」或「数据记录」 |

## `.vms` 里存什么

本步骤在方案文件里只存**参数值**（上表 12 项），不存图。
`Reference` / `Test` 端口的值来自上游步骤，随上游一起持久化或随每轮执行刷新；
`MaskRegion` 若接了「创建 ROI」，其几何形状存在上游步骤里，不在本步骤里。

因此：**把基准图换掉，改的是上游那一步，不是本步骤**。
如果你在排查"换了基准图却没生效"，先去看上游「读图」/基准库那一步存的是哪张图。
