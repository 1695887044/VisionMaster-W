# HALCON 样例流程 ②：散焦 PCB 形状匹配（121 张）

> 日期：2026-10-26。第二条 HALCON 样例流程：用 `matching_defocused_pcb.hdev` 的 121 张散焦
> PCB 图，对比 HALCON `find_shape_model` 与 VM `Matching` 的形状匹配。
> 发现并修复一个**真实能力缺口**：VM 没有 HALCON `NumLevels=[0,-1]` 的第二个值（最低跟踪层）。

## 一、当前状态

- **HALCON 例程**：`examples\hdevelop\Applications\Position-Recognition-2D\matching_defocused_pcb.hdev`
- **图片**：`examples\images\pcb_focus\pcb_focus_telecentric_001..121.png`
  （仓库 `Image\images\pcb_focus` 有同一份 121 张）
- **模型图**：`pcb_focus_telecentric_060.png`
- **ROI**：`gen_rectangle1(50,120,400,600)`（中心 225,360；L1=240、L2=175）
- **HALCON 模型参数**：`create_shape_model(..., 'auto', -0.39, 0.78, 'auto', 'auto',
  'use_polarity', 'auto', 'auto')`；实际 levels=6、angleStart=-0.391703、
  angleExtent=0.783406、angleStep=0.003413、minContrast=6
- **HALCON 搜索**：`find_shape_model(..., -0.39, 0.78, 0.7, 1, 0.5, 'least_squares',
  [0,-1], 0.9, ...)`
- **修复后结果**：121/121 张匹配数一致；位置最大差 **2.444e-4 px**、角度差 **0°**、分数差 **0**。
- **落盘流程**：`解决方案\视觉样例对拍\视觉样例对拍.vms` 新增 `形状匹配_散焦PCB`，
  步骤 `读取PCB散焦图像 → 形状模板匹配`（全中文命名）。

## 二、已做

### 2.1 发现的真实能力缺口

VM 原先的 `FindShapeModel` / `FindScaledShapeModel` 只传一个整数 `numLevels`：

```csharp
HOperatorSet.FindShapeModel(..., numLevels, greediness, ...);
```

HALCON 例程用的是 `NumLevels=[0,-1]`。在同一张散焦图 J=001 上实测：

```text
HALCON [0,-1] -> 1 个匹配 score=0.8795
HALCON [6,-1] -> 1 个匹配 score=0.8795
HALCON [6,1]  -> 0
HALCON [6,2]  -> 0
HALCON 0/6/5/4/3/2/1（单值） -> 全部 0
VM 单值 1~6 -> 全部 0
```

也就是说，HALCON `NumLevels` 的**第二个值 `-1`**（不跟踪到最低层）是散焦图能匹配的关键；
VM 没有这个参数，导致 121 张里只找到 25 张（对焦清楚的），另外 96 张全部漏检。

### 2.2 修复

- `MatchingTemplateEntry` 新增运行期搜索参数 `TrackingLevel`：
  - 默认 **1** = 跟踪到最细层（旧版单值 `NumLevels` 的行为，精度最高）；
  - **-1** = 不跟踪到最低层（HALCON 例程 `[0,-1]` 的用法，散焦/高噪声更稳）。
  - 只影响搜索，不影响模型创建；**不进 `LearnedSignature`**，改它不用重学。
- `MatchingPlugin` 的 `FindShapeModel` / `FindScaledShapeModel` 改为：
  ```csharp
  new HTuple(new[] { numLevels, entry.TrackingLevel })
  ```
  NCC 分支不受影响（`find_ncc_model` 没有这个第二值）。
- `MatchingView.xaml` 增加“跟踪层”数值框（-1~10），ToolTip 写明两种取值的适用场景。
- Help 更新：`03-ports-and-parameters.md`（运行期参数表）与 `04-匹配口径.md`（金字塔一节）。

### 2.3 121 张对拍结果

修复后 VM 使用 `NumLevels=6, TrackingLevel=-1`：

```text
J=001 HALCON=(225.206,362.393,-0.302°,0.8795) VM=(225.206,362.393,-0.302°,0.8795) diff=(2.4e-4px,0,0)
J=002 HALCON=(225.312,362.377,-0.280°,0.8817) VM=(225.312,362.377,-0.280°,0.8817) diff=(2.4e-4px,0,0)
J=060 HALCON=(225.000,360.000,-0°,0.9949)   VM=(225.000,360.000,-0°,0.9949)   diff=(2.4e-4px,0,0)
J=121 HALCON=(225.601,357.841,-0.199°,0.8804) VM=(225.601,357.841,-0.199°,0.8804) diff=(2.4e-4px,0,0)
```

统计：匹配数不一致 **0 张**；位置最大差 **2.444e-4 px**、角度最大差 **0°**、分数最大差 **0**。

### 2.4 落盘流程与回归

`解决方案\视觉样例对拍\视觉样例对拍.vms` 现在有两条流程（每条都是
`For 遍历样例目录 → 图像采集 → 算法 → 结果显示`，效果图发布到图像窗口 1）：

