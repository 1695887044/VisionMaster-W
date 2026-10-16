# 结果显示 · 架构与数据流

> 分类：常用工具 ｜ 来源：插件 Plugin.ResultDisplay ｜ 读者：开发

工程：`Plugins\Plugin.ResultDisplay`（工具箱条目「结果显示」，类 `ResultDisplayPlugin`，分类「常用工具」）。
它**不是**一个"自己画"的插件：它只做"决定画什么"，"怎么画"全在共享件 `AnnotationBaker` 里。
这层分工是 AGENTS.md §11（R36/R37/R39）的全部内容，也是本条目最值得先读的一页。

## 三层职责（谁开窗口 = 唯一答案）

| 层 | 文件 | 干什么 | 明确**不**干什么 |
| --- | --- | --- | --- |
| 插件壳 | `ResultDisplayPlugin.cs` | 配置项、动态端口生命周期、`RunAlgorithm` 编排、发布显示 | **零 `OpenWindow`、零 `DumpWindowImage`、零 `SetPart`**（静态扫描 ④c 钉死） |
| 排版/分派 | `Services\DisplayDraw.cs`（`internal static`） | 值类型 → `BakePlan` 的元素；行主口径几何换算；九宫格排版；钳制与上限；判定换色；信息栏行 | 不碰窗口、不量文字宽度（那是烘焙件的能力） |
| 烘焙件 | `Services\Core.Halcon\Helpers\AnnotationBaker.cs`（**共享**，不属于本工程） | 开窗、`SetPart`、`SetColor`/`SetLineWidth`/`SetFont`、`DispObj`/`DispText`、`DumpWindowImage`、回读成新 `HImage` | 不认识"结果项"，只认识 `BakePlan` |

**全仓 `OpenWindow` 只许出现在 `AnnotationBaker.cs` 一个文件里**，加上一份永久豁免
`Plugins\Plugin.ImageScript\HDevDisplayBackend.cs`（它按 `dev_*` 算子逐条驱动、窗口状态 `[ThreadStatic]`，
"画什么"在脚本跑完之前根本不存在，`BakePlan` 表达不了它）。豁免清单**只许变短、不许变长**。
守门：静态扫描 ④（全仓只有一处开窗 + 豁免）与 ④b（豁免清单没有过期项——迁完的文件必须从清单里删掉）。

## 数据流：一轮 `RunAlgorithm`

```
复位：DisplayedCount=0、IsDisplayed=false、AnnotatedImage=null
viewIndex = DisplayViewIndex

① 底图 SrcImage.ActualValue
   null 或未初始化        → Warn「底图（SrcImage）为空，本轮不发布显示」return（Success 仍 true）
   GetImageSize 抛        → Warn「读取底图尺寸失败，本轮不发布显示：…」return

② draw = DisplayDraw.Build(CollectItems(), w, h,
              ShowTextOnImage, JudgeItemLabel, NgValue, MaxDrawElements,
              TextAnchor, TextMargin, TextScale)
   返回 DisplayDrawResult { Plan, InfoRows, Warnings, TempObjects, DisplayedCount }
   抛 → Warn「组装标注内容失败，本轮不发布显示：…」return
   foreach draw.Warnings → Warn（**该吼的必须吼**：这两层零空 catch，扫描②）

③ annotated = _baker.Bake(src, draw.Plan, warnSink)
   finally { draw.DisposeTemps(); }     ← 只为**本轮新生成**的 XLD；上游给的句柄不在里面

④ annotated == null → Warn「烧录失败，降级为发布原底图（不带标注）」→ Publish(src, …) → return
   AnnotatedImage.Value = annotated
   viewIndex <= 0 → IsDisplayed=false，return（**不抢占窗口但产物照给下游**）
   Publish(annotated, viewIndex, draw.InfoRows, out shown) → IsDisplayed = shown
```

`CollectItems()` 遍历的是**启用项、按表内顺序**，取值走 `GetActualValue()`（有连线取上游值、没连线取手填值）。
★ 一个容易写错的点：基类 `InputValues` 里**也含 `[StepConfig]` 配置项**，所以"收集上游值"不能按 `InputValues.Keys` 遍历。

## 状态有几份，谁是真源

- **真源 = `Items`（`ObservableCollection<DisplayItemDef>`）**，随 `[StepConfig]` 以 JSON 往返进 `.vms`。
  它是结构型 setter（源生成器豁免项，手写档）：`Unhook/Hook` 成对 + `RebuildDynamicInputs()` + `RefreshJudgeItemLabels()`。
- **派生物 1：动态输入端口**（`r1`/`r2`…）。`PortsVersion` 用来让外部知道端口表被重建过。
- **派生物 2：判定项候选 `JudgeItemLabels`**（首项空串 = 不判定；中间 = 启用项 `Label`；末项补当前值）。
  `RefreshJudgeItemLabels()` **只在真的变了才通知**——改 `Label` 每敲一个字都会走到这里，
  无差别通知 = 无差别换 `ItemsSource` = 可编辑下拉每次按键丢一次文本（R35）。
  通知前记住 `JudgeItemLabel`、通知后顶回去。
- **本轮临时态**：`DisplayDrawResult`（含 `TempObjects`）与 baker 的常驻窗口。前者一轮一命、后者跨轮。

## `ApplyConfigValues` 的三段顺序（缺一不可）

```
① base.ApplyConfigValues(stepData)   —— 灌 [StepConfig]（含 Items）
② 按 Items 重建动态端口              —— **必须先于** FlowCompiler 的 LinkPorts
③ RefillDynamicConstants(...)        —— 补灌动态口的手填常量
```

