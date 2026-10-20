# 颜色序列检查 · 开发：不变量与陷阱清单

> 分类：图像处理 ｜ 来源：插件 Plugin.ColorCheck ｜ 读者：开发

**改这个插件前逐条对照**。每条写成 `症状 → 机理 → 守门断言名`。
分两组：**A 组违反后当场红或抛**（好查），**B 组违反后静默出错**（难查，本插件的历史 bug 全在 B 组）。

守门断言主要在 `FlowCanvasChecks\ColorSyntheticChecks.cs`（`Run()` 里 16 条 `Check(...)`：
合成图生成 / 像素抽检 / 落盘一份四步方案 / 8 位与 16 位各跑一遍同一套黄金序列）。
画布手势那部分由 `FlowCanvasChecks\RoiInteractionChecks.cs`（8 条，共享控件 `ImageEdit`，非本插件独有）守。
手册结构由 `FlowCanvasChecks\HelpManualCheck.cs`（HM0~HM17）守。
**查不到断言的条目一律写明"无断言，靠人工核对"**，不许拿别人的断言名顶包。

---

## A 组：违反会当场红或抛

**A1 三条前置校验是唯一的 `Fail` 入口，顺序不许换**
`Image` 空/未初始化 → `RoiParams` 长度不是 5 → `CountChannels() < 3`，三条都在 `TryAnalyze` 之前，任一命中就 `return`。
症状：把通道数校验挪到 `TryAnalyze` 之后 → 灰度图先进了采样几何、`ColorSampler` 只返回 1 个通道，
报错变成内核那句"颜色序列检查要彩色图…"以外的**语义错乱文本**，或直接崩在中位取值上。
守门：`【8 位】流程编译执行、四步全部 Success`、`【16 位】流程编译执行、四步全部 Success`（这两条同时反证"通道校验放行的是彩色图"）。

**A2 剖面长度不足 8 个点必须报错，不许硬算**
`sampleCount = round(2×acrossHalf)+1`，`< 8` 直接 error；分析器 `Analyze` 开头还有一道 `n < 8` 的同款闸。
症状：删掉闸 → 自相关的 `kMax = min(n/2, n-2)` 变成 0 或负，返回 0，线距退回分段值；段数不足时
报的是"凑不出线距"这种**误导现场去调参**的话，而真相是框太窄。
守门：**无断言，靠人工核对**（合成图是 12 根线束的宽剖面，走不到这条闸）。

**A3 结构闸门（亮度极差 < 25 级）不许放宽**
`brightest - darkest < MinContrastLevels(25)` → error。
症状：放宽 → 空白背景上也硬铺出一串颜色词，`Detail` 全是"补"，判定看起来"跑通了但完全没依据"。
守门：`【像素抽检】12 根线束中心的亮度与调色板一致（含高光带 +22）`（它保证合成图的对比度是真的，
从而让"有结构才出序列"这件事在 8 位/16 位两条链上都可反证）。

**A4 `ColorVocabulary.Classify` 是分色的唯一入口**
`names[i] = ColorVocabulary.Classify(r, g, b, settings.Colors)`。阈值对象由 `BuildThresholds()` 从 13 个属性现收。
症状：在插件里另写一套 if-else 分色 → 与「区域颜色检查」给出**两种答案**，配方与画面词表立刻对不上话。
守门：`【8 位】序列检查报出 12 根，与黄金序列逐位一致`（黄金序列就是这套词表的顺序）。

**A5 期望词必须先过词表校验再比对**
`expected.Where(w => !ColorVocabulary.IsKnown(w))`，有陌生词就 `error` 并列出 `AllNames` 与 `*`。
症状：删掉校验 → "深红"这种词**永远匹配不上**，现场看到"应为 深红、实测 红"，不会怀疑配方写错了。
守门：`【8 位】线序判定合格（全通配配方，结论带配方名）`——**这条只覆盖正例，陌生词那条负例无断言，靠人工核对**。

**A6 投图整段必须包在 `catch` 里，且 `DisplayViewIndex <= 0` 先 return**
`ProjectResult` 的 `PublishPreview` 之外没有任何会写端口的动作。
症状：去掉 `catch` → 窗口号越界或事件订阅者抛异常，把一次**成功的检测判成失败**（与 `Plugin.PreProcessing` 口径冲突）。
去掉 `<= 0` 守卫 → 用户明明选了"不投射"，画面照样投。
守门：**无断言，靠人工核对**（`ColorSyntheticChecks` 走的是无界面的流程执行，不验投射）。

