# HALCON 样例流程 ③：标签暗区 Blob（动态阈值，10 张）

> 日期：2026-10-26。第三条 HALCON 样例流程：用 `var_threshold.hdev` 的 10 张 label 图，
> 对比 HALCON `var_threshold + connection + select_shape` 与 VM `BlobDetect` 的
> 动态阈值分割结果。10/10 张、每张 21 个目标，面积/中心/高度**逐位一致（差 0）**。

## 一、当前状态

- **HALCON 例程**：`examples\hdevelop\Segmentation\Threshold\var_threshold.hdev`
- **图片**：`examples\images\label\label_01..10.png`（仓库 `Image\images\label` 有同一份）
- **HALCON 参数**：
  - `var_threshold(Image, Region, 15, 15, 1.01, 40, 'dark')`
  - `connection(Region, ConnectedRegions)`
  - `select_shape(ConnectedRegions, SelectedRegions, ['height','area'], 'and', [20,100], [100,400])`
- **VM 参数**（`BlobDetectPlugin`）：
  - `ThresholdMode=Dynamic`、`DetectTarget=Dark`
  - `VarMaskWidth=15`、`VarMaskHeight=15`、`VarStdDevScale=1.01`、`VarAbsThreshold=40`
  - `MinArea=100`、`MaxBlobArea=400`、`SortMode=AreaDescending`
  - 其余形态学/合并/填孔全为 0/false（与例程的纯分割口径对齐）
- **结果**：10 张图各 21 个目标；`Area / Row / Col / Height` 与 HALCON 参考实现
  **逐位一致（最大差 0）**。
- **落盘流程**：`解决方案\视觉样例对拍\视觉样例对拍.vms` 新增 `Blob缺陷检测_标签字符`，
  步骤 `读取标签图像 → Blob缺陷检测`（全中文命名）。

## 二、已做

### 2.1 对拍结果

逐图输出（`HalconBlobCompare` 临时探针）：

```text
[label_01.png] 640x482 byte ch=1 ref=21 vm=21 vmAreaOnly=23 maxDiff(area=0,row=0,col=0,h=0) OK
[label_02.png] 640x482 byte ch=1 ref=21 vm=21 vmAreaOnly=22 maxDiff(area=0,row=0,col=0,h=0) OK
[label_03.png] 640x482 byte ch=1 ref=21 vm=21 vmAreaOnly=21 maxDiff(area=0,row=0,col=0,h=0) OK
...
[label_10.png] 640x482 byte ch=1 ref=21 vm=21 vmAreaOnly=21 maxDiff(area=0,row=0,col=0,h=0) OK
RESULT: ALL_OK
```

- `vmAreaOnly` 是只按面积筛（插件能力）的个数，`vm` 是再按高度 20~100 后筛的个数；
  两者差 0~2 个，说明**高度筛选是例程的额外约束，不是分割差异**。
- 面积/中心/高度全部由 HALCON 算子算出，插件与参考实现走的是两条代码路径
  （插件内部还有灰度化、参数归一化、排序、标注等步骤），因此"差 0"不是同源自证。

### 2.2 能力差异（不是 bug）

`BlobDetect` 的特征筛选只有 **面积 + 圆度 + 长宽比**，没有 `width` / `height` 参数。
本样例的 `select_shape(['height','area'])` 中高度一项只能在拿到 `DefectRows` 后自行后筛。
若现场确实需要"按宽高筛"，应作为新参数补进插件（本轮不做，先记录差异）。

### 2.3 落盘流程

```text
视觉样例对拍
├─ 模板匹配_NCC散焦图
├─ 形状匹配_散焦PCB
├─ 码读取_Code128
├─ 二维码读取_QR
└─ Blob缺陷检测_标签字符（For 10 次）
   └─ 循环体
      ├─ 读取标签图像（文件夹：<方案根>\Image\label，FileIndex←For.Index）
      ├─ Blob缺陷检测（动态阈值 15×15 / 1.01 / 40，面积 100~400）
      │    SrcImage ← 读取标签图像.Image
      └─ 显示检测结果（结果显示，DisplayViewIndex=1）
           SrcImage ← Blob缺陷检测.DefectImage
           r1..r3 ← DefectCount / MaxArea / TotalArea
```

