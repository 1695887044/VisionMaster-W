# 图像差分 · 不变量清单

> 分类：图像处理 ｜ 来源：插件 Plugin.ImageDiff

开发页。每当有人改这个插件的代码，按这张清单逐条过一遍。
**每条都写了"违反后的症状"和"怎么发现"**——只列结论的清单没人会真的去查。

## A. 算法正确性

### A1 差分必在 real 域做

**判据**：`ComputeDifference` 里对两张灰度图都先 `convert_image_type(..., "real")`，
之后所有减法/除法都在 real 域，最后一步才转回 `byte`。

**违反后的症状**：`0 − 30` 在 byte 域饱和成 `0`，"变暗的差异全被吃掉"。
画面仍有大片变化、`DiffArea` 还有数，只是**变暗方向永久失效**。最坏的是它不报错。

**怎么发现**：构造两张纯色图（基准 30 灰、被测 0 灰），`Polarity=Darker` 时必须得到
约 30 的差异强度；若得到 0，就是踩了这条。

### A2 `AbsDiffImage` 只有 4 参

**判据**：托管 API 是 `HOperatorSet.AbsDiffImage(HObject image1, HObject image2, out HObject imageAbsDiff, double mult)`
——**没有 `add` 参数**（与 `sub_image` 的 5 参不对称）。

**违反后的症状**：`CS1501 "AbsDiffImage"方法没有采用 5 个参数的重载`，编译期就红，不会漏到运行期。

**怎么发现**：编译。别按 HALCON 文档抄 5 参写法。

### A3 极性在 real 域分量上生效，不是在结果图上截断

**判据**：先算 `pos = T − R`、`neg = R − T` 两个 real 分量，再按 `Polarity` 选
`max(pos,neg)` / `pos` / `neg`。**不许**把结果图减去某个基准值来"筛掉一个方向"。

**违反后的症状**：`SignedDisplay` 或阈值的一点偏移会连带把另一个方向吃掉一部分，
表现是"极性看起来生效了，但面积数比真值小一截"。

**怎么发现**：喂一张"左侧变亮 40、右侧变暗 40"的对半图，
`Brighter` 与 `Darker` 两种极性下的 `DiffArea` 必须**相等且各约为一半**。

### A3b `Signed` 的方向由符号表达，不是"把幅度加到零点上方"

**判据**：`Signed` 的显示图 = `pos − neg + SignedDisplay`。`Both` 保留双向符号；
`Brighter` 只留正差（中灰以上）；`Darker` 只留负差并**带负号**（中灰以下）。
**不许**在 `Darker` 分支里把 `neg` 当正幅度直接加零点——那会让变暗区显示成变亮。

**违反后的症状**：选了「只算变暗」，画面上却是中灰以上的亮块；`DiffImage` 与 Help 的
`pos − neg + 零点` 算式对不上，人工看图会判反方向。

**怎么发现**：基准 100、被测 90（暗 10）时，`Signed+Darker` 的像素必须是 118（不是 138）；
被测 110 时 `Signed+Brighter` 必须是 138。`FlowCanvasChecks` 的 A5a~A5d 已钉住这四条。

### A4 面积筛选用 `select_shape`，不用形态学开运算

**判据**：`Segment` 里是 `connection → select_shape('area','and',MinArea,∞)`。
**不许**用 `opening`（圆盘）做面积筛。

**违反后的症状**：细长划痕被整条腐蚀掉——而划痕恰是差分最典型的缺陷。
表现为"划痕检不出来，大块脏污正常"。

**怎么发现**：构造一张 1 像素宽、200 像素长的线，`MinArea=20` 时它必须被检出（面积 200）。

### A5 先 `connection` 再筛

**判据**：连通域拆分必须在筛面积之前。

**违反后的症状**：`DiffCount` 恒为 0/1，`MaxDiffComponentArea` 恒等于 `DiffArea`，
"数几处"彻底失效。

**怎么发现**：构造两块不相连的方块，`Aggregation=Components` 时 `DiffCount` 必须是 2。

### A6 `MaxDiffValue` 不受阈值影响

**判据**：`Range(diff, null)` 在全图上取极值，**入参不是分割后的区域**。

**违反后的症状**：如果把区域传进去，`MaxDiffValue` 会变成"合格差异里的最大强度"，
现场的"最深划痕有多深"就测不出来了；而且它随阈值变化，历史数据不可比。

**怎么发现**：把 `MinArea` 调到极大（全部丢弃），`MaxDiffValue` 应该**不变**。

## B. 资源与句柄

### B1 交给输出端的图不许在本轮 Dispose

**判据**：`DiffImage.Value = diff` 之后，`finally` 里**没有** `diff.Dispose()`；
`OverlayImage` 同理。

**违反后的症状**：端口上挂着已释放的句柄，下游一用就是野指针——
轻则 `HALCON error #4051`，重则进程直接崩。