**A7 `Dispose()` 末尾必须 `base.Dispose()`，且必须真的重写**
基类那一份回收**输出端口**承载的值；本插件那一份回收 `DisplayImage`（上游图的私有副本）并退订 `Image.PropertyChanged`。
症状：不重写 → 每开一次配置欠一张整图的句柄，等 GC 兜底；只写自己不写 base → 端口值不回收。
守门：**无断言覆盖本插件的 Dispose**（`CreateRoiChecks` 的 `【释放】Dispose 幂等不抛` 是别的插件的，不能顶包）。

---

## B 组：违反后**静默**出错

**B1 `_seedingCanvas` 刹车不许去掉**
`Initialize(stepData)` 播种画布集合时必然触发 `CollectionChanged` → `WriteBackRoi()`。
没有这个标志，回写会把刚读出来的 `RoiParams` 用"画布句柄的写法"（`HTuple.D` 数组）再叠一遍。
症状：**重开配置后采样区漂移/翻倍/被清空**；打开一次配置就把方案数据改了（用户什么都没动）。
守门：**无断言，靠人工核对**（`ColorSyntheticChecks` 直接构造流程、不经过配置视图）。
同类刹车在 `Dispose()` 里也必须压着——清空动作若走回写，会把已保存的 `RoiParams` **抹成空数组**，
表现是"关掉配置窗口再打开，框没了"。

**B2 拖拽回写只认 `CanvasRois.LastOrDefault()`**
`OnRoiTuplesChanged` 里有 `if (!ReferenceEquals(info, CanvasRois.LastOrDefault())) return;`；
`WriteBackRoi` 同样取最后一个。
症状：改成"认第一个"或"全部拼起来" → 用户重画了第二个框，**保存的却是第一个**，界面上看着对、跑出来错。
机理：本算子只需一个采样区，"用户画了多个"这件事只能有一个确定性的裁决口径。
守门：**无断言，靠人工核对**。

**B3 新增项要订阅、删除项要退订 `PropertyChanged`，两者都在 `OnCanvasRoisChanged` 里**
`e.NewItems` 加、`e.OldItems` 减。
症状：忘订阅 → 拖完句柄 `RoiParams` 不变，**试运行对、重开方案错**；忘退订 → 已删的 `DrawingObjectInfo`
被迟到回写"复活"或空引用抛。
守门：画布侧由 `RoiInteractionChecks` 的 8 条手势断言**间接**覆盖（共享控件行为），
"订阅链断没断"这一条**无断言，靠人工核对**。

**B4 `ShowUpstreamImage` 必须显示自己的副本，不许把端口里的句柄直接挂上界面**
`DisplayImage = new HImage(upstream)`，换图前先 `DisplayImage?.Dispose()`。
症状：直接把上游引用给 `DisplayImage` → 谁先 `Dispose` 谁把对方的句柄弄坏（表现是"过几轮之后画布变黑/抛 HALCON 句柄错"），
而且基类轮首回收或上游重算都可能已经释放了它。
守门：**无断言，靠人工核对**。

**B5 `Image.PropertyChanged` 订阅在构造函数、退订必须在 `Dispose()`**
`OnImagePortChanged` 只认 `e.PropertyName == nameof(IInputPort.Value)`。
症状：不退订 → 配置窗口关了之后插件实例仍被端口持有，试运行反复开合会攒下一批"活着的死对象"，
每个都压着一张 `DisplayImage`（**这是泄漏面最大的一条**）。
守门：**无断言，靠人工核对**。

