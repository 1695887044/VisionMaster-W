# 几何查找 · 开发：内部结构、宿主契约与不变量

> 分类：几何工具 ｜ 来源：插件 Plugin.GeometryFinder ｜ 读者：开发

本页只讲**这个插件怎么组织的、和宿主签了哪些约、改它要注意什么**。端口怎么用、参数怎么调，在用户册的
「快速上手」「端口与参数」「排错」三页，这里不重复。

## 一、类身份：两文件的极简插件

```
Plugins\Plugin.GeometryFinder\
  GeometryFinderPlugin.cs          254 行  ← 端口声明 + RunAlgorithm + Publish
  Services\GeometryHService.cs     192 行  ← HALCON 封装（internal static）
  Models\GeometryFinderEnums.cs           ← GeometryFindKind / FitAlgorithmKind
```

`GeometryFinderPlugin` 只继承 `VisionPluginBase`，**不实现任何视图/端口动态化接口**：
`[Display]` 实值是 `Name="几何查找"`、`GroupName="几何工具"`、
`Description="在 ROI 内亚像素找边并拟合，输出可复用的几何基元（直线/圆/椭圆）；供几何计算等下游消费"`、
`ShortName="\uf140"`（私有区字形，靠 FontAwesome 字体渲染，换字体就是方框）；另有 `[ParallelSafe]`（:28）。

十个输入端口 + 八个输出端口全部是 `public` 属性初始化器，基类 `EnsurePortsDiscovered()` 反射收集
（`VisionPluginBase.cs:359` `_outputs.TryAdd(port.Name, port)`）——**端口名就是属性名**，
所以重命名属性等于改端口名，方案里的连线当场断（AGENTS.md R33~R35 同一口径）。

## 二、宿主给的执行骨架（本插件不写的部分）

`VisionPluginBase.Execute()`（`VisionPluginBase.cs:94-119`）在 `RunAlgorithm` 之前做三件事：
预置 `Success=true`、清空 `ErrorMessage`、`if (AutoDisposeRoundOutputs) DisposePreviousRoundOutputs()`。
本插件**两个策略属性都不覆写**（`AutoDisposeRoundOutputs` 默认 true :134、`RethrowOnException` 默认 false :126），
所以未捕获异常一律被基类转成 `Fail($"{ex.GetType().Name}: {ex.Message}")` + `Logger.Error`，流程继续跑。

配置生命周期同样全交给基类：本插件 `Initialize()`（:226）与 `Dispose()`（:227）都是**空重写**。
`Initialize()` 空 = 不需要恢复私有态，基类 `Initialize(stepData)` → `ApplyConfigValues`（:234）
把 `InputValues` 灌进未链接的端口，链接端口 `IsLinked` 跳过——这就是"参数能同时被固定值/变量/连线三种来源填"的机制。
`OnConfirm(stepData)`（:275）把未链接端口 `SetInputValue(端口名, 端口值)`、已链接端口 `RemoveInputValue`。
`.vms` 里枚举端口存的是**数值**（演示方案实测 `"Kind": 1` / `"Kind": 0`），回读时由 `ValueConverter` 把 Int64 还原成枚举。

**没有 `[StepConfig]`**：本插件所有参数都是输入端口，一个都不进配置属性通道。
因此参数面板由宿主的端口视图渲染，枚举候选走
`LinkableValueEditor.xaml.cs:227-230` → `StepConfigOption.ForEnum(枚举类型, 成员名)`，
它**只读成员上的 `[Display(Name)]`**（`StepConfigOptionSource.cs:60-63`）：`GeometryFindKind` /
`FitAlgorithmKind` 每个成员都标了中文名，所以下拉是中文、写回的值仍是成员名/数值。
另一条路径是属性网格的 `EnumGenerator`（优先读 `[Description]`、没标就回退成员名），
本插件没有属性网格所以走不到——`Plugin.PreProcessing` 才在那条路上。

## 三、一次 `RunAlgorithm` 的实际流程（行号即源码）

