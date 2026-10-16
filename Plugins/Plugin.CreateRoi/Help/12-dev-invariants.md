# ROI · 开发：不变量与陷阱清单

> 分类：常用工具 ｜ 来源：插件 Plugin.CreateRoi ｜ 读者：开发

**改这个插件前逐条对照**：每条都是"必须成立的事实 + 不成立时的症状 + 现在由谁守"。
分两类：**A 类违反后立刻报错**（好查），**B 类违反后静默出错**（难查，历史上真踩过）。

守门断言在 `FlowCanvasChecks\CreateRoiChecks.cs`（分节 ①~⑦：形状与掩膜 / 合并与涂擦 / 动态端口与命名 /
选中联动契约 / 运行契约 / 序列化往返 / 三种输出形态）与 `FlowCanvasChecks\RoiInteractionChecks.cs`（画布手势 8 条）。

---

## A 组：违反会当场红或抛

**A1 输入图为空是唯一失败出口**
`src == null || !src.IsInitialized()` → `Fail("输入图像为空或未初始化")` 并 `return`；其余一切问题只 Warn。
症状：改成"某个框失败就 Fail" → 下游整条流程中断，产线一个坏框拖垮全部。
守门：`【失败】无输入图 → 显式失败并写明原因`、`【运行】坏 ROI 只跳过不阻断，RoiCount 仍=成功数`。

**A2 只有 `Rectangle` / `Circle` / `Ellipse` 能重建成区域**
`BuildRegion` 要求参数个数 ≥5 / ≥3 / ≥5，其它 `DrawShapeType`（`Region`、`Mask`）走 `default` 分支 → 返回 `null`。
参数非法被 `catch` 成 `null`，**不抛**。症状：新加一种形状忘了补 `case` → 该框永远"形状参数无效"。
守门：`【形状】矩形/圆形/椭圆面积与中心`、`【形状】空参数/参数不足 → null（不炸）`。

**A3 掩膜抠图的资源必须在 `finally` 里成对释放**
`region_to_bin` → `new HImage(maskObj)`；三通道时 `compose3` 出临时 `maskColor`；`mult_image(..., 1/255, 0)`。
`maskColor` 与 `maskObj` 在 `finally` 释放，`maskedObj` 交给返回的 `new HImage(...)`。
症状：漏一个 → 每轮泄漏一幅整图（500 轮 ≈ 几百 MB）。
守门：`【掩膜】三通道图合成正常（逐通道相乘不截断）`。

**A4 缩放因子必须是 `1/255`**
`mult_image` 的 alpha 写成 `1.0` 会把有效区乘成 255×原值 → **截断刷白**。症状："预览全白""输出图发白"。
守门：`【掩膜】正常模式：ROI 内保留原图灰度`（断言圈内灰度 == 180，正好能抓住截断）。

**A5 `Dispose()` 末尾必须 `base.Dispose()`**
基类那一份负责回收**输出端口**承载的 `HImage`；本插件那份负责私有对象（预览图、合成图、涂擦区域、画布句柄）。
症状：只写 base → 预览图/涂擦泄漏；只写自己 → 端口值泄漏（但基类轮首会回收上一轮，泄漏量小、更难发现）。
守门：`【释放】Dispose 幂等不抛`。

**A6 `OnCanvasRoisChanged` 里新增项必须先退订/订阅 `PropertyChanged`**
`info.PropertyChanged += OnCanvasRoiTuplesChanged`（删除时对应 `-=`）。
症状：忘了订阅 → 拖句柄后 `RoiList` 不同步，确认保存的是**旧参数**（试运行对、重启方案错）；
忘了退订 → 已删除的 `RoiItem` 被迟到回写"复活"或空引用抛。
守门：`【联动】*` 全部 7 条。

---

## B 组：违反后**静默**出错（历史上真的发生过）

**B1 `_seedingCanvas` 刹车不许去掉**
`Initialize` 播种画布集合时必然触发 `Add`。没有这个标志，回写会把刚读出来的 `RoiList` 再叠一遍。
症状：**重开方案后框的数量翻倍**（而且第二次保存就真的写进方案了，不可逆）。
守门：`【往返】ROI 配置无损恢复（名字/形状/参数）`、`【重名防御】历史重名方案播种时去重改名`。

**B2 `RestoreSmear()` 必须直接写字段，不能走属性 setter**
setter 里 `SyncSmearData()` 会用"当前活对象"重算快照；恢复阶段另一边（`_smearErase`）还没恢复、还是 `null`，
走 setter 会把另一边**尚未恢复的快照覆盖成空**。
症状：擦除区重开配置即丢，**下次确认静默把方案数据抹掉**。
守门：`【往返】涂抹区从纯文本恢复`、`【往返】擦除区从 gz 压缩恢复（600 行游程全量还原）`。