**B6 高位深归一必须是"等比压回 0~255"，且必须留 note 进 warnings 队首**
`max <= 255` 时**直接返回 false、不动数据**；否则 `k = 255/max` 逐通道逐点乘。
判据（暗线的众数/2% 分位、Otsu 的 256 桶直方图、黑 80 / 白 195 / 棕 175）**全是按 8 位标定的**。
症状：漏了归一 → `uint2` 相机图上"黑永远判不出、白满天飞"，黑白灰棕四类**全错**，而彩度类看着正常，
现场极难归因；`Mode()` 里 `hist[Clamp(v,0,255)]` 还会把所有超 255 的值**塞进同一桶**，众数直接失真。
守门：`【16 位】高位深归一后仍扫出同一条黄金序列（黑能判出、白不泛滥）`、
`【16 位】三个颜色节点都留下高位深归一的 warning（数值变小的原因可查）`、
`【8 位】不触发高位深归一（8 位图没有归一的道理）`。这三条是本插件**断言最硬的一块**。
注意：`TryAnalyze` 里是 `Warnings.Insert(0, DepthNote)`——**必须插在队首**，插到队尾会被后面五条告警挤出现场视野。

**B7 `ReverseSequence` 必须在判定之前、且三个数组一起镜像**
`Names` / `FromSegment` 用 `Reverse()`，`Positions` 用 `Reverse().Select(p => length-1-p)`。
症状：① 只反 `Names` 不反 `Positions` → 画面标签贴到**对面的位置**上，判定与画面互相矛盾；
② 把反向放到判定之后 → 配方得按"正序"写而画面显示反序，口径分裂；
③ 忘了 `length-1-p` 只做 `Reverse()` → `Rows` 端口与投射坐标全部错位（`Positions` 是剖面坐标，不是下标）。
守门：**无断言**（合成流程用的是全通配配方，`ReverseSequence` 默认 `false`，走不到这条）。靠人工核对。

**B8 剖面坐标 → 图像坐标必须走 `ToImageCoord` 线性插值，且越界夹回端点**
`Rows` 端口与每个标签的行列都由两张换算表（`RowsOfProfile` / `ColsOfProfile`）插值。
症状：直接把剖面下标当图像行号写进 `Rows` → 斜着画的采样区里**每一根的行号都是错的**，
下游按行号去找像素全落空；表为空时若不夹回，`table[0]` 越界抛。
机理：换算表取的是**中间那条采样线**（`middleLine = lineCount/2`），不是剖面的物理均值。
守门：**无断言**（`Rows` 的内容没被断言核过）。靠人工核对。

**B9 结论句只有 `BuildVerdict` 一个来源**
日志第四条 `Info`、`Verdict` 端口、画面结论行、试算摘要第六行**共用同一句**。
症状：在任何一处另写一句 → 现场看到"画面上写着线序正确、日志说根数不符"。
机理：根数不符时 `BadIndex` 恒为 `0`，只有 `Verdict` 能区分"全对"与"长度就不对"。
守门：`【8 位】线序判定合格（全通配配方，结论带配方名）`（断言里核的是结论带配方名这个形状）。

**B10 配方匹配按"根数"，多条同长取第一条并告警**
`MatchByWireCount` 的 `hits[0]`。
症状（这是现场最容易踩的）：用「把试算结果存成配方」追加的新行在**末尾**，
所以旧那条同芯数配方**继续赢**——用户明明存了新配方，比的还是老的，且只有一行 Warn。
守门：**无断言，靠人工核对**（合成方案用的是唯一一条全通配配方）。

**B11 `SavePreviewAsRecipe` 的去重是"整行 Ordinal 包含"**
`RecipeText.Contains(line, StringComparison.Ordinal)`，`line = $"{长度} 芯 = {逗号串}"`。
症状：手工写的 `10芯 = …`（没有空格）与生成的 `10 芯 = …` 是**两行不同文本**，去重失效 → 出现两条 10 芯，
回到 B10 那条陷阱。机理：`RecipeTable.Parse` 对没有等号的行自动取名 `"{N} 芯"`（**带空格**），
与生成行一致，但**用户手打的名字不受任何规范化**。
守门：**无断言，靠人工核对**。

**B12 `Regularize == false` 是"对照组路径"，它会跳过回归、端点外推与手动线数**
`Analyze` 在算完 pitch 之后直接 `FillFromSegments(...)` 返回。
症状：把这段改成"仍然回归但不铺点" → 现场失去了"到底有几根是直接看得见的"这个唯一参照，
调参变成瞎调；关掉规整化时 `WireCount` 却仍生效，就是口径分裂。
守门：**无断言，靠人工核对**。

