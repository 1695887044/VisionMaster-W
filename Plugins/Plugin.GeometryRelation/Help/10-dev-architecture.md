# 几何计算 · 开发：内部结构与数据流

> 分类：几何工具 ｜ 来源：插件 Plugin.GeometryRelation ｜ 读者：开发

本页讲**这个插件怎么组织、一次运算的数据怎么流、数学层公式长什么样**。
用户口径（端口怎么用、判定怎么算）在用户册四页，这里不重复；
宿主契约见「宿主契约与移植清单」，改它要注意什么见「不变量与陷阱清单」。

## 一、三个文件，职责一刀切

```
Plugins\Plugin.GeometryRelation\
  GeometryRelationPlugin.cs           455 行  ← 端口声明 + 模式分派 + 判定 + 显示
  Services\GeometryMath.cs            166 行  ← 纯几何公式（public static，可被断言直接调）
  Models\GeometryRelationEnums.cs             ← GeometryOp / LineRelationKind / ConstructKind
```

切分的唯一动机：**公式层能被断言完整覆盖**。`GeometryMath` 不依赖 HALCON、不依赖端口、不依赖宿主，
所以 `FlowCanvasChecks\GeometryChecks.cs` 可以拿合成已知量逐条对解析解（误差 1e-9）。
一旦有人"顺手"在 `GeometryMath` 里加一句读端口或读全局，这层断言立刻失效——公式改错就再也抓不住了。

类身份：`[Display(Name="几何计算", GroupName="几何工具", ShortName="\uf542")]`（:25-30，`ShortName` 是
FontAwesome 私有区字形，换字体就是方框）+ `[ParallelSafe]`（:31）+ 继承 `VisionPluginBase`，
**不实现任何视图接口**。

**参数全走输入端口，一个 `[StepConfig]` 都没有**——这不是风格选择，是宿主决定的：
不实现 `IPluginCustomViewProvider` 的插件由 `AutoPortConfigView` 兜底渲染，而它**只渲染输入端口**，
标成 `[StepConfig]` 的参数在那里没有形态可渲染，等于没有入口（类注释 :18-20 记的就是这个历史坑：
「轴运动」的绝对/相对切换在界面上一直改不了）。

## 二、一次 `RunAlgorithm`（行号即源码 :101-140）

```
:105-107  轮首重置三个输出：Value=0 / Geometry=null / IsOk=false
:109-112  一次性把 Op / A / B / C 的 ActualValue 读进局部变量（后面全用局部量，不再回读端口）
:114-118  A 为空 → Fail("几何计算：输入 A 为空（…）") → return
:120-124  TryResolveScale：当量口径不合法 → Fail(当量错误文案) → return
:126-136  按 Op 分派七个私有方法；default → Fail("几何计算：未知模式 <Op>")
:138-139  if (Success.Value is true) Publish(a, b, op)      ← 失败轮不推图
```

分派表（每格都是"校验 → 公式 → 写哪几个输出"）：

| 方法 | 行 | 输入校验 | 用到的公式 | 写哪些输出 | 判定 |
| --- | --- | --- | --- | --- | --- |
| `DoDistance` | :147 | `B` 非空；Kind 组合限 点-点 / 点-线 / 线-线 | `DistancePointPoint` / `DistancePointLine`；线-线先过 `AreParallel` | `Value`（×scale） | `Judge(value)` |
| `DoAngle` | :179 | `B` 非空；两个都必须是 Line | `AngleBetweenLines` | `Value`（度，不乘 scale） | `Judge(value)` |
| `DoIntersect` | :192 | `B` 非空；线-线 / 线-圆 / 圆-圆（圆-线会交换参数复用同一函数） | `TryIntersectLines` / `TryIntersectLineCircle` / `TryIntersectCircles` | `Geometry`（`Point`） | **不判** |
| `DoPerpendicularFoot` | :227 | `B` 非空；点+线（顺序自动纠正） | `PerpendicularFoot` + `DistancePointLine` | `Geometry` + `Value`（×scale） | **不判** |
| `DoLineRelation` | :241 | `B` 非空；两个都必须是 Line | `AngleBetweenLines` + `AreParallel` / `ArePerpendicular` | `Value`（实测夹角）+ `IsOk` | 走容差，**不走规格三项** |
| `DoCircleRelation` | :258 | `B` 非空；两个都必须是 Circle | `DistancePointPoint`（圆心距） | `Value`（×scale） | `Judge(value)` |
| `DoConstruct` | :271 | 构线要 A/B 是点；构圆要 A/B/C 是点 | `LineThroughTwoPoints` / `TryCircleThroughThreePoints` | `Geometry`（Line 或 Circle） | **不判**，也不乘 scale |

**"不判"就恒 false**：`IsOk` 只在轮首被写成 false（:107），这三条分支再没有第二次赋值。
显示层的上色读的是 `IsOk`，于是求交/垂足/构造即使成功也是黄字，且求交连数值都没写、
`Text = $"{OpLabel(op)} = {Value.Value}"` 会拼出 `求交 = 0`。这是现状（用户页已如实写明），
要改就得决定"这三种模式的颜色语义到底是什么"，不是加一行 `IsOk = true` 那么轻。

## 三、数学层（`Services\GeometryMath.cs`）

