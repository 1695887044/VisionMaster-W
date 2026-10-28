# HALCON 样例流程 ①：NCC 散焦图（模板匹配）

> 日期：2026-10-26。目的：用 HALCON 自带例程和图片，在 VM 里做成**中文命名的流程**并落盘，
> 对比 HALCON 与 VM 的输出，验证 `Matching` 插件的 NCC 算法是否一致。
> 这是“每个样例一个流程、同一解决方案”的第一条；5 张卡尺图按用户要求不做。

## 一、当前状态

- **HALCON 例程**：`examples\hdevelop\Matching\Correlation-Based\find_ncc_model_defocused.hdev`
- **图片**：`examples\images\smd\smd_on_chip_01..11.png`（仓库 `Image\images\smd\` 有同一份，
  SHA-256 与 HALCON 安装目录逐字节相同）
- **HALCON 参数**：ROI `(175,156)-(440,460)`；`create_ncc_model(ImageReduced, 'auto', 0, 0, 'auto', 'use_polarity')`；
  `find_ncc_model(..., 0, 0, 0.5, 1, 0.5, 'true', 0, ...)`
- **HALCON 模型实际参数**：levels=6、angleStart=0、angleExtent=0、angleStep=0、metric=use_polarity
- **VM 参数**：同一 ROI（中心 307.5,308；L1=152、L2=132.5）、`NumLevels=6`、
  角度 0/0、`UsePolarity=true`、`MinScore=0.5`、`MaxMatches=1`、`MaxOverlap=0.5`
- **结果**：11 张散焦图的 `Row / Column / Angle / Score` 与 HALCON **逐位一致（差 0）**。
- **落盘流程**：`解决方案\视觉样例对拍\视觉样例对拍.vms`，第一条流程 `模板匹配_NCC散焦图`，
  步骤 `读取散焦图像 → NCC模板匹配`（全中文命名）。
  2026-10-26 已追加第二条 `形状匹配_散焦PCB`（121 张散焦 PCB，`TrackingLevel=-1`），
  见 `2026-10-26-HALCON样例流程_散焦PCB形状匹配.md`。

## 二、已做

### 2.1 对拍结果

HALCON 参考与 VM 输出（角度已转度）：

| 图 | HALCON (Row, Col, Angle, Score) | VM (Row, Col, Angle, Score) | 差 |
| --- | --- | --- | --- |
| 01 | (307.952, 308.395, 0°, 0.7824) | (307.952, 308.395, 0°, 0.7824) | 0 |
| 02 | (307.025, 307.810, 0°, 0.7930) | (307.025, 307.810, 0°, 0.7930) | 0 |
| 03 | (307.288, 308.032, 0°, 0.8207) | (307.288, 308.032, 0°, 0.8207) | 0 |
| 04 | (307.464, 308.113, 0°, 0.8669) | (307.464, 308.113, 0°, 0.8669) | 0 |
| 05 | (307.500, 308.000, 0°, 1.0000) | (307.500, 308.000, 0°, 1.0000) | 0 |
| 06 | (307.518, 307.839, 0°, 0.8977) | (307.518, 307.839, 0°, 0.8977) | 0 |
| 07 | (307.338, 307.531, 0°, 0.8404) | (307.338, 307.531, 0°, 0.8404) | 0 |
| 08 | (307.248, 307.805, 0°, 0.8192) | (307.248, 307.805, 0°, 0.8192) | 0 |
| 09 | (307.330, 308.001, 0°, 0.8083) | (307.330, 308.001, 0°, 0.8083) | 0 |
| 10 | (307.239, 308.006, 0°, 0.8008) | (307.239, 308.006, 0°, 0.8008) | 0 |
| 11 | (307.025, 307.810, 0°, 0.7930) | (307.025, 307.810, 0°, 0.7930) | 0 |

判定：位置差 0px、角度差 0°、分数差 0；`MatchCount=1`。

### 2.2 落盘的 VM 流程

`解决方案\视觉样例对拍\视觉样例对拍.vms`（工厂批量口径：每条流程用 `For` 遍历整个样例目录，
采集步骤是**文件夹模式**，`FileIndex ← For.Index`；目录用方案内绝对路径，
因为 `AcquireFromFolder` 直接 `Directory.Exists(相对路径)`，宿主不解析目录）：

```text
视觉样例对拍
├─ 模板匹配_NCC散焦图（For 11 次）
│  └─ 循环体
│     ├─ 读取散焦图像（文件夹：<方案根>\Image\smd，FileIndex←For.Index）
│     ├─ NCC模板匹配（NCC 模型来自 smd_on_chip_05 的 ROI）
│     │    Image ← 读取散焦图像.Image
│     └─ 显示检测结果（SrcImage ← NCC模板匹配.MeasureImage；r1..r4 ← Score/Row/Column/Angle）
├─ 形状匹配_散焦PCB（For 121 次）
│  └─ 循环体
│     ├─ 读取PCB散焦图像（文件夹：<方案根>\Image\pcb_focus）
│     ├─ 形状模板匹配（NumLevels=6、TrackingLevel=-1）
│     └─ 显示检测结果（SrcImage ← 形状模板匹配.MeasureImage）
├─ Blob缺陷检测_标签字符（For 10 次）
│  └─ 循环体
│     ├─ 读取标签图像（文件夹：<方案根>\Image\label）
│     ├─ Blob缺陷检测（动态阈值 15×15 / 1.01 / 40，面积 100~400）
│     └─ 显示检测结果（SrcImage ← Blob缺陷检测.DefectImage）
├─ 码读取_Code128（For 3 次）
│  └─ 循环体
│     ├─ 读取条码图像（文件夹：<方案根>\Image\barcode_code128）
│     ├─ Code128读码（Symbology=Code 128、TimeoutMs=500）
│     └─ 显示检测结果（SrcImage ← 采集原图；OverlayContours ← SymbolContours；OverlayLabels ← Codes）
└─ 二维码读取_QR（For 9 次）
   └─ 循环体
      ├─ 读取二维码图像（文件夹：<方案根>\Image\qrcode）
      ├─ QR码读取（Symbology=QR Code、Polarity=any、TimeoutMs=500）
      └─ 显示检测结果（SrcImage ← 采集原图；OverlayContours ← SymbolContours；OverlayLabels ← Codes）
