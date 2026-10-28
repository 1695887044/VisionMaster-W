# 工程纪律分册 · HALCON 画布交互

> 本册是 `AGENTS.md` §16 的**完整判据**（R51-R55：手势换绑、摘除容差、断言窗口、轴语义、双配置重建）；注入层只留指针与「最易踩」。
> 触发条件：改 `Services\Core.Halcon` 的 `HalconBase`/ROI 交互、改插件视图里的画布手势、写/改 `RoiInteractionChecks`。
> 起因与取证见 `2026-10-22-工程纪律全集（原文归档）.md` §16。
> 与 §11 的边界：R53「画布交互断言只能用可见窗口」与 §11 R37①「`OpenWindow` mode 只能 `'buffer'`」方向相反——**离屏烘焙走 buffer，交互断言走可见窗口**，两条不许互相套用。

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
