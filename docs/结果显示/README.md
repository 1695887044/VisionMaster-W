# 结果显示插件 · 文档索引

本目录是 **「结果显示」算子（`Plugin.ResultDisplay`，把任意算子输出烧进图像像素）** 的专题文档目录。

> **当前状态：v4.2（离屏烧录版 + 文字位置）已落地并通过全量 gate（2375 通过 / 0 失败，2026-10-14）。** 方案说明书已按实现校准（R8），与实现的差别逐条记在**第 14 章「实现口径校正」**；变更记录见 `docs/code-changes/2026-10-13-结果显示插件.md`（v4.1 首轮）与 `docs/code-changes/2026-10-14-结果显示文字位置.md`（v4.2，R12）。**唯一未决**：§8.6 的 6 项真机清单（尤其 **dump 节拍实测数字**——它是唯一可能推翻 §12-① 的数据点）。

> **版本沿革**：v3 是"覆盖层（`MeasureAnnotation`）"方案，已被 2026-10-13 的用户裁定推翻（§12-①：走**离屏烧录**）。v4 同时**纠正了 v3 的两处事实错误**：① 仓库里的离屏烘焙实现是**五份**（BlobDetect / CaliperMeasure / BeadInspect / Matching / ImageScript），v3 写的"两份"是低估；② 覆盖层通道目前是**空跑**的——`PluginPreviewExtensions.cs:37-38` 那个带 `annotations` 的重载**全仓零调用方**，12 个发布者全走 2 参重载。所以改走烧录**不会丢掉一个正在用的通道**。
>
> **v4.1（落地版）又纠正了 v4 的两处**：③ 五份里的 `ImageScript\HDevDisplayBackend.cs` **不是烘焙件**（HDevEngine 按 `dev_*` 逐条驱动、`[ThreadStatic]` 窗口状态、`HandoutWindow` 交窗口给脚本，`BakePlan` 表达不了"跑完才知道画什么"）→ **迁移集合是四份，它是永久例外**；④ 用户裁定**本轮就迁**（推翻 §12-③ 设计者"不迁"的建议），迁移当场抓出真 bug：BlobDetect 与 CaliperMeasure 那两份**都没有 `SetPart`**，标注底图整片塌成窗口底色（§8.4 前后对照表）。
>
> **v4.2（2026-10-14，文字位置）**：起因是用户一句"没办法控制文字的位置吗"。四档方案里用户**只点**了"九宫格锚点 + 边距 + 字号"，另三档（几何标签偏移可调 / 逐项归一化坐标 / 先不做）**明确落选**（→ 已写成 §2 的非目标 N8）。落地形态是三个 `[StepConfig]`（`TextAnchor` / `TextMargin` / `TextScale`）**默认值 = 老行为**，实现上有一条硬分工：**垂直分量归 `DisplayDraw`（它才知道行数）、水平分量归 `AnnotationBaker`（只有它持有窗口、量得到字宽）**——理由与两条本轮自踩的坑见方案说明书 **§14.17**，纪律已升格为 AGENTS.md **R39**。

---

## 唯一入口

### 📘 [结果显示插件方案说明书.md](结果显示插件方案说明书.md) ← 从这里开始

**这是本插件的唯一主文档：既是交付给执行者的施工规格书，也是落地后的现行设计文档（R8）。** 第 7 章是逐文件的代码生成清单，第 13 章是分 **7** 批的执行顺序（首轮 6 批 + ★ 批 7 文字位置，**七批都已跑过并就地标了实测数**；**批 1 共享烘焙件不过就不要往下走**，后面全建在它上面），**第 14 章是"实现与上文的差别"逐条记账**——读代码前先看第 14 章，能省掉一轮"文档说的和代码不一样"的困惑。

章节速查：