**B3 `SelectedRoi` 的 setter 必须"先判同值，再退订"**
顺序写成"先 `old.ParamEdited -=` 再 `SetProperty`"，同值短路路径会把订阅摘走且不再补回。
症状：**参数微调断链**——选完一个 ROI 再选回来，数值框改了不动画布（列表选中路径的历史 bug）。
守门：`【联动】参数微调写回画布句柄（回归：旧 bug 在列表选中路径断链）`、`【联动】重选后参数微调恢复生效`、`【联动】换选后旧 ROI 参数不被误改`。

**B4 联动写入必须加 `ReferenceEquals` 守卫**
`SelectedRoi ↔ CanvasActiveRoi` 互相回写，两边都要"不同引用才写"。
症状：属性通知回环 → 界面卡顿/栈溢出，或"画布点空白后列表仍高亮却无句柄可拖"。
守门：`【联动】画布点空白 → 列表同步取消选中`。

**B5 每个 ROI 裁剪前必须先把两个 Offset 写成 `0`**
`double` 端口基类**不回收**（只回收 `IDisposable` 值），不写零就上一轮残值一直挂着。
症状：某框本轮被跳过，下游却拿到**上一轮的偏移** → 坐标看起来"时对时错"，最难查的一类。
守门：`【运行】*` 与 `【输出形态】OffsetRow/Col = 裁剪图左上角在原图中的位置（70,120）`。

**B6 `PublishPreview` 的窗口号必须直传（不 `+1`、不换算法）**
事件消费端按 `ViewIndex == 窗口号` 等值筛选。
症状：选"窗口 1"投进第 2 格、选"窗口 9"哪格都不显示；写 `0` 时若不加 `if (DisplayViewIndex > 0)` 守卫，"不显示"照样投图。
守门：`【运行】三通道底图全链路正常（裁剪+掩膜+发布）`（发布路径覆盖）。

**B7 `MaskRegion` 恒为正语义，反转只在 `MaskImage` 那一层**
`BuildMergedRegion()` 不含 `MaskInvert`；`BuildMaskAndResult(..., invert, ...)` 才反转。
症状：下游拿到"已排除的区域"再乘一次反转 → 语义乱套，且现场看不出是谁反的。
守门：`【掩膜】排除模式掩膜反转（内 0 外 255）` + `【合并】` 三条（证明合并口径不含 invert）。

**B8 涂擦必须同时进「合并区域」和「每个 ROI 的裁剪域」**
`BuildMergedRegion` 里 `∪ 涂抹 − 擦除`；循环里另有 `region.Difference(_smearErase)`。
症状（历史 bug）：涂擦**只影响预览**，输出图里擦掉的地方照样在 → "看着对、跑出来错"。
守门：`【合并】涂抹区并入有效区域`、`【合并】擦除区从有效区域挖除`、`【擦除】擦除区从裁剪域挖除（4941−316）`、`【擦除】裁剪包围盒不变（只挖洞不缩框）`。

**B9 唯一名强制必须在两个入口都做**
① 画布新增（控件命名序号**每开一次配置就归零**，重开必撞历史名）；② `Initialize` 播种（旧方案可能带历史重名）。
症状：`Crop_` 动态端口静默去重 → 两个框**互相覆盖输出** + 被覆盖的 `HImage` 永远进不了端口、**泄漏** + 按名查找歧义。
守门：`【重名防御】画布撞名后 RoiList 全局唯一`、`【重名防御】唯一名回写控件标签`、`【重名防御】端口无重名`、`【重名防御】去重后端口与快照同步重建`。

**B10 改 `RoiList` 必须连带 `RebuildDynamicOutputs()`，且快照要一起写**
`RoiList` setter、画布增删、播种改名三条路径都要走到它；它内部先 `ClearDynamicOutputs()` 再重建，末尾回写
`StepData.OutputPortDefinitions`（名字 + `typeof(...).AssemblyQualifiedName` + 描述）。
症状（**两种都静默**）：① 只建端口不写快照 → 重开方案后编译器/绑定界面看不到 `Crop_x`，接不上线；
② 只写快照不建端口 → `RunAlgorithm` 里 `Outputs.TryGetValue` 拿不到，该框**一个 Warn 都不发**就消失。
守门：`【端口】画布新增 ROI 即长出 Crop_ 动态端口`、`【端口】快照同步到 StepData（编译器据此接线）`。

**B11 `Domain_` 的所有权移交必须看返回值**
`SetImagePort(...)` 返回 `true` 才把本地引用置 `null`，否则 `finally` 里释放。
症状：端口名拼错/类型不符时不是"报错"而是**二选一的危害**——不释放就泄漏，无条件释放就提前把端口里的图释放掉（下游拿到已释放句柄）。
守门：`【输出形态】Domain_{名} 存在且幅面与原图一致`、`Domain_{名} 只有 ROI 内有像素`。