**B13 阈值副本必须每轮现收（`BuildThresholds()`），不许缓存**
`SequenceSettings.Colors` 每次 `TryAnalyze` 都 new 一个 `ColorThresholds` 并把 13 个值逐字段抄进去。
症状：改成"构造时取一次内核默认值就一直用" → 界面上调完色卡**不生效**（试运行对、正式跑错），
而且 `ColorThresholds.Clone()` 那份默认值会被算子改脏、**污染别的算子**（内核注释明确要求各算子各带一份）。
守门：**无断言**（合成方案的色卡走默认值，改阈值这条路没被测过）。靠人工核对。

**B14 投射的窗口号必须直传，画色只能取 `ColorMarks` 常量**
`PublishPreview(image, DisplayViewIndex, marks)`；`green` / `red` / `orange` 来自内核。
症状：写成 `DisplayViewIndex + 1` → 选"窗口 1"投进第 2 格；自己写 `"lime"` → 与「区域颜色检查」画面语言不一致，
且这几个字面量必须与 HALCON 窗口认的颜色名一致（SDK 字面量只该待在内核常量里，不该散到插件里）。
守门：**无断言，靠人工核对**。

**B15 界面文案与算法口径不能各写一份**
本插件现存**两处已知的文案漂移**（界面上写"填示意图路径并点「载入」"、"阈值暂写死"，
ToolTip 说白线百分位"调小"而告警原文说"调高"）。
症状：现场照文案操作 → 找不到按钮、或按错方向调参然后以为算法坏了。
机理：这些句子早于"图像只有端口一个来源"和"色卡可调"两次改版。
守门：**无断言，靠人工核对**（`HelpManualCheck` 只核手册结构，不核视图文案）。**发现即修，别只写进手册**。
> 2026-10-20 已核销一处：`TryPreviewAnalyze` 的空图提示不再指向不存在的「载入」按钮，改为"绑端口/点执行，
> 或在画面上右键 → 打开图片，再试算"，并由 `ColorCheckSequenceCheck` 的 CC4 P5 断言钉住（含「载入」原话仍在，
> 旧的 `summary.Contains("载入")` 断言不失效）。

**B15b 试算的图源必须由视图显式传入，不许让插件自己猜**
`TryPreviewAnalyze()` 无参入口只是转发 `TryPreviewAnalyze(null)`（回退 `DisplayImage`）；
视图的 `TryAnalyzeCommand` 必须传 `ImageEditor?.HImage`。
症状：改回无参调用 → 画布右键「打开图片」换的图**永远不参与试算**，用户看到的是"图明明换了、结果还是老图的那一套"
（因为 `DisplayImage` 与控件 `HImage` 是两份状态，右键换图不经过插件）。
机理：共享控件 `HalconBase.OpenImage()` 直接 `this.HImage = image`，不通知插件。
守门：`ColorCheckSequenceCheck` 的 CC4 P6c 静态扫描（视图源码必须含 `TryPreviewAnalyze(ImageEditor?.HImage)` 且不得残留无参调用）+
P2/P3/P4 三条行为断言（传入图优先 / 无参回退 / `DisplayImage` 未被覆盖）。变异反证：改回无参 → P6c 准确变红。
另：带图重载必须是 `internal`——两个 public 同名重载会让 `Type.GetMethod("TryPreviewAnalyze")` 抛
`AmbiguousMatchException`，断言宿主与任何反射调用方都会当场炸（P6a 钉住）。

**B16 配置属性名不能随便改**
`.vms` 的 `InputValues` 键**逐字等于属性名**（无前缀、无版本位）。
症状：把 `DarkSpanRatio` 改名 → 旧方案里那个键**静默失配**，`ApplyConfigValues` 走"宽容跳过"，
属性回到默认 35，用户看到的是"什么都没改，判据自己变了"。
守门：**无断言**（`流程方案已落盘（可在软件里直接打开）` 与 `生产加载器能打开这份方案（主干 4 步：采集 → 序列 → 区域×2，连线完好）`
只保证**现有键**往返得通，改名它们不会红）。改名必须同时给旧键做兼容读。

---

## 并行安全声明的依据（`[ParallelSafe]`）

