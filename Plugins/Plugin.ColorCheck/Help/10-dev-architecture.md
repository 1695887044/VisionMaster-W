# 颜色序列检查 · 开发：架构与数据流

> 分类：图像处理 ｜ 来源：插件 Plugin.ColorCheck ｜ 读者：开发

四个文件 + 一份内核，各管一段，**边界切得很硬**：

| 文件 | 职责 | 依赖 |
| --- | --- | --- |
| `ColorCheckPlugin.cs`（986 行） | 宿主契约、画布双向同步、采样几何、判定与投射 | `Core.Halcon`、`Core.Halcon.Color`、`Core.Controls` |
| `ColorSequenceAnalyzer.cs`（675 行） | **纯数组进、纯数组出**的自适应找线：阈值 / 分段 / 线距 / 回归 / 铺点 | 只用 `ColorVocabulary` |
| `RecipeTable.cs`（140 行） | 配方文本解析、按芯数匹配、逐位比对 | 只用 `ColorVocabulary.Any` |
| `ColorCheckView.xaml(.cs)`（330 + 29 行） | 绑定与三个按钮的转发，**零业务逻辑** | `ImageEdit`、`LinkableValueEditor`、`ColorThresholdEditor` |
| 内核 `Services/Core.Halcon/Color/` | `ColorSampler`（给点取色）、`ColorVocabulary`（RGB→颜色词）、`ColorThresholds`（默认值）、`ColorMarks`（画面语言 green/red/orange） | HALCON |

**分工口径（内核注释里写死的）**：内核负责"**给点取色**"，算子负责"**点怎么来**"。
颜色序列检查自己算 5 条采样线的点；区域颜色检查让内核把 ROI 变成区域再抽样。**取色只有那一条路**。

## 状态存在哪几份，谁是真源

| 状态 | 真源 | 副本 | 同步方向 |
| --- | --- | --- | --- |
| 采样区 | `RoiParams`（`double[5]`，落盘的就是它） | `CanvasRois` 里那一个 `DrawingObjectInfo`（画布句柄） | 打开配置：`RoiParams` → 播种画布；改框：画布 → `RoiParams` |
| 显示图 | `Image` 端口的上游值 | `DisplayImage`（**自己 `new HImage(upstream)` 拷的一份**） | 端口变化 → 拷一份上屏；**从不反向** |
| 阈值 | 内核 `ColorThresholds` 的默认值 | 插件上 13 个 `[StepConfig]` 属性（各算子一份） | 构造时取默认 → 用户改 → 落盘 → 运行期 `BuildThresholds()` 现收成对象交给内核 |
| 颜色词 | 内核 `ColorVocabulary.AllNames` | 无副本 | 只有一个来源 |
| 配方 | `RecipeText` 文本 | `Recipe` 列表（**每次运行现解析，不缓存**） | 单向 |
| 试算记忆 | `_lastMeasured`（插件实例字段） | 无 | 只在"试算 → 存成配方"之间活 |

**三条同步回路**（画布 ↔ 配置 ↔ 视图）：

1. **控件增删** → `CanvasRois.CollectionChanged` → `OnCanvasRoisChanged` → 新项订阅 / 旧项退订 `PropertyChanged` → `WriteBackRoi()`（取 `LastOrDefault`）。
2. **拖句柄** → 控件回写 `info.HTuples`（INPC）→ `OnRoiTuplesChanged` → **只认集合里最后一个** → `RoiParams = ToParams(info)`。
3. **上游图** → `Image.PropertyChanged`（只认 `IInputPort.Value`）→ `ShowUpstreamImage()` → 拷一份进 `DisplayImage`。

回路 1 和 2 都必须被 `_seedingCanvas` 刹车罩住（见 12 页 B1）。

## `RunAlgorithm` 从头到尾