**B12 `crop_domain` 会平移保留 domain，而不是清成整幅**
实测：圆形 ROI 的裁剪图 domain 面积 = π·r²（2828 px），不是外接矩形（61×61 = 3721）。
症状（认知类，代码不会红）：误以为"裁剪图不带 domain"而把它当纯像素图送进不吃 domain 的下游 →
四角圈外像素进结果；反过来误以为"要自己再 reduce 一次"则多此一举。
守门：`【运行】圆形裁剪图域面积 = π·r²（像素化 2828 px）`（这条就是该认知的反证）。

**B13 `rectangle2` / `ellipse` 的 `length1`/`ra` 沿 column 轴（phi=0 时）**
不是 row（HALCON 口径，`ellipse` 同理）。
症状：按 row 算角点/边中点会得出"手柄命中失败"的**假结论**——探针第一遍就是这么被骗的（AGENTS.md R54）。
守门：`RoiInteractionChecks` 的角点拖拽 8 条、`【形状】矩形区域面积 = 4·L1·L2（旋转不变）`。

**B14 `ParamEntries` 必须 `[JsonIgnore]`**
它是给 UI 用的 get-only 派生集合，`RoiParamEntry` 没有无参构造。
症状：Newtonsoft 尝试填充 → 反序列化抛 `Unable to find a constructor...`，**方案打不开**。
守门：`【往返】RoiList 快照落盘且与活对象脱钩`。

**B15 涂擦快照用 JSON 往返深拷贝，之后界面继续改不能污染已确认数据**
基类 `OnConfirm` 的 `SnapshotConfigValue` 负责；`RoiList` 里每项都要落进这个口径。
症状：确认后继续在界面拖框，把已确认（已存盘）的数据**跟着改掉**——看起来"没保存却变了"。
守门：`【往返】RoiList 快照落盘且与活对象脱钩`。

**B16 掩膜预览是现算的抠图，不是 `MaskImage` 那份黑白图**
`DisplayImage => IsMaskPreview ? _maskPreviewImage : _previewImage`，而 `_maskPreviewImage` 取的是 `BuildMaskAndResult` 的返回值（抠图）。
症状：误改成像 `MaskImage` 那样显示黑白，"预览看不出框对不对"，验收价值直接归零。
守门：`【掩膜】` 组断言分别钉住"圈内保留原灰度"（抠图）与"掩膜内 255 外 0"（二值图）——**两者不是同一张**。

**B17 防抖计时器只在掩膜模式下调度，且 `Dispose` 要停**
`ScheduleMaskPreview` 首行 `if (!IsMaskPreview) return;`；`Dispose()` 首行 `_maskDebounce.Stop()`。
症状：不停 → 配置窗口关了还有定时回调打进来（对象已释放，`HImage.Dispose` 之后再访问 → 抛或访问已释放句柄）。

---

## 并行安全声明的依据（`[ParallelSafe]`）

满足判定口径的四条：纯视觉计算、无 static 可变字段、不碰进程级独占资源、不写全局共享状态；
`RunAlgorithm` 内没有长阻塞，无需分段检查取消令牌。
**注意匹配口径**：`FlowCompiler` 按类型 **FullName 字符串**匹配该特性（不是 `typeof` 等值），
为的是绕开"两份 `Core.Interfaces.dll`"导致程序集身份不一致、明明标了却报 `[并行不安全]`。
所以别把特性写在自己的命名空间副本上。

## 改这个插件之后的必做动作

| 动作 | 判据 |
| --- | --- |
| 重建插件工程（改了生成器/基类相关则全量） | 只重建宿主跑的是旧 DLL（红线③、R16） |
| 核 DLL 真的带了 `Help` 资源 | 资源名数 == `Help\` 下 .md 数（R42，增量构建会静默漏打包）；判据见下方代码块 |
| 跑 gate（含 `CreateRoiChecks` + `RoiInteractionChecks` + `HelpManualCheck` HM0~HM15） | 手册改动的红线在 HM12（`NN-dev-` 命名）/ HM12b（不许另立子目录挤掉用户页）/ HM13（开发章节 ≥100 行） |
| 改了 `Services\Core.Halcon` 时 Debug 与 Release 都重建 | `pack.ps1` 打的是 Release（R55） |
| 大改动后重建 CodeGraph 索引 | 不重建 = 影响面查询静默少项（R28） |

```powershell
# 只看构建退出码不算数——增量构建会静默漏打包（R42）
tr '\0' '\n' < Modules\Plugin.CreateRoi.dll | grep -a "\.Help\."
```