| 成员 | 行 | 实现要点 |
| --- | --- | --- |
| `Direction(phiDeg)` | :20-24 | `(dRow, dCol) = (sin φ, cos φ)`：**φ=0 沿 +Col**、φ=90 沿 +Row。全站的角度口径都在这一行 |
| `NormalizePhi` | :27 | 直接转调 `GeometryParam.NormalizePhi`，不留第二份实现 |
| `DistancePointPoint` | :30 | 欧氏距离 |
| `DistancePointLine` | :38 | `\|v × d\| = \|vr·dc − vc·dr\|`；`d` 是单位向量，所以不用再除模长 |
| `AngleBetweenLines` | :48 | `\|φ1 − φ2\| % 180`，大于 90 取 `180 − d` → **无向夹角 [0,90]** |
| `AreParallel` / `ArePerpendicular` | :55 / :59 | 夹角 ≤ 容差 / 与 90° 的差 ≤ 容差 |
| `TryIntersectLines` | :63 | 参数式求交；只有方向完全共线（`\|det\| < 1e-15`）→ false；近平行按 HALCON 给远交点 |
| `TryIntersectLineCircle` | :82 | 一元二次，`disc < 0` → false；取**较小 t** 的那个根；`count = sqrt<1e-9 ? 1 : 2`（count 不回传上游） |
| `TryIntersectCircles` | :105 | 同心 `d<1e-9` / 相离（超半径和 `1e-12`）/ 内含（差半径 `1e-12`）三类先拒；取 `+h·perp` 那一个解（**`A`/`B` 对调会翻到另一侧**，这就是用户页"对调取另一解"的出处） |
| `PerpendicularFoot` | :130 | 投影长度 `t = vr·dr + vc·dc`，不夹取 → 落在延长线上照样返回 |
| `LineThroughTwoPoints` | :140 | 锚点=中点，`atan2(b.Row−a.Row, b.Col−a.Col)`，经 `GeometryParam.Line` 工厂归一化 |
| `TryCircleThroughThreePoints` | :149 | 行列式分母 `\|d\| < 1e-9` → false（共线） |
| `PointResidual` / `EllipseDistance` | :160 / :206 | 线=点线距、圆=\|d−R\|、椭圆=**最短欧氏距**（法向 Newton + 16 点兜底，对齐 HALCON `distance_pc`） |

`1e-9` / `1e-15` 这一族阈值**散落在各函数内部**，没有统一常量：改判定严格度时要把这几处一起看，
只改插件侧的 `AngleTolerance` 不会动到公式层的退化保护。

## 四、判定与当量

`Judge(value)`（:305-312）：`value >= std − lo − 1e-9 && value <= std + up + 1e-9`。
**只有 `DoDistance` / `DoAngle` / `DoCircleRelation` 调它**（:176 / :189 / :268）。
默认 0/0/0 = 零容忍，所以"没配规格就全 NG"是设计，不是缺陷。

`TryResolveScale`（:319-340）三条口径，与卡尺 `GetEffectivePixelSizeMm` 同规则：

```
MmPerPixel.LinkedSource != null（已连线）
    值 NaN / Inf / ≤0 → 返回 false + 明确错误文案（绝不静默回退手填值）
    否则 scale = Math.Clamp(v, 1e-9, 1e6)          ← 防极端值
未连线
    手填值 NaN 或 ≤0 → scale = 1.0（= 输出像素）
    否则 scale = 手填值
```

**`LinkedSource != null` 是"有没有连线"的判据，不是"值是否非零"**——所以把当量端口填 0 再连线，
行为与不连线完全不同（前者必失败）。这一条决定了"忘重标会不会静默跑偏"，改动前务必保住。

## 五、显示层（:347-439）

`Publish` 只在成功轮被叫，`SrcImage` 为空/未初始化或窗口 ≤ 0 直接 return，窗口 > 9 夹到 9。
`AddGeometry` 按 Kind 出四种标注：点固定文字 `P`；线**没有端点**，以锚点为中心沿方向画一段，
长度取 `sqrt(w²+h²)/4`（`GetImageSize` 现算，:389-390）；圆与椭圆各自 72 点采样成折线（`CirclePoints` / `EllipsePoints`，
纯托管、与「几何查找」里的同名实现是**两份独立代码**）。
最后一条文字标注 `Text = $"{OpLabel(op)} = {Value.Value}"`，颜色 `IsOk.TypedValue ? "green" : "yellow"`。

`try { PublishPreview(...) } catch { }`（:373-374）——**这里连一句 Warn 都不留**。
对照「几何查找」的 `Publish`：那里注释明确"静默吞掉的话，图上了屏却没标注与根本没发布在日志里长得一模一样"，
所以写了 `Logger.Warn`。本插件是另一种写法（类注释与用户页都按现状记）；
要对齐就在 catch 里补 `context.Logger?.Warn(...)`，但 `Publish` 现在拿不到 `context`，得先改签名。

## 六、生命周期与内存

13 个输入端口 + 3 个输出端口全部由基类反射收集，**端口名 = 属性名**（改属性名等于改端口名，方案里的连线当场断）。
`Initialize()`（:142）与 `Dispose()`（:143）都是**空重写**：
不需要恢复私有态，也不需要收尾非托管对象。

输出端口里**没有 `IDisposable` 类型**（`double` / `GeometryParam` / `bool`），所以基类轮首的
`DisposePreviousRoundOutputs()` 对本插件是空转（只回收 `port.Value is IDisposable` 的值），
`base.Dispose()` 同样无值可收。因此"不调 `base.Dispose()`"在这里**不产生泄漏**，
但它把三个输出的清零责任完全压到 :105-107 这三行——**新增输出端口时必须同步这三行**，
否则那个端口的值会跨轮残留（AGENTS.md R33~R35 同一口径）。
`SrcImage` 只是引用上游图，本插件从不 `new HImage`，所以没有任何自建非托管对象。