- 工厂批量口径：`For` 遍历整个样例目录（10 张），采集步骤是文件夹模式，`FileIndex` 接 `For.Index`；
  目录用方案内绝对路径（`AcquireFromFolder` 直接 `Directory.Exists`，不解析相对目录）。
- 每条流程都补了「结果显示」步骤，效果图发布到主界面图像窗口 1（现场流程：
  采集 → 算法 → 结果显示），每圈发布一次。

- 判定参数在样例里放宽为 `MaxDefectCount=100`、`MaxSingleArea=1000`，
  目的只是让样例打开时显示 OK；**判定口径不参与算法对拍**。
  出厂值 `MaxDefectCount=0` 是"零容忍"，不是"不限"（见插件帮助 03 页）。
- 步骤配置全部来自 `BlobDetectPlugin` 的 `[StepConfig]` 属性快照，
  没有手写猜测的键名。

### 2.4 回归断言

`FlowCanvasChecks/BlobDetectChecks.cs` 新增 `RunHalconSampleFlow()`：

1. `SolutionService.LoadAsync` 打开 `解决方案\视觉样例对拍\视觉样例对拍.vms`；
2. 按 `FlowName` 找 `Blob缺陷检测_标签字符`，核对采集/算法/结果显示三个步骤与图片路径；
3. `FlowCompiler.Compile` 校验端口名/连线类型；
4. 真 `BlobDetectPlugin.Initialize(step)` + 喂图 + `Execute`；
5. 同一进程内用 HALCON 参考实现（同参数）现算真值，按面积/中心逐项比对；
6. 判据：`ref=21 && vm=21 && maxDiff==0`。

结果：`FlowCanvasChecks` **3731 通过 / 0 失败**（含前两条样例流程）。

## 三、未做

- 没有补 `width` / `height` 特征筛选参数（只记录差异）。
- 没有覆盖 `Auto`（Otsu）与 `Fixed` 两种阈值方式的 HALCON 对拍；
  本轮只验了 `Dynamic`（`var_threshold`）。
- 没有做 5 个卡尺的 HALCON 例程（用户明确要求跳过）。
- 完整 `run_gates.ps1`（含 `UIThemeSmokeTest` / `HelpProbe` / 根目录与产物审计）
  仍要等用户关闭 `VisionMaster.exe` 后跑。

## 四、下一步

- 继续按类别扩样例流程：优先 `CodeReader`（一维/二维码，HALCON `find_bar_code` /
  `find_data_code_2d`）与 `PreProcessing`（滤波器组）。
- 若要补 `width` / `height` 筛选，先按 R12 走架构评审：新增两个 `[StepConfig]` 参数 +
  在 `select_shape` 的 features 列表里加 `width`/`height`，并补插件帮助与断言。

## 五、踩过的坑

1. **`LinkedSources` 的键与 `TargetPortName` 不是同一个端口名**：
   - 键 = 下游步骤的**输入端口名**（本例 `SrcImage`）；
   - `TargetPortName` = 上游步骤的**输出端口名**（本例 `Image`）。
   写成 `"TargetPortName": "SrcImage"` 时 `FlowCompiler` 报
   `上游 '…读取标签图像' 不存在输出 'SrcImage'`，并连带报 `必填参数 'SrcImage' 未配置`。
   已把这条并入踩坑台账 C-17。
2. **`MaxDefectCount=0` 是零容忍，不是不限**：默认值下任何缺陷都判 NG，
   样例里看到 `缺陷个数 23 超过上限 0` 属预期；样例已放宽到 100/1000。
3. **`FlowCanvasChecks` 进程不退出会锁住下一次 gate 的原始日志**：
   第二次跑 `run_gates.ps1` 报 `raw-FlowCanvasChecks.err 正由另一进程使用`。
   先 `Get-Process FlowCanvasChecks | Stop-Process -Force`（只停自己的测试宿主，
   不动用户的 `VisionMaster.exe`）再重跑。