```
① Image.GetTypedValue() → null 或 !IsInitialized() → Fail(上游没有图像…) → return
② RoiParams == null || Length != 5 → Fail(尚未框选采样区…) → return
③ (int)image.CountChannels().D < 3 → Fail(要彩色图，当前只有 N 个通道) → return
④ TryAnalyze(image, out result, out judgement, out error)
     ├─ TryBuildProfile：采样几何 + 取色 + 中位合成 + 高位深归一
     ├─ ColorSequenceAnalyzer.Analyze：阈值 → 分段 → 线距 → 回归 → 铺点 → 逐根分色
     ├─ ReverseSequence：Names / FromSegment / Positions 一起镜像（Positions 按 length-1-p）
     ├─ 回填 RowsOfProfile / ColsOfProfile（剖面索引 → 图像坐标的换算表）
     ├─ 有 DepthNote 就 Insert(0, …) 塞进 warnings 队首
     └─ TryJudge：端口优先 → 否则解析配方并按根数匹配 → 期望词校验 → Compare → BuildVerdict
   任一步 false → Fail(error) → return（error 已经是"怎么补"的完整句子）
⑤ 逐条 context.Logger.Warn(warning)
⑥ 写八个输出端口：Count / Sequence / Detail / Rows / Result / BadIndex / Verdict / Expected
⑦ 四条 Info 日志：根数与序列、线距与三阈值、逐根来源、判定
⑧ ProjectResult(image, result, judgement)
```

**判定失败不算 `Fail`**：`Result=false` 时⑦⑧照走、端口照写。只有"跑不动"才 `Fail`。

## 采样几何（`TryBuildProfile`）

| 步骤 | 做法 |
| --- | --- |
| 轴向 | `phi = RoiParams[2]`；`sin/cos` 展开。**因为图像行坐标向下**，`phi=0` 时长轴单位向量是 `(-sin, cos)`，短轴是 `(cos, sin)` |
| 判走向 | `firstIsLong = half1 >= half2` → 大的一半当"线束走向"，小的一半当采样范围（`acrossHalf = min(half1, half2)`） |
| 采样点数 | `sampleCount = round(2 × acrossHalf) + 1`，`< 8` 直接报错 |
| 采样线 | `lineOffsets = { -4, -2, 0, 2, 4 }`，5 条 × `sampleCount` 个点，**线优先**（line-major）排布 |
| 越界 | 每个点当场判行是否落在 `0 到 height-1`、列是否落在 `0 到 width-1` 之内，越界即报错（原文带 WxH 与命中的那个点） |
| 取色 | `ColorSampler.TryReadPixels(image, rows, cols, out channel, out error)` |
| 归一 | `NormalizeTo8BitIfHighBitDepth`：实测最大灰度 > 255 时按 `k = 255/max` **等比压回**，返回 true 就带一条 note |
| 合成 | 同一短轴位置上 5 条线的值**逐通道取中位**（`MedianOfFive`，`bucket[Length/2]`）→ 一条 `ColorProfile(R[],G[],B[])` |
| 换算表 | 剖面坐标取**中间那条线**（`middleLine = lineCount/2`）的行列，另存两张 `double[]` 表 |

## 分析器（`ColorSequenceAnalyzer.Analyze`）的六段

骨架是"**抓得到的先抓（暗 / 彩 / 极亮），由它们的间距估出线距，再把整条链按等间距铺开**"。
不可靠的分支一律**早退并给 error**，绝不硬凑根数。

**为什么不用"找边界"的思路**：白线（明度约 240）和浅色背景（约 235）只差几个灰阶，颜色上根本不可分——
任何阈值分割、边缘检测、变化点检测走到白线这里都会断。所以"抓不到的那根"必须由等间距**补**回来，
而不是指望它被检出。这也是 `Detail` 端口要区分"检 / 补"、界面要把补出来的那几根画成橙字的原因。

```
0. 逐点 v = max(R,G,B)，chroma = v - min(R,G,B)
1. 结构闸门：v 的极差 < 25 级 → error（框到空白背景）
   暗线阈值 = Mode(v) - DarkSpanRatio/100 × (Mode(v) - Percentile(v,2))
   亮阈值   = Percentile(v, BrightPercentile)
   彩度阈值 = Otsu(chroma)
   reliable[i] = v < darkTh || chroma > satTh || v > brightTh
2. FindSegments(reliable, minRun, maxRun)：连续可靠够长才算一段；长过 maxRun 的段丢弃并计数告警
   段数 < 2 → error（凑不出线距）
3. pitch：MedianGap(段中心) 与 EstimatePitchByAutocorrelation(v, chroma, hint) 交叉验证
   相差 ≤25% 取平均；分段恰为自相关的 ≥1.6 倍且接近整数 → 信自相关；否则信分段
   pitch < 2 或 > n/2 → error
   Regularize == false → FillFromSegments 后直接返回（对照组路径）
4. FitLineByIteration(centers, pitch)：分配线号 → 最小二乘回归 → 再分配，迭代 3 轮定 offset/slope
5. ExtendEnds：两端各试一格，判据 HasLocalStructure(局部起伏 ≥ 全局起伏的 20%)
6. ForceCount > 0 且与自动值不同 → 以检出范围中点为基准重铺，告警
   逐根：FindSegmentAround 命中就用该段（掐上下各 1/4 求中位色），未命中就按 RuleWindowRatio 开窗
   names[i] = ColorVocabulary.Classify(r,g,b, settings.Colors)
   fromSegment[i] = 是否命中可靠段
```