**怎么发现**：串一个下游步骤（比如「结果展示」）读 `DiffImage`，连跑两轮不崩。

### B2 每层 `try/finally` 只释放自己 new 的对象

**判据**：service 返回的 `HImage` 归调用方释放；`reference`/`test` 是上游的，不碰；
`referenceAdjusted`/`testAdjusted` 在**外层** finally；`refGray`/`testGray`/`diffRegions` 在内层 finally。

**违反后的症状**：双释放（`#4051` 或崩溃）或漏释放（长时间跑批内存爬升）。

**怎么发现**：`ScaleTest`/`CropCommon` 两条分支各跑 100 轮，看进程私有内存是否回落。

### B3 `renderOverlay` 的中间图全部 finally 释放

**判据**：`colorBase`、`composed`（以及内部 `proto`/`paintedH`）都在 finally 里 Dispose，
只返回新建的 `HImage(result)`。

**违反后的症状**：上屏开着时每轮泄漏 1~2 张全尺寸图，跑一天耗掉几个 GB。

**怎么发现**：`DisplayViewIndex=1` 连跑 500 轮，任务管理器看内存曲线是否锯齿后不回落。

## C. 守卫与判定

### C1 尺寸不一致默认硬失败

**判据**：`SizeMismatch` 的默认值是 `Fail`；`Fail` 分支必须 `Fail(...)` 并 `return`，
文案里必须出现"不会替你静默缩放"这一类明确表态。

**违反后的症状**：新装的插件默认就静默缩放，得到"看着有数其实全错"的结果，
而且日志干净。这是 P1-B S1 裁决明确要杜绝的坏法。

**怎么发现**：看 `DiffEnums.SizeMismatchPolicy.Fail` 是否在 `[DefaultValue]` 里；
喂两张不同尺寸的图，`Success` 必须为 false。

### C2 通道守卫放行 `1 ↔ ≥3`，其它组合拒绝

**判据**：`(refC == 1 && testC >= 3) || (testC == 1 && refC >= 3)` 放行，其余 `mismatch`。

**违反后的症状**：2 通道图（很少见，但某些相机 YUV 输出会）一路走到 `ToGray` 抛异常，
错误信息变成"HALCON 内部错误"而不是人话。

**怎么发现**：构造一张 2 通道图，`ErrorMessage` 里应该出现"通道数不一致"。

### C3 三项上限互相独立，0 = 不判

**判据**：`MaxTotalArea`、`MaxComponentArea`、`MaxValueLimit` 三项各判一次，
用 `> 0` 作为"参与判定"的门槛，超限项用「；」拼进同一条 `ErrorMessage`。

**违反后的症状**：
- 写成 `>= 0` → 三项全部恒为 NG（`DiffArea > 0` 恒成立）；
- 用 `else if` 串起来 → 只报第一个超限项，另两项的问题被隐藏，现场不知道该调哪个。

**怎么发现**：三项都填 0 时必须永不 NG；三项同时超限时 `ErrorMessage` 里要看到三条原因。

### C4 判定 NG 时测量值仍然输出

**判据**：`Fail(...)` 在填完端口之后调用。

**违反后的症状**：NG 时 `DiffArea` 停在上一轮的值（或 0），
现场"为什么 NG"只能靠肉眼，无法事后追数据。

**怎么发现**：造一个必然 NG 的配置，看 `DiffArea` 是否仍为真实值。

### C5 参数夹取钩子全覆盖

**判据**：8 个 `partial void OnXxxChanging(ref T value)` 覆盖全部数值参数；
`Threshold`/`MaxValueLimit`/`SignedDisplay` 夹 `[0,255]`，`MinArea`/`MaxTotalArea`/
`MaxComponentArea`/`AmbientFloor` 夹 `≥0`，`DisplayViewIndex` 夹 `[0,9]`。

**违反后的症状**：老方案里存了负面积下限 → `select_shape` 收到负下界（HALCON 行为未定义）；
`DisplayViewIndex=99` → `PublishPreview` 找不到窗口，静默不上屏。

**怎么发现**：手动把 `.vms` 里的 `Threshold` 改成 `-5`、`DisplayViewIndex` 改成 `99`，重载方案看是否被夹回。

## D. 契约与工程

### D1 `RunAlgorithm` 必须是 `public override`

**判据**：基类里是 `public`。

**违反后的症状**：`CS0507 无法更改"VisionPluginBase.RunAlgorithm(IExecutionContext)"访问修饰符`。

**怎么发现**：编译。

### D2 端口取值一律 `ActualValue`

**判据**：`Reference.ActualValue` / `Test.ActualValue` / `MaskRegion.ActualValue`。

**违反后的症状**：这条不只是风格问题。用 `Value`（`object?`）会引出
`m.IsInitialized()` 与 `IModuleCatalogCommonExtensions.IsInitialized<T>(IModuleCatalog)` 的
类型推断冲突（`CS0411`）；更糟的是**编过之后**语义也不对——`Value` 是"手动值"入口，
端口链接上游时它可能是默认值。

