# 工程纪律分册 · HALCON 画布交互

> 本册是 `AGENTS.md` §11 的 R36/R37/R39 与 §16 的**完整判据**（R36-R39 离屏烘焙、R51-R55 手势换绑/摘除容差/断言窗口/轴语义/双配置重建）；注入层只留指针与「最易踩」。
> 触发条件：改 `Services\Core.Halcon` 的 `HalconBase`/ROI 交互、改插件视图里的画布手势、写/改 `RoiInteractionChecks`、动 `AnnotationBaker`。
> 起因与取证见 `2026-10-22-工程纪律全集（原文归档）.md` §16。
> 与 §11 的边界：R53「画布交互断言只能用可见窗口」与 R37①「`OpenWindow` mode 只能 `'buffer'`」方向相反——**离屏烘焙走 buffer，交互断言走可见窗口**，两条不许互相套用。

## 11. 离屏烘焙件只许一份（R36/R37/R39）

| # | 判据 |
|---|---|
| R36 | 全仓 `OpenWindow` 只许在 `Services\Core.Halcon\Helpers\AnnotationBaker.cs`，外加**永久豁免** `Plugins\Plugin.ImageScript\HDevDisplayBackend.cs`（逐算子驱动、`[ThreadStatic]`、`BakePlan` 表达不了）。**豁免清单只许变短。** 用法：`new AnnotationBaker()` + 攒 `BakePlan` + `Bake(底图, plan)`。守门：`ResultDisplayCheck` ④/④b/④c。 |
| R37 | **六条 + 两条状态纪律只在 baker 里实现一遍**：① `OpenWindow` mode 只能 `'buffer'`（否则 #1305）；② 窗口按尺寸常驻复用、`Dispose()` 才关（反复开关 → #9302 死锁）；③ 每轮无条件 `SetPart(win,0,0,h-1,w-1)`（漏了底图缩左上角；复用会串轮污染）；④ 文字坐标只用 `"window"`；⑤ `DispText` 必须显式给 `box`（默认就是开的），`BakeText.Box` 是必填位置参数（⚠ 大写 `Box`），字号走窗口级 `SetFont`；⑥ `DumpWindowImage` 源对象立刻 `Dispose`。外加：整段 `lock` 罩"改状态→画→回读"；`SetLineWidth` 还原 1、`SetDraw` 还原 `"margin"`。绘制顺序固定 `Shapes → Objects → Texts`（改层序改 plan 顺序，不改 baker）。 |
| R39 | **文字宽度只有 baker 能量**：`BakeText.HAlign`（`Left`/`Center`/`Right`，默认 `Left`）表达贴边，换算在 baker 内做并夹回 `[0,w-1]`。`GetStringExtents` 必须排在 `TrySetFont` 之后；本仓 HALCON **没有** `GetTextExtents`。**垂直分量留给调用方**（块高 = 行数 × 行距）。守门：`AnnotationBakerChecks` B8 + `ResultDisplayCheck` U1~U9。 |

守门：`ResultDisplayCheck` ④/④b/④c、`AnnotationBakerChecks` B8 + `ResultDisplayCheck` U1~U9。

## 16. HALCON 画布交互（R51-R55）

| # | 判据 |
|---|---|
| R51 | **"未选中对象一次手势就拖得动"靠换绑后补发 down**：`HalconBase.HSmart_MouseDownForRoi` 换绑 `ActiveRoi` 后调 `ReplayMouseDownForFreshHandle`（`SendMouseDownEvent(row,col,1)`），**只在换绑分支**；失败静默退化为旧行为。**必然行为变更**：未选中对象内部拖 = 移动，必须用断言钉住。 |
| R52 | **摘除容差必须盖住手柄尺寸**：`RoiHitToleranceScreenPx = 14.0` **只作用于"是否摘除已选中对象"**；挑选其它对象保持无容差（否则相邻误选）。代价是"遮蔽带"，写进注释。 |
| R53 | **画布交互断言只能用可见窗口**：`buffer` 窗口上只有"点中对象内部"能命中，手柄与拖拽全失效。用**屏幕外** WPF 窗口（`Left=-4000`）+ STA + `Dispatcher.PushFrame` + **窗口级**主题字典。⚠ 与 R37① 方向相反。 |
| R54 | `rectangle2` 的 length1 沿 **column** 轴（phi=0），不是 row；`ellipse` 是 radius1 沿 column。记反会得"手柄命中失败"假结论。 |
| R55 | **改 `Services\Core.Halcon` 必须 Debug 与 Release 都重建**（`pack.ps1` 打 Release）；核 DLL 里有没有新符号，不看退出码。 |

守门：`RoiInteractionChecks` 8 条（进常规 gate）。

## 读法

1. 先看 `AGENTS.md` §16 的三条「最易踩」判断自己是否踩在这条域上。
2. 动手改画布手势或写交互断言前，逐条对照本表；R53 的"可见窗口"是**唯一**能证明手柄与拖拽行为的方式，`buffer` 窗口上的绿灯是假绿。
3. 改完按 R55 双配置重建，再跑 `RoiInteractionChecks`；条数必须是 8 条（R65：条数 + 明细原文才算证据）。