② 为什么必须早：连线编译在 `Engine\FlowCompiler.cs` 里按端口名找口，口还没建就全线断线。
③ 为什么必须补：基类遍历 `InputValues` 灌值时（`VisionPluginBase.cs`）**动态口还没建**，
手填常量会静默丢——这条被 `A6【§6.4-2 那个坑】往返后**手填常量不丢**` 钉住。

`OnConfirm` 负责把结果项表写回 `InputValues`（`[StepConfig]` 快照），
对应 `A6 OnConfirm 把结果项表写进了 InputValues（[StepConfig] 快照）` + `A6 往返后动态端口存在（r1 出厂项 + r2 新增项）`。

## `BakePlan` 的三个桶与固定层序

```csharp
plan.Shapes   // BakeShape(Kind, double[] P, Color, LineWidth)   Kind ∈ Line/Circle/Ellipse/Cross
plan.Objects  // BakeObj(HObject Obj, Color, LineWidth, Draw)     HRegion / HXLD / 生成的轮廓
plan.Texts    // BakeText(Text, Row, Col, Color, FontSize, Box) + init HAlign
plan.ElementCount = Shapes + Objects + Texts   // 上限与"不占预算"的说明行都按这个口径
```

**绘制顺序固定 `Shapes → Objects → Texts`**：要改层序就改 `Build` 里的入列顺序，**不要改 baker**
（顺序不固定 = 那些窗口级状态纪律没法只写一遍）。

类型分派规则（`DisplayDraw.Dispatch`）：
- 数值/文本/枚举/布尔/时间 → `Texts`（`bool` 写成 `OK`/`NG`，白色底框）；
- `GeometryParam` → 点=十字、线=贯穿直线、圆=Circle + 一个圆心十字、椭圆=Ellipse（`P` 长 5、`P[2]` = −Φ、度数）、
  轮廓 → 生成 XLD 进 `Objects` 且登记 `TempObjects`；**一律 cyan**，标签进 `Texts` 但**不给底框**；
- `HRegion` / `HXLD` → `Objects`（判型顺序 `HImage → GeometryParam → HRegion → HXLD → HObject`），
  用的是**上游同一个句柄**、所有权不接管；
- `HImage` → **拒绝**：告警 + 图上一行提示；
- `HTuple` → 逐元素格式化，最多 32 个（`MaxTupleElements`），超出 `…(+n)`；
- 轮廓点集超过 `MaxContourPoints = 2000` 等间隔抽稀（保留首末点），`HXLD`/`HRegion` **不抽稀**。

## 文字位置：水平归烘焙件、垂直归调用方（R39）

- 水平：`BakeText.HAlign`（`Left`/`Center`/`Right`，`init` 属性，**默认 `Left` = 历史行为**）。
  `Col` 的含义随它变（左边缘/中心/右边缘），换算在 baker 内做并**夹回 `[0, w-1]`**；
  宽度用 `GetStringExtents` **实测**（本仓 HALCON 没有 `GetTextExtents`），且必须排在 `TrySetFont` **之后**——
  字号是窗口级状态，没设就量，量到的是上一号字体的宽。量不到就退回左对齐 + 一条 Warn（宁可贴边也不许丢字）。
  **调用方不许凭字符数估宽**（插件够不到窗口，估必歪）。
- 垂直：留给调用方。块高 = 行数 × 行距，**行数只有攒完 plan 才知道**，baker 看到的是一堆独立 `BakeText`、
  不知道"这一堆是不是一组"。所以 `DisplayDraw` 自己算带的起点/末行底边/整块居中。

## 内存所有权（谁 new 谁 Dispose）

| 对象 | 谁创建 | 谁释放 |
| --- | --- | --- |
| 底图 `src` | 上游 | **插件不碰**（扫描①【禁透传口】禁止把 `SrcImage` 的值赋给任何端口；`C1【禁透传口】底图句柄在烧录后仍然可用`） |
| 产物 `annotated` | baker（`DumpWindowImage` 之后 `new HImage(shot)`，`shot` 是源对象**立刻 Dispose**） | 归 `AnnotatedImage` 端口，下游按框架口径用（`C1 发布的就是产物本身`：`event.Image` 与端口值同句柄） |
| 本轮为轮廓生成的 XLD | `DisplayDraw` | `DisplayDrawResult.TempObjects` → `DisposeTemps()`（`RunAlgorithm` 的 `finally`） |
| 上游给的 `HRegion`/`HXLD` | 上游 | **不进 `TempObjects`**（`A4 上游区域的所有权不被接管：不进 TempObjects，plan 里是**同一句柄**`） |
| baker 的常驻窗口 | baker | baker `Dispose()` 才关（`B7 Dispose 后再 Bake：返回 null、不抛、且有告警留痕`；`Dispose` 幂等） |

## 断言宿主

两个宿主，分工不同：
- `FlowCanvasChecks\ResultDisplayCheck.cs` —— 插件契约：**A1~A9**（发布/窗口号/值分派/钳制/上限/往返）、
  **C1~C5**（产物句柄与像素级烧录证据）、**U1~U9**（文字位置：默认值不变、九宫格行列、边距、字号、开关、像素反证、夹取）、
  外加 **16 条静态扫描 ①②③-a/b/c④④b④c⑤⑥⑥b⑦⑦b⑧⑧b⑧c**。
- `FlowCanvasChecks\AnnotationBakerChecks.cs` —— 共享件纪律：**B1~B8**（底图保真、`box`、窗口复用与串轮、
  uint2 死白、4 通道、坏元素、500 轮内存、三种对齐墨迹）。

⚠ 这两组是**互补**的：B 组证明 baker 正确，A/C/U 组证明插件"只攒 plan、不越界"。
迁走/改共享件时只看 B 组会漏掉插件侧的越界（扫描 ④c 就是为此存在）。
