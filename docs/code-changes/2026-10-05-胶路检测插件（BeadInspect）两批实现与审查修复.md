# 2026-10-05 胶路检测插件（BeadInspect）两批实现与审查修复

- 日期：2026-10-05
- 范围：新增 `Plugins\Plugin.BeadInspect\`（csproj + 10 个源文件）、`FlowCanvasChecks\BeadInspectChecks.cs`（胶路断言 60 项）；同步 `docs\胶路检测\` 两份文档
- 依据：`docs\胶路检测\胶路检测插件方案说明书.md`（§7.1 探针实测结论 P1~P12、§7.2 断言、§8.2 步骤表、§10.2 硬约束）
- 链路：**设计文档先行（2026-10-04 探针 P1~P12 全部实测，默认值/算法链/硬约束均有实测出处）→ 本日第一批核心实现 → 第二批配置界面 → reviewer 独立审查（PASS with notes）→ 第三批 P2 修复 → 全量回归**
- 协作口径：实现两批 + reviewer 子智能体审查 + docs 落盘（本文）

---

## 一、背景

胶路检测插件按"设计文档 + 探针先行"的路线推进：2026-10-04 探针 P1~P12 在真实样图（`Image\bead\adhesive_bead_01..07.png` + `_ref`）上全部实测跑通并回写主文档 §7.1，默认值与算法口径不再是推断（例如：`target_thickness` 下限 6 = P10、骨架必须树直径合并 = P6、容差 8 = P12、planar 模型重建 284~360ms 必须缓存 = P5）。本日在该基础上完成 §8.2 步骤 2~8 的全部实现（步骤 5 的 UI 人工验收除外），两批交付，随后审查与修复收口。

## 二、第一批：核心实现（§8.2 步骤 2~4，新增 11 个文件）

### 2.1 交付物

| 文件 | 职责 |
| --- | --- |
| `Plugins\Plugin.BeadInspect\Plugin.BeadInspect.csproj` | 插件工程文件（参照 `Plugin.Matching.csproj`：net9.0-windows / UseWPF / Nullable，halcondotnet 走 `..\..\DLL\Halcon\halcondotnet.dll`） |
| `BeadInspectPlugin.cs` | 插件主体：入 5 / 出 16 端口面、14 项 `[StepConfig]`（含 `TargetWidth` 下限 6 钩子）、bead/planar 模型指纹缓存、mm 换算（失配必须失败）、离屏标注渲染 |
| `BeadInspectHalcon.cs` | 纯算子层（可被未来插件复用）：`TryBuildContour`（折线路径）、`CreateBeadModel` / `ApplyBead`、`PrepareAlignment` / `AlignImage`（平面可变形对齐）、`DeriveRectifyTarget`、`SegmentBeadByRefDiff`（参考图差分提取）、`SegmentBeadBlackHat`（无参考图回退）、`ExtractCenterline`、`LongestPathThroughTree`（树直径合并）、`Downsample`（托管等距抽稀）、`DirectedHausdorff` |
| `BeadInspectEnums.cs` | 对齐模式 / 单位 / 极性等枚举 |
| `Models\BeadRecipeEntry.cs` | 配方条目（落盘 DTO，留在插件内不进 `Core.Interfaces`） |
| `FlowCanvasChecks\BeadInspectChecks.cs` | 回归断言：§7.2 断言 1~9、13、14、15（54 项） |

**端口面**（契约 §3，断言 8 逐字符锁定，改名即断言失败）：入 `SrcImage` / `RecipeName` / `AlignedImage` / `RoiRegion` / `Transform`，出 16 个（`IsOk` / `NgReason` / `AnnotatedImage` / `ErrorCount` / `ErrorTypes` / `ErrorLengths` / `ErrorRows` / `ErrorCols` / `NoBeadCount` / `TooThinCount` / `TooThickCount` 等，见 `BeadInspectPlugin.cs:152` 起）。

### 2.2 关键实现决策（后人对照用）

| # | 决策 | 理由与出处 |
| --- | --- | --- |
| 1 | 输入属性命名 **`AlignedImageIn`**，端口名仍为契约名 **"AlignedImage"** | 同名端口在输出侧还有一个，C# 属性不能同名——端口名以构造参数为准，连线不受影响（`BeadInspectPlugin.cs:135-142` 注释即写明此事） |
| 2 | `RectifyQuadJson` **双格式**：`[[r,c]×4]` 自动推导目标矩形（`DeriveRectifyTarget`，`BeadInspectHalcon.cs:309`）/ `{"src","dst"}` 显式四点 | 自动推导路径**未过真值断言**（坐标系与探针逐像素对不齐），UI 有警示；真值断言统一走显式 src/dst（断言文件头注释 `BeadInspectChecks.cs:24-26`） |
| 3 | 断言里 **`MinErrorLength = 0`**（`BeadInspectChecks.cs:437,514`） | P4 真值是**未过滤**的算子原始计数，实测最短段只有 4px，出厂默认 5 会滤掉真值段；**生产默认 5 不变**，只是断言显式关掉过滤 |
| 4 | 提取链**未加** `SmoothContoursXld` | 忠实复刻探针已验证口径；后续若加平滑，必须先回归断言 13（覆盖率 ≥95%）与 15（平均偏离 <3px） |
| 5 | planar 模型释放走 `ClearDeformableModel`（`BeadInspectPlugin.cs:739`） | 本机 halcondotnet 23.05 **无 `ClearPlanarUncalibDeformableModel` 导出**（二进制 grep 核实）；`ClearDeformableModel` 对 planar 句柄同样有效 |
| 6 | bead / planar 模型按指纹惰性缓存 | 模型不可序列化（§5.3），planar 重建 284~360ms（P5），指纹不变不重建（断言 9 用计数器锁定） |

### 2.3 验证（第一批）

| 项 | 结果 |
| --- | --- |
| 插件工程构建 | **0 错误** |
| 胶路断言 1~9 / 13 / 14 / 15 | **54 项全绿**，含 7 图真值表：01/02/04 = OK、03 = 太细 1 + 偏移 1、05 = 缺胶 1 + 太细 1 + 太粗 1、06 = 缺胶 1、07 = 偏移 3 + 缺胶 1（容差 8，P12 口径） |

## 三、第二批：配置界面（§8.2 步骤 5~6）

### 3.1 交付物（新增 5 个文件 + 断言扩充）

| 文件 | 职责 |
| --- | --- |
| `BeadInspectView.xaml(.cs)` | 配置界面：左配方列表 + 右画布 + 点列表格（仿 Matching 布局） |
| `BeadInspectPluginConfigView.cs` | `partial class BeadInspectPlugin : IPluginCustomViewProvider`（`BeadInspectPluginConfigView.cs:25`），宿主配置视图接入 |
| `BeadPathEditor.cs` | 插点 / 删点 / 命中几何——**界面交互与断言 12 共用同一份几何**，避免"断言测的是一套几何、界面跑的是另一套" |
| `BeadViewModels.cs` | 配方 / 点列行 ViewModel |
| `EnumDisplayNameConverter.cs` | 枚举显示名（中文化）转换器 |

### 3.2 功能要点

- **画布逐点拾取**：左键点最近线段插点 / 右键删点 / 拖点 / 撤销栈 100 步 / 表格双向同步；以叠加鼠标事件实现，**不改共享控件**（`ImageEdit` 零改动）；
- **学习 / 自动提取**：覆盖已有点列前弹确认；提取失败**不写空点列**（保留旧路径）；无无胶参考图时自动走 black-hat 回退（`SegmentBeadBlackHat`）；
- **矫正四点拾取**（`PlanarDeformable` 模式）：支持显式目标矩形兜底（对应决策 2 的 `{"src","dst"}` 格式）。

### 3.3 对核心批次的回归性小改（仅两处）

类头注释更新；`Dispose` 追加 `DisposeConfigState()`（配置态画布底图 / 预览图 / 行 VM 的释放，`BeadInspectPlugin.cs:770`）。其余核心批次代码不动。

### 3.4 验证（第二批）

胶路断言补 10（自动提取，点数区间 40~80）/ 11（mm 失配三态）/ 12（插点保序）+ UI 样式契约，**共 60 项全绿**。

## 四、reviewer 审查：PASS with notes

- **20 项必查全过**：端口契约逐字符比对、`[StepConfig]` 14 项逐项核对、算法六条硬约束（§10.2：对齐前提 / 单路径 / 极性二选一 / 宽度像素 / 模型不可序列化 / 下限 6）、资源释放、NG 语义（NG 不是失败，`Success` 保持 true）、话术；
- **三条红线全过**（见第七节）；
- **5 条申报偏差全部核实属实**（即 2.2 节决策 1~5 类的实现与方案字面差异，均有实测或二进制证据支撑）。

## 五、第三批：P2 修复（5 项）

| # | 问题 | 修法与出处 |
| --- | --- | --- |
| P2-1 | 零命中时话术拿初始值 0 编造 | 区分「未找到匹配实例」与「分数低于阈值」两种零命中，各给对应中文提示与下一步 |
| P2-2 | bead 指纹在 Plugin 与 ConfigView 两处手写 | 抽成 `BeadRecipeEntry.BuildBeadSignature(...)` **单一来源**（`Models\BeadRecipeEntry.cs:159`）；调用点 `BeadInspectPlugin.cs:603`、`BeadInspectPluginConfigView.cs:636`，两处指纹永不分叉 |
| P2-3 | planar 指纹未含参考图内容变化 | 补 `LastWriteTimeUtc.Ticks`（`BeadInspectPlugin.cs:661`）——同路径**覆盖文件**（重新学习）后指纹必然变化，兜底失效缓存 |
| P2-4 | 断言 10b 点数区间偏宽 | 收紧为 **40~80**（`BeadInspectChecks.cs:167`，与 13d 同口径），对齐 §7.2 与 P12 实测 |
| P2-5 | planar 释放失败静默 | `EnsurePlanarModel` / `ReleasePlanarModel` 加**可选** `ILogService` 参数（`BeadInspectPlugin.cs:641,733`）：有 logger 走 `log.Error`（运行期 `RunAlgorithm` 传 `context.Logger`，`:300`），Dispose 期无 logger 走 `Debug.WriteLine`——照 `ImageScript\EProcedure.cs:246` 先例，不再吞异常 |

## 六、验证证据（修复后全量）

| 项 | 结果 |
| --- | --- |
| 全量断言 | **1198 通过 / 19 失败**——19 项均与胶路无关（相机注册表 1、Yolo 1、UI 键 1、动态候选 2、线序 10、脚本预览 3 等环境性既有失败） |
| 胶路断言 | **60 项全绿** |
| 红线③（产物新鲜） | `Modules\Plugin.BeadInspect.dll` 时间戳晚于全部插件源文件（构建即投递，`Plugins\Directory.Build.targets`） |

## 七、红线核查（reviewer 实测）

| 红线 | 结果 |
| --- | --- |
| ① 插件工程在 `Plugins\` 下 | ✅ `Plugins\Plugin.BeadInspect\`，构建自动投递 `Modules\` |
| ② 公共契约程序集不进 `Modules\` | ✅ `Shard\Core.Interfaces\` 无 Bead 类型（配方/结果 DTO 全在插件内）；`Modules\` 无 `Core.*.dll` |
| ③ 改插件源码必须重建 | ✅ DLL 时间戳晚于全部源文件，产物新鲜 |

## 八、文档同步与遗留

| 文档 | 同步内容 |
| --- | --- |
| `docs\胶路检测\胶路检测插件方案说明书.md` | §8.2 步骤表：步骤 2~9 标 ✅（2026-10-05），步骤 5 验收标注"待用户人工验收"；§11 待定表补第 5 行（UI 人工验收待办） |
| `docs\胶路检测\README.md` | 顶部状态横幅改"已实现"；「相关源码」表改为已创建并列实际文件清单；维护约定第 7 条探针处置改为"可删（三个移植方法已确认在位）"；索引表 §7/§8 状态行同步 |

**遗留**：

1. **步骤 5 的 UI 人工验收待用户**——WPF 鼠标管线无法自动化断言，断言只覆盖几何与数据层（断言 12 与界面共用 `BeadPathEditor` 几何已是能自动化的上限）；
2. §11 其余实施期待定项不变（`PositionTolerance` 现场标定、插点策略手感、mm² 面积输出、black-hat 回退质量）；
3. 本专题目录的**插件使用说明（入门层）**尚未新增（README 维护约定第 2 条：参照卡尺/匹配的"入门层 + 维护层"分层）——属后续轮次；
4. 探针工程 `tools\BeadProbe\` 已可删（README 处置条已更新），删除动作留待用户确认后执行。

## 九、通用性改进：平面匹配搜索范围可配置（2026-10-05 追加）

**起因（通用性审查）**：`find_planar_uncalib_deformable_model` 的角度/缩放范围原先**硬编码**在
`BeadInspectHalcon.AlignImage`（照抄范例 `-0.39rad~+0.39rad`、缩放 1:1）——同一工位换产品没问题，
但**换工位/换相机**（工件旋转变大、视野远近变化）时会「平面匹配未找到匹配实例」，用户只能改源码重建。
（同期审查还确认：`create_planar_uncalib_deformable_model` 的 `auto/use_polarity` 与提取分割的
`smooth_histo` 也属硬编码，但二者是**训练型/分割型语义参数**，调整需重新验证模型质量，一期维持范例口径并留在
识别清单里，不做成随手可调。）

**改动**：

| 文件 | 内容 |
| --- | --- |
| `BeadInspectPlugin.cs` | 新增 6 个 `[StepConfig]`：`FindAngleStartDeg`(-22.35) / `FindAngleExtentDeg`(44.69) / `FindScaleRMin`(1) / `FindScaleRMax`(1) / `FindScaleCMin`(1) / `FindScaleCMax`(1)；新增 `DegToRad` 常量 |
| `BeadInspectHalcon.cs` | `AlignImage` 签名加 6 个入参（角度仍是**弧度**——本层是对算子的直封层），去掉硬编码 |
| `BeadInspectPlugin.cs` / `BeadInspectPluginConfigView.cs` | 3 个调用点改传 `FindAngleStartDeg * DegToRad` 等（度→弧度只在这一处换算，**仓库约定**：插件面向用户用度） |
| `BeadInspectView.xaml` | 新增「③ 平面匹配搜索范围」卡（6 行，含使用提示与逐个 ToolTip）；原「怎么用」改 ④ |
| `BeadInspectChecks.cs` | 新增**断言 16**：16d 静态（XAML 绑定齐全）+ 16a/16b/16c 行为（默认成功 → 窗口挪到 90°~100° 必须失败 → 恢复默认又成功），证明参数真接通算子而非死配置；胶路断言总数 60 → **64** |

**验证**：`VisionMaster.sln` 构建 0 错误；全量 **1411 通过 / 19 失败**（19 项仍为与胶路无关的既有环境性失败）；
断言 16 实测：16a `Success=True IsOk=True`、16b `Success=False Err='图像对齐失败：平面匹配未找到匹配实例…'`、
16c 恢复 `Success=True IsOk=True`。DLL 已随构建投递 `Modules\`（红线③）。

**默认值口径**：`-22.35° / 44.69°` 是 `-0.39 / 0.78 rad` 的度数复刻（差 <0.01°，无行为影响）；
真值表断言用**弧度字面量**直呼算子层，与本组默认值解耦——默认值只能由范例实测定，不许随手调。

**未做（留二期）**：`create` 的 contrast/极性、提取分割的阈值方法仍硬编码——换反光特性或换光照时需改代码，
属算法级调整（要重新验证），已记入方案说明书 §11 待定项。