| 章 | 内容 | 适合谁 |
|---|---|---|
| 第 0 章 | **交付说明** —— 交付物（含 `Core.Halcon` 新文件）/ 执行顺序 / 硬前提 / **「本轮改了 `Core.Halcon`」的级联重编后果** / 不许做 / 验收 / 汇报口径 | 执行者（先读这一章） |
| 第 1 章 | **背景与问题** —— 五条既有事实（数值不上屏、没有纯显示算子、**烘焙五份谱系对照表**、覆盖层"几何→标注"五份、覆盖层通道零调用方）+ §1.1 烧录的三条**已被代码证实**的收益 + §1.2 烧录的五条代价，全部带证据行号 | 所有人 |
| 第 2 章 | **目标 G1~G5 / 非目标 N1~N8**（★ N8 = v4.2 用户裁决：不做逐项坐标、不做几何标签偏移） | 架构、审查 |
| 第 3 章 | **设计决策 D1~D13**（D2 重写为烧录；D9 共享件选址 / D10 只烧不双发 / D11 `disp_*` 直画 / D12 `AnnotatedImage` 输出 / D13 黑底白框，均为 v4 新增；改口请回退到指定小节） | 架构、审查 |
| 第 4 章 | **端口与配置模型** —— 4.1 端口（含 `AnnotatedImage`）/ 4.2 `[StepConfig]` **九项**（首轮六项 + ★ v4.2 三项 `TextAnchor`/`TextMargin`/`TextScale`；三选一写法）/ 4.3 `DisplayItemDef` / 4.4 结果项表 **6** 列（**★ 含"单位"列，容器是 `ItemsControl` 不是 GridView → R19~R22 不适用**，见 §14.4）+「文字位置」独立卡片 | 实现者 |
| 第 5 章 | **渲染规则（烧录管线）** —— 总流程、5.1 底图归一化（**照 BlobDetect 那份，别照 Caliper 那份**；★ 落地后归属改到 `AnnotationBaker.NormalizeForDisplay`，见 §14.1）、5.2 **离屏窗口六条硬纪律 + 两条状态纪律**、5.3 类型分派、5.4 行主口径、**5.5 文字排版（★ v4.2：九宫格锚点/边距/字号可配，逐项坐标仍不给配；垂直归 `DisplayDraw`、水平归 `AnnotationBaker`）**、5.6 颜色白名单、5.7 钳制与元素上限 | 实现者 |
| 第 6 章 | **生命周期 / 线程 / 失败口径** —— 三类对象三种归属、**「有锁不崩≠不阻塞」→ 不做配置态实时预览**、三级降级梯度、无桌面会话边界、配置生命周期三个顺序 | 实现者、审查 |
| 第 7 章 | **代码生成清单 7.1~7.10** —— 7.1 `AnnotationBaker` 完整 API 骨架（★ 含 `BakeText.HAlign` 的落地补注）/ 7.2~7.7 插件各文件 / 7.8 `.sln` 三处 / 7.9 演示方案同步 / 7.10 **四处顺手修（✅ 全部已落地）** | 实现者 |
| 第 8 章 | **断言测试** —— **B1~B8（共享烘焙件；★ B8 = 水平对齐的墨迹盒实测，含"默认 == 显式 Left"的兼容闸门）** + A1~A9 + **C1~C5（含缩略图带标注 C3、导出 PNG 带标注 C4）** + **★ U1~U9（文字位置 24 条：锚点/边距/倍率/钳制，判据是"关系"不是复写公式）** + **静态扫描 16 条**（原 6 → 首轮 10 → ★ v4.2 加 ⑦⑦b⑧⑧b⑧c）+ 全量 gate（**★ 实测 2375 通过 / 0 失败**）+ 构建投递检查（**★ 实测红线②③全过**）+ **6 项只能真机验（未决）** | 实现者、审查 |
| 第 9~13 章 | 验收勾选表（**19 条，17 条已勾**；未勾的两条 = 真机 6 项清单、CodeGraph 重建）/ 红线对照（**★ 已补 R19~R22、R36~R39**）/ 风险 12 条（**★ 第 10 条"零断言覆盖"已消解**）/ **未决 4 条（①②③ 均已裁定，④ 收进高级区）** / 执行顺序 **7** 批（原 6 批 + ★ 批 7 文字位置，**批 6/7 实测结果已就地标注**） | 审查、docs |
| **第 14 章** | **★ 实现口径校正（落地后新增）** —— 17 条"上文怎么说 → 实现怎么做 → 为什么"：归一化归属、`Cross` 的 `P` 长度 2 或 3、椭圆角度的**两处**变换各归各家、6 列与非 GridView、`HRegion`/`HXLD` **不抽稀**、`box` 之争由 B2 实测终结（含 `Box` 大写 B 的 CS1739 坑）、迁移改了一层图层顺序、**演示方案 11 步实测数据**、`JudgeItemLabels`/`IsNg`/`OnConfirm` 三处口径、`InternalsVisibleTo`、**`.Value` 是 object 要用 `.TypedValue`**、`HImage` 明确拒绝、真机清单未决、**★ §14.17 文字位置：两遍排版、`HAlign` 用 `init` 属性保兼容、`GetStringExtents` 必须在 `SetFont` 之后、枚举顺序承重、两条自踩坑（XAML ASCII 引号 / 断言 bug 长得像产品 bug）** | **所有人（读代码前先看这章）** |