```
:101-106  轮首重置 6 个非 IDisposable 输出（Geometry/IsFound/Row/Col/Phi/Radius）
:108-113  img 为 null 或未初始化 → Fail("几何查找：未接入图像（SrcImage 为空）") → return
:115-120  读 Kind / FitAlgorithm，夹取 Sigma≥0.4、Low≥1、High≥Low+1、MinContourLength≥1
:127-131  MaskRegion 有效 → reduce_domain(img, region) 到局部 reduced，source 改指 reduced
:133      edges_sub_pix(source, "canny", sigma, low, high)
:134      取「长度 ≥ minLen 中最长的一条」轮廓（其余就地 Dispose）
:136-145  没找到 → Publish 红字"未找到轮廓"；FailOnNotFound 为真才 Fail → return
:148      Contours.Value = new HXLDCont(best)   ← 独立句柄，源 best 仍由本方法释放
:154-204  按 Kind 分派 FitLine / FitCircle / FitEllipse；default → Fail("未知查找类型")
:206-213  拟合返回 false → Publish 红字"未拟合到目标"；FailOnNotFound 为真才 Fail
:215-216  IsFound=true → Publish（绿字/绿线 + 信息行）
:218-223  finally：edges / best / reduced 三个局部句柄释放
```

夹取常量集中在 :117-120 这四行，**用户页所有"填 0 会怎样"的口径都出自这里**；改这里要同步用户页。

## 四、HALCON 算子层（`Services\GeometryHService.cs`）

| 方法 | 行 | HALCON 调用（字面量抄源码） | 口径 |
| --- | --- | --- | --- |
| `EdgeContours` | :16-20 | `edges_sub_pix(..., "canny", sigma, low, high)` | 算法串**写死 canny**，与 `FitAlgorithm` 无关 |
| `LongestContour` | :23-48 | `count_obj` / `select_obj(i=1..n)` / `length_xld` | 逐条量长，不合格立刻 Dispose；无满足者返回 `null` |
| `TryFitLine` | :51-74 | `fit_line_contour_xld(c, alg, -1, 0, 5, 2.0, ...)` | 取拟合出的两端点 → 中点 + `atan2(dr, dc)` |
| `TryFitCircle` | :108-139 | `get_contour_xld` → 首点补尾 → `gen_contour_polygon_xld` → `fit_circle_contour_xld(c, alg, -1, 0, 0, 5, 2.0, ...)` | 点少 `rows.Length < 3` 直接 false；`maxClosureDist=0` 所以必须人工闭合 |
| `TryFitEllipse` | :142-176 | `fit_ellipse_contour_xld(c, "fitzgibbon", -1, 0, 0, 200, 3, 2.0, ...)` | 点少 `< 5` 直接 false；**算法串写死，`alg` 形参不参与**（见 B1） |
| `CirclePoints` / `EllipsePoints` | :77-105 | 无（纯托管） | 72 点采样，只给标注折线用，不进 `Geometry` |
| `LineAlgorithm` / `CircleAlgorithm` | :178-190 | — | Regression→`regression`/`geometric`，Huber→`huber`/`geohuber`，其余→`tukey`/`geotukey` |

异常口径：三个 `TryFit*` 只 `catch (HOperatorException)` → `return false`。
**别的异常（例如上游图已被释放）照样上抛**，由基类 `Execute` 转 `Fail("ObjectDisposedException: ...")` 形态。
`GeometryParam` 的 `Row/Col/Phi/Radius/Radius2` 就是 `TryFit*` 的 `out` 参数装配的，中间没有第二套换算。

## 五、几何基元契约（跨插件的一等数据）

`GeometryParam` 住在契约层 `Shard\Core.Interfaces\Geometry\GeometryParam.cs`，本插件与「几何计算」共用同一类型：

- **单类 + `Kind` 分派**，不做继承体系：JSON 往返安全、连线类型检查只认一个类型。
  代价是"线端口能连到圆输入"编译期不红，由消费方按 `Kind` 运行期校验。
- **不可变**：属性全是 `init`，只能走 `Point/Line/Circle/Ellipse/Contour` 工厂；下游要变换就产新对象。
- **`Line` / `Ellipse` 工厂内部调 `NormalizePhi`**（:65/:71），`Point` / `Circle` 不调；
  `NormalizePhi(deg) = deg % 180` 后负数补 180（:80-85），所以方向角永远落在 [0,180)。
- **`atan2(dr, dc)` 以 column 轴为 0°**（`GeometryHService.cs:67`）：水平线 φ=0、竖线 φ=90。
  这与卡尺 Angle（以水平线为 0 的偏移角）口径差 90°，不是 bug，是两套定义。
- **不乘像素当量**、图像像素系 (Row, Col)：本插件没有标定输入，输出是纯像素量。