**自相关那条为什么必须"先衰减过零再取第一个显著峰"**：相邻像素本来就相似，零延迟附近的自相关天然最高，
直接取全局最大会给出 3 像素这种荒唐线距。**倍频**则由 `hint`（分段线距）在 `0.5 / 2.0 / 1/3` 三个候选里挑最贴近的那个消掉。
信号选的是**边缘强度**（`|Δv| + |Δchroma|`）而不是亮度本身——亮度会被"黑线 vs 白线"的颜色差压住，周期看不出来。

## 判定与比对（`TryJudge` + `RecipeTable`）

- 解析：按行切（`\r\n` 与 `\r` 都先归一成 `\n`），`Trim` 后跳过空行与 `#` 开头；有 `=` 就前段当产品名、后段当颜色；
  没有 `=` 就整行当颜色、名字自动取 `$"{colors.Length} 芯"`。分隔符认 `,`、`，`、`、` 三种。
- 匹配：`MatchByWireCount` 按 `Colors.Length == 实测根数` 过滤；命中 0 条 → error；命中多条 → 取**第一条**并告警。
- 比对：`Compare` 先比长度（不等则 `badIndex = 0`），再逐位；期望位等于 `ColorVocabulary.Any`（`*`）跳过；
  **字面比较**，第一个不等的位号（从 1 起）返回。
- 期望词校验在比对**之前**：`expected.Where(w => !ColorVocabulary.IsKnown(w))`，有陌生词就 error 并列出可用词。

## 投射（`ProjectResult`）

```
DisplayViewIndex <= 0 或 剖面坐标表为空 → 直接 return
marks = [ ColorMarks.Line(剖面首点, 剖面末点, verdictColor) ]
每根：ColorMarks.Text(row, col + 10, "N.颜色", LabelColor(i, …))
结论行：贴剖面首点上方一个 pitchGap；贴不下改贴末点下方；再越界 Clamp 到 [10, height-10]
this.PublishPreview(image, DisplayViewIndex, marks)   ← 整段包在 catch(Exception) 里，静默
LabelColor：不符那根 → red；FromSegment → green；补出来的 → orange
pitchGap = max(16, pitch)；标签间距下限 max(8, pitch)
```

画面语言（`green` / `red` / `orange`）是**内核常量**，字面量必须与 HALCON 窗口认的颜色名一致，
所有颜色算子共用——现场不管看哪个算子，画面上颜色的含义都一样。

## 内存所有权（谁 new 谁 Dispose）

| 对象 | 谁创建 | 谁释放 |
| --- | --- | --- |
| `DisplayImage` | `ShowUpstreamImage` 里 `new HImage(upstream)` | **插件自己**：再次显示前先 `Dispose` 旧的；`Dispose()` 里释放并置 null。基类管不到它 |
| `Image` 端口的上游图 | 框架/上游 | 框架。本插件**从不 Dispose 上游句柄**（所以才拷副本） |
| 八个输出端口值 | 全是 `bool` / `int` / `string`，没有 `IDisposable` | 基类 `AutoDisposeRoundOutputs` 轮首回收，对本插件等于空转 |
| `ColorSampler` 里的通道对象 | `access_channel` 出来的 `HObject` | 内核 `finally { channelObject?.Dispose(); }` |
| 取回来的灰阶 | `HTuple` → `int[]` 托管数组 | GC |

**为什么必须走元组版取点而不是把通道包成 `HImage`**：一次调用拿回整批点，比逐点调用快两个数量级，
也不必管 Halcon 句柄——那条路上踩过 HALCON `#4056 object-ID is NULL`。
