# UI 设计基线（Web 形式）

本目录存放**以 Web 形式表达的设计基线**，用于界面设计与确认。

## 定位（必读）

- 这里的 HTML/截图/原型是**设计媒介**，**不是交付物**：
  - 不进入任何工程构建，不参与打包，不部署；
  - 不作为"另一个客户端"维护；
  - 产品交付物只有四类：WPF 代码、令牌映射表、截图证据、差异清单（见 `AGENTS.md` §30.1）。
- 设计基线是**只读**的：确认后冻结版本；需要改设计时升版本（`v1` → `v2`），并同步更新已复刻的桌面端页面。

## 流程

```text
设计基线（Web 形式） → 令牌映射 → 桌面端复刻 → 截图验收
```

没有设计基线，不得直接改桌面端样式。

## 目录约定

```text
docs/UI设计基线/
  README.md                 本文件（总约定）
  <页面名>/
    README.md               版本号、确认日期、确认人、覆盖范围、已知偏差
    design.html             可点击设计基线（自包含，无外部依赖）
    evidence/               桌面端复刻后的对照截图（改前 / 改后 / 差异标注）
```

## 现有基线

| 页面 | 版本 | 覆盖范围 | 状态 |
| --- | --- | --- | --- |
| `process-panel/` | v2 | `ProcessView.xaml`（步骤树 + 底部统计） | 已复刻，证据 `process-panel/evidence/after-render-F.png` |
| `global-variables/` | v3.2 | `VisionMaster\Views\DialogViews\GlobalVariableView.xaml`（变量管理：来源导航 + 变量明细）+ `ArrayEditorView.xaml`（数组编辑弹窗，v3 专项优化）+ `DeviceVariableExportView.xaml` / `DeviceVariableImportView.xaml`（单设备变量表批量导入 / 导出，v3.2 增量）；左栏二级分组树 / 「分组」列 / 管理分组覆盖层（v3.1 增量） | 已复刻（v1 2026-10-20 / v2 2026-10-21 / v3 2026-10-21 / v3.1 2026-10-21 / v3.2 2026-10-21）；设计证据 `global-variables/evidence/design-v1-*.png`、`design-v2-*.png`、`design-v3-*.png`；WPF 证据 `after-render-v1-*.png`、`after-render-v2-*.png`、`after-render-v3-*.png`；v3.1 / v3.2 增量见下方两行 |
| `global-variables/`（v3.1 显示分组） | v3.1 | 左栏二级分组树 + 表格「分组」列 + 管理分组覆盖层；只做归类与筛选，不影响采集 / 轮询 / 写入；扫描组编辑入口收敛到通讯设置 | 已复刻（2026-10-21，实现驱动）；详见 `global-variables/README.md` §「v3.1 显示分组」；WPF 证据 `global-variables/evidence/after-render-v1-group-editor.png` |
| `global-variables/`（v3.2 批量导入 / 导出） | v3.2 | `DeviceVariableExportView.xaml` / `DeviceVariableImportView.xaml`：单设备网络变量表 xlsx 批量导出（全部 / 当前显示分组）与导入（按变量名匹配、差异预览、事务应用）；入口仅网络来源 | 已复刻（2026-10-21，先出 Web 设计稿 `variable-batch-io-design.html`）；详见 `global-variables/README.md` §「v3.2 批量导入 / 导出」；证据 `global-variables/evidence/after-render-v1-export.png`、`after-render-v1-import.png` |
| `variable-binding/` | v1 | `VisionMaster\Views\DialogViews\VariableBindingView.xaml`（变量绑定：输入端口 / 数据来源 / 绑定设置三栏） | 已复刻（2026-10-20）；设计证据 `variable-binding/evidence/design-v1-*.png`；WPF 证据 `variable-binding/evidence/after-render-v1-*.png`；验证 `UIThemeSmokeTest --variable-binding` 全部通过 |
| `condition-editor/` | v2 | `VisionMaster\Views\DialogViews\ConditionEditorView.xaml`（双击逻辑算子弹出的条件窗口：If/While 条件、分支匹配、等待条件、计次循环四模式；1120×760 + 960 最小档） | 已复刻（2026-10-20），设计证据 `condition-editor/evidence/design-v2-*.png`；待用户真机确认 |
| `solution-list/` | v1 | `VisionMaster\Views\DialogViews\SolutionListView.xaml`（方案列表：清单表格 + 右侧动作栏 + 默认启动状态条；960×640 / 900×560） | 已复刻（2026-10-20）；设计证据 `solution-list/evidence/design-v1-*.png`；WPF 证据 `after-render-v1-*.png`；待用户真机确认 |
| `io-health/` | v1 | ⚠ **已失效**：原覆盖 `VisionMaster\Views\DialogViews\IoHealthView.xaml`，该视图**已不存在**（IO 域随 IO 接入层下线；`IoHealth` / `SystemHealth` 枚举成员与旧转发分支已删除，系统健康只经硬件管理打开 `SystemHealthView`） | 待归档，不参与任何复刻 |
| `app-shell/` | v2 | `Shell.xaml` 及三场景工作区（调试 / 运行 / 组态 + 自由布局）、单列检查器、可收起控制台、运行条、运行驾驶舱、命令面板；v1 归档在 `design-v1.html` | 待用户确认 |
| `image-acquisition/` | v2 | `Plugin.ImageAcquisition\ImageAcquisitionView.xaml`（配置窗口内容区 + 960×680 宿主外壳；v1 归档在 `design-v1.html`） | 已复刻，证据 `image-acquisition/evidence/after-render-v2-*.png`；待用户确认 |
| `auto-port-config/` | v1 | `VisionMaster\Views\DialogViews\AutoPortConfigView.xaml`（无自定义视图插件的框架兜底参数面板：步骤卡 + 输入端口清单 + 空态/执行态） | 已复刻（2026-10-20）；设计证据 `auto-port-config/evidence/design-v1-*.png`；WPF 证据 `wpf-after-render-v1-*.png`；待用户确认 |
| `yolo/` | v1 | `Plugin.Yolo\YoloView.xaml`（YOLO 目标检测配置：图像输入 / 模型 / 检测参数 / 高级参数 / 试算 + 只读预览） | 已复刻，证据 `_batch-evidence/Plugin.Yolo-YoloPlugin.png`；待用户确认 |
| `dl-classify/` | v1 | `Plugin.DlClassify\ClassifyView.xaml`（深度学习分类配置：图像输入 / ONNX 模型 / 缩放模式与 TopK / 试算 + 只读预览） | 已复刻，证据 `dl-classify/evidence/initial-render-920x520.png`；待用户确认 |
| `hand-eye/` | v1 | `Plugin.HandEye\HandEyeView.xaml`（**拟建**，插件当前无视图）：装法分段 / 成对数据（端口绑定 + CSV 路径）/ 质量门槛（旋转与平移残差上限 + 组数门）/ 只读结果区（4×4 矩阵 + 三枚残差 KPI + 红黄两档失败态） | **仅设计基线，尚未复刻**；四态可点（默认 / 空态 / 失败 / 窄档 640）；待用户确认 §12 五项交互决策 |
| `variable-assignment/` | v1 | `Plugin.Utility\VariableAssignmentView.xaml`（变量赋值配置） | 已复刻，证据 `variable-assignment/evidence/`；待用户确认 |
| `code-reader/` | v1 | `Plugin.CodeReader\CodeReaderView.xaml`（读码配置：图像输入 / 码制 / 参数 / 试算） | 已复刻，证据 `code-reader/evidence/`；待用户确认 |
| `excel-export/` | v1 | `Plugin.ExcelExport\ExcelExportView.xaml`（报表导出配置） | 已复刻，证据 `excel-export/evidence/`；待用户确认 |
| `image-script/` | v1 | `Plugin.ImageScript\ImageScriptView.xaml`（图像脚本配置） | 已复刻，证据 `image-script/evidence/`；待用户确认 |
| `csharp-script/` | v1 | `Plugin.CSharpScript\CSharpScriptView.xaml`（C# 脚本配置） | 已复刻，证据 `csharp-script/evidence/`；待用户确认 |
| `result-display/` | v1 | `Plugin.ResultDisplay\ResultDisplayView.xaml`（结果显示/烘焙配置） | 已复刻，证据 `result-display/evidence/`；待用户确认 |
| `data-record/` | v1 | `Plugin.DataRecord\DataRecordView.xaml`（数据记录配置） | 已复刻，证据 `data-record/evidence/`；待用户确认 |
| `result-upload/` | v1 | `Plugin.ResultUpload\ResultUploadView.xaml`（结果上传配置） | 已复刻，证据 `result-upload/evidence/`；待用户确认 |
| `color-region/` | v1 | `Plugin.ColorRegion\RegionColorView.xaml`（颜色区域配置） | 已复刻，证据 `color-region/evidence/`；待用户确认 |
| `ocr/` | v1 | `Plugin.Ocr\OcrView.xaml`（OCR 配置） | 已复刻，证据 `ocr/evidence/`；待用户确认 |
| `create-roi/` | v1 | `Plugin.CreateRoi\CreateRoiView.xaml`（ROI 创建配置） | 已复刻，证据 `create-roi/evidence/`；待用户确认 |
| `color-check/` | v1 | `Plugin.ColorCheck\ColorCheckView.xaml`（颜色序列检查配置） | 已复刻，证据 `color-check/evidence/`；待用户确认 |
| `pose-transform/` | v1 | `Plugin.PoseTransform\PoseTransformView.xaml`（位姿变换配置） | 已复刻，证据 `pose-transform/evidence/`；待用户确认 |
| `pre-processing/` | v1 | `Plugin.PreProcessing\PreProcessingView.xaml`（图像预处理配置） | 已复刻，证据 `pre-processing/evidence/`；待用户确认 |
| `caliper-measure/` | v1 | `Plugin.CaliperMeasure\CaliperMeasureView.xaml`（卡尺测量配置） | 已复刻，证据 `caliper-measure/evidence/`；待用户确认 |
| `matching/` | v1 | `Plugin.Matching\MatchingView.xaml`（模板匹配配置） | 已复刻，证据 `matching/evidence/`；待用户确认 |
| `calibration/` | v1 | `Plugin.Calibration\CalibrationView.xaml`（标定配置） | 已复刻，证据 `calibration/evidence/`；待用户确认 |
| `blob-detect/` | v1 | `Plugin.BlobDetect\BlobDetectView.xaml`（斑点检测配置） | 已复刻，证据 `blob-detect/evidence/`；待用户确认 |
| `bead-inspect/` | v1 | `Plugin.BeadInspect\BeadInspectView.xaml`（胶路检测配置） | 已复刻，证据 `bead-inspect/evidence/`；待用户确认 |
| `log-console/` | v1 | `VisionMaster\Views\LogView.xaml`（过滤工具条 + 日志区）+ `UI\Controls\Themes\LogConsole.xaml`（控件皮肤）；等级筹码、错误行整行底、无结果/空态、行选中 | **已复刻（2026-10-22）**；两文件 `hex=0`；日志冒烟全绿；渲染证据 `docs/UI样式参考/logview.png`、`logview_narrow.png` |
| `watch-panel/` | v1 | `VisionMaster\Views\MonitorView.xaml`（添加监视工具条 + 分组监视列表 + 底部计数）；联想下拉分组/空态、方向徽标、失效行 | **已复刻（2026-10-22）**；`hex=0`；新增 `MissingCount` / `Name` / `KindLabel` |
| `flow-list/` | v2 | `VisionMaster\Views\FlowListView.xaml`（流程胶囊条 + 底部统计 + 右键菜单）；v1 因 380×260 与宿主 340×112 冲突作废，v2 改单行紧凑式 | **已复刻（2026-10-22）**；按 340×112 单行 22px 胶囊；`hex=0` |
| `run-cockpit/` | v1 | `VisionMaster\Views\RunCockpitView.xaml`（运行场景深色驾驶舱：顶条 56 + 图像区 + KPI 条 96）；运行中 / 待接入 / 故障三场景 | **部分复刻（2026-10-22）**：顶条指标位 + KPI 语义色条已落地；故障态/结果章/帧信息缺数据源，见该页 §6 |
| `剩余视图设计稿-2026-10-22/` | v1 | **33 个剩余视图的汇总设计稿**（单文件 `design.html`，左侧目录分 4 组）：SCADA 编辑工作台 5 / SCADA 数据与弹窗 8 / SCADA 选择器与运动板卡 10 / 设置弹窗与零散 10 | 设计稿已出（2026-10-22），**待用户逐页确认**；未复刻。分片源在 `parts/`，写作规范 `_SPEC.md` |
| `logic-operators/` | v1 | 通用逻辑门参考库：41 个算子 / 6 族、真值表、门形与 WPF 映射；**安全回路备用，不是本期流程画布基线** | 参考库，待未来安全回路使用 |
| `flow-logic/` | v1 | 项目真实算子版流程栏 + 画布：逻辑运算 6 种操作、数值比较 6 种操作、If/分支匹配/While/For/并行分组/等待/Break/Continue/Return；42 节点复杂项目、逻辑容器重点改造、检查器、状态矩阵、WPF 复刻映射 | 待用户确认 |
| `property-grid/` | v1 | `UI\Controls\CustomControl\PropertyGrid\`（框架级公共控件：`PropertyGridBase` + `FlatPropertyGrid` + `CardPropertyGrid` + 生成器/处理器管线）与 `UI\Controls\Themes\PropertyGrid.xaml`；含表格式 / 卡片式两种形态与默认 / 校验失败 / 权限受限 / 无可见属性四态 | **未复刻**；待用户确认 |
| `message-dialog/` | v1 | UI 库公共组件 `MessageDialog`（消息 / 确认弹窗）：信息 / 成功 / 警示 / 危险四种语义、单 / 双按钮（三按钮组合见设计稿，API 延后）、结构化详情区（标签 + 数量 chip + 等宽路径 + 复制）、键盘与焦点契约；覆盖现场恢复 / 信息 / 成功 / 警示 / 危险五场景与 480 / 560 两档宽度 | **已复刻（2026-10-20）**；设计证据 `message-dialog/evidence/design-v1-*.png`；WPF 证据 `message-dialog/evidence/after-render-v1-*.png`；待用户真机确认 |
| `scada-confirm-pause/` | v1 | SCADA P0 交互：事件钩子「执行前确认」、`PauseFlow/ResumeFlow`、条件动作「仅在上一条成功时执行」、自动页暂停/恢复模板与动作编辑器 | **WPF 复刻完成（2026-10-21）**；设计证据 `scada-confirm-pause/evidence/design-v1-*.png`；WPF 证据 `after-01-editor.png`、`after-02-dialog-warning.png`、`after-03-dialog-danger.png`、`after-04-editor-danger.png`；ScadaChecks 2286 / FlowCanvasChecks 3055 / UIThemeSmokeTest 86 全绿；待用户确认 |
| `scada-controls/` | v1 | `Scada.Controls`（27 个内置 TypeKey）+ `Scada.Controls.Charts`（趋势曲线）图元控件；深色运行画面 + 浅色编辑上下文、五态 / 状态矩阵 / 令牌映射；含 9 个分类页签与 16 项原候选 + 7 项补充候选（6 项已落地） | **Phase 1+2 已复刻**（Tank/KpiBoard + SelectorSwitch/TextField/ComboBox/AckButton + 现有控件修复）；证据 `scada-controls/evidence/after-phase1-*.png`、`after-phase2-*.png`；其余待确认 |
| `scada-controls-batch2/` | **v2（复核版）** | SCADA 推荐控件第二批：ComboBox 只读显示（替代 StateText）、运行窗口顶栏页面菜单（替代 PageMenu）、软键盘、弹窗 / 子画面、滑块 `Hmi.Slider`（**唯一新增 TypeKey**）、AlarmBanner 行内逐条确认（替代 RecordList）、DataGrid 历史源模式（替代 HistoryTable）、旋转 / 缩放动画；沿用 `scada-controls` v1 双上下文与 `Hmi*` 令牌，含 v1→v2 复核对照与 10 项待裁定 | **P0-1 / P0-2 / P0-3 / P1 弹窗 + 每画布多基座 + 软键盘 / P2 模拟量滑块已实施**（2026-10-22）；设计文件 `scada-controls-batch2/design.html`；实施记录 `docs/code-changes/2026-10-21-SCADA返回上一页（NavigateBack动作与页栈通知）.md`、`docs/code-changes/2026-10-21-SCADA下拉框只读显示（ComboBox Presentation-Text）.md`、`docs/code-changes/2026-10-21-SCADA顶栏页面菜单（运行窗口导航与页面级角色）.md`、`docs/code-changes/2026-10-21-SCADA多基座与弹窗（特殊画面）.md`、`docs/code-changes/2026-10-22-SCADA软键盘（数字字符共享设施）.md`、`docs/code-changes/2026-10-22-SCADA模拟量滑块（Hmi.Slider）.md`；设计证据 `scada-controls-batch2/evidence/design-v2-*.png`（6 张）；评审记录 `scada-controls-batch2/评审记录-2026-10-21.md`（6 角色 pass with changes）；v1 截图归档在 `evidence/v1/` |
| `industry-templates/` | v1 | 三套 1440×900 深色运行画面：SCADA 监控 / 自动化设备运控 / 视觉行业；公共壳 A~E、信息层级、图元映射与复用约束 | 设计稿待用户确认；证据 `industry-templates/evidence/template-0*.png` |
| `industry-templates/scada-suite/` | v1 | SCADA 完整套件：7 个界面（总览 / 监控 / 手动 / 自动 / 报警条 / 报警日志 / 操作记录）+ 跳转关系、信息层级、图元映射 | 设计稿待用户确认；证据 `scada-suite/evidence/0*.png` |

> 本期流程栏 + 画布以 `flow-logic/` 为准；`logic-operators/` 是通用门电路参考库，留给未来的安全回路使用，不进入本期复刻。

## 插件改造 SOP 与清单

- [**审阅清单-第1批-2026-10-21.md**](审阅清单-第1批-2026-10-21.md)：第 1 批 4 份设计稿（控制台 / 监视 / 流程胶囊条 / 运行驾驶舱）的**待用户逐页确认清单**，含 6 项需用户拍板的事项与复刻时必须新增的接口。
- [未出设计稿视图盘点-2026-10-21.md](未出设计稿视图盘点-2026-10-21.md)：全仓视图覆盖度盘点（哪些已出稿、哪些待出稿、按批分组）。
- [插件自定义视图Web化改造SOP.md](插件自定义视图Web化改造SOP.md)：适用范围、前置条件、逐步操作、涉及文件、验证、回滚、注意事项、单插件记录模板、DoD。
- [插件自定义视图改造清单与实施计划.md](插件自定义视图改造清单与实施计划.md)：22 个自定义视图插件盘点（截至 2026-10-20 全部完成 Web 化，含 ImageAcquisition 参考实现）、分批计划、9 个不改造项目清单。
- [插件自定义视图Web化改造总览.md](插件自定义视图Web化改造总览.md)：22 个插件的变更清单、审核状态、验证结果与统一遗留问题。
- [插件遗留问题核查与修复结果.md](插件遗留问题核查与修复结果.md)：91 项遗留的逐条核查结论、已修复清单与未修复原因（FUNC/TEST/真机/发布）。

## 对应关系

- 颜色：CSS 变量 ↔ `UI/Controls/Themes/Colors.xaml` 语义令牌；
- 间距/圆角/字号/阴影：按 `AGENTS.md` §30 的刻度一一对应；
- 组件：HTML 控件 ↔ UI 库里的 `Dialog*` / Fluent 样式（映射表随每个页面记录）。

## 允许偏差

字体度量、滚动条宽度、阴影层数、动效曲线允许与 Web 基线存在细微差异，但必须逐条写进该页面的 `README.md`，并由用户裁定是否接受。