## 六、内存所有权（谁 new、谁 dispose）

| 对象 | 创建处 | 释放者 |
| --- | --- | --- |
| `reduced`（reduce_domain 产物） | `RunAlgorithm:129` | 本方法 `finally:222` |
| `edges`（edges_sub_pix 产物） | :133 | 本方法 `finally:220` |
| `best`（最长轮廓） | :134 | 本方法 `finally:221`（`LongestContour` 内被替换掉的候选轮廓各自立刻 Dispose） |
| `Contours` 端口的 `HXLDCont` | :148（`new HXLDCont(best)` 是独立句柄） | 基类轮首 `DisposePreviousRoundOutputs`（`port.Value is IDisposable`） |
| `Geometry` / `IsFound` / `Row` / `Col` / `Phi` / `Radius` | 每轮 | 基类**不管**（非 IDisposable）→ 靠 :101-106 轮首手动重置 |
| `closed`（圆/椭圆的闭合临时轮廓） | `TryFitCircle:123` / `TryFitEllipse:156` | 各自 `finally` |
| 发布用的图 | 无新建，直接传 `img` 引用 | 归上游；`PublishPreview` 只发引用（`PluginPreviewExtensions.cs:60-84`），需要留帧的宿主在发布线程上同步拷走 |

**现状事实（不是设计意图）**：`public override void Dispose() { }`（:227）**不调 `base.Dispose()`**。
基类 `VisionPluginBase.Dispose()`（:74-86）会遍历 `_outputs` 释放 `IDisposable` 的端口值，
这条路径因此永远走不到。后果：**最后一轮**的 `Contours`（`HXLD`）不在插件释放路径上——
轮首回收只覆盖"下一轮开始之前"，实例被替换或方案关闭时那份残留句柄没人收。
要修就一行：`Dispose()` 里改成 `base.Dispose()`（改完必须同步本节与用户页无关注处；本手册不改源码，只记事实）。

## 七、不变量与陷阱清单

守门断言的**真实分布**（逐条回源码抄）：
`FlowCanvasChecks\GeometryChecks.cs` = 分节 `[GEO] 几何工具：GeometryMath 纯函数 + GeometryParam 契约`，22 条 `Check(...)`，
其中 `finder.Kind` 那条（:120 `枚举端口接受下拉回写的值（Kind → 圆）`）直接实例化本插件。
`FlowCanvasChecks\GeoDemoCheck.cs` = 分节 `[GEODEMO] 演示方案：加载 → 编译 → 执行（几何查找 + 几何计算）`，13 条，端到端跑 `解决方案\几何工具演示.vms`。
`FlowCanvasChecks\ParallelExecutionChecks.cs:266` = `标注 [ParallelSafe] 的 18 个插件 DLL 全部含特性元数据（漏重建=门禁误报）`，清单含本插件。
**HALCON 找边/拟合这一层没有断言**——`GeometryChecks.cs` 头注写明"HALCON 找边/拟合（GeometryFinder 的算子层）不在此断言——
它依赖真算子，另由现场/探针验"，所以第四节的全部口径**无断言，靠人工核对**。

### A 组：违反会当场红或抛

**A1 「未接入图像」是唯一无条件失败出口**（:109-113）。另外两处 `Fail`（:143 未找到轮廓、:211 拟合失败）都被
`FailOnNotFound` 罩住，:202「未知查找类型」只在枚举加了成员又忘了 `case` 时才可能命中。
守门：`GeoDemoCheck` 的 `全部 9 步执行成功（无失败/未执行）`——本插件两步走的是成功路径。

**A2 轮首必须重置 6 个输出**。`double`/`bool`/`GeometryParam` 都不是 `IDisposable`，基类轮首回收碰不到它们。
症状：本轮失败而下游读到**上一轮的基元**，坐标"时对时错"。守门：`GeoDemoCheck` 的 `直接调用：横线/竖线各拟合出一条线（Phi≈0° / 90°）`
只覆盖成功路径；**失败轮残值这条无断言，靠人工核对**。

**A3 局部句柄只能在 `finally` 成对释放**（:218-223）。`Contours.Value = new HXLDCont(best)` 之后 `best` 仍要被释放，
两者是独立句柄。症状：漏一个 → 每轮泄漏一组轮廓，500 轮现场就是几百 MB。