满足判定口径：纯视觉计算、无 `static` 可变字段（`ColorSequenceAnalyzer` / `RecipeTable` 全是静态**方法**，
只读入参、不写进程级状态）、不碰进程级独占资源、不写全局共享状态。
两个例外要盯着：`_lastMeasured` 是**实例字段**（只服务试算，运行期不碰）；
`ColorSampler` 内部每通道都 `finally` 释放 `HObject`，没有跨调用共享的 HALCON 句柄。
**注意匹配口径**：`FlowCompiler` 按类型 **FullName 字符串**匹配该特性（不是 `typeof` 等值），
为的是绕开"两份 `Core.Interfaces.dll`"导致程序集身份不一致、明明标了却报 `[并行不安全]`——别把特性写在自己的命名空间副本上。

## 断言台账：本插件真有 16 条，但覆盖面很不均

`ColorSyntheticChecks.Run()` 的 16 条里，与本算子直接相关的是这 13 条：
`采集 / 颜色序列 / 区域颜色 三个插件程序集可装载`、`合成图生成成功（8 位 PNG + 16 位 TIFF，同一张图的两个位深）`、
`【像素抽检】12 根线束中心的亮度与调色板一致（含高光带 +22）`、`流程方案已落盘（可在软件里直接打开）`、
`生产加载器能打开这份方案（主干 4 步：采集 → 序列 → 区域×2，连线完好）`、`【8 位】流程编译执行、四步全部 Success`、
`【8 位】序列检查报出 12 根，与黄金序列逐位一致`、`【8 位】线序判定合格（全通配配方，结论带配方名）`、
`【8 位】不触发高位深归一（8 位图没有归一的道理）`、`【16 位】流程编译执行、四步全部 Success`、
`【16 位】高位深归一后仍扫出同一条黄金序列（黑能判出、白不泛滥）`、`【16 位】线序判定合格`、
`【16 位】三个颜色节点都留下高位深归一的 warning（数值变小的原因可查）`
（另 3 条 `【8 位】红色指示灯…`、`【8 位】紫色色块…`、`【16 位】两个区域算子的主色与判定与 8 位一致` 属「区域颜色检查」）。

**盲区（改到这些就要人工验）**：配置视图与画布播种/回写（B1~B3）、`ReverseSequence`（B7）、
`Rows` 的坐标换算（B8）、`WireCount` / `MinRun` / `MaxRunRatio` / `RuleWindowRatio` 这些高级旋钮的取值面、
13 项色卡（B13）、同芯数多配方的取首条（B10）、投射与画面语言（A6、B14）、配方词表的负例（A5 的陌生词分支）。
补断言的正解是**照 `ColorSyntheticChecks` 的合成图路子加参数分支**，而不是写"跳过：本环境不支持"（AGENTS.md R38）。

## 改这个插件之后的必做动作

| 动作 | 判据 |
| --- | --- |
| 重建插件工程（改了生成器/基类相关则全量） | 只重建宿主跑的是旧 DLL（红线③、R16） |
| 核 DLL 真的带了 `Help` 资源 | 资源名数 == `Help\` 下 .md 数（R42，增量构建会静默漏打包），判据见下面代码块 |
| 跑 gate（含 `ColorSyntheticChecks` 16 条 + `HelpManualCheck` HM0~HM17） | 手册改动红线在 HM12（`NN-dev-` 命名）/ HM12b（不许另立子目录挤掉用户页）/ HM13（本条目 L 档：开发章节 ≥300 行）/ HM16（档位表覆盖）/ HM17（骨架台账） |
| 改了 `Services\Core.Halcon` 时 Debug 与 Release 都重建 | `pack.ps1` 打的是 Release（R55） |
| 动了颜色内核（`ColorVocabulary` / `ColorThresholds` / `ColorSampler` / `ColorMarks`） | 「区域颜色检查」与「颜色序列检查」**同时受影响**，两条链都要跑；这是公共契约面，按 R12 走 architect 评审 + reviewer 审查 |
| 大改动后重建 CodeGraph 索引 | 不重建 = 影响面查询静默少项（R28）。XAML 与生成器相关一律 grep（R29） |

```powershell
# 只看构建退出码不算数——增量构建会静默漏打包（R42）
tr '\0' '\n' < Modules\Plugin.ColorCheck.dll | grep -a "\.Help\."
```