**怎么发现**：`grep -n "\.Value" ImageDiffPlugin.cs`，输入端口处出现 `Value` 就是违规。

### D3 `PublishPreview` 用五参重载，参数位置不能省

**判据**：`this.PublishPreview(overlay, DisplayViewIndex, title, info, null)`，
`info` 是 `IEnumerable<ImageInfoRow>`。

**违反后的症状**：`CS1503 参数 N: 无法从 "ImageInfoRow[]" 转换为 "IEnumerable<MeasureAnnotation>"` ——
因为**没有 params 数组重载**，第 4、5 个坑位必须各自给。

**怎么发现**：编译。

### D4 不在图上画文字

**判据**：全工程无 `DispText` / `SetColor(HObject, ...)` / `HMessage`。
本插件只有 `RenderOverlay` 做"拷贝 + 涂色 + 合成"。

**违反后的症状**：`CS1503 参数 1: 无法从 "HObject" 转换为 "HTuple"`（窗口句柄 vs 图像对象），
或者绕过类型改走 `OpenWindow('buffer')` —— 那就违反了 R36（离屏烘焙只许在 `AnnotationBaker`）。

**怎么发现**：`grep -rn "DispText\|OpenWindow" Plugins/Plugin.ImageDiff/` 必须**零命中**。

### D5 `ImageDiffService` 无静态字段

**判据**：`internal static class ImageDiffService` 里只有方法和局部变量，没有 `static` 字段。

**违反后的症状**：`[ParallelSafe]` 的前提被破坏——并行分支里两个实例共享缓冲，
表现是偶发串图、偶发错数。这类 bug 概率性出现，极难复现。

**怎么发现**：`grep -n "static " ImageDiffService.cs` 逐行看，命中处必须是方法或类声明。

### D6 工程必须在 `Plugins\` 下并登记 `VisionMaster.sln`

**判据**：工程放在 `Plugins\Plugin.ImageDiff\`；`VisionMaster.sln` 里有它的 Project 条目。

**违反后的症状**：
- 不在 `Plugins\`：`Directory.Build.targets` 不投递，`Modules\` 里没有 DLL，宿主扫不到；
- 不在 sln：**改代码后 `Modules\` 里还是旧 DLL，且构建没有任何报错**——这是历史上坑过三个插件的那种失效。

**怎么发现**：`grep -c "Plugin.ImageDiff" VisionMaster.sln` ≥ 1；构建后看 `Modules\Plugin.ImageDiff.dll` 的 mtime
≥ 源码 mtime。

### D7 改过 `.md` 内容必须重建并核资源数

**判据**：`Help\` 下 `.md` 数与 DLL 内 `.Help.` 内嵌资源数相等，且 DLL mtime ≥ `.md` mtime。

**违反后的症状**：宿主帮助弹窗里还是旧文案，或者新页根本没打进去（增量构建静默漏打包）。

**怎么发现**：见 11 页第 9 节的两条命令，两个数必须相等。

## E. 断言建议（`FlowCanvasChecks/ImageDiffCheck.cs`）

本插件已有独立断言宿主：`FlowCanvasChecks/ImageDiffCheck.cs`（合成确定性图像，不依赖样图）。
下表是它覆盖的口径；新增能力时按同样方式补行：

| 断言 | 做法 | 反向判据 |
| --- | --- | --- |
| A1 real 域 | 基准 30 灰 / 被测 0 灰 + `Darker` → 最大强度 ≈ 30 | 改成 byte 域后必须变红 |
| A3 极性对称 | 左半变亮 40 / 右半变暗 40 → `Brighter` 与 `Darker` 面积相等 | 去掉分量逻辑后必须变红 |
| A3b Signed 方向 | 基准 100 / 被测 90 → `Signed+Darker` 像素 118；被测 110 → `Signed+Brighter` 138 | 把负差当正幅度加零点 → 变 138/148 |
| A4 细线存活 | 1×200 细线 + `MinArea=20` → 检出面积 200 | 换成 `opening` 后必须变红 |
| A5 连通域计数 | 两块不相连方块 → `Components` 口径 `DiffCount == 2` | 不 `connection` 时得 1 |
| A6 全图极值 | `MinArea` 调到极大 → `MaxDiffValue` 不变 | 把区域传进 `Range` 后必须变红 |
| C1 尺寸守卫 | 两张异尺寸图 → `Success == false` 且原文命中 | 把默认改成 `ScaleTest` 后必须变红 |
| C3 上限独立 | 三项同填 0 → 永不 NG；三项同超 → 原因三条 | 改 `>= 0` 或 `else if` 后必须变红 |

> 写断言时的通用纪律：**每一条都要能反向证伪**——"改坏了会不会变红"。
> 一条断言如果改坏代码仍然绿，它就不是断言，只是装饰。