**A4 枚举端口值的还原链路**。`ApplyConfigValues` → `port.Value = kvp.Value`（object），JSON 里是 Int64；
`GeometryChecks.cs:110/113/120` 三条钉住 `ValueConverter` 的字符串→枚举、数值→枚举、以及"下拉回写的值真的进到 `Kind`"。
症状：转换器漏一种形态 → 下拉改了没效果（试运行对、重开方案错）。

### B 组：违反后静默出错

**B1 `FitAlgorithm` 只对直线、圆生效**。椭圆分支把算法串写死成 `"fitzgibbon"`（`GeometryHService.cs:160`），
`TryFitEllipse` 收了 `alg` 形参但**一次都没用它**。症状：椭圆场景改"最小二乘 / Huber / Tukey"三者结果一模一样，
界面看不出任何异常。现状照实记录，不在本手册内修源码。

**B2 边缘算法与拟合算法是两件事**。`edges_sub_pix` 的 `"canny"` 写死（:18），`Sigma/Low/High` 才影响找边；
`FitAlgorithm` 只影响拟合的鲁棒降权。症状：图噪声大时去调 `FitAlgorithm`，纹丝不动，其实该调 `Sigma`。

**B3 只取最长的一条轮廓**。`LongestContour` 返回单条，其余全部丢弃（:35-44）。
症状：同框里两条边等长时结果取决于 `select_obj` 顺序，看起来"随机挑一条"；要多条必须换设计，不是调参能解决。

**B4 圆/椭圆靠"人工闭合"过 `maxClosureDist=0`**。两处都把首点补到末尾再 `gen_contour_polygon_xld`（:121-123 / :154-156）。
症状：拿一条明显不闭合的弧去拟合圆，会得到一个巨大半径的"成功"结果（只判 `radius > 0` 且 `IsFinite`）。

**B5 `MaskRegion` 不搬坐标系**：只做 `reduce_domain`，输出的 Row/Col 仍是**原图**像素系。
症状：接 ROI 插件的 `Domain_名`（裁剪图域）当 MaskRegion 时，坐标对齐靠上游，本插件不参与。

**B6 显示层失败只 Warn**（:248-251）：`PublishPreview` 抛异常不影响算法结果，但**必须留一句 Warn**——
静默吞掉的话"上屏没标注"与"根本没发布"在日志里长得一样。窗口号口径：`<=0` 直接不发、`>9` 夹到 9（:245-246），
且**窗口号直传不 +1**（`PluginPreviewExtensions.cs:11-13` 的历史坑）。

**B7 `FailOnNotFound=false` 是"探测模式"**：此时未找到/拟合失败都只红字上图、`Success` 保持 true、
`IsFound=false`。症状：下游「几何计算」拿到 `Geometry=null` 会走它自己的"A 为空"失败；
用这个开关做"有没有都行"的判断，应该连 `IsFound` 一起连出去。

**B8 `Dispose()` 不调 base**（见第六节末）。症状不是崩，是**最后残留一份非托管轮廓**，
增量测试完全看不出来，只有长时间换方案/反复实例化才累计。

## 八、改这个插件之后的必做动作

| 动作 | 判据 |
| --- | --- |
| 重建插件工程（只重建宿主跑的是旧 DLL） | `Modules\Plugin.GeometryFinder.dll` 时间戳跟着变（AGENTS.md R16） |
| 核 DLL 真的带了 `Help` 资源 | 资源名数 == `Help\` 下 .md 数；增量构建会静默漏打包（R42），判据见下方代码块 |
| 跑 gate | `GeometryChecks`（22 条）+ `GeoDemoCheck`（13 条）+ `HelpManualCheck`；手册红线在 HM12（`NN-dev-` 命名）/ HM12b（不许另立子目录）/ HM13（本档 S 级：开发页 1 页、合计 ≥100 行） |
| 改端口名/增删端口 | 连 `解决方案\几何工具演示.vms` 一起看，`GeoDemoCheck` 的 `流程编译通过（端口存在、连线类型匹配）` 会先红 |
| 加 `GeometryFindKind` 成员 | 必须同时补 `RunAlgorithm` 的 `case` 与 `KindLabel`，否则命中 :202 `未知查找类型` |

```powershell
# 只看构建退出码不算数——增量构建会静默漏打包（R42）
tr '\0' '\n' < Modules\Plugin.GeometryFinder.dll | grep -a "\.Help\."
```
