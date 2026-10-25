# 代码 — 插件与算子文档

> `Plugins/` 下 **39 个工程**的文档都在这里。按**域**分 6 组，再按**插件**分子目录。
> 每个插件目录三件套：`README.md`（检索入口）+ 主文档（事实源）+ 方案说明书（可选）。
> 改动记录**不放这里**，一律进 [`../code-changes/`](../code-changes/INDEX.md)。

## 域索引

| 域 | 覆盖的插件工程 | 目录 |
|---|---|---|
| **采图** | `Plugin.ImageAcquisition`、`Plugin.Camera.Hikvision`、`Plugin.Camera.Network`、`Plugin.Camera.Sim`、`Plugin.PreProcessing`、`Plugin.ImageAssign`、`Plugin.ImageScript` | [`采图/`](采图/) |
| **视觉算子** | `Plugin.CreateRoi`、`Plugin.CaliperMeasure`、`Plugin.Matching`、`Plugin.ColorCheck`、`Plugin.ColorRegion`、`Plugin.BlobDetect`、`Plugin.BeadInspect`、`Plugin.CodeReader`、`Plugin.Ocr`、`Plugin.Yolo`、`Plugin.DlClassify`、`Plugin.DlAnomaly`、`Plugin.DlSegment`、`Plugin.GeometryFinder`、`Plugin.GeometryRelation` | [`视觉算子/`](视觉算子/) |
| **标定变换** | `Plugin.Calibration`、`Plugin.HandEye`、`Plugin.PoseTransform` | [`标定变换/`](标定变换/) |
| **运动通讯** | `Plugin.Motion.Steps`、`Plugin.Motion.Virtual`、`Plugin.Motion.ZMotion`、`Plugin.Motion.Sim` | [`运动通讯/`](运动通讯/) |
| **结果与报表** | `Plugin.ResultDisplay`、`Plugin.ResultUpload`、`Plugin.ExcelExport`、`Plugin.CSharpScript` | [`结果与报表/`](结果与报表/) |
| **产线仿真** | `Plugin.Utility`、`Plugin.RunFlow` | [`产线仿真/`](产线仿真/) |
| **未单独建目录** | `Plugin.Comm`、`Plugin.Database`、`Plugin.DataRecord`、`Plugin.ImageDiff` | 见下方 |

## 域说明

### 采图（`采图/`）

- `图像采集/` — 相机采集、HTTP 收图服务端、网络推送、相机设置对话框
- `图像预处理/` — 图像预处理算子集合（零改源码迁移）
- `图像赋值/` — 图像赋值插件
- `图像脚本/` — 图像脚本插件（HDev 驱动）

### 视觉算子（`视觉算子/`）

- `创建ROI/`、`卡尺测量/`、`模板匹配/` — 定位与测量三件套
- `颜色检查/` — 区域颜色检查 + 颜色序列检查（线序检测）
- `缺陷检测/` — Blob 缺陷检测
- `胶路检测/` — 胶路检测（方案阶段）
- `码读取/`、`OCR/` — 一维/二维码与文本识别
- `目标检测/` — YOLO 检测
- `深度学习/` — 分类 / 异常 / 分割（方案阶段）
- `几何工具/` — 几何查找与几何计算

### 标定变换（`标定变换/`）

- `标定插件/` — 标定板内参标定
- `坐标变换插件/` — 坐标变换

### 运动通讯（`运动通讯/`）

- `运动控制/` — 三工程分工（Steps / Virtual / ZMotion）、点位与软限位、调试面板
- 通讯相关事实源在 [`../通信协议矩阵/`](../通信协议矩阵/)，不在本目录

### 结果与报表（`结果与报表/`）

- `结果显示/`、`结果上报/` — 运行结果显示与数据上报
- `Excel报表/` — 带图报表导出
- `C#脚本/` — C# 脚本插件（白名单 MVP、编辑器智能提示）

### 产线仿真（`产线仿真/`）

- `产线仿真3D/` — 3D 产线仿真

## 未单独建目录的工程

| 工程 | 文档在哪 |
|---|---|
| `Plugin.Comm` | [`../通信协议矩阵/`](../通信协议矩阵/README.md) |
| `Plugin.Database` | [`../数据记录/`](../数据记录/)（数据库通讯方案见 [`../方案设计/`](../方案设计/README.md)） |
| `Plugin.DataRecord` | [`../数据记录/`](../数据记录/) |
| `Plugin.ImageDiff` | [`采图/图像预处理/`](../代码/采图/图像预处理) |

## 写作约定

1. **主文档是事实源**：`README.md` 只做目录与链接，不抄内容。
2. **改动记录不落这里**：日期开头的 `.md` 会被 `tools/audit_docs.ps1` 报成孤儿——写进
   [`../code-changes/`](../code-changes/README.md)，本目录只留"当前该怎么用"。
3. **新增插件目录必须登记**：在 `tools/docs-layout.json` 的 `codeDirs`（子目录白名单）里加一行，
   否则审计判红。

## 检索建议

```powershell
# 找某个插件的主文档
findstr /s /i /m "<插件中文名>" docs\代码\*.md

# 找某次改动
findstr /s /i /m "<关键词>" docs\code-changes\*.md
```