---

## ⚠️ 最需要记住的三条

1. **烧录让标注"随图走"，宿主零改动**——`ImageCollectionService.BuildFrame:451-452` 里**缩略图与 `Image` 是同一份像素**，`ImageExporter.cs:76` 只消费 `frame.Image`。所以烧进像素的标注**自动**出现在大图、缩略图、导出 PNG、报警留存图上；覆盖层方案（v3）这三条**全都拿不到**，要治就得动宿主（§12-②）。这是本轮改道的第一理由。
2. **离屏窗口六条硬纪律，每条都有仓库实测事故**（§5.2）——`mode` 只能 `'buffer'`（其它值报 **#1305**）；窗口**按尺寸常驻复用**、`Dispose` 才关（反复开关 → **#9302 死锁**）；每轮**无条件** `SetPart`（否则底图缩在左上角 / 上一轮黑边烙进像素——**★ 这条不是理论风险：本轮迁移当场实测到 BlobDetect 与 CaliperMeasure 两份都漏了它，标注底图整片塌成窗口底色，`(150,20)=0`（期望 100）**）；文字坐标系**只用 `"window"`**（无桌面会话下 `"image"` 整段画不出来）；`DispText` 必须**显式**给 `box`（★ 校正：不是"只给 `box_color` 画不出框"，实测 **box 默认就是开的**（18303px vs 3754px），真正的风险是"把决定权交给默认值"→ `BakeText.Box` 因此做成**必填位置参数**，且名字是**大写 `Box`**，写 `box:` 会 CS1739）；`DumpWindowImage` 的源对象**立刻 `Dispose`**（500 次不释放 ≈ **漏 160MB**；B7 实测 500 轮私有内存增量 **−4.4MB**）。
3. **烘焙件必须落在 `Services\Core.Halcon\Helpers\AnnotationBaker.cs`，不许写成插件私有类**——仓库里曾经有**五份**同源代码（§1-3 谱系对照表），第六份不可接受。**★ 本轮已把其中四份迁进去**（BlobDetect / CaliperMeasure / BeadInspect / Matching），第五份 `ImageScript\HDevDisplayBackend.cs` 是**永久非烘焙件例外**（HDevEngine 按 `dev_*` 逐条驱动，`BakePlan` 表达不了）。选 `Core.Halcon` 而不是新建程序集，决定性理由是**红线②**：`Modules\` 只放 `Plugin*.dll`，新程序集反而要往那里塞非 Plugin DLL（正是"两份静态状态"陷阱）。守门人是静态扫描 **④/④b/④c**：**全仓 `OpenWindow` 只许出现在 `AnnotationBaker.cs` + 1 份豁免里（④b 专门守"迁完的文件必须从清单删掉"，防清单只增不减；④c 守"新插件不许绕过共享件自己写一份"）**。

另三条容易踩的：**端口名是稳定 Id（`r1`/`r2`…）**，改名即断线（`FlowCompiler.cs:1073`），标签随便改（D5）；**必须自带自定义配置视图**——兜底面板 `AutoPortConfigView` **不渲染 `[StepConfig]`**，没视图用户加不了结果项（D4）；**配置模型必须留在 `Plugin.ResultDisplay` 命名空间**——`.vms` 的 `$type` 白名单只放行 `VisionMaster.Models`/`VisionMaster.Communications`/`Plugin.*`/`VisionMaster.Scada`，放进 `Core.Interfaces.*` → **方案存得下、打不开**（D8）。

★ 落地后添的四条（都是真踩的，详见第 14 章；第四条是 v4.2 文字位置那轮）：
- **`OutputPort<T>.Value` 是 `object`，`.TypedValue` 才是 `T`**——读 `.Value` 再 `.IsInitialized()` 会**静默绑定到 Prism 的扩展方法**（CS0411），把 `HImage`/`string` 当参数传直接 CS1503（§14.14）。
- **`.vms` 里动态端口必须在灌值之前 `RebuildDynamicInputs()`、且只调一次**——调晚了连线落不到端口上，调多次把已有连线的端口重建掉（连线静默丢失）。C5 有专门断言（§14.9）。
- **迁移会改图层顺序**：baker 的绘制顺序固定为 `Shapes → Objects → Texts`（`AnnotationBaker.cs:148-150`），BlobDetect 的缺陷号因此在黄色高亮框**之上**。要改层序就改 plan 的入列顺序，**不要改 baker**（§14.8）。
- ★ **v4.2：文字宽度只有烘焙件能量**（AGENTS.md **R39**）——宽度取决于**窗口当前字体**，而窗口只在 `AnnotationBaker` 里；调用方（`DisplayDraw`）**不许估宽度**，只把"哪边贴边"表达成 `BakeText.HAlign`（`Col` 的含义随之变左边缘/中心/右边缘）。baker 内的 `GetStringExtents` **必须排在 `TrySetFont` 之后**，否则量的是上一号字体的宽。本仓**没有** `GetTextExtents`（反射核对过）。

---

## 本目录文件

| 文件 | 说明 |
|---|---|
| [`结果显示插件方案说明书.md`](结果显示插件方案说明书.md) | **唯一主文档**（v4.1 离屏烧录版，**已落地**；施工规格书 + 现行设计文档，第 14 章记实现差别） |

---

## 目录外的相关文档

| 文件 | 关系 |
|---|---|
| `../code-changes/2026-10-13-结果显示插件.md` | **v4.1 首轮变更记录（R12 闭环）**：交付物清单、迁移前后像素证据、gate 计数、未决项 |
| `../code-changes/2026-10-14-结果显示文字位置.md` | ★ **v4.2 变更记录（R12 闭环）**：九宫格锚点/边距/字号，分工（垂直 `DisplayDraw` / 水平 `AnnotationBaker`）、B8 + U1~U9 + 扫描 ⑦~⑧c 的实测数字、gate 2335→2375、两条自踩坑 |
| `../缺陷检测/2026-09-24-Blob缺陷检测插件.md:650-700` | **160MB 泄漏专章**（`:660-662` 结论、`:671-675` 对照表、`:676-678` 影响范围）——§5.2-6 与断言 B7 的出处；注意 `:680` 的检索范围只覆盖了 BlobDetect |
| `../卡尺测量/2026-09-25-卡尺测量插件第一批.md:342-343,570-585` | **无桌面会话**边界 + **UI 线程与流程线程共用一扇窗互相阻塞**的实测代价（→ §6.2 裁定"不做配置态实时预览"） |
| `../图像采集/2026-10-04-缩略图快路径先缩小再转换（A2）.md:114` | 邻近成本基线：**每帧 `CopyImage`+缩略图 = 13.8 ms / 28.69 MB**——§8.6-4 的 dump 节拍实测要和它对比 |
| `../code-changes/2026-09-15-主界面运行控制互锁与流程栏交互.md:97` | `SetPart` 缺失**登记在案至今未修**（→ §5.2-3：共享件必须把它做成无条件动作） |
| `../code-changes/2026-09-25-插件配置视图样式统一.md` | 配置视图样式收编（本插件视图必须复用 `Plugin*` 资源键，§7.6） |
| `../code-changes/2026-09-20-插件构建投递收口Directory.Build.targets.md` | 投递清单（红线①②③的出处） |
| `../数据记录/数据记录插件.md` | **动态端口 + 行内连线表格**的范式来源之一（列定义 = 动态端口） |
| `../结果上报/结果上报插件.md` | 另一份动态端口范式来源（`Fields` 表 → `RebuildDynamicInputs`）；同范式兄弟插件 |
| `../新插件端口速查.md` | `[StepConfig]` 三选一写法（R18）、端口/钩子契约 |
| `../几何工具/` | 「几何查找 / 几何关系」两个纯几何算子——本插件的**首要消费者**（几何关系无图像口、连 `Core.Halcon` 都不引用，现在没有任何显示出口） |
| `../图像脚本/图像脚本插件.md:356,438,452` | `'buffer'` 是唯一可用 mode 的实测记录（#1305 三处） |

---

## 相关源码（★ 落地后的实际位置；括号里的"抄来源"仍值得读，它们是这些纪律的出处）

| 路径 | 说明 |
|---|---|
| `Services/Core.Halcon/Helpers/AnnotationBaker.cs` | **✅ 已落地（672 行，v4.2 +51）**——共享烘焙件。`Bake(HObject baseImage, BakePlan plan)` + `NormalizeForDisplay`（`public static`，`:192`）+ `BakePlan`/`BakeShape`/`BakeObj`/`BakeText`/`BakeShapeKind`/`BakeHAlign`（文件尾）。六条硬纪律与两条状态纪律**只在这一个文件里**；类注释头部有"四份已迁入 + 一份永久例外"的记账。★ v4.2 新增：`BakeText.HAlign`（`init` 属性，默认 `Left` = 历史行为）+ `TryMeasureStringWidth`（`GetStringExtents`，排在 `TrySetFont` 之后，量不到就退回左对齐并 Warn） |
| `Plugins/Plugin.ResultDisplay/` | **✅ 已落地（1996 行，v4.2 +248）**：`Plugin.ResultDisplay.csproj`(37) / `DisplayItemDef.cs`(60) / **`TextAnchorKind.cs`(29，★ v4.2 新文件，成员顺序承重)** / `ResultDisplayPlugin.cs`(569) / `Services\DisplayDraw.cs`(726) / `ResultDisplayView.xaml`(359)+`.xaml.cs`(96) / `Help\01-overview.md`(120) |
| `FlowCanvasChecks/AnnotationBakerChecks.cs` | **✅ B1~B8**（606 行；六条硬纪律的回归网、500 轮泄漏守门、`box` 两头钉死、串轮污染、uint2 死白陷阱。★ B8 = 水平对齐的**墨迹包围盒**实测，辅助 `InkBox`（`Threshold`+`SmallestRectangle1`）） |
| `FlowCanvasChecks/ResultDisplayCheck.cs` | **✅ A1~A9 + C1~C5 + U1~U9 + 静态扫描 16 条**（1882 行，v4.2 +312；③/⑥/⑧b 用 `SplitTopLevel`+`ArgsOf` 按**实参位置**判，切分器要跟踪 `{}` 与字符串字面量。★ U 系列的尺子是从基准 plan **量出来**的（`autoMargin`/`gap`），不复写公式） |
| `FlowCanvasChecks/PixelProbe.cs` | **✅ 新增（60 行）**——四个插件"标注底图保真"断言共用的**像素尺子**。⚠ `min_max_gray` 的**第一个入参是区域不是图像**（必须传 `GenRectangle1` 出的 domain），且只吃单通道（3 通道先 `Rgb1ToGray`） |
| `解决方案/结果显示演示.vms` | **✅ 新增（11 步）**——C5 端到端的载体；底图用仓库既有图 `Image\卡尺测试\5-角度90.png` / `4-圆直径200px.png`。**没有**改 `几何工具演示.vms`，所以 `GeoDemoCheck.cs` 零改动 |
| `Plugins/Plugin.BlobDetect/AnnotationRenderer.cs` | **✅ 已迁移（210 → 90 行）**，现在是"攒 `BakePlan` 的薄壳"。原实现是**基础设施主抄来源**（`lock` 罩全程、`TrySetFont`、`Dispose`、`dump` 源对象释放）；⚠ 它**没有 `SetPart`**（迁移前实测底图塌成窗口底色），且 `box` 只给 `box_color`（把决定权交给默认值）——**这两处别抄** |
| `Plugins/Plugin.CaliperMeasure/AnnotationRenderer.cs` | **✅ 已迁移（175 → 62 行）**。原实现是 **`box`+`box_color` 的正确写法**出处与既成分层配色语言（卡尺黄 / 测量青 / 拟合品红 / 边缘点绿）；⚠ 同样**没有 `SetPart`**，且它的 `BuildDisplayBase` 只有一行 `ConvertImageType`（12/16 位死白，已删） |
| `Plugins/Plugin.BeadInspect/BeadInspectPlugin.cs`（内嵌 `OffscreenRenderer`） | **✅ 已迁移（~120 → ~30 行）**。原实现有 `SetPart`（迁移前后都绿）；错误段用 `red` lw2 加粗，一眼看到 NG 在哪一段 |
| `Plugins/Plugin.Matching/MatchingPlugin.cs`（`RenderAnnotated`） | **✅ 已迁移**（`_previewGate`/`_previewWindow`/`_previewW`/`_previewH` + ~78 行渲染体 + `TrySetFont` + `ClosePreviewWindow` 全删，换成一个 `AnnotationBaker` 字段 + 6 行）。原实现是**无条件 `SetPart` 的正确写法**出处 |
| `Plugins/Plugin.ImageScript/HDevDisplayBackend.cs` | **⛔ 永久例外，不迁**（`:26-27,35,41-49` 是 `'buffer'` mode 依据 + "画框写字约 1ms" 唯一量化数字 + `[ThreadStatic]` 窗口；`:514-518` 是 `box` 默认开的量化实测）。✅ `:107-109` 的 `ho` 未 Dispose 已按 §7.10-2 修完，静态扫描 ⑤ 转绿 |
| `Plugins/Plugin.BlobDetect/BlobDetectPlugin.cs:1775-1784,1812` | 元素上限 `MaxRenderMarkers=300` + "仅绘制前 N 项"的提示写法（→ `MaxDrawElements`）；`:1264-1265` 是 `override Dispose()` 里 `_renderer.Dispose(); base.Dispose();` 的范式 |
| `Plugins/Plugin.ResultUpload/ResultUploadPlugin.cs:280-300,355-365,592-628` | 动态口重建 / 生成不撞名的端口名 / NG 值三路智能比对，照抄 |
| `Plugins/Plugin.DataRecord/DataRecordPlugin.cs:160-230` + `DataRecordView.xaml:190-259` | 行内连线表格 + `PortsVersion` MultiBinding 锚点（`:227-231`），照抄 |
| `Shard/Core.Controls/Events/PluginPreviewExtensions.cs:27-84` | 发布通道 `PublishPreview(...)`；**带 `annotations` 的重载 `:37-38` 全仓零调用方**（§1-5），本插件传 `null`（D10）。静态扫描 ⑥/⑥b 守"第 2 实参没有 `+1`" |
| `VisionMaster/Services/ImageCollectionService.cs:352-356,411,435-477,501-528,577-611` | **在宿主工程里，不在 `Core.Halcon`**（`FlowCanvasChecks.csproj:16` 已引用 `VisionMaster.csproj` → 断言可直驱）。`Capture:352` 是 public、`Frames:122` 是 public，但**前置门在 `:355-356`**（图集开关关掉或 `ViewIndex<=0` 直接 return）；`BuildFrame:435`（`:451-452` 缩略图与 `Image` 同源，§1.1-① 的根据）与 `MakeThumbnail:501` 都是 **private** |
| `FlowCanvasChecks/ThumbnailChecks.cs:202-259` | **断言 C3 的现成范式**（造 `AppSettingsService` → 开图集开关 → `new ImageCollectionService(settings, null!)` → 发布 → 读 `svc.Frames[0].Thumbnail` → `finally` 里 `svc.Clear()` + `ImagePreviewCaptureHub.Current = null`），整段照抄 |
| `Services/Core.Halcon/Helpers/ImageExporter.cs:33-86` | 导出只消费 `frame.Image`（`:76`），格式硬编码 `"png"`（`:68`）→ §1.1-② 与 Help 的 PNG 限定（C4 已实测） |
| `Services/Core.Halcon/Color/ColorMarks.cs:9-22` | 全仓画面语言 `Pass=green / Fail=red / Inferred=orange` |
| `Plugins/Plugin.GeometryFinder/Services/GeometryHService.cs:63-67,90-105` | **行主口径**：方向向量 `(dr,dc)=(sinφ,cosφ)`；椭圆长短轴。★ 与 HALCON 的 `Phi`（自列轴逆时针）差一个**符号**，见 §14.3 |
| `Plugins/Plugin.CreateRoi/CreateRoiPlugin.cs:881` | 椭圆角度符号的**同口径旁证**（直接把 phi 交给 `GenEllipse`） |
| `Plugins/Plugin.ImageScript/ImageScriptPlugin.cs:1101-1103,1160-1161,1165-1219` | 判型顺序范式（`DisplayDraw.cs:130-138` 照它）；**纯算子合成路线画不了字**（§5.3 的注），不可替代 |
| `Shard/Core.Interfaces/VisionPluginBase.cs:74-86,101-134,150-167,225-385` | `Dispose`（**无**透传防御，故禁透传口）/ 轮首回收 / `DisposePreviousRoundOutputs`（**有**透传防御 `:158-161`）/ `ApplyConfigValues` / `OnConfirm` / `SnapshotConfigValue` / `AddDynamicInput` |
| `Services/Core.Halcon/Core.Halcon.csproj:11` | `<InternalsVisibleTo Include="FlowCanvasChecks" />`——**本来就有**（原为 `ImageGallery` 的测试辅助而加），本轮**没改这个 csproj**；B1~B8 因此能直调 internal 成员、不走反射（不产生投递物，与红线②/R17 无关，§14.13） |
| `VisionMaster.sln:104,620-631,674` | 工程注册三处（漏一处 = 红线③陈旧 DLL） |
| `FlowCanvasChecks/GeoDemoCheck.cs:17-24` | 端到端断言范式（`.vms` 手工/生成最容易翻车的四点）；★ 本轮**未改**它（新建方案而非改既有方案） |
| `FlowCanvasChecks/CaliperMeasureChecks.cs:205`、`YoloChecks.cs:55`、`PoseTransformChecks.cs:178` | **像素级断言的既有手法**（造已知画布 → 采样）；⚠ `paint_region` 的 Type 只认 `"fill"`（`PreprocessHService.cs:226,247`） |

---

## 维护约定

1. **只改主文档**：`结果显示插件方案说明书.md` 是唯一事实来源。✅ **落地后已回来纠正与实现不符处**——v4.1 那轮就地改了 §4.4 / §8.2-C5 / §8.3 / §8.4 / §8.5 / §11-10 / §12-③ / §13-批6；★ **v4.2（文字位置）那轮改了 §2-N8 / D7 / §4.2 / §4.4 / §5.5 / §7.1 / §8.1-B8 / §8.2-A6+U 表 / §8.3 / §9 / §10**，需要展开讲理由的都记进**第 14 章**（§14.17）；变更记录分别是 `docs/code-changes/2026-10-13-结果显示插件.md` 与 `docs/code-changes/2026-10-14-结果显示文字位置.md`（R12）。**以后再改实现，第 14 章要跟着加条目**（它的读法是"上文怎么说 → 实现怎么做 → 为什么"）。
2. **本轮不是"零公共契约改动"**：新增 `public sealed class AnnotationBaker` 到 `Services\Core.Halcon\Helpers\`。它**不改任何现有签名、不动 `Core.Interfaces`**，所以不是契约破坏；但 `Core.Halcon` 是 `Core.Controls`（`Core.Controls.csproj:13-14`）的上游、**21/30 插件直接引用它** → **级联重编约 30 个插件 + 全量重投递 `Modules\`**，且必须 `dotnet build VisionMaster.sln` 全量重编（只重建单工程 = 现场跑旧 DLL，红线③）。✅ **实测：`dotnet build VisionMaster.sln` EXIT=0 / 0 个错误（51 工程），`Modules\` 六个相关插件 DLL 全部重新投递（01:47~01:51）**。**永远不许为此给 `Core.Halcon` 加 ProjectReference**——它全文零引用，拿不到 `ILogService`，所以烘焙件的日志出口只能是 `Action<string>? warn` 回调（D9）。
3. **`Modules\` 里必须没有任何 `Core.*.dll`**（红线②，`Directory.Build.targets:41-43` 只投 `Plugin*.dll`，**不许放宽**）。这正是 D9 不选"新建独立程序集"的决定性理由。✅ **实测 `ls Modules/ | grep -ic "^Core\."` = 0**；`Core.Halcon.dll` 会出现在**每个插件自己的 `bin\Debug\net9.0-windows\`**（编译期需要）与**宿主输出目录**（120320 B / 01:47，运行期解析处），**就是不进 `Modules\`**——这正是红线② 要的形状（一份静态状态）。
4. **不许出现第六份烘焙件；✅ 四份既有的已于本轮迁入，第五份是永久例外**（§12-③，**用户裁定推翻了设计者"本轮不迁"的建议**）。
   - **已迁的四份**：`Plugin.BlobDetect\AnnotationRenderer.cs`（210→90 行）、`Plugin.CaliperMeasure\AnnotationRenderer.cs`（175→62 行）、`Plugin.BeadInspect\BeadInspectPlugin.cs` 内嵌 `OffscreenRenderer`（~120→~30 行）、`Plugin.Matching\MatchingPlugin.cs` 的 `RenderAnnotated`（删掉 `_previewGate`/`_previewWindow`/`_previewW`/`_previewH` + ~78 行渲染体 + `TrySetFont` + `ClosePreviewWindow`）。顺带删掉两个**逐字重复**的归一化方法（Caliper 的 `BuildDisplayBase`、Blob 的 `BuildDisplayBase`+`DropAlphaChannel`）。
   - **迁移前先织网**：设计者当初"不迁"的第一条理由是"五份零断言覆盖、无网可兜"——本轮的解法不是放弃迁移，而是**先给四份各补一条像素级"底图保真"断言**（共用尺子 `FlowCanvasChecks\PixelProbe.cs`），**再动手**。**网当场兜住两条真 bug**（BlobDetect 与 CaliperMeasure 都漏了 `SetPart`），并暴露出 `BlobDetectChecks` 里一道**误判的"环境闸门"**（它把真 bug 记成"本环境离屏窗口不渲染底图层次"而静默放行——闸门已删、改硬断言）。**教训**：一条"跳过"型断言必须能被反证（这里 `AnnotationBakerChecks` B1 早就实测 buffer 窗口四角像素 == 底图灰度 100），否则它就是给未来的回归留的后门。
   - **永久例外**：`Plugin.ImageScript\HDevDisplayBackend.cs` **不是烘焙件**——它是 HDevEngine 按 `dev_*` 算子**逐条驱动**的显示后端（`[ThreadStatic]` 窗口状态、`HandoutWindow` 把窗口交给脚本），"画什么"在脚本跑完之前根本不存在，`BakePlan`（跑完才攒得出来）表达不了它。它挂在扫描 ④ 的豁免清单里，**清单只剩这一条、不许再变长**（④b 断言专门守"迁完的文件必须从清单删掉"）。
   - **五份覆盖层"几何→标注"实现**（`GeometryFinder` / `ColorRegion` / `BeadInspect` / `Calibration` / `CaliperMeasure`）的收敛点是插件私有的 `Services\DisplayDraw.cs`，**迁移仍属后续轮次**（覆盖层通道零调用方，收敛它不解决任何在跑的问题）。
5. **显示失败绝不停线**：三级降级（丢单项 → `AnnotatedImage=null` 发原底图 → 不发布 + 一条 infoRow），`Success` 恒 true（`FlowInvoker.cs:132-133`）；发布与烧录的 `try/catch` **必须 Warn，不许空 catch**（静态扫描 **②** 会判红——★ 范围是 `Plugin.ResultDisplay` + `AnnotationBaker.cs` 两个文件，**不是全仓**：全仓有 64 处合法的 `try { x.Dispose(); } catch { }`，逼它 Warn 会淹掉真信号）。烧录前必须钳制 NaN/Inf/越界/非法色名/负半径，否则一次抛错 = **整张标注图丢**（B6 实测：5 类坏输入 → 5 条 Warn、坏元素只丢自己、好元素照常烧进像素）。
6. **一个流程放一个实例**（D3）：每轮付一次全幅 `DumpWindowImage` **加**一条全分辨率 `CopyImage`+缩略图（13.8ms/28.69MB 量级），帧不合并，图集上限 300/50。别把显示算子放进高频循环体。**⏳ dump 的节拍成本仓库从未测过 → §8.6-4 的真机实测仍是未决项**；超预算就按 §12-① 回退覆盖层，**这个决定要在真机验证当场做，不要拖到现场**。（离线能给的旁证：`HDevDisplayBackend.cs:35,41-49` 的"画框写字约 1ms"，但那是小窗、不是全幅 dump。）
7. **格式化一律 `InvariantCulture`**（同数据记录插件纪律，防区域设置改结果）；数值文本口径遵守 R34「宽进严出」，报错要带**被拒的原类型与原值**。★ 具体落地（§14.11）：数值默认 `0.####`（最多 4 位小数、不留末尾 0）；解析一律 **`NumberStyles.Float`**——**不能用 `NumberStyles.Any`**，`Any` 含 `AllowThousands`，`"1,5"` 会被读成 **15**（正是 R34 明令拒绝的那条）；`bool` 判定值接受 **`NG`/`OK`/`1`/`0`** 四种写法（现场习惯不统一，硬要一种就是逼用户改配置）。
8. **离线断言只能证明它不炸 + 像素真的烧进去了，证明不了它好看**：⏳ **§8.6 的 6 项真机清单一项都还没做**（图标是否豆腐块 / 多行是否叠字 / 缩略图与导出 PNG 的肉眼复核 / **dump 节拍实测数字** / 无桌面会话降级 / 300-50 帧上限下的内存与节拍）。演示方案已就位（`解决方案\结果显示演示.vms`），打开跑一轮就能覆盖前 3 项。
