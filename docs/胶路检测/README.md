# 胶路检测插件 · 文档索引

本目录是**胶路检测插件（`Plugin.BeadInspect`）**的专题文档目录。

> ✅ **插件已实现（2026-10-05）**：`Plugins\Plugin.BeadInspect\` 与 `FlowCanvasChecks\BeadInspectChecks.cs` 已落地，
> 胶路断言 60 项全绿；改动记录见 [`../code-changes/2026-10-05-胶路检测插件（BeadInspect）两批实现与审查修复.md`](../code-changes/2026-10-05-胶路检测插件（BeadInspect）两批实现与审查修复.md)。
> 本目录仍是主文档所在专题目录（AGENTS.md R8：现行设计文档**不进归档**），持续维护。
> 步骤 5 的 **UI 人工验收待用户**（WPF 鼠标管线无法自动化断言，见主文档 §8.2 / §11）。
>
> ✅ **设计前的探针验证已完成（2026-10-04）**：P1~P12 全部实测跑通并回写主文档 §7.1，
> 默认值、算法链、硬约束均已有实测出处（不再是推断）。探针工程 `tools\BeadProbe\` **可删**（处置见维护约定第 7 条）。

---

## 唯一入口

### 📘 [胶路检测插件方案说明书.md](胶路检测插件方案说明书.md) ← 从这里开始

**这是本插件的唯一主文档**（现行设计文档）。内容分十一节 + 算子速查附录：

| 节 | 内容 |
| --- | --- |
| 已拍板决策 | **D1~D4 四条范围决策（2026-10-05）** —— 开工前先看这张表 |
| 约定 | 命名 / 坐标 / 单位 / 极性 / 图像前提 —— 开工前先读这五条 |
| 一 | 背景与目标：现有插件的缺口、范例三段结构 |
| 二 | **关键设计决策**（9 项，含"为什么不做成输入端口""为什么模型必须重建"） |
| 三 | 契约与端口：输入 5 个 / 输出 16 个 / 落盘 DTO |
| 四 | 配置参数（`[StepConfig]` 三选一写法逐项标注；`TargetWidth` 含下限钩子） |
| 五 | **运行语义**：执行流程、失配强制失败表、模型生命周期（缓存与指纹）、**mm 换算与失配** |
| 六 | 配置界面：布局 + **不改共享控件的逐点拾取** + 矫正四边形 + **自动提取中心线算法链（已实测）** |
| 七 | **验证计划**：**§7.1 探针实测结论 P1~P12（已完成）** + 15 条断言（**已落地：60 项全绿，2026-10-05**）+ 端到端组合 |
| 八 | 文件清单与实施步骤（9 步，**步骤 2~9 已完成（2026-10-05）**；步骤 5 的 UI 人工验收待用户）+ 工程纪律（四条红线） |
| 九 | 二期路线（接口先留位） |
| 十 | **通用性分析** + **10 条硬约束（全部实测确认）** |
| 十一 | 风险与边界 + 实施期待定项（原 4 条已消掉 2 条） |
| 附 | HALCON 算子速查（签名已反射核实，含中心线提取链） |

---

## ⚠️ 最需要记住的六条

1. **模型不可落盘**：`create_bead_inspection_model` 与 `create_planar_uncalib_deformable_model` **都没有** write/read/serialize 算子（已反射确认）→ 落盘存的是「参考图 + 矫正四点 + 路径点列 + 参数」，模型运行期按指纹惰性重建。
2. **输入图必须先对齐**：`apply_bead_inspection_model` 的「位置偏移」判定以参考位姿为基准，未对齐的图像无从判断。对齐是本插件的前置职责（三模式可选，`PlanarDeformable` 在一期范围内）。
3. **NG 不是失败**：检出缺胶/太细/太粗/偏移时 `Success` 保持 true，只有执行失败才 `Fail(...)`（平台既有口径，与 BlobDetect 一致）。
4. **mm 失配必须失败**：接了标定插件 `Transform` 但图像尺寸不符 → 直接 `Fail`，**绝不静默用旧标定**（口径照抄 `PoseTransform`）。
5. **`target_thickness` 下限是 6**（探针 P10 实测，<6 报 #3716）→ 插件必须做下限保护，别把英文算子错误抛给操作员。
6. **骨架必须按「树直径」合并**（探针 P6 实测：只取最长一条只覆盖 68.9%，未覆盖处误判缺胶）→ 提取算法照抄探针已验证的实现，别自己重新设计。

---

## 本目录文件

| 文件 | 说明 |
| --- | --- |
| [`胶路检测插件方案说明书.md`](胶路检测插件方案说明书.md) | **唯一主文档**（现行设计文档） |
| `README.md` | 本索引 |

---

## 目录外的相关文档

| 文件 | 关系 |
| --- | --- |
| [`../新插件端口速查.md`](../新插件端口速查.md) | **写插件代码前必读**：端口声明/读写入口/`[StepConfig]` 三选一写法 |
| [`../模板匹配/模板匹配插件.md`](../模板匹配/模板匹配插件.md) | 配方库（`RecipeName` 选条目、JSON 落盘、`LearnedSignature` 指纹）范式的来源；也是 `PoseFromMatching` 模式的上游 |
| [`../缺陷检测/Blob缺陷检测插件.md`](../缺陷检测/Blob缺陷检测插件.md) | 「NG 是正常结果」口径与离屏渲染标注图的参照系 |
| [`../卡尺测量/卡尺测量插件.md`](../卡尺测量/卡尺测量插件.md) | 局部离散测宽的既有方案（对比：本插件沿整条路径连续检测） |
| [`../图像采集/README.md`](../图像采集/README.md) | 红线①「插件工程必须放 `Plugins\` 下」的出处 |

---

## 相关源码（已创建，2026-10-05）

| 路径 | 说明 |
| --- | --- |
| `../../Plugins/Plugin.BeadInspect/Plugin.BeadInspect.csproj` | 插件工程文件（参照 `Plugin.Matching.csproj`：net9.0-windows / UseWPF，halcondotnet 走 `DLL\Halcon`） |
| `../../Plugins/Plugin.BeadInspect/BeadInspectPlugin.cs` | 插件主体：入 5 出 16 端口面、14 项 `[StepConfig]`（含 `TargetWidth` 下限 6 钩子）、指纹缓存、mm 换算、planar 模型生命周期、NG 语义 |
| `../../Plugins/Plugin.BeadInspect/BeadInspectHalcon.cs` | 纯算子层：折线路径 / bead 模型 / 平面可变形对齐 / 参考图差分与 black-hat 提取 / 树直径合并 / 托管等距抽稀 / 有向 Hausdorff（探针三方法已移植在位） |
| `../../Plugins/Plugin.BeadInspect/BeadInspectEnums.cs` | 对齐模式 / 单位 / 极性等枚举 |
| `../../Plugins/Plugin.BeadInspect/Models/BeadRecipeEntry.cs` | 配方条目（落盘 DTO）+ `BuildBeadSignature` 指纹**唯一出处** |
| `../../Plugins/Plugin.BeadInspect/BeadInspectView.xaml(.cs)` | 配置界面（左配方 + 右画布 + 点列表格） |
| `../../Plugins/Plugin.BeadInspect/BeadInspectPluginConfigView.cs` | `IPluginCustomViewProvider` partial 实现 |
| `../../Plugins/Plugin.BeadInspect/BeadPathEditor.cs` | 画布插点 / 删点 / 命中几何——**界面与断言 12 共用同一份** |
| `../../Plugins/Plugin.BeadInspect/BeadViewModels.cs` | 配方 / 点列行 ViewModel |
| `../../Plugins/Plugin.BeadInspect/EnumDisplayNameConverter.cs` | 枚举显示名（中文化）转换器 |
| `../../FlowCanvasChecks/BeadInspectChecks.cs` | 回归断言（**已创建**）：§7.2 断言 1~15 + UI 样式契约，共 60 项；真值表 = 探针 P4 实测，容差 8 |
| `../../tools/BeadProbe/` | ✅ **探针工程（使命完成，可删）**——`SegmentBeadByRefDiff` / `LongestPathThroughTree` / `Downsample` 三个方法已移植进 `BeadInspectHalcon`；`probe_result.txt` 是原始实测输出 |
| `../../tools/SolutionProbe/` | ✅ **方案生成与验证台（使命完成，可删）**——用宿主 `SolutionService` 生成演示方案并走 `LoadAsync→FlowCompiler→Run` 全链路验证（7/7 真值一致）；改演示方案参数时可在删除前复用 |
| `../../Image/bead/` | HALCON 官方范例与 7 张样图（`apply_bead_inspection_model.hdev` + `adhesive_bead_01..07` + `_ref`） |

---

## 演示方案（可直接加载运行）

### 📄 `解决方案\胶路检测演示.vms`（2026-10-05 生成并通过全链路验证）

**链路**：图像采集（文件夹模式）→ 胶路检测（配方「范例」：14 控制点折线 + 探针矫正四点 + 15/8/30/dark）→ 标注图发布到视图窗口 1。

**用法**：宿主「打开方案」选 `解决方案\胶路检测演示.vms` → 点运行 → 看 1 号窗口的标注图。
逐张检查：改**图像采集**节点的 `FileIndex`（0~7，对应下表的自然序文件）再运行。

| FileIndex | 文件 | 预期结果（已实测验证） |
| --- | --- | --- |
| 0 | adhesive_bead_01.png | **OK** |
| 1 | adhesive_bead_02.png | **OK** |
| 2 | adhesive_bead_03.png | NG：太细 1 段、位置偏移 1 段 |
| 3 | adhesive_bead_04.png | **OK** |
| 4 | adhesive_bead_05.png | NG：缺胶 1 段、太细 1 段、太粗 1 段 |
| 5 | adhesive_bead_06.png | NG：缺胶 1 段 |
| 6 | adhesive_bead_07.png | NG：缺胶 1 段、位置偏移 3 段 |
| 7 | adhesive_bead_ref.png（无胶参考图） | NG：缺胶若干（无胶图判 NG 属**正常**） |

**口径说明**（换产品/上产线前注意）：
- `MinErrorLength=0`（演示口径，与探针真值表逐段一致）；**生产建议调回 5** 滤碎段；
- `TriggerMode=单次`（便于逐张检查）；产线连续运行改「连续」；
- 图像采集是**演示用**文件夹模式，接真相机时换 `Mode=相机` 并连对应驱动步骤；
- 第一次运行会惰性重建 planar 模型（实测 284~360ms），属正常。



---

## 维护约定

1. **只改主文档**。`胶路检测插件方案说明书.md` 是唯一事实来源；本 README 只做索引。
2. **实现后补 `docs/code-changes/` 记录**，并在本目录新增插件使用说明（入门层），参照卡尺/匹配的「入门层 + 维护层」分层。
3. **探针结论已回写主文档 §7.1（P1~P12，2026-10-04）**。此后**任何默认值/算法口径的改动都必须有新的实测依据**，不得凭推理回退——卡尺插件曾因几何约定靠推理而返工（见其维护约定第 2 条）。
4. **HALCON 新坑追加到主文档** §10.2 硬约束表或第十一章风险表，注明「现象 → 原因 → 修法」；探针里已记两条（`distance_cc` 的 max 语义、`gen_polygons_xld` 不可用）。
5. **端口名一经发布即是对外契约**，改名 = 断链；确需改名须同步 `FlowCanvasChecks` 断言（7.2 节断言 8 就是为此设的锁）。
6. **重建纪律**：改插件源码后必须重建插件工程；`Modules\` 不由宿主工程带出（红线③）。
7. **探针工程处置**：`tools\BeadProbe\` 不在 `Plugins\` 下、不进 .sln，不影响主构建；**断言已落地并跑通（2026-10-05，60 项全绿），探针工程可删**——删除前确认 `BeadInspectHalcon.cs` 已含 `SegmentBeadByRefDiff` / `LongestPathThroughTree` / `Downsample` 三个移植方法（现已全部在位）。