```

`<方案根>` = `D:\C#\VM\解决方案\视觉样例对拍`。每圈 `For.Index` 从 0 递增，
文件夹模式按索引取一张图，跑完整个目录；`结果显示` 每圈发布一次到图像窗口 1。

图片只放在方案 `Image\` 内，`.vms` 里是相对路径 + `ImageAssetId`；
整包搬移后资产仍按「新方案根 + RelativePath」解析，不再依赖盘符绝对路径。

- 步骤 `PluginTypeName` 与生产一致：
  - `Plugin.ImageAcquisition.ImageAcquisitionPlugin, Plugin.ImageAcquisition, Version=1.0.0.0, ...`
  - `Plugin.Matching.MatchingPlugin, Plugin.Matching, Version=1.0.0.0, ...`
- 连线 `Kind=StepPort`、`TargetPortName=Image`、`DisplayAddress=读取散焦图像.Image`。
- 模板库载荷在 `TemplateLibraryJson`（含 `Model` base64 与 `NccOffsetRow/Col=0`）。
- 方案名/流程名/步骤名全部中文；5 张卡尺图未做。

### 2.3 永久回归

`FlowCanvasChecks\MatchingChecks.cs` 新增 `RunHalconSampleSolution()`：

- 生产 `SolutionService.LoadAsync` 打开 `.vms`；
- 找到 `模板匹配_NCC散焦图` 与采集/算法/结果显示三个步骤；
- `FlowCompiler` 编译通过（端口名/连线类型）；
- 直接以该步骤数据初始化 `MatchingPlugin`，喂 `smd_on_chip_01.png`，结果必须
  `(307.952, 308.395, 0°, 0.7824)`。

`FlowCanvasChecks` 全量：**3715 通过 / 0 失败**。
日志：`artifacts\logs\20261026\flowcanvas-halcon-sample.log`。

## 三、未做

- 只做了第一条样例；Matching 的 Shape-Based、Caliper、Fit、Blob、Code、Calibration 等还没做。
- 没有直接执行 `.hdev` 原文件：例程含 `dev_*`/`stop`/交互显示；本轮按“同图 + 同 ROI + 同模型参数”
  复刻核心算子链，HALCON 参考值由同参数 C# 探针产出。
- 没有覆盖 `AngleExtent != 0`、缩放、多实例、遮挡等 NCC 变体。
- 没有把样例流程的 runner 做成通用“读 reference JSON → 逐流程比对”工具；目前是
  `FlowCanvasChecks` 里针对第一条的断言。

## 四、下一步

1. 按同一模板补第 2 条：`Matching\Shape-Based\find_shape_model_clutter.hdev`（形状匹配）。
2. 再补 Caliper（`1D-Measuring\gen_measure_rectangle2.hdev`）、Fit（`XLD\Features\fit_line/circle`）、
   Blob（`Regions` 的 threshold/connection/select_shape）、Code（`Identification`）、
   Calibration（`Calibration\Multi-View\calibrate_cameras_monocular.hdev`）。
3. 把“HALCON 参考值”抽成 `reference/<流程名>.json`，runner 通用化，避免每条都写一段断言。
4. 关掉 `VisionMaster.exe` 后跑完整 `tools\run_gates.ps1`。

## 五、踩过的坑

- **HALCON 例程不能直接无头跑**：`dev_open_window`/`dev_display`/`stop`/`disp_continue_message`
  会卡住或依赖 GUI；要复刻核心算子链，不要 `hdevelop -run` 原文件。
- **模型参数必须对齐**：例程 `create_ncc_model` 用 `'auto'` 层数；VM 的 `NumLevels` 是整数。
  先用 `get_ncc_model_params` 读出 HALCON 实际 levels（本次 6），再设 VM 的 `NumLevels=6`；
  否则 Score/位置会差。
- **ROI 参数口径**：`gen_rectangle1(175,156,440,460)` 的中心是 `(307.5,308)`；
  `MatchingTemplateEntry.Rect` 是 `[row,col,phi,L1,L2]`，Phi=0 时 L1 沿列轴（水平）=
  `(460-156)/2=152`，L2 沿行轴（垂直）= `(440-175)/2=132.5`。L1/L2 写反会把 ROI 转 90°。
- **NCC 原点**：本次 ROI 是对称矩形、无涂抹，模型域重心=ROI 中心，`NccOffsetRow/Col=0`；
  若带非对称涂抹掩膜，必须用上一轮修复的偏移补偿，不能直接和 HALCON 的 COG 比。
- **`.vms` 的 `TemplateRect` 是旧字段且为空**：真正模型在 `TemplateLibraryJson`；
  手工生成 `.vms` 时不要只写 `TemplateRect`，否则插件加载不到模型。
- **`SolutionService.LoadAsync` 会自动补 Home/Main/End 三条骨架流程**：加载后流程列表会多三条，
  不是 `.vms` 写错；找目标流程要按 `FlowName`。
- **`FlowCanvasChecks` 上次运行的进程可能残留并锁 exe**：构建前 `Get-Process FlowCanvasChecks`，
  有就等它退；本轮残留 PID 43308 是测试宿主，已终止（不是用户的 `VisionMaster`）。

## 六、证据

| 项 | 结果 |
| --- | --- |
| HALCON vs VM（11 张 SMD 散焦图） | 位置 0px / 角度 0° / 分数 0 |
| VM NCC 模型 | levels=6、angle 0/0、use_polarity |
| 落盘方案 | `解决方案\视觉样例对拍\视觉样例对拍.vms` |
| 流程 | `模板匹配_NCC散焦图`（读取散焦图像 → NCC模板匹配） |
| FlowCanvasChecks | 3715 通过 / 0 失败 |
| audit_root | 8 files / 44 dirs OK |
| audit_artifacts | 39 个插件工程 OK |

日志：
`artifacts\logs\20261026\halcon-ncc-smd-compare3.log`（11 张对拍 + 方案生成）、
`artifacts\logs\20261026\flowcanvas-halcon-sample.log`（3715 条断言）。

源码/资产：
`解决方案\视觉样例对拍\视觉样例对拍.vms`、
`FlowCanvasChecks\MatchingChecks.cs`（`RunHalconSampleSolution`）、
`Image\images\smd\smd_on_chip_01..11.png`。