```text
视觉样例对拍
├─ 模板匹配_NCC散焦图（For 11 次）
│  └─ 循环体
│     ├─ 读取散焦图像（文件夹：<方案根>\Image\smd，FileIndex←For.Index）
│     ├─ NCC模板匹配
│     └─ 显示检测结果（SrcImage ← NCC模板匹配.MeasureImage）
└─ 形状匹配_散焦PCB（For 121 次）
   └─ 循环体
      ├─ 读取PCB散焦图像（文件夹：<方案根>\Image\pcb_focus，FileIndex←For.Index）
      ├─ 形状模板匹配（TrackingLevel=-1）
      └─ 显示检测结果（SrcImage ← 形状模板匹配.MeasureImage）
```

`<方案根>` = `D:\C#\VM\解决方案\视觉样例对拍`；采集步骤是文件夹模式，
`FileIndex` 接 `For.Index`，目录用方案内绝对路径（`AcquireFromFolder` 不解析相对目录）。

`FlowCanvasChecks\MatchingChecks.cs` 的 `RunHalconSampleSolution()` 现在对两条流程都做：
`SolutionService.LoadAsync` → `FlowCompiler.Compile` → `MatchingPlugin.Initialize(step)` →
喂 `pcb_focus_telecentric_001.png` → 必须得到 `(225.206,362.393,-0.302°,0.8795)`。

`FlowCanvasChecks` 全量（2026-10-26 收尾）：**3752 通过 / 0 失败**；`HelpProbe` exit=0。

## 三、未做

- 只做了 NCC 和形状两条样例；Caliper/Fit/Blob/Code/Calibration 还没做。
- 没有直接执行 `.hdev` 原文件（含 `dev_*`/`stop`/交互）；本轮复刻核心算子链。
- 没有覆盖 `TrackingLevel` 的 2~10 等其它取值；只验证了默认 1 与 -1。
- 没有用真实产线图；121 张散焦图来自 HALCON 自带样例。
- 5 张卡尺图按用户要求未做。

## 四、下一步

1. 按同一模板补 Caliper（`1D-Measuring\gen_measure_rectangle2.hdev`）、
   Fit（`XLD\Features\fit_line/circle`）、Blob（`Regions`）、Code（`Identification`）、
   Calibration（`Calibration\Multi-View\calibrate_cameras_monocular.hdev`）。
2. 把 HALCON 参考值抽成 `reference/<流程名>.json`，runner 通用化。
3. 关掉 `VisionMaster.exe` 后跑完整 `tools\run_gates.ps1`。

## 五、踩过的坑

- **HALCON `find_shape_model` 的 `NumLevels` 可以是两元组**：`[0,-1]` 的第二个值 -1 表示
  “不跟踪到最低层”。单值 1~6 在散焦图上全部漏检；这不是参数没调好，是**能力缺口**。
- **`[0,-1]` 与 `[6,-1]` 结果相同**：0 = 用模型实际层数（本次 6）；所以 VM 只要
  `NumLevels=6 + TrackingLevel=-1` 就能对齐。
- **默认值必须保持 1**：旧版单值 `NumLevels` 等价于跟踪到最细层；把默认改成 -1 会改变所有
  存量流程的精度/行为。新参数默认 1，样例显式设 -1。
- **`TrackingLevel` 不进 `LearnedSignature`**：它是运行期搜索参数，改它不应触发“需重新学习”。
- **手工生成 `.vms` 前必须清 `Image.Value`**：`OnConfirm` 会把当前输入图写进 `InputValues`，
  序列化 `HImage` 会走 `serialize_image` 并报 `#4056`；清成 null 后由 `LinkedSources` 提供。
- **ROI 映射**：`gen_rectangle1(50,120,400,600)` 中心 `(225,360)`；Phi=0 时
  `Rect=[225,360,0,240,175]`（L1 沿列轴=水平，L2 沿行轴=垂直）。

## 六、证据

| 项 | 结果 |
| --- | --- |
| HALCON vs VM（121 张散焦 PCB） | 匹配数 121/121；位置 2.444e-4px；角度 0°；分数 0 |
| 修复前 | 仅 25/121（对焦清楚的），96 张漏检 |
| 关键参数 | `NumLevels=6`、`TrackingLevel=-1`、`AngleStart/Extent=-0.39/0.78`、`MinScore=0.7` |
| 落盘方案 | `解决方案\视觉样例对拍\视觉样例对拍.vms`（两条流程） |
| FlowCanvasChecks | 3724 通过 / 0 失败 |
| HelpProbe | exit=0 |

日志：
`artifacts\logs\20261026\halcon-shape-pcb-compare6.log`（121 张 + 层数诊断 + 方案生成）、
`artifacts\logs\20261026\flowcanvas-halcon-samples3.log`（3724 条断言）。

源码/资产：
`Plugins\Plugin.Matching\MatchingTemplateEntry.cs`（`TrackingLevel`）、
`Plugins\Plugin.Matching\MatchingPlugin.cs`（`[numLevels, TrackingLevel]`）、
`Plugins\Plugin.Matching\MatchingView.xaml`（“跟踪层”）、
`FlowCanvasChecks\MatchingChecks.cs`（两条样例流程断言）、
`解决方案\视觉样例对拍\视觉样例对拍.vms`。
